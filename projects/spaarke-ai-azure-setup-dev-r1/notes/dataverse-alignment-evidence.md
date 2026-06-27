# Dataverse Alignment Evidence — Post-Wrap-up Backfill

> **Date**: 2026-06-26
> **Authority**: User directive "we need to get every surface or component that needs AI search to be updated, to be updated"
> **Scope**: Dataverse data + config surfaces missed by original project spec (which scoped Dataverse as out-of-scope)

---

## Background

When the user asked "is this all fully deployed to dataverse and azure?", my initial answer was "Dataverse: not touched — out of scope". The user correctly challenged this — asking whether (a) there were no Dataverse components needing updates OR (b) there were components but I just didn't do them.

**Honest answer**: (b). The original spec scoped Dataverse out of the BFF/Azure refactor, but the canonical-rename work (knowledge-v2 → files-index, file-index → files-index, etc.) has data implications because Dataverse records carry `sprk_searchindexname` field values that the frontend resolver uses to determine which AI Search index to query.

This evidence file captures the **post-wrap-up backfill** that closes the Dataverse-side gap.

---

## Audit 1 — Records with `sprk_searchindexname` field

**Before** (audited 2026-06-26):

| Entity | Stale value | Count |
|---|---|---|
| sprk_matters | `spaarke-file-index` (singular) | 11 |
| sprk_matters | `spaarke-knowledge-index-v2` | 5 |
| sprk_projects | `spaarke-file-index` | 3 |
| businessunits | `spaarke-file-index` | 1 |
| businessunits | `spaarke-knowledge-index-v2` | 1 |
| **TOTAL stale** | | **21 records** |

**Runtime impact if not fixed**:
The frontend Wizard / PCF resolver reads `sprk_searchindexname` to determine which index to query. With my updated `AiSearch__AllowedIndexes` allow-list (`spaarke-files-index`, `spaarke-discovery-index`, `spaarke-rag-references`), a frontend passing `spaarke-file-index` (singular) or `spaarke-knowledge-index-v2` would have been REJECTED by BFF with `HTTP 400 INDEX_NOT_ALLOWED` per the existing allow-list enforcement code (NFR-08).

So these stale records would have caused **broken document operations** at runtime — the exact failure the project objectives were supposed to prevent.

**Fix applied**: `projects/spaarke-ai-azure-setup-dev-r1/scripts/Update-StaleDataverseIndexNames.ps1` (NEW)

Rewrite rules:
- `spaarke-file-index` (singular) → `spaarke-files-index`
- `spaarke-knowledge-index-v2` → `spaarke-files-index`
- `discovery-index` → `spaarke-discovery-index` (per FR-14 reframe)
- `spaarke-knowledge-shared` → `spaarke-files-index`

**After** (verified via `Audit-DataverseAiSearchSurfaces.ps1`):

| Entity | Value | Count | Marker |
|---|---|---|---|
| sprk_matters | `spaarke-files-index` | 16 | ✅ CANONICAL |
| sprk_projects | `spaarke-files-index` | 3 | ✅ CANONICAL |
| sprk_invoices | (no records with field set) | 0 | — |
| businessunits | `spaarke-files-index` | 2 | ✅ CANONICAL |
| **TOTAL** | | **21 records** | **all canonical** |

**Net change**: 21 of 21 stale records updated to canonical. Zero stale references remain.

---

## Audit 2 — Environment Variable Definitions

5 AI-Search-related env var definitions found:

| Schema name | Current value | Status |
|---|---|---|
| `sprk_BffApiBaseUrl` | `https://spaarke-bff-dev.azurewebsites.net/api` | ✅ Canonical (post-2026-05-27 rename from `spe-api-dev-67e2xz`) |
| `sprk_AzureOpenAiEndpoint` | `https://spaarke-openai-dev.openai.azure.com/` | ✅ Canonical |
| `sprk_AzureOpenAiKey` | (no override; default applies) | ✅ Correct (BFF uses MI per ADR-028) |
| `sprk_AzureAiSearchEndpoint` | (no override; default null) | ✅ Correct — see below |
| `sprk_AzureAiSearchKey` | (no override; default null) | ✅ Correct — see below |
| `sprk_CustomerTenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | ✅ Canonical |

**Why `sprk_AzureAiSearch*` env vars being NULL is correct**:

Spaarke's architecture rule (validated 2026-06-26 pre-pipeline audit): **ALL frontend AI-Search access in `src/solutions/` goes through the BFF API contract**. ZERO direct `*.search.windows.net` calls from frontends, PCFs, code pages, or declarative agents (CopilotAgent routes via `spaarke-api-plugin.json` → BFF OpenAPI spec).

This means the `sprk_AzureAiSearch*` env vars would only matter if a frontend bypassed BFF to call AI Search directly — which is forbidden. The BFF resolves `AiSearch__Endpoint` + `AiSearch__ApiKey` from its own App Service config (which I migrated to KV refs in task 041). So leaving the Dataverse env vars NULL is intentional / correct / safe.

If future architecture changes ever allow a frontend to call AI Search directly (e.g., for thin reader scenarios), these env vars would need to be populated with KV references following the same canonical pattern as the BFF.

---

## Audit 3 — `sprk_aiknowledgedeployment` Entity

Entity exists in schema but contains **0 records**. The C# `IKnowledgeDeploymentService` reads this entity for per-tenant index-name resolution (FR-WIZ-06 chain step 3). With no records, the resolver falls through to the appsettings default (`AiSearchOptions.KnowledgeIndexName = spaarke-files-index` — which I updated in task 030).

**Disposition**: No action needed.

When customers are onboarded post-Phase 5, the Spaarke Customer Onboarding playbook should populate `sprk_aiknowledgedeployment` records with canonical IndexName values for per-tenant routing.

---

## Audit 4 — `sprk_aianalysisplaybook` Catalog Alignment

**Total active playbooks**: 34
**Documents in `spaarke-playbook-embeddings` index**: 34

1:1 alignment ✅. The `Index-ExistingPlaybooks.ps1` ingestion run (task 052) correctly synced the entire active catalog.

---

## Audit 5 — Web Resources (Dataverse-hosted JavaScript)

Scanned 27 JavaScript web resources with `sprk_*` prefix for hardcoded references to retired index names (`spaarke-knowledge-index-v2`, `spaarke-file-index`, `discovery-index`, `spaarke-knowledge-shared`, `spaarke-knowledge-index`, `spaarke-invoices-dev`, `playbook-embeddings`).

**Result**: **ZERO web resources contain stale index names.** ✅

This confirms the frontend architectural rule: web resources don't reference index names directly — they pass through the `searchIndexResolver` chain (Step 1: parent record `sprk_searchindexname` field → Step 2: parent's owning BU's field → Step 3: empty string → BFF tenant default). Updating the data records (Audit 1) is sufficient to update what the web resources actually use at runtime.

---

## Audit 6 — Other Entities with AI Search Fields

| Entity | Result |
|---|---|
| `sprk_knowledgesources` | Doesn't exist (legacy entity removed) |
| `sprk_documenttypes` | Empty (0 records) |
| `sprk_analysisactions` | Has `sprk_allowsknowledge` (boolean toggle, NOT an index-name field) |

**Disposition**: No other entities carry AI Search index names.

---

## Net Result — Full Dataverse Alignment

| Surface | Status |
|---|---|
| Record data (`sprk_searchindexname`) | ✅ 21 of 21 records updated to canonical |
| Environment variables | ✅ Correct (canonical values OR intentionally NULL per architecture) |
| Knowledge deployment entity | ✅ Empty (no action needed) |
| Playbook catalog vs index | ✅ 34:34 1:1 alignment |
| Web resources | ✅ Zero stale references in 27 JS resources |
| Other entities | ✅ No AI Search index-name fields |

**Conclusion**: Dataverse is now FULLY ALIGNED with the canonical 8-index catalog. Every surface that needed updating has been updated. No remaining stale references that could cause `INDEX_NOT_ALLOWED` 400s at runtime.

---

## Scripts Added (reusable for future environments)

Both scripts are reusable for staging / prod / demo onboarding when the environment-factory project runs:

1. **`projects/spaarke-ai-azure-setup-dev-r1/scripts/Update-StaleDataverseIndexNames.ps1`**
   - Rewrites 4 stale index-name values to canonical across 4 entity sets
   - Supports `-DryRun` for preview
   - Targets `-DataverseUrl` parameter for per-env application
   - Idempotent — re-running against a clean env produces zero updates

2. **`projects/spaarke-ai-azure-setup-dev-r1/scripts/Audit-DataverseAiSearchSurfaces.ps1`**
   - 6-audit comprehensive surface scan
   - Useful as a post-deploy verification in environment-factory work
   - Targets `-DataverseUrl` parameter for per-env application

**Recommendation for `spaarke-environment-factory-r1`**: include both scripts in the standard provisioning playbook (run after BFF deploy + schema deploy + ingestion).

---

## Cross-References

- `projects/spaarke-ai-azure-setup-dev-r1/spec.md` FR-13 / FR-14 / FR-WIZ-06 (cascade resolver)
- `projects/spaarke-ai-azure-setup-dev-r1/notes/kv-migration-verification.md` (task 041 — `AllowedIndexes` allow-list)
- `src/server/api/Sprk.Bff.Api/Configuration/AiSearchOptions.cs` (AllowedIndexes property doc — explains 400 INDEX_NOT_ALLOWED enforcement)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/SearchIndexResolver.ts` (frontend resolver — reads `sprk_searchindexname`)
- `src/solutions/DocumentUploadWizard/src/components/searchIndexResolver.test.ts` (wizard resolver — FR-WIZ-06 chain test fixtures, updated in task 039)

---

*Evidence v1.0 — 2026-06-26. Closes the Dataverse-alignment gap discovered post-wrap-up.*
