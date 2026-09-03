using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The parts of ImportCases that decide what is written and what a replay answers with
    /// (BR-001, BR-002, NFR-REL-01).
    /// </summary>
    public class ImportCasesPluginTests
    {
        private static List<ImportRules.ImportRow> Rows(params string[] references)
        {
            return references
                .Select((reference, i) => new ImportRules.ImportRow
                {
                    RowNumber = i + 2,
                    Reference = reference,
                    Values = new Dictionary<string, object>(),
                    Raw = reference,
                })
                .ToList();
        }

        private static FakeOrganizationService WithCases(params string[] references)
        {
            var service = new FakeOrganizationService();
            foreach (var reference in references)
            {
                service.Seed("al_outcomecase", Guid.NewGuid(), "al_casereference", reference);
            }

            return service;
        }

        [Fact]
        public void Finds_a_reference_that_is_already_a_case()
        {
            // BR-001: a re-upload skips references Dataverse already holds, so running the
            // same extract twice does not create the case twice.
            var found = ImportCasesPlugin.FindExistingReferences(WithCases("IO-1"), Rows("IO-1", "IO-2"));

            Assert.Contains("IO-1", found);
            Assert.DoesNotContain("IO-2", found);
        }

        [Fact]
        public void Finds_nothing_when_no_reference_is_taken()
        {
            Assert.Empty(ImportCasesPlugin.FindExistingReferences(WithCases(), Rows("IO-1", "IO-2")));
        }

        [Fact]
        public void Queries_nothing_for_an_empty_file()
        {
            // An In condition with no values is a query Dataverse refuses; the loop must
            // not run at all rather than send one.
            var service = WithCases("IO-1");

            Assert.Empty(ImportCasesPlugin.FindExistingReferences(service, Rows()));
            Assert.Equal(0, service.RetrieveMultipleCount);
        }

        [Fact]
        public void Checks_every_reference_when_the_file_spans_more_than_one_chunk()
        {
            // The chunking loop is where a duplicate check silently stops short: a file
            // longer than one chunk whose last row is already a case would import it again.
            var references = Enumerable.Range(1, 450).Select(i => "IO-" + i).ToArray();
            var service = WithCases("IO-1", "IO-201", "IO-450");

            var found = ImportCasesPlugin.FindExistingReferences(service, Rows(references));

            Assert.Equal(3, found.Count);
            Assert.Contains("IO-450", found);
        }

        [Fact]
        public void Matches_an_existing_reference_regardless_of_case()
        {
            // Dataverse compares strings case-insensitively, so "io-1" and "IO-1" are the
            // same case. Skipping only the exact-case match would create the duplicate the
            // check exists to prevent.
            var found = ImportCasesPlugin.FindExistingReferences(WithCases("IO-1"), Rows("IO-1"));

            Assert.Contains("io-1", found);
        }

        [Fact]
        public void Records_the_counts_a_replay_has_to_answer_with()
        {
            Assert.Equal("BATCH-1|10|7|2|1", ImportCasesPlugin.BuildDetails("BATCH-1", 10, 7, 2, 1));
        }

        [Fact]
        public void Keeps_a_guard_refusal_as_the_reason_on_the_row()
        {
            // A guard plug-in refusing a row has already written a message meant for a
            // person. Replacing it with the generic text would throw away the only
            // explanation the user gets.
            var reason = ImportCasesPlugin.RowFailureReason(
                new InvalidPluginExecutionException("VALIDATION: Advice date cannot be in the future."));

            Assert.Equal("Advice date cannot be in the future.", reason);
        }

        [Fact]
        public void Falls_back_to_a_safe_reason_for_a_platform_failure()
        {
            // NFR-OBS-01: platform detail is traced, never shown. A SQL or privilege error
            // must not land in a column an import screen renders.
            var reason = ImportCasesPlugin.RowFailureReason(
                new InvalidOperationException("SqlException: violation of PRIMARY KEY constraint 'PK_al_outcomecase'"));

            Assert.DoesNotContain("Sql", reason);
            Assert.Contains("Dataverse rejected this case", reason);
        }

        [Fact]
        public void Keeps_an_unprefixed_refusal_intact()
        {
            var reason = ImportCasesPlugin.RowFailureReason(new InvalidPluginExecutionException("No checklist version is in force."));

            Assert.Equal("No checklist version is in force.", reason);
        }

        [Fact]
        public void Writes_a_report_row_as_parseable_json()
        {
            var json = ImportCasesPlugin.ReportRow(4, "IO-1", "Invalid", "Bad \"date\".", "IO-1,\nx");

            Assert.Equal(
                "{\"rowNumber\":4,\"caseReference\":\"IO-1\",\"status\":\"Invalid\","
                + "\"reason\":\"Bad \\\"date\\\".\",\"raw\":\"IO-1,\\nx\"}",
                json);
        }

        [Fact]
        public void Writes_a_null_reference_as_json_null_not_the_word()
        {
            // A row rejected for having no reference is the common case, and "null" as a
            // string would show up in the report as a case called null.
            var json = ImportCasesPlugin.ReportRow(4, null, "Invalid", "Missing IO reference (BR-001).", "");

            Assert.Contains("\"caseReference\":null", json);
        }
    }
}
