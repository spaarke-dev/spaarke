# Task 144 input — email Document / Communication filing alignment: write-path inventory

> 2026-11-27. Read-only code map (code-mapper, Sonnet) over master d9595dd98e plus task 143's branch. Facts are file:line on that tree; re-locate by name before acting. Several rows are from grep only (see "Coverage").

## Owner rule (2026-11-27)

> "an email (.eml) is saved to the system as a Document and also has a corresponding Communication record. If the user sets Related to a record then the Document and the Communication should both be associated to that record; this should always be aligned."

Task 143 applies the READ side (one "filed" rule on the pane and the server). Task 144 is the WRITE side: every path that files one of the two must leave both filed to the same record.

## Paths that write the Communication's filing

| # | Path | Write | Writes the .eml too? | Trigger |
|---|---|---|---|---|
| 1 | `IncomingAssociationResolver` | `Services/Communication/IncomingAssociationResolver.cs` ~:532/:550 (update), ~:594 (status) | No | Inbound / mailbox capture; `ApplyToExistingRecordAsync` (user paths) |
| 2 | `ReconciledEmailFiling.FileIfUnfiledAsync` (tasks 121, 140; this project) | `Services/Office/ReconciledEmailFiling.cs` ~:209-212, ~:347 | No — the save's own .eml is created filed, but a communication already filed elsewhere is not moved, so the two can differ | Office email save |
| 3 | `PATCH /api/communications/{id}/filing` (re-file) | `Api/CommunicationEndpoints.cs` ~:144 → `Api/ChildRecordEndpoints.cs` ~:858 `OwnedChildWrite.RefileAsync`, `AfterRefileAsync` ~:932 | No .eml cascade found | Reconciliation / review re-file |
| 4 | Review panel Confirm (`advanceAssociationStatus`) | endpoint not located; status only per `ReconciledEmailFiling.cs` comments | No | User |
| 5 | `CommunicationService.MarkFiledBySender` | `Services/Communication/CommunicationService.cs` ~:3699 | No (status at send) | Send |
| 6 | `CommunicationThreadFilingSweepJob` | `Services/Communication/CommunicationThreadFilingSweepJob.cs` ~:28-45 | No (thread anchor/owner only) | Background, off by default |
| 7 | `EmailUploadCaptureService` | `Services/Communication/EmailUploadCaptureService.cs` ~:266, ~:495-502 | Document via `OfficeDocumentPersistence` | Office save |

## Paths that write the Document's filing

| # | Path | Write | Writes the Communication too? | Trigger |
|---|---|---|---|---|
| 8 | `OfficeDocumentPersistence` | `Services/Office/OfficeDocumentPersistence.cs` ~:47, ~:1060-1077 | No (communication via #2) | Office save |
| 9 | `UploadFinalizationWorker` | `Workers/Office/UploadFinalizationWorker.cs` ~:812, ~:850, ~:1444 | No | Queue |
| 10 | Spaarke send, .eml archive | `CommunicationService.cs` ~:4031-4088 (`StageArchiveRecordLink`) | Copies the communication's record (outbound only) | Send / on-demand archive |
| 11 | Send-time attachment archive | `CommunicationService.cs` ~:708-721 | No record link | Send |
| 12 | `MessageAttachmentMaterializer` | `Services/Communication/MessageAttachmentMaterializer.cs` ~:274-280 | No record link | Message attachment |
| 13 | `EmailArchiveCommunicationLinkSweepJob` | `Services/Communication/EmailArchiveCommunicationLinkSweepJob.cs` ~:48, ~:499 | Link only, not filing | Background |
| 14 | `PUT /api/v1/documents/{id}` re-file | `Api/DataverseDocumentsEndpoints.cs` ~:269 | No | User |

## Misalignments found

- Re-filing the communication (#3) does not move its .eml or attachment documents; re-filing the document (#14) does not move the communication.
- #2 leaves a communication filed elsewhere where it is while the new .eml is filed to the save's record.
- Inbound .eml archives, send-time attachments (#11) and materialized attachments (#12) are created with no record link; only the outbound .eml (#10) copies the communication's record.

## Reusable one-side helpers (no two-side helper exists)

`IncomingAssociationResolver.ApplyToExistingRecordAsync` / `NarrowToRecord`; `ReconciledEmailFiling.FilingColumns`; `DocumentAssociationMap.TryApply`, `DocumentLinkFields.All`; `CommunicationService.StageArchiveRecordLink` (document from communication); `CrossPathLink`; `OwnedChildWrite.RefileAsync`, `CoreAncestorRestamper.AfterWriteAsync`, `SecureChildReconciler.AfterRefileAsync`.

## Ownership (from code comments)

- uac-r2: secure filing, ownership and re-file (`OwnedChildWrite`, `ChildRecordEndpoints`, `SecureChildReconciler`, `CoreAncestorRestamper`).
- field-mapping-r1: the archive create (`CommunicationService` ~:4049-4055) and the Office create paths under the fm W3 direction (project CLAUDE.md).
- word-add-in-r1: `ReconciledEmailFiling` (121, 140).
- Not stated: `UploadFinalizationWorker`, `OfficeDocumentPersistence` (fm W3 note treats them as fm-coordinated).

## Owner's target model (2026-11-27) and the code review against master 60933c82be

Owner's nine points, verdicts from three read-only code reviews (Sonnet code-mappers, code read, key claims re-checked by the main session):

| # | Owner's point | Verdict | Evidence / gap |
|---|---|---|---|
| 1 | Inbound mail to a mapped mailbox creates a Communication | Supported | `IncomingCommunicationProcessor.cs` ~:152-172, ~:320-329 (receive-enabled accounts) |
| 2 | …and an .eml Document in SPE (and attachments) | Supported for .eml; attachments partly | .eml `ArchiveEmlDocument` ~:1578-1601 sets `sprk_relatedcommunication`; archive is default-on, opt-out `sprk_archiveincomingoptin`. Attachments only with `sprk_autocreaterecords`; linked to the communication, NOT parented to the .eml |
| 3 | Reconciliation / Communication form sets Related to → Communication filed | Supported | all surfaces → `PATCH /api/communications/{id}/filing` (`CommunicationEndpoints.cs` ~:144 → `ChildRecordEndpoints.UpdateAsync` filingOnly) |
| 4 | The .eml Document is also filed to that record | **Not supported** | No path writes a record lookup on a document when its communication is filed; `CoreAncestorResolver.StampSourceColumns` comment: a document carries none of the stamp columns. Documents follow the communication for owner/access/shares only. Inbound .eml and attachments never get a record lookup at all |
| 5 | Changing the Communication's Related to changes the Document | **Not supported** | same as 4 (re-file restamps to-dos/events/analyses/memos, not documents) |
| 6 | Add-in save creates Communication + .eml + attachment Documents, all filed to the picked record | Supported, except when the Communication already exists filed to X and the user picks Y | `ReconciledEmailFiling` never moves a filed communication; the .eml and children go to Y → split, reported only as `AlreadyFiled` (no pane notice) |
| 7 | An email already in Spaarke shows its status in Outlook | Supported when an .eml exists (own record, else the communication's, Read-checked); **not supported** when only the Communication exists (pane shows a plain first save with engine suggestions, nothing says "filed"); when .eml and communication disagree only the .eml's record is shown | `DocumentUrlIdentityResolution.cs` ~:436-547; `communicationSuggestionsService.ts` |
| 8 | Changing Related to in Outlook re-files Communication and Document; "save new" possible | **Not supported** (save-new is) | Saved view has no re-file control; "File to record" (unfiled only) calls `PUT /api/v1/documents/{id}`, which writes the document only |
| 9 | Attachments saved earlier (unfiled) follow when the email is saved | **Not supported** | The email save never touches existing standalone documents and creates a second child copy of each ticked attachment under the .eml (child skip rule reads only .eml children; no dedup for children) |

Correction to the inventory above: inbound archives and attachments DO get `sprk_relatedcommunication`; what they never get is a record lookup.

## Coverage

Read fully: `ReconciledEmailFiling.cs`. Excerpts: `ChildRecordEndpoints.cs`, `CommunicationService.cs`, `CommunicationThreadFilingSweepJob.cs`. Grep only: the rest. Not read: full `UploadFinalizationWorker`, `OfficeService`, `IncomingCommunicationProcessor`, `ThreadResolver`, `src/server/shared`. Review-page Confirm endpoint not located.
