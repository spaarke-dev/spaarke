# AI Search Index Catalog — Canonical Source of Truth

> **Status**: Canonical — every Spaarke AI Search index ships against this catalog.
> **Created**: 2026-06-26 (`spaarke-ai-azure-setup-dev-r1` task 002, per FR-01)
> **Authority**: This document supersedes any inline index inventory in `AiSearchOptions.cs`, `AI-EMBEDDING-STRATEGY.md`, `MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md`, or individual schema files when they disagree.
> **Companion guide (operator runbook)**: [`docs/guides/ai-search-azure-setup.md`](../guides/ai-search-azure-setup.md) (created by task 003 of this project).

---

## Purpose

Before this catalog existed, Spaarke had **12 indexes across 3 schema directories**, no single doc owning the inventory, and material code-vs-schema-vs-doc drift on 5 of 7 active indexes (see `projects/spaarke-ai-azure-setup-dev-r1/design.md` §Problem Statement). The 2026-06-25 incident that deleted `spaarke-search-dev` exposed this gap. This catalog closes it.

The catalog answers, for any environment (`dev`/`staging`/`prod`/`demo`):

1. Which indexes are active? (7 — listed in §4)
2. What are they named? (env-agnostic per §1)
3. What schema policy must each field follow? (§2)
4. What vector + embedding configuration is canonical? (§3)
5. Which BFF service/endpoint consumes each? (§4 table)
6. Which historical indexes are retired, and what replaces them? (§5)
7. Where else should I read? (§6 cross-links)

---

## 1. Naming Convention

### Two-tier naming rule (binding, per NFR-03 + NFR-10)

Spaarke applies a **two-tier naming policy** that distinguishes top-level Azure resources from sub-resources scoped inside them.

| Tier | Pattern | Env-suffix? | Reason | Examples |
|---|---|---|---|---|
| **Top-level Azure resources** (have global DNS, exist independently) | `spaarke-{component}-{type}-{env}` | ✅ Yes (env-suffixed) | Required for global DNS uniqueness; visible in cost reports, RBAC scopes, RG listings | `spaarke-search-dev`, `spaarke-bff-redis-dev`, `spaarke-bff-prod`, `sprk-platform-prod-kv` |
| **Sub-resources** (scoped inside a top-level; env is implicit in parent) | `spaarke-{content}-{type}` | ❌ No (env-agnostic) | Code stays portable across environments; same name in dev / prod / demo; aligns with Spaarke's documented "build once, deploy anywhere" architecture (`SPAARKE-DEPLOYMENT-GUIDE.md:81`) | `spaarke-records-index`, `spaarke-playbook-embeddings`, `spaarke-rag-references` |

### Why env-agnostic index names

Decision confirmed 2026-06-25 after explicit owner question. Rationale:

1. **Build-once-deploy-anywhere architecture** — env-suffix on indexes (e.g., `spaarke-records-index-dev` vs `spaarke-records-index-prod`) would force per-env config in BFF code. The same canonical name `spaarke-records-index` exists once per environment, scoped by its parent search service hostname.
2. **Hostname carries the env unambiguously** — `spaarke-search-dev.search.windows.net/indexes/spaarke-records-index` is unambiguous without redundant env tags on the child resource.
3. **Avoids redundancy** in fully-qualified identifiers.

### Canonical name pattern: `spaarke-{content}-{type}`

| Part | Allowed values | Examples |
|---|---|---|
| Prefix | `spaarke-` (mandatory) | — |
| Content | Singular OR plural noun for the conceptual category (plural when the index holds a collection-of-things and that reads naturally) | `records`, `rag`, `insights`, `session`, `files`, `invoices`, `playbook` |
| Type | `index`, `references`, `embeddings`, `files` | — |
| Full name | hyphenated, lowercase | `spaarke-records-index`, `spaarke-rag-references`, `spaarke-playbook-embeddings` |

**Files index settled as plural** (`spaarke-files-index`, not `spaarke-file-index`) — matches code default in `AiSearchOptions.cs:19` and naming convention examples (`spaarke-rag-references`, `spaarke-playbook-embeddings`).

### Forbidden patterns

- ❌ `spaarke-{content}-{type}-{env}` on an index name (env-suffix on a sub-resource)
- ❌ Indexes without the `spaarke-` prefix (e.g., `discovery-index`, `playbook-embeddings`, `invoice-index-schema`) — all 5 retired indexes violate this
- ❌ Mixed-case or underscored names — all lowercase, hyphen-separated

---

## 2. Schema Property Policy

### Binding policy (NFR-09)

For every field in every Spaarke AI Search index, the default field flags are:

| Field type | filterable | sortable | facetable | retrievable | searchable | Notes |
|---|---|---|---|---|---|---|
| Scalar `Edm.String` (ID, FK, code) | ✅ | ✅ | ✅ | ✅ | ❌ | IDs/GUIDs don't tokenize meaningfully |
| Scalar `Edm.String` (text content) | ✅ | ❌ | ❌ | ✅ | ✅ | Long text — sort/facet meaningless |
| `Edm.Int32` / `Edm.Double` | ✅ | ✅ | ✅ | ✅ | ❌ | — |
| `Edm.DateTimeOffset` | ✅ | ✅ | ❌ | ✅ | ❌ | Faceting datetime is rarely useful |
| `Collection(Edm.String)` (tags, IDs) | ✅ | ❌ (Azure forbids) | ✅ | ✅ | ❌ or ✅ depending on intent | — |
| `key` field | ✅ (Azure-required) | ❌ (Azure forbids) | ❌ (Azure forbids) | ✅ | ❌ | Azure has hard rules on the key |
| Vector (`Collection(Edm.Single)`) | n/a | n/a | n/a | ❌ (vector data is heavy; not returned in `$select`) | ✅ (enables vector search) | `stored=true` to retain for reranking |
| `ComplexType` / nested | container itself: not flag-aware | — | — | — | — | Apply policy to each leaf field per type |

**Short form**: every scalar field gets `filterable = sortable = facetable = retrievable = true` unless Azure forbids the combination OR the field has a documented JSON-comment override. `searchable = true` ONLY on text-content fields. Vector fields use `retrievable = false` + `stored = true`.

### Rationale

**Azure Search field flags cannot be changed post index creation.** A missing flag requires a full index rebuild + re-ingestion to fix. The cost-of-being-permissive (slightly larger index metadata) is far smaller than the cost-of-being-restrictive (silent zero-result bugs + multi-hour rebuilds).

### Past-bug evidence (FR-17)

The `spaarke-rag-references` index has carried a **writer/reader field-name mismatch** since deployment, confirmed 2026-06-26 by pre-pipeline investigation:

- **PowerShell writers** (`scripts/ai-search/Add-ReferenceToIndex.ps1`) populate the field named `domain` (see schema line 39 above).
- **C# mapper** (`KnowledgeDocumentSchemaMapper.cs:56`) writes property `DocumentType` → field `documentType`.
- **C# reader** (`ReferenceRetrievalService.cs:309`) filters on `documentType`.
- **Result**: PowerShell-indexed documents are invisible to C# readers; every query returns silent zero results for PS-written docs.

This bug is the canonical example of why the schema property policy + canonical field-name discipline must be machine-verified per deploy. The fix (rename schema field `domain` → `documentType`; update PS writer; C# unchanged) lands in Phase 2 of this project (FR-17 + NFR-08), and the post-deploy invariant verifier (FR-07) will assert canonical field names per index from this catalog forward.

A second class of past bug — `filterable` missing on a frequently-filtered knowledge-index field, causing silent zero results — is the original motivation for the **default-enable-everything-Azure-allows** stance in the table above.

### Override discipline

Any deviation from the default flags in a schema file MUST be commented inline in the JSON with the reason:

```json
{
  "name": "ssn",
  "type": "Edm.String",
  "filterable": false,
  "// retrievable": "false because field contains PII — exclude from $select per ADR-019",
  "retrievable": false
}
```

The post-deploy invariant verifier in `Deploy-AllIndexes.ps1` (FR-07) reads these comment overrides and skips the policy assertion for those fields; un-commented overrides fail the verifier.

---

## 3. Vector + Embedding Configuration

### Canonical configuration (binding, NFR-11 + FR-20)

| Setting | Value | Notes |
|---|---|---|
| **Embedding model** | `text-embedding-3-large` | Azure OpenAI deployment in `spaarke-openai-dev` (and per-env equivalents) |
| **Vector dimensions** | **3072** | 1536-dim vectors are FORBIDDEN in any restored index |
| **Algorithm** | HNSW | — |
| **Distance metric** | cosine | — |
| **HNSW parameters** | `m=4, efConstruction=400, efSearch=500` | Same across all 7 indexes for consistency |
| **Vector field naming** | `contentVector3072` (or `contentVector` legacy) | Suffix `3072` makes dimensionality explicit at field-name level |
| **Vector field flags** | `retrievable=false, stored=true, searchable=true` | Vector data is heavy; not returned in `$select`; retained for reranking |
| **Vector profile naming** | `{index-content}-vector-profile-3072` | Examples: `rag-references-vector-profile-3072`, `session-files-vector-profile-3072` |
| **HNSW algorithm naming** | `hnsw-{index-content}-3072` | Examples: `hnsw-rag-references-3072`, `hnsw-session-files-3072` |

### BFF appsettings dimensionality match (NFR-11 + FR-20)

The BFF embedding client MUST be configured for `text-embedding-3-large`. If `appsettings.template.json` or `appsettings.json` declares `text-embedding-3-small` (1536-dim), every embedding-generating ingestion will fail at upsert time with Azure Search `400 Bad Request: vector dimension mismatch`.

**Authoritative key**: `appsettings.template.json` → `EmbeddingModelName: "text-embedding-3-large"` (FR-20 fix on `appsettings.template.json:248`, 2026-06-26 audit).

Cross-refs:
- **NFR-11** — All 7 schemas use `text-embedding-3-large` (3072 dim); BFF appsettings MUST match.
- **FR-20** — Reconciles embedding-model name across spec, schemas, and BFF appsettings to the single canonical value.

### Why 3072-dim is canonical (not 1536)

The 3072-dim `text-embedding-3-large` model provides materially better retrieval quality on long-form legal text than the 1536-dim `text-embedding-3-small` model. The cost delta (~$0.13 vs $0.02 per 1M tokens) is negligible against the volume of Spaarke's embedding workload. Schema property cost is identical (3072 floats vs 1536 floats — both store as `Collection(Edm.Single)`).

Rolling back to 1536-dim would require: (a) re-deploying the OpenAI `text-embedding-3-small` deployment, (b) rewriting all 7 schema vector dimensions, (c) re-ingesting all data. Architectural rollback is **NOT recommended**; this catalog is authoritative.

---

## 4. Active Index Catalog (7 indexes)

| # | Canonical name | Purpose | Schema file | Scope (filter dimensions) | Ingestion source | Consumers (services + endpoints) | Post-deploy invariants |
|---|---|---|---|---|---|---|---|
| 1 | **spaarke-files-index** | SPE document chunks (replaces retired `spaarke-knowledge-index-v2`); T3 matter-scoped + universal ingest per D-P7 | `infrastructure/ai-search/spaarke-files-index.json` | `tenantId` + `container` + `privilege_group_ids` | `RagIndexingPipeline`, `FilesIndexIngestDocumentSource`, `FileIndexingService` | `RagService` T3, `FilesIndexIngestDocumentSource`, AssistantQuery endpoints, `KnowledgeBaseEndpoints` | Key field present; vector field `contentVector` is 3072-dim HNSW cosine; `tenantId` + `container` + `privilege_group_ids` filterable |
| 2 | **spaarke-records-index** | Dataverse record matching (matter / project / invoice / account) | `infrastructure/ai-search/spaarke-records-index.json` | `tenantId` (added Phase 2 per FR-12) + `recordType` + `dataverseEntityName` | `Sync-RecordsToIndex.ps1`, `DataverseIndexSyncService`, `RecordSyncJob` | `RecordSearchService` → `Api/Ai/RecordSearchEndpoints`, `SemanticSearchControl` PCF | Key field present; `tenantId` field exists + filterable + populated; `contentVector` is 3072-dim HNSW cosine; `recordType` + `dataverseRecordId` + `dataverseEntityName` filterable; `privilege_group_ids` filterable |
| 3 | **spaarke-rag-references** | Golden reference docs (clause libraries, terminology, KNW-*.md content) | `infrastructure/ai-search/spaarke-rag-references.json` | `tenantId` (`"system"` for shared) + `documentType` (renamed from `domain` per FR-17) | `Add-ReferenceToIndex.ps1`, `Index-AllReferences.ps1`, `ReferenceIndexingService` | `ReferenceRetrievalService`, `RagService` T4, `KnowledgeBaseEndpoints` | Key field present; canonical field name is `documentType` (NOT `domain`); `contentVector3072` is 3072-dim HNSW cosine; `tenantId` + `documentType` + `knowledgeSourceId` filterable; semantic config references `documentType` |
| 4 | **spaarke-insights-index** | Derived intelligence (Observations + Precedents) | `infrastructure/ai-search/spaarke-insights-index.json` (consolidated from `infra/insights/schemas/spaarke-insights-index.index.json` per FR-11) | `tenantId` + `scope.{matterId, entityType, entityId}` + `artifactType` | `ObservationIndexUpserter`, `PrecedentProjectionSync` | `Api/Insights/InsightsSearchEndpoint`, `InsightsAssistantEndpoint`, `IndexRetrieveNode` | Key field present; vector field is 3072-dim HNSW cosine; `tenantId` + scope fields filterable; `artifactType` filterable + facetable |
| 5 | **spaarke-session-files** | Chat session uploads (transient) — ADR-014 strict isolation per session | `infrastructure/ai-search/spaarke-session-files.json` | `tenantId` + `sessionId` (strict pair-filter required on every query) | `FileIndexingService`, `PostUploadIndexingEnqueuer`; cleanup: `SessionFilesCleanupJob` | `RecallSessionFileHandler`, `RagService` T2, `R5SummarizeTelemetry` | Key field present; `tenantId` AND `sessionId` BOTH filterable (canonical invariant per `deploy-session-files-index.ps1` lines 209-215); `contentVector3072` + `documentVector3072` are 3072-dim HNSW cosine |
| 6 | **spaarke-invoices-index** *(renamed from `spaarke-invoices-dev` per FR-10)* | Invoice semantic search (Financial Intelligence MVP) | `infrastructure/ai-search/spaarke-invoices-index.json` (renamed from `invoice-index-schema.json` per FR-10) | `tenantId` + `invoiceId` + `matterId` + `projectId` | `InvoiceIndexingJobHandler` | `IInvoiceAi` facade, Financial Intelligence R1 endpoints | Key field present; index `name` field MUST be `spaarke-invoices-index` (NOT `spaarke-invoices-dev`); vector field is 3072-dim HNSW cosine; `tenantId` + scope IDs filterable |
| 7 | **spaarke-playbook-embeddings** *(renamed from `playbook-embeddings` per FR-10)* | Playbook dispatch vectors | `infrastructure/ai-search/spaarke-playbook-embeddings.json` (renamed from `playbook-embeddings.json` per FR-10) | global (NO `tenantId` — shared playbook catalog) | `Index-ExistingPlaybooks.ps1`, `PlaybookEmbeddingService`, `PlaybookIndexingBackgroundService` | `PlaybookDispatcher`, `Api/Ai/PlaybookEmbeddingEndpoints`, `PlaybookIndexDriftDetectionJob` | Key field present; index `name` field MUST be `spaarke-playbook-embeddings` (with `spaarke-` prefix); vector field is 3072-dim HNSW cosine; playbook ID + version filterable |

### Index restoration matrix (dev rebuild — `spaarke-ai-azure-setup-dev-r1` Phase 5)

| # | Index | Restoration strategy | Ingestion script | Notes |
|---|---|---|---|---|
| 1 | `spaarke-files-index` | Schema only | (deferred) | Re-ingest deferred per owner |
| 2 | `spaarke-records-index` | Schema + data | `Sync-RecordsToIndex.ps1` | New `tenantId` field populated |
| 3 | `spaarke-rag-references` | Schema + data | `Index-AllReferences.ps1` | All KNW-*.md golden refs |
| 4 | `spaarke-insights-index` | Schema + Precedent data | `PrecedentProjectionSync`; Observations re-projected | Observation history loss accepted; re-project from event history |
| 5 | `spaarke-session-files` | Schema only | n/a | Sessions are transient by design |
| 6 | `spaarke-invoices-index` | Schema only | (deferred) | Was empty pre-deletion; MVP not in active testing |
| 7 | `spaarke-playbook-embeddings` | Schema + data | `Index-ExistingPlaybooks.ps1` | Playbook catalog |

### Deploy procedure

Indexes are deployed via the **single canonical deployer** `scripts/ai-search/Deploy-AllIndexes.ps1` (FR-07). The script is catalog-driven, supports `-DryRun` + `-VerifyOnly` + `-Indexes <subset>`, and runs the post-deploy invariant verifier per index that asserts the values in the **Post-deploy invariants** column above. Bicep deployment is retained for the Insights index only (`infra/insights/modules/search-index.bicep`); the other 6 indexes are PowerShell-deployed.

Full operator procedure: [`docs/guides/ai-search-azure-setup.md`](../guides/ai-search-azure-setup.md) (task 003 of this project).

---

## 5. Retired Indexes Appendix

These 5 indexes were deployed historically; they are **retired** as of this catalog publication. Do not recreate them. Any code, script, or doc still referencing them as live targets is a defect (the comprehensive FR-13 grep audit covers this scope across BFF + frontend + `.claude/` paths).

| Name | Last seen | Why retired | Replacement |
|---|---|---|---|
| `spaarke-knowledge-index-v2` | Deleted 2026-06-25 | Owner: "moved on from v2"; functionality moved to D-P7 universal ingest via `spaarke-files-index`. Five-generation drift created config + code + schema mismatch. | `spaarke-files-index` |
| `discovery-index` | Deleted 2026-06-25 | Provisioned by AIPL-016 but never wired into runtime; no live writer or query path found in the 2026-06-25 audit. Schema cost without runtime benefit. | (none — was never used in production code) |
| `spaarke-knowledge-shared` | Live in `appsettings.json:122` until FR-14; planned retirement | R3 task 002 was supposed to remove this reference; the C# default in `AnalysisOptions.cs:69` had moved to v2, but the appsettings override survived. FR-14 removes the dangling reference. | `spaarke-files-index` |
| `spaarke-knowledge-index` (v1) | Deleted pre-2026-06-25 (PPI-036) | Superseded by `spaarke-knowledge-index-v2` (which is now itself superseded). | `spaarke-files-index` |
| `knowledge-index` | Only ever in `infrastructure/ai-search/_archive/`; never deployed | Early AIPL-016 design draft; never made it to production. | (none) |

### Why these stay retired (NFR-05 + binding MUST rule)

The MUST rules in `spec.md` explicitly forbid restoring any retired index. The retired-index appendix is the canonical reference for code-review gates and the `Deploy-AllIndexes.ps1` post-deploy verifier — neither will accept a deploy that creates one of these names.

### How to verify

```bash
# Grep across BFF code, frontend code, and .claude/ — should return zero hits as live values
# (.claude/FAILURE-MODES.md AP-2 historical reference is the only acceptable hit)
grep -r "spaarke-knowledge-index-v2\|discovery-index\|spaarke-knowledge-shared" src/ .claude/

# Index list query against any environment — should return exactly the 7 active names from §4
az search indexes list -g spe-infrastructure-westus2 --service-name spaarke-search-dev --query "[].name" -o tsv
```

---

## 6. Cross-Links

This catalog is the canonical inventory. Related documents that consume or are consumed by it:

### Operational

- [`docs/guides/ai-search-azure-setup.md`](../guides/ai-search-azure-setup.md) — Operator runbook for setting up AI Search in any environment (task 003 of this project). Step-by-step procedure: provision service → set KV secret → run `Deploy-AllIndexes.ps1` → verify.
- [`docs/guides/AI-EMBEDDING-STRATEGY.md`](../guides/AI-EMBEDDING-STRATEGY.md) — Embedding-model and chunking strategy. Lines 63–69 (stale index inventory) are replaced per FR-04 by a one-line reference back to this catalog.
- [`docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md`](../guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md) — Multi-container BU operator runbook. BU value table at lines 67–68 is updated per FR-04 to use canonical names from this catalog.
- [`docs/guides/RAG-CONFIGURATION.md`](../guides/RAG-CONFIGURATION.md) — RAG configuration and tuning knobs. Index references should resolve against this catalog.

### Architecture

- [`docs/architecture/AI-ARCHITECTURE.md`](AI-ARCHITECTURE.md) — Updated by FR-03 / task 004 with the per-index consumer map (single greppable table); links here for canonical index definitions.
- [`docs/architecture/rag-architecture.md`](rag-architecture.md) — RAG architecture; updated by FR-04 / task 005 to replace the dual-index narrative with the current 7-index landscape and cross-link here.
- [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](INSIGHTS-ENGINE-ARCHITECTURE.md) — Insights engine architecture; consumer of `spaarke-insights-index` (catalog entry #4). Bicep deployment path retained for this index per FR-11 + Resolved Question.

### Project context

- [`projects/spaarke-ai-azure-setup-dev-r1/spec.md`](../../projects/spaarke-ai-azure-setup-dev-r1/spec.md) — FR-01 acceptance criteria; this catalog is the FR-01 deliverable.
- [`projects/spaarke-ai-azure-setup-dev-r1/design.md`](../../projects/spaarke-ai-azure-setup-dev-r1/design.md) — Design rationale + §Active Index Catalog seed table + §Schema Property Policy seed + §Retired Indexes seed.

---

*Catalog v1.0 — created 2026-06-26 (`spaarke-ai-azure-setup-dev-r1` task 002 per FR-01). Updated atomically when indexes are added, retired, or renamed; renames follow NFR-07 atomic-PR discipline.*
