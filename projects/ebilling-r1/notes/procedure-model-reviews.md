# Procedure and file model — peer reviews of v4 (`c260f9de11`), 2026-10-21

Three reviews of `procedure-and-file-model.md` v4. Each item is folded into v5 or goes to the owner (section 4). Nothing has been implemented.

| Reviewer | Verdict |
|---|---|
| word-add-in-r1 | Content review of §9.1 and §14. Owner items marked. |
| ontology-r1 | **CHANGE**, with 3 blocking items: 2b, 9a, O1. |
| uac-r2 | Not yet acceptable: **6 F1, 3 F2, 3 F4** findings, plus known limits K1–K6. uac-r2 re-reviews v5. |

## 1. word-add-in-r1

1. **Submit Invoice is never part of the add-in's save.**
   - `/office/save` cannot carry a submit flag. Neither key includes one (`OfficeService.cs:1239-1249`, `useSaveFlow.ts:389-408`).
   - The save replays its 202 for 24 hours and then answers `duplicate`.
   - If the add-in ever offers Submit Invoice, it is a separate endpoint keyed on (communication, attachment).
   - That endpoint must never anchor on the returned document id, because an attachment save can resolve to an identical document from another email (`OfficeService.cs:1025-1073`).
2. **K5: re-running D-140 at commit narrows the window but never closes it.**
   - If S is Secure, a late filing is a leak, not a mismatch.
   - Fix: when Submit Invoice finds the communication unfiled, it files it to M first, through `PATCH /api/communications/{id}/filing` with the caller's rights. The add-in's save then reports AlreadyFiled.
   - The D-140 re-run at commit stays as a divergence check that fails the submission.
3. **The copy escapes T7.** `ExistingDocumentIsolationCheck` reads Secure roots only. It ignores Restricted, `sprk_isinternalonly`, the open-thread rule (065), and Restricted/Limited parents. A task-130 Restricted document has no communication link. Required:
   - (a) refuse when the source is Restricted, its communication is internal-only, or its thread is not Open;
   - (b) store the source document id and communication id on the Stored File;
   - (c) email copies are Firm-visible = No;
   - (d) no indexing unless T7 is passed.
4. **The add-in already creates invoice-related data:**
   - it saves Documents onto `sprk_invoice` (`OfficeDocumentPersistence.cs:47`) and quick-creates invoices (`QuickCreateRequest.cs:180`), which conflicts with O-8 and with the rule that invoices are created only by submission (FR-17, FR-20);
   - attachment Documents are created asynchronously (`UploadFinalizationWorker`);
   - once #2150 lands, Submit Invoice checks the caller can Read the source document and its communication.
5. **Corrections to v4:**
   - `FileIfUnfiledAsync` is at `ReconciledEmailFiling.cs:103-196`, called from `OfficeService.cs:523-524` and `:549-550`.
   - It files only an existing, unfiled communication, when the save names a filing record and the caller has Write. It never moves a filed communication.
   - The K5 window runs from confirm until after commit.
   - K5 is not specific to the add-in: any filing of an unfiled communication causes it (`CommunicationEndpoints.cs:133`).
   - An attachment save may create no Document, and an unidentifiable email produces a Restricted Document with no communication link.

## 2. ontology-r1

1. **Naming.** Agreed:
   - Retire "Procedure Library" from the docs (`sprk_ruleset.md:28`, `sprk_ratecard.md:21`, ebilling CLAUDE.md).
   - Rename Type "Internal procedure" to **"Internal guidance"**.
   - Area **reuses `sprk_policydomain`** (`RuleDomain.cs:38-41`).
   - ontology-r1 adds a component-model §3 entry: a Procedure is provenance only, never evaluated, never cited in place of a policy version, and not a `sprk_document`.
2. **Version chain.**
   - Confirmed: SignalReevaluationJob and SignalClosureService are Ontology-only on our branch.
   - Precondition: our 070 reaches master.
   - **BLOCKING 2b:** `BILL-SYSTEM-SUBMISSION` has no rule set (D-125). Exempt it and keep the pointer chain for it.
   - **Re-key:** BillingFactLoader `:97/:116/:123/:383`; RuleSetService `:2039` (the D-79 firm clause `wasInForce`).
   - Display only: `:2134` and `:773`.
   - ontology-r1 fixes OntologyAdminClassificationService `:658/:685/:705` on their side.
   - Recommended: an explicit **published** marker on the policy version, and a ratchet ArchTest that every `sprk_currentversion` reader outside an allowlist applies `RuleDomains`.
   - ontology-r1 amends D-14 (for billing, the pointer means "latest published").
3. **Retirement.**
   - (a) Define what a Retired Procedure Version means for its enabled policies and their Signals.
   - (b) [Owner] Removing a record-rule override closes its Signals on In Review invoices and nothing re-judges them.
   - (c) [Owner] Retiring a Procedure clears findings on undecided invoices.
   - O4: say how D-170 sees "Procedure Retired": switch each policy Off, or have the sweep read the Procedure's status.
4. **Resolver.**
   - [Owner] **Firm + record is lost** (D-171 pt 1). Every source gets the firm condition, and the rank key returns to (Level, −Distance, FirmSpecific).
   - A work assignment's two d1 parents can conflict: **Exclude wins**, and conflicting pins raise a blocking gap.
   - Overlapping Active versions **fail closed**.
   - **Keep the scope lock** (D-172 Q9): Applies to, Firm and the record link are locked once a version is Active.
   - Citation fix: the BU points are D-171 pts 4 and 6 and D-172 Q13, not pt 3.
   - Restate the ExpenseRule split. Ties mean "all apply"; the code/id order is output order only.
   - Recommended: a gap **holds**.
5. **Rule overrides — defect.**
   - A record rule in a firm-less exceptions Procedure applies to **every** firm's invoices on the record, and displaces Replace rules the user never touched.
   - Fix: the record rule takes the base rule's slot (same firm condition and rank); the Exclude removes only the base rule.
   - Record-rule codes start a new D-13 key (K).
   - Compute stale / base-retired in the read model; if ever stamped, stamp only on change (D-61).
6. **AddedFee / Discount.**
   - Option values 100000015 / 100000016 (`RuleType.cs:93`), mapped in `RuleDomains.DomainOf` (an unmapped value throws), with the pinning test extended. The PR goes to ontology-r1.
   - "Not allowed" and "after N days" ship only if extraction gives the fee kind and the due date.
   - **Discount is a check only** ("agreed discount not applied").
7. **Ontology's matter rules stay as they are in R1.**
   - Billing never writes `sprk_policy.sprk_matter`.
   - Ontology-area Procedures are document-only in R1: the validator refuses `sprk_procedureversion` on Ontology policies.
   - A convergence issue is filed post-R1.
8. **Search index: not needed.**
9. **Schema.**
   - **BLOCKING 9a:** "Exceptions for" must be **RemoveLink**, with an `sprk_isexceptions` marker and a no-plugin orphan sweep (O-17).
   - Assignment rows: record lookups cascade; the rule lookup cascades or faults (a nulled rule must never read as a Procedure-level Exclude); the pin is Restrict.
   - Use a custom status, not statecode, because statecode can't be field-secured.
   - Effective dates are **DateOnly and half-open [from, to)**.
   - An alternate key on (Procedure, version number).
   - [Owner] `sprk_createdbyperson` on the 4 new tables.
10. **Other.**
    - **BLOCKING O1:** the census waiver (`RecordOwnerAssignmentCensusTests.cs:128-140`) rests on "a billing rule is never filed under a matter". Route creates through `IRecordOwnershipResolver`, add lineage with uac-r2, and rewrite the waiver and guard.
    - O2: `sprk_factsnapshot` is capped at 100,000 characters. Keep the full snapshot on the submission and a bounded summary on the Decision Record.
    - O5: **prefix decision ids** with the project (EB-D-nn / ONT-D-nn).
    - O6: ontology-r1 records one superseding row for D-171/D-172/D-174/D-173/D-14 when D-148 is final.
    - O7: the read models read as the caller, and their routes go in the RouteAuthorizationGuardTests ledger.

## 3. uac-r2

**Fix now (F1):**
- **F1-a.** Org-level (Global) Read on `sprk_procedure` reaches the Secure business unit, so exceptions Procedures in the same table leak. NFR-05 checks only 3 root tables. uac-r2 recommends **option 1**: record exceptions leave `sprk_procedure` and become a **record-scoped `sprk_ruleset`** (typed record lookups, RemoveLink, secure family at Deep, as 081 planned). "Exceptions for" is dropped. Other options: (2) no Global depth, with reads through the BFF; (3) share with an internal-readers team. In every case, restore 081's NFR-05 depth list.
- **F1-b.** Field-secure every Stored File authorization column: parent lookups, purpose, Firm-visible, status, SHA, size and path. The download checks that the path's parent id equals the row's parent.
- **F1-c.** Assignment, override and change-rule writes are checked as the caller: Write/AppendTo on the record (the gate variant, same 404), Read on the Procedure or rule, and the billing privilege. The row is created as the application, owned by the resolver's team, with creator person = the caller.
- **F1-d.** Submit Invoice copies are **always Firm-visible = No**. Only a PDF uploaded through the SPA is Yes.
- **F1-e.** [Owner] Refuse flagged-external systemusers on the internal file route: always for Procedure files, and for invoice/export files on Restricted roots, or always.
- **F1-f.** Run `ResolveFilingAsync` (D-140) next to the pre-transaction isolation recheck and in the post-commit re-run.

**F2:**
- **F2-a. Orphan reconcile.**
  - "Gone" means a successful not-found read.
  - The grace period is at least the submission job's maximum retry horizon.
  - Skip non-terminal submissions.
  - Check the blob exists and its SHA before the servable flip.
- **F2-b.** The resume path (`InvoiceSubmissionPersister.cs:235-238` → `ResumeAsync`) runs the re-run and the flip. The Stored File id is deterministic from the submission id. A detector catches rows still non-servable after N minutes.
- **F2-c.** Record the source version and SHA at staging, guard on that version, and re-stage under a new file id when the guard fails.

**F4:**
- **F4-a.** Secure-family registration in full:
  - `sprk_procedureassignment`: lineage with 3 typed lookups; `CitationLookups` for procedure, pinned version and excluded policy; role JSON with a live probe; census; person; delete planner.
  - `sprk_storedfile`: lineage on `sprk_invoice` only; role, census and person entries; owner from `ForParents(core)` in the Submit Invoice transaction; a leaf.
  - Under option 1, the record-scoped rule set follows 081's plan.
  - `SecureFamilyConsistencyTests` enforces all of it.
- **F4-b. Delete contract.**
  - Assignment → record: Cascade or planner Delete.
  - The retire writer is a named reconcile.
  - uac-r2 updates `core-record-delete-design.md`: assignment rows → Delete; the new tables go on the hard-deny list.
- **F4-c.** Export Stored Files are **not** in the secure family (D-85 / #1974). They follow the export's ownership.

**Verdicts:**
- **Q1.** CIAM is safe if the new tables stay out of `ExternalModuleRegistry`.
- **Q2.** The secure-child intent is right; see F1-a and F4.
- **Q3.** All new tables need registration.
- **Q4.** Acceptable once F1-f and F2 are done. The NOT-YET-SERVABLE row closes the bytes' K4 window if all of these hold:
  - every reader refuses non-servable rows;
  - the status is field-secured;
  - the flip happens on Allowed via an If-Match write;
  - Unavailable keeps the row and retries, while Undetermined or OtherSecureRoot removes it;
  - the resume path completes the flip.

  The resolver backstop is replaced by comparing `SecureRootsAboveAsync(document)` with the committed Stored File's roots. **Keep** the verified-pointer read (`InvoiceSubmissionProcessingJobHandler.cs:539-544`).
- **Q5. The download probe** is correct; use the gate variant.
- **Q5. Blob account gaps:**
  - network `defaultAction:'Allow'` with no subnet (`storage-account.bicep:64-66`);
  - the role is assigned at account scope;
  - the account is shared;
  - "dev uses the existing account" is unverified;
  - §4.2's bicep claim is stale (`customer.bicep:313` already passes the principal);
  - the scan tag may need more than Storage Blob Data Contributor to read;
  - a missing tag refuses, and "scanning not enabled" is an explicit per-environment setting.
- **Q6. The firm file route:**
  - ReadOne firm match (`ExternalInvoiceSubmissionAuthorizationFilter.cs:100-104`);
  - `RightsOn(core) ≥ Read` after vetoes;
  - the task 187 invoice rule;
  - Invoice PDF + Firm-visible + Servable + clean + parent matches;
  - a workforce systemuser gets 403; every denial is the same 404; a fault returns 503;
  - the route is classified in task 188's ArchTest.
- **Q7.** Nothing of uac-r2's relies on S8 or the BU filter.
- **Q8.** No delete-order conflict. A dev clean-up deletes attachments, then the .eml, then the communication.
- **§14.** #2151 is moot. #2159 and #2161 still matter (the strict pointer rule). T7 applies only to invoice-thread documents.

**Known limits:**
- **K1** [Owner to confirm]: derived invoice content is readable under M until the re-run, and stays readable if it fails. "Never readable" holds for the bytes only.
- **K2:** a document unfiled at submit and later filed under Secure S keeps its earlier copy under M.
- **K3:** soft delete keeps compensated bytes for 7 days.
- **K4:** unlinked Secure-owned rows are reported by the share job; add a "held" state.
- **K5:** until #2150, the thread union over-refuses Direct threads (the safe direction).
- **K6:** a contact uploader cannot be recorded as creator person.

**SPA-r3 questions:**
- route shape and a list route;
- the task 188 ArchTest;
- the firm findings projection excludes the snapshot basis;
- what the firm sees while a scan is pending.

## 4. Owner decisions needed (consolidated)

> **2026-10-21:** the owner answered C, F, G and the override half of D directly in ontology-r1's session (ontology D-175, `notes/decisions.md` in that worktree). Do not ask them again. Later the same day the owner also answered **D2** (ontology D-175 addendum) and **Q** plus the migration question (in SPA-r3's session; relayed by SPA-r3, not found in its notes). Owner answers on I–L given in word-add-in-r1's session were relayed 2026-10-21 (not yet in that project's notes); see the rows. Still open: B (field name and relation), L (with I). Everything else is answered; O leaves a race analysis owed to uac-r2 (section 7). J is answered (A + B); H's Type is answered.

| # | Question | Recommendation |
|---|---|---|
| A | Where matter-specific rule changes live (uac-r2 F1-a) | **Answered (owner, 2026-10-21): option 1.** Matter-specific rule changes live in a record-scoped `sprk_ruleset` in the record's secure family; the exceptions Procedure and "Exceptions for" are dropped. Procedures stay reusable documents. |
| B | Default firm field for a record's Procedures list | **Owner proposes a new explicit field** `sprk_billingentity` (organization lookup) on each core record, on the Billing tab and in record setup, as the default firm for its Procedures list. A prompt "Do billing procedures change?" when it changes is not R1: Procedure changes stay a manual user step. **Our reply (open: name and relation to the assigned firms):** see the note after this table. |
| C | Firm condition on every source, manual assignments included | **Answered (ontology D-175 (1)):** yes. A Procedure naming firm F applies only to F's invoices, from every source. Rank key (Level, −Distance, FirmSpecific). |
| D | In Review invoices when an override is removed | **Answered (ontology D-175 (2)):** re-check. The general rule re-judges invoices still in review, so none is left unjudged. |
| D2 | Retiring a whole Procedure: may it close findings on undecided invoices (ontology D-170 reading)? | **Answered (ontology D-175 addendum):** retiring re-checks its pending and In Review invoices; the retired Procedure's findings close on that re-check. Decided invoices keep their snapshot. |
| E | No version in force for a work date | **Answered (owner): hold**, with a visible warning on the invoice: "No procedure associated with this invoice". |
| F | Creator person on the new tables | **Answered (ontology D-175 (4)):** `sprk_createdbyperson` on Procedure, Procedure Version, assignment and Stored File, D-173 shape and protection; replaces "uploaded by". 081 adds the tables to `Set-ChildRecordCreatorPersonSchema.ps1` (`$Tables`, `$SolutionHomes`) and `StampedChildTables`, and asserts zero non-null values before securing. |
| G | Ontology's matter rules stay separate in R1 | **Answered (ontology D-175 (3)):** yes. Ontology-area Procedures are document-only in R1; a validator refuses `sprk_procedureversion` on Ontology-domain policies. Ontology files a post-R1 convergence issue. |
| H | Type "Internal guidance"; Area reuses `sprk_policydomain` | **Answered (owner, 2026-10-21): "Internal Standard"** (shorter is better; replaces ontology's relay of "Internal Operating Standard"). Type list: Guideline · Rate card · Billing instructions · Internal Standard · Other. Area reusing `sprk_policydomain` stays our recommendation. |
| I | The Outlook add-in offers Submit Invoice | **Owner (relayed by word-add-in-r1): yes.** The user flags an attachment to go to the invoice's files instead of becoming a Document. The process is to be discussed separately with the owner; no design is fixed. |
| J | File an unfiled email to the invoice's matter first | **Answered: A + B.** Earlier the owner leaned to A (file the communication to M through the refile path with the caller's rights, before commit). Options sent to the owner (word-add-in-r1 `notes/ebilling-r1-review-2026-10-21.md`, 277ee0f2c1): **A** file first (gap: a later manual refile to S leaves the copy readable under M); **B** the invoice file follows its source (T7 reads the stored source ids on every access; needs a uac-r2 T7 rule; also the mechanism K needs); **C** row-version check at commit (misses later refiles); **D** block refiling behind a submitted invoice (blocks corrections); **E** reconcile afterwards (leaves a window). **Owner chose A + B ("fix both"; word-add-in-r1 note 0b3810144e).** **Limit of B:** it covers the bytes only. Fields extracted onto the invoice and its lines (amounts, narratives) do not follow the source; that exposure goes to uac-r2 with K. |
| K | Refuse Restricted, internal-only and not-Open sources | **Owner (relayed): internal-only sources are ALLOWED.** The copy carries the isolation forward: source document and communication ids on the Stored File, Firm-visible = No, and kept out of search, indexes and chat grounding unless it passes T7 (extraction still reads the bytes). Who may read such a copy under the invoice, and the fields extracted from it onto the invoice and lines, needs uac-r2's review. **Restricted and not-Open sources: also ALLOWED, same terms (owner, word-add-in-r1 note bce36d0fc0).** Under J's B the invoice file inherits the source's isolation (stays Restricted, or hidden from partners for a Private thread). The extracted-field readership question for uac-r2 covers all three cases: internal-only, Restricted, Private-thread. |
| L | Retire the add-in's "save onto invoice" and invoice quick-create | **Tied to I (owner):** review the whole process so it is not overcomplicated. The goal: the email is associated with the matter, project or work assignment, and with the invoice. Our constraint stays: invoices are created only by submission (FR-17, FR-20). |
| M | Flagged-external internal users on the file route (uac-r2 F1-e) | **Answered (owner, uac-r2 session27 notes, round 113):** Procedure files: always refused (internal-only for download; rule and Decision Record extracts an external user may see are unaffected). Invoice and export files: reachable by "the related external user (i.e., it's their invoice)". uac-r2 then corrected itself: the firm route serves systemusers through the 036 chain (036 read set → I-14 veto → task 187 invoice rule → file gate; R3 d5267adb9), so "their invoice" needs no new relation. **Our design choice (sent to uac-r2):** the internal file route refuses every `sprk_isexternal` caller for every file kind; flagged-external callers get invoice and export files only through the firm route. Must-fire and must-not-fire tests on both routes. Context: the owner reversed #2179 (c); flagged-external systemusers must reach no internal-only record, in Dataverse or the SPA. uac-r2 is designing Dataverse-side enforcement that will cover Procedure and Stored File rows. |
| N | A reusable Procedure used for a Secure matter (e.g. its rate card) is readable org-wide, name included (O-6) | **Answered (owner): confirmed.** A reusable Procedure is readable org-wide even when used on a Secure matter or project (O-6). Record-specific rules go in the record-scoped rule set (A). |
| O | uac-r2 K1: derived invoice content readable under M if the post-commit check fails | **Answered (owner, uac-r2 round 113): all or nothing**, replacing our "accept". The email and its PDF pass together or not at all; the pre-transaction check stays. A failed post-commit re-run undoes **everything** the submission created (invoice, lines, findings, Signal rows, Stored File row, blob), marks the submission failed and adds an admin note. **We owe uac-r2:** whether the in-transaction document-row guard already catches the re-file race (leaving only root-made-Secure for the post-commit re-run), which races remain, and any Signals already sent outside that cannot be withdrawn. §9.1 step 5 and K1 change in v5. |
| P | Auditing on the assignment and Stored File tables, plus a download audit event | **Answered (owner): yes.** |


**B: our reply to the owner's `sprk_billingentity` proposal (2026-10-21).**
- **Yes, an explicit field is clearer** than inferring the firm from `sprk_assignedlawfirm1/2`. It is set on the core record (Billing tab and record setup) and seeds the record's Procedures list with that firm's Procedures.
- **Name.** In e-billing, "billing entity" usually means the *client's* entity being billed (the subsidiary or cost entity that pays), not the law firm. If the field means the firm that bills the record, `sprk_billingfirm` avoids that confusion; if it means the client entity, the firm default still needs a field. Owner to choose.
- **Relation to the assigned firms.** Submission eligibility stays on `sprk_assignedlawfirm1/2` (FR-17). Evaluation always uses the invoice's own firm (C), so a record billed by two firms still applies the right firm's Procedures. The new field only decides the default list and display.
- **§11 answers.** Existing: `sprk_assignedlawfirm1/2` (two slots, submission rule). Extension: reusing slot 1 would silently change who may submit, so a separate field is cleaner. Cost of doing nothing: the Procedures list must guess between two firms.
- **Scope:** on every core record type (see the core-record directive below), not only `sprk_matter`.

**"Matter" means any core record (owner directive, 2026-10-21).** e-billing applies to `sprk_matter`, `sprk_project` and `sprk_workassignment`, and will extend to `sprk_agreement`. "Matter" in these notes is shorthand only. v5 states it once and every new table, lookup and rule is built to the core-record set, not to `sprk_matter`:
- The assignment/override table carries a lookup per core type (or one polymorphic regarding), driven by one list, like `InvoiceFamilyCoreRecordGuard.FilingCores` (`["sprk_matter","sprk_project","sprk_workassignment"]`).
- The new firm field, the Procedures list control and the record-scoped rule set (A) exist on all three.
- **Agreement:** today `sprk_agreement` is a child of a matter or project in `CoreAncestorResolver.cs:212`, not a core record. Adding it means a schema step and one list entry; the resolver walk then treats agreement → matter/project as distance 1. v5 records this as the extension path, not R1 work.

**`sprk_family` (owner question, 2026-10-21).** No table named `sprk_family` exists in v4, the repo, the peer worktrees or dev Dataverse (checked 2026-10-21). Two things in our notes could read that way:
- **v1's "Family" lookup.** v1 proposed one Procedure table whose rows were grouped into a "family" (all versions of one document). That was withdrawn in v3 (O-15) for two descriptively named tables: **Procedure** (`sprk_procedure`, the document) and **Procedure Version** (`sprk_procedureversion`, each issue with its file and effective dates). No family table remains.
- **"Secure family."** uac-r2's term for the set of child tables that inherit a Secure record's isolation (lineage, role row, census). It is a concept in code (`SecureChildLineage`), not a table, and v5 will say "secure child tables" to avoid the word.
If the owner saw `sprk_family` somewhere else (a prototype or another project's design), we need the source to answer.

## 5. SPA-r3: early views on the firm invoice-file route (not final; waits for the owner's go)

- **Today.** The SPA reaches invoice PDFs as SharePoint Embedded Documents at `/api/v1/external/invoices/{id}/documents` (+ `/{documentId}/content`, `/versions`, POST upload; `ExternalProjectDataEndpoints.cs:384-418`, `RequireWorkforceCeiling`). Master has no SPA submission wizard and no findings display.
- **Correction to uac-r2's Q6 (to reconcile with uac-r2).** "Workforce systemuser → 403 UseInternalRoute" contradicts A9 / UAC 036 (on in dev): licensed users use the SPA under their own rights. R3 proposes serving systemusers the 036 way: caller Read on the invoice, then the file gate.
- **Route shape.**
  - `GET /api/v1/external/invoices/{id}/files` (list) and `GET .../files/{fileId}/content` (download), with the same group and filters as the document routes.
  - One gate function returns the visible file set. The download checks "fileId ∈ set" and authorizes before streaming.
  - No upload on this route; uploads go through submission.
  - An invisible invoice gets the uniform 404 on both routes, never an empty 200.
  - The SPA (R4) reuses DocumentLibrary's list and download, without upload.
  - Question from R3: during migration, can one invoice have both SPE documents and files? (O-13: no migration, so new invoices have files only.)
- **Task 188 ArchTest.** The route is classified as an external content surface with its own predicate (Firm-visible + Servable + clean). The guard names the gate method, with must-fire and must-not-fire controls. This is uac-r2's call.
- **Findings projection.** An explicit allow-list DTO, built field by field, plus a negative test that no override reason, Procedure name or Procedure id appears.
- **Scan pending.** R3 prefers showing the row with `scanStatus: pending` ("Checking file…", no download) and having content answer 409 `file_scan_pending`. A malicious file drops out (404). Hiding it until clean is simpler but invites duplicate uploads. **Owner decision Q.**

| # | Question | Recommendation |
|---|---|---|
| Q | Firm sees a pending-scan file as "Checking file…", or the file is hidden until clean | **Answered (owner, SPA-r3's session):** show "Checking file…" with no Download. The list row carries the scan status; content on a pending file is 409 `file_scan_pending`; a Malicious file drops out of the list and its content is 404. |
| Q2 | Migration of invoice PDFs from SPE Documents | **Answered (owner, SPA-r3's session):** no transitional period, all of this is dev. The invoice-PDF-as-Document path and its components are removed. Matches O-13 (no dev migration). |
| R | Licensed (systemuser) SPA users on the firm file route | Serve them under their own rights, the 036 way, once reconciled with uac-r2. |

## 6. uac-r2 and SPA-r3: settled rule for the firm file route (item R, 2026-10-21)

uac-r2 withdrew "workforce systemuser → 403 UseInternalRoute" (it pre-dates 036). Agreed with R3, for v5:

- **Systemuser caller (036 path):**
  1. Resolve the invoice through the same 036 machinery (ImpersonatedRootSet / `AccessibleRecordSetService`). There is no second implementation. This gives:
     - the caller's own Read on the invoice and its root;
     - the Restricted veto (an external-flagged systemuser has no SPA access to a Restricted record, I-14, which covers F1-e here);
     - the task 187 invoice rule.
  2. Then the same file gate as the contact path: Firm-visible + Servable + clean + parent matches. Internal-only files stay on the internal route (F1-e).
  3. Unknown rights (`0x8004d24d`) fail closed exactly as 036 does.
- **CIAM contact:** the ReadOne firm match on `sprk_vendororg` → `RightsOn(core) ≥ Read` after vetoes → the task 187 rule → the same file gate.
- **Both paths:**
  - one gate function returns the visible file set;
  - an invisible invoice or file gets the same 404 after the same work, and a read fault gets 503;
  - no upload route;
  - the routes are `GET /api/v1/external/invoices/{id}/files` and `.../files/{fileId}/content`;
  - no SAS: the managed identity reads the blob only after the gate.
- **ArchTest:** task 188's external-surface guard names this route's gate method, with must-fire and must-not-fire controls. The entry is added in the PR that builds the route. Must-fire tests:
  - a systemuser without Read gets 404;
  - an external-flagged systemuser on a Restricted root gets 404.

Item R is settled (owner informed, no decision needed).

## 7. K1 all-or-nothing: which races the transaction catches (for uac-r2, 2026-10-21)

From a read-only code trace of `work/ebilling-r1` (file:line in the trace; summarised here).

**What the in-transaction guard covers.** One row: the confirmed `sprk_document`, by `versionnumber` with `IfRowVersionMatches` (`InvoiceSubmissionPersister.cs:497-513`). Not the communication, the thread, the root or the owner team. `ExistingDocumentIsolationCheck` reads lineage and owner only, never the Restricted or privacy flags. Live proof that the guard's write moves the version is still outstanding (K1 note in `050a-confirm-redirect.md`).

**Race by race, between the pre-transaction check and commit:**

| Race | Caught by the transaction? |
|---|---|
| a. Communication refiled M → S, both ordinary | **No.** The refile writes the communication; no document row changes. |
| a′. Refiled to a Secure S | **Partly.** `SecureChildReconciler.AfterRefileAsync` runs inline and re-owns the document, but after the communication write. A commit between the two is missed. |
| b. Communication joins a thread regarding S (K4) | **No** for an ordinary S; partly for a Secure S, as a′. |
| c. The thread's regarding changes | **No.** Only thread and communication rows are written. |
| d. M or the root made Secure | **Partly.** Make Secure reconciles children inline after setting the flag; anything missed falls to the 2-minute reconciliation job, after commit. |
| e. Source made Restricted or internal-only, or thread Open → Private | **No.** No Finance code reads these flags. |

So the post-commit re-run is not only for root-made-Secure: it is the main check for a, b, c and e. Under the new model the re-run must also read the source's Restricted, internal-only and privacy state (owner K answer; J's B).

**Things that cannot be withdrawn by deleting rows**, in today's order after commit:
- Signals' close/supersede/episode history, and the AutoRejected Decision Record (append-only; no role holds Delete);
- the Service Bus indexing job and its Azure AI Search entry (only when `DocumentIntelligence:Enabled`);
- the document's owner change and share mirroring (050a re-file; retired in the new model, since the bytes are copied);
- notifications, once task 132 replaces today's no-op sink.

**v5 design for all-or-nothing:**
1. Commit (invoice, lines, approval, Stored File row, submission update) as today.
2. **Immediately** re-run the full check (D-140 chain plus isolation plus the source's Restricted, internal-only and privacy state) **before any after-step**: no totals, Signals, Decision Record, indexing or notification runs until it passes.
3. If it fails: delete the invoice, lines, approval and Stored File row; delete the blob; mark the submission Failed with an admin note. Because nothing irreversible has run yet, the undo is complete. Compensation retries until done; the orphan sweep is the backstop.
4. If it passes: the after-steps run as today.

**Races that remain:**
- A change that lands after the step-2 re-run passes. The invoice is then legitimately M's; the file follows its source through T7 on every access (J's B), so the bytes stay correct. The **extracted fields** on the invoice and its lines do not follow the source: that is the open readership question for uac-r2.
- The re-run is a point-in-time read; a change committed during the re-run itself is in the same position as the bullet above.
