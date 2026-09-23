using System;
using System.IO;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The allocation command asks AllocationScope both of its questions (AD-218, closing
    /// OD-029(c)). No test drives AssignCasePlugin.Execute end to end - it touches a dozen
    /// tables - so AllocationScopeTests would stay green with the calls deleted. This reads
    /// the source instead, the way CommandTargetNotFoundTests does, and pins the ORDER too:
    /// the discipline is the review's, so both checks must follow ResolveReviewInstance.
    /// </summary>
    public class AssignCaseIsScopedTests
    {
        [Fact]
        public void Allocation_checks_the_caller_and_the_assignee_after_resolving_the_review()
        {
            var source = File.ReadAllText(Path.Combine(PluginsPath(), "AssignCasePlugin.cs"));
            var body = source.Substring(source.IndexOf("protected override void ExecuteDataversePlugin", StringComparison.Ordinal));

            var resolve = body.IndexOf("ResolveReviewInstance(", StringComparison.Ordinal);
            var caller = body.IndexOf("AllocationScope.EnsureCallerMayAllocate(", StringComparison.Ordinal);
            var assignee = body.IndexOf("AllocationScope.EnsureAssigneeHoldsDiscipline(", StringComparison.Ordinal);
            var write = body.IndexOf("AllocateAssignment(", StringComparison.Ordinal);

            Assert.True(resolve >= 0, "ExecuteDataversePlugin no longer resolves the review.");
            Assert.True(caller > resolve, "The caller's discipline scope is not checked after the review is resolved.");
            Assert.True(assignee > resolve, "The assignee's reviewer role is not checked after the review is resolved.");
            Assert.True(caller < write && assignee < write, "An assignment row is written before the scope checks.");
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
