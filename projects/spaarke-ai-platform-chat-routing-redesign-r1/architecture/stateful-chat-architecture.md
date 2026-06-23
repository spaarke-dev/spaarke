# Stateful Chat Architecture & Component Model

> **Project**: spaarke-ai-platform-chat-routing-redesign-r1
> **Status**: DRAFT v1 — authored 2026-06-21 from R6 session context (full audit findings + verified code references hot)
> **Authoritative for**: WP5 implementation work in the successor project
> **Companions**: `../design.md` (work-package narrative), `../research/research-summary.md` (source findings), `../spec.md` (FRs/NFRs)
> **Source position**: This doc IS the architecture + component model. The design doc has WP5 prose; this doc has the architecture artifact. Spec has the requirements; this doc has the structure.

---

## §1 Purpose & Scope

This document is the **architecture + component model for the stateful chat persistence subsystem** in the chat-routing redesign project. It does NOT cover the full project — see `../design.md` for the broader WP1-6 framing.

**In scope**:
- 6-tier memory model (Working / Session / Matter / User-Org / Retrieval / Audit)
- Component dependency graph + storage architecture + tool surface
- Per-turn prompt assembly + upload pipeline + recall flow
- Pillar 6b workspace-write reliability
- Honest assessment of Insights Engine reuse boundaries (§5)

**Out of scope** (handled in `../design.md` + `../spec.md`):
- WP1 playbook-embeddings index governance
- WP2 file-aware classification (touched here only where it feeds T2 upload)
- WP3 destination metadata wiring
- WP4 CapabilityRouter retirement
- WP6 specialized playbook authoring
- §1.7 stable playbook codes reform

---

## §2 Architectural Principles

The following principles bind every architectural decision in this document. Violations require explicit user sign-off.

### P1. Six tiers, each with its own contract

Memory is NOT one blob. It's 6 distinct tiers with distinct lifecycles, stores, access patterns, and tools. Cross-tier writes go through explicit promotion APIs (`promote_to_matter_memory`) — never via implicit side effects. See §3.

### P2. JIT retrieval over prompt stuffing

Inject identifiers + structured context cards in every prompt; full content via tools on demand. Industry consensus across 8+ products (Anthropic Sept 2025, Cursor, Copilot, Harvey, Glean, Hebbia, Linear, Claude Artifacts, OpenAI Assistants) verified by A3 research. Stuffing file text in every turn:
- Blows the per-turn token budget at >2-3 small docs
- Triggers "lost in the middle" attention degradation (Stanford 2024 TACL)
- Defeats prompt caching (requires stable prefix)
- Costs 4-10x at scale

### P3. Citation-bearing trust model

Precomputed summaries are **not authoritative**. The agent's system prompt frames them explicitly as "upload-time approximations" and instructs the LLM to call `recall_session_file` with `requireCitations: true` for any legally-precise answer. This is a load-bearing principle for legal-domain accuracy.

### P4. Layered context cards (not 1-line summaries)

Per-file static-prefix entries are **structured context cards** (~150-250 tokens each) carrying: id, fileName, contentType, classifiedDocType + confidence, precomputed summary (NOT authoritative), detected sections, tables, citations referenced in this conversation, recently-discussed flag. NOT 1-line summaries — user's explicit guidance ("1-line summaries every turn is too thin for Spaarke").

### P5. Wire-not-build for existing R6 infrastructure

The R6 Pillar 7 work shipped:
- `MatterMemoryService` + `IMatterMemoryService` (Cosmos `memory`)
- `PinnedContextRepository` (Cosmos `memory`, doc-type discriminator)
- `PinnedContextRecallService` (embedding similarity)
- `MemoryCompositionService` (4-layer composition, 8K budget enforcement)
- `SummarizationCompressionService`
- `PromptBudgetTracker`
- All 7 services DI-registered + tested

Plus the FR-45 wiring is VERIFIED: `MatterMemoryService.ToSystemPromptFragmentAsync` called from `PlaybookChatContextProvider.cs:627`.

**The build is wrapper tools + prompt-assembly refactor + upload enrichment, NOT new storage.** The 6-tier model is the architecture; the build is small.

### P6. Privacy by default

Tier 6 (audit) is append-only and never read by the agent for memory composition (only by UI for transparency). Tier 2 NEVER auto-promotes to Tier 3 without explicit user action via `promote_to_matter_memory(approvalRequired: true)`. Tier 4 is bound by ADR-013 facade boundary — chat code does NOT directly mutate user/org settings.

### P7. ADR-015 audit hygiene

All telemetry / context.* events carry: tier, operation, tenantId, sessionId, durationMs. They do NOT carry: user message text, file contents, recall results, memory facts. The execution-trace widget (Pillar 6c) renders the metadata-only event stream for transparency.

---

## §3 The 6-Tier Memory Model

### §3.1 Tier definitions

| Tier | Name | Purpose | Lifetime | Composed from | Tools |
|---|---|---|---|---|---|
| **T1** | Working context | The active LLM call's system prompt + history. Composed each turn. | Per-turn (rebuilt) | T2-T5 (read-through) | n/a (it IS the prompt) |
| **T2** | Session memory | Uploaded files (metadata + summaries + manifests), generated playbook outputs, user decisions in this conversation, pending plans | Session (24h Redis hot; 90d Cosmos warm) | n/a (T2 is the seed) | `list_session_files`, `get_file_manifest`, `recall_session_file`, `write_session_memory` |
| **T3** | Matter memory | Durable matter-level facts: prior work product, concessions, deadlines, strategy notes, counsel rules | Matter lifetime (durable until explicit close) | Cosmos `memory` container | `retrieve_matter_memory`, `promote_to_matter_memory` |
| **T4** | User/org memory | User preferences (writingStyle, summaryLength, citationStyle), org templates, pinned playbooks, outside counsel rules | User/org lifetime (durable) | Dataverse `sprk_userpreferences` + `PinnedContextItem` (Cosmos) | `get_user_preferences`, `get_org_templates` (read-only) |
| **T5** | Retrieval memory | Searchable indexes for session files + knowledge sources | Index-managed | Azure AI Search indexes | `recall_session_file(mode='section')`, `document_search`, `knowledge_retrieval` |
| **T6** | Audit memory | What was used / retrieved / cited / decided | Retention-policy (no TTL on `audit` container; immutable policy) | Cosmos `audit` + App Insights + `context.*` events | (read-only; UI surfaces via ExecutionTraceWidget) |

### §3.2 Cross-tier separation rules (binding)

1. **T1 composes from T2-T5 each turn**; nothing else writes to T1 (it's per-turn ephemeral).
2. **T2 → T3 promotion is explicit**, via `promote_to_matter_memory` with `approvalRequired: true` (user-approval UI prompt).
3. **T4 is bound by ADR-013 facade boundary** — chat code reads via `get_user_preferences` / `get_org_templates`; writes go through user-settings UI (out of scope here).
4. **T6 is append-only** — agent never reads T6 for memory composition. Only the ExecutionTraceWidget surfaces it for UI transparency.
5. **No tier writes to a tier of higher lifetime without explicit promotion** — Session can't write to Matter except via promotion; Matter can't write to User-Org.

### §3.3 Lifecycle states per tier

```
T1 Working context:    [Compose] → [LLM call] → [Discard]    (per-turn)
T2 Session memory:     [Create] → [Active] → [Persist] → [Cleanup]  (24h sliding / 90d warm)
T3 Matter memory:      [Create via promotion] → [Active] → [Edit] → [Archive]  (durable)
T4 User/org memory:    [User authors] → [Read-only from chat]  (durable)
T5 Retrieval memory:   [Indexed at upload] → [Queryable] → [Reindexed on change]  (index-managed)
T6 Audit memory:       [Emit] → [Persist immutably]  (append-only)
```

---

## §4 Component Architecture

### §4.1 Per-tier component map

```
┌─ T1 Working context ─────────────────────────────────────────────────────┐
│ SprkChatAgentFactory (composes per-turn prompt; reads T2-T5)             │
│ PlaybookChatContextProvider (line 130-154 persona; line 627 matter wire) │
│ MemoryCompositionService (4-layer composition; existing R6 task 067)     │
│ PromptBudgetTracker (8K static + 5K dynamic budget enforcement)          │
└──────────────────────────────────────────────────────────────────────────┘
              │ reads
              ▼
┌─ T2 Session memory ──────────────────────────────────────────────────────┐
│ ChatSessionManager (lifecycle)                                            │
│ ChatHistoryManager (turn-by-turn message append)                          │
│ SessionPersistenceService (Cosmos sessions container; write-through)      │
│ SessionRestoreService (Cosmos → Redis on session resume)                  │
│ SessionSummarizationService (25-msg / 8K threshold)                       │
│ NEW: SessionFileEnrichmentService (upload-time classify + summarize +    │
│       manifest extraction; writes to ChatSession.UploadedFiles)           │
│ NEW: RecentlyDiscussedTracker (Redis hash {sessionId}:fileDiscussed:*)    │
└──────────────────────────────────────────────────────────────────────────┘
              │ reads / promotes
              ▼
┌─ T3 Matter memory ───────────────────────────────────────────────────────┐
│ MatterMemoryService (IMatterMemoryService — existing R6 task 068)         │
│ - GetFactsAsync(tenantId, matterId) — read                                │
│ - AppendFactAsync(tenantId, matterId, fact) — append-only with ETag       │
│ - ToSystemPromptFragmentAsync (≤500 tokens; FR-45 wired in T1)            │
│ Cosmos memory container; doc id `{tenantId}_{matterId}`                   │
│ NEW: MatterMemoryPromotionService (handles user-approval workflow)        │
└──────────────────────────────────────────────────────────────────────────┘
              │ reads (read-only from chat)
              ▼
┌─ T4 User/org memory ─────────────────────────────────────────────────────┐
│ UserPreferencesService (reads sprk_userpreferences Dataverse entity)      │
│ PinnedContextRepository (Cosmos memory; pin-type discriminator)           │
│ - user-preference / system-rule / matter-fact pins                        │
│ OrgTemplatesService (reads org-level template entities)                   │
└──────────────────────────────────────────────────────────────────────────┘
              │ queries
              ▼
┌─ T5 Retrieval memory ────────────────────────────────────────────────────┐
│ RagService (existing; queries spaarke-session-files index)                │
│ KnowledgeRetrievalHandler (per-knowledge-source RAG)                      │
│ DocumentSearchHandler (vector search over knowledge index)                │
│ PinnedContextRecallService (embedding similarity over pins; R6 task 066)  │
│                                                                            │
│ AI Search indexes:                                                         │
│ - spaarke-session-files (session-scoped chunks; primary for T2 recall)    │
│ - spaarke-files-index (matter-bound document chunks)                      │
│ - spaarke-rag-references (knowledge sources)                              │
│ - spaarke-records-index (entity records)                                  │
│ - spaarke-knowledge-index-v2                                              │
│ - discovery-index                                                         │
│ NOT spaarke-insights-index (Insights-domain; see §5)                      │
└──────────────────────────────────────────────────────────────────────────┘
              │ writes (append-only)
              ▼
┌─ T6 Audit memory ────────────────────────────────────────────────────────┐
│ AuditLogService (Cosmos audit container; immutable policy)                │
│ IContextEventEmitter (context.* PaneEventBus emissions)                   │
│ App Insights telemetry (tier-1 fields only per ADR-015)                   │
│                                                                            │
│ Consumers (read):                                                          │
│ - ExecutionTraceWidget (Context pane; Pillar 6c — already shipped)        │
│ - Compliance queries (Cosmos audit container; out of chat scope)          │
└──────────────────────────────────────────────────────────────────────────┘
```

### §4.2 Component dependency graph

```
SprkChatAgentFactory
├─→ PlaybookChatContextProvider
│   ├─→ MatterMemoryService.ToSystemPromptFragmentAsync (T3 read)
│   ├─→ SessionPersistenceService (T2 read)
│   └─→ UserPreferencesService (T4 read)
├─→ MemoryCompositionService
│   ├─→ ChatSession.UploadedFiles (T2)
│   ├─→ MatterMemoryService.GetFactsAsync (T3)
│   ├─→ PinnedContextRecallService (T4 + T5 — pinned items, ranked)
│   ├─→ SessionSummarizationService (T2 compressed history)
│   └─→ PromptBudgetTracker (8K enforcement)
└─→ ToolHandlerFactory
    ├─→ RecallSessionFileHandler (T2 + T5)
    ├─→ ListSessionFilesHandler (T2 read)
    ├─→ GetFileManifestHandler (T2 read)
    ├─→ RetrieveMatterMemoryHandler (T3 read via MatterMemoryService)
    ├─→ WriteSessionMemoryHandler (T2 write)
    ├─→ PromoteToMatterMemoryHandler (T2 → T3 via MatterMemoryPromotionService)
    ├─→ GetUserPreferencesHandler (T4 read)
    ├─→ GetOrgTemplatesHandler (T4 read)
    ├─→ GetWorkspaceTabStateHandler (T2 workspace tabs)
    ├─→ UpdateWorkspaceTabHandler (T2 workspace tab edit; Pillar 6b — exists)
    ├─→ SendWorkspaceArtifactHandler (T2 workspace tab create; exists)
    └─→ CloseWorkspaceTabHandler (T2 workspace tab close; exists)

Upload pipeline (per file):
ChatDocumentEndpoints
└─→ SessionFileEnrichmentService (NEW)
    ├─→ FileClassificationService (cheap LLM call: doc type)
    ├─→ FileSummarizationService (gpt-4o-mini 1-paragraph)
    ├─→ FileManifestExtractor (sections/tables/pageCount/language)
    ├─→ RagService.IndexAsync (T5 — spaarke-session-files)
    └─→ SessionPersistenceService.UpdateUploadedFilesAsync (T2 persist)
```

### §4.3 Existing R6 components leveraged (no new build)

Per Insights/Cosmos audit:

| Component | Where | Reuse |
|---|---|---|
| `MatterMemoryService` + interface | `Services/Ai/Memory/MatterMemoryService.cs` | Direct read in T1 composition; T3 storage substrate |
| `PinnedContextRepository` | `Services/Ai/Memory/PinnedContextRepository.cs` | T4 storage for pins (Cosmos memory container) |
| `PinnedContextRecallService` | `Services/Ai/Memory/PinnedContextRecallService.cs` | Embedding-similarity ranker (T4 + T5) |
| `MemoryCompositionService` | `Services/Ai/Memory/MemoryCompositionService.cs` | T1 composition seam (verify FR-45 unification with `PlaybookChatContextProvider`) |
| `SummarizationCompressionService` | `Services/Ai/Memory/SummarizationCompressionService.cs` | T2 sliding-window compression |
| `PromptBudgetTracker` | `Services/Ai/Memory/PromptBudgetTracker.cs` | 8K + 5K budget enforcement |
| `SessionPersistenceService` | `Services/Ai/Sessions/SessionPersistenceService.cs` | T2 Cosmos write-through (extend with file enrichment fields per `SaveTabsAsync` pattern) |
| `SessionSummarizationService` | `Services/Ai/Sessions/SessionSummarizationService.cs` | T2 history compression (already at 25-msg / 8K threshold) |
| `AuditLogService` | `Services/Ai/Audit/AuditLogService.cs` | T6 write target (already complete) |
| `RagService` | `Services/Ai/RagService.cs` | T5 wrapper (no extension needed; just new tools call it) |
| `WorkspaceStateService` | `Services/Workspace/WorkspaceStateService.cs` | T2 workspace state (R6 task 051 hybrid persistence) |
| Pillar 6b handlers | `Services/Ai/Handlers/*WorkspaceTabHandler.cs` | T2 workspace write — already shipped; just need to be reliably available |
| `ManagePinnedContextHandler` | `Services/Ai/Handlers/ManagePinnedContextHandler.cs` | T4 pin CRUD (already shipped) |

### §4.4 New components introduced

| Component | Tier | Purpose | Storage |
|---|---|---|---|
| `SessionFileEnrichmentService` | T2 | Upload-time orchestration: classify + summarize + manifest | Reads file content; writes to `ChatSession.UploadedFiles[]` |
| `FileClassificationService` | T2 | Cheap LLM call → `documentType` (e.g., "NDA", "patent", "invoice") | Stateless |
| `FileSummarizationService` | T2 | gpt-4o-mini → 1-paragraph `precomputedSummary` (NOT authoritative) | Stateless |
| `FileManifestExtractor` | T2 | Detect sections / tables / pageCount / language from extracted text | Stateless |
| `RecentlyDiscussedTracker` | T2 | Track last-3-turn file mention flag | Redis hash `{sessionId}:fileDiscussed:{fileId}` |
| `MatterMemoryPromotionService` | T3 | User-approval workflow for T2 → T3 promotion | Cosmos memory (writes via existing `MatterMemoryService.AppendFactAsync`) |
| `LayeredContextCardBuilder` | T1 | Produces structured per-file context card (~150-250 tok) for static prefix | Stateless; reads T2 |
| `TrustFrameInstructionInjector` | T1 | Adds "NOT authoritative" warning + `requireCitations: true` rule to persona | Stateless |
| 8 new tool handlers | T2-T4 | See §8 Tool Surface | Each wraps existing storage |

### §4.5 Components NOT to build (explicit decisions)

| Would-be component | Reason NOT to build | Alternative |
|---|---|---|
| `sprk_matterfacts` Dataverse entity | `MatterMemoryService` already covers matter facts in Cosmos `memory` (doc id `{tenantId}_{matterId}`). Adding a Dataverse entity would duplicate storage. | Continue using `MatterMemoryService` |
| New chat-memory AI Search index | Existing `spaarke-session-files` + `spaarke-files-index` cover the recall surface. Adding a new index = more infra, more sync cost, no domain win. | Wrap existing indexes via new tool handlers |
| MultiIndexComposer-derived memory composer | `MemoryCompositionService` already does layered composition with time semantics. MultiIndexComposer does knowledge-tier blending (different semantics). | Use `MemoryCompositionService` (extend if needed; do NOT replace with MultiIndexComposer) |
| Shared envelope type with Insights | Memory artifacts have different fields (factId, source, confidence) than Insights artifacts (dimensions[], playbookName). Sharing the type forces awkward optional fields. | Pattern-level reuse only (see §5) |
| New audit container | `AuditLogService` + Cosmos `audit` container already provide immutable, append-only audit. | Reuse |
| Replace `sprk_aichatmessage` with fixed-impl repository | The placeholder stubs are technical debt. Don't fix them; retire the repository to write-only audit role (`IChatAuditRepository`). See §11.4. | Retire |

---

## §5 Insights Engine — Honest Relationship Assessment

> **This section is load-bearing per user directive (2026-06-21)**: "we do not want to force-fit Insights Engine and Chat Persistence into a single solution approach and architecture and components if not truly complimentary or reusable."

The Insights audit (Audit 5, 2026-06-19) identified several Insights Engine components as potentially leverageable for the 6-tier memory architecture. Re-examining each WITH critical scrutiny — not assumption of reusability — produces a more honest map.

### §5.1 What overlaps semantically — SHORT LIST

After honest review, the genuine overlaps are **pattern-level only**, not type-level or service-level:

1. **Versioned envelope pattern with citations** — Insights uses `{schemaVersion, body, citations[], generatedAt, playbookName, tenantId, dimensions[]}` for InsightArtifact. Memory promotion records have a similar shape (`{factId, fact, source, confidence, createdAt, createdBy, sessionId?}`). **The pattern of versioned provenance is universal**; reuse the design principle, not the type.

2. **Redis hot-tier with TTL pattern** — Insights uses `IInsightsPlaybookExecutionCache` (Redis, per-topic TTL). Chat memory needs Redis for `RecentlyDiscussedTracker` and session-file summaries. **This is the standard distributed-cache pattern**, not Insights-specific. Calling it "borrowing from Insights" overstates the relationship.

3. **Write-through to Dataverse for compliance trail** — Insights writes diagnostic envelopes to `sprk_matter.sprk_performancesummary`. Chat audit writes to Cosmos `audit` container with immutable policy. The PATTERN of compliance-trail write-through is shared; the implementations are different and should stay different.

That's the honest list. Three pattern-level overlaps.

### §5.2 What does NOT overlap — LONGER LIST

The following Insights components were flagged in the audit as candidates for chat memory reuse. **None of them survive critical scrutiny.**

#### §5.2.1 `spaarke-insights-index` is NOT a memory store

**Audit said**: "wrap with retrieval tools" for T5.

**Honest assessment**: The Insights index holds **Observations** (structured findings from documents: "this contract has a 2-year non-solicit") and **Precedents** (SME-confirmed pattern statements). These are **derived knowledge artifacts**, not chat memory.

Chat memory T5 recall needs:
- File chunks from session uploads (use `spaarke-session-files`)
- Matter-bound document chunks (use `spaarke-files-index`)
- Pinned context items (already separate via `PinnedContextRepository`)

**Forcing chat memory through `spaarke-insights-index`** would:
- Pollute Insights with chat-specific records (artifact-type bloat)
- Confuse retrieval ranking (mixing diagnostic findings with conversational memory)
- Couple two unrelated domains (changes to one affect the other)
- Force the Insights team to support chat-memory queries

**Decision**: DO NOT use `spaarke-insights-index` for chat memory. Keep boundaries clean. Chat memory uses its own indexes.

#### §5.2.2 `MultiIndexComposer` is NOT a memory composer

**Audit said**: "already in use; merges multi-tier knowledge blocks under a single output (composable for memory layer assembly)."

**Honest assessment**: `MultiIndexComposer.Merge` blends L1 (reference) + L2 (customer docs) + L3 (entity context) knowledge tiers for synthesis. The semantics are **KNOWLEDGE-TIER blending**.

Memory composition (T1) blends recent verbatim history + compressed mid-distance + retrieved old + pinned forever. The semantics are **TIME-DEPENDENT layered injection** — the dimensions are RECENCY and IMPORTANCE, not knowledge-source-tier.

`MemoryCompositionService` (R6 task 067) ALREADY does time-dependent memory composition with FR-42 pinned-never-drops invariant. It's the right tool for memory.

**Forcing MemoryCompositionService through MultiIndexComposer** would:
- Lose time semantics (no recency dimension in MultiIndexComposer)
- Lose FR-42 pinned-never-drops invariant
- Force memory composition through an interface that doesn't fit

**Decision**: DO NOT use `MultiIndexComposer` for memory composition. Use `MemoryCompositionService`. They're cousins (both "merge multiple blocks") but different semantics.

#### §5.2.3 `InsightsOrchestrator` is NOT a memory orchestrator

**Audit said**: (implicitly via "leverage existing components")

**Honest assessment**: `InsightsOrchestrator` drives Insights playbook synthesis with caching + ingest + embedding generation. It's a **synthesis pipeline orchestrator** for documents → derived knowledge.

Memory has no equivalent orchestration concern. `SprkChatAgentFactory` orchestrates per-turn prompt assembly; `SessionFileEnrichmentService` orchestrates upload-time enrichment. These are different concerns.

**Decision**: DO NOT use `InsightsOrchestrator` for chat memory orchestration. Different domain.

#### §5.2.4 `EvidenceSufficiencyNode` is NOT a memory-recall gate

**Audit said**: (implicitly via "envelope pattern reusable")

**Honest assessment**: `EvidenceSufficiencyNode` (action type 100) is a predicate-based gate that aborts Insights synthesis when evidence is insufficient (e.g., `kpiAssessments.min=2` for matter-health). It's a **playbook-execution decision gate**.

Memory recall has a related but DIFFERENT concern: when the agent calls `recall_session_file` and the query returns no matching sections, the tool should return `{ scope_truncated: true, truncation_reason: "no_match" }` cleanly. This is **tool-level error handling**, not a playbook gate.

The Insights pattern of "abort gracefully when evidence is insufficient" is INSPIRATION — worth understanding — but the implementation is in the recall tool's error path, not via the `EvidenceSufficiencyNode`.

**Decision**: DO NOT use `EvidenceSufficiencyNode` for memory recall. Build clean error returns in the recall tools instead.

#### §5.2.5 `GroundingVerifyNode` is genuinely interesting but still NOT direct reuse

**Audit said**: (in passing)

**Honest assessment**: `GroundingVerifyNode` (action type 70) strips unverified citations from Insights synthesis output. This is a NOVEL pattern: verify the citations match the source chunks before persisting.

Chat memory has an analogous concern: when `recall_session_file` returns content with citations, can we verify the citations match the source? **Yes, this would be valuable** — but it's NEW capability, not Insights reuse. The pattern is portable; the implementation should be specialized for memory's needs (real-time verification during recall vs Insights' post-synthesis verification).

**Decision**: Build memory-specific citation verification INSPIRED by `GroundingVerifyNode` pattern. NOT direct reuse — different timing, different consumer.

#### §5.2.6 `sprk_matter.sprk_performancesummary` is INSIGHTS-OWNED

**Audit said**: "purpose-specific (Insights health diagnostic) and should NOT be repurposed as a generic matter-memory store."

**Honest assessment**: AGREED — this is the audit being honest. The field holds a 7-dimension diagnostic envelope written by `matter-health-single` playbook's `persistEnvelope` node. Repurposing it for chat memory would:
- Conflict with Insights writes (race conditions)
- Force chat memory to fit the Insights envelope shape
- Couple chat memory to Insights' write cadence

**Decision**: DO NOT use `sprk_matter.sprk_performancesummary` for matter memory. `MatterMemoryService` (Cosmos `memory`) is the right T3 store.

### §5.3 Pattern-level reuse vs type-level reuse vs categorical mismatch

| Insights concept | Reuse type | Decision |
|---|---|---|
| Versioned envelope with citations | Pattern-level | Adopt the design principle; do NOT share type |
| Redis hot-tier with TTL | Pattern (standard cache pattern) | Adopt standard pattern; do NOT frame as "Insights borrow" |
| Write-through to compliance store | Pattern-level | Adopt the principle (we use Cosmos `audit`); do NOT share `sprk_performancesummary` |
| `spaarke-insights-index` retrieval | Categorical mismatch | DO NOT use for chat memory |
| `MultiIndexComposer` merge | Categorical mismatch (different semantics) | DO NOT use for memory composition |
| `InsightsOrchestrator` orchestration | Categorical mismatch (different domain) | DO NOT use for chat orchestration |
| `EvidenceSufficiencyNode` gating | Categorical mismatch (playbook vs tool error path) | Build clean recall-tool error returns instead |
| `GroundingVerifyNode` citation verification | Inspiration only | Build memory-specific verification, don't reuse type/service |
| `sprk_matter.sprk_performancesummary` | Insights-owned (DO NOT TOUCH) | Use `MatterMemoryService` for T3 |
| `LiveFactResolver (INS-FACT)` | Insights-specific (resolves matter scalar facts) | Could be used to enrich matter context in T1 — but optional and orthogonal |

### §5.4 The clean boundary

**Where Insights ends, chat memory begins**:

| Domain | Insights Engine | Stateful Chat |
|---|---|---|
| **Primary purpose** | Derived knowledge synthesis (cost predictions, matter health) | Conversational memory across turns/sessions |
| **Authoring model** | Pre-defined playbooks producing structured outputs | User-driven via chat + agent-driven via tools |
| **Storage primary** | Dataverse field + AI Search index for queryable observations | Cosmos containers for live state + AI Search indexes for recall |
| **Consumers** | Matter form widgets + UI dashboards | Chat agent prompt assembly + Pillar 6c trace widget |
| **Retrieval pattern** | Vector + structured filter (predicate, scope) | Tool-call from agent (recall_session_file, retrieve_matter_memory) |
| **Mutability** | Append-only insights | Promotion, edit, delete affordances |
| **Cache key** | (topic, subject) tuple with topic-registry TTL | (sessionId, fileId) tuples; tier-specific TTL |

**The architectural rule for this project**: each system stays in its own lane. Cross-system reuse happens at the **pattern level** (versioned envelopes, Redis caching, write-through to compliance) — not at the type, service, or storage level.

### §5.5 What this means in practice

For the implementation team:
- **DO**: Use `MatterMemoryService` (Cosmos `memory`), `MemoryCompositionService`, `PinnedContextRepository`, `RagService`, `AuditLogService` — these are the right components for memory.
- **DO**: Build new components per §4.4 (SessionFileEnrichmentService, FileClassificationService, etc.).
- **DO NOT**: Try to use `MultiIndexComposer`, `InsightsOrchestrator`, `EvidenceSufficiencyNode`, `GroundingVerifyNode`, `spaarke-insights-index`, or `sprk_matter.sprk_performancesummary` for chat memory.
- **DO**: Cite this section when an engineer or reviewer asks "why aren't we reusing X from Insights?" — the answer is in §5.2.

---

## §6 Data Flow Patterns

### §6.1 Upload pipeline (T2 + T5 enrichment)

```
User uploads file via paperclip (or drag-drop)
    │
    ▼
Frontend: client-side text extraction (PDF.js / mammoth.js / raw read)
    │  multipart/form-data POST: { filename, contentType, textContent }
    ▼
BFF: ChatDocumentEndpoints.UploadDocumentAsync
    │
    ├─→ Validate (size, MIME, total-text budget)
    │
    ├─→ [PARALLEL] SessionFileEnrichmentService.EnrichAsync (NEW):
    │       │
    │       ├─→ FileClassificationService.ClassifyAsync (gpt-4o-mini)
    │       │   Returns: { documentType, confidence }
    │       │
    │       ├─→ FileSummarizationService.SummarizeAsync (gpt-4o-mini)
    │       │   Returns: precomputedSummary (≤120 chars, marked "NOT authoritative")
    │       │
    │       └─→ FileManifestExtractor.ExtractAsync (deterministic)
    │           Returns: { sections[], tables[], pageCount, language }
    │
    ├─→ [PARALLEL] RagService.IndexAsync (existing)
    │       Chunks text → embeds → upserts into spaarke-session-files
    │       Returns: SearchDocumentIdsCsv
    │
    ├─→ SessionPersistenceService.UpdateUploadedFilesAsync (NEW method)
    │       Persists enriched ChatSessionFile to Redis hot + Cosmos warm
    │
    └─→ Response: { fileId, summary, docType, confidence }

Total latency target: ~2-4s for a 50-page doc (parallel paths dominate)
Classify + summarize: ~500ms (gpt-4o-mini parallel calls)
Chunking + embedding: ~1500-3500ms (dominant)
Manifest extraction: ~50ms (regex-based)
Cosmos write: ~50ms
```

### §6.2 Per-turn prompt assembly (T1 composition)

```
User message arrives → ChatEndpoints.SendMessageAsync
    │
    ▼
SprkChatAgentFactory.CreateAgentAsync
    │
    ├─→ [STATIC PREFIX — cacheable, ~6K tokens]
    │   │
    │   ├─→ PlaybookChatContextProvider.ProvideAsync
    │   │       │
    │   │       ├─→ Persona via IScopeResolverService.ResolvePersonaForChatAsync
    │   │       │   (R6 Pillar 1 — verified wired at line 130-154)
    │   │       │
    │   │       └─→ Matter memory via MatterMemoryService.ToSystemPromptFragmentAsync
    │   │           (R6 FR-45 — verified wired at line 627)
    │   │
    │   ├─→ TrustFrameInstructionInjector (NEW)
    │   │   Adds: "Precomputed summaries are NOT authoritative; verify via recall."
    │   │
    │   ├─→ ToolDefinitionsAssembler
    │   │   Tools from §8 surface (filtered by routing layer + always-on memory tools)
    │   │
    │   ├─→ WorkspaceDigestBuilder
    │   │   Tab list + types + 1-line status from WorkspaceStateService
    │   │
    │   ├─→ LayeredContextCardBuilder (NEW)
    │   │   Per-file structured card from ChatSession.UploadedFiles enriched fields
    │   │
    │   ├─→ UserPreferencesSnapshot
    │   │   Cached read from sprk_userpreferences (T4)
    │   │
    │   └─→ GlossaryInjector (stable legal-domain terms)
    │
    ├─→ [DYNAMIC SUFFIX — not cached, ~5K tokens]
    │   │
    │   ├─→ MemoryCompositionService.ComposeAsync
    │   │   (existing R6 task 067 — 4-layer time-dependent composition)
    │   │   │
    │   │   ├─→ recent verbatim (last N turns)
    │   │   ├─→ compressed mid-distance summary (SummarizationCompressionService)
    │   │   ├─→ retrieved-similar (PinnedContextRecallService — embedding rank)
    │   │   └─→ pinned (FR-42 never drops)
    │   │
    │   ├─→ Tool outputs from prior tool calls this turn
    │   │
    │   └─→ User message text
    │
    └─→ PromptBudgetTracker.Validate (enforces 8K static + 5K dynamic)

LLM call (Azure OpenAI Chat Completions with prompt cache)
    │
    └─→ Cache hit on static prefix → 50% discount + 80% faster TTFT
```

### §6.3 Recall flow (tool → storage → response)

```
LLM decides to call recall_session_file
    │
    ▼
RecallSessionFileHandler.ExecuteChatAsync
    │
    ├─→ Validate args (fileId in session; purpose in enum; scope in enum)
    │
    ├─→ Mode dispatch:
    │   │
    │   ├─ Mode='summary'
    │   │   │
    │   │   └─→ Read ChatSession.UploadedFiles[fileId].SummaryText from Redis
    │   │       (5ms; cached at upload time)
    │   │
    │   ├─ Mode='section'
    │   │   │
    │   │   └─→ RagService.SearchAsync (spaarke-session-files)
    │   │       Filter: session_file_id = fileId
    │   │       Query: sectionQuery from args
    │   │       TopK: 3 chunks
    │   │       (~150ms)
    │   │
    │   └─ Mode='full'
    │       │
    │       ├─ If full text ≤8K tokens: return all chunks concatenated (50ms)
    │       └─ Else: return summary + first 2K chars + "ask specific question" (truncation_reason)
    │
    ├─→ Format response with citations:
    │   { content: string,
    │     citations: [{page, paragraph?, section?, text}],
    │     scope_truncated: bool,
    │     truncation_reason: string }
    │
    ├─→ Emit context.* event (T6 audit):
    │   context.tool_call_completed {
    │     tool: "recall_session_file",
    │     mode, scope, fileId (deterministic ID — Tier 1 safe per ADR-015)
    │   }
    │
    └─→ Mark file as "recently discussed" (RecentlyDiscussedTracker)
        Redis SET {sessionId}:fileDiscussed:{fileId} = currentTurn
```

### §6.4 Promote-to-matter flow (T2 → T3 with approval)

```
LLM calls promote_to_matter_memory(fact, source, approvalRequired: true)
    │
    ▼
PromoteToMatterMemoryHandler.ExecuteChatAsync
    │
    ├─→ Validate (fact ≤500 chars; source non-empty)
    │
    ├─→ MatterMemoryPromotionService.QueueForApproval (NEW)
    │   │
    │   ├─→ Write pending record to Cosmos memory:
    │   │   { id: "promotion-pending_{guid}",
    │   │     documentType: "matter-memory-promotion",
    │   │     tenantId, matterId, sessionId,
    │   │     fact, source, confidence,
    │   │     createdAt, status: "pending_approval" }
    │   │
    │   └─→ Emit memory.* event (channel added by ADR-030 v2 amendment 2026-06-21):
    │       memory.promotion_pending {
    │         promotionId, factSummary (80-char preview), matterId, sessionId
    │       }
    │
    ▼
Frontend: ContextPaneController subscribes via usePaneEvent('memory', handler)
    │
    └─→ Render approval notification with Accept / Reject buttons
        │
        ├─ User clicks Accept:
        │   │  POST /api/memory/promotions/{id}/approve
        │   │
        │   ▼
        │   MatterMemoryPromotionService.ApproveAsync
        │   │
        │   ├─→ MatterMemoryService.AppendFactAsync (existing)
        │   │   Cosmos memory, doc id {tenantId}_{matterId}
        │   │
        │   ├─→ Update Cosmos pending record: status: "approved"
        │   │
        │   ├─→ Emit memory.promotion_resolved { promotionId, decision: 'approved', factId }
        │   ├─→ Emit memory.fact_promoted { factId, matterId, source }
        │   │
        │   └─→ Emit context.decision_made { decision: "promotion_approved" }   (audit, ADR-015 tier-1 safe)
        │
        └─ User clicks Reject:
            │  POST /api/memory/promotions/{id}/reject
            │
            ▼
            Update Cosmos pending record: status: "rejected"
            Emit memory.promotion_resolved { promotionId, decision: 'rejected' }
            Emit context.decision_made { decision: "promotion_rejected" }   (audit, ADR-015 tier-1 safe)

Tier 2 → Tier 3 promotion is ALWAYS user-mediated. No silent auto-promotion.
```

### §6.5 Workspace state read/write (T1 + Pillar 6b)

```
[READ — every turn for T1 composition]

SprkChatAgentFactory.CreateAgentAsync
    │
    └─→ WorkspaceStateService.GetTabsAsync (Redis hot-tier; Cosmos warm fallback)
        │
        ├─→ Filter tabs.where(visibleToAssistant === true)
        │
        ├─→ Per tab: invoke widget's getAgentVisibleState() if registered
        │   (Pillar 9 — server-side TryDeriveVisibleState in SprkChatAgentFactory.cs:2173)
        │
        └─→ WorkspaceDigestBuilder emits per-tab summary line (~50 tok)
            Total: ≤800 tokens for workspace digest section

[WRITE — agent-initiated tab mutation; Harvey/Artifacts targeted-edit pattern]

LLM calls update_workspace_tab(tabId, edit: { old, new })
    │
    ▼
UpdateWorkspaceTabHandler.ExecuteChatAsync (existing Pillar 6b)
    │
    ├─→ Q8 conflict check:
    │   │
    │   └─→ WorkspaceStateService.GetTabAsync(tabId)
    │       Compare incoming tab.lastUserEditAt
    │       │
    │       ├─ If user edited since agent's last read → REFUSE with re-read prompt
    │       └─ Else → proceed
    │
    ├─→ Apply targeted edit (deterministic)
    │   Exact string match for 'old' → replace with 'new'
    │
    ├─→ WorkspaceStateService.UpdateTabAsync (Redis + Cosmos write-through)
    │
    ├─→ Emit workspace.* event:
    │   workspace.tab_edited { tabId, editType: "targeted" }
    │
    └─→ Return success → LLM can call get_workspace_tab_state(tabId) to verify
```

---

## §7 Storage Architecture

### §7.1 Cosmos containers (existing — no new containers)

Per Insights audit verification:

| Container | Partition | TTL | Doc types | Used by |
|---|---|---|---|---|
| `sessions` | `/tenantId` | 90d | `ChatSession` (single doc per session) | `SessionPersistenceService` — T2 warm |
| `memory` | `/tenantId` | 90d | Multiple via doc-type discriminator: `matter-memory_{matterId}`, `pinned-context_{pinId}`, `workspace-tab_{tabId}`, `promotion-pending_{promotionId}` (NEW) | `MatterMemoryService`, `PinnedContextRepository`, `WorkspaceStateService`, `MatterMemoryPromotionService` (NEW) — T3 + T4 + T2 workspace |
| `audit` | `/tenantId` | NONE (immutable policy) | Audit events | `AuditLogService` — T6 |
| `prompts` | `/ownerId` | 90d | Personal + Team prompts | `PromptLibraryService` (orthogonal to chat) |
| `feedback` | `/tenantId` | NONE | User feedback | `FeedbackService` (orthogonal) |

**Reuses the discriminator pattern from R6 task 051 (`SaveTabsAsync`) for new doc types.** No new containers needed.

### §7.2 Dataverse entities (audit decisions)

| Entity | Status | Decision |
|---|---|---|
| `sprk_userpreferences` | Existing (SmartTodo + LegalWorkspace consume) | T4 read-only from chat via `UserPreferencesService` |
| `sprk_aichatmessage` | Broken placeholder (5 `Task.CompletedTask` no-ops; 10K char cap) | **RETIRE** to write-only audit role via `IChatAuditRepository`; Cosmos `audit` is the sole reader for compliance queries |
| `sprk_matter.sprk_performancesummary` | Insights-owned (7-dim diagnostic) | **DO NOT TOUCH** — Insights writes here; chat memory uses Cosmos `memory` |
| (none) | `sprk_matterfacts` — would-be matter facts entity | **DO NOT CREATE** — `MatterMemoryService` covers this in Cosmos |

### §7.3 AI Search indexes (existing — wrap, don't extend)

| Index | Used by | Memory tier |
|---|---|---|
| `spaarke-session-files` | Session file chunks for recall mode='section' | T5 (T2-scoped) |
| `spaarke-files-index` | Matter-bound document chunks | T5 (T3-scoped) |
| `spaarke-rag-references` | Knowledge source content | T5 (T4-org-scoped) |
| `spaarke-records-index` | Entity records | T5 |
| `spaarke-knowledge-index-v2` | Domain knowledge | T5 |
| `discovery-index` | Discovery materials | T5 |
| `playbook-embeddings` | (NOT memory; routing only — see WP1 in design.md) | n/a |
| `spaarke-insights-index` | **NOT used for chat memory** — see §5.2.1 | n/a |

### §7.4 Redis hot-tier patterns

| Key pattern | TTL | Used by | Purpose |
|---|---|---|---|
| `session:{sessionId}` | 24h sliding | `ChatSessionManager` | T2 active session blob |
| `{sessionId}:fileDiscussed:{fileId}` | Session-scoped | `RecentlyDiscussedTracker` (NEW) | T2 recently-discussed flag |
| `tenant:{tenantId}:workspace:tabs` | 24h sliding | `WorkspaceStateService` | T2 workspace tabs hot-tier |
| `pinned:{tenantId}:{ownerId}` | 1h | `PinnedContextRecallService` | T4 pin recall cache |

Standard distributed-cache patterns. No Insights-specific borrowing here.

---

## §8 Tool Surface

### §8.1 Eight tools cataloged

#### T2 Session Memory Tools

```typescript
list_session_files()
→ Array<{
    fileId: string,
    fileName: string,
    contentType: string,
    sizeBytes: number,
    pageCount?: number,
    uploadedAt: Date,
    uploadedBy: string,
    classifiedDocType: string,
    classifiedConfidence: number,
    precomputedSummary: string,    // marked NOT authoritative in description
    sections: string[],
    recentlyDiscussed: boolean,
    citationsReferenced: Array<{turn: number, location: string}>
  }>
```

```typescript
get_file_manifest(fileId: string)
→ {
    fileName: string,
    pageCount: number,
    sections: Array<{name: string, startPage: number, endPage: number}>,
    tables: Array<{name: string, page: number}>,
    citations: number,
    language: string,
    extractedText: { chars: number, tokens: number },
    classifiedDocType: string,
    classifiedConfidence: number,
    sha256: string
  }
```

```typescript
recall_session_file({
  fileId: string,
  purpose: "answer_question" | "quote" | "compare" | "summarize" | "extract_dates" | "verify",
  query: string,
  scope: "summary" | "relevant_sections" | "full_text" | "tables" | "citations",
  maxTokens?: number,
  requireCitations: boolean   // default true
})
→ {
    content: string,
    citations: Array<{ page, paragraph?, section?, text }>,
    scope_truncated: boolean,
    truncation_reason?: "exceeded_8K" | "not_found" | "ok"
  }
```

```typescript
write_session_memory({
  fact: string,
  source: string,
  confidence: number,         // 0.0-1.0 self-assessed
  category?: "decision" | "preference" | "open-question" | "assumption"
})
→ { factId: string }
```

#### T3 Matter Memory Tools

```typescript
retrieve_matter_memory(matterId: string, query: string)
→ Array<{
    factId: string,
    fact: string,
    source: string,
    confidence: number,
    category: string,
    createdAt: Date,
    createdBy: string,
    sessionId?: string         // session of origin if promoted
  }>
```

```typescript
promote_to_matter_memory({
  fact: string,
  source: string,
  approvalRequired: boolean    // user-approval workflow if true
})
→ {
    factId: string,
    status: "written" | "pending_approval" | "rejected"
  }
```

#### T4 User/Org Memory Tools (read-only)

```typescript
get_user_preferences()
→ {
    writingStyle: "concise" | "detailed" | "narrative",
    summaryLength: "short" | "medium" | "long",
    citationStyle: "inline" | "footnote" | "bluebook",
    pinnedPlaybooks: string[]
  }

get_org_templates(category?: string)
→ Array<{ templateId, name, type, content }>
```

#### T2 Workspace Tools (Pillar 6b — already exist)

```typescript
get_workspace_tab_state(tabId: string)
→ SerializedWidgetState   // from Pillar 9 getAgentVisibleState contract

update_workspace_tab({ tabId, edit: { old, new } })
→ { success: boolean, conflictReason?: string }   // Harvey/Artifacts targeted-edit

send_workspace_artifact({ widgetType, widgetData })
→ { tabId: string }

close_workspace_tab(tabId: string)
→ { success: boolean }
```

### §8.2 Tool routing logic (per tier)

| Tool | Reads tier | Writes tier | Always-on or filtered? |
|---|---|---|---|
| `list_session_files` | T2 | — | Always-on (when session has files) |
| `get_file_manifest` | T2 | — | Always-on |
| `recall_session_file` (summary/section/full) | T2 + T5 | T6 (audit emit) | Always-on |
| `write_session_memory` | — | T2 | Always-on |
| `retrieve_matter_memory` | T3 | T6 | Always-on (when matter context present) |
| `promote_to_matter_memory` | — | T3 (via pending approval queue) | Always-on (when matter context present) |
| `get_user_preferences` | T4 | — | Always-on |
| `get_org_templates` | T4 | — | Always-on |
| `get_workspace_tab_state` | T2 | — | Always-on (when tabs exist) |
| `update_workspace_tab` | T2 | T2 + T6 | Always-on (when any tab is agent-editable) |
| `send_workspace_artifact` | — | T2 + T6 | Always-on |
| `close_workspace_tab` | — | T2 + T6 | Always-on |

**All memory tools are ALWAYS-ON when the relevant context is present.** They are NOT filtered by `CapabilityRouter` (which is being retired). This eliminates the "agent can't reach memory tools" failure mode the user observed in UAT.

### §8.3 Citation enforcement model

`recall_session_file` enforces `requireCitations: true` by default. The persona instruction reads:

> "When you call `recall_session_file` with `requireCitations: true`, the tool returns citations alongside content. You MUST cite these in any answer that uses the recalled content. Do not quote precomputed summaries from the file cards as if they were the source — those are upload-time approximations marked 'NOT authoritative'. For any legally-precise question (specific clauses, exact wording, dates, parties, dollar amounts), call `recall_session_file` with `requireCitations: true` and cite the source in your answer."

This is the **trust framing** — load-bearing for legal-domain accuracy.

---

## §9 Failure Modes

### §9.1 Per-tier failure isolation

| Tier | Failure mode | Graceful degradation |
|---|---|---|
| T1 Working | `MemoryCompositionService` throws | Log warning; fall back to bare persona + user message; emit `context.error` event |
| T2 Session | Redis unavailable | Read from Cosmos warm-tier; flag prompt with "session-from-cold-recovery" note |
| T2 Session | Cosmos unavailable on session create | Best-effort: chat proceeds with in-memory session; lose persistence across restart; log Warning |
| T3 Matter | `MatterMemoryService.ToSystemPromptFragmentAsync` throws | Return empty fragment; agent operates without matter memory; emit `context.warning` |
| T4 User/org | `sprk_userpreferences` read fails | Use default preferences (concise / medium / inline); log Warning |
| T5 Retrieval | AI Search query fails | Recall tool returns `truncation_reason: "search_unavailable"`; agent can apologize or retry |
| T6 Audit | Cosmos `audit` write fails | NEVER block chat — log to App Insights as backup; alert on cumulative failure rate |

### §9.2 Edge cases handled by design

| Scenario | Behavior |
|---|---|
| Agent calls `recall_session_file` for fileId not in session | Returns `truncation_reason: "not_found"`; persona instruction tells agent to apologize + ask user |
| Agent calls `recall_session_file` with mode='full' on 200-page doc | Returns summary + first 2K chars + `truncation_reason: "exceeded_8K"` |
| Agent calls `promote_to_matter_memory` without matter context | Returns `status: "rejected"` with reason "no matter context"; agent informs user |
| User edits workspace tab while agent is generating an update | Q8 conflict check refuses agent write; agent re-reads + retries |
| Precomputed summary contains hallucinated content | Citation enforcement: agent must call recall with `requireCitations: true` for any precise answer; hallucinated summary content lacks citation → agent can't quote it |
| Two memory tools called in same turn | Tools execute serially (Microsoft.Extensions.AI sequence); both results land in dynamic suffix budget |

---

## §10 Performance Characteristics

### §10.1 Per-turn latency budget

| Phase | Budget | Notes |
|---|---|---|
| Static prefix assembly (cached after first turn) | <50ms (cache hit) / ~300ms (cache miss) | Hot path |
| Dynamic suffix assembly | ~100-200ms | `MemoryCompositionService` composes ~50ms; tool outputs ~50-150ms |
| LLM call (Azure OpenAI Chat Completions) | ~1000-3000ms TTFT cached / ~5000ms cold | Cache discount: 50% input cost + 80% faster TTFT |
| Tool call (recall) | 5ms (summary) / 150ms (section) / 50ms (full) | Per call; multiple per turn possible |
| Response stream | Variable; SSE per delta | Frontend renders progressively |

**Target**: P95 first-token <1.5s on cache hit; <3s on cache miss.

### §10.2 Cost model (per-turn input tokens with cache)

Per A3 research:
- Static prefix ~6K tokens × $2.50/M (gpt-4o input) = $0.015 per turn UNCACHED
- Cache hit: 50% discount → **$0.0075 per turn** (60% reduction on the cached portion)
- Dynamic suffix ~5K tokens × $2.50/M = $0.0125 per turn
- Total per-turn: ~$0.013 with cache (down from ~$0.028 without)

At 50K turns/day → ~$650/day (cached) vs $1400/day (uncached) → **~$23K/month savings at scale**.

### §10.3 Cache hit rate

Required for cost model:
- Static prefix must be byte-stable across turns within a session
- BUT context cards change between turns (recently-discussed flag flips, citation counts update)

**Resolution**: stable BLOCKS within the prefix; recompose ONLY when contents change. Layered context cards refresh ONLY when:
- A new file is uploaded → recompose all cards (rare)
- A file becomes "recently discussed" → flip flag (potentially every turn)

**Open question for benchmark**: does per-turn recently-discussed flag flip defeat caching? If yes, move recently-discussed into the dynamic suffix and accept reduced cache hit on that section.

---

## §11 Migration Path

### §11.1 What exists (R6 carry-forward — DO NOT REBUILD)

- All 7 R6 Pillar 7 memory services (built + DI-registered + tested)
- 5 Cosmos containers provisioned via Bicep
- Pillar 6b workspace-write handlers (registered as `sprk_analysistool` rows)
- `WorkspaceStateService` with Q4 hybrid persistence
- `getAgentVisibleState` per-widget impls (Pillar 9)
- `SprkChatAgentFactory.BuildWorkspaceStateBlock` (server-side privacy projection)
- Pinned Memory CRUD UI (4 endpoints + 4 React components + widget registry)
- FR-45 wiring (`MatterMemoryService` invoked from `PlaybookChatContextProvider:627`) — VERIFIED

### §11.2 What to extend

| Component | Extension | Pattern precedent |
|---|---|---|
| `StoredSession` shape | Add `UploadedFiles[N].SummaryText`, `.ClassifiedDocType`, `.Sections`, `.TableMetadata`, `.Citations` | R6 task 051 `SaveTabsAsync` added `Tabs` + `WidgetStates` fields the same way |
| `ChatSessionFile` record | Add enriched fields (above) | Same as above |
| `IChatDataverseRepository` | Rename to `IChatAuditRepository`; retire read methods | New interface; old methods become extension methods that throw `NotSupportedException` |
| `PlaybookChatContextProvider` | Add trust framing line to persona block | One-line injection between persona + user content |
| `SprkChatAgentFactory` | Replace 1-line file mention with `LayeredContextCardBuilder` output | Same composition seam |

### §11.3 What to wrap (new tool handlers over existing storage)

8 new tool handlers (see §8.1). Each follows the existing `IToolHandler` pattern:
- Implements `ExecuteChatAsync(ChatInvocationContext, AnalysisTool, CancellationToken)` per R6 Pillar 2 contract
- Registered as `sprk_analysistool` rows in Dataverse
- Seeded via `scripts/Seed-TypedHandlers.ps1`
- ADR-015 audit: log handler + IDs + parameter counts; NEVER parameter values

### §11.4 What to retire

| Item | Retirement strategy |
|---|---|
| `ChatDataverseRepository` placeholder methods | Rename interface to `IChatAuditRepository`; deprecate read methods; confirm Cosmos `audit` is sole reader |
| `sprk_aichatmessage` (10K cap, broken stub) | Document as pure write-target; remove all reader code paths in chat; preserve writes for compliance audit trail |
| `SessionSummarizeOrchestrator.ChatSummarizePlaybookId` hardcoded GUID | Lift to `IOptions<AnalysisOptions>` via §1.7 stable code (`summarize-document-chat`) |
| `BuildDefaultSystemPrompt` defense-in-depth fallback | Delete after stabilization window confirms zero production hits |

---

## §12 Open Questions

### §12.1 For early implementation

1. **Per-turn recompose vs cache invalidation** — does flipping the "recently discussed" flag in every turn defeat the static prefix cache? Need benchmark before committing to position in prefix vs suffix. (Performance §10.3)

2. **Memory promotion approval UX** — where in the Context pane does the approval notification render? Notification toast vs dedicated approval panel? (§6.4)

3. **`spaarke-insights-index` artifactType extension** — definitively NOT used per §5.2.1, but confirm: do any T5 tools need to read Insights observations as context (e.g., for matter-aware memory)? If yes, treat as a SEPARATE tool surface (`retrieve_insights_observation`), not as memory.

### §12.2 For successor project planning

4. **Session-file enrichment cost** — at scale, ~$0.0001 per file × N files per day = ?. Need projection.

5. **Workspace-write availability flag** — per-tab `agentEditable: boolean` metadata. Where does this live? Extension to `WorkspaceWidgetRegistry`? Per-widget config?

6. **Long-term cross-session memory** (Foundry-pattern) — Q4 resolution: OUT of scope. Re-evaluate in a follow-up project.

### §12.3 For Insights coordination

7. **Citation verification reuse** — Insights' `GroundingVerifyNode` strips unverified citations from synthesis. The pattern is interesting for memory recall. Does the Insights team want to share an underlying verification PRIMITIVE (e.g., `ICitationVerifier`) or should chat memory build its own? §5.2.5 leans toward separate implementation (inspiration only); coordinate with Insights team for final call.

---

## Appendices

### A. Component Inventory (quick reference)

**Existing R6 components used (no rebuild)**:
- `MatterMemoryService` + `IMatterMemoryService`
- `PinnedContextRepository` + `IPinnedContextRepository`
- `PinnedContextRecallService` + `IPinnedContextRecallService`
- `MemoryCompositionService` + `IMemoryCompositionService`
- `SummarizationCompressionService` + `ISummarizationCompressionService`
- `PromptBudgetTracker` + `IPromptBudgetTracker`
- `SessionPersistenceService`
- `SessionRestoreService`
- `SessionSummarizationService`
- `AuditLogService`
- `RagService`
- `WorkspaceStateService`
- `PlaybookChatContextProvider`
- `SprkChatAgentFactory`
- `IContextEventEmitter`
- All Pillar 6b workspace-write handlers
- `ManagePinnedContextHandler`
- `IScopeResolverService` (persona resolution)

**New components introduced**:
- `SessionFileEnrichmentService`
- `FileClassificationService`
- `FileSummarizationService`
- `FileManifestExtractor`
- `RecentlyDiscussedTracker`
- `MatterMemoryPromotionService`
- `LayeredContextCardBuilder`
- `TrustFrameInstructionInjector`
- `UserPreferencesService` (if not existing — verify)
- `OrgTemplatesService` (if not existing — verify)
- `IChatAuditRepository` (replaces `IChatDataverseRepository`)
- 8 new tool handlers (T2-T4 surface per §8.1)

**Components NOT to build (explicit decisions)**:
- `sprk_matterfacts` Dataverse entity
- New chat-memory AI Search index
- Memory-specific MultiIndexComposer
- Shared envelope type with Insights
- New audit container
- Fixed-impl `ChatDataverseRepository` (retire instead)

### B. Storage Inventory (quick reference)

**Cosmos**: `sessions`, `memory`, `audit` (chat-relevant); `prompts`, `feedback` (orthogonal)
**Dataverse**: `sprk_userpreferences` (read); `sprk_aichatmessage` (audit-write only after retirement)
**Redis**: 4 key patterns per §7.4
**AI Search**: `spaarke-session-files`, `spaarke-files-index`, `spaarke-rag-references`, `spaarke-records-index`, `spaarke-knowledge-index-v2`, `discovery-index`

**Explicitly NOT used for memory**: `spaarke-insights-index`, `sprk_matter.sprk_performancesummary`

### C. Tier Boundaries (quick reference)

```
┌─ T1 Working (per-turn ephemeral) ────────────────┐
│ Composed from T2-T5; never persisted             │
└──────────────────────────────────────────────────┘
                  ↑ read
┌─ T2 Session (24h Redis / 90d Cosmos) ────────────┐
│ Files + history + decisions + workspace tabs     │
└──────────────────────────────────────────────────┘
        ↑ read       ↓ explicit promotion
┌─ T3 Matter (durable Cosmos memory) ──────────────┐
│ Cross-session matter facts                       │
└──────────────────────────────────────────────────┘
                  ↑ read-only
┌─ T4 User/Org (Dataverse + Cosmos pins) ──────────┐
│ Preferences + templates + pinned playbooks       │
└──────────────────────────────────────────────────┘
                  ↑ query
┌─ T5 Retrieval (Azure AI Search) ─────────────────┐
│ Multiple indexes; recall tools wrap them         │
└──────────────────────────────────────────────────┘
                  ↓ append (no read from T1)
┌─ T6 Audit (Cosmos audit; immutable) ─────────────┐
│ Every tool call + decision emitted               │
└──────────────────────────────────────────────────┘
```

### D. Document History

| Version | Date | Change |
|---|---|---|
| v1 | 2026-06-21 | Initial authoring from R6 session context. §5 "Insights Engine — Honest Relationship Assessment" added per user directive. |

---

**End of architecture document**

For the work-package narrative (WP1-6), see `../design.md`.
For the source research, see `../research/research-summary.md`.
For the requirements (FRs/NFRs), see `../spec.md`.
