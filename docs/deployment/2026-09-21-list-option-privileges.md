# The managed lists were readable by administrators only

**2026-09-21. AD-192.** The project owner reported that the Products field "is still a free
text". It was not. The field had been a tick list since that morning — but for them it had
nothing to tick, and a tick list with no options looks exactly like a field nobody built.

## What was wrong

`al_listoption` was created on 2026-09-21 and added to neither app security role. The table
feeds all five managed dropdowns, so for anyone who is not a Dataverse System Administrator:

- Case type, Product / solution type, Sample source and Pre or post check offered **"Not
  set" and nothing else**.
- Products offered **no checkboxes at all**.
- The case header fell back to `al_products`, the free-text column the tick list replaced.

The fault, from the browser console on the DEV app:

```
0x80040220  is missing prvReadal_ListOption privilege on OTC=10789 for entity 'al_listoption'
roleCount=2, privilegeCount=565
```

`useAllListOptions` logs that 403 and reports the list as unavailable, which the controls
render as an empty catalogue. That is the right behaviour for a failed read and it is also
why this went unseen for a day: nothing on the screen says "refused".

Every DEV human is a System Administrator. The reporter holds **Basic User + Outcome Testing
App User**, which is why they hit it and we did not — confirmed by query, not inferred:

```
systemusers(1fae3cf3-…)/systemuserroles_association?$select=name
→ Basic User, Outcome Testing App User
```

This is the **third** instance of one root cause. AD-142 (`prvReadRole`) and the 2026-09-14
note say the same thing: the roles were written from what the pages read, by people who
never hold those roles.

## What was deployed

`grantsecurity`, run against **DEV only**. No solution import, so no step registrations were
touched.

| Role | Privileges on `al_listoption` |
|---|---|
| Outcome Testing App User | Read, Append, AppendTo |
| Outcome Testing App Admin | Read, Create, Write, **Delete**, Append, AppendTo |

Two of those deserve a reason.

**Append and AppendTo, not just read.** The products a case covers are a many-to-many, and
`Associate` needs both ends appendable. The case end already had AppendTo from
`writeForEveryone`; this is the other end.

**Delete, for the admin role only** — unlike every other admin grant in `GrantSecurity`. The
Dropdown options page offers an outright delete for an option added by mistake and never
used. It is safe to grant because the role is not what decides it: the four lookups carry
`CascadeType.Restrict`, so Dataverse refuses the delete the moment any case holds the option
(AD-187), and `describeDeleteFailure` turns that refusal into the suggestion to retire.
Without the privilege the refusal would arrive as a permission fault instead, and an
administrator would be told they may not do a thing they may in fact do.

## Verified in DEV, in the browser

Before: `list options load … 403 … prvReadal_ListOption`.

After, in a fresh tab as the same user:

- No `list options load` error.
- Products shows its four placeholders as checkboxes.
- Sample source offers Random and Mandatory.
- Ticking `Product 4 (placeholder)` on case 900000002 and saving made the header read
  **"Product 4 (placeholder)"** rather than the free text. The fixture was restored
  afterwards — the case carries no products again.

## Not deployed anywhere else

TEST has no `al_listoption` table at all, so there is nothing there to grant. When the
solution reaches TEST the role XML carries the grant with it, the same way AD-142's did.

## Still open

The **portal** cannot read the table either, for a reason of its own. See the third section
below: AD-189's restart has since happened, and what is left is a table permission that is
not bound to any web role.

---

# And the page itself was not reachable

**Same day, found immediately after the above.** With the privilege granted, the dropdowns
filled — but the **Administration → Dropdown options** page still did not appear in the nav
for the reporter. Two separate reasons, both data rather than code.

## The role assignment was inactive

`al_userrolemapping` for Simunye Radingwana, before:

| Role code | State |
|---|---|
| AL Portal - Outcome Testing Manager | **Inactive** |
| AL Portal - Portal Administrator | **Inactive** |
| AL Portal - AQS Reviewer | Inactive |
| AL Portal - Tax Reviewer | Inactive |
| AL Portal - Adviser Remediation | Active |
| AL Portal - T&C Supervisor | Active |

Neither active role grants any `page.admin.*`, so the whole Administration group was hidden.
This looks like deliberate role-switching from earlier UAT, not a defect.

Reinstated through the command rather than by writing `statecode`, so the rule check and the
audit event both ran:

```
al_SetRoleAssignmentActive  MappingId=c75d30fe-…  Active=true
→ 200, AuditEventId 56ebd702-…
```

## page.admin.lists had only one rule

`al_pagepermission` carried exactly **one** rule for `page.admin.lists` — role code
`Administrators`, Manage. `permissions.ts` declares three. The two missing ones are the two
that every comparable admin page already has:

| Resource | Rules before | Rules after |
|---|---|---|
| page.admin.advisers | Administrators, Portal Administrator, Outcome Testing Manager | unchanged |
| page.admin.templates | Administrators, Portal Administrator, Outcome Testing Manager | unchanged |
| **page.admin.lists** | **Administrators only** | Administrators, Portal Administrator, Outcome Testing Manager |

Added through `al_SetPagePermission`, both `Conflict: false`:

```
RoleCode="AL Portal - Outcome Testing Manager" ResourceKey="page.admin.lists" AccessLevel="Manage"
RoleCode="AL Portal - Portal Administrator"    ResourceKey="page.admin.lists" AccessLevel="Manage"
```

Deliberately **not** copied from `page.admin.questions` or `page.admin.security`, which are
Administrator-only by design. Dropdown options follows advisers and templates: the checking
team maintains its own lists, which is the entire point of the migration.

`page.admin.lists` is not seeded by any verb — these rows are created by hand or through the
Security configuration page — so there is nothing in code to harden against a repeat. A new
admin page needs its rules created alongside it.

## Verified

Signed in as the reporter, after a reload: the Administration group appears, **Dropdown
options** is in it, the page opens, the list picker offers all five lists, and Products shows
its four placeholders with Add / Change / Remove.

---

# The portal: the restart happened, and a different thing is wrong

**Signed in on the DEV portal as the reporter to check.** `/_api/al_listoptions` now returns
**403**, not the 404 AD-189 recorded:

```
90040120  EntityPermissionReadIsMissing
"You don't have permission to read the al_listoption table."
```

A 404 meant the site did not know the table existed. A 403 means it does. **The site has been
restarted since AD-189 was written**, so that blocker is gone and this is a new one.

## What is actually wrong

Not the Web API settings — both exist and are correct:

```
Webapi/al_listoption/enabled = true
Webapi/al_listoption/fields  = al_listoptionid,al_name,al_list,al_sortorder,
                               al_effectivefrom,al_effectiveto
```

Not the permission record — `List Option - read` is present, active, Global scope, `read: true`.

**The web-role bindings are in the wrong place.** This site uses the enhanced data model, where
a table permission is a `powerpagecomponent` of type 18 and its web roles live **inside the
`content` JSON**. Compare the two:

| Component | `adx_entitypermission_webrole` in `content` | `/_api` result |
|---|---|---|
| Question Version - read | Administrators, Authenticated Users | **200** |
| List Option - read | **absent** | **403** |

The nine bindings the upload did create went into `mspp_entitypermission_webrole`, the legacy
projection, which the runtime does not read. Those nine rows are the **only** rows in that
intersect in the whole environment — every table permission that works has zero — which is the
proof that the runtime reads `content` and nothing else.

## The fix, not yet applied

One PATCH, adding the array to the component's `content`:

```
PATCH powerpagecomponents(a1000000-0000-4000-8000-000000000079)
content.adx_entitypermission_webrole = [Administrators, Authenticated Users]
```

**Administrators + Authenticated Users, matching `Question Version - read`**, rather than the
nine named roles the YAML lists. Every one of the nine is a subset of Authenticated Users, so
the effect is the same today — and a tenth role added later would otherwise get empty dropdowns
in silence, which is the exact failure this whole note is about.

## Applied and verified

Authorised by the project owner and applied to DEV (`204 No Content`, read back to confirm
the array is stored). **No site restart was needed.**

| Check | Before | After |
|---|---|---|
| `/_api/al_listoptions` | 403 | **200**, returning the options |
| Review page, Case type | empty | New advice, Ongoing |
| Review page, Product or solution type | empty | Protection, No change reviews, Another Option |
| Review page, Sample source | empty | Random, Mandatory |
| Review page, Pre or post check | empty | Pre (selected), Post |
| Review page, Products | no checkboxes | the four placeholders |

Console on the review page: **zero errors**.

The local YAML has been changed to the same two roles, so the file and the environment agree.
Left at nine, the next `pac powerpages upload` would push the old list back in the legacy
shape and undo this.

## AD-191 closed in the same pass

The AD-185 checklist-freshness call was 403-ing because `_al_questionid_value` was missing
from the field allowlist. The candidate fix was never confirmed. It is now:

```
/_api/al_questionversions?$select=al_questionversionid,al_effectivefrom,al_effectiveto,_al_questionid_value
→ 200
```
