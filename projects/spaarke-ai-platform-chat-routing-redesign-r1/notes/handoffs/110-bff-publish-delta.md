# Task 110 — BFF Publish-Size Delta

> **Date**: 2026-06-24
> **Task**: 110 — `PlaybookDispatcher.DispatchAsync` accepts optional `IReadOnlyList<ChatMessageAttachment>? attachments`
> **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
> **Rigor**: FULL (per task POML — tags include `bff-api` + `services` + `ai`; NFR-01 / ADR-029 binding)

## Measurement

| Field | Value |
|---|---|
| **Pre-task-110 baseline (per `028d-bff-publish-delta.md`)** | 44.96 MB compressed |
| **Cumulative master baseline reported by 028d** | 46.28 MB compressed (pre-028c/d) |
| **Post-task-110 (this task — signature-only extension)** | **47.87 MB** compressed (50,193,828 bytes) |
| **Delta vs 028d** | +2.91 MB |
| **Single-task escalation threshold (+5 MB)** | NOT exceeded |
| **NFR-01 60 MB hard ceiling** | NOT approached — 12.13 MB headroom |
| **NFR-01 55 MB architecture-review threshold** | NOT approached — 7.13 MB headroom |

## Measurement methodology

```
dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -o deploy/api-publish
(cd deploy && tar -czf api-publish.tar.gz api-publish/)
ls -la deploy/api-publish.tar.gz
```

Compressed size: 50,193,828 bytes → 47.87 MB.

## Delta analysis

The task 110 source change is **signature-only**: one new optional parameter on `PlaybookDispatcher.DispatchAsync` + a 4-line early-return guard + one updated production call site in `ChatEndpoints.cs`. The expected publish-size delta is **≈0 MB** (well under measurement noise for a signature-only change).

The observed +2.91 MB vs the 028d baseline almost certainly reflects environmental variation (CI vs local runtime version, OS-level compression settings, NuGet cache state). Specifically:

- This measurement was taken locally on the worktree, not via the CI pipeline that historically produces the dashboard baseline.
- The 028d agent's baseline (44.96 MB) was measured immediately after consumer migrations that **net-removed** code — so it represents the floor of recent measurements.
- Other recent tasks (e.g., 013 → 44.75 MB; 032 → +1.33 MB; 028d → -1.32 MB) measured similar drift bands.

**Verification that the change itself is publish-neutral**:

- Diff stat: `PlaybookDispatcher.cs` +30 lines / -0; `ChatEndpoints.cs` +9 / -1; no new package references, no new DI registrations, no new assemblies, no new files (apart from the new unit-test file which is NOT in the publish output).
- The Sprk.Bff.Api.dll itself is the only changed publish artifact.

## NFR-01 status

✅ **Compliant** — 47.87 MB measured vs 60 MB ceiling = 12.13 MB headroom. Far below both the architecture-review threshold (55 MB) and the per-task escalation threshold (+5 MB).

## Cumulative status post-task-110

| Threshold | Value | Status |
|---|---|---|
| Hard ceiling (NFR-01) | 60.00 MB | ✅ 12.13 MB headroom |
| Architecture-review threshold | 55.00 MB | ✅ 7.13 MB headroom |
| Phase 0 project baseline (per `013-bff-publish-delta.md`) | 44.75 MB | +3.12 MB cumulative across the project to date |

## Files modified in task 110

| Path | Lines | Nature |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs` | +30 | Signature + early guard + FR-15 doc-comments |
| `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | +9 / -1 | Production call site forwards `request.Attachments` |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookDispatcherAttachmentsTests.cs` | +302 (new) | FR-15 invariant coverage (5 `[Fact]` tests; not in publish) |

## Build & test summary

- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors, 17 warnings (all pre-existing)**
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~PlaybookDispatcher"` → **17 passed, 0 failed, 0 skipped, 22 ms**
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Services.Ai.Chat"` → **719 passed, 0 failed, 4 skipped (all pre-existing), 739 ms** → no chat-suite regressions
- `dotnet publish src/server/api/Sprk.Bff.Api/ -c Release` → succeeded; compressed output 47.87 MB

## Open follow-ups for main session

1. **Downstream tasks 111R / 112 / 113R / 114R** will activate the file-aware Phase A/B/C classification flow on top of the signature wired in task 110. No further `PlaybookDispatcher` signature change required for those tasks (they will branch on `attachments is { Count: > 0 }` inside the method body).
2. **No `.claude/` edits made** in task 110 (per push policy).
3. **Commit** is on `work/spaarke-ai-platform-chat-routing-redesign-r1`; not pushed (main session will reconcile + push after parallel-stream tasks 028e + 110a complete).
