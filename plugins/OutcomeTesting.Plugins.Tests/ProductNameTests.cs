using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The product is called OTIS in PROD and "Outcome Testing" everywhere else (owner,
    /// 2026-10-01). The name is a label in some places and a KEY in others - the manager web
    /// role, the security roles, the two teams and the AQS queue account are all found by
    /// name - so every one of them is derived from the one per-environment value, and an
    /// environment that sets nothing must behave exactly as it did before.
    /// </summary>
    public class ProductNameTests
    {
        private static readonly Guid DefinitionId = Guid.Parse("b1b2c3d4-0000-4000-8000-000000000002");

        private static FakeOrganizationService WithVariable(string defaultValue, string currentValue)
        {
            var service = new FakeOrganizationService();
            service.Seed(
                "environmentvariabledefinition", DefinitionId,
                "schemaname", ProductName.Variable,
                "defaultvalue", defaultValue);

            if (currentValue != null)
            {
                service.Seed(
                    "environmentvariablevalue", Guid.NewGuid(),
                    "environmentvariabledefinitionid",
                    new EntityReference("environmentvariabledefinition", DefinitionId),
                    "value", currentValue);
            }

            return service;
        }

        [Fact]
        public void An_environment_with_no_variable_keeps_the_old_name()
        {
            Assert.Equal("Outcome Testing", ProductName.Read(new FakeOrganizationService()));
        }

        [Fact]
        public void The_shipped_default_is_read_when_no_value_is_set()
        {
            Assert.Equal("Outcome Testing", ProductName.Read(WithVariable("Outcome Testing", null)));
        }

        [Fact]
        public void A_value_set_on_the_environment_wins()
        {
            Assert.Equal("OTIS", ProductName.Read(WithVariable("Outcome Testing", "OTIS")));
        }

        [Fact]
        public void A_blank_or_padded_value_is_not_taken_literally()
        {
            Assert.Equal("Outcome Testing", ProductName.Read(WithVariable("Outcome Testing", "   ")));
            Assert.Equal("OTIS", ProductName.Read(WithVariable(null, "  OTIS ")));
        }

        [Fact]
        public void A_failure_reading_the_configuration_falls_back_rather_than_throwing()
        {
            var service = WithVariable("Outcome Testing", "OTIS");
            service.RetrieveMultipleThrows = new InvalidOperationException("no privilege");

            Assert.Equal("Outcome Testing", ProductName.Read(service));
        }

        [Fact]
        public void Every_derived_name_under_the_default_is_the_name_in_use_today()
        {
            // The literals that were hard-coded before this change. If one drifts, DEV and
            // TEST stop finding their own roles and teams.
            const string p = ProductName.Default;
            Assert.Equal("AL Portal - Outcome Testing Manager", ProductName.ManagerWebRole(p));
            Assert.Equal("Outcome Testing App User", ProductName.AppUserRole(p));
            Assert.Equal("Outcome Testing App Admin", ProductName.AppAdminRole(p));
            Assert.Equal("Outcome Testing Team Manager", ProductName.TeamManagerRole(p));
            Assert.Equal("Outcome Testing - Tax Team", ProductName.TaxTeam(p));
            Assert.Equal("Outcome Testing - AQS Team", ProductName.AqsTeam(p));
            Assert.Equal("Outcome Testing - AQS Team", ProductName.AqsQueueAccount(p));
            Assert.Equal("Outcome Testing - Checker Checklist", ProductName.ChecklistTitle(p));
            Assert.Equal("Outcome Testing Checker Checklist | V5 Draft", ProductName.ChecklistFooter(p));
            Assert.Equal("Outcome Testing — Remediation and escalation | V8", ProductName.RemediationFooter(p));
        }

        [Fact]
        public void Every_derived_name_under_OTIS()
        {
            const string p = "OTIS";
            Assert.Equal("AL Portal - OTIS Manager", ProductName.ManagerWebRole(p));
            Assert.Equal("OTIS App User", ProductName.AppUserRole(p));
            Assert.Equal("OTIS App Admin", ProductName.AppAdminRole(p));
            Assert.Equal("OTIS Team Manager", ProductName.TeamManagerRole(p));
            Assert.Equal("OTIS - Tax Team", ProductName.TaxTeam(p));
            Assert.Equal("OTIS - AQS Team", ProductName.AqsTeam(p));
            Assert.Equal("OTIS - AQS Team", ProductName.AqsQueueAccount(p));
            Assert.Equal("OTIS - Checker Checklist", ProductName.ChecklistTitle(p));
            Assert.Equal("OTIS Checker Checklist | V5 Draft", ProductName.ChecklistFooter(p));
            Assert.Equal("OTIS — Remediation and escalation | V8", ProductName.RemediationFooter(p));
        }

        [Fact]
        public void The_OTIS_manager_allocates_either_check_and_the_old_name_does_not()
        {
            Assert.True(AllocationScope.MayAllocate(new[] { "AL Portal - OTIS Manager" }, ResponseRules.ReviewTypeTax, "OTIS"));
            Assert.True(AllocationScope.MayAllocate(new[] { "AL Portal - OTIS Manager" }, ResponseRules.ReviewTypeAqs, "OTIS"));
            Assert.False(AllocationScope.MayAllocate(new[] { "AL Portal - Outcome Testing Manager" }, ResponseRules.ReviewTypeTax, "OTIS"));
        }

        [Fact]
        public void Allocation_under_the_default_is_unchanged()
        {
            Assert.True(AllocationScope.MayAllocate(new[] { "AL Portal - Outcome Testing Manager" }, ResponseRules.ReviewTypeTax));
            Assert.False(AllocationScope.MayAllocate(new[] { "AL Portal - OTIS Manager" }, ResponseRules.ReviewTypeTax));
        }

        [Fact]
        public void The_reconciler_finds_the_teams_by_the_environments_name()
        {
            var service = WithVariable("Outcome Testing", "OTIS");
            var tax = Guid.NewGuid();
            var aqs = Guid.NewGuid();
            service.Seed("team", tax, "name", "OTIS - Tax Team");
            service.Seed("team", aqs, "name", "OTIS - AQS Team");
            service.Seed("account", Guid.NewGuid(), "name", "OTIS - AQS Team");

            Assert.NotNull(CaseAccessReconciler.AqsQueueAccount(service));
        }

        [Fact]
        public void The_reconciler_refuses_by_the_name_it_looked_for()
        {
            // An OTIS environment holding only the old-named account is misconfigured, and the
            // refusal has to say which name was missing.
            var service = WithVariable("Outcome Testing", "OTIS");
            service.Seed("account", Guid.NewGuid(), "name", "Outcome Testing - AQS Team");

            var refusal = Assert.Throws<InvalidPluginExecutionException>(() => CaseAccessReconciler.AqsQueueAccount(service));
            Assert.Contains("OTIS - AQS Team", refusal.Message);
        }
    }
}
