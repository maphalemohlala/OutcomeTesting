using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Portal answering without the browser creating anything. Registered as a synchronous
    /// post-operation step on Update of <c>al_reviewinstance</c>, filtered to
    /// <c>al_answerrequest</c>.
    ///
    /// The page used to POST al_response with two @odata.bind lookups. Power Pages refuses
    /// that association with 90040106 and no table permission value changes it — five were
    /// tried on 2026-09-08 and the message never moved. A PATCH of an allowlisted column on
    /// a review the caller is assigned to does get through, which is what this is built on,
    /// and it is the same mechanic SubmitRequestPlugin already uses for submission.
    ///
    /// Authorization is deliberately not checked against the caller, for the reason given in
    /// SubmitRequestPlugin: a Power Pages write arrives as the site's application user. The
    /// boundary is the Contact-scoped "Review Instance - assigned to me" permission, so
    /// reaching this plug-in already means the platform allowed a write on a review assigned
    /// to the signed-in contact (AD-047, AD-053).
    ///
    /// Every rule about the answer itself stays in ResponseGuardPlugin, which fires on the
    /// Create or Update this issues.
    /// </summary>
    public class AnswerRequestPlugin : PluginBase
    {
        private const string ReviewEntity = "al_reviewinstance";
        private const string AnswerRequestAttr = "al_answerrequest";

        public AnswerRequestPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AnswerRequestPlugin))
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

            object target;
            if (!context.InputParameters.TryGetValue("Target", out target))
            {
                return;
            }

            var entity = target as Entity;
            if (entity == null || !string.Equals(entity.LogicalName, ReviewEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!entity.Contains(AnswerRequestAttr))
            {
                return;
            }

            var json = entity.GetAttributeValue<string>(AnswerRequestAttr);
            if (string.IsNullOrWhiteSpace(json))
            {
                // Clearing the column is not an answer.
                return;
            }

            var payload = AnswerRequest.Parse(json);
            AnswerWriter.Save(service, entity.Id, payload);
        }
    }
}
