using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The Tax fields on a case header may be changed only by the checker whose Tax check it
    /// is (audit finding 2, 2026-09-20).
    ///
    /// <para>
    /// These tests exist because a check that ran for MOST payloads read as a check that ran.
    /// <c>EnsureAssignedToCase</c> was called only when the payload carried general fields, so
    /// a payload naming nothing but TaxCheckRequired or TaxTeamDisposition skipped it - and
    /// those two re-derive the review route (BR-004, AD-036). Any contact holding the Tax
    /// Reviewer role could reroute any case in the system whose Tax review was unsubmitted.
    /// </para>
    /// <para>
    /// Reading every case is deliberate (project owner, 2026-09-20: advisers, T&amp;C managers
    /// and both reviewer roles view all cases). Reading is all it may buy: "tax reviewers can
    /// only work on cases assigned to them". So the case id needed for the attack is not a
    /// secret, and the guard is the only thing standing between knowing an id and rerouting
    /// the case behind it.
    /// </para>
    /// <para>
    /// Every test here asserts a REFUSAL except the two that prove the legitimate path still
    /// works. A security test that only shows the allowed case passing proves nothing about
    /// what is forbidden.
    /// </para>
    /// </summary>
    public class CaseHeaderTaxFieldsTests
    {
        private static readonly Guid CaseId = Guid.Parse("aaaaaaaa-2222-4222-8222-222222222222");
        private static readonly Guid Mine = Guid.Parse("cccccccc-2222-4222-8222-222222222222");
        private static readonly Guid Theirs = Guid.Parse("dddddddd-2222-4222-8222-222222222222");

        /// <summary>
        /// A case carrying one review of the given discipline, assigned to whoever is named.
        /// </summary>
        private static FakeOrganizationService WithReview(
            int reviewType, Guid? assignedTo, bool submitted = false)
        {
            var service = new FakeOrganizationService();
            var review = service.Seed(
                "al_reviewinstance",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(reviewType),
                "statecode", new OptionSetValue(0));

            if (assignedTo.HasValue)
            {
                review["al_assignedcontactid"] = new EntityReference("contact", assignedTo.Value);
            }

            if (submitted)
            {
                review["al_reviewstatus"] = new OptionSetValue(120910212);
            }

            return service;
        }

        private static string Refusal(FakeOrganizationService service, Guid caller)
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseHeaderRequestPlugin.EnsureTaxReviewOpen(service, caller, CaseId));
            return error.Message;
        }

        // ------------------------------------------------------------------ the hole itself

        [Fact]
        public void Refuses_a_tax_reviewer_who_holds_no_review_on_this_case()
        {
            // THE defect. Before 2026-09-20 this passed: the guard asked only whether the
            // case's Tax check was unsubmitted, never whose it was.
            var service = WithReview(ResponseRules.ReviewTypeTax, Theirs);

            Assert.Contains("assigned to you", Refusal(service, Mine));
        }

        [Fact]
        public void Refuses_a_tax_reviewer_when_the_case_is_claimed_by_nobody()
        {
            // A queued case with an open, unassigned Tax review. Unowned is not "mine".
            var service = WithReview(ResponseRules.ReviewTypeTax, null);

            Assert.Contains("assigned to you", Refusal(service, Mine));
        }

        [Fact]
        public void Refuses_when_the_case_has_no_tax_review_at_all()
        {
            // An AQS-only case. There is no Tax check to be assigned to, so the Tax fields
            // are nobody's to change from the portal.
            var service = WithReview(ResponseRules.ReviewTypeAqs, Mine);

            Assert.Contains("assigned to you", Refusal(service, Mine));
        }

        [Fact]
        public void Refuses_the_aqs_checker_of_the_same_case()
        {
            // The reason this check is not left to EnsureAssignedToCase, which accepts an
            // open review of EITHER discipline. Editing the Tax fields is the Tax team's
            // work, and this person is assigned to the case - just not to that half of it.
            var service = WithReview(ResponseRules.ReviewTypeAqs, Mine);
            service.Seed(
                "al_reviewinstance",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_assignedcontactid", new EntityReference("contact", Theirs),
                "statecode", new OptionSetValue(0));

            Assert.Contains("assigned to you", Refusal(service, Mine));
        }

        [Fact]
        public void Refuses_a_deactivated_review_even_when_it_is_mine()
        {
            var service = new FakeOrganizationService();
            service.Seed(
                "al_reviewinstance",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_assignedcontactid", new EntityReference("contact", Mine),
                "statecode", new OptionSetValue(1));

            Assert.Contains("assigned to you", Refusal(service, Mine));
        }

        // ------------------------------------------------------------------ the older guard

        [Fact]
        public void Still_refuses_a_submitted_tax_check_before_asking_whose_it_is()
        {
            // The pre-existing rule, and it reports first: "submitted" is a truer account of
            // why the edit is refused than "not yours" when the work is finished. Assigned to
            // the caller, so only the submission can be what refuses it.
            var service = WithReview(ResponseRules.ReviewTypeTax, Mine, submitted: true);

            Assert.Contains("has been submitted", Refusal(service, Mine));
        }

        // ------------------------------------------------------------------ the allowed path

        [Fact]
        public void Allows_the_checker_whose_tax_check_it_is()
        {
            var service = WithReview(ResponseRules.ReviewTypeTax, Mine);

            CaseHeaderRequestPlugin.EnsureTaxReviewOpen(service, Mine, CaseId);
        }

        [Fact]
        public void Allows_the_tax_checker_of_a_case_that_also_carries_an_aqs_review()
        {
            // A Tax-then-AQS case. The AQS leg belonging to somebody else changes nothing.
            var service = WithReview(ResponseRules.ReviewTypeTax, Mine);
            service.Seed(
                "al_reviewinstance",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_assignedcontactid", new EntityReference("contact", Theirs),
                "statecode", new OptionSetValue(0));

            CaseHeaderRequestPlugin.EnsureTaxReviewOpen(service, Mine, CaseId);
        }
    }
}
