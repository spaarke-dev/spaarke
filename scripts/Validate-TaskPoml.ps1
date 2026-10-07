#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Well-formedness + completeness lint for task POML files (the forcing function for the 2026-07-16
    template-drift finding).

.DESCRIPTION
    Two checks per file, in this order:

    (1) XML WELL-FORMEDNESS. The file is parsed with System.Xml.XmlDocument.Load(path) — file-based on
        purpose, so no string/BOM/encoding artifact of our own can masquerade as a content error. A POML
        that does not parse cannot be read by task-execute at all, so it is reported as an ERROR carrying
        the parser message plus line/position, and the field checks below are SKIPPED for that file: one
        root failure, not a cascade of misleading "missing <field>" errors.
        Added 2026-09-18 — this check did not exist, and a POML whose prose quoted bare <element> names
        (the parser reads them as real start tags) passed this gate as "clean, PASS, exit 0" and then
        failed later, at execution time, when task-execute tried to read it.

    (2) FIELD COMPLETENESS. Verifies that every task POML carries the canonical metadata field set that
        the pipeline depends on. A well-formed POML missing e.g. <model-tier> fails SILENTLY otherwise:
        project-pipeline Step 5 dispatch and /goal just fall back to defaults, and nothing flags it. This
        script makes the omission a hard error.

    Authoritative field set: .claude/skills/task-create/SKILL.md (Steps 3.5.5 / 3.5.5b / 3.5.5c / 3.5.6 / 3.8 / 3.65).
    Canonical skeleton: .claude/templates/task-execution.template.md.

.PARAMETER Path
    A tasks/ directory (scans *.poml) or a single .poml file. Defaults to the current directory.

.EXAMPLE
    pwsh scripts/Validate-TaskPoml.ps1 projects/spaarkeai-compose-r3/tasks

.OUTPUTS
    Per-file findings to the console. Exit code 0 if all pass, 1 if any file has an ERROR-level finding
    (a parse failure or a missing/empty required field).
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Path = "."
)

$ErrorActionPreference = 'Stop'

# Required, non-empty metadata fields (task-create Steps 3.5.5b / 3.8).
# <rigor> is handled separately below because it accepts the deprecated <rigor-hint> alias.
$RequiredMeta = @('model-tier', 'effort', 'parallel-group', 'parallel-safe')
$FrontendTags = @('pcf', 'frontend', 'fluent-ui', 'e2e-test')

function Get-PomlFiles {
    param([string]$InputPath)
    if (Test-Path $InputPath -PathType Leaf) { return , (Get-Item $InputPath) }
    if (Test-Path $InputPath -PathType Container) {
        return Get-ChildItem -Path $InputPath -Filter '*.poml' -Recurse -File | Sort-Object FullName
    }
    throw "Path not found: $InputPath"
}

function Test-FieldNonEmpty {
    # Presence of <field>non-whitespace</field> anywhere in $text (case-sensitive tag).
    param([string]$Text, [string]$Field)
    return [regex]::IsMatch($Text, "<$([regex]::Escape($Field))\b[^>]*>\s*\S", 'Singleline')
}

function Test-Poml {
    param([System.IO.FileInfo]$File)

    # NOTE: two stages, and they are deliberately different instruments.
    # Stage 1 is XML WELL-FORMEDNESS — a hard gate, because an unparseable POML is unusable downstream.
    # Stage 2 (everything after it) stays text/regex-based: task POML prose freely contains bare '&',
    # arrows, etc. INSIDE otherwise-legal escaped text, and the field checks are intentionally tolerant of
    # that. What stage 1 catches is the narrower, genuinely broken case — an element name quoted in prose
    # unescaped, which the parser reads as a real start tag and which then breaks the document's nesting.
    $findings = [System.Collections.Generic.List[object]]::new()

    # --- Stage 1: XML well-formedness (runs FIRST; a parse failure short-circuits this file) ---
    # File-based Load, NOT [xml](Get-Content -Raw ...): the latter round-trips through a string and can
    # report BOM/encoding artifacts as content errors, which sends the reader to the wrong line.
    try {
        # The parse is the whole test — the document itself is not needed, only whether Load() throws.
        ([System.Xml.XmlDocument]::new()).Load($File.FullName)
    } catch [System.Xml.XmlException] {
        $findings.Add([pscustomobject]@{
                Level = 'ERROR'
                Msg   = "not well-formed XML (line $($_.Exception.LineNumber), position $($_.Exception.LinePosition)): $($_.Exception.Message)"
            })
        # Skip the field checks for this file: they would report the parser's confusion as missing fields.
        return $findings
    }

    # --- Stage 2: field completeness ---
    $raw = Get-Content -LiteralPath $File.FullName -Raw

    if ($raw -notmatch '(?s)<task\b[^>]*>') {
        $findings.Add([pscustomobject]@{ Level = 'ERROR'; Msg = "no <task ...> root element found" })
        return $findings
    }

    # Metadata block (best-effort) for field-scoping of deprecated names.
    $metaMatch = [regex]::Match($raw, '(?s)<metadata\b[^>]*>(.*?)</metadata>')
    $metaText = if ($metaMatch.Success) { $metaMatch.Groups[1].Value } else { '' }

    # --- Required metadata fields present + non-empty ---
    foreach ($field in $RequiredMeta) {
        if (-not (Test-FieldNonEmpty -Text $raw -Field $field)) {
            $findings.Add([pscustomobject]@{ Level = 'ERROR'; Msg = "missing/empty <$field>" })
        }
    }

    # --- <rigor> (canonical) satisfied by <rigor> OR the deprecated <rigor-hint> alias ---
    if (-not (Test-FieldNonEmpty -Text $raw -Field 'rigor') -and -not (Test-FieldNonEmpty -Text $raw -Field 'rigor-hint')) {
        $findings.Add([pscustomobject]@{ Level = 'ERROR'; Msg = "missing/empty <rigor>" })
    }

    # --- <steps mode="..."> explicit mode ---
    $stepsMatch = [regex]::Match($raw, '<steps\b([^>]*)>')
    if (-not $stepsMatch.Success) {
        $findings.Add([pscustomobject]@{ Level = 'ERROR'; Msg = "missing <steps>" })
    } else {
        $modeMatch = [regex]::Match($stepsMatch.Groups[1].Value, 'mode\s*=\s*"([^"]*)"')
        if (-not $modeMatch.Success -or [string]::IsNullOrWhiteSpace($modeMatch.Groups[1].Value)) {
            $findings.Add([pscustomobject]@{ Level = 'ERROR'; Msg = "<steps> has no mode attribute (directional|prescriptive)" })
        } elseif ($modeMatch.Groups[1].Value -notin @('directional', 'prescriptive')) {
            $findings.Add([pscustomobject]@{ Level = 'WARN'; Msg = "<steps mode='$($modeMatch.Groups[1].Value)'> is not directional|prescriptive" })
        }
    }

    # --- Deprecated field names (scoped to <metadata>) ---
    if ($metaText -match '<rigor-hint\b') {
        $findings.Add([pscustomobject]@{ Level = 'WARN'; Msg = "deprecated <rigor-hint> in <metadata> — rename to <rigor>" })
    }
    if ($metaText -match '<dependencies\b') {
        $findings.Add([pscustomobject]@{ Level = 'WARN'; Msg = "deprecated metadata-sibling <dependencies> — rename to <deps> (the <dependencies> element belongs inside <context>)" })
    }

    # --- NEW-surface tasks require <justification> ---
    $hasNewSurface = [regex]::IsMatch($raw, '<file\b[^>]*\brole\s*=\s*"new"')
    if ($hasNewSurface -and $raw -notmatch '<justification\b') {
        $findings.Add([pscustomobject]@{ Level = 'WARN'; Msg = "task adds NEW surface (relevant-files role='new') but has no <justification> (CLAUDE.md §11)" })
    }

    # --- Frontend tasks require <ui-tests> ---
    $tagsMatch = [regex]::Match($raw, '(?s)<tags\b[^>]*>(.*?)</tags>')
    $tags = if ($tagsMatch.Success) { $tagsMatch.Groups[1].Value.ToLowerInvariant() } else { '' }
    $isFrontend = $false
    foreach ($t in $FrontendTags) { if ($tags -match [regex]::Escape($t)) { $isFrontend = $true; break } }
    if ($isFrontend -and $raw -notmatch '<ui-tests\b') {
        $findings.Add([pscustomobject]@{ Level = 'WARN'; Msg = "frontend task (tags: $($tags.Trim())) has no <ui-tests> (task-create Step 3.65)" })
    }

    return $findings
}

$files = Get-PomlFiles -InputPath $Path
if ($files.Count -eq 0) {
    Write-Host "No .poml files found under: $Path"
    exit 0
}

$errorCount = 0
$warnCount = 0
$cleanCount = 0

foreach ($file in $files) {
    $findings = Test-Poml -File $file
    if ($findings.Count -eq 0) {
        $cleanCount++
        continue
    }
    Write-Host ""
    Write-Host $file.Name -ForegroundColor Cyan
    foreach ($f in $findings) {
        if ($f.Level -eq 'ERROR') {
            $errorCount++
            Write-Host "  [ERROR] $($f.Msg)" -ForegroundColor Red
        } else {
            $warnCount++
            Write-Host "  [WARN]  $($f.Msg)" -ForegroundColor Yellow
        }
    }
}

Write-Host ""
Write-Host "─────────────────────────────────────────────"
Write-Host ("Scanned {0} POML(s): {1} clean, {2} error(s), {3} warning(s)" -f $files.Count, $cleanCount, $errorCount, $warnCount)

if ($errorCount -gt 0) {
    Write-Host "FAIL — task POML(s) are not well-formed XML and/or are missing required canonical fields." -ForegroundColor Red
    exit 1
}
Write-Host "PASS — all task POMLs parse as XML and carry the required canonical field set." -ForegroundColor Green
exit 0
