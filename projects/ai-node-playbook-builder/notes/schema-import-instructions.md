# Dataverse Schema Import Instructions

> **Version**: 1.0
> **Created**: 2026-01-09
> **Task**: 002-implement-dataverse-schema
> **Prerequisites**: Task 001 schema design complete
> **Target Environment**: spaarkedev1.crm.dynamics.com

---

## Overview

This document provides step-by-step instructions for implementing the node-based playbook schema in Dataverse. All schema changes are made via Power Platform Maker portal or PAC CLI.

**Important**: Use **unmanaged solutions only** per ADR-022.

---

## Prerequisites

### 1. PAC CLI Authentication

```powershell
# Authenticate to dev environment
pac auth create --environment https://spaarkedev1.crm.dynamics.com

# Verify authentication
pac auth list
```

### 2. Solution Context

All schema changes should be made within the existing Spaarke solution or a dedicated feature solution:

```powershell
# Option A: Use existing solution
# (Make changes in Power Platform, they'll be captured in existing solution)

# Option B: Create feature solution for this work
pac solution create --publisher-name spaarke --publisher-prefix sprk --name SpaarkePlaybookNodes
```

---

## Phase 1: Create Global Option Sets

Create these first since they're referenced by entity fields.

### 1.1 sprk_playbookmode

| Property | Value |
|----------|-------|
| Display Name | Playbook Mode |
| Logical Name | sprk_playbookmode |
| Type | Global |

| Value | Label |
|-------|-------|
| 0 | Legacy |
| 1 | NodeBased |

**Power Platform Path**: Make.powerapps.com > Solutions > Spaarke > Add existing > Choice > New choice

### 1.2 sprk_playbooktype

| Property | Value |
|----------|-------|
| Display Name | Playbook Type |
| Logical Name | sprk_playbooktype |

| Value | Label |
|-------|-------|
| 0 | AiAnalysis |
| 1 | Workflow |
| 2 | Hybrid |

### 1.3 sprk_triggertype

| Property | Value |
|----------|-------|
| Display Name | Trigger Type |
| Logical Name | sprk_triggertype |

| Value | Label |
|-------|-------|
| 0 | Manual |
| 1 | Scheduled |
| 2 | RecordCreated |
| 3 | RecordUpdated |

### 1.4 sprk_outputformat

| Property | Value |
|----------|-------|
| Display Name | Output Format |
| Logical Name | sprk_outputformat |

| Value | Label |
|-------|-------|
| 0 | JSON |
| 1 | Markdown |
| 2 | PlainText |

### 1.5 sprk_aiprovider

| Property | Value |
|----------|-------|
| Display Name | AI Provider |
| Logical Name | sprk_aiprovider |

| Value | Label |
|-------|-------|
| 0 | AzureOpenAI |
| 1 | OpenAI |
| 2 | Anthropic |

### 1.6 sprk_aicapability

| Property | Value |
|----------|-------|
| Display Name | AI Capability |
| Logical Name | sprk_aicapability |

| Value | Label |
|-------|-------|
| 0 | Chat |
| 1 | Completion |
| 2 | Embedding |

### 1.7 sprk_playbookrunstatus

| Property | Value |
|----------|-------|
| Display Name | Playbook Run Status |
| Logical Name | sprk_playbookrunstatus |

| Value | Label |
|-------|-------|
| 0 | Pending |
| 1 | Running |
| 2 | Completed |
| 3 | Failed |
| 4 | Cancelled |

### 1.8 sprk_noderunstatus

| Property | Value |
|----------|-------|
| Display Name | Node Run Status |
| Logical Name | sprk_noderunstatus |

| Value | Label |
|-------|-------|
| 0 | Pending |
| 1 | Running |
| 2 | Completed |
| 3 | Failed |
| 4 | Skipped |

---

## Phase 2: Extend Existing Entities

**Important Execution Order**: Create `sprk_aiactiontype` and `sprk_analysisdeliverytype` from Phase 3 (sections 3.1 and 3.2) FIRST, then return here to extend entities with lookup fields.

### 2.1 Extend sprk_analysisplaybook

Add these fields to the existing Analysis Playbook entity:

| Logical Name | Display Name | Type | Required | Default |
|--------------|--------------|------|----------|---------|
| `sprk_playbookmode` | Playbook Mode | Choice (sprk_playbookmode) | No | 0 (Legacy) |
| `sprk_playbooktype` | Playbook Type | Choice (sprk_playbooktype) | No | 0 (AiAnalysis) |
| `sprk_canvaslayoutjson` | Canvas Layout | Multiline Text (1,048,576) | No | - |
| `sprk_triggertype` | Trigger Type | Choice (sprk_triggertype) | No | 0 (Manual) |
| `sprk_triggerconfigjson` | Trigger Config | Multiline Text (100,000) | No | - |
| `sprk_version` | Version | Whole Number | No | 1 |
| `sprk_maxparallelnodes` | Max Parallel Nodes | Whole Number | No | 3 |
| `sprk_continueonerror` | Continue On Error | Yes/No | No | No |

**Power Platform Path**: Make.powerapps.com > Solutions > Spaarke > Tables > Analysis Playbook > Columns > New column

### 2.2 Extend sprk_analysisaction

Add these fields to the existing Analysis Action entity:

| Logical Name | Display Name | Type | Required | Default |
|--------------|--------------|------|----------|---------|
| `sprk_actiontypeid` | Action Type | Lookup (sprk_aiactiontype) | No | - |
| `sprk_outputschemajson` | Output Schema | Multiline Text (1,048,576) | No | - |
| `sprk_outputformat` | Output Format | Choice (sprk_outputformat) | No | 0 (JSON) |
| `sprk_modeldeploymentid` | Default Model | Lookup (sprk_aimodeldeployment) | No | - |
| `sprk_allowsskills` | Allows Skills | Yes/No | No | Yes |
| `sprk_allowstools` | Allows Tools | Yes/No | No | Yes |
| `sprk_allowsknowledge` | Allows Knowledge | Yes/No | No | Yes |
| `sprk_allowsdelivery` | Allows Delivery | Yes/No | No | No |

### 2.3 Extend sprk_analysistool

Add these fields to the existing Analysis Tool entity:

| Logical Name | Display Name | Type | Required |
|--------------|--------------|------|----------|
| `sprk_outputschemajson` | Output Schema | Multiline Text (1,048,576) | No |
| `sprk_outputexamplejson` | Output Example | Multiline Text (100,000) | No |

---

## Phase 3: Create New Entities

### 3.1 Create sprk_aiactiontype

**Note**: Create this lookup entity FIRST since sprk_analysisaction references it.

| Property | Value |
|----------|-------|
| Display Name | AI Action Type |
| Plural Name | AI Action Types |
| Logical Name | sprk_aiactiontype |
| Primary Column | sprk_name (200 chars) |
| Ownership | Organization |

**Columns:**

| Logical Name | Display Name | Type | Required |
|--------------|--------------|------|----------|
| `sprk_name` | Name | Text (200) | Yes |
| `sprk_value` | Value | Whole Number | Yes |
| `sprk_description` | Description | Multiline Text (2,000) | No |
| `sprk_isactive` | Is Active | Yes/No | Yes |

**Seed Data** (create these records after entity creation):

| Value | Name | Description |
|-------|------|-------------|
| 0 | AiAnalysis | AI-powered document/data analysis |
| 1 | DataRetrieval | Fetch data from external sources |
| 2 | DataTransformation | Transform/map data between formats |
| 3 | Validation | Validate data against rules |
| 4 | Calculation | Perform calculations |
| 5 | Decision | Make decisions based on conditions |
| 6 | Notification | Send notifications |
| 7 | DocumentGeneration | Generate documents from templates |
| 8 | Approval | Request and wait for approval |
| 9 | Integration | Call external APIs/services |
| 10 | Loop | Iterate over collections |
| 11 | Parallel | Execute nodes in parallel |
| 12 | Delay | Wait for specified duration |
| 13 | SetVariable | Set workflow variables |
| 14 | EndWorkflow | Terminate workflow execution |

**Power Platform Path**: Make.powerapps.com > Solutions > Spaarke > Tables > New table

### 3.2 Create sprk_analysisdeliverytype

| Property | Value |
|----------|-------|
| Display Name | Analysis Delivery Type |
| Plural Name | Analysis Delivery Types |
| Logical Name | sprk_analysisdeliverytype |
| Primary Column | sprk_name (200 chars) |
| Ownership | Organization |

**Columns:**

| Logical Name | Display Name | Type | Required |
|--------------|--------------|------|----------|
| `sprk_name` | Name | Text (200) | Yes |
| `sprk_value` | Value | Whole Number | Yes |
| `sprk_description` | Description | Multiline Text (2,000) | No |
| `sprk_isactive` | Is Active | Yes/No | Yes |

**Seed Data** (create these records after entity creation):

| Value | Name | Description |
|-------|------|-------------|
| 0 | WordDocument | Generate Word document from template |
| 1 | Email | Send results via email |
| 2 | TeamsAdaptiveCard | Post adaptive card to Teams channel |

**Power Platform Path**: Make.powerapps.com > Solutions > Spaarke > Tables > New table

### 3.3 Create sprk_aimodeldeployment

**Note**: Create this entity since other entities reference it.

| Property | Value |
|----------|-------|
| Display Name | AI Model Deployment |
| Plural Name | AI Model Deployments |
| Logical Name | sprk_aimodeldeployment |
| Primary Column | sprk_name (200 chars) |
| Ownership | Organization |

**Columns:**

| Logical Name | Display Name | Type | Required |
|--------------|--------------|------|----------|
| `sprk_name` | Name | Text (200) | Yes |
| `sprk_provider` | Provider | Choice (sprk_aiprovider) | Yes |
| `sprk_modelid` | Model ID | Text (100) | Yes |
| `sprk_endpoint` | Endpoint | Text (500) | No |
| `sprk_capability` | Capability | Choice (sprk_aicapability) | Yes |
| `sprk_contextwindow` | Context Window | Whole Number | No |
| `sprk_isdefault` | Is Default | Yes/No | Yes |
| `sprk_isactive` | Is Active | Yes/No | Yes |

### 3.4 Create sprk_deliverytemplate

| Property | Value |
|----------|-------|
| Display Name | Delivery Template |
| Plural Name | Delivery Templates |
| Logical Name | sprk_deliverytemplate |
| Primary Column | sprk_name (200 chars) |
| Ownership | Organization |

**Columns:**

| Logical Name | Display Name | Type | Required |
|--------------|--------------|------|----------|
| `sprk_name` | Name | Text (200) | Yes |
| `sprk_deliverytypeid` | Delivery Type | Lookup (sprk_analysisdeliverytype) | Yes |
| `sprk_templatecontent` | Template Content | Multiline Text (1,048,576) | No |
| `sprk_templatefileid` | Template File | Text (500) | No |
| `sprk_placeholdersjson` | Placeholders | Multiline Text (100,000) | No |
| `sprk_isactive` | Is Active | Yes/No | Yes |

**Lookup Delete Behaviors:**

| Lookup | Delete Behavior |
|--------|-----------------|
| sprk_deliverytypeid → sprk_analysisdeliverytype | Restrict |

### 3.5 Create sprk_playbooknode

| Property | Value |
|----------|-------|
| Display Name | Playbook Node |
| Plural Name | Playbook Nodes |
| Logical Name | sprk_playbooknode |
| Primary Column | sprk_name (200 chars) |
| Ownership | Organization |

**Columns:**

| Logical Name | Display Name | Type | Required |
|--------------|--------------|------|----------|
| `sprk_name` | Name | Text (200) | Yes |
| `sprk_playbookid` | Playbook | Lookup (sprk_analysisplaybook) | Yes |
| `sprk_actionid` | Action | Lookup (sprk_analysisaction) | Yes |
| `sprk_toolid` | Tool | Lookup (sprk_analysistool) | No |
| `sprk_executionorder` | Execution Order | Whole Number | Yes |
| `sprk_dependsonjson` | Depends On | Multiline Text (100,000) | No |
| `sprk_outputvariable` | Output Variable | Text (100) | Yes |
| `sprk_conditionjson` | Condition | Multiline Text (100,000) | No |
| `sprk_configjson` | Configuration | Multiline Text (1,048,576) | No |
| `sprk_modeldeploymentid` | Model Deployment | Lookup (sprk_aimodeldeployment) | No |
| `sprk_timeoutseconds` | Timeout | Whole Number | No |
| `sprk_retrycount` | Retry Count | Whole Number | No |
| `sprk_position_x` | Canvas X | Whole Number | No |
| `sprk_position_y` | Canvas Y | Whole Number | No |
| `sprk_isactive` | Is Active | Yes/No | Yes |

**Lookup Delete Behaviors:**

| Lookup | Delete Behavior |
|--------|-----------------|
| sprk_playbookid → sprk_analysisplaybook | Cascade |
| sprk_actionid → sprk_analysisaction | Restrict |
| sprk_toolid → sprk_analysistool | Restrict |
| sprk_modeldeploymentid → sprk_aimodeldeployment | Restrict |

### 3.6 Create sprk_playbookrun

| Property | Value |
|----------|-------|
| Display Name | Playbook Run |
| Plural Name | Playbook Runs |
| Logical Name | sprk_playbookrun |
| Primary Column | sprk_name (auto-number) |
| Ownership | Organization |

**Columns:**

| Logical Name | Display Name | Type | Required |
|--------------|--------------|------|----------|
| `sprk_playbookid` | Playbook | Lookup (sprk_analysisplaybook) | Yes |
| `sprk_status` | Status | Choice (sprk_playbookrunstatus) | Yes |
| `sprk_triggeredby` | Triggered By | Lookup (systemuser) | Yes |
| `sprk_inputcontextjson` | Input Context | Multiline Text (1,048,576) | No |
| `sprk_startedon` | Started On | Date and Time | No |
| `sprk_completedon` | Completed On | Date and Time | No |
| `sprk_outputsjson` | Outputs | Multiline Text (1,048,576) | No |
| `sprk_errormessage` | Error Message | Multiline Text (100,000) | No |

**Lookup Delete Behaviors:**

| Lookup | Delete Behavior |
|--------|-----------------|
| sprk_playbookid → sprk_analysisplaybook | Cascade |
| sprk_triggeredby → systemuser | Restrict |

### 3.7 Create sprk_playbooknoderun

| Property | Value |
|----------|-------|
| Display Name | Playbook Node Run |
| Plural Name | Playbook Node Runs |
| Logical Name | sprk_playbooknoderun |
| Primary Column | sprk_name (auto-number) |
| Ownership | Organization |

**Columns:**

| Logical Name | Display Name | Type | Required |
|--------------|--------------|------|----------|
| `sprk_playbookrunid` | Playbook Run | Lookup (sprk_playbookrun) | Yes |
| `sprk_playbooknodeid` | Playbook Node | Lookup (sprk_playbooknode) | Yes |
| `sprk_status` | Status | Choice (sprk_noderunstatus) | Yes |
| `sprk_inputjson` | Input | Multiline Text (1,048,576) | No |
| `sprk_outputjson` | Output | Multiline Text (1,048,576) | No |
| `sprk_tokensin` | Tokens In | Whole Number | No |
| `sprk_tokensout` | Tokens Out | Whole Number | No |
| `sprk_durationms` | Duration (ms) | Whole Number | No |
| `sprk_errormessage` | Error Message | Multiline Text (100,000) | No |
| `sprk_validationwarnings` | Validation Warnings | Multiline Text (100,000) | No |

**Lookup Delete Behaviors:**

| Lookup | Delete Behavior |
|--------|-----------------|
| sprk_playbookrunid → sprk_playbookrun | Cascade |
| sprk_playbooknodeid → sprk_playbooknode | Restrict |

---

## Phase 4: Create N:N Relationships

### 4.1 sprk_playbooknode_skill

| Property | Value |
|----------|-------|
| Relationship Name | sprk_playbooknode_skill |
| Table 1 | sprk_playbooknode |
| Table 2 | sprk_analysisskill |
| Display Option | Use Plural Name |

**Power Platform Path**: Make.powerapps.com > Solutions > Spaarke > Tables > Playbook Node > Relationships > New relationship > Many-to-many

### 4.2 sprk_playbooknode_knowledge

| Property | Value |
|----------|-------|
| Relationship Name | sprk_playbooknode_knowledge |
| Table 1 | sprk_playbooknode |
| Table 2 | sprk_analysisknowledge |
| Display Option | Use Plural Name |

---

## Phase 5: Publish and Validate

### 5.1 Publish All Customizations

```powershell
# Publish all customizations in the solution
pac solution publish
```

Or via Power Platform:
- Make.powerapps.com > Solutions > Spaarke > Publish all customizations

### 5.2 Validate Schema

Run these queries to verify schema implementation:

```powershell
# List all sprk_ entities
pac data fetch --entity "EntityDefinitions" --filter "SchemaName eq 'sprk_playbooknode'"

# Verify option sets
pac data fetch --entity "GlobalOptionSetDefinitions" --filter "startswith(Name, 'sprk_')"
```

### 5.3 Export Solution (Optional)

For source control or backup:

```powershell
# Export unmanaged solution
pac solution export --path ./SpaarkePlaybookNodes.zip --name SpaarkePlaybookNodes --managed false
```

---

## Verification Checklist

After completing all phases, verify:

### Option Sets Created (Global)
- [ ] sprk_playbookmode (2 values)
- [ ] sprk_playbooktype (3 values)
- [ ] sprk_triggertype (4 values)
- [ ] sprk_outputformat (3 values)
- [ ] sprk_aiprovider (3 values)
- [ ] sprk_aicapability (3 values)
- [ ] sprk_playbookrunstatus (5 values)
- [ ] sprk_noderunstatus (5 values)

### Entities Extended
- [ ] sprk_analysisplaybook (8 new fields)
- [ ] sprk_analysisaction (8 new fields)
- [ ] sprk_analysistool (2 new fields)

### New Entities Created
- [ ] sprk_aiactiontype (4 fields + 15 seed records)
- [ ] sprk_analysisdeliverytype (4 fields + 3 seed records)
- [ ] sprk_aimodeldeployment (9 fields)
- [ ] sprk_deliverytemplate (6 fields)
- [ ] sprk_playbooknode (16 fields)
- [ ] sprk_playbookrun (9 fields)
- [ ] sprk_playbooknoderun (11 fields)

### Relationships Created
- [ ] sprk_playbooknode_skill (N:N)
- [ ] sprk_playbooknode_knowledge (N:N)
- [ ] sprk_analysisaction → sprk_aiactiontype (Lookup)
- [ ] sprk_deliverytemplate → sprk_analysisdeliverytype (Lookup)
- [ ] All lookup relationships with correct delete behaviors

### Final Validation
- [ ] All customizations published
- [ ] No errors in solution checker
- [ ] Test queries return expected results

---

## Troubleshooting

### Common Issues

| Issue | Resolution |
|-------|------------|
| Option set not found when creating field | Create global option sets in Phase 1 first |
| Lookup target not found | Create referenced entity before entity with lookup |
| Delete behavior error | Check relationship type and existing data |
| Schema name conflict | Verify prefix is `sprk_` for all custom components |

### Rollback

If needed, delete entities in reverse order:
1. Delete sprk_playbooknoderun
2. Delete sprk_playbookrun
3. Delete sprk_playbooknode
4. Delete sprk_deliverytemplate
5. Delete sprk_aimodeldeployment
6. Remove new fields from extended entities
7. Delete sprk_analysisdeliverytype
8. Delete sprk_aiactiontype
9. Delete global option sets

---

## Next Steps

After schema implementation:
1. **Task 003**: Create NodeService for CRUD operations
2. **Task 009**: Deploy and test in dev environment

---

*Schema import instructions for Task 002. Execute during Task 009 deployment phase.*
