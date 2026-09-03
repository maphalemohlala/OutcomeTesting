using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Xrm.Tooling.Dmt.ImportProcessor.DataInteraction;

namespace OutcomeTesting.DataImport
{
    /// <summary>
    /// Headless Configuration Migration import (OD-028).
    ///
    /// Usage:
    ///   OutcomeTesting.DataImport.exe &lt;orgUrl&gt; &lt;dataFolder&gt;
    ///
    /// The data folder is a Configuration Migration package directory — the one holding
    /// data.xml, data_schema.xml and [Content_Types].xml — exactly what the GUI accepts
    /// and what `pac data import --data` accepted before the verb was withdrawn.
    ///
    /// Records are matched on the key the schema declares, so a re-run updates rather
    /// than duplicating. That is the whole point: NFR-REL-01 requires configuration
    /// loading to be idempotent, and the only honest way to show it is to run twice and
    /// count rows.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine(
                    "Usage: OutcomeTesting.DataImport.exe <orgUrl> <dataFolder>");
                return 1;
            }

            var orgUrl = args[0];
            var dataFolder = Path.GetFullPath(args[1]);

            if (!Directory.Exists(dataFolder))
            {
                Console.Error.WriteLine($"No such folder: {dataFolder}");
                return 1;
            }

            // The engine reads the schema from the folder it is handed, so both files must
            // be there. Failing here beats failing halfway through a write.
            foreach (var required in new[] { "data.xml", "data_schema.xml" })
            {
                if (!File.Exists(Path.Combine(dataFolder, required)))
                {
                    Console.Error.WriteLine($"{dataFolder} has no {required}.");
                    return 1;
                }
            }

            CrmServiceClient client;
            try
            {
                client = Connect(orgUrl);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Connection failed: {ex.Message}");
                return 1;
            }

            if (!client.IsReady)
            {
                Console.Error.WriteLine($"Connection failed: {client.LastCrmError}");
                return 1;
            }

            Console.WriteLine($"Connected to {orgUrl}.");
            Console.WriteLine($"Importing {dataFolder}…");

            var handler = new ImportCrmDataHandler
            {
                CrmConnection = client,
                // Never true. `deleteBeforeAdd` is the destructive mode; a seed load must
                // upsert, and OD-028 is specifically about the update path working.
                OverrideDataImportSafetyChecks = false,
            };

            var progress = new List<string>();
            handler.AddNewProgressItem += (_, e) => Record(progress, e);
            handler.UpdateProgressItem += (_, e) => Record(progress, e);

            if (!handler.ValidateSchemaFile(dataFolder))
            {
                Console.Error.WriteLine("The schema file was rejected by the import engine.");
                WriteLog(handler);
                return 1;
            }

            bool ok;
            try
            {
                ok = handler.ImportDataToCrm(dataFolder, deleteBeforeAdd: false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Import threw: {ex.Message}");
                WriteLog(handler);
                return 1;
            }

            foreach (var line in progress)
            {
                Console.WriteLine($"  {line}");
            }

            WriteLog(handler);

            Console.WriteLine(ok
                ? $"Import reported success. Entities processed: {handler.ImportEntityObjectCount}."
                : "Import reported failure.");

            return ok ? 0 : 1;
        }

        /// <summary>
        /// Same interactive OAuth as <c>plugins/OutcomeTesting.Registration</c>, and the
        /// same well-known public client id, so the two tools share one cached token and
        /// this needs no external token tool (the AD-063 blocker).
        /// </summary>
        private static CrmServiceClient Connect(string orgUrl)
        {
            var connectionString =
                $"AuthType=OAuth;Url={orgUrl.TrimEnd('/')};" +
                "AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;" +
                "RedirectUri=http://localhost;LoginPrompt=Auto";

            Console.WriteLine($"Connecting to {orgUrl} (a browser opens for sign-in the first time)…");
            return new CrmServiceClient(connectionString);
        }

        private static void Record(ICollection<string> sink, EventArgs e)
        {
            var text = e?.ToString();
            if (!string.IsNullOrWhiteSpace(text) && text != e.GetType().FullName)
            {
                sink.Add(text);
            }
        }

        /// <summary>
        /// The engine collects per-record outcomes in its own log. A run that reports
        /// success while every row failed is the failure mode worth catching, so the
        /// counts are printed whatever the result.
        ///
        /// The breakdown by <c>Action</c> is the point for OD-028: a second run of the same
        /// package should report updates, not creates. Row counts alone cannot tell those
        /// apart — a wrong match key duplicates rows, and only the action says so.
        /// </summary>
        private static void WriteLog(ImportCrmDataHandler handler)
        {
            var entries = handler.ImportLog?.Entries;
            if (entries == null || entries.Count == 0)
            {
                Console.WriteLine("The engine recorded no per-record results.");
                return;
            }

            var failures = entries.Where(entry => !entry.Success).ToList();
            Console.WriteLine($"Records logged: {entries.Count}, failures: {failures.Count}.");

            foreach (var group in entries
                .Where(entry => entry.Success)
                .GroupBy(entry => $"{entry.EntityName} / {entry.Action}")
                .OrderBy(group => group.Key))
            {
                Console.WriteLine($"  {group.Key}: {group.Count()}");
            }

            foreach (var failure in failures.Take(20))
            {
                Console.WriteLine($"  FAILED {failure.EntityName} [{failure.Action}]: {failure.LogEntry}");
            }
        }
    }
}
