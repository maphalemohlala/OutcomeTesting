using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The letter an adviser gets on an approval says where the case actually went.
    ///
    /// <para>
    /// F40, found in DEV on 2026-09-20 working APP-072. AD-138 lets the adviser say the
    /// remediation needs no checking again; the case then closes on the approval instead of
    /// stopping at Awaiting Recheck. Live on case 900000006 the case reached Closed
    /// (120910591) and the adviser was sent: "Your remediation on case 900000006 has been
    /// approved and the case has moved on to recheck. Notes: Both points evidenced. No
    /// recheck needed." The letter contradicts the case, and contradicts its own notes in
    /// the same sentence.
    /// </para>
    /// <para>
    /// The cause is ordering. QueueSignoffNotification ran BEFORE the waiver was evaluated,
    /// so it chose between "moved on to recheck" and "closed with a final outcome of X" on
    /// whether the supervisor had graded - the only two endings that existed when the three
    /// letters were written (2026-09-11). AD-138 added a third three days later: closed, and
    /// not graded. Nothing chose a letter for it, so it fell to the recheck one by default.
    /// </para>
    /// <para>
    /// This is the harm the three-letter split was created to prevent, in its own words:
    /// saying a case "moved on to recheck" when it is finished "would send the adviser
    /// looking for a step that is not coming".
    /// </para>
    /// </summary>
    public class SignoffLetterMatchesTheCaseTests
    {
        private static readonly Guid CaseId = Guid.Parse("a1a1a1a1-0001-4001-8001-a1a1a1a1a1a1");
        private static readonly Guid ActionId = Guid.Parse("a2a2a2a2-0002-4002-8002-a2a2a2a2a2a2");
        private static readonly Guid ReviewId = Guid.Parse("a3a3a3a3-0003-4003-8003-a3a3a3a3a3a3");
        private static readonly Guid ContactId = Guid.Parse("a4a4a4a4-0004-4004-8004-a4a4a4a4a4a4");

        /// <summary>
        /// A case at Awaiting Sign-off with one completed action, ready for the decision.
        /// <paramref name="recheckRequired"/> is the adviser's own answer (AD-095).
        /// </summary>
        private static FakeOrganizationService AwaitingSignoff(int recheckRequired)
        {
            return AwaitingSignoff(recheckRequired, withOutcome: true);
        }

        private static FakeOrganizationService AwaitingSignoff(int recheckRequired, bool withOutcome)
        {
            var svc = new FakeOrganizationService();

            svc.Seed(
                "al_outcomecase", CaseId,
                "al_casereference", "F40-0001",
                "al_casestatus", new OptionSetValue(CaseLifecycle.AwaitingSignoff));

            svc.Seed(
                "contact", ContactId,
                "emailaddress1", "adviser@example.invalid",
                "fullname", "An Adviser");

            // Submitted, because an AQS review still open would send the case back to the
            // queue instead of on to recheck - the stranded-route rule, and not what this
            // asks about.
            svc.Seed(
                "al_reviewinstance", ReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusSubmitted),
                "al_submittedon", new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(0));

            svc.Seed(
                "al_remediationaction", ActionId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                "al_assignedcontactid", new EntityReference("contact", ContactId),
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                "al_recheckrequired", new OptionSetValue(recheckRequired),
                "statecode", new OptionSetValue(0));

            // A graded case. Without an Outcome MoveCase closes the case on its own - that is
            // the remediated Tax-only route, which has no final grade to set at recheck - and
            // the waiver below would never be the reason for anything.
            if (withOutcome)
            {
                svc.Seed(
                    "al_outcome", Guid.NewGuid(),
                    "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                    "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                    "al_initialoutcome", new OptionSetValue(120910701),
                    "statecode", new OptionSetValue(0));
            }

            return svc;
        }

        /// <summary>Records the approval the way a create on al_signoff reaches the progress plug-in.</summary>
        private static void Approve(FakeOrganizationService svc)
        {
            var signoff = new Entity("al_signoff")
            {
                Id = Guid.NewGuid(),
                ["al_remediationactionid"] = new EntityReference("al_remediationaction", ActionId),
                ["al_outcomecaseid"] = new EntityReference("al_outcomecase", CaseId),
                ["al_signoffdecision"] = new OptionSetValue(SignoffProgressPlugin.DecisionApprovedValue),
                ["al_notes"] = "Both points evidenced. No recheck needed.",
                ["statecode"] = new OptionSetValue(0),
            };

            svc.Seed(
                "al_signoff", signoff.Id,
                "al_remediationactionid", signoff["al_remediationactionid"],
                "al_outcomecaseid", signoff["al_outcomecaseid"],
                "al_signoffdecision", signoff["al_signoffdecision"],
                "al_notes", signoff["al_notes"],
                "statecode", signoff["statecode"]);

            SignoffProgressPlugin.Progress(svc, new FakePluginExecutionContext(), signoff);
        }

        private static int CaseStatus(FakeOrganizationService svc)
        {
            return svc.Retrieve("al_outcomecase", CaseId, new ColumnSet("al_casestatus"))
                .GetAttributeValue<OptionSetValue>("al_casestatus").Value;
        }

        private static Entity[] LettersTo(FakeOrganizationService svc, string email)
        {
            var query = new QueryExpression("al_notification")
            {
                ColumnSet = new ColumnSet("al_subject", "al_body", "al_recipientemail"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_recipientemail", ConditionOperator.Equal, email);

            return svc.RetrieveMultiple(query).Entities.ToArray();
        }

        /// <summary>
        /// The one that was wrong. The adviser waived the recheck, so the case closes - and
        /// the letter must not promise a step that is not coming.
        /// </summary>
        [Fact]
        public void A_waived_recheck_closes_the_case_and_the_letter_does_not_mention_recheck()
        {
            var svc = AwaitingSignoff(Remediation.RecheckRequiredNo);

            Approve(svc);

            Assert.Equal(CaseLifecycle.Closed, CaseStatus(svc));

            var letters = LettersTo(svc, "adviser@example.invalid");
            var body = string.Join(" | ", letters.Select(l => l.GetAttributeValue<string>("al_body")));

            Assert.NotEmpty(letters);
            Assert.DoesNotContain("moved on to recheck", body);
            Assert.Contains("closed", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// And the ending that was always right stays right: no waiver means a recheck really
        /// is coming, and the adviser should be told so.
        /// </summary>
        [Fact]
        public void A_recheck_that_is_still_owed_is_still_announced()
        {
            var svc = AwaitingSignoff(Remediation.RecheckRequiredYes);

            Approve(svc);

            Assert.Equal(CaseLifecycle.AwaitingRecheck, CaseStatus(svc));

            var body = string.Join(
                " | ",
                LettersTo(svc, "adviser@example.invalid")
                    .Select(l => l.GetAttributeValue<string>("al_body")));

            Assert.Contains("recheck", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The other way an approval closes a case, and the reason the letter is chosen by
        /// reading the case rather than by a flag set in the waiver branch: a case carrying no
        /// Outcome has no final grade to set at recheck, so MoveCase closes it itself. The
        /// adviser must not be told to expect a recheck there either.
        /// </summary>
        [Fact]
        public void A_case_closed_for_having_no_outcome_is_not_told_to_expect_a_recheck()
        {
            var svc = AwaitingSignoff(Remediation.RecheckRequiredYes, withOutcome: false);

            Approve(svc);

            Assert.Equal(CaseLifecycle.Closed, CaseStatus(svc));

            var body = string.Join(
                " | ",
                LettersTo(svc, "adviser@example.invalid")
                    .Select(l => l.GetAttributeValue<string>("al_body")));

            Assert.DoesNotContain("moved on to recheck", body);
            Assert.Contains("closed", body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
