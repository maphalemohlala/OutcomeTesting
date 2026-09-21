using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// What Trail Light writes for Product / solution type once the list is managed rows
    /// (AD-187).
    ///
    /// The case can carry both: choosing a managed option writes the lookup and leaves the
    /// choice column at whatever it last held. Getting the precedence wrong here does not
    /// produce a blank somebody might query - it produces the PREVIOUS product type, which
    /// reads as a perfectly ordinary answer on a fixed-position file (AD-039).
    /// </summary>
    public class ProductTypeExportTests
    {
        private static Entity Case(string chosen, string formatted)
        {
            var row = new Entity("al_outcomecase", Guid.NewGuid());
            if (chosen != null)
            {
                row[ListOptionRules.ProductTypeAttribute] =
                    new EntityReference(ListOptionRules.Entity, Guid.NewGuid()) { Name = chosen };
            }

            if (formatted != null)
            {
                row["al_productsolutiontype"] = new OptionSetValue(120910521);
                row.FormattedValues["al_productsolutiontype"] = formatted;
            }

            return row;
        }

        [Fact]
        public void The_chosen_option_wins_over_the_choice_column_the_case_was_imported_with()
        {
            // THE one that matters. A case moved to an option added since the migration still
            // carries the old integer, and reading that would export the product type the
            // case used to have.
            Assert.Equal("Drawdown", GenerateExportPlugin.ProductType(Case("Drawdown", "Accumulation Pension")));
        }

        [Fact]
        public void A_case_never_edited_since_the_migration_still_exports_its_choice_column()
        {
            // The fallback earns its place: blanking these would be a worse trade than
            // carrying the integer's label.
            Assert.Equal("Accumulation Pension", GenerateExportPlugin.ProductType(Case(null, "Accumulation Pension")));
        }

        [Fact]
        public void A_case_with_neither_exports_nothing_rather_than_something_invented()
        {
            Assert.Null(GenerateExportPlugin.ProductType(Case(null, null)));
        }

        [Fact]
        public void A_lookup_with_no_name_falls_back_rather_than_exporting_a_blank()
        {
            // A reference retrieved without its name is a read that did not ask for it, not a
            // case with no product type. Exporting an empty cell there would be a lie about
            // the case rather than about the read.
            var row = new Entity("al_outcomecase", Guid.NewGuid());
            row[ListOptionRules.ProductTypeAttribute] =
                new EntityReference(ListOptionRules.Entity, Guid.NewGuid());
            row["al_productsolutiontype"] = new OptionSetValue(120910521);
            row.FormattedValues["al_productsolutiontype"] = "Accumulation Pension";

            Assert.Equal("Accumulation Pension", GenerateExportPlugin.ProductType(row));
        }
    }
}
