# UAT Hotfix Post-Mortem: /api/ai/daily-briefing/narrate 503 IndexOutOfRangeException

**Date**: 2026-06-26
**Environment**: spaarkedev1 (spaarke-bff-dev)
**Severity**: P1 — Daily Briefing widget unusable in UAT
**Detected via**: Browser console + Azure App Service container logs

---

## Symptom

`POST /api/ai/daily-briefing/narrate` returned HTTP 503 with body `Index was outside the bounds of the array.` for every non-empty narrate request. The widget surfaced this as `[DailyBriefing] AI narration fetch failed: ApiError`.

---

## Root Cause

**Confirmed from log line 808–813 in `2026_06_26_ln0sdlwk003HF5_success.log`** (UTC 17:34:55):

```
fail: Sprk.Bff.Api.Services.Ai.PlaybookOrchestrationService[0]
      Playbook execution failed - RunId: d68b8ef7-655f-405e-8e77-254458fb4dee
      System.IndexOutOfRangeException: Index was outside the bounds of the array.
         at Sprk.Bff.Api.Services.Ai.AnalysisOrchestrationService.ExecutePlaybookAsync(...)+MoveNext()
         at Sprk.Bff.Api.Services.Ai.PlaybookOrchestrationService.ExecuteLegacyModeAsync(...) at line 597
         at Sprk.Bff.Api.Services.Ai.PlaybookOrchestrationService.ExecuteInternalAsync(...) at line 253
```

Preceded by log line 805: `Playbook 7b5a6ed3-... has no nodes - using Legacy mode`.

### The chain (3 hops)

1. `/narrate` endpoint (task 031 / Path A.5) dispatches via `IInvokePlaybookAi.InvokePlaybookAsync(playbookId, parameters, …)`. The facade hardcodes `DocumentIds = Array.Empty<Guid>()` (`InvokePlaybookAi.cs:71`).
2. `PlaybookOrchestrationService.ExecuteInternalAsync` loads playbook nodes. **The `DAILY-BRIEFING-NARRATE` playbook deployed in spaarkedev1 has zero `sprk_playbooknode` rows** (only the metadata header), so the service drops into Legacy mode.
3. Legacy mode delegates to `AnalysisOrchestrationService.ExecutePlaybookAsync`, whose **very first line was** `var documentId = request.DocumentIds[0]` — crashes immediately on the empty array.

### Why task 030's "IInvokePlaybookAi handles empty documentIds" claim was wrong

Task 030 read the comment at `InvokePlaybookAi.cs:67–72`:
> "The orchestration service interprets an empty documentIds array as 'no document context' (consistent with the existing M365 Copilot adapter path)."

That claim is only true when the playbook has nodes (node-based execution path). For legacy-mode playbooks — which the orchestrator silently falls back to when no nodes are found — the path crashes. The 030 decision was based on **code-comment interpretation, not empirical smoke**. The agent did not run a single test invocation through the chain.

---

## Fix Applied

### `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs:702–728`

Added an empty-`DocumentIds` guard at the entry of `ExecutePlaybookAsync` that emits a clean error chunk instead of crashing. The error chunk propagates through `PlaybookOrchestrationService.ExecuteLegacyModeAsync` (case `"error"` at line 622) → `PlaybookStreamEvent.RunFailed` → `IInvokePlaybookAi` aggregates as `Success=false, ErrorCode=PLAYBOOK_INVOCATION_FAILED, ErrorMessage="…"` → `HandleNarrate` returns `503 ProblemDetails` via `ProblemDetailsHelper.AiUnavailable`.

```csharp
if (request.DocumentIds is null || request.DocumentIds.Length == 0)
{
    _logger.LogError(
        "Legacy-mode playbook execution requires at least one DocumentId. Playbook {PlaybookId} likely has no nodes — node-based execution is required for non-document dispatch (e.g. daily-briefing narrate).",
        request.PlaybookId);
    yield return AnalysisStreamChunk.FromError(
        $"Playbook {request.PlaybookId} cannot run in legacy mode without a document. Configure nodes in the Playbook Builder to enable non-document dispatch.");
    yield break;
}
```

The fix preserves the `/narrate` response shape (AC-12b binding) — empty-payload tolerance branch still returns 200, fix is on the dispatch failure path.

### `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs`

Added regression test `ExecutePlaybookAsync_EmptyDocumentIds_YieldsErrorChunk_Hotfix_2026_06_26` that asserts:
- One error chunk yielded (no crash)
- `chunk.Type == "error"`, `chunk.Done == true`, `chunk.Error` contains "legacy mode" + "nodes"
- `IPlaybookService.GetPlaybookAsync` NOT called (fail-fast before any Dataverse round-trip)

All 28 `AnalysisOrchestrationServiceTests` + `DailyBriefing*Tests` pass; 3 pre-existing skips unchanged.

---

## What this hotfix does NOT solve

The fix prevents the IndexOutOfRangeException → 500/503 crash. The daily-briefing **widget still cannot get a real narration** because the `DAILY-BRIEFING-NARRATE` playbook in spaarkedev1 has no node graph deployed. With the hotfix, the user gets a meaningful 503 ("playbook unconfigured — configure nodes") instead of an opaque "Index was outside the bounds of the array."

The follow-up work (out of scope for this hotfix, tracked separately):
- Deploy the `DAILY-BRIEFING-NARRATE` playbook node graph (Start → LoadKnowledge → [GenerateTldr ‖ GenerateChannelNarratives] → ValidateEntityNames → ReturnResponse) per task 010 design.
- Re-run smoke check to confirm node-based execution succeeds end-to-end.

---

## Timeline

| Time (UTC) | Event |
|---|---|
| 2026-06-25 18:44 | First IndexOutOfRangeException in production (pre-merge) — log line 5783 |
| 2026-06-26 17:34–18:18 | UAT repro confirmed 5x; stack trace captured from container logs |
| 2026-06-26 (this commit) | Fix applied + test added + post-mortem written |

---

## Files Changed

- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` — empty-DocumentIds guard
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` — regression test
- `projects/spaarke-daily-update-service-r4/notes/uat/narrate-503-hotfix.md` (this file)
- `projects/spaarke-daily-update-service-r4/notes/lessons-learned.md` — added "empirical-smoke-before-shipping" entry
