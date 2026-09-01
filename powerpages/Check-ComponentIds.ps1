<#
.SYNOPSIS
    Fails when two Power Pages components claim the same record id.

.DESCRIPTION
    Site metadata in this repository carries hand-minted record ids in the
    a1000000-0000-4000-8000-0000000000NN range, chosen per component type. The
    ranges are not enforced anywhere, so two different component types can be
    given the same id without anything complaining locally.

    In the Enhanced data model every component — web template, page template,
    web page, table permission, web link — is a row in the single
    `powerpagecomponent` table, keyed by `powerpagecomponentid`. Two components
    sharing an id are therefore one row, and `pac pages upload` silently
    replaces one with the other. The component that loses is not reported as
    missing; it simply stops existing, and every record pointing at it resolves
    to a component of the wrong type, which the portal answers with a generic
    server error and an error id.

    That is what happened to `OT Case List Page` and `OT Answer Options`, both
    minted as ...0021: /cases broke because its page template had been replaced
    by a web template.

    Run this before `pac pages upload`.

.EXAMPLE
    pwsh ./powerpages/Check-ComponentIds.ps1
#>

[CmdletBinding()]
param(
    # The downloaded site folder — the one passed to `pac pages upload`.
    [string]$SitePath
)

$ErrorActionPreference = 'Stop'

# Resolved here rather than as a parameter default: $PSScriptRoot is not reliably
# populated while parameter defaults are bound under Windows PowerShell 5.1.
if (-not $SitePath) {
    $root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $SitePath = Join-Path $root 'outcome-testing---outcometesting'
}

if (-not (Test-Path -LiteralPath $SitePath)) {
    throw "Site folder not found: $SitePath"
}

# The yml key that carries each component's OWN id, by file suffix. Keys not
# listed here (adx_webtemplateid inside a .pagetemplate.yml, for instance) are
# references to another component, not an identity, and must not be collected.
$identityKeys = @{
    '.webtemplate.yml'     = 'adx_webtemplateid'
    '.pagetemplate.yml'    = 'adx_pagetemplateid'
    '.webpage.yml'         = 'adx_webpageid'
    '.tablepermission.yml' = 'adx_entitypermissionid'
    '.webfile.yml'         = 'adx_webfileid'
    '.weblink.yml'         = 'adx_weblinkid'
    '.weblinkset.yml'      = 'adx_weblinksetid'
    '.contentsnippet.yml'  = 'adx_contentsnippetid'
    '.basicform.yml'       = 'adx_entityformid'
    '.list.yml'            = 'adx_entitylistid'
}

$claims = [System.Collections.Generic.List[object]]::new()

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

    $lines = Get-Content -LiteralPath $file.FullName
    $relative = $file.FullName.Substring($SitePath.Length).TrimStart([char]92, [char]47)

    # A yml may hold ONE component (a web template) or a LIST of them (every link in
    # a weblink set). Walking every line rather than taking the first match is what
    # makes the list files count: a collision in the fifth link of a set is as fatal
    # as one in the first, and checking only the first would report the file clean.
    $pendingName = $file.BaseName
    foreach ($line in $lines) {
        if ($line -match '^\s*-?\s*adx_name\s*:') {
            $pendingName = ($line -split ':', 2)[1].Trim()
        }
        if ($line -match "^\s*-?\s*$key\s*:") {
            $id = ($line -split ':', 2)[1].Trim()
            if (-not $id) { continue }
            $claims.Add([pscustomobject]@{
                Id   = $id.ToLowerInvariant()
                Name = $pendingName
                Kind = $kind
                Path = $relative
            })
        }
    }
}

# A web file is two records: the adx_webfile and the annotation holding its bytes.
# `objectid` points the note at the file, so it equals adx_webfileid — but the note
# has its own id, and giving annotationid the SAME guid as adx_webfileid makes the
# pair collide. The upload then writes the note and drops the file's own metadata,
# leaving a record with content but no adx_partialurl: the URL 404s and the site
# renders unstyled. Every working web file has a distinct annotationid.
$webFileFaults = [System.Collections.Generic.List[object]]::new()
foreach ($file in Get-ChildItem -LiteralPath $SitePath -Recurse -File -Filter '*.webfile.yml') {
    $lines = Get-Content -LiteralPath $file.FullName
    $get = {
        param($key)
        $line = $lines | Where-Object { $_ -match "^\s*$key\s*:" } | Select-Object -First 1
        if ($line) { ($line -split ':', 2)[1].Trim() } else { $null }
    }
    $webFileId = & $get 'adx_webfileid'
    $annotationId = & $get 'annotationid'
    $partialUrl = & $get 'adx_partialurl'

    if ($webFileId -and $annotationId -and $webFileId -eq $annotationId) {
        $webFileFaults.Add("$($file.Name): annotationid equals adx_webfileid ($webFileId). The note needs its own id.")
    }
    if (-not $partialUrl) {
        $webFileFaults.Add("$($file.Name): no adx_partialurl, so nothing can request this file by URL.")
    }
}

Write-Host "Checked $($claims.Count) component identities under $SitePath."

# A root web page and its language content page legitimately differ in id, but a
# shared id across two DIFFERENT components is the fault this looks for.
$collisions = $claims | Group-Object Id | Where-Object { $_.Count -gt 1 }

if ($webFileFaults.Count -gt 0) {
    Write-Host ''
    Write-Host 'Web file faults:' -ForegroundColor Red
    foreach ($fault in $webFileFaults) { Write-Host "  $fault" }
}

if (-not $collisions) {
    if ($webFileFaults.Count -eq 0) {
        Write-Host 'No duplicate component ids, no web file faults.' -ForegroundColor Green
        exit 0
    }
    exit 1
}

Write-Host ''
Write-Host 'Duplicate component ids found. Uploading this would delete one component per collision:' -ForegroundColor Red
foreach ($collision in $collisions) {
    Write-Host ''
    Write-Host "  $($collision.Name)" -ForegroundColor Red
    foreach ($claim in $collision.Group) {
        Write-Host "    $($claim.Kind.PadRight(16)) $($claim.Name)"
        Write-Host "    $(' ' * 16) $($claim.Path)"
    }
}
Write-Host ''
Write-Host 'Give one of each pair an unused id, and repoint anything referencing it.' -ForegroundColor Red
exit 1
