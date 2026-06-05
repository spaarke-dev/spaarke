# Current Task — BFF AI Architecture Audit r1

> **Purpose**: Active task state tracker.
> **Status**: **PHASE 3 COMPLETE 2026-06-04**. Phase 4 (single end-of-audit owner review per Q-002) is the next step.

---

## 🎯 Active task — Phase 4: Q-002 single end-of-audit owner review

**PHASE 3 IS COMPLETE.** Three sequential/parallel sub-agents (I → J + K) produced 12 synthesis files totaling ~2814 lines:
- 1 primary canonical-architecture decision doc (Sub-Agent I)
- 1 migration plan + 1 r3 scope recommendations doc (Sub-Agent J)
- 8 per-category DR-### records + 1 README (Sub-Agent K)

**Next action**: Owner reads Phase 3 deliverables and makes binding decisions per Q-002 lock. Recommended owner reading order (per Sub-Agent K §8):

1. **`notes/canonical-architecture-decisions.md` §1 Executive Summary** — locks the framing (~50 lines)
2. **`decisions/README.md`** — 41-line index to navigate the 8 DRs
3. **`DR-003` (Public Contracts Facade)** FIRST — highest-priority single finding (LATENT BUG); owner adjudication of Option A remediation is the highest-leverage decision
4. **`DR-008` (DI + Configuration)** — codifies the Endpoint↔DI Symmetry Rule (load-bearing NEW pattern); pairs with DR-003 as the structural-rule + canonical-instance dyad
5. **`DR-005` + `DR-007` + `DR-001`** — bundled-DELETE-PR triad (~2000 LOC cascade); review together
6. **`DR-002` (Cache)** + **`DR-006` (Search)** — pattern-canonicalization decisions with phased adoption
7. **`DR-004` (Node Executors)** — light-touch KEEP verdict with documentation deliverable
8. **`notes/migration-plan.md`** — 8 PRs sequenced; LATENT BUG fix is PR #1
9. **`notes/r3-scope-recommendations.md`** — r3 Wave 2 timeline reduced ~50-66% (~2-3 weeks → ~1 week)

**Owner decisions to make** (consolidated from 31 questions in canonical §11 + 26 questions across 8 DRs):
- Confirm REJECT-consolidation verdicts across 8 categories (LOCKED if no override)
- Confirm DELETE verdicts (3 lookup orphans + intent cascade + 5th orphan)
- **Pick Option A or Option B** for `PlaybookBuilderSystemPrompt.cs` (Option B = EXTRACT-THEN-DELETE recommended)
- **Approve LATENT BUG Option A remediation** (move IInsightsAi + add NullInsightsAi) as Migration PR #1
- **Lock canonical naming** (Q-004): "Spaarke Public-Contracts Facade DI Fascia" + "Spaarke Endpoint↔DI Symmetry Rule" recommended for owner lock
- Approve 8 PR sequencing in migration-plan.md
- Approve r3 Wave 2 scope reduction (~1 week reconciliation, not ~2-3 weeks rebuild)
- Adjudicate 3 security surfaces (RecordSearchService, SemanticSearchService, PrivilegeGroupResolver) — route to Security team
- Confirm Q-005 ADR-deferral (34 ADR candidates → follow-on phase ~2-3 weeks)
- Confirm Q-006 Quarterly Skill deferral

After Phase 4 owner review → follow-on phase begins (ADR authoring + Quarterly Skill institutionalization per Q-005/Q-006 DEFERRED).

---

## Status

| Phase | Status |
|---|---|
| Project initialized | ✅ 2026-06-04 |
| Initial findings captured | ✅ [`notes/initial-findings.md`](notes/initial-findings.md) |
| Design discussion | ✅ Q-001 through Q-006 locked |
| Phase 1 Inventory | ✅ [`notes/inventory.md`](notes/inventory.md) (merged PR #343) |
| Phase 2 Wave 1+2 (Cat 2+4+6+7 + Cat 1+3) | ✅ (merged PR #344) |
| Phase 2 Wave 3 (Cat 5 Prompts) | ✅ (merged PR #346) |
| Phase 2 Wave 4 (DI + Configuration) | ✅ (merged PR #347) |
| **PHASE 2 COMPLETE** | ✅ 2026-06-04 |
| Phase 3 — Canonical Stack synthesis (Sub-Agent I) | ✅ 2026-06-04 ([`notes/canonical-architecture-decisions.md`](notes/canonical-architecture-decisions.md), 815 lines) |
| Phase 3 — Migration plan + r3 recommendations (Sub-Agent J) | ✅ 2026-06-04 ([`notes/migration-plan.md`](notes/migration-plan.md), 730 lines + [`notes/r3-scope-recommendations.md`](notes/r3-scope-recommendations.md), 325 lines) |
| Phase 3 — 8 DR-### records + README (Sub-Agent K) | ✅ 2026-06-04 ([`decisions/`](decisions/), 949 lines) |
| **PHASE 3 COMPLETE** | ✅ 2026-06-04 (this commit) |
| Phase 4 — Q-002 single end-of-audit owner review | 🔲 next |
| Follow-on phase — ADRs (Q-005) + Quarterly Skill (Q-006) | 🔲 DEFERRED |

---

## Phase 3 deliverables summary

| File | Lines | Size | Author |
|---|---|---|---|
| `notes/canonical-architecture-decisions.md` | 815 | 65 KB | Sub-Agent I |
| `notes/migration-plan.md` | 730 | 47 KB | Sub-Agent J |
| `notes/r3-scope-recommendations.md` | 325 | 27 KB | Sub-Agent J |
| `decisions/README.md` | 41 | 4 KB | Sub-Agent K |
| `decisions/DR-001-lookup-services.md` | 81 | 6 KB | Sub-Agent K |
| `decisions/DR-002-cache-patterns.md` | 94 | 8 KB | Sub-Agent K |
| `decisions/DR-003-public-contracts-facade.md` | 119 | 12 KB | Sub-Agent K |
| `decisions/DR-004-node-executors.md` | 96 | 9 KB | Sub-Agent K |
| `decisions/DR-005-intent-classifier.md` | 114 | 11 KB | Sub-Agent K |
| `decisions/DR-006-search-substrates.md` | 132 | 12 KB | Sub-Agent K |
| `decisions/DR-007-prompt-construction.md` | 125 | 13 KB | Sub-Agent K |
| `decisions/DR-008-di-configuration.md` | 147 | 14 KB | Sub-Agent K |
| **TOTAL** | **2819 lines** | **228 KB** | 3 sub-agents |

---

## Migration plan headlines (per `migration-plan.md`)

| PR | Priority | Scope | Effort |
|---|---|---|---|
| **PR #1** | HIGH | LATENT BUG Option A remediation + 4 facade Null peers | ~280 LOC; ~1 week |
| **PR #2** | HIGH | Bundled orphan DELETE (3 lookup + intent cascade + Option B prompt + 5th orphan) | ~2000 LOC removed; ~1 week |
| PR #3 | LOW | Inventory corrections + `Options/` → `Configuration/` mv + doc bug fixes | small; 1-2 days |
| PR #4 | MEDIUM | `ACTION-TYPE-REGISTRY.md` authoring | small doc; 1-2 days |
| PR #5+ | MEDIUM phased | Cache adoption migration (26 sites) | phased; 3-5 weeks |
| PR #6 | MEDIUM time-boxed | InsightsIntentClassifier.BuildPrompt() extraction | small; Insights team owns trigger |
| PR #7+ | LOW | 8 canonical pattern docs | 600-800 LOC docs; 1-2 weeks |
| PR #8 | MEDIUM | Runtime §F.1 detection fixture | 1-2 days; Insights team owns |

**Aggregate**:
- **Code delta**: NET ~-2280 LOC (net code reduction)
- **Docs delta**: ~1200-1800 LOC
- **Effort**: ~9-13 weeks aggregate across 13 teams; ~4-5 weeks audit-team direct PRs

## r3 unblock (per `r3-scope-recommendations.md`)

- **r3 Wave 1**: confirmed consistent with audit; LOCKED (~5d)
- **r3 Wave 2**: timeline **~2-3 weeks → ~1 week (5 days)** — ~50-66% reduction
- Driven by leveraging existing `PlaybookDispatcher` + `playbook-embeddings` (renamed `spaarke-playbook-index`) infrastructure
- **r3 Wave 1.1 (NullInsightsAi) → SUPERSEDED** by audit Migration PR #1 (avoids duplicate work)
- Total r3 short-term timeline: ~3-4 weeks → **~1.9 weeks (~9.5 days)**

---

## Downstream projects waiting on this audit

| Project | What's blocked | What's now UNBLOCKED |
|---|---|---|
| `ai-spaarke-insights-engine-r3` | Wave 2 (Tier 2.5 reconciliation scope) — was PROVISIONAL | **NOW UNBLOCKED** by `r3-scope-recommendations.md` |
| `spaarke-ai-platform-unification-r5` | Nothing explicitly | Heads-up still applies; R5 doc deferred per owner choice |

---

*Phase 3 complete 2026-06-04. Updated by main session after Sub-Agents I + J + K produced 12 synthesis files totaling 2819 lines.*
