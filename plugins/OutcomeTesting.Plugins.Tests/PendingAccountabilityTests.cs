using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Choosing who carries a fail BEFORE the check is submitted (project owner,
    /// 2026-09-19: "when the user performs checks and the case is failed, at the end before
    /// submit button").
    ///
    /// The four flags live on al_outcome, and SubmitReviewPlugin.CreateOutcome is what
    /// creates that row - so at the moment the checker is asked, there is nothing to write
    /// to. The portal parks the choice on the review instead and the submit applies it to
    /// the outcome it is creating, in the same write as the grade.
    /// </summary>
    public class PendingAccountabilityTests
    {
        private static readonly Guid ContactId = Guid.Parse("cccccccc-2222-4222-8222-222222222222");

        private static Entity Review(string parked)
        {
            var review = new Entity("al_reviewinstance", Guid.NewGuid());
            if (parked != null)
            {
                review["al_pendingaccountability"] = parked;
            }

            return review;
        }

        private static string Parked(
            bool fqAdviser = false,
            bool fqParaplanner = false,
            bool aqAdviser = false,
            bool aqParaplanner = false,
            string fqContact = null,
            string aqContact = null)
        {
            return "{\"fqAdviser\":" + (fqAdviser ? "true" : "false")
                + ",\"fqParaplanner\":" + (fqParaplanner ? "true" : "false")
                + ",\"aqAdviser\":" + (aqAdviser ? "true" : "false")
                + ",\"aqParaplanner\":" + (aqParaplanner ? "true" : "false")
                + ",\"fqContactId\":\"" + (fqContact ?? string.Empty) + "\""
                + ",\"aqContactId\":\"" + (aqContact ?? string.Empty) + "\"}";
        }

        [Fact]
        public void Applies_the_flags_the_checker_chose()
        {
            var outcome = new Entity("al_outcome");

            var applied = AccountabilityRequestPlugin.ApplyParked(
                new FakeOrganizationService(), Review(Parked(fqAdviser: true, aqAdviser: true)), outcome);

            Assert.True(applied);
            Assert.True(outcome.GetAttributeValue<bool>("al_fqadviseraccountable"));
            Assert.True(outcome.GetAttributeValue<bool>("al_aqadviseraccountable"));
            Assert.False(outcome.GetAttributeValue<bool>("al_fqparaplanneraccountable"));
        }

        [Fact]
        public void Applies_the_person_the_checker_named()
        {
            var outcome = new Entity("al_outcome");

            AccountabilityRequestPlugin.ApplyParked(
                new FakeOrganizationService(),
                Review(Parked(fqParaplanner: true, fqContact: ContactId.ToString("D"))),
                outcome);

            var named = outcome.GetAttributeValue<EntityReference>("al_fqaccountablecontactid");
            Assert.NotNull(named);
            Assert.Equal("contact", named.LogicalName);
            Assert.Equal(ContactId, named.Id);
        }

        [Fact]
        public void Leaves_the_lookup_null_when_nobody_was_named()
        {
            // The case's own adviser or paraplanner, which is what the extract falls back to.
            var outcome = new Entity("al_outcome");

            AccountabilityRequestPlugin.ApplyParked(
                new FakeOrganizationService(), Review(Parked(fqParaplanner: true)), outcome);

            Assert.Null(outcome.GetAttributeValue<EntityReference>("al_fqaccountablecontactid"));
        }

        [Fact]
        public void Writes_both_lookups_every_time()
        {
            // Including as null. An outcome upserted over an earlier attempt must not keep a
            // name the checker has since cleared.
            var outcome = new Entity("al_outcome");

            AccountabilityRequestPlugin.ApplyParked(
                new FakeOrganizationService(), Review(Parked(aqAdviser: true)), outcome);

            Assert.True(outcome.Contains("al_fqaccountablecontactid"));
            Assert.True(outcome.Contains("al_aqaccountablecontactid"));
        }

        [Fact]
        public void Does_nothing_when_the_checker_chose_nothing()
        {
            // The ordinary case: most reviews never open the panel, and the export derives
            // the pair from the people the case names.
            var outcome = new Entity("al_outcome");

            var applied = AccountabilityRequestPlugin.ApplyParked(
                new FakeOrganizationService(), Review(null), outcome);

            Assert.False(applied);
            Assert.False(outcome.Contains("al_fqadviseraccountable"));
        }

        [Fact]
        public void Does_nothing_with_a_parked_value_it_cannot_read()
        {
            var outcome = new Entity("al_outcome");

            var applied = AccountabilityRequestPlugin.ApplyParked(
                new FakeOrganizationService(), Review("not json"), outcome);

            Assert.False(applied);
            Assert.False(outcome.Contains("al_fqadviseraccountable"));
        }

        [Fact]
        public void Refuses_a_named_person_that_is_not_an_id()
        {
            var outcome = new Entity("al_outcome");

            Assert.Throws<InvalidPluginExecutionException>(() =>
                AccountabilityRequestPlugin.ApplyParked(
                    new FakeOrganizationService(),
                    Review(Parked(fqParaplanner: true, fqContact: "Clare Hook")),
                    outcome));
        }

        [Fact]
        public void Carries_a_deferred_legs_judgement_forward_to_the_outcome()
        {
            // F50. A Tax fail on a Tax-then-AQS case defers: it stamps al_taxoutcome, queues
            // the case and creates NO outcome, so the Tax checker's answer to "who carries
            // this fail" has nothing to be written to and stays parked on the Tax review.
            // The AQS submit is the one that creates the outcome - and it used to read the
            // parked value from the review being submitted only, so the Tax checker's
            // judgement was silently dropped and the export fell back to its default, which
            // names a DIFFERENT person: the paraplanner for a File Quality fail, where this
            // checker said the adviser.
            var service = new FakeOrganizationService();
            var caseId = Guid.NewGuid();
            var taxReview = Guid.NewGuid();
            var aqsReview = Guid.NewGuid();

            service.Seed(
                "al_reviewinstance", taxReview,
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "al_submittedon", new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(0),
                "al_pendingaccountability", Parked(fqAdviser: true));

            service.Seed(
                "al_reviewinstance", aqsReview,
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "statecode", new OptionSetValue(0));

            var source = SubmitReviewPlugin.ParkedAccountability(service, aqsReview, caseId);

            Assert.NotNull(source);
            Assert.Equal(taxReview, source.Id);

            var outcome = new Entity("al_outcome");
            Assert.True(AccountabilityRequestPlugin.ApplyParked(service, source, outcome));
            Assert.True(outcome.GetAttributeValue<bool>("al_fqadviseraccountable"));
        }

        [Fact]
        public void Prefers_the_submitting_reviews_own_judgement_over_a_deferred_legs()
        {
            // Both legs opened the panel. The judgement made about THIS submission wins;
            // nothing recorded here is overwritten by an earlier leg.
            var service = new FakeOrganizationService();
            var caseId = Guid.NewGuid();
            var aqsReview = Guid.NewGuid();

            service.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "al_submittedon", new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(0),
                "al_pendingaccountability", Parked(fqAdviser: true));

            service.Seed(
                "al_reviewinstance", aqsReview,
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "statecode", new OptionSetValue(0),
                "al_pendingaccountability", Parked(aqParaplanner: true));

            var source = SubmitReviewPlugin.ParkedAccountability(service, aqsReview, caseId);

            Assert.NotNull(source);
            Assert.Equal(aqsReview, source.Id);

            var outcome = new Entity("al_outcome");
            AccountabilityRequestPlugin.ApplyParked(service, source, outcome);
            Assert.True(outcome.GetAttributeValue<bool>("al_aqparaplanneraccountable"));
            Assert.False(outcome.GetAttributeValue<bool>("al_fqadviseraccountable"));
        }

        [Fact]
        public void Does_not_reach_onto_another_case()
        {
            // The carry-forward is scoped to this case. A parked judgement on somebody
            // else's case must never attach to this outcome.
            var service = new FakeOrganizationService();
            var caseId = Guid.NewGuid();
            var aqsReview = Guid.NewGuid();

            service.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", Guid.NewGuid()),
                "al_submittedon", new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(0),
                "al_pendingaccountability", Parked(fqAdviser: true));

            service.Seed(
                "al_reviewinstance", aqsReview,
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "statecode", new OptionSetValue(0));

            Assert.Null(SubmitReviewPlugin.ParkedAccountability(service, aqsReview, caseId));
        }

        [Fact]
        public void Ignores_a_deactivated_reviews_parked_judgement()
        {
            var service = new FakeOrganizationService();
            var caseId = Guid.NewGuid();
            var aqsReview = Guid.NewGuid();

            service.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "al_submittedon", new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(1),
                "al_pendingaccountability", Parked(fqAdviser: true));

            service.Seed(
                "al_reviewinstance", aqsReview,
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "statecode", new OptionSetValue(0));

            Assert.Null(SubmitReviewPlugin.ParkedAccountability(service, aqsReview, caseId));
        }

        [Fact]
        public void Finds_nothing_when_no_leg_recorded_a_judgement()
        {
            // The ordinary case, and the one that must keep deriving: the export names the
            // paraplanner for a File Quality fail and the adviser for an Advice Quality one.
            var service = new FakeOrganizationService();
            var caseId = Guid.NewGuid();
            var aqsReview = Guid.NewGuid();

            service.Seed(
                "al_reviewinstance", aqsReview,
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "statecode", new OptionSetValue(0));

            Assert.Null(SubmitReviewPlugin.ParkedAccountability(service, aqsReview, caseId));
        }
    }
}
