# Data Model — `sprk_analysisaction` (Action — the execution unit)

> **Last Updated**: 2026-07-07
> **Reviewed By**: spaarke-ai-architecture-redesign-r1 task 052 (FR-P4-03)
> **Status**: Current
> **Schema source**: live `spaarkedev1` describe (2026-07-07) cross-checked against shipped code (`AnalysisActionService.cs`, `ConsumerRoutingService.cs`, `Binding.cs`)
> **Canonical architecture**: [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) §6.1

---

## 1. Purpose

The **Action** is the platform's **execution unit**: one row defines *what runs* when a capability is invoked — either a JPS prompt executed as one structured-output LLM call (`kind = prompted`, the overwhelming majority) or a registered C# composite workflow (`kind = coded`). It carries the canonical `{SystemPrompt + OutputSchema + Temperature}` prompt-template triple plus the typed-argument contract (`sprk_inputschema`) the agent loop and Click path use to collect arguments.

An Action never routes itself. **Invocation is owned entirely by the Binding table** (`sprk_playbookconsumer`, see [`sprk-playbookconsumer.md`](sprk-playbookconsumer.md)): one Action may be targeted by many Bindings (one-Action-many-Bindings, canonical §6). Since R7 single-hop dispatch, the Action is *not* a dispatch axis — the pre-R7 dispatch-identity columns were dropped in R7 Wave 4 (tasks 043/044, 2026-06-29).

---

## 2. Schema

### 2.1 Entity metadata

| Property | Value |
|---|---|
| **Logical name** | `sprk_analysisaction` |
| **Collection name** | `sprk_analysisactions` |
| **Table description** | "Defines what the AI should do (e.g. Summarize, Review Agreement)" |
| **Primary key** | `sprk_analysisactionid` (GUID) |
| **Primary name column** | `sprk_name` (NVARCHAR(200), required) |
| **Ownership** | User/Team (`ownerid` OWNER + owning business unit/team/user) |
| **State** | Standard `statecode` Active(0)/Inactive(1), `statuscode` Active(1)/Inactive(2) |

### 2.2 Columns (live spaarkedev1 schema, 2026-07-07)

| Logical name | Type | Meaning |
|---|---|---|
| `sprk_analysisactionid` | GUID (PK) | Row identity. Referenced by `sprk_playbookconsumer.sprk_action` and `sprk_playbooknode.sprk_actionid`. |
| `sprk_name` | NVARCHAR(200), required | Display name (primary name column). |
| `sprk_actioncode` | NVARCHAR(64) | Stable string code, unique by convention (e.g. `SUM-CHAT@v1`, `CREATE-TASK@v1`, `BRIEF-NARRATE-TLDR`). Resolved by `AnalysisActionService.GetActionByCodeAsync` (OData filter, `$top=1`) so coded workflows can reference child prompt Actions by code instead of GUID. |
| `sprk_description` | Memo | Human/provenance description. NOT the loop-facing intent surface — that is the Binding's `sprk_tooldescription`. |
| `sprk_kind` | Choice | **`Prompted` (100000000, default)** \| **`Coded` (100000001)**. The execution shape (FR-P0-03 / canonical §4.2). Null on legacy rows → treated as `Prompted` by `ConsumerRoutingService.MapBinding`. |
| `sprk_workflowclass` | NVARCHAR(200) | For `coded` only: registered `ICodedWorkflow` class reference resolved via `ICodedWorkflowRegistry` (e.g. `DailyBriefingNarrator`). Null for prompted Actions. |
| `sprk_inputschema` | Memo (JSON) | Typed-argument contract projected as the capability tool's `function.parameters`. **See §3 — hard authoring rules apply.** |
| `sprk_systemprompt` | Memo | The JPS system prompt (prompted kind). Hot-editable by makers; single-sourced here. |
| `sprk_outputschemajson` | Memo (JSON) | Structured-Outputs JSON Schema — the constrained-decoding schema arg to `IOpenAiClient.GetStructuredCompletionRawAsync` (R7 FR-12). Repo mirrors: `infra/dataverse/outputschemas/*.schema.json`. |
| `sprk_outputformat` | Choice | `JSON` (0) \| `Markdown` (1) \| `PlainText` (2). |
| `sprk_temperature` | Decimal | Per-Action temperature override (0.0–2.0). Null = deterministic 0.0 downstream. |
| `sprk_modeltier` | Choice | `Fast` (100000000) \| `Standard` (100000001) \| `Reasoning` (100000002). The Action's **default** model tier; a Binding's `sprk_modeltieroverride` wins when set (`Binding.EffectiveModelTier`). Tier→deployment mapping stays in code/config per ADR-016. |
| `sprk_modeldeploymentid` | Lookup → `sprk_aimodeldeployment` | Legacy explicit model-deployment pin (pre-tier). Tier columns are the current mechanism. |
| `sprk_sortorder` | Int | Display ordering in maker surfaces. Note: the service-layer DTO currently defaults `SortOrder` to 0 on reads (the pre-R7 lookup-derived source was dropped; full SortOrder reform out of R7 scope). |
| `sprk_tags` | NVARCHAR(1000) | Free-form tags. |
| `sprk_actionid` | NVARCHAR(100) | Legacy string identifier column. No reader in shipped BFF code (verified 2026-07-07); retained on schema only. |
| `sprk_analysisid` | Lookup → `sprk_analysis` | Legacy association to the analysis record (pre-redesign pipeline). |
| `sprk_allowsdelivery` / `sprk_allowsknowledge` / `sprk_allowsskills` / `sprk_allowstools` | Boolean | Legacy JPS/playbook-builder authoring toggles (scope attachment allowances). |
| `sprk_availableadhoc` | Boolean | Legacy ad-hoc availability flag (pre-redesign). |
| Standard audit/system | — | `createdon`, `modifiedon`, `createdby`, `modifiedby`, `overriddencreatedon`, `importsequencenumber`, `versionnumber`, timezone columns. |

### 2.3 Relationships

| Direction | Related entity | Via | Notes |
|---|---|---|---|
| One-to-Many | `sprk_playbookconsumer` (Binding) | `sprk_playbookconsumer.sprk_action` | **One Action, many Bindings.** `ConsumerRoutingService` left-outer-joins this table to project the §6.1 execution fields (`sprk_kind`, `sprk_workflowclass`, `sprk_inputschema`, `sprk_modeltier`) onto every resolved `Binding` record. |
| One-to-Many | `sprk_playbooknode` (frozen engine) | `sprk_playbooknode.sprk_actionid` | Frozen-engine nodes reference an Action as their prompt-template carrier. Insights family only — see [`sprk_playbooknode.md`](sprk_playbooknode.md). |
| Many-to-One | `sprk_analysis` | `sprk_analysisid` | Legacy. |
| Many-to-One | `sprk_aimodeldeployment` | `sprk_modeldeploymentid` | Legacy explicit deployment pin. |

---

## 3. JSON field contract — `sprk_inputschema`

`sprk_inputschema` is an **OpenAI function-parameters JSON Schema** document. When a Binding targeting this Action is projected into the agent loop (`BindingCapabilityTool`), this column becomes the tool's `function.parameters` verbatim; the Click path and loop elicitation (FR-P2-03) read the same contract to know which args are required and how to ask for them.

### 3.1 HARD RULE — object-level `required` array ONLY

> ⛔ **Property-level `"required": true|false` inside a property definition is INVALID JSON Schema and is BANNED.** Azure OpenAI validates every keyword in every tool schema on every request and **rejects the ENTIRE request (HTTP 400 `invalid_function_parameters`) if ANY one schema is invalid** — during G-P3 UAT round 1 (2026-07-07), one bad `CREATE-TASK@v1` row 400'd EVERY text-path loop turn platform-wide (see `projects/spaarke-ai-architecture-redesign-r1/notes/g-p3-uat-round1-findings.md` finding 1). Required-ness goes ONLY in the object-level `required` array.

```json
{
  "type": "object",
  "properties": {
    "due_date": {
      "type": "string",
      "description": "Task due date (ISO 8601).",
      "elicitation_prompt": "When should this task be due?"
    },
    "assign_to": {
      "type": "string",
      "description": "Assignee.",
      "elicitation_prompt": "Who should this be assigned to?"
    }
  },
  "required": ["due_date", "assign_to"]
}
```

### 3.2 Allowed custom keywords

| Keyword | Meaning |
|---|---|
| `elicitation_prompt` | Maker-authored clarifying question the loop asks when the required arg is missing (FR-P2-03 loop elicitation). |
| `ledger_resolution` | Declares how the arg resolves from the session ledger / system (e.g. "system-supplied — DailyBriefingCollector…; never user-elicited"). System-supplied args must **NOT** appear in the `required` array, or elicitation would ask the USER for them. |

The legacy `{"args":[...]}` format is dead — all live rows were normalized to proper JSON Schema in the G-P3 round-1 fix wave (hard data cutover, no code compat branch).

### 3.3 Authoring workflow + validation net

1. **Author-mirror-first**: write the schema in `infra/dataverse/inputschemas/{action-code}.input.schema.json` (canonical CI-validated mirrors of all rows).
2. CI validates every mirror via `CatalogInputSchemaContractTests` + `OpenAiFunctionSchemaValidator` (the pragmatic OpenAI function-parameters subset walk; property-level boolean `required` explicitly banned; the exact UAT payload pinned invalid forever).
3. Then write the schema to the Dataverse row.
4. **Runtime resilience**: `SprkChatAgentFactory` / `ToolHandlerToAIFunctionAdapter` validate at projection time — an invalid schema excludes THAT ONE tool (Error log `[invalid-tool-schema]` + `ai.tool.schema_invalid` telemetry) instead of 400ing the loop, and `RoutingConsumerTypeHealthCheck` reports **Degraded** naming the offending row.

---

## 4. Consumers (shipped code)

| Consumer | What it reads |
|---|---|
| [`Services/Ai/AnalysisActionService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisActionService.cs) | CRUD + by-code resolution. Read projection: `sprk_analysisactionid, sprk_name, sprk_description, sprk_systemprompt, sprk_temperature, sprk_outputschemajson`. |
| [`Services/Ai/PublicContracts/ConsumerRoutingService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs) | Left-outer link from Binding resolution; projects `sprk_kind`, `sprk_workflowclass`, `sprk_inputschema`, `sprk_modeltier` into the [`Binding`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/Binding.cs) contract with legacy-row-tolerant defaults (null kind → Prompted). |
| `ActionRunner` (`Services/Ai/LinearConsumers/`) | The prompted executor: renders JPS + one structured-output call; owns the typed parse of `sprk_inputschema` args. Executor output-token ceiling: `MaxOutputTokensCeiling = 4000`. |
| `ICodedWorkflowRegistry` + coded workflows (e.g. `DailyBriefingNarrator`) | Resolve `sprk_workflowclass`; coded composites read their hot-editable prompts from child Action rows by `sprk_actioncode`. |
| `RoutingConsumerTypeHealthCheck` | Scans active rows' `sprk_inputschema` for OpenAI-subset validity (Degraded on findings). |
| Frozen engine (`PlaybookOrchestrationService` via `sprk_playbooknode.sprk_actionid`) | Prompt-template triple only; dispatch reads `node.sprk_executortype` directly (R7 single-hop). |

---

## 5. Related docs

| Doc | Topic |
|---|---|
| [`sprk-playbookconsumer.md`](sprk-playbookconsumer.md) | The Binding (invocation unit) — THE single routing surface |
| [`sprk_analysistool.md`](sprk_analysistool.md) | The closed tool catalog |
| [`sprk_playbooknode.md`](sprk_playbooknode.md) | Frozen engine node table |
| [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) | Canonical design (§4.2 execution shapes, §6.1 Action) |
| `infra/dataverse/inputschemas/` | CI-validated `sprk_inputschema` mirrors (author here first) |
| `infra/dataverse/outputschemas/` | `sprk_outputschemajson` mirrors |
