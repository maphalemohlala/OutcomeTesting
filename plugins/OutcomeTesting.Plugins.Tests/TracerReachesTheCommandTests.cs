using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A command that can explain itself to the trace log is actually given somewhere to
    /// write.
    ///
    /// <para>
    /// F41, found in DEV on 2026-09-20 working APP-070. When a case's adviser is not in
    /// al_advisermapping, NotifySignoffDue sends no letter - that is deliberate, and
    /// documented: "an unmapped adviser costs a notification, not the ability to proceed".
    /// What is supposed to make that safe is the next paragraph: "The reason goes to the
    /// trace log instead (NFR-OBS-01), which is where an administrator looks when a manager
    /// says they were not told."
    /// </para>
    /// <para>
    /// It did not. <c>Complete</c> takes <c>Action&lt;string&gt; trace = null</c>, and
    /// <c>ExecuteDataversePlugin</c> - the only caller in production - never passed one. So
    /// both <c>if (trace != null)</c> arms were dead: the unrouted reason, and the catch that
    /// reports a sign-off-due notification failing outright. Live in DEV on case 910000004
    /// (adviser <c>nobody.atall@example.com</c>, unmapped): the case reached Awaiting Sign-off,
    /// no letter was queued, and the al_CompleteRemediation trace log held nothing but
    /// "Entered" and "Exiting". Nobody was told, and nothing recorded that nobody was told.
    /// </para>
    /// <para>
    /// Same shape as F33: an optional argument that silently disables a feature when it is
    /// left out, with a green test suite over it because every test supplied the argument by
    /// hand. SignoffDueNotificationTests passes a tracer and asserts the message; the
    /// behaviour it proves had never run.
    /// </para>
    /// </summary>
    public class TracerReachesTheCommandTests
    {
        /// <summary>
        /// Guards the class, not the one command: wherever a shared implementation takes a
        /// tracer, the plug-in entry that calls it has to supply one. A tracer nothing passes
        /// is a diagnostic nobody gets.
        /// </summary>
        [Fact]
        public void Every_command_that_accepts_a_tracer_is_given_one_by_its_plug_in()
        {
            var missing = PluginsWithATracer()
                .Where(path => !Regex.IsMatch(ReadEntryPoint(path), @"\btrace\s*:"))
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                missing.Length == 0,
                "These plug-ins accept an Action<string> trace and their ExecuteDataversePlugin "
                + "passes none, so every 'if (trace != null)' arm inside them is dead in "
                + "production and the reason they went quiet is written nowhere (F41). Pass "
                + "localPluginContext.Trace: " + string.Join(", ", missing));
        }

        [Fact]
        public void The_check_is_looking_at_something()
        {
            // Without this the assertion above passes by finding no plug-ins at all, which is
            // how the hole it guards stayed open in the first place.
            var found = PluginsWithATracer().Select(Path.GetFileName).ToArray();

            Assert.NotEmpty(found);
            Assert.Contains("CompleteRemediationPlugin.cs", found);
        }

        /// <summary>Plug-in files whose shared implementation accepts a tracer.</summary>
        private static string[] PluginsWithATracer()
        {
            return Directory.GetFiles(PluginsPath(), "*Plugin.cs")
                .Where(path => File.ReadAllText(path).Contains("Action<string> trace"))
                .ToArray();
        }

        /// <summary>
        /// The body of ExecuteDataversePlugin, which is the production door. Read on its own
        /// because the file also contains the shared implementation, and matching "trace:"
        /// anywhere in the file would be satisfied by the very declaration under test.
        /// </summary>
        private static string ReadEntryPoint(string path)
        {
            var source = File.ReadAllText(path);
            var start = source.IndexOf("ExecuteDataversePlugin", StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            var depth = 0;
            var started = false;
            for (var i = start; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                    started = true;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (started && depth == 0)
                    {
                        return source.Substring(start, i - start + 1);
                    }
                }
            }

            return source.Substring(start);
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
