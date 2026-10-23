# sprk_invoicesubmission (Invoice Submission)

> **Project**: ebilling-r1, task 020 (FR-10, FR-05, FR-17, FR-20, D-37)
> **Last Updated**: 2026-10-19
> **Script**: `scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1` (idempotent, additive; a second run changes nothing)
> **Solution**: `SpaarkeMaster` (unmanaged, in dev). Solution source, forms and views are synced by task 029.

One row per **submission attempt**: accepted, rejected or failed. A rejected attempt is recorded here and never becomes an `sprk_invoice`, so Finance rollups and Ontology facts never count it (spec "New Components"). An accepted attempt points at its resulting invoice. A resubmission of the same firm invoice number is a new row with the next tracking number (`INV-002.1`, `INV-002.2`, D-37) pointing at the one it resubmits. Created additively in dev on 2026-10-15; no plugin, business rule, calculated or rollup column, or flow (ADR-002). Writers: task 044 creates rows, task 045 allocates tracking numbers, task 049 updates the processing status.

User-owned, entity set `sprk_invoicesubmissions`, primary name `sprk_name` (text 200, normally the tracking number).

## Columns

| Logical name | Type | Notes |
|---|---|---|
| `sprk_name` | Text (200), primary name, application-required | Display name of the submission. Required by the BFF write path, not enforced on API writes. |
| `sprk_trackingnumber` | Text (100), **SystemRequired** | `{firm invoice number}.{n}`. Part of the alternate key; the platform refuses an empty value on every write. Allocated concurrency-safe by task 045. |
| `sprk_firminvoicenumber` | Text (100) | The number as submitted, or as extracted from the PDF when none was entered. |
| `sprk_submissionsequence` | Whole number, 1 .. 2,147,483,647 | The `n` of the tracking number. Dataverse refuses 0 (checked live). |
| `sprk_channel` | Choice (local) | SPA 100000000, Internal 100000001, Mailbox 100000002 (reserved), Outlook 100000003 (reserved), API 100000004 (reserved). |
| `sprk_processingstatus` | Choice (local) | Received 100000000, Processing 100000001, Accepted 100000002, Rejected 100000003, Failed 100000004. |
| `sprk_outcomereasoncode` | Text (100) | Stable reason code for Failed or Rejected (FR-21). |
| `sprk_findingsjson` | Multiline (1,048,576) | Findings that caused a rejection, as JSON. |
| `sprk_firmnoticetext` | Multiline (100,000) | The notice text sent to the firm (FR-50). |
| `sprk_ponumber` | Text (100) | PO number as entered at submission (FR-18). |
| `sprk_receivedon` | Date and time (user local) | When the submission was received. |
| `sprk_correlationid` | Text (100) | Correlation id, same shape as on `sprk_invoice`. |
| `sprk_adminattention` | Text (500), optional | Needs Admin Attention (D-133, task 049). Why this submission needs an administrator's review; empty when nothing does. The BFF sets it, server-side only, from three sources, each a segment of the one text (joined by ` | `, in this order, merged into the current value and never overwriting another source): (1) **Signal plan and write refusals** - a billing Signal the submission should have could not be created: a plan entry the Signal writer would refuse (`InvoiceSubmissionPersister`, same transaction update as `sprk_findingsjson`) or a permanent write refusal (`InvoiceSubmissionAfterSteps`, same update that records `signalWriteRefusals`); (2) **system policy misconfigured** - `BILL-SYSTEM-SUBMISSION` is missing, ambiguous, without a version in force or of the wrong type, so the system Signals were not created (`System billing policy BILL-SYSTEM-SUBMISSION is missing: system Signals were not created`; a policy an administrator DISABLED writes nothing); (3) **processing stopped on a linked submission** - the job handler left a submission that links its invoice Processing (`Processing stopped: decision_owner_refused; the invoice is linked and the submission stays Processing: review it, then resume or correct`); this is not a status write. Codes only, never an extracted value, e.g. `2 Signals could not be created: record_owner_no_default_team (BILL-RATE on line 1a2b3c4d), core_record_changed (...)`; over 500 characters the Signal segment ends with `... (+N more)`. The full detail stays in `sprk_findingsjson`. Code does not clear it, with ONE exception: when the after-steps complete the submission (terminal Accepted or Rejected) they remove the **processing stopped** segment, because it is no longer true, in the same row-version-conditional update that writes the status (null when it was the only segment); the Signal and policy segments and any text an administrator typed stay, and a text with no such segment is never written. An administrator clears the rest after review. Created by `scripts/ebilling/Deploy-EbillingSubmissionAttentionSchema.ps1` (idempotent; not yet run on dev, owner's OK; 053 adds it to SpaarkeMaster). Logical name constant: `InvoiceSubmissionPersister.SubmissionAdminAttention`. |

The table's system `statecode` / `statuscode` keep the defaults (Active 1 / Inactive 2); the pipeline state is `sprk_processingstatus`.

## Views

| View | Filter | Columns | Sort | Created by |
|---|---|---|---|---|
| Submissions needing attention (public, D-133) | `sprk_adminattention` is not empty **or** `sprk_processingstatus` = Failed (100000004) | `sprk_name`, `sprk_trackingnumber`, `sprk_vendororg`, `sprk_processingstatus`, `sprk_outcomereasoncode`, `sprk_adminattention`, `sprk_receivedon`, `modifiedon` | Modified on, newest first | `scripts/ebilling/Deploy-EbillingSubmissionAttentionSchema.ps1` (with the column) |

The customer administrator opens it in the Matter Management app to review submissions whose Signal could not be created and submissions that failed, and clears `sprk_adminattention` once reviewed. The main form (`Deploy-EbillingFormsViews.ps1`) shows `sprk_adminattention` in its Submission section so the admin can clear it; the Active view is separate.

## Lookups

All are optional except `sprk_vendororg`, which is **SystemRequired** (alternate key column; the platform refuses a submission without a firm on every write). Every relationship has Delete = RemoveLink and no other cascade.

| Logical name | Target | Relationship | Meaning |
|---|---|---|---|
| `sprk_submitter` | `contact` | `sprk_invoicesubmission_submitter_contact` | Who submitted (persons are contacts). |
| `sprk_vendororg` | `sprk_organization` | `sprk_invoicesubmission_vendororg_organization` | The firm. Same name as on `sprk_invoice`. Part of the alternate key. |
| `sprk_document` | `sprk_document` | `sprk_invoicesubmission_document_document` | The submitted PDF. |
| `sprk_reportdocument` | `sprk_document` | `sprk_invoicesubmission_reportdocument_document` | The rejection or adjustment report PDF. |
| `sprk_invoice` | `sprk_invoice` | `sprk_invoicesubmission_invoice_invoice` | The resulting invoice; empty unless accepted. |
| `sprk_resubmissionof` | `sprk_invoicesubmission` (self) | `sprk_invoicesubmission_resubmissionof` | The earlier attempt this one resubmits. |

## Core-record regarding (ADR-024, FR-05)

Three entity-specific lookups plus the five resolver fields, named as `CoreAncestorResolver.CoreAncestorLookups` expects (`sprk_regarding{core}`). At most one entity-specific lookup is populated per row, and the resolver fields are filled together: both are enforced server-side by task 013, not by schema.

| Logical name | Type | Relationship / length |
|---|---|---|
| `sprk_regardingmatter` | Lookup -> `sprk_matter` | `sprk_invoicesubmission_regmatter` |
| `sprk_regardingproject` | Lookup -> `sprk_project` | `sprk_invoicesubmission_regproject` |
| `sprk_regardingworkassignment` | Lookup -> `sprk_workassignment` | `sprk_invoicesubmission_regworkassignment` |
| `sprk_regardingrecordtype` | Lookup -> `sprk_recordtype_ref` | `sprk_invoicesubmission_regrecordtype_recordtype_ref` |
| `sprk_regardingrecordid` | Text (100) | resolver field |
| `sprk_regardingrecordname` | Text (200) | resolver field |
| `sprk_regardingrecordnumber` | Text (100) | resolver field |
| `sprk_regardingrecordurl` | Text (500), format Url | resolver field |

Lengths follow `projects/spaarke-ontology-platform-r1/notes/140-schema-delta.md` section 2. Naming note: `sprk_invoice` itself keeps the typed `sprk_matter` / `sprk_project` lookups and adds only `sprk_regardingworkassignment` (task 012); new tables such as this one use the `sprk_regarding*` trio (the mixed scheme recorded in task 013).

## Alternate key

`sprk_invoicesubmission_firm_trackingnumber` on (`sprk_vendororg`, `sprk_trackingnumber`), index status **Active** (Dataverse lists the attributes alphabetically: `sprk_trackingnumber`, `sprk_vendororg`). The key is firm-scoped because two firms can both send `INV-002` (D-37). A lookup-based key is addressed in the Web API by the lookup **value** name: `sprk_invoicesubmissions(_sprk_vendororg_value=<id>,sprk_trackingnumber='INV-002.1')`. Checked live: a second row with the same firm and tracking number is refused with HTTP 412, `0x80060892` "Entity Key Firm and Tracking Number violated"; the same tracking number under a different firm is accepted. Rows with an empty firm or tracking number would not be covered by the key, so both columns are SystemRequired (the platform refuses a null on every write).

## Notes for the writers

- The Web API navigation names follow the lookup schema names: `sprk_VendorOrg@odata.bind`, `sprk_Document@odata.bind`, `sprk_ReportDocument@odata.bind`, `sprk_Invoice@odata.bind`, `sprk_ResubmissionOf@odata.bind`, `sprk_Submitter@odata.bind`, `sprk_RegardingMatter@odata.bind`, and so on; value properties are `_sprk_vendororg_value` etc.
- Secure-record family (task 013, for unified-access-control-r2 review): a child table in `config/secure-record-owner-role.json`, `SecureChildLineage` (every core-record lookup plus its invoice / line / document parents) and `RecordOwnerAssignmentCensusTests.ChildTables`; the Secure Record Owner role holds Read on it (dev, 2026-10-15). It carries `sprk_createdbyperson` (lookup to systemuser, field-secured so only the BFF writes it; created by this table's schema script, secured by `scripts/Set-ChildRecordCreatorPersonSchema.ps1`, which lists the table with SpaarkeMaster as its solution home).
