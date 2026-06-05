# DR-005 — Intent Classifier (Category 1)

> **Author**: Phase 3 Sub-Agent K (synthesis from Phase 2 outputs)
> **Date**: 2026-06-04
> **Status**: PROPOSED (pending Q-002 owner review)
> **Pinned to**: Phase 1 inventory commit `357e6936`
> **Source analysis**: [`notes/phase2/analysis-intent-classification.md`](../notes/phase2/analysis-intent-classification.md)
> **Canonical authority**: [`notes/canonical-architecture-decisions.md` §2.5](../notes/canonical-architecture-decisions.md) · §3 (W2 Cat 1 row) · §8.1 (W2-1) · §8.2 (W2-2, W2-3)

## Context

Phase 1 inventory §2.1 surfaced an ambiguous landscape of "intent classification" services across `Services/Ai/` — claiming 2 `IntentClassificationResult` types and multiple classifier-like services with overlapping responsibilities. The inventory questioned whether a generic `IIntentClassifier<TResult>` abstraction should consolidate them.

W2 Sub-Agent E applied empirical reproduction and corrected the inventory substantially:
- **3 related `IntentClassification*` types** (not 2): Type A `Services.Ai.IntentClassificationResult` (DELETE — orphan), Type B `Services.Ai.Insights.Routing.IntentClassificationResult` (KEEP — rename LOW priority), Type C `Services.Ai.IntentClassification` (independent type inside `AiPlaybookBuilderService`; KEEP — own classifier, 4 production consumers).
- **`InsightsIntentClassifier` is the canonical reference implementation** — Singleton; `IOptions<T>` binding from feature-specific config section; ADR-032 §F.1 dual registration with `NullInsightsIntentClassifier` peer; JSON-schema-constrained LLM decoding; SHA-256 cache key construction; OTEL-instrumented latency tracking (FR-05-style 500ms target); domain-specific result type.
- **`CapabilityRouter`** (3-tier: keyword → LLM → fallback; AIPU2-012) corrected from "4 production" to **2 behavioral + 2 sentinel/telemetry** consumers.
- **`PlaybookDispatcher`** (2-stage: vector + LLM; factory-instantiated for per-request tenantId) corrected from "4 production via factory" to **3 behavioral + 1 sentinel**.
- W2 Cat 1 initially claimed `AiPlaybookBuilderService` was at-risk — `REJECTED` on cross-validation (the service is independent with 4 live production consumers).

Critical W3 cross-correction: W2 Cat 1's initial estimate of the orphan-cascade DELETE scope (~100 LOC) was **wrong by 13×**. W3 Cat 5 audit of the prompt layer found the cascade reaches into `PlaybookBuilderSystemPrompt.cs` (80% dead members) + identified a NEW 5th orphan `BuildPlanGenerationService.cs` (~530 LOC) missed by inventory + W2 Cat 1. The corrected DELETE-cascade scope is **~1280 LOC**:
- `IntentClassificationService.cs` (408 LOC) — the orphan classifier
- `BuildPlanGenerationService.cs` (~530 LOC) — NEW 5th orphan (W3 cross-find)
- `PlaybookBuilderSystemPrompt.cs` dead members (~340 LOC after Option B extraction in DR-007)
- `ClarificationService` (orphan)

The whole-file DELETE was impossible because `PlaybookBuilderSystemPrompt.cs` contained a small live tail consumed by `BuilderAgentService` — necessitating Option B EXTRACT-THEN-DELETE pattern (see DR-007).

## Decision

1. **KEEP 3 classifiers + 1 type**:
   - `InsightsIntentClassifier` (canonical reference impl) — Singleton + ADR-032 §F.1 dual registration with `NullInsightsIntentClassifier` peer.
   - `CapabilityRouter` (3-tier: keyword → LLM → fallback) — AIPU2-012; 2 behavioral consumers + 2 sentinel/telemetry.
   - `PlaybookDispatcher` (2-stage: vector + LLM) — factory-instantiated for per-request tenantId; 3 behavioral + 1 sentinel.
   - `IntentClassification` Type C inside `AiPlaybookBuilderService` (independent service with 4 production consumers).

2. **DELETE 4-file orphan cluster (~1280 LOC corrected scope per W3 cross-correction)**, bundled with DR-001 + DR-007 in the single ~2000-LOC DELETE PR:
   - `IntentClassificationService.cs` (408 LOC orphan)
   - `BuildPlanGenerationService.cs` (~530 LOC NEW 5th orphan)
   - `PlaybookBuilderSystemPrompt.cs` 80%-dead members (~340 LOC after Option B EXTRACT-THEN-DELETE — see DR-007)
   - `ClarificationService` orphan
   - Type A `Services.Ai.IntentClassificationResult` (the orphan result type — DELETE alongside its sole producer)

3. **REJECT generic `IIntentClassifier<TResult>` abstraction**:
   - Output shapes are domain-specific (per-classifier strongly-typed records); abstraction would lose type safety.
   - Multi-stage cascade designs differ structurally (3-tier vs 2-stage vs inline single-call); cannot be unified behind a single interface without leaky abstraction.
   - DI lifetimes differ (Singleton `InsightsIntentClassifier` vs factory-instantiated `PlaybookDispatcher` vs Scoped `AiPlaybookBuilderService` internal classifier).
   - In-process `MemoryCache` use varies per-classifier; cache key construction is domain-specific.

4. **DESIGNATE `InsightsIntentClassifier` as canonical reference impl** for "Spaarke Canonical Intent Classifier Pattern" (Q-004 surfaced). Pattern is **descriptive pattern documentation**, NOT binding interface abstraction (per canonical-architecture-decisions §5.1 universal verdict).

5. **DEFER Type B `IntentClassificationResult` rename** to LOW priority. Candidate names: `InsightsRoutingDecision` or namespace disambiguation. Owner adjudication.

6. **DOCUMENT the W3 cascade-scope correction** (`~100` → `~1280` LOC) prominently in the bundled DELETE PR description — illustrates Empirical-Reproduction-FIRST methodology paying dividends.

## Consequences

### Positive
- ~1280 LOC dead code removed (largest single DELETE bundle in the audit).
- 3 KEEP classifiers retain their domain-appropriate independence — no forced abstraction loss.
- `InsightsIntentClassifier` becomes the documented reference impl for the 7-element canonical pattern.
- W3 cross-correction surfaces a 5th orphan (`BuildPlanGenerationService.cs`) that would have shipped as legacy debt indefinitely.
- Endpoint-side defensive `?=null` patterns eliminated alongside classifier DELETE (consumers were the orphan paths).

### Negative
- AI Chat Playbook Builder team must confirm DELETE list (the orphan owner team) — coordination per Q-003.
- Cascade scope correction risk: if the W3 cross-correction missed additional sites, the DELETE PR may fail to compile. Mitigation: Option B EXTRACT pattern (DR-007) is the pre-condition; CI build verification is the gate.

### Migration impact
- **Cross-team coordination**: AI Chat Playbook Builder team (orphan-cluster owner — confirms DELETE list); Insights team (canonical `InsightsIntentClassifier` confirmation).
- **Effort estimate**: **M (Medium)** — bundled DELETE PR (~1280 LOC of this DR + ~714 LOC from DR-001 + Option B EXTRACT-then-DELETE from DR-007 = ~2000 LOC total bundled).
- **Sequencing**: HIGH-priority bundled DELETE PR. Pre-requisite: Option B EXTRACT-THEN-DELETE (DR-007) must complete first for `PlaybookBuilderSystemPrompt.cs` cascade to be safe.

## Canonical naming (Q-004 — surfaced not locked)

- **Candidate**: "Spaarke Canonical Intent Classifier Pattern" (pattern-doc, NOT interface)
- **Reference impl**: `InsightsIntentClassifier` (`src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Routing/InsightsIntentClassifier.cs`)
- **Pattern elements** (7):
  1. Singleton DI lifetime (stateless, threadsafe)
  2. `IOptions<T>` binding from feature-specific config section
  3. ADR-032 §F.1 dual registration with `NullInsightsIntentClassifier` peer (always-registered)
  4. JSON-schema-constrained LLM decoding (deterministic output shape)
  5. SHA-256 cache key construction over normalized query (in-process `MemoryCache` with documented ADR-009 exception — see DR-002)
  6. OTEL-instrumented latency tracking with FR-05-style budget (500ms target)
  7. Domain-specific result type (NOT generic `IClassifier<T>`)
- **KEEP siblings (3 + 1 type)**:
  - `CapabilityRouter` (3-tier)
  - `PlaybookDispatcher` (2-stage; factory-instantiated)
  - `IntentClassification` Type C in `AiPlaybookBuilderService`
  - Type B `IntentClassificationResult` (rename candidate; LOW priority)

## ADR candidates from this decision (Q-005 — bullets only)

- **W2-1** Spaarke Canonical Intent Classifier Pattern — HIGH priority (7-element pattern doc, NOT binding interface)
- **W2-2** BFF Tenant-Scoping → Factory-Instantiation Rule — MEDIUM priority (rationale for `PlaybookDispatcher` factory pattern)
- **W2-3** Public API Type Naming for Result Shapes — MEDIUM priority (Type B rename guidance)

## Open questions for owner review (Q-002)

1. **Confirm DELETE list scope** (canonical §11.1 Q-2 + Q-4): AI Chat Playbook Builder owner confirms ~1280 LOC cascade (4-file orphan cluster + Type A result type). Particularly: the 5th orphan `BuildPlanGenerationService.cs` (~530 LOC NEW from W3 cross-correction) — owner aware this is dead code?
2. **REJECT generic interface confirmation** (canonical §11.2 Q-5): Owner accepts pattern-doc canonicalization over interface abstraction?
3. **`InsightsIntentClassifier` canonical lock** (canonical §11.3 Q-12): Owner locks as canonical Intent Classifier Pattern reference impl?
4. **Type B rename adjudication** (canonical §11.7 Q-27): Rename to `InsightsRoutingDecision`, namespace disambiguation, or defer?
5. **`InsightsIntentClassifier.BuildPrompt()` extraction trigger** (canonical §11.7 Q-28): Phase 2 multi-playbook timing — Insights team owns; when to extract the inline prompt?

## References

- Source analysis: [`notes/phase2/analysis-intent-classification.md`](../notes/phase2/analysis-intent-classification.md)
- Wave summaries: [`notes/phase2/wave-2-summary.md`](../notes/phase2/wave-2-summary.md) §1.1, [`notes/phase2/wave-3-summary.md`](../notes/phase2/wave-3-summary.md) §2.2 (W3 cross-correction of W2 cascade scope)
- Canonical authority: [`notes/canonical-architecture-decisions.md`](../notes/canonical-architecture-decisions.md) §2.5 + §3 + §5.3 (cross-sub-agent validation) + §6 (inventory corrections rows 6, 12, 13, 14) + §11.1 Q-2/Q-4 + §11.2 Q-5 + §11.3 Q-12 + §11.7 Q-27/Q-28
- Related ADR candidates: W2-1 (HIGH), W2-2/W2-3 (MEDIUM)
- Related DRs: **DR-001** (cascade DELETE bundling), **DR-007** (Option B EXTRACT-THEN-DELETE pre-condition + `PlaybookBuilderSystemPrompt` cascade overlap), **DR-002** (`InsightsIntentClassifier` uses in-process `MemoryCache` with ADR-009 exception XML doc)
- ADR cross-references: ADR-009 (cache discipline), ADR-010 (interface budget cap), ADR-013 (facade-over-internal-SDK), ADR-032 §F.1 (Null-Object dual registration)
- Inventory corrections from this category: §6 rows 6 (3 types not 2), 12 (CapabilityRouter consumer count), 13 (PlaybookDispatcher consumer count), 14 (5th orphan)
