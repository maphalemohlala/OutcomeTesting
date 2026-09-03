using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The PP-15 outbox (AD-035, OD-030). The rules that matter here are the ones that
    /// decide whether a person gets one email, two, or none: the deterministic code that
    /// makes a retry collide instead of queueing again, and the resting status.
    /// </summary>
    public class NotificationOutboxTests
    {
        private static readonly Guid Target = Guid.Parse("11111111-2222-4222-8222-333333333333");
        private static readonly Guid Correlation = Guid.Parse("44444444-5555-4555-8555-666666666666");

        private static FakeOrganizationService Queue(int eventValue, Guid target, string email = "a@example.com")
        {
            var service = new FakeOrganizationService();
            NotificationOutbox.Queue(service, Correlation, eventValue, "al_outcomecase", target, email, "Subject", "Body");
            return service;
        }

        [Fact]
        public void Writes_one_row_at_pending()
        {
            // Pending is the safe resting state: the outbox records that the event happened
            // and nothing claims an email was sent that was not (the drain is not built,
            // because server-side email needs a mailbox nobody has confirmed).
            var service = Queue(NotificationOutbox.EventAllocation, Target);

            var row = Assert.Single(service.Creates);
            Assert.Equal("al_notification", row.LogicalName);
            Assert.Equal(NotificationOutbox.StatusPending, row.GetAttributeValue<OptionSetValue>("al_status").Value);
        }

        [Fact]
        public void Records_the_event_and_what_it_happened_to()
        {
            var row = Assert.Single(Queue(NotificationOutbox.EventSignoffRejected, Target).Creates);

            Assert.Equal(NotificationOutbox.EventSignoffRejected, row.GetAttributeValue<OptionSetValue>("al_event").Value);
            Assert.Equal("al_outcomecase", row.GetAttributeValue<string>("al_targettable"));
            Assert.Equal(Target.ToString("D"), row.GetAttributeValue<string>("al_targetid"));
        }

        [Fact]
        public void Carries_the_correlation_id_so_the_row_ties_back_to_its_command()
        {
            var row = Assert.Single(Queue(NotificationOutbox.EventAllocation, Target).Creates);

            Assert.Equal(Correlation.ToString("D"), row.GetAttributeValue<string>("al_correlationid"));
        }

        [Fact]
        public void Gives_the_same_event_on_the_same_target_the_same_code()
        {
            // This is the whole duplicate-proofing: the code is the table's alternate key,
            // so a command that runs twice collides rather than queueing a second email.
            Assert.Equal(
                NotificationOutbox.CodeFor(NotificationOutbox.EventAllocation, Target),
                NotificationOutbox.CodeFor(NotificationOutbox.EventAllocation, Target));
        }

        [Fact]
        public void Gives_different_events_on_one_target_different_codes()
        {
            // An approval and a rejection on the same action are two things a person must
            // hear about. Keying on the target alone would silently drop the second.
            Assert.NotEqual(
                NotificationOutbox.CodeFor(NotificationOutbox.EventSignoffApproved, Target),
                NotificationOutbox.CodeFor(NotificationOutbox.EventSignoffRejected, Target));
        }

        [Fact]
        public void Gives_the_same_event_on_different_targets_different_codes()
        {
            Assert.NotEqual(
                NotificationOutbox.CodeFor(NotificationOutbox.EventAllocation, Target),
                NotificationOutbox.CodeFor(NotificationOutbox.EventAllocation, Guid.NewGuid()));
        }

        [Fact]
        public void Omits_the_recipient_rather_than_writing_an_empty_one()
        {
            // "Review submitted" has no decided recipient (OD-030). An absent column is a
            // visible gap; an empty string reads as an address that failed to resolve.
            var service = new FakeOrganizationService();
            NotificationOutbox.Queue(service, Correlation, NotificationOutbox.EventReviewSubmitted,
                "al_reviewinstance", Target, null, "Subject", "Body");

            Assert.False(Assert.Single(service.Creates).Contains("al_recipientemail"));
        }

        [Fact]
        public void Keeps_a_recipient_when_the_record_names_one()
        {
            var row = Assert.Single(Queue(NotificationOutbox.EventAllocation, Target, "checker@example.com").Creates);

            Assert.Equal("checker@example.com", row.GetAttributeValue<string>("al_recipientemail"));
        }

        [Fact]
        public void Truncates_a_subject_to_what_the_column_holds()
        {
            var service = new FakeOrganizationService();
            NotificationOutbox.Queue(service, Correlation, NotificationOutbox.EventAllocation,
                "al_outcomecase", Target, "a@example.com", new string('x', 900), "Body");

            Assert.True(Assert.Single(service.Creates).GetAttributeValue<string>("al_subject").Length <= 400);
        }

        [Fact]
        public void Names_every_event_it_can_queue()
        {
            var values = new[]
            {
                NotificationOutbox.EventAllocation,
                NotificationOutbox.EventReviewSubmitted,
                NotificationOutbox.EventRemediationAssigned,
                NotificationOutbox.EventSignoffApproved,
                NotificationOutbox.EventSignoffRejected,
            };

            foreach (var value in values)
            {
                Assert.NotEqual("Unknown", NotificationOutbox.EventName(value));
            }

            // Five, not nine. PP-15 names nine but only five are enumerated anywhere
            // (OD-030 gap (a)); the option set carries what has actually been decided.
            Assert.Equal(5, values.Distinct().Count());
            Assert.Equal("Unknown", NotificationOutbox.EventName(120910899));
        }

        // ------------------------------------------------------------------
        // Para-planner routing for Review submitted (BR-009, OD-030(ii)).
        //
        // The case names the para-planner but holds no address for them, so the name is
        // matched against Contact. Every test below is about the same question: is this
        // match certain enough to send a client's advice outcome to?
        // ------------------------------------------------------------------

        private static readonly Guid CaseId = Guid.Parse("77777777-8888-4888-8888-999999999999");

        private static FakeOrganizationService WithCase(string paraplanner)
        {
            var service = new FakeOrganizationService();
            service.Seed("al_outcomecase", CaseId, "al_paraplanner", paraplanner);
            return service;
        }

        private static void SeedContact(FakeOrganizationService service, string fullName, string email)
        {
            service.Seed("contact", Guid.NewGuid(),
                "fullname", fullName, "emailaddress1", email, "statecode", new OptionSetValue(0));
        }

        private static EntityReference Case()
        {
            return new EntityReference("al_outcomecase", CaseId);
        }

        [Fact]
        public void Resolves_the_paraplanner_named_on_the_case_to_their_email()
        {
            // BR-009 in one line: the para-planner is reached as a Contact, so they are
            // notified without a licence, a web role or any access to the case.
            var service = WithCase("Sam Paraplanner");
            SeedContact(service, "Sam Paraplanner", "sam@example.com");

            Assert.Equal("sam@example.com", NotificationOutbox.ParaplannerEmail(service, Case()));
        }

        [Fact]
        public void Ignores_surrounding_whitespace_on_the_imported_name()
        {
            var service = WithCase("  Sam Paraplanner  ");
            SeedContact(service, "Sam Paraplanner", "sam@example.com");

            Assert.Equal("sam@example.com", NotificationOutbox.ParaplannerEmail(service, Case()));
        }

        [Fact]
        public void Refuses_to_choose_between_two_people_of_the_same_name()
        {
            // The reason this is a null and not a best guess: sending a client's advice
            // outcome to the wrong para-planner is a data-protection incident, and an
            // unrouted row is an operational one.
            var service = WithCase("J Smith");
            SeedContact(service, "J Smith", "first@example.com");
            SeedContact(service, "J Smith", "second@example.com");

            Assert.Null(NotificationOutbox.ParaplannerEmail(service, Case()));
        }

        [Fact]
        public void Returns_nothing_when_no_contact_carries_that_name()
        {
            var service = WithCase("Nobody Here");

            Assert.Null(NotificationOutbox.ParaplannerEmail(service, Case()));
        }

        [Fact]
        public void Returns_nothing_when_the_matched_contact_has_no_work_email()
        {
            var service = WithCase("Sam Paraplanner");
            service.Seed("contact", Guid.NewGuid(),
                "fullname", "Sam Paraplanner", "statecode", new OptionSetValue(0));

            Assert.Null(NotificationOutbox.ParaplannerEmail(service, Case()));
        }

        [Fact]
        public void Skips_a_deactivated_contact()
        {
            // A para-planner who has left. Their old mailbox is not where a live case
            // outcome should go, and an inactive row must not make the match look ambiguous
            // either — the active namesake below still resolves.
            var service = WithCase("Sam Paraplanner");
            service.Seed("contact", Guid.NewGuid(), "fullname", "Sam Paraplanner",
                "emailaddress1", "left@example.com", "statecode", new OptionSetValue(1));
            SeedContact(service, "Sam Paraplanner", "current@example.com");

            Assert.Equal("current@example.com", NotificationOutbox.ParaplannerEmail(service, Case()));
        }

        [Fact]
        public void Returns_nothing_when_the_case_names_no_paraplanner()
        {
            var service = WithCase(null);
            SeedContact(service, "Sam Paraplanner", "sam@example.com");

            Assert.Null(NotificationOutbox.ParaplannerEmail(service, Case()));
        }

        [Fact]
        public void Returns_nothing_when_there_is_no_case()
        {
            Assert.Null(NotificationOutbox.ParaplannerEmail(new FakeOrganizationService(), null));
        }
    }
}
