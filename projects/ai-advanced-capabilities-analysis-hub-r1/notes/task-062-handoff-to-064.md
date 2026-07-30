# Task 062 → Task 064 Hand-off: sprk_chathistory Write + Column Plumbing

> **From**: task 062 (retire legacy session path) · **To**: task 064 (migrate `/save`+`GET /{id}`, drop chathistory)
> **Date**: 2026-07-29
> **Reason**: escalation-guarded provenance rule in task 062's POML — do not force deletion while a
> live non-legacy reader exists.

## What task 062 removed

- `AnalysisEndpoints.cs` — `POST /{analysisId}/continue` + `POST /{analysisId}/resume` route
  registrations and their `ContinueAnalysis` / `ResumeAnalysis` handlers.
- `AnalysisOrchestrationService.cs` — `ContinueAnalysisAsync`, `ResumeAnalysisAsync`, and the
  now-orphaned `EstimateTokens` helper.
- `IAnalysisOrchestrationService.cs` — both interface declarations.
- `AnalysisDocumentLoader.cs` — `ReloadAnalysisFromDataverseAsync` (the "full" loader; its only
  caller was `ContinueAnalysisAsync`).
- `AnalysisResultPersistence.cs` — the `UpdateChatHistoryAsync` wrapper (its only caller was
  `ContinueAnalysisAsync`; confirmed orphaned by repo-wide grep before deletion).
- `Models/Ai/AnalysisResumeRequest.cs` + `AnalysisContinueRequest.cs` — deleted files, no other
  consumers.

## What task 062 deliberately left untouched (hand-trace proved they are still live)

1. **`AnalysisDocumentLoader.GetOrReloadFromDataverseAsync`** (the "lite" loader) + its call to
   **`DeserializeChatHistory`** — used by `SaveWorkingDocumentAsync`, `ExportAnalysisAsync`, and
   `GetAnalysisAsync` (i.e. `POST /{id}/save`, `POST /{id}/export`, `GET /{id}`). These are the
   endpoints task 064 owns migrating/dropping.
2. **`AnalysisResultPersistence`** class itself (still used for `UpdateWorkingDocumentAsync`,
   `FinalizeAnalysisAsync`, `SaveToSpeAsync`, export telemetry — only the chat-history wrapper was
   removed).
3. **`IWorkingDocumentService.UpdateChatHistoryAsync` / `WorkingDocumentService.UpdateChatHistoryAsync`**
   (the actual Dataverse write) — called **directly** by `ChatEndpoints.cs` (~line 981,
   `workingDocumentService.UpdateChatHistoryAsync(analysisGuid, chatHistoryJson, ...)`) for the live
   new-session per-turn write when a chat session is scoped to an analysis record
   (`session.HostContext.EntityType == "sprk_analysisoutput"`). This is NOT the legacy path — it is
   the current production write.
4. **`sprk_chathistory` column plumbing** in `Spaarke.Dataverse`:
   - `DataverseServiceClientImpl.cs` (~161 `ColumnSet(...)`, ~174 `GetAttributeValue<string>("sprk_chathistory")`)
   - `DataverseWebApiService.cs` (~258 `$select=...sprk_chathistory...`, ~284 parse into `ChatHistory`)
   - `Models.cs` — the `ChatHistory` property that carries the parsed value.

   All three feed `IAnalysisDataverseService.GetAnalysisAsync(...)`, which
   `GetOrReloadFromDataverseAsync` calls — i.e. this plumbing is what `/save`, `/export`, and `GET`
   currently read through. It is live, not orphaned.

## What task 064 needs to do

Per task 064's own scope ("migrate `/save`+`GET /{id}` (drop chathistory)"):

1. Confirm `/save` and `/export` don't actually need `ChatHistory` in their response/behavior (they
   likely only need it incidentally via the shared "lite" load) — if so, drop the chathistory read
   from those call sites, or from `GetOrReloadFromDataverseAsync` itself if all three consumers
   (`/save`, `/export`, `GET`) are confirmed to no longer need it.
2. **Re-run the same hand-trace task 062 used** (grep `sprk_chathistory` repo-wide, classify every
   hit LEGACY vs NON-LEGACY) AFTER your read-side changes land, to confirm:
   - No caller of `GetOrReloadFromDataverseAsync`/`DeserializeChatHistory` still needs the field.
   - `ChatEndpoints.cs`'s write (~981) is either (a) still needed because some other reader survives,
     or (b) now provably dead too and can be removed alongside the column plumbing.
3. Only THEN delete, in this order: `IWorkingDocumentService.UpdateChatHistoryAsync` /
   `WorkingDocumentService.UpdateChatHistoryAsync` impl → the `ChatEndpoints.cs` ~972-981 call site →
   the `sprk_chathistory` column plumbing in `DataverseServiceClientImpl.cs` /
   `DataverseWebApiService.cs` / `Models.cs`.
4. **Do NOT touch** `Services/Insights/Observations/ObservationMirrorMapper.cs` — it writes
   `sprk_chathistory` on a **different entity** (Insights observation mirror), unrelated to
   `sprk_analysis.sprk_chathistory`.

## Verification anchors (negative-check, still valid at 062 close)

- `ChatEndpoints.cs` ~972-981 (new-session `sprk_chathistory` write) — present, unchanged, compiles.
- `Services/Insights/Observations/ObservationMirrorMapper.cs` — present, unchanged, compiles (writes
  a different entity's `sprk_chathistory` column — out of scope for both 062 and 064).
