<#
.SYNOPSIS
    Imports the OutcomeTesting solution and proves the plug-in steps are running.

.DESCRIPTION
    Audit finding 3, 2026-09-20.

    "The import succeeded" and "the rules are running" are different facts, and only the
    first one is reported. Dataverse imports an SdkMessageProcessingStep DISABLED unless
    activation is requested, and `pac solution import` requests it only with
    --activate-plugins.

    That is not a hypothetical. On 2026-09-02 an import without the flag switched off the
    six al_response steps in DEV, and nobody noticed for a week: the code was present, the
    tests passed, and only the step state was wrong
    (docs/deployment/2026-09-09-response-steps-reenabled.md). Those steps are where the
    server-side enforcement lives - the conditional root cause, the suitability grade rule,
    the answer guards - so a silent deactivation turns every rule into a suggestion while
    every screen still looks right.

    Two belts, because the cost of being wrong is silent:

      1. --activate-plugins is not a parameter of this script. It is always passed, and
         there is no switch to turn it off. Anyone who wants an import without it can call
         pac directly and own that decision.
      2. The import is not finished until `verifysteps` has confirmed every step the
         solution declares is present AND enabled in the target. A non-zero exit here means
         the environment is not safe to use, whatever the import said.

    The solution files themselves also now carry <StateCode>Enabled</StateCode>, re-applied
    after every export by Restore-StepState in round-trip-src.ps1. That is the third belt,
    and it is the one that would survive somebody bypassing this script.

.PARAMETER Environment
    The target environment URL or id. Required - there is deliberately no default, because
    the active pac profile points somewhere that is not always what the caller meant.

.PARAMETER ZipFile
    The solution package to import.

.PARAMETER Managed
    Import as managed. TEST and PROD take managed packages; DEV is the authoring
    environment and takes unmanaged.

.EXAMPLE
    ./scripts/Import-Solution.ps1 -Environment https://orgXXXX.crm11.dynamics.com/ -ZipFile OutcomeTesting.zip -Managed

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

if (-not (Test-Path (Join-Path $repoRoot 'src/SdkMessageProcessingSteps'))) {
    throw "src/SdkMessageProcessingSteps not found under $repoRoot. verifysteps reads the expected step list from there, so the import cannot be verified."
}

Write-Host "Importing $ZipFile into $Environment" -ForegroundColor Cyan
Write-Host "  --activate-plugins is always passed. See the notes in this script." -ForegroundColor DarkGray
Write-Host ""

$importArgs = @(
    'solution', 'import',
    '--path', $ZipFile,
    '--environment', $Environment,
    '--activate-plugins',
    '--publish-changes'
)

# A silent pac CLI does not mean nothing happened - the job runs server-side. The step
# verification below is what actually settles whether the import did its work.
& pac @importArgs
$importExit = $LASTEXITCODE

if ($importExit -ne 0) {
    Write-Host ""
    Write-Host "pac solution import exited $importExit." -ForegroundColor Yellow
    Write-Host "Check the importjob table before assuming nothing landed; the job runs server-side." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Verifying plug-in steps..." -ForegroundColor Cyan
Write-Host ""

Push-Location $registration
try {
    & dotnet run --project . -- verifysteps $Environment (Join-Path $repoRoot 'src/SdkMessageProcessingSteps')
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
    Write-Host "Steps verified, but pac reported a non-zero exit. Read the importjob row before proceeding." -ForegroundColor Yellow
    exit $importExit
}

Write-Host "Import complete and every step verified enabled." -ForegroundColor Green
