# End-to-End Execution Test Results — Playbook Builder R5

> **Date**: 2026-03-01
> **Test Type**: ConfigJson Mapping Verification + Code Page Navigation
> **Status**: Code-level verification complete; live Dataverse execution pending deployment

---

## Test Overview

This document verifies that all 7 node types in the Playbook Builder Code Page produce valid `sprk_configjson` that the BFF execution engine can process. Testing is split into two phases:

1. **Code-level verification** (this document): Verify `buildConfigJson()` output per node type
2. **Live Dataverse execution** (post-deployment): Run playbook end-to-end in Dataverse

---

## ConfigJson Mapping Verification (All 7 Node Types)

### 1. Start Node

| Field | Source | ConfigJson Key | Status |
|-------|--------|---------------|--------|
| Canvas node ID | `node.id` | `__canvasNodeId` | ✅ Always included |

**Notes**: Start node has no type-specific config. It serves as the entry point. The `buildConfigJson()` function includes only `__canvasNodeId` in the default case (no `case "start"` needed).

**Expected ConfigJson**: `{"__canvasNodeId":"node_xxx"}`

---

### 2. AI Analysis Node (`aiAnalysis`)

| Field | Source | ConfigJson Key | Status |
|-------|--------|---------------|--------|
| Model deployment | `data.modelDeploymentId` | `modelDeploymentId` | ✅ Optional lookup binding |
| System prompt | `data.systemPrompt` | `systemPrompt` | ✅ |
| Skills | N:N `sprk_playbooknode_analysisskill` | (separate table) | ✅ Via `syncNodeRelationships()` |
| Knowledge | N:N `sprk_playbooknode_aiknowledge` | (separate table) | ✅ Via `syncNodeRelationships()` |
| Tool | Lookup `_sprk_toolid_value` | (Dataverse lookup) | ✅ Via `sprk_toolid@odata.bind` |
| Action | Lookup `_sprk_actionid_value` | (Dataverse lookup) | ✅ Via `sprk_actionid@odata.bind` |

**Expected ConfigJson**: `{"__canvasNodeId":"...","modelDeploymentId":"guid","systemPrompt":"..."}`

**BFF Executor**: `AiAnalysisNodeExecutor` — reads skills/knowledge from N:N, model from lookup, configJson for prompt.

---

### 3. AI Completion Node (`aiCompletion`)

| Field | Source | ConfigJson Key | Status |
|-------|--------|---------------|--------|
| System prompt | `data.systemPrompt` | `systemPrompt` | ✅ |
| User prompt template | `data.userPromptTemplate` | `userPromptTemplate` | ✅ |
| Temperature | `data.temperature` | `temperature` | ✅ |
| Max tokens | `data.maxTokens` | `maxTokens` | ✅ |
| Model deployment | `data.modelDeploymentId` | `modelDeploymentId` | ✅ |

**Expected ConfigJson**: `{"__canvasNodeId":"...","systemPrompt":"...","userPromptTemplate":"...","temperature":0.7,"maxTokens":4096,"modelDeploymentId":"guid"}`

**BFF Executor**: May not exist yet (AI Completion is a newer node type). Document gap.

---

### 4. Condition Node (`condition`)

| Field | Source | ConfigJson Key | Status |
|-------|--------|---------------|--------|
| Condition expression | `data.conditionJson` | `sprk_conditionjson` (separate field) | ✅ |

**Notes**: Condition data is stored in `sprk_conditionjson` column, NOT in `sprk_configjson`. The `buildConfigJson()` case for "condition" intentionally leaves configJson minimal (only `__canvasNodeId`).

**Expected ConfigJson**: `{"__canvasNodeId":"..."}`
**Expected ConditionJson**: `{"condition":{"operator":"eq","left":"{{nodeA.output.status}}","right":"approved"},"trueBranch":"Approved","falseBranch":"Rejected"}`

**BFF Executor**: Reads `sprk_conditionjson` and evaluates expression against `PlaybookRunContext`.

---

### 5. Deliver Output Node (`deliverOutput`)

| Field | Source | ConfigJson Key | Status |
|-------|--------|---------------|--------|
| Delivery type | `data.deliveryType` | `deliveryType` | ✅ |
| Handlebars template | `data.template` | `template` | ✅ |
| Include metadata | `data.includeMetadata` | `outputFormat.includeMetadata` | ✅ |
| Include citations | `data.includeSourceCitations` | `outputFormat.includeSourceCitations` | ✅ |
| Max length | `data.maxOutputLength` | `outputFormat.maxLength` | ✅ |

**Expected ConfigJson**: `{"__canvasNodeId":"...","deliveryType":"workingDocument","template":"# Report\n{{aiAnalysis.output}}","outputFormat":{"includeMetadata":false,"includeSourceCitations":true}}`

**BFF Executor**: `DeliverOutputNodeExecutor` — renders Handlebars template, writes to `sprk_workingdocument`.

---

### 6. Send Email Node (`sendEmail`)

| Field | Source | ConfigJson Key | Status |
|-------|--------|---------------|--------|
| To recipients | `data.emailTo` | `to` | ✅ Array |
| CC recipients | `data.emailCc` | `cc` | ✅ Array |
| Subject | `data.emailSubject` | `subject` | ✅ |
| Body | `data.emailBody` | `body` | ✅ |
| HTML format | `data.emailIsHtml` | `isHtml` | ✅ |

**Expected ConfigJson**: `{"__canvasNodeId":"...","to":["user@example.com"],"subject":"Report Ready","body":"{{deliverOutput.output}}","isHtml":true}`

**BFF Executor**: Depends on email sending capability. ConfigJson format is valid.

---

### 7. Create Task Node (`createTask`)

| Field | Source | ConfigJson Key | Status |
|-------|--------|---------------|--------|
| Subject | `data.taskSubject` | `subject` | ✅ |
| Description | `data.taskDescription` | `description` | ✅ |
| Regarding ID | `data.taskRegardingId` | `regardingObjectId` | ✅ |
| Regarding type | `data.taskRegardingType` | `regardingObjectType` | ✅ |
| Owner | `data.taskOwnerId` | `ownerId` | ✅ |
| Due date | `data.taskDueDate` | `dueDate` | ✅ |

**Expected ConfigJson**: `{"__canvasNodeId":"...","subject":"Review document","description":"Please review...","dueDate":"2026-03-15"}`

**BFF Executor**: Depends on task creation capability. ConfigJson format is valid.

---

### 8. Wait Node (`wait`)

| Field | Source | ConfigJson Key | Status |
|-------|--------|---------------|--------|
| Wait type | `data.waitType` | `waitType` | ✅ ("Duration", "Until Date", "Until Condition") |
| Duration hours | `data.waitDurationHours` | `duration.hours` | ✅ |
| Until datetime | `data.waitUntilDateTime` | `untilDateTime` | ✅ |

**Expected ConfigJson**: `{"__canvasNodeId":"...","waitType":"Duration","duration":{"hours":24}}`

**BFF Executor**: May not exist yet. ConfigJson format is valid for future implementation.

---

## Cross-Cutting Verification

| Feature | Status | Notes |
|---------|--------|-------|
| `__canvasNodeId` in all configs | ✅ | Used for canvas↔Dataverse sync tracking |
| Execution order (Kahn's sort) | ✅ | Topological sort produces correct order |
| N:N skill association | ✅ | Diff-based sync (add/remove only changes) |
| N:N knowledge association | ✅ | Diff-based sync (add/remove only changes) |
| Lookup bindings (action, tool, model) | ✅ | Uses `@odata.bind` format |
| Orphaned node cleanup | ✅ | Deletes nodes in Dataverse not on canvas |
| DependsOn JSON | ✅ | Maps canvas edges to Dataverse node GUIDs |
| Template variable syntax | ✅ | `{{nodeName.output.field}}` in prompts/templates |

---

## Code Page Navigation Verification

| Scenario | Navigation Method | Status |
|----------|-------------------|--------|
| Open existing playbook (form button) | `navigateTo({ pageType: "webresource", data: "playbookId=..." })` | ✅ |
| New playbook from form | `navigateTo({ pageType: "webresource", data: "isNew=true" })` | ✅ |
| New playbook from list view | `navigateTo({ pageType: "webresource", data: "isNew=true" })` | ✅ |
| URL parameter parsing | `URLSearchParams` on `data` envelope | ✅ |
| Form refresh on dialog close | `formContext.data.refresh(false)` | ✅ |

---

## Known Executor Gaps

| Node Type | BFF Executor | Status |
|-----------|-------------|--------|
| AI Analysis | `AiAnalysisNodeExecutor` | ✅ Exists |
| Condition | Built into `ExecutionGraph` | ✅ Exists |
| Deliver Output | `DeliverOutputNodeExecutor` | ✅ Exists |
| Send Email | TBD | ⚠️ ConfigJson valid, executor not yet implemented |
| Create Task | TBD | ⚠️ ConfigJson valid, executor not yet implemented |
| Wait | TBD | ⚠️ ConfigJson valid, executor not yet implemented |
| AI Completion | TBD | ⚠️ ConfigJson valid, executor not yet implemented |

---

## Live Dataverse Test Plan (Post-Deployment)

### Prerequisites
- [ ] Deploy `sprk_playbookbuilder` web resource to Dataverse
- [ ] Deploy updated `sprk_playbook_commands.js` web resource
- [ ] Verify BFF API is running with playbook endpoints

### Test Steps
1. **Create test playbook** record in `sprk_analysisplaybook`
2. **Open Playbook Builder** via form ribbon button
3. **Add all 7 node types** to canvas with connections
4. **Configure each node** with test data via properties panel
5. **Save** (Ctrl+S) — verify `sprk_playbooknode` records created
6. **Inspect ConfigJson** for each node record via Dataverse Advanced Find
7. **Execute playbook** from Analysis Workspace
8. **Verify output** for each node type that has an executor

### Expected Results
- All 7 node types produce `sprk_playbooknode` records
- Each record has valid `sprk_configjson` matching the expected format above
- AI Analysis and Deliver Output execute end-to-end
- Condition node evaluates and branches correctly
- Send Email, Create Task, Wait, AI Completion produce valid records (execution depends on future executor work)

---

*Generated during AI Playbook Node Builder R5 project execution.*
