# Plan — Spaarke AI Assistant: new AI Search index + SPE container

> **Status**: Draft — pending team review
> **Date**: 2026-05-28
> **See also**: `design.md` (why), `spec.md` (exact schema + IDs)

## Overview

Six sequential phases. Estimated end-to-end effort: **~1 working day** assuming spec.md is correct. The work is contained — no client-side changes needed beyond a BFF restart.

```
Phase 1 — Create spaarke-file-index (Azure AI Search)        ~1.5h
Phase 2 — Create Spaarke Dev Container 2 (SharePoint Embedded) ~0.5h
Phase 3 — BFF config changes (KV ref + new app setting)        ~0.5h
Phase 4 — Code changes (default-container wiring)              ~1.5h
Phase 5 — Deploy + smoke test                                  ~1h
Phase 6 — Documentation                                        ~1h
```

Plus one full session worth of rework buffer for the inevitable schema detail we miss the first time.

## Phase 1 — Create `spaarke-file-index`

| # | Step | How |
|---|---|---|
| 1.1 | Compose the JSON index definition from `spec.md` §1 | Write `infra/ai-search/spaarke-file-index.json` (new) — single canonical artifact. Includes all 28 fields, vector profile, semantic config. |
| 1.2 | Validate the JSON locally | `cat infra/ai-search/spaarke-file-index.json | jq .` — ensure valid JSON. Cross-check every field against spec.md table. |
| 1.3 | Deploy via Search Management API | `PUT https://spaarke-search-dev.search.windows.net/indexes/spaarke-file-index?api-version=2024-07-01` with `api-key: <admin-key>` header. |
| 1.4 | Verify schema deployed | `GET .../indexes/spaarke-file-index?api-version=2024-07-01` — confirm field count = 28, `privilege_group_ids.filterable = true`, profiles registered. |

**Owner**: BFF dev / DevOps. **Reversible**: yes — `DELETE .../indexes/spaarke-file-index`.

**Quality gate**: dump the live index schema and compare field-by-field to spec.md before progressing.

## Phase 2 — Create `Spaarke Dev Container 2`

| # | Step | How |
|---|---|---|
| 2.1 | Create the container via SpeAdmin endpoint or Graph API | The BFF already exposes container provisioning via the SpeAdmin module. Use it: `POST /api/spe-admin/containers` with body `{ "displayName": "Spaarke Dev Container 2", "containerTypeId": "8a6ce34c-6055-4681-8f87-2f4f9f921c06" }`. If SpeAdmin endpoint isn't available, use direct Graph: `POST https://graph.microsoft.com/v1.0/storage/fileStorage/containers` (admin app token). |
| 2.2 | Capture the new container id | Response includes `id` field. Record it in `spec.md` §2.1 and in the `SharePointEmbedded__DefaultContainerId` app setting (Phase 3). |
| 2.3 | Verify BFF MI has FileStorageContainer.Selected on the container | Test via `GET https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{id}` using the BFF MI token. Should return 200. If 403, grant the MI access on the container via the owning application's admin flow. |

**Owner**: SPE admin / BFF dev. **Reversible**: yes — `DELETE` the container (or just stop pointing at it). PayGo containers can be deactivated if needed.

## Phase 3 — BFF config changes

| # | Step | How |
|---|---|---|
| 3.1 | Set `AiSearch__KnowledgeIndexName` to `spaarke-file-index` in dev | `az webapp config appsettings set --name spaarke-bff-dev --resource-group rg-spaarke-dev --settings "AiSearch__KnowledgeIndexName=spaarke-file-index"`. |
| 3.2 | Set `SharePointEmbedded__DefaultContainerId` to the container id from Phase 2 | Same `az webapp config appsettings set` pattern. |
| 3.3 | Keep `AiSearch__DiscoveryIndexName` and `AiSearch__RagReferencesIndexName` unchanged | Other indexes are not affected. |
| 3.4 | Document the new app settings in `docs/guides/auth-deployment-setup.md` | Add an "AI Search + SPE container" subsection so future env provisioners know to set these. |

**Owner**: BFF dev. **Reversible**: yes — revert the appsetting to `spaarke-knowledge-index-v2` and restart.

## Phase 4 — Code changes for default-container wiring

| # | Step | Files |
|---|---|---|
| 4.1 | Add `DefaultContainerId` option to `SharePointEmbeddedOptions` (or equivalent options class) | `src/server/api/Sprk.Bff.Api/Configuration/SharePointEmbeddedOptions.cs` |
| 4.2 | Inject the new option into `BulkRagIndexingJobHandler` or wherever per-message container scope defaults are decided | Path TBD — `src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/BulkRagIndexingJobHandler.cs` is the likely spot. |
| 4.3 | Make sure the indexing pipeline writes `containerId` to the new index field (added in spec.md §1.2) | `src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs` and / or `KnowledgeDocument` model. |
| 4.4 | Confirm `lastModified` and `sourceSystem` are populated by the indexing pipeline | Same files; minor mapping changes. |
| 4.5 | Build, run existing unit tests | `dotnet build src/server/api/Sprk.Bff.Api/` then `dotnet test`. |

**Owner**: BFF dev. **Reversible**: yes — code change is small and isolated to the indexing path.

## Phase 5 — Deploy + smoke test

| # | Step | How |
|---|---|---|
| 5.1 | Deploy BFF to dev | `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1` (per `bff-deploy` skill). |
| 5.2 | Restart App Service (forces config refresh) | `az webapp restart --name spaarke-bff-dev --resource-group rg-spaarke-dev`. |
| 5.3 | Upload a test document to the new container | Via UniversalDatasetGrid / DocumentRelationshipViewer / direct Graph. Confirm it appears in the container. |
| 5.4 | Wait for the indexing job to complete | Watch App Insights for `BulkRagIndexingJobHandler` traces; expect ≤30 s for one document. |
| 5.5 | Query the index directly | `GET https://spaarke-search-dev.search.windows.net/indexes/spaarke-file-index/docs?search=*&$top=5&api-version=2024-07-01` with admin key. Confirm test document chunks are present with correct fields. |
| 5.6 | Test the Assistant | `sprk_spaarkeai` Code Page → "Find documents about [content of the test doc]". Expect a streamed response with citations from the test document. |
| 5.7 | Verify legacy index still queryable | `GET .../indexes/spaarke-knowledge-index-v2/docs?...` returns existing data. |

**Owner**: BFF dev + product / QA. **Reversible**: yes — `AiSearch__KnowledgeIndexName` back to v2.

## Phase 6 — Documentation

| # | Step | Where |
|---|---|---|
| 6.1 | Add an "AI Search index provisioning" section to `docs/guides/auth-deployment-setup.md` (or a new `docs/guides/AI-SEARCH-INDEX-PROVISIONING.md`) | `docs/guides/` |
| 6.2 | Add an entry to `.claude/FAILURE-MODES.md` about the `privilege_group_ids` filterable issue and the broader "AI Search index schema is immutable" lesson | `.claude/FAILURE-MODES.md` |
| 6.3 | Update `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` to reference `spaarke-file-index` as the AI Assistant default | `docs/architecture/` |
| 6.4 | Update memory: `project_bff-mi-azure-openai-rbac.md` already covers the auth gap; add a sibling memory for this index migration | `~/.claude/projects/.../memory/` |

**Owner**: BFF dev. **Reversible**: yes — docs.

## Critical files / paths

### To create
- `infra/ai-search/spaarke-file-index.json` — index definition
- `projects/spaarke-ai-assistant-new-resources-r1/tasks/001-create-spaarke-file-index.poml`
- `projects/spaarke-ai-assistant-new-resources-r1/tasks/002-create-spe-container.poml`
- `projects/spaarke-ai-assistant-new-resources-r1/tasks/003-bff-config-changes.poml`
- `projects/spaarke-ai-assistant-new-resources-r1/tasks/004-code-changes.poml`
- `projects/spaarke-ai-assistant-new-resources-r1/tasks/005-deploy-smoke-test.poml`
- `projects/spaarke-ai-assistant-new-resources-r1/tasks/006-documentation.poml`

### To modify
- `src/server/api/Sprk.Bff.Api/Configuration/SharePointEmbeddedOptions.cs` (add `DefaultContainerId`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/BulkRagIndexingJobHandler.cs` (read default container)
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs` (write `containerId`, `lastModified`, `sourceSystem`)
- `docs/guides/auth-deployment-setup.md` (provisioning steps)
- `.claude/FAILURE-MODES.md` (immutable-schema lesson)

### Existing files referenced (read-only)
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` (filter / search field references)
- `src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs` (current model — confirm it has fields for new schema)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/DocumentSearchTools.cs` (consumer)

## Verification (full plan)

See `spec.md` §4. After Phase 5 completes successfully, this work is done.

## Risk + mitigation

| Risk | Mitigation |
|---|---|
| Spec misses a field; immutable index requires recreate | Compare spec.md §1.2 against deployed v2 schema dump (already done 2026-05-28). Compare against every search query in `RagService.cs`. Get a second reviewer before Phase 1.4. |
| Container created in wrong subscription/RG | Spec.md §2.1 has the exact target IDs from the user. Pre-flight check at Phase 2.1: `az account show` confirms subscription before creation. |
| New container missing FileStorageContainer.Selected for BFF MI | Pre-flight check in Phase 2.3. If the existing container has it, the new one (same container type) likely will too. |
| Indexing job races between old and new index during cutover | The new index is empty until Phase 5.3. Old paths writing to old index don't conflict. Single-config switch in Phase 3 is atomic on restart. |
| Performance: 28 fields with two 3072-dim vector fields per chunk → larger index | Mitigation: same shape as current v2 index plus 3 strings; storage delta is minor. Test with bulk upload before declaring "done." |

## Decision log

| Date | Decision | By |
|---|---|---|
| 2026-05-28 | New index name = `spaarke-file-index` (not `spaarke-files-v1`) | User |
| 2026-05-28 | New container name = `Spaarke Dev Container 2`, type = `Spaarke PAYGO 1` (existing) | User |
| 2026-05-28 | Don't migrate existing SPE files; leave the legacy container alone | User |
| 2026-05-28 | Drop legacy 1536-dim vector fields from new index (3072 only) | BFF dev |
| 2026-05-28 | Add `containerId`, `lastModified`, `sourceSystem` to schema (future-proofing) | BFF dev (pending review) |
