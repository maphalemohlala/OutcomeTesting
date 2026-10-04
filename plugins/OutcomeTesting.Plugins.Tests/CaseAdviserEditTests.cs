using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Changing a case's adviser NAME alone used to leave the adviser email - and therefore
    /// who holds the open remediation actions - untouched: email-to-name derivation
    /// (CaseAdviser.FollowName) was removed along with every other name match (2026-10-02),
    /// and nothing replaced it yet. Task 2 put the explicit rule in its place
    /// (<see cref="CasePeople.EnsureEmails"/>, called from the end of
    /// <see cref="UpdateCaseDetailsPlugin.ApplyFields"/>, which both front ends reach):
    /// a name travels with its email, so a name-only change is refused outright rather than
    /// silently leaving the email behind (AD-228, "two people can share a name").
    /// </summary>
    public class CaseAdviserEditTests
    {
        private static readonly Guid CaseId = Guid.Parse("cae00000-2222-4222-8222-222222222222");
        private static readonly Guid Checker = Guid.Parse("cae00000-3333-4333-8333-333333333333");

        private static FakeOrganizationService Ready(out EntityReference oldAdviser, out EntityReference newAdviser, out Guid actionId)
        {
            var service = new FakeOrganizationService();
            service.Seed("contact", Checker, "fullname", "A Checker");
            oldAdviser = service.Seed("contact", Guid.NewGuid(),
                "fullname", "Old Adviser", "emailaddress1", "old@example.com",
                "statecode", new OptionSetValue(0)).ToEntityReference();
            newAdviser = service.Seed("contact", Guid.NewGuid(),
                "fullname", "New Adviser", "emailaddress1", "new@example.com",
                "statecode", new OptionSetValue(0)).ToEntityReference();
            service.Seed("al_outcomecase", CaseId,
                "al_casereference", "300000009",
                CaseAdviser.NameAttr, "Old Adviser",
                CaseAdviser.EmailAttr, "old@example.com");
            service.Seed("al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_assignedcontactid", new EntityReference("contact", Checker),
                "statecode", new OptionSetValue(0));
            actionId = Guid.NewGuid();
            service.Seed("al_remediationaction", actionId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_actionstatus", new OptionSetValue(120910600),
                "al_assignedcontactid", oldAdviser);
            return service;
        }

        [Fact]
        public void The_portal_header_edit_refuses_a_name_only_adviser_change()
        {
            // A name-only edit used to change al_advisername and leave the stored email -
            // and therefore who the open action was assigned to - exactly as it was. Task 2
            // refuses this outright instead: the email must travel with the name.
            var service = Ready(out var oldAdviser, out _, out var actionId);

            var provider = new FakeServiceProvider(service);
            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityName = "contact";
            provider.Context.PrimaryEntityId = Checker;
            provider.Context.InputParameters["Target"] = new Entity("contact", Checker)
            {
                [CaseHeaderRequestPlugin.RequestAttr] =
                    "{\"caseId\":\"" + CaseId.ToString("D") + "\",\"fields\":\"{\\\"al_advisername\\\":\\\"New Adviser\\\"}\"}",
            };

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => new CaseHeaderRequestPlugin(null, null).Execute(provider));
            Assert.Contains("adviser's email", error.Message);

            // Refused before anything was written: the name, the email and the open action's
            // assignee are all exactly as seeded.
            var row = service.Row("al_outcomecase", CaseId);
            Assert.Equal("Old Adviser", row.GetAttributeValue<string>(CaseAdviser.NameAttr));
            Assert.Equal("old@example.com", row.GetAttributeValue<string>(CaseAdviser.EmailAttr));
            Assert.Equal(oldAdviser.Id, service.Row("al_remediationaction", actionId)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void The_portal_header_edit_clears_a_name_alone_and_keeps_the_email()
        {
            // A cleared name names nobody, so it is not a name without its email: the label
            // goes, the email stays the identity, and nothing moves.
            var service = Ready(out var oldAdviser, out _, out var actionId);

            var provider = new FakeServiceProvider(service);
            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityName = "contact";
            provider.Context.PrimaryEntityId = Checker;
            provider.Context.InputParameters["Target"] = new Entity("contact", Checker)
            {
                [CaseHeaderRequestPlugin.RequestAttr] =
                    "{\"caseId\":\"" + CaseId.ToString("D") + "\",\"fields\":\"{\\\"al_advisername\\\":\\\"\\\"}\"}",
            };

            new CaseHeaderRequestPlugin(null, null).Execute(provider);

            var row = service.Row("al_outcomecase", CaseId);
            Assert.Null(row.GetAttributeValue<string>(CaseAdviser.NameAttr));
            Assert.Equal("old@example.com", row.GetAttributeValue<string>(CaseAdviser.EmailAttr));
            Assert.Equal(oldAdviser.Id, service.Row("al_remediationaction", actionId)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void The_shared_field_applier_clears_a_name_alone_and_keeps_the_email()
        {
            // The Code App's al_UpdateCaseDetails reaches the same rule through ApplyFields.
            var service = Ready(out _, out _, out _);
            var before = service.Row("al_outcomecase", CaseId);
            var update = new Entity("al_outcomecase", CaseId);
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.ApplyFields(
                service,
                new Dictionary<string, string> { { CaseAdviser.NameAttr, "" } },
                before,
                update,
                changes,
                new OptionLabels(service));

            Assert.True(update.Contains(CaseAdviser.NameAttr));
            Assert.Null(update.GetAttributeValue<string>(CaseAdviser.NameAttr));
            Assert.False(update.Contains(CaseAdviser.EmailAttr));
        }

        [Fact]
        public void The_shared_field_applier_refuses_a_name_only_adviser_change()
        {
            // UpdateCaseDetailsPlugin.ApplyFields is what the Code App's al_UpdateCaseDetails
            // and the portal header both call, and it now ends with CasePeople.EnsureEmails -
            // so a name sent without its email is refused on either front end.
            var service = Ready(out _, out _, out _);
            var before = service.Row("al_outcomecase", CaseId);
            var update = new Entity("al_outcomecase", CaseId);
            var changes = new List<string>();

            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                UpdateCaseDetailsPlugin.ApplyFields(
                    service,
                    new Dictionary<string, string> { { CaseAdviser.NameAttr, "New Adviser" } },
                    before,
                    update,
                    changes,
                    new OptionLabels(service)));

            Assert.Contains("adviser's email", error.Message);
        }

        [Fact]
        public void The_shared_field_applier_accepts_a_name_sent_with_its_email()
        {
            // The positive case alongside the refusal above: a name sent WITH its email is
            // accepted, and the email is applied through its own Editable (al_adviseremail)
            // exactly as the name is.
            var service = Ready(out _, out _, out _);
            var before = service.Row("al_outcomecase", CaseId);
            var update = new Entity("al_outcomecase", CaseId);
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.ApplyFields(
                service,
                new Dictionary<string, string>
                {
                    { CaseAdviser.NameAttr, "New Adviser" },
                    { CaseAdviser.EmailAttr, "new@example.com" },
                },
                before,
                update,
                changes,
                new OptionLabels(service));

            Assert.Equal("New Adviser", update.GetAttributeValue<string>(CaseAdviser.NameAttr));
            Assert.Equal("new@example.com", update.GetAttributeValue<string>(CaseAdviser.EmailAttr));
        }
    }
}
