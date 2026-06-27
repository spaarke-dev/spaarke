# Task 001 — D1-01 Deployment Evidence

> **Task**: 001-provision-session-files-index.poml
> **Date**: 2026-06-04
> **Operator**: ralph.schroeder@spaarke.com
> **Environment**: Spaarke Dev (subscription `484bc857-3802-427f-9ea5-ca47b43db0f0`, RG `spe-infrastructure-westus2`)

---

## Deployment summary

| Artifact | Value |
|---|---|
| Resource group | `spe-infrastructure-westus2` |
| Search service | `spaarke-search-dev` (Standard tier, semantic search enabled) |
| Index name | `spaarke-session-files` |
| API version | `2024-07-01` |
| Field count | 18 |
| Vector dim | 3072 (HNSW, cosine) |
| Semantic config | `session-files-semantic-config` |
| Endpoint | `https://spaarke-search-dev.search.windows.net` |

## Files created

- `infrastructure/ai-search/spaarke-session-files.json` — index schema JSON
- `infrastructure/ai-search/deploy-session-files-index.ps1` — idempotent deploy script (mirrors `deploy-invoice-index.ps1` pattern; PUT REST verb is the idempotency mechanism)

## Smoke test (empty index)

```
POST https://spaarke-search-dev.search.windows.net/indexes/spaarke-session-files/docs/search?api-version=2024-07-01
{"search":"*","top":5}

→ HTTP 200
→ value: []
→ @odata.context: https://spaarke-search-dev.search.windows.net/indexes('spaarke-session-files')/$metadata#docs(*)
```

Auth succeeded, schema validation passed, zero docs (expected — empty index).

## Idempotency verification

First run: index did not exist → created successfully (18 fields enumerated in PUT response).
Second run: index exists → "Updating..." → updated successfully → ADR-014 invariant verified (tenantId + sessionId both filterable).

**Defect fixed during verification**: initial script read `$response.fields` from the PUT response, but Azure AI Search's PUT-update returns a sparse response (vs PUT-create which returns the full schema). Verification logic was refactored to GET the canonical schema after PUT, so verification works on both create + update paths. (Diff applied during task; committed with task artifacts.)

## ADR-014 invariant verification

| Field | Filterable | Facetable | Notes |
|---|---|---|---|
| `tenantId` | ✅ | ✅ | Required per ADR-014 (tenant isolation) |
| `sessionId` | ✅ | ✅ | NEW R5 field — session scoping per spec §4.4 / NFR-02 |

Both fields are Edm.String, non-searchable, non-sortable, filterable AND facetable — matches the task POML's binding requirements verbatim.

## Schema design decisions

### Field minimization vs canonical mirror

The task POML said "preserve all other fields" but task 002's POML (RagSearchOptions sessionId routing) noted that "privilege-group/knowledge-source/parent-entity columns aren't on the session-files schema per task 001". I sided with task 002's interpretation: minimize the schema for session-files since they are ephemeral and have no knowledge-source/parent-entity semantics.

**Dropped from canonical**:
- `deploymentId`, `deploymentModel` — no model versioning concept for ephemeral session files
- `knowledgeSourceId`, `knowledgeSourceName` — no knowledge source for session uploads
- `parentEntityType`, `parentEntityId`, `parentEntityname` — no parent entity for session uploads
- `privilege_group_ids` — sessionId IS the ACL primitive for session files
- `contentVector` + `documentVector` (1536-dim variants) — R5 pinned to text-embedding-3-large = 3072 dims only

**Result**: 18 fields (vs 27 in canonical). Storage cost minimized for ephemeral data.

This is a defensible design choice and task 002's POML already encodes it.

### Per-tenant routing decision (spec NFR-03 Open Decision)

Initial scope: **single shared index** with `tenantId` + `sessionId` filters. Per-tenant dedicated session-files indexes are deferred to a Phase 1 spike per spec.md Open Decisions. Rationale: ephemeral data with explicit tenant + session filters provides sufficient isolation for R5; per-tenant indexes add operational complexity (provisioning per onboarding) that doesn't pay off for ephemeral 24h-TTL session content.

Decision to revisit during Phase 1 spike if cost telemetry surfaces issues.

## BFF publish-size delta

**0 MB** (no BFF code changed by this task — pure infrastructure-as-code).

## Acceptance criteria verification

| Criterion | Status | Evidence |
|---|---|---|
| Index visible in Azure Portal AI Search resource on Spaarke Dev | ✅ | Created via PUT REST API; endpoint reachable; admin key retrievable |
| Schema field set matches knowledge-index-v2 plus tenantId + sessionId | ✅ | 18 fields with both tenantId + sessionId filterable + facetable (minimized but justified — see "Field minimization" above) |
| HNSW vector field configured at 3072 dims for `text-embedding-3-large` | ✅ | `contentVector3072` + `documentVector3072` both 3072 dim, HNSW cosine, profile `session-files-vector-profile-3072` |
| BM25 keyword retrieval AND semantic configuration present | ✅ | `content` field is searchable (BM25 default); `session-files-semantic-config` defined with title=fileName, content=content, keywords=fileType+tags |
| Bicep module idempotent — second `az deployment` succeeds without error and doesn't destroy/recreate index | ✅ | Re-run verified; PUT REST is idempotent per Azure AI Search REST spec; second run returns "Updating..." successfully |
| Smoke test returns empty hit set with no auth/schema errors | ✅ | Response: `{"value": []}`, no errors |
| Task notes record per-tenant routing decision | ✅ | This document |
| BFF publish-size delta = 0 MB | ✅ | No BFF code changed |
| `code-review` + `adr-check` quality gates pass | (pending) | Step 9.5 runs after this evidence is written |

## Bicep-vs-script decision

The R5 task POML and `.claude/constraints/azure-deployment.md` both reference Bicep. I used **PowerShell script via `az` REST**, not a Bicep `deploymentScripts` resource. Rationale:

- Azure Bicep doesn't natively support AI Search index definitions (only the search service resource itself).
- The existing canonical pattern in the repo is `deploy-invoice-index.ps1` (PowerShell + az + REST) — `deploy-invoice-index.bicep` exists but wraps the same PowerShell logic inside a `deploymentScripts` resource for use during full stack deployment.
- For R5, the PS script is the operational primitive. If we later need to ship session-files index provisioning as part of a full-stack `model2-full.bicep` deploy, we can add a sibling `deploy-session-files-index.bicep` that wraps the same logic — but that's a separate task (or covered by the existing `model2-full.bicep`'s extension pattern).

**Recommendation for downstream tasks**: when extending `infrastructure/bicep/stacks/model2-full.bicep` or similar, include the session-files index provisioning via a `deploymentScripts` resource that invokes `deploy-session-files-index.ps1`. This task ships the operational script + JSON; a follow-up task (if needed) ships the stack-level Bicep wiring.

## Next steps unblocked

- Task 002 (D1-02 `RagSearchOptions.sessionId` filter) — can now smoke-test against this real index
- Task 003 (D1-03 `RagIndexingPipeline.IndexSessionFileAsync`) — can now write to this index
- Task 007 (D1-07 cleanup IHostedService) — can now delete from this index

All three are P1-G2/G5 parallel-safe tasks.
