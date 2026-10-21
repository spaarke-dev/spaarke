# Procedures and files — revised approach

> **Status**: v3, 2026-10-21. All owner questions are answered (section 2). Next come one top-tier design review and then peer review (ontology-r1, uac-r2, word-add-in-r1). Nothing is implemented, and no schema, task or decision row has changed yet.
> Once peers align, this note becomes decision D-148 plus revised POMLs for 079–087 and new tasks. The UI/UX is prototyped separately (O-14); the back end goes first.
> **Supersedes, once agreed:** D-44 (in part), D-55 (in part), D-67, D-145, D-146 (in part), D-171 point 3 / D-141 Q1 (the business-unit filter), and most of `rule-set-scope-design.md`.

---

## 1. The approach in one page

A **Procedure** (`sprk_procedure`) is the internal record for any document that drives policy or rules: client billing guidelines, a firm's rate card, special billing instructions for one matter, an internal procedure, and non-billing policy documents later.

- **Its file:** each Procedure carries its details and one file in **our own Azure Blob storage**. A Procedure is not a Document, and there is no document management.
- **Versions:** a Procedure (the document) has Procedure Versions (each issue, with its file and effective dates). v2 replaces v1 from v2's effective date.
- **Rules:** a Procedure's rules live in one rule set. Each Procedure Version publishes its own versions of those rules, so a rule keeps its identity across Procedure Versions. A rate card imported from a workbook belongs to its Procedure Version.

**How a record becomes subject to Procedures.** A matter, project or work assignment is subject to Procedures, not to rule sets directly, in three ways:

1. Procedures that apply to **all matters**.
2. Procedures for the **invoice's firm**.
3. Procedures **assigned manually** to the record.

**Overrides.** Overrides on the record work at two levels:

- **Procedure level:** pin a version (keep 2025 rates) or exclude the Procedure.
- **Rule level:** exclude one rule (no late fee on this matter) or change it for this matter (travel is allowed here).

**Evaluation.** One deterministic resolver gives the rules in force: record → Procedures → rules → overrides → precedence. Every evaluation saves a snapshot of what applied and why. Signals and Decision Records keep citing immutable policy versions, so a decision made under v1 stays a v1 decision.

**Files for invoices and exports.** Invoices and billing exports keep their files in the same file store, not as Documents. An emailed invoice still arrives as a Document. **Submit Invoice** on the attachment copies its bytes into the invoice's files.

```
Procedure "Acme outside counsel guidelines"                     rule set (one per Procedure)
 ├─ v1 (effective 2025-01-01) ── file ─┐                          ├─ rule "Travel"   : versions for v1, v2
 └─ v2 (effective 2026-01-01) ── file ─┴── publishes versions of ─┼─ rule "Late fee" : versions for v1, v2
                                                                  └─ rule "Block billing" : v2 only (new in v2)
 Matter M ── subject to: [all-matters Procedures] + [firm F's Procedures] + [manual assignments]
             Procedure overrides: pin "F rate card" to v2025
             rule overrides:      exclude "Late fee" · change "Travel" for M (travel allowed)
                 │
 Invoice on M from firm F, line dated 2026-03-10
   → resolver → rules in force → evaluation → Signals / Decision Records (cite policy versions)
   → snapshot: each rule's policy version, Procedure Version, source, and the overrides that decided it
```

---

## 2. Owner decisions (2026-10-20 and 2026-10-21)

| # | Decision |
|---|---|
| O-1 | Procedure (`sprk_procedure`) holds guidelines, rate cards, billing instructions, internal procedures and any other documents that drive ontology policy or rules, in billing or other areas. Area and Type are metadata. |
| O-2 | Procedure files go in **our own Blob storage**, not Dataverse file columns and not SharePoint Embedded. An upload/download control is needed. A storage account exists, and the shared library likely holds the control parts. |
| O-3 | Linkage is **record → Procedures → their rules**. The rules that apply to a record must be visible, reviewable and deterministic (section 7). Decisions under v1 keep v1 (section 8). Several Procedures can apply to one record (section 7.3). |
| O-4 | A new version means the record uses the new version, **with per-record overrides**, e.g. a matter grandfathered on 2025 rates. The list of Procedures a record is subject to must be visible. |
| O-5 | Records are subject to Procedures **by the invoice's firm** (organization) or by **manual association**. More sophisticated matching can come later. The rules remain what the billing analysis evaluates and records. |
| O-6 | Procedures are **internal only** and follow our access control, Secure records included. There are no special "secure Procedures"; a Procedure can be reused. The owner does not see this as a risk. If a problem appears, the fallback is Procedures that are secure, one per Secure matter. |
| O-7 | **Export files** become plain files of their export or invoice. They are not Documents. |
| O-8 | **Invoice files** become plain files of the invoice: no Documents, no SharePoint Embedded and no document management. They are listed on the Billing tab, apart from Documents. Search and AI over invoice files are future work unless the analysis needs them. An emailed invoice still creates a Document from the email, and a **Submit Invoice** flag on the attachment routes its bytes to the invoice's files. |
| O-9 | Lists on the record: the Procedures it is subject to, and possibly its rules. These can sit in a popup behind a PCF toolbar. |
| O-10 | **Overrides at the rule level too** (2026-10-21). On a matter, a rule can be switched off (no late fee, no discount) or changed (travel is approved). |
| O-11 | Procedure details (2026-10-21): **Issued By** (lookup to organization), **External Contact** (lookup to contact), **Internal Contact** (lookup to contact). A billing flag is ebilling's call; it is likely handled at the rule level. |
| O-12 | (2026-10-21) Q4: one shared file table. Q5: the version is chosen by **work date** matched to the Procedure's effective dates. Q6: **v2 copies v1's rules for editing and revising.** Q7: firm = the **invoice's firm** (organization). Q8: **no business-unit filter.** Q9: blob immutability and scanning if not difficult. Q10: whatever is easiest. |
| O-13 | **No migration of dev containers or files** (2026-10-21). Existing dev rule sets, rate cards, invoices and their SharePoint Embedded files are dev artifacts. |
| O-14 | **UI/UX is prototyped separately** and drives the matching tasks (2026-10-21). Back-end work proceeds first. |
| O-15 | **Two tables: Procedure and Procedure Version** (2026-10-21, after the owner asked whether two tables are better; section 3.1). |
| O-16 | **A late fee is just an added fee**, and the Procedure and its rules decide whether it is allowed (2026-10-21; section 6.3). |
| O-17 | **A deleted core record's exceptions Procedure and record rules are retired** (2026-10-21). |

---

## 3. The Procedure

### 3.1 Two tables: Procedure and Procedure Version (O-15)

- **`sprk_procedure` (Procedure)** is the document as a lasting thing, for example "Acme outside counsel guidelines". It holds what is true of every version:
  - name, area and type;
  - **Applies to** and **Firm** (who it governs);
  - Issued By and the two contacts.

  Assignments and overrides point to it. Its rule set is attached to it.
- **`sprk_procedureversion` (Procedure Version)** is one issue of that document. It holds:
  - version number;
  - effective from / to;
  - status: Draft · Active · Superseded · Retired;
  - **its file**;
  - the policy versions it published;
  - its rate card.

  "Version 2" is a new Procedure Version under the same Procedure.

Why two tables beat one table with a "family" self-lookup (the v1 recommendation, withdrawn):

- **Applicability lives in exactly one place.** The resolver needs Applies to and Firm before it can pick a version by date. On one table, each version carries its own copy, so v2 could say Firm and v1 All matters, and the resolver must first decide which copy governs. That is circular, and it is a determinism hazard. With two tables there is one row and one answer. A change of who a Procedure governs is a single audited edit, not a side effect of uploading a new file.
- **The document's identity never depends on version 1's row.** On one table the family is "v1's id": retiring or deleting v1 breaks it, and every query needs "family = X or id = X".
- **Every link has a clear target.** Assignment → Procedure. Pin → Procedure Version. Rule set → Procedure. Policy version → Procedure Version. Rate card → Procedure Version. File → Procedure Version.
- **It is more flexible later.** Metadata can be corrected without a new version. Versions can be listed under their document. Future version-level data (approval, signed date, superseded reason) has a home.

The cost is one more table and two forms. Each Procedure owns its versions, and deleting a Procedure is refused while any version has published rules (Signals cite them).

**Invariant (BFF-owned, ADR-002):** a Procedure's Active versions never overlap in effective dates. Publishing v2 with effective-from D sets the previous version's effective-to to D − 1, in one BFF operation.

Model-driven forms bypass the BFF, so effective dates, status and the file are field-secured: Create and Update for the BFF application user only, Read for everyone. They are changed only through the BFF, as S7 does for rule-set scope today.

### 3.2 Metadata

**Procedure** (the document):

| Column | Type | Notes |
|---|---|---|
| Name | text | |
| Area | choice | Billing, plus other ontology areas later (values with ontology-r1). |
| Type | choice | Guideline · Rate card · Billing instructions · Internal procedure · Other. |
| Applies to | choice | **All matters** · **Firm** · **Assigned records only**. Drives section 6. |
| Firm | lookup `sprk_organization` | Required when Applies to = Firm: the firm whose invoices it governs. |
| Issued By | lookup `sprk_organization` | O-11. Who issued the document: the client, a firm, or the legal department. |
| External Contact | lookup `contact` | O-11. |
| Internal Contact | lookup `contact` | O-11. |
| Created by person | lookup `systemuser` | The S9 / D-173 pattern. |
| Notes | multiline | |

**Procedure Version** (one issue):

| Column | Type | Notes |
|---|---|---|
| Procedure | lookup `sprk_procedure` | Parental. |
| Version number | whole number | 1, 2, 3 … assigned by the BFF. |
| Previous version | lookup `sprk_procedureversion` | |
| Effective from / to | date | Field-secured, BFF-owned (section 3.1). |
| Status | choice | Draft · Active · Superseded · Retired. Field-secured. |
| File | (section 4) | One file per version. Optional: a record's exceptions Procedure has no file (section 6.3). |
| Created by person | lookup `systemuser` | |

Firm and Issued By are separate on purpose. A rate card is usually issued by the firm whose invoices it governs. Client guidelines are issued by the client and govern every firm, with Applies to = All matters.

**No billing flag.** Area = Billing marks billing Procedures. Whether a rule is a billing rule is already settled by its rule type (O-11).

**No "visible to firms" flag on the Procedure.** Firms never see a Procedure or its file (D-66). The existing rule-set flag still decides whether a firm sees clause text (D-79).

### 3.3 Name

`sprk_policy` already exists as the ontology's rule container; "Procedure" avoids that clash. Terminology goes through ontology-r1 (ontology component model §3), which confirms the name and the Area/Type values.

---

## 4. The file store (Procedures, invoices, exports)

### 4.1 One file table, one service, one control (O-12 Q4)

- **Table `sprk_storedfile`** ("Stored File"; final name to be confirmed with ontology-r1). One row per file.
  - **Parent:** exactly one lookup is set: `sprk_procedureversion`, `sprk_invoice` or `sprk_billingexport` (more parents later).
  - **Purpose:** Procedure source · Invoice PDF · Invoice supporting file · Export file.
  - **File details:** file name, content type, size, SHA-256 fingerprint.
  - **Blob path:** field-secured, read by the server only.
  - **Audit:** uploaded by and on.
  - **Scan status:** Pending · Clean · Blocked.
- **A BFF service** writes, reads and deletes the blob. It extends the pattern of the existing chat-session store, `Services/Ai/Sessions/SessionFileBlobStore.cs`, which already uses `Azure.Storage.Blobs` with managed identity only, so no new package is needed.
- **One upload/download control.** It is a PCF control built from the shared library's existing `FileUpload/FileUploadZone` and `UploadedFileList`. Its UX comes from the prototype (O-14).

**§11 three questions:**

1. **Overlap.** `sprk_document` is document management on SharePoint Embedded, which the owner has ruled out for these files. `SessionFileBlobStore` stores session bytes with a session lifecycle, so it is a different owner and retention. The table and service are new; the store extends the blob pattern.
2. **Extending instead.** Procedure, invoice and export rows cannot hold a file list themselves, because an invoice has several files.
3. **Cost of doing nothing.** There would be nowhere to keep a Procedure's or an invoice's file outside document management.

### 4.2 Blob layout and protection

- **Containers:** separate containers per purpose, `procedures`, `invoice-files` and `billing-exports`, so each can have its own retention.
- **Path:** `{tenantId}/{parent table}/{parent id}/{file id}`.
- **Write-once:** a file is never overwritten. A new file is a new row, and a new issue of a Procedure is a new Procedure Version.
- **Protection:**
  - soft delete on;
  - a version-level immutability policy and Defender for Storage malware scanning where not difficult (O-12 Q9);
  - with scanning on, a Blocked or Pending file is never downloaded or processed;
  - with scanning off, scan status stays "Not scanned" and is shown.
- **Identity:** the BFF's managed identity only. There is no account key, connection string or SAS, which matches the session store's startup refusal. The browser never receives a storage URL.
- **Provisioning:**
  - the containers and the role assignment (Storage Blob Data Contributor for the BFF identity) are added to environment setup;
  - the session store's config notes that `storage-account.bicep` creates the role assignment only when `appServicePrincipalId` is passed, which `customer.bicep` does not do today; customer-provisioning gets a hand-off;
  - on dev, the existing account is used.
- **Retention and deletion:** a file lives as long as its parent row. A parent deleted through the BFF deletes its files with a job (ADR-004). ADR-015 expects a defined retention and deletion path for stored document bytes, and this design gives one.

### 4.3 Upload and download

- **Upload:** the control calls the BFF. The BFF checks Write on the parent row, streams the bytes to the blob, writes the Stored File row and queues the scan.
- **Download:** the BFF checks Read on the parent row with the caller's own rights (as `GetCallerRightsForRecordsAsync` does), then streams the blob. A Secure invoice's file is therefore exactly as visible as the invoice.
- **External users:**
  - firms (CIAM) have no route to Procedure files;
  - invoice files reach a firm only through the external invoice routes, which already scope by firm and core record (D-70).

---

## 5. Access control (O-6)

| Row | Owner and visibility | Notes |
|---|---|---|
| Procedure | Owned by the uploader's business unit; read by internal users at the depth their roles give. Internal only. | Not in the secure family: a Procedure is reusable across matters, including Secure ones (O-6). Users who review a Secure matter's invoices need Read on its Procedures, so internal roles get org-level Read on `sprk_procedure` (or the depth that achieves it). **uac-r2 to confirm.** |
| Procedure Version | Follows its Procedure (parental). | |
| Rule set, policy, policy version | Unchanged ownership. | The rule set belongs to a Procedure (section 7.2). The planned record-scoped ownership routing (081, D-141) goes away with record scope. |
| Assignment / override (section 6) | A child of the core record it names, in the **secure family**. | An override's existence on a Secure matter is the matter's information. It joins uac-r2's lineage, census and creator-person lists (D-173 pattern). |
| Stored File (invoice, export) | A child of its invoice or export, in the secure family. | Access follows the invoice. |
| Stored File (Procedure Version) | Follows its Procedure Version, and so its Procedure. | |
| Matter-exception Procedure (section 6.3) | Like any Procedure. | Readable internally (O-6). If a Secure matter's exceptions turn out to be sensitive, this is where the O-6 fallback (secure per-matter Procedures) would apply. |

---

## 6. How a record becomes subject to Procedures and rules

### 6.1 Three sources

| Source | How | Stored? |
|---|---|---|
| **All matters** | Procedure Applies to = All matters (today's "client default" level). | No: computed |
| **Firm** | Procedure Applies to = Firm and its firm = **the invoice's firm** (`sprk_invoice.sprk_vendororg`; O-5, O-12 Q7). | No: computed |
| **Manual** | An assignment row: Procedure → matter, project or work assignment. | Yes |

**Firm matches are computed at evaluation time, not copied onto matters.** Copying would need a trigger whenever a matter's firm changes or a new firm Procedure is published. Without plugins (ADR-002), that means a reconcile job and a stale window. Computing is always current, and deterministic, because it reads only stored rows.

A matter billed by two firms gets each firm's Procedures on that firm's invoices. The matter's `sprk_assignedoutsidecounsel` only sets the default firm in the record's list (section 6.5).

### 6.2 Procedure-level overrides (O-4)

These are rows of the assignment table, with a mode:

| Mode | Meaning | Example |
|---|---|---|
| **Follow latest** (default for a manual assignment) | The Procedure's version in force on the work date (section 8.2). | Matter-specific instructions; v2 applies from its effective date. |
| **Pinned to version** | This Procedure Version, whatever is newer. | Matter grandfathered on firm F's 2025 rate card. |
| **Excluded** | A Procedure that would apply by "all matters" or "firm" does not apply to this record. | The firm's standard travel policy is waived for this matter. |

### 6.3 Rule-level overrides (O-10)

A **rule** here is a `sprk_policy` in a Procedure's rule set. It keeps its identity across the Procedure's versions (section 7.2), so a rule-level override survives a new Procedure Version.

| Action on the record | What is stored | Effect |
|---|---|---|
| **Exclude rule** | An override row: rule (`sprk_policy`) → record, mode Excluded, reason. | The rule does not run for this record's invoices. Example: no late fee. |
| **Change rule for this record** | (1) An Exclude row for the general rule. (2) A **record rule**: a copy of the general rule's parameters, edited, published as a policy in the record's **exceptions Procedure**. The exceptions Procedure is created on first use: Type = Billing instructions, no file needed, assigned manually to the record. Both are written in one BFF operation. | The record's version runs in place of the general one. Example: the travel code is removed from the non-reimbursable list, and the other expense caps are kept as copied. |
| **Remove an override** | The override row is removed. The record rule is retired, if there was one. | The general rule runs again. |

Why "change" means copy, edit and exclude, rather than storing new parameters on the override row:

- **The record rule is an ordinary rule.** It is typed and schema-validated (D-27), published and immutable, cited by Signals, and reviewable on the Rule Review page.
- **Precedence would not do it alone.** "Allow travel" removes an item from a **checklist** rule type, and checklist types add up across sources. A more specific rule cannot remove an item from the general list, so the general rule has to be excluded for that record.
- **Copying keeps the rest of the general rule.** The other expense caps stay in force.

**Inheritance:**

- An override on a project applies to work assignments filed directly under it (distance 1, section 6.4).
- An override on the work assignment itself (distance 0) takes precedence over one inherited from its parent.
- **Proposal: no "re-include" in R1.** A work assignment can't switch back on a rule its parent excluded.

**Rule types the examples need (to be decided):**

- **Late fees and discounts are not billing rule types today.** The 11 R1 types are listed in `rule-set-scope-design.md` §3.3.
- **A late fee is an added fee on the invoice (O-16).** The Procedure's rules decide whether it is allowed and how much: not allowed, allowed up to an amount or a percent, or allowed after N days. One additive-fee type covers late fees and other added charges (administrative fee, interest), with the fee kind as a parameter. Today it would need a code in the non-billable or expense checklist, which matches only when the firm codes it that way.
- **A negotiated discount** fits the invoice adjustments of D-42 (a percent applied after line adjustments). It would be a rule type that checks the firm applied it, or proposes the adjustment (to decide with ontology-r1).
- **Both are catalogue additions** (D-27, a closed set; ontology-r1 reviews). The override mechanism covers them once they exist.
- "Approve travel" works today, through ExpenseRule.

### 6.4 Inheritance: direct association only (unchanged rule)

The rule of D-172 stays. A work assignment filed under project P is subject to P's manual assignments and overrides at distance 1, and not to those of P's matter (no second hop). This reuses `CoreRecordChain` (planned in 082) and uac-r2's `NamePairParentAsync` for the project pair, unchanged.

### 6.5 The lists on the record (O-9; UX from the prototype, O-14)

The back end provides two read models, which the prototype's UI will use:

- **Procedures:** every Procedure the record is subject to, from all-matters, firm (the assigned outside counsel by default; any firm selectable), manual and inherited sources. Each shows the version in force today, its source, and any Procedure-level override. Actions: assign, create (an exceptions Procedure for this record), pin, exclude, remove.
- **Rules in force:** for a chosen date and firm, every rule with its source Procedure Version. It marks rules displaced by precedence and rules excluded or changed by a rule-level override. Actions: exclude, change, remove an override.

Both are produced by the same resolver that evaluation uses (section 7.1), so what users see is exactly what evaluation would use.

---

## 7. From Procedure to rules, and how evaluation finds them

### 7.1 The resolver: how rules are "calculated", deterministically

"Calculated" means one server function, `ApplicableRules(record, firm, workDate)`, that reads stored rows only. Given the same rows, it returns the same answer every time.

```
chain     = CoreRecordChain(record)                       // record (d0) + direct parents (d1), section 6.4
procs     = Procedures with Applies to = All matters
          ∪ Procedures with Applies to = Firm AND firm = invoice firm
          ∪ Procedures manually assigned on any chain record
          − Procedures Excluded on the chain               // the nearer override wins
for each procedure:
    procVersion = the pinned Procedure Version (nearer override wins)
                  OR  the Active Procedure Version whose effective dates contain workDate
                  (none in force on that date → the Procedure contributes nothing for that subject)
    level       = Manual d0 | Manual d1 | Firm | All matters
    rules      += the policy versions of the Procedure's rule set that belong to procVersion
                   (rules retired by that version are absent)
    rateCard    = procVersion's published rate card, if any
rules     −= rules Excluded on the chain (rule-level, nearer wins)
             // a "changed" rule is its general rule excluded plus the record rule,
             // which arrives through the record's exceptions Procedure at level Manual d0
→ precedence (section 7.3)
```

It replaces today's rule-set eligibility (`BillingFactLoader`, ClientDefault and Firm scopes) and the planned 082 scope selection. The precedence step (`InvoiceComplianceEvaluator`) is kept, re-keyed from rule-set scope to the levels above.

### 7.2 Drafting rules from a Procedure (O-12 Q6)

- **One rule set per Procedure.** `sprk_ruleset` gets a `sprk_procedure` lookup, replacing `sprk_sourcedocument`.
- **Each Procedure Version publishes policy versions.** `sprk_policyversion` gets a `sprk_procedureversion` lookup to the Procedure Version it came from. Its in-force dates follow that version's effective dates.
- **Drafting v1:** the existing drafting job (`BillingRuleDraftJobHandler`, Action `billing-rule-draft`) reads the file from the file store and proposes rules, each with a quoted clause and its location.
- **Creating v2 copies v1's rules** as v2 drafts. The reviewer edits, removes and adds, optionally re-running the drafting job on v2's file to highlight changes. On publish:
  - an edited or unchanged rule becomes a new version of the **same** policy (same rule identity, so rule-level overrides carry over);
  - a removed rule is retired from v2's effective date;
  - a new rule is a new policy.
- **Rate cards:** importing a workbook (`RateCardImportService`) creates the rate card of that Procedure Version. `sprk_ratecard` gets `sprk_procedureversion` in place of its three core-record lookups and the planned scope column. v2's card can likewise start as a copy of v1's.
- **Review and improve:** the Rule Review page is reached from the Procedure.
  - Correcting a published rule without a new file makes a new policy version for the same Procedure Version, as today.
  - Policy versions are immutable, and decisions already made keep the version they cited (section 8).

### 7.3 Several Procedures on one record

All Procedures the resolver picks are evaluated together, with the precedence already designed and reviewed (`rule-set-scope-design.md` §3.3), re-keyed to the levels in section 7.1:

- **Checklist types add up:** approved timekeepers, non-billable activities, required data, AI observations, and the non-reimbursable list in expenses. Every applicable Procedure's items apply. Removing an item for one record is a rule-level "change" (section 6.3).
- **Limit types replace:** rate tolerance, time increment, daily hours cap, invoice age, core-record window, duplicate definition, and per-category expense caps. The most specific level wins: Manual d0 > Manual d1 > Firm > All matters.
  - A broken or downgraded specific rule never removes the general one (B2, F1-b).
  - At the same level, all apply, and the assigning action warns.
- **Rates:** the tiers of task 085, re-keyed by level. A nearer card beats a farther one, and a timekeeper rate beats a classification rate. Different amounts in the deciding tier give **Ambiguous**: no rate is used and a finding is raised.
- **Determinism:** ties keep the existing ordinal order (policy code, then id).

### 7.4 Determinism and audit

- **Before evaluation:** the record's "rules in force" read model (section 6.5).
- **After evaluation:** each evaluation saves a **snapshot**. For each rule applied, it records the policy version, the Procedure Version, the source and level, the Procedure-level and rule-level overrides that decided it, and each rule displaced or excluded.
  - It is stored with the submission's findings: `sprk_invoicesubmission.sprk_findingsjson` already carries the clause.
  - Its fact summary also goes on the Decision Record.
  - Later edits to Procedures or overrides never change an earlier evaluation's story.

---

## 8. Signals, Decision Records and versions (O-3)

### 8.1 What they cite

Unchanged at the core: each Signal and Decision Record cites the **policy version**, which is immutable and carries the quoted clause and its location, plus reason codes and facts. Added:

- the **Procedure Version** in the evaluation facts, so an internal reviewer can open the source file (download through the BFF);
- the **basis** (source, level, override ids) in the facts.

Firms still see only the clause text, subject to the visible-to-firms flag (D-66, D-79), never the Procedure or its file.

### 8.2 Versions over time (O-12 Q5: by work date)

- **Which version applies:** a line is judged by the Procedure Version whose effective dates contain the line's work date. Without a line date, the invoice date is used (today's per-subject date rule). So 2025 work billed in 2026 is judged on v1, and 2026 work on v2.
- **Decisions already made under v1 stand.** Decision Records are append-only, Signals cite v1's policy versions, and a decided invoice is never re-evaluated.
- **An invoice still In Review** when v2 is published is re-evaluated by the same rule, and its snapshot shows which version judged which line.
- **A pinned override** keeps its version for that record, whatever the dates.

---

## 9. Invoices as files (O-8)

### 9.1 Submission routes

| Route | Today | New |
|---|---|---|
| Firm via SPA | The PDF is uploaded into the core record's SharePoint Embedded container as a Document (D-44). | The PDF goes to the invoice's files (Stored File purpose Invoice PDF). The SPA wizard (051) keeps its UI, and its upload goes to the file store. R3 coordinates the SPA build and deploy. |
| Internal upload | Same as the SPA. | Upload on the Billing tab or the invoice → invoice files. |
| Email attachment | The attachment is a Document. The 050a confirm route re-files it under the invoice (`InvoiceDocumentRefile`). | The attachment stays a Document with its email. **Submit Invoice** on the attachment copies its bytes into the new invoice's files and starts the submission. The Document is never moved. |

**What changes for 050a (the email route):**

- **Removed:** the re-file (moving the Document under the invoice), its retries, its "NOT filed" admin notes, and the K4 race. The Document never moves.
- **Kept, both reviewed by uac-r2 in rounds 2–4:**
  - the D-140 filing chain: which matter the email belongs to, following its thread as a union;
  - the isolation check at submit and at commit: a document protected under a Secure matter must not have its bytes copied into an invoice under another record.

  The copy is the new spot that needs the same guard.
- **Surfaces:** Submit Invoice is offered where users meet email attachments: the communication UI, and possibly the Outlook add-in's save flow (word-add-in-r1). Which ones is a UX decision (O-14).

### 9.2 Processing and viewing

- **Extraction:** Content Understanding reads the bytes. The BFF sends the file body from the file store, or a short-lived link the BFF creates server-side; the browser never gets it.
- **Viewer:** the bounding-box viewer (108) loads the PDF through the BFF download route.
- **Invoice index:** built from the structured invoice and line records (D-44's second half). It is not affected.
- **Lost by design:** invoice PDFs are no longer in the matter's Documents, the files search index, or AI chat over matter documents (owner: acceptable). The `invoice-files` container can be indexed later if needed.
- **Invoice lines:** a file belongs to the invoice, never to a line. The uac-r2 #2047 restriction on lines as parents is unaffected.

## 10. Export files (O-7)

The billing export (`sprk_billingexport`, 023) gets its file as a Stored File, purpose Export file. The E-Billing Output Files container and its key (D-55, D-67, 028) are retired from code. The owner-created dev containers are left in place, and the owner removes them when ready (O-13; the project rule is never to delete them ourselves).

---

## 11. Search and AI

- **Not needed for R1:** evaluation never reads the file (clauses are copied at drafting), and Signals and Decision Records never read it.
- **Procedure files:** nothing indexes them in R1. If ontology-r1 needs retrieval over Procedures for AI evaluation, a `procedures` index (blob indexer, trimmed by Procedure Read, internal only) is a follow-up. **Ask ontology-r1.**
- **Invoice files:** future (O-8).

---

## 12. What changes in existing decisions and completed work

| Item | Change |
|---|---|
| D-44 | Invoice PDFs move from SharePoint Embedded to invoice files. The structured invoice index is unchanged. |
| D-55, D-67 | Procedure Library and E-Billing Output Files containers and keys retire from code. The dev containers are left for the owner (O-13). |
| D-145 | Withdrawn: nothing is filed under a matter. |
| D-146 | Simpler: deleting a core record removes its assignment and override rows. Procedures, rule sets and rate cards are never touched. The record's exceptions Procedure and its record rules are retired (O-17). |
| D-120 | Rate cards are no longer tied to a core record. |
| D-171 point 3 / D-141 Q1 / S3, S4, S8 | Business-unit filter dropped (O-12 Q8). The S8 snapshot and its writer (087) are dropped. Check with uac-r2 that nothing of theirs relies on S8. |
| D-140 | Kept (the email route). |
| D-172 | Direct-association inheritance kept. Explicit record scope on rule sets and rate cards replaced by assignments. The rate-card S6 backfill and ontology D-174 / D-147 become moot. |
| `rule-set-scope-design.md` | Kept: §3.1 chain, §3.3 precedence (re-keyed). Replaced: §2 S1–S4, S6, S8; §4.1–4.5; §5.2–5.4. S9 creator person carries over to the new tables. |
| 019 / 076 / 077 rule-set store, endpoints, Rule Review | Rule set gains `sprk_procedure` and loses `sprk_sourcedocument`. Policy version gains the Procedure Version. Creation starts from a Procedure. Rule Review is reached from the Procedure. |
| 028 containers | Replaced by the blob containers. |
| 044 / 049 / 051 submission | The upload target changes to the file store. Processing reads bytes from the file store. |
| 050a confirm | Becomes Submit Invoice (copy). The re-file is retired. The D-140 chain and the isolation check are kept. |
| 075 drafting job | Reads the file from the file store. |
| 078 rate-card import | The card belongs to a Procedure Version. |
| 108 viewer | Loads through the BFF download route. |
| Dev data | Recreated, not migrated (O-13). |

## 13. Effect on the open tasks 079–087 (sketch; re-planned after alignment)

| Task | Becomes |
|---|---|
| 080 schema | New schema: `sprk_procedure`, `sprk_procedureversion`, the assignment/override table, `sprk_storedfile`; `sprk_procedure` on rule set; `sprk_procedureversion` on policy version and rate card; creator person on the new tables. Dropped: S1–S4, S6, S8. ontology-r1 reviews the schema again. |
| 081 rule-set service | Procedure service (versions, effective dates, the no-overlap invariant, copy-forward of rules and rate cards) plus the assignment and override service, including "change rule for this record". |
| 082 selection | The resolver (section 7.1), reusing `CoreRecordChain`. |
| 083 firm clause | Uses the resolver. |
| 084 Rule Review UI | Waits for the prototype (O-14). The back end exposes the two read models of section 6.5. |
| 085 rate cards | Rate-card tiers re-keyed to resolver levels. The scope column and backfill are dropped. |
| 086 rate-card secure family, workbook filed under record | Mostly obsolete. What remains is the secure-family rows for the assignment table and invoice Stored Files. |
| 087 BU snapshot writer | Dropped (O-12 Q8). |
| New | File store service and control; Stored File schema; storage provisioning hand-off; Submit Invoice; moving invoice submission and exports to files; possibly new rule types (added fee, discount; section 6.3). |

## 14. Coordination (review requested 2026-10-21)

- **ontology-r1:**
  - the Procedure name and Area/Type values (ontology component model §3);
  - the rule set per Procedure and the policy-version → Procedure Version link;
  - the resolver's levels;
  - rule-level overrides (exclude, change);
  - new rule types for added fees (late fee and similar) and discount (D-27);
  - the drafting source change;
  - a full schema re-review (it replaces the 080 work they reviewed);
  - whether they need a Procedures search index;
  - **overlap:** ontology's own matter-scoped rules (`sprk_policy.sprk_matter`, 102b / D-165, `sprk_policy_matter` cascade delete). Should non-billing matter rules move to the same Procedure and assignment model, or stay as they are, with billing using assignments?
- **uac-r2:**
  - Procedure read depth for internal roles;
  - the assignment/override table and Stored File joining the secure family;
  - the copy guard on Submit Invoice (the D-140 chain plus the isolation check);
  - the business-unit filter and S8 dropped (does anything of theirs rely on S8?);
  - which of #2159 / #2161 / #2151 and T7 ebilling still needs (probably none);
  - the file download route's rights check.
- **word-add-in-r1:**
  - Submit Invoice on email attachments: whether the Outlook add-in's save flow is a surface for it, and how it interacts with their attachment-filing work (`FileIfUnfiledAsync`, D7 Restricted, #2171);
  - invoices no longer becoming Documents;
  - a single attachment save that files its parent email.
- **SPA-r3:** the SPA upload target and the dev deploy (after peer alignment).
- **customer-provisioning-orchestration-r1:** blob containers and the role assignment in environment setup (after alignment).

## 15. Questions still open

- Q1, Q16 and Q17 are answered: O-15, O-16, O-17.
- **Q19 (ontology-r1):** is a discount rule a check that the firm applied it, or does it propose the adjustment (D-42)?
- **Q18 (ontology-r1):** the overlap with ontology's matter-scoped rules (section 14).
- UI/UX (v1 Q11–Q15) moves to the prototype (O-14).
