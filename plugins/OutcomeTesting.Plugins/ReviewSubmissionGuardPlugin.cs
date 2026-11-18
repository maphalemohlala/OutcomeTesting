using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// A submitted check stays submitted, and only a real submit submits it. Registered
    /// synchronous pre-operation on Update of <c>al_reviewinstance</c>, filtered to
    /// <c>al_reviewstatus</c>, <c>al_submittedon</c> and <c>al_pendingaccountability</c>,
    /// and on Create of <c>al_reviewinstance</c>, which is held to the same rule against an
    /// empty row.
    ///
    /// The app's Dataverse role writes review instances at Global depth, so a direct write
    /// could move a submitted check back to In progress, clear or move its submission date,
    /// stamp a date on an open check (after which every real submit was refused as "already
    /// submitted"), submit a check without its answers being checked, or park any person as
    /// accountable past every accountability rule. No command does any of that: the submit
    /// sets status and date together, once, as SYSTEM after its own checks (the portal's
    /// submit request runs the same command), and only the portal's request parks a choice.
    /// </summary>
    public class ReviewSubmissionGuardPlugin : PluginBase
    {
        private const string ReviewEntity = "al_reviewinstance";
        private const string StatusAttr = "al_reviewstatus";
        private const string SubmittedOnAttr = "al_submittedon";
        private const string PendingAccountabilityAttr = "al_pendingaccountability";

        public ReviewSubmissionGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ReviewSubmissionGuardPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            object raw;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            if (target == null
                || !string.Equals(target.LogicalName, ReviewEntity, StringComparison.OrdinalIgnoreCase)
                || (!target.Contains(StatusAttr) && !target.Contains(SubmittedOnAttr) && !target.Contains(PendingAccountabilityAttr)))
            {
                return;
            }

            var system = localPluginContext.OrgSvcFactory.CreateOrganizationService(null);
            if (WriteProvenance.IsSystemWrite(system, context))
            {
                return;
            }

            var portal = WriteProvenance.IsApplicationUser(system, context.InitiatingUserId);
            var parked = target.GetAttributeValue<string>(PendingAccountabilityAttr);
            if (!portal && !string.IsNullOrWhiteSpace(parked))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.UnauthorizedPrefix
                    + "Who carries this fail is recorded from the check's own page, which checks who may be named. It cannot be written here.");
            }

            if (!target.Contains(StatusAttr) && !target.Contains(SubmittedOnAttr))
            {
                return;
            }

            // A Create is checked against an empty row: a check created already submitted, or
            // with a date on it, skipped every rule the submit applies.
            var stored = string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
                ? new Entity(ReviewEntity)
                : system.Retrieve(ReviewEntity, target.Id, new ColumnSet(StatusAttr, SubmittedOnAttr));

            var refusal = Refusal(target, stored) ?? SubmitRefusal(target, stored, portal);
            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(refusal);
            }
        }

        /// <summary>Why this write would undo a submission, or null when it does not.</summary>
        public static string Refusal(Entity target, Entity stored)
        {
            var storedStatus = stored.GetAttributeValue<OptionSetValue>(StatusAttr);
            var wasSubmitted = storedStatus != null && storedStatus.Value == ResponseRules.StatusSubmitted;
            var newStatus = target.GetAttributeValue<OptionSetValue>(StatusAttr);
            var leavesSubmitted = wasSubmitted && target.Contains(StatusAttr)
                && (newStatus == null || newStatus.Value != ResponseRules.StatusSubmitted);

            var storedOn = stored.GetAttributeValue<DateTime?>(SubmittedOnAttr);
            var movesStamp = storedOn.HasValue && target.Contains(SubmittedOnAttr)
                && target.GetAttributeValue<DateTime?>(SubmittedOnAttr) != storedOn;

            if (!leavesSubmitted && !movesStamp)
            {
                return null;
            }

            return CommandHelpers.UnauthorizedPrefix
                + "This check has been submitted. A submitted check cannot be reopened or have its submission date changed.";
        }

        /// <summary>
        /// Why this write would submit an open check the way no real submit does, or null. A
        /// submit sets the status and the date together; a date alone on an open check, or a
        /// status alone, is refused, and so is the pair from anyone but the portal
        /// (<paramref name="trusted"/>). SYSTEM never reaches here: al_SubmitReview writes the
        /// pair as SYSTEM after checking the owner, the answers and the grading, so the owner
        /// writing it straight onto the table would skip all three.
        /// </summary>
        public static string SubmitRefusal(Entity target, Entity stored, bool trusted)
        {
            var storedStatus = stored.GetAttributeValue<OptionSetValue>(StatusAttr);
            if (storedStatus != null && storedStatus.Value == ResponseRules.StatusSubmitted)
            {
                return null;
            }

            var newStatus = target.GetAttributeValue<OptionSetValue>(StatusAttr);
            var submits = newStatus != null && newStatus.Value == ResponseRules.StatusSubmitted;
            var stamps = target.GetAttributeValue<DateTime?>(SubmittedOnAttr).HasValue;
            if (!submits && !stamps)
            {
                return null;
            }

            if (submits != stamps || !trusted)
            {
                return CommandHelpers.UnauthorizedPrefix
                    + "A check is submitted from its own page, which checks every answer first. It cannot be marked submitted here.";
            }

            return null;
        }
    }
}
