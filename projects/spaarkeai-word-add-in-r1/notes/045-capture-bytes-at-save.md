# Task 045 — capture the Word document's bytes at Save press, not at Save-tab mount

> Source: `notes/025-residual-collision-surface.md` M12 / R5 / D-i (2026-09-13). Base: `8581bf9f4`
> (work/spaarkeai-word-add-in-r1 tip at task start; includes 025, 031, 050, 053).

## 1. The defect, re-located by symbol (not the POML's stale line numbers)

`SaveView.tsx`'s mount effect (`useEffect(..., [hostAdapter])`) read the open document's bytes ONCE
via `hostAdapter.getDocumentContent({ format: 'ooxml' })`, base64-encoded them, and cached the result
in a `documentContentBase64` state variable. That state flowed down as a static prop through
`SaveFlow.tsx` into `useSaveFlow.ts`'s `SaveFlowContext.documentContentBase64`, read directly by
`startSave`'s `Document` branch and by the version-save idempotency SHA-256 hash.

Every submission path traced back to this SAME cached value:
- **Initial Save** — `SaveFlow.handleSave` → `buildSaveContext()` → the stale prop.
- **Retry** — `useSaveFlow.retry()` just calls `startSave(savedContextRef.current)`, replaying the
  EXACT context object (including its stale bytes) captured at the original `startSave` call.
- **"Save Another"** — `useSaveFlow.reset()` clears flow state but not `SaveView`'s
  `documentContentBase64` state; the pane doesn't remount, so the NEXT Save press still read the
  SAME mount-time snapshot.

Any edit made after the Save tab mounted was silently never uploaded on any of these three paths,
while the pane reported success (task 025's M12).

## 2. The fix

Moved the capture from "once, in a `useEffect`, cached in state" to "a live callback, invoked fresh by
`useSaveFlow.startSave` at the exact moment each attempt submits":

- **`SaveView.tsx`** — removed the eager capture from the mount effect and the
  `documentContentBase64` state entirely. Added `captureDocumentContent` (`useCallback`, deps
  `[hostAdapter]`) that calls `hostAdapter.getDocumentContent({ format: 'ooxml' })` and base64-encodes
  the result — the SAME extraction/encoding code, just moved into a function invoked later instead of
  run inline at mount. Gated on a new `canGetDocumentContent` flag (`hostAdapter?.getCapabilities().
  canGetDocumentContent ?? false`), mirroring the file's existing `canOpenRecord` /
  `canSuggestRelatedRecords` / `canProvideDocumentName` capability-flag pattern (NFR-10 — never a
  `hostType` check). A capture failure is normalized from a `HostAdapterError` (`{code,message}`, not
  `instanceof Error`) into a real `Error` carrying the same message, so `useSaveFlow`'s existing
  `createErrorFromException` catch surfaces the actual cause instead of a generic fallback.
- **`SaveFlow.tsx`** — added `captureDocumentContent?: () => Promise<string>` to `SaveFlowProps`,
  threaded through unchanged into `buildSaveContext()` (and its `useCallback` deps). `SaveFlow` never
  calls it — it only forwards the live reference. Every caller of `buildSaveContext()` (`handleSave`,
  `handleKeepBoth`, `handleSaveAsVersionInstead`) therefore carries it automatically.
- **`useSaveFlow.ts`** — added `captureDocumentContent?: () => Promise<string>` to `SaveFlowContext`
  (kept `documentContentBase64` as a plain-value fallback for callers that already have bytes, e.g.
  tests). In `startSave`'s `Document` branch: `documentContentForRequest = context.
  captureDocumentContent ? await context.captureDocumentContent() : context.documentContentBase64`,
  used for both `serverRequest.document.contentBase64` and the version-save idempotency SHA-256 hash
  (previously two separate reads of `context.documentContentBase64`, now one resolved local so the
  hash and the wire body can never diverge). The call happens inside the existing `try` block, so a
  rejection is caught by the existing `catch` → `createErrorFromException` path — no new error-handling
  mechanism was added (reusing task 053's established pattern, per the task's own instruction).

**Why this fixes all three paths with no path-specific code**: `captureDocumentContent` is a LIVE
function bound to `hostAdapter` by closure, not a value. `retry()` was already resending the SAME
context OBJECT it always has — but that object now carries a live function instead of a cached string,
so re-invoking it re-reads the document. "Save Another" needed no change at all: the user's next Save
press rebuilds the context via `buildSaveContext()`, which still has the SAME live prop from `SaveView`
(unchanged across re-renders, since the pane never remounts). The fix is entirely in **where the value
comes from**, not in any of the three submission call sites.

**Loading state while capturing**: no new UI state was added. `startSave` already sets
`flowState = 'uploading'` (Save button disabled, labeled "Saving...") synchronously, before the try
block — i.e. before the capture call. The capture phase is already covered by the existing
"uploading" state; no separate "Capturing…" sub-state was introduced (would have been scope creep
for no acceptance-criterion benefit).

## 3. Explicit evidence: all three submit paths capture fresh bytes

All three are exercised in `SaveFlow.captureAtSave.test.tsx` against the REAL `useSaveFlow` hook (only
`fetch`/`SseClient` mocked):

1. **Initial Save** — `'reproduces the fix: pressing Save after an edit uploads B2, never the earlier
   B1'`: `captureDocumentContent` mocked to return `'B9'` then `'B2'`; first Save's request body
   carries `'B9'`.
2. **"Save Another"** — same test: after reaching the success card, "Save Another" is clicked, the form
   re-shows, and a SECOND Save press sends `'B2'` — `captureDocumentContent` called exactly twice.
3. **Retry** — `'a retry after a failed save re-captures fresh bytes, not the bytes from the failed
   attempt'`: the first save 500s (`captureDocumentContent` → `'B9'`); clicking the hook's raw `Retry`
   button sends `'B2'` — proving the hook's OWN `retry()` (not a wrapper) already re-invokes the live
   callback.
4. **Bonus (not required by AC, but same mechanism)** — `'the collision retries (Keep both / Save as
   new version, task 025) also re-capture'`: an OFFICE_020 refusal followed by "Keep both" also
   re-captures (`'B2'`), confirming every `buildSaveContext()` caller shares the one capture site.
5. **Negative** — `'a capture failure shows a handled, visible error and uploads NOTHING'`:
   `captureDocumentContent` rejects; the pane shows the error message and `saveCallCount() === 0`
   (no fetch to `/api/office/save` at all).
6. **Regressions** — with neither `captureDocumentContent` nor `documentContentBase64`, Save still
   refuses with the pre-existing "Document content is required" message (byte-identical copy,
   unchanged); with ONLY a plain `documentContentBase64` value (no live callback — the shape every
   existing gated test uses), Save still works exactly as before.

`SaveView.captureAtSave.test.tsx` pins the seam one layer down (mocking `SaveFlow` itself, asserting
what `SaveView` hands it): `getDocumentContent` is never called merely by mounting; the SAME
`captureDocumentContent` function, called twice, reads the adapter's CURRENT bytes each time (`'DRAFT-1'`
then `'DRAFT-2-EDITED-AFTER-MOUNT'`); a `HostAdapterError`-shaped rejection is normalized to a real
`Error`; an Outlook adapter (`canGetDocumentContent: false`) gets no `captureDocumentContent` prop at
all and `getDocumentContent` is never called.

## 4. Reproduce-first (POML step 1)

Confirmed RED→GREEN by temporarily reverting `useSaveFlow.ts`'s one-line resolution
(`documentContentForRequest = context.captureDocumentContent ? await context.captureDocumentContent()
: context.documentContentBase64`) to the old `context.documentContentBase64` read, then restoring it:

| Run | `SaveFlow.captureAtSave.test.tsx` | `SaveView.captureAtSave.test.tsx` |
|---|---|---|
| Pre-fix (reverted) | **5 failed, 2 passed** (the two backward-compat regressions correctly stayed green — they never depended on the live callback) | 7 passed (unaffected — that layer's fix lives entirely in `SaveView.tsx`, not touched by the revert) |
| Post-fix (restored) | 7 passed | 7 passed |

## 5. Gate results

- **`npx tsc --noEmit`**: **111 total / 0 production** — IDENTICAL per-file breakdown to the stated
  baseline (32 `SaveView.test.tsx` + 31 `OutlookAdapter.test.ts` + 24 `shared/__mocks__/office-js.ts`
  [jest manual-mock infrastructure, not production] + 10 `SaveFlow.test.tsx` + 6
  `useEntitySearch.test.ts` + 3 `EntityPicker.test.tsx` + 2 `WordAdapter.test.ts` + 2
  `ShareView.test.tsx` + 1 `useSaveFlow.test.ts` = 111). My two new test files contribute **0** errors
  (one transient `React` unused-import was fixed during development). **0 production files have any
  error.**
  - **Environment note, not a code deviation**: a fresh `npm install` in this worktree left the
    sibling `file:`-linked `@spaarke/auth` package (`src/client/shared/Spaarke.Auth`) unbuilt (no
    `dist/`), which surfaced as a transient `Cannot find module '@spaarke/auth'` in
    `shared/services/AuthService.ts` (a file this task never touches). Fixed by running
    `npm install && npm run build` in `src/client/shared/Spaarke.Auth` — a one-time worktree-setup
    step, not a source change. Confirmed the 111-baseline figure is unaffected once that sibling
    package is built.
- **`npm run build`** (with `ADDIN_CLIENT_ID`/`TENANT_ID`/`BFF_API_CLIENT_ID`/`BFF_API_BASE_URL`
  placeholders): exit 0, no webpack errors.
- **`npx jest --ci`** (full suite): **10 failed suites / 84 failed tests**, exact match to the stated
  baseline both in count AND in suite names (`OutlookAdapter.test.ts`, `ApiClient.test.ts`,
  `EntityPicker.test.tsx`, `SaveFlow.test.tsx`, `TaskPaneNavigation.test.tsx`, `TaskPaneShell.test.tsx`,
  `SaveView.test.tsx`, `ShareView.test.tsx`, `useEntitySearch.test.ts`, `useSaveFlow.test.ts`).
  `SaveView.test.tsx` + `useSaveFlow.test.ts` combined: **29 failed / 23 passed / 52 total** — IDENTICAL
  to the documented per-file baseline (4/26 + 19/26), confirming this task changed the behavior of
  **zero** pre-existing tests in either direction.
- **Gated suite list** (39 non-comment lines of `ci-gated-suites.txt`, run via
  `--runTestsByPath` exactly as CI does): **39 passed / 39 total, 461 passed / 461 total.** No
  regression.
- **New suites** (both pass, both add coverage, neither currently listed in `ci-gated-suites.txt` —
  this task may not edit that file per its worktree boundary):
  - `shared/taskpane/components/__tests__/SaveFlow.captureAtSave.test.tsx` — 7/7
  - `shared/taskpane/components/views/__tests__/SaveView.captureAtSave.test.tsx` — 5/5
- **`eslint`** directly on the 5 touched files: 0 errors, 0 warnings.
- **Quality gates (Step 9.5)**: `code-review` — 0 Critical, 0 Warning (AI code-smell scan clean; one
  non-blocking Suggestion: `SaveView.tsx` now calls `hostAdapter?.getCapabilities()` four times per
  render — a pre-existing per-capability-flag pattern this task extended by one, not introduced, not
  worth refactoring in this task's scope). `adr-check` — 0 Violations, 0 Warnings across ADR-012,
  ADR-021, ADR-028, NFR-10, and the task-010 boundary (`WordAdapter.ts` has zero diff).

## 6. Scope discipline

- `WordAdapter.getDocumentContent()` / `getCompressedFile()` — **zero diff** (`git status` confirms;
  the fix only changes WHEN the existing extraction is called, never HOW).
- No `.cs` files touched — no BFF publish-size measurement needed.
- No file outside `src/client/office-addins/**` touched (plus this notes file).
- Did not touch 051 (client stamp reader), 052 (`problem+json`), 054 (Email collision), or any
  server-side `Services/Office/**` file (task 014's live surface).

## 7. Deviations from the POML

None of substance. The POML's cited line numbers (`SaveView.tsx:202-219`) had drifted (as the POML
itself warned); the actual site was re-located by symbol (the mount `useEffect`'s
`if (type === 'word') { ... canGetDocumentContent ... }` block, confirmed against the 025 note's own
M12 citation of the same drifted range). No escalation trigger fired: task 025 was already merged at
this task's start (verified in the branch tip's commit log before starting), and capturing at Save
press did not change `WordAdapter.getDocumentContent()`'s extraction or the bytes produced for an
unchanged document (proven by the regression test using a plain `documentContentBase64` value, and by
`WordAdapter.test.ts` staying green in both the gated run and the full run).
