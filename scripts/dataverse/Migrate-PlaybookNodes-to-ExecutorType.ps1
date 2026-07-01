<#
.SYNOPSIS
    Backfills `sprk_executortype` on `sprk_playbooknode` rows from the
    owner-reviewed CSV produced by task 052.

.DESCRIPTION
    spaarke-ai-platform-unification-r7 — Wave 5 task 053 (FR-19 writer half).

    Reads an owner-decision CSV (one row per playbook node, with the owner's
    authoritative `sprk_executortype` value) and PATCHes each row in Dataverse.

    INPUT CSV CONTRACT
      Required columns (minimum):
        node_id                       — sprk_playbooknodeid (GUID)
        owner_decision_executortype   — integer value to write
                                        (or one of the auto-detected fallback
                                        column names: see ColumnAutoDetection)
      Optional columns (ignored if present):
        node_name, playbook_name, action_id, action_name,
        current_sprk_executortype, current_advisory_executoractiontype,
        suggested_executortype, suggested_executortype_label,
        confidence, suggestion_source, owner_notes

    IDEMPOTENCY (load-bearing — task 053 acceptance criteria)
      For each row the script:
        1. Reads the CURRENT `sprk_executortype` value from Dataverse
        2. Compares to the CSV's `owner_decision_executortype`
        3. If equal → SKIP (logs "already correct"; no PATCH)
        4. If different → PATCH (or log "WOULD PATCH" under -DryRun)
      Re-runs against an already-migrated set produce zero writes. Re-runs
      against a partially-migrated set pick up where the prior run halted.

    DRY-RUN MODE
      `-DryRun` performs all auth + READS but issues no PATCH calls. The
      log shows every planned write (FROM → TO). Use this BEFORE every
      production run. Acceptance criterion + NFR-05.

    HALT ON FIRST ERROR
      Per spec FR-19 acceptance ("migration script runs cleanly") and the
      defensive-write constraint, the script halts on the first PATCH error.
      Partial migration is harder to recover from than a clean halt: re-run
      after fixing the underlying issue and idempotency picks up unchanged
      rows.

    COLUMN AUTO-DETECTION
      Many owner CSV exports rename the decision column. The script auto-detects
      from this preference order:
        1. owner_decision_executortype  (canonical, emitted by task 050)
        2. owner_decision
        3. executortype_decision
        4. decision_executortype
        5. decision
      If none of these are present, exits 1 with explicit instruction to
      rename the column to `owner_decision_executortype` (the default).

    DEFENSIVE BEHAVIOR
      - Empty owner-decision cells          → skipped with WARN (not an error)
      - Non-integer owner-decision values   → halts with structured error
      - Out-of-range ExecutorType values    → halts with structured error
      - Node GUID not found in Dataverse    → skipped with WARN (not an error;
                                              owner may have deleted node post-review)
      - Empty CSV                           → exits 0 with "no rows to apply"
      - Missing required columns            → exits 1 with structured error
      - All decisions empty (no-op input)   → exits 0 with "no decisions to apply"
                                              graceful (covers the task 053 self-test
                                              scenario where owner CSV not yet produced)

.PARAMETER Environment
    Target Dataverse environment label. Default: dev.

.PARAMETER DataverseUrl
    Base Dataverse URL (e.g. https://spaarkedev1.crm.dynamics.com).
    Defaults to the DATAVERSE_URL env var.

.PARAMETER DryRun
    Preview mode: authenticates, reads current values, prints every planned
    write (FROM → TO) — does NOT issue any PATCH call. Use BEFORE every
    production run.

.PARAMETER InputCsv
    Path to the owner-decision CSV. Default:
    projects/spaarke-ai-platform-unification-r7/notes/drafts/playbook-node-review-output.csv
    Path is relative to current working directory unless rooted.

.EXAMPLE
    # Dry run against spaarkedev1 — verify planned writes before doing them
    .\Migrate-PlaybookNodes-to-ExecutorType.ps1 -DryRun

.EXAMPLE
    # Live run with default CSV path
    .\Migrate-PlaybookNodes-to-ExecutorType.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    # Live run with owner's custom-named CSV
    .\Migrate-PlaybookNodes-to-ExecutorType.ps1 `
      -DataverseUrl https://spaarkedev1.crm.dynamics.com `
      -InputCsv C:\Users\owner\Desktop\playbook-decisions-final.csv

.NOTES
    Prerequisites:
      - PowerShell 7+
      - Azure CLI installed and authenticated (az login)
      - Write access to sprk_playbooknode in the target Dataverse environment

    Related:
      - Task 050: Review-PlaybookNodes-Dispatch.ps1 (READ-ONLY producer of
        the input CSV; this script is the writer half)
      - Task 052: owner-review checkpoint (gates this script; owner CSV must
        exist with `owner_decision_executortype` populated)
      - Task 054: runs this script (-DryRun → live) + post-migration audit
      - Task 055: Deploy-Playbook.ps1 update to write executor type going
        forward (FR-20; this script is the one-time historical backfill)
      - Spec FR-19 (backfill obligation), NFR-05 (idempotent + dry-run)

    NO BFF surface touched. No publish-size verification needed
    (per CLAUDE.md §10).
#>

param(
    [ValidateSet('dev')]
    [string]$Environment = 'dev',

    [string]$DataverseUrl = $env:DATAVERSE_URL,

    [switch]$DryRun,

    [string]$InputCsv = 'projects/spaarke-ai-platform-unification-r7/notes/drafts/playbook-node-review-output.csv'
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# ExecutorType enum range (mirrors INodeExecutor.cs / task 050)
# ---------------------------------------------------------------------------
# Source of truth: the C# ExecutorType enum. Kept here only to validate
# the integer range of owner-decision values (range check is "is this a
# known enum value?"). If a new value is added in C#, add it here AND in
# Review-PlaybookNodes-Dispatch.ps1 in lockstep.
$KnownExecutorTypeValues = @(
    0, 1, 2,
    10, 11, 12,
    20, 21, 22, 23, 24,
    30, 31, 32, 33,
    40, 41, 42,
    50, 51, 52,
    60,
    70,
    80,
    90,
    100,
    110,
    120,
    130,
    140, 141, 142, 143
)

# Preference-ordered list of column names the script will auto-detect
# as the owner-decision column. First match wins.
$DecisionColumnCandidates = @(
    'owner_decision_executortype',
    'owner_decision',
    'executortype_decision',
    'decision_executortype',
    'decision'
)

# ---------------------------------------------------------------------------
# Validate inputs
# ---------------------------------------------------------------------------
if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

if (-not (Test-Path -Path $InputCsv)) {
    Write-Error "Input CSV not found at: $InputCsv. Pass -InputCsv to override."
    exit 1
}

$EnvironmentUrl = $DataverseUrl.TrimEnd('/')
$ApiBase = "$EnvironmentUrl/api/data/v9.2"

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Migrate Playbook Nodes to sprk_executortype (FR-19 / R7 task 053) ===' -ForegroundColor Cyan
Write-Host "Environment      : $EnvironmentUrl"
Write-Host "Input CSV        : $InputCsv"
if ($DryRun) {
    Write-Host 'Mode             : DRY RUN (no PATCH calls)' -ForegroundColor Yellow
} else {
    Write-Host 'Mode             : LIVE (PATCH calls will be issued)' -ForegroundColor Green
}
Write-Host ''

# ---------------------------------------------------------------------------
# Step 1: Load + validate CSV (BEFORE auth — fail fast on input problems)
# ---------------------------------------------------------------------------
Write-Host '=== Step 1: Load + validate CSV ===' -ForegroundColor Cyan

try {
    $csvRows = @(Import-Csv -Path $InputCsv)
} catch {
    Write-Error "Failed to parse CSV '$InputCsv': $($_.Exception.Message)"
    exit 1
}

Write-Host "  Loaded $($csvRows.Count) row(s)" -ForegroundColor Green

if ($csvRows.Count -eq 0) {
    Write-Host ''
    Write-Host '  CSV is empty. Nothing to do. Exiting cleanly.' -ForegroundColor Yellow
    exit 0
}

# Discover columns from first row (PSCustomObject members)
$columnNames = @($csvRows[0].PSObject.Properties.Name)
Write-Host "  Columns detected : $($columnNames -join ', ')" -ForegroundColor Gray

# Required: node_id
if ($columnNames -notcontains 'node_id') {
    Write-Error @"
Required column 'node_id' missing from CSV.
The owner-decision CSV MUST contain a 'node_id' column (sprk_playbooknodeid GUID).
Detected columns: $($columnNames -join ', ')
Re-export from Review-PlaybookNodes-Dispatch.ps1 (task 050) and re-run.
"@
    exit 1
}

# Auto-detect the decision column
$decisionColumn = $null
foreach ($candidate in $DecisionColumnCandidates) {
    if ($columnNames -contains $candidate) {
        $decisionColumn = $candidate
        break
    }
}

if (-not $decisionColumn) {
    Write-Error @"
No decision column found in CSV.
The script auto-detects the owner-decision column from this preference order:
  $($DecisionColumnCandidates -join ', ')
Detected columns: $($columnNames -join ', ')
Fix: rename the decision column in your CSV to 'owner_decision_executortype' and re-run.
"@
    exit 1
}

Write-Host "  Decision column  : $decisionColumn" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 2: Pre-scan rows — collect actionable rows + validate values
# ---------------------------------------------------------------------------
# Done BEFORE auth so input problems halt immediately. Each row classified as:
#   - ACTIONABLE: has both node_id + valid decision int
#   - EMPTY     : has no decision value (owner hasn't decided yet — skip with warn)
# Invalid decision values (non-integer / out-of-range) halt the script — they
# are authoring errors, not "owner skipped" cases.
Write-Host ''
Write-Host '=== Step 2: Pre-scan CSV rows ===' -ForegroundColor Cyan

$actionableRows = @()
$emptyDecisionCount = 0

for ($i = 0; $i -lt $csvRows.Count; $i++) {
    $row = $csvRows[$i]
    $rowNumber = $i + 2  # +2 because CSV row 1 is header; row index 0 = CSV row 2

    $nodeId = $row.node_id
    if ([string]::IsNullOrWhiteSpace($nodeId)) {
        Write-Warning "  Row $rowNumber : empty node_id — skipping"
        continue
    }

    # Trim + clean the decision cell
    $decisionRaw = $row.$decisionColumn
    if ($null -ne $decisionRaw) { $decisionRaw = ([string]$decisionRaw).Trim() }

    if ([string]::IsNullOrWhiteSpace($decisionRaw)) {
        $emptyDecisionCount++
        continue
    }

    # Parse integer
    $decisionValue = 0
    if (-not [int]::TryParse($decisionRaw, [ref]$decisionValue)) {
        Write-Error "Row $rowNumber (node_id $nodeId): decision value '$decisionRaw' is not a valid integer. Fix CSV and re-run."
        exit 1
    }

    # Range check against known ExecutorType enum values
    if ($KnownExecutorTypeValues -notcontains $decisionValue) {
        Write-Error @"
Row $rowNumber (node_id $nodeId): decision value $decisionValue is not a known ExecutorType.
Known values: $(($KnownExecutorTypeValues | Sort-Object) -join ', ')
Fix CSV and re-run.
"@
        exit 1
    }

    $actionableRows += [PSCustomObject]@{
        RowNumber     = $rowNumber
        NodeId        = $nodeId
        DecisionValue = $decisionValue
        NodeName      = if ($columnNames -contains 'node_name') { $row.node_name } else { '' }
    }
}

Write-Host "  Actionable rows  : $($actionableRows.Count)" -ForegroundColor Green
Write-Host "  Empty-decision rows (skipped — owner has not decided yet): $emptyDecisionCount" -ForegroundColor Gray

if ($actionableRows.Count -eq 0) {
    Write-Host ''
    Write-Host '  No decisions to apply (decision column is empty on all rows).' -ForegroundColor Yellow
    Write-Host '  This is the expected state BEFORE the owner has reviewed the CSV.' -ForegroundColor Yellow
    Write-Host '  Re-run after the owner has populated the decision column.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '=== DONE (no-op) ===' -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------------------
# Step 3: Authenticate
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Step 3: Authenticate ===' -ForegroundColor Cyan
try {
    $tokenJson = az account get-access-token --resource $EnvironmentUrl 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Error "az account get-access-token failed:`n$tokenJson"
        exit 1
    }
    $token = ($tokenJson | ConvertFrom-Json).accessToken
    if (-not $token) {
        Write-Error 'Failed to extract access token from az output.'
        exit 1
    }
    Write-Host '  OK - token acquired' -ForegroundColor Green
} catch {
    Write-Error "Authentication failed: $($_.Exception.Message)"
    exit 1
}

$readHeaders = @{
    'Authorization'    = "Bearer $token"
    'Accept'           = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
}

$writeHeaders = @{
    'Authorization'    = "Bearer $token"
    'Accept'           = 'application/json'
    'Content-Type'     = 'application/json; charset=utf-8'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'If-Match'         = '*'   # unconditional update; we've already read current state
}

# ---------------------------------------------------------------------------
# Step 4: Per-row idempotent migration
# ---------------------------------------------------------------------------
# For each actionable row:
#   1. GET current sprk_executortype  (single REST call)
#   2. Compare to CSV decision
#   3. SKIP if equal (idempotent no-op)
#   4. PATCH if different (or "WOULD PATCH" log under DryRun)
# Halt on first PATCH error (per defensive-write constraint).
Write-Host ''
Write-Host '=== Step 4: Migrate (per-row read + compare + write) ===' -ForegroundColor Cyan

$startTime = Get-Date
$counts = @{
    Skipped       = 0   # already correct
    Patched       = 0   # PATCH succeeded (or would-succeed under DryRun)
    NodeNotFound  = 0   # 404 on GET — owner deleted node post-review
}

foreach ($entry in $actionableRows) {
    $nodeId        = $entry.NodeId
    $decisionValue = $entry.DecisionValue
    $rowNum        = $entry.RowNumber
    $nodeLabel     = if ($entry.NodeName) { "$($entry.NodeName) ($nodeId)" } else { $nodeId }

    # Step 4a: Read current value
    $readUri = "$ApiBase/sprk_playbooknodes($nodeId)?`$select=sprk_executortype"
    $currentValue = $null
    $nodeMissing = $false

    try {
        $response = Invoke-RestMethod -Uri $readUri -Headers $readHeaders -Method Get -ErrorAction Stop
        # sprk_executortype may be $null (Dataverse omits null props sometimes)
        if ($null -ne $response.sprk_executortype) {
            $currentValue = [int]$response.sprk_executortype
        }
    } catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        if ($statusCode -eq 404) {
            $nodeMissing = $true
        } else {
            $errMsg = $_.ErrorDetails.Message
            if (-not $errMsg) { $errMsg = $_.Exception.Message }
            Write-Error "Row $rowNum (node $nodeId): GET failed (HTTP $statusCode): $errMsg"
            exit 1
        }
    }

    if ($nodeMissing) {
        Write-Warning "Row $rowNum : node $nodeLabel not found in Dataverse (404). Skipping (owner may have deleted post-review)."
        $counts.NodeNotFound++
        continue
    }

    # Step 4b: Compare + skip if equal (IDEMPOTENT no-op)
    if ($null -ne $currentValue -and $currentValue -eq $decisionValue) {
        Write-Host ("  [SKIP] row {0,3} : {1} already = {2}" -f $rowNum, $nodeLabel, $decisionValue) -ForegroundColor DarkGray
        $counts.Skipped++
        continue
    }

    # Step 4c: PATCH (or log under DryRun)
    $fromDisplay = if ($null -eq $currentValue) { 'null' } else { "$currentValue" }
    if ($DryRun) {
        Write-Host ("  [DRY ] row {0,3} : {1} : {2} -> {3}" -f $rowNum, $nodeLabel, $fromDisplay, $decisionValue) -ForegroundColor Yellow
        $counts.Patched++
        continue
    }

    $patchUri = "$ApiBase/sprk_playbooknodes($nodeId)"
    $patchBody = @{ sprk_executortype = $decisionValue }
    $jsonBody = $patchBody | ConvertTo-Json -Compress

    try {
        Invoke-RestMethod -Uri $patchUri -Headers $writeHeaders -Method Patch `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody)) | Out-Null
        Write-Host ("  [PATCH] row {0,3} : {1} : {2} -> {3}" -f $rowNum, $nodeLabel, $fromDisplay, $decisionValue) -ForegroundColor Green
        $counts.Patched++
    } catch {
        $errMsg = $_.ErrorDetails.Message
        if (-not $errMsg -and $_.Exception.Response) {
            try {
                $errMsg = $_.Exception.Response.Content.ReadAsStringAsync().Result
            } catch { $errMsg = $_.Exception.Message }
        }
        if (-not $errMsg) { $errMsg = $_.Exception.Message }
        Write-Error @"
Row $rowNum (node $nodeLabel): PATCH failed.
  Target value: $decisionValue
  Reason      : $errMsg
HALTING per defensive-write constraint. Fix the underlying issue and re-run
(idempotency will skip already-migrated rows).
"@
        exit 1
    }
}

$endTime = Get-Date
$runtime = $endTime - $startTime

# ---------------------------------------------------------------------------
# Step 5: Summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Summary ===' -ForegroundColor Cyan
Write-Host ("  CSV rows total                     : {0}" -f $csvRows.Count) -ForegroundColor White
Write-Host ("  Actionable rows                    : {0}" -f $actionableRows.Count) -ForegroundColor White
Write-Host ("  Empty-decision rows (skipped)      : {0}" -f $emptyDecisionCount) -ForegroundColor Gray
Write-Host ("  Already-correct rows (no-op)       : {0}" -f $counts.Skipped) -ForegroundColor Green
if ($DryRun) {
    Write-Host ("  WOULD PATCH                        : {0}" -f $counts.Patched) -ForegroundColor Yellow
} else {
    Write-Host ("  Patched                            : {0}" -f $counts.Patched) -ForegroundColor Green
}
Write-Host ("  Node not found (404, skipped)      : {0}" -f $counts.NodeNotFound) -ForegroundColor Gray
Write-Host ("  Runtime                            : {0:N1} s" -f $runtime.TotalSeconds) -ForegroundColor White
Write-Host ''

if ($DryRun) {
    Write-Host '=== DRY RUN COMPLETE ===' -ForegroundColor Yellow
    Write-Host 'Re-run without -DryRun to apply the planned PATCH calls.' -ForegroundColor Yellow
} else {
    Write-Host '=== DONE ===' -ForegroundColor Green
    Write-Host 'NEXT STEPS:' -ForegroundColor Cyan
    Write-Host '  1. Re-run with -DryRun to confirm zero pending writes (idempotency check)'
    Write-Host '  2. Run task 054 post-migration audit (every node has non-null sprk_executortype)'
}
Write-Host ''
