<#
.SYNOPSIS
    Reconcile the task POMLs against TASK-INDEX.md — as SETS, and on each task's status.

.DESCRIPTION
    A project's completion state is written in TWO places that nothing keeps in agreement:

      * the task's own `<status>` element  (root CLAUDE.md §7 step 1)
      * its row marker in `tasks/TASK-INDEX.md` (§7 step 2)

    The index is updated during execution; the POML status is a separate write that nothing
    enforces. On 2026-09-03 a full audit of `unified-access-control-r2` found **17 disagreements
    across 92 tasks** — 14 tasks finished and merged whose POML still said `pending`, one finished
    task the INDEX still showed as in-progress, and two vocabulary mismatches. Neither artifact was
    reliably the authority, which is exactly why a checker is needed rather than a convention.

    A drift of 14 is a missing check, not a discipline problem. This is the check.

    TWO QUESTIONS, NOT ONE (added 2026-09-21, task 116 / ISS-025 / #1004):

      1. Do the POMLs and the index describe the SAME SET of tasks?
      2. Do they agree on each task's state?

    The original script answered only (2). It reconciled by iterating the POMLs and looking up each
    one's index row, so it examined only tasks present on BOTH sides — a POML with no row was
    explicitly `continue`d, and a row with no POML was never visited at all. On 2026-09-18 six index
    rows were added for tasks that had no files; this script printed `poml : 105`, `index : 111`, and
    then "No drift", rc=0. It had both numbers on screen and discarded the difference.

.PARAMETER Project
    Project folder name under `projects/`. Defaults to the name inferred from the current git
    branch (`work/<name>` or `feature/<name>`). Exits 1 on drift — this is the gating mode.

.PARAMETER All
    Scan every project and print an observation report. **Always exits 0.** Repo-wide drift is
    dominated by ARCHIVED projects nobody will revisit (measured 2026-09-03: 84 disagreements
    across 151 projects, concentrated in `x-`-prefixed and closed work). Gating on that total would
    make the check red on day one and waived by day two — the failure mode that retired the
    God-class LOC ratchet. Report repo-wide; gate per-project.

.EXAMPLE
    pwsh scripts/check-task-status-drift.ps1
    # gates the project matching the current branch

.EXAMPLE
    pwsh scripts/check-task-status-drift.ps1 -All
    # non-blocking repo-wide observation report

.NOTES
    ⚠️ A PARSER THAT MATCHES NOTHING MUST NOT REPORT "CLEAN". Index row formats vary across the
    151 projects in this repo; a parse that finds POMLs but zero index rows is an UNPARSEABLE
    result, reported as such and treated as a failure in gating mode. Reporting "no drift" because
    the instrument read nothing is how a broken instrument launders itself into a green check —
    see `.claude/FAILURE-MODES.md` AP-12.
#>
[CmdletBinding()]
param(
    [string] $Project,
    [switch] $All
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-PomlStatuses {
    param([string] $TasksDir)
    $map = @{}
    foreach ($f in Get-ChildItem -Path $TasksDir -Filter '*.poml' -File -ErrorAction SilentlyContinue) {
        $raw = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
        $id = [regex]::Match($raw, '<task\s+id="([^"]+)"')
        if (-not $id.Success) { continue }
        $st = [regex]::Match($raw, '<status>([^<]*)</status>')
        $map[$id.Groups[1].Value] = if ($st.Success) { $st.Groups[1].Value.Trim() } else { 'NO-STATUS-TAG' }
    }
    return $map
}

# Split a markdown table row into its content cells (leading/trailing pipes stripped).
function Get-RowCells {
    param([string] $Line)
    return (($Line -replace '^\|', '' -replace '\|\s*$', '') -split '(?<!\\)\|')
}

<#
    WHICH ROWS ARE STATUS ROWS — the decision this parser has to make before any set comparison.

    Measured 2026-09-21 across all 151 project indexes (task 116). The first-cell pattern alone is
    NOT sufficient: in `unified-access-control-r2` it matched 124 lines for 116 distinct ids, because
    a TASK-INDEX also carries reference tables that name task ids in their first cell —
    a dependency table (`| 055, 064 | 105 | ... |`) and an accuracy-audit table (`| **107** | why |`).
    `$map[$id] = ...` keeps the LAST match, so for eight ids the REFERENCE row silently overwrote the
    real status marker. Nothing went red only because those eight were all open and a marker of `**`
    carries no done token — i.e. by luck. The moment one of them completed, the existing check would
    have reported a PHANTOM disagreement, and a set comparison layered on this parser would have
    reported a phantom orphan.

    Two structural rules, both derived from measurement rather than taste:

      RULE 1 — EXACTLY ONE id in the first cell. A cell naming two tasks (`055, 064`,
      `**087, 088**`) is a dependency list, not one task's status row. Width-independent and
      unconditionally correct.

      RULE 2 — the row must be at least as wide as the index's status table. Where an index contains
      tables of DIFFERING widths, the status table is the wider one; the narrow 2-3 column tables in
      these indexes are reference tables. Floor is 4 cells, the narrowest real status row observed in
      any wide index (`unified-access-control-r2` task 090 at 4 cells).

    ⚠️ RULE 2 IS ADAPTIVE, AND IT HAS TO BE. A fixed floor of 4 was measured first and REJECTED: it
    took 12 projects from some rows to ZERO rows, which would trip the $unparseable guard and exit 1
    on them. Where an index has NO table wider than 3 columns, its narrow tables ARE its status
    tables, so no width filter is applied at all. The floor engages only when the index demonstrably
    has a wider table to distinguish from.

    ⚠️ A MARKER WHITELIST WAS ALSO MEASURED AND REJECTED. Keying on "first cell holds a known status
    glyph or [token]" looks obvious and is wrong: `ai-advanced-capabilities-analysis-hub-r1` writes
    its real status rows as `| **001** | title | phase | rigor | ... |` with NO marker glyph — the
    status lives in a later column. That rule silently dropped ~41 legitimate rows repo-wide. The
    same measurement is why "reject a bold-only first cell" is not used either.
#>
function Get-IndexMarkers {
    param([string] $IndexPath)
    $map = @{}
    if (-not (Test-Path -LiteralPath $IndexPath)) { return $map }

    # `| <marker> <id> |` or `| <marker> **<id>** |`
    # Row shape is `| <marker> [<token>] <id> | ...` — marker, optional ASCII token and the id
    # all live in the SAME first cell. An earlier revision added a pattern that allowed a `|`
    # between marker and id; it then matched rows whose first cell is a WAVE LABEL (`**P0-W0**`)
    # and reported a phantom drift on task 001. Do not reintroduce a cell-spanning pattern.
    $rowPattern = '^\|\s*([^\s|]+(?:\s*\[[a-z]+\])?)\s*\*{0,2}(\d{3})\*{0,2}\s*\|'

    $candidates = @()
    foreach ($line in (Get-Content -LiteralPath $IndexPath -Encoding UTF8)) {
        $m = [regex]::Match($line, $rowPattern)
        if (-not $m.Success) { continue }
        $cells = Get-RowCells -Line $line
        # RULE 1 — a first cell naming more than one task id is a dependency list, not a status row.
        if (([regex]::Matches($cells[0], '\d{3}')).Count -ne 1) { continue }
        $candidates += [pscustomobject]@{
            Id     = $m.Groups[2].Value
            Marker = $m.Groups[1].Value.Trim()
            Width  = $cells.Count
        }
    }
    if ($candidates.Count -eq 0) { return $map }

    # RULE 2 — adaptive width floor (see the block comment above for why it is not fixed).
    $maxWidth = ($candidates | Measure-Object -Property Width -Maximum).Maximum
    $floor = if ($maxWidth -ge 4) { 4 } else { 1 }

    foreach ($c in $candidates) {
        if ($c.Width -lt $floor) { continue }
        $map[$c.Id] = $c.Marker
    }
    return $map
}

# A POML status counts as "done" for reconciliation if it is any terminal state. The vocabularies
# differ on purpose — `completed-with-escalation` (accepted residue) and `blocked-shipped` are real,
# distinct outcomes — so the check compares DONE-ness, not the literal words.
function Test-PomlDone {
    param([string] $Status)
    return $Status.StartsWith('completed') -or $Status -eq 'blocked-shipped'
}

# TERMINAL states — a task is "no longer open".
#
# PREFERRED: the bracketed ASCII token (`[done]`, `[escalated]`, `[blocked]`), mandatory in the index
# since 2026-09-03. ASCII is used because `grep` here SILENTLY returns 0 for characters above U+FFFF,
# and 🔲 is U+1F532 — so `grep -c '🔲'` reported zero open tasks on a 37-open project. See
# FAILURE-MODES G-16. This script reads the file in PowerShell (which handles non-BMP correctly), so
# the tokens are not needed for THIS script to work — they exist so a human or a shell one-liner can
# get the same answer the script does.
#
# FALLBACK: the emoji, for the ~150 pre-existing project indexes that predate the token.
#
# Deliberately more than just "done", because the index has a real vocabulary for
# terminal-but-not-clean outcomes and they are NOT drift:
#   [done]       ✅  completed
#   [escalated]  ⚠️  completed-with-escalation  (accepted residue — e.g. 012 share-link revocation)
#   [blocked]    🟡  blocked-shipped            (e.g. 034 impersonation-inertness canary)
# Scoping this to ✅/[done] alone was the FIRST VERSION of this script, and it reported 012 and 034
# as drift on its very first run. Both were correct as authored. A gate that cries wolf on correct
# state is a gate that gets waived — so the vocabulary is matched, not narrowed.
$doneTokens  = @('[done]', '[escalated]', '[blocked]')
$openTokens  = @('[open]', '[wip]')
$doneMarkers = @('✅', '⚠️', '🟡')

function Invoke-ProjectCheck {
    param([string] $Name, [switch] $ReportOnly)

    $tasksDir = Join-Path $repoRoot "projects/$Name/tasks"
    $indexPath = Join-Path $tasksDir 'TASK-INDEX.md'

    if (-not (Test-Path -LiteralPath $tasksDir)) {
        return [pscustomobject]@{ Project = $Name; Poml = 0; Index = 0; Drift = 0; Unparseable = $false; Skip = 'no tasks/ dir'; Details = @() }
    }

    $poml = Get-PomlStatuses -TasksDir $tasksDir
    $idx = Get-IndexMarkers -IndexPath $indexPath

    # The load-bearing guard: POMLs present but NO index rows parsed means the format is one this
    # script does not understand. That is an unknown result, never a clean one.
    $unparseable = ($poml.Count -gt 0 -and $idx.Count -eq 0 -and (Test-Path -LiteralPath $indexPath))

    # SET COMPARISON (ISS-025 / #1004, task 116). The reconciliation below pairs by id, so it can
    # only ever examine tasks present on BOTH sides. Before pairing, assert the two sides describe
    # the SAME SET of tasks — an unpaired entry on either side is drift.
    #
    # Demonstrated, not theorised: on 2026-09-18 six index rows were added for tasks 109-114 with no
    # task files. This script printed `task POMLs parsed : 105`, `index rows parsed : 111`, and then
    # "No drift", rc=0. The verdict was not wrong, it was NARROW — true about what it measured and
    # silent about what had changed. Both counts were already on screen; the instrument had the
    # evidence and discarded it. Same class as the $unparseable guard one step in (FAILURE-MODES
    # AP-12): a check that cannot see a whole category of change must not call that category clean.
    #
    # ⚠️ SUPPRESSED WHEN $unparseable. An index this script cannot read yields ZERO rows, which would
    # make every POML look unpaired and restate one unknown-format fault as N orphans — burying the
    # actual diagnosis under noise. The $unparseable guard owns that case and reports it in its own
    # words; the set diff is only meaningful once the index parsed at all.
    $unpaired = @()
    if (-not $unparseable) {
        foreach ($id in ($poml.Keys | Sort-Object)) {
            if (-not $idx.ContainsKey($id)) {
                $unpaired += [pscustomobject]@{ Id = $id; Side = 'INDEX has no row for this POML'; Detail = "POML status='$($poml[$id])'" }
            }
        }
        foreach ($id in ($idx.Keys | Sort-Object)) {
            if (-not $poml.ContainsKey($id)) {
                $unpaired += [pscustomobject]@{ Id = $id; Side = 'NO POML backs this index row'; Detail = "INDEX marker='$($idx[$id])'" }
            }
        }
    }

    $details = @()
    foreach ($id in ($poml.Keys | Sort-Object)) {
        if (-not $idx.ContainsKey($id)) { continue }   # already reported as unpaired, above
        $pomlDone = Test-PomlDone -Status $poml[$id]
        $marker = $idx[$id]
        # Token wins when present — it is unambiguous. Emoji only as fallback for legacy indexes.
        $idxDone = $null
        foreach ($t in $doneTokens) { if ($marker.Contains($t)) { $idxDone = $true } }
        if ($null -eq $idxDone) { foreach ($t in $openTokens) { if ($marker.Contains($t)) { $idxDone = $false } } }
        if ($null -eq $idxDone) {
            $idxDone = $false
            foreach ($d in $doneMarkers) { if ($marker.Contains($d)) { $idxDone = $true } }
        }
        if ($pomlDone -ne $idxDone) {
            $details += [pscustomobject]@{
                Id     = $id
                Poml   = $poml[$id]
                Marker = $marker
                Which  = if ($pomlDone) { 'INDEX is behind' } else { 'POML is behind' }
            }
        }
    }

    return [pscustomobject]@{
        Project      = $Name
        Poml         = $poml.Count
        Index        = $idx.Count
        Drift        = $details.Count
        Unpaired     = $unpaired.Count
        Unparseable  = $unparseable
        Skip         = $null
        Details      = $details
        UnpairedRows = $unpaired
    }
}

if ($All) {
    Write-Host 'Task-status drift — repo-wide OBSERVATION report (non-blocking).' -ForegroundColor Cyan
    Write-Host 'Gate per-project; do not gate on this total. See .NOTES in this script for why.'
    Write-Host ''
    $rows = @()
    foreach ($dir in (Get-ChildItem -Path (Join-Path $repoRoot 'projects') -Directory | Sort-Object Name)) {
        $r = Invoke-ProjectCheck -Name $dir.Name -ReportOnly
        if ($r.Skip) { continue }
        if ($r.Drift -gt 0 -or $r.Unpaired -gt 0 -or $r.Unparseable) { $rows += $r }
    }
    $totalDrift = ($rows | Measure-Object -Property Drift -Sum).Sum
    $totalUnpaired = ($rows | Measure-Object -Property Unpaired -Sum).Sum
    foreach ($r in $rows) {
        $tag = if ($r.Unparseable) {
            'UNPARSEABLE index format'
        } else {
            $parts = @()
            if ($r.Drift -gt 0) { $parts += "drift=$($r.Drift)" }
            if ($r.Unpaired -gt 0) { $parts += "unpaired=$($r.Unpaired)" }
            $parts -join ' '
        }
        Write-Host ("  {0,-52} poml={1,-4} idx={2,-4} {3}" -f $r.Project, $r.Poml, $r.Index, $tag)
    }
    Write-Host ''
    Write-Host ("Projects with drift, unpaired entries or an unreadable index: {0}   Total disagreements: {1}   Total unpaired: {2}" -f $rows.Count, $totalDrift, $totalUnpaired)
    exit 0
}

if (-not $Project) {
    $branch = (& git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null)
    if ($branch -match '^(?:work|feature|project)/(.+)$') { $Project = $Matches[1] }
}
if (-not $Project) {
    Write-Host 'Could not infer a project from the branch. Pass -Project <name>, or -All for the report.' -ForegroundColor Yellow
    exit 0   # not a failure: plenty of branches are not project branches
}

$result = Invoke-ProjectCheck -Name $Project

if ($result.Skip) {
    Write-Host "No tasks/ directory for project '$Project' — nothing to reconcile." -ForegroundColor Yellow
    exit 0
}

Write-Host ("Task-status reconciliation — {0}" -f $result.Project) -ForegroundColor Cyan
Write-Host ("  task POMLs parsed : {0}" -f $result.Poml)
Write-Host ("  index rows parsed : {0}" -f $result.Index)

if ($result.Unparseable) {
    Write-Host ''
    Write-Host 'UNPARSEABLE: task POMLs were found but ZERO rows were parsed from TASK-INDEX.md.' -ForegroundColor Red
    Write-Host 'This is an UNKNOWN result, not a clean one — the row format is one this script does'
    Write-Host 'not recognise. Fix the parser or the index format; do not read this as "no drift".'
    exit 1
}

if ($result.Drift -eq 0 -and $result.Unpaired -eq 0) {
    Write-Host ''
    Write-Host 'No drift: the POMLs and the index describe the same set of tasks, and agree on each one.' -ForegroundColor Green
    exit 0
}

if ($result.Unpaired -gt 0) {
    Write-Host ''
    Write-Host ("DRIFT: {0} task(s) appear on only ONE side — the POMLs and the index describe different sets." -f $result.Unpaired) -ForegroundColor Red
    Write-Host ''
    foreach ($u in $result.UnpairedRows) {
        Write-Host ("  {0}  {1}  ({2})" -f $u.Id, $u.Side, $u.Detail)
    }
    Write-Host ''
    Write-Host 'An index row with no task file advertises work that nothing backs; a POML with no row is'
    Write-Host 'work the index cannot report. Add the missing artifact, or remove the one that should not'
    Write-Host 'exist — do NOT silence this by deleting the row that revealed it.'
}

if ($result.Drift -gt 0) {
    Write-Host ''
    Write-Host ("DRIFT: {0} task(s) disagree between POML <status> and TASK-INDEX marker." -f $result.Drift) -ForegroundColor Red
    Write-Host ''
    foreach ($d in $result.Details) {
        Write-Host ("  {0}  POML='{1}'  INDEX='{2}'  -> {3}" -f $d.Id, $d.Poml, $d.Marker, $d.Which)
    }
    Write-Host ''
    Write-Host 'Resolve each one from EVIDENCE, not by picking a side: check git for a completion commit'
    Write-Host 'touching projects/<name>/, then set BOTH artifacts. In the 2026-09-03 audit the POML was'
    Write-Host 'the stale side 14 times and the INDEX was stale once — neither is automatically right.'
}
exit 1
