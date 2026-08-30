using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command RegradeCase (AD-003, OD-007/AD-031). Registered against the
    /// Custom API message <c>al_RegradeCase</c>. The T&amp;C Manager reopens, overrides or
    /// regrades a graded outcome by setting the final outcome with a mandatory reason. The
    /// initial outcome is never edited, so both survive (BR-007). Authorization is enforced
    /// by Dataverse: the outcome update runs as the initiating user, so a caller without the
    /// write-<c>al_outcome</c> privilege is refused by the platform (the T&amp;C Manager team
    /// is granted that privilege in the security configuration). The command also enforces
    /// optimistic concurrency and idempotency, and writes an immutable Audit Event
    /// (BR-012, NFR-AUD-01).
    /// </summary>
    public class RegradeCasePlugin : PluginBase
    {
        private const string InTargetId = "TargetId";
        private const string InFinalOutcome = "FinalOutcome";
        private const string InReason = "Reason";
        private const string InExpectedRowVersion = "ExpectedRowVersion";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutOutcomeId = "OutcomeId";
        private const string OutFinalOutcome = "FinalOutcome";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string OutcomeEntity = "al_outcome";
        private const string FinalOutcomeAttr = "al_finaloutcome";
        private const string InitialOutcomeAttr = "al_initialoutcome";

        private const int CommandRegradeCase = 120910758;

        public RegradeCasePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(RegradeCasePlugin))
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
            var reason = CommandHelpers.GetRequiredString(context, InReason);
            var finalOutcomeLabel = CommandHelpers.GetRequiredString(context, InFinalOutcome);
            var finalOutcomeValue = ParseOutcome(finalOutcomeLabel);
            var canonicalLabel = OutcomeLabel(finalOutcomeValue);
            var expectedRowVersion = CommandHelpers.GetOptionalString(context, InExpectedRowVersion);

            // Idempotency: a replay with the same key returns the original regrade (NFR-REL-01).
            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandRegradeCase);
            if (existingAudit != null)
            {
                var priorLabel = existingAudit.GetAttributeValue<string>("al_details");
                SetResponse(context, targetId.ToString("D"), string.IsNullOrEmpty(priorLabel) ? canonicalLabel : priorLabel, existingAudit.Id, false);
                return;
            }

            // Confirm the outcome exists (and the caller can read it) before writing.
            var outcome = userService.Retrieve(OutcomeEntity, targetId, new ColumnSet(InitialOutcomeAttr));

            // A regrade overrides a grade that was already given, so there has to BE one.
            // Without this, a final outcome can be written against an ungraded record: BR-007
            // is broken from the other side (a final with no initial to preserve against) and
            // the Audit Event claims an override that never happened.
            if (outcome.GetAttributeValue<OptionSetValue>(InitialOutcomeAttr) == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "This case has no initial outcome to regrade. Record the original outcome before overriding it.");
            }

            // Preserve the initial outcome (BR-007): only the final columns are written.
            var update = new Entity(OutcomeEntity, targetId)
            {
                [FinalOutcomeAttr] = new OptionSetValue(finalOutcomeValue),
                ["al_regradereason"] = reason,
                ["al_regradedon"] = DateTime.UtcNow,
                ["al_finalisedon"] = DateTime.UtcNow,
            };

            if (string.IsNullOrEmpty(expectedRowVersion))
            {
                userService.Update(update);
            }
            else
            {
                update.RowVersion = expectedRowVersion;
                try
                {
                    userService.Execute(new Microsoft.Xrm.Sdk.Messages.UpdateRequest
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
                            CommandHelpers.ConflictPrefix + "This outcome changed since you loaded it. Reload and try again.");
                    }

                    throw;
                }
            }

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService,
                CommandRegradeCase,
                "RegradeCase " + targetId.ToString("D"),
                OutcomeEntity,
                targetId,
                reason,
                canonicalLabel,
                idempotencyKey,
                context);

            SetResponse(context, targetId.ToString("D"), canonicalLabel, auditId, false);
        }

        private static int ParseOutcome(string label)
        {
            switch ((label ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "pass": return 120910710;
                case "pass with issues": return 120910711;
                case "insufficient evidence": return 120910712;
                case "potential harm": return 120910713;
                default:
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix + "FinalOutcome must be Pass, Pass with issues, Insufficient evidence or Potential harm.");
            }
        }

        private static string OutcomeLabel(int value)
        {
            switch (value)
            {
                case 120910710: return "Pass";
                case 120910711: return "Pass with issues";
                case 120910712: return "Insufficient evidence";
                case 120910713: return "Potential harm";
                default: return "Unknown";
            }
        }

        private static void SetResponse(IPluginExecutionContext context, string outcomeId, string finalOutcome, Guid auditEventId, bool conflict)
        {
            context.OutputParameters[OutOutcomeId] = outcomeId;
            context.OutputParameters[OutFinalOutcome] = finalOutcome;
            context.OutputParameters[OutAuditEventId] = auditEventId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
