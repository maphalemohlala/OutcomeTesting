using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>Who may allocate which check (AR-01, AR-02, answer 4a/4b; closes OD-029(c)).</summary>
    public class AllocationScopeTests
    {
        private static readonly Guid ContactId = Guid.Parse("bbbbbbbb-2222-4222-8222-222222222299");

        [Theory]
        [InlineData(AllocationScope.TaxTeamManagerRole, ResponseRules.ReviewTypeTax, true)]
        [InlineData(AllocationScope.TaxTeamManagerRole, ResponseRules.ReviewTypeAqs, false)]
        [InlineData(AllocationScope.AqsTeamManagerRole, ResponseRules.ReviewTypeAqs, true)]
        [InlineData(AllocationScope.AqsTeamManagerRole, ResponseRules.ReviewTypeTax, false)]
        [InlineData("AL Portal - Outcome Testing Manager", ResponseRules.ReviewTypeTax, true)]
        [InlineData("AL Portal - Outcome Testing Manager", ResponseRules.ReviewTypeAqs, true)]
        [InlineData("Administrators", ResponseRules.ReviewTypeAqs, true)]
        [InlineData("AL Portal - T&C Supervisor", ResponseRules.ReviewTypeTax, false)]
        [InlineData("AL Portal - Tax Reviewer", ResponseRules.ReviewTypeTax, false)]
        public void Each_role_allocates_only_its_own_discipline(string role, int reviewType, bool allowed)
        {
            Assert.Equal(allowed, AllocationScope.MayAllocate(new[] { role }, reviewType));
        }

        [Fact]
        public void Role_names_match_whatever_their_case_and_spacing()
        {
            Assert.True(AllocationScope.MayAllocate(new[] { "  al portal - tax team manager " }, ResponseRules.ReviewTypeTax));
        }

        [Fact]
        public void An_unrecognised_discipline_is_allocated_by_nobody_below_the_managers()
        {
            Assert.False(AllocationScope.MayAllocate(new[] { AllocationScope.TaxTeamManagerRole }, 0));
        }

        [Fact]
        public void An_assignee_without_the_reviewer_role_is_refused_by_name()
        {
            var svc = new FakeOrganizationService();
            svc.FetchResults.Enqueue(new EntityCollection());
            var assignee = new AssignCasePlugin.Assignee { ContactId = ContactId, ContactName = "Ada Checker" };

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => AllocationScope.EnsureAssigneeHoldsDiscipline(svc, assignee, ResponseRules.ReviewTypeTax));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal.Message);
            Assert.Contains("Ada Checker", refusal.Message);
            Assert.Contains(WebRoleRegistry.TaxReviewerRole, refusal.Message);
        }

        [Fact]
        public void An_assignee_holding_the_reviewer_role_passes()
        {
            var svc = new FakeOrganizationService();
            var row = new Entity("contact", ContactId);
            row["role.name"] = new AliasedValue("powerpagecomponent", "name", WebRoleRegistry.AqsReviewerRole);
            svc.FetchResults.Enqueue(new EntityCollection(new[] { row }));
            var assignee = new AssignCasePlugin.Assignee { ContactId = ContactId, ContactName = "Ada Checker" };

            AllocationScope.EnsureAssigneeHoldsDiscipline(svc, assignee, ResponseRules.ReviewTypeAqs);
        }
    }
}
