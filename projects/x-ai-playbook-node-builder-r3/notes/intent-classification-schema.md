# AI Intent Classification Schema

> **Task**: 010-design-intent-schema
> **Created**: 2026-01-19
> **Status**: Design Complete

---

## Overview

This document defines the structured output schema for AI-powered intent classification in the Playbook Builder. The schema replaces the rule-based `ParseIntent()` method with Azure OpenAI structured output for more accurate, context-aware classification.

## Design Goals

1. **Confidence-based decisions**: Use confidence scores to determine when to execute vs. clarify
2. **Type-safe parameters**: Strongly-typed parameters for each operation type
3. **Multi-turn support**: Handle disambiguation across conversation turns
4. **Azure OpenAI compatible**: Schema compatible with structured output (JSON mode)
5. **Backward compatible**: Support all existing intent types from ParseIntent()

---

## Operation Categories

The schema organizes intents into 6 high-level operation categories:

| Operation | Description | Example Actions |
|-----------|-------------|-----------------|
| **BUILD** | Create new artifacts | CreatePlaybook, AddNode, CreateEdge, CreateScope |
| **MODIFY** | Change existing artifacts | RemoveNode, ConfigureNode, LinkScope, Undo |
| **TEST** | Execute or validate | TestPlaybook, ValidatePlaybook |
| **EXPLAIN** | Provide information | AnswerQuestion, DescribeState, ProvideGuidance |
| **SEARCH** | Query available resources | SearchScopes, BrowseCatalog |
| **CLARIFY** | Request more information | RequestClarification, ConfirmUnderstanding |

---

## JSON Schema for Azure OpenAI Structured Output

### Root Schema: AiIntentResult

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "AiIntentResult",
  "description": "Structured output for AI intent classification",
  "type": "object",
  "required": ["operation", "action", "confidence"],
  "properties": {
    "operation": {
      "type": "string",
      "enum": ["BUILD", "MODIFY", "TEST", "EXPLAIN", "SEARCH", "CLARIFY"],
      "description": "High-level operation category"
    },
    "action": {
      "type": "string",
      "enum": [
        "CREATE_PLAYBOOK", "ADD_NODE", "CREATE_EDGE", "CREATE_SCOPE",
        "REMOVE_NODE", "REMOVE_EDGE", "CONFIGURE_NODE", "LINK_SCOPE",
        "UNLINK_SCOPE", "MODIFY_LAYOUT", "UNDO", "REDO", "SAVE_PLAYBOOK",
        "TEST_PLAYBOOK", "VALIDATE_PLAYBOOK",
        "ANSWER_QUESTION", "DESCRIBE_STATE", "PROVIDE_GUIDANCE",
        "SEARCH_SCOPES", "BROWSE_CATALOG",
        "REQUEST_CLARIFICATION", "CONFIRM_UNDERSTANDING"
      ],
      "description": "Specific action within the operation category"
    },
    "confidence": {
      "type": "number",
      "minimum": 0.0,
      "maximum": 1.0,
      "description": "Confidence score (0.0-1.0)"
    },
    "parameters": {
      "type": "object",
      "description": "Action-specific parameters",
      "properties": {
        "createPlaybook": { "$ref": "#/definitions/CreatePlaybookParams" },
        "addNode": { "$ref": "#/definitions/AddNodeParams" },
        "createEdge": { "$ref": "#/definitions/CreateEdgeParams" },
        "createScope": { "$ref": "#/definitions/CreateScopeParams" },
        "removeNode": { "$ref": "#/definitions/RemoveNodeParams" },
        "configureNode": { "$ref": "#/definitions/ConfigureNodeParams" },
        "linkScope": { "$ref": "#/definitions/LinkScopeParams" },
        "savePlaybook": { "$ref": "#/definitions/SavePlaybookParams" },
        "testPlaybook": { "$ref": "#/definitions/TestPlaybookParams" },
        "searchScopes": { "$ref": "#/definitions/SearchScopesParams" }
      }
    },
    "clarification": {
      "$ref": "#/definitions/ClarificationRequest"
    },
    "reasoning": {
      "type": "string",
      "description": "AI reasoning for the classification"
    },
    "alternatives": {
      "type": "array",
      "items": { "$ref": "#/definitions/AlternativeIntent" },
      "description": "Alternative interpretations if ambiguous"
    }
  },
  "definitions": {
    "CreatePlaybookParams": {
      "type": "object",
      "required": ["goal"],
      "properties": {
        "goal": { "type": "string", "description": "User's playbook goal description" },
        "documentTypes": { "type": "array", "items": { "type": "string" } },
        "matterTypes": { "type": "array", "items": { "type": "string" } },
        "pattern": { "type": "string" },
        "complexity": { "type": "integer", "minimum": 1, "maximum": 5 }
      }
    },
    "AddNodeParams": {
      "type": "object",
      "required": ["nodeType"],
      "properties": {
        "nodeType": { "type": "string", "enum": ["aiAnalysis", "aiCompletion", "condition", "deliverOutput", "createTask", "sendEmail", "wait"] },
        "label": { "type": "string" },
        "position": { "$ref": "#/definitions/NodePositionParams" },
        "connectFrom": { "type": "string" },
        "scopeReference": { "$ref": "#/definitions/ScopeReferenceParams" }
      }
    },
    "CreateEdgeParams": {
      "type": "object",
      "required": ["sourceNode", "targetNode"],
      "properties": {
        "sourceNode": { "type": "string" },
        "targetNode": { "type": "string" },
        "label": { "type": "string" }
      }
    },
    "CreateScopeParams": {
      "type": "object",
      "required": ["scopeType", "name"],
      "properties": {
        "scopeType": { "type": "string", "enum": ["action", "skill", "knowledge", "tool"] },
        "name": { "type": "string" },
        "description": { "type": "string" },
        "content": { "type": "string" },
        "category": { "type": "string" },
        "basedOnId": { "type": "string" }
      }
    },
    "RemoveNodeParams": {
      "type": "object",
      "required": ["nodeReference"],
      "properties": {
        "nodeReference": { "type": "string" },
        "removeEdges": { "type": "boolean", "default": true }
      }
    },
    "ConfigureNodeParams": {
      "type": "object",
      "required": ["nodeReference", "property", "value"],
      "properties": {
        "nodeReference": { "type": "string" },
        "property": { "type": "string" },
        "value": { "type": "string" }
      }
    },
    "LinkScopeParams": {
      "type": "object",
      "required": ["nodeReference", "scopeReference"],
      "properties": {
        "nodeReference": { "type": "string" },
        "scopeReference": { "$ref": "#/definitions/ScopeReferenceParams" }
      }
    },
    "SavePlaybookParams": {
      "type": "object",
      "properties": {
        "name": { "type": "string" },
        "saveAsNew": { "type": "boolean", "default": false }
      }
    },
    "TestPlaybookParams": {
      "type": "object",
      "required": ["mode"],
      "properties": {
        "mode": { "type": "string", "enum": ["mock", "quick", "production"] },
        "maxNodes": { "type": "integer" },
        "startNodeId": { "type": "string" },
        "testDocumentId": { "type": "string" }
      }
    },
    "SearchScopesParams": {
      "type": "object",
      "required": ["query"],
      "properties": {
        "query": { "type": "string" },
        "scopeTypes": { "type": "array", "items": { "type": "string" } },
        "category": { "type": "string" },
        "includeSystem": { "type": "boolean", "default": true },
        "maxResults": { "type": "integer", "default": 10 }
      }
    },
    "NodePositionParams": {
      "type": "object",
      "properties": {
        "x": { "type": "number" },
        "y": { "type": "number" },
        "relative": { "type": "string" }
      }
    },
    "ScopeReferenceParams": {
      "type": "object",
      "required": ["type"],
      "properties": {
        "type": { "type": "string", "enum": ["action", "skill", "knowledge", "tool"] },
        "id": { "type": "string" },
        "name": { "type": "string" },
        "searchQuery": { "type": "string" }
      }
    },
    "ClarificationRequest": {
      "type": "object",
      "required": ["question", "type"],
      "properties": {
        "question": { "type": "string" },
        "type": { "type": "string", "enum": ["INTENT_DISAMBIGUATION", "ENTITY_DISAMBIGUATION", "MISSING_PARAMETER", "CONFIRMATION", "SELECTION", "GENERAL"] },
        "options": {
          "type": "array",
          "items": {
            "type": "object",
            "required": ["id", "label"],
            "properties": {
              "id": { "type": "string" },
              "label": { "type": "string" },
              "description": { "type": "string" },
              "recommended": { "type": "boolean" }
            }
          }
        },
        "context": {
          "type": "object",
          "properties": {
            "understood": { "type": "string" },
            "unclear": { "type": "string" },
            "relatedElements": { "type": "array", "items": { "type": "string" } }
          }
        },
        "allowFreeform": { "type": "boolean", "default": true },
        "suggestions": { "type": "array", "items": { "type": "string" } }
      }
    },
    "AlternativeIntent": {
      "type": "object",
      "required": ["operation", "action", "confidence"],
      "properties": {
        "operation": { "type": "string" },
        "action": { "type": "string" },
        "confidence": { "type": "number" },
        "reasoning": { "type": "string" }
      }
    }
  }
}
```

---

## Confidence Thresholds

| Threshold | Value | Action |
|-----------|-------|--------|
| **High Confidence** | >= 0.80 | Execute immediately |
| **Medium Confidence** | 0.60 - 0.79 | Execute with optional confirmation |
| **Low Confidence** | < 0.60 | Trigger clarification flow |
| **Entity Resolution** | >= 0.80 | Accept entity match |

---

## Example Prompts and Expected Outputs

### Example 1: Create Playbook (High Confidence)

**User Message:**
```
Create a playbook for analyzing commercial lease agreements to extract key terms
```

**Expected Output:**
```json
{
  "operation": "BUILD",
  "action": "CREATE_PLAYBOOK",
  "confidence": 0.95,
  "parameters": {
    "createPlaybook": {
      "goal": "analyze commercial lease agreements to extract key terms",
      "documentTypes": ["lease", "commercial-lease"],
      "pattern": "lease-analysis",
      "complexity": 3
    }
  },
  "reasoning": "User explicitly requested playbook creation with clear goal and document type"
}
```

---

### Example 2: Add Node (High Confidence)

**User Message:**
```
Add an AI analysis node after the document input
```

**Expected Output:**
```json
{
  "operation": "BUILD",
  "action": "ADD_NODE",
  "confidence": 0.92,
  "parameters": {
    "addNode": {
      "nodeType": "aiAnalysis",
      "label": "AI Analysis",
      "connectFrom": "document-input",
      "position": {
        "relative": "after:document-input"
      }
    }
  },
  "reasoning": "Clear request to add aiAnalysis node with explicit positioning"
}
```

---

### Example 3: Ambiguous Intent (Low Confidence - Needs Clarification)

**User Message:**
```
Remove it
```

**Canvas Context:**
```json
{
  "nodeCount": 5,
  "selectedNodeId": null,
  "nodes": [
    { "id": "node-1", "type": "aiAnalysis", "label": "Analyze Content" },
    { "id": "node-2", "type": "aiCompletion", "label": "Generate Summary" }
  ]
}
```

**Expected Output:**
```json
{
  "operation": "CLARIFY",
  "action": "REQUEST_CLARIFICATION",
  "confidence": 0.35,
  "clarification": {
    "question": "Which node would you like to remove?",
    "type": "ENTITY_DISAMBIGUATION",
    "options": [
      { "id": "node-1", "label": "Analyze Content (AI Analysis)", "recommended": false },
      { "id": "node-2", "label": "Generate Summary (AI Completion)", "recommended": false }
    ],
    "context": {
      "understood": "User wants to remove a node",
      "unclear": "Which specific node to remove - no node is selected"
    },
    "allowFreeform": true,
    "suggestions": [
      "Remove the 'Analyze Content' node",
      "Remove the last node I added",
      "Remove node-1"
    ]
  },
  "reasoning": "Pronoun 'it' is ambiguous with no selected node and multiple nodes on canvas"
}
```

---

### Example 4: Search Scopes (Medium Confidence)

**User Message:**
```
Find me a skill for extracting dates from contracts
```

**Expected Output:**
```json
{
  "operation": "SEARCH",
  "action": "SEARCH_SCOPES",
  "confidence": 0.88,
  "parameters": {
    "searchScopes": {
      "query": "extract dates contracts",
      "scopeTypes": ["skill"],
      "includeSystem": true,
      "maxResults": 10
    }
  },
  "reasoning": "User explicitly wants to search for a skill related to date extraction"
}
```

---

### Example 5: Test Playbook (High Confidence)

**User Message:**
```
Run a quick test on this playbook
```

**Expected Output:**
```json
{
  "operation": "TEST",
  "action": "TEST_PLAYBOOK",
  "confidence": 0.91,
  "parameters": {
    "testPlaybook": {
      "mode": "quick"
    }
  },
  "reasoning": "User explicitly requested 'quick' test mode"
}
```

---

### Example 6: Configure Node (With Entity Resolution)

**User Message:**
```
Set the output variable for the analysis node to 'summary'
```

**Canvas Context:**
```json
{
  "nodes": [
    { "id": "abc123", "type": "aiAnalysis", "label": "Document Analysis" }
  ]
}
```

**Expected Output:**
```json
{
  "operation": "MODIFY",
  "action": "CONFIGURE_NODE",
  "confidence": 0.87,
  "parameters": {
    "configureNode": {
      "nodeReference": "abc123",
      "property": "outputVariable",
      "value": "summary"
    }
  },
  "reasoning": "Only one analysis node on canvas, resolved 'the analysis node' to abc123"
}
```

---

### Example 7: Link Scope (Medium Confidence - Needs Search)

**User Message:**
```
Link the lease analysis skill to this node
```

**Canvas Context:**
```json
{
  "selectedNodeId": "node-5"
}
```

**Expected Output:**
```json
{
  "operation": "MODIFY",
  "action": "LINK_SCOPE",
  "confidence": 0.78,
  "parameters": {
    "linkScope": {
      "nodeReference": "node-5",
      "scopeReference": {
        "type": "skill",
        "name": "lease analysis",
        "searchQuery": "lease analysis"
      }
    }
  },
  "reasoning": "User wants to link a skill; resolved 'this node' to selected node-5. Skill needs to be searched by name."
}
```

---

### Example 8: Explain / Question (Explain Operation)

**User Message:**
```
What does this playbook do?
```

**Expected Output:**
```json
{
  "operation": "EXPLAIN",
  "action": "DESCRIBE_STATE",
  "confidence": 0.93,
  "reasoning": "User asking about the current playbook functionality"
}
```

---

### Example 9: Create Custom Scope

**User Message:**
```
Create a new action called 'Risk Assessment' for identifying financial risks
```

**Expected Output:**
```json
{
  "operation": "BUILD",
  "action": "CREATE_SCOPE",
  "confidence": 0.89,
  "parameters": {
    "createScope": {
      "scopeType": "action",
      "name": "Risk Assessment",
      "description": "identifying financial risks"
    }
  },
  "reasoning": "User explicitly requested creating a new action scope with name and purpose"
}
```

---

### Example 10: Destructive Action (Confirmation Needed)

**User Message:**
```
Delete all the nodes
```

**Canvas Context:**
```json
{
  "nodeCount": 8,
  "hasUnsavedChanges": true
}
```

**Expected Output:**
```json
{
  "operation": "CLARIFY",
  "action": "CONFIRM_UNDERSTANDING",
  "confidence": 0.85,
  "clarification": {
    "question": "Are you sure you want to delete all 8 nodes? This action cannot be undone and you have unsaved changes.",
    "type": "CONFIRMATION",
    "options": [
      { "id": "confirm", "label": "Yes, delete all nodes", "recommended": false },
      { "id": "cancel", "label": "Cancel", "recommended": true }
    ],
    "context": {
      "understood": "User wants to delete all nodes on the canvas",
      "unclear": null,
      "relatedElements": ["8 nodes", "unsaved changes"]
    },
    "allowFreeform": false
  },
  "reasoning": "Destructive action affecting all 8 nodes with unsaved changes - requires confirmation"
}
```

---

## Intent Mapping from ParseIntent()

| Old ParseIntent() | New Operation | New Action |
|-------------------|---------------|------------|
| CreatePlaybook | BUILD | CREATE_PLAYBOOK |
| AddNode | BUILD | ADD_NODE |
| RemoveNode | MODIFY | REMOVE_NODE |
| ConnectNodes | BUILD | CREATE_EDGE |
| ConfigureNode | MODIFY | CONFIGURE_NODE |
| SearchScopes | SEARCH | SEARCH_SCOPES |
| CreateScope | BUILD | CREATE_SCOPE |
| LinkScope | MODIFY | LINK_SCOPE |
| TestPlaybook | TEST | TEST_PLAYBOOK |
| SavePlaybook | MODIFY | SAVE_PLAYBOOK |
| AskQuestion | EXPLAIN | ANSWER_QUESTION |
| Unknown | CLARIFY | REQUEST_CLARIFICATION |

## Additional Intents (New)

| Intent | Operation | Action | Description |
|--------|-----------|--------|-------------|
| Remove Edge | MODIFY | REMOVE_EDGE | Remove connection between nodes |
| Unlink Scope | MODIFY | UNLINK_SCOPE | Unlink scope from node |
| Modify Layout | MODIFY | MODIFY_LAYOUT | Auto-arrange canvas |
| Undo | MODIFY | UNDO | Undo last operation |
| Redo | MODIFY | REDO | Redo undone operation |
| Validate | TEST | VALIDATE_PLAYBOOK | Validate without running |
| Describe State | EXPLAIN | DESCRIBE_STATE | Describe current state |
| Provide Guidance | EXPLAIN | PROVIDE_GUIDANCE | Give suggestions |
| Browse Catalog | SEARCH | BROWSE_CATALOG | Browse scope catalog |
| Confirm Understanding | CLARIFY | CONFIRM_UNDERSTANDING | Confirm before action |

---

## Implementation Notes

### C# Classes Location
- **File**: `src/server/api/Sprk.Bff.Api/Models/Ai/AiIntentClassificationSchema.cs`
- **Namespace**: `Sprk.Bff.Api.Models.Ai`

### Key Classes
- `AiIntentResult` - Root result class
- `OperationType` - 6 operation categories enum
- `IntentAction` - Specific actions enum
- `IntentParameters` - Union of action-specific parameter types
- `ClarificationRequest` - Clarification question schema
- `IntentClassificationContext` - Input context for classification

### Integration with Existing Code
- Complements existing `BuilderIntent` enum in `IAiPlaybookBuilderService.cs`
- Works alongside `IntentClassificationResponse` in `IntentClassificationModels.cs`
- Parameters align with `BuildPlan` and `ExecutionStep` in `BuildPlanModels.cs`

### Azure OpenAI Integration
1. Use `response_format: { type: "json_schema" }` in API call
2. Provide JSON schema as `json_schema.schema`
3. Parse response into `AiIntentResult` class
4. Use confidence to route to execution or clarification

---

## References

- Task: `projects/ai-playbook-node-builder-r3/tasks/010-design-intent-schema.poml`
- ADR-013: AI Architecture
- Existing models: `src/server/api/Sprk.Bff.Api/Models/Ai/`
