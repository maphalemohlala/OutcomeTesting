using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The portal signs a check's actions off one at a time under one decision. A rejection
    /// moves the case to Awaiting Remediation on its first action (SignoffProgressPlugin.MoveCase),
    /// so the guard's Awaiting Sign-off check refused the second: TEST case 258841914,
    /// 2026-11-19, "Signing off remedial action 2 of 15 failed. This case is Awaiting
    /// Remediation, not Awaiting Sign-off".
    /// </summary>
    public class SignoffRejectionSittingTests
    {
        private static readonly Guid CaseId = Guid.Parse("12121212-aaaa-4aaa-8aaa-121212121212");
        private static readonly Guid ActionId = Guid.Parse("34343434-bbbb-4bbb-8bbb-343434343434");

        private static void Guard(int caseStatus, int decision)
        {
            var service = new FakeOrganizationService();
            service.Seed("al_outcomecase", CaseId, "al_casestatus", new OptionSetValue(caseStatus));
            service.Seed(
                "al_remediationaction", ActionId,
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));

            var provider = new FakeServiceProvider(service);
            // The portal writes as its application user.
            service.Seed("systemuser", provider.Context.InitiatingUserId, "applicationid", Guid.NewGuid());
            provider.Context.MessageName = "Create";
            provider.Context.PrimaryEntityName = "al_signoff";
            provider.Context.InputParameters["Target"] = new Entity("al_signoff")
            {
                ["al_remediationactionid"] = new EntityReference("al_remediationaction", ActionId),
                ["al_signoffdecision"] = new OptionSetValue(decision),
                ["al_notes"] = "Need to add more to the file around consumer duty",
            };

            new SignoffGuardPlugin(null, null).Execute(provider);
        }

        [Fact]
        public void A_rejection_sitting_records_its_later_actions_after_the_first_moved_the_case()
        {
            Guard(CaseLifecycle.AwaitingRemediation, SignoffProgressPlugin.DecisionRejectedValue);
        }

        [Fact]
        public void An_approval_still_needs_the_case_awaiting_sign_off()
        {
            var refused = Assert.Throws<InvalidPluginExecutionException>(
                () => Guard(CaseLifecycle.AwaitingRemediation, SignoffProgressPlugin.DecisionApprovedValue));
            Assert.Contains("not Awaiting Sign-off", refused.Message);
        }

        [Fact]
        public void A_rejection_still_cannot_reopen_an_action_on_a_closed_case()
        {
            var refused = Assert.Throws<InvalidPluginExecutionException>(
                () => Guard(CaseLifecycle.Closed, SignoffProgressPlugin.DecisionRejectedValue));
            Assert.Contains("not Awaiting Sign-off", refused.Message);
        }
    }
}
