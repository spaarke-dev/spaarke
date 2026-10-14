<#
.SYNOPSIS
    Reads one completed run of the CI Router workflow (ci-router.yml, name "CI") on master and
    decides two quality-alert statuses: router-master and tier2-master/full-unit-tests.

.DESCRIPTION
    Called by .github/workflows/master-ci-alert.yml (workflow_run on "CI" completed). Read-only:
    it only reads the run, its jobs and its Tier 2 verdict artifacts. The quality-alert action
    does the writing.

    router-master
      failing  when the run's conclusion is not success, the Router job is missing or not
               success, or any job outside Tier 2 (Classify, prereqs, every Tier 1 job) ended
               failure / cancelled / timed_out / anything but success|skipped|neutral.
               Cancelled is failing on purpose: a job that hits timeout-minutes reports
               cancelled, and a check that did not finish is not a pass.
      ok       otherwise.

    tier2-master/full-unit-tests
      Tier 2 is advisory, and its jobs always exit 0, so job conclusions say nothing. The
      verdict is the per-shard file each `Full Unit Tests (Debug) / <shard>` job uploads as
      artifact tier2-unit-verdict-<config>-<shard> (contents `pass` or `fail`), which is what
      the Tier 2 aggregator reads too. Nothing is re-run.
      failing  a shard that ran has no verdict file (cancelled / crashed) or a verdict other
               than `pass`.
      ok       every shard that ran says `pass`.
      skip     no shard ran (unit tests are only dispatched when the BFF changed), or master
               did not compile (that is already router-master's alert; a second alert for the
               same cause would only be noise).

    Both statuses are `skip` when the run is not a completed master push. When a newer master
    push run of the same workflow has already completed, that newer run is evaluated instead:
    the newest completed run is master's current state, and acting on an older one could
    re-open an alert that a newer green run had just closed.

.PARAMETER RunId
    The CI run to evaluate.

.PARAMETER Repo
    owner/name. Defaults to $env:GITHUB_REPOSITORY.

.OUTPUTS
    With $env:GITHUB_OUTPUT set: router_status, router_summary, tier2_status, tier2_summary,
    run_url, note. Without it (local run): the same values printed.

.EXAMPLE
    pwsh scripts/ci/master-ci-verdict.ps1 -RunId 37775675230 -Repo spaarke-dev/spaarke -IgnoreSuperseded

.NOTES
    Requires gh (authenticated; GH_TOKEN in CI with actions: read).
    -IgnoreSuperseded skips the newest-run check; it exists only to replay old runs locally.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [long] $RunId,
    [string] $Repo = $env:GITHUB_REPOSITORY,
    [switch] $IgnoreSuperseded
)

$ErrorActionPreference = 'Stop'
if (-not $Repo) { throw 'Repo is required (or set GITHUB_REPOSITORY).' }

$out = [ordered]@{
    router_status  = 'skip'
    router_summary = ''
    tier2_status   = 'skip'
    tier2_summary  = ''
    run_url        = ''
    note           = ''
}

function Write-VerdictOutput {
    if ($env:GITHUB_OUTPUT) {
        foreach ($k in $out.Keys) {
            $delim = "EOF_$([guid]::NewGuid().ToString('N'))"
            Add-Content -Path $env:GITHUB_OUTPUT -Value "$k<<$delim`n$($out[$k])`n$delim" -Encoding utf8
        }
    }
    foreach ($k in $out.Keys) { Write-Output "---- $k`n$($out[$k])" }
}

function Invoke-GhJson([string[]] $GhArgs) {
    $text = & gh @GhArgs
    if ($LASTEXITCODE -ne 0) { throw "gh $($GhArgs -join ' ') exited $LASTEXITCODE" }
    return ($text | Out-String | ConvertFrom-Json)
}

$run = Invoke-GhJson @('api', "repos/$Repo/actions/runs/$RunId")
$out.run_url = $run.html_url
$sha = $run.head_sha.Substring(0, 9)
$title = (($run.head_commit.message -split "`n")[0]).Trim()

if ($run.head_branch -ne 'master' -or $run.event -ne 'push') {
    $out.note = "Run $RunId is $($run.event) on $($run.head_branch), not a push to master; nothing to decide."
    Write-VerdictOutput; exit 0
}
if ($run.status -ne 'completed') {
    $out.note = "Run $RunId is $($run.status), not completed; nothing to decide."
    Write-VerdictOutput; exit 0
}

if (-not $IgnoreSuperseded) {
    # Evaluate master's CURRENT state: the newest completed master push run. When the triggering
    # run is older (runs finish out of order), switch to the newest instead of skipping. Skipping
    # would lose that verdict whenever the newest run's own alert job was the one dropped from the
    # concurrency queue (GitHub keeps only the latest pending run per group).
    $latest = Invoke-GhJson @('api', "repos/$Repo/actions/workflows/$($run.workflow_id)/runs?branch=master&event=push&status=completed&per_page=1")
    $latestId = $latest.workflow_runs[0].id
    if ($latestId -and [long]$latestId -ne $RunId) {
        $out.note = "Run $RunId is superseded by newer completed master run $latestId, which was evaluated instead."
        $RunId = [long]$latestId
        $run = Invoke-GhJson @('api', "repos/$Repo/actions/runs/$RunId")
        $out.run_url = $run.html_url
        $sha = $run.head_sha.Substring(0, 9)
        $title = (($run.head_commit.message -split "`n")[0]).Trim()
    }
}

# Jobs of the latest attempt (filter=latest is the API default; stated for clarity).
$jobs = @()
$jobLines = & gh api --paginate "repos/$Repo/actions/runs/$RunId/jobs?per_page=100&filter=latest" --jq '.jobs[] | {name, conclusion, html_url}'
if ($LASTEXITCODE -ne 0) { throw "could not list the jobs of run $RunId" }
foreach ($line in $jobLines) { if ($line.Trim()) { $jobs += ($line | ConvertFrom-Json) } }

$passing = @('success', 'skipped', 'neutral')

# ---------------------------------------------------------------- router-master
$routerJob = $jobs | Where-Object { $_.name -eq 'Router' } | Select-Object -First 1
$blockingBad = @($jobs | Where-Object {
    $_.name -notlike 'Tier 2 (Advisory)*' -and $_.name -ne 'Router' -and ($_.conclusion -notin $passing)
})
$reasons = @()
if ($run.conclusion -ne 'success') { $reasons += "The run concluded **$($run.conclusion)**." }
if (-not $routerJob) { $reasons += 'The `Router` job did not run.' }
elseif ($routerJob.conclusion -ne 'success') { $reasons += "The ``Router`` job concluded **$($routerJob.conclusion)** ([log]($($routerJob.html_url)))." }
foreach ($j in $blockingBad) {
    $c = if ($j.conclusion) { $j.conclusion } else { 'no conclusion' }
    $reasons += "Job ``$($j.name)`` concluded **$c** ([log]($($j.html_url)))."
}

if ($reasons.Count -gt 0) {
    $out.router_status = 'failing'
    $out.router_summary = @(
        "Master commit ``$sha`` ($title) is red in the CI Router, the required check."
        ''
        ($reasons | ForEach-Object { "- $_" })
        ''
        'Every branch cut from master now starts red. A cancelled job is what a timeout reports and counts as failing.'
        'This alert closes itself on the next green master run.'
    ) -join "`n"
} else {
    $out.router_status = 'ok'
    $out.router_summary = "Master commit ``$sha`` is green in the CI Router."
}

# ---------------------------------------------------- tier2-master/full-unit-tests
$shardPattern = '^Tier 2 \(Advisory\) / Full Unit Tests \((?<config>[^)]+)\) / (?<shard>.+)$'
$shards = @($jobs | Where-Object { $_.name -match $shardPattern -and $_.conclusion -ne 'skipped' })
$compileBroken = @($blockingBad | Where-Object { $_.name -like '*Compile*' }).Count -gt 0

if ($shards.Count -eq 0) {
    $out.note = (($out.note, 'Tier 2 unit tests did not run for this commit (they run only when the BFF changed); tier2 alert left as is.') | Where-Object { $_ }) -join ' '
} elseif ($compileBroken) {
    $out.note = (($out.note, 'Master did not compile, so the Tier 2 shards could not build; router-master carries that alert and the tier2 alert is left as is.') | Where-Object { $_ }) -join ' '
} else {
    $dir = Join-Path ([IO.Path]::GetTempPath()) "tier2-verdicts-$RunId-$([guid]::NewGuid().ToString('N').Substring(0,8))"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    & gh run download $RunId -R $Repo -p 'tier2-unit-verdict-*' -D $dir 2>&1 | Write-Verbose
    # A failed download leaves the directory empty; every shard then reads as missing (failing).

    $bad = @()
    foreach ($s in $shards) {
        $null = $s.name -match $shardPattern
        $config = $Matches['config']; $shard = $Matches['shard']
        $file = Get-ChildItem -Path $dir -Recurse -File -Filter "verdict-$config-$shard.txt" -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $file) {
            $bad += "Shard ``$shard`` has no verdict file (job concluded **$($s.conclusion)**; a missing verdict means the shard was cancelled or crashed before reporting) ([log]($($s.html_url)))."
            continue
        }
        $v = (Get-Content -Path $file.FullName -Raw).Trim()
        if ($v -ne 'pass') {
            $bad += "Shard ``$shard`` verdict **$v**: a test failed both passes, a failed test was not re-run, or the test host crashed ([log]($($s.html_url)))."
        }
    }
    Remove-Item -Recurse -Force -Path $dir -ErrorAction SilentlyContinue

    if ($bad.Count -gt 0) {
        $out.tier2_status = 'failing'
        $out.tier2_summary = @(
            "Tier 2 ``Full Unit Tests`` on master commit ``$sha`` ($title) is not green."
            ''
            ($bad | ForEach-Object { "- $_" })
            ''
            'Tier 2 is advisory (it never blocks a merge), which is why this alert exists: otherwise nobody sees it.'
            'The shard log names the failing tests. This alert closes itself on the next master run whose shards all pass.'
        ) -join "`n"
    } else {
        $out.tier2_status = 'ok'
        $out.tier2_summary = "All $($shards.Count) Tier 2 unit-test shards passed on master commit ``$sha``."
    }
}

Write-VerdictOutput
