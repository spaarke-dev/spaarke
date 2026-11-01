# sprk_ebillingsetting (eBilling Settings)

> **Project**: ebilling-r1, task 025 (FR-48, D-47, D-54, D-56, D-59, D-69)
> **Last Updated**: 2026-11-01 (task 151c-1a: `sprk_apexportemail`)
> **Script**: `scripts/ebilling/Deploy-EbillingSettingsSchema.ps1` (idempotent, additive; a second run changes nothing)
> **Solution**: `SpaarkeMaster` (unmanaged, in dev). Solution source and the admin form are synced by task 029.
> **BFF reader**: `Services/Finance/IEbillingSettingsReader.cs` -> `EbillingSettingsReader.cs` (typed `EbillingSettings`, 30 s cache)

The one audited record that holds the eBilling business settings: default approver roles per core-record type, the rejection reliability threshold, the confidence weights and caps, the related-work window, the coding chunk size, the AP export mapping, the firm-notice AI toggle and the default code set. Values change here, without a deployment, and every change is in the audit history. AI tuning (prompts, examples, evaluation) is not here (D-61).

Organization-owned, entity set `sprk_ebillingsettings`, primary name `sprk_name` (text 100, application-required; the seed row is named "Default"). **Auditing is on** for the table and for every custom column (organization-level auditing was already on in dev). No plugin, business rule or flow (ADR-002): the singleton rule and the ranges are platform metadata.

## Singleton

`sprk_singleton` is a local choice with exactly one option, `Default` (100000000, the default value), required at the **SystemRequired** level (an API write without it is refused or defaulted; ApplicationRequired would only be enforced by forms). The alternate key `sprk_ebillingsetting_singleton` is on that column alone, so a second row carries the same key value and the platform refuses it:

```
Entity Key eBilling Settings singleton violated. A record with the same value for Singleton already exists.
```

(Tested in dev 2026-10-15: a second row with and without an explicit `sprk_singleton` is refused.) Deactivating the row is allowed but the BFF reader then logs a warning and serves the typed defaults; deleting it does the same. The seed script does not re-create a deactivated row.

## Columns

| Logical name | Type | Range / default | Seed (dev) | Notes |
|---|---|---|---|---|
| `sprk_name` | Text (100), primary name | | `Default` | |
| `sprk_singleton` | Choice (local), SystemRequired | `Default` only | Default | See above. |
| `sprk_matterapproverroles` | Multi-select on the global choice `sprk_billingapproverrole` | | Assigned Attorney 1 + Assigned To Internal | Used when a Matter's own `sprk_billingapproverrole` is empty (D-54). |
| `sprk_projectapproverroles` | same | | same | Project. |
| `sprk_workassignmentapproverroles` | same | | same | Work Assignment. |
| `sprk_reliabilitythreshold` | Decimal, precision 2 | 0 .. 1 | 0.90 | A field must reach this combined confidence before it can support an automatic rejection (FR-26, I-5). |
| `sprk_weightanalyzer` | Decimal, precision 2 | 0 .. 1 | 0.50 | Provisional (D-69). |
| `sprk_weightarithmetic` | Decimal, precision 2 | 0 .. 1 | 0.25 | Provisional. |
| `sprk_weightreferencematch` | Decimal, precision 2 | 0 .. 1 | 0.15 | Provisional. |
| `sprk_weightselfreport` | Decimal, precision 2 | 0 .. 1 | 0.10 | Provisional. The four seeded weights sum to 1.00; the platform does not enforce that, so the combiner (task 046) must normalize. |
| `sprk_arithmeticfailcap` | Decimal, precision 2 | 0 .. 1 | 0.50 | Maximum combined confidence of a field whose arithmetic check fails. Provisional. |
| `sprk_referencemissingcap` | Decimal, precision 2 | 0 .. 1 | 0.80 | Maximum combined confidence of a field with no reference-data match. Provisional. |
| `sprk_relatedwindowdays` | Whole number | 0 .. 90 | 7 | Days either side of a line date for the related-work panel (FR-34). Named `sprk_relatedwindowdays`, not the task text's `sprk_relatedworkwindowdays` (see "Deviations"). |
| `sprk_codingchunksize` | Whole number | 1 .. 200 | 25 | Lines per call to the coding Action (FR-22). |
| `sprk_apexportmapping` | Multiline text (100,000) | JSON | the D-56 column set | See "AP export mapping". |
| `sprk_apexportemail` | Text (320), Email format ("AP export email") | optional; added by `scripts/ebilling/Deploy-EbillingApExportEmailColumn.ps1` (task 151c-1a, EB-D-181; idempotent, refuses a same-name column of another type, format or a length under 320) | empty | The one address Email to AP sends a batch's CSV and JSON to. Empty: `POST /api/finance/export-batches/{batchId}/email` answers 409 `ebilling.export.ap_email_not_configured`. On the settings main form, AP export section. |
| `sprk_firmnoticeaienabled` | Two options | default Yes | Yes | Off: firm notices use the template only (FR-50, D-60). |
| `sprk_defaultcodeset` | Lookup -> `sprk_billingcodeset` | optional, delete RemoveLink (relationship `sprk_ebillingsetting_defaultcodeset_billingcodeset`) | UTBMS Litigation | Code-set resolution step 5 (FR-22). |

Out-of-range saves are refused by the platform (tested in dev): `sprk_reliabilitythreshold` 1.5 and -0.1 (`0x80044330`, "outside the valid range(0 to 1)"), `sprk_codingchunksize` 0 and `sprk_relatedwindowdays` 91 (`0x8004432f`). 0.85 saves. Decimal precision 2 means 0.905 cannot be stored.

The confidence weights and caps are **provisional** (D-69): first estimates, validated against the calibration report (task 161), the evaluation (162) and at UAT (163). "Caps" (D-47) is interpreted as a ceiling on combined confidence when that check fails; both seeded caps sit below the 0.90 threshold, so a field whose arithmetic fails or that has no reference match can never reach the reject gate on its own. Task 046 confirms or changes the interpretation.

## AP export mapping

`sprk_apexportmapping` holds JSON, version 1: `columns` is the CSV row and the JSON invoice object (one per invoice, D-56 order), `lineColumns` the `lines[]` of the JSON. Each entry has `name`, a dotted `source` path and an optional `format` (`date`, `decimal`). Task 150 owns the parser and the source-path vocabulary and may extend it; a blank value means "use the built-in D-56 default". GL, cost centre and tax columns are reserved for the fast followers: adding them is a data change, not a code change (FR-41).

## BFF reader

`IEbillingSettingsReader.GetAsync()` returns the immutable `EbillingSettings`. One FetchXml read of the active row, kept in `IMemoryCache` for 30 seconds (ADR-009 exception, documented on the class), so a changed threshold is used by the next submission after at most 30 seconds on each BFF instance. No active row: typed defaults equal to the seed (plus a warning). A cleared column on the row keeps that property's default (so approver resolution never ends with no roles); a Dataverse failure propagates and is not cached.

## Seed and re-runs

The seed row is written only when the table holds no row. A re-run never overwrites an admin edit (tested in dev: threshold changed to 0.85, script re-run, value still 0.85, then restored to 0.90). If `sprk_billingcodeset` does not exist when the script runs, the lookup is skipped (printed SKIPPED) and the row is seeded without a default code set; task 029 re-runs the script after task 018 (in dev the set existed from the first run).

## Deviations

- `sprk_relatedwindowdays` instead of `sprk_relatedworkwindowdays`: a typo in the first dev run created the column under this name; correcting it would mean deleting a column (owner OK needed), and nothing outside the task text names it (consumers use `EbillingSettings.RelatedWorkWindowDays`). The display name is "Related Work Window (days)".
- The table's display name is "eBilling Setting" (plural "eBilling Settings") as is usual for a table; the task text calls the table "eBilling Settings".

## Notes for later tasks

- Task 029: the admin form, view and sitemap entry; re-run the script after the sync. The form should show the singleton choice read-only.
- Task 103 reads the three role lists; "Assigned To" resolves only on Work Assignment (Matter and Project have no `sprk_assignedto`).
- Task 150 reads `ApExportMappingJson` through the reader and snapshots it verbatim into `sprk_billingexport.sprk_mappingsnapshot`.
