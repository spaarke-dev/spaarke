# Task 145 — Insights Engine Regression + Architecture §5 Boundary Verification

**Date**: 2026-06-25
**Task**: 145 — Insights Engine regression suite — verify all binding-NEGATIVE components unchanged
**Phase**: 7 (WP4 Retirement + Project Closeout) — Wave 7-E
**Rigor Level**: STANDARD
**Verifier**: Subagent (main session commits)
**Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1` @ `150be7d02`

---

## §1 Grep Verification Table (Architecture §5 Binding-NEGATIVE Boundary)

Per architecture §5.2.1–§5.2.6 + spec NFR-06 + success criterion #15, the 6 Insights assets MUST be structurally untouched by chat-routing/memory code. Verification uses two-sided grep: zero hits in `Services/Ai/Chat/` and `Services/Ai/Memory/` (the BINDING-NEGATIVE check) and confirmation the assets still live under `Services/Ai/Insights/` (negative-control: not accidentally deleted).

| # | Asset | Chat/ hits | Memory/ hits | Insights/ presence (negative-control) | Status |
|---|---|---|---|---|---|
| 1 | `spaarke-insights-index` (AI Search index name) | **0** | **0** | (referenced by Insights orchestration, see Composition/ + Search/) | ✅ PASS |
| 2 | `MultiIndexComposer` (Insights orchestration class) | **0** | **0** | Present @ `Services/Ai/Insights/Composition/MultiIndexComposer.cs` (1 hit + cross-refs in playbooks) | ✅ PASS |
| 3 | `InsightsOrchestrator` (Insights pipeline entry) | **0** | **0** | Present @ `Services/Ai/Insights/InsightsOrchestrator.cs` (22 hits within Insights) | ✅ PASS |
| 4 | `EvidenceSufficiencyNode` (Insights node executor) | **0** | **0** | (Insights node — present in nodes catalog under Insights/) | ✅ PASS |
| 5 | `GroundingVerifyNode` (Insights node executor) | **0** | **0** | (Insights node — present in nodes catalog under Insights/) | ✅ PASS |
| 6 | `sprk_performancesummary` (Insights-owned Dataverse column) | **0** | **0** | (Insights schema — no chat-side mutations) | ✅ PASS |

**Negative-control aggregate**: `grep MultiIndexComposer|InsightsOrchestrator|EvidenceSufficiencyNode|GroundingVerifyNode` against `Services/Ai/Insights/` returned 61 occurrences across 9 files — confirms the assets remain wholly intact under the Insights subsystem, with no accidental deletion during Phase 4-6 work.

**Result**: 12/12 paired grep checks PASS. Zero architecture §5 boundary violations detected.

---

## §2 Insights Regression Suite Results

**Test project**: `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj`

**Test files located** (via Glob — 13 Insights-relevant test files under `tests/unit/` and `tests/integration/`):

- `Services/Ai/Insights/InsightsOrchestratorTests.cs`
- `Services/Ai/Insights/InsightsPlaybookCacheKeyTests.cs`
- `Services/Ai/Insights/InsightsPlaybookExecutionCacheTests.cs`
- `Services/Ai/Insights/Routing/InsightsIntentClassifierTests.cs`
- `Services/Ai/Insights/Routing/NullInsightsIntentClassifierTests.cs`
- `Services/Ai/Insights/Routing/InsightsActionRouterTests.cs`
- `Services/Ai/Insights/Sanitization/InsightsContentSanitizerTests.cs`
- `Services/Ai/Nodes/InsightsNodesIntegrationTests.cs`
- `Services/Ai/Nodes/InsightsNodeTestHelpers.cs`
- `Services/Jobs/Insights/InsightsIngestJobHandlerTests.cs`
- `Api/Insights/InsightsAssistantEndpointTests.cs`
- `Api/Insights/InsightsAssistantEndpointStreamingTests.cs`
- `Api/Insights/InsightsSearchEndpointTests.cs`
- (integration) `tests/integration/Spe.Integration.Tests/InsightsToolIntegrationTests.cs` — not invoked (requires live Graph; integration suite scope per project convention)

**Command run**:
```
dotnet test "tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj" \
  --nologo --filter "FullyQualifiedName~Insights"
```

**Result**:
```
Passed!  - Failed:  0, Passed:  523, Skipped:  0, Total:  523, Duration: 31s
```

- **Failed**: 0
- **Passed**: 523
- **Skipped**: 0
- **Runtime**: 31s
- **Status**: ✅ GREEN

No failures, no skips. Insights subsystem is behaviorally untouched.

---

## §3 Dataverse `sprk_performancesummary` Audit

**Status**: SKIPPED (per POML Step 4 explicit allowance + main-session instruction).

**Rationale**: The binding mechanism is the **source-side grep** in §1 (Asset #6). With zero references to `sprk_performancesummary` in `Services/Ai/Chat/` or `Services/Ai/Memory/`, no chat-side write path exists in code. A Dataverse runtime audit would be belt-and-suspenders (confirming no historic writes via leaked test/dev paths) but is not required when the source grep is clean.

**Risk if skipped**: Negligible. The audit could only detect drift if (a) a write path existed in source code (caught by §1 grep) or (b) writes occurred from an out-of-band path (PCF, ribbon, plugin) — none of which were touched by Phase 4-6 chat-routing work. Future projects modifying Insights schema should re-run this audit.

---

## §4 Architecture §5 Compliance Check (Binding-NEGATIVE Rules)

Per `architecture/stateful-chat-architecture.md` §5.2.1–§5.2.6, the 6 binding-NEGATIVE rules:

| Rule | Architecture Reference | Asset | Status |
|---|---|---|---|
| Chat memory MUST NOT use `spaarke-insights-index` for retrieval | §5.2.1 | AI Search index | ✅ PASS (0 chat/memory refs) |
| Chat memory MUST NOT consume `MultiIndexComposer` for routing | §5.2.2 | Orchestration class | ✅ PASS (0 chat/memory refs) |
| Chat memory MUST NOT call `InsightsOrchestrator` for pipeline | §5.2.3 | Pipeline entry | ✅ PASS (0 chat/memory refs) |
| Chat memory MUST NOT execute `EvidenceSufficiencyNode` | §5.2.4 | Node executor | ✅ PASS (0 chat/memory refs) |
| Chat memory MUST NOT execute `GroundingVerifyNode` | §5.2.5 | Node executor | ✅ PASS (0 chat/memory refs) |
| Chat memory MUST NOT write to `sprk_matter.sprk_performancesummary` | §5.2.6 | Dataverse column | ✅ PASS (0 chat/memory refs in source) |

**Aggregate status**: 6/6 binding-NEGATIVE rules verified PASS. Pattern-level reuse boundary held throughout Phase 4-6. The Insights Engine subsystem remains a categorical mismatch peer to chat-memory — exactly as architecture §5 specifies.

---

## §5 Acceptance Criterion Mapping (Task POML)

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | Insights regression suite runs to completion with zero failures | ✅ PASS | §2 — 523/523 passed in 31s |
| 2 | All 6 grep checks return 0 hits (or only inside Insights/, not Chat/Memory) | ✅ PASS | §1 — all 12 Chat/Memory paired greps returned 0; negative-control 61 hits inside Insights/ confirms preservation |
| 3 | `sprk_performancesummary` has zero chat-side mutations | ✅ PASS (via source grep) | §1 Asset #6 — 0 refs in `Services/Ai/Chat/` + `Services/Ai/Memory/`; §3 Dataverse audit skipped per POML allowance |
| 4 | Report documents architecture §5 compliance | ✅ PASS | This document, §4 |

**Aggregate**: 4/4 acceptance criteria met.

---

## Closeout

- Spec NFR-06: Insights regression suite passes — ✅ VERIFIED
- Success criterion #15: Grep + Insights regression suite — ✅ VERIFIED
- FR-37: Architecture §5 boundary preservation — ✅ VERIFIED
- No escalation required.
- No source changes; no commits. Main session commits this report at Phase 7 wrap (alongside task 144).
- Task 145 → ✅ in TASK-INDEX.md (row 474).

**Next task in wave 7-E**: 144 ✅ + 145 ✅ both complete → wave 7-E closeable → 7-F (147 + 148 final code-review + adr-check) unblocked.
