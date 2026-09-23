using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Which portal the letters link into (reported 2026-09-22: "the links that go out on the
    /// Test portal still use the old dev url, outcometesting instead of outcometestingtest").
    ///
    /// <para>
    /// The cause was not a wrong lookup. <c>powerpagesite</c> is a SOLUTION COMPONENT, so the
    /// row is exported from DEV and imported everywhere else as a managed row carrying DEV's
    /// domain. Read on Env_AQ_Test on 2026-09-22 it held
    /// <c>outcometesting.powerappsportals.com</c> - one row, managed, DEV's host - while the
    /// site is served from <c>outcometestingtest</c>. PROD would have had the same.
    /// </para>
    /// <para>
    /// So the fix is a source that CAN differ per environment: an environment variable whose
    /// definition travels in the solution and whose value is set per environment, with the
    /// site row kept as the fallback so an unconfigured environment behaves as it did.
    /// </para>
    /// </summary>
    public class PortalSiteTests
    {
        private static readonly Guid CaseId = Guid.Parse("a1b2c3d4-0000-4000-8000-000000000001");
        private static readonly Guid DefinitionId = Guid.Parse("a1b2c3d4-0000-4000-8000-000000000002");

        /// <summary>An environment whose site row carries DEV's domain, as TEST's does.</summary>
        private static FakeOrganizationService Environment(string siteDomain)
        {
            var service = new FakeOrganizationService();
            if (siteDomain != null)
            {
                service.Seed("powerpagesite", Guid.NewGuid(), "primarydomainname", siteDomain);
            }

            return service;
        }

        /// <summary>Sets the environment variable, with or without a value row.</summary>
        private static void Variable(
            FakeOrganizationService service, string defaultValue, string currentValue)
        {
            service.Seed(
                "environmentvariabledefinition", DefinitionId,
                "schemaname", PortalSite.BaseUrlVariable,
                "defaultvalue", defaultValue);

            if (currentValue != null)
            {
                service.Seed(
                    "environmentvariablevalue", Guid.NewGuid(),
                    "environmentvariabledefinitionid",
                    new EntityReference("environmentvariabledefinition", DefinitionId),
                    "value", currentValue);
            }
        }

        [Fact]
        public void The_environment_variable_beats_the_imported_site_row()
        {
            // THE bug, in one test. The site row says DEV because the solution came from DEV.
            var service = Environment("outcometesting.powerappsportals.com");
            Variable(service, null, "outcometestingtest.powerappsportals.com");

            Assert.Equal(
                "https://outcometestingtest.powerappsportals.com", PortalSite.BaseUrl(service));
        }

        [Fact]
        public void Falls_back_to_the_site_row_where_nobody_has_set_the_variable()
        {
            // DEV, where the row is right - and any environment nobody has configured yet,
            // which must go on behaving as it did rather than sending letters with no link.
            var service = Environment("outcometesting.powerappsportals.com");

            Assert.Equal("https://outcometesting.powerappsportals.com", PortalSite.BaseUrl(service));
        }

        [Fact]
        public void Falls_back_to_the_site_row_when_the_variable_has_no_value_row()
        {
            // The ordinary state straight after an import: the definition arrived with the
            // solution and nobody has set it yet.
            var service = Environment("outcometesting.powerappsportals.com");
            Variable(service, null, null);

            Assert.Equal("https://outcometesting.powerappsportals.com", PortalSite.BaseUrl(service));
        }

        [Fact]
        public void Reads_the_definitions_default_when_there_is_no_value_row()
        {
            var service = Environment("outcometesting.powerappsportals.com");
            Variable(service, "shipped.powerappsportals.com", null);

            Assert.Equal("https://shipped.powerappsportals.com", PortalSite.BaseUrl(service));
        }

        [Fact]
        public void A_set_value_beats_the_shipped_default()
        {
            var service = Environment(null);
            Variable(service, "shipped.powerappsportals.com", "outcometestingtest.powerappsportals.com");

            Assert.Equal(
                "https://outcometestingtest.powerappsportals.com", PortalSite.BaseUrl(service));
        }

        [Theory]
        [InlineData("outcometestingtest.powerappsportals.com")]
        [InlineData("https://outcometestingtest.powerappsportals.com")]
        [InlineData("https://outcometestingtest.powerappsportals.com/")]
        [InlineData("  outcometestingtest.powerappsportals.com  ")]
        public void Takes_a_host_or_a_url_and_normalises_either(string typed)
        {
            // Both forms will be typed. The site row holds a bare host; whoever sets the
            // variable is being asked for "the portal's address", which people write with the
            // scheme on. Refusing one would be a configuration error that showed up as a
            // broken link in somebody's email rather than as a message anyone could act on.
            var service = Environment(null);
            Variable(service, null, typed);

            Assert.Equal(
                "https://outcometestingtest.powerappsportals.com", PortalSite.BaseUrl(service));
        }

        [Fact]
        public void An_http_address_is_left_as_it_was_typed()
        {
            // Not silently upgraded. A site genuinely served over http would get a link that
            // redirects; rewriting the scheme would hide a configuration mistake worth seeing.
            var service = Environment(null);
            Variable(service, null, "http://localhost:8080");

            Assert.Equal("http://localhost:8080", PortalSite.BaseUrl(service));
        }

        [Fact]
        public void Says_nothing_rather_than_guessing_when_neither_source_answers()
        {
            // An email that says "/case-details?id=..." is worse than one that says nothing:
            // it looks like a link, and the reader spends their time working out why it does
            // not work.
            Assert.Null(PortalSite.BaseUrl(Environment(null)));
            Assert.Null(PortalSite.CaseLink(Environment(null), CaseId));
        }

        [Fact]
        public void A_blank_value_is_not_a_configured_one()
        {
            var service = Environment("outcometesting.powerappsportals.com");
            Variable(service, null, "   ");

            Assert.Equal("https://outcometesting.powerappsportals.com", PortalSite.BaseUrl(service));
        }

        [Fact]
        public void Builds_the_case_link_onto_whichever_host_won()
        {
            var service = Environment("outcometesting.powerappsportals.com");
            Variable(service, null, "outcometestingtest.powerappsportals.com");

            Assert.Equal(
                "https://outcometestingtest.powerappsportals.com/case-details?id="
                    + CaseId.ToString("D"),
                PortalSite.CaseLink(service, CaseId));
        }

        [Fact]
        public void The_outbox_link_goes_through_the_same_rule()
        {
            // NotificationOutbox.CaseLink is what the bodies actually call, and it used to hold
            // its own copy of the site read. Pinned so it cannot grow one again.
            var service = Environment("outcometesting.powerappsportals.com");
            Variable(service, null, "outcometestingtest.powerappsportals.com");

            var link = NotificationOutbox.CaseLink(
                service, new EntityReference("al_outcomecase", CaseId));

            Assert.StartsWith("https://outcometestingtest.powerappsportals.com/", link);
        }

        [Fact]
        public void A_letter_is_never_lost_to_a_failure_reading_the_configuration()
        {
            // Inside the transaction holding a checker's submit open. A missing link costs the
            // reader a click; a plug-in that threw would cost them the letter.
            var service = Environment("outcometesting.powerappsportals.com");
            service.RetrieveMultipleThrows = new InvalidOperationException("no privilege");

            Assert.Null(PortalSite.BaseUrl(service));
        }
    }
}
