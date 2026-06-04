# Phase 2 Wave 2 Summary — Aggregation of Cat 1 + Cat 3 Sub-Agent Analyses

> **Authored by**: Main session (aggregation after W2 Cat 1 + Cat 3 sub-agents completed)
> **Pinned to**: commit `357e6936` (Phase 1 inventory snapshot)
> **HEAD at aggregation time**: `d862bec6` (same HEAD W2 sub-agents observed; ZERO code drift confirmed by both sub-agents)
> **Date**: 2026-06-04
> **Source documents**:
> - [`analysis-intent-classification.md`](analysis-intent-classification.md) — Cat 1 (Sub-Agent E)
> - [`analysis-search.md`](analysis-search.md) — Cat 3 (Sub-Agent F)
> - W1 baseline: `wave-1-summary.md` + 4 W1 analysis docs

---

## §1 Per-category distillation

### §1.1 Cat 1 — Intent classification (Sub-Agent E)

**Headline**: The central architectural question of the audit — "can the 4 classifiers consolidate behind `IIntentClassifier<TResult>` strategy?" — has a clear empirical **NO**. Output shapes, input contexts, multi-stage cascade designs, and telemetry/concurrency surfaces all differ materially. Forced abstraction would lose type safety AND not improve enforcement. The recommendation is to NAME the canonical pattern, not the interface.

**Decision shape**: 3 KEEP classifiers (CapabilityRouter, PlaybookDispatcher, InsightsIntentClassifier) + 1 KEEP type (`IntentClassification` in PlaybookBuilderService) + **1 DELETE classifier** (`IntentClassificationService` orphan) + 1 DELETE type bundled + 1 RENAME-LOW (Type B `IntentClassificationResult` namespace disambiguation) + **0 CONSOLIDATIONS** + **CONSOLIDATION FEASIBILITY VERDICT: REJECT** generic `IIntentClassifier<TResult>`.

**Cascade DELETE confirmed at 3 files** (verified by main session's grep): `IntentClassificationService.cs` (408 LOC) + `PlaybookBuilderSystemPrompt.cs` (~100 LOC est) + `ClarificationService.cs` (~400-600 LOC est) + 3 DI lines. ~1000-1100 LOC. Bundles with W1's lookup-service DELETE PR for ~20-30 KB compressed publish-size win.

**W1 false-positive corrections**:
- W1 Sub-Agent A's `NullInsightsIntentClassifier` cache-dep anti-smell → **REJECTED**. Empirical reading: ctor takes ONLY `ILogger<T>`; §F.1 compliance is intact.
- W1 Sub-Agent A's `AiPlaybookBuilderService` at-risk cascade flag → **REJECTED**. Empirical reading: independent service with its own inline classifier and 4 production consumers; no cascade risk.

**Inventory corrections**:
- 3 IntentClassification-named types exist (not 2 as inventory claimed): Type A (`Services.Ai`, tied to orphan, DELETE), Type B (`Services.Ai.Insights.Routing`, RENAME-LOW), Type C (`Services.Ai.IntentClassification` in PlaybookBuilderService, KEEP).
- CapabilityRouter consumers 4 → 2 behavioral + 2 sentinel/telemetry.
- PlaybookDispatcher consumers 4 → 3 behavioral + 1 sentinel.

**Surprises**:
- `PlaybookDispatcher` factory-instantiation is LOAD-BEARING for per-request tenantId (not just ADR-010 budget) — inventory §7.1 bullet 3 (DI promotion) REJECTED with empirical justification.
- `InsightsIntentClassifier` recommended as canonical reference impl (Singleton, options-bound, ADR-032 §F.1 dual-registration, JSON-schema-constrained decoding, OTEL-instrumented).

### §1.2 Cat 3 — Search services (Sub-Agent F)

**Headline**: The Null-peer asymmetry (1-of-4 has Null peer) has a clean architectural answer: **endpoint mapping conditionality drives Null-peer requirements**. `RagEndpoints` is mapped unconditionally because the RAG knowledge base is fundamental to Spaarke's value prop; the other 3 substrates are conditionally mapped SYMMETRICALLY with their DI registration. The asymmetry is INTENTIONAL and CORRECT — NOT a §F.1 anti-pattern.

**Decision shape**: **4 KEEP** (all substrates) + 1 KEEP-PENDING-SECURITY-REVIEW (`RecordSearchService` overlapping with #3) + **3 explicit DO-NOT-ADD-Null-peer** (responding to inventory §7.3 bullet 1) + 1 REJECT-consolidation (PlaybookEmbedding ↔ SemanticSearch).

**RagService/NullRagService designated as the canonical reference impl** for ADR-032 double-gate sub-mechanism (compound-AI gate + inner credentials gate; both OFF branches register Null peer). This is the gold-standard pattern for future BFF services depending on configurable Azure-service credentials.

**LATENT BUG verification (Cat 6 cross-check)**: `IRagService` layer is CLEAN. The Cat 6 LATENT BUG fires upstream in `InsightsOrchestrator`'s transitive-conditional ctor deps (`IOpenAiClient` + `IAiPlaybookBuilderService`) BEFORE reaching `_ragService.SearchAsync`. Cat 6's `NullInsightsAi` remediation is the correct fix; Cat 3 needs no additional Null peers.

**PlaybookEmbedding ↔ SemanticSearch consolidation REJECTED** with multi-axis empirical rationale (different indices, document shapes, security models, API surfaces, lifecycles, DI patterns). Mirrors Cat 1's REJECT verdict for classifier consolidation.

**Tenant + privilege filter matrix surfaced as documentable architectural distinction**:
- RagService: MANDATORY tenant + MANDATORY privilege filter (AIPU2-027 fail-closed)
- SemanticSearchService: tenant filter only; **privilege-filter gap surfaced for security adjudication**
- RecordSearchService: NO tenant filter; relies on Dataverse-mediated security; **layered-defense reasoning internally consistent IF assumptions hold; surfaced for security adjudication**
- PlaybookEmbeddingService: NO filters; system-config artifacts (admin-scoped)

**Inventory corrections** (overlap with Cat 1's):
- §2.3.2: `DocumentClassifierHandler` imports `IRagService` at HEAD, NOT `ISemanticSearchService`.
- §2.3.3: `AiAnalysisNodeExecutor` is an `IRecordSearchService` consumer (inventory missed; Cat 7 caught; Cat 3 confirms).

---

## §2 Cross-cutting findings spanning W2 + W1

### §2.1 "REJECT forced consolidation" is the dominant W2 verdict

Both W2 sub-agents reached the same conclusion via independent empirical analysis: **neither classifiers nor search substrates should be forced behind generic abstractions**. The reasons are parallel:
- Output shapes are domain-specific and would lose type safety if abstracted.
- Multi-stage cascade designs differ structurally (Cat 1: 3-tier vs 2-stage vs single-call; Cat 3: hybrid RRF vs vector-only vs metadata-only).
- Telemetry, concurrency, and security models are per-substrate.
- The shared substrate (Azure OpenAI / Azure AI Search) is a thin coincidence that does not justify abstraction.

**Implication for audit deliverable**: the "Spaarke Canonical AI Stack" framing (Q-004 lock) should be **descriptive pattern documentation, NOT binding interface abstractions**. Sub-Agents E and F both recommend pattern-doc canonicalization with InsightsIntentClassifier (Cat 1) and RagService (Cat 3) as the canonical reference impls.

### §2.2 W1 false-positives corrected

Cat 1 (Sub-Agent E) corrected 2 W1 Sub-Agent A claims:
- `NullInsightsIntentClassifier` cache-dep anti-smell → REJECTED (ctor takes only `ILogger<T>`).
- `AiPlaybookBuilderService` at-risk cascade flag → REJECTED (independent service; own inline classifier; 4 production consumers).

This validates the empirical-reproduction-FIRST methodology: cross-sub-agent verification catches mistakes that single-pass analysis misses.

### §2.3 Cat 6 LATENT BUG cross-references reinforced

Both Cat 1 and Cat 3 verified the LATENT BUG cross-reference:
- Cat 1 §4.2: `InsightsIntentClassifier` runs INSIDE `IInsightsAi.AssistantQueryAsync`. The Cat 1 classifier itself is NOT involved in the latent bug — its DI is correct. The bug is at the facade wrapping layer.
- Cat 3 §2.5: `IRagService` layer is CLEAN. `NullRagService` works correctly. The Cat 6 bug fires upstream in `InsightsOrchestrator` ctor BEFORE reaching `_ragService.SearchAsync`.

**Cat 6's `NullInsightsAi` remediation is the correct fix** and addresses both Cat 1 and Cat 3 transitive consumer paths.

### §2.4 Cat 4 cache consolidation lever expanded

W2 surfaces NEW Cat 4 consolidation opportunities:
- Cat 1: 2 cache call sites (`PlaybookDispatcher.cs:405,453` + `InsightsIntentClassifier.cs:129,203-207`) ready for `DistributedCacheExtensions.GetOrCreateAsync<T>` adoption (one-method swap for PlaybookDispatcher; ADR-009 in-process exception documentation for InsightsIntentClassifier).
- Cat 3 §4.5: 3-of-4 search services share `IEmbeddingCache` "try cache → cache miss → generate → cache" pattern; extract to `IEmbeddingCache.GetOrGenerateAsync` extension method.
- Cat 3 §4.7: `RecordSearchService` inline `IDistributedCache` per query (W1 Cat 4 row 18) confirmed.
- Cat 3 §4.6: `PlaybookEmbeddingService.SearchPlaybooksAsync` lacks `IEmbeddingCache` lookup — latency cost in `PlaybookDispatcher` Stage 1.

### §2.5 ZERO code drift across all W2 sub-agents (continues W1 pattern)

Both Cat 1 and Cat 3 independently verified `git diff --stat 357e6936..d862bec6 -- src/server/api/Sprk.Bff.Api/Services/Ai/` returns EMPTY. The 5 commits since snapshot are docs/scaffold + W1 outputs only. Inventory `357e6936` remains fully valid at HEAD `d862bec6` for all W2 categories.

### §2.6 New security adjudication surfaces for Q-002 owner review

Cat 3 surfaces 2 new security questions for end-of-audit owner review:
1. **`RecordSearchService` security model**: tenant isolation disclaimed in favor of Dataverse-layer security. Layered-defense reasoning internally consistent IF (a) index carries only non-sensitive metadata projections, (b) every consumer chain resolves through Dataverse before exposing content, (c) correlation-attack entropy risk is acceptable. **Security team + Record-Matching feature team adjudication needed.**
2. **`SemanticSearchService` privilege-filter gap**: only `RagService` mandates a privilege-group filter (AIPU2-027 fail-closed). `SemanticSearchService` filters by tenant but NOT by privilege groups. **Intentional access-model difference or security gap predating AIPU2-027?**

W1 already surfaced 1 security adjudication question:
3. **`PrivilegeGroupResolver` IMemoryCache for per-user privileges** (W1 Cat 4 §4.4): is this caching DATA (allowed under ADR-009) or DECISIONS (forbidden)?

**3 distinct security adjudication surfaces total** — all routed to Security team per Q-003 sequential coordination.

### §2.7 Cat 7 deferred re-dispatch NOT triggered

Both Cat 1 (Sub-Agent E §4.3) and Cat 3 (Sub-Agent F §8 Recommendations) explicitly confirm:
- Cat 1's REJECT of classifier consolidation → `AiAnalysisNodeExecutor`'s legacy `IToolHandlerRegistry` bridge UNCHANGED.
- Cat 3's KEEP verdicts for all 4 substrates → `AiAnalysisNodeExecutor` + `IndexRetrieveNode` consumer contracts UNCHANGED.

**Cat 7 deferred re-dispatch is NOT triggered by W2.** Sub-Agent D's W1 analysis remains valid in full.

---

## §3 Decision distribution roll-up (W2 totals)

| Category | KEEP | KEEP-with-CAVEAT | CONSOLIDATE | DELETE | REJECT-add-Null | RENAME |
|---|---|---|---|---|---|---|
| Cat 1 (Intent classification) | 3 classifiers + 1 type = 4 | — | 0 (REJECTED) | 1 classifier + 1 type + 2 cascade files = 4 | — | 1 type (low priority) |
| Cat 3 (Search services) | 4 substrates | 1 (RecordSearch pending security review) | 0 (REJECTED) | 0 | 3 (responding to inventory §7.3 bullet 1) | — |
| **W2 unique totals** | **8** | **1** | **0** | **4** | **3** | **1** |

**W2 unique action items synthesized**:
1. **DELETE orphan cluster** (3-file cascade): `IntentClassificationService` + `PlaybookBuilderSystemPrompt` + `ClarificationService` + 3 DI lines. ~1000-1100 LOC. **Bundles with W1 lookup-service DELETE PR** for ~20-30 KB compressed publish-size win.
2. **Designate canonical reference impls**: `InsightsIntentClassifier` (Cat 1) + `RagService`/`NullRagService` (Cat 3 — double-gate Null-Object pattern).
3. **Document canonical patterns** (NOT binding interfaces): "Spaarke Canonical Intent Classification Pattern" (7-element from Cat 1 §4.1.3) + "Spaarke Canonical Search Substrates" (4-substrate from Cat 3 §5.1).
4. **Surface 2 new security questions** for end-of-audit owner review (RecordSearchService model; SemanticSearchService privilege-filter gap).
5. **Type B `IntentClassificationResult` rename** (LOW priority): `InsightsRoutingDecision` candidate; or namespace disambiguation sufficient.
6. **Cat 4 cache lever expansion**: 2 new classifier sites + 1 new search-cache extension method opportunity.

**W1+W2 combined unique action items** (excluding W1 already-summarized):

| Action | LOC impact | Bundling opportunity |
|---|---|---|
| DELETE 3 lookup orphans + dangling cref + FinanceModule cleanup (from W1 Cat 2) | ~714 LOC | Standalone single PR |
| DELETE 3-file intent classifier cascade (from W2 Cat 1) | ~1000-1100 LOC | **Bundles with above** for single ~1700-1800 LOC DELETE PR |
| ADD 4 Null-peer facades + DI rewire (from W1 Cat 6) | ~150 LOC | Standalone |
| `IBriefingAi` consumer cleanup (from W1 Cat 6) | ~30 LOC | Bundles with above Null-peer PR |
| Cat 4 cache adoption migration | 21 sites + 5 new W2 sites = 26 sites | Phased per-team |
| Doc bug fix `AnalysisServicesModule.cs:75-79` (from W1 Cat 6) | 1 line | Trivial PR |
| `PlaybookDispatcher.cs:99-102` XML doc amendment (from W2 Cat 1) | 5 lines | Bundles with above |
| `IntentClassificationResult` Type B rename (LOW from W2 Cat 1) | ~20 LOC | Standalone or deferred |
| Author `ACTION-TYPE-REGISTRY.md` (from W1 Cat 7) | ~100-200 LOC doc | Standalone |
| Canonical pattern docs (W2 outputs) | ~400-600 LOC docs | Phase 3 deliverable |

---

## §4 HIGH-URGENCY findings carried forward + new

### §4.1 HIGH-URGENCY from W1 (unchanged)
- **LATENT BUG**: `IInsightsAi` 500 instead of 503 under compound-AI-OFF. Remediation: add `NullInsightsAi` + DI rewire + integration test. **Cat 1 and Cat 3 W2 verifications CONFIRM** the bug is at the `InsightsOrchestrator` facade wrapping layer, NOT downstream in classifier or search layers.

### §4.2 NEW MEDIUM-URGENCY from W2
- **2 security adjudication surfaces**: `RecordSearchService` tenant-isolation model (Cat 3 §2.7) + `SemanticSearchService` privilege-filter gap (Cat 3 §4.3).
- **`PlaybookDispatcher` XML doc misleading**: leads with ADR-010 budget rationale; empirically the load-bearing rationale is per-request `tenantId` capture (Cat 1 §2.6).

### §4.3 NEW LOW-URGENCY from W2
- **Inventory drift labels**: 3 classifier-consumer counts overcounted; 2 search-consumer mislabels (DocumentClassifierHandler, AiAnalysisNodeExecutor↔IRecordSearchService).
- **3 `IntentClassification*` types share semantic noun**: Type A (DELETE), Type B (RENAME-LOW), Type C (KEEP).

---

## §5 Drift summary (snapshot 357e6936 → HEAD d862bec6)

| Category | Code drift (`Services/Ai/`) | Inventory accuracy | Consumer-side drift |
|---|---|---|---|
| Cat 1 (Intent classification) | ZERO | 3 minor numeric corrections (CapabilityRouter 4→2; PlaybookDispatcher 4→3; `IntentClassificationResult` types 2→3) | None |
| Cat 3 (Search services) | ZERO | 2 minor mislabels (DocumentClassifierHandler is IRagService; AiAnalysisNodeExecutor is IRecordSearchService consumer) | None |

**Aggregate verdict**: Phase 1 inventory remains authoritative. Minor numeric/label corrections do NOT alter any W2 recommendation. Snapshot `357e6936` continues to be the canonical reference point for the audit.

---

## §6 W3 dispatch recommendations

### §6.1 W3 scope (Cat 5 Prompts only)

Per the locked priority order (W1 summary §6.1), W3 = Cat 5 (Prompts). W2 fulfilled the prerequisite:
- Cat 1 → REJECT classifier consolidation; 1 prompt deletion (`PlaybookBuilderSystemPrompt.cs` cascades with orphan DELETE); 3 inline-prompt evaluation candidates.
- Cat 3 → REJECT search-substrate consolidation; KEEP all 4 substrates; no consumer-contract changes.
- **Cat 5 Prompts precondition: MET.**

### §6.2 Cat 5 (Prompts) inputs from W2

| Input | Source | W3 Cat 5 implication |
|---|---|---|
| `PlaybookBuilderSystemPrompt.cs` cascades with orphan DELETE | W2 Cat 1 §3.1 | 1 prompt deletion is in-scope for Cat 5 verification |
| `CapabilityClassificationPromptBuilder` is CapabilityRouter Layer 2 helper | W2 Cat 1 §4.5 | Evaluate as canonical prompt-builder pattern reference |
| `PlaybookDispatcher.RefineWithLlmAsync()` inline system prompt (line 294-300) | W2 Cat 1 §4.5 | Inline-vs-builder evaluation candidate |
| `InsightsIntentClassifier.BuildPrompt()` inline StringBuilder (line 230-274) | W2 Cat 1 §4.5 | Inline-vs-builder evaluation candidate |
| `PromptLibrary` Cosmos-backed service exists, limited adoption | Phase 1 inventory §2.5 + W1 summary | Adoption-driver question for Cat 5 |
| Cat 1 and Cat 3 BOTH recommend pattern-doc (not interface) canonicalization | W2 §2.1 | Apply same framing to Cat 5 — likely pattern-doc, not interface |

### §6.3 W3 sub-agent brief MUST include (lessons learned from W1+W2)

- Self-contained brief with verbatim inventory quote (every sub-agent has benefited)
- Explicit OUT-OF-SCOPE list (preventing drift)
- HARD GATE on delete recommendations (W1+W2 applied this rigorously)
- Empirical-reproduction-FIRST rule (W2 Cat 1 corrected 2 W1 false positives via this)
- **Harness write-block acknowledgment** (all 6 W1+W2 sub-agents hit this)
- Read prior outputs (W1 + Cat 1 + Cat 3) as peer context
- Track which W1/W2 false-positives or assumptions need verification (cross-sub-agent validation is valuable)

### §6.4 Cat 7 deferred re-dispatch — NOT TRIGGERED

Both Cat 1 and Cat 3 explicitly confirm: Cat 7's `AiAnalysisNodeExecutor` + `IndexRetrieveNode` consumer contracts UNCHANGED by W2 verdicts. Sub-Agent D's W1 analysis remains valid in full. **Cat 7 deferred re-dispatch is NOT in W3 scope.**

### §6.5 W4 (DI + Configuration) precondition

W4 is the LAST wave per locked sequencing (W1 plan + Q-001 scope). It depends on:
- W1 Cat 4 (Cache patterns) — DI registration patterns surfaced
- W1 Cat 6 (Public Contracts) — facade DI fascia (`AddPublicContractsFacade` + `AddNullObjectsForCompoundOff` + `AddInsightsFacadeModule`)
- W1 Cat 7 (Node executors) — `INodeExecutor` registry pattern
- W2 Cat 1 (Intent) — `AnalysisServicesModule.cs:372` orphan DI line removal
- W2 Cat 3 (Search) — `Configuration/` vs `Options/` namespace split surfaces; inconsistent `IOptions<T>` adoption

**All preconditions MET after W3 completes.** W4 can dispatch immediately following Cat 5.

---

## §7 Packaged for end-of-audit owner review (Q-002 — additions to W1 summary §7)

The following W2 questions augment W1's packaged-for-owner-review list. They are NOT to be answered mid-wave; all packaged for SINGLE end-of-audit review per Q-002.

### §7.1 Confirm REJECT verdicts

1. **(Cat 1) Generic `IIntentClassifier<TResult>` consolidation REJECTED** with multi-axis empirical rationale (Cat 1 §4.1.3). Owner accepts pattern-doc canonicalization instead? (W2 Cat 1 §7.4 + W2 Cat 1 §5.1 Candidate A)
2. **(Cat 3) `PlaybookEmbeddingService` ↔ `SemanticSearchService` consolidation REJECTED** (Cat 3 §2.6). Owner accepts?
3. **(Cat 3) DO-NOT-ADD-Null-peers** for `SemanticSearchService` + `RecordSearchService` + `PlaybookEmbeddingService` (Cat 3 §2.4 + §3 rows 5-7) — endpoint-mapping symmetry is the architectural answer to inventory §7.3 bullet 1.

### §7.2 Confirm DELETE verdicts

4. **(Cat 1) `IntentClassificationService` orphan + 3-file cascade DELETE** confirmed by main session grep (cascade includes `PlaybookBuilderSystemPrompt.cs` + `ClarificationService.cs`). ~1000-1100 LOC. **AI Chat Playbook Builder team confirmation needed per Q-003.**

### §7.3 New security adjudication surfaces

5. **(Cat 3) `RecordSearchService` tenant-isolation model** (Cat 3 §2.7). Security team + Record-Matching feature team.
6. **(Cat 3) `SemanticSearchService` privilege-filter gap** (Cat 3 §4.3). Security team.

### §7.4 Designate canonical reference impls

7. **(Cat 1) `InsightsIntentClassifier` as canonical reference for intent classifier pattern** (Cat 1 §3 row 4 + §5.3).
8. **(Cat 3) `RagService`/`NullRagService` as canonical reference for ADR-032 double-gate sub-mechanism** (Cat 3 §4.2). Currently undocumented.

### §7.5 Type rename adjudication

9. **(Cat 1) Type B `IntentClassificationResult` rename** (LOW priority). Candidate: `InsightsRoutingDecision`. Namespace disambiguation sufficient or explicit rename?

### §7.6 Documentation amendments

10. **(Cat 1) `PlaybookDispatcher.cs:99-102` XML doc amendment** — lead with tenant-scoping rationale (load-bearing), ADR-010 budget secondary.
11. **(Cat 3) Inventory minor corrections** — `DocumentClassifierHandler` is `IRagService` consumer; `AiAnalysisNodeExecutor` is `IRecordSearchService` consumer.
12. **(Cat 1) Inventory drift labels** — CapabilityRouter "4 production" → "2 behavioral + 2 sentinel/telemetry"; PlaybookDispatcher "4 production via factory" → "3 behavioral + 1 sentinel"; `IntentClassificationResult` types 2 → 3.

### §7.7 ADR candidates (per Q-005 DEFERRED to follow-on phase; consolidated W2 additions)

| # | ADR candidate | Source | Priority |
|---|---|---|---|
| W2-1 | **Spaarke Canonical Intent Classifier Pattern** — 7-element pattern doc, NOT binding interface | Cat 1 ADR-CAND-E-01 | HIGH |
| W2-2 | **BFF Tenant-Scoping → Factory-Instantiation Rule** | Cat 1 ADR-CAND-E-02 | MEDIUM |
| W2-3 | **Public API Type Naming for Result Shapes** | Cat 1 ADR-CAND-E-03 | MEDIUM |
| W2-4 | **Search-Substrate Canonical Architecture** — 4-substrate stack | Cat 3 ADR-CAND-F-1 | HIGH |
| W2-5 | **DI Double-Gate Null-Object Pattern** — peer to ADR-030 single-gate | Cat 3 ADR-CAND-F-2 | HIGH |
| W2-6 | **Search-Substrate Security Model Matrix** — per-substrate filter requirements | Cat 3 ADR-CAND-F-3 | MEDIUM |
| W2-7 | **Endpoint Mapping ↔ DI Registration Symmetry Rule** — formal converse of §F.1 anti-pattern; generalizes Cat 6's W1 ADR-candidate C | Cat 3 ADR-CAND-F-4 | HIGH |
| W2-8 | **Shared Embedding-Cache Helper** — `IEmbeddingCache.GetOrGenerateAsync` extension | Cat 3 ADR-CAND-F-5 (cross-coordinate with Cat 4) | LOW |

**Total ADR candidates (W1 + W2)**: 14 (W1) + 8 (W2) = **22 ADR candidates surfaced** as bullet items for the follow-on ADR phase.

---

## §8 Effort + sequencing roll-up update

Updates to W1 summary §8 effort buckets (additions only):

| Bucket | Effort | Cross-team needs | Recommended timing |
|---|---|---|---|
| **Bundled orphan DELETE PR (W1 lookups + W2 intent cascade)** | ~1.5-2 weeks | Finance Intelligence + AI Chat Playbook Builder team confirmations needed | Immediate after owner sign-off; ungated by other waves |
| **Pattern doc authoring** (Cat 1 + Cat 3 canonical patterns) | ~1 week | None | Phase 3 deliverable |
| **Security adjudication cycle** (3 surfaces: RecordSearch model + SemanticSearch privilege-filter gap + PrivilegeGroupResolver ADR-009) | depends on Security team | YES — multi-team | Owner-driven |
| **Type B `IntentClassificationResult` rename** | XS | Insights team | LOW priority; standalone |
| **`PlaybookDispatcher.cs` XML doc amendment** | XS | SprkChat team | Trivial; bundle with above |

**W2 adds ~1.5-3 weeks to the total downstream migration footprint, mostly the bundled DELETE PR and security adjudication cycle.**

---

## §9 Status + handoff

- **W2 status**: COMPLETE (2/2 sub-agents finished — Cat 1 + Cat 3; both analysis docs persisted; aggregation summary authored)
- **Cat 7 deferred re-dispatch**: NOT TRIGGERED (per Cat 1 §4.3 + Cat 3 §8 — consumer contracts unchanged)
- **W3 precondition (Cat 5 Prompts)**: MET
- **W4 precondition (DI + Configuration)**: MET after W3 completes
- **Next step**: Main session commits + opens PR with auto-merge for `notes/phase2/analysis-intent-classification.md` + `analysis-search.md` + `wave-2-summary.md`. Branch: `work/audit-r1-phase2-wave1` (continuing W2 on the same branch since W1 PR #344 hasn't merged yet).
- **W3 dispatch**: defer until W2 PR merges (clean baseline for Cat 5 sub-agent to read).
- **Owner consultation**: not required mid-wave; Q-002 single end-of-audit review still applies.

---

*W2 summary authored 2026-06-04 by main session from the 2 W2 sub-agent analyses. Sub-agent attribution preserved; recommendations are aggregated, not re-interpreted.*
