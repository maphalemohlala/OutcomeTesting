using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// When a contact is created, reactivated, deactivated or given a new email, the open
    /// remediation actions on every case whose adviser email is theirs follow it (AD-228).
    /// Registered synchronous post-operation on contact Create, and Update filtered to
    /// emailaddress1 and statecode. The Update step carries a pre-image named
    /// <c>PreImage</c> (<see cref="PreImageName"/>) holding <c>emailaddress1</c>, so an email
    /// change follows the address the contact gave up as well as the one it took.
    ///
    /// Before this, an action raised for an adviser not yet onboarded stayed unassigned until
    /// somebody edited the case - which is how PROD case 256497798 waited on a contact whose
    /// name was misspelled. The email on the case already said who the adviser was; this lets
    /// the contact catch up with it.
    ///
    /// SYSTEM, as CaseAccessPlugin is: the bookkeeping is a consequence of the write, not the
    /// caller's edit, and onboarding runs as whoever onboards.
    /// </summary>
    public class AdviserContactPlugin : PluginBase
    {
        private const string CaseEntity = "al_outcomecase";
        private const string ActionEntity = "al_remediationaction";
        private const string EmailAttr = "emailaddress1";

        /// <summary>The pre-image the Update step is registered with, holding emailaddress1.</summary>
        public const string PreImageName = "PreImage";

        public AdviserContactPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AdviserContactPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var system = localPluginContext.OrgSvcFactory.CreateOrganizationService(null);
            Apply(context, system, trace: message => localPluginContext.Trace(message));
        }

        /// <summary>
        /// Follows the contact's email and, after an email change, the address it gave up.
        /// Returns how many actions moved. Separated from the pipeline wiring so the rule can
        /// be tested against any service.
        /// </summary>
        public static int Apply(IPluginExecutionContext context, IOrganizationService system, Action<string> trace = null)
        {
            var email = EmailOf(context, system);

            // One set of lookups for the whole save: both addresses' cases share the product
            // name and the teams, and each email's contact and supervisor are read once.
            var lookups = new CaseAccessLookups(system);
            var moved = Follow(system, email, context.CorrelationId, lookups);

            // The address the contact gave up. Its cases' open actions are still held by this
            // contact, who no longer answers to the email on those cases, so they go to whoever
            // holds that address now - or to nobody, never guessed. The same address in
            // different letter case, or with spaces round it, is the same mailbox, and
            // Dataverse equality already ignores case, so it is not followed twice. No image
            // (a Create, or the step registered before the image was) leaves only the new
            // address followed.
            var previous = PreviousEmailOf(context);
            var followPrevious = previous != null && !string.Equals(previous, email, StringComparison.OrdinalIgnoreCase);
            if (followPrevious)
            {
                moved += Follow(system, previous, context.CorrelationId, lookups);
            }

            // No addresses: the trace log outlives the case, and this is enough to tell a
            // missing pre-image from a follow that found nothing to move.
            trace?.Invoke("Pre-image " + (HasPreImage(context) ? "present" : "absent")
                + "; old email followed: " + (followPrevious ? "yes" : "no")
                + "; actions moved: " + moved);

            return moved;
        }

        /// <summary>
        /// Re-points the open actions of every case carrying this adviser email, and brings
        /// those cases' adviser access in line with it. Returns how many actions moved.
        ///
        /// Only the cases a contact write can still change are visited (<see cref="CasesFor"/>),
        /// because this runs inside the contact save: every case costs a dozen or so requests
        /// against the two-minute synchronous limit, and a long-serving adviser's passes would
        /// otherwise roll back the save of their own contact. What does not depend on the case
        /// - the product name, the teams, who the email resolves to - is read once for the run
        /// (<see cref="CaseAccessLookups"/>), not once per case.
        /// </summary>
        public static int Follow(IOrganizationService service, string email, Guid correlationId)
        {
            return Follow(service, email, correlationId, new CaseAccessLookups(service));
        }

        /// <summary>
        /// <see cref="Follow(IOrganizationService, string, Guid)"/> with lookups shared with
        /// another Follow in the same save.
        /// </summary>
        public static int Follow(IOrganizationService service, string email, Guid correlationId, CaseAccessLookups lookups)
        {
            var moved = 0;
            foreach (var followed in CasesFor(service, email))
            {
                // A case whose actions are all completed has nothing to move (BR-007), so the
                // actions are not read again; its access is still reconciled below.
                if (followed.HasOpenAction)
                {
                    moved += Remediation.AssignOpenActions(service, new EntityReference(CaseEntity, followed.CaseId), correlationId);
                }

                CaseAccessReconciler.Reconcile(service, followed.CaseId, DateTime.UtcNow, lookups);
            }

            return moved;
        }

        /// <summary>
        /// The contact's email: from the write when it carries one, else - on an Update of
        /// statecode alone - from the row. A Create's Target carries every column the create
        /// wrote, so a contact created without an email has none and nothing is read back.
        /// </summary>
        public static string EmailOf(IPluginExecutionContext context, IOrganizationService service)
        {
            object raw;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            if (target != null && target.Contains(EmailAttr))
            {
                return CasePeople.Clean(target.GetAttributeValue<string>(EmailAttr));
            }

            if (string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var row = service.Retrieve("contact", context.PrimaryEntityId, new ColumnSet(EmailAttr));
            return CasePeople.Clean(row.GetAttributeValue<string>(EmailAttr));
        }

        /// <summary>
        /// The email the contact held before this write, from the <see cref="PreImageName"/>
        /// pre-image, or null when there is no image or it holds no email.
        /// </summary>
        private static bool HasPreImage(IPluginExecutionContext context)
        {
            return context.PreEntityImages != null && context.PreEntityImages.Contains(PreImageName);
        }

        public static string PreviousEmailOf(IPluginExecutionContext context)
        {
            Entity image;
            if (context.PreEntityImages == null
                || !context.PreEntityImages.TryGetValue(PreImageName, out image)
                || image == null)
            {
                return null;
            }

            return CasePeople.Clean(image.GetAttributeValue<string>(EmailAttr));
        }

        /// <summary>
        /// The cases carrying this adviser email that a contact write can still change, each
        /// once, and whether it has an action still open.
        ///
        /// A contact changes two things on a case, and both need a remedial action. It decides
        /// who holds the case's OPEN actions (<see cref="Remediation.AssignOpenActions"/>), and
        /// it decides al_advisercontactid, which the reconciler writes only while the case is
        /// released to its adviser - and release needs a remedial action (<see
        /// cref="CaseAccess.IsReleased"/>). So a case with no remediation, which is every pass
        /// whether closed or not, is left alone.
        ///
        /// A CLOSED case with remediation is not: the adviser keeps a remediated case after
        /// sign-off (CaseAccess's release statuses include Closed), so a deactivated contact
        /// has to lose it and a newly onboarded one has to gain it, and nothing else would
        /// reconcile it. Its actions are all completed, so only its access is followed.
        ///
        /// Read from the actions in one paged query rather than case by case, so the cost of
        /// finding the work does not grow with the cases that have none.
        /// </summary>
        public static IList<FollowedCase> CasesFor(IOrganizationService service, string email)
        {
            var cases = new List<FollowedCase>();
            var value = CasePeople.Clean(email);
            if (value == null)
            {
                return cases;
            }

            var query = new QueryExpression(ActionEntity)
            {
                ColumnSet = new ColumnSet("al_outcomecaseid", "al_actionstatus"),
                Criteria = new FilterExpression(),
            };
            var outcomeCase = query.AddLink(CaseEntity, "al_outcomecaseid", "al_outcomecaseid");
            outcomeCase.LinkCriteria.AddCondition(CasePeople.AdviserEmailAttr, ConditionOperator.Equal, value);

            var byCase = new Dictionary<Guid, FollowedCase>();
            foreach (var action in CommandHelpers.RetrieveAll(service, query))
            {
                var caseRef = action.GetAttributeValue<EntityReference>("al_outcomecaseid");
                if (caseRef == null)
                {
                    continue;
                }

                FollowedCase followed;
                if (!byCase.TryGetValue(caseRef.Id, out followed))
                {
                    followed = new FollowedCase { CaseId = caseRef.Id };
                    byCase[caseRef.Id] = followed;
                    cases.Add(followed);
                }

                var status = action.GetAttributeValue<OptionSetValue>("al_actionstatus");
                if (status == null || status.Value != Remediation.StatusCompleted)
                {
                    followed.HasOpenAction = true;
                }
            }

            return cases;
        }

        /// <summary>One case a contact write reaches.</summary>
        public sealed class FollowedCase
        {
            public Guid CaseId { get; set; }

            /// <summary>True when any of its actions is not completed.</summary>
            public bool HasOpenAction { get; set; }
        }
    }
}
