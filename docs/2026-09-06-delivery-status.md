# Delivery status — 2026-09-06

Written at commit `faab916`. Sits alongside `docs/2026-09-04-outstanding-work.md`, which is
the register of what is left. Supersedes `docs/2026-09-05-delivery-status.md` as the current
status; that one still records how PP-15 was proved.

Every environment claim below was verified by query against `Env_AQ_Dev` after the fact.
Where something did **not** land, it says so.

**The day in one line: the app stopped keeping its own idea of who people are and what they
may do, and three latent defects surfaced only because the paths were finally run.**

---

## 1. The app had two registries and neither was real

The starting position, established by read-only probe rather than from the code:

| Table | Rows | |
|---|---|---|
| `contact` | 3 | Dev Account, Service Account, Sims Rad — all `@ascotlloyd.co.uk`, all with enabled Read-Write systemusers |
| `al_user` | 10 | Alex Adminki, Casey Checker, Jane Adviser … all `@example.com` seed data |

**The two sets did not intersect at any row.** The registry the app read was entirely
fictional, and the People screen was not a registry at all — `peopleDirectory.ts` built it in
memory from the adviser/paraplanner/checker name strings on `al_OutcomeCase`, and said so in
its own comment: it "does not claim to be a user directory", because the case carries no email
to join on.

`contact` is now the registry (AD-085) and `/people` is one directory carrying both views'
functionality (AD-086). `al_User` is decommissioned, not deleted — it ships in the managed
solution, so deleting it is a destructive ALM change (OD-037).

**Why this was safe, and how that was known before doing it:** authorisation never read
`al_User`. `PermissionHelpers` resolves roles from `al_userrolemapping` keyed on
`al_useremail` and treats a missing registry row as permitted — and the environment's only
role mapping was for an email that had **no `al_user` row at all**. That single fact is what
made deleting all ten rows a safe operation rather than a lockout.

### 1.1 DEV data, after

```
al_user rows                     0
cases                           13   all adviser/paraplanner/checker are the 3 real people
  allocated                      9   3 each, through the claim path so review instances exist
  closed                         3   full reviews, 36 mandatory questions answered each
  left in the queue              4   IO-100010 + the three IO-DEV-VERIFY fixtures
al_caseassignment                9   was 0 — no case had ever been allocated
```

---

## 2. Roles come from web roles

Application roles are now the Power Pages web roles (AD-087). The list is read live from
`mspp_webrole`, so a role added on the portal appears in the app with no code change.

**This environment runs the enhanced portal data model.** There is no `adx_webrole`. Roles are
`mspp_webrole`, a surface over `powerpagecomponent`, and the contact link is
`powerpagecomponent_mspp_webrole_contact` reached **from the contact side** — `mspp_webrole`
carries no many-to-many of its own, so a query starting at the role finds nothing. Two more
quirks, both of which cost time: `mspp_webrole` does not answer a plain `QueryExpression`, and
it ignores `top` in FetchXML.

**No schema changed.** `al_PagePermission` and `al_UserRoleMapping` already carry
`al_rolecode`, the free-text column AD-044 reserved for custom roles, which outranks the
`al_approle` picklist. Web role names travel through it untouched.

| Capability | How | Evidence |
|---|---|---|
| Pull from web roles | `useRoles` reads `mspp_webrole` live | a portal-side role needs no code change |
| Assign | `al_AssignUserRole` associates the contact **and** mirrors the row | `proveroles`: assign → association exists; withdraw → gone |
| Manage | `al_CreateRole`/`al_UpdateRole` write `mspp_webrole` | `probewebrole`: create, rename, associate, disassociate, delete all passed |
| Configure | `al_PagePermission` keyed on `al_rolecode` | 52 rules seeded across 8 roles |

Role resolution is the **union** of the associations and the mapping table. Reading only the
associations would withdraw access the mapping had; reading only the mapping would make the
web roles decorative. The union is what made replacing the vocabulary outright safe, and the
`al_approle` path is deliberately still read so nothing already assigned is silently revoked.

---

## 3. Three latent defects, all found by running the path

None of these were visible from the code. Each surfaced the first time something actually
exercised the path.

### 3.1 Every allocation had been broken since it was written

`AssignCasePlugin.ResolveAssignee` filtered `systemuser` on **`internalemailid`**, which is
not an attribute. Dataverse rejected the whole query, so the method threw for every caller and
**every allocation failed** — the `al_AssignCase` command and the portal claim path alike.

It survived because nothing had ever allocated a case in DEV (`al_caseassignment` held zero
rows), and because `AssignCasePluginTests` seeded the same wrong name.
`FakeOrganizationService` has no metadata to contradict a column that does not exist, so the
test agreed with the bug rather than catching it. The tests now seed the real column, and a
new case seeds the shape a Dataverse user actually has — a mailbox address and a separate UPN
— so resolution has to work off the mailbox column alone.

### 3.2 The app could never have assigned a web role

Dataverse reports the `AppRole` parameter as optional and the contract declares it optional,
but the platform's request validator still refused any call that **omitted** it — which is
every code-based assignment, i.e. every web role assignment. Both role commands now always
send both keys, with the unused one empty.

### 3.3 Every web role was resolving as "Adviser"

`al_approle` carries a schema default. Every mapping row written with only a role code came
back **also** carrying that default — and `PermissionHelpers.GetActiveRoles` read the picklist
**first**, skipping the code whenever it found one. Three assignments to three different web
roles all resolved identically, to a role nobody had assigned.

Both halves are fixed: precedence inverted to match AD-044 and the client, and both writers
now null the picklist explicitly (AD-088). Found by reading back what the seed actually wrote
rather than trusting that it wrote what was intended.

---

## 4. Exports: the button was never broken

Reported as "the download button stays inactive after generating a batch". It was not a UI
defect. The batch generated `al_rowcount: 0` with zero `al_exportrecord` rows, because
`GenerateExportPlugin` collects only cases at `Closed` and **none of the 13 were closed**.
`ExportMenu` disables on an empty row set, correctly. The application simply never said so.

Three fixes, all in the client:

- The unreadable error was a slicing defect in `commandClient.classify`, which kept the whole
  CDS fault body after the `PRECONDITION:` prefix. It now ends the message at the JSON
  boundary. The rule moved to `services/commands/failures.ts` because `commandClient` imports
  the Power Apps data client, which cannot resolve outside the hosted runtime — nothing
  importing it could be unit-tested at all.
- **Generate** is offered only on a `Draft` batch, so the plug-in's AD-042 guard is no longer
  how users discover the rule.
- A zero-row batch reports in the error tone and says why, and the disabled Download carries
  the reason on the control.

Once three cases were driven to `Closed`, `proveexport` returned `RowCount=3` with both graded
columns populated. The path is proved, not assumed.

---

## 5. New tooling in `plugins/OutcomeTesting.Registration`

| Command | Writes? | Purpose |
|---|---|---|
| `fetch <orgUrl> <fetchXml\|@file>` | No | Ad hoc FetchXML. Refuses anything that is not a query, so it cannot become a write path. |
| `relationships <orgUrl> <entity>` | No | A table's many-to-many relationships. The only reliable way to learn an intersect's logical name. |
| `probewebrole <orgUrl>` | Yes, then cleans up | Feasibility probe: creates, renames, associates, disassociates and deletes a web role. |
| `migratetocontacts <orgUrl> --confirm <orgUrl>` | Yes | Moves DEV onto the contact registry. Re-runnable. |
| `seedwebroles <orgUrl> --confirm <orgUrl>` | Yes | Mirrors the associations and seeds the permission rules. Idempotent. |
| `proveroles <orgUrl>` | Yes, then cleans up | Assigns and withdraws a web role through the commands the app calls, and checks the association really moved. |
| `proveexport <orgUrl>` | Yes | Runs CreateExportBatch then GenerateExport and reports what was written. |

The two migrations take `--confirm` with the org URL repeated, the same discipline the
`verify` modes use, because they rewrite business data and the Audit Events they cause are
immutable (NFR-AUD-01).

---

## 6. One correction worth carrying

The first pass at making `migratetocontacts` re-runnable **blanked the checker name on all
nine allocated cases**: phase 2 cleared the field, and phase 3 then skipped the cases it had
already allocated, so `ClaimCasePlugin` never re-stamped them. Caught on verification, not in
review. Phase 2 now derives the checker from the case's live allocation instead of clearing
it, which is correct in both directions — an unallocated case still ends up naming nobody.

A second correction came out of writing this document rather than the code. `AL Portal -
Planner` was initially seeded with `page.cases` View only, on the reasoning that no
requirement described it. **That was wrong**: OD-019 is *Implemented*, and the portal binds
Planner alongside `AL Portal - Adviser Remediation` on the same Contact-scoped remediation
permission and page rule. It now carries the same remediation authority, and the test pins the
two roles as equal rather than pinning the under-granted shape.

---

## 7. State of `Env_AQ_Dev`

| | |
|---|---|
| Plug-in assembly | Deployed, 22 Custom APIs registered and all solution members |
| `al_user` | 0 rows |
| `contact` | 3, all with web role assignments |
| Role mappings | 3 web role mirrors + 1 legacy `al_approle` row, retained as the fallback |
| `al_pagepermission` | 52 web role rules + 17 legacy picklist rules |
| Cases | 13: 9 allocated, 3 closed, 4 queued |
| Export | Proved end to end, 3 rows with both graded columns populated |
| Code App | Pushed, `appversion` `2026-09-06T12:40:13Z` |

Tests: **167** app, **316** plug-in. `tsc` and `eslint` clean (9 pre-existing warnings).

Deployment record, including the two things that cost time:
`docs/deployment/2026-09-06-contacts-webroles-deployment.md`.
