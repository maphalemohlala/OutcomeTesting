using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// UpdateCaseDetailsPlugin.DeriveRoute (BR-004). Nothing else in the system sets
    /// al_reviewrouteid, and SubmitReviewPlugin reads al_requirestaxreview off that route
    /// to decide whether a Tax check must precede AQS — so a wrong or missing derivation
    /// silently lets an AQS-only review stand in for a case that owed a Tax check.
    /// </summary>
    public class DeriveRouteTests
    {
        private static readonly Guid TaxThenAqs = Guid.Parse("11111111-1111-4111-8111-111111111111");
        private static readonly Guid AqsOnly = Guid.Parse("22222222-2222-4222-8222-222222222222");
        private static readonly Guid TaxOnly = Guid.Parse("33333333-3333-4333-8333-333333333333");

        private const int Yes = 120910560;
        private const int No = 120910561;

        private static FakeOrganizationService Routes()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewroute", TaxThenAqs, "al_routecode", "ROUTE-TAX-AQS");
            svc.Seed("al_reviewroute", AqsOnly, "al_routecode", "ROUTE-AQS");
            svc.Seed("al_reviewroute", TaxOnly, "al_routecode", "ROUTE-TAX");
            return svc;
        }

        private static Entity Case(int? taxRequired, Guid? route)
        {
            var e = new Entity("al_outcomecase", Guid.NewGuid());
            if (taxRequired.HasValue) e["al_taxcheckrequired"] = new OptionSetValue(taxRequired.Value);
            if (route.HasValue) e["al_reviewrouteid"] = new EntityReference("al_reviewroute", route.Value);
            return e;
        }

        private static Guid? RouteOn(Entity update)
        {
            var reference = update.GetAttributeValue<EntityReference>("al_reviewrouteid");
            return reference == null ? (Guid?)null : reference.Id;
        }

        [Fact]
        public void Tax_check_required_routes_the_case_tax_then_aqs()
        {
            var update = new Entity("al_outcomecase") { ["al_taxcheckrequired"] = new OptionSetValue(Yes) };
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.DeriveRoute(Routes(), Case(null, null), update, changes);

            Assert.Equal(TaxThenAqs, RouteOn(update));
            Assert.Single(changes);
        }

        [Fact]
        public void No_tax_check_routes_the_case_aqs_only()
        {
            var update = new Entity("al_outcomecase") { ["al_taxcheckrequired"] = new OptionSetValue(No) };
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.DeriveRoute(Routes(), Case(null, null), update, changes);

            Assert.Equal(AqsOnly, RouteOn(update));
        }

        [Fact]
        public void An_unrouted_case_is_backfilled_from_its_existing_tax_answer()
        {
            // The tax answer is not being edited, but the case has no route: this is the
            // path that repairs cases imported before the derivation existed.
            var update = new Entity("al_outcomecase") { ["al_clientname"] = "Changed" };
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.DeriveRoute(Routes(), Case(Yes, null), update, changes);

            Assert.Equal(TaxThenAqs, RouteOn(update));
        }

        [Fact]
        public void An_unrelated_edit_does_not_revert_a_deliberate_reassignment()
        {
            // Tax only is only reachable through the AD-036 wrong-route reassignment.
            // Re-deriving here would undo it and send the case back through AQS.
            var update = new Entity("al_outcomecase") { ["al_clientname"] = "Changed" };
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.DeriveRoute(Routes(), Case(Yes, TaxOnly), update, changes);

            Assert.Null(RouteOn(update));
            Assert.Empty(changes);
        }

        [Fact]
        public void Changing_the_tax_answer_reroutes_a_routed_case()
        {
            var update = new Entity("al_outcomecase") { ["al_taxcheckrequired"] = new OptionSetValue(No) };
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.DeriveRoute(Routes(), Case(Yes, TaxThenAqs), update, changes);

            Assert.Equal(AqsOnly, RouteOn(update));
        }

        [Fact]
        public void A_case_already_on_the_derived_route_is_left_alone()
        {
            var update = new Entity("al_outcomecase") { ["al_clientname"] = "Changed" };
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.DeriveRoute(Routes(), Case(Yes, TaxThenAqs), update, changes);

            Assert.Null(RouteOn(update));
            Assert.Empty(changes);
        }

        [Fact]
        public void An_unanswered_tax_question_leaves_the_route_unset()
        {
            var update = new Entity("al_outcomecase") { ["al_clientname"] = "Changed" };
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.DeriveRoute(Routes(), Case(null, null), update, changes);

            Assert.Null(RouteOn(update));
            Assert.Empty(changes);
        }

        [Fact]
        public void Clearing_the_tax_answer_leaves_the_route_alone()
        {
            var update = new Entity("al_outcomecase") { ["al_taxcheckrequired"] = null };
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.DeriveRoute(Routes(), Case(Yes, TaxThenAqs), update, changes);

            Assert.Null(RouteOn(update));
            Assert.Empty(changes);
        }

        [Fact]
        public void An_unseeded_route_fails_loudly_rather_than_leaving_the_case_unrouted()
        {
            var update = new Entity("al_outcomecase") { ["al_taxcheckrequired"] = new OptionSetValue(Yes) };
            var changes = new List<string>();

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => UpdateCaseDetailsPlugin.DeriveRoute(new FakeOrganizationService(), Case(null, null), update, changes));

            Assert.Contains("ROUTE-TAX-AQS", error.Message);
        }
    }
}
