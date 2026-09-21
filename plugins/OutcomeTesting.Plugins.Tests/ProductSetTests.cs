using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The products a case covers (project owner, 2026-09-21: "turn the products page into
    /// dropdown with 4 placeholders", multi-select confirmed).
    ///
    /// A SET, not a choice. The column it replaces is labelled "Product(s)" and the seeded
    /// fixture is "Pension; ISA", so a single lookup would have made the field able to record
    /// less than the free text it replaces. That difference is the whole of what these pin.
    /// </summary>
    public class ProductSetTests
    {
        private static readonly Guid CaseId = Guid.Parse("dddddddd-1111-4111-8111-dddddddddddd");
        private static readonly DateTime Today = new DateTime(2026, 9, 21);

        private static Guid Option(
            FakeOrganizationService service, string name, int list, DateTime? to = null)
        {
            var id = Guid.NewGuid();
            var values = new List<object>
            {
                ListOptionRules.NameAttribute, name,
                ListOptionRules.ListAttribute, new OptionSetValue(list),
                "statecode", 0,
            };
            if (to.HasValue) { values.Add(ListOptionRules.EffectiveToAttribute); values.Add(to.Value); }
            service.Seed(ListOptionRules.Entity, id, values.ToArray());
            return id;
        }

        private static Guid Product(FakeOrganizationService service, string name, DateTime? to = null)
        {
            return Option(service, name, ListOptionRules.Products, to);
        }

        private static List<string> Apply(FakeOrganizationService service, params Guid[] ids)
        {
            var changes = new List<string>();
            ListOptionRules.ApplyProducts(
                service, CaseId, string.Join(",", ids.Select(x => x.ToString()).ToArray()), changes, Today);
            return changes;
        }

        // --- what it accepts ---------------------------------------------------------------

        [Fact]
        public void A_case_can_cover_several_products_at_once()
        {
            // THE reason this is a set. A single lookup could not express it, and the free
            // text it replaces already did.
            var service = new FakeOrganizationService();
            var pension = Product(service, "Pension");
            var isa = Product(service, "ISA");

            Apply(service, pension, isa);

            var held = ListOptionRules.CurrentProducts(service, CaseId);
            Assert.Equal(2, held.Count);
            Assert.Contains(pension, held);
            Assert.Contains(isa, held);
        }

        [Fact]
        public void The_payload_is_the_whole_set_so_sending_fewer_removes_the_rest()
        {
            // A payload of additions could never remove one and a payload of removals could
            // never add; sending the set the person is looking at means the saved answer is
            // the one on their screen.
            var service = new FakeOrganizationService();
            var pension = Product(service, "Pension");
            var isa = Product(service, "ISA");
            Apply(service, pension, isa);

            Apply(service, isa);

            var held = ListOptionRules.CurrentProducts(service, CaseId);
            Assert.Single(held);
            Assert.Contains(isa, held);
        }

        [Fact]
        public void An_empty_payload_clears_every_product()
        {
            var service = new FakeOrganizationService();
            Apply(service, Product(service, "Pension"));

            var changes = new List<string>();
            ListOptionRules.ApplyProducts(service, CaseId, string.Empty, changes, Today);

            Assert.Empty(ListOptionRules.CurrentProducts(service, CaseId));
            Assert.Single(changes);
            Assert.Contains("(none)", changes[0]);
        }

        [Fact]
        public void Sending_the_same_set_again_records_no_change()
        {
            // A page that re-sends what is already recorded has asked for the state it has.
            var service = new FakeOrganizationService();
            var pension = Product(service, "Pension");
            Apply(service, pension);

            Assert.Empty(Apply(service, pension));
        }

        [Fact]
        public void The_same_product_named_twice_is_attached_once()
        {
            var service = new FakeOrganizationService();
            var pension = Product(service, "Pension");

            Apply(service, pension, pension);

            Assert.Single(ListOptionRules.CurrentProducts(service, CaseId));
        }

        // --- what it refuses ---------------------------------------------------------------

        [Fact]
        public void An_option_from_another_list_cannot_be_a_product()
        {
            // All five lists share one table, so without this a sample source could be
            // recorded as a product and would read back as an ordinary one.
            var service = new FakeOrganizationService();
            var sampleSource = Option(service, "Thematic", ListOptionRules.SampleSource);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => Apply(service, sampleSource));

            Assert.Contains("is not an option on that list", error.Message);
        }

        [Fact]
        public void A_retired_product_cannot_be_newly_added()
        {
            var service = new FakeOrganizationService();
            var gone = Product(service, "Endowment", Today.AddDays(-1));

            var error = Assert.Throws<InvalidPluginExecutionException>(() => Apply(service, gone));

            Assert.Contains("retired", error.Message);
            Assert.Contains("Endowment", error.Message);
        }

        [Fact]
        public void A_retired_product_a_case_ALREADY_covers_is_left_alone()
        {
            // Retiring stops an option being chosen from now on; it does not rewrite the
            // cases that already carry it. Without this, a checker editing any other header
            // field on an old case would be refused because of a product they did not touch.
            var service = new FakeOrganizationService();
            var pension = Product(service, "Pension");
            var endowment = Product(service, "Endowment");
            Apply(service, pension, endowment);

            // Endowment is retired after the fact.
            service.Seed(
                ListOptionRules.Entity, endowment,
                ListOptionRules.NameAttribute, "Endowment",
                ListOptionRules.ListAttribute, new OptionSetValue(ListOptionRules.Products),
                ListOptionRules.EffectiveToAttribute, Today.AddDays(-1),
                "statecode", 0);

            Apply(service, pension, endowment);

            Assert.Equal(2, ListOptionRules.CurrentProducts(service, CaseId).Count);
        }

        [Fact]
        public void One_bad_id_changes_nothing_rather_than_half_applying()
        {
            // Validated before anything is written. A partly-applied set is a case whose
            // products nobody chose.
            var service = new FakeOrganizationService();
            var pension = Product(service, "Pension");
            var sampleSource = Option(service, "Thematic", ListOptionRules.SampleSource);

            Assert.Throws<InvalidPluginExecutionException>(
                () => Apply(service, pension, sampleSource));

            Assert.Empty(ListOptionRules.CurrentProducts(service, CaseId));
        }

        [Fact]
        public void Something_that_is_not_an_id_at_all_is_refused()
        {
            var service = new FakeOrganizationService();

            var changes = new List<string>();
            Assert.Throws<InvalidPluginExecutionException>(
                () => ListOptionRules.ApplyProducts(service, CaseId, "Pension", changes, Today));
        }

        // --- the audit ---------------------------------------------------------------------

        [Fact]
        public void The_audit_names_the_products_rather_than_their_ids()
        {
            // A change line of GUIDs tells a reader nothing about what was done.
            var service = new FakeOrganizationService();
            var pension = Product(service, "Pension");
            var isa = Product(service, "ISA");

            var changes = Apply(service, pension, isa);

            Assert.Single(changes);
            Assert.DoesNotContain(pension.ToString(), changes[0]);
            Assert.Contains("Pension", changes[0]);
            Assert.Contains("ISA", changes[0]);
            Assert.Contains("(none)", changes[0]);
        }

        [Fact]
        public void The_audit_reads_the_same_whatever_order_the_page_sent_them_in()
        {
            // Two checkers ticking the same boxes in a different order did the same thing,
            // and an audit trail that says otherwise invites a question with no answer.
            var first = new FakeOrganizationService();
            var a1 = Product(first, "ISA");
            var b1 = Product(first, "Pension");

            var second = new FakeOrganizationService();
            var a2 = Product(second, "ISA");
            var b2 = Product(second, "Pension");

            Assert.Equal(Apply(first, a1, b1)[0], Apply(second, b2, a2)[0]);
        }

        // --- the field ---------------------------------------------------------------------

        [Fact]
        public void The_portal_may_send_the_product_set()
        {
            CaseHeaderRequestPlugin.EnsureCheckerEditable(
                new Dictionary<string, string> { { ListOptionRules.ProductsField, string.Empty } });
        }

        [Fact]
        public void The_free_text_column_it_replaces_is_still_editable()
        {
            // Cases imported before the list existed carry their products as text and have to
            // remain correctable until there is nothing left holding only text.
            CaseHeaderRequestPlugin.EnsureCheckerEditable(
                new Dictionary<string, string> { { ListOptionRules.ProductsLegacyAttribute, "Pension; ISA" } });
        }

        [Fact]
        public void The_products_field_is_not_a_column_on_the_case()
        {
            // It is a set applied by association. Naming it as though it were a column would
            // write a stray attribute onto al_outcomecase.
            Assert.Equal("al_productids", ListOptionRules.ProductsField);
            Assert.Equal("al_products", ListOptionRules.ProductsLegacyAttribute);
            Assert.NotEqual(ListOptionRules.ProductsField, ListOptionRules.ProductsLegacyAttribute);
        }
    }
}
