using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Changing a case's adviser, from either front end, keeps the adviser email and the open
    /// remediation actions with the new adviser (project owner, 2026-09-30; see CaseAdviser).
    ///
    /// <para>
    /// Before: the Code App's edit moved the open actions but left the old adviser's email, so
    /// sign-off would have been routed to the previous adviser's T&amp;C Manager; the portal's
    /// review-header edit moved neither.
    /// </para>
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
        public void The_portal_header_edit_moves_the_email_and_the_open_actions_to_the_new_adviser()
        {
            var service = Ready(out _, out var newAdviser, out var actionId);

            var provider = new FakeServiceProvider(service);
            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityName = "contact";
            provider.Context.PrimaryEntityId = Checker;
            provider.Context.InputParameters["Target"] = new Entity("contact", Checker)
            {
                [CaseHeaderRequestPlugin.RequestAttr] =
                    "{\"caseId\":\"" + CaseId.ToString("D") + "\",\"fields\":\"{\\\"al_advisername\\\":\\\"New Adviser\\\"}\"}",
            };

            new CaseHeaderRequestPlugin(null, null).Execute(provider);

            var row = service.Row("al_outcomecase", CaseId);
            Assert.Equal("New Adviser", row.GetAttributeValue<string>(CaseAdviser.NameAttr));
            Assert.Equal("new@example.com", row.GetAttributeValue<string>(CaseAdviser.EmailAttr));
            Assert.Equal(newAdviser.Id, service.Row("al_remediationaction", actionId)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void The_shared_field_applier_puts_the_new_advisers_email_on_the_same_update()
        {
            // UpdateCaseDetailsPlugin.ApplyFields is what the Code App's al_UpdateCaseDetails
            // and the portal header both call, so the rule lives there once.
            var service = Ready(out _, out _, out _);
            var before = service.Row("al_outcomecase", CaseId);
            var update = new Entity("al_outcomecase", CaseId);
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.ApplyFields(
                service,
                new Dictionary<string, string> { { CaseAdviser.NameAttr, "New Adviser" } },
                before,
                update,
                changes,
                new OptionLabels(service));

            Assert.Equal("new@example.com", update.GetAttributeValue<string>(CaseAdviser.EmailAttr));
            Assert.Contains(changes, c => c.StartsWith("Adviser email"));
        }
    }
}
