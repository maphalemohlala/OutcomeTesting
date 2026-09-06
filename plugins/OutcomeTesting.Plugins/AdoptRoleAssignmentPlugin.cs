using System;
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

        private const int CommandAssignUserRole = 120910773;
        private const int CommandSetRoleAssignmentActive = 120910788;

        public sealed class AdoptResult
        {
            public Guid? MappingId { get; set; }
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
            var systemService = localPluginContext.PluginUserService;

            var email = CommandHelpers.GetRequiredString(context, InUserEmail);
            var roleCode = CommandHelpers.GetRequiredString(context, InRoleCode);
            var decision = CommandHelpers.GetRequiredString(context, InDecision);

            var adopt = ParseDecision(decision);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var command = adopt ? CommandAssignUserRole : CommandSetRoleAssignmentActive;

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, command);
            if (existingAudit != null)
            {
                SetResponse(context, null, adopt, existingAudit.Id);
                return;
            }

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            var result = Apply(systemService, email, roleCode, adopt);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, command,
                (adopt ? "AdoptRoleAssignment " : "RevokeRoleAssignment ") + email,
                MappingEntity, result.MappingId ?? Guid.Empty, null,
                result.Details, idempotencyKey, context);

            SetResponse(context, result.MappingId, result.Adopted, auditId);
        }

        /// <summary>
        /// Converges both sources. Public and static so every starting state is testable
        /// without a plug-in context.
        /// </summary>
        public static AdoptResult Apply(
            IOrganizationService service, string email, string roleCode, bool adopt)
        {
            var trimmedEmail = (email ?? string.Empty).Trim();
            var trimmedRole = (roleCode ?? string.Empty).Trim();

            var webRole = WebRoleRegistry.FindByName(service, trimmedRole);
            if (webRole == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.NotFoundPrefix + "No web role is named " + trimmedRole + ".");
            }

            var contact = WebRoleRegistry.FindContactByEmail(service, trimmedEmail);
            if (contact == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "No contact has the work email " + trimmedEmail +
                    ", so this role cannot be reconciled for them.");
            }

            var mapping = FindMapping(service, trimmedEmail, trimmedRole);
            var details = (adopt ? "Adopt " : "Revoke ") + trimmedRole + " for " + trimmedEmail +
                ". Reconciled a role held in two places (AD-089).";

            if (adopt)
            {
                // Association first: it is what actually grants the role, and a mapping row
                // written against a role the person does not hold would be the same lie in
                // the other direction.
                AssignUserRolePlugin.AssociateWebRole(service, trimmedEmail, webRole.Id);

                if (mapping == null)
                {
                    var row = new Entity(MappingEntity);
                    row["al_useremail"] = trimmedEmail;
                    row["al_rolecode"] = trimmedRole;
                    row["al_approle"] = null;
                    var newId = service.Create(row);
                    return new AdoptResult { MappingId = newId, Adopted = true, Details = details };
                }

                if (!CommandHelpers.IsActive(mapping))
                {
                    CommandHelpers.SetState(service, MappingEntity, mapping.Id, true);
                }

                return new AdoptResult { MappingId = mapping.Id, Adopted = true, Details = details };
            }

            AssignUserRolePlugin.DisassociateWebRole(service, trimmedEmail, trimmedRole);

            if (mapping != null && CommandHelpers.IsActive(mapping))
            {
                CommandHelpers.SetState(service, MappingEntity, mapping.Id, false);
            }

            return new AdoptResult
            {
                MappingId = mapping == null ? (Guid?)null : mapping.Id,
                Adopted = false,
                Details = details,
            };
        }

        private static Entity FindMapping(IOrganizationService service, string email, string roleCode)
        {
            var query = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet("al_useremail", "al_rolecode", "statecode"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_useremail", ConditionOperator.Equal, email);
            query.Criteria.AddCondition("al_rolecode", ConditionOperator.Equal, roleCode);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count == 0 ? null : found[0];
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
            IPluginExecutionContext context, Guid? mappingId, bool adopted, Guid auditId)
        {
            context.OutputParameters[OutMappingId] = mappingId.HasValue ? mappingId.Value.ToString("D") : string.Empty;
            context.OutputParameters[OutAdopted] = adopted;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
