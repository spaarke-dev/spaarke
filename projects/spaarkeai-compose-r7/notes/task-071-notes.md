# Task 071 — Reload from source no longer blanks the page (R6 D4 / FR-09) — ROOT-CAUSED + FIXED

> Phase 7 · sonnet@**xhigh** · FULL rigor · 2026-08-17 · client-only. No BFF bytes.

## The affordance

R6 UAT called it "Restore from Source"; the shipped affordance is **"Reload from source"**
(`onReloadFromSource`, task 053 UAT #5) — an always-available toolbar action, gated on
`state.documentRef?.speDriveItemId`, that dispatches `{ kind: 'requestLoad', documentRef, sessionId,
externalChange: true }` to pull the latest SPE bytes (with a dirty-guard confirm).

## Root cause (traced to the exact branch)

1. `requestLoad` resets to `INITIAL_STATE` + `status:'loading'`, keeping `action.documentRef`
   (`ComposeWorkspace.types.ts` ~485).
2. The BFF Load effect (`ComposeWorkspace.tsx` ~1010) computes
   **`loadDriveId = state.documentRef?.driveId ?? effectiveDriveId`**. If BOTH are falsy it takes the
   **`if (!loadDriveId) → dispatch({ kind: 'reset' })`** branch (~1034–1042) → `INITIAL_STATE` → the
   idle **"pick another document or re-upload this one"** empty state (~3932). **That is the D4 blank +
   re-upload prompt.**
3. Why `documentRef.driveId` was falsy: the Load response carries an authoritative **`payload.driveId`**
   (`ComposeWorkspace.tsx` 1077, required), but **`loadSucceeded` never stamped it onto `documentRef`**
   (the reducer's `loadSucceeded` set sprkDocumentId/fileName/transientKey/composeLogicalId — NOT
   driveId). So a doc whose drive the host `driveId` prop does NOT identify (a BU-container /
   PDF-sourced / born-in-editor-then-promoted doc — the exact case the Load effect's own comment at
   ~1028–1032 flags) had `documentRef.driveId === undefined` after loading. When `effectiveDriveId` was
   also unavailable/wrong at reload time, `loadDriveId` was falsy → reset → blank.

The contrast that makes it obvious: **`saveSucceeded` ALREADY stamps `driveId`** onto documentRef (the
create-on-save re-target, UAT-2026-07-19 P2 — `ComposeWorkspace.types.ts` ~753). `loadSucceeded` was
simply missing the symmetric stamp.

## Fix (pure client mount-state — no server round-trip, no mount-contract change)

Stamp the authoritative load-time `driveId` onto `documentRef` in `loadSucceeded`, mirroring the shipped
`saveSucceeded` pattern verbatim:

- **`ComposeWorkspace.types.ts`**: added `driveId?: string` to the `loadSucceeded` action; in the reducer
  `loadSucceeded` documentRef spread, `driveId: action.driveId && action.driveId.length > 0 ?
  action.driveId : state.documentRef.driveId` (defensive fallback preserves an existing value).
- **`ComposeWorkspace.tsx`**: the `loadSucceeded` dispatch now passes `driveId: payload.driveId`.

Effect: every loaded doc now carries the drive it actually lives in, so Reload-from-source's `requestLoad`
retains it → `loadDriveId` is always truthy → the reload FETCHES (and `loadSucceeded` repopulates) instead
of resetting to blank. Reuses the EXISTING `requestLoad` reload path (no parallel restore path — §11).

## Escalation trigger — did NOT fire

The POML trigger fires only if the root cause "requires a server round-trip to restore source content
(not a client mount-state fix)". It does not: the reload's server round-trip is INHERENT to "Reload from
source" (fetching latest SPE bytes is the feature, not the bug); the bug was the client LOSING the drive
pointer → resetting to blank. The fix is a client-side `documentRef` stamp; the mount contract is
unchanged.

## Verification

- **Standalone jest: 645 pass / 0 fail** (642 + 3 new `ComposeWorkspace.reloadFromSource.reducer.test.ts`
  — pure reducer, runs in this session):
  1. `loadSucceeded` stamps `payload.driveId` onto `documentRef.driveId`;
  2. a subsequent Reload-from-source (`requestLoad` with the loaded ref) RETAINS the drive → the Load
     effect's `loadDriveId` is truthy → never the `!loadDriveId → reset` blank;
  3. defensive: `loadSucceeded` with `driveId` omitted preserves an existing `documentRef.driveId`.
- **No regression to 010/011**: full suite green incl. `composeIdentity.test.ts` + `renderOnSave.reducer`.
- **tsc**: 30 = KNOWN `@spaarke/*` baseline; **0 new-symbol errors** (driveId typechecks — confirms
  `ComposeDocumentRef.driveId` exists, mirroring the saveSucceeded assignment).
- Full-component reload UAT (click Reload → GET → editable) is the CI-only ui-test (ComposeWorkspace
  imports `@spaarke/*`); the reducer proof covers the mount-state root cause end to end.
- **No BFF bytes** → publish/CVE unchanged.

## Gates (Step 9.5)

- **code-review: PASS** — mirrors the shipped `saveSucceeded` driveId stamp; additive action field;
  defensive fallback; `payload.driveId` is a required response field (always a valid stamp); no smells.
- **adr-check: PASS** — ADR-049 save path untouched (this is the LOAD path, made symmetric with the save
  stamp); §11 modify-only (reuses the existing `requestLoad` reload path, no parallel restore); NFR-06
  `docxBridge.ts` intact; client-only (no BFF).

## Phase 7: 071 DONE (18→19/20). Next: 072 (add-comment toolbar affordance) → 074 (apply-template ETag/404) → 090 wrap-up.
