using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// An action raised unassigned — the adviser's name on the case matched no contact —
    /// is picked up by the adviser once the case names one that does. Correcting the name
    /// is the repair; this is what makes the correction reach the action.
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

            var count = Remediation.AssignUnassignedActions(svc, Ref(), Correlation);

            Assert.Equal(1, count);
            Assert.Equal(AdviserId, svc.Row("al_remediationaction", actionId)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void Tells_the_adviser_they_now_hold_it()
        {
            var svc = Case("Sam Adviser");
            Action(svc, Remediation.StatusOpen);

            Remediation.AssignUnassignedActions(svc, Ref(), Correlation);

            var queued = svc.Creates.Single(c => c.Contains("al_event"));
            Assert.Equal(NotificationOutbox.EventRemediationAssigned, queued.GetAttributeValue<OptionSetValue>("al_event").Value);
            Assert.Equal("sam@example.com", queued.GetAttributeValue<string>("al_recipientemail"));
        }

        [Fact]
        public void Leaves_an_action_that_already_has_an_adviser_alone()
        {
            // A header edit is not a reassignment: someone already holds this one.
            var other = Guid.NewGuid();
            var svc = Case("Sam Adviser");
            var actionId = Action(svc, Remediation.StatusOpen, assignedTo: other);

            Assert.Equal(0, Remediation.AssignUnassignedActions(svc, Ref(), Correlation));
            Assert.Equal(other, svc.Row("al_remediationaction", actionId)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void Leaves_a_completed_action_alone()
        {
            var svc = Case("Sam Adviser");
            Action(svc, Remediation.StatusCompleted);

            Assert.Equal(0, Remediation.AssignUnassignedActions(svc, Ref(), Correlation));
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void Does_nothing_while_the_name_still_matches_no_contact()
        {
            var svc = Case("Nobody Known");
            Action(svc, Remediation.StatusOpen);

            Assert.Equal(0, Remediation.AssignUnassignedActions(svc, Ref(), Correlation));
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void Refuses_to_guess_between_two_contacts_with_the_same_name()
        {
            var svc = Case("Sam Adviser");
            svc.Seed("contact", Guid.NewGuid(), "fullname", "Sam Adviser", "emailaddress1", "sam2@example.com", "statecode", new OptionSetValue(0));
            Action(svc, Remediation.StatusOpen);

            Assert.Equal(0, Remediation.AssignUnassignedActions(svc, Ref(), Correlation));
        }
    }
}
