# sprk_invoiceapproval (Invoice Approval)

> **Project**: ebilling-r1, task 021 (FR-11, FR-05, FR-35, D-33, D-40, D-77, D-78)
> **Last Updated**: 2026-10-15
> **Script**: `scripts/ebilling/Deploy-EbillingApprovalSchema.ps1` (idempotent, additive; a second run changes nothing)
> **Solution**: `SpaarkeMaster` (unmanaged, in dev). Solution source, forms and views are synced by task 029.

The approval portion of an invoice: one row per invoice and core record (one portion per invoice in R1; multi-portion approval is out of scope). It holds the decision state of the portion, the approved amount, the link to the Decision Record that recorded the decision, and the set of people who may decide it. Decision Records stay append-only history; this table holds what they cannot: the pending state and the eligible approvers. Created additively in dev on 2026-10-15; no plugin, business rule, calculated or rollup column, or flow (ADR-002). Writers: task 103 creates rows, resolves and re-resolves the eligible approvers and records decisions; task 013 guards "exactly one core-record lookup per row".

User-owned, entity set `sprk_invoiceapprovals`, primary name `sprk_name` (text 200, composed by the server, for example invoice number and core record).

## Columns

| Logical name | Type | Notes |
|---|---|---|
| `sprk_name` | Text (200), primary name, application-required | Display name of the approval. Required by the BFF write path, not enforced on API writes. |
| `sprk_approvalstatus` | Choice (local), default Pending | Pending 100000000, Approved 100000001, Rejected 100000002, Returned 100000003 (FR-11). The system `statecode` / `statuscode` keep the defaults (Active 1 / Inactive 2) and carry no approval meaning. |
| `sprk_approvedamount` | Money (precision 2, `_base` companion) | The approved total for the portion, frozen on approval (FR-35). |
| `sprk_eligibleresolvedon` | Date and time (user local) | When the eligible approvers were last resolved: at acceptance, then on each re-resolution (D-78). The staleness marker for the refresh job or the manual refresh. |
| `sprk_decidedon` | Date and time (user local) | When the decision was made. |

## Lookups

All use delete = RemoveLink and NoCascade for assign, merge, reparent, share and unshare.

| Logical name | Target | Relationship | Notes |
|---|---|---|---|
| `sprk_invoice` | `sprk_invoice` | `sprk_invoiceapproval_invoice_invoice` | **Required (SystemRequired)**: a create or update that leaves it empty is refused by the platform for every writer ("Attribute 'sprk_invoice' cannot be NULL"). |
| `sprk_decisionrecord` | `sprk_decisionrecord` | `sprk_invoiceapproval_decisionrecord_decisionrecord` | The Decision Record written when the approval was decided. |
| `sprk_decidedby` | `contact` | `sprk_invoiceapproval_decidedby_contact` | The approver (a person) who decided, so the worklist shows who decided without reading the Decision Record. |
| `sprk_regardingmatter` | `sprk_matter` | `sprk_invoiceapproval_regmatter` | ADR-024 entity-specific lookup (FR-05). |
| `sprk_regardingproject` | `sprk_project` | `sprk_invoiceapproval_regproject` | ADR-024 entity-specific lookup. |
| `sprk_regardingworkassignment` | `sprk_workassignment` | `sprk_invoiceapproval_regworkassignment` | ADR-024 entity-specific lookup. |
| `sprk_regardingrecordtype` | `sprk_recordtype_ref` | `sprk_invoiceapproval_regrecordtype_recordtype_ref` | ADR-024 resolver field. |

The naming follows `CoreAncestorResolver.CoreAncestorLookups` (`sprk_regarding{core}`). `sprk_invoice` itself keeps its typed `sprk_matter` / `sprk_project` lookups plus `sprk_regardingworkassignment` (task 012, task 013's mixed scheme); this table is new, so it uses the `sprk_regarding*` names for all three. Exactly one of the three entity-specific lookups is populated per row and the resolver fields are always complete; both are enforced server-side (task 013), not by metadata.

## Resolver text fields (ADR-024)

| Logical name | Type | Notes |
|---|---|---|
| `sprk_regardingrecordid` | Text (100) | Id of the core record. |
| `sprk_regardingrecordname` | Text (200) | Name of that record. |
| `sprk_regardingrecordnumber` | Text (100) | Number of that record. |
| `sprk_regardingrecordurl` | Text (500, Url format) | URL of that record. |

Lengths follow `projects/spaarke-ontology-platform-r1/notes/140-schema-delta.md` section 2.

## Eligible approvers: the N:N relationship

`sprk_invoiceapproval_eligibleapprover_contact` is a many-to-many relationship (intersect table of the same name) between `sprk_invoiceapproval` and `contact`. A row in it means "this person may decide this approval".

**Why an N:N and not a JSON column.** The "Invoices to review" view (FR-35, task 029) must show an invoice to each eligible approver and only to them, so it filters the approvals through this relationship to the contact whose `sprk_systemuser` is the current user. A model-driven view and a Web API query can filter an N:N; they cannot filter inside JSON. A test in dev confirmed the OData form `sprk_invoiceapproval_eligibleapprover_contact/any(c:c/contactid eq <id>)` returns the approval, and the reverse navigation from the contact works.

**Who writes it.** Only the server (task 103), never a plugin, business rule or flow (ADR-002). The persons come from the core record's assignment lookups named by `sprk_billingapproverrole` (D-33; the default is Assigned Attorney 1 plus Assigned To Internal, D-54). All of those lookups target `contact` on Matter, Project and Work Assignment (verified in dev by the script's pre-check before it creates the relationship), and `contact.sprk_systemuser` links a person to a user. Any one eligible approver may approve (D-40). Re-resolution before a decision follows D-78: a live check at approve, a recheck on GET, and a refresh job or a stale flag plus a manual refresh; `sprk_eligibleresolvedon` records when the set was last resolved. Who may decide *lines* is a different rule (D-77: any user with Write on the invoice).

The associated-menu labels are "Eligible Approvers" on the approval and "Invoice Approvals (eligible approver)" on the contact.

## Notes for later tasks

- A row created through the API takes status Pending (default 100000000) unless the writer sets another.
- Secure-record family (task 013, for unified-access-control-r2 review): a child table in `config/secure-record-owner-role.json`, `SecureChildLineage` (every core-record lookup plus its invoice / line / document parents) and `RecordOwnerAssignmentCensusTests.ChildTables`; the Secure Record Owner role holds Read on it (dev, 2026-10-15). It carries `sprk_createdbyperson` (lookup to systemuser, field-secured so only the BFF writes it; created by this table's schema script, secured by `scripts/Set-ChildRecordCreatorPersonSchema.ps1`, which lists the table with SpaarkeMaster as its solution home).
- Deleting an invoice leaves approvals pointing nowhere only in theory: the invoice lookup is SystemRequired, so Dataverse refuses to clear it, and invoices are closed by status, not deleted.
