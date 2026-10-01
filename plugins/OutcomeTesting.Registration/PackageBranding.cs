using System.IO.Compression;
using System.Text;

namespace OutcomeTesting.Registration;

/// <summary>
/// Rewrites the product name in the labels a solution PACKAGE carries, for an environment
/// that calls the product something else (PROD calls it OTIS; owner, 2026-10-01). Spec:
/// docs/superpowers/specs/2026-10-01-otis-product-name-design.md.
/// </summary>
/// <remarks>
/// <para>
/// Everything that reads a name at run time takes it from the environment (ProductName in the
/// plug-in, the al_ProductName variable in the Code App, a site setting in the portal). What
/// is left is the handful of labels Dataverse takes from the package itself and that no
/// setting can reach: the solution's display name, the Code App's display name, the three
/// security roles, the manager web role and the site record's name. Renaming those in PROD
/// by hand would leave unmanaged layers that hide every later import, so the package is
/// rewritten instead, the same way for every release.
/// </para>
/// <para>
/// Exactly those labels and nothing else. Every rewrite is an exact match on the text DEV
/// exports; a package where an expected label is missing is refused rather than half-branded,
/// and the output is re-read to prove every other file is byte-identical and no id moved.
/// </para>
/// </remarks>
public static class PackageBranding
{
    private const string Old = OutcomeTesting.Plugins.ProductName.Default;

    private const string ManagerWebRoleComponent =
        "powerpagecomponents/a1000000-0000-4000-8000-000000000095/powerpagecomponent.xml";

    private record Rewrite(string Entry, string From, string To, string What);

    private static List<Rewrite> Rewrites(string product)
    {
        var p = OutcomeTesting.Plugins.ProductName.AppUserRole(product);
        return new List<Rewrite>
        {
            new("solution.xml",
                "<LocalizedName description=\"" + Old + "\" languagecode=\"1033\" />",
                "<LocalizedName description=\"" + product + "\" languagecode=\"1033\" />",
                "solution display name"),
            new("customizations.xml",
                "name=\"" + OutcomeTesting.Plugins.ProductName.AppAdminRole(Old) + "\">",
                "name=\"" + OutcomeTesting.Plugins.ProductName.AppAdminRole(product) + "\">",
                "security role App Admin"),
            new("customizations.xml",
                "name=\"" + OutcomeTesting.Plugins.ProductName.AppUserRole(Old) + "\">",
                "name=\"" + p + "\">",
                "security role App User"),
            new("customizations.xml",
                "name=\"" + OutcomeTesting.Plugins.ProductName.TeamManagerRole(Old) + "\">",
                "name=\"" + OutcomeTesting.Plugins.ProductName.TeamManagerRole(product) + "\">",
                "security role Team Manager"),
            new("customizations.xml",
                "<DisplayName>Ascot Lloyd " + Old + "</DisplayName>",
                "<DisplayName>" + product + "</DisplayName>",
                "Code App display name"),
            new(ManagerWebRoleComponent,
                "<name>" + OutcomeTesting.Plugins.ProductName.ManagerWebRole(Old) + "</name>",
                "<name>" + OutcomeTesting.Plugins.ProductName.ManagerWebRole(product) + "</name>",
                "manager web role"),
            new("Assets/powerpagesites.xml",
                "<name>" + Old + " - outcometesting</name>",
                "<name>" + product + "</name>",
                "site name"),
        };
    }

    /// <summary>Brands <paramref name="input"/> into <paramref name="output"/>. Returns 0 on success.</summary>
    public static int Run(string input, string output, string product)
    {
        product = (product ?? string.Empty).Trim();
        if (product.Length == 0 || product == Old)
        {
            Console.Error.WriteLine($"Give the product name to brand as; \"{Old}\" is what the package already says.");
            return 1;
        }

        var source = File.ReadAllBytes(input);
        var rewrites = Rewrites(product);
        var byEntry = rewrites.GroupBy(r => r.Entry).ToDictionary(g => g.Key, g => g.ToList());

        using var inZip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);

        // Refuse before writing anything: every label must be found exactly once.
        foreach (var r in rewrites)
        {
            var entry = inZip.GetEntry(r.Entry);
            var text = entry == null ? null : ReadText(entry);
            var count = text == null ? 0 : Count(text, r.From);
            if (count != 1)
            {
                Console.Error.WriteLine($"Refused: expected the {r.What} once in {r.Entry}, found it {count} time(s): {r.From}");
                return 1;
            }
        }

        var result = new MemoryStream();
        using (var outZip = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in inZip.Entries)
            {
                var copy = outZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copy.LastWriteTime = entry.LastWriteTime;
                using var target = copy.Open();
                if (byEntry.TryGetValue(entry.FullName, out var list))
                {
                    var text = ReadText(entry);
                    foreach (var r in list)
                    {
                        text = text.Replace(r.From, r.To);
                        Console.WriteLine($"  {r.What,-26} {Strip(r.From)}  ->  {Strip(r.To)}");
                    }

                    var bytes = new UTF8Encoding(HasBom(entry)).GetBytes(text);
                    if (HasBom(entry)) target.Write(Encoding.UTF8.GetPreamble());
                    target.Write(bytes);
                }
                else
                {
                    using var from = entry.Open();
                    from.CopyTo(target);
                }
            }
        }

        // Prove it: same entries, every untouched entry byte-identical, every label changed.
        result.Position = 0;
        using (var check = new ZipArchive(result, ZipArchiveMode.Read, leaveOpen: true))
        {
            var names = inZip.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
            if (!names.SequenceEqual(check.Entries.Select(e => e.FullName).OrderBy(n => n)))
            {
                Console.Error.WriteLine("Refused: the branded package does not hold the same files.");
                return 1;
            }

            foreach (var entry in inZip.Entries)
            {
                var after = check.GetEntry(entry.FullName)!;
                if (byEntry.ContainsKey(entry.FullName))
                {
                    var text = ReadText(after);
                    foreach (var r in byEntry[entry.FullName])
                    {
                        if (Count(text, r.From) != 0 || Count(text, r.To) < 1)
                        {
                            Console.Error.WriteLine($"Refused: the {r.What} did not change in {entry.FullName}.");
                            return 1;
                        }
                    }

                    continue;
                }

                if (!Bytes(entry).AsSpan().SequenceEqual(Bytes(after)))
                {
                    Console.Error.WriteLine($"Refused: {entry.FullName} changed and should not have.");
                    return 1;
                }
            }
        }

        File.WriteAllBytes(output, result.ToArray());
        Console.WriteLine($"Branded as \"{product}\": {rewrites.Count} labels in {byEntry.Count} files; "
            + $"{inZip.Entries.Count - byEntry.Count} other files byte-identical. Wrote {output} ({result.Length} bytes).");
        return 0;
    }

    private static int Count(string text, string what)
    {
        var n = 0;
        for (var i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + what.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    private static byte[] Bytes(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    private static bool HasBom(ZipArchiveEntry entry)
    {
        var b = Bytes(entry);
        return b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        var b = Bytes(entry);
        return HasBom(entry) ? Encoding.UTF8.GetString(b, 3, b.Length - 3) : Encoding.UTF8.GetString(b);
    }

    private static string Strip(string s) => s.Replace("<", "").Replace(">", "").Replace("\"", "'");
}
