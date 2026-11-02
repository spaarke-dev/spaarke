## 14. The search index follows the content type, never the user (addendum 2026-11-02, #2502; DRAFT for UAC-r2 and provisioning review)

> Draft for `design.md`. Binding owner decisions: `notes/decisions.md` "2026-11-01 — #2502 …" and "2026-11-02 — Owner accepts the three recommendations" item 1; `notes/defer-issues.md` DI-012. They are not re-opened here. Line numbers are on `work/field-mapping-server-write-path-r1` at 4fdb0a5c99 (master merged).

### 14.1 Why

- **A caller chooses where a file is indexed (DI-012).** `POST /api/ai/rag/index-file` carries the body's `searchIndexName` into the request (`RagEndpoints.cs:582`, `request with { TenantId }`) and on to `FileIndexingService` (`:140`, `:224`, `:272`) → `RagService.IndexDocumentsBatchAsync` (`RagService.cs:657-659`). Any allow-listed index is accepted. The route then stamps `sprk_searchindexname` with `GetDefaultIndexName()` (`RagEndpoints.cs:638-648`), so the record can name an index the file is not in.
- **Per-record and per-BU routing is a second chooser.** `SearchIndexNameResolver.ResolveAsync` (`:138-225`) walks the document → the parent → the parent's owning BU. FR-10/FR-38 step 8 copies the BU's index onto new records. The wizards copy the *user's* BU index onto records and into index-file bodies. On dev every BU that has an index names `spaarke-files-index`, which equals `KnowledgeIndexName`. Customer BUs have none, and each customer has its own search service with the fixed names. The routing changes nothing today. It costs two Dataverse reads per create and up to three per indexing job, and it is the source of #2399, #2206 and #2413.

**Owner rule (2026-11-01):** users never choose the index. The index is decided by what is being indexed.

### 14.2 The rule

1. The server picks the index for each content type from settings: SPE document chunks → `AiSearch:KnowledgeIndexName` (`spaarke-files-index`; discovery-tier chunks → `DiscoveryIndexName`), chat session files → `SessionFilesIndexName`, invoices → the invoice handler's index, and so on (`AI-SEARCH-INDEX-CATALOG.md` §4). No request field, record column or business-unit column changes it.
2. The only SPE-document seam that still takes an index argument is `FileIndexingService`. Every caller of it gets the files index. Session files (`RagIndexingPipeline.cs:314`), discovery (`:156`) and invoices already follow rule 1.
3. `sprk_document.sprk_searchindexname` stays the **stamp** of where the file landed. It is written only by the indexing pipeline, app-only, and always equals the index actually written. `VisualizationService.cs:108`, `RelocatedFileIndexing.cs:109-111` and the relocator (`DocumentContainerRelocator.cs:2692`) read it, so it must be true.
4. Record columns `sprk_ai_search_index` / `sprk_searchindexname` on the six search-indexed tables: **no server writer** (step 8 and Repair stop), **no client writer** (task 039's drop stays), **no reader on the indexing path**. Existing values stay; nothing is backfilled or cleared.

### 14.3 Current inventory: who picks or stamps an index

| Site | Today | Source |
|---|---|---|
| `RagEndpoints.IndexFile` `:582`, `:620` | body `searchIndexName` → write | caller |
| `RagEndpoints.IndexFile` `:638-648` | stamp = `GetDefaultIndexName()` | settings (**mismatch** with the write) |
| `RagEndpoints.SendToIndex` `:1311-1312`, `:1329`, `:1361` | resolver → write, stamp = resolved ?? default | record → parent → BU |
| `RagIndexingJobHandler` `:143-151`, `:184-191`, `:249` | payload, else resolver; INDEX_NOT_ALLOWED → retry with null; stamp = resolved ?? default | payload/record/BU (**mismatch** after the retry: the stamp names the rejected index) |
| `BulkRagIndexingJobHandler` `:291-306`, `:333-339` | resolver → write; retry with null | record → parent → BU |
| `IndexingWorkerHostedService` `:159-173`, `:196` | resolver(document only) → write | record |
| `PostUploadIndexingEnqueuer` `:194`, `:338`, `:414-416` | request value (all 8 producers pass `null`); OBO stamp = `AnalysisOptions.SharedIndexName` | settings, but the **deprecated** one (G-9 split if it differs from `KnowledgeIndexName`) |
| `RecordCreationService` step 8 `:749-760`, `:1365-1447` | copies the final owning BU's two columns | BU |
| `RecordCreationService.Repair` `:618-700` | fills unset columns from the owning BU | BU |
| `KnowledgeDeploymentService` `:124-131`, `:306` | `null` index → Shared deployment → `KnowledgeIndexName` | settings |
| Search: `RagEndpoints.Search` `:274`, `SemanticSearchService` `:144`, `:286`, `RecordSearchService` `:235-249` | request-named index, allow-listed; trimmed by live Read (§13.3b) | caller, search-time only |

### 14.4 Components changed (task 041; BFF only)

| # | File | Change | Reason |
|---|---|---|---|
| 1 | `Services/Ai/IFileIndexingService.cs` (`FileIndexRequest:162`, `ContentIndexRequest:262`), `Jobs/Handlers/RagIndexingJobHandler.cs:436` (payload), `Services/Ai/IPostUploadIndexingEnqueuer.cs:145` | **Remove** the `SearchIndexName` property/parameter | A type check enforces the rule (enforcement ladder: type): no producer can name an index. JSON bodies and queued payloads that still carry it bind fine; unknown members are ignored, and none of these DTOs sets `JsonUnmappedMemberHandling.Disallow`. |
| 2 | `Services/Ai/FileIndexingService.cs` `:140`, `:224`, `:272`, `:313`, `:384`, `:410` | Pass `searchIndexName: null` to `IndexDocumentsBatchAsync` / `DeleteChunksBeyondCountAsync`; drop the parameter from `IndexTextInternalAsync` | One seam, one index: the Shared deployment default (`KnowledgeIndexName`). |
| 3 | `Api/Ai/RagEndpoints.cs` IndexFile `:582-648`; SendToIndex `:1301-1371` | IndexFile: nothing to strip once (1) lands. SendToIndex: delete the resolver call. Both stamp `GetDefaultIndexName()` | Removes the caller chooser and the record/BU chooser; the stamp equals the write. |
| 4 | `RagIndexingJobHandler.cs:137-192`, `:249`; `BulkRagIndexingJobHandler.cs:286-339`; `Workers/Office/IndexingWorkerHostedService.cs:155-200` | Delete the resolver calls and the INDEX_NOT_ALLOWED retry branches; stamp `GetDefaultIndexName()` | The indexer stops reading record and BU columns (owner). Closes the retry stamp mismatch. |
| 5 | `Services/Ai/PostUploadIndexingEnqueuer.cs:414-416` | Stamp `ISearchIndexNameResolver.GetDefaultIndexName()`, not `AnalysisOptions.SharedIndexName` | The write uses `KnowledgeIndexName`; the stamp must too (G-9). The resolver is registered scoped and unconditionally (`AnalysisServicesModule.cs:65`); the enqueuer is scoped (`:110`). |
| 6 | 8 producers passing `SearchIndexName: null` (`AnalysisEndpoints.cs:997`, `ExternalProjectDataEndpoints.cs:2011`, `AnalysisResultPersistence.cs:274`, `RelocatedFileIndexing.cs:74`, `CommunicationEnrichmentService.cs:394`, `IncomingCommunicationProcessor.cs:1498`, `ComposeService.cs:2300`, `UploadFinalizationWorker.cs:1432`) | Delete that one argument line | Follows from (1). Hot-path files: run `/conflict-check`. |
| 7 | `Services/Ai/SearchIndexNameResolver.cs` | Delete `ResolveAsync` and its FetchXml helpers; keep `GetDefaultIndexName()` (the G-9 single point) | No caller is left. |
| 8 | `Services/RecordCreation/RecordCreationService.cs` step 8 `:749-760`, `:1331-1447`, `:295`, `:301`, `:787-789` | **Keep the step as a recorded no-op** (`NoOp`, "the index follows the content type (#2502); nothing written"). Delete `SearchIndexFill`, `BusinessUnitSearchDefaultsAsync` and the step-8 warning. Keep `TeamBusinessUnitAsync` (step 7 uses it, `:1262`) | Step numbers 9–12 are cited in the spec, the docs and the trace tests. A no-op keeps them stable and reads nothing. Saves two Dataverse reads per create (NFR-02). |
| 9 | `RecordCreationService.Repair.cs:132`, `:611-700`, reason codes `:950`, `:971-972`, part `:844` | Delete `RepairSearchIndexAsync` and its three reason codes. **Keep** `:342-343` (mapping may not write the columns in Repair) | Repair is internal and has no production caller yet (052 is 🔲), so the report shape is free to change. |
| 10 | `RecordTableRules.cs:169-175` (comments), `:504`, `:512-524`, `:620-657`, `:754-780`; `Api/DataverseDocumentsEndpoints.cs:151-167` | **Keep the 039 drop.** Move `LookupColumn` / `LegacyTextColumn` here from the resolver; fix the comments that call step 8 "the only writer"; `applied` is now always none, so every client value is logged as `client_search_index_dropped` (ids only) | The columns stay server-owned, so no client can bring routing back. This also removes a CRUD → AI-internal type reference (ADR-013). |
| 11 | `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md:135` (I-5), `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` §4 | I-5 becomes "no server writer; client values dropped; the stamp is the pipeline's"; the catalog gets a routing paragraph (rule 14.2) | Code wins, and the docs are fixed with it. The runbook `MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` goes to 062. |

**Out of 041, in W3 (021–029: the client stops sending; the server already ignores or drops the values):**
- `EntityCreationService.ts` (`:227-279` index-file `searchIndexName`; `:368-401` `applyDefaultSearchIndexName`; `:441-461` `resolveUserBuDefaults` select) is shared by all of the following.
- `CreateMatterWizard/matterService.ts:298-302` (bind) and `:403-412`.
- `CreateProjectWizard/projectService.ts:322-326` and `CreateProjectWizard.tsx:858-867`.
- `CreateEventWizard/eventService.ts:237-267`; `CreateInvoiceWizard/invoiceService.ts:214-217`; `CreateWorkAssignmentWizard/workAssignmentService.ts:415-416`.
- `document-upload/DocumentRecordService.ts:226-232`, `:379-386`; `DocumentUploadWizard/src/services/uploadOrchestrator.ts:347-386`, `:454`, `:499-607`; `DocumentUploadWizard/src/components/searchIndexResolver.ts:102-126` (029).
- `EmailComposer/createXrmEmailComposeHandlers.ts:281`, `:308-310`. **No W3 task names it.** Proposed: 028 (email).
- `Spaarke.SdapClient/src/types/index.ts:187` and `operations/IndexFileOperation.ts:51` are removed after the last wizard (035).

### 14.5 What stays

- The `sprk_aisearchindex` catalog. Provisioning seeds it (H12b; `CanonicalIndexCatalog`, 7 names, no BU default). `DataverseAllowedIndexesProvider` and the Semantic Search picker (`aiSearchIndexService.ts`, `SearchIndexSelector.tsx`) read it.
- Search-time index selection on the search routes: allow-listed, never from the caller's BU, trimmed by live Read (§13.3b). This is not indexing.
- Read-only consumers of the record columns:
  - the PCF `SearchIndexResolver.ts` → `SemanticSearchControl.tsx:543` → `NavigationService.ts:168-170`, which reads the host record's lookup to pick the index searched. Existing values equal the default; new records have none, so the default applies;
  - the Office profile display (`useDocumentProfile.ts:150`).
- Existing column values on every row; BU columns; the Core User role. Removing `prvAppendTosprk_AISearchIndex` stays §13.2 (e)'s question.
- The stamp columns and the relocator's cleanup of the old index plus the default index.
- The `ISearchIndexNameResolver` registration.

### 14.6 Compatibility and rollout

- **No client breaks.** Clients that still send `searchIndexName` to index-file get 200, with the chunks in the files index; the field is ignored at binding. A body naming a non-allow-listed index used to get 400 `INDEX_NOT_ALLOWED`; it now gets 200 (a relaxation). Record payloads keep being dropped (039).
- **No behaviour change on dev or customers today**, because every routed value already equals `KnowledgeIndexName`. The exception is documents whose stamp names another index; 041 step 1 counts them first.
- **Order:** 019 (W2 deploy and review, in progress) → **041** (spec amendment → UAC-r2 review → task → owner approval) → 021–029.
  - W3 cannot verify "the wizard sends no index" against a server that still copies the BU's value, so 041 goes first.
  - 041 also amends these acceptance lines, which now say "the owning-BU search index present at create": 029 `:23`, `:75`, `:82`, `:89`; 030 `:22`, `:110`; 036 `:23`, `:103`; 052 `:22`; 053 `:88`. The new reading is "no index column written by the server; the document stamp names the files index after indexing".
- **Queued jobs** enqueued before the deploy carry `SearchIndexName: null` (every producer passes null), so they are unaffected.

### 14.7 Test plan (ADR-038: update the tests that pin routing; delete only those whose subject is gone)

**Must-fire (new or inverted)**
1. index-file with an allow-listed `searchIndexName: "spaarke-invoices-index"` → `RagService` gets no index (default); stamp = `KnowledgeIndexName`.
2. index-file naming a non-allow-listed index → 200, not 400; the default index is written.
3. SendToIndex on a document whose lookup, text column, parent or BU names another index → write and stamp are the default (inverts `RagSendToIndexIndexNameContractTests.cs:105`).
4. `RagIndexingJobHandler`, bulk handler and Office worker: a document/parent/BU naming another index → no catalog-link fetch is issued; write and stamp are the default.
5. Unified create on each of the six search-indexed tables, with an owning BU that **has** an index → neither column is in the create body; the step-8 trace is `NoOp`; no `businessunit` retrieve (rewrites `SearchIndexDefaultTests.cs:115`, `:125`, `:135`, `:154`, `:197`, `:268`).
6. Repair of a row missing both columns, owner settled → no index key in the patch (rewrites `RecordRepairModeTests.cs:229`, `:264`).
7. OBO enqueuer with `SharedIndexName ≠ KnowledgeIndexName` → stamp = `KnowledgeIndexName` (rewrites `PostUploadIndexingEnqueuerTests.cs:158`).

**Must-not-fire (unchanged behaviour)**
1. index-file with no index, and the skipped (e-billing) file: same write, same stamp, and no stamp when skipped.
2. index-file authorization: tenant 403, row mismatch 409, knowledge-source 400 (existing contract tests pass unchanged).
3. A create or update never sends either column as `null`. Existing values are untouched (absent from the body, not cleared).
4. The 039 drop and its logs (`SearchIndexDefaultTests.cs:228-359` drop half, `RecordCreationServiceMappingSeamTests.Task039.cs`, `SearchIndexGenericUpdatePathTests.cs`, `SecureChildOwnershipAiToolTests.SearchIndex*.cs`) and `Spaarke.ArchTests/SearchIndexDropGuardTests.cs`.
5. Search-time selection (`SearchIndexNameEndpointContractTests.cs`, `RagEndpointsTests.cs:105-174`).
6. Relocation cleanup (`RelocatedFileIndexingTests.cs:71`, `:137`), session-files and discovery routing.

**Deleted (the subject is gone)**
- `SearchIndexNameResolverTests.cs:89-326` (9 `ResolveAsync` tests; the two `GetDefaultIndexName` tests stay).
- `RagIndexingJobHandlerTests.cs:490`, `:522`, `:558`.
- `FileIndexingServiceTests.cs:459`, `:506` (thread-through).
- `SearchIndexDefaultTests.cs:280` and `RecordRepairModeTests.cs:166`, `:210`, `:364` (BU-has-none and unsettled-owner cases: nothing is filled any more, so they fold into must-fire 5 and 6).

**Compile-only updates (mocks of `ResolveAsync`)**
- `BulkRagIndexingJobHandlerReplaceStaleChunksTests.cs`, `VersionSaveAiRefreshSeamTests.cs`, `RelocatedFileIndexingTests.cs`.
- Office quick-create contract tests and the pipeline/secure-root seam tests, where they assert index columns on the create.

### 14.8 ADR checks, placement and component justification

| ADR | Result |
|---|---|
| ADR-002 | **Comply.** No plugin. One owner per invariant: "the chunks' index and the stamp agree" is owned by `FileIndexingService` plus the stamp sites. The record columns hold no invariant any more, and the drop keeps clients out. |
| ADR-008 (path A, this project) | **Comply.** `CallerWriteAuthorization` and the index-file and send-to-index endpoint filters are unchanged. The caller controls one input fewer. No AppendTo on the catalog (already 039). |
| ADR-010 | **Comply.** No new registration; one constructor swaps `IOptions<AnalysisOptions>` for the registered resolver. Removes Dataverse reads. |
| ADR-013 | **Comply, improved.** AI routing stays in `Services/Ai`. `RecordCreation` stops referencing `SearchIndexNameResolver` (the constants move to `RecordTableRules`). |
| ADR-038 | **Comply.** §14.7 updates the tests that pin routing, adds tests for the new behaviour, and deletes only tests whose subject is gone. |
| BFF §10 | In the BFF, where the code already lives. Modifications and deletions only. Publish-size delta is expected at ≈ 0 and is measured against a fresh master build. No package change. |
| §11 | **No new surface.** No endpoint, service, interface, registration, column or package. The moved constants are a relocation. The three questions do not apply. |

### 14.9 Risks

- **Chunks left in a non-default index.** A document indexed under a per-record or caller-named index keeps those chunks there; a re-index writes the files index and trims only there. `privilege_group_ids` is only a pre-filter. The access boundary is `IRetrievalAccessTrim` / `TrimToReadableDocumentsAsync` in each consumer: `RagEndpoints` search, `SemanticSearchEndpoints`, `ReadableRagSearch`, `AnalysisRagProcessor`, `DocumentSearchHandler` and `PlaybookChatContextProvider`. `RecallSessionFileHandler` is scoped to the session (UAC-r2, 2026-11-02). Mitigation: 041 step 1 counts stamps ≠ `spaarke-files-index` on dev and records the count. **Owner 2026-11-02: leave the old chunks;** they are not cleaned.
- **The Dedicated deployment model** (`{tenant}-knowledge`, "do not configure", #1432) would make the `null` write differ from `GetDefaultIndexName()`. This is a known limit and is unchanged.
- **Two settings name the files index** (`KnowledgeIndexName`, `FilesIndexName`; `AiSearchOptions.cs:14`, `:42`). Drift is possible and pre-exists this change; not changed here.
- **Hot-path churn** (RagEndpoints, Compose, Communication, ExternalAccess): one-line deletions; run `/conflict-check` before the PR.
- **The hook is lost.** A future Secure Records index would need a new rule (§14.10 Q1), not a BU column.

### 14.10 Open questions

1. **Owner: Secure Records.** On 2026-10-27 the owner said "no per-BU search except Secure Records". Nothing implements a Secure Records index or plans one:
   - the Secure Record BU has no index (§13.3);
   - `CanonicalIndexCatalog` has 7 names and none is secure;
   - provisioning's H12b seeds rows with no BU default;
   - `infrastructure/ai-search/` has no secure index.

   **Recommendation:** treat the carve-out as withdrawn by the 2026-11-01 rule. If a separate index is ever wanted, the server chooses it from the record's secure state, with its own design and UAC-r2 review, never from BU columns. Withdrawn, or a future requirement?
2. **UAC-r2:** with nothing applied, `client_search_index_dropped` now fires for every client value until W3 lands (ids only). Keep it at Information as the W3 progress signal, or lower it to Debug?
3. **UAC-r2:** do #2206 (stamp not re-derived on owner change) and #2413 (BU move known limit) close with 041? Under rule 14.2 the record's owner no longer affects routing.
4. **Owner:** if the dev pre-check finds documents stamped into a non-default index, clean their old chunks, or leave them?

### 14.11 Proposed task 041 — Index routing follows the content type (#2502)

- **Deps:** 039 ✅; after 019 (do not disturb its review set). **Blocks:** 021 (W3 verification). It amends the acceptance of 029, 030, 036, 052 and 053 (§14.6).
- **Rigor:** FULL. Model sonnet / high. Tags: `bff`, `ai-indexing`, `testing`. One adversarial pass.
- **Scope:** §14.4 rows 1–11 only. No client file, no schema, no data write.

**Steps**
1. Dev read-only pre-check via the Dataverse MCP: `sprk_document` count with `sprk_searchindexname` set and ≠ `spaarke-files-index`; per table (6), rows whose `_sprk_ai_search_index_value` ≠ the `spaarke-files-index` catalog row; BUs with an index. Record the results in the notes.
2. Write must-fire 1–7 (failing first). Then make changes 1–11, the docs and the test updates in §14.7.
3. Build, `dotnet test`, publish-size delta against a fresh master build, `dotnet list package --vulnerable --include-transitive`.

**Acceptance criteria**
- AC1: index-file with any `searchIndexName` (allow-listed or not, or absent) writes to `KnowledgeIndexName` and stamps the same name; never 400 `INDEX_NOT_ALLOWED` for a body value.
- AC2: SendToIndex, the RAG job handler, the bulk handler and the Office worker issue no Dataverse read of `sprk_ai_search_index` / `sprk_searchindexname` on any record or BU, and write and stamp `KnowledgeIndexName`.
- AC3: `FileIndexRequest`, `ContentIndexRequest`, `RagIndexingJobPayload` and `PostUploadIndexingRequest` have no index field. `grep -rn "ResolveAsync" Services/Ai/SearchIndexNameResolver.cs` returns nothing.
- AC4: a unified create on each of the six search-indexed tables, with an owning BU that has an index, sends neither column; trace step 8 = `NoOp`; no `businessunit` retrieve.
- AC5: Repair writes neither column, on any row.
- AC6 (negative): no create, update or Repair body contains either column with `null`. Existing values are unchanged on read-back in the seam tests.
- AC7 (negative): the 039 drop tests, `SearchIndexDropGuardTests`, the index-file authorization contract tests, the search-time selection tests and the relocation cleanup tests pass **unchanged**.
- AC8 (negative): session-files, discovery and invoice index routing are unchanged (existing tests pass).
- AC9: the OBO enqueuer stamp equals `KnowledgeIndexName` when `SharedIndexName` differs.
- AC10: I-5 and the catalog doc are updated in the same PR. The publish-size delta and the CVE result are recorded. No new HIGH CVE.

## Reviews (2026-11-02)

**UAC-r2: OK.** Index routing was never an access control; the per-caller trim on live Read is the boundary (see above). Conditions:
- **Log level.** `client_search_index_dropped` goes to **Information** (039 shipped it at Warning) and stays there as the W3 progress signal, ids only. When W3 lands it goes back to **Warning**. This is a W3 acceptance criterion: from then on, any firing is a client regression.
- **#2206 and #2413.** Both close on 041's merge, with a link to the PR. Close them only after 041 step 1's count is recorded. (Dev fact: the BU1 move ran on 2026-11-02.)
- **Search routes.** Keep a test showing that a request-named index outside the allow-list is still refused on the SEARCH routes.
- **Interaction with 042b (§15.5).** §15.5's step-8 bullet, "a secure event gets an empty index plus `search_index_unset_for_owner_bu`", is moot once step 8 is a no-op. Whichever of 041 and 042b lands second updates that bullet and its test.
- **Review before merge.** UAC-r2 does a scoped check of 041's diff before merge, confirming that only routing is removed and no trim call moves (RagEndpoints, Communication, ExternalAccess).
- **Accepted.** The 400 → 200 change on index-file is acceptable.

**Secure Records carve-out.** The owner deferred this to UAC-r2. UAC-r2's answer: a separate index is not needed, because secure isolation rests on Read and the trim enforces it. The carve-out is **withdrawn**. If a secure index is ever wanted, the server chooses it from the record's secure state, with its own design and a UAC-r2 review, never from BU columns.

**Provisioning: confirmed, no dependency.** H10, H2b, H4b and H13 never read a BU index default. H12b (not built) seeds one catalog row per verified index, with `sprk_isdefault` on `spaarke-files-index` and no BU default. 041 must not change what `sprk_isdefault` means on the catalog row.
- **Added to 041's scope:** retire or update `scripts/backfill-multi-container-multi-index/Audit-MultiContainerMultiIndex-Drift.ps1`, which reads `businessunits.sprk_searchindexname`. Once routing is retired, its "drift" means nothing.

**Owner (2026-11-02):** documents found in a non-default index are **left** as they are; nothing is cleaned.
