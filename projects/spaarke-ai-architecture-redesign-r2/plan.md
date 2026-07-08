# Project Plan: Spaarke AI Architecture Redesign R2 (Core)

> **Last Updated**: 2026-07-08
> **Status**: Ready for Tasks
> **Spec**: [spec.md](./spec.md)
> **Design**: [design.md](./design.md) (v0.4)
> **Pre-spec inputs**: [notes/d-f0-eval-family-spec.md](./notes/d-f0-eval-family-spec.md) · [notes/policy-v2-origin-classification-decision-tree.md](./notes/policy-v2-origin-classification-decision-tree.md)

---

## 1. Executive Summary

**Purpose**: Refine R1's coarse-grained AI platform into a refined experience along two owner-prioritized axes — **judgment/friction** and **memory** — built strictly ON ADR-039 (grounded execution, closed catalogs) + ADR-040 (session ledger). This is the platform CORE: sole owner of `Services/Ai/` internals; publishes seams the parallel Compose r2 satellite consumes.

**Scope**: 51 FRs across Phase 0 (reconciliation/discovery), Phase A0 (seven contract-first walking skeletons), Phase A-infra (triple-twin hoist + test-repair), and the three core gates — G-R2-A (judgment: D-F0..F5), G-R2-B (memory: D-M1..M4), G-R2-D (hardening). ADR-041 + ADR-042 authored + promotion-gated. Compose editor/lifecycle, Daily Briefing remediation, Insights refurbish, and Work IQ runtime are OUT (satellites / separate projects / deferrals).

**Timeline**: Large. ~7 waves; A0 + D-F0 run in parallel up front; the memory wave is the deepest new build (tempered by substantial reuse — see §3). **Estimated effort**: multi-week; gate-sequenced, not date-sequenced — each gate is browser-UAT-verified on spaarkedev1.

**Hot-path**: BFF=Y · SpaarkeAi=Y · ci-workflows=N · skill-directives=Y · root-CLAUDE=N. Parallel peer: `spaarkeai-compose-r2` (BFF+SpaarkeAi) — core-owns-AI-internals rule + seam boundary keep them non-colliding.

---

## 2. Architecture Context

### Design Constraints — From ADRs (must comply)

- **ADR-039** (Accepted, binding) — grounded execution, closed catalogs; risk factors are catalog-declared DATA (`side_effect_class`), never runtime LLM judgment; no second intent mechanism.
- **ADR-040** (Accepted, binding) — session ledger; conversation memory = ledger facade; **no parallel session cache**; storage-before-render.
- **ADR-013** (refined 2026-05-20) — CRUD consumes AI only via `Services/Ai/PublicContracts/`; no injecting `IOpenAiClient`/`IPlaybookService` into CRUD.
- **ADR-037** (amended) — section-keyed streaming for progressive render (D-F5).
- **ADR-015** — memory items are Tier 3 (user-owned, GDPR-erasable).
- **ADR-029** — publish-size governance (≤60 MB compressed; per-task verification).
- **ADR-032** — Null-Object kill-switch for any feature-gated service.
- **ADR-038** — integration-heavy pyramid; contract tests are a KEEP path; eval-green stays a merge gate; bans `Mock<HttpMessageHandler>`, DI-registration, ctor null-check tests.

### From Spec — MUST rules

- MUST build ON ADR-039/040 (catalog rows, ledger entries/readers, gate policy, context assembly, client rendering — no new dispatch protocol / session cache / routing outside Bindings).
- MUST make side effects deterministic; reads are free (D-F0(b)).
- MUST store before render (ADR-040).
- MUST use structured memory objects (not embeddings) for User/Record scopes; partition memory by subject (`entityId`/`userId`), not `/tenantId`.
- MUST publish seams FIRST (Phase A0) so Compose r2 is never blocked.
- MUST sequence the triple-twin hoist (FR-A-01) BEFORE any catalog-row task.
- MUST NOT let untrusted content originate a memory write.
- MUST NOT weaken a gate/hard block via D-F0 (degradation ladder operates BELOW the side-effect line).

### ADR Tensions (all Path C — comply; see spec §ADR Tensions)

ADR-040 no-parallel-cache vs Memory Service (resolved by construction — conversation=ledger facade); ADR-040 disposition-only-rendering vs `compose` member (enum extension is compliant); ADR-039 one-intent vs Policy v2 tiers (catalog DATA, not runtime judgment); D-F0(b) reads-free vs budget-8 (no tension — willingness within the bound). No Path B amendments to pre-merge.

---

## 3. Component Justification — Default to Reuse (CLAUDE.md §11)

Discovery (project-pipeline Step 2) confirmed the presumption-of-reuse and **shrank net-new scope** vs the design's §11 estimate. Reasoning Runtime, gate, ledger, directive layer, and — critically — **a memory subsystem** already exist.

| Need | Reuse / EXTEND (with `file:line` anchor) | Net-new |
|---|---|---|
| Reasoning Runtime | EXTEND `Chat/SprkChatAgentFactory.cs:50` + `Chat/SessionDispatchOrchestrator.cs:74` (FORMALIZE, not rebuild) | Context Binder + Completion Engine collaborators |
| Confirmation gate | EXTEND `Chat/SideEffectGateAIFunction.cs:77` + `Chat/PendingPlanManager.cs:57` + `Chat/TypedHandlerResumeExecutor.cs:86` | Policy v2 tier/origin engine + E-1..E-6 + pre-suspend validation |
| Origin/`dispatchUncertain` seam | EXTEND `Chat/ElicitationTurnRouter.cs:34` (no literal `dispatchUncertain` symbol — behavior lives here) | deterministic origin classifier |
| Session Ledger | REUSE `Models/Ai/Chat/SessionLedgerEntries.cs` (`SessionOutput`, `SessionToolChain`, `SessionGate`; `{bindingId}@t{n}` at `:39`; inline cap `:50`) | ContextEnvelope-fingerprint entry type |
| Context primitives | GENERALIZE `Chat/ChatHistoryManager.cs:302` `BuildLedgerOutputsContext` + `SprkChatAgentFactory.cs:129` `BuildCurrentDateDirective` + `PlaybookChatContextProvider.cs:582` host-identity + `OrchestratorPromptBuilder.cs:44` | Context Binder + ContextEnvelope contract |
| Directive layer | EXTEND `SprkChatAgentFactory.cs:65` honesty directive + `ToolResult.cs:326` `UserSummary` + `HandoffUrlBuilder.cs` link composition | D-F0 strategy meta-prompt block |
| **Memory Service** ⚠️ | **GENERALIZE the EXISTING memory subsystem** — `Services/Ai/Memory/MatterMemoryService.cs:27` (`MemoryFact` model, ETag/versioning, GDPR erasure, budget-serialization) + `MemoryCompositionService`, `PinnedContextRepository`, `RecentlyDiscussedTracker`, `PinnedMemoryEndpoints`, client `memory/*`. **`MatterMemory`→`RecordMemory`, generic `(entityType,entityId)`** (matters/projects/invoices/work-assignments/events/documents — NOT matter-only) | TWO scopes (Record + User); envelope fields; view/delete surface; explicit-only `memory.write` via Policy v2 |
| Memory store | **NEW Cosmos container partitioned by SUBJECT** (`entityId`/`userId`, NOT `/tenantId` — dedicated-per-customer envs make `/tenantId` a single hot partition; key can't change in place) — **reuse the service CODE, not the container** (resolves Q4). Live-doc migration decided at task 050 | subject partition key; scope discriminator; owner/subject keying |
| Insights persistence | Insights currently **TTL-cached, no durable store** (`Insights/InsightsPlaybookExecutionCache.cs`) → Record memory shaped as the future durable insight store (`source: insights-engine`) | envelope supports it; **wiring = follow-on** |
| Trace surface | EXTEND `Spaarke.AI.Widgets/.../ExecutionTraceWidget.tsx` + `executionTraceBuffer.ts` (buffer `MAX=50`, mount-gap documented) + `Telemetry/ContextEventEmitter.cs` | **server ToolChain read surface (NOT FOUND — net-new, FR-A1-09)** + live plan narration |
| OutcomeCard | build on `ToolResult.cs:326` `UserSummary` + `HandoffUrlBuilder.cs` server links | **OutcomeCard component (NOT FOUND — net-new)** + job-aware states |
| Job status | REUSE `Services/Jobs/ServiceBusJobProcessor.cs` + `JobContract.cs:9` + `JobStatusService.cs` + `BatchJobStatusStore.cs` | `JobAwareCompletionState v1` projection (no new job model) |
| Eval | EXTEND `tests/integration/contract/Eval/golden-utterances.json` + CI `eval-gate` (`sdap-ci.yml:225`) | resourcefulness + origin-classification families (memory-poisoning deferred) |
| Catalog governance | EXTEND `Chat/OpenAiFunctionSchemaValidator.cs:70` + `PublicContracts/RoutingConsumerTypeHealthCheck.cs:66` + `infra/dataverse/inputschemas/` | triple-twin single-source hoist |
| Retrieval ACL (FR-B-14) | **already enforced** — `RagService.cs:1238` always appends `PrivilegeFilterBuilder`; history-sanitization at `Safety/CrossMatter/ConversationHistorySanitizer.cs` | spike CONFIRMS (likely no gap) |

**Genuinely net-new** (per §11): OutcomeCard component + server trace-read surface; the Record/User two-scope model + generic `(entityType,entityId)` keying + subject-partitioned container + governance envelope atop `MatterMemoryService`; the Context Binder + ContextEnvelope contract; the D-F0 doctrine block + **two** eval families (resourcefulness + origin-classification — memory-poisoning deferred); ADR-041/042.

> **Memory scope refinement (operator review 2026-07-08)**: five scopes → **two active** (Record + User); Record generalized off `sprk_matter` to any entity; User is one general per-user store (not per-matter); store re-partitioned by subject; **hard-governance rules DEFERRED** to a separate project behind an **explicit-only-write floor** (see spec FR-B-01/02/03/08/09/10 + Deferrals). Insights-Engine-as-consumer is the named direction; wiring is a follow-on.

---

## 4. Phase / Wave Breakdown (WBS)

Gate-sequenced. A0 + D-F0 run in parallel at the front (ruling R-3). A0 gates the Completion/Memory/Binder waves. The triple-twin hoist precedes ALL catalog-row tasks.

### Phase 0 — Reconciliation & Discovery  *(FR-P0-01..04)*
- 001 r1 P4-close reconciliation of §10 contingent rows (4/5/6/8/12)
- 002 Measure-first prompt-assembly baseline (per-slice token counts on current assembly)
- 003 Business-slice determinism check (schema-card render: stable ordering, no timestamps/GUIDs)
- 004 Discovery obligations (golden-utterance format, validator extension points, Gate-ledger surface, `dispatchUncertain`=`ElicitationTurnRouter`, Job status surface) — **largely pre-satisfied by pipeline discovery; task confirms + records**

### Phase A0 — Contract-first walking skeleton  *(FR-A0-01..08)* — parallel with Wave J-D-F0
Each: contract + thin reference producer + consumer + contract test in `tests/integration/contract/**`.
- 010 `ComposeDisposition v1` (+ SSE frame) — **published FIRST** (unblocks Compose r2)
- 011 `OutcomeCard v1` (formalizes shipped `UserSummary`/link primitives)
- 012 `GateDecision v2`
- 013 `TraceEvent v1` (names existing `SessionToolChain` markers)
- 014 `JobAwareCompletionState v1` (integrates `JobContract`)
- 015 `ContextEnvelope v1`
- 016 `MemoryItem v1` (+ 14-field envelope)
- 017 Seam-publication ordering + cross-project obligation filing (Compose r2)

### Phase A-infra — before ANY catalog-row task  *(FR-A-01, A-02)*
- 020 **Triple-twin validator hoist** (single authored source → generated/validated mirrors; extend `OpenAiFunctionSchemaValidator`) — **BLOCKS all catalog-row tasks**
- 021 Test-repair (3 SpaarkeAi + 8 AI.Widgets suites; run jest to pin exact reds) — TEST-MODIFYING rigor

### Wave J — G-R2-A Judgment + Friction  *(FR-A1-01..14)*  — D-F0 FIRST (parallel with A0)
- 030 **D-F0 Resourcefulness Doctrine** (strategy meta-prompt, read/write asymmetry, degradation ladder, affordances) — extend `SprkChatAgentFactory.cs:65` directive layer
- 031 D-F0(e) resourcefulness eval family (from note; ≥20 cases + 10-scenario E2E band)
- 032 Confirmation Policy v2 gate engine (tier table + overlays + origin classifier + E-1..E-6) — extend gate stack *(after 020 for catalog risk-factor fields)*
- 033 Origin-classification eval family
- 034 Gate pre-suspend validation (`ValidateChat` before suspend)
- 035 Completion Engine + OutcomeCard across all side-effect paths
- 036 Job-aware completion (JobAwareCompletionState integration; ingestion-parity enforcement)
- 037 UI-action truthfulness (client-ack gating)
- 038 Decision-traceability view + live plan narration + **server ToolChain read surface**
- 039 Progressive render (ADR-037 section-keyed; client-reveal fallback)
- 040 Refusal-affordance links (Document Upload deep-link, host-scoped)
- 041 Capability-discovery READ endpoint
- 042 Cataloged create-matter capability *(after 020)*
- 043 **ADR-041** authoring (Proposed→Accepted at G-R2-A)
- 049 **G-R2-A browser UAT** (operator, spaarkedev1)

### Wave M — G-R2-B Memory  *(FR-B-01..16)*  — gated by A0
- 050 Memory Service — **generalize `MatterMemoryService`→`RecordMemory`** (generic `(entityType,entityId)`) + User scope; **NEW subject-partitioned Cosmos container** (reuse code, not container); decide live-doc migration
- 051 Governance envelope on `MemoryFact` (scope, subject, provenance, sensitivity, expiration, deletionPolicy, retentionClass) + tolerant-reader migration defaults
- 052 Memory governance (retention/expiration, user review/delete surface, record-auth-aligned read, audit, sensitivity) — *litigation-hold DEFERRED*
- 053 Context Binder + ContextEnvelope assembly (generalize the 6 primitives; cache-stable)
- 054 ContextEnvelope token budgets (fix against 002 measurement; breach-fails-eval)
- 055 Caller-contact self-assignment resolution (claims→contact, server-side)
- 056 Portfolio fresh-retrieval bias (Memory.Conversation retrieval policy)
- 057 `memory.write` as a governed side effect — Policy v2 confirm UX + **explicit-only floor** (never model/document-initiated) *(after 020, 032)*
- ~~058 Semantic-retrieval ↔ memory trust boundary~~ — **DEFERRED** (governance project)
- ~~059 Memory-poisoning eval families~~ — **DEFERRED** (governance project)
- 060 Organizational-scope provider interface (read-only inbound)
- 061 Semantic-scope provider interface (over existing AI Search/SPE)
- 062 Workspace-intelligence precursors (next-step chips + record memory)
- 063 Matter-level retrieval ACL verification spike (bounded; likely confirms `RagService.cs:1238`)
- 064 ADR-040 inline size-cap enforcement home (contingent on 001)
- 065 **ADR-042** authoring (Proposed→Accepted at G-R2-B)
- 069 **G-R2-B browser UAT** (operator, spaarkedev1)

### Wave H — G-R2-D Hardening  *(FR-D-01..07)*
- 070 Publish-size verification harness/report (≤60 MB; per-task)
- 071 Eval-suite-green merge gate (all families)
- 072 Cross-satellite seam-fork verification (grep/NetArchTest; no forked AI-internal seam)
- 073 Track-B hygiene (TimeProvider probes; `Refresh-ScopeModelIndex.ps1` drift; dead env keys)
- 074 Audit-container partition re-key (off bare `/tenantId`)
- 075 Legacy workspace tools verdict (contingent on 001)
- 076 Orphan verification (contingent on 001)
- 079 **G-R2-D verification** (CI green + publish-size + seam-fork)

### Wrap-up
- 090 Project wrap-up (`/test-diet`, README→Complete, lessons-learned, `/defer` the named deferrals: Work IQ spike + runtime providers, goal-tracking subsystem, admin dashboards, MCP-server outbound)

---

## 5. Dependencies & Parallelism

- **Front (parallel)**: Phase 0 + A0 + D-F0 (030/031) run together; A0 gates 035/036 (Completion) + 050–054 (Memory/Binder).
- **Hard sequence**: 020 (triple-twin hoist) BEFORE 032, 042, 057, and any `memory.*` catalog-row task.
- **Contingent-on-001**: 064, 075, 076 (re-checked at r1 P4-close state).
- **Gate order**: G-R2-A (Wave J) → G-R2-B (Wave M) can overlap once A0 lands; G-R2-D (Wave H) closes.
- **Cross-project**: 010 (`ComposeDisposition`) + 017 publish FIRST for Compose r2; `/conflict-check` before every BFF PR.
- **Model tiering (CLAUDE.md §8.5)**: 010–017 (contracts), 030/032/035/036/038 (gate/completion/trace), 050–058 (memory/governance), 043/065 (ADRs) → **opus/fable**; catalog-row, test-repair, hygiene, doc tasks → **sonnet**. Assigned per-POML at task-create.

---

## 6. Key Technical Decisions

| Decision | Rationale | Impact |
|---|---|---|
| Formalize, don't rebuild, the Reasoning Runtime | R1's factory/orchestrator/gate are sound | Binder + Completion are collaborators, not replacements |
| EXTEND `MatterMemoryService`, not greenfield | §11 reuse; discovery found a Cosmos memory subsystem | Smaller net-new; reconcile Q4 container reuse |
| Contracts as walking skeletons | Compose r2 unblocked day-one; contracts proven, not paper | Each A0 FR ships a contract test |
| Risk = catalog DATA | ADR-039 one-intent rule | Policy v2 has no runtime LLM risk judgment |
| D-F0 by prompt+eval (not gate) | Strategy-level steering; degradation below side-effect line | Eval family IS the enforcement (031) |

---

## 7. References (discovered resources)

- **ADRs**: 039, 040, 013, 037, 015, 029, 032, 038 (+ standing set 008/009-014/010/016/018/019/028/030/031/036).
- **Code anchors**: see §3 table (`file:line`).
- **Eval**: `tests/integration/contract/Eval/golden-utterances.json`; CI `eval-gate` (`.github/workflows/sdap-ci.yml:225`).
- **Skills**: `jps-action-create`, `jps-validate`, `adr-check`, `code-review`, `task-execute`, `conflict-check`, `context-handoff`, `azure-deploy`/`bff-deploy`.
- **Constraints**: `.claude/constraints/bff-extensions.md` (binding for every BFF task), `.claude/constraints/azure-deployment.md` (publish-size rule).
- **Cross-project**: `projects/spaarkeai-compose-r2/` (seam consumer), `projects/INDEX.md` (hot-path registry).

---

*Generated by `/project-pipeline` Step 2. Task decomposition (Step 3) produces `tasks/*.poml` + `tasks/TASK-INDEX.md`.*
