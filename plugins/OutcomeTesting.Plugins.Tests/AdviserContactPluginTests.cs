using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
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
        private static readonly Guid OtherCaseId = Guid.Parse("f1f1f1f1-0000-4000-8000-000000000003");

        private const string OldEmail = "adam.strumidlo@example.com";
        private const string NewEmail = "adam.s@example.com";

        private static FakeOrganizationService World(int contactState = 0, int? caseStatus = null)
        {
            var svc = new FakeOrganizationService();
            // CaseAccessReconciler.Reconcile (called by Follow) looks the AQS/Tax teams and
            // the AQS queue account up by name; seed them the way CaseAccessReconcilerTests
            // does, rather than stubbing the reconciler out.
            svc.Seed("team", Guid.NewGuid(), "name", ProductName.TaxTeam(ProductName.Default));
            svc.Seed("team", Guid.NewGuid(), "name", ProductName.AqsTeam(ProductName.Default));
            svc.Seed("account", Guid.NewGuid(), "name", ProductName.AqsQueueAccount(ProductName.Default));
            var outcomeCase = svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-LATE-1",
                "al_advisername", "Adam Strumidlo",
                "al_adviseremail", OldEmail);
            if (caseStatus.HasValue)
            {
                outcomeCase["al_casestatus"] = new OptionSetValue(caseStatus.Value);
            }

            svc.Seed("contact", ContactId,
                "fullname", "Adam Strumdlio",
                "emailaddress1", OldEmail,
                "statecode", new OptionSetValue(contactState));
            return svc;
        }

        private static Guid Action(FakeOrganizationService svc, int status, Guid? holder = null, Guid? caseId = null)
        {
            var row = svc.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId ?? CaseId),
                "al_actionstatus", new OptionSetValue(status),
                "al_name", "Remediation IO-LATE-1");
            if (holder.HasValue)
            {
                row["al_assignedcontactid"] = new EntityReference("contact", holder.Value);
            }

            return row.Id;
        }

        private static Guid? Holder(FakeOrganizationService svc, Guid action)
        {
            var holder = svc.Row("al_remediationaction", action).GetAttributeValue<EntityReference>("al_assignedcontactid");
            return holder == null ? (Guid?)null : holder.Id;
        }

        // Every reconcile shares or unshares the case with both teams - two requests per run -
        // so the requests targeting a case count how many times it was reconciled.
        private static int ReconcileRuns(FakeOrganizationService svc, Guid caseId)
        {
            var shares = svc.Requests.OfType<GrantAccessRequest>().Count(r => r.Target.Id == caseId)
                + svc.Requests.OfType<RevokeAccessRequest>().Count(r => r.Target.Id == caseId);
            return shares / 2;
        }

        private static FakeServiceProvider Write(FakeOrganizationService svc, string message, Entity target, Entity preImage = null)
        {
            var provider = new FakeServiceProvider(svc);
            provider.Context.MessageName = message;
            provider.Context.PrimaryEntityName = "contact";
            provider.Context.PrimaryEntityId = ContactId;
            provider.Context.InputParameters["Target"] = target;
            if (preImage != null)
            {
                provider.Context.PreEntityImages[AdviserContactPlugin.PreImageName] = preImage;
            }

            return provider;
        }

        [Fact]
        public void A_new_contact_takes_the_unassigned_open_actions_on_cases_with_their_email()
        {
            var svc = World();
            var action = Action(svc, Remediation.StatusOpen);

            Assert.Equal(1, AdviserContactPlugin.Follow(svc, OldEmail, Guid.NewGuid()));
            Assert.Equal(ContactId, Holder(svc, action));
        }

        [Fact]
        public void A_completed_action_is_never_moved()
        {
            var svc = World();
            var done = Action(svc, Remediation.StatusCompleted);

            Assert.Equal(0, AdviserContactPlugin.Follow(svc, OldEmail, Guid.NewGuid()));
            Assert.Null(Holder(svc, done));
        }

        [Fact]
        public void A_deactivated_contact_releases_their_open_actions()
        {
            var svc = World(contactState: 1);
            var action = Action(svc, Remediation.StatusOpen, ContactId);

            Assert.Equal(1, AdviserContactPlugin.Follow(svc, OldEmail, Guid.NewGuid()));
            Assert.Null(Holder(svc, action));
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

        // ---- Which cases a contact write reaches --------------------------------------------
        //
        // The step runs inside the contact save, so every case it visits is spent against the
        // two-minute synchronous limit. A contact can change only two things on a case: who
        // holds its OPEN actions, and al_advisercontactid, which is written only while the
        // case is released to its adviser - and release needs a remedial action. A case with
        // no remediation (a pass, closed or not) can change neither, so it is never visited.

        [Fact]
        public void A_closed_case_with_no_remediation_is_not_visited()
        {
            var svc = World(caseStatus: CaseLifecycle.Closed);

            AdviserContactPlugin.Follow(svc, OldEmail, Guid.NewGuid());

            Assert.Equal(0, ReconcileRuns(svc, CaseId));
            Assert.DoesNotContain(svc.Updates, u => u.Id == CaseId);
        }

        [Fact]
        public void An_open_case_has_its_actions_and_its_access_followed()
        {
            var svc = World(caseStatus: CaseLifecycle.AwaitingRemediation);
            var action = Action(svc, Remediation.StatusOpen);

            Assert.Equal(1, AdviserContactPlugin.Follow(svc, OldEmail, Guid.NewGuid()));

            Assert.Equal(ContactId, Holder(svc, action));
            Assert.Equal(1, ReconcileRuns(svc, CaseId));
            Assert.Equal(ContactId, svc.Row("al_outcomecase", CaseId)
                .GetAttributeValue<EntityReference>(CaseAccessReconciler.AdviserAttr).Id);
        }

        [Fact]
        public void A_closed_case_with_remediation_still_loses_a_deactivated_adviser()
        {
            // The adviser keeps a remediated case after sign-off (CaseAccess, answer 2a), so a
            // closed case is still released to whoever holds the email. A deactivated contact
            // has to lose it, and nothing else would take it away.
            var svc = World(contactState: 1, caseStatus: CaseLifecycle.Closed);
            svc.Row("al_outcomecase", CaseId)[CaseAccessReconciler.AdviserAttr] = new EntityReference("contact", ContactId);
            Action(svc, Remediation.StatusCompleted, ContactId);

            AdviserContactPlugin.Follow(svc, OldEmail, Guid.NewGuid());

            Assert.Equal(1, ReconcileRuns(svc, CaseId));
            Assert.Null(svc.Row("al_outcomecase", CaseId).GetAttributeValue<EntityReference>(CaseAccessReconciler.AdviserAttr));
        }

        [Fact]
        public void A_case_with_several_actions_is_visited_once()
        {
            var svc = World(caseStatus: CaseLifecycle.RemediationInProgress);
            Action(svc, Remediation.StatusOpen);
            Action(svc, Remediation.StatusInProgress);
            Action(svc, Remediation.StatusCompleted);

            Assert.Equal(2, AdviserContactPlugin.Follow(svc, OldEmail, Guid.NewGuid()));
            Assert.Equal(1, ReconcileRuns(svc, CaseId));
        }

        // ---- An email change follows the old address too -----------------------------------

        [Fact]
        public void An_email_change_repoints_the_cases_on_the_old_address_and_the_new()
        {
            var svc = World();
            svc.Row("contact", ContactId)["emailaddress1"] = NewEmail;
            svc.Seed("al_outcomecase", OtherCaseId,
                "al_casereference", "IO-LATE-2",
                "al_advisername", "Adam Strumidlo",
                "al_adviseremail", NewEmail);
            var onOld = Action(svc, Remediation.StatusOpen, ContactId);
            var onNew = Action(svc, Remediation.StatusOpen, caseId: OtherCaseId);

            new AdviserContactPlugin(null, null).Execute(Write(svc, "Update",
                new Entity("contact", ContactId) { ["emailaddress1"] = NewEmail },
                new Entity("contact", ContactId) { ["emailaddress1"] = OldEmail }));

            // Nobody holds the old address now, so its case's action is held by nobody -
            // never left with a contact who no longer answers to the email on the case.
            Assert.Null(Holder(svc, onOld));
            Assert.Equal(ContactId, Holder(svc, onNew));
        }

        [Fact]
        public void An_email_change_hands_the_old_address_cases_to_whoever_now_holds_it()
        {
            var svc = World();
            svc.Row("contact", ContactId)["emailaddress1"] = NewEmail;
            var successor = svc.Seed("contact", Guid.NewGuid(),
                "fullname", "Successor",
                "emailaddress1", OldEmail,
                "statecode", new OptionSetValue(0)).Id;
            var onOld = Action(svc, Remediation.StatusOpen, ContactId);

            new AdviserContactPlugin(null, null).Execute(Write(svc, "Update",
                new Entity("contact", ContactId) { ["emailaddress1"] = NewEmail },
                new Entity("contact", ContactId) { ["emailaddress1"] = OldEmail }));

            Assert.Equal(successor, Holder(svc, onOld));
        }

        // Counted at the query that finds an address's cases, because this fake compares the
        // link criterion exactly: a second follow of "ADAM.Strumidlo@Example.com" would find
        // nothing here, though Dataverse would find the same cases again. With no remediation
        // in this world, that query is the only read of al_remediationaction, so each follow
        // costs exactly one.
        [Theory]
        [InlineData(" ADAM.Strumidlo@Example.com ")]
        [InlineData("  adam.strumidlo@example.com  ")]
        public void The_same_email_in_different_case_or_with_spaces_is_followed_once(string before)
        {
            var counted = new CountingOrganizationService(World());
            var context = new FakePluginExecutionContext { MessageName = "Update", PrimaryEntityName = "contact", PrimaryEntityId = ContactId };
            context.InputParameters["Target"] = new Entity("contact", ContactId) { ["emailaddress1"] = OldEmail };
            context.PreEntityImages[AdviserContactPlugin.PreImageName] = new Entity("contact", ContactId) { ["emailaddress1"] = before };

            AdviserContactPlugin.Apply(context, counted);

            Assert.Equal(1, counted.QueriesOf("al_remediationaction"));
        }

        [Fact]
        public void A_different_old_email_is_followed_as_well()
        {
            // The counterpart that proves the count above can reach two.
            var counted = new CountingOrganizationService(World());
            var context = new FakePluginExecutionContext { MessageName = "Update", PrimaryEntityName = "contact", PrimaryEntityId = ContactId };
            context.InputParameters["Target"] = new Entity("contact", ContactId) { ["emailaddress1"] = NewEmail };
            context.PreEntityImages[AdviserContactPlugin.PreImageName] = new Entity("contact", ContactId) { ["emailaddress1"] = OldEmail };

            AdviserContactPlugin.Apply(context, counted);

            Assert.Equal(2, counted.QueriesOf("al_remediationaction"));
        }

        [Fact]
        public void Deactivating_and_changing_the_email_in_one_write_releases_both_addresses_cases()
        {
            // The contact answers to neither address any more: not the old one, which it gave
            // up, and not the new one, because an inactive contact matches nobody.
            var svc = World(contactState: 1);
            svc.Row("contact", ContactId)["emailaddress1"] = NewEmail;
            svc.Seed("al_outcomecase", OtherCaseId,
                "al_casereference", "IO-LATE-2",
                "al_advisername", "Adam Strumidlo",
                "al_adviseremail", NewEmail);
            var onOld = Action(svc, Remediation.StatusOpen, ContactId);
            var onNew = Action(svc, Remediation.StatusOpen, ContactId, OtherCaseId);

            new AdviserContactPlugin(null, null).Execute(Write(svc, "Update",
                new Entity("contact", ContactId) { ["statecode"] = new OptionSetValue(1), ["emailaddress1"] = NewEmail },
                new Entity("contact", ContactId) { ["emailaddress1"] = OldEmail }));

            Assert.Null(Holder(svc, onOld));
            Assert.Null(Holder(svc, onNew));
        }

        [Fact]
        public void Clearing_the_email_follows_the_address_given_up()
        {
            var svc = World();
            svc.Row("contact", ContactId)["emailaddress1"] = null;
            var successor = svc.Seed("contact", Guid.NewGuid(),
                "fullname", "Successor",
                "emailaddress1", OldEmail,
                "statecode", new OptionSetValue(0)).Id;
            var onOld = Action(svc, Remediation.StatusOpen, ContactId);

            new AdviserContactPlugin(null, null).Execute(Write(svc, "Update",
                new Entity("contact", ContactId) { ["emailaddress1"] = null },
                new Entity("contact", ContactId) { ["emailaddress1"] = OldEmail }));

            Assert.Equal(successor, Holder(svc, onOld));
        }

        [Fact]
        public void Without_a_pre_image_only_the_new_email_is_followed()
        {
            var svc = World();
            svc.Row("contact", ContactId)["emailaddress1"] = NewEmail;
            var onOld = Action(svc, Remediation.StatusOpen, ContactId);

            new AdviserContactPlugin(null, null).Execute(Write(svc, "Update",
                new Entity("contact", ContactId) { ["emailaddress1"] = NewEmail }));

            Assert.Equal(ContactId, Holder(svc, onOld));
            Assert.Equal(0, ReconcileRuns(svc, CaseId));
        }

        [Fact]
        public void A_state_change_with_its_pre_image_follows_the_one_email()
        {
            var svc = World(contactState: 1);
            var action = Action(svc, Remediation.StatusOpen, ContactId);

            new AdviserContactPlugin(null, null).Execute(Write(svc, "Update",
                new Entity("contact", ContactId) { ["statecode"] = new OptionSetValue(1) },
                new Entity("contact", ContactId) { ["emailaddress1"] = OldEmail }));

            Assert.Null(Holder(svc, action));
            Assert.Equal(1, ReconcileRuns(svc, CaseId));
        }

        // ---- Where the email is read from ---------------------------------------------------
        //
        // A Create's Target carries every column the create wrote, so a contact created
        // without an email has none and there is nothing to read back. Only an Update can
        // leave the email off the Target - an Update of statecode alone - and only that falls
        // back to the row.

        [Fact]
        public void A_create_takes_the_email_from_the_target()
        {
            var svc = World();
            var context = new FakePluginExecutionContext { MessageName = "Create", PrimaryEntityName = "contact", PrimaryEntityId = ContactId };
            context.InputParameters["Target"] = new Entity("contact", ContactId) { ["emailaddress1"] = " new@example.com " };

            Assert.Equal("new@example.com", AdviserContactPlugin.EmailOf(context, svc));
            Assert.Equal(0, svc.RetrieveCount);
        }

        [Fact]
        public void A_create_without_an_email_has_none_and_reads_nothing()
        {
            var svc = World();
            var context = new FakePluginExecutionContext { MessageName = "Create", PrimaryEntityName = "contact", PrimaryEntityId = ContactId };
            context.InputParameters["Target"] = new Entity("contact", ContactId) { ["firstname"] = "Adam" };

            Assert.Null(AdviserContactPlugin.EmailOf(context, svc));
            Assert.Equal(0, svc.RetrieveCount);
        }

        [Fact]
        public void An_update_of_the_email_takes_it_from_the_target()
        {
            var svc = World();
            var context = new FakePluginExecutionContext { MessageName = "Update", PrimaryEntityName = "contact", PrimaryEntityId = ContactId };
            context.InputParameters["Target"] = new Entity("contact", ContactId) { ["emailaddress1"] = "new@example.com" };

            Assert.Equal("new@example.com", AdviserContactPlugin.EmailOf(context, svc));
            Assert.Equal(0, svc.RetrieveCount);
        }

        [Fact]
        public void An_update_of_the_state_alone_reads_the_email_from_the_row()
        {
            var context = new FakePluginExecutionContext { MessageName = "Update", PrimaryEntityName = "contact", PrimaryEntityId = ContactId };
            context.InputParameters["Target"] = new Entity("contact", ContactId) { ["statecode"] = new OptionSetValue(0) };

            Assert.Equal(OldEmail, AdviserContactPlugin.EmailOf(context, World()));
        }

        [Fact]
        public void The_previous_email_comes_from_the_pre_image_cleaned()
        {
            var context = new FakePluginExecutionContext { MessageName = "Update" };
            context.PreEntityImages[AdviserContactPlugin.PreImageName] =
                new Entity("contact", ContactId) { ["emailaddress1"] = "  old@example.com " };

            Assert.Equal("old@example.com", AdviserContactPlugin.PreviousEmailOf(context));
        }

        [Fact]
        public void No_pre_image_means_no_previous_email()
        {
            Assert.Null(AdviserContactPlugin.PreviousEmailOf(new FakePluginExecutionContext { MessageName = "Create" }));
        }
    }
}
