<#
.SYNOPSIS
    Round-trips a Dataverse solution into src/, leaving src/ an exact mirror of the export.

.DESCRIPTION
    Replaces `pac solution export` + `pac solution unpack --folder src`.

    The reason this exists: `pac solution unpack` UNPACKS into src/ but never prunes it. It
    reports "There are N unnecessary files" and then says "Not deleting files", so anything the
    export no longer contains is left behind. Every Code App rebuild renames its bundle
    (index-<hash>.js), so every round-trip strands the previous one in source control - two on
    2026-09-14 alone, one the round before. `--allowDelete` exists but takes the whole folder
    with it if the export is ever partial, which is a worse failure than a stale file.

    So this unpacks to a temp folder, compares, and mirrors: copies what changed, deletes only
    what the export genuinely no longer has, and prints both lists. Nothing is removed silently.

.PARAMETER OrgUrl
    The environment to export from. Defaults to Env_AQ_Dev.

.PARAMETER SolutionName
    Solution unique name. Defaults to OutcomeTesting.

.PARAMETER WhatIf
    Report what would change without touching src/.

.EXAMPLE
    pwsh ./scripts/round-trip-src.ps1
    pwsh ./scripts/round-trip-src.ps1 -WhatIf
#>
[CmdletBinding()]
param(
    [string]$OrgUrl = 'https://org0b075da8.crm11.dynamics.com/',
    [string]$SolutionName = 'OutcomeTesting',
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$srcPath = Join-Path $repoRoot 'src'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("solution-round-trip-" + [guid]::NewGuid().ToString('N'))
$zipPath = Join-Path $work 'exported.zip'
$unpackPath = Join-Path $work 'unpacked'

New-Item -ItemType Directory -Path $work -Force | Out-Null

try {
    Write-Host "Exporting $SolutionName from $OrgUrl ..." -ForegroundColor Cyan
    pac solution export --name $SolutionName --path $zipPath --managed false --overwrite --environment $OrgUrl
    if ($LASTEXITCODE -ne 0) { throw "pac solution export failed with exit code $LASTEXITCODE." }

    Write-Host "Unpacking to a temporary folder ..." -ForegroundColor Cyan
    pac solution unpack --zipfile $zipPath --folder $unpackPath --packagetype Unmanaged
    if ($LASTEXITCODE -ne 0) { throw "pac solution unpack failed with exit code $LASTEXITCODE." }

    # A partial or empty unpack must never be mirrored: that would delete the whole of src/.
    $exportedFiles = @(Get-ChildItem -Path $unpackPath -Recurse -File)
    if ($exportedFiles.Count -eq 0) {
        throw "The unpack produced no files. src/ has been left untouched."
    }

    function Get-RelativePaths([string]$root) {
        $map = @{}
        foreach ($file in Get-ChildItem -Path $root -Recurse -File) {
            $relative = $file.FullName.Substring($root.Length).TrimStart('\', '/')
            $map[$relative] = $file.FullName
        }
        return $map
    }

    $exported = Get-RelativePaths $unpackPath
    $current = @{}
    if (Test-Path $srcPath) { $current = Get-RelativePaths $srcPath }

    $orphans = @($current.Keys | Where-Object { -not $exported.ContainsKey($_) } | Sort-Object)
    $added = @($exported.Keys | Where-Object { -not $current.ContainsKey($_) } | Sort-Object)

    # Content comparison by hash, so an unchanged file is not reported as churn.
    $changed = @()
    foreach ($relative in $exported.Keys) {
        if (-not $current.ContainsKey($relative)) { continue }
        $a = (Get-FileHash -Path $exported[$relative] -Algorithm SHA256).Hash
        $b = (Get-FileHash -Path $current[$relative] -Algorithm SHA256).Hash
        if ($a -ne $b) { $changed += $relative }
    }
    $changed = @($changed | Sort-Object)

    Write-Host ""
    Write-Host "Exported $($exported.Count) files; src/ currently holds $($current.Count)." -ForegroundColor Cyan
    Write-Host "  new     : $($added.Count)"
    Write-Host "  changed : $($changed.Count)"
    Write-Host "  orphaned: $($orphans.Count)"

    if ($orphans.Count -gt 0) {
        Write-Host ""
        Write-Host "Orphaned in src/ (the export no longer contains these):" -ForegroundColor Yellow
        foreach ($o in $orphans) { Write-Host "  - $o" -ForegroundColor Yellow }
    }

    if ($WhatIf) {
        Write-Host ""
        Write-Host "-WhatIf: src/ was not modified." -ForegroundColor Magenta
        return
    }

    foreach ($relative in $added + $changed) {
        $destination = Join-Path $srcPath $relative
        $parent = Split-Path -Parent $destination
        if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        Copy-Item -Path $exported[$relative] -Destination $destination -Force
    }

    foreach ($relative in $orphans) {
        Remove-Item -Path (Join-Path $srcPath $relative) -Force
    }

    # Directories the pruning just emptied, deepest first so parents empty before they are tested.
    if (Test-Path $srcPath) {
        Get-ChildItem -Path $srcPath -Recurse -Directory |
            Sort-Object { $_.FullName.Length } -Descending |
            Where-Object { @(Get-ChildItem -Path $_.FullName -Recurse -File).Count -eq 0 } |
            ForEach-Object { Remove-Item -Path $_.FullName -Recurse -Force }
    }

    Write-Host ""
    Write-Host "src/ now mirrors the export exactly." -ForegroundColor Green
}
finally {
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
}
