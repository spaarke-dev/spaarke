<#
.SYNOPSIS
    Checks that repository paths named in agent instruction files exist (ratcheted).

.DESCRIPTION
    Agents treat instruction files as ground truth. When a file they name is moved or deleted, the
    sentence that names it survives (FAILURE-MODES AP-12: "prose outlives the mechanism it describes").
    The 2026-10 module CLAUDE.md audit found that drift in most module files. Markdown LINKS are already
    checked by scripts/validate-markdown-links.ps1; this script checks the backticked PATHS that
    instruction files mostly use, e.g. `src/server/api/Sprk.Bff.Api/Program.cs`.

    Corpus: every CLAUDE.md outside projects/, plus .claude/rules, .claude/constraints, .claude/patterns,
    .claude/adr and .claude/skills/**/SKILL.md. A backticked token is checked when it looks like a repo
    path: it starts with a known top-level folder (src/, tests/, docs/, scripts/, .claude/, .github/,
    infrastructure/, config/) and contains no glob, placeholder or URL characters. It resolves against
    the repo root, then against the instruction file's own folder.

    RATCHET: findings listed in the baseline file are reported but do not fail. A NEW missing path
    exits 1. Remove a baseline line when you fix it; -UpdateBaseline rewrites the baseline from the
    current findings (use it only when you have reviewed them).

.PARAMETER Baseline
    Path of the baseline file (one "instruction-file|path" per line). Default: scripts/quality/instruction-paths.baseline.txt

.PARAMETER UpdateBaseline
    Rewrite the baseline with the current findings and exit 0.

.EXAMPLE
    pwsh -File scripts/quality/Test-InstructionPaths.ps1
#>
[CmdletBinding()]
param(
    [string]$Baseline = 'scripts/quality/instruction-paths.baseline.txt',
    [switch]$UpdateBaseline
)

$ErrorActionPreference = 'Stop'
$root = (git rev-parse --show-toplevel).Trim()
Set-Location $root

$corpus = @(git ls-files '*CLAUDE.md' '.claude/FAILURE-MODES.md' '.claude/rules/*.md' '.claude/constraints/*.md' '.claude/patterns/*.md' '.claude/adr/*.md' '.claude/skills/*/SKILL.md') |
    Where-Object { $_ -notmatch '^(projects/|\.claude/archive/|\.claude/skills/_archived/|provisioning-runs/)' } |
    Sort-Object -Unique

$pathPattern = '`((?:src|tests|docs|scripts|\.claude|\.github|infrastructure|config)/[^`\s]+)`'
# Globs, placeholders and URLs are not paths: {x} <x> [x] * $x ... X.Y NNN XXX, a lone X segment
# (`src/solutions/X/...`, `Spaarke.X.Components`), `XFoo.tsx`-style stand-ins, and `00x`-style ids.
$skipPattern = '[\*\{\}<>\[\]\|\$]|\.\.\.|…|https?:|\bX\.Y\b|\bNNN\b|\bXXX\b|/X/|\.X\.|/X[A-Z]\w*\.|\d+x\b|\bI?Foo\w*\.'
# A `src/...` token whose second segment is not a real src/ child is package-relative (e.g. `src/index.ts`
# in a package's own docs) and cannot be resolved from the repo root.
$srcChildren = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Directory | ForEach-Object Name)
if ($srcChildren.Count -eq 0) { throw 'Could not list src/ — refusing to run with an empty folder list (it would skip every src/ path).' }

# Resolve against what git TRACKS, case-sensitively - the same answer on a Windows workstation
# (case-insensitive file system, untracked local files) as on the Linux CI runner.
$tracked = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($t in (git ls-files)) {
    [void]$tracked.Add($t)
    $parent = $t
    while (($i = $parent.LastIndexOf('/')) -gt 0) { $parent = $parent.Substring(0, $i); if (-not $tracked.Add($parent)) { break } }
}
function Resolve-RepoPath([string]$base, [string]$rel) {
    $joined = if ($base) { "$base/$rel" } else { $rel }
    $parts = [System.Collections.Generic.List[string]]::new()
    foreach ($seg in ($joined -split '/')) {
        if ($seg -eq '' -or $seg -eq '.') { continue }
        if ($seg -eq '..') { if ($parts.Count) { $parts.RemoveAt($parts.Count - 1) }; continue }
        $parts.Add($seg)
    }
    return ($parts -join '/')
}

$findings = [System.Collections.Generic.List[string]]::new()
foreach ($file in $corpus) {
    $dir = Split-Path $file -Parent
    $text = Get-Content -Raw -LiteralPath $file
    if (-not $text) { continue }
    foreach ($m in [regex]::Matches($text, $pathPattern)) {
        $p = $m.Groups[1].Value.TrimEnd('.', ',', ';', ':', ')')
        if ($p -match $skipPattern) { continue }
        if ($p -match '^src/([^/]+)' -and $srcChildren -cnotcontains $Matches[1]) { continue }
        $p = ($p -split '#')[0] -replace ':\d+(-\d+)?$', '' -replace ':\w+\(?$', ''   # drop anchors, :line and :member suffixes
        if (-not $p) { continue }
        $candidates = @((Resolve-RepoPath '' $p))
        if ($dir) { $candidates += (Resolve-RepoPath ($dir -replace '\\', '/') $p) }
        if (-not ($candidates | Where-Object { $tracked.Contains($_) })) {
            # Build output and local config (out/, dist/, publish/, .env.local, ...) are gitignored and
            # legitimately absent from a checkout; naming them is not drift.
            git check-ignore -q --no-index -- $p 2>$null
            if ($LASTEXITCODE -eq 0) { continue }
            $findings.Add("$file|$p")
        }
    }
}
$findings = @($findings | Sort-Object -Unique)

if ($UpdateBaseline) {
    $header = @(
        "# Missing paths named in instruction files, recorded $(Get-Date -Format yyyy-MM-dd) (ratchet baseline).",
        '# Listed lines are reported but do not fail; a NEW missing path fails. Delete a line when you fix it.',
        '# Format: instruction-file|path'
    )
    ($header + $findings) | Set-Content -LiteralPath $Baseline -Encoding utf8
    Write-Host "Baseline rewritten: $($findings.Count) entries -> $Baseline"
    exit 0
}

$known = @()
if (Test-Path -LiteralPath $Baseline) {
    $known = @(Get-Content -LiteralPath $Baseline | Where-Object { $_ -and -not $_.StartsWith('#') })
}
$new = @($findings | Where-Object { $known -notcontains $_ })
$fixed = @($known | Where-Object { $findings -notcontains $_ })

Write-Host "Instruction files scanned: $($corpus.Count). Missing paths: $($findings.Count) (baseline $($known.Count), new $($new.Count))."
if ($fixed.Count) {
    Write-Host "Fixed since the baseline (delete these lines from $Baseline):"
    $fixed | ForEach-Object { Write-Host "  $_" }
}
if ($new.Count) {
    Write-Host 'NEW missing paths (fix the instruction text, or the path):'
    $new | ForEach-Object {
        $f, $p = $_ -split '\|', 2
        Write-Host "::error file=$f::Instruction file names a path that does not exist: $p"
        Write-Host "  $f -> $p"
    }
    exit 1
}
exit 0
