# Task 031 — Layer-A Action Seam Extraction: Notes

> **Status**: ✅ Completed 2026-07-21. Session-agnostic Layer-A seam extracted behind the three node executors; behavior-neutral (proven), both quality gates clean.

## What shipped

| Artifact | Purpose |
|---|---|
| `Services/Ai/Nodes/ActionCore/NotificationActionCore.cs` | Idempotency + `BuildNotificationEntity` (verbatim; `context.RunId`→`CorrelationId`, `"playbook"`→`Source` params) + create. |
| `Services/Ai/Nodes/ActionCore/TaskActionCore.cs` | `task` entity build + degraded-success (`Guid.Empty`) create. |
| `Services/Ai/Nodes/ActionCore/UpdateRecordActionCore.cs` | Coercion (typed + metadata-driven fail-loud) + PATCH; `FieldCoercionException` moved here. |
| `Services/Ai/PublicContracts/IActionSeam.cs` + `ActionSeam.cs` | ADR-013 facade — `CreateNotificationAsync`/`CreateTaskAsync`/`UpdateRecordAsync` over typed request records; typed failure results (no throws for expected validation). |
| `Infrastructure/DI/AnalysisServicesModule.cs` (`AddNodeExecutors`) | `AddSingleton<IActionSeam, ActionSeam>()` — unconditional (record-create is not feature-gated → no null-object). |
| 3 executors (refactored) | Render/parse (session-specific) → delegate build+create to the shared core. Constructors, `Validate`, `SupportedExecutorTypes`, `GetConfigSchema`, public `ExecuteAsync` UNCHANGED. |
| `tests/integration/seam/Ai/PublicContracts/ActionSeamTests.cs` (8 tests) | Proves each seam method works with **no** `NodeExecutionContext`/playbook/chat; asserts byte-parity with the executor writes + the negative cases (no-recipient, fail-loud Choice). |

## Acceptance — all 8 criteria met

1. ✅ Seam methods produce the same Dataverse write as the executors (ActionSeamTests parity, no NodeExecutionContext).
2. ✅ Task-030 characterization tests pass **zero-edit**.
3. ✅ `CreateTaskNodeExecutorTests` + `UpdateRecordNodeExecutorTests` pass **zero-edit** (incl. exact ctor calls).
4. ✅ Executor constructor signatures **byte-identical** (verified HEAD vs current).
5. ✅ `IActionSeam` only under `PublicContracts/`; sole non-chat/non-playbook Layer-A entry point.
6. ✅ `DispatchSessionEndpoint.cs` / `OutputRouter.cs` / `DispositionRoutability.cs` **unchanged** (git diff empty) — those are task 033's scope.
7. ✅ Negative cases return typed failure (no-recipient → `"recipientId is required"`; unmatchable Choice → typed error, no PATCH) — never a throw/no-op.
8. ✅ Publish **46.05 MB compressed** ≤60 MB (delta ~0, no packages); **0 new HIGH CVE**; Placement Justification stated.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api` (Release): 0 errors.
- Node-executor + ActionSeam tests: **272 pass / 0 fail** (1 pre-existing skip); characterization + unit tests zero-edit.
- **Full BFF suite: 8,780 pass / 0 fail** (101 pre-existing skips) — validates behavior neutrality across the whole system, incl. the real DI container (`WebApplicationFactory<Program>`), which also confirms `ActionSeam` (Singleton, all-Singleton deps) has no captive dependency.
- Step 9.5: code-review CLEAN (0 Critical/Warning); adr-check 0 violations (1 justified ADR-010 note — `IActionSeam` is the ADR-013 facade seam, matching the `IBriefingAi` precedent).

## Design decisions

- **Cores are constructed inline** by the executors (from their existing injected fields) and by `ActionSeam` (from its own) — NOT injected into the executors, so the frozen-constructor acceptance criterion holds without duplicating logic.
- **Session/agnostic split**: template rendering + `ConfigJson` parsing + `NodeOutput` wrapping stay in the executors; entity build + idempotency + coercion + create moved to the cores. `ResolveViaMatterMemberships` (reads `PreviousOutputs`) stays in the executor.
- **`sprk_source` / `sprk_playbookrunid`** became explicit `Source`/`CorrelationId` params; the executors pass `"playbook"` / `RunId` to preserve today's exact field values (parity), and Phase 4/5 callers pass their own.
- **Escalation triggers did NOT fire**: no characterization test needed editing; the exact executor constructor signatures were preserved WITHOUT logic duplication (the inline-core-construction pattern resolves the extend-vs-fork tension cleanly).

## For task 033 (next)

033 flips `Notification` `Routable=false`→true in `DispositionRoutability.cs` + adds the matching `OutputRouter` switch leg (ADR-043 Path C — both in the same change). This task deliberately left those two files untouched (criterion 6). The `IActionSeam` is available for the Phase-4/5 producers that 033's routing will eventually feed, but 033 itself is the dispatch/router change, not a producer.
