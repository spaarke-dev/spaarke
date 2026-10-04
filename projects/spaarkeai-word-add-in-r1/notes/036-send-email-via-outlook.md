# Task 036 — Send Email via Outlook (FR-15)

## What was built

- `shared/adapters/types.ts`: added `HostCapabilities.canComposeEmail`, plus `EmailComposeContent` /
  `ComposeEmailResult` types.
- `shared/adapters/IHostAdapter.ts`: added `composeNewEmail(content): Promise<ComposeEmailResult>`.
- `shared/adapters/OutlookAdapter.ts`: `canComposeEmail` gated on Mailbox requirement set **1.6** AND
  the pane running in **read mode** (`_currentMode === 'read'`) — `displayNewMessageForm`'s
  Microsoft-documented applicable Outlook mode is Message Read, not Compose. `composeNewEmail` calls
  `Office.context.mailbox.displayNewMessageForm({ subject, htmlBody })`, wrapped in try/catch so a
  synchronous throw (e.g. a parameter exceeding its size limit) becomes the same defined
  `{ success, errorMessage }` shape as every other adapter failure path.
- `shared/adapters/WordAdapter.ts`: `canComposeEmail` is always `false`; `composeNewEmail` returns a
  defined "not supported in Word" failure result (never throws) — mirrors `attachFile`'s convention.
- `shared/taskpane/services/sendEmailService.ts` (new): pure orchestration — mints the document share
  link via the existing `POST /api/documents/{documentId}/share-link` route (no body, no expiry
  override), builds the record link by reusing `openRecordLauncher.buildOpenRecordUrl` verbatim, and
  composes an HTML body with one or both links. A document-link minting failure blocks composition
  entirely (returns `{ kind: 'error' }`) even when a record link is also available — chosen to satisfy
  the acceptance criterion literally ("no compose window opens with a broken or placeholder link")
  rather than silently degrading to a record-only email.
- `shared/taskpane/App.tsx`: added the Send Email button (visible on any tab, gated on
  `hostAdapter.getCapabilities().canComposeEmail && (documentId || relatedRecord)`, never on
  `hostType`), `handleSendEmail`, and two small plumbing extensions:
  - `handleSaved` (fired by `SaveView.onSaved` on both hosts) now also populates
    `savedContext.relatedRecord` (previously it only set the friendly `regardingEntity` /
    `regardingRecordId` / `regardingName` trio) — this is the ONLY path that gives Outlook a
    record link at all, since Outlook has no FR-01 URL-based identity resolution.
  - `SaveView`'s `onComplete` callback now also sets `savedContext.documentId` (previously it only
    logged) — the only place Outlook ever learns a `documentId`; for Word it's a same-value
    overwrite in the common case, since FR-01 resolution usually already populated it.

## Outlook API used

`Office.context.mailbox.displayNewMessageForm(parameters)` — Mailbox requirement set **1.6**
([Microsoft Learn](https://learn.microsoft.com/javascript/api/outlook/office.mailbox), "Applicable
Outlook mode: Read"). Deliberately NOT `displayNewMessageFormAsync` (Mailbox 1.9, same Read-only
applicability, no functional advantage for this use case — no attachments, no callback-dependent
follow-up). Gated on `_currentMode === 'read'`, not just the requirement-set check, because the API's
own documented contract excludes Compose surfaces; calling it from a compose-mode pane is unsupported
by the host, not merely untested by Spaarke.

## Word finding (escalation trigger 2 — needs owner confirmation)

`Office.context.mailbox` does not exist in Word — confirmed by reading the WordAdapter and by the
`@types/office-js` ambient declarations (`Mailbox` is a member of `Office.Context`'s Outlook-only
surface). **Implemented as instructed**: `WordAdapter.getCapabilities().canComposeEmail` is always
`false`, so the Send Email button never renders in Word (capability-gated, not a `hostType` branch).

Options considered for a future Word-side equivalent, NOT implemented (out of scope per the task's
Word-finding instruction):

1. **Outlook-on-the-web compose deep link**
   (`https://outlook.office.com/mail/deeplink/compose?subject=...&body=...`) opened via
   `Office.context.ui.openBrowserWindow` (the same `OpenBrowserWindowApi` 1.1 mechanism task 027 uses
   for the record-open escape hatch). Pros: reuses an already-proven mechanism in this codebase, no
   new Office capability, works for any Outlook-on-the-web or desktop-with-web-fallback user. Cons:
   body must be URL-encoded into the query string (there is a practical length ceiling well below
   Mailbox 1.6's 32 KB `htmlBody` limit — untested exactly where it breaks); opens a full browser tab
   rather than a native compose window; does not work offline.
2. **`mailto:` link** opened via `window.open` or an `<a href="mailto:...">`. Pros: zero new Office
   API surface, works everywhere including fully offline. Cons: `mailto:` bodies are **plain text
   only** — the two links could not be rendered as clickable anchors, only as raw URL text, which is a
   materially worse recipient experience than the Outlook-side rich HTML body Send Email otherwise
   produces. Recipients using non-Outlook default mail clients would get a jarring experience switch.
3. **Hide the affordance in Word entirely (current implementation).** Zero new surface, zero
   inconsistency risk, matches "the capability is honestly absent" framing NFR-10 already establishes
   for `canGetDocumentUrl`/`canOpenBrowserWindow` in the other direction (Outlook-only capabilities
   reporting `false` in Word is the SAME pattern already shipped, not a new one).

**Recommendation**: keep option 3 (current behavior) for r1. If the owner wants SOME Word-side
affordance, option 1 (web-compose deep link via the existing `openBrowserWindow` mechanism) is the
better fit technically — it is the smallest new surface and reuses a mechanism this codebase already
trusts — but option 2's lower bar (zero new Office API, works offline) may matter more to the owner
than rich-body links. This needs the owner's decision before either is built; it is explicitly NOT
built here per the task's instruction.

## Deviations from the exact file list in the POML

The POML's `<relevant-files>` named `App.tsx`, `sendEmailService.ts`, `IHostAdapter.ts`,
`WordAdapter.ts`, `OutlookAdapter.ts` as the surfaces to touch. Two additional files needed a minimal
edit that were not in that list:

- **`shared/adapters/types.ts`** — the actual `HostCapabilities` interface + the new
  `EmailComposeContent`/`ComposeEmailResult` types live here, not in `IHostAdapter.ts` (confirmed by
  the task's own "Context the POML does not have" note, which flags this exact discrepancy for
  `canOpenBrowserWindow`'s precedent).
- **`src/client/office-addins/outlook/OutlookHostAdapter.ts`** — a legacy, unused duplicate Outlook
  adapter (superseded by `shared/adapters/OutlookAdapter.ts` per the 2026-09-09 task-010 consolidation
  decision recorded in the project `CLAUDE.md`). It still implements `IHostAdapter` and is compiled by
  `tsc`, so extending the interface with `composeNewEmail` broke its conformance (0 production errors
  is a hard gate). Fixed with the minimal mirror of `WordAdapter`'s "not supported" pattern. Confirmed
  via `grep -rn "OutlookHostAdapter"` that the only other reference to this class anywhere in the
  package is an illustrative example inside a doc comment in `HostAdapterFactory.ts` — it is not
  constructed anywhere in the live app. Deleting this dead file is out of scope for this task; flagging
  it here as a candidate for a future cleanup task.

Jest mocks (`jest.setup.js`, `shared/__mocks__/office-js.ts`) were also updated to add
`displayNewMessageForm: jest.fn()` to the mocked `Office.context.mailbox` — required for both the new
adapter tests and to keep the mock context realistic for any future test exercising this path; these
were anticipated by the task's own context note ("the office-js mocks... plus the office-js mocks").

## Gates

- `npx tsc --noEmit`: **111 errors total, 0 production** (matches the stated baseline exactly; my 3
  new test files contribute 0 errors).
- `npm run build` (with placeholder env vars): **exit 0**.
- Full `npx jest --ci`: **10 failing suites / 84 failing tests** — exactly the ten named in scope
  (OutlookAdapter, ApiClient, EntityPicker, SaveFlow, TaskPaneNavigation, TaskPaneShell, SaveView,
  ShareView, useEntitySearch, useSaveFlow). My 3 new suites are NOT among the failures.
- All 29 `ci-gated-suites.txt` paths: **all green** (394/394 tests).
- New suites (all green, recommended for gating):
  - `shared/adapters/__tests__/OutlookAdapter.composeEmail.test.ts`
  - `shared/adapters/__tests__/WordAdapter.composeEmail.test.ts`
  - `shared/taskpane/services/__tests__/sendEmailService.test.ts`

## Self-run Step 9.5 findings (code-review + adr-check) and how addressed

1. **(Fixed)** `OutlookAdapter.composeNewEmail` used `this._mailbox?.displayNewMessageForm(...)` —
   optional chaining meant a null `_mailbox` (in principle unreachable given `_currentMode` and
   `_mailbox` are always set together in `initialize()`, but not guaranteed by the type system) would
   silently no-op and still return `{ success: true }`. Changed the guard to also require
   `this._mailbox` truthy, returning the same defined failure result instead.
2. **(Fixed)** The record link's `typeLabel` was not wired from `App.tsx` — it fell back to the raw
   Dataverse logical name (`sprk_matter`) instead of the friendly label ("Matter") already available as
   `savedContext.regardingEntity`. Fixed by threading it through.
3. **ADR-044**: verified every GUID (`documentId` into the share-link route, `record.id` into the
   record URL, `entity.id` in `handleSaved`, `docId` in `onComplete`) is canonicalized via `cleanGuid`
   before crossing its respective boundary. No violations.
4. **ADR-021**: all new UI (`Button`, `MessageBar`, `MessageBarBody`, `Spinner`) is
   `@fluentui/react-components` (v9); all styling uses `tokens.*`, no hex literals.
5. **ADR-012**: no new import of `@spaarke/ui-components`; `sendEmailService.ts` consumes only
   `@shared/services` (the existing `apiClient`/`ApiClientError`) and the package-local
   `cleanGuid`/`openRecordLauncher`.
6. **ADR-028**: no raw `fetch` with a hand-built `Authorization` header, no token bridge, no MSAL
   construction — `sendEmailService.ts` routes through the existing `apiClient` (which already goes
   through `authenticatedJsonFetch` / `AuthService`).
7. **NFR-10**: confirmed by grep — no `hostType ===` / `getHostType() === 'word'` conditional was added
   to any view for Send Email; visibility is entirely `hostAdapter.getCapabilities().canComposeEmail`
   driven.
8. **NFR-11**: the button is a standard Fluent v9 `Button` (native keyboard reachability + visible
   focus ring, the established pattern elsewhere in this codebase e.g. `RelatedRecordCard`'s Open
   button); success/failure are announced via `useAnnounce`, with its `liveRegion` rendered in the
   component tree per that hook's documented contract.
9. **No Spaarke email modal was created** — confirmed by diff: `sendEmailService.ts` has no dialog/modal
   rendering of any kind, only pure link-minting/body-building logic; `App.tsx`'s Send Email UI is a
   single button + inline error `MessageBar`, not a modal.

## Escalations fired

None of the three POML `<escalation><trigger>` items required a STOP-and-escalate:

- Trigger 1 (expiry bounds too short for email recipients): the default organization-scoped expiry is
  **14 days** (`ShareLinkOptions.MaxLifetimeDays`, unchanged, not touched by this task) — reasonable
  for an email recipient to open. Not fired.
- Trigger 2 (Word cannot open a compose window): fired as **documented, not blocking** — per the task's
  explicit instruction, this is reported as a finding needing owner confirmation (see "Word finding"
  above), not a STOP. Implementation proceeded with the capability-absent/hidden-affordance behavior as
  instructed.
- Trigger 3 (requiring a Spaarke-rendered email modal): not fired — no such requirement surfaced;
  confirmed no modal was built (see code-review point 9 above).

## What remains unverified

The six Send Email `<ui-tests>` in the POML (both-links, document-only, capability gating live-render,
minting-failure-honestly, dark-mode, keyboard+announcement) require a live Office host (Outlook
desktop/web) to exercise `Office.context.mailbox.displayNewMessageForm` for real — this cannot be
verified from a headless jest run or a static build. Unit tests cover the same logic paths
(`sendEmailService.test.ts`'s both-links/document-only/record-only/minting-failure cases,
`OutlookAdapter.composeEmail.test.ts`'s capability + compose-call assertions) but do not prove the
actual Outlook compose window opens correctly with rendered links, correct dark-mode styling, or that
screen-reader announcements are audible. This needs manual verification in a deployed environment.
