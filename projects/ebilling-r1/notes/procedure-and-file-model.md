# Procedures and files — revised approach

> **Status**: v5, 2026-10-21. Ready for peer re-check: ontology-r1 (scoped), uac-r2 (full), word-add-in-r1 (§9.1). SPA-r3 checks §9.3.
> - v5 folds in the three peer reviews of v4 (`c260f9de11`) and the owner's answers A–R, all recorded in `procedure-model-reviews.md`. Section 0 lists what changed.
> - Nothing is implemented. No schema, task or decision row has changed yet.
> - Once the peers re-check and the owner finalizes, this note becomes decision **EB-D-148** plus revised POMLs for 079–087 and new tasks (section 13). The UI/UX is prototyped separately (O-14); the back end goes first.
>
> **Supersedes, once agreed:** D-44 (in part), D-55 (in part), D-67, D-145, D-146 (in part), the business-unit filter points (D-171 pts 4 and 6, D-172 Q13, D-141 Q1), and most of `rule-set-scope-design.md`.

**"Matter" is shorthand.** e-billing applies to every **core record**: `sprk_matter`, `sprk_project` and `sprk_workassignment` in R1, and `sprk_agreement` in R2 (section 17). Wherever this note says "matter" or "record", it means any core record. Every table, lookup, field, control and rule here is built to the core-record set, held in one list (like `InvoiceFamilyCoreRecordGuard.FilingCores`), never to `sprk_matter` alone (owner directive, 2026-10-21; project CLAUDE.md §3).

---

## 0. What changed from v4

| # | Change | Source |
|---|---|---|
| 1 | Record-specific rule changes live in a **record-scoped `sprk_ruleset`** (secure child of its record). The exceptions Procedure and "Exceptions for" are gone; Procedures are reusable documents only. | Owner A; uac-r2 F1-a |
| 2 | New fields `sprk_billingfirm` (the law firm that decides the default Procedures) and `sprk_billingresponsible` (the client's billing entity, future use) on every core record. Neither confers access. | Owner B; uac-r2 census note |
| 3 | A Procedure that names a firm applies only to that firm's invoices, **from every source**. Rank key (Level, −Distance, FirmSpecific). | Owner C; ontology 4 |
| 4 | Removing an override, or retiring a Procedure, **re-checks** the affected pending and In Review invoices. | Owner D, D2 |
| 5 | A gap (no version in force) **holds** the invoice with the warning "No procedure associated with this invoice". | Owner E; ontology 4 |
| 6 | `sprk_createdbyperson` on the four new tables, replacing "uploaded by". | Owner F (ontology D-175 (4)) |
| 7 | Ontology's matter rules stay as built; Ontology-area Procedures are document-only in R1. | Owner G; ontology 7 |
| 8 | Type "Internal procedure" → **"Internal Standard"**; Area reuses `sprk_policydomain`. | Owner H; ontology 1 |
| 9 | Submit Invoice: email filed first (A) and the invoice file follows its source through T7 (B); internal-only, Restricted and Private-thread sources are allowed with their isolation carried forward. The add-in will offer Submit Invoice; the process is still to be discussed. | Owner I, J, K; word-add-in-r1 1–5 |
| 10 | Submit Invoice is **all or nothing**: the full re-run happens right after commit and before any after-step; a failure undoes everything. | Owner O; uac-r2 K1 and its three conditions |
| 11 | The internal file route refuses every `sprk_isexternal` caller; flagged-external users get invoice files only through the firm route. | Owner M; uac-r2 F1-e |
| 12 | Firm file routes settled; the four `/external/invoices/{id}/documents…` routes retire in the same change. Scan pending shows "Checking file…". No migration. | §6 of the reviews; owner Q, Q2, R; R3 |
| 13 | Stored File: all authorization columns field-secured, a servable status, source ids, deterministic id, orphan rules, a download audit event. | uac-r2 F1-b, F2-a–c, Q4, Q5; owner P |
| 14 | Version chain: `BILL-SYSTEM-SUBMISSION` exempt; re-key list named; explicit published marker; ratchet ArchTest. | ontology 2 (blocking 2b) |
| 15 | Schema details: custom status, DateOnly half-open dates, alternate key, scope lock, delete behaviours, census waiver rewrite. | ontology 4, 9 (blocking 9a), O1 (blocking) |
| 16 | Record rules take their base rule's slot; the Replace-type known limit is gone. | ontology 5 |
| 17 | AddedFee and Discount: option values, extraction preconditions; Discount is a check only. | ontology 6 |
| 18 | Snapshot: full on the submission, bounded summary on the Decision Record; firm findings use an allow-list DTO. | ontology O2; R3 |
| 19 | A census table for uac-r2 task 191 (section 5.2). | uac-r2 F4-a, task 191 |
| 20 | Agreement is a core record, deferred to R2, with its extension path (section 17). | Owner, 2026-10-21 |
| 21 | Extracted invoice fields stay readable after a later source change (known limit, owner-confirmed). | Owner; uac-r2 |
| 22 | The word "family" is not used for any table; uac-r2's concept is called **secure child tables**. | Owner, 2026-10-21 |

---

## 1. The approach in one page

**Procedures and their versions**

- A **Procedure** (`sprk_procedure`) is the internal record of a document that drives policy or rules: client billing guidelines, a firm's rate card, billing instructions, an internal standard, and non-billing policy documents later. It is provenance only: never evaluated, never cited in place of a policy version, and not a `sprk_document`.
- Each issue of it is a **Procedure Version** (`sprk_procedureversion`), which carries the file, its effective dates and its status. The file is kept in **our own Azure Blob storage**. There is no document management.
- A Procedure's rules live in one rule set. Each Procedure Version publishes its own versions of those rules, so a rule keeps its identity across versions. A rate card imported from a workbook belongs to its Procedure Version.

**How a record becomes subject to Procedures.** In three ways, always under the firm condition (a Procedure that names a firm applies only to that firm's invoices):

1. Procedures that apply to **all records**.
2. Procedures for the **invoice's firm**.
3. Procedures **assigned manually** to the record.

The record's `sprk_billingfirm` sets which firm's Procedures its list shows by default.

**Overrides on the record**

- **Procedure level:** pin a version (keep 2025 rates), or exclude the Procedure.
- **Rule level:** exclude one rule (no late fee on this matter), or change it for this record. A changed rule lives in the record's own **record-scoped rule set**, which is a secure child of the record, not a Procedure.

**Evaluation.** One deterministic resolver gives the rules in force: record → Procedures → Procedure Version for the work date → rules → overrides → precedence.

- It fails closed when it can't read what it needs.
- A gap holds the invoice ("No procedure associated with this invoice").
- Every evaluation saves a self-contained snapshot of what applied and why.
- Signals and Decision Records keep citing immutable policy versions, so a decision made under v1 stays a v1 decision.

**Files for invoices and exports.** Invoices and billing exports keep their files in the same file store, not as Documents. An emailed invoice still arrives as a Document. **Submit Invoice** on the attachment copies its bytes into the new invoice's files, all or nothing, and the copy keeps following its source email's access (section 9.1).

```
Procedure "Acme outside counsel guidelines"  (applies to: firm F)        rule set (one per Procedure)
 ├─ Version 1 [2025-01-01, 2026-01-01) ── file ─┐                         ├─ rule "Travel"        : v1, v2
 └─ Version 2 [2026-01-01, …)          ── file ─┴─ publish ────────────────┼─ rule "Added fees"    : v1, v2
                                                                          └─ rule "Block billing" : v2 only
 Matter M (sprk_billingfirm = F) ── subject to: [all-records Procedures] + [F's Procedures on F's invoices] + [manual]
             Procedure overrides: pin "F rate card" to its 2025 version
             rule overrides:      exclude "Added fees" · change "Travel" (copy in M's record-scoped rule set)
                 │
 Invoice on M from firm F, line worked 2026-03-10
   → resolver → rules in force → evaluation → Signals / Decision Records (cite policy versions)
   → snapshot: each rule's policy version, Procedure Version, source, level, and the overrides that decided it
```

---

## 2. Owner decisions (2026-10-20 and 2026-10-21)

| # | Decision |
|---|---|
| O-1 | Procedure holds guidelines, rate cards, billing instructions, internal standards and any other documents that drive ontology policy or rules, in billing or other areas. Area and Type are metadata. |
| O-2 | Procedure files go in **our own Blob storage**, not Dataverse file columns and not SharePoint Embedded. An upload/download control is needed. |
| O-3 | Linkage is **record → Procedures → their rules**. The rules that apply to a record must be visible, reviewable and deterministic. Decisions under v1 keep v1. Several Procedures can apply to one record. |
| O-4 | A new version means the record uses the new version, **with per-record overrides**, e.g. grandfathered 2025 rates. The list of Procedures a record is subject to must be visible. |
| O-5 | Records are subject to Procedures **by the invoice's firm** or by **manual association**. |
| O-6 | Procedures are **internal only** and follow our access control. There are no "secure Procedures"; a Procedure is reusable and readable org-wide even when used on a Secure record (confirmed, owner N). Fallback if a problem appears: secure Procedures per Secure record. |
| O-7 | **Export files** are plain files of their export, not Documents. |
| O-8 | **Invoice files** are plain files of the invoice: no Documents, no SharePoint Embedded, no document management. They are listed on the Billing tab. An emailed invoice still creates a Document from the email, and **Submit Invoice** routes its bytes to the invoice's files. |
| O-9 | Lists on the record: the Procedures it is subject to, and its rules in force. |
| O-10 | **Rule-level overrides**: on a record, a rule can be switched off or changed. |
| O-11 | Procedure details: **Issued By** (organization), **External Contact** (contact), **Internal Contact** (contact). No billing flag. |
| O-12 | One shared file table. Version by **work date**. **v2 copies v1's rules for editing.** Firm = the **invoice's firm**. **No business-unit filter.** Blob protections if not difficult. |
| O-13 | **No migration** of dev containers or files. |
| O-14 | **UI/UX is prototyped separately.** Back-end work goes first. |
| O-15 | **Two tables: Procedure and Procedure Version.** |
| O-16 | **A late fee is just an added fee**; the Procedure's rules decide whether it is allowed. |
| O-17 | A deleted core record's **record-scoped rule set** and its record rules are retired (was: its exceptions Procedure). |
| O-18 (A) | Record-specific rule changes live in a **record-scoped `sprk_ruleset`** in the record's secure child tables. |
| O-19 (B) | Add `sprk_billingfirm` and `sprk_billingresponsible` (both → `sprk_organization`) to every core record, on the Billing tab and in record setup. A "Do billing procedures change?" prompt is not R1; changing Procedures stays manual. |
| O-20 (C) | Firm condition on every source; rank key (Level, −Distance, FirmSpecific). |
| O-21 (D, D2) | Removing an override, or retiring a Procedure, re-checks pending and In Review invoices. Decided invoices keep their snapshot. |
| O-22 (E) | A gap **holds** the invoice, with the warning "No procedure associated with this invoice". |
| O-23 (F) | `sprk_createdbyperson` on the four new tables (D-173 shape). |
| O-24 (G) | Ontology's matter rules stay as built in R1. |
| O-25 (H) | Type value **"Internal Standard"**. |
| O-26 (I, J, K) | The add-in offers Submit Invoice (process to be discussed). The email is filed first, and the invoice file follows its source (A + B). Internal-only, Restricted and Private-thread sources are allowed, isolation carried forward. |
| O-27 (M) | Procedure files are never served to flagged-external callers. Invoice files reach "the related external user (it's their invoice)". |
| O-28 (O) | Submit Invoice is **all or nothing**. |
| O-29 (P) | Auditing on the new tables, plus a download audit event. |
| O-30 (Q, Q2) | A pending-scan file shows "Checking file…" with no download. No transitional period for invoice PDFs. |
| O-31 | "Matter" means any core record. Agreement is a core record (stand-alone or under a matter or project), deferred to R2. |
| O-32 | Fields extracted onto an invoice stay readable after a later change to its source (known limit, section 9.1). |

---

## 3. Procedure and Procedure Version

### 3.1 Two tables (O-15)

- **`sprk_procedure` (Procedure)** is the document as a lasting thing: name, area, type, **Applies to**, **Firm**, Issued By, the contacts and its status. Assignments, overrides and its rule set point to it.
- **`sprk_procedureversion` (Procedure Version)** is one issue of it: version number, effective dates, status, the file, the policy versions it published, and its rate card. It has a parental relationship to its Procedure.

**Why two tables:** applicability lives in one row (the resolver reads Applies to and Firm before it picks a version by date); the document's identity doesn't depend on version 1's row; and every link has a clear target: assignment → Procedure; pin → Procedure Version; rule set → Procedure; policy version → Procedure Version; rate card → Procedure Version; file → Procedure Version.

**Invariants (BFF-owned, ADR-002):**

- **I-P1. No overlap.** Effective dates are **DateOnly, half-open [from, to)**. A Procedure's Active versions never overlap. Publishing a new version from D sets the previous Active version's `to` = D, in one BFF operation. If the resolver ever reads two Active versions containing the same date, it **fails closed** (the evaluation faults).
- **I-P2. Field security.** Applies to, Firm, effective dates, status and the file are Create/Update for the BFF application user only, Read for every user (the S7 pattern).
- **I-P3. Rate cards name their firm.** A Procedure of Type = Rate card always names its Firm, whatever its Applies to (`RateCardResolver.cs:125`; `RateCardImportService` `:19,168,298`). `sprk_ratecard.sprk_firm` is set from it.
- **I-P4. Scope lock.** Once any version is Active, Applies to and Firm are locked (D-172 Q9). Changing who a Procedure governs means a new Procedure.
- **I-P5. Deletion and retirement.**
  - Deleting a Procedure Version is refused (**Restrict**) while policy versions or a rate card reference it; the same for `sprk_ruleset → sprk_procedure`. Platform-native Restrict (ADR-002 WP-1(a), checked by a schema `-Verify`), because Signals cite them.
  - **Retiring a Procedure** is the normal end of life. The retire operation is a named BFF reconcile: it sets the Procedure Retired, switches each of its policies Off (so D-170's sweep closes their open Signals as PolicyRetired with no new reads, `SignalClosureService.cs:164-177`), and queues the re-check of the affected pending and In Review invoices (O-21). Decided invoices keep their snapshot.
  - **Retiring one Procedure Version** (withdrawn, not superseded) means it is never selected again. Work dated in its window then has no version in force, which is a gap (section 7.1). Its policies stay enabled, because other versions use them. The same re-check runs for In Review invoices whose snapshot cites it. *(Design choice for peers to confirm; the owner's D2 covers whole Procedures.)*
  - A **Superseded** version stays in force for its own window; only its `to` date changed.

### 3.2 Metadata

**Procedure** (the document):

| Column | Type | Notes |
|---|---|---|
| Name | text | Internal name. |
| Firm-facing name | text | What a firm sees where D-66 / D-79 show the "rule set name". Defaults to Name. |
| Area | choice, reuses `sprk_policydomain` | Billing now; other ontology areas later (`RuleDomain.cs:38-41`). |
| Type | choice | Guideline · Rate card · Billing instructions · **Internal Standard** · Other. |
| Applies to | choice | **All records** (every core record: today's ClientDefault) · **Firm** · **Assigned records only**. Field-secured, scope-locked. |
| Firm | lookup `sprk_organization` | Required when Applies to = Firm and for a rate card. Optional otherwise; when set, the firm condition applies from every source (O-20). Field-secured, scope-locked. |
| Issued By | lookup `sprk_organization` | O-11. |
| External Contact | lookup `contact` | O-11. |
| Internal Contact | lookup `contact` | O-11. |
| Status | **custom** choice (not statecode, which can't be field-secured) | Active · Retired. Field-secured. |
| Created by person | `sprk_createdbyperson` | D-173 shape and protection (O-23). |
| Notes | multiline | |

**Procedure Version** (one issue):

| Column | Type | Notes |
|---|---|---|
| Procedure | lookup, parental | |
| Version number | whole number | Assigned by the BFF. **Alternate key (Procedure, version number).** |
| Previous version | lookup `sprk_procedureversion` | |
| Effective from / to | DateOnly, half-open | Field-secured (I-P1, I-P2). |
| Status | custom choice | Draft · Active · Superseded · Retired. Field-secured. |
| File | Stored File (section 4) | One file per version. Required to publish. |
| Created by person | `sprk_createdbyperson` | |

There is no billing flag (Area and the rule type mark billing) and no "visible to firms" flag on the Procedure: the rule-set flag decides whether a firm sees clause text (D-79), and firms never see a Procedure or its file (D-66).

**New fields on every core record (O-19).** On `sprk_matter`, `sprk_project` and `sprk_workassignment`:

| Column | Type | Use |
|---|---|---|
| `sprk_billingfirm` | lookup `sprk_organization` | The law firm whose Procedures the record's list shows by default (section 6.6). Not used for evaluation (the invoice's firm is) or for submission eligibility (`sprk_assignedlawfirm1/2` stays, FR-17). |
| `sprk_billingresponsible` | lookup `sprk_organization` | The client's billing entity. No R1 consumer. |

Neither column confers access (section 5.2).

### 3.3 §11 justification for the new surface (root CLAUDE.md §11)

**`sprk_procedure`**
1. **Overlap.** `sprk_ruleset` (`RuleSetService.cs:320`) carries a name, scope, firm, dates, visible-to-firms, a source document and a status.
2. **Why not extend it.** A Procedure is the document; the rule set is the rules drawn from it. O-1 needs non-billing documents, files, versions and per-record assignment, which a billing rule set can't hold.
3. **Cost of doing nothing.** No home for the document and its file, and no way to make a record subject to a document (O-1, O-3, O-5).

**`sprk_procedureversion`**
1. **Overlap.** `sprk_policyversion` versions rules, not documents.
2. **Why not one table.** One table grouping versions by a lookup was rejected (O-15): applicability would sit in several rows and identity would hang on v1's row.
3. **Cost of doing nothing.** No grandfathering by version (O-4) and no per-work-date choice of document (O-12).

**`sprk_procedureassignment`** (assignment / override)
1. **Overlap.** A plain N:N between Procedure and record, or `sprk_policy.sprk_matter` (ontology 102b).
2. **Why not extend them.** Neither carries the mode (follow / pin / exclude), the pinned version, the excluded rule or the reason; `sprk_policy.sprk_matter` scopes one rule to one matter and cannot waive a shared rule.
3. **Cost of doing nothing.** No grandfathering, waivers or rule changes per record (O-4, O-10).

**Record-scoped rule set** (extension of `sprk_ruleset`, not a new table): typed record lookups and a record-scope marker on `sprk_ruleset`, as 081 planned. Cost of doing nothing: a changed rule for one Secure record would have nowhere to live that its record's isolation covers (uac-r2 F1-a).

**`sprk_billingfirm`**: existing overlap `sprk_assignedlawfirm1/2` (two slots, the submission rule); reusing slot 1 would silently change who may submit; without it the Procedures list must guess between two firms.

**`sprk_billingresponsible`**: no R1 consumer. Added by owner direction (O-19), which answers question 3.

### 3.4 Name

`sprk_policy` already exists as the ontology's rule container; "Procedure" avoids the clash. ontology-r1 records a component-model §3 entry. "Procedure Library" is retired from the docs (`sprk_ruleset.md:28`, `sprk_ratecard.md:21`, the ebilling CLAUDE.md).

---

## 4. The file store (Procedures, invoices, exports)

### 4.1 One file table, one service, one control (O-12)

**Table `sprk_storedfile`** ("Stored File"). One row per file.

| Column | Notes |
|---|---|
| Parent | Exactly one of `sprk_procedureversion`, `sprk_invoice`, `sprk_billingexport`. |
| Purpose | Procedure source · Invoice PDF · Invoice supporting file · Export file. |
| Firm-visible | Invoice files only; default No. **Yes only for a PDF the firm uploaded through the SPA.** Submit Invoice copies are always No (uac-r2 F1-d). |
| Status | Not yet servable · Servable · Removed. Readers refuse every row that is not Servable. |
| Source document, source communication | Set on a Submit Invoice copy (word-add-in-r1 3b). T7 reads them on every access (section 9.1, B). |
| Source version, SHA at staging | For the Submit Invoice guard (uac-r2 F2-c). |
| File name, content type, size, SHA-256 | |
| Blob path | Server-only. |
| Created by person | `sprk_createdbyperson` (O-23). |

**Field security (uac-r2 F1-b):** every authorization column is Create/Update for the BFF application user only: the parent lookups, purpose, Firm-visible, status, source ids, SHA, size and path. The download checks that the path's parent id equals the row's parent.

**Service and control.** A BFF service writes, reads and deletes blobs, extending `Services/Ai/Sessions/SessionFileBlobStore.cs` (managed identity only, `Azure.Storage.Blobs` already referenced; no new package). One upload/download PCF built from the shared library's `FileUpload/FileUploadZone` and `UploadedFileList`; its UX comes from the prototype (O-14).

**§11:** (1) `sprk_document` is SharePoint Embedded document management, ruled out (O-2, O-8); `SessionFileBlobStore` holds AI session bytes. (2) A parent row cannot hold several files. (3) Without it there is no place for these files outside document management.

### 4.2 Blob layout and protection

- **Containers:** `procedures`, `invoice-files`, `billing-exports`. **Path:** `{tenantId}/{parent table}/{parent id}/{file id}`.
- **Write-once:** a file is never overwritten; a new file is a new row.
- **Malware scanning (Defender for Storage, on upload):**
  - The scan result is a blob index tag. The download route and every processing step read it on demand; nothing is queued, and there is no Event Grid receiver in R1.
  - Pending means wait (firm list: "Checking file…", content 409 `file_scan_pending`); Malicious means refuse (the file drops out of lists, content 404).
  - **A missing tag refuses.** "Scanning not enabled" is an explicit per-environment setting, not inferred; only then is a file served as "Not scanned" (uac-r2 Q5).
- **No WORM policy in R1** (it would block deleting files with their parent). Soft delete is on (known limit K3: compensated bytes stay 7 days).
- **Identity:** the BFF managed identity only. No account key, connection string or SAS (`SessionFileBlobStore` `:704-720`). The browser never receives a storage URL. Content Understanding gets the **bytes inline** (`ContentUnderstandingClient.cs:104,255`).
- **Provisioning (to verify in the provisioning task, uac-r2 Q5):** the account's network rule is `defaultAction: 'Allow'` with no subnet (`storage-account.bicep:64-66`); the role is assigned at account scope; the account is shared; "dev uses the existing account" is unverified; `customer.bicep:313` already passes the principal (v4's claim was stale); reading the scan tag may need more than Storage Blob Data Contributor. Containers and the role go into environment setup, with a hand-off to customer-provisioning.
- **Retention and deletion:**
  - A file lives as long as its parent row. Deleting through the BFF deletes the files with a job (ADR-004).
  - **Orphan reconcile** (scheduled, ADR-036, ADR-002 WP-5), for parents deleted outside the BFF (uac-r2 F2-a): "gone" means a successful not-found read, never a fault; the grace period is at least the submission job's maximum retry horizon; non-terminal submissions are skipped.
  - ADR-015 doesn't govern these stores: they are business records, not AI work history.

### 4.3 Upload and download

- **Upload:** check Write on the parent → stream to a blob keyed by the new file id → write the Stored File row → if the row write fails, delete the blob (compensation job, orphan backstop).
- **Download (internal route):**
  1. **Refuse every caller with `sprk_isexternal = true`**, for every file kind (O-27; uac-r2 F1-e). Flagged-external users get invoice files only through the firm route (section 9.3). A fault reading the flag refuses.
  2. Check Read on the parent with the caller's own rights, the gate variant (`GetCallerRightsForRecordsAsync`, `CallerRecordAccessProbe.cs`; uac-r2 Q5).
  3. For a Submit Invoice copy, apply T7 through its source ids (section 9.1, B).
  4. Check Status = Servable, the scan tag, and that the path's parent matches the row.
  5. Write the download audit event (O-29), then stream.
- **Auditing (O-29):** Dataverse auditing on `sprk_procedureassignment` and `sprk_storedfile`; a download audit event per served file on both routes.

---

## 5. Access control (O-6)

### 5.1 Rows

| Row | Owner and visibility | Notes |
|---|---|---|
| Procedure | Owned by the uploader's business unit; internal roles get org-level Read. Internal only. | Not a secure child (O-6, owner N). Holds only reusable documents now, so org-level Read leaks nothing record-specific (uac-r2 F1-a resolved by A). |
| Procedure Version, its Stored File | Follow the Procedure. | |
| Rule set, policy, policy version of a Procedure | Unchanged ownership. | |
| **Record-scoped rule set** and its policies and policy versions | **Secure child of its record**, at Deep, as 081 planned (D-141 Q11): lineage, role row, census, creator person. | Holds one record's rule changes, including clause text. |
| Assignment / override | **Secure child of the record it names.** | Its existence on a Secure record is the record's information. |
| Stored File (invoice) | **Secure child of its invoice**; a leaf. Owner from `ForParents(core)` inside the Submit Invoice transaction. | Access follows the invoice; T7 narrows a copy further. |
| Stored File (export) | **Not** a secure child (D-85 / #1974); follows the export's ownership (uac-r2 F4-c). | |

**Writes are checked as the caller (uac-r2 F1-c).** Assignment, override and change-rule writes check Write/AppendTo on the record (the gate variant, the same 404), Read on the Procedure or rule, and the billing privilege. The row is created as the application, owned by the resolver's team, with creator person = the caller. Creates route through `IRecordOwnershipResolver`.

**Census waiver (ontology blocking O1).** `RecordOwnerAssignmentCensusTests.cs:128-140` waives rule sets on the ground that "a billing rule is never filed under a matter". Record-scoped rule sets break that. The waiver and its guard are rewritten: record-scoped rule sets get lineage with uac-r2 and route through `IRecordOwnershipResolver`; Procedure rule sets keep the waiver.

**Depth list.** 081's NFR-05 depth list is restored and extended to every new secure child table (uac-r2 F1-a).

**CIAM.** The new tables stay out of `ExternalModuleRegistry` (uac-r2 Q1). **Flagged-external systemusers** reach no internal-only record (owner reversal of #2179 (c)). uac-r2's Dataverse-side enforcement (task 191) covers `sprk_procedure`, `sprk_procedureversion`, Procedure Stored Files and the rest of section 5.2; the BFF refusal in 4.3 is the route-level half.

### 5.2 Census for uac-r2 task 191

"Root column per core type" is the column the row hangs from for `sprk_matter` / `sprk_project` / `sprk_workassignment`.

| Table / column | Parent | Root column per core type | Secure child | Creator person |
|---|---|---|---|---|
| `sprk_procedure` | none | none (org-level Read) | No | Yes |
| `sprk_procedureversion` | `sprk_procedure` (parental) | none | No | Yes |
| `sprk_procedureassignment` | the record | typed lookups `sprk_matter` / `sprk_project` / `sprk_workassignment`, exactly one set; Cascade or planner Delete on record delete | Yes: lineage on the three lookups; `CitationLookups` = procedure, pinned version, excluded policy; role JSON with a live probe; census; delete planner | Yes |
| `sprk_ruleset` (record-scoped) | the record | new typed lookups `sprk_matter` / `sprk_project` / `sprk_workassignment`, exactly one set, **RemoveLink**, plus a record-scope marker (name with ontology-r1; 9a proposed `sprk_isexceptions`) and a no-plugin orphan sweep that retires sets whose record is gone (O-17) | Yes, at Deep (081 plan); its `sprk_policy` and `sprk_policyversion` follow it | Yes (S9) |
| `sprk_storedfile` (invoice purposes) | `sprk_invoice` | via the invoice: `sprk_matter` / `sprk_project` / the work-assignment regarding stamp (`InvoiceFamilyCoreRecordGuard.CoreRecordLookups`) | Yes: lineage on `sprk_invoice` only; role, census, person entries; a leaf | Yes (K6: a contact uploader can't be recorded) |
| `sprk_storedfile` (export) | `sprk_billingexport` | none | No (F4-c) | Yes |
| `sprk_storedfile` (Procedure source) | `sprk_procedureversion` | none | No | Yes |
| `sprk_storedfile.sprk_sourcedocument`, `.sprk_sourcecommunication` | — | — | Citation lookups, not access-conferring; read by T7 | — |
| `sprk_ruleset.sprk_procedure`, `sprk_policyversion.sprk_procedureversion`, `sprk_ratecard.sprk_procedureversion` | — | — | Citation lookups (Restrict), not access-conferring | — |
| `sprk_billingfirm`, `sprk_billingresponsible` on the three core tables | — | — | **Not access-conferring** (below) | — |

**`sprk_billingfirm` and `sprk_billingresponsible` confer no access.** A lookup to `sprk_organization` on a core record can confer access through task 043's org-expansion term if it is in the access registry. So:
- neither column is added to the access registry;
- no grant or share path reads them;
- giving a firm access through either column would be a separate owner decision, through uac-r2's registry review.

**Delete contract (uac-r2 F4-b).** Assignment rows → Delete with their record (Cascade or the planner). The retire writer is a named reconcile. uac-r2 updates `core-record-delete-design.md`: assignment rows → Delete; record-scoped rule-set rows kept as secure child rows with the secure-child delete rule (its §12); the new tables go on the hard-deny list. Assignment-row lookups: the record lookups cascade; the rule lookup cascades or faults (a nulled rule must never read as a Procedure-level Exclude); the pinned-version lookup is Restrict.

---

## 6. How a record becomes subject to Procedures and rules

### 6.1 Three sources, one firm condition

| Source | How | Stored? |
|---|---|---|
| **All records** | Applies to = All records | No: computed |
| **Firm** | Applies to = Firm and Firm = **the invoice's firm** (`sprk_invoice.sprk_vendororg`) | No: computed |
| **Manual** | An assignment row: Procedure → core record | Yes |

- **The firm condition applies to every source (O-20).** A Procedure that names a firm applies only to that firm's invoices, whether it comes from all records, the firm or a manual assignment.
- **Firm matches are computed at evaluation time, not copied onto records.** Copying would need a trigger on every firm change; without plugins that means a reconcile job and a stale window.
- **A record billed by two firms** gets each firm's Procedures on that firm's invoices.
- **The default list firm** is the record's `sprk_billingfirm` (O-19). It only sets the default view.

### 6.2 Procedure-level overrides (O-4)

| Mode | Meaning | Example |
|---|---|---|
| **Follow latest** (default for a manual assignment) | The version in force on the work date. | Billing instructions; v2 applies from its date. |
| **Pinned** | This Procedure Version, whatever is newer. | Grandfathered on F's 2025 rate card. |
| **Excluded** | A Procedure that would apply by all-records, firm or a parent assignment does not apply here. | F's standard travel policy waived here. |

### 6.3 Rule-level overrides (O-10)

A **rule** is a `sprk_policy` in a rule set. It keeps its identity across the Procedure's versions (section 7.2), so a rule-level override survives a new version.

| Action | What is stored | Effect |
|---|---|---|
| **Exclude rule** | An override row: rule → record, mode Excluded, reason. | The rule does not run for this record. |
| **Change rule for this record** | (1) An Exclude row for the base rule. (2) A **record rule**: a copy of the base rule's current parameters, edited and published in the record's **record-scoped rule set** (created on first use). It stores **based on** = the base rule's policy version, and carries the base rule's firm condition and rank. Both are written in one BFF operation. | The record rule runs **in the base rule's slot**: same firm condition, same level. The Exclude removes only the base rule (ontology 5). |
| **Remove an override** | The override row is deleted (its content survives in past snapshots). A record rule is switched off. | The base rule runs again; affected pending and In Review invoices are re-checked (O-21). |

- **Why copy, edit and exclude:** the record rule is an ordinary rule (typed, validated D-27, immutable once published, cited by Signals, reviewable), and checklist types add up across sources, so the base rule must be excluded.
- **Taking the base rule's slot** means a record rule never applies to other firms' invoices and never displaces a rule the user didn't touch. v4's Replace-type known limit is gone.
- **Record-rule codes** start a new ontology D-13 key.
- **Drift:** when the base rule gets a new version, or is retired, the record rule keeps running. The read model computes **stale** (based on ≠ the base rule's current version) and **base retired**; nothing is stamped (if ever stamped, only on change, D-61).

**Rule types the examples need (ontology 6):**

- **AddedFee (O-16).** Fee kind (late fee, administrative fee, interest …) and an allowance: not allowed, a cap (amount or percent), or allowed after N days. Option value 100000015 (`RuleType.cs:93`), mapped in `RuleDomains.DomainOf` (an unmapped value throws), pinning test extended, PR to ontology-r1. **"Not allowed" and "after N days" ship only if extraction gives the fee kind and the due date.**
- **Discount.** Option value 100000016. **A check only:** "agreed discount not applied".
- Catalogue additions go through D-27 and ontology-r1. "Approve travel" works today through ExpenseRule.

### 6.4 Inheritance: direct association only (unchanged)

A work assignment filed under project P is subject to P's manual assignments and overrides at distance 1, not to those of P's matter (D-172; `CoreRecordChain`; uac-r2's `NamePairParentAsync`). A work assignment can have **two d1 parents**: if they conflict, **Exclude wins**; two different pins raise a blocking gap (section 7.1).

### 6.5 Determinism

- **Rank key:** (Level, −Distance, FirmSpecific). Levels: Manual d0 > Manual d1 > Firm > All records.
- **One Procedure, several sources:** the highest level wins.
- **Assignment rows at different distances:** the nearer row wins, whatever its mode. An Exclude at d1 is lifted only by a d0 assignment of the same Procedure.
- **Two rows at the same distance for the same Procedure** are refused by the BFF (I-A1); for the two-d1-parent case, see 6.4. Rule-level overrides follow the same rules.
- **Overlapping Active versions** fail closed (I-P1).

### 6.6 The lists on the record (O-9; UX from the prototype)

Two read models, both produced by the evaluation's own resolver, **read as the caller**, with their routes in the `RouteAuthorizationGuardTests` ledger (ontology O7):

- **Procedures:** every Procedure the record is subject to (sources, inheritance) for its `sprk_billingfirm` by default, or a chosen firm; the version in force today; its overrides.
- **Rules in force:** for a chosen date and firm, every rule with its source Procedure Version, marked displaced, excluded, changed, stale or base retired.

---

## 7. From Procedure to rules, and how evaluation finds them

### 7.1 The resolver

`ApplicableRules(record, firm, workDate)` reads stored rows only.

```
chain    = CoreRecordChain(record)                       // d0 + direct parents d1 (section 6.4)
rows     = assignment/override rows on the chain          // read in full; ANY read fault → the evaluation faults
procs    = (Applies to = All records
            ∪ Applies to = Firm AND Firm = invoice firm
            ∪ manually assigned on the chain)
           AND (Procedure.Firm is empty OR Procedure.Firm = invoice firm)      // firm condition, every source
         − Excluded (nearer row wins; between two d1 parents Exclude wins)
for each procedure (status Active):
    procVersion = the Pin (nearer wins; two different d1 pins → blocking gap)
                  OR the Active Procedure Version with from ≤ workDate < to (two found → fault)
    if none:  contributes nothing for this subject AND raises a gap (below)
    level    = Manual d0 | Manual d1 | Firm | All records
    rules   += for each enabled policy of the Procedure's rule set: its policy version for procVersion (7.2)
    rateCard = procVersion's published rate card (always firm-named, I-P3)
rules    −= rules Excluded on the chain (nearer wins)
rules    += record rules of the chain's record-scoped rule sets, each in its base rule's slot (6.3)
→ precedence (7.3)
```

- **Fails closed.** Any failed read of chain, assignment, override, Procedure or version rows faults the evaluation; the job retries; nothing is judged on a partial set (`BillingFactLoader.cs:243-244`).
- **A gap holds the invoice (O-22).** When an applicable Procedure has no version in force on the date, a pinned version is retired, or two d1 pins conflict, the system raises a **blocking internal finding** on `BILL-SYSTEM-SUBMISSION` (the D-115 pattern) naming the Procedure and date, and the invoice shows **"No procedure associated with this invoice"**. The invoice cannot be approved until a re-check clears the gap.
- **`BILL-SYSTEM-SUBMISSION` is exempt** from the Procedure model (ontology blocking 2b): it has no rule set (D-125) and keeps its `sprk_currentversion` pointer chain.
- It replaces today's rule-set eligibility (`BillingFactLoader`, ClientDefault and Firm) and the planned 082 selection.

### 7.2 Rule versions under Procedure Versions (re-keys today's version chain)

**Today**, a rule (`sprk_policy`) has one chain of versions: an edit creates a version in force from now (`RuleSetService.cs:1296-1297`); publish refuses an earlier effective date (`:1628-1631`), end-dates the predecessor and moves `sprk_currentversion` (`:1720-1731`); the loader takes current or end-dated versions (`BillingFactLoader.cs:116-135`), and the evaluator picks by date (`InvoiceComplianceEvaluator.cs:397-399`). That chain cannot hold two Procedure Versions' rules side by side.

**For billing rules, this changes to:**

- Each policy version is linked to **one Procedure Version** (`sprk_policyversion.sprk_procedureversion`), with an explicit **published** marker.
- The Procedure Version's [from, to) is the rule's window; for a pinned record, the pinned version's rules apply whatever the date.
- **Corrections** add a policy version under the same Procedure Version with its own effective-from; within it, the latest published policy version with effective-from ≤ work date applies.
- **`sprk_currentversion` means "latest published"**, for display only (ontology-r1 amends D-14). `RuleSetService` edit, publish, `IsDraft` and `Predecessor` are re-keyed per (policy, Procedure Version).
- **Readers to re-key:** `BillingFactLoader` `:97`, `:116`, `:123`, `:383`; `RuleSetService` `:2039` (the D-79 firm clause `wasInForce`). Display only: `RuleSetService` `:2134`, `:773`. ontology-r1 fixes `OntologyAdminClassificationService` `:658/:685/:705`.
- **Ratchet ArchTest:** every `sprk_currentversion` reader outside an allowlist applies `RuleDomains`.
- **Ontology's readers are unaffected** and stay Ontology-only: `SignalReevaluationJob.cs:226-235`, `SignalClosureService.cs:54-56` (confirmed by ontology-r1).
- **Ontology-area Procedures are document-only in R1 (O-24):** the validator refuses `sprk_procedureversion` on Ontology-domain policies. Billing never writes `sprk_policy.sprk_matter`.
- **Precondition:** our 070 reaches master.

**v2 copies v1 (O-12):** creating Version 2 copies Version 1's rules as drafts. On publish, kept or edited rules become new policy versions of the **same** policies under Version 2; new rules are new policies; a rule dropped by Version 2 stays **enabled** with no policy version under Version 2 (work in Version 1's window still sees it; its open Signals stay open). A rule is switched off only when its whole Procedure is retired, or when it is a record rule whose override is removed.

**Drafting** (`BillingRuleDraftJobHandler`) is keyed **per Procedure Version** (today the key is the rule-set id, `:77-85`, and "already drafted" short-circuits, `:119-130`). Its source is the version's Stored File, not `sprk_sourcedocument` (`RuleSetService.cs:614`).

**Rate cards:** importing a workbook creates that Procedure Version's card; Version 2's card can start as a copy. `sprk_ratecard` gets `sprk_procedureversion` in place of the core-record lookups and the planned scope column, and keeps `sprk_firm`.

### 7.3 Several Procedures on one record

The reviewed precedence (`rule-set-scope-design.md` §3.3) is kept, re-keyed to the rank key.

- **Checklist types add up.** **Limit types replace** in rank order. A broken or downgraded specific rule never removes the general one.
- **ExpenseRule** keeps its split as written in `rule-set-scope-design.md` §3.3; 082's POML quotes it.
- **Rates:** 085's tiers re-keyed. Different amounts in the deciding tier give Ambiguous.
- **Ties mean "all apply".** The (policy code, id) order is output order only.

### 7.4 The snapshot

- **On the submission (full):** with the findings (`sprk_findingsjson` already carries `policyVersionId` and the clause, `InvoiceSubmissionPersister.cs:1038-1044`): for each rule applied, the policy version, Procedure, Procedure Version, source, level, and the **content** of every override that decided it (mode, rule, reason, distance, chain record); rules displaced or excluded; gap findings.
- **On the Decision Record (bounded summary):** `sprk_factsnapshot` is capped at 100,000 characters, so it carries a summary and a pointer to the submission (ontology O2).
- **Firms never see the basis.** The firm findings projection is an explicit allow-list DTO built field by field, with a negative test that no override reason, Procedure name or Procedure id appears (R3).

---

## 8. Signals, Decision Records and versions

### 8.1 What they cite

Unchanged at the core: the immutable policy version (clause and location), reason codes and facts. Added in the facts: the Procedure Version and the basis (source, level, override content). Firms see only clause text under the visible-to-firms flag, with the rule set's firm-facing name; a record-scoped rule set's firm-facing name is neutral ("Billing instructions for this matter").

### 8.2 Versions over time

- **Which version judges a line:** the one whose [from, to) contains its work date (else the invoice date). 2025 work billed in 2026 is judged on Version 1.
- **Decided invoices stand:** Decision Records are append-only; Signals cite Version 1's policy versions.
- **Re-check triggers (O-21):** removing an override, retiring a Procedure, or retiring a Procedure Version queues a **targeted re-check** of the pending and In Review invoices whose records or snapshots are affected. Findings from a retired Procedure close on that re-check. The re-check is a job (ADR-004 `IJobHandler`); §11: today's triggers (`InvoiceSubmissionPersister.cs:248-251`, task 101 corrections) don't fire on these changes, so invoices would stay judged under rules that no longer apply.
- **Publishing a new version does not re-check** In Review invoices (unchanged).
- **A pinned override** keeps its version whatever the dates.

---

## 9. Invoices as files (O-8)

### 9.1 Submission routes and Submit Invoice

| Route | Behaviour |
|---|---|
| Firm via SPA | Multipart to the existing external submission routes. The PDF becomes the invoice's Stored File (Invoice PDF, Firm-visible = Yes). The 051 wizard's UI is unchanged. |
| Internal upload | Billing tab or the invoice → invoice files (Firm-visible = No unless it is the firm's PDF). |
| Email attachment | The attachment stays a Document with its email. **Submit Invoice** copies its bytes into the new invoice's files and starts the submission. The Document never moves. |

**Surfaces (owner I, L).** Submit Invoice is offered in the communication UI and in the Outlook add-in. In the add-in the user flags an attachment as going to the invoice's files instead of becoming a Document. **The process is still to be discussed with the owner** (section 15); this section fixes only the server contract:

- Submit Invoice is a **separate endpoint keyed on (communication, attachment)**, never part of `/office/save` (whose replay and duplicate keys can't carry it, `OfficeService.cs:1239-1249`), and it never anchors on a returned document id (an attachment save can resolve to an identical document from another email, `OfficeService.cs:1025-1073`).
- Invoices are created only by submission (FR-17, FR-20). The add-in's save onto `sprk_invoice` (`OfficeDocumentPersistence.cs:47`) and invoice quick-create (`QuickCreateRequest.cs:180`) are decided with the process (L).
- The goal (owner L): the email is associated with the core record **and** with the invoice. The communication is filed to the core record (A below); the invoice's Stored File carries the source ids.

**Sources (owner K).** Internal-only, Restricted (a task-130 document whose email couldn't be identified) and Private-thread sources are **allowed**. Their isolation is carried forward, not refused: the copy stores the source document and communication ids, is Firm-visible = No, is never served to the firm, and stays out of search, indexes and chat grounding unless T7 passes. Extraction still reads the bytes.

**The two halves of J (owner: A + B).**
- **A, file first.** When the communication is unfiled, Submit Invoice files it to the target core record M **before commit**, through `PATCH /api/communications/{id}/filing` with the caller's rights. The add-in's save then reports AlreadyFiled.
- **B, the file follows its source.** T7 reads the stored source ids on **every access** to the copy, so a later refile, Restricted flag, internal-only flag or Open → Private change is honoured for the bytes. This needs a T7 rule for invoice files (uac-r2).

**All or nothing (owner O), step by step:**

1. **Confirm.** The caller must Read the source document and its communication (#2150). Then: the D-140 filing chain, with the thread hop through `RecordThreadFiling.ParentsOf` (#2150 merged; a Direct thread contributes nothing), and `ExistingDocumentIsolationCheck`. Answers: 409 `document_core_record_mismatch`; 503 on a read fault. If the communication is unfiled, file it first (A).
2. **Staging (job).** Copy the bytes to a blob under a Stored File id that is **deterministic from the submission id**. Record the source document's version and SHA (uac-r2 F2-c). The blob is unreachable without a Servable row.
3. **Pre-transaction check (job).** Re-run D-140 (`ResolveFilingAsync`, uac-r2 F1-f) and the isolation check, and read the source's Restricted, internal-only and privacy state (uac-r2 condition 2). Under owner K these flags are not refusal grounds; they are read so the copy is created with the right carried isolation, and T7 reads them again at each access.
4. **Commit (one Dataverse transaction).** The invoice, its lines, the approval, the submission update and the **Stored File row as Not yet servable**, plus the source-document guard on the version recorded at staging (`IfRowVersionMatches`, `InvoiceSubmissionPersister.cs:497-513`). If the guard fails, re-stage under a new file id and retry (F2-c). If the transaction fails, delete the staged blob (compensation; orphan backstop). The verified-pointer read stays (`InvoiceSubmissionProcessingJobHandler.cs:539-544`).
5. **Re-run immediately after commit, before any after-step.** No totals, Signals, Decision Record, indexing or notification runs until it passes. It re-runs D-140 and compares `SecureRootsAboveAsync(source document)` with the committed Stored File's roots (uac-r2 Q4).
   - **Pass:** flip the Stored File to Servable with an If-Match write, then run the after-steps as today.
   - **Fail, or any fault** (exception, throttle, timeout, unreadable flag; uac-r2 condition 1): **undo everything**, in this order: the Stored File row and blob, the approval, the lines, the invoice. Mark the submission Failed with an admin note. Nothing irreversible has run yet, so the undo is complete.
   - **Undo complete or held (condition 3):** if any delete fails, the submission stays Failed and the file stays Not yet servable until compensation finishes.
6. **Resume (uac-r2 F2-b).** The resume path (`InvoiceSubmissionPersister.cs:235-238` → `ResumeAsync`) runs step 5 and the flip. A detector reports rows still Not yet servable after N minutes.

**Which races the transaction catches** (reviews §7): the in-transaction guard covers only the source document row. A refile between two ordinary records, a thread join to an ordinary record, a thread's regarding change and any Restricted/internal-only/privacy change write no document row, and a refile or join to a Secure record is caught only partly. So step 5 is the main check, not a backstop.

**What cannot be withdrawn** once the after-steps run: Signal history and the AutoRejected Decision Record (append-only), the Service Bus indexing job and its AI Search entry, and notifications once task 132 has a real sink. Step 5 runs before all of them. The 050a re-file (owner change, share mirroring) is retired, since bytes are copied.

**Remaining races and known limits:**
- **A change after step 5 passes.** The invoice is then legitimately the core record's. The bytes stay correct through B. **The fields extracted onto the invoice and its lines do not follow the source:** they are readable by the core record's readers, narrowed by the invoice's own Access Permission. Disclosure happened at submit, as with a download (owner-confirmed, O-32; uac-r2 K2). To hide an invoice later, set its Access Permission to Restricted; D7 and uac-r2 task 191 then hide it from flagged-external users.
- **A change during the re-run itself** is in the same position.
- **K3:** soft delete keeps compensated bytes for 7 days.
- **K4:** unlinked Secure-owned rows are reported by the share job; they are held, not served.
- **K6:** a contact uploader can't be recorded as creator person.
- *(v4's Outlook save race K5 is closed by A; uac-r2's K5, Direct-thread over-refusal, is closed by `ParentsOf`.)*

### 9.2 Processing and viewing

- **Extraction:** Content Understanding gets the bytes inline, from every source kind.
- **Viewer:** the bounding-box viewer (108) loads the PDF through the internal download route.
- **Indexing:** the invoice index stays the structured record (D-44's second half). The indexing job takes no bytes from a Submit Invoice copy unless T7 passes (owner K).
- **Lost by design:** invoice PDFs leave the core record's Documents, the files index and AI over record documents. Related work ranks the record's documents, not the invoice PDF (`InvoiceRelatedWorkService.cs:78-81`).
- **Invoice lines:** a file belongs to the invoice, never to a line, so #2047 is unaffected.

### 9.3 Firm routes (settled with uac-r2 and R3)

- **Routes:** `GET /api/v1/external/invoices/{id}/files` (list) and `GET .../files/{fileId}/content` (download). No upload route; uploads go through submission.
- **Systemuser caller (036 path):** the same 036 machinery (`AccessibleRecordSetService`) gives the caller's Read on the invoice and its root, the I-14 Restricted veto (which covers flagged-external users here) and the task 187 invoice rule. Unknown rights (`0x8004d24d`) fail closed as 036 does.
- **CIAM contact:** the ReadOne firm match on `sprk_vendororg` (`ExternalInvoiceSubmissionAuthorizationFilter.cs:100-104`) → `RightsOn(core) ≥ Read` after vetoes → the task 187 rule.
- **One file gate for both:** Invoice PDF + Firm-visible + Servable + scan clean + parent matches. Pending scan: listed as "Checking file…", content 409 `file_scan_pending`. Malicious: not listed, 404.
- **Uniform answers:** an invisible invoice or file gets the same 404 after the same work; a read fault gets 503. No SAS.
- **Task 188 ArchTest:** the guard names this route's gate method, with must-fire (a systemuser without Read gets 404; a flagged-external systemuser on a Restricted root gets 404) and must-not-fire controls. Added in the PR that builds the route.
- **Retired in the same change:** the four `/api/v1/external/invoices/{id}/documents…` routes (list, content, upload, versions; `ExternalProjectDataEndpoints.cs:378-412`). The BFF change is ours, reviewed by uac-r2; the SPA change is R4's (or R3's). Dev deploys BFF then SPA back to back through R3; until the SPA ships, the old SPA's invoice-documents call returns 404.
- **Flagged-external users** get invoice files only here (O-27).

## 10. Export files (O-7)

`sprk_billingexport` (023) gets its file as a Stored File (Export file), **not** a secure child; it follows the export's ownership (F4-c). Export files are internal: Firm-visible is always No, so they never appear on the firm route. The E-Billing Output Files container and key (D-55, D-67, 028) retire from code; the owner-created dev containers stay for the owner to remove (O-13).

## 11. Search and AI

- **Nothing reads the file at evaluation:** clauses are copied at drafting.
- **No Procedures index** (ontology-r1: not needed).
- **Invoice files:** future work (O-8); Submit Invoice copies stay out unless T7 passes.

---

## 12. What changes in existing decisions and completed work

| Item | Change |
|---|---|
| D-44 | Invoice PDFs move to invoice files. The structured invoice index is unchanged. |
| D-55, D-67 | Containers and keys retire from code; the dev containers stay (O-13). |
| D-145 | Withdrawn. |
| D-146 | Deleting a core record deletes its assignment rows and retires its record-scoped rule set and record rules (O-17). Reusable Procedures, rule sets and rate cards are untouched. |
| D-120 | Rate cards are no longer tied to a core record. |
| BU filter: D-171 pts 4 and 6, D-172 Q13, D-141 Q1, S3, S4, S8, 087 | Dropped (O-12). Nothing of uac-r2's relies on S8 (uac-r2 Q7). |
| D-141 Q11 | Kept for **record-scoped rule sets** (O-18). |
| D-140 | Kept; the thread hop moves to `RecordThreadFiling.ParentsOf` (#2150 merged). |
| D-172 | Direct-association inheritance kept. Record scope on rule sets is replaced by assignments plus record-scoped rule sets. The S6 backfill and ontology D-174 / D-147 are moot. |
| D-170 | Applies when a Procedure is retired (its policies switched Off by the retire reconcile) or a record rule is switched off. A rule dropped by a newer version stays enabled. |
| D-14 | ontology-r1 amends it: for billing, `sprk_currentversion` means "latest published". |
| Ontology rows | ontology-r1 records one superseding row for D-171/D-172/D-174/D-173/D-14 when EB-D-148 is final. |
| Decision ids | New rows are project-prefixed: **EB-D-nnn** (ontology O5). |
| `rule-set-scope-design.md` | Kept: §3.1 chain, §3.3 precedence (re-keyed). Replaced: §2 S1–S4, S6, S8; §4.1–4.5; §5.2–5.4. S9 creator person carries over. |
| 019 / 076 / 077 | Rule set gains `sprk_procedure` (or the record lookups, if record-scoped) and loses `sprk_sourcedocument`. Policy version gains `sprk_procedureversion` and the published marker. The version chain is re-keyed (7.2). Rule Review is reached from the Procedure or the record. |
| 028 | Replaced by the blob containers. |
| 044 / 049 / 051 | The upload target becomes the file store. Processing reads bytes from it. 051's UI is unchanged. |
| 050a | Becomes Submit Invoice (9.1). The re-file is retired. Its D-140 chain and isolation check are kept. |
| 075 | Drafting is keyed per Procedure Version and reads the version's Stored File. |
| 078 | The card belongs to a Procedure Version and keeps its firm. |
| 102 (D-74 adjustment rule list) | Uses the resolver, not its own scope filter. |
| 108 | Loads through the BFF download route. |
| External invoice-document routes (R3 task 031, uac-r2 187) | Retired with the firm file routes (9.3). |
| Docs | "Procedure Library" retired (3.4). |
| Dev data | Recreated, not migrated (O-13). |

## 13. Effect on open tasks 079–087 (sketch; re-planned after the owner finalizes)

| Task | Becomes |
|---|---|
| 080 schema | `sprk_procedure`, `sprk_procedureversion`, `sprk_procedureassignment`, `sprk_storedfile`; record lookups and the record-scope marker on `sprk_ruleset`; `sprk_procedure` on rule set; `sprk_procedureversion` and the published marker on policy version; `sprk_procedureversion` on rate card; `sprk_billingfirm` and `sprk_billingresponsible` on the three core tables; custom statuses, DateOnly dates, the alternate key; field security (I-P2, F1-b); Restrict and delete behaviours (5.2); creator person (081 adds the tables to `Set-ChildRecordCreatorPersonSchema.ps1` and `StampedChildTables`, asserting zero non-null values before securing); auditing. Notices to ontology-r1 and uac-r2 before and after. ontology-r1 reviews the schema in full. |
| 081 | Procedure service (versions, I-P1–I-P5, copy-forward, retire reconcile); assignment and override service (pin, exclude, change rule into the record-scoped rule set), caller-checked (F1-c); secure child registration for assignment rows and record-scoped rule sets (F4-a); census waiver rewrite (O1); NFR-05 depth list. |
| 082 | The resolver (7.1) with the firm condition, gaps and holds, and the re-keyed version selection (7.2); `BILL-SYSTEM-SUBMISSION` exemption; the ratchet ArchTest. |
| 083 | Firm clause via the resolver, with the firm-facing name. |
| 084 | Waits for the prototype (O-14). The back end exposes the read models of 6.6. |
| 085 | Rate-card tiers re-keyed. Scope column and backfill dropped. |
| 086 | What remains: secure child registration for invoice Stored Files. |
| 087 | Dropped. |
| New | File store service, control and orphan reconcile; Stored File schema; provisioning (storage account checks in 4.2, customer-provisioning hand-off); Submit Invoice (server contract; the add-in surface after the owner's process discussion); invoice submission and exports to files; firm file routes and retirement of the four document routes (uac-r2 review, task 188 entry, R3 deploy pairing); internal route's flagged-external refusal; the targeted re-check job (8.2); AddedFee and Discount types; docs clean-up. |

## 14. Coordination: v5 re-check requested (2026-10-21)

- **ontology-r1 (scoped):** sections 0, 3, 6.3–6.5, 7, 8.2, 12; the record-scope marker name; whether the single-version retirement in I-P5 matches D-170's reading.
- **uac-r2 (full):** sections 4, 5 (census 5.2), 9.1 (all or nothing, T7 rule for copies), 9.3, and the open items in 15.
- **word-add-in-r1:** section 9.1.
- **SPA-r3:** section 9.3. **customer-provisioning:** the storage hand-off in 4.2.

## 15. Questions still open

- **Owner — the Submit Invoice process (I, L):** how the user flags an attachment in the add-in and the communication UI, and what happens to the add-in's save onto `sprk_invoice` and invoice quick-create. To be discussed separately; the server contract in 9.1 does not depend on it.
- **Owner — export files and flagged-external users (M wording).** The owner's words say invoice **and export** files reach "the related external user". An AP export batch spans many firms' invoices, so no single related external user exists. v5 keeps export files internal (never on the firm route). Owner to confirm, or name the case.
- **Owner — clearing a gap hold.** v5 lets a re-check clear a gap (O-22). Whether an Override (D-24 / D-39) may also release a held invoice is not decided.
- **uac-r2 — condition 2 under owner K.** uac-r2's condition 2 assumed Restricted, internal-only and privacy flags would refuse. Under K they do not; v5 reads them in both checks only to carry isolation forward (9.1 step 3). uac-r2 to confirm.
- **uac-r2 — T7 when the source is deleted.** With the source document or communication gone, the copy has nothing to follow. Proposed: the lookups are RemoveLink, and a copy whose source is gone keeps the strictest isolation last read (served only to the invoice's readers who pass the invoice's own Access Permission). uac-r2 to decide.
- **Engineering — the guard's version bump.** Live proof that the source-document guard's write moves `versionnumber` is still outstanding (050a K1 note).
- **ontology-r1 — extraction for AddedFee.** Whether extraction gives the fee kind and the due date decides whether "not allowed" and "after N days" ship.

- **Owner + uac-r2 — derived rows from an already-isolated source (gap found by uac-r2, 2026-10-21).** v5 carries a Restricted, internal-only or Private-thread source's isolation onto the **Stored File only** (9.1 step 3). The invoice header, lines, approval and findings extracted from that source would still be readable by the core record's ordinary readers, and by flagged-external users, which conflicts with uac-r2 round 113 (no flagged-external user reaches an internal-only record). This differs from the owner-confirmed limit in 9.1, which is about a source that changes *after* submit. Options:
  - **(a) Carry the isolation onto the invoice itself (recommended).** At commit, in the same transaction, the invoice takes the strictest isolation of its source; lines, approval and findings follow it as secure child rows. **Fact:** `sprk_invoice` has no Access Permission column today; the access-permission reconciler covers four tables (To Do, Event, Communication, Document; `ChildAccessPermissionReconciler.cs`). So (a) needs the column on `sprk_invoice` (and the reconciler's scope), and reviewers and approvers of such an invoice must be granted access explicitly.
  - **(b) Refuse isolated sources at submit.** Simplest and fully safe, but reverses owner K.
  - **(c) Accept the disclosure at submit.** Conflicts with round 113 for flagged-external users at least.

- **word-add-in-r1 — the flagged-attachment case, where no Document exists (re-check of v5 §9.1, 2026-10-21; accepted, to fold in when the owner settles the process).**
  1. **No source document.** The bytes come from the mailbox (Graph, on behalf of the user). Step 1 checks the caller's Read on the communication; the mailbox read is the caller's right to the attachment. The Stored File stores the communication id and the attachment identity, with a null source-document id. B follows the communication alone. Step 4's source-document guard does not apply. Step 5 checks the communication's filing and thread, not `SecureRootsAboveAsync(document)`. §9.1 must state which checks apply with and without a source document.
  2. **An email that cannot be identified** (no communication and capture fails: older add-in, failed Graph read, no Message-ID; the task-130 D7 case). Submit Invoice first captures the email as `/office/save` does (`EmailUploadCaptureService`), so a communication normally exists. Only if capture fails is the copy created with its own Restricted, in the same create (`AccessInheritance.OwnNarrowing(Restricted)`, as uac-r2 task 189 requires for documents), if invoice files use that model. word-add-in-r1 puts the exact fallback to the owner with the process.
  3. **Keys.** For an uncaptured email the communication key is the RFC Message-ID resolved to the captured communication, never the Exchange item id (task 130 found an older add-in sending the item id as a Message-ID). The endpoint has its own idempotency key, which includes the flag, so a replay of `/office/save`'s key is never taken for a submission.

## 16. Review record

**Fable review of v3 (`92fe0be17e`), 2026-10-21, owner-approved.** 13 must-fix items, all folded into v4: the version chain (7.2), "retired by a version" (7.2, 3.1), the Submit Invoice copy guard (9.1), access (5), record-rule drift (6.3), fail-closed reads, gaps and snapshot (7.1, 7.4), re-evaluation on publish (8.2), blob protections (4.2, 4.3), §11 answers (3.3), the rate-card firm (I-P3), the drafting key (7.2), delete ownership (I-P5), overlapping sources (6.5).

**Peer reviews of v4 (`c260f9de11`), 2026-10-21**, recorded in `procedure-model-reviews.md`: word-add-in-r1 (content), ontology-r1 (CHANGE; blocking 2b, 9a, O1, all folded in), uac-r2 (6 F1, 3 F2, 3 F4, K1–K6, all folded in or answered by the owner). The owner's answers A–R are in section 2 (O-18 to O-32).

## 17. Agreement extension path (R2)

Agreement is a core record that can stand alone or sit under a matter or project (owner, 2026-10-21). It is deferred to e-billing R2. Code today treats `sprk_agreement` only as a child of a matter or project (`CoreAncestorResolver.cs:212`); making it a root is uac-r2's core-record model to change first. R2 then touches:

1. **The core-record list** (`InvoiceFamilyCoreRecordGuard.FilingCores` and `CoreRecordLookups`, `CoreAncestorResolver`, `CoreRecordChain`): add `sprk_agreement`; an agreement under a matter or project is distance 1 from it.
2. **Invoice tables:** an agreement lookup on `sprk_invoice`, `sprk_billingevent`, `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_externalidentifier`, `sprk_invoicecorrection`.
3. **This model:** an agreement lookup on `sprk_procedureassignment` and on the record-scoped `sprk_ruleset`; `sprk_billingfirm` and `sprk_billingresponsible` on `sprk_agreement`; the Procedures list control on its form.
4. **Access:** secure child registration (lineage, role rows, census) for the new lookups; uac-r2 task 191's enforcement; the delete planner.
5. **Submission:** internal and external submission routes for agreements; the SPA (R4 or later); the D-140 filing chain and isolation check over the new core type.
6. **Spend and rollups:** ontology-r1's `FinanceBaseValues` and spend rollups per core type.
7. **Approvers:** `sprk_billingapproverrole` on agreements (D-33 / D-40 / D-54).

**R1 groundwork:** every list of core types is one shared list; new tables use one typed lookup per core type, so adding a column is additive; a ratchet test fails when new e-billing code names the core types outside that list.
