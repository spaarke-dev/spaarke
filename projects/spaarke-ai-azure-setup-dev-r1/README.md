# Spaarke AI Search Azure Setup (Dev Restoration + Canonicalization)

> **Last Updated**: 2026-06-26
>
> **Status**: Ready for Implementation
>
> **Created**: 2026-06-25
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
| **Phase** | Ready for Implementation |
| **Progress** | 0% |
| **Target Date** | TBD |
| **Completed Date** | — |
| **Owner** | ralph.schroeder@hotmail.com |

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

## Graduation Criteria

The project is **complete** when:

- [ ] All 3 canonical docs published: `AI-SEARCH-INDEX-CATALOG.md`, `ai-search-azure-setup.md`, updated `AI-ARCHITECTURE.md`
- [ ] `Deploy-AllIndexes.ps1` operational: idempotent, `-DryRun` + `-VerifyOnly` work, post-deploy verifier asserts policy per index
- [ ] All 7 active indexes deployed to `spaarke-search-dev` with canonical names + correct property policy + 3072-dim vectors
- [ ] `spaarke-records-index` has `tenantId` field populated by ingestion; reader removes workaround comment
- [ ] `grep -r` for retired index names in `src/` returns zero matches as live string values
- [ ] No hardcoded API keys or URLs in dev BFF app settings (migrated to Key Vault references)
- [ ] Dev BFF functional: `/healthz` Healthy + 4 AI endpoints return real (non-error) results
- [ ] `SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 added with `Deploy-AllIndexes.ps1` invocation; Appendix D updated
- [ ] `factory-r1` handoff documented: factory-r1 plan references this project's deliverables as prerequisite
- [ ] Stale doc cleanups completed per FR-04
- [ ] ADR pointer drift resolved per FR-06
- [ ] BFF publish-size delta ≤ 0 MB per CLAUDE.md §10 NFR-01
- [ ] `spaarke-rag-references` field-name bug fix verified (golden-reference roundtrip)
- [ ] Embedding model `text-embedding-3-large` (3072 dim) alignment verified (FR-20)

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

---

*Repository: [spaarke](https://github.com/spaarke-dev/spaarke) | Branch: `work/spaarke-ai-azure-setup-dev-r1`*
