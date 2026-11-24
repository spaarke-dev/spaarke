# iss051 — the resolved UTBMS codes reach the Invoice Line code lookups (ISS-051, #3080)

Task: `tasks/iss051-line-code-lookups.poml`. Rigor FULL. Branch `task/ebilling-iss051` (base `e4363a9120`).

## Before → after

| | Before | After |
|---|---|---|
| A coded line at intake | The resolved codes are only in `sprk_extractionconfidence.coding`; `sprk_taskcode` / `sprk_activitycode` / `sprk_expensecode` stay empty (0 lines on dev have one) | Each resolved code's `sprk_billingcode` row is set on its lookup in the same create, and recorded in the JSON slot as `coding.<slot>.codeId` |
| A refused (out-of-set) code, a line the Action did not return, a line with no description, an uncoded invoice | No code, no lookup | Unchanged: no code is stored, so no lookup (FR-22) |
| A reviewer's code correction (and the D-126 re-code of an uncoded line) | Writes the JSON only (the class remarks said "the code lookups are not written") | Writes the JSON and the lookup of that slot in the same conditional update; the JSON slot records the row |
| Add line (164a) | Typed codes stored as JSON + as-billed text; no lookup | Also sets the lookups (the typed codes were checked against the invoice's set) |
| The re-run after Set firm (095d-2a) | Lookups never written | The lookups follow the new derivation: a code with its recorded row sets it, no code clears it, a code stored before ISS-051 (no row recorded) leaves the column as it is |
| AP export (`ApExportService.CodeAsync`) | Read the empty lookup, so exported the firm's as-billed text (or nothing) | Unchanged code; now exports the resolved code. The as-billed fallback applies only to a line with no resolved code (lookup empty) |
| Invoice index (`InvoiceIndexDocument`) | Indexed the empty lookups (the as-billed text as fallback) | Unchanged code; now indexes the resolved code (the lookup's primary name is `sprk_code`) |
| Invoice Review page | Reads the JSON | Unchanged (the JSON and the lookup are written together, so they agree) |

## Source of truth: option A (the lookups)

Chosen: **A — every writer of a resolved code sets the matching lookup to the `sprk_billingcode` row of that code, in the same write.**

Why:

- The lookups are in the spec's data model: FR-04 lists "UTBMS task / activity / expense code lookups plus codes-as-billed text and coding confidence" on `sprk_billingevent`.
- Other surfaces read them: the Invoice Line main form (`sprk_BillingEvent/FormXml/main/{54b1c4d9-…}.xml`) and the line view built by `scripts/ebilling/Deploy-EbillingFormsViews.ps1` (line 193) show `sprk_taskcode` / `sprk_activitycode` / `sprk_expensecode`. With option B those columns would stay empty forever and need a "derived, do not read" note on every form, view and report.
- Both BFF readers (the AP export and the invoice index) already read the lookup first and the as-billed text second, so option A needs no reader change and keeps one rule for every reader: **the lookup is the resolved code; as-billed text is used only when there is none.**
- `sprk_billingcode` has an alternate key on (code set, code) (task 018, `sprk_billingcode_codeset_code_key`) and its primary name is `sprk_code`, so a lookup names exactly one code of one set and reads back as the code text.

The coding JSON stays the record the review page and the correction/decision services read; it now also records the row (`codeId`), so the JSON and the lookup cannot drift: every writer writes both in one request.

## How the row is known (no extra query in the write path)

- `CodeSetResolver.LoadAsync` already reads every code row of the resolved set (and of the shared "UTBMS Activity" / "UTBMS Expense" sets when the set has no codes of that kind). It now keeps each allowed code's row id and passes them to `ResolvedCodeSet` (new optional `codeIds` constructor argument, `CodeIdOf(kind, code)`). Ids are kept out of `InvoiceCodeOption`, which is what the model reads. A code that is not in the allowed list never gets an id, even if one is offered (FR-22).
- `LineCodingGuard` puts the row of each code it keeps on `LineCodeValue.CodeId` (new optional member). A refused code is not kept, so it has no row.
- The persister writes `coding.<slot>.codeId` and the lookup. `StoredLineCoding.Parse` reads `codeId` back, so the re-run after Set firm rebuilds the same lookup for a line it does not re-code.
- `InvoiceCorrectionService.CheckCodeAsync` / `ResolveForUncodedAsync` load the set through the resolver (`LoadRecordedAsync` / `ResolveAsync`), so the corrected code's row is known from the same set the code was checked against.
- `InvoiceLineAddService` resolves the invoice's set through the resolver, so the typed codes' rows are known.

## Writers and readers

| Component | Kind | Change |
|---|---|---|
| `Services/Finance/Coding/CodingModels.cs` | model | `ResolvedCodeSet` carries code rows (`CodeIdOf`); `LineCodeValue.CodeId` |
| `Services/Finance/Coding/CodeSetResolver.cs` | reader of reference data | records each allowed code's row |
| `Services/Finance/Coding/LineCodingGuard.cs` | FR-22 control | kept code → its row; refused/none → no row |
| `Services/Finance/Submission/InvoiceSubmissionPersister.cs` | writer (intake) | `LineTaskCode` / `LineActivityCode` / `LineExpenseCode`, `CodeIdMember`, `CodeLookupColumn`, `CodeLookupsFor`; `BuildLines` sets the lookups; the line JSON slot records `codeId` (the findings JSON is unchanged) |
| `Services/Finance/Submission/InvoiceSubmissionRerun.cs` | writer (re-run after Set firm) | `LineUpdate` writes the lookups from `CodeLookupsFor` |
| `Services/Finance/Submission/StoredSubmissionRead.cs` | reader (re-run) | reads `codeId` |
| `Services/Finance/InvoiceCorrectionService.cs` | writer (reviewer code correction, D-126 re-code) | the value update carries the lookup and the JSON `codeId` |
| `Services/Finance/InvoiceLineAddService.cs` | writer (Add line) | sets the lookups and the JSON `codeId` |
| `Services/Finance/ApExportService.cs` | reader | unchanged (lookup → `sprk_code`; as-billed only with no lookup) |
| `Services/Jobs/Handlers/InvoiceIndexDocument.cs` | reader | unchanged (lookup name; as-billed only with no lookup) |
| `Services/Finance/InvoiceReviewReadService.cs`, `InvoiceLineDecisionService.cs` (~1486, the D-64 bulk rows), `InvoiceCorrectionReevaluation.cs`, `InvoiceFindingsDiff.cs` | readers of the JSON | unchanged; `InvoiceLineDecisionService` ~1486 only reads the JSON code for the "Accepted in bulk" correction rows, it writes no code |

No new endpoint, service, DI registration, package or column.

## Rules kept

- FR-22: a code outside the line's resolved set is never stored, so it never gets a lookup. A line with no resolved code (refused, not returned, no narrative, uncoded invoice) leaves the lookup empty.
- A reviewer's code correction updates the source of truth (JSON + lookup, one conditional update). A Confirm writes no value (unchanged), so it does not touch the lookup.
- A correction whose set was read without rows (not a production path: `LoadAsync` always reads the rows) clears the lookup rather than leaving the previous code's row.
- The export falls back to the firm's as-billed text **only when the line has no resolved code** (lookup empty). Until the backfill is applied, lines stored before this change also have an empty lookup, so they still export as-billed text.

## Backfill (existing lines)

`scripts/ebilling/Backfill-EbillingLineCodeLookups.ps1`. Report mode by default (read only); `-Apply` writes. Routed through `EbillingSchema.psm1` (`Invoke-Dv` sends `Consistency: Strong`).

- **Which lines.** By default only lines of an invoice that is **In Review or Approved**. A line of an invoice that is Sent to AP, Paid, or inactive (Superseded, Rejected, Returned to Firm, Cancelled) is skipped: AP already received that invoice with the firm's as-billed codes (FR-41 / D-56), so its lines keep matching what AP got. `-IncludeClosed` includes them. The report breaks every line read down by invoice status (lines, lines with a slot to set, slots to set, included / skipped).
- **Which row.** Per slot, the code's set is the line's code set (`coding.codeSetId`), or, for activity / expense when that set has no active code of the kind, the shared set (the resolver's own rule). Then: 1. the row the JSON names (`codeId`), used **only if** that row is the slot's code, of the slot's kind, in that set (a row that names anything else is a conflict, left as it is; a row that no longer exists falls through to 2); 2. the code's row in that set. Never guesses (0 or several rows = unresolved, left empty), never clears, never overwrites a lookup that names another row (conflict, listed), never writes the JSON. Idempotent: a set lookup reads as "ok" on the next run.
- **Concurrency.** The PATCH sends `If-Match: <the row version read>`, so a line changed since the read (a reviewer's correction, a re-run) is refused with 412, counted as "changed since read" (its own count, not an error) and never overwritten; re-run the script for it. A PATCH moves the line's row version.

**Not applied.** `-Apply` is a live dev write and needs the owner's typed OK in the main session.

### Runbook (owner)

1. `pwsh scripts/ebilling/Backfill-EbillingLineCodeLookups.ps1` (report), then `pwsh scripts/ebilling/Backfill-EbillingLineCodeLookups.ps1 -Apply`. If "lines changed since read" is not 0, run `-Apply` again.
2. **Re-index the affected invoices.** `-Apply` changes only line rows; the invoice index reads the lookups but is rebuilt only when the invoice changes, and a line PATCH does not move `sprk_invoice.modifiedon`, so the index keeps the as-billed codes until a re-index. The script prints the distinct affected invoice ids, the oldest one's `modifiedon`, and the sweep command. `Invoke-EbillingInvoiceReindexSweep.ps1` queues one re-index per invoice with `modifiedon` on or after `-Since` (a UTC date; it compares from midnight of that date), so the oldest affected invoice's date covers them all (other invoices modified since are re-indexed too, which is harmless). On dev, from today's report:

   ```
   pwsh scripts/ebilling/Invoke-EbillingInvoiceReindexSweep.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -Since 2026-11-03 -Change iss051-lookups
   pwsh scripts/ebilling/Invoke-EbillingInvoiceReindexSweep.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -Since 2026-11-03 -Change iss051-lookups -Apply
   ```

   Use the `-Since` the `-Apply` run prints (it lists only the invoices it actually wrote). The sweep's `-Apply` needs "Azure Service Bus Data Sender" on the namespace.

Report run against dev, 2026-11-24, fix round 1 (read only, closed invoices skipped):

| | Count |
|---|---|
| Fee / Expense lines with a coding JSON | 130 (all coded) |
| Skipped by invoice status | 49 (Superseded 22, Sent to AP 27) |
| Included lines with at least one slot to set | 80 (all In Review) |
| Slots that would be set | 139 |
| Slots already ok | 104 |
| Unresolved | 0 |
| Conflict | 0 |
| Invoices to re-index after `-Apply` | 3 (oldest `modifiedon` 2026-11-03T18:36:05Z) |

By invoice status (every line read):

| Status | Lines | With a slot to set | Slots to set | |
|---|---|---|---|---|
| In Review | 81 | 80 | 139 | included |
| Sent to AP | 27 | 27 | 46 | skipped |
| Superseded | 22 | 22 | 38 | skipped |

The totals across statuses (129 lines, 223 slots) are the first run's (before the status filter). The 42 lines of the checklist-Q invoice (INV-123.1.1) are all In Review and in the report; their E118 expense lines resolve to the "UTBMS Expense" row of E118.

## Tests

Failing before → passing after (old source restored, the tests run, then restored):

- `InvoiceCorrectionServiceTests.ACodeInTheSet_IsStoredInTheCodingJson_AndTheCodeLookup_…` (renamed; it pinned "the code lookups are not written") and `AnActivityCode_IsCheckedAgainstTheActivityList_…` (activity lookup).
- `InvoiceCorrectionReevaluationTests.UncodedInvoice_APendingLineIsReCoded_…` (the re-code sets the lookup).
- `InvoiceLineAddServiceTests.AnAddedLine_SetsItsCodeLookups`.

Tests that use the new members (`CodeIdOf`, `CodeId`, `CodeLookupsFor`, `CodeIdMember`) cannot compile against the old source; they were proved by mutation instead (id plumbing removed in the guard, the resolver, the persister and the re-run → 7 fail):

- `LineCodingGuardTests` (domain): a kept code carries its row; a refused code, a firm-only code and an uncoded line carry none; an id never makes a code allowed.
- `CodeSetResolverSeamTests`: the resolved set names each allowed code's row, from the set that holds it (shared activity / expense sets included); none for an inactive or foreign code.
- `LineCodingSeamTests.FirmCodes_…` (updated): the real resolver + guard give the stored code its row.
- `InvoiceSubmissionPersisterTests`: a coded line persists its lookups and `codeId`; uncoded / not-returned / no-description lines store none; `CodeLookupsFor` cases.
- `InvoiceSubmissionRerunTests`: the re-run sets the lookup from the stored `codeId`, clears an empty slot's lookup, and leaves a code with no recorded row untouched; the commit-columns test now admits the three lookups.
- `InvoiceSubmissionRerunTests` (fix round 1, F2): a line the re-run re-codes (its code set changed) gets the new code's row on its task lookup (replacing the row from before) and on the JSON `codeId`; a re-coded line with no code has its task lookup cleared. Mutation: with the re-run's lookup write removed, both fail (and the earlier ISS-051 re-run test): 3 fail, 123 pass.

Reader contract (passes before and after; the reader did not change): `ApExportBatchTests.Export_EmitsTheResolvedCode_AndTheAsBilledTextOnlyWithNoResolvedCode`. The index's equivalent is the existing `InvoiceIndexingJobHandlerTests.Codes_FallBackToAsBilled_AreDistinctAndOrdered`.

## Known limits

- Until `-Apply` runs (and the re-index sweep after it), the 80 included dev lines keep empty lookups, so their AP export and index still carry the as-billed text.
- The 49 lines of Sent to AP / Superseded invoices are skipped by design (FR-41 / D-56: AP received the as-billed text); they keep empty lookups unless the owner runs `-IncludeClosed`.
- The re-run after Set firm trusts the `codeId` stored in the line's JSON (`StoredLineCoding.Code`) without checking it is that code, kind and set: the parse has no code set to check against, and checking would add a code-row read to the re-run. Only the BFF writes that JSON, from the same resolved set it checked the code against, so a mismatch needs a hand edit of the JSON. The backfill does check (K3).
- The backfill leaves the JSON without `codeId` on existing lines; a later re-run after Set firm on such a line leaves its (backfilled) lookup as it is, and a reviewer's correction writes both.
- A code row deactivated after coding still names the line's lookup (the code was in the set when coded); the backfill accepts a row in any state for the same reason.
- `sprk_taskcode` etc. are not field-locked; a direct form edit can still make a lookup disagree with the JSON. The backfill reports that as a conflict and leaves it.
