<#
.SYNOPSIS
    Builds the portal stylesheet the site serves from its commented source.

.DESCRIPTION
    powerpages\outcome-testing.src.css is the stylesheet people edit. Close to half of it is
    comment - the reasons behind the rules - and none of that is any use to a browser, so
    the web file the site serves is a copy with the comments taken out:

        powerpages\outcome-testing.src.css                      edit this
        <site>\web-files\outcome-testing.css                    built, never edited

    The source sits outside the site folder on purpose: `pac pages upload` sends everything
    under web-files, and a second stylesheet there would be uploaded as a web file.

    Only comments and the lines they leave empty are removed. Every rule reaches the built
    file byte for byte, spacing included, because app tests read the built file and match
    rules as they are written. A "/*" inside a quoted string or a url( ) is not a comment
    and is left alone.

    -Check builds in memory and compares, changing nothing. It fails when the web file is
    not what the source builds to - the source was edited and not rebuilt, or the web file
    was edited by hand. Deploy-Portal.ps1 runs it as a gate.

    After a rebuild, bump the ?v= on the stylesheet link in OT Layout, as that template's
    own comment asks for every change to the file.

.EXAMPLE
    powershell -NoProfile -File .\powerpages\Build-PortalCss.ps1

.EXAMPLE
    powershell -NoProfile -File .\powerpages\Build-PortalCss.ps1 -Check
#>

[CmdletBinding()]
param(
    # The site folder - the one passed to `pac pages upload`.
    [string]$SitePath,
    [string]$SourcePath,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# Resolved here rather than as parameter defaults, for the reason the Check-*.ps1 scripts
# give: $PSScriptRoot is not reliably populated while defaults are bound under 5.1.
$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $SitePath) { $SitePath = Join-Path $root 'outcome-testing---outcometesting' }
if (-not $SourcePath) { $SourcePath = Join-Path $root 'outcome-testing.src.css' }
$targetPath = Join-Path $SitePath 'web-files\outcome-testing.css'

if (-not (Test-Path -LiteralPath $SourcePath)) {
    Write-Host "Stylesheet source not found: $SourcePath" -ForegroundColor Red
    exit 2
}

$banner = '/* Built from powerpages/outcome-testing.src.css by Build-PortalCss.ps1. Edit the source, not this file. */'

function Remove-CssComments {
    param([string]$Css)

    # A marker left where each comment stood, so the line it was on can be told from a line
    # nobody touched. U+0000 cannot occur in a stylesheet.
    $mark = [char]0
    $out = New-Object System.Text.StringBuilder
    $i = 0
    $n = $Css.Length

    while ($i -lt $n) {
        $c = $Css[$i]

        if ($c -eq '/' -and $i + 1 -lt $n -and $Css[$i + 1] -eq '*') {
            $end = $Css.IndexOf('*/', $i + 2)
            if ($end -lt 0) { throw "Unclosed comment starting at character $i." }
            # Removing a comment with text hard against both sides would join two tokens
            # (`0/**/auto` -> `0auto`), so refuse rather than change a rule.
            if ($i -gt 0 -and $end + 2 -lt $n -and -not [char]::IsWhiteSpace($Css[$i - 1]) -and -not [char]::IsWhiteSpace($Css[$end + 2])) {
                throw "Comment at character $i has no space on either side; removing it would join two tokens."
            }
            [void]$out.Append($mark)
            $i = $end + 2
            continue
        }

        if ($c -eq '"' -or $c -eq "'") {
            $j = $i + 1
            while ($j -lt $n -and $Css[$j] -ne $c) {
                if ($Css[$j] -eq '\') { $j++ }
                $j++
            }
            if ($j -ge $n) { throw "Unclosed string starting at character $i." }
            [void]$out.Append($Css, $i, $j - $i + 1)
            $i = $j + 1
            continue
        }

        # url( ... ) with no quotes: everything up to the closing bracket is the address.
        if (($c -eq 'u' -or $c -eq 'U') -and $i + 4 -le $n -and $Css.Substring($i, 4) -ieq 'url(') {
            $j = $i + 4
            while ($j -lt $n -and [char]::IsWhiteSpace($Css[$j])) { $j++ }
            if ($j -lt $n -and $Css[$j] -ne '"' -and $Css[$j] -ne "'") {
                $close = $Css.IndexOf(')', $j)
                if ($close -lt 0) { throw "Unclosed url( starting at character $i." }
                [void]$out.Append($Css, $i, $close - $i + 1)
                $i = $close + 1
                continue
            }
        }

        [void]$out.Append($c)
        $i++
    }

    # A line a comment was taken from loses the space the comment left behind; a line that
    # is now empty goes altogether, as does one that was empty to begin with.
    $newline = if ($Css.Contains("`r`n")) { "`r`n" } else { "`n" }
    $kept = New-Object System.Collections.Generic.List[string]
    foreach ($line in ($out.ToString() -split "`r?`n")) {
        if ($line.IndexOf($mark) -ge 0) { $line = $line.Replace([string]$mark, '').TrimEnd() }
        if ($line.Trim().Length -eq 0) { continue }
        $kept.Add($line)
    }

    return ($kept -join $newline) + $newline
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$source = [IO.File]::ReadAllText($SourcePath, $utf8)
$newline = if ($source.Contains("`r`n")) { "`r`n" } else { "`n" }
$built = $banner + $newline + (Remove-CssComments $source)

if ($Check) {
    $current = if (Test-Path -LiteralPath $targetPath) { [IO.File]::ReadAllText($targetPath, $utf8) } else { '' }
    if ($current -ceq $built) {
        Write-Host "Portal stylesheet is up to date with its source ($($built.Length) characters built from $($source.Length))."
        exit 0
    }
    Write-Host 'Portal stylesheet is STALE:' -ForegroundColor Red
    Write-Host "  $targetPath"
    Write-Host "  is not what $SourcePath builds to."
    Write-Host '  Edit the source, run powerpages\Build-PortalCss.ps1, and bump ?v= in OT Layout.'
    exit 1
}

[IO.File]::WriteAllText($targetPath, $built, $utf8)
Write-Host "Built $targetPath"
Write-Host "  $($source.Length) characters of source -> $($built.Length) characters served."
exit 0
