# Task 223 notes: every Signals lookup that trusts a Decision Record as the writer's uses WriterCreatedRecord (#2632, DI-144)

## Finding
ebilling-r1 task 165b-3 (EB-D-208) added `Services/Signals/WriterCreatedRecord.cs`, the one rule for "a row the BFF writer identity created":
`createdby` = the writer AND `createdonbehalfby` empty. Its guard (`tests/Spaarke.ArchTests/WriterCreatedRecordSingleSourceGuardTests.cs`)
baselined the ontology's six remaining createdby-only conditions; a seventh site compared `createdby` in memory, invisible to the scan.
Defence-in-depth, not a reachable defect: the writer never impersonates (dev: 96 of 96 Decision Records with `createdonbehalfby` empty,
checked 2026-11-07 by #2632) and no Spaarke role grants the delegate privilege. Base: origin/master `1b89025736`.

## Sites (by name; what changed)
| Family | Site | Change |
|---|---|---|
| Commit review-id lookup | `DecisionCommitService.FindRecordAsync` | `WriterCreatedRecord.AddConditions(query.Criteria, writer)` |
| Gate record lookup | `DecisionRecordWriter.FindGateRecordAsync` | same |
| Condition-cleared record lookup | `DecisionRecordWriter.FindConditionClearedRecordAsync` | same |
| Billing writer filter | `DecisionRecordWriter.AddWriterFilterAsync` (body of `AddBillingWriterFilterAsync`; also the Procedure and many-subject lookups) | same; `sprk_actioncode In` follows it |
| Re-evaluation prior-link read | `SignalReevaluationJob.RetriesReplacingAPriorLinkAsync` | same |
| Read route | `SignalReadService` Decision Record FetchXML | `WriterCreatedRecord.FetchXmlFilter(writer)` nested in the outer `and` filter |
| Gate sharing (in memory) | `GateDecisionRecordSharing.FindGateRecordsAsync` | `createdonbehalfby` added to the ColumnSet; the predicate also requires it empty. Kept local (one in-memory site) with a comment citing `WriterCreatedRecord`; no change to `WriterCreatedRecord.cs` (ebilling's file), so no §11 answers are needed |

Every other condition, its order, `TopCount`, `NoLock` and the error handling are unchanged. The writer id is never empty
(`OntologyWriterDataverseClient.GetWriterUserIdAsync` throws on an empty WhoAmI), so the helper's `Guid.Empty` refusal changes nothing on a
real path; a WhoAmI fault still lands in each caller's existing catch (refused / Unavailable / propagated), never "no record".

Guard: `Baseline` is now empty (the dictionary and ratchet tests stay for a future entry). Remarks updated for the in-memory site.

## Tests (failing first)
One test per family; each seeds a row with `createdby` = the writer and `createdonbehalfby` = another identity, which must not be trusted.
Harnesses that evaluate queries now also evaluate `createdonbehalfby Null` (the commit rig, the re-evaluation sysadmin fake, the read-route
FetchXML fake); the gate-sharing world gained an opt-in ColumnSet projection so dropping the column from the ColumnSet also fails the test.

| Family | Test |
|---|---|
| Commit | `DecisionCommitServiceTests.FindRecord_ARowCreatedOnTheWritersBehalfByAnotherIdentity_IsNotAnEarlierRun_TheFullPathRuns` |
| Gate | `DecisionRecordWriterTests.Gate_ARowCreatedOnTheWritersBehalfWithTheKey_IsNotTheGatesRecord_AndTheRecordIsWritten` |
| Condition-cleared | `DecisionRecordWriterTests.ConditionCleared_ARowCreatedOnTheWritersBehalfWithTheKeyAndCode_IsIgnored_AndItsConfirmerIsNotCopied` |
| Billing | `DecisionRecordWriterTests.Billing_ARowCreatedOnTheWritersBehalfWithTheKeyAndCode_IsNotAlreadyRecorded_AndTheRecordIsWritten` |
| Re-evaluation | `SignalReevaluationJobTests.Run_AClosuresRetryWhosePriorLinkWasCreatedOnTheWritersBehalf_IsNotReplaced_AndTheSkipIsLogged` |
| Read route | `SignalReadRouteTests.ADecisionRecordCreatedOnTheWritersBehalf_IsOmitted_AndTheWritersOwnIsStillShown` |
| Gate sharing | `GateDecisionRecordSharingTests.ARecordCreatedOnTheWritersBehalf_IsNotTheWritersOwn_AndIsNeverMirrored` |

Proof: all seven were run against the unmodified source (tests and harness edits only) and each failed on its behavioural assertion
(`AlreadyRecorded`/`Replayed` true, the on-behalf record shared, the retry writing, the decision shown); after the fix all pass.
Must-not-fire: every existing lookup test seeds writer rows with `createdonbehalfby` absent and still finds them; the read-route and
gate-sharing tests also assert the writer's own row is still found in the same run.

Deviation from "existing tests pass unmodified" (each is a test that pinned the old query shape or depended on the old inconsistency):
- `DecisionRecordWriterTests.GateIdempotency.cs`: pinned the gate lookup's exact condition count (`HaveCount(4)`); now asserts the
  `createdonbehalfby Null` condition and `HaveCount(5)`.
- `DecisionRecordWriterTests.RecordProcedure.cs` (ebilling 084h-1) `RecordProcedure_W4_...`: pinned the exact condition list of the
  Procedure and billing lookups; both lists now include `createdonbehalfby Null` after `createdby Equal`.
- `InvoiceApprovalCompletionTests.K3_AlreadyRecordedWithNoWriterRecordFound_IsRefused` (ebilling 165b-3): reached the
  `record_not_writer_created` refusal by seeding a row created on the writer's behalf, which the writer's lookup counted and the
  completion's lookup did not. This task removes that split (both use `WriterCreatedRecord`), so the test now reaches the same branch the
  way it can still happen: a writer record that only the writer's keyed lookup sees (it landed after the completion's read). Assertions
  unchanged. **ebilling review requested.**
- The condition-cleared key-lookup fake in `SignalReevaluationJobTests.cs` answered any operator other than Equal/Like with "no match"; it now
  evaluates `Null` as Dataverse does (three existing re-evaluation tests depended on it).

## Results (final state, base origin/master `1b89025736`)
- `Sprk.Bff.Api.Tests` filter `Signals|WriterCreatedRecord|Finance|GateDecisionRecordSharing`: 15,131 passed, 0 failed, 10 skipped.
- `Sprk.Bff.Api.IntegrationTests` filter `Signals|WriterCreatedRecord|Finance`: 198 passed, 0 failed, 2 skipped (includes the Signals
  contract baselines; none changed).
- Full `Spaarke.ArchTests` (rebuilt explicitly, run after the last edit): 2,831 passed, 0 failed.
- Publish size (`dotnet publish -c Release`, `Compress-Archive`, short paths): base 43,619,644 bytes / 194 files; branch 43,619,566 bytes /
  194 files; delta -78 bytes. No package, DI registration, endpoint or new type.

## Review fix (independent review of #3167, F2)
`Services/Finance/InvoiceApprovalService.cs` (ebilling's): the K3 comments on `CompletionRecordNotWriterCreated`, on `FinishAsync` and at
the refusal branch said the writer's "already recorded" record is one "not created under the completion's filter". With both reads on
`WriterCreatedRecord`, the only way in is a concurrent approve (a writer record landing after the completion's read), so a double-click logs
`record_not_writer_created` for the writer's own record. Comments reworded to describe that race; the log message text is generic and was
not changed; the code name and value are kept (logs and tests key on them). The name now overstates; renaming is ebilling's call. No
behaviour change.

## Known limits
- K1: the read-route test fake (`SignalReadRouteTests.cs` ~2989) matches the `createdonbehalfby` condition with `Descendants("condition")`
  and the new string pin uses `Contains`, so a later move of `FetchXmlFilter` under an `or` or into the link-entity would not be caught;
  production nests it correctly (`SignalReadService.cs` ~1535).
- K2: `RuleSetService.CreatedByBefore` / `CreatedByAndNeverModified` compare `createdby` with the BFF identity to pick drafts for cleanup.
  That is not writer trust, so it is out of scope.
