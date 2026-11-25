using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Custom API message <c>al_ReassignTcManager</c> (requirements log row 4): hands one case's
    /// remedial action and sign-off to another T&amp;C Manager, for when the managers mapped to the
    /// adviser are away. Changing the adviser mapping would move every case of that adviser.
    /// The chosen manager replaces ALL the mapped ones for this case; an adviser with several
    /// managers rarely needs it, since any of them may sign.
    ///
    /// The choice is stored on the case (<see cref="TcManagerRouting.OverrideAttr"/>), and
    /// routing reads it before the mapping, so the portal sign-off and regrade gates, the
    /// emails and the case access all follow it. An empty manager hands the case back to the
    /// mapping. The new manager is told when the case is already waiting for sign-off.
    ///
    /// Gated by its own capability, <c>case.reassignmanager</c>. The writes run as the plug-in
    /// user: the case's access columns are refused to direct writes.
    /// </summary>
    public class ReassignTcManagerPlugin : PluginBase
    {
        public const string Resource = "case.reassignmanager";
        public const int CommandReassignTcManager = 120910780;

        private const string InTargetId = "TargetId";
        private const string InManagerId = "ManagerId";
        private const string InReason = "Reason";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutStatus = "Status";
        private const string OutSummary = "Summary";
        private const string OutAuditEventId = "AuditEventId";

        private const string CaseEntity = "al_outcomecase";

        public ReassignTcManagerPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ReassignTcManagerPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var systemService = localPluginContext.PluginUserService;

            var caseId = CommandHelpers.ParseRequiredGuid(context, InTargetId);
            var rawManager = CommandHelpers.GetOptionalString(context, InManagerId);
            var reason = CommandHelpers.GetRequiredString(context, InReason).Trim();
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(systemService, context, Resource, PermissionHelpers.AccessEdit);

            var existing = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandReassignTcManager);
            if (existing != null)
            {
                SetResponse(context, existing.GetAttributeValue<string>("al_details"), existing.Id);
                return;
            }

            Guid? managerId = null;
            if (!string.IsNullOrWhiteSpace(rawManager))
            {
                Guid parsed;
                if (!Guid.TryParse(rawManager.Trim(), out parsed))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ValidationPrefix + "The T&C Manager chosen is not a valid person. Pick them again.");
                }

                managerId = parsed;
            }

            var summary = Reassign(systemService, caseId, managerId, reason);

            // The same follow-up an adviser mapping change runs: access moves with the manager,
            // and a manager taking over a waiting sign-off is told.
            var change = CaseAccessReconciler.Reconcile(systemService, caseId, DateTime.UtcNow);
            AdviserMappingPlugin.TellNewCoach(
                systemService, caseId, change.NewSupervisors, context.CorrelationId, message => localPluginContext.Trace(message));

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandReassignTcManager, "ReassignTcManager", CaseEntity, caseId,
                reason, summary, idempotencyKey, context);

            SetResponse(context, summary, auditId);
        }

        /// <summary>
        /// Records <paramref name="managerId"/> as the case's own T&amp;C Manager (null hands it back
        /// to the adviser mapping) and returns the audit line. The caller reconciles access after.
        /// </summary>
        public static string Reassign(IOrganizationService service, Guid caseId, Guid? managerId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "Say why the case is being handed to another T&C Manager.");
            }

            var found = service.RetrieveMultiple(new QueryExpression(CaseEntity)
            {
                ColumnSet = new ColumnSet("al_casereference", TcManagerRouting.CaseAdviserEmailAttr, TcManagerRouting.OverrideAttr),
                Criteria = { Conditions = { new ConditionExpression("al_outcomecaseid", ConditionOperator.Equal, caseId) } },
            }).Entities.FirstOrDefault();

            if (found == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.NotFoundPrefix + "That case no longer exists. Refresh the page.");
            }

            var reference = found.GetAttributeValue<string>("al_casereference") ?? "the case";
            var before = TcManagerRouting.ChosenFor(found);
            var beforeText = before == null ? "the adviser's mapped T&C Managers" : NameOf(service, before.Id);

            EntityReference chosen = null;
            if (managerId.HasValue)
            {
                var contact = service.RetrieveMultiple(new QueryExpression("contact")
                {
                    ColumnSet = new ColumnSet("fullname", "emailaddress1", "statecode"),
                    Criteria = { Conditions = { new ConditionExpression("contactid", ConditionOperator.Equal, managerId.Value) } },
                }).Entities.FirstOrDefault();

                if (contact == null || !CommandHelpers.IsActive(contact))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ValidationPrefix + "The T&C Manager chosen is no longer an active person. Pick someone else.");
                }

                var name = contact.GetAttributeValue<string>("fullname") ?? "That person";
                if (!WebRoleRegistry.HasRole(service, contact.Id, WebRoleRegistry.TcSupervisorRole))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix + name + " does not hold the "
                        + WebRoleRegistry.TcSupervisorRole + " role, so they could not sign the case off.");
                }

                var adviser = CasePeople.Clean(found.GetAttributeValue<string>(TcManagerRouting.CaseAdviserEmailAttr));
                var email = CasePeople.Clean(contact.GetAttributeValue<string>("emailaddress1"));
                if (adviser != null && string.Equals(adviser, email, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix + "An adviser cannot sign off their own case. Choose someone else.");
                }

                chosen = new EntityReference("contact", contact.Id);
            }

            if ((before == null && chosen == null) || (before != null && chosen != null && before.Id == chosen.Id))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "Case " + reference + " is already with " + beforeText + ".");
            }

            service.Update(new Entity(CaseEntity, caseId) { [TcManagerRouting.OverrideAttr] = chosen });

            var afterText = chosen == null ? "the adviser's mapped T&C Managers" : NameOf(service, chosen.Id);
            return "Case " + reference + " handed from " + beforeText + " to " + afterText + ".";
        }

        private static string NameOf(IOrganizationService service, Guid contactId)
        {
            var name = service.Retrieve("contact", contactId, new ColumnSet("fullname")).GetAttributeValue<string>("fullname");
            return string.IsNullOrWhiteSpace(name) ? "an unnamed person" : name.Trim();
        }

        private static void SetResponse(IPluginExecutionContext context, string summary, Guid auditId)
        {
            context.OutputParameters[OutStatus] = "Reassigned";
            context.OutputParameters[OutSummary] = summary ?? string.Empty;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
