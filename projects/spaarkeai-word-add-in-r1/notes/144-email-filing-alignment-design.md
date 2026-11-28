# Task 144 design — an email's Communication, Documents and record always move together

> Status: DRAFT for review (uac-r2, field-mapping-r1, then the owner). 2026-11-27.
> Inputs: [`144-email-filing-alignment-inventory.md`](144-email-filing-alignment-inventory.md) (write paths, the nine-point review against master 60933c82be, corrections). Facts below are file:line on that master; re-locate by name before acting.

## 1. The rule (owner, 2026-11-27)

> "The Communication<>Document<>Record should always follow each other."

The owner's nine points, condensed:
- Inbound mail to a mapped mailbox creates a Communication and an .eml Document (with its attachments as Documents).
- Setting Related to anywhere (Email Reconciliation, the Communication form, the Outlook add-in) files the Communication AND its Documents to that record.
- Changing it anywhere moves all of them.
- Attachments saved earlier on their own follow when the email is saved.
- Outlook shows the email's status whenever the email exists in Spaarke.

**Invariant E-1 (proposed name):** for every email Communication C, every *email document* of C has exactly C's filing in its record lookups, at all times after the write that changed either one completes.

An *email document* of C is a `sprk_document` that is either:
- an archive (`sprk_isemailarchive = true`) with `sprk_relatedcommunication = C`; or
- an attachment of C: `sprk_relatedcommunication = C` (inbound, send-time, standalone Office attachment saves), or `sprk_parentdocument` = an archive of C (Office save children).

Not in scope: documents a user filed to a record that are not email documents (Word documents, uploads). A document linked to C only through `sprk_relatedcommunication` by a user's explicit link is an open question (§6, Q5).

## 2. What is true today (summary of the review)

| Owner point | Today |
|---|---|
| 1–3: inbound creates C + .eml; Reconciliation / the form file C | Works (`IncomingCommunicationProcessor`; `PATCH /api/communications/{id}/filing`) |
| 4–5: the Documents are filed with C and follow a change | **No.** No path writes a record lookup on an email document when C is filed. Inbound archive and attachment documents never get one (`ArchivedEmlDocument` ~:1584-1612, attachments ~:1325-1337). The re-file re-owns and re-shares them (`SecureChildReconciler`) but never writes their lookups (`CoreAncestorResolver.StampSourceColumns`: "a document … carr[ies] none"). |
| 6: Outlook save files C + .eml + attachments | Works, except: C already filed to X and the user picks Y → C stays on X, the documents go to Y, and the pane says nothing (`ReconciledEmailFiling` never moves a filed C; the outcome `AlreadyFiled` shows no notice) |
| 7: Outlook shows the status | Works when an .eml exists (own record, else C's, Read-checked). Not when only C exists (a plain first save with engine suggestions). When the .eml and C disagree, only the .eml's record is shown |
| 8: changing Related to in Outlook moves both | **No.** No re-file control on a saved email. "File to record" (unfiled only) writes the document alone (`PUT /api/v1/documents/{id}`) |
| 9: attachments saved earlier follow | **No.** The email save leaves them as they are and creates a second child copy of each ticked attachment |

## 3. Design

### 3.1 One authority and one owner

- **The Communication's filing is the authority.** It already carries the richer model: several regarding lookups (`RegardingFieldMap.All`, 12 columns), the ADR-024 primary pair, `sprk_associationstatus`, the thread, and access derivation. Its email documents carry a copy.
- **One server-side owner of E-1:** a new step, working name `EmailDocumentFilingMirror` (BFF, `Services/Communication`), with one operation: `AlignAsync(communicationId)`. It:
  - reads C's filing;
  - lists C's email documents (both link shapes in §1);
  - writes each document's record lookups to the mirror of C's filing (§3.2), as the BFF app, like the reconciler already re-owns them;
  - marks each changed document for re-index of its parent fields (§3.6).
- It is idempotent: a document already aligned is not written. One fact, one answer, no plugins (ADR-002), the write-path architecture's "one server owner per invariant".

### 3.2 Mapping C's filing onto a document

A Communication can be filed to more than one record (primary plus server-set secondaries such as `AlsoRegarding`, task 176). A document has the 4 direct lookups (`sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment`), 12 `sprk_related*` lookups and `sprk_email` (`DocumentLinkFields.All`, `Spaarke.Dataverse/Models.cs` ~:483-515).

Proposed mapping (owner question Q2):
- C's primary regarding → the document's direct lookup of that type, when the type has one (matter, project, invoice, work assignment). Otherwise → the matching `sprk_related*` lookup (event, contact, organization, …).
- Every other regarding of C → the matching `sprk_related*` lookup.
- A lookup the mirror owns that C no longer has → cleared. `sprk_relatedcommunication`, `sprk_parentdocument` and `sprk_email` are never touched.
- The mirror owns all 16 record lookups on an email document. A user cannot file an email document separately (§3.4).

### 3.3 Every path that writes C's filing calls the mirror

| Path | Today | Change |
|---|---|---|
| `PATCH /api/communications/{id}/filing` (Email Reconciliation, review panel, Communication form, RegardingResolver PCF) → `OwnedChildWrite.RefileAsync` → `AfterRefileAsync` | re-files C, restamps to-dos/events, re-owns documents | call `AlignAsync(C)` in the same after-write step (uac-r2's path) |
| Inbound association (`IncomingAssociationResolver`, auto-file and Suggested) | files C before any document exists (steps 4.5/4.6 vs 5/6) | the document builders copy C's filing at create (§3.5) |
| `ReconciledEmailFiling` (Office save of an email whose C exists) | files an unfiled C; never moves a filed one | §3.4 Outlook |
| `CommunicationService` send (`MarkFiledBySender`, outbound archive `StageArchiveRecordLink`) | the outbound .eml copies C's record; send-time attachments do not | attachments copy too (§3.5) |
| Thread continuity (`ThreadContinuityRung`, `ReconcileThreadForFilingAsync`) | a reply inherits the parent's regarding; the thread re-anchors | no change: each reply is its own C, aligned by its own create/re-file |
| Unfiling (`PATCH …/filing` with nulls) | supported for C; documents keep nothing (they have no lookups) | the mirror clears the documents' lookups |

### 3.4 Every path that writes an email document's filing goes through C

- `PUT /api/v1/documents/{id}` on an email document (re-file, "File to record" in the add-in): re-file **C** instead, through the same rules as `PATCH …/filing` (Write on C, AppendTo on the target, F3 to leave a secure record), then `AlignAsync(C)`. Proposal: the route detects an email document and delegates; the caller's rights on C decide (owner question Q4: delegate, or refuse with a pointer to the Communication).
- The Office save (`OfficeDocumentPersistence`, `UploadFinalizationWorker`) stops writing its own record onto an email document of an existing C; it files C (new or existing) and the documents get C's filing from the mirror. For a NEW capture this is the same record, so behaviour is unchanged.

### 3.5 Documents created or linked after C is filed copy C's filing

Every creator or linker of an email document copies C's current filing in the same write:
- inbound archive and attachments (`IncomingCommunicationProcessor` builders);
- send-time attachments (`CommunicationService` ~:709-720), the outbound archive already does;
- Office save children and standalone attachments linked to an existing C;
- `CrossPathLink.FindAndLinkArchiveDocumentsAsync` and `EmailArchiveCommunicationLinkSweepJob` when they link an archive to C;
- `MessageAttachmentMaterializer` (ACS).

A safety net catches a missed write or a race: the mirror also runs from `SecureChildReconciliationJob`'s recent-changes pass (every 2 minutes), or a pass of its own, over communications whose filing changed. The same pass with `Apply` is the backfill for existing rows (owner-run, dry run first).

### 3.6 Side effects the mirror must carry

- **SPE containers.** Re-filing can mean the file belongs in another container (a secure record has its own). `DocumentContainerRelocator` already moves files whose row points elsewhere, from the reconciler pass, with its guards: the widening guard, F3 between secure records, checked-out files, unfiled files in no BU container. The mirror writes the lookups and lets the relocator move the file. A document may be refused under the strict container rule for up to about one pass (~2 min), as for any re-file today.
- **Search index.** Chunks carry `ParentEntityType/Id/Name` (`KnowledgeDocument`). A re-file inside the same container re-indexes nothing today, so Find keeps the old record (a pre-existing gap for every re-file). The mirror queues a parent-field refresh for each document it changes (reuse the indexing pipeline's `ReplaceParentEntity`; `documentId` on every call).
- **Ownership and shares** keep following C through `SecureChildReconciler`, unchanged.

### 3.7 Outlook add-in

- **Status (point 7):** the identity answer reports C's filing when only C exists (no .eml), and the pane shows "This email is in Spaarke, filed to {record}" with Save offered for the .eml and attachments.
- **Change Related to (point 8):** the saved view gets "Change" next to "Filed to". It calls the communication re-file (same route as Email Reconciliation), with a confirm when C is filed by someone else or the move leaves a secure record. The documents follow through the mirror. "Save again as a new document" stays.
- **A save with a different record than C's (point 6 split):** the save files the new documents with C's filing, and the pane says "This email is filed to {X}. Change it here or in Email Reconciliation." It never silently moves C on a save (owner question Q3).
- **Attachments saved earlier (point 9):** they are email documents of C (task 143 links them to C), so the mirror files them when the email is saved with a record. The email save skips creating a second copy of a name already saved as an email document of C.

### 3.8 Where this crosses other projects

| Area | Owner | What changes |
|---|---|---|
| `OwnedChildWrite.RefileAsync` / `AfterRefileAsync`, `SecureChildReconciler`, `DocumentContainerRelocator`, `PUT /api/v1/documents/{id}` | uac-r2 | call the mirror; document re-file of an email document delegates to C |
| `OfficeDocumentPersistence`, `UploadFinalizationWorker`, the archive create in `CommunicationService` | field-mapping-r1 (W3) | email documents take C's filing, not their own; copy at create |
| `IncomingCommunicationProcessor`, `CrossPathLink`, sweep jobs, send | communications (closed; fixed here, as ISS-043 was) | copy at create/link |
| Indexing pipeline | AI | a parent-field refresh call |
| Add-in, `ReconciledEmailFiling`, identity route | this project | §3.7 |

## 4. Known edge cases

| Case | Handling |
|---|---|
| One attachment document shared by two emails (Office content dedup returns the first email's document; `sprk_relatedcommunication` is single-valued) | It belongs to the first email and follows it. The second email's save reports "Already in Spaarke" (task 143 A1) and does not re-file it. Inbound attachments are never deduplicated, so this arises only from Office saves |
| Several archives for one C (save-as-new copies; 6 such communications on dev, #2225) | All are email documents of C and all follow |
| C is Suggested (engine guess, lookups written; `IsFiledToRecord` counts it as filed) | Owner question Q1 |
| A secure record on either side | Existing F3, widening guard and relocator rules apply unchanged; the mirror never bypasses them |
| The user may re-file C but cannot write its documents | Same as today's ownership follow: the documents follow as the app, because the decision is on C |
| Invoice submission emails (`sprk_restrictedbysubmission`) | Excluded from the mirror until ebilling confirms (their restricted narrowing owns those documents) |
| A matter deactivated or deleted | No cascade today for C either; out of scope |
| A document with no C (Restricted, never linked) | Not an email document; out of scope |
| Thread replies | Each reply is its own C; no cross-message re-file |

## 5. Tasks (proposed split)

1. Server mirror + E-1 guard tests (this project, with uac-r2 review): `AlignAsync`, called from the re-file after-write step and the reconciler pass; unfile; index refresh.
2. Create/link copy (with field-mapping-r1 and the communications paths): inbound, send, Office save, linkers.
3. Document re-file delegates to C (uac-r2).
4. Add-in: status for C-only, Change Related to, the split-save notice, attachment follow and no duplicate (this project).
5. Backfill dry run → owner-run Apply.

## 5a. Independent "what is missing" review (Fable, 2026-11-27) — findings and design changes

Read-only review against master 60933c82be. Each finding changes the design as stated; §3 is superseded where they conflict.

| # | Class | Finding | Design change |
|---|---|---|---|
| R1 | RISK, access | `DocumentUrlIdentityResolution.cs` ~:508-523 trusts a document's own slot as readable and Read-checks only the derived (communication) record. A mirrored slot would show a matter to an email participant who cannot read it | Mirrored slots are derived: every reader that treats "own slot ⇒ record readable" must Read-check them (identity route first; inventory the others) |
| R2 | MISSING | The Office save when the caller cannot change C (`ReconciledEmailFiling` `NotPermitted`), when C's record is one the caller cannot read or append to, or when C is only Suggested and the user picks another record | Define each outcome; never file to a record the caller has no AppendTo on; a human pick beats an engine guess (Suggested) |
| R3 | WRONG | §3.2 cannot represent C's filing: budget, report card, analysis, account have no document lookup; organization has two candidates; "the mirror owns all 16" would clear `sprk_relatedagreement` / `sprk_relatedtodo`. The outbound archive already uses `DocumentAssociationMap` (`StageArchiveRecordLink`), and D-218 (uac-r2 Option A) deliberately links outbound archives only | One mapping function shared with `StageArchiveRecordLink`; the mirror owns only columns it can derive; supersede D-218 explicitly |
| R4 | RISK | Ordering vs the after-refile step: `RecordContainerResolver.DocumentPointer` consults every link; two secure answers are undecided, so a secure-to-secure move leaves the file held, not "one pass". `sprk_matter` outranks the communication as the document's ownership parent (`SecureChildLineage.cs` ~:166-171), so a missed document stays owned by the old record | Write the document lookups BEFORE the after-refile step, inside the re-file's lock; a mirror failure is an unfinished re-file |
| R5 | MISSING | More writers of C's filing: generic `PATCH /api/v1/child-records/{table}/{id}`, the chat update tool, the playbook update node, `DataverseUpdateHandler`, the field-mapping push, the thread join; party-only re-files are `NotARefile`; `ParentsUnchanged`. Generic writers can also set or clear `sprk_relatedcommunication` / `sprk_parentdocument`. Direct Dataverse edits are never seen | Trigger on "C's regarding columns or a document's link columns were written", not on re-file; the safety net also scans recently modified email documents |
| R6 | QUESTION | C's effective filing includes its thread (a re-file re-anchors the thread; siblings' access follows it, their lookups do not) | Decide whether E-1 mirrors C's lookups or its effective filing; fix the "Thread replies" edge row |
| R7 | MISSING | A document lookup write is a document re-file today (re-stamps to-dos/analyses under it, re-derives the owner, refreshes inherited Access Permission I-16) | Route each mirror write through the document re-file core |
| R8 | RISK | Readers change: the index parent is the most specific record (project before matter); the external portal lists by record lookup, so inbound mail starts appearing there (with Q1, engine guesses too) | External-access owner's explicit OK; do not mirror Suggested until confirmed |
| R9 | RISK | Backfill changes derived containers (sequence with uac-r2's migration verify and the strict flip); unfiling moves files; the Restricted bar reads only by `sprk_relatedcommunication` (cap 250), so parent-only children fall outside | Backfill plan with uac-r2 gates; stamp `sprk_relatedcommunication` on every child |
| R10 | RISK | Concurrent re-files (X→Y, X→Z) can land a stale mirror last | Write conditional on C's version; re-read C after the document writes |
| R11 | WRONG/QUESTION | `MessageAttachmentMaterializer` is chat, not email; private messages are never fileable; Q5 has no discriminator (use `sprk_sourcetype`); a C-only status answer needs its own Read check on C and the record | Scope E-1 by communication type; discriminate with `sprk_sourcetype`; Read-check C and the record on the C-only answer |

**The reviewer's verdict on "Communication is the authority":** right for inbound and Reconciliation, not without a stated rule for R2 (a human pick against an unfiled or guessed C) and R6 (thread).

### Option B — derive instead of copy (raised by R1, R4, R7, R10)

Most of R1, R4, R7 and R10 exist because the design stores the same fact twice. The alternative is to store it once: an email document carries NO record lookup of its own (Office saves stop writing one onto email documents), and every reader resolves an email document's record through its communication — as the identity route already does (task 132) and as access already does (`SecureChildLineage`).

- **Pro:** alignment holds by construction — nothing to keep in sync, no ordering, no partial failure, no concurrency, no backfill of lookups (only the Office-saved email documents' own lookups to clear or ignore).
- **Con:** every reader that filters on a document's own lookup must also follow the communication: record Documents grids/views (FetchXML link-entity through `sprk_relatedcommunication` with an OR), the external portal's lists, Find/search (the index parent, already re-derived at index time from the communication for inbound), the AI context assembly, and D-218's outbound link (which would be dropped).
- **Open:** whether every Documents surface can express "own lookup OR via communication" (Dataverse views can, with an outer link-entity; custom grids and the external portal need code).

Which option to take is for uac-r2 and field-mapping-r1 to weigh in on, then the owner.

## 6. Owner questions

- **Q1 — Suggested.** Does an engine-suggested (unconfirmed) filing also file the documents? Recommendation: yes, so the record's documents and communications always agree; confirming changes only the status.
- **Q2 — Several records.** Does a communication filed to several records file its documents to all of them (primary to the direct lookup, others to the related lookups)? Recommendation: yes.
- **Q3 — Outlook save to a different record.** Keep C where it is and tell the user (with a Change action), rather than move it on save. Recommendation: yes; moving stays an explicit action.
- **Q4 — Re-filing an email document directly** (Documents tab, "File to record"): delegate to the Communication re-file, or refuse with a pointer to it? Recommendation: delegate.
- **Q5 — Documents a user linked to a communication by hand** (not archives or attachments): do they follow too? Recommendation: no; only archives and attachments are email documents.
