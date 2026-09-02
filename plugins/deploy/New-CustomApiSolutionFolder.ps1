# Generates one src/customapis/<name> solution folder from its contract JSON plus the
# plug-in type id the environment actually holds. The id is a parameter, not a table, so
# it cannot go stale the way artifacts/gen-customapis.ps1 did (AD-071).
param(
    [Parameter(Mandatory = $true)][string] $ApiName,
    [Parameter(Mandatory = $true)][string] $PluginTypeId
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$apiDir = Join-Path $root "src\customapis\$ApiName"
$contract = Join-Path $root "plugins\customapi\$ApiName.customapi.json"

$json = Get-Content -LiteralPath $contract -Raw | ConvertFrom-Json
$api = $json.customApi

$enc = New-Object System.Text.UTF8Encoding($false)
function Write-Xml($path, $content) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [System.IO.File]::WriteAllText($path, ($content -replace "`r?`n", "`r`n"), $enc)
}
function Esc($s) { [System.Security.SecurityElement]::Escape([string]$s) }

$desc = Esc $api.description
$disp = Esc $api.displayname

# Canonical export shape: no XML declaration, plugintypeexportkey == plugintypeid.
Write-Xml (Join-Path $apiDir 'customapi.xml') @"
<customapi uniquename="$($api.uniquename)">
  <allowedcustomprocessingsteptype>0</allowedcustomprocessingsteptype>
  <bindingtype>0</bindingtype>
  <description default="$desc">
    <label description="$desc" languagecode="1033" />
  </description>
  <displayname default="$disp">
    <label description="$disp" languagecode="1033" />
  </displayname>
  <iscustomizable>1</iscustomizable>
  <isfunction>0</isfunction>
  <isprivate>0</isprivate>
  <name>$($api.name)</name>
  <plugintypeid>
    <plugintypeexportkey>$PluginTypeId</plugintypeexportkey>
  </plugintypeid>
  <workflowsdkstepenabled>0</workflowsdkstepenabled>
</customapi>
"@

foreach ($p in $json.requestParameters) {
    $pd = Esc $p.description
    $pn = Esc $p.displayname
    $opt = if ($p.isoptional) { 1 } else { 0 }
    Write-Xml (Join-Path $apiDir "customapirequestparameters\$($p.uniquename)\customapirequestparameter.xml") @"
<customapirequestparameter uniquename="$($p.uniquename)">
  <description default="$pd">
    <label description="$pd" languagecode="1033" />
  </description>
  <displayname default="$pn">
    <label description="$pn" languagecode="1033" />
  </displayname>
  <iscustomizable>1</iscustomizable>
  <isoptional>$opt</isoptional>
  <name>$($api.uniquename).$($p.uniquename)</name>
  <type>$($p.type)</type>
</customapirequestparameter>
"@
}

foreach ($r in $json.responseProperties) {
    $rd = Esc $r.description
    $rn = Esc $r.displayname
    Write-Xml (Join-Path $apiDir "customapiresponseproperties\$($r.uniquename)\customapiresponseproperty.xml") @"
<customapiresponseproperty uniquename="$($r.uniquename)">
  <description default="$rd">
    <label description="$rd" languagecode="1033" />
  </description>
  <displayname default="$rn">
    <label description="$rn" languagecode="1033" />
  </displayname>
  <iscustomizable>1</iscustomizable>
  <name>$($api.uniquename).$($r.uniquename)</name>
  <type>$($r.type)</type>
</customapiresponseproperty>
"@
}

Write-Host "Generated src/customapis/$ApiName bound to plug-in type $PluginTypeId."
