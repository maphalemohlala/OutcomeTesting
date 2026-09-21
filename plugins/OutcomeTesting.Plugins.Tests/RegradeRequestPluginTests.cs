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
        private const string Adviser = "adviser@example.com";

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
            return Holding(caseStatus, ContactId, roleNames);
        }

        /// <summary>
        /// A supervisor who may regrade THIS case: they hold the role and they are the T&amp;C
        /// Manager mapped to its adviser. Both are required from 2026-09-21 (AD-202), so the
        /// fixture carries the whole chain - outcome, case, adviser email, mapping - and the
        /// tests that vary one link are the interesting ones.
        ///
        /// <paramref name="mappedManager"/> of <see cref="Guid.Empty"/> seeds no mapping at
        /// all, which is the adviser nobody supervises.
        /// </summary>
        private static FakeOrganizationService Holding(
            int caseStatus, Guid mappedManager, params string[] roleNames)
        {
            var svc = new FakeOrganizationService();

            // The row the page wrote, which the plug-in clears back; the fake refuses an
            // update on a row it has never seen.
            svc.Seed("contact", ContactId, RegradeRequestPlugin.RequestAttr, "{}", "fullname", "Sam Supervisor");

            svc.Seed("al_outcomecase", CaseId,
                "al_casestatus", new OptionSetValue(caseStatus),
                TcManagerRouting.CaseAdviserEmailAttr, Adviser);

            svc.Seed(
                "al_outcome", OutcomeId,
                "al_initialoutcome", new OptionSetValue(120910712),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));

            if (mappedManager != Guid.Empty)
            {
                if (mappedManager != ContactId)
                {
                    // TcManagerRouting reads the manager's work email to decide whether they
                    // are reachable, so the row has to exist for the routing to resolve.
                    svc.Seed("contact", mappedManager,
                        "fullname", "Pat Manager", "emailaddress1", "pat@example.com");
                }

                svc.Seed(TcManagerRouting.MappingEntity, Guid.NewGuid(),
                    TcManagerRouting.MappingEmailAttr, Adviser,
                    TcManagerRouting.ManagerAttr, new EntityReference("contact", mappedManager));
            }

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

        [Fact]
        public void A_second_regrade_to_a_different_grade_is_recorded_rather_than_replayed()
        {
            var svc = Holding(CaseLifecycle.AwaitingRecheck, WebRoleRegistry.TcSupervisorRole);
            var first = RegradeRequestPlugin.Apply(
                svc, ContactId, Request("Pass", "Regraded after remediation."), Context());

            // The supervisor comes back and corrects the grade (AD-031). That is a new
            // intent, not a replay of the first: it has to be written and audited, not
            // answered with the first regrade's result while the page reports success.
            svc.Seed("contact", ContactId, RegradeRequestPlugin.RequestAttr, "{}", "fullname", "Sam Supervisor");
            svc.FetchResults.Enqueue(new EntityCollection(new List<Entity>
            {
                RoleRow(WebRoleRegistry.TcSupervisorRole),
            }));

            var second = RegradeRequestPlugin.Apply(
                svc, ContactId, Request("Pass with issues", "Corrected: one issue was left open."), Context());

            Assert.NotEqual(first.AuditEventId, second.AuditEventId);
            Assert.Equal("Pass with issues", second.FinalOutcome);
            Assert.Equal(
                OutcomeRules.FinalOutcomePassWithIssues,
                svc.Row("al_outcome", OutcomeId).GetAttributeValue<OptionSetValue>("al_finaloutcome").Value);
        }

        [Theory]
        [InlineData(CaseLifecycle.Submitted)]
        [InlineData(CaseLifecycle.AwaitingRemediation)]
        [InlineData(CaseLifecycle.RemediationInProgress)]
        [InlineData(CaseLifecycle.AwaitingSignoff)]
        public void A_final_outcome_before_the_case_reaches_the_recheck_is_refused(int status)
        {
            // The final outcome IS the recheck decision (AD-057), and a case still in
            // remediation has nothing to recheck: the remedial actions are open and no
            // supervisor has attested to any of them. Reported on case 254398988, which
            // took a Pass while six actions sat unanswered at Awaiting Remediation.
            var svc = Holding(status, WebRoleRegistry.TcSupervisorRole);

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => RegradeRequestPlugin.Apply(svc, ContactId, Request("Pass", "Regraded."), Context()));

            Assert.Contains("Awaiting Recheck", ex.Message);
            Assert.Contains(CaseLifecycle.NameOf(status), ex.Message);
            Assert.Null(svc.Row("al_outcome", OutcomeId).GetAttributeValue<OptionSetValue>("al_finaloutcome"));
        }

        [Fact]
        public void A_closed_case_can_still_be_corrected()
        {
            // AD-031's privileged correction: the case is finished and the grade was wrong.
            // The guard above must not take this away.
            var svc = Holding(CaseLifecycle.Closed, WebRoleRegistry.TcSupervisorRole);

            var result = RegradeRequestPlugin.Apply(
                svc, ContactId, Request("Pass with issues", "Corrected after review."), Context());

            Assert.Equal("Pass with issues", result.FinalOutcome);
        }

        private static Entity RoleRow(string name)
        {
            var row = new Entity("contact", ContactId);
            row["role.name"] = new AliasedValue("powerpagecomponent", "name", name);
            return row;
        }
        /// <summary>
        /// AD-202. Holding the supervisor role was the ONLY thing this command checked, so
        /// anybody holding it could record the final outcome on any case - including their own
        /// cases, and including the cases whose sign-off they had just been refused. Found on
        /// the DEV portal: the Regraded outcome form rendered on two cases whose mapped
        /// manager was somebody else, while the sign-off form on the same pages was withheld.
        /// </summary>
        [Fact]
        public void A_supervisor_who_is_not_this_case_manager_cannot_record_the_outcome()
        {
            var someoneElse = Guid.Parse("abababab-9999-4999-8999-999999999999");
            var svc = Holding(120910590, someoneElse, WebRoleRegistry.TcSupervisorRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => RegradeRequestPlugin.Apply(
                    svc, ContactId, Request("Pass", "Evidence supplied on recheck."), Context()));

            Assert.Contains("Recording the final outcome is the T&C Manager mapped to its adviser", error.Message);
            Assert.Contains("not the T&C Manager mapped to this case's adviser", error.Message);
        }

        /// <summary>
        /// And the refusal must not become a directory. Naming the manager would let anyone
        /// holding the role enumerate who supervises whom, which is the same reason the portal
        /// reads al_advisermapping at CONTACT scope (AD-201).
        /// </summary>
        [Fact]
        public void The_refusal_does_not_name_the_manager_it_refused_for()
        {
            var someoneElse = Guid.Parse("abababab-9999-4999-8999-999999999999");
            var svc = Holding(120910590, someoneElse, WebRoleRegistry.TcSupervisorRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => RegradeRequestPlugin.Apply(
                    svc, ContactId, Request("Pass", "Evidence supplied on recheck."), Context()));

            Assert.DoesNotContain("Pat Manager", error.Message);
            Assert.DoesNotContain("pat@example.com", error.Message);
            Assert.DoesNotContain(Adviser, error.Message);
        }

        /// <summary>
        /// An adviser nobody supervises has nobody who may record their final outcome. The
        /// same consequence the sign-off already carried, and the message says where the gap
        /// is because the reader is the person who can have it filled in (F58).
        /// </summary>
        [Fact]
        public void An_adviser_with_no_mapping_leaves_nobody_to_record_the_outcome()
        {
            var svc = Holding(120910590, Guid.Empty, WebRoleRegistry.TcSupervisorRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => RegradeRequestPlugin.Apply(
                    svc, ContactId, Request("Pass", "Evidence supplied on recheck."), Context()));

            Assert.Contains("No T&C Manager is mapped to the adviser", error.Message);
        }

        /// <summary>
        /// Nothing is written when the mapping refuses - not the outcome, and not the
        /// clear-down of the request column, which shares the transaction.
        /// </summary>
        [Fact]
        public void A_refused_regrade_records_no_outcome()
        {
            var someoneElse = Guid.Parse("abababab-9999-4999-8999-999999999999");
            var svc = Holding(120910590, someoneElse, WebRoleRegistry.TcSupervisorRole);

            Assert.Throws<InvalidPluginExecutionException>(
                () => RegradeRequestPlugin.Apply(
                    svc, ContactId, Request("Pass", "Evidence supplied on recheck."), Context()));

            Assert.Null(Outcome(svc).GetAttributeValue<OptionSetValue>("al_finaloutcome"));
        }

        /// <summary>
        /// The role is answered before the mapping. "You do not hold the role" is a different
        /// problem from "you hold it but not over this adviser", and answering the second to
        /// somebody who has neither sends them looking for a mapping they could not use.
        /// </summary>
        [Fact]
        public void The_role_is_refused_before_the_mapping_is_considered()
        {
            var svc = Holding(120910590, Guid.Empty, WebRoleRegistry.AqsReviewerRole);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => RegradeRequestPlugin.Apply(
                    svc, ContactId, Request("Pass", "Evidence supplied on recheck."), Context()));

            Assert.Contains("needs the " + WebRoleRegistry.TcSupervisorRole + " role", error.Message);
            Assert.DoesNotContain("mapped to its adviser", error.Message);
        }

        /// <summary>
        /// An outcome with no case cannot have a manager resolved for it, and says so rather
        /// than failing somewhere less legible.
        /// </summary>
        [Fact]
        public void An_outcome_attached_to_no_case_is_refused()
        {
            var svc = Holding(120910590, ContactId, WebRoleRegistry.TcSupervisorRole);
            svc.Row("al_outcome", OutcomeId).Attributes.Remove("al_outcomecaseid");

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => RegradeRequestPlugin.Apply(
                    svc, ContactId, Request("Pass", "Evidence supplied on recheck."), Context()));

            Assert.Contains("not attached to a case", error.Message);
        }
    }
}
