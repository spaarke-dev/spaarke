#!/usr/bin/env pwsh
<#
.SYNOPSIS
    One-time backfill of the EXISTING children of every secure project, matter and work assignment (unified-access-control-r2
    task 148, C10 part 2): drives the BFF's `secure-child-reconciliation` job — dry run (report-only) by default, -Apply to
    write, -Verify to prove nothing is left to do.

.DESCRIPTION
    Task 146 makes NEW children of a secure record owned by the memberless `Secure Record Owners` team; task 149 shares them
    with exactly the record's sharees. Children that existed before a record was made secure — and the children of every
    record secure before task 148 shipped — are brought into that state by the BFF's ONE engine, SecureChildReconciler,
    which the `secure-child-reconciliation` job runs over every record flagged sprk_issecure = true.

    THIS SCRIPT DOES NOT DECIDE ANYTHING. The ownership rule is the BFF's (IRecordOwnershipResolver), the shares are the
    BFF's (SecureChildShareSynchronizer) — the write-path architecture's rule that a backfill calls the SAME invariant owner
    as the inline path (DATAVERSE-WRITE-PATH-ARCHITECTURE.md §3). The script only:
      - triggers the job through POST /api/admin/jobs/secure-child-reconciliation/trigger (SystemAdmin policy),
      - polls /history for the run's ResultJson, and repeats until a run reports passComplete,
      - saves each run's report, and prints the planned / applied changes with each row's PREVIOUS owner. A report lists at
        most 200 changes per run and counts all of them (changesTotal): when a run made or planned more, the script WARNS
        and names the complete list (the BFF's per-row '[SECURE-CHILD-RECONCILE] plan:' / 'reassign:' log lines). The plan
        includes grandchildren reached only through a row the same pass would move (the dry run plans to a fixpoint over
        planned owners, as the apply writes to one). Every move the sweep plans or makes is INTO the Secure team: it never
        takes a row out of isolation (owner round 24) — such a row is listed as NeedsF3, counted, and warned about; only
        /unsecure-project (a Full Access holder or the record's creator) releases it,
      - for -Apply only: turns the App Service setting SecureChild__Reconciliation__WritesEnabled on for the run and OFF
        again afterwards (an app-setting change restarts the app; the script waits for /healthz).

    SAFETY MODEL
      - Dry run is the DEFAULT: with neither -Apply nor -Verify the job runs report-only and writes nothing.
      - -Apply needs -ResourceGroup and -AppName, and asks for confirmation (use -Confirm:$false in automation). The setting
        is removed again in a finally block, whatever happens.
      - -Verify runs report-only and exits 0 ONLY when a FULL pass plans zero changes and refuses / fails nothing. A pass
        counts from a run that began at the first secure record (the report's startPosition = 1); a run that began
        mid-list (the job's place is shared with an interrupted earlier run or a scheduled tick) is the tail of an earlier
        pass and is not counted. The counted runs must be contiguous, see an unchanged number of secure records, and add
        up to all of them — otherwise the script throws (exit non-zero) instead of passing on part of the list.
      - Idempotent and resumable: every step the job takes is keyed on observed state, so a re-run after an interruption
        completes the work and changes nothing already done.
      - The reversal record: every re-own the BFF makes is logged BEFORE it is written
        ("[SECURE-CHILD-RECONCILE] reassign: <table> <id> owner <previous> -> team <target>"), and the saved reports carry
        the first 200 changes per run with the previous owner (with a warning when a run had more).

    ⚠️ The job runs on the App Service instance that serves the trigger, and its run history AND its place in the list (a
    cursor) are per instance. Run this against a single-instance app (dev), or scale to one instance for the backfill; and
    with the job's schedule disabled (it is registered disabled), so no scheduled tick moves its place mid-pass.

.PARAMETER BffBaseUrl
    The BFF's base URL, e.g. https://spe-api-dev-67e2xz.azurewebsites.net

.PARAMETER ApiScope
    The scope of a token for the BFF API, e.g. api://<bff-app-id>/.default. The token is minted with `az account
    get-access-token --scope`, so the Azure CLI login must be a user the BFF's SystemAdmin policy admits.

.PARAMETER Apply
    Write. Requires -ResourceGroup and -AppName.

.PARAMETER Verify
    Report-only; exit 0 only when nothing is left to do.

.PARAMETER ResourceGroup
    The BFF App Service's resource group (-Apply only).

.PARAMETER AppName
    The BFF App Service's name (-Apply only).

.PARAMETER MaxRuns
    Safety cap on the number of job runs (default 200).

.PARAMETER OutputDirectory
    Where the per-run reports go (default .\secure-child-backfill-<timestamp>).

.EXAMPLE
    .\Invoke-SecureChildBackfill.ps1 -BffBaseUrl https://spe-api-dev-67e2xz.azurewebsites.net -ApiScope api://<id>/.default

.EXAMPLE
    .\Invoke-SecureChildBackfill.ps1 -BffBaseUrl https://spe-api-dev-67e2xz.azurewebsites.net -ApiScope api://<id>/.default `
        -Apply -ResourceGroup spe-infrastructure-westus2 -AppName spe-api-dev-67e2xz

.EXAMPLE
    .\Invoke-SecureChildBackfill.ps1 -BffBaseUrl https://spe-api-dev-67e2xz.azurewebsites.net -ApiScope api://<id>/.default -Verify

.NOTES
    Runbook: docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md §7c.1. Task note: projects/unified-access-control-r2/notes/
    task-148-secure-child-backfill.md. Live runs are manual gates (owner-approved round 11; the main session runs them).
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High', DefaultParameterSetName = 'DryRun')]
param(
    [Parameter(Mandatory)] [string] $BffBaseUrl,
    [Parameter(Mandatory)] [string] $ApiScope,
    [Parameter(ParameterSetName = 'Apply', Mandatory)] [switch] $Apply,
    [Parameter(ParameterSetName = 'Verify', Mandatory)] [switch] $Verify,
    [Parameter(ParameterSetName = 'Apply', Mandatory)] [string] $ResourceGroup,
    [Parameter(ParameterSetName = 'Apply', Mandatory)] [string] $AppName,
    [int] $MaxRuns = 200,
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$JobId = 'secure-child-reconciliation'
$WritesSetting = 'SecureChild__Reconciliation__WritesEnabled'
$base = $BffBaseUrl.TrimEnd('/')
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path (Get-Location) ("secure-child-backfill-{0:yyyyMMdd-HHmmss}" -f (Get-Date))
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Get-BffToken {
    $token = az account get-access-token --scope $ApiScope --query accessToken -o tsv
    if ($LASTEXITCODE -ne 0 -or -not $token) { throw "Could not get a token for $ApiScope (az login as a SystemAdmin user)." }
    return $token
}

function Invoke-Bff([string] $Method, [string] $Path) {
    $headers = @{ Authorization = "Bearer $(Get-BffToken)" }
    return Invoke-RestMethod -Method $Method -Uri "$base$Path" -Headers $headers
}

function Wait-Healthy {
    for ($i = 0; $i -lt 60; $i++) {
        try {
            Invoke-RestMethod -Method Get -Uri "$base/healthz" | Out-Null
            return
        } catch { Start-Sleep -Seconds 10 }
    }
    throw "The BFF did not answer /healthz within 10 minutes."
}

function Invoke-OneRun {
    $trigger = Invoke-Bff 'Post' "/api/admin/jobs/$JobId/trigger"
    $runId = $trigger.runId
    for ($i = 0; $i -lt 360; $i++) {
        Start-Sleep -Seconds 5
        $history = Invoke-Bff 'Get' "/api/admin/jobs/$JobId/history?limit=20"
        $run = @($history) | Where-Object { $_.runId -eq $runId } | Select-Object -First 1
        if ($run -and $run.status -ne 'InProgress') {
            if (-not $run.resultJson) { throw "Run $runId ended '$($run.status)' with no report: $($run.errorMessage)" }
            $path = Join-Path $OutputDirectory "run-$runId.json"
            $run.resultJson | Out-File -FilePath $path -Encoding utf8
            return [pscustomobject]@{ Run = $run; Report = ($run.resultJson | ConvertFrom-Json) }
        }
    }
    throw "Run $runId did not finish within 30 minutes."
}

function Invoke-Pass([string] $ExpectedMode) {
    # A pass counts only from a run that began at the FIRST secure record (startPosition = 1). The job keeps its place in a
    # per-instance cursor shared with every other trigger of it — an interrupted earlier run, or a scheduled tick once task
    # 147 schedules it — so the first run this script triggers may begin mid-list. Such a run is the tail of an earlier
    # pass: its report is saved and printed (with -Apply its writes are real), but it is NOT counted, and the script carries
    # on to the next run, which begins at 1. Then every run must begin exactly where the previous one ended, the number of
    # secure records must not change, and the runs must add up to all of them — otherwise the pass is refused, never
    # reported as a clean pass over part of the list.
    $totals = [ordered]@{ roots = 0; examined = 0; alreadyCorrect = 0; changed = 0; wouldChange = 0; refused = 0; failed = 0;
        needsF3 = 0; changesTotal = 0; changesListed = 0 }
    $incomplete = New-Object System.Collections.Generic.List[string]
    $covering = $false
    $total = $null
    for ($n = 1; $n -le $MaxRuns; $n++) {
        $result = Invoke-OneRun
        $r = $result.Report
        if ($r.mode -ne $ExpectedMode) {
            throw "The job ran in mode '$($r.mode)', expected '$ExpectedMode'. Check $WritesSetting on the App Service."
        }
        foreach ($required in 'startPosition', 'changesTotal', 'changesListed', 'needsF3') {
            if ($null -eq $r.PSObject.Properties[$required]) {
                throw "Run $n's report has no $($required): the deployed BFF predates this script. Deploy it first."
            }
        }
        # The report LISTS at most 200 changes per run; changesTotal counts them all. Never let a shortened list pass for
        # the whole plan: say how many are missing and where the complete list is.
        if ($r.changesTotal -gt $r.changesListed) {
            $message = ("Run {0} made or planned {1} row changes; its report lists only the first {2}. The complete list " +
                "is the BFF log ('[SECURE-CHILD-RECONCILE] plan:' report-only, 'reassign:' with writes), or lower " +
                "SecureChild__Reconciliation__MaxRootsPerRun so each run plans fewer.") -f $n, $r.changesTotal, $r.changesListed
            Write-Warning $message
        }
        if (-not $covering) {
            if ($r.startPosition -ne 1) {
                foreach ($c in @($r.changes)) {
                    if ($c) {
                        Write-Host ("  (tail) {0,-12} {1} {2} {3}  previous={4} target={5} {6}" -f $c.outcome, $c.root,
                            $c.table, $c.id, $c.previousOwner, $c.targetTeam, $c.detail)
                    }
                }
                Write-Host ("Run {0}: began at secure record {1} of {2} — the tail of an earlier pass; not counted." -f $n,
                    $r.startPosition, $r.rootsTotal)
                continue
            }
            $covering = $true
            $total = $r.rootsTotal
        } elseif ($r.startPosition -ne $totals.roots + 1) {
            $message = ("Run {0} began at secure record {1}, but the pass had reached {2}: another trigger of the job (a " +
                "scheduled tick?) moved its place during the pass. Disable the job's schedule and run the script again.") -f
                $n, $r.startPosition, $totals.roots
            throw $message
        }
        if ($r.rootsTotal -ne $total) {
            $message = "The number of secure records changed during the pass ({0} -> {1}); run the script again." -f
                $total, $r.rootsTotal
            throw $message
        }
        $totals.roots += $r.rootsInRun
        foreach ($k in 'examined', 'alreadyCorrect', 'changed', 'wouldChange', 'refused', 'failed', 'needsF3',
                'changesTotal', 'changesListed') { $totals[$k] += $r.$k }
        foreach ($i in @($r.incompleteRoots)) { if ($i) { $incomplete.Add($i) } }
        foreach ($c in @($r.changes)) {
            if ($c) {
                Write-Host ("  {0,-12} {1} {2} {3}  previous={4} target={5} {6}" -f $c.outcome, $c.root, $c.table, $c.id,
                    $c.previousOwner, $c.targetTeam, $c.detail)
            }
        }
        Write-Host ("Run {0}: secure records {1}-{2} of {3}, passComplete={4}" -f $n, $r.startPosition,
            ($r.startPosition + $r.rootsInRun - 1), $r.rootsTotal, $r.passComplete)
        if ($r.passComplete) {
            if ($totals.roots -ne $total) {
                $message = "The pass covered {0} of {1} secure records; run the script again." -f $totals.roots, $total
                throw $message
            }
            return [pscustomobject]@{ Totals = $totals; Incomplete = $incomplete }
        }
    }
    throw "The pass did not complete within $MaxRuns runs (raise -MaxRuns or SecureChild__Reconciliation__MaxRootsPerRun)."
}

if ($Apply) {
    if (-not $PSCmdlet.ShouldProcess("$AppName", "Enable $WritesSetting, re-own the existing children of every secure record, then disable it")) {
        return
    }
    try {
        az webapp config appsettings set --resource-group $ResourceGroup --name $AppName --settings "$WritesSetting=true" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not set $WritesSetting." }
        Start-Sleep -Seconds 90  # An app-setting change restarts the app; polling /healthz at once can answer from the old instance (dev deploy 2026-10-06)
        Wait-Healthy
        $pass = Invoke-Pass 'write'
    } finally {
        az webapp config appsettings delete --resource-group $ResourceGroup --name $AppName --setting-names $WritesSetting | Out-Null
        Wait-Healthy
    }
} else {
    $pass = Invoke-Pass 'report-only'
}

$t = $pass.Totals
Write-Host ""
Write-Host ("Secure records: {0}  examined: {1}  already correct: {2}  changed: {3}  would change: {4}  refused: {5}  failed: {6}" -f `
    $t.roots, $t.examined, $t.alreadyCorrect, $t.changed, $t.wouldChange, $t.refused, $t.failed)
if ($t.needsF3 -gt 0) {
    $message = ("{0} related record(s) are NeedsF3: isolated, but every record they are filed under is ordinary. The " +
        "sweep never takes a row out of isolation (owner round 24); they stay isolated until a Full Access holder or the " +
        "record's creator unsecures it. Listed above with outcome NeedsF3.") -f $t.needsF3
    Write-Warning $message
}
if ($t.changesTotal -gt $t.changesListed) {
    $message = ("The reports list {0} of {1} row changes; the complete list is the BFF log ('[SECURE-CHILD-RECONCILE] plan:' / " +
        "'reassign:' lines).") -f $t.changesListed, $t.changesTotal
    Write-Warning $message
}
foreach ($i in $pass.Incomplete) { Write-Warning "Not fully reconciled: $i" }
Write-Host "Reports: $OutputDirectory"

if ($Verify) {
    if ($t.wouldChange -eq 0 -and $t.refused -eq 0 -and $t.failed -eq 0 -and $pass.Incomplete.Count -eq 0) {
        Write-Host "VERIFY: PASS — nothing left to reconcile." -ForegroundColor Green
        exit 0
    }
    Write-Host "VERIFY: FAIL — related records are not all in their secure state (see above)." -ForegroundColor Red
    exit 1
}

if ($t.refused -gt 0 -or $t.failed -gt 0) { exit 1 }
exit 0
