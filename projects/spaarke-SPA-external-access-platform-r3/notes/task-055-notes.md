# Task 055 - Notification bell and deep-link record open (FR-08 / FR-07 client)

Status: implemented, verified, left UNCOMMITTED for the main session to commit by path. TASK-INDEX and POML status were
not touched (brief forbids it), so POML step 6 (status update) was skipped on purpose.

## Design

**Bell.** `NotificationBell` is a Fluent v9 `Popover` (`trapFocus`, `below-end`) around a `Tooltip` + `Button` with the
alert icon and a `Badge` laid over it. Everything is `makeStyles` with semantic tokens (ADR-021); a source scan in the
test file proves no hard-coded colours, and a dark-theme render proves it resolves under `webDarkTheme`.

- Accessible name: "Notifications, N unread" / "Notifications, N or more unread" / "Notifications". The badge itself is
  `aria-hidden` (the name carries the count). The popover surface is labelled "Notifications".
- List: `<ul aria-label="Notifications">` with `useArrowNavigationGroup({axis:'vertical', circular:true})`, native
  `<button>` items. Escape closes and focus returns to the bell. Choosing an item closes the popover and opens the record,
  and on close of the detail view focus returns to the bell (`openRecord(target, {returnFocusTo})`).
- Item text comes only from allow-listed fields: grant "You were given access to {title}", message "{sender} sent a
  message on {title}". Null title falls back to the record kind ("a matter", ...); null sender to "Someone". No body is
  read or rendered (the DTO has none; privileged messages are filtered server-side).
- States: error (`MessageBar` + "Try again") for any failed read - NEVER the empty state; empty state only for a feed that
  was read and is empty; "seen failed" notice when the marker POST failed (retried on next open); truncated notice.
- Unopenable items (unknown `recordType` or non-GUID id) render as plain text, not buttons.

**Feed hook** (`useNotificationFeed`): timer chain (not `setInterval`), `POLL_INTERVAL_MS = 60_000`. The timer is parked
while the tab is hidden and re-armed on `visibilitychange` for the remainder of the minute. Polling is paused while the
popover is open. Back-off `min(60s * 2^n, 10 min)` on 429/503/500/network; success resets; 401/403 stop polling. Stale
responses are dropped (sequence ref). `refreshAndMarkSeen()` = GET, then ONE `POST /seen` only if the GET succeeded and
`unreadCount > 0`. A failed GET never reaches `/seen`.

**Client** (`notifications-client.ts`): `bffApiCall` only. `normaliseFeed` keeps allow-listed fields, sorts newest first,
throws if `value` is not an array. `notificationFailureOf` classifies unauthorized / rate-limited / contact-unreadable /
seen-unavailable / server / network from status + ProblemDetails `errorCode`. `unreadBadgeText`: "N+" when `truncated`
or the page is full (count capped at 100, so "100+"), exact count otherwise, null for none.

**Deep link.** `parseRecordLink(search)` accepts exactly one `record` param, lowercase token from the six-token set
(project, matter, workassignment, document, invoice, servicerequest), `:`, GUID. Anything else is ignored and the URL is
left untouched. Valid link: `openRecord(target)` through the same `RecordOpenerProvider`/`RecordDetailModal` the bell
uses, then `record` is removed from the URL (other params kept, `replace`). Access is never decided client-side: the modal
calls the BFF and shows its existing denied state on 403/empty. `?customer=` is untouched (task 041 seam; unknown key
ignored, never a host or scope).

- Browser: `AuthGuard` already carries `pathname + search` across sign-in, so no new parsing was needed.
- Teams: the Teams host never mounts `MsalAccountGate`, so the return-to restore never ran there. `RecordDeepLink` has a
  Teams effect using the new `consumeReturnToWhere(accept)` (msal-auth.ts), which consumes the stored return-to only if it
  is safe AND is a valid record link; any other captured route is left alone.

## Files

New: `src/api/notifications-client.ts`, `src/hooks/useNotificationFeed.ts`, `src/components/shell/NotificationBell.tsx`,
`src/components/detail/record-targets.ts`, `RecordOpener.tsx`, `RecordDeepLink.tsx`; tests `tests/NotificationBell.test.tsx`
(38), `tests/RecordDeepLink.test.tsx` (52), `tests/App.notificationBell.test.tsx` (6).
Modified: `src/App.tsx` (provider, `RecordDeepLink`, bell slot, `useMsal().inProgress`), `src/components/AppHeader.tsx`
(`bell` slot, also in the Teams hint bar), `src/components/shell/index.ts`, `src/auth/msal-auth.ts`
(`consumeReturnToWhere`), `src/mocks/mock-service.ts` (dev mock GET/POST handlers).
All under `src/client/external-spa/`.

## Deviations from the POML (and why)

1. Bell lives in `AppHeader` (a `bell` slot), not `PortalWorkspaceShell`: the header is where the existing theme toggle
   and user menu are. Also added to the Teams hint bar for parity.
2. NFR-02 record token set is six (the detail view's entities); task 036's emailed links only use three. A superset is
   safe: unknown tokens are ignored.
3. `/seen` is skipped when nothing is unread (no marker move for no reason).
4. Bell mounts only when signed in AND `inProgress === None`, so it cannot trigger a token redirect before sign-in.
5. Bell always re-reads on open (no throttle); polling pauses while open.
6. Teams return-to restore added (`consumeReturnToWhere`) because deep links would otherwise not survive sign-in in Teams.
7. Existing source scan `App.removedRoutes.test.tsx` flagged a multi-line `navigate(... search ...)` in
   `RecordDeepLink.tsx` (regex `navigate\([^)]*search`). Fixed in the source (destination built into a const first), the
   guard test was not changed.

## Test evidence

Final, in `src/client/external-spa`: `npm run typecheck` clean; `npm run lint` clean (0 problems); `npx vitest run`
29 files, 775 passed / 0 failed (679 before + 96 new); `npm run build` ok (bundle 1.41 MB, existing size warning only).
The first full run had 1 failure (the removedRoutes scan above); after the fix the full suite ran clean (once;
no flakes observed, so no second run was needed beyond the re-run after perturbation restore, also 775/775).

Fail-before proofs (one change at a time to the new code, run the 3 new test files, restore; all 96 tests pass unperturbed):

| # | Perturbation | Result |
|---|---|---|
| P1 | `/seen` posted even after a failed read | 5 failed (503, 500, network, "failed open does not mark seen", retry) |
| P2 | poll interval 1 s | 3 failed (mount+1/min, hidden tab, wait out the minute) |
| P3 | no back-off | 3 failed (429 doubling, 503/500, cap) |
| P4 | hidden tab still polls | 2 failed |
| P5 | badge ignores `truncated` / full page | 2 failed ("100+" both ways) |
| P6 | `/seen` posted when nothing unread | 3 failed |
| P7 | `matters` mapped to project | 4 failed (click opens matter, titles, token table) |
| P8 | deep-link GUID not checked | 10 failed |
| P9 | two `record` params accepted | 2 failed |
| P10 | `record` left in URL | 4 failed |
| P11 | Teams restores any captured route | 2 failed |
| P12 | bell mounted for nobody / mid-interaction | 2 failed |
| P13 | no bell in Teams hint | 1 failed |
| P14 | failed read shown as empty feed | 5 failed |
| P15 | null title renders blank | 2 failed |

(The perturbation harness is a scratchpad script, not committed.)

## Acceptance criteria

- Bell with unread badge, "100+" overflow, accessible name with count: met (tests).
- Open marks seen: exactly one POST, after a successful GET only; none on 503/500/network/429: met (tests, P1/P6).
- Click opens the right record for projects / matters / workassignments / servicerequests; unknown types not clickable: met.
- Null title fallback; no body ever rendered: met.
- 503 / 500 / network show an error, never empty, never `/seen`: met (P14).
- Poll >= 60 s, paused when hidden and while open, back-off on 429/503, stops on 401/403: met (fake timers, P2-P4).
- Deep link valid / invalid / unknown type; param stripped; no network call for invalid; 403 shows existing denial; survives
  sign-in (browser via AuthGuard return-to; Teams via `consumeReturnToWhere`): met in jsdom.
- Fluent v9 only, tokens only, dark theme: met (source scan + dark render). Teams theme visually: OWED (live).
- Keyboard: arrow navigation, Escape returns focus to bell: met in jsdom; real-browser focus OWED.
- Task 037 contract (allow-listed fields, `unreadCount` of the page, `truncated`): matches the server code.

## Live browser checks owed (not provable in jsdom)

1. Popover initial focus placement, trap and Escape/focus return in a real browser (jsdom tabster is unreliable; tests
   fire Escape on the dialog).
2. Dark mode and Teams theme appearance of bell, badge and popover.
3. The real grant email link (`?customer=&record=`) after a fresh sign-in, in a browser and in Teams.
4. A real 429 / 503 back-off against the BFF; hidden-tab pause in a real tab.
5. Bell end-to-end against the real BFF with a seeded grant and message.

## Known limits

- `/seen` moves the marker to server "now" (task 037 K2), so items that arrive between the GET and the POST are marked seen.
- No live-region announcement when new items arrive (the accessible name updates with the count).
- Pre-existing gap noticed, not fixed: Teams `subPageId` restore never happens because the Teams host skips `MsalAccountGate`.

## Review fixes (fix round after the two independent reviews)

The reviews found no fix-now defects; per the owner's policy (a confirmed defect on a real path is fixed in scope) these were fixed.

1. **A background poll never starts an interactive sign-in.** New opt-in `noInteraction` on `StandaloneAcquireOptions` /
   `BffTokenAcquireOptions`. In `acquireStandaloneBffToken` (no account, or InteractionRequired / `timed_out`) it throws
   `SignInRequiredError` BEFORE `assertRedirectAllowed` / `captureReturnTo` / `acquireTokenRedirect`, and records no sign-in
   failure (a later user-initiated redirect is not blocked). Teams path: NAA `InteractionRequired` and the popup fallback
   both throw `SignInRequiredError` instead of `acquireTokenPopup`, with no change to `teamsWorkforcePath`. Threaded through
   `acquireActiveBffToken` (passes the options object only when something is set, so the default `acquirer(scope)` and the
   401 retry `acquirer(scope, { forceRefresh: true })` shapes are unchanged), `applyStandalonePlane` (workforce options
   merge; CIAM silent request still exactly `{ scopes, account }`) and `bffApiCall(path, init, { noInteraction: true })`
   (third argument; the 401 retry keeps it). Only the timer-driven poll GET passes it (`readFeed(true)`); the open / "Try
   again" read and `/seen` use the normal path (user gesture). `SignInRequiredError` maps to the bell's `signin` failure:
   polling stops, "Sign in again to see notifications.", no redirect.
2. **A superseded read no longer drives `/seen`.** `readFeed` returns `null` when `seq !== seqRef.current` (or unmounted).
3. **401 is "sign in again".** `notificationFailureOf`: 401 and `SignInRequiredError` -> `'signin'` ("Sign in again to see
   notifications."), 403 -> `'unauthorized'` (text unchanged). Both stop polling; an explicit open tries again.
4. **`record` is stripped only after a terminal state.** `RecordDetailModal` gained `onSettled` (loaded / denied / not found;
   a retryable error is NOT settled, and neither is loading). `openRecord(target, { onTerminal })` runs the callback once on
   settle, on close, or when another record replaces it. `RecordDeepLink` keeps `record` in the URL until then, opens once
   per link (ref guard), and strips only if the bar still names that record. Interaction found while writing item 5: with the
   old immediate strip, when MSAL had already landed on the link the gate saw `target !== current` (the deep link had just
   stripped it) and navigated back to the link, so the record opened twice. Deferring the strip fixes that too.
5. **Integration test with the real gate:** `tests/App.deepLinkSignIn.test.tsx` mounts the real `App` (real `AuthGuard` /
   `MsalAccountGate`, `RecordDeepLink`, opener, modal, bell; only `useMsal`, the pages and `bffApiCall` are stubbed), seeds
   `sessionStorage['spaarke.ext.returnTo']` with the grant-email link (`%3A`-escaped colon), and asserts one record read, one
   dialog, the final URL `?customer=demo`, the key consumed, no `loginRedirect`; plus the target === current case and a
   check that the link is still in the address bar when the bell's first GET and the record read have happened.
6. **Cleanups:** `FEED_RECORD_TYPES` gains `invoices: 'sprk_invoice'` (server `SegmentByEntity` is built from
   `ExternalMessageEndpoints.Roots`: projects, matters, workassignments, invoices, servicerequests; `sprk_invoice` has a
   detail view); the "no retry loop" bell test now uses fake timers and advances past the back-off cap, open and closed.

Tests: `tests/background-no-interaction.test.ts` (13), `tests/NotificationBell.noInteraction.test.tsx` (2, real chain with a
fake MSAL), plus new cases in `NotificationBell.test.tsx`, `RecordDeepLink.test.tsx` and `App.deepLinkSignIn.test.tsx`.
Must-fire (mutation) checks run and reverted: removing the `noInteraction` checks, not passing `background` from the poll,
returning the stale read, stripping at open, and 401 -> `unauthorized` each fail the new tests.

Not changed (noted): in Teams a click-path `TeamsWorkforceAuthError` still renders the generic "couldn't be loaded" text
(only `SignInRequiredError` and 401 map to the sign-in text).

**Extra review pass (reason):** the fix round added `noInteraction` to the shared token acquirer (`msal-auth.ts`, `bff-client.ts`, `standalone-plane.ts`), an approach change in auth code, so one scoped fable re-check covers the fix diff and its direct callers/callees.

**Re-check outcome (scoped fable):** no F-class. K2 fixed in scope by the main session: a background `SignInRequiredError` (status null; may be a transient `timed_out` renewal) no longer stops the poll; it backs off (2/4/8/10-min) and retries silently, and a later silent success clears the state. A 401 after the forced refresh and a 403 still stop. Must-fire: reverting the hook condition fails 3 tests. K3 (Try again after a recorded 056 redirect failure re-shows the message; recovery is the page "Sign in again") and K4 (same-record URL re-entry while settled defers the strip to close) are accepted known limits. Owed live: Teams `inProgress` leaves Startup so the bell mounts (task 071 run).

**Final verification:** typecheck 0, lint 0, vitest 32 files / 809 tests, build ok, check:dist clean.
