<#
.SYNOPSIS
    Phase G data migration: copy `sprk_searchindexname` (text) into
    `sprk_ai_search_index` (lookup) on every source-entity record that has the
    text set but the lookup empty.

.DESCRIPTION
    Phase G of spaarke-multi-container-multi-index-r1 replaces the legacy
    `sprk_searchindexname` string column on 7 source entities with a lookup to
    the master-data table `sprk_aisearchindex`. The BFF resolver and PCF v1.1.75
    are already wired to read the lookup first; until every record carries the
    lookup, the BFF tenant default applies and PCF-driven searches lose intent.

    This script bridges that gap by mapping each non-null text value to the
    matching catalog GUID and writing the lookup. Mapping is deterministic:

        sprk_searchindexname text value      → sprk_aisearchindex (Document-targeted)
        ----------------------------------     ---------------------------------------
        'spaarke-file-index'                 → dd04e55f-dd64-f111-ab0c-7ced8ddc4a05
                                               ("Development Files 2")
        'spaarke-knowledge-index-v2'         → c104e55f-dd64-f111-ab0c-7ced8ddc4a05
                                               ("Development Files 1")
        any other value                      → SKIP + WARNING (no catalog match)

    All source entities (BU, Matter, Project, WorkAssignment, Document, Invoice,
    Event) point at Document-targeted catalog rows. The Matter/Project/Invoice/
    Event/WorkAssignment-targeted rows in the catalog drive the code-page
    dropdown only; they MUST NOT appear as a source record's lookup value.

    INV-5 / idempotency: only writes the lookup when (text IS NOT NULL AND
    lookup IS NULL). Existing lookup values are never overwritten. Re-runs are
    safe and report zero migrations.

    Halt-loud convention: any text value with no matching catalog row produces
    a yellow WARNING with the source entity + record ID + observed text value.
    Operator must either extend the catalog or fix the source data before a
    re-run will migrate the record.

.PARAMETER DataverseUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com
    Required. Trailing slash optional.

.PARAMETER DryRun
    Switch (default behavior). Reports counts + per-record intent without
    issuing any PATCH. Always safe to run.

.PARAMETER Apply
    Explicit switch required to commit. Must be passed; not implied by absence
    of -DryRun. Defensive UX: -Apply alone enables writes.

.PARAMETER LogPath
    Optional path to a summary log file (markdown). Defaults to
    `projects/spaarke-multi-container-multi-index-r1/notes/phase-g/migration-run-{timestamp}.log`.

.EXAMPLE
    # Default: dry run, no writes
    .\Migrate-SearchIndexLookup.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    # Commit the migration
    .\Migrate-SearchIndexLookup.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -Apply

.NOTES
    Spec: projects/spaarke-multi-container-multi-index-r1/notes/phase-g/spec.md §9
    Task: 104-data-migration-text-to-lookup.poml
    Style template: scripts/Backfill-DocumentHasFile.ps1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DataverseUrl,

    [switch]$DryRun,

    [switch]$Apply,

    [string]$LogPath
)

$ErrorActionPreference = 'Stop'

# Default to dry run unless -Apply is explicit (defensive UX)
if (-not $Apply) { $DryRun = $true }

# Strip trailing slash
$DataverseUrl = $DataverseUrl.TrimEnd('/')

# Resolve log path
if (-not $LogPath) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $repoRoot  = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $LogPath = Join-Path $repoRoot "projects/spaarke-multi-container-multi-index-r1/notes/phase-g/migration-run-$timestamp.log"
}

Write-Host ""
Write-Host "=== Phase G: SearchIndex text -> lookup Migration ===" -ForegroundColor Cyan
Write-Host "  Environment: $DataverseUrl"
Write-Host "  Mode:        $(if ($DryRun) { 'DRY-RUN (no writes; pass -Apply to commit)' } else { 'APPLY (will write)' })"
Write-Host "  Log:         $LogPath"
Write-Host ""

# ---------------------------------------------------------------------------
# Catalog mapping (text value -> sprk_aisearchindex GUID; Document-targeted)
# Source of truth: spec.md §3 + §4; GUIDs verified via MCP read_query
# on 2026-06-10.
# ---------------------------------------------------------------------------
$IndexMap = @{
    'spaarke-file-index'         = 'dd04e55f-dd64-f111-ab0c-7ced8ddc4a05'  # Development Files 2
    'spaarke-knowledge-index-v2' = 'c104e55f-dd64-f111-ab0c-7ced8ddc4a05'  # Development Files 1
}

# ---------------------------------------------------------------------------
# Entity table (logical name, OData entity-set name, primary id attribute)
# OData set names follow Dataverse pluralization rules; verified against
# Web API metadata.
# ---------------------------------------------------------------------------
$Entities = @(
    [pscustomobject]@{ LogicalName = 'businessunit';        EntitySet = 'businessunits';        IdColumn = 'businessunitid'       }
    [pscustomobject]@{ LogicalName = 'sprk_matter';         EntitySet = 'sprk_matters';         IdColumn = 'sprk_matterid'        }
    [pscustomobject]@{ LogicalName = 'sprk_project';        EntitySet = 'sprk_projects';        IdColumn = 'sprk_projectid'       }
    [pscustomobject]@{ LogicalName = 'sprk_workassignment'; EntitySet = 'sprk_workassignments'; IdColumn = 'sprk_workassignmentid'}
    [pscustomobject]@{ LogicalName = 'sprk_document';       EntitySet = 'sprk_documents';       IdColumn = 'sprk_documentid'      }
    [pscustomobject]@{ LogicalName = 'sprk_invoice';        EntitySet = 'sprk_invoices';        IdColumn = 'sprk_invoiceid'       }
    [pscustomobject]@{ LogicalName = 'sprk_event';          EntitySet = 'sprk_events';          IdColumn = 'sprk_eventid'         }
)

# ---------------------------------------------------------------------------
# Acquire token via az CLI (same pattern as Backfill-DocumentHasFile.ps1)
# ---------------------------------------------------------------------------
Write-Host "Acquiring Dataverse access token..." -ForegroundColor Yellow
$token = (az account get-access-token --resource $DataverseUrl --query accessToken -o tsv)
if (-not $token) {
    Write-Error "Failed to acquire token for $DataverseUrl. Try 'az login' or check 'az account show'."
}

$readHeaders = @{
    Authorization      = "Bearer $token"
    Accept             = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
}
$writeHeaders = @{
    Authorization      = "Bearer $token"
    Accept             = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "If-Match"         = "*"
    Prefer             = 'return=minimal'
}

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Invoke-DataversePagedQuery {
    param(
        [string]$Url,
        [hashtable]$Headers
    )
    $all = @()
    $nextUrl = $Url
    while ($nextUrl) {
        $resp = Invoke-RestMethod -Uri $nextUrl -Headers $Headers -Method GET
        if ($resp.value) { $all += $resp.value }
        $nextUrl = $resp.'@odata.nextLink'
    }
    return ,$all
}

function Get-CountSimple {
    param(
        [string]$EntitySet,
        [string]$Filter,
        [hashtable]$Headers
    )
    # Server-side count via $count=true / $top=0
    $url = "$DataverseUrl/api/data/v9.2/$EntitySet`?`$filter=$Filter&`$count=true&`$top=0"
    try {
        $resp = Invoke-RestMethod -Uri $url -Headers (
            $Headers + @{ Prefer = 'odata.include-annotations="OData.Community.Display.V1.FormattedValue"' }
        ) -Method GET
        return [int]$resp.'@odata.count'
    } catch {
        # Some envs disable @odata.count without explicit prefer; fall back to length
        $resp = Invoke-DataversePagedQuery -Url ("$DataverseUrl/api/data/v9.2/$EntitySet`?`$filter=$Filter&`$select=") -Headers $Headers
        return $resp.Count
    }
}

# ---------------------------------------------------------------------------
# Migrate loop
# ---------------------------------------------------------------------------
$report = New-Object System.Collections.Generic.List[object]
$grandMigrated = 0
$grandSkippedNoMatch = 0
$grandTotal = 0

foreach ($e in $Entities) {
    Write-Host ""
    Write-Host "--- $($e.LogicalName) ---" -ForegroundColor Cyan

    # Pre-state counts
    $textSet      = Get-CountSimple -EntitySet $e.EntitySet -Filter "sprk_searchindexname ne null" -Headers $readHeaders
    $lookupSetPre = Get-CountSimple -EntitySet $e.EntitySet -Filter "_sprk_ai_search_index_value ne null" -Headers $readHeaders

    Write-Host "  text_set (pre):      $textSet"
    Write-Host "  lookup_set (pre):    $lookupSetPre"

    if ($textSet -eq 0) {
        Write-Host "  Nothing to migrate. Skipping." -ForegroundColor DarkGray
        $report.Add([pscustomobject]@{
            Entity         = $e.LogicalName
            TextSet        = $textSet
            LookupSetPre   = $lookupSetPre
            LookupSetPost  = $lookupSetPre
            Migrated       = 0
            SkippedNoMatch = 0
        }) | Out-Null
        continue
    }

    # Idempotent fetch: text set AND lookup null
    $filter = "sprk_searchindexname ne null and _sprk_ai_search_index_value eq null"
    $select = "$($e.IdColumn),sprk_searchindexname"
    $queryUrl = "$DataverseUrl/api/data/v9.2/$($e.EntitySet)?`$filter=$filter&`$select=$select"

    $records = Invoke-DataversePagedQuery -Url $queryUrl -Headers $readHeaders
    Write-Host "  candidates (text NOT NULL AND lookup NULL): $($records.Count)" -ForegroundColor Yellow

    $migrated = 0
    $skippedNoMatch = 0

    foreach ($r in $records) {
        $id   = $r."$($e.IdColumn)"
        $text = $r.sprk_searchindexname
        $targetGuid = $IndexMap[$text]

        if (-not $targetGuid) {
            Write-Host "    WARN SKIP-no-match: $($e.LogicalName) $id has text='$text' (no matching catalog row)" -ForegroundColor Yellow
            $skippedNoMatch++
            continue
        }

        if ($DryRun) {
            Write-Host "    DRY  $($e.LogicalName) $id : '$text' -> sprk_aisearchindex($targetGuid)" -ForegroundColor Gray
            $migrated++
            continue
        }

        # OData @odata.bind for single-valued lookup
        $patchUrl = "$DataverseUrl/api/data/v9.2/$($e.EntitySet)($id)"
        $body = @{
            "sprk_ai_search_index@odata.bind" = "/sprk_aisearchindexes($targetGuid)"
        } | ConvertTo-Json -Compress

        try {
            Invoke-RestMethod -Uri $patchUrl -Headers $writeHeaders -Method PATCH -Body $body -ContentType "application/json" | Out-Null
            Write-Host "    OK   $($e.LogicalName) $id : '$text' -> $targetGuid" -ForegroundColor Green
            $migrated++
        } catch {
            Write-Host "    ERR  $($e.LogicalName) $id : $($_.Exception.Message)" -ForegroundColor Red
        }
    }

    # Post-state
    $lookupSetPost = if ($DryRun) {
        $lookupSetPre + $migrated   # simulated
    } else {
        Get-CountSimple -EntitySet $e.EntitySet -Filter "_sprk_ai_search_index_value ne null" -Headers $readHeaders
    }

    Write-Host "  Summary: text=$textSet  lookup_pre=$lookupSetPre  lookup_post=$lookupSetPost  migrated=$migrated  skipped=$skippedNoMatch"
    if ($lookupSetPost -ne $textSet -and $skippedNoMatch -eq 0 -and -not $DryRun) {
        Write-Host "  ! MISMATCH: text_set ($textSet) != lookup_set_post ($lookupSetPost)" -ForegroundColor Red
    }

    $grandMigrated += $migrated
    $grandSkippedNoMatch += $skippedNoMatch
    $grandTotal += $textSet

    $report.Add([pscustomobject]@{
        Entity         = $e.LogicalName
        TextSet        = $textSet
        LookupSetPre   = $lookupSetPre
        LookupSetPost  = $lookupSetPost
        Migrated       = $migrated
        SkippedNoMatch = $skippedNoMatch
    }) | Out-Null
}

# ---------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Migration Summary ===" -ForegroundColor Cyan
$report | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "  Grand totals: text_set=$grandTotal  migrated=$grandMigrated  skipped=$grandSkippedNoMatch"
Write-Host ""

# Write markdown log
try {
    $logDir = Split-Path -Parent $LogPath
    if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    $md = @()
    $md += "# Phase G migration run — $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    $md += ""
    $md += "- Environment: $DataverseUrl"
    $md += "- Mode: $(if ($DryRun) { 'DRY-RUN' } else { 'APPLY' })"
    $md += ""
    $md += "| Entity | text_set | lookup_pre | lookup_post | migrated | skipped-no-match |"
    $md += "|---|---:|---:|---:|---:|---:|"
    foreach ($row in $report) {
        $md += "| $($row.Entity) | $($row.TextSet) | $($row.LookupSetPre) | $($row.LookupSetPost) | $($row.Migrated) | $($row.SkippedNoMatch) |"
    }
    $md += ""
    $md += "**Grand totals**: text_set=$grandTotal · migrated=$grandMigrated · skipped-no-match=$grandSkippedNoMatch"
    $md -join [Environment]::NewLine | Set-Content -Path $LogPath -Encoding UTF8
    Write-Host "  Log written: $LogPath" -ForegroundColor Green
} catch {
    Write-Host "  WARN: failed to write log: $($_.Exception.Message)" -ForegroundColor Yellow
}

if ($grandSkippedNoMatch -gt 0) {
    Write-Host ""
    Write-Host "WARN: $grandSkippedNoMatch records skipped due to unmapped text values." -ForegroundColor Yellow
    Write-Host "      Review log + add catalog rows OR fix source records, then re-run." -ForegroundColor Yellow
    exit 2
}

if ($DryRun) {
    Write-Host ""
    Write-Host "DRY-RUN complete. No data was modified. Re-run with -Apply to commit." -ForegroundColor Cyan
    exit 0
}

Write-Host "Migration complete." -ForegroundColor Green
exit 0
