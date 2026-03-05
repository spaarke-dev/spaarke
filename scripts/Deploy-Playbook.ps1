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

    [switch]$DryRun,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Environment URL resolution
# ---------------------------------------------------------------------------
$envMap = @{
    'dev' = 'https://spaarkedev1.crm.dynamics.com'
}

$EnvironmentUrl = $envMap[$Environment]
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
    $response = Invoke-RestMethod -Uri $uri -Headers $Headers -Method Post `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonBody)) `
        -ResponseHeadersVariable 'responseHeaders'
    # Extract the created record ID from the OData-EntityId header
    $entityId = $null
    if ($responseHeaders -and $responseHeaders['OData-EntityId']) {
        $entityIdHeader = $responseHeaders['OData-EntityId']
        if ($entityIdHeader -match '\(([0-9a-fA-F\-]{36})\)') {
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
# Node type mapping
# ---------------------------------------------------------------------------
$NodeTypeMap = @{
    'AIAnalysis' = 100000000
    'Output'     = 100000001
    'Control'    = 100000002
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

$playbookName = $definition.playbook.name
$playbookDescription = if ($definition.playbook.description) { $definition.playbook.description } else { '' }
$playbookIsPublic = if ($null -ne $definition.playbook.isPublic) { $definition.playbook.isPublic } else { $true }
$nodeCount = $definition.nodes.Count

Write-Host "  Playbook: $playbookName" -ForegroundColor White
Write-Host "  Nodes   : $nodeCount" -ForegroundColor White

# Collect all unique scope codes from playbook-level and node-level
$allActionCodes    = @()
$allSkillCodes     = @()
$allKnowledgeCodes = @()
$allToolCodes      = @()
$allModelNames     = @()

# Playbook-level scopes
if ($definition.playbook.actions)   { $allActionCodes    += $definition.playbook.actions }
if ($definition.playbook.skills)    { $allSkillCodes     += $definition.playbook.skills }
if ($definition.playbook.knowledge) { $allKnowledgeCodes += $definition.playbook.knowledge }
if ($definition.playbook.tools)     { $allToolCodes      += $definition.playbook.tools }

# Node-level scopes and models
foreach ($node in $definition.nodes) {
    if ($node.actionCode) { $allActionCodes += $node.actionCode }
    if ($node.skills)     { $allSkillCodes += $node.skills }
    if ($node.knowledge)  { $allKnowledgeCodes += $node.knowledge }
    if ($node.tools)      { $allToolCodes += $node.tools }
    if ($node.model)      { $allModelNames += $node.model }
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
        Write-Host "  $($resolutionErrors.Count) scope code(s) could not be resolved:" -ForegroundColor Red
        foreach ($err in $resolutionErrors) {
            Write-Host "    - $err" -ForegroundColor Red
        }
        throw "Cannot proceed: unresolved scope codes. Ensure all referenced scope records exist in Dataverse."
    }
}

# ===========================================================================
# Step 4: Resolve model deployments
# ===========================================================================
Write-Host ''
Write-Host '[4/12] Resolving model deployments...' -ForegroundColor Yellow

$resolvedModels = @{}

if ($DryRun) {
    Write-Host '  Skipped (dry run) — would resolve:' -ForegroundColor Gray
    foreach ($model in $allModelNames) { Write-Host "    Model: $model" -ForegroundColor Gray }
} else {
    foreach ($modelName in $allModelNames) {
        $encodedName = [Uri]::EscapeDataString($modelName)
        $endpoint = "sprk_aimodeldeployments?`$filter=sprk_name eq '$encodedName'&`$select=sprk_aimodeldeploymentid"
        $result = Invoke-DataverseGet -Endpoint $endpoint -Headers $headers

        if ($result -and $result.value -and $result.value.Count -gt 0) {
            $resolvedModels[$modelName] = $result.value[0].sprk_aimodeldeploymentid
            Write-Host "  $modelName -> $($result.value[0].sprk_aimodeldeploymentid)" -ForegroundColor Green
        } else {
            throw "Model deployment '$modelName' not found in Dataverse."
        }
    }
}

# ===========================================================================
# Step 5: Check for existing playbook
# ===========================================================================
Write-Host ''
Write-Host '[5/12] Checking for existing playbook...' -ForegroundColor Yellow

$existingPlaybookId = $null

if (-not $DryRun) {
    $encodedName = [Uri]::EscapeDataString($playbookName)
    $endpoint = "sprk_analysisplaybooks?`$filter=sprk_name eq '$encodedName'&`$select=sprk_analysisplaybookid,sprk_name"
    $result = Invoke-DataverseGet -Endpoint $endpoint -Headers $headers

    if ($result -and $result.value -and $result.value.Count -gt 0) {
        $existingPlaybookId = $result.value[0].sprk_analysisplaybookid

        if ($Force) {
            Write-Host "  FOUND: $existingPlaybookId — will delete and recreate (-Force)" -ForegroundColor Yellow

            # Delete existing nodes first
            $nodesEndpoint = "sprk_playbooknodes?`$filter=_sprk_playbookid_value eq '$existingPlaybookId'&`$select=sprk_playbooknodeid"
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
        sprk_isactive    = $true
    }

    try {
        $playbookId = Invoke-DataversePost -Endpoint 'sprk_analysisplaybooks' -Body $playbookBody -Headers $headers
        if (-not $playbookId) {
            throw "POST succeeded but could not extract record ID from response headers."
        }
        Write-Host "  Created: $playbookId" -ForegroundColor Green
    } catch {
        throw "Failed to create playbook: $($_.Exception.Message)"
    }
}

# ===========================================================================
# Step 7: Associate playbook-level N:N scopes
# ===========================================================================
Write-Host ''
Write-Host '[7/12] Associating playbook scopes...' -ForegroundColor Yellow

$scopeAssociationCount = 0

if ($DryRun) {
    if ($definition.playbook.actions) {
        foreach ($code in $definition.playbook.actions) { Write-Host "  WOULD LINK Action $code" -ForegroundColor Gray; $scopeAssociationCount++ }
    }
    if ($definition.playbook.skills) {
        foreach ($code in $definition.playbook.skills) { Write-Host "  WOULD LINK Skill $code" -ForegroundColor Gray; $scopeAssociationCount++ }
    }
    if ($definition.playbook.knowledge) {
        foreach ($code in $definition.playbook.knowledge) { Write-Host "  WOULD LINK Knowledge $code" -ForegroundColor Gray; $scopeAssociationCount++ }
    }
    if ($definition.playbook.tools) {
        foreach ($code in $definition.playbook.tools) { Write-Host "  WOULD LINK Tool $code" -ForegroundColor Gray; $scopeAssociationCount++ }
    }
} else {
    # Associate actions
    if ($definition.playbook.actions) {
        foreach ($code in $definition.playbook.actions) {
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
    if ($definition.playbook.skills) {
        foreach ($code in $definition.playbook.skills) {
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
    if ($definition.playbook.knowledge) {
        foreach ($code in $definition.playbook.knowledge) {
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
    if ($definition.playbook.tools) {
        foreach ($code in $definition.playbook.tools) {
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

# Map: node definition name -> created GUID (for dependency resolution)
$nodeIdMap = @{}
$nodeIndex = 0

foreach ($node in $definition.nodes) {
    $nodeIndex++
    $nodeName       = $node.name
    $nodeType       = if ($node.nodeType) { $node.nodeType } else { 'AIAnalysis' }
    $nodeTypeValue  = $NodeTypeMap[$nodeType]
    $actionCode     = $node.actionCode
    $modelName      = $node.model
    $outputVariable = if ($node.outputVariable) { $node.outputVariable } else { '' }
    $posX           = if ($null -ne $node.positionX) { $node.positionX } else { 100 + (($nodeIndex - 1) * 300) }
    $posY           = if ($null -ne $node.positionY) { $node.positionY } else { 200 }
    $configJson     = if ($node.configJson) { $node.configJson | ConvertTo-Json -Depth 10 -Compress } else { $null }

    $modelDisplay = if ($modelName) { $modelName } else { 'none' }
    $actionDisplay = if ($actionCode) { $actionCode } else { 'none' }

    if ($DryRun) {
        Write-Host "  Node $nodeIndex`: $nodeName ($actionDisplay, $modelDisplay)" -ForegroundColor Gray
        $nodeIdMap[$nodeName] = [guid]::NewGuid()
    } else {
        $nodeBody = @{
            sprk_name           = $nodeName
            sprk_nodetype       = $nodeTypeValue
            sprk_executionorder = $nodeIndex
            sprk_isactive       = $true
            'sprk_playbookid@odata.bind' = "sprk_analysisplaybooks($playbookId)"
        }

        if ($outputVariable) {
            $nodeBody['sprk_outputvariable'] = $outputVariable
        }
        if ($null -ne $posX) {
            $nodeBody['sprk_positionx'] = $posX
        }
        if ($null -ne $posY) {
            $nodeBody['sprk_positiony'] = $posY
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
            Write-Host "  Node $nodeIndex`: $nodeName ($actionDisplay, $modelDisplay) -> $nodeId" -ForegroundColor Green
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

    $skillCount     = 0
    $knowledgeCount = 0
    $toolCount      = 0

    if ($DryRun) {
        if ($node.skills)    { $skillCount     = $node.skills.Count }
        if ($node.knowledge) { $knowledgeCount = $node.knowledge.Count }
        if ($node.tools)     { $toolCount      = $node.tools.Count }
    } else {
        # Associate skills
        if ($node.skills) {
            foreach ($code in $node.skills) {
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
        if ($node.knowledge) {
            foreach ($code in $node.knowledge) {
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
        if ($node.tools) {
            foreach ($code in $node.tools) {
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
    $nodeType = if ($node.nodeType) { $node.nodeType } else { 'AIAnalysis' }

    # Map node type to canvas type string
    $canvasType = switch ($nodeType) {
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
        data     = @{ label = $nodeName }
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
