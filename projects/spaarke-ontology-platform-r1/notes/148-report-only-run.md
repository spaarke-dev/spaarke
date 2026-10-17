# Task 148: dev report-only run (done: writes on 2026-10-16)

Status 2026-10-16: **run (report-only), sent to uac-r2.** See "Run 2026-10-16" at the end.

- The job ships in the BFF. Dev runs the BFF built from master (D-126). Task 148 is on `docs/ontology-platform-design`, so the
  job is not deployed to `spaarke-bff-dev`, and no run can be taken there yet.
- No Dataverse writes were made on dev for this task (brief: report-only proofs through read-only queries only).

## How to take the run once deployed

1. Leave `Ontology:TodoAssigneeOwner:WritesEnabled` unset (report-only is the default; only the exact string `true` writes).
2. Trigger a full pass from the scheduler admin surface: job `todo-assignee-owner-reconciliation`, parameter `pass=full`.
   Without it, the first run at or after 02:00 UTC takes the nightly pass.
3. Read the run's `ResultJson` (and the `[TODO-ASSIGNEE-OWNER]` heartbeat line). Record here:
   - `status`, `fullPass`, `scanned`, `stale`;
   - the skip counters (`skippedCored`, `skippedSecure`, `skippedRefused`, `configurationRefusals`, `assigneeTeamRefusals`, `skippedUserOwned`);
   - the own-team counters (`fallbackNoAssignee`, `fallbackAmbiguous`, `fallbackDisabled`, `filedNonCore`);
   - the read count against the shared identity (review K1: a full pass costs about 2-6 reads per distinct To Do);
   - `unverified`, `scanFailures`, `truncated`;
   - `sampleStale` (`table:id:fromTeam->toTeam`).
4. uac-r2 reviews this report. Only then does the owner decide whether to set `WritesEnabled=true`.

## Live gate still open (POML gate, UAC 183 K2)

When the job first runs with writes on, its server gate must show one stale-ETag 412 → `Conflict` on `sprk_signal` and one on
`sprk_decisionrecord`. That proves Dataverse honours If-Match on an owner-only PATCH. It needs writes, so it needs the owner's
approval and F2 decided: business-unit default teams lack Read on `sprk_signal` (0x80040299). Until then, every re-own is
expected to come back `accessDenied`.


## Run 2026-10-16 (dev, BFF `1.0.0+eb5ba4011`, R3 + master `7315b3e85`)

Prerequisite: D-146 enabled the five policies at 01:56:00Z; manual `signal-reevaluation` run `dd1ebeec` (01:56:07–01:57:50Z) wrote 87 Signals (hits 87, refused 0). App settings: `Ontology__TodoAssigneeOwner__WritesEnabled` unset; `CoreAncestor__StampReconciliation__ReportOnlyTables` absent (default `sprk_signal,sprk_decisionrecord`). Both jobs ran on the scheduled 02:00Z tick (the admin trigger passes no parameters, so `pass=full` comes from the first run at or after 02:00 UTC).

**148 `todo-assignee-owner-reconciliation`** (02:00:00–02:00:14Z, Succeeded): mode report-only, fullPass true, scanned 43, **stale 0**, reowned 0, alreadyApplied 11, skippedCored 32, filedNonCore 3, fallbackNoAssignee 6; skippedSecure / skippedRefused / configurationRefusals / assigneeTeamRefusals / skippedUserOwned / accessDenied / unverified / scanFailures all 0; truncated false; sampleStale [].

**144 `core-ancestor-stamp-reconciliation`** (02:00:00–02:00:11Z, Succeeded): mode write with Signal and DR report-only; scanned 130, **stale 51 = reportedOnly 51**, repaired 0, failures 0, truncated false. All 51 are new Signals whose live `sprk_regarding{matter,project}` stamp the writer leaves to the restamper by design (D-131). Log join (`[CORE-ANCESTOR-RECON] stale`, correlationId `fd6e39fc52d347ada6f6d48652bc09c7`) against each Signal's `sprk_corerecordid`/type: **51/51 proposed stamps equal the core record** (44 matter, 6 project, 1 matter + the subject's project). Not stale: 4 Decide, 19 work-assignment core, 13 no core. Owners are not touched by this job.

Owner baseline after the evaluator pass (for uac-r2): 87 Signals, all open, all team-owned (BU1 and root default teams), 0 user-owned, 0 Secure Record Owners.

Next: uac-r2 review → (CLEAN) coordinate the app-setting change with R3 (owns the BFF line) → `ReportOnlyTables=","`, `WritesEnabled=true` → read the next runs.

### Writes enabled (2026-10-16)

uac-r2 reviewed the report-only run CLEAN. Under D-138, and with the owner's "yes approve" on 2026-10-15, the main session set `CoreAncestor__StampReconciliation__ReportOnlyTables=","` and `Ontology__TodoAssigneeOwner__WritesEnabled=true` at 02:26:42Z. R3, word-add-in and uac-r2 were notified before and after.
- Restart: "Application started" at 02:27:12Z; healthz 200 at 02:28:00Z.
- Writer MI `69040982-…` unchanged. Code unchanged: the live ProductVersion is `1.0.0+eb5ba4011844bcaf327af9cc6aec8ced5e33dc9a`, read through Kudu VFS.

**Runs after enabling**
- 144 at 02:30: repaired 50, failures 0.
- 144 at 02:35: scanned 130, stale 0, repaired 0.
- 148: stale 0.
- Nightly `signal-reevaluation` at 02:30: hits 87, written 0, unchanged 87 (no unchanged rows stamped, D-61).

uac-r2 post-enable check: **CLEAN**.

**Why 50 and not 51:** Signal `cb6f5be3` left the stale set between the 02:10 and 02:15 report-only runs.
- A write to its To Do `af1ba427` at 02:14:33Z triggered the live restamper cascade (`[CORE-ANCESTOR-RESTAMP] examined=2 changed=1`, mi-bff-api-dev).
- That cascade stamped `sprk_regardingmatter=00aef0ab`, the value the report-only pass had proposed.

**Note:** `ReportOnlyTables` gates only the reconciliation job. The live restamper writes Signal and DR stamps regardless (D-131, by design), so "report-only" never meant "no stamp writes on these tables".
