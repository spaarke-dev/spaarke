<#
.SYNOPSIS
    Decides two quality-alert statuses for master from the CI Router workflow (ci-router.yml,
    name "CI"): router-master and tier2-master/full-unit-tests.

.DESCRIPTION
    Called by .github/workflows/master-ci-alert.yml (workflow_run on "CI" completed). Read-only:
    it only reads runs, their jobs and their Tier 2 verdict artifacts. The quality-alert action
    does the writing.

    WHAT IS EVALUATED. Not one run, but master's current state: the newest -Window completed
    push runs of the workflow on master, newest first. Each check is decided from the most
    recent run that EXECUTED it (conclusion other than `skipped`). One run is not enough:
      - a docs-only push skips Tier 1 entirely and reports a green Router, so judging that run
        alone would close an alert while master still does not compile;
      - path-gated jobs (Auth Smoke, DataGrid gate, ...) and the Tier 2 shards run only when
        their paths change, so a later run that did not touch those paths says nothing about
        them;
      - runs finish out of order (a slow BFF run can complete after a newer client-only run),
        so the triggering run is not necessarily the newest.

    router-master
      failing  any job outside Tier 2 (Classify, prereqs, Router, every Tier 1 job) whose most
               recent execution in the window ended anything but success / neutral. Cancelled
               is failing on purpose: a job that hits timeout-minutes reports cancelled, and a
               check that did not finish is not a pass. Also failing when the newest run
               concluded non-success without starting any job (e.g. startup_failure).
      ok       every such job's most recent execution passed.

    tier2-master/full-unit-tests
      Tier 2 is advisory and its jobs always exit 0, so job conclusions say nothing. The verdict
      is the per-shard file each `Full Unit Tests (Debug) / <shard>` job uploads as artifact
      tier2-unit-verdict-<config>-<shard> (contents `pass` or `fail`), which is what the Tier 2
      aggregator reads too. Nothing is re-run. Each shard is judged from its most recent run in
      the window in which master compiled (a compile break is router-master's alert; a second
      alert for the same cause would only be noise).
      failing  a shard's most recent execution has no verdict file (cancelled / crashed) or a
               verdict other than `pass`.
      ok       every shard's most recent execution says `pass`.
      skip     no shard ran in the window.

    Both statuses are `skip` when the triggering run is not a completed push to master.
    Every gh call is retried (3 attempts); if one still fails the script throws, and the
    workflow raises the master-ci-alert/evaluation alert instead of staying silent.

    KNOWN LIMITS: a job whose last execution is older than the window (default 25 master runs)
    is no longer considered, so its alert would close as recovered; at current master volume the
    window covers roughly a working day. Conversely, a job that failed and was then renamed or
    removed keeps its red until it ages out of the window, since it never executes again.

.PARAMETER RunId
    The triggering CI run. It identifies the workflow; it is also the window's end with -AsOf.

.PARAMETER Repo
    owner/name. Defaults to $env:GITHUB_REPOSITORY.

.PARAMETER Window
    How many completed master push runs to look back over (default 25, max 100).

.PARAMETER AsOf
    Evaluate master as it was when RunId was created (the window ends at RunId) instead of
    master's current state. For replaying past runs locally.

.OUTPUTS
    With $env:GITHUB_OUTPUT set: router_status, router_summary, tier2_status, tier2_summary,
    run_url, note. Without it (local run): the same values printed.

.EXAMPLE
    pwsh scripts/ci/master-ci-verdict.ps1 -RunId 37775675230 -Repo spaarke-dev/spaarke -AsOf

.NOTES
    Requires gh (authenticated; GH_TOKEN in CI with actions: read).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [long] $RunId,
    [string] $Repo = $env:GITHUB_REPOSITORY,
    [ValidateRange(1, 100)] [int] $Window = 25,
    [switch] $AsOf
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

# Runs gh with retries. Returns the output lines; throws after the last failed attempt.
# -AllowPattern: a failure whose output matches it is a valid answer, returned with Ok = $false.
function Invoke-Gh {
    param([string[]] $GhArgs, [string] $AllowPattern)
    $delays = @(0, 5, 15)
    $text = $null
    foreach ($d in $delays) {
        if ($d) { Start-Sleep -Seconds $d }
        $text = & gh @GhArgs 2>&1
        if ($LASTEXITCODE -eq 0) { return [pscustomobject]@{ Ok = $true; Lines = @($text) } }
        if ($AllowPattern -and (($text | Out-String) -match $AllowPattern)) {
            return [pscustomobject]@{ Ok = $false; Lines = @($text) }
        }
        Write-Verbose "gh $($GhArgs -join ' ') failed (exit $LASTEXITCODE): $($text | Out-String)"
    }
    throw "gh $($GhArgs -join ' ') failed after $($delays.Count) attempts: $($text | Out-String)"
}

function Invoke-GhJson([string[]] $GhArgs) {
    return ((Invoke-Gh -GhArgs $GhArgs).Lines | Where-Object { $_ -is [string] } | Out-String | ConvertFrom-Json)
}

function Get-RunJob([long] $Id) {
    $r = Invoke-Gh -GhArgs @('api', '--paginate', "repos/$Repo/actions/runs/$Id/jobs?per_page=100&filter=latest",
        '--jq', '.jobs[] | {name, conclusion, html_url}')
    $list = @()
    foreach ($line in $r.Lines) {
        if ($line -is [string] -and $line.Trim()) { $list += ($line | ConvertFrom-Json) }
    }
    return , $list
}

function Get-RunLabel($r) {
    $sha = $r.head_sha.Substring(0, 9)
    $title = (($r.head_commit.message -split "`n")[0]).Trim()
    return "``$sha`` ($title)"
}

# ------------------------------------------------------------------ the triggering run
$trigger = Invoke-GhJson @('api', "repos/$Repo/actions/runs/$RunId")
if ($trigger.head_branch -ne 'master' -or $trigger.event -ne 'push') {
    $out.run_url = $trigger.html_url
    $out.note = "Run $RunId is $($trigger.event) on $($trigger.head_branch), not a push to master; nothing to decide."
    Write-VerdictOutput; exit 0
}
if ($trigger.status -ne 'completed') {
    $out.run_url = $trigger.html_url
    $out.note = "Run $RunId is $($trigger.status), not completed; nothing to decide."
    Write-VerdictOutput; exit 0
}

# ------------------------------------------------------------------ the window
$query = "repos/$Repo/actions/workflows/$($trigger.workflow_id)/runs?branch=master&event=push&status=completed&per_page=$Window"
if ($AsOf) {
    # ConvertFrom-Json turns timestamps into DateTime; the API wants ISO 8601 UTC.
    $end = ([datetime]$trigger.created_at).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $query += '&created=' + [uri]::EscapeDataString("<=$end")
}
$runs = @((Invoke-GhJson @('api', $query)).workflow_runs | Sort-Object -Property created_at -Descending)
if ($runs.Count -eq 0) { $runs = @($trigger) }
$newest = $runs[0]
$out.run_url = $newest.html_url
$out.note = "Evaluated the $($runs.Count) newest completed master push runs (newest: run $($newest.id), $(Get-RunLabel $newest)); each check is judged from the most recent run that executed it."

$passing = @('success', 'neutral')
$shardPattern = '^Tier 2 \(Advisory\) / Full Unit Tests \((?<config>[^)]+)\) / (?<shard>.+)$'
$jobState = [ordered]@{}     # job name   -> latest execution (outside Tier 2)
$shardState = [ordered]@{}   # shard name -> latest execution in a run where master compiled
$startupProblem = $null

for ($i = 0; $i -lt $runs.Count; $i++) {
    $r = $runs[$i]
    $jobs = Get-RunJob ([long]$r.id)
    if ($i -eq 0 -and $jobs.Count -eq 0 -and $r.conclusion -notin @('success', 'neutral', 'skipped')) {
        $startupProblem = "The newest master run $(Get-RunLabel $r) concluded **$($r.conclusion)** before any job started ([run]($($r.html_url)))."
    }
    $compileBroken = @($jobs | Where-Object {
        $_.name -like '*Compile*' -and $_.conclusion -ne 'skipped' -and $_.conclusion -notin $passing
    }).Count -gt 0
    foreach ($j in $jobs) {
        if ($j.conclusion -eq 'skipped') { continue }   # did not execute in this run
        if ($j.name -like 'Tier 2 (Advisory)*') {
            if (-not $compileBroken -and $j.name -match $shardPattern -and -not $shardState.Contains($j.name)) {
                $shardState[$j.name] = [pscustomobject]@{
                    Config = $Matches['config']; Shard = $Matches['shard']; Job = $j; Run = $r
                }
            }
            continue
        }
        if (-not $jobState.Contains($j.name)) {
            $jobState[$j.name] = [pscustomobject]@{ Job = $j; Run = $r }
        }
    }
}

# ---------------------------------------------------------------- router-master
$reasons = @()
if ($startupProblem) { $reasons += $startupProblem }
foreach ($name in $jobState.Keys) {
    $s = $jobState[$name]
    if ($s.Job.conclusion -notin $passing) {
        $c = if ($s.Job.conclusion) { $s.Job.conclusion } else { 'no conclusion' }
        $reasons += "Job ``$name`` concluded **$c** on its most recent master run, $(Get-RunLabel $s.Run) ([log]($($s.Job.html_url)))."
    }
}

if ($reasons.Count -gt 0) {
    $out.router_status = 'failing'
    $out.router_summary = @(
        'Master is red in the CI Router, the required check.'
        ''
        ($reasons | ForEach-Object { "- $_" })
        ''
        'Every branch cut from master now starts red. A cancelled job is what a timeout reports and counts as failing.'
        'This alert closes itself once every job listed has executed again and passed on a later master run. A path-gated job runs only when its paths change; a job not executed in the newest 25 master runs (for example renamed or removed) drops out of the evaluation.'
    ) -join "`n"
} elseif ($jobState.Count -gt 0) {
    $out.router_status = 'ok'
    $out.router_summary = "All $($jobState.Count) Router and Tier 1 jobs passed on their most recent master run (newest master run: $(Get-RunLabel $newest))."
}

# ---------------------------------------------------- tier2-master/full-unit-tests
if ($shardState.Count -eq 0) {
    $out.note += " No Tier 2 unit-test shard ran in a compiling master run in this window (they run only when the BFF changed); the tier2 alert is left as is."
} else {
    $bad = @()
    foreach ($group in ($shardState.Values | Group-Object { $_.Run.id })) {
        $rid = $group.Name
        $dir = Join-Path ([IO.Path]::GetTempPath()) "tier2-verdicts-$rid-$([guid]::NewGuid().ToString('N').Substring(0,8))"
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        # "no valid artifacts" is a real answer (every shard of that run lost its verdict); any
        # other failure, after retries, throws and raises the evaluation alert instead of
        # being misreported as crashed shards.
        $dl = Invoke-Gh -GhArgs @('run', 'download', $rid, '-R', $Repo, '-p', 'tier2-unit-verdict-*', '-D', $dir) -AllowPattern 'no valid artifacts'
        if (-not $dl.Ok) { Write-Verbose "run $rid has no tier2 verdict artifacts" }
        foreach ($s in $group.Group) {
            $where = "on $(Get-RunLabel $s.Run) ([log]($($s.Job.html_url)))"
            $file = Get-ChildItem -Path $dir -Recurse -File -Filter "verdict-$($s.Config)-$($s.Shard).txt" -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $file) {
                $bad += "Shard ``$($s.Shard)`` has no verdict file $where. The job concluded **$($s.Job.conclusion)**; a missing verdict means the shard was cancelled or crashed before reporting."
                continue
            }
            $v = (Get-Content -Path $file.FullName -Raw).Trim()
            if ($v -ne 'pass') {
                $bad += "Shard ``$($s.Shard)`` verdict **$v** ${where}: a test failed both passes, a failed test was not re-run, or the test host crashed."
            }
        }
        Remove-Item -Recurse -Force -Path $dir -ErrorAction SilentlyContinue
    }

    if ($bad.Count -gt 0) {
        $out.tier2_status = 'failing'
        $out.tier2_summary = @(
            'Tier 2 `Full Unit Tests` is not green on master.'
            ''
            ($bad | ForEach-Object { "- $_" })
            ''
            'Tier 2 is advisory (it never blocks a merge), which is why this alert exists: otherwise nobody sees it.'
            'The shard log names the failing tests. This alert closes itself once every shard listed passes on a later master run that changes BFF paths (shards run only then).'
        ) -join "`n"
    } else {
        $out.tier2_status = 'ok'
        $out.tier2_summary = "All $($shardState.Count) Tier 2 unit-test shards passed on their most recent master run."
    }
}

Write-VerdictOutput
