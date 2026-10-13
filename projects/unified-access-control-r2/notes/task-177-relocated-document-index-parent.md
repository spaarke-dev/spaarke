# Task 177: indexing recovers a document's parent when the request has none (#1510)

Owner decision 2026-10-09 ("fix now in uac-r2"). Branch `task/uac-r2-177`, from `origin/master` `c8a87d818`. PR #1517.

## The defect, re-confirmed

Two probe tests, written first and run against unchanged master code, both FAILED with `ParentEntity` null:

- `RelocatedFileIndexingTests.Issue1510_TheRelocatedFile_IsIndexedByTheJobHandler_UnderTheDocumentsParent` runs the
  REAL `RelocatedFileIndexing` into the REAL `PostUploadIndexingEnqueuer`, captures the job where Service Bus would take
  it, and runs it through the REAL `RagIndexingJobHandler`. The `FileIndexRequest` the handler builds is asserted, so
  the parent has to come from the handler, not from the enqueue.
- `RagIndexingJobHandlerTests.Issue1510_APayloadWithNoParent_IsIndexedUnderTheParentTheDocumentRowNames`.

The issue text did not carry the probe code, so these two were written fresh against the issue's Suspect 2 path. Both
are kept as regression tests and pass now.

## What changed, per goal

1. **One helper.** `Services/Dataverse/DocumentIndexParentResolver.cs`. Send-to-Index's inline matter / project / invoice
   block moved into it. **The index parent is the record whose access GOVERNS the document** (main-session rule after the
   verifier pass, 2026-10-09). It matches `RecordContainerResolver`'s storage decision, because search authorizes a row by
   its parent.
   - **Candidates** are the records the document names, most specific first: work assignment, project, matter, invoice.
     Only when it names none of them do the core stamps of its `sprk_relatedevent` count. Those are read through the
     existing `CoreAncestorResolver.ResolveStampsAsync("sprk_event", …)`, one hop. Search cannot authorize an `event`
     parent, and the event inherits its access from those records (owner Q2: keep).
   - **Rule.**
     1. If exactly ONE candidate is secure (EFFECTIVE, task 174 / round 87), it wins. When several secure candidates
        are one secure family (their secure roots overlap), the most specific of them wins.
     2. If none is secure, the most specific wins (owner Q1).
     3. Two DIFFERENT secure roots, or a secure state that cannot be read, give NO parent (fail closed).
   - **Cost.** One candidate is its own answer and costs no flag read. Two or more cost the task-174 reads, reused: the
     own-flag read `ExternalParticipationService.GetRootRecordFlagsAsync` and the ONE filing walk
     `EffectiveRootFlags.ReadAncestryAsync`, folded with `EffectiveRootFlags.Fold`. This is the same composition as
     `FoldEffectiveAsync`, kept local because the family test needs the walk's secure parents, which the folded flags
     drop. There is no second walk.
   - **Names** are the ones `scope=entity` uses (`SemanticSearchAuthorizationFilter.AuthorizableEntitySets`): `matter`,
     `project`, `invoice`, `workassignment`.
2. **Job handler.** `RagIndexingJobHandler` calls `ResolveAsync(payload.DocumentId)` when `payload.ParentEntity` is null
   and a document id is set. That is one read of five columns, plus the flag reads when two or more records are named.
   - The recovered parent feeds both the index request and the `ISearchIndexNameResolver` routing.
   - An unreadable row or event, no link, or a fail-closed rule gives no parent, and the job still indexes. Only
     cancellation propagates.
   - A parent the payload already carries is used as is.
3. **Send-to-Index.** It calls `ResolveAsync(DocumentEntity)`. That makes one read of the two links the type lacks
   (work assignment, related event) and applies the same rule.
   - **Behaviour change:** a document that names more than one of work assignment / project / matter / invoice used to
     take the matter. It now takes the governing record by the rule. Dev has 0 such documents (2026-10-09).
   - A document that names one record takes the same parent as before.
   - A document that names none may now get one from its event.
   - If the extra read fails, there is no parent.
4. **`/index-file`** (`HoldIndexFileRequestToRowAsync`) uses the same decision.
   - A body parent must be one of the records the row names (now including a work assignment and a related event),
     else 409 `INDEX_FILE_PARENT_MISMATCH`.
   - The chunks carry the GOVERNING record, whichever one the body named, or none when the rule fails closed.
   - The body parent the filter authorized is used only when the row names no record. When the row cannot be read, no
     parent is used.
5. **Communications** (`RegardingParentEntityMapper`).
   - The candidates are the communication's `sprk_regarding{core}` lookups (work assignment, project, matter). One is
     the parent; two or more go through the rule.
   - So a communication filed to matter M AND to a secure project or work assignment is grounded under the secure
     record, not under the first regarding in `RegardingFieldMap` order.
   - A work assignment is now representable (it was null before). With no core regarding, the old primary-only logic is
     unchanged (invoice, service request, account, contact).
   - The three callers resolve the scoped rule per call. In a partial composition without it, a multi-core
     communication is grounded with no parent (fail closed).
6. **Producers.** Every listed producer already passes `DocumentId`, so none needed a change (table below). Compose
   create-on-save indexes INLINE on the OBO path (`PostUploadIndexingEnqueuer.EnqueueIfApplicableAsync`), not through the
   job handler. So the enqueuer's OBO path applies the same `ResolveAsync(documentId)` when the caller sent no parent.

| Producer | Path | DocumentId | Fixed by |
|---|---|---|---|
| `RelocatedFileIndexing` (relocator: Make Secure, inherit, reconciliation, migration, move-along, re-copy) | app-only job | `request.DocumentId` ✓ | handler |
| `UploadFinalizationWorker.EnqueueRagIndexingAsync` (Office new item + version save) | app-only job | `documentId` ✓ | handler |
| `ComposeService` create-on-save STEP 4 | OBO inline | `promotion.DocumentRecordId`, set on both the create and the existing-row branch ✓ | enqueuer OBO path |
| `AnalysisResultPersistence.EnqueueRagIndexingJobAsync` | app-only job | `documentId` (always set by `AnalysisOrchestrationService`) ✓ | handler |

## Component justification (CLAUDE.md §11)

`DocumentIndexParentResolver` (new class, one Scoped registration in `AnalysisServicesModule`):

1. **Existing.** These overlap with it:
   - Send-to-Index's inline block (`RagEndpoints` Step 3);
   - `RagEndpoints.HoldIndexFileRequestToRow` (index-file, the same three columns);
   - `RegardingParentEntityMapper` (communications: the first regarding wins);
   - `BulkRagIndexingJobHandler` (matter only, #1516);
   - `CoreAncestorResolver` (core-ancestor stamps);
   - the task-174 effective-flag read.
2. **Extension.** `CoreAncestorResolver` derives ACCESS stamps for a write. It covers core types only, gives no names,
   fails closed for a write, and has no notion of which stamp governs. The new class reuses it for the event hop, and
   reuses the task-174 flag read and filing walk for the rule. The three parent derivations (Send-to-Index, index-file,
   communications) now call it instead of each choosing on its own.
3. **Cost of doing nothing.** A file moved by a secure transition, or saved by Office or Compose, is indexed with no
   parent and is never found in record search or parent-scoped RAG. A document or communication that names a secure
   record and a broader one is indexed under the broader one, so that record's readers see it in search.

Registration: `TryAddScoped`, because the flag reader is a typed HttpClient. It is unconditional and sits beside the
enqueuer that consumes it (§10 F.1). It also calls the TryAdd-only `AddCoreAncestorResolver`. No package, no endpoint,
no column.

## Placement (CLAUDE.md §10)

It stays in the BFF: these are synchronous Dataverse reads on the existing indexing paths, with nothing to place
elsewhere under ADR-052. It sits in `Services/Dataverse/`, beside `CoreAncestorResolver`. It is a Dataverse access
decision with no AI in it, and the communication pipeline (CRUD code) consumes it, so placing it in `Services/Ai/` would
have added a CRUD→AI dependency (bff-extensions.md A.4).

## Tests (the AC's closed set, the main session's list, and justified extras)

- The two #1510 probes above. The relocator one asserts the handler's `FileIndexRequest` and the routing call.
- `DocumentIndexParentResolverTests` (now in `Services/Dataverse`): matter, project, work assignment, invoice (one
  theory, with no flag read); no parent link; an unreadable row; an event's single core record (the unchanged
  single-stamp case).
- The main session's list:
  - a multi-stamp event, secure project + non-secure matter, gives the project
    (`AnEventUnderANonSecureMatterAndASecureProject_FilesItsDocumentsUnderTheProject`);
  - two secure roots give no parent (`TwoDifferentSecureRoots_GiveNoParent`);
  - an unreadable secure state gives no parent, for both a throwing read and an unreadable flag
    (`AnUnreadableSecureState_GivesNoParent`);
  - a communication with a matter + a secure project gives the project
    (`RagGroundingParentEntitySeamTests.ResolveAsync_MatterAndSecureProject_GroundsUnderTheProject`).
- Owner Q1: `ADocumentNamingTwoRecords_NeitherSecure_IsFiledUnderTheMoreSpecificOne` (three ties).
- Pre-merge round. Both K1 tests run through a REAL filing-walk stub: the walk's queries are answered from a
  (table, id) world.
  - `AWorkAssignmentUnderTheSecureMatterTheDocumentAlsoNames_IsOneFamily_TheWorkAssignmentWins`.
  - `AWorkAssignmentUnderOneSecureMatter_AndAnUnrelatedSecureMatter_GiveNoParent`. It carries a control: with M2 not
    secure, the work assignment wins, which proves the null comes from the two-roots rule and not from an unreadable walk.
  - K6: `ADocumentFiledOnlyThroughARelatedTwin_IsFiledUnderThatRecord`, and
    `ADocumentRelatedOnlyToACommunication_IsFiledUnderTheCommunicationsCoreRecord` (the second new branch).
- Extras, each justified:
  - `ADocumentUnderASecureMatterAndANonSecureWorkAssignment_IsFiledUnderTheMatter`: rule 1 beats rule 2 on the
    document's own links.
  - `SendToIndexsPath_TakesTheSameDecision_IncludingTheWorkAssignmentTheEntityCannotCarry`: the `DocumentEntity` path
    takes the same answer.
  - `PostUploadIndexingEnqueuerTests.EnqueueIfApplicableAsync_NoParentButADocument_IndexesUnderTheParentTheRowNames`:
    the Compose path.
- `RagIndexingJobHandlerTests.Issue1510_AnUnreadableDocumentRow_IndexesWithoutAParent_AndTheJobSucceeds`.
- **Changed existing tests.**
  - `RagGroundingParentEntitySeamTests` had a case pinning "work assignment + account gives null". A work assignment is
    now a core record that grounds, so that case expects `workassignment`. The no-fall-through case now uses an event,
    which is still not representable.
  - `RagEndpointsAuthorizationContractTests.ArrangeRow` also answers the decision's read of the row's extra links, with
    an empty row.
- Construction sites use `TestInfrastructure/TestDocumentIndexParentResolver.Over()`: the real class over a Dataverse
  double that answers "not found" and a flag double that answers "not secure". Their behaviour is unchanged.

## Known limits (K-class, one line each)

- Fixed in the pre-merge round (verifier K6): the `sprk_related{workassignment,project,matter,invoice}` twins are
  candidates under the same rule, each after its typed lookup and deduplicated with it. `sprk_relatedcommunication`
  resolves like the event, through the communication's core stamps (`CoreAncestorResolver`, one read). Both come from a
  named projection of `DocumentLinkFields.All`, with the exclusions written down, so no second copy of the vocabulary
  exists (`DocumentLinkVocabularyGuardTests`).
- K2: an event whose only core ancestor is a service request gives no parent, because search cannot authorize one.
- K2: two secure candidates of ONE family are recognised through the walk's secure parents (their secure roots
  overlap). A document that names a secure project P AND the secure matter M that P is filed under resolves to P. Two
  own-secure records not filed under each other give no parent.
- K4: Send-to-Index's response `ParentEntityId` can now name an event's core record to a caller who holds Write on the
  document. It is the id of the record the document's family is filed under; no content is exposed.
- K (documented, not fixed, per the main session): the index write merges, so a re-index whose decided parent is null
  does NOT clear a parent that an older chunk with the same id carries. Fail-closed "no parent" therefore stops new
  chunks being filed under a record, but cannot retract an old chunk's parent.
- Not touched (the main session is filing it separately): `SearchIndexNameResolver`'s short-name FetchXml always falls
  through to the default index. Fixing it could move new chunks to a different index than their existing ones.

## Backfill (owner Q3: no new code; a post-deploy Send-to-Index repair, steps in PR #1517)

The constraint rules out a backfill job. Chunks written before this fix keep no parent until the document is re-indexed
(Send-to-Index, a save, or another relocation).

The index could not be counted: `az` holds control-plane access to `spaarke-search-dev`, but data-plane queries return
403, and a query key was deliberately not fetched.

Dataverse proxy (spaarkedev1, read-only, 2026-10-09):
- 574 documents in total;
- 144 carry a matter / project / invoice, and 1 carries only a work assignment;
- 0 carry only an event;
- 429 carry no parent link at all, so no fix can give those a parent;
- 141 have a file and a parent link, spread over 3 drives (129 in one, 11 and 1 in the other two).

## Owner decisions (main session, 2026-10-09)

1. **Several core records named:** the most specific wins when none is secure. After the verifier pass, the secure
   record that governs wins over that; two different secure roots, or an unreadable secure state, give no parent.
2. **Event:** naming the event's core record is correct. Kept.
3. **Backfill:** no new code. A one-off Send-to-Index repair runs as a post-deploy main-session step (in the PR body).

## Out-of-scope defects found (filed as #1516)

- `BulkRagIndexingJobHandler.QueryDocumentsAsync` selects and filters `_sprk_matterid_value` and `sprk_ragindexedon`.
  Neither column exists on `sprk_document` (live check 2026-10-09), so every bulk / scheduled RAG sweep's Dataverse query
  fails with 400. It also derives a matter-only parent.
- `DataverseServiceClientImpl.MapToDocumentEntityWithLookups` never sets `MatterName` / `ProjectName` / `InvoiceName`, so
  Send-to-Index and index-file write "Unknown Matter" / "Unknown Project" / "Unknown Invoice" as `parentEntityName`.

## Verification

- Probes: both FAILED on master `c8a87d818` (`ParentEntity` null) and PASS after the fix.
- First round: the targeted run was 276 / 276. The full `Sprk.Bff.Api.Tests` run gave 19,068 passed, 54 skipped and 18
  failed. All 18 failures were host-startup timeouts (3-7 minutes each) on the shared machine, in suites this change does
  not touch, and all 32 of their cases pass when re-run in isolation.
- After the owner's Q1 decision: 279 / 279 targeted.
- After the verifier-round rule (governing record): 2,167 targeted cases, covering indexing, Send-to-Index, index-file
  and every Communication / Email / inbound-pipeline suite. 2,154 pass and 13 are skipped. `Spaarke.ArchTests` passes
  841 / 841, `-warnaserror` is clean, and `Spe.Integration.Tests` and `Sprk.Bff.Api.IntegrationTests` build. The full suite
  then gave 19,087 passed, 54 skipped and 9 failed. All 9 were host-startup timeouts (2-19 min), and all 10 of their
  cases pass in isolation.
- Publish size (`dotnet publish -c Release`, fresh master `c8a87d818` against the first-round branch, same machine):
  +10,284 B uncompressed, +4,542 B zipped (37,203,854 B, about 35.48 MiB). No package reference changed.
- Self-review (task-execute Step 9.5): no F-class finding remains. The K-class items are listed above.
- Pre-merge round (K1 / K4 / K6): 2,171 targeted cases, 2,158 passed and 13 skipped. `Spaarke.ArchTests` 841 / 841 (the
  document-link vocabulary guard caught a literal copy, which was replaced by a projection of `DocumentLinkFields.All`).
  `-warnaserror` is clean.
