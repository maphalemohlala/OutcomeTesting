using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The adviser status OTIS holds, set by an administrator, beats IO (client, 2026-11-25).
    /// </summary>
    public class AdviserStatusTests
    {
        private const int Cas = 120910501;
        private const string Email = "ann@example.com";
        private static readonly Guid CaseId = Guid.Parse("ad0a0000-0000-4000-8000-000000000001");
        private static readonly Guid PatRow = Guid.Parse("ad0a0000-0000-4000-8000-000000000002");
        private static readonly Guid SamRow = Guid.Parse("ad0a0000-0000-4000-8000-000000000003");

        private static FakeOrganizationService World(int? held, int? onCase = null, string ticked = null,
            int caseStatus = CaseLifecycle.Queued)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("team", Guid.NewGuid(), "name", ProductName.TaxTeam(ProductName.Default));
            svc.Seed("team", Guid.NewGuid(), "name", ProductName.AqsTeam(ProductName.Default));
            svc.Seed("account", Guid.NewGuid(), "name", ProductName.AqsQueueAccount(ProductName.Default));
            svc.Seed("al_outcomecase", CaseId,
                "al_adviseremail", Email,
                "al_casestatus", new OptionSetValue(caseStatus),
                "statecode", new OptionSetValue(0),
                "al_checklistitems", ticked,
                AdviserStatus.Attr, onCase.HasValue ? new OptionSetValue(onCase.Value) : null);
            Map(svc, PatRow, held);
            Map(svc, SamRow, held);
            return svc;
        }

        private static void Map(FakeOrganizationService svc, Guid row, int? status)
        {
            var manager = Guid.NewGuid();
            svc.Seed("contact", manager, "fullname", "Manager", "emailaddress1", manager + "@example.com",
                "statecode", new OptionSetValue(0));
            svc.Seed("al_advisermapping", row,
                "al_adviseremail", Email,
                "al_tcmanagerid", new EntityReference("contact", manager),
                "statecode", new OptionSetValue(0),
                AdviserStatus.Attr, status.HasValue ? new OptionSetValue(status.Value) : null);
        }

        private static Entity Case(FakeOrganizationService svc)
        {
            return svc.Retrieve("al_outcomecase", CaseId, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));
        }

        private static int? StatusOf(FakeOrganizationService svc, string table, Guid id)
        {
            var value = svc.Retrieve(table, id, new Microsoft.Xrm.Sdk.Query.ColumnSet(AdviserStatus.Attr))
                .GetAttributeValue<OptionSetValue>(AdviserStatus.Attr);
            return value == null ? (int?)null : value.Value;
        }

        [Fact]
        public void Otis_signed_off_beats_the_io_tick()
        {
            var svc = World(held: Cas, ticked: "Pre-CAS Adviser");
            Assert.False(AdviserStatus.IsNewEntrant(svc, Case(svc)));
        }

        [Fact]
        public void Otis_pre_cas_beats_io_saying_nothing()
        {
            var svc = World(held: AdviserStatus.PreCas);
            Assert.True(AdviserStatus.IsNewEntrant(svc, Case(svc)));
        }

        [Fact]
        public void Without_an_otis_status_io_still_decides()
        {
            Assert.True(AdviserStatus.IsNewEntrant(World(null, ticked: "Pre-CAS Adviser"), Case(World(null, ticked: "Pre-CAS Adviser"))));
            var plain = World(null);
            Assert.False(AdviserStatus.IsNewEntrant(plain, Case(plain)));
        }

        [Fact]
        public void A_case_edit_away_from_the_otis_status_is_refused()
        {
            var svc = World(held: Cas, onCase: Cas);
            var error = Assert.Throws<InvalidPluginExecutionException>(() => AdviserStatus.EnsureCaseWriteAllowed(svc,
                new Entity("al_outcomecase", CaseId) { [AdviserStatus.Attr] = new OptionSetValue(AdviserStatus.PreCas) }));
            Assert.StartsWith(CommandHelpers.PreconditionPrefix, error.Message);
        }

        [Fact]
        public void Resaving_the_value_already_on_the_case_passes()
        {
            // A closed case keeps its old status; a header save that sends it back unchanged must not fail.
            var svc = World(held: Cas, onCase: AdviserStatus.PreCas, caseStatus: CaseLifecycle.Closed);
            AdviserStatus.EnsureCaseWriteAllowed(svc,
                new Entity("al_outcomecase", CaseId) { [AdviserStatus.Attr] = new OptionSetValue(AdviserStatus.PreCas) });
        }

        [Fact]
        public void Without_an_otis_status_the_case_field_stays_editable()
        {
            var svc = World(held: null);
            AdviserStatus.EnsureCaseWriteAllowed(svc,
                new Entity("al_outcomecase", CaseId) { [AdviserStatus.Attr] = new OptionSetValue(AdviserStatus.PreCas) });
        }

        [Fact]
        public void Setting_the_status_on_one_row_sets_the_advisers_other_rows_and_open_cases()
        {
            var svc = World(held: null);
            svc.Update(new Entity("al_advisermapping", PatRow) { [AdviserStatus.Attr] = new OptionSetValue(AdviserStatus.PreCas) });
            var context = new FakePluginExecutionContext
            {
                MessageName = "Update",
                Depth = 1,
                InputParameters = new ParameterCollection
                {
                    { "Target", new Entity("al_advisermapping", PatRow) { [AdviserStatus.Attr] = new OptionSetValue(AdviserStatus.PreCas) } },
                },
            };

            AdviserMappingPlugin.Apply(context, svc);

            Assert.Equal(AdviserStatus.PreCas, StatusOf(svc, "al_advisermapping", SamRow));
            Assert.Equal(AdviserStatus.PreCas, StatusOf(svc, "al_outcomecase", CaseId));
        }

        [Fact]
        public void A_closed_case_keeps_the_status_it_was_checked_under()
        {
            var svc = World(held: Cas, onCase: AdviserStatus.PreCas, caseStatus: CaseLifecycle.Closed);
            CaseAccessReconciler.Reconcile(svc, CaseId, DateTime.UtcNow);
            Assert.Equal(AdviserStatus.PreCas, StatusOf(svc, "al_outcomecase", CaseId));
        }

        [Fact]
        public void A_new_manager_row_takes_the_advisers_status()
        {
            var svc = World(held: Cas);
            var added = Guid.NewGuid();
            Map(svc, added, null);
            var context = new FakePluginExecutionContext
            {
                MessageName = "Create",
                Depth = 1,
                PrimaryEntityId = added,
                InputParameters = new ParameterCollection { { "Target", new Entity("al_advisermapping", added) } },
            };

            AdviserStatus.SyncMappingRows(context, svc);

            Assert.Equal(Cas, StatusOf(svc, "al_advisermapping", added));
        }

        [Fact]
        public void Only_a_holder_of_adviser_status_may_set_it()
        {
            var svc = World(held: null);
            var caller = Guid.NewGuid();
            svc.Seed("systemuser", caller, "internalemailaddress", "otm@example.com");
            svc.Seed(ContactRegistry.Entity, Guid.NewGuid(),
                ContactRegistry.EmailAttr, "otm@example.com",
                ContactRegistry.StateCodeAttr, new OptionSetValue(ContactRegistry.StateActive));
            svc.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", "otm@example.com", "al_rolecode", "Manager", "statecode", new OptionSetValue(0));
            svc.Seed("al_pagepermission", Guid.NewGuid(),
                "al_resourcekey", "page.admin.advisers", "al_rolecode", "Manager",
                "al_accesslevel", new OptionSetValue(PermissionHelpers.AccessManage), "statecode", new OptionSetValue(0));
            svc.FetchResults.Enqueue(new EntityCollection());
            svc.FetchResults.Enqueue(new EntityCollection());

            var context = new FakePluginExecutionContext
            {
                MessageName = "Update",
                Depth = 1,
                UserId = caller,
                InitiatingUserId = caller,
                InputParameters = new ParameterCollection
                {
                    { "Target", new Entity("al_advisermapping", PatRow) { [AdviserStatus.Attr] = new OptionSetValue(Cas) } },
                },
            };

            var error = Assert.Throws<InvalidPluginExecutionException>(() => AdviserStatus.SyncMappingRows(context, svc));
            Assert.Contains(AdviserStatus.Resource, error.Message);
        }
    }
}
