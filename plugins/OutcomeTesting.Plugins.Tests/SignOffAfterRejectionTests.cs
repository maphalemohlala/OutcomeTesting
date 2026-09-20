using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A rejected remediation can be decided again once the adviser has reworked it.
    ///
    /// <para>
    /// F39, found in DEV on 2026-09-20 working APP-067 and APP-072. This is e4e4ff3's bug,
    /// in the place e4e4ff3 did not reach. That commit - "a rejection starts the loop, it
    /// does not end it" - fixed <see cref="SignoffGuardPlugin.AlreadySettled"/> to count
    /// APPROVED sign-offs only, because refusing on any sign-off at all made every rejection
    /// terminal: the action reopens, the adviser reworks it, the case returns to Awaiting
    /// Sign-off, and the supervisor is refused the decision that would close it. Two real
    /// cases were stuck that way.
    /// </para>
    /// <para>
    /// <c>al_SignOffRemediation</c> carries its own copy of the rule, and its copy was never
    /// changed: <c>FindSignoff(...) != null</c> refuses on any active sign-off, rejections
    /// included, and it runs before the guard ever sees the create. So the command path still
    /// ends the loop it is supposed to start. Live in DEV on case 900000006: rejected,
    /// reworked, completed, and then "PRECONDITION: This remediation action has already been
    /// signed off. Reopening a completed sign-off is a privileged correction (AD-031)."
    /// </para>
    /// <para>
    /// The guard's six tests all passed, because they exercise the guard. Nothing asked the
    /// command the same question.
    /// </para>
    /// </summary>
    public class SignOffAfterRejectionTests
    {
        private static readonly Guid ActionId = Guid.Parse("77777777-aaaa-4aaa-8aaa-777777777777");
        private static readonly Guid CaseId = Guid.Parse("88888888-bbbb-4bbb-8bbb-888888888888");

        private const int StatusCompleted = 120910602;
        private const int Approved = 120910720;
        private const int Rejected = 120910721;

        private static FakeOrganizationService CompletedAction()
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                "al_remediationaction",
                ActionId,
                "al_actionstatus", new OptionSetValue(StatusCompleted),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            return svc;
        }

        /// <summary>A decision already recorded against the action, as the command would leave it.</summary>
        private static Guid Decided(FakeOrganizationService svc, int decision, DateTime signedOffOn)
        {
            return svc.Seed(
                "al_signoff",
                Guid.NewGuid(),
                "al_remediationactionid", new EntityReference("al_remediationaction", ActionId),
                "al_signoffdecision", new OptionSetValue(decision),
                "al_signedoffon", signedOffOn,
                "statecode", new OptionSetValue(0)).Id;
        }

        private static int SignoffCount(FakeOrganizationService svc)
        {
            var query = new QueryExpression("al_signoff")
            {
                ColumnSet = new ColumnSet(false),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(
                "al_remediationactionid", ConditionOperator.Equal, ActionId);

            return svc.RetrieveMultiple(query).Entities.Count;
        }

        /// <summary>
        /// The one that was failing. A rejection sends the work back; the decision that
        /// follows the rework is the one that finishes the case.
        /// </summary>
        [Fact]
        public void A_rejected_action_can_be_approved_once_it_has_been_reworked()
        {
            var svc = CompletedAction();
            Decided(svc, Rejected, new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc));

            SignOffRemediationPlugin.CreateSignoff(
                svc, svc, ActionId, "Approved", "Both points now evidenced.", null);

            Assert.Equal(2, SignoffCount(svc));
        }

        /// <summary>
        /// Two rounds is a real outcome, not an abuse: the adviser can miss the point twice.
        /// </summary>
        [Fact]
        public void A_rejected_action_can_be_rejected_again()
        {
            var svc = CompletedAction();
            Decided(svc, Rejected, new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc));

            SignOffRemediationPlugin.CreateSignoff(
                svc, svc, ActionId, "Rejected", "The ATR discussion is still not on the file.", null);

            Assert.Equal(2, SignoffCount(svc));
        }

        /// <summary>
        /// And the rule the refusal was reaching for is kept: overriding an APPROVED sign-off
        /// is a privileged correction (AD-031), not something the ordinary command does.
        /// </summary>
        [Fact]
        public void An_approved_action_still_refuses_a_second_decision()
        {
            var svc = CompletedAction();
            Decided(svc, Approved, new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc));

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => SignOffRemediationPlugin.CreateSignoff(
                    svc, svc, ActionId, "Rejected", "Changed my mind.", null));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal.Message);
            Assert.Contains("approved", refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, SignoffCount(svc));
        }

        /// <summary>
        /// Once an action can carry two decisions, "the sign-off on this action" stops being
        /// one row. The idempotent replay path reads it to report what was recorded, so it
        /// has to be the latest decision rather than whichever the platform returns first -
        /// otherwise a replayed approval can answer with the rejection it superseded.
        /// </summary>
        [Fact]
        public void The_decision_read_back_is_the_latest_one()
        {
            var svc = CompletedAction();
            Decided(svc, Rejected, new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc));
            var second = Decided(svc, Approved, new DateTime(2026, 9, 20, 14, 0, 0, DateTimeKind.Utc));

            var found = SignOffRemediationPlugin.FindSignoff(svc, ActionId);

            Assert.NotNull(found);
            Assert.Equal(second, found.Id);
            Assert.Equal(
                Approved,
                found.GetAttributeValue<OptionSetValue>("al_signoffdecision").Value);
        }
    }
}
