# Task 194 - take the retired extraction fields off the invoice forms (ISS-045, #2997)

Status: code and tests done on branch `task/ebilling-194`; the dev form change (-Apply) is NOT run (main session, after review, with the uac-r2 notices).

## What changed

| Item | Change |
|---|---|
| `scripts/ebilling/Deploy-EbillingFormsViews.ps1` | `sprk_extractionstatus` leaves the `sprk_invoice` eBilling `Cols` list. |
| SpaarkeMaster invoice **main** form `{93aa1c69-...}.xml` and `_managed.xml` | The Extraction Status cell is removed from `section_ebilling`. Its row now holds only Confidence (one cell, like the form's other single-cell rows). |
| SpaarkeMaster invoice **quick-create** form `{c5e5807d-...}.xml` and `_managed.xml` | Both cells (Extraction Status, Extracted JSON), their two rows, the now-empty "JSON FILE" section and its third column are removed. The two remaining columns go from 34% / 33% to 50% / 50%. No empty row or section is left. |
| `scripts/ebilling/Remove-EbillingInvoiceExtractionFormControls.ps1` (new) | Guarded path for the LIVE forms (report / `-Apply` / `-Verify`). |
| `tests/scripts/Remove-EbillingInvoiceExtractionFormControls.Tests.ps1` (new) | 38 Pester 5.7.1 tests. |
| `scripts/seed-data/playbooks.json` | PB-013 (Finance Invoice Processing) removed. PB-006's "superseded by PB-013" text now names the eBilling pipeline (050b). |
| `scripts/seed-data/Verify-Finance-Playbook.ps1` | Deleted: its only subject was the PB-013 playbook (`sprk_name eq 'Finance Invoice Processing'`). Nothing called it; it appears only in historical project notes (task 001, defer-issues, x-financial-intelligence-module-r1), left as they are. |
| `scripts/README.md` | Entry for the new script. |

Not touched: the two columns (`Entity.xml`), the `sprk_extractionstatus` option set, `docs/data-model/*` (none of those docs says a form shows these fields; they list the columns, which stay), `FinanceBaseValuesSeamTests`.

## Does Deploy-EbillingFormsViews.ps1 remove controls that are no longer listed?

Yes, for the section it owns. `Sync-Form` in Section mode builds the whole `section_ebilling` from the `Cols` list and `ReplaceChild`s the existing section with it (it adds the section only when none exists). A control left out of the list therefore disappears with the next run. It is additive only for controls OUTSIDE `section_ebilling`: those get read-only flags but are never removed. On dev the live main form shows the control inside `section_ebilling` (the read-only report below), so the next forms-script run would also remove it. The new script does not wait for that: it removes the control from whichever of the two forms still shows it, so the main session need not run the large forms script (which also touches views, sitemap and apps) for this.

## The guarded dev path

`scripts/ebilling/Remove-EbillingInvoiceExtractionFormControls.ps1`, modelled on 193's `Remove-EbillingRateCardCoreLookups.ps1`:

- Default (no switch): READ-ONLY report. Per form: which control it shows, what would be removed, mentions that would remain, then READY / BLOCKED / ALREADY CLEAN. Writes nothing.
- `-Apply` (dev host only; `-WhatIf` is the dry run): edits each main / quick-create form that shows a control, then publishes `sprk_invoice` entity-scoped through `Invoke-PublishXml` (`scripts/lib/Publish-SolutionComponents.ps1`) wrapped in `Invoke-WithTransientRetry`, then verifies. Refuses before the first write (exit 2) when: an expected form ("Invoice main form" type 2, "Invoice quick create form" type 7) is missing or ambiguous; a control is outside a cell / row / section (a header) or would leave a tab without columns; a mention would remain after the clean-up (an event handler); or another pending form / view of `sprk_invoice` would go live with the publish (D-83; `-AllowPendingCollateral` overrides). A form of another type that names a control is reported and left alone.
- `-Verify`: READ-ONLY. Exit 0 only when both forms exist and neither shows either control; exit 1 otherwise.
- Every Dataverse call goes through `EbillingSchema.psm1` (`Invoke-Dv`, `Consistency: Strong`); the script has no REST call of its own. `ScriptMetadataStrongConsistencyGuardTests` stays green.
- Known limit (same as 193): the edit is built from the published text; someone else's unpublished edit on the same form would be overwritten. Run the report first and tell whoever works on the `sprk_invoice` forms.

## Read-only run against dev (2026-11-23, https://spaarkedev1.crm.dynamics.com)

Default mode, exit 0:

```
sprk_invoice main form 'Invoice main form': found
sprk_invoice quick create form 'Invoice quick create form': found
forms showing a retired control : 2
  form 'Invoice quick create form' (c5e5807d-e20c-f111-8342-7c1e520aa4df, type 7): shows sprk_extractionstatus, sprk_extractedjson; would remove control sprk_extractionstatus, an emptied row, control sprk_extractedjson, an emptied row, an emptied section (tab_1_column_3_section_1), an emptied column; mentions left 0
  form 'Invoice main form' (93aa1c69-0406-f111-8406-7c1e525abd8b, type 2): shows sprk_extractionstatus; would remove control sprk_extractionstatus; mentions left 0
other form types naming one (not edited): 0

VERDICT: READY
```

`-Verify` today, exit 1 (as it must while the controls are on the forms): `Verification failed: form 'Invoice quick create form' still names sprk_extractionstatus, sprk_extractedjson; form 'Invoice main form' still names sprk_extractionstatus`.

## Tests

- New `tests/scripts/Remove-EbillingInvoiceExtractionFormControls.Tests.ps1` (Pester 5.7.1): 38 pass, 0 fail. Covers the pure transform, default mode writes nothing, `-Verify` both ways (and per form), `-Apply` happy path / idempotent second run / `-WhatIf` / refusal on an unexpected shape (event handler, header control, missing or ambiguous form) / pending collateral / non-dev host, structure (no REST call of its own, writes only behind `Confirm-Write`), and the repository sources (the four form files and the forms script no longer name the controls; the columns and option set stay; PB-013 gone from the seed).
- Fails before the change: the four source-state tests (forms, Confidence row, forms script, seed) fail on the base commit's files (34 pass / 4 fail), pass after. A mutant that drops the `Confirm-Write` guard and the report-only return fails 4 tests.
- `Remove-EbillingRateCardCoreLookups.Tests.ps1` (5.7.1): 115 pass. `Deploy-EbillingFormsViews.Tests.ps1` (5.7.1): 7 pass.
- ArchTests (`ScriptMetadataStrongConsistency|SpaarkeMaster|Form`): 114 pass, 0 fail.
- `FinanceBaseValuesSeamTests` (compiled into `tests/unit/Sprk.Bff.Api.Tests`): 2 pass, unchanged.

## Commands for the main session (after review, with the uac-r2 notices)

```powershell
# 1. Re-read the live state (read-only), and the dry run
pwsh scripts/ebilling/Remove-EbillingInvoiceExtractionFormControls.ps1
pwsh scripts/ebilling/Remove-EbillingInvoiceExtractionFormControls.ps1 -Apply -WhatIf

# 2. The live change: edits both forms, publishes sprk_invoice (entity-scoped), verifies
pwsh scripts/ebilling/Remove-EbillingInvoiceExtractionFormControls.ps1 -Apply -EnvironmentUrl https://spaarkedev1.crm.dynamics.com

# 3. Confirm (exit 0 only when neither control is on either form)
pwsh scripts/ebilling/Remove-EbillingInvoiceExtractionFormControls.ps1 -Verify
```

Exit codes: 0 done / report / verified; 1 error, verification failed, or stopped after the first write (re-run to resume); 2 refused before the first write. If a pending-collateral refusal appears, read the names it lists, and only add `-AllowPendingCollateral` after the owner of those items agrees.

Acceptance criterion 4 (forms on dev show neither field; `-Verify` passes; recorded here) is open until the main session runs step 2 and pastes the `-Verify` output below.

## Result of -Apply / -Verify on dev

(to be filled in by the main session)
