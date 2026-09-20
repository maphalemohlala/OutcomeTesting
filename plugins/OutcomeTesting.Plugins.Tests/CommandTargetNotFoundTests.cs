using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// What a command says when the thing it was pointed at is not there.
    ///
    /// <para>
    /// F37, found in DEV on 2026-09-20 working APP-075. `al_RegradeCase` takes the OUTCOME id;
    /// called with the case id it answered "UNEXPECTED: OutcomeTesting.Plugins
    /// .RegradeCasePlugin could not complete. OrganizationServiceFault: Entity 'al_outcome'
    /// With Id = ... Does Not Exist" - a raw platform fault under the prefix this solution
    /// reserves for genuine surprises, where <see cref="CommandHelpers.NotFoundPrefix"/>
    /// exists for exactly this and the app's classifier branches on it.
    /// </para>
    /// <para>
    /// It is not one command's slip. Five of the seven commands that take a TargetId read it
    /// with a bare Retrieve, and the two that do not - CompleteRemediation and
    /// SignOffRemediation, either side of RegradeCase in the same chain - show what was
    /// intended. The realistic case is not a mistyped id: it is a page left open while the
    /// row was removed or the case reset somewhere else, where "refresh and try again" is the
    /// whole of the advice and a stack-shaped sentence is none of it.
    /// </para>
    /// </summary>
    public class CommandTargetNotFoundTests
    {
        /// <summary>
        /// Every plug-in that reads a TargetId must refuse a missing row by name rather than
        /// letting the platform fault escape.
        /// </summary>
        [Fact]
        public void A_command_pointed_at_a_row_that_is_gone_says_so()
        {
            var bare = CommandPlugins()
                .Where(path =>
                {
                    var source = File.ReadAllText(path);
                    return !source.Contains("RetrieveOrNotFound")
                        && !source.Contains("NotFoundPrefix");
                })
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                bare.Length == 0,
                "These commands read their target with a bare Retrieve, so a row that has "
                + "been removed surfaces as UNEXPECTED carrying an OrganizationServiceFault "
                + "instead of NOTFOUND carrying a sentence. Use CommandHelpers"
                + ".RetrieveOrNotFound (F37): " + string.Join(", ", bare));
        }

        [Fact]
        public void The_check_is_looking_at_the_commands_at_all()
        {
            // Guards the test itself: a rename of the TargetId constant would otherwise make
            // the assertion above pass by finding nothing.
            var found = CommandPlugins().Select(Path.GetFileName).ToArray();

            Assert.True(found.Length >= 7, "Found only " + found.Length + " commands.");
            Assert.Contains("RegradeCasePlugin.cs", found);
            Assert.Contains("CompleteRemediationPlugin.cs", found);
        }

        /// <summary>The plug-ins that take a TargetId, which is what makes them addressable.</summary>
        private static string[] CommandPlugins()
        {
            return Directory.GetFiles(PluginsPath(), "*Plugin.cs")
                .Where(path => Regex.IsMatch(
                    File.ReadAllText(path), @"InTargetId\s*=\s*""TargetId"""))
                .ToArray();
        }

        private static string PluginsPath()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null
                && !Directory.Exists(Path.Combine(
                    directory.FullName, "plugins", "OutcomeTesting.Plugins")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(directory.FullName, "plugins", "OutcomeTesting.Plugins");
        }
    }
}
