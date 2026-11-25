using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// When an adviser's T&amp;C Managers change, their cases follow them. Registered synchronous
    /// post-operation on al_advisermapping Create, Update (al_tcmanagerid, al_adviseremail,
    /// statecode) and Delete. The Update and Delete steps carry a pre-image named
    /// <c>PreImage</c> holding al_adviseremail.
    ///
    /// The sign-off gate reads the mapping live, but the portal lists a case to the managers
    /// linked to it (al_outcomecase_tcmanagers), and those links were only rewritten by a write
    /// to the case. So a mapping changed after the adviser completed their actions left the
    /// case with the old coach, who was refused, while the new coach could not see it (case
    /// 259162944). An adviser may have several managers (owner, 2026-11-24): one row each.
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
        /// tells each manager newly linked to a case already waiting for sign-off. Returns how
        /// many cases changed managers.
        /// </summary>
        public static int Apply(IPluginExecutionContext context, IOrganizationService system, Action<string> trace = null)
        {
            RefuseSelfManagement(context, system);

            // Before the cases are reconciled, so they read the status this write set.
            AdviserStatus.SyncMappingRows(context, system);

            var moved = 0;
            var lookups = new CaseAccessLookups(system);
            foreach (var email in EmailsOf(context, system))
            {
                foreach (var followed in AdviserContactPlugin.CasesFor(system, email))
                {
                    var change = CaseAccessReconciler.Reconcile(system, followed.CaseId, DateTime.UtcNow, lookups);
                    if (change.NewSupervisors.Count == 0 && change.RemovedSupervisors == 0
                        && !change.ChangedColumns.Contains(CaseAccessReconciler.SupervisorAttr))
                    {
                        continue;
                    }

                    moved++;
                    TellNewCoach(system, followed.CaseId, change.NewSupervisors, context.CorrelationId, trace);
                }
            }

            trace?.Invoke("Cases whose T&C Managers changed: " + moved);
            return moved;
        }

        /// <summary>
        /// Refuses an active mapping that makes an adviser their own T&amp;C Manager. The
        /// sign-off gate only asks whether the signer is one of the mapped managers
        /// (<see cref="SupervisorMapping"/>), so such a row let an adviser attest to their own
        /// remediation. Checked per row: each row is one adviser and one manager. Compared by email, as people are identified (AD-228). A withdrawn row
        /// is left alone so a bad one can still be switched off. Runs inside the write's
        /// transaction (synchronous post-operation), so the refusal undoes the save.
        /// </summary>
        public static void RefuseSelfManagement(IPluginExecutionContext context, IOrganizationService system)
        {
            object raw;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            if (target == null)
            {
                return;
            }

            var id = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
            var row = system.Retrieve(
                TcManagerRouting.MappingEntity, id,
                new ColumnSet(TcManagerRouting.MappingEmailAttr, TcManagerRouting.ManagerAttr, "statecode"));
            var adviser = CasePeople.Clean(row.GetAttributeValue<string>(TcManagerRouting.MappingEmailAttr));
            var manager = row.GetAttributeValue<EntityReference>(TcManagerRouting.ManagerAttr);
            if (!CommandHelpers.IsActive(row) || adviser == null || manager == null)
            {
                return;
            }

            var managerEmail = CasePeople.Clean(
                system.Retrieve(ContactRegistry.Entity, manager.Id, new ColumnSet(ContactRegistry.EmailAttr))
                    .GetAttributeValue<string>(ContactRegistry.EmailAttr));
            if (string.Equals(managerEmail, adviser, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "An adviser cannot be their own T&C Manager. Choose someone else to sign off their cases.");
            }
        }

        /// <summary>
        /// Tells <paramref name="newManagers"/>, and only them, that this case is waiting for
        /// sign-off - the managers already linked were told when it started waiting.
        /// </summary>
        public static void TellNewCoach(
            IOrganizationService system, Guid caseId, IList<EntityReference> newManagers, Guid correlationId, Action<string> trace)
        {
            var row = system.Retrieve(CaseEntity, caseId, new ColumnSet("al_casestatus"));
            var status = row.GetAttributeValue<OptionSetValue>("al_casestatus");
            if (status == null || status.Value != CaseLifecycle.AwaitingSignoff || newManagers == null || newManagers.Count == 0)
            {
                return;
            }

            var ids = new HashSet<Guid>();
            foreach (var manager in newManagers)
            {
                ids.Add(manager.Id);
            }

            CompleteRemediationPlugin.NotifySignoffDue(
                system, new EntityReference(CaseEntity, caseId), correlationId, trace, ids);
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
