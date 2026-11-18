using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The owner's decision of 2026-11-17: the same approval through al_SignOffRemediation
    /// and through the portal ends in the same place. The portal carries the supervisor's
    /// final outcome and the two recheck answers with the sign-off, so an approval can close
    /// the case; the command used to carry neither, so the same approval parked the case at
    /// Awaiting Recheck. Omitting the answers keeps that older behaviour, which is the
    /// portal's own "leave for a separate regrade" choice.
    /// </summary>
    public class SignOffRemediationAnswersFixTests
    {
        private static readonly Guid ContactId = Guid.Parse("dddddddd-7777-4777-8777-777777777777");
        private static readonly Guid ActionId = Guid.Parse("aaaaaaaa-7777-4777-8777-777777777777");
        private static readonly Guid SiblingId = Guid.Parse("bbbbbbbb-7777-4777-8777-777777777777");
        private static readonly Guid OtherLegId = Guid.Parse("eeeeeeee-7777-4777-8777-777777777777");
        private static readonly Guid CaseId = Guid.Parse("cccccccc-7777-4777-8777-777777777777");
        private static readonly Guid ReviewId = Guid.Parse("99999999-7777-4777-8777-777777777777");
        private const int StatusCompleted = 120910602;
        private const int ChangesAdviceNo = 120910799;

        private static FakeOrganizationService Check()
        {
            var svc = new FakeOrganizationService();
            foreach (var id in new[] { ActionId, SiblingId })
            {
                svc.Seed("al_remediationaction", id,
                    "al_actionstatus", new OptionSetValue(StatusCompleted),
                    "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                    "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                    "statecode", new OptionSetValue(0));
            }

            svc.Seed("al_remediationaction", OtherLegId,
                "al_actionstatus", new OptionSetValue(StatusCompleted),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", Guid.NewGuid()),
                "statecode", new OptionSetValue(0));
            return svc;
        }

        /// <summary>The same check, with what the portal path also needs: the request row, the role and the mapping.</summary>
        private static FakeOrganizationService PortalCheck()
        {
            var svc = Check();
            svc.Seed("contact", ContactId, SignoffRequestPlugin.RequestAttr, "{}");
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-7",
                TcManagerRouting.CaseAdviserEmailAttr, "adviser@example.com");
            svc.Seed(TcManagerRouting.MappingEntity, Guid.NewGuid(),
                TcManagerRouting.MappingEmailAttr, "adviser@example.com",
                TcManagerRouting.ManagerAttr, new EntityReference("contact", ContactId),
                "statecode", new OptionSetValue(0));
            var role = new Entity("contact", ContactId);
            role["role.name"] = new AliasedValue("powerpagecomponent", "name", WebRoleRegistry.TcSupervisorRole);
            svc.FetchResults.Enqueue(new EntityCollection(new List<Entity> { role }));
            return svc;
        }

        private static SignoffRequestPayload Answers()
        {
            return new SignoffRequestPayload
            {
                FinalOutcome = OutcomeRules.FinalOutcomePass,
                RecheckRequired = Remediation.RecheckRequiredNo,
                ChangesAdvice = ChangesAdviceNo,
            };
        }

        private static List<Entity> ActionWrites(FakeOrganizationService svc)
        {
            return svc.Updates.FindAll(u => u.LogicalName == "al_remediationaction");
        }

        private static string Describe(FakeOrganizationService svc)
        {
            var writes = ActionWrites(svc)
                .OrderBy(w => w.Id)
                .Select(w => w.Id.ToString("N") + ":"
                    + w.GetAttributeValue<OptionSetValue>("al_recheckrequired")?.Value + "/"
                    + w.GetAttributeValue<OptionSetValue>("al_changesadvice")?.Value);
            var signoff = svc.Creates.Single(c => c.LogicalName == "al_signoff");
            return string.Join(",", writes) + " | outcome "
                + signoff.GetAttributeValue<OptionSetValue>("al_finaloutcome")?.Value;
        }

        [Fact]
        public void An_approval_with_the_answers_records_what_the_portal_records()
        {
            var api = Check();
            SignOffRemediationPlugin.CreateSignoff(api, api, ActionId, "Approved", "Graded on the evidence.", null, Answers());

            var portal = PortalCheck();
            SignoffRequestPlugin.Apply(portal, ContactId,
                "{\"actionId\":\"" + ActionId.ToString("D") + "\",\"decision\":120910720"
                + ",\"notes\":\"Graded on the evidence.\""
                + ",\"finalOutcome\":" + OutcomeRules.FinalOutcomePass
                + ",\"recheckRequired\":" + Remediation.RecheckRequiredNo
                + ",\"changesAdvice\":" + ChangesAdviceNo + "}");

            Assert.Equal(Describe(portal), Describe(api));
            // The whole check, not the other leg, and the grade on the sign-off.
            Assert.Equal(2, ActionWrites(api).Count);
            Assert.DoesNotContain(ActionWrites(api), w => w.Id == OtherLegId);
            Assert.Equal(OutcomeRules.FinalOutcomePass,
                api.Creates.Single().GetAttributeValue<OptionSetValue>("al_finaloutcome").Value);
        }

        [Fact]
        public void The_answers_are_written_before_the_sign_off_is_created()
        {
            // SignoffProgressPlugin reads al_recheckrequired on the al_signoff Create to
            // decide whether the case closes, so the answers must already be there.
            var svc = Check();
            var log = new Logging(svc);
            SignOffRemediationPlugin.CreateSignoff(log, log, ActionId, "Approved", "Graded.", null, Answers());

            Assert.Equal(new[] { "Update al_remediationaction", "Update al_remediationaction", "Create al_signoff" }, log.Writes);
        }

        [Fact]
        public void Omitting_the_answers_keeps_the_separate_regrade_behaviour()
        {
            var svc = Check();
            SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Approved", null, null, null);
            SignOffRemediationPlugin.CreateSignoff(svc, svc, SiblingId, "Approved", null, null, new SignoffRequestPayload());

            Assert.Empty(svc.Updates);
            Assert.All(svc.Creates, c => Assert.False(c.Contains("al_finaloutcome")));
        }

        [Fact]
        public void A_final_outcome_on_a_rejection_is_refused_before_anything_is_written()
        {
            var svc = Check();
            var answers = Answers();

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Rejected", "Missing evidence.", null, answers));

            Assert.Contains("cannot record a final outcome", error.Message);
            Assert.Empty(svc.Updates);
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void A_final_outcome_without_notes_is_refused_in_plain_words()
        {
            var svc = Check();

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Approved", null, null, Answers()));

            Assert.Contains("Recording a final outcome must say why the grade was set.", error.Message);
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void A_value_that_is_not_a_final_outcome_is_refused()
        {
            var svc = Check();
            var answers = Answers();
            answers.FinalOutcome = OutcomeRules.OutcomePass; // the initial scale, not the final one

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Approved", "Graded.", null, answers));

            Assert.Contains("An approved sign-off's final outcome must be Pass or Potential harm.", error.Message);
            Assert.DoesNotContain("AD-", error.Message);
            Assert.DoesNotContain("BR-", error.Message);
        }

        [Fact]
        public void The_contract_declares_the_three_answers_as_optional_whole_numbers()
        {
            var json = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "plugins", "customapi", "al_SignOffRemediation.customapi.json"));
            foreach (var name in new[] { "FinalOutcome", "RecheckRequired", "ChangesAdvice" })
            {
                Assert.Matches("\"uniquename\":\\s*\"" + name + "\"[^}]*\"type\":\\s*7,\\s*\"isoptional\":\\s*true", json);
                var xml = System.IO.Path.Combine(RepoRoot(), "src", "customapis", "al_SignOffRemediation",
                    "customapirequestparameters", name, "customapirequestparameter.xml");
                var text = System.IO.File.ReadAllText(xml);
                Assert.Contains("uniquename=\"" + name + "\"", text);
                Assert.Contains("<type>7</type>", text);
                Assert.Contains("<isoptional>1</isoptional>", text);
            }
        }

        /// <summary>Delegates to the fake and records every write in the order it was issued.</summary>
        private sealed class Logging : IOrganizationService
        {
            private readonly IOrganizationService _inner;

            public Logging(IOrganizationService inner) { _inner = inner; }

            public List<string> Writes { get; } = new List<string>();

            public Guid Create(Entity entity) { Writes.Add("Create " + entity.LogicalName); return _inner.Create(entity); }
            public void Update(Entity entity) { Writes.Add("Update " + entity.LogicalName); _inner.Update(entity); }
            public Entity Retrieve(string entityName, Guid id, Microsoft.Xrm.Sdk.Query.ColumnSet columnSet) { return _inner.Retrieve(entityName, id, columnSet); }
            public EntityCollection RetrieveMultiple(Microsoft.Xrm.Sdk.Query.QueryBase query) { return _inner.RetrieveMultiple(query); }
            public void Delete(string entityName, Guid id) { Writes.Add("Delete " + entityName); _inner.Delete(entityName, id); }
            public OrganizationResponse Execute(OrganizationRequest request) { Writes.Add("Execute " + request.RequestName); return _inner.Execute(request); }
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { _inner.Associate(entityName, entityId, relationship, relatedEntities); }
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { _inner.Disassociate(entityName, entityId, relationship, relatedEntities); }
        }

        private static string RepoRoot()
        {
            var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !System.IO.Directory.Exists(System.IO.Path.Combine(directory.FullName, "plugins", "customapi")))
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new System.IO.DirectoryNotFoundException("plugins/customapi was not found above " + AppContext.BaseDirectory);
            }

            return directory.FullName;
        }
    }
}
