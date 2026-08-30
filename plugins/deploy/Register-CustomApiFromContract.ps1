<#
.SYNOPSIS
    Creates the plug-in type and the Custom API for any command described by a
    *.customapi.json contract, and adds both to the solution.

.DESCRIPTION
    This is the generic form of the Web API half of Register-CompleteRemediation.ps1.
    Every field it writes is read from the contract, so nothing is hand-typed here and
    a new command needs a contract file rather than a new script.

    It deliberately does NOT push the plug-in assembly. `pac plugin push` is a separate,
    ordered step (see docs/deployment/), and folding it in here would let a caller
    register a command whose assembly is older than the contract it was built from.

    Run it AFTER `pac plugin push` has landed the assembly containing the plug-in type.

    Steps:
      1. Resolve or create the plug-in type. `pac plugin push` does not create plug-in
         type rows for classes the environment has not seen before (AD-052), so a
         first-time command needs this even though the push reported success.
      2. Create the Custom API, its request parameters and its response properties.

.PARAMETER OrgUrl
    The Dataverse environment URL to deploy to, e.g. https://<your-org>.crm<n>.dynamics.com
    Pass the environment you intend to target; no environment is assumed or defaulted.

.PARAMETER AccessToken
    A bearer token for the Dataverse Web API of OrgUrl. Obtain one however your
    environment allows (for example an Azure CLI or MSAL token for the org).

.PARAMETER ContractFile
    Path to the *.customapi.json contract. Relative paths resolve against plugins\customapi.

.PARAMETER PluginAssemblyId
    Assembly the plug-in type belongs to. Defaults to the OutcomeTesting.Plugins assembly.

.PARAMETER SolutionUniqueName
    Solution the components are added to. Defaults to OutcomeTesting.

.EXAMPLE
    $org = 'https://<your-org>.crm<n>.dynamics.com'
    $t = az account get-access-token --resource $org --query accessToken -o tsv
    .\Register-CustomApiFromContract.ps1 -OrgUrl $org -AccessToken $t -ContractFile al_SetFailAccountability.customapi.json

.NOTES
    Idempotent: existing components are looked up and reused rather than duplicated.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OrgUrl,
    [Parameter(Mandatory = $true)][string]$AccessToken,
    [Parameter(Mandatory = $true)][string]$ContractFile,
    [string]$PluginAssemblyId = '7b51d0d1-f5a1-f111-b8dd-e4fade069307',
    [string]$SolutionUniqueName = 'OutcomeTesting'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$contractPath = if ([System.IO.Path]::IsPathRooted($ContractFile)) {
    $ContractFile
} else {
    Join-Path (Join-Path $root 'customapi') $ContractFile
}
if (-not (Test-Path -LiteralPath $contractPath)) {
    throw "Contract file not found: $contractPath"
}
$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json

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

Write-Host "1. Resolving plug-in type..."
$typeName = $contract.customApi.pluginType
$pluginTypeId = Get-OrCreate -Set 'plugintypes' -IdField 'plugintypeid' `
    -Filter "typename eq '$typeName'" `
    -Body ([ordered]@{
        typename                      = $typeName
        name                          = $typeName
        friendlyname                  = ($typeName -replace '^.*\.', '')
        'pluginassemblyid@odata.bind' = "/pluginassemblies($PluginAssemblyId)"
    })

Write-Host '2. Creating Custom API...'
$c = $contract.customApi
$apiBody = [ordered]@{
    uniquename                      = $c.uniquename
    name                            = $c.name
    displayname                     = $c.displayname
    description                     = $c.description
    bindingtype                     = $c.bindingtype
    isfunction                      = $c.isfunction
    isprivate                       = $c.isprivate
    allowedcustomprocessingsteptype = $c.allowedcustomprocessingsteptype
    'PluginTypeId@odata.bind'       = "/plugintypes($pluginTypeId)"
}
$customApiId = Get-OrCreate -Set 'customapis' -IdField 'customapiid' -Filter "uniquename eq '$($c.uniquename)'" -Body $apiBody

Write-Host '3. Creating request parameters...'
foreach ($p in $contract.requestParameters) {
    $body = [ordered]@{
        uniquename               = $p.uniquename
        name                     = $p.name
        displayname              = $p.displayname
        description              = $p.description
        type                     = $p.type
        isoptional               = $p.isoptional
        'CustomAPIId@odata.bind' = "/customapis($customApiId)"
    }
    Get-OrCreate -Set 'customapirequestparameters' -IdField 'customapirequestparameterid' -Filter "uniquename eq '$($p.uniquename)' and _customapiid_value eq $customApiId" -Body $body | Out-Null
}

Write-Host '4. Creating response properties...'
foreach ($p in $contract.responseProperties) {
    $body = [ordered]@{
        uniquename               = $p.uniquename
        name                     = $p.name
        displayname              = $p.displayname
        description              = $p.description
        type                     = $p.type
        'CustomAPIId@odata.bind' = "/customapis($customApiId)"
    }
    Get-OrCreate -Set 'customapiresponseproperties' -IdField 'customapiresponsepropertyid' -Filter "uniquename eq '$($p.uniquename)' and _customapiid_value eq $customApiId" -Body $body | Out-Null
}

Write-Host "Done. $($c.uniquename) is deployed to solution '$SolutionUniqueName'." -ForegroundColor Green
