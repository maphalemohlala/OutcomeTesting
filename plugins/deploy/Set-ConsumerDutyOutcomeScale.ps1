<#
.SYNOPSIS
    Moves the Consumer Duty questions Q-CD-01 to Q-CD-04 onto the outcome scale
    (120910014: Pass, Pass with issues, Insufficient evidence, Potential harm).

.DESCRIPTION
    Project owner, 2026-11-24. The retype is DATA, not solution: the option value travels with
    the OutcomeTesting solution (al_questionversion is a behaviour-0 root component), but which
    version each question answers on lives in al_questionversion rows, so every environment is
    retyped by running this after the solution carrying the value - and the assembly, portal
    templates and Code App that read it - is in place. Run it before then and the deployed
    response guard refuses the new type as unknown, and the portal draws the four questions as
    root causes.

    Each question is found by code, then its versions by _al_questionid_value (a library-made
    version is coded QV-<questionid>-vN, so a lookup by version code misses it). The version in
    force on the effective-from date is retired and succeeded through al_RetireAndSucceedQuestion
    with its own wording, mandatory flag and display order and only the response type changed.

    Re-runnable: a question already on 120910014 is skipped, and each call carries an
    idempotency key, so a repeated call returns the first one's result instead of a second
    version. Without -Apply it only reads and prints what it would do.

.PARAMETER OrgUrl
    The environment, e.g. https://org0b075da8.crm11.dynamics.com (DEV).

.PARAMETER EffectiveFrom
    The day the new scale is asked from, yyyy-MM-dd; today by default. It cannot be in the past.
    Open reviews keep their answers on the old version; the review page carries a value across
    only where the new scale offers it (Insufficient evidence carries, Yes and No are cleared).

.PARAMETER Apply
    Makes the change. Omit for a read-only dry run.

.EXAMPLE
    .\plugins\deploy\Set-ConsumerDutyOutcomeScale.ps1 -OrgUrl https://org0b075da8.crm11.dynamics.com
    .\plugins\deploy\Set-ConsumerDutyOutcomeScale.ps1 -OrgUrl https://org0b075da8.crm11.dynamics.com -Apply
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OrgUrl,
    [string]$EffectiveFrom = (Get-Date -Format 'yyyy-MM-dd'),
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$OutcomeScale = 120910014
$Codes = 'Q-CD-01', 'Q-CD-02', 'Q-CD-03', 'Q-CD-04'
$Project = Join-Path $PSScriptRoot '..\OutcomeTesting.Registration'
$env:DOTNET_ROLL_FORWARD = 'Major'

# One webapi call through the Registration tool (built: dotnet build -c Release). Its last
# output line is the JSON body; anything but HTTP 2xx stops the script.
function Invoke-WebApi([string]$Verb, [string]$Path, [string]$BodyFile) {
    $toolArgs = @('run', '--project', $Project, '-c', 'Release', '--no-build', '--', 'webapi', $OrgUrl, $Verb, $Path)
    if ($BodyFile) { $toolArgs += "@$BodyFile" }
    $out = & dotnet @toolArgs 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "webapi $Verb $Path failed:`n$($out -join "`n")" }
    $json = $out | Where-Object { $_.TrimStart().StartsWith('{') } | Select-Object -Last 1
    if ($json) { return $json | ConvertFrom-Json }
    return $null
}

function Get-Versions([string]$QuestionId) {
    $select = 'al_questionversioncode,al_responsetype,al_effectivefrom,al_effectiveto,al_ismandatory,al_versionnumber,al_displayorder,al_questiontext'
    (Invoke-WebApi GET "al_questionversions?`$select=$select&`$filter=_al_questionid_value eq $QuestionId&`$orderby=al_versionnumber").value
}

# In force on the day: from on or before it, and no end or an end after it (IsVersionEffective).
function Select-InForce($Versions, [string]$Day) {
    $Versions | Where-Object {
        ($_.al_effectivefrom -and $_.al_effectivefrom.Substring(0, 10) -le $Day) -and
        (-not $_.al_effectiveto -or $_.al_effectiveto.Substring(0, 10) -gt $Day)
    }
}

$filter = ($Codes | ForEach-Object { "al_questioncode eq '$_'" }) -join ' or '
$questions = (Invoke-WebApi GET "al_questions?`$select=al_questioncode,al_questionid&`$filter=$filter&`$orderby=al_questioncode").value
if (@($questions).Count -ne $Codes.Count) {
    throw "Expected $($Codes.Count) Consumer Duty questions, found $(@($questions).Count): $(($questions.al_questioncode) -join ', ')"
}

foreach ($q in $questions) {
    $versions = Get-Versions $q.al_questionid
    Write-Host "`n$($q.al_questioncode)  question $($q.al_questionid)"
    $versions | Format-Table al_questionversioncode, al_versionnumber, al_responsetype, al_effectivefrom, al_effectiveto, al_ismandatory, al_displayorder -AutoSize | Out-String | Write-Host

    $current = @(Select-InForce $versions $EffectiveFrom)
    if ($current.Count -ne 1) { throw "$($q.al_questioncode): $($current.Count) versions in force on $EffectiveFrom; expected one." }
    $current = $current[0]
    if ($current.al_responsetype -eq $OutcomeScale) {
        Write-Host "  already on the outcome scale ($($current.al_questionversioncode)); nothing to do."
        continue
    }

    Write-Host "  will succeed $($current.al_questionversioncode) (type $($current.al_responsetype)) with type $OutcomeScale from $EffectiveFrom"
    if (-not $Apply) { continue }

    $body = [ordered]@{
        QuestionId     = $q.al_questionid
        NewWording     = $current.al_questiontext
        ResponseType   = "$OutcomeScale"
        EffectiveFrom  = $EffectiveFrom
        IdempotencyKey = "cd-outcome-scale-$($q.al_questioncode)-$EffectiveFrom"
    }
    $file = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($file, ($body | ConvertTo-Json -Compress), (New-Object System.Text.UTF8Encoding($false)))
    try {
        $result = Invoke-WebApi POST 'al_RetireAndSucceedQuestion' $file
    } finally {
        Remove-Item $file -Force
    }
    Write-Host "  done: new version $($result.NewVersionId), v$($result.VersionNumber), audit $($result.AuditEventId)"
    Get-Versions $q.al_questionid | Format-Table al_questionversioncode, al_versionnumber, al_responsetype, al_effectivefrom, al_effectiveto -AutoSize | Out-String | Write-Host
}

if (-not $Apply) { Write-Host "`nDry run: nothing changed. Re-run with -Apply to make the change." }
