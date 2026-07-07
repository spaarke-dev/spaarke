# Data Model — `sprk_analysistool` (the closed tool catalog)

> **Last Updated**: 2026-07-07
> **Reviewed By**: spaarke-ai-architecture-redesign-r1 task 052 (FR-P4-03)
> **Status**: Current
> **Schema source**: live `spaarkedev1` describe (2026-07-07) cross-checked against shipped code (`AnalysisToolService.cs`, `ToolHandlerToAIFunctionAdapter.cs`, `RoutingConsumerTypeHealthCheck.cs`) and seed mirrors (`infra/dataverse/sprk_analysistool-*-row.json`)
> **Canonical architecture**: [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) §6.2 (tool manifest), §4.5 invariant 2

---

## 1. Purpose — one of the two closed catalogs

`sprk_analysistool` is the platform's **tool manifest**: one row per primitive tool the AI runtime may invoke (extractors, analyzers, Dataverse read/write primitives, workspace/tab actions, search, email draft). Together with Actions+Bindings it forms the **two closed catalogs** (ADR-039 invariant D6): *the LLM never invokes an unlisted tool; nothing dispatches to an uncataloged capability.*

Closure is enforced mechanically, not by convention:

- **Row ↔ handler bijection** — [`RoutingConsumerTypeHealthCheck`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/RoutingConsumerTypeHealthCheck.cs) (startup hosted service + `/healthz` health check) verifies every active row names exactly one handler registered in `IToolHandlerRegistry` (via `sprk_handlerclass`, the routing identity) AND every registered handler has exactly one row. Orphans/duplicates on either side = catalog drift = **Unhealthy** (a deploy gate, not a first-failed-request surprise).
- **Function-calling projection** — chat-available rows are wrapped by `ToolHandlerToAIFunctionAdapter`; the model can only call what projects.
- **Schema validation** — invalid `sprk_jsonschema` maps to null at the service layer (tool refused to the LLM) and invalid-by-OpenAI-subset schemas are excluded at projection with health **Degraded** naming the row (G-P3 round-1 H1 fix).

---

## 2. Schema

### 2.1 Entity metadata

| Property | Value |
|---|---|
| **Logical name** | `sprk_analysistool` |
| **Collection name** | `sprk_analysistools` |
| **Table description** | "Reusable AI tools (extractors, analyzers, generators)" |
| **Primary key** | `sprk_analysistoolid` (GUID) |
| **Primary name column** | `sprk_name` (NVARCHAR(200), required) — convention: `SYS-` prefix for system rows (the seed script's safety filter) |
| **Ownership** | User/Team (`ownerid` OWNER) |

### 2.2 Columns (live spaarkedev1 schema, 2026-07-07)

Identity + routing:

| Logical name | Type | Meaning |
|---|---|---|
| `sprk_analysistoolid` | GUID (PK) | Row identity. |
| `sprk_name` | NVARCHAR(200), required | Display name (`SYS-Dataverse Create Record`). |
| `sprk_toolcode` | NVARCHAR(100) | Stable seed/upsert code (`DATAVERSE-CREATE-RECORD`). Seed script upserts by `sprk_handlerclass` + `sprk_toolcode`. |
| `sprk_toolid` | NVARCHAR(100) | **Namespaced id** — the LLM-facing function name the loop projects (e.g. `dataverse.create_record`, local segment frozen to the GA Dataverse MCP tool name per ADR-039 / D10). |
| `sprk_namespace` | NVARCHAR(100) | Namespace segment (`dataverse`, `workspace`, …). |
| `sprk_handlerclass` | NVARCHAR(200) | **The routing identity**: names the registered `IToolHandler` class (e.g. `DataverseCreateRecordHandler`). Bijection-checked at startup (see §1). |
| `sprk_tooltypeid` | Lookup → `sprk_aitooltype` | Legacy display categorization; `HandlerClass` mapping wins when present. |

Model-facing contract (steering surfaces):

| Logical name | Type | Meaning |
|---|---|---|
| `sprk_description` | Memo | **Model-facing steering text** — becomes the function description the loop sees. Maker/operator-editable with zero deploy; production-tuned across G-P3 UAT rounds 2–3 (2026-07-07): the `dataverse.create_record` description gained recordId-required/resolve-first, numeric-choice-never-labels, omit-unresolvable, records-belong-to-caller, resolve-in-the-SAME-TURN-before-confirm, and the `sprk_document` creation ban — each fixing observed model misbehavior purely via catalog data. |
| `sprk_jsonschema` | Memo (JSON) | The tool's **argument JSON Schema** (function-calling parameters). Double-validated: Draft 2020-12 meta-schema at the service mapper (`AnalysisToolService.MapJsonSchema` — malformed → null + warning, tool not exposed) AND the stricter OpenAI function-parameters subset at projection (`OpenAiFunctionSchemaValidator` — invalid → that ONE tool excluded + health Degraded). |
| `sprk_outputschema` | Memo (JSON) | The tool's **result shape** contract (e.g. `{tool, tablename, recordId, path, columnsSet, columnCount}` for create_record) — documents/pins what the model receives back; mirrored in the seed JSON. |
| `sprk_configuration` | Memo (JSON) | Per-tool config defaults for the handler (reserved on most rows). |

Governance + gating (FR-P0-03 extension columns):

| Logical name | Type | Meaning |
|---|---|---|
| `sprk_sideeffectclass` | Choice | `Read` (100000000) \| `Write` (100000001) \| `Communicate` (100000002) \| `Pure` (100000003). **Drives the ONE confirmation gate**: `SideEffectGateAIFunction` suspends any `write`/`communicate` invocation into `PendingPlanManager` (the single pending store, D12) and resumes only on the user's dialog Confirm — BY THIS DECLARED CLASS, never by tool-name list (ADR-039 invariant 6). Handlers contain NO gating logic. Null (legacy rows) = no declared side effect (FR-P0-03 tolerance; mapping never throws). |
| `sprk_permissionscope` | NVARCHAR(200) | Declared execution-identity scope (e.g. `dataverse-user-context` = user-OBO: the user's own 403/404 surfaces; never app-identity escalation). |
| `sprk_budgetclass` | NVARCHAR(100) | Cost-budget class (`light`, …) consumed by cost controls (ADR-016). |
| `sprk_requiredcapability` | NVARCHAR(100) | Optional capability gate: tool only projects when the playbook/session declares the named capability (Wave 7b). Null = always available. |
| `sprk_availableincontexts` | Choice | `Playbook` (100000000) \| `Chat` (100000001) \| `Both` (100000002). Where the row may be invoked; null (legacy) = Playbook. `dataverse.*` tools are Chat-only. |
| `sprk_availableadhoc` | Boolean | Legacy ad-hoc flag. |
| `sprk_analysisid` | Lookup → `sprk_analysis` | Legacy association (pre-redesign pipeline). |
| `sprk_tags` | NVARCHAR(100) | Free-form tags. |

Standard audit/system columns as usual (`createdon`, `modifiedon`, `statecode`/`statuscode`, etc.).

### 2.3 Relationships

| Direction | Related entity | Via | Notes |
|---|---|---|---|
| Many-to-One | `sprk_aitooltype` | `sprk_tooltypeid` | Legacy categorization. |
| Many-to-One | `sprk_analysis` | `sprk_analysisid` | Legacy. |
| Many-to-Many | `sprk_playbooknode` | `sprk_playbooknode_tool` | Frozen-engine nodes attach tools (Insights only — see [`sprk_playbooknode.md`](sprk_playbooknode.md)). |

---

## 3. Seed mirrors + authoring workflow

Every system row has a JSON mirror at **`infra/dataverse/sprk_analysistool-{tool}-row.json`** (40+ files: `dataverse-create-record`, `dataverse-read-query`, `dataverse-describe`, `dataverse-search-data`, `dataverse-update-record`, `dataverse-delete-record`, `email-draft`, `send-workspace-artifact`, `web-search`, `entity-extractor`, `risk-detector`, …). The mirror is the author-first source: it carries the full row (`sprk_toolid`, `sprk_namespace`, `sprk_sideeffectclass`, `sprk_permissionscope`, `sprk_budgetclass`, `sprk_description`, `sprk_jsonschema`, `sprk_outputschema`) plus `_comment_*` provenance. `scripts/Seed-TypedHandlers.ps1` consumes them — idempotent UPSERT by `sprk_handlerclass` + `sprk_toolcode`, restricted to `sprk_name LIKE 'SYS-%'`.

When live-tuning a row on spaarkedev1 (UAT steering edits), the mirror MUST be updated to match — the round-2/3 description hardening is mirrored with a `_comment_description_history` note.

---

## 4. Consumers (shipped code)

| Consumer | What it reads |
|---|---|
| [`Services/Ai/AnalysisToolService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisToolService.cs) | CRUD + the catalog read (`ListToolsAsync`). Maps `sprk_jsonschema` (meta-schema validated → null on failure), `sprk_sideeffectclass`, `sprk_availableincontexts`, `sprk_requiredcapability` into the `AnalysisTool` DTO. |
| [`Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs) | Wraps row + handler as an `AIFunction` (Microsoft.Extensions.AI primitives only, NFR-04); three-layer schema validation; ADR-015 telemetry (identifiers only, never payloads). |
| `SprkChatAgentFactory.ResolveTools` | Projects chat-available rows into the loop's tool list; applies context/capability filters + OpenAI-subset validation. |
| `SideEffectGateAIFunction` + `PendingPlanManager` | The ONE confirmation gate keyed on `sprk_sideeffectclass ∈ {write, communicate}` (suspend → `action_confirmation` SSE → dialog Confirm → resume-once). |
| `RoutingConsumerTypeHealthCheck` | Row↔handler bijection (Unhealthy on drift) + `sprk_jsonschema` OpenAI-subset scan (Degraded naming the row). |
| Frozen engine nodes | Via the `sprk_playbooknode_tool` N:N (Insights pipelines only). |

---

## 5. Related docs

| Doc | Topic |
|---|---|
| [`sprk_analysisaction.md`](sprk_analysisaction.md) | The Action (execution unit) |
| [`sprk-playbookconsumer.md`](sprk-playbookconsumer.md) | The Binding (invocation unit) — capability-level `sprk_risk` complements tool-level `side_effect_class` |
| [`sprk_playbooknode.md`](sprk_playbooknode.md) | Frozen engine node table |
| `infra/dataverse/sprk_analysistool-*-row.json` | Seed mirrors (author-first) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/HandlerRegistrationConventions.md` | Handler-class routing-identity convention |
