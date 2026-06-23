# Playbook Architecture

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current
> **Purpose**: Technical reference for the Spaarke Playbook system — visual AI workflows, node execution, canvas data model, builder subsystem, scheduler
> **Parent**: [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) (Spaarke AI platform overview)

---

## Overview

Playbooks are the primary AI composition pattern in Spaarke — visual node-based workflows stored as Dataverse records. They define **what** AI operations to perform and in what order. The execution backend is pluggable (currently in-process, future: Microsoft Agent Framework).

The system has three major subsystems: the **Playbook Builder** (visual canvas for authoring), the **Execution Engine** (node graph orchestration with parallel batching), and the **Scheduler** (background service for notification-mode playbooks). All three share the same node type system and scope resolution infrastructure.

**Entity**: `sprk_analysisplaybook`
**Canvas field**: `sprk_canvaslayoutjson` (serialized JSON of nodes and edges)
**Builder**: `src/client/code-pages/PlaybookBuilder/` (React 18 Code Page)

---

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| PlaybookExecutionEngine | `Services/Ai/PlaybookExecutionEngine.cs` | Dual-mode entry point (Batch vs Conversational) |
| PlaybookOrchestrationService | `Services/Ai/PlaybookOrchestrationService.cs` | Node graph execution with parallel batching |
| PlaybookRunContext | `Services/Ai/PlaybookRunContext.cs` | Thread-safe state container for a single run |
| NodeExecutionContext | `Services/Ai/NodeExecutionContext.cs` | Per-node execution context (scopes, outputs, document) |
| NodeExecutorRegistry | `Services/Ai/Nodes/NodeExecutorRegistry.cs` | ActionType-to-INodeExecutor lookup |
| INodeExecutor | `Services/Ai/Nodes/INodeExecutor.cs` | Executor interface + NodeType/ActionType enums |
| TemplateEngine | `Services/Ai/TemplateEngine.cs` | Handlebars.NET rendering for variable substitution |
| PlaybookSchedulerService | `Services/PlaybookSchedulerService.cs` | Background scheduler for notification playbooks |
| BuilderAgentService | `Services/Ai/Builder/BuilderAgentService.cs` | AI-assisted playbook construction (agentic loop) |
| BuilderToolExecutor | `Services/Ai/Builder/BuilderToolExecutor.cs` | Executes builder tool calls into canvas operations |
| BuilderToolDefinitions | `Services/Ai/Builder/BuilderToolDefinitions.cs` | OpenAI function calling tool schemas |
| PlaybookBuilder (Code Page) | `src/client/code-pages/PlaybookBuilder/` | Visual canvas (React 18 + @xyflow/react v12) |
| PlaybookBuilderHost (PCF) | `src/client/pcf/PlaybookBuilderHost/` | Legacy field-bound control (React 16/17) |

All paths above are relative to `src/server/api/Sprk.Bff.Api/` unless noted otherwise.

---

## Three-Level Node Type System

Nodes use three type concepts at different layers:

| Level | Name | Where Stored | Purpose | Example |
|-------|------|-------------|---------|---------|
| **Canvas Type** | `PlaybookNodeType` | React Flow `node.data.type` | React component selection | `"aiAnalysis"` |
| **Dataverse NodeType** | `sprk_nodetype` | `sprk_playbooknode` OptionSet | Coarse scope resolution | `AIAnalysis (100000000)` |
| **ActionType** | `__actionType` in ConfigJson | `sprk_playbooknode.sprk_configjson` | Fine-grained executor dispatch | `AiAnalysis (0)` |

### Canvas Types (9 node types -- drag-and-drop palette items)

| Canvas Type | Dataverse NodeType | ActionType | Backend |
|------------|-------------------|------------|---------|
| `start` | -- | Start (33) | Always Spaarke code |
| `aiAnalysis` | AIAnalysis (100000000) | AiAnalysis (0) | Backend-flexible |
| `aiCompletion` | AIAnalysis (100000000) | AiCompletion (1) | Backend-flexible |
| `condition` | Control (100000002) | Condition (30) | Always Spaarke code |
| `deliverOutput` | Output (100000001) | DeliverOutput (40) | Always Spaarke code |
| `deliverToIndex` | Output (100000001) | DeliverToIndex (41) | Always Spaarke code |
| `updateRecord` | Workflow (100000003) | UpdateRecord (22) | Always Spaarke code |
| `createTask` | Workflow (100000003) | CreateTask (20) | Always Spaarke code |
| `sendEmail` | Workflow (100000003) | SendEmail (21) | Always Spaarke code |

### Dataverse NodeType (4 coarse categories for scope resolution)

| Value | Category | Scope Resolution |
|-------|----------|-----------------|
| `AIAnalysis = 100_000_000` | AI-powered | Full scopes (skills, knowledge, tools from N:N) |
| `Output = 100_000_001` | Delivery | No scopes -- assembles previous outputs |
| `Control = 100_000_002` | Flow control | No scopes -- condition/wait logic |
| `Workflow = 100_000_003` | Dataverse/email | No scopes -- Dataverse/Graph actions |

### ActionType Enum (18 values, 9 with executors)

The full enum includes reserved values for future use. Currently implemented executors cover 11 ActionTypes:

| Range | Category | Implemented | Reserved |
|-------|----------|------------|----------|
| 0-2 | AI | AiAnalysis (0), AiCompletion (1), AiEmbedding (2) | -- |
| 10-12 | Processing | -- | RuleEngine (10), Calculation (11), DataTransform (12) |
| 20-24 | Workflow | CreateTask (20), SendEmail (21), UpdateRecord (22) | CallWebhook (23), SendTeamsMessage (24) |
| 30-33 | Control | Condition (30), Start (33) | Parallel (31), Wait (32) |
| 40-41 | Output | DeliverOutput (40), DeliverToIndex (41) | -- |
| 50-51 | Data/Notify | CreateNotification (50), QueryDataverse (51) | -- |

**Key rule**: NodeType determines scope resolution strategy. ActionType determines which `INodeExecutor` runs. Both must stay in sync -- missing entries in the canvas-to-Dataverse mapping cause fallthrough to AIAnalysis, which triggers incorrect scope resolution and "requires an Action" errors.

**Mapping files** (must stay in sync):
- Client-side: `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts`
- Server-side: `src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs`

---

## Canvas-to-Dataverse Sync

**File**: `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts`

- Auto-save: 30-second debounce after canvas changes
- Manual save: Ctrl+S
- On save: Canvas JSON written to `sprk_canvaslayoutjson`, then `syncNodesToDataverse()`:
  1. Queries existing `sprk_playbooknode` records
  2. Computes execution order via Kahn's topological sort of canvas edges
  3. Creates/updates/deletes node records with `sprk_nodetype` + `__actionType` in ConfigJson
  4. Writes `sprk_dependsonjson` with upstream node GUIDs
  5. Manages N:N relationships (skills, knowledge, tools) via associate/disassociate

---

## Playbook Builder (Code Page)

**Path**: `src/client/code-pages/PlaybookBuilder/`
**Stack**: React 18, @xyflow/react v12, Fluent UI v9, Zustand
**Deployment**: Inline HTML web resource (`sprk_playbookbuilder.html`)

The builder provides a visual canvas where users drag-and-drop node types from the NodePalette, configure properties in the NodePropertiesDialog (per-node modal with ScopeSelector, ModelSelector, ActionSelector, and type-specific forms), connect nodes with edges (including conditional true/false branches), and can use the AI Assistant Modal for conversational playbook construction.

### AI Builder Subsystem

**Directory**: `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/`

The AI Assistant Modal delegates to the **BuilderAgentService**, which implements an agentic loop (max 10 tool-call rounds) using GPT-4o with function calling. The agent receives the current canvas state, available scopes, and user request, then manipulates the canvas through structured tool calls.

| Component | Responsibility |
|-----------|---------------|
| `BuilderAgentService` | Agentic conversation loop with function calling |
| `BuilderToolDefinitions` | 10 OpenAI tool schemas (canvas + scope operations) |
| `BuilderToolExecutor` | Converts tool calls into CanvasOperation patches |
| `BuilderScopeImporter` | Imports scope records for link/search/create operations |

**Builder Tools** (2 categories):
- **Canvas Operations**: `add_node`, `remove_node`, `create_edge`, `update_node_config`, `auto_layout`, `validate_canvas`, `configure_prompt_schema`
- **Scope Operations**: `link_scope`, `search_scopes`, `create_scope`

### PlaybookBuilderHost PCF Control (Legacy R4)

**Path**: `src/client/pcf/PlaybookBuilderHost/`
**Stack**: React 16/17, react-flow-renderer v10, Fluent UI v9
**Status**: Still maintained; mirrors Code Page structure. Used as field-bound control on `sprk_analysisplaybook` form.

---

## Execution Engine

### PlaybookExecutionEngine (Dual Mode)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs`

Top-level entry point supporting two execution modes:

| Mode | Method | Use Case |
|------|--------|----------|
| **Batch** | `ExecuteBatchAsync()` | Document analysis via node graph (delegates to PlaybookOrchestrationService) |
| **Conversational** | `ExecuteConversationalAsync()` | Builder UI interactions (delegates to IAiPlaybookBuilderService) |

Mode detection: `hasDocuments` -> Batch; `hasCanvasState && !hasDocuments` -> Conversational; default -> Conversational.

### PlaybookOrchestrationService (Node-Based Execution)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs`

Orchestrates node graph execution with parallel batching. Supports both interactive (OBO via HttpContext) and app-only (background) execution paths.

```
PlaybookRunRequest
  |
  v
Mode Detection: Legacy (no nodes) or NodeBased (has sprk_playbooknode records)
  |
  +-- Legacy -> delegates to IAnalysisOrchestrationService
  |
  +-- NodeBased:
      1. Build ExecutionGraph (DAG from DependsOn arrays, Kahn's algorithm)
      2. Topological sort -> execution batches (independent nodes grouped)
      3. FOR EACH batch (sequential between batches, parallel within):
         a. Resolve scopes per node based on NodeType
            - AIAnalysis -> full scopes (skills, knowledge, tools from N:N)
            - Output/Control/Workflow -> empty scopes (no LLM overhead)
         b. Determine ActionType from __actionType in ConfigJson
         c. Route to INodeExecutor via NodeExecutorRegistry[ActionType]
         d. Execute nodes in parallel (SemaphoreSlim throttle)
      4. Stream PlaybookStreamEvents per node (SSE via Channel)
      5. Store node outputs in PlaybookRunContext (ConcurrentDictionary)
      6. Template substitution: downstream nodes reference {{variable}} outputs
      7. Rate limit handling with exponential backoff (ADR-016)
```

### PlaybookRunContext (State Container)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookRunContext.cs`

Thread-safe state container for a single playbook execution. Two constructors: one for interactive runs (with HttpContext for OBO), one for app-only runs (with tenant ID for background execution). Uses `ConcurrentDictionary<string, NodeOutput>` for node output storage since nodes may complete in parallel within a batch.

Key state: `RunId`, `PlaybookId`, `DocumentIds`, `TenantId`, `Document` (shared DocumentContext), `NodeOutputs`, `State` (Pending/Running/Completed/Failed/Cancelled).

### Parallel Execution and Performance

**Key design decision**: Nodes with no declared dependencies run in parallel within the same batch. The performance formula is `Total time ~ SUM(slowest node in each batch)`.

**Throttle**: `SemaphoreSlim(DefaultMaxParallelNodes)` -- default 3, tuned to Azure OpenAI TPM/RPM quota. Rate limit retries: max 3 with exponential backoff starting at 2 seconds.

**Key rule**: Only add dependency edges where a node actually references `{{upstream.output}}` in its prompt. Unnecessary edges force sequential execution.

| Pattern | Structure | Batches | Performance |
|---------|-----------|---------|-------------|
| Fully sequential | A -> B -> C -> D -> Output | 5 | Slowest |
| Fully parallel | A,B,C,D -> Output | 2 | Fastest |
| Partial deps | A -> B; C,D -> Output | 3 | Middle ground |

---

## Node Executor Framework

**Directory**: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/`

Each ActionType maps to exactly one `INodeExecutor`. Executors register via DI (`IEnumerable<INodeExecutor>` constructor injection) and are indexed by `SupportedActionTypes` in `NodeExecutorRegistry`.

Every executor implements three methods: `SupportedActionTypes` (which ActionTypes it handles), `Validate()` (fail-fast before execution), and `ExecuteAsync()` (produce `NodeOutput`).

| File | ActionType(s) | Backend | Description |
|------|--------------|---------|-------------|
| `AiAnalysisNodeExecutor.cs` | AiAnalysis (0) | Backend-flexible | Bridges to IAnalysisToolHandler pipeline with L1/L2/L3 knowledge retrieval |
| `ConditionNodeExecutor.cs` | Condition (30) | Spaarke code | JSON condition expression evaluation (eq/ne/gt/lt/contains/and/or/not) |
| `CreateTaskNodeExecutor.cs` | CreateTask (20) | Spaarke code | Creates Dataverse task records via OData Web API |
| `UpdateRecordNodeExecutor.cs` | UpdateRecord (22) | Spaarke code | Writes AI-extracted values to Dataverse fields with typed coercion |
| `SendEmailNodeExecutor.cs` | SendEmail (21) | Spaarke code | Sends email via Microsoft Graph (OBO authentication) |
| `DeliverOutputNodeExecutor.cs` | DeliverOutput (40) | Spaarke code | Renders Handlebars template with all previous node outputs |
| `DeliverToIndexNodeExecutor.cs` | DeliverToIndex (41) | Spaarke code | Enqueues RAG semantic indexing job via Service Bus |
| `CreateNotificationNodeExecutor.cs` | CreateNotification (50) | Spaarke code | Creates Dataverse appnotification records with idempotency check |
| `QueryDataverseNodeExecutor.cs` | QueryDataverse (51) | Spaarke code | Executes FetchXML queries with date/user variable resolution |
| `LookupUserMembershipNodeExecutor.cs` | LookupUserMembership (52) | Spaarke code | Resolves caller's record memberships for a given entity type via `IMembershipResolverService` (in-process). See "Membership-aware playbook authoring (R3)" below. |

### AiAnalysisNodeExecutor (ActionType 0)

Bridges node-based orchestration to the existing `IAnalysisToolHandler` pipeline. Requires document context and a configured tool with a handler class. Performs 3-tier knowledge retrieval before LLM call:
- **L1**: `ReferenceRetrievalService` -- curated domain knowledge from `spaarke-rag-references`
- **L2**: `IRagService` -- similar customer docs from `spaarke-knowledge-index-v2` (optional)
- **L3**: `IRecordSearchService` -- entity metadata from `spaarke-records-index` (optional)

Supports per-token streaming via `IStreamingAnalysisToolHandler` and resolves `$choices` lookup references for constrained decoding.

### ConditionNodeExecutor (ActionType 30)

Evaluates JSON condition expressions against previous node outputs. Supports comparison operators (`eq`, `ne`, `gt`, `lt`, `gte`, `lte`, `contains`, `startsWith`, `endsWith`, `exists`) and logical operators (`and`, `or`, `not` with nesting). Returns `ConditionResult` with `SelectedBranch`.

### UpdateRecordNodeExecutor (ActionType 22)

Writes AI-extracted values to Dataverse fields with typed coercion. Two config formats: **new** (typed `fieldMappings` with `type`/`options`) and **legacy** (flat dictionary). Types: `string`, `choice` (case-insensitive options map), `boolean`, `number`. Uses OData PATCH (not SDK) via `IFieldMappingDataverseService`.

### DeliverToIndexNodeExecutor (ActionType 41)

Enqueues RAG indexing job via `JobSubmissionService` (Service Bus). Background `RagIndexingJobHandler` processes: extract -> chunk -> embed -> index. **Important**: `sprk_searchindexed = true` means enqueued, not completed.

### CreateNotificationNodeExecutor (ActionType 50)

Creates Dataverse `appnotification` records with **idempotency check** (skips if unread duplicate exists for same user + regarding + category). Supports **iterate-items mode** for batch notifications from upstream QueryDataverse results. Priority: Informational (100M), Important (200M, default), Urgent (300M).

### QueryDataverseNodeExecutor (ActionType 51)

Executes FetchXML via OData Web API with variable resolution: `{{todayUtc}}`, `{{dueSoonWindowUtc}}` (+7d), `{{timeWindowStartUtc}}` (-24h), `{{run.userId}}`. Replaces `operator="eq-userid"` with resolved user ID.

### LookupUserMembershipNodeExecutor (ActionType 52)

Added in R3 (Part 1 — user-record membership resolution). Resolves the executing user's record memberships for a given Dataverse entity type by calling `IMembershipResolverService` **in-process** (NOT an HTTP round-trip to `/api/users/me/memberships/{entityType}`). The resolver is the same one that backs the public membership endpoint, so node-driven results match endpoint-driven results exactly.

Config (`PlaybookNodeDto.ConfigJson`):
- `entityType` (required, string) — Dataverse logical entity name (e.g., `sprk_matter`).
- `roles` (optional, string[]) — case-insensitive role filter (e.g., `["owner", "assignedAttorney"]`). Empty/missing means all discovered roles.
- `includeRelated` (optional, bool, default false) — Phase 1D transitive expansion flag. **1-hop max** per Q3 owner clarification (multi-hop deliberately not supported).
- `outputVariable` (required) — lives on `PlaybookNodeDto` itself, NOT inside `ConfigJson`.

Output (`NodeOutput.StructuredData`):

```jsonc
{
  "entityType": "sprk_matter",
  "count": 47,
  "ids": ["...", "..."],                              // de-duplicated; ready for downstream FetchXML IN
  "byRole": { "owner": [...], "assignedAttorney": [...] },
  "continuationToken": null,
  "cacheExpiresAt": "2026-06-22T15:34:00Z"
}
```

DI pattern: Singleton executor + Scoped resolver via `IServiceScopeFactory.CreateScope()` per invocation (mirrors `AgentServiceNodeExecutor` / `AiAnalysisNodeExecutor`). User identity comes from `NodeExecutionContext.UserId` (set by `PlaybookSchedulerJob` for per-user scheduled runs) with a fallback scan of previous node outputs.

See [Membership-aware playbook authoring (R3)](#membership-aware-playbook-authoring-r3) for authoring patterns and worked examples.

### SendEmailNodeExecutor (ActionType 21)

Sends email via Microsoft Graph (OBO). Config: `to`, `cc`, `subject`, `body`, `isHtml` -- all template-enabled.

### CreateTaskNodeExecutor (ActionType 20)

Creates Dataverse task records via OData Web API. Config: `subject`, `description`, `regardingObjectId`, `ownerId`, `dueDate` -- all template-enabled.

---

## TemplateEngine

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs`

Renders Handlebars.NET templates against a context built from previous node outputs. Used by UpdateRecord, DeliverOutput, DeliverToIndex, Condition, CreateNotification, QueryDataverse, CreateTask, and SendEmail executors.

**Template syntax**:
```
{{output_aiAnalysis.text}}                   -> AI summary text
{{output_aiAnalysis.output.documentType}}    -> Structured JSON field
{{output_aiAnalysis.output.keyFindings}}     -> Array -> flattened to bullet list
{{document.id}}                              -> Document record ID
{{run.userId}}                               -> Executing user's systemuserid
```

Arrays are automatically flattened to `"- item1\n- item2"` bullet strings for Dataverse text fields.

### Handlebars helpers (R3 additions)

R3 registered two new helpers in `TemplateEngine.cs` to close gaps that surfaced during R2 UAT:

| Helper | Syntax | Behavior | Replaces |
|--------|--------|---------|----------|
| `default` | `{{default X 'Y'}}` | Returns `X` when it resolves to a non-empty value; otherwise renders `Y`. Handlebars.NET represents unresolved bindings as `UndefinedBindingResult` (NOT null), and the helper treats those as empty. | The unsupported `{{X ?? 'Y'}}` null-coalescing operator pattern that emitted literal text (see Pitfall G1). |
| `joinIds` | `{{joinIds varName.ids}}` | Converts an `IEnumerable` (List, array, `JsonElement` array) into a comma-separated string suitable for FetchXML `operator='in' value='...'` clauses. Returns empty string for null, unresolved bindings, scalars, or empty enumerables. Treats strings as scalars (NOT as `IEnumerable<char>`). | Hand-written `{{#each}}…{{/each}}` patterns that produced fragile CSV joins; pairs directly with `LookupUserMembershipNodeExecutor` output. |

Both helpers ship unconditionally — no feature flag. Implementation: `src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs` lines 57–73 (registration), 81–107 (`JoinIds`), 114–127 (`IsNonEmptyValue`). See R3 tasks 001 (default helper), 002 (joinIds helper), 050–052 (playbook migration).

### Runtime unrendered-template detection (R3 — FR-3H1.4)

After each node executes, `PlaybookOrchestrationService.cs` scans `NodeOutput` text/structured fields for literal `{{` substrings. When detected, it logs a structured warning AND emits a `PlaybookStreamEvent` of type `UnrenderedTemplateDetected` (visible to SSE consumers in real time). This lets authors discover broken variable references during a run rather than after downstream consumers misbehave. Non-fatal — node output is still delivered downstream.

See `PlaybookOrchestrationService.ScanForUnrenderedTemplatesAsync` (line 1363) and Pitfall G10.

---

## Membership-aware playbook authoring (R3)

> **Section status**: Added 2026-06-22 per R3 FR-1B + FR-3H2 + AC-Docs.
> **Audience**: Developers + architects designing playbooks that need to filter Dataverse data to "records the current user is associated with" (matters, opportunities, documents, events, …).
> **Maker-facing companion**: [`docs/guides/PLAYBOOK-AUTHOR-GUIDE.md`](../guides/PLAYBOOK-AUTHOR-GUIDE.md).

R3 introduced a coordinated set of capabilities that together let a playbook author answer "what are MY records of type T?" without writing fragile FetchXML against junction tables that may or may not exist. The five pieces compose:

1. The `LookupUserMembership` node executor (ActionType=52) — server-side resolution
2. The `joinIds` Handlebars helper — output binding for downstream FetchXML
3. The `default` Handlebars helper — graceful fallback for missing template parameters
4. PlaybookBuilder UI affordances — rename guard, branch picker, edge perf hint
5. The canvas↔server drift CI test — prevents the canvas/server mapping skew that caused G6

### 1. The `LookupUserMembership` node — what it does

`LookupUserMembershipNodeExecutor` (`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs`) implements ActionType=52 (declared in `INodeExecutor.cs` lines 137–142). At execution time it:

1. Reads `entityType`, `roles`, `includeRelated` from `ConfigJson`.
2. Resolves the caller's `systemuserid` from `NodeExecutionContext.UserId` (set by `PlaybookSchedulerJob` for per-user scheduled runs; fallback scans previous node outputs for a `userId` property).
3. Bridges the Singleton executor to the Scoped `IMembershipResolverService` via `IServiceScopeFactory.CreateScope()` per invocation (Pitfall G7 pattern).
4. Calls `resolver.ResolveAsync(userId, entityType, options, cancellationToken)` — the SAME entry point used by `GET /api/users/me/memberships/{entityType}`. Authors get identical results whether they call the endpoint from a UI or wire the node into a playbook.
5. Binds the response to the node's `OutputVariable` as `StructuredData`: `{ entityType, count, ids[], byRole{}, continuationToken, cacheExpiresAt }`.

The membership endpoint contract is documented in [`docs/guides/MEMBERSHIP-RESOLUTION-GUIDE.md`](../guides/MEMBERSHIP-RESOLUTION-GUIDE.md) (operator-facing) and ADR-034 (binding rules). The node executor adds NO new resolution logic — it is a thin adapter over the existing service.

### 2. The downstream FetchXML pattern — `{{joinIds X.ids}}`

The canonical use of `LookupUserMembership` is to feed a downstream `QueryDataverse` (or any FetchXML-bearing) node with an IN-clause built from the resolved IDs:

```xml
<fetch top="50">
  <entity name="sprk_document">
    <attribute name="sprk_name" />
    <filter type="and">
      <condition attribute="sprk_matter" operator="in" value="{{joinIds myMatters.ids}}" />
      <condition attribute="createdon" operator="last-x-hours" value="{{timeWindowHours}}" />
    </filter>
  </entity>
</fetch>
```

`joinIds` renders `myMatters.ids` (a `List<Guid>` from the `LookupUserMembership` output) as `"<guid>,<guid>,<guid>"` — exactly the shape FetchXML's `operator="in"` expects. Empty lists render as `""` so the IN clause becomes `value=""` (matches zero rows — fail-closed, which is what an author wants when "user has zero matters" means "no notifications").

Authors should NOT hand-roll `{{#each}}` joins for this — it duplicates `joinIds` behavior without the empty-handling and scalar-defensiveness.

### 3. The `default` helper — graceful fallback

When a playbook reads a template parameter that may be unset (e.g., a per-user preference column that hasn't been populated), wrap the reference in `default`:

```jsonc
"templateParameters": {
  "timeWindow": "{{default userPreferences.timeWindow '24h'}}"
}
```

This replaces the broken `{{userPreferences.timeWindow ?? '24h'}}` pattern that emitted raw literal text in R2 UAT (Pitfall G1). `default` correctly handles `UndefinedBindingResult` (the type Handlebars.NET returns for unresolved bindings) and treats empty strings as "missing."

### 4. PlaybookBuilder UI affordances (R3 H2)

The builder ships three new safety affordances. They follow the existing per-ActionType form pattern documented in `projects/spaarke-platform-foundations-r3/notes/playbookbuilder-pattern-research.md` (Q5 owner directive: do NOT invent new patterns).

#### a. OutputVariable rename guard (FR-3H2.1)

When a user changes a node's `OutputVariable` in `NodePropertiesForm.tsx` / `NodePropertiesDialog.tsx`, the canvas first scans every other node for `{{<oldName>.output.*}}` template references (reusing the existing `TEMPLATE_REF_RE` regex in `services/canvasValidation.ts`). If any are found, `RenameGuardDialog.tsx` opens with three actions:

- **Auto-rename references** (PRIMARY) — applies the new name AND find/replaces every downstream reference via `canvasStore.renameOutputVariableReferences(oldName, newName)`.
- **Keep old name** — reverts the field; downstream references unchanged.
- **Cancel rename** — same effect as Keep, presented as the escape hatch for users who reflexively look for "Cancel."

A complementary validation rule `outputvar-collision` (severity=error) fires when two nodes share the same non-empty OutputVariable — prevents the ambiguity-by-design state.

#### b. Branch wiring picker (FR-3H2.2)

When an edge is dragged from a Condition node body (no `sourceHandle`), `canvasStore.onConnect` opens `BranchPickerDialog.tsx` with three options:

- **True** — single edge, `sourceHandle='true'`, type=`trueBranch` (green).
- **False** — single edge, `sourceHandle='false'`, type=`falseBranch` (red).
- **Both** — TWO edges (one True + one False). The dialog deliberately does NOT invent a `bothBranch` edge type; reuses the existing renderers.

Labels for "True"/"False" come from the Condition node's `conditionJson.trueBranch` / `falseBranch` strings (managed by `ConditionEditor.tsx`), so authors who renamed the branches to "Approved" / "Rejected" see those labels in the picker.

#### c. Edge perf hint advisory (FR-3H2.3)

A new per-edge validation rule `edge-no-data-dependency` (severity=warning) scans every edge: if the target node's serialized config does NOT reference the source node's `outputVariable` via `{{<source.outputVariable>.output.*}}`, the rule emits an advisory warning attached to the source node's `NodeValidationBadge`:

> "Edge from \"X\" to \"Y\" does not reference {{X.output.*}} in the target's configuration. This edge forces sequential execution. Confirm or remove?"

**Advisory only** — save remains allowed. The author may have a legitimate side-effect-only sequencing need (the rule is intentionally non-blocking). See Pitfall G9.

Implementation: `services/canvasValidation.ts` lines 207–212 (per-edge loop), 636–669 (`validateEdgePerfHint`). Reuses `parseDownstreamNode` so the scan covers the same field set as the other rules (configJson fieldMappings, legacy fields dict, template/body/subject/description, node.data template/emailBody/emailSubject).

### 5. The canvas↔server drift CI test (FR-3H3.1)

`tests/integration/Sprk.Bff.Api.IntegrationTests/Playbooks/CanvasServerMappingDriftTests.cs` is a parse-the-source xUnit test that runs on every CI build. It asserts two invariants:

1. Every canvas node type emitted by the `buildConfigJson()` switch in `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts` MUST have a matching arm in `MapCanvasTypeToActionType()` in `src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs` — otherwise authoring a playbook with that node type persists a record the server cannot dispatch (Pitfall G6).
2. Every `ActionType` slot referenced by the client's `NodeTypeToActionType` lookup MUST exist as a named member of the server-side `ActionType` enum in `INodeExecutor.cs`.

The test uses regex (no Roslyn, no Node subprocess) so it runs on every CI image without extra tooling. To verify locally: `dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/ --filter "FullyQualifiedName~CanvasServerMappingDriftTests"`. Failure messages name the missing entry exactly — repair is mechanical. See the test file's header comment for the "intentional drift" verification procedure.

### Worked examples — migrated R3 notification playbooks

Three notification playbooks were migrated as part of R3 to exercise the new pattern end-to-end. All three live at `projects/spaarke-daily-update-service/notes/playbooks/`:

| Playbook | Before (R2 defect) | After (R3) |
|----------|---------------------|------------|
| `notification-new-documents.json` | FetchXML joined through `sprk_matterteammember` — an entity that does not exist in production Dataverse; returned zero rows silently (R3 FR-1C.1 / A1 defect) | `LookupUserMembership` node resolves `sprk_matter` memberships for roles `owner`, `assignedAttorney`, `assignedParalegal`; downstream `QueryDataverse` filters via `{{joinIds myMatters.ids}}` |
| `notification-new-emails.json` | Query had NO user-membership filter at all — would have returned inbound emails on every matter in the tenant (R3 FR-1C.2 / A1 defect) | Same `LookupUserMembership` node; `regardingobjectid` filtered by `{{joinIds myMatters.ids}}` |
| `notification-new-events.json` | Same defect class as emails | Same fix |

The "before" defects were silent failures — the playbook ran to completion and produced empty notifications, so operators saw "system working, no new items." This is precisely the failure mode the membership endpoint + node + drift CI test together prevent.

### Pattern doc cross-reference

Developers adding **new** node types (not just configuring existing ones) should read `.claude/patterns/ai/node-executor-authoring.md` (R3 FR-3H3.5). It documents the Singleton-executor-depends-on-Scoped-service pattern, the registry plumbing, the validation contract, and the canvas↔server mapping checklist that the drift CI test enforces. `LookupUserMembershipNodeExecutor` and `AgentServiceNodeExecutor` are the canonical worked examples.

---

## Dual Output Paths

| Path | Data | Storage | Display |
|------|------|---------|---------|
| **Analysis Output** | `ToolResult.Summary` (text) | `sprk_analysisoutput.sprk_output_rtf` | AiSummaryPanel |
| **Document Fields** | AI structured output (JSON) | `sprk_document.*` fields via UpdateRecord node | Document form fields |

Document field writes happen **during** playbook execution (UpdateRecord node), not after. There is no post-execution field mapping step.

---

## Playbook Scheduler

**File**: `src/server/api/Sprk.Bff.Api/Services/PlaybookSchedulerService.cs`

Background service (`BackgroundService` per ADR-001) that periodically executes notification-mode playbooks. Queries `sprk_analysisplaybook` where `sprk_playbooktype = Notification (2)`, reads schedule config from `sprk_configjson`, and processes users in parallel.

| Setting | Value |
|---------|-------|
| Tick interval | 1 hour (default) |
| Max parallel users per playbook | 5 (`Parallel.ForEachAsync`) |
| Error retry delay | 5 minutes |
| Last-run tracking | In-memory `ConcurrentDictionary`, seeded from Dataverse `sprk_lastrundate` |
| Execution mode | App-only (no HttpContext) via `ExecuteAppOnlyAsync()` |

The scheduler sets `UserId` on `NodeExecutionContext` so that QueryDataverse and CreateNotification nodes can resolve per-user data.

---

## Consumer Integration Pattern

```
+-------------------+     SSE stream      +------------------+     OData PATCH     +----------+
|  PCF / CodePage   | ------------------>  |  BFF API         | -----------------> | Dataverse|
|  (useAiSummary)   | <-- progress events  |  (Playbook exec) |   (UpdateRecord)   | (fields) |
+-------------------+                      +------------------+                    +----------+
```

**Rules**:
1. Component triggers playbook via SSE endpoint and displays streaming progress
2. All Dataverse field writes are server-side (UpdateRecord node via OData PATCH)
3. Component does NOT write AI output fields -- no client-side callbacks to Dataverse
4. Component owns its own UX -- file upload, metadata entry, progress display, error handling

---

## Playbooks as "Frontend"

Playbooks define **what** to do. The execution backend is pluggable:

```
Playbook (Spaarke Canvas)
+-- AI Nodes -> Backend-flexible:
|   +-- Option A: In-Process (current) -- PlaybookOrchestrationService
|   +-- Option B: Agent Framework Workflow (future) -- IChatClient/AIAgent
|   +-- Option C: AI Foundry Agent Service (future) -- Published agents
+-- Workflow Nodes -> Always Spaarke code:
|   +-- CreateTask, SendEmail, UpdateRecord
|   +-- DeliverToIndex (RAG semantic indexing via Service Bus)
|   +-- Condition (branch evaluation)
|   +-- DeliverOutput (output assembly)
+-- Data/Notify Nodes -> Always Spaarke code:
    +-- QueryDataverse (FetchXML queries)
    +-- CreateNotification (appnotification with idempotency)
```

A single playbook can mix backends per node. This is not an either/or decision.

---

## Nodes as Agents (Architectural Evolution)

Each playbook node maps conceptually to an AI agent: Node -> `AIAgent`, Action -> agent instructions, Skills -> behavioral modifiers, Knowledge -> grounding context, Tool -> `AIFunctionFactory.Create()`, edges -> graph-based workflow. The PlaybookOrchestrationService evolves toward a "workflow compiler" that translates canvas definitions into Agent Framework workflows. The scope library stays as Spaarke IP; the execution engine becomes a translation layer.

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | AI Tool Handlers | `IToolHandlerRegistry` / `IAnalysisToolHandler` | AiAnalysisNodeExecutor bridges to existing tool pipeline |
| Depends on | Scope Resolution | `IScopeResolverService` | Resolves skills, knowledge, tools from N:N relationships |
| Depends on | RAG Services | `ReferenceRetrievalService`, `IRagService`, `IRecordSearchService` | L1/L2/L3 knowledge retrieval |
| Depends on | Job Queue | `JobSubmissionService` (Azure Service Bus) | DeliverToIndex enqueues indexing jobs |
| Depends on | Microsoft Graph | `IGraphClientFactory` | SendEmail executor uses OBO |
| Depends on | Dataverse Web API | `IHttpClientFactory("DataverseApi")` | UpdateRecord, CreateTask, CreateNotification, QueryDataverse |
| Consumed by | PCF Controls | SSE streaming endpoints | `useAiSummary` hook triggers and displays playbook runs |
| Consumed by | Code Pages | SSE streaming endpoints | Document viewers, workspace pages |
| Consumed by | Agent Endpoints | `PlaybookInvocationService` | Agent Framework invokes playbooks |
| Consumed by | Background Services | `PlaybookSchedulerService` | Notification playbooks on schedule |

---

## Known Pitfalls

> **Status Legend** (refreshed 2026-06-22 per R3 spec FR-3G4 + AC-Docs):
> - **Fixed (R3)** — Engineered fix landed in `spaarke-platform-foundations-r3`. Original description retained for context.
> - **Documented (R3)** — Behavior is intentional but surprising; documented explicitly so authors can plan around it.
> - **Deferred to R4** — Out of R3 scope; tracked in successor project.
>
> Pitfall numbering follows the canonical G1-G11 catalog from `projects/spaarke-platform-foundations-r3/design.md` §Part 3. G5 is Part-1 (membership) and listed here for completeness.

### G1 — Handlebars `??` operator not supported; renders as literal text

The Handlebars.NET engine does not support the JavaScript-style `{{X ?? 'Y'}}` null-coalescing operator. When authors wrote this pattern in playbook JSON (carried over from PCF muscle memory), the engine emitted the raw literal text instead of falling back to the default — a silent breakage mode affecting 2 of 7 active notification playbooks (detected during R2 UAT 2026-06-20).

**Fixed (R3)**: New `{{default X 'Y'}}` Handlebars helper registered in `TemplateEngine.cs` (FR-3H1.1); 3 known-broken playbooks migrated from `??` to `default` helper (FR-3H1.3); integration tests assert rendered output (task 053). See: tasks 003 + 004 + 050 + 051 + 052 + 053.

### G2 — Renaming a node's `OutputVariable` silently breaks downstream `{{x.output}}` references

In the Builder UI, changing a node's `OutputVariable` did not propagate to other nodes that reference `{{<oldName>.output*}}` in their prompts/configs. Downstream nodes would render the raw `{{...}}` literal at runtime.

**Fixed (R3)**: PlaybookBuilder UI `OutputVariable` rename guard added (FR-3H2.1) — scans all other nodes for `{{<oldName>.output*}}` references via existing `VariableReferencePanel.tsx` infrastructure; presents dialog: (a) Auto-rename + find/replace all references [default], (b) Keep old name, (c) Continue and break. New validation rule in `services/canvasValidation.ts`. See: tasks 091 + 094.

### G3 — Condition node's `selectedBranch` only skips downstream nodes with explicit branch metadata

Without `DependsOn` branch metadata wired (`true` / `false` / `both`), all downstream nodes execute regardless of the Condition node's result, defeating the conditional intent.

**Fixed (R3)**: PlaybookBuilder Branch Wiring auto-generation (FR-3H2.2) — when an edge connects a Condition node (handled via existing `ConditionEditor.tsx`) to a downstream node, the UI prompts for branch and persists in `DependsOn` branch metadata; edges visualize differently per branch. See: tasks 092 + 094.

### G4 — `CreateNotification` idempotency dedupes per UNREAD only (intentional, surprising)

The `CreateNotificationNodeExecutor` checks for an existing unread notification with the same `sprk_notificationkey` for the same user before creating; if one is found, it is updated rather than inserted. **However**, once a user reads/dismisses the notification, subsequent scheduled runs of the same playbook will create a fresh notification (the prior one is no longer "unread" and therefore not matched).

This is **intentional**: it lets recurring schedules re-surface a notification after a user marks it read, which is desirable for daily-update-style playbooks. But it is surprising to authors who expect "once per key, ever" semantics.

**Documented (R3)** — no behavior change. The complementary mechanism that prevents *runtime* duplicate playbook execution on the same scheduled tick lives in `ScheduledJobHost` + `IBackgroundJobStore.HasRunForScheduledTimeAsync` (`src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs`, task 014) — this guards retries-on-restart and tick-coalescing, not the per-user notification dedupe described above. The two layers are independent. See: task 014 (host-level idempotency), `CreateNotificationNodeExecutor.cs` (unread-key dedupe).

### G5 — `sprk_matterteammember` membership resolution (Part 1)

Out-of-band of Playbook engine but listed for catalog completeness. Identity normalization + membership resolution previously had no canonical service; user-record-based playbook lookups (e.g., `LookupUserMembership` node) had ad-hoc patterns.

**Fixed (R3)**: New `MembershipResolverService` + identity normalization + organization mapping + `/api/membership/{userId}` endpoint + `LookupUserMembership` node executor (ActionType = 52). Pattern captured in ADR-034. See: tasks 030 + 031 + 032 + 033 + 034 + 035 + 037.

### G6 — Canvas-to-Dataverse mapping drift

If a new canvas node type is added to the builder without updating both `playbookNodeSync.ts` (client) and `NodeService.cs` (server), the node falls through to `AIAnalysis` default, causing scope resolution errors and misleading "requires an Action" messages.

**Fixed (R3)**: Canvas-server mapping drift integration test added in `tests/integration/PlaybookBuilder.Tests/` (FR-3H3.1) — asserts every canvas type in `playbookNodeSync.ts` has a corresponding entry in `NodeService.cs`; fails CI build on drift. See: task 065.

### G7 — `AiAnalysisNodeExecutor` Singleton-with-Scoped-dependency pattern

The executor uses `IServiceProvider.CreateScope()` per execution to resolve the scoped `IToolHandlerRegistry`. Forgetting this pattern when adding new executors that depend on scoped services will cause DI resolution failures (runtime "cannot resolve scoped service from root provider" errors).

**Fixed (R3) — documentation**: Node-executor authoring pattern doc created at `.claude/patterns/ai/node-executor-authoring.md` (FR-3H3.5) documenting the Singleton-executor-depends-on-Scoped-service pattern; `AiAnalysisNodeExecutor` cited as worked example. See: task 066. (Sub-mechanism related: dual-write schema migration in tasks 060 + 061 + 062.)

### G8 — `sprk_searchindexed = true` means "enqueued", not "indexing completed"

The boolean flag was misleading because it was set when the indexing job was enqueued (before AI Search confirmation), causing operational confusion when operators checked the flag and assumed the document was searchable.

**Fixed (R3)**: Schema migration in progress (FR-3H3.2) — renamed `sprk_searchindexed` (bool) → `sprk_searchindexqueuedon` (datetime) and added `sprk_searchindexcompletedon` (datetime). `DeliverToIndexNodeExecutor.cs` writes both fields; legacy `sprk_searchindexed` maintained as dual-write during consumer migration (FR-3H3.4). To verify actual index presence operators may still query Azure AI Search directly. See: tasks 060 (consumer inventory) + 061 (schema migration) + 062 (dual-write executor).

### G9 — Unnecessary dependency edges kill parallelism

Every edge between nodes forces sequential execution. Only add edges where a node actually references `{{upstream.output}}` in its prompt or config. Audit playbooks with unexpectedly slow execution for unnecessary edges.

**Fixed (R3)**: Edge perf hint added in PlaybookBuilder UI (FR-3H2.3) — when an edge connects two nodes whose configs don't reference each other's `OutputVariable`, a non-blocking advisory warning displays via `NodeValidationBadge.tsx`: "This edge forces sequential execution. Confirm or remove?" Validation rule added to `services/canvasValidation.ts`. See: tasks 093 + 094.

### G10 — Template variable resolution timing (raw `{{x}}` leakage)

Template variables resolve at execution time using outputs from completed nodes. If a referenced variable's node failed or was skipped, the template renders the raw `{{variable}}` string. Condition nodes should guard downstream template usage.

**Fixed (R3)**: Runtime unrendered-template detection added (FR-3H1.4) — after each node executes, `PlaybookOrchestrationService.cs` scans `NodeOutput` string fields for literal `{{`; if found, logs structured warning AND emits `PlaybookStreamEvent` with type `unrendered-template-detected`. Authors can react in real time instead of discovering breakage in downstream consumers. See: task 005.

### G11 — Scheduler activates new notification playbooks for ALL users on deploy

The opt-out model means a new notification playbook is immediately active for every user. There is no per-user opt-in -- plan accordingly when deploying new notification playbooks.

**Deferred to R4**: Playbook rollout-mode (Disabled / PilotUsers / AllUsers) is reserved for the successor project `spaarke-playbook-rollout-mode-r4` + **ADR-035 (reserved)**. Out of R3 scope per design.md decision 2026-06-20. Until then, treat any new notification playbook deploy as an all-user activation.

---

### R4 Follow-up

The following pitfalls are explicitly **deferred** out of R3 and tracked for the successor project:

| Pitfall | Tracking |
|---------|----------|
| G11 (scheduler rollout-mode) — also referenced internally as **H4** | `spaarke-playbook-rollout-mode-r4` (planned); ADR-035 (reserved) |

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Execution engine location | In-process BackgroundService | No Azure Functions per ADR-001; keeps deployment simple | ADR-001 |
| Node executor dispatch | Registry pattern with ActionType enum | DI minimalism per ADR-010; fast lookup, extensible | ADR-010 |
| AI architecture | Tool framework extending BFF | No separate AI service per ADR-013 | ADR-013 |
| Rate limiting | Exponential backoff with SemaphoreSlim throttle | Azure OpenAI quota management | ADR-016 |
| Template engine | Handlebars.NET | Familiar syntax, supports nested properties, array flattening | -- |
| Dual execution mode | Batch + Conversational in single engine | Builder needs interactive; analysis needs streaming | -- |

---

## Constraints

- **MUST** keep client-side and server-side node type mappings in sync (`playbookNodeSync.ts` and `NodeService.cs`)
- **MUST** use OData Web API (not Dataverse SDK) for UpdateRecord, CreateTask, CreateNotification, and QueryDataverse to avoid serialization issues
- **MUST NOT** add dependency edges between nodes unless one node's output is actually referenced by the other
- **MUST** use `IServiceProvider.CreateScope()` in Singleton executors when resolving Scoped services
- **MUST** keep Workflow/Control/Output nodes free of scope resolution overhead (no LLM calls)
- **MUST NOT** exceed `DefaultMaxParallelNodes` (3) without validating against Azure OpenAI TPM/RPM quota

---

## Related

- [Pattern: analysis scopes](../../.claude/patterns/ai/analysis-scopes.md) -- scope resolution code entry points
- [Pattern: streaming endpoints](../../.claude/patterns/ai/streaming-endpoints.md) -- SSE streaming patterns
- [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) -- Minimal API + BackgroundService (no Azure Functions)
- [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) -- DI minimalism
- [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) -- AI Architecture (tool framework)
- [AI Architecture](AI-ARCHITECTURE.md) -- parent document covering full AI platform

---

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2026-06-22 | 2.2 | Added "Membership-aware playbook authoring (R3)" major section + `LookupUserMembershipNodeExecutor` row in Node Executor Framework + `default` / `joinIds` Handlebars helper subsection + runtime unrendered-template detection subsection. Cross-references to maker guide (`PLAYBOOK-AUTHOR-GUIDE.md`), pattern doc (`node-executor-authoring.md`), and 3 migrated playbooks. Per R3 FR-1B + FR-3H2 + AC-Docs. |
| 2026-06-22 | 2.1 | Refreshed Known Pitfalls section per R3 FR-3G4 + AC-Docs: restructured to canonical G1-G11 catalog, added Fixed-(R3) attribution for G1/G2/G3/G5/G6/G7/G8/G9/G10, added explicit G4 doc (idempotency-on-unread semantics), marked G11/H4 deferred to R4 (ADR-035 reserved). Task 102. |
| 2026-04-05 | 2.0 | Restored depth: execution engine dual mode, all 9 node executors (including CreateNotification/QueryDataverse), builder subsystem, scheduler, integration points, known pitfalls, constraints |
| 2026-03-13 | 1.0 | Initial version -- extracted from AI-ARCHITECTURE.md (v3.3). Added DeliverToIndex node documentation. |
