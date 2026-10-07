# Task 096 — Word "Email" tab from the shared compose components; no Send Email path uses a sharing link

> **Task**: `tasks/096-email-tab-shared-compose-components.poml` (UAT round 4 items 6-8 + the Send Email defect,
> `notes/042-uat-round4-2026-10-04.md` §1, §3).
> **Executed**: 2026-10-04, Opus subagent, wave UAT4-W1 beside task 095 (same worktree). No git operations, no
> `TASK-INDEX.md` / `current-task.md` edits, no real email sent, no Dataverse write.

## 1. What shipped

| Area | Change |
|---|---|
| **Email tab (Word)** | New tab after Find: **Save · To Do · Find · Email**. Gated on a new capability `HostCapabilities.canEmailFromPane` (Word `true`, Outlook `false`) through `TabConfig.requiresCapability` in `getAvailableTabs(hostType, capabilities)` — NFR-10, never `hostType`. Fails closed (no capabilities → no tab). |
| **The form** | `components/views/EmailView.tsx`: a thin container over the **shared compose engine** `EmailComposer` (`@spaarke/ui-components` — the one the Spaarke email page mounts via `SendEmailPage`), mounted through a new shared pane wrapper **`SendEmailPane`** (see §2). It pre-fills: Subject = the document name; Body = "Please see the attached document." + the Spaarke **record** link (the related record; the document's own record when unfiled); the document as the single, selected attachment; the related record as the association. Send is locked to the user's mailbox (`sendMode="user"`, From line shows the signed-in address); `archiveToSpe={false}`; `attachmentSources={[]}` (no add-file affordances that could not be sent). |
| **Outcome UI** | Success → "Email sent" + **Open in Spaarke** (the `sprk_communication`, via `openRecord`; gated on `canOpenBrowserWindow` + `ORG_URL`) and the form resets (engine remounted by key). Refusal (any non-2xx) → "Email not sent: {server ProblemDetails detail}", form kept. 2xx without `communicationId` → "Email sent, but not recorded … Do not send it again." No response / no token → "Email may not have been sent — no response from Spaarke ({reason}). Check your Sent Items before sending again." |
| **Not yet in Spaarke** | No `documentId` → info bar "Save to Spaarke first" with a button that switches to the Save tab; the composer (and so Send) is not rendered. While identity resolution is running → a spinner. A FAILED identity check (error / denied / conflict / indeterminate) → "Could not confirm this document" + Go to Save — never "not in Spaarke yet". |
| **Wiring** | `services/paneEmailService.ts` (pure): subject/body/attachment/association builders, contact → recipient mapping (`"Name (email)"`, contacts without email dropped), and the injected `authenticatedFetch` (`authenticatedJsonFetch` + one 401 retry; observes the two outcomes the engine does not report — transport failure, 2xx without a record id). |
| **Task 086's Word row** | Removed from `App.tsx` (with its now-unused `openRecordLauncher.openUrlInBrowserWindow`) (the "Send Email" menu above the tabs with Spaarke email / Outlook on the web). Outlook's native Send Email button is unchanged in placement and gating. |
| **Sharing link removed** | `sendEmailService.prepareSendEmail` (Outlook native compose) now builds the **document's Spaarke record link** (`etn=sprk_document`) instead of minting a Graph sharing link; composing makes no network call. `shareLinkService.ts` and its suite were deleted — no client code path references `mintDocumentShareLink` or the share-link route. The Word two-choice helpers (`buildSpaarkeComposeUrl`, `buildOutlookWebComposeUrl`, `prepareWordSendEmailChoice`) were removed with the row. |

## 2. Which shared components, and how they were made consumable

**Finding that changed the plan.** The POML named `Spaarke.Communication.Components` (`EmailRecipients`,
`EmailBody`, `AttachmentList`, `EmailComposeActions`, `EmailAssociationsAndTracking`) as "the Spaarke email page's
composer pieces". Read, they are the **reading pane's** components: `EmailRecipients` is "purely presentational,
read-only"; `EmailBody` is `EmailBodyView` (renders a received email); `AttachmentList` lists an existing
communication's attachments; `EmailComposeActions` is a hook that opens `SendEmailDialog` from
`@spaarke/ui-components`; `EmailAssociationsAndTracking`'s `EmailConnectionsReview` calls `getXrmForPicker` (host-bound).
None is an input form. Following the POML's own instruction — "find their consumer … for how they are assembled" —
the email page (`CommunicationPage/src/components/EmailComposerSlot.tsx`) mounts **`SendEmailPage` →
`EmailComposer`** from `@spaarke/ui-components`. That engine is the form the owner sees in Spaarke, so it is what the
Email tab uses. No component was copied.

**Dependencies checked (the escalation trigger did not fire).** The import closure of the engine + wrapper is 22
files with **no Xrm / model-driven dependency** (Xrm lookups are host-injected callbacks the pane does not pass).
Third-party runtime imports: `react`, `@fluentui/react-components`, `@fluentui/react-icons`, `lexical` + `@lexical/*`.
React 19.2.6 and Fluent 9.72.9 in the add-in ≥ the library's 19 / 9.68 — compatible.

**ADR-045 compliance (Path C).** ADR-045 requires every email-send UX to go through a wrapper over the one engine
and says "if a new mount emerges, add a new thin wrapper". None of the three fits a pane (`SendEmailPage` = page
chrome with a close ×, no `sendMode`/`onError`; `SendEmailDialog` = modal; `SendEmailStep` = wizard step, no
`onSent`/`onError`). Added **`SendEmailPane`** to the shared library
(`Spaarke.UI.Components/src/components/EmailComposer/wrappers/SendEmailPane.tsx`, exported from the EmailComposer
barrel): locks `mount="inline"`, defaults `mode` to compose, forwards every other engine prop. Tested in the shared
library's own `wrappers.test.tsx` (+4 tests).

**How the add-in consumes it (ADR-012 Path A is kept narrow).**
- **Runtime**: an exact (`$`) alias `@spaarke/ui-components/send-email-pane` → the wrapper SOURCE file in
  `webpack.config.js` and `jest.config.js` — same mechanism as the existing `provenance` alias. Never the
  `@spaarke/ui-components` barrel (that would pull the library's Xrm-bound components — the reason for the project's
  ADR-012 Path A exception).
- **Bare-import resolution**: `webpack resolve.modules = [<add-in>/node_modules, 'node_modules']` and jest
  `modulePaths: ['<rootDir>/node_modules']`, so the shared source binds to the add-in's single React / Fluent /
  lexical (the shared lib has no node_modules in CI).
- **Packages**: `lexical` + `@lexical/{html,link,list,react,rich-text,selection,utils}` **0.41.0** (exact; the
  version the shared library is developed and tested against) added to the add-in; the lockfile also moved
  `@floating-ui/*` within semver (shared with Fluent positioning).
- **Types**: `shared/types/spaarke-send-email-pane.d.ts` — an ambient declaration of exactly the props the tab passes,
  with the library's names and shapes (precedent: `external-spa/src/types/sdap-client.d.ts`). Why not type-check the
  source: under the add-in's stricter flags (`exactOptionalPropertyTypes`, `noUncheckedIndexedAccess`) the engine's
  files produce 56 errors (none a runtime defect) and its type graph reaches `EntityCreationService.ts` →
  `@spaarke/sdap-client`, which the add-in does not install; CI's production-typecheck job counts every non-test
  error. `EmailView.test.tsx` renders the REAL wrapper + engine with exactly these props and asserts the request, so
  shim drift fails a test.
- **Decision to record in the project CLAUDE.md** (main session): ADR-012 Path A is narrowed for this one surface —
  the add-in consumes `SendEmailPane` (and its engine closure) from `@spaarke/ui-components` source by exact alias;
  the barrel stays off-limits.

## 3. What a User-mode send records (from code; no live send)

Traced in `CommunicationService.cs` / `EmailChannelSender.cs` / `CommunicationAuthorizationFilter.cs`:

- **Filter**: authenticated + an `oid` claim; no role, scope, Dataverse-user or approved-sender check
  (`SendAsUserAsync` skips `ApprovedSenderValidator`). The pane's delegated token passes, as it does on `/api/office/*`.
- **Send**: OBO → `graphClient.Me.SendMail` with `SaveToSentItems = true` (lands in the user's **Sent Items**). From =
  the user's email claim (`email`/`preferred_username`/`upn`; none → 400 `USER_EMAIL_NOT_FOUND`).
- **Attachment**: each `attachmentDocumentIds` id → Dataverse row (app identity) → SPE file downloaded **app-only** →
  Graph `FileAttachment`. Limits 150 files / 35 MB → 400 `ATTACHMENT_LIMIT_EXCEEDED`; no file → 422
  `ATTACHMENT_MISSING_SPE_REF`.
- **Record**: AFTER the Graph send, best-effort, a `sprk_communication` row: type Email, direction Outgoing, status
  Send, to/cc/from/subject/body, `sprk_sentby`, `sprk_hasattachments`/`sprk_attachmentcount`, and the regarding from
  `associations[0]` by **logical name** (`sprk_matter` → `sprk_regardingmatter`, plus `sprk_regardingrecordname/id/url`);
  one `sprk_communicationattachment` per document (lookup to the `sprk_document`). Response `communicationId` =
  that row. If the row fails, the email is still sent and the response carries `communicationId: null` with no
  warning field — the pane reports that case as "sent, but not recorded".
- **Missing Mail.Send consent** surfaces as **500 `GRAPH_SEND_FAILED`** ("Unexpected error (OBO): …"), not 401/403 —
  the pane shows the server's text.

## 4. The share-link defect — recommendation (for the main session)

`POST /api/documents/{id}/share-link` (`FileAccessEndpoints.cs:130`, OBO Graph `createLink`) cannot succeed for any
Spaarke document: every one lives in a SharePoint Embedded container, and Graph refuses item sharing links there
("This sharing scenario is not supported on CSP Container site"). The add-in no longer calls it. Its other consumer
is the shared composer's `onResolveShareLink` (wired by `createXrmEmailComposeHandlers` → the Email code page, the
SpaarkeAi email widget, the TrackingFieldTrio PCF): when an author ticks **Link** on an attached document, the engine
asks for a sharing link and, on failure, silently keeps the document's internal URL — so recipients outside Spaarke
get a link they cannot open, with no message to the author.

Recommendation (owner rule 2026-09-21 — fixed in this project): (1) have the route return a typed, honest refusal
for SPE files (a ProblemDetails code such as `SHARE_LINK_UNSUPPORTED_SPE`, 409/422) instead of relaying the Graph
error, and make `onResolveShareLink` return the Spaarke document record link (the same link Outlook's Send Email now
uses) so the body link is at least openable by Spaarke users; (2) decide with the owner whether external
recipients need file access at all — if so, the supported route on SPE is container-level access / the external
access platform, not item links (confirm current SPE sharing support with the `researcher` agent before building);
otherwise retire the route and the composer's **Link** toggle for SPE documents. Both are BFF/shared-library work,
outside this task's client scope.

## 5. Security findings (🔔 for the owner / main session)

- **The send route does not authorize the attachment read.** `AttachmentDocumentIds` are resolved and downloaded
  **app-only**, with **no check that the caller can read the document** — any authenticated user who knows a
  `sprk_document` id can email that file to anyone. This contradicts the POML's security constraint ("the server
  authorizes the send, the attachment read and the association"). The pane only ever sends the id of the user's own
  resolved document, so the pane is not the exposure — the route is, for every caller. Recommended fix: before
  attaching, verify read access per document with an impersonated (caller) Dataverse read — the same mechanism the
  Office search routes use (task 062) — and refuse with 403 per document. Not implemented here: a security change
  to a shared route needs owner sign-off (root CLAUDE.md §6).
- **Associations are not authorized either**: the regarding stamp is written with the app identity from whatever
  `associations[0]` names. Same recommendation (impersonated read of the record before stamping).
- **`archiveToSpe: true` has a server bug** (outbound archival writes the `sprk_document` GUID into
  `sprk_graphitemid`, `CommunicationService.cs` ~2330-2343). The pane sends `false`; the shared engine **defaults
  `archiveToSpe` to true**, so the Spaarke email page and other engine hosts exercise that path — worth a defect.

## 6. Acceptance criteria

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | Word tabs Save · To Do · Find · Email; 086's row gone; Outlook tabs unchanged, native compose | **PASS** | `TaskPaneNavigation.test.tsx` (order, capability gate, fail-closed, live toolbar Word/Outlook); `capabilities.test.ts` (Word `canEmailFromPane` true, Outlook false); App.tsx row is `canSendEmail` (Outlook `canComposeEmail`) only; `sendEmailService.test.ts` `resolveSendEmailAffordance` (Word → none, Outlook → native) |
| 2 | Form composed of the shared compose components (test/import check) | **PASS** (deviation §2) | `EmailView.test.tsx` asserts the alias resolves to `Spaarke.UI.Components/…/wrappers/SendEmailPane.tsx` and renders the engine's own regions (Email composer, From, To, Cc, Subject, Attachments (1), Related to (1)); the shared pieces are the email page's engine, not the reading-pane components the POML named |
| 3 | To (search with email + free-typed), Cc, Subject = doc name, Body = record link, attachment shown, related record shown | **PASS** | `EmailView.test.tsx` (recipient search shows each contact's email; free-typed address committed; subject value; Attachments (1) + "Attached: …docx"; Related to (1)); `paneEmailService.test.ts` (body link) |
| 4 | Send → `POST /api/communications/send` with `SendMode: User`, `AttachmentDocumentIds: [documentId]`, `Associations` (exact request) | **PASS** | `EmailView.test.tsx` "posts the exact request…" (url, POST, bearer, `sendMode:"User"`, `attachmentDocumentIds:[DOC]`, `associations[0]` logical name/id/name/url, `archiveToSpe:false`, no token in body); seeded a defect (attachment not selected) → test failed |
| 5 | Success → "Open in Spaarke" for the communication; form resets | **PASS** | `EmailView.test.tsx` success test (openRecord `sprk_communication` + id; To cleared, subject re-prefilled); NFR-10 test (no button without `canOpenRecord`) |
| 6 | Not in Spaarke → explain + "Save to Spaarke first"; no Send | **PASS** | `EmailView.test.tsx` (no composer, no Send, button calls `onGoToSave`, no fetch); checking → spinner; failed check → "Could not confirm", never "new" |
| 7 | Refused send (403/400/500) shows the server's reason; nothing reported sent | **PASS** | `EmailView.test.tsx` parametrized 403/400/500; plus 2xx-without-id and network-failure cases |
| 8 | No client path calls `mintDocumentShareLink`; Outlook body carries the Spaarke link (test) | **PASS** | module deleted (grep: no reference in `src/client/office-addins`); `sendEmailService.test.ts` (document link = `etn=sprk_document`; no share-link call, no network call; no SharePoint URL in body) |
| 9 | Gates | **PASS** | §7 |
| 10 | Live: send to yourself from Word | **OPEN** | needs a deploy + a live Word host; not attempted (no real email allowed in this run) |

## 7. Gates

| Gate | Result |
|---|---|
| Gated jest (`ci-gated-suites.txt`, 75 paths after this task; was 74) | **75/75 suites, 1031/1031 tests** green (final run, after all review fixes). Includes 095's in-progress suites, which were green at the time |
| Shared library `wrappers.test.tsx` (run with a temporary harness over the add-in's node_modules — the shared lib has none installed here; CI's own shared-lib job is the authoritative run) | 14/14 (10 existing + 4 new `SendEmailPane`) |
| `npm run lint` | 0 errors / 0 warnings; Prettier clean on every task-096 source file |
| `npm run typecheck` | **68 total, 0 production** — equals CI's pinned `ACCEPTED_TEST_DEBT_LINES: '68'` |
| `SendEmailPane.tsx` under the shared library's own compiler settings | 0 errors in the file (1 environmental: `@spaarke/sdap-client` dist not built locally) |
| Webpack production build (CI env values) | Run twice — once on the first alias, once after the scoped-resolve change (the second was needed to prove the `Rule.resolve` config; the dispatcher allowed one). Both exit 0, only the pre-existing size advisories. `vendors.bundle.js` 1,326,841 B (identical across both, so a single React); the engine + wrapper in a 221,928 B shared chunk; `word/taskpane.bundle.js` 6,407 B. Pre-096 baseline NOT measured — the main session's build owns the delta (lexical 0.41 + the 22-file closure are new) |
| BFF | **unchanged** — no `dotnet` change, no publish-size delta |

## 8. Step 9.5 quality gates

**adr-check** (main agent): ADR-021 ✓ (Fluent v9, tokens only, no hex/rgb, decorative icons `aria-hidden`) ·
ADR-028 ✓ (the shared lib receives a fetch function, never a token; the add-in's `authenticatedJsonFetch` attaches
it; no `accessToken` prop) · ADR-044 ✓ (`cleanGuid` on the document id, record id and association id) · NFR-10 ✓ (no
`hostType` branch; capability gate) · ADR-038 ✓ (new suites in KEEP paths, gated; no banned patterns) · ADR-012 —
Path A narrowed for one surface (§2, record in project CLAUDE.md) · **ADR-045 — found a deviation and pivoted to
comply (Path C)**: the first cut mounted the engine directly; ADR-045 says every send UX goes through a wrapper and a
new mount gets a new thin wrapper → added `SendEmailPane`.

**code-review** (independent subagent, coverage-first): 1 Critical (server, pre-existing), 9 Warnings, 9 Suggestions.

| Finding | Action |
|---|---|
| C1 attachment read + association are app-only with no caller-access check (server) | **Escalated** (§5). Reviewer notes `SpeFileStore.DownloadFileAsUserAsync` already exists (`SpeFileStore.cs:137`) — the natural fix |
| W1 AC2 deviation + missing note | This note (§2) |
| W2 shared-lib file outside the POML file list | Justified below; shared tests 14/14 |
| W3 a FAILED identity check said "not in Spaarke yet" | **Fixed**: `identityStatus: 'unconfirmed'` → "Could not confirm this document" + Go to Save; test added |
| W4 subject/attachment fell back to generic text after a first save | **Fixed**: `App.onComplete` keeps the saved file name from the stored URL (`fileNameFromWebUrl`) |
| W5 Outlook Send Email silently no-op'd with ORG_URL unset | **Fixed**: explicit error message |
| W6 `openUrlInBrowserWindow` dead after removing 086's choice | **Removed** with its 2 tests; header comment corrected |
| W7 server `detail` can carry internal exception text | Server-side; noted with C1 |
| W8 global `resolve.modules` override | **Fixed**: scoped to requests issued by the shared source (`module.rules[].resolve`); build re-verified |
| W9 bundle size not measured | Absolute sizes above; delta owed to the main session's build |
| S2 stale pre-fill if document/record changes while mounted | **Fixed**: composer `key` includes document + record ids |
| S3 outcomes announced twice (live region + MessageBar role) | Kept — same pattern as `CreateTodoView` (task 091 precedent) |
| S4 "not sent" can be wrong after a dropped connection | **Fixed**: "may not have been sent … check your Sent Items" |
| S6 inline `[]` defeats the engine's memo | **Fixed**: module constant |
| S7 title not a heading | **Fixed**: `as="h2"` |
| S8 pre-existing `mergeClasses` warning in `TaskPaneNavigation` | **Fixed** |
| S1 shim drift caught only at runtime; S5 clear-on-send ordering; S9 long comments | Accepted (S1: reviewer verified every declared prop matches today; `EmailView.test` is the guard) |

**§11 justification — `SendEmailPane` (new shared component).** *Existing*: `SendEmailStep`/`SendEmailDialog`/
`SendEmailPage` (`EmailComposer/wrappers/`). *Extension*: none fits — `Page` locks page chrome and hides
`sendMode`/`onError`, `Dialog` is a modal, `Step` is a wizard step without `onSent`/`onError`; widening `Step` would
change a wizard contract. *Cost of doing nothing*: the pane would mount the engine directly, which ADR-045 forbids
("a new mount gets a new thin wrapper"), or reuse `SendEmailPage`, whose close × does nothing in a task pane.

## 9. Deviations from the POML (directional mode)

1. Shared components = the email page's `EmailComposer` (+ new `SendEmailPane` wrapper), not the reading-pane
   components the POML listed (§2).
2. A shared-library file was added (`SendEmailPane.tsx`) and the EmailComposer barrel extended — required by ADR-045
   for a new mount.
3. `TaskPaneShell.tsx`, `adapters/types.ts`, `WordAdapter.ts`, `OutlookAdapter.ts`, `openRecordLauncher.ts` (+test),
   five view-test fixtures and `package.json`/lock were touched beyond the POML's file list (capability + dependency plumbing). None is a 095 file.
4. `shareLinkService.ts` deleted with its gated suite (documented in `ci-gated-suites.txt`).
