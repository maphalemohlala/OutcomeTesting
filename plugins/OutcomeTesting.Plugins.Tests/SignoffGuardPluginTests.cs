using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Sign-off happens once per <b>decision that ends the loop</b>, not once per action.
    ///
    /// BR-008 sends a rejected remediation back to the adviser, and the rest of the solution
    /// implements that round trip: <see cref="SignoffProgressPlugin"/> reopens the action and
    /// returns the case to Awaiting Remediation, the response guard unlocks the adviser's
    /// answer again, and the sign-off notification is keyed on the sign-off rather than the
    /// action precisely "because an action that goes round twice is genuinely two decisions".
    ///
    /// The guard blocked the second decision anyway, because it asked whether the action had
    /// *any* sign-off rather than an *approved* one. Every rejection in DEV was therefore
    /// terminal: 43 sign-offs across 43 distinct actions and 14 rejections, with not one
    /// action ever reaching a second decision. IO-300003 and IO-300006 sat at Awaiting
    /// Sign-off with the rework done and no way to accept it.
    /// </summary>
    public class SignoffGuardPluginTests
    {
        private static readonly Guid ActionId = Guid.Parse("aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa");

        private const int Approved = SignoffProgressPlugin.DecisionApprovedValue;
        private const int Rejected = SignoffProgressPlugin.DecisionRejectedValue;

        private static FakeOrganizationService WithSignoff(int? decision)
        {
            var service = new FakeOrganizationService();
            if (decision.HasValue)
            {
                service.Seed(
                    "al_signoff",
                    Guid.NewGuid(),
                    "al_remediationactionid", new EntityReference("al_remediationaction", ActionId),
                    "al_signoffdecision", new OptionSetValue(decision.Value),
                    "statecode", new OptionSetValue(0));
            }

            return service;
        }

        [Fact]
        public void A_first_decision_is_allowed()
        {
            Assert.False(SignoffGuardPlugin.AlreadySettled(WithSignoff(null), ActionId));
        }

        [Fact]
        public void An_approved_action_cannot_be_signed_off_again()
        {
            // The case the original guard was written for: a later rejection would otherwise
            // reopen an already-approved action, leaving two contradictory sign-offs with
            // nothing recording which is authoritative. Reversing an approval stays a
            // privileged correction (AD-031).
            Assert.True(SignoffGuardPlugin.AlreadySettled(WithSignoff(Approved), ActionId));
        }

        [Fact]
        public void A_rejected_action_can_be_signed_off_again_once_the_adviser_has_reworked_it()
        {
            // The deadlock. A rejection is the start of the loop, not the end of it.
            Assert.False(SignoffGuardPlugin.AlreadySettled(WithSignoff(Rejected), ActionId));
        }

        [Fact]
        public void A_rejection_then_an_approval_still_closes_the_action()
        {
            // Having accepted the rework, the action is settled and a third decision is
            // refused - so the loop terminates rather than staying open for ever.
            var service = WithSignoff(Rejected);
            service.Seed(
                "al_signoff",
                Guid.NewGuid(),
                "al_remediationactionid", new EntityReference("al_remediationaction", ActionId),
                "al_signoffdecision", new OptionSetValue(Approved),
                "statecode", new OptionSetValue(0));

            Assert.True(SignoffGuardPlugin.AlreadySettled(service, ActionId));
        }

        [Fact]
        public void Two_rejections_in_a_row_are_allowed()
        {
            // An adviser can rework badly twice. Nothing about a second rejection settles the
            // action, so the supervisor is not locked out by their own previous return.
            var service = WithSignoff(Rejected);
            service.Seed(
                "al_signoff",
                Guid.NewGuid(),
                "al_remediationactionid", new EntityReference("al_remediationaction", ActionId),
                "al_signoffdecision", new OptionSetValue(Rejected),
                "statecode", new OptionSetValue(0));

            Assert.False(SignoffGuardPlugin.AlreadySettled(service, ActionId));
        }

        [Fact]
        public void A_sign_off_on_another_action_does_not_settle_this_one()
        {
            var service = new FakeOrganizationService();
            service.Seed(
                "al_signoff",
                Guid.NewGuid(),
                "al_remediationactionid", new EntityReference("al_remediationaction", Guid.NewGuid()),
                "al_signoffdecision", new OptionSetValue(Approved),
                "statecode", new OptionSetValue(0));

            Assert.False(SignoffGuardPlugin.AlreadySettled(service, ActionId));
        }
    }
}
