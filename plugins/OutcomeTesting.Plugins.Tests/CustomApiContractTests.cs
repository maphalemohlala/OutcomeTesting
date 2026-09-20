using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Every input a command plug-in reads has to be declared on its Custom API, because a
    /// parameter that is not declared never arrives: Dataverse does not carry it, the code
    /// app's generated schema does not send it, and the plug-in reads null. Nothing fails,
    /// so the value simply goes missing.
    ///
    /// That is F15, found in DEV on 2026-09-20. SetFailAccountabilityPlugin read
    /// FqContactId and AqContactId, wrote them to al_fqaccountablecontactid and
    /// al_aqaccountablecontactid, and named them in the audit line. The contract declared
    /// neither. The panel offered a person picker, counted a changed person as a change
    /// worth saving, reported success - and who carried the fail was never recorded on any
    /// outcome in the environment.
    ///
    /// The plug-in's own tests could not catch it: they hand the parameter straight to the
    /// plug-in, so they exercise a path the product cannot reach. This one reads the
    /// contract instead, which is the thing that was wrong.
    /// </summary>
    public class CustomApiContractTests
    {
        public static IEnumerable<object[]> Contracts()
        {
            return Directory
                .GetFiles(Path.Combine(RepoRoot(), "plugins", "customapi"), "*.customapi.json")
                .OrderBy(path => path)
                .Select(path => new object[] { Path.GetFileName(path) });
        }

        [Theory]
        [MemberData(nameof(Contracts))]
        public void EveryParameterThePluginReadsIsDeclared(string fileName)
        {
            var path = Path.Combine(RepoRoot(), "plugins", "customapi", fileName);
            var json = File.ReadAllText(path);

            var pluginTypeName = Single(json, "\"pluginType\"\\s*:\\s*\"([^\"]+)\"");
            if (pluginTypeName == null)
            {
                // A contract with no plug-in behind it reads nothing, so there is nothing to
                // compare it against.
                return;
            }

            var declared = DeclaredParameters(json);
            var read = ParametersReadBy(pluginTypeName);

            var undeclared = read.Where(name => !declared.Contains(name)).OrderBy(n => n).ToArray();

            Assert.True(
                undeclared.Length == 0,
                fileName + " does not declare " + string.Join(", ", undeclared)
                    + ", but " + pluginTypeName + " reads " + (undeclared.Length == 1 ? "it" : "them")
                    + ". An undeclared parameter never arrives and the plug-in reads null.");
        }

        /// <summary>
        /// The uniquenames under requestParameters. responseProperties carry uniquenames too,
        /// so the search stops where the request parameters do.
        /// </summary>
        private static HashSet<string> DeclaredParameters(string json)
        {
            var start = json.IndexOf("\"requestParameters\"", StringComparison.Ordinal);
            if (start < 0)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var end = json.IndexOf("\"responseProperties\"", start, StringComparison.Ordinal);
            var block = end < 0 ? json.Substring(start) : json.Substring(start, end - start);

            return new HashSet<string>(
                Regex.Matches(block, "\"uniquename\"\\s*:\\s*\"([^\"]+)\"")
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value),
                StringComparer.Ordinal);
        }

        /// <summary>
        /// The input names a plug-in reads, by the convention every one of them follows:
        /// `private const string In&lt;Name&gt; = "&lt;Name&gt;"`. Base types are walked too, so a
        /// parameter read by a shared base is not missed.
        /// </summary>
        private static IEnumerable<string> ParametersReadBy(string pluginTypeName)
        {
            var type = typeof(CommandHelpers).Assembly.GetType(pluginTypeName);
            Assert.True(type != null, "No such plug-in type: " + pluginTypeName);

            var names = new List<string>();
            for (var current = type; current != null; current = current.BaseType)
            {
                names.AddRange(current
                    .GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                    .Where(f => f.Name.Length > 2 && f.Name.StartsWith("In", StringComparison.Ordinal)
                                && char.IsUpper(f.Name[2]))
                    .Select(f => (string)f.GetRawConstantValue())
                    .Where(value => !string.IsNullOrEmpty(value)));
            }

            return names.Distinct(StringComparer.Ordinal);
        }

        private static string Single(string text, string pattern)
        {
            var match = Regex.Match(text, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string RepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "plugins", "customapi")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("plugins/customapi was not found above " + AppContext.BaseDirectory);
        }
    }
}
