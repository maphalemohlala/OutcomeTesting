using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The server's rules for the case-header lists whose options are rows rather than choice
    /// metadata (project owner, 2026-09-21: "How easy would it be to create a management page
    /// where users can add, edit, and remove options?").
    ///
    /// <para>
    /// This is the authority, and the page is not. A caller who sends an option id straight to
    /// al_UpdateCaseDetails - or to the portal's header edit - never goes near the management
    /// screen, so every rule that matters is checked here: the row must exist, it must belong
    /// to the list the field holds, and it must be offered on the day it is chosen. Rendering
    /// the same rules in listOptions.ts is a convenience for the person, not the gate
    /// (NFR-SEC-01).
    /// </para>
    /// <para>
    /// Kept in step with app/src/features/admin/listOptions.ts, which names the same four lists
    /// against the same values.
    /// </para>
    /// </summary>
    public static class ListOptionRules
    {
        public const string Entity = "al_listoption";

        public const string NameAttribute = "al_name";
        public const string ListAttribute = "al_list";
        public const string SortOrderAttribute = "al_sortorder";
        public const string LegacyValueAttribute = "al_legacyvalue";
        public const string EffectiveFromAttribute = "al_effectivefrom";
        public const string EffectiveToAttribute = "al_effectiveto";

        /// <summary>al_outcomecase's lookup to the chosen product / solution type.</summary>
        public const string ProductTypeAttribute = "al_producttypeid";

        /// <summary>The choice column al_producttypeid supersedes, kept for un-backfilled cases.</summary>
        public const string ProductTypeLegacyAttribute = "al_productsolutiontype";

        // al_listoption.al_list.
        public const int ProductSolutionType = 120910840;
        public const int SampleSource = 120910841;
        public const int CaseType = 120910842;
        public const int PreOrPostCheck = 120910843;

        /// <summary>
        /// Whether an option is offered on a given day: <c>from &lt;= day &lt; to</c>.
        ///
        /// The same window the checklist reads (BR-013 / AD-015), deliberately, so "in force"
        /// means one thing across the solution rather than two that nearly agree.
        /// </summary>
        public static bool InForce(DateTime? effectiveFrom, DateTime? effectiveTo, DateTime day)
        {
            var on = day.Date;
            if (effectiveFrom.HasValue && effectiveFrom.Value.Date > on) { return false; }
            if (effectiveTo.HasValue && effectiveTo.Value.Date <= on) { return false; }
            return true;
        }

        /// <summary>
        /// The option a case may be pointed at, or a refusal naming why not.
        /// </summary>
        /// <remarks>
        /// A RETIRED option is refused rather than accepted quietly. Retiring exists to stop an
        /// option being chosen from now on, and a command that still accepted it would make the
        /// management page's central promise untrue for anybody not using the page. Where a case
        /// genuinely needs a retired option - correcting an old record - the option is reinstated
        /// first, which leaves a trail of that decision instead of hiding it.
        ///
        /// A case that ALREADY holds a retired option is untouched by this: nothing is validated
        /// unless the field is being written.
        /// </remarks>
        public static Entity Resolve(
            IOrganizationService service,
            Guid optionId,
            int listValue,
            string fieldLabel,
            DateTime asOf)
        {
            if (service == null) { throw new ArgumentNullException("service"); }

            Entity option;
            try
            {
                option = service.Retrieve(
                    Entity,
                    optionId,
                    new ColumnSet(NameAttribute, ListAttribute, EffectiveFromAttribute, EffectiveToAttribute));
            }
            catch (Exception)
            {
                // Deleted, or never existed. Either way the caller named something that is not
                // an option, and the id is not echoed back - it says nothing to a person.
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + fieldLabel + " is not an option on that list.");
            }

            var list = option.GetAttributeValue<OptionSetValue>(ListAttribute);
            if (list == null || list.Value != listValue)
            {
                // Not pedantry: every list's options live in one table, so without this a
                // sample source could be written into the product type field and would read
                // back as a perfectly ordinary value.
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + fieldLabel + " is not an option on that list.");
            }

            if (!InForce(
                option.GetAttributeValue<DateTime?>(EffectiveFromAttribute),
                option.GetAttributeValue<DateTime?>(EffectiveToAttribute),
                asOf))
            {
                var name = option.GetAttributeValue<string>(NameAttribute);
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + fieldLabel + " '"
                    + (string.IsNullOrWhiteSpace(name) ? "that option" : name)
                    + "' has been retired and can no longer be chosen.");
            }

            return option;
        }
    }
}
