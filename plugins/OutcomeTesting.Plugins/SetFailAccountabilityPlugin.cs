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
        private const int CommandSetFailAccountability = 120910788;

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
            var fqAdviser = GetBool(context, InFqAdviser);
            var fqParaplanner = GetBool(context, InFqParaplanner);
            var aqAdviser = GetBool(context, InAqAdviser);
            var aqParaplanner = GetBool(context, InAqParaplanner);

            PermissionHelpers.EnsureAppPermission(systemService, context, "page.cases", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandSetFailAccountability);
            if (existingAudit != null)
            {
                SetResponse(context, "Recorded", existingAudit.Id);
                return;
            }

            var outcome = userService.Retrieve(OutcomeEntity, targetId, new ColumnSet("al_initialoutcome", "al_finaloutcome"));

            // Accountability describes a fail. Recording it against a Pass would put a
            // name in a Trail Light column that AD-039 only ever fills for a fail.
            var effective = outcome.GetAttributeValue<OptionSetValue>("al_finaloutcome")
                ?? outcome.GetAttributeValue<OptionSetValue>("al_initialoutcome");
            if (effective == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "This case has no outcome recorded, so there is no fail to attribute.");
            }
            if (!OutcomeRules.RequiresRemediation(effective.Value))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "This case passed, so there is no fail to attribute.");
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

        private static bool GetBool(IPluginExecutionContext context, string name)
        {
            object value;
            if (context.InputParameters.TryGetValue(name, out value) && value is bool)
            {
                return (bool)value;
            }

            throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + name + " is required.");
        }

        private static void SetResponse(IPluginExecutionContext context, string status, Guid auditId)
        {
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
