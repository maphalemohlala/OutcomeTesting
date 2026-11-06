using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// When an adviser's T&amp;C Manager changes, their cases follow it. Registered synchronous
    /// post-operation on al_advisermapping Create, Update (al_tcmanagerid, al_adviseremail,
    /// statecode) and Delete. The Update and Delete steps carry a pre-image named
    /// <c>PreImage</c> holding al_adviseremail.
    ///
    /// The sign-off gate reads the mapping live, but the portal lists a case to the coach held
    /// in al_tcsupervisorcontactid, and that column was only rewritten by a write to the case.
    /// So a mapping changed after the adviser completed their actions left the case with the
    /// old coach, who was refused, while the new coach could not see it (case 259162944).
    ///
    /// SYSTEM, as AdviserContactPlugin is: the access columns are a consequence of the write.
    /// </summary>
    public class AdviserMappingPlugin : PluginBase
    {
        private const string CaseEntity = "al_outcomecase";

        public const string PreImageName = "PreImage";

        public AdviserMappingPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AdviserMappingPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var system = localPluginContext.OrgSvcFactory.CreateOrganizationService(null);
            Apply(localPluginContext.PluginExecutionContext, system, trace: message => localPluginContext.Trace(message));
        }

        /// <summary>
        /// Brings the cases of every adviser this write touches in line with the mapping, and
        /// tells the new coach about any case already waiting for them. Returns how many
        /// cases changed coach.
        /// </summary>
        public static int Apply(IPluginExecutionContext context, IOrganizationService system, Action<string> trace = null)
        {
            var moved = 0;
            var lookups = new CaseAccessLookups(system);
            foreach (var email in EmailsOf(context, system))
            {
                foreach (var followed in AdviserContactPlugin.CasesFor(system, email))
                {
                    var change = CaseAccessReconciler.Reconcile(system, followed.CaseId, DateTime.UtcNow, lookups);
                    if (!change.ChangedColumns.Contains(CaseAccessReconciler.SupervisorAttr))
                    {
                        continue;
                    }

                    moved++;
                    TellNewCoach(system, followed.CaseId, context.CorrelationId, trace);
                }
            }

            trace?.Invoke("Cases that changed coach: " + moved);
            return moved;
        }

        private static void TellNewCoach(IOrganizationService system, Guid caseId, Guid correlationId, Action<string> trace)
        {
            var row = system.Retrieve(
                CaseEntity, caseId, new ColumnSet("al_casestatus", CaseAccessReconciler.SupervisorAttr));
            var status = row.GetAttributeValue<OptionSetValue>("al_casestatus");
            var coach = row.GetAttributeValue<EntityReference>(CaseAccessReconciler.SupervisorAttr);
            if (status == null || status.Value != CaseLifecycle.AwaitingSignoff || coach == null)
            {
                return;
            }

            CompleteRemediationPlugin.NotifySignoffDue(
                system,
                new EntityReference(CaseEntity, caseId),
                correlationId,
                trace,
                coach.Id.ToString("N").Substring(0, 8));
        }

        /// <summary>
        /// The adviser emails this write touches: the one it holds now and, where it changed
        /// or the row went, the one it held. A write that leaves the email alone carries none,
        /// so the row answers.
        /// </summary>
        public static IList<string> EmailsOf(IPluginExecutionContext context, IOrganizationService service)
        {
            var emails = new List<string>();
            Action<string> add = value =>
            {
                var clean = CasePeople.Clean(value);
                if (clean != null && !emails.Exists(e => string.Equals(e, clean, StringComparison.OrdinalIgnoreCase)))
                {
                    emails.Add(clean);
                }
            };

            object raw;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            if (target != null && target.Contains(TcManagerRouting.MappingEmailAttr))
            {
                add(target.GetAttributeValue<string>(TcManagerRouting.MappingEmailAttr));
            }

            Entity before;
            if (context.PreEntityImages != null && context.PreEntityImages.TryGetValue(PreImageName, out before))
            {
                add(before.GetAttributeValue<string>(TcManagerRouting.MappingEmailAttr));
            }

            if (emails.Count == 0 && target != null)
            {
                add(service.Retrieve(target.LogicalName, target.Id, new ColumnSet(TcManagerRouting.MappingEmailAttr))
                    .GetAttributeValue<string>(TcManagerRouting.MappingEmailAttr));
            }

            return emails;
        }
    }
}
