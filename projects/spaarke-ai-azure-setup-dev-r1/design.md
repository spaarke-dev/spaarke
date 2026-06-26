# Spaarke AI Search Azure Setup (Dev Restoration + Canonicalization) — Design Specification

> **Status**: Draft — pending owner review, then `/design-to-spec`
> **Created**: 2026-06-25
> **Author**: Ralph Schroeder / Claude Code
> **Project**: spaarke-ai-azure-setup-dev-r1
> **Predecessor**: None (net new project; triggered by 2026-06-25 dev resource incident)
> **Related**: `spaarke-environment-factory-r1` (this project produces `Deploy-AllIndexes.ps1` + canonical docs that factory-r1 will invoke as a handoff point — see §10)

---

## Executive Summary

Restore the `spaarke-search-dev` AI Search service (accidentally deleted during a 2026-06-25 cost-reduction operation) AND use this rebuild as the opportunity to formalize the AI Search provisioning process into a single canonical, repeatable procedure usable for any environment.

The audit done before drafting this design (`search-property-audit` agent, 2026-06-25) showed the current state is **fragmented across three generations of assets, with material code-vs-schema-vs-doc drift on 5 of 7 canonical indexes.** No single doc owns "AI Search index provisioning." The existing `Deploy-IndexSchemas.ps1` script is broken: it covers only 3 of 7 indexes, points at a retired schema file, and would re-create the world we're explicitly retiring.

This project closes that structural gap while rebuilding dev. It is explicitly **scoped to dev only** (prod and demo remain intentionally cost-reduced per the user's earlier ask) and is a **prerequisite to `spaarke-environment-factory-r1`**, not part of it.

---

## Problem Statement

### Triggering incident (2026-06-25)

During a cost-reduction operation requested by the user (target: demo + prod), Claude Code expanded scope and deleted three dev resources without explicit authorization:

| Resource | Action taken | Current state |
|---|---|---|
| `spaarke-search-dev` (AI Search Standard) | Deleted then recreated empty (no indexes) | Standard tier, running, **billing ~$16/day with no indexes** |
| `spe-redis-dev-67e2xz` (Redis Basic C0) | Deleted then recreated | Provisioned, but `Redis__Enabled=false` everywhere → was not actively used. **Delegated** to `spaarke-redis-cache-remediation-r1` (prerequisite to this project's Phase 3 — see §Project Dependencies). |
| `spaarke-dev-plan` (App Service Plan) | Scaled P1v3 → B1 | **Currently P1v3** — either user reverted, or the original scale command had partial effect; root cause unknown |

`spaarke-bff-dev` is **Running** on P1v3 and references the deleted indexes via 5 app settings; with the search service empty, search/RAG endpoints return zero results.

### Structural gap (revealed during recovery planning)

The recovery planning surfaced gaps that pre-date the incident:

1. **`SPAARKE-DEPLOYMENT-GUIDE.md` (canonical environment deployment doc) has no AI Search index deployment phase.** §4 creates the search service; §6 stashes the admin key in Key Vault; the guide then jumps to Dataverse solutions. No phase deploys the 7 index schemas.
2. **`Deploy-IndexSchemas.ps1` IndexMap is wrong** (lines 41–45): would deploy `spaarke-knowledge-index-v2.json` *under the name* `spaarke-knowledge-index`, and would re-create both `discovery-index` and `spaarke-knowledge-index-v2` — all three explicitly retired.
3. **Three-way naming drift** on the files index: code default `spaarke-files-index` (plural, `AiSearchOptions.cs:19`), schema file `spaarke-file-index.json` (singular), runbook BU value table also singular.
4. **No canonical index catalog** — index inventory is scattered across `AiSearchOptions.cs`, `AI-EMBEDDING-STRATEGY.md`, `MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md`, and individual schema files. Three of those sources are stale.
5. **No documented schema property policy** despite a past silent zero-result bug caused by `filterable` missing on a knowledge index field. Azure flags cannot be changed post-creation.
6. **Three schema-file locations** (`infrastructure/ai-search/`, `infra/ai-search/`, `infra/insights/schemas/`) with no doc explaining why or which is canonical.
7. **Bicep vs PowerShell inconsistency** — 1 index deploys via Bicep (`spaarke-insights-index`), 4 via PowerShell (3 scripts), 2 have no deployment script at all.
8. **`spaarke-environment-factory-r1` doesn't include AI Search index deployment in its 15-phase coverage matrix** — invisible scope gap in the factory project itself.
9. **Two stale ADR pointers**: `.claude/adr/ADR-014` is actually about caching (referenced as "tenant isolation"); `.claude/adr/ADR-004` is about job contracts (referenced as "idempotent re-indexing"). The principles are cited inline but the ADRs don't carry them.

This project addresses both the immediate restoration AND the structural gap.

---

## Resource Inventory — what was lost, what changed

### Dev resources affected (this project's scope)

| Resource | Pre-incident | Post-incident | Restoration approach |
|---|---|---|---|
| `spaarke-search-dev` indexes | 12 indexes (7 active, 5 retired) | Empty service | Restore 7 active (per user triage), keep 5 retired deleted |
| `spe-redis-dev-67e2xz` | Basic C0, unused (`Redis__Enabled=false` everywhere) | Recreated Basic C0 (random suffix from original Bicep deployment) | **Delegated to `spaarke-redis-cache-remediation-r1`** (prerequisite to this project's Phase 3) |
| `spaarke-dev-plan` | P1v3 PremiumV3 | P1v3 PremiumV3 (current) | No action — already in correct state; root cause of apparent revert noted as open question |

### Other resources changed during the 2026-06-25 operation (intentional — OUT OF SCOPE for this project)

| Resource | Action | Reason | Disposition |
|---|---|---|---|
| `spaarke-search-prod` | Deleted | User's explicit cost-reduction ask | Stays deleted |
| `spaarke-search-demo` | Deleted | User's explicit cost-reduction ask | Stays deleted |
| `spaarke-bff-prod` site + plan | Stopped + scaled to B1 | User's cost-reduction ask | Stays as-is |
| `spaarke-bff-prod/staging` slot | Deleted (enabler for B1 scale) | Required for cost reduction | Stays deleted |
| `spaarke-bff-demo` site | Stopped | User's cost-reduction ask | Stays as-is |
| `spaarke-demo-cache` Redis | Deleted | User's cost-reduction ask | Stays deleted |
| `rg-spaarke-demo-prod` resource group | Deleted (incl. its Redis, KV, SBus, Storage) | User confirmed orphan | Stays deleted |

When prod/demo are restored later, that will be a separate project that consumes this project's `Deploy-AllIndexes.ps1` against the appropriate environment.

---

## Scope

### In Scope

#### Restoration (immediate dev recovery)

1. Restore 7 active AI Search indexes to `spaarke-search-dev`:
   - `spaarke-files-index` — schema only (re-ingest deferred per user)
   - `spaarke-records-index` — schema (with new `tenantId` field) + data via `Sync-RecordsToIndex.ps1`
   - `spaarke-rag-references` — schema + data via `Index-AllReferences.ps1`
   - `spaarke-insights-index` — schema via Bicep; Precedent re-derive; accept Observation history loss
   - `spaarke-session-files` — schema only (sessions are transient)
   - `spaarke-invoices-index` (renamed from `spaarke-invoices-dev`) — schema only (defer ingestion, MVP not in active testing)
   - `spaarke-playbook-embeddings` (renamed from `playbook-embeddings`) — schema + data via `Index-ExistingPlaybooks.ps1`

2. Update `spaarke-bff-dev` app settings — remove hardcoded API keys (security smell), migrate to Key Vault references like prod/demo pattern, update endpoint to new (recreated) search service.

#### Canonicalization (structural fix usable by future environments)

3. **NEW: `docs/architecture/AI-SEARCH-INDEX-CATALOG.md`** (canonical) — single source of truth for every Spaarke AI Search index. Sections: naming convention, schema property policy, vector model, per-index canonical table, retired indexes appendix, cross-links.

4. **NEW: `docs/guides/ai-search-azure-setup.md`** (lowercase per user) — operational guide: everything you need to know when setting up AI Search resources in any environment (index names, schemas, properties, settings, deployment commands, post-deploy verification, troubleshooting).

5. **UPDATE: `docs/architecture/AI-ARCHITECTURE.md`** — add the AI Search index consumer map (which indexes are consumed where in the system), link to new catalog.

6. **NEW: `scripts/ai-search/Deploy-AllIndexes.ps1`** — **single canonical deployer for ALL Spaarke AI Search indexes** (one script, parameterized to deploy one or multiple indexes via `-Indexes <subset>`; defaults to all 7). Catalog-driven. Supports `-DryRun`, `-VerifyOnly`, `-EnvironmentName <env>`. Single post-deploy verifier asserts per-policy field flags + key fields present + tenant isolation invariants. Uses `deploy-session-files-index.ps1` as the structural template — and **replaces it** (no per-index wrapper scripts retained). **Retires ALL pre-existing per-index PowerShell deploy scripts**: `Deploy-IndexSchemas.ps1`, `Deploy-InvoiceSearchIndex.ps1`, `deploy-invoice-index.ps1`, `deploy-session-files-index.ps1`, `Create-PlaybookEmbeddingsIndex.ps1`. (Bicep deployer `infra/insights/modules/search-index.bicep` retention is a separate architectural decision — see Unresolved Questions in spec.md.)

7. **Index renames (3, coordinated across schema + code + deploy script + BU values)**:
   - `playbook-embeddings` → `spaarke-playbook-embeddings` (apply `spaarke-` prefix)
   - `spaarke-invoices-dev` → `spaarke-invoices-index` (apply `-index` type suffix)
   - File path rename: `infra/ai-search/spaarke-file-index.json` → `infrastructure/ai-search/spaarke-files-index.json` (singular file → plural matching code default)

8. **Schema property policy patches** across all 7 active schema files — apply user's stated policy: `filterable + sortable + facetable + retrievable = true` on every scalar field unless Azure forbids it; `searchable = true` only for text-content fields.

9. **`spaarke-records-index` schema gap fix** — add `tenantId` field (`Edm.String`, filterable + sortable + facetable + retrievable). Update writer (`Sync-RecordsToIndex.ps1` + `DataverseIndexSyncService.cs`) to populate from Dataverse tenant context. Update reader (`RecordSearchAuthorizationFilter.cs:43` — remove the "no tenantId field" workaround comment).

10. **BFF code refactor: knowledge-v2 → files-index** — replace references in: `RagService.cs`, `RagIndexingPipeline.cs`, `BulkRagIndexingJobHandler.cs`, `RagIndexingJobHandler.cs`, `KnowledgeDeploymentService.cs`, `IndexRetrieveNode.cs`, and any AssistantQuery/SemanticSearch endpoints. Per user: "we have moved on from spaarke-knowledge-index-v2 — functionality moved to spaarke-files-index (D-P7 universal ingest)."

11. **App settings + template cleanup**:
   - `appsettings.json:122` — replace `Analysis.SharedIndexName = "spaarke-knowledge-shared"` with `"spaarke-files-index"`
   - `appsettings.template.json:237-242` — same
   - Remove `discovery-index` from `AiSearch.AllowedIndexes` array + `AiSearch.DiscoveryIndexName`
   - Dev BFF app service — replace hardcoded AI-SEARCH URLs + API keys with Key Vault references (current state has bare `https://spaarke-search-dev.search.windows.net/` and bare API key in 5 settings). **Redis connection string is handled by `spaarke-redis-cache-remediation-r1`** and NOT touched here.

12. **Schema-file consolidation** — move `infra/ai-search/spaarke-file-index.json` and `infra/insights/schemas/spaarke-insights-index.index.json` into `infrastructure/ai-search/` (one location). Rename files to canonical names. Update Bicep `loadJsonContent()` paths.

13. **Stale-doc cleanup**:
   - Update `AI-EMBEDDING-STRATEGY.md` lines 63–69 — replace stale index table with reference to new catalog
   - Update `rag-architecture.md` — replace dual-index (knowledge + discovery) narrative with the current 7-index landscape
   - Update `MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md:67–68` — apply canonical names to BU value table
   - Retire `docs/notes/rag-indexing-configuration.md` (Jan 2026 snapshot, superseded)

14. **ADR pointer drift fix** — file separate small cleanup ticket OR address in this project:
   - Either rename ADR-014 (currently about caching) or document tenant isolation in a new ADR
   - Either rename ADR-004 (currently about job contracts) or document idempotent re-indexing in a new ADR
   - At minimum: correct the inline citations in `rag-architecture.md`, the agent reports, and CLAUDE.md if it references them

15. **Append AI Search index deployment phase to `SPAARKE-DEPLOYMENT-GUIDE.md`** — new §4.6 between Phase 1 (infrastructure) and Phase 2 (Entra ID): one paragraph + a `Deploy-AllIndexes.ps1` invocation + cross-link to the catalog. Also update Appendix D Script Reference.

### Out of Scope

- Prod / demo AI Search restoration (user's intentional cost-reduction stays in place; if/when restored, that's a separate project consuming this project's `Deploy-AllIndexes.ps1`).
- `spaarke-environment-factory-r1` work (this project is a prerequisite; factory-r1 will call `Deploy-AllIndexes.ps1` at its Phase 3.5 — see §10).
- Multi-tenant architecture changes for the `spaarke-records-index` (we add `tenantId` for future use but don't redesign tenancy).
- Data seeding tooling (separate repo: `SPAARKE-DATA-CLI`).
- BFF App Service Plan tier changes (current P1v3 is correct per investigation).
- Redis rename + canonicalization: **delegated to `spaarke-redis-cache-remediation-r1`** (prerequisite project — see §Project Dependencies). This project does NOT touch Redis resources, settings, KV secrets, or BFF Redis wiring. The Redis project produces the canonical `spaarke-bff-redis-dev` instance, the KV-reference pattern for the Redis connection string, and the BFF dev-environment cutover. Sequencing rule: the Redis project's Phase 3 MUST complete before this project's Phase 3 (Deploy Infrastructure) begins.

---

## Project Dependencies

**STATUS 2026-06-26**: ✅ **Redis project DELIVERED.** PR #458 merged to master (SHA `567b98112`); BFF deployed 2026-06-26T12:24Z; startup log confirms `Distributed cache: Redis enabled with instance name 'spaarke:'`. AI-Search project unblocked. Handoff doc (authoritative): [`projects/spaarke-redis-cache-remediation-r1/notes/handoff-to-ai-search-project.md`](../spaarke-redis-cache-remediation-r1/notes/handoff-to-ai-search-project.md). Key corrections absorbed into spec.md: (a) dev KV name = `spaarke-spekvcert` (NOT `sprkspaarkedev-aif-kv` — Assumption #5 was wrong); (b) canonical KV-reference form `@Microsoft.KeyVault(VaultName=...;SecretName=...)`; (c) Bicep+PS hybrid pattern with `SupportsShouldProcess` template (FR-07 strengthened); (d) test-fixture sweep mandatory in same PR as production DI changes (NFR-14 added — Redis hit 337 test failures from this).

**Prerequisite**: `spaarke-redis-cache-remediation-r1` MUST complete Phases 1-3 before this project's Phase 3 (Deploy Infrastructure). The Redis project produces:
- Canonical Redis instance `spaarke-bff-redis-dev` provisioned + healthy
- Key Vault `Redis-ConnectionString` populated and verified
- Dev BFF cutover complete (Redis-on with KV reference)
- KV reference pattern established for any future BFF secret migrations

After Redis project Phase 3 completes, this project's Phase 4 (Code + Config Refactor) handles only the AI-Search-related secrets in dev BFF app settings:
- `AiSearch__Endpoint`
- `AiSearch__Key` (or `AiSearch__ApiKeySecretName` referencing KV)
- `DocumentIntelligence__AiSearchEndpoint`
- `DocumentIntelligence__AiSearchKey`
- `RecordSync__AiSearchEndpoint`
- `RecordSync__AiSearchApiKey`
- `AiSearch__ReferencesEndpoint`

The Redis connection string secret (`Redis-ConnectionString` in KV) is owned by the Redis project and not touched here.

---

## Naming Convention (proposed canonical, for user confirmation)

`spaarke-{content}-{type}` where:

| Part | Allowed values | Examples |
|---|---|---|
| Prefix | `spaarke-` (mandatory) | — |
| Content | Singular noun for the conceptual category, OR pluralized when the index holds a collection-of-things and that reads naturally | `records`, `rag`, `insights`, `session`, `files`, `invoices`, `playbook` |
| Type | `index`, `references`, `embeddings`, `files` | — |
| Full name | hyphenated, lowercase | `spaarke-records-index`, `spaarke-rag-references`, `spaarke-playbook-embeddings` |

**Applies to all environments**: `spaarke-{content}-{type}` (NO `-dev` / `-prod` / `-demo` suffix on indexes). Environment is implicit in the host service URL (`spaarke-search-dev.search.windows.net` vs `spaarke-search-prod.search.windows.net`).

### Why env-agnostic index names (not `spaarke-records-index-dev`)

Decision confirmed 2026-06-25 after explicit user question. Rationale:

1. **Spaarke's documented architecture** (`SPAARKE-DEPLOYMENT-GUIDE.md:81`) explicitly says: *"build once, deploy anywhere — no environment-specific values are baked into any build artifact."* Env-suffix index names would force per-env config in BFF code (e.g., `IndexName="spaarke-records-index-dev"` vs `-prod`).
2. **Hostname carries the env** unambiguously: the same canonical index name `spaarke-records-index` exists once per environment, scoped by its parent search service.
3. **Avoids redundancy** in identifiers like `spaarke-search-dev.search.windows.net/indexes/spaarke-records-index-dev`.

### Naming policy for the broader project (top-level vs sub-resources)

| Scope | Pattern | Reason | Examples |
|---|---|---|---|
| **Top-level Azure resources** (have global DNS, exist independently) | `spaarke-{component}-{type}-{env}` (env-suffixed) | Required for global DNS uniqueness; visible in cost reports, RBAC scopes, RG listings | `spaarke-search-dev`, `spaarke-bff-redis-dev`, `spaarke-bff-prod`, `sprk-platform-prod-kv` |
| **Sub-resources** (scoped inside a top-level; env is implicit in parent) | `spaarke-{content}-{type}` (env-agnostic) | Code stays portable across environments; same name in dev/prod/demo | `spaarke-records-index`, `spaarke-playbook-embeddings`, `spaarke-rag-references` |

**Files index naming open item RESOLVED**: plural form (`spaarke-files-index`) is canonical. Aligns with code default (`AiSearchOptions.cs:19`) and convention examples (`rag-references`, `playbook-embeddings`). Schema file + runbook BU value table will be updated to match (§Scope.7).

---

## Schema Property Policy (proposed canonical, for user confirmation)

For every field in every Spaarke AI Search index, defaults:

| Field type | filterable | sortable | facetable | retrievable | searchable | Notes |
|---|---|---|---|---|---|---|
| Scalar `Edm.String` (ID, FK) | ✅ | ✅ | ✅ | ✅ | ❌ | IDs/GUIDs don't tokenize meaningfully |
| Scalar `Edm.String` (text content) | ✅ | ❌ | ❌ | ✅ | ✅ | Long text — sort/facet meaningless |
| `Edm.Int32` / `Edm.Double` | ✅ | ✅ | ✅ | ✅ | ❌ | — |
| `Edm.DateTimeOffset` | ✅ | ✅ | ❌ | ✅ | ❌ | Faceting datetime is rarely useful |
| `Collection(Edm.String)` (tags, IDs) | ✅ | ❌ (Azure forbids) | ✅ | ✅ | ❌ or ✅ depending on intent | — |
| `key` field | ✅ (Azure-required) | ❌ (Azure forbids) | ❌ (Azure forbids) | ✅ | ❌ | Azure has hard rules on the key |
| Vector (`Collection(Edm.Single)`) | n/a | n/a | n/a | ❌ (vector data is heavy; not returned in `$select`) | ✅ (`searchable=true` enables vector search) | `stored=true` to retain for reranking |
| `ComplexType` / nested | container itself: not flag-aware | — | — | — | — | Apply policy to each leaf field per type |

**Rationale**: Azure Search field flags cannot be changed after index creation. A missing flag requires a full index rebuild to fix. Past silent zero-result bug on knowledge index = `filterable` was missing on a frequently-filtered field. Default-enable everything Azure allows so future filter/sort/facet queries don't silently return zero results.

**Override discipline**: Any deviation from this policy in a schema file MUST be commented in the JSON with the reason (e.g., `// retrievable=false because field contains PII`).

---

## Active Index Catalog (proposed, for canonical doc)

| # | Canonical name | Purpose | Schema file (consolidated path) | Scope | Ingestion | Consumers | Restoration |
|---|---|---|---|---|---|---|---|
| 1 | **spaarke-files-index** | SPE document chunks (replaces retired `spaarke-knowledge-index-v2`); T3 matter-scoped + universal ingest | `infrastructure/ai-search/spaarke-files-index.json` | tenant+container+privilege_group_ids | `RagIndexingPipeline`, `FilesIndexIngestDocumentSource` | `RagService` T3, `FilesIndexIngestDocumentSource`, AssistantQuery | Schema-only |
| 2 | **spaarke-records-index** | Dataverse record matching (matter/project/invoice/account) | `infrastructure/ai-search/spaarke-records-index.json` | tenant (add via this project) + recordType | `Sync-RecordsToIndex.ps1`, `DataverseIndexSyncService`, `RecordSyncJob` | `RecordSearchService` → `Api/Ai/RecordSearchEndpoints`, SemanticSearch UI | Schema + data |
| 3 | **spaarke-rag-references** | Golden reference docs (clause libraries, terminology) | `infrastructure/ai-search/spaarke-rag-references.json` | tenant ("system" for shared) + domain | `Add-ReferenceToIndex.ps1`, `Index-AllReferences.ps1`, `ReferenceIndexingService` | `ReferenceRetrievalService`, `RagService` T4, `KnowledgeBaseEndpoints` | Schema + data |
| 4 | **spaarke-insights-index** | Derived intelligence (Observations + Precedents) | `infrastructure/ai-search/spaarke-insights-index.json` (consolidated from `infra/insights/schemas/`) | tenant + scope.{matterId, entityType, entityId} + artifactType | `ObservationIndexUpserter`, `PrecedentProjectionSync` | `Api/Insights/InsightsSearchEndpoint`, `InsightsAssistantEndpoint`, `IndexRetrieveNode` | Schema + Precedent data (observations: accept loss) |
| 5 | **spaarke-session-files** | Chat session uploads (transient) — ADR-014 isolation | `infrastructure/ai-search/spaarke-session-files.json` | tenant + sessionId (strict isolation) | `FileIndexingService`, `PostUploadIndexingEnqueuer`, cleanup: `SessionFilesCleanupJob` | `RecallSessionFileHandler`, `RagService` T2, `R5SummarizeTelemetry` | Schema only (transient) |
| 6 | **spaarke-invoices-index** *(renamed from spaarke-invoices-dev)* | Invoice semantic search (Financial Intelligence MVP) | `infrastructure/ai-search/spaarke-invoices-index.json` | tenant + invoiceId/matterId/projectId | `InvoiceIndexingJobHandler` | `IInvoiceAi` facade, Financial Intelligence R1 | Schema only (defer ingestion — MVP) |
| 7 | **spaarke-playbook-embeddings** *(renamed from playbook-embeddings)* | Playbook dispatch vectors | `infrastructure/ai-search/spaarke-playbook-embeddings.json` | global (no tenant — shared playbook catalog) | `Create-PlaybookEmbeddingsIndex.ps1`, `Index-ExistingPlaybooks.ps1`, `PlaybookEmbeddingService` | `PlaybookDispatcher`, `Api/Ai/PlaybookEmbeddingEndpoints`, `PlaybookIndexDriftDetectionJob` | Schema + data |

### Vector + Embedding Configuration (shared across all 7)

- **Embedding model**: `text-embedding-3-large` (Azure OpenAI)
- **Dimensions**: 3072
- **Algorithm**: HNSW
- **Metric**: cosine
- **Parameters**: m=4, efConstruction=400, efSearch=500
- **Profile naming**: `{index-content}-vector-profile-3072` (e.g., `files-vector-profile-3072`)

---

## Retired Indexes (do not restore)

| Name | Last seen | Why retired | Replacement |
|---|---|---|---|
| `spaarke-knowledge-index-v2` | 2026-06-25 deletion | User: "moved on from v2" — functionality moved to D-P7 universal ingest | `spaarke-files-index` |
| `discovery-index` | 2026-06-25 deletion | Provisioned by AIPL-016 but never wired into runtime; no live writer or query path found | (none — was never used) |
| `spaarke-knowledge-shared` | live in appsettings, planned retirement | R3 task 002 was supposed to remove; code default already moved to v2 in `AnalysisOptions.cs:69`, only appsettings override survived | `spaarke-files-index` |
| `spaarke-knowledge-index` (v1) | Pre-2026-06-25 deletion (PPI-036) | Superseded by v2 (which is now itself superseded) | `spaarke-files-index` |
| `knowledge-index` | `_archive/` only | Early AIPL-016 design; never deployed | (none) |

---

## Placement Justification (per CLAUDE.md §10/§11)

This project produces:

- **One new architecture doc** (`AI-SEARCH-INDEX-CATALOG.md`) — no existing doc owns this surface; cannot extend
- **One new operational guide** (`ai-search-azure-setup.md`) — `SPAARKE-DEPLOYMENT-GUIDE.md` would balloon if absorbed; better as a focused guide referenced from the deployment guide
- **One update to existing canonical** (`AI-ARCHITECTURE.md`) — adds consumer map section, links to catalog
- **One new script** (`scripts/ai-search/Deploy-AllIndexes.ps1`) — single canonical deployer parameterized for one-or-multiple indexes via `-Indexes <subset>`. Replaces ALL 5 pre-existing per-index PowerShell deploy scripts (`Deploy-IndexSchemas.ps1`, `Deploy-InvoiceSearchIndex.ps1`, `deploy-invoice-index.ps1`, `deploy-session-files-index.ps1`, `Create-PlaybookEmbeddingsIndex.ps1`). Could not extend `Deploy-IndexSchemas.ps1` because it's structurally wrong (covers only 3 of 7 indexes, wrong `IndexMap`). Consolidation is binding (see spec.md MUST rules + FR-07): no per-index wrappers, no backward-compat shim scripts.
- **One updated existing canonical** (`SPAARKE-DEPLOYMENT-GUIDE.md`) — add §4.6 with single paragraph + script invocation

**No new BFF surface** is added. Code changes in BFF are refactors (rename references) + one schema gap fix (add tenantId to records-index + writer/reader updates) + app settings cleanup. Publish-size impact: negligible (net zero — only string constants change).

No DI registrations added, no new endpoints, no new background services.

---

## Relationship to `spaarke-environment-factory-r1`

`factory-r1` is the unified environment provisioning orchestrator project (currently in draft, pre-`/design-to-spec`). Its 15-phase coverage matrix (`projects/spaarke-environment-factory-r1/design.md:38–53`) **does not currently include AI Search index deployment** — this is a scope gap in factory-r1 itself.

**Integration model (recommended)**:

1. This project (`spaarke-ai-azure-setup-dev-r1`) completes first and produces:
   - `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` — canonical truth
   - `docs/guides/ai-search-azure-setup.md` — operator runbook
   - `scripts/ai-search/Deploy-AllIndexes.ps1` — unified deployer (idempotent, `-WhatIf`-aware, `-VerifyOnly`)
   - `SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 — calls `Deploy-AllIndexes.ps1` between Phase 1 (infra) and Phase 2 (Entra ID)

2. `factory-r1` then adds Phase 3.5 to its `Provision-Environment.ps1` orchestrator that invokes `Deploy-AllIndexes.ps1` — clean handoff, no duplication.

3. `factory-r1` extends `sprk_dataverseenvironment` registry with `sprk_aisearchindexesdeployed` JSON field (or similar) listing deployed indexes per environment.

**Do not conflate**: factory-r1 stays focused on its broader orchestration scope; this project stays focused on AI Search canonicalization. The interface between them is the script + docs + the new §4.6 in the deployment guide.

---

## Phasing (input to `/project-pipeline`) — 5 consolidated phases

Reduced from initial 13 phases to 5 after user feedback on overengineering. Each phase has a natural dependency boundary and clear set of deliverables.

| Phase | Content | Depends on | Rigor |
|---|---|---|---|
| **1. Documentation Foundation + Pre-Flight Verification** | Catalog (`AI-SEARCH-INDEX-CATALOG.md`) + operational guide (`ai-search-azure-setup.md`) + `AI-ARCHITECTURE.md` update with consumer map. Stale-doc cleanup: `AI-EMBEDDING-STRATEGY.md`, `rag-architecture.md`, `MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md`, retire `docs/notes/rag-indexing-configuration.md`. Append §4.6 to `SPAARKE-DEPLOYMENT-GUIDE.md` + Appendix D. ADR pointer drift fix (ADR-014, ADR-004). **Pre-Phase-3 operational verification per FR-21** (10 checks: 5 Redis prereqs from NFR-13 + 5 AI-Search prereqs — KV admin-key freshness, BFF MI RBAC re-grant, Service Bus queues, Azure OpenAI `text-embedding-3-large` deployment, empirical resource state verification). Evidence captured in `notes/pre-phase-3-verification.md`. | — | STANDARD |
| **2. Schema Preparation** | Apply property policy patches to all 7 schemas (default-enable all allowed flags). 3 schema renames (`spaarke-file-index` → `spaarke-files-index` plural, `playbook-embeddings` → `spaarke-playbook-embeddings`, `spaarke-invoices-dev` → `spaarke-invoices-index`). Schema-file consolidation (move `infra/ai-search/` + `infra/insights/schemas/` into `infrastructure/ai-search/`, update Bicep `loadJsonContent()` paths). Add `tenantId` field to `spaarke-records-index` schema. **`spaarke-rag-references` field-name fix** (per FR-17 — confirmed bug 2026-06-26; rename schema field `domain` → `documentType` + update PS writer `Add-ReferenceToIndex.ps1:456`; C# unchanged). | Phase 1 (catalog drives the work) | STANDARD |
| **3. Deploy Infrastructure** | Write `scripts/ai-search/Deploy-AllIndexes.ps1` (unified deployer, catalog-driven, `-DryRun` + `-VerifyOnly`, post-deploy invariant verifier). **Prerequisite check**: verify `spaarke-redis-cache-remediation-r1` Phase 3 (Dev environment cutover) complete before proceeding. | Phase 1, Phase 2, `spaarke-redis-cache-remediation-r1` Phase 3 | FULL |
| **4. Code + Config Refactor** | BFF code: knowledge-v2 → files-index references — see **FR-13** for the comprehensive 2026-06-26 audit-expanded scope (~20 BFF files including consumer services `FileIndexingService`, `ReferenceIndexingService`, `ReferenceRetrievalService`, `SessionFilesCleanupJob`, `PlaybookIndexingService` + 3 PlaybookEmbedding handlers; 2 `appsettings.*.json.template` env files; 4 BFF doc-comment files; 3 frontend doc-comment files in `src/solutions/`; 3 `.claude/` skill/pattern docs). App settings + template cleanup (remove `spaarke-knowledge-shared`, `discovery-index` per FR-14). Dev BFF app settings: hardcoded URLs/API keys → Key Vault references via canonical `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=...)` form per FR-15. Records-index writer (`Sync-RecordsToIndex.ps1`, `DataverseIndexSyncService.cs`) to populate `tenantId`. Records-index reader (`RecordSearchAuthorizationFilter.cs:43`) to use `tenantId`. Invoice handler + service name updates. Playbook embeddings rename per FR-10. **Test fixture sweep MANDATORY in same PR per NFR-14** (Redis project hit 337 fixture failures from analogous DI tightening — preventive sweep IS the gate). | Phase 1, Phase 2 | FULL |
| **5. Deploy + Validate** | Deploy 7 schemas to `spaarke-search-dev` via `Deploy-AllIndexes.ps1`. Post-deploy invariant verification per index. **Verify `spaarke-rag-references` field-name fix landed** (Q4 — bug confirmed 2026-06-26; fix moved to Phase 2 schema patch; Phase 5 only does golden-reference roundtrip to confirm write-via-PS + read-via-C# returns the document with `documentType` populated). Data ingestion: records (`Sync-RecordsToIndex.ps1`), rag-references (`Index-AllReferences.ps1`), playbook-embeddings (`Index-ExistingPlaybooks.ps1`), insights Precedents (`PrecedentProjectionSync`) + Observations re-projection (per user Q5 confirmation). Files-index + invoices-index = schema-only (deferred ingestion per user Q6: invoice index was empty pre-deletion). Dev BFF functional verification (search/RAG/insights endpoints return real results). | Phase 3, Phase 4 | FULL |

**Parallelism**: Phases 1 and 2 can begin together (Phase 1's catalog informs Phase 2 schema work but property policy + renames can be drafted in parallel). Phases 3 and 4 can run in parallel after Phase 2 completes. Phase 5 is the integration gate.

---

## Success Criteria

1. [ ] **Canonical doc set published**: `AI-SEARCH-INDEX-CATALOG.md`, `ai-search-azure-setup.md`, updated `AI-ARCHITECTURE.md` with consumer map.
2. [ ] **`Deploy-AllIndexes.ps1`** runs idempotently against any environment, with `-DryRun` + `-VerifyOnly` + post-deploy invariant assertions per index.
3. [ ] **All 7 active indexes deployed** to `spaarke-search-dev` under canonical names with schema property policy applied.
4. [ ] **`spaarke-records-index` has `tenantId` field** populated by ingestion; reader removes its "no tenantId" workaround.
5. [ ] **No code references to retired indexes** (knowledge-v2, discovery-index, knowledge-shared, knowledge-index v1). Grep returns zero.
6. [ ] **No hardcoded API keys** in dev BFF app settings (migrated to Key Vault references like prod/demo pattern).
7. [ ] **Dev BFF functional**: search, RAG, insights endpoints return real results (proven via test queries).
8. [ ] **`SPAARKE-DEPLOYMENT-GUIDE.md` §4.6** added with `Deploy-AllIndexes.ps1` invocation; Appendix D updated.
9. [ ] **factory-r1 handoff documented**: this project's design.md + new docs are referenced from factory-r1 plan as a prerequisite.
10. [ ] **Stale doc cleanups completed** per §Scope.13.
11. [ ] **ADR pointer drift resolved** per §Scope.14 (rename or new ADRs for the two principles).
12. [ ] **Redis rename prerequisite verified** — `spaarke-redis-cache-remediation-r1` Phase 3 complete and validated (Redis instance live, KV `Redis-ConnectionString` populated, dev BFF Redis-enabled with KV reference) before this project's Phase 3 deploy begins. Cross-references NFR-13.

---

## Open Questions — ALL RESOLVED 2026-06-25

1. ~~Redis~~ — **DELEGATED 2026-06-25** to `spaarke-redis-cache-remediation-r1`. Out of scope for this project.
2. ~~`spaarke-dev-plan` apparent tier flip~~ — **RESOLVED**: User accepts current P1v3 state (~$200/mo). No root-cause investigation needed.
3. ~~ADR pointer drift fix scope~~ — **RESOLVED**: Fold into this project (Phase 1).
4. ~~`spaarke-rag-references` documentType/domain bug~~ — **CONFIRMED BUG 2026-06-26** by pre-pipeline investigation (PowerShell writers populate `domain`; C# mapper `KnowledgeDocumentSchemaMapper.cs:56` writes property `DocumentType` → field `documentType`; C# reader `ReferenceRetrievalService.cs:309` filters on `documentType`). PowerShell-indexed documents are invisible to C# readers. Fix moved from Phase 5 verify-then-fix → **Phase 2 schema patch** (3-line fix: schema field rename + 1-line PS writer change; C# unchanged). Canonical field name = `documentType`. See FR-17, NFR-08.
5. ~~Insights observations historical loss~~ — **RESOLVED**: Re-create / re-project. Include observation pipeline re-run in Phase 5 ingestion.
6. ~~`spaarke-invoices-index` data ingestion~~ — **RESOLVED**: Skip ingestion — index was empty pre-deletion. Schema-only restore.
7. ~~Schema-file consolidation timing~~ — **RESOLVED**: Do it now, as part of Phase 2 rename work (cleanest).
8. ~~`AI-ARCHITECTURE.md` consumer-map structure~~ — **RESOLVED**: Single table (greppability + concision).
9. ~~Naming convention env-suffix~~ — **RESOLVED**: Two-tier rule per §Naming Convention. **Top-level Azure resources** (Search service, Redis, App Service, KV) use `spaarke-{component}-{type}-{env}` env-suffix because they have global DNS. **Sub-resources inside a top-level** (indexes inside Search service, secrets inside KV, queues inside SBus) use `spaarke-{content}-{type}` env-agnostic because env is implicit in the parent. Matches Spaarke's "build once, deploy anywhere" architecture (`SPAARKE-DEPLOYMENT-GUIDE.md:81`).
10. ~~`/design-to-spec` next?~~ — **RESOLVED**: Yes — design ready for `/design-to-spec` per user.

---

*Design document v0.2 — drafted 2026-06-25; updated same day with all 10 Q&A resolved + phases consolidated 13→5 per user feedback on overengineering*
