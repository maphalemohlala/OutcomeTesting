using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// <c>{{grading}}</c>, <c>{{finalOutcome}}</c> and <c>{{notes}}</c> in every letter
    /// (2026-10-19): offered everywhere, filled from the case where the event supplies nothing.
    /// </summary>
    public class CaseOutcomeTokenTests
    {
        private static readonly Guid CaseId = Guid.Parse("caee4444-4444-4444-8444-444444444444");
        private static readonly EntityReference CaseRef = new EntityReference("al_outcomecase", CaseId);

        [Fact]
        public void Every_letter_and_a_letter_of_your_own_may_use_them()
        {
            const string body = "{{grading}} {{finalOutcome}}{{notes}}";
            foreach (var definition in NotificationTemplates.All)
            {
                Assert.Empty(NotificationTemplates.UnknownTokens(definition.Code, "Subject", body));
            }

            Assert.Null(NotificationTemplateGuardPlugin.Refusal(
                "TELL-THE-MANAGER", "Subject", body,
                NotificationOutbox.EventCasePassed, NotificationRecipients.KindTcManager, false));
        }

        [Fact]
        public void A_letter_whose_event_supplies_none_reads_them_off_the_case()
        {
            var service = Graded();
            Template(service, NotificationTemplates.CasePassed,
                "Case {{reference}}", "Graded {{grading}}, final {{finalOutcome}}.{{notes}}");

            var letter = NotificationTemplates.Render(
                service,
                NotificationTemplates.CasePassed,
                new Dictionary<string, string> { { NotificationTemplates.TokenReference, "OT-1" } },
                CaseRef);

            Assert.Equal("Graded Pass with issues, final Pass. Notes: All fixed", letter.Body);
        }

        [Fact]
        public void A_value_the_event_supplied_wins_over_the_case()
        {
            var service = Graded();

            var letter = NotificationTemplates.Render(
                service,
                NotificationTemplates.SignoffApprovedClosed,
                new Dictionary<string, string>
                {
                    { NotificationTemplates.TokenReference, "OT-1" },
                    { NotificationTemplates.TokenFinalOutcome, "Potential harm" },
                    { NotificationTemplates.TokenNotes, string.Empty },
                },
                CaseRef);

            Assert.Contains("final outcome of Potential harm.", letter.Body);
            Assert.EndsWith(" Notes: All fixed", letter.Body);
        }

        [Fact]
        public void A_case_not_yet_graded_leaves_them_empty()
        {
            var service = new FakeOrganizationService();
            var tokens = NotificationTemplateRows.OutcomeTokens(service, CaseRef);

            Assert.Equal(string.Empty, tokens[NotificationTemplates.TokenGrading]);
            Assert.Equal(string.Empty, tokens[NotificationTemplates.TokenFinalOutcome]);
            Assert.Equal(string.Empty, tokens[NotificationTemplates.TokenNotes]);
        }

        [Fact]
        public void The_case_is_not_read_when_the_wording_does_not_ask()
        {
            var service = Graded();
            NotificationTemplates.Render(
                service,
                NotificationTemplates.RecheckDue,
                new Dictionary<string, string> { { NotificationTemplates.TokenReference, "OT-1" } },
                CaseRef);

            Assert.DoesNotContain("al_outcome", service.ReadEntities);
        }

        private static FakeOrganizationService Graded()
        {
            var service = new FakeOrganizationService();
            var outcome = service.Seed("al_outcome", Guid.NewGuid(),
                "al_outcomecaseid", CaseRef,
                Outcomes.InitialOutcomeAttr, new OptionSetValue(OutcomeRules.OutcomePassWithIssues),
                Outcomes.FinalOutcomeAttr, new OptionSetValue(OutcomeRules.FinalOutcomePass),
                "createdon", new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
            outcome.FormattedValues[Outcomes.InitialOutcomeAttr] = "Pass with issues";

            service.Seed("al_signoff", Guid.NewGuid(),
                "al_outcomecaseid", CaseRef,
                "al_notes", "All fixed",
                "createdon", new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc));
            return service;
        }

        private static void Template(FakeOrganizationService service, string code, string subject, string body)
        {
            service.Seed(NotificationTemplates.TemplateEntity, Guid.NewGuid(),
                NotificationTemplates.CodeAttr, code,
                NotificationTemplates.SubjectAttr, subject,
                NotificationTemplates.BodyAttr, body,
                "statecode", 0);
        }
    }
}
