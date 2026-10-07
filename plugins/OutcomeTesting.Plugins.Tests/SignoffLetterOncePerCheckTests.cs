using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// One sign-off letter per decision on a check, not one per action (reported 2026-10-06:
    /// "users have reported getting duplicate emails").
    ///
    /// <para>
    /// The portal signs a check's actions off one at a time, each its own create on
    /// <c>al_signoff</c>, and the letter was keyed on the sign-off row. On TEST on 2026-09-30
    /// case 256643210 sent its adviser twelve identical "Remediation approved" emails inside
    /// 21 seconds, one per action. The remediation letter had the same fault and was keyed on
    /// the review on 2026-09-10; the sign-off letter was not.
    /// </para>
    /// </summary>
    public class SignoffLetterOncePerCheckTests
    {
        private static readonly Guid CaseId = Guid.Parse("b1b1b1b1-0001-4001-8001-b1b1b1b1b1b1");
        private static readonly Guid ReviewId = Guid.Parse("b3b3b3b3-0003-4003-8003-b3b3b3b3b3b3");
        private static readonly Guid ContactId = Guid.Parse("b4b4b4b4-0004-4004-8004-b4b4b4b4b4b4");
        private const string AdviserEmail = "adviser@example.invalid";

        private static readonly Guid[] Actions =
        {
            Guid.Parse("b2b2b2b2-0002-4002-8002-000000000001"),
            Guid.Parse("b2b2b2b2-0002-4002-8002-000000000002"),
            Guid.Parse("b2b2b2b2-0002-4002-8002-000000000003"),
        };

        /// <summary>A graded case at Awaiting Sign-off whose check raised three completed actions.</summary>
        private static FakeOrganizationService AwaitingSignoff()
        {
            var svc = new FakeOrganizationService();

            svc.Seed(
                "al_outcomecase", CaseId,
                "al_casereference", "DUP-0001",
                "al_casestatus", new OptionSetValue(CaseLifecycle.AwaitingSignoff));

            svc.Seed(
                "contact", ContactId,
                "emailaddress1", AdviserEmail,
                "fullname", "An Adviser");

            svc.Seed(
                "al_reviewinstance", ReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusSubmitted),
                "al_submittedon", new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(0));

            foreach (var action in Actions)
            {
                svc.Seed(
                    "al_remediationaction", action,
                    "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                    "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                    "al_assignedcontactid", new EntityReference("contact", ContactId),
                    "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                    "al_recheckrequired", new OptionSetValue(Remediation.RecheckRequiredYes),
                    "statecode", new OptionSetValue(0));
            }

            svc.Seed(
                "al_outcome", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                "al_initialoutcome", new OptionSetValue(120910701),
                "statecode", new OptionSetValue(0));

            return svc;
        }

        /// <summary>One decision on one action, the way a create on al_signoff reaches the progress plug-in.</summary>
        private static void Decide(
            FakeOrganizationService svc, Guid actionId, int decision, string notes = null, DateTime? createdOn = null)
        {
            var signoff = new Entity("al_signoff")
            {
                Id = Guid.NewGuid(),
                ["al_remediationactionid"] = new EntityReference("al_remediationaction", actionId),
                ["al_outcomecaseid"] = new EntityReference("al_outcomecase", CaseId),
                ["al_signoffdecision"] = new OptionSetValue(decision),
                ["al_notes"] = notes,
                ["statecode"] = new OptionSetValue(0),
            };

            svc.Seed(
                "al_signoff", signoff.Id,
                "al_remediationactionid", signoff["al_remediationactionid"],
                "al_outcomecaseid", signoff["al_outcomecaseid"],
                "al_signoffdecision", signoff["al_signoffdecision"],
                "al_notes", notes,
                "createdon", createdOn ?? DateTime.UtcNow,
                "statecode", signoff["statecode"]);

            SignoffProgressPlugin.Progress(svc, new FakePluginExecutionContext(), signoff);
        }

        private static void SetCaseStatus(FakeOrganizationService svc, int status)
        {
            svc.Update(new Entity("al_outcomecase", CaseId) { ["al_casestatus"] = new OptionSetValue(status) });
        }

        private static Entity[] Letters(FakeOrganizationService svc, int eventValue)
        {
            var query = new QueryExpression("al_notification")
            {
                ColumnSet = new ColumnSet("al_subject", "al_recipientemail"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_recipientemail", ConditionOperator.Equal, AdviserEmail);
            query.Criteria.AddCondition("al_event", ConditionOperator.Equal, eventValue);

            return svc.RetrieveMultiple(query).Entities.ToArray();
        }

        [Fact]
        public void Approving_every_action_on_a_check_sends_the_adviser_one_letter()
        {
            var svc = AwaitingSignoff();

            foreach (var action in Actions)
            {
                Decide(svc, action, SignoffProgressPlugin.DecisionApprovedValue);
            }

            Assert.Single(Letters(svc, NotificationOutbox.EventSignoffApproved));
        }

        [Fact]
        public void No_approval_letter_goes_while_an_action_on_the_check_is_still_waiting()
        {
            // Until the last one the case has not moved, so there is nothing true to tell the
            // adviser - and "moved on to recheck" would be false.
            var svc = AwaitingSignoff();

            Decide(svc, Actions[0], SignoffProgressPlugin.DecisionApprovedValue);
            Decide(svc, Actions[1], SignoffProgressPlugin.DecisionApprovedValue);

            Assert.Empty(Letters(svc, NotificationOutbox.EventSignoffApproved));
        }

        [Fact]
        public void Rejecting_several_actions_in_one_sitting_sends_one_letter()
        {
            // The first rejection sends the check back; the rest add to a remediation the
            // adviser is already being told to rework.
            var svc = AwaitingSignoff();

            foreach (var action in Actions)
            {
                Decide(svc, action, SignoffProgressPlugin.DecisionRejectedValue);
            }

            Assert.Single(Letters(svc, NotificationOutbox.EventSignoffRejected));
        }

        [Fact]
        public void A_check_sent_back_with_the_rest_approved_gets_only_the_rejection()
        {
            var svc = AwaitingSignoff();

            Decide(svc, Actions[0], SignoffProgressPlugin.DecisionRejectedValue);
            Decide(svc, Actions[1], SignoffProgressPlugin.DecisionApprovedValue);
            Decide(svc, Actions[2], SignoffProgressPlugin.DecisionApprovedValue);

            Assert.Single(Letters(svc, NotificationOutbox.EventSignoffRejected));
            Assert.Empty(Letters(svc, NotificationOutbox.EventSignoffApproved));
        }

        [Fact]
        public void A_rejection_while_the_check_is_back_with_the_adviser_still_sends_its_letter()
        {
            // The audit's finding. On a graded fail the portal offers the sign-off panel for
            // any completed action, also while the case is at Awaiting Remediation because
            // another action is still being worked on. That rejection reopens the action, so
            // the adviser has to hear about it.
            var svc = AwaitingSignoff();
            SetCaseStatus(svc, CaseLifecycle.AwaitingRemediation);

            Decide(svc, Actions[0], SignoffProgressPlugin.DecisionRejectedValue, "Evidence missing.");

            Assert.Single(Letters(svc, NotificationOutbox.EventSignoffRejected));
        }

        [Fact]
        public void A_later_sitting_sends_its_own_rejection_letter()
        {
            var svc = AwaitingSignoff();
            Decide(svc, Actions[0], SignoffProgressPlugin.DecisionRejectedValue, "First pass.",
                DateTime.UtcNow.AddMinutes(-30));

            Decide(svc, Actions[1], SignoffProgressPlugin.DecisionRejectedValue, "First pass.");

            Assert.Equal(2, Letters(svc, NotificationOutbox.EventSignoffRejected).Length);
        }

        [Fact]
        public void A_rejection_with_different_notes_is_its_own_decision()
        {
            // The portal sends one sitting as one set of notes; different words are a
            // different decision even minutes apart.
            var svc = AwaitingSignoff();
            Decide(svc, Actions[0], SignoffProgressPlugin.DecisionRejectedValue, "Evidence missing.");

            Decide(svc, Actions[1], SignoffProgressPlugin.DecisionRejectedValue, "Client letter unsigned.");

            Assert.Equal(2, Letters(svc, NotificationOutbox.EventSignoffRejected).Length);
        }

        [Fact]
        public void Every_sign_off_takes_the_case_lock_before_anything_else()
        {
            // Two sign-offs on one check committing at the same moment cannot see each
            // other's rows, so each would count the other's action as undecided: the case
            // would never move and no approval letter would go. Writing the case row first
            // makes the second wait for the first to commit.
            var svc = AwaitingSignoff();

            Decide(svc, Actions[0], SignoffProgressPlugin.DecisionRejectedValue);

            Assert.Equal("al_outcomecase", svc.Updates.First().LogicalName);
            Assert.Equal(CaseId, svc.Updates.First().Id);
        }

        // ---- A case with a Tax check and an AQS check --------------------------------------

        private static readonly Guid AqsReviewId = Guid.Parse("b5b5b5b5-0005-4005-8005-b5b5b5b5b5b5");
        private static readonly Guid AqsActionId = Guid.Parse("b2b2b2b2-0002-4002-8002-0000000000a1");

        /// <summary>
        /// The three-action check above made the Tax check, with no grade, and an AQS review
        /// still open behind it - so approving the Tax remediation hands the case back to the
        /// queue for AQS rather than on to recheck.
        /// </summary>
        private static FakeOrganizationService TaxThenAqs(bool aqsSubmitted = false)
        {
            var svc = AwaitingSignoff();
            svc.Update(new Entity("al_reviewinstance", ReviewId)
            {
                ["al_reviewtype"] = new OptionSetValue(ResponseRules.ReviewTypeTax),
            });

            foreach (var outcome in svc.RetrieveMultiple(new QueryExpression("al_outcome")).Entities.ToList())
            {
                svc.Delete("al_outcome", outcome.Id);
            }

            svc.Seed(
                "al_reviewinstance", AqsReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_reviewstatus", new OptionSetValue(aqsSubmitted ? ResponseRules.StatusSubmitted : ResponseRules.StatusAssigned),
                "statecode", new OptionSetValue(0));
            return svc;
        }

        [Fact]
        public void A_tax_check_handed_on_to_aqs_is_not_told_it_moved_to_recheck()
        {
            var svc = TaxThenAqs();

            foreach (var action in Actions)
            {
                Decide(svc, action, SignoffProgressPlugin.DecisionApprovedValue);
            }

            var letters = Letters(svc, NotificationOutbox.EventSignoffApproved);
            Assert.Single(letters);

            var body = svc.Row("al_notification", letters[0].Id).GetAttributeValue<string>("al_body");
            Assert.DoesNotContain("recheck", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AQS check", body);
            Assert.Equal(CaseLifecycle.Queued,
                svc.Row("al_outcomecase", CaseId).GetAttributeValue<OptionSetValue>("al_casestatus").Value);
        }

        [Fact]
        public void An_aqs_check_is_not_held_back_by_the_tax_checks_actions()
        {
            // Scoped to the check: a Tax action still waiting is not the AQS check's work, so
            // approving every AQS action sends the AQS letter.
            var svc = TaxThenAqs(aqsSubmitted: true);
            svc.Seed(
                "al_remediationaction", AqsActionId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", AqsReviewId),
                "al_assignedcontactid", new EntityReference("contact", ContactId),
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                "al_recheckrequired", new OptionSetValue(Remediation.RecheckRequiredYes),
                "statecode", new OptionSetValue(0));

            Decide(svc, AqsActionId, SignoffProgressPlugin.DecisionApprovedValue);

            Assert.Single(Letters(svc, NotificationOutbox.EventSignoffApproved));
        }

        [Fact]
        public void A_reworked_check_that_is_then_approved_gets_its_approval_letter()
        {
            // A second round is a second decision, and the adviser must hear about it.
            var svc = AwaitingSignoff();
            Decide(svc, Actions[0], SignoffProgressPlugin.DecisionRejectedValue);

            // The adviser reworks the action and the check comes back for sign-off.
            svc.Update(new Entity("al_remediationaction", Actions[0])
            {
                ["al_actionstatus"] = new OptionSetValue(Remediation.StatusCompleted),
            });
            svc.Update(new Entity("al_outcomecase", CaseId)
            {
                ["al_casestatus"] = new OptionSetValue(CaseLifecycle.AwaitingSignoff),
            });

            foreach (var action in Actions)
            {
                Decide(svc, action, SignoffProgressPlugin.DecisionApprovedValue);
            }

            Assert.Single(Letters(svc, NotificationOutbox.EventSignoffRejected));
            Assert.Single(Letters(svc, NotificationOutbox.EventSignoffApproved));
        }
    }
}
