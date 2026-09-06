# Role Assignment Conflict Rule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a role assignment made in Power Pages visible in the app, resolvable by an
explicit administrator decision, and unable to disagree with what the server enforces.

**Architecture:** Three new Dataverse Custom APIs carry the reads and the write the browser
cannot do for itself, because the contact-to-web-role intersect is reachable only from the
contact side by FetchXML. The client stops deriving role membership and asks the server for
it. A guard stops any web role that is auto-granted to everyone from conferring application
permissions.

**Tech Stack:** C# plug-ins on net462 (no Newtonsoft — JSON is built with
`ImportRules.JsonEscape`), xunit with `FakeOrganizationService`, React 19 + TypeScript with
vitest, Power Apps Code App client.

**Spec:** `docs/superpowers/specs/2026-09-06-role-assignment-conflict-rule-design.md`

## Global Constraints

- Target framework is `net462`. No new NuGet packages. JSON is built by hand with
  `ImportRules.JsonEscape`.
- Every Custom API here is declared `isfunction: false`, `bindingtype: 0`, `isprivate: false`.
- No new `al_auditevent` command option value is minted. `al_AdoptRoleAssignment` reuses
  `120910773` (AssignUserRole) when adopting and `120910788` (SetRoleAssignmentActive) when
  revoking, following AD-076. The details line records that it reconciled a portal-side
  assignment. (`Program.cs` does have an `addcommandvalue` verb if this is ever revisited.)
- Access level constants: `PermissionHelpers.AccessView = 120910767`,
  `AccessManage = 120910769`.
- Plug-in logic that tests must reach is exposed as a **public static method on the plug-in
  class** returning a result type; `ExecuteDataversePlugin` stays a thin marshaller. This is
  the established pattern (`CompleteRemediationPlugin.Complete`).
- Writes run as `InitiatingUserService`; reads that must see across users run as
  `PluginUserService`. Never widen this without saying why in a comment.
- Failure messages carry a `CommandHelpers.*Prefix` so the client classifies them.
- Do not delete rows. Deactivation is the sanctioned alternative (AD-037/OD-010).

---

### Task 1: AD-090 — a role auto-granted to everyone never resolves

`WebRoleRegistry.IsSystemRole` is currently defined and called from nowhere, and
`PermissionHelpers.GetActiveRoles` unions in whatever web roles a contact holds. OD-033
records `Administrators` carrying `mspp_authenticatedusersrole = Yes` in DEV, which means
every contact resolved as `Administrators`. The guard follows the flag, not the name.

This task also teaches `FakeOrganizationService` to answer `FetchExpression`, which it
currently refuses — `WebRoleRegistry.RolesForContact` uses FetchXML, so nothing downstream
is testable until it can.

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/WebRoleRegistry.cs`
- Modify: `plugins/OutcomeTesting.Plugins/PermissionHelpers.cs` (`GetActiveRoles`, ~line 173)
- Modify: `plugins/OutcomeTesting.Plugins.Tests/FakeOrganizationService.cs` (`RetrieveMultiple`, ~line 138)
- Test: `plugins/OutcomeTesting.Plugins.Tests/WebRoleResolutionTests.cs`

**Interfaces:**
- Consumes: `WebRoleRegistry.FindByName`, `WebRoleRegistry.AuthenticatedAttr`, `IsSystemRole`.
- Produces: `WebRoleRegistry.ExcludedFromResolution(IOrganizationService service, string roleName) -> bool`.
  `FakeOrganizationService.FetchResults` (a `Queue<EntityCollection>`) and
  `FakeOrganizationService.FetchXml` (a `List<string>` recording each fetch issued).

- [ ] **Step 1: Give the fake a FetchExpression stub**

In `FakeOrganizationService`, add the queue and record, then handle the non-QueryExpression
case instead of throwing:

```csharp
/// <summary>Results handed back, in order, for each FetchExpression the code under test issues.</summary>
public Queue<EntityCollection> FetchResults { get; } = new Queue<EntityCollection>();

/// <summary>Every fetch issued, so a test can assert on what was asked for.</summary>
public List<string> FetchXml { get; } = new List<string>();
```

Then at the top of `RetrieveMultiple`, before the `QueryExpression` cast:

```csharp
var fetch = query as FetchExpression;
if (fetch != null)
{
    FetchXml.Add(fetch.Query);
    return FetchResults.Count > 0 ? FetchResults.Dequeue() : new EntityCollection();
}
```

- [ ] **Step 2: Write the failing test**

Create `plugins/OutcomeTesting.Plugins.Tests/WebRoleResolutionTests.cs`:

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-090. A web role flagged as the authenticated-users role is granted to every
    /// signed-in contact, so it can never confer application permissions. OD-033 is why
    /// this is not a name check: `Administrators` carried that flag in DEV, and every
    /// contact resolved as Administrators.
    /// </summary>
    public class WebRoleResolutionTests
    {
        private static readonly Guid RoleId = Guid.Parse("44444444-dddd-4ddd-8ddd-444444444444");

        private static FakeOrganizationService WithRole(string name, bool authenticatedUsersRole)
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                RoleId,
                WebRoleRegistry.NameAttr, name,
                WebRoleRegistry.AuthenticatedAttr, authenticatedUsersRole);
            return svc;
        }

        [Fact]
        public void ExcludesARoleCarryingTheAuthenticatedUsersFlagWhateverItIsCalled()
        {
            var svc = WithRole("Administrators", true);
            Assert.True(WebRoleRegistry.ExcludedFromResolution(svc, "Administrators"));
        }

        [Fact]
        public void KeepsAnOrdinaryBusinessRole()
        {
            var svc = WithRole("AL Portal - Tax Reviewer", false);
            Assert.False(WebRoleRegistry.ExcludedFromResolution(svc, "AL Portal - Tax Reviewer"));
        }

        [Theory]
        [InlineData("Anonymous Users")]
        [InlineData("Authenticated Users")]
        [InlineData("  authenticated users  ")]
        public void ExcludesTheTwoSystemRolesByNameWithoutNeedingTheFlag(string name)
        {
            var svc = WithRole(name, false);
            Assert.True(WebRoleRegistry.ExcludedFromResolution(svc, name));
        }

        [Fact]
        public void KeepsARoleThatCannotBeLookedUpRatherThanSilentlyDroppingIt()
        {
            // A role the query cannot resolve is not evidence that it is auto-granted.
            // Dropping it here would withdraw access on a transient read failure.
            var svc = new FakeOrganizationService();
            Assert.False(WebRoleRegistry.ExcludedFromResolution(svc, "AL Portal - Planner"));
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter WebRoleResolutionTests`
Expected: FAIL — `WebRoleRegistry` does not contain a definition for `ExcludedFromResolution`.

- [ ] **Step 4: Implement the guard**

In `WebRoleRegistry.cs`, below `IsSystemRole`:

```csharp
/// <summary>
/// Whether a web role must be ignored when resolving what a caller may do (AD-090).
///
/// Two reasons, and the second is the one that matters. `Anonymous Users` and
/// `Authenticated Users` are Power Pages plumbing and excluded by name. Beyond them, ANY
/// role carrying <c>mspp_authenticatedusersrole</c> is auto-granted to every signed-in
/// contact, so treating it as an application role grants that role to everyone —
/// which is exactly what OD-033 found `Administrators` doing in DEV.
///
/// A role that cannot be looked up is NOT excluded. Absence of evidence that a role is
/// auto-granted is not evidence that it is, and excluding on a failed read would withdraw
/// access from everyone holding it.
/// </summary>
public static bool ExcludedFromResolution(IOrganizationService service, string roleName)
{
    if (IsSystemRole(roleName))
    {
        return true;
    }

    var role = FindByName(service, (roleName ?? string.Empty).Trim());
    if (role == null)
    {
        return false;
    }

    return role.GetAttributeValue<bool?>(AuthenticatedAttr) ?? false;
}
```

`FindByName` must return the flag, so add it to that method's fetch — after the
`NameAttr` attribute line:

```csharp
"<attribute name='" + AuthenticatedAttr + "'/>" +
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter WebRoleResolutionTests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Apply the guard in the resolver**

In `PermissionHelpers.GetActiveRoles`, replace the loop body so an excluded role never
enters the caller's codes:

```csharp
foreach (var webRole in WebRoleRegistry.RolesForContact(service, contact.Id))
{
    if (WebRoleRegistry.ExcludedFromResolution(service, webRole))
    {
        continue;
    }

    if (!roles.RoleCodes.Contains(webRole))
    {
        roles.RoleCodes.Add(webRole);
    }
}
```

- [ ] **Step 7: Run the whole plug-in suite**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: PASS, no previously-passing test broken.

- [ ] **Step 8: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/WebRoleRegistry.cs plugins/OutcomeTesting.Plugins/PermissionHelpers.cs plugins/OutcomeTesting.Plugins.Tests/
git commit -m "fix(plugins): a web role auto-granted to everyone never resolves (AD-090)"
```

---

### Task 2: Merge the two sources into holder records

The pure half of the read: given the mapping rows for a role and the contacts actually
associated with it, produce one record per person carrying both facts. Kept free of
`IOrganizationService` so the join is testable without any fake, following `ImportRules`
and `OutcomeRules`.

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/RoleHolders.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/RoleHoldersTests.cs`

**Interfaces:**
- Consumes: `ImportRules.JsonEscape`, `CommandHelpers.IsActive`.
- Produces: `RoleHolder` (fields `Email`, `Name`, `MappingId`, `MappingActive`, `Associated`),
  `RoleHolders.Merge(IEnumerable<Entity> mappings, IEnumerable<Entity> associatedContacts) -> List<RoleHolder>`,
  `RoleHolders.ToJson(IEnumerable<RoleHolder> holders) -> string`.

- [ ] **Step 1: Write the failing test**

Create `plugins/OutcomeTesting.Plugins.Tests/RoleHoldersTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The join behind al_GetRoleHolders. Every interesting case is a DISAGREEMENT between
    /// the mapping table and the web role association, so the merge has to keep a person
    /// who appears in only one of them.
    /// </summary>
    public class RoleHoldersTests
    {
        private static readonly Guid MappingId = Guid.Parse("55555555-eeee-4eee-8eee-555555555555");

        private static Entity Mapping(string email, bool active, Guid? id = null)
        {
            var row = new Entity("al_userrolemapping", id ?? MappingId);
            row["al_useremail"] = email;
            row["statecode"] = new OptionSetValue(active ? 0 : 1);
            return row;
        }

        private static Entity Contact(string email, string name)
        {
            var row = new Entity("contact", Guid.NewGuid());
            row["emailaddress1"] = email;
            row["fullname"] = name;
            return row;
        }

        [Fact]
        public void ReportsAConsistentHolderOnce()
        {
            var holders = RoleHolders.Merge(
                new[] { Mapping("a@ascotlloyd.co.uk", true) },
                new[] { Contact("a@ascotlloyd.co.uk", "A Person") });

            var only = Assert.Single(holders);
            Assert.Equal("a@ascotlloyd.co.uk", only.Email);
            Assert.Equal("A Person", only.Name);
            Assert.Equal(MappingId, only.MappingId);
            Assert.True(only.MappingActive);
            Assert.True(only.Associated);
        }

        [Fact]
        public void KeepsAPortalOnlyGrantThatHasNoMappingAtAll()
        {
            var holders = RoleHolders.Merge(
                new Entity[0],
                new[] { Contact("portal@ascotlloyd.co.uk", "Portal Person") });

            var only = Assert.Single(holders);
            Assert.Null(only.MappingId);
            Assert.Null(only.MappingActive);
            Assert.True(only.Associated);
        }

        [Fact]
        public void KeepsAWithdrawnMappingWhoseAssociationSurvived()
        {
            // The silent un-withdraw: the app says withdrawn, the portal still grants it.
            var holders = RoleHolders.Merge(
                new[] { Mapping("z@ascotlloyd.co.uk", false) },
                new[] { Contact("z@ascotlloyd.co.uk", "Z Person") });

            var only = Assert.Single(holders);
            Assert.False(only.MappingActive);
            Assert.True(only.Associated);
        }

        [Fact]
        public void KeepsAnActiveMappingWhoseAssociationWasRemoved()
        {
            var holders = RoleHolders.Merge(
                new[] { Mapping("gone@ascotlloyd.co.uk", true) },
                new Entity[0]);

            var only = Assert.Single(holders);
            Assert.True(only.MappingActive);
            Assert.False(only.Associated);
        }

        [Fact]
        public void MatchesTheTwoSourcesRegardlessOfEmailCasing()
        {
            var holders = RoleHolders.Merge(
                new[] { Mapping("Mixed.Case@AscotLloyd.co.uk", true) },
                new[] { Contact("mixed.case@ascotlloyd.co.uk", "Mixed Case") });

            Assert.Single(holders);
            Assert.True(holders[0].Associated);
        }

        [Fact]
        public void OrdersByEmailSoTheListIsStableBetweenReads()
        {
            var holders = RoleHolders.Merge(
                new[]
                {
                    Mapping("z@ascotlloyd.co.uk", true, Guid.NewGuid()),
                    Mapping("a@ascotlloyd.co.uk", true, Guid.NewGuid()),
                },
                new Entity[0]);

            Assert.Equal(new[] { "a@ascotlloyd.co.uk", "z@ascotlloyd.co.uk" }, holders.Select(h => h.Email));
        }

        [Fact]
        public void EmitsJsonTheClientCanParseIncludingNulls()
        {
            var json = RoleHolders.ToJson(RoleHolders.Merge(
                new Entity[0],
                new[] { Contact("q\"uote@ascotlloyd.co.uk", "Quote \"Person\"") }));

            Assert.Contains("\"mappingId\":null", json);
            Assert.Contains("\"mappingActive\":null", json);
            Assert.Contains("\"associated\":true", json);
            Assert.Contains("\\\"", json);
            Assert.StartsWith("[", json);
            Assert.EndsWith("]", json);
        }

        [Fact]
        public void EmitsAnEmptyArrayRatherThanNothingWhenNobodyHoldsTheRole()
        {
            Assert.Equal("[]", RoleHolders.ToJson(new List<RoleHolder>()));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter RoleHoldersTests`
Expected: FAIL — the name `RoleHolders` does not exist.

- [ ] **Step 3: Implement the merge**

Create `plugins/OutcomeTesting.Plugins/RoleHolders.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>One person's relationship to one role, from both sources at once (AD-089).</summary>
    public sealed class RoleHolder
    {
        public string Email { get; set; }
        public string Name { get; set; }

        /// <summary>Null when no al_userrolemapping row exists — a portal-only grant.</summary>
        public Guid? MappingId { get; set; }

        /// <summary>Null when there is no mapping; otherwise whether that mapping is active.</summary>
        public bool? MappingActive { get; set; }

        /// <summary>Whether the contact is actually associated with the web role.</summary>
        public bool Associated { get; set; }
    }

    /// <summary>
    /// Joins the mapping table to the web role associations for one role.
    ///
    /// Pure on purpose: every case worth testing is a disagreement between the two
    /// sources, and none of them needs a service to express. The plug-in does the two
    /// reads and hands the rows here.
    ///
    /// Work email is the join key (AD-010), compared case-insensitively because the two
    /// sources are written by different paths and a difference of casing is not a
    /// different person.
    /// </summary>
    public static class RoleHolders
    {
        public static List<RoleHolder> Merge(
            IEnumerable<Entity> mappings,
            IEnumerable<Entity> associatedContacts)
        {
            var byEmail = new Dictionary<string, RoleHolder>(StringComparer.OrdinalIgnoreCase);

            foreach (var mapping in mappings ?? Enumerable.Empty<Entity>())
            {
                var email = (mapping.GetAttributeValue<string>("al_useremail") ?? string.Empty).Trim();
                var holder = Find(byEmail, email);
                holder.MappingId = mapping.Id;
                holder.MappingActive = CommandHelpers.IsActive(mapping);
            }

            foreach (var contact in associatedContacts ?? Enumerable.Empty<Entity>())
            {
                var email = (contact.GetAttributeValue<string>("emailaddress1") ?? string.Empty).Trim();
                var holder = Find(byEmail, email);
                holder.Associated = true;

                var name = contact.GetAttributeValue<string>("fullname");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    holder.Name = name.Trim();
                }
            }

            return byEmail.Values.OrderBy(h => h.Email, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static RoleHolder Find(IDictionary<string, RoleHolder> byEmail, string email)
        {
            RoleHolder holder;
            if (!byEmail.TryGetValue(email, out holder))
            {
                holder = new RoleHolder { Email = email, Name = null, Associated = false };
                byEmail[email] = holder;
            }

            return holder;
        }

        /// <summary>
        /// The holders as a JSON array. Custom API response properties are String, Boolean
        /// or Integer only, so a list travels as JSON in a String — the precedent is
        /// al_ImportCases. Hand-built because the plug-in targets net462 with no serializer.
        /// </summary>
        public static string ToJson(IEnumerable<RoleHolder> holders)
        {
            var builder = new StringBuilder("[");
            var first = true;

            foreach (var holder in holders ?? Enumerable.Empty<RoleHolder>())
            {
                if (!first)
                {
                    builder.Append(",");
                }

                first = false;
                builder.Append("{\"email\":\"").Append(ImportRules.JsonEscape(holder.Email ?? string.Empty)).Append("\"");
                builder.Append(",\"name\":");
                builder.Append(holder.Name == null ? "null" : "\"" + ImportRules.JsonEscape(holder.Name) + "\"");
                builder.Append(",\"mappingId\":");
                builder.Append(holder.MappingId.HasValue ? "\"" + holder.MappingId.Value.ToString("D") + "\"" : "null");
                builder.Append(",\"mappingActive\":");
                builder.Append(holder.MappingActive.HasValue ? (holder.MappingActive.Value ? "true" : "false") : "null");
                builder.Append(",\"associated\":").Append(holder.Associated ? "true" : "false");
                builder.Append("}");
            }

            return builder.Append("]").ToString();
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter RoleHoldersTests`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/RoleHolders.cs plugins/OutcomeTesting.Plugins.Tests/RoleHoldersTests.cs
git commit -m "feat(plugins): join role mappings to web role associations (AD-089)"
```

---

### Task 3: `al_GetRoleHolders`

**Files:**
- Create: `plugins/customapi/al_GetRoleHolders.customapi.json`
- Create: `plugins/OutcomeTesting.Plugins/GetRoleHoldersPlugin.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/GetRoleHoldersPluginTests.cs`

**Interfaces:**
- Consumes: `RoleHolders.Merge`, `RoleHolders.ToJson`, `WebRoleRegistry.ContactRelationship`,
  `PermissionHelpers.EnsureAppPermission`, `CommandHelpers.RetrieveAll`.
- Produces: `GetRoleHoldersPlugin.Read(IOrganizationService service, string roleCode) -> List<RoleHolder>`.
  Response property `Holders` (String, JSON array).

- [ ] **Step 1: Write the contract**

Create `plugins/customapi/al_GetRoleHolders.customapi.json`:

```json
{
  "$comment": "Contract for the GetRoleHolders read (AD-003, AD-089). Deployed via the registration console (plugins/OutcomeTesting.Registration) and added to the OutcomeTesting solution via a solution-file import (src/customapis/al_GetRoleHolders). Custom API parameter Type codes: 0=Boolean, 10=String.",
  "customApi": {
    "uniquename": "al_GetRoleHolders",
    "name": "al_GetRoleHolders",
    "displayname": "Get Role Holders",
    "description": "Everyone holding one application role, from both sources: the al_userrolemapping rows and the Power Pages web role associations. Returns the raw facts so the caller can see where the two disagree (AD-089). Enforces permission.manage View. Reads only; writes nothing and audits nothing.",
    "bindingtype": 0,
    "boundentitylogicalname": null,
    "isfunction": false,
    "isprivate": false,
    "allowedcustomprocessingsteptype": 0,
    "executeprivilegename": null,
    "pluginType": "OutcomeTesting.Plugins.GetRoleHoldersPlugin"
  },
  "requestParameters": [
    {
      "uniquename": "RoleCode",
      "name": "RoleCode",
      "displayname": "Role code",
      "description": "The web role name, which is what al_rolecode carries (AD-044).",
      "type": 10,
      "isoptional": false
    }
  ],
  "responseProperties": [
    {
      "uniquename": "Holders",
      "name": "Holders",
      "displayname": "Holders",
      "description": "JSON array of { email, name, mappingId, mappingActive, associated }. mappingId and mappingActive are null when the role was granted in Power Pages and never mirrored.",
      "type": 10
    }
  ]
}
```

- [ ] **Step 2: Write the failing test**

Create `plugins/OutcomeTesting.Plugins.Tests/GetRoleHoldersPluginTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The read behind the role detail screen. The two reads it performs are a
    /// QueryExpression over the mapping table and a FetchXML over the contact-side
    /// intersect — the intersect cannot be queried from the role side.
    /// </summary>
    public class GetRoleHoldersPluginTests
    {
        private const string Role = "AL Portal - Tax Reviewer";

        private static FakeOrganizationService WithMapping(string email, bool active)
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", email,
                "al_rolecode", Role,
                "statecode", new OptionSetValue(active ? 0 : 1));
            return svc;
        }

        private static void ReturnsAssociatedContacts(FakeOrganizationService svc, params string[] emails)
        {
            var rows = emails.Select(email =>
            {
                var contact = new Entity("contact", Guid.NewGuid());
                contact["emailaddress1"] = email;
                contact["fullname"] = "Person " + email;
                return contact;
            }).ToList();

            svc.FetchResults.Enqueue(new EntityCollection(rows));
        }

        [Fact]
        public void ReportsSomeoneGrantedOnlyInPowerPages()
        {
            var svc = new FakeOrganizationService();
            ReturnsAssociatedContacts(svc, "portal@ascotlloyd.co.uk");

            var holder = Assert.Single(GetRoleHoldersPlugin.Read(svc, Role));
            Assert.Null(holder.MappingId);
            Assert.True(holder.Associated);
        }

        [Fact]
        public void ReportsAWithdrawnMappingThatIsStillAssociated()
        {
            var svc = WithMapping("z@ascotlloyd.co.uk", active: false);
            ReturnsAssociatedContacts(svc, "z@ascotlloyd.co.uk");

            var holder = Assert.Single(GetRoleHoldersPlugin.Read(svc, Role));
            Assert.False(holder.MappingActive);
            Assert.True(holder.Associated);
        }

        [Fact]
        public void ReportsAnActiveMappingWhoseAssociationIsGone()
        {
            var svc = WithMapping("gone@ascotlloyd.co.uk", active: true);
            ReturnsAssociatedContacts(svc);

            var holder = Assert.Single(GetRoleHoldersPlugin.Read(svc, Role));
            Assert.True(holder.MappingActive);
            Assert.False(holder.Associated);
        }

        [Fact]
        public void LeavesOutMappingsForOtherRoles()
        {
            var svc = WithMapping("tax@ascotlloyd.co.uk", active: true);
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", "planner@ascotlloyd.co.uk",
                "al_rolecode", "AL Portal - Planner",
                "statecode", new OptionSetValue(0));
            ReturnsAssociatedContacts(svc);

            var holder = Assert.Single(GetRoleHoldersPlugin.Read(svc, Role));
            Assert.Equal("tax@ascotlloyd.co.uk", holder.Email);
        }

        [Fact]
        public void AsksForTheRoleByNameOnTheContactSideIntersect()
        {
            var svc = WithMapping("a@ascotlloyd.co.uk", active: true);
            ReturnsAssociatedContacts(svc);

            GetRoleHoldersPlugin.Read(svc, Role);

            var fetch = Assert.Single(svc.FetchXml);
            Assert.Contains(WebRoleRegistry.ContactRelationship, fetch);
            Assert.Contains(Role, fetch);
        }

        [Fact]
        public void EscapesARoleNameCarryingXmlSoTheFetchStaysWellFormed()
        {
            var svc = new FakeOrganizationService();
            ReturnsAssociatedContacts(svc);

            GetRoleHoldersPlugin.Read(svc, "Tax & <Advice>");

            Assert.Contains("Tax &amp; &lt;Advice&gt;", Assert.Single(svc.FetchXml));
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter GetRoleHoldersPluginTests`
Expected: FAIL — the name `GetRoleHoldersPlugin` does not exist.

- [ ] **Step 4: Implement the plug-in**

Create `plugins/OutcomeTesting.Plugins/GetRoleHoldersPlugin.cs`:

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side read GetRoleHolders (AD-003, AD-089). Registered against the Custom API
    /// message <c>al_GetRoleHolders</c>.
    ///
    /// This exists because the browser cannot perform the second read at all: the
    /// contact-to-web-role intersect has no many-to-many on mspp_webrole, so it is
    /// reachable only from the contact side and only by FetchXML, and the generated client
    /// has no $expand. Without this the app can show the mapping table and call it the
    /// truth, which is the state AD-089 closes.
    ///
    /// Reads only. It writes nothing and therefore audits nothing — there is no decision
    /// here to record. Runs as the plug-in user because it reports on everyone holding the
    /// role, not on the caller.
    /// </summary>
    public class GetRoleHoldersPlugin : PluginBase
    {
        private const string InRoleCode = "RoleCode";
        private const string OutHolders = "Holders";

        private const string MappingEntity = "al_userrolemapping";

        public GetRoleHoldersPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(GetRoleHoldersPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var systemService = localPluginContext.PluginUserService;

            var roleCode = CommandHelpers.GetRequiredString(context, InRoleCode);

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "permission.manage", PermissionHelpers.AccessView);

            context.OutputParameters[OutHolders] = RoleHolders.ToJson(Read(systemService, roleCode));
        }

        /// <summary>
        /// The two reads and the join. Public and static so the disagreement cases are
        /// testable without standing up a plug-in context.
        /// </summary>
        public static List<RoleHolder> Read(IOrganizationService service, string roleCode)
        {
            var trimmed = (roleCode ?? string.Empty).Trim();

            var query = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet("al_useremail", "al_rolecode", "statecode"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_rolecode", ConditionOperator.Equal, trimmed);
            var mappings = CommandHelpers.RetrieveAll(service, query);

            return RoleHolders.Merge(mappings, ContactsHolding(service, trimmed));
        }

        /// <summary>
        /// Contacts associated with the role. Starts at contact because the intersect hangs
        /// off contact, not off the role — the same reason WebRoleRegistry.RolesForContact
        /// does, in the opposite direction.
        /// </summary>
        private static List<Entity> ContactsHolding(IOrganizationService service, string roleName)
        {
            var fetch =
                "<fetch>" +
                  "<entity name='contact'>" +
                    "<attribute name='contactid'/>" +
                    "<attribute name='emailaddress1'/>" +
                    "<attribute name='fullname'/>" +
                    "<link-entity name='" + WebRoleRegistry.ContactRelationship + "' from='contactid' to='contactid' intersect='true'>" +
                      "<link-entity name='powerpagecomponent' from='powerpagecomponentid' to='powerpagecomponentid' alias='role'>" +
                        "<filter><condition attribute='name' operator='eq' value='" +
                          System.Security.SecurityElement.Escape(roleName) + "'/></filter>" +
                      "</link-entity>" +
                    "</link-entity>" +
                  "</entity>" +
                "</fetch>";

            var rows = new List<Entity>();
            foreach (var row in service.RetrieveMultiple(new FetchExpression(fetch)).Entities)
            {
                rows.Add(row);
            }

            return rows;
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter GetRoleHoldersPluginTests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add plugins/customapi/al_GetRoleHolders.customapi.json plugins/OutcomeTesting.Plugins/GetRoleHoldersPlugin.cs plugins/OutcomeTesting.Plugins.Tests/GetRoleHoldersPluginTests.cs
git commit -m "feat(plugins): al_GetRoleHolders reads both sources for one role (AD-089)"
```

---

### Task 4: `al_GetMyRoles`

**Files:**
- Create: `plugins/customapi/al_GetMyRoles.customapi.json`
- Create: `plugins/OutcomeTesting.Plugins/GetMyRolesPlugin.cs`
- Modify: `plugins/OutcomeTesting.Plugins/PermissionHelpers.cs` (expose the resolver)
- Test: `plugins/OutcomeTesting.Plugins.Tests/GetMyRolesPluginTests.cs`

**Interfaces:**
- Consumes: `PermissionHelpers` role resolution, `WebRoleRegistry.ExcludedFromResolution`.
- Produces: `PermissionHelpers.ResolveRoleCodesForEmail(IOrganizationService service, string email) -> List<string>`.
  Response property `RoleCodes` (String, JSON array of strings).

- [ ] **Step 1: Write the failing test**

Create `plugins/OutcomeTesting.Plugins.Tests/GetMyRolesPluginTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// What the client is told it holds. This has to be the SAME union the server enforces
    /// with, because the whole defect AD-089 closes is the client deriving a different
    /// answer from the mapping table alone.
    /// </summary>
    public class GetMyRolesPluginTests
    {
        private const string Email = "person@ascotlloyd.co.uk";

        private static FakeOrganizationService WithContact(Guid contactId)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", contactId, "emailaddress1", Email, "fullname", "A Person");
            return svc;
        }

        private static void ReturnsWebRoles(FakeOrganizationService svc, params string[] names)
        {
            var rows = new List<Entity>();
            foreach (var name in names)
            {
                var row = new Entity("contact", Guid.NewGuid());
                row["role.name"] = new AliasedValue("powerpagecomponent", "name", name);
                rows.Add(row);
            }

            svc.FetchResults.Enqueue(new EntityCollection(rows));
        }

        [Fact]
        public void UnionsTheMappingTableWithTheWebRoleAssociations()
        {
            var contactId = Guid.NewGuid();
            var svc = WithContact(contactId);
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", Email,
                "al_rolecode", "AL Portal - Planner",
                "statecode", new OptionSetValue(0));
            ReturnsWebRoles(svc, "AL Portal - Tax Reviewer");
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                Guid.NewGuid(),
                WebRoleRegistry.NameAttr, "AL Portal - Tax Reviewer",
                WebRoleRegistry.AuthenticatedAttr, false);

            var codes = PermissionHelpers.ResolveRoleCodesForEmail(svc, Email);

            Assert.Contains("AL Portal - Planner", codes);
            Assert.Contains("AL Portal - Tax Reviewer", codes);
        }

        [Fact]
        public void LeavesOutARoleAutoGrantedToEveryone()
        {
            var svc = WithContact(Guid.NewGuid());
            ReturnsWebRoles(svc, "Administrators");
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                Guid.NewGuid(),
                WebRoleRegistry.NameAttr, "Administrators",
                WebRoleRegistry.AuthenticatedAttr, true);

            Assert.DoesNotContain("Administrators", PermissionHelpers.ResolveRoleCodesForEmail(svc, Email));
        }

        [Fact]
        public void LeavesOutAWithdrawnMapping()
        {
            var svc = WithContact(Guid.NewGuid());
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", Email,
                "al_rolecode", "AL Portal - Planner",
                "statecode", new OptionSetValue(1));
            ReturnsWebRoles(svc);

            Assert.Empty(PermissionHelpers.ResolveRoleCodesForEmail(svc, Email));
        }

        [Fact]
        public void EmitsAJsonArrayOfStrings()
        {
            Assert.Equal("[]", GetMyRolesPlugin.ToJson(new List<string>()));
            Assert.Equal("[\"A\",\"B\"]", GetMyRolesPlugin.ToJson(new List<string> { "A", "B" }));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter GetMyRolesPluginTests`
Expected: FAIL — `ResolveRoleCodesForEmail` and `GetMyRolesPlugin` do not exist.

- [ ] **Step 3: Expose the resolver**

In `PermissionHelpers.cs`, add a public wrapper beside `GetActiveRoles`. Do not duplicate
the union — call the existing private method so there is exactly one implementation:

```csharp
/// <summary>
/// The role codes a person holds, as the gate resolves them (AD-089).
///
/// Public so al_GetMyRoles can hand the client the SAME answer the server enforces with.
/// The client used to derive its own from the mapping table alone, which meant a
/// portal-side assignment authorised writes the UI would not offer.
///
/// Built-in al_approle values are not returned: they have no code to match a permission
/// rule on, and AD-087 stopped offering the picklist. A caller holding only a picklist
/// role still resolves server-side through GetActiveRoles, unchanged.
/// </summary>
public static List<string> ResolveRoleCodesForEmail(IOrganizationService service, string email)
{
    return GetActiveRoles(service, (email ?? string.Empty).Trim()).RoleCodes;
}
```

- [ ] **Step 4: Implement the plug-in**

Create `plugins/OutcomeTesting.Plugins/GetMyRolesPlugin.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side read GetMyRoles (AD-003, AD-089). Registered against the Custom API
    /// message <c>al_GetMyRoles</c>.
    ///
    /// Answers "what do I hold" for the caller, so the client stops deriving an answer the
    /// server disagrees with. No permission is enforced: the caller is asking about
    /// themselves, and gating it would make the sign-in path depend on a permission that
    /// cannot be resolved until the path completes.
    /// </summary>
    public class GetMyRolesPlugin : PluginBase
    {
        private const string OutRoleCodes = "RoleCodes";

        public GetMyRolesPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(GetMyRolesPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var systemService = localPluginContext.PluginUserService;

            var email = PermissionHelpers.CallerEmail(systemService, context);
            var codes = string.IsNullOrWhiteSpace(email)
                ? new List<string>()
                : PermissionHelpers.ResolveRoleCodesForEmail(systemService, email);

            context.OutputParameters[OutRoleCodes] = ToJson(codes);
        }

        /// <summary>A JSON array of strings; net462 with no serializer, as elsewhere here.</summary>
        public static string ToJson(IEnumerable<string> codes)
        {
            var builder = new StringBuilder("[");
            var first = true;

            foreach (var code in codes ?? new List<string>())
            {
                if (!first)
                {
                    builder.Append(",");
                }

                first = false;
                builder.Append("\"").Append(ImportRules.JsonEscape(code ?? string.Empty)).Append("\"");
            }

            return builder.Append("]").ToString();
        }
    }
}
```

**Before writing the plug-in, extract the caller lookup.** `PermissionHelpers` already
resolves the caller's email inside `EnsureAppPermission`, privately. Promote exactly that
code — do not write a second copy, or the gate and this API can disagree about who is
calling:

```csharp
/// <summary>
/// The work email of the caller (AD-010). Promoted out of EnsureAppPermission so
/// al_GetMyRoles reports on the same person the gate authorises, resolved the same way.
/// </summary>
public static string CallerEmail(IOrganizationService service, IPluginExecutionContext context)
{
    // Move the existing body from EnsureAppPermission here verbatim, then call this from
    // EnsureAppPermission so there is one implementation.
}
```

- [ ] **Step 5: Write the contract**

Create `plugins/customapi/al_GetMyRoles.customapi.json`:

```json
{
  "$comment": "Contract for the GetMyRoles read (AD-003, AD-089). Deployed via the registration console (plugins/OutcomeTesting.Registration) and added to the OutcomeTesting solution via a solution-file import (src/customapis/al_GetMyRoles). Custom API parameter Type codes: 0=Boolean, 10=String.",
  "customApi": {
    "uniquename": "al_GetMyRoles",
    "name": "al_GetMyRoles",
    "displayname": "Get My Roles",
    "description": "The role codes the caller holds, resolved exactly as the server gate resolves them (AD-089). No permission is enforced: the caller is asking about themselves, and gating it would make the sign-in path depend on a permission that cannot be resolved until that path completes. Reads only; writes nothing and audits nothing.",
    "bindingtype": 0,
    "boundentitylogicalname": null,
    "isfunction": false,
    "isprivate": false,
    "allowedcustomprocessingsteptype": 0,
    "executeprivilegename": null,
    "pluginType": "OutcomeTesting.Plugins.GetMyRolesPlugin"
  },
  "requestParameters": [],
  "responseProperties": [
    {
      "uniquename": "RoleCodes",
      "name": "RoleCodes",
      "displayname": "Role codes",
      "description": "JSON array of the role codes the caller holds: active mappings unioned with web role associations, less any role auto-granted to everyone (AD-090).",
      "type": 10
    }
  ]
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: PASS, whole suite.

- [ ] **Step 7: Commit**

```bash
git add plugins/customapi/al_GetMyRoles.customapi.json plugins/OutcomeTesting.Plugins/GetMyRolesPlugin.cs plugins/OutcomeTesting.Plugins/PermissionHelpers.cs plugins/OutcomeTesting.Plugins.Tests/GetMyRolesPluginTests.cs
git commit -m "feat(plugins): al_GetMyRoles gives the client the server's own answer (AD-089)"
```

---

### Task 5: `al_AdoptRoleAssignment`

The write. Two decisions, each converging both sources on one state, so all four rows of
the truth table are covered without a decision per row.

**Files:**
- Create: `plugins/customapi/al_AdoptRoleAssignment.customapi.json`
- Create: `plugins/OutcomeTesting.Plugins/AdoptRoleAssignmentPlugin.cs`
- Modify: `plugins/OutcomeTesting.Plugins.Tests/FakeOrganizationService.cs` (`Associate`, `Disassociate`)
- Test: `plugins/OutcomeTesting.Plugins.Tests/AdoptRoleAssignmentPluginTests.cs`

**Interfaces:**
- Consumes: `AssignUserRolePlugin.AssociateWebRole`, `AssignUserRolePlugin.DisassociateWebRole`,
  `WebRoleRegistry.FindByName`, `CommandHelpers.FindAuditByKey`, `CommandHelpers.WriteAuditEvent`,
  `CommandHelpers.SetState`.
- Produces: `AdoptRoleAssignmentPlugin.Apply(IOrganizationService service, string email, string roleCode, bool adopt) -> AdoptResult`
  with fields `MappingId` (Guid?), `Adopted` (bool), `Details` (string).

- [ ] **Step 1: Teach the fake to record associations**

Replace the two throwing members in `FakeOrganizationService`:

```csharp
/// <summary>Every Associate call, as (relationship, target, related).</summary>
public List<Tuple<string, EntityReference, EntityReference>> Associations { get; } =
    new List<Tuple<string, EntityReference, EntityReference>>();

/// <summary>Every Disassociate call, as (relationship, target, related).</summary>
public List<Tuple<string, EntityReference, EntityReference>> Disassociations { get; } =
    new List<Tuple<string, EntityReference, EntityReference>>();

public void Associate(
    string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
{
    foreach (var related in relatedEntities)
    {
        Associations.Add(Tuple.Create(
            relationship.SchemaName, new EntityReference(entityName, entityId), related));
    }
}

public void Disassociate(
    string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
{
    foreach (var related in relatedEntities)
    {
        Disassociations.Add(Tuple.Create(
            relationship.SchemaName, new EntityReference(entityName, entityId), related));
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `plugins/OutcomeTesting.Plugins.Tests/AdoptRoleAssignmentPluginTests.cs`:

```csharp
using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Reconciling a role held in two places (AD-089). Adopt converges on granted, Revoke
    /// converges on not granted, and each has to work from every inconsistent starting
    /// state rather than assuming one.
    /// </summary>
    public class AdoptRoleAssignmentPluginTests
    {
        private const string Email = "person@ascotlloyd.co.uk";
        private const string Role = "AL Portal - Tax Reviewer";

        private static readonly Guid ContactId = Guid.Parse("66666666-ffff-4fff-8fff-666666666666");
        private static readonly Guid RoleId = Guid.Parse("77777777-aaaa-4aaa-8aaa-777777777777");

        private static FakeOrganizationService Environment(bool withMapping, bool mappingActive)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, "emailaddress1", Email, "fullname", "A Person");
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                RoleId,
                WebRoleRegistry.NameAttr, Role,
                WebRoleRegistry.AuthenticatedAttr, false);

            if (withMapping)
            {
                svc.Seed(
                    "al_userrolemapping",
                    Guid.NewGuid(),
                    "al_useremail", Email,
                    "al_rolecode", Role,
                    "statecode", new OptionSetValue(mappingActive ? 0 : 1));
            }

            return svc;
        }

        [Fact]
        public void AdoptingAPortalOnlyGrantWritesTheMissingMapping()
        {
            var svc = Environment(withMapping: false, mappingActive: false);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, Email, Role, adopt: true);

            Assert.True(result.Adopted);
            Assert.NotNull(result.MappingId);
            Assert.Contains(svc.Creates, row => row.LogicalName == "al_userrolemapping");
        }

        [Fact]
        public void AdoptingAWithdrawnMappingReactivatesItRatherThanWritingASecond()
        {
            var svc = Environment(withMapping: true, mappingActive: false);

            AdoptRoleAssignmentPlugin.Apply(svc, Email, Role, adopt: true);

            Assert.DoesNotContain(svc.Creates, row => row.LogicalName == "al_userrolemapping");
            Assert.Contains(svc.Updates, row => row.LogicalName == "al_userrolemapping");
        }

        [Fact]
        public void AdoptingRepairsAMissingAssociation()
        {
            var svc = Environment(withMapping: true, mappingActive: true);

            AdoptRoleAssignmentPlugin.Apply(svc, Email, Role, adopt: true);

            Assert.Contains(
                svc.Associations,
                a => a.Item1 == WebRoleRegistry.ContactRelationship);
        }

        [Fact]
        public void RevokingRemovesTheAssociation()
        {
            var svc = Environment(withMapping: false, mappingActive: false);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, Email, Role, adopt: false);

            Assert.False(result.Adopted);
            Assert.Contains(
                svc.Disassociations,
                d => d.Item1 == WebRoleRegistry.ContactRelationship);
        }

        [Fact]
        public void RevokingAlsoWithdrawsAMappingWhereOneExists()
        {
            var svc = Environment(withMapping: true, mappingActive: true);

            AdoptRoleAssignmentPlugin.Apply(svc, Email, Role, adopt: false);

            var update = Assert.Single(svc.Updates.Where(r => r.LogicalName == "al_userrolemapping"));
            Assert.Equal(1, update.GetAttributeValue<OptionSetValue>("statecode").Value);
        }

        [Fact]
        public void RefusesAPersonWithNoContactRatherThanWritingAMappingNothingGrants()
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                RoleId,
                WebRoleRegistry.NameAttr, Role,
                WebRoleRegistry.AuthenticatedAttr, false);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AdoptRoleAssignmentPlugin.Apply(svc, "nobody@ascotlloyd.co.uk", Role, adopt: true));

            Assert.StartsWith(CommandHelpers.ValidationPrefix, error.Message);
        }

        [Fact]
        public void RefusesARoleThatDoesNotExist()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, "emailaddress1", Email, "fullname", "A Person");

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AdoptRoleAssignmentPlugin.Apply(svc, Email, "No Such Role", adopt: true));

            Assert.StartsWith(CommandHelpers.NotFoundPrefix, error.Message);
        }

        [Fact]
        public void SaysWhatItReconciledSoTheAuditLineIsReadable()
        {
            var svc = Environment(withMapping: true, mappingActive: false);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, Email, Role, adopt: true);

            Assert.Contains("Adopt", result.Details);
            Assert.Contains(Role, result.Details);
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter AdoptRoleAssignmentPluginTests`
Expected: FAIL — the name `AdoptRoleAssignmentPlugin` does not exist.

- [ ] **Step 4: Implement the plug-in**

Create `plugins/OutcomeTesting.Plugins/AdoptRoleAssignmentPlugin.cs`:

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command AdoptRoleAssignment (AD-003, AD-089). Registered against the
    /// Custom API message <c>al_AdoptRoleAssignment</c>.
    ///
    /// A role can be granted in two places: this app, which writes an audited mapping row
    /// AND the web role association, and Power Pages management, which writes the
    /// association alone. AD-089 settles what the second one means — it grants access, it
    /// is visible, and it is not authoritative until a person decides.
    ///
    /// Two decisions rather than four, because each CONVERGES both sources rather than
    /// patching a particular disagreement: Adopt makes both say granted, Revoke makes both
    /// say not granted. That covers a portal-only grant, a withdrawn mapping whose
    /// association survived, and an active mapping whose association was removed, without
    /// the caller having to say which one they are looking at.
    ///
    /// No new audit command value is minted: adopting reuses AssignUserRole and revoking
    /// reuses SetRoleAssignmentActive, per AD-076, with the details line recording that
    /// this reconciled a portal-side assignment.
    /// </summary>
    public class AdoptRoleAssignmentPlugin : PluginBase
    {
        private const string InUserEmail = "UserEmail";
        private const string InRoleCode = "RoleCode";
        private const string InDecision = "Decision";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutMappingId = "MappingId";
        private const string OutAdopted = "Adopted";
        private const string OutAuditEventId = "AuditEventId";

        private const string MappingEntity = "al_userrolemapping";

        private const int CommandAssignUserRole = 120910773;
        private const int CommandSetRoleAssignmentActive = 120910788;

        public sealed class AdoptResult
        {
            public Guid? MappingId { get; set; }
            public bool Adopted { get; set; }
            public string Details { get; set; }
        }

        public AdoptRoleAssignmentPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AdoptRoleAssignmentPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var systemService = localPluginContext.PluginUserService;

            var email = CommandHelpers.GetRequiredString(context, InUserEmail);
            var roleCode = CommandHelpers.GetRequiredString(context, InRoleCode);
            var decision = CommandHelpers.GetRequiredString(context, InDecision);

            var adopt = ParseDecision(decision);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var command = adopt ? CommandAssignUserRole : CommandSetRoleAssignmentActive;

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, command);
            if (existingAudit != null)
            {
                SetResponse(context, null, adopt, existingAudit.Id);
                return;
            }

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            var result = Apply(systemService, email, roleCode, adopt);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, command,
                (adopt ? "AdoptRoleAssignment " : "RevokeRoleAssignment ") + email,
                MappingEntity, result.MappingId ?? Guid.Empty, null,
                result.Details, idempotencyKey, context);

            SetResponse(context, result.MappingId, result.Adopted, auditId);
        }

        /// <summary>
        /// Converges both sources. Public and static so every starting state is testable
        /// without a plug-in context.
        /// </summary>
        public static AdoptResult Apply(
            IOrganizationService service, string email, string roleCode, bool adopt)
        {
            var trimmedEmail = (email ?? string.Empty).Trim();
            var trimmedRole = (roleCode ?? string.Empty).Trim();

            var webRole = WebRoleRegistry.FindByName(service, trimmedRole);
            if (webRole == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.NotFoundPrefix + "No web role is named " + trimmedRole + ".");
            }

            var contact = WebRoleRegistry.FindContactByEmail(service, trimmedEmail);
            if (contact == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "No contact has the work email " + trimmedEmail +
                    ", so this role cannot be reconciled for them.");
            }

            var mapping = FindMapping(service, trimmedEmail, trimmedRole);
            var details = (adopt ? "Adopt " : "Revoke ") + trimmedRole + " for " + trimmedEmail +
                ". Reconciled a role held in two places (AD-089).";

            if (adopt)
            {
                // Association first: it is what actually grants the role, and a mapping row
                // written against a role the person does not hold would be the same lie in
                // the other direction.
                AssignUserRolePlugin.AssociateWebRole(service, trimmedEmail, webRole.Id);

                if (mapping == null)
                {
                    var row = new Entity(MappingEntity);
                    row["al_useremail"] = trimmedEmail;
                    row["al_rolecode"] = trimmedRole;
                    row["al_approle"] = null;
                    var newId = service.Create(row);
                    return new AdoptResult { MappingId = newId, Adopted = true, Details = details };
                }

                if (!CommandHelpers.IsActive(mapping))
                {
                    CommandHelpers.SetState(service, MappingEntity, mapping.Id, true);
                }

                return new AdoptResult { MappingId = mapping.Id, Adopted = true, Details = details };
            }

            AssignUserRolePlugin.DisassociateWebRole(service, trimmedEmail, trimmedRole);

            if (mapping != null && CommandHelpers.IsActive(mapping))
            {
                CommandHelpers.SetState(service, MappingEntity, mapping.Id, false);
            }

            return new AdoptResult
            {
                MappingId = mapping == null ? (Guid?)null : mapping.Id,
                Adopted = false,
                Details = details,
            };
        }

        private static Entity FindMapping(IOrganizationService service, string email, string roleCode)
        {
            var query = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet("al_useremail", "al_rolecode", "statecode"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_useremail", ConditionOperator.Equal, email);
            query.Criteria.AddCondition("al_rolecode", ConditionOperator.Equal, roleCode);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count == 0 ? null : found[0];
        }

        private static bool ParseDecision(string decision)
        {
            var value = (decision ?? string.Empty).Trim();
            if (value.Equals("Adopt", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.Equals("Revoke", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.ValidationPrefix + "Decision must be Adopt or Revoke.");
        }

        private static void SetResponse(
            IPluginExecutionContext context, Guid? mappingId, bool adopted, Guid auditId)
        {
            context.OutputParameters[OutMappingId] = mappingId.HasValue ? mappingId.Value.ToString("D") : string.Empty;
            context.OutputParameters[OutAdopted] = adopted;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
```

- [ ] **Step 5: Write the contract**

Create `plugins/customapi/al_AdoptRoleAssignment.customapi.json`:

```json
{
  "$comment": "Contract for the AdoptRoleAssignment command (AD-003, AD-089). Deployed via the registration console (plugins/OutcomeTesting.Registration) and added to the OutcomeTesting solution via a solution-file import (src/customapis/al_AdoptRoleAssignment). Custom API parameter Type codes: 0=Boolean, 10=String.",
  "customApi": {
    "uniquename": "al_AdoptRoleAssignment",
    "name": "al_AdoptRoleAssignment",
    "displayname": "Adopt Role Assignment",
    "description": "Reconciles a role held in two places (AD-089). Adopt converges both sources on granted; Revoke converges both on not granted. Enforces permission.manage Manage and writes an immutable Audit Event naming the administrator who decided (BR-012). Reuses the AssignUserRole and SetRoleAssignmentActive command values rather than minting a new one (AD-076).",
    "bindingtype": 0,
    "boundentitylogicalname": null,
    "isfunction": false,
    "isprivate": false,
    "allowedcustomprocessingsteptype": 0,
    "executeprivilegename": null,
    "pluginType": "OutcomeTesting.Plugins.AdoptRoleAssignmentPlugin"
  },
  "requestParameters": [
    {
      "uniquename": "UserEmail",
      "name": "UserEmail",
      "displayname": "User email",
      "description": "Work email of the person whose assignment is being reconciled (AD-010).",
      "type": 10,
      "isoptional": false
    },
    {
      "uniquename": "RoleCode",
      "name": "RoleCode",
      "displayname": "Role code",
      "description": "The web role name, which is what al_rolecode carries (AD-044).",
      "type": 10,
      "isoptional": false
    },
    {
      "uniquename": "Decision",
      "name": "Decision",
      "displayname": "Decision",
      "description": "Adopt or Revoke. Adopt makes both sources say the person holds the role; Revoke makes both say they do not.",
      "type": 10,
      "isoptional": false
    },
    {
      "uniquename": "IdempotencyKey",
      "name": "IdempotencyKey",
      "displayname": "Idempotency key",
      "description": "Stable key for the intent; a replay returns the same result.",
      "type": 10,
      "isoptional": false
    }
  ],
  "responseProperties": [
    {
      "uniquename": "MappingId",
      "name": "MappingId",
      "displayname": "Mapping id",
      "description": "Id of the al_userrolemapping row written or reconciled. Empty when revoking a portal-only grant that never had one.",
      "type": 10
    },
    {
      "uniquename": "Adopted",
      "name": "Adopted",
      "displayname": "Adopted",
      "description": "True when the decision was Adopt, false when it was Revoke.",
      "type": 0
    },
    {
      "uniquename": "AuditEventId",
      "name": "AuditEventId",
      "displayname": "Audit event id",
      "description": "Id of the Audit Event written for this decision.",
      "type": 10
    }
  ]
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: PASS, whole suite.

- [ ] **Step 7: Commit**

```bash
git add plugins/customapi/al_AdoptRoleAssignment.customapi.json plugins/OutcomeTesting.Plugins/AdoptRoleAssignmentPlugin.cs plugins/OutcomeTesting.Plugins.Tests/
git commit -m "feat(plugins): al_AdoptRoleAssignment converges a role held in two places (AD-089)"
```

---

### Task 6: Register, deploy and wire the three APIs

Nothing in Tasks 3–5 is reachable from the app until this is done, and
`operations.test.ts` fails deliberately until each API is added to the code app.

**Files:**
- Create: `src/customapis/al_GetMyRoles/`, `src/customapis/al_GetRoleHolders/`, `src/customapis/al_AdoptRoleAssignment/`
- Modify: `app/src/services/commands/operations.ts`
- Modify: `app/.power/schemas/appschemas/dataSourcesInfo.ts` (written by the CLI, not by hand)

**Interfaces:**
- Produces: three entries in `COMMAND_OPERATIONS`, resolvable as
  `dataSourcesInfo['al_getmyroles' | 'al_getroleholders' | 'al_adoptroleassignment']`.

- [ ] **Step 1: Build and register the plug-ins**

```bash
export DOTNET_ROLL_FORWARD=LatestMajor
dotnet build plugins/OutcomeTesting.Plugins
dotnet run --project plugins/OutcomeTesting.Registration -- registerall <environment-url>
```

`DOTNET_ROLL_FORWARD` is required; without it the registration console will not start.

- [ ] **Step 2: Add each API to the solution**

```bash
dotnet run --project plugins/OutcomeTesting.Registration -- addtosolution <environment-url>
```

Then confirm `src/customapis/al_GetMyRoles`, `src/customapis/al_GetRoleHolders` and
`src/customapis/al_AdoptRoleAssignment` exist, matching the existing folders.

- [ ] **Step 3: Add each API to the code app**

```bash
cd app
npx pa app add dataverse-api --api-name al_GetMyRoles
npx pa app add dataverse-api --api-name al_GetRoleHolders
npx pa app add dataverse-api --api-name al_AdoptRoleAssignment
```

Use `npx pa`, not `pac code` — `pac code add-data-source` cannot resolve the connection
non-interactively.

- [ ] **Step 4: Declare the operations**

In `app/src/services/commands/operations.ts`, add to `COMMAND_OPERATIONS`, keeping the
existing alphabetical order:

```ts
  'al_AdoptRoleAssignment',
  'al_AssignCase',
  'al_AssignUserRole',
  'al_CompleteRemediation',
  'al_CreateExportBatch',
  'al_CreateRole',
  'al_CreateUser',
  'al_GenerateExport',
  'al_GetMyRoles',
  'al_GetRoleHolders',
  'al_ImportCases',
```

- [ ] **Step 5: Run the registration guard**

Run: `cd app && npx vitest run src/services/commands/operations.test.ts`
Expected: PASS. A failure names the exact `pa app add dataverse-api` command that is missing —
run it and re-run the test rather than editing `dataSourcesInfo.ts` by hand.

- [ ] **Step 6: Commit**

```bash
git add app/src/services/commands/operations.ts app/.power src/customapis
git commit -m "chore(deploy): register the three role reconciliation APIs with the code app"
```

---

### Task 7: Classify a holder in the client

**Files:**
- Modify: `app/src/features/admin/roleDetail.ts`
- Test: `app/src/features/admin/roleDetail.test.ts` (append; the file already exists)

**Interfaces:**
- Consumes: nothing new.
- Produces: `RoleHolderRecord` (`{ email, name, mappingId, mappingActive, associated }`),
  `HolderState` (`'consistent' | 'portal-only' | 'withdrawn-still-granted' | 'association-missing'`),
  `classifyHolder(record: RoleHolderRecord) -> { state: HolderState; label: string; canAdopt: boolean; canRevoke: boolean }`.

- [ ] **Step 1: Write the failing test**

Append to `app/src/features/admin/roleDetail.test.ts`:

```ts
describe('classifyHolder', () => {
  const base = {
    email: 'a@ascotlloyd.co.uk',
    name: 'A Person',
    mappingId: 'map-1' as string | null,
    mappingActive: true as boolean | null,
    associated: true,
  };

  it('reports an assignment made in the app as needing nothing', () => {
    const result = classifyHolder(base);
    expect(result.state).toBe('consistent');
    expect(result.canAdopt).toBe(false);
    expect(result.canRevoke).toBe(false);
  });

  it('reports a grant made only in Power Pages as unadopted', () => {
    const result = classifyHolder({ ...base, mappingId: null, mappingActive: null });
    expect(result.state).toBe('portal-only');
    expect(result.canAdopt).toBe(true);
    expect(result.canRevoke).toBe(true);
  });

  it('reports a withdrawn assignment that is still granted', () => {
    // The dangerous one: the app says withdrawn and the access is live.
    const result = classifyHolder({ ...base, mappingActive: false });
    expect(result.state).toBe('withdrawn-still-granted');
    expect(result.canRevoke).toBe(true);
  });

  it('reports an assignment whose association was removed elsewhere', () => {
    const result = classifyHolder({ ...base, associated: false });
    expect(result.state).toBe('association-missing');
    expect(result.canAdopt).toBe(true);
  });

  it('gives every state a label that says what is true, not what is wrong', () => {
    const states = [
      classifyHolder(base),
      classifyHolder({ ...base, mappingId: null, mappingActive: null }),
      classifyHolder({ ...base, mappingActive: false }),
      classifyHolder({ ...base, associated: false }),
    ];
    expect(new Set(states.map((s) => s.label)).size).toBe(4);
    expect(states.every((s) => s.label.length > 0)).toBe(true);
  });

  it('treats a withdrawn assignment with no association as fully withdrawn', () => {
    const result = classifyHolder({ ...base, mappingActive: false, associated: false });
    expect(result.state).toBe('consistent');
    expect(result.canAdopt).toBe(false);
  });
});
```

Add `classifyHolder` and the types to the existing import at the top of the file.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd app && npx vitest run src/features/admin/roleDetail.test.ts`
Expected: FAIL — `classifyHolder` is not exported by `./roleDetail`.

- [ ] **Step 3: Implement it**

Append to `app/src/features/admin/roleDetail.ts`:

```ts
/** One person's relationship to one role, as al_GetRoleHolders reports it. */
export interface RoleHolderRecord {
  email: string;
  name: string | null;
  /** Null when no mapping row exists — the role was granted in Power Pages only. */
  mappingId: string | null;
  mappingActive: boolean | null;
  associated: boolean;
}

export type HolderState =
  | 'consistent'
  | 'portal-only'
  | 'withdrawn-still-granted'
  | 'association-missing';

export interface HolderClassification {
  state: HolderState;
  label: string;
  canAdopt: boolean;
  canRevoke: boolean;
}

/**
 * What the two sources say, and what an administrator can do about it (AD-089).
 *
 * Adopt converges on granted and Revoke converges on not granted, so a state offers the
 * action that would make the two sources agree — and a state where they already agree
 * offers neither.
 *
 * A withdrawn mapping with no association is `consistent`: both sources say the person
 * does not hold the role, which is exactly what a completed withdrawal looks like.
 */
export function classifyHolder(record: RoleHolderRecord): HolderClassification {
  const held = record.mappingActive === true;

  if (record.mappingId === null && record.associated) {
    return {
      state: 'portal-only',
      label: 'Granted in Power Pages, not adopted',
      canAdopt: true,
      canRevoke: true,
    };
  }

  if (record.mappingActive === false && record.associated) {
    return {
      state: 'withdrawn-still-granted',
      label: 'Withdrawn in app, still granted',
      canAdopt: true,
      canRevoke: true,
    };
  }

  if (held && !record.associated) {
    return {
      state: 'association-missing',
      label: 'Assigned in app, association missing',
      canAdopt: true,
      canRevoke: true,
    };
  }

  return {
    state: 'consistent',
    label: held ? 'Assigned in app' : 'Withdrawn',
    canAdopt: false,
    canRevoke: false,
  };
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd app && npx vitest run src/features/admin/roleDetail.test.ts`
Expected: PASS, 18 tests (the 12 already there plus 6).

- [ ] **Step 5: Commit**

```bash
git add app/src/features/admin/roleDetail.ts app/src/features/admin/roleDetail.test.ts
git commit -m "feat(app): classify where the two role sources disagree (AD-089)"
```

---

### Task 8: Show the real holders, with Adopt and Revoke

**Files:**
- Create: `app/src/features/admin/useRoleHolders.ts`
- Create: `app/src/services/commands/roleReconciliation.ts`
- Modify: `app/src/features/admin/RoleDetailPage.tsx`

**Interfaces:**
- Consumes: `classifyHolder`, `RoleHolderRecord`, `executeCommand`.
- Produces: `useRoleHolders(roleCode: string, reloadKey: number) -> RoleHoldersState`,
  `adoptRoleAssignment(input: { userEmail; roleCode; decision: 'Adopt' | 'Revoke'; idempotencyKey }) -> Promise<CommandResult<AdoptOutput>>`.

- [ ] **Step 1: Write the command wrapper**

Create `app/src/services/commands/roleReconciliation.ts`:

```ts
import { executeCommand, type CommandResult } from './commandClient';

/**
 * Reconciling a role held in two places (AD-089). Adopt makes both sources say granted;
 * Revoke makes both say not granted. The server writes the Audit Event naming the
 * administrator who decided — which is the reason this is a command and not a schedule.
 */
export interface AdoptRoleAssignmentInput {
  userEmail: string;
  roleCode: string;
  decision: 'Adopt' | 'Revoke';
  idempotencyKey: string;
}

export interface AdoptRoleAssignmentOutput {
  MappingId: string;
  Adopted: boolean;
  AuditEventId: string;
}

export function adoptRoleAssignment(
  input: AdoptRoleAssignmentInput,
): Promise<CommandResult<AdoptRoleAssignmentOutput>> {
  return executeCommand<AdoptRoleAssignmentOutput>('al_AdoptRoleAssignment', {
    UserEmail: input.userEmail,
    RoleCode: input.roleCode,
    Decision: input.decision,
    IdempotencyKey: input.idempotencyKey,
  });
}
```

- [ ] **Step 2: Write the hook**

Create `app/src/features/admin/useRoleHolders.ts`:

```ts
import { useEffect, useState } from 'react';
import { executeCommand } from '../../services/commands/commandClient';
import { logTechnical } from '../../services/errors';
import type { RoleHolderRecord } from './roleDetail';

export type RoleHoldersState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; holders: RoleHolderRecord[] };

/**
 * Everyone holding one role, from both sources (AD-089).
 *
 * A Custom API rather than a table read, because the contact-to-web-role intersect has no
 * many-to-many on the role and the generated client has no $expand — the browser cannot
 * perform the second half of this read at all.
 */
export function useRoleHolders(roleCode: string, reloadKey = 0): RoleHoldersState {
  const [state, setState] = useState<RoleHoldersState>({ status: 'loading' });

  useEffect(() => {
    if (!roleCode) return;
    let cancelled = false;
    setState({ status: 'loading' });

    executeCommand<{ Holders: string }>('al_GetRoleHolders', { RoleCode: roleCode })
      .then((result) => {
        if (cancelled) return;
        if (!result.ok) {
          logTechnical('role holders load', result.message);
          setState({ status: 'unavailable', reason: 'The people holding this role could not be loaded right now.' });
          return;
        }

        try {
          setState({ status: 'ready', holders: JSON.parse(result.data.Holders) as RoleHolderRecord[] });
        } catch (error) {
          logTechnical('role holders parse', error);
          setState({ status: 'unavailable', reason: 'The people holding this role could not be read.' });
        }
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('role holders load', error);
        setState({ status: 'unavailable', reason: 'The people holding this role could not be loaded right now.' });
      });

    return () => {
      cancelled = true;
    };
  }, [roleCode, reloadKey]);

  return state;
}
```

- [ ] **Step 3: Rework the holders panel**

In `RoleDetailPage.tsx`, swap the data source and add the handler:

```tsx
const holdersState = useRoleHolders(role?.code ?? '', reloadKey);
const holders = holdersState.status === 'ready' ? holdersState.holders : [];

async function onReconcile(holder: RoleHolderRecord, decision: 'Adopt' | 'Revoke') {
  if (rowBusy || !role) return;
  setRowBusy(holder.email);
  setRowNotice(null);

  const token = `adopt:${holder.email}:${role.code}:${decision}`;
  const result = await adoptRoleAssignment({
    userEmail: holder.email,
    roleCode: role.code,
    decision,
    idempotencyKey: intent.keyFor(token),
  });

  setRowBusy(null);
  if (result.ok) {
    intent.release(token);
    setRowNotice({
      tone: 'ok',
      message: decision === 'Adopt'
        ? `${role.name} adopted for ${holder.email}. The decision is now in the audit trail.`
        : `${role.name} revoked for ${holder.email} in both Power Pages and this app.`,
    });
    reloadConfig();
  } else {
    setRowNotice({ tone: 'error', message: result.message });
  }
}
```

Then replace the holders table body. `buildRoleHolders` is no longer called here — keep it
and its tests, since the mapping-only view is still what `SecurityConfigPage` reads:

```tsx
{holders.map((holder) => {
  const state = classifyHolder(holder);
  return (
    <tr key={holder.email} data-inactive={state.state === 'consistent' && !holder.mappingActive ? 'true' : undefined}>
      <td>{holder.name ?? '—'}</td>
      <td>{holder.email || '—'}</td>
      <td>{state.label}</td>
      {canManage ? (
        <td className="security__row-actions">
          {state.canAdopt ? (
            <button
              type="button"
              className="security__link-btn"
              onClick={() => onReconcile(holder, 'Adopt')}
              disabled={rowBusy !== null}
            >
              {rowBusy === holder.email ? 'Working…' : 'Adopt'}
            </button>
          ) : null}
          {state.canRevoke ? (
            <button
              type="button"
              className="security__link-btn"
              onClick={() => onReconcile(holder, 'Revoke')}
              disabled={rowBusy !== null}
            >
              Revoke
            </button>
          ) : null}
        </td>
      ) : null}
    </tr>
  );
})}
```

Replace the caveat paragraph — the one saying portal-side grants would not appear — with:

```tsx
<p className="security__hint">
  Both sources at once: assignments made here, and web roles granted directly in Power
  Pages. A role granted in Power Pages already grants access; adopting it records the
  decision in the audit trail, and revoking it removes the access (AD-089).
</p>
```

- [ ] **Step 4: Verify**

Run: `cd app && npx tsc -b && npm run lint && npx vitest run`
Expected: typecheck clean, 0 lint errors (9 pre-existing warnings remain), all tests pass.

- [ ] **Step 5: Commit**

```bash
git add app/src/features/admin app/src/services/commands/roleReconciliation.ts
git commit -m "feat(app): adopt or revoke a role granted outside the app (AD-089)"
```

---

### Task 9: Let the server answer "what do I hold", and record the decisions

**Files:**
- Modify: `app/src/app/permissions/PermissionProvider.tsx`
- Modify: `app/src/features/admin/SecurityConfigPage.tsx`
- Modify: `knowledge/decision-log.md`
- Modify: `docs/2026-09-04-outstanding-work.md`

**Interfaces:**
- Consumes: `al_GetMyRoles` via `executeCommand`.
- Produces: no new exports.

- [ ] **Step 1: Switch the provider to the server's answer**

In `loadPermissions`, replace the `Al_userrolemappingsService` read used for **roles** with
`al_GetMyRoles`, keeping the `al_pagepermission` read and the contact deactivation check:

```ts
const rolesResult = await executeCommand<{ RoleCodes: string }>('al_GetMyRoles', {});

// Bootstrap / fail-open is unchanged in spirit and now has one more trigger: if the API
// cannot answer, the permissive set stands in exactly as it did when the mapping table
// was unreadable. Server commands still gate every write, so this only affects what the
// UI offers.
let roles: string[];
if (!rolesResult.ok) {
  roles = [...APP_ROLES];
} else {
  const codes = JSON.parse(rolesResult.data.RoleCodes) as string[];
  roles = codes.length === 0 ? [] : codes;
}
```

Keep the existing "no mapping rows at all means grant every role" bootstrap by treating an
**API failure** as the bootstrap case, and an empty array as a genuine "holds nothing".
Leave the comment explaining why, updated to name the API.

- [ ] **Step 2: Show the discrepancy count where an administrator looks first**

In `SecurityConfigPage.tsx`, under the "Assign a role" panel hint, render a line naming the
roles that have something to reconcile. Keep it silent when there are none — a permanent
zero teaches people to ignore it:

```tsx
{unreconciled.length > 0 ? (
  <p className="security__notice security__notice--error" role="status">
    {unreconciled.length === 1
      ? '1 role has an assignment made outside this app: '
      : `${unreconciled.length} roles have assignments made outside this app: `}
    {unreconciled.map((entry, index) => (
      <span key={entry.code}>
        {index > 0 ? ', ' : ''}
        <Link to={roleDetailPath(entry.code)}>{entry.name}</Link>
      </span>
    ))}
  </p>
) : null}
```

`unreconciled` comes from calling `al_GetRoleHolders` per active role and keeping those with
any holder whose `classifyHolder(...).state !== 'consistent'`. **Fan-out warning:** that is
one API call per role. With the eight business web roles this environment has, that is
acceptable; if the role list ever grows past roughly twenty, move the count server-side into
a dedicated read rather than looping harder here.

- [ ] **Step 3: Verify**

Run: `cd app && npx tsc -b && npm run lint && npx vitest run`
Expected: typecheck clean, 0 lint errors, all tests pass.

- [ ] **Step 4: Record the decisions**

Append AD-089 and AD-090 to `knowledge/decision-log.md` in the existing three-column format
(decision, rationale, date). Take the wording from the spec's Decisions section — including
that scheduled reconciliation was **rejected, not deferred**, and that OD-033 is the
evidence behind AD-090.

In `docs/2026-09-04-outstanding-work.md` §5, replace the two "Still owed" bullets: the role
detail view is built, and the conflict rule is settled by AD-089/AD-090.

- [ ] **Step 5: Commit**

```bash
git add app/src/app/permissions/PermissionProvider.tsx app/src/features/admin/SecurityConfigPage.tsx knowledge/decision-log.md docs/2026-09-04-outstanding-work.md
git commit -m "feat(app): the server answers what a caller holds (AD-089, AD-090)"
```

---

## What the unit tests deliberately do not cover

Idempotent replay and the `EnsureAppPermission` gate both live in
`ExecuteDataversePlugin`, which needs a plug-in execution context the test project does not
build — this is why every existing command here extracts its logic to a public static and
tests that instead. The consequence is honest rather than hidden: **the replay guard and
the permission gate on all three APIs are covered by the deploy verification below, not by
`dotnet test`.** Do not claim them as unit-tested.

## Verification after deploy

None of this is provable until it reaches DEV. After `pa app push`:

1. Associate a contact with a role in Power Pages management only. Open the role detail
   screen. Expect that person listed as "Granted in Power Pages, not adopted".
2. Adopt it. Expect a mapping row and an Audit Event naming you.
3. Withdraw it in the app, then re-associate in Power Pages. Expect "Withdrawn in app,
   still granted" — **not** "Withdrawn". This is the case the whole plan exists for.
4. Sign in as someone holding a role only through a portal association. Expect the app to
   offer the screens that role grants, where before it showed "No access" everywhere.
5. Confirm a role flagged `mspp_authenticatedusersrole` grants nothing (AD-090).
6. Adopt the same assignment twice with the same idempotency key. Expect one mapping row
   and one Audit Event, not two — this is the replay guard the unit tests cannot reach.
7. Sign in as someone without `permission.manage` and call each of the three APIs. Expect
   `al_GetMyRoles` to answer and the other two to refuse with `UNAUTHORIZED:`.
