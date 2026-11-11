# Task 028 — eBilling containers: config keys, ownership, non-indexed exports, CIAM deny, hand-off

> Project: ebilling-r1 (FR-16, D-35, D-44, D-55, D-63, D-66, D-67). Executed 2026-10-15 in a sub-agent worktree. Rigor FULL (POML said STANDARD; raised because it changes BFF `.cs` and storage access control). No live change: no container, container type, App Service setting or Key Vault item was created or changed.

## 1. What was built

| Area | Change |
|---|---|
| Options | `FinanceOptions.ProcedureLibraryContainerId`, `FinanceOptions.EbillingOutputContainerId`. Optional: empty boots. A non-empty value must match `^b![A-Za-z0-9_-]+$` (one id; a list such as `b!x,b!y` is refused), otherwise start-up fails (`OptionsValidationException`) with a message naming the key. `FinanceOptionsValidator` (review follow-up) also fails start-up, naming both keys, when the two ids are equal or either equals `EmailProcessing:DefaultContainerId` or `Communication:ArchiveContainerId`; empty values never collide. Validated through the existing `AddOptions<FinanceOptions>().ValidateDataAnnotations().ValidateOnStart()` chain. |
| Template | `appsettings.template.json` `Finance` section: `#{SPE_PROCEDURE_LIBRARY_CONTAINER_ID}#`, `#{SPE_EBILLING_OUTPUT_CONTAINER_ID}#` with a D-55 comment. (The template is not a stamp channel — the csproj never publishes it.) |
| Ownership (D-67) | `GraphModule.StampContainerSettingKeys` gains `Finance:ProcedureLibraryContainerId` and `Finance:EbillingOutputContainerId` as single ids, taken verbatim like the default and archive keys. Escalation trigger checked: the guard admits only the exact configured ids (trimmed, blanks dropped); nothing changes for existing keys. |
| Not indexed | `FileIndexingService` skips a file whose `DriveId` equals `EbillingOutputContainerId` (INFO log, `FileIndexingResult.Skip(...)`: `Success = true`, `Skipped = true`, `SkipReason = container_not_indexed`, 0 chunks). Checked on both entry points before any download or pointer check. `RagIndexingJobHandler`, `PostUploadIndexingEnqueuer` and `RagEndpoints` (index-file, send-to-index) do not stamp `sprk_searchindexed` for a skipped file; send-to-index reports it as "not indexed"; the bulk job counts it as skipped; the Office worker logs it as skipped. Every caller has a skip test except `IndexingWorkerHostedService` (the Office worker): it has none, and its change only alters a log line (skipped versus succeeded); the skip itself is decided in `FileIndexingService`, which is tested. |

### Why `FileIndexingService` is the convergence point

Every path that indexes an SPE **file** calls `IndexFileAsync` or `IndexFileAppOnlyAsync`: `PostUploadIndexingEnqueuer` (all upload / save / re-index callers), `RagIndexingJobHandler` (`DocumentOperationsEndpoints` and `/api/ai/rag/enqueue-indexing` enqueue it), `BulkRagIndexingJobHandler`, `IndexingWorkerHostedService` (Office), `RagEndpoints` index-file and send-to-index. Not covered, by design: `IndexContentAsync` (pre-extracted text, no drive; no caller), `RagIndexingPipeline` (chat session files, not container files), `RagEndpoints.IndexDocument` (SystemAdmin chunk injection; no file). No Azure AI Search indexer pulls from SPE.

## 2. Dev values for the WP2 deploy (task 053)

| App Service setting | Dev value |
|---|---|
| `Finance__ProcedureLibraryContainerId` | `b!tqSgSM13R06R6O5ymHDynNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y` |
| `Finance__EbillingOutputContainerId` | `b!1m_ZuA0VWUKCsWg44OvUtNNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y` |

Container type `8a6ce34c-6055-4681-8f87-2f4f9f921c06` (Spaarke PAYGO 1). Both slots (production + staging). Not set on `spaarke-bff-dev` by this task.

## 3. CIAM trace — why a firm user cannot download a Procedure Library document, and has no content or RAG search route (D-63)

**Scope: bytes, not metadata.** What is proven is that a firm user gets no file content and has no content or RAG search route. The external document LIST and fetch routes filter on the record lookup only, so a Procedure Library `sprk_document` row filed under a firm-readable record would still show its name and summary. That gap is DEF-015 / #1975 (owner decision; the fix is in shared external-access code).

**Who is a CIAM caller.** A CIAM token validates only on the `Ciam` JwtBearer scheme (`AuthorizationModule.cs:65`). That scheme is on two policies only, `CiamExternal` and `ExternalCollaboration` (`AuthorizationModule.cs:433-456`), both used only by the `/api/v1/external` group (`ExternalAccessEndpoints.cs:62`). The default (workforce) scheme rejects any CIAM token (issuer or `tid` guard; pinned by `AuthorizationModuleCiamTests`).

**Open.** The external group's only file-bytes routes are `GET /api/v1/external/{projects|matters|workassignments|servicerequests|invoices}/{id}/documents/{documentId}/content` (`ExternalProjectDataEndpoints.DownloadDocumentForParent`, line ~585). In order:

1. Read on the route record (`RightsForDocumentParentAsync`) — otherwise 403.
2. The document is filed under that record through that type's lookup, and every secure root it is also filed to is readable (`IsDocumentReadableUnderAsync`) — otherwise 403.
3. `IDocumentStorageResolver.GetSpePointersAsync` → `RecordContainerResolver.EnsureDocumentPointerContainerAsync` (`DocumentStorageResolver.cs:107`). This serves only a container derived for the document's records — a business unit's container (interim rule: in the owner's subtree), a secure record's own container, the communication archive, or the unfiled default. The Procedure Library is none of those (it must never be stamped on a business unit — see the hand-off). It is refused with 409 `document_storage_unverified` **even when the document is filed under a record the firm can read**. No SPE read happens.

Uploads by a CIAM user go to the route record's derived container, never to the Procedure Library. The module-data route (`ExternalModuleDataEndpoints`, `/api/v1/external/api/dataverse/*`) returns Dataverse rows under the Tier-2 scope filter, never file bytes. Its "documents" module (`Infrastructure/DI/ExternalAccessModule.cs:397-421`) does return `sprk_documentname` for a document filed under a firm-readable record; that exposure is DEF-015.

**Search.** There is no content or RAG search route in the external group, and every search route uses the workforce scheme, so a CIAM token gets 401 there. Pinned from the real endpoint table. This does not cover the module-data route above, which lists document names (DEF-015).

**Tests (KEEP path, `tests/integration/auth/ExternalAccess/ProcedureLibraryCiamDenyTests.cs`).**

| Test | Proves |
|---|---|
| `ProcedureLibraryPointerTests` (interim and strict) | Pointer check refuses a Procedure Library pointer on a document filed to a firm-readable matter; control: same document and item in its derived container is served. |
| `ProcedureLibraryExternalDownloadTests` | Real route plus real pointer check (strict rule; the interim rule is covered by the pointer tests): 409, no download. The item is really in the Procedure Library drive, so only the container rule can refuse. Control: the firm's own upload to the same matter streams. Mutation: with the pointer check off the deny test fails (200). |
| `CiamSearchRouteTests` | Real app endpoint table: only `/api/v1/external/**` admits Ciam; none of those routes is a search route; every search route is authenticated and not Ciam. Must-fire control: the scan finds Ciam on the external group. |

## 4. Findings for the consumers (075 / 077 / 078 / 133 / 150)

- **App-only reads of eBilling `sprk_document` rows are refused** by the same pointer check (DEF-014, #1968). That includes indexing that names the document (D-55 intends Procedure Library documents to be indexed), standard download and preview. 075 / 078 must choose an approach before they create Procedure Library documents (options in DEF-014). App-only writes via the guard work: both containers are owned (D-67).
- **Internal OBO access** to either container depends on the container's own permissions (SPE membership). The owner created the dev containers, and no permissions were changed here. 077 (Rule Review page) and any internal viewer need either a container role for internal users / a group, or an app-only path (see DEF-014).
- **Consumers refuse when the key is empty**, with a stable ProblemDetails code (their own task), since the keys are optional at start-up.
- **Task 040 / later EbillingOptions**: the keys live on `FinanceOptions` (`Finance:` section). If they move, `GraphModule.StampContainerSettingKeys`, the template, the manifest hand-off and `FileIndexingService` move with them.

## 5. Hand-off and deferred items

- **DEF-013 / #1967**: provisioning hand-off to customer-provisioning-orchestration-r1. Two containers of the existing type per environment, marked, not stamped on a business unit, and two `per_env_settings` entries for H4b.
- **DEF-014 / #1968**: pointer check versus eBilling documents (above). Resolving it by option 1 removes the basis of the CIAM byte deny, so that change must add an explicit refusal on the external routes.
- **DEF-015 / #1975**: external document lists can show the name and summary of a Procedure Library row filed under a firm-readable record (owner decision).

### Known limits (review pass 1)

- **K4 — two shapes for "not indexed".** `POST /api/ai/rag/index-file` returns 200 with the `FileIndexingResult` (`success: true`, `skipped: true`, `skipReason: container_not_indexed`). `POST /api/ai/rag/send-to-index` reports the document as `success: false` with "This document is stored in a container that is not indexed." Each matches its route's existing contract (index-file returns the indexing result; send-to-index has no "skipped" field). Neither stamps the row.
- **K2 — SPE Admin sees both containers as owned.** Because the keys are in `StampContainerSettingKeys`, the SPE Admin app treats both containers as owned: a SystemAdmin can list and delete them through it. Nothing beyond operator discipline protects them from deletion, as for the default and archive containers today.
- **Skips are counted as skips.** The bulk job counts a skipped file as skipped (not processed); the RAG job handler and the Office worker log it as "not indexed", not as success.

## 6. Proposed `.claude/` text (main session applies — sub-agents cannot write `.claude/`)

`.claude/constraints/provisioning.md`, section "SPE: container type per model…", bullet **How (T227d)**. Replace

> Own = an id in `EmailProcessing__DefaultContainerId` / `Communication__ArchiveContainerId` / `SharePointEmbedded__OwnedContainerIds` (operator-only, pre-marker environments) **or** custom property `spaarkeCustomerId` = `Customer__Id`.

with

> Own = an id in `EmailProcessing__DefaultContainerId` / `Communication__ArchiveContainerId` / `Finance__ProcedureLibraryContainerId` / `Finance__EbillingOutputContainerId` (eBilling, ebilling-r1 D-67) / `SharePointEmbedded__OwnedContainerIds` (operator-only, pre-marker environments) **or** custom property `spaarkeCustomerId` = `Customer__Id`. The two eBilling containers are never stamped on a business unit (`sprk_containerid`).

## 6b. Commits (work/ebilling-r1)

`d33c95486` (config keys and ownership), `dbb277643` (never index the output container), `0c594a220` (CIAM deny tests), `cce75bdbc` (review pass 1 fixes), `5d7780ac5` (notes, DEF-013/014/015). The review follow-up (container-id pattern, distinctness validator, wording, these corrections) is a later commit.

## 7. Verification

| Check | Result |
|---|---|
| Touched test classes (options, ownership, FileIndexingService, enqueuer, RAG job, bulk job, index-file / send-to-index contracts, external document routes, CIAM deny, seam) | 329 passed, 0 failed |
| Broader slice (ExternalAccess, Rag, Indexing, Finance, Spe, AccessControl, Tenant, OptionsValidation) | 8,597 passed, 7 skipped (pre-existing), 0 failed |
| ArchTests (incl. `IOptionsDriftTests`, ADR-010) | 1,045 / 1,045. No new DI registration: ADR-010 1:1 ceiling unchanged at 162 |
| Fail-before (mutation) | Removing the keys, the format check and the skip: 10 tests failed. Base callers (c70e6c0a0): all 5 caller skip tests failed, both controls passed. Pointer check off: CIAM download deny failed (200). Strict container rule alone disabled: CIAM download deny failed |
| BFF publish size (Release, linux-x64, zipped) | base 38,939,623 B → head 38,941,091 B: **+1,468 B** (37.14 MB total, 192 files each). The review fixes add a few branches only |
| Packages / CVE | No package change; no new vulnerable package |
| Live changes | None: no container, container type, App Service setting or Key Vault item created or changed |

### Known limits (independent review follow-up)

- **Chunks indexed before the output-container key was set are not removed.** The skip in `FileIndexingService` returns before `ReplaceStaleChunks`, so a file that was indexed while `Finance:EbillingOutputContainerId` was empty (or different) keeps its chunks after the key is set, until they are removed by hand.
- **The bulk-job skip is unreachable until #1516 is fixed.** `BulkRagIndexingJobHandler` counts a skipped file as skipped, but the bulk/scheduled RAG sweep queries `sprk_document` columns that do not exist (#1516), so it indexes nothing today; the skip counting is covered by a handler unit test only, not by a working sweep.

### Review

- **Pass 1** (adversarial-reviewer, opus): 4 F2 and 1 F1 findings — all fixed in cce75bdbc: caller skip tests, CIAM download test isolates the container rule, exact `/api/v1/external` route match, skips counted as skips, claim narrowed to bytes. Known limits K2 and K4 recorded above. Metadata exposure filed as DEF-015.
- **Pass 2** (run in the main agent; the subagent limit was reached): checked every `IndexFileAsync` / `IndexFileAppOnlyAsync` caller and fallback (all pass the skip; the pointer check downloads from the same `DriveId` the skip compares, so the skip cannot be bypassed), other search-index writers (session files, observations, precedents, record matching: none indexes container files), and ownership parsing (single keys trimmed; guard compares `Ordinal`). No F-class finding. Fixed one doc nit: the template comment no longer says the Procedure Library is indexed.
