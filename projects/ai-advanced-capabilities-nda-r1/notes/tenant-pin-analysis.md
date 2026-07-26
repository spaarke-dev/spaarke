# NFR-06 Tenant-Pin Analysis — Task 012

> **Status**: Analysis complete; empirical verification ENV-BLOCKED (no live Azure Search / Dataverse credentials in this session).
> **Date**: 2026-07-22
> **Task**: `projects/ai-advanced-capabilities-nda-r1/tasks/012-seed-nda-standard-grounding-pin.poml`

## 1. Question

Reference docs in `spaarke-rag-references` (KNW-001..010, and the new KNW-011 NDA standard) are indexed with
`tenantId = "system"`. Does `ReferenceRetrievalService.SearchReferencesAsync` — the call NDA-REVIEW's L1
knowledge-retrieval step makes — filter in a way that matches `tenantId eq 'system'`, or will it filter on
the real execution tenant and return zero results?

## 2. How the tenant filter is applied (code trace)

**Seeding side** — `scripts/ai-search/Add-ReferenceToIndex.ps1` line 453 (and the equivalent BFF-side path,
`Services/Ai/Indexing/KnowledgeDocumentSchemaMapper.cs` line 53) both hard-code:

```
tenantId = "system"
```

This is documented in `KnowledgeDocumentSchemaMapper.cs` as "mirroring the original golden-reference
convention" — i.e. golden reference content is treated as tenant-agnostic/org-wide by design.

**Retrieval side** — `src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceRetrievalService.cs`,
`BuildSearchOptions` (lines 288–316):

```csharp
// ALWAYS filter by tenant for security
filters.Add($"tenantId eq '{EscapeFilterValue(options.TenantId)}'");
```

There is **no "system" fallback or OR-clause** — the filter is unconditionally
`tenantId eq '{options.TenantId}'`, where `options.TenantId` is whatever the caller passed in
`ReferenceSearchOptions.TenantId`.

**Caller side** — `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` line 854, `RetrieveReferenceKnowledgeAsync`:

```csharp
var searchResponse = await _referenceRetrieval.SearchReferencesAsync(
    query,
    new ReferenceSearchOptions { TenantId = context.TenantId, ... },
    cancellationToken);
```

`context.TenantId` (the `NodeExecutionContext`) is set from `context.TenantId` passed down through
`CreateToolExecutionContextAsync` (line 451), which ultimately traces back to the run's
`PlaybookRunContext.TenantId`.

**Where `PlaybookRunContext.TenantId` comes from** — `Services/Ai/PlaybookRunContext.cs`:
- Interactive (HTTP) constructor (line 50): `TenantId = ExtractTenantId(httpContext)`, where
  `ExtractTenantId` (line 325) reads the **Azure AD `tid` claim** (or the
  `http://schemas.microsoft.com/identity/claims/tenantid` claim), falling back to the literal string
  `"default"` only if neither claim is present.
- App-only (background) constructor (line 74): `TenantId = tenantId` — an explicit parameter (not
  `"system"` unless the caller passes that literal string).

There is a second tenant-resolution path used by the older non-node-based orchestration flow
(`AnalysisOrchestrationService.cs` line 825): `_ragProcessor.GetTenantIdFromClaims() ?? "default"`, which
reads the same `tid` / tenant claims via `HttpContextAccessor` (`AnalysisRagProcessor.cs` lines 64–75).

## 3. What tenant NDA-REVIEW will actually execute under

Compose is an interactive, browser-driven surface — every NDA-REVIEW run is triggered by an authenticated
user request, not a background job. That means the request flows through the **interactive
`PlaybookRunContext` constructor**, and `context.TenantId` will be the caller's **Azure AD `tid` claim**
(the real Entra ID tenant GUID for the Spaarke customer/environment), not the literal string `"system"`.

(The app-only / background path exists for scheduled/system-triggered analysis and is not the path
Compose's interactive NDA-REVIEW uses; it is noted here only because `AiAnalysisNodeExecutor` is shared
code and a future background-triggered NDA-REVIEW would hit the same mismatch unless it is explicitly
passed `tenantId = "system"`.)

## 4. Finding: MISMATCH (confirmed by code trace, not yet by live query)

`ReferenceRetrievalService.BuildSearchOptions` filters `tenantId eq '{tid-claim-value}'`. Every document in
`spaarke-rag-references` — KNW-001 through KNW-010, and the new KNW-011 seeded by this task — is indexed
with `tenantId = "system"`. **`'{tid-claim-value}' != 'system'`** for any real interactive user, so the
`tenantId eq '...'` filter will exclude every golden-reference chunk. L1 retrieval in
`AiAnalysisNodeExecutor.RetrieveReferenceKnowledgeAsync` will return 0 results, `referenceKnowledge` will be
`null`, and the merged knowledge context passed to the tool handler will silently omit the standard —
NDA-REVIEW would run "ungrounded" with no error surfaced (L1 failures are non-fatal by design, logged only
at Information/Warning level).

**This is not unique to the new NDA-standard source** — it is a pre-existing structural mismatch affecting
all 10 previously-indexed golden references (`KNW-001`..`KNW-010`) whenever any playbook runs interactively
under a real tenant. This task surfaced it; it was not introduced by seeding KNW-011.

## 5. Resolution options considered

| Option | What it does | Verdict |
|---|---|---|
| **(A) Seed under the execution tenant** | Change the ingest script/mapper to stamp real tenant GUID instead of `"system"` | **Rejected as the sole fix.** Golden references are explicitly documented as tenant-agnostic/org-wide content (`KnowledgeDocumentSchemaMapper` comment: "mirroring the original golden-reference convention"). Seeding per-tenant would mean re-seeding all 11 sources per Spaarke customer environment and would fight the existing "system-wide reference" design intent instead of aligning with it. |
| **(B) Align the retrieval filter to also match `"system"`** | Change `ReferenceRetrievalService.BuildSearchOptions` to filter `(tenantId eq '{tenant}' or tenantId eq 'system')` — the same "system sentinel" pattern already used elsewhere in this codebase (`EmbeddingCache.SystemTenantSentinel`, `PlaybookService.SystemTenantSentinel`, `TextExtractorService.SystemTenantSentinel`, `RecordSearchService._noUserTenantFallback`) | **Recommended fix**, but this file is a tenant-isolation security boundary (`ReferenceRetrievalService` doc comment: "MUST include tenantId filter on all queries (security)"). Per this task's own `<escalation>` trigger ("If resolving the tenant pin would require changing multi-tenant isolation semantics in ReferenceRetrievalService, STOP and escalate ... rather than broadening the tenant filter"), **this code change is NOT applied by this task** — it is escalated below. |
| **(C) Pivot: don't seed under `"system"`, use per-tenant seeding only for THIS source** | Seed KNW-011 under whatever tenant NDA-REVIEW runs under in the deploy target, leaving KNW-001..010 as-is | **Rejected.** Would create an inconsistent seeding convention across the 11 sources in the same index, contradicting §11 (reuse/consistency) and not actually fixing the pre-existing mismatch for KNW-001..010 that NDA-REVIEW's own checklist reference (KNW-002) also needs. |

## 6. Escalation — required per this task's `<escalation>` trigger and root CLAUDE.md §6.5

🔔 **Human Input Required / ADR Conflict — Resolution Required**

- **File in question**: `src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceRetrievalService.cs` (`BuildSearchOptions`)
- **Specific rule**: XML-doc-documented invariant — "MUST include tenantId filter on all queries (security)" — currently implemented as an unconditional `tenantId eq '{options.TenantId}'` filter with no exception.
- **Conflict**: All content in `spaarke-rag-references` is, by explicit prior design (`KnowledgeDocumentSchemaMapper` comment), tenant-agnostic golden-reference content seeded under the `"system"` sentinel. The retrieval filter does not special-case that sentinel, so **zero golden references are ever retrievable by any interactive tenant** — a pre-existing, silent grounding failure, not a hypothetical one. NDA-REVIEW (and every other analysis Action that relies on L1 reference retrieval) will run ungrounded with no error surfaced.
- **Proposed path**: **(B) narrow, targeted amendment** — extend the tenant filter in `ReferenceRetrievalService.BuildSearchOptions` to `(tenantId eq '{tenant}' or tenantId eq 'system')`, mirroring the existing "system sentinel" pattern already used for other org-wide/shared resources in this codebase (`EmbeddingCache`, `PlaybookService`, `TextExtractorService`, `RecordSearchService`). This is additive (OR, not replace) and scoped to `spaarke-rag-references` only — it does not touch tenant isolation for customer document indexes (`spaarke-files-index`, `spaarke-records-index`, etc.), which have no `"system"` documents and are unaffected.
- **Rationale**: This aligns retrieval behavior with an already-documented seeding convention rather than inventing new semantics; the "system" sentinel pattern is an established, reviewed idiom elsewhere in the same codebase for exactly this org-wide-vs-tenant-scoped distinction.
- **Impact if path B is accepted**: One-method code change in `ReferenceRetrievalService.cs` (~1 line, OR-clause) + regression test asserting `(A)` a `"system"`-tenant document IS returned for any tenant, and `(B)` a genuinely tenant-scoped document from a different tenant is still NOT returned (isolation still holds for anything that isn't the `"system"` sentinel). Unblocks grounding for all 11 golden references immediately, without any re-seeding.
- **Alternatives considered and rejected**: (A) per-tenant seeding — rejected, see table above (fights existing design intent, doesn't fix KNW-001..010); (C) per-source inconsistent seeding — rejected, see table above.

**This task does not implement path B.** Per the task's own escalation trigger, a change to
`ReferenceRetrievalService`'s tenant-isolation filter is security-adjacent and is left for explicit human
sign-off (or a follow-on task, e.g. as part of 052's integration-test work) rather than applied silently
here.

## 7. `documentType` vs `domain` filter — confirmed

`ReferenceRetrievalService.BuildSearchOptions` (line 310–313):

```csharp
// Optional: filter by domain (stored in documentType field)
if (!string.IsNullOrEmpty(options.Domain))
{
    filters.Add($"documentType eq '{EscapeFilterValue(options.Domain)}'");
}
```

Confirmed: the filter predicate is on the **`documentType`** index field (post-rename), not a `domain`
field. `Add-ReferenceToIndex.ps1` writes `documentType = $sourceDomain` (line 456), where `$sourceDomain`
is resolved from the `.md` header's `> **Domain**: legal` line (or the `-Domain` script parameter,
default `"legal"`). So the front-matter field is spelled "Domain" for authoring convenience, but the
script maps it into the index's `documentType` field at write time — there is no drift between the two.
KNW-011 was authored with `> **Domain**: legal`, which will populate `documentType: legal` on ingest,
matching the task's `documentType: legal` requirement.

## 8. Empirical verification — ENV-BLOCKED

The following steps CANNOT be run in this session (no `az login` / live Azure AI Search admin key / live
Dataverse token available). They must be run by an operator with Azure credentials before this task's
acceptance criterion #2 can be marked met.

### Step 1 — Ingest KNW-011

```powershell
.\scripts\ai-search\Add-ReferenceToIndex.ps1 `
  -FilePath "projects\x-ai-spaarke-platform-enhancements-r1\notes\design\knowledge-sources\KNW-011-spaarke-nda-standard.md"
```

**Expected passing result**: console output ending `=== Indexing Complete ===`, `Chunks: <N>` (N > 0),
`Dimensions: 3072`, `Index: spaarke-rag-references`, `Domain: legal`, and (unless `-SkipDataverse`) a
`Dataverse Record: <guid>` line with `Delivery Type: RAG Index (100000002)`.

### Step 2 — Confirm chunks landed under `tenantId=system`

```bash
SEARCH_KEY=$(az search admin-key show --service-name spaarke-search-dev --resource-group spe-infrastructure-westus2 --query 'primaryKey' -o tsv)
curl -s -X POST \
  "https://spaarke-search-dev.search.windows.net/indexes/spaarke-rag-references/docs/search?api-version=2024-07-01" \
  -H "api-key: $SEARCH_KEY" -H "Content-Type: application/json" \
  -d '{"search":"*","filter":"knowledgeSourceId eq '\''KNW-011'\''","select":"id,knowledgeSourceName,tenantId,documentType,chunkIndex,chunkCount","top":5}'
```

**Expected passing result**: non-empty `value[]` array; every returned doc has `"tenantId":"system"` and
`"documentType":"legal"`.

### Step 3 — Empirically confirm (or refute) the tenant-pin mismatch

Determine NDA-REVIEW's actual execution tenant GUID (the `tid` claim value for whatever Entra ID tenant
this Spaarke environment/user is running under — check a captured BFF request's JWT, or App Insights
`traces` for a `ReferenceRetrievalQuery tenant={TenantId}` log line from a real Compose session), call it
`<EXEC_TENANT_ID>`, then query with that exact tenant filter — the same filter shape
`ReferenceRetrievalService` builds:

```bash
curl -s -X POST \
  "https://spaarke-search-dev.search.windows.net/indexes/spaarke-rag-references/docs/search?api-version=2024-07-01" \
  -H "api-key: $SEARCH_KEY" -H "Content-Type: application/json" \
  -d '{"search":"*","filter":"tenantId eq '\''<EXEC_TENANT_ID>'\'' and knowledgeSourceId eq '\''KNW-011'\''","select":"id","top":5}'
```

**Expected result BEFORE any fix (predicted, per §4/§6 above)**: `"value":[]` — zero chunks, confirming the
mismatch empirically.

**Expected result AFTER path B is implemented and re-tested** (filter becomes
`(tenantId eq '<EXEC_TENANT_ID>' or tenantId eq 'system') and knowledgeSourceId eq 'KNW-011'`):
non-empty `value[]` — confirms the fix. This is the check task 052 should promote to a standing
integration test per the project's own task graph (052 depends on 012).

### Step 4 — End-to-end: run NDA-REVIEW and confirm L1 retrieval logs non-zero results

Trigger a real NDA-REVIEW playbook run (once task 020 exists) and search App Insights `traces` for:

```
L1 reference retrieval for node {NodeId}: {ResultCount} chunks from {SourceCount} source(s) ...
```

**Expected passing result**: `ResultCount > 0` and `SourceCount` includes KNW-011 (and ideally KNW-002, the
existing NDA checklist). A `ResultCount: 0` here confirms the grounding gap is live, not just theoretical.

## 9. Summary

- Baseline NDA standard content prepared for ingestion (§9 files_changed below); ingest script run itself is
  env-blocked.
- `documentType` (not `domain`) is confirmed as the actual filter field — no discrepancy between the `.md`
  header convention and the runtime filter.
- Tenant pin: **confirmed mismatch by code trace** (seeded `tenantId="system"`, retrieval filters on the
  real Entra ID `tid` claim, no `"system"` fallback). This affects all 11 golden references, not just the
  new one.
- Resolution: **escalated per the task's own trigger** rather than silently patched — recommended fix
  (path B, OR-clause for the `"system"` sentinel in `ReferenceRetrievalService.BuildSearchOptions`) is
  documented in §6 for human sign-off, matching an existing idiom already used elsewhere in this codebase.
- Empirical verification (both that KNW-011 indexed correctly and that the tenant-pin mismatch either does
  or doesn't reproduce, and — post-fix — that it's resolved) requires live Azure credentials not available
  in this session; exact commands + expected results are in §8 for an operator to run.
