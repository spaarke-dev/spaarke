<#
.SYNOPSIS
    Reconcile each task POML's <status> against its row marker in TASK-INDEX.md.

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

function Get-IndexMarkers {
    param([string] $IndexPath)
    $map = @{}
    if (-not (Test-Path -LiteralPath $IndexPath)) { return $map }
    foreach ($line in (Get-Content -LiteralPath $IndexPath -Encoding UTF8)) {
        # `| <marker> <id> |` or `| <marker> **<id>** |`
        $m = [regex]::Match($line, '^\|\s*([^\s|]+)\s*\*{0,2}(\d{3})\*{0,2}\s*\|')
        if ($m.Success) { $map[$m.Groups[2].Value] = $m.Groups[1].Value }
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

# TERMINAL markers — a task is "no longer open". Deliberately more than just ✅, because the index
# has a real vocabulary for terminal-but-not-clean outcomes and they are NOT drift:
#   ✅  completed
#   ⚠️  completed-with-escalation  (accepted residue — e.g. 012 anonymous-share-link revocation)
#   🟡  blocked-shipped            (e.g. 034 impersonation-inertness canary)
# Scoping this to ✅ alone was the FIRST VERSION of this script, and it reported 012 and 034 as
# drift on its very first run. Both were correct as authored. A gate that cries wolf on correct
# state is a gate that gets waived — so the vocabulary is matched, not narrowed.
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

    $details = @()
    foreach ($id in ($poml.Keys | Sort-Object)) {
        if (-not $idx.ContainsKey($id)) { continue }   # no row to compare against
        $pomlDone = Test-PomlDone -Status $poml[$id]
        $marker = $idx[$id]
        $idxDone = $false
        foreach ($d in $doneMarkers) { if ($marker.Contains($d)) { $idxDone = $true } }
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
        Project     = $Name
        Poml        = $poml.Count
        Index       = $idx.Count
        Drift       = $details.Count
        Unparseable = $unparseable
        Skip        = $null
        Details     = $details
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
        if ($r.Drift -gt 0 -or $r.Unparseable) { $rows += $r }
    }
    $totalDrift = ($rows | Measure-Object -Property Drift -Sum).Sum
    foreach ($r in $rows) {
        $tag = if ($r.Unparseable) { 'UNPARSEABLE index format' } else { "drift=$($r.Drift)" }
        Write-Host ("  {0,-52} poml={1,-4} idx={2,-4} {3}" -f $r.Project, $r.Poml, $r.Index, $tag)
    }
    Write-Host ''
    Write-Host ("Projects with drift or an unreadable index: {0}   Total disagreements: {1}" -f $rows.Count, $totalDrift)
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

if ($result.Drift -eq 0) {
    Write-Host ''
    Write-Host 'No drift: every task POML status agrees with its index marker.' -ForegroundColor Green
    exit 0
}

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
exit 1
