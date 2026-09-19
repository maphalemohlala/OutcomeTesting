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
    }
}
