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

## Deploy-EbillingFormsViews.ps1 is NOT a way to change the live main form

The script's `Cols` list no longer names `sprk_extractionstatus`, so a NEW environment built from it never gets the control. But re-running it on dev is not the way to remove the control from the live main form: in Section mode it rebuilds the whole `section_ebilling` and would drop the `qv_billingexport` quick view (a pre-existing defect, filed separately by the main session). Only `Remove-EbillingInvoiceExtractionFormControls.ps1` is used for the live change; it removes the control from both forms and touches nothing else on them.
## The guarded dev path

`scripts/ebilling/Remove-EbillingInvoiceExtractionFormControls.ps1`, modelled on 193's `Remove-EbillingRateCardCoreLookups.ps1`:

- Default (no switch): READ-ONLY report. Per form: which control it shows, what would be removed, mentions that would remain; the pending (unpublished) items on `sprk_invoice`; then READY / BLOCKED / ALREADY CLEAN. Writes nothing.
- `-Apply` (dev host only; `-WhatIf` is the dry run): edits the form(s) that show a control, then publishes `sprk_invoice` entity-scoped through `Invoke-PublishXml` (`scripts/lib/Publish-SolutionComponents.ps1`) wrapped in `Invoke-WithTransientRetry`, then verifies.
- `-Verify`: READ-ONLY. Exit 0 only when both forms exist and neither shows either control (nor does any other invoice form); exit 1 otherwise.
- Only the two form ids are ever edited: main `93aa1c69-0406-f111-8406-7c1e525abd8b` (type 2) and quick create `c5e5807d-e20c-f111-8342-7c1e520aa4df` (type 7). Any other `sprk_invoice` form that names a control is a blocker (exit 2), whatever its type.
- Refuses before the first write (exit 2) when: the environment is not dev (checked first, before anything is initialised); an expected form is missing; a control is outside a cell / row / section or would leave a tab without columns; a mention would remain after the clean-up (an event handler); another invoice form names a control; a target form has unpublished changes that are not exactly this run's edit; or another pending form / view of `sprk_invoice` would go live with the publish (D-83; `-AllowPendingCollateral` overrides only this last one).
- Unpublished edits (fix round 1, F2): the new XML is built from the published text, so the pending read lists everything unpublished on `sprk_invoice` WITHOUT excluding the two target forms; for a target form with pending changes the unpublished formxml is read (`RetrieveUnpublished`) and compared, after XML normalisation, with what this run would write. Equal = a resume; anything else refuses. The same check runs again right before the publish (a change that appears during the run is a plain error, exit 1, nothing published by that step). Both unpublished reads (a form, and a view for 193) were exercised read-only against dev and work.
- Consistency: the reads and the form edits go through `EbillingSchema.psm1` (`Invoke-Dv`, `Consistency: Strong`). The pending-collateral reads and the `PublishXml` POST go through the shared lib, whose context carries no `Consistency` header of its own; `Get-PublishContext` sets `Consistency: Strong` on it (fix round 1, F4; the same fix is in 193's script, which had the same gap). The script has no REST call of its own. `ScriptMetadataStrongConsistencyGuardTests` stays green.
## Read-only run against dev (2026-11-23, https://spaarkedev1.crm.dynamics.com; re-run after fix round 1)

Default mode, exit 0:

```
sprk_invoice main form 'Invoice main form' (93aa1c69-0406-f111-8406-7c1e525abd8b): found
sprk_invoice quick create form 'Invoice quick create form' (c5e5807d-e20c-f111-8342-7c1e520aa4df): found
forms showing a retired control : 2
  form 'Invoice quick create form' (c5e5807d-..., type 7): shows sprk_extractionstatus, sprk_extractedjson; would remove control sprk_extractionstatus, an emptied row, control sprk_extractedjson, an emptied row, an emptied section (tab_1_column_3_section_1), an emptied column; mentions left 0
  form 'Invoice main form' (93aa1c69-..., type 2): shows sprk_extractionstatus; would remove control sprk_extractionstatus; mentions left 0
other forms naming one (never edited; blockers): 0
pending (unpublished) items on sprk_invoice: none

VERDICT: READY
```

`-Verify` today, exit 1 (as it must while the controls are on the forms): `Verification failed: form 'Invoice quick create form' still names sprk_extractionstatus, sprk_extractedjson; form 'Invoice main form' still names sprk_extractionstatus`.

## Tests

- New `tests/scripts/Remove-EbillingInvoiceExtractionFormControls.Tests.ps1` (Pester 5.7.1): 59 pass, 0 fail. Covers the pure transform, default mode writes nothing and shows the pending line, `-Verify` both ways, `-Apply` (happy path / idempotent / `-WhatIf` / unexpected shapes / only the two ids / unpublished edits / pending collateral / a change during the run), the dev-only pre-flight, the `Consistency: Strong` context, structure (no REST call of its own, writes only behind `Confirm-Write`), and the repository sources (forms, forms script, columns and option set stay, PB-013 gone).
- `Remove-EbillingRateCardCoreLookups.Tests.ps1` (5.7.1): 122 pass. `Deploy-EbillingFormsViews.Tests.ps1` (5.7.1): 7 pass.
- ArchTests (`ScriptMetadataStrongConsistency|SpaarkeMaster|Form`): 114 pass, 0 fail. `FinanceBaseValuesSeamTests` (compiled into `tests/unit/Sprk.Bff.Api.Tests`): 2 pass, unchanged.
- Fails before the change (first round): the four source-state tests fail on the base commit's files (34 pass / 4 fail).

## Fix round 1 (Opus review of 6a85e61cee)

Each fix has tests that fail with the fix removed (a mutant of the script, run against the same test file) and pass with it.

| # | Fix | Evidence (tests failing when the fix is removed) |
|---|---|---|
| 1 | F2, unpublished edits: pending read WITHOUT exclusion; a target form's (193: form or view's) unpublished text must equal this run's edit, else exit 2; same line in report mode; re-checked before the publish. 194 and 193. | 194: own check disabled, 4 fail (report BLOCKED, `-Apply` refuses, `-AllowPendingCollateral` does not override, change during the run); targets excluded from the read again, 6 fail; publish re-check removed, 1 fail (own) + 1 fail (foreign/mid-run). 193: own check disabled, 5 fail; forms and views excluded again, 7 fail; publish re-check removed, 1 fail. |
| 2 | F4: `Get-PublishContext` sets `Consistency: Strong`. 193 did not do it; fixed there too. | 194: header line removed, 2 fail (context header; the contexts passed to the collateral read and `Invoke-PublishXml` through a whole `-Apply`). 193: header line removed, 1 fail. |
| 3 | F2, pre-flight: `Get-PreflightRefusals` and `Initialize-EbillingSchema` moved into `Invoke-Cleanup`; the entry only calls it. | Pre-flight call removed: 2 fail (a non-dev URL with `-Apply` / `-Apply -WhatIf` is refused before any initialise, read, context or write). |
| 4 | K1: only the two form ids are edited (pinned by id and type); any other invoice form naming a control, of any type, is a blocker. | Other-forms blocker removed: 2 fail (same name but another id; any other form of type 2 / 7 / 11 / 6). |
| 5 | Test gap: "pending changes appeared during the run". | Own re-check removed: 1 fail (target form changed mid-run); foreign re-check removed: 1 fail (another item appears mid-run); with `-AllowPendingCollateral` the foreign one is accepted. |
| 6 | Docs: script header and this note no longer suggest re-running `Deploy-EbillingFormsViews.ps1`; the false "no REST call of its own" claim is corrected (see the guarded-path section: the shared lib's calls are covered by setting the header on its context). | n/a |

Counts after fix round 1: 194 59 pass; 193 122 pass (was 115); `Deploy-EbillingFormsViews` 7 pass; ArchTests 114 pass; `FinanceBaseValuesSeamTests` 2 pass.

Note for the main session: a genuine resume is refused if Dataverse stores a pending form text in a different shape than the script wrote it (the comparison normalises insignificant whitespace but nothing more); the safe answer is then to publish the pending edit in the form designer and re-run.

## Fix round 2

Mutants are runs of the same test file against a copy of the script with the fix removed.

| # | Change | Evidence (tests failing when it is removed) |
|---|---|---|
| F1 | Every pending-text comparison is case-strict: 194 `-cne` on the form text; 193 `-ceq` on the form text, the view fetchxml and the view layoutxml. The other comparisons in both scripts were listed and checked (`-eq`/`-ne`/`-in`/`-contains`/`-like`): they are on form ids (normalised), the host name, XML element names, kinds, types and counts; none compares pending, re-check or resume text. | 194: `-cne` to `-ne`, 3 fail. 193: `-ceq` to `-eq`, 5 fail (form, view fetchxml, view layoutxml, report, change during the run). |
| F2 | 193: the view is compared per part. A view whose pending fetchxml differs (layoutxml equal to this run's text), and one whose pending layoutxml differs (fetchxml equal), each refuse (exit 2) with 0 writes. Case-only tests in both scripts (form; view fetchxml and layoutxml in 193; report mode; a case-only change during the run). | 193: fetch comparison dropped, 3 fail (fetchxml-only, case-only fetchxml, K2 post-read-back view); layout comparison dropped, 2 fail (layoutxml-only, case-only layoutxml). |
| K2 | After each PATCH both scripts read the unpublished text back (`RetrieveUnpublished`) and the pre-publish re-check expects THAT text (the form's formxml; a view's fetchxml and layoutxml), so a Dataverse that normalises the XML on save does not stop the first `-Apply` with exit 1 after the writes. The pre-WRITE check of a re-run still compares with the text the script would write, so after a stop before the publish a normalised pending text is refused (exit 2). Help text of both scripts says so: a first-run mismatch means the pending text is not byte-for-byte this script's edit, inspect it, and publishing the pending edit in the designer clears it. | Read-back not adopted: 194 1 fail, 193 1 fail (the normalise-on-save run publishes). A change made after the read-back is still caught (194 and 193, form and view). The re-run refusal is pinned in both. |
| K1 | A read that fails before the first write (for example the pending `RetrieveUnpublished` read) ends with "Nothing was written (the error happened before the first write)." instead of "Steps already done stay done". `Get-ErrorTrailer` decides from `$script:WriteStarted`, which is set immediately before each write that is actually sent (not under `-WhatIf`, not in report mode). Exit code kept at 1: 2 is reserved for refusal decisions made before the first write, and a failed read is an error, not a decision. | Flag never set: 194 1 fail, 193 1 fail (after a write the trailer must say partly applied). Trailer always the old text: 194 1 fail, 193 1 fail. |

Counts after fix round 2: 194 68 pass (was 59); 193 135 pass (was 122); `Deploy-EbillingFormsViews` and the ArchTests and `FinanceBaseValuesSeamTests` as below.
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
