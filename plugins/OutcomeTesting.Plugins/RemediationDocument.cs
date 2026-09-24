using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The case's Remediation and escalation form, as blocks for <see cref="PdfWriter"/>
    /// (project owner, 2026-09-24: send the checks "and include remediation if there were
    /// any remediations completed. a seperate file for each").
    ///
    /// <para>
    /// Drawn as <c>OT Remediation</c> draws it read-only, row for row: the numbered issues
    /// grouped under the check that raised them, the action's owner, target date, status,
    /// age and sign-off, then the form's closing rows - client contact, recheck, whether the
    /// actions change the advice, whether they are all approved, the regraded outcome and the
    /// two sign-offs. The whole form, not only its completed rows: that was the choice made
    /// the same day, so the reader sees what is still open beside what was put right.
    /// </para>
    /// <para>
    /// Landscape, because the page is eight columns wide and portrait would set each at a
    /// word a line. Never throws, for the reason every attachment gives: the letter matters
    /// more than the document.
    /// </para>
    /// </summary>
    public static class RemediationDocument
    {
        public const string FormFooter = "Outcome Testing — Remediation and escalation | V8";

        private const int DecisionApproved = 120910720;
        private const int DecisionRejected = 120910721;

        /// <summary>True when any remedial action on the case has been completed by the adviser.</summary>
        public static bool AnyCompleted(IOrganizationService service, EntityReference caseRef)
        {
            foreach (var action in Actions(service, caseRef))
            {
                if (action.GetAttributeValue<DateTime?>("al_completedon").HasValue)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The form, or an empty list where the case cannot be read.</summary>
        public static List<PdfBlock> Blocks(IOrganizationService service, EntityReference caseRef, DateTime today)
        {
            var blocks = new List<PdfBlock>();
            if (caseRef == null)
            {
                return blocks;
            }

            Entity row;
            try
            {
                row = service.Retrieve("al_outcomecase", caseRef.Id, new ColumnSet(
                    "al_casereference", "al_clientname", "al_advisername", "al_reviewrouteid"));
            }
            catch (Exception)
            {
                return blocks;
            }

            var labels = new OptionLabels(service);
            var actions = Actions(service, caseRef);
            var signoffs = Signoffs(service, actions);

            blocks.Add(PdfBlock.Note(FormFooter));
            blocks.Add(PdfBlock.Title("Remediation and escalation"));

            var head = new PdfTable(0.25, 0.25, 0.25, 0.25) { Ruled = false };
            head.Add(
                Stacked("CLIENT", row.GetAttributeValue<string>("al_clientname")),
                Stacked("ADVISER", row.GetAttributeValue<string>("al_advisername")),
                Stacked("OUTCOME", Outcome(actions)),
                Stacked("CASE", row.GetAttributeValue<string>("al_casereference")));
            blocks.Add(PdfBlock.Table(head));

            if (actions.Count == 0)
            {
                blocks.Add(PdfBlock.Paragraph("No remedial actions are recorded on this case."));
                return blocks;
            }

            var table = new PdfTable(0.04, 0.2, 0.2, 0.1, 0.09, 0.09, 0.09, 0.19);
            table.AddHeader(
                PdfCell.Head("No."), PdfCell.Head("Issue / fail reason"), PdfCell.Head("Remedial action"),
                PdfCell.Head("Owner"), PdfCell.Head("Target date"), PdfCell.Head("Status"),
                PdfCell.Head("Age"), PdfCell.Head("Sign-off"));

            var number = 0;
            string group = null;
            Entity formSource = null;
            Entity latestAdviser = null;
            var allApproved = true;
            var anyRejected = false;

            foreach (var action in actions)
            {
                var review = action.GetAttributeValue<EntityReference>("al_reviewinstanceid");
                var thisGroup = review != null && !string.IsNullOrWhiteSpace(review.Name) ? review.Name : "This check";
                if (thisGroup != group)
                {
                    group = thisGroup;
                    var band = new PdfCell { Span = 8, Fill = PdfCell.BandFill };
                    band.Runs.Add(PdfRun.Of(group, true));
                    table.Add(band);
                }

                Entity latest;
                signoffs.TryGetValue(action.Id, out latest);
                var decision = latest == null ? null : latest.GetAttributeValue<OptionSetValue>("al_signoffdecision");
                if (decision == null || decision.Value != DecisionApproved)
                {
                    allApproved = false;
                }

                if (decision != null && decision.Value == DecisionRejected)
                {
                    anyRejected = true;
                }

                if (formSource == null && action.GetAttributeValue<OptionSetValue>("al_clientcontactrequired") != null)
                {
                    formSource = action;
                }

                var completed = action.GetAttributeValue<DateTime?>("al_completedon");
                if (completed.HasValue
                    && (latestAdviser == null
                        || completed.Value > latestAdviser.GetAttributeValue<DateTime?>("al_completedon").Value))
                {
                    latestAdviser = action;
                }

                var status = action.GetAttributeValue<OptionSetValue>("al_actionstatus");
                var owner = action.GetAttributeValue<EntityReference>("al_assignedcontactid");
                var opened = action.GetAttributeValue<DateTime?>("al_clockstartedon")
                    ?? action.GetAttributeValue<DateTime?>("createdon");

                var details = new[]
                {
                    Text(action.GetAttributeValue<string>("al_adviserresponse")),
                    owner != null && !string.IsNullOrWhiteSpace(owner.Name) ? owner.Name : "Nobody assigned",
                    Day(action.GetAttributeValue<DateTime?>("al_duedate")),
                    status == null ? "—" : labels.Label("al_remediationaction", "al_actionstatus", status.Value),
                    opened.HasValue ? Age(opened.Value, completed ?? today) : "—",
                    SignOff(latest, completed, labels),
                };

                var issues = Issues(action.GetAttributeValue<string>("al_description"));
                if (issues.Count == 0)
                {
                    issues.Add("—");
                }

                for (var i = 0; i < issues.Count; i++)
                {
                    number++;
                    var cells = new List<PdfCell>
                    {
                        PdfCell.Of(number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        PdfCell.Of(issues[i]),
                    };

                    // The page runs the action's own columns down the rows of its issues; here
                    // they sit on the first, and the rows under it belong to the same action.
                    foreach (var detail in details)
                    {
                        cells.Add(i == 0 ? PdfCell.Of(detail) : PdfCell.Blank());
                    }

                    table.Add(cells.ToArray());
                }
            }

            if (formSource == null)
            {
                formSource = actions[0];
            }

            const string AtSignOff = "The T&C Manager answers this at sign-off";
            table.Add(
                PdfCell.Label("Client contact required?", 2),
                PdfCell.Of(Choice(formSource, "al_clientcontactrequired", labels, "—"), 2),
                PdfCell.Label("Recheck required?", 2),
                PdfCell.Of(Choice(formSource, "al_recheckrequired", labels, AtSignOff), 2));
            table.Add(
                PdfCell.Label("Do the remedial actions change the advice?", 2),
                PdfCell.Of(Choice(formSource, "al_changesadvice", labels, AtSignOff), 2),
                PdfCell.Label("All remedial actions checked and approved?", 2),
                PdfCell.Of(allApproved ? "Yes" : anyRejected ? "No" : "—", 2));
            blocks.Add(PdfBlock.Table(table));

            var regraded = new PdfTable(0.3, 0.7);
            regraded.Add(
                PdfCell.Label("Regraded outcome (for potential harms / insufficient evidence — the supervisor signs this off)"),
                PdfCell.Of(Regraded(service, caseRef, row, labels)));
            blocks.Add(PdfBlock.Table(regraded));

            Entity latestSupervisor = null;
            DateTime? latestOn = null;
            foreach (var signoff in signoffs.Values)
            {
                var on = signoff.GetAttributeValue<DateTime?>("al_signedoffon");
                if (latestSupervisor == null || (on.HasValue && (!latestOn.HasValue || on.Value > latestOn.Value)))
                {
                    latestSupervisor = signoff;
                    latestOn = on;
                }
            }

            var signoffTable = new PdfTable(0.5, 0.5);
            signoffTable.AddHeader(PdfCell.Head(string.Empty), PdfCell.Head("Date"));
            signoffTable.Add(
                PdfCell.Label("Supervisor sign-off (complete remedial task in IO)"),
                PdfCell.Of(Supervisor(latestSupervisor, labels)));
            signoffTable.Add(
                PdfCell.Label("Adviser sign-off (complete remedial task in IO)"),
                PdfCell.Of(Adviser(latestAdviser, row)));
            blocks.Add(PdfBlock.Table(signoffTable));

            return blocks;
        }

        // ------------------------------------------------------------------ reads

        private static List<Entity> Actions(IOrganizationService service, EntityReference caseRef)
        {
            var actions = new List<Entity>();
            if (caseRef == null)
            {
                return actions;
            }

            try
            {
                var query = new QueryExpression("al_remediationaction")
                {
                    ColumnSet = new ColumnSet(
                        "al_description", "al_actionstatus", "al_duedate", "al_completedon",
                        "al_adviserresponse", "al_assignedcontactid", "al_clientcontactrequired",
                        "al_recheckrequired", "al_changesadvice", "al_clockstartedon",
                        "al_reviewinstanceid", "createdon"),
                    Criteria = new FilterExpression(),
                };
                query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseRef.Id);
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                query.AddOrder("al_duedate", OrderType.Ascending);

                actions.AddRange(CommandHelpers.RetrieveAll(service, query));
            }
            catch (Exception)
            {
                actions.Clear();
            }

            return actions;
        }

        /// <summary>Each action's latest sign-off, by action id.</summary>
        private static Dictionary<Guid, Entity> Signoffs(IOrganizationService service, List<Entity> actions)
        {
            var latest = new Dictionary<Guid, Entity>();
            if (actions.Count == 0)
            {
                return latest;
            }

            try
            {
                var ids = new List<object>();
                foreach (var action in actions)
                {
                    ids.Add(action.Id);
                }

                var query = new QueryExpression("al_signoff")
                {
                    ColumnSet = new ColumnSet(
                        "al_signoffdecision", "al_signedoffon", "al_signedbyname", "al_remediationactionid"),
                    Criteria = new FilterExpression(),
                };
                query.Criteria.AddCondition("al_remediationactionid", ConditionOperator.In, ids.ToArray());

                foreach (var signoff in CommandHelpers.RetrieveAll(service, query))
                {
                    var action = signoff.GetAttributeValue<EntityReference>("al_remediationactionid");
                    if (action == null)
                    {
                        continue;
                    }

                    Entity held;
                    var on = signoff.GetAttributeValue<DateTime?>("al_signedoffon");
                    if (!latest.TryGetValue(action.Id, out held)
                        || (on.HasValue && on.Value > (held.GetAttributeValue<DateTime?>("al_signedoffon") ?? DateTime.MinValue)))
                    {
                        latest[action.Id] = signoff;
                    }
                }
            }
            catch (Exception)
            {
                latest.Clear();
            }

            return latest;
        }

        private static string Regraded(
            IOrganizationService service, EntityReference caseRef, Entity row, OptionLabels labels)
        {
            Entity outcome = null;
            try
            {
                var query = new QueryExpression("al_outcome")
                {
                    ColumnSet = new ColumnSet("al_finaloutcome", "al_regradedon", "al_finalisedon"),
                    Criteria = new FilterExpression(),
                };
                query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseRef.Id);
                foreach (var found in CommandHelpers.RetrieveAll(service, query))
                {
                    outcome = found;
                    break;
                }
            }
            catch (Exception)
            {
                outcome = null;
            }

            var final = outcome == null ? null : outcome.GetAttributeValue<OptionSetValue>("al_finaloutcome");
            if (final == null)
            {
                var route = row.GetAttributeValue<EntityReference>("al_reviewrouteid");
                return route != null && route.Name == "Tax only"
                    ? "Not applicable — a Tax-only case carries no advice quality grade"
                    : "Not recorded yet";
            }

            var text = labels.Label("al_outcome", "al_finaloutcome", final.Value);
            var on = outcome.GetAttributeValue<DateTime?>("al_regradedon")
                ?? outcome.GetAttributeValue<DateTime?>("al_finalisedon");
            return on.HasValue ? text + " · " + Day(on) : text;
        }

        // ------------------------------------------------------------------ the page's wording

        /// <summary>
        /// The grade each action was raised for, read from "... submitted (Insufficient
        /// evidence)" in its description and joined across actions, as the page reads it.
        /// </summary>
        private static string Outcome(List<Entity> actions)
        {
            var reasons = new List<string>();
            foreach (var action in actions)
            {
                var description = action.GetAttributeValue<string>("al_description") ?? string.Empty;
                if (description.IndexOf("submitted (", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                var open = description.LastIndexOf('(');
                var close = open < 0 ? -1 : description.IndexOf(')', open);
                if (open < 0 || close < 0)
                {
                    continue;
                }

                var reason = description.Substring(open + 1, close - open - 1).Trim();
                if (reason.Length > 0 && !reasons.Contains(reason))
                {
                    reasons.Add(reason);
                }
            }

            return reasons.Count == 0 ? null : string.Join("; ", reasons.ToArray());
        }

        /// <summary>The issues an action lists, one per "- " line of its description.</summary>
        private static List<string> Issues(string description)
        {
            var issues = new List<string>();
            foreach (var raw in (description ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    issues.Add(line.Substring(2).Trim());
                }
            }

            return issues;
        }

        private static string SignOff(Entity latest, DateTime? completed, OptionLabels labels)
        {
            if (latest != null)
            {
                var decision = latest.GetAttributeValue<OptionSetValue>("al_signoffdecision");
                var label = decision == null ? string.Empty : labels.Label("al_signoff", "al_signoffdecision", decision.Value);
                return (label + " " + Day(latest.GetAttributeValue<DateTime?>("al_signedoffon"))).Trim();
            }

            return completed.HasValue
                ? "Adviser completed " + Day(completed) + "; awaiting supervisor"
                : "—";
        }

        private static string Supervisor(Entity signoff, OptionLabels labels)
        {
            if (signoff == null)
            {
                return "—";
            }

            var decision = signoff.GetAttributeValue<OptionSetValue>("al_signoffdecision");
            var parts = new List<string>();
            if (decision != null)
            {
                parts.Add(labels.Label("al_signoff", "al_signoffdecision", decision.Value));
            }

            var by = signoff.GetAttributeValue<string>("al_signedbyname");
            if (!string.IsNullOrWhiteSpace(by))
            {
                parts.Add(by.Trim());
            }

            parts.Add(Day(signoff.GetAttributeValue<DateTime?>("al_signedoffon")));
            return string.Join(", ", parts.ToArray());
        }

        private static string Adviser(Entity action, Entity row)
        {
            if (action == null)
            {
                return "—";
            }

            var owner = action.GetAttributeValue<EntityReference>("al_assignedcontactid");
            var name = owner != null && !string.IsNullOrWhiteSpace(owner.Name)
                ? owner.Name
                : row.GetAttributeValue<string>("al_advisername");
            return (name ?? string.Empty) + ", " + Day(action.GetAttributeValue<DateTime?>("al_completedon"));
        }

        private static string Choice(Entity action, string attribute, OptionLabels labels, string fallback)
        {
            var value = action == null ? null : action.GetAttributeValue<OptionSetValue>(attribute);
            return value == null ? fallback : labels.Label("al_remediationaction", attribute, value.Value);
        }

        /// <summary>
        /// Working days, Monday to Friday, from the day the action opened to the day it was
        /// completed or today, counting both - the page's workingDaysBetween, exactly.
        /// </summary>
        public static string Age(DateTime opened, DateTime until)
        {
            var from = opened.Date;
            var to = until.Date;
            var count = 0;
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday)
                {
                    count++;
                }
            }

            return count + (count == 1 ? " working day" : " working days");
        }

        private static PdfCell Stacked(string label, string value)
        {
            var cell = new PdfCell();
            cell.Runs.Add(PdfRun.Of(label, true));
            cell.Runs.Add(PdfRun.LineBreak());
            cell.Runs.Add(PdfRun.Of(string.IsNullOrWhiteSpace(value) ? "—" : value.Trim()));
            return cell;
        }

        private static string Text(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        }

        private static string Day(DateTime? value)
        {
            return value.HasValue ? CompletedCheck.Day(value) : "—";
        }
    }
}
