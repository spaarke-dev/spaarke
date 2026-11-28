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

## 6. Owner questions

- **Q1 — Suggested.** Does an engine-suggested (unconfirmed) filing also file the documents? Recommendation: yes, so the record's documents and communications always agree; confirming changes only the status.
- **Q2 — Several records.** Does a communication filed to several records file its documents to all of them (primary to the direct lookup, others to the related lookups)? Recommendation: yes.
- **Q3 — Outlook save to a different record.** Keep C where it is and tell the user (with a Change action), rather than move it on save. Recommendation: yes; moving stays an explicit action.
- **Q4 — Re-filing an email document directly** (Documents tab, "File to record"): delegate to the Communication re-file, or refuse with a pointer to it? Recommendation: delegate.
- **Q5 — Documents a user linked to a communication by hand** (not archives or attachments): do they follow too? Recommendation: no; only archives and attachments are email documents.
