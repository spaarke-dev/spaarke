# Task 058 evidence — Conflict resolution implementation (D-C-11 / Q8 USER WINS)

**Pillar / Spec ref**: R6 Pillar 6b / FR-40 — Q8 conflict resolution wired end-to-end.
**Wave**: C-G3 gap-fill.
**Date**: 2026-06-11.
**Dependencies**: task 055 (UpdateWorkspaceTabHandler stale_read refusal path); task 053 (workspace state snapshot in chat factory system prompt).

## What was already on disk

Per the C-G3 checkpoint, `UpdateWorkspaceTabHandler.cs` had only the
`using System.Diagnostics.Metrics;` import added — no actual counter
emission code. Other 058 outputs (persona snippet, integration test) were
entirely missing.

## What this task added

### 1. Telemetry counter `workspace.conflict_refused`

In `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/UpdateWorkspaceTabHandler.cs`:

- A static `Meter` field `_meter` with name `Sprk.Bff.Api.Workspace` (following
  the existing `Sprk.Bff.Api.*` Meter-naming convention used by every other
  Telemetry/*.cs file).
- A static `Counter<long> _conflictRefusedCounter` named
  `workspace.conflict_refused` with unit `{refusal}`.
- The counter increment is added at the stale-read refusal site (immediately
  inside the `if (conflictDecision.IsStale)` branch, BEFORE the
  `return ToolResult.Ok(...)` that carries the structured `stale_read`
  payload).

Counter dimensions emitted (per ADR-015 BINDING — deterministic IDs only):

- `tenantId` — opaque tenant identifier (no PII)
- `sessionId` — the chat session GUID rendered as `N` format
- `tabId` — the LLM-supplied tab identifier (echo of the tool argument)
- `decision` — set to the `StatusStaleRead` constant ("stale_read")

**ADR-015 audit at the emission site**: the payload construction does NOT
include `args.WidgetDataRawJson` (the LLM-supplied widget body), does NOT
include `current.WidgetData` (the existing widget body), does NOT include
`context.ToolArgumentsJson` (raw JSON), does NOT include any user message
text. Visual code review of the emission site at lines 380-385 of the
modified handler confirms only the four deterministic identifiers above.

Counter is INTRINSICALLY confined to the stale-read branch — the
applied / refused_not_found / refused_not_editable / refused_kind_mismatch
paths each have their own `_logger.Log*` calls but do NOT call
`_conflictRefusedCounter.Add`. Asserted empirically by the integration
test's "applied path must NOT increment workspace.conflict_refused"
assertion.

### 2. Persona seed-script snippet

`scripts/Seed-AiPersonaDefault.ps1` — the verbatim system-prompt text
inside the script's here-string (`$SystemPrompt = @'...'@`) was extended
with a new top-level Markdown section titled "Workspace Tab Conflict
Resolution" carrying the instruction:

> If a workspace tab update (update_workspace_tab) refuses with status
> "stale_read", the user edited the tab since your last read. Re-read the
> tab from the current workspace state in your next turn before
> re-attempting the update. User edits always win.

The script's drift-detection logic (PATCH on drift) will deploy this
addition idempotently on next `Seed-AiPersonaDefault.ps1` run. The script
itself was NOT executed in this task (per the brief: "the user will deploy
separately — DO NOT call `pac` or `mcp__dataverse__*` tools").

### 3. Integration test

`tests/integration/Spe.Integration.Tests/Workspace/ConflictResolutionTests.cs` — three tests:

- **`StaleReadFollowedByFreshReread_SucceedsOnSecondAttempt`** — the
  end-to-end happy-flow test. User edit at 13:00; agent's stale view says
  11:00. First call returns `ToolResult.Ok` with
  `Status = StatusStaleRead`, persistence is NOT invoked, and the counter
  increments to 1 with the expected tag values. The agent reads the
  refusal payload's `CurrentLastUserEditAt`, uses it as the fresh
  `expectedLastUserEditAt` on a second call, and the second call succeeds
  (`Status = StatusApplied`, persistence invoked exactly once, counter
  stays at 1 — applied path does NOT re-increment).

- **`ConflictCounter_OmitsUserContent_PerAdr015`** — drives a tab whose
  `WidgetData.Body` contains intentionally privileged text
  ("PRIVILEGED LEGAL DRAFT: do NOT share with adverse counsel"), forces a
  stale-read refusal, and asserts (a) the captured tag KEYS are exactly the
  four-element set {`tenantId, sessionId, tabId, decision`}, and (b) NONE
  of the captured tag VALUES contain any substring of the privileged text.
  This is the empirical ADR-015 contract verification.

- **`SeedAiPersonaDefaultScript_CarriesStaleReadInstruction`** — walks up
  the assembly directory chain to find the repo root, reads
  `scripts/Seed-AiPersonaDefault.ps1`, and asserts the three text-content
  contracts: the section heading "Workspace Tab Conflict Resolution"
  exists, "stale_read" appears, and (case-insensitive) "re-read the tab"
  appears. This is the data-side half of the Q8 wiring contract — drift
  here would mean the deployed SYS-DEFAULT persona is missing the contract
  the LLM needs to recognize on a stale_read response.

The test uses a stateful `FakeWorkspaceStateService` (implements all four
`IWorkspaceStateService` members; `GetTabsAsync` returns the current tab,
`UpsertTabAsync` captures the mutation), a `FakeTimeProvider` for
deterministic timestamps, and `MeterListener` to capture the counter
emissions. No WebApplicationFactory bootstrap is needed because the
handler is exercised via its public `ExecuteChatAsync` surface directly —
the integration nature is that we use the REAL handler + real meter
pipeline + real persona-side data contract.

## Build + test status

```
dotnet build src/server/api/Sprk.Bff.Api/ -nologo -v q
  Build succeeded. 0 Error(s), 16 Warning(s).  (warnings pre-existing)

dotnet build tests/integration/Spe.Integration.Tests/ -nologo -v q
  Build succeeded. 0 Error(s), 1 Warning(s).   (warning pre-existing)

dotnet test tests/integration/Spe.Integration.Tests/ --filter "FullyQualifiedName~ConflictResolutionTests"
  Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3.  Duration: 19 ms.

dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~UpdateWorkspaceTabHandlerTests"
  Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5.  Duration: 7 ms.
  (regression check — pre-existing handler unit tests still pass after
   adding the static Meter field)
```

## Governance

- **ADR-013** (AI architecture, facade boundary): persona-side instruction
  is DATA (deployed via Seed-AiPersonaDefault.ps1 row), NOT hardcoded in
  C#. Telemetry counter lives in the handler (Services/Ai/Handlers/) —
  consistent with the existing handler ownership of its own meter; no DI
  change needed.
- **ADR-015** (data governance, BINDING): per-site audit documented above;
  deterministic IDs ONLY in counter tags; ADR-015 anti-leakage assertion
  enforced by the second integration test (substring search on captured
  tag values).
- **ADR-029** (BFF publish hygiene): zero new NuGet dependencies added.
  `System.Diagnostics.Metrics` is BCL. Estimated publish-size delta: 0 MB
  (counter + meter compile to ~1 KB of IL). The BFF builds clean (0 errors,
  16 warnings — all pre-existing).
- **Q8 binding** (USER WINS): stale-read refusal is a `ToolResult.Ok` carrying
  the structured payload (not a `ToolResult.Failure` — refusal is a re-actable
  response per the original task-055 design). Confirmed by both the
  integration test and the existing unit test at lines 190-219 of
  `UpdateWorkspaceTabHandlerTests.cs`.

## Outcome

- ✅ Telemetry counter wired at the stale-read refusal site with ADR-015-compliant
  deterministic-ID tags only.
- ✅ Persona seed script extended with the Q8 conflict-resolution instruction
  snippet (idempotent — script's PATCH-on-drift logic deploys cleanly).
- ✅ End-to-end integration test green: stale → refuse → re-read →
  re-attempt → success.
- ✅ ADR-015 anti-leakage contract empirically verified.
- ✅ Persona-side contract empirically verified (script text-contract test).
- ✅ Pre-existing UpdateWorkspaceTabHandlerTests unit tests still pass.

Q8 conflict-resolution loop is functional end-to-end. Deployment of the
updated `SYS-DEFAULT` persona row is the user's separate step (run
`Seed-AiPersonaDefault.ps1` against the target Dataverse environment).
