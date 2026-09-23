using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A checker editing Product(s) from the portal header (reported 2026-09-22: "values like
    /// product(s) are not saving", and Adam's "Your header changes could not be saved, so the
    /// review was not submitted" on TEST).
    ///
    /// <para>
    /// The portal sends the set under the key <see cref="ListOptionRules.ProductsField"/> -
    /// <c>al_productids</c> - which is NOT a column: a case covers several products and they
    /// attach through a many-to-many, applied by <see cref="ListOptionRules.ApplyProducts"/>.
    /// <see cref="CaseHeaderRequestPlugin"/> nonetheless put every field key on the ColumnSet
    /// it read the case with, so Dataverse faulted on the read before anything was applied:
    /// </para>
    /// <code>
    /// 'al_OutcomeCase' entity doesn't contain attribute with Name = 'al_productids'
    /// </code>
    /// <para>
    /// The portal surfaced that as a bare platform error, so the header did not save and the
    /// submit that flushes it first was refused on top. The Code App never hit it: its own
    /// ColumnSet is built from the editable-column map, which has never carried the key.
    /// </para>
    /// <para>
    /// Driven through <see cref="CaseHeaderRequestPlugin.Execute"/> rather than against the
    /// rules it calls, because the defect was in the wiring between them and every unit test
    /// over the parts stayed green while the portal failed on every such edit.
    /// </para>
    /// </summary>
    public class CaseHeaderProductsTests
    {
        private static readonly Guid CaseId = Guid.Parse("eeeeeeee-1111-4111-8111-111111111111");
        private static readonly Guid Checker = Guid.Parse("ffffffff-1111-4111-8111-111111111111");

        /// <summary>
        /// A case a checker holds an open review on, with the platform's own refusal of the
        /// key the portal sends armed. Without that last part the fake projects an absent
        /// attribute away and the read the portal cannot make looks like one that works.
        /// </summary>
        private static FakeOrganizationService Ready()
        {
            var service = new FakeOrganizationService();
            service.NotAColumn("al_outcomecase", ListOptionRules.ProductsField);

            service.Seed("contact", Checker, "fullname", "A Checker");
            service.Seed("al_outcomecase", CaseId, "al_casereference", "300000002");
            service.Seed(
                "al_reviewinstance",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_assignedcontactid", new EntityReference("contact", Checker),
                "statecode", new OptionSetValue(0));

            return service;
        }

        private static Guid Product(FakeOrganizationService service, string name)
        {
            var id = Guid.NewGuid();
            service.Seed(
                ListOptionRules.Entity,
                id,
                ListOptionRules.NameAttribute, name,
                ListOptionRules.ListAttribute, new OptionSetValue(ListOptionRules.Products),
                "statecode", 0);
            return id;
        }

        /// <summary>Runs the plug-in over the request the portal writes onto the contact.</summary>
        private static void Save(FakeOrganizationService service, string fieldsJson)
        {
            var provider = new FakeServiceProvider(service);
            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityName = "contact";
            provider.Context.PrimaryEntityId = Checker;
            provider.Context.InputParameters["Target"] = new Entity("contact", Checker)
            {
                [CaseHeaderRequestPlugin.RequestAttr] =
                    "{\"caseId\":\"" + CaseId.ToString("D") + "\",\"fields\":"
                    + Newtonsoft(fieldsJson) + "}",
            };

            new CaseHeaderRequestPlugin(null, null).Execute(provider);
        }

        /// <summary>
        /// The Fields payload as the page sends it: a JSON STRING holding an object, not a
        /// nested object. Quoted here rather than hand-escaped at every call site.
        /// </summary>
        private static string Newtonsoft(string json)
        {
            return "\"" + json.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        [Fact]
        public void A_product_ticked_on_the_portal_header_is_saved()
        {
            var service = Ready();
            var pension = Product(service, "Pension");

            Save(service, "{\"" + ListOptionRules.ProductsField + "\":\"" + pension.ToString("D") + "\"}");

            Assert.Equal(new[] { pension }, ListOptionRules.CurrentProducts(service, CaseId).ToArray());
        }

        [Fact]
        public void Several_products_save_together()
        {
            // The header sends the WHOLE set, so this is the ordinary edit and not a corner.
            var service = Ready();
            var pension = Product(service, "Pension");
            var isa = Product(service, "ISA");

            Save(
                service,
                "{\"" + ListOptionRules.ProductsField + "\":\""
                    + pension.ToString("D") + "," + isa.ToString("D") + "\"}");

            var held = ListOptionRules.CurrentProducts(service, CaseId);
            Assert.Equal(2, held.Count);
            Assert.Contains(pension, held);
            Assert.Contains(isa, held);
        }

        [Fact]
        public void Products_save_alongside_an_ordinary_header_column()
        {
            // The mixed payload the page actually sends when someone corrects the client name
            // and the products in one visit. The column still has to be read - the audit line
            // names what moved - so the fix cannot be "stop reading the case".
            var service = Ready();
            var isa = Product(service, "ISA");

            Save(
                service,
                "{\"al_clientname\":\"J Smith\",\"" + ListOptionRules.ProductsField + "\":\""
                    + isa.ToString("D") + "\"}");

            Assert.Contains(isa, ListOptionRules.CurrentProducts(service, CaseId));

            var saved = service.Updates.LastOrDefault(u =>
                string.Equals(u.LogicalName, "al_outcomecase", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(saved);
            Assert.Equal("J Smith", saved.GetAttributeValue<string>("al_clientname"));
        }

        [Fact]
        public void The_case_is_never_read_by_a_name_that_is_not_a_column()
        {
            // The rule under the fix, stated where a reader will find it: a Fields key that is
            // applied by association names no column, so it must not reach a ColumnSet. Pinned
            // separately from the behaviour above so that widening the payload with another
            // such key fails here, naming the reason, rather than only on the environment.
            Assert.True(ListOptionRules.AppliedByAssociation(ListOptionRules.ProductsField));
            Assert.False(ListOptionRules.AppliedByAssociation("al_clientname"));
            Assert.False(ListOptionRules.AppliedByAssociation(ListOptionRules.ProductsLegacyAttribute));
        }
    }
}
