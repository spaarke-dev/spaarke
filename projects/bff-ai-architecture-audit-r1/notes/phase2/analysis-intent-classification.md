# Phase 2 Analysis — Category 1: Intent Classification

> **Authored by**: Phase 2 W2 Sub-Agent E
> **Pinned to**: commit `357e6936` (Phase 1 inventory snapshot)
> **HEAD at analysis time**: `d862bec6` (3 commits since `12275b10` W1 baseline; ZERO code drift in `Services/Ai/` between `357e6936` → `d862bec6` confirmed via `git diff --stat`)
> **Scope boundary**: 4-classifier consolidation evaluation. Out-of-scope per brief §4: cache backing choice (defer to Cat 4), model choice (per-consumer), prompt content (Cat 5), vector index schema (SprkChat-owned), performance benchmarking, cross-team roadmap decisions (Q-003 sequential).

---

## §1 Phase 1 baseline (verbatim from inventory §2.1 + §7.1)

### §1.1 Inventory §2.1.1 — `CapabilityRouter` (three-tier)
Verbatim: "Path: `Services/Ai/Capabilities/CapabilityRouter.cs`. Layer 1: keyword classifier (AIPU2-012; <50ms); Layer 2: LLM (`IChatClient("raw")`); Layer 3 fallback inlined. Consumers: `SprkChatAgentFactory`, `NullSprkChatAgentFactory`, `AiLatencyTracker`, `AiLatencyTelemetry` (4 production). State: **ACTIVE** — production-wired in SprkChatAgentFactory's tool selection path. DI: Singleton via factory; `AiCapabilitiesModule.cs:117-123`. Also exposed as `ICapabilityRouter`. Config: `IOptions<CapabilityRouterOptions>` bound from `Capabilities:Router`. Origin: AIPU2-012/013/014."

### §1.2 Inventory §2.1.2 — `PlaybookDispatcher` (two-stage vector + LLM)
Verbatim: "Path: `Services/Ai/Chat/PlaybookDispatcher.cs`. Stage 1: vector similarity (`PlaybookEmbeddingService` → `playbook-embeddings` index; 1.5s budget; ≥0.85 short-circuits). Stage 2: LLM refinement + parameter extraction (0.5s budget). Consumers: `ChatEndpoints`, `SprkChatAgentFactory`, `NullSprkChatAgentFactory`, `PlaybookOutputHandler` (4 production via factory). State: **ACTIVE** — factory-instantiated, NOT DI-registered (ADR-010 budget constraint). Config: Inline constants. Uses Redis (`IDistributedCache`) for dispatch result caching. Origin: SprkChat r2 task r2-015."

### §1.3 Inventory §2.1.3 — `IntentClassificationService` (playbook builder, 11 categories)
Verbatim: "Path: `Services/Ai/IntentClassificationService.cs`. Classifies user intent into 11 categories (CREATE_PLAYBOOK, ADD_NODE, etc.) for the AI Chat Playbook Builder. 0.75 confidence threshold. Hardcoded model `gpt-4o-mini`. Consumers: NONE in production source code beyond DI registration. State: **UNUSED / ORPHANED**. DI: Scoped via `AddBuilderServices` in `AnalysisServicesModule.cs:372` (compound gate). Config: No `IOptions<T>`; hardcoded model. No Null peer. Origin: AI Chat Playbook Builder design."

### §1.4 Inventory §2.1.4 — `InsightsIntentClassifier` (JSON-schema-constrained)
Verbatim: "Path: `Services/Ai/Insights/Routing/InsightsIntentClassifier.cs`. Phase 1.5 LLM-based routing between Insights playbook synthesis (`/api/insights/ask`) and open-ended RAG retrieval (`/api/insights/search`). JSON-schema-constrained output; SHA-256 cache key per normalized query; 500ms FR-05 budget. Consumers: `AssistantToolCallHandler.cs` (Wave E3 task 042). State: **ACTIVE (gated)** — real impl behind compound AI gate + fine-grained `Insights:IntentClassifier:Enabled` opt-out. `NullInsightsIntentClassifier` registered ALWAYS. DI: Singleton via `AddInsightsIntentClassifier` in `AnalysisServicesModule.cs:508-534`. Config: `IOptions<InsightsIntentClassifierOptions>` bound from `Insights:IntentClassifier`. Origin: Insights Engine r2 Wave E2 task 041 (FR-05)."

### §1.5 Inventory §7.1 open questions
Verbatim: "Can the 4 classifiers consolidate into ONE generic `IIntentClassifier<TResult>` with strategy plug-ins (keyword, vector, LLM)? What's the migration path for `IntentClassificationService` consumers? Is `AiPlaybookBuilderService` still actively used or part of the orphan cluster? Should `PlaybookDispatcher` (factory-instantiated) join DI to enable the same testability seam the other three have? The two `IntentClassificationResult` types (different shapes, same name in `Models/Ai/`) cause confusion. Rename one?"

---

## §2 Empirical reproduction (consumer counts re-run at HEAD `d862bec6`)

### §2.1 Drift check
`git diff --stat 357e6936..d862bec6 -- src/server/api/Sprk.Bff.Api/Services/Ai/` returns EMPTY. ZERO code drift on Cat 1 surface between snapshot and HEAD. All findings in §1 remain accurate at HEAD.

### §2.2 LOC inventory (4 classifier source files)

| File | LOC |
|---|---|
| `CapabilityRouter.cs` | 827 |
| `PlaybookDispatcher.cs` | 514 |
| `IntentClassificationService.cs` | 408 |
| `InsightsIntentClassifier.cs` | 379 |
| **Total** | **2128** |

### §2.3 Consumer reproduction

**§2.3.1 `ICapabilityRouter` (and `CapabilityRouter` concrete)** — 9 hits in `src/`:
- Behavioral production consumers: `Chat/SprkChatAgentFactory.cs` (lines 56-60, 67, 230, 264, 333, 598), `Telemetry/AiLatencyTracker.cs` (uses routing result).
- Sentinels: `Chat/NullSprkChatAgentFactory.cs` (line 21 — XML doc kill-switch parallel), `Telemetry/AiLatencyTelemetry.cs` (line 86 — metric description string).
- **Behavioral consumer count: 2** (inventory's 4 included telemetry surface + Null peer sentinel). State remains **ACTIVE**.

**§2.3.2 `PlaybookDispatcher`** — 9 hits in `src/`:
- Behavioral consumers: `Api/Ai/ChatEndpoints.cs`, `Chat/SprkChatAgentFactory.cs:489` (factory instantiation `return new PlaybookDispatcher(...)`), `Chat/PlaybookOutputHandler.cs`.
- Sentinels: `Chat/NullSprkChatAgentFactory.cs` (kill-switch peer).
- **Behavioral consumer count: 3** (inventory's 4 counted the Null-Object sentinel). State remains **ACTIVE**.

**§2.3.3 `IIntentClassificationService` / `IntentClassificationService`** — 2 hits in entire `src/`:
- `Services/Ai/IntentClassificationService.cs` (self — interface + impl).
- `Infrastructure/DI/AnalysisServicesModule.cs:372` (DI registration).
- **NO other consumers anywhere in `src/`.** Verified via independent Greps of both interface and concrete names.
- **CRITICAL CORRECTION of W1 Sub-Agent A's at-risk flag**: `AiPlaybookBuilderService.cs` does NOT import or inject `IIntentClassificationService`. The lexical Grep collision (W1 saw `AiPlaybookBuilderService` in "IntentClassification" results) was because `AiPlaybookBuilderService` has its OWN inline LLM-based intent classification (`ClassifyIntentAsync` at line 600; `BuildAiIntentClassificationSystemPrompt` at line 684; references the `IntentClassification` type — NOT `IntentClassificationResult`). `AiPlaybookBuilderService` has 4 production consumers and is independently active. **Orphan classifier is genuine and isolated; no cascade risk to AiPlaybookBuilderService.**

**§2.3.4 `IInsightsIntentClassifier`** — 7 hits in `src/server/api/Sprk.Bff.Api/`:
- Behavioral consumer: `Services/Ai/Insights/AssistantToolCallHandler.cs` (line 104 field, 111 ctor param, 478 call `var classification = await _classifier.ClassifyAsync(...)`).
- XML cref only: `PublicContracts/IInsightsAi.cs`.
- Definitions + DI + Options: `Routing/IInsightsIntentClassifier.cs`, `Routing/InsightsIntentClassifier.cs`, `Routing/NullInsightsIntentClassifier.cs`, `Infrastructure/DI/AnalysisServicesModule.cs` (lines 265, 269, 523, 530 — both Null-OFF and real-ON branches), `Configuration/InsightsIntentClassifierOptions.cs`.
- **Behavioral consumer count: 1**. State remains **ACTIVE (gated)**.

### §2.4 The "IntentClassificationResult" naming collision — INVENTORY UNDERCOUNTED

Inventory claimed "two `IntentClassificationResult` types." Empirical reality: **THREE related types**:
- **Type A**: `Sprk.Bff.Api.Services.Ai.IntentClassificationResult` (`IntentClassificationService.cs:364-408`). Fields: `Intent` (`BuilderIntentCategory` 11-value enum), `Confidence`, `Entities` (`IntentEntities?`), `EntityDictionary`, `NeedsClarification`, `ClarificationQuestion`, `ClarificationOptions[]`, `Reasoning`, `IntentDescription`. Tied to orphan classifier.
- **Type B**: `Sprk.Bff.Api.Services.Ai.Insights.Routing.IntentClassificationResult` (`IInsightsIntentClassifier.cs:107-113`). Fields: `Path` (`IntentPath` 2-value enum), `PlaybookId`, `Confidence`, `BelowThreshold`, `Reason`, `CacheHit`.
- **Type C**: `Sprk.Bff.Api.Services.Ai.IntentClassification` (NOT "Result"; declared at `IAiPlaybookBuilderService.cs:286-308`). Fields: `Intent` (`BuilderIntent` enum — 12 values, different from Type A's `BuilderIntentCategory` despite semantic overlap), `Confidence`, `Entities` (`Dictionary<string,string>?`), `NeedsClarification`, `ClarificationQuestion`, `Message`. Heavily consumed in `AiPlaybookBuilderService.cs` (lines 295, 333, 437, 488, 1583-1593, 1730).

Plus a 4th near-name type: `IntentClassificationResponse` (`Models/Ai/IntentClassificationModels.cs:61`) — the LLM RESPONSE wire shape (vs result shape). Used by `AiPlaybookBuilderService` inline classification path.

### §2.5 `NullInsightsIntentClassifier` ctor — W1 Sub-Agent A flag REJECTED

W1 Sub-Agent A (Cat 4) flagged `NullInsightsIntentClassifier` as potentially taking `IMemoryCache` in ctor (anti-smell). **Empirical correction**: `NullInsightsIntentClassifier.cs:54-57` ctor takes ONLY `ILogger<InsightsIntentClassifier> logger`. NO `IMemoryCache`. The Null peer is correctly minimal per ADR-032 §MUST "Null-Object constructors MUST be minimal — typically only ILogger<T>". W1's grep was confused by bundling the real classifier's `IMemoryCache` use under "Insights/Routing/" directory. **§F.1 compliance is intact.**

### §2.6 `PlaybookDispatcher` factory-instantiation — load-bearing tenant scope

Inventory §7.1 third bullet asked whether `PlaybookDispatcher` should join DI. Empirical reality (verified at `Chat/SprkChatAgentFactory.cs:489`): the factory captures per-request `tenantId` and threads it through (`PlaybookDispatcher.cs:88` field; line 400 cache key `dispatch:output:{_tenantId}:{playbookId}`). **DI registration would be non-trivial**: Singleton lifetime impossible (per-request tenant), Scoped lifetime would require `IHttpContextAccessor` injection — backward step from current explicit factory pattern. The XML doc currently leads with "ADR-010 budget" rationale; the empirically-correct primary rationale is tenant-scoping.

---

## §3 Per-classifier decision table

| # | Service | Path | Decision | Rationale | Migration cost | Cross-team owner |
|---|---|---|---|---|---|---|
| 1 | `CapabilityRouter` | `Capabilities/CapabilityRouter.cs` | **KEEP** | Genuinely multi-stage (keyword → LLM → broad superset). Heavy OTEL. 2 behavioral consumers. Singleton (correct lifetime). Options-bound. Semantically distinct from other 3 (capability routing, not playbook selection). | N/A | AIPU R2 |
| 2 | `PlaybookDispatcher` | `Chat/PlaybookDispatcher.cs` | **KEEP (do NOT promote to DI)** | Factory-instantiation is load-bearing because per-request `tenantId` is captured; Singleton impossible, Scoped requires `IHttpContextAccessor` (backward step). 3 behavioral consumers; factory pattern works. **Inventory §7.1 bullet 3 → REJECTED** with empirical justification. Recommend XML doc amendment to lead with tenant-scoping rationale. | N/A | SprkChat |
| 3 | `IntentClassificationService` | `Services/Ai/IntentClassificationService.cs` | **DELETE** (HARD GATE A,B,C all pass) | **HARD GATE A**: ZERO non-test consumers in `src/`. **HARD GATE B**: DI removal = single line `AnalysisServicesModule.cs:372`. NO transitive consumer cascade (`AiPlaybookBuilderService` independent — §2.3.3). **HARD GATE C**: 408 LOC → ~5-7 KB compressed IL. NEGLIGIBLE per NFR-01. Bundles with Sub-Agent B's lookup orphan PR as a 4th "single-PR cleanup" item. | S (<1d) | AI Chat Playbook Builder team |
| 4 | `InsightsIntentClassifier` | `Insights/Routing/InsightsIntentClassifier.cs` | **KEEP** | Sole consumer (`AssistantToolCallHandler`) active in Wave E3 task 042. JSON-schema-constrained decoding via `GetStructuredCompletionRawAsync` is materially unique. Null peer is correctly minimal (W1 anti-smell REJECTED per §2.5). DI registration is rigorous (compound gate + fine-grained kill-switch + ADR-032 §F.1 dual-registration). Options-bound. **Recommend as canonical reference impl** for future intent classifiers. | N/A | Insights Engine r2 |
| 5 | Type A `IntentClassificationResult` (`Services.Ai`) | `IntentClassificationService.cs:364-408` | **DELETE** (subsumed by #3) | Tied to orphan classifier. No external consumers. Folds into #3 PR. | N/A | Same as #3 |
| 6 | Type B `IntentClassificationResult` (`Services.Ai.Insights.Routing`) | `IInsightsIntentClassifier.cs:107-113` | **RENAME (low priority)** | Same name as Type A; even after #3 deletes, future risk is "next new classifier introduces 3rd `IntentClassificationResult`." Rename candidate: `InsightsRoutingDecision`. Owner adjudicates whether namespace disambiguation is sufficient. | XS (~20 LOC) | Insights Engine |
| 7 | Type C `IntentClassification` (`Services.Ai`) | `IAiPlaybookBuilderService.cs:286-308` | **KEEP** | Active with 6+ references in `AiPlaybookBuilderService.cs`. Becomes the sole intent-classification result type in `Services.Ai` namespace post-#3 deletion. | N/A | AI Chat Playbook Builder |

**Decision distribution**: 3 KEEP classifiers (1, 2, 4) + 1 KEEP type (7) + 1 DELETE classifier (3) + 1 DELETE type bundled (5) + 1 RENAME-LOW (6) = 7 total cluster decisions.

### §3.1 HARD GATE C deeper verification — #3 cascade

Cross-checking what else gets removed:

| Artifact | Status post-delete | Notes |
|---|---|---|
| `Services/Ai/IntentClassificationService.cs` (408 LOC) | DELETE | Interface + impl + Type A |
| `Models/Ai/IntentClassificationModels.cs` | KEEP | `IntentClassificationResponse` (LLM wire shape) is used by `AiPlaybookBuilderService.cs:1103-1117`. NOT subsumed. |
| `Models/Ai/AiIntentClassificationSchema.cs` | KEEP | Referenced by `AiPlaybookBuilderService.cs:40` (Confidence thresholds reference) |
| `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs` | **CASCADE DELETE** | Inventory §6.2 flagged this as AT RISK. Verified: `PlaybookBuilderSystemPrompt.IntentClassification` (50+ line static string) + `PlaybookBuilderSystemPrompt.Thresholds.IntentConfidence` are referenced ONLY in `IntentClassificationService.cs:92, 175, 334, 335`. Post-#3 deletion: zero consumers. DELETE. |
| `Services/Ai/FallbackPrompts.cs:IntentClassification` constant | KEEP | Referenced by `AiPlaybookBuilderService.cs:224, 257` — different fallback path. Independent. |
| `Services/Ai/ClarificationService.cs` | **INVESTIGATE (likely cascade)** | Uses Type A `IntentClassificationResult` (lines 22, 83). `Grep IClarificationService` shows only definition + DI registration `AnalysisServicesModule.cs:374`. **Likely orphan; trigger W3 verification grep.** |

**Cascade budget**:
- `IntentClassificationService.cs` (408 LOC) + `PlaybookBuilderSystemPrompt.cs` (~100 LOC est) + `ClarificationService.cs` if confirmed (~400-600 LOC est) + DI lines 372-374 cleanup.
- **Estimated total**: ~1000-1100 LOC + 3 DI lines. NEGLIGIBLE per NFR-01.
- **Combined with Sub-Agent B's lookup-service DELETE PR**: ~20-30 KB compressed publish-size win.

---

## §4 Cross-cutting findings (canonical consolidation feasibility)

### §4.1 THE CENTRAL ARCHITECTURAL QUESTION — `IIntentClassifier<TResult>` consolidation

**Inventory §7.1 first bullet**: "Can the 4 classifiers consolidate into ONE generic `IIntentClassifier<TResult>` with strategy plug-ins (keyword, vector, LLM)?"

**Verdict: NO — partial consolidation only.**

#### §4.1.1 Semantic distinctness audit

| Classifier | What it classifies | Output shape | Decision space | Domain |
|---|---|---|---|---|
| `CapabilityRouter` | Which capability answers this turn? | `string[] capabilityName + confidence + Layer + LatencyMs + toolNames[]` | N-way (~50 capabilities) | Per-chat-turn routing to tool subsets |
| `PlaybookDispatcher` | Which playbook matches this message? | `playbookId + confidence + parameters + OutputType + targetPage` | N-way + parameter extraction | Per-chat-turn JPS dispatch |
| `IntentClassificationService` (orphan) | Which UI builder action? | `BuilderIntentCategory (11-way) + entities + clarification` | Closed 11-way | Canvas manipulation at build time |
| `InsightsIntentClassifier` | Playbook synthesis OR open RAG? | `IntentPath (2-way) + optional playbookId + BelowThreshold` | Closed 2-way | Single binary routing |

**Key empirical observations**:

1. **Output shapes are not abstractable**. `CapabilityRoutingResult` (selectedCapabilities + Layer + LatencyMs + toolNames), `DispatchResult` (OutputType + ExtractedParameters + targetPage), Type B `IntentClassificationResult` (Path + BelowThreshold + Reason + CacheHit) overlap on `Confidence` only. Forcing `TResult` would lose type safety at call sites.

2. **Input contexts are domain-specific**. `string? activePlaybookName` vs `ChatHostContext?` vs `ClassificationCanvasContext?` vs `IntentClassificationContext?`. No abstract `TContext` captures these without information loss.

3. **Strategy plug-ins (keyword/vector/LLM) are NOT orthogonal across classifiers**:
   - CapabilityRouter: keyword (Layer 1) + LLM (Layer 2) + broad superset (Layer 3) — cascade.
   - PlaybookDispatcher: vector (Stage 1) + LLM refinement (Stage 2) — cascade with concurrency permit (`SemaphoreSlim AiConcurrencyLimiter`).
   - IntentClassificationService: single-LLM + rule-based fallback on parse failure.
   - InsightsIntentClassifier: single-LLM with JSON-schema-constrained decoding + low-confidence-to-RAG fallback.

   Even the "LLM stage" cannot abstract: `InsightsIntentClassifier.cs:73-102` defines a `BinaryData ClassificationJsonSchema` for constrained decoding — folding this into a strategy interface would leak JSON-schema concerns into the abstraction.

4. **Concurrency/backpressure semantics differ materially**. PlaybookDispatcher has shared `SemaphoreSlim(10,10)` (ADR-016 backpressure). CapabilityRouter has none (lock-free in-memory). InsightsIntentClassifier has none. Folding these would either standardize a backpressure model none currently share, or leak concurrency hooks into the strategy interface.

5. **Telemetry shapes differ**. CapabilityRouter emits 3 distinct OTEL Activity names (`capability_router.layer1`, `ai.routing.layer2`, plus Layer 3 counter); 4 Meter instruments (Layer1Hit/Latency + Layer2Hit/Latency + Layer3Hit). PlaybookDispatcher uses Stopwatch-based logging only. InsightsIntentClassifier uses LogInformation. Generic strategy would have to either drop or homogenize.

#### §4.1.2 What IS abstractable — partial consolidation candidates

| Pattern | Abstractable? | Action |
|---|---|---|
| Cache wrapper (SHA-256 key + TTL) | YES — coordinate with Cat 4 | `InsightsIntentClassifier.cs:303-316` `ComputeCacheKey` + `PlaybookDispatcher.cs:400-457` cache pattern would benefit from `DistributedCacheExtensions.GetOrCreateAsync<T>`. **Not Cat 1; defer to Cat 4.** |
| LLM JSON-schema-constrained primitive | YES — narrow helper | `IConstrainedClassifier<TInput, TOutput>` wrapping `IOpenAiClient.GetStructuredCompletionRawAsync`. ONLY `InsightsIntentClassifier` uses constrained decoding. Promotion to helper has value for Cat 5 (Prompts), not Cat 1. |
| Confidence + threshold + fallback semantics | PARTIAL — pattern, not interface | 3 of 4 share this. Documenting as "Spaarke Intent Classifier Pattern" is feasible; extracting as `interface IConfidenceClassifier` is over-abstraction per ADR-010. |

#### §4.1.3 Consolidation verdict + canonical recommendation

**REJECT** generic `IIntentClassifier<TResult>` per:
- ADR-010 §"MUST NOT create interfaces without genuine seam requirement" — no seam; each is unique impl of its decision space.
- Input/output shapes not shape-compatible without information loss.
- Multi-stage cascade designs not orthogonal.
- Specialized telemetry/concurrency/OTEL surfaces.

**RECOMMEND** instead: **NAME the canonical pattern, not the interface.** Q-004 "Spaarke Canonical Intent Classification Pattern":
1. **Singleton** when stateless (CapabilityRouter, InsightsIntentClassifier). **Factory-instantiated** when per-request data is load-bearing (PlaybookDispatcher tenantId).
2. **`IOpenAiClient`** for LLM calls (not direct Azure SDK).
3. **JSON-schema-constrained decoding** preferred where output is closed/enumerable (InsightsIntentClassifier exemplar).
4. **Confidence + threshold + fallback** as documented semantic pattern.
5. **Null-Object kill-switch P3 Fail-fast** for DI-registered classifiers (per ADR-032).
6. **OTEL activity per stage** `<service>.<stage>` (CapabilityRouter per-layer span exemplar).
7. **SHA-256 of normalized input** for cache keys when caching in scope (InsightsIntentClassifier exemplar).

Document as descriptive pattern (NOT binding interface). Defer to W4 ADR phase.

### §4.2 LATENT BUG cross-reference (Cat 6 §4.1)

W1 Sub-Agent C surfaced HIGH severity LATENT BUG: `IInsightsAi` registered unconditionally but ctor transitively depends on conditional `IOpenAiClient` + `IAiPlaybookBuilderService` — produces 500 instead of 503 under compound-OFF.

**Cat 1 cross-reference**: `InsightsIntentClassifier` runs INSIDE `IInsightsAi.AssistantQueryAsync` via `AssistantToolCallHandler`. The Cat 1 classifier itself is NOT involved in the latent bug — its DI is correct (`AnalysisServicesModule.cs:265-270` + `:519-533` dual-registration ADR-032 §F.1 forward-mitigation). **The bug is at the facade wrapping layer (`IInsightsAi → InsightsOrchestrator`), not the classifier layer.** Cat 1 analysis NOT invalidated.

When Cat 6's `NullInsightsAi` P3 peer is added:
- Endpoint catches `FeatureDisabledException` from `NullInsightsAi` → 503.
- `InsightsIntentClassifier` path doesn't fire under compound-OFF (facade rejects first).
- Cat 1's KEEP recommendation for `InsightsIntentClassifier` (§3 row 4) is robust under both pre- and post-Cat6-remediation states.

### §4.3 Cat 7 (Sub-Agent D) handoff — `AiAnalysisNodeExecutor`

Sub-Agent D's handoff: "if Cat 1 consolidates classifiers, AiAnalysisNodeExecutor's role may shift."

**Cat 1 verdict impact**: Since Cat 1 REJECTS consolidation (§4.1.3), `AiAnalysisNodeExecutor`'s legacy `IToolHandlerRegistry` bridge role is UNCHANGED. Sub-Agent D's KEEP-WITH-CONCERN classification stands. **No re-dispatch of Cat 7 required for classifier reasons.** (Cat 3 search-substrate may still trigger separate re-dispatch.)

### §4.4 Cat 4 (Sub-Agent A) handoff — cache adoption opportunity

Sub-Agent A's W2 dispatch: "3 of 4 classifiers cache without canonical wrapper."

**Cat 1 verdict**: Cache backing is out-of-scope per brief §4. However recommending the canonical pattern (§4.1.3) explicitly include `DistributedCacheExtensions.GetOrCreateAsync<T>` aligns Cat 1 + Cat 4 without overstepping. Specifically:
- `PlaybookDispatcher.cs:405,453` could swap `_cache.GetStringAsync` / `SetStringAsync` for `GetOrCreateAsync<DispatchResult>` — one-method swap.
- `InsightsIntentClassifier.cs:129,203-207` uses `IMemoryCache.TryGetValue` + `CreateEntry`; ADR-009 in-process exception acceptable per `OrchestratorPromptBuilder` precedent IF documented.

Surfaced for W3 cross-coordination per Q-003.

### §4.5 Cat 5 (Prompts) precondition

Cat 5's W3 expectations from Cat 1 verdict:
- **1 prompt deletion**: `PlaybookBuilderSystemPrompt.cs` cascades with #3 (per §3.1).
- **3 inline prompts to evaluate**: `CapabilityClassificationPromptBuilder` (CapabilityRouter Layer 2 helper), `PlaybookDispatcher.RefineWithLlmAsync()` inline system prompt (line 294-300), `InsightsIntentClassifier.BuildPrompt()` inline StringBuilder (line 230-274). Each different style.
- Cat 5 evaluates extraction-to-builder candidates; ADR-010 minimalism likely forbids forced abstraction.

### §4.6 Single-LLM-call invariant

CLAUDE.md §16 references `CHAT-ATTACHMENT-POLICY.md` "single-LLM-call invariant." All 4 classifiers individually uphold this (each makes 0 or 1 LLM call). Consolidation would not improve enforcement. Status quo correct.

---

## §5 Canonical naming candidates (Q-004 framing — candidates only)

### §5.1 Candidate A: "Spaarke Canonical Intent Classification Pattern" (descriptive pattern doc, NO interface)
- Codifies §4.1.3's 7-element pattern.
- Pros: ADR-010 compliant; captures empirical best practice; applicable to FUTURE classifiers without forcing consolidation.
- Cons: pattern docs weaker enforcement than interfaces.
- **Sub-Agent E favors this candidate.**

### §5.2 Candidate B: "Spaarke Intent Classifier Stack" (umbrella for 3 specialists)
- Frames CapabilityRouter + PlaybookDispatcher + InsightsIntentClassifier as a "stack" of specialist classifiers.
- Pros: aligns with Q-004 "Spaarke Canonical X Stack" naming convention; mirrors Sub-Agent A's "canonical specialists" framing.
- Cons: invites re-litigation of consolidation question.

### §5.3 Candidate C: Promote `InsightsIntentClassifier` as canonical reference impl
- "InsightsIntentClassifier is the canonical reference implementation. New classifiers MUST mirror its pattern."
- Pros: concrete reference; easier to apply.
- Cons: ties pattern to one specific impl.

**Sub-Agent E recommendation**: §5.1 (pattern doc) as primary canonical naming; §5.3 (InsightsIntentClassifier as reference impl) as the worked example.

---

## §6 Drift report (357e6936 → d862bec6)

### §6.1 Commits since snapshot
```
d862bec6 Merge remote-tracking branch 'origin/master' into work/audit-r1-phase2-wave1
788aee1e docs(bff-ai-audit): Phase 2 Wave 1 — 4 per-category analyses + summary
83e9e2ae Merge pull request #340 from spaarke-dev/work/insights-engine-r2-090-wrap-up
[plus W1 baseline 12275b10 commits]
```

### §6.2 Code drift in scope
`git diff --stat 357e6936..HEAD -- src/server/api/Sprk.Bff.Api/Services/Ai/` returns EMPTY. **ZERO code drift.** All 4 classifier sources + DI + options + Null peer byte-identical to snapshot.

### §6.3 Numeric reconciliation

| Inventory claim | HEAD reproduction | Match? |
|---|---|---|
| CapabilityRouter production consumers | 4 | OVERCOUNTED → 2 behavioral + 2 sentinel/telemetry. State remains ACTIVE. |
| PlaybookDispatcher production consumers | 4 via factory | OVERCOUNTED → 3 behavioral + 1 sentinel. State remains ACTIVE. |
| IntentClassificationService consumers | 0 (ORPHAN) | EXACT MATCH |
| InsightsIntentClassifier consumer count | 1 | EXACT MATCH |
| `IntentClassificationResult` shape collision | "2 types same name" | UNDERCOUNTED → 3 related types (see §2.4) |
| `NullInsightsIntentClassifier` ctor anti-smell (W1 Sub-Agent A flag) | `IMemoryCache` dep claim | INVENTORY WAS CORRECT; W1 false-positive REJECTED (ctor takes only `ILogger<T>`) |

### §6.4 W1 Sub-Agent A claim corrections
- `NullInsightsIntentClassifier` cache-dep anti-smell → REJECTED (§2.5).
- `AiPlaybookBuilderService` AT-RISK cascade flag → REJECTED (§2.3.3 — independent service, has its own inline classifier, 4 production consumers).

---

## §7 Open questions for owner review (packaged per Q-002)

1. **(HIGH PRIORITY) Confirm `IntentClassificationService` orphan + cascade DELETE** (§3 row 3 + §3.1). Cascade: `IntentClassificationService.cs` (408 LOC) + `PlaybookBuilderSystemPrompt.cs` + likely `ClarificationService.cs` + 3 DI lines. ~1000-1100 LOC. **AI Chat Playbook Builder team confirmation needed** — aspirational scaffold or abandoned feature?

2. **(HIGH PRIORITY) `ClarificationService` cascade verification** — single Grep task (~5 min) deferred to W3 follow-on; confirms whether cascade is 1-file or 3-file.

3. **(MEDIUM) Type B `IntentClassificationResult` rename** (§3 row 6). Candidate: `InsightsRoutingDecision`. Namespace disambiguation sufficient or explicit rename?

4. **(LOW) Canonical pattern doc adoption** (§4.1.3 + §5.1). Becomes `.claude/patterns/` entry or constraint section in `bff-extensions.md`?

5. **(LOW) `PlaybookDispatcher.cs:99-102` XML doc amendment** — lead with tenant-scoping rationale, ADR-010 secondary. Inventory §7.1 third bullet → REJECTED.

6. **(LOW) Inventory drift labels** — CapabilityRouter "4 production" → "2 behavioral + 2 sentinel/telemetry"; PlaybookDispatcher "4 production via factory" → "3 behavioral + 1 sentinel".

---

## §8 ADR candidates (per Q-005 — bullets only, NOT authored)

- **ADR-CAND-E-01 (HIGH)**: "Spaarke Canonical Intent Classifier Pattern" — codifies §4.1.3's 7-element pattern. Descriptive, NOT binding interface. Cross-refs ADR-010, ADR-013, ADR-032. Closes inventory §7.1 first bullet definitively.

- **ADR-CAND-E-02 (MEDIUM)**: "BFF Tenant-Scoping → Factory-Instantiation Rule" — generalizes §2.6 + §3 row 2 finding. Per-request tenantId-consuming services MUST be factory-instantiated. Could be `bff-extensions.md` section rather than ADR.

- **ADR-CAND-E-03 (MEDIUM)**: "Public API Type Naming for Result Shapes" — domain-qualified names required when same name appears in different namespaces. Addresses §2.4.

- **ADR-CAND-E-04 (LOW)**: "Inline LLM Prompts vs PromptLibrary adoption" — defer to Cat 5; surfaces from §4.5.

- **ADR-CAND-E-05 (LOW, cross-cutting with Cat 4)**: `DistributedCacheExtensions.GetOrCreateAsync<T>` adoption for `PlaybookDispatcher` + `InsightsIntentClassifier`. Defer to Cat 4's ADR-CAND-A-01.

---

## §9 W3 dispatch implications

### §9.1 Cat 5 (Prompts) precondition
- **1 prompt deletion**: `PlaybookBuilderSystemPrompt.cs` cascades.
- **3 inline-prompt evaluation candidates**: CapabilityClassificationPromptBuilder helper, PlaybookDispatcher inline prompt, InsightsIntentClassifier inline StringBuilder.

### §9.2 Cat 7 deferred — NOT triggered by Cat 1
Per §4.3, Cat 1's REJECT of consolidation means `AiAnalysisNodeExecutor` legacy bridge is UNCHANGED. **Cat 7 deferred re-dispatch NOT triggered by Cat 1.** Cat 3 may still trigger separately.

### §9.3 Cat 4 coordination
Bundle `DistributedCacheExtensions.GetOrCreateAsync<T>` adoption for `PlaybookDispatcher` + `InsightsIntentClassifier`.

### §9.4 Cat 6 cross-reference — LATENT BUG
`NullInsightsAi` remediation does NOT block Cat 1; independent fixes. Cat 1's `InsightsIntentClassifier` KEEP is robust under both states (§4.2).

### §9.5 NEW W3 verification step
`IClarificationService` orphan cascade Grep — single-grep task; ~5 min; determines whether §3.1 cascade is 1-file or 3-file DELETE.

---

# Sub-Agent E Final Status Report

1. **Status**: COMPLETED (8/8 sections + §9 W3 dispatch implications)
2. **Output file path**: `projects/bff-ai-architecture-audit-r1/notes/phase2/analysis-intent-classification.md`
3. **Classifiers analyzed**: 4 classifiers + 3 related result-shape types + 2 W1 cross-cutting corrections
4. **Decision distribution**:
   - **KEEP**: 3 classifiers (CapabilityRouter, PlaybookDispatcher, InsightsIntentClassifier) + 1 active result type (Type C `IntentClassification`)
   - **DELETE**: 1 classifier (`IntentClassificationService` orphan) + 1 result type (Type A) + cascade likely includes `PlaybookBuilderSystemPrompt.cs` and (pending verification) `ClarificationService.cs`
   - **RENAME (LOW priority)**: 1 result type (Type B `IntentClassificationResult` namespace disambiguation)
   - **CONSOLIDATION FEASIBILITY VERDICT**: **REJECT** generic `IIntentClassifier<TResult>`. Recommend pattern-doc canonical naming (§4.1.3 + §5.1 Candidate A).
5. **Drift findings**:
   - ZERO code drift `357e6936` → `d862bec6` in `Services/Ai/`.
   - `IntentClassificationService` ORPHAN CORROBORATED rigorously; W1 Sub-Agent A's "AiPlaybookBuilderService at-risk" flag REJECTED.
   - W1 Sub-Agent A's `NullInsightsIntentClassifier` cache-dep anti-smell REJECTED (ctor takes only `ILogger<T>`).
   - Inventory undercounted: **3 types** named-similarly (Type A + B + C), not 2.
   - Inventory overcounted CapabilityRouter (4→2 behavioral) and PlaybookDispatcher (4→3 behavioral) consumers.
6. **Cross-cutting observations**:
   - Central architectural question (consolidation) has clear empirical NO with detailed multi-axis rationale.
   - `IntentClassificationService` deletion is 3-4 file cascade, bundling with Cat 2 lookup cleanup for ~20-30 KB compressed publish win.
   - LATENT BUG (Cat 6 §4.1) does NOT invalidate Cat 1 — classifier DI is correct; bug is at facade wrapping layer.
   - Cat 7 NOT re-triggered by Cat 1 verdict.
   - `PlaybookDispatcher` factory instantiation load-bearing (per-request tenantId), not just ADR-010 budget — DI promotion REJECTED with empirical justification.
7. **Open questions surfaced**: 6 items in §7; highest priority = confirm orphan cascade DELETE + verify `ClarificationService` cascade via W3 follow-on Grep.
8. **Recommendations for W3 dispatch**:
   - Cat 5 (Prompts): prepare for 1 prompt deletion (`PlaybookBuilderSystemPrompt.cs`) + 3 inline-prompt evaluations. ADR-010 likely forbids forced consolidation.
   - Cat 7 deferred: NOT triggered by Cat 1; only Cat 3 search-substrate may trigger.
   - Cat 4 coordination: bundle `DistributedCacheExtensions.GetOrCreateAsync<T>` adoption for `PlaybookDispatcher` + `InsightsIntentClassifier`.
   - Cat 6 cross-reference: `NullInsightsAi` remediation does NOT block Cat 1; independent fixes.
   - **NEW W3 verification step**: `IClarificationService` orphan Grep (~5 min effort) to scope the §3.1 cascade.
