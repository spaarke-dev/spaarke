<#
.SYNOPSIS
    R4 fallback playbook-node deployer (MCP-equivalent direct Dataverse Web API).

.DESCRIPTION
    Deploy-Playbook.ps1's actionCode lint rejects all 5 R4 R4 playbook JSON files
    (Control nodes lack top-level actionCode; the daily-briefing-narrate.json uses
    nodeType variants "AiAnalysis" and "Tool" outside the NodeTypeMap).

    This fallback writes sprk_playbooknode rows + dependency PATCH + canvas PATCH
    directly against the existing sprk_analysisplaybook rows on spaarkedev1, matching
    the working PB-018 (New Emails on Matters) pattern observed via MCP read_query.

    Playbook rows are NOT deleted/recreated — only the node-row set is replaced.
    The playbook-level scope N:N rows are left intact.

    Per the canonical recipe (ai-guide-playbook-deploy-recipe.md):
      - sprk_isactive = true on EVERY node (LOAD-BEARING — default is false)
      - sprk_actionid FK set for dispatchable nodes that need one (AI / Workflow)
      - Control nodes (Start, Has X?, Check Results) carry sprk_actionid = null,
        matching the deployed PB-018 pattern (Control nodes work via __actionType
        structural fallback per PlaybookOrchestrationService.cs:1116)
      - sprk_configjson carries __actionType + executor-specific bindings
      - sprk_dependsonjson is PATCHed in second pass with resolved node Guids
      - sprk_canvaslayoutjson is PATCHed on the playbook with React Flow JSON

    Limitations vs. Deploy-Playbook.ps1:
      - NO N:N scope writes (playbook-level scopes already exist on these 5 rows)
      - NO node-level scope writes (none in repo JSON)
      - NO model deployment resolution (Action rows carry model assignment via FK)
      - NO actionCode lint (intentional — see comments above)
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$PlaybookId,         # GUID of existing sprk_analysisplaybook row

    [Parameter(Mandatory=$true)]
    [string]$DefinitionFile,     # Path to repo JSON file

    [string]$DataverseUrl = ($env:DATAVERSE_URL ?? 'https://spaarkedev1.crm.dynamics.com')
)

$ErrorActionPreference = 'Stop'
$ApiBase = "$DataverseUrl/api/data/v9.2"

# Action codes (resolved 2026-06-26 via MCP audit)
$ActionCodeToGuid = @{
    'SYS-QUERY-DV'                = 'ef7747ca-2b6f-f111-ab0e-7ced8ddc4cc6'
    'SYS-CREATE-NOTIF'            = 'f97747ca-2b6f-f111-ab0e-7ced8ddc4cc6'
    'SYS-LOOKUP-MEMBERSHIP'       = 'ca44b7aa-fc70-f111-ab0e-7ced8ddc4cc6'
    'BRIEF-NARRATE-TLDR'          = 'ce299eb4-fc70-f111-ab0e-7ced8ddc4cc6'
    'BRIEF-NARRATE-CHANNEL'       = 'dc3533c0-fc70-f111-ab0e-7ced8ddc4cc6'
    'BRIEF-VALIDATE-ENTITY-NAMES' = '290e786c-ff70-f111-ab0e-7ced8ddc4cc6'
}

# Map repo JSON nodeType + __actionType to Action FK + NodeType option-set int.
# - nodeType "Control" -> NodeType 100000002, no action FK (Start, Has X?, Check Results)
# - nodeType "Workflow" with actionType 52 -> NodeType 100000003, FK to SYS-LOOKUP-MEMBERSHIP
# - nodeType "Workflow" with actionType 51 -> NodeType 100000003, FK to SYS-QUERY-DV
# - nodeType "Workflow" with actionType 50 -> NodeType 100000003, FK to SYS-CREATE-NOTIF
# - nodeType "AiAnalysis" -> NodeType 100000000, FK by inline configJson.actionCode
# - nodeType "Tool" -> NodeType 100000000, FK by inline configJson.actionCode
$NodeTypeMap = @{
    'Control'    = 100000002
    'Workflow'   = 100000003
    'AiAnalysis' = 100000000  # PowerShell hashtable keys are case-insensitive — covers AIAnalysis too
    'Tool'       = 100000000  # treat as AIAnalysis - registered executor
    'Output'     = 100000001
}

# ActionType -> default action FK lookup (for nodes without inline actionCode)
$ActionTypeToFk = @{
    50 = 'SYS-CREATE-NOTIF'
    51 = 'SYS-QUERY-DV'
    52 = 'SYS-LOOKUP-MEMBERSHIP'
}

function Get-Headers {
    $token = az account get-access-token --resource $DataverseUrl --query 'accessToken' -o tsv 2>$null
    if (-not $token) { throw "Failed to acquire access token. Run 'az login' first." }
    return @{
        'Authorization'    = "Bearer $token"
        'Accept'           = 'application/json'
        'Content-Type'     = 'application/json; charset=utf-8'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        'Prefer'           = 'odata.include-annotations="*"'
    }
}

function Invoke-DvPost {
    param([string]$Endpoint, [hashtable]$Body, [hashtable]$Headers)
    $uri = "$ApiBase/$Endpoint"
    $jsonBody = $Body | ConvertTo-Json -Depth 30 -Compress
    try {
        $response = Invoke-WebRequest -Uri $uri -Headers $Headers -Method Post `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody))
    } catch {
        $errMsg = $_.ErrorDetails.Message
        if (-not $errMsg) { $errMsg = $_.Exception.Message }
        throw "POST $Endpoint failed: $errMsg`nBody: $jsonBody"
    }
    $entityIdHeader = $response.Headers['OData-EntityId']
    if ($entityIdHeader) {
        $h = if ($entityIdHeader -is [array]) { $entityIdHeader[0] } else { $entityIdHeader }
        if ($h -match '\(([0-9a-fA-F\-]{36})\)') { return $Matches[1] }
    }
    return $null
}

function Invoke-DvPatch {
    param([string]$Endpoint, [hashtable]$Body, [hashtable]$Headers)
    $uri = "$ApiBase/$Endpoint"
    $jsonBody = $Body | ConvertTo-Json -Depth 30 -Compress
    Invoke-RestMethod -Uri $uri -Headers $Headers -Method Patch `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody)) | Out-Null
}

Write-Host "=== R4 Playbook Node Deployer ===" -ForegroundColor Cyan
Write-Host "Playbook ID: $PlaybookId"
Write-Host "Definition : $DefinitionFile"
Write-Host "Environment: $DataverseUrl"
Write-Host ""

if (-not (Test-Path $DefinitionFile)) { throw "Definition file not found: $DefinitionFile" }
$definition = Get-Content $DefinitionFile -Raw -Encoding utf8 | ConvertFrom-Json -Depth 30
$headers = Get-Headers

# Map of repo-name -> Dataverse GUID for second-pass dependency resolution
$nodeIdMap = @{}

# -- Step 1: Create each node ----------------------------------------------------
Write-Host "[1/3] Creating $($definition.nodes.Count) node rows..." -ForegroundColor Yellow
$idx = 0
foreach ($node in $definition.nodes) {
    $idx++
    $rawType = if ($node.nodeType) { [string]$node.nodeType } else { 'AIAnalysis' }
    $nodeTypeInt = $NodeTypeMap[$rawType]
    if ($null -eq $nodeTypeInt) { throw "Unknown nodeType '$rawType' for node '$($node.name)'" }

    # Resolve FK: first try inline configJson.actionCode, else map by actionType int
    $actionFkGuid = $null
    if ($node.configJson -and $node.configJson.actionCode) {
        $code = [string]$node.configJson.actionCode
        if ($ActionCodeToGuid.ContainsKey($code)) { $actionFkGuid = $ActionCodeToGuid[$code] }
    }
    if (-not $actionFkGuid -and $null -ne $node.actionType) {
        $atInt = [int]$node.actionType
        if ($ActionTypeToFk.ContainsKey($atInt)) {
            $code = $ActionTypeToFk[$atInt]
            $actionFkGuid = $ActionCodeToGuid[$code]
        }
    }

    $configJsonStr = if ($node.configJson) { $node.configJson | ConvertTo-Json -Depth 30 -Compress } else { $null }

    $body = @{
        sprk_name           = $node.name
        sprk_nodetype       = $nodeTypeInt
        sprk_executionorder = $idx
        sprk_isactive       = $true   # LOAD-BEARING — Deploy-Playbook.ps1:823
        'sprk_playbookid@odata.bind' = "sprk_analysisplaybooks($PlaybookId)"
    }
    if ($node.outputVariable) { $body['sprk_outputvariable'] = [string]$node.outputVariable }
    if ($configJsonStr) { $body['sprk_configjson'] = $configJsonStr }
    if ($actionFkGuid) { $body['sprk_actionid@odata.bind'] = "sprk_analysisactions($actionFkGuid)" }

    $newId = Invoke-DvPost -Endpoint 'sprk_playbooknodes' -Body $body -Headers $headers
    if (-not $newId) { throw "Failed to create node '$($node.name)' - no record id returned" }
    $nodeIdMap[[string]$node.name] = $newId
    $fkDisplay = if ($actionFkGuid) { "FK=$actionFkGuid" } else { "FK=null" }
    Write-Host "  [$idx] $($node.name) (NodeType=$nodeTypeInt, $fkDisplay) -> $newId" -ForegroundColor Green
}

# -- Step 2: Second pass — write sprk_dependsonjson -----------------------------
Write-Host ""
Write-Host "[2/3] Writing sprk_dependsonjson..." -ForegroundColor Yellow
$depCount = 0
foreach ($node in $definition.nodes) {
    if (-not $node.dependsOn -or $node.dependsOn.Count -eq 0) { continue }
    $nodeId = $nodeIdMap[[string]$node.name]
    $depGuids = @()
    foreach ($depName in $node.dependsOn) {
        $key = [string]$depName
        if (-not $nodeIdMap.ContainsKey($key)) {
            throw "Node '$($node.name)' depends on '$depName' which was not created."
        }
        $depGuids += $nodeIdMap[$key]
    }
    # ConvertTo-Json with single-item array — force array shape
    $jsonStr = if ($depGuids.Count -eq 1) { "[`"$($depGuids[0])`"]" } else { $depGuids | ConvertTo-Json -Compress }
    Invoke-DvPatch -Endpoint "sprk_playbooknodes($nodeId)" -Body @{ sprk_dependsonjson = $jsonStr } -Headers $headers
    Write-Host "  $($node.name) -> $($node.dependsOn -join ', ')" -ForegroundColor Green
    $depCount++
}
if ($depCount -eq 0) { Write-Host "  No node dependencies." -ForegroundColor Gray }

# -- Step 3: Write sprk_canvaslayoutjson on playbook -----------------------------
Write-Host ""
Write-Host "[3/3] Writing sprk_canvaslayoutjson..." -ForegroundColor Yellow
$canvasNodes = @()
$canvasEdges = @()
foreach ($node in $definition.nodes) {
    $name = [string]$node.name
    $nodeId = $nodeIdMap[$name]
    $rawType = if ($node.nodeType) { [string]$node.nodeType } else { 'AIAnalysis' }
    $canvasType = switch ($rawType) {
        'AiAnalysis' { 'aiAnalysis' }
        'AIAnalysis' { 'aiAnalysis' }
        'Tool'       { 'aiAnalysis' }
        'Workflow'   { 'aiAnalysis' }
        'Control'    { 'control' }
        'Output'     { 'output' }
        default      { 'aiAnalysis' }
    }
    $px = if ($null -ne $node.positionX) { [int]$node.positionX } else { 100 + ($idx - 1) * 300 }
    $py = if ($null -ne $node.positionY) { [int]$node.positionY } else { 200 }
    $canvasNodes += @{
        id = $nodeId
        type = $canvasType
        position = @{ x = $px; y = $py }
        data = @{ label = $name; type = $canvasType; isConfigured = $true; validationErrors = @() }
    }
    if ($node.dependsOn) {
        foreach ($depName in $node.dependsOn) {
            $key = [string]$depName
            if ($nodeIdMap.ContainsKey($key)) {
                $src = $nodeIdMap[$key]
                $canvasEdges += @{
                    id = "e-$src-$nodeId"
                    source = $src
                    target = $nodeId
                    type = 'smoothstep'
                    animated = $false
                }
            }
        }
    }
}
$canvas = @{ viewport = @{ x = 0; y = 0; zoom = 1.0 }; nodes = $canvasNodes; edges = $canvasEdges; version = 1 }
$canvasJson = $canvas | ConvertTo-Json -Depth 20 -Compress
Invoke-DvPatch -Endpoint "sprk_analysisplaybooks($PlaybookId)" `
    -Body @{ sprk_canvaslayoutjson = $canvasJson } -Headers $headers
Write-Host "  $($canvasNodes.Count) nodes / $($canvasEdges.Count) edges written." -ForegroundColor Green

Write-Host ""
Write-Host "=== Deployment Complete ===" -ForegroundColor Cyan
Write-Host "  Playbook: $PlaybookId"
Write-Host "  Nodes   : $($nodeIdMap.Count)"
Write-Host "  Deps    : $depCount"
Write-Host ""
