using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The case's check date: stamped by the submit, and editable by nobody (project owner,
    /// 2026-09-21: "the check date has to be uneditable as it is automatically updated on
    /// submit").
    ///
    /// This SUPERSEDES the 2026-09-19 rule these tests used to assert, which stamped the
    /// date on the first answer saved and left it alone once set so a checker could correct
    /// it. Both halves are gone: the column is derived from the submit, the latest submit
    /// wins, and neither front end offers it.
    /// </summary>
    public class CheckDateStampTests
    {
        private static readonly Guid CaseId = Guid.Parse("11111111-1111-4111-8111-111111111111");

        private static FakeOrganizationService WithCase(DateTime? checkDate)
        {
            var service = new FakeOrganizationService();
            if (checkDate.HasValue)
            {
                service.Seed("al_outcomecase", CaseId, "al_checkdate", checkDate.Value);
            }
            else
            {
                service.Seed("al_outcomecase", CaseId);
            }

            return service;
        }

        [Fact]
        public void Stamps_today_when_the_case_has_no_check_date()
        {
            var service = WithCase(null);

            SubmitReviewPlugin.StampCheckDate(service, CaseId);

            var update = Assert.Single(service.Updates);
            Assert.Equal("al_outcomecase", update.LogicalName);
            Assert.Equal(CaseId, update.Id);
            Assert.Equal(DateTime.UtcNow.Date, update.GetAttributeValue<DateTime>("al_checkdate"));
        }

        [Fact]
        public void Writes_the_date_without_a_time()
        {
            // al_checkdate is a date-only column (behavior 2). A time component would be
            // dropped by the platform, but the value written should say what is meant.
            var service = WithCase(null);

            SubmitReviewPlugin.StampCheckDate(service, CaseId);

            var written = service.Updates[0].GetAttributeValue<DateTime>("al_checkdate");
            Assert.Equal(TimeSpan.Zero, written.TimeOfDay);
        }

        [Fact]
        public void Overwrites_a_check_date_the_import_carried()
        {
            // The column is no longer a default the file supplies. It says when the check was
            // submitted, and on 2026-09-21 the project owner chose "latest submit wins" over
            // "first submit wins" with both put to them.
            var service = WithCase(new DateTime(2026, 9, 1));

            SubmitReviewPlugin.StampCheckDate(service, CaseId);

            var update = Assert.Single(service.Updates);
            Assert.Equal(DateTime.UtcNow.Date, update.GetAttributeValue<DateTime>("al_checkdate"));
        }

        [Fact]
        public void The_second_submit_on_a_case_redates_it()
        {
            // A Tax-then-AQS case is submitted twice. The date ends up as the day the
            // checking finished, not the day its first half did.
            var service = WithCase(new DateTime(2026, 9, 18));

            SubmitReviewPlugin.StampCheckDate(service, CaseId);
            SubmitReviewPlugin.StampCheckDate(service, CaseId);

            Assert.Equal(2, service.Updates.Count);
            Assert.Equal(
                DateTime.UtcNow.Date,
                service.Updates[1].GetAttributeValue<DateTime>("al_checkdate"));
        }

        [Fact]
        public void Does_nothing_when_it_is_handed_nothing()
        {
            var service = WithCase(null);

            SubmitReviewPlugin.StampCheckDate(service, Guid.Empty);
            SubmitReviewPlugin.StampCheckDate(null, CaseId);

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void No_command_offers_the_check_date_as_an_editable_field()
        {
            // The rule is "uneditable", so both allowlists have to refuse it - the Code App's
            // manager edit and the portal's header edit. Asserted through the two public
            // gates rather than by reading the maps, because the maps are private and it is
            // the refusal that matters.
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "al_checkdate", "2026-09-01" },
            };

            Assert.Throws<InvalidPluginExecutionException>(
                () => CaseHeaderRequestPlugin.EnsureCheckerEditable(fields));

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => UpdateCaseDetailsPlugin.ApplyFields(
                    new FakeOrganizationService(),
                    fields,
                    new Entity("al_outcomecase", CaseId),
                    new Entity("al_outcomecase", CaseId),
                    new List<string>(),
                    null));

            Assert.Contains("al_checkdate", error.Message);
        }
    }
}
