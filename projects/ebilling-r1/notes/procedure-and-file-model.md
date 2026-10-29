# Procedures and files — revised approach

> **Status**: **FINAL — v5.9, finalized by the owner 2026-10-21 (decision EB-D-148).** All owner questions are answered (section 15, owner 2026-10-21): no automatic re-check in all three cases; switching a rule Off still closes its open findings, behind a confirmation warning; an Override can release a gap hold; export files are internal-only; the gap reading stands; the neutral banner text. uac-r2: ACCEPT (v5.8); ontology-r1: PASS (v5.1) with later items folded in. v5.8 applies uac-r2's ACCEPT-WITH-CHANGES on v5.6/v5.7 (reviews §15): the marker lives on the .eml, the marked-.eml limits, the caller's-own candidate rule, retryable versus permanent refusals, and stale text removed. v5.7 records the owner's picks on uac-r2's F1-a (option i: the `from-email` .eml is saved own-Restricted with a submission marker that (a′) skips) and F1-c (refuse, retryable), both given in this session and in uac-r2's round 118, and word-add-in-r1's correction to how the .eml is resolved. v5.6 applies uac-r2's review of v5.5 (`f66eec5e25`, CHANGES-NEEDED; reviews §14): the `from-email` route's filters, resolving the .eml from its communication, the job's byte read, correction guards and (a′)'s rule 4 on the .eml. Two owner decisions are open (section 15: the exposure of the add-in .eml, F1-a; an email that can't be identified, F1-c). v5.5 records the owner's Submit Invoice process for the Outlook add-in (I, L; relayed by word-add-in-r1 and checked in its notes) and defines the email-only save contract (section 9.1). v5.4 applies uac-r2's re-check of v5.3 (`cbee545d41`): step 0 is defined once (section 5.3), "false or blank" means internal, and its scope is every `/api/finance` invoice and submission route plus `/api/finance/rulesets/*`. v5.3 applies uac-r2's re-check of v5.2 (`b5872265be`; C1–C7: snapshot states, app-only comparison, cost bound, one route, step 0 on Finance invoice reads and on every Procedure/override read). v5.2 applied uac-r2's check of v5.1 (`ae690ec8a4`; the banner is computed at read time, N1–N9). v5 (`70fc2cc616`) re-checked by ontology-r1 (findings, nothing blocking), uac-r2 (accept with changes) and word-add-in-r1 (§9.1 matches; three gaps); all folded in here, with the owner's re-check answer (ontology D-175). Waiting on the owner's answers in section 15, then the owner finalizes.
> - v5 folded in the three peer reviews of v4 (`c260f9de11`) and the owner's answers A–R; v5.1 folds in the v5 re-checks (`procedure-model-reviews.md` §8, §9); v5.2 folds in uac-r2's check of v5.1 (§11). Section 0 lists what changed.
> - Nothing is implemented. No schema, task or decision row has changed yet.
> - Once the owner answers section 15 and finalizes, this note becomes decision **EB-D-148** plus revised POMLs for 079–087 and new tasks (section 13). The UI/UX is prototyped separately (O-14); the back end goes first.
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
| 4 | Removing an override, or retiring a Procedure, **re-checks** the affected pending and In Review invoices. *Revised in v5.1: no automatic re-check; a banner (8.2).* | Owner D, D2 |
| 5 | A gap (no version in force) **holds** the invoice with the warning "No procedure associated with this invoice". | Owner E; ontology 4 |
| 6 | `sprk_createdbyperson` on the four new tables, replacing "uploaded by". | Owner F (ontology D-175 (4)) |
| 7 | Ontology's matter rules stay as built; Ontology-area Procedures are document-only in R1. | Owner G; ontology 7 |
| 8 | Type "Internal procedure" → **"Internal Standard"**; Area reuses `sprk_policydomain`. | Owner H; ontology 1 |
| 9 | Submit Invoice: email filed first (A) and the invoice file follows its source through T7 (B) *(superseded in v5.1 by the caller's own Read on the source)*; internal-only, Restricted and Private-thread sources are allowed with their isolation carried forward. The add-in will offer Submit Invoice; the process is still to be discussed. | Owner I, J, K; word-add-in-r1 1–5 |
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

### What changed in v5.1

| # | Change | Source |
|---|---|---|
| 1 | **No automatic re-check.** Removing an override (D), retiring a Procedure (D2) and the end of a Procedure Version (superseded or withdrawn) re-check nothing; affected open invoices show the banner "A new procedure that applies to this invoice has been published" (8.2). Decided invoices keep their snapshot. ebilling-r2 owns the explicit re-check, which clears the banner. The single-version retire question is answered: no re-check, banner only. | Owner, ontology D-175 |
| 2 | **Derived rows of an isolated source:** read-time (a′) — `sprk_invoice` gets an `ExternalMarker` predicate (`ExternalDocumentNarrowing`, then 191's `ExternalVisibility`) that hides the invoice and its secure child rows from every non-internal caller while its source is isolated; nothing is stored; it heals when the source is lowered. Every "invoice's own Access Permission" reference is gone. | uac-r2 F1-1, F4-1 |
| 3 | The internal copy rule is **the caller's own Read on the source** (gate variant), replacing "apply T7". | uac-r2 F4-2 |
| 4 | **Deleted source:** the Stored File's source lookups are Restrict; single-document delete answers 409; invoices block the core-record delete (D-120). "Strictest isolation last read" is gone. | uac-r2 answer B |
| 5 | `sprk_storedfile` is **Deep** and on the NFR-05 list; Upload refuses `sprk_isexternal` callers (step 0). | uac-r2 F1-2, F1-3 |
| 6 | Census: an `ExternalMarker` per table; lineage edges and the rewritten census guard with controls; named hard-deny / Delete / unlink-only lists; RemoveLink across the record-scoped path. | uac-r2 F4-3, F4-5; ontology F2-a, F3-e |
| 7 | Record scope uses `sprk_ruleset.sprk_scope` = **Record (100000002)** or **Firm record (100000003)**; no `sprk_isexceptions`. Override rows carry a firm, so a change made for firm F touches only F's invoices. A record scope with no lookup applies to nothing. | ontology §15 answer 1, F2-b, F2-c |
| 8 | Gap reading (Draft-only not Active; a Procedure applies from its first version's start; an end with no successor holds only work dated after it) — accepted by ontology; confirmed by the owner in v5.9. | ontology F2-d, D-175 |
| 9 | Submit Invoice: file id = f(submission id, source version); SHA check inside the flip; the undo also clears the submission's committed columns; step A's filing survives an undo (admin note); the no-source-document (flagged attachment) case. | uac-r2 F1-5, F2-1, F4-4, K-4; word-add-in-r1 1–3 |
| 10 | Firm route: a flagged-external systemuser is matched to the firm through their linked contact's organisation; no link → no invoices (owner, uac-r2 round 116). | uac-r2 F1-4 |
| 11 | Retire, publish, date changes and the banner-setting write: BFF-only, Write on the Procedure plus a billing-admin privilege, writes through `IRecordOwnershipResolver`, never a count or list of invoices the caller can't read. *(Banner write and the resolver wording superseded in v5.2: the banner is stored nowhere.)* | uac-r2 |
| 12 | Global option sets from 100000000; the published-marker column named; ratchet as a baseline-plus-controls ArchTest; ExpenseRule split restated; AddedFee forms refused by the validator until extraction supports them. | ontology F3-a–c, §15 answer 3 |
| 13 | K-1 (191 window), K-2 (pin test on the billing lookups), K-3: accepted as a known limit (owner round 117); network block at prod hardening; container-scoped role in R1. | uac-r2 K-1–K-3 |
| 14 | "this matter" → "this record" in firm-facing text; 080 uses the shared core-record list; dev check of the assigned-firm columns; Type "Internal Standard" confirmed. | ontology F4-a, matter-only items, D-175 |

### What changed in v5.2

| # | Change | Source |
|---|---|---|
| 1 | **The banner is computed at read time and stored nowhere.** On the invoice's own read, for internal callers only, the BFF re-runs the Procedure resolution for the open invoice and compares it with the evaluation snapshot; any difference shows the banner. This covers every trigger: override removed or added, Procedure retired, version ended, retired or newly published. No marker column, no field security, no banner write, no write scope, nothing to make atomic. | uac-r2 N2 (option a), N1, N4, N8 |
| 2 | The banner is never computed for or returned to CIAM contacts or flagged-external systemusers; it is out of the firm findings DTO and the SPA invoice projection, and in R3's negative test. | uac-r2 N3 |
| 3 | The override service and the Procedure service refuse `sprk_isexternal` callers first (step 0); a fault reading the flag refuses. | uac-r2 N9 |
| 4 | Billing-admin stays for retire, publish and date changes only; the banner-write and "goes through `IRecordOwnershipResolver`" text is removed. | uac-r2 N4 |
| 5 | File id = f(submission id, source version), or f(submission id, the attachment's identity) when there is no source document, else a fixed 0, so a replay re-stages under the same id. | uac-r2 N6 |
| 6 | §9.1 known-limits wording: (a′) reads the source's state "but not rule 1 (which roots the source is filed under)". | uac-r2 N5 |
| 7 | §15(b) widened to switching off a record rule on override removal, not only Procedure retire; banner text question added. | uac-r2 N7, N8 |

### What changed in v5.3

| # | Change | Source |
|---|---|---|
| 1 | No snapshot, or a pre-v5 snapshot, shows a distinct **"Not yet checked"** state, never "no change". Invoices judged only by `BILL-SYSTEM-SUBMISSION` are exempt (no banner). | uac-r2 C1 |
| 2 | The comparison runs **app-only**, like the evaluation, and returns one banner state only (no diff, names or ids). New K limit: one bit about a Secure distance-1 parent. | uac-r2 C2 |
| 3 | Cost bound: one resolution per invoice, each row read once, work dates in memory; a test that the query count does not depend on line count; once per page load on `/api/finance/invoices/{id}/review` only; 429 or timeout → "could not be checked"; the fault log carries ids and counts only. | uac-r2 C3 |
| 4 | Only that route computes the banner, and only when `sprk_isexternal` reads false or blank. **Step 0 on the internal Finance invoice reads** (`/review`, `/context`, `/api/finance/invoice-submissions/{id}`): refuse flagged-external systemusers; a fault refuses. *(Superseded in v5.4: the definition, placement and scope are in section 5.3, and it is not a change to master behaviour.)* | uac-r2 C4; owner M and round 113 |
| 5 | Step 0 covers **every** Procedure and override service operation, reads included (the 6.6 read models). | uac-r2 C5 |
| 6 | K-3 wording everywhere: accepted as a known limit (owner round 117). Round-116 wording: (a′)'s effect accepted (Restricted and internal-only); F1-4 confirmed. | uac-r2 C6, C7 |
| 7 | K: a non-atomic publish can briefly show a false banner, or two Active versions that fault into "could not be checked". | uac-r2 probes |

### What changed in v5.4

| # | Change | Source |
|---|---|---|
| 1 | **Step 0 is defined once (section 5.3):** the caller's systemuser from `oid` (no row → refuse); `sprk_isexternal` true → refuse; **false or blank → internal**; a fault reading the flag → refuse. Every "definitely false" in this note now reads "false or blank". | uac-r2 v5.3 re-check; round 67 |
| 2 | **Where:** a check inside `FinanceAuthorizationFilter`, before any record read, set by one flag at route-group level, answering with the route's own `Deny()` (403 on `/review` and `/context`; `UniformNotFound` 404 on `GET /invoice-submissions/{id}`, `GET /submissions/{id}/blocked`, `POST /matters/{id}/recalculate` and `POST /projects/{id}/recalculate`). Not in `InvoiceSubmissionService`, which the external submit route shares. | uac-r2 v5.3 re-check |
| 3 | **Scope by group rule (owner round 116 item 2):** every `/api/finance` route that reads or writes invoices or submissions, including `GET /api/finance/submissions/{submissionId}/blocked`, plus `/api/finance/rulesets/*` (`RuleSetService`). Out of scope: `/invoices/search`, `/matters/{id}/summary`. `/invoice-submissions/{id}` is in scope by the group rule, not because it exposes Procedure content (it returns firm-visible facts only). | uac-r2 v5.3 re-check |
| 4 | **Not a change to master behaviour:** none of these routes is on master yet, and no client depends on them for flagged-external callers. It is a change on our branch, landing with #1911 (sections 12 and 13 corrected). | uac-r2 v5.3 re-check |
| 5 | The 081 row says the banner comparison runs on `/review` only. | uac-r2 v5.3 re-check |

---

### What changed in v5.5

| # | Change | Source |
|---|---|---|
| 1 | **The add-in process is decided.** Each attachment is marked **Invoice** (exactly one; pre-selected when there is exactly one PDF), **Supporting** (the default) or **Don't save**. The server never guesses and refuses a submission without exactly one Invoice. If extraction finds the marked file is not an invoice, the submission fails with a clear message. | Owner I, L (word-add-in-r1, 2026-10-21) |
| 2 | **A separate Invoice module** in the add-in (a "Submit Invoice" ribbon button, task-pane view like Add To Do), apart from Save. New invoice: the core record is required (Matter, Project or Work Assignment; pre-filled when the email is filed). Existing invoice: (a) add supporting files, or (b) submit a corrected invoice. | Owner I, L |
| 3 | **A corrected invoice is a resubmission** (D-37, D-43): the old invoice closes as Superseded, and the new one links to it ("resubmission of", `{invoice}.{n}`). No in-place PDF replacement. A correction is refused once the invoice is Approved, Sent to AP or Paid (a credit or adjustment instead). | Owner I, L |
| 4 | **Removed from Save:** Invoice as a save target (`OfficeDocumentPersistence.cs:47`) and the blank "+New invoice" quick-create (`QuickCreateRequest.cs:180`). Invoices are created only by submission (FR-17, FR-20). | Owner L |
| 5 | **The email is always saved** as its .eml Document, filed to the core record, never an invoice file. Its attachments are not created as child Documents; only the marked ones go to the invoice's files. So the **.eml Document is the source document** of every add-in copy, and v5.4's "no source document" branch is retired. | Owner L; contract in 9.1 |
| 6 | Core records Matter, Project, Work Assignment in R1; the add-in takes the list from ebilling, so Agreement appears when R2 adds it. LEDES is out of scope. | Owner; O-31 |

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
| O-21 (D, D2) | *Revised 2026-10-21 (ontology D-175):* "the recheck needs to be an explicit decision by a user, not automatic". Removing an override, retiring a Procedure, or the end of a Procedure Version (superseded or withdrawn) re-checks nothing; affected open invoices show a banner (8.2). The explicit re-check is ebilling-r2's. Decided invoices keep their snapshot. |
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
  - **Retiring a Procedure** is the normal end of life. The retire operation is a named BFF reconcile: it sets the Procedure Retired, switches each of its policies Off (so D-170's sweep closes their open Signals as PolicyRetired with no new reads, `SignalClosureService.cs:164-177`), and affected open invoices then show the banner, computed at read time (8.2). It re-checks nothing (O-21, revised). Decided invoices keep their snapshot. **Owner (2026-10-21): the Off switch still closes them through D-170**, behind a confirmation warning (section 15, answer 2).
  - **Retiring one Procedure Version** (withdrawn, not superseded) means it is never selected again. Work dated in its window then has no version in force, which is a gap (section 7.1) for invoices checked from then on. Its policies stay enabled, because other versions use them. **Answered (owner, ontology D-175): no re-check, banner only**, for a superseded or a withdrawn version alike; open invoices whose snapshot cites it show the banner, computed at read time (8.2).
  - **Who may retire, publish or change dates (uac-r2).** BFF-only writes (Status and dates are field-secured). Step 0 refuses every `sprk_isexternal` caller, and a fault reading the flag refuses (uac-r2 N9). Then Write on the Procedure **plus a billing-admin privilege** (D-76 style, `CallerHoldsPrivilegeAsync`), because the change reaches invoices on any record, Secure ones included. The response never lists invoices; its only count is the one in section 15 answer 2 (the confirmation preview and the result, both counted as the caller: open invoices the caller can Read).  No banner is written (8.2).
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

**Option sets (ontology F3-a).** Type, Applies to, Status (Procedure), Status (Procedure Version) and Purpose (Stored File) are **global** choices whose values start at 100000000.

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

**Record-scoped rule set** (extension of `sprk_ruleset`, not a new table): typed record lookups on `sprk_ruleset` and two new values on the existing `sprk_ruleset.sprk_scope`: **Record (100000002)** and **Firm record (100000003**, with the existing `sprk_firm`), as D-171 and the 080 notes planned. No new boolean (no `sprk_isexceptions`). Cost of doing nothing: a changed rule for one Secure record would have nowhere to live that its record's isolation covers (uac-r2 F1-a).

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
| Source document, source communication | Set on a Submit Invoice copy (word-add-in-r1 3b). **Restrict** (uac-r2 answer B; section 5.2). The copy rule reads them on every access (section 9.1, B). The source document is null for a flagged mailbox attachment (9.1). |
| Source attachment identity | For a flagged mailbox attachment: the RFC Message-ID resolved to the communication, plus the attachment id. Never the Exchange item id (word-add-in-r1 3). |
| Source version, SHA at staging | For the Submit Invoice guard (uac-r2 F2-c) and the flip's SHA check (F2-1). The row id is f(submission id, source version), or f(submission id, the attachment's identity) when there is no source document, else a fixed 0 (F4-4, N6). |
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
- **Provisioning (uac-r2 Q5, K-3):** the account's network rule is `defaultAction: 'Allow'` with no subnet (`storage-account.bicep:55-66`) and the role is assigned at account scope (`:171-179`). **R1 assigns the role at container scope** (cheap; uac-r2 K-3). Network Deny is **a known limit accepted by the owner** (uac-r2 round 117, 2026-10-21): invoice files are reachable only through the invoice record and its gated file route; a network block (private endpoint, or deny on public access) is due at prod hardening; re-check the decision if an invoice-file search is ever added. Also to verify: the account is shared; "dev uses the existing account" is unverified; `customer.bicep:313` already passes the principal (v4's claim was stale); reading the scan tag may need more than Storage Blob Data Contributor. Containers and the role go into environment setup, with a hand-off to customer-provisioning.
- **Retention and deletion:**
  - A file lives as long as its parent row. Deleting through the BFF deletes the files with a job (ADR-004).
  - **Orphan reconcile** (scheduled, ADR-036, ADR-002 WP-5), for parents deleted outside the BFF (uac-r2 F2-a): "gone" means a successful not-found read, never a fault; the grace period is at least the submission job's maximum retry horizon; non-terminal submissions are skipped.
  - ADR-015 doesn't govern these stores: they are business records, not AI work history.

### 4.3 Upload and download

- **Upload:** (0) **refuse every `sprk_isexternal` caller**, for every parent kind; a fault reading the flag refuses (uac-r2 F1-3) → check Write on the parent → stream to a blob keyed by the new file id → write the Stored File row → if the row write fails, delete the blob (compensation job, orphan backstop).
- **Download (internal route):**
  1. **Refuse every caller with `sprk_isexternal = true`**, for every file kind (O-27; uac-r2 F1-e). Flagged-external users get invoice files only through the firm route (section 9.3). A fault reading the flag refuses.
  2. Check Read on the parent with the caller's own rights, the gate variant (`GetCallerRightsForRecordsAsync`, `CallerRecordAccessProbe.cs`; uac-r2 Q5).
  3. For a Submit Invoice copy, apply **the copy rule**: the caller's own Read on the source document, gate variant (section 9.1, B; uac-r2 F4-2).
  4. Check Status = Servable, the scan tag, and that the path's parent matches the row.
  5. Write the download audit event (O-29), then stream.
- **Auditing (O-29):** Dataverse auditing on `sprk_procedureassignment` and `sprk_storedfile`; a download audit event per served file on both routes.

---

## 5. Access control (O-6)

### 5.1 Rows

| Row | Owner and visibility | Notes |
|---|---|---|
| Procedure | Owned by the uploader's business unit; internal roles get org-level Read. Internal only. | Not a secure child (O-6, owner N). Holds only reusable documents now, so org-level Read leaks nothing record-specific (uac-r2 F1-a resolved by A). |
| Procedure Version | Follows the Procedure (parental). | |
| Stored File (all purposes) | `sprk_storedfile` is one table with one depth: **Deep**, on the NFR-05 list (uac-r2 F1-2). Procedure and export file rows are listed and served only by the BFF after the parent check; no subgrid. | |
| Rule set, policy, policy version of a Procedure | Unchanged ownership. | |
| **Record-scoped rule set** and its policies and policy versions | **Secure child of its record**, at Deep, as 081 planned (D-141 Q11): lineage (`sprk_ruleset` → core record, `sprk_policy` → `sprk_ruleset`, `sprk_policyversion` → `sprk_policy`), role row, census, creator person. Record-rule policy creates route through `IRecordOwnershipResolver`. | Holds one record's rule changes, including clause text. |
| Assignment / override | **Secure child of the record it names.** | Its existence on a Secure record is the record's information. |
| Stored File (invoice) | **Secure child of its invoice**; a leaf. Owner from `ForParents(core)` inside the Submit Invoice transaction. | Access follows the invoice; the copy rule narrows a copy further; the invoice's (a′) predicate hides it from external callers while its source is isolated (9.1). |
| Stored File (export) | **Not** a secure child (D-85 / #1974); follows the export's ownership (uac-r2 F4-c). | |

**Writes are checked as the caller (uac-r2 F1-c).** Step 0: the assignment/override service and the Procedure service refuse every `sprk_isexternal` caller, and a fault reading the flag refuses (uac-r2 N9). Then assignment, override and change-rule writes check Write/AppendTo on the record (the gate variant, the same 404), Read on the Procedure or rule, and the billing privilege. The row is created as the application, owned by the resolver's team, with creator person = the caller. Creates route through `IRecordOwnershipResolver`.

**Census waiver (ontology blocking O1).** `RecordOwnerAssignmentCensusTests.cs:128-140` waives rule sets on the ground that "a billing rule is never filed under a matter". Record-scoped rule sets break that. The waiver and its guard are rewritten: record-scoped rule sets get lineage with uac-r2 and route through `IRecordOwnershipResolver`; Procedure rule sets keep the waiver.

**Depth list.** 081's NFR-05 depth list is restored and extended to every new secure child table and to `sprk_storedfile` at Deep (uac-r2 F1-a, F1-2).

**CIAM.** The new tables stay out of `ExternalModuleRegistry` (uac-r2 Q1). **Flagged-external systemusers** reach no internal-only record (owner reversal of #2179 (c)). uac-r2's Dataverse-side enforcement (task 191) covers `sprk_procedure`, `sprk_procedureversion`, Procedure Stored Files and the rest of section 5.2; the BFF refusals in 4.3 are the route-level half. **K-1:** until 191 deploys, org-level Read on procedure and procedure version would reach flagged-external users in the MDA, so the grant is kept out of every role those users hold, and the External Collaborator role has no privilege on either table; where that can't be guaranteed it is a recorded 191 window.

### 5.2 Census for uac-r2 task 191

"Root column per core type" is the column the row hangs from for `sprk_matter` / `sprk_project` / `sprk_workassignment`.

| Table / column | Parent | Root column per core type | Secure child | `ExternalMarker` (191) | Creator person |
|---|---|---|---|---|---|
| `sprk_procedure` | none | none (org-level Read) | No | **Never**; no privilege in the External Collaborator role | Yes |
| `sprk_procedureversion` | `sprk_procedure` (parental) | none | No | **Never**; no privilege in the External Collaborator role | Yes |
| `sprk_procedureassignment` | the record | typed lookups `sprk_matter` / `sprk_project` / `sprk_workassignment` (one per core type in the shared list), exactly one set | Yes: lineage on the record lookups; `CitationLookups` = procedure, pinned version, excluded policy; role JSON with a live probe; census; delete planner | **Never** | Yes |
| `sprk_ruleset` (record-scoped) | the record | typed lookups per core type, exactly one set, **RemoveLink**; `sprk_scope` = Record (100000002) or Firm record (100000003, with `sprk_firm`); a no-plugin sweep retires sets whose record is gone (O-17), cleanup only | Yes, at Deep | **Never** (firms see clauses only through the findings DTO) | Yes (S9) |
| `sprk_policy`, `sprk_policyversion` of a record-scoped set | `sprk_ruleset` → `sprk_policy` | via the set | Yes (edges `sprk_policy` → `sprk_ruleset`, `sprk_policyversion` → `sprk_policy`) | **Never** | Yes |
| `sprk_storedfile` (invoice purposes) | `sprk_invoice` | via the invoice: `sprk_matter` / `sprk_project` / the work-assignment regarding stamp (`InvoiceFamilyCoreRecordGuard.CoreRecordLookups`) | Yes: lineage on `sprk_invoice` only; role, census, person entries; a leaf | **Follows the invoice**; Firm-visible = No hides a copy | Yes (K6) |
| `sprk_storedfile` (export) | `sprk_billingexport` | none | No (F4-c) | **Never** until section 15's export question is answered | Yes |
| `sprk_storedfile` (Procedure source) | `sprk_procedureversion` | none | No | **Never** | Yes |
| `sprk_invoice` | core record | existing | existing | **The (a′) predicate** (9.1) | existing |
| `sprk_storedfile.sprk_sourcedocument`, `.sprk_sourcecommunication` | — | — | Citation lookups, **Restrict** (answer B), not access-conferring; read by the copy rule | — | — |
| `sprk_ruleset.sprk_procedure`, `sprk_policyversion.sprk_procedureversion`, `sprk_ratecard.sprk_procedureversion` | — | — | Citation lookups (Restrict, on the Procedure side only), not access-conferring | — | — |
| `sprk_policyversion` "based on" (record rule → base version) | — | — | Citation lookup, **RemoveLink** (ontology F2-a) | — | — |
| `sprk_billingfirm`, `sprk_billingresponsible` on every core table | — | — | **Not access-conferring** (below) | — | — |

**`sprk_billingfirm` and `sprk_billingresponsible` confer no access.** A lookup to `sprk_organization` on a core record can confer access through task 043's org-expansion term if it is in the access registry. So:
- neither column is added to the access registry;
- no grant or share path reads them;
- giving a firm access through either column would be a separate owner decision, through uac-r2's registry review;
- **K-2 pin test:** membership discovery is opt-out (`MembershipFieldDiscoveryService`; `sprk_organization` is an identity table, `MembershipOptions.cs:257`), so `MembershipReconciliationJob` would write junction rows for both columns. A test pins that the registry has no `billing*` column and that `AccessConferringOnly` confers nothing through them; optionally per-entity `ExcludedFields`.

**Lineage edges (agreed with uac-r2; ontology F3-e):** `sprk_procedureassignment` → core record; `sprk_ruleset` (record-scoped) → core record; `sprk_policy` → `sprk_ruleset`; `sprk_policyversion` → `sprk_policy`; `sprk_storedfile` → `sprk_invoice`. `sprk_procedure`, `sprk_procedureversion`, Procedure-source and export Stored Files have no lineage.

**Census guard, rewritten (ontology O1, F3-e).** `RecordOwnerAssignmentCensusTests` stops waiving all rule sets: it requires every record-scoped `sprk_ruleset` writer (and its `sprk_policy` / `sprk_policyversion` creates) to route through `IRecordOwnershipResolver`, and keeps the waiver only for Procedure rule sets. **Must-fire:** a record-scoped rule-set or record-rule policy create that bypasses the resolver fails the guard. **Must-not-fire:** a Procedure rule-set create, and a Procedure-scoped policy create, pass.

**Delete contract (uac-r2 F4-b, F4-5; ontology F2-a, F2-b).** uac-r2 updates `core-record-delete-design.md` with these lists:

| List | Tables |
|---|---|
| **Hard-deny** (never deleted by a core-record delete) | `sprk_procedure`, `sprk_procedureversion`, `sprk_storedfile` |
| **Delete** with the record (**the planner**; Dataverse allows one parental relationship per table, so the core lookups are RemoveLink, 2026-10-21) | `sprk_procedureassignment` |
| **Unlink-only** (RemoveLink; kept as secure child rows, retired by the sweep) | record-scoped `sprk_ruleset` and its `sprk_policy` / `sprk_policyversion` |

- **No Restrict is reachable from a core-record delete** on the record-scoped path: the set's record lookups and the record rule's "based on" link are RemoveLink (D-172, #2132). Assignment-row lookups: the record lookups are **RemoveLink** (080 dev run 2026-10-21 hit Dataverse's one-parental-relationship limit; uac-r2 ACCEPT, ontology PASS). The core-record delete planner (117) deletes the rows in the same delete and counts them in the preview. The O-17 sweep (117) removes orphans, and is the only cleanup after a native model-driven delete. **An orphan row (no core lookup) applies to nothing**: 082 ignores it, and it never reads as "applies to all". Known limit: until the sweep runs, an orphan keeps its owner and its reason text. `sprk_excludedpolicy` is the table's one parental relationship (Delete Cascade, the rest NoCascade); a nulled rule must never read as a Procedure-level Exclude; the pinned-version lookup is Restrict, which only stops deleting a pinned Procedure Version, never a core record.
- **A record scope with no lookup applies to nothing** (ontology F2-b): the loader requires the scope value **and** a non-null record lookup, so a set unlinked by a delete can never read as firm-wide. The sweep is cleanup only.
- **The deliberate Restrict on the Stored File's source lookups** blocks deleting a source *document*, not a core record. It fits because a core record whose invoices exist can't be deleted anyway (D-120). For a document delete: the BFF single-document delete answers **409** when a Stored File references the document (pattern `document_has_child_documents`), and S's delete preview shows a block and never unlinks. A communication delete only deactivates it, so the copy rule still reads it (uac-r2 answer B).
- The retire writer is a named reconcile (I-P5).

---

### 5.3 Step 0: refusing flagged-external callers on internal routes

One definition, used by every "step 0" in this note (Upload, the Procedure and override services, the Finance routes):

1. Resolve the caller's systemuser from the token's `oid`. **No row → refuse.**
2. `sprk_isexternal` **true → refuse.** **False or blank → internal** (the platform rule, uac-r2 round 67; `RecordContainerResolver.cs:2400`, `SpeContainerMembershipSync.cs:235`).
3. **A fault reading the flag → refuse.**

CIAM contacts never reach these routes: the workforce scheme rejects their tokens with 401 (`AuthorizationModule.cs:183-200`).

**Finance routes.** Step 0 is a check inside `FinanceAuthorizationFilter`, run **before any record read**, switched on by one flag at **route-group level**, and it answers with the route's own `Deny()`: 403 on `/review` and `/context`; `UniformNotFound` (404) on `GET /invoice-submissions/{id}`, `GET /submissions/{id}/blocked`, `POST /matters/{id}/recalculate` and `POST /projects/{id}/recalculate`, so the answer is the same whether or not the id exists. It is **not** placed in `InvoiceSubmissionService`, which the external submit route shares.

**Scope (group rule, owner round 116 item 2: flagged-external users reach invoices only through the firm route).** Every `/api/finance` route that reads or writes invoices or submissions:
- `/api/finance/invoices/{id}/review` (`InvoiceLineReviewEndpoints.cs:39`) and `/context` (`InvoiceContextEndpoints.cs:28`);
- `/api/finance/invoice-submissions/{id}` (`InvoiceSubmissionEndpoints.cs:74`). It returns firm-visible facts only (`InvoiceSubmissionContracts.cs:184`), so it is in scope by the group rule, not because it exposes Procedure content;
- `GET /api/finance/submissions/{submissionId}/blocked` (`SubmissionOverrideEndpoints.cs:40`). It returns `BlockedFindingView` (policy code, version id, clause) plus the override stamp with the reviewer's reason and id, from the snapshot that v5 extends with the Procedure Version and the override basis;
- `/lines/{lineId}/decision`, `/lines/decisions`, `/accept-clean`, `/lines/{lineId}/related-work`;
- `/submissions/{id}/override` and `/return`;
- the internal submit routes;
- plus `/api/finance/rulesets/*` (`RuleSetEndpoints.cs`, `RuleSetService`): record-scoped rule sets hold override content, and these routes are gated only by billing-admin today.

~~Out of scope: `/api/finance/invoices/search` and `/matters/{id}/summary` (no Procedure or override content).~~ **Superseded by the owner, 2026-10-21 (re-plan Q3; section 15): step 0 applies to EVERY `/api/finance` route** (invoices, submissions, invoice-review incl. reject, recalculate, matter/project rollups, rate cards, code-set mappings, rulesets, related work), so a future route cannot be missed. Task 099 sets the flag on every `/api/finance` route group; `FinanceStepZeroCoverageGuardTests` (ArchTests, booted route table, no exemptions) fails a route without it.

**Not a change to master behaviour.** None of these routes is on master yet. No client calls `/review`, `/context` or `/invoice-submissions/{id}`; RuleReview calls only `rulesets`; the external SPA uses `/api/v1/external/*` only, and the external `GET /invoice-submissions/{id}` answers from its own scope with no internal call. The change lands on our branch with #1911.

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
| **Exclude rule** | An override row: rule → record, mode Excluded, reason, and an optional **Firm**. | The rule does not run for this record (for that firm's invoices only, when Firm is set). |
| **Change rule for this record** | (1) An Exclude row for the base rule. (2) A **record rule**: a copy of the base rule's current parameters, edited and published in the record's **record-scoped rule set** (created on first use). It stores **based on** = the base rule's policy version (RemoveLink) and takes the base rule's rank. **The change is made for all firms or for one firm** (ontology F2-c): for one firm F, the Exclude row carries Firm = F and the record rule goes into a set with `sprk_scope` = Firm record (100000003) and `sprk_firm` = F; for all firms, no Firm and scope Record (100000002). Both writes are one BFF operation. | The record rule runs **in the base rule's slot**: same level, and only on the invoices its firm covers. The Exclude removes only the base rule, only for that firm (ontology 5). A firm-named base rule can only be changed for its own firm (O-20). |
| **Remove an override** | The override row is deleted (its content survives in past snapshots). A record rule is switched off. | The base rule runs again for invoices checked from then on; affected open invoices show the banner and are not re-checked (O-21 revised; 8.2). Removing a changed-rule override closes that rule's open findings on In Review invoices (D-170), behind the confirmation warning (section 15, answer 2). |

- **Why copy, edit and exclude:** the record rule is an ordinary rule (typed, validated D-27, immutable once published, cited by Signals, reviewable), and checklist types add up across sources, so the base rule must be excluded.
- **Taking the base rule's slot** means a record rule never applies to other firms' invoices and never displaces a rule the user didn't touch. v4's Replace-type known limit is gone.
- **Record-rule codes** start a new ontology D-13 key.
- **Drift:** when the base rule gets a new version, or is retired, the record rule keeps running. The read model computes **stale** (based on ≠ the base rule's current version) and **base retired**; nothing is stamped (if ever stamped, only on change, D-61).

**Rule types the examples need (ontology 6):**

- **AddedFee (O-16).** Fee kind (late fee, administrative fee, interest …) and an allowance: not allowed, a cap (amount or percent), or allowed after N days. Option value 100000015 (`RuleType.cs:93`), mapped in `RuleDomains.DomainOf` (an unmapped value throws), pinning test extended, PR to ontology-r1. **The validator refuses "not allowed" and "after N days"** until extraction returns the fee kind per line and the due date, with a test on each refusal; the cap form ships now. **Precedence (D-39, owner 2026-10-21): the replacement key for AddedFee is the rule type plus the fee kind.** A firm (later, a record) cap on late fees replaces only the broader late-fee cap; the broader administrative-fee cap still applies. Every other rule type, Discount included, is replaced by rule type alone. Fee kinds are the closed vocabulary (late fee, administrative fee, interest, other), matched exactly; a kind outside it is refused at save and at the gate, so it never reaches precedence. For the kind `other` the key also carries the exact fee-code set (case-insensitive, order-independent, as the handler matches codes): a firm courier cap replaces only the client default's courier cap, never its postage cap; a cap on `{A}` does not replace a cap on `{A,B}` (exact set, not subset), and both stay in force. Today `InvoiceExtractionToolHandler` returns the header due date but no per-line fee kind (ontology §15 answer 3; the reference is ebilling's D-27, closed typed templates).
- **Discount.** Option value 100000016. **A check only:** "rate above the agreed discounted rate" (per line; ontology-r1 115 review: a firm may discount at invoice level, so the check never claims the discount was not applied). A line with no rack rate gives one "Discount not checked: no rack rate" finding.
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

Two read models, both produced by the evaluation's own resolver, **read as the caller**, with their routes in the `RouteAuthorizationGuardTests` ledger (ontology O7). **Step 0 applies to both (uac-r2 C5):** every `sprk_isexternal` caller is refused, and a fault reading the flag refuses; they expose Procedure names, versions and overrides, which are internal-only (O-27, owner round 113):

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
for each procedure (status Active, with at least one Active version — a Draft-only Procedure is not Active; proposed):
    procVersion = the Pin (nearer wins; two different d1 pins → blocking gap)
                  OR the Active Procedure Version with from ≤ workDate < to (two found → fault)
    if none:  contributes nothing for this subject AND raises a gap (below)
    level    = Manual d0 | Manual d1 | Firm | All records
    rules   += for each enabled policy of the Procedure's rule set: its policy version for procVersion (7.2)
    rateCard = procVersion's published rate card (always firm-named, I-P3)
rules    −= rules Excluded on the chain for this firm (row Firm empty or = invoice firm; nearer wins)
rules    += record rules of the chain's record-scoped rule sets whose scope is Record,
            or Firm record with sprk_firm = invoice firm (scope AND a non-null record lookup required),
            each in its base rule's slot (6.3)
→ precedence (7.3)
```

- **Fails closed.** Any failed read of chain, assignment, override, Procedure or version rows faults the evaluation; the job retries; nothing is judged on a partial set (`BillingFactLoader.cs:243-244`).
- **What counts as a gap — confirmed by the owner 2026-10-21 (ontology F2-d).** A Procedure applies from its **first Active version's effective-from**: work dated before that is *not applicable*, not a gap. A gap is a hole **between** Active versions, or a date after an end with no successor. **A version whose window ends with no successor raises the gap hold only for work dated after that end.** A Procedure with only Draft versions is not Active.
- **A gap holds the invoice (O-22).** When an applicable Procedure has no version in force on the date (as read above), a pinned version is retired, or two d1 pins conflict, the system raises a **blocking internal finding** on `BILL-SYSTEM-SUBMISSION` (the D-115 pattern) naming the Procedure and date, and the invoice shows **"No procedure associated with this invoice"**. The hold is raised when the invoice is checked (submission, or a correction under task 101), never by a later Procedure change (O-21 revised). The invoice cannot be approved until a check clears the gap; whether an Override may release it is open (section 15).
- **`BILL-SYSTEM-SUBMISSION` is exempt** from the Procedure model (ontology blocking 2b): it has no rule set (D-125) and keeps its `sprk_currentversion` pointer chain.
- It replaces today's rule-set eligibility (`BillingFactLoader`, ClientDefault and Firm) and the planned 082 selection.

### 7.2 Rule versions under Procedure Versions (re-keys today's version chain)

**Today**, a rule (`sprk_policy`) has one chain of versions: an edit creates a version in force from now (`RuleSetService.cs:1296-1297`); publish refuses an earlier effective date (`:1628-1631`), end-dates the predecessor and moves `sprk_currentversion` (`:1720-1731`); the loader takes current or end-dated versions (`BillingFactLoader.cs:116-135`), and the evaluator picks by date (`InvoiceComplianceEvaluator.cs:397-399`). That chain cannot hold two Procedure Versions' rules side by side.

**For billing rules, this changes to:**

- Each policy version is linked to **one Procedure Version** (`sprk_policyversion.sprk_procedureversion`), with an explicit **published** marker: `sprk_publishstate`, global choice **Draft (100000000) · Published (100000001)** (ontology F3-b; name confirmed in the 080 schema review). Only Published versions are loaded.
- The Procedure Version's [from, to) is the rule's window; for a pinned record, the pinned version's rules apply whatever the date.
- **Corrections** add a policy version under the same Procedure Version with its own effective-from; within it, the latest published policy version with effective-from ≤ work date applies.
- **`sprk_currentversion` means "latest published"**, for display only (ontology-r1 amends D-14). `RuleSetService` edit, publish, `IsDraft` and `Predecessor` are re-keyed per (policy, Procedure Version).
- **Readers to re-key:** `BillingFactLoader` `:97`, `:116`, `:123`, `:383`; `RuleSetService` `:2039` (the D-79 firm clause `wasInForce`). Display only: `RuleSetService` `:2134`, `:773`. ontology-r1 fixes `OntologyAdminClassificationService` `:658/:685/:705`.
- **Ratchet ArchTest (baseline plus controls, ontology F3-b):** every `sprk_currentversion` reader outside a baseline allowlist applies `RuleDomains`; new readers fail, known ones are listed and worked down. Must-fire: a fixture reader with no `RuleDomains` call; must-not-fire: a reader that applies it.
- **Ontology's readers are unaffected** and stay Ontology-only: `SignalReevaluationJob.cs:226-235`, `SignalClosureService.cs:54-56` (confirmed by ontology-r1).
- **Ontology-area Procedures are document-only in R1 (O-24):** the validator refuses `sprk_procedureversion` on Ontology-domain policies. Billing never writes `sprk_policy.sprk_matter`.
- **Precondition:** our 070 reaches master.

**v2 copies v1 (O-12):** creating Version 2 copies Version 1's rules as drafts. On publish, kept or edited rules become new policy versions of the **same** policies under Version 2; new rules are new policies; a rule dropped by Version 2 stays **enabled** with no policy version under Version 2 (work in Version 1's window still sees it; its open Signals stay open). A rule is switched off only when its whole Procedure is retired, or when it is a record rule whose override is removed.

**Drafting** (`BillingRuleDraftJobHandler`) is keyed **per Procedure Version** (today the key is the rule-set id, `:77-85`, and "already drafted" short-circuits, `:119-130`). Its source is the version's Stored File, not `sprk_sourcedocument` (`RuleSetService.cs:614`).

**Rate cards:** importing a workbook creates that Procedure Version's card; Version 2's card can start as a copy. `sprk_ratecard` gets `sprk_procedureversion` in place of the core-record lookups and the planned scope column, and keeps `sprk_firm`.

### 7.3 Several Procedures on one record

The reviewed precedence (`rule-set-scope-design.md` §3.3) is kept, re-keyed to the rank key.

- **Checklist types add up.** **Limit types replace** in rank order. A broken or downgraded specific rule never removes the general one.
- **ExpenseRule** keeps its split (ontology F3-c): its **caps replace** in rank order (a limit), and its **checklist adds** across sources (non-billable items). Stated here because section 12 retires most of `rule-set-scope-design.md`.
- **Rates:** 085's tiers re-keyed. Different amounts in the deciding tier give Ambiguous.
- **Ties mean "all apply".** The (policy code, id) order is output order only.

### 7.4 The snapshot

- **On the submission (full):** with the findings (`sprk_findingsjson` already carries `policyVersionId` and the clause, `InvoiceSubmissionPersister.cs:1038-1044`): for each rule applied, the policy version, Procedure, Procedure Version, source, level, and the **content** of every override that decided it (mode, rule, reason, distance, chain record); rules displaced or excluded; gap findings.
- **On the Decision Record (bounded summary):** `sprk_factsnapshot` is capped at 100,000 characters, so it carries a summary and a pointer to the submission (ontology O2).
- **Firms never see the basis.** The firm findings projection is an explicit allow-list DTO built field by field, with a negative test that no override reason, Procedure name or Procedure id appears (R3).

---

## 8. Signals, Decision Records and versions

### 8.1 What they cite

Unchanged at the core: the immutable policy version (clause and location), reason codes and facts. Added in the facts: the Procedure Version and the basis (source, level, override content). Firms see only clause text under the visible-to-firms flag, with the rule set's firm-facing name; a record-scoped rule set's firm-facing name is neutral ("Billing instructions for this record", or the record type's name; ontology F4-a).

### 8.2 Versions over time

- **Which version judges a line:** the one whose [from, to) contains its work date (else the invoice date). 2025 work billed in 2026 is judged on Version 1.
- **Decided invoices stand:** Decision Records are append-only; Signals cite Version 1's policy versions.
- **No automatic re-check (O-21, revised by the owner 2026-10-21, ontology D-175):** "the recheck needs to be an explicit decision by a user, not automatic". Removing an override, retiring a Procedure, and the end of a Procedure Version (superseded or withdrawn) re-check nothing. Today's triggers (`InvoiceSubmissionPersister.cs:248-251`, task 101 corrections) are unchanged.
- **The banner (computed at read time; stored nowhere; uac-r2 N2 option a).** An open invoice (pending or In Review) shows **"The procedures that apply to this invoice have changed; re-check to apply them."** (owner 2026-10-21 chose this neutral text over "A new procedure that applies to this invoice has been published", which reads wrong for an override removal or a retire) when its **current Procedure resolution differs from its evaluation snapshot** (7.4).
  - **How:** on **one route only, `/api/finance/invoices/{id}/review`** (`InvoiceLineReviewEndpoints.cs:39`), once per page load, the BFF re-runs the resolver (7.1) for that invoice and compares the result with the snapshot: the Procedure Versions chosen, the policy versions applied and the override content that decided them. Any difference shows the banner.
  - **Whose rights (uac-r2 C2):** the comparison runs **app-only**, exactly like the evaluation, so an internal user who cannot read a distance-1 parent gets neither a false banner nor a `0x8004d24d` fault. It returns **one banner state only** (changed / not changed / not yet checked / could not be checked): never a diff, names or ids.
  - **Snapshot states (uac-r2 C1):** a pending invoice has no snapshot yet, and snapshots older than v5 lack the Procedure and override fields. Either shows a distinct **"Not yet checked"** state, never "no change". An invoice judged only by `BILL-SYSTEM-SUBMISSION` is exempt and shows no banner.
  - **Cost bound (uac-r2 C3):** one resolution per invoice, not per line: the chain, the assignment and override rows, the Procedures and the versions are each read once, and every line's work date is resolved in memory against those rows. A test asserts the query count does not depend on the line count. A 429 or timeout shows "could not be checked".
  - **What it covers:** every trigger, because it compares outcomes: an override removed or added, a record rule switched off, a Procedure retired or newly assigned, a version ended, retired or newly published (a first version included), a date change. uac-r2 N1's scope falls out of it: a project's override that reached a work-assignment invoice at distance 1 is in that invoice's snapshot; an override for firm F never touches firm G's invoice.
  - **Why full comparison, not only the snapshot-citation test:** the citation test (snapshot cites a removed override, a retired Procedure, an ended or retired version, or a record rule switched Off) misses a newly published first version and a newly added override (N8). The full comparison runs only on the single-invoice review read, within the cost bound above. **Invoice lists do not show the banner in R1** (a known limit, section 15).
  - **A fault** in the comparison shows a short "Procedure changes could not be checked" note instead; it never blocks the page and never reads as "no change". The fault log carries ids and counts only.
  - **Who sees it (uac-r2 N3, C4):** internal systemusers only. The review route computes and returns the banner and the "could not be checked" note **only when step 0 classes the caller as internal** (`sprk_isexternal` false or blank; section 5.3); a true flag, no systemuser row or a read fault leaves the field out (and step 0 has already refused the route). It is never computed for CIAM contacts, never on the external `/invoice-submissions` routes or for export files, it is not in the firm findings DTO or the SPA invoice projection, and R3's negative test asserts its absence. It reveals nothing beyond the invoice the caller can already read: no counts, no other invoices, no override reasons or Procedure names in its text.
  - **Nothing is written:** no marker column, no field security, no banner write, no write scope to check, nothing to keep atomic with the change.
  - **Known limits:** (K) an internal reader learns one bit, "something changed", about a Secure distance-1 parent they cannot read, with no content (uac-r2 C2). (K) A publish is not atomic, so a read during it can briefly show a false banner, or see two Active versions and fault into "could not be checked"; nothing is stored, so the next read is right (uac-r2 probe).
  - Decided invoices keep their snapshot and get no banner.
  - **ebilling-r2 owns the explicit re-check**, a user action that re-runs the check; the banner then disappears because the new snapshot matches.
- **Publishing a new version does not re-check** In Review invoices (unchanged); invoices whose resolution it changes show the banner (above).
- **A pinned override** keeps its version whatever the dates.

---

## 9. Invoices as files (O-8)

### 9.1 Submission routes and Submit Invoice

| Route | Behaviour |
|---|---|
| Firm via SPA | Multipart to the existing external submission routes. The PDF becomes the invoice's Stored File (Invoice PDF, Firm-visible = Yes). The 051 wizard's UI is unchanged. |
| Internal upload | Billing tab or the invoice → invoice files (Firm-visible = No). |
| Email attachment, communication UI | The attachment stays a Document with its email. **Submit Invoice** copies its bytes into the new invoice's files and starts the submission. The Document never moves. |
| Email, Outlook add-in (v5.5) | The email is saved as its .eml Document with no attachment children; **Submit Invoice** (`from-email`) copies the marked attachments' bytes out of the .eml into the invoice's files. The .eml is the source document. |

**Surfaces (owner I, L; decided v5.5).** Submit Invoice is offered in the communication UI and in the Outlook add-in's separate Invoice module (section 0, v5.5). The server contract:

- Submit Invoice is a **separate endpoint keyed on (communication, attachment)**, never part of `/office/save` (whose replay and duplicate keys can't carry it, `OfficeService.cs:1239-1249`), and it never anchors on a returned document id (an attachment save can resolve to an identical document from another email, `OfficeService.cs:1025-1073`).
- Invoices are created only by submission (FR-17, FR-20). The add-in's save onto `sprk_invoice` (`OfficeDocumentPersistence.cs:47`) and invoice quick-create (`QuickCreateRequest.cs:180`) are **removed** (owner L; word-add-in-r1 implements the add-in side, we remove the server legs).
- **Marks.** The request names each attachment as Invoice (exactly one), Supporting or Don't save; anything else is refused with 400 before any write. Invoice becomes Stored File purpose Invoice PDF, Supporting becomes Invoice supporting file; both Firm-visible = No. Don't save creates nothing (its bytes stay only inside the .eml). If extraction classifies the Invoice file as not an invoice, the submission ends Failed with a message naming the file.
- **Email-only save (the contract word-add-in-r1 asked for).** One server call, `POST /api/finance/invoice-submissions/from-email`, does both halves server-side. The client sends the same email payload as `/office/save`, plus the core record, the marks and its own idempotency key:
  0. **Before any write, every Invoice and Supporting attachment must be present in the request with its bytes**; otherwise 422 naming the file. The .eml holds only the attachments whose `ContentBase64` the client sent (`OfficeEmailEnricher.cs:324`); the add-in leaves out files over 20 MB, files past its 28 MB budget, cloud attachments (links with no bytes) and unreadable unticked ones, each with a named warning. The add-in treats Invoice and Supporting as ticked (priority, and a read failure stops the call) and Don't save as unticked. Inline parts cannot be marked. Marks identify an attachment by file name, size and content type as the client sends them (not a MIME ordinal, which the client does not know).
  1. The server saves the email through the existing `/office/save` pipeline with `SelectedAttachmentFileNames = []`, which already means "create no attachment Documents" (`UploadFinalizationWorker.cs:1037-1045`). The .eml is linked to its communication in the same create (task 130, #2177) and saved own-Restricted with the submission marker (below); an email that can't be identified is refused, never saved (F1-c).
  1a. **The duplicate path (word-add-in-r1).** The save's key is content type, target type, target id and Message-ID only (`OfficeService.cs:1239-1249`), so an email already saved to the same record by the same user returns before doing anything (`OfficeService.cs:436-450`): only the earlier JobId, no document id and no filing (`ReconciledEmailFiling.FileIfUnfiledAsync` is not reached). So the endpoint (a) **resolves the .eml from its communication**, never from a returned or job-result document id (line 613's rule, uac-r2 F2-b). After task 130 (#2177) every Office email-derived Document links to its communication (the .eml, each attachment child, an attachment saved alone), and one communication can have several .eml Documents (two users, or two core records), so (word-add-in-r1, v5.7): candidates are the Documents with that `sprk_relatedcommunication` **and** `sprk_isemailarchive = true` (set at `OfficeDocumentPersistence.cs:327` and `UploadFinalizationWorker.cs:684`; the existing `GetEmailArchiveByCommunicationAsync` returns only the first, so a list variant is needed); narrow to the .eml filed under the target core record with no parent document. **On a fresh create** the endpoint uses the .eml this request created (known in-process) and does not search. **On the duplicate path** it considers only candidates the caller created (the duplicate check is per user, `FindExistingAsync(…, userId)`; another user's .eml of the same email is never chosen, uac-r2 v5.7 change 3); if more than one of the caller's remains, the one whose MIME parts contain the marked Invoice attachment's SHA-256; none, or still more than one, refuses with the permanent message below, (b) runs A itself through the refile path rather than relying on the save to file the communication, and (c) treats a communication filed elsewhere as the D-140 mismatch (409 `document_core_record_mismatch`). Attachment children an earlier save made stay where they are.
  2. It creates the submission, naming the .eml Document (resolved as in 1a) as the source document and each marked attachment by file name and SHA-256, matched against the .eml's MIME parts. **The endpoint refuses when no communication resolves** (uac-r2 F1-b): an unlinked .eml passes D7 rule 4 and would leak an internal-only email.
  **The email can't be identified (owner F1-c, v5.7: refuse).** `from-email` refuses and saves nothing, neither an .eml nor a submission (all or nothing); it never takes task 130's save-Restricted fallback. On master the capture runs before the upload (`OfficeService.cs:498-517`), so refusing with nothing written is feasible. Two kinds (uac-r2 v5.7 change 4):
  - **Retryable ("try again"):** a Graph timeout, Graph 429, Graph 5xx, or a transient Dataverse error.
  - **Permanent (a named, non-retryable message suggesting the internal upload route):** no Message-ID; Graph 403 or 404; a duplicate onto an earlier unlinked or task-130 Restricted .eml; no communication resolving; more than one candidate .eml after the SHA check.
  **The .eml is own-Restricted (owner F1-a option (i), v5.7).** An .eml that `from-email` creates is saved with its own Restricted narrowing (`AccessInheritance.OwnNarrowing(Restricted)`), so the matter's external participants cannot open firm F's invoice email even on an Open thread (owner D6). **In the same create**, and only from the submission path, it writes a **submission marker on the .eml (`sprk_document`)**, recording that the submission wrote that narrowing. It is not on the Stored File: a failed submission's undo deletes the Stored File, and the retry would then reuse a Restricted .eml with no marker, hiding the invoice from its firm permanently (uac-r2 v5.7 change 1). (a′) **skips only a Restricted narrowing that carries this marker**, so the firm still sees its own invoice. **To hide the invoice from its firm later, mark the communication internal-only (rule 4)**; re-Restricting the marked .eml changes nothing, since it is already Restricted and the marker still applies. Enforced by an ArchTest like task 130's K2 guard: the marker is written only by the submission path, only in the create that writes the narrowing, and **never in any update payload**. The 080 schema review settles: the marker column in `ParentLineage.RefuseAppOnlyWrite` and in the field-mapping skip; field security to the BFF app user (or the recorded reason not to). **K limit:** lowering the .eml's Restricted and then raising it again leaves the marker in place, so (a′) still skips it. **Reused .eml (known limit, accepted by uac-r2 v5.7):** the only reuse is the same user's own duplicate (same Message-ID, same record), their own earlier deliberate save; `BuildEmlFromMetadata` uses a random MIME boundary (`OfficeEmailEnricher.cs:255-365`), so byte dedupe never fires. If that .eml lacks the invoice part, F2-c's SHA check fails the submission; if it has it, those bytes were already disclosed by that save. when 1a resolves an .eml saved earlier by a normal save, the submission does not narrow it or mark it (changing an existing Document's access is not the submission's to do); that email keeps whatever visibility the earlier save gave it, and (a′) applies to it unchanged.
  **Route and filters (uac-r2 F2-a).** `from-email` is an `/api/finance` route, so it carries the Finance group's filters, with **step 0 before any record read** (section 5.3), plus the filters `/office/save` itself carries, since it reuses that pipeline without going through that route (`OfficeEndpoints.cs:185-195`): `OfficeAuthFilter`, `EntityAccessFilter` (Read and AppendTo on the target core record), the Office rate limit and the version-save filter. The server **overwrites** `SelectedAttachmentFileNames` with `[]` whatever the client sends: `null` means "create every attachment Document" (`UploadFinalizationWorker.cs:1033-1036`).
  **Prerequisite (uac-r2 F1-b).** "Task 130's rules apply unchanged" holds only once **#2177 (task 130, T8)** is merged and deployed; on master the communication link is still best-effort and written after the create (`OfficeDocumentPersistence.cs:330-338`, `:834-842`). `from-email` does not ship before #2177 is deployed.
  3. The submission job starts only after the .eml's finalization, and reads the marked attachments' bytes **out of the saved .eml in SPE** (app-only), never from a mailbox: a B2B guest's mailbox cannot be read through Graph from our tenant (word-add-in-r1 task 116a), and the job needs no on-behalf-of token. **The byte read (uac-r2 F2-c):** at staging, one retrieve of the .eml row gives `driveId`, `itemId` and `versionnumber`; the MIME part's ordinal, name and SHA-256 must match the mark, or the submission ends Failed; step 4's `IfRowVersionMatches` pins that version. Logs carry ids and ordinals only, never file names or subjects. A failed .eml finalization ends the submission Failed; the .eml survives a failed submission.
  **Idempotency key:** Message-ID + core record + mode + invoice id + the sorted (file name, mark) list; a different marking is a different operation.
  **Removal order:** the add-in removes the Invoice save target and invoice quick-create first, or in the same release; the server legs go one release later, because cached add-in clients can still call them.
  **Known limits (owner-visible):**
  - An invoice sent as a cloud link, or a PDF over 20 MB, cannot be submitted from the add-in.
  - If the email was first saved normally with the invoice ticked, an invoice-PDF Document already exists on the core record beside the new Stored File. That Document is not moved or removed, so for that email "invoice PDFs leave the core record's Documents" (section 9) does not hold.
  - If the invoice flow ran first, a later normal Save of the same email to the same record answers "already saved", so its Supporting or Don't save attachments cannot become Documents that way. This is a general `/office/save` limit (re-saving to tick more attachments creates nothing); filed by word-add-in-r1 as #2209 (their ISS-022).
  Why one call, not two: two client calls can leave an email saved with no submission, and the client would have to correlate the async save's document id. Email analysis reads attachment text only from child Documents (`AppOnlyAnalysisService.cs:1416-1417`), so with no children the invoice text does not enter the email's analysis.
- **Existing invoice.** Adding supporting files uses the internal upload route (Firm-visible = No). A corrected invoice is a new submission linked to the old invoice: the old one closes as Superseded at the new one's commit (inside step 4), and a correction is refused with 409 when the old invoice is Approved, Sent to AP or Paid. **Guards (uac-r2 F1-d):** the caller needs Write on the old invoice, and the old and new invoices must have the same `sprk_vendororg` and the same core record (otherwise firm G could correct firm F's invoice); the Approved / Sent to AP / Paid refusal is re-checked **inside step 4** with `IfRowVersionMatches` on the old invoice; step 5's undo list **reopens the old invoice**. (a′) does not follow the resubmission link: each invoice is judged by its own source. **The old invoice's open Signals (ontology-r1, v5.8):** the resubmission closes every open billing Signal on the superseded invoice with the existing Signal resolution **Superseded** (`SignalWriter.ResolutionSuperseded`, `SignalWriter.cs:411`), through the system close `SignalWriter.CloseAsync` (`SignalWriter.cs:943`, the close for ConditionCleared / Superseded / PolicyRetired; not the Acted close `CloseActedByDecisionAsync`, :1020), recording no person; never Dismissed, which suppression counts in every domain and would mute the record's rules. It runs as an **after-step, only once step 5 has passed**, since Signal history is append-only and step 5's undo reopens the old invoice. **Durable and resumable (ontology-r1):** the close is recorded as a pending after-step and retried through the existing after-steps resume and Retry (like the totals-failed Retry), so a process death or a failed close between step 5 and the close comes back. It is idempotent: it re-reads each Signal first, counts an already-closed Signal as success, and leaves a Signal that changed since the read (for example, decided in the gap) as it is. The new invoice's evaluation raises fresh Signals from its own snapshot. Tests: a superseded invoice is left with no open Signal; the close fails once and the resume completes it. A wrapper that closes one invoice's set of Signals, if needed, goes through ontology-r1's owner review.
- The goal (owner L): the email is associated with the core record **and** with the invoice. The communication is filed to the core record (A below); the invoice's Stored File carries the source ids.

**Sources (owner K).** Internal-only, Restricted (a task-130 document whose email couldn't be identified) and Private-thread sources are **allowed**. Their isolation is carried forward, not refused: the copy stores the source ids, is Firm-visible = No, is never served to the firm, and stays out of search, indexes and chat grounding. Extraction still reads the bytes.

**Derived rows: (a′), read-time (uac-r2 F1-1).** The invoice header, lines, approval, findings and every other row extracted from an isolated source must not reach external callers. The core record's internal readers are not a leak: Restricted means only "no flagged-external share" (I-14), and internal callers pass the 065 rule. So:
- `sprk_invoice` gets an **`ExternalMarker` predicate** (191), not a lineage lookup (which would join the secure intersection): the invoice is hidden from every caller who is not an internal systemuser while its Submit Invoice source fails **D7 rules 2–4** (the source document's own value, any Restricted ancestor, or the 065 communication rule) or the effective Restricted state of any of the source's roots. It reuses `ExternalDocumentNarrowing`, then 191's `ExternalVisibility`. It does **not** use rule 1 (roots), so a later refile never hides the invoice from its own firm (O-32).
- **Taint closure** carries it through the `SecureChildLineage.cs` edges (`:74-77`, `:142-165`) to lines, approval, submission, correction, external identifier, Signals, Decision Records and the Stored File.
- **Surfaces, from R1** (not only after 191): the SPA invoice list and module, the firm findings DTO, the firm file route (invoice-level, the same 404), the external invoice-index retrieval, and the AI tools.
- **On an add-in invoice (v5.6, uac-r2 Q1)** the source is the .eml Document: rule 4 is the .eml's `sprk_relatedcommunication` → that communication's internal-only flag, and rule 3 is evaluated too, since a reused .eml could be a child. Rule 2 skips the .eml's own Restricted only when it carries the submission marker (owner F1-a (i), 9.1).
- **Nothing is stored.** It heals automatically when the source is lowered (`CommunicationInternalOnlyFloor.cs:131-154`). A Private thread needs nothing more (internally Private narrows only chat overlays and fan-out; externally the 065 rule treats it as internal-only).
- **Effect, accepted by the owner (uac-r2 round 116):** while its Submit Invoice source is internal-only **or Restricted**, the invoice and everything derived from it are hidden from external callers, the firm included; it reappears when the source is lowered.

**The two halves of J (owner: A + B).**
- **A, file first.** When the communication is unfiled, Submit Invoice files it to the target core record M **before commit**, through `PATCH /api/communications/{id}/filing` with the caller's rights. The add-in's save then reports AlreadyFiled.
- **B, the file follows its source.** On **every access** to the copy, the internal route applies **the copy rule: the caller's own Read on the source document**, gate variant, so a later refile, Restricted flag, internal-only flag or Open → Private change is honoured for the bytes (uac-r2 F4-2). External callers never get a copy (Firm-visible = No) and are also covered by (a′).

**All or nothing (owner O), step by step:**

1. **Confirm.** The caller must Read the source document and its communication (#2150); with no source document, the communication only (below). If the communication is unfiled, file it first (A), and **resolve its thread before the pre-check**. Then: the D-140 filing chain, with the thread hop through `RecordThreadFiling.ParentsOf` (#2150 merged; a Direct thread contributes nothing), and `ExistingDocumentIsolationCheck`. Answers: 409 `document_core_record_mismatch`; 503 on a read fault.
2. **Staging (job).** Copy the bytes to a blob under a Stored File id **= f(submission id, source version)**, or **f(submission id, the attachment's identity)** when there is no source document, else a fixed 0, so a replay re-stages under the same id (uac-r2 F4-4, N6). Record the source document's version and SHA (uac-r2 F2-c). The blob is unreachable without a Servable row.
3. **Pre-transaction check (job).** Re-run D-140 (`ResolveFilingAsync`, uac-r2 F1-f) and the isolation check, and read the source's Restricted, internal-only and privacy state (uac-r2 condition 2). Under owner K these flags are not refusal grounds, and nothing is stored from them ((a′) derives them at read time); an unreadable flag fails the submission (condition 1).
4. **Commit (one Dataverse transaction).** The invoice, its lines, the approval, the submission update and the **Stored File row as Not yet servable**, plus the source-document guard on the version recorded at staging (`IfRowVersionMatches`, `InvoiceSubmissionPersister.cs:497-513`). **From-email (amended 2026-10-28, task 094):** the `.eml`'s guard is at the version step 3 read in this delivery, not the version the parts were read at: the in-process save's finalization writes the `.eml` row after the intake, so a staging-version pin would never commit. The parts' bytes are pinned instead by the SHA-256 recorded at intake from the caller's marks, and the guard pins the filing and isolation step 3 judged. A guard failure retries the delivery (there is no job-staged copy to re-stage). If the guard fails, re-stage under the id for the new source version and retry (F2-c, F4-4). If the transaction fails, delete the staged blob (compensation; orphan backstop). The verified-pointer read stays (`InvoiceSubmissionProcessingJobHandler.cs:539-544`).
5. **Re-run immediately after commit, before any after-step.** No totals, Signals, Decision Record, indexing or notification runs until it passes. It re-runs D-140 and compares `SecureRootsAboveAsync(source document)` with the committed Stored File's roots (uac-r2 Q4).
   - **Pass:** flip the Stored File to Servable with an If-Match write, **checking inside the flip that the blob exists and its SHA matches** (uac-r2 F2-1), then run the after-steps as today.
   - **Fail, or any fault** (exception, throttle, timeout, unreadable flag; uac-r2 condition 1): **undo everything**, in this order (amended 2026-10-28, task 094, uac-r2 conditions K2, (b) and F4-1): the invoice's Stored File rows are read first and every row read is **withdrawn** at once (status Removed, conditional on the version read), before any delete, so a held undo never leaves a row Servable on a Failed submission or without its blob; more rows than one transaction can hold (a data fault) then hold the undo with nothing deleted (the rows read stay withdrawn); then the approval; the lines; the rows' blobs; then **one transaction** that deletes the Stored File rows and the invoice together (the invoice is never left without its file rows, which are the source gate task 098 reads, and no row is left without its invoice); then **clear the commit's columns on the submission** (`sprk_FirmInvoiceNumber`, `sprk_PONumber`, `sprk_FindingsJson`, `sprk_Invoice`), keeping only status Failed, the admin note and the correlation (uac-r2 F1-5). Nothing irreversible has run yet, so the undo is complete. **Step A's filing of the communication survives the undo:** it is the caller's own authorized filing, and the admin note says so (uac-r2 K-4).
   - **Undo complete or held (condition 3):** if any delete fails, the submission stays Failed and its file rows stay unservable (Not yet servable, or Removed once withdrawn) until compensation finishes. (Amended 2026-10-28, task 094: a held undo can leave Removed rows, never a Servable one.)
6. **Resume (uac-r2 F2-b).** The resume path (`InvoiceSubmissionPersister.cs:235-238` → `ResumeAsync`) runs step 5 and the flip. A detector reports rows still Not yet servable after N minutes.

**Retired in v5.5: "no source document".** The email is now always saved, so the .eml Document is the source of every add-in copy: the copy rule (B) is the caller's Read on the .eml Document, the step 4 guard is the .eml Document's `IfRowVersionMatches`, and step 5 compares `SecureRootsAboveAsync(.eml Document)`. An email that can't be identified is refused (F1-c), so the copy needs no Restricted of its own. v5.4's comparison table is removed (uac-r2 minor); it is in git history at `09b147ec9a`.


**Which races the transaction catches** (reviews §7): the in-transaction guard covers only the source document row. A refile between two ordinary records, a thread join to an ordinary record, a thread's regarding change and any Restricted/internal-only/privacy change write no document row, and a refile or join to a Secure record is caught only partly. So step 5 is the main check, not a backstop.

**What cannot be withdrawn** once the after-steps run: Signal history and the AutoRejected Decision Record (append-only), the Service Bus indexing job and its AI Search entry, and notifications once task 132 has a real sink. Step 5 runs before all of them. The 050a re-file (owner change, share mirroring) is retired, since bytes are copied.

**Remaining races and known limits:**
- **A change after step 5 passes.** The invoice is then legitimately the core record's. The bytes stay correct through B. **The fields extracted onto the invoice and its lines stay readable by the core record's internal readers** after a later change to the source: disclosure happened at submit, as with a download (owner-confirmed, O-32; uac-r2 K2). External callers follow (a′), which reads the source's current state (D7 rules 2–4) but not rule 1 (which roots the source is filed under), so a later refile never hides the invoice from its own firm; the roots' Restricted-state term still applies. To hide an invoice from external callers later, mark its source document Restricted or its communication internal-only (uac-r2 F4-1).
- **A change during the re-run itself** is in the same position.
- **K3:** soft delete keeps compensated bytes for 7 days.
- **K4:** unlinked Secure-owned rows are reported by the share job; they are held, not served.
- **K6:** a contact uploader can't be recorded as creator person.
- *(v4's Outlook save race K5 is closed by A; uac-r2's K5, Direct-thread over-refusal, is closed by `ParentsOf`.)*

### 9.2 Processing and viewing

- **Extraction:** Content Understanding gets the bytes inline, from every source kind.
- **Viewer:** the bounding-box viewer (108) loads the PDF through the internal download route.
- **Indexing:** the invoice index stays the structured record (D-44's second half). The indexing job takes no bytes from a Submit Invoice copy (owner K), and external invoice-index retrieval applies the (a′) predicate.
- **Lost by design:** invoice PDFs leave the core record's Documents, the files index and AI over record documents. Related work ranks the record's documents, not the invoice PDF (`InvoiceRelatedWorkService.cs:78-81`).
- **Invoice lines:** a file belongs to the invoice, never to a line, so #2047 is unaffected.

### 9.3 Firm routes (settled with uac-r2 and R3)

- **Routes:** `GET /api/v1/external/invoices/{id}/files` (list) and `GET .../files/{fileId}/content` (download). No upload route; uploads go through submission.
- **Systemuser caller (036 path):** the same 036 machinery (`AccessibleRecordSetService`) gives the caller's Read on the invoice and its root, the I-14 Restricted veto (which covers flagged-external users here) and the task 187 invoice rule. Unknown rights (`0x8004d24d`) fail closed as 036 does. **Firm match for a flagged-external systemuser (uac-r2 F1-4; confirmed by the owner, round 116):** the invoice's `sprk_vendororg` must equal the organisation of the user's linked contact; no link, or a read fault, refuses. Otherwise firm G's flagged-external user on a matter shared with co-counsel could reach firm F's PDFs. Internal users are unchanged.
- **(a′) at invoice level:** an invoice whose source is isolated is invisible here, with the same 404.
- **CIAM contact:** the ReadOne firm match on `sprk_vendororg` (`ExternalInvoiceSubmissionAuthorizationFilter.cs:100-104`) → `RightsOn(core) ≥ Read` after vetoes → the task 187 rule.
- **One file gate for both:** Invoice PDF + Firm-visible + Servable + scan clean + parent matches. Pending scan: listed as "Checking file…", content 409 `file_scan_pending`. Malicious: not listed, 404.
- **Uniform answers:** an invisible invoice or file gets the same 404 after the same work; a read fault gets 503. No SAS.
- **Task 188 ArchTest:** the guard names this route's gate method, with must-fire (a systemuser without Read gets 404; a flagged-external systemuser on a Restricted root gets 404) and must-not-fire controls. Added in the PR that builds the route.
- **Retired in the same change:** the four `/api/v1/external/invoices/{id}/documents…` routes (list, content, upload, versions; `ExternalProjectDataEndpoints.cs:378-412`). The BFF change is ours, reviewed by uac-r2; the SPA change is R4's (or R3's). Dev deploys BFF then SPA back to back through R3; until the SPA ships, the old SPA's invoice-documents call returns 404.
- **Flagged-external users** get invoice files only here (O-27).

## 10. Export files (O-7)

`sprk_billingexport` (023) gets its file as a Stored File (Export file), **not** a secure child; it follows the export's ownership (F4-c). Export files are internal: Firm-visible is always No, so they never appear on the firm route; their `ExternalMarker` is Never until the export question in section 15 is answered. The E-Billing Output Files container and key (D-55, D-67, 028) retire from code; the owner-created dev containers stay for the owner to remove (O-13).

## 11. Search and AI

- **Nothing reads the file at evaluation:** clauses are copied at drafting.
- **No Procedures index** (ontology-r1: not needed).
- **Invoice files:** future work (O-8); Submit Invoice copies stay out of search and indexes; AI tools apply (a′) to invoices.

---

## 12. What changes in existing decisions and completed work

| Item | Change |
|---|---|
| D-44 | Invoice PDFs move to invoice files. The structured invoice index is unchanged. |
| D-55, D-67 | Containers and keys retire from code; the dev containers stay (O-13). |
| D-145 | Withdrawn. |
| D-146 | Deleting a core record deletes its assignment rows; its record-scoped rule set and record rules are unlinked (RemoveLink) and then retired by the no-plugin sweep (O-17, task 117), never by the delete job, so the rule-set lifecycle keeps one writer (agreed with uac-r2). Reusable Procedures, rule sets and rate cards are untouched. |
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
| Every `/api/finance` route (section 5.3; owner 2026-10-21 widened it from the invoice, submission and ruleset routes) | **Step 0 in R1**, inside `FinanceAuthorizationFilter` at route-group level (uac-r2 C4/C5 and its v5.3 re-check; owner M and rounds 113, 116). `FinanceAuthorizationFilter` has no `sprk_isexternal` check today, but none of these routes is on master yet and no client depends on them for flagged-external callers, so this is a change on our branch landing with #1911, not a change to master behaviour. Flagged-external callers use the firm route. |
| Docs | "Procedure Library" retired (3.4). |
| Dev data | Recreated, not migrated (O-13). |

## 13. Effect on open tasks 079–087 (the input to the re-plan, EB-D-148)

| Task | Becomes |
|---|---|
| 080 schema | `sprk_procedure`, `sprk_procedureversion`, `sprk_procedureassignment`, `sprk_storedfile`; record lookups and the scope values Record (100000002) / Firm record (100000003) on `sprk_ruleset.sprk_scope`; `sprk_publishstate` on policy version; no banner column (the banner is computed at read time, 8.2); global option sets from 100000000; the Stored File source lookups Restrict and the "based on" link RemoveLink; `sprk_storedfile` at Deep; check that the Billing tab and record setup exist on the project and work-assignment forms; `sprk_procedure` on rule set; `sprk_procedureversion` and the published marker on policy version; `sprk_procedureversion` on rate card; `sprk_billingfirm` and `sprk_billingresponsible` on every core table in the shared list (`FilingCores`; no hand-typed table names); custom statuses, DateOnly dates, the alternate key; field security (I-P2, F1-b); Restrict and delete behaviours (5.2); creator person (081 adds the tables to `Set-ChildRecordCreatorPersonSchema.ps1` and `StampedChildTables`, asserting zero non-null values before securing); auditing. Notices to ontology-r1 and uac-r2 before and after. ontology-r1 reviews the schema in full. |
| 081 | Procedure service (versions, I-P1–I-P5, copy-forward, retire reconcile; retire, publish and date changes need Write plus the billing-admin privilege; step 0 refuses `sprk_isexternal` callers; the read-time banner comparison on `/review` only); assignment and override service (pin, exclude, change rule into the record-scoped rule set), caller-checked (F1-c); secure child registration for assignment rows and record-scoped rule sets (F4-a); census waiver and guard rewrite with must-fire / must-not-fire controls (O1, F3-e); NFR-05 depth list incl. `sprk_storedfile`; the K-2 pin test. |
| 082 | The resolver (7.1) with the firm condition, gaps and holds, and the re-keyed version selection (7.2); `BILL-SYSTEM-SUBMISSION` exemption; the ratchet ArchTest. |
| 083 | Firm clause via the resolver, with the firm-facing name. |
| 084 | Waits for the prototype (O-14). The back end exposes the read models of 6.6. |
| 085 | Rate-card tiers re-keyed. Scope column and backfill dropped. |
| 086 | What remains: secure child registration for invoice Stored Files. |
| 087 | Dropped. |
| New | File store service, control and orphan reconcile; Stored File schema; provisioning (storage account checks in 4.2, customer-provisioning hand-off); Submit Invoice (the `from-email` endpoint with marks and the email-only save; removal of the Invoice save target and invoice quick-create server legs; the add-in side is word-add-in-r1's); corrected invoice: close as Superseded and the "resubmission of" link (no such column or close reason exists in code today; check against 105's Return to Firm resubmission) with the Approved / Sent to AP / Paid refusal; invoice submission and exports to files; firm file routes and retirement of the four document routes (uac-r2 review, task 188 entry, R3 deploy pairing); internal route's and upload's flagged-external refusals; the (a′) `ExternalMarker` predicate on `sprk_invoice` across the listed surfaces (with uac-r2); container-scoped storage role; the affected-invoice banner (8.2; the explicit re-check is ebilling-r2's); the confirmation warning and read-only preview on Procedure retire and changed-rule override removal (section 15, answer 2); an Override that releases a gap hold (answer 3); AddedFee and Discount types; docs clean-up. |
| New (Finance step 0) | Step 0 per section 5.3: one route-group flag on every `/api/finance` route (owner 2026-10-21; enforced by `FinanceStepZeroCoverageGuardTests`), checked in `FinanceAuthorizationFilter` before any record read, answering with each route's own `Deny()` (403 on `/review` and `/context`; `UniformNotFound` on `GET /invoice-submissions/{id}`, `GET /submissions/{id}/blocked`, `POST /matters/{id}/recalculate` and `POST /projects/{id}/recalculate`). Tests per route: must-fire (flag true; no systemuser row; flag read fault) and must-not-fire (flag false; flag blank), plus, on each of those four routes, a test that a step-0 refusal is indistinguishable (status, Content-Type, body but the correlation id) from an internal caller's 404 for an absent id. The review route alone computes the banner (8.2). uac-r2 C4/C5 and v5.3 re-check. |

## 14. Coordination

- **v5 re-checks are in** (`procedure-model-reviews.md` §8, §9; word-add-in-r1's in v5's §15): ontology-r1 findings with nothing blocking; uac-r2 accept with changes, given (a′); word-add-in-r1 matches with three gaps. All folded into v5.1, with the owner's re-check answer (ontology D-175).
- **Next:** the owner's answers (section 15), then the owner finalizes; EB-D-148 and the re-plan of 079–087. ontology-r1 then does the full schema review of the 080 rewrite; uac-r2 registers the new tables in task 191 and updates `core-record-delete-design.md` (section 5.2 lists). ebilling-r2 owns the explicit re-check.
- **SPA-r3:** section 9.3. **customer-provisioning:** the storage hand-off in 4.2.

## 15. Questions still open

**Owner**
- *(Answered in v5.5: the Submit Invoice process, owner I and L; section 0 v5.5 and 9.1.)*
- *(Answered in v5.7, owner 2026-10-21, also uac-r2 round 118: F1-a option (i), the own-Restricted `from-email` .eml with a submission marker; F1-c refuse, retryable. Section 9.1.)*
**Answered by the owner, 2026-10-21:**
1. **No automatic re-check covers all three cases:** override removal, Procedure retire and Procedure Version end. Each shows the banner; a re-check is always a user's explicit decision.
2. **Switching a rule Off still closes its open findings automatically** (D-170 PolicyRetired sweep on In Review invoices), for both Procedure retire and removing a changed-rule override. **The action shows a confirmation warning first**, for example: "Retiring this Procedure will close all related rules and remove pending decisions on active invoices." The override-removal variant: "Removing this override will close the rule it changed and remove pending decisions on active invoices." The warning is a read-only server preview (the wizard previews, the server acts); the action proceeds only on confirm. **The count (ontology-r1 and uac-r2, v5.9):**
   - **Preview = sweep.** The preview selects Signals with **the same method** (query and predicate) the D-170 sweep closes, never a second query, so the warning never claims a set the action does not touch. If the set changes between preview and confirm, the sweep closes what is true at confirm time and the result reports the actual count.
   - **Counted as the caller.** From that set, the preview counts only the open invoices the caller can Read (gate variant, `GetCallerRightsForRecordsAsync`, or an impersonated query), labelled "N open invoices you can see are affected". No "others hidden" number or flag. If the caller's rights read faults, the warning shows without a count, never the app-only number. The retire or removal itself still acts on every affected invoice; only the preview's number is narrowed (an app-only count would tell a billing admin how many invoices exist under Secure or Restricted records they cannot read).
3. **An Override can release an invoice held because no Procedure version is in force.** It is the "push it through" release: a recorded human Override (D-24 / D-39) with a reason and its Decision Record, for that invoice only. The Decision Record goes through the existing billing decision path, `DecisionRecordWriter.WriteBillingDecisionAsync` (`DecisionRecordWriter.cs:1303`, D-100 / D-101), with the reason in the record; no new writer entry point (a new one would go to ontology-r1's owner review and into the immutability ArchTest's CreateEntryPoints list). It creates no Procedure version and changes nothing for other invoices.
4. **Export files are internal-only** (`ExternalMarker` Never).
5. **The gap reading in 7.1 stands.**
6. **Banner text:** "The procedures that apply to this invoice have changed; re-check to apply them."

**Answered by the owner after the re-plan, 2026-10-21:**
- **Inactive invoices are not changeable.** A correction (097) against a Rejected, Returned to Firm, Cancelled or Superseded invoice is refused (409). It becomes changeable only if reactivated, and only an admin may reactivate.
- **An Override releases any held invoice** (114), whatever the channel and whatever raised the hold — the master "push it through", tracked by its Decision Record.
- **Storage (K-3, 091):** remove the account-scope storage grant; dev/test is not a current risk, and the requirement is correct settings for provisioned customer environments (container-scoped only).
- **Submit Invoice in the email UI (118):** three entry points running one flow: the email toolbar's "Link Invoice" becomes "Submit Invoice"; attachments the document profile typed as Invoice (`sprk_documenttype`) show a badge and "Submit as invoice"; the Email Review dialog gets an Invoice tab. The marking step is the Outlook add-in's, shared. No new classifier.
- **Step 0 (099) covers every `/api/finance` route.**

- **Banner on lists (known limit).** R1 computes the banner only on the single-invoice review read; invoice lists do not show it.
- **Banner known limits (uac-r2 C2, probes):** one bit about a Secure distance-1 parent; a brief false banner or "could not be checked" during a non-atomic publish (section 8.2).
- **Decided by the owner (uac-r2 round 116, 2026-10-21):** F1-1 accepted — while the Submit Invoice source is internal-only or Restricted, the invoice and everything derived from it are hidden from external callers, the firm included, and reappear when the source is lowered. F1-4 confirmed — a flagged-external systemuser sees only invoices of the firm their linked contact belongs to; no link means none (fail closed); internal users unchanged.
- **K-3:** accepted as a known limit (owner round 117); network block at prod hardening; container-scoped role in R1 (section 4.2).

**Engineering**
- **The guard's version bump.** Live proof that the source-document guard's write moves `versionnumber` is still outstanding (050a K1 note).

## 16. Review record

**Fable review of v3 (`92fe0be17e`), 2026-10-21, owner-approved.** 13 must-fix items, all folded into v4: the version chain (7.2), "retired by a version" (7.2, 3.1), the Submit Invoice copy guard (9.1), access (5), record-rule drift (6.3), fail-closed reads, gaps and snapshot (7.1, 7.4), re-evaluation on publish (8.2), blob protections (4.2, 4.3), §11 answers (3.3), the rate-card firm (I-P3), the drafting key (7.2), delete ownership (I-P5), overlapping sources (6.5).

**Peer reviews of v4 (`c260f9de11`), 2026-10-21**, recorded in `procedure-model-reviews.md`: word-add-in-r1 (content), ontology-r1 (CHANGE; blocking 2b, 9a, O1, all folded in), uac-r2 (6 F1, 3 F2, 3 F4, K1–K6, all folded in or answered by the owner). The owner's answers A–R are in section 2 (O-18 to O-32).

**Re-checks of v5 (`70fc2cc616`, `c6c5cb0c3b`), 2026-10-21:** ontology-r1 scoped (findings, nothing blocking; reviews §8), uac-r2 full (accept with changes provided (a′); reviews §9), word-add-in-r1 §9.1 (matches; three flagged-attachment gaps). All folded into v5.1 (section 0), with the owner's answers relayed in ontology D-175 (Type "Internal Standard" confirmed; no automatic re-check).

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


## Owner decision 2026-10-21: matter spend stays readable by flagged-external users (uac-r2 191 question)

uac-r2 (191, `notes/2179-flagged-external-dataverse-design.md`) found that `sprk_totalspend` and `sprk_totalbudget` on a core record are not field-secured. Under owner round 114, a flagged-external user who can open the record therefore sees them on the form and in POST /api/workspace/ai/summary. The owner chose to **leave them visible for now**: no field security and no new task. If this is revisited, the fix is field security on the spend columns, not a route gate, because a gate would leave the same values visible in the MDA.
