<#
.SYNOPSIS
    Registers the ResponseGuard and ResponseProgress plug-in steps (AD-053).

.DESCRIPTION
    These are SDK message processing steps, not Custom APIs, so nothing here touches
    customapis. Steps:
      1. pac plugin push  - refreshes the assembly.
      2. plugintype rows  - created explicitly; pac plugin push does not create them
                            for classes the environment has not seen (AD-052).
      3. sdkmessageprocessingstep rows:
           ResponseGuardPlugin    Create/Update  al_response  stage 20 (pre-op)  sync
           ResponseGuardPlugin    Associate/Disassociate      stage 20           sync
           ResponseProgressPlugin Create/Update  al_response  stage 40 (post-op) sync
      4. Pre-images on the Update steps, so the guard sees columns the Target omits.

    Idempotent: existing components are looked up and reused rather than duplicated.

.PARAMETER OrgUrl
    The Dataverse environment URL to deploy to. No environment is assumed or defaulted.

.PARAMETER AccessToken
    A bearer token for the Dataverse Web API of OrgUrl.

.PARAMETER PluginAssemblyId
    Id of the existing OutcomeTestingPlugins assembly registered in the environment.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OrgUrl,
    [Parameter(Mandatory = $true)][string]$AccessToken,
    [string]$SolutionUniqueName = 'OutcomeTesting',
    [string]$PluginAssemblyId = '7b51d0d1-f5a1-f111-b8dd-e4fade069307',
    [string]$PacPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$pluginProject = Join-Path $root 'OutcomeTesting.Plugins'

$api = "$($OrgUrl.TrimEnd('/'))/api/data/v9.2"
$headers = @{
    Authorization              = "Bearer $AccessToken"
    'OData-MaxVersion'         = '4.0'
    'OData-Version'            = '4.0'
    Accept                     = 'application/json'
    'Content-Type'             = 'application/json; charset=utf-8'
    'MSCRM.SolutionUniqueName' = $SolutionUniqueName
}

function Invoke-Dv {
    param([string]$Method, [string]$Path, [object]$Body)
    $json = if ($Body) { $Body | ConvertTo-Json -Depth 6 } else { $null }
    return Invoke-RestMethod -Method $Method -Uri "$api/$Path" -Headers $headers -Body $json
}

function Get-OrCreate {
    param([string]$Set, [string]$IdField, [string]$Filter, [object]$Body)
    $existing = Invoke-Dv -Method Get -Path "$Set`?`$filter=$Filter&`$select=$IdField"
    if ($existing.value.Count -gt 0) {
        Write-Host "  exists: $Filter"
        return $existing.value[0].$IdField
    }
    $resp = Invoke-RestMethod -Method Post -Uri "$api/$Set" -Headers ($headers + @{ Prefer = 'return=representation' }) -Body ($Body | ConvertTo-Json -Depth 6)
    Write-Host "  created: $Filter"
    return $resp.$IdField
}

Write-Host '0. Resolving PAC CLI...'
if (-not $PacPath) {
    $onPath = Get-Command pac -ErrorAction SilentlyContinue
    if ($onPath) {
        $PacPath = $onPath.Source
    }
    else {
        $found = Get-ChildItem -Path (Join-Path $env:LOCALAPPDATA 'Microsoft\PowerAppsCLI') -Filter 'pac.exe' -Recurse -ErrorAction SilentlyContinue |
                 Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { $PacPath = $found.FullName }
    }
}
if (-not $PacPath -or -not (Test-Path -LiteralPath $PacPath)) {
    throw "PAC CLI not found. Pass -PacPath with the full path to pac.exe, or add it to PATH."
}
Write-Host "  using: $PacPath"

Write-Host '1. Pushing plug-in assembly...'
$dll = Join-Path $pluginProject 'bin\Debug\net462\OutcomeTesting.Plugins.dll'
if (-not (Test-Path -LiteralPath $dll)) {
    throw "Plug-in assembly not found: $dll. Run 'dotnet build' in $pluginProject first."
}
& $PacPath plugin push --pluginId $PluginAssemblyId --pluginFile $dll --type Assembly
if ($LASTEXITCODE -ne 0) {
    throw "pac plugin push failed with exit code $LASTEXITCODE. Fix the push, then re-run; the Web API steps below are idempotent."
}

function Resolve-PluginType {
    param([string]$TypeName)
    $pt = Invoke-Dv -Method Get -Path "plugintypes`?`$filter=typename eq '$TypeName'&`$select=plugintypeid"
    if ($pt.value.Count -gt 0) {
        Write-Host "  exists: $TypeName"
        return $pt.value[0].plugintypeid
    }
    $shortName = $TypeName -replace '^.*\.', ''
    $body = [ordered]@{
        typename                      = $TypeName
        name                          = $TypeName
        friendlyname                  = $shortName
        'pluginassemblyid@odata.bind' = "/pluginassemblies($PluginAssemblyId)"
    }
    $created = Invoke-RestMethod -Method Post -Uri "$api/plugintypes" -Headers ($headers + @{ Prefer = 'return=representation' }) -Body ($body | ConvertTo-Json -Depth 4)
    Write-Host "  created: $TypeName"
    return $created.plugintypeid
}

Write-Host '2. Resolving plug-in types...'
$guardTypeId = Resolve-PluginType -TypeName 'OutcomeTesting.Plugins.ResponseGuardPlugin'
$progressTypeId = Resolve-PluginType -TypeName 'OutcomeTesting.Plugins.ResponseProgressPlugin'

function Get-MessageId {
    param([string]$Name)
    $m = Invoke-Dv -Method Get -Path "sdkmessages`?`$filter=name eq '$Name'&`$select=sdkmessageid"
    if ($m.value.Count -eq 0) { throw "SDK message '$Name' not found." }
    return $m.value[0].sdkmessageid
}

function Get-MessageFilterId {
    param([string]$MessageId, [string]$Entity)
    $f = Invoke-Dv -Method Get -Path "sdkmessagefilters`?`$filter=_sdkmessageid_value eq $MessageId and primaryobjecttypecode eq '$Entity'&`$select=sdkmessagefilterid"
    if ($f.value.Count -eq 0) { throw "No message filter for '$Entity' on that message." }
    return $f.value[0].sdkmessagefilterid
}

# stage 20 = pre-operation, 40 = post-operation; mode 0 = synchronous.
function New-Step {
    param(
        [string]$Name, [string]$PluginTypeId, [string]$Message,
        [string]$Entity, [int]$Stage, [string]$FilteringAttributes
    )
    $messageId = Get-MessageId -Name $Message
    $body = [ordered]@{
        name                      = $Name
        stage                     = $Stage
        mode                      = 0
        rank                      = 1
        supporteddeployment       = 0
        invocationsource          = 0
        'sdkmessageid@odata.bind' = "/sdkmessages($messageId)"
        'plugintypeid@odata.bind' = "/plugintypes($PluginTypeId)"
    }
    if ($Entity) {
        $filterId = Get-MessageFilterId -MessageId $messageId -Entity $Entity
        $body['sdkmessagefilterid@odata.bind'] = "/sdkmessagefilters($filterId)"
    }
    if ($FilteringAttributes) {
        $body['filteringattributes'] = $FilteringAttributes
    }
    return Get-OrCreate -Set 'sdkmessageprocessingsteps' -IdField 'sdkmessageprocessingstepid' -Filter "name eq '$Name'" -Body $body
}

Write-Host '3. Registering steps...'
$answerColumns = 'al_answertext,al_answerchoice,al_answerchoices,al_answerdate'

New-Step -Name 'ResponseGuard: Create of al_response' -PluginTypeId $guardTypeId -Message 'Create' -Entity 'al_response' -Stage 20 | Out-Null
$guardUpdate = New-Step -Name 'ResponseGuard: Update of al_response' -PluginTypeId $guardTypeId -Message 'Update' -Entity 'al_response' -Stage 20 -FilteringAttributes $answerColumns

# Associate and Disassociate carry no per-entity message filter, so these register
# unfiltered and the plug-in checks the relationship name itself.
New-Step -Name 'ResponseGuard: Associate fail reason' -PluginTypeId $guardTypeId -Message 'Associate' -Stage 20 | Out-Null
New-Step -Name 'ResponseGuard: Disassociate fail reason' -PluginTypeId $guardTypeId -Message 'Disassociate' -Stage 20 | Out-Null

New-Step -Name 'ResponseProgress: Create of al_response' -PluginTypeId $progressTypeId -Message 'Create' -Entity 'al_response' -Stage 40 | Out-Null
$progressUpdate = New-Step -Name 'ResponseProgress: Update of al_response' -PluginTypeId $progressTypeId -Message 'Update' -Entity 'al_response' -Stage 40 -FilteringAttributes $answerColumns

Write-Host '4. Registering pre-images on the Update steps...'
# imagetype 0 = pre-image. The guard needs the review and question links on Update,
# which the Target omits because only changed columns travel in it.
foreach ($step in @(
    @{ Id = $guardUpdate;    Name = 'ResponseGuard Update pre-image' },
    @{ Id = $progressUpdate; Name = 'ResponseProgress Update pre-image' }
)) {
    $body = [ordered]@{
        name                                    = $step.Name
        entityalias                             = 'PreImage'
        imagetype                               = 0
        attributes                              = "al_reviewinstanceid,al_questionversionid,$answerColumns"
        'sdkmessageprocessingstepid@odata.bind' = "/sdkmessageprocessingsteps($($step.Id))"
    }
    Get-OrCreate -Set 'sdkmessageprocessingstepimages' -IdField 'sdkmessageprocessingstepimageid' -Filter "name eq '$($step.Name)'" -Body $body | Out-Null
}

Write-Host "Done. ResponseGuard and ResponseProgress are registered in '$SolutionUniqueName'." -ForegroundColor Green
