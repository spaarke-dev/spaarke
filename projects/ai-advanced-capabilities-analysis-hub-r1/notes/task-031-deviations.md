# Task 031 deviations — reopen from grid → rehydrate session + review state + files

> Spec FR-11. Completed 2026-07-29.

## Scope change: two additional files beyond the POML's declared `<file role="modify">`

The POML listed only `AnalysisHubWidget.tsx` as the file to modify (plus a new test file). Implementing
the actual reopen flow required touching three more files. Each is documented below with the reasoning
for why the narrower scope wasn't achievable.

### 1. `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` (new endpoint)

No BFF endpoint existed to resolve `analysisId → sessionId`. The task-020 repository method
(`IChatDataverseRepository.GetSessionsByAnalysisAsync`) existed but was never exposed over HTTP. Added
`GET /api/ai/chat/sessions/by-analysis/{analysisId}` — a thin, read-only projection, picking the most
recently created bound session (archived or not; task 022 makes archived sessions still hold their
transcript). Selection logic (`SelectMostRecentSession`) extracted as `internal static` and unit-tested
directly (`tests/unit/Sprk.Bff.Api.Tests/Api/Ai/ChatEndpointsSessionByAnalysisTests.cs`, 4 tests) — no
reflection into a private member (tests CLAUDE.md B8 ban).

**Known gap**: no HTTP-level integration/contract test for the route itself. `ChatEndpoints`'s
`MapChatEndpoints()` group has a very large DI graph (RAG, `SprkChatAgentFactory`, session persistence,
etc.) and no fixture anywhere in the suite hosts it end-to-end. Building one from scratch for a single
thin GET was judged disproportionate to this task's scope; accepted as a documented Path A exception
per code-review (§6.5). Follow-up: if a reviewer wants full route coverage, budget a separate task for a
`ChatEndpoints`-hosting `WebApplication` test fixture (would then unblock contract tests for this route
and any future addition to the group).

### 2. `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx` (additive prop)

`DataGrid.tsx` already exposes an `onRecordOpen` override prop ("hosts can still pass `onRecordOpen` to
override entirely — escape hatch for surfaces that need custom side panes"), but `DataverseEntityViewWidget`
(the wrapper `AnalysisHubWidget` composes) didn't thread it through. Added `onRecordOpen?` to
`DataverseEntityViewWidgetData` and passed it to `<DataGrid onRecordOpen={data.onRecordOpen} />` — the
SAME pass-through pattern the widget already uses for `pageSize`/`availableViews`. Every other
`DataverseEntityViewWidget` consumer (Documents/Projects/Invoices/Work Assignments grids) is unaffected
(prop is optional, defaults to DataGrid's existing native-form-open behavior).

### 3. `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` + `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`

**The core design problem**: AnalysisHubWidget is itself a workspace TAB inside the already-open
three-pane surface — "opens the three-pane surface" for FR-11 therefore has to mean an IN-PLACE session
switch, not a fresh navigation. Investigated three approaches before landing on the one shipped:

1. **Full-page reload via `?sessionId=` URL param** (main.tsx already reads this param — the AIPU2-106
   cold-load restore path). Zero touches to `ConversationPane`/`WorkspacePane`, but (a) it's a much
   heavier UX act than "reopening" implies, and (b) it can't reliably restore the linked file — a
   `document-viewer` tab's `fetchPreviewUrl` closure cannot survive a page reload / JSON round-trip
   (functions aren't serializable, and a static preview URL would already be stale by restore time).
   Rejected.
2. **Persist a `document-viewer` tab via the existing `/tabs` PATCH endpoint** before doing an in-place
   `setChatSessionId`. Same closure-serialization problem as above — a persisted plain-JSON `widgetData`
   has no live `fetchPreviewUrl`, so the restored tab would render but show "preview not available".
   Rejected.
3. **In-place session switch + live PaneEventBus dispatch for the file** (shipped). Requires:
   - `PaneEventTypes.ts`: one new additive discriminant, `session_switch`, on the `conversation` channel
     (ADR-030 additive-types rule — closed 4-channel union unchanged; reuses the ALREADY-DECLARED
     `sessionId` field, no new field).
   - `ConversationPane.tsx`: one new `usePaneEvent('conversation', ...)` subscription that calls the
     component's EXISTING `handleSelectHistorySession(sessionId)` — the exact function the History menu
     already uses (`setChatSessionId` + remount-key bump so SprkChat's `resumeSession()`/`loadHistory()`
     mount-effect re-fires). This is TTL-safe: `ChatSessionManager.GetSessionAsync` (task-025 hardening)
     already falls back Redis → Cosmos → Dataverse on a stale/expired hot-cache entry, so a "reopen an
     old Analysis" case restores correctly without a second restore mechanism.
   - Setting `chatSessionId` independently drives `WorkspacePane`'s existing `chatSessionId`-keyed `/tabs`
     restore effect (task 025) — review/findings-state restore came for free, no `WorkspacePane` changes
     needed.
   - The file restore dispatches a `workspace.widget_load` (`document-viewer`) event with a LIVE
     `fetchPreviewUrl` closure built the same way `analysisFileResolution.ts` builds one — restated
     locally in `AnalysisHubWidget.tsx` because that module is SpaarkeAi-solution-owned and this
     shared-lib widget cannot import it (ADR-012 dependency direction; mirrors the file's own existing
     `WORK_TYPE_AGREEMENT_REVIEW` restatement precedent).

This was the only option that genuinely satisfies acceptance criterion 2 ("TTL-expired session →
restored from Cosmos, no empty session created") without duplicating task-025's restore logic in a
second place.

## Verified, not touched

- `ThreePaneShell.tsx` / `main.tsx` / `SessionRestoreManager` — unchanged. The cold-load `?sessionId=`
  restore path continues to work exactly as before.
- `WorkspaceTabManager.ts` / the `/tabs` PATCH+GET NFR-09 mechanism — unchanged; reused as-is via the
  `chatSessionId` state change alone.
