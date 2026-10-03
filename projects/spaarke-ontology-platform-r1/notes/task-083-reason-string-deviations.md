# Task 083 — Association `reason` string repair: deviations from the POML

> Recorded per task 083 step 5 / project CLAUDE.md §8 ("Any deviation recorded in `notes/`").

## 1. The POML's `<relevant-files>` / `<outputs>` paths were stale

The POML names `src/server/api/Sprk.Bff.Api/Services/RecordMatching` (code) and
`tests/unit/Sprk.Bff.Api.Tests/Services/RecordMatching` (test) as the files to touch. Neither contains the bug.
`RecordMatching/RecordMatchService.cs` is an unrelated record-search-suggestion service — its `CalculateConfidenceScore`
builds `MatchReasons` strings but never emits the `"Reinforced confidence … in […, …)"` phrasing spec FR-44 quotes.

Repo-wide grep for the literal phrase `Reinforced confidence` returns exactly one file:
`src/server/api/Sprk.Bff.Api/Services/Communication/Engine/AssociationStatusMapper.cs` (lines 382–383 before the
fix). That is the Association Engine's confidence→status ladder (R4 FR-11), not record matching. Its paired test
file is `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/AssociationStatusMapperTests.cs`. Both the code fix
and the new tests landed there instead.

## 2. The fix is not a pure variable swap

The POML's step 2 ("Interpolate the same variable the band was computed from") reads as a one-line substitution.
On inspection that doesn't work: `BuildSuggestedReason(topDet, topFull, aiInvolved, settings)`'s band-membership
guarantee (`SuggestFloor ≤ x < Threshold`) was never actually tied to a single variable consistently — the
surrounding `if` conditions test combinations of `topDet` and `topFull`, and `topDet` has no guaranteed lower
bound (it can be `0` while `topFull` is high, e.g. a rung-2/3-only match under C-1 narrowing). Printing `topDet`
in place of `topFull` in the generic fallback would have swapped one false band claim (`0.97` not in
`[0.50, 0.85)`) for a different one (`0.00` not in `[0.50, 0.85)` either, since `0.00 < SuggestFloor`).

Proof that `topDet ≤ topFull` always (so the old bug could only ever be "`topFull` escaped upward," never the
reverse): for a given field's winning target, `DeterministicConfidence` noisy-ORs a *subset* of the rungs that
`FullConfidence` noisy-ORs, and noisy-OR is monotonically non-decreasing as rungs are added. So
`topDet = max_f det_f(winner_f) ≤ max_f full_f(winner_f) = topFull` for every field `f`.

The actual fix adds the missing branch for exactly the case the old code mishandled —
`topDet < threshold && topFull ≥ threshold && !aiInvolved` (full confidence cleared the threshold via a rung
outside the auto-file-eligible set: C-1-narrowed rung 2/3, or a surface-only rung like `RecordNameMatch` /
`ContactNameMatch` / `Affinity`) — with a message that states what was actually tested (`≥ threshold`), not a
band claim. This makes the generic fallback's band claim provably true in every remaining case, rather than
relabeling which variable gets to be wrong.

One existing test, `Decide_ParticipantCorrelationSubstantiveMatch_AtOrAboveThreshold_LandsSuggested_NotResolved`,
already exercised this exact scenario (confidence `0.90` ≥ threshold `0.85` via `ParticipantCorrelation` alone)
but asserted only `Status` / `AutoFiled` / `RegardingWrites` — never `Reason` — so the false band claim shipped
unnoticed. The new tests close that gap.

## 3. `current-task.md` was not updated

`projects/spaarke-ontology-platform-r1/current-task.md` is a single shared file, and six other agents are
concurrently executing parallel-group-H / other-wave tasks (010, 011, 012, 020, 023, 082) in this same worktree.
The file's "Quick Recovery" currently points at task 001 as the next action; task-execute Step 2 would normally
overwrite that with this task's own state, but doing so risks clobbering whichever other agent's checkpoint
lands next (or being clobbered itself). Completion for this task is recorded in the two artifacts project
CLAUDE.md §8 actually treats as load-bearing: the `TASK-INDEX.md` marker and the POML's own `<status>` element
— both updated as part of this task's completion, verified in agreement via
`scripts/check-task-status-drift.ps1`.

## 4. Quality gates ran unconditionally despite STANDARD rigor

The POML declares `<rigor>STANDARD</rigor>`, which would normally skip Step 9.5. Because this task modifies a
file under `tests/**` (`AssociationStatusMapperTests.cs`), root CLAUDE.md §8's TEST-MODIFYING override applies:
code-review and adr-check are mandatory regardless of declared rigor. Both ran (inline, this session) against
`AssociationStatusMapper.cs` and `AssociationStatusMapperTests.cs`. Result: 0 Critical, 0 blocking Warnings. One
informational, pre-existing, out-of-scope note: `AssociationStatusMapperTests.cs` lives at
`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/`, not under the ADR-038 canonical `tests/unit/domain/**`
path for pure-domain-logic unit tests — a structural mismatch that predates this task (all 35 pre-existing test
methods in the file already live there) and is well outside this task's narrow scope to relocate.
