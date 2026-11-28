# Task 006 notes: FR-03 cleanup PR part 2 (§10.1 item 7 threaded reply, item 8 docs)

> 2026-11-25. Same branch and PR as task 005. Deviations from the POML, findings, measurements. Rework line is at the end.

## What changed

`EmailChannelSender.SendAsync` has a reply branch, taken when `InReplyToMessageId` is set (non-blank); every other send is the original `sendMail` path, untouched (the request it builds is regression-tested; see below).

Reply sequence (`SendReplyAsync`):
1. Find the parent in the **sending mailbox**: `InReplyToMessageId` holds the RFC-2822 Internet-Message-Id for email (`SendCommunicationRequest` doc, `ApplyReplyParentThreadFloorAsync` looks it up by `GetCommunicationByInternetMessageIdAsync`, then falls back to the Graph id). So: a value in angle brackets is looked up with `messages?$filter=internetMessageId eq '...'&$select=id&$top=1`; any other value is read as a Graph message id (`GET messages/{id}`, 404 = not found). The quote in the id is doubled (OData escaping).
2. `createReply` on the parent (a draft; Exchange sets In-Reply-To and References). The server-send marker, when present, goes in the `message` parameter of this request.
3. `PATCH` the draft with `BuildGraphMessage(request, carryServerSendMarker: false)`: subject, body, To/Cc/Bcc and (shared-mailbox mode) From, exactly as the `sendMail` path builds them.
4. `POST attachments` per file, then `POST send` on the draft (marked `NoTransientRetryOption`, like `sendMail`).
5. The Internet-Message-Id read-back from Sent Items is the existing `TryCaptureInternetMessageIdAsync` (a draft's `send` always saves to Sent Items).

Failure behaviour:
- Parent not found: `SdapProblemException` code `sdap.communication.reply_parent_not_found`, 404, nothing created or sent. It never falls back to a new message.
- Any Graph failure before `send`: `GRAPH_SEND_FAILED`, `sendOutcome: NotSent`, and the draft is deleted best-effort. A failure of `send`: classified exactly like `sendMail` (5xx / timeout = `Unknown`, draft kept; 4xx = `NotSent`, draft deleted).
- Attachment above 3 MB: `sdap.communication.reply_attachment_too_large`, 413, before anything is created (Graph adds a file to a draft in one request only below 3 MB; larger needs an upload session, which this branch does not implement).
- System-mail send that is also a reply: `sdap.communication.reply_system_mail_unsupported`, 400 (a draft's `send` always keeps a Sent Items copy, which system mail must not).
- Logs carry ids, mailbox, outcome only (ADR-015).

`CommunicationService` needed no change: `ChannelSendRequest.Communication` is the request, so `InReplyToMessageId` already reaches the sender (POML step 3 not needed).

The two Graph builders (`/me` and `/users/{address}`) have different Kiota types, so the reply calls live in a small private `ReplyMailbox` abstract class with two nested implementations; the methods are `internal` because ArchTest `ADR-007: Graph SDK types must be isolated` fails on a public Graph-typed member (it caught my first version). `MapGraphFailure` is the one place the `GRAPH_SEND_FAILED` problem is built; the two pre-existing catch blocks in `SendAsync` now call it too (same extensions, detail, status code; `EmailChannelSenderSendOutcomeTests` covers them).

Docs: ADR-036 full text :104 now says run records go to the `IBackgroundJobStore` (`InMemoryBackgroundJobStore` today, process-local) with `sprk_backgroundjobrun` as the target state, and that the idempotency probe is process-local today. `docs/data-model/sprk_backgroundjobrun.md:8` was already accurate ("deployed but unused"): not touched. `.claude/adr/ADR-036-...` already says target state: no `.claude/` edit needed. `communication-service-architecture.md`: reply branch described, three error codes added to the table.

## Deviations from the POML

- **No `createReplyAll`.** No request field asks for reply-all (grep: none in `SendCommunicationRequest`/`ChannelSendRequest`), and recipients are set explicitly from the request on the draft, so `createReply` is sufficient. Adding a reply-all flag would be new surface with no caller.
- **Marker via `createReply`, not via the draft update.** Microsoft Learn "Update message" does not list `internetMessageHeaders` among updatable properties, so the POML's "PATCH draft with ... server-send marker" cannot work. The `createReply` page says its `message` parameter takes message properties. This is **not verified against live Graph**: whether Exchange honours `internetMessageHeaders` in the createReply `message` parameter is exactly what the live test must confirm. Not treated as the POML escalation trigger ("cannot be carried") because that is unproven, not disproven. Consequence if it is dropped: a server-send reply carrying attachments is re-filed by the inbound path (duplicate documents), not data loss. See pending acceptance.
- The draft's subject is set to the request's subject (not Exchange's "RE: parent subject"), so the Sent Items read-back (which filters on the request's subject) keeps working and behaviour matches today's send. Outlook groups by conversation id/index from `createReply`, not by subject text.
- The reply body replaces the quoted original in the draft (callers compose their own body, as today).
- `DataverseRecordCreationTests` (existing, `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/`): its Graph stub answered every request 202 with no body, so its 15 email-reply tests (thread-join / privacy-floor, about the Dataverse record, not Graph) found no parent and failed under the new behaviour. Its stub now answers the parent lookup and `createReply` with an id (the parent exists); every other answer is unchanged. Not a weakening: those tests assert the record, and the parent-missing contract has its own tests.

## Tests

`tests/integration/regression/Intake10_1_Item7_EmailChannelSenderThreadedReplyTests.cs`: 15 cases (12 facts/theories), real `EmailChannelSender` and real Graph SDK over a recording `HttpMessageHandler` transport (hand-written, as the sibling `EmailChannelSender*Tests` do; Graph has no other boundary to drive; B1 bans `Mock<HttpMessageHandler>`, this is a recording fake, same shape as the siblings).

| Contract | Test |
|---|---|
| (c) createReply on the parent, draft sent, no `sendMail`, both modes | `Reply_WhenTheParentExists_...` (2 cases: app, user) |
| (d) no `InReplyToMessageId` = same Graph request (null, empty, whitespace; whole `sendMail` JSON compared) | `Send_WithoutAnInReplyToMessageId_...` (3 cases) |
| (e) parent not found = problem code, 404, nothing sent (RFC id and Graph id) | `Reply_WhenTheParentIsNotFound_...`, `Reply_WhenAGraphIdParentIsNotFound_...` |
| marker header carried, only `x-`, not in the PATCH | `Reply_WithAServerSendMarker_...`, `Reply_WithoutAServerSendMarker_...` |

Beyond the contract (one-line justification each: each is a branch this task added): Graph-id parent lookup (`..._IsFoundByIdAndRepliedTo`); attachment added to the draft before send, and the 3 MB refusal (a silent drop or an after-send add would lose a file); system-mail refusal; pre-send failure deletes the draft and is `NotSent`; send 5xx is sent once, `Unknown`, draft kept.

**Seeded failure (parent not found falls back to a new message):** in `SendReplyAsync` the `parentId is null` throw replaced by `return await SendAsync(request with { Communication = request.Communication with { InReplyToMessageId = null } })` (the defect this task removes). Result: **2 failed** (`Reply_WhenTheParentIsNotFound_...`, `Reply_WhenAGraphIdParentIsNotFound_...`), 13 passed. Restored: 15 of 15 pass.
**Against the pre-change sender** (HEAD version of `EmailChannelSender.cs` plus four stub constants so the file compiles): 12 failed, 3 passed. The 3 that pass are the "no `InReplyToMessageId`" cases, which proves the golden `sendMail` JSON is what the sender produced before this task.

Other suites (all after the final code): `EmailChannelSender*|DataverseRecordCreationTests|ServerSend|SystemMail|Intake10_1_Item7`: 195 passed, 0 failed. Earlier broader run `Communication|EmailChannelSender|Intake10_1|Email|ServerSend` (before the ADR-007 visibility fix, the dedup and the stub fix): 5095 passed, 15 failed (all `DataverseRecordCreationTests`, now fixed and re-run). ArchTests full run (after the fix): 2831 passed, 0 failed. `dotnet build -warnaserror` of the BFF: 0 warnings, 0 errors. First ArchTests run failed `ADR-007: Graph SDK types must be isolated` (public Graph-typed members on my nested classes): fixed, `ADR007*` 6 of 6 pass.

## Gates

- code-review (standard, whole diff): one F2 fixed in scope: `MapGraphFailure` duplicated the two catch blocks of `SendAsync` (a second copy of the problem mapping); the old blocks now call it. No other F-class finding. K-class: (K2) two near-identical `/me` and `/users/{x}` implementations are forced by Kiota's types and are documented; (K4) `ReplyMailbox` is an abstract class with 2 private implementations, not an interface, so no ADR-010 single-implementation smell. Security: the id is quote-escaped in the OData filter; a Graph-id value goes through Kiota's path encoding; the lookup is confined to the sending mailbox (an OBO caller can only reply to their own mail).
- adr-check (checklist run by hand over the diff): ADR-007 (fixed, see above), ADR-010 (no new interface or DI registration), ADR-015 (ids only), ADR-019 (stable `sdap.communication.*` codes via `SdapProblemException`), ADR-028 A5 (app-only with human approval upstream; no change), ADR-032 (no gate), ADR-036 (text fix), ADR-038 (new test at the KEEP `regression` path; no `Mock<HttpMessageHandler>`), ADR-045 (stays inside `CommunicationService` -> `EmailChannelSender`; no new send path, no other service calls Graph): compliant.

## BFF hygiene (§10 / `bff-extensions.md`)

- Placement Justification (for the PR): no new surface beyond one private reply branch in the existing `EmailChannelSender` (extends the canonical send in place; reuse-scan row 12 verdict "fix in place"); no endpoint, DI registration, package, column or job. Cost of doing nothing: replies arrive unthreaded, so FR-03(c) and FR-20 (threaded return) fail.
- Publish size: `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` (framework-dependent linux-x64, Release, PDBs included) of `origin/master` @ de76599830 (fetched this session) vs this branch @ 5e22bb7a83, each from a fresh `git archive` copy at a short path (`C:\w6m`, `C:\w6b`; no worktree created), zipped with `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`. Both 194 files. master 41.608 MB, branch 41.607 MB, delta -0.001 MB. Well under 60 MB.
- CVE: `dotnet list package --vulnerable --include-transitive` on the BFF: no vulnerable packages; no package reference changed.
- Hot path: `Communication/` files; no overlap with `origin/master` changes since the branch point in the files edited here (checked `git diff HEAD...origin/master --name-only`).

## Pending owner acceptance (not done: owner-gated)

POML step 7 and acceptance (c) live part: **not performed**. No Graph, Azure or Dataverse call was made, no `/bff-deploy`, no email sent. To close it the owner needs to: OK the dev deploy via `/bff-deploy`; name the test recipient; use the dev shared legal mailbox (it must hold the three `Application Mail.*` Exchange roles per the customer deployment guide §4.2.1: `createReply`, the draft `PATCH`, `send` and the draft cleanup need `Mail.ReadWrite`, not only `Mail.Send` as `sendMail` did; H14a already grants all three). Then: send one reply to a message that exists in that mailbox and record (1) `In-Reply-To` and `References` on the received message, (2) Outlook threads it under the original (screenshot path), (3) **a reply with a server-send marker and an attachment** carries `x-spaarke-server-send` (this is the unverified createReply-`message`-parameter assumption above; if absent, stop and escalate), (4) a reply to a deliberately wrong id returns 404 `sdap.communication.reply_parent_not_found` and sends nothing, (5) the reply's draft is not left in Drafts.

## Found in passing (not fixed)

- Stale mentions of the deleted job handlers in docs outside the POML list (from task 005): `docs/architecture/AI-ARCHITECTURE.md:407-408`, `sdap-bff-api-patterns.md:254-255`, `sdap-component-interactions.md:155`, `sdap-document-processing-architecture.md:141,346,565,637`, `sdap-overview.md:276-281`, root `FLOW-ANALYSIS.md`; `CommunicationEnrichmentService.cs:304` comment still names the old key `DocAnalysis:{documentId}`; `.claude/patterns/api/background-workers.md:14` names the deleted `DocumentProcessingJobHandler` (a `.claude/` edit for the main session). I did not fold them into this task's doc pass (not on the POML list; the owner can decide).
- `docs/architecture/communication-service-architecture.md` is otherwise out of date in places (it still lists a "Thread" step that is struck through; the daily send limit text). Not touched.
- The husky pre-commit runs `lint-staged`, which stashes and restores the working tree around the format step (`git stash` entry created by the hook, not by me); it needs `prettier` on PATH, so I committed with the main checkout's `node_modules/.bin` on PATH, as in 005.

rework: fix-round-1 task 006
