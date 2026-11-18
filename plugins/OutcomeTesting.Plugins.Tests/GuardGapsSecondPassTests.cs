using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The four gaps left after the 2026-11-17 guard audit: a check created ready-submitted,
    /// an allocation moved by an update, the owner writing the submit pair straight onto the
    /// table, and the Code App submit needing the cases permission to record a parked choice.
    /// </summary>
    public class GuardGapsSecondPassTests
    {
        private const string Email = "someone@example.com";
        private static readonly Guid CaseId = Guid.Parse("bbbbbbbb-8888-4888-8888-000000000001");
        private static readonly Guid ReviewId = Guid.Parse("bbbbbbbb-8888-4888-8888-000000000002");
        private static readonly Guid AssignmentId = Guid.Parse("bbbbbbbb-8888-4888-8888-000000000003");
        private static readonly Guid RouteId = Guid.Parse("bbbbbbbb-8888-4888-8888-000000000004");
        private static readonly Guid TaxReviewId = Guid.Parse("bbbbbbbb-8888-4888-8888-000000000005");
        private static readonly Guid ChecklistVersionId = Guid.Parse("bbbbbbbb-8888-4888-8888-000000000006");
        private static readonly DateTime Stamp = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

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

        private static FakeServiceProvider Provider(FakeOrganizationService svc, string message, Entity target)
        {
            var provider = new FakeServiceProvider(svc);
            provider.Context.MessageName = message;
            provider.Context.PrimaryEntityName = target.LogicalName;
            provider.Context.PrimaryEntityId = target.Id;
            provider.Context.InputParameters["Target"] = target;
            return provider;
        }

        private static string Refused(Action act)
        {
            return Assert.Throws<InvalidPluginExecutionException>(act).Message;
        }

        // ---- A. a check created straight onto the table ---------------------------------------

        private static void CreateReview(FakeOrganizationService svc, Entity review, bool asSystem = false)
        {
            var provider = Provider(svc, "Create", review);
            if (asSystem)
            {
                provider.Context.UserId = FakeOrganizationService.SystemUserId;
                provider.Context.InitiatingUserId = FakeOrganizationService.SystemUserId;
            }
            else
            {
                Person(svc, provider.Context.UserId, "AL Portal - Tax Reviewer");
            }

            new ReviewSubmissionGuardPlugin(null, null).Execute(provider);
        }

        [Fact]
        public void A_check_created_already_submitted_is_refused()
        {
            var svc = new FakeOrganizationService();
            var message = Refused(() => CreateReview(svc, new Entity("al_reviewinstance")
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusSubmitted),
                ["al_submittedon"] = Stamp,
            }));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_check_created_with_a_submission_date_alone_is_refused()
        {
            var svc = new FakeOrganizationService();
            Assert.Throws<InvalidPluginExecutionException>(() => CreateReview(svc, new Entity("al_reviewinstance")
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusAssigned),
                ["al_submittedon"] = Stamp,
            }));
        }

        [Fact]
        public void A_check_created_with_a_parked_accountability_choice_is_refused()
        {
            var svc = new FakeOrganizationService();
            Assert.Throws<InvalidPluginExecutionException>(() => CreateReview(svc, new Entity("al_reviewinstance")
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusAssigned),
                ["al_pendingaccountability"] = "{\"fqAdviser\":true}",
            }));
        }

        [Fact]
        public void System_may_create_any_check()
        {
            var svc = new FakeOrganizationService();
            CreateReview(svc, new Entity("al_reviewinstance")
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusSubmitted),
                ["al_submittedon"] = Stamp,
            }, asSystem: true);
        }

        [Fact]
        public void The_only_creator_of_a_check_still_opens_one_as_an_allocating_caller()
        {
            // ClaimCasePlugin.OpenNextReview, which al_AssignCase calls through the caller's
            // service and the claim through the site's: an open check, Assigned, no date.
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", true, "al_requiresaqsreview", false);
            svc.Seed("al_checklistversion", ChecklistVersionId,
                "al_effectivefrom", DateTime.UtcNow.Date.AddDays(-30), "statecode", new OptionSetValue(0));
            svc.Seed("al_outcomecase", CaseId,
                "al_casestatus", new OptionSetValue(CaseLifecycle.Queued),
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId));
            var manager = Guid.NewGuid();
            Person(svc, manager, AllocationScope.TaxTeamManagerRole, "command.assign");
            var caller = new GuardedPipeline.GuardedService(svc, manager, manager);

            var review = ClaimCasePlugin.OpenNextReview(
                caller,
                svc.Row("al_outcomecase", CaseId),
                new AssignCasePlugin.Assignee { ContactId = Guid.NewGuid(), UserId = Guid.NewGuid(), UserName = "Casey" });

            Assert.Equal(ResponseRules.StatusAssigned,
                svc.Row("al_reviewinstance", review.Id).GetAttributeValue<OptionSetValue>("al_reviewstatus").Value);
        }

        // ---- B. an allocation moved by an update ----------------------------------------------

        private static FakeOrganizationService Allocated(bool newPersonHoldsRole, out Guid newUser, out Guid newContact)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewinstance", ReviewId,
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            svc.Seed("al_caseassignment", AssignmentId,
                "al_assigneduserid", new EntityReference("systemuser", Guid.NewGuid()),
                "al_assignedcontactid", new EntityReference("contact", Guid.NewGuid()),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_isactive", true);

            newUser = Guid.NewGuid();
            newContact = Guid.NewGuid();
            svc.Seed("systemuser", newUser, "internalemailaddress", "noah@example.com", "fullname", "Noah Body");
            svc.Seed("contact", newContact, "fullname", "Noah Body", "emailaddress1", "noah@example.com", "statecode", new OptionSetValue(0));
            var roles = new List<Entity>();
            if (newPersonHoldsRole)
            {
                var role = new Entity("contact", newContact);
                role["role.name"] = new AliasedValue("powerpagecomponent", "name", WebRoleRegistry.TaxReviewerRole);
                role["role.statecode"] = new AliasedValue("powerpagecomponent", "statecode", new OptionSetValue(0));
                roles.Add(role);
            }

            svc.FetchResults.Enqueue(new EntityCollection(roles));
            return svc;
        }

        private static void UpdateAssignment(FakeOrganizationService svc, Entity update, Action<FakePluginExecutionContext> who)
        {
            var provider = Provider(svc, "Update", update);
            who(provider.Context);
            new ClaimCasePlugin(null, null).Execute(provider);
        }

        private static Entity MoveTo(Guid user, Guid contact)
        {
            return new Entity("al_caseassignment", AssignmentId)
            {
                ["al_assigneduserid"] = new EntityReference("systemuser", user),
                ["al_assignedcontactid"] = new EntityReference("contact", contact),
            };
        }

        [Fact]
        public void Moving_an_allocation_to_someone_without_the_reviewer_role_is_refused()
        {
            Guid user, contact;
            var svc = Allocated(false, out user, out contact);

            var message = Refused(() => UpdateAssignment(svc, MoveTo(user, contact),
                c => Person(svc, c.UserId, AllocationScope.TaxTeamManagerRole, "command.assign")));

            Assert.Contains("Noah Body does not hold the " + WebRoleRegistry.TaxReviewerRole + " role", message);
        }

        [Fact]
        public void Moving_an_allocation_without_the_assign_permission_is_refused()
        {
            Guid user, contact;
            var svc = Allocated(true, out user, out contact);

            var message = Refused(() => UpdateAssignment(svc, MoveTo(user, contact),
                c => Person(svc, c.UserId, "AL Portal - Tax Reviewer")));

            Assert.Contains("command.assign", message);
        }

        [Fact]
        public void Moving_an_allocation_to_a_check_on_another_case_is_refused()
        {
            Guid user, contact;
            var svc = Allocated(true, out user, out contact);
            var otherCheck = Guid.NewGuid();
            svc.Seed("al_reviewinstance", otherCheck,
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_outcomecaseid", new EntityReference("al_outcomecase", Guid.NewGuid()));

            var message = Refused(() => UpdateAssignment(svc,
                new Entity("al_caseassignment", AssignmentId) { ["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", otherCheck) },
                c => Person(svc, c.UserId, AllocationScope.TaxTeamManagerRole, "command.assign")));

            Assert.Contains("does not belong to this case", message);
        }

        [Fact]
        public void An_allocator_moving_it_to_a_tax_reviewer_passes()
        {
            Guid user, contact;
            var svc = Allocated(true, out user, out contact);

            UpdateAssignment(svc, MoveTo(user, contact),
                c => Person(svc, c.UserId, AllocationScope.TaxTeamManagerRole, "command.assign"));
        }

        [Fact]
        public void The_assign_commands_release_of_prior_rows_needs_no_permission()
        {
            // AssignCasePlugin.ReleasePriorAssignments, through the guard, as a caller holding
            // nothing: a release allocates nobody.
            Guid user, contact;
            var svc = Allocated(true, out user, out contact);
            var nobody = Guid.NewGuid();
            Person(svc, nobody, "AL Portal - Tax Reviewer");

            AssignCasePlugin.ReleasePriorAssignments(new GuardedPipeline.GuardedService(svc, nobody, nobody), CaseId);

            Assert.False(svc.Row("al_caseassignment", AssignmentId).GetAttributeValue<bool>("al_isactive"));
        }

        [Fact]
        public void Restating_an_allocation_as_it_stands_is_not_read_as_a_move()
        {
            Guid user, contact;
            var svc = Allocated(true, out user, out contact);
            var stored = svc.Row("al_caseassignment", AssignmentId);

            Assert.Null(ClaimCasePlugin.AllocationAfterUpdate(svc, new Entity("al_caseassignment", AssignmentId)
            {
                ["al_assigneduserid"] = stored["al_assigneduserid"],
                ["al_reviewinstanceid"] = stored["al_reviewinstanceid"],
                ["al_isactive"] = true,
            }));
        }

        [Fact]
        public void Reactivating_a_released_allocation_is_a_move()
        {
            Guid user, contact;
            var svc = Allocated(true, out user, out contact);
            svc.Update(new Entity("al_caseassignment", AssignmentId) { ["al_isactive"] = false });

            Assert.NotNull(ClaimCasePlugin.AllocationAfterUpdate(svc, new Entity("al_caseassignment", AssignmentId)
            {
                ["al_isactive"] = true,
            }));
        }

        // ---- C and D. the submit pair, and the outcome a Code App submit creates ---------------

        [Fact]
        public void The_owner_writing_the_submit_pair_straight_onto_the_table_is_refused()
        {
            var owner = Guid.NewGuid();
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewinstance", ReviewId,
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusInProgress),
                "ownerid", new EntityReference("systemuser", owner));
            var provider = Provider(svc, "Update", new Entity("al_reviewinstance", ReviewId)
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusSubmitted),
                ["al_submittedon"] = DateTime.UtcNow,
            });
            provider.Context.UserId = owner;
            provider.Context.InitiatingUserId = owner;

            Assert.Throws<InvalidPluginExecutionException>(() => new ReviewSubmissionGuardPlugin(null, null).Execute(provider));
        }

        /// <summary>
        /// A Tax-then-AQS case whose Tax check is submitted with a parked "who carries this
        /// fail" choice, and whose AQS check is open, owned by <paramref name="checker"/>.
        /// </summary>
        private static FakeOrganizationService AqsDue(Guid checker)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", true, "al_requiresaqsreview", true);
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "GG2-0001",
                "al_casestatus", new OptionSetValue(CaseLifecycle.ReviewInProgress),
                "al_adviseremail", "adviser@example.invalid",
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId));
            svc.Seed("al_checklistversion", ChecklistVersionId);

            Review(svc, TaxReviewId, ResponseRules.ReviewTypeTax, 1, null,
                "{\"fqAdviser\":true,\"fqParaplanner\":false,\"aqAdviser\":false,\"aqParaplanner\":false,\"fqContactId\":\"\",\"aqContactId\":\"\"}");
            Review(svc, ReviewId, ResponseRules.ReviewTypeAqs, 2, checker, null);
            Answer(svc, TaxReviewId, "Q-TAX-02", ResponseRules.ChoiceFail);
            Answer(svc, ReviewId, GradingRules.GradeQuestionCode, ResponseRules.ChoicePassWithIssues);

            SubmitReviewPlugin.Submit(svc, TaxReviewId, "gg2-tax", null, Guid.NewGuid(), Guid.NewGuid(),
                requireCallerOwnsReview: false, details: "setup");
            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);

            // The checker: an AQS reviewer holding no cases permission.
            Person(svc, checker, "AL Portal - AQS Reviewer", "page.checklist");
            return svc;
        }

        private static void Review(FakeOrganizationService svc, Guid id, int reviewType, int sequence, Guid? owner, string parked)
        {
            svc.Seed(
                "al_reviewinstance", id,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(reviewType),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusInProgress),
                "al_checklistversionid", new EntityReference("al_checklistversion", ChecklistVersionId),
                "al_sequence", sequence,
                "al_pendingremedialactions", "[{\"item\":\"__overall__\",\"text\":\"Put it right.\"},{\"item\":\"__clientcontact__\",\"text\":\"120910794\"}]",
                "statecode", new OptionSetValue(0));
            if (owner.HasValue)
            {
                svc.Row("al_reviewinstance", id)["ownerid"] = new EntityReference("systemuser", owner.Value);
            }

            if (parked != null)
            {
                svc.Row("al_reviewinstance", id)["al_pendingaccountability"] = parked;
            }
        }

        private static void Answer(FakeOrganizationService svc, Guid reviewId, string questionCode, int choice)
        {
            var questionId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            svc.Seed("al_question", questionId, "al_questioncode", questionCode);
            svc.Seed("al_questionversion", versionId, "al_questionid", new EntityReference("al_question", questionId));
            svc.Seed(
                "al_response", Guid.NewGuid(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", versionId),
                "al_answerchoice", new OptionSetValue(choice),
                "statecode", new OptionSetValue(0));
        }

        [Fact]
        public void A_code_app_submit_by_the_checker_passes_the_guards_and_records_the_parked_choice()
        {
            // al_SubmitReview through a host whose services run the table guards: the owner
            // check runs as the caller, the submit pair and the outcome are written as SYSTEM.
            var checker = Guid.NewGuid();
            var svc = AqsDue(checker);
            var host = new GuardedPipeline(svc);
            host.Context.UserId = checker;
            host.Context.InitiatingUserId = checker;
            host.Context.MessageName = "al_SubmitReview";
            host.Context.InputParameters["TargetId"] = ReviewId.ToString("D");
            host.Context.InputParameters["IdempotencyKey"] = "gg2-aqs";

            new SubmitReviewPlugin(null, null).Execute(host);

            var review = svc.Row("al_reviewinstance", ReviewId);
            Assert.Equal(ResponseRules.StatusSubmitted, review.GetAttributeValue<OptionSetValue>("al_reviewstatus").Value);
            Assert.NotNull(review.GetAttributeValue<DateTime?>("al_submittedon"));
            var outcome = Assert.Single(svc.All("al_outcome"));
            Assert.True(outcome.GetAttributeValue<bool>("al_fqadviseraccountable"));
        }

        [Fact]
        public void A_code_app_submit_by_anyone_but_the_owner_is_still_refused()
        {
            var svc = AqsDue(Guid.NewGuid());
            var stranger = Guid.NewGuid();
            var host = new GuardedPipeline(svc);
            host.Context.UserId = stranger;
            host.Context.InitiatingUserId = stranger;
            host.Context.MessageName = "al_SubmitReview";
            host.Context.InputParameters["TargetId"] = ReviewId.ToString("D");
            host.Context.InputParameters["IdempotencyKey"] = "gg2-aqs-stranger";

            var message = Refused(() => new SubmitReviewPlugin(null, null).Execute(host));

            Assert.Contains("Only the checker assigned", message);
            Assert.Empty(svc.All("al_outcome"));
        }
    }
}
