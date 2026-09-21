using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The server's rules for a dropdown whose options are rows (project owner, 2026-09-21:
    /// "How easy would it be to create a management page where users can add, edit, and remove
    /// options?").
    ///
    /// The management page is not the gate and never can be: al_UpdateCaseDetails and the
    /// portal's header edit both take an option id straight from a caller, and neither goes
    /// near the page. These pin what the SERVER refuses - which is the whole of NFR-SEC-01 as
    /// it applies here.
    /// </summary>
    public class ListOptionRulesTests
    {
        private static readonly Guid CaseId = Guid.Parse("cccccccc-1111-4111-8111-cccccccccccc");
        private static readonly DateTime Today = new DateTime(2026, 9, 21);

        private static Guid Option(
            FakeOrganizationService service,
            string name,
            int list,
            DateTime? from = null,
            DateTime? to = null)
        {
            var id = Guid.NewGuid();
            var values = new List<object>
            {
                ListOptionRules.NameAttribute, name,
                ListOptionRules.ListAttribute, new OptionSetValue(list),
                "statecode", 0,
            };
            if (from.HasValue) { values.Add(ListOptionRules.EffectiveFromAttribute); values.Add(from.Value); }
            if (to.HasValue) { values.Add(ListOptionRules.EffectiveToAttribute); values.Add(to.Value); }

            service.Seed(ListOptionRules.Entity, id, values.ToArray());
            return id;
        }

        private static List<string> Apply(FakeOrganizationService service, string optionId)
        {
            var changes = new List<string>();
            UpdateCaseDetailsPlugin.ApplyFields(
                service,
                new Dictionary<string, string> { { ListOptionRules.ProductTypeAttribute, optionId } },
                new Entity("al_outcomecase", CaseId),
                new Entity("al_outcomecase", CaseId),
                changes,
                new OptionLabels(service));
            return changes;
        }

        // --- the window ------------------------------------------------------------------

        [Fact]
        public void An_option_is_in_force_from_its_start_day_up_to_but_not_on_its_end_day()
        {
            // from <= day < to, the same window the checklist reads (BR-013 / AD-015). Written
            // out rather than assumed, because a half-open window that quietly became closed
            // would offer a retired option for one extra day and nobody would notice.
            Assert.True(ListOptionRules.InForce(null, null, Today));
            Assert.True(ListOptionRules.InForce(Today, null, Today));
            Assert.True(ListOptionRules.InForce(null, Today.AddDays(1), Today));
            Assert.False(ListOptionRules.InForce(Today.AddDays(1), null, Today));
            Assert.False(ListOptionRules.InForce(null, Today, Today));
        }

        [Fact]
        public void The_time_of_day_does_not_decide_it()
        {
            // A retire stamped at 16:00 must not still be offered at 17:00 on the same day.
            Assert.False(ListOptionRules.InForce(null, Today.AddHours(16), Today.AddHours(17)));
        }

        // --- what the command accepts -----------------------------------------------------

        [Fact]
        public void A_case_can_be_pointed_at_an_option_that_is_offered()
        {
            var service = new FakeOrganizationService();
            var id = Option(service, "Accumulation Pension", ListOptionRules.ProductSolutionType);

            var changes = Apply(service, id.ToString());

            Assert.Single(changes);
            Assert.Contains("Accumulation Pension", changes[0]);
        }

        [Fact]
        public void The_audit_names_the_option_rather_than_its_id()
        {
            // A change line reading a GUID tells a reader nothing about what was done.
            var service = new FakeOrganizationService();
            var id = Option(service, "IHT", ListOptionRules.ProductSolutionType);

            var changes = Apply(service, id.ToString());

            Assert.DoesNotContain(id.ToString(), changes[0]);
            Assert.Contains("(none)", changes[0]);
            Assert.Contains("IHT", changes[0]);
        }

        [Fact]
        public void Clearing_the_field_is_allowed_and_recorded()
        {
            var service = new FakeOrganizationService();

            var changes = Apply(service, string.Empty);

            Assert.Single(changes);
            Assert.Contains("(none)", changes[0]);
        }

        // --- what the command refuses -----------------------------------------------------

        [Fact]
        public void An_option_from_a_DIFFERENT_list_is_refused()
        {
            // THE one that matters most. Every list's options share one table, so without this
            // check a sample source could be written into the product type field and would read
            // back afterwards as a perfectly ordinary value - wrong, and invisible.
            var service = new FakeOrganizationService();
            var id = Option(service, "Thematic review", ListOptionRules.SampleSource);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => Apply(service, id.ToString()));

            Assert.Contains("is not an option on that list", error.Message);
        }

        [Fact]
        public void A_retired_option_cannot_be_newly_chosen()
        {
            // Retiring exists to stop an option being chosen from now on. A command that still
            // accepted one would make the management page's central promise untrue for anybody
            // not using the page - which is every caller that matters here.
            var service = new FakeOrganizationService();
            var id = Option(
                service, "Drawdown", ListOptionRules.ProductSolutionType, null, Today.AddDays(-1));

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => Apply(service, id.ToString()));

            Assert.Contains("retired", error.Message);
            Assert.Contains("Drawdown", error.Message);
        }

        [Fact]
        public void An_option_that_has_not_started_yet_cannot_be_chosen()
        {
            var service = new FakeOrganizationService();
            var id = Option(
                service, "Bulk transfer", ListOptionRules.ProductSolutionType, Today.AddDays(1), null);

            Assert.Throws<InvalidPluginExecutionException>(() => Apply(service, id.ToString()));
        }

        [Fact]
        public void An_option_that_does_not_exist_is_refused_without_echoing_the_id_back()
        {
            var service = new FakeOrganizationService();
            var missing = Guid.NewGuid();

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => Apply(service, missing.ToString()));

            Assert.Contains("is not an option on that list", error.Message);
            Assert.DoesNotContain(missing.ToString(), error.Message);
        }

        [Fact]
        public void Something_that_is_not_an_id_at_all_is_refused()
        {
            var service = new FakeOrganizationService();

            Assert.Throws<InvalidPluginExecutionException>(() => Apply(service, "Accumulation Pension"));
        }

        // --- the field itself --------------------------------------------------------------

        [Fact]
        public void The_lookup_is_editable_and_so_is_the_choice_column_it_supersedes()
        {
            // Both, deliberately. Cases imported before the lookup existed carry only the
            // integer, and until every one of them is backfilled taking the choice column away
            // would leave a case with a product type nobody could correct.
            var service = new FakeOrganizationService();
            var id = Option(service, "Protection", ListOptionRules.ProductSolutionType);

            var changes = new List<string>();
            UpdateCaseDetailsPlugin.ApplyFields(
                service,
                new Dictionary<string, string>
                {
                    { ListOptionRules.ProductTypeAttribute, id.ToString() },
                    { ListOptionRules.ProductTypeLegacyAttribute, "120910523" },
                },
                new Entity("al_outcomecase", CaseId),
                new Entity("al_outcomecase", CaseId),
                changes,
                new OptionLabels(service));

            Assert.Equal(2, changes.Count);
        }

        [Fact]
        public void The_list_values_match_the_ones_the_app_and_the_table_use()
        {
            // Three places name these: here, app/src/features/admin/listOptions.ts, and the
            // al_list choice column the registration tool creates. A drift between them would
            // not fail loudly - it would make one list silently read as another.
            Assert.Equal(120910840, ListOptionRules.ProductSolutionType);
            Assert.Equal(120910841, ListOptionRules.SampleSource);
            Assert.Equal(120910842, ListOptionRules.CaseType);
            Assert.Equal(120910843, ListOptionRules.PreOrPostCheck);
        }
    }
}
