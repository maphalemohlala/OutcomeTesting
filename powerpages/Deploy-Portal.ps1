<#
.SYNOPSIS
    The only safe way to run `pac pages upload` against this site.

.DESCRIPTION
    Running `pac pages upload` directly on this site does one of two harmful
    things, and there is no third option:

      * With `table-permissions/` present, the run aborts. `pac` 2.11.2 routes a
        **parent-scoped** table permission (`adx_scope: 756150003` with an
        `adx_parententitypermission`) down the Standard-model path and writes to
        `adx_entitypermission`, a table that does not exist on an enhanced data
        model site. Two permissions in source are parent-scoped — `...072` and
        `...06c` — and the run dies on the first. Components ordered after it are
        silently not deployed, and which ones those are varies with what happens
        to be dirty, so a run that reports failure has still changed the site.
        Tracked as OD-034.

      * With `table-permissions/` moved aside — the workaround used up to now —
        the upload completes, and `pac` deletes every table permission that the
        manifest lists and the source folder no longer contains. This is not
        limited to `--forceUploadAll`. It has fired at least twice. On
        2026-09-06 DEV was left holding 2 of 13 permissions, and the two
        survivors were exactly the two the manifest did not know about
        (`...06b`, `...076`) — the correlation is total, which is what identifies
        the manifest as the trigger rather than the upload as a whole.

        The damage is not subtle: with the Global read grants gone, every list
        template queries a table the signed-in user cannot read, and with
        `Review Instance - assigned to me` gone there is no reviewer write path
        at all. It is fail-closed, so an outage rather than an exposure.

    This script takes the second path and removes what makes it dangerous. It
    moves `table-permissions/` aside AND strips the `adx_entitypermission` and
    `adx_entitypermission_webrole` blocks out of the manifest, so `pac` has
    nothing to upload and nothing to reconcile away. It then deploys the
    permissions with `restoretablepermissions`, which writes `powerpagecomponent`
    type 18 rows directly and is the only thing on this site that can create a
    table permission at all. Finally it verifies by query, because on this site a
    successful-looking upload is not evidence that a component landed: the table
    permissions of THIS site (not every site in the environment), and the most recently
    edited web template's source against what -OrgUrl serves, which is what shows the
    upload itself reached -OrgUrl. Every pac call names -OrgUrl with --environment; the
    active pac profile is never used.

    The manifest is deliberately left stripped. `pac` does not own table
    permissions here, so a manifest section for them is not tracking — it is the
    loaded gun above, waiting for the next run. `pac pages download` rebuilds the
    section from the environment, truthfully, whenever someone next downloads;
    until then its absence is the safe state.

.PARAMETER OrgUrl
    Target environment. Deliberately mandatory: a deploy script must never guess
    which environment it is writing to.

.EXAMPLE
    powershell -NoProfile -File .\powerpages\Deploy-Portal.ps1 -OrgUrl https://org0b075da8.crm11.dynamics.com

.EXAMPLE
    # Check what is actually deployed, without uploading anything.
    powershell -NoProfile -File .\powerpages\Deploy-Portal.ps1 -OrgUrl https://org0b075da8.crm11.dynamics.com -VerifyOnly
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OrgUrl,

    # The site folder — the one passed to `pac pages upload`, not `powerpages/`.
    [string]$SitePath,

    # Reconcile rather than add. Safe here only because the permissions are out
    # of both the source folder and the manifest before `pac` ever runs.
    [switch]$ForceUploadAll,

    # Query the environment and report, changing nothing.
    [switch]$VerifyOnly,

    [switch]$SkipGates
)

$ErrorActionPreference = 'Stop'

# Resolved here rather than as a parameter default: $PSScriptRoot is not reliably
# populated while parameter defaults are bound under Windows PowerShell 5.1.
if (-not $SitePath) { $SitePath = Join-Path $PSScriptRoot 'outcome-testing---outcometesting' }
$SitePath = (Resolve-Path $SitePath).Path
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$permissionsFolder = Join-Path $SitePath 'table-permissions'
$portalConfig      = Join-Path $SitePath '.portalconfig'

# The site this folder describes. Every query below is scoped to it, so a second site in the
# same environment can neither hide a missing permission nor appear as drift.
$siteIdLine = @(Get-Content -Path (Join-Path $SitePath 'website.yml') | Where-Object { $_ -match '^adx_websiteid:\s*(\S+)\s*$' })
if ($siteIdLine.Count -ne 1 -or $siteIdLine[0] -notmatch '^adx_websiteid:\s*([0-9a-fA-F-]{36})\s*$') {
    throw "No adx_websiteid in $(Join-Path $SitePath 'website.yml')."
}
$siteId = $Matches[1].ToLowerInvariant()

# The manifest section names that must never reach a `pac pages upload` on this
# site. Both describe table permissions: the records and their web-role links.
$permissionKeys = @('adx_entitypermission', 'adx_entitypermission_webrole')

# ---------------------------------------------------------------- tools

function Resolve-Tool {
    param([string]$Name, [string[]]$Candidates)

    $onPath = Get-Command $Name -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    foreach ($candidate in $Candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }
    throw "$Name not found. Looked on PATH and in: $($Candidates -join ', ')"
}

$pac = Resolve-Tool 'pac' @(
    (Join-Path $env:USERPROFILE '.dotnet\tools\pac.exe'),
    (Join-Path $env:LOCALAPPDATA 'Microsoft\PowerAppsCLI\pac.exe')
)
$dotnet = Resolve-Tool 'dotnet' @(
    (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe')
)

# The registration tool targets net8.0 and this machine carries only the .NET 10
# runtime; without this it refuses to start with a message that reads like a
# missing SDK. Same reason as the RollForward property in its csproj.
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'

# ---------------------------------------------------------------- source

function Get-SourcePermissions {
    $files = @(Get-ChildItem -Path $permissionsFolder -Filter '*.tablepermission.yml' -ErrorAction SilentlyContinue)
    $result = @()
    foreach ($file in $files) {
        $lines = Get-Content -Path $file.FullName
        $id    = @($lines | Where-Object { $_ -match '^adx_entitypermissionid:\s*(.+)$' } | ForEach-Object { $Matches[1].Trim() })
        $name  = @($lines | Where-Object { $_ -match '^adx_entityname:\s*(.+)$' }         | ForEach-Object { $Matches[1].Trim() })
        if ($id.Count -gt 0) {
            $result += [pscustomobject]@{
                Id   = $id[0].ToLowerInvariant()
                Name = if ($name.Count -gt 0) { $name[0] } else { $file.Name }
                File = $file.Name
            }
        }
    }
    return $result
}

# ---------------------------------------------------------------- verify

# pac exits 0 when it cannot connect (a revoked token, AADSTS50173), and its error text
# carries a Trace ID and a Correlation ID - two GUIDs a pattern match would read as data.
# On 2026-11-17 that made a DEV site holding all 44 permissions look like it held 2.
function Test-PacFailed([string]$text) {
    return $text -match 'Failed to connect to Dataverse|Could not connect to the Dataverse|AADSTS\d+'
}

function Get-DeployedPermissionIds {
    $fetch = @"
<fetch>
  <entity name="powerpagecomponent">
    <attribute name="powerpagecomponentid" />
    <filter>
      <condition attribute="powerpagecomponenttype" operator="eq" value="18" />
      <condition attribute="powerpagesiteid" operator="eq" value="$siteId" />
    </filter>
  </entity>
</fetch>
"@
    $queryFile = Join-Path ([System.IO.Path]::GetTempPath()) ("ot-perms-{0}.xml" -f ([guid]::NewGuid().ToString('N')))
    try {
        Set-Content -Path $queryFile -Value $fetch -Encoding UTF8
        # One logical blob, matched by pattern: the column layout of `pac env
        # fetch` is presentation, not a contract, so nothing here parses columns.
        $raw = (& $pac env fetch --environment $OrgUrl --xmlFile $queryFile 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0 -or (Test-PacFailed $raw)) {
            throw "pac env fetch could not read $OrgUrl, so it reported nothing about the permissions there. Run 'pac auth create' and retry:`n$raw"
        }
        $ids = [regex]::Matches($raw, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}') |
               ForEach-Object { $_.Value.ToLowerInvariant() }
        return @($ids | Sort-Object -Unique)
    }
    finally {
        Remove-Item -Path $queryFile -Force -ErrorAction SilentlyContinue
    }
}

function Test-Deployment {
    $source     = @(Get-SourcePermissions)
    try {
        $deployed = @(Get-DeployedPermissionIds)
    }
    catch {
        # Reported, not fatal: the template check below reads through another client and
        # still says whether the site is current. The run still fails.
        Write-Host ''
        Write-Host "Table permissions NOT checked: $($_.Exception.Message.Split([char]10)[0])" -ForegroundColor Red
        return $false
    }
    $sourceIds  = @($source | ForEach-Object { $_.Id })

    $missing = @($source   | Where-Object { $deployed  -notcontains $_.Id })
    $extra   = @($deployed | Where-Object { $sourceIds -notcontains $_ })

    Write-Host ''
    Write-Host "Table permissions: $($source.Count) in source, $($deployed.Count) for site $siteId in $OrgUrl."

    if ($missing.Count -gt 0) {
        Write-Host ''
        Write-Host "$($missing.Count) permission(s) in source but NOT deployed:" -ForegroundColor Red
        foreach ($m in $missing) { Write-Host "  $($m.Id)  $($m.Name)" -ForegroundColor Red }
    }
    if ($extra.Count -gt 0) {
        Write-Host ''
        Write-Host "$($extra.Count) permission(s) deployed but NOT in source:" -ForegroundColor Yellow
        foreach ($e in $extra) { Write-Host "  $e" -ForegroundColor Yellow }
        Write-Host '  These are drift. Nothing here deletes them; decide deliberately.' -ForegroundColor Yellow
    }
    if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
        Write-Host 'Every table permission in source is deployed, and nothing else is.' -ForegroundColor Green
    }

    return ($missing.Count -eq 0)
}

# The table-permission check above proves restoretablepermissions reached -OrgUrl; it says
# nothing about where `pac pages upload` went. This compares the web templates in source
# with what -OrgUrl serves for this site. The most recently edited template - the change
# being deployed - must match, or the upload did not land here. Others that differ are
# reported as drift: pac uploads only what its manifest thinks changed.
function Get-DeployedTemplates {
    $fetch = @"
<fetch>
  <entity name="powerpagecomponent">
    <attribute name="powerpagecomponentid" />
    <attribute name="content" />
    <filter>
      <condition attribute="powerpagecomponenttype" operator="eq" value="8" />
      <condition attribute="powerpagesiteid" operator="eq" value="$siteId" />
    </filter>
  </entity>
</fetch>
"@
    $queryFile = Join-Path ([System.IO.Path]::GetTempPath()) ("ot-templates-{0}.xml" -f ([guid]::NewGuid().ToString('N')))
    try {
        Set-Content -Path $queryFile -Value $fetch -Encoding UTF8
        # stderr stays on the console: under Stop, 2>&1 on a native exe throws on its first line.
        $raw = (& $dotnet run --project (Join-Path $repoRoot 'plugins\OutcomeTesting.Registration') -- fetch $OrgUrl "@$queryFile" | Out-String)
        if ($LASTEXITCODE -ne 0) { throw "fetch of web templates failed:`n$raw" }
        # The verb prints its sign-in lines, then the indented JSON from a line holding only "{".
        $json = [regex]::Match($raw, '(?ms)^\{\s*$.*')
        if (-not $json.Success) { throw "fetch of web templates returned no JSON:`n$raw" }
        $map = @{}
        foreach ($row in @(($json.Value | ConvertFrom-Json).rows)) {
            $source = ''
            if ($row.content) { $source = [string](($row.content | ConvertFrom-Json).source) }
            $map[([string]$row.powerpagecomponentid).ToLowerInvariant()] = $source
        }
        return $map
    }
    finally {
        Remove-Item -Path $queryFile -Force -ErrorAction SilentlyContinue
    }
}

function Test-UploadLanded {
    $deployed = Get-DeployedTemplates
    if ($deployed.Count -eq 0) {
        Write-Host "No web templates for site $siteId in ${OrgUrl}: this site is not there, so the upload did not land in it." -ForegroundColor Red
        return $false
    }

    $local = @()
    foreach ($yml in @(Get-ChildItem -Path (Join-Path $SitePath 'web-templates') -Filter '*.webtemplate.yml' -Recurse)) {
        $idLine = @(Get-Content -Path $yml.FullName | Where-Object { $_ -match '^adx_webtemplateid:\s*(\S+)' })
        $sourceFile = Join-Path $yml.DirectoryName ($yml.Name -replace '\.webtemplate\.yml$', '.webtemplate.source.html')
        if ($idLine.Count -eq 0 -or -not (Test-Path $sourceFile)) { continue }
        [void]($idLine[0] -match '^adx_webtemplateid:\s*(\S+)')
        # Stored templates keep the file's own line endings (DEV, 2026-11-16: CRLF stored as
        # CRLF), so both sides are folded the same way rather than guessing what upload does.
        $text = [System.IO.File]::ReadAllText($sourceFile, (New-Object System.Text.UTF8Encoding($false))).TrimStart([char]0xFEFF) -replace "`r`n", "`n"
        $local += [pscustomobject]@{
            Id       = $Matches[1].ToLowerInvariant()
            Name     = $yml.Directory.Name
            Source   = $text
            Modified = (Get-Item $sourceFile).LastWriteTimeUtc
        }
    }

    $differs = @($local | Where-Object {
        -not $deployed.ContainsKey($_.Id) -or ($deployed[$_.Id].TrimStart([char]0xFEFF) -replace "`r`n", "`n") -cne $_.Source
    })
    $newest = $local | Sort-Object Modified -Descending | Select-Object -First 1

    Write-Host ''
    Write-Host "Web templates: $($local.Count) in source, $($deployed.Count) for this site in $OrgUrl, $($differs.Count) differ."
    foreach ($d in $differs) {
        if ($d.Id -ne $newest.Id) { Write-Host "  differs (drift, not uploaded by this run): $($d.Name)" -ForegroundColor Yellow }
    }

    if ($differs | Where-Object { $_.Id -eq $newest.Id }) {
        Write-Host "The most recently edited template, $($newest.Name), does not match $OrgUrl." -ForegroundColor Red
        Write-Host 'The upload did not land in this environment.' -ForegroundColor Red
        return $false
    }

    Write-Host "The most recently edited template, $($newest.Name), matches $OrgUrl." -ForegroundColor Green
    return $true
}

# ---------------------------------------------------------------- manifest

# The manifest is a flat map of `key:` at column 0, each followed by an indented
# block. Splitting on that shape lets a whole section be lifted out without
# touching the rest of the file, and without a YAML library.
function Split-Manifest {
    param([string[]]$Lines)

    $blocks  = New-Object System.Collections.ArrayList
    $current = $null
    foreach ($line in $Lines) {
        if ($line -match '^([A-Za-z_][A-Za-z0-9_]*):\s*$') {
            $current = [pscustomobject]@{ Key = $Matches[1]; Lines = (New-Object System.Collections.ArrayList) }
            [void]$blocks.Add($current)
        }
        elseif ($null -eq $current) {
            # Anything before the first key. None is expected; carried anyway so
            # this can never silently drop content.
            $current = [pscustomobject]@{ Key = ''; Lines = (New-Object System.Collections.ArrayList) }
            [void]$blocks.Add($current)
        }
        [void]$current.Lines.Add($line)
    }
    return $blocks
}

function Remove-PermissionSections {
    param([string]$ManifestPath)

    # Read and write as raw text with the file's own newline convention. The
    # manifest is LF and BOM-less; Set-Content would rewrite it as CRLF with a
    # BOM and show up as a whole-file diff.
    $raw = [System.IO.File]::ReadAllText($ManifestPath)
    if ($raw.Contains("`r`n")) { $newline = "`r`n" } else { $newline = "`n" }

    $blocks  = Split-Manifest ($raw -split "`r?`n")
    $kept    = @($blocks | Where-Object { $permissionKeys -notcontains $_.Key })
    $removed = @($blocks | Where-Object { $permissionKeys -contains    $_.Key })

    if ($removed.Count -eq 0) { return 0 }

    $out = (($kept | ForEach-Object { $_.Lines }) -join $newline)
    [System.IO.File]::WriteAllText($ManifestPath, $out, (New-Object System.Text.UTF8Encoding($false)))

    $records = 0
    foreach ($block in $removed) {
        $records += @($block.Lines | Where-Object { $_ -match '^\s*-\s+RecordId:' }).Count
    }
    return $records
}

# ---------------------------------------------------------------- run

Write-Host "Site        $SitePath"
Write-Host "Environment $OrgUrl"

if ($VerifyOnly) {
    $permissionsOk = Test-Deployment
    $templatesOk = Test-UploadLanded
    if ($permissionsOk -and $templatesOk) { exit 0 }
    exit 1
}

if (-not $SkipGates) {
    Write-Host ''
    Write-Host '--- Gates ---------------------------------------------------------'
    foreach ($gate in @('Check-ComponentIds.ps1', 'Check-PortalSecurity.ps1')) {
        $gatePath = Join-Path $PSScriptRoot $gate
        & powershell -NoProfile -ExecutionPolicy Bypass -File $gatePath -SitePath $SitePath
        if ($LASTEXITCODE -ne 0) {
            Write-Host ''
            Write-Host "$gate failed. Nothing uploaded." -ForegroundColor Red
            exit 1
        }
    }
    # The stylesheet the site serves is built from powerpages\outcome-testing.src.css.
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-PortalCss.ps1') -SitePath $SitePath -Check
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host 'Build-PortalCss.ps1 -Check failed. Nothing uploaded.' -ForegroundColor Red
        exit 1
    }
}

$source = @(Get-SourcePermissions)
if ($source.Count -eq 0) {
    Write-Host ''
    Write-Host "No *.tablepermission.yml under $permissionsFolder. Refusing to run: an" -ForegroundColor Red
    Write-Host 'empty folder is indistinguishable from the state that caused the deletions.' -ForegroundColor Red
    exit 1
}

$stash = Join-Path ([System.IO.Path]::GetTempPath()) ("ot-portal-deploy-{0}" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $stash -Force | Out-Null

$manifests    = @(Get-ChildItem -Path $portalConfig -Filter '*-manifest.yml' -ErrorAction SilentlyContinue)
$stashedPerms = Join-Path $stash 'table-permissions'

Write-Host ''
Write-Host '--- Neutralising the upload ---------------------------------------'
Write-Host "Backup: $stash"

Copy-Item -Path $permissionsFolder -Destination $stashedPerms -Recurse -Force
foreach ($manifest in $manifests) {
    Copy-Item -Path $manifest.FullName -Destination (Join-Path $stash $manifest.Name) -Force
}

Remove-Item -Path $permissionsFolder -Recurse -Force
Write-Host "Moved $($source.Count) permission file(s) out of the upload."

foreach ($manifest in $manifests) {
    $records = Remove-PermissionSections $manifest.FullName
    Write-Host "Stripped $records permission record(s) from $($manifest.Name)."
}

$uploadFailed = $false
try {
    Write-Host ''
    Write-Host '--- pac pages upload ----------------------------------------------'
    # --environment: without it pac uploads to the active profile, which defaults to neither
    # DEV nor TEST, while everything after this writes to and verifies -OrgUrl.
    $uploadArgs = @('pages', 'upload', '--path', $SitePath, '--modelVersion', 'Enhanced', '--environment', $OrgUrl)
    if ($ForceUploadAll) { $uploadArgs += '--forceUploadAll' }
    $uploadOut = (& $pac @uploadArgs 2>&1 | Out-String)
    Write-Host $uploadOut
    if ($LASTEXITCODE -ne 0 -or (Test-PacFailed $uploadOut)) { $uploadFailed = $true }
}
finally {
    # Unconditional: the source tree must never be left without its permissions,
    # whatever the upload did. The manifest sections stay stripped by design —
    # see the description above.
    if (-not (Test-Path $permissionsFolder)) {
        Copy-Item -Path $stashedPerms -Destination $permissionsFolder -Recurse -Force
    }
    Write-Host ''
    Write-Host "Restored $permissionsFolder"
}

if ($uploadFailed) {
    Write-Host ''
    Write-Host 'pac pages upload reported failure. Permissions were NOT deployed.' -ForegroundColor Red
    Write-Host 'A failed run on this site still changes components. Verify before retrying:' -ForegroundColor Red
    Write-Host "  -VerifyOnly against $OrgUrl" -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '--- Table permissions ---------------------------------------------'
& $dotnet run --project (Join-Path $repoRoot 'plugins\OutcomeTesting.Registration') -- restoretablepermissions $OrgUrl $SitePath
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host 'restoretablepermissions failed. The site is deployed WITHOUT its' -ForegroundColor Red
    Write-Host 'table permissions, which is an outage. Fix and re-run.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '--- Verification by query -----------------------------------------'
$permissionsOk = Test-Deployment
$uploadLanded = Test-UploadLanded
if (-not $permissionsOk -or -not $uploadLanded) {
    Write-Host ''
    Write-Host 'Deployment incomplete. Do not treat this run as successful.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'Portal deployed and verified.' -ForegroundColor Green
Write-Host 'Site cache: templates can take ~15 minutes to appear, or clear the cache.'
exit 0
