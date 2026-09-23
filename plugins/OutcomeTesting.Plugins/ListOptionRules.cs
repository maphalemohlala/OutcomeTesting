using System;
using System.Collections.Generic;
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

        public const string SampleSourceAttribute = "al_samplesourceid";
        public const string SampleSourceLegacyAttribute = "al_samplesource";

        public const string CaseTypeAttribute = "al_casetypeid";
        public const string CaseTypeLegacyAttribute = "al_casetype";

        public const string PreOrPostCheckAttribute = "al_preorpostcheckid";
        public const string PreOrPostCheckLegacyAttribute = "al_preorpostcheck";

        /// <summary>
        /// The Fields key naming the products a case covers, as comma-separated option ids.
        ///
        /// Not a column. A case covers SEVERAL products - the text column it replaces is
        /// labelled "Product(s)" and holds "Pension; ISA" - so the options attach through a
        /// many-to-many and are applied by <see cref="ApplyProducts"/> rather than written
        /// onto the case row.
        /// </summary>
        public const string ProductsField = "al_productids";

        public const string ProductsRelationship = "al_listoption_al_outcomecase_products";

        /// <summary>
        /// Whether a Fields key is applied by ASSOCIATION rather than written to a column.
        ///
        /// The one place that answers it. <see cref="ApplyProducts"/> is reached through this
        /// test, and so is the ColumnSet a caller reads the case with - which is the half that
        /// was missing: <c>CaseHeaderRequestPlugin</c> put every key it was sent on its
        /// ColumnSet, and Dataverse faults on a ColumnSet naming something that is not a
        /// column at all, so every portal header edit touching Products failed on the READ,
        /// before a single product was looked at. Asking here means a second such field cannot
        /// be added with only one of the two places taught about it.
        /// </summary>
        public static bool AppliedByAssociation(string field)
        {
            return string.Equals(field, ProductsField, StringComparison.OrdinalIgnoreCase);
        }


        /// <summary>The free-text column the products list replaces, kept for old cases.</summary>
        public const string ProductsLegacyAttribute = "al_products";

        // al_listoption.al_list.
        public const int ProductSolutionType = 120910840;
        public const int SampleSource = 120910841;
        public const int CaseType = 120910842;
        public const int PreOrPostCheck = 120910843;
        public const int Products = 120910844;

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

        /// <summary>
        /// The option ids a case currently covers, through the products relationship.
        /// </summary>
        public static List<Guid> CurrentProducts(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression(Entity)
            {
                ColumnSet = new ColumnSet(NameAttribute),
                LinkEntities =
                {
                    new LinkEntity(
                        Entity, ProductsRelationship, "al_listoptionid", "al_listoptionid", JoinOperator.Inner)
                    {
                        LinkCriteria = new FilterExpression
                        {
                            Conditions =
                            {
                                new ConditionExpression("al_outcomecaseid", ConditionOperator.Equal, caseId),
                            },
                        },
                    },
                },
            };

            var found = new List<Guid>();
            foreach (var row in service.RetrieveMultiple(query).Entities)
            {
                found.Add(row.Id);
            }

            return found;
        }

        /// <summary>
        /// Points a case at exactly the products named, and describes the move for the audit.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The payload is the WHOLE set, not a change to it. A page that sent only additions
        /// could never remove one, and a page that sent only removals could never add; sending
        /// the set the person is looking at means the saved answer is the one they can see.
        /// </para>
        /// <para>
        /// Every id is validated before anything is written - exists, belongs to the Products
        /// list, and is offered today - so a payload naming one bad id changes nothing rather
        /// than half-applying. <see cref="Resolve"/> is the same gate the single-choice lists
        /// use, for the same reason: neither the management page nor the header form is what
        /// decides this (NFR-SEC-01).
        /// </para>
        /// <para>
        /// An option ALREADY attached is left attached even if it has since been retired.
        /// Retiring stops an option being chosen from now on; it does not rewrite the cases
        /// that already carry it. Only ids being newly added are checked for being in force.
        /// </para>
        /// </remarks>
        public static void ApplyProducts(
            IOrganizationService service,
            Guid caseId,
            string idList,
            List<string> changes,
            DateTime asOf)
        {
            if (service == null) { throw new ArgumentNullException("service"); }

            var current = CurrentProducts(service, caseId);
            var wanted = new List<Guid>();
            var names = new Dictionary<Guid, string>();

            foreach (var raw in (idList ?? string.Empty).Split(','))
            {
                var text = raw.Trim();
                if (text.Length == 0) { continue; }

                Guid optionId;
                if (!Guid.TryParse(text, out optionId))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ValidationPrefix + "Products is not an option on that list.");
                }

                if (wanted.Contains(optionId)) { continue; }

                // Already on the case: accepted as it stands, retired or not.
                if (current.Contains(optionId))
                {
                    wanted.Add(optionId);
                    continue;
                }

                var option = Resolve(service, optionId, Products, "Products", asOf);
                names[optionId] = option.GetAttributeValue<string>(NameAttribute);
                wanted.Add(optionId);
            }

            var added = new List<Guid>();
            foreach (var id in wanted)
            {
                if (!current.Contains(id)) { added.Add(id); }
            }

            var removed = new List<Guid>();
            foreach (var id in current)
            {
                if (!wanted.Contains(id)) { removed.Add(id); }
            }

            if (added.Count == 0 && removed.Count == 0) { return; }

            var relationship = new Relationship(ProductsRelationship);

            if (added.Count > 0)
            {
                service.Associate(
                    "al_outcomecase", caseId, relationship, References(added));
            }

            if (removed.Count > 0)
            {
                service.Disassociate(
                    "al_outcomecase", caseId, relationship, References(removed));
            }

            changes.Add("Products " + Describe(service, current, names)
                + " -> " + Describe(service, wanted, names));
        }

        private static EntityReferenceCollection References(List<Guid> ids)
        {
            var references = new EntityReferenceCollection();
            foreach (var id in ids)
            {
                references.Add(new EntityReference(Entity, id));
            }

            return references;
        }

        /// <summary>
        /// A set of options as a person reads it, names rather than ids: an audit line full of
        /// GUIDs says nothing about what was done.
        /// </summary>
        private static string Describe(
            IOrganizationService service, List<Guid> ids, Dictionary<Guid, string> known)
        {
            if (ids.Count == 0) { return "(none)"; }

            var parts = new List<string>();
            foreach (var id in ids)
            {
                string name;
                if (!known.TryGetValue(id, out name))
                {
                    try
                    {
                        name = service
                            .Retrieve(Entity, id, new ColumnSet(NameAttribute))
                            .GetAttributeValue<string>(NameAttribute);
                    }
                    catch (Exception)
                    {
                        name = null;
                    }
                }

                parts.Add(string.IsNullOrWhiteSpace(name) ? "(unknown)" : name);
            }

            parts.Sort(StringComparer.OrdinalIgnoreCase);
            return "'" + string.Join("; ", parts.ToArray()) + "'";
        }
    }
}
