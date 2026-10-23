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

> **2026-10-21:** the owner answered C, F, G and the override half of D directly in ontology-r1's session (ontology D-175, `notes/decisions.md` in that worktree). Do not ask them again. Later the same day the owner also answered **D2** (ontology D-175 addendum) and **Q** plus the migration question (in SPA-r3's session; relayed by SPA-r3, not found in its notes). Owner answers on I–L given in word-add-in-r1's session were relayed 2026-10-21 (not yet in that project's notes); see the rows. Still open: L (with I). B is answered (`sprk_billingfirm` + `sprk_billingresponsible`). Everything else is answered; O leaves a race analysis owed to uac-r2 (section 7). J is answered (A + B); H's Type is answered.

| # | Question | Recommendation |
|---|---|---|
| A | Where matter-specific rule changes live (uac-r2 F1-a) | **Answered (owner, 2026-10-21): option 1.** Matter-specific rule changes live in a record-scoped `sprk_ruleset` in the record's secure family; the exceptions Procedure and "Exceptions for" are dropped. Procedures stay reusable documents. |
| B | Default firm field for a record's Procedures list | **Answered (owner, 2026-10-21):** add **`sprk_billingfirm`** (lookup → `sprk_organization`): the law firm that decides the record's default Procedures. Also add **`sprk_billingresponsible`** (lookup → `sprk_organization`): the client's billing entity, no R1 consumer, for future use (owner direction answers §11 question 3). Both on every core record type, on the Billing tab and in record setup. A "Do billing procedures change?" prompt on change is not R1. Submission eligibility stays on `sprk_assignedlawfirm1/2`; evaluation uses the invoice's firm (C). |
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
- **Agreement (owner, 2026-10-21): a core record, deferred to e-billing R2.** An agreement can sit under a matter or project, or stand alone. It is not urgent, so it goes to R2 rather than reopening completed work. Code today treats `sprk_agreement` as a child of a matter or project (`CoreAncestorResolver.cs:212`); a stand-alone agreement as a root is uac-r2's core-record model to change. v5 lists the R2 extension points, and R1 keeps the core-record set in one list.
- *(Earlier wording, superseded:)* today `sprk_agreement` is a child of a matter or project in `CoreAncestorResolver.cs:212`, not a core record. Adding it means a schema step and one list entry; the resolver walk then treats agreement → matter/project as distance 1. v5 records this as the extension path, not R1 work.

**`sprk_family` (owner question, 2026-10-21).** No table named `sprk_family` exists in v4, the repo, the peer worktrees or dev Dataverse (checked 2026-10-21). Two things in our notes could read that way:
- **v1's "Family" lookup.** v1 proposed one Procedure table whose rows were grouped into a "family" (all versions of one document). That was withdrawn in v3 (O-15) for two descriptively named tables: **Procedure** (`sprk_procedure`, the document) and **Procedure Version** (`sprk_procedureversion`, each issue with its file and effective dates). No family table remains.
- **"Secure family."** uac-r2's term for the set of child tables that inherit a Secure record's isolation (lineage, role row, census). It is a concept in code (`SecureChildLineage`), not a table, and v5 will say "secure child tables" to avoid the word.
If the owner saw `sprk_family` somewhere else (a prototype or another project's design), we need the source to answer.

### 4.x Owner answers on the Submit Invoice process (I, L), 2026-10-21

Given in word-add-in-r1's session and relayed to us; checked in `projects/spaarkeai-word-add-in-r1/notes/ebilling-r1-review-2026-10-21.md` lines 66-75 before recording.

1. **Explicit mark.** Each attachment is Invoice (exactly one; pre-selected when exactly one PDF), Supporting (default) or Don't save. The server never guesses and refuses without exactly one Invoice. If extraction finds the marked file is not an invoice, the submission fails with a clear message.
2. **Separate Invoice module** in the add-in (ribbon "Submit Invoice", task-pane view like Add To Do). New invoice: core record required (Matter, Project, Work Assignment; pre-filled when the email is filed). Existing invoice: add supporting files, or submit a corrected invoice: a resubmission (D-34/D-37/D-43) that closes the old invoice as superseded and links the new one ({invoice}.{n}). No in-place PDF replacement. A correction is refused once Approved, Sent to AP or Paid (credit or adjustment instead; word-add-in-r1's recommendation, owner ok).
3. **Removed from Save:** Invoice as a save target (`OfficeDocumentPersistence.cs:47`) and the blank "+New invoice" quick-create (`QuickCreateRequest.cs:180`).
4. **Core records:** Matter, Project, Work Assignment in R1; Agreement with ebilling R2 (O-31); the add-in takes the list from ebilling.
5. **The email is always saved** as its .eml in SPE, a Document filed to the core record, never an invoice file; only attachments go to the invoice's files. The save must not create the attachments as child Documents; ebilling defines the contract.
6. **LEDES is out of scope.**

Applied in model v5.5 (section 0 v5.5, 9.1, 13, 15). The contract word-add-in-r1 asked for is in 9.1 ("Email-only save").

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

**uac-r2's response (2026-10-21): the plan is right, with three conditions (all accepted for v5).**
1. A fault in the re-run (exception, throttle, timeout or an unreadable flag) is a **failure** and triggers the undo. It is never read as a pass.
2. The Restricted, internal-only and privacy reads also go into the **pre-transaction** check, so a source that is already isolated is refused up front instead of committed and undone.
3. **Undo is complete or held:** delete the Stored File row and blob, then the approval, then the lines, then the invoice. If any delete fails, the submission stays Failed and the file stays not-yet-servable until compensation finishes.

**Extracted fields after a later source change: UAC position, confirmed by the owner 2026-10-21 ("yes invoice stay readable"); a known limit in v5.** They do not follow the source. Once the re-run passes, the invoice is its own business record under the core record: its readers are the core record's readers, narrowed by the invoice's own Access Permission *(superseded: `sprk_invoice` has no such column; see §9 F4-1 and (a′))*. Disclosure happened at submit, as with a download (uac-r2 K2). To hide an invoice later, set its own Access Permission to Restricted *(superseded by (a′): mark the source document Restricted or the communication internal-only)*; D7 and uac-r2 task 191 then hide it from flagged-external users.

**For uac-r2 task 191:** v5 names each new table and the root column it hangs from, for all core types. uac-r2 updates its delete design §12 to keep record-scoped rule-set rows as secure child rows.

## 8. ontology-r1 scoped re-check of v5 (70fc2cc616), 2026-10-21

Verdict: **FINDINGS, nothing blocking.** 2b and O1 resolved; 9a's intent carries to the record-scoped set. Patch v5 before the POMLs (folded into v5.1 together with uac-r2's re-review).

- **F2-a (§5.2, I-P5).** The record policy version's "based on" link to the base version, and everything under a record-scoped set, must be **RemoveLink**; state that no Restrict is reachable from a core-record delete (D-172, #2132).
- **F2-b (§5.2, §6.3).** After RemoveLink a record-scoped set has no lookup. State that a record scope with no lookup applies to nothing (the loader requires scope plus a non-null lookup); the sweep is cleanup only.
- **F2-c (§6.3, §7.1).** An override has no firm, so a change made for firm F would exclude the base rule for every firm and apply the record rule to firm G too, contradicting D-175 C. Fix: the Exclude row and the record-scoped set carry the firm, using the scope values below. Add the question to §15.
- **F2-d (§7.1).** The gap rule fires for any Active Procedure with no version in force. State whether a Draft-only Procedure counts as Active, and whether a future-dated v1 means "not applicable yet" or a gap. (Our proposed reading for the owner: Draft-only is not Active; a Procedure applies from its first version's effective date, so earlier work is "not applicable", not a gap. A gap is a hole between versions, or after an end date with no successor.)
- **F3-a.** The option sets (Type, Applies to, Status, Purpose, version status) are global and start at 100000000.
- **F3-b.** Name the published-marker column and its values; the pointer ratchet is a baseline-plus-controls ArchTest.
- **F3-c.** Restate the ExpenseRule split (caps replace, checklist adds) in §7.3, since §12 retires the file it points to.
- **F3-d.** Type name: we record "Internal Standard" (owner in this session); ontology is re-confirming with the owner because its D-175 holds its own relay.
- **F3-e.** List the lineage edges agreed with uac-r2 (`sprk_ruleset` → core record, `sprk_policy` → set, policy version → policy, plus the Procedure tables) and write out the rewritten census guard with must-fire and must-not-fire controls.
- **F4-a.** §8.1 "Billing instructions for this matter" → "for this record".
- **Answers to our §15:**
  1. **Marker:** reuse `sprk_ruleset.sprk_scope`, no new boolean; drop `sprk_isexceptions`. Add **Record = 100000002** (every firm on the record) and **Firm record = 100000003** (one firm, existing `sprk_firm`), as D-171 and the 080 notes planned. Term: "record-scoped rule set" (ontology adds it to component-model §3).
  2. **Retiring one Procedure Version:** ontology is asking the owner. Its recommendation: Superseded = no re-check; Retired/withdrawn = re-check pending and In Review invoices whose snapshot cites it (the gap hold then applies); decided invoices keep their snapshot.
  3. **AddedFee:** the reference is ebilling's D-27 (closed typed templates). `InvoiceExtractionToolHandler` returns the header due date but no per-line fee kind. So the validator refuses "not allowed" and "after N days" until extraction returns both, with a test on each; the cap form can ship now.
- **Matter-only items:** §8.1 wording; §13 must use the shared core-record list, not three names typed by hand; confirm the Billing tab and record setup exist on project and work-assignment forms (§3.2, O-19); confirm `sprk_assignedlawfirm1/2` exists on all three core types (§3.3).
- **Checked on dev, 2026-10-21 (read-only):** `sprk_assignedlawfirm1/2` (→ `sprk_organization`) and `sprk_billingapproverrole` exist on `sprk_project` (describe) and `sprk_workassignment` (a query selecting them is accepted). Whether the Billing tab and record setup exist on the project and work-assignment forms is still to check, in the 080 rewrite.

## 9. uac-r2 full re-review of v5 (c6c5cb0c3b), 2026-10-21

Code checked at 70fc2cc616 and master a10e814d02. **Verdict: ACCEPT WITH CHANGES, provided (a′) replaces v5 §15 (a). If (a) is adopted as written: REVISE.** All items below are accepted and folded into v5.1.

- **F1-1. Derived rows of an isolated source.** The gap is **external callers only**: Restricted means only "no flagged-external share" (I-14), and internal callers pass the 065 rule, so the core record's internal readers are not a leak. Exposure: the firm's contacts (header, lines, findings DTO); another firm's flagged-external user on M (036 and AI tools); under 191, the Stored File row mirrored to them.
  - **(a) is the wrong mechanism:** it stamps at commit (contradicts D7's "derived at read time"); it never heals when `LowerWhenSafeAsync` lowers the communication; `ChildAccessPermissionReconciler` writes I-16 display copies recomputed from M every 2 minutes and would overwrite it (and I-16 says no decision reads them). Reviewer/approver grants are unnecessary: Restricted removes no internal access.
  - **(a′) Derive at read time.** The invoice is hidden from every caller who is not an internal systemuser while its Submit Invoice source fails D7 rules 2–4 (the source document's own value, any Restricted ancestor, or the 065 communication rule) or the effective Restricted state of any of the source's roots. Use the existing predicate: `ExternalDocumentNarrowing`, then 191's `ExternalVisibility`; **not rule 1** (roots), because a later refile must not hide the invoice from its own firm (O-32). Declare it as `sprk_invoice`'s **`ExternalMarker` predicate, not a lineage lookup** (a lineage lookup would join the secure intersection). Taint closure carries it through `SecureChildLineage.cs` edges (:74-77, :142-165) to lines, approval, submission, correction, external identifier, Signals, Decision Records and the Stored File. Surfaces, now (not only after 191): SPA invoice list and module, firm findings DTO, firm file route (invoice-level, same 404), external invoice-index retrieval, AI tools.
  - Sub-answers: **store nothing** (if the owner insists on storing: Restricted; never Limited; no new marker). **A Private thread needs nothing more** (internally Private narrows only chat overlays and fan-out; externally the 065 rule treats it as internal-only). The copy stays Firm-visible = No; the invoice is hidden from the firm while its source is isolated and **heals automatically** when the source is lowered (`CommunicationInternalOnlyFloor.cs:131-154`). Verify step A's filing resolves the thread before the pre-check.
  - **uac-r2 takes to the owner:** "the firm doesn't see its own invoice while the source is internal-only" (touches "it's their invoice").
- **F1-2. Table depth.** `sprk_storedfile` is one table with one depth: **Deep**, and on the NFR-05 list. Procedure and export file rows are listed and served only by the BFF after the parent check, with no subgrid.
- **F1-3. Upload refusal.** §4.3 Upload gets a **step 0 refusing `sprk_isexternal` callers** (a fault also refuses), for every parent kind (else a flagged-external Core User could feed `BillingRuleDraftJobHandler`).
- **F1-4. Firm match for systemusers.** §9.3's systemuser path has no `sprk_vendororg` match: firm G's flagged-external user on a matter shared with co-counsel could list and download firm F's Firm-visible PDFs. Fix: a flagged-external systemuser gets the firm match through **their linked contact's organisation**, fail closed with no link; internal users unchanged. Corrects uac-r2's earlier "no new relation needed". **uac-r2 takes to the owner.**
- **F1-5. Submission-row undo.** Step 5's undo also clears `sprk_FirmInvoiceNumber`, `sprk_PONumber`, `sprk_FindingsJson`, `sprk_Invoice` on `sprk_invoicesubmission`; keep only status Failed, the admin note and the correlation.
- **F1-6.** Delete "(Firm-visible = No unless it is the firm's PDF)" from the §9.1 Internal upload row; it contradicts §4.1.
- **F2-1. SHA check.** Inside the If-Match flip, check the blob exists and its SHA matches (F2-a's check, dropped in v5).
- **F4-1.** Remove every reference to "the invoice's own Access Permission" (`sprk_invoice` has none). Under (a′) the remedy is to mark the source document Restricted or the communication internal-only.
- **F4-2.** "Apply T7" on the internal route (§4.3 step 3, §9.1 B) is a no-op. The copy rule is **the caller's own Read on the source document, gate variant**.
- **F4-3.** The §5.2 census needs an `ExternalMarker` per table (191 forces one at compile time): `sprk_procedure`, `sprk_procedureversion`, `sprk_procedureassignment`: **Never** (procedure and version: also no privilege in the External Collaborator role). `sprk_storedfile`: by parent (Procedure Version: Never; Export: Never until §15 is answered; Invoice: follows the invoice, and Firm-visible = No hides it). Record-scoped `sprk_ruleset`, `sprk_policy`, `sprk_policyversion`: Never (firms see clauses only through the DTO). `sprk_invoice`: the (a′) predicate. Also state the `sprk_policy → sprk_ruleset` lineage edge, and that record-rule policy creates route through `IRecordOwnershipResolver`.
- **F4-4.** Step 2's deterministic file id conflicts with step 4's re-stage under a new id: **id = f(submission id, source version)**.
- **F4-5.** Name the delete lists: **hard-deny** procedure, procedure version, stored file; **Delete** assignment; **unlink-only** record-scoped rule set.
- **Answer B (deleted source).** Not "strictest isolation last read" (no one defines that stamp; contradicts §13 and round 111). `sprk_storedfile.sprk_sourcedocument` and `.sprk_sourcecommunication` are **Restrict**. The BFF single-document delete answers **409** when a Stored File references the document (pattern: `document_has_child_documents`). Invoices block the core-record delete (D-120). If S's delete preview finds a document referenced by an invoice file, it shows a block and never unlinks. A communication delete only deactivates the communication, so the copy rule still reads it.
- **Version retire: no access issue, with conditions.** BFF-only write (Status is field-secured); needs Write on the Procedure **plus a billing-admin privilege** (it can hold invoices on any record, Secure ones included); the re-check's writes go through `IRecordOwnershipResolver`; the response never counts or lists invoices the caller can't read. Same for publish and date changes.
- **K-1.** Org-level Read on procedure and procedure version reaches flagged-external users in the MDA until 191 deploys: keep the grant out of roles they hold, or record a 191 window.
- **K-2.** The billing lookups are verified not access-conferring, but discovery is opt-out (`MembershipFieldDiscoveryService`; `sprk_organization` is an identity table, `MembershipOptions.cs:257`), so `MembershipReconciliationJob` will write junction rows for both columns. Add a **pin test** (the registry has no `billing*` column → `AccessConferringOnly` confers nothing), optionally per-entity `ExcludedFields`.
- **K-3.** Blob storage still has `defaultAction: 'Allow'` (`storage-account.bicep:55-66`) and an account-scope role (`:171-179`). Do the **container-scoped role** now (cheap). Network Deny with a private endpoint: do it, or the owner accepts a known limit. **uac-r2 takes to the owner.**
- **K-4.** Step A's communication filing survives an undo; it is the caller's own authorized filing. Say so in the admin note.
- **§0 status against v4.** Closed: F1-a–f, F2-b, F2-c, F4-a, F4-c, K1 order and condition 1, K2–K6, Q1, Q4, Q6. Half-closed (now closed by this round's items): F2-a (F2-1), F4-b (F4-5), condition 2 (F1-1), condition 3 (F1-5), Q5 blob items (K-3). Dropped: none.

## 10. ontology-r1 re-check of v5.1 (ae690ec8a4), 2026-10-21

**PASS.** All ontology findings are applied. The two owner points (whether "no automatic re-check" covers every case; the D-170 sweep on a Procedure retire) are with the owner, and leaving the sweep as it is meanwhile is fine with ontology. Ontology is ready for the 080 rewrite once the owner finalizes.

## 11. uac-r2 check of v5.1 (ae690ec8a4), 2026-10-21

**Verdict: CHANGES NEEDED, in the banner design only (§8.2, I-P5); text changes.** All 19 earlier UAC items are closed; F4-4 was half closed (N6). Applied in v5.2.

- **N2 (F2).** Override delete/retire plus banner writes on N invoices are not atomic; a throttle leaves invoices unbannered with no detector. Options: (a) compute at read time, store nothing (recommended); (b) an idempotent ADR-004 job with retry and a detector. **v5.2 takes (a)**, generalised to comparing the current resolution with the snapshot on the invoice's own read.
- **N1 (F1).** Scope too narrow: a project's override reaches work-assignment invoices at distance 1 (§6.4). Affected = open invoices on the record and its distance-1 children whose snapshot cites the override or the record rule it switched off, limited to the override's firm. Under (a) this is the snapshot test.
- **N3 (F1/F4).** Nothing kept the banner from firms or flagged-external users (I-P2's "Read for every user" field security would show it in the MDA; reveals Procedure activity, D-66, O-27). Under (a): compute for internal callers only; out of the firm findings DTO and the SPA invoice projection; in R3's negative test.
- **N4 (F4).** "Goes through IRecordOwnershipResolver" does nothing (it resolves owners on create and reparent only, `RecordOwnershipResolver.cs:102-130`); and §0 row 11 / I-P5 vs §8.2 disagreed on the caller check. Moot under (a); text removed.
- **N5 (F4).** §9.1 known limits: "but not its roots" → "but not rule 1 (which roots the source is filed under)", so the roots' Restricted-state term stays.
- **N6 (F4).** A mailbox attachment has no source version: file id = f(submission id, the attachment's identity, or a fixed 0).
- **N7 (F4).** Removing a changed-rule override switches its record rule Off, and D-170 then closes open findings automatically: widen §15(b) beyond Procedure retire.
- **N8 (owner wording).** "A new procedure … has been published" is wrong for override removal and retire, and publish of a first version or adding an override set no banner. Suggested neutral text; list triggers or take the gaps to the owner. Under v5.2's full comparison every trigger is covered; the text question is in §15.
- **N9 (K).** The override and Procedure services must refuse `sprk_isexternal` callers explicitly (step 0, a fault refuses), not only through the role.
- **Conditions if the banner had been stored** (app-only marker write, never count or list, N3, N1 scope, N9) mostly fall away under (a).
- **Stray wording:** reviews §7 (~:300) "invoice's own Access Permission" (now marked superseded); v5 §0 row 9 "through T7" (kept as history with a note).
- **Owner round 116 (uac-r2 session27 notes), 2026-10-21:** F1-1's effect accepted (the invoice and everything derived from it are hidden from external callers, the firm included, while the source is internal-only or Restricted; reappears when lowered) and F1-4 confirmed (flagged-external systemuser firm match through the linked contact; no link → none). Both applied in `480d6fa027`. **K-3** (network Deny with a private endpoint, or a known limit) is still open; the container-scoped role is done regardless.

## 12. uac-r2 re-check of v5.2 (b5872265be), 2026-10-21

**ACCEPT-WITH-CHANGES.** The read-time banner design is sound; no design point reopened. N1, N4, N5, N6, N7, N8 closed; N2, N3, N9 partly closed via C1–C5. All applied in v5.3.
- **C1.** No snapshot (pending) or a pre-v5 snapshot → a distinct "Not yet checked" state, never "no change". `BILL-SYSTEM-SUBMISSION`-only invoices exempt.
- **C2.** The comparison runs app-only, like the evaluation (the 6.6 read models read as the caller, which would give a false banner or `0x8004d24d` for a user who cannot read a d1 parent). One banner state only, no diff, names or ids. K: one bit about a Secure d1 parent.
- **C3.** One resolution per invoice; each row read once; work dates in memory; test that query count does not depend on line count; once per page load on one named route; 429 or timeout → "could not be checked".
- **C4.** The internal single-invoice reads (`/api/finance/invoices/{id}/review` `InvoiceLineReviewEndpoints.cs:39`, `/context` `InvoiceContextEndpoints.cs:28`, `/api/finance/invoice-submissions/{id}` `InvoiceSubmissionEndpoints.cs:74`) are gated only by `AddFinanceAuthorizationFilter`, and `FinanceAuthorizationFilter` has no `sprk_isexternal` check. Only the review route computes the banner, only when the flag reads definitely false. Under owner round 113 the review route would also carry the 7.4 basis (override reasons, Procedure names) to flagged-external systemusers: step 0 on those Finance reads. **Our choice: in R1** (v5.3 §12, §13); uac-r2 confirms.
- **C5.** Step 0 on every Procedure and override service operation, reads included; a fault refuses.
- **C6.** K-3 wording everywhere: accepted as a known limit (owner round 117).
- **C7.** Round-116 leftovers: (a′)'s effect accepted, covering Restricted as well as internal-only; F1-4 confirmed.
- **Probes (no action beyond recording):** a publish/read race is benign since nothing is stored; a non-atomic publish can briefly show a false banner or fault into "could not be checked" (recorded as K); the comparison's fault log carries ids and counts only.
- **Owner round 117 (K-3)** is recorded in uac-r2's session27 notes at `eff5f6c714`.

## 13. uac-r2 re-check of v5.3 (cbee545d41), 2026-10-21

**ACCEPT-WITH-CHANGES.** C1, C2, C3, C6 and C7 closed; C4 and C5 partly closed. All applied in v5.4 (section 5.3 of the model).

- **Step 0 definition:** resolve the caller's systemuser from `oid` (no row → refuse); `sprk_isexternal` true → refuse; **false or blank → internal** (round 67; `RecordContainerResolver.cs:2400`, `SpeContainerMembershipSync.cs:235`); a fault reading the flag → refuse. "Definitely false" would have hidden the banner from every internal user with a blank flag. CIAM contacts never reach these routes (workforce scheme 401, `AuthorizationModule.cs:47-55`).
- **Placement:** inside `FinanceAuthorizationFilter`, before any record read, one route-group flag, the route's own `Deny()`: 403 on `/review` and `/context`; `UniformNotFound` on `/invoice-submissions/{id}` and `/blocked`. Not in `InvoiceSubmissionService` (shared with the external submit route).
- **Scope (group rule, owner round 116 item 2):** every `/api/finance` route that reads or writes invoices or submissions, adding the missed `GET /api/finance/submissions/{submissionId}/blocked` (`SubmissionOverrideEndpoints.cs:40`; `BlockedFindingView` plus the override stamp with reviewer reason and id), the line-decision, accept-clean, related-work, override, return and internal submit routes, plus `/api/finance/rulesets/*` (`RuleSetService`; record-scoped rule sets hold override content). Out of scope: `/invoices/search`, `/matters/{id}/summary`. `/invoice-submissions/{id}` is in scope by the group rule only: it returns firm-visible facts (`InvoiceSubmissionContracts.cs:184`).
- **Nothing breaks:** none of these routes is on master; no client calls `/review`, `/context` or `/invoice-submissions/{id}`; RuleReview calls only `rulesets`; the external SPA uses `/api/v1/external/*` only.
- **Leftover:** the 081 row's "banner comparison on the invoice GET" → "on `/review` only".

## 14. uac-r2 review of v5.5 (`13b310e1c4..f66eec5e25`), 2026-10-21: CHANGES-NEEDED

Answers to our two questions: (1) yes, (a′) changes on the .eml; (2) yes, new exposure.

| Finding | Summary | v5.6 |
|---|---|---|
| F1-a | The add-in .eml, filed to the core record and partner-visible on an Open thread (owner D6, `CommunicationInternalOnlyFloor.cs` `AddInSaveWithoutReplyHeadersIsShareable`), exposes firm F's invoice to every external participant; narrowing it hides the invoice from its own firm via (a′). | Owner decision, section 15 (options i/ii/iii; i recommended) |
| F1-b | "Task 130 unchanged" holds only after #2177 is merged and deployed; refuse when no communication resolves. | Applied: prerequisite and refusal |
| F1-c | An unidentifiable email saves Restricted and silently hides the firm's invoice. | Owner decision, section 15 (refuse recommended) |
| F2-a | `from-email` must carry `/office/save`'s filters (`OfficeEndpoints.cs:185-195`) plus Finance's, step 0 first; overwrite `SelectedAttachmentFileNames` with `[]`. | Applied |
| F2-b | Resolve the .eml from the communication (Message-ID, `sprk_relatedcommunication`, exactly one), not a returned id. | Applied (replaces word-add-in-r1's `JobResult.Artifact.Id`) |
| F2-c | The job's byte read: one retrieve of drive/item/version; ordinal, name and SHA match; version pinned; id-only logs; failure handling. | Applied |
| F1-d | Correction: re-check inside step 4 with If-Match on the old invoice; undo reopens it; Write on the old invoice, same `sprk_vendororg` and core record; (a′) does not follow the link. | Applied |
| Q1 | (a′) rule 4 on the .eml is its `sprk_relatedcommunication`'s internal-only flag; evaluate rule 3 too. | Applied |
| Minor | The Email attachment row contradicted the add-in flow; v5.4's table was stale. | Applied |

Checked clean by uac-r2: `[]` means no children; invoice text stays out of email analysis.

**Owner picks (2026-10-21, given in this session and in uac-r2's round 118):** F1-a **option (i)**: the `from-email` .eml is own-Restricted with a submission marker written in the same create, by the submission path only (ArchTest), that (a′) skips. F1-c **refuse**: no communication resolves → retryable refusal; no .eml, no submission. Applied in v5.7.

**word-add-in-r1 on v5.6 point 1:** "exactly one Document with that `sprk_relatedcommunication`" fails after #2177 (attachment children and several .eml Documents link to one communication). v5.7 resolves candidates by `sprk_isemailarchive = true`, then the target core record with no parent, then the marked attachment's SHA-256, refusing retryably on none or several. Code checked: `OfficeDocumentPersistence.cs:327`, `UploadFinalizationWorker.cs:684`, `GetEmailArchiveByCommunicationAsync` (`DataverseServiceClientImpl.cs:1152`, first only).

## 15. uac-r2 re-check of v5.6/v5.7 (`f66eec5e25..ef9b1a9e9b`), 2026-10-21: ACCEPT-WITH-CHANGES

Every v5.5 item closed (F1-a, F1-b, F1-c, F1-d, F2-a, F2-b, F2-c, Q1). The reused-.eml known limit is accepted (same user's own duplicate; random MIME boundary, `OfficeEmailEnricher.cs:255-365`; SHA check covers a missing part). No new owner decision.

| # | Change | v5.8 |
|---|---|---|
| 1 | Marker on the .eml, not the Stored File (undo would delete it; retry then hides the invoice permanently). 080 settles `ParentLineage.RefuseAppOnlyWrite`, the field-mapping skip and field security; the ArchTest also bans the marker in update payloads. | Applied |
| 2 | "A user's later Restricted hides it" is false for a marked .eml; hide via the communication's internal-only flag; K limit: lower-then-raise leaves the marker. | Applied |
| 3 | "Several" can't be retried away (another user's marked .eml of the same email); use the created .eml on a fresh create, only the caller's candidates on the duplicate path. | Applied |
| 4 | Retryable only for Graph timeout / 429 / 5xx and transient Dataverse errors; named permanent messages otherwise. | Applied |
| 5 | Stale text against F1-c and own-Restricted (step 1, "Retired in v5.5" paragraph, "Capture first", "Keys"). | Rewritten or removed |

uac-r2 checks the v5.8 diff only; then the model is ACCEPT from UAC.

**uac-r2 on v5.8 (`eca8b52258..adb15bdb65`), 2026-10-21: ACCEPT from UAC.** All five items in. Two stale parentheticals from the retired "no source document" branch (model :334, :651) removed afterwards, no re-check needed.
