# Task 086 — Word Send Email choice (FR-15 amended) + focused record open (FR-10 amended)

> **Task**: `086-word-send-email-choice-and-focused-record-open.poml`
> **Executed**: 2026-10-02, as a background Sonnet-5 subagent in the shared worktree
> `spaarke-wt-spaarkeai-word-add-in-r1`, in parallel with the main session's task 076. Office-addins-only
> per the orchestrating session's boundaries — no git state changes, no `TASK-INDEX.md` / `current-task.md`
> edits, Steps 10/10.6/11 and devops sync left to the main session.

## What was built

### FR-10 — focused record open (`navbar=off`)

`shared/taskpane/services/openRecordLauncher.ts`:
- `buildOpenRecordUrl` now appends `&navbar=off` unconditionally. `cmdbar` is left alone (not set to
  `false`) so Save and the record's own actions stay available, per the amended FR-10 text. This is the
  SAME builder `SaveFlow.tsx`'s "Open" affordances and `sendEmailService.ts`'s record link both already
  call — one change, three consumers, no new URL-building shape.
- Added `openUrlInBrowserWindow(url, opener?)`, a generic version of the file's existing (unexported)
  `defaultOpener` — exposed so the Word Send Email choice (below) opens its two destinations through the
  SAME `Office.context.ui.openBrowserWindow` mechanism task 027 established, not a second one.

### FR-15 — the Word Send Email choice

`shared/taskpane/services/sendEmailService.ts` — extended, not forked:
- Extracted the existing `prepareSendEmail`'s link-resolution logic (mint the document share link, build
  the record deep link) into a shared internal `resolveSendEmailLinks`, now called by BOTH
  `prepareSendEmail` (Outlook, unchanged behavior) and the new `prepareWordSendEmailChoice` (Word). This
  is the mechanism that satisfies the task's "do not mint a link a second way" constraint — it is
  structural, not just a stated intention.
- `buildSpaarkeComposeUrl` — builds `{orgUrl}/main.aspx?pagetype=webresource&webresourceName=
  sprk_communicationpage&data=<encoded mode=compose&subject=...&body=...&associatedTo=...>`, mirroring
  `createTodoLauncher.ts`'s `sprk_smarttodo` shape. Body is HTML (same `buildComposeBody` the Outlook path
  already uses — the Communication page's `initialBody` feeds `EmailComposer`, which defaults to HTML
  when the URL contract carries no explicit format).
- `buildOutlookWebComposeUrl` — builds the documented
  `https://outlook.office.com/mail/deeplink/compose?subject=...&body=...` deep link, PLAIN TEXT body
  (`buildPlainTextComposeBody`, a new `label: url`-per-line builder) since the deep link has no HTML
  parameter.
- `prepareWordSendEmailChoice` — orchestrates both: resolves links once via `resolveSendEmailLinks`, then
  builds both destination URLs. `spaarkeUrl` is `null` when `ORG_URL` is unset (the Spaarke composer is a
  Dataverse page and cannot be addressed without it); `outlookWebUrl` is always built when there is
  something to link, since it never depends on `ORG_URL`.
- `resolveSendEmailAffordance(capabilities, hasSomethingToLink)` — the single pure gating function:
  `'outlook-native' | 'word-choice' | 'none'`. Kept here (not inline in `App.tsx`) so the NFR-10 gating
  rule has exactly one tested definition.

`shared/taskpane/App.tsx`:
- Extracted `buildSendEmailRelatedRecordInput(savedContext)` (module-level, pure) so the "friendly type
  label" rule is defined once and used by both the existing `handleSendEmail` (Outlook) and the new
  `handleSendEmailChoice` (Word).
- Added `handleSendEmailChoice('spaarke' | 'outlookWeb')`: calls `prepareWordSendEmailChoice`, then opens
  the chosen URL via `openUrlInBrowserWindow`. A `spaarke` choice with `spaarkeUrl: null` (ORG_URL unset)
  surfaces an inline error rather than opening a broken link — the same "degrade, never break" posture the
  rest of this pane already uses for ORG_URL-dependent features.
- Replaced the single `canSendEmail` boolean with `sendEmailAffordance =
  resolveSendEmailAffordance(capabilities, hasSomethingToLink)`, and render either the existing single
  Button (`'outlook-native'`) or a new Fluent v9 `Menu`/`MenuTrigger`/`MenuPopover`/`MenuList` with two
  `MenuItem`s — "Spaarke email" and "Outlook on the web" — for `'word-choice'`. Neither renders for
  `'none'` (negative acceptance criterion: no affordance, never disabled).

## A real bug found and fixed during this task's own verification

While writing the round-trip test for `buildSpaarkeComposeUrl`, I hand-traced the EXACT decode sequence
`CommunicationPage/src/services/parseParams.ts`'s `unwrapDataEnvelope` runs:
`outer.get('data')` (one automatic decode) → an **unconditional** `decodeURIComponent` (a second,
explicit decode) → `new URLSearchParams(...)`. My first implementation built the `data=` value with only
ONE encode pass (`outer.searchParams.set('data', inner.toString())`), relying on the outer URL's own
serialization for that single pass. That survives the real decode sequence only when no field value
contains a literal `&` or `=` — and this URL's own embedded record link (`main.aspx?etn=...&id=...&
pagetype=...&navbar=off`, now FOUR params) is exactly such a value. A premature `decodeURIComponent` on an
escaped `&` resolves it to a literal `&` one step before the final `new URLSearchParams` parse, which then
reads it as a real pair delimiter and truncates the subject/body at that point — a silent, intermittent
pre-fill corruption that would only show up with "real" content (a record URL, or a subject like
"Smith & Jones"), not with simple test fixtures.

Fixed by explicitly double-encoding: `outer.searchParams.set('data', encodeURIComponent(inner.toString()))`
— this supplies the matching second encode pass the decode side performs. Verified with a test that
reproduces the real decode algorithm inline (`decodeSpaarkeComposeUrl` in `sendEmailService.test.ts`,
sourced from `parseParams.ts`, not assumed) and a case containing an embedded `&`
(`'Re: Smith & Jones — review'`, and the four-param record URL). This is recorded here because it is
exactly the kind of defect a hand-trace alone would not have caught with simpler fixtures — the fix is
real and tested, not theoretical.

## Deviations from the POML

- **`<relevant-files>` named `SaveFlow.tsx` as an edit target for the Send Email choice.** The Send Email
  affordance (button, state, `handleSendEmail`) actually lives in `App.tsx`, not `SaveFlow.tsx` — this was
  true before this task too (task 036 built it there; `SaveFlow.tsx` owns the SEPARATE "Open record" /
  "Open document record" affordances from task 027, which is probably what the POML's file list was
  conflating). Verified by `grep -rn "Send Email\|handleSendEmail\|canComposeEmail" shared/taskpane` before
  writing anything. All Send Email changes went into `App.tsx`; `SaveFlow.tsx` was not touched by this
  task (it already carries the FR-10 `navbar=off` behavior transitively, via `openRecordLauncher`, with no
  edit needed there).
- **Gating test**: the POML constraint calls for a test of "the gating (Word shows the choice; Outlook
  unchanged; no `canOpenBrowserWindow` ⇒ no choice)". `App.tsx` has no existing component-level test (task
  036 didn't add one for the Outlook button either — it tested gating at the capability/service level).
  Mounting the full `App.tsx` (auth, host adapter, multiple tabs) in RTL for this one behavior would be a
  disproportionate new test surface with no existing precedent in this package. Instead,
  `resolveSendEmailAffordance` was extracted as the single pure gating function and is what `App.tsx`
  renders from — its 4 unit tests in `sendEmailService.test.ts` cover exactly the three POML-named
  scenarios (plus the "nothing to link" case) without mounting the component. This is the same proportion
  of effort task 036/027 already established for this package.

## Escalation triggers — neither fired

1. **"The Communication page cannot be opened in compose mode with pre-fill from a plain browser URL."**
   Did not fire. Verified by reading (not assuming) three files:
   - `CommunicationPage/src/index.tsx`: the page's bootstrap is `parseFromLocation(window.location.search)`
     → `@spaarke/auth`'s `resolveRuntimeConfig()`/`initAuth()` (function-based, independent of `Xrm`) →
     render. Nothing in this path requires the Dataverse model-driven-app chrome.
   - `CommunicationPage/src/services/parseParams.ts`: `unwrapDataEnvelope` reads the `data=` envelope (or
     falls back to flat top-level params if absent) — both are plain URL mechanics, not Xrm-dependent.
   - `CommunicationPage/src/App.tsx`: for `mode=compose`, `useCommunicationRecord(params.id)` runs with
     `id` undefined (compose is not in `MODES_REQUIRING_ID`) and renders `CommunicationLayout` directly —
     no record read is required to reach the composer.
   - ⚠️ *Corrected by the main session on review (2026-10-02).* This is **not** the mechanism
     `createTodoLauncher.ts` uses — that launcher appends FLAT params to a configured
     `SMARTTODO_CODEPAGE_URL` base (`buildCreateTodoLaunchUrl`, `url.searchParams.set(...)`), not
     `main.aspx?...&data=`. The task POML's background had stated the opposite; the claim was inherited from
     it. The `main.aspx?pagetype=webresource&webresourceName=sprk_communicationpage&data=<envelope>` form
     used here is still the right one for `main.aspx`: Dataverse forwards only the `data` parameter to a web
     resource, and the page's own header documents the `data=` envelope as its contract. But it has **no
     shipped precedent in this package** — the first live open (acceptance criterion 8) is its first
     verification. Fallback if it misbehaves live: the page also reads flat top-level params when no `data`
     is present (`parseParams.ts:27-40`), i.e. the `/WebResources/sprk_communicationpage?mode=compose&...`
     shape the To Do launcher uses.
   - Also live-only: in Word **on the web**, `openBrowserWindow` runs after an `await` (the share-link mint),
     so a browser popup blocker could refuse the tab if the mint is slow (user activation expires after a few
     seconds). Word desktop opens the default browser and is unaffected. Watch for it in criterion 8.
2. **"The Outlook-on-the-web deep link truncates or rejects a body carrying both links."** Not measured
   live — no live Outlook-web host is reachable from this environment (same limitation noted in task 036's
   own report for a related question). Not fired on the only evidence available: the composed plain-text
   body for a realistic case (one document link + one record link, both labeled) is a few hundred
   characters — well under commonly-cited URL length ceilings (practical browser limits are several KB;
   the old IE 2083-character ceiling is itself far above this body's length). This is recorded as an
   **estimate, not a verification** — if the owner wants it confirmed, it needs a live Outlook-web open,
   which is acceptance criterion 8's territory (see below).

## Gates

| Gate | Result |
|---|---|
| Gated jest (62 suites, `ci-gated-suites.txt`) | **62/62 suites, 836/836 tests green.** Before this task's additions: 817 tests (62 suites — no new suite FILE was added; `openRecordLauncher.test.ts` and `sendEmailService.test.ts` were already gated from tasks 027/036 and gained tests in place). This task added 19 new tests across those two files (16 in `sendEmailService.test.ts`, 3 in `openRecordLauncher.test.ts`). `SaveFlow.openRecord.test.tsx` needed no changes — it mocks `openRecordLauncher` entirely, so it is insulated from the URL-shape change. |
| `npm run lint` | Exit 0, zero warnings/errors (`--max-warnings 0`). |
| `npm run typecheck` | **68 total errors, 0 production, 68 test-file-only** — matches the pinned baseline exactly (see note below for the one production regression caught and fixed en route to this number). |
| `npm run build` | Exit 0 (placeholder env vars: `ADDIN_CLIENT_ID`/`TENANT_ID`/`BFF_API_CLIENT_ID`/`BFF_API_BASE_URL`, matching task 036's precedent). |

**Typecheck note**: the FIRST typecheck run after the initial implementation showed 70 total / **2
production** errors — `App.tsx`'s `buildSendEmailRelatedRecordInput` had its return type derived via
`Parameters<typeof prepareSendEmail>[0]['relatedRecord']`, which (because the source property is
*optional*) widens to include `| undefined`, and `exactOptionalPropertyTypes: true` then refuses an object
literal that explicitly supplies a key typed to include `undefined` where the target declares the key
optional. Fixed by declaring the helper's return type as the concrete `SendEmailRelatedRecordInput | null`
(imported from `sendEmailService.ts`) instead of deriving it indirectly. Re-ran: **68 total / 0
production**, matching the pinned baseline exactly. Reported here so the number in this file is the
VERIFIED final state, not the first (wrong) measurement.

## Acceptance criteria

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | Word (capabilities: `canComposeEmail` false, `canOpenBrowserWindow` true) offers exactly two choices | **Met** | `resolveSendEmailAffordance` returns `'word-choice'` only under exactly this capability pair + something to link; `App.tsx` renders exactly two `MenuItem`s ("Spaarke email", "Outlook on the web") for that case. Unit-tested (`resolveSendEmailAffordance` suite). |
| 2 | Spaarke choice opens `sprk_communicationpage` compose with subject + both links + `associatedTo`; no related record → `associatedTo` omitted, email still opens | **Met** | `buildSpaarkeComposeUrl` + `prepareWordSendEmailChoice` tests, incl. the real-decode-algorithm round trip and the "no related record" case. |
| 3 | Outlook-on-the-web choice opens the compose deep link with the same subject + both links | **Met** | `buildOutlookWebComposeUrl` + `prepareWordSendEmailChoice` "both" test (plain-text body asserted via `new URL(...).searchParams.get('body')`). |
| 4 | Outlook behaves exactly as before; a test pins it | **Met** | `prepareSendEmail`'s original 10 tests are unchanged and still pass (only the `navbar=off` substring in two of them needed updating, since they assert the record-link URL literally); `resolveSendEmailAffordance`'s `'outlook-native'` test pins that `canComposeEmail: true` always wins regardless of `canOpenBrowserWindow`. |
| 5 | Negative: `canOpenBrowserWindow` false in Word → no Send Email affordance at all | **Met** | `resolveSendEmailAffordance`'s "neither capability → none" test; `App.tsx` renders neither block when `sendEmailAffordance === 'none'`. |
| 6 | Open on the record card: URL has `navbar=off`, no `cmdbar=false`; refetch-on-return unchanged | **Met** | `openRecordLauncher.test.ts`'s updated `buildOpenRecordUrl` tests (positive `navbar=off` + explicit `not cmdbar=false` negative). Refetch-on-return (`SaveFlow.openRecord.test.tsx`'s focus/visibility suite) is untouched and still green — it mocks `openRecordLauncher`, so the URL-shape change cannot affect it. |
| 7 | Gates: gated jest green with new suites added; lint 0; typecheck 0 production; build succeeds | **Met** | See Gates table above. |
| 8 | Live (after a deploy): each choice opens with working links; the Spaarke email appears against the record after sending | **Left open, as instructed.** Cannot be exercised from this environment — no deployed Static Web App / live Office host / live Dataverse session is reachable here. Everything up to the URL boundary is verified (including a round-trip through the real downstream parser's decode algorithm); what is NOT verified is the actual Communication page render, the actual Outlook-web compose window, and the actual send-and-file. |

## What remains unverified

Same category as tasks 027/036's own reports: the four `<ui-tests>` (Word choice end-to-end, Outlook
unchanged, focused record open end-to-end, dark mode) all require a live Office host and a deployed
environment. Unit coverage is thorough (35 tests across the two touched service test files — 26 + 9, up from 10 + 6;
re-counted and re-run by the main session on review, which also re-ran typecheck 68 / lint 0 / build 0 —
including a literal round-trip through the real consumer's decode algorithm), but it does not substitute
for: confirming the Communication page actually renders the compose form from this URL in a live Dataverse
org, confirming Outlook-on-the-web's own deep link doesn't impose an undocumented body-length or
character-escaping rule beyond what was estimated above, and confirming dark-mode rendering of the new
`Menu`/`MenuItem` choice (Fluent v9 theming is inherited from the existing `FluentProvider`, so no new
risk is expected, but it is not observed).

## Publish-size statement (client-only task)

No file under `src/server/api/Sprk.Bff.Api/` (or `Spaarke.Core`/`Spaarke.Dataverse`) was touched. Zero
delta — nothing to measure, per root CLAUDE.md §10 bullet 4.
