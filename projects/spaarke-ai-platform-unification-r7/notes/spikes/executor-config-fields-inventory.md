# Executor Config Field Inventory (task 032 / FR-16)

> **Status**: complete; drives task 032 GetConfigSchema() implementations.
> **Generated**: 2026-06-28
> **Method**: read each executor's `Validate()` + `ExecuteAsync()` + private config record (where present); enumerated every `ConfigJson` field read at runtime.

---

## Inventory summary — 25 concrete executors (per task 031 / FR-16)

| # | Executor | Tier | ExecutorType | Schema Strategy |
|---|---|---|---|---|
| 1 | AiAnalysisNodeExecutor | **RICH** | AiAnalysis (0) | 6 fields |
| 2 | AiCompletionNodeExecutor | **RICH** | AiCompletion (1) | 2 fields |
| 3 | ConditionNodeExecutor | **RICH** | Condition (30) | 3 fields |
| 4 | EntityNameValidatorNodeExecutor | **RICH** | EntityNameValidator (141) | 2 fields |
| 5 | CreateNotificationNodeExecutor | **RICH** | CreateNotification (50) | 4 core + flagged for full set |
| 6 | AgentServiceNodeExecutor | PLACEHOLDER | AgentService (60) | Empty |
| 7 | CreateTaskNodeExecutor | PLACEHOLDER | CreateTask (20) | Empty |
| 8 | DeclineToFindNode | PLACEHOLDER | DeclineToFind (110) | Empty |
| 9 | DeliverCompositeNodeExecutor | PLACEHOLDER | DeliverComposite (42) | Empty |
| 10 | DeliverOutputNodeExecutor | PLACEHOLDER | DeliverOutput (40) | Empty |
| 11 | DeliverToIndexNodeExecutor | PLACEHOLDER | DeliverToIndex (41) | Empty |
| 12 | EvidenceSufficiencyNode | PLACEHOLDER | EvidenceSufficiency (100) | Empty |
| 13 | GroundingVerifyNode | PLACEHOLDER | GroundingVerify (70) | Empty |
| 14 | IndexRetrieveNode | PLACEHOLDER | IndexRetrieve (90) | Empty |
| 15 | LiveFactNode | PLACEHOLDER | LiveFact (80) | Empty |
| 16 | LoadKnowledgeNodeExecutor | PLACEHOLDER | LoadKnowledge (142) | Empty |
| 17 | LookupUserMembershipNodeExecutor | PLACEHOLDER | LookupUserMembership (52) | Empty |
| 18 | ObservationEmitterNodeExecutor | PLACEHOLDER | ObservationEmit (140) | Empty |
| 19 | QueryDataverseNodeExecutor | PLACEHOLDER | QueryDataverse (51) | Empty |
| 20 | ReturnInsightArtifactNode | PLACEHOLDER | ReturnInsightArtifact (120) | Empty |
| 21 | ReturnResponseNodeExecutor | PLACEHOLDER | ReturnResponse (143) | Empty |
| 22 | SanitizerNodeExecutor | PLACEHOLDER | Sanitization (130) | Empty |
| 23 | SendEmailNodeExecutor | PLACEHOLDER | SendEmail (21) | Empty |
| 24 | StartNodeExecutor | PLACEHOLDER | Start (33) | Empty |
| 25 | UpdateRecordNodeExecutor | PLACEHOLDER | UpdateRecord (22) | Empty |

Total **25 executors** (matches task 031 commit notes). The ExecutorType enum declares 33 values; the remaining 8 (RuleEngine, Calculation, DataTransform, CallWebhook, SendTeamsMessage, Parallel, Wait, ReturnResponse-prior-id?) are enum-declared-but-no-impl — they will dispatch through INodeExecutor's default `GetConfigSchema()` returning a placeholder.

---

## Rich schemas — field-by-field derivation

### 1. AiAnalysisNodeExecutor (ExecutorType.AiAnalysis = 0)

Reads from `node.ConfigJson`:

| Field | Type | Required | Source | Notes |
|---|---|---|---|---|
| `templateParameters` | Object | No | `ExtractTemplateParameters()` | Key→value map substituted into `{{var}}` bindings in JPS prompt instruction. |
| `promptSchemaOverride` | Object | No | `ApplyPromptSchemaOverride()` via `PromptSchemaOverrideMerger.ExtractOverride` | Per-node JPS override merged into Action's base prompt (FR-25). |
| `knowledgeRetrieval` | Object | No | `ParseKnowledgeRetrievalConfig()` | Nested config: `{mode: "auto"\|"always"\|"never", topK: number, includeDocumentContext: bool, includeEntityContext: bool}`. |
| `includeDocumentContext` | Boolean | No | `IsDocumentContextEnabled()` | Legacy top-level flag (superseded by `knowledgeRetrieval.includeDocumentContext`; both supported for back-compat). |
| `parentEntityType` | String | No | `ExtractEntityScope()` | Entity type for L2/L3 retrieval scoping (e.g., "Matter", "Project"). |
| `parentEntityId` | String | No | `ExtractEntityScope()` | Entity ID for L2/L3 retrieval scoping. |

### 2. AiCompletionNodeExecutor (ExecutorType.AiCompletion = 1)

Reads from `node.ConfigJson`:

| Field | Type | Required | Source | Notes |
|---|---|---|---|---|
| `templateParameters` | Object | No | `ExtractTemplateParameters()` | Same shape as AiAnalysis — `{{var}}` substitution into JPS instruction section. |
| `promptSchemaOverride` | Object | No | `ApplyPromptSchemaOverride()` | Per-node JPS override (FR-25 KEEP). |

Note: per task 030 design §8, this is the canonical "minimal-rich" schema. AiCompletion is prompt-only per FR-13 — no L1/L2/L3 retrieval, no `$ref`/`$choices` resolution, so no other config fields.

### 3. ConditionNodeExecutor (ExecutorType.Condition = 30)

Reads from `node.ConfigJson` via `ConditionNodeConfig` record:

| Field | Type | Required | Source | Notes |
|---|---|---|---|---|
| `condition` | Object | Yes | `ConditionNodeConfig.Condition` | Required. Nested `ConditionExpression`: `{operator, left, right, conditions?, condition?}`. Operators: eq/ne/gt/lt/gte/lte/contains/startsWith/endsWith/exists/and/or/not. |
| `trueBranch` | String | No | `ConditionNodeConfig.TrueBranch` | Node OutputVariable to enable when condition is true. |
| `falseBranch` | String | No | `ConditionNodeConfig.FalseBranch` | Node OutputVariable to enable when condition is false. |

Validate enforces: at least ONE of `trueBranch` or `falseBranch` must be specified. Neither alone is marked required, but together they're required by the validator.

### 4. EntityNameValidatorNodeExecutor (ExecutorType.EntityNameValidator = 141)

Reads from `node.ConfigJson` via `EntityNameValidatorNodeConfig` record (camelCase via `[JsonPropertyName]`):

| Field | Type | Required | Source | Notes |
|---|---|---|---|---|
| `candidateText` | String | Yes | `EntityNameValidatorNodeConfig.CandidateText` | LLM-emitted text to scrub. Required (non-whitespace). |
| `allowList` | Array | Yes | `EntityNameValidatorNodeConfig.AllowList` | Array of allowed entity names (matters, contacts, parties, etc.). Required (must be non-null; empty array is valid = "scrub all proper-noun-bearing sentences"). |

### 5. CreateNotificationNodeExecutor (ExecutorType.CreateNotification = 50)

Reads from `node.ConfigJson` via `NotificationNodeConfig` record. **Required fields per Validate(): `title` + `body`**. Full field set:

| Field | Type | Required | Notes |
|---|---|---|---|
| `title` | String | Yes | Notification title; supports `{{templateVars}}`. |
| `body` | String | Yes | Notification body; supports `{{templateVars}}`. |
| `category` | String | No | Idempotency-grouping category. |
| `priority` | Number | No | Default 200000000. Enum-ish: 100000000=Informational, 200000000=Important, 300000000=Urgent. |
| `toastType` | Number | No | Default 200000000 (Timed). Enum-ish: 100000000=Hidden, 200000000=Timed, 300000000=Standard. |
| `actionUrl` | String | No | URL to navigate when clicked. |
| `regardingId` | String | No | Regarding record GUID; supports templates. |
| `regardingType` | String | No | Regarding entity logical name (e.g., "sprk_document"). |
| `recipientId` | String | No | Systemuserid; falls back to run context userId. |
| `iterateItems` | Boolean | No | When true, iterate upstream query items. |
| `itemNotification` | Object | No | Per-item template when `iterateItems` is true. |
| `dueDate` | String | No | ISO-8601 due date (R2.2 enrichment). |
| `regardingName` | String | No | FR-6 enrichment (R4 task 020). |
| `sourceEntityType` | String | No | FR-6 enrichment. |
| `sourceId` | String | No | FR-6 enrichment. |
| `sourceModifiedOn` | String | No | FR-6 enrichment (ISO-8601). |
| `sourceOwningUser` | String | No | FR-6 enrichment. |
| `viaMatterId` | String | No | FR-6 enrichment. |
| `viaMatterName` | String | No | FR-6 enrichment. |
| `viaMatterMembershipsVariable` | String | No | FR-6 enrichment — upstream LookupUserMembership node's OutputVariable name. |

Per FR-16 acceptance (task POML), the rich schema declares the four core fields (title/body/recipient/category) which task POML calls out, plus all R4 enrichment fields for backward-compat documentation. Per design §5, nested `itemNotification` is `SchemaFieldType.Object` (sub-JSON editor in canvas).

---

## Placeholder schemas — accurate descriptions

Each placeholder executor uses `ExecutorConfigSchema.Empty(ExecutorType.X, "<description>")`. Descriptions below are drawn from the executor's XML doc, INodeExecutor enum doc comments, or class-level remarks:

| ExecutorType | Description for Empty() schema |
|---|---|
| AgentService (60) | "Routes the playbook node to Azure AI Foundry Agent Service (Phase 2)." |
| CreateTask (20) | "Creates a Dataverse task record from playbook context." |
| DeclineToFind (110) | "Deterministic exit emitting a structured DeclineResponse when EvidenceSufficiency returns insufficient (zero-LLM)." |
| DeliverComposite (42) | "Multi-section composite delivery — assembles N upstream Action node outputs keyed by sectionName for consumer routing (FR-52)." |
| DeliverOutput (40) | "Single-action delivery — renders and emits the final playbook output for the consumer." |
| DeliverToIndex (41) | "Queues the document for RAG semantic indexing." |
| EvidenceSufficiency (100) | "Reads prior node outputs and applies a configured evidence rule; emits sufficient/insufficient verdict (D-49 / LAVERN Pattern #7)." |
| GroundingVerify (70) | "Zero-LLM citation verification — checks quoted evidence from prior AI nodes against source chunks (D-P9 / D-47 / LAVERN 10.6)." |
| IndexRetrieve (90) | "Retrieves Observations and Precedents from spaarke-insights-index via filter + vector search (D-P12 / SPEC §3.4.3)." |
| LiveFact (80) | "Resolves a deterministic Live Fact about a Dataverse subject (e.g., matter:M-1234.totalSpend) per design.md §2.1 (confidence=1.0)." |
| LoadKnowledge (142) | "Canvas-only Control node — pass-through knowledge binding (R4 control-flow-executor). Evaluates optional passthroughBinding templates." |
| LookupUserMembership (52) | "Resolves current user's record memberships for a given entity type via IMembershipResolverService (FR-1B.1)." |
| ObservationEmit (140) | "Emits N observations (one per surviving L2 candidate after grounding) + L1 classification — final node of universal-ingest@v1 JPS playbook." |
| QueryDataverse (51) | "Executes a FetchXML query against Dataverse and returns results." |
| ReturnInsightArtifact (120) | "Final node of an Insights synthesis playbook — serializes upstream outputs into an InsightArtifact envelope (D-P12 / D-P1)." |
| ReturnResponse (143) | "Canvas-only Control node — terminal 'return response' projection. Reads configJson.responseBinding (R4 control-flow-executor)." |
| Sanitization (130) | "Sanitizes raw document text for downstream LLM consumption — strips control characters, retrieval blocks, noise (D-50 / D-A25 LAVERN sanitizer)." |
| SendEmail (21) | "Sends email via Microsoft Graph." |
| Start (33) | "Canvas anchor — pass-through with no execution logic." |
| UpdateRecord (22) | "Updates a Dataverse entity record." |

---

## Coordination notes

- **Schema-field-name ↔ config-record contract**: per design §11, schema field names match the `[JsonPropertyName]` attributes (or property names with camelCase serialization) on each executor's private config record. AiAnalysis/AiCompletion read camelCase property names (e.g., `templateParameters`, `promptSchemaOverride`) directly via `JsonElement.TryGetProperty`. ConditionNodeConfig uses `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` so PascalCase C# props map to camelCase JSON. EntityNameValidatorNodeConfig explicitly declares `[JsonPropertyName("candidateText")]` + `[JsonPropertyName("allowList")]`. NotificationNodeConfig relies on case-insensitive deserialization (no explicit attributes — schema uses camelCase per convention).

- **Forward-compat**: per design §7, adding fields is additive on `ConfigSchemaField`. Removing is breaking. Renaming `ExecutorTypeName` is breaking (canvas keys grouping by it).

- **Task 033 endpoint**: aggregates all 25 via `INodeExecutorRegistry.GetAllExecutors() + .Select(e => e.GetConfigSchema())`, ordered by `ExecutorTypeValue`.

- **Task 084 (Wave 8) consumes the rich schemas** to render typed forms in PlaybookBuilder canvas. The 5 rich schemas above are the prime targets.
