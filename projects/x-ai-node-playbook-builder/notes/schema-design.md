# Dataverse Schema Specification - Node-Based Playbook System

> **Version**: 1.0
> **Created**: 2026-01-09
> **Task**: 001-design-dataverse-schema
> **Source**: design.md Section 4

---

## Overview

This document specifies the complete Dataverse schema for the node-based playbook system, including:
- **Extended Entities**: Existing entities with new fields
- **New Entities**: Brand new entities for node orchestration
- **Choice Option Sets**: All enumeration values
- **Relationships**: N:N and lookup relationships

**Naming Convention**: All custom fields use `sprk_` prefix per project constraint.

---

## 1. Extended Entity: sprk_analysisplaybook

**Purpose**: Existing playbook entity extended with mode selection and node-based configuration.

### New Fields

| Logical Name | Display Name | Type | Required | Default | Description |
|--------------|--------------|------|----------|---------|-------------|
| `sprk_playbookmode` | Playbook Mode | Choice | No | `Legacy (0)` | Determines execution mode: Legacy uses existing N:N, NodeBased uses sprk_playbooknode |
| `sprk_playbooktype` | Playbook Type | Choice | No | `AiAnalysis (0)` | Classification for UI filtering and validation |
| `sprk_canvaslayoutjson` | Canvas Layout | Multiline Text (1M) | No | null | JSON storing React Flow viewport and node positions |
| `sprk_triggertype` | Trigger Type | Choice | No | `Manual (0)` | When playbook executes |
| `sprk_triggerconfigjson` | Trigger Config | Multiline Text (100K) | No | null | Trigger-specific settings (schedule, event filters) |
| `sprk_version` | Version | Integer | No | 1 | Schema version number (stub for future versioning) |
| `sprk_maxparallelnodes` | Max Parallel Nodes | Integer | No | 3 | Maximum concurrent node execution |
| `sprk_continueonerror` | Continue On Error | Yes/No | No | false | Whether to continue when a node fails |

### Choice Values: sprk_playbookmode

| Value | Label | Description |
|-------|-------|-------------|
| 0 | Legacy | Uses existing N:N relationships on playbook entity (backward compatible) |
| 1 | NodeBased | Uses sprk_playbooknode for multi-node orchestration |

### Choice Values: sprk_playbooktype

| Value | Label | Description |
|-------|-------|-------------|
| 0 | AiAnalysis | Document analysis with AI nodes |
| 1 | Workflow | Deterministic business process |
| 2 | Hybrid | Mix of AI and workflow actions |

### Choice Values: sprk_triggertype

| Value | Label | Description |
|-------|-------|-------------|
| 0 | Manual | User-initiated execution |
| 1 | Scheduled | Time-based trigger |
| 2 | RecordCreated | Dataverse record creation |
| 3 | RecordUpdated | Dataverse record update |

---

## 2. New Entity: sprk_playbooknode

**Purpose**: Represents a single node in the playbook execution graph. Each node bundles an action with its configuration.

### Entity Definition

| Property | Value |
|----------|-------|
| Logical Name | `sprk_playbooknode` |
| Display Name | Playbook Node |
| Primary Field | `sprk_name` |
| Ownership | Organization |

### Fields

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| `sprk_playbooknodeid` | Playbook Node ID | Uniqueidentifier | Yes (PK) | Primary key |
| `sprk_playbookid` | Playbook | Lookup (sprk_analysisplaybook) | Yes | Parent playbook reference |
| `sprk_actionid` | Action | Lookup (sprk_analysisaction) | Yes | Action to execute |
| `sprk_toolid` | Tool | Lookup (sprk_analysistool) | No | Optional tool handler (single tool per node) |
| `sprk_name` | Name | Text (200) | Yes | Display name |
| `sprk_executionorder` | Execution Order | Integer | Yes | Linear ordering for sequential execution |
| `sprk_dependsonjson` | Depends On | Multiline Text (100K) | No | JSON array of node IDs this node depends on |
| `sprk_outputvariable` | Output Variable | Text (100) | Yes | Variable name for referencing output in downstream nodes |
| `sprk_conditionjson` | Condition | Multiline Text (100K) | No | JSON condition expression for conditional execution |
| `sprk_configjson` | Configuration | Multiline Text (1M) | No | Action-type-specific configuration JSON |
| `sprk_modeldeploymentid` | Model Deployment | Lookup (sprk_aimodeldeployment) | No | Override AI model for this node |
| `sprk_timeoutseconds` | Timeout | Integer | No | Execution timeout in seconds (default: 300) |
| `sprk_retrycount` | Retry Count | Integer | No | Number of retry attempts (default: 0) |
| `sprk_position_x` | Canvas X | Integer | No | X coordinate on visual canvas |
| `sprk_position_y` | Canvas Y | Integer | No | Y coordinate on visual canvas |
| `sprk_isactive` | Is Active | Yes/No | Yes | Whether node is enabled |

### N:N Relationships

| Relationship Name | Related Entity | Description |
|-------------------|----------------|-------------|
| `sprk_playbooknode_skill` | sprk_analysisskill | Multiple skills per node |
| `sprk_playbooknode_knowledge` | sprk_analysisknowledge | Multiple knowledge sources per node |

### Lookup Relationships

| Field | Target Entity | Relationship Type |
|-------|---------------|-------------------|
| `sprk_playbookid` | sprk_analysisplaybook | N:1 (cascade delete) |
| `sprk_actionid` | sprk_analysisaction | N:1 (restrict delete) |
| `sprk_toolid` | sprk_analysistool | N:1 (restrict delete) |
| `sprk_modeldeploymentid` | sprk_aimodeldeployment | N:1 (restrict delete) |

---

## 3. Extended Entity: sprk_analysisaction

**Purpose**: Existing action entity extended with action type classification and compatibility settings.

### New Fields

| Logical Name | Display Name | Type | Required | Default | Description |
|--------------|--------------|------|----------|---------|-------------|
| `sprk_actiontype` | Action Type | Choice | No | `AiAnalysis (0)` | Classification of action behavior |
| `sprk_outputschemajson` | Output Schema | Multiline Text (1M) | No | null | JSON Schema defining expected output structure |
| `sprk_outputformat` | Output Format | Choice | No | `JSON (0)` | Format of action output |
| `sprk_modeldeploymentid` | Default Model | Lookup (sprk_aimodeldeployment) | No | null | Default AI model for this action |
| `sprk_allowsskills` | Allows Skills | Yes/No | No | true | Whether this action can have skills attached |
| `sprk_allowstools` | Allows Tools | Yes/No | No | true | Whether this action can have a tool handler |
| `sprk_allowsknowledge` | Allows Knowledge | Yes/No | No | true | Whether this action can have knowledge sources |
| `sprk_allowsdelivery` | Allows Delivery | Yes/No | No | false | Whether this action can configure delivery output |

### Choice Values: sprk_actiontype

| Value | Label | Category | Description |
|-------|-------|----------|-------------|
| 0 | AiAnalysis | AI | Tool handler execution (existing pipeline) |
| 1 | AiCompletion | AI | Raw LLM call with prompt template |
| 2 | AiEmbedding | AI | Generate embeddings |
| 10 | RuleEngine | Deterministic | Business rules evaluation |
| 11 | Calculation | Deterministic | Formula/computation |
| 12 | DataTransform | Deterministic | JSON/XML transformation |
| 20 | CreateTask | Integration | Create Dataverse task |
| 21 | SendEmail | Integration | Send via Microsoft Graph |
| 22 | UpdateRecord | Integration | Update Dataverse entity |
| 23 | CallWebhook | Integration | External HTTP call |
| 24 | SendTeamsMessage | Integration | Teams notification |
| 30 | Condition | Control Flow | If/else branching |
| 31 | Parallel | Control Flow | Fork into parallel paths |
| 32 | Wait | Control Flow | Wait for human approval |
| 40 | DeliverOutput | Delivery | Render and deliver final output |

### Choice Values: sprk_outputformat

| Value | Label | Description |
|-------|-------|-------------|
| 0 | JSON | Structured JSON output |
| 1 | Markdown | Formatted markdown text |
| 2 | PlainText | Unformatted text |

---

## 4. Extended Entity: sprk_analysistool

**Purpose**: Existing tool entity extended with output schema definition.

### New Fields

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| `sprk_outputschemajson` | Output Schema | Multiline Text (1M) | No | JSON Schema defining tool output structure |
| `sprk_outputexamplejson` | Output Example | Multiline Text (100K) | No | Sample output for documentation |

---

## 5. New Entity: sprk_aimodeldeployment

**Purpose**: Registry of available AI model deployments for per-node model selection.

### Entity Definition

| Property | Value |
|----------|-------|
| Logical Name | `sprk_aimodeldeployment` |
| Display Name | AI Model Deployment |
| Primary Field | `sprk_name` |
| Ownership | Organization |

### Fields

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| `sprk_aimodeldeploymentid` | AI Model Deployment ID | Uniqueidentifier | Yes (PK) | Primary key |
| `sprk_name` | Name | Text (200) | Yes | Display name (e.g., "GPT-4o Production") |
| `sprk_provider` | Provider | Choice | Yes | AI provider service |
| `sprk_modelid` | Model ID | Text (100) | Yes | Model identifier (e.g., "gpt-4o", "claude-3-opus") |
| `sprk_endpoint` | Endpoint | Text (500) | No | API endpoint URL |
| `sprk_capability` | Capability | Choice | Yes | What the model can do |
| `sprk_contextwindow` | Context Window | Integer | No | Maximum context tokens |
| `sprk_isdefault` | Is Default | Yes/No | Yes | Default model for this capability |
| `sprk_isactive` | Is Active | Yes/No | Yes | Whether deployment is enabled |

### Choice Values: sprk_provider

| Value | Label | Description |
|-------|-------|-------------|
| 0 | AzureOpenAI | Azure OpenAI Service |
| 1 | OpenAI | OpenAI API direct |
| 2 | Anthropic | Anthropic Claude |

### Choice Values: sprk_capability

| Value | Label | Description |
|-------|-------|-------------|
| 0 | Chat | Chat/completion models |
| 1 | Completion | Legacy completion models |
| 2 | Embedding | Embedding generation |

---

## 6. New Entity: sprk_deliverytemplate

**Purpose**: Templates for rendering AI outputs into delivery artifacts (documents, emails, etc.).

### Entity Definition

| Property | Value |
|----------|-------|
| Logical Name | `sprk_deliverytemplate` |
| Display Name | Delivery Template |
| Primary Field | `sprk_name` |
| Ownership | Organization |

### Fields

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| `sprk_deliverytemplateid` | Delivery Template ID | Uniqueidentifier | Yes (PK) | Primary key |
| `sprk_name` | Name | Text (200) | Yes | Display name |
| `sprk_type` | Template Type | Choice | Yes | Type of delivery output |
| `sprk_templatecontent` | Template Content | Multiline Text (1M) | No | Template markup (Handlebars syntax) |
| `sprk_templatefileid` | Template File | Text (500) | No | SPE file ID for Word templates |
| `sprk_placeholdersjson` | Placeholders | Multiline Text (100K) | No | JSON array of expected placeholders |
| `sprk_isactive` | Is Active | Yes/No | Yes | Whether template is enabled |

### Choice Values: sprk_type

| Value | Label | Description |
|-------|-------|-------------|
| 0 | WordDocument | Microsoft Word document via template |
| 1 | Email | Email message body |
| 2 | TeamsAdaptiveCard | Teams Adaptive Card |

---

## 7. New Entity: sprk_playbookrun

**Purpose**: Tracks playbook execution instances with status, timing, and aggregated outputs.

### Entity Definition

| Property | Value |
|----------|-------|
| Logical Name | `sprk_playbookrun` |
| Display Name | Playbook Run |
| Primary Field | Auto-generated name |
| Ownership | Organization |

### Fields

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| `sprk_playbookrunid` | Playbook Run ID | Uniqueidentifier | Yes (PK) | Primary key |
| `sprk_playbookid` | Playbook | Lookup (sprk_analysisplaybook) | Yes | Playbook that was executed |
| `sprk_status` | Status | Choice | Yes | Execution status |
| `sprk_triggeredby` | Triggered By | Lookup (systemuser) | Yes | User who initiated execution |
| `sprk_inputcontextjson` | Input Context | Multiline Text (1M) | No | JSON with documents, parameters |
| `sprk_startedon` | Started On | DateTime | No | Execution start time |
| `sprk_completedon` | Completed On | DateTime | No | Execution end time |
| `sprk_outputsjson` | Outputs | Multiline Text (1M) | No | JSON with aggregated node outputs |
| `sprk_errormessage` | Error Message | Multiline Text (100K) | No | Error details if failed |

### Choice Values: sprk_status (Playbook Run)

| Value | Label | Description |
|-------|-------|-------------|
| 0 | Pending | Queued, not yet started |
| 1 | Running | Currently executing |
| 2 | Completed | Successfully finished |
| 3 | Failed | Execution failed |
| 4 | Cancelled | Cancelled by user |

---

## 8. New Entity: sprk_playbooknoderun

**Purpose**: Tracks individual node execution within a playbook run with detailed metrics.

### Entity Definition

| Property | Value |
|----------|-------|
| Logical Name | `sprk_playbooknoderun` |
| Display Name | Playbook Node Run |
| Primary Field | Auto-generated name |
| Ownership | Organization |

### Fields

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| `sprk_playbooknoderunid` | Node Run ID | Uniqueidentifier | Yes (PK) | Primary key |
| `sprk_playbookrunid` | Playbook Run | Lookup (sprk_playbookrun) | Yes | Parent run instance |
| `sprk_playbooknodeid` | Playbook Node | Lookup (sprk_playbooknode) | Yes | Node definition that was executed |
| `sprk_status` | Status | Choice | Yes | Node execution status |
| `sprk_inputjson` | Input | Multiline Text (1M) | No | Input data provided to node |
| `sprk_outputjson` | Output | Multiline Text (1M) | No | Output data from node |
| `sprk_tokensin` | Tokens In | Integer | No | Input tokens consumed (AI nodes) |
| `sprk_tokensout` | Tokens Out | Integer | No | Output tokens generated (AI nodes) |
| `sprk_durationms` | Duration (ms) | Integer | No | Execution time in milliseconds |
| `sprk_errormessage` | Error Message | Multiline Text (100K) | No | Error details if failed |
| `sprk_validationwarnings` | Validation Warnings | Multiline Text (100K) | No | Output validation warnings |

### Choice Values: sprk_status (Node Run)

| Value | Label | Description |
|-------|-------|-------------|
| 0 | Pending | Waiting for dependencies |
| 1 | Running | Currently executing |
| 2 | Completed | Successfully finished |
| 3 | Failed | Execution failed |
| 4 | Skipped | Skipped due to condition |

---

## 9. Relationship Summary

### N:N Relationships

| Relationship Name | Entity 1 | Entity 2 | Purpose |
|-------------------|----------|----------|---------|
| `sprk_playbooknode_skill` | sprk_playbooknode | sprk_analysisskill | Multiple skills per node |
| `sprk_playbooknode_knowledge` | sprk_playbooknode | sprk_analysisknowledge | Multiple knowledge per node |

### N:1 Lookup Relationships

| Child Entity | Lookup Field | Parent Entity | Delete Behavior |
|--------------|--------------|---------------|-----------------|
| sprk_playbooknode | sprk_playbookid | sprk_analysisplaybook | Cascade |
| sprk_playbooknode | sprk_actionid | sprk_analysisaction | Restrict |
| sprk_playbooknode | sprk_toolid | sprk_analysistool | Restrict |
| sprk_playbooknode | sprk_modeldeploymentid | sprk_aimodeldeployment | Restrict |
| sprk_playbookrun | sprk_playbookid | sprk_analysisplaybook | Cascade |
| sprk_playbookrun | sprk_triggeredby | systemuser | Restrict |
| sprk_playbooknoderun | sprk_playbookrunid | sprk_playbookrun | Cascade |
| sprk_playbooknoderun | sprk_playbooknodeid | sprk_playbooknode | Restrict |
| sprk_analysisaction | sprk_modeldeploymentid | sprk_aimodeldeployment | Restrict |

---

## 10. Choice Option Sets - Complete Reference

### Global Option Sets

| Option Set Name | Used By | Values |
|-----------------|---------|--------|
| `sprk_playbookmode` | sprk_analysisplaybook | Legacy (0), NodeBased (1) |
| `sprk_playbooktype` | sprk_analysisplaybook | AiAnalysis (0), Workflow (1), Hybrid (2) |
| `sprk_triggertype` | sprk_analysisplaybook | Manual (0), Scheduled (1), RecordCreated (2), RecordUpdated (3) |
| `sprk_actiontype` | sprk_analysisaction | See Section 3 - 15 values |
| `sprk_outputformat` | sprk_analysisaction | JSON (0), Markdown (1), PlainText (2) |
| `sprk_aiprovider` | sprk_aimodeldeployment | AzureOpenAI (0), OpenAI (1), Anthropic (2) |
| `sprk_aicapability` | sprk_aimodeldeployment | Chat (0), Completion (1), Embedding (2) |
| `sprk_deliverytype` | sprk_deliverytemplate | WordDocument (0), Email (1), TeamsAdaptiveCard (2) |
| `sprk_playbookrunstatus` | sprk_playbookrun | Pending (0), Running (1), Completed (2), Failed (3), Cancelled (4) |
| `sprk_noderunstatus` | sprk_playbooknoderun | Pending (0), Running (1), Completed (2), Failed (3), Skipped (4) |

---

## 11. Entity Count Summary

| Category | Count | Entities |
|----------|-------|----------|
| **Extended Entities** | 3 | sprk_analysisplaybook, sprk_analysisaction, sprk_analysistool |
| **New Entities** | 5 | sprk_playbooknode, sprk_aimodeldeployment, sprk_deliverytemplate, sprk_playbookrun, sprk_playbooknoderun |
| **Total** | 8 | All entities for node-based playbook system |

---

## 12. Implementation Notes

### Field Size Rationale

| Field | Size | Rationale |
|-------|------|-----------|
| `*json` fields | 1M chars | JSON payloads can be large (canvas layout, outputs) |
| `sprk_name` | 200 chars | Standard display name length |
| `sprk_outputvariable` | 100 chars | Variable names should be short and readable |
| `sprk_modelid` | 100 chars | Model identifiers like "gpt-4o-2024-05-13" |
| `sprk_endpoint` | 500 chars | Full Azure endpoint URLs |

### Backward Compatibility

- `sprk_playbookmode` defaults to `Legacy` - existing playbooks work unchanged
- Legacy mode ignores `sprk_playbooknode` entities entirely
- Mode check happens at execution start in `PlaybookOrchestrationService`

### Index Recommendations

| Entity | Indexed Fields | Purpose |
|--------|---------------|---------|
| sprk_playbooknode | sprk_playbookid, sprk_executionorder | Node retrieval by playbook |
| sprk_playbookrun | sprk_playbookid, sprk_startedon | Run history queries |
| sprk_playbooknoderun | sprk_playbookrunid | Node runs by parent run |

---

*Schema specification for Task 001. Ready for implementation in Task 002.*
