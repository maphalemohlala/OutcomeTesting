# Deployment — deactivation withdraws access again, and the signing key leaves git

Date: 2026-09-08
Target: `Env_AQ_Dev` (`org0b075da8`, environment `d50d27e8-cb3b-e718-b6e2-30aa92d944aa`)
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

Follows `docs/deployment/2026-09-07-identity-binding-and-tax-routing.md`. That record closed the
sign-in fault and left four decisions; this one settles them, and two of the four turned out to
be sitting on top of live defects rather than tidiness.

**The day in one line: following a register item nobody thought was urgent found that
deactivating a leaver withdrew nothing.**

---

## What was deployed, in order

| # | Step | Command | Result |
|---|---|---|---|
| 1 | Plug-in assembly, Release | `dotnet build -c Release` | 0 warnings, 0 errors |
| 2 | Plug-in types and Custom APIs | `registerall <orgUrl>` | 25 commands, all members of `OutcomeTesting` |
| 3 | Code App | `npm run build` then `npx pa app push` | Built clean, pushed successfully |
| 4 | Portal | `Deploy-Portal.ps1 -OrgUrl <orgUrl>` | Upload succeeded 19/19 in 18.70s, 13 of 13 permissions written and verified |

Suites at the deployed commit: **plug-ins 384/384** (was 376), **app 192/192**, `tsc` clean.

The portal upload emitted the familiar
`Updating table powerpagecomponent with record ID:f065878a-… FAILED … Does Not Exist` once and
carried on to completion. That line is expected and is documented in the 2026-09-07 record: the
`annotationid` it names is dead, and removing it is worse than keeping it because `pac` requires
the key whether or not the row exists.

---

## 1. Deactivating a leaver withdrew nothing

The register carried OD-037 as a housekeeping item: two unused tables, zero rows, delete them
when convenient. **"Nothing reads them" was wrong**, and checking before deleting is the only
reason this was found rather than caused.

`PermissionHelpers.IsRegisteredActive` queried `al_user` on **every permission check**.
`SetUserActivePlugin` has written the **contact's** `statecode`/`statuscode` since AD-085 — so
deactivation had already moved to Contact and only the *check* was left behind. `al_user` went
to zero rows the same day.

The failure needs both halves to see. The read is permissive on absence, deliberately: a
Dataverse user can legitimately predate their registry row, and refusing there would lock out
anyone the registry has not caught up with. Point that rule at a table nobody writes any more
and **it becomes a blanket yes.** A deactivated person kept full command access, and nothing
anywhere said so.

OD-010 makes deactivation the sanctioned alternative to deleting a leaver. That is precisely the
case it failed open on.

**An empty table and a deleted table look identical to a permissive read** — right up until the
query itself faults. That is also why deleting the tables first, as the register proposed, would
have been a self-inflicted outage on the authorisation path rather than a clean-up.

The read now asks `contact` and `ContactRegistry.IsActive`. It is public so a test can drive it:
the defect was unreachable from `EnsureAppPermission`, which needs a plug-in context, and that
unreachability is why it survived. Five tests pin it, including that rows in the retired
registry do not decide.

## 2. A second live defect, found on the way to the first

`al_SetPagePermission` validated a role code against **`al_role` alone**, and none of the eleven
`al_role` rows carries an `AL Portal - *` code. So configuring a page permission for any of the
seven roles the app actually offers was refused with "the role code does not match an active
role".

It stayed hidden because the 52 web role rules in DEV were written directly by `seedwebroles`,
never through the command. **Seeding a result is how a broken path stops being walked.**

Both call sites now use `AssignUserRolePlugin.RoleCodeExists`, which at this point accepted a
web role or a legacy `al_role`, matching what `al_AssignUserRole` already did. **The legacy half
was dropped later the same day** — see the addendum: it could only ever have matched a code
nothing carries, and it was the last thing reading `al_role`.

## 3. The sign-in now reaches the contact it is named after

The crossed `adx_externalidentity` is repointed: `svc.automate.aq`'s Entra object id resolved to
the **`Dev Account`** contact and now resolves to the **`Service Account`** contact, which is
what it is named after and which holds `Administrators` — reachable by a sign-in for the first
time.

```
Repointed e044a8e9-…: Dev Account -> Service Account <svc.automate.aq@ascotlloyd.co.uk>
  roles now reachable by that sign-in: Administrators
  Dev Account is now reachable by no sign-in; it holds: AL Portal - Tax Reviewer
```

`identities` now reports no `NOTE` line. `Dev Account` keeps Tax Reviewer and a Tax review with
no way in, which costs nothing: `Sims Rad` has held Tax Reviewer and its own Tax review since
2026-09-07, and its manager roles read every review including that one.

**`bindidentity` gained `--repoint` rather than overwriting.** Moving a binding takes a sign-in
away from whoever holds it today, which is a different act from granting one and should not be
what happens when a command reading as "add" is re-run. It also reports what the previous
contact can no longer be reached by — a contact left holding roles and no way in looks like
nothing at all from the environment.

## 4. The unused intersect is gone

`al_contact_al_outcomecase`, built on 2026-08-30 to test an access mechanic OD-022 then
rejected, is deleted from DEV and from `src/`. It held 0 rows.

`deleterelationship` refuses unless the relationship is custom, unmanaged and its intersect
empty, and reads back afterwards rather than trusting the call. The emptiness check is the one
that matters: an intersect with rows in it is data somebody is relying on, whatever a register
says it is.

## 5. Scope decisions taken, not gaps closed

Both by project owner direction, recorded so neither is re-raised as a defect:

- **PP-15 is met at five events.** The remaining events named in the requirement text are
  descoped. They were never specified in any requirement, knowledge file or design document.
  The five AD-035 enumerates are built, deployed and proved end to end.
- **OD-023, the support model, is out of scope.** The consequence is accepted rather than
  resolved: nothing fires on a timer, so with no stated hours of cover nothing tells a user when
  to expect a response, and there is no named hand-off for a change needing Power Pages or
  Dataverse admin.

## 6. The signing key is out of `main` — and still on GitHub

`OutcomeTesting.Plugins.snk` is untracked, ignored, and stripped from all 175 commits by
`git filter-repo` (installed via pip; neither it nor BFG was present). `main` was force-pushed
with `--force-with-lease`.

**The 37 broken citations are repaired.** Every commit SHA changed, and this project's records
are built on them — that, not the force-push, was the real cost. All 37 across 14 files were
repointed from filter-repo's own `commit-map`, so no SHA is guessed, using a token pattern that
refuses anything adjacent to a hex digit or a dash because the repository is full of hand-minted
GUIDs a loose match would corrupt in silence. Verified after: the most-cited GUIDs intact; every
remaining 7-hex token in `docs/` resolving to a commit except `0249002` and `1209107`, which
were never SHAs but a CRM email tracking token and an option-value block. The diff was 47
insertions against 47 deletions — what a pure remap should look like.

**It was still fetchable at this point, and the check that found it is the part worth keeping.**
Two remote branches had not been rewritten — closed in the addendum below:

| Branch | Remote (old) | Local (rewritten) | Unique work |
|---|---|---|---|
| `feat/role-assignment-conflict-rule` | `5b61849` | `441a1ab` | none — merged |
| `feature/outcome-creation-dev-deployment` | `cc112da` | `de224e3` | none — merged |

This was caught by checking `--all` reachability after the push instead of trusting it. **A
secret-removal that verifies only the branch it rewrote reports success while the secret is
still fetchable** — that is the method worth keeping, more than the outcome.

And the standing point: the key was public on GitHub, so it must be treated as compromised.
History removal does not un-publish it, and anyone who cloned or forked still holds it. **Only
rotation closes OD-025.**

Pre-rewrite state is preserved as a verified `git bundle` with the key alongside it, so every
step here is reversible.

---

## Verification, after the fact

| Check | Result |
|---|---|
| `identities` | Two bindings, both truthful, no `NOTE` line |
| Role associations | `Sims Rad` holds all four roles; `Service Account` holds `Administrators` |
| Review instances | 11 — 9 AQS, 2 Tax (one per Tax-capable contact) |
| `al_contact_al_outcomecase` | Gone; confirmed by a re-read that expects failure |
| Table permissions | 13 in source, 13 in DEV, nothing else |
| Component ids | 237 identities, no duplicates, no web file faults |
| Portal security assertions | All pass |
| Key in history | 0 commits and 0 objects on `main`; **present on two remote branches** |

## 7. Addendum — the rest of the batch, run later the same day

**The key is unreachable from every ref.** Both stale branches were force-pushed to their
rewritten versions: `feat/role-assignment-conflict-rule` `5b61849→441a1ab` and
`feature/outcome-creation-dev-deployment` `cc112da→de224e3`. Re-checked after a pruning fetch:
**0 commits touch the file and 0 `.snk` objects are reachable**, local or remote.

**`al_user` is deleted**, and getting there found what the register had not. The first attempt
was refused: *"referenced by 1 other component"*, which names a GUID and a type code and leaves
the rest as an exercise. `deletetable` now asks `RetrieveDependenciesForDeleteRequest` **before**
trying and resolves each dependency to something actionable. It reported component type 300, id
`5d9fc475-…` — **the code app's own `appId`**: the app still declared `al_user` and `al_role`
as data sources, which no amount of plug-in work would have revealed.

So the app had to change first:

- `Al_usersService` was exported and used nowhere — removed outright.
- `Al_rolesService` fed the full extract's **Roles** sheet, which means that report had been
  presenting the eleven retired `al_role` rows as the role list. It now reads
  `Mspp_webrolesService`, the actual registry since AD-087. That is a defect fixed, not a
  substitution: the sheet was naming roles nobody can hold.
- Both data sources removed from `power.config.json` (19 remain) and their generated models,
  services and exports deleted.

The `al_role` fallback in `RoleCodeExists` is gone too, so nothing reads either table.

**Order, and why it is not negotiable:** app and assembly deployed *first*, table deleted
*after*. Dataverse cannot see a plug-in's `RetrieveMultiple` as a dependency — it is a fault at
run time on whatever path reads first — so the environment must already be running code that
does not read the table.

**AD-013 round trip ran**, and the export is independent confirmation rather than a restatement:
`al_User` is absent (24 entities, was 25), and `al_contact_al_outcomecase` is absent from
`Other/Relationships*`, which agrees with the hand edit made when it was deleted. 25 Custom APIs,
14 SDK steps, and **34 plug-in types in the manifest against 34 classes the assembly builds**.
`Pack-Schema-Solution.ps1` succeeds at 209 entries with no assembly.

Deployed after the round trip: code app pushed, portal uploaded 19/19 in 16.86s with 13 of 13
permissions verified.

## Still owed

- **`deletetable <orgUrl> al_role --with-rows --confirm <orgUrl>`.** Nothing reads the table now
  and nothing references its eleven rows, but they *are* rows, so `deletetable` refuses without
  an explicit second opt-in — a command that silently destroys data is one nobody can safely
  re-run. `--with-rows` prints every row before deleting so the run's own output is the record.
  Blocked by the session's permission classifier. Once it runs, `src/Entities/al_Role` goes with
  the next round trip.
- **OD-025 rotation.** The key was public on GitHub, so it is compromised whatever the history
  now says. Nothing done today changes that, and only a new key does. GitHub may also retain the
  old objects until it garbage-collects, which is worth asking Support to force.
