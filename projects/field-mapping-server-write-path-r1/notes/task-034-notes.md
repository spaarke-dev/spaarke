# Task 034 — inbound ingestion through the system principal: inventory and escalation

Status: **stopped at the POML escalation (step 5 / ADR-036 constraint)**. Branch `task/fm-034` from the work-branch
tip `dc08e14990`. No production code or test was changed. Steps 1 and 2 are done below; steps 3 to 9 wait for the
owner's choice.

## Step 1 — /conflict-check (2026-11-06)

- UAC-r2 PR #2110 is MERGED. No open PR touches `MessagingIngestor.cs`, `IncomingCommunicationProcessor.cs`,
  `EmailAttachmentProcessor.cs`, `EmailUploadCaptureService.cs`, `RecordTableRules.cs`, `RecordCreationService*.cs` or
  `RecordOwnerAssignmentCensusTests.cs` (checked: #2580, #2579, #2153, #1507, #1494, #2387).
- In flight in this project: tasks 030, 032 and 046c (parallel agents) may edit `RecordTableRules.cs` and the census.
  This task's census edit is limited to the four ingestors' entries.

## Step 2 — create sites and their owner behaviour today

Line numbers are on `dc08e14990`.

| Ingestor | Site | Table | Create call | Owner today |
|---|---|---|---|---|
| `MessagingIngestor` | `PersistAsync` :265 | `sprk_communication` | `_genericEntityService.CreateAsync` | none set: always unfiled at create, so the creator (the app identity) keeps it (E1). Filed later by a record-thread JOIN (`ReparentAsync`). Census: a Permanent waiver. Dedupe on `sprk_acsmessageid` is upstream (job layer), not at the create. |
| `IncomingCommunicationProcessor` | `CreateCommunicationRecordAsync` :897 | `sprk_communication` | `CreateCommunicationRaceProofAsync` (UNIQUE `sprk_internetmessageid`) | `ResolveInboundFilingAsync` evaluates the association BEFORE the create and asks `IRecordOwnershipResolver.ResolveOwnerAsync`: filed gives a team (secure-if-any) and the filing fields go on the create; unfiled keeps the creator (E1); a refusal or an undeterminable filing HOLDS the email (`RecordOwnerUnresolvedException` → retry / dead-letter). |
| `IncomingCommunicationProcessor` | `ProcessIncomingAttachmentsAsync` :1212 | `sprk_document` | `_genericEntityService.CreateAsync` | `ApplyOwner(doc, owner)`: the communication's team, or nothing (the creator) when the email is unfiled. |
| `IncomingCommunicationProcessor` | `ProcessIncomingAttachmentsAsync` :1247 | `sprk_communicationattachment` | `_genericEntityService.CreateAsync` | `ApplyOwner`, as above. Not named by the POML; the table has no `RecordTableRules` entry. |
| `IncomingCommunicationProcessor` | `ArchiveEmlAsync` :1414 (builder `ArchivedEmlDocument`) | `sprk_document` | `_genericEntityService.CreateAsync` | `ApplyOwner`, as above. |
| `EmailAttachmentProcessor` | `ProcessAttachmentAsync` :342 | `sprk_document` | `_documentService.CreateDocumentAsync`, then `UpdateDocumentAsync` with the parent document and association | `ResolveOwnerAsync(OwnershipOfAttachmentDocument(parent document, associated record))`; not owned → no row (per-attachment failure contract). |
| `EmailUploadCaptureService` | `CaptureAsync` :194 | `sprk_communication` | `CreateCommunicationRaceProofAsync` | as the mailbox path, with `RequestedBy` = the saving user's oid and the creator person stamped; a refusal SKIPS the capture (the save proceeds as an archive). Registered as a singleton. |

Direct threads: none of the four creates a thread. A Direct thread is the thread resolver's and
`DirectThreadAccessService`'s (census waiver E2). Inside the service, a message in a Direct thread already gains no
filing (`RecordThreadFiling.ParentsAsync`).

## Why the task stopped: what the service cannot do today

Read on `dc08e14990` (`Services/RecordCreation/RecordCreationService.cs`).

1. **Race-proof dedupe (POML escalation trigger 1).** Step 11 is a PATCH-upsert of a fresh id. An alternate-key
   duplicate is retried only on an autonumber table (`retriesTheNumber`); any other create fault propagates (NFR-03).
   A delivery that loses the race on `sprk_internetmessageid` would become a faulted create, not "the existing id,
   `WasDuplicate`". The 412 classifier exists (`IsAlternateKeyDuplicate`), so a documented option is technically
   small, but it is a new create option, and the POML says to escalate before adding one.
2. **Unfiled communication with the system principal.** `DecideOwnerAsync` (:1115) REFUSES a table with an
   `OwnershipException` that is filed under nothing (`record_creation.ownership_exception`). The only precedent keeps
   unfiled communications OUT of the service (`OwnedChildWrite.PathFor` / `KeepsItsCreatorWhenUnfiled`, the email
   draft tool). `MessagingIngestor`'s row is always unfiled at create, and the two email ingestors' rows are unfiled
   whenever the association keeps the creator. Routing them means changing the service's owner rule: a system
   principal and an unfiled row on an `OwnershipException` table would be created with no owner bound. That is an
   owner-rule change, so UAC-r2 gets a ping before merge (w2-coordination).
3. **A system item needs an app-only metadata reader.** A system principal must pass a `RecordCreationPayload.Mapped`
   item (a Web API body with navigation-property binds). `DataverseWriteItemMapper.MapAsync` reads the navigation
   properties as a caller (`IDataverseUserClient`). The service's own remark says task 033 adds the app-only reader
   with the first system creator, and 033 is not done. The ingestors build SDK entities with `EntityReference`
   filing fields (`BuildNewRecordFieldsAsync`), so 034 needs that reader too, or a duplicate of it.
4. **Carried owner.** The service has no "owned like its parent" input. It re-resolves record-first from the row's
   own lookups. An inbound document names only `sprk_relatedcommunication`, which the resolver does not treat as an
   ownership parent. An unfiled email's attachment would then have no team, and the system principal has no caller
   business unit to fall back on. The POML asks for a carried owner as "an explicit service input". That is new
   request surface, and it needs a second unfiled-keeps-creator case, on `sprk_document`.
5. Smaller points, decided once the above is settled:
   - Step 12 publishes the membership event for every `sprk_document` (`MembershipPublishingTables`) and runs the
     restamp. Both are new for inbound documents.
   - `EmailAttachmentProcessor` is create-then-update: the parent document and association arrive in the update.
     Routing it means filing at create, in one write.
   - `EmailUploadCaptureService` is a singleton and the service is scoped. It needs `IServiceScopeFactory`; that is a
     constructor change, not a registration change.

## Options for the owner

- **A — extend the service.** Add four things:
  - a documented `RecordCreationRequest` dedupe option: on a 412 on the named key, read the row by key and return its
    id, marked a duplicate, with step 12 skipped;
  - system-principal keep-creator for an unfiled row on an `OwnershipException` table;
  - a system-only carried-owner input (a team, or keep-creator);
  - the app-only item mapper, taken from 033 or added here.

  One create path; the largest change. It alters the owner rule (UAC-r2 ping) and overlaps 033.
- **B — keep the race-proof call for the communication row only** (both email ingestors keep
  `CreateCommunicationRaceProofAsync` and their task-146 resolution, which already asks `IRecordOwnershipResolver`
  secure-if-any). Route the three document creates through the service with a carried-owner input.
  `MessagingIngestor`'s always-unfiled create stays outside the service, under a named registry exception that
  replaces the Permanent waiver. Still needs the carried-owner input and the app-only mapper.
- **C — sequence after 033**, which adds the system-principal mapping the service's remarks assign to it. Then run
  034 under A or B.

**Recommendation: C, then A for dedupe only and B for the rest.**
- Run 033 first.
- Dedupe: add the documented dedupe option. It is small, the 412 classifier exists, and it keeps one create path for
  the filed communication.
- Unfiled rows: keep creating them outside the service, under named registry exceptions, as the email draft tool
  does. This keeps the service's owner rule unchanged and avoids a UAC-r2 owner-rule review.
- Documents: route the three document creates with a system-only carried-owner input.

The owner chooses.

rework: none task 034 (stopped at escalation before implementation)
