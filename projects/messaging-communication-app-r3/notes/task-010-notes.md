# Task 010 Notes — Characterize CommunicationTimeline core + SendEmailDialog

**Status**: Characterization tests authored, all green. No production file modified. See test files listed below.

## ESCALATION (per POML `<escalation>` trigger) — message identity is EMAIL-STRING, not systemuserid

**Finding**: The conversation core's message-identity model is coupled to **email-address strings**, not a `systemuserid` (Dataverse system user GUID), end to end:

- `TimelineMessage.sender` (`CommunicationTimeline.types.ts`) is typed `string | null` and populated directly from `IThreadMessageDto.from`, which is `string | null`.
- The wire DTO field mirrors the BFF's `ThreadMessageDto.From`, which in turn is read off `sprk_from` (`Sprk.Bff.Api/Services/Communication/CommunicationThreadReadService.cs` line ~780: `From = TryString(row, FromField)`), a plain text/email column — not a lookup to `systemuser`.
- Backend-wide, `From` is populated from `message.From?.EmailAddress?.Address` (Graph), `senderResult.Email`, `userEmail`, etc. (`CommunicationService.cs`, `IncomingCommunicationProcessor.cs`, `GraphMessageNormalizer.cs`) — always an email string, never a `systemuserid`/GUID.
- `CommunicationTimeline.tsx`'s own "Quote into message" handler (`handleQuoteIntoMessage`) builds the reply recipient list as `to: message.sender ? [message.sender] : undefined` — i.e., it re-uses the sender STRING as the next recipient. There is no `systemuserid`-keyed lookup anywhere in this component tree.
- Grepped the entire `CommunicationTimeline/**` tree and the DTO layer for `systemuserid`/`SystemUserId`/`isOwn`/`isMine`/`currentUser` — **zero matches**. No "is this message mine" alignment logic exists today (the timeline renders a flat left-aligned list, no bubble left/right split).

**Why this matters for FR-02/18 (sender-identity alignment)**: R3's planned bubble `ConversationView` (task 011+) will very likely need "is this message from the current user" to decide left/right bubble alignment — the natural, WRONG shortcut is `message.sender === currentUserEmail`. That breaks for:
- Shared-mailbox sends (`sendMode: 'sharedMailbox'`, the EmailComposer default) — the persisted `sprk_from` is the **shared mailbox's** email, not the acting user's, so every teammate sending from the shared mailbox would collapse to the same "sender" identity.
- Display-name vs. address mismatches, case differences, or a user's email changing over time.
- External participants whose email happens to coincide with an internal user's alias.

**Recommendation for task 011**: Do NOT inherit `sender`-string equality as the identity signal for alignment/ownership. If the BFF doesn't already stamp a `systemuserid`-keyed "sent by" field on `sprk_communication` rows (worth checking — `CommunicationParticipantIndexer.cs` has a `RoleFrom` participant-role concept that may be closer to what's needed), that is a backend gap task 011 (or a dedicated backend task) needs to close before FR-02/18 can be implemented correctly. This was NOT something task 010 could fix (characterization-only, no production changes) — flagging per the POML's explicit escalation trigger so task 011 doesn't silently inherit the email-string assumption.

## Other findings for Phase 2/3 authors

1. **`buildTimeline` does NOT do day-boundary grouping today.** The task 010 POML brief listed "day-boundary grouping" as something to characterize, assuming it already exists. Reading `CommunicationTimeline.buildTimeline.ts` shows `buildTimeline` returns a flat `TimelineEntry[]` (`{ message, depth }` only) — no day label, no group key, no divider rows, and `CommunicationTimeline.tsx` renders `timeline.map(entry => <MessageRow .../>)` directly with no day dividers in between. **Day-boundary grouping is Phase 2/3 net-new work**, not a modification of existing behavior. `CommunicationTimeline.characterize.buildTimeline.test.ts` pins the current flat shape (exact `{message, depth}` entry keys, no synthetic divider entries) so that whoever adds day-grouping does so as an additive, loudly-diffed change.

2. **`EmailComposer`'s own `archiveToSpe` default (`true`) differs from the lower-level `sendCommunication()`/BFF wire default (`false`).** `EmailComposer.reducer.ts` `initialState`: `archiveToSpe: props.archiveToSpe ?? true`. `communicationApi.ts` `sendCommunication()`: `archiveToSpe: opts.archiveToSpe ?? false`. The composer's default only matters when a caller (e.g. `SendEmailDialog`) doesn't pass `archiveToSpe` explicitly — which is the common case today. Not a bug, but worth knowing before task 020 adds new `SendEmailDialog` callers that assume the BFF-level default.

3. **`MAX_DEPTH_HOPS` (64) pathological-chain guard**: a strictly linear (non-cyclic) reply chain longer than 64 hops degrades the tail message to `depth: 0`, same defensive posture as the cycle-safety guard — this was previously exercised only by a 2-3-message cycle test, not a genuinely long non-cyclic chain. Now characterized in `CommunicationTimeline.characterize.buildTimeline.test.ts`.

## Test files created (all new, all green — no production file touched)

1. `src/client/shared/Spaarke.UI.Components/src/components/CommunicationTimeline/__tests__/CommunicationTimeline.characterize.reducer.test.ts` — pins the THREAD-ID-MODE reducer slice (`SET_THREAD`/`MERGE_POLL`/`SET_UNREAD`/`ADVANCE_LAST_SEEN`/`BEGIN_SEND`/`END_SEND`/`SET_ERROR`/`CLEAR_ERROR`) — the existing reducer test file covers ONLY the regarding-mode slice.
2. `src/client/shared/Spaarke.UI.Components/src/components/CommunicationTimeline/__tests__/CommunicationTimeline.characterize.buildTimeline.test.ts` — pins the day-grouping absence (see finding #1) and the `MAX_DEPTH_HOPS` non-cyclic pathological-chain guard.
3. `src/client/shared/Spaarke.UI.Components/src/components/CommunicationTimeline/__tests__/CommunicationTimeline.characterize.poll.test.ts` — pins `useThreadPoll`/`useRegardingPoll` (previously ZERO test coverage): ~5s default cadence, cursor pass-through-from-ref (not computed by the hook), overlapping-in-flight-poll guard, `document.hidden` pause, loading/error propagation, `enabled: false` suspension.
4. `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/__tests__/SendEmailDialog.characterize.test.tsx` — pins the actual send-path invocation (request payload shape sent to `authenticatedFetch`), `onSent`→`onClose` ordering, `onError` (send-failure) behavior, the current prop contract, and a dark-theme (ADR-021) render — filling the gap `wrappers.test.tsx` (task 023) leaves (that suite covers render/open-close/Cancel-maps-to-onClose but never drives an actual send).

## Verification

- `npm install --legacy-peer-deps --no-audit --no-fund` — 793 packages installed (no local `node_modules` existed before this task).
- `npx jest --testPathPatterns "characterize"` — 4 suites / 43 tests, all green.
- `npx jest --testPathPatterns "CommunicationTimeline|EmailComposer"` — full 15 suites / 193 tests, all green (no regression in pre-existing tests).
- `npx tsc --noEmit -p tsconfig.json` — 2 pre-existing errors (`@spaarke/auth`, `@spaarke/sdap-client` unresolved `file:../...` workspace deps not vendored in this worktree) unrelated to this task's files; zero errors attributable to the 4 new test files.
- `git status --porcelain` / `git diff --name-only` — only the 4 new test files are untracked; zero production files modified.
