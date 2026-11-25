# Task 209 notes: the nightly D-170 close reads Signals only for the not-in-force policies that still have open Signals (#3014, DI-164)

## Cause
Task 204 (#3011) made `SignalClosureService.ReadPoliciesNotInForceAsync` page through every Off or Inactive policy (Ontology + Billing for the
close). `ClosePoliciesNotInForceAsync` then sent one `sprk_signal` query (`SelectOpenSignalsOfPolicyAsync`) per such policy, whether or not it
had anything open. ebilling task 117 switches record rules Off on every core-record delete, so that count grows without bound while most of
those policies have no open Signal; at ~50 ms a read the close alone would fill `MaxRunDuration` (2 h) at ~140,000 policies.

## Before (counted on the job harness fake, origin/master e70ff38894)
| Off policies | policies with open Signals | `sprk_signal` reads by the close |
|---|---|---|
| 500 | 3 | 500 |
| 1,000 | 3 | 1,000 |

## Design
`ClosePoliciesNotInForceAsync` keeps its shape and adds one step between the policy read and the per-policy close:
1. **Policy read: unchanged** (`ReadPoliciesNotInForceAsync(includeBilling: true)`, id-cursor pages of 5,000, ceil(N / 5,000) reads per condition,
   task 204's fault/bound reporting). It still defines D-170's set: only a policy it returns is ever closed.
2. **New: one paged open-Signal read** (`ReadPoliciesWithOpenSignalsAsync`, private): `sprk_signal` with status In (Open, Acknowledged),
   inner-joined on `sprk_policy` -> `sprk_policyid`, link criteria = D-170's conditions OR-ed (`sprk_enabled` = No OR `statecode` = Inactive,
   from one shared `NotInForceConditions` array that the policy read now also uses) AND the close's domain allowlist
   (`RuleDomains.AddClosableOnRetirementAllowlist`, applied to the link through a carrier query so the rule keeps one definition).
   ColumnSet = `sprk_policy` only (exists in every environment; the F1 billing-column rule holds). 5,000 per page (`NotInForcePageSize`), ordered
   by `sprk_signalid`, paging cookie, at most `maxPages` pages (default `SignalEvaluator.MaxPages` = 200, so 1,000,000 Signals). The whole read
   completes before any close, so paging never runs over rows being closed. Skipped when the policy read returned no policy.
3. **Per-policy close: unchanged** for each policy the policy read returned AND the open-Signal read named: the same
   `SelectOpenSignalsOfPolicyAsync(withBillingSubject: false)` (the shared "preview = sweep" selection) and the same `CloseAsync` with
   `PolicyRetired`, in the same order. A policy with no open Signal is skipped: its selection would have returned no row.

### Why the same Signals close
- The joined read's predicate is the policy read's predicate (same conditions array, same allowlist helper), so every policy the policy read
  returns that has an Open/Acknowledged Signal is named. It can name extra policies only in a race (a policy switched Off between the two
  reads); the intersection with the policy read drops those, as before.
- The Signals of a named policy are selected and closed by the unchanged code (same criteria, columns, row-version conditional close, reason,
  `resolvedon` = `lastevaluated` in one update (D-120), secure-child Signals included with no skip (D-38)). A Resolved Signal is never read, and
  nothing is written for a policy with nothing open (D-61).
- A Signal whose policy was deleted has no row to join; `CloseSignalsOfDeletedPoliciesAsync` keeps handling it (untouched).
- Only difference, a race window: a policy switched back On between the policy read and the joined read is no longer closed this run (before:
  closed on the stale read, then re-raised by the next pass under D-32). That's the policy's current state, so it's a known limit (K2), not a
  change of rule.

### Truncation and faults (task 204's rule, extended to the new read)
A page fault, or `maxPages` pages with more to come, ends the joined read: logged at Error (`EvaluatorPolicyFailed`) with the page and counts,
the policies it named are still closed (in full, through their own selection), and the outcome gets one `PolicyFailures`, so `IsComplete` is
false and the job fails the run (`policyFailures=` in its error, `outcome: failed`). Closed Signals drop out of the read, so the next run continues.
Before, a per-policy read fault counted one failure for that policy only; now a joined-read fault counts one failure and leaves the policies
after the fault for the retry. Both fail the run and are retried (D-78).

### Preview and sweep paths
Unchanged. `PoliciesNotInForceAsync` (Ontology-only, the stored-evidence redaction read) and `ReadPoliciesNotInForceAsync` behave exactly as
before (the only edit there swaps an inline array for the shared `NotInForceConditions`, same values and order). The Procedure retire preview,
the Procedure retire close and the admin retire close keep `SelectOpenSignalsOfPolicyAsync`; the nightly close still uses it for every policy it
closes (`ProcedureRetireSelectionGuardTests` passes).

## After
| Off policies | policies with open Signals | `sprk_signal` reads by the close |
|---|---|---|
| 500 | 3 | 4 (1 joined page + 3 selections) |
| 1,000 | 3 | 4 |
Policy reads are unchanged: ceil(N / 5,000) per condition.

## Deviation: one existing test changed (3 lines added)
`SignalReevaluationJobTests.ProcedureRetire.cs` `EveryPolicyRetiredSelection_SelectsTheSameRows_AndOnlyTheProcedureRetireOptsInToTheInvoiceColumns`
ran the nightly sweep over an Off policy with **no** Signals and expected the sweep to send its per-policy Signal read anyway. That read is the
exact one #3014 removes, so the test cannot pass unmodified together with acceptance criterion 1. The edit seeds one open Signal on the policy
just before the sweep call, so the sweep still reaches its selection and the test still proves what it is for (every PolicyRetired caller selects
through `OpenSignalsOfPolicyQuery`; only the Procedure retire opts into the invoice columns). Its assertions are unchanged, and it passes on
origin/master's service too. Every other existing SignalClosure / SignalReevaluation test passes unmodified.

## Tests (`tests/unit/domain/Signals/SignalReevaluationJobTests.NotInForceOpenSignals.cs`, 7 cases)
Fake (additive, in `SignalReevaluationJobTests.cs`): the joined read is answered as Dataverse answers it. Root criteria are evaluated on the
Signal and link criteria (nested And/Or filters: eq, in, null, not-null) on its policy row. A deleted policy's Signal has nothing to join, the
read is paged by id (`PageSizeCap` lowers the page), and any other link shape or operator throws.
- Call count, 500 and 1,000 Off policies with 3 having open Signals (one with an Acknowledged second): 4 Signal reads, only those 3 policies
  selected, all 4 closed PolicyRetired with `lastevaluated` = `resolvedon`.
- Must-not-fire: an in-force policy's Open and Acknowledged Signals and an Off policy's Resolved Signal are not written (row version and fields
  unchanged), and an in-force policy or an Off policy with nothing open is never selected. One conditional update in all.
- Bound: two-Signal pages, `maxPages` 2, 5 Signals: 4 closed, the 5th open, one `PolicyFailures`, Error logs with counts. The next run
  closes the 5th.
- Fault on page 2 of the joined read: 2 closed, 3 open, one `PolicyFailures`, Error log with page and counts.
- Run level: a joined read cut short at the 200-page bound fails the run (`policyFailures=1`, `outcome: failed`); a faulted joined read fails
  the run and leaves the Signal open.
- No not-in-force policy: no Signal read at all (regression guard; passes on master too).
Fail-before: with origin/master's `SignalClosureService.cs` swapped in, 6 of the 7 fail (500 and 1,000 reads instead of 4; no truncation or
fault reporting; the Off policy with nothing open is selected). The seventh is the regression guard.
Existing billing-domain tests (Billing Off closes, Docketing Off does not, Billing enabled untouched) now run through the joined read's domain
allowlist and pass unmodified.

## Placement and §11
- Placement: existing `SignalClosureService` (nightly `IScheduledJob` path). No new service, interface, endpoint, DI registration or package; job
  unchanged.
- Existing: `SelectOpenSignalsOfPolicyAsync` (per policy), `ReadOpenSignalsWithoutPolicyAsync` (null policy only), `ReadPolicyPagesAsync`
  (policies, not Signals). Extension: the new private helper extends `SignalClosureService`; none of the existing reads can say "which
  not-in-force policies have an open Signal" in one query. Cost of doing nothing: one Signal read per Off/Retired policy every night, growing
  with every core-record delete, until the close fills the 2 h run and the job fails every night.

## Known limits
- K2: the race above (a policy switched On between the two reads is not closed this run; its state is current).
- K2: a joined-read fault leaves the policies after the faulted page for the next run (reported, run failed), where a per-policy fault used to
  leave one policy.
- The policy read still costs ceil(N / 5,000) reads per condition (bounded at 200 pages = 1,000,000 policies per condition by task 204).

## Out-of-scope finding (for the operator to file)
The stored-evidence redaction (`SignalReevaluationJob.RedactPoliciesNotInForceAsync` -> `SweepStoredEvidenceAsync` -> `ReadPolicySignalsAsync`)
sends one `sprk_signal` read per Off/Retired ONTOLOGY policy every night, all statuses, by dedupe-key prefix. That is the same per-policy pattern.
It reads Ontology policies only (`PoliciesNotInForceAsync`), so ebilling's billing rules switched Off do not grow it. It grows only with
Ontology policies switched Off or Retired. It is the sweep this task must leave unchanged (POML escalation trigger), so it is not touched here.
Fix direction: the same joined-read pattern, or a per-policy "already redacted" marker.

## Results
- BFF build (`--warnaserror`): 0 warnings, 0 errors. Test project build: 0 warnings.
- `--filter "FullyQualifiedName~Signals"` (includes the contract baselines): 5,412 pass / 0 fail / 6 skipped (pre-existing).
- `--filter "FullyQualifiedName~Signal|FullyQualifiedName~Procedure|FullyQualifiedName~Ontology"`: 8,084 pass / 0 fail / 7 skipped.
- `SignalReevaluationJobTests` (all partials): 313 pass.
- `tests/Spaarke.ArchTests` (built explicitly, full): 2,793 pass / 0 fail.
- Publish size (Release, short paths `C:\wm209` = fresh origin/master e70ff38894 vs `C:\wt209`, Compress-Archive, 194 files each): master
  43,573,356 bytes, branch 43,574,425 bytes, **+1,069 bytes**.
- No package or csproj change, so no new CVE surface.
- Conflict check: no open PR and no `work/ebilling-r1` change touches `SignalClosureService.cs`, `SignalReevaluationJob.cs` or
  `tests/unit/domain/Signals/`.
- Self-review (Step 9.5): no F-class finding. ADR-002, ADR-036 (job unchanged), ADR-038 (one KEEP-path test modified, not deleted: see
  Deviation) and §11 (private members only) were all checked. Production wiring: the `SignalClosureService` singleton sits over
  `DataverseServiceClientImpl`, which passes the `QueryExpression` (link included) to `ServiceClient`. The dry-run `ProcedureRowOverlay`, which
  refuses joins, never wraps this service.

rework: none task 209
