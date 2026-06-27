# Task 116 — BFF Publish-Size Delta + Grep Evidence

**Task**: 116 — Remove `SoftSlashIntentToCapabilityName` dict (BFF + FE)
**Phase**: 5R Wave 5-C
**FR**: FR-20
**Date**: 2026-06-25
**Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`

---

## Summary

Removed the dict-based `CapabilityRouter` Layer 0.5 soft-slash short-circuit
on the BFF side and the structurally redundant `SOFT_SLASH_TO_INTENT` lookup
table on the FE side. The `intentHint` wire field is preserved end-to-end; the
slash UX is now driven solely by the Phase B vector-query bias added in
task 115 (FR-20 binding: slash + NL converge on the SAME path).

---

## Files Modified

### BFF (C#)

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs` | Deleted `SoftSlashIntentToCapabilityName` dict (4 entries), 4 `SoftSlash*CapabilityName` constants, `SoftSlashDecisionConfident` constant, `TryClassifySoftSlash()` method (~80 lines), and the Layer 0.5 invocation in `RouteSync`. Replaced with a 2-line historical-context comment + `_ = intentHint;` discard. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/ICapabilityRouter.cs` | Updated `intentHint` XML-doc on `RouteSync` + `RouteAsync` to note the param is retained for interface stability but ignored by the router (task 115 bias is the consumer). |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Updated `intentHint` XML-doc on `CreateAgentAsync` + inline comment at the `RouteAsync` call site. |
| `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | Updated `ChatSendMessageRequest.IntentHint` XML-doc to reflect the new bias-only consumer. |

### BFF tests (C#)

| File | Change |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/CapabilityRouterSoftSlashTests.cs` | **DELETED** — 315 lines / 16 test cases, all testing the deleted Layer 0.5 dict + synthetic-capability constants. |
| `tests/integration/Spe.Integration.Tests/PhaseD/Pillar8ToPlaybookEngineTests.cs` | **DELETED** — 481 lines / 7 test cases, all framed as Pillar 8 → Layer 0.5 cross-pillar verification using the now-removed constants. |

### FE (TypeScript)

| File | Change |
|---|---|
| `src/solutions/SpaarkeAi/src/components/conversation/SoftSlashRouter.ts` | Deleted the `SOFT_SLASH_TO_INTENT` const (4-entry `Record<SoftSlashCommand, CommandIntent>`). Replaced `toCommandIntent()` lookup with a structural derivation (`intent.command.slice(1) as CommandIntent`) — valid because every `SoftSlashCommand` value is `'/' + <CommandIntent>` by construction (closed Q6 vocabulary). `SoftSlashIntents` enumerable export retained for `/help` UI + telemetry audits. Updated `DecoratedChatBody.intentHint` XML doc to reference the new bias-side consumer. |

### Existing tests (unchanged + green)

| File | Note |
|---|---|
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/SoftSlashRouter.test.ts` | 38 tests; all pass (tests `decorateBody` + `toCommandIntent` behavior, not the dict literal). |
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/natural-language-regression.test.ts` | Included in the 62 tests below; passes. |
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/composition.integration.test.ts` | Included in the 62 tests below; passes. |

---

## Build + Test Evidence

### BFF build (Release)

```
dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release
→ Build succeeded. 0 Error(s). 17 Warning(s) — all pre-existing, none introduced by this task.
```

### BFF unit tests

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/
→ Passed!  - Failed: 0, Passed: 7872, Skipped: 137, Total: 8009, Duration: 1m 13s
```

(Pre-task baseline included 7872 passing; deletion of `CapabilityRouterSoftSlashTests.cs`
removed 16 cases, so the prior count was 7888. New count 7872 = 7888 − 16 ✓.)

### Integration test project build

```
dotnet build tests/integration/Spe.Integration.Tests/
→ Build succeeded. 0 Error(s). (Pillar8ToPlaybookEngineTests.cs deletion did not break compilation.)
```

### FE build

```
cd src/solutions/SpaarkeAi
npm install --legacy-peer-deps --no-audit --no-fund   (up to date)
npm run build                                          (vite v5.4.21 — built in 17.92s ✓)
```

### FE focused tests

```
npm test -- --testPathPatterns="SoftSlashRouter"
→ Test Suites: 1 passed, 1 total
→ Tests: 38 passed, 38 total

npm test -- --testPathPatterns="natural-language-regression|composition.integration"
→ Test Suites: 2 passed, 2 total
→ Tests: 62 passed, 62 total
```

Wider conversation-area test run (`--testPathPatterns="conversation"`) reported
**289/289 tests passing** across 10 passing suites. The 6 failing suites are
**pre-existing** `@fluentui/react-components` resolution errors in
`insights/__tests__/*` files (LowConfidenceBadge, InsightsResponseRenderer, etc.)
that fail before any test runs and are completely unrelated to this task's
changes. They reproduce on master.

---

## Publish-Size Delta

| Measurement | Compressed (zip) |
|---|---|
| Pre-task baseline (task 115, 2026-06-24) | **46.31 MB** |
| Post-task 116 (this commit) | **46.31 MB** |
| Δ | **0.00 MB** (no measurable delta — expected for ~80 lines of pure-C# code removal; well-within zip compression noise) |

NFR-01 ceiling: ≤60 MB. Current headroom: ~13.69 MB. ✅ within budget.

ADR-029 verification: BFF publish-size pinned at 46.31 MB (identical to task 115).
No measurable regression — net effect is a small reduction in IL plus a tiny
increase in XML-doc strings; the two roughly cancel under deflate compression.

---

## Grep Evidence (FR-20 binding)

```
Grep "SoftSlashIntentToCapabilityName" src/
→ 1 hit:  src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs:105
         (historical-context comment naming the now-removed dict)

Grep "SoftSlashIntentToCapabilityName" tests/
→ 0 hits

Grep "SOFT_SLASH_TO_INTENT" src/
→ 2 hits: src/solutions/SpaarkeAi/src/components/conversation/SoftSlashRouter.ts:140,179
         (historical-context comments naming the now-removed FE map)

Grep "SOFT_SLASH_TO_INTENT" tests/
→ 0 hits

Grep "SoftSlashSummarizeCapabilityName|SoftSlashDraftCapabilityName|SoftSlashExtractEntitiesCapabilityName|SoftSlashAnalyzeCapabilityName|SoftSlashDecisionConfident|TryClassifySoftSlash" src/ tests/
→ 0 hits
```

All dict / constant / method LITERALS are removed from production code AND
tests. Per POML guidance, the 3 remaining grep hits are intentional comments
documenting the removal for future readers.

---

## Architectural Notes

### What was preserved

- **`intentHint` wire field**: still emitted by `SoftSlashRouter.decorateBody()`,
  still received by `ChatSendMessageRequest.IntentHint`, still threaded through
  `CapabilityRouter.RouteAsync()` for interface stability, and — critically —
  still consumed by `PlaybookDispatcher.DispatchAsync()` → `RunPhaseBVectorMatchAsync()`
  as the `"Intent: {hint} | "` query-prefix bias (task 115's binding semantics).
- **`SoftSlashIntents` runtime list** (FE): retained for `/help` UI + telemetry
  audits (4-entry closed-vocabulary export).
- **`CommandIntent` TypeScript type**: closed-vocabulary union retained (per
  task 022 decision — the wire field renamed, the value-shape type kept).
- **`CapabilityRouter` itself**: only the Layer 0.5 sub-layer was removed.
  Full router retirement is task 141 (Phase 7).

### Special-case callers handled

None. The dict had exactly ONE consumer (`TryClassifySoftSlash` inside
`CapabilityRouter.cs`); deleting the method eliminated all reads. No
intermediate refactoring was needed.

### FR-20 acceptance verified

> "`SoftSlashIntentToCapabilityName` dict removed; slash + NL flows produce
> identical routing for same query text."

- ✅ Dict removed (BE + FE structural equivalent).
- ✅ Slash + NL converge: both feed `intentHint` (slash → set; NL → null) into
  the same `PlaybookDispatcher.DispatchAsync()` Phase B path. The bias-prefix
  derived from `intentHint` (when set) shifts the embedding semantics; the
  routing destination is otherwise identical.

---

## Coordination Notes

- Task 117a (parallel) modifies `Models/Ai/Chat/SseEvents/`,
  `Services/Ai/Chat/PlaybookOptionsEventBuilder.cs`, and `Infrastructure/DI/AiChatModule.cs`
  — completely disjoint from this task's file set. No conflict expected.
- This task's commit is atomic FE+BE (single commit) per the POML constraint.

---

## Follow-Ups for Main Session

- Phase 7 task 141 will delete the rest of `CapabilityRouter` + 10 supporting
  files. After 141, the `intentHint` parameter on `RouteAsync`/`RouteSync` will
  also disappear (along with the entire router); for now the parameter remains
  as a no-op pass-through to maintain `SprkChatAgentFactory` ABI stability
  during Phase 5R completion.
- No new open items.
