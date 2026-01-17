<#
.SYNOPSIS
    Creates Builder-specific scope records in Dataverse for the AI Playbook Builder.

.DESCRIPTION
    This script creates the following scope records:
    - 5 Actions (ACT-BUILDER-001 to ACT-BUILDER-005)
    - 5 Skills (SKL-BUILDER-001 to SKL-BUILDER-005)
    - 9 Tools (TL-BUILDER-001 to TL-BUILDER-009)
    - 4 Knowledge sources (KNW-BUILDER-001 to KNW-BUILDER-004)

.NOTES
    Author: AI Architecture Team
    Date: 2026-01-16
    Requires: Azure CLI authenticated to spaarkedev1.crm.dynamics.com
#>

param(
    [switch]$WhatIf
)

$token = az account get-access-token --resource "https://spaarkedev1.crm.dynamics.com" --query "accessToken" -o tsv
if (-not $token) {
    Write-Error "Failed to get access token. Please run 'az login' first."
    exit 1
}

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'Content-Type' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

$baseUrl = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2"

function Create-DataverseRecord {
    param(
        [string]$EntitySetName,
        [hashtable]$Data,
        [switch]$WhatIf
    )

    $url = "$baseUrl/$EntitySetName"
    $body = $Data | ConvertTo-Json -Depth 10

    if ($WhatIf) {
        Write-Host "[WhatIf] Would create in $EntitySetName : $($Data['sprk_name'])" -ForegroundColor Yellow
        return $null
    }

    try {
        $response = Invoke-WebRequest -Uri $url -Headers $headers -Method Post -Body $body -UseBasicParsing
        $location = $response.Headers['OData-EntityId']
        $id = [regex]::Match($location, '\(([^)]+)\)').Groups[1].Value
        Write-Host "Created $($Data['sprk_name']) - ID: $id" -ForegroundColor Green
        return $id
    }
    catch {
        Write-Host "ERROR creating $($Data['sprk_name']): $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

# =============================================================================
# ACTIONS (ACT-BUILDER-*)
# =============================================================================
Write-Host "`n=== Creating Builder Actions ===" -ForegroundColor Cyan

$actions = @(
    @{
        sprk_name = "ACT-BUILDER-001: Intent Classification"
        sprk_description = "Parse user message into operation intent (CREATE_PLAYBOOK, ADD_NODE, CONNECT_NODES, etc.)"
        sprk_systemprompt = @"
You are an intent classifier for the AI Playbook Builder. Analyze the user's message and determine their intent.

Valid intent categories:
- CREATE_PLAYBOOK: User wants to create a new playbook from scratch
- ADD_NODE: User wants to add a node to the canvas
- REMOVE_NODE: User wants to delete a node
- CONNECT_NODES: User wants to create an edge between nodes
- CONFIGURE_NODE: User wants to modify a node's settings
- LINK_SCOPE: User wants to attach a scope to a node
- CREATE_SCOPE: User wants to create a new custom scope
- QUERY_STATUS: User is asking about the playbook state
- MODIFY_LAYOUT: User wants to rearrange the canvas
- UNDO: User wants to reverse the last action
- UNCLEAR: Ambiguous input requiring clarification

Return a JSON object with:
- intent: The classified intent category
- confidence: Confidence score (0-1)
- entities: Extracted entities (node names, scope references, etc.)
- reasoning: Brief explanation of classification
"@
    },
    @{
        sprk_name = "ACT-BUILDER-002: Node Configuration"
        sprk_description = "Generate node configuration from user requirements"
        sprk_systemprompt = @"
You are a node configuration generator for the AI Playbook Builder. Given a user's description of what they want a node to do, generate the appropriate node configuration.

For each node type, configure:
- aiAnalysis: Action, Skills, Knowledge, Tools, Output variable
- aiCondition: Condition expression, true/false paths
- aiAssemble: Assembly template, input mappings
- aiDeliver: Delivery method, recipients, format
- aiGateway: Parallel/sequential mode, timeout

Return a JSON object with:
- nodeType: The appropriate node type
- config: Complete configuration object
- requiredScopes: List of scopes needed
- suggestedLabel: User-friendly node label
"@
    },
    @{
        sprk_name = "ACT-BUILDER-003: Scope Selection"
        sprk_description = "Select appropriate existing scope from catalog"
        sprk_systemprompt = @"
You are a scope selector for the AI Playbook Builder. Given a node's purpose and the available scope catalog, select the most appropriate existing scope.

Consider:
- Semantic similarity to node purpose
- Document type compatibility
- Usage frequency and reliability
- Version/update status

Return a JSON object with:
- selectedScopeId: ID of the best matching scope
- confidence: Match confidence (0-1)
- alternatives: Up to 3 alternative scopes with confidence
- reasoning: Why this scope was selected
"@
    },
    @{
        sprk_name = "ACT-BUILDER-004: Scope Creation"
        sprk_description = "Generate new scope definition when no suitable match exists"
        sprk_systemprompt = @"
You are a scope creator for the AI Playbook Builder. When no existing scope matches the user's needs, generate a new scope definition.

For each scope type:
- Action: System prompt, output format, allowed attachments
- Skill: Prompt fragment, category, applicability
- Knowledge: Content description, source type, tags
- Tool: Handler configuration, parameters, validation

Return a JSON object with:
- scopeType: action/skill/knowledge/tool
- definition: Complete scope definition
- suggestedId: Recommended ID (CUST-XXX-NNN format)
- dependencies: Other scopes this requires
"@
    },
    @{
        sprk_name = "ACT-BUILDER-005: Build Plan Generation"
        sprk_description = "Create structured build plan from user requirements"
        sprk_systemprompt = @"
You are a build planner for the AI Playbook Builder. Given a user's high-level requirements, generate a structured execution plan.

Plan structure:
1. Analyze requirements
2. Identify required nodes
3. Determine node sequence
4. Map scope requirements
5. Generate step-by-step operations

Return a JSON object with:
- playbookSpec: Purpose, document types, matter types
- nodeSequence: Ordered list of nodes to create
- scopeRequirements: Scopes needed (existing or new)
- executionPlan: Ordered operations (createNode, createEdge, linkScope)
- estimatedSteps: Total number of operations
"@
    }
)

foreach ($action in $actions) {
    Create-DataverseRecord -EntitySetName "sprk_analysisactions" -Data $action -WhatIf:$WhatIf
}

# =============================================================================
# SKILLS (SKL-BUILDER-*)
# =============================================================================
Write-Host "`n=== Creating Builder Skills ===" -ForegroundColor Cyan

$skills = @(
    @{
        sprk_name = "SKL-BUILDER-001: Lease Analysis Pattern"
        sprk_description = "Pattern for building lease document analysis playbooks"
        sprk_promptfragment = @"
When building a lease analysis playbook, follow this pattern:

1. TL;DR Summary Node - Extract key terms, parties, dates
2. Party Extraction Node - Identify landlord, tenant, guarantors
3. Key Terms Node - Rent, term, renewal options, escalations
4. Compliance Check Node - Compare against standard terms
5. Risk Assessment Node - Identify concerning clauses
6. Assemble Node - Compile analysis report
7. Deliver Node - Output to specified fields

Standard scopes for leases:
- Actions: TL;DR Summarizer, Entity Extractor, Compliance Checker
- Skills: Legal Writing, Risk Assessment, Lease Terminology
- Knowledge: Standard Lease Terms, Jurisdiction Requirements
- Tools: Date Extractor, Financial Calculator
"@
        sprk_category = 100000000
    },
    @{
        sprk_name = "SKL-BUILDER-002: Contract Review Pattern"
        sprk_description = "Pattern for building contract review playbooks"
        sprk_promptfragment = @"
When building a contract review playbook, follow this pattern:

1. Document Classification Node - Identify contract type
2. Party Identification Node - Extract all parties
3. Term Extraction Node - Key dates, obligations, rights
4. Clause Analysis Node - Standard vs. non-standard clauses
5. Risk Flagging Node - Unusual or concerning terms
6. Summary Node - Executive overview
7. Delivery Node - Output fields

Consider contract types:
- Service agreements
- NDAs
- Employment contracts
- Vendor agreements
- License agreements
"@
        sprk_category = 100000000
    },
    @{
        sprk_name = "SKL-BUILDER-003: Risk Assessment Pattern"
        sprk_description = "Pattern for structuring risk detection flows"
        sprk_promptfragment = @"
When building risk assessment nodes, follow this pattern:

Risk Categories:
- Financial: Payment terms, penalties, caps
- Legal: Jurisdiction, arbitration, liability
- Operational: SLA, termination, force majeure
- Compliance: Regulatory, industry-specific

Risk Scoring:
- Low (1-3): Standard terms, minor deviations
- Medium (4-6): Non-standard but acceptable
- High (7-9): Requires legal review
- Critical (10): Show-stopper issues

Output format:
- Risk item
- Category
- Severity score
- Relevant clause text
- Recommendation
"@
        sprk_category = 100000000
    },
    @{
        sprk_name = "SKL-BUILDER-004: Node Type Guide"
        sprk_description = "When to use each playbook node type"
        sprk_promptfragment = @"
Node Type Selection Guide:

AI Analysis (aiAnalysis):
- Use for: Text extraction, summarization, classification
- Requires: Action, optionally Skills/Knowledge/Tools
- Output: Structured data or text

AI Condition (aiCondition):
- Use for: Branching logic, routing decisions
- Requires: Condition expression
- Output: Routes to true/false paths

AI Gateway (aiGateway):
- Use for: Parallel processing, fan-out/fan-in
- Modes: Parallel (all paths), Sequential (first success)
- Output: Aggregated results

AI Assemble (aiAssemble):
- Use for: Combining outputs from multiple nodes
- Template: Markdown with variable substitution
- Output: Formatted document

AI Deliver (aiDeliver):
- Use for: Final output to external systems
- Methods: Field mapping, email, webhook
- Output: Confirmation of delivery
"@
        sprk_category = 100000000
    },
    @{
        sprk_name = "SKL-BUILDER-005: Scope Matching"
        sprk_description = "How to find and match appropriate scopes"
        sprk_promptfragment = @"
Scope Matching Strategy:

1. Semantic Analysis
   - Extract keywords from node purpose
   - Match against scope names and descriptions
   - Consider synonyms and related terms

2. Document Type Compatibility
   - Check scope's applicable document types
   - Match against playbook's target documents
   - Consider hierarchical document categories

3. Confidence Thresholds
   - > 0.85: Auto-select scope
   - 0.70-0.85: Suggest with confirmation
   - < 0.70: Ask user to select or create

4. Fallback Strategy
   - If no match: Offer to create new scope
   - If partial match: Offer "Save As" from existing
   - If extension needed: Offer to extend base scope

Metadata Fields:
- Actions: name, description, tags, documentTypes
- Skills: name, description, category, applicability
- Knowledge: name, sourceType, contentTags
- Tools: name, handlerType, inputSchema
"@
        sprk_category = 100000000
    }
)

foreach ($skill in $skills) {
    Create-DataverseRecord -EntitySetName "sprk_analysisskills" -Data $skill -WhatIf:$WhatIf
}

# =============================================================================
# TOOLS (TL-BUILDER-*)
# =============================================================================
Write-Host "`n=== Creating Builder Tools ===" -ForegroundColor Cyan

$tools = @(
    @{
        sprk_name = "TL-BUILDER-001: addNode"
        sprk_description = "Create a new node on the playbook canvas"
        sprk_handlerclass = "CanvasOperationHandler"
        sprk_configuration = '{"operation":"addNode","parameters":["type","label","position","config"],"validTypes":["aiAnalysis","aiCondition","aiGateway","aiAssemble","aiDeliver"]}'
    },
    @{
        sprk_name = "TL-BUILDER-002: removeNode"
        sprk_description = "Delete a node and its connected edges from the canvas"
        sprk_handlerclass = "CanvasOperationHandler"
        sprk_configuration = '{"operation":"removeNode","parameters":["nodeId"],"cascadeEdges":true}'
    },
    @{
        sprk_name = "TL-BUILDER-003: createEdge"
        sprk_description = "Connect two nodes with a directed edge"
        sprk_handlerclass = "CanvasOperationHandler"
        sprk_configuration = '{"operation":"createEdge","parameters":["sourceId","targetId","label"],"validateCycles":true}'
    },
    @{
        sprk_name = "TL-BUILDER-004: updateNodeConfig"
        sprk_description = "Modify configuration properties of an existing node"
        sprk_handlerclass = "CanvasOperationHandler"
        sprk_configuration = '{"operation":"updateNodeConfig","parameters":["nodeId","config"],"mergeStrategy":"deep"}'
    },
    @{
        sprk_name = "TL-BUILDER-005: linkScope"
        sprk_description = "Attach an existing scope to a node"
        sprk_handlerclass = "CanvasOperationHandler"
        sprk_configuration = '{"operation":"linkScope","parameters":["nodeId","scopeType","scopeId"],"scopeTypes":["action","skill","knowledge","tool"]}'
    },
    @{
        sprk_name = "TL-BUILDER-006: createScope"
        sprk_description = "Create a new scope record in Dataverse"
        sprk_handlerclass = "DataverseOperationHandler"
        sprk_configuration = '{"operation":"createScope","parameters":["type","data"],"scopeTypes":["action","skill","knowledge"],"ownerPrefix":"CUST-"}'
    },
    @{
        sprk_name = "TL-BUILDER-007: searchScopes"
        sprk_description = "Find existing scopes by name or purpose"
        sprk_handlerclass = "DataverseOperationHandler"
        sprk_configuration = '{"operation":"searchScopes","parameters":["type","query","limit"],"defaultLimit":10,"maxLimit":50}'
    },
    @{
        sprk_name = "TL-BUILDER-008: autoLayout"
        sprk_description = "Automatically arrange nodes for visual clarity"
        sprk_handlerclass = "CanvasOperationHandler"
        sprk_configuration = '{"operation":"autoLayout","algorithms":["dagre","elk","grid"],"defaultAlgorithm":"dagre","spacing":{"horizontal":150,"vertical":100}}'
    },
    @{
        sprk_name = "TL-BUILDER-009: validateCanvas"
        sprk_description = "Validate canvas structure and scope linkages"
        sprk_handlerclass = "CanvasOperationHandler"
        sprk_configuration = '{"operation":"validateCanvas","checks":["cycles","orphanNodes","missingScopes","requiredNodes"]}'
    }
)

foreach ($tool in $tools) {
    Create-DataverseRecord -EntitySetName "sprk_analysistools" -Data $tool -WhatIf:$WhatIf
}

# =============================================================================
# KNOWLEDGE (KNW-BUILDER-*)
# =============================================================================
Write-Host "`n=== Creating Builder Knowledge ===" -ForegroundColor Cyan

$knowledge = @(
    @{
        sprk_name = "KNW-BUILDER-001: Scope Catalog"
        sprk_description = "Catalog of all available system scopes for search and selection"
        sprk_content = @"
# System Scope Catalog

## Actions (SYS-ACT-*)
| ID | Name | Purpose | Document Types |
|----|------|---------|----------------|
| SYS-ACT-001 | TL;DR Summarizer | Generate executive summary | All |
| SYS-ACT-002 | Entity Extractor | Extract named entities | All |
| SYS-ACT-003 | Clause Analyzer | Analyze contract clauses | Contracts |
| SYS-ACT-004 | Compliance Checker | Check against standards | All |
| SYS-ACT-005 | Risk Assessor | Identify and score risks | All |

## Skills (SYS-SKL-*)
| ID | Name | Purpose | Category |
|----|------|---------|----------|
| SYS-SKL-001 | Legal Writing | Professional legal tone | Tone |
| SYS-SKL-002 | Lease Terminology | Real estate lease terms | Domain |
| SYS-SKL-003 | Financial Analysis | Financial calculation focus | Domain |
| SYS-SKL-004 | Risk Language | Risk-focused writing | Tone |

## Tools (SYS-TL-*)
| ID | Name | Handler | Purpose |
|----|------|---------|---------|
| SYS-TL-001 | Entity Extractor | EntityExtractorHandler | Extract named entities |
| SYS-TL-002 | Clause Analyzer | ClauseAnalyzerHandler | Analyze clauses |
| SYS-TL-003 | Document Classifier | DocumentClassifierHandler | Classify documents |
| SYS-TL-004 | Summary Generator | SummaryHandler | Generate summaries |
| SYS-TL-005 | Risk Detector | RiskDetectorHandler | Detect risks |
"@
    },
    @{
        sprk_name = "KNW-BUILDER-002: Reference Playbooks"
        sprk_description = "Example playbook patterns for common use cases"
        sprk_content = @"
# Reference Playbook Patterns

## Lease Analysis (PB-REF-LEASE)
Purpose: Analyze real estate lease documents
Nodes: 8
Flow: TL;DR → Parties → Terms → Compliance → Risk → Assemble → Deliver

## Contract Review (PB-REF-CONTRACT)
Purpose: Review and analyze contracts
Nodes: 7
Flow: Classify → Parties → Clauses → Risk → Summary → Assemble → Deliver

## Due Diligence (PB-REF-DUEDILIGENCE)
Purpose: Corporate transaction document review
Nodes: 10
Flow: Classify → Gateway(Parallel) → [Multiple Analysis] → Assemble → Risk Score → Deliver

## Compliance Check (PB-REF-COMPLIANCE)
Purpose: Check documents against compliance rules
Nodes: 5
Flow: Extract → Check → Condition(Pass/Fail) → Report → Deliver
"@
    },
    @{
        sprk_name = "KNW-BUILDER-003: Node Schema"
        sprk_description = "Valid node configurations and their schemas"
        sprk_content = @"
# Node Configuration Schema

## aiAnalysis Node
{
  "type": "aiAnalysis",
  "label": "string (required)",
  "position": { "x": number, "y": number },
  "config": {
    "actionId": "guid (required)",
    "skillIds": ["guid"],
    "knowledgeIds": ["guid"],
    "toolIds": ["guid"],
    "outputVariable": "string",
    "outputFormat": "text|json|markdown"
  }
}

## aiCondition Node
{
  "type": "aiCondition",
  "label": "string (required)",
  "config": {
    "condition": "expression (required)",
    "trueLabel": "string",
    "falseLabel": "string"
  }
}

## aiGateway Node
{
  "type": "aiGateway",
  "label": "string (required)",
  "config": {
    "mode": "parallel|sequential",
    "timeout": "number (seconds)",
    "failurePolicy": "continue|abort"
  }
}

## aiAssemble Node
{
  "type": "aiAssemble",
  "label": "string (required)",
  "config": {
    "template": "markdown with {{variables}}",
    "inputs": ["variableNames"]
  }
}

## aiDeliver Node
{
  "type": "aiDeliver",
  "label": "string (required)",
  "config": {
    "method": "field|email|webhook",
    "mappings": { "fieldName": "{{variable}}" }
  }
}
"@
    },
    @{
        sprk_name = "KNW-BUILDER-004: Best Practices"
        sprk_description = "Guidelines for effective playbook design"
        sprk_content = @"
# Playbook Design Best Practices

## General Principles
1. Start with TL;DR - Users need quick overview first
2. Single responsibility - Each node does one thing well
3. Clear naming - Node labels should describe output
4. Logical flow - Left to right, top to bottom

## Performance
1. Parallel where possible - Use gateways for independent analysis
2. Early filtering - Use conditions to skip unnecessary work
3. Scope reuse - Prefer existing scopes over creating new

## Error Handling
1. Validate inputs - Check document type at start
2. Handle empty - Account for missing sections
3. Graceful degradation - Continue if optional analysis fails

## Maintainability
1. Document purpose - Add descriptions to nodes
2. Version scopes - Use Save As when modifying
3. Test incrementally - Validate after each change

## Common Mistakes
- Too many nodes (keep under 15)
- Circular dependencies (validate with cycles check)
- Missing scope links (all analysis nodes need action)
- Orphan nodes (all nodes should be connected)
"@
    }
)

foreach ($k in $knowledge) {
    Create-DataverseRecord -EntitySetName "sprk_analysisknowledges" -Data $k -WhatIf:$WhatIf
}

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Actions: 5 records"
Write-Host "Skills: 5 records"
Write-Host "Tools: 9 records"
Write-Host "Knowledge: 4 records"
Write-Host "Total: 23 records"

if ($WhatIf) {
    Write-Host "`n[WhatIf mode - no records were created. Run without -WhatIf to create records.]" -ForegroundColor Yellow
}
