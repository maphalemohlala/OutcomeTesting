using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-142. Every table <see cref="PermissionHelpers.EnsureAppPermission"/> reads has to be
    /// one the application's own security roles grant, because the gate reads them AS THE
    /// CALLER and may not survive being refused.
    ///
    /// Both halves of that were false. The gate's first act is a break-glass System
    /// Administrator probe against the platform `role` table, and neither shipped role granted
    /// `prvReadRole` (`prvReadal_Role` is the custom al_role table, a different thing). So
    /// every one of the 27 commands behind this gate refused every caller who was not already
    /// a Dataverse System Administrator — on the gate's first line, before any application
    /// rule ran, with a platform privilege fault in place of anything a user could act on.
    /// Adam Strumidlo hit it on Import Cases on 2026-09-16: `privilegeCount=125`, the Outcome
    /// Testing App Admin role to the privilege, and the missing privilege id in the fault was
    /// `prvReadRole`'s own.
    ///
    /// The reads are the caller's because these are Custom APIs. A Custom API plug-in type has
    /// no run-as user, so `PluginUserService` — `GetOrganizationService(context.UserId)` —
    /// resolves to the initiating user. The parameter is still called `systemService`, and the
    /// class comment used to claim the reads did not depend on the caller's privileges, which
    /// is what kept this invisible through 859 passing tests.
    ///
    /// Tolerating the fault in code is NOT available as a fix: AD-109 forbids a plug-in from
    /// absorbing an OrganizationService fault and carrying on, because the platform then
    /// aborts the whole transaction ("ISV code reduced the open transaction count") — and
    /// OptionLabels recorded that for a read. So the grant IS the fix, and this test is what
    /// holds the two halves together: a read the gate performs against the privileges the
    /// roles ship.
    /// </summary>
    public class AppPermissionGateTests
    {
        private const string Email = "adam@ascotlloyd.co.uk";
        private const string Resource = "page.imports";

        private static readonly Guid CallerId = Guid.Parse("6d08a459-d0b1-f111-aaac-002248c654cd");
        private static readonly Guid ContactId = Guid.Parse("11111111-aaaa-4aaa-8aaa-111111111111");

        /// <summary>
        /// The read privilege each table the gate touches needs, by logical name.
        ///
        /// Intersects are absent deliberately: `systemuserroles` and
        /// `powerpagecomponent_mspp_webrole_contact` have no privileges of their own and are
        /// governed by the tables they join, so requiring one would be requiring something
        /// that cannot exist. Everything else must be here AND in both role files.
        /// </summary>
        private static readonly Dictionary<string, string> PrivilegeByTable =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "role", "prvReadRole" },
                { "systemuser", "prvReadUser" },
                { "contact", "prvReadContact" },
                { "mspp_webrole", "prvReadmspp_webrole" },
                { "powerpagecomponent", "prvReadpowerpagecomponent" },
                { "al_userrolemapping", "prvReadal_UserRoleMapping" },
                { "al_pagepermission", "prvReadal_PagePermission" },
            };

        private static readonly string[] Intersects =
        {
            "systemuserroles",
            WebRoleRegistry.ContactRelationship,
        };

        /// <summary>
        /// A caller the gate has to walk end to end: a mapping table with rows (so the
        /// bootstrap rule does not short-circuit), a contact, a web role, and a permission rule
        /// that grants what is being asked for. Anything less and the gate returns early
        /// without reading everything it reads in production, which is the whole point here.
        /// </summary>
        private static FakeOrganizationService FullyConfiguredCaller()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", CallerId, "internalemailaddress", Email);
            svc.Seed(
                ContactRegistry.Entity,
                ContactId,
                ContactRegistry.EmailAttr, Email,
                ContactRegistry.StateCodeAttr, new OptionSetValue(ContactRegistry.StateActive));
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", Email,
                "al_rolecode", "Compliance",
                "statecode", new OptionSetValue(0));
            svc.Seed(
                "al_pagepermission",
                Guid.NewGuid(),
                "al_resourcekey", Resource,
                "al_rolecode", "Compliance",
                "al_accesslevel", new OptionSetValue(PermissionHelpers.AccessManage),
                "statecode", new OptionSetValue(0));

            // The contact-to-web-role join is FetchXML, which the fake does not execute.
            // Two are queued: the AD-090 exclusion probe the mapping resolver issues, and the
            // contact's own web roles.
            svc.FetchResults.Enqueue(new EntityCollection());
            svc.FetchResults.Enqueue(new EntityCollection());

            return svc;
        }

        private static IPluginExecutionContext Context()
        {
            return new FakePluginExecutionContext
            {
                UserId = CallerId,
                InitiatingUserId = CallerId,
            };
        }

        /// <summary>
        /// The defect itself, stated as the rule that was broken. If the gate ever reads a
        /// table the roles do not grant — this one, or one added later — that read faults in
        /// production for every non-administrator and cannot be caught.
        /// </summary>
        [Fact]
        public void EveryTableTheGateReadsIsGrantedByBothShippedRoles()
        {
            var svc = FullyConfiguredCaller();
            PermissionHelpers.EnsureAppPermission(svc, Context(), Resource, PermissionHelpers.AccessEdit);

            var admin = PrivilegesOf("Outcome Testing App Admin");
            var user = PrivilegesOf("Outcome Testing App User");

            foreach (var table in svc.ReadEntities.Except(Intersects, StringComparer.OrdinalIgnoreCase))
            {
                string privilege;
                Assert.True(
                    PrivilegeByTable.TryGetValue(table, out privilege),
                    "The permission gate read '" + table + "', which is not in PrivilegeByTable. " +
                    "Name the read privilege it needs and grant it in both role files, or stop reading it: " +
                    "AD-109 means a read the caller is refused cannot be recovered from.");

                Assert.True(admin.Contains(privilege), "Outcome Testing App Admin does not grant " + privilege);
                Assert.True(user.Contains(privilege), "Outcome Testing App User does not grant " + privilege);
            }
        }

        /// <summary>
        /// And the gate did read something on this run. Without this, the test above passes
        /// just as well when the gate is deleted or never reached, which is exactly the false
        /// negative that let AD-142 ship: a rule about "every table the gate reads" is
        /// vacuously true of a gate that reads none.
        /// </summary>
        [Fact]
        public void TheGateActuallyReadsTheTablesItIsCheckedAgainst()
        {
            var svc = FullyConfiguredCaller();
            PermissionHelpers.EnsureAppPermission(svc, Context(), Resource, PermissionHelpers.AccessEdit);

            Assert.Contains("al_userrolemapping", svc.ReadEntities, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("systemuser", svc.ReadEntities, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The platform <c>role</c> table is no longer read at all (2026-09-21).
        ///
        /// It was read only by the System Administrator short-circuit, and that is gone. This
        /// is pinned rather than left implicit because the read is the expensive kind: it
        /// needs <c>prvReadRole</c> granted on every shipped role, and AD-109 means a gate
        /// read the caller is refused cannot be recovered from - which is precisely how
        /// AD-142 took down all 27 commands. Re-introducing it should be a deliberate act
        /// that fails this test first.
        /// </summary>
        [Fact]
        public void TheGateNoLongerReadsThePlatformRoleTable()
        {
            var svc = FullyConfiguredCaller();
            PermissionHelpers.EnsureAppPermission(svc, Context(), Resource, PermissionHelpers.AccessEdit);

            Assert.DoesNotContain("role", svc.ReadEntities, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("systemuserroles", svc.ReadEntities, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A Dataverse System Administrator gets NOTHING the application has not granted
        /// them (project owner, 2026-09-21). This is the negative the change has to prove.
        ///
        /// The seeding is deliberately the strongest possible case for the old behaviour:
        /// the caller really does hold the System Administrator role, and a mapping table
        /// really does exist so the bootstrap cannot fire. Under the break-glass this
        /// returned on the gate's first line with AccessManage on a resource the caller had
        /// no mapping for. Now it refuses, like anybody else holding no application role.
        ///
        /// Removing the short-circuit strands nobody: a System Administrator holds every
        /// table privilege, so they can still repair a bad rule by writing
        /// al_userrolemapping and al_pagepermission directly. What they can no longer do is
        /// pass this gate without a grant - which is what "the same rules as everybody else"
        /// means, and what al_CompleteRemediation already did.
        /// </summary>
        [Fact]
        public void ASystemAdministratorIsHeldToTheSameRulesAsEverybodyElse()
        {
            var svc = new FakeOrganizationService();
            var roleId = Guid.NewGuid();
            svc.Seed("role", roleId, "name", "System Administrator");
            svc.Seed("systemuserroles", Guid.NewGuid(), "roleid", roleId, "systemuserid", CallerId);
            svc.Seed("systemuser", CallerId, "internalemailaddress", Email);
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", "someone.else@ascotlloyd.co.uk",
                "al_rolecode", "Reviewer",
                "statecode", new OptionSetValue(0));

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => PermissionHelpers.EnsureAppPermission(
                    svc, Context(), Resource, PermissionHelpers.AccessManage));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, error.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// And a caller who is not an administrator is still refused by the application rules.
        /// Granting read on the role table widens what a caller can SEE, never what they can
        /// do — the probe returning "no rows" has to keep meaning "not an administrator".
        /// </summary>
        [Fact]
        public void ANonAdministratorHoldingNoRoleIsStillRefused()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", CallerId, "internalemailaddress", Email);
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", "someone.else@ascotlloyd.co.uk",
                "al_rolecode", "Reviewer",
                "statecode", new OptionSetValue(0));
            svc.FetchResults.Enqueue(new EntityCollection());

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => PermissionHelpers.EnsureAppPermission(svc, Context(), Resource, PermissionHelpers.AccessEdit));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, error.Message, StringComparison.Ordinal);
        }

        /// <summary>The privilege names one shipped role file grants.</summary>
        private static HashSet<string> PrivilegesOf(string roleName)
        {
            var path = Path.Combine(RepoRoot(), "src", "Roles", roleName + ".xml");
            var document = System.Xml.Linq.XDocument.Load(path);

            return new HashSet<string>(
                document.Descendants("RolePrivilege").Select(p => (string)p.Attribute("name")),
                StringComparer.OrdinalIgnoreCase);
        }

        private static string RepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "src", "Roles")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("src/Roles was not found above " + AppContext.BaseDirectory);
        }
    }
}
