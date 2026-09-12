# Task 022 — Generate Profile button and BFF trigger (FR-08)

## Summary

Added a "Generate Profile" control to the Save tab's Profile section (task 021) and the
`POST /api/office/documents/{documentId}/generate-profile` trigger behind it. Mirrors Compose's
shipped `refresh-profile` semantics: fire-and-forget, 202 Accepted, unconditional overwrite, no
confirmation prompt.

## §11 reuse decision (component justification)

**Existing:** `POST /api/compose/documents/{documentRecordId:guid}/refresh-profile`
(`ComposeDocumentEndpoints.cs:49`) is the only existing manual profile-refresh trigger in the codebase.
Grep evidence:

```
grep -rn "refresh-profile" --include=*.cs src/server
  → src/server/api/Sprk.Bff.Api/Api/ComposeDocumentEndpoints.cs:49 (route)
  → src/server/api/Sprk.Bff.Api/Api/ComposeDocumentEndpoints.cs:301 (handler)
```

No equivalent exists on `/api/office`.

**Extension:** Not for the ROUTE — the Compose route is bound to the Compose session/body contract
(`RefreshProfileBody.TenantId`/`DocumentSpeId`/`ETag`) and lives on a spine `spaarkeai-compose-r8` is
actively changing; the add-in has no Compose session and the task's own escalation trigger forbids
touching anything under `Services/Compose/`. The route is therefore new.

The SERVICE behind it IS reused: both the Compose route and this task's route ultimately call the SAME
sanctioned facade, `Services/Ai/PublicContracts/DocumentProfileAi.ProfileDocumentAsUserAsync`
(`IDocumentProfileAi`) — the platform's canonical OBO direct-Action document-profile facade (its own
XML doc calls it "CANONICAL... not a temporary stand-in"). No AI logic, prompt, or field-mapping code
is duplicated. What IS duplicated is the minimal fire-and-forget detached-DI-scope plumbing
(`ComposeProfileDispatcher` vs the new `OfficeProfileDispatcher`) — necessarily, because
`ComposeProfileDispatcher` is `internal sealed` to the Compose module and hand-constructed (not
DI-registered) inside `ComposeService`'s own constructor. There is no DI seam to inject it from
`OfficeService` without modifying a file under `Services/Compose/`, which the POML's escalation
trigger #2 explicitly forbids ("If mirroring the Compose semantics would require modifying
`IComposeService` or anything under `Services/Compose/`, STOP and escalate"). Building a small,
independent Office-side dispatcher that calls the SAME facade is the reading of that trigger that lets
the task proceed without violating it: the trigger protects the Compose *files*, not the *pattern*.

**Cost of doing nothing:** without this trigger, a document whose `sprk_filesummarystatus` is Failed,
None, or Skipped can never be re-profiled from the pane — the user's only recourse is leaving Word,
opening the record in a browser, and finding the refresh action there (which does not exist on
`sprk_document`'s form today either). This is exactly the workflow break FR-08 exists to close, and it
also strands every document that failed profiling before GitHub #919 was fixed on master.

**Filter reuse:** the resource-authorization decision reuses the EXISTING, unmodified
`DocumentAuthorizationFilter` (`Api/Filters/DocumentAuthorizationFilter.cs`) with operation `"write"` —
the same filter and operation `PUT /api/v1/documents/{id}` and the checkout family already use. No new
authorization filter class was written.

## Placement Justification (`.claude/constraints/bff-extensions.md`)

The new route extends the existing `/api/office` group (`OfficeEndpoints.MapDocumentProfileEndpoints`)
rather than living elsewhere, because:

1. It is an Office add-in surface operation — every other add-in-facing route lives under
   `/api/office` (save, todo, quickcreate, search, share, jobs, recent), and this one follows the same
   shape (`AddOfficeRateLimitFilter` + `AddOfficeAuthFilter` + a resource filter, delegate to
   `IOfficeService`).
2. It does not belong on `/api/compose` — that group's routes carry Compose-specific session/body
   contracts this trigger has no analog for, and `spaarkeai-compose-r8` owns that spine.
3. It does not belong on `/api/v1/documents` (the generic document CRUD surface task 021's READ uses)
   — this is an add-in-initiated ACTION (dispatch a background job), not a document field read/write,
   and the Office group already owns the add-in's authorization/rate-limit conventions this action
   needs (`OfficeAuthFilter`, the `QuickCreate` rate category).
4. No new service, interface-plus-implementation pair, or package was added. `IOfficeService` gained
   ONE new method; `OfficeService` gained ONE new internal collaborator
   (`OfficeProfileDispatcher`, mirroring the established `ComposeProfileDispatcher` shape) built from
   already-registered DI types.

## The idempotency trap — how this task avoids it

The dispatch brief's warning: `UploadFinalizationWorker` queues an `AppOnlyDocumentAnalysis`
Service-Bus job with idempotency key `analysis-{documentId}-documentprofile`
(`Workers/Office/UploadFinalizationWorker.cs` `QueueNextStageAsync`), and the downstream handler skips
an already-processed key. That is Path C in
`docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md` — the **node-based playbook** spine
used by the app-only/background save path, and the ONE spine that is also the confirmed root cause of
GitHub #919 (Update Record node re-parse failure).

**This trigger never touches that queue, that key, or that spine.** `OfficeProfileDispatcher.Dispatch`
calls `IDocumentProfileAi.ProfileDocumentAsUserAsync` directly — the SAME **direct-Action** facade
Compose's `refresh-profile` and the interactive wizard both use (Path B in the architecture doc).
Reading `DocumentProfileAi.ProfileDocumentAsUserAsync` end to end confirms it carries **no idempotency
gate of its own**: it resolves the record, extracts text under OBO, runs `ActionRunner` once, and
writes the mapped fields — unconditionally, every call. There is no cache check, no stored-key lookup,
no "already processed" skip anywhere in that path. A "Generate Profile" click therefore ALWAYS runs the
profile, whether the document was never profiled, already Completed, or previously Failed — which is
exactly the required semantics (unconditional overwrite, no confirmation) and exactly why the button
can never silently no-op on an already-processed document.

**The interface this task assumes for task 029 (per-version key, not implemented here):** task 029 owns
a per-VERSION idempotency key for version saves, which is a DIFFERENT question (should re-uploading the
same bytes as a new version re-trigger profiling?) than this task's question (should a user's explicit
click always re-trigger?). This task's design does not constrain task 029's key scheme in any way,
because this trigger's path does not read or write ANY idempotency key at all — task 029 is free to key
however it needs to on the Path C / Service-Bus side without colliding with this Path B / direct-call
trigger.

**Proof, by test:** `OfficeGenerateProfileContractTests.Post_GenerateProfile_OnACompletedProfile_
StillDispatches_NoConfirmationRequired` (server) and `DocumentProfileSection.test.tsx`'s "overwrites a
Completed profile with no confirmation prompt" (client) both arrange a document already in the
Completed state and assert the trigger still dispatches/POSTs — i.e., is never silently skipped.

## Authorization filter

`DocumentAuthorizationFilter` (existing, unmodified), operation `"write"`, bound via the route's
`documentId` parameter — the ADR-008-mandated resource-level check. Filter order on the endpoint:
`AddOfficeRateLimitFilter` → `AddOfficeAuthFilter` → an inline Guid.Empty VALIDATION filter → `Add
DocumentAuthorizationFilter("write")` → handler. The validation filter runs BEFORE authorization
deliberately, so an obviously-invalid id (`Guid.Empty`) 400s without ever probing the access data
source for a record that cannot exist — validation and authorization stay two separate decisions,
neither one inline in the handler body.

## Deviations from the POML

1. **Contract tests in a NEW file**, per the dispatch brief's authorized deviation (task 024 owns
   `OfficeEndpointsContractTests.cs` in this parallel wave):
   `tests/integration/contract/Api/Office/OfficeGenerateProfileContractTests.cs`. It reuses (does not
   modify) the shared `OfficeTestWebAppFactory`/`TestAuthHandler` declared in that file, adding its own
   local `OfficeGenerateProfileTestWebAppFactory` subclass that doubles `IAccessDataSource` and
   `IDocumentProfileAi` — configured entirely in the new file.
2. **Malformed-id acceptance criterion, read literally vs. the reachable HTTP shape.** The acceptance
   criterion says "a malformed OR empty document id returns 400." `Guid.Empty` — a syntactically valid
   GUID matching the `{documentId:guid}` route constraint but never a real record — returns 400
   ProblemDetails as required. A route segment that fails to parse as a GUID at all (e.g.
   `not-a-guid`) never reaches the handler or any endpoint filter: ASP.NET Core's `:guid` route
   constraint 404s it at the routing layer before any application code runs. This is the IDENTICAL
   shape Compose's own reference `refresh-profile` route accepts (`{documentRecordId:guid}`), so it is
   a deliberate mirror of the reference implementation, not an oversight — recorded here and covered by
   `Post_GenerateProfile_WithNonGuidRouteSegment_Returns404_NotAnUnhandledError`, which asserts the 404
   is a defined, non-crashing response.
3. **Rate-limit category:** reused `OfficeRateLimitCategory.QuickCreate` rather than adding a new
   category — the same choice `/todo` made for the same reason (a low-frequency inline action), per
   CLAUDE.md §11 reuse-before-extend.

## Files touched

- `src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs` — new `MapDocumentProfileEndpoints` +
  `GenerateProfileAsync` handler
- `src/server/api/Sprk.Bff.Api/Services/Office/IOfficeService.cs` — new `GenerateProfileAsync` method
- `src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs` — 3 new optional ctor params, new
  `_profileDispatcher` field, `GenerateProfileAsync` implementation
- `src/server/api/Sprk.Bff.Api/Services/Office/OfficeProfileDispatcher.cs` — NEW FILE, the fire-and-forget
  dispatcher (mirrors `ComposeProfileDispatcher`'s pattern without touching `Services/Compose/`)
- `src/client/office-addins/shared/taskpane/hooks/useDocumentProfile.ts` — added `generateProfile`,
  `isGenerating`, `generateError`; changed the hook's return shape from a bare `DocumentProfileOutcome`
  to `UseDocumentProfileResult` (breaking change for this hook's only consumer, `DocumentProfileSection`,
  updated in the same commit)
- `src/client/office-addins/shared/taskpane/components/DocumentProfileSection.tsx` — added the Generate
  Profile `Button` (Fluent v9, semantic tokens only) with default/busy/disabled states
- `src/client/office-addins/shared/taskpane/components/__tests__/DocumentProfileSection.test.tsx` —
  updated the pre-existing "Completed" test's read-only assertion (a button now legitimately exists —
  Generate Profile, not a field save-back) and added a `Generate Profile` describe block
- `tests/integration/contract/Api/Office/OfficeGenerateProfileContractTests.cs` — NEW FILE
