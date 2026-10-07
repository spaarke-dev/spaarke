# Task 093: a saved document's job row never reaches a terminal state, and `using var activity = Activity.Current` ends the request's own trace span early

> **Date**: 2026-10-03 · **Rigor**: FULL (sonnet / xhigh) · **Mode**: directional
> Evidence record: `projects/spaarkeai-word-add-in-r1/notes/042-uat-round3-2026-10-03.md` §4.
> Base: branch `work/spaarkeai-word-add-in-r1` @ `534bfc360` (task 068/078/088/092 landed). Written per the
> POML's xhigh-effort instruction: the root-cause trace for (A) below was done, and recorded, before any
> production code changed.

## 1. (A) Root cause: why the 10-02 midday rows closed and the later ones did not

### 1.1 What the code does today (before this task's fix)

`UploadFinalizationWorker.ProcessAsync` Step 10 (`UploadFinalizationWorker.cs`, was `:349-364`):

```csharp
if (payload.TriggerAiProcessing)
{
    await QueueNextStageAsync(message, documentId, payload, driveId, itemId, cancellationToken);
}
else
{
    await UpdateJobStatusAsync(message.JobId, JobStatus.Completed, "Complete", 100, cancellationToken, documentId);
}
```

`QueueNextStageAsync` (`:826-898`) submits the `AppOnlyDocumentAnalysis` job via `JobSubmissionService.SubmitJobAsync`
to the `sdap-jobs` queue — unconditionally, every time this branch runs — and then, only if requested,
`EnqueueRagIndexingAsync` / `EnqueueInsightsIngestAsync`. **It returns without ever writing a terminal status for
the `sprk_processingjob` row.** Control then falls through to Step 11 (temp-file cleanup) and `ProcessAsync`
returns `JobOutcome.Success()` having last written `Running / FileUploaded / 70` at Step 9.

### 1.2 Why nothing closes the row: the one place that used to is orphaned

`grep`-ing `UpdateProcessingJobAsync` / `IProcessingJobService` / `sprk_processingjob` across
`Services/Ai/Jobs/AppOnlyDocumentAnalysisJobHandler.cs` returns **zero matches** — confirmed by reading the
handler (task 068's own note, §7.2 ADR-017 row: *"this job type's status is persisted on the **document**
(`sprk_filesummarystatus`)"*). Profiling and RAG indexing persist their own outcomes on the **document**, never on
the Office save's job row.

The only code that ever wrote `Completed` to this row from the AI side is `ProfileSummaryWorker.CompleteJobAsync`
/ `UpdateJobStatusAsync` (`Workers/Office/ProfileSummaryWorker.cs:406-528`) — a `BackgroundService` still
registered and running (`OfficeWorkersModule.cs:52-56`), listening on the Service Bus queue **`"office-profile"`**.
But `grep "office-profile"` across `src/server/api/Sprk.Bff.Api` shows the ONLY other reference is the constant's
own declaration (`UploadFinalizationWorker.cs:66`, `ProfileQueueName`) — **never used as a `CreateSender` target
anywhere**. `QueueNextStageAsync` sends to `sdap-jobs` via `JobSubmissionService`, not to `"office-profile"` via a
`ServiceBusSender`. The git history confirms exactly why: commit `2f3dcbfac`
(`refactor(office): simplify AI analysis by reusing AppOnlyDocumentAnalysisJobHandler`, **2026-01-28**, nine months
before this task) moved the Office save's AI-queueing off `"office-profile"` onto the `sdap-jobs`/
`AppOnlyDocumentAnalysisJobHandler` path, explicitly noting *"Eliminates need for ProfileSummaryWorker (can be
deleted in future cleanup)"* — and then never deleted it. `ProfileSummaryWorker` has been running, listening on a
queue nothing sends to, since that commit. This is the structural defect: **it has applied to every save with AI
processing on, for nine months**, not something that changed on 2026-10-02.

### 1.3 Why every real save's `TriggerAiProcessing` is true

`SaveRequest.TriggerAiProcessing` defaults `true` (`Models/Office/SaveRequest.cs:72`) and flows verbatim onto the
payload (`OfficeJobQueue.cs:74`). Two client paths set it explicitly:

- **Ribbon Quick Save** (`quickSaveHelpers.ts:77,192`) sends `triggerAiProcessing: true` unconditionally — pinned by
  its own test (`quickSaveHelpers.test.ts:35,155`).
- **The pane** (`useSaveFlow.ts:998-999`) computes it as
  `processingOptions.deepAnalysis || processingOptions.ragIndex || processingOptions.profileSummary`, from
  `DEFAULT_PROCESSING_OPTIONS = { profileSummary: true, ragIndex: true, deepAnalysis: false }` (`:306-308`) —
  **true by default** for every save the user has not manually toggled both AI options off.

There is no shipped UI path that produces `false` for a genuine save. So since 2026-01-28, every real save's job
row has stayed `Running / FileUploaded / 70` forever once `UploadFinalizationWorker` finished its own steps.

### 1.4 Why the three 10-02 midday rows DID close

They did not go through a real save with default options. The timestamps in the midday block
(12:22, 12:25, 13:27) match — to the minute — the project's own **prior-task verification calls**, not owner
UAT: `notes/060-job-status-store-and-extraction.md` §11 records a live check on `spaarke-bff-dev` with
`"Restart, then read the same job" — az webapp restart 12:23:03 UTC`, and a hard-kill probe at
`"13:18:45 … 13:20:12 … 13:22:59"`; `notes/068-durability-siblings.md` §9 reuses **the same document**
(`fb79f621-43c5-4083-a721-d0d482074731`, *"saved for 060's check, §11 there"*) to test Generate Profile as a
**separate, later** step — which only makes sense if that original save's own AI processing was suppressed, i.e.
sent with the inline-`Completed` branch (`payload.TriggerAiProcessing = false`), precisely so the save's own
completion could be verified independently of AI processing (which 068 then exercised on purpose, through the
Generate Profile button, a different code path entirely — `OfficeProfileQueue`, not `UploadFinalizationWorker`).
The five rows after 20:49 are the owner's real UAT saves through the pane, which — per §1.3 — always carry
`TriggerAiProcessing: true` and hit the never-closes branch. This is inferred from the exact timestamp
correlation and the Generate-Profile-reuses-the-same-document fact in 068 §9, not from a literal request-body log
line for the midday calls (not re-pulled from App Insights for this task); it is the only explanation consistent
with the code read in §1.1–§1.3.

## 2. (A) Fix: the owner of "pipeline finished"

**Chosen: the finalization worker** (POML's preferred default) — `UploadFinalizationWorker` marks the row
`Completed` immediately after `QueueNextStageAsync` returns, with `CurrentStage = "AiAnalysisQueued"`,
`Progress = 100`. Not the shared `AppOnlyDocumentAnalysisJobHandler`: §1.2 traced that profiling and indexing
already persist their own outcomes on the **document** (`sprk_filesummarystatus`, `sprk_searchindexed`), so
threading the Office job id into that handler would duplicate a status surface that already exists elsewhere and
would couple a shared, multi-producer handler (also used by `EmailToDocumentJobHandler`, Generate Profile, and
email-attachment children) to one caller's row. The trace did not show a reason to deviate from the preferred
default.

**Escalation trigger check** (POML): *"a consumer relies on the row staying non-terminal"* — does not fire.
`OfficeJobStatusService.ToEffectiveView` (the ONE rule both the status read and the idempotency check use) prefers
the save's own view in `sprk_result` whenever present, and that view is **already terminal** (Completed, with the
artifact) the moment the synchronous save returns — it is written by `OfficeService`/`OfficeJobStatusService`
at save time, a code path this task does not touch. The pipeline columns (`sprk_status`/`sprk_currentstage`/
`sprk_progress`) that this fix closes are read only as a **fallback**, for rows written before task 060 that carry
no `sprk_result` view at all (`FromPipelineColumns`). No current code path reads the pipeline columns as the
authoritative pane-facing answer, so nothing depends on them staying non-terminal.

**What changed** (`UploadFinalizationWorker.cs`, Step 10's `if (payload.TriggerAiProcessing)` branch):

```csharp
await QueueNextStageAsync(message, documentId, payload, driveId, itemId, cancellationToken);
await UpdateJobStatusAsync(message.JobId, JobStatus.Completed, "AiAnalysisQueued", 100, cancellationToken, documentId);
```

- If `QueueNextStageAsync`'s one un-guarded call (`_jobSubmissionService.SubmitJobAsync` for the
  `AppOnlyDocumentAnalysis` submission) throws, the new line is never reached — control falls to `ProcessAsync`'s
  existing outer `catch`, which correctly marks the row `Failed` on the last attempt. (RAG/Insights enqueue calls
  are each wrapped in their own try/catch already, so they cannot reach this new line via an exception either —
  unchanged from before.)
- `sprk_result` (the save's own view, task 060) is untouched: the anonymous update object this method sends to
  `IProcessingJobService.UpdateProcessingJobAsync` carries exactly `Status`, `Progress`, `CurrentStage`,
  `ErrorCode`, `ErrorMessage`, `CompletedDate` — the same five columns the "no AI" branch already wrote, no
  `Result` property. Pinned by a test assertion (§4).
- This write is **best-effort**, exactly like the existing "no AI" branch and exactly like `RecordAsync`'s
  documented best-effort write in the save path (task 060 gates table, accepted there as a Warning). A transient
  Dataverse fault on THIS specific write leaves the raw row non-terminal — reduced from "always stuck" to "stuck
  only on a secondary failure," not eliminated. Accepted, consistent with the existing pattern; not a new risk this
  task introduces.
- **Not done, deliberately out of scope**: deleting `ProfileSummaryWorker`, `IndexingWorkerHostedService`, or the
  dead `ProfileQueueName`/`IndexingQueueName` constants. They are orphaned (§1.2) but their removal is a larger,
  separate change (a running `BackgroundService` + its DI registration + its own test file) with no acceptance
  criterion asking for it. Recorded here for the owner, not fixed silently.

## 3. (B) Fix: `using var activity = Activity.Current` at 20 sites

**Root cause** (already clear from the code, no trace needed): `Activity.Current` is the AMBIENT request Activity
— started by ASP.NET Core's request-tracing middleware (or the OBO/app-only pipeline above the call), not by the
method reading it. `using var activity = Activity.Current;` disposes (= **stops**) that Activity the moment the
method returns. Stopping someone else's span ends their request's own trace early: it is then exported with
whatever `resultCode`/`success`/duration had accumulated up to that point, not what the request actually produced.
`UploadSessionManager.UploadSmallAsync:122` turned a real 3-second, successful Word save into `resultCode 0`,
`success=false`, `1.4s` in App Insights (`notes/042-uat-round3-2026-10-03.md` §4.2). The identical line appears at
19 more sites: `DriveItemOperations.cs` ×15, `ContainerOperations.cs` ×4.

**Fix chosen**: drop `using` at all 20 sites — `var activity = Activity.Current;` with no disposal. Tags still
land on the ambient span exactly as before (`activity?.SetTag(...)` is unchanged); only the premature `Dispose()`
is removed. **Not** a child span from an `ActivitySource`: every one of the 20 sites only ever wanted to annotate
the CALLER's span, never to create its own — a child span would change each call's telemetry shape (new span ids,
a different parent/duration relationship) for no behavioural need the evidence shows. Graph call behaviour is
unchanged (confirmed: the edits touch only the `Activity.Current` line itself, nothing else in any of the 20
methods).

## 4. Tests

### 4.1 (A) `tests/unit/domain/Office/UploadFinalizationOwnerTeamTests.cs` (existing class, 2 new `[Fact]`s)

The real `UploadFinalizationWorker`, `ProcessAsync` called directly, doubled only at its module boundaries
(`IProcessingJobService`, `JobSubmissionService`, `ITenantCache` → the repo's existing `InMemoryTenantCache`) —
per `tests/CLAUDE.md`'s "mock at module boundaries" rule and the established pattern in
`tests/integration/seam/Office/VersionSaveAiRefreshSeamTests.cs` / `Issue1086_OfficeBackgroundWorkRestartTests.cs`
(both already construct `UploadFinalizationWorker` + `Mock<JobSubmissionService>(MockBehavior.Loose, …,
new Mock<ServiceBusClient>().Object)` this same way). `ContentType = Document` with `DocumentId` already set and
`AiOptions = { ProfileSummary: true, RagIndex: false, InsightsIngest: false }` keeps the exercised surface to
exactly the branch under test (no email/attachment/RAG/Insights side paths, each of which would need its own
boundary double).

- `ProcessAsync_SuccessfulSaveWithAiProcessing_EndsTerminalCompletedWithAStageNamingWhatWasHandedOn` — asserts
  `JobSubmissionService.SubmitJobAsync` was actually called once (the hand-off claim must be true, not just
  worded that way), then that the row's terminal write is `Status=2 (Completed)`, `CurrentStage="AiAnalysisQueued"`,
  `Progress=100`, and that the update object carries **no** `Result` property (pins the `sprk_result`
  untouched constraint). **Seeded-reverted and confirmed red** before restoring the fix (failure showed the row
  stuck at `Status=1/FileUploaded/70`, matching the reported defect verbatim).
- `ProcessAsync_FinalizationFailsOnTheLastAttempt_EndsTerminalFailed` — `SubmitJobAsync` throws; asserts
  `outcome.ErrorCode == "OFFICE_012"`, `Retryable == false` (last attempt), and the row's terminal write is
  `Status=3 (Failed)`, `CurrentStage="Failed"`, `Progress=0`.

### 4.2 (B) `tests/unit/domain/Office/UploadSessionManagerActivityLifetimeTests.cs` (new)

`UploadSmallAsync_DoesNotStopTheCallersRequestActivity_UntilTheCallerStopsItItself` — an `ActivityListener`
(`ActivityStopped` callback) watches a test-owned `ActivitySource`'s request Activity (set as `Activity.Current`,
exactly as ASP.NET Core would leave one running). `IGraphClientFactory.ForApp()` is made to throw so the method
returns (by propagating, via its own `catch (Exception ex) { …; throw; }`) without a live Graph dependency —
irrelevant to the assertion, since `Activity.Current` is read/tagged as the method's first statement, before
`ForApp()` is ever called. Asserts: no `ActivityStopped` callback fired for the request Activity, and its
`Duration` is still `TimeSpan.Zero` (only set by `Stop()`); then the TEST (standing in for the caller) calls
`.Stop()` itself and confirms the listener now sees it — proving the listener would have caught an earlier stop
had `UploadSmallAsync` caused one.

### 4.3 (B) `tests/Spaarke.ArchTests/ActivityCurrentDisposalGuardTests.cs` (new — the seeded guard)

Source-scan structural fitness function (the repo's established `SourceScan` machinery, same pattern as
`FabricatedResultGuardTests`/`SpeUploadPathIsFlatGuardTests`): `NoMethodDisposesActivityCurrent` fails the build if
`using var X = Activity.Current` (either `using var` or `using (var … )` form) appears anywhere under
`src/server/**`; a non-vacuity check pins that the fixed shape (`var activity = Activity.Current;`, no `using`)
is still found at **≥ 20** sites, so the scanner is proven to be reading the tree. Negative control fires on both
`using` forms under a different local name; positive control passes on the fixed shape, on a genuinely OWNED
child span from an `ActivitySource` (must not be confused with the ambient one), on prose, and on the real tree.

**Seeded**: reverted `UploadSessionManager.cs:122` to `using var activity = Activity.Current;`, ran the guard —
**failed**, naming the exact file:line (`UploadSessionManager.cs:122: using var activity = Activity.Current;`).
Restored the fix; guard green again.

## 5. Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release` | 0 warnings / 0 errors |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj -c Release` | 0 warnings / 0 errors |
| **BFF Office + Graph test scope** (`--filter "FullyQualifiedName~Office\|~Graph\|~UploadFinalization\|~UploadSession\|~DriveItemOperations\|~ContainerOperations"`) | **1008 passed / 0 failed / 9 skipped** (the 9 skips are pre-existing, unrelated to this task) |
| **The 2 new (A) tests + 1 new (B) behavioural test**, isolated | 3/3 passed; the (A)-success test seeded-reverted → red with the exact reported symptom, then green |
| **ArchTests (full suite)** | **349 / 349** passed (3 of these are the new `ActivityCurrentDisposalGuardTests`; seeded-reverted → red naming the exact offending line, then green) |
| `dotnet format --verify-no-changes` on all touched/added files (prod + test) | Clean (two new test files needed one `dotnet format` pass each for CRLF line endings — fixed, then re-verified clean) |
| CVE (`dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api.csproj`) | **No vulnerable packages.** No `.csproj`/package-manifest change in this task (none was needed) |
| Publish size vs fresh master | **Owed to the main session** per this run's briefing (CLAUDE.md §10: this worktree has been built repeatedly across tasks 088/089/092, so a publish measured from it is not comparable to a fresh `origin/master` build; the main session measures once for the PR from committed state) |

## 6. Code review / ADR check (self-assessed, coverage-first; Step 9.5)

**Code review** (via `/code-review` procedure, applied directly — no Critical, no Warning requiring a fix before
merge):
- Security / Performance: no new surface; no blocking calls; no secrets.
- AI code smells (5-point checklist): none introduced — no new single-impl interfaces, no catch-log-rethrow, no
  null-check-on-non-nullable, comments explain WHY (defect history + the orphaned-worker root cause) not WHAT,
  no god-method (the changed branch gained 7 lines, no new responsibility).
- **Accepted, stated** (Suggestion, not a blocker): the new terminal write is best-effort, inheriting the exact
  same characteristic the "no AI" branch and `OfficeJobStatusService.RecordAsync` already have (§2, bullet 4).
- **Accepted, stated** (Suggestion): the ArchTests guard's non-vacuity threshold (`>= 20`) will need bumping if a
  future task legitimately adds MORE `Activity.Current` read sites, and would false-positive-fail if a future
  consolidation legitimately drops below 20 — the same tradeoff every other non-vacuity check in this file set
  already accepts (`SpeUploadPathIsFlatGuardTests`'s own `>= 10` / `>= 5`).
- **Observed, not fixed, recorded for the owner**: `ProfileSummaryWorker` + `IndexingWorkerHostedService` (and
  their dead `"office-profile"`/`"office-indexing"` queue constants) have been orphaned since 2026-01-28 (§1.2) —
  a live `BackgroundService` permanently idle. Deleting them is a separate, larger change than this task's scope.

**ADR check**:
- **ADR-017** (job status persistence) — ✅ this task's entire point: the row now persists a terminal outcome for
  every save with AI processing on, closing the gap §1 traced.
- **ADR-004** (job contract) — ✅ unchanged; no new job type, no new queue, no change to `JobContract` shape.
- **ADR-010** (DI minimalism) — ✅ no new registrations.
- **ADR-013** (AI architecture / facades) — ✅ no new CRUD→AI dependency; `AppOnlyDocumentAnalysisJobHandler` is
  untouched.
- **ADR-038** (testing strategy) — ✅ new tests live under KEEP paths (`tests/unit/domain/**`,
  `tests/Spaarke.ArchTests/**`); mocks are at module boundaries only (no `Mock<HttpMessageHandler>`); each new
  test asserts real behaviour (terminal status + stage + no-Result; Activity not stopped), not wiring; the guard
  test is a structural fitness function per Amendment A1, with both a negative and positive control and a
  non-vacuity check, matching the established house pattern exactly.
- **bff-extensions.md §F (Test Update Obligation)** — ✅ this PR modifies `Workers/Office/` and
  `Infrastructure/Graph/` and includes corresponding test additions in `tests/unit/` (via the compiled-in
  `tests/unit/domain/**` and `tests/Spaarke.ArchTests/**` globs).
- **CLAUDE.md §11 (component justification)** — N/A for the production fix (modification of existing files only,
  which the rule explicitly exempts). The new ArchTests guard file and new domain test file are coverage for
  EXISTING code, explicitly required by bff-extensions.md §F and ADR-038's "every bug = regression test," not new
  production surface — consistent with 12 existing precedent files in `tests/Spaarke.ArchTests/` alone.

No escalation fired. Neither POML `<escalation>` trigger applies (§2 above; `/conflict-check` found zero file
overlap with any open PR or active worktree before editing `Infrastructure/Graph`).

## 7. Not done here, stated

- **Live check on a deployed BFF** (acceptance criterion: *"a Word save's request shows its real status (202) and
  full duration in App Insights, and its job row reads Completed"*). **Left open.** This worktree has no
  authorization in this session to deploy; per the parallel-wave briefing, the main session measures publish size
  and (if appropriate) coordinates any deploy from committed state. Stated per the criterion's own fallback
  ("If no deploy is available, leave open and say so").
- Deleting the orphaned `ProfileSummaryWorker`/`IndexingWorkerHostedService` (§2, §6) — filed for the owner, not
  this task's scope.
