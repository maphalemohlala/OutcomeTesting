using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Both teams edit the case header, before the Tax handover and after it (project owner,
    /// 2026-09-23: "I need to reverse the change we made, for tax -> AQS checks. both teams
    /// need to be able to edit the headers").
    ///
    /// <para>
    /// This supersedes HeaderFreezesAfterTaxTests, which pinned the opposite for one day. On
    /// 2026-09-22 the ordinary header - client name, adviser, products, dates - was frozen at
    /// the moment the Tax check was submitted, so an AQS checker picking the case up could
    /// read the Tax checker's header but not correct it. That is reversed.
    /// </para>
    /// <para>
    /// <b>What did NOT reverse.</b> The Tax team's own two fields, TaxCheckRequired and
    /// TaxTeamDisposition, keep the rule they had before the batch: refused once the Tax
    /// check is submitted, and refused to anyone but the checker it is assigned to. Those two
    /// re-derive the review route (BR-004, AD-036), so they are not header fields in the
    /// ordinary sense - moving one after the submit would reopen a decision
    /// <c>SubmitReviewPlugin</c> has already acted on, against a review BR-012 has made
    /// immutable. The reversal was asked for the headers, and that is what it covers.
    /// </para>
    /// <para>
    /// <b>Where the ordinary path is pinned.</b> Its guards are
    /// <see cref="CaseHeaderRequestPlugin.EnsureCheckerEditable"/>, which says WHICH fields a
    /// checker may touch, and <see cref="CaseHeaderRequestPlugin.EnsureAssignedToCase"/>,
    /// which says WHO may touch them - both public and both tested here and in
    /// CaseHeaderRequestPluginTests. What the reversal removed was a third call between them,
    /// so the assertion that it is gone is that an assigned AQS checker passes every guard
    /// the ordinary path still has, on a case whose Tax check is submitted.
    /// </para>
    /// </summary>
    public class HeaderStaysEditableAfterTaxTests
    {
        private static readonly Guid CaseId = Guid.Parse("11111111-2222-4222-8222-222222222222");
        private static readonly Guid AqsChecker = Guid.Parse("aaaaaaaa-2222-4222-8222-222222222222");
        private static readonly Guid TaxChecker = Guid.Parse("bbbbbbbb-2222-4222-8222-222222222222");

        /// <summary>A review on the case, of a discipline, assigned, at a submission state.</summary>
        private static Entity Review(
            FakeOrganizationService service,
            Guid caseId,
            int reviewType,
            Guid assignedTo,
            bool submitted)
        {
            var review = service.Seed(
                "al_reviewinstance",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "al_reviewtype", new OptionSetValue(reviewType),
                "al_assignedcontactid", new EntityReference("contact", assignedTo),
                "statecode", new OptionSetValue(0));

            if (submitted)
            {
                review["al_reviewstatus"] = new OptionSetValue(120910212);
                review["al_submittedon"] = new DateTime(2026, 9, 20);
            }

            return review;
        }

        /// <summary>The handover: the Tax check submitted, the AQS check open and assigned.</summary>
        private static FakeOrganizationService AfterTheHandover()
        {
            var service = new FakeOrganizationService();
            Review(service, CaseId, ResponseRules.ReviewTypeTax, TaxChecker, submitted: true);
            Review(service, CaseId, ResponseRules.ReviewTypeAqs, AqsChecker, submitted: false);
            return service;
        }

        // ------------------------------------------------------------------ the reversal

        [Fact]
        public void The_aqs_checker_may_edit_the_header_after_the_tax_check_is_submitted()
        {
            // THE reversal. Between 2026-09-22 and 2026-09-23 this threw, because the
            // ordinary header path called EnsureTaxCheckNotSubmitted on every payload
            // carrying a field.
            CaseHeaderRequestPlugin.EnsureAssignedToCase(AfterTheHandover(), AqsChecker, CaseId);
        }

        [Theory]
        [InlineData("al_clientname")]
        [InlineData("al_advisername")]
        [InlineData("al_products")]
        [InlineData("al_advicedate")]
        public void The_ordinary_header_fields_stay_on_the_checker_allowlist(string field)
        {
            // The other guard on that path, unchanged by the reversal and pinned alongside it
            // so that "both teams may edit the header" is not quietly narrowed from the other
            // end - by a field leaving the allowlist rather than by a rule being added back.
            var fields = new System.Collections.Generic.Dictionary<string, string>
            {
                { field, "x" },
            };

            CaseHeaderRequestPlugin.EnsureCheckerEditable(fields);
        }

        [Fact]
        public void An_unassigned_checker_is_still_refused_the_header()
        {
            // The reversal widened WHAT may be edited and WHEN, never WHO. A contact with no
            // open review on the case is refused exactly as before.
            var stranger = Guid.Parse("cccccccc-2222-4222-8222-222222222222");

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseHeaderRequestPlugin.EnsureAssignedToCase(
                    AfterTheHandover(), stranger, CaseId));

            Assert.Contains("only edit the header of a case you are checking", error.Message);
        }

        [Fact]
        public void The_tax_checker_is_refused_once_their_own_check_is_submitted()
        {
            // Not the reversal undoing itself: this is EnsureAssignedToCase, which has always
            // required an OPEN review of the caller's. The Tax checker's work on this case is
            // finished, so the header is no longer theirs to edit - and the AQS checker above,
            // whose review IS open, may edit it.
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseHeaderRequestPlugin.EnsureAssignedToCase(
                    AfterTheHandover(), TaxChecker, CaseId));

            Assert.Contains("already been submitted", error.Message);
        }

        // ------------------------------------------------------- what did not reverse

        [Fact]
        public void The_tax_teams_own_two_fields_are_still_refused_after_the_submit()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseHeaderRequestPlugin.EnsureTaxReviewOpen(
                    AfterTheHandover(), TaxChecker, CaseId));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, error.Message);
            Assert.Contains("Tax check", error.Message);
        }

        [Fact]
        public void The_refusal_says_the_rest_of_the_header_is_still_editable()
        {
            // The words matter here: the portal shows this message as it stands. Before the
            // reversal it read "its header can no longer be changed", which is now false and
            // would send an AQS checker away from an edit they are allowed to make.
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseHeaderRequestPlugin.EnsureTaxCheckNotSubmitted(
                    AfterTheHandover(), CaseId));

            Assert.Contains("Tax fields can no longer be changed", error.Message);
            Assert.Contains("rest of the header is still editable", error.Message);
        }

        [Fact]
        public void The_tax_fields_are_open_while_the_tax_check_is_still_open()
        {
            var service = new FakeOrganizationService();
            Review(service, CaseId, ResponseRules.ReviewTypeTax, TaxChecker, submitted: false);

            CaseHeaderRequestPlugin.EnsureTaxReviewOpen(service, TaxChecker, CaseId);
        }

        [Fact]
        public void A_submitted_aqs_check_says_nothing_about_the_tax_fields()
        {
            // Only the TAX leg closes them. An AQS review that has been submitted says
            // nothing about whether a Tax checker is still working.
            var service = new FakeOrganizationService();
            Review(service, CaseId, ResponseRules.ReviewTypeAqs, AqsChecker, submitted: true);
            Review(service, CaseId, ResponseRules.ReviewTypeTax, TaxChecker, submitted: false);

            CaseHeaderRequestPlugin.EnsureTaxCheckNotSubmitted(service, CaseId);
        }

        [Fact]
        public void A_submitted_tax_check_on_another_case_closes_nothing_here()
        {
            var service = new FakeOrganizationService();
            var otherCase = Guid.Parse("33333333-2222-4222-8222-222222222222");
            Review(service, otherCase, ResponseRules.ReviewTypeTax, TaxChecker, submitted: true);

            CaseHeaderRequestPlugin.EnsureTaxCheckNotSubmitted(service, CaseId);
        }
    }
}
