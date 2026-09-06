using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command AdoptRoleAssignment (AD-003, AD-089). Registered against the
    /// Custom API message <c>al_AdoptRoleAssignment</c>.
    ///
    /// A role can be granted in two places: this app, which writes an audited mapping row
    /// AND the web role association, and Power Pages management, which writes the
    /// association alone. AD-089 settles what the second one means — it grants access, it
    /// is visible, and it is not authoritative until a person decides.
    ///
    /// Two decisions rather than four, because each CONVERGES both sources rather than
    /// patching a particular disagreement: Adopt makes both say granted, Revoke makes both
    /// say not granted. That covers a portal-only grant, a withdrawn mapping whose
    /// association survived, and an active mapping whose association was removed, without
    /// the caller having to say which one they are looking at.
    ///
    /// No new audit command value is minted: adopting reuses AssignUserRole and revoking
    /// reuses SetRoleAssignmentActive, per AD-076, with the details line recording that
    /// this reconciled a portal-side assignment.
    /// </summary>
    public class AdoptRoleAssignmentPlugin : PluginBase
    {
        private const string InUserEmail = "UserEmail";
        private const string InRoleCode = "RoleCode";
        private const string InDecision = "Decision";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutMappingId = "MappingId";
        private const string OutAdopted = "Adopted";
        private const string OutAuditEventId = "AuditEventId";

        private const string MappingEntity = "al_userrolemapping";

        // Shared with al_AssignUserRole (adopt) and al_SetRoleAssignmentActive (revoke)
        // rather than minting a new value (AD-076, global no-new-option-value constraint).
        // Consequence: CommandHelpers.FindAuditByKey's "a replay can only return the result
        // of the same command" guarantee is weaker for these two values specifically — an
        // idempotency key first used against al_AssignUserRole can be replayed by this
        // command's adopt path (and symmetrically for revoke/al_SetRoleAssignmentActive),
        // because both write audit events tagged with the same command number. Callers are
        // expected to generate keys that do not collide across commands; this is recorded
        // here rather than worked around, since minting a new value is out of scope.
        private const int CommandAssignUserRole = 120910773;
        private const int CommandSetRoleAssignmentActive = 120910788;

        public sealed class AdoptResult
        {
            public Guid? MappingId { get; set; }

            /// <summary>
            /// The contact behind the work email, always populated. Used to give the audit
            /// event a real target when there is no mapping row to point at (a revoke of a
            /// portal-only grant) rather than writing every such event against Guid.Empty.
            /// </summary>
            public Guid ContactId { get; set; }

            public bool Adopted { get; set; }
            public string Details { get; set; }
        }

        public AdoptRoleAssignmentPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AdoptRoleAssignmentPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService; // mapping row write: who decided
            var systemService = localPluginContext.PluginUserService;   // permission read, audit, cross-user lookups, association

            var email = CommandHelpers.GetRequiredString(context, InUserEmail).Trim();
            var roleCode = CommandHelpers.GetRequiredString(context, InRoleCode);
            var decision = CommandHelpers.GetRequiredString(context, InDecision);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            var adopt = ParseDecision(decision);
            var command = adopt ? CommandAssignUserRole : CommandSetRoleAssignmentActive;

            // Permission check before the idempotency lookup (matching AssignUserRolePlugin):
            // otherwise an unauthorised caller could probe whether a key exists and receive
            // a real audit id back for work they were never allowed to trigger.
            PermissionHelpers.EnsureAppPermission(
                systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, command);
            if (existingAudit != null)
            {
                // A replay returns the same result. MappingId only replays true when the
                // original event actually targeted a mapping row — a revoke of a
                // portal-only grant targets the contact instead (see AdoptResult.ContactId),
                // and that must still replay as "no mapping row", not as a contact id.
                var targetTable = existingAudit.GetAttributeValue<string>("al_targettable");
                var replayMappingId = string.Equals(targetTable, MappingEntity, StringComparison.OrdinalIgnoreCase)
                    ? existingAudit.GetAttributeValue<string>("al_targetid")
                    : string.Empty;
                SetResponse(context, replayMappingId, adopt, existingAudit.Id);
                return;
            }

            var result = Apply(userService, systemService, email, roleCode, adopt);

            var auditTargetTable = result.MappingId.HasValue ? MappingEntity : ContactRegistry.Entity;
            var auditTargetId = result.MappingId ?? result.ContactId;

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, command,
                (adopt ? "AdoptRoleAssignment " : "RevokeRoleAssignment ") + email,
                auditTargetTable, auditTargetId, null,
                result.Details, idempotencyKey, context);

            SetResponse(
                context,
                result.MappingId.HasValue ? result.MappingId.Value.ToString("D") : string.Empty,
                result.Adopted,
                auditId);
        }

        /// <summary>
        /// Converges both sources. Public and static so every starting state is testable
        /// without a plug-in context.
        ///
        /// Takes two services rather than one: role and contact lookups, the association
        /// itself, and revoke's cross-row read all run as <paramref name="systemService"/>
        /// (PluginUserService) so they see every row regardless of who is signed in, while
        /// the mapping row write runs as <paramref name="userService"/>
        /// (InitiatingUserService) so Dataverse privilege gates it and createdby/modifiedby
        /// name the administrator who actually decided — the one thing this command's
        /// audit trail exists to record (AD-089).
        /// </summary>
        public static AdoptResult Apply(
            IOrganizationService userService, IOrganizationService systemService,
            string email, string roleCode, bool adopt)
        {
            var trimmedEmail = (email ?? string.Empty).Trim();
            var trimmedRole = (roleCode ?? string.Empty).Trim();

            var webRole = WebRoleRegistry.FindByName(systemService, trimmedRole);
            if (webRole == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.NotFoundPrefix + "No web role is named " + trimmedRole + ".");
            }

            var contact = WebRoleRegistry.FindContactByEmail(systemService, trimmedEmail);
            if (contact == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "No contact has the work email " + trimmedEmail +
                    ", so this role cannot be reconciled for them.");
            }

            var details = (adopt ? "Adopt " : "Revoke ") + trimmedRole + " for " + trimmedEmail +
                ". Reconciled a role held in two places (AD-089).";

            if (adopt)
            {
                // AD-090: a web role carrying mspp_authenticatedusersrole is auto-granted to
                // every signed-in contact and is deliberately excluded from resolution
                // (Task 1's WebRoleRegistry.ExcludedFromResolution) — OD-033 found
                // Administrators carrying that flag in DEV. Adopting such a role here would
                // turn a grant the gate deliberately ignores into one it enforces, for
                // everyone. Revoke is exempt (below) so a bad row can still be cleaned up.
                if (WebRoleRegistry.ExcludedFromResolution(systemService, trimmedRole))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ValidationPrefix + "The role " + trimmedRole +
                        " is granted automatically to every signed-in user, so it cannot be " +
                        "adopted as an application role (AD-090).");
                }

                // Row first, then association: a failure between the two leaves a mapping
                // row with no association, which is visible and fixable from the role
                // detail screen. The reverse — an association nobody can see — is exactly
                // the state AD-089 exists to end, so it is never the intermediate state.
                //
                // Upserted on the same business code AssignUserRolePlugin.Upsert keys on
                // (al_userrolemappingcode, an alternate key per src/Entities/al_UserRoleMapping),
                // so an adopted row is exactly as reachable to a later al_AssignUserRole as
                // one that command wrote itself, rather than becoming an invisible second row.
                var code = "URM-" + trimmedEmail.ToLowerInvariant() + "-" + trimmedRole;
                var mapping = new Entity(MappingEntity)
                {
                    ["al_name"] = trimmedRole + " - " + trimmedEmail,
                    ["al_useremail"] = trimmedEmail,
                    ["al_rolecode"] = trimmedRole,
                    // Explicitly null: al_approle carries a schema default, so leaving it
                    // out writes a picklist role nobody asked for onto a code-based
                    // assignment, and anything reading the picklist would honour it
                    // (AD-044).
                    ["al_approle"] = null,
                    ["al_userrolemappingcode"] = code,
                    ["statecode"] = new OptionSetValue(0),
                    ["statuscode"] = new OptionSetValue(1),
                };
                var mappingId = AssignUserRolePlugin.Upsert(
                    userService, MappingEntity, "al_userrolemappingcode", code, mapping);

                AssignUserRolePlugin.AssociateWebRole(systemService, trimmedEmail, webRole.Id);

                return new AdoptResult
                {
                    MappingId = mappingId,
                    ContactId = contact.Id,
                    Adopted = true,
                    Details = details,
                };
            }

            AssignUserRolePlugin.DisassociateWebRole(systemService, trimmedEmail, trimmedRole);

            // Every row for the pair, not just one. Going forward, adopt's upsert-by-code
            // makes exactly one row reachable per (email, role), but a row created before
            // this command existed, or by any path that does not honour that code, is not
            // guaranteed unique. PermissionHelpers.GetMappedRoles honours ANY active row, so
            // deactivating only one of several would leave the person authorised regardless
            // of the association just removed — revoke would fail open.
            var mappings = FindAllMappings(systemService, trimmedEmail, trimmedRole);
            Guid? firstMappingId = null;
            foreach (var row in mappings)
            {
                if (firstMappingId == null)
                {
                    firstMappingId = row.Id;
                }

                if (CommandHelpers.IsActive(row))
                {
                    CommandHelpers.SetState(userService, MappingEntity, row.Id, false);
                }
            }

            return new AdoptResult
            {
                MappingId = firstMappingId,
                ContactId = contact.Id,
                Adopted = false,
                Details = details,
            };
        }

        /// <summary>
        /// Every al_userrolemapping row for an (email, role) pair, active or not. Unpaged
        /// TopCount would silently hide a second row and let it keep granting the role after
        /// revoke — see the fail-open comment at the revoke call site.
        /// </summary>
        private static List<Entity> FindAllMappings(IOrganizationService service, string email, string roleCode)
        {
            var query = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet("al_useremail", "al_rolecode", "statecode"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_useremail", ConditionOperator.Equal, email);
            query.Criteria.AddCondition("al_rolecode", ConditionOperator.Equal, roleCode);

            return CommandHelpers.RetrieveAll(service, query);
        }

        private static bool ParseDecision(string decision)
        {
            var value = (decision ?? string.Empty).Trim();
            if (value.Equals("Adopt", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.Equals("Revoke", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.ValidationPrefix + "Decision must be Adopt or Revoke.");
        }

        private static void SetResponse(
            IPluginExecutionContext context, string mappingId, bool adopted, Guid auditId)
        {
            context.OutputParameters[OutMappingId] = mappingId ?? string.Empty;
            context.OutputParameters[OutAdopted] = adopted;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
