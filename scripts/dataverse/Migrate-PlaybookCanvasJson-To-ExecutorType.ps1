<#
.SYNOPSIS
    Repair sprk_analysisplaybook.sprk_canvaslayoutjson — hydrate `data.executorType`
    (and fix corrupted `type:"unknown"`) using sprk_playbooknode rows as authoritative source.

.DESCRIPTION
    spaarke-ai-platform-unification-r7 — Wave 5/8 hotfix (2026-06-29).

    BUG HISTORY:
    Task 088 (FR-26) renamed canvas state from sprk_nodetype → sprk_executortype.
    Task 089 (FR-27) added coerceUnknownNodeTypes to the canvas LOADER. The two
    together created a corruption pipeline:

      1. Pre-R7 canvasLayoutJson has legacy `data.type` discriminators
         ('control', 'aiAnalysis', 'output', 'workflow', 'deliverComposite')
         WITHOUT an executorType integer.
      2. coerceUnknownNodeTypes treats absent executorType as "unknown" and
         rewrites `type:'unknown'` + `data.type:'unknown'`.
      3. Next save/auto-save persists corruption to Dataverse.

    Observed: "Tasks Due Soon" already corrupted (all 5 nodes → type:"unknown");
    all other ~20 playbooks at-risk on next open.

    FIX (two parts):
      A. Code fix: canvasStore.coerceUnknownNodeTypes patched to NOT coerce
         when executorType is absent (commit forthcoming).
      B. Data migration (THIS SCRIPT): for each playbook, read sprk_playbooknode
         rows (which Wave 5 task 054 backfilled correctly with sprk_executortype),
         hydrate `data.executorType = <int>` + `data.type = canvasType(int)` +
         `type = canvasType(int)` in the canvasLayoutJson, PATCH back.

    Idempotent: if a node already has `data.executorType` set AND matches the
    playbooknode row, no change. If it's set but DIFFERS, fix to match the
    playbooknode (authoritative). If absent, hydrate.

    Dry-run mode prints planned writes without committing.

.PARAMETER DataverseUrl
    Target Dataverse environment URL.
    Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER DryRun
    Print planned PATCHes without committing.

.PARAMETER PlaybookName
    If set, restrict migration to a single playbook by name (sprk_name).

.EXAMPLE
    .\Migrate-PlaybookCanvasJson-To-ExecutorType.ps1 -DryRun
    # Preview all changes; no writes

.EXAMPLE
    .\Migrate-PlaybookCanvasJson-To-ExecutorType.ps1
    # LIVE: migrate all playbooks

.EXAMPLE
    .\Migrate-PlaybookCanvasJson-To-ExecutorType.ps1 -PlaybookName "Tasks Due Soon"
    # LIVE: just the corrupted playbook

.NOTES
    Requires: pwsh 7+, az CLI authenticated, env writable to the operator.
    Source of truth for int → canvasType mapping:
        src/client/code-pages/PlaybookBuilder/src/config/executorMetadata.ts
    Extracted 2026-06-29 and frozen in $CanvasTypeMap below. If new executors
    are added to the enum, append to the map.
#>

param(
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com',
    [switch]$DryRun,
    [string]$PlaybookName
)
$ErrorActionPreference = 'Stop'
$AZ = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'

# Source: src/client/code-pages/PlaybookBuilder/src/config/executorMetadata.ts (33 entries)
$CanvasTypeMap = @{
    0   = 'aiAnalysis';            1   = 'aiCompletion';          2   = 'aiAnalysis'
    10  = 'aiAnalysis';            11  = 'aiAnalysis';            12  = 'aiAnalysis'
    20  = 'createTask';            21  = 'sendEmail';             22  = 'updateRecord'
    23  = 'aiAnalysis';            24  = 'sendEmail'
    30  = 'condition';             31  = 'condition';             32  = 'wait';            33  = 'start'
    40  = 'deliverOutput';         41  = 'deliverToIndex';        42  = 'deliverOutput'
    50  = 'createNotification';    51  = 'aiAnalysis';            52  = 'lookupUserMembership'
    60  = 'aiAnalysis';            70  = 'entityNameValidator';   80  = 'aiAnalysis'
    90  = 'aiAnalysis';            100 = 'condition';             110 = 'deliverOutput'
    120 = 'deliverOutput';         130 = 'entityNameValidator';   140 = 'deliverOutput'
    141 = 'entityNameValidator';   142 = 'condition';             143 = 'deliverOutput'
}

Write-Host '==============================================='
Write-Host 'Migrate sprk_canvaslayoutjson → executorType (R7 hotfix)'
Write-Host "Env  : $DataverseUrl"
Write-Host "Mode : $(if ($DryRun) { 'DRY RUN' } else { 'LIVE' })"
if ($PlaybookName) { Write-Host "Scope: '$PlaybookName' only" } else { Write-Host 'Scope: all active playbooks' }
Write-Host '==============================================='

# -- Auth --
$token = & $AZ account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($token)) { Write-Error 'az get-access-token failed'; exit 1 }
$apiUrl = "$DataverseUrl/api/data/v9.2"
$headers = @{
    'Authorization'    = "Bearer $token"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
    'If-Match'         = '*'
}

# -- Fetch playbooks --
$filter = 'statecode eq 0 and sprk_canvaslayoutjson ne null'
if ($PlaybookName) {
    $filter = "$filter and sprk_name eq '$($PlaybookName -replace "'", "''")'"
}
$pbUrl = "$apiUrl/sprk_analysisplaybooks?`$select=sprk_name,sprk_analysisplaybookid,sprk_canvaslayoutjson&`$filter=$([uri]::EscapeDataString($filter))"
$playbooks = (Invoke-RestMethod -Uri $pbUrl -Headers $headers -Method GET).value
Write-Host ''
Write-Host "Found $($playbooks.Count) playbook(s) with canvasLayoutJson."
Write-Host ''

$totals = @{ Playbooks = 0; NodesHydrated = 0; NodesUncorrupted = 0; NodesAlreadyOk = 0; Patched = 0; Skipped = 0; Errors = 0 }

foreach ($pb in $playbooks) {
    $pbId = $pb.sprk_analysisplaybookid
    $pbName = $pb.sprk_name
    Write-Host "── $pbName ($pbId)"

    # Parse canvas JSON
    try {
        $canvas = $pb.sprk_canvaslayoutjson | ConvertFrom-Json
    } catch {
        Write-Host "   ⚠ canvasLayoutJson parse error — skipping" -ForegroundColor Yellow
        $totals.Skipped++
        continue
    }
    if (-not $canvas.nodes -or $canvas.nodes.Count -eq 0) {
        Write-Host "   (no nodes in canvas) — skipping"
        $totals.Skipped++
        continue
    }

    # Load node rows for this playbook (with sprk_executortype set by Wave 5 task 054 migration)
    $nodesUrl = "$apiUrl/sprk_playbooknodes?`$select=sprk_playbooknodeid,sprk_name,sprk_executortype&`$filter=_sprk_playbookid_value eq $pbId"
    $rows = (Invoke-RestMethod -Uri $nodesUrl -Headers $headers -Method GET).value
    $rowsById = @{}
    foreach ($r in $rows) { $rowsById[$r.sprk_playbooknodeid] = $r }
    Write-Host "   Loaded $($rows.Count) node rows from Dataverse"

    # Walk canvas nodes; hydrate each
    $dirty = $false
    $nodesHydrated = 0; $nodesUncorrupted = 0; $nodesAlreadyOk = 0
    foreach ($n in $canvas.nodes) {
        $row = $rowsById[$n.id]
        if (-not $row) {
            Write-Host "   ⚠ canvas node $($n.id) ('$($n.data.label)') has NO matching sprk_playbooknode row — leaving untouched" -ForegroundColor Yellow
            continue
        }
        $serverExec = $row.sprk_executortype
        if ($serverExec -isnot [int]) {
            Write-Host "   ⚠ sprk_playbooknode $($n.id) has null sprk_executortype — Wave 5 backfill incomplete? Leaving canvas node untouched" -ForegroundColor Yellow
            continue
        }
        $targetCanvasType = $CanvasTypeMap[[int]$serverExec]
        if (-not $targetCanvasType) {
            Write-Host "   ⚠ Unknown executorType=$serverExec on $($n.id); not in CanvasTypeMap — leaving untouched" -ForegroundColor Yellow
            continue
        }

        $currentExec = $n.data.executorType
        $currentDataType = $n.data.type
        $currentNodeType = $n.type
        $isCorrupted = ($currentDataType -eq 'unknown' -or $currentNodeType -eq 'unknown')
        $isAlreadyOk = (
            ($currentExec -is [int]) -and ([int]$currentExec -eq [int]$serverExec) -and
            ($currentDataType -eq $targetCanvasType) -and
            ($currentNodeType -eq $targetCanvasType)
        )

        if ($isAlreadyOk) {
            $nodesAlreadyOk++
            continue
        }

        # Hydrate the node
        if ($n.data -isnot [PSCustomObject]) { $n.data = [PSCustomObject]@{} }
        if ($n.data.PSObject.Properties['executorType']) { $n.data.executorType = [int]$serverExec }
        else { $n.data | Add-Member -NotePropertyName executorType -NotePropertyValue ([int]$serverExec) }
        $n.data.type = $targetCanvasType
        $n.type = $targetCanvasType
        $dirty = $true
        if ($isCorrupted) {
            $nodesUncorrupted++
            Write-Host "   ✓ UNCORRUPT  $($n.id) ('$($n.data.label)') type:unknown → $targetCanvasType, executorType=$serverExec"
        } else {
            $nodesHydrated++
            Write-Host "   ✓ HYDRATE    $($n.id) ('$($n.data.label)') type:'$currentDataType' → $targetCanvasType, executorType=$serverExec"
        }
    }

    $totals.NodesHydrated += $nodesHydrated
    $totals.NodesUncorrupted += $nodesUncorrupted
    $totals.NodesAlreadyOk += $nodesAlreadyOk

    if (-not $dirty) {
        Write-Host "   ✓ no changes needed ($nodesAlreadyOk nodes already OK)"
        continue
    }

    $newJson = $canvas | ConvertTo-Json -Depth 25 -Compress
    if ($DryRun) {
        Write-Host "   [DRY-RUN] would PATCH sprk_canvaslayoutjson ($($newJson.Length) chars, +$nodesHydrated hydrated, +$nodesUncorrupted uncorrupted)"
        $totals.Patched++
        $totals.Playbooks++
        continue
    }

    # PATCH
    try {
        $body = @{ sprk_canvaslayoutjson = $newJson } | ConvertTo-Json -Depth 3
        Invoke-RestMethod -Uri "$apiUrl/sprk_analysisplaybooks($pbId)" -Headers $headers -Method PATCH -Body $body | Out-Null
        Write-Host "   ✓ PATCHED ($($newJson.Length) chars, +$nodesHydrated hydrated, +$nodesUncorrupted uncorrupted)" -ForegroundColor Green
        $totals.Patched++
        $totals.Playbooks++
    } catch {
        Write-Host "   ✗ PATCH failed: $($_.Exception.Message)" -ForegroundColor Red
        $totals.Errors++
    }
}

Write-Host ''
Write-Host '=== Summary ==='
Write-Host "  Playbooks scanned        : $($playbooks.Count)"
Write-Host "  Playbooks needing update : $($totals.Patched)"
Write-Host "  Nodes hydrated           : $($totals.NodesHydrated)"
Write-Host "  Nodes un-corrupted       : $($totals.NodesUncorrupted)"
Write-Host "  Nodes already OK         : $($totals.NodesAlreadyOk)"
Write-Host "  Playbooks skipped        : $($totals.Skipped)"
Write-Host "  Errors                   : $($totals.Errors)"
if ($DryRun) {
    Write-Host ''
    Write-Host '=== DRY RUN — no writes ===' -ForegroundColor Yellow
    Write-Host 'Re-run without -DryRun to apply.'
}
