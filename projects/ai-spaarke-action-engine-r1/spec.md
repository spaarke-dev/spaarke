# Spaarke Action Engine R1 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-05-29
> **Source**: [design.md](design.md) (~7,500 words, 12 sections; revised 2026-05-29 to add §4.5 Surfaces and §7.4 Execution-Time Conversational Invocation)
> **Owner**: ralph.schroeder@hotmail.com
> **Project key**: `ai-spaarke-action-engine-r1`

---

## Executive Summary

The Action Engine is the agent creation, management, and execution surface for the Spaarke domain — the management plane *around* the existing JPS Playbook engine. It treats deterministic and probabilistic Tools as first-class peers in a single Tool Registry, exposes Actions through three execution invocation paths (conversational via the Spaarke AI Assistant, explicit via UI affordances, system via scheduled/event/signal/webhook triggers), and enforces side-effect safety through a canonical `IGateResolver` primitive and mechanical Phase deny-tools enforcement at the `IToolHandlerRegistry` dispatch layer. MVP ships pro-code authoring of three starter Action Templates (Summarize a matter, Send weekly task digest, Find similar matters), the three meta-tools (`FindResources` / `GetResourceDetail` / `InvokeResource`) that expose the Tool Registry to the LLM, and the SpaarkeAi Code Page + ribbon + command bar surfaces; Visual Builder, Conversational Builder Agent, Assistant PCF on entity forms, and signal/event-driven Monitors defer to R2/R3.

---

## Scope

### In Scope (MVP)

**Conceptual model (design §4)** — Dataverse entities for `Action`, `ActionTemplate`, `ActionInstance`, `ActionRun`, `Monitor` (record shape only; binding to triggers deferred to R2), with the Action sitting **above** the existing JPS playbook (Action Definition → Playbook is a FK per coordination assessment §4.1).

**Tool Registry with extended metadata (design §5)** — `Identity`, `InputSchema`, `OutputSchema`, `Classification` (Deterministic / AI / Hybrid), `CostClass` (Free / Cheap / Expensive), `LatencyClass` (Sub100ms / Sub1s / Sub10s / LongRunning), `Idempotency`, `AuthMode` (Obo / AppOnly / None), `Discoverability` (`{ keywords, semanticDescription, sampleInvocations }`), `ModelTier` (Premium / Standard / Fast / Embedding), `PhaseRestrictions` (deny-list array), `EvidenceRequired` (bool), `IsAlwaysOnInAssistant` (bool), `PromoteInContexts` (string array).

**Meta-tools (design §7.4.1)** — Three first-class Tools registered on every Assistant session:
- `ResourceCandidate[] FindResources(string userIntent, int topK = 5)`
- `ResourceDetail GetResourceDetail(string resourceId)`
- `ResourceExecutionResult InvokeResource(string resourceId, Dictionary<string, object> parameters)`

**Always-on foundational Tools** — `SearchDocuments`, `QueryDataverse`, `GetCurrentEntityContext` registered alongside meta-tools on every session.

**Assistant surfaces (design §4.5.2)** — SpaarkeAi Code Page (`sprk_spaarkeai`, no-context default); ribbon + command bar invocation buttons on relevant entity forms (Matter, Project, Account, Contact) that pre-populate `ChatLaunchContext` and open SpaarkeAi as a side pane.

**Triggers (design §4)** — Manual + scheduled (cron) only. Trigger spec lives on Action Definition; trigger dispatch is the Hybrid D runtime topology (Azure-native scheduler for cron + BFF BackgroundService for execution).

**Approval via `IGateResolver` (design §5)** — Interface + 4 implementations + `sprk_gate_approval` Dataverse entity + `GateApprovalCard` shared UI component:
- `DataversePrecedentBoardGateResolver` — writes `sprk_gate_approval`; polls or webhook-resumes
- `InteractiveGateResolver` — in-chat / context-pane card via existing SSE
- `WebhookGateResolver` — agent-to-agent callbacks
- `AutoApproveGateResolver` — tests + opt-in low-risk Actions only

Gate types: `EthicsCritical`, `MeaningCritical`, `FinalDelivery`, `EngagementAcceptance`, `TeamSelection`, plus `Custom`. Default timeout 5 minutes → auto-reject.

**Phase deny-tools (design §5)** — `IToolHandlerRegistry` enforces phase-keyed deny-lists at dispatch time. Violation throws `PhaseToolDeniedException`. Phases: `authoring`, `schedule`, `execute`, `approve`, `deliver`. **Mechanical** enforcement, not prompt-coached.

**Three starter Action Templates** (the "Conservative 3" set per owner decision):
1. **Summarize a matter** — Deterministic Dataverse query for matter rollup + AI Tool for narrative summary; output to chat surface and optional email.
2. **Send weekly task digest** — Scheduled cron trigger; Deterministic Dataverse query + template render + Deterministic send-email Tool.
3. **Find similar matters / precedents** — Hybrid: semantic search via Azure AI Search + Deterministic Dataverse hydration; output to chat with citations.

**Pro-code authoring only** — Action Definitions and Templates authored as JSON in solution source, deployed via ALM. No Visual Builder, no Conversational Builder Agent in MVP.

**Default "Spaarke Assistant — General" playbook** — Provisioned via seed data on first deploy. Configures system prompt per design §7.4.3, always-on Tools, default `InteractiveGateResolver`. `sprk_aichatcontextmap` row for no-context SpaarkeAi landing.

**Hybrid D runtime topology** (per design §6 coordination recommendation):
- Scheduler: Azure-native Function timer (or Service Bus scheduled message — chosen in architecture spike)
- Single-step / deterministic Action execution: BFF (`Sprk.Bff.Api`) `IToolHandlerRegistry` dispatch
- Multi-step probabilistic agent loops: BFF — extend existing `SprkChatAgent` + `UseFunctionInvocation`; **do NOT introduce Microsoft Agent Framework as a separate runtime in MVP**

### Out of Scope (R2, R3, Post-MVP)

- **R2**: Signal-triggered Monitors (Insights Engine coupling); event-triggered Monitors (Dataverse webhooks); approval queue UI in workspace and Teams; Conversational Builder Agent v1 (template matching + parameter elicitation); expanded template library (15+); `EvaluatorGate` JPS Action category; `PlaybookExecutionFlow` shared component; consumption of `ISanitizer` / `GroundingVerifier` shared primitives from Insights Engine Phase 1; **Assistant PCF on entity forms**.
- **R3**: Visual Builder for templates and composition; Conversational Builder Agent v2; Action Library within tenant; Copilot Studio agent generation from Action Definitions.
- **Post-MVP horizon**: Cross-tenant Action Template sharing (vetted); mobile-first delivery surface; partner-built tools/templates marketplace; cost/usage governance UI for admins; M365 Copilot agent.
- **Explicitly NOT in this project (referenced for clarity)**: CUAD + MAUD seed-data ingestion; `sprk_clausetype` Dataverse taxonomy; `RedFlagDetector` Tool; `PB-011 Tabular Extraction` Playbook. These deferred with the Conservative 3 template choice.

### Affected Areas

| Area | Path | Nature of change |
|---|---|---|
| BFF AI feature module | `src/server/api/Sprk.Bff.Api/Services/Ai/` | New `ActionEngine/` subfolder: handlers, dispatch, registry, meta-tools |
| BFF API endpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/` | New `ActionEndpoints.cs` (Action CRUD + run), `ToolRegistryEndpoints.cs` (registry query for UI), extend `ChatEndpoints.cs` to register meta-tools and always-on Tools |
| BFF background jobs | `src/server/api/Sprk.Bff.Api/Services/Jobs/` | New `ScheduledActionDispatchJobHandler.cs` (consumes scheduler messages, dispatches Action runs) |
| BFF auth/audit | (existing) `Infrastructure/Auth/`, `Infrastructure/Audit/` | Extend `AuditEnrichmentMiddleware` to log Tool dispatch ids per ADR-015 |
| BFF DI | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` | New `AddActionEngineModule()` extension (ADR-010 feature module) |
| SpaarkeAi shell | `src/solutions/SpaarkeAi/src/` | Wire `ChatLaunchContext` schema from URL params; default playbook hookup; result tile rendering for explicit-path invocations |
| Shared components | `src/client/shared/Spaarke.UI.Components/src/components/` | New `GateApprovalCard/`; `IsAlwaysOnInAssistant` flag handling in `SprkChat`; result tile component |
| Dataverse schema | (solution) | New entities: `sprk_action`, `sprk_actiontemplate`, `sprk_actioninstance`, `sprk_actionrun`, `sprk_toolregistry`, `sprk_gate_approval`; extend existing `sprk_aichatcontextmap` |
| Azure AI Search | `infra/ai-search/` | New `spaarke-resource-registry-index` index definition (semantic search over Tool Registry `Discoverability` metadata) |
| Azure scheduler | `infra/` | New Function App or Service Bus scheduled message infrastructure (decided in architecture spike) |
| Web resources | `src/client/webresources/js/` | Ribbon launcher script `sprk_openActionEngine.js` for command-bar entry points |

---

## Requirements

### Functional Requirements

#### Conceptual Model (FR-01..FR-06)

- **FR-01** — System SHALL provide a `sprk_action` Dataverse entity carrying identity, owner, template reference, parameter values, trigger spec, authorization requirements, human-gate config, output contract, and governance fields (per design §5 "Action Definition"). **Acceptance**: Action record can be created via Dataverse, validated via endpoint, retrieved by id.

- **FR-02** — System SHALL provide a `sprk_actiontemplate` entity with parameter schema (JSON Schema), defaults, validation rules, natural-language description, and discoverability metadata. **Acceptance**: 3 starter Templates can be authored as solution JSON and deployed.

- **FR-03** — System SHALL provide a `sprk_actioninstance` entity that references a template + parameter blob. **Acceptance**: An Instance pointing at "Send weekly task digest" with `{matter: 'Smith Co', when: 'Mon 9am'}` is loadable and runnable.

- **FR-04** — System SHALL provide an `sprk_actionrun` entity with status states `Queued | Running | Completed | Failed | Poisoned | Cancelled` per ADR-017. **Acceptance**: Every dispatch produces a Run record with inputs, step outcomes, correlation id, attempt count, approval history.

- **FR-05** — Action Definition SHALL reference the existing JPS Playbook entity via FK (Action sits *above* Playbook per coordination assessment §4.1). **Acceptance**: Action's `playbookId` field validates against existing `sprk_aiplaybook`; deletion of referenced Playbook is blocked.

- **FR-06** — Action Definition SHALL carry a Trigger spec discriminator (`Manual | Scheduled`) in MVP, with serialized payload (`null` for manual; cron expression for scheduled). **Acceptance**: Scheduled Action with `"0 9 * * MON"` triggers next-Monday-9am dispatch.

#### Tool Registry (FR-07..FR-09)

- **FR-07** — System SHALL provide a `sprk_toolregistry` Dataverse entity with the extended metadata schema (Classification, CostClass, LatencyClass, Idempotency, AuthMode, Discoverability, ModelTier, PhaseRestrictions, EvidenceRequired, IsAlwaysOnInAssistant, PromoteInContexts) per design §5. **Acceptance**: At least 8 Tools registered in MVP (3 always-on foundational + 3 meta-tools + 2 starter-Template-supporting Tools).

- **FR-08** — Tool Registry SHALL be indexed in Azure AI Search index `spaarke-resource-registry-index` keyed by `resourceId`, with `Discoverability.semanticDescription` as the semantic-search field. **Acceptance**: `FindResources("send email to outside counsel")` returns ranked candidates including matter-email Tools.

- **FR-09** — Tool dispatch SHALL go through `IToolHandlerRegistry` which enforces (a) `AuthMode` (rejects unauthorized callers), (b) `PhaseRestrictions` (throws `PhaseToolDeniedException` when current phase is in deny-list), (c) audit-log per ADR-015 on every invocation. **Acceptance**: A Builder-Agent-phase caller invoking `send_email` throws `PhaseToolDeniedException` with phase + tool name in message.

#### Meta-tools (FR-10..FR-12)

- **FR-10** — System SHALL register `FindResources(string userIntent, int topK = 5)` on every Assistant session. Implementation: semantic search over `spaarke-resource-registry-index` filtered by caller's authorization. **Acceptance**: Returns `ResourceCandidate[]` with id, descriptions, parameter schema; respects p95 latency NFR-01.

- **FR-11** — System SHALL register `GetResourceDetail(string resourceId)` returning full schema + `SourceHints` per parameter (which can be auto-resolved from surface context vs which require user input). **Acceptance**: Returns `ResourceDetail` with parameter schema, source hints, sample invocations.

- **FR-12** — System SHALL register `InvokeResource(string resourceId, Dictionary<string, object> parameters)` returning `ResourceExecutionResult` or a confirmation request if `humanGate.required = true`. **Acceptance**: Direct invocation of a Tool with all parameters resolved executes end-to-end; Tool with `humanGate.required = true` returns gate-pending status.

#### Surfaces (FR-13..FR-15)

- **FR-13** — System SHALL render the Assistant in SpaarkeAi Code Page (`sprk_spaarkeai`) using the existing `SprkChat` shared component, hydrated with the default "Spaarke Assistant — General" playbook when no entity context is supplied. **Acceptance**: User lands on SpaarkeAi, sends "What matters are open?", `DataverseQueryTools` invokes and returns scoped results.

- **FR-14** — System SHALL expose a ribbon button (`sprk_openActionEngine.js`) on Matter, Project, Account, Contact entity forms that opens SpaarkeAi as an `Xrm.App.sidePanes` pane, passing entity context via `ChatLaunchContext` URL params. **Acceptance**: Clicking the ribbon button on a Matter record opens the pane pre-populated with that Matter's id and type.

- **FR-15** — System SHALL render result tiles for explicit-path Action invocations in SpaarkeAi (and surface notifications via SSE to other open chat surfaces). **Acceptance**: Invoking "Summarize a matter" via the ribbon delivers the summary to the SpaarkeAi result tile + appends to chat history.

#### Triggers (FR-16)

- **FR-16** — System SHALL support manual + scheduled (cron) triggers. Manual: direct API invocation from any surface. Scheduled: Azure-native scheduler reads Action `triggerSpec` from Dataverse, enqueues `ScheduledActionDispatchJob` per ADR-004 Job Contract. **Acceptance**: Scheduled Action with `"0 9 * * MON"` triggers within 60 seconds of 09:00 Monday; manual invocation completes synchronously for sub-second Tools.

#### Approval / IGateResolver (FR-17)

- **FR-17** — System SHALL provide the `IGateResolver` interface with the four declared implementations and a `sprk_gate_approval` Dataverse entity. The `GateApprovalCard` shared UI component renders the approval surface (in-chat for `InteractiveGateResolver`). **Acceptance**: An Action with `humanGate.required = true` paused at gate; approval via UI resumes; rejection terminates with reason; 5-minute timeout auto-rejects.

#### Authoring (FR-18)

- **FR-18** — MVP authoring is pro-code only: Action Definitions and Templates authored as JSON in solution source, deployed via ALM. No Visual Builder, no Conversational Builder Agent. **Acceptance**: 3 starter Templates land via solution import; tenant admin can clone + modify a Template by editing JSON and re-importing.

#### Starter Templates (FR-19..FR-21)

- **FR-19** — System SHALL ship the **Summarize a matter** Template: takes a Matter record id; Deterministic Tool fetches matter rollup (status, key dates, recent activity); AI Tool composes narrative summary; default `humanGate = false`; output to chat surface + optional email via `SendEmail` Tool (which requires gate). **Acceptance**: Invocation via SpaarkeAi returns coherent multi-paragraph summary citing the matter's recent events.

- **FR-20** — System SHALL ship the **Send weekly task digest** Template: takes Matter id + day-of-week + time + lookahead window; cron-triggered; Deterministic Dataverse query for due tasks; template-rendered email body; `SendEmail` Tool with `humanGate = true` by default (configurable). **Acceptance**: Scheduled instance fires; if `humanGate = false` opt-out is configured, email sends without confirmation; otherwise gate UI prompts.

- **FR-21** — System SHALL ship the **Find similar matters** Template: takes a Matter record id + optional similarity dimensions (practice area, counterparty type, amount range); Hybrid invocation: semantic search via Azure AI Search over matter-summary index + Deterministic Dataverse hydration of top results; returns ranked list with citations. **Acceptance**: Invocation from a Matter form returns 3-5 ranked candidates with similarity rationale; results render in chat with hyperlinks back to matter records.

#### Phase Enforcement (FR-22)

- **FR-22** — `IToolHandlerRegistry` SHALL enforce phase-keyed deny-lists at dispatch time per design §5. Default phase sequence: `authoring → schedule → execute → approve → deliver`. Each phase declares forbidden Tool ids. Violation throws `PhaseToolDeniedException` with phase + tool name. **Acceptance**: Unit test: dispatching `send_email` while current phase is `authoring` throws; dispatching `query_dataverse` succeeds.

### Non-Functional Requirements

- **NFR-01 — Latency**: `FindResources` MUST return p95 < 200ms over the Tool Registry semantic index (design §12 #16). Always-on Tools dispatch p95 < 100ms. Measured via App Insights custom metric `action_engine.metatool.latency`.

- **NFR-02 — Auth**: All BFF endpoints MUST follow ADR-028: function-based contract (`useAuth()` / `authenticatedFetch`), MI for outbound (`DefaultAzureCredential` for Graph / Dataverse / Cosmos / Key Vault outbound), no plaintext secrets, audit enrichment per request (`oid`, `appid`, `obo`, `tenantId`, `correlationId`). Endpoint-filter authorization per ADR-008.

- **NFR-03 — Audit**: Every Tool dispatch MUST write audit per ADR-015 — Tier 2 hash-only for compliance (no Tool content); Tier 3 (Cosmos work history) with tenant scoping + GDPR erasure semantics. `IToolHandlerRegistry` is the audit choke point.

- **NFR-04 — Governance / Rate-limits**: Action invocation endpoints MUST apply rate limiting per ADR-016. Per-user soft cap on Tool dispatches/hour; per-tenant soft cap on AI Tool invocations/day. **MVP soft enforcement only** (warnings + telemetry); hard caps deferred to R2.

- **NFR-05 — Tenant isolation**: All Action / Run / Tool Registry artifacts MUST be tenant-scoped. Tool Registry semantic index uses tenant-filtered queries. Run records carry `tenantId` and never cross tenant boundaries in any query.

- **NFR-06 — Hallucination guardrails (design §7.4.5)**: All 8 guardrails MUST be in place: (1) system-prompt discipline, (2) Tool descriptions as contracts, (3) `EvidenceRequired: true` runtime guard, (4) `IGateResolver` before side effects, (5) Phase deny-tools mechanical, (6) ADR-008 authorization filter on discovery, (7) citation discipline (`SourceCitations` in Tool results), (8) audit on every invocation. Each tested in MVP.

- **NFR-07 — Observability**: App Insights custom events per Tool dispatch (event name `action_engine.dispatch`), per surface (event name `action_engine.surface_invocation`), per gate decision (event name `action_engine.gate_decision`). Custom metrics for `metatool.latency`, `tool.cost_class.invocations`, `run.duration_ms`.

- **NFR-08 — Idempotency / retry**: Scheduled dispatch follows ADR-004 — idempotent handler with deterministic `IdempotencyKey`, at-least-once delivery assumption, max attempts before poison defaults to ADR-004's standard (concrete value set in plan.md; baseline: 5 attempts with exponential backoff).

- **NFR-09 — Placement**: All new server code lands in the BFF (`Sprk.Bff.Api`) per ADR-013 (no separate microservice — meets all 4 exception criteria check below in §6 Placement Justification). New CRUD-side consumers of Action Engine capability MUST reach it via `Services/Ai/PublicContracts/` facade types per ADR-013 refined 2026-05-20.

- **NFR-10 — Publish hygiene**: Action Engine addition MUST NOT regress the BFF publish baseline (~60 MB compressed) by more than 5 MB. New NuGet package additions checked for HIGH-severity CVEs per `bff-extensions.md` §B before merge.

---

## Technical Constraints

### Applicable ADRs (binding)

| ADR | Title | Spec-relevant binding rule |
|---|---|---|
| **ADR-001** | Minimal API + BackgroundService | MUST use Minimal API for all BFF HTTP endpoints (no Functions hosting BFF endpoints); MUST use BackgroundService + Service Bus for BFF-coupled async work |
| **ADR-002** | Thin Dataverse plugins | MUST NOT implement Action orchestration in plugins; MUST keep plugins < 200 LoC and < 50ms p95 |
| **ADR-004** | Async Job Contract | MUST use Job Contract schema for `ScheduledActionDispatchJob`; MUST implement handlers as idempotent (safe under at-least-once); MUST propagate `CorrelationId`; MUST NOT place document bytes in payload |
| **ADR-006** | UI Surface Architecture | MUST use Code Page for SpaarkeAi (already exists); MUST keep ribbon scripts minimal (invocation only); MUST NOT add business logic to ribbon scripts |
| **ADR-008** | Endpoint-filter authorization | MUST apply endpoint-filter-based authorization (`.AddEndpointFilter<...>()`) on every new Action / Tool Registry endpoint; MUST NOT use global middleware |
| **ADR-009** | Redis-first caching | MUST use `IDistributedCache` for cross-request caching; MUST version cache keys (rowversion/etag); MUST NOT cache authorization decisions |
| **ADR-010** | Feature-module DI | MUST register through `AddActionEngineModule()` extension — not flat `Program.cs` blob |
| **ADR-012** | Shared component library | MUST place `GateApprovalCard` in `Spaarke.UI.Components`; MUST use Fluent UI v9; MUST NOT reference PCF-specific APIs in shared components |
| **ADR-013** | AI Architecture (refined 2026-05-20) | MUST keep new AI synthesis/chat/orchestration in BFF unless 4 exception criteria met (Action Engine does NOT meet them — stays in BFF); MUST route CRUD-side consumers via `Services/Ai/PublicContracts/` facades; MUST NOT inject `IOpenAiClient`/`IPlaybookService` directly into CRUD code |
| **ADR-015** | AI Data Governance | MUST send minimum text required to achieve outcome; MUST log only identifiers, sizes, timings, outcome codes; MUST scope all persisted AI artifacts by tenant; MUST NOT log document contents, prompts, or model responses verbatim; MUST NOT store verbatim AI response in Tier 2 audit (hash only) |
| **ADR-016** | AI Cost, Rate Limits, Backpressure | MUST apply rate limiting to Action invocation endpoints; MUST bound concurrency for upstream AI service calls; MUST use async jobs for batch AI work; MUST return clear `429`/`503` ProblemDetails under load |
| **ADR-017** | Run record status states | MUST use the canonical state sequence `Queued → Running → Completed | Failed | Poisoned | Cancelled` for `sprk_actionrun` |
| **ADR-018** | Feature flag + kill-switch | MUST include a feature-flag + kill-switch reference on Action Definition `governance` block |
| **ADR-028** | Spaarke Auth v2 | MUST use `useAuth()` / `authenticatedFetch` for all BFF calls from client surfaces; MUST use `DefaultAzureCredential` (MI) for server outbound; MUST enrich every authenticated server log with `oid`, `appid`, `obo`, `tenantId`, `correlationId`; MUST validate webhooks via HMAC-SHA256; MUST NOT add `accessToken`/`token` props/fields to client code; MUST NOT add plaintext secrets to `appsettings*.json` |

### Binding constraint: `.claude/constraints/bff-extensions.md`

Per CLAUDE.md §10, every BFF-touching task MUST:
- Cite the placement decision in the design (this spec — see §6 below)
- Verify publish-size impact (NFR-10)
- Verify no new HIGH-severity CVE from `dotnet list package --vulnerable --include-transitive`
- Route CRUD-side AI consumers through `Services/Ai/PublicContracts/` facade types
- Follow ADR-010 feature-module DI conventions

### Existing patterns to reuse (do NOT duplicate)

- **`SprkChatAgent`** at `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs` — existing agent runtime. Action Engine extends (does not replace) this for multi-step probabilistic invocation.
- **`PlaybookChatContextProvider`** at `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` — existing context resolution. Action Engine adds Action-context resolution rules to it.
- **`SprkChat`** shared component at `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/` — existing chat component. Action Engine surfaces consume it via different `ChatLaunchContext` payloads (per Phase D of the prior CORS investigation).
- **`SpaarkeAi` shell** at `src/solutions/SpaarkeAi/src/` — existing host shell. Action Engine adds: default-playbook hookup, result-tile component, `ChatLaunchContext` URL-param parsing.
- **`spaarke-file-index`** Azure AI Search service (`spaarke-search-dev`) — existing infrastructure. Action Engine adds new index `spaarke-resource-registry-index` to the same service (not the file index).
- **`Services/Ai/PublicContracts/`** facade pattern — per ADR-013 refined. Any CRUD-side caller of Action Engine reaches it via a facade type defined here.

---

## Placement Justification (per CLAUDE.md §10 binding rule)

**Decision**: All Action Engine server-side code lands in the BFF (`Sprk.Bff.Api`).

**ADR-013 exception-criteria check** (separate deployable would require ALL four):

| Criterion | Action Engine status |
|---|---|
| No latency coupling (<500ms TTFB requirement) | **FAILS** — meta-tool dispatch + Tool execution are latency-sensitive; conversational invocation requires sub-second p95 |
| No transactional coupling | **FAILS** — Action runs share auth, audit, and Run records with the rest of BFF |
| Bounded integration surface | **FAILS** — Action Engine surfaces to every Spaarke client (PCFs, Code Pages, ribbon, Add-ins, M365 Copilot in R3) |
| No duplication of latency-sensitive components | **FAILS** — separating would duplicate Tool dispatch + auth + audit |

**Conclusion**: BFF placement is the only architecturally valid choice. Documented per ADR-013 refined 2026-05-20.

**Size impact estimate**: ~3–5 MB compressed publish-size delta. Drivers: (a) Action / Tool Registry handler set (~1.5 MB), (b) meta-tool implementations + semantic-search client (~0.5 MB), (c) `IGateResolver` + 4 implementations (~0.5 MB), (d) new `Action` / `Job` endpoint groups (~0.5 MB), (e) new Dataverse SDK call surface (~0.5 MB). MUST verify ≤ 5 MB delta in task 001 architecture spike (NFR-10).

**Boundary statement**:
- All Action Engine code lives under `src/server/api/Sprk.Bff.Api/Services/Ai/ActionEngine/`.
- CRUD-side consumers reach Action Engine via new `Services/Ai/PublicContracts/IActionEngineFacade.cs` (per ADR-013 refined) — NOT direct injection of `IToolHandlerRegistry`, `IGateResolver`, or other internal types.
- All new endpoints use endpoint-filter authorization (ADR-008).
- All DI registration goes through `AddActionEngineModule()` extension (ADR-010).
- New Tool Registry semantic index is a separate Azure AI Search index (`spaarke-resource-registry-index`) on the existing `spaarke-search-dev` service — does NOT touch the `spaarke-file-index` infrastructure.

---

## Success Criteria

1. [ ] Three starter Templates (Summarize a matter, Send weekly task digest, Find similar matters) author + execute end-to-end via both manual and (where applicable) scheduled invocation paths. **Verify**: integration test `ActionEngine.IntegrationTests.StarterTemplates`.
2. [ ] Conversational invocation via SpaarkeAi Code Page resolves user intent → Tool via meta-tools and executes. **Verify**: E2E test with "Summarize Matter X" yielding the matter's summary in chat with citations.
3. [ ] `IGateResolver` routes EthicsCritical / MeaningCritical / FinalDelivery gates to declared resolvers; approval resumes execution; rejection terminates with reason; 5-min timeout auto-rejects. **Verify**: unit + integration test per resolver.
4. [ ] Phase deny-tools throws `PhaseToolDeniedException` when an execution Tool is dispatched during authoring phase. **Verify**: unit test `IToolHandlerRegistry.PhaseDenyTests`.
5. [ ] `FindResources` p95 < 200ms over the `spaarke-resource-registry-index` (NFR-01). **Verify**: load test in task 001 architecture spike + App Insights metric `action_engine.metatool.latency`.
6. [ ] Every Tool dispatch writes audit per ADR-015 — Tier 2 hash-only + Tier 3 work history with tenant scoping. **Verify**: integration test inspects audit log + Cosmos partition.
7. [ ] All 8 hallucination guardrails (design §7.4.5) present and tested (each has at least one unit/integration test asserting enforcement).
8. [ ] BFF publish-size delta ≤ 5 MB (NFR-10). **Verify**: compare `dotnet publish --runtime linux-x64` output before/after.
9. [ ] No new HIGH-severity CVE introduced. **Verify**: `dotnet list package --vulnerable --include-transitive` clean.
10. [ ] Hybrid D runtime topology validated by architecture spike (task 001) — concrete Azure-native scheduler choice (Logic Apps timer vs Service Bus scheduled message vs Azure Container Apps Jobs) documented in plan.md before main MVP build begins.
11. [ ] Endpoint-filter authorization (ADR-008) applied on every new Action Engine endpoint. **Verify**: integration test asserting 401 without auth + 403 for unauthorized user on protected Action.
12. [ ] No CRUD-side caller injects Action Engine internals — all reach via `Services/Ai/PublicContracts/IActionEngineFacade.cs`. **Verify**: architecture-test scanning DI graph + `code-review` skill at Step 9.5.

---

## Dependencies

### Prerequisites (must exist before MVP build begins)

- Existing BFF (`Sprk.Bff.Api`) — running on `spaarke-bff-dev` (Linux App Service); confirmed healthy after the 2026-05-28 auth chain remediation.
- Existing `SprkChatAgent` + `PlaybookChatContextProvider` — reused as agent runtime substrate.
- Existing shared component library (`@spaarke/ui-components`) — for `GateApprovalCard` placement and `SprkChat` reuse.
- Existing shared auth library (`@spaarke/auth`) — for ADR-028 compliance on every Action Engine endpoint.
- Existing JPS Playbook engine and `sprk_aiplaybook` entity — Action Definition FK target.
- Existing Azure AI Search service (`spaarke-search-dev`) — host for the new `spaarke-resource-registry-index`.
- Existing dev Dataverse environment with publisher prefix `sprk_` — host for new Action Engine entities.
- Existing `spaarke-openai-dev` Cognitive Services account with `gpt-4o-mini` deployment — for AI Tools.

### External (decided during this project)

- **Azure-native scheduler choice** — Logic Apps timer vs Service Bus scheduled message vs Azure Container Apps Jobs. Decided in task 001 architecture spike; documented in plan.md and a new runtime-topology ADR before main MVP build.

---

## Owner Clarifications

*Answers captured during design-to-spec interview, 2026-05-29:*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| MVP starter templates | Design says "3–5 starter templates" but doesn't enumerate. Which set? | **Conservative 3**: (a) Summarize a matter, (b) Send weekly task digest, (c) Find similar matters | LAVERN CUAD/MAUD ingestion, `RedFlagDetector`, PB-011 Tabular Extraction → all deferred to R2. Smaller MVP, faster path to ship. |
| Runtime topology | Commit to Hybrid D now, or require architecture spike as a blocking prereq? | **Commit to Hybrid D in MVP** — architecture spike folded into task 001 as a 1-2 day validation, not a separate prerequisite | MVP build starts immediately. Spike output: concrete Azure-native scheduler choice + a runtime-topology ADR. |
| Assistant PCF on Matter form | MVP scope or defer? | **Defer to R2** | MVP ships only SpaarkeAi Code Page + ribbon + command bar surfaces. Smaller, narrower demo. |
| LAVERN-derived patterns (IGateResolver, Phase deny-tools, extended Tool Registry metadata) | Ratify as separate Spaarke ADRs first, or inline as Action Engine design? | **Inline as Action Engine design** — these patterns are formalized within THIS project's design; no separate ADR ratification | LAVERN is an external Apache-2.0 reference project ([AnttiHero/lavern](https://github.com/AnttiHero/lavern)); patterns adopted from `LAVERN-ANALYSIS-AND-PLAN.md §10.x` are cited as inspiration source, not as Spaarke ADRs. Eliminates the prerequisite to ratify LAVERN-derived patterns as separate Spaarke ADRs (a now-resolved item in design §12). |

---

## Assumptions

*Proceeding with these assumptions (owner did not explicitly specify):*

- **Default Assistant playbook** ("Spaarke Assistant — General") provisioned via seed data on first deploy of the Action Engine solution. Seed data lives in solution source.
- **Tool Registry semantic index** is a new index `spaarke-resource-registry-index` on the existing `spaarke-search-dev` service — NOT piggybacked on `spaarke-file-index` (which is for document RAG).
- **One registry, two record types** for Tools and Action Templates (design §12 #14 lean) — simplifies LLM discovery surface; concrete schema in plan.md.
- **Cancellation / retry** for `ScheduledActionDispatchJob` follows ADR-004 defaults — idempotent + at-least-once; concrete max-retry + backoff curve set in plan.md (baseline: 5 attempts with exponential backoff before poison).
- **Cost-class enforcement** is soft in MVP (telemetry + warnings); hard caps deferred to R2 (NFR-04).
- **Audit log schema** follows ADR-015 Tier 2 (hash-only) + Tier 3 (Cosmos work history with tenant partition + GDPR erasure); concrete field list set in plan.md.
- **Ribbon launcher pattern** mirrors the existing `sprk_openSprkChatPane.js` pattern (per Phase D of the prior CORS investigation) — minimal ribbon script invoking `Spaarke.ActionEngine.openPane(...)` that opens SpaarkeAi as a side pane with `ChatLaunchContext` URL params.
- **Existing JPS playbook entity name** is `sprk_aiplaybook` — FK target for Action Definition. Verified in plan.md.

---

## Unresolved Questions

*Open items carried from design §12; need answers during plan.md / task decomposition (not blocking spec generation):*

- [ ] **Tool Handler registration mechanism** — attribute, configuration, or hybrid? (design §12 #4) Blocks: Tool Registry implementation task structure.
- [ ] **Builder Agent AI-Tool definitions for the 3 starter Templates** — full Builder Agent is R2, but the 3 MVP Templates each need their AI-classed Tools (`MatterSummary`, `MatterSimilarity`) specified. (design §12 #6) Blocks: starter Template implementation.
- [ ] **`sprk_aichatcontextmap` schema** — exact field list (entity type, optional view id, optional command-bar context). (design §12 #15) Implementation-level; resolved in plan.md.
- [ ] **Audit field list per Tool dispatch** — concrete column list satisfying ADR-015 Tier 2 hash + Tier 3 work history. (NFR-03) Resolved in plan.md.
- [ ] **Variable-type control library scope** — needed for R2 Visual Builder; MVP is pro-code so non-blocking, but pre-work for R2 should be scoped. (design §12 #5)
- [ ] **Resource Registry vs Tool Registry naming** — design lean is "one registry, two record types"; final name + schema decided in plan.md. (design §12 #14)
- [ ] **Concrete Azure-native scheduler choice** (Logic Apps timer vs Service Bus scheduled message vs Azure Container Apps Jobs) — task 001 architecture spike output. (design §6, NFR-10)
- [ ] **Cross-solution layering rules** for Templates (system → ISV → customer overrides). (design §12) R2+ consideration; not MVP-blocking.
- [ ] **Cancellation and recovery semantics** for long-running multi-step Actions beyond the ADR-004 defaults. (design §12) Resolved in plan.md.

---

*AI-optimized specification. Original design: [design.md](design.md). LAVERN reference: [`projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md`](../ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md) (external Apache-2.0 source — patterns inspiration only).*
