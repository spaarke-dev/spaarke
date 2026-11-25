# Task 217 notes: the nightly evidence redaction reads Signals only for the not-in-force policies that may still have something to redact (#3091, DI-172)

## Cause
`SignalReevaluationJob.RedactPoliciesNotInForceAsync` (task 145) read every Off or Retired ONTOLOGY policy
(`SignalClosureService.PoliciesNotInForceAsync`), then `SweepStoredEvidenceAsync` sent one `sprk_signal` read per policy
(`ReadPolicySignalsAsync(forRedactionSweep: true)`, dedupe-key prefix, every status, the evidence column) every night. Same per-policy shape
as DI-164 (task 209). Most of those Signals never change again: the redaction is monotonic (it only removes detail), so a Signal whose stored
evidence has no detail left has nothing to redact, ever.

## Before (counted on the job harness fake, origin/master 2ac250cae4)
| Off policies | policies with a Signal still carrying detail | per-policy `sprk_signal` reads by the not-in-force sweep |
|---|---|---|
| 500 | 3 | 500 |

## Stored marker (the escalation check)
The marker exists, no schema change: task 145's redaction REMOVES the `text` and `source` properties of a stored evidence line
(`SignalSuppressionService.StoredEvidenceToRedactAsync`), and `SignalWriter` writes evidence with camelCase names and null properties omitted
(`EvidenceRefJsonOptions`). So a Signal whose stored evidence has detail left contains `"text"` or `"source"` (quotes included) in
`sprk_evidencerefs`; a Signal with nothing left to redact contains neither. `sprk_evidencerefs` is a multi-line text column (ntext, 100,000)
that Dataverse can filter with LIKE (`ValidForAdvancedFind`). The filter is a superset of the rule: a value equal to `text`/`source` or a
property name in another case (Dataverse string conditions are case-insensitive) also matches, and such a row is read and left alone by the rule.

## Design
`RedactPoliciesNotInForceAsync` keeps its shape and adds one step between the policy read and the per-policy sweep (task 209's pattern):
1. **Policy read: unchanged** (`PoliciesNotInForceAsync`, Ontology only, D-170's Off or Inactive; an incomplete read still throws and is a
   policy failure).
2. **New: `SignalClosureService.PoliciesNotInForceWithStoredDetailAsync`** (internal, next to `NotInForceConditions`), two paged reads of
   `sprk_signal`, both with `SignalSuppressionService.AddStoredDetailFilter` (`sprk_evidencerefs` LIKE `%"text"%` OR LIKE `%"source"%`):
   - inner-joined to `sprk_policy` with D-170's conditions OR-ed (the shared `NotInForceConditions`) and the Ontology allowlist
     (`RuleDomains.AddOntologyPolicyAllowlist`, through a carrier query as task 209 does): the predicate of `PoliciesNotInForceAsync`.
     Selects `sprk_policy` only; names policies by id.
   - with an EMPTY `sprk_policy`; selects the dedupe key and names the code it parses to. The per-policy sweep reads by dedupe-key prefix, so
     it also reached a Signal of the policy whose lookup is empty and which the deleted-policy sweep does not call an orphan (created at or
     after the policy, or a date unknown: a hand edit). This read keeps that Signal reached, so the set of Signals the sweep redacts is
     unchanged.
   Only a name in the policy read is returned (the intersection, as 209). 5,000 per page, by id, at most `SignalEvaluator.MaxPages` (200)
   pages per read.
3. **Per-policy sweep: unchanged** (`SweepStoredEvidenceAsync` → `ReadPolicySignalsAsync(forRedactionSweep: true)` →
   `RedactStoredEvidenceAsync`) for the named policies, in the policy read's order.
4. **When the new read is incomplete** (a page fault, or the bound with more to come) it logs at Warning with the counts and the sweep reads
   EVERY not-in-force policy, as before task 217.

### Why the same evidence is redacted
- `RedactStoredEvidenceAsync` (via `StoredEvidenceToRedactAsync`) only ever changes a Signal with a line holding `text` or `source`. Every such
  Signal of a not-in-force Ontology policy is returned by one of the two reads: its lookup names the policy (read 1), or its lookup is empty
  and its key names the code (read 2). A lookup names a policy whose code is the key's: `sprk_policycode` is written on create only
  (`OntologyAdminAuthoringService`, `RuleSetService`, `ProcedureAssignmentService`) and is an alternate key (`sprk_PolicyCodeKey`).
- A named policy's Signals are read, judged and written by the unchanged code (same prefix read, columns, rule, conditional write, counts).
- The redaction rule's code changed only mechanically: `text`/`source` now come from one array (`StoredDetailProperties`) shared with the
  query filter, removed in the same order, so the written JSON is byte-identical (tests compare the exact string).
- A policy not named has no Signal the rule could change, so the old read wrote nothing for it.

### Why a fallback, not "report and fail the run", for the new read
Task 204/209's rule is "never a silent partial success". The new read is a FILTER over a complete source: when it cannot answer, the sweep
does the complete work it did before, so nothing is partial and no evidence is left unredacted. Failing the run there would make a
filter's fault leave evidence unredacted (if the sweep then skipped) or retry the whole job for a result that is already complete. The reads
whose truncation fails the run are unchanged and still do: the policy read (throws, one policy failure) and each per-policy Signal read
(past 200 pages or a fault: a policy failure). Test `Run_APerPolicyReadThatFails_IsStillAPolicyFailure_AndTheRunFails` pins the second;
`Run_NotInForcePoliciesPastTheFirstPage_AreClosed_AndARunWhoseReadFailsIsReportedFailed` (unchanged) the first.

### Leading wildcard (Dataverse)
A `%…%` LIKE cannot use an index; Microsoft documents it as a query anti-pattern that Dataverse may time out
(`LeadingWildcardCauseTimeout`) or throttle (`DataEngineLeadingWildcardQueryThrottling`) when it judges the query a risk. Mitigations here:
read 1 is paired with the indexed join on the policy lookup (only Signals of Off/Retired Ontology policies are tested); read 2 with
`sprk_policy` null (only deleted policies' Signals); one query each per night; and any fault or throttle falls back to the old per-policy
sweep (logged at Warning), so the worst case is the old cost, never missed redaction. The database still reads the evidence of the candidate
rows to test the LIKE, as the old sweep read it to return it; what drops is the rows returned and the calls made.

### Deleted-policy sweep
Unchanged code (`DeletedPolicyCodesWithSignalsAsync`, `RedactSignalsOfDeletedPoliciesAsync`). One interaction: it skips a code the
not-in-force sweep already attempted this run. A not-in-force code that is no longer swept (no detail left) can now be swept by the
deleted-policy sweep when an older deleted policy's Signals carry the same reused code; that sweep reads the same prefix, applies the same
rule and finds nothing left to redact under it (otherwise read 2 would have named the code), so its writes are unchanged.
`SignalReevaluationJobDeletedPolicyTests` pass (one changed by a precondition line, see Deviation).

## After
| Off policies | policies with a Signal still carrying detail | `sprk_signal` reads by the not-in-force sweep |
|---|---|---|
| 500 | 3 | 5 (1 page of each stored-detail read + 3 per-policy reads) |

## Tests
Fake (additive, `SignalReevaluationJobTests.cs`): `Holds` evaluates `Like` as SQL LIKE (`%`, `_`, case-insensitive; bracket classes throw);
the no-policy read now also applies its full criteria through `Holds` (its existing reads have only `sprk_policy` null and the status `in`,
which `Holds` already modelled); `StoredDetailReads` records each read that filters on the stored evidence.

`SignalReevaluationJobTests.NotInForceRedaction.cs` (run level, the real job, closure, suppression service and writer):
- `Run_ManyOffPoliciesAlreadyRedacted_FewWithDetail_SweepReadsDoNotGrowWithTheOffPolicies(500)`: 500 Off policies, each with an
  already-redacted Signal, 3 also with one quoting a witness since moved behind a secure wall. 3 per-policy reads (master: 500), one page of
  each stored-detail read, the 3 redacted to the exact expected JSON, the 500 never written.
- `Run_AnOffPolicyWithDetail_IsRedactedAsBefore_AlreadyRedactedIsNotRewritten_AndTheRunSucceeds`: an Off policy's Signal with detail
  (redacted), its already-redacted Signal (untouched), a Retired policy's Signal with only a `source` (redacted to the exact JSON), an Off
  policy with nothing to clear (never read).
- `Run_ASignalWithNoPolicyLookup_KeyedToAnOffPolicy_IsStillRedacted_Once`: the empty-lookup case (read 2), read once.
- `Run_TheStoredDetailReadFaults_TheSweepReadsEveryNotInForcePolicyAsBefore_AndStillRedacts`: the fallback.
- `Run_APerPolicyReadThatFails_IsStillAPolicyFailure_AndTheRunFails`: regression guard (passes on master too).

`SignalReevaluationJobTests.NotInForceStoredDetailRead.cs` (service level):
- `StoredDetailRead_NamesOnlyNotInForceOntologyPoliciesWithDetail_InOnePageOfEachRead`: names exactly {Off with detail, Retired with `source`
  only, Off whose detail is on an empty-lookup Signal}; noise (detail on an in-force policy, on an Off Billing policy, an empty-lookup Signal
  keyed to no policy, already-redacted Signals with and without a lookup) would push a read past one page.
- `StoredDetailRead_PastTheBoundOrOnAFault_IsIncomplete_AndLogged`, `StoredDetailRead_NoNotInForcePolicy_ReadsNothing`.

Fail-before: with origin/master's three source files swapped in (the service-level file set aside: it calls the new method, so it does not
compile on master), 4 of the 5 run-level tests fail (500 per-policy reads instead of 3; every policy read instead of the named ones; no
fallback log). The fifth is the regression guard.

Mutation evidence for `StoredDetailRead_NamesOnly…` (scratch edits to `SignalClosureService.cs`, each built, run and reverted; the file
restored from a byte copy):

| Mutation | Test |
|---|---|
| Joined read: stored-detail filter removed | fails |
| Joined read: D-170 link filter removed (in-force policies' Signals read) | fails |
| Joined read: Ontology allowlist removed (Billing read) | fails |
| No-policy read: stored-detail filter removed | fails |
| No-policy read: key code not intersected with the not-in-force codes | fails |

## Deviation: one existing test changed (1 line)
`SignalReevaluationJobDeletedPolicyTests.Run_R4_ACodeThatStillExists_IsSweptOnce_AndNeverAsDeleted` seeded an Off policy's empty-lookup
Signal with NO stored evidence and expected the not-in-force sweep to read it anyway (then faulted that read). That read is the exact one
#3091 removes (nothing to redact), so the test cannot pass unmodified together with acceptance criterion 1 (209 hit the same, see its notes).
The edit gives that Signal stored evidence with detail, so the sweep still reads it (now through the no-policy stored-detail read) and the
test still proves what it is for: swept once, by the not-in-force sweep, never as "was deleted", one policy failure. Its assertions are
unchanged, and it passes on origin/master's sources too. Every other existing test passes unmodified.

## Placement and §11
- Placement: existing `SignalClosureService` (beside `NotInForceConditions`, `PoliciesNotInForceAsync` and task 209's joined read) and
  `SignalSuppressionService` (beside the redaction rule it mirrors); the job's call site. Nightly `IScheduledJob` path (ADR-036), unchanged.
- New surface: two internal methods (`PoliciesNotInForceWithStoredDetailAsync`, `AddStoredDetailFilter`), one internal record, one private
  helper and one private array. No endpoint, service, interface, DI registration, package or column.
- §11: **Existing** — `PoliciesNotInForceAsync` (policies, not Signals), task 209's `ReadPoliciesWithOpenSignalsAsync` (open Signals, close
  allowlist incl. Billing, no evidence filter), `DeletedPolicyCodesWithSignalsAsync` (empty lookup, no evidence filter), `ReadPolicySignalsAsync`
  (one policy). **Extension** — the new read extends `SignalClosureService` with the same join pattern and shares `NotInForceConditions`; 209's
  read could not be reused because its status filter and Billing allowlist are wrong for the redaction (it must read every status, Ontology only).
  **Cost of doing nothing** — one Signal read per Off or Retired Ontology policy every night, growing with every policy switched Off, inside the
  job's 2 h `MaxRunDuration`.

## Known limits
- K2: the two LIKE reads' cost on real Dataverse is not measured (leading wildcard, see above). A fault or throttle falls back to the old
  sweep, logged at Warning. To time on dev after deploy (App Insights dependency duration of the two `sprk_signal` reads in the 02:30Z run).
- K4: a policy whose Signals keep detail INSIDE their family (an ordinary witness on an ordinary record) is still read per policy every night,
  because the rule must re-check it (a witness can be re-filed into a secure family any day). The read count grows with the policies that
  still carry detail, not with every Off/Retired policy. Reading those Signals straight from the joined pages, grouped by policy, would remove
  the per-policy reads too; not done: it holds every candidate row (evidence up to 100,000 characters) at once or needs a policy-ordered page
  walk, and changes the per-policy bound and failure accounting. A follow-up if dev timing shows it is needed.
- K1: a detail property name written with a JSON escape (`"text"`) by something other than `SignalWriter` would not match the filter.
  Only a hand edit of a writer-only column can produce it.
- K2: a race between the policy read and the new read (a policy switched On in between) is handled as 209: only names in the policy read are swept.

## Results
- BFF build (`-warnaserror`): 0 warnings, 0 errors. Test project build: 0 warnings.
- `--filter "FullyQualifiedName~Signals"` (includes the contract baselines): 5,517 pass / 0 fail / 6 skipped (pre-existing).
- `--filter "FullyQualifiedName~SignalReevaluationJob"`: 323 pass.
- `tests/Spaarke.ArchTests` (built explicitly, full): 2,816 pass / 0 fail.
- Publish size (Release, short paths `C:\wm217` = fresh origin/master 2ac250cae4 vs `C:\wt217`, Compress-Archive, 194 files each): master
  43,591,916 bytes, branch 43,595,377 bytes, **+3,461 bytes**.
- No package or csproj change, so no new CVE surface.
- Conflict check: no open PR and no `work/ebilling-r1` change touches `SignalReevaluationJob.cs`, `SignalClosureService.cs`,
  `SignalSuppressionService.cs` or `tests/unit/domain/Signals/`.
- Self-review (Step 9.5; no sub-agents in this lane): no F-class finding left. ADR-002 n/a; ADR-036 (job unchanged); ADR-038 (tests under the
  `tests/unit/domain` KEEP path, one test modified by a precondition line, nothing deleted); §11 (internal/private members in existing
  services). Independent adversarial review: for the main session.

rework: none task 217
