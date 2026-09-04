<#
.SYNOPSIS
    Retroactive dedup + key reactivation for the Compose save-identity alternate key
    (`sprk_graphitemid_uk` on `sprk_document`).

.DESCRIPTION
    Issue #781 item 3. The sibling `Verify-ComposeIdentityKey.ps1` DETECTS that the key is not Active;
    this script REPAIRS the data underneath it so the key can build, then reactivates it.

    WHY THIS EXISTS AS A REPEATABLE TOOL. On 2026-08-17 `spaarkedev1` had 105 duplicated
    `sprk_graphitemid` values across `sprk_document` (417 excess rows). A UNIQUE alternate key cannot
    build over non-unique data, so `sprk_graphitemid_uk` sat in `Failed` and every Compose save that
    routed through the FR-07(d) atomic upsert threw — surfacing to users as an opaque 500. The cleanup
    was performed by hand, once, and existed nowhere afterwards. Prod cannot be brought up on Compose
    without running the same repair, and "it was done by hand in dev six weeks ago" is not a procedure.

    WHAT IT DOES
      1. Pages the whole `sprk_document` table for non-empty `sprk_graphitemid` values and groups them.
      2. Reports every value carried by more than one row, with the row it would keep and the rows it
         would change.
      3. (with -Apply) Repairs each duplicate group, then reactivates the key, then re-verifies.

    THE CANONICAL RULE — active first, then OLDEST `createdon`, then lowest `sprk_documentid`.

    NOTE, and read this before "fixing" it: issue #781's suggested approach says "keeping newest". This
    script deliberately keeps the OLDEST, because the RUNTIME self-heal
    (`ComposeRecordResolution.ResolveDuplicatedDocumentByGraphItemIdAsync`) resolves a duplicated key to
    the oldest active row. The two MUST agree. If this tool kept the newest, it would strip the pointer
    from the row a live save had just written into, and the two mechanisms would fight. `createdon` is
    also the only stable term available — `modifiedon` moves whenever a row is touched, so a rule keyed
    on it gives different answers to different callers. If you change the rule here, change it there in
    the same commit.

    THE DEFAULT STRATEGY IS NON-DESTRUCTIVE. `ClearPointer` (default) blanks `sprk_graphitemid` on the
    losing rows: the unique value stops being duplicated so the key can build, and the rows keep their
    identity, associations and history. `Delete` is opt-in and removes them outright — only choose it
    for rows you have confirmed are junk, because a `sprk_document` row typically carries matter links,
    regarding references and activity history that nothing else reproduces.

    A losing row that has had its pointer cleared no longer resolves to its SPE file. That is a real,
    user-visible change, which is why this script reports before it writes, requires -Apply, and emits
    a machine-readable record of every row it touched.

.PARAMETER DataverseUrl
    Target Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Perform the repair. WITHOUT this switch the script only reports (safe to run against prod).

.PARAMETER Strategy
    ClearPointer (default) blanks `sprk_graphitemid` on losing rows. Delete removes them.

.PARAMETER ReactivateKey
    After a successful repair, POST ReactivateEntityKey and poll until the index reports Active.

.PARAMETER ReportPath
    Where to write the JSON record of what was found/changed. Default: a timestamped file in the
    current directory. The report is written in report-only mode too — that IS the deliverable when
    you are surveying an environment before a change window.

.EXAMPLE
    .\Repair-ComposeIdentityKey.ps1
    # Survey dev. Writes a report, changes nothing.

.EXAMPLE
    .\Repair-ComposeIdentityKey.ps1 -DataverseUrl 'https://spaarke-prod.crm.dynamics.com'
    # Survey prod before a change window. Safe.

.EXAMPLE
    .\Repair-ComposeIdentityKey.ps1 -Apply -ReactivateKey
    # Repair dev and bring the key back to Active.

.NOTES
    Exit codes: 0 = nothing to do, or repair succeeded; 1 = duplicates found and NOT repaired
    (report-only mode, so this is the expected code for a survey that found work); 2 = the run failed.
#>
[CmdletBinding()]
param(
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com',
    [switch]$Apply,
    [ValidateSet('ClearPointer', 'Delete')]
    [string]$Strategy = 'ClearPointer',
    [switch]$ReactivateKey,
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'

$ApiBase = "$DataverseUrl/api/data/v9.2"
$KeyLogicalName = 'sprk_graphitemid_uk'

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path (Get-Location) ("compose-identity-repair-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
}

Write-Host '========================================='
Write-Host 'Compose save-identity key REPAIR'
Write-Host "Target:   $DataverseUrl"
Write-Host "Mode:     $(if ($Apply) { "APPLY ($Strategy)" } else { 'REPORT ONLY (no writes)' })"
Write-Host "Report:   $ReportPath"
Write-Host '========================================='

function Get-Headers {
    $token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
    if ([string]::IsNullOrEmpty($token)) { throw 'Failed to get access token. Run: az login' }
    return @{
        Authorization      = "Bearer $token"
        Accept             = 'application/json'
        'OData-Version'    = '4.0'
        'OData-MaxVersion' = '4.0'
        'Content-Type'     = 'application/json'
    }
}

try {
    $headers = Get-Headers

    # ── 1. Page the table ────────────────────────────────────────────────────────────────────────
    # `$top` does not combine with server-driven paging, so page via @odata.nextLink and let the
    # server pick the page size. An environment with hundreds of thousands of documents will take a
    # few minutes here; that is the honest cost of surveying the whole table, and this runs rarely.
    Write-Host 'Reading sprk_document (paged)...'
    $rows = [System.Collections.Generic.List[object]]::new()
    $uri = "$ApiBase/sprk_documents?`$select=sprk_documentid,sprk_graphitemid,statecode,createdon" +
           "&`$filter=sprk_graphitemid ne null"
    $page = 0
    while ($uri) {
        $resp = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        foreach ($r in $resp.value) { $rows.Add($r) | Out-Null }
        $page++
        Write-Host ("  page {0}: {1} rows (running total {2})" -f $page, $resp.value.Count, $rows.Count)
        $uri = $resp.'@odata.nextLink'
    }
    Write-Host ("Read {0} rows carrying a sprk_graphitemid." -f $rows.Count)

    # ── 2. Group and select canonicals ───────────────────────────────────────────────────────────
    # Ordering MUST match ComposeRecordResolution.ResolveDuplicatedDocumentByGraphItemIdAsync:
    # active(statecode 0) first, then oldest createdon, then lowest id. See the .DESCRIPTION note.
    $groups = $rows | Group-Object -Property sprk_graphitemid | Where-Object { $_.Count -gt 1 }

    # @(...) so an environment with zero duplicates yields an empty ARRAY, not $null — .Count on the
    # latter is a silent trap that would make "no duplicates" and "one duplicate" behave alike.
    $plan = @(foreach ($g in $groups) {
        $ordered = $g.Group | Sort-Object `
            @{ Expression = { if ($_.statecode -eq 0) { 0 } else { 1 } } }, `
            @{ Expression = { [datetime]$_.createdon } }, `
            @{ Expression = { [string]$_.sprk_documentid } }
        [pscustomobject]@{
            GraphItemId = $g.Name
            RowCount    = $g.Count
            KeepId      = $ordered[0].sprk_documentid
            KeepState   = $ordered[0].statecode
            KeepCreated = $ordered[0].createdon
            RepairIds   = @($ordered | Select-Object -Skip 1 | ForEach-Object { $_.sprk_documentid })
        }
    })

    $excess = if ($plan.Count -eq 0) { 0 } else { ($plan | Measure-Object -Property RowCount -Sum).Sum - $plan.Count }

    Write-Host ''
    Write-Host ("Duplicated sprk_graphitemid values: {0}" -f $plan.Count)
    Write-Host ("Excess rows:                        {0}" -f $excess)

    foreach ($p in ($plan | Select-Object -First 20)) {
        Write-Host ("  {0}  x{1}  keep {2}  repair {3}" -f `
            $p.GraphItemId, $p.RowCount, $p.KeepId, ($p.RepairIds -join ', '))
    }
    if ($plan.Count -gt 20) { Write-Host ("  ... and {0} more (see the report)" -f ($plan.Count - 20)) }

    $report = [pscustomobject]@{
        RanAtUtc       = (Get-Date).ToUniversalTime().ToString('o')
        DataverseUrl   = $DataverseUrl
        Mode           = $(if ($Apply) { "apply/$Strategy" } else { 'report-only' })
        RowsInspected  = $rows.Count
        DuplicateKeys  = $plan.Count
        ExcessRows     = $excess
        CanonicalRule  = 'active first, then oldest createdon, then lowest sprk_documentid (matches the runtime self-heal)'
        Groups         = $plan
        Applied        = @()
        Failures       = @()
    }

    if ($plan.Count -eq 0) {
        Write-Host ''
        Write-Host 'No duplicates. The data does not block the key.' -ForegroundColor Green
        # Clean data is NOT the same as a healthy key: the index can still be sitting in Failed from an
        # earlier duplication that has since been cleaned by hand. So fall through to reactivation when
        # it was asked for, and only exit early when there is genuinely nothing left to do.
        if (-not ($Apply -and $ReactivateKey)) {
            $report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding utf8
            Write-Host "Report written: $ReportPath"
            exit 0
        }
    }

    if (-not $Apply) {
        $report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding utf8
        Write-Host ''
        Write-Warning 'REPORT ONLY - nothing was changed.'
        Write-Host "Review $ReportPath, then re-run with -Apply (and -ReactivateKey) to repair." -ForegroundColor Yellow
        exit 1
    }

    # ── 3. Repair ────────────────────────────────────────────────────────────────────────────────
    Write-Host ''
    Write-Host ("Repairing {0} rows using strategy '{1}'..." -f $excess, $Strategy)
    $applied = [System.Collections.Generic.List[object]]::new()
    $failures = [System.Collections.Generic.List[object]]::new()

    foreach ($p in $plan) {
        foreach ($id in $p.RepairIds) {
            try {
                if ($Strategy -eq 'Delete') {
                    Invoke-RestMethod -Uri "$ApiBase/sprk_documents($id)" -Headers $headers -Method Delete | Out-Null
                }
                else {
                    $body = @{ sprk_graphitemid = $null } | ConvertTo-Json
                    Invoke-RestMethod -Uri "$ApiBase/sprk_documents($id)" -Headers $headers -Method Patch -Body $body | Out-Null
                }
                $applied.Add([pscustomobject]@{ GraphItemId = $p.GraphItemId; RowId = $id; Action = $Strategy; KeptId = $p.KeepId }) | Out-Null
            }
            catch {
                # Keep going. One row that refuses (a plugin, a lock, a cascade restriction) must not
                # abandon the other 416 — and the failure list is what the operator retries.
                $failures.Add([pscustomobject]@{ GraphItemId = $p.GraphItemId; RowId = $id; Error = $_.Exception.Message }) | Out-Null
                Write-Warning ("  row {0}: {1}" -f $id, $_.Exception.Message)
            }
        }
    }

    $report.Applied = $applied
    $report.Failures = $failures
    Write-Host ("Repaired {0} rows; {1} failed." -f $applied.Count, $failures.Count)

    if ($failures.Count -gt 0) {
        $report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding utf8
        Write-Warning 'Some rows could not be repaired - the key will NOT build while duplicates remain.'
        Write-Host "Retry the RowIds listed in $ReportPath, then re-run." -ForegroundColor Yellow
        exit 1
    }

    # ── 4. Reactivate ────────────────────────────────────────────────────────────────────────────
    if ($ReactivateKey) {
        Write-Host ''
        Write-Host 'Reactivating sprk_graphitemid_uk...'
        $body = @{ EntityLogicalName = 'sprk_document'; EntityKeyLogicalName = $KeyLogicalName } | ConvertTo-Json
        Invoke-RestMethod -Uri "$ApiBase/ReactivateEntityKey" -Headers $headers -Method Post -Body $body | Out-Null

        # The rebuild is ASYNC. Poll rather than declaring success on the POST returning 204 - a
        # reactivation that is accepted and then fails to build is exactly the state that produced the
        # original outage, and it looks identical from the POST.
        $statusUri = "$ApiBase/EntityDefinitions(LogicalName='sprk_document')/Keys" +
                     "?`$select=LogicalName,EntityKeyIndexStatus&`$filter=LogicalName eq '$KeyLogicalName'"
        $status = $null
        for ($i = 1; $i -le 30; $i++) {
            Start-Sleep -Seconds 10
            $status = (Invoke-RestMethod -Uri $statusUri -Headers $headers -Method Get).value |
                      Select-Object -First 1 -ExpandProperty EntityKeyIndexStatus
            Write-Host ("  attempt {0}: {1}" -f $i, $status)
            if ($status -eq 'Active' -or $status -eq 'Failed') { break }
        }

        $report | Add-Member -NotePropertyName KeyStatusAfter -NotePropertyValue $status
        $report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding utf8

        if ($status -ne 'Active') {
            Write-Warning "sprk_graphitemid_uk = $status after reactivation. Compose saves remain degraded."
            Write-Host 'Re-run this script in report mode to find the duplicates that are still blocking it.' -ForegroundColor Yellow
            exit 1
        }
        Write-Host 'sprk_graphitemid_uk = Active' -ForegroundColor Green
    }

    $report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding utf8
    Write-Host ''
    Write-Host "OK - repair complete. Report written: $ReportPath" -ForegroundColor Green
    Write-Host 'Confirm with: .\Verify-ComposeIdentityKey.ps1' -ForegroundColor Green
    exit 0
}
catch {
    Write-Warning "Repair failed to run: $($_.Exception.Message)"
    exit 2
}
