<#
.SYNOPSIS
    Imports the OutcomeTesting solution and proves it landed and its plug-in steps are running.

.DESCRIPTION
    Audit finding 3, 2026-09-20.

    "The import succeeded" and "the rules are running" are different facts, and only the
    first one is reported. Dataverse imports an SdkMessageProcessingStep DISABLED unless
    activation is requested, and `pac solution import` requests it only with
    --activate-plugins.

    That is not a hypothetical. On 2026-09-02 an import without the flag switched off the
    six al_response steps in DEV, and nobody noticed for a week: the code was present, the
    tests passed, and only the step state was wrong. Those steps are where the
    server-side enforcement lives - the conditional root cause, the suitability grade rule,
    the answer guards - so a silent deactivation turns every rule into a suggestion while
    every screen still looks right.

    And "pac exited 0" is not "the import happened": on a revoked token pac can exit 0
    without ever connecting. So there are three checks, because the cost of being wrong is
    silent:

      1. --activate-plugins is not a parameter of this script. It is always passed, and
         there is no switch to turn it off. Anyone who wants an import without it can call
         pac directly and own that decision.
      2. The target's solution version is read back after the import and must equal the
         version in the package's solution.xml (and its managed flag must match).
      3. `verifysteps` confirms every step the PACKAGE declares (its customizations.xml,
         not the working tree) is present AND enabled in the target.

      A non-zero exit means the environment is not safe to use, whatever pac said.

    The solution files themselves also now carry <StateCode>Enabled</StateCode>, re-applied
    after every export by Restore-StepState in round-trip-src.ps1. That is the extra belt
    that would survive somebody bypassing this script.

.PARAMETER Environment
    The target environment URL. Required - there is deliberately no default, because
    the active pac profile points somewhere that is not always what the caller meant.

.PARAMETER ZipFile
    The solution package to import.

.PARAMETER Managed
    Asserts the package is managed. TEST and PROD take managed packages; DEV is the
    authoring environment and takes unmanaged. Without it the package must be unmanaged.
    The script refuses before importing when the package says otherwise, and fails when the
    target does not hold the solution in the same form afterwards.

.EXAMPLE
    ./scripts/Import-Solution.ps1 -Environment https://orgXXXX.crm11.dynamics.com/ -ZipFile release/OutcomeTesting_1_0_32_0_managed.zip -Managed

.NOTES
    AD-062: anything packing the whole of src/ must strip PluginAssemblies/ first, because
    the round trip puts a committed DLL back there and an import would revert the live
    assembly. This script imports a package someone else built; it does not pack.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Environment,

    [Parameter(Mandatory = $true)]
    [string]$ZipFile,

    [switch]$Managed
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$registration = Join-Path $repoRoot 'plugins/OutcomeTesting.Registration'

if (-not (Test-Path $ZipFile)) {
    throw "Solution package not found: $ZipFile"
}
$ZipFile = (Resolve-Path $ZipFile).Path

# ---------------------------------------------------------------- the package

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($ZipFile)
try {
    $entry = $zip.GetEntry('solution.xml')
    if (-not $entry) { throw "No solution.xml in $ZipFile - not a solution package." }
    $reader = New-Object System.IO.StreamReader($entry.Open())
    try { [xml]$manifestXml = $reader.ReadToEnd() } finally { $reader.Dispose() }
}
finally {
    $zip.Dispose()
}

$manifest = $manifestXml.ImportExportXml.SolutionManifest
$uniqueName = [string]$manifest.UniqueName
$packageVersion = [string]$manifest.Version
$packageManaged = ([string]$manifest.Managed) -eq '1'

if (-not $uniqueName -or -not $packageVersion) {
    throw "solution.xml in $ZipFile names no UniqueName or Version."
}
if ($uniqueName -notmatch '^[A-Za-z0-9_]+$') {
    throw "Unexpected solution unique name '$uniqueName' in $ZipFile."
}
if ($packageManaged -ne [bool]$Managed) {
    $isForm = if ($packageManaged) { 'managed' } else { 'unmanaged' }
    $askedForm = if ($Managed) { 'managed (-Managed)' } else { 'unmanaged (no -Managed)' }
    throw "$ZipFile is a $isForm package, but the call asked for $askedForm. Nothing imported."
}

Write-Host "Importing $ZipFile into $Environment" -ForegroundColor Cyan
Write-Host "  package: $uniqueName $packageVersion, $(if ($packageManaged) { 'managed' } else { 'unmanaged' })"
Write-Host "  --activate-plugins is always passed. See the notes in this script." -ForegroundColor DarkGray
Write-Host ""

$importArgs = @(
    'solution', 'import',
    '--path', $ZipFile,
    '--environment', $Environment,
    '--activate-plugins',
    '--publish-changes'
)

# A silent pac CLI does not mean nothing happened - the job runs server-side - and an exit
# of 0 does not mean something did. The version read-back below settles which.
& pac @importArgs
$importExit = $LASTEXITCODE

if ($importExit -ne 0) {
    Write-Host ""
    Write-Host "pac solution import exited $importExit." -ForegroundColor Yellow
    Write-Host "Check the importjob table before assuming nothing landed; the job runs server-side." -ForegroundColor Yellow
}

# ---------------------------------------------------------------- read-back

Write-Host ""
Write-Host "Reading back the solution version in $Environment..." -ForegroundColor Cyan

$query = "solutions?`$filter=uniquename eq '$uniqueName'&`$select=version,ismanaged"
Push-Location $registration
try {
    # stderr is left on the console: under Stop, 2>&1 on a native exe turns its first line into a throw.
    $raw = (& dotnet run --project . -- webapi $Environment GET $query | Out-String)
    $readExit = $LASTEXITCODE
}
finally {
    Pop-Location
}

$target = $null
$httpAt = $raw.IndexOf("HTTP 200")
$start = if ($httpAt -ge 0) { $raw.IndexOf("{", $httpAt) } else { -1 }
if ($readExit -eq 0 -and $start -ge 0) {
    try { $target = @(($raw.Substring($start) | ConvertFrom-Json).value) } catch { $target = $null }
}

if ($null -eq $target) {
    Write-Host $raw
    Write-Host "IMPORT NOT VERIFIED: could not read the solution back from $Environment." -ForegroundColor Red
    exit 1
}
if ($target.Count -ne 1) {
    Write-Host "IMPORT NOT COMPLETE: $Environment holds no solution '$uniqueName'." -ForegroundColor Red
    exit 1
}

$targetVersion = [string]$target[0].version
$targetManaged = [bool]$target[0].ismanaged
Write-Host "  $Environment holds $uniqueName $targetVersion, $(if ($targetManaged) { 'managed' } else { 'unmanaged' })"

if ($targetVersion -ne $packageVersion -or $targetManaged -ne $packageManaged) {
    Write-Host ""
    Write-Host "IMPORT NOT COMPLETE: the package is $packageVersion but $Environment holds $targetVersion" -ForegroundColor Red
    Write-Host "(or the managed form differs). The import did not land, whatever pac reported." -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------- steps

Write-Host ""
Write-Host "Verifying the plug-in steps the package declares..." -ForegroundColor Cyan
Write-Host ""

Push-Location $registration
try {
    & dotnet run --project . -- verifysteps $Environment $ZipFile
    $verifyExit = $LASTEXITCODE
}
finally {
    Pop-Location
}

Write-Host ""

if ($verifyExit -ne 0) {
    Write-Host "IMPORT NOT COMPLETE." -ForegroundColor Red
    Write-Host "One or more plug-in steps are missing or disabled, so the server-side rules are" -ForegroundColor Red
    Write-Host "not running. The screens will still look correct. Do not release this environment." -ForegroundColor Red
    exit 1
}

if ($importExit -ne 0) {
    Write-Host "Version and steps verified, but pac reported a non-zero exit. Read the importjob row before proceeding." -ForegroundColor Yellow
    exit $importExit
}

Write-Host "Import complete: $Environment holds $uniqueName $packageVersion and every step the package declares is enabled." -ForegroundColor Green
