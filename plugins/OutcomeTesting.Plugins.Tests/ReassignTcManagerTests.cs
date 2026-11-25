using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>Handing one case to another T&amp;C Manager (requirements log row 4).</summary>
    public class ReassignTcManagerTests
    {
        private static readonly Guid CaseId = Guid.Parse("22222222-0000-4000-8000-000000000001");
        private static readonly Guid Mapped = Guid.Parse("22222222-0000-4000-8000-0000000000a1");
        private static readonly Guid Cover = Guid.Parse("22222222-0000-4000-8000-0000000000b2");
        private const string Adviser = "adviser@example.com";

        private static FakeOrganizationService Seeded(Guid? chosen = null)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", Mapped, "contactid", Mapped, "fullname", "Pat Mapped", "emailaddress1", "pat@example.com", "statecode", new OptionSetValue(0));
            svc.Seed("contact", Cover, "contactid", Cover, "fullname", "Cora Cover", "emailaddress1", "cora@example.com", "statecode", new OptionSetValue(0));
            svc.Seed("al_outcomecase", CaseId,
                "al_outcomecaseid", CaseId,
                "al_casereference", "259162944",
                TcManagerRouting.CaseAdviserEmailAttr, Adviser,
                TcManagerRouting.OverrideAttr, chosen.HasValue ? new EntityReference("contact", chosen.Value) : null);
            svc.Seed(TcManagerRouting.MappingEntity, Guid.NewGuid(),
                TcManagerRouting.MappingEmailAttr, Adviser,
                TcManagerRouting.ManagerAttr, new EntityReference("contact", Mapped),
                "statecode", new OptionSetValue(0));
            return svc;
        }

        private static void Holds(FakeOrganizationService svc, params string[] roles)
        {
            var rows = new List<Entity>();
            foreach (var name in roles)
            {
                var row = new Entity("contact", Cover);
                row["role.name"] = new AliasedValue("powerpagecomponent", "name", name);
                rows.Add(row);
            }

            svc.FetchResults.Enqueue(new EntityCollection(rows));
        }

        private static EntityReference Chosen(FakeOrganizationService svc)
        {
            return svc.Retrieve("al_outcomecase", CaseId, new Microsoft.Xrm.Sdk.Query.ColumnSet(TcManagerRouting.OverrideAttr))
                .GetAttributeValue<EntityReference>(TcManagerRouting.OverrideAttr);
        }

        [Fact]
        public void Routing_follows_the_manager_chosen_for_the_case_over_the_mapping()
        {
            var svc = Seeded(Cover);

            var routing = TcManagerRouting.ForCase(svc, new EntityReference("al_outcomecase", CaseId));

            Assert.True(routing.IsRouted);
            Assert.Equal(Cover, Assert.Single(routing.Managers).Id);
            Assert.Equal("cora@example.com", Assert.Single(routing.Recipients).Email);
        }

        [Fact]
        public void Routing_falls_back_to_the_mapping_when_nobody_is_chosen()
        {
            var routing = TcManagerRouting.ForCase(Seeded(), new EntityReference("al_outcomecase", CaseId));

            Assert.Equal(Mapped, Assert.Single(routing.Managers).Id);
        }

        [Fact]
        public void A_T_and_C_supervisor_takes_the_case()
        {
            var svc = Seeded();
            Holds(svc, WebRoleRegistry.TcSupervisorRole);

            var summary = ReassignTcManagerPlugin.Reassign(svc, CaseId, Cover, "Pat is on leave");

            Assert.Equal(Cover, Chosen(svc).Id);
            Assert.Equal("Case 259162944 handed from the adviser's mapped T&C Managers to Cora Cover.", summary);
        }

        [Fact]
        public void No_manager_hands_the_case_back_to_the_mapping()
        {
            var svc = Seeded(Cover);

            var summary = ReassignTcManagerPlugin.Reassign(svc, CaseId, null, "Pat is back");

            Assert.Null(Chosen(svc));
            Assert.Equal("Case 259162944 handed from Cora Cover to the adviser's mapped T&C Managers.", summary);
        }

        [Fact]
        public void Someone_without_the_supervisor_role_is_refused()
        {
            var svc = Seeded();
            Holds(svc, WebRoleRegistry.AqsReviewerRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => ReassignTcManagerPlugin.Reassign(svc, CaseId, Cover, "cover"));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, error.Message);
            Assert.Contains("Cora Cover does not hold the " + WebRoleRegistry.TcSupervisorRole, error.Message);
            Assert.Null(Chosen(svc));
        }

        [Fact]
        public void The_case_adviser_cannot_be_chosen()
        {
            var svc = Seeded();
            svc.Seed("contact", Cover, "contactid", Cover, "fullname", "Ada Adviser", "emailaddress1", " ADVISER@example.com ", "statecode", new OptionSetValue(0));
            Holds(svc, WebRoleRegistry.TcSupervisorRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => ReassignTcManagerPlugin.Reassign(svc, CaseId, Cover, "cover"));

            Assert.Contains("cannot sign off their own case", error.Message);
        }

        [Fact]
        public void A_reason_is_required()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => ReassignTcManagerPlugin.Reassign(Seeded(), CaseId, Cover, "  "));

            Assert.StartsWith(CommandHelpers.ValidationPrefix, error.Message);
        }

        [Fact]
        public void Choosing_the_manager_the_case_already_has_is_refused()
        {
            var svc = Seeded(Cover);
            Holds(svc, WebRoleRegistry.TcSupervisorRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => ReassignTcManagerPlugin.Reassign(svc, CaseId, Cover, "again"));

            Assert.Contains("already with Cora Cover", error.Message);
        }

        [Fact]
        public void A_missing_case_is_not_found()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => ReassignTcManagerPlugin.Reassign(Seeded(), Guid.NewGuid(), Cover, "cover"));

            Assert.StartsWith(CommandHelpers.NotFoundPrefix, error.Message);
        }

        [Fact]
        public void The_chosen_manager_cannot_be_written_directly()
        {
            var target = new Entity("al_outcomecase", CaseId) { [TcManagerRouting.OverrideAttr] = new EntityReference("contact", Cover) };

            Assert.Throws<InvalidPluginExecutionException>(() => CaseAccessGuardPlugin.EnsureNotWrittenDirectly(target, 1));
            CaseAccessGuardPlugin.EnsureNotWrittenDirectly(target, 2);
        }
    }
}
