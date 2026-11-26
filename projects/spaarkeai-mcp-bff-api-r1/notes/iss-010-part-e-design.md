# ISS-010 part E: the decision emails' post-send work leaves the commit request (design note, 2026-11-25)

> Status: DESIGN, for review. field-mapping-server-write-path-r1 reviews first, then spaarke-ontology-platform-r1. No code changed yet.
> Inputs: `iss-010-commit-latency.md` ("Part D scoping", "Part E constraints"); `decisions.md` 2026-11-25 (email rule, ontology's answers, where a SharePoint failure is reported; D-216 amendment). Paths are under `src/server/api/Sprk.Bff.Api/`; `CS` = `Services/Communication/CommunicationService.cs`.

## 1. Today's post-send sequence (both email paths)

`SendAsync` `CS:2412`. A Message, including every private message (056b), branches off at `CS:2484-2487` before any email code. `SendMode.User` goes to `SendAsUserAsync` `CS:2866` (`:2496-2499`). Otherwise the shared-mailbox path continues in `SendAsync`. Line pairs are shared path / user path. Every step from 5 onward runs after the send and catches its own faults, so none of them can fail the send (the outer catch `CS:2829` / `:3185` maps only faults before the return).

| # | Step | Lines (shared / user) | Client | Can fail the send? |
|---|---|---|---|---|
| 3-4 | Footer, then the send (`EmailChannelSender`; the Sent-Items read for the Internet-Message-Id happens inside it) | 2585-2597 / 2949-2965 | Graph (app-only / OBO) | **Yes**: this is the send |
| 4b | Daily count (shared path only) | 2600-2613 | Dataverse (via the account service) | No |
| 5 | `sprk_communication` create (`CreatePreparedRecordAsync` `CS:1567`; the row built before the send carries `sprk_correlationid`, filing, `sprk_isinternalonly`, `sprk_sentby`: `CS:3226-3245`) | 2616-2627 / 2968-2979 | SDK `IGenericEntityService` | No (best-effort; a null id skips 5b-8) |
| 5b | Internet-Message-Id stamp (`CS:4971`) | 2631-2635 / 2983-2987 | SDK | No |
| 5c | Thread resolve or create and assign (`ResolveOutboundThreadAsync` `CS:844`, `CreateThreadPrivate` `:879`) | 2638-2641 / 2990-2993 | SDK (via `ThreadResolver`) | No |
| 6a | Archive decision `ShouldArchiveOutgoing` (`CS:1545`), then the `ArchiveOutgoingOptIn` read | 2645-2674 / 2999-3029 | SDK | No |
| 6b | `.eml` archive `ArchiveToSpeAsync` (`CS:3804`): owner, SPE PUT, `sprk_document` create, access refresh; then `EnqueueDocumentAnalysisAsync` (`CS:4551`, already a job) | 2675-2708 / 3030-3062 | SDK + Graph/SPE (app-only) + Service Bus | No (sets `ArchivalWarning`) |
| 6c | Outbound attachment copies (only `ArchiveToSpe` with attachments) | 2713-2728 / 3067-3082 | SDK + SPE | No |
| 7 | `sprk_communicationattachment` rows | 2739-2773 / 3093-3128 | SDK | No |
| 8 | `EnrichAsync` (AI triage and propose; RAG indexing needs the archive id: `CommunicationEnrichmentService.cs:345`) | 2780-2798 / 3135-3153 | SDK + Azure OpenAI | No |
| 9 | Participant index | 2802-2806 / 3158-3162 | SDK | No |
| 10 | `communication-arrived` (outbox, SignalR ping, per-thread bell; skips silently without a thread: `CommunicationArrivedProducer.cs:170-176`) | 2811-2812 / 3167-3168 | SDK + SignalR | No |

## 2. The split

A new server-only request flag, `[JsonIgnore] internal bool DeferPostSend { get; init; }` on `SendCommunicationRequest`, follows the `SystemMail`/`ServerSend`/`CreateThreadPrivate` pattern (`Models/SendCommunicationRequest.cs:154-172`). A request body can never set it.
- **Guard** (beside `CS:2429-2473`): `DeferPostSend` with a type other than Email, `AttachmentDocumentIds`, `ServerSend`, `SystemMail` or `ThreadId` throws `InvalidOperationException` before anything is sent. The decision senders use none of these.
- **Stays inline, unchanged:** steps 3-5, **5b**, 6a and 7.
  - Step 5b stays inline, a reason the constraints did not list: an inbound reply is matched by the W1 rung on `sprk_internetmessageid`. A reply that arrives before the job runs would otherwise miss its thread. It costs one update.
  - Step 6a stays inline so the opt-out and no-row warnings (`CS:3023-3029`, `:3084-3090`) are unchanged.
- **The enqueue:** when `DeferPostSend && communicationId.HasValue`, ONE call to `EnqueuePostSendAsync(...)` replaces:
  - on the user path, 5c (`CS:2989-2993`), 6b-6c (`:3030-3083`) and 8-10 (`:3130-3169`);
  - on the shared path, `:2637-2641`, `:2675-2729` and `:2775-2813`.
  - The response then carries `ArchivedDocumentId = null` and no archive-failure `ArchivalWarning`.
- **The job's order (today's order):** thread → archive → enrichment (with the archive id) → participant index → arrived.

## 3. The job

**Type:** `CommunicationPostSend`. Handler: `Services/Communication/CommunicationPostSendJobHandler.cs`, an `IJobHandler` on the main queue, consumed by `ServiceBusJobProcessor` (`JobSubmissionService.SubmitJobAsync` `:60`). It is not on `sdap-communication`, whose processor routes only two hard-coded types (`CommunicationJobProcessor.cs:150-152`). The contract and submit path copy `AssignTaskNotifier.FallBackAsync` (`Services/Signals/AssignTaskNotifier.cs:546-561`).
- `SubjectId` and `IdempotencyKey`: `comm-postsend:{communicationId:N}`. `CorrelationId` is the send's own.

**Payload** (ids and flags only: no address, subject, body or bytes; ADR-015, ADR-004):
`CommunicationPostSendPayload(Guid CommunicationId, Guid? RequestedByObjectId, bool Archive, bool Enrich, bool CreateThreadPrivate)`.
- `CreateThreadPrivate` is the one fact not on the row.
- Everything else is read from the row by id: To, Cc, Bcc, From, subject, body, `sprk_inreplyto` and `sprk_sentby`. The job reads them with `ReconstructEnvelopeAsync` (`CS:417`) and the archive read.

**Idempotency per step:**

| Step | Guard | Strength |
|---|---|---|
| Thread | Skip when the row's `sprk_communicationthread` is already set. Needed because create-when-absent (`ThreadResolver.cs:184-190`) would mint a second thread. | Durable |
| Archive | `ArchiveExistingAsync` → `FindExistingArchiveDocumentAsync` (`CS:306`, `:492`), plus the lock in §5 | Durable + lock |
| Enrichment | Marker `…:enriched`, written after `EnrichAsync` returns (it never throws) | Cache, fails open (#984) |
| Participants | The indexer is idempotent per (role, address) (`CommunicationParticipantIndexer.cs:36`, `:85`) | Durable |
| Arrived | Marker `…:arrived`, written after the emit. The bell inside is deduplicated per thread (`CommunicationArrivedProducer.cs:273`). | Cache (known limit: a crash between emit and mark can repeat an outbox row) |

**Can the job just call `ArchiveExistingAsync`?** Yes for the Assign Task email (HTML, To only). **Not as-is for the inquiry.**
- `ArchiveExistingAsync` rebuilds the request with `BodyFormat.HTML` (`CS:346`) and reads no `sprk_bcc` or `sprk_bodyformat` (`CS:285`).
- The inquiry is `PlainText` with an optional Bcc (`Services/Signals/Actions/BudgetInquiryExecutor.cs:150`, `:153`). The `.eml` generator honours both (`EmlGenerationService.cs:42-44`, `:56-58`).
- **Proposal:** for an OUTGOING row, `ArchiveExistingAsync` also reads `sprk_bodyformat` and `sprk_bcc`. This also makes a manual Save to SharePoint match the send-time `.eml` (field-mapping-r1 decides; **Q2**).

**Retry and poison.** Fixed limit: 5 logical deliveries, 1/2/4/8-minute back-off, ~15 minutes in all (`JobContract.cs:64`, `:85-97`, `IsFinalDelivery` `:117`).
- **Poisoned**, bell at once: the owner refusal (409) and `ARCHIVE_NOT_CONFIGURED`; no retry can fix them. **Success**, Information log, no bell: `COMMUNICATION_NOT_FOUND` (the row was deleted; nothing to fix).
- **Failure**, redelivered: every other archive fault, and a refused archive lock. A non-final failure skips enrichment for that delivery (RAG indexing needs the document); participants and arrived still run. On `IsFinalDelivery` the job runs enrichment with no document (today's behaviour after a failed archive), bells and returns Failure; `ServiceBusJobProcessor.cs:246-250` dead-letters it as `MaxRetriesExceeded`, visible and re-drivable.

**Defined final state.** One Error line, `CommunicationPostSendEvents.ArchiveFailedFinal`: JobId, CommunicationId, attempt, delivery, exception type, error code; never the message, which can name an address. Enrichment, participants and arrived never fail the job (best-effort today too). "Done" is Information `PostSendCompleted` with per-step outcomes.

**Placement.** ADR-004 / ADR-052: a queue consumer is ADR-004 work. In the BFF by ADR-052 signals B2 (uses BFF domain code) and B3 (low volume); every step is already app-only and no caller token leaves the request. §11 is in §8.

## 4. Warnings that change, the bell, and the email bell

**D-216 today.**
- `CS:3057` (archive threw) → `InquiryEmailSender.SendAsync` (`BudgetInquiryExecutor.cs:101-102`) → `SendEmailAsync` (`:395-409`) → `ToOutcome` (`DecisionActionExecutors.cs:1638`, `Warning = "inquiry_email_not_archived"`) → `ToStep` (`DecisionCommitService.cs:2340-2349`).
- The warning reaches the response twice: on `steps[].Warning` and on `warnings[]` (`DecisionCommitService.cs:1522-1523`).
- **Under the deferred flag:** the archive-failure cause disappears from the response. It is replaced by the final-failure bell (amended D-216).
- **Unchanged:** the same code still fires for the opt-out (`CS:3025`) and for "no row was written" (`CS:3086`). Both stay inline.

**Assign Task has no archive warning today.** `SendAsUserAsync`/`SendAsSystemAsync` discard the response (`AssignTaskNotifier.cs:393-399`). So nothing disappears from its response. The bell is new: it is the first notice of an Assign Task archive failure.

**The archive bell.** It reuses `NotificationService.CreateNotificationAsync` (`Services/NotificationService.cs:61`), as `BellAsync` does (`AssignTaskNotifier.cs:717`).
- **Recipient:** the row's `sprk_sentby`, the decider. The user path sets it to the caller's enabled systemuser (`CS:3238`); the shared path sets it to the caller's oid.
- **Link only when the decider can use it.** It is not true by construction: the row is team-owned and may be a secure child, and access can change during the ~15-minute retry window. So at bell time the job asks `CallerRecordAccessProbe.GetPrincipalRightsAppOnlyOrUnknownAsync(userId, "sprk_communications", id)`, the call the recipient gate uses (`AssignTaskRecipientGate.cs:398`).
  - Read and AppendTo: `actionUrl` `/main.aspx?etn=sprk_communication&id={id:D}&pagetype=entityrecord` (the `AssignTaskNotifier.cs:631` shape), `actionTitle` "Open email". The manual route also needs AppendTo (`Api/CommunicationEndpoints.cs:346-348`).
  - Otherwise, or no answer: no link (fail closed).
- **Names (D-123):** fixed text, nothing to withhold. Title "An email you sent was not saved to SharePoint"; body "Open the email and choose Save to SharePoint.", or "Ask your administrator to save it." with no link. No record name and no subject (the Assign Task subject carries the record label), so the `RecordFacts` withholding rules are not needed.
- **Once.** The marker `…:archive-bell` is set BEFORE the create (at most once); if the create faults, the Error line `ArchiveBellNotCreated` with the communication id is the discoverable state. Category `documents`; `regardingId` = the communication.

**The email bell (separate; it does not fit the same job).** A failed system-mailbox send has no `sprk_communication` row, so there is no communication to link to.
- **Same pattern, in `AssignTaskEmailJobHandler`** (`Services/Signals/AssignTaskEmailJob.cs:61-146`). On `Poisoned` (`:132`), or `Failure` when `IsFinalDelivery` (`:88`, `:140`), it bells the assigner: `AssignedByObjectId` → systemuser.
- **Link:** to the task, after the same probe on `sprk_events`. Fixed text, no names.
- **Once:** marker `assign-task-email-bell:{taskId:N}`. That is once per task, not per decision, because the payload has no decision id (**Q5**).
- **Pre-existing gap:** a queued fallback returns no warning (`AssignTaskNotifier.cs:462`, `:532`). The optional "being sent from the shared mailbox" note needs an owner-approved code. It is not in this change.

## 5. Risks

- **The job runs before the row is committed: not possible.** The enqueue happens after `CreatePreparedRecordAsync` returns, and an SDK Create has committed by then. A create that faulted gives no id, so nothing is enqueued (today's skip). A 404 in the job means the row was deleted (§3).
- **Queue outage: inline fallback.**
  - If `SubmitJobAsync` throws or exceeds a 2 s bound, the send runs today's inline tail. That code is unchanged, so the archive warning returns in that case only. Nothing is lost.
  - `AssignTaskNotifier.FallBackAsync` does NOT do this. On an enqueue failure it logs an Error and returns false, which turns into `assignee_email_not_sent` in the response (`AssignTaskNotifier.cs:567-572`).
  - The 2 s bound plus the ~5 s inline tail stays under 30 s only once parts A-D land (**Q4**).
- **Manual Save to SharePoint racing the job: NOT race-safe today.** `ArchiveExistingAsync` checks then creates (`CS:306` → `:364` → `CS:3942`) with no lock. Two concurrent calls both PUT the same path `{commId:N}_{name}.eml`. Graph's path PUT is a silent replace (`CS:3857-3860`). The result is two `sprk_document` rows on one SPE item, the K3 hazard (`CS:521-527`).
  - **Guard:** inside `ArchiveExistingAsync`, around find → create, take `IIdempotencyService.TryAcquireProcessingLockAsync("comm-archive:{id:N}", owner, 2 min)` (`Services/Jobs/IIdempotencyService.cs:44`). Owner = job id, or the request trace id.
  - A refused lock is a retry for the job. For the manual route it is a 409 `archive_in_progress`.
  - The lock fails open (#984). A hard guarantee needs a Dataverse alternate key on a new `sprk_document` column set only on `.eml` archives. That is a schema decision (**Q3**).
- **Messages lag (D-217).** In the normal case, delivery (~1 s) plus the moved work (~4-6 s) means the `.eml` Document and the thread appear about 5-10 s after the commit. Under retries it can take up to ~15 minutes before the bell. The PR states this.
- **Enrichment re-run.** Its create-task and propose steps are not proven idempotent. The marker makes a repeat rare, not impossible (**Q6**).

## 6. Scope, order, D-17

- **Deferred mode (`DeferPostSend = true`), set only by:**
  - `InquiryEmailSender.Build` (`BudgetInquiryExecutor.cs:146-170`);
  - `AssignTaskNotifier.Request` (`AssignTaskNotifier.cs:578-591`), covering the in-request user send and the external system-mailbox send.
- **Not deferred:**
  - the fallback job's own send (`:382-388`): it runs off the request already, so there is nothing to save;
  - the Console and REST send (`Api/CommunicationEndpoints.cs:552`, `:823`);
  - `EmailDispositionSender.cs:107`, `ApExportDeliveryService.cs:87`, every `RegistrationEmailService` send and replies;
  - the decision's own `send-email` / `send-reminder` actions (`DecisionRouteCores.cs:141`, called from `DecisionActionExecutors.cs:1036`): **Q1**.
- **Private messages are untouched.** `PrivateMessageSendGate` refuses a private context that is not a Message (`Access/PrivateMessageSendGate.cs:159-160`). A Message returns at `CS:2484-2487`, before any line this design touches. The new guard also refuses the flag on a non-Email request.
- **Order unchanged:** validate → writes → Next steps → email → Decision Record → close.
  - The inquiry's send is phase 4, before the record; Assign Task's is step 7, after it (`DecisionCommitService.cs:1442-1483`, `:1528-1540`). Only work after the send moves.
- **D-17:** the record's steps carry only `Written` ids (`DecisionCommitService.cs:2360-2369`). For the inquiry those are `sprk_servicerequest` and `sprk_communication`, both written inline. No thread, Document, enrichment or warning is in the record.
- **Replay:** a replay returns the stored outcome, and done steps are skipped (`:1444-1448`), so the send and the enqueue do not repeat. Arrived drives only SignalR, the outbox and a bell; no commit response reads it.

## 7. Expected saving (per deferred send)

Moved SDK calls (code-mapper estimates): archive 13-16, thread 8-11, enrichment 6-10, participants 7-9, arrived 5-7, so **39-53 calls = 3.4-4.7 s** at ~88 ms each. Plus the SPE PUT (~0.3-0.6 s, inferred) and 0-1.3 s of AI (1.3 s if the two `gpt-4o-mini` calls are enrichment's, 0 if they were the queued analysis job's); minus ~0.1 s for the enqueue. **Net ~3.5-6.5 s per send.** The live four-action call has one Assign Task, so E alone takes it from ~27 s to ~21-23 s; the 15 s target also needs parts A-D.

## 8. Tests, files, §11, review

**Tests:** unit tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/`; the integration tests listed run unchanged.
- **Must fire:** a deferred send makes one `SubmitJobAsync` and no archive, enrichment, thread, indexer or arrived call; a failed send still returns `assignee_email_not_sent` / `SendFailed` (`AssignTaskNotifierTests`, `BudgetInquiryEmailFilingTests`); an enqueue fault runs the inline tail; a final archive failure gives exactly one bell (also across two final deliveries and a re-drive) and one `ArchiveFailedFinal` line; a poisoned archive bells at once; a manual archive in flight refuses the job's, which later reports AlreadyArchived with one Document; the job's `.eml` of a PlainText + Bcc inquiry equals the send-time one; no link when the decider lacks Read.
- **Must not fire:** every §6 caller sends with `DeferPostSend = false`, and `CommunicationServiceTests`, `…EmailSendThreadTests` and `…ArchiveTests` pass unchanged; a replayed commit makes no `SubmitJobAsync`; a redelivered job makes no second thread, Document, participant row or arrived emit; the flag on a Message request throws.
- **Private path:** `PrivateMessageSendTests`, `PrivateMessageSendContractTests` and `CommunicationServiceMessageSendTests` pass unchanged.

**Files:** `Models/SendCommunicationRequest.cs` (the flag); `CS` (the guard, the branch at both send sites, `EnqueuePostSendAsync`, and in `ArchiveExistingAsync` the outgoing body format and Bcc plus the lock); `CommunicationPostSendJobHandler.cs` (new); `CommunicationModule.cs` (one `AddScoped<IJobHandler, …>`); `CommunicationEndpoints.cs:882-891` (a refused lock → 409); `BudgetInquiryExecutor.cs` and `AssignTaskNotifier.cs` (set the flag); `AssignTaskEmailJob.cs` (the email bell).

**§11 answers:**
- **Flag:** extends an existing request (the `ServerSend`/`CreateThreadPrivate` precedent).
- **Handler:**
  - *Existing:* `AppOnlyDocumentAnalysis` needs a Document that does not exist yet and is already enqueued by the archive (`CS:390`). `IncomingCommunication` and `EmailAnalysis` start from a Graph message and an `.eml` respectively. `AssignTaskEmail` sends; it does not process after a send.
  - *Extension:* none of them takes a sent communication id. Folding the work into `AssignTaskEmail` would leave the inquiry out.
  - *Cost of doing nothing:* 39-53 SDK calls plus the AI stay in every decision email, and ISS-010's target is missed.
- **Lock:** reuses `IIdempotencyService`.

**Review sequence:** field-mapping-r1 (the communication core, #3133) → ontology-r1 (D-216, D-217, tasks 177 and 216, `Services/Signals/**`) → uac-r2, only if the probe use in the bell counts as an access change. Then one Opus adversarial review, a publish-size delta and the `~Signals` and ArchTests.

## 9. Open questions

- **Q0 (blocker, owner and ontology).** This project's spec says "MUST NOT add … any background work (ADR-052)" (`spec.md:345`; project `CLAUDE.md` §2). A new `IJobHandler` is background work. Two ways forward:
  - Path A, a project exception (spec ADR Tensions, cited in the PR);
  - field-mapping-r1 owns the handler (it is their pipeline), and this project only sets the flag.
- **Q1.** Should the decision's `send-email` / `send-reminder` actions take the flag too? Same cost, same safety.
- **Q2.** Does `ArchiveExistingAsync` read `sprk_bodyformat` and `sprk_bcc` for outgoing rows? This changes a manual archive's `.eml` to match the sent email.
- **Q3.** Is the lock enough, or is a `sprk_document` alternate key wanted (schema)?
- **Q4.** The inline fallback's 2 s enqueue bound: accept it, or bell instead (no inline work) on an enqueue fault?
- **Q5.** The email bell once per task, or once per decision (add the decision id to `AssignTaskEmailPayload`)?
- **Q6.** Is `EnrichAsync` safe to re-run (create-task, propose, assessment-event), or does it need a durable guard?

## field-mapping-r1 review (2026-11-25)
- **Q0: declined.** Their spec has no post-send background work, and their CommunicationService changes were the create-time mapping and owner steps, not the archive/thread/enrichment tail. They have asked their owner. Until then, plan on **Path A**: a recorded project exception in OUR spec, with field-mapping reviewing the CommunicationService hunks. **Needs our owner's approval.**
- **Q2: yes.** Read `sprk_bodyformat` and `sprk_bcc` for outgoing rows; both are written on every send (CS:3226/3245, :3461/:3478). Keep the HTML default when the column is null. Bcc goes only into the .eml, never into a response or log.
- **Q3: the lock is not enough.**
  - The job re-runs `FindExistingArchiveDocumentAsync` after the PUT and before the create.
  - The manual route returns AlreadyArchived when it finds a row.
  - The lock covers the normal case. A detected duplicate is logged as an Error.
  - An alternate key only if duplicates show up in telemetry: it is a schema change that needs their owner's go per environment.
- **Q4:** keep the inline fallback with the 2 s bound. One Warning log with the communication id; no response change.
- **Q6:** EnrichAsync is partly safe to re-run.
  - propose is idempotent (~847-881); the triage update is additive (~550).
  - create-task and assessment-event (~268) are NOT verified. Add an existing-row lookup by communication id if either can create a second row; don't trust the cache marker.
- **Q1, Q5:** ours and ontology's.
- **#3193 fix:** field-mapping will check that CallerMetadataReadMemo stays per caller AND per request.

## Owner and ontology decisions (2026-11-26)
- **Q0: Path A approved by the owner** ("A2- yes accept"). Recorded in spec.md ADR Tensions.
- **Q1: yes** (ontology-r1). send-email and send-reminder also take `DeferPostSend`, on three conditions:
  - (a) the send and the `sprk_communication` create with its correlation id stay inline;
  - (b) they use the same idempotent job, keyed the same way;
  - (c) the response may say "sent", never "archived", before the job runs.
- **Q5: one bell per communication** (ontology-r1, matching D-216 as amended, d7300b6e72). The bell goes to the decider or sender (Assigned To Internal, D-129, never the owning team) and links to that `sprk_communication` form ("Save to SharePoint" → `ArchiveExistingAsync`). Retries never add a second bell, because the bell is keyed on the communication.
- **Review order:** field-mapping-r1 first (the CommunicationService hunks), then ontology-r1.

## ontology-r1 on run 2 (2026-11-26, branch head b85413236f)
- **Bell recipient `sprk_sentby`: ACCEPTED** (within D-129: the individual sender, not ownership; `BuildDataverseRecordAsync` stamps it with the caller on shared-mailbox sends too). Conditions:
  - (a) When `sprk_sentby` is empty, the Error log names the communication id and the correlation id with a stable, queryable message. **Known limit:** the user is not told when the sender couldn't be resolved.
  - (b) Optional, taken: carry the caller's systemuser id (resolved at send time) in the job payload and fall back to it when `sprk_sentby` is empty.
  - Link and regarding only on Read+AppendTo (D-123), marker before create: agreed.
- **`BudgetInquiryEmailFilingTests...Task176Review2`:** changed in this branch, reviewed by ontology-r1 in the PR. Proposed (a)–(c) agreed; **(d) re-drive still one bell is required**; (e) keep a test for the queue-fails → inline-fallback path, renamed for what it shows (the response carries the core's archival warning); (f) in the queued case assert the response does not say "archived" (it may say "sent"). Run under `~Signals` plus the BudgetInquiry tests; update changed contract baselines in the same PR.
- These land in run 3 with the adversarial review's fixes.
