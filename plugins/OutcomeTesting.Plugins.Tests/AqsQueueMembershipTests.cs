using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The AQS queue is an Account-scoped portal permission (AD-218), so an AQS reviewer sees it
    /// only while their contact's parent account is the AQS Team account. Found in DEV
    /// verification: a reviewer granted the role after rollout saw an empty queue, because only
    /// the one-off rollout step had set the account.
    /// </summary>
    public class AqsQueueMembershipTests
    {
        private static readonly Guid Contact = Guid.Parse("abababab-0000-4000-8000-000000000001");
        private static readonly Guid AqsRole = Guid.Parse("abababab-0000-4000-8000-000000000002");
        private static readonly Guid TaxRole = Guid.Parse("abababab-0000-4000-8000-000000000003");
        private static readonly Guid QueueAccount = Guid.Parse("abababab-0000-4000-8000-000000000004");
        private static readonly Guid OtherAccount = Guid.Parse("abababab-0000-4000-8000-000000000005");

        private static readonly Relationship WebRoleLink = new Relationship(WebRoleRegistry.ContactRelationship);

        private static FakeOrganizationService Environment(Guid? parentAccount = null, bool withQueueAccount = true)
        {
            var svc = new FakeOrganizationService();
            svc.Seed(WebRoleRegistry.RoleEntity, AqsRole, WebRoleRegistry.NameAttr, WebRoleRegistry.AqsReviewerRole);
            svc.Seed(WebRoleRegistry.RoleEntity, TaxRole, WebRoleRegistry.NameAttr, WebRoleRegistry.TaxReviewerRole);
            if (withQueueAccount)
            {
                svc.Seed("account", QueueAccount, "name", CaseAccessReconciler.AqsQueueAccountName);
            }

            svc.Seed("account", OtherAccount, "name", "Some firm");
            if (parentAccount.HasValue)
            {
                svc.Seed("contact", Contact, "parentcustomerid", new EntityReference("account", parentAccount.Value));
            }
            else
            {
                svc.Seed("contact", Contact);
            }

            return svc;
        }

        private static EntityReferenceCollection Roles(params Guid[] ids)
        {
            var refs = new EntityReferenceCollection();
            foreach (var id in ids)
            {
                refs.Add(new EntityReference(WebRoleRegistry.RoleEntity, id));
            }

            return refs;
        }

        private static EntityReference ContactRef => new EntityReference("contact", Contact);

        private static EntityReference ParentOf(FakeOrganizationService svc) =>
            svc.Row("contact", Contact).GetAttributeValue<EntityReference>("parentcustomerid");

        [Fact]
        public void Granting_aqs_reviewer_puts_the_contact_under_the_aqs_team_account()
        {
            var svc = Environment();

            AqsQueueMembershipPlugin.Apply(svc, true, WebRoleLink, ContactRef, Roles(AqsRole));

            Assert.Equal(QueueAccount, ParentOf(svc).Id);
            Assert.Equal("account", ParentOf(svc).LogicalName);
        }

        [Fact]
        public void A_grant_made_from_the_role_side_is_handled_the_same_way()
        {
            // The Power Pages admin screens associate from the web role, not the contact.
            var svc = Environment();
            var contacts = new EntityReferenceCollection { ContactRef };

            AqsQueueMembershipPlugin.Apply(
                svc, true, WebRoleLink, new EntityReference(WebRoleRegistry.RoleEntity, AqsRole), contacts);

            Assert.Equal(QueueAccount, ParentOf(svc).Id);
        }

        [Fact]
        public void Granting_another_role_changes_nothing()
        {
            var svc = Environment();

            AqsQueueMembershipPlugin.Apply(svc, true, WebRoleLink, ContactRef, Roles(TaxRole));

            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void Another_relationship_is_ignored()
        {
            var svc = Environment();

            AqsQueueMembershipPlugin.Apply(svc, true, new Relationship("contact_something_else"), ContactRef, Roles(AqsRole));

            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void A_contact_already_in_the_queue_is_not_rewritten()
        {
            var svc = Environment(QueueAccount);

            AqsQueueMembershipPlugin.Apply(svc, true, WebRoleLink, ContactRef, Roles(AqsRole));

            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void A_contact_under_another_account_is_refused_rather_than_moved()
        {
            // Moving it would silently take away whatever that account grants; leaving it
            // would grant a role whose queue stays empty. Refusing says which.
            var svc = Environment(OtherAccount);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                AqsQueueMembershipPlugin.Apply(svc, true, WebRoleLink, ContactRef, Roles(AqsRole)));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, ex.Message);
            Assert.Contains(CaseAccessReconciler.AqsQueueAccountName, ex.Message);
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void Withdrawing_aqs_reviewer_takes_the_contact_out_of_the_queue()
        {
            var svc = Environment(QueueAccount);

            AqsQueueMembershipPlugin.Apply(svc, false, WebRoleLink, ContactRef, Roles(AqsRole));

            Assert.Null(ParentOf(svc));
            Assert.Single(svc.Updates);
        }

        [Fact]
        public void Withdrawing_leaves_any_other_parent_account_alone()
        {
            var svc = Environment(OtherAccount);

            AqsQueueMembershipPlugin.Apply(svc, false, WebRoleLink, ContactRef, Roles(AqsRole));

            Assert.Equal(OtherAccount, ParentOf(svc).Id);
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void A_grant_where_the_queue_account_is_missing_is_refused()
        {
            var svc = Environment(withQueueAccount: false);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                AqsQueueMembershipPlugin.Apply(svc, true, WebRoleLink, ContactRef, Roles(AqsRole)));

            Assert.Contains("ensureaccessprincipals", ex.Message);
        }

        [Fact]
        public void The_step_entry_reads_the_association_from_the_context()
        {
            var svc = Environment();
            var provider = new FakeServiceProvider(svc);
            provider.Context.MessageName = "Associate";
            provider.Context.InputParameters["Target"] = ContactRef;
            provider.Context.InputParameters["Relationship"] = WebRoleLink;
            provider.Context.InputParameters["RelatedEntities"] = Roles(AqsRole);

            new AqsQueueMembershipPlugin(null, null).Execute(provider);

            Assert.Equal(QueueAccount, ParentOf(svc).Id);
            Assert.Single(svc.Updates, u => u.LogicalName == "contact");
        }
    }
}
