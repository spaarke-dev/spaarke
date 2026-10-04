# Task 060: the Office job-status store, and the job-status/SSE extraction

> **Date**: 2026-10-01 · **Rigor**: FULL (opus / xhigh) · **Mode**: directional
> **Status of this note**: §1–§4 were written **before any code changed**, as the POML requires. Later sections
> were added as the work proceeded.

## 1. What is actually broken (verified, not assumed)

The POML described one defect: the job status lives in a `private static ConcurrentDictionary`. Reading the code
and **production telemetry** (`spe-insights-dev-67e2xz`, the App Insights resource of `spaarke-bff-dev`, 60 days)
found three, and the second and third are bigger than the first.

### 1.1 The in-memory store (the POML's defect)

`OfficeService._jobStore` is `private static`. It holds the only copy of the job's **outcome**: which document the
save produced (`Result.Artifact`). The pane completes on that document id.
- A restart, or a poll that lands on a second instance, finds nothing in memory.
- The code then falls back to the Dataverse row, which cannot answer the question (§1.2, §1.3).

### 1.2 The Dataverse reads never worked in production: `dynamic` over an anonymous type

`IProcessingJobService.GetProcessingJobAsync` and `GetProcessingJobByIdempotencyKeyAsync` return `Task<object?>`,
built in `DataverseServiceClientImpl` as **anonymous types**. Anonymous types are `internal` to the assembly that
compiles them (`Spaarke.Dataverse`). The BFF reads them through `dynamic`. The runtime binder resolves members
against the **call site's** accessibility. `Sprk.Bff.Api` has no `InternalsVisibleTo` from `Spaarke.Dataverse` (only
`Sprk.Bff.Api.Tests` does), so every property read throws `RuntimeBinderException`.

**Production evidence**: `Microsoft.CSharp.RuntimeBinder.RuntimeBinderException`, category
`Sprk.Bff.Api.Services.Office.OfficeDocumentPersistence`, 2026-08-25 01:24:55, *"'object' does not contain a
definition for 'Status'"*.

Two consumers, both swallowing the exception:

| Consumer | Effect in production |
|---|---|
| `OfficeService.GetJobStatusAsync` fallback | Every poll that misses memory returns **404** |
| `OfficeDocumentPersistence.CheckForExistingJobAsync` (the 039/047 idempotency check) | A real duplicate is answered **"no duplicate"**, so the save runs again. **Duplicate detection has never worked in production.** |

**Why no test caught it**: the contract-test world (`OfficeVersionSaveWorld.FindJobByIdempotencyKey`) returns an
`ExpandoObject`. That type is public and fully dynamic, so binding succeeds in tests. The tests exercised a shape
production never produces.

The idempotency check does not log on every save. It reaches `dynamic` only when a row with the same key exists,
which is why the exception appears once and not on every save.

### 1.3 Most saves never got a job row at all: the payload carried the document bytes

The save serialized the whole request, **including `Document.ContentBase64`, `Attachment.ContentBase64` and the
email body**, into `sprk_payload`, whose maximum length is 50,000 characters. Dataverse refused the create. The
save caught that, logged "falling back to in-memory storage", and continued with a made-up `Guid` that has **no
row**. Every later status write then failed with *"sprk_ProcessingJob … Does Not Exist"*.

Production, 60 days:

| Signal | Count |
|---|---|
| Saves requested | **40** |
| Job rows created | **13** |
| Create refused: *"The length of the 'sprk_payload' attribute … exceeded the maximum allowed length of '50000'"* | **27** |
| Later status writes refused: *"Entity 'sprk_ProcessingJob' … Does Not Exist"* | 75 (save path) + 104 (finalization worker) |

So **68% of real saves had no durable job**: status existed only in one process's memory, and idempotency had no row
to find. Nothing reads `sprk_payload` (grep across `src/`); it is write-only diagnostics. ADR-004 and ADR-017 (via
ADR-015) both forbid content bytes in it.

### 1.4 The row has two writers with different meanings

The save is **synchronous**: when `POST /api/office/save` returns, the job is already terminal (Completed with its
document, or Failed). Finalization then runs on the queue, and the workers write **the same row**:

| Writer | `sprk_status` / `sprk_currentstage` / `sprk_progress` |
|---|---|
| The save (`OfficeService`) | Queued → Running (FileUploaded 30, RecordsCreated 50) → **Completed** (100) |
| `UploadFinalizationWorker` | **Running** (50, 60, 65, 70) → Completed, or **Failed** on its last attempt |
| `ProfileSummaryWorker`, `IndexingWorkerHostedService` | Completed at their end |

The in-memory record held the **save's** view only. The pane treats it as the save's outcome: the comment at
`OfficeService.cs:1037` says *"User sees immediate success while AI processing continues in background"*. A store
that reads `sprk_status` naively would turn that immediate success into a wait on AI, and would show a finalization
failure as a failed save. That is a client-contract change, so it is not acceptable silently.

### 1.5 Smaller findings on the same row (recorded; see §5)

- **`sprk_jobtype` is written with the wrong numbers.** The save writes `(int)JobType` (EmailSave 0, AttachmentSave 1,
  DocumentSave 2). Dataverse's options are Document Save 0, Email Save 1, Share Links 2. The reader maps back with a
  third table. Nothing that matters reads it.
- **The column limits fit.** `sprk_idempotencykey` is 64 characters; the client key is 64-character SHA-256 hex and
  the server key 44 characters. No production failure came from it.

## 2. Decision: the store is the Dataverse `sprk_processingjob` row (option a)

**The row the save already creates is the job's durable record. The pane reads it, typed. Nothing is held in
process memory.**

| Part | What |
|---|---|
| **The save's own view** | Persisted as JSON in **`sprk_result`**, at create and at every transition the save makes, in the **same** update as the pipeline fields. It holds exactly what the in-memory record held: status, phase, progress, job type, timestamps and the result artifact. `sprk_result` exists, is documented as *"JSON output data from the job"*, and nothing writes it today. **The workers never write it**, so finalization can no longer change what the pane reads as the save's outcome (§1.4). |
| **The row always exists** | `sprk_payload` keeps the request's metadata but **drops the content**: document and attachment bytes, the email body, and email attachments' bytes. A defensive cap falls back to a minimal payload if it would still exceed 50,000. |
| **Typed reads** | `IProcessingJobService` returns a **public** `ProcessingJobRecord` from `Spaarke.Dataverse`. No `dynamic` anywhere on the job path. |
| **One rule, two readers** | One mapper turns a row into the job's effective state. The status read and the idempotency check both use it (§3). |
| **No orphan jobs** | If the row cannot be created, the save is refused **before** any SPE write, as `OFFICE_014` (Dataverse error, 502, retryable; an existing code). The only production cause, the payload overflow, is removed. ADR-017: *"MUST NOT leave jobs without status records"*. |
| **Owner** | *(Refined in §5.1.)* The creator is the OID the save authenticated, recorded in its view. For rows written before this task, it is `sprk_initiatedby` joined to `azureactivedirectoryobjectid` (task 067). `JobOwnershipFilter` therefore enforces ownership on every instance and after a restart. |

### Why (a): reasons

1. **It is the durable record ADR-017 requires**, and it already exists, written on every save, with the creator
   (067) and the idempotency key (039) on it. Making the pane read it gives one source of truth for "what happened to
   this save".
2. **The read cost is small because the save is synchronous.** The job is terminal before the pane's first poll, so
   a save costs one or two Dataverse reads: `JobOwnershipFilter`, then the handler. The SSE stream reads once and ends
   on a terminal state. Its 500 ms polling fallback runs only for a job still in flight on another request, which is
   bounded by that request's lifetime.
3. **It fixes the idempotency spine as a by-product, which no other option does.** The 039 check reads this row. Once
   the row exists for every save (§1.3) and is read typed (§1.2), duplicate detection works in production for the
   first time.

### Rejected: (b) Redis via `ITenantCache` (ADR-009)

- **A second store for the same fact.** The row still has to be fixed and read for idempotency (§1.2, §1.3), so Redis
  would duplicate the save's outcome in a second place. They would need keeping in step, and a Redis miss would still
  fall back to the row.
- **No performance need.** Reads are about one per save (reason 2 above). ADR-009 is about cross-request *caching*,
  and there is no hot read to cache.
- **Weaker durability.** Dev Redis is Basic C0, with no persistence, and in Development it can fall back to memory.
  ADR-017 asks for persistence, not a cache.

### Rejected: (c) keep in-memory and document it

- **ADR-017**: *"MUST persist status transitions and final outcome"*.
- **The loss is observed, not theoretical**: 2026-09-19 restarts, and 68% of saves with no durable row.
- **The job is not ephemeral.** The pane's completion depends on reading "which document" after the save returns,
  across reconnects.

The POML's second escalation trigger ("evidence shows job status is genuinely ephemeral") therefore does **not**
fire.

## 3. The effective-state rule (one mapper, used by both readers)

| Row has | Status read (`GET /jobs/{id}`, SSE) | Idempotency check (039) |
|---|---|---|
| The save's view in `sprk_result` | **Exactly that view**: Completed + artifact, or Failed | Completed / in flight → **duplicate**; Failed → **new attempt** (039) |
| No view (a row written before this task) and `sprk_status` Completed | Completed, **no artifact** (it cannot be recovered). The pane shows a definite error | duplicate |
| No view and Failed / Cancelled | Failed / Cancelled | new attempt (039, unchanged) |
| No view, or a non-terminal view, **older than the save's maximum lifetime** | **Failed**: "the save did not finish" | **new attempt** |
| A non-terminal view within that lifetime | Queued / Running (another request is mid-save) | duplicate (039: an in-flight save is not repeated) |

**Why the last two rows exist.** A save killed mid-flight (a restart) leaves its row non-terminal forever. Today
that is invisible, because the idempotency check never sees a row (§1.2). Once the check works, a retry would be
answered "duplicate" with a job that never finishes, and the pane would spin forever. That is the failure class 039
fixed for exceptions (finding 2), now arriving through process death.

The maximum lifetime is **5 minutes**: the App Service front end ends a request at 230 seconds, plus margin.

**This is not a key-derivation change** (the 039/047 constraint). Keys are untouched. What changes is that the
check can read the row at all, and reads it through the same rule as the status endpoint.

## 4. Scope

**In:**
- The store (above).
- The payload content drop.
- Typed reads.
- The pane treating Completed-without-a-document as a visible terminal error, on both the poll and the SSE path.
- The extraction of the job-status and SSE members into `Services/Office/OfficeJobStatusService.cs` (concrete,
  registered unconditionally). `IOfficeService` is unchanged; `OfficeService` delegates to the new service, as 059
  did.
- `JobOwnershipFilter`'s incorrect comments.
- The stale `TRACKED: GitHub #229` comments.

**Name clash, resolved by deleting dead code.** `Workers/Office/OfficeJobStatusService.cs` and
`IOfficeJobStatusService.cs` already exist:
- a logging-only stub ("Will be replaced … in task 064");
- **never registered in DI** and with **no production consumer**, only its own unit test.

A second class with the same name in another namespace would break any file that imports both namespaces. The stub,
its interface and its unit test are deleted. The test is not under a KEEP path, and it tests a stub that logs.

**Out (named so 060 is never cited as having fixed them):**
- **`OfficeProfileDispatcher`'s fire-and-forget `Task.Run`**: a restart loses the work. **Task 068.**
- **`JobStatusService._jobSequences`**: an in-memory `Dictionary` that numbers the Redis pub/sub updates per job, so
  sequence numbers restart after a process restart. **Task 068.**
- **The `sprk_jobtype` number mismatch (§1.5).** The read no longer depends on it, because the job type comes from
  the save's view. Fixing the write would relabel every new row in Dataverse; filed for the owner, not done
  silently.

## 5. Test plan, and the one fixture change

- **Reproduce first, red before any production change:**
  - **(R1)** A completed save's row, polled by a process that never held it in memory, returns 404. This uses the
    production-shaped (anonymous, other-assembly) read.
  - **(R2)** A duplicate save whose first attempt was written by another process is not detected.
  - **(R3)** A document over about 37 KB gets no job row.
- **Unmodified test methods:** the existing job-status, idempotency (039) and AI-refresh (029) tests keep their test
  methods unchanged (named in §8).
- **The one fixture change:** their shared Dataverse double (`OfficeVersionSaveWorld`) is extended to **answer the
  job read** and to store the fields the save writes. It also returns the typed record instead of an
  `ExpandoObject`, which the interface change requires anyway. Production never read the row back successfully
  before, so the double was never asked to. This is a fixture learning the store, not a changed assertion.

### 5.1 The ownership source (decided while writing the tests)

The test caller's OID is `"test-user-oid"`, not a GUID, so the caller resolver finds no `systemuser` and the row
records no `sprk_initiatedby`. Any real caller without a resolvable `systemuser` is in the same position, and today
they can poll their own job from memory.

So the save's view in `sprk_result` records the **creator OID the save authenticated**, which is what the in-memory
record held. Ownership uses that first, and **falls back to 067's `sprk_initiatedby` join** for rows written before
this task. A row with neither is refused, as 067 decided.

## 6. Reproduce first: the reds, before any production change

The world was first made **faithful to production**:
- it stores what is written and refuses an over-length `sprk_payload`, as Dataverse does;
- it returns the **production shape** for both job reads: an anonymous type from another assembly than the
  `dynamic` call site, which is the property that breaks the binder.

Test run on the **unchanged** production code, `Sprk.Bff.Api.Tests` filtered: **11 failed / 18 passed / 29**.

The new regression tests (`tests/integration/regression/Issue1084_OfficeJobRecordDurabilityTests.cs`), all red:

| Test | Red, verbatim |
|---|---|
| `JobStatus_ReadByAProcessThatNeverHeldIt_…` | *"Expected poll.StatusCode to be HttpStatusCode.OK {value: 200} because the job row is the durable record; … but found HttpStatusCode.NotFound {value: 404}."* |
| `JobStream_ReadByAProcessThatNeverHeldIt_…` | *"Response status code does not indicate success: 404 (Not Found)."* |
| `RepeatedSave_WhoseFirstAttemptWasWrittenByAnotherProcess_…` | *"Expected response.StatusCode to be HttpStatusCode.OK … {"title":"File Already Exists","status":409, …"errorCode":"OFFICE_020"…"* The repeat was not recognised, ran again, and hit the name collision |
| `SaveOfLargeContent_GetsADurableJobRow_…` (Document, Attachment, Email) | *"Expected world.JobRows {empty} to contain key {…} because every save needs its job row; …"* (all three) |

**Five EXISTING tests also go red.** They were false greens: they passed only because the double returned an
`ExpandoObject`. Their test methods are unchanged; only the double became faithful.

| Existing test (task) | Red, verbatim |
|---|---|
| `OfficeSaveSpineIdempotencyTests.ByteIdenticalCreateRetry_UnderADifferentHeaderKey_IsDeduplicatedByTheServerKey` (039) | *"Expected retry.StatusCode to be HttpStatusCode.OK {value: 200}, but found HttpStatusCode.Conflict {value: 409}."* |
| `OfficeVersionSaveRevertTests.TheSameVersionSave_SentTwice_WithNoClientKey_WritesOnce_AndTheSecondIsTheFirstJobsDuplicate` (047) | *"… to be HttpStatusCode.OK {value: 200}, but found HttpStatusCode.Accepted {value: 202}."* The repeat wrote a second version |
| `OfficeVersionSaveRevertTests.TheSameVersionSave_SentTwice_UnderThePanesKeys_WritesOnce_AndTheSecondReturnsTheFirstJob` (047) | *"Expected (BodyOf(retry)).JobId to be {3771afb3-…} because a retry is answered with the save it repeats, but found {5d5ea892-…}."* |
| `OfficeVersionSaveRevertTests.EmailSave_ResentWithNoClientKey_IsStillAnsweredDuplicate_AndReadsNoFile` (047) | *"Expected resent.StatusCode to be HttpStatusCode.OK {value: 200}, but found HttpStatusCode.Conflict {value: 409}."* |
| `OfficeCreateCollisionTests.ThreeCreatesOfBThenAThenB_UnderTheSameNameAndTarget_NeverCorruptsOrLosesB` (025) | *"Expected createBAgain.StatusCode to be HttpStatusCode.OK {value: 200} because the document already truthfully holds these exact bytes, but found HttpStatusCode.Conflict {value: 409}."* |

This is the production symptom reproduced in tests: a repeated save is not recognised, so it either writes again or
collides.

**Client** (`useSaveFlow.terminalOutcome.test.ts`, on the unchanged hook): **5 failed / 1 passed**. Every failure was
*"Expected: "error" / "complete", Received: "processing""*, the pane stuck on the job card. The one pass is the
abandoned-job poll: the existing Failed branch already shows the server's message, and the test pins that the new
answer reaches the pane.

## 7. What was built

Commits `3511668f4` (the change) and `3fb75a1a4` (a review fix).

| Area | Change |
|---|---|
| `Spaarke.Dataverse` | New public `ProcessingJobRecord`. `GetProcessingJobAsync` / `GetProcessingJobByIdempotencyKeyAsync` return it, built by one mapper, with `sprk_currentstage`, `sprk_result`, the error columns, `createdon` and `sprk_completeddate` added. **No `dynamic` anywhere on the job path** |
| `Services/Office/OfficeJobStatusService.cs` (new, concrete, scoped, unconditional) | `CreateAsync`, `RecordAsync` (pipeline columns + the save's view, in ONE update), `FindExistingAsync` (039), `GetAsync` (ownership when a caller is given), `StreamAsync` (moved **verbatim** by script; only its two status reads changed), `ToEffectiveView` (the rule, §3), `BuildPayload` (metadata, never content) |
| `OfficeService` | `_jobStore` deleted. Each transition: `jobRecord = jobRecord with {…}; await _jobs.RecordAsync(…)`, with the same status, phase, progress and error text as before. A job row that cannot be created **refuses the save before any write** (`OFFICE_014`). Constructor **20 → 19** (`IJobStatusService`, `IProcessingJobService` out; `OfficeJobStatusService` in). 2,656 → 2,109 lines |
| `OfficeDocumentPersistence` | `UpdateJobStatusInDataverseAsync`, `CheckForExistingJobAsync`, the two map helpers, and the `IProcessingJobService` dependency removed. The six direct test constructions drop that one argument; nothing else in those two dedup test files changed |
| `OfficeEndpoints.MapSaveErrorToProblem` | `OFFICE_014` renders as a **retryable 502** (found in review: it fell to the default 400, blaming the request) |
| `JobOwnershipFilter` | Comments corrected: there is no `sprk_createdby` and no same-tenant rule; the `reasonCode` IS caller-visible; creator-less rows do not "drain". Code unchanged |
| `OfficeModule` | `TryAddSingleton(TimeProvider.System)` + `AddScoped<OfficeJobStatusService>()` |
| Deleted | `Workers/Office/OfficeJobStatusService.cs` + `IOfficeJobStatusService.cs` (a logging stub, never registered) and their unit test (27 tests of the stub) |
| Pane (`useSaveFlow.ts`) | Completed with no document (no version fallback) is a **visible terminal error** on poll and SSE. The SSE handler accepts the server's names (`job-complete`, `job-failed`, `stage-update`) and reads `errorMessage`. The terminal outcome is applied **once** (review fix, `3fb75a1a4`) |
| Docs | Architecture doc: job status lives in `OfficeJobStatusService`, read from the row; the route row's missing `/jobs` segment fixed |

### Behaviour changes (each intended, each tested)

1. **Idempotency works in production for the first time.** A repeated save is answered as a duplicate of the
   earlier job (039/047 as designed). Before, it ran again.
2. **Every save gets a job row.** Large saves included (§1.3).
3. **A job row that cannot be created refuses the save** (`OFFICE_014`, 502, retryable). It used to continue with a
   job held only in memory.
4. **A Dataverse fault on a job read is a 5xx**, not a 404 "not found".
5. **A finalization failure no longer makes a repeated save run again.** The save's view says Completed, so the
   repeat is a duplicate, and the pane opens the existing document.
6. **A non-terminal job older than 5 minutes reads as abandoned** (Failed, retryable). A retry runs.
7. **Pane:** the SSE path is live; Completed-with-no-document is a visible error; the server's failure text is shown;
   the outcome is applied once.

A caller the resolver cannot map to a `systemuser` can **still** poll their own job: the save's view records the
creator OID it authenticated. This is unchanged from the in-memory behaviour.

## 8. Tests

**Unmodified test methods, all green** (the AC's named sets):
- Job status / SSE: `OfficeEndpointsContractTests` (incl. `Get_OfficeJobStatus_*`), `OfficeEndpointAuthorizationContractTests`,
  `Issue975_OfficeJobStreamProblemJsonTests`.
- Idempotency (039/047): `OfficeSaveSpineIdempotencyContractTests`, `OfficeSaveSpineIdempotencyTests`, `OfficeVersionSaveRevertTests`,
  `OfficeCreateCollisionTests`, `OfficeTypedNameCollisionTests`, `OfficeImmutableSaveFileSafetyTests`, `DuplicateDetectionTests`.
- AI refresh (029): `VersionSaveAiRefreshSeamTests`.

**The one fixture change** (§5): `OfficeVersionSaveWorld` now stores what is written, answers the job read, enforces
the payload limit, returns the typed record, and can fail the next job create.

**New:**
- `Issue1084_OfficeJobRecordDurabilityTests` (9): restart read, restart stream, cross-process duplicate, abandoned
  retry, finalization rewrite, the 502 refusal, and large content × 3.
- `OfficeJobEffectiveStateTests` (11, `tests/unit/domain/Office/`).
- `useSaveFlow.terminalOutcome.test.ts` (7, gated).

**Seeded negative controls** (each applied, run, reverted; no seed text left):

| Seed | Result |
|---|---|
| The rule ignores the save's view | **11 failed** (incl. the 039 finding-3 tests `CompletedCreateSave…`, `CompletedVersionSave…`, `CompletedEmailSave…`, `AttachmentDeduplicated…`) |
| The abandoned rule disabled | **3 failed**: exactly the three abandoned tests |
| `OFFICE_014` not mapped | **1 failed**: *"… to be HttpStatusCode.BadGateway {value: 502} … but found HttpStatusCode.BadRequest {value: 400}"* |
| The pane's poll-side outcome claim removed | **1 failed**: *"Expected number of calls: 1 / Received number of calls: 2"* |

## 9. Gates

| Gate | Result |
|---|---|
| Build | 0 warnings / 0 errors |
| **Full BFF suite** | First run (before the 502 fix): **13,057 / 0 / 54**, exactly 13,065 + 19 new − 27 deleted stub tests. Final run: **13,058 / 0 / 54** = 13,057 + exactly the one new 502 test (a single-process run, rebuilt, nothing concurrent) |
| ArchTests | **337 / 337** (`RouteAuthorizationGuardTests` unchanged: no route or filter added) |
| office-addins | jest (gated) **62 / 62 suites, 816 tests**; lint **0**; typecheck **68** (baseline, all test files, **0 production**); `npm run build` OK |
| **Publish size (§10)** | Fresh master `d68924b93` **47,671,178 B (45.463 MB)** → branch `3511668f4` **47,671,516 B** = **+338 B**. `Compress-Archive` Optimal, incl. PDBs, fresh short-path worktrees (removed), **212 files each side** |
| CVE / packages | No `.csproj` / `.props` / `package*.json` change |
| **Code review** | Coverage-first. **Fixed in review:** (C) `OFFICE_014` rendered as a 400 → now a retryable 502; (W) a double `onComplete` race once SSE is live → once-only claim. **Accepted, stated:** (W) `RecordAsync` stays best-effort: if the TERMINAL write fails, the view stays non-terminal and reads as abandoned after 5 minutes, so a retry runs and meets the name-collision/content-dedup checks; it is logged at Error. (I) A poll costs two Dataverse reads (filter + handler), about one or two per save, because the save is synchronous. (I) `OfficeJobStatusService` is 830 lines, about 420 of them the moved stream: one cohesive job record, decomposed from `OfficeService` (§11.5). **Security:** ownership is enforced on the durable path (filter + `GetAsync`); the view's creator is server-written from the authenticated OID; the payload now carries LESS personal data (no bodies) |
| **ADR check** | **0 violations.** ADR-004 (no bytes in the payload) ✓. ADR-008: filters unchanged ✓. ADR-009: the static cross-request store is removed, and no L1 was added ✓. ADR-010: concrete, no new interface ✓. ADR-017: status and outcome persisted, no orphan jobs, stable codes ✓. ADR-019: `OFFICE_014` ProblemDetails with `errorCode` ✓. ADR-032: unconditional ✓. ADR-038: regression + domain tests, reproduce-first, production-faithful double ✓. ADR-052: no background work added ✓ |

## 10. Not done here, stated

- **Live behavioural check** (AC: "restart the app mid-save"). It is proven in tests at both layers: a restart after
  the save (cloned row, read and stream), a restart during the save (abandoned row, read and retry), and the pane's
  definite outcome on both paths. A live restart on `spaarke-bff-dev` needs the next deploy from master (deferred by
  the owner).
- **Task 068, not this task:** `OfficeProfileDispatcher`'s fire-and-forget `Task.Run`, and
  `JobStatusService._jobSequences` (in-memory sequence counters for the Redis pub/sub).
- **`sprk_jobtype` written with the wrong numbers (§1.5):** nothing the pane reads depends on it any more. Fixing the
  write relabels every new row; left for the owner.
- **`IProcessingJobService.GetEmailArtifactAsync` / `GetAttachmentArtifactAsync`** still return anonymous types as
  `Task<object?>`. They have **no caller at all** (grep over `src/`), so nothing fails today. Whoever first calls one
  must type it first, or it will fail exactly as §1.2 describes.

## 11. Live check on `spaarke-bff-dev`, 2026-10-02 (after the deploy from master `5e39f2bea`)

The owner authorized the deploy and the restarts. The deploy used `Deploy-BffApi.ps1` from a fresh worktree of
`origin/master`: package 45.46 MB, SHA-256 verified, `/healthz` 200. Calls were made with a user token
(`ralph.schroeder@spaarke.com`, a root-BU caller) against the endpoints the pane uses, with the pane's request shape.

| Check (POML ui-tests) | Result |
|---|---|
| Save a document over 40 KB | **202**, job `2dcf1cf0-5bbe-f111-aaaf-0022482913fc` → Completed, document `fb79f621-43c5-4083-a721-d0d482074731` (79,651-byte .docx, unfiled). Owned by the root team "Spaarke", the #1081 path |
| Restart, then read the same job | `az webapp restart` 12:23:03 UTC; new process "Application started" 12:23:29. `GET /api/office/jobs/{id}` → **200 Completed, same document id** (not 404). `GET …/stream` → `connected`, `progress` 100, `job-complete` with the same document id. **PASS** |
| Save the identical document again | **No second document** (one `restart-probe-%` row). The response is the ORIGINAL job's 202, replayed by the `X-Idempotency-Key` response cache, so it reads `duplicate: false`; the pane would show a normal success for the same document rather than "already saved". **Outcome holds; the wording differs from the test's expectation** |
| Cut a save mid-flight, graceful restart | **Cannot cut it.** A 12.6 MB save was started and the app restarted 3 s later (12:25:36); the old process kept serving and the save finished there at 12:25:41 (job `9889cf5c-…`); the new process started 12:25:59. The job reads Completed from the new process: a definite outcome |
| Cut a save mid-flight, **hard kill** (owner: "if we need to produce it live then we should do it") | `kill -9` on the `dotnet` process over SSH (`az webapp create-remote-connection`), sent **0.1 s after the save's job row appeared** (row `58c85c4f-64be-f111-a05b-3833c5e9614d`, polled in Dataverse; a pre-connected SSH session waiting on `read`). The client got **HTTP 502 after 5 s**: a definite error. The row was left `Queued`, 0%. Back up at 13:22:59 |
| Retry inside 2 minutes | **409 `OFFICE_IDEMPOTENCY_CONFLICT`** *"A request with the same idempotency key is currently being processed. Please retry shortly."* That is `IdempotencyFilter`'s Redis in-progress lock (`LockDuration` 2 min), taken by the dead request and never released. Between 2 and 5 minutes a retry would be answered as the dead job, which reads `Queued` until it is 5 minutes old. So after a crash the pane can show "processing" for up to about 5 minutes, never longer |
| Retry after 5 minutes | The dead job reads **Failed / `Abandoned`**, `OFFICE_INTERNAL`, retryable, *"This save did not finish. Check whether the document was saved, then try again."* The retry got **202, a NEW job** `d110de0d-65be-f111-a05b-3833c5e9614d`, **Completed** with document `62669395-…`. It was not answered as a duplicate of the dead attempt. **PASS** |

All three probe documents were deleted afterwards through `DELETE /api/documents/{id}`, along with the four
`sprk_analysis` rows the profile runs had created. The document delete does not remove a document's analysis rows; they
were deleted separately.
