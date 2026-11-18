using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The guard gaps the 2026-11-17 audit proved by probe: each test asserts the corrected rule.
    /// </summary>
    public class GuardGapsAuditFixTests
    {
        private const string Email = "someone@example.com";
        private static readonly Guid ActionId = Guid.Parse("aaaaaaaa-7777-4777-8777-000000000001");
        private static readonly Guid CaseId = Guid.Parse("aaaaaaaa-7777-4777-8777-000000000002");
        private static readonly Guid OutcomeId = Guid.Parse("aaaaaaaa-7777-4777-8777-000000000003");
        private static readonly Guid ReviewId = Guid.Parse("aaaaaaaa-7777-4777-8777-000000000004");

        private static void Person(FakeOrganizationService svc, Guid userId, string roleCode, params string[] resources)
        {
            svc.Seed("systemuser", userId, "internalemailaddress", Email);
            svc.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", Email, "al_rolecode", roleCode, "statecode", new OptionSetValue(0));
            foreach (var resource in resources)
            {
                svc.Seed("al_pagepermission", Guid.NewGuid(),
                    "al_resourcekey", resource, "al_rolecode", roleCode,
                    "al_accesslevel", new OptionSetValue(PermissionHelpers.AccessEdit),
                    "statecode", new OptionSetValue(0));
            }
        }

        private static FakeServiceProvider Provider(FakeOrganizationService svc, string message, string entity, Entity target)
        {
            var provider = new FakeServiceProvider(svc);
            provider.Context.MessageName = message;
            provider.Context.PrimaryEntityName = entity;
            provider.Context.PrimaryEntityId = target.Id;
            provider.Context.InputParameters["Target"] = target;
            return provider;
        }

        // ---- the T&C-only answers ------------------------------------------------------------

        private static void WriteRecheck(FakeOrganizationService svc, bool asPortal)
        {
            svc.Seed("al_remediationaction", ActionId,
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            var provider = Provider(svc, "Update", "al_remediationaction", new Entity("al_remediationaction", ActionId)
            {
                ["al_recheckrequired"] = new OptionSetValue(Remediation.RecheckRequiredNo),
            });
            if (asPortal)
            {
                svc.Seed("systemuser", provider.Context.InitiatingUserId, "applicationid", Guid.NewGuid());
            }

            new RemediationResponseGuardPlugin(null, null).Execute(provider);
        }

        [Fact]
        public void A_stray_signoff_request_on_some_contact_does_not_open_the_TC_only_answers_to_an_app_user()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", Guid.NewGuid(), SignoffRequestPlugin.RequestAttr, "{}");

            Assert.Throws<InvalidPluginExecutionException>(() => WriteRecheck(svc, asPortal: false));
        }

        [Fact]
        public void The_portal_sign_off_in_flight_still_records_them()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", Guid.NewGuid(), SignoffRequestPlugin.RequestAttr, "{}");

            WriteRecheck(svc, asPortal: true);
        }

        // ---- the outcome guard ---------------------------------------------------------------

        private static FakePluginExecutionContext As(Guid user, Guid initiating, string message = "Update")
        {
            return new FakePluginExecutionContext { UserId = user, InitiatingUserId = initiating, MessageName = message };
        }

        [Fact]
        public void Impersonating_the_portal_application_user_is_not_trusted()
        {
            var svc = new FakeOrganizationService();
            var appUser = Guid.NewGuid();
            var person = Guid.NewGuid();
            svc.Seed("systemuser", appUser, "applicationid", Guid.NewGuid());
            Person(svc, person, "AL Portal - Tax Reviewer", "page.cases");
            svc.Seed("al_outcome", OutcomeId, "al_initialoutcome", new OptionSetValue(OutcomeRules.OutcomeInsufficient));
            var target = new Entity("al_outcome", OutcomeId) { ["al_finaloutcome"] = new OptionSetValue(120910710) };

            Assert.Throws<InvalidPluginExecutionException>(() => OutcomeGuardPlugin.Guard(svc, As(appUser, person), target));
        }

        [Fact]
        public void The_sign_off_permission_alone_does_not_set_a_final_grade_directly()
        {
            var svc = new FakeOrganizationService();
            var person = Guid.NewGuid();
            Person(svc, person, "AL Portal - T&C Supervisor", SignOffRemediationPlugin.SignOffResource);
            svc.Seed("al_outcome", OutcomeId, "al_initialoutcome", new OptionSetValue(OutcomeRules.OutcomePotentialHarm));
            var target = new Entity("al_outcome", OutcomeId) { ["al_finaloutcome"] = new OptionSetValue(120910710) };

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => OutcomeGuardPlugin.Guard(svc, As(person, person), target));
            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, refusal.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_final_grade_on_a_created_outcome_is_held_to_the_regrade_permission()
        {
            var svc = new FakeOrganizationService();
            var person = Guid.NewGuid();
            Person(svc, person, "AL Portal - Tax Reviewer", "page.cases");
            var created = new Entity("al_outcome")
            {
                ["al_outcomecode"] = "OUT-300000001-1",
                ["al_finaloutcome"] = new OptionSetValue(120910710),
            };

            Assert.Throws<InvalidPluginExecutionException>(
                () => OutcomeGuardPlugin.Guard(svc, As(person, person, "Create"), created));
        }

        [Fact]
        public void The_portal_submit_creating_an_outcome_with_its_parked_choice_passes()
        {
            var svc = new FakeOrganizationService();
            var appUser = Guid.NewGuid();
            svc.Seed("systemuser", appUser, "applicationid", Guid.NewGuid());
            var created = new Entity("al_outcome")
            {
                ["al_initialoutcome"] = new OptionSetValue(OutcomeRules.OutcomeInsufficient),
                ["al_aqadviseraccountable"] = true,
            };

            OutcomeGuardPlugin.Guard(svc, As(appUser, appUser, "Create"), created);
        }

        /// <summary>The caller's service as the outcome guard now answers it: no final grade.</summary>
        private sealed class CallerWithoutRegrade : IOrganizationService
        {
            private readonly FakeOrganizationService _inner;

            public CallerWithoutRegrade(FakeOrganizationService inner)
            {
                _inner = inner;
            }

            private static void Refuse(Entity entity)
            {
                if (entity.LogicalName == "al_outcome")
                {
                    throw new InvalidPluginExecutionException(CommandHelpers.UnauthorizedPrefix + "caller may not grade");
                }
            }

            public Guid Create(Entity entity) { Refuse(entity); return _inner.Create(entity); }
            public Entity Retrieve(string entityName, Guid id, Microsoft.Xrm.Sdk.Query.ColumnSet columnSet) { return _inner.Retrieve(entityName, id, columnSet); }
            public void Update(Entity entity) { Refuse(entity); _inner.Update(entity); }
            public void Delete(string entityName, Guid id) { _inner.Delete(entityName, id); }
            public OrganizationResponse Execute(OrganizationRequest request) { return _inner.Execute(request); }
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { _inner.Associate(entityName, entityId, relationship, relatedEntities); }
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { _inner.Disassociate(entityName, entityId, relationship, relatedEntities); }
            public EntityCollection RetrieveMultiple(QueryBase query) { return _inner.RetrieveMultiple(query); }
        }

        [Fact]
        public void A_sign_off_records_its_grade_through_the_system_service_not_the_callers()
        {
            // The supervisor holds the sign-off permission, not the regrade one, so the grade
            // must reach the outcome as SYSTEM or the outcome guard refuses the approval.
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "SYS-1",
                "al_casestatus", new OptionSetValue(CaseLifecycle.AwaitingSignoff));
            svc.Seed("al_reviewinstance", ReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusSubmitted),
                "al_submittedon", new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(0));
            svc.Seed("al_remediationaction", ActionId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                "al_recheckrequired", new OptionSetValue(Remediation.RecheckRequiredYes),
                "statecode", new OptionSetValue(0));
            svc.Seed("al_outcome", OutcomeId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                "al_initialoutcome", new OptionSetValue(OutcomeRules.OutcomeInsufficient),
                "statecode", new OptionSetValue(0));
            var signoff = new Entity("al_signoff", Guid.NewGuid())
            {
                ["al_remediationactionid"] = new EntityReference("al_remediationaction", ActionId),
                ["al_outcomecaseid"] = new EntityReference("al_outcomecase", CaseId),
                ["al_signoffdecision"] = new OptionSetValue(SignoffProgressPlugin.DecisionApprovedValue),
                ["al_notes"] = "Evidenced in full.",
                ["al_finaloutcome"] = new OptionSetValue(OutcomeRules.FinalOutcomePass),
                ["statecode"] = new OptionSetValue(0),
            };
            svc.Seed("al_signoff", signoff.Id,
                "al_remediationactionid", signoff["al_remediationactionid"],
                "al_outcomecaseid", signoff["al_outcomecaseid"],
                "al_signoffdecision", signoff["al_signoffdecision"],
                "al_notes", signoff["al_notes"],
                "al_finaloutcome", signoff["al_finaloutcome"],
                "statecode", signoff["statecode"]);

            SignoffProgressPlugin.Progress(new CallerWithoutRegrade(svc), new FakePluginExecutionContext(), signoff, svc);

            Assert.Equal(OutcomeRules.FinalOutcomePass,
                svc.Row("al_outcome", OutcomeId).GetAttributeValue<OptionSetValue>("al_finaloutcome").Value);
            Assert.Equal(CaseLifecycle.Closed, CaseTransitions.CurrentStatus(svc, CaseId));
        }

        // ---- the review guard ----------------------------------------------------------------

        private static Entity StoredOpen(Guid owner)
        {
            return new Entity("al_reviewinstance", ReviewId)
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusInProgress),
                ["ownerid"] = new EntityReference("systemuser", owner),
            };
        }

        [Fact]
        public void A_submission_date_alone_on_an_open_check_is_refused()
        {
            var stamp = new Entity("al_reviewinstance", ReviewId)
            {
                ["al_submittedon"] = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };

            Assert.NotNull(ReviewSubmissionGuardPlugin.SubmitRefusal(stamp, StoredOpen(Guid.NewGuid()), trusted: true));
        }

        [Fact]
        public void The_pair_together_passes_from_a_trusted_writer_and_is_refused_from_anyone_else()
        {
            // Trusted is the portal: SYSTEM (al_SubmitReview's write) passes before this rule.
            var submit = new Entity("al_reviewinstance", ReviewId)
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusSubmitted),
                ["al_submittedon"] = DateTime.UtcNow,
            };

            Assert.Null(ReviewSubmissionGuardPlugin.SubmitRefusal(submit, StoredOpen(Guid.NewGuid()), trusted: true));
            Assert.NotNull(ReviewSubmissionGuardPlugin.SubmitRefusal(submit, StoredOpen(Guid.NewGuid()), trusted: false));
        }

        [Fact]
        public void Through_the_pipeline_only_system_may_write_the_submit_pair()
        {
            // al_SubmitReview writes the pair as SYSTEM after checking the owner, so neither a
            // stranger nor the owner may write it straight onto the table.
            var owner = Guid.NewGuid();
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewinstance", ReviewId,
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusInProgress),
                "ownerid", new EntityReference("systemuser", owner));
            Func<Entity> submit = () => new Entity("al_reviewinstance", ReviewId)
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusSubmitted),
                ["al_submittedon"] = DateTime.UtcNow,
            };

            var stranger = Provider(svc, "Update", "al_reviewinstance", submit());
            Assert.Throws<InvalidPluginExecutionException>(() => new ReviewSubmissionGuardPlugin(null, null).Execute(stranger));

            var own = Provider(svc, "Update", "al_reviewinstance", submit());
            own.Context.UserId = owner;
            own.Context.InitiatingUserId = owner;
            Assert.Throws<InvalidPluginExecutionException>(() => new ReviewSubmissionGuardPlugin(null, null).Execute(own));

            var system = Provider(svc, "Update", "al_reviewinstance", submit());
            system.Context.UserId = FakeOrganizationService.SystemUserId;
            system.Context.InitiatingUserId = owner;
            new ReviewSubmissionGuardPlugin(null, null).Execute(system);
        }

        [Fact]
        public void A_direct_write_of_the_parked_accountability_is_refused_but_clearing_it_is_not()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewinstance", ReviewId,
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusInProgress));

            var park = Provider(svc, "Update", "al_reviewinstance", new Entity("al_reviewinstance", ReviewId)
            {
                ["al_pendingaccountability"] = "{\"fqAdviser\":true,\"fqContactId\":\"" + Guid.NewGuid() + "\"}",
            });
            Assert.Throws<InvalidPluginExecutionException>(() => new ReviewSubmissionGuardPlugin(null, null).Execute(park));

            var clear = Provider(svc, "Update", "al_reviewinstance", new Entity("al_reviewinstance", ReviewId)
            {
                ["al_pendingaccountability"] = null,
            });
            new ReviewSubmissionGuardPlugin(null, null).Execute(clear);
        }

        // ---- direct allocations ----------------------------------------------------------------

        private static FakeServiceProvider Allocation(FakeOrganizationService svc, bool withReview)
        {
            var provider = new FakeServiceProvider(svc);
            Person(svc, provider.Context.UserId, AllocationScope.TaxTeamManagerRole, "command.assign");
            svc.Seed("al_reviewinstance", ReviewId,
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            var nobody = Guid.NewGuid();
            svc.Seed("systemuser", nobody, "internalemailaddress", "nobody@example.com");
            var contact = svc.Seed("contact", Guid.NewGuid(), "fullname", "Noah Body", "emailaddress1", "nobody@example.com");
            svc.FetchResults.Enqueue(new EntityCollection()); // the assignee holds no web role

            var assignment = new Entity("al_caseassignment", Guid.NewGuid())
            {
                ["al_assigneduserid"] = new EntityReference("systemuser", nobody),
                ["al_assignedcontactid"] = new EntityReference("contact", contact.Id),
                ["al_outcomecaseid"] = new EntityReference("al_outcomecase", CaseId),
            };
            if (withReview)
            {
                assignment["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", ReviewId);
            }

            provider.Context.MessageName = "Create";
            provider.Context.PrimaryEntityName = "al_caseassignment";
            provider.Context.InputParameters["Target"] = assignment;
            return provider;
        }

        [Fact]
        public void A_direct_allocation_to_someone_without_the_reviewer_role_is_refused()
        {
            var svc = new FakeOrganizationService();
            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => new ClaimCasePlugin(null, null).Execute(Allocation(svc, withReview: true)));
            Assert.Contains("Noah Body does not hold the " + WebRoleRegistry.TaxReviewerRole + " role", ex.Message);
        }

        [Fact]
        public void A_direct_allocation_that_names_no_check_is_refused()
        {
            var svc = new FakeOrganizationService();
            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => new ClaimCasePlugin(null, null).Execute(Allocation(svc, withReview: false)));
            Assert.Contains("must name the check", ex.Message);
        }
    }
}
