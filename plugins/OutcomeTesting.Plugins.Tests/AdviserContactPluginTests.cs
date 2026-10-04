using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A contact created, reactivated, or given its email after remediation was raised picks
    /// the open actions up (AD-228). Until now they moved only when someone edited the case.
    /// </summary>
    public class AdviserContactPluginTests
    {
        private static readonly Guid CaseId = Guid.Parse("f1f1f1f1-0000-4000-8000-000000000001");
        private static readonly Guid ContactId = Guid.Parse("f1f1f1f1-0000-4000-8000-000000000002");

        private static FakeOrganizationService World(int contactState = 0)
        {
            var svc = new FakeOrganizationService();
            // CaseAccessReconciler.Reconcile (called by Follow) looks the AQS/Tax teams and
            // the AQS queue account up by name; seed them the way CaseAccessReconcilerTests
            // does, rather than stubbing the reconciler out.
            svc.Seed("team", Guid.NewGuid(), "name", ProductName.TaxTeam(ProductName.Default));
            svc.Seed("team", Guid.NewGuid(), "name", ProductName.AqsTeam(ProductName.Default));
            svc.Seed("account", Guid.NewGuid(), "name", ProductName.AqsQueueAccount(ProductName.Default));
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-LATE-1",
                "al_advisername", "Adam Strumidlo",
                "al_adviseremail", "adam.strumidlo@example.com");
            svc.Seed("contact", ContactId,
                "fullname", "Adam Strumdlio",
                "emailaddress1", "adam.strumidlo@example.com",
                "statecode", new OptionSetValue(contactState));
            return svc;
        }

        private static Guid Action(FakeOrganizationService svc, int status, Guid? holder = null)
        {
            var row = svc.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_actionstatus", new OptionSetValue(status),
                "al_name", "Remediation IO-LATE-1");
            if (holder.HasValue)
            {
                row["al_assignedcontactid"] = new EntityReference("contact", holder.Value);
            }

            return row.Id;
        }

        [Fact]
        public void A_new_contact_takes_the_unassigned_open_actions_on_cases_with_their_email()
        {
            var svc = World();
            var action = Action(svc, Remediation.StatusOpen);

            Assert.Equal(1, AdviserContactPlugin.Follow(svc, "adam.strumidlo@example.com", Guid.NewGuid()));
            Assert.Equal(ContactId, svc.Row("al_remediationaction", action)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void A_completed_action_is_never_moved()
        {
            var svc = World();
            var done = Action(svc, Remediation.StatusCompleted);

            Assert.Equal(0, AdviserContactPlugin.Follow(svc, "adam.strumidlo@example.com", Guid.NewGuid()));
            Assert.Null(svc.Row("al_remediationaction", done).GetAttributeValue<EntityReference>("al_assignedcontactid"));
        }

        [Fact]
        public void A_deactivated_contact_releases_their_open_actions()
        {
            var svc = World(contactState: 1);
            var action = Action(svc, Remediation.StatusOpen, ContactId);

            Assert.Equal(1, AdviserContactPlugin.Follow(svc, "adam.strumidlo@example.com", Guid.NewGuid()));
            Assert.Null(svc.Row("al_remediationaction", action).GetAttributeValue<EntityReference>("al_assignedcontactid"));
        }

        [Fact]
        public void An_email_no_case_carries_touches_nothing()
        {
            var svc = World();
            Action(svc, Remediation.StatusOpen);

            Assert.Equal(0, AdviserContactPlugin.Follow(svc, "someone.else@example.com", Guid.NewGuid()));
        }

        [Fact]
        public void A_blank_email_touches_nothing()
        {
            Assert.Equal(0, AdviserContactPlugin.Follow(World(), "  ", Guid.NewGuid()));
        }

        [Fact]
        public void The_email_comes_from_the_target_when_the_write_carries_it()
        {
            var context = new FakePluginExecutionContext { PrimaryEntityName = "contact", PrimaryEntityId = ContactId };
            context.InputParameters["Target"] = new Entity("contact", ContactId) { ["emailaddress1"] = "new@example.com" };

            Assert.Equal("new@example.com", AdviserContactPlugin.EmailOf(context, World()));
        }

        [Fact]
        public void The_email_is_read_from_the_row_when_only_the_state_changed()
        {
            var context = new FakePluginExecutionContext { PrimaryEntityName = "contact", PrimaryEntityId = ContactId };
            context.InputParameters["Target"] = new Entity("contact", ContactId) { ["statecode"] = new OptionSetValue(0) };

            Assert.Equal("adam.strumidlo@example.com", AdviserContactPlugin.EmailOf(context, World()));
        }
    }
}
