# Portal Security Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bind the portal's table permissions to its purpose-built web roles, add the Restrict Read page-permission layer that does not exist today, and shut off every anonymous route into the site.

**Architecture:** Everything here is Power Pages site metadata under `powerpages/outcome-testing---outcometesting/`, deployed with `pac pages upload`. There is no application code. The testable artefact is a new PowerShell gate, `Check-PortalSecurity.ps1`, whose assertions are written first and proven to fail against today's metadata; each subsequent task turns a named assertion green. A second gate, the existing `Check-ComponentIds.ps1`, is extended to cover the two component types it does not currently see.

**Tech Stack:** Power Pages Enhanced data model, PAC CLI 2.11.2, Windows PowerShell 5.1, YAML site metadata.

**Spec:** `docs/superpowers/specs/2026-08-31-portal-security-closure-design.md`

## Global Constraints

- Security is enforced in Dataverse and Power Pages permissions, never by hiding UI (NFR-SEC-01).
- Never invent a business rule. Cite the requirement ID from `knowledge/requirements-index.md`, and name the blocking OD ID from `knowledge/decision-log.md` rather than assuming a resolution.
- Do not hardcode secrets, tenant or environment IDs, connection IDs, URLs, email addresses or group IDs.
- Portal metadata stays under `powerpages/`, never under `src/` or `app/` (AD-048).
- Component record ids are hand-minted in the `a1000000-0000-4000-8000-0000000000NN` space, banded by component type (AD-059). A deleted component's id is retired, never reissued.
- `powerpages/Check-ComponentIds.ps1` must pass before every `pac pages upload`.
- DEV (`Env_AQ_Dev`) is the only authoring environment. TEST and PROD are out of scope.
- Scope option values in this site: Global `756150000`, Contact `756150001`, Parent `756150003`.
- Page rule option values: `adx_right` 1 = Grant Change, 2 = Restrict Read. `adx_scope` 1 = All content, 2 = Exclude direct child web files.
- The Anonymous Users web role (`eb1dfa60-6f53-4d61-8cbc-2f8b0a6ee08e`) must never appear on a table permission or a page rule.

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `powerpages/Check-ComponentIds.ps1` | Collision gate. Extended to see `webrole.yml` and `webpagerule.yml` | 1 |
| `powerpages/Check-PortalSecurity.ps1` | **New.** The security gate: eight assertions over the site metadata | 2 |
| `powerpages/outcome-testing---outcometesting/webrole.yml` | Web role matrix | 3 |
| `powerpages/outcome-testing---outcometesting/table-permissions/*.yml` | Table permissions | 4 |
| `powerpages/outcome-testing---outcometesting/webpagerule.yml` | Page access control rules | 5 |
| `powerpages/outcome-testing---outcometesting/sitesetting.yml` | Registration settings | 6 |
| `powerpages/outcome-testing---outcometesting/web-pages/`, `basic-forms/`, `web-files/` | Starter content removal | 7 |
| `powerpages/README.md`, `knowledge/decision-log.md`, `docs/deployment/2026-08-31-portal-security-closure-deployment.md` | Documentation and decisions | 8 |

**Reference ids used throughout.** Web roles: Tax Reviewer `a1000000-0000-4000-8000-000000000090`, AQS Reviewer `…091`, Adviser Remediation `…092`, T&C Supervisor `…093`, Regional Manager `…094` (deleted in Task 3), Outcome Testing Manager `…095`, Portal Administrator `…096`, Planner `…097` (created in Task 3). Stock: Administrators `c53b2908-1fc1-4470-89cd-6f5b95c17ffe`, Authenticated Users `e24b50c5-1443-4725-84c9-70355724547f`, Anonymous Users `eb1dfa60-6f53-4d61-8cbc-2f8b0a6ee08e`. Web pages: Home `52570e2a-4d91-41f8-95c9-d0017a937039`, My Work `…030`, Cases `…031`, Case detail `…032`, Tax reviews `…033`, AQS reviews `…034`, Remediation `…035`, Review `…036`.

---

### Task 1: Teach the collision gate about web roles and page rules

`Check-ComponentIds.ps1` matches component files by suffix (`.webtemplate.yml`, `.tablepermission.yml`). Web roles and page rules are single top-level list files named exactly `webrole.yml` and `webpagerule.yml`, so the gate cannot currently see them — the two component types this plan edits most are the two it does not check.

**Files:**
- Modify: `powerpages/Check-ComponentIds.ps1`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: a gate that fails on a duplicate id in `webrole.yml` or `webpagerule.yml`. Tasks 3 and 5 rely on it.

- [ ] **Step 1: Prove the gate is blind, by planting a collision**

Temporarily append a duplicate of an existing web role id to the end of `webrole.yml`:

```yaml
- adx_anonymoususersrole: false
  adx_authenticatedusersrole: false
  adx_name: TEMP Collision Probe
  adx_webroleid: a1000000-0000-4000-8000-000000000090
```

- [ ] **Step 2: Run the gate and watch it pass anyway**

```powershell
powershell -NoProfile -File .\powerpages\Check-ComponentIds.ps1
```

Expected: `No duplicate component ids, no web file faults.` and exit 0. It reports 178 identities and misses the planted duplicate entirely. That is the defect.

- [ ] **Step 3: Add exact-filename identity keys**

In `Check-ComponentIds.ps1`, immediately after the `$identityKeys` hashtable, add a second map and widen the matching. Replace the block that begins `$suffix = $identityKeys.Keys | Where-Object` with:

```powershell
# Web roles and page access control rules are not per-component files with a
# suffix; each is one top-level list file. Match those by exact name, so the
# two component types this repository edits most are no longer unchecked.
$fileKeys = @{
    'webrole.yml'     = 'adx_webroleid'
    'webpagerule.yml' = 'adx_webpageaccesscontrolruleid'
}

foreach ($file in Get-ChildItem -LiteralPath $SitePath -Recurse -File -Filter '*.yml') {
    $key = $null
    $kind = $null

    if ($fileKeys.ContainsKey($file.Name)) {
        $key = $fileKeys[$file.Name]
        $kind = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    }
    else {
        $suffix = $identityKeys.Keys | Where-Object { $file.Name.EndsWith($_, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if (-not $suffix) { continue }
        $key = $identityKeys[$suffix]
        $kind = $suffix.Trim('.').Replace('.yml', '')
    }
```

Delete the three now-superseded lines that previously assigned `$key`, `$kind` and the old `foreach` opener, keeping the rest of the loop body (the `$lines`, `$relative`, `$pendingName` assignments and the inner line walk) exactly as it is.

- [ ] **Step 4: Run the gate and watch it catch the planted collision**

```powershell
powershell -NoProfile -File .\powerpages\Check-ComponentIds.ps1
```

Expected: exit 1, with output naming `a1000000-0000-4000-8000-000000000090` claimed twice by `webrole` entries `AL Portal - Tax Reviewer` and `TEMP Collision Probe`.

- [ ] **Step 5: Remove the planted collision and confirm green**

Delete the `TEMP Collision Probe` entry from `webrole.yml`, then:

```powershell
powershell -NoProfile -File .\powerpages\Check-ComponentIds.ps1
```

Expected: `No duplicate component ids, no web file faults.`, exit 0, and an identity count **higher than 178** — the web roles and page rules are now counted.

- [ ] **Step 6: Commit**

```bash
git add powerpages/Check-ComponentIds.ps1
git commit -m "fix(powerpages): collision gate was blind to web roles and page rules"
```

---

### Task 2: The security gate, written failing

**Files:**
- Create: `powerpages/Check-PortalSecurity.ps1`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `Check-PortalSecurity.ps1`, exit 0 when every assertion holds and exit 1 otherwise. Tasks 3–7 each turn named assertions green; Task 8 documents it as the release gate.

- [ ] **Step 1: Write the gate**

Create `powerpages/Check-PortalSecurity.ps1`:

```powershell
<#
.SYNOPSIS
    Fails when the portal's security metadata regresses.

.DESCRIPTION
    The companion to Check-ComponentIds.ps1. That script asks whether the site
    will deploy intact; this one asks whether it is safe to deploy at all.

    Eight assertions, each traceable to the design at
    docs/superpowers/specs/2026-08-31-portal-security-closure-design.md:

      1. No table permission grants an anonymous role anything.
      2. No PROVISIONAL permission survives into a release artefact.
      3. Every business page is covered by a Restrict Read rule, on itself or
         on an ancestor. A page with no rule is public.
      4. A child page's rule uses a subset of its nearest ancestor rule's roles.
         Power Pages makes the page unreachable for the extra roles otherwise.
      5. At most one Restrict Read rule per page. Power Pages raises a
         conflicting-rules error on more.
      6. No page rule binds the Anonymous Users role.
      7. Self-registration and external login are off.
      8. Every web role referenced by a permission still exists.

    Run before every `pac pages upload`, alongside Check-ComponentIds.ps1.

.EXAMPLE
    powershell -NoProfile -File .\powerpages\Check-PortalSecurity.ps1
#>

[CmdletBinding()]
param(
    [string]$SitePath
)

$ErrorActionPreference = 'Stop'

if (-not $SitePath) {
    $root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $SitePath = Join-Path $root 'outcome-testing---outcometesting'
}
if (-not (Test-Path -LiteralPath $SitePath)) { throw "Site folder not found: $SitePath" }

# Pages that may be reached without signing in. Sign-in itself is a system page
# outside the web page tree; these three must render before authentication and
# disclose nothing.
$publicPages = @('Access Denied', 'Page Not Found', 'Default Offline Page')

$failures = [System.Collections.Generic.List[string]]::new()
function Add-Failure { param([string]$Rule, [string]$Detail) $failures.Add("[$Rule] $Detail") }

# ---------------------------------------------------------------- YAML readers
# Windows PowerShell 5.1 has no YAML parser, and Check-ComponentIds.ps1 already
# reads this metadata line by line. The same approach is used here rather than
# taking a module dependency for four shapes of file.

# A file holding ONE record, keys at column 0, list values as "- value" at column 0.
# This is the shape of a .tablepermission.yml.
function Read-YamlDoc {
    param([string]$Path)
    $doc = @{}
    $listKey = $null
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^([A-Za-z0-9_]+):\s*(.*)$') {
            $key = $Matches[1]
            $val = $Matches[2].Trim()
            if ($val -eq '') { $listKey = $key; $doc[$key] = @() }
            else { $listKey = $null; $doc[$key] = $val }
            continue
        }
        if ($listKey -and $line -match '^-\s+(.+)$') {
            $doc[$listKey] += $Matches[1].Trim()
        }
    }
    return $doc
}

# A file holding a LIST of records: "- key: value" opens each record, "  key: value"
# continues it, "  - value" appends to the most recent empty-valued key.
# This is the shape of webrole.yml and webpagerule.yml.
function Read-YamlList {
    param([string]$Path)
    $records = [System.Collections.Generic.List[object]]::new()
    $current = $null
    $listKey = $null
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^-\s+([A-Za-z0-9_]+):\s*(.*)$') {
            if ($current) { $records.Add($current) }
            $current = @{}
            $listKey = $null
            $current[$Matches[1]] = $Matches[2].Trim()
            continue
        }
        if (-not $current) { continue }
        if ($line -match '^\s{2}-\s+(.+)$') {
            if ($listKey) { $current[$listKey] += $Matches[1].Trim() }
            continue
        }
        if ($line -match '^\s{2}([A-Za-z0-9_]+):\s*(.*)$') {
            $key = $Matches[1]
            $val = $Matches[2].Trim()
            if ($val -eq '') { $listKey = $key; $current[$key] = @() }
            else { $listKey = $null; $current[$key] = $val }
        }
    }
    if ($current) { $records.Add($current) }
    return $records
}

# ---------------------------------------------------------------- load metadata
$roles = Read-YamlList (Join-Path $SitePath 'webrole.yml')
$rulesPath = Join-Path $SitePath 'webpagerule.yml'
$rules = if (Test-Path -LiteralPath $rulesPath) { Read-YamlList $rulesPath } else { @() }

$roleById = @{}
foreach ($r in $roles) { $roleById[$r.adx_webroleid] = $r }
$anonRoleIds = @($roles | Where-Object { $_.adx_anonymoususersrole -eq 'true' } | ForEach-Object { $_.adx_webroleid })

$permissions = @()
foreach ($f in Get-ChildItem -LiteralPath (Join-Path $SitePath 'table-permissions') -File -Filter '*.tablepermission.yml') {
    $doc = Read-YamlDoc $f.FullName
    $doc['_file'] = $f.Name
    $permissions += ,$doc
}

$pages = @()
foreach ($f in Get-ChildItem -LiteralPath (Join-Path $SitePath 'web-pages') -Recurse -File -Filter '*.webpage.yml') {
    if ($f.FullName -match 'content-pages') { continue }   # language variants, not the root page
    $doc = Read-YamlDoc $f.FullName
    if ($doc.adx_webpageid) { $pages += ,$doc }
}
$pageById = @{}
foreach ($p in $pages) { $pageById[$p.adx_webpageid] = $p }

$settings = Read-YamlList (Join-Path $SitePath 'sitesetting.yml')
$settingByName = @{}
foreach ($s in $settings) { $settingByName[$s.adx_name] = $s.adx_value }

# ---------------------------------------------------------------- 1, 2, 8
foreach ($p in $permissions) {
    $bound = @($p.adx_entitypermission_webrole)

    foreach ($id in $bound) {
        if ($anonRoleIds -contains $id) {
            Add-Failure '1 anonymous-permission' "$($p._file) grants '$($p.adx_entityname)' to an anonymous web role."
        }
        if (-not $roleById.ContainsKey($id)) {
            Add-Failure '8 dangling-role' "$($p._file) references web role $id, which does not exist in webrole.yml."
        }
    }

    if ($p.adx_entityname -match 'PROVISIONAL') {
        Add-Failure '2 provisional' "$($p._file) is still named '$($p.adx_entityname)'."
    }
}

# ---------------------------------------------------------------- 5, 6
$restrictRules = @($rules | Where-Object { $_.adx_right -eq '2' })

foreach ($rule in $restrictRules) {
    foreach ($id in @($rule.adx_webpageaccesscontrolrule_webrole)) {
        if ($anonRoleIds -contains $id) {
            Add-Failure '6 anonymous-page-rule' "Rule '$($rule.adx_name)' binds an anonymous web role."
        }
    }
}

foreach ($group in $restrictRules | Group-Object { $_.adx_webpageid } | Where-Object { $_.Count -gt 1 }) {
    $names = ($group.Group | ForEach-Object { $_.adx_name }) -join ', '
    Add-Failure '5 multiple-rules' "Page $($group.Name) carries $($group.Count) Restrict Read rules ($names). Power Pages rejects this."
}

# ---------------------------------------------------------------- 3, 4
$ruleByPage = @{}
foreach ($rule in $restrictRules) { $ruleByPage[$rule.adx_webpageid] = $rule }

# Walks a page up its parent chain and returns the nearest Restrict Read rule,
# which is the one Power Pages actually applies through inheritance.
function Get-EffectiveRule {
    param([hashtable]$Page, [hashtable]$RuleByPage, [hashtable]$PageById, [switch]$Inherited)
    $cursor = $Page
    $first = $true
    $seen = @{}
    while ($cursor) {
        if ($seen.ContainsKey($cursor.adx_webpageid)) { return $null }   # cycle guard
        $seen[$cursor.adx_webpageid] = $true
        if (-not ($first -and $Inherited)) {
            if ($RuleByPage.ContainsKey($cursor.adx_webpageid)) { return $RuleByPage[$cursor.adx_webpageid] }
        }
        $first = $false
        if (-not $cursor.adx_parentpageid) { return $null }
        $cursor = $PageById[$cursor.adx_parentpageid]
    }
    return $null
}

foreach ($page in $pages) {
    if ($publicPages -contains $page.adx_name) { continue }

    $effective = Get-EffectiveRule -Page $page -RuleByPage $ruleByPage -PageById $pageById
    if (-not $effective) {
        Add-Failure '3 unprotected-page' "Page '$($page.adx_name)' has no Restrict Read rule on itself or any ancestor, so it is public."
        continue
    }

    # Only pages carrying their OWN rule can violate the subset rule.
    if ($ruleByPage.ContainsKey($page.adx_webpageid)) {
        $ancestor = Get-EffectiveRule -Page $page -RuleByPage $ruleByPage -PageById $pageById -Inherited
        if ($ancestor) {
            $own = @($effective.adx_webpageaccesscontrolrule_webrole)
            $parent = @($ancestor.adx_webpageaccesscontrolrule_webrole)
            $extra = @($own | Where-Object { $parent -notcontains $_ })
            if ($extra.Count -gt 0) {
                $names = ($extra | ForEach-Object { if ($roleById.ContainsKey($_)) { $roleById[$_].adx_name } else { $_ } }) -join ', '
                Add-Failure '4 role-not-in-parent' "Rule '$($effective.adx_name)' grants roles absent from ancestor rule '$($ancestor.adx_name)': $names."
            }
        }
    }
}

# ---------------------------------------------------------------- 7
$mustBeFalse = @(
    'Authentication/Registration/Enabled',
    'Authentication/Registration/OpenRegistrationEnabled',
    'Authentication/Registration/ExternalLoginEnabled'
)
foreach ($name in $mustBeFalse) {
    if (-not $settingByName.ContainsKey($name)) {
        Add-Failure '7 registration' "Site setting '$name' is absent, so its platform default applies. Set it explicitly to false."
        continue
    }
    $value = ($settingByName[$name] -replace "^'|'$", '').Trim()
    if ($value -notmatch '^(?i)false$') {
        Add-Failure '7 registration' "Site setting '$name' is '$value', not false."
    }
}

# ---------------------------------------------------------------- report
Write-Host "Checked $($permissions.Count) table permissions, $($rules.Count) page rules, $($pages.Count) web pages and $($roles.Count) web roles under $SitePath."

if ($failures.Count -eq 0) {
    Write-Host 'Portal security assertions all pass.' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "$($failures.Count) portal security assertion(s) failed:" -ForegroundColor Red
foreach ($f in $failures) { Write-Host "  $f" }
Write-Host ''
Write-Host 'This site must not be uploaded to TEST or PROD in this state.' -ForegroundColor Red
exit 1
```

- [ ] **Step 2: Run it and confirm it fails for the right reasons**

```powershell
powershell -NoProfile -File .\powerpages\Check-PortalSecurity.ps1
```

Expected: exit 1. The failure list must include, at minimum:
- `[1 anonymous-permission] Feedback.tablepermission.yml grants 'Feedback' to an anonymous web role.`
- seven `[2 provisional]` lines, one per `PROVISIONAL DEV ONLY` permission
- a `[3 unprotected-page]` line for every business page — Home, My Work, Cases, Case detail, Tax reviews, AQS reviews, Remediation, Review, Profile, Pages, Subpage 1, Subpage 2, Contact us, Search
- three `[7 registration]` lines

If any of those categories is missing, the assertion is not wired correctly — fix the script before continuing. A gate that passes when the site is broken is worse than no gate.

- [ ] **Step 3: Commit the failing gate**

```bash
git add powerpages/Check-PortalSecurity.ps1
git commit -m "test(powerpages): security gate, failing against the current site"
```

---

### Task 3: Web role matrix

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/webrole.yml`

**Interfaces:**
- Consumes: the extended `Check-ComponentIds.ps1` from Task 1.
- Produces: web role `a1000000-0000-4000-8000-000000000097` (`AL Portal - Planner`), consumed by Tasks 4 and 5. Removes `…094`, which nothing may reference afterwards.

- [ ] **Step 1: Add the Planner role and remove Regional Manager**

In `webrole.yml`, delete this entry entirely (OD-021: notification only, not a portal user):

```yaml
- adx_anonymoususersrole: false
  adx_authenticatedusersrole: false
  adx_name: AL Portal - Regional Manager
  adx_webroleid: a1000000-0000-4000-8000-000000000094
```

And add this one (OD-019: Adviser and Planner are separate roles):

```yaml
- adx_anonymoususersrole: false
  adx_authenticatedusersrole: false
  adx_name: AL Portal - Planner
  adx_webroleid: a1000000-0000-4000-8000-000000000097
```

`…094` is retired, not reused by the new role. Reissuing a deleted component's id is the AD-059 failure mode on a delay.

- [ ] **Step 2: Run the collision gate**

```powershell
powershell -NoProfile -File .\powerpages\Check-ComponentIds.ps1
```

Expected: exit 0, no duplicates. This is the check that would catch `…097` colliding with an existing component.

- [ ] **Step 3: Run the security gate for the dangling-role assertion**

```powershell
powershell -NoProfile -File .\powerpages\Check-PortalSecurity.ps1
```

Expected: still exit 1 overall, but **no `[8 dangling-role]` line**. Nothing referenced Regional Manager, so removing it leaves no dangling reference. If an `[8 dangling-role]` line appears naming `…094`, something did reference it and must be repointed before continuing.

- [ ] **Step 4: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/webrole.yml
git commit -m "feat(powerpages): add the Planner web role, retire Regional Manager (OD-019, OD-021)"
```

---

### Task 4: Table permissions

**Files:**
- Modify: `table-permissions/PROVISIONAL-DEV-ONLY---RemediationAction.tablepermission.yml` → rename file to `Remediation-Action---read-all.tablepermission.yml`
- Modify: `table-permissions/PROVISIONAL-DEV-ONLY---Section.tablepermission.yml` → rename to `Section---read.tablepermission.yml`
- Modify: `table-permissions/PROVISIONAL-DEV-ONLY---QuestionVersion.tablepermission.yml` → rename to `Question-Version---read.tablepermission.yml`
- Modify: `table-permissions/PROVISIONAL-DEV-ONLY---Question.tablepermission.yml` → rename to `Question---read.tablepermission.yml`
- Modify: `table-permissions/Review-Instance-Assigned.tablepermission.yml`
- Modify: `table-permissions/Response-Of-Assigned-Review.tablepermission.yml`
- Create: `table-permissions/Remediation-Action---assigned-to-me.tablepermission.yml`
- Delete: `table-permissions/PROVISIONAL-DEV-ONLY---Outcome.tablepermission.yml`, `PROVISIONAL-DEV-ONLY---ChecklistVersion.tablepermission.yml`, `PROVISIONAL-DEV-ONLY---Checklist.tablepermission.yml`, `Feedback.tablepermission.yml`

**Interfaces:**
- Consumes: web role `…097` from Task 3.
- Produces: permission `a1000000-0000-4000-8000-00000000006b`, referenced by nothing later; the rebound `…071`/`…072` pair, whose role set must match the page rules in Task 5.

- [ ] **Step 1: Delete the three unused provisional permissions and the anonymous Feedback permission**

```bash
cd powerpages/outcome-testing---outcometesting/table-permissions
git rm PROVISIONAL-DEV-ONLY---Outcome.tablepermission.yml
git rm PROVISIONAL-DEV-ONLY---ChecklistVersion.tablepermission.yml
git rm PROVISIONAL-DEV-ONLY---Checklist.tablepermission.yml
git rm Feedback.tablepermission.yml
```

No web template queries `al_outcome`, `al_checklistversion` or `al_checklist`, so these three are deleted rather than renamed. `Feedback` granted Anonymous Users create access and goes with the contact-us form removed in Task 7.

- [ ] **Step 2: Promote the four reference-config permissions**

Rename each file and change only its `adx_entityname`. Every other field — id, scope, privileges, role bindings — stays exactly as it is; these four were always legitimate Global reads of reference configuration under AD-047, and only their provisional naming was wrong.

```bash
git mv PROVISIONAL-DEV-ONLY---RemediationAction.tablepermission.yml Remediation-Action---read-all.tablepermission.yml
git mv PROVISIONAL-DEV-ONLY---Section.tablepermission.yml Section---read.tablepermission.yml
git mv PROVISIONAL-DEV-ONLY---QuestionVersion.tablepermission.yml Question-Version---read.tablepermission.yml
git mv PROVISIONAL-DEV-ONLY---Question.tablepermission.yml Question---read.tablepermission.yml
```

Then set the names inside each file:

| File | `adx_entityname` becomes |
|---|---|
| `Remediation-Action---read-all.tablepermission.yml` | `Remediation Action - read all` |
| `Section---read.tablepermission.yml` | `Section - read` |
| `Question-Version---read.tablepermission.yml` | `Question Version - read` |
| `Question---read.tablepermission.yml` | `Question - read` |

- [ ] **Step 3: Rebind the two write-scope permissions to the reviewer roles**

In `Review-Instance-Assigned.tablepermission.yml` and `Response-Of-Assigned-Review.tablepermission.yml`, replace the `adx_entitypermission_webrole` list in both files with:

```yaml
adx_entitypermission_webrole:
- a1000000-0000-4000-8000-000000000090
- a1000000-0000-4000-8000-000000000091
```

Both files get the identical pair. Power Pages requires every web role on a child permission to also exist on its parent, and `…072` is the child of `…071`; an identical list satisfies that by construction.

- [ ] **Step 4: Create the adviser and planner write permission**

Create `Remediation-Action---assigned-to-me.tablepermission.yml`:

```yaml
adx_append: false
adx_appendto: false
adx_contactrelationship: contact_al_remediationaction
adx_create: false
adx_delete: false
adx_entitylogicalname: al_remediationaction
adx_entityname: Remediation Action - assigned to me
adx_entitypermission_webrole:
- a1000000-0000-4000-8000-000000000092
- a1000000-0000-4000-8000-000000000097
adx_entitypermissionid: a1000000-0000-4000-8000-00000000006b
adx_read: true
adx_scope: 756150001
adx_write: true
```

Scope `756150001` is Contact, resolving through the `contact_al_remediationaction` relationship that AD-050 already created on `al_RemediationAction.al_assignedcontactid`. Read is granted here as well as globally: the Contact-scoped grant is what carries **write**, and a permission cannot grant write without read.

- [ ] **Step 5: Run both gates**

```powershell
powershell -NoProfile -File .\powerpages\Check-ComponentIds.ps1
powershell -NoProfile -File .\powerpages\Check-PortalSecurity.ps1
```

Expected from the collision gate: exit 0.
Expected from the security gate: exit 1 still, but assertions **1, 2 and 8 now have no failures** — no anonymous permission, no PROVISIONAL name, no dangling role. Only `[3 unprotected-page]` and `[7 registration]` lines should remain.

- [ ] **Step 6: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/table-permissions
git commit -m "feat(powerpages): bind write scopes to the portal roles, retire the provisional permissions"
```

---

### Task 5: Page permissions

The layer that closes the URL-reachable hole. Rules are appended to the existing `webpagerule.yml`.

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/webpagerule.yml`

**Interfaces:**
- Consumes: web roles `…090`–`…097` from Task 3.
- Produces: rules `…0b0`–`…0b4`. Task 8 documents the `b0`–`bf` band.

- [ ] **Step 1: Add the five Restrict Read rules**

Append to `webpagerule.yml`, and delete the existing `Grant Change to Content` entry while you are in the file — it names no page and no role, so it grants nothing:

```yaml
- adx_name: Restrict read - portal roles
  adx_right: 2
  adx_scope: 2
  adx_webpageaccesscontrolrule_webrole:
  - a1000000-0000-4000-8000-000000000090
  - a1000000-0000-4000-8000-000000000091
  - a1000000-0000-4000-8000-000000000092
  - a1000000-0000-4000-8000-000000000093
  - a1000000-0000-4000-8000-000000000095
  - a1000000-0000-4000-8000-000000000096
  - a1000000-0000-4000-8000-000000000097
  adx_webpageaccesscontrolruleid: a1000000-0000-4000-8000-0000000000b0
  adx_webpageid: 52570e2a-4d91-41f8-95c9-d0017a937039
- adx_name: Restrict read - Tax reviews
  adx_right: 2
  adx_scope: 1
  adx_webpageaccesscontrolrule_webrole:
  - a1000000-0000-4000-8000-000000000090
  - a1000000-0000-4000-8000-000000000095
  - a1000000-0000-4000-8000-000000000096
  adx_webpageaccesscontrolruleid: a1000000-0000-4000-8000-0000000000b1
  adx_webpageid: a1000000-0000-4000-8000-000000000033
- adx_name: Restrict read - AQS reviews
  adx_right: 2
  adx_scope: 1
  adx_webpageaccesscontrolrule_webrole:
  - a1000000-0000-4000-8000-000000000091
  - a1000000-0000-4000-8000-000000000095
  - a1000000-0000-4000-8000-000000000096
  adx_webpageaccesscontrolruleid: a1000000-0000-4000-8000-0000000000b2
  adx_webpageid: a1000000-0000-4000-8000-000000000034
- adx_name: Restrict read - Review
  adx_right: 2
  adx_scope: 1
  adx_webpageaccesscontrolrule_webrole:
  - a1000000-0000-4000-8000-000000000090
  - a1000000-0000-4000-8000-000000000091
  - a1000000-0000-4000-8000-000000000095
  - a1000000-0000-4000-8000-000000000096
  adx_webpageaccesscontrolruleid: a1000000-0000-4000-8000-0000000000b3
  adx_webpageid: a1000000-0000-4000-8000-000000000036
- adx_name: Restrict read - Remediation
  adx_right: 2
  adx_scope: 1
  adx_webpageaccesscontrolrule_webrole:
  - a1000000-0000-4000-8000-000000000092
  - a1000000-0000-4000-8000-000000000093
  - a1000000-0000-4000-8000-000000000095
  - a1000000-0000-4000-8000-000000000096
  - a1000000-0000-4000-8000-000000000097
  adx_webpageaccesscontrolruleid: a1000000-0000-4000-8000-0000000000b4
  adx_webpageid: a1000000-0000-4000-8000-000000000035
```

`…0b0` uses `adx_scope: 2`, Exclude direct child web files, and this is not interchangeable with `1`. `bootstrap.min.css`, `theme.css` and `outcome-testing.css` are web files under Home; scope `1` would restrict them to authenticated users and the anonymous sign-in page would render unstyled.

- [ ] **Step 2: Run both gates**

```powershell
powershell -NoProfile -File .\powerpages\Check-ComponentIds.ps1
powershell -NoProfile -File .\powerpages\Check-PortalSecurity.ps1
```

Expected from the collision gate: exit 0.
Expected from the security gate: exit 1 still, but only `[7 registration]` lines remain — **assertions 3, 4, 5 and 6 now pass**. Every business page inherits `…0b0` or carries its own rule, every child rule's roles are a subset of `…0b0`'s seven, no page has two rules, and no rule binds Anonymous Users.

If a `[3 unprotected-page]` line remains for `Contact us`, `Search`, `Pages`, `Subpage 1` or `Subpage 2`, that is expected only until Task 7 deletes them. They are children of Home, so they should in fact inherit `…0b0` and pass. A failure here means the parent chain is not being read correctly.

- [ ] **Step 3: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/webpagerule.yml
git commit -m "feat(powerpages): restrict read to portal roles, deny-by-default from Home"
```

---

### Task 6: Close self-registration

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/sitesetting.yml`

**Interfaces:**
- Consumes: nothing.
- Produces: the last three assertions the gate needs to go green.

- [ ] **Step 1: Turn the three registration settings off**

In `sitesetting.yml`, set `adx_value` to `false` on each of these three, leaving their `adx_sitesettingid` and `adx_description` untouched:

| `adx_name` | Current | Set to |
|---|---|---|
| `Authentication/Registration/Enabled` | `true` | `false` |
| `Authentication/Registration/OpenRegistrationEnabled` | `true` | `false` |
| `Authentication/Registration/ExternalLoginEnabled` | `true` | `false` |

`Authentication/Registration/LocalLoginEnabled` is already `false` and is left alone. Entra ID OpenIdConnect becomes the only route in, which is what AD-047's Entra-group-sync provisioning already assumes.

- [ ] **Step 2: Run the security gate and watch it go green**

```powershell
powershell -NoProfile -File .\powerpages\Check-PortalSecurity.ps1
```

Expected: `Portal security assertions all pass.` and exit 0. This is the first point at which the gate passes; every assertion written failing in Task 2 is now satisfied.

- [ ] **Step 3: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/sitesetting.yml
git commit -m "fix(powerpages): disable self-registration and external login (PP-01)"
```

---

### Task 7: Remove the starter content

**Files:**
- Delete: `web-pages/contact-us/`, `web-pages/search/`, `web-pages/subpage-1/`, `web-pages/subpage-2/`, `web-pages/pages/`
- Delete: `basic-forms/simple-contact-us-form/`
- Delete: `web-files/Cat-PC.png`, `Circle-1.png`, `Circle-2.png`, `Circle-3.png`, `Geometric-2.png`, `Geometric-4.png`, `Graph-1.png`, `Site-mockup-1.png`, `Video-1.mp4` and each one's `.webfile.yml`

**Interfaces:**
- Consumes: nothing.
- Produces: a smaller site. Nothing later depends on it.

- [ ] **Step 1: Confirm nothing references what is about to go**

```bash
grep -rniE 'contact-us|subpage-1|subpage-2|Cat-PC|Circle-1|Circle-2|Circle-3|Geometric-2|Geometric-4|Graph-1|Site-mockup-1|Video-1' powerpages/outcome-testing---outcometesting/web-templates powerpages/outcome-testing---outcometesting/weblink-sets powerpages/outcome-testing---outcometesting/content-snippets
```

Expected: no hits. The primary navigation targets only Home, Cases, Tax reviews, AQS reviews and Remediation, and no `ot-*` template references the starter media. If there is a hit, remove the referring markup in this task before deleting the target.

- [ ] **Step 2: Delete the pages, the form and the sample media**

```bash
cd powerpages/outcome-testing---outcometesting
git rm -r web-pages/contact-us web-pages/search web-pages/subpage-1 web-pages/subpage-2 web-pages/pages
git rm -r basic-forms/simple-contact-us-form
git rm web-files/Cat-PC.png web-files/Cat-PC.png.webfile.yml
git rm web-files/Circle-1.png web-files/Circle-1.png.webfile.yml
git rm web-files/Circle-2.png web-files/Circle-2.png.webfile.yml
git rm web-files/Circle-3.png web-files/Circle-3.png.webfile.yml
git rm web-files/Geometric-2.png web-files/Geometric-2.png.webfile.yml
git rm web-files/Geometric-4.png web-files/Geometric-4.png.webfile.yml
git rm web-files/Graph-1.png web-files/Graph-1.png.webfile.yml
git rm web-files/Site-mockup-1.png web-files/Site-mockup-1.png.webfile.yml
git rm web-files/Video-1.mp4 web-files/Video-1.mp4.webfile.yml
```

`OfflinePage.png` and `PWALogo.png` are **not** deleted despite looking like starter media: `PWAManifest.json` references `/PWALogo.png` and the retained `Default Offline Page` references `/OfflinePage.png`.

- [ ] **Step 3: Run both gates**

```powershell
powershell -NoProfile -File .\powerpages\Check-ComponentIds.ps1
powershell -NoProfile -File .\powerpages\Check-PortalSecurity.ps1
```

Expected: both exit 0. The identity count drops by the number of deleted components; the security gate stays green, now with five fewer pages to protect.

- [ ] **Step 4: Commit**

```bash
git add -A powerpages/outcome-testing---outcometesting
git commit -m "chore(powerpages): remove the starter pages, form and sample media"
```

---

### Task 8: Deploy to DEV, verify by role, and record the decisions

**Files:**
- Create: `docs/deployment/2026-08-31-portal-security-closure-deployment.md`
- Modify: `powerpages/README.md`
- Modify: `knowledge/decision-log.md`

**Interfaces:**
- Consumes: everything above.
- Produces: the record. Nothing later depends on it.

- [ ] **Step 1: Run both gates one final time**

```powershell
powershell -NoProfile -File .\powerpages\Check-ComponentIds.ps1
powershell -NoProfile -File .\powerpages\Check-PortalSecurity.ps1
```

Both must exit 0. Do not upload otherwise.

- [ ] **Step 2: Confirm the target environment before uploading**

```powershell
$pac = Join-Path $env:USERPROFILE ".dotnet\tools\pac.exe"
& $pac auth list
& $pac org who
```

Expected: `Env_AQ_Dev`. Uploading to the wrong environment is not something the gates can catch.

- [ ] **Step 3: Upload**

```powershell
$pac = Join-Path $env:USERPROFILE ".dotnet\tools\pac.exe"
& $pac pages upload --path ".\powerpages\outcome-testing---outcometesting" --modelVersion Enhanced
```

`--modelVersion` takes `Enhanced` or `Standard` on PAC CLI 2.11.2; the `--modelVersion 2` form in the build guide is not valid syntax.

- [ ] **Step 4: Prove the page rules actually round-tripped**

Download to a scratch folder — never over `powerpages/`, which would overwrite uncommitted work — and diff:

```powershell
$pac = Join-Path $env:USERPROFILE ".dotnet\tools\pac.exe"
$scratch = Join-Path $env:TEMP 'pages-verify'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
& $pac pages download --path $scratch --webSiteId "b4cfe195-fd15-42e8-94e5-f27bcceaf5fc" --modelVersion Enhanced --overwrite
```

```bash
diff --strip-trailing-cr powerpages/outcome-testing---outcometesting/webpagerule.yml "$TEMP/pages-verify/outcome-testing---outcometesting/webpagerule.yml"
```

Expected: no differences. This is the step that proves `pac pages upload` carries `adx_webpageaccesscontrolrule`; download was already observed to serialise it, but upload was inferred rather than seen. If the five new rules are absent from the re-download, stop: the rules must then be created through the Portal Management app, and that fact must be recorded before anything else proceeds.

- [ ] **Step 5: Clear the site cache and run the role matrix**

In the design studio, sync configuration. Then, in an InPrivate session per role:

| Test | Expected |
|---|---|
| Anonymous requests `/`, `/cases`, `/review?id=…` | Redirected to sign-in, no content disclosed |
| Anonymous requests `/outcome-testing.css` | Served — styling must survive on the sign-in page |
| Tax Reviewer opens `/aqs-reviews` | Access denied |
| AQS Reviewer opens `/tax-reviews` | Access denied |
| Adviser opens `/review?id=…` | Access denied |
| Adviser opens `/remediation` | Renders |
| Tax Reviewer opens another checker's review | Renders read-only; save is refused |
| Any authenticated portal role opens `/cases` | Renders (OD-022) |
| A contact with no portal web role opens `/` | Access denied — fail closed. **Watch for a redirect loop:** the Access Denied page inherits Home's rule, so a role-less contact may be denied the page they are being sent to. If that happens, clear `adx_parentpageid` on `Access Denied`, `Page Not Found` and `Default Offline Page` so they leave Home's inheritance chain, and re-run. Do not weaken the Home rule to fix it. |
| Self-registration URL | Unavailable |

Do **not** assert that an Administrator is denied any page. `Grant Change to Administrators` sits on Home with All content scope and overrides Restrict Read site-wide, so that test could only ever fail.

- [ ] **Step 6: Write the deployment record**

Create `docs/deployment/2026-08-31-portal-security-closure-deployment.md` following the shape of `docs/deployment/2026-08-30-outcome-creation-deployment.md`: what was run, in what order, what the gates reported, the role matrix results, and any defect the DEV run exposed with its correction in place.

- [ ] **Step 7: Update the README**

In `powerpages/README.md`, rewrite the "Current state" section, which is stale in every row: web roles now exist and are bound, the Contact-scoped permissions exist, and the provisional-permission warning is replaced by a line pointing at `Check-PortalSecurity.ps1` as the release gate. Add the AD-059 band table from the spec's section 9, and record the filename trap: the repository's table permission files are named differently from what `pac pages download` emits, so a `--overwrite` download into `powerpages/` leaves two files per permission, each claiming the same id.

- [ ] **Step 8: Record the decisions**

Append to `knowledge/decision-log.md`:

- **AD-068** — page permissions introduced: one Restrict Read rule on Home scoped to exclude direct child web files, per-page rules for the role-specific branches, the subset rule honoured, and Access Denied / Page Not Found / Default Offline Page allow-listed as public. Note that Grant Change on Home overrides Restrict Read site-wide for Administrators.
- **AD-069** — write-scope permissions bind to the purpose-built `AL Portal` roles; read-all stays on Authenticated Users per OD-022, superseding the AD-067 finding that the roles are inert.
- **AD-059 amendment** — band table extended to web roles (`90`–`97`), site settings (`a0`–`af`) and page access control rules (`b0`–`bf`); the table-permission band narrowed to `60`–`6f` because `70` is a weblink set.
- **OD-019, OD-021** — marked implemented, naming `…097` as added and `…094` as retired.

- [ ] **Step 9: Commit**

```bash
git add docs/deployment powerpages/README.md knowledge/decision-log.md
git commit -m "docs(powerpages): DEV deployment record, README refresh, AD-068/AD-069"
```
