using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Every question code the plug-ins or the portal templates read by literal is one
    /// ChecklistGuards refuses to retire or move. Read from the sources rather than restated,
    /// because a hand-kept list is exactly what let the Tax categories, the notes cards' codes
    /// and the Q-AML prefix go unguarded.
    /// </summary>
    public class ChecklistGuardsFixTests
    {
        /// <summary>A quoted code in C#: "Q-GR-01", or a prefix such as "Q-AML".</summary>
        private static readonly Regex CsharpLiteral = new Regex("\"(Q-[A-Z]+(?:-[0-9]+)?)\"");

        /// <summary>
        /// A code a template compares or fetches by: 'Q-GR-01', a 'Q-GR-03,Q-GR-04' split
        /// list, or a FetchXML &lt;value&gt;. A code merely named in a comment is not matched.
        /// </summary>
        private static readonly Regex TemplateLiteral =
            new Regex("(?:'|<value>|,)(Q-[A-Z]+(?:-[0-9]+)?)(?=['<,])");

        public static IEnumerable<object[]> CodesReadByLiteral()
        {
            var root = RepoRoot();
            var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            var plugins = Path.Combine(root, "plugins", "OutcomeTesting.Plugins");
            foreach (var file in Directory.GetFiles(plugins, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    || Path.GetFileName(file) == "ChecklistGuards.cs")
                {
                    continue;
                }

                foreach (Match match in CsharpLiteral.Matches(File.ReadAllText(file)))
                {
                    found.Add(match.Groups[1].Value);
                }
            }

            var templates = Path.Combine(root, "powerpages");
            foreach (var file in Directory.GetFiles(templates, "*.webtemplate.source.html", SearchOption.AllDirectories))
            {
                foreach (Match match in TemplateLiteral.Matches(File.ReadAllText(file)))
                {
                    found.Add(match.Groups[1].Value);
                }
            }

            return found.Select(code => new object[] { code });
        }

        /// <summary>
        /// Codes read only to show earlier answers, which may therefore be retired. Q-TAX-04
        /// "Tax Remedial" was replaced by the Tax categories (client, 2026-11-06) and is retired
        /// in DEV and TEST; OT Tax Notes still fetches its answers by code with no date filter,
        /// so a check completed before the retirement keeps its note.
        /// </summary>
        private static readonly HashSet<string> ReadForHistoryOnly =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Q-TAX-04" };

        [Theory]
        [MemberData(nameof(CodesReadByLiteral))]
        public void Every_code_read_by_literal_is_protected(string code)
        {
            if (ReadForHistoryOnly.Contains(code))
            {
                Assert.Null(ChecklistGuards.ProtectedReason(code));
                return;
            }

            // A prefix guards every code that starts with it; check a code it would cover.
            var asCode = Regex.IsMatch(code, "-[0-9]+$") ? code : code + "-01";

            Assert.False(
                string.IsNullOrWhiteSpace(ChecklistGuards.ProtectedReason(asCode)),
                code + " is read by name in compiled code or a portal template but ChecklistGuards does not protect it, "
                + "so retiring or moving it would silently break what reads it.");
        }

        [Fact]
        public void The_scan_finds_the_codes_this_fix_was_raised_for()
        {
            // Guards the scan itself: a regex that matched nothing would pass the theory above.
            var codes = CodesReadByLiteral().Select(row => (string)row[0]).ToList();

            foreach (var expected in new[] { "Q-AML", "Q-GR-01", "Q-GR-03", "Q-GR-04", "Q-TAX-03", "Q-TAX-04", "Q-TAX-05", "Q-TAX-12" })
            {
                Assert.Contains(expected, codes);
            }
        }

        [Theory]
        [InlineData("Q-AML-01")]
        [InlineData("Q-AML-05")]
        [InlineData("q-aml-02")]
        public void Every_aml_checking_point_is_protected_by_its_prefix(string code)
        {
            var reason = ChecklistGuards.ProtectedReason(code);

            Assert.NotNull(reason);
            Assert.StartsWith(code, reason);
            Assert.EndsWith(".", reason);
        }

        [Fact]
        public void Every_reason_names_its_code_and_ends_as_a_sentence()
        {
            foreach (var code in ChecklistGuards.ProtectedCodes)
            {
                var reason = ChecklistGuards.ProtectedReason(code);
                Assert.StartsWith(code, reason);
                Assert.EndsWith(".", reason);
                Assert.DoesNotMatch("\\b(AD|OD|BR|FR|PP)-[0-9]", reason);
            }
        }

        /// <summary>
        /// Found from this source file rather than the test binary, so the scan works whatever
        /// --artifacts-path the build was given.
        /// </summary>
        private static string RepoRoot([CallerFilePath] string here = null)
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", ".."));
        }
    }
}
