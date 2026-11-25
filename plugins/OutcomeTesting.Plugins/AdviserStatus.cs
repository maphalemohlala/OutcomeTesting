using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The adviser's status as OTIS holds it, set by an administrator (client, 2026-11-25).
    /// Where it is set it beats IO: an adviser signed off in OTIS is not a new entrant however
    /// the IO task is ticked, and the reverse. Where it is not set, IO decides as before.
    ///
    /// Held on the adviser's mapping rows (one per T&amp;C Manager), kept equal across them by
    /// <see cref="SyncMappingRows"/>, so every reader can take any active row.
    /// </summary>
    public static class AdviserStatus
    {
        /// <summary>The column on both al_advisermapping and al_outcomecase; same option values.</summary>
        public const string Attr = "al_adviserstatus";

        /// <summary>The capability an administrator holds to set it.</summary>
        public const string Resource = "adviser.status";

        public const int PreCas = 120910500;

        /// <summary>The status OTIS holds for this adviser email, or null when none is set.</summary>
        public static int? For(IOrganizationService service, string adviserEmail)
        {
            var email = CasePeople.Clean(adviserEmail);
            if (email == null)
            {
                return null;
            }

            int? status;
            TcManagerRouting.ManagersFor(service, email, out status);
            return status;
        }

        /// <summary>
        /// Run on a mapping write. A row that carries the status passes it to the adviser's other
        /// rows; a new row that carries none takes it from them. Only an administrator may set it,
        /// checked on the caller's own write (depth 1) - the copies this makes are not theirs.
        /// </summary>
        public static void SyncMappingRows(IPluginExecutionContext context, IOrganizationService system)
        {
            object raw;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            if (target == null)
            {
                return;
            }

            var carries = target.Contains(Attr);
            if (carries && context.Depth <= 1)
            {
                PermissionHelpers.EnsureAppPermission(system, context, Resource, PermissionHelpers.AccessEdit);
            }

            var isCreate = string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase);
            if (!carries && !isCreate)
            {
                return;
            }

            var id = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
            var row = system.Retrieve(TcManagerRouting.MappingEntity, id, new ColumnSet(TcManagerRouting.MappingEmailAttr, Attr));
            var email = CasePeople.Clean(row.GetAttributeValue<string>(TcManagerRouting.MappingEmailAttr));
            if (email == null)
            {
                return;
            }

            var query = new QueryExpression(TcManagerRouting.MappingEntity)
            {
                ColumnSet = new ColumnSet(Attr),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(TcManagerRouting.MappingEmailAttr, ConditionOperator.Equal, email);
            var siblings = CommandHelpers.RetrieveAll(system, query).FindAll(r => r.Id != id);

            if (!carries)
            {
                // A new manager row for an adviser who already has a status.
                var held = siblings.Find(r => r.GetAttributeValue<OptionSetValue>(Attr) != null);
                if (held != null)
                {
                    system.Update(new Entity(TcManagerRouting.MappingEntity, id) { [Attr] = held[Attr] });
                }

                return;
            }

            var wanted = row.GetAttributeValue<OptionSetValue>(Attr);
            foreach (var sibling in siblings)
            {
                var has = sibling.GetAttributeValue<OptionSetValue>(Attr);
                if ((has == null ? (int?)null : has.Value) != (wanted == null ? (int?)null : wanted.Value))
                {
                    system.Update(new Entity(TcManagerRouting.MappingEntity, sibling.Id) { [Attr] = wanted });
                }
            }

            // Every open case of the adviser, remediated or not; a closed case keeps the status
            // it was checked under. Clearing the status leaves the cases alone: IO decides again.
            if (wanted == null)
            {
                return;
            }

            var cases = new QueryExpression("al_outcomecase")
            {
                ColumnSet = new ColumnSet(Attr, "al_casestatus"),
                Criteria = new FilterExpression(),
            };
            cases.Criteria.AddCondition(CasePeople.AdviserEmailAttr, ConditionOperator.Equal, email);
            cases.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            foreach (var outcomeCase in CommandHelpers.RetrieveAll(system, cases))
            {
                var status = ValueOf(outcomeCase.GetAttributeValue<OptionSetValue>("al_casestatus"));
                if (status != CaseLifecycle.Closed && status != CaseLifecycle.NoCheckRequired
                    && ValueOf(outcomeCase.GetAttributeValue<OptionSetValue>(Attr)) != wanted.Value)
                {
                    system.Update(new Entity("al_outcomecase", outcomeCase.Id) { [Attr] = new OptionSetValue(wanted.Value) });
                }
            }
        }

        /// <summary>
        /// Refuses a case write that changes the adviser status away from what OTIS holds for
        /// the adviser. Rewriting the value already on the case passes, so a form that sends its
        /// whole header still saves; where OTIS holds nothing, the case field stays editable.
        /// </summary>
        public static void EnsureCaseWriteAllowed(IOrganizationService service, Entity target)
        {
            if (target == null || !target.Contains(Attr) || target.Id == Guid.Empty)
            {
                return;
            }

            var current = service.Retrieve("al_outcomecase", target.Id, new ColumnSet(Attr, CasePeople.AdviserEmailAttr));
            var wanted = ValueOf(target.GetAttributeValue<OptionSetValue>(Attr));
            if (wanted == ValueOf(current.GetAttributeValue<OptionSetValue>(Attr)))
            {
                return;
            }

            var email = target.Contains(CasePeople.AdviserEmailAttr)
                ? target.GetAttributeValue<string>(CasePeople.AdviserEmailAttr)
                : current.GetAttributeValue<string>(CasePeople.AdviserEmailAttr);
            var held = For(service, email);
            if (held.HasValue && held != wanted)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "This adviser's status is set by an administrator on the Adviser mapping page, so it cannot be changed on the case.");
            }
        }

        /// <summary>Whether the case's adviser is a new entrant: OTIS's status if set, otherwise IO's tick or the case field.</summary>
        public static bool IsNewEntrant(IOrganizationService service, Entity caseRow)
        {
            if (caseRow == null)
            {
                return false;
            }

            var held = For(service, caseRow.GetAttributeValue<string>(CasePeople.AdviserEmailAttr));
            if (held.HasValue)
            {
                return held.Value == PreCas;
            }

            return ValueOf(caseRow.GetAttributeValue<OptionSetValue>(Attr)) == PreCas
                || (caseRow.GetAttributeValue<string>("al_checklistitems") ?? string.Empty)
                    .IndexOf("Pre-CAS Adviser", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int? ValueOf(OptionSetValue value)
        {
            return value == null ? (int?)null : value.Value;
        }
    }
}
