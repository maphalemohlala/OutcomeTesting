using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The remedial actions one check raised, as the last section of that check's document
    /// (project owner, 2026-10-05: add the remediation actions to the PDF downloaded from the
    /// portal and to the one that goes out to advisers and para-planners).
    ///
    /// <para>
    /// Only this check's actions: those whose <c>al_reviewinstanceid</c> is the review. A Tax
    /// fail deferred to the AQS submit is raised against the AQS review, so it is drawn under
    /// the AQS check. The case's whole Remediation and escalation form stays its own document
    /// (<see cref="RemediationDocument"/>).
    /// </para>
    /// <para>Never throws, for the reason every attachment gives: the letter matters more.</para>
    /// </summary>
    public static class CheckRemedialActions
    {
        /// <summary>The section's heading, on the page and in the document.</summary>
        public const string Heading = "Remedial actions";

        /// <summary>
        /// The section, or an empty list when the check raised nothing or cannot be read.
        ///
        /// The whole build - the actions, the metadata labels, the rows - sits inside one
        /// try/catch, not only the query that fetches the actions: OptionLabels deliberately
        /// lets a metadata-read failure propagate (its own doc comment says so), and a catch
        /// around just the query would let that failure through as a half-built section
        /// rather than the "never throws" promise this class makes.
        /// </summary>
        public static List<PdfBlock> Blocks(IOrganizationService service, Guid reviewId)
        {
            try
            {
                var actions = Actions(service, reviewId);
                if (actions.Count == 0)
                {
                    return new List<PdfBlock>();
                }

                var labels = new OptionLabels(service);
                var table = new PdfTable(0.05, 0.3, 0.3, 0.13, 0.11, 0.11);
                table.AddHeader(
                    PdfCell.Head("No."), PdfCell.Head("Fail point"), PdfCell.Head("Remedial action"),
                    PdfCell.Head("Owner"), PdfCell.Head("Target date"), PdfCell.Head("Status"));

                var number = 0;
                foreach (var action in actions)
                {
                    var checkerAction = action.GetAttributeValue<string>(RemedialActions.ActionAttr);
                    var adviserText = action.GetAttributeValue<string>("al_adviserresponse");
                    var owner = action.GetAttributeValue<EntityReference>("al_assignedcontactid");
                    var status = action.GetAttributeValue<OptionSetValue>("al_actionstatus");

                    // The checker's words where the row has them (2026-09-29); a row raised
                    // before that carries the adviser's own remedial action, as RemediationDocument shows.
                    var details = new[]
                    {
                        RemediationDocument.Text(string.IsNullOrWhiteSpace(checkerAction) ? adviserText : checkerAction),
                        owner != null && !string.IsNullOrWhiteSpace(owner.Name) ? owner.Name : "Nobody assigned",
                        RemediationDocument.Day(action.GetAttributeValue<DateTime?>("al_duedate")),
                        status == null ? "—" : labels.Label("al_remediationaction", "al_actionstatus", status.Value),
                    };

                    var issues = RemediationDocument.Issues(action.GetAttributeValue<string>("al_description"));
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

                        // The action's own columns sit on its first issue; the rows under it
                        // belong to the same action, as on the remediation form.
                        foreach (var detail in details)
                        {
                            cells.Add(i == 0 ? PdfCell.Of(detail) : PdfCell.Blank());
                        }

                        table.Add(cells.ToArray());
                    }
                }

                var blocks = new List<PdfBlock>();
                blocks.Add(PdfBlock.Subheading(Heading));
                blocks.Add(PdfBlock.Table(table));
                return blocks;
            }
            catch (Exception)
            {
                return new List<PdfBlock>();
            }
        }

        /// <summary>The review's live actions, in the order they were raised.</summary>
        private static List<Entity> Actions(IOrganizationService service, Guid reviewId)
        {
            var query = new QueryExpression("al_remediationaction")
            {
                ColumnSet = new ColumnSet(
                    "al_description", "al_actionstatus", "al_duedate", "al_adviserresponse",
                    "al_assignedcontactid", "createdon", "al_remediationactioncode",
                    RemedialActions.ActionAttr),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.AddOrder("createdon", OrderType.Ascending);
            query.AddOrder("al_remediationactioncode", OrderType.Ascending);

            var actions = new List<Entity>(CommandHelpers.RetrieveAll(service, query));

            // The server-side order above is best effort only. Remediation.Raise writes
            // every item's action inside one transaction (Remediation.cs, the loop that
            // numbers al_remediationactioncode "...-1", "...-2", ... "...-10", "...-11"), so
            // createdon commonly ties across ten or more rows, and the code's own lexical
            // order then reads 1, 10, 2, 3... "In raise order" is guaranteed here instead,
            // in memory: createdon, then the code's trailing index read as a number (a code
            // with none sorts first), then the code string as a last resort.
            actions.Sort((left, right) =>
            {
                var byCreated = Nullable.Compare(
                    left.GetAttributeValue<DateTime?>("createdon"),
                    right.GetAttributeValue<DateTime?>("createdon"));
                if (byCreated != 0)
                {
                    return byCreated;
                }

                var leftCode = left.GetAttributeValue<string>("al_remediationactioncode");
                var rightCode = right.GetAttributeValue<string>("al_remediationactioncode");

                var byIndex = Nullable.Compare(TrailingIndex(leftCode), TrailingIndex(rightCode));
                return byIndex != 0 ? byIndex : string.CompareOrdinal(leftCode, rightCode);
            });

            return actions;
        }

        /// <summary>The integer after an action code's last '-', or null when there is none.</summary>
        private static int? TrailingIndex(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            var dash = code.LastIndexOf('-');
            if (dash < 0 || dash == code.Length - 1)
            {
                return null;
            }

            int value;
            return int.TryParse(
                code.Substring(dash + 1),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value)
                ? value
                : (int?)null;
        }
    }
}
