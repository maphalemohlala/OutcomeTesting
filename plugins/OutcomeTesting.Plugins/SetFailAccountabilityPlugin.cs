using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command SetFailAccountability (AD-003, OD-024). Records who is
    /// accountable for a File Quality or Advice Quality fail on an Outcome, which is the
    /// judgement AD-039's export columns 11-14 and 17-20 report and which nothing in the
    /// model captured before.
    ///
    /// Set after submission rather than during the review, because the Outcome does not
    /// exist until the review is submitted. al_GenerateExport refuses a non-pass Outcome
    /// that records no accountability, so the step cannot be silently skipped.
    /// </summary>
    public class SetFailAccountabilityPlugin : PluginBase
    {
        private const string InTargetId = "TargetId";
        private const string InFqAdviser = "FqAdviser";
        private const string InFqParaplanner = "FqParaplanner";
        private const string InAqAdviser = "AqAdviser";
        private const string InAqParaplanner = "AqParaplanner";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";

        private const string OutcomeEntity = "al_outcome";
        private const string CaseAttr = "al_outcomecaseid";
        // Its own value since 2026-09-05 (OD-032). It previously reused 120910788, which the
        // option set labels SetRoleAssignmentActive, so every row this command wrote was
        // labelled as a role change and CommandHelpers.FindAuditByKey could not keep the two
        // commands' idempotency keys apart. Rows written before the cut-over stay on
        // 120910788 and are immutable (NFR-AUD-01); they are identified by
        // al_name = "SetFailAccountability" with al_targettable = "al_outcome".
        private const int CommandSetFailAccountability = 120910792;

        public SetFailAccountabilityPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SetFailAccountabilityPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService;
            var systemService = localPluginContext.PluginUserService;

            var targetId = CommandHelpers.ParseRequiredGuid(context, InTargetId);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var fqAdviser = CommandHelpers.GetRequiredBool(context, InFqAdviser);
            var fqParaplanner = CommandHelpers.GetRequiredBool(context, InFqParaplanner);
            var aqAdviser = CommandHelpers.GetRequiredBool(context, InAqAdviser);
            var aqParaplanner = CommandHelpers.GetRequiredBool(context, InAqParaplanner);

            PermissionHelpers.EnsureAppPermission(systemService, context, "page.cases", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandSetFailAccountability);
            if (existingAudit != null)
            {
                SetResponse(context, "Recorded", existingAudit.Id);
                return;
            }

            var outcome = userService.Retrieve(
                OutcomeEntity, targetId,
                new ColumnSet(Outcomes.InitialOutcomeAttr, Outcomes.FinalOutcomeAttr, CaseAttr));

            var caseRef = outcome.GetAttributeValue<EntityReference>(CaseAttr);
            var fileQualityFailed = caseRef != null && FileQuality.FailedOn(userService, caseRef.Id);

            var refusal = RefusalFor(Outcomes.EffectiveOutcome(outcome), fileQualityFailed);
            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
            }

            userService.Update(new Entity(OutcomeEntity, targetId)
            {
                ["al_fqadviseraccountable"] = fqAdviser,
                ["al_fqparaplanneraccountable"] = fqParaplanner,
                ["al_aqadviseraccountable"] = aqAdviser,
                ["al_aqparaplanneraccountable"] = aqParaplanner,
            });

            var details = "FQ adviser " + fqAdviser + ", FQ paraplanner " + fqParaplanner
                + ", AQ adviser " + aqAdviser + ", AQ paraplanner " + aqParaplanner;

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandSetFailAccountability, "SetFailAccountability", OutcomeEntity, targetId,
                null, details, idempotencyKey, context);

            SetResponse(context, "Recorded", auditId);
        }

        /// <summary>
        /// Why there is no fail on this case to attribute, or null when there is one.
        ///
        /// Accountability describes a fail: recording it against a clean case would put a
        /// name in a Trail Light column that AD-039 only ever fills for a fail.
        ///
        /// A file quality fail counts on its own. The guard used to read the advice quality
        /// outcome alone, which refused every case that passed on advice and failed on the
        /// file - five of the seven closed cases in DEV, and exactly the population the
        /// export now names the paraplanner for. Refusing to override a pair the export had
        /// already derived is the wrong way round.
        ///
        /// Pure so the decision can be read off a test without a fake organisation service,
        /// as GenerateExportPlugin.IsAccountable is.
        /// </summary>
        public static string RefusalFor(int? effectiveOutcome, bool fileQualityFailed)
        {
            if (fileQualityFailed)
            {
                return null;
            }

            if (!effectiveOutcome.HasValue)
            {
                return "This case has no outcome recorded, so there is no fail to attribute.";
            }

            if (!OutcomeRules.RequiresRemediation(effectiveOutcome.Value))
            {
                return "This case passed, so there is no fail to attribute.";
            }

            return null;
        }

        private static void SetResponse(IPluginExecutionContext context, string status, Guid auditId)
        {
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
