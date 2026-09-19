using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Post-operation on al_response Create and Update. The first saved answer moves the
    /// review from Assigned to Review In Progress, which is the FR-010 lifecycle step the
    /// checker never performs explicitly (AD-053).
    ///
    /// This runs server-side because the portal holds no write permission on
    /// al_reviewinstance and must not be given one: a checker who could write the review
    /// row directly could also write al_submittedon.
    /// </summary>
    public class ResponseProgressPlugin : PluginBase
    {
        private const string ReviewEntity = "al_reviewinstance";

        public ResponseProgressPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ResponseProgressPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var service = localPluginContext.PluginUserService;

            if (!context.InputParameters.Contains("Target"))
            {
                return;
            }

            var target = context.InputParameters["Target"] as Entity;
            if (target == null || target.LogicalName != "al_response")
            {
                return;
            }

            // On Update the Target carries only changed columns, so the review link comes
            // from the pre-image registered by Register-ResponseGuard.ps1.
            var pre = context.PreEntityImages.Values.FirstOrDefault();
            var reviewRef = target.GetAttributeValue<EntityReference>("al_reviewinstanceid")
                ?? (pre == null ? null : pre.GetAttributeValue<EntityReference>("al_reviewinstanceid"));

            if (reviewRef == null)
            {
                return;
            }

            var review = service.Retrieve(
                ReviewEntity,
                reviewRef.Id,
                new ColumnSet("al_reviewstatus", "al_startedon", "al_outcomecaseid"));

            var status = review.GetAttributeValue<OptionSetValue>("al_reviewstatus");
            if (status == null || status.Value != ResponseRules.StatusAssigned)
            {
                return;
            }

            var update = new Entity(ReviewEntity, reviewRef.Id)
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusInProgress),
            };

            // Started is stamped once, on the transition, so a later edit never moves it.
            if (!review.Contains("al_startedon"))
            {
                update["al_startedon"] = DateTime.UtcNow;
            }

            service.Update(update);

            StampCheckDate(service, review);
        }

        /// <summary>
        /// Stamps the case's check date the moment checks actually start on it (project
        /// owner, 2026-09-19: "the check date should default to when someone starts checks
        /// on the case").
        ///
        /// Here rather than on assignment, because a case can sit assigned for days before
        /// anyone opens it, and the business reads this column as the day the work was
        /// done. This runs on the Assigned -> In progress transition, which is the first
        /// answer saved, so it fires exactly once per review and the first review to start
        /// is the one that dates the case.
        ///
        /// Only when the column is empty. It is a DEFAULT, not a derived value: the import
        /// may have carried one, and a checker may correct it afterwards, and neither should
        /// be overwritten by the next review on the same case starting.
        ///
        /// Date-only column (behavior 2), so the date is written without a time.
        /// </summary>
        public static void StampCheckDate(IOrganizationService service, Entity review)
        {
            if (service == null || review == null)
            {
                return;
            }

            var caseRef = review.GetAttributeValue<EntityReference>("al_outcomecaseid");
            if (caseRef == null)
            {
                return;
            }

            var outcomeCase = service.Retrieve(
                "al_outcomecase", caseRef.Id, new ColumnSet("al_checkdate"));

            if (outcomeCase != null
                && outcomeCase.GetAttributeValue<DateTime?>("al_checkdate").HasValue)
            {
                return;
            }

            service.Update(new Entity("al_outcomecase", caseRef.Id)
            {
                ["al_checkdate"] = DateTime.UtcNow.Date,
            });
        }
    }
}
