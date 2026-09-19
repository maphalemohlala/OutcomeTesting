using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The case's check date, stamped when checks actually start on it (project owner,
    /// 2026-09-19).
    ///
    /// ResponseProgressPlugin already owns that moment: it runs post-operation on
    /// al_response and moves the review from Assigned to In progress on the first saved
    /// answer. Stamping here rather than on assignment matters because a case can sit
    /// assigned for days before anyone opens it, and the business reads this column as the
    /// day the work was done.
    /// </summary>
    public class CheckDateStampTests
    {
        private static readonly Guid CaseId = Guid.Parse("11111111-1111-4111-8111-111111111111");

        private static Entity Review(Guid? caseId)
        {
            var review = new Entity("al_reviewinstance", Guid.NewGuid());
            if (caseId.HasValue)
            {
                review["al_outcomecaseid"] = new EntityReference("al_outcomecase", caseId.Value);
            }

            return review;
        }

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

            ResponseProgressPlugin.StampCheckDate(service, Review(CaseId));

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

            ResponseProgressPlugin.StampCheckDate(service, Review(CaseId));

            var written = service.Updates[0].GetAttributeValue<DateTime>("al_checkdate");
            Assert.Equal(TimeSpan.Zero, written.TimeOfDay);
        }

        [Fact]
        public void Leaves_a_check_date_the_import_already_carried()
        {
            // It is a DEFAULT, not a derived value. Overwriting would let the second review
            // on a case silently redate work the first one did.
            var service = WithCase(new DateTime(2026, 9, 1));

            ResponseProgressPlugin.StampCheckDate(service, Review(CaseId));

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void Leaves_a_check_date_a_checker_corrected()
        {
            // Same rule from the other direction: a checker may edit this field, and the
            // next review starting must not undo that.
            var service = WithCase(new DateTime(2026, 9, 17));

            ResponseProgressPlugin.StampCheckDate(service, Review(CaseId));

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void Does_nothing_for_a_review_with_no_case()
        {
            var service = WithCase(null);

            ResponseProgressPlugin.StampCheckDate(service, Review(null));

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void Does_nothing_when_it_is_handed_nothing()
        {
            var service = WithCase(null);

            ResponseProgressPlugin.StampCheckDate(service, null);
            ResponseProgressPlugin.StampCheckDate(null, Review(CaseId));

            Assert.Empty(service.Updates);
        }
    }
}
