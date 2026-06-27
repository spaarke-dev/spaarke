# Project Plan: Spaarke AI Search Azure Setup (Dev Restoration + Canonicalization)

> **Last Updated**: 2026-06-26
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)
> **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Restore `spaarke-search-dev` (7 active indexes) AND formalize AI Search provisioning into a single canonical doc + unified deploy script + property policy + naming convention, reusable for any future environment setup.

**Scope** (key deliverables):
- 3 new/updated canonical docs (`AI-SEARCH-INDEX-CATALOG.md`, `ai-search-azure-setup.md`, `AI-ARCHITECTURE.md`)
- 1 new unified deployer (`scripts/ai-search/Deploy-AllIndexes.ps1`) that retires 5 per-index scripts
- 7 schemas patched with property policy + consolidated to single location + 3 atomic renames
- `tenantId` field added to records-index + writer/reader updates
- BFF code refactor across ~20 files (knowledge-v2 → files-index) + appsettings cleanup + embedding-model alignment
- Dev BFF app-settings migration to Key Vault references
- Mandatory test-fixture sweep alongside DI changes (NFR-14)
- 7 schemas deployed to `spaarke-search-dev`; ingestible indexes populated; dev BFF functional verification

**Timeline**: ~3-5 days | **Estimated Effort**: 60-80 hours across 5 phases

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-013** — AI services bounded concurrency. Embedding generation in ingestion scripts MUST honor `SemaphoreSlim` patterns (max 16 indexing, max 5 search)
- **ADR-017** — Background jobs (Service Bus pattern). `RagIndexingJobHandler` + `BulkRagIndexingJobHandler` refactors MUST preserve job contract
- **ADR-028** — Auth v2 canonical. Dev BFF KV-reference migration MUST follow `@Microsoft.KeyVault(...)` syntax + managed-identity resolution
- **ADR-014** *(currently misattributed: caching)*. Inline-cited for tenant-isolation principle. FR-06 fixes citation drift
- **ADR-004** *(currently misattributed: job contract)*. Inline-cited for idempotent re-indexing principle. FR-06 fixes citation drift
- **ADR-032** — Null-Object kill-switch pattern (per Redis handoff §7 — applies to any new feature-gated AI-Search service)

**From Spec**:
- **NFR-01** — `Deploy-AllIndexes.ps1` MUST be idempotent
- **NFR-04** — BFF code changes net publish-size delta MUST be ≤ 0 MB (refactor only — no new packages or services)
- **NFR-05** — Prod and demo MUST remain unchanged; no commands target `spaarke-search-prod` or `spaarke-search-demo`
- **NFR-07** — Schema renames MUST be coordinated atomically per rename (one PR per rename)
- **NFR-11** — All 7 schemas use `text-embedding-3-large` (3072 dim); BFF appsettings MUST match
- **NFR-13** — Phase 3 BLOCKED until `spaarke-redis-cache-remediation-r1` Phase 3 complete (✅ DELIVERED 2026-06-26)
- **NFR-14** — Test-fixture sweep MANDATORY in same PR as production DI changes (Redis project lesson: 337 fixture failures from analogous DI tightening)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Two-tier naming: top-level env-suffixed, sub-resources env-agnostic | Build-once-deploy-anywhere; DNS uniqueness only at top level | Same canonical index name in dev/prod/demo |
| Single unified deployer; retire 5 per-index PS scripts | One source of truth; mirrors Redis project's validated Bicep+PS pattern | All 5 pre-existing per-index scripts deleted in FR-07's PR |
| `infrastructure/ai-search/` is the single consolidated schema location | More mature dir, more files already | Move `infra/ai-search/*` + `infra/insights/schemas/*` into it |
| `text-embedding-3-large` (3072 dim) canonical | Schemas already use 3072; appsettings drift is the bug | FR-20 aligns appsettings to schema |
| Canonical KV = `spaarke-spekvcert` (NOT `sprkspaarkedev-aif-kv`) | Spec Assumption #5 was wrong; confirmed via Redis project handoff | All FR-15 KV refs target `spaarke-spekvcert` |

### Discovered Resources

**Applicable ADRs**:
- `.claude/adr/ADR-013-ai-bounded-concurrency.md` — embedding generation concurrency
- `.claude/adr/ADR-017-background-jobs-service-bus.md` — job contract preservation
- `.claude/adr/ADR-028-spaarke-auth-architecture.md` — KV-reference syntax + MI resolution
- `.claude/adr/ADR-014-*.md` — needs rename or new ADR for inline-cited principles (FR-06)
- `.claude/adr/ADR-004-*.md` — needs rename or new ADR for inline-cited principles (FR-06)
- `.claude/adr/ADR-032-bff-nullobject-kill-switch.md` — Null-Object pattern (Redis handoff §7)
- `.claude/adr/ADR-009-caching-policy.md` — Redis amendment context (post-Redis project)

**Applicable Skills**:
- `.claude/skills/task-execute/SKILL.md` — mandatory task execution protocol
- `.claude/skills/adr-aware/SKILL.md` — automatic ADR loading per resource type
- `.claude/skills/adr-check/SKILL.md` — pre-commit ADR validation
- `.claude/skills/code-review/SKILL.md` — Step 9.5 quality gate for FULL-rigor tasks
- `.claude/skills/azure-deploy/SKILL.md` — Azure infrastructure deployment helpers
- `.claude/skills/bff-deploy/SKILL.md` — BFF App Service deployment (Phase 5 functional verification)
- `.claude/skills/dataverse-mcp-usage/SKILL.md` — Dataverse MCP for record ingestion validation
- `.claude/skills/script-aware/SKILL.md` — discover existing scripts before writing new code
- `.claude/skills/push-to-github/SKILL.md` — atomic-rename PRs per NFR-07
- `.claude/skills/merge-to-master/SKILL.md` — post-PR merge workflow

**Knowledge Articles + Architecture Docs**:
- `docs/architecture/AI-ARCHITECTURE.md` — update target (consumer map per FR-03)
- `docs/architecture/rag-architecture.md` — update target (FR-04, stale dual-index narrative)
- `docs/architecture/auth-azure-resources.md` — KV + MI + role reference
- `docs/guides/auth-deployment-setup.md` — KV-reference deployment pattern (ADR-028)
- `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` — update target (§4.6 + Appendix D per FR-05)
- `docs/guides/AI-EMBEDDING-STRATEGY.md` — update target (FR-04, lines 63–69)
- `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` — update target (FR-04, BU table)
- `docs/notes/rag-indexing-configuration.md` — RETIRE per FR-04
- `docs/standards/INTEGRATION-CONTRACTS.md` — BFF surface contract reference

**Patterns**:
- `.claude/patterns/ai/indexing-pipeline.md` — UPDATE per FR-13 (references `spaarke-knowledge-index-v2`)
- `.claude/patterns/auth/spaarke-sso-binding.md` — canonical KV-ref binding (ADR-028)

**Reusable Code / Reference Scripts**:
- `scripts/Deploy-RedisCache.ps1` — **canonical PS+Bicep deployer template to mirror** (Redis handoff §6)
- `infrastructure/ai-search/deploy-session-files-index.ps1` — structural template per FR-07 (and replaces it)
- `infrastructure/byok/main.bicep:443-454` — BFF MI `Search Index Data Contributor` role assignment (FR-21 #2 re-grant source)
- `infra/insights/modules/search-index.bicep` — Insights index Bicep deployer (retained per Open Question resolution; `loadJsonContent()` path update per FR-11)
- `infrastructure/bicep/customer.json:92-100` — Service Bus queue definitions (FR-21 #3 verification source)
- `scripts/ai-search/Add-ReferenceToIndex.ps1:456` — `domain` → `documentType` rename target per FR-17
- `infrastructure/ai-search/spaarke-rag-references.json:39,151` — schema field rename target per FR-17

**Out-of-Scope Reference (DO NOT MODIFY)**:
- `scripts/Migrate-SprkSearchIndexedSchema.ps1` — NOT AI Search related (despite name); excluded from scope

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Documentation Foundation + Pre-Flight Verification (Tasks 001-009)
├─ Catalog + operational guide + AI-ARCHITECTURE update
├─ Stale-doc cleanup + ADR pointer drift fix
└─ Pre-Phase-3 operational verification (10 checks)

Phase 2: Schema Preparation (Tasks 010-019)
├─ Property policy patches across 7 schemas
├─ 3 atomic renames (files-index, playbook-embeddings, invoices-index)
├─ Schema-file consolidation to infrastructure/ai-search/
├─ tenantId field on records-index
└─ spaarke-rag-references field-name bug fix (FR-17, Phase 2 schema patch)

Phase 3: Deploy Infrastructure (Tasks 020-029)
├─ Pre-Phase-3 gate verification (NFR-13: 5 Redis prereqs + FR-21: 5 AI-Search prereqs)
├─ Write Deploy-AllIndexes.ps1 (mirrors Deploy-RedisCache.ps1)
└─ Post-deploy invariant verifier per index

Phase 4: Code + Config Refactor (Tasks 030-049)
├─ BFF code refactor (~20 files, knowledge-v2 → files-index)
├─ AiSearchOptions.DiscoveryIndexName property REMOVAL
├─ appsettings + templates cleanup
├─ Dev BFF KV-reference migration
├─ records-index writer/reader tenantId
├─ Invoice + playbook embeddings handler renames
├─ Embedding model alignment (FR-20)
└─ MANDATORY test-fixture sweep (NFR-14)

Phase 5: Deploy + Validate (Tasks 050-059)
├─ Deploy 7 schemas via Deploy-AllIndexes.ps1
├─ Verify FR-17 fix (golden-reference roundtrip)
├─ Data ingestion (records, rag-references, playbook-embeddings, insights)
└─ Dev BFF functional verification (healthz + 4 AI endpoints)

Wrap-up (Task 090)
└─ Code-review + adr-check + repo-cleanup + README update
```

### Critical Path

**Blocking Dependencies**:
- Phase 2 BLOCKED BY Phase 1 (catalog drives schema work; FR-21 pre-flight blocks deploy)
- Phase 3 BLOCKED BY Phase 2 (schemas must be patched before deploy)
- Phase 3 BLOCKED BY `spaarke-redis-cache-remediation-r1` Phase 3 (✅ DELIVERED 2026-06-26)
- Phase 4 BLOCKED BY Phase 2 (schema names must be finalized before code refactor)
- Phase 5 BLOCKED BY Phase 3 + Phase 4 (need deployed indexes + refactored BFF for functional verify)
- Phase 5 FR-18 BLOCKED BY FR-20 (embeddings will fail upsert without 3072-dim alignment)

**High-Risk Items**:
- **FR-13 (Phase 4)** — 20+ file BFF refactor with DI implications → Mitigation: NFR-14 test-fixture sweep in same PR
- **FR-17 (Phase 2)** — bug fix with PowerShell + C# coordination → Mitigation: Phase 5 golden-reference roundtrip validation
- **FR-20 (Phase 4)** — embedding model alignment → Mitigation: FR-21 #4 verifies deployment exists before code change
- **FR-21 (Phase 1)** — 10 prereq checks; failures block Phase 3 → Mitigation: evidence captured in `notes/pre-phase-3-verification.md`

---

## 4. Phase Breakdown

### Phase 1: Documentation Foundation + Pre-Flight Verification (Tasks 001-009)

**Rigor Level**: STANDARD (mostly docs + verification; no code)

**Objectives**:
1. Publish 3 canonical docs as single source of truth
2. Clean up 4 stale docs that reference retired indexes as active
3. Resolve ADR pointer drift (ADR-014, ADR-004)
4. Append `Deploy-AllIndexes.ps1` invocation to `SPAARKE-DEPLOYMENT-GUIDE.md`
5. Complete 10 pre-Phase-3 operational verification checks with evidence

**Deliverables**:
- [ ] `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` created (FR-01)
- [ ] `docs/guides/ai-search-azure-setup.md` created (FR-02)
- [ ] `docs/architecture/AI-ARCHITECTURE.md` consumer map added (FR-03)
- [ ] 4 stale docs updated; `rag-indexing-configuration.md` retired (FR-04)
- [ ] `SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 + Appendix D updated (FR-05)
- [ ] ADR-014 + ADR-004 inline-citation drift resolved (FR-06)
- [ ] `notes/pre-phase-3-verification.md` with 10 checks evidence (FR-21)

**Discovered Resources**:
- `docs/architecture/AI-ARCHITECTURE.md`, `docs/architecture/rag-architecture.md`, `docs/guides/AI-EMBEDDING-STRATEGY.md`, `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md`, `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md`
- `.claude/adr/ADR-014-*.md`, `.claude/adr/ADR-004-*.md`
- `.claude/skills/azure-deploy/SKILL.md` (FR-21 Azure CLI verification helpers)
- `infrastructure/byok/main.bicep:443-454` (FR-21 #2 RBAC re-grant source)
- `scripts/Deploy-RedisCache.ps1` (FR-21 NFR-13 Redis prereq verification helpers)

**Inputs**: spec.md, design.md, Redis handoff doc, existing BFF code grep evidence

**Outputs**: 3 new docs, 4 updated docs, 1 retired doc, 1 verification evidence file

### Phase 2: Schema Preparation (Tasks 010-019)

**Rigor Level**: STANDARD (JSON schema patches + 3 atomic renames + file moves; no compiled code)

**Objectives**:
1. Apply schema property policy to all 7 schemas (default-enable all allowed flags)
2. Execute 3 atomic renames (one PR per rename per NFR-07)
3. Consolidate schema files to single location `infrastructure/ai-search/`
4. Add `tenantId` field to `spaarke-records-index` schema
5. Fix `spaarke-rag-references` field-name bug (`domain` → `documentType`)

**Deliverables**:
- [ ] 7 schemas property-policy compliant (FR-09)
- [ ] 3 atomic renames coordinated across schema + code constants + scripts + BU values (FR-10)
- [ ] `infra/ai-search/*` + `infra/insights/schemas/*` moved into `infrastructure/ai-search/`; Bicep `loadJsonContent()` paths updated (FR-11)
- [ ] `tenantId` field on records-index schema (FR-12)
- [ ] `spaarke-rag-references` `domain` → `documentType` (schema + PS writer) (FR-17)

**Discovered Resources**:
- `infrastructure/ai-search/*.json` (7 schemas) + `infra/ai-search/spaarke-file-index.json` + `infra/insights/schemas/spaarke-insights-index.index.json`
- `infra/insights/modules/search-index.bicep` (loadJsonContent path update)
- `scripts/ai-search/Add-ReferenceToIndex.ps1:456` (FR-17 PS writer rename target)
- `infrastructure/ai-search/spaarke-rag-references.json:39,151` (FR-17 schema field rename target)
- `src/server/api/Sprk.Bff.Api/Configuration/AiSearchOptions.cs` (code constants for renames)

**Inputs**: Phase 1 catalog (drives schema work), existing 7 schemas

**Outputs**: 7 patched schemas, 3 renames merged, consolidated directory, FR-17 bug fix landed

### Phase 3: Deploy Infrastructure (Tasks 020-029)

**Rigor Level**: FULL (script implementation + Azure deploys)

**Prerequisites Gate**:
- `spaarke-redis-cache-remediation-r1` Phase 3 (✅ DELIVERED 2026-06-26; NFR-13)
- All 10 FR-21 pre-flight checks passing (5 Redis prereqs + 5 AI-Search prereqs)

**Objectives**:
1. Write `scripts/ai-search/Deploy-AllIndexes.ps1` mirroring `scripts/Deploy-RedisCache.ps1` (Bicep+PS hybrid; `SupportsShouldProcess`)
2. Retire 5 pre-existing per-index PS scripts in same PR
3. Implement post-deploy invariant verifier per index (vector dim, key field, required-filterable fields, schema policy compliance)

**Deliverables**:
- [ ] `scripts/ai-search/Deploy-AllIndexes.ps1` operational (FR-07): `-Environment`, `-Indexes`, `-DryRun`, `-VerifyOnly`, `-Force`, `-CutoverBffSettings`
- [ ] 5 pre-existing per-index scripts deleted in same PR (FR-07): `Deploy-IndexSchemas.ps1`, `Deploy-InvoiceSearchIndex.ps1`, `deploy-invoice-index.ps1`, `deploy-session-files-index.ps1`, `Create-PlaybookEmbeddingsIndex.ps1` (+ `infrastructure/ai-search/deploy-invoice-index.bicep` per 2026-06-26 audit)
- [ ] Post-deploy invariant verifier passes for each of 7 indexes

**Discovered Resources**:
- `scripts/Deploy-RedisCache.ps1` (canonical template per Redis handoff §6)
- `infrastructure/ai-search/deploy-session-files-index.ps1` (structural template per FR-07 — and replaces it)
- `infra/insights/modules/search-index.bicep` (retained per Open Question resolution)
- `infrastructure/bicep/parameters/` (env-typed bicepparam pattern per Redis handoff §5)
- `.claude/skills/azure-deploy/SKILL.md` + `.claude/skills/script-aware/SKILL.md`

**Inputs**: Phase 1 catalog (drives `-Indexes` choices), Phase 2 patched schemas

**Outputs**: 1 unified deployer script, 5+1 retired scripts, working `-DryRun` + `-VerifyOnly`

### Phase 4: Code + Config Refactor (Tasks 030-049)

**Rigor Level**: FULL (code implementation across ~20 files; DI changes)

**Objectives**:
1. BFF code refactor: knowledge-v2 → files-index (FR-13, 20+ files including consumer services + appsettings templates + 4 BFF doc-comments + 3 frontend doc-comments + 3 `.claude/` doc updates)
2. Remove `AiSearchOptions.DiscoveryIndexName` property entirely (FR-14)
3. App settings + template cleanup (remove `spaarke-knowledge-shared`, `discovery-index`)
4. Dev BFF app settings → Key Vault references via `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=...)` form (FR-15)
5. Records-index writer/reader `tenantId` (FR-12 code side)
6. Invoice handler + service rename per FR-10 code side
7. Embedding model alignment to `text-embedding-3-large` (FR-20)
8. **MANDATORY test-fixture sweep alongside DI changes (NFR-14)**: integration fixtures, `WebApplicationFactory`-derived, `Mock<SearchIndexClient>` setups, `RecordSyncJobTests.cs:43` hardcoded endpoint replacement

**Deliverables**:
- [ ] 15+ BFF files refactored knowledge-v2 → files-index (FR-13)
- [ ] 2 `appsettings.*.json.template` env files updated (FR-13 + FR-14)
- [ ] 4 BFF doc-comment files updated (FR-13 cosmetic)
- [ ] 3 frontend doc-comment files updated (FR-13 cosmetic in `src/solutions/` + PCF + DocumentUploadWizard test)
- [ ] 3 `.claude/` doc updates (FR-13: `.claude/skills/add-reference-to-index/SKILL.md`, `.claude/patterns/ai/indexing-pipeline.md`) — main-session-only per sub-agent boundary
- [ ] `AiSearchOptions.DiscoveryIndexName` property REMOVED (FR-14)
- [ ] `appsettings.json` + `appsettings.template.json` cleaned (FR-14)
- [ ] Dev BFF AI-Search app settings migrated to KV references (FR-15)
- [ ] `DataverseIndexSyncService.cs` + `RecordSyncJob.cs` populate `tenantId` (FR-12)
- [ ] `RecordSearchAuthorizationFilter.cs:43` uses `tenantId` filter; workaround comment removed (FR-12)
- [ ] `InvoiceIndexingJobHandler.cs:40` + `InvoiceSearchService.cs:45` index name updated (FR-10)
- [ ] `PlaybookEmbeddingService.cs:46` index name updated (FR-10)
- [ ] `appsettings.template.json:248` `EmbeddingModelName` `text-embedding-3-small` → `text-embedding-3-large` (FR-20)
- [ ] Test fixtures swept per NFR-14 — zero `WebApplicationFactory` test failures from DI changes
- [ ] BFF publish-size delta ≤ 0 MB verified per CLAUDE.md §10 NFR-01

**Discovered Resources**:
- ~20 BFF source files (per FR-13 expanded scope)
- 2 appsettings env templates + 1 appsettings.json + 1 appsettings.tokens.md
- 3 frontend files: `src/client/pcf/SemanticSearchControl/.../SearchIndexResolver.ts:35`, `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx:1079`, `src/solutions/DocumentUploadWizard/src/components/searchIndexResolver.test.ts`
- 1 SavedQuery XML: `src/solutions/spaarke_insights/Entities/sprk_Precedent/SavedQueries/{b637f6c8-...}.xml:48`
- `.claude/patterns/ai/indexing-pipeline.md` + `.claude/skills/add-reference-to-index/SKILL.md`
- `tests/integration/Spe.Integration.Tests/IntegrationTestFixture.cs`
- `tests/integration/Sprk.Bff.Api.IntegrationTests/CustomWebAppFactory.cs`
- `tests/unit/Sprk.Bff.Api.Tests/.../RecordSyncJobTests.cs:43`
- `.claude/constraints/bff-extensions.md` (binding §F.2 Fixture-Config-FIRST)
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` (BFF-vs-Xrm.WebApi context)

**Inputs**: Phase 2 schema names finalized, FR-21 #4 deployment verified

**Outputs**: ~20 BFF files updated, 2 template files updated, ~7 client/solution files updated, 3 `.claude/` doc updates, dev BFF KV-ref migration, test fixtures swept

### Phase 5: Deploy + Validate (Tasks 050-059)

**Rigor Level**: FULL (Azure deploys + data ingestion + functional verification)

**Objectives**:
1. Deploy 7 schemas to `spaarke-search-dev` via `Deploy-AllIndexes.ps1`
2. Verify `spaarke-rag-references` field-name fix landed (golden-reference roundtrip)
3. Ingest data for ingestible indexes
4. Verify dev BFF functional (healthz + 4 AI endpoints return real results)

**Deliverables**:
- [ ] 7 schemas deployed; `az search indexes list` shows 7 with canonical names (FR-16)
- [ ] Golden-reference roundtrip: write via `Add-ReferenceToIndex.ps1`, read via `ReferenceRetrievalService` returns document with `documentType` populated (FR-17 verification)
- [ ] Records-index populated (with `tenantId` set) (FR-18)
- [ ] rag-references populated (FR-18)
- [ ] playbook-embeddings populated (FR-18)
- [ ] insights Precedents populated + Observations re-projected (FR-18)
- [ ] files-index, session-files, invoices-index = schema-only (deferred per FR-18)
- [ ] Dev BFF endpoints return real results (FR-19): `/healthz`, `/api/ai/search`, `/api/ai/rag/query`, `/api/ai/insights/search`, playbook dispatch

**Discovered Resources**:
- Phase 3 `Deploy-AllIndexes.ps1`
- Phase 2 patched schemas
- `scripts/ai-search/Sync-RecordsToIndex.ps1` (records ingestion)
- `scripts/ai-search/Index-AllReferences.ps1` (rag-references ingestion)
- `scripts/Index-ExistingPlaybooks.ps1` (playbook-embeddings ingestion)
- `scripts/ai-search/Add-ReferenceToIndex.ps1` + `ReferenceRetrievalService` (FR-17 roundtrip)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/PrecedentProjectionSync.cs` (Precedents)
- `.claude/skills/bff-deploy/SKILL.md` (BFF deploy for Phase 5 functional verify)
- `.claude/skills/dataverse-mcp-usage/SKILL.md` (ingestion result validation via MCP)

**Inputs**: Phase 3 unified deployer, Phase 4 refactored BFF (deployed), source data intact

**Outputs**: 7 deployed indexes, populated indexes, dev BFF functional verification evidence

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| `spaarke-search-dev` Azure AI Search (Standard) | Provisioned 2026-06-25 (empty) | Low | FR-21 #5 empirical verification |
| `spaarke-spekvcert` Key Vault | Active | Low | BFF MI `Key Vault Secrets User` role confirmed |
| Azure OpenAI `text-embedding-3-large` deployment | Verify (FR-21 #4) | Medium | Pre-condition for FR-20 + FR-18 |
| Azure CLI logged in with required permissions | User maintains | Low | User session prerequisite |
| Service Bus `sdap-jobs` + `sdap-communication` queues | Verify (FR-21 #3) | Low | Recreate via Bicep if missing |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `spaarke-redis-cache-remediation-r1` Phase 3 cutover | PR #458 (master `567b98112`) | ✅ DELIVERED 2026-06-26 |
| BFF MI `Search Index Data Contributor` role | `infrastructure/byok/main.bicep:443-454` | Re-grant via FR-21 #2 |
| KNW-*.md golden references | Repo + Dataverse | Source data ready |
| Dataverse playbooks | Dataverse | Source data ready |
| Insights event history | Dataverse | Source data ready (re-project per Q5) |

---

## 6. Testing Strategy

**Unit Tests**:
- BFF refactor (Phase 4): existing unit-test suite MUST pass with index-name updates
- `RecordSyncJobTests.cs:43` hardcoded endpoint replaced with test fake (NFR-14)
- `Mock<SearchIndexClient>` / `Mock<SearchClient>` setups updated where index names referenced

**Integration Tests** (NFR-14 — preventive sweep):
- `tests/integration/Spe.Integration.Tests/IntegrationTestFixture.cs` — in-memory config supplies fake AI-Search endpoint + key
- `tests/integration/Sprk.Bff.Api.IntegrationTests/CustomWebAppFactory.cs` — `SearchIndexClient` removed + mock-replaced
- All `WebApplicationFactory`-derived fixtures verified — no test relies on resolved KV references at construction time

**Deployment Validation**:
- `Deploy-AllIndexes.ps1 -DryRun` — shows all 7 would deploy without changes
- `Deploy-AllIndexes.ps1 -VerifyOnly` — runs post-deploy invariant verifier against existing indexes
- Post-deploy invariant per index: vector dim 3072, key field configured, required-filterable fields present, schema policy compliance

**Functional Verification** (Phase 5 FR-19):
- `GET /healthz` returns Healthy
- `POST /api/ai/search` with `scope=entity` returns records
- `POST /api/ai/rag/query` returns chunks
- `POST /api/ai/insights/search` returns insights
- Playbook dispatch routes correctly

**Roundtrip Validation** (FR-17):
- Write reference via `Add-ReferenceToIndex.ps1`
- Read via `ReferenceRetrievalService`
- Verify returned document has `documentType` populated (NOT `domain`)

---

## 7. Acceptance Criteria

### Phase 1 (Documentation Foundation + Pre-Flight)
- [ ] `AI-SEARCH-INDEX-CATALOG.md` exists with all required sections (naming convention, schema property policy, vector config, per-index canonical table, retired indexes appendix, cross-links)
- [ ] `ai-search-azure-setup.md` is operator-ready: prerequisites, step-by-step, troubleshooting, post-deploy verification, env-specific variables
- [ ] `AI-ARCHITECTURE.md` has single greppable consumer-map table for 7 indexes; links to catalog; does NOT duplicate catalog content
- [ ] 4 stale docs updated; `rag-indexing-configuration.md` retired
- [ ] `SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 + Appendix D updated
- [ ] ADR-014 + ADR-004 inline-citation drift resolved
- [ ] `notes/pre-phase-3-verification.md` has evidence for all 10 checks (5 Redis prereqs + 5 AI-Search prereqs); all pass

### Phase 2 (Schema Preparation)
- [ ] Every scalar field in 7 schemas has `filterable=true sortable=true facetable=true retrievable=true` unless Azure-restricted or documented override comment
- [ ] 3 atomic renames merged (one PR per rename touches schema + code + script + BU values)
- [ ] All schema files in `infrastructure/ai-search/`; Bicep `loadJsonContent()` paths updated
- [ ] `spaarke-records-index` has `tenantId` field (`Edm.String`, `filterable=true sortable=true facetable=true retrievable=true searchable=false`)
- [ ] `spaarke-rag-references` schema field `domain` → `documentType` (line 39 + semantic config line 151)
- [ ] `Add-ReferenceToIndex.ps1:456` uses `documentType` (was `domain`)

### Phase 3 (Deploy Infrastructure)
- [ ] `Deploy-AllIndexes.ps1` catalog-driven, `SupportsShouldProcess`, supports `-Environment`, `-Indexes`, `-DryRun`, `-VerifyOnly`, `-Force` (prod/demo per NFR-05), `-CutoverBffSettings`
- [ ] 5 pre-existing per-index PS scripts + `deploy-invoice-index.bicep` deleted in same PR
- [ ] Post-deploy invariant verifier returns non-zero on any failure
- [ ] Total runtime for full 7-index deploy < 30 min (NFR-12 target)

### Phase 4 (Code + Config Refactor)
- [ ] `grep -r "spaarke-knowledge-index-v2" src/ .claude/` returns zero matches as live values (excluding `.claude/archive/` and `.claude/FAILURE-MODES.md:148` historical)
- [ ] `grep -r "spaarke-knowledge-shared\|discovery-index" src/` returns zero matches as live values
- [ ] `AiSearchOptions.DiscoveryIndexName` property no longer exists in source
- [ ] `az webapp config appsettings list --name spaarke-bff-dev` shows no `https://*.search.windows.net` or raw keys
- [ ] All AI-Search settings in dev BFF use `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=...)` form
- [ ] `appsettings.template.json:248` `EmbeddingModelName=text-embedding-3-large`
- [ ] All `WebApplicationFactory`-derived tests pass without resolved KV references at construction time
- [ ] BFF publish-size delta ≤ 0 MB (CLAUDE.md §10 NFR-01)

### Phase 5 (Deploy + Validate)
- [ ] `az search indexes list -g spe-infrastructure-westus2 --service-name spaarke-search-dev` shows 7 canonical-named indexes
- [ ] Golden-reference roundtrip returns document with `documentType` populated
- [ ] Each ingestion script reports non-zero documents indexed (except schema-only indexes)
- [ ] Spot-check queries return expected matches
- [ ] Dev BFF `/healthz` + 4 AI endpoints return real (non-error, non-empty where data exists) results

### Wrap-up (Task 090)
- [ ] `/code-review` + `/adr-check` run; critical issues addressed
- [ ] `/repo-cleanup projects/spaarke-ai-azure-setup-dev-r1` audited; ephemeral files removed
- [ ] README.md updated: Status=Complete, Phase=Complete, Progress=100%, all graduation criteria checked
- [ ] `notes/lessons-learned.md` created if notable insights

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Test fixtures fail with KV-ref DI tightening (Redis hit 337 failures) | High | High | NFR-14 binding: fixture sweep in same PR as DI changes; §F.2 Fixture-Config-FIRST |
| R2 | `text-embedding-3-large` deployment missing in dev Azure OpenAI | Low | High | FR-21 #4 verifies before FR-20 + FR-18 |
| R3 | BFF MI role lost on recreated `spaarke-search-dev` | High | High | FR-21 #2 re-runs `infrastructure/byok/main.bicep` |
| R4 | KV admin-key drift between Azure and `spaarke-spekvcert/AiSearch--AdminKey` | Medium | High | FR-21 #1 freshness check + atomic rotation |
| R5 | Service Bus queues missing (job handlers enqueue but never dequeue) | Low | High | FR-21 #3 verification + Bicep recreate if missing |
| R6 | `spaarke-rag-references` PS-indexed docs already exist with old `domain` field | High | Medium | Phase 5 FR-17 reindex pre-existing docs that only have `domain` |
| R7 | Schema rename PR conflicts with concurrent work | Medium | Medium | NFR-07 atomic rename PRs; verify no concurrent work via `git fetch` before opening |
| R8 | `infra/insights/modules/search-index.bicep` deploys after Phase 3 with stale path | Low | Medium | FR-11 updates `loadJsonContent()` path atomically with file move |

---

## 9. Next Steps

1. **Review this plan.md** for accuracy against spec.md
2. **Execute task 001** via task-execute skill (start with Pre-Phase-3 Verification FR-21)
3. **Phase 1 parallel wave**: docs (002-008) can run concurrently after 001 evidence captured
4. **Sequential gate**: Phase 2 starts after Phase 1 complete
5. **Sequential gate**: Phase 3 starts after Phase 2 + Redis prereqs (✅ DELIVERED 2026-06-26)
6. **Parallel-after-gate**: Phase 4 can run concurrently with Phase 3 (no shared files)
7. **Integration gate**: Phase 5 starts after Phase 3 + Phase 4 complete
8. **Wrap-up**: Task 090 after all phase tasks completed

---

## 10. References

### Spec + Design
- [`spec.md`](spec.md) — authoritative spec (21 FRs, 14 NFRs)
- [`design.md`](design.md) — design rationale + 5-phase plan
- `projects/spaarke-redis-cache-remediation-r1/notes/handoff-to-ai-search-project.md` — canonical KV name + Bicep+PS pattern + Lessons #1-5

### ADRs Loaded This Project
- `.claude/adr/ADR-013-ai-bounded-concurrency.md`
- `.claude/adr/ADR-017-background-jobs-service-bus.md`
- `.claude/adr/ADR-028-spaarke-auth-architecture.md`
- `.claude/adr/ADR-014-*.md` (rename/correction target per FR-06)
- `.claude/adr/ADR-004-*.md` (rename/correction target per FR-06)
- `.claude/adr/ADR-032-bff-nullobject-kill-switch.md`
- `.claude/adr/ADR-009-caching-policy.md`

### Skills + Constraints
- `.claude/skills/task-execute/SKILL.md` (mandatory)
- `.claude/skills/adr-aware/SKILL.md`, `.claude/skills/adr-check/SKILL.md`, `.claude/skills/code-review/SKILL.md`
- `.claude/skills/azure-deploy/SKILL.md`, `.claude/skills/bff-deploy/SKILL.md`
- `.claude/skills/script-aware/SKILL.md`, `.claude/skills/dataverse-mcp-usage/SKILL.md`
- `.claude/skills/push-to-github/SKILL.md`, `.claude/skills/merge-to-master/SKILL.md`
- `.claude/constraints/bff-extensions.md` (§F.2 binding for NFR-14)

### Architecture + Standards
- `docs/architecture/AI-ARCHITECTURE.md` (update target)
- `docs/architecture/rag-architecture.md` (update target)
- `docs/architecture/auth-azure-resources.md`
- `docs/standards/INTEGRATION-CONTRACTS.md`
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`

### Guides
- `docs/guides/auth-deployment-setup.md` (ADR-028 KV pattern)
- `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` (update target per FR-05)
- `docs/guides/AI-EMBEDDING-STRATEGY.md` (update target per FR-04)
- `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` (update target per FR-04)

### Patterns
- `.claude/patterns/ai/indexing-pipeline.md` (update target per FR-13)
- `.claude/patterns/auth/spaarke-sso-binding.md` (KV-ref binding)

### Reference Scripts + Code
- `scripts/Deploy-RedisCache.ps1` (canonical template to mirror)
- `infrastructure/ai-search/deploy-session-files-index.ps1` (structural template per FR-07)
- `infrastructure/byok/main.bicep:443-454` (BFF MI RBAC)
- `infra/insights/modules/search-index.bicep` (Insights Bicep deployer; FR-11 path update)
- `infrastructure/bicep/customer.json:92-100` (Service Bus queue defs)

---

**Status**: Ready for Tasks
**Next Action**: Execute task 001 via task-execute skill

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
