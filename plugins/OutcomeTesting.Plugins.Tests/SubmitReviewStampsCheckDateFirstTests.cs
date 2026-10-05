using System;
using System.IO;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// CompletedCheck's case header draws al_checkdate off the case row at the moment its PDF
    /// is built. RaiseRemediation's Remediation.Raise and QueueCasePassed both build that PDF
    /// (the remediation letter and the case-passed letter respectively), so FinaliseReview must
    /// stamp the date before either runs - stamping after meant the letter carried the previous
    /// review's date (final-review fix wave, 2026-10-05). No test drives FinaliseReview end to
    /// end, so this reads the source instead, the way AssignCaseIsScopedTests does.
    /// </summary>
    public class SubmitReviewStampsCheckDateFirstTests
    {
        [Fact]
        public void StampCheckDate_runs_before_the_remediation_and_pass_letters_are_raised()
        {
            var source = File.ReadAllText(Path.Combine(PluginsPath(), "SubmitReviewPlugin.cs"));
            var start = source.IndexOf(
                "private static void FinaliseReview(IOrganizationService service, Entity review, Guid targetId, Guid correlationId)",
                StringComparison.Ordinal);
            var end = source.IndexOf("private static void CreateOutcome(", StringComparison.Ordinal);
            Assert.True(start >= 0, "FinaliseReview was not found.");
            Assert.True(end > start, "CreateOutcome was not found after FinaliseReview.");

            var body = source.Substring(start, end - start);

            var stamp = body.IndexOf("StampCheckDate(", StringComparison.Ordinal);
            var raise = body.IndexOf("RaiseRemediation(", StringComparison.Ordinal);
            var queuePass = body.IndexOf("QueueCasePassed(", StringComparison.Ordinal);

            Assert.True(stamp >= 0, "FinaliseReview no longer stamps the check date.");
            Assert.True(raise >= 0, "FinaliseReview no longer raises remediation.");
            Assert.True(queuePass >= 0, "FinaliseReview no longer queues the case-passed letter.");
            Assert.True(stamp < raise, "StampCheckDate must run before RaiseRemediation, or its letter carries the previous review's date.");
            Assert.True(stamp < queuePass, "StampCheckDate must run before QueueCasePassed, or its letter carries the previous review's date.");
        }

        private static string PluginsPath()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "OutcomeTesting.Plugins")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory.FullName, "OutcomeTesting.Plugins");
        }
    }
}
