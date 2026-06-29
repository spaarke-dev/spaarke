# Spaarke AI Search Azure Setup (Dev Restoration + Canonicalization)

> **Portfolio**: [Issue #519](https://github.com/spaarke-dev/spaarke/issues/519) (Type=Project; Parent Epic #421 SPAARKE AI; Project Board #2 Spaarke Core)
>
> **Last Updated**: 2026-06-26
>
> **Status**: ✅ **COMPLETE**
>
> **Created**: 2026-06-25
> **Completed**: 2026-06-26
>
> **Project ID**: `spaarke-ai-azure-setup-dev-r1`

---

## Overview

Restores the accidentally-deleted `spaarke-search-dev` AI Search service (7 active indexes) and uses the rebuild as the opportunity to formalize the AI Search provisioning process into a single canonical doc + unified deploy script + property policy + naming convention, reusable for any future environment setup. Scoped to dev only — prod/demo remain intentionally cost-reduced. **Prerequisite project `spaarke-redis-cache-remediation-r1` is DELIVERED** (PR #458 merged to master `567b98112` on 2026-06-26).

## Quick Links

| Document | Description |
|----------|-------------|
| [spec.md](./spec.md) | Authoritative spec — 21 FRs, 14 NFRs, 5 phases |
| [design.md](./design.md) | Design rationale, resource inventory, 5-phase plan |
| [plan.md](./plan.md) | Implementation plan with phase breakdown + discovered resources |
| [CLAUDE.md](./CLAUDE.md) | AI context file — mandatory task-execute protocol, ADRs, constraints |
| [current-task.md](./current-task.md) | Active task state tracker (for context recovery) |
| [tasks/TASK-INDEX.md](./tasks/TASK-INDEX.md) | Task registry + parallel-execution groups |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Complete |
| **Progress** | **100%** (30 of 30 tasks ✅) |
| **Target Date** | 2026-06-26 |
| **Completed Date** | **2026-06-26** |
| **Owner** | ralph.schroeder@hotmail.com |
| **Branch** | `work/spaarke-ai-azure-setup-dev-r1` (28+ commits ahead of master) |
| **Final commit** | `76b84f385` (Phase 5 complete) |

## Problem Statement

During a 2026-06-25 cost-reduction operation (target: demo + prod), `spaarke-search-dev` (Standard tier AI Search service) was deleted then recreated empty. `spaarke-bff-dev` is Running on P1v3 but search/RAG endpoints return zero results.

The recovery planning surfaced **9 structural gaps that pre-date the incident**: no canonical index catalog, three-way naming drift on the files index, `Deploy-IndexSchemas.ps1` broken (wrong IndexMap; covers 3 of 7 indexes), schema files scattered across three locations, BFF code-vs-schema-vs-doc drift on 5 of 7 indexes, `spaarke-rag-references` writer/reader field-name mismatch (confirmed bug), and embedding-model name drift (`text-embedding-3-small` in appsettings vs `text-embedding-3-large` in schemas).

## Solution Summary

Restore 7 active indexes + formalize the provisioning process:

1. Author one canonical doc (`AI-SEARCH-INDEX-CATALOG.md`), one operational guide (`ai-search-azure-setup.md`), one consumer-map update to `AI-ARCHITECTURE.md`
2. Write ONE unified deployer (`scripts/ai-search/Deploy-AllIndexes.ps1`) mirroring the validated Bicep+PS pattern of `scripts/Deploy-RedisCache.ps1` — retires 5 pre-existing per-index PS scripts
3. Apply schema property policy + 3 atomic renames + schema-file consolidation + `tenantId` field on records-index + bug-fix on `spaarke-rag-references` field-name mismatch
4. BFF code refactor: 20+ files knowledge-v2 → files-index, including consumer services + appsettings templates + doc-comments; remove `AiSearchOptions.DiscoveryIndexName` entirely; align embedding model to `text-embedding-3-large`
5. Mandatory test-fixture sweep alongside DI changes (Redis project hit 337 test failures from this)
6. Migrate dev BFF app settings to Key Vault references via `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=...)` form
7. Deploy 7 schemas + ingest data + verify dev BFF functional

## Graduation Criteria — ALL PASSED ✅

- [x] All 3 canonical docs published: `AI-SEARCH-INDEX-CATALOG.md`, `ai-search-azure-setup.md`, updated `AI-ARCHITECTURE.md` (tasks 002, 003, 004)
- [x] `Deploy-AllIndexes.ps1` operational: idempotent, `-DryRun` + `-VerifyOnly` work, post-deploy verifier asserts policy per index (tasks 020, 021)
- [x] **All 8 active indexes** deployed to `spaarke-search-dev` with canonical names + correct property policy + 3072-dim vectors (was 7; expanded per FR-14 reframe — discovery-index reactivated as `spaarke-discovery-index`) (task 050)
- [x] `spaarke-records-index` has `tenantId` field populated by ingestion; reader removes workaround comment (tasks 015 schema, 036 code, 052 ingestion → 67 docs visible via tenantId filter)
- [x] `grep -r` for retired index names in `src/` returns zero matches as live string values (task 046 — 9 grep checks all pass)
- [x] No hardcoded API keys or URLs in dev BFF app settings (migrated to Key Vault references) (task 041 — 7 settings; eliminated 2 plaintext stale keys; fixed truncated KV-ref parenthesis)
- [x] Dev BFF functional: `/healthz` Healthy + 5 AI endpoints registered (return 401 not 404) (task 054 — `/healthz` HTTP 200; `/api/ai/search`, `/api/ai/knowledge/test-search`, `/api/insights/search`, `/api/insights/ask`, KnowledgeBase group all routes wired)
- [x] `SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 added with `Deploy-AllIndexes.ps1` invocation; Appendix D updated (task 006)
- [x] `factory-r1` handoff documented: factory-r1 plan references this project's deliverables as prerequisite (this project's spec.md + `lessons-learned.md` § Handoff section list 6 deliverables for factory-r1 consumption)
- [x] Stale doc cleanups completed per FR-04 (task 005)
- [x] ADR pointer drift resolved per FR-06 (task 007)
- [x] BFF publish-size delta ≤ +5 MB single-task threshold per CLAUDE.md §10 (task 045 — 46.33 MB, +0.68 MB vs baseline; project contribution ≤ 0 since refactor + 6 scripts deleted; +0.68 attributable to elapsed-time cumulative growth from concurrent master commits)
- [x] `spaarke-rag-references` field-name bug fix verified (golden-reference roundtrip) (tasks 016 + 051 — REST query `$filter=documentType eq 'legal'` returns hits; PS writer + C# reader contract aligned)
- [x] Embedding model `text-embedding-3-large` (3072 dim) alignment verified (FR-20) (tasks 038 appsettings + 052 ingestion — rag-references chunks confirmed 3072-dim; playbooks confirmed 3072-dim)

## Scope

### In Scope

- Restoration of 7 active AI Search indexes to `spaarke-search-dev` (dev only)
- New canonical architecture doc + operational guide
- New unified deploy script `scripts/ai-search/Deploy-AllIndexes.ps1`
- Schema property policy patches across 7 schemas
- 3 atomic schema renames coordinated across schema + code + script + BU values
- Schema-file consolidation to single location `infrastructure/ai-search/`
- `tenantId` field on `spaarke-records-index` + writer + reader
- BFF code refactor: knowledge-v2 → files-index (~20 files including consumer services + templates + doc-comments)
- Removal of `AiSearchOptions.DiscoveryIndexName` property entirely
- Embedding model alignment to `text-embedding-3-large` (FR-20)
- Dev BFF app settings: hardcoded URLs/API keys → Key Vault references
- `spaarke-rag-references` field-name bug fix (Phase 2)
- Test-fixture sweep alongside DI changes (NFR-14)
- Stale-doc cleanup + ADR pointer drift fix
- Pre-Phase-3 operational verification (10 checks per FR-21)

### Out of Scope

- Prod / demo AI Search restoration
- `spaarke-environment-factory-r1` work (prerequisite, not part of it)
- Multi-tenant architecture redesign for `spaarke-records-index`
- Data seeding tooling (separate repo: `SPAARKE-DATA-CLI`)
- BFF App Service Plan tier changes
- Redis canonical-rename + KV secret migration (delegated to `spaarke-redis-cache-remediation-r1` — DELIVERED)

## Key Decisions

| Decision | Rationale | Source |
|----------|-----------|--------|
| Two-tier naming: top-level env-suffixed, sub-resources env-agnostic | Build-once-deploy-anywhere; DNS uniqueness only at top level | design.md §Naming Convention; NFR-03 + NFR-10 |
| `spaarke-rag-references` canonical field = `documentType` (rename `domain`) | Bug confirmed 2026-06-26: PS writers use `domain`, C# reader filters `documentType` → PS-indexed docs invisible | FR-17; NFR-08 |
| Single unified deployer; retire 5 per-index PS scripts | One source of truth; mirrors Redis project's validated pattern | FR-07; MUST-rule |
| `infrastructure/ai-search/` is the single consolidated schema location | More mature dir, more files already; matches naming convention | FR-11; Assumption |
| `text-embedding-3-large` (3072 dim) canonical embedding | Schema uses 3072; appsettings drift to `-small` (1536) is the bug | FR-20; NFR-11 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Test fixtures fail with KV-ref DI tightening (Redis hit 337 failures) | High | High | NFR-14 binding — fixture sweep MUST land in same PR as DI changes |
| `text-embedding-3-large` deployment missing in dev Azure OpenAI | High | Low | FR-21 check #4 verifies deployment exists before FR-20 + FR-18 |
| BFF MI role lost on recreated `spaarke-search-dev` | High | High | FR-21 check #2 re-runs Bicep to restore `Search Index Data Contributor` |
| Wrong KV name from spec Assumption #5 | High | Confirmed | Spec corrected 2026-06-26: canonical KV = `spaarke-spekvcert` (NOT `sprkspaarkedev-aif-kv`) |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| `spaarke-search-dev` Azure AI Search service provisioned | External | Ready | Recreated empty 2026-06-25 |
| `spaarke-redis-cache-remediation-r1` Phase 3 cutover | Internal | DELIVERED 2026-06-26 | PR #458 merged; BFF Redis-enabled verified |
| `spaarke-spekvcert` Key Vault accessible (BFF MI role) | External | Ready | `Key Vault Secrets User` role confirmed |
| Azure OpenAI `text-embedding-3-large` deployment in dev | External | Verify (FR-21 #4) | Pre-condition for FR-20 + FR-18 |
| Source data intact (Dataverse, KNW-*.md, playbooks, insights events) | Internal | Ready | For ingestion in Phase 5 |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-06-25 | 0.1 | Initial design + spec drafted | Ralph Schroeder / Claude Code |
| 2026-06-26 | 1.0 | Spec absorbed 10 verified Affected-Areas gaps; Redis prereq delivered; project artifacts generated | Claude Code |
| 2026-06-26 | 2.0 | **PROJECT COMPLETE** — all 30 tasks ✅; 8 schemas deployed to dev (FR-14 reframe expanded 7→8); 194 docs ingested (records 67 + rag-references 93 + playbooks 34); BFF deployed + 5 endpoints verified; KV-ref migration applied; lessons-learned captured | Claude Code |

## Final Deliverables

### Documentation (5 canonical docs)
- `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` (8 indexes, schema property policy, vector + embedding config, retired-index appendix with reactivation trail)
- `docs/guides/ai-search-azure-setup.md` (operator runbook — provision-to-verify)
- `docs/architecture/AI-ARCHITECTURE.md` updated (AI Search Consumer Map)
- `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 added
- `docs/architecture/rag-architecture.md` updated (7-index landscape, now 8)

### Scripts (1 unified deployer; 6 retired)
- `scripts/ai-search/Deploy-AllIndexes.ps1` (430+ LOC; SupportsShouldProcess + Force gate + verifier + KV-ref cutover; catalog-driven)
- Retired same PR (FR-07): Deploy-IndexSchemas.ps1, Deploy-InvoiceSearchIndex.ps1, deploy-invoice-index.ps1, deploy-invoice-index.bicep, deploy-session-files-index.ps1, Create-PlaybookEmbeddingsIndex.ps1

### Schemas (8 canonical; 1 new + 7 updated)
- All 8 in `infrastructure/ai-search/` consolidated location:
  - `spaarke-files-index.json` (renamed from spaarke-file-index)
  - `spaarke-discovery-index.json` (NEW per FR-14 reframe)
  - `spaarke-records-index.json` (with new tenantId field — FR-12)
  - `spaarke-rag-references.json` (field-name bug fix domain→documentType — FR-17)
  - `spaarke-insights-index.json` (Collection ComplexType sortable fix)
  - `spaarke-session-files.json`
  - `spaarke-invoices-index.json` (renamed from spaarke-invoices-dev)
  - `spaarke-playbook-embeddings.json` (renamed from playbook-embeddings)

### Code Changes (~15 BFF files; pure refactor)
- `AiSearchOptions.cs`: KnowledgeIndexName + DiscoveryIndexName defaults canonicalized
- `AnalysisOptions.cs`: SharedIndexName default canonicalized
- `IKnowledgeDeploymentService.cs`: defaults + doc-comments updated
- `KnowledgeBaseEndpoints.cs`: fallback default updated
- `KnowledgeDocument.cs` + `AiAnalysisNodeExecutor.cs`: doc-comments
- `appsettings.template.json`: KnowledgeIndexName + DiscoveryIndexName + AllowedIndexes + EmbeddingModelName (FR-20)
- `Sync-RecordsToIndex.ps1`: tenantId parameter + populator (FR-12)
- Frontend test fixtures: `SearchIndexResolver.ts` doc-comment, `searchIndexResolver.test.ts` constants

### Azure State (dev — `spaarke-search-dev` + `spaarke-bff-dev`)
- 8 indexes deployed + post-deploy invariants pass
- 194 documents ingested across 3 indexes
- 7 App Service KV-ref settings live (canonical `@Microsoft.KeyVault(VaultName=...;SecretName=...)` form)
- BFF binary deployed (hash-verified)
- 5 endpoints registered + auth-gated (401 not 404)
- healthz returns 200

### Dataverse State (dev — `spaarkedev1.crm.dynamics.com`) — POST-WRAP-UP BACKFILL
- 21 of 21 stale `sprk_searchindexname` records updated to canonical `spaarke-files-index`
  (16 matters + 3 projects + 2 business units; 0 invoices had the field set)
- 5 AI-Search-related env vars audited: 4 canonical, 2 intentionally NULL (BFF
  resolves via App Service KV refs per Spaarke "no direct frontend→AI Search" rule)
- 27 JS web resources scanned: ZERO contain stale index names
- `sprk_aiknowledgedeployment` empty (no records to update)
- 34 `sprk_aianalysisplaybook` records ↔ 34 `spaarke-playbook-embeddings` docs (1:1)
- 2 reusable scripts added for future env provisioning:
  - `scripts/Update-StaleDataverseIndexNames.ps1` (canonical rewriter)
  - `scripts/Audit-DataverseAiSearchSurfaces.ps1` (6-audit verification)

### Project Artifacts (8 evidence files)
- `notes/pre-phase-3-verification.md`, `notes/group-c-disposition.md`, `notes/kv-migration-verification.md`, `notes/test-fixture-sweep.md`, `notes/phase-4-final-verification.md`, `notes/phase-5-deploy-evidence.md`, `notes/phase-5-ingestion-evidence.md`, `notes/phase-5-functional-verification.md`, `notes/deploy-allindexes-validation.md`, `notes/lessons-learned.md`

---

*Repository: [spaarke](https://github.com/spaarke-dev/spaarke) | Branch: `work/spaarke-ai-azure-setup-dev-r1`*
