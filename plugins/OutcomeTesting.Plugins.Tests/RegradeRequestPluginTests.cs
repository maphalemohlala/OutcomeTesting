using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The final regraded outcome recorded from the portal through the contact's request
    /// column (OD-041, AD-031, AD-099's pattern): the supervisor role is checked against the
    /// platform, the shared RegradeCasePlugin.Regrade does the work, and the initial outcome
    /// is preserved (BR-007).
    /// </summary>
    public class RegradeRequestPluginTests
    {
        private static readonly Guid ContactId = Guid.Parse("dddddddd-9999-4999-8999-999999999999");
        private static readonly Guid OutcomeId = Guid.Parse("eeeeeeee-9999-4999-8999-999999999999");
        private static readonly Guid CaseId = Guid.Parse("ffffffff-9999-4999-8999-999999999999");

        private static FakePluginExecutionContext Context()
        {
            return new FakePluginExecutionContext
            {
                InitiatingUserId = Guid.Parse("11111111-9999-4999-8999-999999999999"),
                CorrelationId = Guid.Parse("22222222-9999-4999-8999-999999999999"),
            };
        }

        private static FakeOrganizationService Holding(int caseStatus, params string[] roleNames)
        {
            var svc = new FakeOrganizationService();

            // The row the page wrote, which the plug-in clears back; the fake refuses an
            // update on a row it has never seen.
            svc.Seed("contact", ContactId, RegradeRequestPlugin.RequestAttr, "{}", "fullname", "Sam Supervisor");

            svc.Seed("al_outcomecase", CaseId, "al_casestatus", new OptionSetValue(caseStatus));

            svc.Seed(
                "al_outcome", OutcomeId,
                "al_initialoutcome", new OptionSetValue(120910712),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));

            var rows = new List<Entity>();
            foreach (var name in roleNames)
            {
                var row = new Entity("contact", ContactId);
                row["role.name"] = new AliasedValue("powerpagecomponent", "name", name);
                rows.Add(row);
            }

            svc.FetchResults.Enqueue(new EntityCollection(rows));
            return svc;
        }

        private static string Request(string finalOutcome, string reason)
        {
            return "{\"outcomeId\":\"" + OutcomeId.ToString("D") + "\""
                + ",\"finalOutcome\":\"" + finalOutcome + "\""
                + (reason == null ? string.Empty : ",\"reason\":\"" + reason + "\"")
                + "}";
        }

        private static Entity Outcome(FakeOrganizationService svc)
        {
            return svc.Row("al_outcome", OutcomeId);
        }

        [Fact]
        public void Records_the_final_outcome_and_the_reason()
        {
            var svc = Holding(CaseLifecycle.AwaitingRecheck, WebRoleRegistry.TcSupervisorRole);

            var result = RegradeRequestPlugin.Apply(
                svc, ContactId, Request("Pass with issues", "Evidence supplied on recheck."), Context());

            Assert.Equal("Pass with issues", result.FinalOutcome);
            Assert.Equal(120910711, Outcome(svc).GetAttributeValue<OptionSetValue>("al_finaloutcome").Value);
            Assert.Equal("Evidence supplied on recheck.", Outcome(svc).GetAttributeValue<string>("al_regradereason"));
        }

        [Fact]
        public void Never_touches_the_initial_outcome()
        {
            // BR-007: both grades survive, which is the whole point of a separate final.
            var svc = Holding(CaseLifecycle.AwaitingRecheck, WebRoleRegistry.TcSupervisorRole);

            RegradeRequestPlugin.Apply(svc, ContactId, Request("Pass", "Regraded after remediation."), Context());

            Assert.Equal(120910712, Outcome(svc).GetAttributeValue<OptionSetValue>("al_initialoutcome").Value);
        }

        [Fact]
        public void Closes_a_case_that_was_awaiting_recheck()
        {
            var svc = Holding(CaseLifecycle.AwaitingRecheck, WebRoleRegistry.TcSupervisorRole);

            RegradeRequestPlugin.Apply(svc, ContactId, Request("Pass", "Regraded after remediation."), Context());

            Assert.Equal(
                CaseLifecycle.Closed,
                svc.Row("al_outcomecase", CaseId).GetAttributeValue<OptionSetValue>("al_casestatus").Value);
        }

        [Fact]
        public void Clears_the_request_column()
        {
            var svc = Holding(CaseLifecycle.AwaitingRecheck, WebRoleRegistry.TcSupervisorRole);

            RegradeRequestPlugin.Apply(svc, ContactId, Request("Pass", "Regraded after remediation."), Context());

            Assert.Null(svc.Row("contact", ContactId).GetAttributeValue<string>(RegradeRequestPlugin.RequestAttr));
        }

        [Fact]
        public void Refuses_a_contact_without_the_supervisor_role_and_writes_nothing()
        {
            // The contact column allowlist is shared with the reviewer roles' claim request,
            // so the role check has to be here rather than in a table permission.
            var svc = Holding(CaseLifecycle.AwaitingRecheck, WebRoleRegistry.AqsReviewerRole);

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => RegradeRequestPlugin.Apply(svc, ContactId, Request("Pass", "Looks fine to me."), Context()));

            Assert.Contains(WebRoleRegistry.TcSupervisorRole, ex.Message);
            Assert.False(Outcome(svc).Contains("al_finaloutcome"));
        }

        [Fact]
        public void Refuses_a_regrade_carrying_no_reason()
        {
            // AD-031 makes the reason mandatory, and the refusal must land before the
            // clear-down so the request is not silently swallowed.
            var svc = Holding(CaseLifecycle.AwaitingRecheck, WebRoleRegistry.TcSupervisorRole);

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => RegradeRequestPlugin.Apply(svc, ContactId, Request("Pass", null), Context()));

            Assert.Contains("why the outcome was changed", ex.Message);
            Assert.False(Outcome(svc).Contains("al_finaloutcome"));
        }

        [Fact]
        public void Refuses_a_grade_outside_the_four_br005_outcomes()
        {
            var svc = Holding(CaseLifecycle.AwaitingRecheck, WebRoleRegistry.TcSupervisorRole);

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => RegradeRequestPlugin.Apply(svc, ContactId, Request("Excellent", "Best file I have read."), Context()));

            Assert.Contains("Pass with issues", ex.Message);
        }

        [Fact]
        public void Refuses_a_request_naming_no_outcome()
        {
            var svc = Holding(CaseLifecycle.AwaitingRecheck, WebRoleRegistry.TcSupervisorRole);

            Assert.Throws<InvalidPluginExecutionException>(
                () => RegradeRequestPlugin.Apply(svc, ContactId, "{\"finalOutcome\":\"Pass\",\"reason\":\"x\"}", Context()));
        }

        [Fact]
        public void Refuses_an_outcome_that_was_never_graded()
        {
            // A final with no initial to preserve against breaks BR-007 from the other side.
            var svc = Holding(CaseLifecycle.AwaitingRecheck, WebRoleRegistry.TcSupervisorRole);
            Outcome(svc).Attributes.Remove("al_initialoutcome");

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => RegradeRequestPlugin.Apply(svc, ContactId, Request("Pass", "Regraded."), Context()));

            Assert.Contains("no initial outcome", ex.Message);
        }

        [Fact]
        public void A_replay_of_the_same_regrade_does_not_write_a_second_audit_event()
        {
            var svc = Holding(CaseLifecycle.AwaitingRecheck, WebRoleRegistry.TcSupervisorRole);
            var first = RegradeRequestPlugin.Apply(
                svc, ContactId, Request("Pass", "Regraded after remediation."), Context());

            // The page reloads and sends the same request again; the derived key is stable
            // per outcome, so the original regrade is returned rather than repeated.
            svc.Seed("contact", ContactId, RegradeRequestPlugin.RequestAttr, "{}", "fullname", "Sam Supervisor");
            svc.FetchResults.Enqueue(new EntityCollection(new List<Entity>
            {
                RoleRow(WebRoleRegistry.TcSupervisorRole),
            }));

            var second = RegradeRequestPlugin.Apply(
                svc, ContactId, Request("Pass", "Regraded after remediation."), Context());

            Assert.Equal(first.AuditEventId, second.AuditEventId);
        }

        private static Entity RoleRow(string name)
        {
            var row = new Entity("contact", ContactId);
            row["role.name"] = new AliasedValue("powerpagecomponent", "name", name);
            return row;
        }
    }
}
