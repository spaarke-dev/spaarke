# 084h-2: each per-record Procedure change files its Decision Record

> **Task:** `tasks/084h-2-procedure-change-records-service.poml` (FULL) · **Design:** `084h-procedure-change-decision-records-design.md` (§16 and §17 override earlier sections) · **Base:** `task/ebilling-084h-2` from 69745e926d · **Status:** built and tested. Code-review, adr-check and the adversarial pass are for the coordinator.

## What was built

| Item | Where |
|---|---|
| `RecordProcedureDecision`, `IRecordProcedureDecisionEnqueuer` (implemented by 084h-3), `RecordProcedureAsync`, `PreflightProcedure` and `ToRequest` | `Services/Finance/BillingDecisionRecorder.cs` |
| Note check, minted decision id, preflight, record after commit, enqueue on failure, Unknown read-back | `Services/Finance/Procedures/ProcedureAssignmentService.cs` |
| Decider resolution (403 before the service), the optional JSON body on DELETE, and the note 400 codes | `Api/Finance/ProcedureAssignmentEndpoints.cs` |

## Order of one change

1. The note check (`InvoiceCorrectionNote.Check`) runs before any read.
2. Mint the decision id.
3. Run the preflight. A refusal gives 500 `assignment.decision_unrecordable` and writes nothing.
4. The transaction.
5. On Done, the record runs last, on `CancellationToken.None`.

When the record fails, the decision is enqueued unchanged and the answer is `Queued`. If the enqueue also fails, the answer is `NotRecorded` with `assignment.decision_record_failed`. The change itself stays 201/200 either way.

For change-rule, the preflight runs after stages 1 and 2 and before stage 3.

## Decisions

1. **H-1.** The recorder passes `RecordProcedureLookupFailed` as the decider-contact refusal reason, never the billing reason.
2. **H-2.** The recorded reason is the row's reason. It is trimmed, and a blank reason is recorded as none (`Trimmed` at row build; `Decision` also maps blank to null). The repair job carries the same text, so the hashes agree. Test: `RecordedReason_IsTrimmed_AndBlankIsNone`.
3. **H-3.** The service enforces the 2000-character cap and the control-character check itself, not only the route. This covers the three POST bodies and the DELETE body. A 2001-character note or `\u0007` is refused (400) before any read (zero queries, zero transactions) for each of the seven acts, including unpin and remove. Tests: the S6/H-3 theory, seven acts × two bad notes, plus seven acts × three good notes.
4. **The enqueuer is optional in DI.** The public constructor takes `IRecordProcedureDecisionEnqueuer? enqueuer = null`. With none registered, a private `NoEnqueuer` returns a faulted task, so a failed record answers `NotRecorded` until 084h-3 registers the real one. There is no `FinanceModule` edit and no registration, because DI builds without one.
5. **EventIds:**
   - 50320 `RecordFailedQueued`.
   - 50321 `RecordFailedNotQueued`.
   - 50322 `RecordUnproven`.
   - 50323-50329 are reserved for 084h-3.

   Each line logs ids and exception type names only, never the note or the reason.
6. **Unknown read-back (S3).** The service reads the row back and compares mode, procedure, pinned version, excluded policy, firm, reason and the core lookup with the built row. It also requires the row's creator person to equal the decider. Only an exact match records. A mismatch, no row or a read fault records nothing and the 500 stays. The escalation trigger did not fire, because the creator person is stamped on the row.
7. **change-rule on Unknown.** The commit is proven by `CommittedAfterNoAnswerAsync`, and then the Exclude row is read back the same way. If that row is not proven the caller's, the change answers Done with `decisionRecord` `NotRecorded`, logs `RecordUnproven` and files no record.
8. **Remove.**
   - A Pinned row with a version files `unpin-procedure` (procedure, version, override; outcome Unpinned).
   - Every other row files `remove-override` (procedure, rule, firm, override, kind; outcome Removed).
   - Both carry a null reason (S9).
9. **403 shape.** An unresolved decider gives `ProblemDetailsHelper.Forbidden`, whose extension key is `reasonCode` (the same idiom as the reject and return endpoints), not `errorCode`.

## Known limits

- **K-1.** A DELETE with no answer (Unknown) files no record.
- **K-3, K-4.** As in design §16 and §17.
- The dry-run preview does not validate the note. It writes nothing and records nothing.
- An unproven change-rule answers `NotRecorded`, with no repair. Nothing is enqueued, because the row is not proven the caller's.

## Deviations from the POML

- **Mechanical compile fixes** in the contract and data-mutation tests that construct the service or mock the endpoints with the new signatures:
  - `RecordProcedureDryRunParityContractTests`
  - `ProcedureDryRunPlanningContractTests`
  - `RecordAssignableProceduresContractTests`
  - `RecordProcedureDryRunContractTests`
  - `RecordWritePermissionsContractTests`
  - `ProcedureAssignmentServiceTests.FirmDescription.cs`

  These files are outside the POML's named test list, but the build fails without them. Assertions are unchanged.
- **`ProcedureDecisionRecords` helper** in `RecordProcedureWriteRig.cs` (`Writing(...)` and `NeverRecording`), so the contract worlds build the recorder in one place.
- **Two existing 089c asserts** compared whole `OverrideRemoved` values. They now include the new `DecisionRecord` member and assert `Recorded`.

## Verification

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror --no-incremental` | 0 warnings, 0 errors |
| `ProcedureAssignmentServiceTests` | 236 pass |
| `ProcedureAssignmentEndpointsTests` | 283 pass |
| `BillingDecisionRecorderTests` | 28 pass |
| Procedure, contract and data-mutation filter (`~Procedure`, `~RecordWritePermissions`, `~FirmDescription`) | 2420 pass |
| `Spaarke.ArchTests` (all, including DecisionRecordContextImmutability, RecordOwnerAssignmentCensus, DryRunNoWriteGuard and OntologyEventIdUniqueness) | 2650 pass |
| Full `Sprk.Bff.Api.Tests` | Not completed. The one full run overlapped three Release publishes, and about 30 tests failed in unrelated areas (Compose, Office, Insights, External Access, Documents). It was stopped. The 26 failing classes then passed alone: 325 / 325. No failure touched a Finance or Procedure class. |
| H-2 fail-before | `RecordedReason_IsTrimmed_AndBlankIsNone` fails when the exclude-rule row keeps the untrimmed reason, and passes restored. |
| Fail-before | Two mutation batches each failed exactly the targeted tests, and the sources were restored after each. Batch A: note check, read-back, pinned column, enqueue, unproven, endpoint decider, H-1. Batch B: creator check, reason column, preflight. |
| Publish size (Release) | +42 KB against the branch base 69745e926d (140,392,033 → 140,434,245 bytes). Against a fresh `origin/master` (57431c8e6a), it is -62 KB, because master carries later code. |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages |

## Rework

None after the build; fixes during the build were test-only (see the commit).

## Review (2026-11-16): one Opus adversarial pass

Hunt items (a), (b), (d), (e), (f) and (i) are clean. ontology-r1's H-3 glance passed at e3127757fa.

**Fixed (F4):** `ProcedureAssignmentEndpointsTests` DELETE note-body test used `seen?.Note`, which skipped the assertion when the body failed to bind. It now asserts that the request is non-null and that the note equals the sent text when a body is sent. Fixed on work after the merge.

**Known limits:**
- **K-a, change-rule preflight after stages 1-2.** If the preflight refuses (only a service bug can cause that), the record-scoped rule set and an Off policy row already exist, and the answer says nothing was changed. The leftover rows have no effect, and a retry reuses them.
- **K-b, same-caller double submit.** Two identical requests: A commits and records; B's transaction gives no answer, and B's read-back finds A's row (same creator, same reason), so B files a second record. Nothing on the row tells the attempts apart. Earlier-episode rows cannot match, because the duplicate check refuses before the send.
- **K-c, a create that commits after its read-back.** A timed-out transaction that commits later leaves the change with no record and no repair (the answer was 500 "may or may not"). This extends design K-1 from DELETE to creates.
- **K-d, an unproven change-rule.** The commit is proven, but the Exclude-row read-back faults: the answer is NotRecorded with nothing enqueued, logged once as `RecordUnproven`.
- **K-e, 403 shape.** The decider 403 puts its code under `reasonCode` (as the reject and return routes do). RecordProcedures reads `errorCode`, so it shows the title and detail. A transient WhoAmI or OBO fault answers 403, not a retryable 503 (same idiom as reject and return, design §6.1).
- **K-f, coverage.** S10 (`decisionRecord` in the answer) is proven at the endpoint level with a mocked service result; the contract rig captures records but no contract test asserts the member.
