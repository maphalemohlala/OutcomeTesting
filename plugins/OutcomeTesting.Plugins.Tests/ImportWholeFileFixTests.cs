using System;
using System.Linq;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Owner decision, 2026-11-17: a UK date-time in a date column imports as its UK date;
    /// rows that can be refused are found before anything is written; and a fault while
    /// writing fails the whole file with one sentence instead of counting rows the platform
    /// then rolls back.
    /// </summary>
    public class ImportWholeFileFixTests
    {
        private const string Header =
            "TaskID,AdviserName,AdviserEmail,AssignedBy,ParaplannerEmail,ChecklistItem1,CompletedBy1,StartDate\n";

        private static string Row(string taskId, string item = "Leaver", string startDate = "")
        {
            return taskId + ",A,a@x.com,P,p@x.com," + item + ",C," + startDate + "\n";
        }

        // ------------------------------------------------ a UK date-time in a date column

        [Theory]
        [InlineData("18/08/2026 13:54", 2026, 8, 18)]
        [InlineData("06/08/2026 14:49", 2026, 8, 6)]
        [InlineData("6/8/2026 09:05:30", 2026, 8, 6)]
        [InlineData("2026-09-04", 2026, 9, 4)]
        [InlineData("2026-09-04T13:54:00", 2026, 9, 4)]
        [InlineData("2026-09-04T23:30:00.000+01:00", 2026, 9, 4)]
        [InlineData("2026-09-04 08:15", 2026, 9, 4)]
        public void A_uk_date_time_reads_as_its_uk_date(string value, int y, int m, int d)
        {
            Assert.Equal(new DateTime(y, m, d), ImportRules.ParseDate(value));
        }

        [Theory]
        [InlineData("13:54")]
        [InlineData("10:00 AM")]
        [InlineData("soon")]
        [InlineData("18/08/2026 25:99")]
        [InlineData("13/13/2026 10:00")]
        [InlineData("2026-09-04junk")]
        [InlineData("2026-09-04T25:00:00")]
        [InlineData("2026-09-04 later")]
        public void A_time_alone_or_nonsense_is_still_not_a_date(string value)
        {
            Assert.Null(ImportRules.ParseDate(value));
        }

        [Fact]
        public void A_row_with_a_uk_date_time_imports_with_its_date()
        {
            var row = Assert.Single(ImportRules.ParseCsv(Header + Row("T1", startDate: "18/08/2026 13:54")).Valid);

            Assert.Equal(new DateTime(2026, 8, 18), row.Values["al_taskstartdate"]);
        }

        [Fact]
        public void A_row_with_a_time_alone_fails_naming_the_column_and_the_value()
        {
            var error = Assert.Single(ImportRules.ParseCsv(Header + Row("T1", startDate: "13:54")).Invalid);

            Assert.Equal("\"StartDate\" is not a valid date: \"13:54\".", error.Reason);
        }

        // ------------------------------------------------ checked before anything is written

        [Fact]
        public void A_row_whose_route_is_not_configured_is_refused_before_any_case_is_written()
        {
            // Only the AQS route exists, so the Tax-only row cannot be routed. It is reported as an
            // invalid row and the other row still imports.
            var service = new FakeOrganizationService();
            service.Seed("al_reviewroute", Guid.NewGuid(), "al_routecode", "ROUTE-AQS");
            var provider = Run(service, Header + Row("T1", "Tax Check") + Row("T2"));

            new ImportCasesPlugin(null, null).Execute(provider);

            Assert.Equal("1", provider.Context.OutputParameters["Imported"]);
            Assert.Equal("1", provider.Context.OutputParameters["Failed"]);
            var report = (string)provider.Context.OutputParameters["Report"];
            Assert.Contains("\"rowNumber\":2,\"caseReference\":\"T1\",\"status\":\"Invalid\"", report);
            Assert.Contains("Review route 'ROUTE-TAX' is not configured.", report);
            Assert.Equal(new[] { "T2" }, service.All("al_outcomecase").Select(c => c.GetAttributeValue<string>("al_casereference")));
        }

        // ------------------------------------------------ a fault while writing fails the file

        [Fact]
        public void A_case_the_platform_refuses_fails_the_whole_file_naming_the_row()
        {
            var service = WithRoutes();
            var failing = new FailingService(service)
            {
                CreateThrowsFor = "T2",
                Fault = new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { Message = "VALIDATION: The adviser's email \"a@x\" is not allowed." },
                    "VALIDATION: The adviser's email \"a@x\" is not allowed."),
            };
            var provider = Run(failing, Header + Row("T1") + Row("T2") + Row("T3"));

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => new ImportCasesPlugin(null, null).Execute(provider));

            // The same sentence the app test reads back (caseUpload.fix.test.ts). Quotes in
            // the reason become single quotes, because the app cuts a message at a double one.
            Assert.Equal(
                "VALIDATION: Row 3 (TaskID T2) could not be saved, so nothing from this file was imported: "
                + "The adviser's email 'a@x' is not allowed. Fix that row and upload the file again.",
                error.Message);

            // Stopped at the fault rather than going on to T3.
            Assert.DoesNotContain(
                service.All("al_outcomecase"),
                c => c.GetAttributeValue<string>("al_casereference") == "T3");
        }

        [Fact]
        public void A_platform_fault_with_no_readable_reason_still_names_the_row()
        {
            var failing = new FailingService(WithRoutes())
            {
                CreateThrowsFor = "T1",
                Fault = new InvalidOperationException("SqlException: deadlock victim"),
            };
            var provider = Run(failing, Header + Row("T1"));

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => new ImportCasesPlugin(null, null).Execute(provider));

            Assert.StartsWith("VALIDATION: Row 2 (TaskID T1) could not be saved, so nothing from this file was imported: ", error.Message);
            Assert.DoesNotContain("Sql", error.Message);
        }

        [Fact]
        public void A_queue_hop_the_platform_refuses_fails_the_whole_file_too()
        {
            var failing = new FailingService(WithRoutes())
            {
                UpdateCaseThrows = new InvalidPluginExecutionException("PRECONDITION: The case cannot move to Queued."),
            };
            var provider = Run(failing, Header + Row("T1"));

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => new ImportCasesPlugin(null, null).Execute(provider));

            Assert.Equal(
                "VALIDATION: Row 2 (TaskID T1) could not be saved, so nothing from this file was imported: "
                + "The case cannot move to Queued. Fix that row and upload the file again.",
                error.Message);
        }

        // ------------------------------------------------ harness

        private static FakeOrganizationService WithRoutes()
        {
            var service = new FakeOrganizationService();
            service.Seed("al_reviewroute", Guid.NewGuid(), "al_routecode", "ROUTE-AQS");
            service.Seed("al_reviewroute", Guid.NewGuid(), "al_routecode", "ROUTE-TAX-AQS");
            return service;
        }

        private static Provider Run(IOrganizationService service, string csv)
        {
            var provider = new Provider(service);
            provider.Context.MessageName = "al_ImportCases";
            provider.Context.InputParameters["FileName"] = "extract.csv";
            provider.Context.InputParameters["Csv"] = csv;
            provider.Context.InputParameters["IdempotencyKey"] = Guid.NewGuid().ToString("N");
            return provider;
        }

        /// <summary>FakeServiceProvider, but handing out any service rather than only the fake.</summary>
        private sealed class Provider : IServiceProvider, IOrganizationServiceFactory
        {
            private readonly FakeServiceProvider _inner = new FakeServiceProvider();
            private readonly IOrganizationService _service;

            public Provider(IOrganizationService service)
            {
                _service = service;
            }

            public FakePluginExecutionContext Context
            {
                get { return _inner.Context; }
            }

            public object GetService(Type serviceType)
            {
                return serviceType == typeof(IOrganizationServiceFactory) ? this : _inner.GetService(serviceType);
            }

            public IOrganizationService CreateOrganizationService(Guid? userId)
            {
                return _service;
            }
        }

        /// <summary>The fake, with one case create or every case update failing.</summary>
        private sealed class FailingService : IOrganizationService
        {
            private readonly FakeOrganizationService _inner;

            public FailingService(FakeOrganizationService inner)
            {
                _inner = inner;
            }

            public string CreateThrowsFor { get; set; }

            public Exception Fault { get; set; }

            public Exception UpdateCaseThrows { get; set; }

            public Guid Create(Entity entity)
            {
                if (entity.LogicalName == "al_outcomecase"
                    && entity.GetAttributeValue<string>("al_casereference") == CreateThrowsFor)
                {
                    throw Fault;
                }

                return _inner.Create(entity);
            }

            public void Update(Entity entity)
            {
                if (entity.LogicalName == "al_outcomecase" && UpdateCaseThrows != null)
                {
                    throw UpdateCaseThrows;
                }

                _inner.Update(entity);
            }

            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
            {
                return _inner.Retrieve(entityName, id, columnSet);
            }

            public void Delete(string entityName, Guid id)
            {
                _inner.Delete(entityName, id);
            }

            public OrganizationResponse Execute(OrganizationRequest request)
            {
                return _inner.Execute(request);
            }

            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            {
                _inner.Associate(entityName, entityId, relationship, relatedEntities);
            }

            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            {
                _inner.Disassociate(entityName, entityId, relationship, relatedEntities);
            }

            public EntityCollection RetrieveMultiple(QueryBase query)
            {
                return _inner.RetrieveMultiple(query);
            }
        }
    }
}
