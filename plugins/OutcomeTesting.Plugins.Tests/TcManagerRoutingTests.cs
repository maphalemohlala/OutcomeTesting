using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The adviser -> T&amp;C Manager mapping: who is TOLD, and - since 2026-09-21 - who is
    /// allowed to attest. See The_routing_type_still_carries_no_access_decision_of_its_own.
    /// Formerly: decides who is TOLD, never who is allowed
    /// (Fixes 5, AD-162).
    ///
    /// <para>
    /// The remediation brief asked for a test proving an unmapped T&amp;C Manager cannot read
    /// the case. That test is deliberately absent, and its absence is the point: the owner
    /// confirmed on 2026-09-20 that T&amp;C Managers read every case, so such a test would
    /// assert the opposite of the access model Phase 1 built. Writing it to satisfy the brief
    /// would have pinned a behaviour nobody wants.
    /// </para>
    /// <para>
    /// What is pinned instead is the routing, and the fact that a missing mapping costs a
    /// notification rather than the ability to proceed.
    /// </para>
    /// </summary>
    public class TcManagerRoutingTests
    {
        private static readonly Guid CaseId = Guid.Parse("caee0000-1111-4111-8111-111111111111");

        private const string Adviser = "adviser@example.com";

        [Fact]
        public void Routes_a_case_to_the_manager_mapped_to_its_adviser()
        {
            var service = new FakeOrganizationService();
            var manager = SeedContact(service, "Pat Manager", "pat@example.com");
            SeedCase(service, Adviser);
            SeedMapping(service, Adviser, manager);

            var routing = TcManagerRouting.ForCase(service, Case());

            Assert.True(routing.IsRouted);
            Assert.Equal("pat@example.com", routing.Email);
            Assert.Equal(manager.Id, routing.Manager.Id);
        }

        [Fact]
        public void Matches_the_adviser_email_ignoring_surrounding_whitespace()
        {
            var service = new FakeOrganizationService();
            var manager = SeedContact(service, "Pat Manager", "pat@example.com");
            SeedCase(service, "  " + Adviser + "  ");
            SeedMapping(service, Adviser, manager);

            Assert.True(TcManagerRouting.ForCase(service, Case()).IsRouted);
        }

        [Fact]
        public void Says_when_the_case_carries_no_adviser_email()
        {
            var service = new FakeOrganizationService();
            SeedCase(service, null);

            var routing = TcManagerRouting.ForCase(service, Case());

            Assert.Equal(TcManagerRouting.RoutingKind.NoAdviserEmail, routing.Kind);
            Assert.False(routing.IsRouted);
        }

        [Fact]
        public void Says_when_no_manager_is_mapped_to_the_adviser_and_names_them()
        {
            // The adviser's address has to appear in the reason. "Unmapped" on its own tells
            // an administrator nothing they can go and fix - the same reasoning as AD-161.
            var service = new FakeOrganizationService();
            SeedCase(service, Adviser);

            var routing = TcManagerRouting.ForCase(service, Case());

            Assert.Equal(TcManagerRouting.RoutingKind.NoMapping, routing.Kind);
            Assert.Contains(Adviser, routing.Reason);
        }

        [Fact]
        public void Says_when_the_mapped_manager_has_no_work_email()
        {
            // A mapping that exists but cannot be written to is a different problem from no
            // mapping at all, and a different person fixes it.
            var service = new FakeOrganizationService();
            var manager = service.Seed("contact", Guid.NewGuid(),
                "fullname", "Pat Manager", "statecode", new OptionSetValue(0));
            SeedCase(service, Adviser);
            SeedMapping(service, Adviser, manager.ToEntityReference());

            var routing = TcManagerRouting.ForCase(service, Case());

            Assert.Equal(TcManagerRouting.RoutingKind.ManagerNotReachable, routing.Kind);
            Assert.False(routing.IsRouted);

            // The manager is still reported, because knowing WHICH contact needs an email
            // is the actionable half.
            Assert.NotNull(routing.Manager);
        }

        [Fact]
        public void Another_advisers_mapping_does_not_route_this_case()
        {
            // The lookup is by the case's own adviser, not "any mapping that exists". With
            // one mapping in the environment a resolver that ignored the key would still
            // look correct, so this is the test that separates the two.
            var service = new FakeOrganizationService();
            var manager = SeedContact(service, "Pat Manager", "pat@example.com");
            SeedCase(service, Adviser);
            SeedMapping(service, "someone.else@example.com", manager);

            var routing = TcManagerRouting.ForCase(service, Case());

            Assert.Equal(TcManagerRouting.RoutingKind.NoMapping, routing.Kind);
        }

        [Fact]
        public void Resolving_nothing_is_never_an_exception()
        {
            // The caller is an adviser finishing their own work. A configuration gap must
            // not fail their completion, so every path returns a value.
            var routing = TcManagerRouting.ForCase(new FakeOrganizationService(), null);

            Assert.False(routing.IsRouted);
            Assert.NotNull(routing.Reason);
        }

        // ----------------------------------------------------------------- routing only

        [Fact]
        public void The_routing_type_still_carries_no_access_decision_of_its_own()
        {
            // This test used to be the guard against the mapping quietly becoming
            // authorisation, and said the argument had to be had here first. It was had:
            // on 2026-09-21 the owner found a service account holding the supervisor role
            // could sign off a case whose adviser it supervises nothing of, and directed
            // that the signatory must be the mapped manager. SignoffRequestPlugin
            // .EnsureMappedToCase makes that call - openly, in the plug-in that enforces it.
            //
            // The assertion is kept unchanged, because the property it protects still holds
            // and is now worth more, not less: Routing returns a recipient and a reason and
            // nothing else. A caller that wants an access decision has to make it and be seen
            // to make it. The day this type grows an IsAllowedToSign, the argument gets had
            // here again.
            var properties = typeof(TcManagerRouting.Routing).GetProperties();

            Assert.Equal(
                new[] { "Email", "IsRouted", "Kind", "Manager", "Reason" },
                Array.ConvertAll(properties, p => p.Name).OrderBy());
        }

        private static EntityReference Case()
        {
            return new EntityReference("al_outcomecase", CaseId);
        }

        private static void SeedCase(FakeOrganizationService service, string adviserEmail)
        {
            service.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-1",
                TcManagerRouting.CaseAdviserEmailAttr, adviserEmail);
        }

        private static EntityReference SeedContact(
            FakeOrganizationService service, string name, string email)
        {
            return service.Seed("contact", Guid.NewGuid(),
                "fullname", name, "emailaddress1", email,
                "statecode", new OptionSetValue(0)).ToEntityReference();
        }

        private static void SeedMapping(
            FakeOrganizationService service, string adviserEmail, EntityReference manager)
        {
            service.Seed(TcManagerRouting.MappingEntity, Guid.NewGuid(),
                TcManagerRouting.MappingEmailAttr, adviserEmail,
                TcManagerRouting.ManagerAttr, manager);
        }
    }

    internal static class TestArrayExtensions
    {
        /// <summary>Sorted copy, so the assertion above does not depend on reflection order.</summary>
        public static string[] OrderBy(this string[] values)
        {
            var copy = (string[])values.Clone();
            Array.Sort(copy, StringComparer.Ordinal);
            return copy;
        }
    }
}
