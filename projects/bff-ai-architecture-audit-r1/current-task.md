# Current Task — BFF AI Architecture Audit r1

> **Purpose**: Active task state tracker.
> **Status**: Phase 2 Wave 1 complete 2026-06-04; W2 dispatch is the next step.

---

## 🎯 Active task — Phase 2 W2 dispatch (next)

Phase 2 Wave 1 completed 2026-06-04. 4 parallel sub-agents (Cat 4 Cache, Cat 2 Lookup, Cat 6 Public Contracts, Cat 7 Node executors) produced 4 analysis docs + 1 aggregation summary in [`notes/phase2/`](notes/phase2/). All findings pinned to inventory commit `357e6936`; HEAD `12275b10` verified ZERO code drift.

**Next action**: Owner approves W1 PR merge, then dispatch W2 sequential sub-agents per [`notes/phase2/wave-1-summary.md`](notes/phase2/wave-1-summary.md) §6 recommendations:
1. **Cat 1 — Intent classification** (4 parallel classifiers; HIGHEST architectural significance)
2. **Cat 3 — Search services** (after Cat 1 lands; includes integration test for IInsightsAi compound-OFF latent bug per Cat 6 §4.1)
3. **Cat 7 deferred questions** (`AiAnalysisNodeExecutor` + `IndexRetrieveNode` classifier/search migrations post-Cat 1+3)

---

## Status

| Phase | Status |
|---|---|
| Project initialized | ✅ 2026-06-04 |
| Initial findings captured | ✅ [`notes/initial-findings.md`](notes/initial-findings.md) |
| Design discussion | ✅ Q-001 through Q-006 locked (in recovery memory; design.md skeleton TBD codification) |
| Phase 1 Inventory | ✅ [`notes/inventory.md`](notes/inventory.md) (542 lines, 357 services, 11 categories — PR #343 in flight) |
| Phase 2 Wave 1 — Cat 4 Cache + Cat 2 Lookup + Cat 6 Public Contracts + Cat 7 Node executors | ✅ [`notes/phase2/`](notes/phase2/) (4 analysis docs + summary; PR pending) |
| Phase 2 Wave 2 — Cat 1 Intent + Cat 3 Search + Cat 7 deferred | 🔲 next |
| Phase 2 Wave 3 — Cat 5 Prompts | 🔲 (depends on Cat 1) |
| Phase 2 Wave 4 — DI + Configuration | 🔲 (last per Q-001 scope) |
| Phase 3 Canonical Stack naming + migration plan | 🔲 |
| Phase 4 owner review (Q-002 single end-of-audit review) | 🔲 |
| Follow-on phase — ADRs (Q-005) + Quarterly Skill (Q-006) | 🔲 DEFERRED |

---

## Wave 1 headline findings (per `wave-1-summary.md`)

| Finding | Severity | Action item |
|---|---|---|
| LATENT BUG — `IInsightsAi` 500 instead of 503 under compound-AI-OFF | HIGH | Add `NullInsightsAi` + DI rewire; verify via integration test in W2 Cat 1 |
| 3 lookup-service orphans confirmed DELETE-ready | MEDIUM | Single PR cleanup; ~6-12 KB compressed savings |
| 4 Public Contracts facades missing Null peer (§F.1 gap) | MEDIUM | Add `NullInvoiceAi`, `NullWorkspacePrefillAi`, `NullRecordMatchingAi`, `NullInsightsAi` |
| `DistributedCacheExtensions.GetOrCreateAsync<T>` exists but 0% adoption in `Services/Ai/` | MEDIUM | Promote opt-in → MUST for new sites; phased existing-site migration |
| Inventory §2.7 has 3 labeling errors (executor count 16→18) | LOW | Annotate inventory or accept as snapshot artifact |
| `AnalysisServicesModule.cs:75-79` factual error in comment | LOW | 1-line PR fix |
| NO `ACTION-TYPE-REGISTRY.md` allocation doc | LOW | Author small doc; preempts parallel-team collision risk |
| `PrivilegeGroupResolver` ADR-009 compliance ambiguity | depends | Security team adjudication required |

---

## Downstream projects waiting on this audit

| Project | What's blocked | What's NOT blocked |
|---|---|---|
| `ai-spaarke-insights-engine-r3` | Wave 2 (Tier 2.5 reconciliation scope) | Wave 1 cleanup (Tier 1 + Tier 1.5 index rename) safe to proceed |
| `spaarke-ai-platform-unification-r5` | Nothing explicitly | Heads-up to audit own chat-agent layer for similar parallel-build risk |

---

*Phase 2 W1 complete 2026-06-04. Updated by main session after aggregation of 4 parallel sub-agent analyses.*
