using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Direct table writes are held to the permission the matching command checks (audit,
    /// 2026-11-16). The app's Dataverse role keeps Global create and write on these tables
    /// because the commands write as the caller, so the guards trust SYSTEM and the portal's
    /// application user and hold everyone else to the command's rule.
    /// </summary>
    public class WriteGuardFixTests
    {
        private const string Email = "someone@example.com";
        private const string Role = "AL Portal - Tax Reviewer";

        private static readonly Guid ActionId = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000001");
        private static readonly Guid CaseId = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000002");
        private static readonly Guid OutcomeId = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000003");
        private static readonly Guid ReviewId = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000004");
        private static readonly Guid ContactId = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000005");
        private static readonly Guid RouteId = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000006");

        // ---- callers ---------------------------------------------------------------------

        /// <summary>An interactive person holding an app role, and the rules named.</summary>
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

        /// <summary>
        /// The site's application user. A mapping exists for someone else, so the gate's
        /// empty-table bootstrap is not what lets it through.
        /// </summary>
        private static void Portal(FakeOrganizationService svc, Guid userId)
        {
            svc.Seed("systemuser", userId, "applicationid", Guid.NewGuid());
            svc.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", "other@example.com", "al_rolecode", Role, "statecode", new OptionSetValue(0));
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

        private static string Refused(Action act)
        {
            return Assert.Throws<InvalidPluginExecutionException>(act).Message;
        }

        // ---- 1a, 2, 4: sign-off create -----------------------------------------------------

        private static FakeOrganizationService SignoffReady(int caseStatus, DateTime? clock = null)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId, "al_casestatus", new OptionSetValue(caseStatus));
            var action = svc.Seed("al_remediationaction", ActionId,
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            if (clock.HasValue)
            {
                action["al_clockstartedon"] = clock.Value;
            }

            return svc;
        }

        private static Entity Signoff(int decision = SignoffProgressPlugin.DecisionApprovedValue)
        {
            return new Entity("al_signoff")
            {
                ["al_signoffdecision"] = new OptionSetValue(decision),
                ["al_notes"] = "Checked.",
                ["al_remediationactionid"] = new EntityReference("al_remediationaction", ActionId),
            };
        }

        private static void CreateSignoff(FakeOrganizationService svc, Entity signoff, Action<FakePluginExecutionContext> who)
        {
            var provider = Provider(svc, "Create", "al_signoff", signoff);
            who(provider.Context);
            new SignoffGuardPlugin(null, null).Execute(provider);
        }

        [Fact]
        public void A_direct_signoff_without_the_signoff_permission_is_refused()
        {
            var svc = SignoffReady(CaseLifecycle.AwaitingSignoff);
            var signoff = Signoff();

            var message = Refused(() => CreateSignoff(svc, signoff, c => Person(svc, c.UserId, Role, "page.cases")));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, message, StringComparison.Ordinal);
            Assert.Contains(SignOffRemediationPlugin.SignOffResource, message);
            Assert.False(signoff.Contains("al_signoffcode"));
        }

        [Fact]
        public void A_signoff_by_a_holder_of_the_signoff_permission_is_stamped()
        {
            var svc = SignoffReady(CaseLifecycle.AwaitingSignoff);
            var signoff = Signoff();

            CreateSignoff(svc, signoff, c => Person(svc, c.UserId, Role, SignOffRemediationPlugin.SignOffResource));

            Assert.Equal(SignoffGuardPlugin.SignoffCode(ActionId, null), signoff.GetAttributeValue<string>("al_signoffcode"));
        }

        [Fact]
        public void The_portals_signoff_needs_no_app_permission()
        {
            var svc = SignoffReady(CaseLifecycle.AwaitingSignoff);
            var signoff = Signoff();

            CreateSignoff(svc, signoff, c => Portal(svc, c.UserId));

            Assert.True(signoff.Contains("al_signoffcode"));
        }

        [Fact]
        public void A_signoff_written_as_system_needs_no_app_permission()
        {
            var svc = SignoffReady(CaseLifecycle.AwaitingSignoff);
            svc.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", "other@example.com", "al_rolecode", Role, "statecode", new OptionSetValue(0));
            var signoff = Signoff();

            // Nothing seeded for either user: a SYSTEM write must not even look them up.
            CreateSignoff(svc, signoff, c => c.UserId = FakeOrganizationService.SystemUserId);

            Assert.True(signoff.Contains("al_signoffcode"));
        }

        [Fact]
        public void A_rejection_on_a_closed_case_is_refused()
        {
            // Pass with issues closes on the last completion; a rejection after that used to
            // reopen the action on a case that can never move again.
            var svc = SignoffReady(CaseLifecycle.Closed);
            var signoff = Signoff(SignoffProgressPlugin.DecisionRejectedValue);

            var message = Refused(() => CreateSignoff(svc, signoff, c => Portal(svc, c.UserId)));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, message, StringComparison.Ordinal);
            Assert.Contains("not Awaiting Sign-off", message);
            Assert.False(signoff.Contains("al_signoffcode"));
        }

        [Fact]
        public void A_signoff_on_a_case_still_in_remediation_is_refused()
        {
            var svc = SignoffReady(CaseLifecycle.AwaitingRemediation);

            var message = Refused(() => CreateSignoff(svc, Signoff(), c => Portal(svc, c.UserId)));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, message, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_decisions_in_the_same_round_get_the_same_code()
        {
            // So the second insert collides on the al_signoffcode key instead of both
            // approvals passing the already-approved check before either commits.
            var clock = new DateTime(2026, 11, 2, 9, 30, 0, DateTimeKind.Utc);
            var first = Signoff();
            var second = Signoff();

            var svc = SignoffReady(CaseLifecycle.AwaitingSignoff, clock);
            CreateSignoff(svc, first, c => Portal(svc, c.UserId));
            svc = SignoffReady(CaseLifecycle.AwaitingSignoff, clock);
            CreateSignoff(svc, second, c => Portal(svc, c.UserId));

            Assert.Equal(first.GetAttributeValue<string>("al_signoffcode"), second.GetAttributeValue<string>("al_signoffcode"));
        }

        [Fact]
        public void A_rejection_starts_a_round_with_a_new_code()
        {
            // The rejection restarts the action's clock, so the reworked action can be
            // decided again without colliding with the decision that sent it back.
            var restarted = SignoffProgressPlugin.ReopenedAction(ActionId, new DateTime(2026, 11, 3, 14, 0, 0, DateTimeKind.Utc));

            Assert.NotEqual(
                SignoffGuardPlugin.SignoffCode(ActionId, null),
                SignoffGuardPlugin.SignoffCode(ActionId, restarted.GetAttributeValue<DateTime>("al_clockstartedon")));
            Assert.NotEqual(
                SignoffGuardPlugin.SignoffCode(ActionId, null),
                SignoffGuardPlugin.SignoffCode(Guid.NewGuid(), null));
        }

        [Fact]
        public void The_signoff_code_is_the_tables_alternate_key_and_fits_the_column()
        {
            // The collision is the platform's, so it only exists while the key does.
            var entity = XDocument.Load(RepoPath("src", "Entities", "al_Signoff", "Entity.xml"));
            var keyColumns = entity.Descendants("EntityKey").Descendants("AttributeName").Select(e => e.Value).ToArray();
            Assert.Equal(new[] { "al_signoffcode" }, keyColumns);

            var code = SignoffGuardPlugin.SignoffCode(ActionId, DateTime.UtcNow);
            var max = entity.Descendants("attribute")
                .Single(a => (string)a.Element("LogicalName") == "al_signoffcode")
                .Element("MaxLength").Value;
            Assert.True(code.Length <= int.Parse(max), code);
        }

        // ---- 1b, 3: case assignment create -------------------------------------------------

        private static Entity Claim(Guid? assignedUser = null, Guid? review = null)
        {
            var row = new Entity("al_caseassignment")
            {
                ["al_outcomecaseid"] = new EntityReference("al_outcomecase", CaseId),
                ["al_assignedcontactid"] = new EntityReference("contact", ContactId),
            };
            if (assignedUser.HasValue)
            {
                row["al_assigneduserid"] = new EntityReference("systemuser", assignedUser.Value);
            }

            if (review.HasValue)
            {
                row["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", review.Value);
            }

            return row;
        }

        private static void CreateAssignment(FakeOrganizationService svc, Entity row, Action<FakePluginExecutionContext> who)
        {
            var provider = Provider(svc, "Create", "al_caseassignment", row);
            who(provider.Context);
            new ClaimCasePlugin(null, null).Execute(provider);
        }

        /// <summary>A queued Tax case and a checker who holds the Tax reviewer role.</summary>
        private static FakeOrganizationService ClaimReady()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, "fullname", "Casey Checker", "emailaddress1", "casey@example.com", "statecode", new OptionSetValue(0));
            svc.Seed("systemuser", Guid.NewGuid(), "internalemailaddress", "casey@example.com", "fullname", "Casey Checker");
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", true, "al_requiresaqsreview", false);
            svc.Seed("al_checklistversion", Guid.NewGuid(),
                "al_effectivefrom", DateTime.UtcNow.Date.AddDays(-30), "statecode", new OptionSetValue(0));
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-1",
                "al_casestatus", new OptionSetValue(CaseLifecycle.Queued),
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId));

            // The checker's web roles, which EnsureDisciplineRole reads with a joined fetch.
            var role = new Entity("contact", ContactId);
            role["role.name"] = new AliasedValue("powerpagecomponent", "name", WebRoleRegistry.TaxReviewerRole);
            svc.FetchResults.Enqueue(new EntityCollection(new List<Entity> { role }));
            return svc;
        }

        [Fact]
        public void A_claim_shaped_row_written_directly_without_the_assign_permission_is_refused()
        {
            var svc = ClaimReady();

            var message = Refused(() => CreateAssignment(svc, Claim(), c => Person(svc, c.UserId, Role, "page.cases")));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, message, StringComparison.Ordinal);
            Assert.Contains("command.assign", message);
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void A_claim_shaped_row_from_someone_who_does_not_allocate_this_discipline_is_refused()
        {
            var svc = ClaimReady();

            var message = Refused(() => CreateAssignment(svc, Claim(), c => Person(svc, c.UserId, Role, "command.assign")));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, message, StringComparison.Ordinal);
            Assert.Contains("does not allocate", message);
        }

        [Fact]
        public void The_portals_claim_reaches_the_claim_rules()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, "fullname", "Casey Checker");

            // Past the gate with no app permission: the first claim rule is what answers.
            var message = Refused(() => CreateAssignment(svc, Claim(), c => Portal(svc, c.UserId)));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, message, StringComparison.Ordinal);
            Assert.Contains("no work email", message);
        }

        [Fact]
        public void A_direct_allocation_row_is_held_to_the_allocators_scope()
        {
            var svc = new FakeOrganizationService();
            OpenTaxReview(svc);

            var unpermitted = Refused(() => CreateAssignment(svc, Claim(Guid.NewGuid(), ReviewId), c => Person(svc, c.UserId, Role)));
            Assert.Contains("command.assign", unpermitted);

            svc = new FakeOrganizationService();
            OpenTaxReview(svc);
            var outOfScope = Refused(() => CreateAssignment(svc, Claim(Guid.NewGuid(), ReviewId), c => Person(svc, c.UserId, Role, "command.assign")));
            Assert.Contains("does not allocate", outOfScope);
        }

        [Fact]
        public void The_assign_commands_own_row_still_passes()
        {
            // al_AssignCase creates as the caller, who has already passed both checks.
            var svc = new FakeOrganizationService();
            OpenTaxReview(svc);
            var row = Claim(Allocatable(svc, holdsTaxRole: true), ReviewId);

            CreateAssignment(svc, row, c => Person(svc, c.UserId, AllocationScope.TaxTeamManagerRole, "command.assign"));

            Assert.False(row.Contains("al_caseassignmentcode"));
        }

        /// <summary>An unsubmitted Tax check on <see cref="CaseId"/>.</summary>
        private static void OpenTaxReview(FakeOrganizationService svc)
        {
            svc.Seed("al_reviewinstance", ReviewId,
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
        }

        /// <summary>
        /// Casey as al_AssignCase resolves her: a user and an active contact on one work email,
        /// and her web roles for the joined fetch. Returns the user's id.
        /// </summary>
        private static Guid Allocatable(FakeOrganizationService svc, bool holdsTaxRole)
        {
            var userId = Guid.NewGuid();
            svc.Seed("systemuser", userId, "internalemailaddress", "casey@example.com", "fullname", "Casey Checker");
            svc.Seed("contact", ContactId, "fullname", "Casey Checker", "emailaddress1", "casey@example.com", "statecode", new OptionSetValue(0));
            var roles = new List<Entity>();
            if (holdsTaxRole)
            {
                var role = new Entity("contact", ContactId);
                role["role.name"] = new AliasedValue("powerpagecomponent", "name", WebRoleRegistry.TaxReviewerRole);
                role["role.statecode"] = new AliasedValue("powerpagecomponent", "statecode", new OptionSetValue(0));
                roles.Add(role);
            }

            svc.FetchResults.Enqueue(new EntityCollection(roles));
            return userId;
        }

        private static void AsTaxManager(FakeOrganizationService svc, FakePluginExecutionContext c)
        {
            Person(svc, c.UserId, AllocationScope.TaxTeamManagerRole, "command.assign");
        }

        [Fact]
        public void A_direct_allocation_for_a_check_on_another_case_is_refused()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewinstance", ReviewId,
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_outcomecaseid", new EntityReference("al_outcomecase", Guid.NewGuid()));
            var row = Claim(Allocatable(svc, holdsTaxRole: true), ReviewId);

            var message = Refused(() => CreateAssignment(svc, row, c => AsTaxManager(svc, c)));

            Assert.Contains("does not belong to this case", message);
        }

        [Fact]
        public void A_direct_allocation_for_a_submitted_check_is_refused()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewinstance", ReviewId,
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_submittedon", Stamp);
            var row = Claim(Allocatable(svc, holdsTaxRole: true), ReviewId);

            var message = Refused(() => CreateAssignment(svc, row, c => AsTaxManager(svc, c)));

            Assert.Contains("already submitted", message);
        }

        [Fact]
        public void A_direct_allocation_naming_another_persons_contact_is_refused()
        {
            // The role check reads the contact; the work goes to the user. They must be one person.
            var svc = new FakeOrganizationService();
            OpenTaxReview(svc);
            var someoneElse = Guid.NewGuid();
            svc.Seed("systemuser", someoneElse, "internalemailaddress", "other.person@example.com", "fullname", "Other Person");
            svc.Seed("contact", Guid.NewGuid(), "fullname", "Other Person", "emailaddress1", "other.person@example.com", "statecode", new OptionSetValue(0));
            Allocatable(svc, holdsTaxRole: true);

            var message = Refused(() => CreateAssignment(svc, Claim(someoneElse, ReviewId), c => AsTaxManager(svc, c)));

            Assert.Contains("not the user it names", message);
        }

        [Fact]
        public void A_claim_shaped_row_from_an_allocator_to_someone_without_the_reviewer_role_is_refused()
        {
            var svc = ClaimReady();
            svc.FetchResults.Clear();
            svc.FetchResults.Enqueue(new EntityCollection()); // Casey holds no web role

            // The claim rules hold the named contact to the discipline's reviewer role.
            var message = Refused(() => CreateAssignment(svc, Claim(), c => AsTaxManager(svc, c)));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, message, StringComparison.Ordinal);
            Assert.Contains(WebRoleRegistry.TaxReviewerRole, message);
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void The_checker_who_already_holds_the_due_review_can_claim_it_again()
        {
            var svc = ClaimReady();
            var outcomeCase = svc.Row("al_outcomecase", CaseId);
            svc.Seed("al_reviewinstance", ReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusAssigned),
                "al_sequence", 1,
                "al_assignedcontactid", new EntityReference("contact", ContactId),
                "statecode", new OptionSetValue(0));

            var me = new AssignCasePlugin.Assignee { ContactId = ContactId, UserId = Guid.NewGuid(), UserName = "Casey Checker" };

            Assert.Equal(ReviewId, ClaimCasePlugin.ResolveOrCreateReview(svc, outcomeCase, me).Id);
        }

        [Fact]
        public void The_holder_can_also_reclaim_a_case_that_carries_no_route()
        {
            var svc = ClaimReady();
            var outcomeCase = svc.Row("al_outcomecase", CaseId);
            outcomeCase.Attributes.Remove("al_reviewrouteid");
            svc.Seed("al_reviewinstance", ReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_sequence", 1,
                "al_assignedcontactid", new EntityReference("contact", ContactId),
                "statecode", new OptionSetValue(0));

            var me = new AssignCasePlugin.Assignee { ContactId = ContactId, UserId = Guid.NewGuid(), UserName = "Casey Checker" };

            Assert.Equal(ReviewId, ClaimCasePlugin.ResolveOrCreateReview(svc, outcomeCase, me).Id);
        }

        // ---- 1c, 5: portal request columns -------------------------------------------------

        public static IEnumerable<object[]> RequestWrites()
        {
            yield return new object[] { "submit", "al_reviewinstance", "al_submitrequested", true };
            yield return new object[] { "complete", "al_remediationaction", "al_completerequested", true };
            yield return new object[] { "answer", "al_reviewinstance", "al_answerrequest", "{}" };
            yield return new object[] { "claim", "contact", "al_claimrequest", CaseId.ToString("D") };
            yield return new object[] { "signoff", "contact", SignoffRequestPlugin.RequestAttr, "{}" };
            yield return new object[] { "regrade", "contact", RegradeRequestPlugin.RequestAttr, "{}" };
            yield return new object[] { "caseheader", "contact", CaseHeaderRequestPlugin.RequestAttr, "{}" };
            yield return new object[] { "accountability", "contact", AccountabilityRequestPlugin.RequestAttr, "{}" };
            yield return new object[] { "remedial", "contact", RemedialActionsRequestPlugin.RequestAttr, "{}" };
        }

        private static IPlugin RequestPlugin(string name)
        {
            switch (name)
            {
                case "submit": return new SubmitRequestPlugin(null, null);
                case "complete": return new CompleteRequestPlugin(null, null);
                case "answer": return new AnswerRequestPlugin(null, null);
                case "claim": return new ClaimRequestPlugin(null, null);
                case "signoff": return new SignoffRequestPlugin(null, null);
                case "regrade": return new RegradeRequestPlugin(null, null);
                case "caseheader": return new CaseHeaderRequestPlugin(null, null);
                case "accountability": return new AccountabilityRequestPlugin(null, null);
                default: return new RemedialActionsRequestPlugin(null, null);
            }
        }

        private static void WriteRequest(
            FakeOrganizationService svc, string plugin, string entity, string attr, object value, Action<FakePluginExecutionContext> who)
        {
            var target = new Entity(entity, ContactId) { [attr] = value };
            var provider = Provider(svc, "Update", entity, target);
            who(provider.Context);
            RequestPlugin(plugin).Execute(provider);
        }

        [Theory]
        [MemberData(nameof(RequestWrites))]
        public void A_request_column_written_by_anyone_but_the_portal_is_refused(string plugin, string entity, string attr, object value)
        {
            // An App Admin writing al_signoffrequest onto a supervisor's contact is this case.
            var svc = new FakeOrganizationService();

            var message = Refused(() => WriteRequest(svc, plugin, entity, attr, value,
                c => Person(svc, c.UserId, AllocationScope.TaxTeamManagerRole, "page.cases", SignOffRemediationPlugin.SignOffResource)));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, message, StringComparison.Ordinal);
            Assert.Empty(svc.Updates);
            Assert.Empty(svc.Creates);
        }

        [Theory]
        [InlineData("claim", "al_claimrequest", "not-a-case")]
        [InlineData("signoff", SignoffRequestPlugin.RequestAttr, "{")]
        [InlineData("regrade", RegradeRequestPlugin.RequestAttr, "{")]
        [InlineData("caseheader", CaseHeaderRequestPlugin.RequestAttr, "{")]
        [InlineData("accountability", AccountabilityRequestPlugin.RequestAttr, "{")]
        [InlineData("remedial", RemedialActionsRequestPlugin.RequestAttr, "{")]
        public void The_portals_request_reaches_the_plugins_own_rules(string plugin, string attr, string value)
        {
            var svc = new FakeOrganizationService();

            var message = Refused(() => WriteRequest(svc, plugin, "contact", attr, value, c => Portal(svc, c.UserId)));

            Assert.StartsWith(CommandHelpers.ValidationPrefix, message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_whitespace_signoff_request_is_refused_rather_than_left_standing()
        {
            // A non-null value reads as a sign-off in flight to the response guard, so it must
            // not survive: the refusal rolls the write back.
            var svc = new FakeOrganizationService();

            var message = Refused(() => WriteRequest(svc, "signoff", "contact", SignoffRequestPlugin.RequestAttr, "   ",
                c => Portal(svc, c.UserId)));

            Assert.StartsWith(CommandHelpers.ValidationPrefix, message, StringComparison.Ordinal);

            var other = new FakeOrganizationService();
            var unauthorised = Refused(() => WriteRequest(other, "signoff", "contact", SignoffRequestPlugin.RequestAttr, " ",
                c => Person(other, c.UserId, Role)));
            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, unauthorised, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void The_clear_down_is_still_a_no_op(string value)
        {
            // Nothing seeded: the clear-down must not even look the caller up.
            var svc = new FakeOrganizationService();

            WriteRequest(svc, "signoff", "contact", SignoffRequestPlugin.RequestAttr, value, c => { });

            Assert.Empty(svc.Updates);
        }

        // ---- 1d: outcome final grade and accountability -----------------------------------

        private static FakeOrganizationService OutcomeStored(params object[] pairs)
        {
            var svc = new FakeOrganizationService();
            var all = new List<object>
            {
                Outcomes.InitialOutcomeAttr, new OptionSetValue(OutcomeRules.OutcomeInsufficient),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
            };
            all.AddRange(pairs);
            svc.Seed("al_outcome", OutcomeId, all.ToArray());
            return svc;
        }

        private static FakePluginExecutionContext Caller(FakeOrganizationService svc, string roleCode, params string[] resources)
        {
            var context = new FakePluginExecutionContext();
            Person(svc, context.UserId, roleCode, resources);
            return context;
        }

        /// <summary>The update RegradeCasePlugin.Regrade writes, which the sign-off path also uses.</summary>
        private static Entity RegradeWrite()
        {
            return new Entity("al_outcome", OutcomeId)
            {
                [Outcomes.FinalOutcomeAttr] = new OptionSetValue(OutcomeRules.FinalOutcomePass),
                ["al_regradereason"] = "Rework accepted.",
                ["al_regradedon"] = DateTime.UtcNow,
                ["al_finalisedon"] = DateTime.UtcNow,
            };
        }

        /// <summary>The update SetFailAccountabilityPlugin and AccountabilityRequestPlugin write.</summary>
        private static Entity AccountabilityWrite(bool fqAdviser = true)
        {
            return new Entity("al_outcome", OutcomeId)
            {
                ["al_fqadviseraccountable"] = fqAdviser,
                ["al_fqparaplanneraccountable"] = false,
                ["al_aqadviseraccountable"] = false,
                ["al_aqparaplanneraccountable"] = false,
                ["al_fqaccountablecontactid"] = null,
                ["al_aqaccountablecontactid"] = null,
            };
        }

        [Fact]
        public void A_direct_final_grade_without_the_regrade_permission_is_refused()
        {
            var svc = OutcomeStored();

            var message = Refused(() => OutcomeGuardPlugin.Guard(svc, Caller(svc, Role, "page.cases"), RegradeWrite()));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, message, StringComparison.Ordinal);
            Assert.Contains(RegradeCasePlugin.RegradeResource, message);
        }

        [Fact]
        public void Direct_accountability_without_the_cases_permission_is_refused()
        {
            var svc = OutcomeStored();

            var message = Refused(() => OutcomeGuardPlugin.Guard(
                svc, Caller(svc, Role, RegradeCasePlugin.RegradeResource), AccountabilityWrite()));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, message, StringComparison.Ordinal);
            Assert.Contains("page.cases", message);
        }

        [Fact]
        public void Writer_regrade_command_passes()
        {
            var svc = OutcomeStored();
            OutcomeGuardPlugin.Guard(svc, Caller(svc, Role, RegradeCasePlugin.RegradeResource), RegradeWrite());
        }

        [Fact]
        public void The_signoff_permission_alone_no_longer_sets_a_final_grade_directly()
        {
            // 2026-11-17 audit: a sign-off records its grade as SYSTEM (SignoffProgress), so the
            // sign-off permission is no longer leave to set any grade on any outcome.
            var svc = OutcomeStored();
            Assert.Throws<InvalidPluginExecutionException>(() =>
                OutcomeGuardPlugin.Guard(svc, Caller(svc, Role, SignOffRemediationPlugin.SignOffResource), RegradeWrite()));
        }

        [Fact]
        public void Writer_set_fail_accountability_passes()
        {
            var svc = OutcomeStored();
            OutcomeGuardPlugin.Guard(svc, Caller(svc, Role, "page.cases"), AccountabilityWrite());
        }

        [Fact]
        public void Writer_portal_regrade_and_accountability_requests_pass()
        {
            // RegradeRequestPlugin, the portal sign-off and AccountabilityRequestPlugin all
            // write as the site, with no app permission at all.
            var svc = OutcomeStored();
            var context = new FakePluginExecutionContext();
            Portal(svc, context.UserId);

            OutcomeGuardPlugin.Guard(svc, context, RegradeWrite());
            OutcomeGuardPlugin.Guard(svc, context, AccountabilityWrite());
        }

        [Fact]
        public void Writer_submit_upserting_over_its_own_outcome_passes()
        {
            // SubmitReviewPlugin.CreateOutcome upserts on the outcome code, so a replay is an
            // Update carrying the grade and the parked flags it already wrote. A checker holds
            // no regrade or cases permission, and an unchanged value asks for none.
            var svc = OutcomeStored("al_fqadviseraccountable", true);
            var replay = AccountabilityWrite();
            replay[Outcomes.InitialOutcomeAttr] = new OptionSetValue(OutcomeRules.OutcomeInsufficient);

            OutcomeGuardPlugin.Guard(svc, Caller(svc, Role), replay);
        }

        [Fact]
        public void Writer_system_passes_without_any_lookup()
        {
            var svc = new FakeOrganizationService();
            var context = new FakePluginExecutionContext
            {
                UserId = FakeOrganizationService.SystemUserId,
                InitiatingUserId = FakeOrganizationService.SystemUserId,
            };

            OutcomeGuardPlugin.Guard(svc, context, RegradeWrite());
        }

        [Fact]
        public void A_write_that_touches_no_guarded_column_is_not_read()
        {
            var svc = new FakeOrganizationService();
            var initialOnly = new Entity("al_outcome", OutcomeId)
            {
                [Outcomes.InitialOutcomeAttr] = new OptionSetValue(OutcomeRules.OutcomePass),
            };

            OutcomeGuardPlugin.Guard(svc, new FakePluginExecutionContext(), initialOnly);
            Assert.Equal(0, svc.RetrieveCount);
        }

        [Fact]
        public void The_outcome_guard_runs_through_the_pipeline()
        {
            var svc = OutcomeStored();
            var provider = Provider(svc, "Update", "al_outcome", RegradeWrite());
            Person(svc, provider.Context.UserId, Role);

            var message = Refused(() => new OutcomeGuardPlugin(null, null).Execute(provider));
            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, message, StringComparison.Ordinal);
        }

        // ---- 1e: a submitted review stays submitted ---------------------------------------

        private static Entity Review(int status, DateTime? submittedOn)
        {
            var review = new Entity("al_reviewinstance", ReviewId) { ["al_reviewstatus"] = new OptionSetValue(status) };
            if (submittedOn.HasValue)
            {
                review["al_submittedon"] = submittedOn.Value;
            }

            return review;
        }

        private static readonly DateTime Stamp = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Reopening_a_submitted_review_directly_is_refused()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewinstance", ReviewId,
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusSubmitted), "al_submittedon", Stamp);
            var provider = Provider(svc, "Update", "al_reviewinstance", new Entity("al_reviewinstance", ReviewId)
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusInProgress),
            });

            var message = Refused(() => new ReviewSubmissionGuardPlugin(null, null).Execute(provider));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, message, StringComparison.Ordinal);
        }

        [Fact]
        public void System_may_reopen_a_submitted_review()
        {
            var svc = new FakeOrganizationService();
            var provider = Provider(svc, "Update", "al_reviewinstance", new Entity("al_reviewinstance", ReviewId)
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusInProgress),
                ["al_submittedon"] = null,
            });
            provider.Context.UserId = FakeOrganizationService.SystemUserId;
            provider.Context.InitiatingUserId = FakeOrganizationService.SystemUserId;

            new ReviewSubmissionGuardPlugin(null, null).Execute(provider);
        }

        [Fact]
        public void Clearing_or_moving_the_submission_date_is_refused()
        {
            var stored = Review(ResponseRules.StatusSubmitted, Stamp);

            var cleared = new Entity("al_reviewinstance", ReviewId) { ["al_submittedon"] = null };
            var moved = new Entity("al_reviewinstance", ReviewId) { ["al_submittedon"] = Stamp.AddDays(-3) };

            Assert.NotNull(ReviewSubmissionGuardPlugin.Refusal(cleared, stored));
            Assert.NotNull(ReviewSubmissionGuardPlugin.Refusal(moved, stored));
        }

        [Fact]
        public void The_writers_of_review_status_are_not_refused()
        {
            // SubmitReviewPlugin: Assigned or In progress to Submitted, stamped once.
            Assert.Null(ReviewSubmissionGuardPlugin.Refusal(
                Review(ResponseRules.StatusSubmitted, Stamp), Review(ResponseRules.StatusInProgress, null)));

            // ResponseProgressPlugin: the first answer moves Assigned to In progress.
            Assert.Null(ReviewSubmissionGuardPlugin.Refusal(
                Review(ResponseRules.StatusInProgress, null), Review(ResponseRules.StatusAssigned, null)));

            // AssignCasePlugin and ClaimCasePlugin stamp Assigned on an unsubmitted check.
            Assert.Null(ReviewSubmissionGuardPlugin.Refusal(
                Review(ResponseRules.StatusAssigned, null), Review(ResponseRules.StatusInProgress, null)));

            // Restating Submitted with the same date changes nothing.
            Assert.Null(ReviewSubmissionGuardPlugin.Refusal(
                Review(ResponseRules.StatusSubmitted, Stamp), Review(ResponseRules.StatusSubmitted, Stamp)));
        }

        // ---- 6: a parked accountability choice obeys the command's rule -------------------

        private static Entity ParkedReview()
        {
            return new Entity("al_reviewinstance", ReviewId)
            {
                ["al_pendingaccountability"] = "{\"fqAdviser\":true,\"fqParaplanner\":false,\"aqAdviser\":true,\"aqParaplanner\":false,\"fqContactId\":\"\",\"aqContactId\":\"\"}",
            };
        }

        private static Entity NewOutcome(int grade)
        {
            return new Entity("al_outcome")
            {
                [Outcomes.InitialOutcomeAttr] = new OptionSetValue(grade),
                ["al_outcomecaseid"] = new EntityReference("al_outcomecase", CaseId),
            };
        }

        [Fact]
        public void A_parked_choice_on_a_pass_is_consumed_but_not_applied()
        {
            // The command refuses "there must be a fail"; the parked route used to write it.
            var outcome = NewOutcome(OutcomeRules.OutcomePass);

            Assert.NotNull(SetFailAccountabilityPlugin.RefusalFor(OutcomeRules.OutcomePass, false));
            Assert.True(AccountabilityRequestPlugin.ApplyParked(new FakeOrganizationService(), ParkedReview(), outcome));
            Assert.False(outcome.Contains("al_fqadviseraccountable"));
            Assert.False(outcome.Contains("al_aqadviseraccountable"));
        }

        [Fact]
        public void A_parked_choice_on_a_fail_is_applied()
        {
            var outcome = NewOutcome(OutcomeRules.OutcomeInsufficient);

            Assert.True(AccountabilityRequestPlugin.ApplyParked(new FakeOrganizationService(), ParkedReview(), outcome));
            Assert.True(outcome.GetAttributeValue<bool>("al_fqadviseraccountable"));
            Assert.True(outcome.GetAttributeValue<bool>("al_aqadviseraccountable"));
        }

        // ---- 7: the portal completion is traced like the command --------------------------

        [Fact]
        public void Every_plugin_that_calls_complete_passes_a_tracer()
        {
            var callers = Directory.GetFiles(RepoPath("plugins", "OutcomeTesting.Plugins"), "*Plugin.cs")
                .Select(path => new { path, source = File.ReadAllText(path) })
                .Where(f => f.source.Contains("CompleteRemediationPlugin.Complete("))
                .ToList();

            Assert.Contains(callers, f => Path.GetFileName(f.path) == "CompleteRequestPlugin.cs");
            foreach (var file in callers)
            {
                var start = file.source.IndexOf("CompleteRemediationPlugin.Complete(", StringComparison.Ordinal);
                var call = file.source.Substring(start, file.source.IndexOf(");", start, StringComparison.Ordinal) - start);
                Assert.True(call.Contains("trace:"), Path.GetFileName(file.path) + " calls Complete without a tracer.");
            }
        }

        // ---- step registration -----------------------------------------------------------

        [Theory]
        [InlineData("{891b853d-57e2-f111-84e8-70a8a5b3561e}.xml", "OutcomeGuardPlugin", "al_outcome")]
        [InlineData("{4fcab04a-57e2-f111-84e8-70a8a5b3561e}.xml", "ReviewSubmissionGuardPlugin", "al_reviewinstance")]
        public void The_new_guards_are_registered_pre_operation_on_their_columns(string file, string type, string entity)
        {
            var step = XDocument.Load(RepoPath("src", "SdkMessageProcessingSteps", file)).Root;
            Assert.Equal("20", step.Element("Stage").Value);
            Assert.Equal("0", step.Element("Mode").Value);
            Assert.Equal(entity, step.Element("PrimaryEntity").Value);
            Assert.StartsWith("OutcomeTesting.Plugins." + type + ",", step.Element("PluginTypeName").Value, StringComparison.Ordinal);

            var typeId = step.Element("PluginTypeId").Value;
            var assembly = Directory.GetFiles(RepoPath("src", "PluginAssemblies"), "OutcomeTestingPlugins.dll.data.xml", SearchOption.AllDirectories).Single();
            Assert.Contains(
                XDocument.Load(assembly).Descendants("PluginType"),
                t => (string)t.Attribute("PluginTypeId") == typeId && (string)t.Attribute("Name") == "OutcomeTesting.Plugins." + type);

            var filtered = step.Element("FilteringAttributes").Value.Split(',');
            var expected = type == "OutcomeGuardPlugin"
                ? OutcomeGuardPlugin.FinalGradeColumns.Concat(OutcomeGuardPlugin.AccountabilityColumns).ToArray()
                : new[] { "al_reviewstatus", "al_submittedon", "al_pendingaccountability" };
            Assert.Equal(expected.OrderBy(c => c), filtered.OrderBy(c => c));
        }

        [Theory]
        [InlineData("{a42cdd96-64e2-f111-84e8-70a8a5b3561e}.xml", "OutcomeGuardPlugin", "Create", "al_outcome", "")]
        [InlineData("{f668c412-0d81-48e1-8aca-7940214a5bed}.xml", "ReviewSubmissionGuardPlugin", "Create", "al_reviewinstance", "")]
        [InlineData("{be626991-eb41-4467-80cb-6913ee82ad85}.xml", "ClaimCasePlugin", "Update", "al_caseassignment",
            "al_assigneduserid,al_assignedcontactid,al_reviewinstanceid,al_outcomecaseid,al_isactive")]
        public void The_second_pass_guard_steps_are_registered_and_in_the_solution(
            string file, string type, string message, string entity, string filtering)
        {
            var step = XDocument.Load(RepoPath("src", "SdkMessageProcessingSteps", file)).Root;
            Assert.Equal("20", step.Element("Stage").Value);
            Assert.Equal("0", step.Element("Mode").Value);
            Assert.Equal(entity, step.Element("PrimaryEntity").Value);
            Assert.Equal(type + ": " + message + " of " + entity, (string)step.Attribute("Name"));
            Assert.Equal(message == "Create" ? "9ebdbb1b-ea3e-db11-86a7-000a3a5473e8" : "20bebb1b-ea3e-db11-86a7-000a3a5473e8",
                step.Element("SdkMessageId").Value);
            Assert.Equal("Enabled", step.Element("StateCode").Value);
            Assert.StartsWith("OutcomeTesting.Plugins." + type + ",", step.Element("PluginTypeName").Value, StringComparison.Ordinal);
            Assert.Equal(filtering, step.Element("FilteringAttributes").Value);
            if (type == "ClaimCasePlugin")
            {
                Assert.Equal(ClaimCasePlugin.AllocationColumns.OrderBy(c => c), filtering.Split(',').OrderBy(c => c));
            }

            var typeId = step.Element("PluginTypeId").Value;
            var assembly = Directory.GetFiles(RepoPath("src", "PluginAssemblies"), "OutcomeTestingPlugins.dll.data.xml", SearchOption.AllDirectories).Single();
            Assert.Contains(
                XDocument.Load(assembly).Descendants("PluginType"),
                t => (string)t.Attribute("PluginTypeId") == typeId && (string)t.Attribute("Name") == "OutcomeTesting.Plugins." + type);

            var stepId = (string)step.Attribute("SdkMessageProcessingStepId");
            Assert.Contains(
                XDocument.Load(RepoPath("src", "Other", "Solution.xml")).Descendants("RootComponent"),
                c => (string)c.Attribute("type") == "92" && (string)c.Attribute("id") == stepId);
        }

        private static string RepoPath(params string[] parts)
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile()), "..", ".."));
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }

        private static string ThisFile([CallerFilePath] string path = null)
        {
            return path;
        }
    }
}
