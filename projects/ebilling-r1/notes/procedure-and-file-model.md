# Procedures and files — revised approach (draft for discussion)

> **Status**: DRAFT for owner discussion, 2026-10-20. Nothing here is implemented, and no schema, task or decision has changed yet.
> Section 15 lists the schema and UI/UX decisions still open. Once they are agreed, this note becomes one decision row (D-148) plus revised POMLs for 079–087.
> **Supersedes, once agreed:** D-44 (in part), D-55 (in part), D-67, D-145, D-146 (in part), and most of `rule-set-scope-design.md`.

---

## 1. The approach in one page

**Procedures**

A **Procedure** (`sprk_procedure`) is the internal record for any document that drives policy or rules: client billing guidelines, a firm's rate card, special billing instructions for one matter, an internal procedure, and non-billing policy documents later. Each Procedure carries its details and **one file**, kept in **our own Azure Blob storage**. A Procedure is not a Document and has no document management.

**Versions and links**

- **A new version is a new Procedure record** in the same *family*. v2 replaces v1 from v2's effective date.
- **Rules come from a Procedure.** The rules drafted from a Procedure version form that version's rule set, and a rate card imported from a workbook is that version's rate card.
- **A matter, project or work assignment is subject to Procedures, not to rule sets directly.** It becomes subject in one of three ways:
  1. Procedures that apply to **all matters**.
  2. Procedures for the **firm** on the invoice.
  3. Procedures **assigned manually** to the record.

  Per-record **overrides** can pin a version (for example, keep 2025 rates) or exclude a standard Procedure.

**Evaluation**

- Evaluation runs one deterministic resolver: record → Procedures → their rule sets and rate cards → the rules in force.
- Every evaluation saves a snapshot of what applied and why.
- Signals and Decision Records keep citing immutable policy versions, as they do today. A decision made under v1 stays a v1 decision forever.

**Files for invoices and exports**

Invoices and billing exports keep their files in the same file store. These files are not Documents, so there is no SharePoint Embedded and no document management for them either. An emailed invoice still arrives as a Document. The user marks the attachment **Submit Invoice**, and its bytes are copied into the invoice's files.

```
Procedure family "Acme outside counsel guidelines"
 ├─ v1 (effective 2025-01-01) ── file ── rule set v1 ── policies/versions (published, immutable)
 └─ v2 (effective 2026-01-01) ── file ── rule set v2 ── policies/versions
                 ▲
 Matter M ── subject to: [all-matters Procedures] + [firm F's Procedures] + [manual assignments]
             overrides:  pin "F rate card" to v2025 · exclude "F travel policy"
                 │
 Invoice on M from firm F, dated 2026-03-10
   → resolver → rules in force → evaluation → Signals / Decision Records (cite policy versions)
   → snapshot: each rule's policy version, rule set, Procedure version, and why it applied
```

---

## 2. What the owner has decided (2026-10-20 discussion)

| # | Decision |
|---|---|
| O-1 | Procedure (`sprk_procedure`) holds guidelines, rate cards, billing instructions, internal procedures and any other documents that drive ontology policy or rules, in billing or other areas. Area and Type are metadata; a billing flag is added if needed. |
| O-2 | Procedure files go in **our own Blob storage**, not Dataverse file columns and not SharePoint Embedded. An upload/download control is needed. A storage account already exists. |
| O-3 | Linkage is **record → Procedures → their rule sets**, with conditions: the rules that apply to a record must be visible, reviewable and deterministic (section 7); decisions under v1 keep v1 (section 8); several Procedures can apply to one record (section 7.3). |
| O-4 | A new version means the record uses the new version, **with per-record overrides** such as a matter grandfathered on 2025 rates. The list of Procedures a record is subject to must be visible. |
| O-5 | Records are subject to Procedures **by firm** or by **manual association**. More sophisticated matching can come later. The rules remain what the billing analysis evaluates and records. |
| O-6 | Procedures are **internal only** and follow our access control, Secure records included. There are no special "secure Procedures": a Procedure can be reused, and a matter-specific one is created and then associated manually. |
| O-7 | **Export files** become plain files of their export or invoice. They are not Documents. |
| O-8 | **Invoice files** become plain files of the invoice: no Documents, no SharePoint Embedded and no document management. They are listed on the Billing tab, apart from Documents. Search and AI over invoice files are future work unless the analysis needs them. An emailed invoice still creates a Document from the email, and a **Submit Invoice** flag on the attachment routes its bytes to the invoice's files. |
| O-9 | Lists on the record: the Procedures the record is subject to, and possibly its rules too. These can sit in a popup behind a PCF toolbar. |

---

## 3. The Procedure

### 3.1 One record per version, grouped in a family

The owner's model is that version 2 is a new Procedure record. The versions of one document still need to be grouped, for three reasons:

- An assignment can follow "the latest version".
- An override can pin "this version".
- Evaluation can pick the version in force on a date.

**Proposal: a self-lookup `sprk_family`.** It points to the first version, and is empty on that first version. Its other columns:

- **`sprk_versionnumber`** (whole number): 1, 2, 3 …
- **`sprk_previousversion`**: a lookup to the version this one replaces.
- **`sprk_effectivefrom` / `sprk_effectiveto`**: the dates. Publishing v2 with effective-from D sets v1's effective-to to D − 1, so the in-force versions of one family never overlap. The BFF enforces that one invariant (ADR-002).
- **Status**: Draft · Active · Superseded · Retired.

**Alternative:** a parent "Procedure" table plus a child "Procedure Version" table. It is cleaner relationally, but it is one more table, and it departs from the owner's "new Procedure record" model. It is not recommended.

### 3.2 Metadata (proposal; schema to be decided)

| Column | Type | Notes |
|---|---|---|
| Name | text | |
| Area | choice | Billing, plus other ontology areas later. Replaces a separate "billing flag" unless one is still wanted. |
| Type | choice | Guideline · Rate card · Billing instructions · Internal procedure · Other. |
| Applies to | choice | **All matters** · **Firm** · **Assigned records only**. Drives section 6. |
| Firm | lookup `sprk_organization` | Required when Applies to = Firm. |
| Client / issuer | lookup or text | Who issued the document (to be decided; see section 15 Q3). |
| Family, version number, previous version, effective from / to, status | (section 3.1) | |
| Business units | (to be decided) | Today's rule-set business-unit filter (D-171). Keep it on the Procedure, or drop it? See Q8. |
| File | (section 4) | One file per Procedure version, in the file store. |
| Created by person | lookup `systemuser` | D-173 / S9 pattern. |
| Notes | multiline | |

The Procedure has **no** "visible to firms" flag. Firms never see a Procedure or its file (D-66). The existing rule-set flag still decides whether a firm sees clause text (D-79).

### 3.3 Name

`sprk_policy` already exists: it is the ontology's rule container. "Procedure" avoids that clash. Terminology changes go through ontology-r1, under the ontology component model §3, so ontology-r1 confirms the name and the Area/Type values before the schema is written.

---

## 4. The file store (Procedures, invoices, exports)

### 4.1 One file table, one service, one control

One shared design serves all three uses, rather than three:

- **Table `sprk_storedfile`** ("Stored File"; name to be decided). One row per file. Exactly one parent lookup is set:
  - `sprk_procedure`, `sprk_invoice` or `sprk_billingexport`;
  - more parents can be added later.

  Its columns:
  - **purpose** (choice): Procedure source · Invoice PDF · Invoice supporting file · Export file;
  - **file name, content type, size**;
  - **SHA-256** fingerprint;
  - **blob path**: field-secured, read by the server only;
  - **uploaded by / on**;
  - **scan status**: Pending · Clean · Blocked.
- **A BFF service** writes, reads and deletes the blob. It extends the pattern of the existing chat-session store, `Services/Ai/Sessions/SessionFileBlobStore.cs`, which already uses `Azure.Storage.Blobs` with managed identity only, so no new package is needed.
- **One upload/download control.** It is a PCF control on model-driven forms, built from the shared library's existing `FileUpload/FileUploadZone` and `UploadedFileList`. It shows a record's files and uploads to them.

**§11 three questions:**

1. **Overlap.** `sprk_document` is document management on SharePoint Embedded, which the owner has ruled out for these files. `SessionFileBlobStore` stores session bytes with a session lifecycle, so it is a different owner and retention. The table and service are new; the store extends that blob pattern.
2. **Extending instead.** Procedure, invoice and export rows cannot hold a file list themselves: invoices need several files.
3. **Cost of doing nothing.** There would be nowhere to keep a Procedure's or an invoice's file outside document management.

**Alternative:** a file column set (path, name, hash) on each parent table, with no child table. It is simpler for the Procedure, which has one file, but it can't hold several invoice files. Not recommended.

### 4.2 Blob layout and protection (proposal)

- **Containers:** separate containers per purpose, `procedures`, `invoice-files` and `billing-exports`, so each can have its own retention.
- **Path:** `{tenantId}/{parent table}/{parent id}/{file id}`.
- **Write-once:** a file is never overwritten. A new file is a new row, and a new Procedure version is a new Procedure.
- **Protection:**
  - soft delete on;
  - optionally, a version-level immutability policy on `procedures` and `invoice-files`;
  - malware scanning (Defender for Storage) sets the scan status, and a Blocked or still-Pending file is never downloaded or processed.
- **Identity:** the BFF's managed identity only. There is no account key, connection string or SAS, which matches the session store's startup refusal. The browser never receives a storage URL.
- **Provisioning:**
  - the containers and the role assignment (Storage Blob Data Contributor for the BFF identity) are added to environment setup;
  - the session store's config notes that `storage-account.bicep` creates the role assignment only when `appServicePrincipalId` is passed, which `customer.bicep` does not do today; the customer-provisioning project gets a hand-off;
  - on dev, the existing account is used.
- **Retention and deletion:** a file lives as long as its parent row. A parent deleted through the BFF deletes its files with a job (ADR-004). ADR-015 expects a defined retention and deletion path for stored document bytes, and this design gives one.

### 4.3 Upload and download

- **Upload:** the control calls the BFF, which checks Write on the parent row, streams the bytes to the blob, writes the Stored File row and queues the scan.
- **Download:** the BFF checks Read on the parent row with the caller's own rights (the same check as `GetCallerRightsForRecordsAsync`), then streams the blob. A Secure invoice's file is therefore exactly as visible as the invoice.
- **External users:**
  - firms (CIAM) have no route to Procedure files at all;
  - invoice files reach a firm only through the external invoice routes, which already scope by firm and core record (D-70).

---

## 5. Access control (O-6)

| Row | Owner and visibility | Notes |
|---|---|---|
| Procedure | Owned by the uploader's business unit; read according to the role depth set for internal users. Internal only. | Not in the secure family: a Procedure can be reused across matters, including Secure ones (O-6). A Secure matter's team needs Read on the Procedures that apply to it, so readers of matters must be able to read Procedures: org-level Read for internal roles, or the role depth that achieves it. **uac-r2 to confirm.** |
| Procedure assignment / override (section 6) | A child of the core record it names, in the **secure family**. | That an override exists on a Secure matter is itself the matter's information. Joins uac-r2's lineage, census and creator-person lists (D-173 pattern). |
| Stored File (invoice, export) | A child of its invoice or export, in the secure family. | Access follows the invoice. |
| Stored File (Procedure) | Follows the Procedure. | |

**One consequence to accept:** a reusable Procedure assigned to a Secure matter is readable by users outside that matter. That is the owner's choice (no secure Procedures). Instructions sensitive to one Secure matter must therefore not be uploaded as a Procedure, or they need a restricted Procedure, which O-6 rules out for now.

---

## 6. How a record becomes subject to Procedures

### 6.1 Three sources

| Source | How | Stored? |
|---|---|---|
| **All matters** | Procedure Applies to = All matters (today's "client default" level) | No: computed |
| **Firm** | Procedure Applies to = Firm and its firm = the **invoice's firm** (`sprk_invoice.sprk_vendororg`) | No: computed |
| **Manual** | An assignment row: Procedure family (or version) → matter, project or work assignment | Yes |

**Firm matches are computed at evaluation time, not copied onto matters.** Copying would need a trigger whenever a matter's firm changes or a new firm Procedure is published. Without plugins (ADR-002), that means a reconcile job and a window in which the copied rows are stale. Computing is always current and still deterministic, because it reads only stored rows.

**Firm on the invoice, not on the matter.** A matter can be billed by more than one firm, and firm F's rate card must apply only to F's invoices. The matter's `sprk_assignedoutsidecounsel` decides which firm's Procedures the matter's **list** shows by default (section 6.4), not which ones apply to an invoice.

### 6.2 Overrides per record (O-4)

Overrides are rows of the same assignment table, with a **mode**:

| Mode | Meaning | Example |
|---|---|---|
| **Follow latest** (default for manual assignments) | The family's version in force on the date (section 8.2). | Matter-specific billing instructions; v2 applies from its effective date. |
| **Pinned to version** | This version, whatever is newer. | Matter grandfathered on firm F's 2025 rate card: pin F's rate-card family to v2025. |
| **Excluded** | A Procedure that would apply by "all matters" or "firm" does not apply to this record. | The firm's standard travel policy is waived for this matter. |

Proposed assignment columns:

- the Procedure (family root, or the pinned version);
- one core-record lookup (matter, project or work assignment), RemoveLink;
- mode;
- the date range it applies within (optional);
- reason (required for Pinned and Excluded);
- created by person.

**§11:**

- **Overlap.** A plain N:N between Procedure and matter can't carry the mode, the pinned version or the reason.
- **Extending instead.** No existing table carries these per-record attributes.
- **Cost of doing nothing.** No grandfathering and no per-matter waiver (O-4).

### 6.3 Inheritance: direct association only (unchanged rule)

The direct-association rule of D-172 stays. A work assignment filed under project P is subject to P's manual assignments and overrides at distance 1, but not to P's matter's (no second hop). This reuses `CoreRecordChain`, which was planned in 082, and uac-r2's `NamePairParentAsync` for the project pair, unchanged. Overrides on the record itself (distance 0) beat overrides inherited from its parent (distance 1).

### 6.4 The list on the record (O-9)

A **Procedures** list on the Billing tab of the matter, project and work assignment: a PCF toolbar button opens a popup.

- **Shows:**
  - every Procedure the record is subject to, from the all-matters, firm (the assigned outside counsel by default; any firm can be chosen) and manual sources, plus those inherited from a direct parent;
  - for each: the version in force today, the source, and any override.
- **Actions:**
  - assign an existing Procedure;
  - create a new Procedure for this record (create and assign in one step; the "special instructions" case);
  - pin, exclude, remove.
- **Rules in force:** a second view or tab lists the **rules** in force on a chosen date, each with its source Procedure version, and marks rules displaced by precedence (section 7.3). It calls the same resolver the evaluation calls (section 7.1), so what users see is exactly what evaluation would use.

---

## 7. From Procedure to rules, and how evaluation finds them

### 7.1 The resolver (answers O-3(a): how rules are "calculated")

"Calculated" means one pure server function, `ApplicableRules(record, firm, date)`, that reads stored rows only. Given the same rows and the same date, it returns the same answer every time.

```
chain     = CoreRecordChain(record)                         // record (d0) + direct parents (d1), section 6.3
families  = Procedures with Applies to = All matters
          ∪ Procedures with Applies to = Firm AND firm = invoice firm
          ∪ manual assignments on any chain record
          − families Excluded by an override on the chain (the nearer override wins)
for each family:
    version = pinned version (nearer override wins)  OR  the family's version in force on `date`
    level   = Manual d0 | Manual d1 | Firm | All matters      // the precedence levels of today's design
rule sets  = the published rule set of each chosen version
rate cards = the published rate card of each chosen version
rules      = published policy versions of those rule sets in force on `date`
           → precedence (section 7.3)
```

It replaces today's rule-set eligibility (`BillingFactLoader`, scope ClientDefault and Firm) and the planned 082 scope selection. The precedence step (`InvoiceComplianceEvaluator`) is kept, re-keyed from rule-set scope to the level above.

### 7.2 Drafting rules from a Procedure

The steps are the same as today, with the source changed from a Document to a Procedure:

- **Draft rules** on a Procedure version runs the existing drafting job (`BillingRuleDraftJobHandler`, Action `billing-rule-draft`). It reads the file from the file store instead of SharePoint Embedded, and proposes rules with a quoted clause and its location.
- The proposals form **that version's rule set**. `sprk_ruleset` gets a `sprk_procedure` lookup that replaces `sprk_sourcedocument`.
- **v2 starts from v1:** creating a v2 copies v1's rules as the starting proposals, and re-drafting highlights the differences. This is a proposal (Q6).
- **Rate cards:** importing a workbook (`RateCardImportService`) creates the rate card of that Procedure version. `sprk_ratecard` gets `sprk_procedure` in place of its three core-record lookups and scope.
- **Review and improve:** the Rule Review page is reached from the Procedure and lists its rule set and rules.
  - Correcting a published rule makes a new policy version, as today. Policy versions are immutable.
  - A correction without a new file is allowed and is recorded on the policy version (who, when, why).
  - Decisions already made keep the version they cited (section 8).

### 7.3 Several Procedures on one record (O-3(c))

All Procedures the resolver picks are evaluated together. Conflicts are settled by the precedence already designed and reviewed (`rule-set-scope-design.md` §3.3), unchanged except that the level now comes from section 7.1:

- **Checklist types add up:** approved timekeepers, non-billable activities, required data, AI observations, and the non-reimbursable part of expenses. Every applicable Procedure's items apply.
- **Limit types replace:** rate tolerance, time increment, daily hours cap, invoice age, core-record window, duplicate definition, and per-category expense caps. The most specific level wins: Manual d0 > Manual d1 > Firm > All matters.
  - A broken or downgraded specific rule never removes the general one (B2, F1-b, unchanged).
  - At the same level, all apply, and assigning one shows a warning.
- **Rates:** the rate-card tiers of task 085, re-keyed the same way. A nearer card beats a farther one, and a timekeeper rate beats a classification rate. Different amounts in the deciding tier give **Ambiguous**: no rate is used and a finding is raised, as today.

### 7.4 Determinism, visibility and review (O-3)

- **Visible before evaluation:** the record's "rules in force" view (section 6.4).
- **Visible after evaluation:** each evaluation saves a **snapshot**. For each rule applied, it records the policy version, rule set, Procedure version, the source and level, and any override that decided it, plus each rule displaced by precedence.
  - It is stored with the submission's findings: `sprk_invoicesubmission.sprk_findingsjson` already carries the clause.
  - Its fact summary also goes on the Decision Record (section 8).
  - Later edits to Procedures or overrides never change an earlier evaluation's story.
- **Deterministic:** the resolver reads stored rows only. Ties keep the existing ordinal order (policy code, then id).

---

## 8. Signals, Decision Records and versions (answers O-3(b))

### 8.1 What they cite

Unchanged at the core: each Signal and Decision Record cites the **policy version**, which is immutable and carries the quoted clause and its location, plus reason codes and facts. Added:

- the **Procedure version** id in the evaluation facts, so an internal reviewer can open the source file (download through the BFF);
- the **basis** (source and level, override id) in the facts.

Firms still see only the clause text, subject to the visible-to-firms flag (D-66, D-79), never the Procedure or its file.

### 8.2 What "M uses v2" means

- **New evaluations of M** use v2 for anything dated on or after v2's effective-from. Following today's per-subject date rule, that is a line's service date where present, otherwise the invoice date. Anything dated earlier uses v1. An invoice for 2025 work submitted in 2026 is still judged on v1. **Owner to confirm** (Q5): the alternative is "v2 from the day it is published, whatever the work date".
- **Decisions already made under v1 stand.** Decision Records are append-only, Signals cite v1's policy versions, and a decided invoice is never re-evaluated.
- **An invoice still In Review** when v2 is published is re-evaluated with the date rule above, so 2025 work is still judged on v1. The snapshot shows which version applied to which line.
- **A pinned override** keeps v2025 for that record whatever the dates.

---

## 9. Invoices as files (O-8)

### 9.1 Submission routes

| Route | Today | Proposed |
|---|---|---|
| Firm via SPA | The PDF is uploaded into the core record's SharePoint Embedded container as a Document (D-44) | The PDF goes to the invoice's files, Stored File purpose Invoice PDF. The SPA wizard (051) keeps its UI; its upload goes to the file store. R3 coordinates the SPA build and deploy. |
| Internal upload | Same as the SPA | Upload on the Billing tab or the invoice → invoice files. |
| Email attachment | The attachment is a Document. The 050a confirm route re-files it under the invoice (`InvoiceDocumentRefile`). | The attachment stays a Document with its email. **Submit Invoice** on the attachment copies its bytes into the new invoice's files and starts the submission. The Document is never moved. |

**What changes for 050a (the email route):**

- **Removed:** the re-file (moving the Document under the invoice), its retries, its "NOT filed" admin notes, and the K4 race. The Document never moves.
- **Kept:**
  - the D-140 filing chain: which matter the email belongs to, following its thread as a union;
  - the isolation check at submit and at commit: a document protected under a Secure matter must not have its bytes copied into an invoice under another record.

  The copy needs the same guard the re-file had.
- **Completed work affected:** 050a's confirm route becomes the Submit Invoice action. The admin-note and re-file code paths are retired.

### 9.2 Processing and viewing

- **Extraction:** Content Understanding reads the bytes. The BFF sends the file body from the file store, or a short-lived link the BFF creates server-side; the browser never gets it.
- **Viewer:** the bounding-box viewer (108) loads the PDF through the BFF download route.
- **Invoice index:** built from the structured invoice and line records, as already planned (D-44's second half). It is not affected.
- **Lost by design:** invoice PDFs are no longer in the matter's Documents, the files search index, or AI chat over matter documents (owner: acceptable). A future version can index the `invoice-files` container (Azure AI Search blob indexer) if needed.
- **Invoice lines:** a file belongs to the invoice, never to a line. The restriction that no route accepts an invoice line as a parent until uac-r2 #2047 lands is unaffected.

## 10. Export files (O-7)

The billing export (`sprk_billingexport`, 023) gets its file as a Stored File, purpose Export file. The E-Billing Output Files container and its key (D-55, D-67, 028) are retired once nothing writes there. Exports were already excluded from indexing.

---

## 11. Search and AI

- **Not needed for R1:** evaluation never reads the file (clauses are copied at drafting), and Signals and Decision Records never read it.
- **Procedure files:** nothing indexes them in R1. If ontology-r1 needs retrieval over Procedures for AI evaluation, a `procedures` index (blob indexer) is a follow-up. It must trim by Procedure Read, because Procedures are internal only. **Ask ontology-r1.**
- **Invoice files:** future (O-8).

---

## 12. What changes in existing decisions and completed work

| Item | Change |
|---|---|
| D-44 | Invoice PDFs move from the core record's SharePoint Embedded container to invoice files. The structured invoice index is unchanged. |
| D-55 | Procedure Library and E-Billing Output Files containers retire. The owner-created containers on dev are **left in place** (project rule: never delete them) until the owner says otherwise. |
| D-67 | Container keys retire with the containers. |
| D-145 | Withdrawn: nothing is filed under a matter. |
| D-146 | Becomes simpler: deleting a core record removes its assignment and override rows (RemoveLink or a delete job). Procedures, rule sets and rate cards are never touched. |
| D-120 | Rate cards are no longer tied to a core record, so "deleted with its record" no longer arises. |
| D-140 | Kept (the email route still needs it). |
| `rule-set-scope-design.md` | Kept: §3.1 chain, §3.3 precedence (re-keyed). Replaced: §2 S1/S2/S6 (lookups and scope on rule sets and rate cards), §4.1–4.5, §5.4. Open: S3/S4 business units (Q8), S8 snapshot (needed only if business units stay), S9 creator person (also on the new tables). |
| 019 / 076 / 077 rule-set store, endpoints, Rule Review | Rule set gains `sprk_procedure`, loses `sprk_sourcedocument` and the planned scope lookups. Creation starts from a Procedure. Rule Review is reached from the Procedure. |
| 028 containers | Replaced by the blob containers (section 4.2). |
| 044 / 049 / 051 submission | The upload target changes to the file store. Processing reads bytes from the file store. |
| 050a confirm | Becomes Submit Invoice (copy). The re-file is retired (section 9.1). |
| 075 drafting job | Reads the file from the file store. |
| 078 rate-card import | The card belongs to a Procedure version. The workbook is that version's file. |
| 108 viewer | Loads through the BFF download route. |
| Dev data | Existing test rule sets, rate cards and invoices with SharePoint Embedded files are dev test data. Recreate rather than migrate (to confirm). |

## 13. Effect on the open tasks 079–087 (sketch; re-planned after agreement)

| Task | Becomes |
|---|---|
| 080 schema | New schema: `sprk_procedure`, the assignment table, `sprk_storedfile`; `sprk_procedure` on rule set and rate card; creator person on the new tables. The planned rule-set and rate-card scope lookups are dropped, as is the S6 backfill. ontology-r1 reviews the schema again. |
| 081 rule-set service | Procedure service (versions, effective dates, family invariant) plus the assignment and override service. |
| 082 selection | The resolver (section 7.1), reusing `CoreRecordChain`. |
| 083 firm clause | Uses the resolver. |
| 084 Rule Review UI | Procedure-centred pages plus the record's Procedures list and "rules in force" view. |
| 085 rate cards | Rate-card tiers re-keyed to resolver levels. The scope column and backfill are dropped, so ontology D-174 / D-147 become moot. |
| 086 rate-card secure family, workbook filed under record | Mostly obsolete. What remains is the secure-family rows for the assignment table and invoice Stored Files. |
| 087 BU snapshot writer | Only if business units stay (Q8). |
| New | The file store service and control; the Stored File schema; storage provisioning hand-off; Submit Invoice; moving invoices and exports to files. |

## 14. Coordination

- **ontology-r1:**
  - the Procedure name and Area/Type values (ontology component model §3);
  - whether non-billing policies (today `sprk_policy.sprk_matter`, #2084) should also come from Procedures;
  - the resolver's levels;
  - the drafting source change;
  - a full schema re-review (it replaces the 080 work they reviewed);
  - whether they need a Procedures search index.
- **uac-r2:**
  - Procedure read depth for internal roles;
  - the assignment table and Stored File joining the secure family;
  - the copy guard on Submit Invoice;
  - which of #2159 / #2161 / #2151 and T7 ebilling still needs. Probably none, because nothing is relocated into SharePoint Embedded.
- **SPA-r3:** the SPA upload target and the dev deploy.
- **customer-provisioning-orchestration-r1:** blob containers and the role assignment in environment setup.

## 15. Open questions: schema and UI/UX to decide together

**Schema**

- **Q1.** Versions as one table with a `sprk_family` self-lookup (recommended), or Procedure + Procedure Version tables?
- **Q2.** Area / Type / Applies to values, and whether a separate billing flag is needed beyond Area = Billing.
- **Q3.** The issuer on a Procedure: a lookup to the client (account) or to the firm, or text?
- **Q4.** One shared `sprk_storedfile` table for Procedure, invoice and export files (recommended), or file columns per table? And its name.
- **Q5.** "M uses v2" by work date (line or invoice date, recommended) or from publication?
- **Q6.** Does v2 start from v1's rules (copied as proposals, recommended) or from a fresh draft?
- **Q7.** Firm matching uses the **invoice's** firm (recommended). The matter's assigned outside counsel only sets the list's default view. Agree?
- **Q8.** Keep the business-unit filter (D-171) on Procedures, or drop it now that Procedures are assigned per record? Dropping it removes S3, S4, S8 and task 087.
- **Q9.** Blob protections: immutability policy on `procedures` and `invoice-files`; malware scanning required before processing?
- **Q10.** Dev: recreate test rule sets, rate cards and invoices rather than migrate? The dev containers stay untouched.

**UI/UX**

- **Q11.** Procedure form: details, file control, versions list, rule set and Rule Review entry, and where it is used (records assigned or overridden). Is a "New version" command on the form enough?
- **Q12.** Record Billing tab: a PCF toolbar with **Procedures** (list plus assign, create, pin, exclude) and **Rules in force** (date picker; rules with source and "displaced by"), as popups or as inline tabs?
- **Q13.** Invoice Billing tab: an **Invoice files** list (upload, download, purpose). Does the Invoice Review page show the files list too, next to the PDF viewer?
- **Q14.** Email attachment **Submit Invoice**: a command on the attachment row in the communication UI, or a flag set in the attachment list? Which surfaces carry it?
- **Q15.** Where is the Procedure library browsed: a model-driven view, or a page in the Spaarke Console?
