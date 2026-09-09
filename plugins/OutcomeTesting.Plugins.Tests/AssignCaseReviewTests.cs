using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Which review instance a manager allocation lands on (BR-003, BR-004, AD-072).
    ///
    /// The case that matters most is the one that used to be refused: a case with no open
    /// check at all. Only the portal self-claim opened review instances, so a queued case
    /// nobody had claimed could not be allocated by a manager, and neither could the AQS leg
    /// of a Tax-then-AQS case after its Tax check returned it to the queue.
    /// </summary>
    public class AssignCaseReviewTests
    {
        private static readonly Guid CaseId = Guid.Parse("cccccccc-aaaa-4aaa-8aaa-cccccccccccc");
        private static readonly Guid RouteId = Guid.Parse("dddddddd-bbbb-4bbb-8bbb-dddddddddddd");
        private static readonly Guid UserId = Guid.Parse("eeeeeeee-cccc-4ccc-8ccc-eeeeeeeeeeee");
        private static readonly Guid ContactId = Guid.Parse("ffffffff-dddd-4ddd-8ddd-ffffffffffff");

        private static readonly AssignCasePlugin.Assignee Ada = new AssignCasePlugin.Assignee
        {
            UserId = UserId,
            ContactId = ContactId,
            UserName = "Ada Checker",
        };

        private static FakeOrganizationService Environment(bool tax, bool aqs)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", tax, "al_requiresaqsreview", aqs);
            svc.Seed(
                "al_checklistversion",
                Guid.NewGuid(),
                "statecode", new OptionSetValue(0),
                "al_effectivefrom", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            return svc;
        }

        private static Entity Case()
        {
            return new Entity("al_outcomecase", CaseId)
            {
                ["al_casereference"] = "IO-TEST-001",
                ["al_reviewrouteid"] = new EntityReference("al_reviewroute", RouteId),
            };
        }

        private static Entity SeedReview(FakeOrganizationService svc, int reviewType, int sequence, DateTime? submittedOn)
        {
            var review = svc.Seed(
                "al_reviewinstance",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(reviewType),
                "al_sequence", sequence,
                "statecode", new OptionSetValue(0));
            if (submittedOn.HasValue)
            {
                review["al_submittedon"] = submittedOn.Value;
            }

            return review;
        }

        [Fact]
        public void Opens_the_first_check_when_the_case_has_none_yet()
        {
            var svc = Environment(tax: true, aqs: true);

            var review = AssignCasePlugin.ResolveReviewInstance(svc, svc, Case(), null, Ada);

            var created = svc.Creates.Single(c => c.LogicalName == "al_reviewinstance");
            Assert.Equal(created.Id, review.Id);
            Assert.Equal(ResponseRules.ReviewTypeTax, created.GetAttributeValue<OptionSetValue>("al_reviewtype").Value);
            Assert.Equal(ContactId, created.GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
            Assert.Equal(UserId, created.GetAttributeValue<EntityReference>("ownerid").Id);
            Assert.NotNull(created.GetAttributeValue<EntityReference>("al_checklistversionid"));
        }

        [Fact]
        public void Opens_the_aqs_leg_once_the_tax_check_is_submitted()
        {
            // The BR-004 handoff from the manager's side: the Tax review is in, the case is
            // back in the queue, and the only check left to allocate is AQS.
            var svc = Environment(tax: true, aqs: true);
            SeedReview(svc, ResponseRules.ReviewTypeTax, 1, new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc));

            var review = AssignCasePlugin.ResolveReviewInstance(svc, svc, Case(), null, Ada);

            Assert.Equal(ResponseRules.ReviewTypeAqs, review.GetAttributeValue<OptionSetValue>("al_reviewtype").Value);
            Assert.Equal(2, review.GetAttributeValue<int>("al_sequence"));
        }

        [Fact]
        public void Allocates_the_open_check_rather_than_opening_a_second()
        {
            var svc = Environment(tax: true, aqs: true);
            var open = SeedReview(svc, ResponseRules.ReviewTypeTax, 1, null);

            var review = AssignCasePlugin.ResolveReviewInstance(svc, svc, Case(), null, Ada);

            Assert.Equal(open.Id, review.Id);
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void Takes_the_earliest_open_check_by_sequence()
        {
            var svc = Environment(tax: true, aqs: true);
            SeedReview(svc, ResponseRules.ReviewTypeAqs, 2, null);
            var tax = SeedReview(svc, ResponseRules.ReviewTypeTax, 1, null);

            var review = AssignCasePlugin.ResolveReviewInstance(svc, svc, Case(), null, Ada);

            Assert.Equal(tax.Id, review.Id);
        }

        [Fact]
        public void An_explicit_check_wins()
        {
            var svc = Environment(tax: true, aqs: true);
            SeedReview(svc, ResponseRules.ReviewTypeTax, 1, null);
            var aqs = SeedReview(svc, ResponseRules.ReviewTypeAqs, 2, null);

            var review = AssignCasePlugin.ResolveReviewInstance(svc, svc, Case(), aqs.Id, Ada);

            Assert.Equal(aqs.Id, review.Id);
        }

        [Fact]
        public void Refuses_an_explicit_check_that_is_already_submitted()
        {
            var svc = Environment(tax: true, aqs: true);
            var submitted = SeedReview(svc, ResponseRules.ReviewTypeTax, 1, new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc));

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => AssignCasePlugin.ResolveReviewInstance(svc, svc, Case(), submitted.Id, Ada));

            Assert.Contains("already submitted", ex.Message);
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void Refuses_a_case_whose_route_owes_nothing_more()
        {
            var svc = Environment(tax: true, aqs: false);
            SeedReview(svc, ResponseRules.ReviewTypeTax, 1, new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc));

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => AssignCasePlugin.ResolveReviewInstance(svc, svc, Case(), null, Ada));

            Assert.Contains("already been submitted", ex.Message);
        }
    }
}
