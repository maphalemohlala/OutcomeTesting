using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The daily file's rows are still in remediation on their check day (owner, 2026-11-17),
    /// so a batch also snapshots graded cases that are not yet closed, marked so the
    /// quarterly file can still take only the closed ones.
    /// </summary>
    public class GenerateExportDailyFeedFixTests
    {
        private const int Draft = 120910770;
        private static readonly Guid AqsRoute = Guid.Parse("e1000000-0000-4000-8000-000000000001");

        private static FakeOrganizationService World(out Guid batchId)
        {
            var service = new FakeOrganizationService();
            batchId = Guid.NewGuid();
            service.Seed("al_exportbatch", batchId, "al_exportbatchcode", "EXB-D", "al_batchstatus", new OptionSetValue(Draft));
            service.Seed("al_reviewroute", AqsRoute, "al_requiresaqsreview", true);
            return service;
        }

        private static Guid GradedCase(FakeOrganizationService service, string reference, int status, DateTime? checkDate,
            bool clean = false)
        {
            var caseId = Guid.NewGuid();
            var row = service.Seed("al_outcomecase", caseId,
                "al_casereference", reference,
                "al_casestatus", new OptionSetValue(status),
                "statecode", new OptionSetValue(0),
                "al_reviewrouteid", new EntityReference("al_reviewroute", AqsRoute));
            if (checkDate.HasValue)
            {
                row["al_checkdate"] = checkDate.Value;
            }

            var outcome = service.Seed("al_outcome", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "al_initialoutcome", new OptionSetValue(clean ? OutcomeRules.OutcomePass : OutcomeRules.OutcomeInsufficient),
                "createdon", DateTime.UtcNow);
            outcome.FormattedValues["al_initialoutcome"] = clean ? "Pass" : "Insufficient evidence";

            var reviewId = Guid.NewGuid();
            service.Seed("al_reviewinstance", reviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "statecode", new OptionSetValue(0));
            var questionId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            service.Seed("al_question", questionId, "al_questioncode", FileQuality.QuestionCode);
            service.Seed("al_questionversion", versionId, "al_questionid", new EntityReference("al_question", questionId));
            service.Seed("al_response", Guid.NewGuid(),
                "al_answerchoice", new OptionSetValue(clean ? ResponseRules.ChoicePass : ResponseRules.ChoiceFail),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", versionId),
                "modifiedon", DateTime.UtcNow).FormattedValues["al_answerchoice"] = clean ? "Pass" : "Fail";
            return caseId;
        }

        private static FakeServiceProvider Generate(
            FakeOrganizationService service, Guid batchId, string file = "Daily", string from = null, string to = null)
        {
            var provider = new FakeServiceProvider(service);
            provider.Context.MessageName = "al_GenerateExport";
            provider.Context.InputParameters["BatchId"] = batchId.ToString("D");
            provider.Context.InputParameters["IdempotencyKey"] = Guid.NewGuid().ToString("N");
            provider.Context.InputParameters["FileKind"] = file;
            if (from != null) provider.Context.InputParameters["CheckedFrom"] = from;
            if (to != null) provider.Context.InputParameters["CheckedTo"] = to;
            new GenerateExportPlugin(null, null).Execute(provider);
            return provider;
        }

        private static Entity RecordFor(FakeOrganizationService service, Guid caseId)
        {
            return service.All("al_exportrecord")
                .Single(r => r.GetAttributeValue<EntityReference>("al_outcomecaseid").Id == caseId);
        }

        [Fact]
        public void The_daily_file_holds_a_case_failed_today_and_still_in_remediation()
        {
            var service = World(out var batchId);
            var today = new DateTime(2026, 11, 17);
            var open = GradedCase(service, "OPEN-1", CaseLifecycle.AwaitingRemediation, today);
            GradedCase(service, "CLOSED-2", CaseLifecycle.Closed, new DateTime(2026, 10, 12));

            var provider = Generate(service, batchId);

            Assert.Equal("2", provider.Context.OutputParameters["RowCount"]);
            var openRecord = RecordFor(service, open);
            Assert.Equal(today, openRecord.GetAttributeValue<DateTime>("al_checkdate"));
            Assert.Equal("Insufficient evidence", openRecord.GetAttributeValue<string>("al_advicequalitygrade"));
            Assert.Equal("Fail", openRecord.GetAttributeValue<string>("al_filequalitygrade"));
        }

        [Fact]
        public void The_quarterly_file_holds_only_closed_cases()
        {
            var service = World(out var batchId);
            GradedCase(service, "OPEN-1", CaseLifecycle.AwaitingRemediation, new DateTime(2026, 11, 17));
            var closed = GradedCase(service, "CLOSED-2", CaseLifecycle.Closed, new DateTime(2026, 10, 12));

            Assert.Equal("1", Generate(service, batchId, "Quarterly").Context.OutputParameters["RowCount"]);
            Assert.NotNull(RecordFor(service, closed));
        }

        [Fact]
        public void The_daily_file_leaves_out_a_clean_pass()
        {
            var service = World(out var batchId);
            GradedCase(service, "PASS-1", CaseLifecycle.Closed, new DateTime(2026, 11, 17), clean: true);
            var failed = GradedCase(service, "FAIL-2", CaseLifecycle.Closed, new DateTime(2026, 11, 17));

            Assert.Equal("1", Generate(service, batchId).Context.OutputParameters["RowCount"]);
            Assert.NotNull(RecordFor(service, failed));
        }

        [Fact]
        public void Only_cases_checked_inside_the_range_are_held_both_ends_included()
        {
            var service = World(out var batchId);
            GradedCase(service, "BEFORE", CaseLifecycle.Closed, new DateTime(2026, 9, 30));
            GradedCase(service, "FIRST", CaseLifecycle.Closed, new DateTime(2026, 10, 1));
            GradedCase(service, "LAST", CaseLifecycle.Closed, new DateTime(2026, 12, 31));
            GradedCase(service, "AFTER", CaseLifecycle.Closed, new DateTime(2027, 1, 1));
            GradedCase(service, "UNDATED", CaseLifecycle.Closed, null);

            var provider = Generate(service, batchId, "Quarterly", "2026-10-01", "2026-12-31");

            Assert.Equal("2", provider.Context.OutputParameters["RowCount"]);
            Assert.Equal(new[] { "Export FIRST", "Export LAST" },
                service.All("al_exportrecord").Select(r => r.GetAttributeValue<string>("al_name")).OrderBy(n => n).ToArray());
        }

        [Fact]
        public void One_call_creates_the_batch_under_the_id_given_and_names_it_for_its_file()
        {
            var service = new FakeOrganizationService();
            service.Seed("al_reviewroute", AqsRoute, "al_requiresaqsreview", true);
            GradedCase(service, "FAIL-1", CaseLifecycle.Closed, new DateTime(2026, 11, 24));
            var batchId = Guid.NewGuid();

            var provider = Generate(service, batchId, "Daily", "2026-11-24", "2026-11-24");

            Assert.Equal("1", provider.Context.OutputParameters["RowCount"]);
            var batch = service.All("al_exportbatch").Single();
            Assert.Equal(batchId, batch.Id);
            Assert.Equal("Daily file, checked 24 Nov 2026", batch.GetAttributeValue<string>("al_name"));
            Assert.Equal(120910771, batch.GetAttributeValue<OptionSetValue>("al_batchstatus").Value);
            Assert.Equal(1, batch.GetAttributeValue<int>("al_rowcount"));
            Assert.Equal(batchId, service.All("al_exportrecord").Single().GetAttributeValue<EntityReference>("al_exportbatchid").Id);
        }

        [Theory]
        [InlineData("Weekly", null, null)]
        [InlineData("Daily", "24/11/2026", null)]
        [InlineData("Daily", "2026-11-25", "2026-11-24")]
        public void A_file_type_or_range_that_cannot_be_read_is_refused(string file, string from, string to)
        {
            var service = World(out var batchId);

            var refusal = Assert.Throws<InvalidPluginExecutionException>(() => Generate(service, batchId, file, from, to));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal.Message);
            Assert.Empty(service.All("al_exportrecord"));
        }

        [Theory]
        [InlineData(CaseLifecycle.RemediationInProgress)]
        [InlineData(CaseLifecycle.AwaitingSignoff)]
        [InlineData(CaseLifecycle.AwaitingRecheck)]
        public void Every_graded_but_open_status_is_snapshotted(int status)
        {
            var service = World(out var batchId);
            GradedCase(service, "OPEN-3", status, new DateTime(2026, 11, 17));

            Assert.Equal("1", Generate(service, batchId).Context.OutputParameters["RowCount"]);
        }

        [Fact]
        public void A_case_still_being_checked_is_not_snapshotted()
        {
            var service = World(out var batchId);
            // Tax submitted (so dated), AQS still to come: queued, not graded.
            GradedCase(service, "QUEUED-4", CaseLifecycle.Queued, new DateTime(2026, 11, 17));
            GradedCase(service, "INPROGRESS-5", CaseLifecycle.ReviewInProgress, null);
            // Open in remediation yet never dated: no day's file could hold it.
            GradedCase(service, "UNDATED-6", CaseLifecycle.AwaitingRemediation, null);

            var provider = Generate(service, batchId);

            Assert.Equal("0", provider.Context.OutputParameters["RowCount"]);
            Assert.Empty(service.All("al_exportrecord"));
        }

        [Fact]
        public void A_left_out_open_case_is_not_counted_as_closed()
        {
            var reason = GenerateExportPlugin.LeftOutReason(new[] { "Case A x" }, new[] { "Case B y", "Case C z" });

            Assert.Contains("1 closed case(s) left out of this export: Case A x", reason);
            Assert.Contains("2 case(s) not yet closed left out of this export: Case B y | Case C z", reason);
            Assert.Null(GenerateExportPlugin.LeftOutReason(new string[0], new string[0]));
            Assert.DoesNotContain("closed case(s)", GenerateExportPlugin.LeftOutReason(new string[0], new[] { "Case B y" }));
        }

        [Fact]
        public void The_missing_outcome_reason_does_not_call_an_open_case_closed()
        {
            Assert.DoesNotContain("closed", GenerateExportPlugin.DescribeIncompleteRow(null, "Pass", true));
        }
    }
}
