#!/usr/bin/env pwsh
<#
.SYNOPSIS
    The LEGACY MIGRATION of document files into the container derived for their record (unified-access-control-r2 task
    166 f1; owner round 21 item 1 (ii), round 26 item 3): drives the BFF's `document-container-migration` job — dry run
    (report-only) by default, -Apply to move files, -Verify to prove the strict document-pointer rule may be switched on.

.DESCRIPTION
    Documents uploaded before task 076 live in the UPLOADER's business-unit container, not the one derived for their
    record (live dev, 2026-10-04: 447 of 530). Round 21 decided the strict download check — a document's file must be in
    the container derived for its own record — once those files are moved. Round 26 item 3 made the move ONE BFF service,
    DocumentContainerRelocator (shared with Make Secure), which for each misplaced file: copies it into the derived
    container through the BFF identity, verifies the copy (size, and quickXorHash where Graph returns one), re-points the
    sprk_document through the pointer-attach path, and only then deletes the source — never before the copy is verified,
    and never while another document still points at it. Each step is logged with before / after ids
    ('[DOCUMENT-RELOCATE] copied: / re-pointed: / source deleted: / source KEPT:').

    THIS SCRIPT DOES NOT DECIDE OR MOVE ANYTHING (148's Invoke-SecureChildBackfill.ps1 precedent). It only:
      - triggers the job through POST /api/admin/jobs/document-container-migration/trigger (SystemAdmin policy),
      - polls /history for each run's ResultJson and repeats until a run reports passComplete,
      - saves each run's report, prints the listed documents (state, interim / strict answers, source and target ids),
        and totals the pass;
      - for -Apply only: turns the App Service setting DocumentContainerMigration__WritesEnabled on for the run and OFF
        again afterwards (an app-setting change restarts the app; the script waits for /healthz).

    WHAT A RUN REPORTS, per document with a file:
      InPlace           already in its derived container (healthy; counted, not listed);
      WouldRelocate     report-only: misplaced, verifiably the row's own, and WOULD be moved;
      Relocated         moved (copy verified, row re-pointed, source deleted);
      RelocatedSourceKept moved and re-pointed; the source was kept because another document still points at it;
      SourceUnverified  misplaced, but not verifiably the row's own file (another person's upload, or a container of no
                        business unit) — NEVER copied; refused by both rules; an administrator repairs or re-files it;
      Undecidable       its container cannot be derived (an unreadable record, two secure records) — fix the filing;
      FileMissing       the item the row names does not exist;
      Failed            a copy / verify / re-point step failed — the row is unchanged; re-run.
    and, for every document, whether the INTERIM rule (in force) serves it and whether the STRICT rule would:
    wouldNewlyRefuse counts documents the interim rule serves that the strict rule would refuse — exactly the documents
    flipping the flag would break.

    SAFETY MODEL
      - Dry run is the DEFAULT: with neither -Apply nor -Verify the job runs report-only and writes nothing.
      - -Apply needs -ResourceGroup and -AppName, and asks for confirmation (use -Confirm:$false in automation). The setting
        is removed again in a finally block, whatever happens.
      - -Verify runs report-only and exits 0 ONLY when a FULL pass plans no move (WouldRelocate 0), has no failure, and
        would newly refuse nothing (wouldNewlyRefuse 0). Only then may DocumentPointer__StrictDerivedContainer be set to
        true. A pass counts from a run that began at the first document (startAfter null) and whose runs are contiguous
        (each began where the previous ended) — otherwise the script throws instead of passing on part of the list.
      - Idempotent and resumable: a moved file is InPlace on the next run; a re-run completes what an interrupted one
        left.

    ⚠️ Run against a single-instance app (dev), or scale to one instance: the job's place in the list (its cursor) is per
    instance. The job is registered disabled, so no scheduled tick moves its place mid-pass.

.PARAMETER BffBaseUrl
    The BFF's base URL, e.g. https://spe-api-dev-67e2xz.azurewebsites.net

.PARAMETER ApiScope
    The scope of a token for the BFF API, e.g. api://<bff-app-id>/.default. The token is minted with `az account
    get-access-token --scope`, so the Azure CLI login must be a user the BFF's SystemAdmin policy admits.

.PARAMETER Apply
    Move files. Requires -ResourceGroup and -AppName.

.PARAMETER Verify
    Report-only; exit 0 only when the strict rule may be switched on.

.PARAMETER ResourceGroup
    The BFF App Service's resource group (-Apply only).

.PARAMETER AppName
    The BFF App Service's name (-Apply only).

.PARAMETER MaxRuns
    Safety cap on the number of job runs (default 200).

.PARAMETER OutputDirectory
    Where the per-run reports go (default .\document-container-migration-<timestamp>).

.EXAMPLE
    .\Invoke-DocumentContainerMigration.ps1 -BffBaseUrl https://spe-api-dev-67e2xz.azurewebsites.net -ApiScope api://<id>/.default

.EXAMPLE
    .\Invoke-DocumentContainerMigration.ps1 -BffBaseUrl https://spe-api-dev-67e2xz.azurewebsites.net -ApiScope api://<id>/.default `
        -Apply -ResourceGroup spe-infrastructure-westus2 -AppName spe-api-dev-67e2xz

.EXAMPLE
    .\Invoke-DocumentContainerMigration.ps1 -BffBaseUrl https://spe-api-dev-67e2xz.azurewebsites.net -ApiScope api://<id>/.default -Verify

.NOTES
    Task note: projects/unified-access-control-r2/notes/task-166-memory-compose-remaining-route-authorization.md §20.
    Live runs are manual gates (the main session runs them; -Apply only after the FLS lock, §20 live order).
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

$JobId = 'document-container-migration'
$WritesSetting = 'DocumentContainerMigration__WritesEnabled'
$States = @('InPlace', 'WouldRelocate', 'Relocated', 'RelocatedSourceKept', 'SourceUnverified', 'Undecidable', 'FileMissing', 'Failed', 'NoFile')
$base = $BffBaseUrl.TrimEnd('/')
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path (Get-Location) ("document-container-migration-{0:yyyyMMdd-HHmmss}" -f (Get-Date))
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
    for ($i = 0; $i -lt 720; $i++) {
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
    throw "Run $runId did not finish within 60 minutes."
}

function Invoke-Pass([string] $ExpectedMode) {
    $totals = [ordered]@{ examined = 0; wouldNewlyRefuse = 0; refusedByBoth = 0; rowsTotal = 0; rowsListed = 0 }
    foreach ($s in $States) { $totals[$s] = 0 }
    $covering = $false
    $lastEnd = $null
    for ($n = 1; $n -le $MaxRuns; $n++) {
        $result = Invoke-OneRun
        $r = $result.Report
        if ($r.mode -ne $ExpectedMode) {
            throw "The job ran in mode '$($r.mode)', expected '$ExpectedMode'. Check $WritesSetting on the App Service."
        }
        foreach ($required in 'startAfter', 'endAt', 'passComplete', 'wouldNewlyRefuse', 'counts', 'rowsTotal') {
            if ($null -eq $r.PSObject.Properties[$required]) {
                throw "Run $n's report has no $($required): the deployed BFF predates this script. Deploy it first."
            }
        }
        if (-not $covering) {
            if ($null -ne $r.startAfter) {
                Write-Host ("Run {0}: began after document {1} — the tail of an earlier pass; not counted." -f $n, $r.startAfter)
                continue
            }
            $covering = $true
        } elseif ("$($r.startAfter)" -ne "$lastEnd") {
            throw ("Run {0} began after document {1}, but the pass had reached {2}: another trigger of the job moved its " +
                "place during the pass. Run the script again.") -f $n, $r.startAfter, $lastEnd
        }
        $lastEnd = $r.endAt
        $totals.examined += $r.examined
        $totals.wouldNewlyRefuse += $r.wouldNewlyRefuse
        $totals.refusedByBoth += $r.refusedByBoth
        $totals.rowsTotal += $r.rowsTotal
        $totals.rowsListed += $r.rowsListed
        foreach ($s in $States) {
            if ($r.counts.PSObject.Properties[$s]) { $totals[$s] += $r.counts.$s }
        }
        if ($r.rowsTotal -gt $r.rowsListed) {
            Write-Warning ("Run {0} has {1} documents to report but lists {2}; the complete list is the BFF log " +
                "('[DOCUMENT-RELOCATE]' / '[DOCUMENT-POINTER]'), or lower DocumentContainerMigration__MaxDocumentsPerRun." -f
                $n, $r.rowsTotal, $r.rowsListed)
        }
        foreach ($row in @($r.rows)) {
            if ($row) {
                Write-Host ("  {0,-20} {1}  interim={2} strict={3}{4}  {5}/{6} -> {7}/{8}  {9}" -f $row.state, $row.documentId,
                    $row.interim, $row.strict, $(if ($row.wouldNewlyRefuse) { ' NEWLY-REFUSED' } else { '' }),
                    $row.sourceDrive, $row.sourceItem, $row.targetDrive, $row.targetItem, $row.detail)
            }
        }
        Write-Host ("Run {0}: {1} documents (after {2} … {3}), passComplete={4}" -f $n, $r.examined, $r.startAfter, $r.endAt, $r.passComplete)
        if ($r.passComplete) {
            return $totals
        }
    }
    throw "The pass did not complete within $MaxRuns runs (raise -MaxRuns or DocumentContainerMigration__MaxDocumentsPerRun)."
}

if ($Apply) {
    if (-not $PSCmdlet.ShouldProcess("$AppName", "Enable $WritesSetting, move every misplaced document file into its derived container, then disable it")) {
        return
    }
    try {
        az webapp config appsettings set --resource-group $ResourceGroup --name $AppName --settings "$WritesSetting=true" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not set $WritesSetting." }
        Wait-Healthy
        $t = Invoke-Pass 'write'
    } finally {
        az webapp config appsettings delete --resource-group $ResourceGroup --name $AppName --setting-names $WritesSetting | Out-Null
        Wait-Healthy
    }
} else {
    $t = Invoke-Pass 'report-only'
}

Write-Host ""
Write-Host ("Documents: {0}  " -f $t.examined) -NoNewline
Write-Host (($States | ForEach-Object { "{0}: {1}" -f $_, $t[$_] }) -join '  ')
Write-Host ("Would be newly refused by the strict rule: {0}   refused by both rules: {1}" -f $t.wouldNewlyRefuse, $t.refusedByBoth)
if ($t.SourceUnverified -gt 0 -or $t.FileMissing -gt 0) {
    Write-Warning ("{0} document(s) are not verifiably their row's own file and {1} name a missing item: both rules refuse " +
        "them today; they are listed above for an administrator to repair or re-file. The migration never copies them." -f
        $t.SourceUnverified, $t.FileMissing)
}
Write-Host "Reports: $OutputDirectory"

if ($Verify) {
    if ($t.WouldRelocate -eq 0 -and $t.Failed -eq 0 -and $t.wouldNewlyRefuse -eq 0) {
        Write-Host "VERIFY: PASS — nothing left to move, and the strict rule refuses no document the interim rule serves. DocumentPointer__StrictDerivedContainer may be set to true." -ForegroundColor Green
        exit 0
    }
    Write-Host ("VERIFY: FAIL — {0} planned move(s), {1} failure(s), {2} document(s) the strict rule would newly refuse (see above)." -f
        $t.WouldRelocate, $t.Failed, $t.wouldNewlyRefuse) -ForegroundColor Red
    exit 1
}

if ($t.Failed -gt 0) { exit 1 }
exit 0
