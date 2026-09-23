using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Who may see a case, decided from what the case is (AD-218, AR-01 to AR-04). Pure, so
    /// every combination the portal permissions depend on is pinned here rather than found
    /// in DEV.
    /// </summary>
    public class CaseAccessTests
    {
        private static readonly Guid Ada = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
        private static readonly Guid Bo = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
        private static readonly EntityReference Queue = new EntityReference("account", Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001"));
        private static readonly EntityReference Adviser = new EntityReference("contact", Guid.Parse("cccccccc-0000-4000-8000-000000000001"));
        private static readonly EntityReference Supervisor = new EntityReference("contact", Guid.Parse("cccccccc-0000-4000-8000-000000000002"));
        private static readonly DateTime Now = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

        private static ReviewFact Tax(Guid? contact, bool submitted = false, int sequence = 1)
        {
            return new ReviewFact { ReviewType = ResponseRules.ReviewTypeTax, AssignedContactId = contact, Submitted = submitted, Sequence = sequence };
        }

        private static ReviewFact Aqs(Guid? contact, bool submitted = false, int sequence = 2)
        {
            return new ReviewFact { ReviewType = ResponseRules.ReviewTypeAqs, AssignedContactId = contact, Submitted = submitted, Sequence = sequence };
        }

        private static CaseAccessInput Case(int status, bool tax, bool aqs, params ReviewFact[] reviews)
        {
            return new CaseAccessInput
            {
                CaseStatus = status,
                RouteRequiresTax = tax,
                RouteRequiresAqs = aqs,
                Reviews = new List<ReviewFact>(reviews),
                AqsQueueAccount = Queue,
                ResolvedAdviser = Adviser,
                ResolvedSupervisor = Supervisor,
                Now = Now,
            };
        }

        [Fact]
        public void The_tax_checker_is_the_contact_on_the_tax_review()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Assigned, true, true, Tax(Ada)));
            Assert.Equal(Ada, result.TaxChecker.Id);
            Assert.Null(result.AqsChecker);
        }

        [Fact]
        public void Reallocation_replaces_the_checker()
        {
            // The review row carries one contact; reallocating changes it, and the case must
            // follow, so the previous checker's Contact-scoped read goes with it (AR-02).
            var result = CaseAccess.Decide(Case(CaseLifecycle.Assigned, true, false, Tax(Bo)));
            Assert.Equal(Bo, result.TaxChecker.Id);
        }

        [Fact]
        public void An_inactive_review_grants_nothing()
        {
            var review = Tax(Ada);
            review.Active = false;
            var result = CaseAccess.Decide(Case(CaseLifecycle.Assigned, true, false, review));
            Assert.Null(result.TaxChecker);
        }

        [Fact]
        public void An_aqs_only_case_waiting_to_be_taken_is_on_the_queue()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, false, true));
            Assert.Equal(Queue.Id, result.QueueAccount.Id);
            Assert.Equal(Now, result.QueuedOn);
        }

        [Fact]
        public void A_tax_then_aqs_case_is_not_on_the_queue_before_its_tax_check()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, true, true));
            Assert.Null(result.QueueAccount);
            Assert.Null(result.QueuedOn);
        }

        [Fact]
        public void A_tax_then_aqs_case_joins_the_queue_once_its_tax_check_is_submitted()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, true, true, Tax(Ada, submitted: true)));
            Assert.NotNull(result.QueueAccount);
        }

        [Fact]
        public void The_queue_keeps_the_time_the_case_joined_it()
        {
            var input = Case(CaseLifecycle.Queued, false, true);
            input.CurrentQueuedOn = Now.AddDays(-3);
            Assert.Equal(Now.AddDays(-3), CaseAccess.Decide(input).QueuedOn);
        }

        [Fact]
        public void A_case_leaves_the_queue_when_its_aqs_review_is_taken()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, false, true, Aqs(Ada)));
            Assert.Null(result.QueueAccount);
            Assert.Null(result.QueuedOn);
            Assert.Equal(Ada, result.AqsChecker.Id);
        }

        [Fact]
        public void A_case_not_at_queued_is_not_on_the_queue()
        {
            Assert.Null(CaseAccess.Decide(Case(CaseLifecycle.Assigned, false, true)).QueueAccount);
        }

        [Fact]
        public void A_pass_case_is_never_released()
        {
            // Closed is in the release range; a Pass case reaches it with no remediation.
            var input = Case(CaseLifecycle.Closed, false, true, Aqs(Ada, submitted: true));
            input.HasRemediation = false;
            var result = CaseAccess.Decide(input);
            Assert.Null(result.Adviser);
            Assert.Null(result.Supervisor);
        }

        [Fact]
        public void A_remediation_case_is_released_to_its_adviser_and_supervisor()
        {
            var input = Case(CaseLifecycle.AwaitingRemediation, false, true, Aqs(Ada, submitted: true));
            input.HasRemediation = true;
            var result = CaseAccess.Decide(input);
            Assert.Equal(Adviser.Id, result.Adviser.Id);
            Assert.Equal(Supervisor.Id, result.Supervisor.Id);
        }

        [Fact]
        public void The_adviser_keeps_the_case_after_it_closes()
        {
            var input = Case(CaseLifecycle.Closed, false, true, Aqs(Ada, submitted: true));
            input.HasRemediation = true;
            Assert.NotNull(CaseAccess.Decide(input).Adviser);
        }

        [Fact]
        public void An_open_review_holds_release_back()
        {
            // A Tax fail on a Tax-then-AQS case can carry actions while AQS is still owed.
            // AR-04: an adviser never sees a case whose review is still in progress.
            var input = Case(CaseLifecycle.AwaitingRemediation, true, true, Tax(Ada, submitted: true), Aqs(Bo));
            input.HasRemediation = true;
            Assert.Null(CaseAccess.Decide(input).Adviser);
        }

        [Fact]
        public void An_unmatched_adviser_releases_to_nobody()
        {
            var input = Case(CaseLifecycle.AwaitingRemediation, false, true, Aqs(Ada, submitted: true));
            input.HasRemediation = true;
            input.ResolvedAdviser = null;
            Assert.Null(CaseAccess.Decide(input).Adviser);
        }

        [Fact]
        public void The_teams_follow_the_route()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, true, true));
            Assert.True(result.ShareWithTaxTeam);
            Assert.True(result.ShareWithAqsTeam);

            var aqsOnly = CaseAccess.Decide(Case(CaseLifecycle.Queued, false, true));
            Assert.False(aqsOnly.ShareWithTaxTeam);
            Assert.True(aqsOnly.ShareWithAqsTeam);
        }

        [Fact]
        public void A_tax_review_keeps_the_tax_team_after_a_route_change()
        {
            // Both managers see a Tax-then-AQS case for its whole life (answer 4c); a route
            // later edited to AQS only still carries a Tax review the Tax team did.
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, false, true, Tax(Ada, submitted: true)));
            Assert.True(result.ShareWithTaxTeam);
        }

        [Fact]
        public void A_case_with_no_route_shares_with_no_team_and_joins_no_queue()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, false, false));
            Assert.False(result.ShareWithTaxTeam);
            Assert.False(result.ShareWithAqsTeam);
            Assert.Null(result.QueueAccount);
        }
    }
}
