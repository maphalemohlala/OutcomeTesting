using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Audit fixes to the export: per-batch memos for routes and people, a case that cannot
    /// be exported complete left out (and named) instead of refusing the batch, and a
    /// Tax-only case's file quality read only from the review its route owes.
    /// </summary>
    public class GenerateExportFixTests
    {
        private const int Closed = 120910591;
        private const int Draft = 120910770;
        private static readonly Guid AqsRoute = Guid.Parse("e0000000-0000-4000-8000-000000000001");
        private static readonly Guid TaxRoute = Guid.Parse("e0000000-0000-4000-8000-000000000002");
        private static readonly Guid Named = Guid.Parse("e0000000-0000-4000-8000-000000000003");

        private static FakeOrganizationService World(out Guid batchId)
        {
            var service = new FakeOrganizationService();
            batchId = Guid.NewGuid();
            service.Seed("al_exportbatch", batchId, "al_exportbatchcode", "EXB-T", "al_batchstatus", new OptionSetValue(Draft));
            service.Seed("al_reviewroute", AqsRoute, "al_requiresaqsreview", true);
            service.Seed("al_reviewroute", TaxRoute, "al_requiresaqsreview", false);
            service.Seed("contact", Guid.NewGuid(), "emailaddress1", "adviser@example.com", "al_staffcode", "A1", "statecode", new OptionSetValue(0));
            service.Seed("contact", Guid.NewGuid(), "emailaddress1", "pp@example.com", "al_staffcode", "P1", "statecode", new OptionSetValue(0));
            service.Seed("contact", Named, "fullname", "Named Person", "al_staffcode", "N1", "statecode", new OptionSetValue(0));
            return service;
        }

        private static Guid ClosedCase(FakeOrganizationService service, string reference, Guid? route)
        {
            var caseId = Guid.NewGuid();
            var row = service.Seed("al_outcomecase", caseId,
                "al_casereference", reference,
                "al_casestatus", new OptionSetValue(Closed),
                "statecode", new OptionSetValue(0),
                "al_adviseremail", "adviser@example.com",
                "al_paraplanneremail", "pp@example.com");
            if (route.HasValue)
            {
                row["al_reviewrouteid"] = new EntityReference("al_reviewroute", route.Value);
            }

            return caseId;
        }

        private static void Outcome(FakeOrganizationService service, Guid caseId)
        {
            service.Seed("al_outcome", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "al_initialoutcome", new OptionSetValue(OutcomeRules.OutcomeInsufficient),
                GenerateExportPlugin.AqAccountableContactAttr, new EntityReference("contact", Named) { Name = "Named Person" },
                "createdon", DateTime.UtcNow);
        }

        private static Guid Review(FakeOrganizationService service, Guid caseId, int type, int state)
        {
            var reviewId = Guid.NewGuid();
            service.Seed("al_reviewinstance", reviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", caseId),
                "al_reviewtype", new OptionSetValue(type),
                "statecode", new OptionSetValue(state));
            return reviewId;
        }

        private static void Answer(FakeOrganizationService service, Guid reviewId, string questionCode, int choice, string label)
        {
            var questionId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            service.Seed("al_question", questionId, "al_questioncode", questionCode);
            service.Seed("al_questionversion", versionId, "al_questionid", new EntityReference("al_question", questionId));
            var response = service.Seed("al_response", Guid.NewGuid(),
                "al_answerchoice", new OptionSetValue(choice),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", versionId),
                "modifiedon", DateTime.UtcNow);
            response.FormattedValues["al_answerchoice"] = label;
        }

        private static Guid CompleteAqsCase(FakeOrganizationService service, string reference)
        {
            var caseId = ClosedCase(service, reference, AqsRoute);
            Outcome(service, caseId);
            Answer(service, Review(service, caseId, ResponseRules.ReviewTypeAqs, 0), FileQuality.QuestionCode, ResponseRules.ChoicePass, "Pass");
            return caseId;
        }

        private static FakeServiceProvider Generate(FakeOrganizationService service, Guid batchId)
        {
            var provider = new FakeServiceProvider(service);
            provider.Context.MessageName = "al_GenerateExport";
            provider.Context.InputParameters["BatchId"] = batchId.ToString("D");
            provider.Context.InputParameters["IdempotencyKey"] = Guid.NewGuid().ToString("N");
            provider.Context.InputParameters["FileKind"] = GenerateExportPlugin.FileQuarterly;
            new GenerateExportPlugin(null, null).Execute(provider);
            return provider;
        }

        private static int[] CallsFor(int caseCount)
        {
            var service = World(out var batchId);
            for (var i = 0; i < caseCount; i++)
            {
                CompleteAqsCase(service, "C" + i);
            }

            var provider = Generate(service, batchId);
            Assert.Equal(caseCount.ToString(), provider.Context.OutputParameters["RowCount"]);
            return new[] { service.RetrieveCount, service.RetrieveMultipleCount };
        }

        [Fact]
        public void Routes_people_and_named_contacts_are_read_once_per_batch_not_per_case()
        {
            var two = CallsFor(2);
            var five = CallsFor(5);

            // Three more cases sharing one route, one adviser, one para-planner and one named
            // contact: no extra Retrieve at all (the route and the staff code are memoised),
            // and one query per case - the file quality answer. Outcomes are read in chunks and
            // records are created without a lookup (2026-11-17).
            Assert.Equal(two[0], five[0]);
            Assert.Equal(3 * 1, five[1] - two[1]);
        }

        [Fact]
        public void Outcomes_past_one_chunk_are_all_read_and_each_case_keeps_its_newest()
        {
            var service = new FakeOrganizationService();
            var counting = new CountingOrganizationService(service);
            var ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();
            foreach (var id in ids)
            {
                service.Seed("al_outcome", Guid.NewGuid(), "al_outcomecaseid", new EntityReference("al_outcomecase", id),
                    "al_initialoutcome", new OptionSetValue(OutcomeRules.OutcomeInsufficient), "createdon", DateTime.UtcNow.AddDays(-1));
            }

            var newer = service.Seed("al_outcome", Guid.NewGuid(), "al_outcomecaseid", new EntityReference("al_outcomecase", ids[200]),
                "al_initialoutcome", new OptionSetValue(OutcomeRules.OutcomePotentialHarm), "createdon", DateTime.UtcNow);

            var outcomes = GenerateExportPlugin.ResolveOutcomes(counting, ids);

            Assert.Equal(201, outcomes.Count);
            Assert.Equal(newer.Id, outcomes[ids[200]].Id);
            Assert.Equal(2, counting.QueriesOf("al_outcome"));
        }

        [Fact]
        public void A_cached_matcher_asks_contact_once_per_address()
        {
            var inner = World(out _);
            var counting = new CountingOrganizationService(inner);
            var match = CasePeople.CachedMatcher(counting, "adviser");

            for (var i = 0; i < 5; i++)
            {
                Assert.Equal("A1", match("adviser@example.com").StaffCode);
                Assert.Equal("A1", match(" ADVISER@example.com ").StaffCode);
            }

            Assert.Equal(1, counting.QueriesOf("contact"));
        }

        [Fact]
        public void An_incomplete_closed_case_is_left_out_and_named_rather_than_failing_the_batch()
        {
            var service = World(out var batchId);
            var good = CompleteAqsCase(service, "GOOD-1");
            ClosedCase(service, "NO-OUTCOME-2", AqsRoute); // closed with no outcome at all

            var provider = Generate(service, batchId);

            Assert.Equal("1", provider.Context.OutputParameters["RowCount"]);
            var record = Assert.Single(service.All("al_exportrecord"));
            Assert.Equal(good, record.GetAttributeValue<EntityReference>("al_outcomecaseid").Id);

            var audit = Assert.Single(service.All("al_auditevent"));
            var reason = audit.GetAttributeValue<string>("al_reason");
            Assert.Contains("1 closed case(s) left out", reason);
            Assert.Contains("NO-OUTCOME-2", reason);
            Assert.Contains("no outcome recorded", reason);
            Assert.DoesNotContain("reopen", reason);
            Assert.Equal("1", audit.GetAttributeValue<string>("al_details"));
        }

        [Fact]
        public void A_routeless_case_whose_aqs_check_is_no_longer_active_is_left_out_not_exported_blank()
        {
            var service = World(out var batchId);
            var superseded = ClosedCase(service, "OLD-3", null);
            Outcome(service, superseded);
            Review(service, superseded, ResponseRules.ReviewTypeAqs, 1);
            var taxOnly = ClosedCase(service, "TAX-4", null); // no route, never had an AQS check
            Answer(service, Review(service, taxOnly, ResponseRules.ReviewTypeTax, 0), FileQuality.TaxQuestionCode, ResponseRules.ChoicePass, "Pass");

            var provider = Generate(service, batchId);

            Assert.Equal("1", provider.Context.OutputParameters["RowCount"]);
            Assert.Equal(taxOnly, Assert.Single(service.All("al_exportrecord")).GetAttributeValue<EntityReference>("al_outcomecaseid").Id);
            Assert.Contains("OLD-3", Assert.Single(service.All("al_auditevent")).GetAttributeValue<string>("al_reason"));
        }

        [Fact]
        public void A_tax_only_case_takes_its_file_quality_from_the_tax_review_not_an_abandoned_aqs_one()
        {
            var service = World(out var batchId);
            var caseId = ClosedCase(service, "TAX-5", TaxRoute);
            Answer(service, Review(service, caseId, ResponseRules.ReviewTypeAqs, 0), FileQuality.QuestionCode, ResponseRules.ChoiceFail, "Fail");
            Answer(service, Review(service, caseId, ResponseRules.ReviewTypeTax, 0), FileQuality.TaxQuestionCode, ResponseRules.ChoicePass, "Pass");

            Assert.Equal("Pass", FileQuality.Label(FileQuality.Resolve(service, caseId)));
            Assert.False(FileQuality.FailedOn(service, caseId));

            Generate(service, batchId);

            var record = Assert.Single(service.All("al_exportrecord"));
            Assert.Equal("Pass", record.GetAttributeValue<string>("al_filequalitygrade"));
            Assert.Equal(string.Empty, record.GetAttributeValue<string>("al_fqfailparaplannername"));
        }

        [Fact]
        public void A_tax_only_checkers_choice_of_who_carries_the_fail_reaches_the_file()
        {
            var service = World(out var batchId);
            var caseId = ClosedCase(service, "TAX-7", TaxRoute);
            service.Row("al_outcomecase", caseId)["al_advisername"] = "Case Adviser";
            service.Row("al_outcomecase", caseId)["al_paraplanner"] = "Case Paraplanner";
            var tax = Review(service, caseId, ResponseRules.ReviewTypeTax, 0);
            Answer(service, tax, FileQuality.TaxQuestionCode, ResponseRules.ChoiceFail, "Fail");

            // Parked on the Tax review: the adviser, and a named person for File Quality.
            service.Row("al_reviewinstance", tax)["al_pendingaccountability"] =
                "{\"fqAdviser\":true,\"fqParaplanner\":false,\"aqAdviser\":false,\"aqParaplanner\":false,"
                + "\"fqContactId\":\"" + Named.ToString("D") + "\",\"aqContactId\":\"\"}";

            Generate(service, batchId);

            var record = Assert.Single(service.All("al_exportrecord"));
            // Without the choice the default is the para-planner, a different person.
            Assert.Equal("Named Person", record.GetAttributeValue<string>("al_fqfailadvisername"));
            Assert.Equal("N1", record.GetAttributeValue<string>("al_fqfailadvisercode"));
            Assert.Equal(string.Empty, record.GetAttributeValue<string>("al_fqfailparaplannername"));
        }

        [Fact]
        public void A_tax_only_case_with_no_choice_still_derives_the_paraplanner()
        {
            var service = World(out var batchId);
            var caseId = ClosedCase(service, "TAX-8", TaxRoute);
            service.Row("al_outcomecase", caseId)["al_paraplanner"] = "Case Paraplanner";
            Answer(service, Review(service, caseId, ResponseRules.ReviewTypeTax, 0), FileQuality.TaxQuestionCode, ResponseRules.ChoiceFail, "Fail");

            Generate(service, batchId);

            var record = Assert.Single(service.All("al_exportrecord"));
            Assert.Equal("Case Paraplanner", record.GetAttributeValue<string>("al_fqfailparaplannername"));
            Assert.Equal(string.Empty, record.GetAttributeValue<string>("al_fqfailadvisername"));
        }

        [Fact]
        public void A_case_whose_route_owes_aqs_still_prefers_the_aqs_answer()
        {
            var service = World(out _);
            var caseId = ClosedCase(service, "AQS-6", AqsRoute);
            Answer(service, Review(service, caseId, ResponseRules.ReviewTypeAqs, 0), FileQuality.QuestionCode, ResponseRules.ChoiceFail, "Fail");
            Answer(service, Review(service, caseId, ResponseRules.ReviewTypeTax, 0), FileQuality.TaxQuestionCode, ResponseRules.ChoicePass, "Pass");

            Assert.Equal("Fail", FileQuality.Label(FileQuality.Resolve(service, caseId)));
        }

        [Fact]
        public void Nothing_is_left_out_when_every_case_is_complete()
        {
            Assert.Null(GenerateExportPlugin.LeftOutReason(new string[0]));
            Assert.True(GenerateExportPlugin.LeftOutReason(Enumerable.Repeat(new string('x', 500), 10).ToList()).Length <= 2000);
        }
    }
}
