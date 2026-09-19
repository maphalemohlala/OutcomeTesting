using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A checker may edit the header of a case they are checking, and no other (item 5,
    /// 2026-09-19).
    ///
    /// This is a server-side guard and not a hidden button. The portal writes a header edit
    /// through a contact permission shared with the sign-off and the claim - one allowlist
    /// per table - so the request can be made by hand for any case id at all. Without this
    /// a signed-in checker could edit the header of a case they had never been given.
    ///
    /// "Assigned to them" is an OPEN review on the case carrying their contact. Deliberately
    /// not al_checkername: that column arrives in the upload file and names whoever the
    /// paraplanner expected, so it never proves the case was allocated to anyone.
    /// </summary>
    public class CaseHeaderAssignmentTests
    {
        private static readonly Guid CaseId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
        private static readonly Guid OtherCaseId = Guid.Parse("bbbbbbbb-1111-4111-8111-111111111111");
        private static readonly Guid Checker = Guid.Parse("cccccccc-1111-4111-8111-111111111111");
        private static readonly Guid OtherChecker = Guid.Parse("dddddddd-1111-4111-8111-111111111111");

        private static FakeOrganizationService WithReview(
            Guid caseId, Guid? assignedTo, DateTime? submittedOn = null, int stateCode = 0)
        {
            var service = new FakeOrganizationService();
            var review = service.Seed(
                "al_reviewinstance",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "statecode", new OptionSetValue(stateCode));

            if (assignedTo.HasValue)
            {
                review["al_assignedcontactid"] = new EntityReference("contact", assignedTo.Value);
            }

            if (submittedOn.HasValue)
            {
                review["al_submittedon"] = submittedOn.Value;
            }

            return service;
        }

        private static string Refusal(FakeOrganizationService service)
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseHeaderRequestPlugin.EnsureAssignedToCase(service, Checker, CaseId));
            return error.Message;
        }

        [Fact]
        public void Allows_the_checker_holding_an_open_review_on_the_case()
        {
            var service = WithReview(CaseId, Checker);

            CaseHeaderRequestPlugin.EnsureAssignedToCase(service, Checker, CaseId);
        }

        [Fact]
        public void Refuses_a_case_assigned_to_someone_else()
        {
            Assert.Contains("not assigned to you", Refusal(WithReview(CaseId, OtherChecker)));
        }

        [Fact]
        public void Refuses_a_case_assigned_to_nobody()
        {
            // Queued, not yet claimed. Nobody is checking it, so nobody may edit its header
            // from the portal.
            Assert.Contains("only edit the header of a case you are checking", Refusal(WithReview(CaseId, null)));
        }

        [Fact]
        public void Refuses_once_the_checkers_own_review_is_submitted()
        {
            // The same line BR-012 draws for the answers: the work is done, and what was
            // recorded is no longer theirs to move.
            var service = WithReview(CaseId, Checker, submittedOn: new DateTime(2026, 9, 18));

            Assert.Contains("already been submitted", Refusal(service));
        }

        [Fact]
        public void Refuses_when_the_checkers_review_is_on_a_different_case()
        {
            // The guard has to compare the case in the REQUEST with the case on the review.
            // A checker holding work of their own would otherwise pass a check that only
            // asked whether they were checking anything at all.
            Assert.Contains("not assigned to you", Refusal(WithReview(OtherCaseId, Checker)));
        }

        [Fact]
        public void Refuses_when_the_review_has_been_deactivated()
        {
            // statecode 1. A deactivated review is not open work.
            Assert.Contains("only edit the header", Refusal(WithReview(CaseId, Checker, stateCode: 1)));
        }

        [Fact]
        public void Refuses_when_the_case_has_no_review_at_all()
        {
            Assert.Contains("only edit the header", Refusal(new FakeOrganizationService()));
        }
    }
}
