using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command SignOffRemediation (AD-003). Registered against the Custom API
    /// message <c>al_SignOffRemediation</c>. The T&amp;C Manager validates a completed
    /// remediation action (BR-008, FR-023), recording an Approved or Rejected sign-off; a
    /// Rejected sign-off requires notes and returns the action to the adviser.
    ///
    /// Authorization is the <c>command.signoff</c> rule (AD-143). This comment used to say it
    /// was "enforced by Dataverse: a caller without the create-al_signoff privilege is refused
    /// by the platform (the T&amp;C Manager team is granted that privilege in the security
    /// configuration)". That privilege refused nobody: <c>GrantSecurity</c> puts
    /// <c>al_signoff</c> in <c>writeForEveryone</c>, so both application roles hold create on
    /// it at Global depth. AD-031 gives sign-off to the T&amp;C Supervisor so the adviser who
    /// completed the work is not the person attesting to it, and until AD-143 only the
    /// portal half (<see cref="SignoffRequestPlugin"/>) actually enforced that.
    ///
    /// This command records the decision and nothing else. Creating the <c>al_signoff</c>
    /// row is what fires <see cref="SignoffGuardPlugin"/> (validation and stamping) and
    /// <see cref="SignoffProgressPlugin"/> (reopening the action on a rejection, moving the
    /// case, notifying the adviser) — the same two steps the portal's own create fires — so
    /// the consequences of a sign-off are written once, in one place, whichever door it came
    /// through. This command used to reopen the action itself as well, without the OD-018
    /// clock reset the progress plug-in applies, and to write a second Audit Event for the
    /// same decision. Both are gone: the progress plug-in skips its own audit when the create
    /// was raised by this command, so the caller's idempotency key is the one recorded.
    ///
    /// Optimistic concurrency is applied to the action the manager saw: the sign-off is
    /// refused when the action's row version has moved since they loaded it.
    /// </summary>
    public class SignOffRemediationPlugin : PluginBase
    {
        public const string MessageName = "al_SignOffRemediation";

        /// <summary>
        /// The permission rule this command enforces (AD-143). Public for the same reason as
        /// <see cref="RegradeCasePlugin.RegradeResource"/>: the client gates the sign-off
        /// button on this key, and the two tiers have to read the same rulebook.
        /// </summary>
        public const string SignOffResource = "command.signoff";

        private const string InTargetId = "TargetId";
        private const string InDecision = "Decision";
        private const string InNotes = "Notes";
        private const string InExpectedRowVersion = "ExpectedRowVersion";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutSignoffId = "SignoffId";
        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string ActionEntity = "al_remediationaction";
        private const string ActionStatus = "al_actionstatus";
        private const int StatusCompleted = Remediation.StatusCompleted;

        private const string SignoffEntity = "al_signoff";
        private const string SignoffAction = "al_remediationactionid";
        private const string SignoffDecision = "al_signoffdecision";
        public const string DecisionApproved = "Approved";
        public const string DecisionRejected = "Rejected";
        private const int DecisionApprovedValue = 120910720;
        private const int DecisionRejectedValue = 120910721;

        private const int CommandSignOffRemediation = 120910757;

        public SignOffRemediationPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SignOffRemediationPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService; // the create still runs as the caller
            var systemService = localPluginContext.PluginUserService;   // audit always writes

            // AD-143. "A caller without create-al_signoff privilege is refused" was the whole
            // of the authorization here, and that privilege does not discriminate: both
            // application roles hold create on al_signoff at Global depth, so any app user
            // could sign off any remediation by calling this API directly. AD-031 gives
            // sign-off to the T&C Supervisor so that the adviser who completed the work is
            // not the person attesting to it.
            //
            // SignoffRequestPlugin already enforces the same thing on the portal side, against
            // the contact's web roles, because a Power Pages write arrives as the site's
            // application user (AD-053). This closes the Code App route.
            PermissionHelpers.EnsureAppPermission(
                systemService, context, SignOffResource, PermissionHelpers.AccessEdit);

            var targetId = CommandHelpers.ParseRequiredGuid(context, InTargetId);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var decision = NormaliseDecision(CommandHelpers.GetRequiredString(context, InDecision));
            var notes = CommandHelpers.GetOptionalString(context, InNotes);
            var expectedRowVersion = CommandHelpers.GetOptionalString(context, InExpectedRowVersion);

            // Idempotency: a replay with the same key returns the original sign-off
            // (NFR-REL-01). Resolved from the sign-off row itself rather than from the audit
            // event's details text, so the replay reports the decision that was recorded.
            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandSignOffRemediation);
            if (existingAudit != null)
            {
                var prior = FindSignoff(systemService, targetId);
                SetResponse(
                    context,
                    prior == null ? string.Empty : prior.Id.ToString("D"),
                    prior == null ? decision : DescribeDecision(prior),
                    existingAudit.Id,
                    false);
                return;
            }

            var signoffId = CreateSignoff(userService, systemService, targetId, decision, notes, expectedRowVersion);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService,
                CommandSignOffRemediation,
                "SignOffRemediation " + targetId.ToString("D"),
                ActionEntity,
                targetId,
                notes,
                "Signed off through " + MessageName + ": " + decision + ". Sign-off " + signoffId.ToString("D"),
                idempotencyKey,
                context);

            SetResponse(context, signoffId.ToString("D"), decision, auditId, false);
        }

        /// <summary>
        /// Validates and records the decision. Public and static so the command's own
        /// footprint is testable without a plug-in context: exactly one create, no write to
        /// the action, and a refusal when the action moved under the caller.
        ///
        /// The preconditions here repeat <see cref="SignoffGuardPlugin"/> deliberately, so a
        /// caller is told why before a create is attempted rather than by a fault from
        /// inside it; the guard remains the rule for every path.
        /// </summary>
        public static Guid CreateSignoff(
            IOrganizationService userService,
            IOrganizationService systemService,
            Guid targetId,
            string decision,
            string notes,
            string expectedRowVersion)
        {
            // BR-008: a rejected sign-off must return with a reason for the adviser.
            if (decision == DecisionRejected && string.IsNullOrWhiteSpace(notes))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "A rejected sign-off must record notes explaining the return.");
            }

            var action = CommandHelpers.RetrieveOrNotFound(
                userService,
                ActionEntity,
                targetId,
                new ColumnSet(ActionStatus, "al_outcomecaseid"),
                "That remediation action no longer exists. Refresh the case and try again.");

            var status = action.GetAttributeValue<OptionSetValue>(ActionStatus);
            if (status == null || status.Value != StatusCompleted)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "Only a completed remediation action can be signed off.");
            }

            // APPROVED sign-offs only, which is the same question SignoffGuardPlugin asks
            // on the create this command is about to issue - and asked here through the very
            // same method, so the two cannot drift apart again.
            //
            // This used to refuse on ANY active sign-off, rejections included (F39), which
            // made every rejection terminal on the command path: the action reopens, the
            // adviser reworks it, CompleteRemediation returns the case to Awaiting Sign-off,
            // and the supervisor is then refused the decision that would close it. e4e4ff3
            // fixed exactly that on 2026-09-11 and fixed it in the guard, where a portal
            // create lands; nothing changed here, and this check runs first, so the command
            // went on ending the loop it exists to start.
            //
            // What the refusal was reaching for is still enforced: overriding an approval is
            // a privileged correction (AD-031), not something the ordinary command does.
            if (SignoffGuardPlugin.AlreadySettled(systemService, targetId))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "This remediation action has already been approved. Reopening an approved sign-off is a privileged correction (AD-031).");
            }

            // The version the manager attested to. The action is not written by this
            // command any more, so the check is against what was read rather than an
            // IfRowVersionMatches update; the sign-off is then created in the same
            // transaction. A null version from the platform is not a mismatch.
            if (!string.IsNullOrEmpty(expectedRowVersion)
                && !string.IsNullOrEmpty(action.RowVersion)
                && !string.Equals(action.RowVersion, expectedRowVersion, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ConflictPrefix + "This remediation action changed since you loaded it. Reload and try again.");
            }

            var signoff = new Entity(SignoffEntity)
            {
                [SignoffDecision] = new OptionSetValue(decision == DecisionApproved ? DecisionApprovedValue : DecisionRejectedValue),
                [SignoffAction] = new EntityReference(ActionEntity, targetId),
            };

            var caseRef = action.GetAttributeValue<EntityReference>("al_outcomecaseid");
            if (caseRef != null)
            {
                signoff["al_outcomecaseid"] = caseRef;
            }

            if (!string.IsNullOrWhiteSpace(notes))
            {
                signoff["al_notes"] = notes;
            }

            // Runs as the caller: a user without create-al_signoff privilege is refused here.
            // The name, code and timestamp are stamped by SignoffGuardPlugin on the way in,
            // exactly as they are for a portal create.
            return userService.Create(signoff);
        }

        /// <summary>
        /// The active sign-off already recorded on an action, or null. Read with the system
        /// service so the answer does not depend on the caller's own read privileges: a
        /// sign-off they cannot see is still a sign-off.
        /// </summary>
        public static Entity FindSignoff(IOrganizationService systemService, Guid actionId)
        {
            var query = new QueryExpression(SignoffEntity)
            {
                ColumnSet = new ColumnSet(SignoffDecision),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(SignoffAction, ConditionOperator.Equal, actionId);

            // Latest first. An action reworked after a rejection carries two decisions, so
            // "the sign-off on this action" stopped being one row - and with TopCount 1 and
            // no order the platform may return either. The replay above reports what was
            // recorded, and answering an approval with the rejection it superseded would be
            // worse than not answering at all. createdon breaks a tie and covers rows written
            // before al_signedoffon was stamped.
            query.AddOrder("al_signedoffon", OrderType.Descending);
            query.AddOrder("createdon", OrderType.Descending);

            var found = systemService.RetrieveMultiple(query).Entities;
            return found.Count > 0 ? found[0] : null;
        }

        private static string DescribeDecision(Entity signoff)
        {
            var decision = signoff.GetAttributeValue<OptionSetValue>(SignoffDecision);
            return decision != null && decision.Value == DecisionRejectedValue ? DecisionRejected : DecisionApproved;
        }

        public static string NormaliseDecision(string decision)
        {
            if (string.Equals(decision, DecisionApproved, StringComparison.OrdinalIgnoreCase))
            {
                return DecisionApproved;
            }

            if (string.Equals(decision, DecisionRejected, StringComparison.OrdinalIgnoreCase))
            {
                return DecisionRejected;
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix + "Decision must be Approved or Rejected.");
        }

        private static void SetResponse(IPluginExecutionContext context, string signoffId, string status, Guid auditEventId, bool conflict)
        {
            context.OutputParameters[OutSignoffId] = signoffId ?? string.Empty;
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditEventId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
