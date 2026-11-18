using System;
using System.Text;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The import asks Contact once per address per file, not twice per row: a file of a
    /// thousand rows names the same few advisers and para-planners.
    /// </summary>
    public class ImportCasesFixTests
    {
        private const int Rows = 4;

        private static int QueriesFor(bool sharedPeople)
        {
            var csv = new StringBuilder("TaskID,AdviserName,AdviserEmail,AssignedBy,ParaplannerEmail,ChecklistItem1,CompletedBy1\n");
            for (var i = 0; i < Rows; i++)
            {
                var who = sharedPeople ? "0" : i.ToString();
                csv.Append("T" + i + ",A,a" + who + "@x.com,P,p" + who + "@x.com,Tax Check,C\n");
            }

            var service = new FakeOrganizationService();
            service.Seed("al_reviewroute", Guid.NewGuid(), "al_routecode", "ROUTE-AQS");
            service.Seed("al_reviewroute", Guid.NewGuid(), "al_routecode", "ROUTE-TAX-AQS");

            var provider = new FakeServiceProvider(service);
            provider.Context.MessageName = "al_ImportCases";
            provider.Context.InputParameters["FileName"] = "extract.csv";
            provider.Context.InputParameters["Csv"] = csv.ToString();
            provider.Context.InputParameters["IdempotencyKey"] = Guid.NewGuid().ToString("N");
            new ImportCasesPlugin(null, null).Execute(provider);

            Assert.Equal(Rows.ToString(), provider.Context.OutputParameters["Imported"]);
            return service.RetrieveMultipleCount;
        }

        [Fact]
        public void Repeated_people_are_looked_up_once_per_import()
        {
            // Same rows, same everything, except whether the rows share their two people. The
            // only difference left is the contact lookups: one adviser and one para-planner
            // query per new address, and none for an address the import has already seen.
            Assert.Equal(2 * (Rows - 1), QueriesFor(sharedPeople: false) - QueriesFor(sharedPeople: true));
        }
    }
}
