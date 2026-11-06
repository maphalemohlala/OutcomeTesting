using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Every letter names the client in its subject (2026-11-06), including letters whose
    /// event supplies no client: those read it from the case when the letter is rendered.
    /// </summary>
    public class ClientInSubjectTests
    {
        private static readonly Guid CaseId = new Guid("11111111-1111-4111-8111-111111111111");
        private static readonly EntityReference CaseRef = new EntityReference("al_outcomecase", CaseId);

        private static Dictionary<string, string> Reference()
        {
            return new Dictionary<string, string> { { NotificationTemplates.TokenReference, "IO-1" } };
        }

        [Fact]
        public void A_letter_whose_event_supplies_no_client_reads_it_from_the_case()
        {
            var service = new FakeOrganizationService();
            service.Seed("al_outcomecase", CaseId, "al_clientname", "Mr and Mrs Smith");

            var letter = NotificationTemplates.Render(
                service, NotificationTemplates.SignoffRejected, Reference(), CaseRef);

            Assert.Equal("Remediation sent back on case IO-1 - Mr and Mrs Smith", letter.Subject);
        }

        [Fact]
        public void A_case_with_no_client_leaves_no_dangling_separator()
        {
            var service = new FakeOrganizationService();
            service.Seed("al_outcomecase", CaseId);

            var letter = NotificationTemplates.Render(
                service, NotificationTemplates.SignoffRejected, Reference(), CaseRef);

            Assert.Equal("Remediation sent back on case IO-1", letter.Subject);
        }

        [Fact]
        public void A_case_that_cannot_be_read_still_gets_its_letter()
        {
            var letter = NotificationTemplates.Render(
                new FakeOrganizationService(), NotificationTemplates.SignoffRejected, Reference(), CaseRef);

            Assert.Equal("Remediation sent back on case IO-1", letter.Subject);
        }

        [Fact]
        public void The_body_opener_for_a_missing_client_stays_out_of_the_subject()
        {
            var tokens = Reference();
            tokens[NotificationTemplates.TokenClient] = NotificationTemplates.ClientOpener(null);

            var letter = NotificationTemplates.Render(
                new FakeOrganizationService(), NotificationTemplates.CasePassed, tokens);

            Assert.Equal("Case check - Pass: IO-1", letter.Subject);
        }

        [Fact]
        public void Client_is_accepted_on_a_letter_that_does_not_list_it()
        {
            Assert.Empty(NotificationTemplates.UnknownTokens(
                NotificationTemplates.SignoffRejected, "Sent back: {{client}}", "<p>Body</p>"));
        }

        [Theory]
        [InlineData("Mr and Mrs Smith", "Checks and remedial points: IO-1 - Mr and Mrs Smith")]
        [InlineData(null, "Checks and remedial points: IO-1")]
        [InlineData("  ", "Checks and remedial points: IO-1")]
        public void The_paraplanner_letter_names_the_client_too(string client, string expected)
        {
            Assert.Equal(expected, ParaplannerRemediationLetter.Subject("IO-1", client));
        }
    }
}
