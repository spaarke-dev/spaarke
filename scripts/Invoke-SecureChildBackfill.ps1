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
      - saves each run's report, and prints the planned / applied changes with each row's PREVIOUS owner,
      - for -Apply only: turns the App Service setting SecureChild__Reconciliation__WritesEnabled on for the run and OFF
        again afterwards (an app-setting change restarts the app; the script waits for /healthz).

    SAFETY MODEL
      - Dry run is the DEFAULT: with neither -Apply nor -Verify the job runs report-only and writes nothing.
      - -Apply needs -ResourceGroup and -AppName, and asks for confirmation (use -Confirm:$false in automation). The setting
        is removed again in a finally block, whatever happens.
      - -Verify runs report-only and exits 0 ONLY when a full pass plans zero changes and refuses / fails nothing.
      - Idempotent and resumable: every step the job takes is keyed on observed state, so a re-run after an interruption
        completes the work and changes nothing already done.
      - The reversal record: every re-own the BFF makes is logged BEFORE it is written
        ("[SECURE-CHILD-RECONCILE] reassign: <table> <id> owner <previous> -> team <target>"), and the saved reports carry
        the first 200 changes per run with the previous owner.

    ⚠️ The job runs on the App Service instance that serves the trigger, and its run history is per instance. Run this
    against a single-instance app (dev), or scale to one instance for the backfill.

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
    $totals = [ordered]@{ roots = 0; examined = 0; alreadyCorrect = 0; changed = 0; wouldChange = 0; refused = 0; failed = 0 }
    $incomplete = New-Object System.Collections.Generic.List[string]
    for ($n = 1; $n -le $MaxRuns; $n++) {
        $result = Invoke-OneRun
        $r = $result.Report
        if ($r.mode -ne $ExpectedMode) {
            throw "The job ran in mode '$($r.mode)', expected '$ExpectedMode'. Check $WritesSetting on the App Service."
        }
        $totals.roots += $r.rootsInRun
        foreach ($k in 'examined', 'alreadyCorrect', 'changed', 'wouldChange', 'refused', 'failed') { $totals[$k] += $r.$k }
        foreach ($i in @($r.incompleteRoots)) { if ($i) { $incomplete.Add($i) } }
        foreach ($c in @($r.changes)) {
            if ($c) {
                Write-Host ("  {0,-12} {1} {2} {3}  previous={4} target={5} {6}" -f $c.outcome, $c.root, $c.table, $c.id,
                    $c.previousOwner, $c.targetTeam, $c.detail)
            }
        }
        Write-Host ("Run {0}: {1} of {2} secure records, passComplete={3}" -f $n, $r.rootsInRun, $r.rootsTotal, $r.passComplete)
        if ($r.passComplete) {
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
