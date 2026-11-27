# Task 233 notes - a ConditionCleared Decision Record derives its privilege flag app-only (D-232, #3275, DI-177)

Branch `fix/ontology-233-cc-dr-privilege-flag` (worktree `C:\wt233`, from origin/master `6f4d7bc7e4`). Rigor FULL.

## Findings (step 1)
- `SignalReevaluationJob.RecordConditionClearedAsync` stamped `PrivilegeFlagged` from `LineageRow["sprk_privilegeflagged"]`, selected in `ReadPolicySignalsAsync`. Nothing writes that column (DI-64), so every ConditionCleared record read not privileged.
- The commit route (task 225, `DecisionCommitService.PrivilegeFlaggedAsync`) uses `WorkItemPrivilege.SourcesOf` and `FlaggedAsync`, read with the caller's `IDataverseUserClient`. The nightly has no caller, and no app-only `IDataverseUserClient` exists. The job reads with `IGenericEntityService` (application identity).
- Escalation check: the job already reads `sprk_communication` app-only (the witness-row reads of the evidence filter, `SignalReevaluationJobTests` "witness" cases). The new read is the same identity and table, selecting only the id and `sprk_privilegeclassification`. No new privilege or role, and no communication the job could not already read. Not triggered.
- The job is a singleton (`AddScheduledJob` -> `TryAddSingleton`); `WorkItemPrivilege` is scoped (SignalsModule). The job resolves it per pass from `_scopes`, the same way it resolves `FinanceRollupService` (task 232).
- `/conflict-check`: no open PR touches `SignalReevaluationJob.cs`, `WorkItemPrivilege.cs` or their tests.

## What changed
- `Services/Signals/WorkItemPrivilege.cs`: new public `FlaggedAppOnlyAsync<TKey>(IGenericEntityService, items, ct)`. It applies the same rule as `FlaggedAsync`: one batched `QueryExpression` on `sprk_communication` (id and classification, In lists of at most 200). A row that is not returned flags its item. A fault (an exception, or `MoreRecords`) flags every item that needed the read. The item partition (undetermined items flag without a read; items with no communication are never read) moved unchanged into a private `FlaggedThroughAsync`, which `FlaggedAsync` now calls, so the commit path behaves as before. There is a new SDK overload of `IsFlagging`.
- `Services/Signals/SignalReevaluationJob.cs`: a new private `ClosurePrivilegeFlagsAsync` makes ONE call per policy pass, over all candidates, before the per-Signal loop. Sources come from `SourcesOf(subject, id, stored sprk_evidencerefs)`; a Signal with no row read is `Unknown`. A fault returns null, and every record is then flagged. Each record stamps `privilegeFlagged is null || privilegeFlagged.Contains(signal.Id)`. `sprk_privilegeflagged` is removed from the Signal `ColumnSet`.
- `Services/Signals/DecisionRecordWriter.cs`: doc comment of `ConditionClearedRecordRequest.PrivilegeFlagged` only.
- Not touched: `DecisionCommitService`, `DecisionRecordWriter` logic, D-145b keying and lookup.

## Cost (nightly)
One extra Dataverse read per Do-lane policy pass that has at least one condition-cleared candidate naming a communication, plus one more per 200 distinct communications. There is no read when no candidate names one; that is the common case, because Do-lane subjects are To Dos, events and work assignments. A linked closure is never a candidate, so a re-run reads nothing (test).

## Tests
- New `tests/unit/domain/Signals/SignalReevaluationJobTests.ConditionClearedPrivilege.cs` (real job and writer over the harness): Privileged / Potentially Privileged -> true; None / null -> false; Signal column seeded true with no communication -> false, and no `sprk_signal` read selects the column; a classification read fault -> true, and the closure is recorded and linked with a successful run; a missing communication -> true; two closures -> one batched read, each with its own flag; a re-run after a flagged closure -> no writes and no classification read.
- New `tests/unit/domain/Signals/WorkItemPrivilegeAppOnlyTests.cs`: every classification plus a missing row, no read when nothing names a communication, throw / MoreRecords fault, cancellation rethrown, chunks of 200.
- Harness (`SignalReevaluationJobTests.cs`): registers `WorkItemPrivilege` in the job's scopes; `World` answers the classification read from stubbed `sprk_communication` rows and records `PrivilegeReads`.
- Fail before: with the new job tests on the origin/master production code, 9 of the 9 new job cases fail (the None/null rows fail only on the "one read" assertion; their flag is false on master too). `WorkItemPrivilegeAppOnlyTests` does not compile on master, because the method is new.
- After: `--filter "FullyQualifiedName~Signals"`: 5637 passed, 0 failed, 6 skipped (contract baselines included; no baseline moved). ArchTests: see below.

## BFF hygiene (§10, §11)
- No new package, endpoint or DI registration. There is a new public method on an existing class:
  - Existing: `WorkItemPrivilege.FlaggedAsync` (the caller read), used by the Decide lane and by `DecisionCommitService.PrivilegeFlaggedAsync`.
  - Extension: the class is extended, not copied. `FlaggedAsync` cannot serve: its client is the caller's on-behalf-of identity, and the nightly has none (D-232 chooses the application identity).
  - Cost of doing nothing: every system-closed (ConditionCleared) Decision Record reads not privileged, even when its evidence communication is Privileged (#3275).
- Publish size: `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` in fresh short-path worktrees (`C:\wt233m` = origin/master `6f4d7bc7e4`, `C:\wt233` = this branch), then `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`. Master: 43,716,797 B, 194 files. Branch: 43,718,981 B, 194 files. Delta +2,184 B.
- No package change, so no CVE re-check is needed.
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. `dotnet format --verify-no-changes` on the three BFF files: clean. `tests/Spaarke.ArchTests` (built explicitly first): 2860 passed, 0 failed.

## Step 9.5 (inline self-review; the main session reviews the PR, D-115)
- No F-class finding. ADR-015: the flag is stamped only and never branched on. ADR-010: no new registration. ADR-052: no new background work. ADR-038: each acceptance criterion has a test, including must-not-fire controls. D-145b: keying and lookup are unchanged, the existing idempotency tests pass, and a re-run after a flagged closure writes and reads nothing.
- Known limit (K): a communication deleted after the Signal was raised flags its record, because it cannot be judged. This is the same fail-closed rule as the caller read, where an unreturned row flags.
- Known limit (K): the read uses the evidence as stored before this pass's redaction writes. Redaction removes detail only, so the flag can only err toward flagged.

## Other issues seen (not fixed)
- None.
