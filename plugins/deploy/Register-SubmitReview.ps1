<#
.SYNOPSIS
    Deploys the SubmitReview server-side command (AD-003): registers the
    plug-in assembly and creates the al_SubmitReview Custom API with its
    request parameters and response properties.

.DESCRIPTION
    Run this AFTER the al_AuditEvent, al_ReviewInstance, al_Response, al_QuestionVersion,
    al_Question and al_Section tables are imported (the plug-in reads or writes all of them)
    and in coordination with anyone else changing the solution. Steps:
      1. pac plugin push  - registers OutcomeTesting.Plugins into the solution.
      2. Web API calls     - create the Custom API, its 3 request parameters and
                             3 response properties, bound to the plug-in type, all
                             inside the OutcomeTesting solution.

    The Custom API contract is read from ..\customapi\al_SubmitReview.customapi.json
    so the definition lives in one place.

.PARAMETER OrgUrl
    The Dataverse environment URL to deploy to, e.g. https://<your-org>.crm<n>.dynamics.com
    Pass the environment you intend to target; no environment is assumed or defaulted.

.PARAMETER AccessToken
    A bearer token for the Dataverse Web API of OrgUrl. Obtain one however your
    environment allows (for example an Azure CLI or MSAL token for the org).

.PARAMETER SolutionUniqueName
    Solution the components are added to. Defaults to OutcomeTesting.

.NOTES
    Idempotent: existing components are looked up and reused rather than duplicated.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OrgUrl,
    [Parameter(Mandatory = $true)][string]$AccessToken,
    [string]$SolutionUniqueName = 'OutcomeTesting'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$contractPath = Join-Path $root 'customapi\al_SubmitReview.customapi.json'
$pluginProject = Join-Path $root 'OutcomeTesting.Plugins'
$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json

$api = "$($OrgUrl.TrimEnd('/'))/api/data/v9.2"
$headers = @{
    Authorization              = "Bearer $AccessToken"
    'OData-MaxVersion'         = '4.0'
    'OData-Version'            = '4.0'
    Accept                     = 'application/json'
    'Content-Type'             = 'application/json; charset=utf-8'
    'MSCRM.SolutionUniqueName'  = $SolutionUniqueName
}

function Invoke-Dv {
    param([string]$Method, [string]$Path, [object]$Body)
    $uri = "$api/$Path"
    $json = if ($Body) { $Body | ConvertTo-Json -Depth 6 } else { $null }
    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -Body $json
}

# $IdField is passed in rather than derived from $Set. The primary-key column comes from
# the entity's LOGICAL name, not its entity-set name, and the two differ by more than a
# trailing 's' (customapiresponseproperties -> customapiresponsepropertyid), so deriving
# it produces column names Dataverse rejects with HTTP 400.
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

Write-Host '1. Pushing plug-in assembly (pac plugin push)...'
$settingsFile = Join-Path $pluginProject 'spkl.json'
if (-not (Test-Path -LiteralPath $settingsFile)) {
    # Fail here rather than at step 2. Without this file the push cannot run, and the
    # only symptom downstream is "Plug-in type not found", which points at the wrong cause.
    throw "Plug-in push settings file not found: $settingsFile. Create it, or push the assembly manually with 'pac plugin push' from $pluginProject and re-run this script (the Web API steps are idempotent)."
}
Push-Location $pluginProject
try {
    # stderr is NOT discarded: when the push fails, its own message is the only thing that
    # explains why, and step 2 can only report the symptom.
    pac plugin push --settingsFile $settingsFile
    if ($LASTEXITCODE -ne 0) {
        throw "pac plugin push failed with exit code $LASTEXITCODE (see its output above). Fix the push, then re-run this script; the Web API steps below are idempotent."
    }
}
finally { Pop-Location }

Write-Host '2. Resolving plug-in type id...'
$typeName = $contract.customApi.pluginType
$pt = Invoke-Dv -Method Get -Path "plugintypes`?`$filter=typename eq '$typeName'&`$select=plugintypeid"
if ($pt.value.Count -eq 0) { throw "Plug-in type '$typeName' not found. Ensure pac plugin push succeeded." }
$pluginTypeId = $pt.value[0].plugintypeid

Write-Host '3. Creating Custom API...'
$c = $contract.customApi
$apiBody = [ordered]@{
    uniquename                       = $c.uniquename
    name                             = $c.name
    displayname                      = $c.displayname
    description                      = $c.description
    bindingtype                      = $c.bindingtype
    isfunction                       = $c.isfunction
    isprivate                        = $c.isprivate
    allowedcustomprocessingsteptype  = $c.allowedcustomprocessingsteptype
    'PluginTypeId@odata.bind'        = "/plugintypes($pluginTypeId)"
}
$customApiId = Get-OrCreate -Set 'customapis' -IdField 'customapiid' -Filter "uniquename eq '$($c.uniquename)'" -Body $apiBody

Write-Host '4. Creating request parameters...'
foreach ($p in $contract.requestParameters) {
    $body = [ordered]@{
        uniquename                = $p.uniquename
        name                      = $p.name
        displayname               = $p.displayname
        description               = $p.description
        type                      = $p.type
        isoptional                = $p.isoptional
        'CustomAPIId@odata.bind'  = "/customapis($customApiId)"
    }
    Get-OrCreate -Set 'customapirequestparameters' -IdField 'customapirequestparameterid' -Filter "uniquename eq '$($p.uniquename)' and _customapiid_value eq $customApiId" -Body $body | Out-Null
}

Write-Host '5. Creating response properties...'
foreach ($p in $contract.responseProperties) {
    $body = [ordered]@{
        uniquename                = $p.uniquename
        name                      = $p.name
        displayname               = $p.displayname
        description               = $p.description
        type                      = $p.type
        'CustomAPIId@odata.bind'  = "/customapis($customApiId)"
    }
    Get-OrCreate -Set 'customapiresponseproperties' -IdField 'customapiresponsepropertyid' -Filter "uniquename eq '$($p.uniquename)' and _customapiid_value eq $customApiId" -Body $body | Out-Null
}

Write-Host "Done. al_SubmitReview is deployed to solution '$SolutionUniqueName'." -ForegroundColor Green
