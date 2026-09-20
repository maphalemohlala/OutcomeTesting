using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The effective-from date AD-122 relies on, across every command that can change what a
    /// review is owed.
    ///
    /// F31, found in DEV on 2026-09-20 while working APP-047. AD-122 accepts a real cost — a
    /// mandatory question in force today is owed by every unsubmitted review of that
    /// discipline at its next submit — and names the control for it: "which is why every
    /// command exposes the effective-from date."
    ///
    /// Six of the seven did. `al_RetireAndSucceedQuestion` hard-coded `DateTime.UtcNow.Date`
    /// for both the retirement and the successor, and it is the command that changes a
    /// question's WORDING, which is the most ordinary edit there is. So the one mitigation
    /// the decision leans on was missing exactly where it was argued to apply.
    ///
    /// The first test checks the decision rather than this one command, so a command added
    /// later without a date is caught the same way.
    /// </summary>
    public class ChecklistEffectiveFromContractTests
    {
        /// <summary>
        /// The commands that change WHEN something is in force, and therefore what an
        /// unsubmitted review is owed at its next submit.
        ///
        /// The two Retire commands carry `EffectiveTo` rather than `EffectiveFrom`; it is the
        /// same control pointing the other way, so they are named here with the column they
        /// actually use. `UpdateSection` is deliberately absent: owner role, name and help
        /// text are not versioned and carry no dates, which AD-122's design note records as
        /// the only shape available.
        /// </summary>
        public static IEnumerable<object[]> DatedCommands()
        {
            yield return new object[] { "al_AddQuestion", "EffectiveFrom" };
            yield return new object[] { "al_AddSection", "EffectiveFrom" };
            yield return new object[] { "al_MoveQuestion", "EffectiveFrom" };
            yield return new object[] { "al_RetireAndSucceedQuestion", "EffectiveFrom" };
            yield return new object[] { "al_RetireQuestion", "EffectiveTo" };
            yield return new object[] { "al_RetireSection", "EffectiveTo" };
        }

        [Theory]
        [MemberData(nameof(DatedCommands))]
        public void EveryCommandThatChangesWhatAReviewOwesExposesItsDate(
            string command, string parameter)
        {
            var contract = File.ReadAllText(ContractPath(command));

            Assert.True(
                contract.Contains("\"" + parameter + "\""),
                command + " does not expose " + parameter + ", so an administrator cannot "
                + "date the change forward and every in-flight review of that discipline is "
                + "owed it at the next submit (AD-122).");
        }

        [Fact]
        public void TheDateIsOptionalSoExistingCallersAreUnaffected()
        {
            // Absent means today, which is what this command did before it had the parameter
            // at all. Making it required would break every caller to fix none of them.
            var contract = File.ReadAllText(ContractPath("al_RetireAndSucceedQuestion"));
            var block = Regex.Match(
                contract,
                "\\{[^{}]*\"EffectiveFrom\"[^{}]*\\}",
                RegexOptions.Singleline);

            Assert.True(block.Success, "EffectiveFrom is not declared as a parameter object.");
            Assert.Contains("\"isoptional\": true", block.Value);
        }

        [Fact]
        public void AbsentIsToday()
        {
            var today = new DateTime(2026, 9, 20);

            Assert.Equal(today, AddQuestionPlugin.ParseEffectiveFrom(null, today));
            Assert.Equal(today, AddQuestionPlugin.ParseEffectiveFrom("   ", today));
        }

        [Fact]
        public void AFutureDayIsTakenAsGiven()
        {
            // The whole point: dated forward, in-flight reviews finish against the set they
            // started with.
            var today = new DateTime(2026, 9, 20);

            Assert.Equal(
                new DateTime(2026, 10, 1),
                AddQuestionPlugin.ParseEffectiveFrom("2026-10-01", today));
        }

        [Fact]
        public void ThePastIsRefusedInASentence()
        {
            var today = new DateTime(2026, 9, 20);

            var refusal = Assert.Throws<Microsoft.Xrm.Sdk.InvalidPluginExecutionException>(
                () => AddQuestionPlugin.ParseEffectiveFrom("2026-09-19", today));

            Assert.Contains("cannot be in the past", refusal.Message);
        }

        private static string ContractPath(string command)
        {
            // Walk up to the repository root: the test binary sits several folders below it.
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null
                && !Directory.Exists(Path.Combine(directory.FullName, "plugins", "customapi")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(
                directory.FullName, "plugins", "customapi", command + ".customapi.json");
        }
    }
}
