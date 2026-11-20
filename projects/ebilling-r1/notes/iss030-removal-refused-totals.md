# ISS-030 (#2881): a refused totals recalculation after an adjustment removal (or add) is undone

Task: `projects/ebilling-r1/tasks/iss030-adjustment-removal-refused-totals-undo.poml`. Files: `Services/Finance/InvoiceAdjustmentService.cs`, `tests/integration/data-mutation/Ebilling/InvoiceAdjustmentServiceTests.RemovalRefusedTotals.cs`.

## The change

`ApplyRemoveAsync`: when the recalculation after the deactivation `IsRefused`:

- **Repeat whose removal record exists** (`alreadyRemoved && !writeColumn`): the removal finished; it is removed for good. The refusal is answered, nothing is written (the add path's "a repeat is not this one's to undo" rule).
- **Otherwise** (this request deactivated the row, or a repeat found the row inactive with no removal record): `UndoRemovalAsync` reactivates the row, then answers the refusal.

`UndoRemovalAsync` writes `ReactivationFields`: `statecode` Active (0) and `statuscode` Active (1) together (reverse of `DeactivationFields`).

- **Remover stamp.** On the request that deactivated the row, `sprk_reviewedby` and `sprk_reviewedon` go back to the values read before the deactivation (a cleared value, `DBNull`, when the row held none). This is the add path's own treatment of those fields (the add stamps them; the removal overwrites them; the undo restores them).
- **Write mode.** A plain `UpdateAsync` on `CancellationToken.None`, as the add undo is. The row-version batch update returns no new version, so a conditional reactivation is not possible without a re-read, which would not be atomic with the deactivation either. The invoice lock is held for the whole request, so the only competing writer is one outside the BFF.
- **Failed reactivation.** 503 `incomplete`, step `undo`, with its own wording ("could not be put back"), never "totals were not updated". Nothing else is written.
- **No Decision Record, no note.** The refused calculation writes no totals, so the note column is untouched; the record step is never reached. No index job is queued.

## Distinguishing "refused and not undone" from "removed for good" on a repeat

After a failed undo the row is inactive, stamped with the remover, with no removal record. The discriminator used is the Decision Record: the removal's record is written only after the totals succeed, so
- record exists: removal complete, never undone;
- no record, repeat finds the row inactive: recalculate. Still refused: undo (as the first request would have). Calculation now works: the repeat finishes the removal (totals, note, one record) as before.

The repeat's existing same-reviewer check still applies (another reviewer gets 409 `id_reused` and the row is untouched). An undo on a repeat logs a warning (ids only) that an earlier removal with no record was reverted and the stamp is left as it is.

## The add path (review round 1, F1)

The add path had the same gap: after a failed add-undo the row stayed active, and a repeated add answered `Refused` (because `alreadyApplied`) with no add record and totals not counting the line. `FinishAddAsync` now follows the same rule: a refused calculation on a repeat whose `add-adjustment` record exists reports the refusal and writes nothing (the add finished; a record follows the totals); otherwise (first request, or a repeat with no record) it deactivates the row. A failed add-undo is 503 `incomplete` at the `undo` step, worded "could not be taken back", no longer reported as a `totals` step ("saved but the totals were not updated"). A repeat that undoes logs the same kind of warning (ids only).

## Tests (11, all in the new partial; `DisplayName` starts "ISS-030")

Removal:

| Test | Proves |
|---|---|
| AC1 | Refused removal: row Active (state and status), prior stamp restored, NegativeTotal answered, no record, no note, no totals write, no index job, 2 row writes |
| no prior stamp | A row with no stamp has the remover's stamp cleared (`DBNull`) |
| AC3 | Failed undo: 503 `incomplete` with the undo wording, row still inactive, no record; the same request repeated reactivates and answers the refusal; the stamp is the remover's (known limit 1) and the warning is logged |
| repeat with totals restored | After a failed undo, once the totals calculate again the repeat finishes the removal: one record, note written, row stays removed |
| AC2 must-not-fire | A repeat of an earlier SUCCESSFUL removal (record exists) now refused: row stays inactive, stamp untouched, no row write |
| other reviewer | After a failed undo, a repeat by another reviewer is 409 `id_reused`, row untouched |
| repeat, direct state | A row built inactive with no record and the totals refused: the repeat reactivates it, stamp left, warning logged |

Add (F1):

| Test | Proves |
|---|---|
| undo fails | 503 `incomplete`, "could not be taken back", not "saved ... totals were not updated"; row still active; no record |
| AC3 twin | The same add repeated while the refusal holds deactivates the row, answers the refusal, no record, no second create, warning logged |
| totals restored | After a failed add-undo, once the totals calculate again the repeat finishes the add: one `add-adjustment` record |
| must-not-fire | A repeat of a finished add (record exists) with the totals now refused is not undone: row active, no row write |

## Fail-before

- **Round 0** (old service, the first 6 tests): 5 failed, 1 passed (the must-not-fire test passes before and after by design). Not all five failed for the behaviour they name:
  - AC1 and "no prior stamp" fail on the new behaviour itself (the row is not reactivated, the stamp not restored or cleared).
  - AC3 fails on its first assertion, the 503 at the undo step (before the change the undo never ran, so the first request answered the refusal); its repeat half was never reached.
  - "repeat with totals restored" and "other reviewer" failed only at setup: their first request, which expects `incomplete` from the broken undo, did not. They are regression controls for the repeat paths and prove no new behaviour on their own.
- **Round 1** (the round-0 service against the 11 tests): 4 failed, 7 passed. The 4 are the new proofs: repeat on direct state (the repeat-half proof AC3 did not reach, plus the warning), the add undo wording and step, and the add repeat deactivating the row. The add "totals restored" and add must-not-fire tests are controls that pass before and after.

## Review round 1

| # | Finding | Resolution |
|---|---|---|
| F1 | The add path has the same gap (after a failed add-undo a repeat is `Refused`, row active) | Same rule in `FinishAddAsync`; `undo` step and wording; 4 tests |
| 2 | An undo on a repeat is silent | Warning, ids only, in `UndoRemovalAsync` (`priorRow is null`) and in the add path; asserted in the tests |
| 3 | Stale comments: `IsReviewedBy` "nothing else writes"; `Incomplete`'s step list and log template | Rewritten (the undo restores or leaves the stamp; steps `totals`, `record`, `undo`; the template reads "the {Step} step did not finish") |
| 4 | Test gaps | AC1 asserts no index job is queued; AC3 asserts the stamp and the warning after an undo on a repeat; the adder is `PriorAdder`, no longer `Remover`; a direct-state repeat test added; this note says which tests prove what |
| 5 | Known limit 2 | Recorded as K2 below |

## Known limits

1. **Repeat undo cannot restore the stamp.** On a repeat after a failed undo, the first request's overwrite of `sprk_reviewedby` / `sprk_reviewedon` is not recoverable, so the repeat reactivates the row with the remover's stamp left on it (no stamp is written). The row's own creator person is a different column kind and is not used. For the add twin the stamp is the adder's own and is left as it is. Logged as a warning on the repeat.
2. **K2: record-step strand plus a later refusal.** A removal whose totals succeeded but whose record write failed (503 `record`), followed by an outside change that makes the totals refuse before the repeat, is indistinguishable (no record yet) from a refused-and-not-undone removal, so that repeat reactivates the row; the invoice's stored totals then still exclude it until the next successful recalculation. Reviewer's reasoning for keeping it a known limit: it needs a writer outside the BFF (a removal raises the total, so the refusal cannot come from the BFF's own writes under the lock); it fails closed at approval (an invoice whose totals cannot be calculated is not approved); and no cheap way exists to tell the two cases apart (it would need a stored-totals comparison against a hypothetical active row). The add path has the mirror case (an add stranded at the record step, then a later refusal): its repeat deactivates the row, with the same reasoning.
3. The reactivation is not conditional on a row version (the batch update returns no new version; the invoice lock is held).
