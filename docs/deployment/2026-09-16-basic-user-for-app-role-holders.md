# App roles are additive to Basic User, not a replacement for it

**2026-09-16. Follows AD-142.** Adam Strumidlo still could not import cases in TEST after the
morning's `prvReadRole` / `prvReadpowerpagecomponent` grant. The fault was the same shape and
the next privilege along:

```
OutcomeTesting.Plugins.ImportCasesPlugin could not complete. OrganizationServiceFault:
SecLib::CheckPrivilege failed. User: 6d08a459-d0b1-f111-aaac-002248c654cd,
PrivilegeName: prvReadEntity, PrivilegeId: a3311f47-2134-44ee-a258-6774018d4bc3,
Required Depth: Basic, BusinessUnitId: 381efaf5-4195-f111-b8db-0022481bd3f2,
MetadataCache Privileges Count: 13663, User Privileges Count: 127
```

`User Privileges Count: 127` is AD-142's 125 plus the two privileges granted that morning, so
the earlier fix did land. The gate simply got one line further.

## What was wrong

Resolved by query rather than inferred: `a3311f47-2134-44ee-a258-6774018d4bc3` is
**`prvReadEntity`** (accessright 1, Read) — read on entity *metadata*, not on any application
table. Like `prvReadRole` it is a core platform privilege, so the id is identical in DEV and
TEST; it resolved in DEV and matched in TEST.

In both environments it is granted by `Basic User`, `System Administrator` and the platform
service roles — and by **neither** `Outcome Testing App User` nor `Outcome Testing App Admin`.

Adam held exactly one security role in TEST:

| Role | |
|---|---|
| Outcome Testing App Admin | `457c6daf-28a2-f111-b8dd-e4fade069307` |

No Basic User. That is the whole defect. Every other app-role holder in TEST already had it:

| Person | Roles held before the fix |
|---|---|
| Zoe Ramwell | Basic User + Outcome Testing App Admin |
| Gill Philpott | Basic User + Outcome Testing App User |
| Adam Strumidlo | Outcome Testing App Admin **only** |

DEV never showed it because every DEV human is a System Administrator — the same blind spot
recorded on 2026-09-14 and again in AD-142.

## Why the grant was not repeated a third time

`prvReadRole` → `prvReadpowerpagecomponent` → `prvReadEntity` is three missing platform
privileges found the same way: a real user in TEST hitting one, a grant, then the next. Each
fix revealed the next because the app roles were being written as if they were self-sufficient.

They are not, and were never meant to be. Dataverse's model is that a custom role is
**additive** to Basic User. Basic User carries 492 privileges in DEV; the App Admin role
carries 127. Replicating that baseline one fault at a time is unbounded — `prvReadEntity` is
simply the third of a long tail.

So the fix is the missing baseline role, not a third privilege on the app roles. The role XML
and `grantsecurity` are deliberately unchanged.

## What was deployed

```
pac admin assign-user --environment https://org37995f36.crm11.dynamics.com/ \
  --user Adam.Strumidlo@ascotlloyd.co.uk --role "Basic User"
```

Business unit `381efaf5-4195-f111-b8db-0022481bd3f2` — the one named in the fault. **TEST
only**; DEV needed no change, and PROD is a separate decision.

Verified by query afterwards:

- Adam now holds `Basic User` (`8c22faf5-4195-f111-b8db-0022481bd3f2`) and
  `Outcome Testing App Admin`.
- `Basic User` in TEST grants `prvReadEntity` at depth 8 (Global), which satisfies the
  fault's `Required Depth: Basic`.

Adam may need a fresh sign-in for the privilege cache to pick the new role up.

## Every app-role holder checked, not just Adam

The whole gate was then walked for each of them against TEST, rather than checking the one
privilege that had just failed. `page.imports` is granted to `Administrators` (Manage),
`AL Portal - Outcome Testing Manager` (Edit) and `AL Portal - Portal Administrator` (Edit).

| Person | Basic User | Contact row | Roles granting page.imports | Can import |
|---|---|---|---|---|
| Zoe Ramwell | yes | Active | Administrators, Outcome Testing Manager, Portal Administrator | yes |
| Gill Philpott | yes | Active | Administrators | yes |
| Adam Strumidlo | yes (granted today) | Active | Administrators | yes |

Zoe was never exposed: she has held Basic User throughout, her contact row is active, and she
holds three of the roles that grant the page — more headroom than either of the others. Each
person's `al_userrolemapping` rows and Power Pages web roles agree, so none of them is carrying
the AD-089 drift the gate unions the two sources to survive.

**No DEV user holds either app role** — every DEV human is a System Administrator. That is the
mechanical reason this class of fault cannot appear in DEV, stated as a fact about the
environment rather than an impression.

## The standing rule this sets

**Anyone given an Outcome Testing app role must also hold `Basic User`.** The app roles grant
the application's own tables; Basic User grants the platform baseline underneath them. Granting
one without the other produces a `SecLib::CheckPrivilege` fault on a platform privilege, in a
plug-in, with no message a user can act on — which is what happened three times this week.

## What was not established

Which SDK call in the import actually triggers the metadata read. No plug-in on the
`ImportCasesPlugin` path calls metadata explicitly — `OptionLabels` holds the codebase's only
`RetrieveAttributeRequest` and is not reached from it. `PluginBase` wraps any
`FaultException<OrganizationServiceFault>` raised anywhere under the command, including from a
synchronous step downstream of `userService.Create`, and passes the inner platform text through
unchanged, so the wrapper names the outer command rather than the call that faulted. Pinning it
down needs the TEST plug-in trace log. The missing privilege is certain either way, and the
baseline role covers it regardless of which call asked.
