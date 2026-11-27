# Task 013 notes: intake run orchestrator (`IntakeRunJobHandler`, FR-07)

> 2026-11-27. Rigor FULL, opus @ high. Branch `work/spaarke-intake-pipeline-r1`.

## Stage table (written before coding, POML step 1)

The run's stored state is one JSON object in `sprk_processingjob.sprk_stagestatus` (`IntakeRunState`, ids, keys,
codes and numbers only, ADR-015). `sprk_currentstage` holds the stage name; `sprk_status` stays In Progress (1) until
the terminal write. Every write of a delivery is read → claim check → `If-Match` on `versionnumber`
(`IGenericEntityService.UpdateIfRowVersionMatchesAsync`).

| # | Stage | Persisted result (on the run) | Idempotency key / marker | Resume rule |
|---|---|---|---|---|
| 0 | Step 0 | claim `{owner = jobId.nonce (one delivery), at}` in the state; `at` refreshed by every fenced write | conditional write on the version read; a later delivery of the same job takes over after 1 min of silence, another job after 15 min | payload → run read (missing: no-op) → terminal first (no-op) → claim → flag/kill switch → resume at the first stage not in `completed` |
| 1 | Receive | `completed += Receive`, status In Progress, `sprk_starteddate`, `sprk_stages`; request: `sprk_currentrun` = run, stage In review | run id (deterministic, from the door's key) | skipped once completed |
| 2 | Store | verified content document ids | ids from the run state written by the door; each must exist, be active and name the request | skipped once completed; no content → triage |
| 3 | Classify | `{isAgreement, subDomain, confidence}` (no reasoning text) | stage name | **never re-run once stored** (FR-07a); AI kill switch → held |
| 4 | Route | `{requestType, documentType, harnessKey}` or `{triageCode}`; `sprk_harnesskey` | stage name | stored key resolved with `IntakeHarnessRegistry.Resolve`, never re-routed |
| 5 | Analyse | harness steps of stage Analyse (NDA: `segment` = engine job, `edit-plan` = AI) by reference | step name; an external step is marked `awaiting` before dispatch | a stored step is **never re-run**; an awaiting step is not re-dispatched by a duplicate delivery |
| 6 | Build | harness steps of stage Build (NDA: `redline` = engine job) by reference | as 5 | as 5 |
| 7 | Persist | persisted ids by role/kind, output document (`sprk_outputdocument` lookup), approval flag, return roles | `PersistAsync` is idempotent (deterministic ids, harness contract) | skipped once stored |
| 8 | Approve | request stage Awaiting approval; marker `approve` | marker key `intake-approve:{runId}` (task 017 creates the Work Item under it) | waits until `approvedOn` is set (task 017); redelivery is a no-op |
| 9 | Return | marker `return` written BEFORE the door is called | `intake-return:{runId}` passed to the door | door return by channel (`IIntakeDoorReturn`); none registered → held |
| 10 | Record | request stage Returned, then run Completed **last** | marker `record` (`intake-record:{runId}`) | terminal write is the last write |
| T | Triage slot | request stage Needs triage; marker `triage`; run Completed last | `intake-triage:{runId}` (task 016 creates the Work Item under it) | terminal |

Held (kill switch, AI disabled, return unavailable, exhausted final delivery): status stays In Progress,
`sprk_errorcode` + `sprk_errormessage` = admin-attention text (codes and ids only), `held` in the state. A later
delivery (replay, re-drive) resumes from the stored state; it is never Completed or Failed.

## Decision: the `IIntakeHarness.RunAsync` split (brief item, owner order Classify → Segment → Analyse → Build)

`RunAsync` is replaced by **steps**. A harness declares `Steps` (`IntakeHarnessStep(Name, Stage, External)`) and runs
one at a time through `RunStepAsync(step, request, completedSteps)`; `PersistAsync(request, completedSteps)` runs once
after the last step. Classify is the orchestrator's own stage (it needs no harness: it decides which harness).

- NDA (task 015) declares `segment` (Analyse, External = engine job), `edit-plan` (Analyse, AI, uses the `segment`
  result's para_key list) and `redline` (Build, External = engine job). Order is the declared order.
- The orchestrator stores each step's **result reference** (`IntakeHarnessResultReference`: kind + blob/staging
  reference, never content) in the run state BEFORE the next step runs; a stored step is **never re-run** (FR-07a). So
  resume never re-segments or re-plans once persisted.
- A step returns `Completed(reference)`, `Dispatched(executionId)` (external: the run waits) or `Held(code)`.
  An external step is marked `awaiting` before `RunStepAsync` is called; on `Dispatched` the run writes
  `sprk_engineexecutionid`, releases and ends the job Success (a wait). A duplicate delivery during the wait does not
  dispatch again. Task 014's completion calls `IntakeRunStore.RecordExternalStepResultAsync` (accepted only for the
  step the run awaits, once) and enqueues `IntakeRunJobHandler.CreateJob`; the run resumes at the next step.
- The approval flag and return document roles moved to `IntakeHarnessPersistResult`
  (`ApprovalRequired` default true, `OutputDocumentId`, `ReturnPayload`). `IntakeHarnessDocument`/`IntakeHarnessResult`
  were removed (no implementation existed; the 012 test fakes were updated).
- No AI output content is stored on the run row (only references): the POML escalation trigger "resume would require
  persisting AI output content on the run row" does not fire.

## Atomic claim (escalation trigger checked)

Built from an existing primitive: `IGenericEntityService.UpdateIfRowVersionMatchesAsync` (Dataverse `If-Match` on
`versionnumber`; a mismatch is `DBConcurrencyException`). Trigger "cannot be built from existing primitives" does not
fire. The claim owner is one delivery (`IntakeRunStore.ClaimOwner`: `{jobId:N}.{nonce:N}`), so a redelivery of the
same message after a lost lock cannot pass the overrun delivery's fence (found while writing the tests: the first cut
used the job id as owner, and the overrun delivery kept writing; test
`ProcessAsync_HolderWhoseHeartbeatsCannotLand_…WithNoFurtherWrite`). Every fenced write refreshes the claim time, so a
takeover only happens after the holder fell silent (same job: 1 min, the invoice lock's #1086 value; another job: 15 min).

**Heartbeat (review F1, owner-approved 2026-11-27).** While a delivery waits on a long step (content read, AI classify,
harness step, persist, door return) `IntakeRunJobHandler.WhileHeartbeatingAsync` refreshes the claim every
`IntakePipeline:ClaimHeartbeatSeconds` (default 20) through `IntakeRunStore.RefreshClaimAsync` — the same fenced
`WriteRunAsync` (re-read, owner check, If-Match). The interval is clamped by `IntakeRunStore.ClaimHeartbeat` to
5 s … `OwnerTakeoverAge / 3` (20 s), so two missed beats in a row still leave a live claim younger than the 1-minute
takeover age. A `PeriodicTimer` on the injected `TimeProvider`, scoped to one await (not a `BackgroundService`;
ADR-052/036 not engaged); it is stopped and awaited before the next run write. A beat that finds the claim taken over
cancels the step's token and the delivery ends as claim lost (writes nothing more); a beat that fails otherwise is logged
(type name only) and retried at the next beat.

The frozen invoice handlers (`InvoiceSubmissionProcessingJobHandler.cs`, `InvoiceSubmissionRerunJobHandler.cs`,
`Services/Finance/Submission/**`) are not edited: trigger 3 does not fire.

## Task 011 write-path gap (fixed)

`DataverseServiceClientImpl.CreateProcessingJobAsync`/`UpdateProcessingJobAsync` wrote every Guid as a bare Guid; a
lookup needs an `EntityReference` or Dataverse refuses the whole write. Both now call
`ProcessingJobGuidValue(field, value, onCreate)`: `sprk_servicerequest` → `sprk_servicerequest`,
`sprk_outputdocument` → `sprk_document`, `sprk_initiatedby` on create → `systemuser` (unchanged), anything else the
bare Guid (Office writes unchanged). The intake store writes its own rows through `IGenericEntityService` with
`EntityReference`s directly (`sprk_servicerequest`, `sprk_outputdocument`, request `sprk_currentrun`).

## Placement Justification (root §10, `bff-extensions.md`)

In the BFF, as an ADR-004 `IJobHandler` on the existing `sdap-jobs` queue (ADR-052: queue → `IJobHandler`): design §4's
written reason — every stage uses BFF domain code (Dataverse write path, SPE reads, the AI facade, routing), volume is
low, and the one long wait (approval) is a persisted status, not a held delivery. No `BackgroundService`, no Durable
Task, no new queue, no new package. AI is reached only through `Services/Ai/PublicContracts` (`IAgreementAi`,
`IConsumerRoutingService`); `ITextExtractor` (Services.Ai) is used by `IntakeContentTextReader` as
`Services/Communication` and `Services/Workspace` already do (not on the ADR-013 ArchTest forbidden list).
Registration is unconditional (`JobProcessingModule`): every dependency has a Null peer when the AI gate is off
(`NullAgreementAi` → the run is held `intake.ai_disabled`; `ITextExtractor` Null peer → no text → triage).

## §11 — new surfaces

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `IntakeRunJobHandler` (+ DI line) | invoice submission handlers (reuse-scan row 1) | invoice-typed and frozen until eBilling UAT; the generic `IJobHandler`/`JobContract` framework is reused | no intake request is ever processed (FR-07, SC-4) |
| `IntakeRunStore` (+ DI line) | `IProcessingJobService`, `OfficeJobStatusService` | neither writes conditionally on a row version; Office must never see intake rows (FR-06b) | no atomic claim: duplicate AI calls and outputs (FR-07b) |
| `IntakeContentTextReader` (+ DI line) | `AnalysisDocumentLoader` / `DocumentTextSource` | registered inside the AI gate, log the file name, return placeholder text on failure (would be classified as the document) | Classify has no text; nothing routes to a harness (FR-07 stage 3, FR-10) |
| `IntakePipelineOptions` (+ `Configure` line, template section) | `EbillingOptions.Enabled` (one flag, one pipeline) | no options class covers intake; NFR-08 needs per-door and per-harness | a bad harness or door stops only by redeploy (NFR-08) |
| `IIntakeDoorReturn` | `CommunicationService` (email send), External SPA endpoints | a door's return is door-specific (021/043/044); the orchestrator needs one seam to call by channel | Return has no way to reach the requester (FR-07 stage 9) |
| `IntakeRunContracts.cs` records/constants | `sprk_processingjob` docs, `ProcessingJobStatus` | intake-only codes and state shape; nothing to extend | stage results cannot be stored, so resume re-runs AI (FR-07a) |
| `IntakeHarnessStep`/`StepOutcome` (012 contract change) | `IIntakeHarness.RunAsync` (task 012) | changed in place, not added beside | resume would re-run segment/edit-plan (owner order, FR-07a) |
| `ProcessingJobGuidValue` | inline mapping in both mappers | pure modification of existing surface, extracted to be testable | first intake run create fails in Dataverse (011 finding 1) |

## Run ownership (ArchTest census, write-path invariant I-2)

The full ArchTest run flagged the run create as an UNLISTED create of `sprk_processingjob` (unified-access-control-r2
task 146 census) and the store's conditional writes as unlisted generic update writers (task 188 r4e). The census
procedure forbids waiving a row that names a core record, so `CreateRunAsync` now takes its owner team from
`IRecordOwnershipResolver` (`RecordOwnershipContext.ForChild` over the run's lookups: its request, secure-if-any) and
refuses with `IntakeRunOwnerRefusedException` (nothing written) when none resolves. Listed `Routed` in
`RecordOwnerAssignmentCensusTests` (census + owner-write census); the five store writers are listed in
`ExternalDocumentNarrowingGuardTests.GenericWritersThatUpdateNoDocument` (they write `sprk_processingjob` or
`sprk_servicerequest` only). Doors (021, 022, 040, 044) must map `IntakeRunOwnerRefusedException` to their own error
contract.

**Dev changes for routed ownership (owner "Approve, do it now", 2026-11-27; spaarkedev1; ids and codes only).**

| Change | Evidence |
|---|---|
| `sprk_createdbyperson` on `sprk_processingjob` | `scripts/Set-ChildRecordCreatorPersonSchema.ps1` (`$Tables` + `sprk_processingjob`): dry run WOULD create + secure; `-Apply` created lookup → systemuser, relationship `sprk_systemuser_sprk_processingjob_createdbyperson`, secured, Readers/Writers profiles granted, in SpaarkeCore; `-Verify` PASS. `RecordCreatorPerson.StampedChildTables` + `sprk_processingjob`; `CreatorPersonColumnSecuredGuardTests.SecuredOnlyByDeployScript` + `sprk_processingjob` (its SpaarkeMaster Entity.xml does not define the column, as `sprk_policy`'s) |
| Refusal probe (howToExtend) | probe row owned by team Secure Record Owners `6eabc7f9-13be-f111-a05b-0022482913fc` (BU `d9ec0b6f-80a0-f111-aaac-000d3a99d1d7`), 2026-11-27T20:09:33Z: HTTP 403 `0x80040299` "Read Privilege Check For Owner failed", missing `prvReadsprk_ProcessingJob` (`89832fe1-9118-468d-8c43-7f84a8d6fbb0`), OTC 10857 |
| Role entry | `config/secure-record-owner-role.json` + `sprk_processingjob` (kind `child`, privilegeName from metadata); `scripts/Set-SecureRecordOwnerRolePrivileges.ps1` dry run MISSING 1 → `-Apply` added at Basic on role `e4ebabd9-b4a0-f111-aaac-000d3a99d1d7` → `-Verify` PASS 41/41 |
| Re-probe and cleanup | 20:13:01Z HTTP 204 (owner the team); probe row DELETE 204, GET 404 |

## Deviations from the POML

- **Extra file `IntakeContentTextReader`**: Classify needs the document text and no reader is resolvable when the AI
  gate is off (§11 row above). App-only, pointer-guarded, never logs text.
- **`IntakeHarnessDocument`/`IntakeHarnessResult` removed** with the `RunAsync` split (task 012 contract; no
  implementation existed).
- **`appsettings.template.json`**: `IntakePipeline` section added (kill switches; absent = on).
- **Run ownership routed** (section above): not in the POML; required by the ArchTest census.
- **Content no longer a live document of the request** → triage (`intake.content_invalid`), not Failed (review F5:
  Failed left the request In review with nobody told).

## Known limits

- A claim never released (process killed mid-delivery) blocks deliveries of OTHER jobs for the 15-minute lease; a
  later delivery of the same job takes it after 1 minute of silence. Retries can run out first; the run is then
  dead-lettered without attention text and the admin re-drive (task 023) recovers it.
- An external step whose result the engine completion records is stored, but the stage marker (`Analyse`/`Build`
  in `completed`) is only written for steps that complete in-process. The step results drive resume; the marker is
  display only.
- A stored state that no longer parses (a future `IntakeRunState` shape change) fails before the claim: the run is
  dead-lettered without attention text. `IntakeRunState.Version` exists for that migration; none exists today.
- The door-off switch (`IntakePipelineOptions.IsDoorEnabled`) is enforced by each door, not here.

## Slots left for later tasks

| Task | Slot |
|---|---|
| 014 engine dispatch | `IIntakeHarness` external steps return `Dispatched(executionId)`; the completion calls `IntakeRunStore.RecordExternalStepResultAsync` then enqueues `IntakeRunJobHandler.CreateJob` |
| 015 NDA harness | `IIntakeHarness` with steps `segment` (External), `edit-plan`, `redline` (External); `PersistAsync` idempotent on deterministic ids; one `AddScoped<IIntakeHarness, …>` line in `JobProcessingModule` |
| 016 triage | `TriageAsync`: Work Item under marker key `intake-triage:{runId}` |
| 017 approval | `AwaitApprovalAsync`: Work Item under `intake-approve:{runId}`; the endpoint sets `IntakeRunState.ApprovedOn` (fenced write) and enqueues `CreateJob`; assigned attorney `sprk_assignedtointernal` |
| 020 status emails | each `WriteRequestStageAsync` call is a status change; `sprk_sendemailupdates` |
| 021 / 022 / 040 / 044 doors | `IntakeRunStore.CreateRunAsync` (deterministic run id from the door's pass key) + `CreateJob`; door-off check; map `IntakeRunOwnerRefusedException` |
| 021 / 043 / 044 returns | `IIntakeDoorReturn` per channel, idempotent on `IntakeReturnContext.IdempotencyKey` (`intake-return:{runId}`) |
| 023 admin re-drive | enqueue `CreateJob` for a held run; the claim write clears the hold |

## Step 9.5 — adr-check

ADRs considered (mapping + `.claude/adr/INDEX.md` pass). In: 001, 002, 004, 007, 010, 013, 015, 028, 029, 032, 036,
038, 044, 052. Out: 006/012/021/022/026/030/031/050/051 (client), 008 (no endpoint), 033/037/039–043/045–049 (no
chat, playbook, memory, communication or Compose surface), 027 (no solution change).

| ADR | Result | Evidence |
|---|---|---|
| 001 | Compliant | no Functions / Durable Task in the BFF |
| 002 | Compliant | no plugin; the request stage and run invariants have one owner (`IntakeRunStore`); the run's owner fails closed (`IntakeRunOwnerRefusedException`) |
| 004 | Compliant | `IJobHandler`, ids-only payload, idempotency key `intake-run-{runId:N}`, `IsFinalDelivery` → Poisoned; atomic claim by If-Match (A1 §4), not `IIdempotencyService` |
| 007 | Compliant | SPE bytes through `ISpeFileOperations` (`IntakeContentTextReader`) |
| 010 | Compliant | concretes registered; one new interface `IIntakeDoorReturn` is a real seam (one implementation per door: 021, 043, 044) |
| 013 | Compliant, with a note | AI only through `Services/Ai/PublicContracts` (`IAgreementAi`, `IConsumerRoutingService`); `ITextExtractor` from `Services.Ai` is a text-extraction utility, used the same way by `Services/Communication` and `Services/Workspace`, and not on the ArchTest forbidden list |
| 015 | Compliant | run state, error text and logs hold ids, references and codes only (test `…CarryNoContent…`) |
| 028 | Compliant | app-only through the existing identity; no credential added |
| 029 | See publish size below | no package added |
| 032 | Compliant | kill switches via `IOptionsMonitor`; `NullAgreementAi` → the run is held, not failed |
| 036 | N/A, compliant | no scheduled job, no `BackgroundService` |
| 038 | Compliant | new tests under `tests/integration/data-mutation/**` and `tests/unit/domain/**`; seeded proofs below |
| 044 | Compliant | GUIDs cross the Dataverse boundary as `Guid`/`EntityReference`, never as text keys; keys and markers use `:N` (lowercase) |
| 052 | Compliant | queue → `IJobHandler` with the written reason (Placement Justification above) |

No violations; no ADR tension raised.

## Step 9.5 — adversarial review (one pass, opus) and dispositions

| # | Finding | Class | Disposition |
|---|---|---|---|
| 1 | A duplicate of the SAME message (lost lock, or a refused duplicate's 1-minute retry copy) takes the claim over after 1 minute while the first delivery is still inside a long AI call (no write refreshes the claim during the call); the in-flight AI call is paid twice (outputs are not duplicated: the overrun delivery's fence refuses its writes) | F1 | Fixed (owner chose the heartbeat, 2026-11-27): claim heartbeat, "Atomic claim" above. Tests `…InALongAiCall_TheHeartbeatKeepsTheClaimAndTheRedeliveryWritesNothing` (20 s and 5 s; fails with the heartbeat seeded off) and `…HolderWhoseHeartbeatsCannotLand_IsTakenOver…`; bounds `IntakeClaimHeartbeatTests` |
| 2 | An external step whose result the engine completion recorded between the claim read and the awaiting mark could be dispatched again | F2 | Fixed: the awaiting mark skips a stored step and the loop re-checks the stored state. Test `ProcessAsync_EngineResultRecordedBetweenTheClaimAndTheAwaitingMark_IsNeverDispatchedAgain` (fails without the fix) |
| 3 | `WriteRequestStageAsync` was check-then-set and unconditional; a re-driven older run could take the request back from a newer live run | F3 | Fixed: conditional on the request's row version (re-read and retried), and a run becomes current only when the request has none or its current run is gone or terminal. Test `ProcessAsync_OlderRunReDrivenWhileANewerRunIsLive_NeverTakesTheRequestFromIt` (fails without the fix) |
| 4 | A state over 10,000 characters or a state that no longer parses leaves a run with no attention text | F4 | Partly a known limit: an oversized state throws at `CreateRunAsync` (the door), before any row exists; an unparseable state needs a future shape change (`IntakeRunState.Version`). Listed under Known limits |
| 5 | Content that is no longer a live document of the request failed the run and left the request In review | F4 | Fixed: goes to triage (`intake.content_invalid`); test renamed `…GoesToTriageBeforeAnyAiCall` |
| 6 | The in-memory Dataverse returns every column regardless of the column list | K | Known limit of the test bed; every column the code reads was checked |

## Results

**Tests** (targeted filters, shared CPU): `IntakeRunJobHandlerTests` 20, `IntakeRunResumeTests` 12,
`ProcessingJobLookupMappingTests` 8, `IntakeHarnessRegistryTests` 20, `IntakeHarnessBindingContractTests` 4 — all
pass. Wider filter (Intake, ProcessingJob, DiGraph, JobProcessing, OfficeJob, RecordOwnership): 3253 passed,
0 failed, 4 skipped (pre-existing skips). BFF build `--no-incremental`: 0 warnings, 0 errors.

**Seeded proofs** (each weakening applied, test run, file restored and compared byte for byte):

| Weakening | Test that fails |
|---|---|
| Claim write unconditional (check-then-set) | `ProcessAsync_SameMessageDeliveredTwiceConcurrently_RunsAiAndPersistOnce` |
| Stored-result check dropped for Analyse steps | `IntakeRunResumeTests` crash points `step:segment`, `step:edit-plan`, `step:redline`, `stage:Persist`, `marker:approve` + the external-step test (6 of 11) |
| Claim owner = job id (no per-delivery nonce) | `…HolderWhoseHeartbeatsCannotLand_IsTakenOver…WithNoFurtherWrite` (then named `…TheOverrunDeliveryWritesNothingMore`) |
| Old Guid mapping (bare Guid lookups) | `ProcessingJobLookupMappingTests` 4 of 8 (the run with the old mapping also counted 1 unrelated filter match) |
| No re-check after the awaiting mark (F2) | `…EngineResultRecordedBetweenTheClaimAndTheAwaitingMark_IsNeverDispatchedAgain` |
| Any run may become current (F3) | `…OlderRunReDrivenWhileANewerRunIsLive_NeverTakesTheRequestFromIt` |
| Owner refusal ignored | `CreateRunAsync_NoOwnerTeamResolvesForTheRequest_RefusesAndCreatesNothing` |
| Heartbeat off (F1; `WhileHeartbeatingAsync` never starts the beat) | `…InALongAiCall_TheHeartbeatKeepsTheClaimAndTheRedeliveryWritesNothing` (20 s and 5 s): "Expected redelivered.Status to be JobStatus.Failed … but found JobStatus.Completed" — the redelivery took the run over; `…HolderWhoseHeartbeatsCannotLand…` also fails (no beat reaches Dataverse) |

**After the owner-approved dev changes and F1** (2026-11-27): `IntakeRunJobHandlerTests` 22 (the overrun test replaced by the two
heartbeat tests), `IntakeClaimHeartbeatTests` 8; wider filter (Intake, ProcessingJob, DiGraph, JobProcessing, OfficeJob,
RecordOwnership, CreatorPerson): 3318 passed, 0 failed, 4 skipped. BFF build: 0 warnings, 0 errors.

**ArchTests** (`dotnet test tests/Spaarke.ArchTests/`): the three earlier failures (`Task 146 b2`, `UAC 099b r3 F4`, `Task 146
c1-r1`) pass. Full run: 2843 passed, **2 failed — both `Task 182` (SecureFamilyConsistencyTests), open below**. Two
creator-person guard failures the role/column change surfaced were closed by the `SecuredOnlyByDeployScript` entry (the
guard's documented route for a column created by the deploy script).

**Publish size** (fresh worktrees at short paths, `dotnet publish -c Release`, `Compress-Archive`): master
`d7ab69b1ab` 194 files, 43,721,605 bytes; branch `acea991584` 194 files, 43,734,622 bytes; **delta +13,017 bytes
(+0.01 MB)**. Total 43.7 MB (< 55 MB). No package added; `dotnet list package --vulnerable --include-transitive`: no
vulnerable packages.

## 🔔 Open for the owner

Items 1 (run ownership: role + column) and 2 (F1: heartbeat) of the earlier list are done (sections above).

1. **Task 182: `sprk_processingjob` is a kind `child` role entry with no `SecureChildLineage` row.** The family contract
   (C7) requires every role-set child to have a lineage row (or a reasoned `RoleSetNotInLineage` entry). Not resolved here
   ("don't waive"): a lineage row needs a closed `ExternalMarker` choice (an external-visibility decision) and makes the
   secure reconciler and share mirror re-own and mirror EVERY processing-job row (Office rows included) filed under a
   secure document/root, which is uac-r2's model. The run is filed only under a service request, which the secure walk
   never looks through (`IsReparentableChild`, D-36), so a run is never Secure-owned through lineage. Options: (a) a
   lineage row from live metadata (lookups to a root or codified child, e.g. `sprk_outputdocument` → `sprk_document`)
   with an `ExternalMarker` decided by uac-r2; (b) a `RoleSetNotInLineage` entry with that reason, signed off by uac-r2;
   (c) a new role-config kind for a routed table never mirrored, added to `RoleSetKinds` by uac-r2. Recommendation:
   (b), reviewed by uac-r2 (smallest change, no reconciler behaviour change); (a) if uac-r2 wants run rows mirrored.
