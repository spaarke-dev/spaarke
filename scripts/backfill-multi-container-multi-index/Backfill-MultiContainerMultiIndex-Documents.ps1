<#
.SYNOPSIS
    Backfill `sprk_searchindexname` on historical `sprk_document` records that
    pre-date the DocumentRecordService client-side fix (task 027).

.DESCRIPTION
    Implements spec.md FR-BF-02 and design.md §5.3 for the
    spaarke-multi-container-multi-index-r1 project.

    For each `sprk_document` whose `sprk_searchindexname` is empty:
      1. Read its `sprk_graphdriveid` (the canonical Document-side container
         reference per design.md §4.1 / INV-6 / Spaarke convention).
      2. If `sprk_graphdriveid` is NULL or empty → ORPHAN: log + skip.
         (Out of scope per spec.md §9 round-3 resolution: "Orphan Document
         handling — dev/test data; out of scope". DO NOT error.)
      3. Map `sprk_graphdriveid` through the hardcoded §5.1 container → index
         table (`$ContainerIndexMap`) to derive `sprk_searchindexname`.
      4. If the container is NOT in the map → HALT LOUD (operator must extend
         the map per design.md §5.1).
      5. PATCH `sprk_searchindexname` ONLY when it is currently empty.
         (INV-5 — never overwrite explicit values. The OData filter already
         excludes non-null values, but a defensive re-check is performed.)

    DESIGN INVARIANT (reaffirmed below at the write call):
    This script DOES NOT write `sprk_containerid` on any `sprk_document`
    record. The Document container reference is `sprk_graphdriveid`;
    `sprk_containerid` on Document is intentionally NULL per Spaarke
    convention (design.md §4.1 / §10.2 / INV-6).

    Idempotent (re-runs find zero remaining empty values).
    Resumable    (checkpoint marker file every -CheckpointInterval records).
    Paged        (server-driven paging via @odata.nextLink, -BatchSize default 500).
    INV-5-safe   (filter + defensive re-check; PATCH bodies are minimal).

.PARAMETER Environment
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER DryRun
    When set, computes the audit log and reports actions but issues NO writes.
    Use this for the FR-BF-02 dry-run verification (task 053).

.PARAMETER CheckpointInterval
    Persist a checkpoint marker file every N processed records. Default 100.
    Allows a killed run to resume by skipping IDs already recorded.

.PARAMETER BatchSize
    Server-side page size (`$top`). Default 500. Server-driven paging via
    `@odata.nextLink` is honored regardless.

.PARAMETER LogPath
    Override the audit log file path. Default
    `projects/spaarke-multi-container-multi-index-r1/notes/handoffs/051-document-backfill-{env}-{timestamp}.csv`.

.EXAMPLE
    .\Backfill-MultiContainerMultiIndex-Documents.ps1 `
        -Environment "https://spaarkedev1.crm.dynamics.com" -DryRun

.EXAMPLE
    .\Backfill-MultiContainerMultiIndex-Documents.ps1 `
        -Environment "https://spaarke-demo.crm.dynamics.com"

.NOTES
    Project       : spaarke-multi-container-multi-index-r1
    Task          : 051 (FR-BF-02)
    Sibling tasks : 050 (parent backfill), 052 (drift audit) — parallel-safe
    Spec refs     : spec.md FR-BF-02, FR-BF-04; spec.md §9 round-3
    Design refs   : design.md §5.1 (container→index map), §5.3 (Document
                    backfill), INV-5 (no-overwrite), INV-6 (routing tuple)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Environment,

    [switch]$DryRun,

    [int]$CheckpointInterval = 100,

    [int]$BatchSize = 500,

    [string]$LogPath
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# §5.1 Container → index name mapping (HARDCODED — design.md §5.1)
# ---------------------------------------------------------------------------
# Same source of truth as task 050 (parent-records backfill) and task 052
# (drift audit). Any new container/index pair added to the platform MUST
# be appended here BEFORE running the backfill — the "fail loud" rule
# (design.md §5.1) is intentional: silent default would hide misalignment.
$ContainerIndexMap = @{
    'b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50' = 'spaarke-knowledge-index-v2'
    'b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh' = 'spaarke-file-index'
}

# ---------------------------------------------------------------------------
# Bootstrap
# ---------------------------------------------------------------------------
$Environment = $Environment.TrimEnd('/')
$envSlug     = ([uri]$Environment).Host -replace '\.', '-'
$timestamp   = Get-Date -Format 'yyyyMMdd-HHmmss'

# Default audit log path lives under the project's handoff notes folder.
if (-not $LogPath) {
    $scriptDir  = Split-Path -Parent $PSCommandPath
    $repoRoot   = Resolve-Path (Join-Path $scriptDir '..\..')
    $handoffDir = Join-Path $repoRoot 'projects\spaarke-multi-container-multi-index-r1\notes\handoffs'
    if (-not (Test-Path $handoffDir)) {
        New-Item -ItemType Directory -Path $handoffDir -Force | Out-Null
    }
    $LogPath = Join-Path $handoffDir "051-document-backfill-$envSlug-$timestamp.csv"
}

# Checkpoint marker — co-located with audit log so resume is obvious.
$CheckpointPath = "$LogPath.checkpoint"

Write-Host ""
Write-Host "=== Backfill: sprk_document.sprk_searchindexname (FR-BF-02) ===" -ForegroundColor Cyan
Write-Host "  Environment          : $Environment"
Write-Host "  Mode                 : $(if ($DryRun) { 'DRY-RUN (no writes)' } else { 'EXECUTE (will PATCH)' })"
Write-Host "  Batch size           : $BatchSize"
Write-Host "  Checkpoint interval  : $CheckpointInterval"
Write-Host "  Audit log            : $LogPath"
Write-Host "  Checkpoint marker    : $CheckpointPath"
Write-Host "  Container map size   : $($ContainerIndexMap.Count)"
Write-Host ""

# ---------------------------------------------------------------------------
# Resume support — read prior checkpoint (set of recordIds already processed)
# ---------------------------------------------------------------------------
$processedIds = New-Object 'System.Collections.Generic.HashSet[string]'
if (Test-Path $CheckpointPath) {
    Get-Content $CheckpointPath | ForEach-Object {
        $line = $_.Trim()
        if ($line) { [void]$processedIds.Add($line) }
    }
    Write-Host "Resuming: $($processedIds.Count) record(s) marked processed in prior run." -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# Acquire Dataverse Web API token
# ---------------------------------------------------------------------------
Write-Host "Acquiring Dataverse access token via 'az account get-access-token'..." -ForegroundColor Yellow
$token = (az account get-access-token --resource $Environment --query accessToken -o tsv)
if (-not $token) {
    Write-Error "Failed to acquire token for $Environment. Try 'az login' or check 'az account show'."
}

$queryHeaders = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Prefer             = "odata.maxpagesize=$BatchSize"
}

$patchHeaders = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'If-Match'         = '*'
    Prefer             = 'return=minimal'
}

# ---------------------------------------------------------------------------
# Initialise audit log (CSV header)
# ---------------------------------------------------------------------------
if (-not (Test-Path $LogPath)) {
    'recordId,sprk_graphdriveid,mapped_index,action,detail' | Out-File -FilePath $LogPath -Encoding utf8
}

function Write-Audit {
    param(
        [string]$RecordId,
        [string]$GraphDriveId,
        [string]$MappedIndex,
        [string]$Action,   # filled | skipped | orphan | halt | dry-run-fill
        [string]$Detail = ''
    )
    # Quote graphdriveid + detail in case they ever contain commas
    $line = '{0},"{1}",{2},{3},"{4}"' -f $RecordId, $GraphDriveId, $MappedIndex, $Action, ($Detail -replace '"', '""')
    Add-Content -Path $LogPath -Value $line
}

function Save-Checkpoint {
    param([string]$RecordId)
    Add-Content -Path $CheckpointPath -Value $RecordId
}

# ---------------------------------------------------------------------------
# Query candidates — sprk_searchindexname empty
#   INV-5: the filter excludes non-empty values; defensive re-check below
#          enforces it again per write call.
# ---------------------------------------------------------------------------
$filter = "sprk_searchindexname eq null"
$select = "sprk_documentid,sprk_graphdriveid,sprk_searchindexname"
$nextUrl = "$Environment/api/data/v9.2/sprk_documents?`$filter=$filter&`$select=$select&`$top=$BatchSize"

$counts = @{
    seen    = 0
    filled  = 0          # actually PATCHed (or would-be in dry-run)
    skipped = 0          # defensive INV-5 skip (already non-empty)
    orphan  = 0          # no sprk_graphdriveid
    halt    = 0          # unmapped container — fatal
    resumed = 0          # already in checkpoint
}

$pageNumber = 0
$haltDetail = $null

while ($nextUrl) {
    $pageNumber++
    Write-Host "Fetching page $pageNumber ..." -ForegroundColor Gray

    $response = Invoke-RestMethod -Uri $nextUrl -Headers $queryHeaders -Method GET
    $records  = @($response.value)
    Write-Host "  Page ${pageNumber}: $($records.Count) record(s)" -ForegroundColor Gray

    foreach ($r in $records) {
        $counts.seen++
        $recordId    = $r.sprk_documentid
        $graphDrive  = $r.sprk_graphdriveid
        $currentVal  = $r.sprk_searchindexname

        # Skip if a previous (killed) run already processed this id.
        if ($processedIds.Contains($recordId)) {
            $counts.resumed++
            continue
        }

        # --- Orphan handling (design + spec §9 round-3): log + skip, no error
        if ([string]::IsNullOrWhiteSpace($graphDrive)) {
            Write-Audit -RecordId $recordId -GraphDriveId '' -MappedIndex '' `
                -Action 'orphan' -Detail 'No sprk_graphdriveid — out of scope per spec §9 round-3'
            Save-Checkpoint $recordId
            $counts.orphan++
            if (($counts.seen % $CheckpointInterval) -eq 0) {
                Write-Host "    Checkpoint: $($counts.seen) seen / $($counts.filled) filled / $($counts.orphan) orphan" -ForegroundColor DarkGray
            }
            continue
        }

        # --- Map via §5.1 — halt loud on unmapped
        if (-not $ContainerIndexMap.ContainsKey($graphDrive)) {
            Write-Audit -RecordId $recordId -GraphDriveId $graphDrive -MappedIndex '' `
                -Action 'halt' -Detail 'Unmapped container — extend $ContainerIndexMap per design.md §5.1'
            $counts.halt++
            $haltDetail = "Unmapped container '$graphDrive' on record $recordId — extend `$ContainerIndexMap (design.md §5.1) before retrying."
            break  # break inner foreach; outer while will exit via $haltDetail check
        }

        $mappedIndex = $ContainerIndexMap[$graphDrive]

        # --- Defensive INV-5 re-check (filter already excluded non-empty; belt + braces)
        if (-not [string]::IsNullOrWhiteSpace($currentVal)) {
            Write-Audit -RecordId $recordId -GraphDriveId $graphDrive -MappedIndex $mappedIndex `
                -Action 'skipped' -Detail "INV-5: existing value '$currentVal' preserved"
            Save-Checkpoint $recordId
            $counts.skipped++
            continue
        }

        # --- Write (or dry-run report)
        # DESIGN INV (reaffirm): we ONLY set sprk_searchindexname on the
        # Document. We DO NOT write sprk_containerid — Document container
        # reference is sprk_graphdriveid (design.md §4.1 / §10.2 / Spaarke
        # convention). Touching sprk_containerid would violate the invariant.
        if ($DryRun) {
            Write-Audit -RecordId $recordId -GraphDriveId $graphDrive -MappedIndex $mappedIndex `
                -Action 'dry-run-fill' -Detail 'Would PATCH sprk_searchindexname'
            $counts.filled++
        }
        else {
            $patchUrl = "$Environment/api/data/v9.2/sprk_documents($recordId)"
            $body     = @{ sprk_searchindexname = $mappedIndex } | ConvertTo-Json -Compress
            try {
                Invoke-RestMethod -Uri $patchUrl -Headers $patchHeaders -Method PATCH `
                    -Body $body -ContentType 'application/json' | Out-Null
                Write-Audit -RecordId $recordId -GraphDriveId $graphDrive -MappedIndex $mappedIndex `
                    -Action 'filled' -Detail ''
                $counts.filled++
            }
            catch {
                Write-Audit -RecordId $recordId -GraphDriveId $graphDrive -MappedIndex $mappedIndex `
                    -Action 'halt' -Detail "PATCH failed: $($_.Exception.Message)"
                $haltDetail = "PATCH failed on record $recordId : $($_.Exception.Message)"
                $counts.halt++
                break
            }
        }

        Save-Checkpoint $recordId
        if (($counts.seen % $CheckpointInterval) -eq 0) {
            Write-Host "    Checkpoint: $($counts.seen) seen / $($counts.filled) filled / $($counts.orphan) orphan / $($counts.skipped) skipped" -ForegroundColor DarkGray
        }
    }

    if ($haltDetail) { break }

    $nextUrl = $response.'@odata.nextLink'
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "  Seen (this run)    : $($counts.seen)"
Write-Host "  Resumed (skipped)  : $($counts.resumed)"
Write-Host "  Filled             : $($counts.filled)" -ForegroundColor Green
Write-Host "  INV-5 skipped      : $($counts.skipped)"
Write-Host "  Orphan (skipped)   : $($counts.orphan)" -ForegroundColor Yellow
Write-Host "  Halt               : $($counts.halt)" -ForegroundColor $(if ($counts.halt -gt 0) { 'Red' } else { 'Gray' })
Write-Host "  Audit log          : $LogPath"
Write-Host ""

if ($haltDetail) {
    Write-Host "HALT LOUD: $haltDetail" -ForegroundColor Red
    Write-Error $haltDetail
    exit 2
}

if ($DryRun) {
    Write-Host "Dry-run complete. Re-run without -DryRun to apply." -ForegroundColor Cyan
}
else {
    Write-Host "Backfill complete." -ForegroundColor Green
}

exit 0
