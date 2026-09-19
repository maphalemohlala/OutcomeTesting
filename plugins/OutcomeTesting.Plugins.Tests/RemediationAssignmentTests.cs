using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Remediation follows the adviser named on the case. That covers two repairs: an
    /// action raised unassigned because the name matched no contact, and an action left
    /// with the previous adviser when the case was reassigned. Completed actions, and
    /// actions the named adviser already holds, stay where they are.
    /// </summary>
    public class RemediationAssignmentTests
    {
        private static readonly Guid CaseId = Guid.Parse("0a0a0a0a-1b1b-4c1c-8d1d-0e0e0e0e0e0e");
        private static readonly Guid AdviserId = Guid.Parse("1f1f1f1f-2a2a-4b2b-8c2c-1d1d1d1d1d1d");
        private static readonly Guid Correlation = Guid.NewGuid();

        private static FakeOrganizationService Case(string adviserName)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId, "al_casereference", "IO-TEST-009", "al_advisername", adviserName);
            svc.Seed("contact", AdviserId, "fullname", "Sam Adviser", "emailaddress1", "sam@example.com", "statecode", new OptionSetValue(0));
            return svc;
        }

        private static Guid Action(FakeOrganizationService svc, int status, Guid? assignedTo = null)
        {
            var action = svc.Seed(
                "al_remediationaction",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_actionstatus", new OptionSetValue(status),
                "al_name", "Remediation IO-TEST-009");
            if (assignedTo.HasValue)
            {
                action["al_assignedcontactid"] = new EntityReference("contact", assignedTo.Value);
            }

            return action.Id;
        }

        private static EntityReference Ref()
        {
            return new EntityReference("al_outcomecase", CaseId);
        }

        [Fact]
        public void Assigns_an_open_unassigned_action_to_the_adviser_now_named()
        {
            var svc = Case("Sam Adviser");
            var actionId = Action(svc, Remediation.StatusOpen);

            var count = Remediation.AssignOpenActions(svc, Ref(), Correlation);

            Assert.Equal(1, count);
            Assert.Equal(AdviserId, svc.Row("al_remediationaction", actionId)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void Tells_the_adviser_they_now_hold_it()
        {
            var svc = Case("Sam Adviser");
            Action(svc, Remediation.StatusOpen);

            Remediation.AssignOpenActions(svc, Ref(), Correlation);

            var queued = svc.Creates.Single(c => c.Contains("al_event"));
            Assert.Equal(NotificationOutbox.EventRemediationAssigned, queued.GetAttributeValue<OptionSetValue>("al_event").Value);
            Assert.Equal("sam@example.com", queued.GetAttributeValue<string>("al_recipientemail"));
        }

        [Fact]
        public void Moves_an_action_held_by_the_previous_adviser_to_the_one_now_named()
        {
            // Reassigning the case is how remediation is reassigned: the work follows the
            // adviser named on it, or it strands on someone who has left the case.
            var previous = Guid.NewGuid();
            var svc = Case("Sam Adviser");
            var actionId = Action(svc, Remediation.StatusOpen, assignedTo: previous);

            Assert.Equal(1, Remediation.AssignOpenActions(svc, Ref(), Correlation));
            Assert.Equal(AdviserId, svc.Row("al_remediationaction", actionId)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void Leaves_an_action_the_named_adviser_already_holds_alone()
        {
            // An edit that does not change the adviser must not rewrite the row or tell them
            // a second time about work they already have.
            var svc = Case("Sam Adviser");
            Action(svc, Remediation.StatusOpen, assignedTo: AdviserId);

            Assert.Equal(0, Remediation.AssignOpenActions(svc, Ref(), Correlation));
            Assert.Empty(svc.Updates);
            Assert.DoesNotContain(svc.Creates, c => c.Contains("al_event"));
        }

        [Fact]
        public void Leaves_a_completed_action_with_the_adviser_who_did_the_work()
        {
            // BR-007: a completed action is history. Reassigning the case does not rewrite
            // who answered the ones already answered.
            var previous = Guid.NewGuid();
            var svc = Case("Sam Adviser");
            var actionId = Action(svc, Remediation.StatusCompleted, assignedTo: previous);

            Assert.Equal(0, Remediation.AssignOpenActions(svc, Ref(), Correlation));
            Assert.Equal(previous, svc.Row("al_remediationaction", actionId)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void Leaves_a_completed_action_alone()
        {
            var svc = Case("Sam Adviser");
            Action(svc, Remediation.StatusCompleted);

            Assert.Equal(0, Remediation.AssignOpenActions(svc, Ref(), Correlation));
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void Does_nothing_while_the_name_still_matches_no_contact()
        {
            var svc = Case("Nobody Known");
            Action(svc, Remediation.StatusOpen);

            Assert.Equal(0, Remediation.AssignOpenActions(svc, Ref(), Correlation));
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void Refuses_to_guess_between_two_contacts_with_the_same_name()
        {
            var svc = Case("Sam Adviser");
            svc.Seed("contact", Guid.NewGuid(), "fullname", "Sam Adviser", "emailaddress1", "sam2@example.com", "statecode", new OptionSetValue(0));
            Action(svc, Remediation.StatusOpen);

            Assert.Equal(0, Remediation.AssignOpenActions(svc, Ref(), Correlation));
        }
    }
}
