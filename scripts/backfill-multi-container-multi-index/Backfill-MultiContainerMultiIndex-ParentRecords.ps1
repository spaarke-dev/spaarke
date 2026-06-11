<#
.SYNOPSIS
    Backfill empty `sprk_containerid` + `sprk_searchindexname` on parent records
    (Matter / Project / Invoice / WorkAssignment / Event) by deriving values
    from existing child Documents' `sprk_graphdriveid` (mode), with owner-BU
    fallback when zero children exist.

.DESCRIPTION
    Project: spaarke-multi-container-multi-index-r1, Task 050 (FR-BF-01).

    BU `sprk_containerid` has been changed since older parent records were
    created (the migration that motivated this project). Backfilling from BU
    produces wrong values for historical records. Per design.md §5.2, the
    canonical evidence source is each parent record's child Documents —
    specifically the MODE of their `sprk_graphdriveid`.

    For each parent record (sprk_matter / sprk_project / sprk_invoice /
    sprk_workassignment / sprk_event) where `sprk_containerid` OR
    `sprk_searchindexname` is empty, this script:

      1. Queries child sprk_document records via the relevant lookup field.
      2. If ≥1 children: derives effective container = MODE of children's
         `sprk_graphdriveid`. Tiebreaker (equal counts): sort container ids
         alphabetically and take the first (deterministic).
      3. If 0 children: falls back to the owning BU's current
         `sprk_containerid`.
      4. Fills empty `sprk_containerid` with the derived value (INV-5: never
         overwrite an existing non-null value).
      5. Maps the derived container through the §5.1 hardcoded
         $ContainerIndexMap; fills empty `sprk_searchindexname` (INV-5).
      6. HALTS LOUD if the derived container is not in $ContainerIndexMap —
         operator must extend the map and re-run.

    Idempotent: re-running produces the same end-state (INV-5 prevents
    overwrites; checkpoint file prevents reprocessing the same record).
    Resumable: writes a checkpoint file after every -CheckpointInterval
    records; on restart, already-processed record ids are skipped.
    Paged: uses Dataverse OData paging (`@odata.nextLink`) with configurable
    -BatchSize (default 500).

    Throttling: a small Start-Sleep is inserted between PATCH writes to stay
    well clear of Dataverse service-protection limits (NFR-04: 10K records in
    a single run).

    Per-record audit log line (CSV) is emitted for every record processed.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com
    (Use Azure CLI `az login` first; this script acquires a Bearer token via
    `az account get-access-token --resource $EnvironmentUrl`.)

.PARAMETER DryRun
    Switch. When set, the script performs ALL queries + derivations + audit
    logging but issues NO PATCH writes. Use this for safe pre-flight inspection.

.PARAMETER CheckpointInterval
    Write the resumability checkpoint file after every N processed records.
    Default 100. (FR-BF-04.)

.PARAMETER BatchSize
    Page size for parent-record queries. Default 500. (FR-BF-04 / NFR-04.)

.PARAMETER LogPath
    Path to the audit log file. Default
    `./backfill-parent-records-{timestamp}.log` in the current working dir.

.PARAMETER CheckpointPath
    Path to the resumability checkpoint file. Default
    `./backfill-parent-records-progress.json` in the current working dir.

.PARAMETER ThrottleMilliseconds
    Sleep duration between Dataverse PATCH writes (NFR-04 throttle guard).
    Default 100ms.

.PARAMETER EntityFilter
    Optional. Comma-separated list of entity logical names to process; if
    omitted, all 5 entity types are processed. Useful for partial runs and
    targeted re-runs.
    Valid values: sprk_matter, sprk_project, sprk_invoice, sprk_workassignment, sprk_event

.EXAMPLE
    # Safe preview (no writes)
    .\Backfill-MultiContainerMultiIndex-ParentRecords.ps1 `
        -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" `
        -DryRun

.EXAMPLE
    # Real run against test environment
    .\Backfill-MultiContainerMultiIndex-ParentRecords.ps1 `
        -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com"

.EXAMPLE
    # Targeted run — Matter records only, smaller page + checkpoint
    .\Backfill-MultiContainerMultiIndex-ParentRecords.ps1 `
        -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" `
        -EntityFilter "sprk_matter" `
        -BatchSize 200 `
        -CheckpointInterval 50

.NOTES
    File: scripts/backfill-multi-container-multi-index/Backfill-MultiContainerMultiIndex-ParentRecords.ps1
    Project: spaarke-multi-container-multi-index-r1
    Task: 050 (FR-BF-01)
    Related: 051 (Documents backfill), 052 (drift audit), 053 (dry-run verification)
    Spec/design: projects/spaarke-multi-container-multi-index-r1/{spec.md,design.md}

    Invariants honored:
      INV-3 — BU change does not propagate to records (we never touch BU's value)
      INV-5 — Explicit overrides are sacred (never overwrite non-null values)
      INV-6 — Container + index travel together (we set both when both empty)
      INV-7 — Resolution chain order (own field → BU → tenant default)

    Mode tiebreaker: if two `sprk_graphdriveid` values appear in equal counts
    among a record's children, sort alphabetically and take the first.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$DryRun,

    [int]$CheckpointInterval = 100,

    [int]$BatchSize = 500,

    [string]$LogPath,

    [string]$CheckpointPath = "./backfill-parent-records-progress.json",

    [int]$ThrottleMilliseconds = 100,

    [string]$EntityFilter
)

$ErrorActionPreference = 'Stop'

# Default log path with timestamp (computed here so it's stable for the run)
if (-not $LogPath) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $LogPath = "./backfill-parent-records-$timestamp.log"
}

# Strip trailing slash from environment URL
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')

# -----------------------------------------------------------------------------
# §5.1 Container → index map (HARDCODED, per design.md §5.1)
#
# Source of truth: projects/spaarke-multi-container-multi-index-r1/design.md §5.1.
# Verified via MCP against live data 2026-06-05 (see design.md §5.0).
#
# OPERATOR: when a new SPE container is provisioned, ADD an entry here and
# re-run this script. Unmapped containers will HALT LOUD with a surfaceable
# error (intentional — silent default would hide misalignment).
# -----------------------------------------------------------------------------
$ContainerIndexMap = @{
    'b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50' = 'spaarke-knowledge-index-v2'  # Spaarke Demo BU container
    'b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh' = 'spaarke-file-index'           # Spaarke BU container
}

# -----------------------------------------------------------------------------
# Parent-entity metadata table
#
# entitySet = OData collection name used in Web API paths
# logicalName = Dataverse entity logical name (used in audit log)
# idField = primary-key field name
# nameField = best-effort display field for log readability
# documentLookup = the `_<schema>_value` filter on sprk_document that joins to this parent
# -----------------------------------------------------------------------------
$ParentEntities = @(
    @{ logicalName = 'sprk_matter';         entitySet = 'sprk_matters';         idField = 'sprk_matterid';         nameField = 'sprk_mattername';         documentLookup = '_sprk_matter_value' }
    @{ logicalName = 'sprk_project';        entitySet = 'sprk_projects';        idField = 'sprk_projectid';        nameField = 'sprk_projectname';        documentLookup = '_sprk_project_value' }
    @{ logicalName = 'sprk_invoice';        entitySet = 'sprk_invoices';        idField = 'sprk_invoiceid';        nameField = 'sprk_invoicename';        documentLookup = '_sprk_invoice_value' }
    @{ logicalName = 'sprk_workassignment'; entitySet = 'sprk_workassignments'; idField = 'sprk_workassignmentid'; nameField = 'sprk_workassignmentname'; documentLookup = '_sprk_workassignment_value' }
    @{ logicalName = 'sprk_event';          entitySet = 'sprk_events';          idField = 'sprk_eventid';          nameField = 'sprk_eventname';          documentLookup = '_sprk_event_value' }
)

# Apply optional entity filter
if ($EntityFilter) {
    $allowed = ($EntityFilter -split ',') | ForEach-Object { $_.Trim().ToLowerInvariant() }
    $unknown = $allowed | Where-Object { $_ -notin ($ParentEntities | ForEach-Object { $_.logicalName }) }
    if ($unknown.Count -gt 0) {
        throw "Unknown entity logical name(s) in -EntityFilter: $($unknown -join ', '). Valid: $(($ParentEntities | ForEach-Object { $_.logicalName }) -join ', ')"
    }
    $ParentEntities = $ParentEntities | Where-Object { $_.logicalName -in $allowed }
}

# -----------------------------------------------------------------------------
# Banner
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Backfill: Multi-Container / Multi-Index — Parent Records ===" -ForegroundColor Cyan
Write-Host "  Project:             spaarke-multi-container-multi-index-r1 (Task 050, FR-BF-01)"
Write-Host "  Environment:         $EnvironmentUrl"
Write-Host "  Mode:                $(if ($DryRun) { 'DRY-RUN (no writes)' } else { 'EXECUTE (will write)' })" -ForegroundColor $(if ($DryRun) { 'Yellow' } else { 'Green' })
Write-Host "  Batch size:          $BatchSize"
Write-Host "  Checkpoint every:    $CheckpointInterval records"
Write-Host "  Throttle:            ${ThrottleMilliseconds}ms between writes"
Write-Host "  Audit log:           $LogPath"
Write-Host "  Checkpoint file:     $CheckpointPath"
Write-Host "  Entities to process: $(($ParentEntities | ForEach-Object { $_.logicalName }) -join ', ')"
Write-Host "  Container map size:  $($ContainerIndexMap.Keys.Count) known container(s)"
Write-Host ""

# -----------------------------------------------------------------------------
# Acquire Dataverse access token (Azure CLI convention — matches existing
# Spaarke scripts like Backfill-DocumentHasFile.ps1)
# -----------------------------------------------------------------------------
Write-Host "Acquiring Dataverse access token..." -ForegroundColor Yellow
$token = (az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv)
if (-not $token) {
    Write-Error "Failed to acquire token for $EnvironmentUrl. Try 'az login' or check 'az account show'."
    exit 2
}

# Standard headers (read)
$ReadHeaders = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Prefer             = 'odata.include-annotations="OData.Community.Display.V1.FormattedValue"'
}

# Standard headers (write) — If-Match:* allows updates without ETag, return=minimal
# reduces payload size (NFR-04 mem-pressure mitigation across 10K records).
$WriteHeaders = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'If-Match'         = '*'
    Prefer             = 'return=minimal'
}

# -----------------------------------------------------------------------------
# Load checkpoint (resumability per FR-BF-04)
# Checkpoint shape:
#   {
#     "processedRecordIds": {
#       "sprk_matter":  ["guid1", "guid2", ...],
#       "sprk_project": [...],
#       ...
#     },
#     "lastUpdated": "2026-06-07T20:00:00Z"
#   }
# -----------------------------------------------------------------------------
$processedRecordIds = @{}
foreach ($e in $ParentEntities) { $processedRecordIds[$e.logicalName] = New-Object 'System.Collections.Generic.HashSet[string]' }

if (Test-Path $CheckpointPath) {
    try {
        $existing = Get-Content $CheckpointPath -Raw | ConvertFrom-Json
        foreach ($e in $ParentEntities) {
            $logicalName = $e.logicalName
            if ($existing.processedRecordIds -and $existing.processedRecordIds.PSObject.Properties[$logicalName]) {
                foreach ($id in $existing.processedRecordIds.$logicalName) {
                    [void]$processedRecordIds[$logicalName].Add($id)
                }
            }
        }
        $resumedCount = ($processedRecordIds.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
        Write-Host "Resumed from checkpoint: $resumedCount previously-processed record(s) will be skipped." -ForegroundColor Yellow
    } catch {
        Write-Warning "Could not parse existing checkpoint at $CheckpointPath — starting fresh. ($($_.Exception.Message))"
    }
}

function Save-Checkpoint {
    param([hashtable]$ProcessedIds, [string]$Path)
    $payload = [ordered]@{
        processedRecordIds = [ordered]@{}
        lastUpdated        = (Get-Date).ToUniversalTime().ToString('o')
    }
    foreach ($k in $ProcessedIds.Keys) {
        $payload.processedRecordIds[$k] = @($ProcessedIds[$k])
    }
    $payload | ConvertTo-Json -Depth 5 | Set-Content -Path $Path -Encoding UTF8
}

# -----------------------------------------------------------------------------
# Audit log — CSV with header
# Columns: timestamp, entity, recordId, recordName, existing_containerid,
#          existing_indexname, derived_container, derived_index, source, action, message
# -----------------------------------------------------------------------------
$auditDir = Split-Path -Parent $LogPath
if ($auditDir -and -not (Test-Path $auditDir)) {
    New-Item -ItemType Directory -Path $auditDir -Force | Out-Null
}
"timestamp,entity,recordId,recordName,existing_containerid,existing_indexname,derived_container,derived_index,source,action,message" |
    Set-Content -Path $LogPath -Encoding UTF8

function ConvertTo-CsvField {
    param([Parameter(ValueFromPipeline)]$Value)
    process {
        if ($null -eq $Value) { return '' }
        $s = [string]$Value
        if ($s -match '[,"\r\n]') { return '"' + $s.Replace('"', '""') + '"' }
        return $s
    }
}

function Write-AuditLine {
    param(
        [string]$Entity,
        [string]$RecordId,
        [string]$RecordName,
        [string]$ExistingContainer,
        [string]$ExistingIndex,
        [string]$DerivedContainer,
        [string]$DerivedIndex,
        [string]$Source,
        [string]$Action,
        [string]$Message = ''
    )
    # CSV-escape any field that might contain a comma, quote, or newline
    $line = @(
        (Get-Date).ToUniversalTime().ToString('o'),
        (ConvertTo-CsvField $Entity),
        (ConvertTo-CsvField $RecordId),
        (ConvertTo-CsvField $RecordName),
        (ConvertTo-CsvField $ExistingContainer),
        (ConvertTo-CsvField $ExistingIndex),
        (ConvertTo-CsvField $DerivedContainer),
        (ConvertTo-CsvField $DerivedIndex),
        (ConvertTo-CsvField $Source),
        (ConvertTo-CsvField $Action),
        (ConvertTo-CsvField $Message)
    ) -join ','
    Add-Content -Path $LogPath -Value $line -Encoding UTF8
}

# -----------------------------------------------------------------------------
# Helper: page through a Dataverse OData query, yield records one at a time.
# Uses @odata.nextLink, so we never load all 10K records into memory (NFR-04).
# -----------------------------------------------------------------------------
function Invoke-PagedQuery {
    param(
        [string]$InitialUrl
    )
    $nextUrl = $InitialUrl
    while ($nextUrl) {
        $headersWithPage = $ReadHeaders.Clone()
        $headersWithPage['Prefer'] = 'odata.maxpagesize=' + $BatchSize + ',odata.include-annotations="OData.Community.Display.V1.FormattedValue"'
        $response = Invoke-RestMethod -Uri $nextUrl -Headers $headersWithPage -Method GET
        foreach ($r in $response.value) {
            # Emit each record to the pipeline. We deliberately do NOT use
            # `,$r` here — array-wrapping would still flatten when consumed
            # by foreach, but writing single objects matches the streaming
            # contract callers expect.
            Write-Output $r
        }
        $nextUrl = $response.'@odata.nextLink'
    }
}

# -----------------------------------------------------------------------------
# Helper: derive container from child Documents (mode of sprk_graphdriveid).
# Returns @{ container = '...'; source = 'doc-mode'; childCount = N }
# or @{ container = $null; source = 'no-children'; childCount = 0 }.
# Tiebreaker on equal counts: alphabetical sort, take first.
# -----------------------------------------------------------------------------
function Get-ContainerFromChildren {
    param(
        [string]$EntitySetForChildLookup,  # always 'sprk_documents'
        [string]$LookupField,              # e.g. _sprk_matter_value
        [string]$ParentId
    )
    # Filter: only child docs with non-null sprk_graphdriveid (records without
    # are not evidence). $select trimmed to minimize wire payload.
    $filter = "$LookupField eq $ParentId and sprk_graphdriveid ne null"
    $select = 'sprk_graphdriveid'
    $url = "$EnvironmentUrl/api/data/v9.2/sprk_documents?`$filter=$filter&`$select=$select&`$top=5000"

    $driveIds = @()
    foreach ($doc in Invoke-PagedQuery -InitialUrl $url) {
        if ($doc.sprk_graphdriveid) { $driveIds += $doc.sprk_graphdriveid }
    }

    if ($driveIds.Count -eq 0) {
        return @{ container = $null; source = 'no-children'; childCount = 0 }
    }

    # Group by drive id, take the most common, tiebreak alphabetically
    $grouped = $driveIds | Group-Object | Sort-Object @{ Expression = 'Count'; Descending = $true }, @{ Expression = 'Name'; Descending = $false }
    $mode = $grouped[0].Name
    return @{ container = $mode; source = 'doc-mode'; childCount = $driveIds.Count; uniqueDriveCount = $grouped.Count }
}

# -----------------------------------------------------------------------------
# Helper: derive container from owning BU's sprk_containerid.
# Returns the BU container id (or $null if BU has no value).
# -----------------------------------------------------------------------------
function Get-ContainerFromOwningBu {
    param(
        [string]$BusinessUnitId
    )
    if (-not $BusinessUnitId) { return $null }
    $url = "$EnvironmentUrl/api/data/v9.2/businessunits($BusinessUnitId)?`$select=sprk_containerid"
    try {
        $bu = Invoke-RestMethod -Uri $url -Headers $ReadHeaders -Method GET
        return $bu.sprk_containerid
    } catch {
        Write-Warning "Could not read BU $BusinessUnitId : $($_.Exception.Message)"
        return $null
    }
}

# -----------------------------------------------------------------------------
# Helper: HALT LOUD on unmapped container
# -----------------------------------------------------------------------------
function Stop-OnUnmappedContainer {
    param(
        [string]$Entity,
        [string]$RecordId,
        [string]$Container
    )
    Write-AuditLine -Entity $Entity -RecordId $RecordId -RecordName '' `
        -ExistingContainer '' -ExistingIndex '' `
        -DerivedContainer $Container -DerivedIndex '' `
        -Source 'derived' -Action 'halt-loud' `
        -Message "Container '$Container' is NOT in `$ContainerIndexMap"

    $known = ($ContainerIndexMap.Keys | Sort-Object) -join "`n    "
    Write-Host ""
    Write-Host "=============================================================" -ForegroundColor Red
    Write-Host "  HALT — Unmapped container encountered" -ForegroundColor Red
    Write-Host "=============================================================" -ForegroundColor Red
    Write-Host "  Entity:    $Entity" -ForegroundColor Red
    Write-Host "  Record ID: $RecordId" -ForegroundColor Red
    Write-Host "  Container: $Container" -ForegroundColor Red
    Write-Host ""
    Write-Host "  This container id is NOT in the hardcoded §5.1 map." -ForegroundColor Yellow
    Write-Host "  Known containers ($($ContainerIndexMap.Keys.Count)):" -ForegroundColor Yellow
    Write-Host "    $known" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  OPERATOR ACTION:" -ForegroundColor Yellow
    Write-Host "    1. Verify this container id is a legitimate SPE container that should be indexed." -ForegroundColor Yellow
    Write-Host "    2. Add an entry to `$ContainerIndexMap at the top of this script:" -ForegroundColor Yellow
    Write-Host "         '$Container' = '<new-index-name>'" -ForegroundColor Yellow
    Write-Host "    3. Add the new index name to BFF appsettings.AiSearch.AllowedIndexes." -ForegroundColor Yellow
    Write-Host "    4. Re-run this script — the checkpoint will resume from where it stopped." -ForegroundColor Yellow
    Write-Host "=============================================================" -ForegroundColor Red
    Write-Host ""

    # Persist checkpoint before exiting so the resume picks up cleanly
    Save-Checkpoint -ProcessedIds $processedRecordIds -Path $CheckpointPath
    exit 3
}

# -----------------------------------------------------------------------------
# Main processing loop
# -----------------------------------------------------------------------------
$summary = @{
    totalScanned       = 0
    skippedAlreadyDone = 0
    skippedNoEmpties   = 0
    filledContainer    = 0
    filledIndex        = 0
    skippedInv5        = 0
    fallbackBuUsed     = 0
    fallbackEmpty      = 0
    writeErrors        = 0
}
$processedSinceCheckpoint = 0

foreach ($entity in $ParentEntities) {
    $logicalName = $entity.logicalName
    $entitySet   = $entity.entitySet
    $idField     = $entity.idField
    $nameField   = $entity.nameField
    $lookupField = $entity.documentLookup

    Write-Host ""
    Write-Host "--- Processing $logicalName ---" -ForegroundColor Cyan

    # Query parent records where container OR index is empty.
    # We could narrow to "container=null OR index=null" only — that's the
    # essential population. Records with BOTH set are honored by INV-5 and
    # need not be touched.
    $filter = "(sprk_containerid eq null or sprk_searchindexname eq null)"
    $select = "$idField,$nameField,sprk_containerid,sprk_searchindexname,_owningbusinessunit_value"
    $url = "$EnvironmentUrl/api/data/v9.2/$entitySet`?`$filter=$filter&`$select=$select"

    $entityScanned = 0

    foreach ($record in Invoke-PagedQuery -InitialUrl $url) {
        $summary.totalScanned++
        $entityScanned++
        $recordId   = $record.$idField
        $recordName = $record.$nameField

        # Skip if already processed (resumability)
        if ($processedRecordIds[$logicalName].Contains($recordId)) {
            $summary.skippedAlreadyDone++
            continue
        }

        $existingContainer = [string]$record.sprk_containerid
        $existingIndex     = [string]$record.sprk_searchindexname
        $owningBu          = [string]$record._owningbusinessunit_value

        $needContainer = [string]::IsNullOrWhiteSpace($existingContainer)
        $needIndex     = [string]::IsNullOrWhiteSpace($existingIndex)

        if (-not $needContainer -and -not $needIndex) {
            # The filter shouldn't return these, but belt-and-suspenders
            $summary.skippedNoEmpties++
            [void]$processedRecordIds[$logicalName].Add($recordId)
            continue
        }

        # Derive effective container from children (mode), else owner-BU fallback
        $derive = Get-ContainerFromChildren -EntitySetForChildLookup 'sprk_documents' -LookupField $lookupField -ParentId $recordId
        $source = $derive.source
        $derivedContainer = $derive.container

        if (-not $derivedContainer) {
            # Owner-BU fallback
            $derivedContainer = Get-ContainerFromOwningBu -BusinessUnitId $owningBu
            if ($derivedContainer) {
                $source = 'bu-fallback'
                $summary.fallbackBuUsed++
            } else {
                $source = 'no-derivable-value'
                $summary.fallbackEmpty++
            }
        }

        # If we still can't derive anything → log + skip (cannot fill)
        if (-not $derivedContainer) {
            Write-AuditLine -Entity $logicalName -RecordId $recordId -RecordName $recordName `
                -ExistingContainer $existingContainer -ExistingIndex $existingIndex `
                -DerivedContainer '' -DerivedIndex '' `
                -Source $source -Action 'skipped' `
                -Message 'No children with sprk_graphdriveid; owning BU has no sprk_containerid'
            [void]$processedRecordIds[$logicalName].Add($recordId)
            $processedSinceCheckpoint++
            continue
        }

        # Map derived container to index name — HALT LOUD if unmapped
        if (-not $ContainerIndexMap.ContainsKey($derivedContainer)) {
            Stop-OnUnmappedContainer -Entity $logicalName -RecordId $recordId -Container $derivedContainer
            # Stop-OnUnmappedContainer exits the process; we never get past here
        }
        $derivedIndex = $ContainerIndexMap[$derivedContainer]

        # Build the PATCH payload — INV-5: only set fields that are currently empty
        $patch = @{}
        if ($needContainer) {
            $patch['sprk_containerid'] = $derivedContainer
        }
        if ($needIndex) {
            $patch['sprk_searchindexname'] = $derivedIndex
        }

        # Audit line BEFORE the write (so we capture intent even if write fails)
        $action = if ($DryRun) { 'would-fill' } else { 'fill-pending' }
        $msg    = "container={0} index={1} childCount={2}{3}" -f `
            ($(if ($needContainer) { 'fill' } else { 'preserve' })),
            ($(if ($needIndex) { 'fill' } else { 'preserve' })),
            ($(if ($derive.childCount) { $derive.childCount } else { 0 })),
            ($(if ($derive.uniqueDriveCount -gt 1) { ", uniqueDrives=$($derive.uniqueDriveCount)" } else { '' }))

        Write-AuditLine -Entity $logicalName -RecordId $recordId -RecordName $recordName `
            -ExistingContainer $existingContainer -ExistingIndex $existingIndex `
            -DerivedContainer $derivedContainer -DerivedIndex $derivedIndex `
            -Source $source -Action $action -Message $msg

        if ($DryRun) {
            # Mark processed (so a follow-up real run can resume cleanly),
            # increment counters that reflect what WOULD happen
            if ($needContainer) { $summary.filledContainer++ }
            if ($needIndex)     { $summary.filledIndex++ }
            [void]$processedRecordIds[$logicalName].Add($recordId)
            $processedSinceCheckpoint++
        } else {
            # Execute the PATCH
            $patchUrl  = "$EnvironmentUrl/api/data/v9.2/$entitySet($recordId)"
            $patchBody = $patch | ConvertTo-Json -Compress
            try {
                Invoke-RestMethod -Uri $patchUrl -Headers $WriteHeaders -Method PATCH -Body $patchBody -ContentType 'application/json' | Out-Null
                if ($needContainer) { $summary.filledContainer++ }
                if ($needIndex)     { $summary.filledIndex++ }

                # Update audit with success
                Write-AuditLine -Entity $logicalName -RecordId $recordId -RecordName $recordName `
                    -ExistingContainer $existingContainer -ExistingIndex $existingIndex `
                    -DerivedContainer $derivedContainer -DerivedIndex $derivedIndex `
                    -Source $source -Action 'filled' -Message "PATCH ok: $patchBody"

                [void]$processedRecordIds[$logicalName].Add($recordId)
                $processedSinceCheckpoint++

                # Throttle to stay under Dataverse service-protection limits
                if ($ThrottleMilliseconds -gt 0) {
                    Start-Sleep -Milliseconds $ThrottleMilliseconds
                }
            } catch {
                $summary.writeErrors++
                Write-AuditLine -Entity $logicalName -RecordId $recordId -RecordName $recordName `
                    -ExistingContainer $existingContainer -ExistingIndex $existingIndex `
                    -DerivedContainer $derivedContainer -DerivedIndex $derivedIndex `
                    -Source $source -Action 'write-error' -Message $_.Exception.Message
                Write-Host "  ERROR on $recordId ($recordName): $($_.Exception.Message)" -ForegroundColor Red
                # DO NOT mark as processed — let a re-run retry it
            }
        }

        # Periodic checkpoint
        if ($processedSinceCheckpoint -ge $CheckpointInterval) {
            Save-Checkpoint -ProcessedIds $processedRecordIds -Path $CheckpointPath
            $processedSinceCheckpoint = 0
            Write-Host "  [checkpoint] processed so far: $($summary.totalScanned)" -ForegroundColor Gray
        }
    }

    Write-Host "  Scanned in this entity: $entityScanned" -ForegroundColor Gray
}

# Final checkpoint write
Save-Checkpoint -ProcessedIds $processedRecordIds -Path $CheckpointPath

# -----------------------------------------------------------------------------
# Summary banner
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "  Total scanned:                     $($summary.totalScanned)"
Write-Host "  Skipped (already processed):       $($summary.skippedAlreadyDone)"
Write-Host "  Skipped (no empties on record):    $($summary.skippedNoEmpties)"
Write-Host "  Filled sprk_containerid:           $($summary.filledContainer)"
Write-Host "  Filled sprk_searchindexname:       $($summary.filledIndex)"
Write-Host "  Used BU-fallback:                  $($summary.fallbackBuUsed)"
Write-Host "  Could not derive (no value at all):$($summary.fallbackEmpty)"
Write-Host "  Write errors:                      $($summary.writeErrors)" -ForegroundColor $(if ($summary.writeErrors -gt 0) { 'Red' } else { 'Gray' })
Write-Host ""
Write-Host "  Audit log: $LogPath" -ForegroundColor Green
Write-Host "  Checkpoint: $CheckpointPath" -ForegroundColor Green
Write-Host ""

if ($DryRun) {
    Write-Host "DRY-RUN complete. Re-run without -DryRun to execute writes." -ForegroundColor Yellow
}

if ($summary.writeErrors -gt 0) { exit 1 }
exit 0
