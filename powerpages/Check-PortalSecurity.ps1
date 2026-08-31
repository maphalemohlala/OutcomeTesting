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

# Resolved here rather than as a parameter default: $PSScriptRoot is not reliably
# populated while parameter defaults are bound under Windows PowerShell 5.1.
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
# This is the shape of a .tablepermission.yml and a root .webpage.yml.
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
# This is the shape of webrole.yml, webpagerule.yml and sitesetting.yml.
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
    # Language variants live in a content-pages subfolder; skip only THAT folder.
    # Matching on $f.FullName (the full path) instead of the immediate parent
    # would also match if the repo itself ever sat under a directory named
    # "content-pages" — silently dropping every page in the scan.
    if ($f.Directory.Name -eq 'content-pages') { continue }
    $doc = Read-YamlDoc $f.FullName
    if ($doc.adx_webpageid) { $pages += ,$doc }
}
$pageById = @{}
foreach ($p in $pages) { $pageById[$p.adx_webpageid] = $p }

# A scan that finds zero pages is not proof the site is clean — it's proof page
# discovery broke (wrong -SitePath, a moved web-pages folder, an over-eager
# content-pages filter). Left unguarded, assertions 3 and 4 would simply have
# nothing to iterate and the gate would report a pass over a site nobody checked.
if ($pages.Count -eq 0) {
    Add-Failure 'guard empty-page-scan' "No web pages found under '$(Join-Path $SitePath 'web-pages')'. Page discovery is broken; refusing to treat that as a clean site."
}

$settings = Read-YamlList (Join-Path $SitePath 'sitesetting.yml')
$settingByName = @{}
foreach ($s in $settings) { $settingByName[$s.adx_name] = $s.adx_value }

# ---------------------------------------------------------------- 1, 2, 8
foreach ($p in $permissions) {
    $bound = @($p.adx_entitypermission_webrole)

    # A permission with no adx_entitypermission_webrole key at all yields @($null),
    # a one-element array whose element is $null — filter it out before the
    # ContainsKey calls below, which throw on a null key rather than returning false.
    foreach ($id in ($bound | Where-Object { $_ })) {
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

# Assertion 6 scans EVERY rule, not just Restrict Read ones: the anonymous role
# must never be bound to a page rule of any right, and a Grant Change rule is
# exactly the shape someone copies when adding a new one. Scoping this to
# $restrictRules would let an anonymous Grant Change rule pass silently.
foreach ($rule in $rules) {
    # A rule with no adx_webpageaccesscontrolrule_webrole key yields @($null);
    # filter it out rather than let the anonymous-role check compare against it.
    foreach ($id in (@($rule.adx_webpageaccesscontrolrule_webrole) | Where-Object { $_ })) {
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
foreach ($rule in $restrictRules) {
    # A site-scoped rule (adx_scope not 'page') carries no adx_webpageid at all —
    # "Grant Change to Content" is exactly this shape today, at adx_right 1, but
    # nothing stops a future Restrict Read rule from being scoped the same way.
    # Indexing a hashtable with a null key throws, so skip rather than crash;
    # such a rule can't be attributed to any one page for assertions 3-5 anyway.
    if (-not $rule.adx_webpageid) { continue }
    $ruleByPage[$rule.adx_webpageid] = $rule
}

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
            # Filter out $null here: a rule with no adx_webpageaccesscontrolrule_webrole
            # key yields @($null), which would otherwise (a) manufacture a spurious
            # "extra role" that is really just "no roles bound", and (b) crash the
            # ContainsKey lookup below, which throws on a null key.
            $own = @($effective.adx_webpageaccesscontrolrule_webrole) | Where-Object { $_ }
            $parent = @($ancestor.adx_webpageaccesscontrolrule_webrole) | Where-Object { $_ }
            $extra = @($own | Where-Object { $parent -notcontains $_ })
            if ($extra.Count -gt 0) {
                $names = ($extra | ForEach-Object { if ($roleById.ContainsKey($_)) { $roleById[$_].adx_name } else { $_ } }) -join ', '
                Add-Failure '4 role-not-in-parent' "Rule '$($effective.adx_name)' grants roles absent from ancestor rule '$($ancestor.adx_name)': $names."
            }
        }
    }
}

# ---------------------------------------------------------------- 7
# Values on disk are inconsistently quoted (true, 'true', False, '') depending on
# how each setting was last saved in the maker portal, so the comparison strips
# surrounding quotes and is case-insensitive rather than trusting exact text.
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
