using System;
using System.IO;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The check form decides nothing for the checker (project owner, 2026-10-05: "Remove any
    /// parts of the form that are automatically greyed out or selected by default", and
    /// "allow any answer"). No test drives the three plug-ins end to end over a full
    /// checklist, so this reads their source, the way AssignCaseIsScopedTests does.
    /// </summary>
    public class OpenCheckFormTests
    {
        private static readonly string[] Rules =
        {
            "GradeRefusal(",
            "FileQualityRefusal(",
            "RemedialActionRefusal(",
            "RemedialActionDefault(",
            "GradeCleared(",
            "FailPointsLocked(",
            "ReadGatingFacts(",
            "ClearOutcomesContradictedByAnswer(",
            "ReconcileRemedialAction(",
        };

        [Theory]
        [InlineData("ResponseGuardPlugin.cs")]
        [InlineData("ResponseProgressPlugin.cs")]
        [InlineData("SubmitReviewPlugin.cs")]
        public void No_plugin_refuses_or_rewrites_an_answer_for_contradicting_the_form(string file)
        {
            var source = File.ReadAllText(Path.Combine(PluginsPath(), file));

            foreach (var rule in Rules)
            {
                Assert.False(source.Contains(rule), file + " still calls " + rule);
            }
        }

        [Fact]
        public void Remediation_is_still_decided_by_the_grade_or_the_flag()
        {
            var source = File.ReadAllText(Path.Combine(PluginsPath(), "SubmitReviewPlugin.cs"));

            Assert.Contains("OutcomeRules.RequiresRemediation(outcomeValue, remedialFlagged)", source);
            Assert.Contains("RemedialActions.EnsureWritten(", source);
        }

        [Fact]
        public void The_root_cause_is_still_cleared_on_a_pass()
        {
            var source = File.ReadAllText(Path.Combine(PluginsPath(), "ResponseProgressPlugin.cs"));

            Assert.Contains("ClearRootCauseOnPass(service, target, pre, reviewRef.Id);", source);
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
