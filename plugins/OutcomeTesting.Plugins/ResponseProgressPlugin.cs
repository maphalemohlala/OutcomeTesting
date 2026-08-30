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
                new ColumnSet("al_reviewstatus", "al_startedon"));

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
        }
    }
}
