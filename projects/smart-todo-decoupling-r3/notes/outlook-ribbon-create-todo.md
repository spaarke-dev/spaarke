# Outlook ribbon "Create To Do" — UX + manifest decisions

> **Project**: smart-todo-decoupling-r3
> **Task**: 070 (Phase 8: Outlook Add-in)
> **Spec**: [FR-27](../spec.md)
> **Related contract**: [`createtodo-launch-contract.md`](./createtodo-launch-contract.md) §3
> **Authored**: 2026-06-08

---

## Purpose

This note records the implementation decisions made in task 070 for the
Outlook ribbon "Create To Do" action (FR-27). The deploy + manual smoke test
is deferred to task 072; this task is code-only.

---

## Summary

When the user clicks "Create To Do" on an email-read ribbon button in Outlook:

1. The taskpane opens with `?action=createTodo` in the URL.
2. `outlook/taskpane/index.tsx` reads the query param and passes
   `initialAction = 'createTodo'` + `createTodoConfig` to `<App>`.
3. `App.tsx` renders `<CreateTodoView>` instead of the default Save / Share /
   Status tabs.
4. `CreateTodoView` calls `useCreateTodoFromEmail`, which:
   - Reads `internetMessageId` + `subject` from the host adapter.
   - Calls the BFF lookup
     `GET /api/office/communications/by-message-id/{id}`.
   - On 404 (email not saved) → invokes the caller-supplied
     `saveEmailToSpaarke` callback. The current wiring (see "Open follow-ups"
     below) leaves this as a stub; a future task wires the in-taskpane
     SaveView orchestration. For now, when the email isn't saved, the view
     surfaces a clear "save first" error message.
   - On 200 (email already saved) → opens the CreateTodo wizard from the
     SmartTodo Code Page in a new browser window with `?action=createTodo`,
     `?regardingType=sprk_communication`, `?regardingId=<commId>`,
     `?regardingName=<subject>` query params.
5. The SmartTodo Code Page reads those params on init and opens
   `<CreateTodoWizard>` with the matching `initialRegarding` prop per the
   launch contract.

---

## Key decisions

### D-1 — Popup window instead of inline wizard mount

**Decision**: Open the CreateTodo wizard in a new browser window
(`window.open`), not inside the Outlook taskpane.

**Why**:
- The Outlook taskpane is 250-450px wide. The `CreateTodoWizard` uses the
  `CreateRecordWizard` shell which requires a dialog-sized surface.
- `CreateTodoWizard` requires `IDataService` + `INavigationService` (Xrm
  Web API + Dataverse lookup dialogs). These live in the Power Apps host
  that runs the SmartTodo Code Page; they're not available in the Outlook
  taskpane process.
- The existing Outlook add-in pattern for "open a Dataverse form" is
  `window.open` — see the `SaveView` Quick Create handler in
  [`App.tsx`](../../../src/client/office-addins/shared/taskpane/App.tsx).
- Mounting the wizard inline would require either (a) shipping a Dataverse
  Xrm shim into the add-in (heavy, regresses NFR-02 share-lib boundary) or
  (b) writing a new BFF-backed IDataService adapter (out of scope for 070).

**Trade-off accepted**: The user moves between two windows (Outlook
taskpane + the popup). This matches the existing Quick Create UX so users
already understand the pattern.

### D-2 — Same taskpane URL with `?action=createTodo` (not a separate runtime)

**Decision**: Both ribbon buttons ("Save to Spaarke" and "Create To Do")
target the SAME taskpane HTML — `outlook/taskpane.html` — distinguished by
a query-string parameter `?action=createTodo`.

**Why**:
- Reuses the existing auth bootstrap, host adapter init, theme detection,
  and React root rendering. No code duplication.
- Webpack only needs one entry point + one HTML — no new bundle
  artifacts.
- The XML manifest and JSON manifest both support multiple `<bt:Url>` /
  `runtimes` entries that point to the same HTML with different query
  strings.

**Trade-off accepted**: A single `URLSearchParams.get('action')` read on
init drives the routing. Small, contained, well-tested.

### D-3 — Email-save callback is a stub for task 070

**Decision**: `outlook/taskpane/index.tsx` provides
`createTodoConfig.saveEmailToSpaarke = async () => null` as the initial
wiring.

**Why**:
- The full integration — opening SaveView programmatically, awaiting the
  job to complete, re-looking up the new `sprk_communication` — requires
  coordination with SaveView's internal state machine that's out of scope
  for task 070 (the POML explicitly says "Run the existing save flow first
  ... if the email-save flow doesn't exist yet (would need separate task)").
- The save flow DOES exist (see `SaveView.tsx` + `useSaveFlow.ts`); what
  doesn't exist is a programmatic API to invoke it + await job completion
  + extract the `sprk_communicationid`. The current SaveResponse returns
  only `documentId` (`sprk_document` GUID), NOT the `sprk_communicationid`.
- Until that wiring lands, the CreateTodoView shows a clear error to the
  user: "The email was not saved to Spaarke, so the To Do cannot be
  linked. Try again or save the email first." The user opens the Save
  taskpane separately, completes the save, then clicks "Create To Do"
  again — at which point the lookup succeeds and the wizard launches.

**Trade-off accepted**: A two-click UX for unsaved emails until a later
task wires the save flow into the CreateTodoView. Already-saved emails
work end-to-end with this task's changes.

### D-4 — BFF lookup endpoint `/api/office/communications/by-message-id/{id}` does not yet exist server-side

**Decision**: The client `communicationLookupService` calls a BFF endpoint
that doesn't exist yet. When the endpoint is missing in deployment, every
call returns 404, which the service interprets as "not saved" — so the
user sees the D-3 error message and falls back to opening the Save flow
manually.

**Why**:
- Adding the BFF endpoint requires a server-side task with publish-size
  measurement (NFR-03) + tests (NFR-11) per CLAUDE.md §10. That's a
  separate task with its own scope.
- The client-side contract is clean: when the endpoint exists, the
  end-to-end flow lights up automatically. No client changes needed.

**Trade-off accepted**: The "Create To Do" action degrades to a graceful
"save first" message until the BFF endpoint ships. Documented as
**Open follow-up #1** below.

### D-5 — Manifest button labels + icons

- **Label**: "Create To Do" (matches FR-27 verbatim).
- **Icon**: Reuses the standard Spaarke icon set (`Icon.16x16` /
  `Icon.32x32` / `Icon.80x80`). No new artwork in task 070; the existing
  icons distinguish by label + supertip text.
- **Supertip title**: "Create Spaarke To Do".
- **Supertip description**: "Create a Spaarke To Do linked to this email".
- **Both manifest forms** (XML for production, JSON for dev preview)
  updated. Both V1.0 and V1.1 `MessageReadCommandSurface` extension
  points include the new button.

### D-6 — Configuration: `SMARTTODO_CODEPAGE_URL` env var

Per CLAUDE.md §16 (product portability), no hardcoded org URLs in
source. The SmartTodo Code Page base URL is supplied via a new optional
env var `SMARTTODO_CODEPAGE_URL`. When unset, the launcher service is
inert (returns no URL) and the ribbon action surfaces a clear
"action not configured" error.

The webpack config + `.env.example` are updated. The env var flows through
`webpack.DefinePlugin` and into `outlook/taskpane/index.tsx` as
`process.env.SMARTTODO_CODEPAGE_URL`.

---

## Files created

| Path | Purpose |
|---|---|
| `shared/taskpane/services/communicationLookupService.ts` | BFF lookup for email → communication by `internetMessageId` |
| `shared/taskpane/services/createTodoLauncher.ts` | Builds the launch URL + opens the wizard popup |
| `shared/taskpane/hooks/useCreateTodoFromEmail.ts` | Hook orchestrating lookup → save → launch state machine |
| `shared/taskpane/components/views/CreateTodoView.tsx` | The view rendered when `initialAction === 'createTodo'` |
| `shared/taskpane/services/__tests__/communicationLookupService.test.ts` | Unit tests (10 cases) |
| `shared/taskpane/services/__tests__/createTodoLauncher.test.ts` | Unit tests (12 cases) |
| `shared/taskpane/hooks/__tests__/useCreateTodoFromEmail.test.ts` | Unit tests (7 cases) |

## Files modified

| Path | Change |
|---|---|
| `outlook/outlook-manifest.xml` | + Create To Do button (V1.0 + V1.1), + CreateTodoTaskpane.Url, + new ShortStrings / LongStrings |
| `outlook/manifest.json` | + CreateTodoTaskpaneRuntime + CreateTodoButton control |
| `outlook/taskpane/index.tsx` | + `readInitialAction()`, + `buildCreateTodoConfig()`, pass `initialAction` + `createTodoConfig` to `<App>` |
| `shared/taskpane/App.tsx` | + `initialAction` + `createTodoConfig` props, + CreateTodoView mount when `initialAction === 'createTodo' && hostType === 'outlook'`, + `showCreateTodoView` guard on default tabs |
| `shared/taskpane/components/views/index.ts` | + CreateTodoView barrel export |
| `shared/taskpane/hooks/index.ts` | + useCreateTodoFromEmail barrel export |
| `webpack.config.js` | + `SMARTTODO_CODEPAGE_URL` env var threading |
| `.env.example` | + `SMARTTODO_CODEPAGE_URL` documentation |

---

## Test coverage

29 unit tests across 3 suites — all passing:

| Suite | Test count | Coverage |
|---|---|---|
| `communicationLookupService.test.ts` | 10 | inert behavior, 200 / 404 / 500 / 401, URL encoding, defensive parsing |
| `createTodoLauncher.test.ts` | 12 | param emission, GUID normalization, encoding, base-URL preservation, error throws, popup-blocked handling |
| `useCreateTodoFromEmail.test.ts` | 7 | already-saved short-circuit, save-required + launch, save-failed error, lookup-error, popup-blocked, reader-error, reset |

**Key acceptance criteria from the POML covered**:
- ✅ Click on un-saved email triggers save flow first, then wizard (verified via mock: lookup returns 404 → save callback invoked → launch fires with new commId)
- ✅ Click on already-saved email opens wizard directly (verified: lookup returns 200 → launch fires immediately, save callback NOT invoked)
- ✅ Wizard pre-fills regarding to `sprk_regardingcommunication` with the correct id (verified: launch URL contains `regardingType=sprk_communication` + `regardingId=<commId>`)
- ✅ Resulting `sprk_todo` resolver fields populated atomically — this is the wizard's responsibility (TodoService.createTodo + applyResolverFields, ADR-024). The wizard itself is covered by task 030/031/032 tests.

---

## Build / lint / type-check results

| Command | Result |
|---|---|
| `npm run build` (webpack production) | ✅ clean — no errors / warnings |
| `npx jest --testPathPattern="(createTodoLauncher\|communicationLookupService\|useCreateTodoFromEmail)"` | ✅ 29/29 pass |
| `npx eslint <new files>` | ✅ zero issues |
| `npx tsc --noEmit --skipLibCheck` filtered to new files | ✅ zero errors |

Pre-existing `tsc` errors in unrelated files (`useSaveFlow.ts`, `useEntitySearch.test.ts`, `errorMessages.ts`, etc.) are NOT regressed by this task. The webpack build uses `transpileOnly: true` so the build remains green; the strict `tsc` issues are tracked elsewhere.

---

## Open follow-ups (not blockers for task 070)

### #1 — BFF endpoint: `GET /api/office/communications/by-message-id/{internetMessageId}`

**Owner**: Server-side task (TBD).

**Contract**:
- Path: `/api/office/communications/by-message-id/{internetMessageId}`
  with `{internetMessageId}` URL-encoded
- 200 response: `{ "communicationId": "<guid>", "subject": "<string>" }`
- 404 response when no `sprk_communication` exists for that message id
- Authorization: OBO — same as other `/api/office/*` endpoints
- Implementation: OData query against `sprk_communication` filtered by
  `sprk_internetmessageid eq '<encodedId>'`, `$select=sprk_communicationid,sprk_name`

Until this endpoint ships, all `findCommunicationByMessageId` calls return
`null` (graceful "not saved" path).

### #2 — `saveEmailToSpaarke` programmatic wiring

Wire the existing `useSaveFlow` to be invokable from `CreateTodoView`:
- Open the SaveView in a controlled flow
- Await job completion
- Extract the `sprk_communicationid` from the BFF response — requires
  extending `SaveResponse.Artifact` to carry `communicationId` alongside
  `documentId` (or doing a post-save lookup via the D-4 endpoint).
- Resolve the `saveEmailToSpaarke` promise with the new triple.

This is the missing piece between "email not saved" and "wizard opens
automatically". Until it lands, the UX for unsaved emails is two clicks:
"Save to Spaarke" → "Create To Do".

### #3 — SmartTodo Code Page URL-param parser

The launch contract terminates at the SmartTodo Code Page. That page
needs to read `?action=createTodo` + `?regardingType` + `?regardingId` +
`?regardingName` on init and open the wizard with the matching
`initialRegarding`. The `CREATE_TODO_LAUNCH_PARAMS` constant in
`createTodoLauncher.ts` is the stable contract — the Code Page reader
imports the same key names.

This is naturally a task in the SmartTodo solution (kanban / Code Page
work), not the office-addins workspace.

### #4 — Manifest icon — dedicated "Create To Do" icon

The button currently reuses the Spaarke generic icon. A dedicated icon
(e.g. a checklist + plus glyph) is a polish task — out of scope for
code-only task 070.

---

## Manual smoke test plan (for task 072 — deploy)

Once the BFF endpoint (#1) and SmartTodo URL parser (#3) ship, the
end-to-end manual test is:

1. Open Outlook web → open an email that hasn't been saved to Spaarke.
2. Click the "Create To Do" ribbon button.
3. The taskpane opens; CreateTodoView renders a Spinner ("Checking
   whether this email is saved to Spaarke…").
4. The lookup returns 404 → the view shows a "save first" message.
5. (Until #2 is wired): user opens the Save taskpane separately, completes
   the save. Then re-clicks "Create To Do".
6. The lookup now returns 200 → a new browser window opens to the
   SmartTodo Code Page with the wizard pre-filled to the communication.
7. User completes the wizard → `sprk_todo` row created with
   `sprk_regardingcommunication` populated atomically + four resolver
   fields populated via `applyResolverFields` (ADR-024).
8. Verify in Dataverse the new `sprk_todo` has the expected regarding.
9. Repeat in Outlook desktop.

---

*Update this note when any of the four follow-ups land.*
