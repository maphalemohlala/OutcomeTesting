using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A T&amp;C Manager changed on the mapping takes the adviser's cases with it, including one
    /// already waiting for sign-off. Until now such a case stayed with the old coach.
    /// </summary>
    public class AdviserMappingPluginTests
    {
        private static readonly Guid CaseId = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000001");
        private static readonly Guid OldCoach = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000002");
        private static readonly Guid NewCoach = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000003");
        private static readonly Guid MappingId = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000004");

        private const string AdviserEmail = "ann@example.com";
        private const string NewCoachEmail = "new.coach@example.com";

        private static FakeOrganizationService World(int status, Guid? mappedCoach)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("team", Guid.NewGuid(), "name", ProductName.TaxTeam(ProductName.Default));
            svc.Seed("team", Guid.NewGuid(), "name", ProductName.AqsTeam(ProductName.Default));
            svc.Seed("account", Guid.NewGuid(), "name", ProductName.AqsQueueAccount(ProductName.Default));
            svc.Seed("contact", OldCoach, "fullname", "Old Coach", "emailaddress1", "old.coach@example.com", "statecode", new OptionSetValue(0));
            svc.Seed("contact", NewCoach, "fullname", "New Coach", "emailaddress1", NewCoachEmail, "statecode", new OptionSetValue(0));
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-COACH-1",
                "al_advisername", "Ann Adviser",
                "al_adviseremail", AdviserEmail,
                "al_casestatus", new OptionSetValue(status),
                CaseAccessReconciler.SupervisorAttr, new EntityReference("contact", OldCoach));
            svc.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted));
            if (mappedCoach.HasValue)
            {
                svc.Seed("al_advisermapping", MappingId,
                    "al_adviseremail", AdviserEmail,
                    "al_tcmanagerid", new EntityReference("contact", mappedCoach.Value),
                    "statecode", new OptionSetValue(0));
            }

            return svc;
        }

        private static FakePluginExecutionContext Update(Entity preImage = null)
        {
            var context = new FakeServiceProvider().Context;
            context.MessageName = "Update";
            context.PrimaryEntityName = "al_advisermapping";
            context.PrimaryEntityId = MappingId;
            context.InputParameters["Target"] = new Entity("al_advisermapping", MappingId)
            {
                ["al_tcmanagerid"] = new EntityReference("contact", NewCoach),
            };
            if (preImage != null)
            {
                context.PreEntityImages[AdviserMappingPlugin.PreImageName] = preImage;
            }

            return context;
        }

        private static EntityReference Coach(FakeOrganizationService svc)
        {
            return svc.Row("al_outcomecase", CaseId).GetAttributeValue<EntityReference>(CaseAccessReconciler.SupervisorAttr);
        }

        private static Entity[] Letters(FakeOrganizationService svc)
        {
            return svc.Creates.Where(c => c.LogicalName == "al_notification").ToArray();
        }

        [Fact]
        public void A_case_waiting_for_sign_off_moves_to_the_new_coach_who_is_told()
        {
            var svc = World(CaseLifecycle.AwaitingSignoff, NewCoach);

            Assert.Equal(1, AdviserMappingPlugin.Apply(Update(), svc));

            Assert.Equal(NewCoach, Coach(svc).Id);
            var letter = Assert.Single(Letters(svc));
            Assert.Equal(NewCoachEmail, letter.GetAttributeValue<string>("al_recipientemail"));
        }

        [Fact]
        public void A_case_not_yet_waiting_moves_without_a_letter()
        {
            var svc = World(CaseLifecycle.RemediationInProgress, NewCoach);

            Assert.Equal(1, AdviserMappingPlugin.Apply(Update(), svc));

            Assert.Equal(NewCoach, Coach(svc).Id);
            Assert.Empty(Letters(svc));
        }

        [Fact]
        public void Saving_the_same_coach_again_moves_nothing_and_tells_nobody()
        {
            var svc = World(CaseLifecycle.AwaitingSignoff, OldCoach);

            Assert.Equal(0, AdviserMappingPlugin.Apply(Update(), svc));

            Assert.Equal(OldCoach, Coach(svc).Id);
            Assert.Empty(Letters(svc));
        }

        [Fact]
        public void A_removed_mapping_takes_the_case_off_the_old_coach()
        {
            var svc = World(CaseLifecycle.AwaitingSignoff, null);
            var context = new FakeServiceProvider().Context;
            context.MessageName = "Delete";
            context.InputParameters["Target"] = new EntityReference("al_advisermapping", MappingId);
            context.PreEntityImages[AdviserMappingPlugin.PreImageName] =
                new Entity("al_advisermapping", MappingId) { ["al_adviseremail"] = AdviserEmail };

            Assert.Equal(1, AdviserMappingPlugin.Apply(context, svc));

            Assert.Null(Coach(svc));
            Assert.Empty(Letters(svc));
        }

        [Fact]
        public void An_email_change_follows_both_addresses_once_each()
        {
            var context = Update(new Entity("al_advisermapping", MappingId) { ["al_adviseremail"] = "ANN@example.com " });
            ((Entity)context.InputParameters["Target"])["al_adviseremail"] = AdviserEmail;
            Assert.Single(AdviserMappingPlugin.EmailsOf(context, new FakeOrganizationService()));

            ((Entity)context.InputParameters["Target"])["al_adviseremail"] = "ann.new@example.com";
            Assert.Equal(2, AdviserMappingPlugin.EmailsOf(context, new FakeOrganizationService()).Count);
        }
    }
}
