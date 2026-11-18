using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Regression tests for the 2026-11-16 audit of the case and remediation commands: the
    /// response history that filled up, the checker's client-contact answer, the Tax-field
    /// lock on the Code App, the review a removed or requeued case kept, the reopened action
    /// left with the old adviser, the last two completions, the close decided over two
    /// scopes, and the replay answered before the permission check.
    /// </summary>
    public class CaseRemediationAuditFixTests
    {
        private static readonly Guid CaseId = Guid.Parse("c0c0c0c0-1111-4111-8111-111111111111");
        private static readonly Guid ActionId = Guid.Parse("c0c0c0c0-2222-4222-8222-222222222222");
        private static readonly Guid OtherActionId = Guid.Parse("c0c0c0c0-3333-4333-8333-333333333333");
        private static readonly Guid ReviewId = Guid.Parse("c0c0c0c0-4444-4444-8444-444444444444");
        private static readonly Guid OtherReviewId = Guid.Parse("c0c0c0c0-5555-4555-8555-555555555555");
        private static readonly Guid CheckerId = Guid.Parse("c0c0c0c0-6666-4666-8666-666666666666");
        private static readonly Guid TaxAqsRouteId = Guid.Parse("c0c0c0c0-7777-4777-8777-777777777777");
        private static readonly Guid AqsRouteId = Guid.Parse("c0c0c0c0-8888-4888-8888-888888888888");
        private static readonly Guid TaxRouteId = Guid.Parse("c0c0c0c0-9999-4999-8999-999999999999");

        // --- 1. The response history once the column is full --------------------------------

        private static Entity Stored(string history)
        {
            var stored = new Entity("al_remediationaction", ActionId);
            stored["al_actionstatus"] = new OptionSetValue(Remediation.StatusCompleted);
            stored[RemedialActions.ResponseHistoryAttr] = history;
            return stored;
        }

        private static Entity Reopen(string history)
        {
            var update = new Entity("al_remediationaction", ActionId);
            update["al_actionstatus"] = new OptionSetValue(Remediation.StatusInProgress);
            update[RemedialActions.ResponseHistoryAttr] = history;
            return update;
        }

        [Fact]
        public void Every_rejection_keeps_passing_the_history_guard_after_the_column_fills()
        {
            var history = string.Empty;
            for (var round = 1; round <= 15; round++)
            {
                var next = RemedialActions.WithEarlierResponse(
                    history, DateTime.UtcNow, RemedialActions.ActionPerformedYes, new string('x', 1990) + " round " + round);

                var refusal = RemediationResponseGuardPlugin.HistoryRefusal(Reopen(next), Stored(history), true);
                Assert.True(refusal == null,
                    "rejection round " + round + " refused (history " + history.Length + " -> " + next.Length + " chars): " + refusal);

                // The newest answer is the one on the end.
                Assert.EndsWith(" round " + round, next);
                history = next;
            }

            Assert.Equal(RemedialActions.ResponseHistoryLength, history.Length);
        }

        [Fact]
        public void A_full_history_that_drops_more_than_one_answer_is_still_refused()
        {
            var stored = new string('a', 10000) + new string('b', 9500);
            var forged = stored.Substring(9000) + new string('z', RemedialActions.ResponseHistoryLength - (stored.Length - 9000));

            Assert.Equal(RemedialActions.ResponseHistoryLength, forged.Length);
            Assert.StartsWith(CommandHelpers.PreconditionPrefix,
                RemediationResponseGuardPlugin.HistoryRefusal(Reopen(forged), Stored(stored), true));
        }

        [Fact]
        public void A_full_history_that_rewrites_what_it_kept_is_still_refused()
        {
            var stored = new string('a', 19500);
            var trimmed = RemedialActions.WithEarlierResponse(stored, DateTime.UtcNow, null, new string('n', 1500));
            var rewritten = "Q" + trimmed.Substring(1);

            Assert.Null(RemediationResponseGuardPlugin.HistoryRefusal(Reopen(trimmed), Stored(stored), true));
            Assert.StartsWith(CommandHelpers.PreconditionPrefix,
                RemediationResponseGuardPlugin.HistoryRefusal(Reopen(rewritten), Stored(stored), true));
        }

        // --- 2. "Client contact required?" is the checker's ----------------------------------

        [Fact]
        public void The_adviser_cannot_write_the_checkers_client_contact_answer()
        {
            var update = new Entity("al_remediationaction", ActionId)
            {
                ["al_adviserresponse"] = "Called the client.",
                ["al_clientcontactrequired"] = new OptionSetValue(120910794),
            };

            var refusal = RemediationResponseGuardPlugin.WriteOnceRefusal(update);

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal);
            Assert.Contains("Client contact required?", refusal);
        }

        [Fact]
        public void The_guard_refuses_the_adviser_writing_it_while_the_action_is_still_open()
        {
            // Through the step itself: an open action is where the freeze lets an adviser
            // answer, and this answer is not theirs at any status.
            var svc = new FakeOrganizationService();
            svc.Seed("al_remediationaction", ActionId, "al_actionstatus", new OptionSetValue(Remediation.StatusInProgress));
            var provider = new FakeServiceProvider(svc);
            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityName = "al_remediationaction";
            provider.Context.InputParameters["Target"] = new Entity("al_remediationaction", ActionId)
            {
                ["al_clientcontactrequired"] = new OptionSetValue(120910795),
            };

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => new RemediationResponseGuardPlugin(null, null).Execute(provider));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal.Message);
        }

        [Fact]
        public void The_advisers_own_answer_still_passes_the_write_once_rule()
        {
            var update = new Entity("al_remediationaction", ActionId)
            {
                ["al_adviserresponse"] = "Called the client.",
                ["al_evidencereference"] = "IO-1",
            };

            Assert.Null(RemediationResponseGuardPlugin.WriteOnceRefusal(update));
        }

        // --- 3. The Tax fields after the Tax submit, and a queued case owing nothing ---------

        private static FakeOrganizationService TaxSubmitted()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId,
                "al_casestatus", new OptionSetValue(CaseLifecycle.Queued),
                "al_taxcheckrequired", new OptionSetValue(120910560),
                "al_reviewrouteid", new EntityReference("al_reviewroute", TaxAqsRouteId));
            svc.Seed("al_reviewroute", TaxAqsRouteId, "al_requirestaxreview", true, "al_requiresaqsreview", true);
            svc.Seed("al_reviewinstance", ReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusSubmitted),
                "al_submittedon", new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(0));
            return svc;
        }

        private static FakeServiceProvider UpdateCase(FakeOrganizationService svc, string fieldsJson, string key = null)
        {
            var provider = new FakeServiceProvider(svc);
            provider.Context.InputParameters["TargetId"] = CaseId.ToString("D");
            provider.Context.InputParameters["IdempotencyKey"] = key ?? "key-" + Guid.NewGuid().ToString("N");
            provider.Context.InputParameters["Fields"] = fieldsJson;
            return provider;
        }

        [Fact]
        public void The_code_app_cannot_change_a_tax_field_once_the_tax_check_is_submitted()
        {
            var svc = TaxSubmitted();

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => new UpdateCaseDetailsPlugin(null, null).Execute(
                    UpdateCase(svc, "{\"al_taxcheckrequired\":\"120910561\"}")));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal.Message);
            Assert.Contains("Tax check on this case has been submitted", refusal.Message);
            Assert.Equal(120910560, svc.Row("al_outcomecase", CaseId).GetAttributeValue<OptionSetValue>("al_taxcheckrequired").Value);
        }

        [Fact]
        public void Restating_a_tax_field_is_not_a_change()
        {
            var before = new Entity("al_outcomecase", CaseId) { ["al_taxcheckrequired"] = new OptionSetValue(120910560) };

            Assert.False(UpdateCaseDetailsPlugin.ChangesTaxFields(
                before, new Entity("al_outcomecase", CaseId) { ["al_taxcheckrequired"] = new OptionSetValue(120910560) }));
            Assert.True(UpdateCaseDetailsPlugin.ChangesTaxFields(
                before, new Entity("al_outcomecase", CaseId) { ["al_taxteamdisposition"] = new OptionSetValue(120910571) }));
            Assert.False(UpdateCaseDetailsPlugin.ChangesTaxFields(
                before, new Entity("al_outcomecase", CaseId) { ["al_clientname"] = "A. Client" }));
        }

        [Fact]
        public void A_route_change_that_leaves_a_queued_case_owing_nothing_is_refused()
        {
            // Tax submitted, case queued for AQS; rerouted to Tax only, it would owe nothing
            // and could be neither claimed nor moved.
            var svc = TaxSubmitted();
            svc.Seed("al_reviewroute", TaxRouteId, "al_requirestaxreview", true, "al_requiresaqsreview", false);

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => UpdateCaseDetailsPlugin.RequeueAfterRouteChange(
                    svc,
                    svc.Row("al_outcomecase", CaseId),
                    new Entity("al_outcomecase", CaseId) { ["al_reviewrouteid"] = new EntityReference("al_reviewroute", TaxRouteId) },
                    new List<string>()));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal.Message);
        }

        [Fact]
        public void A_route_change_at_queued_that_still_owes_a_check_is_left_alone()
        {
            var svc = TaxSubmitted();
            svc.Seed("al_reviewroute", AqsRouteId, "al_requirestaxreview", false, "al_requiresaqsreview", true);

            Assert.False(UpdateCaseDetailsPlugin.RequeueAfterRouteChange(
                svc,
                svc.Row("al_outcomecase", CaseId),
                new Entity("al_outcomecase", CaseId) { ["al_reviewrouteid"] = new EntityReference("al_reviewroute", AqsRouteId) },
                new List<string>()));
            Assert.Equal(CaseLifecycle.Queued, svc.Row("al_outcomecase", CaseId).GetAttributeValue<OptionSetValue>("al_casestatus").Value);
        }

        // --- 4. The old checker loses the review with the case ---------------------------------

        private static Entity OpenReview(FakeOrganizationService svc, Guid id, int type)
        {
            return svc.Seed("al_reviewinstance", id,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(type),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusAssigned),
                "al_assignedcontactid", new EntityReference("contact", CheckerId),
                "statecode", new OptionSetValue(0));
        }

        private static EntityReference Holder(FakeOrganizationService svc, Guid reviewId)
        {
            return svc.Row("al_reviewinstance", reviewId).GetAttributeValue<EntityReference>("al_assignedcontactid");
        }

        [Fact]
        public void Removing_a_case_takes_the_checker_off_its_unsubmitted_review()
        {
            var svc = new FakeOrganizationService();
            OpenReview(svc, ReviewId, ResponseRules.ReviewTypeAqs);
            svc.Seed("al_reviewinstance", OtherReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_assignedcontactid", new EntityReference("contact", CheckerId),
                "al_submittedon", new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(0));

            UpdateCaseDetailsPlugin.ReleaseIfRemoved(
                svc, CaseId,
                new Entity("al_outcomecase", CaseId) { ["al_casestatus"] = new OptionSetValue(CaseLifecycle.NoCheckRequired) },
                new List<string>());

            Assert.Null(Holder(svc, ReviewId));
            Assert.Equal(CheckerId, Holder(svc, OtherReviewId).Id); // who did the submitted work stays
        }

        [Fact]
        public void Requeueing_a_case_takes_the_checker_off_its_unsubmitted_review()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewroute", TaxAqsRouteId, "al_requirestaxreview", true, "al_requiresaqsreview", true);
            svc.Seed("al_reviewroute", AqsRouteId, "al_requirestaxreview", false, "al_requiresaqsreview", true);
            svc.Seed("al_outcomecase", CaseId,
                "al_casestatus", new OptionSetValue(CaseLifecycle.Assigned),
                "al_reviewrouteid", new EntityReference("al_reviewroute", AqsRouteId));
            OpenReview(svc, ReviewId, ResponseRules.ReviewTypeAqs);

            var moved = UpdateCaseDetailsPlugin.RequeueAfterRouteChange(
                svc,
                svc.Row("al_outcomecase", CaseId),
                new Entity("al_outcomecase", CaseId) { ["al_reviewrouteid"] = new EntityReference("al_reviewroute", TaxAqsRouteId) },
                new List<string>());

            Assert.True(moved);
            Assert.Null(Holder(svc, ReviewId));
        }

        // --- 5. A reopened action goes to the adviser the case names now -----------------------

        [Fact]
        public void A_rejection_reopens_the_action_for_the_cases_current_adviser()
        {
            var oldAdviser = Guid.NewGuid();
            var newAdviser = Guid.NewGuid();
            var svc = new FakeOrganizationService();
            svc.Seed("contact", oldAdviser, "emailaddress1", "old@example.com", "statecode", new OptionSetValue(0));
            svc.Seed("contact", newAdviser, "emailaddress1", "new@example.com", "statecode", new OptionSetValue(0));
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "AUD-1",
                CaseAdviser.EmailAttr, "new@example.com",
                "al_casestatus", new OptionSetValue(CaseLifecycle.AwaitingSignoff));
            svc.Seed("al_remediationaction", ActionId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                "al_adviserresponse", "Done.",
                "al_assignedcontactid", new EntityReference("contact", oldAdviser),
                "statecode", new OptionSetValue(0));

            var signoff = new Entity("al_signoff")
            {
                Id = Guid.NewGuid(),
                ["al_remediationactionid"] = new EntityReference("al_remediationaction", ActionId),
                ["al_outcomecaseid"] = new EntityReference("al_outcomecase", CaseId),
                ["al_signoffdecision"] = new OptionSetValue(SignoffProgressPlugin.DecisionRejectedValue),
                ["al_notes"] = "Add the declaration.",
            };
            svc.Seed("al_signoff", signoff.Id,
                "al_remediationactionid", signoff["al_remediationactionid"],
                "al_outcomecaseid", signoff["al_outcomecaseid"],
                "al_signoffdecision", signoff["al_signoffdecision"],
                "al_notes", "Add the declaration.",
                "createdon", DateTime.UtcNow,
                "statecode", new OptionSetValue(0));

            SignoffProgressPlugin.Progress(svc, new FakePluginExecutionContext(), signoff);

            var action = svc.Row("al_remediationaction", ActionId);
            Assert.Equal(Remediation.StatusInProgress, action.GetAttributeValue<OptionSetValue>("al_actionstatus").Value);
            Assert.Equal(newAdviser, action.GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        // --- 6. The last two completions ------------------------------------------------------

        /// <summary>Records the order of the case lock and the reads of the actions.</summary>
        private sealed class OrderRecorder : IOrganizationService
        {
            private readonly FakeOrganizationService _inner;

            public OrderRecorder(FakeOrganizationService inner)
            {
                _inner = inner;
            }

            public List<string> Log { get; } = new List<string>();

            public Guid Create(Entity entity) => _inner.Create(entity);

            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => _inner.Retrieve(entityName, id, columnSet);

            public void Update(Entity entity)
            {
                if (entity.LogicalName == "al_outcomecase")
                {
                    Log.Add("lock");
                }

                _inner.Update(entity);
            }

            public void Delete(string entityName, Guid id) => _inner.Delete(entityName, id);

            public OrganizationResponse Execute(OrganizationRequest request) => _inner.Execute(request);

            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
                => _inner.Associate(entityName, entityId, relationship, relatedEntities);

            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
                => _inner.Disassociate(entityName, entityId, relationship, relatedEntities);

            public EntityCollection RetrieveMultiple(QueryBase query)
            {
                var expression = query as QueryExpression;
                if (expression != null && expression.EntityName == "al_remediationaction")
                {
                    Log.Add("read actions");
                }

                return _inner.RetrieveMultiple(query);
            }
        }

        private static FakeOrganizationService TwoLegs()
        {
            var svc = new FakeOrganizationService();

            // Graded Pass on Tax with no AQS check owed: nothing needs the T&C Manager.
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "AUD-2",
                "al_taxoutcome", new OptionSetValue(ResponseRules.ChoicePass),
                "al_casestatus", new OptionSetValue(CaseLifecycle.AwaitingRemediation));
            foreach (var pair in new[] { new[] { ActionId, ReviewId }, new[] { OtherActionId, OtherReviewId } })
            {
                svc.Seed("al_remediationaction", pair[0],
                    "al_actionstatus", new OptionSetValue(Remediation.StatusOpen),
                    "al_adviserresponse", "Done.",
                    "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                    "al_reviewinstanceid", new EntityReference("al_reviewinstance", pair[1]),
                    "statecode", new OptionSetValue(0));
            }

            return svc;
        }

        private static void Complete(IOrganizationService svc, Guid actionId)
        {
            CompleteRemediationPlugin.Complete(
                svc, actionId, "key-" + Guid.NewGuid().ToString("N"), null, Guid.NewGuid(), Guid.NewGuid(),
                requireCallerOwnsAction: false, details: null);
        }

        private static int CaseStatus(FakeOrganizationService svc)
        {
            return svc.Row("al_outcomecase", CaseId).GetAttributeValue<OptionSetValue>("al_casestatus").Value;
        }

        [Fact]
        public void A_completion_locks_the_case_before_it_reads_what_is_outstanding()
        {
            var recorder = new OrderRecorder(TwoLegs());

            Complete(recorder, ActionId);

            var locked = recorder.Log.IndexOf("lock");
            Assert.True(locked >= 0, "no case lock: " + string.Join(", ", recorder.Log));
            Assert.True(recorder.Log.IndexOf("read actions", locked) > locked, string.Join(", ", recorder.Log));
            Assert.DoesNotContain("read actions", recorder.Log.GetRange(0, locked));
        }

        // --- 7. "May close" is decided over the whole case -----------------------------------

        [Fact]
        public void A_check_finished_while_the_other_legs_action_is_open_does_not_close_the_case()
        {
            var svc = TwoLegs();

            Complete(svc, ActionId);

            Assert.Equal(CaseLifecycle.RemediationInProgress, CaseStatus(svc));
            Assert.False(CompleteRemediationPlugin.MayCloseWithoutSignoff(svc, CaseId));
        }

        [Fact]
        public void The_last_action_on_the_case_closes_it()
        {
            var svc = TwoLegs();

            Complete(svc, ActionId);
            Complete(svc, OtherActionId);

            Assert.Equal(CaseLifecycle.Closed, CaseStatus(svc));
        }

        // --- 8. Permission before the replay ---------------------------------------------------

        private static FakeServiceProvider Unauthorised(string key, int command, out FakeOrganizationService svc)
        {
            svc = new FakeOrganizationService();
            var provider = new FakeServiceProvider(svc);
            svc.Seed("systemuser", provider.Context.InitiatingUserId, "internalemailaddress", "nobody@example.com");
            svc.Seed("al_userrolemapping", Guid.NewGuid(), "al_email", "someone@example.com");
            svc.Seed(CommandHelpers.AuditEntity, Guid.NewGuid(), "al_idempotencykey", key, "al_command", new OptionSetValue(command));
            provider.Context.InputParameters["TargetId"] = CaseId.ToString("D");
            provider.Context.InputParameters["IdempotencyKey"] = key;
            return provider;
        }

        [Fact]
        public void Update_case_details_refuses_a_caller_without_permission_even_on_a_replay()
        {
            FakeOrganizationService svc;
            var provider = Unauthorised("replayed", UpdateCaseDetailsPlugin.CommandUpdateCaseDetails, out svc);
            provider.Context.InputParameters["Fields"] = "{\"al_clientname\":\"A\"}";

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => new UpdateCaseDetailsPlugin(null, null).Execute(provider));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, refusal.Message);
            Assert.False(provider.Context.OutputParameters.Contains("AuditEventId"));
        }

        [Fact]
        public void Assign_case_refuses_a_caller_without_permission_even_on_a_replay()
        {
            FakeOrganizationService svc;
            var provider = Unauthorised("replayed", 120910751, out svc);
            provider.Context.InputParameters["AssigneeEmail"] = "checker@example.com";

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => new AssignCasePlugin(null, null).Execute(provider));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, refusal.Message);
            Assert.False(provider.Context.OutputParameters.Contains("AuditEventId"));
        }
    }
}
