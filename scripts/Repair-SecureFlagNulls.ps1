#!/usr/bin/env pwsh
<#
.SYNOPSIS
    One-time cleanup: every NULL sprk_issecure becomes No (false) — on sprk_project, sprk_matter, sprk_workassignment AND every other entity carrying the column (live dev: sprk_invoice),
    and the column's default is No (unified-access-control-r2 task 150; owner decision Q1, 2026-10-01).
    Dry run by default; -Apply writes; -Verify checks. Idempotent. Records before/after counts and every record id.

.DESCRIPTION
    WHY. Task 150 makes an ABSENT sprk_issecure FAIL CLOSED in the BFF: RecordContainerResolver refuses the upload
    (secure_flag_unreadable) and the unsecure endpoint refuses rather than answering "already not secure". That is only
    correct once no row legitimately holds NULL — then "absent" can only mean the field-secured value was masked from
    the reader. Live dev 2026-10-02 (read-only): 9 projects, 18 matters and 11 work assignments carry NULL, every one
    created before the column existed (newest NULL row 2026-03-15; the column was created 2026-03-17); the column's
    default is already No, so no new row is created NULL.

    ⚠️ DEPLOY ORDER. Run this — and see -Verify exit 0 — BEFORE deploying a BFF that carries task 150 to the
    environment. Until it has run, every upload to one of those rows (and to any child filed under one) is refused.

    What it does, per table:
      (a) DEFAULT   reports the column's DefaultValue; -Apply sets it to false when it is not (it ships in the solution
                    with the attribute's metadata, so a new environment gets it).
      (b) BACKFILL  lists every row whose sprk_issecure is NULL (every page, past Dataverse's 5,000-row page);
                    -Apply PATCHes each one to false (If-Match: * — never an upsert), then re-counts.
      (c) REPORT    writes a JSON report (-ReportPath): mode, timestamps, and per table the default value, the before
                    count, every record id, and the after count.
    Nothing else is written. The PATCH changes only sprk_issecure (modifiedon/modifiedby move, as for any update).

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER ReportPath
    Where the JSON report is written. Default: .\secure-flag-null-backfill-<yyyyMMdd-HHmmss>.json

.PARAMETER Apply
    Write mode. Without it the script never writes (except the report file).

.PARAMETER Verify
    Read-only check with a pass/fail exit code: 0 NULL rows and DefaultValue false on every table.

.EXAMPLE
    .\Repair-SecureFlagNulls.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: the default value, the NULL count and every NULL record id per table; zero writes.

.EXAMPLE
    .\Repair-SecureFlagNulls.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply

.EXAMPLE
    .\Repair-SecureFlagNulls.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify

.NOTES
    unified-access-control-r2 task 150 (#1067). Code: RecordContainerResolver.SecureFlagUnreadableCode,
    UnsecureProjectEndpoint.ReasonSecureFlagUnreadable. Companion: scripts/Set-SecureFlagFieldSecurity.ps1 (the lock,
    which refuses -Apply while any NULL remains). Auth: the operator's own az CLI identity. No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [string]$ReportPath = (Join-Path (Get-Location) ("secure-flag-null-backfill-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (SecurableEntityRegistry.SecureFlagAttribute; the three secure roots and the invoice, all four locked by ──
# scripts/Set-SecureFlagFieldSecurity.ps1, whose (p4) refuses -Apply while any of them holds a NULL) ───────────────────
$Column = 'sprk_issecure'
$Tables = @('sprk_project', 'sprk_matter', 'sprk_workassignment', 'sprk_invoice')

$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
function Get-DvAll([string]$Path) {
    $h = $headers.Clone(); $h['Prefer'] = 'odata.maxpagesize=5000'
    $rows = [System.Collections.Generic.List[object]]::new(); $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers $h -Method Get
        foreach ($r in @($page.value)) { $rows.Add($r) }
        $next = $page.'@odata.nextLink'
    }
    , $rows
}

# Every OTHER entity carrying the column is covered too (found by the task 150 review, live 2026-10-02: sprk_invoice
# carries sprk_issecure — 4 of 10 rows NULL; it is now in $Tables). The BFF's SecurableEntityRegistry is metadata-driven,
# so it treats every carrier as securable — except sprk_invoice, whose flag the owner ruled is not a security input
# (round 10 item 11: an invoice follows its matter) — and an ABSENT flag on any securable carrier refuses uploads.
$carriers = @((Invoke-DvGet "EntityDefinitions?`$select=LogicalName&`$filter=IsCustomEntity eq true&`$expand=Attributes(`$select=LogicalName;`$filter=LogicalName eq '$Column')").value |
    Where-Object { @($_.Attributes).Count -gt 0 } | ForEach-Object LogicalName)
$extra = @($carriers | Where-Object { $_ -notin $Tables } | Sort-Object)
$Tables = @($Tables) + $extra

$IsDryRun = -not $Apply.IsPresent
$gaps = [System.Collections.Generic.List[string]]::new()
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))
Write-Host ("Tables      : {0}{1}" -f ($Tables -join ', '), $(if ($extra.Count) { "  (discovered beyond the four tables: $($extra -join ', '))" } else { '' }))

$report = [ordered]@{
    environment = $EnvironmentUrl
    organization = $org
    mode = $(if ($Verify) { 'verify' } elseif ($IsDryRun) { 'dry-run' } else { 'apply' })
    startedUtc = (Get-Date).ToUniversalTime().ToString('o')
    column = $Column
    tables = [ordered]@{}
}

foreach ($t in $Tables) {
    Write-Host "`n$t.$Column"
    $meta = Invoke-DvGet "EntityDefinitions(LogicalName='$t')?`$select=EntitySetName,PrimaryIdAttribute"
    $set = $meta.EntitySetName; $idColumn = $meta.PrimaryIdAttribute
    $attrPath = "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
    $attr = Invoke-DvGet "$attrPath`?`$select=LogicalName,DefaultValue,IsSecured"
    $entry = [ordered]@{ defaultValueBefore = $attr.DefaultValue; isSecured = $attr.IsSecured }

    # (a) DEFAULT
    if ($attr.DefaultValue -eq $false) { Report 'OK' "default is No" }
    elseif ($Verify) { Report 'FAIL' "default is '$($attr.DefaultValue)', not No — a new row can be created without a value" }
    elseif ($IsDryRun) { Report 'WOULD' "set the default to No (it is '$($attr.DefaultValue)')" }
    else {
        $typed = Invoke-DvGet "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        $typed.DefaultValue = $false
        $h = $headers.Clone(); $h['MSCRM.MergeLabels'] = 'true'
        Invoke-RestMethod -Uri "$Api/EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$Column')" -Headers $h -Method Put -Body ($typed | ConvertTo-Json -Depth 20 -Compress) | Out-Null
        Report 'DONE' 'set the default to No'
    }

    # (b) BACKFILL
    $nulls = Get-DvAll "$set`?`$select=$idColumn&`$filter=$Column eq null"
    $ids = @($nulls | ForEach-Object { $_.$idColumn })
    $entry.nullBefore = $ids.Count
    $entry.recordIds = $ids
    if ($ids.Count -eq 0) { Report 'OK' 'no row holds NULL' }
    elseif ($Verify) { Report 'FAIL' "$($ids.Count) row(s) hold NULL — run with -Apply (the BFF refuses uploads to them)" }
    elseif ($IsDryRun) { Report 'WOULD' "set $($ids.Count) NULL row(s) to No: $($ids -join ', ')" }
    else {
        $h = $headers.Clone(); $h['If-Match'] = '*'
        $failed = 0
        foreach ($id in $ids) {
            try {
                Invoke-RestMethod -Uri "$Api/$set($id)" -Headers $h -Method Patch -Body (@{ $Column = $false } | ConvertTo-Json -Compress) | Out-Null
            } catch {
                $failed++
                Report 'FAIL' "could not set $t $id to No: $($_.Exception.Message)"
            }
        }
        Report 'DONE' "set $($ids.Count - $failed) of $($ids.Count) NULL row(s) to No"
    }

    # Get-DvAll emits its List as ONE object (`, $rows`), so wrapping the call in @(...) made a one-element array and
    # .Count was always 1 — every -Apply then reported "1 row(s) still hold NULL" per table and exited 1, even after a
    # full repair (seen live at G-0 in dev, 2026-10-03). Count the List itself. Pinned by
    # SecureFlagFieldSecurityScriptAgreementTests.TheBackfillScript_CountsTheRowsGetDvAllReturns_NotTheSingleListItEmits.
    $after = if ($Apply) { (Get-DvAll "$set`?`$select=$idColumn&`$filter=$Column eq null").Count } else { $ids.Count }
    $entry.nullAfter = $after
    if ($Apply) {
        if ($after -eq 0) { Report 'OK' 'after: no row holds NULL' } else { Report 'FAIL' "after: $after row(s) still hold NULL" }
    }

    $report.tables[$t] = $entry
}

$report.finishedUtc = (Get-Date).ToUniversalTime().ToString('o')
$report.gaps = @($gaps)
$report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding utf8
Write-Host "`nReport: $ReportPath"

if ($Verify -or $Apply) {
    if ($gaps.Count -eq 0) { Write-Host "`nPASS: no NULL sprk_issecure, and the default is No on every table." -ForegroundColor Green; exit 0 }
    Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nDRY RUN complete — nothing was written. Re-run with -Apply." -ForegroundColor Cyan
