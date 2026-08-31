# Portal security closure — DEV deployment runbook

**Not yet run.** This document is a runbook to be executed against DEV (`Env_AQ_Dev`),
not a record of a completed deployment. Steps 1 and the local gate output in section 0
are the only things that have actually happened — they are local script runs, not
environment calls, and are safe to run repeatedly. Everything from step 2 onward
(`pac org who`, `pac pages upload`, the round-trip download, the deletion check and the
DEV role matrix) writes to and deletes rows from a shared environment and is held back
for a human to run. Fill in the blank "Result" fields as each step is actually carried
out; do not fill them in from expectation.

Requirements: PP-01, PP-02, NFR-SEC-01, NFR-SEC-02. Decisions: OD-019, OD-021, OD-022,
AD-047, AD-056, AD-059 (amended), AD-067, AD-068, AD-069.

## Three things to hold in mind before running this

1. **`pac pages upload` is additive.** Deleting a YAML file from the site folder does
   not delete the row in DEV. Without step 4b, uploading this branch leaves the
   anonymous-create `Feedback` table permission live in DEV while every gate reports
   green — because the gates read the repository, not the site. **Do not skip step 4b.**
2. **A Grant Change rule on Home overrides Restrict Read site-wide.**
   `Grant Change to Administrators` (`563ee258-2962-4440-a6e8-d25296ac40bb`) sits on
   Home with All-content scope, so Administrators reach every page regardless of the
   five new Restrict Read rules. The role matrix in step 5 does not, and must not,
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

## 2. Upload

```powershell
$pac = Join-Path $env:USERPROFILE ".dotnet\tools\pac.exe"
& $pac pages upload --path ".\powerpages\outcome-testing---outcometesting" --modelVersion Enhanced
```

`--modelVersion` takes `Enhanced` or `Standard` on PAC CLI 2.11.2; the `--modelVersion 2`
form in the build guide is not valid syntax.

**Result:** _(not run)_

## 3. Prove the page rules actually round-tripped

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

## 4. Verify the deletions actually happened — upload does not delete

Confirm each row below is genuinely gone from DEV, using the re-downloaded scratch
folder from step 3 and the Portal Management app. If the re-download still lists any of
them, delete it **environment-side** in the Portal Management app, then re-download and
confirm again. Do not proceed to step 5 until the scratch download is free of all of
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

## 5. Clear the site cache and run the role matrix

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
| 2. Upload | _(unassigned)_ | | |
| 3. Round-trip verification | _(unassigned)_ | | |
| 4. Deletion verification | _(unassigned)_ | | |
| 5. Role matrix | _(unassigned)_ | | |
