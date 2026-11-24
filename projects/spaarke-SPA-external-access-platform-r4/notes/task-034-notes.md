# Task 034 notes — Messages: Refresh action + "New message" indicator (FR-21, #2452)

Rigor: FULL (POML said STANDARD; overridden UP because the task modifies .ts/.tsx). Model: sonnet @ high. Not committed (per the brief).

## Files changed

- `src/client/external-spa/src/api/messages-client.ts` — `listMessages(root, id, options?)` forwards `auth.noInteraction` (third argument of `bffApiCall`, only when set; existing calls keep the shape `bffApiCall(path)`); new pure `hasNewerMessages(shown, fetched)`.
- `src/client/external-spa/src/components/detail/RecordMessages.tsx` — Refresh offer (`onRefreshActionChange`), in-flight guard, background check loop, indicator in a polite live region; `NEW_MESSAGE_CHECK_INTERVAL_MS`.
- `src/client/external-spa/src/components/record/sections/MessagesSection.tsx` — turns the thread's Refresh offer into a header slot (icon from `RECORD_ACTION_ICONS.refresh` = ArrowClockwise), ahead of New message.
- `src/client/external-spa/tests/RecordMessages.test.tsx` — 22 new tests (fake timers).
- No change to `sections/index.ts`, `RecordSectionCard`, `recordTemplates.ts`, `recordTemplate.types.ts`, or any other task's file.

## Escalation trigger 1 (no header-action slot) — did NOT fire

The section card already has the slot: `RecordSectionCard actions` (an `IHeaderToolbarSlot[]`), fed by `headerActions`. Refresh is added to it from inside `MessagesSection`; the renderer is not forked or touched.

## Where Refresh sits and why

In the Messages card header, first icon (before New message), tooltip/accessible name "Refresh messages". It is local to `MessagesSection`, NOT a config `sectionActions` entry, because:
- the config's `refresh` id is the record-level toolbar Refresh (re-reads the record and every section, hidden while an operation holds the view), and "an action id appears in `toolbar` or in one section, never both";
- this one re-reads the thread only (draft and other sections untouched) and, being a read action, is shown at every access level (FR-06, NFR-04): it does not depend on `canCreate`.
The record toolbar Refresh is unchanged and still re-reads the thread through `refreshSignal`.

Shown while the thread is shown (ready); withdrawn on loading/error/access/unavailable. Disabled (not hidden) while a read or a send is in flight; the handler also ignores a click while a read is in flight (ref guard, same tick).

## Indicator

Under the thread (so nothing the reader is looking at moves), in a `role="status" aria-live="polite" aria-atomic` region that is mounted with the thread and empty until a newer message is found (a live region added together with its text is not reliably announced). Fluent `MessageBar` was NOT used: it announces by itself (`useAnnounce`), which would double-announce inside our live region. Plain flex box + Fluent `Text`/`Button`, semantic tokens only (`colorBrandBackground2`, `colorBrandStroke2`, `colorNeutralForeground1`). "Show new messages" moves focus to the region first (the button unmounts when the refresh succeeds), then runs the same refresh. Clears on a successful read (Refresh / Show new messages / host refresh), on a successful send (even if the re-read fails) and on a record change / Try again.

"Newer" (`hasNewerMessages`): a fetched message whose id is not on screen AND whose `createdon` is after the newest on screen (any unseen message when nothing dated is on screen). Removed messages, older unseen ones and undated ones over a dated thread never raise it.

## Polling design and request volume (for the owner / uac-r2)

- **Interval: `NEW_MESSAGE_CHECK_INTERVAL_MS = 60_000`** (one named constant in `RecordMessages.tsx`). Same budget as the notification bell (`useNotificationFeed`, 60 s), whose comment records that the BFF's `dataverse-query` limit is 50/min shared with thread reads.
- One `setTimeout` armed after the previous read, never `setInterval`; one request at a time; at least one interval between two thread reads of ANY kind (a user refresh or a send's re-read moves the next check back).
- **Expected volume: at most 1 GET per minute per open, visible, expanded Messages section** (each GET is the existing full-thread read `GET /{root}/{id}/messages`, bounded by the route's existing cap; no BFF change). A record opened over another record that also has Messages (an invoice over a matter) is two sections = 2/min. With the bell: about 2-3 requests/min of the 50/min budget for a user with one or two threads open.
- **It stops / pauses:** hidden tab (timer parks, no request; `visibilitychange` re-arms, checking at once if an interval has passed); section collapsed (an ancestor is `hidden`: skips the read, looks again next interval); unmount (modal closed, record closed); record change (`readSeq` dropped, timer re-armed for the new record); thread not ready (error/access/unavailable: no loop); background errors: a 401/403/sign-in failure stops the checks at once, any other failure after 3 in a row (so a failing BFF costs at most 3 extra requests); a read the user asked for that succeeds re-enables them. A background answer is dropped if any other read started after it.
- Background reads pass `auth.noInteraction`: a token that cannot be had silently ends in `SignInRequiredError` (no redirect/popup). A failing background read touches no state: no error, no refresh-failed notice, no sign-in state.
- **Owner decision needed (POML escalation trigger 2):** if the owner or uac-r2 object to ~1 full-thread GET/min per open Messages section, the options are a longer interval (one constant) or a manual-only indicator (remove the loop; Refresh stays). Nothing is deployed.

## Deviations / decisions

- Section Refresh is local to `MessagesSection` (see above), not a config action.
- The collapsed check uses `closest('[hidden]')` on the live region: it couples to `RecordSectionCard` setting `hidden` on a collapsed body (documented there "for assistive technology"). A section-collapse test guards the coupling.
- The poll loop copies the shape of `useNotificationFeed`'s loop rather than extracting a shared `usePolling` hook (that file belongs to R3 and a shared hook is a wider change). Suggestion for the operator (K-class, not a defect): extract one hook if a third poller appears.
- Existing refresh-failed bar's own "Refresh" button still calls `read('refresh')` unguarded (R3 behaviour kept as-is; the guard is on the new entry points).

## Test scope justification (acceptance criterion: edge cases beyond the contract)

Beyond the stated contract and criteria: `hasNewerMessages` unit cases (the definition of "newer"); a stale background answer dropped after a refresh (the `readSeq` rule); 3-failure stop + re-arm on success and 401 stop (the "error" stop condition I chose); the one-interval gap after a user refresh (the volume guarantee); collapsed-section pause (guards the `[hidden]` coupling).

## Gate results

- **Fail before / pass after:** `tests/RecordMessages.test.tsx` against the pre-change sources (HEAD copies of the three src files swapped in, then restored): 22 failed / 52 passed (74). With the change: 74 passed (22 new tests, 52 existing unchanged).
- **Neighbouring suites** (`RecordMessages`, `RecordTemplate*` x6, `DetailDocumentsDelete`, `DetailEventsRoots`): 526 tests. First run 477 passed / 49 failed: all 49 in `RecordTemplateAccess.test.tsx`, because its `sectionActionsOf` helper lists every header button and the new Messages Refresh appeared in each expected action list. Fixed in the helper (it now leaves the Messages thread's read-only Refresh out, with a comment; the rights-gated create actions it asserts are untouched). Re-run `RecordTemplateAccess` + `RecordMessages`: 217 passed. The other 7 files passed in the first run (no source change since except that helper).
- Between those runs task 032's `DocumentLibrary.tsx` was briefly mid-edit (`useGridSettings is not defined`); unrelated to this task and resolved by their later write.
- `npx tsc --noEmit -p tsconfig.json`: exit 0 (whole SPA, including the other agents' in-progress files). `eslint` on the 5 changed src/test files: exit 0.
- code-review (standard, own files): no Critical / Warning. One F2-class observation, found and reported rather than fixed (cross-task refactor, R3's file): the poll loop in `RecordMessages.tsx` repeats the shape of `useNotificationFeed.ts`'s loop (timer armed after the previous read, hidden-tab parking, visibilitychange re-arm). The semantics differ (gap measured across read kinds, collapsed skip, failure cap), so it was not forced into one hook; if a third poller appears, extract a shared hook (it needs its own task, touches R3's file). Cohesion: `RecordMessages.tsx` grew ~250 lines but stays one responsibility (the thread); the new poll block is one effect. Sanitizer: `allowRemoteImages` appears nowhere in `external-spa/src`.
- adr-check (client-only change): ADR-021 compliant (semantic tokens, no hex/rgb, Fluent v9); ADR-028 compliant (`bffApiCall`'s existing `noInteraction`, no raw fetch or Authorization header); ADR-012 compliant (no new shared component, local to the SPA); ADR-052 / ADR-001 / ADR-010 / ADR-002 N/A (no BFF change, no DI, no job: a browser timer); ADR-038 compliant (tests at the existing `bffApiCall` seam, fake timers, no banned mocks); ADR-050 untouched. No violations, no ADR path needed.
- Class search (Step 9.5 rule 6): the dead `if (!res.ok)` / content-type patterns are not in the diff; nothing to list.
- Not run: browser UI tests (`<ui-tests>`; no live or local environment per the brief), dark-mode / high-contrast visual check, `npm run build`, `pwsh scripts/check-task-status-drift.ps1` (brief: TASK-INDEX, POML status and current-task.md are left to the caller).

rework: fix-round-1 task 034

## Review fix round 1

Findings fixed (uncommitted, same working tree):

- **F1 wrong copy after a failed refresh.** `RecordMessages.tsx`: `refreshFailed` is now `null | 'plain' | 'afterSend'`; `read(mode, afterSend = false)`; only the re-read that `handleSend` starts passes `true`. "Your message was sent, but ..." is shown only for `afterSend`; every other failed re-read (header Refresh, "Show new messages", record toolbar Refresh via `refreshSignal`, the notice's own Refresh button) shows "The messages could not be refreshed just now." A plain re-read that fails while the notice already says `afterSend` keeps that copy (still true).
- **F1 focus lost on Refresh.** `MessagesSection.tsx` no longer sets `disabled` on the header Refresh. `userRefresh` ignores a click while a read is in flight (`readInFlight`) and, to keep the old "held during a send" behaviour, while a send is in flight (`sendLock`).
- **F2 helper too broad.** `RecordTemplateAccess.test.tsx` `sectionActionsOf` drops `refresh` only from `record-section-messages`.
- **Same-second messages.** `messages-client.ts` `hasNewerMessages` uses `t >= newest` (second-precision `createdon`; the id check already excludes messages on screen).

Tests (`RecordMessages.test.tsx`): 4 under "a failed refresh says only what is true" (header Refresh, Show new messages, refreshSignal show the plain copy; a failed post-send re-read still says "was sent"), 1 same-second `hasNewerMessages` case, and the former "disables Refresh while its read is in flight" test now asserts the button stays enabled and focused and a second click makes no second read.

Fail before / pass after (`tests/RecordMessages.test.tsx`, fixes reverted in effect by writing the tests first): 5 failed / 74 passed (79) before; 79 passed after. The post-send "was sent" test passes before and after by design (it guards the kept behaviour).

rework: fix-round-1 task 034

## Owner decision 2026-11-23

- The owner accepted the background check: at most one GET a minute per open Messages section (uac-r2 has no objection).
- The owner asked for a manual Refresh. This is already in task 034: the Messages section header has a Refresh icon (`MessagesSection.tsx`, `userRefresh`). The `"Show new messages"` action also re-reads. Both are covered in `RecordMessages.test.tsx`.

