<#
.SYNOPSIS
    Deploys a complete playbook to Dataverse from a definition JSON file.

.DESCRIPTION
    Unified provisioning script that creates a playbook with nodes, scope
    associations, dependencies, and canvas layout in Dataverse. The script
    resolves scope codes (ACT-*, SKL-*, KNW-*, TL-*) to Dataverse record GUIDs,
    creates all records, wires N:N relationships, and builds the canvas layout.

    This script is idempotent when run without -Force — it skips playbooks that
    already exist by name. Use -Force to delete and recreate.

    Authentication uses Azure CLI (az account get-access-token).

.PARAMETER DefinitionFile
    Path to the playbook definition JSON file.

.PARAMETER Environment
    Target Dataverse environment. Default: dev

.PARAMETER DryRun
    Preview all operations without making any POST/PATCH calls.

.PARAMETER Force
    Delete and recreate the playbook if it already exists.

.EXAMPLE
    # Dry run — preview what would be created
    .\Deploy-Playbook.ps1 -DefinitionFile ./playbooks/contract-review.json -DryRun

.EXAMPLE
    # Deploy to dev
    .\Deploy-Playbook.ps1 -DefinitionFile ./playbooks/contract-review.json

.EXAMPLE
    # Force recreate
    .\Deploy-Playbook.ps1 -DefinitionFile ./playbooks/contract-review.json -Force

.NOTES
    Entities:
      sprk_analysisplaybooks     — Playbook records
      sprk_playbooknodes         — Playbook node records
      sprk_analysisactions       — Actions (scope)
      sprk_analysisskills        — Skills (scope)
      sprk_analysisknowledges    — Knowledge sources (scope)
      sprk_analysistools         — Tools (scope)
      sprk_aimodeldeployments    — AI model deployments

    Prerequisites:
      - Azure CLI installed and authenticated (az login)
      - Scope records (Actions, Skills, Knowledge, Tools) must already exist
      - Model deployment records must already exist
      - PowerShell Core 7+ recommended
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$DefinitionFile,

    [ValidateSet('dev')]
    [string]$Environment = 'dev',

    [string]$DataverseUrl = $env:DATAVERSE_URL,

    [switch]$DryRun,

    [switch]$Force,

    # ----- chat-routing-redesign-r1 FR-14e: JSON-Schema validation gate -----
    # Path to the node-routing-config JSON Schema (Draft 2020-12). Each playbook
    # node's sprk_configjson is validated against this schema BEFORE the Dataverse
    # POST. The C# source of truth is Sprk.Bff.Api.Models.Ai.NodeRoutingConfig;
    # this schema is its mechanical projection (per task 052 / POML notes). If
    # the NodeDestination enum changes in C#, the schema must update in lockstep.
    [string]$SchemaPath = "$PSScriptRoot\schemas\node-routing-config.schema.json",

    # Emergency override. When set, skips the FR-14e schema validation gate and
    # emits a Write-Warning per skipped node. Reserved for break-glass scenarios
    # (e.g. validating a hot fix against an environment with a slightly stale
    # schema); routine deploys MUST NOT use this switch.
    [switch]$SkipValidation
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Environment URL resolution
# ---------------------------------------------------------------------------
if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$EnvironmentUrl = $DataverseUrl
$ApiBase = "$EnvironmentUrl/api/data/v9.2"

# ---------------------------------------------------------------------------
# Header
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '=== Deploy Playbook ===' -ForegroundColor Cyan
Write-Host "Definition : $DefinitionFile"
Write-Host "Environment: $EnvironmentUrl"
if ($DryRun) {
    Write-Host 'Mode       : DRY RUN' -ForegroundColor Yellow
} else {
    Write-Host 'Mode       : LIVE' -ForegroundColor Green
}
Write-Host ''

# ---------------------------------------------------------------------------
# Dataverse helpers
# ---------------------------------------------------------------------------
function Get-DataverseHeaders {
    param([string]$BearerToken)
    return @{
        'Authorization'    = "Bearer $BearerToken"
        'Accept'           = 'application/json'
        'Content-Type'     = 'application/json; charset=utf-8'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
    }
}

function Invoke-DataverseGet {
    param(
        [string]$Endpoint,
        [hashtable]$Headers
    )
    $uri = "$ApiBase/$Endpoint"
    try {
        $response = Invoke-RestMethod -Uri $uri -Headers $Headers -Method Get
        return $response
    } catch {
        return $null
    }
}

function Invoke-DataversePost {
    param(
        [string]$Endpoint,
        [hashtable]$Body,
        [hashtable]$Headers
    )
    $uri = "$ApiBase/$Endpoint"
    $jsonBody = $Body | ConvertTo-Json -Depth 20 -Compress
    Write-Verbose "POST $uri`n$jsonBody"
    try {
        $response = Invoke-WebRequest -Uri $uri -Headers $Headers -Method Post `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody))
    } catch {
        $errMsg = $_.ErrorDetails.Message
        if (-not $errMsg -and $_.Exception.Response) {
            try {
                $errMsg = $_.Exception.Response.Content.ReadAsStringAsync().Result
            } catch { $errMsg = $_.Exception.Message }
        }
        if (-not $errMsg) { $errMsg = $_.Exception.Message }
        throw "POST $Endpoint failed: $errMsg"
    }
    # Extract the created record ID from the OData-EntityId header
    $entityId = $null
    $entityIdHeader = $response.Headers['OData-EntityId']
    if ($entityIdHeader) {
        $headerValue = if ($entityIdHeader -is [array]) { $entityIdHeader[0] } else { $entityIdHeader }
        if ($headerValue -match '\(([0-9a-fA-F\-]{36})\)') {
            $entityId = $Matches[1]
        }
    }
    return $entityId
}

function Invoke-DataversePatch {
    param(
        [string]$Endpoint,
        [hashtable]$Body,
        [hashtable]$Headers
    )
    $uri = "$ApiBase/$Endpoint"
    $jsonBody = $Body | ConvertTo-Json -Depth 20 -Compress
    Invoke-RestMethod -Uri $uri -Headers $Headers -Method Patch `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody)) | Out-Null
}

function Invoke-DataverseDelete {
    param(
        [string]$Endpoint,
        [hashtable]$Headers
    )
    $uri = "$ApiBase/$Endpoint"
    Invoke-RestMethod -Uri $uri -Headers $Headers -Method Delete | Out-Null
}

# ---------------------------------------------------------------------------
# Scope resolution helper
# ---------------------------------------------------------------------------
function Resolve-ScopeCode {
    param(
        [string]$EntitySet,
        [string]$FilterField,
        [string]$Code,
        [string]$SelectField,
        [hashtable]$Headers
    )

    $encodedCode = [Uri]::EscapeDataString($Code)
    $filter = "`$filter=$FilterField eq '$encodedCode'"
    $select = "`$select=$SelectField,sprk_name"
    $endpoint = "${EntitySet}?${filter}&${select}"

    $result = Invoke-DataverseGet -Endpoint $endpoint -Headers $Headers
    if ($result -and $result.value -and $result.value.Count -gt 0) {
        return @{
            Id   = $result.value[0].$SelectField
            Name = $result.value[0].sprk_name
        }
    }
    return $null
}

# ---------------------------------------------------------------------------
# N:N association helper
# ---------------------------------------------------------------------------
function Associate-NtoN {
    param(
        [string]$SourceEntitySet,
        [guid]$SourceId,
        [string]$RelationshipName,
        [string]$TargetEntitySet,
        [guid]$TargetId,
        [hashtable]$Headers
    )

    $uri = "$ApiBase/$SourceEntitySet($SourceId)/$RelationshipName/`$ref"
    $body = @{
        '@odata.id' = "$ApiBase/$TargetEntitySet($TargetId)"
    } | ConvertTo-Json -Depth 2

    try {
        Invoke-RestMethod -Uri $uri -Headers $Headers -Method Post `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) | Out-Null
        return $true
    } catch {
        # 409 = already associated — treat as success
        if ($_.Exception.Response.StatusCode -eq 409 -or
            $_.Exception.Response.StatusCode.value__ -eq 409) {
            return $true
        }
        Write-Warning "  Association failed: $($_.Exception.Message)"
        return $false
    }
}

# ---------------------------------------------------------------------------
# Executor-type allow-list (R7 FR-20 — single-hop dispatch)
# ---------------------------------------------------------------------------
# Source of truth: src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs
# `public enum ExecutorType` (post-Wave 2 task 022 rename from `ActionType`).
# Each playbook node deploy writes the integer value below into
# sprk_playbooknode.sprk_executortype (a Dataverse global Choice). Reject deploy
# if a node's executorType is not in this list.
#
# When the C# enum grows past 33 values, update this hashtable in lockstep.
# Tracked for codegen replacement as DEF-NNN (file via /devops-idea-create if
# manual sync becomes a pain point). Per R7 task 055 POML default — inline
# constant array is the chosen mechanism over fragile regex parsing of the
# C# enum file.
$KnownExecutorTypes = @{
    0   = 'AiAnalysis'
    1   = 'AiCompletion'
    2   = 'AiEmbedding'
    10  = 'RuleEngine'
    11  = 'Calculation'
    12  = 'DataTransform'
    20  = 'CreateTask'
    21  = 'SendEmail'
    22  = 'UpdateRecord'
    23  = 'CallWebhook'
    24  = 'SendTeamsMessage'
    30  = 'Condition'
    31  = 'Parallel'
    32  = 'Wait'
    33  = 'Start'
    40  = 'DeliverOutput'
    41  = 'DeliverToIndex'
    42  = 'DeliverComposite'   # ADR-037 multi-section delivery (chat-routing-redesign-r1 FR-52)
    50  = 'CreateNotification'
    51  = 'QueryDataverse'
    52  = 'LookupUserMembership'
    60  = 'AgentService'
    70  = 'GroundingVerify'
    80  = 'LiveFact'
    90  = 'IndexRetrieve'
    100 = 'EvidenceSufficiency'
    110 = 'DeclineToFind'
    120 = 'ReturnInsightArtifact'
    130 = 'Sanitization'
    140 = 'ObservationEmit'
    141 = 'EntityNameValidator'
    142 = 'LoadKnowledge'
    143 = 'ReturnResponse'
}

# ---------------------------------------------------------------------------
# Backward-compat: legacy `nodeType` (string friendly label) -> ExecutorType (int)
# ---------------------------------------------------------------------------
# Existing playbook JSON files (R3/R4-era) use friendly `nodeType` strings.
# This map lets them deploy without modification while new playbook JSONs
# SHOULD set `executorType: <int>` directly. The map is INPUT CONVENIENCE
# ONLY; the column being written is sprk_executortype, not nodeType.
$LegacyNodeTypeToExecutorType = @{
    'AIAnalysis'           = 0   # AiAnalysis
    'AiCompletion'         = 1
    'AiEmbedding'          = 2
    'Output'               = 40  # DeliverOutput (legacy single-section)
    'DeliverOutput'        = 40
    'DeliverComposite'     = 42
    'DeliverToIndex'       = 41
    'Control'              = 30  # Condition (conservative default; explicit executorType wins when present)
    'Condition'            = 30
    'Start'                = 33
    'LoadKnowledge'        = 142
    'ReturnResponse'       = 143
    'Workflow'             = 20  # CreateTask (conservative default for legacy "Workflow" label)
    'CreateTask'           = 20
    'CreateNotification'   = 50
    'EntityNameValidator'  = 141
    'Sanitization'         = 130
    'ObservationEmit'      = 140
}

# ===========================================================================
# Step 1: Authenticate
# ===========================================================================
Write-Host '[1/12] Authenticating...' -ForegroundColor Yellow

$headers = $null
if (-not $DryRun) {
    Write-Host '  Acquiring token via Azure CLI...' -ForegroundColor Gray
    $token = az account get-access-token --resource $EnvironmentUrl --query 'accessToken' -o tsv 2>$null

    if (-not $token) {
        throw "Failed to acquire access token. Run 'az login' first."
    }

    $headers = Get-DataverseHeaders -BearerToken $token
    Write-Host '  Token acquired.' -ForegroundColor Green
} else {
    Write-Host '  Skipped (dry run).' -ForegroundColor Gray
}

# ===========================================================================
# Step 2: Load & validate definition JSON
# ===========================================================================
Write-Host ''
Write-Host '[2/12] Loading definition...' -ForegroundColor Yellow

if (-not (Test-Path $DefinitionFile)) {
    throw "Definition file not found: $DefinitionFile"
}

$definitionContent = Get-Content $DefinitionFile -Raw -Encoding utf8
try {
    $definition = $definitionContent | ConvertFrom-Json -Depth 20
} catch {
    throw "Invalid JSON in definition file: $($_.Exception.Message)"
}

# Validate required fields
if (-not $definition.playbook) {
    throw "Definition file must contain a 'playbook' property."
}
if (-not $definition.playbook.name) {
    throw "Playbook definition must have a 'name' property."
}
if (-not $definition.nodes -or $definition.nodes.Count -eq 0) {
    throw "Playbook definition must have at least one node."
}

# ===========================================================================
# Lint A: executor-type validation per node (R7 task 055 FR-20)
# ===========================================================================
# Every node MUST resolve to one of the 33 known sprk_playbookexecutortype
# Choice values BEFORE any Dataverse write. This catches author errors at
# deploy time instead of at orchestrator dispatch time (where an unknown
# value would silently no-op against the executor registry).
#
# Resolution order per node (matches what Step 8 will write):
#   1. node.executorType (preferred; int)
#   2. legacy mapping: node.nodeType (string) -> $LegacyNodeTypeToExecutorType
#   3. otherwise -> lint FAIL with named offending node
#
# Lint runs BEFORE the deploy loop. If any node fails, exit 1 — no partial
# deploy (which could leave some nodes new-shape + some old-shape).
$nodesFailingExecutorTypeLint = @()
foreach ($lintNode in $definition.nodes) {
    $resolvedExecutorType = $null
    $resolutionSource     = $null

    if ($null -ne $lintNode.executorType) {
        # Explicit field on the node — R7 preferred convention.
        $resolvedExecutorType = [int]$lintNode.executorType
        $resolutionSource     = 'executorType field'
    } elseif ($lintNode.nodeType -and $LegacyNodeTypeToExecutorType.ContainsKey($lintNode.nodeType)) {
        # Legacy R3/R4 friendly-label fallback (input convenience only).
        $resolvedExecutorType = $LegacyNodeTypeToExecutorType[$lintNode.nodeType]
        $resolutionSource     = "legacy nodeType '$($lintNode.nodeType)' -> $resolvedExecutorType"
    }

    if ($null -eq $resolvedExecutorType -or -not $KnownExecutorTypes.ContainsKey($resolvedExecutorType)) {
        $nodesFailingExecutorTypeLint += [pscustomobject]@{
            Name            = $lintNode.name
            ExecutorType    = $lintNode.executorType
            NodeType        = $lintNode.nodeType
            Resolution      = $resolutionSource
            ResolvedValue   = $resolvedExecutorType
        }
    }
}
if ($nodesFailingExecutorTypeLint.Count -gt 0) {
    Write-Host ''
    Write-Host '❌ LINT FAILED — executor-type validation (R7 FR-20)' -ForegroundColor Red
    Write-Host ('The following nodes do not resolve to a known sprk_playbookexecutortype Choice value:') -ForegroundColor Red
    foreach ($n in $nodesFailingExecutorTypeLint) {
        $detail = "  - $($n.Name): executorType=$($n.ExecutorType), nodeType=$($n.NodeType), resolved=$($n.ResolvedValue)"
        Write-Host $detail -ForegroundColor Red
    }
    Write-Host ''
    Write-Host 'Fix:' -ForegroundColor Yellow
    Write-Host '  Set the node `executorType` field to one of the 33 known integer values:' -ForegroundColor Yellow
    foreach ($k in ($KnownExecutorTypes.Keys | Sort-Object)) {
        Write-Host "    $k => $($KnownExecutorTypes[$k])" -ForegroundColor Gray
    }
    Write-Host ''
    Write-Host '  OR set the node `nodeType` field to a legacy friendly label that maps to one of the above.' -ForegroundColor Yellow
    Write-Host '  Source of truth: src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs (enum ExecutorType).' -ForegroundColor Gray
    throw "Playbook lint failed: $($nodesFailingExecutorTypeLint.Count) of $($definition.nodes.Count) nodes have unknown/missing executorType."
}
Write-Host "  Lint A  : ✅ all $($definition.nodes.Count) nodes resolve to a known sprk_playbookexecutortype Choice value (R7 FR-20)" -ForegroundColor Green

# ===========================================================================
# Lint B: action-code wiring per node (Wave B B3 per Insights Engine r2 D-01;
# R7 2026-06-29 refined to be prompt-driven-only)
# ===========================================================================
# Prompt-driven nodes (AiAnalysis=0, AiCompletion=1, AiEmbedding=2) MUST carry
# an `actionCode` so this script can resolve it to a sprk_analysisaction row
# and set sprk_playbooknode.sprk_actionid (the prompt template's home).
#
# Pure executors (Condition=30, Start=33, CreateNotification=50,
# LookupUserMembership=52, QueryDataverse=51, etc.) do NOT use Actions — their
# config lives entirely in sprk_configjson on the node. Requiring actionCode
# for these would block legitimate notification/control-flow playbooks
# (observed 2026-06-29 when re-deploying notification-tasks-due-soon.json).
#
# Exemptions:
#   - DeliverComposite (ExecutorType 42, ADR-037) — code-registered structural
#     executor (reads sections + destination from configjson, not Action FK).
#   - All non-prompt-driven executors per R7 single-hop dispatch model.
$promptDrivenExecutorTypes = @(0, 1, 2)  # AiAnalysis, AiCompletion, AiEmbedding
$nodesMissingActionCode = @()
foreach ($lintNode in $definition.nodes) {
    # Resolve effective executorType (same logic as Lint A)
    $effectiveType = $null
    if ($null -ne $lintNode.executorType) {
        $effectiveType = [int]$lintNode.executorType
    } elseif ($lintNode.nodeType -and $LegacyNodeTypeToExecutorType.ContainsKey([string]$lintNode.nodeType)) {
        $effectiveType = [int]$LegacyNodeTypeToExecutorType[[string]$lintNode.nodeType]
    }
    # Exempt non-prompt-driven executors + DeliverComposite (covered by inclusion check below)
    if ($null -eq $effectiveType -or -not ($promptDrivenExecutorTypes -contains $effectiveType)) {
        continue
    }
    if (-not $lintNode.actionCode) {
        $nodesMissingActionCode += $lintNode.name
    }
}
if ($nodesMissingActionCode.Count -gt 0) {
    Write-Host ''
    Write-Host '❌ LINT FAILED — action-code wiring missing' -ForegroundColor Red
    Write-Host ('The following nodes lack `actionCode` references:') -ForegroundColor Red
    foreach ($missingName in $nodesMissingActionCode) {
        Write-Host "  - $missingName" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host 'Why this matters:' -ForegroundColor Yellow
    Write-Host '  Every dispatchable node MUST reference a sprk_analysisaction row via `actionCode`.' -ForegroundColor Yellow
    Write-Host '  Without it, this script cannot set sprk_playbooknode.sprk_actionid,' -ForegroundColor Yellow
    Write-Host '  and the orchestrator dispatch falls back to canvas-Designer configjson' -ForegroundColor Yellow
    Write-Host '  (which gets clobbered if the playbook is opened in the Designer).' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  Exception: DeliverComposite nodes (ADR-037) are code-registered and exempt.' -ForegroundColor Gray
    Write-Host ''
    Write-Host 'Reference: projects/ai-spaarke-insights-engine-r2/decisions/D-01-wave-b-root-cause-corrected.md' -ForegroundColor Gray
    throw "Playbook lint failed: $($nodesMissingActionCode.Count) of $($definition.nodes.Count) nodes missing actionCode."
}
Write-Host "  Lint B  : ✅ all dispatchable nodes have actionCode wiring (DeliverComposite nodes exempt)" -ForegroundColor Green

$playbookName = $definition.playbook.name
$playbookDescription = if ($definition.playbook.description) { $definition.playbook.description } else { '' }
$playbookIsPublic = if ($null -ne $definition.playbook.isPublic) { $definition.playbook.isPublic } else { $true }
$playbookType = if ($null -ne $definition.playbook.sprk_playbooktype) { $definition.playbook.sprk_playbooktype } elseif ($null -ne $definition.playbook.playbookType) { $definition.playbook.playbookType } else { $null }
$playbookIsSystem = if ($null -ne $definition.playbook.isSystemPlaybook) { $definition.playbook.isSystemPlaybook } else { $false }
$playbookConfigJson = if ($definition.playbook.sprk_configjson) { $definition.playbook.sprk_configjson | ConvertTo-Json -Depth 10 -Compress } elseif ($definition.playbook.configJson) { $definition.playbook.configJson | ConvertTo-Json -Depth 10 -Compress } else { $null }
$nodeCount = $definition.nodes.Count

Write-Host "  Playbook: $playbookName" -ForegroundColor White
Write-Host "  Nodes   : $nodeCount" -ForegroundColor White

# Collect all unique scope codes from playbook-level and node-level
$allActionCodes    = @()
$allSkillCodes     = @()
$allKnowledgeCodes = @()
$allToolCodes      = @()
$allModelNames     = @()

# Playbook-level scopes (support both flat and nested .scopes format)
$pbScopesSrc = if ($definition.playbook.scopes) { $definition.playbook.scopes } else { $definition.playbook }
if ($pbScopesSrc.actions)   { $allActionCodes    += $pbScopesSrc.actions }
if ($pbScopesSrc.skills)    { $allSkillCodes     += $pbScopesSrc.skills }
if ($pbScopesSrc.knowledge) { $allKnowledgeCodes += $pbScopesSrc.knowledge }
if ($pbScopesSrc.tools)     { $allToolCodes      += $pbScopesSrc.tools }

# Node-level scopes and models (support both flat and nested .scopes format)
foreach ($node in $definition.nodes) {
    if ($node.actionCode) { $allActionCodes += $node.actionCode }
    $nodeScopesSrc = if ($node.scopes) { $node.scopes } else { $node }
    if ($nodeScopesSrc.skills)     { $allSkillCodes += $nodeScopesSrc.skills }
    if ($nodeScopesSrc.knowledge)  { $allKnowledgeCodes += $nodeScopesSrc.knowledge }
    if ($nodeScopesSrc.tools)      { $allToolCodes += $nodeScopesSrc.tools }
    if ($node.model)               { $allModelNames += $node.model }
}

$allActionCodes    = $allActionCodes    | Select-Object -Unique
$allSkillCodes     = $allSkillCodes     | Select-Object -Unique
$allKnowledgeCodes = $allKnowledgeCodes | Select-Object -Unique
$allToolCodes      = $allToolCodes      | Select-Object -Unique
$allModelNames     = $allModelNames     | Select-Object -Unique

# ===========================================================================
# Step 3: Resolve scope codes to GUIDs
# ===========================================================================
Write-Host ''
Write-Host '[3/12] Resolving scope codes...' -ForegroundColor Yellow

$resolvedActions    = @{}
$resolvedSkills     = @{}
$resolvedKnowledge  = @{}
$resolvedTools      = @{}
$resolutionErrors   = @()

if ($DryRun) {
    Write-Host '  Skipped (dry run) — would resolve:' -ForegroundColor Gray
    foreach ($code in $allActionCodes)    { Write-Host "    Action    : $code" -ForegroundColor Gray }
    foreach ($code in $allSkillCodes)     { Write-Host "    Skill     : $code" -ForegroundColor Gray }
    foreach ($code in $allKnowledgeCodes) { Write-Host "    Knowledge : $code" -ForegroundColor Gray }
    foreach ($code in $allToolCodes)      { Write-Host "    Tool      : $code" -ForegroundColor Gray }
} else {
    # Resolve Actions
    foreach ($code in $allActionCodes) {
        $resolved = Resolve-ScopeCode -EntitySet 'sprk_analysisactions' -FilterField 'sprk_actioncode' `
            -Code $code -SelectField 'sprk_analysisactionid' -Headers $headers
        if ($resolved) {
            $resolvedActions[$code] = $resolved
            Write-Host "  $code -> $($resolved.Id) ($($resolved.Name))" -ForegroundColor Green
        } else {
            $resolutionErrors += "Action '$code' not found"
            Write-Host "  $code -> NOT FOUND" -ForegroundColor Red
        }
    }

    # Resolve Skills
    foreach ($code in $allSkillCodes) {
        $resolved = Resolve-ScopeCode -EntitySet 'sprk_analysisskills' -FilterField 'sprk_skillcode' `
            -Code $code -SelectField 'sprk_analysisskillid' -Headers $headers
        if ($resolved) {
            $resolvedSkills[$code] = $resolved
            Write-Host "  $code -> $($resolved.Id) ($($resolved.Name))" -ForegroundColor Green
        } else {
            $resolutionErrors += "Skill '$code' not found"
            Write-Host "  $code -> NOT FOUND" -ForegroundColor Red
        }
    }

    # Resolve Knowledge
    foreach ($code in $allKnowledgeCodes) {
        $resolved = Resolve-ScopeCode -EntitySet 'sprk_analysisknowledges' -FilterField 'sprk_externalid' `
            -Code $code -SelectField 'sprk_analysisknowledgeid' -Headers $headers
        if ($resolved) {
            $resolvedKnowledge[$code] = $resolved
            Write-Host "  $code -> $($resolved.Id) ($($resolved.Name))" -ForegroundColor Green
        } else {
            $resolutionErrors += "Knowledge '$code' not found"
            Write-Host "  $code -> NOT FOUND" -ForegroundColor Red
        }
    }

    # Resolve Tools
    foreach ($code in $allToolCodes) {
        $resolved = Resolve-ScopeCode -EntitySet 'sprk_analysistools' -FilterField 'sprk_toolcode' `
            -Code $code -SelectField 'sprk_analysistoolid' -Headers $headers
        if ($resolved) {
            $resolvedTools[$code] = $resolved
            Write-Host "  $code -> $($resolved.Id) ($($resolved.Name))" -ForegroundColor Green
        } else {
            $resolutionErrors += "Tool '$code' not found"
            Write-Host "  $code -> NOT FOUND" -ForegroundColor Red
        }
    }

    if ($resolutionErrors.Count -gt 0) {
        Write-Host ''
        Write-Host "  $($resolutionErrors.Count) scope code(s) could not be resolved (will check models before stopping):" -ForegroundColor Yellow
        foreach ($err in $resolutionErrors) {
            Write-Host "    - $err" -ForegroundColor Red
        }
    }
}

# ===========================================================================
# Step 4: Resolve model deployments
# ===========================================================================
Write-Host ''
Write-Host '[4/12] Resolving model deployments...' -ForegroundColor Yellow

$resolvedModels = @{}
$modelErrors = @()

if ($DryRun) {
    Write-Host '  Skipped (dry run) — would resolve:' -ForegroundColor Gray
    foreach ($model in $allModelNames) { Write-Host "    Model: $model" -ForegroundColor Gray }
} else {
    foreach ($modelName in $allModelNames) {
        $encodedName = [Uri]::EscapeDataString($modelName)
        $filter = "`$filter=sprk_name eq '$encodedName'"
        $select = "`$select=sprk_aimodeldeploymentid"
        $endpoint = $filter, $select -join '&'
        $endpoint = "sprk_aimodeldeployments?$endpoint"
        $result = Invoke-DataverseGet -Endpoint $endpoint -Headers $headers

        if ($result -and $result.value -and $result.value.Count -gt 0) {
            $resolvedModels[$modelName] = $result.value[0].sprk_aimodeldeploymentid
            Write-Host "  $modelName -> $($result.value[0].sprk_aimodeldeploymentid)" -ForegroundColor Green
        } else {
            $modelErrors += "Model deployment '$modelName' not found"
            Write-Host "  $modelName -> NOT FOUND" -ForegroundColor Red
        }
    }

    if ($modelErrors.Count -gt 0) {
        Write-Host ''
        Write-Host "  $($modelErrors.Count) model deployment(s) could not be resolved:" -ForegroundColor Red
        foreach ($err in $modelErrors) {
            Write-Host "    - $err" -ForegroundColor Red
        }
    }
}

# ---------------------------------------------------------------------------
# Pre-flight check: report ALL resolution failures before creating anything
# ---------------------------------------------------------------------------
$totalErrors = $resolutionErrors.Count + $modelErrors.Count
if ($totalErrors -gt 0 -and -not $DryRun) {
    Write-Host ''
    Write-Host "=== PRE-FLIGHT FAILED ===" -ForegroundColor Red
    Write-Host "  $totalErrors unresolved reference(s) detected." -ForegroundColor Red
    Write-Host "  No records were created — Dataverse is unchanged." -ForegroundColor Red
    Write-Host ''
    Write-Host "  Missing scope records:" -ForegroundColor Red
    foreach ($err in $resolutionErrors) { Write-Host "    - $err" -ForegroundColor Red }
    foreach ($err in $modelErrors)      { Write-Host "    - $err" -ForegroundColor Red }
    Write-Host ''
    Write-Host "  Fix: Seed the missing records to Dataverse, then re-run this script." -ForegroundColor Yellow
    throw "Pre-flight failed: $totalErrors unresolved reference(s). No records created."
}

# ===========================================================================
# Step 5: Check for existing playbook
# ===========================================================================
Write-Host ''
Write-Host '[5/12] Checking for existing playbook...' -ForegroundColor Yellow

$existingPlaybookId = $null

if (-not $DryRun) {
    $encodedName = [Uri]::EscapeDataString($playbookName)
    $filter = "`$filter=sprk_name eq '$encodedName'"
    $select = "`$select=sprk_analysisplaybookid,sprk_name"
    $endpoint = $filter, $select -join '&'
    $endpoint = "sprk_analysisplaybooks?$endpoint"
    $result = Invoke-DataverseGet -Endpoint $endpoint -Headers $headers

    if ($result -and $result.value -and $result.value.Count -gt 0) {
        $existingPlaybookId = $result.value[0].sprk_analysisplaybookid

        if ($Force) {
            Write-Host "  FOUND: $existingPlaybookId — will delete and recreate (-Force)" -ForegroundColor Yellow

            # Delete existing nodes first
            $nodesFilter = "`$filter=_sprk_playbookid_value eq '$existingPlaybookId'"
            $nodesSelect = "`$select=sprk_playbooknodeid"
            $nodesEndpoint = $nodesFilter, $nodesSelect -join '&'
            $nodesEndpoint = "sprk_playbooknodes?$nodesEndpoint"
            $existingNodes = Invoke-DataverseGet -Endpoint $nodesEndpoint -Headers $headers
            if ($existingNodes -and $existingNodes.value) {
                foreach ($existingNode in $existingNodes.value) {
                    try {
                        Invoke-DataverseDelete -Endpoint "sprk_playbooknodes($($existingNode.sprk_playbooknodeid))" -Headers $headers
                        Write-Host "    Deleted node: $($existingNode.sprk_playbooknodeid)" -ForegroundColor Gray
                    } catch {
                        Write-Warning "    Failed to delete node $($existingNode.sprk_playbooknodeid): $($_.Exception.Message)"
                    }
                }
            }

            # Delete the playbook
            try {
                Invoke-DataverseDelete -Endpoint "sprk_analysisplaybooks($existingPlaybookId)" -Headers $headers
                Write-Host "    Deleted playbook: $existingPlaybookId" -ForegroundColor Gray
            } catch {
                throw "Failed to delete existing playbook: $($_.Exception.Message)"
            }

            $existingPlaybookId = $null
        } else {
            Write-Host "  FOUND: $existingPlaybookId ($playbookName)" -ForegroundColor Yellow
            Write-Host "  Playbook already exists. Use -Force to delete and recreate." -ForegroundColor Yellow
            Write-Host ''
            Write-Host '=== Deployment skipped ===' -ForegroundColor Yellow
            exit 0
        }
    } else {
        Write-Host '  NOT FOUND — will create new' -ForegroundColor Green
    }
} else {
    Write-Host '  Skipped (dry run)' -ForegroundColor Gray
}

# ===========================================================================
# Step 6: Create playbook record
# ===========================================================================
Write-Host ''
Write-Host '[6/12] Creating playbook record...' -ForegroundColor Yellow

$playbookId = $null

if ($DryRun) {
    Write-Host "  WOULD CREATE: $playbookName" -ForegroundColor Gray
    $playbookId = [guid]::NewGuid()  # Placeholder for dry run
} else {
    $playbookBody = @{
        sprk_name        = $playbookName
        sprk_description = $playbookDescription
        sprk_ispublic    = $playbookIsPublic
    }
    if ($null -ne $playbookType) { $playbookBody['sprk_playbooktype'] = $playbookType }
    if ($playbookIsSystem) { $playbookBody['sprk_issystemplaybook'] = $true }
    if ($playbookConfigJson) { $playbookBody['sprk_configjson'] = $playbookConfigJson }

    try {
        $playbookId = Invoke-DataversePost -Endpoint 'sprk_analysisplaybooks' -Body $playbookBody -Headers $headers
        if (-not $playbookId) {
            throw "POST succeeded but could not extract record ID from response headers."
        }
        Write-Host "  Created: $playbookId" -ForegroundColor Green
    } catch {
        $errDetail = $_.ErrorDetails.Message
        if (-not $errDetail) { $errDetail = $_.Exception.Message }
        throw "Failed to create playbook: $errDetail"
    }
}

# ===========================================================================
# Step 7: Associate playbook-level N:N scopes
# ===========================================================================
Write-Host ''
Write-Host '[7/12] Associating playbook scopes...' -ForegroundColor Yellow

$scopeAssociationCount = 0

# Support both flat (playbook.actions) and nested (playbook.scopes.actions) formats
$pbScopes = if ($definition.playbook.scopes) { $definition.playbook.scopes } else { $definition.playbook }

if ($DryRun) {
    if ($pbScopes.actions) {
        foreach ($code in $pbScopes.actions) { Write-Host "  WOULD LINK Action $code" -ForegroundColor Gray; $scopeAssociationCount++ }
    }
    if ($pbScopes.skills) {
        foreach ($code in $pbScopes.skills) { Write-Host "  WOULD LINK Skill $code" -ForegroundColor Gray; $scopeAssociationCount++ }
    }
    if ($pbScopes.knowledge) {
        foreach ($code in $pbScopes.knowledge) { Write-Host "  WOULD LINK Knowledge $code" -ForegroundColor Gray; $scopeAssociationCount++ }
    }
    if ($pbScopes.tools) {
        foreach ($code in $pbScopes.tools) { Write-Host "  WOULD LINK Tool $code" -ForegroundColor Gray; $scopeAssociationCount++ }
    }
} else {
    # Associate actions
    if ($pbScopes.actions) {
        foreach ($code in $pbScopes.actions) {
            $targetId = [guid]$resolvedActions[$code].Id
            $success = Associate-NtoN -SourceEntitySet 'sprk_analysisplaybooks' -SourceId ([guid]$playbookId) `
                -RelationshipName 'sprk_analysisplaybook_action' -TargetEntitySet 'sprk_analysisactions' `
                -TargetId $targetId -Headers $headers
            if ($success) {
                Write-Host "  Action $code linked" -ForegroundColor Green
                $scopeAssociationCount++
            }
        }
    }

    # Associate skills
    if ($pbScopes.skills) {
        foreach ($code in $pbScopes.skills) {
            $targetId = [guid]$resolvedSkills[$code].Id
            $success = Associate-NtoN -SourceEntitySet 'sprk_analysisplaybooks' -SourceId ([guid]$playbookId) `
                -RelationshipName 'sprk_playbook_skill' -TargetEntitySet 'sprk_analysisskills' `
                -TargetId $targetId -Headers $headers
            if ($success) {
                Write-Host "  Skill $code linked" -ForegroundColor Green
                $scopeAssociationCount++
            }
        }
    }

    # Associate knowledge
    if ($pbScopes.knowledge) {
        foreach ($code in $pbScopes.knowledge) {
            $targetId = [guid]$resolvedKnowledge[$code].Id
            $success = Associate-NtoN -SourceEntitySet 'sprk_analysisplaybooks' -SourceId ([guid]$playbookId) `
                -RelationshipName 'sprk_playbook_knowledge' -TargetEntitySet 'sprk_analysisknowledges' `
                -TargetId $targetId -Headers $headers
            if ($success) {
                Write-Host "  Knowledge $code linked" -ForegroundColor Green
                $scopeAssociationCount++
            }
        }
    }

    # Associate tools
    if ($pbScopes.tools) {
        foreach ($code in $pbScopes.tools) {
            $targetId = [guid]$resolvedTools[$code].Id
            $success = Associate-NtoN -SourceEntitySet 'sprk_analysisplaybooks' -SourceId ([guid]$playbookId) `
                -RelationshipName 'sprk_playbook_tool' -TargetEntitySet 'sprk_analysistools' `
                -TargetId $targetId -Headers $headers
            if ($success) {
                Write-Host "  Tool $code linked" -ForegroundColor Green
                $scopeAssociationCount++
            }
        }
    }
}

if ($scopeAssociationCount -eq 0) {
    Write-Host '  No playbook-level scopes defined.' -ForegroundColor Gray
}

# ===========================================================================
# Step 8: Create nodes
# ===========================================================================
Write-Host ''
Write-Host '[8/12] Creating nodes...' -ForegroundColor Yellow

# ---------------------------------------------------------------------------
# FR-14e: JSON-Schema validation gate for sprk_configjson (chat-routing-redesign-r1)
# ---------------------------------------------------------------------------
# Each node's compacted sprk_configjson is validated against the schema below
# BEFORE the POST. Authoring errors (e.g. destination = "invalid", or an unknown
# enum value) surface here, NOT at runtime.
#
# Source-of-truth invariant (per task 052 / POML notes): the C# enum
# Sprk.Bff.Api.Models.Ai.NodeDestination is the source of truth; this JSON
# Schema is its mechanical projection. If the enum changes in C# (e.g. a new
# destination is added), this schema MUST update in lockstep.
#
# Use -SkipValidation for break-glass only. Default behavior is GATED.
$schemaContent = $null
if (-not $SkipValidation) {
    if (-not (Test-Path $SchemaPath)) {
        throw "Schema file not found at: $SchemaPath. Pass -SchemaPath to override or -SkipValidation to bypass (not recommended)."
    }
    try {
        $schemaContent = Get-Content $SchemaPath -Raw -Encoding utf8
    } catch {
        throw "Failed to load schema file '$SchemaPath': $($_.Exception.Message)"
    }
    Write-Host "  Schema  : $SchemaPath" -ForegroundColor Gray
} else {
    Write-Warning "  -SkipValidation set — FR-14e configJson schema gate BYPASSED for this run."
}

# Extract a playbook code for the structured error (best-effort — the
# definition shape varies across older + newer playbook definition files).
$playbookCode = $null
if ($definition.playbook.code)         { $playbookCode = $definition.playbook.code }
elseif ($definition.playbook.sprk_code) { $playbookCode = $definition.playbook.sprk_code }
elseif ($definition.playbook.playbookCode) { $playbookCode = $definition.playbook.playbookCode }
if (-not $playbookCode) { $playbookCode = $playbookName }

# Map: node definition name -> created GUID (for dependency resolution)
$nodeIdMap = @{}
$nodeIndex = 0

foreach ($node in $definition.nodes) {
    $nodeIndex++
    $nodeName       = $node.name

    # R7 FR-20 (task 055): resolve sprk_executortype explicitly. Lint A
    # above has already validated that every node resolves successfully —
    # this block mirrors that resolution to produce the integer to write.
    if ($null -ne $node.executorType) {
        $executorTypeValue = [int]$node.executorType
    } elseif ($node.nodeType -and $LegacyNodeTypeToExecutorType.ContainsKey($node.nodeType)) {
        $executorTypeValue = $LegacyNodeTypeToExecutorType[$node.nodeType]
    } else {
        # Defensive: Lint A should have caught this. Hard-fail rather than
        # silently writing a wrong value or letting a $null into the POST.
        throw "INTERNAL: node '$nodeName' has no executorType + no legacy nodeType mapping (should have been caught by Lint A)."
    }
    $executorTypeName = $KnownExecutorTypes[$executorTypeValue]

    $actionCode     = $node.actionCode
    $modelName      = $node.model
    $outputVariable = if ($node.outputVariable) { $node.outputVariable } else { '' }
    $posX           = if ($null -ne $node.positionX) { $node.positionX } else { 100 + (($nodeIndex - 1) * 300) }
    $posY           = if ($null -ne $node.positionY) { $node.positionY } else { 200 }
    $configJson     = if ($node.configJson) { $node.configJson | ConvertTo-Json -Depth 10 -Compress } else { $null }

    $modelDisplay = if ($modelName) { $modelName } else { 'none' }
    $actionDisplay = if ($actionCode) { $actionCode } else { 'none' }

    # -----------------------------------------------------------------------
    # FR-14e schema gate — validate sprk_configjson BEFORE the POST.
    # -----------------------------------------------------------------------
    # Test-Json -Schema accepts the schema as a string (PowerShell 7+). On
    # failure, abort with a structured error including: playbook code, node
    # index, node name, and the Test-Json error. The Test-Json -ErrorAction
    # SilentlyContinue + -ErrorVariable pattern lets us capture the schema
    # violation reason cleanly instead of letting Test-Json's own throw bubble
    # up unscoped.
    if ($configJson -and -not $SkipValidation) {
        $schemaErrors = $null
        $isValid = $false
        try {
            $isValid = Test-Json -Json $configJson -Schema $schemaContent -ErrorAction SilentlyContinue -ErrorVariable schemaErrors
        } catch {
            # Some PowerShell builds throw rather than returning false. Treat
            # both paths the same — surface a structured error and abort.
            $schemaErrors = @($_.Exception.Message)
            $isValid = $false
        }
        if (-not $isValid) {
            $errDetail = if ($schemaErrors) { ($schemaErrors | ForEach-Object { $_.ToString() }) -join '; ' } else { 'unknown schema violation' }
            $errMsg = @(
                "❌ FR-14e schema validation FAILED",
                "  Playbook : $playbookCode",
                "  Node     : #$nodeIndex '$nodeName'",
                "  ConfigJson: $configJson",
                "  Reason   : $errDetail",
                "  Schema   : $SchemaPath",
                "  Fix      : Correct the node's configJson in the definition file, or update the schema if the C# NodeDestination enum changed."
            ) -join [Environment]::NewLine
            throw $errMsg
        }
    }

    if ($DryRun) {
        Write-Host "  Node $nodeIndex`: $nodeName ($actionDisplay, $modelDisplay) -> sprk_executortype = $executorTypeValue ($executorTypeName)" -ForegroundColor Gray
        $nodeIdMap[$nodeName] = [guid]::NewGuid()
    } else {
        $nodeBody = @{
            sprk_name           = $nodeName
            sprk_executortype   = $executorTypeValue   # R7 FR-20 (task 055): single-hop dispatch column. Source: $node.executorType (preferred) or $LegacyNodeTypeToExecutorType[$node.nodeType]. Legacy sprk_nodetype column was dropped pre-R7; do NOT write it.
            sprk_executionorder = $nodeIndex
            sprk_isactive       = $true   # MUST set explicitly — Dataverse column default is false, so omitting this causes PlaybookOrchestrationService.ExecutionGraph to filter out the node ("0 active nodes"). Surfaced during 2026-05-30 live smoke of predict-matter-cost@v1.
            'sprk_playbookid@odata.bind' = "sprk_analysisplaybooks($playbookId)"
        }

        if ($outputVariable) {
            $nodeBody['sprk_outputvariable'] = $outputVariable
        }
        if ($configJson) {
            $nodeBody['sprk_configjson'] = $configJson
        }
        if ($actionCode -and $resolvedActions.ContainsKey($actionCode)) {
            $actionGuid = $resolvedActions[$actionCode].Id
            $nodeBody['sprk_actionid@odata.bind'] = "sprk_analysisactions($actionGuid)"
        }
        if ($modelName -and $resolvedModels.ContainsKey($modelName)) {
            $modelGuid = $resolvedModels[$modelName]
            $nodeBody['sprk_modeldeploymentid@odata.bind'] = "sprk_aimodeldeployments($modelGuid)"
        }

        try {
            $nodeId = Invoke-DataversePost -Endpoint 'sprk_playbooknodes' -Body $nodeBody -Headers $headers
            if (-not $nodeId) {
                throw "POST succeeded but could not extract node record ID."
            }
            $nodeIdMap[$nodeName] = [guid]$nodeId
            Write-Host "  Node $nodeIndex`: $nodeName [$executorTypeName] ($actionDisplay, $modelDisplay) -> $nodeId" -ForegroundColor Green
        } catch {
            throw "Failed to create node '$nodeName': $($_.Exception.Message)"
        }
    }
}

# ===========================================================================
# Step 9: Second pass — set node dependencies
# ===========================================================================
Write-Host ''
Write-Host '[9/12] Setting node dependencies...' -ForegroundColor Yellow

$dependencyCount = 0

foreach ($node in $definition.nodes) {
    if (-not $node.dependsOn -or $node.dependsOn.Count -eq 0) {
        continue
    }

    $nodeName = $node.name
    $nodeId   = $nodeIdMap[$nodeName]

    # Build dependency JSON with resolved GUIDs
    $dependsOnGuids = @()
    foreach ($depName in $node.dependsOn) {
        if ($nodeIdMap.ContainsKey($depName)) {
            $dependsOnGuids += $nodeIdMap[$depName].ToString()
        } else {
            throw "Node '$nodeName' depends on '$depName', but that node was not found."
        }
    }

    $dependsOnJson = $dependsOnGuids | ConvertTo-Json -Compress
    # If single item, ConvertTo-Json won't wrap in array
    if ($dependsOnGuids.Count -eq 1) {
        $dependsOnJson = "[$dependsOnJson]"
    }

    if ($DryRun) {
        Write-Host "  $nodeName depends on: $($node.dependsOn -join ', ')" -ForegroundColor Gray
    } else {
        try {
            Invoke-DataversePatch -Endpoint "sprk_playbooknodes($nodeId)" `
                -Body @{ sprk_dependsonjson = $dependsOnJson } -Headers $headers
            Write-Host "  $nodeName depends on: $($node.dependsOn -join ', ')" -ForegroundColor Green
        } catch {
            Write-Warning "  Failed to set dependencies for '$nodeName': $($_.Exception.Message)"
        }
    }
    $dependencyCount++
}

if ($dependencyCount -eq 0) {
    Write-Host '  No node dependencies defined.' -ForegroundColor Gray
}

# ===========================================================================
# Step 10: Associate node-level N:N scopes
# ===========================================================================
Write-Host ''
Write-Host '[10/12] Associating node scopes...' -ForegroundColor Yellow

foreach ($node in $definition.nodes) {
    $nodeName = $node.name
    $nodeId   = $nodeIdMap[$nodeName]

    # Support both flat (node.skills) and nested (node.scopes.skills) formats
    $nodeScopes = if ($node.scopes) { $node.scopes } else { $node }

    $skillCount     = 0
    $knowledgeCount = 0
    $toolCount      = 0

    if ($DryRun) {
        if ($nodeScopes.skills)    { $skillCount     = $nodeScopes.skills.Count }
        if ($nodeScopes.knowledge) { $knowledgeCount = $nodeScopes.knowledge.Count }
        if ($nodeScopes.tools)     { $toolCount      = $nodeScopes.tools.Count }
    } else {
        # Associate skills
        if ($nodeScopes.skills) {
            foreach ($code in $nodeScopes.skills) {
                if ($resolvedSkills.ContainsKey($code)) {
                    $targetId = [guid]$resolvedSkills[$code].Id
                    $success = Associate-NtoN -SourceEntitySet 'sprk_playbooknodes' -SourceId ([guid]$nodeId) `
                        -RelationshipName 'sprk_playbooknode_skill' -TargetEntitySet 'sprk_analysisskills' `
                        -TargetId $targetId -Headers $headers
                    if ($success) { $skillCount++ }
                }
            }
        }

        # Associate knowledge
        if ($nodeScopes.knowledge) {
            foreach ($code in $nodeScopes.knowledge) {
                if ($resolvedKnowledge.ContainsKey($code)) {
                    $targetId = [guid]$resolvedKnowledge[$code].Id
                    $success = Associate-NtoN -SourceEntitySet 'sprk_playbooknodes' -SourceId ([guid]$nodeId) `
                        -RelationshipName 'sprk_playbooknode_knowledge' -TargetEntitySet 'sprk_analysisknowledges' `
                        -TargetId $targetId -Headers $headers
                    if ($success) { $knowledgeCount++ }
                }
            }
        }

        # Associate tools
        if ($nodeScopes.tools) {
            foreach ($code in $nodeScopes.tools) {
                if ($resolvedTools.ContainsKey($code)) {
                    $targetId = [guid]$resolvedTools[$code].Id
                    $success = Associate-NtoN -SourceEntitySet 'sprk_playbooknodes' -SourceId ([guid]$nodeId) `
                        -RelationshipName 'sprk_playbooknode_tool' -TargetEntitySet 'sprk_analysistools' `
                        -TargetId $targetId -Headers $headers
                    if ($success) { $toolCount++ }
                }
            }
        }
    }

    if ($skillCount -gt 0 -or $knowledgeCount -gt 0 -or $toolCount -gt 0) {
        Write-Host "  $nodeName`: $skillCount skill, $knowledgeCount knowledge, $toolCount tool" -ForegroundColor Green
    }
}

# ===========================================================================
# Step 11: Build and save canvas layout
# ===========================================================================
Write-Host ''
Write-Host '[11/12] Saving canvas layout...' -ForegroundColor Yellow

# Build canvas nodes
$canvasNodes = @()
$canvasEdges = @()

foreach ($node in $definition.nodes) {
    $nodeName = $node.name
    $nodeId   = $nodeIdMap[$nodeName]

    # R7 NOTE (task 055): the `nodeType` string read here drives the canvas
    # layout JSON shape ONLY (canvasType: 'aiAnalysis' | 'output' | 'control').
    # It does NOT drive dispatch — dispatch is via sprk_executortype written
    # above. The friendly-label switch is the correct level of granularity
    # for the canvas builder (which renders by category, not by specific
    # executor). Wave 8 task 088 owns the full Builder migration to
    # sprk_executortype-driven canvas rendering.
    $canvasNodeTypeLabel = if ($node.nodeType) { $node.nodeType } else { 'AIAnalysis' }

    # Map node type to canvas type string
    $canvasType = switch ($canvasNodeTypeLabel) {
        'AIAnalysis' { 'aiAnalysis' }
        'Output'     { 'output' }
        'Control'    { 'control' }
        default      { 'aiAnalysis' }
    }

    $posX = if ($null -ne $node.positionX) { $node.positionX } else { 100 + (($canvasNodes.Count) * 300) }
    $posY = if ($null -ne $node.positionY) { $node.positionY } else { 200 }

    $canvasNodes += @{
        id       = $nodeId.ToString()
        type     = $canvasType
        position = @{ x = $posX; y = $posY }
        data     = @{
            label            = $nodeName
            type             = $canvasType
            isConfigured     = $true
            validationErrors = @()
        }
    }

    # Build edges from dependencies
    if ($node.dependsOn) {
        foreach ($depName in $node.dependsOn) {
            if ($nodeIdMap.ContainsKey($depName)) {
                $sourceId = $nodeIdMap[$depName].ToString()
                $targetId = $nodeId.ToString()
                $canvasEdges += @{
                    id       = "e-$sourceId-$targetId"
                    source   = $sourceId
                    target   = $targetId
                    type     = 'smoothstep'
                    animated = $false
                }
            }
        }
    }
}

$canvasLayout = @{
    viewport = @{ x = 0; y = 0; zoom = 1.0 }
    nodes    = $canvasNodes
    edges    = $canvasEdges
    version  = 1
}

$canvasJson = $canvasLayout | ConvertTo-Json -Depth 10 -Compress

Write-Host "  $($canvasNodes.Count) nodes, $($canvasEdges.Count) edge(s)" -ForegroundColor White

if ($DryRun) {
    Write-Host '  WOULD SAVE canvas layout to playbook.' -ForegroundColor Gray
} else {
    try {
        Invoke-DataversePatch -Endpoint "sprk_analysisplaybooks($playbookId)" `
            -Body @{ sprk_canvaslayoutjson = $canvasJson } -Headers $headers
        Write-Host '  Canvas layout saved.' -ForegroundColor Green
    } catch {
        Write-Warning "  Failed to save canvas layout: $($_.Exception.Message)"
    }
}

# ===========================================================================
# Step 12: Summary
# ===========================================================================
Write-Host ''
Write-Host '[12/12] Summary' -ForegroundColor Cyan
Write-Host "  Playbook : $playbookName ($playbookId)" -ForegroundColor White
Write-Host "  Nodes    : $nodeCount" -ForegroundColor White
Write-Host "  Scope associations: $scopeAssociationCount (playbook-level)" -ForegroundColor White
Write-Host "  Dependencies: $dependencyCount" -ForegroundColor White
Write-Host "  Canvas   : saved ($($canvasNodes.Count) nodes, $($canvasEdges.Count) edges)" -ForegroundColor White
Write-Host ''

if ($DryRun) {
    Write-Host 'DRY RUN complete — no changes were made.' -ForegroundColor Yellow
} else {
    Write-Host 'Deployment complete!' -ForegroundColor Green
}

Write-Host ''
