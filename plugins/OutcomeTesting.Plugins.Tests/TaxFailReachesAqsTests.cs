using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A Tax check that did not pass must still reach the AQS review (AD-157, 4d0cf0d).
    ///
    /// <para>
    /// F38, found in DEV on 2026-09-20 working APP-051 and APP-061. The September change
    /// deferred a Tax fail's remediation: the Tax submit stamps al_taxoutcome, raises
    /// nothing and returns the case to the queue, and the AQS submit gathers both checks
    /// into ONE set of actions. Its message retired OD-038 in as many words - "it said a
    /// case had to pass through remediation before it could reach AQS, and on this route
    /// there is no longer any remediation before AQS" - but the gate enforcing OD-038 was
    /// left standing in EnsureTaxPrecedesAqs.
    /// </para>
    /// <para>
    /// The two rules deadlock. The gate refuses the AQS submit until the Tax review's
    /// remediation is approved; the deferral means the Tax review raises no remediation at
    /// all; and the only thing that would ever raise it is the AQS submit the gate is
    /// refusing. A Tax-then-AQS case whose Tax check fails cannot be finished by anybody.
    /// Live in DEV on case 900000006: "PRECONDITION: The tax check on this case did not
    /// pass and its remediation has not been signed off, so the case cannot proceed to an
    /// AQS review yet (OD-027, OD-038)." - with zero actions on the case to sign off.
    /// </para>
    /// <para>
    /// The thirteen tests that came with the deferral all passed, because each one asked
    /// either what the Tax submit does or what the AQS submit does, and the deadlock is
    /// only visible when one follows the other. These drive both in sequence.
    /// </para>
    /// </summary>
    public class TaxFailReachesAqsTests
    {
        private static readonly Guid CaseId = Guid.Parse("f1f1f1f1-0001-4001-8001-f1f1f1f1f1f1");
        private static readonly Guid RouteId = Guid.Parse("f2f2f2f2-0002-4002-8002-f2f2f2f2f2f2");
        private static readonly Guid TaxReviewId = Guid.Parse("f3f3f3f3-0003-4003-8003-f3f3f3f3f3f3");
        private static readonly Guid AqsReviewId = Guid.Parse("f4f4f4f4-0004-4004-8004-f4f4f4f4f4f4");
        private static readonly Guid ChecklistVersionId = Guid.Parse("f5f5f5f5-0005-4005-8005-f5f5f5f5f5f5");

        /// <summary>
        /// A Tax-then-AQS case with both reviews open, the Tax outcome answered and the AQS
        /// grade answered. No mandatory question versions are seeded, so the checklist owes
        /// nothing beyond the two answers the finalise path reads by code.
        /// </summary>
        private static FakeOrganizationService Case(int taxAnswer, int aqsGrade)
        {
            var svc = new FakeOrganizationService();

            svc.Seed(
                "al_reviewroute", RouteId,
                "al_requirestaxreview", true,
                "al_requiresaqsreview", true);

            svc.Seed(
                "al_outcomecase", CaseId,
                "al_casereference", "F38-0001",
                "al_casestatus", new OptionSetValue(CaseLifecycle.ReviewInProgress),
                "al_adviseremail", "adviser@example.invalid",
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId));

            svc.Seed("al_checklistversion", ChecklistVersionId);

            Review(svc, TaxReviewId, ResponseRules.ReviewTypeTax, 1);
            Review(svc, AqsReviewId, ResponseRules.ReviewTypeAqs, 2);

            Answer(svc, TaxReviewId, "Q-TAX-02", taxAnswer);
            Answer(svc, AqsReviewId, GradingRules.GradeQuestionCode, aqsGrade);

            return svc;
        }

        private static void Review(FakeOrganizationService svc, Guid id, int reviewType, int sequence)
        {
            svc.Seed(
                "al_reviewinstance", id,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(reviewType),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusInProgress),
                "al_checklistversionid", new EntityReference("al_checklistversion", ChecklistVersionId),
                "al_sequence", sequence,
                "statecode", new OptionSetValue(0));
        }

        private static void Answer(
            FakeOrganizationService svc, Guid reviewId, string questionCode, int choice)
        {
            var questionId = Guid.NewGuid();
            var versionId = Guid.NewGuid();

            svc.Seed("al_question", questionId, "al_questioncode", questionCode);
            svc.Seed(
                "al_questionversion", versionId,
                "al_questionid", new EntityReference("al_question", questionId));

            svc.Seed(
                "al_response", Guid.NewGuid(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", versionId),
                "al_answerchoice", new OptionSetValue(choice),
                "statecode", new OptionSetValue(0));
        }

        private static void Submit(FakeOrganizationService svc, Guid reviewId)
        {
            SubmitReviewPlugin.Submit(
                svc,
                reviewId,
                "f38-" + Guid.NewGuid().ToString("N"),
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                requireCallerOwnsReview: false,
                details: "tax fail reaches aqs");
        }

        private static Entity[] ActionsOn(FakeOrganizationService svc)
        {
            var query = new QueryExpression("al_remediationaction")
            {
                ColumnSet = new ColumnSet("al_description", "al_reviewinstanceid"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, CaseId);

            return svc.RetrieveMultiple(query).Entities.ToArray();
        }

        /// <summary>
        /// The AQS checker picking the case up out of the queue. In the product this is
        /// <c>al_AssignCase</c> or <c>al_ClaimCase</c>, which open the AQS review and move the
        /// case in one go; here the review is already seeded, so only the move is needed. It
        /// is not optional scenery: a Tax submit leaves the case at Queued, and
        /// <c>OutcomeRules.HopsFor</c> routes a submit through Submitted, which AD-057 does
        /// not allow from Queued. Nothing can submit an AQS review that nobody picked up.
        /// </summary>
        private static void PickUp(FakeOrganizationService svc)
        {
            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);
        }

        private static int CaseStatus(FakeOrganizationService svc)
        {
            return svc.Retrieve("al_outcomecase", CaseId, new ColumnSet("al_casestatus"))
                .GetAttributeValue<OptionSetValue>("al_casestatus").Value;
        }

        /// <summary>
        /// The Tax half, which is what APP-051 asks for: the case goes back to the queue for
        /// the AQS checker and nobody is asked to do anything yet.
        /// </summary>
        [Fact]
        public void A_tax_fail_returns_the_case_to_the_queue_and_raises_nothing()
        {
            var svc = Case(ResponseRules.ChoiceFail, ResponseRules.ChoicePassWithIssues);

            Submit(svc, TaxReviewId);

            Assert.Equal(CaseLifecycle.Queued, CaseStatus(svc));
            Assert.Empty(ActionsOn(svc));
        }

        /// <summary>
        /// The half that deadlocked. Nothing about the AQS review has changed; the only
        /// reason it could not be submitted was a rule that had been retired.
        /// </summary>
        [Fact]
        public void The_aqs_review_can_be_submitted_after_a_tax_fail()
        {
            var svc = Case(ResponseRules.ChoiceFail, ResponseRules.ChoicePassWithIssues);

            Submit(svc, TaxReviewId);
            Assert.Empty(ActionsOn(svc));
            PickUp(svc);

            // Before the fix: "PRECONDITION: The tax check on this case did not pass and its
            // remediation has not been signed off ... (OD-027, OD-038)", with no remediation
            // in existence that anybody could ever have signed off.
            Submit(svc, AqsReviewId);

            Assert.Equal(CaseLifecycle.AwaitingRemediation, CaseStatus(svc));
        }

        /// <summary>
        /// And the combined remediation the deferral exists for actually arrives - one set
        /// for the case, naming both checks. Unreachable in production while the gate stood,
        /// because the submit that raises it was the submit being refused.
        /// </summary>
        [Fact]
        public void The_aqs_submit_raises_one_set_of_actions_naming_both_checks()
        {
            var svc = Case(ResponseRules.ChoiceFail, ResponseRules.ChoicePassWithIssues);

            Submit(svc, TaxReviewId);
            PickUp(svc);
            Submit(svc, AqsReviewId);

            var actions = ActionsOn(svc);
            Assert.NotEmpty(actions);

            // Every action belongs to the AQS submit: a second set keyed on the Tax review
            // would be the two-conversations shape AD-157 removed.
            foreach (var action in actions)
            {
                Assert.Equal(
                    AqsReviewId,
                    action.GetAttributeValue<EntityReference>("al_reviewinstanceid").Id);
            }

            var described = string.Join(
                " | ",
                actions.Select(action => action.GetAttributeValue<string>("al_description") ?? string.Empty));

            Assert.Contains("Tax check", described, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The gate's own purpose is still served, by the check that was never in question:
        /// an unsubmitted Tax review still stops the AQS review (BR-004). That is the part of
        /// EnsureTaxPrecedesAqs AD-157 left alone.
        /// </summary>
        [Fact]
        public void An_unsubmitted_tax_review_still_stops_the_aqs_review()
        {
            var svc = Case(ResponseRules.ChoiceFail, ResponseRules.ChoicePassWithIssues);

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => Submit(svc, AqsReviewId));

            Assert.Contains("Tax check on this case has not been submitted", refusal.Message);
        }
    }
}
