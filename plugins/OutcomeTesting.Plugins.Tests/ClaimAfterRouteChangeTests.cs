using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Which check a claim lands on once a case's route has changed since its review
    /// instances were created (BR-004, AD-076).
    ///
    /// The shape that produced this: IO-SEED-AQS-04 on DEV was seeded AQS-only and
    /// allocated, opening an AQS instance at sequence 2. It was then edited so a Tax check
    /// was required, and DeriveRoute re-derived the route to Tax-then-AQS. Reading "the
    /// earliest unsubmitted instance" as the check due picked that AQS instance, so the
    /// Tax check the route now required was never created — it appeared nowhere on the
    /// case — while the AQS submit offered in its place is refused by
    /// EnsureTaxPrecedesAqs until Tax is in. The case could not progress at all.
    /// </summary>
    public class ClaimAfterRouteChangeTests
    {
        private static readonly Guid CaseId = Guid.Parse("aaaa1111-2222-4333-8444-555555555555");
        private static readonly Guid RouteId = Guid.Parse("bbbb1111-2222-4333-8444-555555555555");
        private static readonly Guid VersionId = Guid.Parse("cccc1111-2222-4333-8444-555555555555");
        private static readonly Guid ContactId = Guid.Parse("dddd1111-2222-4333-8444-555555555555");
        private static readonly Guid UserId = Guid.Parse("eeee1111-2222-4333-8444-555555555555");
        private static readonly Guid OtherContactId = Guid.Parse("ffff1111-2222-4333-8444-555555555555");

        private static AssignCasePlugin.Assignee Assignee()
        {
            return new AssignCasePlugin.Assignee
            {
                ContactId = ContactId,
                UserId = UserId,
                UserName = "Seed Checker",
            };
        }

        /// <summary>
        /// A case on the Tax-then-AQS route with a checklist version in force, and the
        /// claimant's web roles queued for EnsureDisciplineRole to read.
        /// </summary>
        private static FakeOrganizationService TaxThenAqsCase(params string[] roles)
        {
            var svc = new FakeOrganizationService();

            var contactRows = new System.Collections.Generic.List<Entity>();
            foreach (var role in roles)
            {
                var row = new Entity("contact", ContactId);
                row["role.name"] = new AliasedValue("powerpagecomponent", "name", role);
                contactRows.Add(row);
            }

            svc.FetchResults.Enqueue(new EntityCollection(contactRows));

            svc.Seed(
                "al_reviewroute", RouteId,
                "al_requirestaxreview", true,
                "al_requiresaqsreview", true);

            svc.Seed(
                "al_checklistversion", VersionId,
                "al_effectivefrom", DateTime.UtcNow.Date.AddDays(-30),
                "statecode", new OptionSetValue(0));

            return svc;
        }

        private static Entity Case(FakeOrganizationService svc)
        {
            return svc.Seed(
                "al_outcomecase", CaseId,
                "al_casestatus", new OptionSetValue(CaseLifecycle.Queued),
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId));
        }

        private static Entity OpenAqsInstance(FakeOrganizationService svc, Guid? assignedContact)
        {
            var review = svc.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusAssigned),
                "al_sequence", 2,
                "statecode", new OptionSetValue(0));

            if (assignedContact.HasValue)
            {
                review["al_assignedcontactid"] = new EntityReference("contact", assignedContact.Value);
            }

            return review;
        }

        [Fact]
        public void A_premature_aqs_instance_does_not_become_the_check_a_claim_lands_on()
        {
            var svc = TaxThenAqsCase(WebRoleRegistry.TaxReviewerRole);
            var outcomeCase = Case(svc);
            var aqs = OpenAqsInstance(svc, assignedContact: null);

            var review = ClaimCasePlugin.ResolveOrCreateReview(svc, outcomeCase, Assignee());

            Assert.NotEqual(aqs.Id, review.Id);
            Assert.Equal(
                ResponseRules.ReviewTypeTax,
                review.GetAttributeValue<OptionSetValue>("al_reviewtype").Value);
            Assert.Equal(1, review.GetAttributeValue<int>("al_sequence"));
        }

        [Fact]
        public void An_aqs_instance_taken_by_another_checker_does_not_block_the_tax_check()
        {
            // The deadlock itself. The premature AQS instance carried an assigned contact,
            // so the claim was refused as "another checker has already started checks" —
            // and the Tax check the case owed could never be picked up by anybody.
            var svc = TaxThenAqsCase(WebRoleRegistry.TaxReviewerRole);
            var outcomeCase = Case(svc);
            OpenAqsInstance(svc, assignedContact: OtherContactId);

            var review = ClaimCasePlugin.ResolveOrCreateReview(svc, outcomeCase, Assignee());

            Assert.Equal(
                ResponseRules.ReviewTypeTax,
                review.GetAttributeValue<OptionSetValue>("al_reviewtype").Value);
        }

        [Fact]
        public void An_open_unclaimed_tax_instance_is_attached_to_rather_than_duplicated()
        {
            var svc = TaxThenAqsCase(WebRoleRegistry.TaxReviewerRole);
            var outcomeCase = Case(svc);
            var tax = svc.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusAssigned),
                "al_sequence", 1,
                "statecode", new OptionSetValue(0));

            var review = ClaimCasePlugin.ResolveOrCreateReview(svc, outcomeCase, Assignee());

            Assert.Equal(tax.Id, review.Id);
        }

        [Fact]
        public void Two_checkers_racing_the_same_tax_check_still_collide()
        {
            // The race protection the discipline filter must not lose: the loser is told,
            // rather than being made a silent co-assignee.
            var svc = TaxThenAqsCase(WebRoleRegistry.TaxReviewerRole);
            var outcomeCase = Case(svc);
            svc.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusAssigned),
                "al_sequence", 1,
                "al_assignedcontactid", new EntityReference("contact", OtherContactId),
                "statecode", new OptionSetValue(0));

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => ClaimCasePlugin.ResolveOrCreateReview(svc, outcomeCase, Assignee()));

            Assert.Contains("already started checks", ex.Message);
        }

        [Fact]
        public void The_discipline_role_checked_is_the_one_the_route_makes_due()
        {
            // An AQS reviewer cannot take the Tax check the route now puts first, even
            // though the only instance on the case is the AQS one they could have worked.
            var svc = TaxThenAqsCase(WebRoleRegistry.AqsReviewerRole);
            var outcomeCase = Case(svc);
            OpenAqsInstance(svc, assignedContact: null);

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => ClaimCasePlugin.ResolveOrCreateReview(svc, outcomeCase, Assignee()));

            Assert.Contains(WebRoleRegistry.TaxReviewerRole, ex.Message);
        }

        [Fact]
        public void A_case_with_no_route_still_attaches_to_its_open_instance()
        {
            // Pre-route cases keep the old behaviour: NextDiscipline refuses a routeless
            // case outright, so asking the route first would make them unclaimable.
            var svc = TaxThenAqsCase(WebRoleRegistry.AqsReviewerRole);
            var outcomeCase = svc.Seed(
                "al_outcomecase", CaseId,
                "al_casestatus", new OptionSetValue(CaseLifecycle.Queued));
            var aqs = OpenAqsInstance(svc, assignedContact: null);

            var review = ClaimCasePlugin.ResolveOrCreateReview(svc, outcomeCase, Assignee());

            Assert.Equal(aqs.Id, review.Id);
        }
    }
}
