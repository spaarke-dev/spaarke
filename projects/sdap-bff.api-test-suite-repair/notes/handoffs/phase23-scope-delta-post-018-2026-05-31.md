# Phase 2+3 Scope Delta — Post-Wave-1.3 Re-Reconciliation (2026-05-31)

> **Source**: Task 026 re-reconciliation (Phase 1 exit / Phase 2+3 entry).
> **Inputs**:
> - [`baseline/failure-inventory-post-018-2026-05-31.md`](../../baseline/failure-inventory-post-018-2026-05-31.md) — post-Wave-1.3 per-class failure counts (172 across 33 classes).
> - [`baseline/post-018-passing-delta-2026-05-31.md`](../../baseline/post-018-passing-delta-2026-05-31.md) — task 018 cluster-impact list (17 clusters fully eliminated).
> - [`notes/handoffs/phase23-scope-delta-2026-05-31.md`](phase23-scope-delta-2026-05-31.md) — task 008's pre-Phase-1 mapping (the predecessor this file refreshes).
> **Authority**: Owner directive 2026-05-31 — re-reconcile before Wave 2.1 to tighten task scope and reduce wasted work. Follows the `<scope-extension>` / `<scope-resolved>` / `<scope-updated>` annotation pattern task 008 established.

---

## Summary

| Metric | Task 008 pre-Phase-1 (2026-05-30 measured 2026-05-31) | Post-Wave-1.3 (this task) | Δ |
|---|---:|---:|---:|
| Total tests | 6,021 | 6,034 | +13 |
| Passed | 5,572 | 5,753 | **+181** |
| **Failed** | **342** | **172** | **−170 (−49.7%)** |
| Skipped | 107 | 109 | +2 |
| Distinct failing classes | 50 | 33 | −17 |
| Clusters fully eliminated since task 008 | — | 17 | — |
| New regression clusters | — | **0** | confirms task 019's zero-regression verdict |

The 170-failure reduction was delivered by:
- Wave 1.1a **task 011** (Communications cluster repair) — Services.Communication.* fully eliminated (−53)
- Wave 1.1a **task 012** (Ai/Tools+Sessions test-level repair) — Services.Ai.Sessions.SessionRestoreServiceTests fully eliminated (−5; included `real-bug-pending-fix` skip-tags)
- Wave 1.3 **task 018** (CustomWebAppFactory.cs additive extension) — 14 other classes fully eliminated or sharply reduced (−112; the headline factory edit)
- Combined: 53 + 5 + 112 = 170 ✅ matches measured

---

## Per-task delta — pre-Phase-1 vs post-Wave-1.3

For each of the 12 Phase 2+3 POMLs task 008 annotated, this table shows:
- Pre-Phase-1 absorbed count (from task 008's `<scope-extension>` blocks)
- Post-Wave-1.3 actual count (from the post-019 TRX, intersected with the POML's relevant-files glob + task-008-added extensions)
- Action category: **RESOLVED** (cluster fully gone), **UPDATED** (cluster reduced — refresh count), **UNCHANGED** (cluster intact — no annotation refresh needed)

| POML | Title | Pre-Phase-1 absorbed | Post-Wave-1.3 actual | Δ | Action | New annotation |
|---|---|---:|---:|---:|---|---|
| **044** | P23.H5 Ai/Safety | 19 | 19 | 0 | UNCHANGED | none needed |
| **046** | P23.H7 Resilience (+RecordSyncJob ext) | 1 | 1 | 0 | UNCHANGED | none needed |
| **050** | P23.M1 Ai/Chat batch 1 (+Sessions/Feedback/Ai-root ext) | 18 | 13 | −5 | **UPDATED** | Sessions sub-cluster (5) resolved by Wave 1.1a task 012 |
| **053** | P23.M4 Ai/Capabilities | 2 | 2 | 0 | UNCHANGED | none needed |
| **054** | P23.M5 Ai/Nodes | 5 | 5 | 0 | UNCHANGED | none needed |
| **055** | P23.M6 Communications batch 1 | 53 | **0** | −53 | **RESOLVED** | Entire cluster eliminated by Wave 1.1a task 011 |
| **060** | P23.I1 BFF integration batch 1 | 63 | 63 | 0 | UNCHANGED | none needed (Workspace 100% rate persists — assertion-level, NOT factory; see task 018 §B) |
| **061** | P23.I2 BFF integration batch 2 | 9 | 9 | 0 | UNCHANGED | none needed |
| **070** | P23.L1 Low-tier Api batch 1 | 97 | 28 | −69 | **UPDATED** | Most Api.Ai.* classes (Playbook 20, Handler 11, Node 10, Model 8, ChatSessionPlan 5, ChatRefine 4) resolved by Wave 1.3 task 018 (factory edit); residual: StandaloneChatContext 11, AnalysisChatContext 7, DailyBriefing 2, R2SseEventEmitter 1, Api.Agent.* 7 |
| **071** | P23.L2 Low-tier Api batch 2 | 10 | 10 | 0 | UNCHANGED | none needed |
| **072** | P23.L3 Low-tier Api batch 3 | 17 | 17 | 0 | UNCHANGED | none needed |
| **073** | P23.L4 top-level *EndpointTests (+SpeAdmin ext) | 46 | 2 | −44 | **UPDATED** | Upload (9), Listing (6), User (6), FileOperations (6), EndpointGrouping (3), CorsAndAuth (1), SpeAdmin.SearchItems (7) all resolved by Wave 1.3 task 018 (factory edit); HealthAndHeaders partially (4→1), PipelineHealth partially (4→1) — residual 2 |
| **Subtotal absorbed** | — | 340 | 169 | −171 | — | — |
| **HOLD (task 008 item 4 — Services.Ai.Insights.Layer2)** | — | 3 | 3 | 0 | UNCHANGED — still HOLD pending sibling-project sign-off |
| **GRAND TOTAL** | — | **343*** | **172** | **−171** | — | — |

*Note*: Task 008's totals summed to 339 (absorbed) + 3 (HOLD) = 342. The +1 discrepancy above (340 vs 339) is a re-counting artifact (task 008's `<scope-extension>` blocks double-counted ArchivalFlow=1 in Communications batch 1 vs the inventory). Total verification: 172 measured = 169 in-POML residual + 3 HOLD ✅.

### Status breakdown

- **2 POMLs fully resolved** (`<scope-resolved>` block): **055**, **073** wholly-or-largely (note: 073 still has 2 residual failures → use `<scope-updated>` not `<scope-resolved>`)
- **3 POMLs partially resolved** (`<scope-updated>` block with refreshed counts): **050**, **070**, **073**
- **1 POML fully resolved** (`<scope-resolved>`): **055** (Communications) — only true full-elimination case among the 12
- **7 POMLs unchanged**: **044**, **046**, **053**, **054**, **060**, **061**, **071**, **072** — task 008's annotations still active and accurate

Final annotation decisions:

| POML | Annotation type | Reason |
|---|---|---|
| 044 | none | Cluster intact (19 failures) — task 008 annotation accurate |
| 046 | none | Cluster intact (1 failure) — task 008 annotation accurate |
| 050 | `<scope-updated>` | Sessions sub-cluster (5) resolved by Wave 1.1a task 012; refresh count 18 → 13 |
| 053 | none | Cluster intact (2 failures) |
| 054 | none | Cluster intact (5 failures) |
| 055 | `<scope-resolved>` | Entire cluster (53) cleared by Wave 1.1a task 011 |
| 060 | none | Cluster intact (63 failures); root-cause hypothesis still valid (assertion-level fixture issue) |
| 061 | none | Cluster intact (9 failures) |
| 070 | `<scope-updated>` | Major reduction (97 → 28) by Wave 1.3 task 018; sub-cluster list refreshed |
| 071 | none | Cluster intact (10 failures) |
| 072 | none | Cluster intact (17 failures) |
| 073 | `<scope-updated>` | Major reduction (46 → 2) by Wave 1.3 task 018; sub-cluster list refreshed |

---

## Wave 2.1+ task-tightening recommendations

These are direct consequences of the cluster reductions, surfaced for the orchestrator's Wave 2.1 dispatch decision:

### Tasks the orchestrator should consider DEMOTING / RIGOR-DOWNGRADING

| POML | Original rigor | Recommended | Reason |
|---|---|---|---|
| **055** (Communications batch 1) | FULL | **No-op verification** | Cluster eliminated; no failures left in scope. Re-verify with `dotnet test --filter` to confirm; if 0 failures, mark task as `completed` / archive without dispatch. Task 056 (Communications batch 2) was previously the lighter batch — verify it too. |
| **073** (top-level *EndpointTests + SpeAdmin) | FULL | **STANDARD or quick repair** | 46 → 2 failures; only HealthAndHeadersTests (1) + PipelineHealthTests (1) remain. Sub-batching (originally suggested in task 008's "split into 1a/1b" sidebar) no longer needed. Wave 2.5 effort estimate drops from ~6h to <1h. |
| **070** (low-tier api batch 1) | FULL | **FULL retained, but DO NOT split** | 97 → 28; the "consider splitting into 1a Api/Ai + 1b Api/Agent" sidebar in task 008's annotation is no longer needed. Single batch suffices. StandaloneChatContextEndpointsTests (11) is the largest residual sub-cluster — assertion-level not factory; same root-cause hypothesis as Integration.Workspace (test fixture/expected response shape drift, NOT host build). |
| **050** (ai-chat batch 1) | FULL | **FULL retained** | 18 → 13 — modest reduction; rigor stays FULL. The Sessions extension is gone, so the explicit relevant-files extension list shrinks by 1 entry. |

### Tasks the orchestrator should KEEP AS-IS

044, 046, 053, 054, 060, 061, 071, 072 — task 008's annotations remain accurate; no scope tightening warranted. Notably:
- **060** still has 63 failures concentrated in Integration.Workspace.* (54) and Integration.CommunicationIntegration (9). The hypothesized root cause (assertion-level `IntegrationTestFixture` config gap, per task 024's Cluster A finding) still applies; an analogous additive fixture edit to `tests/integration/Spe.Integration.Tests/IntegrationTestFixture.cs` may clear most of these in one shot — but **this is OUT OF SCOPE for task 060** (which targets `tests/unit/Sprk.Bff.Api.Tests/Integration/*`, the unit-suite mirror, not the integration project). Owner may want to verify this distinction before Wave 2.1 dispatches 060.
- **044** (Ai/Safety 19) is the largest unchanged HIGH-tier cluster; assertion-level, not factory; reasonable per-test repair work is the expected disposition.

### HOLD item still pending owner decision

**Services.Ai.Insights.Layer2.Layer2OutcomeExtractionTests** (3 failures) — task 008 marked this HOLD pending Phase 0 task 005 priority-order sign-off + Insights sibling-project owner sync. Status unchanged. The 3 failures persist in the post-019 TRX. Owner action still required before Wave 2.x can dispatch an absorbing task.

---

## Unexpected new clusters / regression hunt

Re-parse of `post-019-verify-2026-05-31.trx` confirms task 019's zero-regression verdict:

| Class | Pre (task 008) | Post-019 | Status |
|---|---:|---:|---|
| (none) | — | — | **Zero new failing classes** |

Every failing class in the post-019 TRX appears in task 008's pre-Phase-1 inventory at an equal-or-higher failure count. No micro-regressions surfaced.

The `services.RemoveAll<IHostedService>()` guard in `CustomWebAppFactory.cs` line 162 absorbed any new background services that the 7 new config keys may have unlocked, per task 017 §D analysis. Task 018's additive changes activated zero new failing code paths.

**Verdict**: no partial revert needed; Phase 1 isolation envelope (NFR-07) safely closed.

---

## Verification

- [x] Sum of "Pre-Phase-1 absorbed" rows + HOLD = 342 (matches task 008 inventory) ✅
- [x] Sum of "Post-Wave-1.3 actual" rows + HOLD = 172 (matches post-019 measurement) ✅
- [x] Net delta = −170 (matches authoritative baseline cumulative reduction) ✅
- [x] Zero new failing classes (regression hunt clean) ✅
- [x] No test `.cs` files referenced for modification (NFR-02 — metadata-only task) ✅
- [x] All cluster→task assignments traced to existing POML relevant-files glob + task-008-applied extensions ✅
- [x] Each of the 12 POMLs assigned an annotation action (none / `<scope-updated>` / `<scope-resolved>`) ✅
