# Current Task — BFF AI Architecture Audit r1

> **Purpose**: Active task state tracker.
> **Status**: Phase 2 Wave 2 complete 2026-06-04; W3 (Cat 5 Prompts) dispatch is the next step.

---

## 🎯 Active task — Phase 2 W3 dispatch (next)

Phase 2 Wave 2 completed 2026-06-04. 2 sequential sub-agents (Cat 1 Intent classification, Cat 3 Search services) produced 2 analysis docs + 1 aggregation summary in [`notes/phase2/`](notes/phase2/). All findings pinned to inventory commit `357e6936`; HEAD `d862bec6` verified ZERO code drift across both sub-agents.

**Next action**: Owner approves W2 PR merge, then dispatch W3 sub-agent per [`notes/phase2/wave-2-summary.md`](notes/phase2/wave-2-summary.md) §6 recommendations:
1. **Cat 5 — Prompts** (depends on Cat 1 + Cat 3 outcomes — both complete; precondition MET)
2. **Cat 7 deferred re-dispatch**: NOT TRIGGERED (per Cat 1 §4.3 + Cat 3 §8 — consumer contracts unchanged by W2 verdicts)

After W3 (Cat 5 Prompts) lands, W4 (DI + Configuration) is the final wave per Q-001 scope.

---

## Status

| Phase | Status |
|---|---|
| Project initialized | ✅ 2026-06-04 |
| Initial findings captured | ✅ [`notes/initial-findings.md`](notes/initial-findings.md) |
| Design discussion | ✅ Q-001 through Q-006 locked (recovery memory; design.md skeleton TBD codification) |
| Phase 1 Inventory | ✅ [`notes/inventory.md`](notes/inventory.md) (PR #343 in flight) |
| Phase 2 Wave 1 — Cat 4 Cache + Cat 2 Lookup + Cat 6 Public Contracts + Cat 7 Node executors | ✅ [`notes/phase2/`](notes/phase2/) (PR #344 in flight) |
| Phase 2 Wave 2 — Cat 1 Intent + Cat 3 Search | ✅ 2026-06-04 (this commit) |
| Phase 2 Wave 3 — Cat 5 Prompts | 🔲 next (precondition MET) |
| Phase 2 Wave 4 — DI + Configuration | 🔲 (after W3 — last per Q-001 scope) |
| Phase 3 Canonical Stack naming + migration plan | 🔲 |
| Phase 4 owner review (Q-002 single end-of-audit) | 🔲 |
| Follow-on phase — ADRs (Q-005) + Quarterly Skill (Q-006) | 🔲 DEFERRED |

---

## W1 + W2 headline findings (per `wave-1-summary.md` + `wave-2-summary.md`)

| Finding | Severity | Action item |
|---|---|---|
| **LATENT BUG** — `IInsightsAi` 500 instead of 503 under compound-AI-OFF (W1 Cat 6); verified at upstream `InsightsOrchestrator` ctor by W2 Cat 1 + Cat 3 | HIGH | Add `NullInsightsAi` + DI rewire + integration test |
| **3 lookup-service orphans** confirmed DELETE-ready (W1 Cat 2) | MEDIUM | Single PR cleanup; ~714 LOC |
| **3-file intent classifier cascade DELETE** confirmed (W2 Cat 1 + main session grep) | MEDIUM | Bundle with above for ~1700-1800 LOC DELETE PR |
| **4 Public Contracts facades missing Null peer** (W1 Cat 6 §F.1 gap) | MEDIUM | Add 4 Null peers + ADR-032-compliant consumer cleanup |
| Canonical `DistributedCacheExtensions.GetOrCreateAsync<T>` exists, 0% adoption in `Services/Ai/` (W1 Cat 4); expanded by W2 Cat 1 + Cat 3 cache levers (26 total sites) | MEDIUM | Promote opt-in → MUST for new sites; phased existing-site migration |
| **Generic `IIntentClassifier<TResult>` consolidation REJECTED** (W2 Cat 1) | LOCKED | Pattern-doc canonical naming instead of interface |
| **PlaybookEmbedding ↔ SemanticSearch consolidation REJECTED** (W2 Cat 3) | LOCKED | Pattern-doc canonical naming instead of interface |
| **Null-peer asymmetry (1-of-4 search services) is ARCHITECTURALLY CORRECT** (W2 Cat 3 §2.4) | LOCKED | Endpoint mapping conditionality drives Null-peer requirements; do NOT add Null peers to other 3 |
| **2 NEW security adjudication surfaces** (W2 Cat 3) | MEDIUM | RecordSearchService tenant-isolation model; SemanticSearchService privilege-filter gap |
| `RagService`/`NullRagService` = canonical reference for ADR-032 **double-gate sub-mechanism** (W2 Cat 3 §4.2) | DOCUMENT | Currently undocumented; ADR candidate |
| `InsightsIntentClassifier` = canonical reference impl for intent classifier pattern (W2 Cat 1) | DOCUMENT | ADR candidate |
| 2 W1 false positives corrected by W2 Cat 1 (`NullInsightsIntentClassifier` cache-dep; `AiPlaybookBuilderService` at-risk) | LOW | Annotate W1 summary |
| Inventory §2.7 has 3 labeling errors (executor count 16→18) | LOW | Annotate inventory or accept as snapshot artifact |
| `AnalysisServicesModule.cs:75-79` factual error in comment | LOW | 1-line PR fix |
| NO `ACTION-TYPE-REGISTRY.md` allocation doc | LOW | Author small doc; preempts parallel-team collision risk |
| `PrivilegeGroupResolver` ADR-009 compliance ambiguity | depends | Security team adjudication required |
| `PlaybookDispatcher.cs` XML doc leads with wrong rationale (ADR-010 instead of tenant-scoping) | LOW | Amend XML doc |
| Type B `IntentClassificationResult` rename | LOW | `InsightsRoutingDecision` candidate |

**Total ADR candidates surfaced** (W1 + W2): 22 (deferred to follow-on phase per Q-005).

---

## Downstream projects waiting on this audit

| Project | What's blocked | What's NOT blocked |
|---|---|---|
| `ai-spaarke-insights-engine-r3` | Wave 2 (Tier 2.5 reconciliation scope) | Wave 1 cleanup (Tier 1 + Tier 1.5 index rename) safe to proceed |
| `spaarke-ai-platform-unification-r5` | Nothing explicitly | Heads-up to audit own chat-agent layer for similar parallel-build risk |

---

*Phase 2 W2 complete 2026-06-04. Updated by main session after aggregation of Cat 1 + Cat 3 sub-agent analyses.*
