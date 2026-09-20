<#
.SYNOPSIS
    Static checks on the portal's Liquid templates (audit finding 8).

.DESCRIPTION
    The solution had 88 plug-in test files and 43 app test files and not one covering a
    Liquid template, while real logic lives in them. This is the first of those checks, and
    it exists because of a specific defect rather than for coverage:

    Audit finding 14. `OT Tax Notes` fetched question `Q-FQTAX-03` and read
    `al_answerrichtext` off it. Q-FQTAX-03 is "Remedial action required?", a mandatory
    Yes/No that stores its answer in `al_answerchoice` and can never carry markup, so the
    panel asked for a column that question never fills. The Tax checker's remedial text
    reached neither the AQS checker nor the adviser - and it drew NOTHING rather than
    failing, because an empty answer is exactly how that template is told there is nothing
    to show. It survived a code review, a deployment and an audit.

    So the assertions here are about the pairing of a question with the column a template
    reads its answer from:

      A  Every question code a template names exists in knowledge/checklist-v8.md.
      B  Every question code a template names has at least one answer column selected in
         the same fetch that can actually hold its answer.
      C  Every answer column a fetch selects is one that at least one of the question codes
         in that fetch can fill.
      D  Every '{% include 'OT ...' %}' names a web template that exists in this tree.
      E  No template uses a Liquid construct this site's engine rejects.

    B is the one that catches finding 14. C catches its mirror image - selecting a column
    nothing in the query will ever populate.

    D and E cover the same failure MODE from two other directions, which is what makes them
    worth having: each one fails SILENTLY. A mistyped include renders nothing at all - the
    engine does not raise, it simply produces no output, exactly as the finding-14 fetch drew
    nothing rather than erroring. E is the list of constructs this engine has already been
    caught rejecting (two-argument truncate, multi-character split, indexing entities[0]);
    each was found in production rather than in a document, and entities[0] throws "Index was
    outside the bounds of the array" on a page that is merely empty that day.

    STATIC, and deliberately so. It reads the checked-in templates and the checked-in
    checklist, needs no environment and no sign-in, and so it can run on every change
    rather than only before a deployment. Dataverse remains authoritative for what a
    question actually is; checklist-v8.md is the catalogue this project maintains beside
    it, and AD-122 content changes are expected to update both.

.EXAMPLE
    powershell -NoProfile -File .\powerpages\Check-PortalTemplates.ps1
#>

[CmdletBinding()]
param(
    [string]$TemplateRoot,
    [string]$ChecklistPath,
    # When given, response types are read from the environment, which is authoritative.
    # Without it the check falls back to checklist-v8.md, which types only some of its
    # questions - see the note where the catalogue is built.
    [string]$OrgUrl
)

$ErrorActionPreference = 'Stop'

# Resolved here rather than as a parameter default, for the reason Check-PortalSecurity.ps1
# gives: $PSScriptRoot is not reliably populated while defaults are bound under 5.1.
if (-not $TemplateRoot) {
    $TemplateRoot = Join-Path $PSScriptRoot 'outcome-testing---outcometesting\web-templates'
}
if (-not $ChecklistPath) {
    $ChecklistPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'knowledge\checklist-v8.md'
}

$failures = New-Object System.Collections.ArrayList
function Add-Failure { param([string]$Rule, [string]$Detail) [void]$failures.Add("[$Rule] $Detail") }

# ---------------------------------------------------------------- the catalogue

if (-not (Test-Path $ChecklistPath)) {
    Write-Error "Checklist catalogue not found: $ChecklistPath"
    exit 2
}

# Which typed column a response type stores its answer in (AD-023). The plug-in's
# ResponseRules.ColumnFor is authoritative; this mirrors it, and the mapping is small and
# stable enough that a mirror is cheaper than a round trip to an environment.
#
# Keys are the type name with every non-alphanumeric character removed, so the two spellings
# this project uses both land here: checklist-v8.md writes "PassFailInsufficient" while the
# environment's option label is "Pass / Fail / Insufficient evidence". PowerShell hashtables
# key strings case-insensitively, which covers "Rich text" against "RichText". Both spellings
# are listed rather than fuzzily matched, because a type this map does not recognise is
# reported as a failure and a near-miss would read as a defect in a template.
$columnFor = @{
    'Text'                  = 'al_answertext'
    'MultilineText'         = 'al_answertext'
    'RichText'              = 'al_answerrichtext'
    'Date'                  = 'al_answerdate'
    'MultiSelect'           = 'al_answerchoices'
    'SingleSelect'          = 'al_answerchoice'
    'PassFail'              = 'al_answerchoice'
    'PassFailInsufficient'  = 'al_answerchoice'
    'YesNo'                 = 'al_answerchoice'
    'YesNoNa'               = 'al_answerchoice'
    'YesNoInsufficient'     = 'al_answerchoice'
    'Grade'                 = 'al_answerchoice'

    # The two environment labels that differ by more than case once normalised. The rest -
    # "Rich text", "Multi select", "Yes / No / N/A" and so on - already land on a key above,
    # because these keys are matched case-insensitively.
    'PassFailInsufficientevidence'  = 'al_answerchoice'
    'YesNoInsufficientevidence'     = 'al_answerchoice'
}

# Dataverse is authoritative. checklist-v8.md is a catalogue this project maintains beside
# it, and it does NOT type every question: the core-check sections list a code and its
# wording only, because their response type is stated once for the whole section in prose.
# So the markdown path can type 14 of 47 rows, and a gate resting on it alone would quietly
# check a fraction of what it appears to.
$responseTypeOf = @{}
$source = 'checklist-v8.md'

if ($OrgUrl) {
    $source = $OrgUrl
    $fetch = @'
<fetch>
  <entity name="al_questionversion">
    <attribute name="al_responsetype" />
    <link-entity name="al_question" from="al_questionid" to="al_questionid" alias="q">
      <attribute name="al_questioncode" />
    </link-entity>
  </entity>
</fetch>
'@
    $tempFetch = [IO.Path]::GetTempFileName()
    [IO.File]::WriteAllText($tempFetch, $fetch)

    $tool = Join-Path (Split-Path $PSScriptRoot -Parent) 'plugins\OutcomeTesting.Registration'
    Push-Location $tool
    try {
        $raw = (& dotnet run -- fetch $OrgUrl "@$tempFetch" 2>&1) -join "`n"
    }
    finally {
        Pop-Location
        Remove-Item $tempFetch -ErrorAction SilentlyContinue
    }

    $start = $raw.IndexOf('{')
    if ($start -lt 0) {
        Write-Error "Could not read question metadata from $OrgUrl."
        exit 2
    }

    foreach ($row in ($raw.Substring($start) | ConvertFrom-Json).rows) {
        $code = $row.'q.al_questioncode'
        if ($code) {
            # The response type comes back as "Rich text (120910011)" - a label, a space and
            # the option value. The value is dropped and the spaces removed, giving
            # "Richtext", which matches the catalogue's "RichText" because a PowerShell
            # hashtable keys strings case-insensitively.
            $label = ($row.al_responsetype -replace '\s*\(\d+\)\s*$', '')
            $responseTypeOf[$code] = ($label -replace '[^A-Za-z0-9]', '')
        }
    }
}
else {
    foreach ($line in Get-Content $ChecklistPath) {
        if ($line -match '^\|\s*(Q-[A-Z0-9-]+)\s*\|\s*[^|]*\|\s*([A-Za-z /]+?)\s*\|') {
            $responseTypeOf[$Matches[1]] = ($Matches[2] -replace '[^A-Za-z0-9]', '')
        }
    }
}

if ($responseTypeOf.Count -eq 0) {
    Write-Error "No question response types were read from $source."
    exit 2
}

# ---------------------------------------------------------------- the templates

$templates = Get-ChildItem -Recurse -File -Filter *.html $TemplateRoot
$fetchCount = 0
$pairChecks = 0

foreach ($file in $templates) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $rel = $file.FullName.Substring($TemplateRoot.Length + 1)

    # Each <fetch>…</fetch> is checked on its own: a template may hold several, and codes in
    # one say nothing about the columns another selects.
    foreach ($fetch in [regex]::Matches($text, '(?s)<fetch.*?</fetch>')) {
        $block = $fetch.Value
        $fetchCount++

        $codes = New-Object System.Collections.Generic.HashSet[string]
        foreach ($m in [regex]::Matches($block, 'attribute\s*=\s*"al_questioncode"[^>]*value\s*=\s*"(Q-[A-Z0-9-]+)"')) {
            [void]$codes.Add($m.Groups[1].Value)
        }

        # The `in` form puts its values in child elements rather than an attribute.
        if ($block -match 'attribute\s*=\s*"al_questioncode"[^>]*operator\s*=\s*"in"') {
            foreach ($m in [regex]::Matches($block, '<value>\s*(Q-[A-Z0-9-]+)\s*</value>')) {
                [void]$codes.Add($m.Groups[1].Value)
            }
        }

        if ($codes.Count -eq 0) { continue }

        $columns = New-Object System.Collections.Generic.HashSet[string]
        foreach ($m in [regex]::Matches($block, 'name\s*=\s*"(al_answer[a-z]+)"')) {
            [void]$columns.Add($m.Groups[1].Value)
        }

        if ($columns.Count -eq 0) { continue }
        $pairChecks++

        $expected = New-Object System.Collections.Generic.HashSet[string]

        foreach ($code in $codes) {
            if (-not $responseTypeOf.ContainsKey($code)) {
                Add-Failure 'A' "$rel names $code, which is not in checklist-v8.md."
                continue
            }

            $type = $responseTypeOf[$code]
            if (-not $columnFor.ContainsKey($type)) {
                Add-Failure 'A' "$rel names $code, whose response type '$type' is not one this check knows."
                continue
            }

            $column = $columnFor[$type]
            [void]$expected.Add($column)

            if (-not $columns.Contains($column)) {
                Add-Failure 'B' ("$rel reads $code ($type) but does not select $column, " +
                    "so that question's answer can never be read. This is audit finding 14.")
            }
        }

        foreach ($column in $columns) {
            if (-not $expected.Contains($column)) {
                Add-Failure 'C' ("$rel selects $column, which none of the questions it names " +
                    "($($codes -join ', ')) can fill.")
            }
        }
    }
}

# ---------------------------------------------------------------- D: includes resolve

# Every OT template this tree defines, by the name Liquid would include it under.
$templateNames = New-Object System.Collections.Generic.HashSet[string]
foreach ($manifest in (Get-ChildItem -Recurse -File -Filter *.webtemplate.yml $TemplateRoot)) {
    foreach ($line in (Get-Content $manifest.FullName)) {
        if ($line -match '^\s*adx_name\s*:\s*(.+?)\s*$') {
            [void]$templateNames.Add($Matches[1])
        }
    }
}

# ---------------------------------------------------------------- E: constructs that fail here
#
# Each pattern is something this engine was caught rejecting on a live page. The fix is
# recorded beside it so a failure here reads as an instruction rather than a prohibition.
$rejected = @(
    @{ Pattern = "truncate:\s*\d+\s*,";      Detail = "two-argument truncate; this engine takes the length only" },
    @{ Pattern = "split:\s*'[^']{2,}'";        Detail = "multi-character split; this engine splits on a single character" }
)

# entities[0] is checked on its own, because the index is not the defect - an UNGUARDED index
# is. The tiles on OT My Work read entities[0] correctly, inside an "if ... .size > 0" that
# opens on the line above, and a rule that failed them would be teaching the wrong lesson.
#
# What throws is indexing an EMPTY result: "Index was outside the bounds of the array". The
# index is evaluated before any filter or null test that follows it, so a `default` filter
# cannot save it and neither can testing the result afterwards - the guard has to come first.
# That is how OT Review Detail's "Review not available" empty state came to be unreachable:
# it was written for a review you cannot see, and the line that threw ran before it.
#
# The guard is looked for on the same line or the three above it, naming the same variable,
# which is the whole of the pattern this codebase uses.
$guardWindow = 3

foreach ($file in $templates) {
    $rel = $file.FullName.Substring($TemplateRoot.Length + 1)
    $text = [IO.File]::ReadAllText($file.FullName)

    # Comments are stripped first, and they have to be: the templates that explain why
    # entities[0] is not used say the words "entities[0]" to do it. A gate that failed on its
    # own documentation would be turned off within a week.
    $code = [regex]::Replace($text, '(?s)<!--.*?-->', '')
    $code = [regex]::Replace($code, '(?s)\{%-?\s*comment\s*-?%\}.*?\{%-?\s*endcomment\s*-?%\}', '')

    foreach ($m in [regex]::Matches($code, "\{%-?\s*include\s+'([^']+)'")) {
        $name = $m.Groups[1].Value

        # Only this solution's own templates. The platform's - Breadcrumbs, Search, Page
        # Header and the faceted-search set - are not in this tree and never will be.
        if (-not $name.StartsWith('OT ', [StringComparison]::Ordinal)) { continue }

        if (-not $templateNames.Contains($name)) {
            Add-Failure 'D' ("$rel includes '$name', which no web template in this tree " +
                "defines. A mistyped include renders NOTHING rather than failing.")
        }
    }

    foreach ($rule in $rejected) {
        foreach ($m in [regex]::Matches($code, $rule.Pattern)) {
            Add-Failure 'E' "$rel uses $($rule.Detail). Found: '$($m.Value.Trim())'."
        }
    }

    $lines = $code -split "`n"
    for ($i = 0; $i -lt $lines.Length; $i++) {
        foreach ($m in [regex]::Matches($lines[$i], '([A-Za-z_]\w*)\.results\.entities\[0\]')) {
            $variable = $m.Groups[1].Value
            $guard = [regex]::Escape($variable) + '\.results\.entities\.size\s*>\s*0'

            $guarded = $false
            for ($back = [Math]::Max(0, $i - $guardWindow); $back -le $i; $back++) {
                if ($lines[$back] -match $guard) { $guarded = $true; break }
            }

            if (-not $guarded) {
                Add-Failure 'E' ("$rel line $($i + 1) indexes $variable.results.entities[0] " +
                    "with no '.size > 0' guard above it. An empty result throws " +
                    "'Index was outside the bounds of the array', and it throws BEFORE any " +
                    "null test that follows - so the empty state below it is unreachable.")
            }
        }
    }
}

# ---------------------------------------------------------------- result

Write-Host ("Checked {0} template(s) and {1} fetch block(s); {2} name a question AND read an answer column, which is the pairing this checks, against {3} question(s) typed from {4}." -f `
    $templates.Count, $fetchCount, $pairChecks, $responseTypeOf.Count, $source)
Write-Host ("Resolved every 'OT ...' include against {0} template name(s), scanned for {1} construct(s) this engine rejects, and checked every entities[0] for a .size guard." -f `
    $templateNames.Count, $rejected.Count)

if (-not $OrgUrl) {
    Write-Host ''
    Write-Host 'Note: run with -OrgUrl to type every question from the environment. Without it'
    Write-Host '      only the questions checklist-v8.md gives a response type are covered.'
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host 'Portal template checks FAILED:'
    foreach ($failure in $failures) { Write-Host "  $failure" }
    exit 1
}

Write-Host 'Portal template assertions all pass.'
exit 0
