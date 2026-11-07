# Task 034 — inbound ingestion creators: named exceptions, pinned by guards; the attachment creator files in its create

Status: **done under the W3 direction** (decisions.md 2026-11-06 "W3 direction", its 034 bullet; 033 dropped 2026-11-07).
Branch `task/fm-034-w3` (pushed to `origin/task/fm-034` as a fast-forward). Rigor: FULL (the task changes `tests/**` and three `.cs` files).
The inbound ingestors stay OUTSIDE `RecordCreationService`. One production change, in the inbound attachment creator (below).

Section order: result, creators and tables, per-creator determination, guards and controls, mutation and fail-first
evidence, the attachment change, census, owner questions, section 10, history.

## Result

- Every creator of an inbound communication, attachment row or attachment document is named and closed: seven
  creators (C1 to C7), five source files. A new file that creates one of those tables fails the guard.
- For each creator the guard pins the owner classes it calls for every invariant its table carries, and that nothing else
  writes an owner.
- The dedupe on the message id is pinned on all three paths (mailbox, upload capture, chat), including the race-proof
  create itself.
- The unfiled-communication creator exception is pinned to the ingestor path: the unified service still refuses an
  unfiled create on an `OwnershipException` table, and the ingestor files are the only creators.
- **C6 (`EmailAttachmentProcessor`) now carries its parent link and its association in the create**, as task 030b does
  for the Office save (coordinator instruction, 2026-11-07). A guard pins it; the behaviour tests failed first.
- Mapping into inbound rows is an owner question (below).

## Creators and tables (closed set, found by grepping `src/server` for the inbound ingestion code)

| # | Creator (file, under `Sprk.Bff.Api/Services/`) | Table(s) it creates | How |
|---|---|---|---|
| C1 | `Communication/Channels/MessagingIngestor.cs` | `sprk_communication` | one generic create, always unfiled |
| C2 | `Communication/IncomingCommunicationProcessor.cs` `CreateCommunicationRecordAsync` | `sprk_communication` | `CreateCommunicationRaceProofAsync` (UNIQUE key `sprk_internetmessageid`) |
| C3 | same file, `ProcessIncomingAttachmentsAsync` | `sprk_document`, `sprk_communicationattachment` | generic creates, `ApplyOwner(row, owner)` |
| C4 | same file, `ArchiveEmlAsync` / `ArchivedEmlDocument` | `sprk_document` | generic create, `ApplyOwner(document, owner)` |
| C5 | `Communication/EmailUploadCaptureService.cs` | `sprk_communication` | `CreateCommunicationRaceProofAsync`, stamped before create |
| C6 | `Email/EmailAttachmentProcessor.cs` | `sprk_document` | document seam create carrying the parent link and association; the update writes the file pointer only. No caller today |
| C7 | `Communication/MessageAttachmentMaterializer.cs` | `sprk_document`, `sprk_communicationattachment` | two generic creates, `owner.ApplyTo(row)` |

Not creators of these tables, and left out on purpose: `ThreadResolver`, `DirectThreadAccessService` (threads),
`CommunicationParticipantIndexer`, `CommunicationEnrichmentService`, `CommunicationInternalOnlyFloor`,
`DeliveryContextMerge`, the outbound `CommunicationService.cs` (a user's own send; its own census rule), and the Office
save path (task 030b).

## Per-creator determination (AC9)

Invariants: I-1 core-ancestor stamp (`CoreAncestorResolver`, applied by `IncomingAssociationResolver` to a communication),
I-2/I-6 record-first and secure-if-any owner (`IRecordOwnershipResolver`), I-3 mapping, I-4 container
(`CommunicationContainerResolver`), I-5 search index, I-7 membership. A `sprk_document` has no stamp column of its own;
its root is read from its typed lookups, so the lookups must be written in its create.

| Creator | I-2/I-6 owner | I-1 | Other | Verdict |
|---|---|---|---|---|
| C1 | none by design: the row names no parent, so it keeps its creator (registry rule `UnfiledCommunicationKeepsCreator`, task 146 E1, owner round 10 item 8). Guard: the file names no owner, no resolution, no filing column, no lookup. | none: never filed at create; a later thread JOIN re-derives the owner (`ReparentAsync`) and stamps | dedupe: ACS id in the job handler | complete |
| C2 | `ResolveOwnerAsync` once, before the create; owned writes the resolver's team and nothing else writes `ownerid`; a refusal holds the email | `ApplyToNewRecordAsync` runs after the create, only past the duplicate check | | complete |
| C3 | carried: `ApplyOwner` before each create, the calls pass the same `owner` | `sprk_relatedcommunication` | | complete |
| C4 | carried, as C3 | as C3 | | complete |
| C5 | as C2, plus `StampCreatorOn(communication)` before the create; a refusal skips the capture | as C2 | `RequestedBy` only from the authenticated saver | complete |
| C6 | `ResolveOwnerAsync(OwnershipOfAttachmentDocument(...))`, refuse without an owner, create with the resolver's team | parent link and association now in the create (was: a follow-up update) | | fixed in scope (below) |
| C7 | `ResolveOwnerAsync` before any bytes move; refusal stops; `owner.ApplyTo` on both rows before their creates | `sprk_relatedcommunication` | `RequestedBy` from the request | complete |

Correction to an earlier pass of this task: it described C6 as missing an "I-1 stamp" and pinned the processor
unreachable until one was added. That premise did not hold (a document is rooted from its lookups, and the Office writers
call no stamp). The real gap was that the lookups were written in a follow-up update, so the row existed unfiled between
two writes and stayed so if the update failed. The unreachable-until-stamped pin is removed; the create-carries-links pin
replaces it.

## Guards (both in `tests/Spaarke.ArchTests/`, a KEEP path)

`InboundIngestionCreatorsGuardTests`:
- the creator set is exactly C1 to C7 for `sprk_communication`, `sprk_communicationattachment` and the ingestion
  documents; a per-file count of each create shape (a new create site in a known file fails too);
- C1 always-unfiled; C2/C5 owner order (`EvaluateAsync` → `OwnershipContextForNewRecordAsync` → `ResolveOwnerAsync` →
  `BuildNewRecordFieldsAsync` → race-proof create) and exactly one owner resolution; C3/C4 carried owner; C6 owner calls;
  C7 owner calls and order; every `ownerid` write is the resolver's team under `owner.IsOwned` (a column-list read is
  not a write); no user owner;
- no inbound principal: `RequestedBy` is assigned once, from the authenticated saver (C5) or the request (C7), and
  nowhere else;
- the unified service still refuses an unfiled create on an `OwnershipException` table;
- an attachment carries its parent link and its association in the create, never in the follow-up update (C6), and the
  client's one document create stages both (`StageDocumentAssociation`, `StageDocumentParent`).

`InboundIngestionDedupeGuardTests`:
- D1 mailbox: Graph id → canonical internet id → filing → create order; the create is the keyed race-proof call and
  never a plain create; the row writes both ids; the `if (wasDuplicate)` branch returns before the association, thread,
  participants, attachments, archive and enrichment;
- D2 upload: the keyed race-proof create; a reconciled save returns `ReconciledToExisting: true` before association,
  thread and enrichment; the row writes the internet id;
- D3 `CreateCommunicationRaceProofAsync`: a blank id creates unguarded; a UNIQUE-key duplicate reconciles to the
  existing row and reports a duplicate; an unreadable canonical row surfaces; no retry loop;
- D4 chat: key from the ACS id, check, lock, re-check, ingest, then mark, then release; `MessagingIngestor` writes the
  id the handler dedupes on; the handler is the only `IngestAsync` caller.

Controls (AC8): each rule has must-fire rows and must-not-fire rows. Must-fire rows are in-memory mutations of the real
source (the row asserts the replaced text exists, so a control cannot go vacuous silently). Must-not-fire rows: a
comment that names every forbidden token, and a whitespace-only change, for each ingestor. Plus controls for the closed
set, the service refusal, the attachment-links rules and the block reader. Known limit: a control pins the text it
mutates, so an edit to one of those exact lines also fails its control with "control is vacuous: ..." and names the
file; re-point the row.

## Mutation and fail-first evidence (AC8)

Production mutations (each applied to the real file on disk, both dedupe/creator guards run `--no-build`, file restored
byte for byte; `git status` showed no `src` change). Failing guard assertion per mutation:

| Mutation | Failing assertion |
|---|---|
| Mailbox `if (wasDuplicate)` disabled | `TheMailboxPath_DedupesOnTheMessageId_AndStopsOnALostRace` |
| Mailbox owner written as a user | `EachIngestor_CreatesWhatTheInventoryNames_AndCallsItsOwners` |
| Chat ingestor names an owner | `EachIngestor_...` ("MessagingIngestor names an owner") |
| Chat ingestor stops writing `sprk_acsmessageid` | `TheChatPath_DedupesOnTheAcsMessageId_BeforeAndAfterTheIngest` |
| Chat handler marks processed before the ingest | `TheChatPath_...` ("marks the message processed before the ingest") |
| Race-proof loser reports not-a-duplicate | `TheRaceProofCreate_ReconcilesAUniqueKeyDuplicate_AndNeverRetries` |
| Upload `if (wasDuplicate)` disabled | `TheUploadCapture_...` ("has no `if (wasDuplicate)` branch") |
| Unified service refusal code removed | `TheUnifiedService_StillRefusesAnUnfiledCreateOnAnOwnershipExceptionTable` |
| New `sprk_communication` creator in another file | `TheCreatorsOfTheInboundTables_AreExactlyTheInventory` (names the file) |

Fail-first for the attachment change (the old processor restored with the new contract in place, then the new processor
restored and confirmed identical to the edited file):
- guard, old processor: `EachIngestor_CreatesWhatTheInventoryNames_AndCallsItsOwners` fails with "does not carry the
  attachment's parent link in the create", "... association in the create" and "follow-up update names a parent or
  association lookup" (the five source-based control rows that start from the real source fail with it);
- behaviour, old processor: 11 of 17 new tests fail (every accepted spelling, both unsupported types, the empty-parent
  case); the 6 that pass are the stager tests and the refusal, fault and owner-question cases, which the change does not alter;
- behaviour, new processor: 17 of 17 pass. The stager tests cannot fail first against the old code in the same sense:
  `ParentDocumentLookup` and `StageDocumentParent` did not exist, so they did not compile.

## The attachment change (C6)

- `CreateDocumentRequest.ParentDocumentLookup` (`[JsonIgnore]`, server-only, in `Spaarke.Dataverse/Models.cs`) and
  `DataverseServiceClientImpl.StageDocumentParent` (pure, refuses an empty id) put `sprk_parentdocument` on the one create
  entity, beside 030b's `CreateDocumentRequest.Association` and its stager.
- `EmailAttachmentProcessor` sets the parent link (null for an empty parent) and applies the association to the create
  with the same shared map the Office save uses; the follow-up update writes the file pointer and metadata only. Before,
  an empty parent id would have been written as an empty lookup; it is now no link.
- This branch contains task 030b's commit `581f034144` (merged, not copied): the association contract is that commit's.
  Merge 030b first, or together; the two touch `Models.cs` and `DataverseServiceClientImpl.cs` in different places.
- No caller exists for `ProcessAttachmentsAsync` today, so no behaviour a user sees changes.

## Verification (2026-11-07)

- Build: `Sprk.Bff.Api` with its test project, and `Spaarke.ArchTests`: 0 errors, 0 warnings (warnings are errors).
- Filtered BFF tests (`FullyQualifiedName~Communication|FullyQualifiedName~Email|FullyQualifiedName~Ingest|FullyQualifiedName~RecordOwnership`,
  never unfiltered): 5123 passed, 2 skipped, 0 failed.
- New behaviour and stager tests (`EmailAttachmentCreateCarriesLinksTests`, `DocumentParentCreateStagingTests`): 17 of 17 pass.
- ArchTests in full: 2104 passed, 0 failed (the two new guard classes are 62 + 31 = 93 of them; the run includes task 030b's guards from the merged commit).
- Census: reason text only.

## Census

`RecordOwnerAssignmentCensusTests.cs`: reason text only, for the `MessagingIngestor.cs` Waived entry. It now names the
registry rule `UnfiledCommunicationKeepsCreator` and the new guard. No count or disposition changed.

## Owner questions

1. **Mapping into inbound rows (I-3).** No inbound creator calls the mapping engine; the unified create does, for the
   rows it owns. Putting it on an inbound row needs the app-only lookup reader that W3 keeps out of R1, and that task
   033's drop leaves unbuilt. Does the owner want inbound communications covered by task 052 (Repair) or by a later
   task, or is "no mapping for system-created communications" the accepted rule? Not built.
2. **Behaviour test for a lost mailbox race.** The mailbox dedupe is proved by structure here. A behaviour test needs
   infrastructure that is private to `Sprk.Bff.Api.Tests` (not a KEEP path). Existing tests cover the classifier
   (`AlternateKeyDuplicateClassifierTests`), the chat handler (`IncomingMessagingJobHandlerTests`) and the upload
   reconcile (`EmailUploadCaptureSeamTests`). Say if a mailbox behaviour test is wanted.
3. **`EmailAttachmentProcessor` has no caller.** Delete it, or keep it (now filing in its create). Either is safe; the
   guard fails if its creates change shape.

## Section 10 (BFF hygiene)

Three production files changed: a `[JsonIgnore]` property on `CreateDocumentRequest`, one internal static staging method,
and the processor's request building. No endpoint, DI registration, package, hosted service or new file under
`Sprk.Bff.Api`, `Spaarke.Core` or `Spaarke.Dataverse`; no new dependency, so no CVE change. The publish-size delta was
not measured against a fresh master build (as in task 030b's notes): the change is a few lines of IL and no dependency,
and a measured publish pair is left to the merge step.

## History: the earlier escalation (2026-11-06, superseded)

The first pass stopped at the POML escalation because the unified create could not honour the race-proof create, the
unfiled-keeps-creator rule, a system mapper or a carried owner without new service surface. The owner then chose W3:
keep the ingestors outside the service as named exceptions, with guards. Findings that still hold: the create sites and
their owner behaviour (table above); the service refuses an unfiled create on an `OwnershipException` table
(`DecideOwnerAsync`); the race-proof create is the dedupe for the two email ingestors. Findings that no longer apply:
the four service gaps and options A to C (no service change in R1).

rework: none task 034
