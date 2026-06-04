# Current Task — BFF AI Architecture Audit r1

> **Purpose**: Active task state tracker.
> **Status**: Phase 2 Wave 3 complete 2026-06-04; W4 (DI + Configuration) is the FINAL Phase 2 wave.

---

## 🎯 Active task — Phase 2 W4 dispatch (final wave)

Phase 2 Wave 3 completed 2026-06-04. Cat 5 (Prompts) sub-agent produced 1 analysis doc + 1 aggregation summary in [`notes/phase2/`](notes/phase2/). All findings pinned to inventory commit `357e6936`; HEAD `3abbe918` verified ZERO code drift.

**Critical W3 correction**: Cat 5 surfaced that W2 Cat 1's `PlaybookBuilderSystemPrompt.cs` cascade DELETE estimate was wrong by 10× (~100 → ~1280 LOC) and missed a 5th orphan (`BuildPlanGenerationService.cs`, ~530 LOC). Whole-file DELETE is impossible — `BuilderAgentService.Build()` consumes a live method. Recommended **Option B (EXTRACT-THEN-DELETE)**: extract live `Build()` method to new file, delete the rest.

**Next action**: Owner approves W3 PR merge, then dispatch W4 sub-agent per [`notes/phase2/wave-3-summary.md`](notes/phase2/wave-3-summary.md) §6 recommendations:
1. **Cat 4 — DI + Configuration patterns** (FINAL wave; touches everything)
   - 34 DI modules; consolidate or per-concern split?
   - `Configuration/` vs `Options/` namespace split
   - 35 options classes; "feature manifest" collapse?
   - Compound AI gate complexity
   - §F.1 anti-pattern + endpoint-mapping ↔ DI registration symmetry rule (Cat 6 + Cat 3 inputs)
   - Orphan DI line removals (Cat 1 + Cat 5 inputs)
   - `INodeExecutor` registry + ActionType central enum (Cat 7 input)

After W4 (DI + Configuration) lands, Phase 2 is COMPLETE. Phase 3 = Canonical Stack naming + migration plan + Phase 4 = owner review.

---

## Status

| Phase | Status |
|---|---|
| Project initialized | ✅ 2026-06-04 |
| Initial findings captured | ✅ [`notes/initial-findings.md`](notes/initial-findings.md) |
| Design discussion | ✅ Q-001 through Q-006 locked (recovery memory) |
| Phase 1 Inventory | ✅ [`notes/inventory.md`](notes/inventory.md) (merged PR #343) |
| Phase 2 Wave 1 — Cat 4 Cache + Cat 2 Lookup + Cat 6 Public Contracts + Cat 7 Node executors | ✅ (merged PR #344) |
| Phase 2 Wave 2 — Cat 1 Intent + Cat 3 Search | ✅ (bundled with W1 in PR #344) |
| Phase 2 Wave 3 — Cat 5 Prompts | ✅ 2026-06-04 (this commit) |
| Phase 2 Wave 4 — DI + Configuration | 🔲 next (precondition MET; FINAL Phase 2 wave) |
| Phase 3 Canonical Stack naming + migration plan | 🔲 |
| Phase 4 owner review (Q-002 single end-of-audit) | 🔲 |
| Follow-on phase — ADRs (Q-005) + Quarterly Skill (Q-006) | 🔲 DEFERRED |

---

## W1 + W2 + W3 headline findings (per all 3 summaries)

| Finding | Severity | Action item |
|---|---|---|
| **LATENT BUG** — `IInsightsAi` 500 instead of 503 under compound-AI-OFF (W1 Cat 6); confirmed by W2 Cat 1 + Cat 3 + W3 Cat 5 to be at upstream `InsightsOrchestrator` ctor layer | HIGH | Add `NullInsightsAi` + DI rewire + integration test |
| **Bundled orphan DELETE PR** (~2000 LOC corrected): 3 lookup orphans (W1 Cat 2) + IntentClassificationService cascade (W2 Cat 1) + ClarificationService + PlaybookBuilderSystemPrompt prune (W3 Cat 5 Option B) + BuildPlanGenerationService NEW orphan (W3 Cat 5) | MEDIUM | Single PR; owner picks Option A or B for PlaybookBuilderSystemPrompt |
| **4 Public Contracts facades missing Null peer** (W1 Cat 6 §F.1 gap) | MEDIUM | Add 4 Null peers + ADR-032-compliant consumer cleanup |
| Canonical `DistributedCacheExtensions.GetOrCreateAsync<T>` exists, 0% adoption (W1 Cat 4 + 26 sites total per W2 expansions) | MEDIUM | Promote opt-in → MUST for new sites; phased migration |
| **REJECT generic consolidation** for intent classifiers (W2 Cat 1), search substrates (W2 Cat 3), prompt builders (W3 Cat 5) | LOCKED | Pattern-doc canonical naming instead of interfaces |
| **Null-peer asymmetry (1-of-4 search services) is ARCHITECTURALLY CORRECT** (W2 Cat 3) | LOCKED | Do NOT add Null peers to other 3; endpoint-mapping conditionality is the rule |
| **2 security adjudication surfaces** (W2 Cat 3) + 1 from W1 Cat 4 (`PrivilegeGroupResolver`) | MEDIUM | RecordSearchService tenant-isolation; SemanticSearchService privilege-filter gap; PrivilegeGroupResolver ADR-009 |
| **NEW 5th orphan**: `BuildPlanGenerationService.cs` (W3 Cat 5; ~530 LOC) | MEDIUM | AI Chat Playbook Builder team confirmation |
| **W2 Cat 1's cascade DELETE estimate was wrong by 10×** — Cat 5 found `BuilderAgentService.Build()` is live consumer | HIGH | Adjust bundled DELETE PR to Option B (EXTRACT-THEN-DELETE) |
| `RagService`/`NullRagService` = canonical reference for ADR-032 **double-gate sub-mechanism** (W2 Cat 3) | DOCUMENT | Currently undocumented; ADR candidate |
| `OrchestratorPromptBuilder` = gold-standard ADR-009 exception XML doc convention (W1 Cat 4 + W3 Cat 5) | DOCUMENT | ADR candidate |
| 4 canonical reference impls designated (Cat 1: InsightsIntentClassifier; Cat 3: RagService; Cat 5: OrchestratorPromptBuilder + CapabilityClassificationPromptBuilder) | DOCUMENT | Phase 3 deliverable |
| 5 W1+W2 false positives corrected by later sub-agents (cross-validation works) | LOW | Annotate prior summaries |
| `PromptLibrary` mis-framed in inventory §2.5 (user-CRUD layer, not LLM-call-site) | LOW | Inventory correction |
| Multiple inventory corrections (executor count 16→18; classifier consumer counts; 3 IntentClassification types not 2; AnalysisContextBuilder + FallbackPrompts missed) | LOW | Annotate inventory or accept as snapshot artifact |
| `AnalysisServicesModule.cs:75-79` factual error in comment | LOW | 1-line PR fix |
| NO `ACTION-TYPE-REGISTRY.md` allocation doc (W1 Cat 7) | LOW | Author small doc |
| `PrivilegeGroupResolver` ADR-009 compliance ambiguity | depends | Security team adjudication |
| `PlaybookDispatcher.cs` XML doc misleading rationale | LOW | Amend XML doc |
| Type B `IntentClassificationResult` rename | LOW | `InsightsRoutingDecision` candidate |

**Total ADR candidates surfaced** (W1+W2+W3): **27** (14 W1 + 8 W2 + 5 W3), deferred to follow-on phase per Q-005.

---

## Downstream projects waiting on this audit

| Project | What's blocked | What's NOT blocked |
|---|---|---|
| `ai-spaarke-insights-engine-r3` | Wave 2 (Tier 2.5 reconciliation scope) | Wave 1 cleanup (Tier 1 + Tier 1.5 index rename) safe to proceed |
| `spaarke-ai-platform-unification-r5` | Nothing explicitly | Heads-up to audit own chat-agent layer for similar parallel-build risk |

---

*Phase 2 W3 complete 2026-06-04. Updated by main session after Cat 5 sub-agent analysis.*
