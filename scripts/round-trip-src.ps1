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

    <#
    .SYNOPSIS
        Re-applies <StateCode>Enabled</StateCode> to every exported step file.

    .DESCRIPTION
        Audit finding 3, 2026-09-20.

        Dataverse imports an SdkMessageProcessingStep DISABLED unless activation is
        requested, and `pac solution import` requests it only with --activate-plugins. An
        import that forgets the flag switches the rules off silently. That is not
        hypothetical: it happened on 2026-09-02 to the six al_response steps and went
        unnoticed until 2026-09-09. Those steps are where the server-side enforcement lives,
        so a silent deactivation turns every rule into a suggestion.

        The fix is to say Enabled in the solution files. The problem is that the EXPORT does
        not emit StateCode, and this script's whole contract is that src/ mirrors the export
        (AD-013) - so the next round trip would strip all 21 stamps and say nothing.

        Hence this: the one deliberate, declared divergence from "mirrors the export". It is
        idempotent, it only ever adds, and it reports what it touched so the divergence is
        visible in the run rather than buried in a diff.

        Enabled is not an assumption. All 52 OutcomeTesting.Plugins steps in DEV were
        confirmed statecode 0 before the stamp was introduced. If a step is ever DELIBERATELY
        disabled, this will fight that decision - which is why it prints a line per file.
    #>
    function Restore-StepState([string]$StepFolder) {
        if (-not (Test-Path $StepFolder)) { return }

        $anchor = '  <SdkMessageProcessingStepImages'
        $stamp = @(
            '  <!-- Stamped 2026-09-20, audit finding 3. Dataverse imports a step DISABLED'
            '       unless activation is requested, and an import that forgot the'
            '       activate-plugins flag switched these off once already (2026-09-02, six'
            '       al_response steps, unnoticed for a week). The export does not emit these'
            '       two elements, so round-trip-src.ps1 re-applies them after every export;'
            '       see Restore-StepState there. Do not remove either without reading that. -->'
            '  <StateCode>Enabled</StateCode>'
            '  <StatusCode>Enabled</StatusCode>'
        ) -join "`n"

        $restored = 0
        $already = 0
        foreach ($file in Get-ChildItem -Path $StepFolder -Filter '*.xml' -File) {
            $text = Get-Content -Path $file.FullName -Raw
            if ($text -match '<StateCode>') { $already++; continue }

            $index = $text.IndexOf($anchor)
            if ($index -lt 0) {
                Write-Host "  ! $($file.Name) has no images element; state not stamped." -ForegroundColor Yellow
                continue
            }

            $text = $text.Insert($index, $stamp + "`n")
            Set-Content -Path $file.FullName -Value $text -NoNewline -Encoding UTF8
            $restored++
        }

        Write-Host ""
        if ($restored -gt 0) {
            Write-Host "Step state re-stamped on $restored file(s); $already already carried it." -ForegroundColor Cyan
        }
        else {
            Write-Host "Step state: all $already file(s) already Enabled." -ForegroundColor Cyan
        }
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

    Restore-StepState -StepFolder (Join-Path $srcPath 'SdkMessageProcessingSteps')

    Write-Host ""
    Write-Host "src/ now mirrors the export exactly, plus the step-state stamp." -ForegroundColor Green
}
finally {
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
}
