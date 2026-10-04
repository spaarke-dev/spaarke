# Task 025 input: the residual collision surface after FR-11 (023 + 024 + 028 + 039)

> **What this is.** The "re-measure" half of the owner's 2026-09-08 **path C then B** decision (spike-4 §5). FR-11 is
> built; this note measures what collision surface is left, and proposes the FR-12 amendment and the re-scoped task 025.
> **Read-only analysis.** No product code, test, POML, TASK-INDEX, current-task or project CLAUDE.md was changed.
> **Base**: `bdb98c230` (the task 039 merge, local project branch). Every file:line below was read at this commit.
> **Evidence grades**: **[code]** means verified by code trace in this session. **[inferred]** means reasoned, not traced
> to a line. **[live-unverified]** marks every claim about SharePoint Embedded / Graph or Dataverse runtime behaviour
> that no live call confirmed. There was no Office host, so every pane statement is from code, never observed.

---

## 0. Headline

1. **The create path still silently overwrites another document's file.** This covers Word create, the Word
   "A new document" override, and Outlook email. It happens whenever the destination container already holds an item
   with the same sanitized name. The only surfaced symptom is a generic error shown to the saver, *after* the overwrite.
   This is spike-4's D1. FR-11 shrank the set of saves that reach it, but did not change it.
2. **Two things raise D1's likelihood well beyond "needs a matching title"** (024 F5):
   - **Default names collide.** A Word document with no Title property uploads as `Untitled Document.docx`, and email
     archives are named `{date}_{subject}.eml`.
   - **Containers are shared.** They are per record *or per business-unit default*, and the root is flat.
3. **NEW: the immutable dedup branch can delete a canonical document's own file.** Under `Replace`, a byte-identical
   Email/Attachment collision makes the "delete the transient duplicate blob" step delete the item the canonical row
   points to, and the save then reports success. The code path is verified. It is reachable deterministically by a
   direct `ContentType=Attachment` API call. For pane Email saves reachability is inferred and probably low (§1 row E/F).
4. **NEW, and outside FR-12 but blocking: the Word pane uploads stale bytes.** It captures the document bytes once,
   when the Save tab mounts, not when Save is pressed. An edit made after the tab mounted is never uploaded, on the
   version path (the default) as well as on create. The pane reports success. This is silent lost edits on the
   default path, and it also decides which "edited bytes" cells below are reachable at all (§1 row D, §4 O5).
5. **Recommended close:** reuse the shipped `ConflictBehavior.Fail` overload on the Office create upload, surface a new
   typed `OFFICE_020` before any row is written, and give the pane a thin Keep both / Change name choice (task 025).
   A separate client task captures bytes at Save press and remembers the just-created document so the next save is a
   version (§4, §6).

---

## 1. The residual surface

### 1.1 Mechanics common to every create (verified once, referenced by the table)

| # | Fact | Where | Grade |
|---|---|---|---|
| M1 | Every create save (Document, Email, Attachment) uploads **app-only by path** into the container root. The 4-arg `UploadSmallAsync` hard-codes `ConflictBehavior.Replace`. | `OfficeService.cs:492-496` → `OfficeStorageUploader.cs:61-62` → `SpeFileStore.cs:70-75` → `UploadSessionManager.cs:98-103`, `:138` | [code] |
| M2 | Graph's `replace` on an existing name writes the bytes as a new version of **that existing item** (same item id). | `UploadSessionManager.cs:58-69` | [live-unverified]. SharePoint semantics; also the premise of 024 F5 and 039 §5 |
| M3 | The path is `SanitizeFileName(name)`, which strips `<>:"/\|?*` and `\0` and trims. `A/B.docx` and `AB.docx` collide. SPE name matching is case-insensitive. | `SpeUploadPath.cs:72-87` | [code] for sanitize; [live-unverified] for case (the contract-test world models it case-insensitively, `OfficeEndpointsContractTests.cs:1375-1376`) |
| M4 | The container comes from the record (`TargetEntity`), else the configured default. A record-less "document-only" save is **allowed** and goes to `EmailProcessing:DefaultContainerId`, which is shared by every such save. Records in the same business unit share a container. | `OfficeService.cs:146-194`, `:337-339`; `OfficeEndpoints.cs:371`; pane guard `useSaveFlow.ts:937-943` | [code]. Sharing across records is [inferred] from `RecordContainerResolver`'s BU-default outcome |
| M5 | After a successful upload, `CreateDocumentAsync` **always creates the row first**, with name/description/status only. The pointer write (`sprk_graphitemid`) comes in a **separate** `UpdateDocumentAsync`. | `OfficeDocumentPersistence.cs:184`, `:188-198`, `:230`; `DataverseServiceClientImpl.cs:274-288`, `:795-796` | [code] |
| M6 | `sprk_graphitemid_uk` exists and is **Active** on `sprk_graphitemid`. | live GET `EntityDefinitions(LogicalName='sprk_document')/Keys`, 2026-09-13 | **verified live** |
| M7 | When M2 lands the upload on an item that already has a row, M5's pointer update violates M6 and throws. The outer catch marks the job Failed and returns `OFFICE_INTERNAL`. The endpoint maps it to **400 "Save Failed"** with `retryable: true`. **The row created in M5 is left behind** as an unassociated Draft with no pointers. | throw: [inferred], Dataverse duplicate-key on update, [live-unverified]. Catch/map: `OfficeService.cs:663-704`; `OfficeEndpoints.cs:522-531` | mixed |
| M8 | The server idempotency key is `ContentType|EntityType|EntityId|messageId-or-subject|attachmentId|FileName|ExistingDocumentId`, plus a content hash for every Document save (039). A key hit returns the earlier job and **writes nothing**. The IdempotencyFilter response cache is bound to the same key for Documents. The key is not user-scoped (024 F7). | `OfficeService.cs:240-260`, `:711-746`; `OfficeEndpoints.cs:180`, `:354-357` | [code] |
| M9 | Immutable content (Email, Attachment) runs **suppress** dedup. `FindCanonicalByHashAsync` does **not** exclude the probed item's own row. On a hit, the caller deletes "the transient blob it just uploaded". **Under M2, that blob is the canonical's own item.** | `ContentDedupDetector.cs:68-86`, `:127-142`; `OfficeDocumentPersistence.cs:158-165`; `OfficeService.cs:550-578` | [code] |
| M10 | Editable content (Document) runs **link/graduate** (028). If the colliding item's row is a hash-linked copy, `GraduateLinkedCopyIfDivergedAsync` severs its link and rewrites its `sprk_canonicalhash` **before** the create fails. A true canonical's stale hash is left as is. | `OfficeDocumentPersistence.cs:114-155`, `:459-521` (`:489-491`) | [code] |
| M11 | The Word pane's default name is `documentName` (empty until typed), else `getSubject()`, which is `title \|\| 'Untitled Document'`, plus `.docx`. | `SaveFlow.tsx:425`; `SaveView.tsx:153-154`; `WordAdapter.ts:116-123`; `useSaveFlow.ts:891`, `:988-990` | [code]. That most real documents have no Title property is [inferred] |
| M12 | The pane captures the document bytes **once per Save-tab mount** (effect deps `[hostAdapter]`). It never re-captures them at Save press, on retry, or on "Save Another". The Save tab re-mounts on a tab switch. | `SaveView.tsx:132-231` (`:202-219`); `SaveFlow.tsx:490-530`; `useSaveFlow.ts:982-995`, `:1155-1159`; `App.tsx:529-530` | [code] |
| M13 | "Save Another" calls the hook's `reset`. That keeps the selected record (`reset` does not clear it; its initial value is the sessionStorage last association) and keeps the typed Document Name (SaveFlow state). | `SaveFlow.tsx:778-780`; `useSaveFlow.ts:452`, `:530-542` | [code] |

**Cell legend (the only columns that behave differently).** Every create cell collapses into four columns:
- **C1**: different container, any name, any bytes.
- **C2**: same container, different name after M3.
- **C3**: same container, same name, **identical** bytes to the existing item.
- **C4**: same container, same name, **different/edited** bytes.

For C3/C4, "same record" and "different record sharing the container" differ **only** by whether M8's key matches, and that is called out where it matters. "Different container" can never collide. That is safe by construction of the path namespace (M1), verified by code.

### 1.2 The table

| Row (pane path) | Cell | At SPE | At Dataverse | What the user sees | Severity | Grade |
|---|---|---|---|---|---|---|
| **A. Word create.** Identity `new`, or an explicit "save as new anyway" on `indeterminate`/`error`/`denied` (`SaveModeSection.tsx:92-111`) | C1, C2 | New item | One new row; link/graduate if byte-identical to any canonical (M10, 028) | "Document Saved" | **correct** | [code] |
| | C3, **same record** | Nothing written (M8 key hit, before the upload) | Nothing | Success via the replayed 202, or the 200 duplicate falling through to job tracking. The duplicate body carries no `documentId`, so `isDuplicateSaveResponse` is false (`useSaveFlow.ts:398-405`, `:1086-1096`) | **correct** (a true retry). Caveat: a *different* user saving identical bytes and name to the same record is answered with the first user's job (M8 not user-scoped) | [code]; the pane's exact state is [inferred] |
| | C3, different record in the same container | Replace onto the existing item with the same bytes (+1 SPE version) | The existing row is found as canonical. Graduate is a no-op. An orphan row is created, then the pointer update hits the key (M7) | Generic 400 "Save failed: …" with **Retry**. Each Retry adds another orphan row and SPE version | **visible error** (misleading), plus orphan rows. No content change | [code] + M7 [inferred] |
| | **C4**, any record | **Replace overwrites the other document's current content** (M2) | The victim row graduates if it was a linked copy (M10). Orphan row, then the key throws (M7). The victim's profile/index still describe the old content | The saver gets a generic error. **The victim document's owner is told nothing.** Opening the victim record opens the saver's document. Across records in one container this is cross-matter content exposure | **silent overwrite of another document** | [code]; M2 and M7 [live-unverified] |
| **B. Word version**: the default when identity is `resolved` (`SaveModeSection.tsx:82-91`) | all cells | **No path is involved.** Item-keyed OBO write to the resolved row's own item (`OfficeService.cs:484-489`, `:858-958`, `:875` → `OfficeStorageUploader.cs:141-201`). The name and the picker are ignored (`useSaveFlow.ts:913`; container = the target's drive, `OfficeService.cs:337-339`) | Updates the existing row only (`OfficeDocumentPersistence.cs:304-354`); never a create, so the key cannot be hit | "New Version Saved" | **correct** for collisions. **But see M12**: the version written is the Save-tab-mount snapshot. An edit made after mount is silently not uploaded, and a second press (identical stale bytes, same client version key) is replayed, writing nothing | [code]; M12 impact on a live host [live-unverified] |
| **C. Word "A new document" override** (resolved + choice `new`; `SaveModeSection.tsx:87`) | C1, C2 | New item | One new row linked to the original as canonical when byte-identical (028; 024 data-mutation tests) | "Document Saved" | **correct** | [code] |
| | C3 | If the key matches the original's own create (same record, name and bytes), **nothing is written**: the explicit copy request is replayed. Otherwise, as A-C3 | as A-C3, or nothing | Success, but **no copy was made**; or the generic error | **visible error**, or a silent no-op of an explicit "new document" request (minor) | [code] |
| | **C4** | **Overwrites the ORIGINAL's content.** This is the most likely collision here: the picker pre-selects the last record (M13, `useSaveFlow.ts:452`) and the default name is often the original's own default name (M11) | Original graduates if linked (M10). Orphan row, then the key throws (M7) | The pane had just promised *"The existing document is not changed"* (`SaveModeSection.tsx:180`). The user then sees a generic error; the original has silently changed | **silent overwrite of another document**, the very one the user chose to leave alone | [code]; M2/M7 [live-unverified] |
| **D. Re-save of a just-created local document.** The pane still sees it as `new`: identity is resolved once per pane session (`App.tsx:301-310`); a completed save only logs (`App.tsx:547-549`); a local or unsaved URL maps to `new` (`documentIdentityService.ts:150-156`, `:179-183`) | **D-i: same mount** ("Save Another", or Retry) | **Nothing written.** The bytes are the *first* save's stale snapshot (M12), so the name, record and content hash are identical and M8 replays | Nothing | "Document Saved". **The edits were never sent** | **silent data loss** (lost edits). A capture defect, not a collision | [code]; live host [live-unverified] |
| | **D-ii: after a remount** (tab switch or pane reopen), same name (default name, or retyped) + same record (sessionStorage) = C4 onto **its own** item | Replace. The edits land as a **new SPE version of the first save's item** | row1 keeps a stale `sprk_canonicalhash` (M10, true canonical). Orphan row2, then the key throws (M7). **No finalization, so row1's profile/index are stale** | Generic "Save failed" with Retry, **although the edits were saved**. Each Retry adds another version and another orphan | **visible error** (misleading, not lossy), plus orphan rows. This is 039 §5's D1 | [code]; M2/M7 [live-unverified] |
| | D-ii, different name, or a different container/record | New item | **A second independent row**: the user now has two documents, not a version | "Document Saved" | **correct** by the rules, but not the user's intent | [code] |
| **E. Outlook email save**: the pane (`useSaveFlow.ts:881-888`, `:969-979`) and the ribbon quick-save (`outlook/commands/index.ts:134`, `quickSaveHelpers.ts:37,74`). Name: `{SentDate:yyyy-MM-dd}_{subject≤80}.eml`, where the subject is the pane's editable name, else the email subject (`OfficeEmailEnricher.cs:375-383`; `useSaveFlow.ts:970`) | C1, C2 | New `.eml` item | New row. Immutable suppress if byte-identical to a canonical (M9), with the transient blob deleted, which is correct when names differ | "Document Saved" | **correct** | [code] |
| | Same email, same record | Nothing (M8 key hit: message id + record) | Nothing | Success or duplicate | **correct** | [code] |
| | **C4**: a *different* email, same subject, same sent date, same container (e.g. two same-day replies in one thread, filed to one matter) | **Replace overwrites email 1's archive with email 2** | Suppress finds no canonical for the new hash. Orphan row, then the key throws (M7). Row1's `sprk_email*` fields still describe email 1; its file is email 2 | Saver: generic error. The victim is told nothing | **silent overwrite of another document** | [code]; M2/M7 [live-unverified] |
| | C3: the same email filed to a **second record** in the same container, or a same-key **retry after a post-create failure** (039 §5 made that retry re-run) | Replace onto item1 with a *rebuilt* `.eml` | **If** the rebuild is byte-identical: suppress finds **row1** (M9, no item exclusion). `OfficeService.cs:550-552` then **deletes item1, row1's own file**, and completes "DeduplicatedToExisting" pointing at row1. If it is not byte-identical: as C4, though the content is semantically the same | Byte-identical: **success**, and row1 now points at a deleted file. Otherwise: generic error | Byte-identical: **silent data loss**. Otherwise: visible error | Code path [code]. Byte-identity is [inferred] **unlikely**: `new MimeMessage()` (`OfficeEmailEnricher.cs:253`) is only overridden for Date/Message-Id when `SentDate` is present and the id is RFC-shaped (`:283-309`; the pane sends an `AAMk…` item id, `useSaveFlow.ts:977`); multipart boundaries are random when attachments/inline images exist (`:311-362`) |
| **F. Outlook attachment save** | Pane (every cell) | The pane never sends `ContentType=Attachment` (`useSaveFlow.ts:877-888`). Selected attachments ride the Email save and are uploaded by the finalization worker as `{parentDocumentId:N}_{name}` (`UploadFinalizationWorker.cs:1088-1094`). The parent id is fresh for every new email row, so **no name can collide** | One child row per attachment | as the email | **correct**, safe by naming, not by policy (still the Replace overload, `:1094`). If the parent email hits E-C4, finalization never runs and the attachments are not saved; that is part of E's error | [code] |
| | Direct API `ContentType=Attachment` (no in-repo client sends it; grep of `src/client` and `src/solutions`) | C4: Replace overwrites. **C3 with a different record: attachment bytes are deterministic** | C4: as A-C4. C3: suppress finds the existing row, so **its own file is deleted** (M9) | C4: generic error. C3: success | C4: **silent overwrite**. C3: **silent data loss** (deterministic) | [code]; reachable by any authenticated caller of the live route |

### 1.3 Incidence

A read-only dev query on 2026-09-13 for `sprk_document` rows created since 2026-08-01 with
`sprk_graphitemid eq null and sprk_filename eq null` (the M7 orphan shape) returned **0**. There is no evidence of D1
orphans in dev. That is not proof, because I did not verify whether 039's build is deployed to dev.

---

## 2. What 023, 024, 028 and 039 closed, and what remains

| Task | Closed (concretely) | Did not close / newly exposed |
|---|---|---|
| **023** | An identified document is versioned by **item id** (row B). No path, no name collision, no second row, no key collision on the default path. D3 (inert `ExistingDocumentId`) closed. | Nothing on the create path. |
| **024** | The pane never silently creates on `conflict`/`indeterminate`/`error`/`denied`. `resolved` defaults to version. A create is reachable only through `new`, an explicit choice, or Outlook. | **Made row C (override) a one-click normal action** (024 F5). The override's pre-selected record and default name make C-C4 the *likely* override outcome, not an edge case. |
| **028** | D2: an editable Document is never suppressed, so the Document path never deletes the blob it uploaded. | The immutable suppress-and-delete branch (M9) is intact for Email/Attachment, and under Replace it can delete a canonical's own file. **Not documented anywhere before this note.** |
| **039** | F4 (silent lost edits at the server key/cache layer); F2 (failed jobs dedupe retries); F3 (no document id). | **By design**, it turned row D-ii's silent drop into a visible D1 error. It made a retry after a post-create failure re-run, which reaches A/C-C3, or the E-C3 delete branch for byte-identical immutable content. It did **not** touch the pane's stale capture (M12), so an edited re-save in the same mount is still silently dropped (D-i), now at the client. |

**What remains**, all verified by code except where the table says otherwise:
- **R1.** Silent overwrite on every create path (A-C4, C-C4, E-C4, F-API-C4).
- **R2.** The key collision surfaces as a misleading, retryable 400 `OFFICE_INTERNAL` that leaves an orphan row per attempt (A-C3, D-ii).
- **R3.** Immutable suppress deletes the canonical's own file (E-C3 if byte-identical; F-API-C3 deterministically).
- **R4.** The pane never treats a document it just created as identified (row D).
- **R5.** The pane uploads mount-time bytes (M12). This is outside FR-12 but decides rows B and D.

---

## 3. Root causes of what remains

1. **RC-1: Collision policy is decided in the wrong place, and hard-coded.** The Office create upload is path-keyed and
   rides the no-policy overload (`UploadSessionManager.cs:98-103`), so Graph resolves a collision by **overwriting
   after the bytes have moved**. Nothing on `POST /api/office/save` can ask for `Fail` or `Rename`, although the
   explicit overload and its typed 409 already exist (`SpeFileStore.cs:82-88`; `UploadSessionManager.cs:115-200`,
   `:169-179`). This is the direct cause of R1 and R3.
2. **RC-2: The real collision detector is the Dataverse key, and it runs last.** The row is created before the pointer
   update (M5), so the key fires after the SPE write, after a row exists, and without typing. That causes R2.
3. **RC-3: The name namespace is coarse, and defaults converge on it.** The container root is flat
   (`OfficeStorageUploader.cs:31-35`). Containers are shared (M4). The sanitizer and case-folding merge names (M3).
   Word defaults to `Untitled Document.docx` (M11), and email names are date + subject. This multiplies R1's
   likelihood. It is not a defect in itself.
4. **RC-4: The pane's document identity is read-only and session-once.** A completed create never becomes the pane's
   identity (`App.tsx:547-549`). A local document can never be resolved by FR-01, and FR-02's stamp goes into the
   *uploaded* bytes, never the open document (spec FR-02). That causes R4.
5. **RC-5: The bytes are captured at mount, not at save** (M12). That causes R5 and D-i.
6. **RC-6: The suppress branch assumes "the item I just uploaded is never the canonical's item"**
   (`OfficeService.cs:542-549`; `SpeWriteSinkContainerProvenanceGuardTests.cs:702-706` says the same). That is true
   only when uploads cannot land on an existing item, which Replace breaks. This is the second cause of R3.

---

## 4. Options

| # | Option | Behaviour | Blast radius | Files | Interaction with 023 / 028 / 039 |
|---|---|---|---|---|---|
| **O1** | **Office create uploads state `ConflictBehavior.Fail`** (reuse; do not rebuild) | `OfficeService.cs:492` passes `Fail`. `OfficeStorageUploader.UploadToSpeAsync` gains a `ConflictBehavior` parameter and calls the **existing** explicit overload (`SpeFileStore.cs:82-88`). **It must catch `SpaarkeStorageException` (409) before its catch-all** (`OfficeStorageUploader.cs:76-80`); otherwise the 409 is swallowed into `OFFICE_012` "upload failed", 502, retryable. Return a new **`OFFICE_020`** (409, type `conflict`) through the existing `!uploadSuccess`-style branch (`OfficeService.cs:498-520`), with the job marked Failed and `Retryable = false`. **This happens before any row, pointer write, dedup call or delete.** | **Document, Email and API Attachment**: the call site is shared and the policy is host-neutral (the owner's 028 rule). Every other app-only caller keeps Replace (the 4-arg overload is untouched). Email **already** errors today on C4 (M7), just *after* overwriting, so Fail removes the overwrite and changes no user-visible outcome class. | `OfficeStorageUploader.cs`, `OfficeService.cs`, `OfficeErrorCodes.cs` (+020), `OfficeEndpoints.cs` (`MapSaveErrorToProblem`), `OfficeEndpointsContractTests.cs` (world: add a 5-arg setup that 409s on an existing name), and the `OfficeDocumentPersistence.cs:132-145` comment (now stale: "reachable because Replace"). **Not** `UploadSessionManager.cs`, `SpeFileStore.cs` or `ContentDedupDetector.cs`. The ArchTest `SinkSite` (`SpeWriteSinkContainerProvenanceGuardTests.cs:579`) is keyed on the method name and ordinal and is probably unchanged; let the guard confirm. | **Closes R1, R2 and R3 on the Office path. The key becomes unreachable from the create path**, because a Fail upload always lands on a fresh item. 023: none (the version path never calls `UploadToSpeAsync`). 028: link/graduate unchanged; graduate-on-same-item collision becomes dead (fix the comment). 039: a retry after a *post-create* failure now meets `OFFICE_020` against **its own** first-attempt file rather than D1. That is non-destructive but confusing (owner Q9). Also: an **orphan SPE item** (upload succeeded, create failed) now blocks its name until renamed; Keep both (O3) covers it. |
| **O2** | Server always `Rename` | Graph stores `Name 1.docx`; never a collision, never a prompt. | All three content types. | As O1, plus **the tuple change**: `UploadToSpeAsync` returns no item name today (`OfficeStorageUploader.cs:71`), so the row would record the *requested* name, not SPE's. | Closes R1 and R3 **silently**. Row D-ii would mint `X 1.docx`, `X 2.docx`: a new document per re-save, which defeats FR-11. **Rejected as the default.** It is acceptable as the *opt-in* behind Keep both (O3), and arguably right for immutable email (owner Q1). Rename on app-only simple PUT for SPE is [live-unverified]. The OBO path's "Keep both" uses the same Graph parameter. |
| **O3** | **Thin pane choice on `OFFICE_020`** | An inline MessageBar that names the file, with **Keep both** (resend the same request with an explicit rename opt-in), **Change the name** (focus Document Name; nothing sent), and Dismiss. **Not "Replace"** on this path: replacing an existing file's content is FR-11's version save, reached through identity. A path-keyed replace would re-open R1 and needs write authorization on a row the request does not name (ADR-008). | Both hosts (shared `SaveFlow`). | `errorMessages.ts` (+`OFFICE_020`), `useSaveFlow.ts` (resend with opt-in; `retryable:false` means no Retry button), `SaveFlow.tsx` (actions). Keep both needs **one new request field** mirroring the OBO vocabulary (`fail` default, `rename` opt-in, as in `OBOEndpoints.cs:558-562`), e.g. `SaveRequest.OnNameConflict` in `SaveRequest.cs`. That is a contract addition (owner Q2). | Mirrors the shipped dialog's semantics without importing it (ADR-012 Path A). Deviates from it by omitting "Save as new version" (owner Q3). |
| **O4** | **The pane remembers the document it just created** | On a completed Word create, set the pane identity to a synthetic `resolved` (`documentId` from the job result, which 039 now guarantees; name; file name). The next save of the same open document then defaults to a **version** save (023 path), with "A new document" still offered. | **Word only.** Outlook has no identity (`App.tsx:175-181`); email is immutable. | `App.tsx` (`onComplete` → `setDocumentIdentity`), `SaveFlow.tsx`/`SaveView.tsx` (pass the id up), `documentIdentityService.ts` (type only, if a distinct reason is wanted). | Removes D-ii's D1 within a pane session. **Depends on O5**: without it, the version save sends stale bytes and is replayed as a no-op. **Unverified precondition (owner Q6):** 023 writes the version **OBO**, but a locally-created document's item was written **app-only**, so the user may lack SPE write on it. That would give `OFFICE_009`, and 024's existing handling offers "save as new", which lands on O1/O3. Needs 029 for profile freshness after the version. Cross-session (pane reopened) is not covered: the re-save is a create, hits `OFFICE_020`, and Keep both makes a second document (owner Q5). |
| **O5** | **Capture the bytes at Save press** | `getDocumentContent` is called in the Save handler and on retry, not in the mount effect. | Word (Outlook sends no bytes). | `SaveView.tsx`, `SaveFlow.tsx`, `useSaveFlow.ts`. | Closes R5 and D-i. It **raises the reachability** of D-ii (edited bytes now really reach the server). So it must land **with or after O1**, or together with O4. |
| **O6** | Defence in depth for RC-6 | In `OfficeService`, do not delete when the canonical's `sprk_graphitemid` equals the uploaded item. Or exclude the probed item in `FindCanonicalByHashAsync`. | O1 makes the Office path unreachable, so this protects only future Replace callers of `ReconcileAsync`. | `OfficeService.cs` (this project); or `ContentDedupDetector.cs` (owned by email-r2; 024 treated modifying it as an escalation). | Recommend a `/defer` issue now, because **F-API-C3 is live on master today, independent of 025's timing**. Fold the `OfficeService` guard into 025 only if the owner wants belt and braces. |
| **O7** | Consume UAC-r2 **094**'s pre-flight probe | Ask the server before the bytes move. | n/a | n/a | **Not applicable as a fix.** 094 is `pending` (deps 076), is built on the client SDK/wizard path, and by its own text *"the probe is an OPTIMISATION, not the guarantee … the 409 path MUST remain"*. The Office request already carries its bytes, so the server-side `Fail` (O1) **is** that guarantee. Adopting 094's probe later is an optional UX follow-up, not a dependency. |
| **O8** | Unique-prefix email archive names (`{id}_{date}_{subject}.eml`, the attachment/communication precedent) | Email names can never collide; no prompt for email. | Email only. | `OfficeEmailEnricher.GenerateEmlFileName` and the call site. | Removes E-C4 entirely without a pane prompt, at the cost of uglier SPE names. **An owner judgment (Q1).** Under O1 alone, email collisions become an `OFFICE_020` prompt. |

**Recommended set.**
- **Task 025 (server + pane) = O1 + O3.** One host-neutral policy: every Office create states `Fail` through the
  **existing** overload and its **existing** 409 translation, and gets a typed `OFFICE_020` before any row. The pane
  shows it thinly, with Keep both / Change the name. There is no Replace: replacing content is FR-11. This closes every
  silent-overwrite and delete cell and removes the orphan rows, and it needs no new detector, resolver, probe or dialog
  component.
- **A new client task = O5 + O4.** Capture at Save press, which I recommend treating as **production-blocking in its
  own right**, because it silently drops edits on the default version path. Then remember the just-created document so
  the next save is a version. Serialize it after 025's client half (shared `SaveFlow.tsx`/`useSaveFlow.ts`).
- **A `/defer` issue now for O6** (F-API-C3 is live today).
- **Owner judgment calls**: Email under Fail+prompt vs O8 or silent Rename (Q1); whether Keep both's request field is
  in r1 (Q2); omitting Replace (Q3); ordering the stale-capture fix (Q4).

---

## 5. Proposed FR-12 amendment (replacement text for spec.md FR-12)

> 12. **FR-12: A name collision on the add-in's create save never overwrites; it is surfaced before anything is
> recorded.** The add-in saves through `POST /api/office/save`. Its create path uploads app-only, by file name, into
> the destination record's container. It does **not** ride the client/OBO upload path that `unified-access-control-r2`
> protected on 2026-09-02 (Spike-4, `notes/spikes/spike-4-collision-path.md`), so that fix cannot be consumed as
> shipped. This FR closes the same defect on this path by **reusing its mechanism, not rebuilding it**:
> (a) Every create upload on `POST /api/office/save` (Document, Email and Attachment content alike; host-neutral)
> states `ConflictBehavior.Fail` through the existing explicit `SpeFileStore.UploadSmallAsync(…, conflictBehavior, …)`
> overload and its existing 409 translation. The shared no-policy overload keeps `Replace` for its other callers. No
> second conflict detector, conflict-behaviour resolver or pre-flight probe is added; the probe remains UAC-r2 task
> 094's.
> (b) A collision is answered **before any `sprk_document` row is created, updated or linked, and before any content
> dedup or SPE delete runs**, with a distinct 409 ProblemDetails `OFFICE_020`. It is not `OFFICE_011`, which means
> duplicate *content*.
> (c) The pane shows the collision, naming the file, and offers **Keep both** (the server stores the file under a
> non-colliding name) or **Change the name**. It does not offer a by-name Replace. Writing new content into an
> existing document is FR-11's version save, reached through document identity.
> (d) A version save (FR-11) never takes this path.
> — *Acceptance*: given a file with the same sanitized name in the destination container, a create save from either
> host returns 409 `OFFICE_020`. The existing file's bytes and version count are unchanged, and no `sprk_document` row
> is created, updated or deleted, and no SPE item is deleted. All of this is asserted by a live (non-skipped) contract
> test that reads them before and after. Keep both creates exactly one new file under a non-colliding name and exactly
> one new row. Different-container saves, different-name saves, identical-request retries and version saves behave
> exactly as before.

Consequential spec edits:
- **Success Criterion 6** (spec.md:203) becomes: *"A create save whose file name collides returns `OFFICE_020` with the
  existing file's bytes untouched and no row written; the pane offers Keep both / Change the name. Verify: live
  contract test reading bytes before and after (FR-12)."*
- The spec.md:274 line *"FR-12 is fixed at the shared client upload path…"* is **false**; replace it with *"FR-12 is
  fixed at the shared Office save path (`OfficeService` → `OfficeStorageUploader`), for both hosts, by reusing the
  shipped `Fail` overload."*
- If the owner drops Keep both (Q2), delete it from (c) and from the acceptance text.

---

## 6. Proposed re-scoped task 025

- **Title**: FR-12: create-path name collisions fail safe (`Fail` + `OFFICE_020`) and are surfaced in the pane
- **Rigor** FULL. **Tier** sonnet / high. **Steps** directional. **parallel-safe** false (it is on
  `OfficeService.SaveAsync`, like 014 and 029). **Deps**: 005 ✅, 023 ✅, 024 ✅, **039** (merged at `bdb98c230`).
  Run `/conflict-check`: UAC-r2 PR #950 touches `OfficeService.cs` (039 §8).
- **Goal**: A create save on `POST /api/office/save` whose sanitized file name already exists in the destination
  container writes nothing to SPE or Dataverse, and returns a typed `OFFICE_020` that the pane shows with Keep both /
  Change the name. The Office create upload does this by stating `ConflictBehavior.Fail` through the existing
  facade overload.
- **Files (modify)**:
  - `Services/Office/OfficeStorageUploader.cs`
  - `Services/Office/OfficeService.cs`
  - `Api/Office/Errors/OfficeErrorCodes.cs`
  - `Api/Office/OfficeEndpoints.cs` (`MapSaveErrorToProblem`)
  - `Models/Office/SaveRequest.cs` (only if Q2 = yes)
  - `Services/Office/OfficeDocumentPersistence.cs` (comment `:132-145` only)
  - `shared/taskpane/utils/errorMessages.ts`, `hooks/useSaveFlow.ts`, `components/SaveFlow.tsx`
  - `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs`, plus one data-mutation test.
- **Files (must NOT change)**: `UploadSessionManager.cs`, `SpeFileStore.cs`, `ContentDedupDetector.cs`,
  `OBOEndpoints.cs`, `src/client/shared/Spaarke.SdapClient/**`, `src/solutions/DocumentUploadWizard/**`.
- **Acceptance criteria (closed set)**:
  1. Given an existing SPE item whose name equals the request's sanitized name (case-insensitively) in the derived
     container, a **Document** create returns **409** with `errorCode = OFFICE_020` and `retryable = false`.
     Afterwards, the existing item's bytes and version count are unchanged, `DocumentCreates == 0`, there are no
     document updates or generic updates, there is no finalization message, and the job is Failed.
  2. Same as AC1 for an **Email** create (same `{date}_{subject}.eml`) and an **Attachment** create.
  3. **Data-loss negative**: a byte-identical **Attachment** create onto an item whose row is a canonical with that
     hash returns 409 `OFFICE_020`. `DeleteFileAsync` is **never** called, and the canonical's item still exists.
     Before this task, the same arrangement completed "DeduplicatedToExisting" and deleted it. Reproduce-first.
  4. **Overwrite negative**: a C4 arrangement (a different document, same name, same container, different record)
     leaves the victim row's pointers, `sprk_canonicaldocument` and `sprk_canonicalhash` unchanged.
  5. (Only if Q2 = yes) With the rename opt-in, exactly one new item with a non-colliding name and exactly one new row
     are created. The row's `sprk_filename` is **SPE's returned name**, and the existing item is untouched.
  6. **Regression**: different container, different name, an identical-request retry (key hit; still answered before
     the upload, never 409), and a version save (never reaches the upload; `ReplaceCalls` unchanged) all behave as
     before. The existing 023/024/028/039 Office suites stay green.
  7. The 4-arg `UploadSessionManager.UploadSmallAsync` still states `Replace`, and `git diff` shows no change to the
     "must NOT change" list.
  8. `OFFICE_011` is not reused. `OFFICE_020` exists in `OfficeErrorCodes` (409 / `conflict` / title), in
     `MapSaveErrorToProblem`, and in the pane catalog.
  9. Pane: `OFFICE_020` renders a specific message naming the file, with **no Retry** button. Keep both (if in scope)
     resends exactly once with the opt-in. Change the name focuses Document Name and sends nothing.
  10. **Negative**: Dismiss or Change the name sends no request, and there is no new row or item.
  11. The collision contract test is **live** (not skipped), and the world's 5-arg `UploadSmallAsync` setup models
      `Fail` (409 on an existing name) and `Rename`.
  12. ADR-021: tokens only, dark-mode check. ADR-019: typed ProblemDetails. BFF publish delta measured against a
      fresh `origin/master` build with `Compress-Archive`. Full `Sprk.Bff.Api.Tests`, ArchTests (including
      `SpeWriteSinkContainerProvenanceGuardTests`), office-addins typecheck, gated jest and `npm run build` green.
- **Escalation triggers**:
  - (a) A dev check shows app-only `PUT …?conflictBehavior=fail` does **not** 409 on SPE, or `rename` is unsupported.
  - (b) Anyone asks for a by-name "Replace / save as new version of that file". That needs write authorization on a row
    the request does not name (ADR-008), which is security-sensitive (§6).
  - (c) The 409 body would need to reveal the existing document's id or owner. That is an enumeration concern.
  - (d) Any other app-only caller's conflict behaviour would change.
  - (e) PR #950 or 094 lands changes on the same files; rebase and re-run `/conflict-check`.

### Separate client task (new; next free id): "Save what is on screen, and version what you just saved"

- **Rigor** FULL. **Tier** sonnet / high. **Deps**: 024 ✅; **after 025's client half** (shared files).
  **Recommend: blocks production use of the Word save path** (R5 is silent lost edits on the default path).
- **Goal**: A Word save uploads the document as it is at the moment Save is pressed. Within a pane session, the next
  save of a document the pane has just created defaults to a version save of that document.
- **Files**: `components/views/SaveView.tsx`, `components/SaveFlow.tsx`, `hooks/useSaveFlow.ts`, `App.tsx`,
  and optionally `services/documentIdentityService.ts` (type only).
- **Acceptance criteria**:
  1. Given the Save tab mounted, and the adapter returning bytes B1 at mount and B2 later, pressing Save uploads
     **B2**. The same holds for Retry and for "Save Another".
  2. Given a completed Word **create** (the job result names document D), the next save without a pane reload
     defaults to **a new version of D** (`existingDocumentId = cleanGuid(D)`, `isNewVersion: true`), and
     "A new document" remains available.
  3. **Negative**: a failed, refused or `OFFICE_020` create does not set the remembered identity.
  4. **Negative**: Outlook is unchanged (no identity; every Email save remains a create).
  5. **Negative**: an unchanged document saved again as a version is answered by the server's key. The pane does not
     report a second version. Assert whatever the pane shows; nothing is written.
  6. If that version save is refused with `OFFICE_009`, the existing `describeVersionSaveFailure` path offers
     "Save as new document", and nothing is written.
  7. It is documented that a remembered identity does not survive a pane reload. The cross-session re-save of a local
     file is a create, which is 025's `OFFICE_020` path.
- **Escalation trigger**: the owner wants the identity to survive a pane reload. The only in-document carrier is
  Office document settings or a custom XML part written into the *open* document, which conflicts with FR-02's
  "never mutate the user's open document".

---

## 7. Open questions for the owner

1. **Email under one host-neutral `Fail` + prompt**, or should immutable email archives never prompt: a unique prefix
   (O8), or silent `Rename`? Recommendation: one policy (Fail) now; O8 as a follow-up if same-day same-subject prompts
   prove noisy.
2. **Keep both in r1?** It needs one new request field (mirroring OBO's `fail|rename` vocabulary). Without it, the
   pane offers only Change the name.
3. **Confirm "Replace" is deliberately not offered** on the create path, which deviates from the shipped two-option
   dialog; content replacement is FR-11 only.
4. **Stale capture (R5)**: accept it as production-blocking, and schedule the new client task immediately after 025
   (or ahead of it, with O1 landing in the same release, because O5 alone raises D-ii's reachability)?
5. **Cross-session identity** for locally-created documents: accept "per pane session only", or authorize writing a
   marker into the open document (which conflicts with FR-02's rule)?
6. **Verify before building O4**: can a user OBO-write a new version of an SPE item the BFF created app-only? If not,
   O4 degrades to `OFFICE_009`, then "save as new", then `OFFICE_020`.
7. **F-API-C3 is live on master** (a direct `ContentType=Attachment` save can delete a canonical's own file). File a
   defect now, and add the `OfficeService` "never delete the canonical's own item" guard in 025 as defence in depth?
8. **Default names** (`Untitled Document.docx`) are the largest multiplier of collisions. Should FR-06 / task 020
   default to something more specific? Office.js exposes the local file name only via `document.url`.
9. **Retry after a post-create failure** (039 §5) now meets `OFFICE_020` against the user's own half-finished file.
   Is Keep both acceptable there for r1, or should 025 recognise "same idempotency key, prior attempt" as a resume?
10. **The account/contact association drop** (`OfficeEndpoints.cs:382-387`) and **record-less saves into the shared
    default container** (M4) both widen the namespace saves collide in. Keep record-less saves allowed?

## 8. Verified vs not

- **Verified live** (read-only GETs, 2026-09-13): `sprk_graphitemid_uk` is Active on `sprk_graphitemid`. There are 0
  orphan-shaped `sprk_document` rows since 2026-08-01 in dev.
- **Verified by code at `bdb98c230`**: every file:line cited above.
- **Not verified**:
  - SPE `replace` writing a new version of the same item (M2);
  - SPE case-insensitive names;
  - app-only `fail` returning 409 and `rename` being supported on SPE;
  - the Dataverse key violation throwing on `Update` (M7);
  - `.eml` byte-determinism;
  - the pane's exact rendered copy for `OFFICE_INTERNAL`;
  - every behaviour in a live Word or Outlook host, including M12's effect.
