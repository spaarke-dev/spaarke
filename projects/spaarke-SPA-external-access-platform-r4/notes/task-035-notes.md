# Task 035 — Message threads (FR-22, #2453)

## Owner decision (step 1)

- 2026-11-24: the external message DTO has no thread key (verified: `ExternalMessageDtos.cs`, `messages-client.ts`). The owner chose **option A**: add the message's thread id (`_sprk_communicationthread_value`) to the external message DTO, reviewed by unified-access-control-r2. Option B (defer FR-22) was not chosen. Client grouping by subject was never offered.
- Tag deviation: `bff-api` added to this task because the DTO change is on the BFF.

## UAC review

- Requested from uac-r2 on 2026-11-24 (design before code).
- **Result: APPROVED with conditions** (uac-r2 `notes/decisions.md`, "2026-11-24: External messages may carry the thread GUID", commit `f23828d3cb` on `work/unified-access-control-r2`). The raw thread GUID is fine; no per-response opaque key.
- Conditions (binding), and how this task meets each:
  - a) Project only the id: no thread name, subject or other thread column. `ToExternalDto` sets one property, `ThreadId = message.ThreadId?.ToString("D")`; `ExternalMessageDto` gained exactly one property (`threadId`); the thread name is not read on this path.
  - b) No external route may accept `threadId` as input; posts keep deriving the thread from the record. No route, request DTO or query parameter was added or changed; `PostExternalMessageRequest` is untouched, and the post's 201 body leaves `threadId` null.
  - c) Never serialize `ThreadMessageDto` straight to an external caller; keep the allow-list projection. `ThreadMessageDto.ThreadId` is `[JsonIgnore]` (internal wire unchanged, test below) and the only external reader is `ToExternalDto`.
- 2026-11-24: uac-r2 checked the final diff (`8c08c7bcd1`) against the conditions: **APPROVED** (value from the same parsed row the filters and the open-thread gate decided on; no route or request change; internal wire unchanged).
- Tests added for the verdict: a filtered private-thread / internal-only message's thread id appears nowhere in the serialized body; a row with no thread lookup answers `threadId: null` (not a failure); the `ExternalNotificationDto` property set is pinned (reflection); the `ExternalMessageDto` allow-list is pinned twice (serialized and reflection).

## Files changed

BFF
- `Services/Communication/CommunicationThreadReadModels.cs`: `ThreadMessageDto` gains optional `Guid? ThreadId = null` (last parameter, `[JsonIgnore]`, so the internal thread wire is unchanged and every existing caller compiles as before).
- `Services/Communication/CommunicationThreadReadService.cs`: `BuildDto` sets `ThreadId` from the already-parsed thread lookup (empty = null). **No select or filter change:** the app-only external select already carries `_sprk_communicationthread_value` (D7 open-thread gate, `ExternalSelect`), so the escalation trigger "selecting the lookup needs a filter change" did not fire.
- `Api/ExternalAccess/Dtos/ExternalMessageDtos.cs`: `ExternalMessageDto` gains exactly one property, `threadId` (string?).
- `Api/ExternalAccess/ExternalMessageEndpoints.cs`: `ToExternalDto` projects it. The post's 201 body is built separately and leaves it null (the SPA re-reads after a send).
- Not touched: routes, errorCodes, filters, DI, packages, the POST request DTO.

SPA (`src/client/external-spa`)
- `src/api/messages-client.ts`: `ExternalMessage.threadId?: string | null`.
- `src/components/detail/groupMessagesByThread.ts` (new, pure): groups by `threadId` only.
- `src/components/detail/RecordMessages.tsx`: renders groups; a thread of 2+ messages is one bordered conversation (`record-message-thread`, brand start edge, "N messages in this conversation"); a message with no thread, or alone in its thread, is the plain item as before. Fluent v9 semantic tokens only; refresh, background check, "Show new messages" and the empty/error/loading states are untouched.

Tests
- `tests/integration/contract/Api/ExternalAccess/ExternalMessageEndpointsTests.cs` (contract KEEP path), `ExternalNotificationFeedTests.cs`.
- `tests/RecordMessages.test.tsx`, `tests/groupMessagesByThread.test.ts` (new), `tests/NoRemoteImagesInMessages.sources.test.ts` (new).

## Tests and evidence

BFF (filtered; the full suite was not run, other agents build here):
- `ExternalMessageEndpointsTests`: 137 pass (6 new; the two existing allow-list tests were updated to the one list). New: threaded messages carry `threadId` (two threads); a row with no thread lookup answers `threadId: null` (internal systemuser caller; an external caller never receives a no-thread row, `ExternalRead_ItemWithNoThread_IsHidden`); must-fire: a private-thread message and an internal-only message are absent AND neither thread id nor message id appears anywhere in the serialized body; must-not-fire: a normal message beside them is listed with its `threadId`; `ExternalMessageDto` allow-list equality twice (serialized shape and reflection over JSON names; both fail if a property is added without updating `AllowListedMessageFields`); `ThreadMessageDto` serialized for the internal wire has no thread id.
- `ExternalNotificationFeedTests`: new reflection test pins `ExternalNotificationDto`'s property set (UAC condition 3); the existing serialized-shape test stays.
- Wider filtered run (`ExternalMessageEndpointsTests`, `ExternalNotificationFeedTests`, `ExternalMessageSendTests`, `CommunicationOpenThreadGateTests`, thread-read classes): 414 pass, 0 fail.
- `Spaarke.ArchTests`: 2783 pass, 0 fail.
- Fail-before: with the `ThreadId = ...` line removed from `ToExternalDto`, 2 fail (`GetMessages_ThreadedMessages_CarryTheirThreadIdAsTheOnlyThreadField`, `GetMessages_NormalMessageBesideFilteredOnes_IsStillListedWithItsThreadId`), 135 pass; restored, 137 pass. (The allow-list tests pass either way, by design: the property exists on the DTO; the projection tests carry the proof.)

SPA (vitest):
- `RecordMessages.test.tsx` 85 pass (6 new: replies group oldest first with the conversation at its latest message; an unthreaded message stands alone beside same-subject/sender/time messages; different threads are not merged and a thread of one is a plain item; a remote image `src` (https://example.com/x.png) is not rendered, inside a conversation; a no-image HTML body keeps its markup (must-not-fire pair); a re-read replaces the grouping).
- `groupMessagesByThread.test.ts` 5 pass; `NoRemoteImagesInMessages.sources.test.ts` 5 pass (scan of `src/` with comments stripped: no `allowRemoteImages`, no options argument to `sanitizeEmailHtml`, the one `dangerouslySetInnerHTML` is `{ __html: sanitizedHtml }`; the matcher has must-fire and must-not-fire controls).
- Ten message-related files together: 516 pass, 0 fail.
- Fail-before: with `groupMessagesByThread` stubbed to one group per message, 4 fail (2 pure, 2 render); restored, all pass.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- `npx tsc --noEmit` (external-spa): clean. `npm run build` (external-spa): succeeds (the existing chunk-size warning only). `eslint` on the six touched SPA files: clean. `client-lint` does not cover `external-spa` (its packages list has no such id), so it was not run for this package.
- `dotnet list ... package --vulnerable --include-transitive`: no vulnerable packages; no csproj or props change.
- Publish-size delta (main session, measured): fresh master `0362599b94` (the master this branch merged) vs the branch, both `dotnet publish -c Release`, zipped with `Compress-Archive`: master 43,567,222 B (41.55 MB), branch 43,569,881 B (41.55 MB), **delta +2,659 B**; 194 files each; `Sprk.Bff.Api.dll` 28,940,800 → 28,941,312 B (+512 B). Under the 5 MB / 55 MB / 60 MB thresholds.
- `/conflict-check` (main session, 2026-11-24): no open PR (29 checked) touches this task's files; no master commit since the merge touches them. The BFF hot path is shared with uac-r2 and ebilling-r1 (soft warn, no file overlap).
- Live UI test (remote image network log, dark mode, high contrast): not run; it belongs to task 038's E2E after the deploy.

## Placement Justification (for the PR)

Existing route (`GET /api/v1/external/{root}/{id}/messages`), existing service (`CommunicationThreadReadService`), extension only: one optional field on an existing DTO and one projected property. Cost of doing nothing: replies cannot be grouped as the model-driven app groups them (grouping by subject was refused), so FR-22 fails. No new DI, package, endpoint or background work.

## Known limits and deviations

- **Order.** POML step 4 says "thread order by latest message, replies oldest first inside a thread" (corrected quote; an earlier draft of these notes misquoted it as "newest first"). The list stays oldest to newest, as before (the new-message indicator sits under it): a conversation sits where its latest message is (most recently active last), replies oldest first inside. Flipping to newest first is a small change in `groupMessagesByThread` if the owner wants it.
- A conversation is drawn only for 2+ messages sharing a `threadId`; a thread with one visible message looks as it did before.
- Order inside a thread is the read's order (the endpoint orders `createdon` ascending); the client does not re-sort by time.
- External callers never receive a message without a thread (D7 open-thread gate), so a null `threadId` reaches the SPA only for an internal systemuser caller, or from a BFF that predates this change; the SPA treats null, absent and blank alike as standalone.
- The 201 body of a post carries `threadId: null` (not read back); the SPA re-reads after a send, so the new message is grouped from the read.
- A message whose thread is hidden by the open-thread gate is filtered before grouping (unchanged filters), so a conversation can show fewer messages than the thread holds.
- Tag deviation: `bff-api` added to this task (DTO change on the BFF), as in the POML notes.

## Adversarial review (opus, one pass, 2026-11-24)

- No F1, no leak. Leak check clean: `ThreadMessageDto` reaches external callers only through `ToExternalDto`; the notification feed projects explicitly; no cache, SignalR or MCP serializer of it; no route takes `threadId` as input.
- F4 publish-size delta → done (above). F4 `/conflict-check` → done (above). F2 notes misquote of the POML → corrected (above). UAC review → recorded above, cited in the PR.
- Known limits, not fixed (K, one line each):
  - K4: the source scan misses a sanitizer call whose first argument contains parentheses plus an options argument, and checks the HTML sink in `RecordMessages.tsx` only; today there is one sanitizer call and one sink, both correct, and a literal `allowRemoteImages` anywhere under `src/` fails it.
  - K3: "N messages in this conversation" counts visible messages only (hidden, gated or past the 500-row scan are not counted); no information leaks.
  - K4: the group's `aria-label` repeats the visible count; the outer list counts groups; a single message that gains a reply remounts as a group, so focus inside its body is lost on that refresh.
  - K1: thread ids compare case-sensitively; the only producer, `ToString("D")`, is always lowercase.

rework: fix-round-1 task 035
