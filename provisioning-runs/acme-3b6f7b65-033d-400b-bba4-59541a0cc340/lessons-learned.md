# Lessons Learned — acme-3b6f7b65-033d-400b-bba4-59541a0cc340

> **MANDATORY** — written by `/provision-environment` Step 7. Drafted by Claude for the operator from the run record, the
> L2 Worker's telemetry, `notes/defer-issues.md` (ISS-030–ISS-036) and the handoff report
> `runs/3b6f7b65-033d-400b-bba4-59541a0cc340.md`.

- **Outcome**: Completed 2026-11-23 14:01:48 UTC (started 2026-11-20 18:25:00 UTC; 2 d 19 h 37 min wall clock)
- **Operator**: `ralph.schroeder@spaarke.com`
- **Customer / kind**: acme, Model 1 dedicated stamp — the first UPGRADE run of an existing stamp (SpaarkeMaster managed
  1.3.0.0 → 1.3.2.1; BFF pinned to the build it already ran)

## What went right

- **Fail-closed held at every stop**: H2b, H6 and H9 each stopped with production untouched or rolled back (H6's
  1.3.2.0 import rolled back cleanly 11-21 22:28; ACME kept 1.3.0.0 and a healthy BFF), and every handler re-ran
  idempotently after its fix (H2b 11-21 21:49:50, H6 11-22 16:44:15, H9 11-22 19:22:05).
- **Upgrade semantics work end to end**: H6 managed stage-and-upgrade applied 1.3.2.1 (incl. the five Core User removals on
  `sprk_invoicecorrection`, verified after the run); H10 moved the ontology writer into its own unit (task 267 K2, live);
  H4b rewrote the app settings incl. `AiSearch__AllowedIndexes`; H7b procedure 10 ran S24–S28.
- **H13 is a real acceptance, not a formality**: on 11-23 it ran every check in 40 s — 14 live checks, T1–T7, I2–I5, ARM
  keyless on ten resources, cost (−13.8 %), census Isolated, mailbox — and wrote the registry itself (Step 6a found
  nothing to reconcile).
- **The fix loop stayed disciplined**: each defect got a PR with must-fire tests, every check green, owner go, then a dev
  L2 deploy from the merge and a resume — no hand-patching of the stamp.
- **Step 0.5 prerequisites**: all PASS except PRQ-C-03, which fails by design on an upgrade (L1 below). No prerequisite
  was skipped.

## What went wrong

### Lesson L01 — PRQ-C-03 fails on every upgrade run

- **Symptom**: global name availability failed for the four stamp names.
- **Root cause**: the names belong to ACME's own resources in `rg-spaarke-acme-prod`; the recipe assumes a fresh stamp.
- **Fix applied**: no — deferred, ISS-030 #2925.
- **Landing spot**: `scripts/provisioning-prereqs/prereqs.yaml` PRQ-C-03 recipe (accept names owned by the customer's RG).
- **Blocks future runs**: conditional-on an upgrade run (operator must read past one FAIL).
- **Punch-list class**: A

### Lesson L02 — No upgrade run can start from a Ready registry row

- **Symptom**: first `POST /api/runs` → 400 `registry-setup-status-not-in-progress`.
- **Root cause**: CreateRun (REG-07) accepts only an InProgress row; nothing in the skill or L2 moves a Ready row back.
- **Fix applied**: no — the row was set 2 → 1 by hand with owner OK; deferred, ISS-031 #2926.
- **Landing spot**: L2 CreateRun (upgrade mode) or skill Step 1f.
- **Blocks future runs**: yes — every upgrade run needs the manual reset.
- **Punch-list class**: B

### Lesson L03 — The intake schema cannot pin the BFF build

- **Symptom**: `buildId` had to be added to the POST outside the schema-valid intake (a recorded deviation).
- **Root cause**: `IntakeParameterCatalog` accepts `buildId`; `intake.schema.json` does not list it.
- **Fix applied**: no — deferred, ISS-036 #2987.
- **Landing spot**: `scripts/provisioning-prereqs/intake.schema.json` + a catalog/schema parity test.
- **Blocks future runs**: conditional-on an upgrade that must not move the BFF.
- **Punch-list class**: A

### Lesson L04 — H2b treated an index update's HTTP 204 as a failure

- **Symptom**: quarantined at H2b 11-20 18:27 (`PUT index … returned HTTP 204`).
- **Root cause**: `SearchIndexClientProvisioner` accepted only 200/201; AI Search answers 204 to an update of an existing index — so every re-run of an existing stamp failed there.
- **Fix applied**: yes — PR #2924 (`aff1133e55`), test fails before / passes after.
- **Landing spot**: L2 H2b.
- **Blocks future runs**: no (fixed).
- **Punch-list class**: B

### Lesson L05 — A published SpaarkeMaster carried a dangling web-resource dependency

- **Symptom**: H6 quarantined 11-21 22:28: `WebResource (Id=sprk_invoicefiles) does not exist … SystemForm 93aa1c69` (Invoice main form); import rolled back.
- **Root cause**: ebilling task 151e put a `WebResource_invoicefiles` control on the form but the code page was never added to SpaarkeMaster; every package since 1.3.1.0 carried it, and no stamp had imported one.
- **Fix applied**: yes — ebilling PR #2935, SpaarkeMaster 1.3.2.1.
- **Landing spot**: SpaarkeMaster assembly (a dependency check at export/publish would catch the class).
- **Blocks future runs**: no (fixed for this package).
- **Punch-list class**: B

### Lesson L06 — H6 misjudges a long upgrade

- **Symptom**: the 1.3.2.1 InlineUpgrade took 73 min against H6's 60-min timeout; H6 had no in-flight check and trusted the async-op status over the import result.
- **Root cause**: fixed wait shorter than the Azure/Dataverse operation.
- **Fix applied**: no — deferred, ISS-032 #2942 (the run was resumed by owner go after the import completed).
- **Landing spot**: L2 H6.
- **Blocks future runs**: conditional-on an import longer than 60 min.
- **Punch-list class**: B

### Lesson L07 — H9 gave up on a slot swap that was still running

- **Symptom**: H9 failed 11-22; the swap took ~11 min against a 10-min `SlotSwapTimeout`, and the immediate retry hit 409 "prepared for swap".
- **Root cause**: a swap restarts and warms staging with production's settings; the BFF needs ~220 s to start on S1.
- **Fix applied**: yes — PR #2956 (`c39114e7c6`): 30-min timeout, the slot guard waits out a swap in flight, honest classification.
- **Landing spot**: L2 H9.
- **Blocks future runs**: no (fixed).
- **Punch-list class**: B

### Lesson L08 — H13 judged a saturated BFF on one 90-second call

- **Symptom**: H13 failed twice on 11-22 (`no answer within 00:01:30 (KeylessProofTimeout)`) against a healthy build.
- **Root cause**: after H9's swap the stamp's single S1 worker, which also runs the staging slot, stayed at 85–97 % CPU for ~45 min (24–30 % before); H13 made one attempt.
- **Fix applied**: yes — PR #2980 (`c4d7da8d57`): retry with 30 s → 5 min waits for up to 40 min on no-verdict only; never on a failure, an earlier failed check or a 404. Stamp sizing left to the owner (NFR-04 cost envelope).
- **Landing spot**: L2 H13.
- **Blocks future runs**: no (fixed).
- **Punch-list class**: B

### Lesson L09 — A completed run still shows open gates

- **Symptom**: `Completed` with five `Pending` gates (`h2b-index-provisioning-failed`, `h6-import-timeout`, two H9, `h13-h13-extended-validation-inconclusive`) and H13 in `failedHandlers`.
- **Root cause**: failure paths write `{handler}-{rejectionCode}` gates as Pending; success never clears them.
- **Fix applied**: no — deferred, ISS-035 #2986.
- **Landing spot**: L2 handlers' failure-gate writes / dispatcher success path.
- **Blocks future runs**: no — misleading only.
- **Punch-list class**: C

## New prereqs to codify

- PRQ-C-03 upgrade-mode exemption (L01).

## New patterns to add

- "A fixed wait shorter than the Azure operation, then a retry into the operation still in flight" recurred three times
  (H6 L06, H9 L07, H13 L08). Pattern for `.claude/patterns/provisioning/`: size every handler wait from the operation's
  observed duration on the smallest stamp SKU, and make the retry check for the operation still running.

## Recommendations for next run

- Before an upgrade run: reset the registry row (until ISS-031), expect PRQ-C-03 to fail (until ISS-030), and pin
  `buildId` in the POST (until ISS-036).
- Owner decision: stamp plan sizing after deploy (S1 shared by both slots saturates for ~45 min).
- Run Step 6e's probe items with owner OK at the same sitting as the run, so the handoff can certify secure-record ready.

## Cross-run pattern

- First observed: run b6941254 (L45, the Kudu deploy timeout); this run: three more instances (L06, L07, L08).
  Recommended promotion: the pattern above, plus a review checklist item for every new handler wait.

## Sign-off

- Author: Claude (drafted for the operator)
- Reviewer: pending — ralph.schroeder@spaarke.com
- git-sha: c4d7da8d57 (master with every fix used by this run)
