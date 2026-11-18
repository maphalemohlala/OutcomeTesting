using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Custom API message <c>al_AmendOutcome</c> (requirements log row 39): amends the grades a
    /// check first gave a case - the File grade and the Advice grade - at any stage, with a
    /// reason, for appeals. Regrade only sets the final outcome; this changes the original.
    ///
    /// The grade only. The case stays where it is: a fail amended to a pass is not closed, and
    /// a pass amended to a fail is not reopened (owner, 2026-11-17). The grades before and after
    /// and the reason go to an audit event on the case, which the case's History lists.
    ///
    /// The Advice grade is <c>al_initialoutcome</c> on the case's newest outcome, the row the
    /// export reads. The File grade is the File Quality answer <see cref="FileQuality"/>
    /// resolves. A submitted answer is locked to everyone else; the response guard lets this
    /// SYSTEM write change its choice and nothing more, so the writes go through the SYSTEM
    /// service rather than the plug-in user.
    /// </summary>
    public class AmendOutcomePlugin : PluginBase
    {
        public const string Resource = "case.amendoutcome";
        public const int CommandAmendOutcome = 120910779;

        private const string InTargetId = "TargetId";
        private const string InFileGrade = "FileGrade";
        private const string InAdviceGrade = "AdviceGrade";
        private const string InReason = "Reason";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutStatus = "Status";
        private const string OutSummary = "Summary";
        private const string OutAuditEventId = "AuditEventId";

        private const string CaseEntity = "al_outcomecase";

        // No ValueTuple in the sandbox, so label/value pairs are dictionaries.
        private static readonly Dictionary<string, int> AdviceGrades = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Pass", OutcomeRules.OutcomePass },
            { "Pass with issues", OutcomeRules.OutcomePassWithIssues },
            { "Insufficient evidence", OutcomeRules.OutcomeInsufficient },
            { "Potential harm", OutcomeRules.OutcomePotentialHarm },
        };

        private static readonly Dictionary<string, int> FileGrades = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Pass", ResponseRules.ChoicePass },
            { "Fail", ResponseRules.ChoiceFail },
        };

        public AmendOutcomePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AmendOutcomePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var pluginUser = localPluginContext.PluginUserService;

            var caseId = CommandHelpers.ParseRequiredGuid(context, InTargetId);
            var fileGrade = CommandHelpers.GetOptionalString(context, InFileGrade);
            var adviceGrade = CommandHelpers.GetOptionalString(context, InAdviceGrade);
            var reason = CommandHelpers.GetRequiredString(context, InReason).Trim();
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(pluginUser, context, Resource, PermissionHelpers.AccessEdit);

            var existing = CommandHelpers.FindAuditByKey(pluginUser, idempotencyKey, CommandAmendOutcome);
            if (existing != null)
            {
                SetResponse(context, existing.GetAttributeValue<string>("al_details"), existing.Id);
                return;
            }

            var summary = Amend(
                localPluginContext.OrgSvcFactory.CreateOrganizationService(null), caseId, fileGrade, adviceGrade, reason);

            var auditId = CommandHelpers.WriteAuditEvent(
                pluginUser, CommandAmendOutcome, "AmendOutcome", CaseEntity, caseId,
                reason, summary, idempotencyKey, context);

            SetResponse(context, summary, auditId);
        }

        /// <summary>
        /// Writes the grades given (an empty one is left as it is) and returns the audit line,
        /// naming each grade before and after.
        /// </summary>
        public static string Amend(IOrganizationService service, Guid caseId, string fileGrade, string adviceGrade, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "Say why the outcome is being amended.");
            }

            var newFile = Parse(fileGrade, FileGrades, "The File grade must be Pass or Fail.");
            var newAdvice = Parse(adviceGrade, AdviceGrades,
                "The Advice grade must be Pass, Pass with issues, Insufficient evidence or Potential harm.");
            if (newFile == null && newAdvice == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "Choose a new File grade, a new Advice grade, or both.");
            }

            var found = service.RetrieveMultiple(new QueryExpression(CaseEntity)
            {
                ColumnSet = new ColumnSet("al_casereference"),
                Criteria = { Conditions = { new ConditionExpression("al_outcomecaseid", ConditionOperator.Equal, caseId) } },
            }).Entities.FirstOrDefault();
            if (found == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.NotFoundPrefix + "That case no longer exists. Refresh the page.");
            }

            var changes = new List<string>();
            var writes = new List<Entity>();

            if (newAdvice != null)
            {
                var outcome = FileQuality.RouteIsTaxOnly(service, caseId) ? null : NewestOutcome(service, caseId);
                var current = outcome == null ? null : outcome.GetAttributeValue<OptionSetValue>(Outcomes.InitialOutcomeAttr);
                if (current == null)
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix + "This case has no Advice grade to amend: no AQS check has graded it.");
                }

                if (current.Value != newAdvice.Value)
                {
                    changes.Add("Advice grade from " + Label(AdviceGrades, current.Value) + " to " + Label(AdviceGrades, newAdvice.Value));
                    writes.Add(new Entity(outcome.LogicalName, outcome.Id) { [Outcomes.InitialOutcomeAttr] = new OptionSetValue(newAdvice.Value) });
                }
            }

            if (newFile != null)
            {
                var answer = FileQuality.Resolve(service, caseId);
                var current = FileQuality.Choice(answer);
                if (current == null)
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix + "This case has no File grade to amend: the File Quality question has not been answered.");
                }

                if (current.Value != newFile.Value)
                {
                    changes.Add("File grade from " + Label(FileGrades, current.Value) + " to " + Label(FileGrades, newFile.Value));
                    writes.Add(new Entity(answer.LogicalName, answer.Id) { ["al_answerchoice"] = new OptionSetValue(newFile.Value) });
                }
            }

            if (changes.Count == 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "The case already has those grades, so there is nothing to amend.");
            }

            foreach (var write in writes)
            {
                service.Update(write);
            }

            return "Case " + (found.GetAttributeValue<string>("al_casereference") ?? string.Empty).Trim()
                + ": " + string.Join("; ", changes) + ".";
        }

        private static int? Parse(string label, Dictionary<string, int> grades, string refusal)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return null;
            }

            int value;
            if (!grades.TryGetValue(label.Trim(), out value))
            {
                throw new InvalidPluginExecutionException(CommandHelpers.ValidationPrefix + refusal);
            }

            return value;
        }

        private static string Label(Dictionary<string, int> grades, int value)
        {
            var match = grades.FirstOrDefault(pair => pair.Value == value);
            return match.Key ?? "an unrecognised grade";
        }

        private static Entity NewestOutcome(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression("al_outcome") { ColumnSet = new ColumnSet(Outcomes.InitialOutcomeAttr), TopCount = 1 };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
            query.AddOrder("createdon", OrderType.Descending);
            return service.RetrieveMultiple(query).Entities.FirstOrDefault();
        }

        private static void SetResponse(IPluginExecutionContext context, string summary, Guid auditId)
        {
            context.OutputParameters[OutStatus] = "Amended";
            context.OutputParameters[OutSummary] = summary ?? string.Empty;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
