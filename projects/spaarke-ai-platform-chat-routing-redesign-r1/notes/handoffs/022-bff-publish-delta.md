# Task 022 — BFF Publish Delta

> **Generated**: 2026-06-22
> **Task**: 022 — Rename chat-request wire-format field `commandIntent` → `intentHint` (FR-07 / Q5)
> **Wave**: 1-G (FE + BE atomic rename, single agent)
> **Phase 0 baseline**: 44.75 MB compressed
> **Post-Wave 1-F baseline (per `020-bff-publish-delta.md`)**: 44.75 MB compressed

---

## Measurement

| Metric | Value |
|---|---|
| Source | `deploy/api-publish-022/` |
| Compressed archive | `c:/tmp/api-publish-022.zip` |
| Raw bytes | **46,929,510** |
| MB (1024²) | **44.75 MB** |
| Δ vs Phase 0 baseline | **0.00 MB** |
| Δ vs prior wave (post-021) | **0.00 MB** |
| NFR-01 ceiling | 60 MB (HARD STOP) |
| NFR-01 architecture-review trigger | 55 MB |
| NFR-01 escalation trigger | +5 MB single-task delta |

**Verdict**: ✅ **WITHIN BUDGET**. Zero-delta task as expected — this is a wire-format rename with no new dependencies, no new DI registrations, no new endpoints, no new packages.

---

## Why zero delta

This task is a pure rename:
- C# property `ChatSendMessageRequest.CommandIntent` → `IntentHint`
- Optional parameter `commandIntent` → `intentHint` on `ICapabilityRouter.RouteSync` / `RouteAsync`, `SprkChatAgentFactory.CreateAgentAsync`, `NullSprkChatAgentFactory.CreateAgentAsync`, `CapabilityRouter.TryClassifySoftSlash`
- All dictionary / capability-name infrastructure unchanged
- No NuGet additions
- No new public-contract surface in `Services/Ai/PublicContracts/`
- No new DI registrations
- No new endpoints

Property + parameter renames are token-level edits; the compiled IL identifier rename does not alter linker-pruned output size (camelCase property name lives in metadata only and is JSON-serializer-visible; the linker output is identical mod identifier strings).

---

## Cumulative project trajectory

| Wave | Compressed | Δ vs prior |
|---|---|---|
| Phase 0 baseline | 44.75 MB | — |
| 010 | 44.75 MB | 0.00 |
| 011 | 44.75 MB | 0.00 |
| 012 | 44.75 MB | 0.00 |
| 013 | 44.75 MB | 0.00 |
| 015 | 44.75 MB | 0.00 |
| 016 | 44.75 MB | 0.00 |
| 017 | 44.75 MB | 0.00 |
| 018 | 44.75 MB | 0.00 |
| 019 | 44.75 MB | 0.00 |
| 020 | 44.75 MB | 0.00 |
| **022** | **44.75 MB** | **0.00** |

Headroom vs NFR-01 ceiling: **60.00 − 44.75 = 15.25 MB** (25.4% of budget remains).

---

## Notes

- Task POML scope: ATOMIC FE + BE rename, no behavior change, no back-compat alias (per FR-07 / Q5).
- Phase 5 task 115 (vector-query bias semantics) will land non-zero delta if it adds embeddings or retrieval clients; THIS task adds nothing.
- Owner-confirmed new name `intentHint` recorded in `022-commandintent-rename-decision.md`.
