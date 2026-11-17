# ISS-030 (#2881): a refused totals recalculation after an adjustment removal reactivates the row

Task: `projects/ebilling-r1/tasks/iss030-adjustment-removal-refused-totals-undo.poml`. Files: `Services/Finance/InvoiceAdjustmentService.cs`, `tests/integration/data-mutation/Ebilling/InvoiceAdjustmentServiceTests.RemovalRefusedTotals.cs`.

## The change

`ApplyRemoveAsync`: when the recalculation after the deactivation `IsRefused`:

- **Repeat whose removal record exists** (`alreadyRemoved && !writeColumn`): the removal finished; it is removed for good. The refusal is answered, nothing is written (the add path's "a repeat is not this one's to undo" rule).
- **Otherwise** (this request deactivated the row, or a repeat found the row inactive with no removal record): `UndoRemovalAsync` reactivates the row, then answers the refusal.

`UndoRemovalAsync` writes `ReactivationFields`: `statecode` Active (0) and `statuscode` Active (1) together (reverse of `DeactivationFields`).

- **Remover stamp.** On the request that deactivated the row, `sprk_reviewedby` and `sprk_reviewedon` go back to the values read before the deactivation (a cleared value, `DBNull`, when the row held none). This is the add path's own treatment of those fields (the add stamps them; the removal overwrites them; the undo restores them).
- **Write mode.** A plain `UpdateAsync` on `CancellationToken.None`, as the add undo is. The row-version batch update returns no new version, so a conditional reactivation is not possible without a re-read, which would not be atomic with the deactivation either. The invoice lock is held for the whole request, so the only competing writer is one outside the BFF.
- **Failed reactivation.** 503 `incomplete`, step `undo`, with its own wording ("could not be put back"), never "totals were not updated". Nothing else is written.
- **No Decision Record, no note.** The refused calculation writes no totals, so the note column is untouched; the record step is never reached.

## Distinguishing "refused and not undone" from "removed for good" on a repeat

After a failed undo the row is inactive, stamped with the remover, with no removal record. That is the same row state as a removal whose totals succeeded but whose record has not been written yet. The discriminator used is the Decision Record: the removal's record is written only after the totals succeed, so
- record exists: removal complete, never undone;
- no record, repeat finds the row inactive: recalculate. Still refused: undo (as the first request would have). Calculation now works: the repeat finishes the removal (totals, note, one record) as before.

The repeat's existing same-reviewer check still applies (another reviewer gets 409 `id_reused` and the row is untouched).

## Tests (6, all in the new partial; `DisplayName` starts "ISS-030")

| Test | Proves |
|---|---|
| AC1 | Refused removal: row Active (state and status), prior stamp restored, NegativeTotal answered, no record, no note, no totals write, 2 row writes |
| no prior stamp | A row with no stamp has the remover's stamp cleared (`DBNull`) |
| AC3 | Failed undo: 503 `incomplete` with the undo wording, row still inactive, no record; the same request repeated reactivates and answers the refusal |
| repeat with totals restored | After a failed undo, once the totals calculate again the repeat finishes the removal: one record, note written, row stays removed |
| AC2 must-not-fire | A repeat of an earlier SUCCESSFUL removal (record exists) now refused: row stays inactive, stamp untouched, no row write |
| other reviewer | After a failed undo, a repeat by another reviewer is 409 `id_reused`, row untouched |

Fail-before (old service, same tests): 5 failed, 1 passed (the must-not-fire test passes before and after by design).

## Known limits

1. **Repeat undo cannot restore the stamp.** On a repeat after a failed undo, the first request's overwrite of `sprk_reviewedby` / `sprk_reviewedon` is not recoverable, so the repeat reactivates the row with the remover's stamp left on it (no stamp is written). The row's own creator person is a different column kind and is not used.
2. **Record-step strand plus a later refusal.** A removal whose totals succeeded but whose record write failed (503 `record`), followed by an outside change that makes the totals refuse before the repeat, is indistinguishable (no record yet) from a refused-and-not-undone removal, so that repeat reactivates the row. The invoice's stored totals then still exclude the row until the next successful recalculation. Narrow (a removal raises the total, so the refusal needs lines changed outside the lock) and deterministic to heal (any later successful recalculation counts the active row). Distinguishing it would need a stored-totals comparison with a hypothetical active row; not done.
3. The reactivation is not conditional on a row version (see above).
