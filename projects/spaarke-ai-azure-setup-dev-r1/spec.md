# Spaarke AI Search Azure Setup (Dev Restoration + Canonicalization) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-25
> **Source**: `projects/spaarke-ai-azure-setup-dev-r1/design.md` (v0.2)
> **Project ID**: `spaarke-ai-azure-setup-dev-r1`

---

## Executive Summary

Restore the accidentally-deleted `spaarke-search-dev` AI Search service (7 active indexes) and rename `spe-redis-dev-67e2xz` to canonical `spaarke-bff-redis-dev`. Use this rebuild to formalize the AI Search provisioning process into a single canonical doc + unified deploy script + property policy + naming convention, usable for any future environment setup. Out of scope: prod/demo (intentionally cost-reduced per prior user ask) and `spaarke-environment-factory-r1` work (this project is a prerequisite, not part of it).

---

## Scope

### In Scope

**Restoration (immediate dev recovery)**
1. Restore 7 active AI Search indexes to `spaarke-search-dev`
2. Update `spaarke-bff-dev` app settings to remove hardcoded API keys + URLs (migrate to Key Vault references)
3. Rename `spe-redis-dev-67e2xz` → `spaarke-bff-redis-dev` (canonical pattern); update Key Vault `Redis-ConnectionString` secret

**Canonicalization (structural fix, reusable for any env)**
4. New canonical architecture doc: `docs/architecture/AI-SEARCH-INDEX-CATALOG.md`
5. New operational guide: `docs/guides/ai-search-azure-setup.md`
6. Update `docs/architecture/AI-ARCHITECTURE.md` with AI Search consumer map + link to catalog
7. New unified deploy script: `scripts/ai-search/Deploy-AllIndexes.ps1` (catalog-driven, `-DryRun` + `-VerifyOnly` + post-deploy invariant verifier)
8. Schema property policy patches across 7 schema files (default-enable `filterable`/`sortable`/`facetable`/`retrievable` on every scalar field unless Azure-restricted)
9. Schema renames coordinated across schema + code + script + BU values (3 renames: `files` plural, `spaarke-playbook-embeddings`, `spaarke-invoices-index`)
10. Schema-file consolidation to single location `infrastructure/ai-search/` (move `infra/ai-search/*` + `infra/insights/schemas/*`; update Bicep `loadJsonContent()` paths)
11. Add `tenantId` field to `spaarke-records-index` schema + writer + reader
12. BFF code refactor: knowledge-v2 → files-index references (~7 files)
13. App settings + template cleanup (retire `spaarke-knowledge-shared`, `discovery-index` references)
14. Stale-doc cleanup: `AI-EMBEDDING-STRATEGY.md`, `rag-architecture.md`, `MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md`, retire `docs/notes/rag-indexing-configuration.md`
15. Append §4.6 to `SPAARKE-DEPLOYMENT-GUIDE.md` (calls `Deploy-AllIndexes.ps1`) + Appendix D Script Reference update
16. ADR pointer drift fix: ADR-014 (caching ≠ tenant isolation) and ADR-004 (jobs ≠ idempotent re-indexing) — either rename ADRs or write new ones for the inline-cited principles

### Out of Scope

- Prod / demo AI Search restoration (user's intentional cost-reduction stays in place)
- `spaarke-environment-factory-r1` work (this project is a prerequisite; factory-r1 will call `Deploy-AllIndexes.ps1` at its Phase 3.5 — handoff documented)
- Multi-tenant architecture redesign for `spaarke-records-index` (add `tenantId` field for future use; don't redesign tenancy)
- Data seeding tooling (separate repo: `SPAARKE-DATA-CLI`)
- BFF App Service Plan tier changes (current P1v3 confirmed correct)
- Restoring deleted prod resources (BFF prod staging slot, demo Redis, `rg-spaarke-demo-prod` RG) — all stay deleted per user's cost-reduction ask

### Affected Areas

| Path | Description |
|---|---|
| `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` | NEW — canonical index catalog |
| `docs/architecture/AI-ARCHITECTURE.md` | UPDATE — consumer-map section + catalog link |
| `docs/architecture/rag-architecture.md` | UPDATE — replace dual-index narrative with 7-index landscape |
| `docs/guides/ai-search-azure-setup.md` | NEW — operational guide |
| `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` | UPDATE — add §4.6 + Appendix D entry |
| `docs/guides/AI-EMBEDDING-STRATEGY.md` | UPDATE — replace stale index inventory (lines 63–69) |
| `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` | UPDATE — apply canonical names to BU value table (lines 67–68) |
| `docs/notes/rag-indexing-configuration.md` | RETIRE (Jan 2026 snapshot) |
| `scripts/ai-search/Deploy-AllIndexes.ps1` | NEW — unified deployer |
| `scripts/ai-search/Deploy-IndexSchemas.ps1` | RETIRE (broken IndexMap) |
| `infrastructure/ai-search/*.json` | CONSOLIDATE + PATCH (property policy + renames) |
| `infra/ai-search/spaarke-file-index.json` | MOVE to `infrastructure/ai-search/spaarke-files-index.json` |
| `infra/insights/schemas/spaarke-insights-index.index.json` | MOVE to `infrastructure/ai-search/spaarke-insights-index.json` |
| `infra/insights/modules/search-index.bicep` | UPDATE `loadJsonContent()` path post-consolidation |
| `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` | knowledge-v2 → files-index refs |
| `src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs` | knowledge-v2 → files-index refs |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/BulkRagIndexingJobHandler.cs` | knowledge-v2 → files-index refs |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RagIndexingJobHandler.cs` | knowledge-v2 → files-index refs |
| `src/server/api/Sprk.Bff.Api/Services/Ai/KnowledgeDeploymentService.cs` | knowledge-v2 → files-index refs |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/IndexRetrieveNode.cs` | knowledge-v2 → files-index refs |
| `src/server/api/Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs` | Populate `tenantId` field |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RecordSyncJob.cs` | Populate `tenantId` field |
| `src/server/api/Sprk.Bff.Api/Api/Filters/RecordSearchAuthorizationFilter.cs` | Use `tenantId` (remove "no tenantId field" workaround at line 43) |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs` | Index name constant: `spaarke-invoices-dev` → `spaarke-invoices-index` (line 40) |
| `src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceSearchService.cs` | Index name constant update (line 45) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookEmbeddingService.cs` | Index name constant: `playbook-embeddings` → `spaarke-playbook-embeddings` (line 46) |
| `src/server/api/Sprk.Bff.Api/appsettings.json` | Remove `spaarke-knowledge-shared` ref (line 122); remove `discovery-index` from `AllowedIndexes` |
| `src/server/api/Sprk.Bff.Api/appsettings.template.json` | Same as above (lines ~237–242) |
| `src/server/api/Sprk.Bff.Api/Configuration/AiSearchOptions.cs` | Update `DiscoveryIndexName` removal if present |
| `scripts/ai-search/Sync-RecordsToIndex.ps1` | Populate `tenantId` field |
| `scripts/ai-search/Add-ReferenceToIndex.ps1` | Verify `domain` field population |
| `scripts/ai-search/Index-AllReferences.ps1` | Ingestion script (no changes expected) |
| `scripts/Create-PlaybookEmbeddingsIndex.ps1` | Index name update + retire (replaced by Deploy-AllIndexes.ps1) |
| `scripts/Index-ExistingPlaybooks.ps1` | Index name update |
| `infrastructure/ai-search/Deploy-InvoiceSearchIndex.ps1` | RETIRE (replaced by Deploy-AllIndexes.ps1) |
| `infrastructure/ai-search/deploy-session-files-index.ps1` | KEEP as backward-compat wrapper that calls Deploy-AllIndexes.ps1 |
| `.claude/adr/ADR-014-*.md` | Either rename to reflect actual scope, OR write new ADR for tenant isolation |
| `.claude/adr/ADR-004-*.md` | Either rename to reflect actual scope, OR write new ADR for idempotent re-indexing |

---

## Requirements

### Functional Requirements

#### Documentation

1. **FR-01** — Create `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` as the canonical source of truth for every Spaarke AI Search index.
   **Acceptance**: Doc exists with these sections: naming convention (with top-level-vs-sub-resource rule), schema property policy (with rationale + past-bug reference), vector + embedding configuration, per-index canonical table (7 active indexes with name/purpose/schema-file/scope/ingestion/consumers/post-deploy-invariants), retired indexes appendix (5 deprecated indexes with replacement notes), cross-links to AI-EMBEDDING-STRATEGY / MULTI-CONTAINER-RUNBOOK / rag-architecture / INSIGHTS-ENGINE-ARCHITECTURE / RAG-CONFIGURATION.

2. **FR-02** — Create `docs/guides/ai-search-azure-setup.md` as the operational runbook.
   **Acceptance**: Doc exists with these sections: prerequisites (Azure CLI, search service, KV access), step-by-step procedure (provision service → set KV secret → run Deploy-AllIndexes.ps1 → verify), per-index schema deploy + ingestion procedure, troubleshooting, post-deploy verification commands, environment-specific variables. Reads as a complete operator-ready guide for setting up AI Search in ANY environment.

3. **FR-03** — Update `docs/architecture/AI-ARCHITECTURE.md` with AI Search consumer map.
   **Acceptance**: Doc has a new section (single greppable table) showing for each of 7 active indexes: index name, primary consumers (services + endpoints), data flow direction. Includes prominent link to AI-SEARCH-INDEX-CATALOG.md. Does NOT duplicate catalog content.

4. **FR-04** — Update stale docs to reflect 7-index reality:
   - `docs/guides/AI-EMBEDDING-STRATEGY.md` lines 63–69: replace stale index inventory with one-line reference to catalog
   - `docs/architecture/rag-architecture.md` lines 13–15: replace dual-index narrative with current 7-index landscape; cross-link to catalog
   - `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` lines 67–68: apply canonical names to BU value table
   - Retire `docs/notes/rag-indexing-configuration.md` (move to `_archive/` or delete)
   **Acceptance**: No doc references retired indexes as active; canonical names appear in all surviving docs.

5. **FR-05** — Append §4.6 to `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` (between Phase 1 Azure Infrastructure and Phase 2 Entra ID).
   **Acceptance**: New §4.6 "Phase 1.5: AI Search Index Schemas" contains: brief context paragraph, code block invoking `Deploy-AllIndexes.ps1 -EnvironmentName <env>`, cross-link to AI-SEARCH-INDEX-CATALOG.md and ai-search-azure-setup.md. Appendix D Script Reference includes `Deploy-AllIndexes.ps1`.

6. **FR-06** — Resolve ADR pointer drift for ADR-014 and ADR-004.
   **Acceptance**: Either (a) ADRs renamed to reflect actual content (ADR-014-ai-caching, ADR-004-job-contract — they already match) AND new ADRs created for the inline-cited principles (tenant isolation, idempotent re-indexing); OR (b) inline citations in `rag-architecture.md` removed/corrected. CLAUDE.md and any agent prompts that cite these ADRs are updated.

#### Deploy Infrastructure

7. **FR-07** — Write `scripts/ai-search/Deploy-AllIndexes.ps1` unified deployer.
   **Acceptance**: Script exists, is catalog-driven (reads index list from a manifest or the catalog itself), supports `-DryRun`, `-VerifyOnly`, `-Indexes <subset>`, `-EnvironmentName <env>`, idempotent (safe to re-run), includes post-deploy invariant verifier per index (vector dim, key field, required-filterable fields, schema policy compliance). Uses `deploy-session-files-index.ps1` as structural template. Returns non-zero exit on any failure.

8. **FR-08** — Rename `spe-redis-dev-67e2xz` to `spaarke-bff-redis-dev`.
   **Acceptance**: New Redis instance `spaarke-bff-redis-dev` (Basic C0, same RG: `spe-infrastructure-westus2`) provisioned; Key Vault secret `Redis-ConnectionString` updated to point to new instance; old `spe-redis-dev-67e2xz` deleted; BFF dev verified to still resolve Redis connection (even though `Redis__Enabled=false`).

#### Schemas

9. **FR-09** — Apply schema property policy to all 7 schemas.
   **Acceptance**: Every scalar field (`Edm.String`, `Edm.Int32`, `Edm.Double`, `Edm.DateTimeOffset`) in 7 schemas has `filterable=true sortable=true facetable=true retrievable=true` unless Azure forbids OR the field has a documented JSON comment explaining the override. `searchable=true` ONLY on text-content fields. `key` field follows Azure-required configuration. Vector fields use 3072 dimensions + HNSW cosine + `retrievable=false stored=true`.

10. **FR-10** — Apply 3 index renames coordinated across all touchpoints.
    **Acceptance**:
    - `spaarke-file-index.json` (singular) → `spaarke-files-index.json` (plural) — file path + JSON `name` field
    - `playbook-embeddings.json` → `spaarke-playbook-embeddings.json` — file path + JSON `name` field
    - `invoice-index-schema.json` → `spaarke-invoices-index.json` — file path + JSON `name` field
    Plus: code constants in `AiSearchOptions.cs`, `InvoiceIndexingJobHandler.cs:40`, `InvoiceSearchService.cs:45`, `PlaybookEmbeddingService.cs:46` all updated. BU value table in `MULTI-CONTAINER-RUNBOOK.md` updated. `Deploy-InvoiceSearchIndex.ps1` retired. Each rename happens as one atomic PR.

11. **FR-11** — Consolidate schema files to single location.
    **Acceptance**: `infra/ai-search/spaarke-file-index.json` moved to `infrastructure/ai-search/spaarke-files-index.json`. `infra/insights/schemas/spaarke-insights-index.index.json` moved to `infrastructure/ai-search/spaarke-insights-index.json`. Bicep `loadJsonContent()` paths in `infra/insights/modules/search-index.bicep` updated. All Deploy-AllIndexes.ps1 references point to consolidated location.

12. **FR-12** — Add `tenantId` field to `spaarke-records-index`.
    **Acceptance**: Schema has new `tenantId` field (`Edm.String`, `filterable=true sortable=true facetable=true retrievable=true searchable=false`). `DataverseIndexSyncService.cs` and `RecordSyncJob.cs` populate the field from Dataverse tenant context. `RecordSearchAuthorizationFilter.cs:43` updated to apply `tenantId eq` filter; the "no tenantId field" workaround comment removed.

#### Code + Config

13. **FR-13** — BFF code refactor: knowledge-v2 → files-index.
    **Acceptance**: Grep for `spaarke-knowledge-index-v2` returns zero matches in `src/server/api/`. References replaced with `spaarke-files-index` in: `RagService.cs`, `RagIndexingPipeline.cs`, `BulkRagIndexingJobHandler.cs`, `RagIndexingJobHandler.cs`, `KnowledgeDeploymentService.cs`, `IndexRetrieveNode.cs`, and any AssistantQuery/SemanticSearch endpoints discovered.

14. **FR-14** — App settings + template cleanup.
    **Acceptance**:
    - `appsettings.json:122` `Analysis.SharedIndexName` value updated from `spaarke-knowledge-shared` to `spaarke-files-index`
    - `appsettings.template.json` (lines ~237–242): same update
    - `discovery-index` removed from `AiSearch.AllowedIndexes` array and `AiSearch.DiscoveryIndexName`
    - No app setting references `spaarke-knowledge-shared`, `discovery-index`, `spaarke-knowledge-index-v2`, or `spaarke-knowledge-index` after change

15. **FR-15** — Dev BFF app settings KV-reference migration.
    **Acceptance**: Dev BFF (`spaarke-bff-dev`) app settings updated to use Key Vault references like prod/demo pattern. Specifically: `AiSearch__Endpoint`, `DocumentIntelligence__AiSearchEndpoint`, `RecordSync__AiSearchEndpoint`, `AiSearch__ReferencesEndpoint`, `DocumentIntelligence__AiSearchKey`, `RecordSync__AiSearchApiKey` — all use `@Microsoft.KeyVault(VaultName=...;SecretName=...)` syntax pointing to `sprkspaarkedev-aif-kv` or equivalent dev KV. No hardcoded URLs or API keys remain in dev BFF app settings.

#### Deploy + Validate

16. **FR-16** — Deploy 7 schemas to `spaarke-search-dev`.
    **Acceptance**: `Deploy-AllIndexes.ps1 -EnvironmentName dev` deploys all 7 indexes successfully. Post-deploy verifier reports all invariants pass. `az search indexes list -g spe-infrastructure-westus2 --service-name spaarke-search-dev` shows 7 indexes with canonical names.

17. **FR-17** — Validate `spaarke-rag-references` `domain`/`documentType` claim.
    **Acceptance**: Targeted grep + code-trace pass before Phase 5 data ingestion. Document whether the bug exists (writer/reader use different field names) OR is a non-issue (writer correctly maps `Domain = domain`, reader correctly filters by `domain`). Apply fix ONLY if bug is reproduced; otherwise close as non-issue with note in `_archive/` or PR description.

18. **FR-18** — Data ingestion for ingestible indexes.
    **Acceptance**:
    - `spaarke-records-index`: populated via `Sync-RecordsToIndex.ps1` (with `tenantId` field set)
    - `spaarke-rag-references`: populated via `Index-AllReferences.ps1` (all KNW-*.md files)
    - `spaarke-playbook-embeddings`: populated via `Index-ExistingPlaybooks.ps1`
    - `spaarke-insights-index`: Precedents populated via `PrecedentProjectionSync`; Observations re-projected via pipeline re-run against historical events
    - `spaarke-files-index`, `spaarke-session-files`, `spaarke-invoices-index`: schema-only (no ingestion — deferred or N/A)
    Verification: each ingestion script reports non-zero documents indexed (except schema-only indexes); spot-check a query returns expected matches.

19. **FR-19** — Dev BFF functional verification.
    **Acceptance**: With dev BFF Running, test the following endpoints return real (non-error, non-empty where data exists) results:
    - `GET /healthz` returns Healthy
    - `POST /api/ai/search` with `scope=entity` returns records (uses spaarke-records-index)
    - `POST /api/ai/rag/query` returns chunks (uses spaarke-files-index or spaarke-rag-references)
    - `POST /api/ai/insights/search` returns insights (uses spaarke-insights-index)
    - Playbook dispatch routes correctly (uses spaarke-playbook-embeddings)

### Non-Functional Requirements

- **NFR-01** — `Deploy-AllIndexes.ps1` MUST be idempotent (re-running against an environment where indexes already exist is safe; no destructive side effects without explicit `-Force`).
- **NFR-02** — Post-deploy verifier MUST fail-fast on policy violations (returns non-zero exit code; logs which index + which field + which violation).
- **NFR-03** — Index names MUST be environment-agnostic (`spaarke-records-index` in dev AND prod AND demo). Environment is implicit in the parent search service hostname.
- **NFR-04** — BFF code changes net publish-size delta MUST be ≤ 0 MB (this project only renames string constants and refactors references; no new packages, no new services). Verify via `dotnet publish` size comparison per CLAUDE.md §10 bullet 4.
- **NFR-05** — Prod and demo environments MUST remain unchanged throughout this project. No commands in Deploy-AllIndexes.ps1 or any script can target `spaarke-search-prod` or `spaarke-search-demo` during this project's execution.
- **NFR-06** — `Deploy-AllIndexes.ps1` MUST support `-DryRun` (show what would deploy without changes) and `-VerifyOnly` (run only post-deploy invariant checks against existing indexes).
- **NFR-07** — Schema renames MUST be coordinated atomically per rename (one PR per rename touches all of: schema file, JSON `name` field, BFF code constant, deploy script reference, BU value config, runbook table).
- **NFR-08** — `spaarke-rag-references` `documentType`/`domain` claim MUST be validated (FR-17) before any code fix is applied. No "fix" applied without reproduction.
- **NFR-09** — Every schema MUST comply with the property policy unless a documented JSON comment explains the override (e.g., `"// retrievable=false because field contains PII"`).
- **NFR-10** — Naming policy (top-level resource env-suffix vs sub-resource env-agnostic) MUST be documented as canonical in both the catalog and the operational guide. Future environment provisioning MUST follow this rule.
- **NFR-11** — All 7 schemas use `text-embedding-3-large` (3072 dimensions) for vector fields. No 1536-dim vectors permitted in any restored index.
- **NFR-12** — `Deploy-AllIndexes.ps1` total runtime for full 7-index deploy MUST complete within 30 minutes (Azure provisioning latency varies; this is a target not a hard deadline).

---

## Technical Constraints

### Applicable ADRs

- **ADR-014** — *(currently misattributed)*: caching. Inline-cited for tenant isolation principle. This project may rename or add new ADR.
- **ADR-004** — *(currently misattributed)*: job contract. Inline-cited for idempotent re-indexing principle. This project may rename or add new ADR.
- **ADR-013** — AI services bounded concurrency. Embedding generation in ingestion scripts MUST honor the `SemaphoreSlim` patterns documented in BFF (max 16 for indexing, max 5 for search).
- **ADR-017** — Background jobs (Service Bus pattern). `RagIndexingJobHandler` and `BulkRagIndexingJobHandler` refactors MUST preserve job contract.
- **ADR-028** — Auth v2 (canonical). Dev BFF app settings KV-reference migration MUST follow ADR-028 patterns (Key Vault references via `@Microsoft.KeyVault(...)` syntax, managed identity for secret resolution).

### MUST Rules (binding)

- ✅ MUST use canonical index names per AI-SEARCH-INDEX-CATALOG.md (no env suffix on indexes)
- ✅ MUST apply schema property policy unless Azure-restricted; document any override with a JSON comment
- ✅ MUST use 3072-dim vectors (`text-embedding-3-large`); no 1536-dim vectors
- ✅ MUST migrate dev BFF hardcoded URLs + API keys to Key Vault references
- ✅ MUST verify publish-size delta on BFF refactor (CLAUDE.md §10)
- ✅ MUST coordinate renames atomically (one rename = one PR touching all surfaces)
- ✅ MUST validate `spaarke-rag-references` claim before applying any code fix
- ❌ MUST NOT touch prod or demo AI Search services during this project
- ❌ MUST NOT introduce new BFF endpoints, services, DI registrations, or packages (this project is a refactor)
- ❌ MUST NOT restore retired indexes (`spaarke-knowledge-index-v2`, `discovery-index`, `spaarke-knowledge-shared`, `spaarke-knowledge-index` v1, `knowledge-index`)
- ❌ MUST NOT skip the post-deploy invariant verifier on any index deployment

### Existing Patterns to Follow

- **Deploy script pattern**: `infrastructure/ai-search/deploy-session-files-index.ps1` — has the gold-standard post-deploy invariant verification template (lines 209–215: asserts `tenantId + sessionId both filterable`). Use as structural template for `Deploy-AllIndexes.ps1`.
- **Bicep deployment pattern**: `infra/insights/modules/search-index.bicep` — has the Bicep deploymentScript + jq strip-comments pattern. May or may not be retained depending on consolidation decisions.
- **KV reference pattern**: `spaarke-bff-prod` app settings — `AiSearch__Endpoint = @Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=ai-search-endpoint)`. Apply same pattern to dev.
- **Schema reference**: `infrastructure/ai-search/spaarke-rag-references.json` — clean reference schema example (3072 vector, 3 named fields, semantic config).

---

## Success Criteria

1. [ ] **All 3 canonical docs published**: `AI-SEARCH-INDEX-CATALOG.md`, `ai-search-azure-setup.md`, updated `AI-ARCHITECTURE.md`. Verify by: file existence + content review against FR-01/02/03 acceptance criteria.
2. [ ] **`Deploy-AllIndexes.ps1` operational**: idempotent, `-DryRun` + `-VerifyOnly` work, post-deploy verifier asserts policy per index. Verify by: dry-run shows all 7 indexes; verify-only against deployed dev passes.
3. [ ] **All 7 active indexes deployed** to `spaarke-search-dev` with canonical names + correct property policy + 3072 vectors. Verify by: `az search indexes list` returns 7 + each schema query confirms field flags + vector dims.
4. [ ] **`spaarke-records-index` has `tenantId` field** populated by ingestion; reader removes workaround comment. Verify by: index query confirms field present; sample record query confirms `tenantId` populated; grep confirms reader code uses `tenantId` filter.
5. [ ] **No code references to retired indexes**. Verify by: `grep -r "spaarke-knowledge-index-v2\|spaarke-knowledge-shared\|discovery-index\|spaarke-knowledge-index" src/` returns zero matches.
6. [ ] **No hardcoded API keys or URLs** in dev BFF app settings (migrated to Key Vault references). Verify by: `az webapp config appsettings list --name spaarke-bff-dev` shows no `https://...search.windows.net` or raw keys.
7. [ ] **Dev BFF functional**: `/healthz` Healthy + 4 AI endpoints return real (non-error) results. Verify by: FR-19 acceptance.
8. [ ] **`SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 added** with Deploy-AllIndexes.ps1 invocation; Appendix D updated. Verify by: file inspection.
9. [ ] **factory-r1 handoff documented**: this project's design.md and new docs are referenced from factory-r1's plan as a prerequisite. Verify by: factory-r1 plan.md or notes include reference to this project's deliverables.
10. [ ] **Stale doc cleanups completed** per FR-04. Verify by: grep across `docs/` confirms no retired index names referenced as active.
11. [ ] **ADR pointer drift resolved** per FR-06. Verify by: file inspection of ADRs + CLAUDE.md + any agent prompts.
12. [ ] **Redis renamed**: `spaarke-bff-redis-dev` provisioned; `spe-redis-dev-67e2xz` deleted; KV secret updated; BFF dev resolves Redis connection. Verify by: Azure CLI listing + KV secret value + dev BFF log on startup.
13. [ ] **BFF publish-size delta ≤ 0 MB** per CLAUDE.md §10 NFR-01. Verify by: `dotnet publish -c Release` before/after + compressed size measurement.

---

## Dependencies

### Prerequisites
- `spaarke-search-dev` Azure AI Search service provisioned (Standard tier, westus2, `spe-infrastructure-westus2` RG) — **currently exists** (recreated empty 2026-06-25)
- `sprkspaarkedev-aif-kv` Key Vault accessible with admin secret access for adding `ai-search-endpoint`, `ai-search-key`, `Redis-ConnectionString` secrets if not present
- Azure CLI logged in with permission to: create AI Search indexes, modify Redis instances, modify Key Vault secrets, modify App Service app settings, all in `spe-infrastructure-westus2` and `rg-spaarke-dev` resource groups
- Source data intact: Dataverse records (for `Sync-RecordsToIndex.ps1`), KNW-*.md golden references (for `Index-AllReferences.ps1`), Dataverse playbooks (for `Index-ExistingPlaybooks.ps1`), insights event history (for Observations re-projection)
- `infrastructure/ai-search/_archive/` exists or can be created for retired schema files

### External Dependencies
- None — all work is within Spaarke repo + existing Azure subscription

---

## Owner Clarifications

*Answers captured during design iteration (recorded in design.md v0.2 Open Questions section, all 10 resolved):*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Redis disposition | Keep recreated or delete? | Don't recreate with random-suffix name. Rename to canonical `spaarke-bff-redis-dev`. Redis is coded into BFF, will be used in future. | Codified naming convention for top-level resources; new FR-08 + FR-15 |
| Dev plan tier | Investigate B1→P1v3 flip? | Accept current P1v3 (~$200/mo); no investigation needed | No action this project |
| ADR drift | Fold into this project or separate ticket? | Fold in | FR-06 |
| rag-references bug | Apply fix or verify first? | Confirm and validate before fix | FR-17 enforced as NFR-08 |
| Insights observations | Accept loss or re-project? | Re-project | Included in FR-18 |
| Invoices data | Defer or rebuild? | Skip — was empty pre-deletion | FR-18 (schema-only for invoices) |
| Schema consolidation | Now or later? | Now, in Phase 2 | FR-11 |
| AI-ARCH consumer map | Single table or per-section? | Single table | FR-03 |
| Env-suffix on index names | Use `-dev` suffix or not? | Two-tier rule: top-level resources env-suffixed, sub-resources env-agnostic | NFR-03 + NFR-10; canonical naming policy in catalog |
| Next step | `/design-to-spec` ready? | Yes | This spec |

---

## Assumptions

*Items where no explicit owner direction was given; proceeding with stated assumptions:*

- **ADR-014 / ADR-004 resolution path**: Assuming **(b) inline citation correction** (cheaper, less ADR sprawl) unless owner prefers (a) renaming + new ADRs. Flagged for confirmation during Phase 1 (FR-06 implementation).
- **Schema-file directory choice**: Assuming `infrastructure/ai-search/` (more mature, more files already) as the consolidation target. Alternative `infra/ai-search/` rejected.
- **`Deploy-AllIndexes.ps1` manifest source**: Assuming the catalog is itself the source of truth (script reads index list from the catalog or a small YAML/JSON manifest derived from it), not a separate hardcoded `$IndexMap` like the broken `Deploy-IndexSchemas.ps1`.
- **Observation re-projection scope**: Assuming "full pipeline re-run against historical events" means re-running the existing Observation pipeline; no new tooling needed beyond invoking existing services.
- **Dev BFF KV name**: Assuming dev BFF's Key Vault is `sprkspaarkedev-aif-kv` per `CONFIGURATION-MATRIX.md:25` — verify during Phase 4.
- **BU value updates**: Assuming `MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` lines 67–68 BU value table is the only place where index names need updating in BU configs — verify by grep during Phase 1.
- **`infra/insights/` retention**: Assuming `infra/insights/modules/search-index.bicep` is updated in-place to point at new consolidated schema path, NOT deleted. The Bicep deployment pattern may still be valuable for the insights index even if `Deploy-AllIndexes.ps1` becomes the primary deployer.

---

## Unresolved Questions

*None blocking implementation start. The following may arise during execution but do not block planning:*

- [ ] **Will the post-deploy verifier need read-only Search service permission?** (Likely just admin key reuse; verify during FR-07 implementation)
- [ ] **Should `Deploy-AllIndexes.ps1` write a `deploymentstate.json` artifact** (or call `factory-r1`'s registry?) recording what was deployed when? (Defer — factory-r1 hasn't designed its registry extension yet)
- [ ] **Should `infra/insights/modules/search-index.bicep` be retired entirely** in favor of `Deploy-AllIndexes.ps1`, or kept as the canonical Bicep pattern for environments that prefer Bicep over PowerShell? (Defer until Phase 3 — depends on how `Deploy-AllIndexes.ps1` shapes up)
- [ ] **Should we add `corsOptions` to all 7 schemas** (only `spaarke-files-index` has it currently)? (Defer — minor consistency cleanup, not blocking)

---

*AI-optimized specification. Original design: `projects/spaarke-ai-azure-setup-dev-r1/design.md` v0.2 (2026-06-25)*
