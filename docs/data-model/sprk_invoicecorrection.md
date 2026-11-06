# sprk_invoicecorrection (Invoice Correction)

> **Project**: ebilling-r1, task 027 (D-64, D-47, FR-31, D-82 proposed, ADR-015)
> **Last Updated**: 2026-10-15
> **Script**: `scripts/ebilling/Deploy-EbillingCorrectionSchema.ps1` (idempotent, additive; a second run changes nothing)
> **Solution**: `SpaarkeMaster` (unmanaged, in dev). Solution source, forms and views are synced by task 029.

The structured correction record (D-64): one row per reviewer decision on one extracted or AI-coded value. It stores what Dataverse audit cannot: the field, the AI value, the final value, the AI confidence, the Action / analyzer code and version, the outcome type, the reviewer and role, and when. It is queryable per Action version, field and outcome type, which feeds the D-47 calibration report (task 161) and is the labelled history the later tuning platform (`spaarkeai-tuning-and-feedback-r1`) loads. Nothing is built for that platform in R1.

**Shape.** A child table, not columns on `sprk_billingevent` (decision D-82, proposed): a line has several fields that each need their own value, confidence and outcome; header fields are corrected too and have no line; a value can be confirmed and later corrected; and rows must be filtered by Action version. Columns on the line cannot hold per-field history.

User-owned, entity set `sprk_invoicecorrections`, primary name `sprk_name` (text 200, composed by the server, for example field name and line or invoice). The system `statecode` / `statuscode` keep the defaults (Active / Inactive) and carry no meaning here. Writers: task 101 (corrections), task 100 (bulk accept), task 103 (approve implies Accepted in bulk and Unreviewed for undecided AI fields, D-72). No plugin, business rule, calculated or rollup column, or flow (ADR-002). Task 013 guards "exactly one core-record lookup per row".

## Columns

| Logical name | Type | Notes |
|---|---|---|
| `sprk_name` | Text (200), primary name, application-required | Required by the BFF write path, not enforced on API writes. |
| `sprk_fieldname` | Text (100), **SystemRequired** | The field this row is about (for example `sprk_utbmscode`, `sprk_invoicedate`). |
| `sprk_aivalue` | Multiline text (4000) | The value the AI produced. See the ADR-015 note below. |
| `sprk_finalvalue` | Multiline text (4000) | The value after review. See the ADR-015 note below. |
| `sprk_aiconfidence` | Decimal (precision 4, 0 to 1) | Combined confidence of the AI value (D-47). Empty when the value had no AI source. |
| `sprk_outcometype` | Choice (local), **SystemRequired** | Corrected 100000000, Confirmed individually 100000001, Accepted in bulk 100000002, Unreviewed 100000003. Exactly these four. |
| `sprk_islabel` | Two-option (Yes / No), default No | Yes only for Corrected and Confirmed individually. Stored (not derived) so the calibration query and the later platform filter labels without re-implementing the rule; writers set it from the outcome type. |
| `sprk_sourcecode` | Text (100) | The Action code or analyzer id, for example `invoice-line-code` or `sprk_invoice_v1`. A separate filterable column. |
| `sprk_sourceversion` | Text (20) | The Action or analyzer version. A separate filterable column. |
| `sprk_reviewerrole` | Text (100) | The role in which the reviewer acted. |
| `sprk_reviewedon` | Date and time (user local) | When the reviewer confirmed or corrected the value. |

## Outcome semantics

| Outcome | Label (`sprk_islabel`) | Meaning |
|---|---|---|
| Corrected | Yes | The reviewer changed the AI value. |
| Confirmed individually | Yes | The reviewer decided the item on its own and kept the AI value. |
| Accepted in bulk | No | Bulk accept of Clean lines, or approve with undecided lines (D-72). Recorded but not a confirmation: this prevents the AI learning from its own unreviewed output. |
| Unreviewed | No | An AI-sourced field of an individually decided line that no reviewer touched (D-72). |

A value confirmed individually and corrected later has two rows (append-only); the later reviewed-on wins for any current-value view.

## Lookups

| Logical name | Target | Relationship | Delete | Notes |
|---|---|---|---|---|
| `sprk_invoice` | `sprk_invoice` | `sprk_invoicecorrection_invoice_invoice` | Cascade | **Required (SystemRequired)**: a create without it is refused by the platform for every writer ("Attribute 'sprk_invoice' cannot be NULL"). The parent of the row. |
| `sprk_invoiceline` | `sprk_billingevent` | `sprk_invoicecorrection_invoiceline_billingevent` | RemoveLink | Empty for a header field. |
| `sprk_reviewedby` | `systemuser` | `sprk_invoicecorrection_reviewedby_systemuser` | RemoveLink | The reviewing user. |
| `sprk_regardingmatter` | `sprk_matter` | `sprk_invoicecorrection_regmatter` | RemoveLink | ADR-024 entity-specific lookup (FR-05). |
| `sprk_regardingproject` | `sprk_project` | `sprk_invoicecorrection_regproject` | RemoveLink | ADR-024 entity-specific lookup. |
| `sprk_regardingworkassignment` | `sprk_workassignment` | `sprk_invoicecorrection_regworkassignment` | RemoveLink | ADR-024 entity-specific lookup. |
| `sprk_regardingrecordtype` | `sprk_recordtype_ref` | `sprk_invoicecorrection_regrecordtype_recordtype_ref` | RemoveLink | ADR-024 resolver field. |

All lookups use NoCascade for assign, merge, reparent, share and unshare. Dataverse allows a table only one cascade-delete (parental) relationship, so the line cannot cascade as well: if a line is deleted, its rows keep the invoice, field name and name label and lose only the line link. The `sprk_regarding*` names follow `CoreAncestorResolver.CoreAncestorLookups`; exactly one of the three is populated and the resolver fields are complete, enforced server-side by task 013, not by metadata.

## Resolver text fields (ADR-024)

| Logical name | Type |
|---|---|
| `sprk_regardingrecordid` | Text (100) |
| `sprk_regardingrecordname` | Text (200) |
| `sprk_regardingrecordnumber` | Text (100) |
| `sprk_regardingrecordurl` | Text (500, Url format) |

## Data governance (ADR-015)

`sprk_aivalue` and `sprk_finalvalue` may contain invoice narrative text (line descriptions), which can be privileged client information. Logs, traces and telemetry never carry these values; code that logs a correction logs ids, the field name, the outcome type and the source code and version only. No AI prompt content is stored in this table.

## Querying per Action version

Filter on `sprk_sourcecode`, `sprk_sourceversion` and `sprk_outcometype` (and `sprk_islabel` for labels only). A FetchXML query on source code `invoice-line-code`, version `1`, outcome Confirmed individually returned exactly the matching test row in dev (task 027).

## Notes for later tasks

- Append-only: writers insert rows and do not update them; a later decision on the same value is a new row.
- Secure-record family (task 013, for unified-access-control-r2 review): a child table in `config/secure-record-owner-role.json`, `SecureChildLineage` (every core-record lookup plus its invoice / line / document parents) and `RecordOwnerAssignmentCensusTests.ChildTables`; the Secure Record Owner role holds Read on it (dev, 2026-10-15). It carries `sprk_createdbyperson` (lookup to systemuser, field-secured so only the BFF writes it; created by this table's schema script, secured by `scripts/Set-ChildRecordCreatorPersonSchema.ps1`, which lists the table with SpaarkeMaster as its solution home).
- Invoices are closed by status, not deleted, so the invoice cascade is a safety net rather than a normal path.
