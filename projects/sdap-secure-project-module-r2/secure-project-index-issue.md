# Secure Project AI Search Indexing — Issue Analysis

> **Status**: Analysis / Decision needed
> **Created**: 2026-05-19
> **Author**: Claude Code (incident-triggered analysis during Auth v2 close-out)
> **Audience**: R2 design owner, R3 planners
> **Related**: [`sdap-secure-project-module/spec.md`](../sdap-secure-project-module/spec.md) (R1), [`sdap-secure-project-module-r2/design.md`](./design.md) (R2)

---

## TL;DR

R1 promised — but did not deliver — Azure AI Search isolation for Secure Project content. The codebase contains the *query-side* infrastructure (`PrivilegeFilterBuilder`, `IPrivilegeGroupResolver`, OData filter expressions) but **never built the indexing-side write path that populates the security field**. Every document currently in the live index has an empty privilege list, so the filter is effectively a no-op — search returns everything to every authenticated user. This bug was discovered on 2026-05-19 when a 400 indexing error surfaced the schema mismatch.

**Aligned with user direction (2026-05-19 session)**: rather than retro-fitting the existing `spaarke-knowledge-index-v2` (739 docs of internal Spaarke AI knowledge), **create a NEW dedicated index for secure-project / matter / invoice / work-assignment content**. The existing index remains unchanged and continues serving internal RAG.

This isolation is also a security win: customer matter content (high-sensitivity, multi-tenant boundaries) lives in a separate index from Spaarke's internal playbook / knowledge-base content, with separate access semantics, separate population pipelines, and separate retention policies.

---

## 1. What R1 spec promised vs. what R1 actually shipped

### 1.1 R1 spec deliverables touching AI Search

From [`sdap-secure-project-module/spec.md`](../sdap-secure-project-module/spec.md):

| Section | Promise |
|---|---|
| Scope (line 56) | "Azure AI Search — index field additions (`project_ids`, `business_unit_id`)" |
| FR-09 | "Semantic search: Scoped to project documents via AI Search `project_ids` filter; natural language queries return documents with excerpts" |
| FR-04 (orchestration) | "Three-Plane Access Orchestration (Grant)" — Dataverse + SPE + **AI Search** |
| NFR-04 | "AI Search queries with `search.in` filter must return within 1 second" |
| Success criterion #8 | "Semantic search returns only accessible project documents — Verify: search with `project_ids` filter" |
| Owner clarifications | "Home page AI: show pre-computed summaries from Document Profile playbook" |
| Unresolved questions | "AI Search index schema: Can `project_ids` be added as a filterable field to the existing index without full re-index?" |

The intended architecture was: tag every indexed chunk with the `project_ids` it belongs to, plus the `business_unit_id` of the owning BU. Query-time apply a `search.in(project_ids, '{accessible_project_ids}')` filter derived from the external user's participation records.

### 1.2 R1 shipped (audit of current codebase)

**✅ Delivered:**

| Area | Code |
|---|---|
| BFF API external endpoints | [`Api/ExternalAccess/`](../../src/server/api/Sprk.Bff.Api/Api/ExternalAccess/): `ExternalAccessEndpoints`, `GrantExternalAccessEndpoint`, `RevokeExternalAccessEndpoint`, `InviteExternalUserEndpoint`, `ProjectClosureEndpoint`, `ExternalUserContextEndpoint`, `ExternalProjectDataEndpoints` |
| External caller authorization filter | [`Api/Filters/ExternalCallerAuthorizationFilter.cs`](../../src/server/api/Sprk.Bff.Api/Api/Filters/ExternalCallerAuthorizationFilter.cs) |
| Participation cache | [`Infrastructure/ExternalAccess/ExternalParticipationService.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalParticipationService.cs), `ExternalCallerContext.cs`, `ExternalDataService.cs` |
| DI module | [`Infrastructure/DI/ExternalAccessModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs) |
| Dataverse | `sprk_externalrecordaccess` table, secure-project fields on `sprk_project` |
| Power Pages SPA | [`src/client/external-spa/`](../../src/client/external-spa/) — full React 18 + Fluent v9 implementation: `DocumentLibrary`, `EventsCalendar`, `SmartTodo`, `AiToolbar`, `SemanticSearch`, `InviteUserDialog` |
| Closure service | `closureService.ts` in shared components |
| Project closure cascade | `ProjectClosureEndpoint.cs` (Dataverse + SPE) |

**❌ NOT delivered (despite spec listing them):**

| Promised | Reality in codebase |
|---|---|
| AI Search field `project_ids` on the index | **Field does not exist anywhere in the codebase or live indexes.** A different field (`privilege_group_ids`) was added with similar intent, but only as a partial implementation (see §2). |
| AI Search field `business_unit_id` on the index | Field does not exist anywhere. |
| Three-plane orchestration of AI Search on Grant | No code path. `GrantExternalAccessEndpoint` orchestrates Dataverse + SPE; it does **not** touch AI Search. |
| Three-plane orchestration of AI Search on Revoke | Same — `RevokeExternalAccessEndpoint` doesn't touch AI Search either. |
| Semantic search scoped to `project_ids` filter | [`SemanticSearch.tsx:7`](../../src/client/external-spa/src/components/SemanticSearch.tsx) — comment says *"The project_ids filter is applied server-side via the entity scope"* — but the BFF endpoint receiving these calls does not in fact apply any external-caller-scoped filter to the AI Search query. |
| Success criterion #8 (semantic search returns only accessible projects) | Search returns documents based on whatever index it queries; isolation by external participation is not enforced. |

### 1.3 The architectural drift

Between spec and implementation, the privilege model **shifted** without being documented:

- **Spec**: filter by **entity IDs** the user has access to (`project_ids` IN [list-of-accessible-projects])
- **Code**: filter by **AAD group IDs** the user belongs to (`privilege_group_ids` ANY matches user's resolved groups)

Group-based filtering is a more general/flexible model — but it requires a *mapping* from "document chunk" to "owning AAD group(s)" that doesn't exist in any code. Without that mapping, every chunk has `privilege_group_ids = []` and the filter degrades to "return all public documents" — which is all of them.

So R1 delivered the *external access* feature end-to-end for Dataverse records and SPE files, but the **AI Search plane never actually got built** — and the spec's success criterion #8 has never been verified.

---

## 2. The schema mismatch incident (2026-05-19)

Surfaced when a user retested document indexing and hit:

```
400 Bad Request
A null value was found for the property named 'privilege_group_ids',
which has the expected type 'Collection(Edm.String)[Nullable=False]'.
```

### Investigation timeline

1. Live index `spaarke-knowledge-index-v2` (739 docs, the actively-used index) had **no `privilege_group_ids` field**.
2. Schema file [`infrastructure/ai-search/spaarke-knowledge-index-v2.json:228`](../../infrastructure/ai-search/spaarke-knowledge-index-v2.json#L228) declared the field correctly (filterable=true, retrievable=true), but the deploy script [`Deploy-IndexSchemas.ps1:42`](../../scripts/ai-search/Deploy-IndexSchemas.ps1#L42) was targeting the wrong index name (`spaarke-knowledge-index` vs the actually-used `-v2`), so the schema file had never been deployed to the live index.
3. User added the field manually via Portal UI to unblock indexing. The Portal-added field ended up with `filterable: false, retrievable: true`. Azure Search makes most field properties immutable post-creation.
4. The C# model [`KnowledgeDocument.cs:205`](../../src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs#L205) was sending `null` for documents without group restrictions; Azure Search `Collection(Edm.String)` is **implicitly Nullable=False** and rejects nulls.
5. Fix shipped same day: C# defaults to `new List<string>()`. BFF redeployed to `spe-api-dev-67e2xz`, hash-verify + healthz PASS.

### What this fixed and what it didn't

- ✅ Indexing now succeeds (sends `[]` instead of `null`).
- ❌ The field on `spaarke-knowledge-index-v2` is still wrongly configured (`filterable: false`) — any query that tries to filter on it will fail.
- ❌ The indexing pipeline still hard-codes `PrivilegeGroupIds = new List<string>()` everywhere (see [`RecordSyncJob.cs:486`](../../src/server/api/Sprk.Bff.Api/Services/Jobs/RecordSyncJob.cs#L486)). No code path computes what groups should be assigned.
- ❌ The privilege-filtering security feature remains dormant.

---

## 3. Live index inventory (2026-05-19)

| Index | Doc count | Purpose (current/intended) | `privilege_group_ids` status |
|---|---|---|---|
| `spaarke-knowledge-index-v2` | **739** | Internal Spaarke AI knowledge (RAG over internal playbooks, reference content) — **NOT customer matter content** | Field added with wrong config (`filterable: false`); pipeline hard-codes `[]` |
| `spaarke-knowledge-index` | 0 | Orphan — deploy script targets it but no code uses it | n/a |
| `spaarke-knowledge-shared` | 0 | Orphan | n/a |
| `spaarke-records-index` | 31 | Record matching service (`DataverseIndexSyncService`) | Same gap likely |
| `spaarke-rag-references` | 93 | Reference indexing pipeline | Unknown — separate model |
| `discovery-index` | 0 | Orphan / unused | n/a |
| `playbook-embeddings` | 16 | Playbook execution embeddings | Separate concern |
| `spaarke-invoices-dev` | 0 | Pre-existing (purpose unclear from this audit; possibly DM-related) | n/a |

**Critical implication**: the existing `spaarke-knowledge-index-v2` is the *internal* RAG index. Secure Project content is conceptually different (multi-tenant matter data, external user boundaries, per-matter ACLs). Mixing them in one index is what created the field-config dilemma — the internal RAG flow doesn't need per-group filtering, but the secure-project flow does.

---

## 4. R2 design review (in light of the indexing gap)

The [R2 design](./design.md) does NOT address AI Search at all:

| R2 design statement | Reality |
|---|---|
| Out of Scope: "Matter-level AI toolbar or semantic search (deferred to R3)" | R1 promised this for projects and it was never delivered. Deferring again to R3 means the **security feature stays dormant for matter content too**. |
| Architecture diagram (lines 71-106) | Shows BFF and Dataverse + SPE planes only. AI Search is absent. |
| Three-plane model (R1) | R2 silently drops the third plane (AI Search) without flagging the regression. |

**R2 design gap**: by deferring search to R3, R2 extends the security boundary to three new entity types (matter, invoice, work assignment) without delivering search isolation for any of them. The "Three-Plane Access Orchestration" framing from R1 spec no longer matches what the codebase does or what R2 plans to do.

---

## 5. Recommended approach: dedicated `spaarke-secure-content-index`

Per the user's 2026-05-19 directive ("create a separate index so that data is segmented … we may not need to redo the existing knowledge V2 index"), the right architecture is:

### 5.1 Strategy

- **Leave `spaarke-knowledge-index-v2` alone.** It's internal Spaarke AI content, doesn't need per-group filtering. The wrongly-configured `privilege_group_ids` field on it becomes dead weight — non-blocking. Optionally clean up in a maintenance pass.
- **Create new index `spaarke-secure-content-index`** (name TBD; could also be split per entity-type if there's clear value).
- **Schema from day 1 carries `privilege_group_ids` correctly**: `Collection(Edm.String)`, `filterable: true`, `retrievable: true`, `searchable: false`. Avoid the Portal-UI drift entirely.
- **Indexing pipeline** for matter/project/invoice/WA documents writes to the new index, and **populates `privilege_group_ids` from a defined policy**.
- **`RagService` routes** queries by context: external/secure callers → secure-content-index; internal RAG → knowledge-v2.
- **Deploy script fix** updates `Deploy-IndexSchemas.ps1` to target the actual names being used. Schema file becomes the canonical source of truth going forward.

### 5.2 Group-assignment policy (R2 must decide)

The biggest open question: when the indexing pipeline writes a chunk for a matter/project document, which AAD group IDs should it tag the chunk with?

Possible models (R2 must commit to one):

| Model | Source | Pro | Con |
|---|---|---|---|
| **Per-matter SP-{MatterRef} BU primary security group** | The child BU created by `ProvisionMatterEndpoint`; resolve its associated security group | Maps 1:1 to the BU that already isolates the matter | Requires standing up a security group per matter; ops overhead |
| **External participants from `sprk_externalrecordaccess`** | Aggregate Entra External IDs of all active grants on the matter | Direct mapping to who has access | Field churn on every grant/revoke; cache invalidation complexity |
| **Hybrid: BU group + per-grant individual user OIDs** | Both above | Most flexible | Most complex; field can grow large |
| **Internal team membership** | The matter's owning team's AAD group | Captures internal users too | Doesn't capture external participants |

**R2 design owner must choose** before any of the implementation work below can be scoped.

### 5.3 New index proposed schema

```json
{
  "name": "spaarke-secure-content-index",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true, "filterable": false, "retrievable": true },
    { "name": "tenantId", "type": "Edm.String", "filterable": true, "retrievable": true },
    { "name": "entityType", "type": "Edm.String", "filterable": true, "retrievable": true, "searchable": false },
    { "name": "entityId", "type": "Edm.String", "filterable": true, "retrievable": true, "searchable": false },
    { "name": "matterId", "type": "Edm.String", "filterable": true, "retrievable": true, "searchable": false },
    { "name": "projectId", "type": "Edm.String", "filterable": true, "retrievable": true, "searchable": false },
    { "name": "documentId", "type": "Edm.String", "filterable": true, "retrievable": true, "searchable": false },
    { "name": "speFileId", "type": "Edm.String", "filterable": true, "retrievable": true },
    { "name": "documentName", "type": "Edm.String", "searchable": true, "retrievable": true },
    { "name": "content", "type": "Edm.String", "searchable": true, "retrievable": true },
    { "name": "contentVector", "type": "Collection(Edm.Single)", "dimensions": 1536, "vectorSearchProfile": "..." },
    { "name": "chunkIndex", "type": "Edm.Int32", "retrievable": true },

    // The security field, CORRECTLY configured from day 1
    { "name": "privilege_group_ids", "type": "Collection(Edm.String)",
      "filterable": true, "retrievable": true, "searchable": false },

    // Discriminator for which planes (Dataverse / SPE) the chunk inherits ACLs from
    { "name": "spe_container_id", "type": "Edm.String", "filterable": true, "retrievable": true },

    { "name": "createdAt", "type": "Edm.DateTimeOffset", "filterable": true, "retrievable": true, "sortable": true },
    { "name": "updatedAt", "type": "Edm.DateTimeOffset", "filterable": true, "retrievable": true, "sortable": true }
  ],
  "vectorSearch": { /* HNSW + 1536-dim profile, same shape as knowledge-v2 */ }
}
```

Refine field set during R2 based on the group-assignment policy chosen.

### 5.4 Implementation tasks for R2 (proposed additions to R2 design)

Add these to R2's "Development Instructions" sequence (after Phase 4: BFF API, before Phase 5: SPA), or split into a separate "Phase 4.5: AI Search Integration":

1. **Schema file**: `infrastructure/ai-search/spaarke-secure-content-index.json` — the new index definition (§5.3 above)
2. **Deploy script update**: `Deploy-IndexSchemas.ps1` — add new index to `IndexMap`; also fix existing entries' target names so PUT lands on the right indexes
3. **New BFF service**: `Services/Ai/SecureContentIndexingPipeline.cs` — chunks + embeds + writes to the new index, populates `privilege_group_ids` per the chosen policy
4. **Service Bus wiring**: trigger SecureContentIndexingPipeline on:
   - Document upload to a secure matter/project SPE container
   - Grant/revoke (to rebuild affected docs' privilege_group_ids — or use a side cache, see §5.5)
5. **Group resolver implementation**: ensure `IPrivilegeGroupResolver` returns the AAD groups consistent with the chosen indexing policy (so query-side and write-side stay in sync)
6. **RagService routing**: dispatch queries to either `knowledge-v2` or `secure-content-index` based on caller context (`ExternalCallerContext` present → secure index)
7. **Query wiring on external endpoints**: ensure `/api/ai/search` and similar accept the external context and pass the user's resolved groups into the filter — this closes the gap where `SemanticSearch.tsx` claims server-side filtering but the BFF doesn't actually enforce it
8. **Verification tests**:
   - Index round-trip test: write a chunk with `privilege_group_ids = [g1]`, query as user in g1 → returned; query as user in g2 → not returned
   - External SPA: semantic search returns only accessible matter documents (R1 success criterion #8, finally)
9. **Backfill plan**: if any secure matter/project documents already exist in `spaarke-knowledge-index-v2` from earlier work, decide whether to re-index them into the new index. Likely answer: no — the new index starts empty, populated only by net-new uploads + a one-time backfill of currently-secure entities.

### 5.5 Optional: query-time group resolution instead of write-time tagging

An alternative design worth considering: instead of denormalizing AAD groups into each document chunk at index time, do it the inverse way:
- At write time, tag each chunk with just `matterId` / `projectId` / `entityType` (which are immutable)
- At query time, the BFF resolves the user's accessible matter/project IDs (from `ExternalParticipationService`'s Redis cache) and builds a `search.in(matterId, '{list}')` filter

**Pros**: no field churn on grant/revoke (group memberships change frequently; matter ID never changes). Simpler indexing pipeline. Naturally consistent with the participation cache.

**Cons**: bigger filter expression on every query (one per accessible matter); slightly slower for users with many matter accesses; not the path R1 envisioned.

This is closer to R1 spec's *original* intent (`project_ids` filter). **R2 should evaluate both** before committing.

---

## 6. Cleanup items (not blocking, but worth tracking)

| Item | Status | Where to track |
|---|---|---|
| Wrong `privilege_group_ids` config on `spaarke-knowledge-index-v2` | Dead weight, non-blocking | R2 / maintenance |
| Deploy script `IndexMap` targets wrong name | Pre-existing | R2 task: schema deploy fix |
| `RecordSyncJob.cs:486` hard-codes empty privilege list | Symptom of the policy gap | R2 task: indexing pipeline |
| `SemanticSearch.tsx:7` comment misleading ("filter applied server-side") | False as written | Update comment when search wiring lands in R2 |
| Orphan indexes (`knowledge-index`, `knowledge-shared`, `discovery-index`) | 0 docs each | R3 cleanup pass |

---

## 7. Decision required from R2 design owner

Before R2 implementation begins, please confirm:

1. **Index strategy**: separate `spaarke-secure-content-index` (recommended per 2026-05-19 directive) ☐  vs. retro-fit `spaarke-knowledge-index-v2` ☐
2. **Group-assignment policy** (§5.2): BU group ☐ / participation aggregate ☐ / hybrid ☐ / team-based ☐
3. **Indexing trigger model**: document upload only ☐ / + grant-revoke-driven retagging ☐ / + scheduled reindex ☐
4. **Query filter approach** (§5.5): write-time group tagging ☐ / query-time entity-ID resolution ☐ / hybrid ☐
5. **R2 scope expansion** to include AI Search work (vs. defer to R3): include in R2 ☐ / defer to R3 ☐

If R2 scope expands to include AI Search, this analysis suggests 5 net-new tasks (schema, deploy script, indexing pipeline, query routing, verification) plus 2 modifications (group resolver alignment, search endpoint wiring). Plausible 1-2 week increment to R2 timeline, depending on the policy decision in §5.2.

If R2 defers AI Search to R3, then R2's "Three-Plane Access Orchestration" framing should be explicitly downgraded in the design to "Two-Plane (Dataverse + SPE)" and the R1 spec acceptance criteria for search should be flagged as unmet pending R3.

---

## 8. References

- R1 spec: [`projects/sdap-secure-project-module/spec.md`](../sdap-secure-project-module/spec.md)
- R2 design: [`projects/sdap-secure-project-module-r2/design.md`](./design.md)
- Knowledge document model: [`src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs`](../../src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs)
- Privilege filter builder: [`src/server/api/Sprk.Bff.Api/Services/Ai/Security/PrivilegeFilterBuilder.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Security/PrivilegeFilterBuilder.cs)
- Schema files: [`infrastructure/ai-search/`](../../infrastructure/ai-search/)
- Deploy script: [`scripts/ai-search/Deploy-IndexSchemas.ps1`](../../scripts/ai-search/Deploy-IndexSchemas.ps1)
- Incident-context recovery file: [`projects/spaarke-auth-v2-and-hardening/current-task.md`](../spaarke-auth-v2-and-hardening/current-task.md)

---

*Generated during incident triage on 2026-05-19. Owner review needed before R2 implementation begins.*
