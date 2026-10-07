#!/usr/bin/env pwsh
<#
.SYNOPSIS
    One-off backfill of sprk_document.sprk_graphitemidbound — the field-secured copy of sprk_graphitemid
    (unified-access-control-r2 task 171, owner round 72 item 1). Dry run by default; -Apply writes; -Verify checks.
    Idempotent.

.DESCRIPTION
    WHY. sprk_graphitemid cannot be field-secured (it is in the alternate key sprk_graphitemid_uk), so the BFF's pointer
    check compares it with sprk_graphitemidbound, which only the BFF may write. The BFF writes the copy on every pointer
    write from task 171 round 72 on; rows written before that have NO copy. This script copies each row's CURRENT item id
    into its empty copy. The value copied is whatever the row holds now — a pointer forged BEFORE the lock is copied too
    (the pre-lock residual, accepted by the owner exactly as it was for sprk_graphdriveid).

    WHAT IT DOES, per sprk_document with a non-empty sprk_graphitemid:
      copy empty                 -> WOULD / DONE: PATCH sprk_graphitemidbound = sprk_graphitemid;
      copy equal                 -> OK (nothing to do);
      copy DIFFERENT             -> FAIL, never overwritten: the item id was changed after the BFF bound it, i.e. a
                                    re-point outside the BFF — the pointer check already refuses that row; an
                                    administrator must decide which item is right (the copy is the BFF's last write).
    -Verify: zero rows with an item id and an empty or different copy (exit 1 names each).

    LIVE ORDER (owner round 72 item 1 — main session):
      1. scripts/Set-DocumentRelocationSchema.ps1 (dry run -> -Apply -> -Verify)   creates the column, secured from birth;
      2. scripts/Set-DocumentPointerFieldSecurity.ps1 -BffApplicationIds ... (dry run -> -ClientNoLongerWritesPointers
         -Apply -> -Verify)                                                        grants the BFF writer / reader profiles;
      3. deploy the BFF of task 171 round 72                                      it now writes the copy on every pointer
                                                                                  write (steps 1-2 must precede it);
      4. THIS SCRIPT (dry run -> -Apply -> -Verify)                               fills every existing row;
      5. App Service setting DocumentPointer__ItemIdBoundBackfillComplete = true  from then on a row with NO copy is
                                                                                  refused (before it, an empty copy falls
                                                                                  back to the rule in force).
    Re-run -Verify immediately before step 5: a row created by an OLD BFF instance between steps 3 and 4 (a slot swap,
    a straggling worker) is caught there.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code. Cannot be combined with -Apply.

.PARAMETER PageSize
    Rows per page read (default 500).

.EXAMPLE
    .\Invoke-DocumentItemIdBoundBackfill.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    .\Invoke-DocumentItemIdBoundBackfill.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply

.EXAMPLE
    .\Invoke-DocumentItemIdBoundBackfill.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify

.NOTES
    Auth: the operator's own az CLI identity, which must be a System Administrator (full field access by platform rule —
    the column is field-secured and the BFF-managed writer profile holds only the BFF application users). No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [switch]$Apply,
    [switch]$Verify,
    [ValidateRange(1, 5000)][int]$PageSize = 500
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$Api = "$EnvironmentUrl/api/data/v9.2"
$ItemColumn = 'sprk_graphitemid'
$BoundColumn = 'sprk_graphitemidbound'

$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
    Prefer             = "odata.maxpagesize=$PageSize"
}

$IsDryRun = -not $Apply.IsPresent
Write-Host "Environment : $EnvironmentUrl"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))

# The column must exist (Set-DocumentRelocationSchema.ps1) and be field-secured; otherwise there is nothing to backfill.
try {
    $attr = Invoke-RestMethod -Headers $headers -Method Get -Uri (
        "$Api/EntityDefinitions(LogicalName='sprk_document')/Attributes(LogicalName='$BoundColumn')?`$select=LogicalName,IsSecured")
} catch { throw "sprk_document.$BoundColumn does not exist — run scripts/Set-DocumentRelocationSchema.ps1 -Apply first." }
if (-not $attr.IsSecured) {
    throw "sprk_document.$BoundColumn is NOT field-secured — run scripts/Set-DocumentPointerFieldSecurity.ps1 -Apply first (a copy users can write protects nothing)."
}

$url = "$Api/sprk_documents?`$select=sprk_documentid,$ItemColumn,$BoundColumn&`$filter=$ItemColumn ne null"
$rows = 0; $equal = 0; $empty = 0; $written = 0; $failed = 0
$mismatches = [System.Collections.Generic.List[string]]::new()
$writeFailures = [System.Collections.Generic.List[string]]::new()

while ($url) {
    $page = Invoke-RestMethod -Headers $headers -Method Get -Uri $url
    foreach ($row in $page.value) {
        $item = "$($row.$ItemColumn)".Trim()
        if (-not $item) { continue }
        $rows++
        $bound = "$($row.$BoundColumn)".Trim()

        if ($bound -ceq $item) { $equal++; continue }   # ordinal, as the BFF compares (round 74 V7)
        if ($bound) {
            # Never overwritten: the BFF bound another item, so this row was re-pointed outside the BFF.
            $mismatches.Add("$($row.sprk_documentid): $ItemColumn='$item' but $BoundColumn='$bound'")
            continue
        }

        $empty++
        if ($Verify -or $IsDryRun) { continue }
        try {
            $patchHeaders = $headers.Clone(); $patchHeaders.Remove('Prefer'); $patchHeaders['If-Match'] = '*'
            Invoke-RestMethod -Headers $patchHeaders -Method Patch -Uri "$Api/sprk_documents($($row.sprk_documentid))" `
                -Body (@{ $BoundColumn = $item } | ConvertTo-Json -Compress) | Out-Null
            $written++
        } catch {
            $failed++
            $writeFailures.Add("$($row.sprk_documentid): $($_.Exception.Message)")
        }
    }
    $url = $page.'@odata.nextLink'
}

Write-Host ""
Write-Host "  documents with an item id : $rows"
Write-Host "  copy already equal        : $equal"
Write-Host ("  copy empty                : {0}{1}" -f $empty, $(if ($IsDryRun -and -not $Verify) { ' (WOULD write)' } elseif ($Apply) { " (written: $written, failed: $failed)" } else { '' }))
Write-Host "  copy DIFFERENT (refused)  : $($mismatches.Count)"
$mismatches | ForEach-Object { Write-Host "    FAIL  $_" -ForegroundColor Red }
$writeFailures | ForEach-Object { Write-Host "    FAIL  write: $_" -ForegroundColor Red }

if ($Verify) {
    if ($empty -eq 0 -and $mismatches.Count -eq 0) {
        Write-Host "`nPASS: every document with an item id carries an equal field-secured copy. Next: set DocumentPointer__ItemIdBoundBackfillComplete=true on the BFF." -ForegroundColor Green
        exit 0
    }
    Write-Host "`nFAIL: $empty row(s) with no copy, $($mismatches.Count) row(s) whose copy differs." -ForegroundColor Red
    exit 1
}
if ($Apply) {
    if ($failed -gt 0 -or $mismatches.Count -gt 0) { exit 1 }
    Write-Host "`nDONE. Run -Verify." -ForegroundColor Green
    exit 0
}
Write-Host "`nDRY RUN complete — nothing was written." -ForegroundColor Cyan
