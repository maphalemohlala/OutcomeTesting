<#
.SYNOPSIS
    The only safe way to run `pac solution pack` over this repository's `src/`.

.DESCRIPTION
    `pac solution pack --folder src` puts the committed plug-in assembly in the
    zip, and importing that zip replaces the live assembly with whatever was
    last committed — at the same version and the same public key token, so
    nothing about the import looks unusual and nothing afterwards explains the
    breakage.

    This is not hypothetical. It was caught during the 2026-08-30 DEV
    deployment, before the import ran: the committed DLL held 11 plug-in types
    while DEV was running 16, so the import would have removed
    `SubmitReviewPlugin`, `ResponseGuardPlugin`, `ResponseProgressPlugin`,
    `SetUserActivePlugin` and `UpdateUserPlugin` from the live assembly,
    breaking five commands at runtime with no schema change to point at.
    Tracked as AD-062, which extends AD-061: the plug-in assembly reaches an
    environment through `pac plugin push` only, never through a solution import.

    The hazard keeps coming back because the AD-013 round trip is what puts the
    DLL there. Exporting the solution from DEV and copying it over `src/` is
    correct and is how `src/` stays the source of truth — and it restores
    `src/PluginAssemblies/` every time it runs. So this cannot be fixed by
    deleting the folder once. It needs a pack path that strips it on every run,
    which is what this script is.

    What it does: copies `src/` to a staging folder, deletes
    `PluginAssemblies/` from the copy, packs that, and verifies the resulting
    zip contains no assembly before reporting success. `src/` itself is never
    modified.

    `pac` reports the assembly root component as "not defined in
    customizations". That line is expected and is the evidence the strip
    worked — it appears alongside the same line for `CanvasApps`, which is the
    long-standing AD-012 warning.

.PARAMETER OutFile
    Where to write the packed solution. Defaults to `bin/OutcomeTesting.zip`
    beside the repository root.

.PARAMETER PackageType
    `Unmanaged` (default) or `Managed`.

.PARAMETER StagingPath
    Where to stage the stripped copy. Defaults to a temp folder, removed
    afterwards.

.EXAMPLE
    ./plugins/deploy/Pack-Schema-Solution.ps1
    ./plugins/deploy/Pack-Schema-Solution.ps1 -PackageType Managed -OutFile ./bin/OutcomeTesting_managed.zip
#>
[CmdletBinding()]
param(
    [string] $OutFile,
    [ValidateSet('Unmanaged', 'Managed')]
    [string] $PackageType = 'Unmanaged',
    [string] $StagingPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sourcePath = Join-Path $repoRoot 'src'

if (-not (Test-Path $sourcePath)) {
    throw "No src/ folder at $sourcePath."
}

if (-not $OutFile) {
    $OutFile = Join-Path $repoRoot "bin\OutcomeTesting$(if ($PackageType -eq 'Managed') { '_managed' }).zip"
}

$outDir = Split-Path -Parent $OutFile
if ($outDir -and -not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force $outDir | Out-Null
}

$temporaryStaging = -not $StagingPath
if ($temporaryStaging) {
    $StagingPath = Join-Path ([System.IO.Path]::GetTempPath()) "src-schema-$([Guid]::NewGuid().ToString('N'))"
}

$pac = (Get-Command pac -ErrorAction SilentlyContinue).Source
if (-not $pac) {
    # pac installs as a dotnet tool and is not always on PATH in a non-interactive shell.
    $candidate = Join-Path $env:USERPROFILE '.dotnet\tools\pac.exe'
    if (Test-Path $candidate) { $pac = $candidate }
}
if (-not $pac) {
    throw 'pac was not found on PATH or in ~/.dotnet/tools. Install the Power Platform CLI.'
}

try {
    if (Test-Path $StagingPath) { Remove-Item -Recurse -Force $StagingPath }
    Write-Host "Staging src/ -> $StagingPath"
    Copy-Item -Recurse $sourcePath $StagingPath

    $assemblies = Join-Path $StagingPath 'PluginAssemblies'
    if (Test-Path $assemblies) {
        Remove-Item -Recurse -Force $assemblies
        Write-Host 'Stripped PluginAssemblies/ from the copy (AD-062).'
    }
    else {
        # Not an error: it means the round trip has not run since the folder was
        # last cleared. Said out loud so its absence is never mistaken for the
        # strip having happened.
        Write-Host 'No PluginAssemblies/ in src/ — nothing to strip.'
    }

    Write-Host "Packing $PackageType -> $OutFile"
    & $pac solution pack --folder $StagingPath --zipfile $OutFile --packagetype $PackageType
    if ($LASTEXITCODE -ne 0) {
        throw "pac solution pack failed with exit code $LASTEXITCODE."
    }

    # The pack is not the proof. Read the zip back, because a strip that silently
    # did not happen produces a zip that looks exactly like one that did.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($OutFile)
    try {
        $carried = @($zip.Entries | Where-Object { $_.FullName -match '\.dll$' })
        $entryCount = $zip.Entries.Count
    }
    finally {
        $zip.Dispose()
    }

    if ($carried.Count -gt 0) {
        Remove-Item -Force $OutFile
        throw ("The packed solution carries $($carried.Count) assembly file(s): " +
               ($carried.FullName -join ', ') +
               '. Deleted the zip rather than leave an importable one that would replace the live assembly.')
    }

    Write-Host "OK: $entryCount entries, no assembly. Safe to import." -ForegroundColor Green
}
finally {
    if ($temporaryStaging -and (Test-Path $StagingPath)) {
        Remove-Item -Recurse -Force $StagingPath
    }
}
