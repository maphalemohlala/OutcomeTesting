using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command SignOffRemediation (AD-003). Registered against the Custom API
    /// message <c>al_SignOffRemediation</c>. The T&amp;C Manager validates a completed
    /// remediation action (BR-008, FR-023), recording an Approved or Rejected sign-off; a
    /// Rejected sign-off requires notes and returns the action to the adviser. Authorization
    /// is enforced by Dataverse: the domain writes run as the initiating user, so a caller
    /// without the create-<c>al_signoff</c> privilege is refused by the platform (the
    /// T&amp;C Manager team is granted that privilege in the security configuration). The
    /// command also enforces the transition guard, optimistic concurrency and idempotency,
    /// and writes an immutable Audit Event (BR-012, NFR-AUD-01).
    /// </summary>
    public class SignOffRemediationPlugin : PluginBase
    {
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
        private const int StatusInProgress = 120910601;
        private const int StatusCompleted = 120910602;

        private const string SignoffEntity = "al_signoff";
        private const string DecisionApproved = "Approved";
        private const string DecisionRejected = "Rejected";
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
            var userService = localPluginContext.InitiatingUserService; // caller privileges gate authorization
            var systemService = localPluginContext.PluginUserService;   // audit always writes

            var targetId = CommandHelpers.ParseRequiredGuid(context, InTargetId);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var decision = NormaliseDecision(CommandHelpers.GetRequiredString(context, InDecision));
            var notes = CommandHelpers.GetOptionalString(context, InNotes);
            var expectedRowVersion = CommandHelpers.GetOptionalString(context, InExpectedRowVersion);

            // BR-008: a rejected sign-off must return with a reason for the adviser.
            if (decision == DecisionRejected && string.IsNullOrWhiteSpace(notes))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "A rejected sign-off must record notes explaining the return.");
            }

            // Idempotency: a replay with the same key returns the original sign-off (NFR-REL-01).
            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey);
            if (existingAudit != null)
            {
                var priorSignoff = existingAudit.GetAttributeValue<string>("al_details");
                SetResponse(context, priorSignoff, decision, existingAudit.Id, false);
                return;
            }

            var action = userService.Retrieve(
                ActionEntity,
                targetId,
                new ColumnSet(ActionStatus, "al_outcomecaseid"));

            var status = action.GetAttributeValue<OptionSetValue>(ActionStatus);
            if (status == null || status.Value != StatusCompleted)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "Only a completed remediation action can be signed off.");
            }

            var caseRef = action.GetAttributeValue<EntityReference>("al_outcomecaseid");
            var decisionValue = decision == DecisionApproved ? DecisionApprovedValue : DecisionRejectedValue;

            var signoff = new Entity(SignoffEntity)
            {
                ["al_name"] = "SignOff " + targetId.ToString("D"),
                ["al_signoffcode"] = "SO-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                ["al_signoffdecision"] = new OptionSetValue(decisionValue),
                ["al_signedoffon"] = DateTime.UtcNow,
                ["al_remediationactionid"] = new EntityReference(ActionEntity, targetId),
            };
            if (caseRef != null)
            {
                signoff["al_outcomecaseid"] = caseRef;
            }

            if (!string.IsNullOrWhiteSpace(notes))
            {
                signoff["al_notes"] = notes;
            }

            // Runs as the caller: a user without create-al_signoff privilege is refused here.
            var signoffId = userService.Create(signoff);

            // A rejected sign-off reopens the action so the adviser reworks it (BR-008).
            if (decision == DecisionRejected)
            {
                ReopenAction(userService, targetId, expectedRowVersion);
            }

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService,
                CommandSignOffRemediation,
                "SignOffRemediation " + targetId.ToString("D"),
                ActionEntity,
                targetId,
                notes,
                signoffId.ToString("D"),
                idempotencyKey,
                context);

            SetResponse(context, signoffId.ToString("D"), decision, auditId, false);
        }

        private static void ReopenAction(IOrganizationService service, Guid targetId, string expectedRowVersion)
        {
            var update = new Entity(ActionEntity, targetId)
            {
                [ActionStatus] = new OptionSetValue(StatusInProgress),
            };

            if (string.IsNullOrEmpty(expectedRowVersion))
            {
                service.Update(update);
                return;
            }

            update.RowVersion = expectedRowVersion;
            try
            {
                service.Execute(new Microsoft.Xrm.Sdk.Messages.UpdateRequest
                {
                    Target = update,
                    ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
                });
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault> fault)
            {
                if (CommandHelpers.IsConcurrencyFault(fault))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ConflictPrefix + "This remediation action changed since you loaded it. Reload and try again.");
                }

                throw;
            }
        }

        private static string NormaliseDecision(string decision)
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
