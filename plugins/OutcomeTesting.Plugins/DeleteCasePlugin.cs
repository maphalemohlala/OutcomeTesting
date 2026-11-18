using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Custom API message <c>al_DeleteCase</c> (requirements log row 40): deletes a case and
    /// everything recorded against it, at any stage. "Remove case" stays for taking a case out
    /// of checking; this is for a case that should never have been in OTIS at all.
    ///
    /// Every table that points at a case restricts the delete (al_outcomecase's relationships
    /// in DEV, read 2026-11-17), so the children go first, in the order their own lookups
    /// demand: sign-offs, remedial actions, outcomes, allocations, export records, then the
    /// checks (their answers cascade), then the case. Unsent letters about any of them go too,
    /// so nothing is emailed about a case that no longer exists. One transaction: a failure
    /// anywhere rolls the whole delete back.
    ///
    /// Audit events are kept, and one more is written naming the case, who deleted it and
    /// why - the trail outlives the record it describes.
    ///
    /// Gated by its own capability, <c>case.delete</c>, and confirmed by typing the case
    /// reference, because nothing brings a deleted case back. The deletes run as the plug-in
    /// user: the capability is the gate, and no app role holds Delete on every child table.
    /// </summary>
    public class DeleteCasePlugin : PluginBase
    {
        public const string Resource = "case.delete";
        public const int CommandDeleteCase = 120910777;

        private const string InTargetId = "TargetId";
        private const string InConfirmReference = "ConfirmReference";
        private const string InReason = "Reason";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutStatus = "Status";
        private const string OutSummary = "Summary";
        private const string OutAuditEventId = "AuditEventId";

        private const string CaseEntity = "al_outcomecase";
        private const string CaseAttr = "al_outcomecaseid";

        /// <summary>A table that points at a case, and what to call its rows in the audit line.</summary>
        public sealed class Child
        {
            public Child(string table, string label)
            {
                Table = table;
                Label = label;
            }

            public string Table { get; }
            public string Label { get; }
            public List<Guid> Ids { get; set; }
        }

        // No ValueTuple: the sandbox runs .NET Framework 4.6.2, which does not ship it.
        /// <summary>The case's children, in the order they must be deleted.</summary>
        public static readonly string[][] Children =
        {
            new[] { "al_signoff", "sign-offs" },
            new[] { "al_remediationaction", "remedial actions" },
            new[] { "al_outcome", "outcomes" },
            new[] { "al_caseassignment", "allocations" },
            new[] { "al_exportrecord", "export records" },
            new[] { "al_reviewinstance", "checks" },
        };

        public DeleteCasePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(DeleteCasePlugin))
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
            var confirm = CommandHelpers.GetRequiredString(context, InConfirmReference);
            var reason = CommandHelpers.GetRequiredString(context, InReason).Trim();
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(systemService, context, Resource, PermissionHelpers.AccessEdit);

            // A replay after a lost response: the case is already gone, so answer as before.
            var existing = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandDeleteCase);
            if (existing != null)
            {
                SetResponse(context, existing.GetAttributeValue<string>("al_details"), existing.Id);
                return;
            }

            var summary = Delete(systemService, caseId, confirm, reason);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandDeleteCase, "DeleteCase", CaseEntity, caseId,
                reason, summary, idempotencyKey, context);

            SetResponse(context, summary, auditId);
        }

        /// <summary>
        /// Deletes the case and its children and returns what was deleted, for the audit line.
        /// Public so the whole delete can be exercised against a fake service.
        /// </summary>
        public static string Delete(IOrganizationService service, Guid caseId, string confirmReference, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "Say why the case is being deleted.");
            }

            var found = service.RetrieveMultiple(new QueryExpression(CaseEntity)
            {
                ColumnSet = new ColumnSet("al_casereference", "al_clientname"),
                Criteria = { Conditions = { new ConditionExpression(CaseAttr, ConditionOperator.Equal, caseId) } },
            }).Entities.FirstOrDefault();

            if (found == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.NotFoundPrefix + "That case no longer exists. Refresh the list.");
            }

            var reference = found.GetAttributeValue<string>("al_casereference") ?? string.Empty;
            if (!ReferenceMatches(reference, confirmReference))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "Type the case reference " + reference
                    + " exactly to confirm the delete.");
            }

            var children = Children
                .Select(pair => new Child(pair[0], pair[1]) { Ids = IdsFor(service, pair[0], caseId) })
                .ToList();

            // Unsent letters about the case or anything on it.
            var targets = new HashSet<string>(
                children.SelectMany(c => c.Ids).Concat(new[] { caseId }).Select(id => id.ToString("D")),
                StringComparer.OrdinalIgnoreCase);
            var unsent = UnsentLettersAbout(service, targets);

            foreach (var id in unsent)
            {
                service.Delete("al_notification", id);
            }

            foreach (var child in children)
            {
                foreach (var id in child.Ids)
                {
                    service.Delete(child.Table, id);
                }
            }

            service.Delete(CaseEntity, caseId);

            var client = found.GetAttributeValue<string>("al_clientname");
            return "Deleted case " + reference
                + (string.IsNullOrWhiteSpace(client) ? string.Empty : " (" + client.Trim() + ")")
                + " with " + string.Join(", ", children.Select(c => c.Ids.Count + " " + c.Label))
                + " and " + unsent.Count + " unsent letters.";
        }

        /// <summary>Whether the typed confirmation names the case: trimmed, any case.</summary>
        public static bool ReferenceMatches(string reference, string typed)
        {
            return !string.IsNullOrWhiteSpace(reference)
                && string.Equals(reference.Trim(), (typed ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static List<Guid> IdsFor(IOrganizationService service, string table, Guid caseId)
        {
            var query = new QueryExpression(table) { ColumnSet = new ColumnSet(false) };
            query.Criteria.AddCondition(CaseAttr, ConditionOperator.Equal, caseId);
            return service.RetrieveMultiple(query).Entities.Select(e => e.Id).ToList();
        }

        private static List<Guid> UnsentLettersAbout(IOrganizationService service, HashSet<string> targets)
        {
            var query = new QueryExpression("al_notification") { ColumnSet = new ColumnSet("al_targetid") };
            query.Criteria.AddCondition("al_status", ConditionOperator.Equal, NotificationOutbox.StatusPending);
            return service.RetrieveMultiple(query).Entities
                .Where(n => targets.Contains((n.GetAttributeValue<string>("al_targetid") ?? string.Empty).Trim()))
                .Select(n => n.Id)
                .ToList();
        }

        private static void SetResponse(IPluginExecutionContext context, string summary, Guid auditId)
        {
            context.OutputParameters[OutStatus] = "Deleted";
            context.OutputParameters[OutSummary] = summary ?? string.Empty;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
