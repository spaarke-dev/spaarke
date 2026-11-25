# Task 204 notes: the nightly D-170 close pages the not-in-force policies (DI-162, #2982)

## Cause
`SignalClosureService.ReadPoliciesNotInForceAsync` ran one unpaged `RetrieveMultipleAsync` per condition (Off, Inactive). Dataverse answers
one page (5,000 rows) and `IGenericEntityService` does not follow `MoreRecords`, so past 5,000 Off (or Inactive) policies the rest never had
their open Signals closed and nothing reported it. `SignalReevaluationJob` would also have counted such a run a success.

## Fix (`Services/Signals/SignalClosureService.cs`)
- Each condition is read by id cursor: `TopCount` = `NotInForcePageSize` (5,000), ordered by `sprk_policyid`, continued after the last id read
  (the paging of `RuleSetService.RetireUnlinkedRecordSetsAsync`), until a short page. Off/Inactive overlap is deduped by policy id.
- A page that adds no new policy (a store ignoring the cursor) ends the read as a fault (no endless loop). The page bound is
  `SignalEvaluator.MaxPages` (200) per condition.
- A faulted or bound-truncated read is logged at Error (`EvaluatorPolicyFailed`) with the page, the condition and the counts. The read
  returns the policies it got plus `IsComplete = false`.
  - `ClosePoliciesNotInForceAsync` closes the policies it did read, then adds one `PolicyFailures` and logs Error with counts. The job already
    adds `outcome.PolicyFailures` to its counts, so the run is `Success = false` (`policyFailures=` in `ErrorMessage`, `outcome: failed` in the
    result JSON) and `RecordLastSuccess` is not written. No job change was needed.
  - `PoliciesNotInForceAsync` (the Ontology-only redaction read, signature kept) throws when incomplete; the job's existing catch counts a
    policy failure and retries next run.
- `pageSize` / `maxPages` are optional internal parameters so tests reach "past one page" without seeding thousands of rows.
- D-61, D-120, D-170 untouched: same conditions, same allowlists, same per-policy close. No schema change, no new job.

## Second read, same defect: the job's enabled-policies read (review of #3011)
`SignalReevaluationJob.ReadEnabledPoliciesAsync` was one unpaged `RetrieveMultipleAsync`: past 5,000 enabled Ontology policies the rest were never
evaluated (nightly) or consulted (trigger) and the run reported success. Matter-scoped Ontology rules (D-165) make the count grow.
- The paging moved into ONE shared reader, `SignalClosureService.ReadPolicyPagesAsync` (id cursor, `TopCount`, order by `sprk_policyid`, no-progress
  guard, `MaxPages` bound, Error log with counts, rows read so far returned with `IsComplete`). `ReadPoliciesNotInForceAsync` and the job's
  `ReadEnabledPoliciesAsync` both call it; no second copy. The not-in-force read's log wording changed to the shared text ("N were read in all").
- Same filter, allowlist (`AddOntologyPolicyAllowlist` + per-row `IsOntologyPolicy`) and `ToPolicyRow`. The result is re-sorted by policy code
  (ordinal, ignore case), the order the evaluation always had, because the cursor needs the id order.
- Incomplete read: nightly adds one `PolicyFailures` (run `Success=false`, `policyFailures=1`, no last-success) and EVALUATES the policies read (a retry
  has only the rest to do; not evaluating them would lose a night's signals for nothing). `EvaluateSubjectAsync` (the trigger) adds one to the result's
  `PolicyFailures` and evaluates what was read. A total failure (read throws before any page) is unchanged: logged, run failed, rethrown for the retry.
- The stored-evidence sweep after the evaluation redacts only the codes it is given, so a cut-short list sweeps fewer policies, never more.
- Tests (4 new, all fail on master's job read): 5,001 enabled policies all evaluated; reader pages (small page size), reports a page fault and the
  bound; run with a faulted page fails and still evaluates the 5,000 read; trigger reports the failure.

## Not done (and why)
"Limit the read to policies that still have open Signals": not cheap and correct. It means a Signal-side query joined to the policy state
(a different selection from `OpenSignalsOfPolicyQuery`, which the preview and the sweep share), or a second per-policy count. The per-policy
close stays; the cost is one Signal read per not-in-force policy per night, as before. Worth revisiting if the policy count reaches the
tens of thousands.

## Tests (`SignalReevaluationJobTests.NotInForcePaging.cs`: 7 for the close, 4 for the enabled read)
Fake extension in `SignalReevaluationJobTests.cs` (additive, no existing test changed): `sprk_policy` reads honour `sprk_policyid >` cursor
and `TopCount`, an unpaged read returns one 5,000-row page (`UnpagedReadCap`, as Dataverse), `IgnorePolicyCursor` simulates a store that
ignores the cursor.
Fail-before (new tests against origin/master's service with only the two optional parameters added so they compile): 6 of 7 fail; the seventh
(overlap dedupe) is a regression guard and passes on both (the old dictionary already deduped).
Fail on master: past-first-page close (5,003 policies, 3 Signals, only the first closed), page failure reported, bound truncation reported,
redaction read throws, cursor-ignoring store, job-level (5,001 policies; a faulted cursor read leaves a run that reports success).
Enabled-read fail-before (master's job read plus a shim so the tests compile): 4 of 4 fail.

## Known limits
- Re-sorting by code is in memory on the whole list (5,000-row pages, so the list is the whole catalogue in memory, as before).
- K2: two reads (close, then redaction) of an incomplete not-in-force list each count one policy failure (`policyFailures=2`).
- The id cursor compares ids as Dataverse orders them; the no-progress guard uses a seen-set, not a Guid comparison.

## Results
- BFF build: 0 warnings, 0 errors. Filtered tests (after the enabled-read fix): `~Signal` 5,315 pass / 7 skipped (pre-existing), SignalReevaluationJobTests 306 pass; `~Procedure` and ArchTests: see the PR.
- Publish size (measured on the first commit; the enabled-read commit adds one shared method and removes a copy, so no re-measure) (Release, short paths, Compress-Archive, 191 files each): master 42,615,338 bytes (40.64 MB), branch 42,617,948 bytes (40.64 MB), +2,610 bytes.
- `dotnet list package --vulnerable --include-transitive`: none.
- Placement: in BFF, existing `SignalClosureService`/nightly `IScheduledJob`; no new service, endpoint, DI registration or package.
- Follow-up: tell ebilling-r1 (filed #2982 as ISS-044) when merged.
