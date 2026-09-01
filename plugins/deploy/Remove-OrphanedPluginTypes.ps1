<#
.SYNOPSIS
    Removes plug-in types registered in a Dataverse environment that no longer
    exist in the OutcomeTesting.Plugins source, together with any Custom API
    bound to them.

.DESCRIPTION
    'pac plugin push' refuses to update an assembly when the environment has a
    registered plug-in type the new assembly does not contain. One orphan
    therefore blocks every plug-in update for the whole solution.

    Three orphans were found in Env_AQ_Dev on 2026-08-29, all prototypes that
    were deployed and then superseded without being cleaned up:

      SetPermissionRuleActivePlugin  -> al_SetPermissionRuleActive  (removed)
      SetRoleAssignmentActivePlugin  -> al_SetRoleAssignmentActive
      UpdateRolePlugin               -> al_UpdateRole

    None is referenced anywhere in the repository: not in app/src, not in
    plugins/, not in src/, not in knowledge/. The Code App invokes eleven
    Custom APIs, all as string literals, and none of these is among them.

    The script re-derives the orphan list at run time by comparing registered
    types against the *.cs classes on disk, so it will not delete a type whose
    source exists. It prints what it will do and requires -Confirm:$false or an
    interactive confirmation before deleting anything.

.PARAMETER OrgUrl
    Dataverse environment URL, e.g. https://<your-org>.crm<n>.dynamics.com

.PARAMETER AccessToken
    Bearer token for the Dataverse Web API of OrgUrl.

.PARAMETER PluginSourcePath
    Folder holding the plug-in .cs files. Defaults to ..\OutcomeTesting.Plugins.

.PARAMETER WhatIf
    List the orphans and exit without deleting.

.EXAMPLE
    $org = "https://org0b075da8.crm11.dynamics.com"
    $t = az account get-access-token --resource $org --query accessToken -o tsv
    .\Remove-OrphanedPluginTypes.ps1 -OrgUrl $org -AccessToken $t -WhatIf
    .\Remove-OrphanedPluginTypes.ps1 -OrgUrl $org -AccessToken $t

.NOTES
    Deletion order matters: request parameters and response properties, then
    the Custom API, then the plug-in type. Deleting the Custom API cascades its
    SDK message processing step, so the step is not deleted directly - doing so
    returns "Invalid plug-in registration stage".
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)][string]$OrgUrl,
    [Parameter(Mandatory = $true)][string]$AccessToken,
    [string]$PluginSourcePath,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'

if (-not $PluginSourcePath) {
    $PluginSourcePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'OutcomeTesting.Plugins'
}
if (-not (Test-Path -LiteralPath $PluginSourcePath)) {
    throw "Plug-in source folder not found: $PluginSourcePath"
}

$api = "$($OrgUrl.TrimEnd('/'))/api/data/v9.2"
$headers = @{
    Authorization      = "Bearer $AccessToken"
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Accept             = 'application/json'
    'Content-Type'     = 'application/json; charset=utf-8'
}

function Get-Dv { param([string]$Path) return Invoke-RestMethod -Method Get -Uri "$api/$Path" -Headers $headers }
function Remove-Dv { param([string]$Path) Invoke-RestMethod -Method Delete -Uri "$api/$Path" -Headers $headers | Out-Null }

# Classes on disk. PluginBase is the abstract base and is not a registered type.
$local = Get-ChildItem -LiteralPath $PluginSourcePath -Filter '*.cs' |
         ForEach-Object { $_.BaseName } |
         Where-Object { $_ -like '*Plugin' -and $_ -ne 'PluginBase' }

Write-Host "Source classes on disk: $($local.Count)"

$registered = (Get-Dv "plugintypes?`$filter=startswith(typename,'OutcomeTesting.Plugins')&`$select=plugintypeid,typename").value
Write-Host "Registered in environment: $($registered.Count)"

$orphans = @()
foreach ($r in $registered) {
    $short = $r.typename -replace '^OutcomeTesting\.Plugins\.', ''
    if ($local -notcontains $short) {
        $apis = (Get-Dv "customapis?`$filter=_plugintypeid_value eq $($r.plugintypeid)&`$select=customapiid,uniquename").value
        $orphans += [pscustomobject]@{
            TypeId   = $r.plugintypeid
            Name     = $short
            Apis     = $apis
        }
    }
}

if ($orphans.Count -eq 0) {
    Write-Host 'No orphaned plug-in types. Nothing to do.' -ForegroundColor Green
    return
}

Write-Host ''
Write-Host 'Orphans (registered here, no source on disk):' -ForegroundColor Yellow
foreach ($o in $orphans) {
    $names = if ($o.Apis.Count) { ($o.Apis | ForEach-Object { $_.uniquename }) -join ', ' } else { '(no custom api)' }
    Write-Host "  $($o.Name)  ->  $names"
}

if ($WhatIfOnly) {
    Write-Host ''
    Write-Host 'WhatIfOnly set. Nothing deleted.' -ForegroundColor Cyan
    return
}

Write-Host ''
foreach ($o in $orphans) {
    if (-not $PSCmdlet.ShouldProcess($o.Name, 'Delete plug-in type and its Custom API')) { continue }

    foreach ($c in $o.Apis) {
        foreach ($set in 'customapirequestparameters', 'customapiresponseproperties') {
            $idField = if ($set -like '*request*') { 'customapirequestparameterid' } else { 'customapiresponsepropertyid' }
            $rows = (Get-Dv "$set`?`$filter=_customapiid_value eq $($c.customapiid)&`$select=$idField").value
            foreach ($row in $rows) { Remove-Dv "$set($($row.$idField))" }
            Write-Host "  removed $($rows.Count) from $set"
        }
        Remove-Dv "customapis($($c.customapiid))"
        Write-Host "  deleted custom api: $($c.uniquename)"
    }

    Remove-Dv "plugintypes($($o.TypeId))"
    Write-Host "  deleted plug-in type: $($o.Name)" -ForegroundColor Green
}

Write-Host ''
$after = (Get-Dv "plugintypes?`$filter=startswith(typename,'OutcomeTesting.Plugins')&`$select=typename").value.Count
Write-Host "Registered types now: $after (source classes: $($local.Count))"
Write-Host 'Done. pac plugin push should now succeed.' -ForegroundColor Green
