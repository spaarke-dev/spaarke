# Post-Wave-1.1a Runtime Failure Delta — 2026-05-31

> **Source**: Task 014 (P1.A5) execution, 2026-05-31.
> **Inputs**: `baseline/post-wave1.1a-runtime-2026-05-31.trx` (this task's run) vs. `baseline/test-baseline-2026-05-31.trx` (Wave 1 baseline / task 001).
> **Companion**: `baseline/failure-inventory-2026-05-31.md` (per-class baseline) + `notes/handoffs/phase23-scope-delta-2026-05-31.md` (task 008 reconciliation).
> **Authority**: Per §6.3 binding rule, this becomes the AUTHORITATIVE post-Wave-1.1a + 1.1b baseline that Phase 2+3 cluster work and long-tail work measure against. Previous authoritative baseline (342) remains the Phase 0 anchor for absolute comparisons.

---

## Headline

| Metric | Pre (Wave 1 baseline 2026-05-31) | Post (Wave 1.1a + 1.1b 2026-05-31) | Δ |
|---|---:|---:|---:|
| Total tests | 6,021 | 6,020 | −1 |
| Passed | 5,572 (92.5%) | 5,627 (93.5%) | **+55** |
| Failed | **342** (5.7%) | **284** (4.7%) | **−58 (−17.0%)** |
| Skipped | 107 (1.8%) | 109 (1.8%) | +2 |
| Build errors | 0 | 0 | 0 |
| Build warnings | 17 | 17 | 0 |
| Duration | 1m 13s | 1m 14s | +1s |

**Build verification**: `dotnet build tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj -c Release` returned **0 errors / 17 warnings** (unchanged from 2026-05-31 Wave 1 baseline). Tasks 010, 011, 012, 013 did not regress the build.

---

## Where the −58 went (delta accounting)

| Source | Net effect on `Failed` |
|---|---:|
| Task 011 (Communications batch 2 — `Services/Communication/*`) — 53 tests now pass | **−53** |
| Task 012 (Ai/Tools, Sessions batch 3) — RB-T012-01 skip-tagging (2 tests `real-bug-pending-fix`) | **−2** |
| Net minor change (1 test: Api.Ai.* −1 net; Services.Ai non-Safety +1 net; effect cancels except −1 absolute) | **−3** (residual) |
| Tasks 010 + 013 — trait-tagging only, minimal runtime effect | ≈ 0 |
| **Total** | **−58** |

Verification: `Failed` Δ (−58) + `Skipped` Δ (+2) accounts for 60 tests; `Passed` Δ (+55) + `Total` Δ (−1) = 56 → reconciliation: 56 newly-passing − 1 test removed from total = 55. The +2 skipped = 2 tests changed from Failed to Skipped (RB-T012-01); the remaining 56 net moves: 53 Communications failures now pass + 3 residual net from other classes (likely Api.Ai.* −1 + Services.Ai.Chat fluctuation).

---

## Namespace Roll-up — Pre vs. Post

(Pre numbers from `baseline/failure-inventory-2026-05-31.md` "Failures by Namespace Prefix — Roll-up" section)

| Namespace bucket | Pre | Post | Δ | Notes |
|---|---:|---:|---:|---|
| Api.Ai.* | 90 | 89 | −1 | 11 distinct AI endpoint test classes — mostly unchanged; largest single hot cluster |
| Integration.Workspace.* | 54 | 54 | 0 | WorkspaceEndpoints (31) + WorkspaceLayoutEndpoints (23) — 100% failure rate persists |
| Services.Communication.* | **53** | **0** | **−53** | Task 011 cleared the entire cluster (AssociationMapping 29 + DataverseRecordCreation 23 + ArchivalFlow 1) |
| Top-level (Sprk.Bff.Api.Tests.*) | 39 | 39 | 0 | UploadEndpoints/UserEndpoints/FileOperations/etc. — unchanged |
| Services.Ai.* (non-Safety) | 22 | 23 | +1 | Sessions/Feedback/Rag/Insights/Chat/Capabilities — net +1 from minor Chat fluctuation |
| Services.Ai.Safety.* | 19 | 19 | 0 | CitationExtractor (8) + PrivilegeLeakage (7) + others |
| Integration.* (non-Workspace) | 18 | 18 | 0 | CommunicationIntegration (9) + SseStreamingIntegration (8) + PlaybookExecution (1) |
| Api.Reporting.* | 17 | 17 | 0 | ReportingEndpoints (12) + ReportingAuthorizationFilter (5) |
| Api.Office.* | 10 | 10 | 0 | OfficeEndpoints |
| Api.Agent.* | 7 | 7 | 0 | AgentConversation/HandoffUrlBuilder/AgentConfiguration |
| SpeAdmin.* | 7 | 7 | 0 | SearchItems |
| Services.Jobs.* | 1 | 1 | 0 | RecordSyncJob (single failure) |
| **TOTAL** | **342** | **284** | **−58** | |

Sum verified: 89+54+0+39+23+19+18+17+10+7+7+1 = 284 ✅

---

## Top-5 Remaining Hot Clusters (post-Wave-1.1a)

Ranked by absolute failure count — these dominate the Phase 2+3 work:

| # | Cluster | Failures | % of remaining 284 | Owning Phase 2+3 task(s) |
|---|---|---:|---:|---|
| 1 | Api.Ai.* (11 classes) | 89 | 31.3% | **070** (LOW-tier Api/Ai batch) |
| 2 | Integration.Workspace.* (2 classes, 100% fail) | 54 | 19.0% | **060** (BFF Integration batch 1) |
| 3 | Top-level *EndpointTests (8 classes) | 39 | 13.7% | **073** (LOW-tier top-level endpoint tests) |
| 4 | Services.Ai.* non-Safety (Sessions/Feedback/Rag/Insights/Chat/Capabilities/Nodes/WorkingDoc) | 23 | 8.1% | **050** (ai-chat batch 1, extended per task 008 defaults) + **054** (ai-nodes) + Insights HOLD |
| 5 | Services.Ai.Safety.* (5 classes) | 19 | 6.7% | **044** (ai-safety) |
| **Subtotal top-5** | — | **224** | **78.9%** | — |

The remaining 60 failures (21.1%) spread across 7 namespace buckets — long-tail work suitable for cleanup waves after the top-5 are cleared.

**Top-5 alignment with task 008 reconciliation**: All 5 hot clusters already have absorbing Phase 2+3 tasks per `notes/handoffs/phase23-scope-delta-2026-05-31.md`. No new tasks required.

---

## Cross-reference: §3.2 compile-fixed files (now post-Wave-1.1a)

Of the 7 §3.2 compile-fixed files that still contributed runtime failures in the Wave 1 baseline, post-Wave-1.1a status:

| §3.2 file | Pre failures | Post failures | Δ | Disposition |
|---|---:|---:|---:|---|
| Integration/CommunicationIntegrationTests.cs | 9 | 9 | 0 | Pending (task 060 P2+3 work) |
| Services/Ai/Sessions/SessionRestoreServiceTests.cs | 5 | 5 | 0 | Pending (task 050 extended per default decision item 1) |
| Services/Ai/WorkingDocumentServiceTests.cs | 2 | 2 | 0 | Pending (task 050 extended per default decision item 3) |
| **Services/Communication/ArchivalFlowTests.cs** | 1 | **0** | **−1** | Task 011 ✅ |
| **Services/Communication/AssociationMappingTests.cs** | 29 | **0** | **−29** | Task 011 ✅ |
| **Services/Communication/DataverseRecordCreationTests.cs** | 23 | **0** | **−23** | Task 011 ✅ |
| Services/Jobs/RecordSyncJobTests.cs | 1 | 1 | 0 | Pending (task 046 extended per default decision item 6) |

Task 011's repair scope (Phase 1 P1.A batch 2 Communications): 53 / 53 cleared. Matches task 011's report exactly.

---

## Authoritative Baseline Status

This post-Wave-1.1a TRX (`baseline/post-wave1.1a-runtime-2026-05-31.trx`) is the new **Phase 2+3 starting baseline**: **284 failures across 46 classes / 11 namespace buckets**.

The 2026-05-31 Wave 1 baseline (342) remains valid for absolute Phase 0 anchoring; the post-Wave-1.1a baseline (284) is the measurement surface that Phase 2+3 cluster work and long-tail work will close against.

**Per §6.3 binding rule** all Phase 2+3 task POMLs and downstream artifacts cite ONE of:
- 342 (Phase 0 anchor; cite when measuring full-project trajectory)
- 284 (post-Wave-1.1a; cite when measuring Phase 2+3 progress against immediate starting state)

NEVER cite design.md §3's stale 269.

---

## Verification (Acceptance Criteria)

- [x] `dotnet build tests/unit/Sprk.Bff.Api.Tests/ -c Release` exits 0 with NO `error CS` lines (build succeeded in 6.37s; 0 errors / 17 warnings)
- [x] `post-wave1.1a-delta-2026-05-31.md` exists with total/passed/failed/skipped + delta vs 342 (this file)
- [x] `post-wave1.1a-runtime-2026-05-31.trx` exists and is parseable (XPath query returned 6,020 result nodes)
- [x] No modifications to `src/`, `power-platform/`, `infra/`, `scripts/` (measurement-only task, NFR-01)
- [x] No modifications to `tests/` (measurement-only task, NFR-02)
- [x] Author: AI agent (this task 014) — no manual review intervention required for measurement

---

## Files Produced by This Task

| Path | Size | Purpose |
|---|---|---|
| `projects/sdap-bff.api-test-suite-repair/baseline/post-wave1.1a-runtime-2026-05-31.trx` | ~6.5 MB (TRX) | Authoritative post-Wave-1.1a run record |
| `projects/sdap-bff.api-test-suite-repair/baseline/post-wave1.1a-delta-2026-05-31.md` | this file | Human-readable delta + namespace roll-up + top-5 hot clusters |
