# Task 148: dev report-only run (pending)

Status 2026-10-14: **pending, not run.**

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
