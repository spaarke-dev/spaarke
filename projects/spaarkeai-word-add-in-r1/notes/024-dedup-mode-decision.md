# Task 024: which dedup mode the "Save as new document" override takes, and why

> POML step 1. Written after reading both dedup callers side by side, before any code change.
> Base: `7bc11ddf3`. Symbols are named rather than line numbers, because the POML's line numbers predate
> tasks 023 and 028.

## The decision

**The override takes the editable LINK/GRADUATE mode, and it already does. Task 028 made that true on the
shared create path, so this task changes no server code to achieve it.**

The override sends a `ContentType=Document` save with **no** `document.existingDocumentId`. That request
misses task 023's version branch (`OfficeService.IsVersionSave` is false) and lands on the create path:
`OfficeStorageUploader.UploadToSpeAsync`, then `OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync`.
There, `IsEditableContent(SaveContentType.Document)` is `true`, and the save takes the editable branch:

1. `ContentDedupDetector.ResolveContentIdentityAsync` is the pure seam. It reads the live `quickXorHash`
   and the true canonical for that hash (`FindCanonicalByHashAsync` excludes hash-linked copies). It sends
   no notification and suppresses nothing.
2. `GraduateLinkedCopyIfDivergedAsync(itemId, hash)` runs next.
3. `CreateDocumentAsync` **always** runs, so the override gets its own row.
4. `UpdateDocumentAsync` stamps the pointers and `sprk_canonicalhash`.
5. `LinkEditableCopyToCanonicalAsync` writes `sprk_canonicaldocument` pointing at the canonical when the
   bytes matched, then calls `NotifyLinkedCopyAsync`.
6. The method returns `(newId, WasContentDuplicate: false)`. `OfficeService` therefore never runs its
   `wasContentDuplicate` short-circuit: it does **not** call `DeleteFromSpeAsync` on the blob it just
   uploaded, and it does **not** return the canonical's id.

That mirrors `ComposeCreateOnSavePromoter.PromoteIfEphemeralAsync`. Its create branch stamps the hash and
links on a byte-identical hit (via `ResolveContentIdentityAsync`). Its existing-row branch calls
`ComposeRecordResolution.GraduateLinkedCopyIfDivergedAsync`, which severs the link with the `DBNull`
clear-sentinel and stamps the new hash once the content diverges. The same two columns and the same
detector seam are used, so there is no second mechanism.

The immutable **suppress** mode is `ContentDedupDetector.ReconcileAsync` inside
`CreateDocumentWithSpePointersAsync`'s `else` branch. It is reachable only for `SaveContentType.Email` and
`SaveContentType.Attachment`. `OfficeDocumentPersistenceEditableDedupTests.ImmutableSave_ByteIdentical_StillSuppressesTheSecondDocument`
pins that boundary. **Nothing the add-in's Word pane sends can reach it.** Word always sends
`contentType: 'Document'` (the `useSaveFlow` content-type switch); only the Outlook host sends `Email`.

## Why link/graduate is the correct mode here, not only the available one

A Word document is a living draft. When a user explicitly chooses "Save as new document" on a document
Spaarke already tracks, they are asserting that it is a **second** document, even when its bytes happen to
equal the first one right now. Suppress-forever would answer that assertion by discarding the new record
and pointing at the old one, silently collapsing two drafts into one (NFR-08). Link/graduate records both
facts:

- a new, independent record exists (what the user asked for); and
- it is currently byte-identical to a canonical (what dedup knows).

It then forgets the second fact the moment the draft diverges.

## Where graduation happens after an override

A linked copy made by the override is re-saved along one of two paths:

- **The sanctioned path.** The user later edits the copy and saves it. Task 013 resolves the open document
  to the copy's own row, so the default is a **version** save. `OfficeDocumentPersistence.RecordNewVersionAsync`
  then calls `GraduateLinkedCopyIfDivergedAsync` with the new live hash. That call is task 023's use of
  task 028's seam.
- **A second create under the same name into the same container.** The path-keyed upload lands on the
  copy's own drive item. `GraduateLinkedCopyIfDivergedAsync` severs the copy's link, and then the create
  path would try to write a second row for that item. This is spike-4's **D1** (`sprk_graphitemid_uk`),
  owned by task 025. It is not new with this task and is not something this task works around (NFR-07).

## What this task therefore does on the server side

No production code. The POML lists `OfficeDocumentPersistence.cs` as a file to modify, "to ensure" the
override routes through link/graduate. Task 028 already ensures it, and changing it again would only
widen the blast radius on a path that `email-communication-intelligence-r2` and Compose also depend on.
Directional step mode lets the sequence adapt to the real code: the obligation that remains is **proof**.
A data-mutation integration test drives the real HTTP route, `POST /api/office/save`, through the in-memory
world. It asserts all of the following:

- the override creates exactly one new row carrying `sprk_canonicaldocument` pointing at the canonical;
- nothing is deleted, and the response is not redirected to the canonical;
- a later version save of that copy with edited bytes severs the link.

`ContentDedupDetector.cs` is not modified, so escalation trigger 2 does not fire. The override never
reaches the suppress path, so escalation trigger 1 does not fire.
