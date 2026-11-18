using System;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Audit fixes to the import path: a stray quote inside an unquoted field, a leading
    /// byte-order mark, closing an exception as Resolved, and the check date's day.
    /// </summary>
    public class ImportRulesFixTests
    {
        // The same input as app/src/features/imports/caseUpload.fix.test.ts.
        private const string StrayQuote = "TaskID,Client\n1,Acme 5\" Ltd\n2,Beta\n3,Gamma\n";

        [Fact]
        public void A_stray_quote_inside_a_field_does_not_swallow_the_rows_after_it()
        {
            var rows = ImportRules.Tokenise(StrayQuote);

            Assert.Equal(4, rows.Count);
            Assert.Equal(new[] { "1", "Acme 5\" Ltd" }, rows[1]);
            Assert.Equal(new[] { "3", "Gamma" }, rows[3]);
        }

        [Fact]
        public void A_quote_at_the_start_of_a_field_still_opens_a_quoted_field()
        {
            Assert.Equal(new[] { "a", "Client, A.", "c" }, ImportRules.Tokenise("a,\"Client, A.\",c\r\n")[0]);
            Assert.Equal("A \"nickname\" client", ImportRules.Tokenise("\"A \"\"nickname\"\" client\"\r\n")[0][0]);
        }

        [Fact]
        public void A_leading_byte_order_mark_is_ignored()
        {
            var r = ImportRules.ParseCsv("﻿TaskID,AdviserName,AdviserEmail,AssignedBy,ParaplannerEmail,ChecklistItem1,CompletedBy1\nT1,A,a@x.com,P,p@x.com,Tax Check,C\n");

            Assert.Null(r.Fatal);
            Assert.Empty(r.Invalid);
            Assert.Single(r.Valid);
        }

        [Fact]
        public void Resolved_is_refused_while_no_case_carries_the_TaskID()
        {
            var service = new FakeOrganizationService();

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => ResolveImportExceptionPlugin.EnsureCaseImported(service, "300000123"));

            Assert.Contains("PRECONDITION:", error.Message);
            Assert.Contains("300000123", error.Message);
        }

        [Fact]
        public void Resolved_is_allowed_once_the_case_exists()
        {
            var service = new FakeOrganizationService();
            service.Seed("al_outcomecase", Guid.NewGuid(), "al_casereference", "300000123");

            ResolveImportExceptionPlugin.EnsureCaseImported(service, "300000123");
        }

        [Fact]
        public void The_resolve_command_checks_for_the_case_only_when_closing_as_resolved()
        {
            var source = System.IO.File.ReadAllText(SourcePath("ResolveImportExceptionPlugin.cs"));
            var check = source.IndexOf("if (resolution == StatusResolved)", StringComparison.Ordinal);
            var update = source.IndexOf("userService.Update(", StringComparison.Ordinal);

            Assert.True(check > 0 && check < update, "The Resolved check must run before the exception is written.");
        }

        [Fact]
        public void The_check_date_is_the_uk_day_of_the_submit()
        {
            // 23:30 UTC on 1 July is 00:30 on 2 July in London (BST).
            var service = new FakeOrganizationService();
            var caseId = Guid.NewGuid();
            service.Seed("al_outcomecase", caseId);

            SubmitReviewPlugin.StampCheckDate(service, caseId, new DateTime(2026, 7, 1, 23, 30, 0, DateTimeKind.Utc));

            Assert.Equal(new DateTime(2026, 7, 2), service.Updates[0].GetAttributeValue<DateTime>("al_checkdate"));
        }

        // The compile-time path, so the test finds the source wherever the build output lands.
        private static string SourcePath(string file, [System.Runtime.CompilerServices.CallerFilePath] string here = "")
        {
            return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here), "..", "OutcomeTesting.Plugins", file);
        }
    }
}
