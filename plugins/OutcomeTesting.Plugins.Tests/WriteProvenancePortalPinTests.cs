using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Only the application the portal writes as passes once its client id is set. Before,
    /// any application user did: DEV holds 149 of them (2026-11-17). The ids are DEV's: the
    /// portal's writes arrive as "# PowerPages Data Runtime PROD", and the other user is the
    /// site's own "# Portals-Outcome Testing" registration, which no portal write used.
    /// </summary>
    public class WriteProvenancePortalPinTests
    {
        private static readonly Guid PortalApp = Guid.Parse("f61b1d65-0f1b-4ec8-8721-cfeca7c30416");
        private static readonly Guid PortalUser = Guid.Parse("ef4223af-169e-f111-b8dd-e4fade065423");
        private static readonly Guid OtherUser = Guid.Parse("d247aec6-98a3-f111-aaac-e4fade069307");

        private static FakeOrganizationService Environment(string pinned)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", PortalUser, "applicationid", PortalApp);
            svc.Seed("systemuser", OtherUser, "applicationid", Guid.NewGuid());

            var definition = Guid.NewGuid();
            svc.Seed("environmentvariabledefinition", definition,
                "schemaname", WriteProvenance.PortalApplicationIdVariable);
            if (pinned != null)
            {
                svc.Seed("environmentvariablevalue", Guid.NewGuid(),
                    "environmentvariabledefinitionid", new EntityReference("environmentvariabledefinition", definition),
                    "value", pinned);
            }

            return svc;
        }

        private static FakePluginExecutionContext Caller(Guid userId)
        {
            return new FakePluginExecutionContext { InitiatingUserId = userId };
        }

        [Fact]
        public void The_pinned_site_application_passes()
        {
            var svc = Environment(PortalApp.ToString());

            Assert.True(WriteProvenance.IsApplicationUser(svc, PortalUser));
            WriteProvenance.EnsurePortalCaller(svc, Caller(PortalUser));
        }

        [Fact]
        public void Another_application_user_is_refused_once_pinned()
        {
            var svc = Environment(" " + PortalApp.ToString("B").ToUpperInvariant() + " ");

            Assert.False(WriteProvenance.IsApplicationUser(svc, OtherUser));
            Assert.False(WriteProvenance.IsTrusted(svc, Caller(OtherUser)));
            Assert.Throws<InvalidPluginExecutionException>(
                () => WriteProvenance.EnsurePortalCaller(svc, Caller(OtherUser)));
        }

        [Fact]
        public void Unset_falls_back_to_any_application_user()
        {
            var svc = Environment(null);

            Assert.True(WriteProvenance.IsApplicationUser(svc, PortalUser));
            Assert.True(WriteProvenance.IsApplicationUser(svc, OtherUser));
        }

        [Fact]
        public void A_pin_that_is_not_a_guid_refuses_every_application_user()
        {
            var svc = Environment("Portals-Outcome Testing");

            Assert.False(WriteProvenance.IsApplicationUser(svc, PortalUser));
        }

        [Fact]
        public void A_person_never_reads_the_variable()
        {
            var svc = Environment(PortalApp.ToString());
            var person = Guid.NewGuid();
            svc.Seed("systemuser", person, "internalemailaddress", "person@example.com");

            Assert.False(WriteProvenance.IsApplicationUser(svc, person));
            Assert.Equal(1, svc.RetrieveMultipleCount);
        }
    }
}
