using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// People on a case are identified by email, never by name (owner, 2026-10-02: "two
    /// people can have the same name"). PROD case 256497798 named "Adam Strumidlo"; his contact
    /// was onboarded as "Adam Strumdlio", so a name match found nobody and his remediation was
    /// raised unassigned. His email was right everywhere.
    /// </summary>
    public class EmailIdentityTests
    {
        private static readonly Guid CaseId = Guid.Parse("e1e1e1e1-0000-4000-8000-000000000001");
        private static readonly Guid AdamId = Guid.Parse("e1e1e1e1-0000-4000-8000-000000000002");
        private static readonly Guid OtherAdamId = Guid.Parse("e1e1e1e1-0000-4000-8000-000000000003");
        private static readonly Guid Correlation = Guid.NewGuid();

        private static FakeOrganizationService Case(string name, string email, string paraName = null, string paraEmail = null)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-EMAIL-1",
                "al_advisername", name,
                "al_adviseremail", email,
                "al_paraplanner", paraName,
                "al_paraplanneremail", paraEmail,
                "al_clientname", "Mr Client");
            return svc;
        }

        private static void Contact(FakeOrganizationService svc, Guid id, string fullname, string email, int state = 0)
        {
            svc.Seed("contact", id,
                "fullname", fullname,
                "emailaddress1", email,
                "statecode", new OptionSetValue(state));
        }

        private static EntityReference Ref()
        {
            return new EntityReference("al_outcomecase", CaseId);
        }

        private static Guid OpenAction(FakeOrganizationService svc, Guid? holder)
        {
            var row = svc.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", Ref(),
                "al_actionstatus", new OptionSetValue(Remediation.StatusOpen),
                "al_name", "Remediation IO-EMAIL-1");
            if (holder.HasValue)
            {
                row["al_assignedcontactid"] = new EntityReference("contact", holder.Value);
            }

            return row.Id;
        }

        [Fact]
        public void A_misspelled_contact_name_does_not_matter_when_the_email_matches()
        {
            var svc = Case("Adam Strumidlo", "adam.strumidlo@example.com");
            Contact(svc, AdamId, "Adam Strumdlio", "adam.strumidlo@example.com");

            Assert.Equal(AdamId, Remediation.AdviserContact(svc, Ref()).Id);
        }

        [Fact]
        public void Two_people_with_one_name_are_told_apart_by_email()
        {
            var svc = Case("Adam Smith", "adam.smith2@example.com");
            Contact(svc, AdamId, "Adam Smith", "adam.smith@example.com");
            Contact(svc, OtherAdamId, "Adam Smith", "adam.smith2@example.com");

            Assert.Equal(OtherAdamId, Remediation.AdviserContact(svc, Ref()).Id);
        }

        [Fact]
        public void A_case_with_no_adviser_email_resolves_to_nobody_even_when_the_name_is_unique()
        {
            var svc = Case("Sam Adviser", null);
            Contact(svc, AdamId, "Sam Adviser", "sam@example.com");

            Assert.Null(Remediation.AdviserContact(svc, Ref()));
            Assert.Equal(NotificationOutbox.PersonMatchKind.NoEmail,
                NotificationOutbox.MatchAdviser(svc, null).Kind);
        }

        [Fact]
        public void Spaces_around_the_stored_email_are_ignored()
        {
            var svc = Case("Sam Adviser", "  sam@example.com ");
            Contact(svc, AdamId, "Sam Adviser", "sam@example.com");

            Assert.Equal(AdamId, Remediation.AdviserContact(svc, Ref()).Id);
        }

        [Fact]
        public void Two_contacts_with_one_email_resolve_to_nobody()
        {
            var svc = Case("Sam Adviser", "sam@example.com");
            Contact(svc, AdamId, "Sam Adviser", "sam@example.com");
            Contact(svc, OtherAdamId, "Sam A", "sam@example.com");

            var match = NotificationOutbox.MatchAdviser(svc, "sam@example.com");
            Assert.Equal(NotificationOutbox.PersonMatchKind.Ambiguous, match.Kind);
            Assert.Contains("sam@example.com", match.Reason);
            Assert.Null(Remediation.AdviserContact(svc, Ref()));
        }

        [Fact]
        public void An_inactive_contact_is_nobody()
        {
            var svc = Case("Sam Adviser", "sam@example.com");
            Contact(svc, AdamId, "Sam Adviser", "sam@example.com", state: 1);

            Assert.Equal(NotificationOutbox.PersonMatchKind.NoContact,
                NotificationOutbox.MatchAdviser(svc, "sam@example.com").Kind);
        }

        [Fact]
        public void The_pass_letter_goes_to_the_stored_email_even_with_no_contact()
        {
            var svc = Case("Sam Adviser", "sam@example.com");

            NotificationEmitterPlugin.QueueCasePassed(svc, Correlation, Ref());

            var queued = svc.Creates.Single(c => c.Contains("al_event"));
            Assert.Equal("sam@example.com", queued.GetAttributeValue<string>("al_recipientemail"));
        }

        [Fact]
        public void The_pass_letter_is_not_addressed_by_name()
        {
            var svc = Case("Sam Adviser", null);
            Contact(svc, AdamId, "Sam Adviser", "sam@example.com");

            NotificationEmitterPlugin.QueueCasePassed(svc, Correlation, Ref());

            Assert.False(svc.Creates.Single(c => c.Contains("al_event")).Contains("al_recipientemail"));
        }

        [Fact]
        public void TC_routing_does_not_fall_back_to_the_name()
        {
            var svc = Case("Sam Adviser", null);
            Contact(svc, AdamId, "Sam Adviser", "sam@example.com");

            Assert.Equal(TcManagerRouting.RoutingKind.NoAdviserEmail,
                TcManagerRouting.ForCase(svc, Ref()).Kind);
        }

        [Fact]
        public void TC_routing_ignores_an_inactive_mapping()
        {
            var svc = Case("Sam Adviser", "sam@example.com");
            Contact(svc, AdamId, "Pat Manager", "pat@example.com");
            svc.Seed("al_advisermapping", Guid.NewGuid(),
                "al_adviseremail", "sam@example.com",
                "al_tcmanagerid", new EntityReference("contact", AdamId),
                "statecode", new OptionSetValue(1));

            Assert.Equal(TcManagerRouting.RoutingKind.NoMapping,
                TcManagerRouting.ForCase(svc, Ref()).Kind);
        }

        [Fact]
        public void The_paraplanner_letter_uses_the_stored_email_and_never_the_name()
        {
            var withEmail = Case("Sam Adviser", "sam@example.com", "Pip Planner", "pip@example.com");
            Assert.Equal("pip@example.com", NotificationOutbox.ParaplannerEmail(withEmail, Ref()));

            var withoutEmail = Case("Sam Adviser", "sam@example.com", "Pip Planner", null);
            Contact(withoutEmail, AdamId, "Pip Planner", "pip@example.com");
            Assert.Null(NotificationOutbox.ParaplannerEmail(withoutEmail, Ref()));
        }

        [Fact]
        public void AssignOpenActions_moves_open_actions_to_the_contact_with_the_case_email()
        {
            var svc = Case("Adam Strumidlo", "adam.strumidlo@example.com");
            Contact(svc, AdamId, "Adam Strumdlio", "adam.strumidlo@example.com");
            var action = OpenAction(svc, null);

            Assert.Equal(1, Remediation.AssignOpenActions(svc, Ref(), Correlation));
            Assert.Equal(AdamId, svc.Row("al_remediationaction", action)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void AssignOpenActions_unassigns_when_the_email_matches_nobody()
        {
            // The adviser changed to someone not yet onboarded. Leaving the action with the
            // previous adviser would let the wrong person answer it.
            var svc = Case("New Adviser", "new@example.com");
            var action = OpenAction(svc, OtherAdamId);

            Assert.Equal(1, Remediation.AssignOpenActions(svc, Ref(), Correlation));
            Assert.Null(svc.Row("al_remediationaction", action)
                .GetAttributeValue<EntityReference>("al_assignedcontactid"));
            Assert.DoesNotContain(svc.Creates, c => c.Contains("al_event"));
        }

        [Fact]
        public void AssignOpenActions_never_moves_a_completed_action()
        {
            var svc = Case("New Adviser", "new@example.com");
            var done = svc.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", Ref(),
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                "al_assignedcontactid", new EntityReference("contact", OtherAdamId)).Id;

            Assert.Equal(0, Remediation.AssignOpenActions(svc, Ref(), Correlation));
            Assert.Equal(OtherAdamId, svc.Row("al_remediationaction", done)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Theory]
        [InlineData("sam@example.com", true)]
        [InlineData("  sam.o'neil@sub.example.co.uk ", true)]
        [InlineData("Sam Adviser", false)]
        [InlineData("sam@", false)]
        [InlineData("sam@example", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsEmail_accepts_one_address_only(string value, bool expected)
        {
            Assert.Equal(expected, CasePeople.IsEmail(value));
        }
    }
}
