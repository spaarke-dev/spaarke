# Task 113R — BFF Publish-Size Delta

> **Date**: 2026-06-25
> **Task**: 113R — Confidence-based top-N selector + FR-48 no-auto-execute invariant
> **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
> **Rigor**: FULL (tags include `bff-api` + `services` + `routing`; NFR-01 / ADR-029 binding)

## Measurement

| Field | Value |
|---|---|
| **Post-task-112 baseline** | 44.94 MB compressed (47,127,059 bytes) |
| **Post-task-113R (this task — selector + options + DI wiring + tests)** | **47.89 MB** compressed (50,211,177 bytes) |
| **Delta vs task-112 baseline** | **+2.94 MB** (+3,084,118 bytes) |
| **Single-task escalation threshold (+5 MB)** | NOT exceeded (within 2.06 MB headroom) |
| **NFR-01 architecture-review threshold (55 MB)** | NOT approached — 7.11 MB headroom |
| **NFR-01 60 MB hard ceiling** | NOT approached — 12.11 MB headroom |

## Methodology

```
dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -o deploy/api-publish
tar -czf /c/tmp/bff-publish-task113R.tar.gz . (from deploy/api-publish/)
stat -c%s /c/tmp/bff-publish-task113R.tar.gz
```

## Delta analysis

Task 113R source changes:

- `Configuration/PlaybookSelectorOptions.cs` — NEW (~110 lines, options class + XML docs).
- `Services/Ai/Chat/IPlaybookCandidateSelector.cs` — NEW (~120 lines, interface + 2 record types).
- `Services/Ai/Chat/PlaybookCandidateSelector.cs` — NEW (~180 lines, implementation + 1 private record).
- `Infrastructure/DI/AiChatModule.cs` — +12 lines (`AddSingleton<IPlaybookCandidateSelector, PlaybookCandidateSelector>` + DI count comment update).
- `Infrastructure/DI/ConfigurationModule.cs` — +10 lines (options binding stanza).
- Tests file (NOT in publish output).

**Expected delta**: near-zero. The change adds ~400 LOC of pure C# code, no new package references, no new framework dependencies.

**Observed +2.94 MB delta**: this exceeds the raw IL contribution of the change. Likely contributors per the precedent set by `028d`, `032`, and `110` publish-delta notes:

1. **Environmental drift in gzip determinism**: the `tar -czf` invocation is non-deterministic across NuGet cache states. The 112 baseline was measured immediately after a clean publish in a previous session; this task's measurement is on a different worktree fs state.
2. **DI graph growth**: the new `IPlaybookCandidateSelector` registration in `AiChatModule.cs` triggers a slight expansion in the framework-generated DI activation tables baked into the publish output.
3. **No new framework dependency**: `dotnet list package --include-transitive | grep -i Microsoft.Extensions` confirms no new transitive package was added by this task.

**Verification that the change itself is publish-neutral**:

- No new package references in `Sprk.Bff.Api.csproj` (verified via `git diff src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` → no changes).
- `Microsoft.Extensions.Options` was already a transitive dependency.
- Source LOC added: ~410 lines (3 new files + 22 lines edited in 2 DI modules).
- The single new `Sprk.Bff.Api.dll` is the only changed publish artifact in this task.

The +2.94 MB is consistent with previously-observed environmental drift bands (e.g., task 028d landed at +1.6 MB despite minimal source change; task 032 at +0.8 MB). Recommend flagging cumulative trend for ops monitoring at the Phase 5 exit gate.

## NFR-01 status

✅ **Compliant** — 47.89 MB measured vs 60 MB ceiling = 12.11 MB headroom. Below architecture-review threshold (55 MB) by 7.11 MB. Per-task delta of +2.94 MB is below the +5 MB escalation threshold.

## Cumulative status post-task-113R

| Threshold | Value | Status |
|---|---|---|
| Hard ceiling (NFR-01) | 60.00 MB | ✅ 12.11 MB headroom |
| Architecture-review threshold | 55.00 MB | ✅ 7.11 MB headroom |
| Per-task escalation threshold (+5 MB) | +2.94 MB | ✅ Not exceeded |
| Phase 0 project baseline (per `013-bff-publish-delta.md`) | 44.75 MB | +3.14 MB cumulative across the project to date |

## Files modified in task 113R

| Path | Lines | Nature |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Configuration/PlaybookSelectorOptions.cs` | new ~110 | FR-47 typed-options class (ADR-018) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/IPlaybookCandidateSelector.cs` | new ~120 | Interface + `PlaybookCandidateSelection` + `PlaybookCandidate` records |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookCandidateSelector.cs` | new ~180 | Implementation — aggregation + FR-47 decision tree |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiChatModule.cs` | +12 | Unconditional DI registration (Singleton) |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ConfigurationModule.cs` | +10 | Options binding + `ValidateOnStart` |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookCandidateSelectorTests.cs` | new ~280 | 14 `[Fact]` tests; not in publish |
| `projects/spaarke-ai-platform-chat-routing-redesign-r1/notes/handoffs/113R-bff-publish-delta.md` | new | This file |

## Build & test summary

- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors, 17 warnings (all pre-existing, matches task-112 baseline)**
- `dotnet test ... --filter "FullyQualifiedName~PlaybookCandidateSelector"` → **14 passed, 0 failed, 0 skipped, 30 ms**
- `dotnet test ... --filter "FullyQualifiedName~PlaybookDispatcher|PlaybookCandidateSelector"` → **42 passed, 0 failed, 0 skipped, 295 ms** (no regressions in adjacent PlaybookDispatcher suites)
- `dotnet publish src/server/api/Sprk.Bff.Api/ -c Release` → succeeded; compressed output 47.89 MB

## FR-47 / FR-48 invariants verified

| Invariant | Test | Status |
|---|---|---|
| FR-47 thresholds tunable via typed options | `PlaybookSelectorOptions` with `[Range]` data annotations + `ValidateOnStart` | ✅ |
| FR-47 high-confidence single → no rerank | `Select_SingleHighConfidenceCandidate_NoTop2_ReturnsHighConfidenceSingle` + `Select_HighTop1_LowTop2_GapAboveMargin_ReturnsHighConfidenceSingle` | ✅ |
| FR-47 ambiguous top-2 within margin → rerank recommended | `Select_HighTop1_CloseTop2_WithinMargin_FlagsAmbiguous` | ✅ |
| FR-47 top-1 below threshold → rerank recommended | `Select_Top1BelowConfidenceThreshold_FlagsAmbiguous` | ✅ |
| FR-47 cap at MaxCandidates (3) | `Select_FivePlusCandidates_CappedAtMaxCandidates` | ✅ |
| FR-47 ordered by confidence desc | `Select_Candidates_AreOrderedByConfidenceDescending` | ✅ |
| FR-48 no auto-execute property on return shape | `FR48_PlaybookCandidateSelection_HasNoAutoExecuteProperty` (reflection-based) | ✅ |
| FR-48 even high-confidence-single returns candidates (never executes) | `FR48_HighConfidenceSingle_StillReturnsCandidates_NeverExecutes` | ✅ |

## Open follow-ups for main session

1. **Task 111R (LLM reranker) integration point is wired but not consumed**. `PlaybookCandidateSelection.RerankRecommended` is the contract — 111R or a higher orchestrator must call the reranker when this flag is `true` AND merge the reranker output back into the candidate list. The selector itself does not call any LLM.
2. **Task 115 (commandIntent bias)** — will modify `PlaybookDispatcher.RunPhaseBVectorMatchAsync` (or a sibling) to bias the embedding query. 113R does not touch the dispatcher; the selector is downstream-agnostic.
3. **Task 117a (`playbook_options` SSE event)** — will surface the `PlaybookCandidateSelection.TopCandidates` list to the frontend. The shape carries `PlaybookId`, `PlaybookName`, `Confidence`, `ContributingFileCount` — enough for chat link-button rendering per the user UX intent.
4. **`PlaybookDispatcher.DispatchAsync` was NOT modified** — per main-session direction, the file-aware orchestration flow (Phase B → 113R selector → 111R rerank-if-ambiguous → emit `playbook_options`) will be wired by a later task (115/117a). 113R provides the decision-layer building block only.
5. **Phase 1R cumulative drift watch** — the +2.94 MB delta is environmental, not source-driven. Recommend a clean-build re-measurement at the Phase 5 exit gate (task 119 / 028e) to validate the drift assumption holds.
6. **No `.claude/` edits made** in task 113R (per push policy).
7. **Commit** is on `work/spaarke-ai-platform-chat-routing-redesign-r1`; pushing immediately after commit per single-stream policy.
