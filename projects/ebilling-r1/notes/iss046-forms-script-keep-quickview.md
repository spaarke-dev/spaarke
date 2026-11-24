# ISS-046 (#3001): Deploy-EbillingFormsViews.ps1 Section mode must keep cells it does not own

## Defect

In Section mode (`sprk_invoice`, `sprk_timekeeper`, `sprk_billingevent`) the script built `section_ebilling` from the spec's `Cols` and
replaced the whole existing section. The invoice main form's `section_ebilling` also holds the `qv_billingexport` "Batch" quick view
(task 151e, in the SpaarkeMaster source `{93aa1c69-0406-f111-8406-7c1e525abd8b}.xml`; no script creates it). A re-run on an environment
that has the quick view deleted it, with no warning. Task 194 hit this and worked around it with a separate script
(`Remove-EbillingInvoiceExtractionFormControls.ps1`).

## What changed (`scripts/ebilling/Deploy-EbillingFormsViews.ps1`)

- `Get-SpecCells` (new): the spec section's field cells in spec order (`Col`, `Wide`, `Xml`). The read-only / wide / organization-owned
  logic that was inline in `New-SectionXml` now lives here once.
- `New-RowsXml` (new): the two-cells-to-a-row layout (a Memo alone in its row). `New-SectionXml` is now `Get-SpecCells` + `New-RowsXml`.
  Replace mode and the "section does not exist yet" path are unchanged: the output of `New-SectionXml` for every spec section was
  compared old vs new on fake metadata covering every attribute type and is byte-identical (50,910 characters each).
- `Merge-SpecCells`, `Get-FieldCells`, `Test-FieldCellInStep` (new): Section mode on an EXISTING section now merges.
  - A spec field is "present" only if the section has a cell whose control has `id` = `datafieldname` = the column. The quick view
    (`id="qv_billingexport"`, `datafieldname="sprk_billingexport"`) is bound to the same column as the plain Export Batch lookup, so
    matching on `datafieldname` alone would have treated the quick view as the lookup and then "updated" it into one. It is never matched.
  - Present and in step (control class, disabled flag, cell label, colspan/rowspan, compared on those attributes, label case-strict, not on
    the whole text so a Dataverse re-serialised form still counts as in step): left alone. Present and different: replaced where it stands.
  - Missing: appended in new rows at the end of the section (two cells to a row, Memo alone). No existing row is moved or edited.
  - Everything else in the section (quick views, subgrids, web resources, spacer rows, controls the spec does not list) is untouched.
- The read-only sweep over controls outside the section, the "no section yet: append it" path, `-WhatIf`, `-FormsOnly`, parameters, output
  lines, the pending-collateral refusal and the `Invoke-Dv` calls (all via `EbillingSchema.psm1`, `Consistency: Strong`) are unchanged.
  No new Dataverse call was added.
- Header comment updated: the section is merged, and retiring a control is not this script's job.

Not part of this change: task 194's "shared safety checks" (case-strict pending-edit comparison, read-back after each PATCH) are in
`Remove-EbillingInvoiceExtractionFormControls.ps1`, not in this script, and were left alone. This script's own guard is the D-103
pending-collateral refusal, also untouched.

## Tests (`tests/scripts/Deploy-EbillingFormsViews.Tests.ps1`, Pester 5.7.1)

New `Describe 'Section mode keeps the cells the spec does not own (ISS-046)'`: loads every top-level function of the script from its
syntax tree and drives `Sync-Form` against a fake `Invoke-Dv` (one form row; GET returns it, PATCH stores it and is counted). No
environment, no network.

1. quick view kept, byte for byte, at the same row index, when the run has other work (a missing spec cell) and writes
2. a form that already has the quick view and every spec cell: no PATCH, no change recorded, nothing touched (so no publish)
3. a run that writes, then a second run on its output: the second writes nothing, quick view still there
4. a missing spec field cell is added (guard: the old script also did this)
5. the Export Batch lookup is added when only the same-column quick view is present; the quick view is left alone
6. a spec cell that differs (a total not disabled; a stale label) is updated in place, control order unchanged
7. no spec field duplicated across two runs, quick view included
8. subgrid, web resource and an unlisted control in the section survive
9. `-WhatIf`: no PATCH, a `WOULD ...` change recorded (guard)
10. no `section_ebilling` yet: the whole section is added (guard)
11. the real SpaarkeMaster invoice main form: metadata derived from its own cells, run, quick view cell byte-identical to source, each spec
    field exactly once

### Fail-before / pass-after

| Run | Passed | Failed | Total |
|---|---|---|---|
| Old script (`git show HEAD:` copy put in place, same test file) | 10 | 8 | 18 |
| New script | 18 | 0 | 18 |

Failing on the old script: tests 1, 2, 3, 5, 6, 7, 8, 11. Passing on both (regression guards): the 7 existing spec tests, 4, 9, 10.
The new script was restored afterwards and the 18 re-run green.

ArchTest guard: `dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~ScriptMetadataStrongConsistency" -- xUnit.MaxParallelThreads=4`:
2 passed, 0 failed.

## Found in passing (not fixed)

- Behaviour change to know: because the section is no longer replaced, a control the spec stops listing is no longer dropped by a re-run.
  Task 194 relied on the old whole-section rewrite to drop `sprk_extractionstatus` from dev; that is now done only by
  `Remove-EbillingInvoiceExtractionFormControls.ps1` (which it already said is the only supported way). A future retirement needs that
  kind of script, or a `Retire` list in the spec; not added here (no current need).
- The present-check is per section. A spec field that sits on the form outside `section_ebilling` would still be added to the section
  (same as before); only a duplicate inside the section is prevented.
- If a section already holds two plain cells for one spec column, only the first is checked and updated; the second is left alone.
- `Sync-Form` Replace mode still replaces the whole form (by design, for stub forms); a Replace-mode table whose form later gains a
  hand-made cell would lose it the same way. None does today.
- The script has no read-back after its `PATCH` (task 194's script does); out of scope here.
