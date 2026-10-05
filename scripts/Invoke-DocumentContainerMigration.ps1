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
    sprk_document through the pointer-attach path (re-keying the row's own columns that held the old ids, recording the
    replayed versions' original authorship in sprk_relocatedversions and what the move still owes — with the source's
    witness — in the row's relocation ledger, sprk_relocationpending, all in the same update), and then settles the rest:
    the rows holding the old item for this document are re-keyed (a child attachment's sprk_parentgraphitemid, its
    communication attachment row), the new item is indexed and the old item's chunks removed (owner round 37 item 1), and
    the source is deleted — never before the copy is verified, never while a row still uses it, and only while it still
    matches the witness recorded at verification (owner round 45 item 4). The copy carries the source's version history
    (round 45 item 1: replayed oldest first; a target version limit that truncates it is listed under versionsTruncated). A
    row of ANOTHER record that uses the same file keeps it (it is that record's file; listed under
    sourceKeptForOtherRecords); a row that belongs with the moved file is moved along with its own copy (round 37 item 2);
    a communication's own attachment record goes by the subtree of its communication's record (round 45 item 3). A source
    EDITED after its move is listed under sourceChangedAfterMove and closed by the relocation itself — re-copy, verify,
    re-point — never by a manual step. Whatever cannot be finished stays in the ledger and the NEXT run settles it (round
    37 / F2). Each step is logged with before / after ids
    ('[DOCUMENT-RELOCATE] copied: / re-pointed: / re-keyed: / index re-keyed: / source deleted: / source KEPT for other
    records: / moved along:').

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
      Relocated         moved (copy verified, row re-pointed, references and index re-keyed, source deleted);
      RelocatedSourceKeptForOtherRecords  moved and settled; the source is kept because rows of OTHER records still use
                        it (their file; listed under sourceKeptForOtherRecords) — complete;
      RelocationPending re-pointed (now or by an earlier run) but a step is still owed — a source delete, a re-key, or the
                        index (the row's 'pending' lines say which); the next -Apply run settles it;
      SourceUnverified  misplaced, but not verifiably the row's own file (another person's upload, or a container of no
                        business unit) — NEVER copied; refused by both rules; an administrator repairs or re-files it;
      Undecidable       its container cannot be derived (an unreadable record, two secure records) — fix the filing;
      FileMissing       the item the row names does not exist;
      Failed            a copy / verify / re-point step failed — the row is unchanged; re-run.
    and, for every document, whether the INTERIM rule (in force) serves it and whether the STRICT rule would:
    wouldNewlyRefuse counts documents the interim rule serves that the strict rule would refuse — exactly the documents
    flipping the flag would break; servedOnlyAfterFlip counts the opposite (the census classes the interim rule refuses by
    design); relocatedButRefused counts relocated files the rule in force refuses (owner round 37 item 3 makes every
    relocated file servable before the flip — any here is a defect); pending counts relocations still owing a step.

    SAFETY MODEL
      - Dry run is the DEFAULT: with neither -Apply nor -Verify the job runs report-only and writes nothing.
      - -Apply needs -ResourceGroup and -AppName, and asks for confirmation (use -Confirm:$false in automation). The setting
        is removed again in a finally block, whatever happens.
      - -Verify runs report-only and exits 0 ONLY when a FULL pass plans no move (WouldRelocate 0), has no failure, owes
        nothing (RelocationPending 0, pending 0), refuses no relocated file (relocatedButRefused 0), and would newly refuse
        nothing (wouldNewlyRefuse 0). Only then may DocumentPointer__StrictDerivedContainer be set to
        true. A pass counts from a run that began at the first document (startAfter null) and whose runs are contiguous
        (each began where the previous ended) — otherwise the script throws instead of passing on part of the list.
      - Idempotent and resumable: a moved file is InPlace on the next run; a re-run settles what an interrupted one
        left owing (the row's relocation ledger).

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
$States = @('InPlace', 'WouldRelocate', 'Relocated', 'RelocatedSourceKeptForOtherRecords', 'RelocationPending', 'SourceUnverified', 'Undecidable', 'FileMissing', 'Failed', 'NoFile')
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
    $totals = [ordered]@{ examined = 0; wouldNewlyRefuse = 0; refusedByBoth = 0; servedOnlyAfterFlip = 0; relocatedButRefused = 0; pending = 0; movedAlong = 0; keptForOtherRecords = 0; sourceChangedAfterMove = 0; versionsTruncated = 0; rowsTotal = 0; rowsListed = 0 }
    foreach ($s in $States) { $totals[$s] = 0 }
    $covering = $false
    $lastEnd = $null
    for ($n = 1; $n -le $MaxRuns; $n++) {
        $result = Invoke-OneRun
        $r = $result.Report
        if ($r.mode -ne $ExpectedMode) {
            throw "The job ran in mode '$($r.mode)', expected '$ExpectedMode'. Check $WritesSetting on the App Service."
        }
        foreach ($required in 'startAfter', 'endAt', 'passComplete', 'wouldNewlyRefuse', 'servedOnlyAfterFlip', 'relocatedButRefused', 'pending', 'counts', 'rowsTotal') {
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
        $totals.servedOnlyAfterFlip += $r.servedOnlyAfterFlip
        $totals.relocatedButRefused += $r.relocatedButRefused
        $totals.pending += $r.pending
        $totals.movedAlong += $r.movedAlong
        $totals.keptForOtherRecords += @($r.sourceKeptForOtherRecords).Count
        if ($null -ne $r.PSObject.Properties['sourceChangedAfterMove']) { $totals.sourceChangedAfterMove += $r.sourceChangedAfterMove }
        if ($null -ne $r.PSObject.Properties['versionsTruncated']) { $totals.versionsTruncated += $r.versionsTruncated }
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
                Write-Host ("  {0,-20} {1}  interim={2} strict={3}{4}{5}  {6}/{7} -> {8}/{9}  {10}" -f $row.state, $row.documentId,
                    $row.interim, $row.strict, $(if ($row.wouldNewlyRefuse) { ' NEWLY-REFUSED' } else { '' }),
                    $(if ($row.relocatedButRefused) { ' RELOCATED-BUT-REFUSED' } else { '' }),
                    $row.sourceDrive, $row.sourceItem, $row.targetDrive, $row.targetItem, $row.detail)
                foreach ($owed in @($row.pending)) { if ($owed) { Write-Host "      owes: $owed" } }
            }
        }
        foreach ($kept in @($r.sourceKeptForOtherRecords)) {
            if ($kept) {
                Write-Host ("  KEPT for other records: {0}/{1} (document {2} moved away) used by {3}" -f $kept.sourceDrive,
                    $kept.sourceItem, $kept.documentId, (@($kept.keptFor) -join ', '))
            }
        }
        foreach ($changed in @($r.sourceChangedAfterMoveRows)) {
            if ($changed) {
                Write-Host ("  SOURCE CHANGED AFTER MOVE: {0}/{1} (document {2}) — {3}" -f $changed.sourceDrive, $changed.sourceItem,
                    $changed.documentId, $(if ($changed.newItem) { "re-copied with $($changed.carriedVersions) later version(s) into $($changed.newItem); " + $(if ($changed.editIsCurrent) { 'the edit is current (its author may write the document)' } else { "the edit is in the history only (its author may not write the document); the document's own content stays current" }) } else { 'not closed yet (see its pending line); the next -Apply run re-copies it' }))
            }
        }
        foreach ($truncated in @($r.versionsTruncatedRows)) {
            if ($truncated) {
                Write-Host ("  VERSIONS TRUNCATED: document {0} copy {1} keeps {2} of {3} replayed versions (the target's version limit); {4} oldest author(s) unrecorded" -f
                    $truncated.documentId, $truncated.item, $truncated.keptVersions, $truncated.replayedVersions, $truncated.unrecordedAuthors)
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
Write-Host ("Would be newly refused by the strict rule: {0}   refused by both rules: {1}   served only after the flip: {2}" -f
    $t.wouldNewlyRefuse, $t.refusedByBoth, $t.servedOnlyAfterFlip)
Write-Host ("Relocations still owing a step: {0}   relocated files the rule in force refuses: {1}   rows moved along: {2}   sources kept for other records: {3}" -f
    $t.pending, $t.relocatedButRefused, $t.movedAlong, $t.keptForOtherRecords)
Write-Host ("Sources edited after their move (re-copied by the relocation; any not closed yet is in 'still owing'): {0}   histories truncated by a target's version limit (stated): {1}" -f
    $t.sourceChangedAfterMove, $t.versionsTruncated)
if ($t.SourceUnverified -gt 0 -or $t.FileMissing -gt 0) {
    Write-Warning ("{0} document(s) are not verifiably their row's own file and {1} name a missing item: both rules refuse " +
        "them today; they are listed above for an administrator to repair or re-file. The migration never copies them." -f
        $t.SourceUnverified, $t.FileMissing)
}
Write-Host "Reports: $OutputDirectory"

if ($Verify) {
    if ($t.WouldRelocate -eq 0 -and $t.Failed -eq 0 -and $t.wouldNewlyRefuse -eq 0 -and $t.RelocationPending -eq 0 -and
        $t.pending -eq 0 -and $t.relocatedButRefused -eq 0) {
        Write-Host "VERIFY: PASS — nothing left to move or settle, every relocated file is served, and the strict rule refuses no document the interim rule serves. DocumentPointer__StrictDerivedContainer may be set to true." -ForegroundColor Green
        exit 0
    }
    Write-Host ("VERIFY: FAIL — {0} planned move(s), {1} failure(s), {2} relocation(s) still owing a step, {3} relocated file(s) refused, {4} document(s) the strict rule would newly refuse (see above)." -f
        $t.WouldRelocate, $t.Failed, [Math]::Max($t.RelocationPending, $t.pending), $t.relocatedButRefused, $t.wouldNewlyRefuse) -ForegroundColor Red
    exit 1
}

if ($t.Failed -gt 0 -or $t.pending -gt 0 -or $t.relocatedButRefused -gt 0) { exit 1 }
exit 0
