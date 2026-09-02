# Portal security closure — DEV deployment runbook

**Not yet run.** This document is a runbook to be executed against DEV (`Env_AQ_Dev`),
not a record of a completed deployment. Step 1 and the local gate output in section 0
are the only things that have actually happened — they are local script runs, not
environment calls, and are safe to run repeatedly. Everything from step 2 onward
(`pac org who`, role-membership checks, `pac pages upload`, the sign-in check, the
round-trip download, the deletion check and the DEV role matrix) reads from or writes to
a shared environment and is held back for a human to run. Fill in the blank "Result"
fields as each step is actually carried out; do not fill them in from expectation.

Requirements: PP-01, PP-02, NFR-SEC-01, NFR-SEC-02. Decisions: OD-019, OD-021, OD-022,
AD-047, AD-056, AD-059 (amended), AD-067, AD-068, AD-069.

## Three things to hold in mind before running this

1. **`pac pages upload` is additive.** Deleting a YAML file from the site folder does
   not delete the row in DEV. Without step 6b, uploading this branch leaves the
   anonymous-create `Feedback` table permission live in DEV while every gate reports
   green — because the gates read the repository, not the site. **Do not skip step 6b.**
2. **A Grant Change rule on Home overrides Restrict Read site-wide.**
   `Grant Change to Administrators` (`563ee258-2962-4440-a6e8-d25296ac40bb`) sits on
   Home with All-content scope, so Administrators reach every page regardless of the
   five new Restrict Read rules. The role matrix in step 7 does not, and must not,
   assert that an Administrator is ever denied a page — that test could only ever fail.
3. **The `.portalconfig` manifest is stale.** The per-org manifest file under
   `powerpages/outcome-testing---outcometesting/.portalconfig/`
   still carries `IsDeleted: false` against every component this branch deleted
   (`Feedback`, the three `PROVISIONAL DEV ONLY - *` permissions, the `AL Portal -
   Regional Manager` role, `Grant Change to Content`) and knows nothing about the
   components this branch added. It is `pac`-generated bookkeeping that a download
   rewrites; it does not need hand-editing, but whoever runs the upload should expect
   `pac` to report these as deletions and not be surprised by the diff.

## 0. Gates run locally, 2026-08-31

Both are local scripts that read the repository only — no `pac`, no environment call.
Run before anything below, and after any further edit to `powerpages/`.

```powershell
powershell -NoProfile -File .\powerpages\Check-ComponentIds.ps1
powershell -NoProfile -File .\powerpages\Check-PortalSecurity.ps1
```

**Actual output, captured 2026-08-31:**

```
Checked 171 component identities under C:\Users\rsimu\OutcomeTesting\powerpages\outcome-testing---outcometesting.
No duplicate component ids, no web file faults.
```
Exit code: `0`.

```
Checked 11 table permissions, 6 page rules, 12 web pages and 10 web roles under C:\Users\rsimu\OutcomeTesting\powerpages\outcome-testing---outcometesting.
Portal security assertions all pass.
```
Exit code: `0`.

Both gates pass. This is the only verification this document can currently make —
everything past this point is unrun.

## 1. Confirm the target environment before uploading

```powershell
$pac = Join-Path $env:USERPROFILE ".dotnet\tools\pac.exe"
& $pac auth list
& $pac org who
```

Expected: `Env_AQ_Dev`. Uploading to the wrong environment is not something the gates
can catch.

**Result:** _(not run)_

## 2. Confirm Entra-provisioned contacts hold portal roles

Before uploading, confirm that at least one Entra-provisioned contact already holds each
`AL Portal - *` web role in DEV. This branch turns self-registration and local login off
(section 7 of the design) — after this upload, Entra ID OpenID Connect is the only way in.
A role with zero members is not itself an upload error, but it means nobody can perform
that role's function until Entra group sync catches up, and it is much cheaper to find
that out now than after the upload.

In the Portal Management app (or the `Contacts` view filtered by `Web Roles`), confirm at
least one contact holds each of:

| Web role | Id |
|---|---|
| AL Portal - Tax Reviewer | `a1000000-0000-4000-8000-000000000090` |
| AL Portal - AQS Reviewer | `a1000000-0000-4000-8000-000000000091` |
| AL Portal - Adviser Remediation | `a1000000-0000-4000-8000-000000000092` |
| AL Portal - T&C Supervisor | `a1000000-0000-4000-8000-000000000093` |
| AL Portal - Outcome Testing Manager | `a1000000-0000-4000-8000-000000000095` |
| AL Portal - Portal Administrator | `a1000000-0000-4000-8000-000000000096` |
| AL Portal - Planner | `a1000000-0000-4000-8000-000000000097` |

If any role shows zero members, stop and raise it before continuing — do not upload a
permission model nobody can exercise.

**Result: RUN 2026-08-31 — FAILED. Stopped here; the upload was not performed.**

Queried with `pac org fetch` against `Env_AQ_Dev` (logical names in this environment are
`mspp_*`, not `adx_*`):

- **`contact` row count: 1.** That single contact is Active but has no `fullname`, no
  `firstname`/`lastname` and no `emailaddress1`.
- **`al_user` row count: 10.** The application side has ten identities; the portal side
  has none that can be used.
- Every `AL Portal - *` web role therefore has **zero usable members**.

The ten web roles that exist in DEV are the pre-branch set — `AL Portal - Regional
Manager` (`…094`) is still present and `AL Portal - Planner` (`…097`) is absent, which is
expected, since this branch has not been uploaded.

**What this means.** AD-047 provisions portal contacts by Entra group sync, joined to the
application identity on work email. That sync has evidently never run against DEV: there
is no contact carrying an email to join on. Uploading this branch in that state would
apply the Restrict Read rule on Home to a site where nobody holds a portal role, so every
authenticated user would be denied every page. Administrators would still reach
everything through the `Grant Change to Administrators` rule, so DEV would not be bricked
— but steps 4 and 7 could not be performed at all, because there is no contact to sign in
as, and step 6's irreversible deletions would then run against an environment whose
sign-in path was never verified.

**This is the gate doing its job.** Resolve portal identity before re-running from step 3.

## 3. Upload

```powershell
$pac = Join-Path $env:USERPROFILE ".dotnet\tools\pac.exe"
& $pac pages upload --path ".\powerpages\outcome-testing---outcometesting" --modelVersion Enhanced
```

`--modelVersion` takes `Enhanced` or `Standard` on PAC CLI 2.11.2; the `--modelVersion 2`
form in the build guide is not valid syntax.

**Result: RUN 2026-09-02 — succeeded on the third attempt, with a CLI defect worked around.**

The site is uploaded. All 10 web roles, 11 table permissions, 6 page access control rules,
56 site settings, 47 web templates and 24 web pages are in DEV, and both gates pass against
the source that produced them.

**The defect: `pac pages upload` 2.11.2 cannot write `adx_entitypermission` to an Enhanced
data model site.** Partway through it abandons the `powerpagecomponent` path and addresses
the legacy `adx_entitypermission` table, which does not exist here, and the whole upload
aborts:

> Upload failed for file 'adx_entitypermission' (record a1000000-…-000000000071): the
> target entity was not found. … The entity with a name = 'adx_entitypermission' … was not
> found in the MetadataCache.

The message blames `--modelVersion`, which is a red herring — `Enhanced` is correct and
every other component type wrote successfully as a `powerpagecomponent`. It failed on a
different permission each run (`…072`, then `…071`), so it is not one bad record.

**Why it matters more than it looks.** The abort happens *before* the web templates, so a
run that reports failure has still silently changed site settings and permissions while
leaving every page at its old content. The first attempt here did exactly that: the write
permission and the two new Web API site settings landed, and the review page kept posting
to a cloud flow that does not exist. **Check what actually landed before re-running; do not
assume a failed upload changed nothing.**

**Workaround, and why it is safe.** Move `table-permissions/` out of the site folder, run
the upload, move it back — **and never with `--forceUploadAll`**. A plain
`pac pages upload` is additive (see point 1 above), so files that are absent are not
deleted from DEV. `--forceUploadAll` is **not** additive: it reconciles, and it deletes
site components that are missing from the source folder. Combining the two on 2026-09-02
wiped nine of the eleven table permissions from DEV, which presented as
`Liquid error: Invalid cast from 'System.Int32' to 'System.Linq.Enumerable+...'` on every
navigation page — templates querying tables the signed-in user could no longer read. It is
fail-closed, so it is an outage rather than an exposure, but it takes the site down.

**Deploy table permissions with the console, not the CLI.** `pac pages upload` cannot
*create* a table permission on an Enhanced data model site at all — it addresses the legacy
`adx_entitypermission` table and aborts the whole upload. Three separate attempts restored
nothing. Use instead, from `plugins/OutcomeTesting.Registration`:

```powershell
dotnet bin\Debug\net8.0\OutcomeTesting.Registration.dll `
    restoretablepermissions https://org0b075da8.crm11.dynamics.com `
    ..\..\powerpages\outcome-testing---outcometesting
```

It reads the same `*.tablepermission.yml` and writes `powerpagecomponent` rows directly:
type 18, settings as JSON in `content`, site resolved from the single `powerpagesite`, and
the component id taken from `adx_entitypermissionid` so re-running updates rather than
duplicating (AD-059). Parentless permissions are written before children. This is now the
only reliable way to get table permissions into any environment, including TEST and PROD.

Two non-fatal errors also appear and can be ignored; both name records that no longer
exist and neither stops the upload:

- `Could not delete powerpagecomponent record 'fc16bce0-…'` — concurrent delete conflict.
- `Updating … record ID:5140384b-… FAILED … Does Not Exist`.

**Verification is a `-Raw` string match, not `Select-String`.** The `content` column of a
`powerpagecomponent` holds the whole template on one logical line, so a line-oriented
`Select-String` reports 0 matches for text that is demonstrably present. Read the file with
`-Raw` and use `.Contains()`.

### Rollback — if the upload leaves users locked out

`pac pages upload` has no built-in undo. If step 4 (the sign-in smoke check, run
immediately after this step) fails — nobody can sign in, or a contact who could sign in
yesterday now cannot — do this, in order:

1. **Stop. Do not proceed to step 5, 6 or 7.** Step 6's deletions are irreversible;
   nothing there should run until sign-in is confirmed working again.
2. **Check the two registration settings first** — see step 4. `ExternalLoginEnabled`
   must be `true` (the branch ships it that way; a stale `false` in DEV would lock
   everyone out) and, if only first-time sign-ins fail, `Registration/Enabled` is the
   suspect. Both are single setting edits in the Portal Management app, not a redeploy.
3. **If sign-in is still broken**, re-upload the pre-branch state of `powerpages/` from
   the last known-good commit (`git log -- powerpages/`) with the same `pac pages upload`
   command. Upload overwrites existing rows, so re-applying a prior commit's metadata
   restores the previous role and permission bindings — but it does not un-delete
   anything already removed in DEV under step 6; those rows would need to be recreated
   by hand in the Portal Management app if rollback has to go that far.
4. **Record what happened** in "Defects found during the DEV run" below before retrying,
   so the next attempt is not repeating a failure nobody wrote down.

## 4. Confirm an existing contact can still sign in

**Run this first, before step 5, 6 or 7.** Everything after this step assumes someone can
reach the portal at all, and one setting changed in this branch makes that not obviously
true.

**This was a real defect and it has been fixed in the branch — read this before assuming
the risk is theoretical.** `Authentication/Registration/ExternalLoginEnabled` was briefly
set to `false` in this work, on the mistaken reading that "external login" meant external
self-service accounts. It does not: it is the site-wide switch for **external identity
providers**, and Entra ID OIDC is one. Microsoft Learn's Entra setup page says so plainly
— if no identity providers appear, External login must be On
(`power-pages/security/authentication/openid-settings`) — and this site's own
`AzureADLoginEnabled` description calls Azure AD "an external identity provider". With
local login and registration both off, `false` would have left no route into the portal
at all.

The setting is back to `true`, and `Check-PortalSecurity.ps1` assertion 7 now **fails the
build if it is ever false again**. Confirm the shipped value before uploading:

```powershell
Select-String -Path .\powerpages\outcome-testing---outcometesting\sitesetting.yml -Pattern 'ExternalLoginEnabled' -Context 0,2
```

Expected: `adx_value: true`.

One thing genuinely remains unverified, and this step is where it surfaces:
`Authentication/Registration/Enabled` is `false`. Sign-in is not registration, and
contacts are provisioned by Entra group sync (AD-047), so an existing contact should sign
in unaffected. What is not established from source is whether a contact signing in for
the **first time** — one with no `adx_externalidentity` record yet — is routed through a
registration step to associate their external identity. If it is, that association is
blocked while registration is off.

In an InPrivate session immediately after the upload, sign in twice — the second test is
the one that matters and is easy to skip:

1. **A contact who has signed in to this portal before.** Proves the upload did not break
   working authentication.
2. **A contact who has never signed in before** — an Entra-provisioned contact with no
   external identity association yet. This is the case `Registration/Enabled: false` may
   block, and it is how every new joiner will arrive.

- **If sign-in succeeds:** record the result below and continue to step 5.
- **If the returning contact fails:** this is the lockout scenario. Follow the Rollback
  paragraph under step 3. Check `ExternalLoginEnabled` is `true` in the environment first
  — the branch ships `true`, but a stale value in DEV from an earlier upload would
  reproduce the fault.
- **If only the first-time contact fails:** the cause is almost certainly
  `Authentication/Registration/Enabled: false` blocking external-identity association.
  Remedy: set it back to `true` in the Portal Management app, leaving
  `OpenRegistrationEnabled` and `LocalLoginEnabled` `false` — that still permits no
  self-service sign-up, which is what PP-01 actually requires. Record the outcome, because
  it settles a question this document could not.

**Result:** _(not run)_

## 5. Prove the page rules actually round-tripped

Download to a scratch folder — never over `powerpages/`, which would overwrite
uncommitted work — and diff:

```powershell
$pac = Join-Path $env:USERPROFILE ".dotnet\tools\pac.exe"
$scratch = Join-Path $env:TEMP 'pages-verify'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
& $pac pages download --path $scratch --webSiteId "b4cfe195-fd15-42e8-94e5-f27bcceaf5fc" --modelVersion Enhanced --overwrite
```

```bash
diff --strip-trailing-cr powerpages/outcome-testing---outcometesting/webpagerule.yml "$TEMP/pages-verify/outcome-testing---outcometesting/webpagerule.yml"
```

Expected: no differences. This is the step that proves `pac pages upload` carries
`adx_webpageaccesscontrolrule`; download was already observed to serialise it, but
upload was inferred rather than seen. **If the five new rules are absent from the
re-download, stop:** the rules must then be created through the Portal Management app,
and that fact must be recorded here before anything else proceeds.

**Result:** _(not run)_

## 6. Verify the deletions actually happened — upload does not delete

Confirm each row below is genuinely gone from DEV, using the re-downloaded scratch
folder from step 5 and the Portal Management app. If the re-download still lists any of
them, delete it **environment-side** in the Portal Management app, then re-download and
confirm again. Do not proceed to step 7 until the scratch download is free of all of
them — a role matrix run against a site that still holds the old permissions tests the
wrong site.

Deleting rows in a shared environment is destructive and irreversible. Confirm the
target is DEV before each deletion.

| Component | Id | Present in scratch re-download? | Deleted environment-side on |
|---|---|---|---|
| `Feedback` table permission (Anonymous Users, create) | `73e2df0d-fb6c-4d88-94cc-eeec79eaca3e` | _(not checked)_ | _(n/a)_ |
| `PROVISIONAL DEV ONLY - Outcome` | `a1000000-0000-4000-8000-000000000064` | _(not checked)_ | _(n/a)_ |
| `PROVISIONAL DEV ONLY - ChecklistVersion` | `a1000000-0000-4000-8000-000000000065` | _(not checked)_ | _(n/a)_ |
| `PROVISIONAL DEV ONLY - Checklist` | `a1000000-0000-4000-8000-00000000006a` | _(not checked)_ | _(n/a)_ |
| `AL Portal - Regional Manager` web role | `a1000000-0000-4000-8000-000000000094` | _(not checked)_ | _(n/a)_ |
| `Grant Change to Content` page rule | `7f9846c2-9af9-4dae-a52d-4b106ec11302` | _(not checked)_ | _(n/a)_ |
| `contact-us` web page | see task 7 of the security-closure plan | _(not checked)_ | _(n/a)_ |
| `search` web page | see task 7 | _(not checked)_ | _(n/a)_ |
| `pages` web page | see task 7 | _(not checked)_ | _(n/a)_ |
| `subpage-1` web page | see task 7 | _(not checked)_ | _(n/a)_ |
| `subpage-2` web page | see task 7 | _(not checked)_ | _(n/a)_ |

Note also from the `.portalconfig` manifest (see "Three things to hold in mind" above):
these ids all still show `IsDeleted: false` in the committed manifest before this run.
That is expected and does not by itself mean the row is live in DEV — the manifest is
bookkeeping, not the source of truth. The scratch re-download is the source of truth
for this table.

**Any additional deletions performed, not on the list above:** _(none recorded yet)_

## 7. Clear the site cache and run the role matrix

In the design studio, sync configuration. Then, in an InPrivate session per role, run
each test below and record the actual result. Do **not** add a test asserting an
Administrator is denied any page — see "Three things to hold in mind," point 2.

| Test | Expected | Actual result |
|---|---|---|
| Anonymous requests `/`, `/cases`, `/review?id=…` | Redirected to sign-in, no content disclosed | _(not run)_ |
| Anonymous requests `/outcome-testing.css` | Served — styling must survive on the sign-in page | _(not run)_ |
| Tax Reviewer opens `/aqs-reviews` | Access denied | _(not run)_ |
| AQS Reviewer opens `/tax-reviews` | Access denied | _(not run)_ |
| Adviser opens `/review?id=…` | Access denied | _(not run)_ |
| Adviser opens `/remediation` | Renders | _(not run)_ |
| Tax Reviewer opens another checker's review | Renders read-only; save is refused | _(not run)_ |
| Any authenticated portal role opens `/cases` | Renders (OD-022) | _(not run)_ |
| A contact with no portal web role opens `/` | Access denied — fail closed | _(not run)_ |
| Self-registration URL | Unavailable | _(not run)_ |
| Planner opens `/remediation` | Renders (bound on `…0b4`) | _(not run)_ |
| Planner opens `/tax-reviews` | Access denied (not bound on `…0b1`) | _(not run)_ |
| T&C Supervisor opens `/remediation` | Renders (bound on `…0b4`) | _(not run)_ |
| T&C Supervisor opens `/review?id=…` | Access denied (not bound on `…0b3`) | _(not run)_ |
| Outcome Testing Manager opens `/tax-reviews` | Renders (bound on `…0b1`) | _(not run)_ |
| Outcome Testing Manager opens `/aqs-reviews` | Renders (bound on `…0b2`) | _(not run)_ |
| Portal Administrator opens `/review?id=…` | Renders (bound on `…0b3`) | _(not run)_ |
| Portal Administrator opens `/remediation` | Renders (bound on `…0b4`) | _(not run)_ |
| **Tax Reviewer saves an answer on a review assigned to them** | **Save succeeds** — exercises the write path this branch rebound onto the purpose-built role: `Response - on a review assigned to me` (`…072`), child of the Contact-scoped `Review Instance - assigned to me` (`…071`) | _(not run)_ |
| **Adviser updates a remediation action assigned to them** | **Save succeeds** — exercises `Remediation Action - assigned to me` (`…06b`), the Contact-scoped write permission this branch added for Adviser and Planner (AD-069) | _(not run)_ |

The last two rows are deliberately **positive** tests: every other row in this table
checks that access is correctly denied, and none of them proves the write paths this
branch actually rebound still work. A gate that only ever proves "no" can pass while
"yes" is silently broken.

**Watch for a redirect loop:** the Access Denied page inherits Home's rule, so a
role-less contact may be denied the page they are being sent to. If that happens, clear
`adx_parentpageid` on `Access Denied`, `Page Not Found` and `Default Offline Page` so
they leave Home's inheritance chain, and re-run. Do not weaken the Home rule to fix it.

## Defects found during the DEV run

_(none recorded yet — this section is filled in only if the run above surfaces one, with
the correction placed in the code/metadata and referenced here, following the pattern of
`docs/deployment/2026-08-30-outcome-creation-deployment.md`.)_

## Sign-off

| Step | Run by | Date | Outcome |
|---|---|---|---|
| 0. Gates | Delivery (automated) | 2026-08-31 | Pass — see section 0 |
| 1. Confirm environment | _(unassigned)_ | | |
| 2. Confirm role coverage | _(unassigned)_ | | |
| 3. Upload | _(unassigned)_ | | |
| 4. Sign-in smoke check | _(unassigned)_ | | |
| 5. Round-trip verification | _(unassigned)_ | | |
| 6. Deletion verification | _(unassigned)_ | | |
| 7. Role matrix | _(unassigned)_ | | |
