# Task 013 — In-conversation compose (FR-06)

**Status**: completed 2026-07-21 · FULL rigor · sonnet/high · Step 9.5 gate RUN (1 Major fixed, 3 Minor fixed, 1 Minor deferred)

## What shipped
Added a Teams-style chat input at the bottom of `ConversationView` (shared lib). Sends through the **existing** send path; no 6th send implementation.

- **Files** (only the POML's declared outputs):
  - `src/client/shared/Spaarke.UI.Components/src/components/ConversationView/ConversationView.tsx` — internal `ConversationComposeBar` (not exported) + capture `pollNow` from `useThreadPoll` + render the bar. `ConversationViewProps` **unchanged** (no new public surface — §11).
  - `.../ConversationView/__tests__/ConversationView.compose.test.tsx` — new (jest).
  - Doc-accuracy: header comments in `ConversationView.tsx` + `ConversationView.types.ts` updated (were "READ-ONLY, no compose").

## Key contract decisions
- **Send path (ADR-045/046)**: `sendTimelineMessage({ communicationType: 'message', threadId, body, bodyFormat: 'text' }, { authenticatedFetch, bffBaseUrl })`. For **message** sends the BFF addresses by thread — `to`/`subject` are NOT required (`sendCommunication` guards only enforce them for `email`; it still sends `to:[]`/`subject:''` to satisfy the DTO's required-presence). So a chat input needs only a body. This is why the full `TimelineComposeBox` (To/Cc/Bcc/Subject/attachments) does **not** fit and was NOT mounted — the send *wiring* is mirrored, not the component.
- **Refresh (FR-06)**: `useThreadPoll` already returns `pollNow()` (out-of-band poll) — ConversationView previously discarded it. On successful send → `onRefresh()` = `pollNow()` (immediate). Manual Refresh (↻) button = same `pollNow()`. The ~5s interval keeps running independently (never disabled) — proven by a short-interval test.
- **Single-flight**: `inFlightRef` (ref, not display `status`) is the real send lock; textarea stays enabled during send (Teams-style), and `onChange` only clears TERMINAL feedback (`sent`/`failed`), never `sending`.

## Step 9.5 gate outcome (adversarial review)
- **Major (FIXED, Path C)**: typing during an in-flight send reset `sending → idle`, which killed the spinner AND re-enabled Send → possible double-send for one user message. Fix: `inFlightRef` guard + narrowed the status reset. New test `does NOT double-send when the user keeps typing during an in-flight send` proves it (sends stays 1).
- **Minor (FIXED)**: setState-after-unmount on the async continuation (React 16.14 PCF target, ADR-022) → `mountedRef` guard.
- **Minor (FIXED)**: nested `aria-live` (wrapper + role=status/alert children) → dropped wrapper live region; each transient child owns its announcement.
- **Minor (FIXED)**: the fake fetch ignored `since` → made it honor the exclusive cursor so the `sinceCursorRef` path is genuinely exercised.
- **Minor (DEFERRED → ISS-003)**: `useThreadPoll.pollNow` is swallowed if a poll is already in flight → on-send immediate refresh can miss by up to ~5s in a narrow race. Fix belongs in the **characterized shared core** (NFR-06/NFR-08 treat it as stable), not a UI task. Filed in `notes/defer-issues.md` (needs GitHub URL before next push).

## Verification
- `npm test -- src/components/ConversationView` → **25 passed** (2 suites). The `act()` console warning is the pre-existing benign async-poll-settle one, present in the untouched sibling `ConversationView.test.tsx`.
- `tsc --noEmit -p tsconfig.json` → **2 errors, both pre-existing + unrelated** (`@spaarke/sdap-client` / `@spaarke/auth` sibling `dist` unbuilt in this worktree — `EntityCreationService.ts` / `useWizardPageBootstrap.ts`). Zero from this change.
- `eslint src/components/ConversationView` → clean (exit 0).
- Whole-package `npm run build` NOT run — blocked on the same sibling-dist gap; scoped tsc + jest are the verification path per current-task.md Critical Context #5.

## Acceptance criteria — all met
Existing send path only ✅ · immediate + on-demand refresh + ~5s polling retained ✅ · sending/sent/failed + retryable + disabled-empty + Enter-sends ✅ · ARIA labels + keyboard ✅ · dark mode ✅ · component tests pass ✅.
