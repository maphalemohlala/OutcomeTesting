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
    /// emailaddress1 and statecode.
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
            Follow(system, EmailOf(context, system), context.CorrelationId);
        }

        /// <summary>Re-points the open actions of every case carrying this adviser email.</summary>
        public static int Follow(IOrganizationService service, string email, Guid correlationId)
        {
            var moved = 0;
            foreach (var caseId in CasesFor(service, email))
            {
                moved += Remediation.AssignOpenActions(service, new EntityReference(CaseEntity, caseId), correlationId);
                CaseAccessReconciler.Reconcile(service, caseId, DateTime.UtcNow);
            }

            return moved;
        }

        /// <summary>The contact's email: from the write when it carries one, else from the row.</summary>
        public static string EmailOf(IPluginExecutionContext context, IOrganizationService service)
        {
            object raw;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            if (target != null && target.Contains("emailaddress1"))
            {
                return CasePeople.Clean(target.GetAttributeValue<string>("emailaddress1"));
            }

            var row = service.Retrieve("contact", context.PrimaryEntityId, new ColumnSet("emailaddress1"));
            return CasePeople.Clean(row.GetAttributeValue<string>("emailaddress1"));
        }

        /// <summary>The cases whose adviser email is this one.</summary>
        public static IList<Guid> CasesFor(IOrganizationService service, string email)
        {
            var ids = new List<Guid>();
            var value = CasePeople.Clean(email);
            if (value == null)
            {
                return ids;
            }

            var query = new QueryExpression(CaseEntity) { ColumnSet = new ColumnSet(false), Criteria = new FilterExpression() };
            query.Criteria.AddCondition(CasePeople.AdviserEmailAttr, ConditionOperator.Equal, value);
            foreach (var row in CommandHelpers.RetrieveAll(service, query))
            {
                ids.Add(row.Id);
            }

            return ids;
        }
    }
}
