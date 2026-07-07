# Data Model — `sprk_playbooknode` (FROZEN playbook-engine node table)

> **Last Updated**: 2026-07-07
> **Reviewed By**: spaarke-ai-architecture-redesign-r1 task 052 (FR-P4-03)
> **Status**: **Frozen engine (as-is)**
> **Schema source**: live `spaarkedev1` describe (2026-07-07) cross-checked against shipped code (`NodeService.cs`, `INodeExecutor.cs`)

---

## ⛔ THE ENGINE IS FROZEN

**The node-graph playbook engine is FROZEN** (design OQ-2 resolution / D11, operator-ratified 2026-07-05 — see [`SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) §4.2/§4.2.1):

- **NO new capability may land on the node-graph engine.** It serves ONLY the existing Insights-family pipelines, maintained-but-frozen, retired by attrition when those pipelines next restructure.
- **New composites are `coded` workflows** — registered `ICodedWorkflow` C# classes referenced by `sprk_analysisaction.sprk_workflowclass` (kind = `coded`), reading their hot-editable prompts from child Action rows. See [`sprk_analysisaction.md`](sprk_analysisaction.md).
- **No maker-facing graph authoring, ever** — the PlaybookBuilder canvas is de-scoped (FR-P4-04); control flow is system-owned code, behavior (prompts/scopes/bindings) is maker-editable data.
- Do NOT create new `sprk_playbooknode` rows for new features, add executor types, or route new consumers to playbook targets. Legacy Binding rows still carrying `sprk_playbook` targets are the Insights family plus pre-redesign consumers pending migration.

This document records the table **AS-IS** for maintenance of the frozen Insights pipelines only. It is not an invitation to build on it.

---

## 1. Purpose (historical)

One row = one node in a playbook's execution graph (parent: `sprk_analysisplaybook`). The engine (`PlaybookOrchestrationService` + node executors) walks nodes in `sprk_executionorder`, dispatching each **directly on `sprk_executortype`** (R7 single-hop dispatch, task 024 — the Action lookup is a prompt-template carrier, not a dispatch axis).

---

## 2. Schema

### 2.1 Entity metadata

| Property | Value |
|---|---|
| **Logical name** | `sprk_playbooknode` |
| **Collection name** | `sprk_playbooknodes` |
| **Table description** | "Individual nodes within a playbook" |
| **Primary key** | `sprk_playbooknodeid` (GUID) |
| **Primary name column** | `sprk_name` (NVARCHAR(200), required) |
| **Ownership** | Organization (`organizationid`) |

### 2.2 Columns (live spaarkedev1 schema, 2026-07-07)

| Logical name | Type | Meaning |
|---|---|---|
| `sprk_playbooknodeid` | GUID (PK) | Row identity. |
| `sprk_name` | NVARCHAR(200), required | Node display name. |
| `sprk_playbookid` | Lookup → `sprk_analysisplaybook`, required | Parent playbook. |
| `sprk_executionorder` | Int, required | Walk order (ascending). |
| `sprk_executortype` | Choice | **The dispatch axis** (single-hop). Full live option set in §2.3. |
| `sprk_actionid` | Lookup → `sprk_analysisaction` | Prompt-template carrier for AI-executor nodes ({SystemPrompt + OutputSchema + Temperature}). |
| `sprk_outputvariable` | NVARCHAR(100), required | Variable name the node's output binds to for downstream nodes. |
| `sprk_configjson` | Memo (JSON) | Per-executor configuration (shape varies by executor type; e.g. DeliverComposite's `sectionName` map). |
| `sprk_conditionjson` | Memo (JSON) | Conditional-execution predicate. |
| `sprk_dependsonjson` | Memo (JSON) | Upstream node dependencies (parallel-branch joins). |
| `sprk_modeldeploymentid` | Lookup → `sprk_aimodeldeployment` | Per-node model-deployment pin. |
| `sprk_isactive` | Boolean | Node-level enable flag. |
| `sprk_retrycount` | Int | Retry policy. |
| `sprk_timeoutseconds` | Int | Node timeout. |
| `sprk_position_x` / `sprk_position_y` | Int | Canvas coordinates (PlaybookBuilder canvas — de-scoped per FR-P4-04; coordinates are vestigial). |
| Standard audit/system | — | `createdon`, `modifiedon`, `statecode`/`statuscode`, etc. |

### 2.3 `sprk_executortype` option set (live, as-is)

| Value | Label | | Value | Label |
|---|---|---|---|---|
| 0 | AI Analysis | | 40 | Deliver Output |
| 1 | AI Completion | | 41 | Deliver To Index |
| 2 | AI Embedding | | 42 | Deliver Composite |
| 10 | Rule Engine | | 50 | Create Notification |
| 11 | Calculation | | 51 | Query Dataverse |
| 12 | Data Transform | | 52 | Lookup User Membership |
| 20 | Create Task | | 60 | Agent Service |
| 21 | Send Email | | 70 | Grounding Verify |
| 22 | Update Record | | 80 | Live Fact |
| 23 | Call Webhook | | 90 | Index Retrieve |
| 24 | Send Teams Message | | 100 | Evidence Sufficiency |
| 30 | Condition | | 110 | Decline To Find |
| 31 | Parallel | | 120 | Return Insight Artifact |
| 32 | Wait | | 130 | Sanitization |
| 33 | Start | | 140 | Observation Emit |
| | | | 141 | Entity Name Validator |
| | | | 142 | Load Knowledge |
| | | | 143 | Return Response |

The values mirror the code enum `ExecutorType` in [`Services/Ai/Nodes/INodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs). **Option-set gap note**: a code↔Dataverse option-set gap on the node type/executor vocabulary was flagged during the redesign; its resolution (align or document permanently) is **tracked by FR-P4-02** (catalog governance — no task-051 note existed as of 2026-07-07). Per the freeze, the option set must NOT gain new values in any case.

### 2.4 Relationships

| Direction | Related entity | Via | Notes |
|---|---|---|---|
| Many-to-One | `sprk_analysisplaybook` | `sprk_playbookid` (required) | Parent graph. |
| Many-to-One | `sprk_analysisaction` | `sprk_actionid` | Prompt-template carrier. |
| Many-to-One | `sprk_aimodeldeployment` | `sprk_modeldeploymentid` | Model pin. |
| Many-to-Many | scopes | `sprk_playbooknode_skill` / `sprk_playbooknode_knowledge` / `sprk_playbooknode_tool` | Node-attached skills, knowledge, tools (managed by `NodeService` N:N operations). |

---

## 3. Consumers (frozen — Insights family only)

| Consumer | Role |
|---|---|
| [`Services/Ai/NodeService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs) | Node CRUD + N:N scope management (Web API). |
| `PlaybookOrchestrationService` + node executors | Walks the graph for the existing Insights pipelines (matter-health etc.). Reads `sprk_executortype` directly (single-hop). |
| `EngineOutputLedgerAdapter` (E-2) | Bridges engine outputs into the session ledger, reverse-resolving the real Binding via `GetBindingByPlaybookIdAsync`; unregistered playbooks degrade to the `{playbookId}@t{n}` / `engine-playbook` identity (ADR-040). |
| Insights honesty primitives | GroundingVerifier / EvidenceSufficiency / DeclineToFind executor types (70/100/110) — binding commitments preserved per canonical §5.10. |

Engine-shell deletions (task 044) removed the non-Insights callers; the `SessionDispatchOrchestrator` and agent loop never touch this table.

---

## 4. Related docs

| Doc | Topic |
|---|---|
| [`sprk_analysisaction.md`](sprk_analysisaction.md) | Action (execution unit) — where new work goes (`prompted`/`coded`) |
| [`sprk-playbookconsumer.md`](sprk-playbookconsumer.md) | Binding (invocation unit) — the single routing surface |
| [`sprk_analysistool.md`](sprk_analysistool.md) | Closed tool catalog |
| [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) §4.2/§4.2.1 | The freeze decision (OQ-2/D11) and the two live execution shapes |
