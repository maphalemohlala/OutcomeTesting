using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>A true delete of a case and everything on it (requirements log row 40).</summary>
    public class DeleteCaseTests
    {
        private static readonly Guid CaseId = Guid.Parse("11111111-0000-4000-8000-000000000001");
        private static readonly Guid OtherCaseId = Guid.Parse("11111111-0000-4000-8000-000000000002");

        /// <summary>Forwards to the fake and records every delete, in order.</summary>
        private sealed class Recording : IOrganizationService
        {
            public Recording(FakeOrganizationService inner) { Inner = inner; }
            public FakeOrganizationService Inner { get; }
            public List<string> Deleted { get; } = new List<string>();
            public Guid Create(Entity entity) => Inner.Create(entity);
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => Inner.Retrieve(entityName, id, columnSet);
            public void Update(Entity entity) => Inner.Update(entity);
            public void Delete(string entityName, Guid id) { Deleted.Add(entityName); Inner.Delete(entityName, id); }
            public OrganizationResponse Execute(OrganizationRequest request) => Inner.Execute(request);
            public void Associate(string a, Guid b, Relationship c, EntityReferenceCollection d) => Inner.Associate(a, b, c, d);
            public void Disassociate(string a, Guid b, Relationship c, EntityReferenceCollection d) => Inner.Disassociate(a, b, c, d);
            public EntityCollection RetrieveMultiple(QueryBase query) => Inner.RetrieveMultiple(query);
        }

        private static Recording Seeded()
        {
            var fake = new FakeOrganizationService();
            fake.Seed("al_outcomecase", CaseId, "al_outcomecaseid", CaseId, "al_casereference", "259162944", "al_clientname", "Mr Client");
            fake.Seed("al_outcomecase", OtherCaseId, "al_outcomecaseid", OtherCaseId, "al_casereference", "OTHER-1");

            var review = Guid.NewGuid();
            var action = Guid.NewGuid();
            foreach (var row in new[]
            {
                ("al_reviewinstance", review), ("al_reviewinstance", Guid.NewGuid()),
                ("al_outcome", Guid.NewGuid()), ("al_remediationaction", action),
                ("al_signoff", Guid.NewGuid()), ("al_caseassignment", Guid.NewGuid()),
                ("al_exportrecord", Guid.NewGuid()),
            })
            {
                fake.Seed(row.Item1, row.Item2, "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            }

            fake.Seed("al_reviewinstance", Guid.NewGuid(), "al_outcomecaseid", new EntityReference("al_outcomecase", OtherCaseId));

            // Unsent letters about the case and about one of its actions; a sent one; one for another case.
            fake.Seed("al_notification", Guid.NewGuid(), "al_status", new OptionSetValue(NotificationOutbox.StatusPending), "al_targetid", CaseId.ToString("D"));
            fake.Seed("al_notification", Guid.NewGuid(), "al_status", new OptionSetValue(NotificationOutbox.StatusPending), "al_targetid", action.ToString("D").ToUpperInvariant());
            fake.Seed("al_notification", Guid.NewGuid(), "al_status", new OptionSetValue(NotificationOutbox.StatusPending + 1), "al_targetid", CaseId.ToString("D"));
            fake.Seed("al_notification", Guid.NewGuid(), "al_status", new OptionSetValue(NotificationOutbox.StatusPending), "al_targetid", OtherCaseId.ToString("D"));
            return new Recording(fake);
        }

        [Fact]
        public void Deletes_the_case_with_everything_recorded_against_it()
        {
            var svc = Seeded();

            var summary = DeleteCasePlugin.Delete(svc, CaseId, " 259162944 ", "Imported in error.");

            Assert.Null(svc.Inner.Row("al_outcomecase", CaseId));
            foreach (var table in new[] { "al_reviewinstance", "al_outcome", "al_remediationaction", "al_signoff", "al_caseassignment", "al_exportrecord" })
            {
                Assert.DoesNotContain(svc.Inner.All(table), e => e.GetAttributeValue<EntityReference>("al_outcomecaseid")?.Id == CaseId);
            }

            Assert.Equal(
                "Deleted case 259162944 (Mr Client) with 1 sign-offs, 1 remedial actions, 1 outcomes, 1 allocations, "
                + "1 export records, 2 checks and 2 unsent letters.",
                summary);
        }

        [Fact]
        public void Leaves_other_cases_and_letters_already_sent()
        {
            var svc = Seeded();

            DeleteCasePlugin.Delete(svc, CaseId, "259162944", "Imported in error.");

            Assert.NotNull(svc.Inner.Row("al_outcomecase", OtherCaseId));
            Assert.Single(svc.Inner.All("al_reviewinstance"));
            Assert.Equal(2, svc.Inner.All("al_notification").Count());
        }

        [Fact]
        public void Deletes_each_row_before_the_rows_its_lookups_point_at()
        {
            var svc = Seeded();

            DeleteCasePlugin.Delete(svc, CaseId, "259162944", "Imported in error.");

            var order = svc.Deleted.Distinct().ToList();
            Assert.Equal(
                new[] { "al_notification", "al_signoff", "al_remediationaction", "al_outcome", "al_caseassignment", "al_exportrecord", "al_reviewinstance", "al_outcomecase" },
                order);
        }

        [Theory]
        [InlineData("259162945")]
        [InlineData("")]
        public void Refuses_and_deletes_nothing_unless_the_reference_is_typed(string typed)
        {
            var svc = Seeded();

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => DeleteCasePlugin.Delete(svc, CaseId, typed, "Imported in error."));

            Assert.Contains("Type the case reference 259162944", error.Message);
            Assert.Empty(svc.Deleted);
        }

        [Fact]
        public void Refuses_without_a_reason()
        {
            var svc = Seeded();

            Assert.Throws<InvalidPluginExecutionException>(() => DeleteCasePlugin.Delete(svc, CaseId, "259162944", "  "));
            Assert.Empty(svc.Deleted);
        }

        [Fact]
        public void Says_so_when_the_case_is_already_gone()
        {
            var svc = Seeded();

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => DeleteCasePlugin.Delete(svc, Guid.NewGuid(), "259162944", "Imported in error."));

            Assert.StartsWith(CommandHelpers.NotFoundPrefix, error.Message);
        }

        [Fact]
        public void Matches_the_reference_ignoring_case_and_spaces_only()
        {
            Assert.True(DeleteCasePlugin.ReferenceMatches("IO-100", " io-100 "));
            Assert.False(DeleteCasePlugin.ReferenceMatches("IO-100", "IO-1000"));
            Assert.False(DeleteCasePlugin.ReferenceMatches("", ""));
        }
    }
}
