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
| Pester `Invoke-OntologyStampBackfill.Tests.ps1` (8) | the backfill classification, with must-fire and must-not-fire cases | new |

The existing `Run_PathBHit_WritesOneWitnessLinePerExistsClause…` stays as the ordinary control.

Review F1 (option A), through the job's real write path:

| Test | Proves |
|---|---|
| `Run_T145F1_AWitnessRefiledIntoSecureXAfterTheSignalWasCreated_LosesItsDetailOnTheNextPass_Once` | Created with the detail; after a re-file into secure X the next pass makes one conditional write of `sprk_evidencerefs` only (ref, asOf and clause stay). A third pass writes nothing (monotonic). |
| `Run_T145F1_ASignalOfAPolicySwitchedOffLater_IsStillRedacted_ThoughThePolicyIsNotEvaluated` | An Off or Retired policy is never evaluated, so its Signals reach the redaction only through the nightly not-in-force sweep (`RedactPoliciesNotInForceAsync`). The test fails when the sweep is removed. |
| `Run_T145F1_AWitnessThatStaysInTheSignalsFamily_KeepsItsDetail` | Ordinary, same family: no write, and the detail stays. With a Secure team present, this is also the create-path ordinary control. |
| `Run_T145F1_AFamilyReadFault_LeavesTheStoredLinesUntouchedThatPass_AndTheNextPassClearsThem` | A fault writes nothing (`policyFailures:1`, `SuppressionEvents.Failed` logged); the next pass clears. |
| `Run_T145F1_ASignalChangedSinceTheRead_IsNotOverwritten_AndOnceResolvedIsStillRedactedNextPass` | A person's resolve between read and write wins (`changedSinceRead:1`, nothing written unconditionally). The next pass redacts the now **resolved** Signal. |

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
- **Pass 2 is REQUIRED** because this is tenant-isolation-class work (the D-123 secure-family wall; CLAUDE.md §8.5). It was NOT
  run: the concurrent-subagent cap refused the launch twice. It must run before merge.

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

## 6. Review F1: evidence is written at create only — DECIDED: F1 → A in 145, B in 038, DR snapshot deferred to the owner

Adversarial review pass 1 (2026-10-14) found the following.
- `SignalWriter` writes `sprk_evidencerefs` only on create, so the create-time filter did not protect an existing Signal whose
  witness later moved into a secure family, or whose own family moved.
- A fault stripped a create's detail permanently.
- The Decision Record's `sprk_factsnapshot` freezes the evidence on an append-only record.

**Main-session decision (2026-10-14): F1 → A in 145, B in 038, DR snapshot deferred to the owner.**
- **A (done here):** the stored-evidence redaction pass (§2). On a fault it leaves the lines untouched for that pass (defer, never
  widen).
- **B:** the read-time filter belongs to task 038's Signal read route. 038's agent owns it; this task does not touch 038's code.
- **Decision Record `sprk_factsnapshot`:** unchanged in 145, and nothing is written to existing Decision Records. Deferred to the
  owner as **DI-78** ([#1871](https://github.com/spaarke-dev/spaarke/issues/1871)). The question: redact an append-only Decision
  Record's snapshot by a service write after a re-file, or store communication evidence in Decision Record snapshots as references
  only, without subject and sender.

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
