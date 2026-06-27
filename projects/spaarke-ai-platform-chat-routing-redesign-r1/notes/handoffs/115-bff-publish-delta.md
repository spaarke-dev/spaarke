# Task 115 — BFF Publish-Size Delta

> **Date**: 2026-06-25
> **Task**: 115 — Integrate `intentHint` (slash-bias signal) as a vector-query bias in Phase B query composition (FR-20)
> **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
> **Rigor**: FULL (tags include `bff-api` + `services` + `ai`; NFR-01 / ADR-029 binding)

## Measurement

| Field | Value |
|---|---|
| **Post-task-113R baseline** | 47.89 MB compressed (50,211,177 bytes) |
| **Post-task-115 (this task — `intentHint` parameter + query bias + telemetry)** | **47.91 MB** compressed (50,232,852 bytes) |
| **Delta vs task-113R baseline** | **+0.02 MB** (+21,675 bytes) |
| **Single-task escalation threshold (+5 MB)** | NOT exceeded — 4.98 MB headroom |
| **NFR-01 architecture-review threshold (55 MB)** | NOT approached — 7.09 MB headroom |
| **NFR-01 60 MB hard ceiling** | NOT approached — 12.09 MB headroom |

## Methodology

```
dotnet publish src/server/api/Sprk.Bff.Api/ -c Release -o deploy/api-publish
tar -czf deploy/api-publish-115.tar.gz -C deploy/api-publish .
ls -la deploy/api-publish-115.tar.gz
```

## Delta analysis

Task 115 source changes (additive, no new files in production):

- `Services/Ai/Chat/PlaybookDispatcher.cs` — +1 optional parameter `intentHint` on `DispatchAsync` (no body change beyond ADR-015 plumbing). +1 optional parameter `intentHint` on `RunPhaseBVectorMatchAsync`. Per-file query composition prefix `"Intent: {intentHint} | "` applied on both manifest-present and manifest-absent paths when hint is non-null/non-whitespace. Cache key for manifest-present path now includes intent segment so intent shifts bust cache. Manifest-absent cache key unchanged in structure — intent is part of the hashed query body. Telemetry log line carries new `intentHintProvided` boolean (ADR-015 tier-1: provided flag only, not value).
- `Api/Ai/ChatEndpoints.cs` — +1 named arg `intentHint: request.IntentHint` on the `dispatcher.DispatchAsync(...)` call; +3 lines of comments.

Test-only additions (excluded from publish):

- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookDispatcherIntentBiasTests.cs` — NEW (~430 lines, 12 tests covering FR-20 + ADR-014 cache busting + ADR-015 tier-1 telemetry).

The +0.02 MB delta is consistent with a thin parameter-and-string-formatting addition (no new DLLs, no new DI registrations, no new packages).

## NFR-01 status

- **Within all binding thresholds** — well under 60 MB hard ceiling, under 55 MB architecture-review threshold, and well below the +5 MB single-task escalation line.
- **No publish-size escalation required**. Task 115 is bias-side logic only; cumulative growth stays within budget.

## Constraint compliance

- **ADR-013 (AI facade)**: changes stay inside `Services/Ai/Chat/` — no boundary crossings.
- **ADR-014 (5-min TTL cache)**: cache key includes intent hint so bias shifts bust the cache cleanly. Verified by `RunPhaseBVectorMatchAsync_CacheBustsOnIntentChange_*` tests.
- **ADR-015 (tier-1 logging)**: only `intentHintProvided` boolean is logged; the hint value never leaves the process via telemetry. Verified by `RunPhaseBVectorMatchAsync_LogsIntentHintProvidedFlag_NotValue` (sentinel-marker test).
- **ADR-029 (publish hygiene)**: +0.02 MB delta well within budget.
- **FR-20**: bias applied in query composition, NOT a separate routing layer or dict lookup. Verified by `RunPhaseBVectorMatchAsync_BiasObservable_WhenSameMessageDifferentIntent` (FR-20 binding test — same message + different intent produces different embed input).
- **FR-21**: embedding model unchanged (`text-embedding-3-large`).
- **Backward compat**: null/empty/whitespace `intentHint` produces identical behaviour to pre-task-115 task-112 path (verified by `RunPhaseBVectorMatchAsync_NoBias_When*` tests; all 11 pre-existing PhaseB tests still pass).

## Test outcome

- 11 pre-existing `PlaybookDispatcherPhaseBTests` — PASS (zero regressions; no signature breakage for the existing tests since `intentHint` defaults to null).
- 12 new `PlaybookDispatcherIntentBiasTests` — PASS.
- 17 pre-existing `PlaybookDispatcherAttachmentsTests` / `PlaybookDispatcherDestinationTests` / `PlaybookDispatcherIntegrationTests` — PASS.
- 28 pre-existing `ChatEndpointsTests` — PASS (no regression from new named-arg call-site change).
- **Total**: 40 PlaybookDispatcher tests pass, 28 ChatEndpoints tests pass.

## Build

- **Errors**: 0
- **Warnings**: 17 (matches pre-task-115 baseline — no new warnings introduced).
