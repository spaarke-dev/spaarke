# Current Task — BFF AI Architecture Audit r1

> **Purpose**: Active task state tracker.
> **Status**: **PHASE 2 COMPLETE 2026-06-04**. Phase 3 (Canonical Stack naming + migration plan) is the next step.

---

## 🎯 Active task — Phase 3 (Canonical Stack naming + migration plan)

**PHASE 2 IS COMPLETE.** All 4 waves landed: W1 (Cat 4 Cache + Cat 2 Lookup + Cat 6 Public Contracts + Cat 7 Node executors), W2 (Cat 1 Intent + Cat 3 Search), W3 (Cat 5 Prompts), W4 (DI + Configuration). 8 analysis docs + 4 wave summaries on master. **34 ADR candidates** surfaced. 4 canonical reference impls + 2 new W4 canonical patterns designated.

**Next action**: Owner approves W4 PR merge, then dispatch Phase 3 sub-agent(s) to:
1. Author "Spaarke Canonical AI Stack" naming document — folds 4 reference impls + 2 W4 patterns into a single coherent canonical-architecture artifact
2. Author migration plan with effort estimates per Q-001 + per audit design.md §3.1
3. Bundle inventory corrections (7+ findings across waves)
4. Prepare consolidated open-questions list for Q-002 single end-of-audit owner review

After Phase 3 lands → Phase 4 = owner review.

---

## Status

| Phase | Status |
|---|---|
| Project initialized | ✅ 2026-06-04 |
| Initial findings captured | ✅ [`notes/initial-findings.md`](notes/initial-findings.md) |
| Design discussion | ✅ Q-001 through Q-006 locked (recovery memory) |
| Phase 1 Inventory | ✅ [`notes/inventory.md`](notes/inventory.md) (merged PR #343) |
| Phase 2 Wave 1 — Cat 4 + Cat 2 + Cat 6 + Cat 7 | ✅ (merged PR #344) |
| Phase 2 Wave 2 — Cat 1 + Cat 3 | ✅ (bundled in PR #344) |
| Phase 2 Wave 3 — Cat 5 Prompts | ✅ (merged PR #346) |
| Phase 2 Wave 4 — DI + Configuration | ✅ 2026-06-04 (this commit) |
| **PHASE 2 COMPLETE** | **✅ 2026-06-04** |
| Phase 3 Canonical Stack naming + migration plan | 🔲 next |
| Phase 4 owner review (Q-002 single end-of-audit) | 🔲 |
| Follow-on phase — ADRs (Q-005) + Quarterly Skill (Q-006) | 🔲 DEFERRED |

---

## Phase 2 cumulative headline findings

### HIGH severity
- **LATENT BUG** — `IInsightsAi` 500 instead of 503 under compound-AI-OFF (W1 Cat 6); structural remediation pattern (Option A) defined by W4 §4.5: move `IInsightsAi` + `IPlaybookExecutionEngine` to `AddPublicContractsFacade`; add `NullInsightsAi`; register in `AddNullObjectsForCompoundOff`; fix misleading comment at `AnalysisServicesModule.cs:75-79`; add integration test asserting 503 from 3 Insights endpoints.
- **Endpoint↔DI Registration Conditionality Symmetry Rule** (W4 §4.1) — generalizes Cat 6 §F.1 into a binding architectural constraint; closes the structural root cause of LATENT BUG; recommended as binding ADR.
- **§F.1 detection MUST become runtime-verifiable** (W4 §4.2) — comment-block discipline is inadequate; integration-test fixture probing 4 compound-gate combinations recommended.

### MEDIUM severity
- **Bundled orphan DELETE PR** (~2000 LOC corrected): 3 lookup orphans + IntentClassificationService cascade + ClarificationService + PlaybookBuilderSystemPrompt prune (Option B) + BuildPlanGenerationService NEW orphan. Owner picks Option A vs Option B for PlaybookBuilderSystemPrompt.
- **4 Public Contracts facades missing Null peer** (W1 Cat 6 §F.1 gap): bundled with LATENT BUG fix.
- **Canonical `DistributedCacheExtensions.GetOrCreateAsync<T>` 0% adoption** in `Services/Ai/` (26 sites total per W2 expansions): promote opt-in → MUST.
- **2 security adjudication surfaces** (W2 Cat 3): RecordSearchService tenant-isolation; SemanticSearchService privilege-filter gap.
- **1 security adjudication surface** (W1 Cat 4): PrivilegeGroupResolver ADR-009 compliance.

### LOCKED architectural verdicts (universal across 8 categories)
- **REJECT** forced consolidation behind generic interfaces (Cat 1, Cat 3, Cat 5, W4 modules + options + compound-gate). The "Spaarke Canonical AI Stack" framing MUST be descriptive pattern documentation, NOT binding interfaces.
- **Null-peer asymmetry (1-of-4 search services) is ARCHITECTURALLY CORRECT** (W2 Cat 3) — endpoint-mapping conditionality is the rule.
- **Compound AI gate is the right structural choice** (W4 §4.3) — DO NOT distribute per-service.

### Canonical reference impls designated
- `EmbeddingCache` (binary specialist) + `DistributedCacheExtensions.GetOrCreateAsync<T>` (generic helper) — W1 Cat 4
- `InsightsIntentClassifier` — W2 Cat 1
- `RagService`/`NullRagService` (gold-standard for ADR-032 double-gate) — W2 Cat 3
- `OrchestratorPromptBuilder` (gold-standard for ADR-009 exception) + `CapabilityClassificationPromptBuilder` — W3 Cat 5

### Inventory corrections (7+ items)
- Module count 34 → 31 (W4)
- `Sprk.Bff.Api.Options` namespace does NOT exist (W4)
- `Configuration/` per-dir count 25 → 21 (W4)
- 5 explicit prompt sources (not 3): adds AnalysisContextBuilder + FallbackPrompts (W3)
- 18 node executors (not 16); ActionType labels (W1 Cat 7)
- 3 IntentClassification-named types (not 2); classifier consumer counts (W2 Cat 1)
- DocumentClassifierHandler is IRagService consumer (not ISemanticSearchService) (W2 Cat 3)
- AiAnalysisNodeExecutor is IRecordSearchService consumer (W2 Cat 3 + W1 Cat 7)
- PromptLibrary mis-framed: user-CRUD layer (W3 Cat 5)
- NEW 5th orphan: BuildPlanGenerationService.cs (W3 Cat 5)

### Cross-sub-agent validation worked
- W2 Cat 1 corrected 2 W1 Sub-Agent A false positives
- W3 Cat 5 corrected 3 W2 Cat 1 errors (cascade DELETE was wrong by 10×)
- W4 corrected 3 inventory errors

**Total ADR candidates surfaced**: **34** (14 W1 + 8 W2 + 5 W3 + 7 W4), deferred to follow-on phase per Q-005.

---

## Downstream projects waiting on this audit

| Project | What's blocked | What's NOT blocked |
|---|---|---|
| `ai-spaarke-insights-engine-r3` | Wave 2 (Tier 2.5 reconciliation scope) — UNBLOCKED after Phase 3 lands | Wave 1 cleanup (Tier 1 + Tier 1.5 index rename) safe to proceed |
| `spaarke-ai-platform-unification-r5` | Nothing explicitly | Heads-up to audit own chat-agent layer for similar parallel-build risk |

---

*Phase 2 complete 2026-06-04. Updated by main session after final W4 sub-agent analysis + Phase 2 aggregation.*
