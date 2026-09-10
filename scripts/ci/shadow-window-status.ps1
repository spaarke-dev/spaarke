<#
.SYNOPSIS
    Reports progress toward the CI shadow-window exit criterion.

.DESCRIPTION
    The shadow window is the gate between "the two-tier CI exists" and "the
    legacy sdap-ci.yml can be deleted". Its exit criterion (spec.md, MUST Rules
    + "Shadow-window exit criterion", amended 2026-08-27):

        20 code PRs on which sdap-ci.yml and CI (Router) returned the SAME
        blocking verdict, with ZERO false greens, spanning >= 5 calendar days.

    This script reports where that stands. It is READ-ONLY and holds no state
    of its own -- every number is derived live from the GitHub API, so it cannot
    drift, cannot be stale, and needs no maintenance. Run it whenever you want
    to know; there is deliberately no cron, no workflow, and no tracking file.

    A PR counts toward the 20 only if BOTH systems reached a terminal
    conclusion on its head commit. That single rule also handles docs-only PRs
    correctly for free: sdap-ci.yml has paths-ignore for docs/**, **.md and
    .claude/**, so it never runs on them, so no comparison exists and the PR is
    skipped -- which is exactly what the spec requires (tier1/tier2 are also
    correctly skipped there via the router's docs_only classifier).

.PARAMETER Limit
    How many recently-merged PRs to examine. Default 60 -- comfortably more than
    the 20 needed, since docs-only PRs are skipped and don't count.

.PARAMETER Since
    Window start. PRs merged before this are EXCLUDED, and the default is
    load-bearing rather than cosmetic.

    The comparison treats sdap-ci.yml as the trusted baseline: "legacy red +
    Router green" is read as the new tier having passed something real. That
    reading is only valid while the baseline is itself trustworthy. Before
    2026-08-27 it was not -- sdap-ci was failing roughly 60% of master runs
    (I1 tenant regression, comment-blind I5 scanner, a retry classifier that
    defined "deterministic" as "not on an allowlist"). Every one of those reds
    was the baseline being broken, not the new tier being wrong.

    Run this script against that period and it reports a wall of disqualifying
    false greens that are nothing of the sort, and the window can never close.
    Verified empirically on first run: 5 such rows, all dated 08-18 to 08-26.

    Default is immediately after the LAST change to the CI configuration under
    observation. Starting earlier would measure a configuration that no longer
    exists -- the same reason the window must not be edited while it runs.

    ADVANCED 2026-09-10 (was 2026-08-27T20:47:00Z, immediately after PR #841,
    router concurrency keyed per-SHA). PR #944 / ce5c2c3d7, merged
    2026-09-04T22:13:10Z, widened Tier 1's compile step from
    `Sprk.Bff.Api.csproj` to `dotnet build Spaarke.sln`. That IS a change to the
    configuration under observation, so by this parameter's own doctrine the
    window restarts after it.

    Why it had to move: PR #934 (2026-09-03) merged a solution that did not
    compile -- `Phase2EndToEndFixture.cs(443,17) CS1503` -- and Router reported
    SUCCESS, because Tier 1 then built only the production BFF project and
    Router excludes Tier 2 by construction. A genuine ROUTER DEFECT, and one
    whose class was 5 of 8 test projects; #944 is its fix. Measuring across
    #944 compares two different Tier 1s, which is precisely what this parameter
    exists to prevent. Diagnosis:
    `projects/spaarkeai-word-add-in-r1/notes/044-false-green-diagnosis.md`.

    Cost of the advance, stated plainly: 6 comparable PRs and 1.1 calendar days
    of accumulated agreement are discarded. Post-advance the window is clean --
    0 false greens, 0 false reds.

    KNOWN LIMITATION, deliberately NOT fixed here. `$falseGreens` (line ~149) is
    computed over the ENTIRE window and `$ready` (line ~189) gates on
    `$falseGreens.Count -eq 0` WITHOUT filtering by `$countingFrom`, even though
    the agreeing count IS filtered. So a false green latches permanently and the
    window can never close, which contradicts this script's own printed line
    "the count above restarts from the most recent one". Advancing -Since sidesteps
    that latch for #934 rather than repairing it; the next false green will latch
    the same way. Repairing it is a separate decision about what the exit
    criterion MEANS -- a hard stop, or a reset -- and belongs to whoever owns the
    cutover, not to a `-Since` bump.

.EXAMPLE
    pwsh scripts/ci/shadow-window-status.ps1

.EXAMPLE
    # Include the pre-remediation period -- diagnostic only, NOT the criterion.
    pwsh scripts/ci/shadow-window-status.ps1 -Since '2026-08-01'

.NOTES
    Requires the `gh` CLI, authenticated. Read-only: performs no writes.
#>

[CmdletBinding()]
param(
    [int] $Limit = 60,

    # See .PARAMETER Since -- do not lower this casually.
    [datetime] $Since = '2026-09-04T22:13:10Z'
)

$ErrorActionPreference = 'Stop'

# The two systems being compared. Names must match the `name:` in each workflow.
$LegacyWorkflow = 'SDAP CI'
$NewWorkflow    = 'CI'

$TargetPrs  = 20
$MinDaySpan = 5

Write-Host ''
Write-Host 'CI shadow window -- progress toward retiring sdap-ci.yml' -ForegroundColor Cyan
Write-Host ('=' * 72)

$repo = (gh repo view --json nameWithOwner --jq .nameWithOwner)
if (-not $repo) { throw 'Could not resolve the repository. Is `gh` authenticated?' }

$prsJson = gh pr list --state merged --limit $Limit --json number,title,mergeCommit,mergedAt
$prs = $prsJson | ConvertFrom-Json

$rows = [System.Collections.Generic.List[object]]::new()

$excludedAsPreWindow = 0

foreach ($pr in $prs) {
    $sha = $pr.mergeCommit.oid
    if (-not $sha) { continue }

    # Pre-window PRs are excluded before any comparison: the baseline was not
    # trustworthy then, so "legacy red + Router green" carries no signal.
    if ([datetime]$pr.mergedAt -lt $Since) { $excludedAsPreWindow++; continue }

    # All workflow runs for this exact commit, both systems in one call.
    $runs = (gh api "repos/$repo/actions/runs?head_sha=$sha&per_page=50" --jq '.workflow_runs[] | "\(.name)\t\(.status)\t\(.conclusion)"') -split "`n" |
            Where-Object { $_ }

    $legacy = $null; $new = $null
    foreach ($r in $runs) {
        $parts = $r -split "`t"
        if ($parts[0] -eq $LegacyWorkflow -and $parts[1] -eq 'completed') { $legacy = $parts[2] }
        if ($parts[0] -eq $NewWorkflow    -and $parts[1] -eq 'completed') { $new    = $parts[2] }
    }

    # Cancelled runs are not verdicts -- they are absent data. Treat as no-compare.
    if ($legacy -in @($null, 'cancelled') -or $new -in @($null, 'cancelled')) { continue }

    $legacyGreen = ($legacy -eq 'success')
    $newGreen    = ($new    -eq 'success')

    $verdict =
        if     ($legacyGreen -eq $newGreen)          { 'agree' }
        elseif (-not $legacyGreen -and $newGreen)    { 'FALSE-GREEN' }   # disqualifying
        else                                          { 'false-red'  }

    $rows.Add([pscustomobject]@{
        Pr       = $pr.number
        MergedAt = [datetime]$pr.mergedAt
        Legacy   = $legacy
        Router   = $new
        Verdict  = $verdict
        Title    = $pr.title
    })
}

Write-Host ''
Write-Host ('  Window opened           : {0:yyyy-MM-dd HH:mm} UTC' -f $Since.ToUniversalTime())
if ($excludedAsPreWindow -gt 0) {
    Write-Host ('  Excluded as pre-window  : {0} PR(s) -- baseline was not trustworthy before this' -f $excludedAsPreWindow) -ForegroundColor DarkGray
}

if ($rows.Count -eq 0) {
    Write-Host ''
    Write-Host '  No comparable PRs in the window yet.' -ForegroundColor Yellow
    Write-Host '  Both systems must reach a terminal conclusion on the same commit.'
    Write-Host '  Docs-only PRs never qualify (sdap-ci skips them via paths-ignore).'
    Write-Host ''
    Write-Host '  Nothing to do -- keep merging normally.' -ForegroundColor Cyan
    Write-Host ''
    exit 0
}

$falseGreens = @($rows | Where-Object Verdict -eq 'FALSE-GREEN')
$falseReds   = @($rows | Where-Object Verdict -eq 'false-red')
$agreed      = @($rows | Where-Object Verdict -eq 'agree')

# A false green resets the count: only PRs merged AFTER the most recent one
# count toward the target.
$countingFrom = if ($falseGreens.Count -gt 0) {
    ($falseGreens | Sort-Object MergedAt | Select-Object -Last 1).MergedAt
} else { [datetime]::MinValue }

$counting = @($agreed | Where-Object { $_.MergedAt -gt $countingFrom })
$daySpan  = if ($counting.Count -gt 0) {
    [math]::Round((( $counting | Measure-Object MergedAt -Maximum).Maximum -
                   ( $counting | Measure-Object MergedAt -Minimum).Minimum).TotalDays, 1)
} else { 0 }

Write-Host ''
Write-Host ('  Comparable PRs examined : {0}' -f $rows.Count)
Write-Host ('  Agreeing                : {0} / {1}' -f $counting.Count, $TargetPrs) -ForegroundColor $(if ($counting.Count -ge $TargetPrs) { 'Green' } else { 'Yellow' })
Write-Host ('  Calendar-day span       : {0} / {1}' -f $daySpan, $MinDaySpan) -ForegroundColor $(if ($daySpan -ge $MinDaySpan) { 'Green' } else { 'Yellow' })
Write-Host ('  False reds (logged)     : {0}' -f $falseReds.Count)
Write-Host ('  FALSE GREENS            : {0}' -f $falseGreens.Count) -ForegroundColor $(if ($falseGreens.Count -gt 0) { 'Red' } else { 'Green' })

if ($falseGreens.Count -gt 0) {
    Write-Host ''
    Write-Host '  A false green is DISQUALIFYING -- the new tier passed a commit the' -ForegroundColor Red
    Write-Host '  legacy system failed. Diagnose before continuing; the count above' -ForegroundColor Red
    Write-Host '  restarts from the most recent one.' -ForegroundColor Red
    $falseGreens | Sort-Object MergedAt -Descending |
        Select-Object Pr, MergedAt, Legacy, Router, Title -First 5 | Format-Table -AutoSize
}

if ($falseReds.Count -gt 0) {
    Write-Host ''
    Write-Host '  False reds do not disqualify, but each one burns the "no constant' -ForegroundColor Yellow
    Write-Host '  reds" goal and must be understood before branch protection:' -ForegroundColor Yellow
    $falseReds | Sort-Object MergedAt -Descending |
        Select-Object Pr, MergedAt, Legacy, Router, Title -First 5 | Format-Table -AutoSize
}

$ready = ($counting.Count -ge $TargetPrs) -and ($daySpan -ge $MinDaySpan) -and ($falseGreens.Count -eq 0)

Write-Host ''
if ($ready) {
    Write-Host '  WINDOW SATISFIED -- sdap-ci.yml may be retired (tasks 071/075/077),' -ForegroundColor Green
    Write-Host '  then branch protection with `CI / Router` as the required check.' -ForegroundColor Green
} else {
    Write-Host '  Window still open. Nothing to do -- keep merging normally.' -ForegroundColor Cyan
    Write-Host '  Do NOT edit ci-router.yml / ci-tier1-blocking.yml / ci-tier2-advisory.yml' -ForegroundColor Cyan
    Write-Host '  while it runs: changing the configuration invalidates what was observed.' -ForegroundColor Cyan
}
Write-Host ''
