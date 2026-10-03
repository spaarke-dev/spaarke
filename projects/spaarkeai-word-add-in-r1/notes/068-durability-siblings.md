# Task 068: the durability siblings — the profile dispatcher and SSE sequence numbers

> Base: branch `work/spaarkeai-word-add-in-r1` at `b5d0dd114` (task 060 in PR #1085). Rigor FULL, opus @ xhigh,
> directional. Symbols are named by symbol; line numbers drift.

## 1. What the code actually does (read before designing)

### 1.1 (a) The Generate Profile button

`POST /api/office/documents/{id}/generate-profile` → `OfficeService.GenerateProfileAsync` →
`OfficeProfileDispatcher.Dispatch` → `_ = Task.Run(RunAsync)` → `IDocumentProfileAi.ProfileDocumentAsUserAsync` (OBO,
direct Action ACT-011). The 202 is returned before anything is persisted. `RunAsync` runs under
`ApplicationStopping`, so a restart cancels it mid-flight, and its `catch` logs and drops the work.

Things the review did not say, found by reading the code:

| # | Finding | Consequence |
|---|---|---|
| A1 | The dispatcher's reason to avoid the queue is the `analysis-{documentId}-documentprofile` key, which skips an already-profiled document. Task 029 solved that: the key carries **the id of the request that asked for the profile** (`UploadFinalizationWorker.QueueNextStageAsync`). `AppOnlyDocumentAnalysisJobHandler` treats the key as opaque (`job.IdempotencyKey`, fallback to the base key only when empty). | 029's discriminator generalises: a Generate Profile request gets its own id, so two clicks both run, and a Service Bus redelivery of one click repeats its key and still skips. **Escalation trigger 2 does not fire.** |
| A2 | Since `f5c7687d8` (#919 Fix 2), the queue path's `AppOnlyAnalysisService` runs the "Document Profile" consumer on the **same** ACT-011 Action and the **same** `DocumentProfileOutputMapper` as the OBO path. | Moving the button onto the queue changes the identity that downloads the file (app-only instead of the caller's OBO token), not the AI work or the fields written. The caller is authorized for `write` on the document by `DocumentAuthorizationFilter` before anything is queued; app-only background work after a request-time check is the ADR-004 pattern the save path already uses for every profile. An OBO token cannot be queued durably (it expires, and it must not be stored in a message). |
| A3 | The OBO path **never writes `sprk_filesummarystatus`**. The mapper emits content fields only. The queue path writes Pending when it starts and Completed or Failed when it ends. | Today a click on a Failed document can fill the profile while the status stays Failed. On the queue, the status the pane reads (task 021) is true afterwards. |
| A4 | The handler's processing lock is in Redis (`IdempotencyService`, `IDistributedCache`, 10-minute TTL). On a graceful stop the handler releases it with the already-cancelled token, so `RemoveAsync` throws (caught) and the lock stays. `sdap-jobs` has `lockDuration` PT5M, so the redelivery arrives while the stale lock is still held, and the handler answers "already being processed by another instance" with **Success**: the message is completed and the profile is lost. A hard crash leaves the same stale lock. | **Moving onto the queue is not enough for AC "survives a restart".** The handler must not lose a redelivery to its own stale lock. This hazard also hits every profile queued by a save today. |
| A5 | The compound AI gate (`Analysis:Enabled` && `DocumentIntelligence:Enabled`) registers `IAppOnlyAnalysisService` and `IDocumentProfileAi` together (`AnalysisServicesModule.AddAnalysisOrchestrationServices`). With the gate off, `AppOnlyDocumentAnalysisJobHandler` cannot even be constructed. | The 503 (`OFFICE_PROFILE_002`) for "profiling unavailable" must stay, and its signal stays the profile facade's registration. |
| A6 | `POST /api/documents/{id}/analyze` (`DocumentOperationsEndpoints.TriggerDocumentAnalysis`) enqueues the same job with the **bare** key, so a second request within 7 days of a processed profile is skipped. | Not this task's route. Recorded (§8). |
| A7 | `Services/Compose/ComposeProfileDispatcher` has the same `Task.Run` shape. | compose-r8's spine. Not touched. Recorded (§8). |

### 1.2 (b) SSE sequence numbers

`JobStatusService` (singleton) numbers published updates from a `Dictionary<Guid,long>` per instance, and never removes
an entry. The publishers are `ProfileSummaryWorker` and `IndexingWorkerHostedService`, on whichever instance takes the
message, so one job's events are numbered by several independent counters, each restarting at 1 after a restart.

The stream (`OfficeJobStatusService.ProduceJobStatusEventsAsync`, moved verbatim from `OfficeService` by 060) makes it
worse, on a single instance too:

| # | Finding |
|---|---|
| B1 | Its event ids mix two number lines: its **own** counter for `connected`, the snapshot and the polling fallback, and the **publisher's** for live events (`sequence = Math.Max(sequence, update.Sequence)`). The same id can name two different events. |
| B2 | Heartbeats send `getCurrentSequence() + 1` without advancing it, so every heartbeat repeats one id, an id the next real event then also takes. |
| B3 | On reconnect it drops live events with `update.Sequence <= startSequence`, comparing the publisher's number with the client's id from the mixed line. A live event, including the terminal one, can be dropped. |
| B4 | It reads the snapshot **before** subscribing. An event published between the two never reaches the stream; if that is the terminal event, the stream waits on heartbeats until the client leaves. |

The pane polls as its primary mechanism and reconnects SSE only on a 401, so none of this has hung the pane. The route's
contract is still wrong, and fixing it costs one method.

### 1.3 Latency (escalation trigger 1), measured

App Insights `spe-insights-dev-67e2xz`, 60 days, the save path's own `AppOnlyDocumentAnalysis` jobs:

| Measure | n | p50 | p90 | max |
|---|---|---|---|---|
| Service Bus submit (`JobSubmissionService`, "Queueing…" → "Queued…") | 24 | 324 ms | 379 ms | 547 ms |
| Queue pickup ("Queued…" → handler "Processing…") | 24 | 2 ms | 26 ms | 4.4 s |
| Profile run (handler "completed in") | 16 | 7.5 s | 12.3 s | — |
| The OBO click path today ("starting best-effort OBO profile") | **0** | — | — | — |

The button has never been clicked in dev, so there is no OBO baseline; the AI work is the same Action on both paths. The
change adds the submit (≈0.3 s) to the 202 and a negligible pickup to a profile that takes 8–12 s. **Judged not
material; trigger 1 does not fire.** The numbers are in the report so the owner can disagree.

## 2. Decision (written before code)

> Kept as written. Where the build differs — the queue class, the constructor count, the lock's takeover age, the 202
> body, the 503 for a refused submit — §4 and §7 record what was built and why.

### 2.1 (a) Generate Profile is a queued job

- `OfficeJobQueue.QueueProfileGenerationAsync` submits ONE `AppOnlyDocumentAnalysis` `JobContract` through
  `JobSubmissionService`, the ADR-004 producer path five producers already use. `JobId` is new per request.
- **Key:** `AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey(documentId, requestId)`, a single helper that now
  owns task 029's format. `UploadFinalizationWorker` passes the version save's job id (its keys stay byte-for-byte), and
  Generate Profile passes the request's job id. One scheme, one format: `analysis-{documentId}-documentprofile`, or
  `…-version-{requestId:N}` when a request asks for a fresh profile.
- **Availability:** the queue component takes the profile facade `IDocumentProfileAi?`, the ADR-013-sanctioned
  capability that is registered exactly when the handler can run (A5). Absent → `FacadeUnavailable` → 503, as today.
- **`OfficeProfileDispatcher` is deleted**, and with it `OfficeService`'s three optional parameters
  (`IServiceScopeFactory`, `IDocumentProfileAi`, `IHostApplicationLifetime`): **19 → 16** (as built **17**, §4).
- `GenerateProfileDispatchOutcome.NoBearer` is deleted. It existed because the OBO call needed the bearer; the route's
  filter already refuses a token-less caller, and the queued job needs none.
- The 202 now means "the request is on the queue". A queue failure is an exception (500 via the global handler), never a
  202.

### 2.2 (a) The handler must not lose a redelivery to its own lock (A4)

- `IIdempotencyService` gains an **owner-aware** `TryAcquireProcessingLockAsync(eventId, ownerId, …)` as a **default
  interface method** that falls back to the existing behaviour. `IdempotencyService` implements it: the lock's value is
  the owner, and the same owner may take it again. A default method keeps the six hand-written implementers compiling
  unchanged, including one in UAC-r2's test folder this task must not edit.
- `AppOnlyDocumentAnalysisJobHandler` acquires with the job's id as owner. A Service Bus redelivery is the same message,
  so the same `JobId`, and only arrives after the previous delivery lost its lock: it reclaims the lock and runs. A
  different job holding the same key is still answered as before.
- It releases the lock with `CancellationToken.None`, so a graceful stop releases it.
- Other handlers keep the ownerless lock. The same stale-lock hazard applies to them (§8).

### 2.3 (b) One number line, from Redis

- `JobStatusService` takes the sequence from Redis `INCR sdap:job:{jobId}:seq` (24 h sliding TTL), shared by every
  instance and surviving restarts. The in-memory dictionary and its lock are deleted. Publishing already needs Redis, so
  this adds no new dependency or failure mode.
- The stream numbers **only** publisher events: a live event's id is its `Sequence`. `connected`, the snapshot,
  heartbeats and the polling fallback carry **no id**, so they never move the client's `Last-Event-ID` (the pane's
  `SseClient` keeps the previous id when an event has none, as the SSE spec does).
- On reconnect it forwards only live events with `Sequence > Last-Event-ID`.
- It **subscribes before** reading the snapshot (B4). `SubscribeToJobAsync` reports when the subscription is live.
- **"Neither skips nor replays"** means, precisely: every numbered event after the client's id is delivered; no numbered
  event at or before it is sent again; and the state between them, which Redis pub/sub does not keep, arrives as the
  unnumbered snapshot.

## 3. Reproduce first (unchanged code, base `b5d0dd114`)

`tests/integration/regression/Issue1086_OfficeBackgroundWorkRestartTests.cs`: **7 red, 1 green (the guard)**. Verbatim:

| Test | Failure on the unchanged code |
|---|---|
| (a) `GenerateProfile_ARestartAfterThe202_LeavesTheRequestOnTheJobQueue` | *"Expected var job = host.SubmittedJobs to contain a single item because the 202 promised a profile, and only a request on the job queue outlives the process **(the in-process profile was cancelled by the stop)**, but the collection is empty."* |
| (a) `GenerateProfile_TwiceForADocumentASaveAlreadyProfiled_BothRequestsRun` | *"Expected jobs to contain 2 item(s) because each click is its own request on the queue, but found 0: {empty}."* |
| (a) `ProfileJob_RedeliveredAfterItsProcessCrashed_RunsInsteadOfBeingCompletedAsInProgress` | *"Expected invocation on the mock once, but was 0 times: a => a.AnalyzeDocumentAsync(…)"*, the redelivery completed as "already being processed" |
| (a) `ProfileJob_StoppedGracefully_ReleasesItsLock_SoTheNextJobOnThatKeyRuns` | *"Expected invocation on the mock once, but was 0 times"*, the lock outlived the graceful stop |
| (b) `SseSequence_TwoInstancesAndARestart_NumberOneJobsEventsWithoutReuse` | *"Expected redis.PublishedSequences(jobId) to be equal to {1L, 2L, 3L, 4L, 5L} … but **{1L, 1L, 2L, 2L, 1L}** differs at index 1."* |
| (b) `SseStream_ReconnectWithLastEventId_SendsEveryLaterEventOnce_AndNoEarlierOne` | *"… Frames: **connected#6, progress#7, progress#7, job-complete#7**, but {6L, 7L, 7L, 7L} contains 2 item(s) too many."* |
| (b) `SseStream_AnEventPublishedWhileTheSnapshotIsRead_IsStillDelivered` | The stream never ended; no `job-complete` frame after 3 s |
| Guard: `ProfileJob_WhileADifferentJobOnTheSameKeyIsRunning_DoesNotRunAlongsideIt` | Passed (current behaviour, which the fix must keep) |

(b) was demonstrated with **two `JobStatusService` instances over one shared Redis double**, plus a third standing in for
a restarted instance; a true two-process test is not available. The (a) restart is `IHostApplicationLifetime.StopApplication()`
on the test host, which cancels `ApplicationStopping`, the token the dispatcher ran under.

## 4. As built, and where it differs from §2

| Area | Change |
|---|---|
| `Services/Office/OfficeProfileQueue.cs` (new, concrete, unconditional in `AddOfficeModule`) | `QueueAsync` submits the job through `JobSubmissionService` and returns `Dispatched` only after the submit; `FacadeUnavailable` when `IDocumentProfileAi` is not registered. Holds `GenerateProfileDispatchOutcome` (moved from the deleted dispatcher file; `NoBearer` removed) |
| **Deviation from §2: not `OfficeJobQueue`** | `OfficeJobQueue` is constructed directly by three test fixtures (two in `OfficeEndpointsContractTests.cs`, which UAC-r2 also edits), and it writes the finalization queue with a raw sender. The profile request uses a different path (`JobSubmissionService` → `sdap-jobs`) and needs the AI gate. A separate class keeps both cohesive and touches no fixture |
| `OfficeService` | `OfficeProfileDispatcher` and its three optional parameters removed; `OfficeProfileQueue` added (required). Constructor **19 → 17**. `GenerateProfileAsync` delegates |
| `OfficeEndpoints` | `NoBearer` → 401 `OFFICE_PROFILE_003` branch deleted; docs and log text say "queued". 202 / 503 / 400 / 401 / 403 / 404 / 429 otherwise unchanged |
| `AppOnlyDocumentAnalysisJobHandler` | `ProfileIdempotencyKey(documentId, requestId)` owns task 029's format; the lock is taken under `job.JobId` and released with `CancellationToken.None` |
| `IIdempotencyService` / `IdempotencyService` | Owner-aware `TryAcquireProcessingLockAsync(eventId, ownerId, …)` as a default interface method. The real service stores `owner|acquiredAt` and lets the same owner take the lock back only once it is **at least one minute old** (`OwnerTakeoverAge`; review W1). **`ReleaseProcessingLockAsync` now always releases**, whatever token it is given (review W5), which fixes the graceful-stop half for every handler. Optional `TimeProvider` (UAC-r2's test constructs the service directly) |
| `UploadFinalizationWorker`, `DocumentOperationsEndpoints` | Every producer of the profile key builds it through the helper; strings byte-for-byte unchanged (029's seam test pins them) |
| `OfficeProfileQueue` (review round) | The submit is not cancelled with the request; a `ServiceBusException` is `QueueUnavailable` → **503 `OFFICE_PROFILE_004`, retryable**; the result carries the job id; the requester's `oid` goes in the payload and the log |
| `OfficeEndpoints` (review round) | The 202 carries `jobId`, and its `Location` is `/api/v1/documents/{id}`, where this job type records its status (ADR-017) |
| `OfficeJobStatusService` (review round) | A subscription that ends without an outcome falls back to polling the row (W3); a terminal event at or before `Last-Event-ID` ends the stream (S5) |
| `SystemCacheKeys` | `JobStatusSequence` added (15 of the 20 the NFR-08 cap allows), with the three-question justification; the key carries the `SYSTEM-LEVEL EXCEPTION (NFR-08)` annotation (W2). **For the owner's architecture review** |
| `JobStatusService` | Redis `INCR` per job (24 h sliding TTL); the dictionary, its lock and `IDisposable` removed; `SubscribeToJobAsync` calls `onSubscribed` once Redis acknowledges |
| `OfficeJobStatusService` stream | Subscribe first, then snapshot; only publisher events numbered; `> Last-Event-ID` filter; heartbeats and fallback unnumbered |
| `SseHelper` | `eventId` optional on the frame formatters |
| Docs | `office-outlook-teams-integration-architecture.md` (the route table now lists Generate Profile; the stream row states the numbering rule), `DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md` (the Office button now takes the app-only queue) |

**Tests changed on purpose** (each asserted the mechanism 068 replaces, or needed the new fixture shape):
- `OfficeGenerateProfileContractTests`: the happy path asserted the in-process OBO facade call; it now asserts the queued
  job (type, subject, correlation id, key) and that the facade is NOT called. One method renamed
  (`…_AndQueuesOneProfileJobForTheDocument`). The five negative cases assert "nothing queued" instead of "facade not
  called". The 503 test is unchanged.
- `AppOnlyDocumentAnalysisJobHandlerTests`: three lock setups/verifies move to the owner overload (owner = `job.JobId`).
- `JobStatusServiceTests`, `JobStatusSseIntegrationTests`: the Redis double answers `StringIncrementAsync`; the removed
  `Dispose()` calls go.

## 5. Seeded controls (each restored afterwards)

| Seed | Caught by (only) |
|---|---|
| Handler takes the ownerless lock again | `ProfileJob_RedeliveredAfterItsProcessCrashed_…` |
| Handler releases the lock with the job's token again | `ProfileJob_StoppedGracefully_ReleasesItsLock_…` |
| `connected` carries the next number | `SseStream_ReconnectWithLastEventId_…` |
| The snapshot is read before subscribing | `SseStream_AnEventPublishedWhileTheSnapshotIsRead_…` |

Each run: **1 failed, 7 passed** of the 8 regression tests.

**After the review round** (21 tests: the regression class plus the Generate Profile contract), each seed again fails
exactly one test, 20 pass:

| Seed | Caught by (only) |
|---|---|
| The same owner takes its lock back however recent it is | `ProfileJob_DeliveredTwiceWhileTheFirstCopyRuns_DoesNotRunTwice` |
| Both releases honour the cancelled token again | `ProfileJob_StoppedGracefully_ReleasesItsLock_…` |
| A subscription that ends early just closes the stream | `SseStream_SubscriptionEndsWithoutAnOutcome_FallsBackToPollingAndDeliversIt` |
| A terminal event the client already has does not end the stream | `SseStream_ReconnectAfterTheOutcomeWasSent_EndsInsteadOfWaiting` |
| A Service Bus failure escapes to the global handler | `Post_GenerateProfile_WhenTheJobQueueRefuses_Returns503Retryable_NeverA202` |

## 6. Gates (final, after the review round)

| Gate | Result |
|---|---|
| **Full BFF suite** | **13,071 passed / 0 failed / 54 skipped.** **Reconciled exactly:** 060's 13,058 + 8 regression tests (§3) + 4 more from the review round (duplicate delivery, outcome already sent, subscription ends, no Redis) + 1 contract test (queue refuses) = 13,071 |
| The first full run (before the review round) | 13,065 / **1 failed** / 54. The failure was `EmailRegardingIntentSeamTests…StoresGatedProposalAndNotesSummary`, Communication code this task does not touch; it passes alone 3 of 3 and passed in the second run. Likely a 1-second regex timeout under load → **ISS-019 / #1088** |
| ArchTests | **337 / 337.** The review round first tripped `CallerIdentityGuardTests` (a direct `oid` claim read); fixed by using `CallerResolution.ResolveObjectId` |
| Affected classes incl. every other `IIdempotencyService` user (UAC-r2's ExternalAccess reconciliation, grant-expiry reminder, DocumentOperations) | **209 passed / 0 failed / 8 pre-existing skips** |
| Publish size | fresh master `402afb657` **47,671,577 B** → master + this diff **47,673,783 B** = **+2,206 B**; **212 = 212 files**; `Compress-Archive` Optimal, PDBs included, fresh short-path worktrees (`C:\tmp\w068m`, `C:\tmp\w068b`, removed). (The first measurement, before the review round, was +616 B) |
| CVE | No `.csproj` / `Directory.Packages.props` change → no new package |
| `dotnet format --verify-no-changes` | Clean on every changed `.cs` (source and tests) |
| office-addins | One comment changed (`useDocumentProfile.ts` header); no code |

## 7. Quality gates: the code review and the ADR check

### 7.1 Code review

An independent reviewer (a separate agent, coverage-first) read the diff and the design: **no Critical**, 5 Warnings,
12 Suggestions. Decisions:

| Finding | Decision |
|---|---|
| **W1** Taking the lock back by owner removed mutual exclusion for a request's own key: a resent message (duplicate detection is off) or a lapsed lock renewal gives two live copies one `JobId` | **Fixed**: one-minute takeover age (§4); regression test `…DeliveredTwiceWhileTheFirstCopyRuns_DoesNotRunTwice` |
| **W2** The counter's Redis key was not in the NFR-08 allow-list | **Fixed**: `SystemCacheKeys.JobStatusSequence` + annotation. Kept the `sdap:job:` prefix: the job's own channel uses it, as do three allow-listed keys. **The allow-list says additions need architecture review: owner to confirm** |
| **W3** A live stream ended silently when its subscription did | **Fixed**: falls back to polling the row (pre-existing behaviour, improved) |
| **W4** No test for a refused submit; the 500 came from the global handler | **Fixed**: 503 `OFFICE_PROFILE_004`, retryable, with a contract test |
| **W5** Six sibling handlers share the stale-lock hazard | **Graceful half fixed centrally** (release always releases); **crash half filed: ISS-020 / #1089** |
| S1 ADR-013 | Not a violation (the reviewer's verdict and mine, §7.2). The two remaining inline key literals now use the helper |
| S2 Default-interface-method traps | Accepted and recorded: a Moq setup on the old overload stops matching; ISS-020 says so for the next handler |
| S3 The pane reads once, too early; ADR-017 202 body | 202 body **fixed** (`jobId` + `Location`); the pane's single read **filed: ISS-021 / #1090** (predates 068) |
| S4 Stale text | Fixed: `useDocumentProfile.ts` header, `SseHelper` param doc, this note's §2 |
| S5 A skipped terminal event left the stream open | **Fixed** with a test |
| S6 Progress can step back between the snapshot and a live event | Accepted: cosmetic, and the snapshot is unnumbered by design |
| S7 Old mixed ids during a rolling deploy | Accepted: the pane never sends `Last-Event-ID` |
| S8 `INCR` then fire-and-forget `EXPIRE` is not atomic | Accepted: a dropped expire leaves one small key; recorded |
| S9 Test hygiene | Fixed: releases in `finally`, the CTS disposed, three new stream tests (fallback unnumbered, subscription ends, outcome already sent) |
| S10 The app-only job carries no requester | **Fixed**: `RequestedBy` (the caller's `oid`) in the payload and the log. Not an authorization weakening: `write` is checked before anything is queued, the same pattern as `POST /api/documents/{id}/analyze` |
| S11 The submit was cancellable by the client | **Fixed**: `CancellationToken.None` |
| S12 Nits | Accepted |

### 7.2 ADR check

| ADR | Result |
|---|---|
| ADR-001 / ADR-052 | ✅ The `Task.Run` behind the 202 is gone; the work is an ADR-004 `IJobHandler` job on `sdap-jobs` |
| ADR-004 | ✅ Job Contract; deterministic per-request key; correlation id propagated; no bytes in the payload; no reliance on `MessageId` dedupe. ⚠️ **Inherited, not introduced:** the lock is check-then-set, not atomic (#984); the owner-aware acquire has the same window |
| ADR-008 | ✅ Filters unchanged; the route census unchanged |
| ADR-009 | ✅ Cross-instance state moved from an in-memory dictionary to Redis |
| ADR-010 | ✅ `OfficeProfileQueue` concrete, registered in `AddOfficeModule`; no new interface (a default method on an existing one) |
| ADR-013 | ✅ **Not a violation.** The only injected AI type is the `IDocumentProfileAi` PublicContracts facade (the availability signal). `AppOnlyDocumentAnalysisJobHandler.JobTypeName`/`ProfileIdempotencyKey` are the job contract's type name and key, referenced the same way by four existing producers; ArchTests (which encode the forbidden types) pass |
| ADR-017 | ✅ After the review round: the 202 carries `jobId` and a `Location`; this job type's status is persisted on the document (`sprk_filesummarystatus`), as for every `AppOnlyDocumentAnalysis` job |
| ADR-019 | ✅ 503s are ProblemDetails with `errorCode`; anything else reaches the global ProblemDetails handler |
| ADR-032 | ✅ Every new dependency registered unconditionally; the gated facade is optional and turns into an explicit 503. ⚠️ **Inherited:** with the compound gate off, the unconditionally registered `AppOnlyDocumentAnalysisJobHandler` cannot be constructed; 068 never queues in that state |

## 8. Observed, not fixed (each filed)

| Item | Where it went |
|---|---|
| A6: `POST /api/documents/{id}/analyze` used the bare key, so a re-request within 7 days was skipped | Its key now comes from the helper (same string). The skip itself is that route's behaviour (MatterCreationWizard); recorded in ISS-018 |
| A7: `Services/Compose/ComposeProfileDispatcher` has the same `Task.Run` shape | compose-r8's spine; recorded in ISS-018 (#1086) |
| Six sibling handlers: the crash half of the stale lock | **ISS-020 / #1089** |
| The pane reads once after Generate Profile | **ISS-021 / #1090** |
| A Communication seam test flakes under full-suite load | **ISS-019 / #1088** |

## 9. Live check on `spaarke-bff-dev`, 2026-10-02 (after the deploy from master `5e39f2bea`)

Owner-authorized deploy and restarts; user token `ralph.schroeder@spaarke.com`; document `fb79f621-43c5-4083-a721-d0d482074731`
(saved for 060's check, §11 there).

| Check (POML ui-test) | Result |
|---|---|
| Generate Profile answers at once | **202** at 12:23:03 UTC, `Location: /api/v1/documents/fb79f621-…`, job `3b90642b-a4ee-4caf-bf9c-d4b3ef47c3d8` |
| Restart within ten seconds | `az webapp restart` issued at once (12:23:03). The job had been picked up at 12:23:02.27 (attempt 1/3) and **completed on the old process at 12:23:11.9 (9,661 ms)**; the new process started 12:23:29. So the restart did **not** interrupt the run: the old process was still running when the job ended, 9 s after the restart command (when it received the stop signal is not in the logs) |
| Status Completed afterwards | From the new process, `GET /api/v1/documents/{id}` → `summaryStatus` 100000002 with summary, TL;DR, keywords and type written. App Insights: one "queued as job" line, one AppOnlyDocumentAnalysis "completed in" line for that job id. **PASS as written** |
| Click twice | Jobs `862252c2-…` and `5217e424-…`: both queued, both **completed** (11,086 ms and 9,185 ms). **PASS** |
| **Crash mid-run (hard kill)**, owner: "if we need to produce it live then we should do it" | Job `f2733e20-77a0-4ae4-b7fe-8cabe516440a` queued 13:18:45, attempt 1 started 13:18:45.2; `kill -9` on the `dotnet` process at 13:18:49 (over SSH), mid-run. No completion from attempt 1. The message came back as **delivery count 2** at 13:20:12, when the new process started. Its idempotency lock (taken by the same job 87 s earlier) was **taken back by its owner** (older than the 1-minute takeover age; logged at 13:20:12.31, Warning: *"taking over a processing lock left by an earlier attempt of the same job f2733e2077a04ae4b7fe8cabe516440a"*), and the run **completed in 10,764 ms**. Before 068, the 10-minute lock would have made this redelivery complete as "already being processed" and drop it. **PASS** |

**What this shows.** Live, a profile request survives both a graceful restart (the work finished first) and a hard crash
mid-run (redelivered, lock reclaimed, completed). A graceful restart could not cut a run: App Service left the old process
running longer than the run took (twice: 9 s and 8 s runs, new processes 26 s and 23 s after the command). That path (stale lock released on a stop; owner takeover after a crash) stays proven by
`Issue1086_OfficeBackgroundWorkRestartTests` only. Before 068 the same restart would have cancelled the in-process
`Task.Run`, which was bound to `ApplicationStopping`.
