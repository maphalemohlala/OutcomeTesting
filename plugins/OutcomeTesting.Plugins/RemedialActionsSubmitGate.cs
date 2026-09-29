using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The submit-time half of <see cref="RemedialActions"/>, split into its own file so the
    /// rest of the class - the shape the checker's remediation page and the letter renderer
    /// both read - does not pull in <see cref="Remediation"/> (and, through it,
    /// <see cref="NotificationEmitterPlugin"/>) wherever it is linked. The Registration tool
    /// links <c>RemedialActions.cs</c> for <see cref="RemedialActions.ActionAttr"/>,
    /// <see cref="RemedialActions.ActionPerformedAttr"/> and
    /// <see cref="RemedialActions.ActionPerformedLabel(int?)"/> only; it never calls the submit
    /// gate, so this file is deliberately left out of that link set.
    /// </summary>
    public static partial class RemedialActions
    {
        /// <summary>
        /// The submit gate: refuses a review that owes a remediation while any item it will
        /// raise has no remedial action. Called by SubmitReviewPlugin only where remediation is
        /// owed, including a Tax fail that defers its raising to the AQS submit (AD-184) -
        /// checked at the Tax submit, while the Tax checker can still fix it.
        /// </summary>
        public static void EnsureWritten(IOrganizationService service, Guid reviewId, DateTime asOf)
        {
            var refusal = Refusal(Remediation.NonPassItems(service, reviewId, asOf), Pending(service, reviewId));
            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
            }
        }
    }
}
