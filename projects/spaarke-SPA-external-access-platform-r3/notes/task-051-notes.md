# Task 051 — Messages section in the record detail view (FR-04 read, FR-11 post)

**Status (2026-10-11):** implemented (rigor FULL, model tier sonnet, effort high). Not committed (the main session commits by path). Adversarial review pending. The browser UI tests in the POML (`<ui-tests>`) were not run: no live session from this agent.

## Files
- `src/client/external-spa/src/api/messages-client.ts` (new) — `listMessages`, `sendMessage`, the `MESSAGE_ROOTS` table, `MAX_MESSAGE_BODY_LENGTH`, `messageProblemOf` (ProblemDetails fields).
- `src/client/external-spa/src/components/detail/RecordMessages.tsx` (new) — the section: thread + compose.
- `src/client/external-spa/src/components/detail/RecordDetailModal.tsx` (modified, +5 lines) — mounts `<RecordMessages>` after the events section.
- `src/client/external-spa/tests/RecordMessages.test.tsx` (new, 35 tests).
- `src/client/external-spa/tests/RecordDetailModal.test.tsx` (modified; see Deviations 2).
- `src/client/external-spa/tsconfig.json` (modified; see Deviations 1).

## Server contract (read from code, not docs)
`ExternalMessageEndpoints.cs` / `ExternalMessageDtos.cs`:
- `GET` and `POST /api/v1/external/{root}/{id}/messages`. `Roots`: `projects`, `matters`, `workassignments`, `invoices`, `servicerequests` (case-insensitive). Anything else, documents included, is a 404 `EXT_MESSAGES_ROOT_NOT_SUPPORTED`.
- `RecordDetailModal` opens six entities (`RECORD_DETAIL_ENTITIES`); five map to a root. `sprk_document` has no thread, so no section and no read.
- POST body is `{ sprk_body }`, plain text, non-blank, at most 10,000 characters (`string.Length`, UTF-16 units, the same count as JS `.length`). A key containing "attach" or equal to `file`/`files` is refused 400 `EXT_MESSAGES_ATTACHMENTS_NOT_SUPPORTED`; this client has no way to send one.
- Errors: ProblemDetails with `errorCode` (400/404/500/502) or `reasonCode` (403: `record_not_accessible`, `insufficient_rights`, `sender_unattributable`). 409 is the filing step's owner refusal. 429 is the `dataverse-query` per-user limiter.
- `ExternalMessageDto.isMine` is the own-vs-other signal. The 201 body has `sendername: null` and a server-time `createdon`.

## Sanitizer decision (review 033 K1)
**Reuse the shared `sanitizeEmailHtml`** (`@spaarke/ui-components/utils/sanitizeEmailHtml`), no new dependency.
- A hardened sanitizer already exists in `Spaarke.UI.Components` (closed DOMPurify allow-list; no script/iframe/object/embed/form/style; every `on*` attribute dropped; http/https/mailto only; anchors forced to `rel="noopener noreferrer" target="_blank"`; dangerous CSS dropped). The internal `MessageRow` and `MessageBubble` use it for the same `sprk_body` + `sprk_bodyformat` pair.
- `dompurify ^3.4.0` is already a direct dependency of external-spa (`package.json`), so this adds no package. ADR-012 says reuse before writing new.
- Why not "render everything as text": the thread read returns inbound HTML mail filed on the record (`sprk_bodyformat` 100000001). Showing it as text would display raw tags to the user. The server DTO carries the format precisely so the client renders HTML as HTML. A thread post is always plain text (the server stores it so), and that path is a React text node.
- Rules in `RecordMessages.tsx`: format = HTML renders `sanitizeEmailHtml(body)` via `dangerouslySetInnerHTML` (the only place it is used); format PlainText, null or anything else renders as a text node. A body with no format is never treated as HTML (tested).

## Behaviour
- List: oldest-to-newest as the server orders it, each row sender + local time (`<time dateTime>`). Own messages: sender "You", `data-mine="true"`, a brand-tinted background token. `isMine` only; names are never compared. No sender: "Unknown sender". `truncated` shows the shared `TruncatedListNotice`. Empty: "No messages yet."
- Compose: a Fluent `Textarea` (no `maxLength` attribute, so a paste is never silently cut), live counter "n / 10,000" that turns to "n over the 10,000 character limit" (red-foreground token, `role=alert`), Send. Send is off for empty/whitespace, over 10,000, and while a post is in flight; the textarea is also disabled while sending. A ref lock closes the same-tick double-click gap before React re-renders (review 034 K4). The text sent is the trimmed draft.
- After a 201 the box is cleared and the thread is re-read (GET); the 201 body is never rendered (review 034 K3). If the re-read fails, a warning with Refresh shows and the thread on screen stays.
- Refused send: inline `MessageBar` under the box, the draft kept untouched. 403 shows the server detail when specific (insufficient rights), else "You do not have permission to post messages on this record." 429: "You have sent too many messages. Please try again shortly." 400/404/409: the server detail. 5xx or network: a generic retry sentence (the raw detail is not shown). 401: the shared sign-in state (`BffAccessState`).
- List refused: 401 → `BffAccessState` sign-in; 403 → `BffAccessState` denied ("You do not have permission to view messages on this record."), no compose box; 404 → "Messages are not available for this record."; other → error with Try again.
- Look: mirrors the shared `MessageRow` (sender semibold, caption timestamp, `pre-wrap` body). Fluent v9 semantic tokens only; no hex/rgb values (grep-checked); inherits the host `FluentProvider`, so light/dark/Teams themes apply.

## Gates (in `src/client/external-spa`)
- `npm run typecheck` exit 0; `npm run lint` exit 0 (0 errors, 0 warnings); `npx vitest run` 452 passed / 19 files (exit 0); `npm run build` exit 0 (`dist/assets/app.js` 1,413,376 bytes); `npm run check:dist` exit 0 ("1 file(s) clean").
- New: 35 tests in `RecordMessages.test.tsx` plus 6 added to `RecordDetailModal.test.tsx` (5 supported-entity sections + the document negative). Suite: 411 before, 452 after (41 new), all passing.
- Fail-before checks (mutating the finished component, restoring after; the files are new, so there is no old file to restore):
  - `sanitizeEmailHtml(body)` replaced by `body` → the script-bearing-body test fails (1 failed, 34 passed).
  - the same-tick lock `if (sendLock.current || …)` removed → the double-click test fails (1 failed, 34 passed).
  - draft cleared in the send `catch` → the 8 refused-send tests fail (8 failed, 27 passed).

## Acceptance criteria → evidence
1. Thread with sender and timestamp → `the thread > lists the messages oldest-to-newest with sender and time`; `own-vs-other follows isMine`.
2. Sending posts once and shows it → `sending > posts the text once, re-reads the thread, and shows the server row (not the 201 body)` (1 POST, 2 GETs, 201-body marker absent).
3. Send disabled when empty / in flight; over 10,000 cannot be sent → `Send is disabled for an empty body…`; `a body of exactly 10,000 … 10,001 cannot`; `Send is disabled while a post is in flight, and a double-click posts once`.
4. 403 on send is inline and keeps the draft → `a refused send … > 403 (no Create)`, `403 with the generic detail`; the same group covers 429, 400, 409, 5xx, network and 401.
5. No attachment control → `there is no attachment control` (no file input, no attach/file/upload button or label, no "attach" text) and `the POST body carries the text only` (keys = `['sprk_body']`).
6. typecheck, lint, tests, build pass → above.
Script-bearing body → `message bodies > an HTML body with script and handlers is sanitized` (no script/iframe/object/embed/form/style element and no `on*` attribute or `javascript:` value in the section; `window.__pwned` unset; safe `<b>` kept) and `a plain-text body is a text node`.

## Deviations
1. **`tsconfig.json` gained `"dompurify": ["./node_modules/dompurify"]` under `paths`.** The shared `sanitizeEmailHtml.ts` is type-checked as part of external-spa once imported, and `tsc` could not resolve `dompurify` from the shared folder (it has no `node_modules` here). The vite build already redirects it (`resolveSharedLibDeps`); this is the same fix the file already applies to react and fluent. Without it `npm run typecheck` fails.
2. **`RecordDetailModal.test.tsx` edited (50 lines).** (a) Every network mock answers the new `/messages` GET with an empty list. (b) The "read-only: no input of any kind in the modal" assertion now excludes the Messages compose box (its one textarea) and adds a no-file-input assertion; posting to a thread is not editing the record. (c) The paging test filtered its call scan to the fetch route (a message GET has no body). (d) Added section tests: Messages shown for the 5 supported entities (correct route per root), not shown and no read for a document. No assertion was weakened beyond (b).
3. **`teamsHost` is not threaded to `RecordMessages`.** `RecordDetailModal`/`GridWidgetBody` have no host signal today (it lives in `App`/`main`), and threading it widens the change. `RecordMessages` takes an optional `teamsHost` prop (default false), passed through to `BffAccessState`. Effect: a 401 on a message call inside the Teams tab would show "Sign in again" that tries a redirect instead of the tab reload. A 401 there needs bff-client's forced-refresh retry to fail first. Follow-up: pass `teamsHost` from the grid widget bodies (or a context) to the modal.
4. POML step 5 (TASK-INDEX / `<status>` / drift script) skipped per the main session's instruction.
5. The mock mode (`mock-service.ts`) was not extended: it has no handler for the module fetch the detail view reads, so the detail view is unreachable in mock mode anyway.

## Observations (not changed)
- The sanitizer keeps `style`/`color`/`bgcolor` attributes and http(s) `<img src>` (shared decision, task-001 of email-r5). An inbound HTML mail with its own colours can be hard to read in dark mode, and a remote image loads when shown. The same holds on the internal timeline; a change belongs in the shared util.
- The thread read has no paging; `truncated` is surfaced but there is no "load more". It is the server's cap behaviour.
- The 429 body is whatever the ASP.NET limiter emits (usually empty); the UI does not depend on it.

## Review fix: "Sign in again" inside the Teams tab (NFR-04) - supersedes Deviation 3
Defect: `RecordMessages` took an optional `teamsHost` (default false) that nothing passed, because the modal is opened from registry-built widget bodies (`createGridWidgetBody(configId)`) that carry no host signal. A 401 on the messages list or on send therefore called `loginRedirect` inside the Teams iframe.

**Mechanism.** No existing context or module getter carried the host (checked `App.tsx`, `main.tsx`, `src/host/TeamsHostAdapter.ts`, `src/auth/*`; `teamsHost` was only ever a prop on `AuthGuard`, `AppHeader`, `WorkspaceHomePage`, `DocumentUploadPage`). Added one React context, `src/components/auth/TeamsHostContext.tsx` (`TeamsHostProvider`, `useTeamsHost`). `App` wraps its tree (inside `DataGridExternalHostProvider`, around `FluentProvider`) with `<TeamsHostProvider teamsHost={teamsHost}>`. `BffAccessState` resolves `teamsHostProp ?? contextValue ?? false`: an explicit prop still wins, so `WorkspaceHomePage` / `DocumentUploadPage` behave as before, and a missing provider keeps the browser behaviour. React context crosses portals, so modals are covered.

**`RecordMessages` prop removed** (redundant: nothing used it and the context supplies it). `BffAccessState`'s own `teamsHost` prop stays as the explicit override.

**Tests** (`tests/RecordMessages.test.tsx` new describe, `tests/App.teamsHost.test.tsx`): Teams + 401 on list, Teams + 401 on send (draft kept): reload once, `loginRedirect` never; browser: `loginRedirect` with `{scopes, prompt:'select_account'}`, no reload; no provider: browser behaviour; explicit prop overrides the provider both ways; and the real `App` provides its `teamsHost` to a prop-less `BffAccessState`.
Fail-before: with `BffAccessState` at HEAD, the 2 Teams tests failed (reload called 0 times); with `TeamsHostProvider` removed from `App`, `App.teamsHost` "Teams: reloads, never redirects" failed (1 failed, 1 passed). The override, browser and no-provider tests are regression guards and pass before and after.

**Other call sites reachable from the Teams tab** (none needed a change):
- `BffAccessState` callers `WorkspaceHomePage`, `DocumentUploadPage`: pass `teamsHost` explicitly (still correct, now redundant). `RecordMessages`: covered by the context. `ProjectPage` has its own denied `MessageBar` and no sign-in action.
- `AuthGuard` `instance.loginRedirect`: guarded by `if (teamsHost)` before it can run.
- `msal-auth.ts` `acquireStandaloneBffToken` (`acquireTokenRedirect`, lines ~217/231): the standalone (CIAM / workforce-browser) path only; Teams uses the NAA/popup strategy (header of `TeamsHostAdapter`/msal-auth: "never `acquireTokenRedirect`"). Not reachable in Teams.
- `App.tsx` `logoutRedirect`: `AppHeader` renders no sign-out menu when `teamsHost`, so unreachable in the tab.

**§11 (new context).** 1 Existing: no context/getter carried the host; `teamsHost` was prop-only (grep over `src`). 2 Extension: threading the prop through `RecordDetailModal` -> `GridWidgetBody` -> registry `createGridWidgetBody` would change the widget-body contract for every widget; a module-level getter would be untestable global state. 3 Cost of nothing: a 401 in the Teams tab calls `loginRedirect` inside the iframe (NFR-04 violation) - covered by the tests above.
