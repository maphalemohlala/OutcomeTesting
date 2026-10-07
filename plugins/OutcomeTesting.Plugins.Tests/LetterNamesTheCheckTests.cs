using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The allocation and review-submitted letters say which check they are about (reported
    /// 2026-10-06 as duplicate emails).
    ///
    /// <para>
    /// A case routed Tax then AQS has two checks, so it is allocated twice and submitted
    /// twice, and each is a letter of its own. They were word for word the same - "Case X has
    /// been allocated to you", "Review submitted on case X" - so whoever received both read
    /// the second as a duplicate. Thirteen such pairs went out on TEST in a month.
    /// </para>
    /// </summary>
    public class LetterNamesTheCheckTests
    {
        private static readonly Guid CaseId = Guid.Parse("c1c1c1c1-0001-4001-8001-c1c1c1c1c1c1");
        // Differing in the leading bytes, as two reviews created together in Dataverse do;
        // the assignment code carries only the first twelve hex digits.
        private static readonly Guid TaxReviewId = Guid.Parse("c75d875b-fac0-4111-8002-000000000001");
        private static readonly Guid AqsReviewId = Guid.Parse("c85d875b-fac0-4111-8002-000000000001");
        private static readonly Guid CheckerUserId = Guid.Parse("c3c3c3c3-0003-4003-8003-c3c3c3c3c3c3");
        private static readonly Guid CheckerContactId = Guid.Parse("c4c4c4c4-0004-4004-8004-c4c4c4c4c4c4");
        private static readonly Guid Correlation = Guid.NewGuid();

        private static FakeOrganizationService Case()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "OT-1",
                "statecode", 0);
            svc.Seed("contact", CheckerContactId,
                "fullname", "A Checker",
                "emailaddress1", "checker@example.com",
                "statecode", new OptionSetValue(0));
            svc.Seed("al_reviewinstance", TaxReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax));
            svc.Seed("al_reviewinstance", AqsReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs));
            return svc;
        }

        /// <summary>An allocation of one check, carrying the code AssignCase writes.</summary>
        private static Guid Allocate(FakeOrganizationService svc, Guid reviewId)
        {
            var id = Guid.NewGuid();
            svc.Seed("al_caseassignment", id,
                "al_caseassignmentcode", AssignCasePlugin.BuildAssignmentCode(CaseId, reviewId, CheckerUserId),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_assignedcontactid", new EntityReference("contact", CheckerContactId),
                "al_isactive", true,
                "al_assignedon", new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));

            NotificationEmitterPlugin.QueueAllocation(svc, Correlation, id);
            return id;
        }

        private static Entity LetterFor(FakeOrganizationService svc, Guid targetId)
        {
            return svc.Creates.Single(c =>
                c.LogicalName == "al_notification"
                && c.GetAttributeValue<string>("al_targetid") == targetId.ToString("D"));
        }

        [Fact]
        public void The_allocation_letter_names_the_tax_check()
        {
            var svc = Case();

            var letter = LetterFor(svc, Allocate(svc, TaxReviewId));

            Assert.Equal("Case OT-1: Tax check allocated to you", letter.GetAttributeValue<string>("al_subject"));
            Assert.StartsWith("The Tax check on case OT-1 is now assigned to you.", letter.GetAttributeValue<string>("al_body"));
        }

        [Fact]
        public void The_two_allocations_of_one_case_no_longer_read_the_same()
        {
            var svc = Case();

            var tax = LetterFor(svc, Allocate(svc, TaxReviewId)).GetAttributeValue<string>("al_subject");
            var aqs = LetterFor(svc, Allocate(svc, AqsReviewId)).GetAttributeValue<string>("al_subject");

            Assert.Equal("Case OT-1: AQS check allocated to you", aqs);
            Assert.NotEqual(tax, aqs);
        }

        [Fact]
        public void An_allocation_whose_check_cannot_be_found_still_reads_as_a_sentence()
        {
            // An assignment written with neither the review nor the code, or one whose review
            // has gone. "Review" is true of every check, so it reads as a sentence without
            // naming the wrong one - the audit found "(check)" read like a broken placeholder.
            var svc = Case();
            var id = Guid.NewGuid();
            svc.Seed("al_caseassignment", id,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_assignedcontactid", new EntityReference("contact", CheckerContactId),
                "al_isactive", true);

            NotificationEmitterPlugin.QueueAllocation(svc, Correlation, id);

            var letter = LetterFor(svc, id);
            Assert.Equal("Case OT-1: review allocated to you", letter.GetAttributeValue<string>("al_subject"));
            Assert.StartsWith("The review on case OT-1 is now assigned to you.", letter.GetAttributeValue<string>("al_body"));
        }

        [Fact]
        public void Two_reviews_the_code_cannot_tell_apart_name_neither()
        {
            // Naming the wrong check would be worse than the duplicate this fixes.
            var svc = Case();
            var twin = Guid.Parse("c75d875b-fac0-4111-8002-000000000099");
            svc.Seed("al_reviewinstance", twin,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs));

            var letter = LetterFor(svc, Allocate(svc, TaxReviewId));

            Assert.Equal("Case OT-1: review allocated to you", letter.GetAttributeValue<string>("al_subject"));
        }

        [Fact]
        public void The_allocation_reads_its_check_from_the_review_the_assignment_names()
        {
            // The audit: matching the alternate key's format was a workaround. Both writers
            // now set the review on the row, and the letter reads it from there first.
            var svc = Case();
            var id = Guid.NewGuid();
            svc.Seed("al_caseassignment", id,
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", TaxReviewId),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_assignedcontactid", new EntityReference("contact", CheckerContactId),
                "al_isactive", true);

            NotificationEmitterPlugin.QueueAllocation(svc, Correlation, id);

            Assert.Equal("Case OT-1: Tax check allocated to you", LetterFor(svc, id).GetAttributeValue<string>("al_subject"));
        }

        [Fact]
        public void A_manager_allocation_records_the_review_on_the_assignment()
        {
            var svc = Case();
            svc.Seed("systemuser", CheckerUserId, "internalemailaddress", "checker@example.com", "fullname", "A Checker");

            var id = AssignCasePlugin.AllocateAssignment(
                svc, CaseId, AqsReviewId,
                new AssignCasePlugin.Assignee { UserId = CheckerUserId, ContactId = CheckerContactId, UserName = "A Checker" },
                "OT-1", null, null);

            Assert.Equal(AqsReviewId,
                svc.Row("al_caseassignment", id).GetAttributeValue<EntityReference>("al_reviewinstanceid").Id);
        }

        [Fact]
        public void Every_writer_of_an_assignment_code_also_records_the_review()
        {
            // A self-claim is a portal create that ClaimCasePlugin completes in pre-operation,
            // which no test drives end to end. Pinned at the source so neither writer can set
            // the code without the review the letter now reads.
            var directory = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null
                && !System.IO.Directory.Exists(System.IO.Path.Combine(directory.FullName, "OutcomeTesting.Plugins")))
            {
                directory = directory.Parent;
            }

            var plugins = System.IO.Path.Combine(directory.FullName, "OutcomeTesting.Plugins");

            foreach (var file in new[] { "AssignCasePlugin.cs", "ClaimCasePlugin.cs" })
            {
                var source = System.IO.File.ReadAllText(System.IO.Path.Combine(plugins, file));
                Assert.Contains("[ReviewLookupAttr] = new EntityReference(", source);
            }
        }

        [Fact]
        public void The_review_submitted_letter_names_the_check()
        {
            var svc = Case();
            var review = new Entity("al_reviewinstance", AqsReviewId)
            {
                ["al_outcomecaseid"] = new EntityReference("al_outcomecase", CaseId),
                ["al_reviewtype"] = new OptionSetValue(ResponseRules.ReviewTypeAqs),
            };

            SubmitReviewPlugin.QueueSubmittedNotification(svc, Correlation, review, AqsReviewId);

            var letter = LetterFor(svc, AqsReviewId);
            Assert.Equal("Case OT-1: AQS check submitted", letter.GetAttributeValue<string>("al_subject"));
            Assert.Equal(
                "The AQS check on case OT-1 has been submitted and is locked to further edits.",
                letter.GetAttributeValue<string>("al_body"));
        }
    }
}
