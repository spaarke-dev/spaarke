# Task 145 — matter readers (D-34), stamp backfill, evidence leak (D-123, #1747 e)

> Branch `task/ontology-145-matter-readers` (from `origin/docs/ontology-platform-design` at `8f7ff5ed5`, which includes 144 / #1862).
> Rigor FULL. This is [uac] work; uac-r2 reviews the PR. This file is the POML's `notes/145-matter-readers.md`, renamed to
> the `task-145-*` notes convention.

## uac-r2 code read

`origin/work/unified-access-control-r2` at **`f808329b76973781509f4bc620e1027954890292`** (2026-10-14 13:09, "notes(uac-r2): 185
release gate met..."). Read: `SecureChildLineage`, `SecureChildReconciler`, `SecureChildShareSynchronizer.SecureRootsAboveAsync`,
and the #1747 approval comment ("(e) is fixed by ontology task 145"). uac-r2's `SecureChildLineage` Decision Record row is older
than this branch's; ours already carries the DR subject lookups from master's uac-182. Neither `SecureChildLineage` nor any other
uac-r2 file is edited here.

## 1. Reader classification (D-34; criterion 1)

D-34 / D-131: **context** = the decision-time core (`sprk_corerecordid` + `sprk_corerecordtype`, immutable); **filing** = the live
`sprk_regarding{matter,project,workassignment,servicerequest}` stamp, which follows the SUBJECT (144 review K1).

| Reader / writer of a typed core column | Meaning | Column it uses now | Change |
|---|---|---|---|
| `SignalWriter.BuildEntity` writes `sprk_signal.sprk_matter` = the core when the core is a matter | context (equals the frozen core); lineage parent | typed = core | kept; remark says so |
| `DecisionRecordWriter` `Content` → `LineageColumnByCoreEntity` writes `sprk_matter` / `sprk_project` / `sprk_workassignment` = the core | context; lineage parent | typed = core | kept; remark says so |
| `DecisionCommitService` — the decision's matter, passed to the executors' follow-ups and the budget-revision trigger (`AfterRecordedAsync`, `AfterFailedCommitAsync`) | **context** (the matter the decision was about) | was typed `sprk_matter` read off the Signal row | **FIXED**: `Prepared.MatterId` is the core pair's id when `sprk_corerecordtype` is a matter; `sprk_matter` is no longer selected (`SignalColumns`, `SignalRow`) |
| `SignalSuppressionService.ItemOf(hit)` sets `sprk_matter` on the probe row | lineage (mirrors the row the writer will write, so the family walk sees the same parents) | typed = core | kept |
| Suppression `SignalLineageColumns`, `SignalReevaluationJob.ReadPolicySignalsAsync` lineage row, `TodoAssigneeOwnerReconciliationJob` lineage parents, `SecureChildReconciler` / `SecureChildShareSynchronizer` / reconciliation job (uac-r2) | lineage / ownership (D-123: a record ABOUT a secure core is in that core's family) | typed = core, alongside the subject lookups and stamp | kept; any retirement is uac-r2's, after re-proving K1 per column |
| `SignalCoreRecordAccess`, worklist grid configuration (`config/worklist.gridconfiguration.json`), suppression grains | context | core pair already | none |
| Dedupe key `{policy}\|{subjectEntity}\|{subjectId}\|{episode}` | subject identity | the subject; no typed read | none (escalation trigger 2 does not fire: no key changes) |
| `SignalWriteResult.GroupingMatterId` | context | computed from the core | none (no production consumer) |
| Client code, forms, views, subgrids | — | the `sprk_signal_matter` / `sprk_decisionrecord_{matter,project,workassignment}` relationships exist; nothing renders them | none |

No reader needs both meanings, so escalation trigger 1 does not fire.

### Typed core columns: kept as context (criterion 3)

New Signal and DR rows still write the typed core columns, **as context, never as filing**. Why they are kept and not retired:
they always equal the frozen core, and the core is an owner parent in the lineage (`OwnershipContextOf`, `SecureChildLineage`):
D-123 puts a record about a secure core in that core's family even after its subject is re-filed. The stamp cannot replace them
(K1): after a re-file P→Q the stamp says Q while the core stays P, so a lineage built on "stamp equals core" would drop P. No
reader treats them as filing now. Retiring them from lineage is uac-r2's decision and needs the K1 premise re-proved per column,
or the column stays as context.

## 2. Evidence leak (D-123, #1747 finding e; criterion 2)

**Before:** a matter Signal's evidence persisted a witness communication's `sprk_subject` / `sprk_from` (the `SignalEvidenceRef`
`Text` / `Source`), even when that communication was also filed under a secure project X, so M's (ordinary) Signal showed X's
communication detail.

**After:** `SignalSuppressionService.EvidenceWithinPartitionAsync` (called once per policy run at the top of
`SignalReevaluationJob.ApplyAsync`, for every reconcile and create; evidence is stored only when a row is created) keeps a line's text and source only when the witness's
secure-root family equals the Signal's family and neither is Undetermined. Otherwise it writes the line with `Text = null,
Source = null`; ref, tier, confidence, asOf and clause stay, and the UI renders a missing text as Missing. Both families come from
`PartitionsAsync`, the one partition truth (uac-r2's `SecureRootsAboveAsync` walk). The witness rows come from
`ReadSubjectRowsAsync`. An environment with no Secure Record owner team (`ResolveSecureTeamAsync` null) is all-ordinary and is
left unchanged. A fault while reading the families clears the detail on every line (fail closed) and logs
`SuppressionEvents.Failed` at Warning.

**Stored evidence (review F1, option A; §6).** Evidence is written once, at create, so the create-time filter alone would leave
detail on an existing Signal whose witness later moves into a secure family. Each pass therefore also runs
`SignalReevaluationJob.RedactStoredEvidenceAsync`, LAST in the policy's pass:
- It takes every Signal of the policy read this pass, open AND resolved, with `sprk_evidencerefs` (added to
  `ReadPolicySignalsAsync`'s columns).
- `SignalSuppressionService.StoredEvidenceToRedactAsync` places each Signal from its row as read (`PartitionsAsync`) and the
  witnesses through the same `WitnessFamiliesAsync`, and applies the same `KeepsDetail` rule as a create.
- A stored line that fails the rule loses `text` and `source`; every other property is kept as stored. It is monotonic: detail
  is never added or restored.
- `SignalWriter.RedactEvidenceAsync` writes only `sprk_evidencerefs`, conditional on the version read. A concurrent change wins
  and is counted `changedSinceRead`; the next pass retries.
- A family-read fault writes nothing that pass. It counts as a policy failure, logs a Warning, and the next pass retries. So a
  fault neither strips detail nor widens it.
- It runs last so its writes never invalidate the versions this pass's own conditional closes were planned on.
- Task 144's report-only tables do not apply: they hold stamp writes, and evidence is not a stamp.
- The run result counts `evidenceRedacted`. A write failure is metered `signal_evidence_redact_failed`.

**Sweep after the policy loop (uac-r2 F1 on #1874 = pass 2 F2).** Several paths stop an IN-FORCE policy before its own
redaction:
- an invalid rule;
- no current version or lane (thrown);
- a version not in force;
- a partition skip in the suppression pass;
- `hitPartitions is null` (placing the hits faulted);
- `PartitionsUnavailable` from the dismissal-facts/count read;
- any other exception.

`SweepStoredEvidenceAsync` therefore runs after the loop. It covers every in-force policy code the run has not yet sent
through `RedactStoredEvidenceAsync`, then the Off/Retired policies (`RedactPoliciesNotInForceAsync` goes through the same
sweep).
- **Once per policy per run:** `RunCounts.EvidenceRedactionAttempted` is set when a policy's redaction starts, whatever the
  outcome. The sweep skips those policies, so a family-read fault in the policy's own pass is not retried within the run.
- **Faults:** a sweep that cannot read a policy's Signals writes nothing, counts a policy failure and logs at Error, so the run
  fails visibly. A family-read fault inside the sweep is handled by `RedactStoredEvidenceAsync` the same way.
- **Never counted twice (uac-r2 re-check F1).**
  - The sweep reads with `ReadPolicySignalsAsync(..., forRedactionSweep: true)`. In that mode an unparseable dedupe key is
    skipped without being counted, logged or metered, and the read does not throw. So the policy's parseable Signals are still
    redacted; this closes uac-r2's K-a, where one bad key left the whole policy unredacted.
  - `RunCounts.PoliciesFailedThisRun` holds the policies whose own pass already failed the run (a policy failure or an invalid
    rule). A sweep read failure for one of them is logged but not counted again.

## 3. Stamp backfill (criterion 4)

**What it means (K1).** The typed core columns are never copied into the stamp:
- When the subject is itself a core, the subject lookup *is* the stamp, so there is nothing to write.
- When the subject is an intermediate, the stamp is that intermediate's *current* root. Only the BFF derives that root
  (`CoreAncestorResolver`, task 144's `CoreAncestorStampReconciliationJob`).
- A typed→stamp copy would be wrong after any re-file, and a second derivation in a script would be a parallel owner of the
  invariant (D-131 C2).
- So the write is the job's own write. Task 144 holds `sprk_signal` and `sprk_decisionrecord` report-only, and the backfill
  releases that hold.

**Tool.** `scripts/Invoke-OntologyStampBackfill.ps1`. By default it runs a read-only inventory using GETs only, then writes a
JSON report. `-Apply` prints the same report, then sets the app setting below; ShouldProcess gates the setting change. The tool
has 8 Pester tests in `tests/scripts/Invoke-OntologyStampBackfill.Tests.ps1`, which exercise the script's own AST functions.

**Dry run on spaarkedev1, 2026-10-14 18:20Z.** It ran without `-Apply`, issued GETs only, and resolved every `$select` column.

| Table | total | core subject | intermediate, stamp empty (definite backfill) | intermediate, stamped | no subject | typed set / not in stamp |
|---|---|---|---|---|---|---|
| `sprk_signal` | 0 | 0 | 0 | 0 | 0 | `sprk_matter` 0 / 0 |
| `sprk_decisionrecord` | 0 | 0 | 0 | 0 | 0 | `sprk_matter` 0 / 0, `sprk_project` 0 / 0, `sprk_workassignment` 0 / 0 |

There are no sample rows: both tables are empty on dev (matching the MCP `read_query` of 2026-10-14). The backfill write has
**not** run.

**Live command (owner approval required; D-138 prerequisites in the script help: the BFF carries 144 + 145, a report-only job run
is recorded, and uac-r2 has reviewed it):**

```powershell
& ./scripts/Invoke-OntologyStampBackfill.ps1 -Apply
```

That command sets `CoreAncestor__StampReconciliation__ReportOnlyTables=","` on `rg-spaarke-dev/spaarke-bff-dev`. The value `","`
names no table, whereas an empty app setting can be dropped and would restore the default hold. To verify, re-run the command
without `-Apply` about 10 minutes later: `stampEmpty` should be 0 on both tables. To undo, run
`az webapp config appsettings delete --resource-group rg-spaarke-dev --name spaarke-bff-dev --setting-names CoreAncestor__StampReconciliation__ReportOnlyTables`.

## 4. Tests (criterion 5)

| Test | Proves | Before → after |
|---|---|---|
| `SignalSuppressionTests.T145_ACommunicationFiledToOrdinaryMAndSecureX_GivesMsSignalNoSubjectOrSender` | the stated case | fails on HEAD (no such method) → pass |
| `…T145_APurelyOrdinaryCommunication_StillGivesItsSubjectAndSender` | the stated control | → pass |
| `…T145_ASecureSignalQuotingAWitnessOfItsOwnFamily_KeepsTheDetail` | beyond scope, justified: proves the rule compares families rather than clearing on "any secure" | → pass |
| `…T145_AWitnessWhoseFamilyIsUndetermined_CountsAsSecure_AndGivesNoDetail` | D-123 "Undetermined counts as secure" | → pass |
| `…T145_WhenTheFamiliesCannotBeRead_EveryLineLosesItsDetail_FailClosed` | fail closed | → pass |
| `SignalReevaluationJobTests.Run_T145_AWitnessAlsoFiledUnderASecureProject_IsWrittenWithoutItsSubjectOrSender` | the job writes the filtered lines (the written `sprk_evidencerefs` has no text or source; ref, asOf and clause stay) | fails with the job unfiltered → pass |
| `DecisionCommitServiceTests.T145_TheDecisionsMatter_IsTheCorePairsMatter_NeverTheTypedSprkMatter` (2 cases) | the commit route's matter is the core pair's | 2/2 fail on the HEAD file → pass |
| `SignalSuppressionTests.T145_ASignalAndAWitnessBothUndetermined_AreNeverTheSameFamily_AndGiveNoDetail` | review F3: two Undetermined families are never equal | fails with the Undetermined guards removed → pass |
| `SignalReevaluationJobTests.Run_T145_AReconcileOfAWitnessAlsoFiledUnderASecureProject_AttemptsNoSubjectOrSender` | review F3: a reconcile's create-first payload is filtered too | fails with `Writable` removed from the reconcile → pass |
| `DecisionCommitServiceTests.T145_AProjectCoresDecision_HasNoMatter_EvenWhenATypedSprkMatterIsSet` | review F3: only a matter core gives the decision a matter | fails under `MatterId => Core?.Id` → pass |
| Pester `Invoke-OntologyStampBackfill.Tests.ps1` (10) | the backfill classification, with must-fire and must-not-fire cases; `Get-AppSettingValue` throws on a non-zero `az` exit and returns the trimmed value on success (pass 2 K5) | new |

The existing `Run_PathBHit_WritesOneWitnessLinePerExistsClause…` stays as the ordinary control.

Review F1 (option A), through the job's real write path:

| Test | Proves |
|---|---|
| `Run_T145F1_AWitnessRefiledIntoSecureXAfterTheSignalWasCreated_LosesItsDetailOnTheNextPass_Once` | Created with the detail; after a re-file into secure X the next pass makes one conditional write of `sprk_evidencerefs` only (ref, asOf and clause stay). A third pass writes nothing (monotonic). |
| `Run_T145F1_ASignalOfAPolicySwitchedOffLater_IsStillRedacted_ThoughThePolicyIsNotEvaluated` | An Off or Retired policy is never evaluated, so its Signals reach the redaction only through the nightly not-in-force sweep (`RedactPoliciesNotInForceAsync`). The test fails when the sweep is removed. |
| `Run_T145F1_AWitnessThatStaysInTheSignalsFamily_KeepsItsDetail` | Ordinary, same family: no write, and the detail stays. With a Secure team present, this is also the create-path ordinary control. |
| `Run_T145F1_AFamilyReadFault_LeavesTheStoredLinesUntouchedThatPass_AndTheNextPassClearsThem` | A fault writes nothing (`policyFailures:1`, `SuppressionEvents.Failed` logged); the next pass clears. |
| `Run_T145F1_ASignalChangedSinceTheRead_IsNotOverwritten_AndOnceResolvedIsStillRedactedNextPass` | A person's resolve between read and write wins (`changedSinceRead:1`, nothing written unconditionally). The next pass redacts the now **resolved** Signal. |

uac-r2 F1 / pass 2 F2 (the in-force sweep). Each test stores the witness's detail on an ordinary Signal, re-files the witness
under secure X, and breaks the policy's own pass one way. It then asserts:
- one conditional write of `sprk_evidencerefs` only (ref kept, text and source gone);
- `evidenceRedacted:1`;
- the run still failing.

| Test | Stop path |
|---|---|
| `Run_T145Sweep_AnInvalidRule_StillHasItsStoredEvidenceRedacted` | the validator gate (`invalidPolicies:1`) |
| `Run_T145Sweep_ACurrentVersionNoLongerInForce_…` | `PolicyVersionNotInForce` early return (`policyFailures:1`: the sweep added none) |
| `Run_T145Sweep_NoCurrentVersion_Thrown_…` | thrown "no current version" (caught per policy) |
| `Run_T145Sweep_APartitionSkipInTheSuppressionPass_…` | #1661 skip (one businessunit timeout) |
| `Run_T145Sweep_AFaultPlacingTheHits_…` | `hitPartitions is null` (a timeout after the suppression pass) |
| `Run_T145Sweep_ADismissalFactsReadFailure_…` | `PartitionsUnavailable` from the Decision Record facts read |
| `Run_T145Sweep_APolicyThatReachedItsOwnRedaction_IsNotSweptAgain` | not twice: one write, `policyFailures:0`, and the policy's `sprk_signal` read runs exactly once (uac-r2 re-check F3) |
| `Run_T145Sweep_APolicyWithAnUnparseableKey_CountsItOnce_AndStillRedactsItsParseableSignals` | re-check F1: `unparseableKeys:1`, `policyFailures:1`, one metric, one log line; the parseable Signal is redacted |
| `Run_T145Sweep_ASweepReadFailureForAPolicyThatAlreadyFailed_IsNotCountedTwice` | re-check F1: both reads fault, `policyFailures:1` |

Mutation: dropping the in-force sweep turns all six stop-path tests red. Removing the sweep's skip guard turns the not-twice
test red (2 reads instead of 1). Before re-check F3 it asserted only the write count, which could not see the second pass.

Re-check F1:
- the two new tests, and the tightened `Run_PolicyWithAKeyThatIsNotEpisodeScoped_…`, fail on the previous job file;
- the tightened test asserts the `DedupeKeyUnparseable` metric is exactly 1 (was `>= 1`), plus `unparseableKeys:1` and
  `policyFailures:1`.

Other items from this round:
- **pass 2 K1:** `SignalSuppressionTests.T145_AWitnessFiledUnderSecureXButStillOwnedByAnOrdinaryTeam_IsInXsFamily_AndGivesNoDetail`.
  This is the re-own window. The partition is `Secure([X])`, not Undetermined, and the detail is cleared. It is red under an
  owner-based family check.
- **uac-r2 K4:** `CoreAncestorStampReconciliationJobTests.ReportOnlyTables_ACommaAlone_HoldsNothing_TheBackfillRelease`.
- **pass 2 audit K:** the ArchTest `SpaarkeRoleAuditReadGuardTests` (3 tests: the scan, must-fire, must-not-fire).
  - It scans the 12 role XMLs in `src/dataverse/solutions/SpaarkeMaster/Roles`.
  - The scan turns red when `prvReadRecordAuditHistory` is added to `Spaarke Core User.xml`.

The job fixture now makes matter M a readable ordinary root (`OrdinaryMatterBesideSecureX`). Without it the walk could not name
M's family, every line was undetermined, and the earlier `Run_T145_…SecureProject…` tests would have passed even for an ordinary
witness. Those two tests use the same fixture now, so their clearing is attributable to X.

Redaction-guard seeding proof. Each mutation was applied alone, then the source was restored:
- no redaction pass (the before-state): 3 F1 tests fail;
- a fault answers "nothing to clear" silently: 1 fails;
- stored lines always keep their detail: 3 fail;
- an unconditional write instead of the version-conditional one: 3 fail.

Mutations killed:
- the suppression filter always keeping detail: 2 tests fail;
- clearing on any secure family: 1 fails;
- keeping detail on a fault: 1 fails;
- the job not calling the filter: 1 fails;
- the backfill marking a typed column as "in stamp": 2 fail.

### Review record

- **Pass 1** (adversarial-reviewer, Opus) found F1 plus three test gaps. Fixed in `b5f0c98d4` and `fcb930525`; the census ArchTest
  was fixed in `1365e9a43`.
- **Scoped re-check of the F1 fix.** It was done in-session, because the concurrent-subagent cap refused a reviewer agent. It
  found one gap: the Signals of Off or Retired policies were never read, so they were never redacted. Fixed in `a13204cd9`
  (`SignalClosureService.PoliciesNotInForceAsync` is extracted, so "not in force" stays one query).
- **Pass 2** (adversarial-reviewer, Opus, launched by the main session on `3835015ed`). It is required for tenant-isolation-class
  work (CLAUDE.md §8.5). Its findings:
  - F2: the same as uac-r2 F1, plus the hit-placement and facts-read paths. Fixed in `a96404372`.
  - F1, audit history: reclassified K by the main session; see K-145-11.
  - K1: a fixture test, `88663a29e`.
  - K2 and K3: §7.
  - K4: handed to 038.
  - K5: `2e2f4ab7b`.
- **uac-r2 re-check of `3835015ed..a8a163f96`.** It closed F1 and K4 and raised two fix-now items:
  - F1 (double counting by the sweep's re-read): `b7d82fae3`.
  - F3 (a not-twice test that proved nothing): `b15f52d6d`.

  K-a was closed by the F1 fix; K-b and K-c are in §7.
- **uac-r2 review of #1874 at `3835015ed`** (changes requested):
  - F1, the in-force sweep: `a96404372`.
  - K4, the comma release test: `10bba2fe1`.
  - The #1871 premise correction: §6.
  - K1 to K3 accepted: §7.

## 5. Component justification (CLAUDE.md §11)

`SignalSuppressionService.EvidenceWithinPartitionAsync` (internal):
1. **Existing:** `PartitionsAsync` and `ReadSubjectRowsAsync` in the same service compute families; nothing compares a witness's
   family to its Signal's.
2. **Extension:** it *is* an extension of the suppression service, the owner of partition truth, and adds no new class,
   interface or DI registration. Putting it in the writer would duplicate the partition walk.
3. **Cost of doing nothing:** a secure communication's subject and sender persist on an ordinary matter's Signal (#1747 e).

`WitnessOf` is a private parser for that method. `scripts/Invoke-OntologyStampBackfill.ps1`:
1. **Existing:** the 144 job does the write; nothing reports the backfill set before release.
2. **Extension:** the job's report-only log has no per-table backfill counts and needs App Insights access, so the job cannot be
   extended to produce this report.
3. **Cost of doing nothing:** the owner cannot approve the backfill on a report (POML step 3).

`SignalSuppressionService.StoredEvidenceToRedactAsync` (internal), with the private helpers `WitnessFamiliesAsync` and `KeepsDetail`
factored out of `EvidenceWithinPartitionAsync`:
1. **Existing:** the create-time filter covers only rows being created.
2. **Extension:** it extends the same service and shares its helpers; there is no new class, interface or DI registration.
3. **Cost of doing nothing:** a Signal created before its witness moved into a secure family keeps that family's subject and
   sender (review F1).

`SignalClosureService.PoliciesNotInForceAsync` (internal) is extracted from `ClosePoliciesNotInForceAsync`, not added, so the
query has one owner. The job's `RedactPoliciesNotInForceAsync` (private) exists because the Signals of an Off or Retired policy are
otherwise never read again (cost of doing nothing: their stored detail is never redacted).

`SignalWriter.RedactEvidenceAsync`, the failure reason `OntologyWriterFailureReason.SignalEvidenceRedactFailed` and the run
counter `evidenceRedacted`:
1. **Existing:** the writer is the one owner of `sprk_signal` writes; `MarkClearedObservedAsync` is the conditional-write
   pattern copied.
2. **Extension:** neither existing write can be extended to cover this: they write other columns with other semantics, and
   reusing `signal_close_failed` would meter a redaction failure as a close.
3. **Cost of doing nothing:** the redaction would need an unconditional write (clobbering a concurrent resolve) or a second
   writer of `sprk_signal`.

Published size: the measurement is in the PR body.

## 6. Review F1: evidence is written at create only — DECIDED: F1 → A in 145, B in 038; DR snapshot not a leak (corrected)

Adversarial review pass 1 (2026-10-14) found the following.
- `SignalWriter` writes `sprk_evidencerefs` only on create, so the create-time filter did not protect an existing Signal whose
  witness later moved into a secure family, or whose own family moved.
- A fault stripped a create's detail permanently.
- The Decision Record's `sprk_factsnapshot` freezes the evidence on an append-only record.

**Main-session decision (2026-10-14): F1 → A in 145, B in 038, DR snapshot deferred to the owner.**
- **A (done here):** the stored-evidence redaction pass (§2). On a fault it leaves the lines untouched for that pass (defer, never
  widen).
- **B:** the read-time filter belongs to task 038's Signal read route. 038's agent owns it; this task does not touch 038's code.
- **Decision Record `sprk_factsnapshot`: not a leak (premise corrected by uac-r2; verified in code).** DI-78
  ([#1871](https://github.com/spaarke-dev/spaarke/issues/1871)) is closed as not planned. The snapshot is
  `{facts, decidedBy.role, because}`:
  - `facts` is each resolved Signal's own `sprk_factsnapshot` (`DecisionCommitService.FactValues`, `DecisionRecordWriter.cs:963`);
  - a Signal's fact snapshot is written once from `SignalEvaluator.cs:285-299`. That is the subject's `when`/threshold columns,
    the derived value and the rule's literals; it is the only producer;
  - it never holds evidence `text`/`source`;
  - the wizard's `factSnapshot` (`decisionRecording.ts:101`) is session state, not persisted.

  Pass 1's third point therefore did not hold. The Decision Record writes nothing new.

## 7. Known limits and items for uac-r2

- **K-145-1. The witness ref and date stay on a cleared line.** `sprk_communication:{id}` and its `asOf` (the received date)
  stay without the text, so a reader of the Signal learns that a witness exists and when it arrived. The Signal itself already reveals that its predicate held. The line keeps its ref so that
  dated evidence (D-119 asOf/clause) and the drill-through stay intact, and the drill-through is itself access-checked.
- **K-145-2. SignalWriter writes no stamp at create.** This was already a known item in 144. The DR writer stamps at create
  through `CoreAncestorResolver.StampAsync`; a Signal gets its stamp only from the job, once the hold is released. Filed as
  **DI-79** ([#1872](https://github.com/spaarke-dev/spaarke/issues/1872)): C2 parity in SignalWriter's create.
- **K-145-3. A uac-r2 comment.** `SecureChildLineage`'s comment says "typed core lookups stay until ontology task 145's backfill
  … is verified". Per K1 and §1 above, the backfill does not justify retirement: the typed columns are context and stay in
  lineage unless uac-r2 re-proves the premise per column. Please update that comment in uac-r2's lane.
- **K-145-5. A create during a fault is written without detail.** The create-time filter clears every line when the families
  cannot be read. The redaction pass never restores detail, so that Signal stays without it. This errs in the safe direction
  (never wider).
- **K-145-6. Ordinary detail crosses business units.** Ordinary equals ordinary whoever owns the rows, so an ordinary witness
  from another business unit keeps its detail on an ordinary Signal. This is outside D-123 and pre-existing. Filed as **DI-80**
  ([#1873](https://github.com/spaarke-dev/spaarke/issues/1873)).
- **K-145-7. Redaction cost.** Each pass walks the families of every Signal of the policy whose stored evidence still has
  detail. Once cleared, a line is never walked again, but a Signal whose detail stays in its family is walked every pass.
- **K-145-8. uac-r2 K1 (accepted). A family-read fault leaves the detail until the next pass.** The pass writes nothing and
  counts the policy in `PolicyFailures`, so the run fails visibly. The sweep does not retry a policy in the same run.
- **K-145-9. uac-r2 K2 (accepted). If-Match covers only the Signal row.** The redaction is conditional on the Signal's version,
  not on the witness's. A witness re-filed after the families were read is caught by the next nightly pass.
- **K-145-10. uac-r2 K3 (accepted). `ref`, `asOf`, `kind` and `clause` stay on a cleared line.** This is the same as K-145-1.
- **K-145-11. Pass 2 F1, reclassified K by the main session: audit history keeps the redacted text.**
  - Live in spaarkedev1 (2026-10-14): org audit is on, and `sprk_signal.sprk_evidencerefs` and
    `sprk_decisionrecord.sprk_factsnapshot` are column-audited. So the value from before a redaction stays in audit rows.
  - Reading them needs `prvReadRecordAuditHistory`. None of Spaarke Basic User, Spaarke Core User, Spaarke Console User or
    Basic User holds it; only admin-class roles do, and those already read every row.
  - Guard: the ArchTest `SpaarkeRoleAuditReadGuardTests` fails if a source-controlled Spaarke role grants
    `prvReadRecordAuditHistory` or `prvReadAuditSummary`. A role edited live in an environment is outside it.
  - Owner decision (schema): turn off audit on both columns, and whether to purge the existing rows. Filed as **DI-81**
    ([#1879](https://github.com/spaarke-dev/spaarke/issues/1879)).
- **K-145-12. Pass 2 K2. Redaction is permanent, but Undetermined can be temporary.** If a matter is un-secured before uac-r2
  re-owns its children, its Signals are Secure-owned with no root for a while, so they lose legitimate in-family detail for good
  (`KeepsDetail`). This errs safe (never wider).
- **K-145-13. Pass 2 K3. A Signal whose dedupe-key policy code matches no `sprk_policy` is never read by either pass.** That
  happens when the policy is deleted or its code edited in Dataverse. Its stored detail is then never redacted. No BFF path
  renames or deletes policies.
- **K-145-14. uac-r2 re-check K-b. A policy with more than `SignalEvaluator.MaxPages` pages of Signals is never redacted.**
  `ReadPolicySignalsAsync` throws past the page cap, in the policy's pass and in the sweep. That is a policy failure each run
  (visible), but its stored evidence is never checked. It predates 145: the same cap stops the policy's evaluation.
- **K-145-15. uac-r2 re-check K-c. The publish and event-trigger paths have no sweep.**
  - `ReevaluatePolicyAsync` (on publish) and the scoped event-trigger pass run the policy's own redaction only when the pass
    completes.
  - A policy that stops early on those paths relies on the nightly pass, which sweeps. Worst case: until the next nightly run.
- **K-145-16. A bad key on a policy whose own pass did not read its Signals is not counted.** This covers Off/Retired policies
  and in-force policies that stopped before the read.
  - The sweep (re-check F1) skips an unparseable key silently, so such a key is counted only once its policy is evaluated.
  - Its Signal cannot be placed (no readable subject), so it is not redacted either.
  - Before 145, those policies' Signals were not read at all.
- **Pass 2 K4, handed to task 038.** `EvidenceLine.tsx:82-87` renders a redacted line (no `text`) as tier Missing, "No value
  recorded", which misstates a withheld witness as absent. It is not wired to production yet. 038 owns it, with the read-time
  filter B; 145 does not touch it.
- **K-145-4. Stale stamps are not counted.** The dry run counts only *empty* stamps on intermediate-filed rows. A stamp that is
  set but stale needs the subject's root, which is the job's derivation; it appears in the job's report-only log
  (`traces | where message has "[CORE-ANCESTOR-RECON] stale mode=report-only"`).

**uac-r2 should review:**
- `EvidenceWithinPartitionAsync`: its use of `PartitionsAsync` and `ReadSubjectRowsAsync`, and its Undetermined and fault
  handling;
- the stored-evidence redaction pass (`StoredEvidenceToRedactAsync`, `RedactEvidenceAsync`): the same rule as a create, open and
  resolved Signals, monotonic, conditional, untouched on a fault;
- the decision to keep the typed columns as context and in lineage;
- K-145-3;
- the `-Apply` release of the 144 job's hold, including the D-138 report-only review it presupposes.
