# Procedures and files — revised approach

> **Status**: v4, 2026-10-21. All owner questions are answered (section 2).
> - A top-tier design review (Fable, owner-approved) checked v3 against the code. Its 13 must-fix findings are folded in (section 16).
> - **Next:** peer review by ontology-r1, uac-r2 and word-add-in-r1 (section 14).
> - Nothing is implemented. No schema, task or decision row has changed yet.
> - Once the peers align and the owner finalizes, this note becomes decision D-148 plus revised POMLs for 079–087 and new tasks. The UI/UX is prototyped separately (O-14); the back end goes first.
>
> **Supersedes, once agreed:** D-44 (in part), D-55 (in part), D-67, D-145, D-146 (in part), D-171 point 3 / D-141 Q1 (the business-unit filter), and most of `rule-set-scope-design.md`.

---

## 1. The approach in one page

**Procedures and their versions**

- A **Procedure** (`sprk_procedure`) is the internal record of any document that drives policy or rules: client billing guidelines, a firm's rate card, special billing instructions for one matter, an internal procedure, and non-billing policy documents later.
- Each issue of it is a **Procedure Version** (`sprk_procedureversion`), which carries the file, its effective dates and its status. The file is kept in **our own Azure Blob storage**. A Procedure is not a Document, and there is no document management.
- A Procedure's rules live in one rule set. Each Procedure Version publishes its own versions of those rules, so a rule keeps its identity across versions. A rate card imported from a workbook belongs to its Procedure Version.

**How a record becomes subject to Procedures.** A matter, project or work assignment is subject to Procedures, not to rule sets directly, in three ways:

1. Procedures that apply to **all records**.
2. Procedures for the **invoice's firm**.
3. Procedures **assigned manually** to the record.

**Overrides on the record** work at two levels:

- **Procedure level:** pin a version (keep 2025 rates), or exclude the Procedure.
- **Rule level:** exclude one rule (no late fee on this matter), or change it for this matter (travel is allowed here).

**Evaluation.** One deterministic resolver gives the rules in force: record → Procedures → Procedure Version for the work date → rules → overrides → precedence.

- It fails closed when it can't read what it needs.
- Every evaluation saves a self-contained snapshot of what applied and why.
- Signals and Decision Records keep citing immutable policy versions, so a decision made under v1 stays a v1 decision.

**Files for invoices and exports.** Invoices and billing exports keep their files in the same file store, not as Documents. An emailed invoice still arrives as a Document. **Submit Invoice** on the attachment copies its bytes into the invoice's files, under the guards of section 9.1.

```
Procedure "Acme outside counsel guidelines"  (applies to: firm F)        rule set (one per Procedure)
 ├─ Version 1 (effective 2025-01-01 → 2025-12-31) ── file ─┐                ├─ rule "Travel"        : v1, v2
 └─ Version 2 (effective 2026-01-01 → )            ── file ─┴─ publish ──────┼─ rule "Added fees"    : v1, v2
                                                                             └─ rule "Block billing" : v2 only
 Matter M ── subject to: [all-records Procedures] + [firm F's Procedures on F's invoices] + [manual assignments]
             Procedure overrides: pin "F rate card" to its 2025 version
             rule overrides:      exclude "Added fees" · change "Travel" for M (travel allowed)
                 │
 Invoice on M from firm F, line worked 2026-03-10
   → resolver → rules in force → evaluation → Signals / Decision Records (cite policy versions)
   → snapshot: each rule's policy version, Procedure Version, source, level, and the overrides that decided it
```

---

## 2. Owner decisions (2026-10-20 and 2026-10-21)

| # | Decision |
|---|---|
| O-1 | Procedure holds guidelines, rate cards, billing instructions, internal procedures and any other documents that drive ontology policy or rules, in billing or other areas. Area and Type are metadata. |
| O-2 | Procedure files go in **our own Blob storage**, not Dataverse file columns and not SharePoint Embedded. An upload/download control is needed. A storage account exists, and the shared library likely holds the control parts. |
| O-3 | Linkage is **record → Procedures → their rules**. The rules that apply to a record must be visible, reviewable and deterministic. Decisions under v1 keep v1. Several Procedures can apply to one record. |
| O-4 | A new version means the record uses the new version, **with per-record overrides**, e.g. grandfathered 2025 rates. The list of Procedures a record is subject to must be visible. |
| O-5 | Records are subject to Procedures **by the invoice's firm** (organization) or by **manual association**. More sophisticated matching can come later. The rules remain what the billing analysis evaluates and records. |
| O-6 | Procedures are **internal only** and follow our access control, Secure records included. There are no special "secure Procedures"; a Procedure can be reused. The owner does not see this as a risk. Fallback if a problem appears: secure Procedures, one per Secure matter. (Section 5 proposes one narrow exception for system-created exceptions Procedures. **Owner to confirm.**) |
| O-7 | **Export files** are plain files of their export, not Documents. |
| O-8 | **Invoice files** are plain files of the invoice: no Documents, no SharePoint Embedded and no document management. They are listed on the Billing tab, apart from Documents. Search and AI over them are future work unless the analysis needs them. An emailed invoice still creates a Document from the email, and **Submit Invoice** on the attachment routes its bytes to the invoice's files. |
| O-9 | Lists on the record: the Procedures it is subject to, and possibly its rules. These can sit in a popup behind a PCF toolbar. |
| O-10 | **Rule-level overrides**: on a matter, a rule can be switched off (no late fee, no discount) or changed (travel approved). |
| O-11 | Procedure details: **Issued By** (organization), **External Contact** (contact), **Internal Contact** (contact). A billing flag is ebilling's call; it is likely handled at the rule level. |
| O-12 | One shared file table (Q4). Version by **work date** against the Procedure's effective dates (Q5). **v2 copies v1's rules for editing** (Q6). Firm = the **invoice's firm** (Q7). **No business-unit filter** (Q8). Blob protections if not difficult (Q9). Whatever is easiest for dev (Q10). |
| O-13 | **No migration of dev containers or files.** They are dev artifacts. |
| O-14 | **UI/UX is prototyped separately** and drives the matching tasks. Back-end work goes first. |
| O-15 | **Two tables: Procedure and Procedure Version.** |
| O-16 | **A late fee is just an added fee**, and the Procedure's rules decide whether it is allowed. |
| O-17 | **A deleted core record's exceptions Procedure and record rules are retired.** |

---

## 3. Procedure and Procedure Version

### 3.1 Two tables (O-15)

- **`sprk_procedure` (Procedure)** is the document as a lasting thing: name, area, type, **Applies to**, **Firm**, Issued By, and the contacts. Assignments, overrides and its rule set point to it.
- **`sprk_procedureversion` (Procedure Version)** is one issue of it: version number, effective from / to, status, the file, the policy versions it published, and its rate card. It has a parental relationship to its Procedure.

**Why two tables:**

- **Applicability lives in one row.** The resolver needs Applies to and Firm before it can pick a version by date.
- **The document's identity doesn't depend on version 1's row.**
- **Every link has a clear target:**
  - assignment → Procedure;
  - pin → Procedure Version;
  - rule set → Procedure;
  - policy version → Procedure Version;
  - rate card → Procedure Version;
  - file → Procedure Version.

**Invariants (BFF-owned, ADR-002):**

- **I-P1.** A Procedure's Active versions never overlap in effective dates. Publishing a new version with effective-from D sets the previous Active version's effective-to to D − 1, in one BFF operation.
- **I-P2.** Applies to, Firm, effective dates, status and the file are **field-secured**: Create and Update for the BFF application user only, Read for every user (the S7 pattern). A model-driven edit of Applies to would otherwise change who a Procedure governs with no BFF check.
- **I-P3.** A Procedure of Type = Rate card **always names its Firm**, whatever its Applies to. `RateCardResolver.cs:125` drops a card whose firm is not the invoice's, and `RateCardImportService` requires the firm (`:19,168,298`). A manually assigned card still names its firm, and the card keeps `sprk_ratecard.sprk_firm` set from it.
- **I-P4. Deletion and retirement:**
  - **Deleting a Procedure Version is refused (Restrict) while policy versions or a rate card reference it.** This uses platform-native Restrict on those lookups (ADR-002 WP-1(a), checked with a schema `-Verify`), because Signals cite them. The same goes for `sprk_ruleset → sprk_procedure`. D-172's "no Restrict" applied to core records, not to these.
  - **Retiring is the normal end of life.** "Retired" ends the Procedure for evaluations that start after it. Older versions never reactivate. Decided invoices are unaffected. A retired Procedure's rules are switched off, and the nightly job then closes their open Signals as PolicyRetired (D-170, `SignalClosureService.cs:164-177`).

### 3.2 Metadata

**Procedure** (the document):

| Column | Type | Notes |
|---|---|---|
| Name | text | Internal name. |
| Firm-facing name | text | What a firm sees where D-66 / D-79 show the "rule set name". It defaults to Name. An exceptions Procedure gets a neutral one ("Matter billing instructions"), so an internal name never reaches a firm. |
| Area | choice | Billing, plus other ontology areas later (values with ontology-r1). |
| Type | choice | Guideline · Rate card · Billing instructions · Internal procedure · Other. |
| Applies to | choice | **All records** (every matter, project and work assignment: today's ClientDefault) · **Firm** · **Assigned records only**. Field-secured (I-P2). |
| Firm | lookup `sprk_organization` | Required when Applies to = Firm, and always for a rate card (I-P3). Field-secured. |
| Issued By | lookup `sprk_organization` | O-11. |
| External Contact | lookup `contact` | O-11. |
| Internal Contact | lookup `contact` | O-11. |
| Exceptions for | lookup to the record (matter / project / work assignment), set only on an exceptions Procedure | Marks a system-created exceptions Procedure (section 6.3) and gives it its secure-family parent (section 5). Field-secured. |
| Status | choice | Active · Retired. Field-secured. |
| Created by person | lookup `systemuser` | S9 / D-173. |
| Notes | multiline | |

**Procedure Version** (one issue):

| Column | Type | Notes |
|---|---|---|
| Procedure | lookup, parental | |
| Version number | whole number | Assigned by the BFF. |
| Previous version | lookup `sprk_procedureversion` | |
| Effective from / to | date | Field-secured (I-P1, I-P2). |
| Status | choice | Draft · Active · Superseded · Retired. Field-secured. |
| File | (section 4) | One file per version. Optional: an exceptions Procedure has no file. |
| Created by person | lookup `systemuser` | |

There is no billing flag (Area marks billing; the rule type marks billing rules, O-11). There is no "visible to firms" flag on the Procedure: the rule-set flag still decides whether a firm sees clause text (D-79), and firms never see a Procedure or its file (D-66).

### 3.3 §11 justification for the new tables (root CLAUDE.md §11)

**`sprk_procedure`**

1. **Overlap.** `sprk_ruleset` (`RuleSetService.cs:320`) already carries a name, scope, firm, effective dates, visible-to-firms, a source document and a status.
2. **Why extending the rule set isn't enough.** A Procedure is the document, and the rule set is the rules drawn from it. O-1 needs non-billing documents, files, versions and per-record assignment, none of which a billing rule set can hold. A rule set stays the rules and points to its Procedure.
3. **Cost of doing nothing.** No home for the document and its file, and no way to make a record subject to a document (O-1, O-3, O-5).

**`sprk_procedureversion`**

1. **Overlap.** `sprk_policyversion` versions *rules*, not documents.
2. **Why not one table.** A single table with a "family" lookup was rejected (O-15): applicability would sit in several rows, and identity would hang on v1's row.
3. **Cost of doing nothing.** No grandfathering by version (O-4) and no per-work-date choice of document (O-12 Q5).

**Assignment / override table** (`sprk_procedureassignment`, name to be decided)

1. **Overlap.** A plain N:N between Procedure and record, or `sprk_policy.sprk_matter` (ontology's matter-scoped rules, 102b).
2. **Why those can't be extended.** Neither can carry the mode (follow / pin / exclude), the pinned version, the excluded rule, or the reason. `sprk_policy.sprk_matter` scopes a rule to *one* matter; it cannot waive a shared rule for one matter.
3. **Cost of doing nothing.** No grandfathering, waivers or rule changes per record (O-4, O-10).

### 3.4 Name

`sprk_policy` already exists as the ontology's rule container; "Procedure" avoids the clash. ontology-r1 confirms the name and the Area/Type values (ontology component model §3).

---

## 4. The file store (Procedures, invoices, exports)

### 4.1 One file table, one service, one control (O-12 Q4)

- **Table `sprk_storedfile`** ("Stored File"; name to be confirmed). One row per file.
  - **Parent:** exactly one lookup is set: `sprk_procedureversion`, `sprk_invoice` or `sprk_billingexport` (more later).
  - **Purpose:** Procedure source · Invoice PDF · Invoice supporting file · Export file.
  - **Firm-visible** (yes/no, invoice files only; default No): only Invoice PDFs the firm submitted are Yes. Internal supporting files never reach a firm.
  - **File details:** file name, content type, size, SHA-256.
  - **Blob path:** field-secured, server-only.
  - **Audit:** uploaded by / on.
- **A BFF service** writes, reads and deletes blobs. It extends the pattern of `Services/Ai/Sessions/SessionFileBlobStore.cs` (managed identity only, `Azure.Storage.Blobs` already referenced; no new package).
- **One upload/download control:** a PCF built from the shared library's `FileUpload/FileUploadZone` and `UploadedFileList`. Its UX comes from the prototype (O-14).

**§11:**

1. **Overlap.** `sprk_document` is document management on SharePoint Embedded, which is ruled out (O-2, O-8). `SessionFileBlobStore` holds AI session bytes with a session lifecycle.
2. **Why extending isn't enough.** A parent row cannot hold several files (an invoice has several).
3. **Cost of doing nothing.** No place for these files outside document management.

### 4.2 Blob layout and protection

- **Containers:** `procedures`, `invoice-files` and `billing-exports`.
- **Path:** `{tenantId}/{parent table}/{parent id}/{file id}`.
- **Write-once:** a file is never overwritten. A new file is a new row.
- **Malware scanning (O-12 Q9):** Defender for Storage on-upload scanning, where the account has it.
  - The scan result is a **blob index tag** that Defender writes. Nothing is queued.
  - The download route and every processing step (extraction, drafting) read the tag. Pending or missing means wait or refuse, and Malicious means refuse.
  - No Event Grid receiver in R1: the tag is read on demand.
  - Where scanning is not enabled, the file is served and shown as "Not scanned".
- **No immutability (WORM) policy in R1.** A version-level immutability policy would block deleting files with their parent. Soft delete is on.
- **Identity:** the BFF managed identity only: no account key, connection string or SAS (the `SessionFileBlobStore` refusal, `:704-720`). The browser never receives a storage URL. Content Understanding gets the **bytes inline**, which its client already supports (`ContentUnderstandingClient.cs:104,255`).
- **Provisioning:**
  - containers and the BFF identity's Storage Blob Data Contributor role go into environment setup;
  - `storage-account.bicep` creates the role assignment only when `appServicePrincipalId` is passed, which `customer.bicep` doesn't do today, so customer-provisioning gets a hand-off;
  - dev uses the existing account.
- **Retention and deletion:**
  - **Lifetime:** a file lives as long as its parent row.
  - **Delete through the BFF:** deletes the files with a job (ADR-004).
  - **Delete from a model-driven form:** a parent deleted that way bypasses the BFF, so a scheduled **orphan reconcile** job (ADR-036, ADR-002 WP-5) deletes blobs whose Stored File row or parent is gone, after a grace period.
  - **ADR-015 doesn't govern these stores.** They are business records (invoices, exports, policy documents), not AI work history, so they sit outside ADR-015's tiers. GDPR erasure for business records follows the records' own retention.

### 4.3 Upload and download

- **Upload:**
  1. The BFF checks Write on the parent row.
  2. It streams the bytes to a blob keyed by the new file id.
  3. It writes the Stored File row.
  4. If the row write fails, it deletes the blob. A compensation job retries that delete, and the orphan reconcile job backstops it.
- **Download (internal):** the BFF checks Read on the parent row with the caller's own rights (`GetCallerRightsForRecordsAsync`, `CallerRecordAccessProbe.cs`), checks the scan tag, and then streams the blob.
- **Firms:**
  - No route reaches Procedure files.
  - Invoice files reach a firm only through a **new** external route. Today the external invoice-submission routes carry no file (`ExternalInvoiceSubmissionEndpoints.cs`), and external document content is served by `ExternalProjectDataEndpoints.cs:138,263`.
  - The new route scopes by firm and core record (D-70) and serves only Firm-visible files. Peer question for SPA-r3 and uac-r2.

---

## 5. Access control (O-6)

| Row | Owner and visibility | Notes |
|---|---|---|
| Procedure (reusable) | Owned by the uploader's business unit; read by internal users at the depth their roles give. Internal only. | Not in the secure family (O-6). People who review a Secure matter's invoices need Read on its Procedures, so internal roles get org-level Read on `sprk_procedure` (or the depth that achieves it). uac-r2 to confirm. |
| **Exceptions Procedure** (Exceptions for = a record) | **A child of its record, in the secure family**: lineage, role row, census and creator person, as 081 planned for record-scoped rule sets (D-141 Q11). Its versions, rule set, policies and policy versions follow it. | **Proposed; owner to confirm.** By construction it holds one record's billing exceptions, including the clause text of a changed rule. It is never reused, so this is not the "secure Procedure" O-6 declined. If it were org-readable, half of a "change" would be secure (the Exclude row) and half not (the record rule). |
| Procedure Version | Follows its Procedure (parental). | |
| Rule set, policy, policy version (reusable Procedure) | Unchanged ownership. | The planned record-scoped ownership routing for rule sets (081) applies only to exceptions Procedures now. |
| Assignment / override | A child of the record it names, in the secure family. | Its existence on a Secure matter is the matter's information. |
| Stored File (invoice, export) | A child of its invoice or export, in the secure family. | Access follows the invoice. |
| Stored File (Procedure Version) | Follows its Procedure Version. | |

---

## 6. How a record becomes subject to Procedures and rules

### 6.1 Three sources

| Source | How | Stored? |
|---|---|---|
| **All records** | Applies to = All records | No: computed |
| **Firm** | Applies to = Firm and firm = **the invoice's firm** (`sprk_invoice.sprk_vendororg`) | No: computed |
| **Manual** | An assignment row: Procedure → matter, project or work assignment | Yes |

- **Firm matches are computed at evaluation time, not copied onto matters.** Copying would need a trigger on every firm change, and without plugins that means a reconcile job and a stale window.
- **A matter billed by two firms** gets each firm's Procedures on that firm's invoices.
- **Which firm the record's list shows by default** is a peer question. The note first said `sprk_assignedoutsidecounsel`, but the submission rule uses `sprk_assignedlawfirm1/2` (`ExternalInvoiceSubmissionAuthorizationFilter.cs:242`). Use one definition, the submission rule's unless the owner says otherwise. It only sets the default view; evaluation always uses the invoice's firm.

### 6.2 Procedure-level overrides (O-4)

| Mode | Meaning | Example |
|---|---|---|
| **Follow latest** (default for a manual assignment) | The version in force on the work date (section 8.2). | Matter-specific instructions; v2 applies from its effective date. |
| **Pinned** | This Procedure Version, whatever is newer. | Grandfathered on firm F's 2025 rate card. |
| **Excluded** | A Procedure that would apply by all-records, firm or a parent assignment does not apply to this record. | The firm's standard travel policy is waived here. |

### 6.3 Rule-level overrides (O-10)

A **rule** is a `sprk_policy` in a Procedure's rule set. It keeps its identity across the Procedure's versions (section 7.2), so a rule-level override survives a new version.

| Action | What is stored | Effect |
|---|---|---|
| **Exclude rule** | An override row: rule → record, mode Excluded, reason. | The rule does not run for this record. Example: added fees not allowed. |
| **Change rule for this record** | (1) An Exclude row for the general rule. (2) A **record rule**: a copy of the general rule's current parameters, edited and published in the record's **exceptions Procedure**, which is created on first use (Type = Billing instructions, Exceptions for = the record, no file, assigned manually). The record rule stores **based on** = the general rule's policy version it copied. Both are written in one BFF operation. | The record rule runs instead. Example: travel removed from the non-reimbursable list, other caps kept. |
| **Remove an override** | The override row is deleted (its content survives in past snapshots, section 7.4). A record rule is switched off. | The general rule runs again. |

Why "change" means copy, edit and exclude:

- **The record rule is an ordinary rule.** It is typed and validated (D-27), immutable once published, cited by Signals, and reviewable.
- **Checklist types add up across sources.** A more specific rule cannot remove an item from the general list, so the general rule is excluded for that record.

**When the general rule moves on:**

- **The general rule gets a new version.** The record rule does not follow it. The "rules in force" read model and a notice at the general rule's publish mark the record rule **stale** (based on ≠ the general rule's current version), so someone can re-copy or keep it.
- **The general rule is retired** (or its Procedure no longer applies). The record rule keeps running and is flagged **base retired** for review. A deliberate matter exception never silently disappears, and it never silently outlives its reason unflagged.
- **Known limit (Replace types).** A record rule sits at level Manual d0, so for a Replace type it also displaces a **Firm** rule of the same type that the user never touched. The "change" action warns when another applicable rule of that type exists at a lower level.

**Rule types the examples need:**

- **Added fees (O-16).** A late fee is an added fee on the invoice. A new type, **AddedFee**, with the fee kind (late fee, administrative fee, interest …) and an allowance: not allowed, a cap as an amount or a percent, or allowed after N days. It is named AddedFee to avoid confusion with the precedence flag `IsAdditive`. Peer question for ontology-r1: does extraction give fee lines distinctly, and does it carry the due date that "after N days" needs?
- **Discount.** Fits the invoice adjustments of D-42. Peer question: does the rule check that the firm applied the discount, or propose the adjustment?
- **Catalogue additions** go through D-27 (closed set) and ontology-r1. "Approve travel" works today, through ExpenseRule.

### 6.4 Inheritance: direct association only (unchanged)

A work assignment filed under project P is subject to P's manual assignments and overrides at distance 1, and not to those of P's matter (D-172; `CoreRecordChain`; uac-r2's `NamePairParentAsync`).

### 6.5 Determinism rules for overlapping sources

- **One Procedure, several sources:** the highest level wins. A Procedure that is both firm-matched and manually assigned is level Manual.
- **Assignment rows at different distances:** the **nearer row wins, whatever its mode**. A Follow-latest assignment on a work assignment (d0) overrides a Pin on its project (d1), and an Exclude at d0 overrides an assignment at d1. There is no "re-include" across distances beyond this: an Exclude at d1 is lifted only by a d0 assignment of the same Procedure.
- **Two rows at the same distance for the same Procedure** are refused by the BFF (I-A1). Rule-level overrides follow the same nearer-wins rule.

### 6.6 The lists on the record (O-9; UX from the prototype)

The back end provides two read models, both produced by the evaluation's own resolver:

- **Procedures:** every Procedure the record is subject to (sources, inheritance), the version in force today, and its overrides.
- **Rules in force:** for a chosen date and firm, every rule with its source Procedure Version. It marks rules displaced, excluded, changed, stale or base retired.

---

## 7. From Procedure to rules, and how evaluation finds them

### 7.1 The resolver

`ApplicableRules(record, firm, workDate)` reads stored rows only.

```
chain    = CoreRecordChain(record)                       // d0 + direct parents d1 (section 6.4)
rows     = assignment/override rows on the chain          // read in full; ANY read fault → the evaluation faults
procs    = Applies to = All records
         ∪ Applies to = Firm AND firm = invoice firm
         ∪ manually assigned on the chain
         − Excluded (nearer row wins, section 6.5)
for each procedure (status Active):
    procVersion = the Pin (nearer row wins) OR the Active Procedure Version whose effective dates contain workDate
    if none:  contributes nothing for this subject, AND raises the internal-only system finding (below)
    level    = Manual d0 | Manual d1 | Firm | All records   (highest source wins, section 6.5)
    rules   += for each enabled policy of the Procedure's rule set: its policy version for procVersion (section 7.2)
    rateCard = procVersion's published rate card (always firm-named, I-P3)
rules    −= rules Excluded on the chain (nearer wins); record rules arrive through the exceptions Procedure (Manual d0)
→ precedence (section 7.3)
```

- **Fails closed.** Any failed read of chain, assignment, override, Procedure or version rows faults the evaluation. The job retries, and nothing is ever judged on a partial set. This is the loader's existing contract (`BillingFactLoader.cs:243-244`).
- **Gaps are visible.** When a Procedure that applies has no version in force on the date, or a pinned version was retired, the subject is not silently unjudged. The system raises an **internal-only finding** on the system policy (`BILL-SYSTEM-SUBMISSION`, the D-115 pattern) naming the Procedure and the date.
- It replaces today's rule-set eligibility (`BillingFactLoader`, ClientDefault and Firm) and the planned 082 selection. The precedence step is kept, re-keyed.

### 7.2 Rule versions under Procedure Versions (re-keys today's version chain)

**Today**, a rule (`sprk_policy`) has one chain of versions:

- an edit creates a version with in-force-from = now (`RuleSetService.cs:1296-1297`);
- publish refuses an effective date earlier than the predecessor's (`:1628-1631`), then end-dates the predecessor and moves `sprk_currentversion` (`:1720-1731`);
- the loader takes current or end-dated versions (`BillingFactLoader.cs:116-135`), and the evaluator picks by date (`InvoiceComplianceEvaluator.cs:397-399`).

That chain cannot hold two Procedure Versions' rules side by side. A March correction of a rule for Procedure Version 1 would be refused, or would knock out Version 2's version.

**For billing, this changes to:**

- Each policy version is linked to **one Procedure Version** (`sprk_policyversion.sprk_procedureversion`).
- **The Procedure Version defines when the rule is in force.** Its effective dates are the window. For a pinned record, the window is ignored and the pinned version's rules apply.
- **Corrections to a Procedure Version's rule** add a policy version under the same Procedure Version, with its own effective-from. Within that Procedure Version, the latest published policy version whose effective-from ≤ the work date applies, so a mid-year correction applies from its date and earlier work keeps the earlier wording.
- **The `sprk_currentversion` pointer stops deciding what is in force** for billing rules. It is kept as "latest published", for display. `RuleSetService` edit, publish, `IsDraft` and `Predecessor` are re-keyed per (policy, Procedure Version).
- **Ontology's own readers are unaffected:**
  - `SignalReevaluationJob.cs:226-235`;
  - `SignalClosureService.cs:54-56`.

  Both stay Ontology-only, which ontology-r1 confirms.

**v2 copies v1 (O-12 Q6):** creating Version 2 copies Version 1's rules as Version 2 drafts. On publish:

- **Kept or edited rules** become new policy versions of the **same** policies, under Version 2.
- **Removed rules:** a rule dropped by Version 2 stays **enabled** and simply has no policy version under Version 2. Work dated in Version 1's window still sees it, and its open Signals stay open. A rule is switched off (and its Signals closed, D-170) only when its whole Procedure is retired, or when it is a record rule whose override is removed.
- **New rules** are new policies.

**Drafting** (`BillingRuleDraftJobHandler`) is keyed **per Procedure Version**. Today the idempotency key is the rule-set id (`:77-85`) and "already drafted" short-circuits (`:119-130`), so one rule set per Procedure would make a Version 2 run a no-op. Its source is the version's Stored File, not `sprk_sourcedocument` (`RuleSetService.cs:614`).

**Rate cards:** importing a workbook creates the card of that Procedure Version, and Version 2's card can start as a copy. `sprk_ratecard` gets `sprk_procedureversion` in place of the three core-record lookups and the planned scope column, and keeps `sprk_firm` (I-P3).

### 7.3 Several Procedures on one record

The reviewed precedence (`rule-set-scope-design.md` §3.3) is kept, re-keyed to the levels above.

- **Checklist types add up.**
- **Limit types replace:** Manual d0 > Manual d1 > Firm > All records. A broken or downgraded specific rule never removes the general one.
- **Rates:** 085's tiers re-keyed. Different amounts in the deciding tier give Ambiguous.
- **Ties** keep the existing ordinal order (policy code, then id).

### 7.4 The snapshot (audit and reproduction)

Each evaluation stores, with the submission's findings (`sprk_findingsjson` already carries `policyVersionId` and the clause, `InvoiceSubmissionPersister.cs:1038-1044`):

- **For each rule applied:** the policy version, the Procedure and Procedure Version, the source and level, and the **content** of every override that decided it (mode, rule, reason, distance, the chain record), not just its id, because override rows are deleted when removed.
- **Also recorded:** rules displaced or excluded, and gap findings.

Its fact summary goes on the Decision Record. Later edits never change an earlier evaluation's story.

---

## 8. Signals, Decision Records and versions

### 8.1 What they cite

Unchanged at the core: the immutable policy version (clause and location), reason codes and facts. Added in the facts: the Procedure Version (an internal reviewer can download the source) and the basis (source, level, override content). Firms see only clause text under the visible-to-firms flag, with the rule set's **firm-facing name** (section 3.2), never the Procedure or its file.

### 8.2 Versions over time (O-12 Q5)

- **Which version judges a line:** the one whose effective dates contain its work date (else the invoice date; today's per-subject rule). So 2025 work billed in 2026 is judged on Version 1.
- **Decided invoices stand:** Decision Records are append-only, and Signals cite Version 1's policy versions.
- **No re-evaluation on publish.** Publishing a Procedure Version does **not** re-evaluate invoices In Review. Re-evaluation happens only on the existing triggers: the persister's re-evaluation on a changed core record (`InvoiceSubmissionPersister.cs:248-251`) and corrections (task 101). A publish-triggered re-evaluation job is possible later (new surface, §11).
- **A pinned override** keeps its version whatever the dates.

---

## 9. Invoices as files (O-8)

### 9.1 Submission routes

| Route | New |
|---|---|
| Firm via SPA | The PDF goes to the invoice's files (purpose Invoice PDF, Firm-visible). The SPA wizard (051) keeps its UI. R3 coordinates the build and deploy. |
| Internal upload | Billing tab or the invoice → invoice files. |
| Email attachment | The attachment stays a Document with its email. **Submit Invoice** copies its bytes into the new invoice's files and starts the submission. The Document never moves. |

**Submit Invoice, step by step:** it keeps 050a's checks and replaces the re-file's guarantees.

1. **At Submit Invoice (confirm):** the D-140 filing chain (communication → thread union) and `ExistingDocumentIsolationCheck`, unchanged. The answers are 409 `document_core_record_mismatch` and 503 on a read fault.
2. **In the job, staging:** copy the bytes to a staged blob keyed by the submission id. It is unreachable without a Stored File row, so a staged blob exposes nothing.
3. **In the job, commit:** in one Dataverse transaction, write the invoice, its lines, the **Stored File row** pointing at the staged blob, and the document-row guard (`IfRowVersionMatches`, `InvoiceSubmissionPersister.cs:497-511`). Re-run the isolation check before the transaction, as today's commit-time check does.
4. **Rollback:** if the transaction fails, delete the staged blob. A compensation job retries, and the orphan reconcile job backstops it.
5. **After commit:** re-run the isolation check. If the document has become isolated meanwhile, delete the Stored File row and the blob (compensation), mark the submission failed with an admin note, and never leave the bytes readable under the invoice.

**What is different from 050a, for uac-r2 to accept or reject explicitly:**

- **The backstop is gone.** The re-file passed through the real resolver inside `ReparentAsync`, which refused a move out of a Secure record (tests `Refile_…NeverReleased…`). A byte copy never passes through the resolver.
- **K4 now exposes bytes.** In the K4 race (a thread join between check and commit), 050a's document itself stayed isolated, because the reconciler narrowed it. A copied Stored File under M is readable under M. Step 5 closes the window after commit, but between commit and the re-check the bytes are readable under M for seconds.
- **Removed with the re-file:** its retries, its "NOT filed" admin notes, and its admin note codes.

**Outlook save race (word-add-in-r1; known limit).** `FileIfUnfiledAsync` files the communication on save (`ReconciledEmailFiling.cs:33-36`), and the D-140 chain is not re-run at commit (050a round 3, "noticed, not changed"). A save and a Submit Invoice in the same minute can commit the bytes under M while the email is filed under an ordinary S. That is a mismatch, not a leak. **Peer question:** re-run D-140 at commit, or accept it as known limit K5?

**Surfaces:** Submit Invoice is offered where users meet attachments: the communication UI, and possibly the Outlook add-in (word-add-in-r1). The UX comes from the prototype.

### 9.2 Processing and viewing

- **Extraction:** Content Understanding gets the bytes inline.
- **Viewer:** the bounding-box viewer (108) loads the PDF through the BFF download route.
- **Invoice index:** structured records (D-44's second half), unaffected.
- **Lost by design:** invoice PDFs leave the matter's Documents, the files index and AI over matter documents (owner: acceptable). Related work ranks the matter's documents, not the invoice PDF (`InvoiceRelatedWorkService.cs:78-81`), so it is unaffected.
- **Invoice lines:** a file belongs to the invoice, never to a line, so #2047 is unaffected.

## 10. Export files (O-7)

`sprk_billingexport` (023) gets its file as a Stored File (purpose Export file). The E-Billing Output Files container and key (D-55, D-67, 028) retire from code. The owner-created dev containers stay for the owner to remove (O-13; the project rule is never to delete them ourselves).

## 11. Search and AI

- **Nothing reads the file at evaluation:** clauses are copied at drafting.
- **A Procedures index** (blob indexer, trimmed by Procedure Read, internal only) is a follow-up if ontology-r1 needs retrieval. Question to ontology-r1.
- **Invoice files:** future work (O-8).

---

## 12. What changes in existing decisions and completed work

| Item | Change |
|---|---|
| D-44 | Invoice PDFs move to invoice files. The structured invoice index is unchanged. |
| D-55, D-67 | Containers and keys retire from code; the dev containers stay for the owner (O-13). |
| D-145 | Withdrawn. |
| D-146 | Deleting a core record removes its assignment and override rows. Its exceptions Procedure and record rules are retired (O-17). Reusable Procedures, rule sets and rate cards are untouched. |
| D-120 | Rate cards are no longer tied to a core record. |
| D-171 pt 3 / D-141 Q1 / S3, S4, S8, 087 | Business-unit filter dropped (O-12 Q8). uac-r2 to confirm nothing of theirs uses S8. |
| D-141 Q11 (rule set in the secure family) | Kept **only for exceptions Procedures** (section 5). |
| D-140 | Kept (the email route). |
| D-172 | Direct-association inheritance kept. Record scope on rule sets and rate cards replaced by assignments. The S6 backfill and ontology D-174 / D-147 become moot. |
| D-170 | Applies when a Procedure is retired or a record rule is switched off. A rule dropped by a newer version stays enabled (section 7.2). |
| `rule-set-scope-design.md` | Kept: §3.1 chain, §3.3 precedence (re-keyed). Replaced: §2 S1–S4, S6, S8; §4.1–4.5; §5.2–5.4. S9 creator person carries over. |
| 019 / 076 / 077 | Rule set gains `sprk_procedure` and loses `sprk_sourcedocument`. Policy version gains `sprk_procedureversion`. Version-chain logic is re-keyed per Procedure Version (section 7.2). Rule Review is reached from the Procedure. |
| 028 | Replaced by the blob containers. |
| 044 / 049 / 051 | The upload target becomes the file store. Processing reads bytes from it. |
| 050a | Becomes Submit Invoice (section 9.1). The re-file is retired. |
| 075 | Drafting is keyed per Procedure Version and reads the version's Stored File. |
| 078 | The card belongs to a Procedure Version and keeps its firm. |
| 102 (D-74 adjustment rule list) | Uses the resolver, not its own scope filter. |
| 108 | Loads through the BFF download route. |
| Dev data | Recreated, not migrated (O-13). |

## 13. Effect on open tasks 079–087 (sketch; re-planned after alignment)

| Task | Becomes |
|---|---|
| 080 schema | `sprk_procedure`, `sprk_procedureversion`, the assignment/override table, `sprk_storedfile`; `sprk_procedure` on rule set; `sprk_procedureversion` on policy version and rate card; field security (I-P2); Restrict (I-P4); creator person on new tables. Dropped: S1–S4, S6, S8. ontology-r1 reviews again. |
| 081 | Procedure service (versions, I-P1–I-P4, copy-forward of rules and cards); assignment and override service (pin, exclude, change rule, exceptions Procedure); secure-family rows for exceptions Procedures and assignment rows. |
| 082 | The resolver (section 7.1) and the re-keyed version selection (section 7.2). |
| 083 | Firm clause via the resolver, with the firm-facing name. |
| 084 | Waits for the prototype (O-14). The back end exposes the read models of section 6.6. |
| 085 | Rate-card tiers re-keyed. The scope column and backfill are dropped. |
| 086 | Mostly obsolete. What remains is the secure-family rows for invoice Stored Files. |
| 087 | Dropped. |
| New | File store service, control and orphan reconcile job; Stored File schema; provisioning hand-off; Submit Invoice; invoice submission and exports to files; firm invoice-file route; AddedFee and Discount rule types (if agreed). |

## 14. Coordination: peer review requested (2026-10-21)

**ontology-r1**

- the Procedure name and Area/Type values;
- one rule set per Procedure;
- policy version → Procedure Version, and the re-keyed version chain for billing (section 7.2): confirm that `SignalReevaluationJob` and `SignalClosureService` stay Ontology-only;
- the resolver levels and determinism rules (6.5, 7.1);
- rule-level overrides;
- the AddedFee and Discount types (do fee lines and due dates come out of extraction? check or adjust?);
- a full schema re-review (replaces the 080 they reviewed);
- whether they need a Procedures index;
- **overlap:** ontology's matter-scoped rules (`sprk_policy.sprk_matter`, 102b / D-165). Should they move to Procedures and assignments, or stay?

**uac-r2**

- org-level Read for reusable Procedures;
- the **exceptions Procedure as a secure child** (section 5);
- assignment rows and Stored Files in the secure family;
- **Submit Invoice:** the five steps, the lost resolver backstop, and K4 now exposing bytes until the post-commit check (section 9.1). Accept or reject;
- the download route's rights check;
- the firm invoice-file route and Firm-visible flag (with SPA-r3);
- BU filter / S8 dropped;
- which of #2159 / #2161 / #2151 / T7 ebilling still needs.

**word-add-in-r1**

- Submit Invoice on attachments and the Outlook add-in;
- the save race (K5 or re-run D-140 at commit);
- invoices no longer becoming Documents;
- interaction with `FileIfUnfiledAsync`, D7 Restricted and #2171.

**SPA-r3 and customer-provisioning** follow after alignment.

## 15. Questions still open

- **Owner:** the exceptions Procedure as a secure child of its record (section 5); the default firm column for the record's list (section 6.1).
- **Peers:** as listed in section 14.

## 16. Design review record

**Fable review of v3 (`92fe0be17e`), 2026-10-21, owner-approved.** It found 13 must-fix items; all are folded into v4.

| # | Finding | Fix |
|---|---|---|
| 1 | The version chain | Section 7.2 |
| 2 | "Retired by a version" | Sections 7.2, 3.1 I-P4 |
| 3 | The Submit Invoice copy guard | Section 9.1 |
| 4 | The exceptions Procedure's access | Section 5 |
| 5 | Record-rule drift | Section 6.3 |
| 6 | Fail-closed reads, gaps, snapshot content | Sections 7.1, 7.4 |
| 7 | Re-evaluation on publish | Section 8.2 |
| 8 | Blob scanning, WORM, SAS, orphans, ADR-015 | Sections 4.2, 4.3 |
| 9 | §11 answers | Section 3.3 |
| 10 | Rate-card firm | Section 3.1 I-P3 |
| 11 | Drafting key | Section 7.2 |
| 12 | Delete ownership | Section 3.1 I-P4 |
| 13 | Overlapping sources | Section 6.5 |

Its seven peer questions (firm file route; Outlook race; firm columns; AddedFee naming and facts; task 102; firm-facing name; "All records") are folded into sections 4.3, 9.1, 6.1, 6.3, 12, 3.2 and 3.2.
