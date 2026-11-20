# 084h-3: the repair job for a per-record Procedure Decision Record

> **Task:** `tasks/084h-3-procedure-change-record-repair-job.poml` (FULL) · **Design:** `084h-procedure-change-decision-records-design.md` §7.3, as settled by §16 items 3-4 and §17 (EB-D-239 Q10) · **Base:** `task/ebilling-084h-3` from 5508b576fe · **Status:** built and tested; review round 1 fixed (see "Review round 1"). The ADR-004 tension is resolved by task 084h-5 (Path C, comply; EB-D-247).

## What was built

| Item | Where |
|---|---|
| `RecordProcedureDecisionEnqueuer`: implements 084h-2's `IRecordProcedureDecisionEnqueuer` as an ADR-004 job | `Services/Signals/RecordProcedureDecisionJob.cs` |
| `RecordProcedureDecisionJobHandler : IJobHandler` (job type `RecordProcedureDecision`) | same file |
| `RefusalClasses`: every `DecisionRecordRefusalReason`, classified retryable or permanent (ontology-r1 K1) | same file |
| `OwnerRefusalClasses`: every `RecordOwnerRefusal` code, classified (review round 1, ontology-r1 F1) | same file |
| `RecordProcedureDecisionJobEvents` (EventIds 50323-50329) and `RecordProcedureDecisionRepairReason` (metric tags) | same file |
| Registration: the enqueuer (singleton) and the handler (scoped), beside the gate job's pair | `Infrastructure/DI/SignalsModule.cs` |

With the enqueuer registered, `ProcedureAssignmentService` (which takes it as an optional constructor argument since 084h-2) answers `Queued` instead of `NotRecorded` when the inline record fails after the change committed.

## How one repair runs

1. **Enqueue (in the request that saw the failure).** The payload is the `RecordProcedureDecision` the inline write was given, unchanged: Values, outcome, decider, role, decided-on, reason and note (design §7.3). The idempotency key is the writer's own `RecordProcedureDecisionKey.Of(core table, record, decision id)` (at most 106 characters, under the 128-character MessageId limit). The correlation id is the decision id, which the service's failure log (`RecordFailedQueued`, 50320) also names. A send that fails or takes more than 5 s throws, so the service answers `NotRecorded`; the enqueuer meters it once.
2. **Handler.** With `Ebilling:Enabled` off it writes nothing and poisons the job (below). Otherwise it reads the payload, takes a processing lock under the key (owned by the job id, as the gate job does), and calls `BillingDecisionRecorder.RecordProcedureAsync`, the same entry the inline write called. The request is therefore built by the same `ToRequest`, so its key and hash are the inline ones. It makes **no read of `sprk_procedureassignment`**.
3. **Outcome** (as changed by review round 1). The payload is the only copy of the user's reason and note, so no path that writes nothing completes the message.
   - Written, or already recorded (the inline create had landed): `Success`.
   - The processing lock is held: `Failure` (redelivered). A stale lock of this job's own earlier attempt is taken over once it is older than `IdempotencyService.OwnerTakeoverAge`; a genuinely concurrent copy answers AlreadyRecorded on its next delivery after the other finishes.
   - A retryable refusal or an exception that is not a refusal: `Failure`. `ServiceBusJobProcessor` abandons it, the broker redelivers it, and it is dead-lettered after the last delivery. The log is Warning, or Error on the last delivery; an unexpected exception's line carries its type and stack trace.
   - A permanent refusal: logged at Error (ids, reason code, owner refusal code), metered under `decision_record_procedure_repair_abandoned`, and **`Poisoned`** (dead-lettered at once, never retried; the message is kept).
   - An unreadable payload: `Poisoned`, metered under `decision_record_procedure_repair_payload_invalid`.
   - `Ebilling:Enabled` off (ADR-018): nothing is read or written, Warning log, metered under `decision_record_procedure_repair_disabled`, and `Poisoned`, so the message is kept in the dead-letter queue for replay once eBilling is on.

## Refusal classification (ontology-r1 K1)

Every constant of `DecisionRecordRefusalReason` is in `RecordProcedureDecisionJobHandler.RefusalClasses`. A reflection test fails when a reason is added to the writer and not here. At run time an unlisted reason is treated as retryable, so a message is never dropped.

| Reason | Class | Why |
|---|---|---|
| `RecordProcedureLookupFailed` | Retryable | The lookup, the writer identity or the decider's contact read faulted |
| `FilingFailed` | Retryable | The record-type read faulted |
| `OwnerResolutionFailed` | Retryable | The owner could not be read |
| `CreateFailed` | Retryable | A create that in fact landed is AlreadyRecorded on the next delivery |
| `DataverseAccessDenied` | Retryable | An administrator can grant the privilege |
| `ReviewInvalid` | Permanent | The request is invalid, or the key already holds other content |
| `FactSnapshotMissing` | Permanent | The request is invalid |
| `RecordClassUnderivable` | Permanent | Not a per-record Procedure code |
| `SubjectUnsupported` | Permanent | Not a filing core table |
| `SubjectUnfileable` | Permanent | A data condition a retry does not change |
| `CoreUnsupported` | Permanent | A data condition a retry does not change |
| `OwnerMissing` | Permanent | No owner source |
| `OwnerRefused` | By its owner refusal code (table below) | The writer raises it for every resolver refusal. With no code: Retryable |
| `BillingRecordLookupFailed` | Retryable | Not raised on this path (084h-2 H-1); a lookup fault by meaning |
| `GateRecordLookupFailed`, `ConditionClearedLookupFailed` | Retryable | Not raised on this path; lookup faults |
| `GateRecordFailed`, `GateEnqueueFailed` | Retryable | Not raised on this path; faults |
| `SecureOwnerMismatch`, `ParticipantShareFailed`, `FilingReownFailed` | Retryable | Not raised on this path; after-create faults, and a retry finds the record |
| `GateSkippedNoUnitTeam`, `GateSkippedOwnerDisabled` | Permanent | Not raised on this path; skips by design |
| `ConditionClearedSubjectMissing` | Permanent | Not raised on this path; the subject is gone |

**Owner refusal codes (review round 1, ontology-r1 F1).** `OwnerRefused` is classified by `ex.OwnerRefusalCode` in `RecordProcedureDecisionJobHandler.OwnerRefusalClasses`, which holds every `RecordOwnerRefusal` constant (a reflection test fails when the resolver gains a code; at run time an unlisted or absent code is retried).

| Code | Class | Why |
|---|---|---|
| `ParentUnresolved` | Permanent | The core record does not exist (deleted after the change). The same code also covers "the parent has no owning business unit"; that case is accepted as permanent too (ontology-r1 accepts this) |
| `RecordMissing` | Retryable | The row being re-filed is missing: a re-file's code, not raised on a create, so it does not mean "deleted core" here |
| Every other code (`NoDefaultOwnerTeam`, `SecureOwnerTeamUnresolved`, `SecureBusinessUnitAmbiguous`, `SecureParentNotIsolated`, `ActingUserUnresolved`, `ParentUndetermined`, `NoOwnerSource`, and the rest) | Retryable | A configuration, an identity or a read that an administrator or time can clear, as the gate job retries them |

## Decisions

1. **The payload is `RecordProcedureDecision` itself.** 084h-2 designed it as the repair payload. A second payload record would be a second shape that could drift from the inline one; one shape is what makes J1 hold.
2. **The writer handles JSON values.** Values cross the wire as `JsonElement`s. The writer's canonical hash and its note checks already treat a JSON string and a `Guid` or `string` the same way (084h-1 fix round 1), so the repaired hash equals the inline hash. J1 proves it for all seven codes, with and without a note, and through the real service.
3. **Permanent refusals are dead-lettered** (review round 1, superseding the first build's "completed"): the payload is the only copy of the reason and note, so the gate job's `Poisoned` is copied. The Error log (with the decision id) and the `repair_abandoned` count stay.
4. **`OwnerRefused` is classified by its resolver code** (review round 1): only `ParentUnresolved` is permanent.
5. **Metric tags are the job's own,** not writer refusal reasons. The writer already meters each refusal it raises under its own reason; a second count under the same tag would double it. The job's tags count repair outcomes (abandoned, faulted, payload invalid, disabled, enqueue failed) once each.
6. **Logs carry ids, codes, exception type names and stack traces only,** never an exception object or message. A Dataverse fault's message can contain a column value, such as the reason (J5). A stack trace holds method names only.
7. **EventIds 50323-50329,** in the block 084h-2 reserved (50329 `HandlerDisabled` added in review round 1). `OntologyEventIdUniquenessGuardTests` passes.
8. **The eBilling flag is read by the handler** (ADR-018; `FinanceModule`'s flag rule) through `IOptionsMonitor<EbillingOptions>`, bound unconditionally by `FinanceModule`. `InvoiceIndexingJobHandler` completes its job when the flag is off; that is not copied here, because this payload is not reproducible (an index job can be re-queued by the next change; a lost decision cannot).

## ADR-052 Placement Justification

- **Host: the BFF, as an ADR-004 `IJobHandler` on the existing main queue.** It is queue work: one message per failed write, enqueued by the request that saw the failure. ADR-052 places queue work in the BFF under ADR-004, and a queue consumer is never a scheduled job.
- **Not an ADR-036 `IScheduledJob`.** A sweep would have to find the gaps by re-reading override rows, which design §7.3 forbids (row ids recur across episodes), and nothing else stores the gap. It would also delay every repair to the next schedule.
- **Signals.** B2: the job runs the BFF's `DecisionRecordWriter` and `BillingDecisionRecorder`. B3: low volume (only failed writes), with the same identity and release cadence. No F-signal is material: no event intake independent of the BFF, no bursty load, no multi-step orchestration. Tie-breaker: fewer moving parts.
- **Idempotent per unit of work (ADR-052, ADR-004 A1).** The unit is the decision id. The handler takes a processing lock under the writer's key before the write, and the writer's durable "already recorded" lookup is the completion marker: a second delivery answers AlreadyRecorded.
- **Precedent:** `GateDecisionRecordJob` (the gate's post-commit record).

## Component justification (root CLAUDE.md §11)

- **Existing:** `GateDecisionRecordEnqueuer` and `GateDecisionRecordJobHandler`.
- **Extension:** both are bound to the gate entry and its payload, so this job copies their shape rather than widening them.
- **Cost of doing nothing:** a post-commit write failure leaves a permanent gap in the decision history that no user can close. A repeat of the change is refused (409 or 404), and 084h-2 answers `NotRecorded`.

## Known limits

- **K-g, retry spacing; no dead-letter alert or resubmit tool (pre-existing pipeline).** The processor abandons a `Failure` with no delay and the broker redelivers it at once, up to `MaxDeliveryCount` (5), then dead-letters it (ISS-025 / #2876: the processor redelivers back to back). A Dataverse outage longer than five quick deliveries ends in the dead-letter queue. Nothing alerts on a dead-lettered message (#2848) and there is no resubmit tool; the message is kept and can be resubmitted by hand. The gate job has the same limits.
- **K-g2, a held lock dead-letters fast (review round 1, intended floor).** With `Failure` on a held lock and no abandon delay, a stale lock of this job's own earlier attempt (younger than the 1-minute owner takeover age) can burn every delivery in seconds and dead-letter the message. That is visible and replayable: a replay after the lock expires writes the record, or answers AlreadyRecorded if it is there. A genuinely concurrent duplicate can likewise dead-letter while the other copy writes the record; its replay answers AlreadyRecorded. Both are the chosen floor (never lose the decision), not a defect; spacing is ISS-025 / #2876.
- **K-h, enqueue timeout.** A send that takes more than 5 s is abandoned and the change answers `NotRecorded`, but the message may still have been sent. If it was, the repair files the record later, and the warning shown was pessimistic. It is never the other way round.
- **K-i, metric tags and double counting.** The job's tags (`repair_abandoned`, `repair_faulted`, `repair_payload_invalid`, `repair_disabled`, `procedure_enqueue_failed`) share `ontology.writer.failures` with the writer's own refusal reasons. A permanent refusal is counted twice on that instrument, once under the writer's reason and once as `repair_abandoned` (different tags, so no tag is double counted). `repair_faulted` and `repair_disabled` count each delivery, so one message can count up to five times. Dashboards should read the job's tags as deliveries, not decisions.
- **K-j, CorrelationId is the decision id,** not a request correlation id: the enqueue log and the service's failure log name the decision, which is what an operator searches for. The request trace is not carried into the job.
- **K-k, the gate job's lock-refused path (reported, not changed here).** `GateDecisionRecordJobHandler` still answers `Success` when its processing lock is refused, the flaw review F1 found here. It is owned by ontology-r1, which is fixing it; this task does not change `GateDecisionRecordJob.cs`.
- **Observation (writer, out of scope).** The writer's own refusal log (`LogRefusal`) logs the exception object, whose inner Dataverse exception message could in principle carry a column value. J5 covers the enqueuer and the handler only, and the writer is not changed by this task (POML). Not a confirmed defect: no Dataverse fault on this path is known to echo `sprk_reason`.
- **K-3 (design) stands.** A crash between the commit and the enqueue files nothing and surfaces nothing.

## ADR-004 tension: receive-side idempotency is not atomic

- **ADR:** ADR-004 (async job contract), A1: a handler is idempotent per unit of work.
- **The gap.** Three layers make one record per decision, and none is atomic: the processing lock is check-then-set and fails open on a cache fault (#984); the writer looks the record up by the decision key before it creates; and `sprk_decisionrecord` has no alternate key on the decision key, so Dataverse does not refuse a second create. Two copies that both pass the lock and both find nothing could both create.
- **Superseded: no longer a §6.5 Path A exception.** Path A was proposed here (the same exception as the gate job's ontology task 149 K2 and 084h-1 K-b). The owner did not take it (EB-D-247, question (d): build a check instead).
- **Resolved by task 084h-5: §6.5 Path C (comply).** Every keyed writer entry point (gate, condition-cleared, billing, Procedure) now creates its Decision Record with a primary id derived from its key alone, so Dataverse refuses a second create of the same decision, and the writer answers the refused copy exactly as the existing already-recorded case. The repair job's unit of work (the decision id) is therefore idempotent at the receive side, with no change to this job. Design: ontology-r1 option 1. Detail: `084h-5-decision-record-derived-id.md`. Known limit: derivable record id, see uac-r2 thread. In effect when 084h-5 merges; its merge waits on the owner and uac-r2 accepting that limit.

## Review round 1 (2026-11-17)

| # | Finding | Fix | Tests |
|---|---|---|---|
| 1 | ontology-r1 F1: `OwnerRefused` was permanent for every resolver code | Classified by `ex.OwnerRefusalCode` (`OwnerRefusalClasses`, exhaustive over `RecordOwnerRefusal`): only `ParentUnresolved` is permanent | `J4_OwnerRefused_ADeletedCoreRecord_IsPermanent_LoggedMeteredAndPoisoned_WithTheRealResolver` (kept); `J4_OwnerRefused_ForAConfigurationOrIdentityCode_IsAFailureForRedelivery_NotAbandoned_AndTheRedeliveryWritesTheRecord` (must-fire: NoDefaultOwnerTeam, SecureOwnerTeamUnresolved, ActingUserUnresolved); `F1_EveryOwnerRefusalCode_IsClassified_OnlyTheDeletedCoreCodeIsPermanent_AndTheTableNamesNoOtherCode`; `F1_Control_ACodeAddedToTheResolver_WouldBeReportedUnclassified_AndIsRetriedAtRunTime`; `F1_AnOwnerRefusal_HasTheClassOfItsCode` |
| 2 | ontology-r1 F2: a permanent refusal completed the message, losing the only copy of the reason and note | `Poisoned` (dead-lettered), Error log and `repair_abandoned` meter kept | `J4_APermanentRefusal_IsLoggedAtErrorMeteredAndPoisoned_NotRetried_AndWritesNothing` (4 shapes); the deleted-core test above |
| 3 | review F1: a refused lock answered `Success`, including a stale lock of the same job's earlier attempt | `Failure` (redelivered) | `F1_ALockLeftByAnEarlierAttemptOfTheSameJob_IsAFailure_NotASuccess_AndTheRecordIsNotLost` (must-fire); `AJobWhileAnotherJobHoldsTheSameDecision_WritesNothing_AndIsAFailureForRedelivery_ThenAnswersAlreadyRecorded`; `F1_MustNotFire_TheNormalPath_TakesTheLockAndReleasesIt` |
| 4 | review F2 (ADR-018): the handler ignored `Ebilling:Enabled` | Off: nothing read or written, Warning (`HandlerDisabled`, 50329), `repair_disabled` meter, `Poisoned` (kept for replay) | `F2_WithEbillingDisabled_TheJobWritesNothing_AndIsPoisoned_SoTheMessageIsKept_LoggedAndMetered` (must-fire); `F2_MustNotFire_WithEbillingEnabled_TheJobWritesTheRecord`; DI: `TheRealContainer_WiresTheEnqueuerIntoTheProcedureService_AndRoutesItsJobTypeToTheRepairHandler` resolves the handler with its options |
| 5 | review K: real-service coverage was assign-procedure only; Values were built by hand | Seam tests through the REAL `ProcedureAssignmentService` for all seven acts | `EachAct_AFailedInlineRecord_IsRepairedByTheJob_WithTheInlineStepsAndHash` (7 acts: repaired `sprk_steps` and hash equal the inline attempt's); `EachAct_BuildsValuesOfGuidsAndStringsOnly` (7 acts); `Control_TheValuesTypeCheck_RefusesADateANumberAndNull` |
| 6 | review K/F2 low: an unexpected exception was logged by type name only | Its stack trace (method names) is logged too; never the exception object or message | `J4_AnExceptionThatIsNotARefusal_IsAFailureForRedelivery_AndMetered` (asserts the trace); `J5_NoLogLineOrJobOutcome_FromTheEnqueuerOrTheHandler_CarriesTheReasonOrTheNoteText` (now also covers the held-lock and disabled lines and the stack-trace field) |

**Fail-before (mutation) checks, round 1.** Each mutant was applied to the fixed source, the tests were run, and the source was restored from a copy:

| Mutant | Tests that failed |
|---|---|
| R1: `OwnerRefused` permanent for every code, and one code dropped from `OwnerRefusalClasses` | 5: the three must-fire owner-code cases, the completeness test and its control |
| R2: a permanent refusal answers `Success` | 5: the four permanent shapes and the deleted-core test |
| R3: a refused lock answers `Success` | 2: the stale-lock must-fire and the concurrent-copy test |
| R4: the flag check disabled | 2: the flag-off must-fire and J5 |
| R5 (service): a date value added to every act's Values | 18: all seven Guid-or-string cases, all seven per-act repair cases, the three J1 slice tables and J2 (a date does not survive the payload round trip: the hazard the pin guards) |
| R6: the stack trace dropped from the log | 2: the exception test and J5 |

**Gates, round 1.** `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror --no-incremental`: 0 warnings, 0 errors. New and changed tests (`~RecordProcedureDecisionJobTests`, `~RecordProcedureRepairSeamTests`): 88 pass. `Sprk.Bff.Api.Tests` filtered `~Signals | ~Finance | ~Procedure`: 13,641 pass, 10 skipped, 0 failed. `Spaarke.ArchTests`: 2,656 pass, 0 failed. No package added.

## Deviations from the POML

- **Test location.** The POML names `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/RecordProcedureDecisionJobTests.cs`, which is not a KEEP path (tests/CLAUDE.md: new files go under a KEEP path). The tests are in:
  - `tests/integration/data-mutation/Signals/RecordProcedureDecisionJobTests.cs` (the job writes a Decision Record): J1-J5, K1, the enqueuer and the handler's other outcomes;
  - `tests/integration/seam/Signals/RecordProcedureRepairSeamTests.cs`: the slice from the Procedure service's enqueue through the Service Bus wire and the processor's routing rule to the record, plus the real host composition.
- **Review round 1 changes two POML outcomes.** The POML's J4 completes a permanent refusal and its K1 constraint makes `OwnerRefused` permanent. Both are superseded by the round-1 findings (ontology-r1 F1, F2): a permanent refusal is now `Poisoned`, and `OwnerRefused` is permanent only for `ParentUnresolved`.

## Tests (first build, before review round 1: counts and outcomes superseded where round 1 changed them; see "Review round 1")

| Criterion | Proven by |
|---|---|
| J1 | `J1_TheRepairedRecord_HashesAsTheInlineRequest_AndCarriesItsContent_ForEachCodeWithAndWithoutANote` (7 codes × note/no note: the stored hash equals `RecordProcedureDecisionHash` of the inline request, and the stored values and hash equal an inline write's); `AFailedInlineRecord_IsQueuedToTheRepairJob_WhichFilesTheInlineRecord` (matter, project, work assignment: through the real service, the repaired `sprk_steps` equal the failed inline attempt's, byte for byte) |
| J2 | `J2_WhenTheInlineCreateLandedButTimedOut_TheJobAnswersAlreadyRecorded_WithZeroCreates`; slice `AnInlineCreateThatLanded_IsAlreadyRecordedByTheRepair_WithZeroCreates` |
| J3 | `J3_TheJobReadsNoOverrideRow_AndARowWithTheSameIdAndOtherContent_ChangesNothingRecorded`, with the detector's must-fire control `J3_Control_TheReadDetector_FiresOnAnOverrideRowRead` |
| J4 | `J4_ARetryableRefusal_IsAFailureForRedelivery_WritesNothing_AndTheRedeliveryWritesTheRecord` (lookup, writer identity, decider contact, owner read, create); `J4_ARetryableRefusalOnTheLastDelivery_...`; `J4_APermanentRefusal_IsLoggedAtErrorMeteredAndPoisoned_NotRetried_AndWritesNothing` (ReviewInvalid ×2, SubjectUnsupported, RecordClassUnderivable); `J4_OwnerRefused_ADeletedCoreRecord_IsPermanent_...` (real resolver); `J4_OwnerRefused_ForAConfigurationOrIdentityCode_...`; `J4_AnExceptionThatIsNotARefusal_...` (see "Review round 1") |
| J5 | `J5_NoLogLineOrJobOutcome_FromTheEnqueuerOrTheHandler_CarriesTheReasonOrTheNoteText` (written, already recorded, retried, faulted, abandoned, queued, enqueue failed, enqueue timed out; messages, structured fields, exceptions and job outcomes) |
| K1 | `K1_EveryWriterRefusalReason_IsClassifiedRetryableOrPermanent_AndTheTableNamesNoOtherReason` (reflection over the enum's constants), `K1_Control_AReasonAddedToTheWriter_WouldBeReportedUnclassified_AndIsRetriedAtRunTime`, `K1_TheNamedRefusals_HaveTheirAgreedClass` |
| Enqueue reaches the handler; registration | `AFailedInlineRecord_IsQueuedToTheRepairJob_...` (the service's real enqueue, through the wire, routed by the processor's rule to exactly this handler); `TheRealContainer_WiresTheEnqueuerIntoTheProcedureService_AndRoutesItsJobTypeToTheRepairHandler` (real host composition, scope validation on) |

## Verification (first build; round 1 and the merged tip are in "Review round 1" and "Merge")

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror --no-incremental` | 0 warnings, 0 errors |
| New tests (`~RecordProcedureDecisionJobTests`, `~RecordProcedureRepairSeamTests`) | 56 pass (51 + 5) |
| `Sprk.Bff.Api.Tests` filtered `~Signals \| ~Finance \| ~Procedure` (includes the new tests) | 13,609 pass, 10 skipped, 0 failed |
| `Spaarke.ArchTests` (whole project, including the EventId uniqueness guard) | 2,656 pass, 0 failed |
| Full `Sprk.Bff.Api.Tests` | Not run in one process (it times out on this machine); the filtered run above covers every suite the change touches |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages. No package was added. |

**Fail-before (mutation) checks.** Each mutant was applied to the committed source, the new tests were run, and the source was restored with `git checkout`:

| Mutant | Tests that failed |
|---|---|
| M1: the two `SignalsModule` registrations removed (084h-2's state: no enqueuer) | the real-container registration test (1) |
| M2: the payload drops the note (the "omit the text" alternative design §7.3 rejected) | 19, including J1 with a note (7 codes), the J1 seam slice (3 tables), J2 and the enqueuer payload test |
| M3 + M4: `OwnerRefused` classified retryable, and `CoreUnsupported` removed from the table | 4: the K1 named-class test, the K1 completeness test, its control, and the real-resolver OwnerRefused test |
| M5 + M6 + M7: permanent answers Failure, retryable answers Success, and the completion log carries the reason | 12: J5, every permanent J4 case (including OwnerRefused), every retryable J4 case and the last-delivery case |

**Publish size (root CLAUDE.md §10, NFR-01).** `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <folder>`, framework-dependent linux-x64 (the csproj RID), Release, `Compress-Archive -CompressionLevel Optimal` over the publish output, PDBs **included** (4). Both trees were fresh `git archive` exports of `src/server` plus `config/secure-record-owner-role.json` (embedded by the csproj), published the same way into a temporary folder inside the worktree, which was then deleted.

| Tree | Uncompressed | Compressed |
|---|---|---|
| Base 5508b576fe | 150,770,305 bytes | 52,708,592 bytes (50.27 MiB) |
| HEAD | 150,788,277 bytes | 52,717,735 bytes (50.28 MiB) |
| **Delta** | **+17,972 bytes** | **+9,143 bytes (~+9 KB)** |

Well under the +5 MB justification trigger and the 55 MB review / 60 MB stop thresholds. The base is the work-branch tip named in the brief, not a fresh `origin/master` build: the change is one new source file and a registration, and the work branch carries other unmerged code, so a master comparison would measure the branch, not this task.

BFF Hygiene §10 + NFR-01 verified: compressed publish size 52.72 MB (50.28 MiB) incl. PDBs, delta +9 KB, no new HIGH CVEs.

## Re-check and merge (2026-11-17)

- **ontology-r1 re-check of F1/F2 at 36b3f9004f: PASS** (ontology sign-off; RecordMissing retryable agreed).
- **Scoped Sonnet re-check of the fix diff b1d31707d5..36b3f9004f:** all six fixes verified; no F-class finding. Two known limits:
  - K4 (doc): the first-build "Tests" and "Verification" figures predate round 1 (labelled above).
  - K1: a permanent owner refusal is dead-lettered with the description `decision_owner_refused` only. The owner refusal code is in the Error log, not on the message, so a dead-letter triage joins to logs.
- **Merged into work/ebilling-r1 as bb4ad3f578.** On the merged tip: BFF build `--warnaserror` 0 warnings; the 084h-3, Procedure assignment, billing recorder and gate job suites 661/0.
- **ADR-004 tension above: resolved by task 084h-5** (§6.5 Path C, comply; EB-D-247). The Path A exception first proposed here was not taken.
