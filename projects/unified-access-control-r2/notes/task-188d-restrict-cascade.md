# Task 188 slice D — document links Delete = Restrict

> Branch `uac/188d-restrict-cascade` (from `origin/master`). Must merge before, or together with, #2170 (D7).
> Owner decision: round 111 item 3, option (a) Restrict (`notes/session27-owner-decisions-and-research.md`).

## 1. Why

D7 (task 188, #2170) hides a document from external callers when the communication it comes from is internal-only or
not Open. It finds the communication through `sprk_relatedcommunication`, else through the `sprk_parentdocument` chain.
Both relationships were Delete = RemoveLink (`sprk_Communication.xml:151`, `sprk_Document.xml:322` on master
`2a38e6ce6c`), so deleting the communication, or the parent .eml document, cleared the link and the documents became
visible to partners. Restrict refuses that delete while a linked document exists, so the link is kept.

## 2. What changed

| Piece | Change |
|---|---|
| Solution XML | `sprk_document_RelatedCommunication_sprk_communication` and `sprk_document_ParentDocument`: `CascadeDelete` RemoveLink → Restrict. Archive (RemoveLink), Assign, Share, Unshare, Reparent, RollupView untouched. Restrict + Archive RemoveLink already exists on 9 other relationships in this solution, so the pair is valid. |
| `scripts/Set-DocumentLinkDeleteRestrict.ps1` | Copy of `Set-DocumentAnalysisCascadeSchema.ps1`: dry run by default, `-Apply`, `-Verify`, solution membership via `scripts/common/DataverseSolutionMembership.ps1`, publish on `-Apply`. Adds (b): `-Verify` also reads the two relationships in the solution XML and fails unless both read Restrict. Adds an offline `-SelfTest` of the XML reader (2 must-fire, 1 must-not-fire, 1 missing). |
| `DocumentCheckoutService.DeleteCoreAsync` | After the checkout check and BEFORE the SPE file step: counts documents whose `_sprk_parentdocument_value` is this document (app-only, any state, cap 5000). Any → `HasChildDocumentsDeleteResult(count)`, nothing deleted. A failed read throws before any delete (fail closed), like the document read before it. |
| `ProblemDetailsHelper.DocumentHasChildDocuments` | The ONE 409 shape: `reasonCode` = `document_has_child_documents`, `childCount` when given, `traceId`. |
| `DELETE /api/documents/{id}` | Maps `HasChildDocumentsDeleteResult` → the 409 with the count. |
| `DELETE /api/v1/external/documents/{id}` | Maps it → the 409 WITHOUT a count: a child the external caller cannot see (D7) must not be counted to them. |
| `DELETE /api/v1/documents/{id}` (legacy, `IDocumentDataverseService`) | Counts children with `GetDocumentsByParentAsync` before `DeleteDocumentAsync`; any → the 409 with the count. Before this, the Restrict refusal would have surfaced as the route's generic 500. |
| I-14 (`DATAVERSE-WRITE-PATH-ARCHITECTURE.md`) | One sentence: the Restrict cascade keeps the link; enforced by the script's `-Verify` and the delete-path tests. |

Why the check must precede the file step: both document delete policies delete the SPE file BEFORE the row. A Restrict
refusal at the row step would leave a row whose file is gone (BestEffort) or answer 500 "file deleted, row not"
(VerifiedFileFirst, external route).

### CLAUDE.md §11

**The script** — (1) Existing: `Set-DocumentAnalysisCascadeSchema.ps1` sets a different cascade on different
relationships; no script touches these two. (2) Extension: that script's relationship list and target value are
constants of its decision (round 34, Cascade); adding Restrict rows would give one script two targets and two owner
decisions, so it is copied, as the brief and that script's own pattern direct. (3) Cost of doing nothing: there is no
repeatable live apply or check of these two cascades; a solution import or a hand edit could put RemoveLink back
unseen, and documents would become visible to partners again.

**The refusal code `document_has_child_documents`, `HasChildDocumentsDeleteResult`, `ProblemDetailsHelper.DocumentHasChildDocuments`**
— (1) Existing: `document_locked` (409, checkout) and `has_related_records` (409, external service request with related
records) are other conditions; none means "this document is a parent". (2) Extension: reusing `document_locked` would
make the model-driven app and the SPA tell the user the document is checked out; `has_related_records` is the service
request's code on a different table. One new result type in the existing `DeleteResult` family and one helper shared by
the three routes. (3) Cost of doing nothing: under Restrict the internal route would delete the file and then fail the
row (a row with no file, answered 500), the external route would answer 500 `document_row_not_deleted` after deleting
the file, and the legacy route would answer a generic 500.

## 3. Delete paths (master `2a38e6ce6c` + this branch)

| Path | File:line | Deletes | Under Restrict | Action |
|---|---|---|---|---|
| Internal single-document delete `DELETE /api/documents/{id}` | `Api/DocumentOperationsEndpoints.cs:80` → `Services/DocumentCheckoutService.cs:781` `DeleteCoreAsync` (row delete `:879`) | sprk_document (+ SPE file) | Parent with children: refused by Dataverse after the file was deleted | **Fixed**: pre-check `:824`, 409 with count (`DocumentOperationsEndpoints.cs:497`). Tests: `ExternalDeleteOwnTests.InternalDelete_ParentWithChildDocuments_IsRefused_WithTheCount_BeforeTheFile`, `DocumentDestroyAuthorizationTests.DeleteDocument_OfAParentWithChildDocuments_Is409_WithTheCount_AndDestroysNothing` |
| External delete-own `DELETE /api/v1/external/documents/{id}` | `Api/ExternalAccess/ExternalProjectDataEndpoints.cs:507`, `:2597` → same `DeleteCoreAsync` (VerifiedFileFirst) | sprk_document (+ file) | Same, answered 500 `document_row_not_deleted` after the file went | **Fixed**: same pre-check; 409 without count (`:2642`); a failed child read → 503 `delete_own_unavailable`, nothing deleted. Tests: `ExternalDeleteOwnTests.Document_WithChildDocuments_Is409_NamesNoCount_AndNothingIsDeleted`, `Document_WhenTheChildDocumentReadFails_Is503_AndNothingIsDeleted`; must-not-fire `Delete_OfAChildDocument_IsNotRefused` |
| Legacy `DELETE /api/v1/documents/{id}` | `Api/DataverseDocumentsEndpoints.cs:311` → `IDocumentDataverseService.DeleteDocumentAsync` (`Spaarke.Dataverse/DataverseServiceClientImpl.cs:1088`) | sprk_document row only (no file — pre-existing) | Raw Dataverse refusal → generic 500 | **Fixed**: pre-check `:345`, 409 with count. Test: `DocumentDestroyAuthorizationTests.DeleteDataverseDocument_OfAParentWithChildDocuments_Is409_WithTheCount_AndDestroysNothing` |
| Communication "delete" `DELETE /api/communications/{id}`, `/threads/{threadId}` | `Api/CommunicationEndpoints.cs:297`, `:283` → `ThreadResolver.DeactivateMessageAsync` | Nothing: statecode Inactive / statuscode Deleted (soft delete) | Unaffected | None |
| Unresolved external post | `Services/Communication/CommunicationService.cs:1452` | Nothing: deactivates (soft delete) | Unaffected | None |
| Core-record delete (planner / executor, `notes/core-record-delete-design.md`) | Not on master: no `CoreRecordDelete*` code exists | — | — | Not implemented. The design must order documents children-first, then each communication after its documents (§4 below) |
| AI tool `dataverse.delete_record` | `Services/Ai/Handlers/DataverseDeleteRecordHandler.cs:162` (User OBO, any table) | Any row the user may delete, incl. sprk_document / sprk_communication | Dataverse refusal → `ToolResult.Error` with Dataverse's own message and a mapped code (`DataverseUserClient.MapErrorCode`); never a 500 | None. The frozen GA contract is "fails with the user's own Dataverse error". Known limit: the message is Dataverse's wording, not the 409 code |
| External `DeleteRecordAsync` (todo / event / service request) | `Infrastructure/ExternalAccess/ExternalDataService.cs:2728` | Refuses `DeletableKind.Document` (`:2730`); never a communication | Unaffected | None |
| Secure-root create rollback | `Services/Access/SecureRootInheritance.cs:1200` | sprk_project / sprk_workassignment just created | Unaffected | None |
| Compensation deletes (analysis promote, reports, invoice, workspace layout, membership junction) | `AnalysisEndpoints.cs:1264`, `ReportingEndpoints.cs:508/637`, `InvoiceReviewService.cs:1127`, `WorkspaceLayoutService.cs:797`, `MembershipJunctionUpdater.cs:244` | Other tables | Unaffected | None |
| Email processing (`IncomingCommunicationProcessor`, `EmailAttachmentProcessor`) | — | No document or communication delete found | — | None |
| Office add-in / undo | — | No document or communication delete in `Services/Office` | — | None |
| Retention / cleanup jobs | `SessionFilesCleanupJob`, `RagIndexingPipeline` | AI Search index entries only | Unaffected | None |
| `scripts/seed-uat-communication-corpus.ps1 -Clean` | `:35` | sprk_communication rows with the UAT marker | Refused for a seed communication that a document links to (the seed creates none; only a later hand link) | None; the Web API error names the row. Noted |
| `scripts/Repair-ComposeIdentityKey.ps1 -Strategy Delete` | `:213` | Duplicate compose sprk_document rows | Refused for a duplicate that is a parent (compose documents are not parents); the try/catch records it under failures | None |
| `scripts/Load-DemoSampleData.ps1` | — | Demo documents carry no parent or communication link | Unaffected | None |

## 4. Core-record delete design — amendment for the main session

`notes/core-record-delete-design.md` lives only on the project branch, not on master, so this branch cannot edit it.
These statements in it assume RemoveLink and need amending for these two relationships:

- §3.2 (ordering, line ~212): "RemoveLink empties a child's lookup when its parent goes" → for
  `sprk_relatedcommunication` and `sprk_parentdocument` the platform now REFUSES a parent delete while a child exists.
  The executor must delete documents children-first (attachments before their parent .eml, deepest first), and every
  document linked to a communication before that communication. A Restrict refusal during the run is a Partial
  (the parent kept), never an orphan.
- D1 (line ~241) "Relationship metadata stays RemoveLink" and the known-limit row (line ~264) "Every relationship is
  RemoveLink": add "except `sprk_document_RelatedCommunication_sprk_communication` and `sprk_document_ParentDocument`,
  Restrict (task 188 slice D, owner round 111 item 3); a delete that bypasses the BFF is refused there instead of
  orphaning".
- D8: deleting a communication's documents first is now required, not only chosen.

## 5. Single-document delete of a parent: (i) refuse, not (ii) delete the children

Recommendation **(i)**, implemented. Evidence that the product does not treat an .eml and its attachments as one unit
on delete:

- The model-driven app command (`src/client/webresources/js/sprk_DocumentOperations.js:1080`) confirms "This will
  permanently delete "<name>" and its file" — one document, one file.
- The shared hook (`Spaarke.DocumentOperations/src/hooks/useDocumentActions.ts:203`) and the SPE viewer PCF
  (`SpeDocumentViewer/control/BffClient.ts:231`) delete the ids they are given, one call each.
- No server code deletes child documents: `GetDocumentsByParentAsync` is used only by AI analysis and visualization
  (reads). Under RemoveLink, deleting an .eml KEPT its attachments (unparented), so today they are separate records.

(ii) would newly delete records and files the user did not select: a user-visible behaviour change.

Model-driven app message (coordinator, 2026-10-21; done in this PR). The delete command used to show every 409 as
"checked out". `sprk_DocumentOperations.js` 1.28.2 (`Spaarke.Document.Utils.getDeleteConflictMessage`) handles the
cases separately:
- `document_has_child_documents`: "This document has N attached documents. Delete them first, then delete this
  document."
- `document_locked` (the checkout body's `error`): the checked-out message, as before.
- Any other 409: a generic message plus the server's detail.

Both copies are changed and stay identical: `src/client/webresources/js/` and
`src/dataverse/solutions/SpaarkeMaster/WebResources/`. Test:
`Spaarke.UI.Components/src/__tests__/documentOperations.deleteConflict.test.ts`.

**Deploy step (manual, not run):**
`scripts/Deploy-WebResourceInline.ps1 -DataverseUrl <env> -WebResourceName sprk_DocumentOperations.js
-FilePath src/client/webresources/js/sprk_DocumentOperations.js -WebResourceType 3`. This uploads and publishes the
one web resource. It is not in `Deploy-AllWebResources.ps1`, and it also ships in SpaarkeCore through
`Import-SolutionScoped.ps1`.

Archive cascade: filed as **#2187** (label `bug`). `CascadeArchive` stays RemoveLink on both relationships, because
the brief said to leave other cascades alone. If Dataverse long-term retention is ever enabled for sprk_communication
or sprk_document, archiving a communication would clear the link the same way. Retention is not enabled today.
Dataverse's allowed Archive values must be checked before choosing a fix.

## 6. Live (spaarkedev1, read-only, 2026-10-21)

`-Apply` NOT run (main-session gate).

Dry run: both relationships unmanaged, customizable, `Delete=RemoveLink, Archive=RemoveLink`, everything else
NoCascade; both in SpaarkeCore via `sprk_document` (subcomponents included); XML reads Restrict.

`-Verify` (exit 1, expected before `-Apply`):

```
(a) MISSING  sprk_document_RelatedCommunication_sprk_communication Delete = Restrict (is RemoveLink)
    MISSING  sprk_document_ParentDocument Delete = Restrict (is RemoveLink)
(b) OK       sprk_Communication.xml ... CascadeDelete = Restrict
    OK       sprk_Document.xml ... CascadeDelete = Restrict
(c) OK       both relationships in SpaarkeCore (sprk_document includes subcomponents)
VERIFY FAIL (2)
```

Counts (no ids):

| Measure | Count |
|---|---|
| Documents, total | 596 |
| Documents with `sprk_relatedcommunication` set | 76 |
| **Communications with ≥ 1 linked document** (deletes Restrict will refuse) | **49** |
| Documents with `sprk_parentdocument` set | 77 |
| **Documents with ≥ 1 child document** (deletes Restrict will refuse) | **46** |
| **Documents linking to a communication that no longer exists** | **0** (76 set, 76 resolve) |
| Documents linking to a parent document that no longer exists | 0 (77 set, 77 resolve) |
| Proxy: .eml archives (`sprk_isemailarchive`) | 172 |
| Proxy: .eml archives with NO communication link | 120 |
| Audit: Delete operations recorded on sprk_communication / sprk_document | 70 / 754 |

Reading the orphan count: RemoveLink empties the lookup when the parent is deleted, so a dangling id cannot exist; 0 is
expected and does not mean nothing was un-hidden. The already-unhidden set is among the 120 .eml archives with no
communication link (and their attachments), but that set also holds archives created before communications existed,
so it is an upper bound, not a count. The 70 communication deletes in audit bound how many communications could have
released documents.

## 7. Security pass 2: the delete race (known limit, now a clean failure)

**The race.** A child document can be created between `CountChildDocumentsAsync` (step 1b) and the row delete (step 3).
The email pipeline opens this window when it creates attachments. Before this fix, the file was already deleted, then
Restrict refused the row. The internal route answered 500 "Delete Failed", the external route answered 500
`document_row_not_deleted`, and the legacy v1 route answered a generic 500. The row was kept without its file.
Security held: the link was kept.

**The fix.** Each of the three routes maps Dataverse's Restrict refusal at the row delete to the same 409
`document_has_child_documents`:

- **The error code.** The fix matches the error code, never the message text (messages are localized). The code is
  `0x80040227` (`-2147220953`), `CannotDeleteDueToAssociation`: "The object you tried to delete is associated with
  another object and cannot be deleted." The source is Microsoft Learn, "Web service error codes (Microsoft Dataverse)"
  (`power-apps/developer/data-platform/reference/web-service-error-codes`). In the solution XML, the only Restrict
  relationship that has `sprk_document` as its parent is `sprk_document_ParentDocument`. So on a document delete, this
  code means that child documents exist.
- **Web API path** (`DocumentCheckoutService.DeleteCoreAsync`, internal and external routes). The fix reads
  `error.code` from the failed DELETE's body with the existing `DataverseContactIdentityStore.ErrorCodeOf`, whatever the
  HTTP status. A match returns `HasChildDocumentsDeleteResult(ChildCount: null, FileGone: true|false)`.
  - `FileGone` is true when the file step removed the file or found it already missing.
  - The log entry records the document id, the code and `FileGone`. It records no names.
- **SDK path** (legacy `DELETE /api/v1/documents/{id}`, `ServiceClient`). The route catches a
  `FaultException<OrganizationServiceFault>` whose `ErrorCode` is `0x80040227`, in the exception or any inner exception,
  and answers the 409. This route deletes no file, so nothing was deleted.
- **The response.**
  - It never includes a count, because Dataverse's refusal names none. The external route also never counts children.
  - When `FileGone` is true, the detail says "The document's file was deleted, but its record was kept …" instead of
    "Nothing was deleted", because that would be false.
  - The model-driven app shows its no-count message, "This document has attached documents …".
- **Order unchanged.** The file is still deleted first, then the row. A row deleted before its file would leave an
  unreachable file.

**Retrying after the children are deleted:**

| Route | Retry | Why |
|---|---|---|
| Internal `DELETE /api/documents/{id}` (BestEffort at the time of this fix; VerifiedFileFirst since R3 ISS-004, see the external row) | Succeeds | `SpeFileStore.DeleteFileAsync` returns false for a 404, which counts as already deleted, so the row is removed. Test: `InternalDelete_Retry_AfterTheChildrenAreDeleted_DeletesTheRowWhoseFileIsGone` |
| Legacy `DELETE /api/v1/documents/{id}` | Succeeds | No file step. Test: `DeleteDataverseDocument_Retry_AfterTheChildrenAreDeleted_Is204` |
| External `DELETE /api/v1/external/documents/{id}` (VerifiedFileFirst) | **Refused** with 409 `document_storage_unverified` (as of this fix); **succeeds since R3 ISS-004 review F1 (2026-10-22)** | The pointer check (`EnsureDocumentPointerContainerAsync`, interim and strict rules) refuses an item that is not in the drive ("the item is not in the drive the row names") before `DeleteFileOrRefuseAsync` reaches `DeleteFileAsync`. That was the task 032 known limit. The delete now asks `VerifyDocumentPointerForDeleteAsync`, which reads a bound pointer (`sprk_graphitemidbound` equals the item) whose item Graph reports gone (404) from an owned container as already deleted, so the retry deletes the row and touches no file. The same holds for the internal route, which since ISS-004 uses the same policy (the row above is the pre-ISS-004 BestEffort behaviour). Tests: `ExternalDeleteOwnTests+PointerCheckOnDelete_InterimRule` and `..._StrictRule`, `Retry_AfterTheChildrenAreDeleted_DeletesTheRowWhoseFileIsGone` and `ExternalRoute_AlreadyGone_BoundPointer_OwnedContainer_Is204_AndTouchesNoFile`. |

**Tests** (`ExternalDeleteOwnTests`, `DocumentDestroyAuthorizationTests`):

- Must-fire:
  - `Document_WhenAChildAppearsAfterTheCount_TheRestrictRefusal_Is409_NotA500_AndTheRowIsKept`: external, real service,
    the child is created during the file delete.
  - `InternalDelete_WhenAChildAppearsAfterTheCount_IsHasChildDocuments_WithTheFileGone` with Restrict status 400 and
    500.
  - `DeleteDocument_WhenRestrictRefusesTheRowAfterTheFile_Is409_WithoutACount_NotA500`: internal route mapping.
  - `DeleteDataverseDocument_WhenRestrictRefusesTheRow_Is409_WithoutACount_NotA500`: v1, with an SDK fault.
- Must-not-fire:
  - `Document_WhenTheRowDeleteFailsWithAnotherDataverseCode_IsStill500` (`0x80040220`).
  - `DeleteDataverseDocument_WhenTheRowDeleteFailsWithAnotherFaultCode_IsStill500`.
- The test double `DocumentRowsHandler` now enforces Restrict on DELETE, as the live schema does.

**Mutants:**

| Mutant | Result |
|---|---|
| M1: service mapping removed | 4 tests fail. The external route answers 500 `document_row_not_deleted` |
| M2: v1 catch removed | 2 tests fail. v1 answers 500 |
| M3: detail ignores `FileGone` | 2 tests fail |

### CLAUDE.md §11 (new surface)

`CannotDeleteDueToAssociationCode`, the two `IsCannotDeleteDueToAssociation` predicates, `FileGone` and `fileGone`:

1. **Existing.** `RecordContainerResolver.IsRecordNotFound` and `DocumentUrlIdentityResolution.IsAlternateKeyRecordDoesNotExist`
   match other codes. `DataverseContactIdentityStore.ErrorCodeOf` parses a Web API error body, and it is reused here.
   Nothing matched `0x80040227`.
2. **Extension.** The predicates sit beside the one delete that needs them and are shared with the v1 route.
   `HasChildDocumentsDeleteResult` and `ProblemDetailsHelper.DocumentHasChildDocuments` are extended with optional
   members, not duplicated.
3. **Cost of doing nothing.** The race answers 500 on every route. On the external route, the detail calls the
   document "not fully deleted" and gives no reason.
