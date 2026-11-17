# 084h-3: the repair job for a per-record Procedure Decision Record

> **Task:** `tasks/084h-3-procedure-change-record-repair-job.poml` (FULL) · **Design:** `084h-procedure-change-decision-records-design.md` §7.3, as settled by §16 items 3-4 and §17 (EB-D-239 Q10) · **Base:** `task/ebilling-084h-3` from 5508b576fe · **Status:** built and tested. Code-review, adr-check and the adversarial pass are for the coordinator.

## What was built

| Item | Where |
|---|---|
| `RecordProcedureDecisionEnqueuer`: implements 084h-2's `IRecordProcedureDecisionEnqueuer` as an ADR-004 job | `Services/Signals/RecordProcedureDecisionJob.cs` |
| `RecordProcedureDecisionJobHandler : IJobHandler` (job type `RecordProcedureDecision`) | same file |
| `RefusalClasses`: every `DecisionRecordRefusalReason`, classified retryable or permanent (ontology-r1 K1) | same file |
| `RecordProcedureDecisionJobEvents` (EventIds 50323-50328) and `RecordProcedureDecisionRepairReason` (metric tags) | same file |
| Registration: the enqueuer (singleton) and the handler (scoped), beside the gate job's pair | `Infrastructure/DI/SignalsModule.cs` |

With the enqueuer registered, `ProcedureAssignmentService` (which takes it as an optional constructor argument since 084h-2) answers `Queued` instead of `NotRecorded` when the inline record fails after the change committed.

## How one repair runs

1. **Enqueue (in the request that saw the failure).** The payload is the `RecordProcedureDecision` the inline write was given, unchanged: Values, outcome, decider, role, decided-on, reason and note (design §7.3). The idempotency key is the writer's own `RecordProcedureDecisionKey.Of(core table, record, decision id)` (at most 106 characters, under the 128-character MessageId limit). The correlation id is the decision id, which the service's failure log (`RecordFailedQueued`, 50320) also names. A send that fails or takes more than 5 s throws, so the service answers `NotRecorded`; the enqueuer meters it once.
2. **Handler.** It reads the payload, takes a processing lock under the key (owned by the job id, as the gate job does), and calls `BillingDecisionRecorder.RecordProcedureAsync`, the same entry the inline write called. The request is therefore built by the same `ToRequest`, so its key and hash are the inline ones. It makes **no read of `sprk_procedureassignment`**.
3. **Outcome.**
   - Written, already recorded (the inline create had landed), or another job holds the lock: `Success`.
   - A retryable refusal or an exception that is not a refusal: `Failure`. `ServiceBusJobProcessor` abandons it, the broker redelivers it, and it is dead-lettered after the last delivery. The log is Warning, or Error on the last delivery.
   - A permanent refusal: logged at Error (ids, reason code, owner refusal code), metered under `decision_record_procedure_repair_abandoned`, and **completed**, never retried.
   - An unreadable payload: `Poisoned` (dead-lettered at once, so the message is kept), metered under `decision_record_procedure_repair_payload_invalid`.

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
| `OwnerRefused` | Permanent | For example, the core record was deleted after the change (POML; tested with the real resolver) |
| `BillingRecordLookupFailed` | Retryable | Not raised on this path (084h-2 H-1); a lookup fault by meaning |
| `GateRecordLookupFailed`, `ConditionClearedLookupFailed` | Retryable | Not raised on this path; lookup faults |
| `GateRecordFailed`, `GateEnqueueFailed` | Retryable | Not raised on this path; faults |
| `SecureOwnerMismatch`, `ParticipantShareFailed`, `FilingReownFailed` | Retryable | Not raised on this path; after-create faults, and a retry finds the record |
| `GateSkippedNoUnitTeam`, `GateSkippedOwnerDisabled` | Permanent | Not raised on this path; skips by design |
| `ConditionClearedSubjectMissing` | Permanent | Not raised on this path; the subject is gone |

## Decisions

1. **The payload is `RecordProcedureDecision` itself.** 084h-2 designed it as the repair payload. A second payload record would be a second shape that could drift from the inline one; one shape is what makes J1 hold.
2. **The writer handles JSON values.** Values cross the wire as `JsonElement`s. The writer's canonical hash and its note checks already treat a JSON string and a `Guid` or `string` the same way (084h-1 fix round 1), so the repaired hash equals the inline hash. J1 proves it for all seven codes, with and without a note, and through the real service.
3. **Permanent refusals are completed, not dead-lettered** (POML J4). The gate job poisons them instead. The Error log (with the decision id) and the `repair_abandoned` count are the trace.
4. **`OwnerRefused` is permanent** (POML K1 constraint). The gate job treats it as retryable. See known limit K-j.
5. **Metric tags are the job's own,** not writer refusal reasons. The writer already meters each refusal it raises under its own reason; a second count under the same tag would double it. The job's tags count repair outcomes (abandoned, faulted, payload invalid, enqueue failed) once each.
6. **Logs carry ids, codes and exception type names only,** never an exception object. A Dataverse fault's message can contain a column value, such as the reason (J5).
7. **EventIds 50323-50328,** in the block 084h-2 reserved. `OntologyEventIdUniquenessGuardTests` passes.

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

- **K-g, retry spacing.** The processor abandons a `Failure` and the broker redelivers it at once, up to `MaxDeliveryCount` (5), then dead-letters it. A Dataverse outage longer than five quick deliveries ends in the dead-letter queue. The gate job has the same limit. The message is kept, so it can be resubmitted.
- **K-h, enqueue timeout.** A send that takes more than 5 s is abandoned and the change answers `NotRecorded`, but the message may still have been sent. If it was, the repair files the record later, and the warning shown was pessimistic. It is never the other way round.
- **K-i, permanent refusals leave no message.** They are completed (POML J4), so the only trace is the Error log and the `repair_abandoned` count, both with the decision id.
- **K-j, `OwnerRefused` covers configuration refusals too.** Besides a deleted core record, the resolver can refuse because a business unit has no default owner team or the Secure Record team is unresolved. An administrator could fix those, but the job completes them as permanent, as the POML requires.
- **Observation (writer, out of scope).** The writer's own refusal log (`LogRefusal`) logs the exception object, whose inner Dataverse exception message could in principle carry a column value. J5 covers the enqueuer and the handler only, and the writer is not changed by this task (POML). Not a confirmed defect: no Dataverse fault on this path is known to echo `sprk_reason`.
- **K-3 (design) stands.** A crash between the commit and the enqueue files nothing and surfaces nothing.

## Deviations from the POML

- **Test location.** The POML names `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/RecordProcedureDecisionJobTests.cs`, which is not a KEEP path (tests/CLAUDE.md: new files go under a KEEP path). The tests are in:
  - `tests/integration/data-mutation/Signals/RecordProcedureDecisionJobTests.cs` (the job writes a Decision Record): J1-J5, K1, the enqueuer and the handler's other outcomes;
  - `tests/integration/seam/Signals/RecordProcedureRepairSeamTests.cs`: the slice from the Procedure service's enqueue through the Service Bus wire and the processor's routing rule to the record, plus the real host composition.

## Tests

| Criterion | Proven by |
|---|---|
| J1 | `J1_TheRepairedRecord_HashesAsTheInlineRequest_AndCarriesItsContent_ForEachCodeWithAndWithoutANote` (7 codes × note/no note: the stored hash equals `RecordProcedureDecisionHash` of the inline request, and the stored values and hash equal an inline write's); `AFailedInlineRecord_IsQueuedToTheRepairJob_WhichFilesTheInlineRecord` (matter, project, work assignment: through the real service, the repaired `sprk_steps` equal the failed inline attempt's, byte for byte) |
| J2 | `J2_WhenTheInlineCreateLandedButTimedOut_TheJobAnswersAlreadyRecorded_WithZeroCreates`; slice `AnInlineCreateThatLanded_IsAlreadyRecordedByTheRepair_WithZeroCreates` |
| J3 | `J3_TheJobReadsNoOverrideRow_AndARowWithTheSameIdAndOtherContent_ChangesNothingRecorded`, with the detector's must-fire control `J3_Control_TheReadDetector_FiresOnAnOverrideRowRead` |
| J4 | `J4_ARetryableRefusal_IsAFailureForRedelivery_WritesNothing_AndTheRedeliveryWritesTheRecord` (lookup, writer identity, decider contact, owner read, create); `J4_ARetryableRefusalOnTheLastDelivery_...`; `J4_APermanentRefusal_IsLoggedAtErrorMeteredAndCompleted_NotRetried_AndWritesNothing` (ReviewInvalid ×2, SubjectUnsupported, RecordClassUnderivable); `J4_OwnerRefused_ADeletedCoreRecord_IsPermanent_...` (real resolver); `J4_AnExceptionThatIsNotARefusal_...` |
| J5 | `J5_NoLogLineOrJobOutcome_FromTheEnqueuerOrTheHandler_CarriesTheReasonOrTheNoteText` (written, already recorded, retried, faulted, abandoned, queued, enqueue failed, enqueue timed out; messages, structured fields, exceptions and job outcomes) |
| K1 | `K1_EveryWriterRefusalReason_IsClassifiedRetryableOrPermanent_AndTheTableNamesNoOtherReason` (reflection over the enum's constants), `K1_Control_AReasonAddedToTheWriter_WouldBeReportedUnclassified_AndIsRetriedAtRunTime`, `K1_TheNamedRefusals_HaveTheirAgreedClass` |
| Enqueue reaches the handler; registration | `AFailedInlineRecord_IsQueuedToTheRepairJob_...` (the service's real enqueue, through the wire, routed by the processor's rule to exactly this handler); `TheRealContainer_WiresTheEnqueuerIntoTheProcedureService_AndRoutesItsJobTypeToTheRepairHandler` (real host composition, scope validation on) |

## Verification

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
