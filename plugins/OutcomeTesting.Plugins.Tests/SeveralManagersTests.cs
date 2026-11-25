using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// An adviser may be linked to several T&amp;C Managers, all equal (owner, 2026-11-24), so
    /// that one being away never holds a sign-off up: each may sign, each is told, each can
    /// read the case. A manager chosen for one case still replaces them all.
    /// </summary>
    public class SeveralManagersTests
    {
        private static readonly Guid CaseId = Guid.Parse("5e5e0000-0000-4000-8000-000000000001");
        private static readonly Guid Pat = Guid.Parse("5e5e0000-0000-4000-8000-000000000002");
        private static readonly Guid Sam = Guid.Parse("5e5e0000-0000-4000-8000-000000000003");
        private static readonly Guid Stranger = Guid.Parse("5e5e0000-0000-4000-8000-000000000004");
        private static readonly Guid Cover = Guid.Parse("5e5e0000-0000-4000-8000-000000000005");
        private static readonly Guid PatRow = Guid.Parse("5e5e0000-0000-4000-8000-000000000006");
        private static readonly Guid SamRow = Guid.Parse("5e5e0000-0000-4000-8000-000000000007");

        private const string AdviserEmail = "ann@example.com";

        private static FakeOrganizationService World(int status = CaseLifecycle.AwaitingSignoff)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("team", Guid.NewGuid(), "name", ProductName.TaxTeam(ProductName.Default));
            svc.Seed("team", Guid.NewGuid(), "name", ProductName.AqsTeam(ProductName.Default));
            svc.Seed("account", Guid.NewGuid(), "name", ProductName.AqsQueueAccount(ProductName.Default));
            Person(svc, Pat, "Pat Manager", "pat@example.com");
            Person(svc, Sam, "Sam Manager", "sam@example.com");
            Person(svc, Stranger, "Stu Stranger", "stu@example.com");
            Person(svc, Cover, "Cora Cover", "cora@example.com");

            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-TWO-1",
                "al_advisername", "Ann Adviser",
                "al_adviseremail", AdviserEmail,
                "al_casestatus", new OptionSetValue(status));
            svc.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted));
            Map(svc, PatRow, Pat);
            Map(svc, SamRow, Sam);
            return svc;
        }

        private static void Person(FakeOrganizationService svc, Guid id, string name, string email)
        {
            svc.Seed("contact", id, "fullname", name, "emailaddress1", email, "statecode", new OptionSetValue(0));
        }

        private static void Map(FakeOrganizationService svc, Guid row, Guid manager)
        {
            svc.Seed("al_advisermapping", row,
                "al_adviseremail", AdviserEmail,
                "al_tcmanagerid", new EntityReference("contact", manager),
                "statecode", new OptionSetValue(0));
        }

        private static EntityReference Case()
        {
            return new EntityReference("al_outcomecase", CaseId);
        }

        private static string[] Recipients(FakeOrganizationService svc)
        {
            return svc.Creates
                .Where(c => c.LogicalName == "al_notification")
                .Select(c => c.GetAttributeValue<string>("al_recipientemail"))
                .OrderBy(e => e)
                .ToArray();
        }

        [Fact]
        public void Both_managers_pass_the_signoff_gate()
        {
            var svc = World();

            SupervisorMapping.EnsureManagesCase(svc, Pat, Case(), "Signing a case off");
            SupervisorMapping.EnsureManagesCase(svc, Sam, Case(), "Signing a case off");
        }

        [Fact]
        public void A_supervisor_mapped_to_neither_is_refused()
        {
            var svc = World();

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SupervisorMapping.EnsureManagesCase(svc, Stranger, Case(), "Signing a case off"));

            Assert.Contains("not one of this case's T&C Managers", error.Message);
        }

        [Fact]
        public void Removing_one_manager_leaves_the_other_able_to_sign()
        {
            var svc = World();
            svc.Delete("al_advisermapping", PatRow);

            SupervisorMapping.EnsureManagesCase(svc, Sam, Case(), "Signing a case off");
            Assert.Throws<InvalidPluginExecutionException>(
                () => SupervisorMapping.EnsureManagesCase(svc, Pat, Case(), "Signing a case off"));
        }

        [Fact]
        public void A_withdrawn_mapping_no_longer_counts()
        {
            var svc = World();
            svc.Row("al_advisermapping", PatRow)["statecode"] = new OptionSetValue(1);

            Assert.Equal(new[] { Sam }, TcManagerRouting.ForCase(svc, Case()).Managers.Select(m => m.Id));
        }

        [Fact]
        public void A_manager_chosen_for_the_case_replaces_them_all()
        {
            var svc = World();
            svc.Row("al_outcomecase", CaseId)[TcManagerRouting.OverrideAttr] = new EntityReference("contact", Cover);

            SupervisorMapping.EnsureManagesCase(svc, Cover, Case(), "Signing a case off");
            Assert.Throws<InvalidPluginExecutionException>(
                () => SupervisorMapping.EnsureManagesCase(svc, Pat, Case(), "Signing a case off"));
        }

        [Fact]
        public void Every_manager_is_told_on_a_row_of_their_own()
        {
            var svc = World();

            CompleteRemediationPlugin.NotifySignoffDue(svc, Case(), Guid.NewGuid(), null);

            Assert.Equal(new[] { "pat@example.com", "sam@example.com" }, Recipients(svc));
            var codes = svc.Creates.Where(c => c.LogicalName == "al_notification")
                .Select(c => c.GetAttributeValue<string>("al_notificationcode")).ToArray();
            Assert.Equal(2, codes.Distinct().Count());
        }

        [Fact]
        public void Telling_again_in_the_same_round_queues_nothing_new()
        {
            var svc = World();
            CompleteRemediationPlugin.NotifySignoffDue(svc, Case(), Guid.NewGuid(), null);
            var first = Recipients(svc).Length;

            CompleteRemediationPlugin.NotifySignoffDue(svc, Case(), Guid.NewGuid(), null);

            Assert.Equal(first, Recipients(svc).Length);
        }

        [Fact]
        public void The_reconciler_links_every_manager_and_unlinks_a_removed_one()
        {
            var svc = World();

            var first = CaseAccessReconciler.Reconcile(svc, CaseId, DateTime.UtcNow);
            Assert.Equal(new[] { Pat, Sam }.OrderBy(g => g), CaseAccessReconciler.LinkedSupervisors(svc, CaseId).OrderBy(g => g));
            Assert.False(first.SupervisorUnmatched);

            // Two managers: the single column names nobody, the links name both.
            Assert.Null(svc.Row("al_outcomecase", CaseId).GetAttributeValue<EntityReference>(CaseAccessReconciler.SupervisorAttr));

            svc.Delete("al_advisermapping", PatRow);
            var second = CaseAccessReconciler.Reconcile(svc, CaseId, DateTime.UtcNow);

            Assert.Equal(new[] { Sam }, CaseAccessReconciler.LinkedSupervisors(svc, CaseId));
            Assert.Equal(1, second.RemovedSupervisors);
            Assert.Equal(Sam, svc.Row("al_outcomecase", CaseId).GetAttributeValue<EntityReference>(CaseAccessReconciler.SupervisorAttr).Id);
        }

        [Fact]
        public void Reconciling_twice_links_nothing_twice()
        {
            var svc = World();
            CaseAccessReconciler.Reconcile(svc, CaseId, DateTime.UtcNow);
            var attempts = svc.AssociateAttempts;

            var again = CaseAccessReconciler.Reconcile(svc, CaseId, DateTime.UtcNow);

            Assert.Equal(attempts, svc.AssociateAttempts);
            Assert.Empty(again.AddedSupervisors);
        }

        [Fact]
        public void A_manager_added_to_a_waiting_case_is_told_and_the_others_are_not()
        {
            var svc = World();
            CaseAccessReconciler.Reconcile(svc, CaseId, DateTime.UtcNow);
            var third = Guid.NewGuid();
            Person(svc, third, "Tia Third", "tia@example.com");
            var row = Guid.NewGuid();
            Map(svc, row, third);

            var context = new FakeServiceProvider().Context;
            context.MessageName = "Create";
            context.PrimaryEntityName = "al_advisermapping";
            context.PrimaryEntityId = row;
            context.InputParameters["Target"] = new Entity("al_advisermapping", row)
            {
                ["al_adviseremail"] = AdviserEmail,
                ["al_tcmanagerid"] = new EntityReference("contact", third),
            };

            Assert.Equal(1, AdviserMappingPlugin.Apply(context, svc));
            Assert.Equal(new[] { "tia@example.com" }, Recipients(svc));
        }

        [Fact]
        public void Each_manager_gets_a_copy_when_a_template_goes_to_the_tc_manager()
        {
            var svc = World();
            var choice = new NotificationTemplateRows.RecipientChoice();
            choice.Kinds.Add(NotificationRecipients.KindTcManager);

            var resolved = NotificationRecipients.Resolve(svc, choice, Case(), null);

            Assert.Equal(new[] { "pat@example.com", "sam@example.com" }, resolved.Select(r => r.Email).OrderBy(e => e));
        }

        [Fact]
        public void A_client_cannot_link_a_manager_to_a_case_directly()
        {
            Assert.Throws<InvalidPluginExecutionException>(() =>
                CaseAccessGuardPlugin.EnsureLinksNotWrittenDirectly(
                    new Relationship(CaseAccessReconciler.SupervisorsRelationship), 1));

            // The reconciler links from inside a plug-in, deeper than any client.
            CaseAccessGuardPlugin.EnsureLinksNotWrittenDirectly(
                new Relationship(CaseAccessReconciler.SupervisorsRelationship), 2);
            CaseAccessGuardPlugin.EnsureLinksNotWrittenDirectly(
                new Relationship(ListOptionRules.ProductsRelationship), 1);
        }
    }
}
