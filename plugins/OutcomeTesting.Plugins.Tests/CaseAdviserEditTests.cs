using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Changing a case's adviser NAME alone no longer moves the adviser email or the open
    /// remediation actions (AD-228, 2026-10-02): email-to-name derivation (CaseAdviser.FollowName)
    /// was removed along with every other name match, so an edit that sends only
    /// al_advisername now leaves al_adviseremail - and therefore who holds the open actions -
    /// untouched. Task 2 is expected to put a new, explicit rule here.
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
        public void The_portal_header_edit_renames_the_adviser_without_moving_the_email_or_the_open_actions()
        {
            // Inverted from the removed behaviour. A name-only edit changes al_advisername but
            // leaves the stored email - and therefore who the open action is assigned to -
            // exactly as it was, because nothing derives an email from a name any more.
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

            new CaseHeaderRequestPlugin(null, null).Execute(provider);

            var row = service.Row("al_outcomecase", CaseId);
            Assert.Equal("New Adviser", row.GetAttributeValue<string>(CaseAdviser.NameAttr));
            Assert.Equal("old@example.com", row.GetAttributeValue<string>(CaseAdviser.EmailAttr));
            Assert.Equal(oldAdviser.Id, service.Row("al_remediationaction", actionId)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void The_shared_field_applier_leaves_the_email_alone_on_a_name_only_edit()
        {
            // UpdateCaseDetailsPlugin.ApplyFields is what the Code App's al_UpdateCaseDetails
            // and the portal header both call. CaseAdviser.FollowName, which used to derive an
            // email from the new name here, was removed (AD-228) - Task 2 is expected to put
            // a new, explicit rule in its place.
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

            Assert.False(update.Contains(CaseAdviser.EmailAttr));
            Assert.DoesNotContain(changes, c => c.StartsWith("Adviser email"));
        }
    }
}
