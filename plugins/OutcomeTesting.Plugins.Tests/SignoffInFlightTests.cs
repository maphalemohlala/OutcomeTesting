using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// How the guard tells the supervisor's write of the two T&amp;C-only columns from
    /// anybody else's.
    ///
    /// <para>
    /// <b>Two earlier answers were wrong, and the reason matters.</b> The guard first asked
    /// whether the parent pipeline was an Update of <c>contact</c>, then whether the sign-off
    /// had raised a shared variable. Both were disproved in DEV on 2026-09-21 by printing the
    /// chain: <b>Power Pages wraps every write in the same shape</b> -
    /// <c>Upsert/none(depth 1) -&gt; Update/al_remediationaction(depth 2)</c> - so the
    /// adviser's PATCH and the supervisor's sign-off are indistinguishable by message, entity,
    /// depth or parent; and a parent's shared variables are snapshotted before the plug-in
    /// that would set one runs, so the flag was never visible downstream. Every unit test
    /// passed throughout, because they all passed the decision in as a bool.
    /// </para>
    /// <para>
    /// The answer that works is state: a sign-off leaves <c>al_signoffrequest</c> on a contact
    /// for exactly the window in which it writes these columns.
    /// </para>
    /// </summary>
    public class SignoffInFlightTests
    {
        private static readonly Guid ContactId = Guid.Parse("dddddddd-4444-4444-8444-444444444444");

        [Fact]
        public void No_request_anywhere_is_not_a_sign_off()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, "fullname", "Pat Manager");

            Assert.False(RemediationResponseGuardPlugin.IsSignoffInFlight(svc));
        }

        [Fact]
        public void A_contact_carrying_a_request_is_a_sign_off_in_flight()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId,
                SignoffRequestPlugin.RequestAttr, "{\"actionId\":\"x\",\"decision\":120910720}");

            Assert.True(RemediationResponseGuardPlugin.IsSignoffInFlight(svc));
        }

        [Fact]
        public void It_does_not_matter_which_action_the_request_names()
        {
            // The tighter form was written first and refused four writes out of five:
            // RecordFormAnswers answers the whole check, writing every sibling action on the
            // case, while the request names only the one the supervisor clicked.
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId,
                SignoffRequestPlugin.RequestAttr,
                "{\"actionId\":\"5642d55a-b7b5-f111-aaac-e4fade069307\",\"decision\":120910720}");

            Assert.True(RemediationResponseGuardPlugin.IsSignoffInFlight(svc));
        }

        [Fact]
        public void Nothing_to_ask_is_not_a_sign_off()
        {
            Assert.False(RemediationResponseGuardPlugin.IsSignoffInFlight(null));
        }

        [Fact]
        public void The_two_halves_joined()
        {
            // Identical writes, told apart only by whether a sign-off is happening.
            var update = new Entity("al_remediationaction", Guid.NewGuid())
            {
                ["al_recheckrequired"] = new OptionSetValue(Remediation.RecheckRequiredNo),
            };

            var quiet = new FakeOrganizationService();
            quiet.Seed("contact", ContactId, "fullname", "Pat Manager");

            var signing = new FakeOrganizationService();
            signing.Seed("contact", ContactId, SignoffRequestPlugin.RequestAttr, "{\"decision\":1}");

            Assert.NotNull(RemediationResponseGuardPlugin.TcOnlyRefusal(
                update, RemediationResponseGuardPlugin.IsSignoffInFlight(quiet)));
            Assert.Null(RemediationResponseGuardPlugin.TcOnlyRefusal(
                update, RemediationResponseGuardPlugin.IsSignoffInFlight(signing)));
        }
    }
}
