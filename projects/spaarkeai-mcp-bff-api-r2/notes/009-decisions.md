# Task 009 — Record search: no query built from raw query text (proof)

Date: 2026-11-25. Verdict: **clean**. No production code changed.

## 1. Trace of `request.Query` (step 1)

Every use of `request.Query` in `RecordSearchService.SearchAsync` and in the code it calls:

| Where | What happens to the query | Query language? |
|---|---|---|
| `RecordSearchService.cs:181-183` | Debug log of `request.Query.Length` only (ADR-015) | No |
| `BuildCacheId` (:603) | `IEmbeddingCache.ComputeContentHash(request.Query)`; only the hash enters the Redis key via `ITenantCache` | No |
| `GetEmbeddingWithCacheAsync` (:205-210, :326-342) | `IEmbeddingCache.Get/SetEmbeddingForContentAsync(text)` (hash-keyed) and `IOpenAiClient.GenerateEmbeddingAsync(text)` | No (embedding input) |
| `searchText` (:273-275) | Passed verbatim as `SearchClient.SearchAsync(searchText, …)` in rrf / keywordOnly; `"*"` in vectorOnly | Search terms only. Simple/semantic query syntax operators in the text act within the filtered result set; they cannot widen past `SearchOptions.Filter` |
| `BuildRecordFilter` (:353-404) | **Not used.** Built only from the `tid` claim (or `AzureAd:TenantId` fallback), `RecordTypes` and `Filters.{Organizations,People,ReferenceNumbers}`, each through `EscapeODataValue` (doubles `'`) | OData (`$filter`) from structured inputs only |
| `BuildSearchOptions` (:409-479) | **Not used.** `VectorizedQuery` carries the computed embedding, never text | No |
| `IKnowledgeDeploymentService.GetSearchClientAsync` (:244) | Receives `SearchIndexName` + tenant, not the query | No |

Callers above the service: `RecordSearchEndpoints.PostRecordSearch` validates `Query` non-empty and allow-lists `RecordTypes` (`RecordEntityType.IsValid`); `AuthorizeRowsAsync` queries Dataverse per row with the row's entity set (allow-listed) and a parsed GUID — never the query. `RecordSearchAuthorizationFilter` does not read `Query`. Internal callers (`AiAnalysisNodeExecutor`, `RecordNameMatchRung`, `SemanticMatchRung`, `RecordMatchingAi`) pass text only as `Query` and reference numbers through `Filters`, which the service escapes.

No OData or FetchXML is built from `request.Query` anywhere on this path. The escalation trigger did not fire.

## 2. Test

`tests/integration/regression/RecordSearch/RecordSearchQueryConstructionTests.cs` (KEEP path, compiled by `Sprk.Bff.Api.Tests.csproj`'s regression glob). The real service runs; doubles only at module boundaries: `SearchClient`/`SearchIndexClient` (capture), `IOpenAiClient` (embedding), `IEmbeddingCache` and `ITenantCache` (Redis), `IKnowledgeDeploymentService` (strict, must not be called). Real `HttpContextAccessor`. No `Mock<HttpMessageHandler>` / `Mock<IServiceClient>`.

- `SearchAsync_HostileQuery_ReachesOnlySearchTextAndEmbedding` — 8 hostile queries (`' eq '`, `' or `, quote+paren break-out, double quotes, `<fetch…>`, `$filter=`, `any(`, `search.in(`) × 3 modes = 24 cases. Asserts: Filter **equals exactly** the tenant + recordType + structured-filter string; no other query-shaped option (OrderBy, Select, SearchFields, HighlightFields, ScoringParameters, ScoringProfile, SemanticQuery, SemanticConfigurationName) carries the marker; searchText = query (rrf/keywordOnly) or `"*"` (vectorOnly); every embedding input = query (none in keywordOnly); vector queries are `VectorizedQuery`.
- `SearchAsync_FilterValuesWithSingleQuotes_AreEscapedByDoubling` — hostile values in recordTypes (multi-type `search.in` branch), organizations, people, referenceNumbers; exact expected filter with doubled quotes.
- `SearchAsync_SingleRecordTypeWithSingleQuote_IsEscapedByDoubling` — single-type `eq` branch.

Scope justification (AC "test scope"): the "other query-shaped options" and `VectorizedQuery`-type assertions go one step beyond `Filter`; they cover the goal's "any FetchXML/OData string" and the constraint's "VectorQueries" capture.

**Deviation (AC wording):** AC 1 says SearchText "is null in vector-only mode". The service sends `"*"` (match-all) in vector-only mode, which carries no query text; the test pins `"*"`. No code change — `"*"` is the existing REST behaviour and changing it is out of scope.

## 3. Seeding proof (constraint + AC 3)

1. Appended `filterParts.Add($"search.ismatch('{EscapeODataValue(request.Query)}')")` to `BuildRecordFilter` → **26/26 failed** (even an escaped query in the filter is caught). Restored.
2. Made `EscapeODataValue` return its input unchanged → **2 failed** (both escaping tests). Restored.
Restored code: 26/26 pass. `git status` showed `RecordSearchService.cs` unmodified after each restore.

## 4. Results

- New file: 26 passed. `RecordSearchServiceTests` 29, `RecordSearchEndpointsTests` 40, `RecordSearchRowAuthorizationContractTests` 1 — all pass unchanged (96 total).
- BFF build: succeeded, 0 warnings. `check-changed-cs-format.sh origin/master`: clean.
- Publish-size delta / CVE: not applicable — no change under `src/server/api/Sprk.Bff.Api/` and no package change (ADR-029).

## 5. Review findings (Step 9.5, inline)

- Fixed: the test mocked `IHttpContextAccessor`; replaced with the real `HttpContextAccessor` (ADR-038 B5).
- **Out of scope, F2 — to file (main session):** stale comments contradict the code on where tenant isolation lives. `IRecordSearchService.cs:27-28` and `RecordSearchService.cs:55-56, :67-70, :128-129, :188-189, :238-239` say the records index has no `tenantId` field and isolation is Dataverse-only; but `BuildRecordFilter` always applies `tenantId eq '…'` (FR-12) and `infrastructure/ai-search/spaarke-records-index.json` defines `tenantId`. Failure scenario: a maintainer trusting the comments removes or bypasses the filter as "cache-only", reopening cross-tenant reads at the search layer. Not fixed here because the POML limits `RecordSearchService.cs` edits to a failed proof. Not added to `notes/defer-issues.md` to avoid parallel-wave conflicts; the main session files issue + defer entry together.
- K4 (known limit): `search.in(recordType, '…', ',')` splits on `,`, so a record type containing a comma would match several types. Unreachable: the endpoint allow-lists `RecordTypes` and internal callers pass `RecordEntityType.ValidTypes`.
- adr-check: ADR-038 (KEEP path, boundary-only doubles, naming, no reflection) pass; ADR-015 (no query text logged; `:183` stays length-only) pass; ADR-029 N/A (no BFF change).

## 6. Pending

- Verifier passes: 0 run here (main session runs two — security tag).
- review pending: unified-access-control-r2 (caller-path diff review per project CLAUDE.md §4).

rework: none task 009

## Adversarial verifier pass 1 (main session, 2026-11-25)
Proof holds: `request.Query` reaches only `searchText` and the embedding input; every filter value is tenant / allow-listed / escaped. Fix-now findings next to the proof (follow-up implementer):
- F1: cache lookup runs before the explicit-index allow-list check, and the cache id omits `SearchIndexName` and `PreferKeywordRanking` with ambiguous joins → a not-allowed index can be served cached default-index results (rows still per-row trimmed).
- F1 (low): request `[StringLength]`/`[Range]` never enforced; `limit:0`/`offset:-1` → 500 with raw `ex.Message` as detail.
- F2: stale "no tenantId field" comment in `AiAnalysisNodeExecutor.cs:1152-1155`; "per-row checks by the endpoint" is REST-only.
- K4: add proof rows for the explicit-index, no-user fallback and `PreferKeywordRanking` paths.
- K4 / obligation for task 022: `search.in` delimiter unescaped — the tool must reuse `RecordEntityType.IsValid` (or move the allow-list into the service). K2: record-type casing mismatch fails closed.
- POML AC1 wording: vector-only sends `"*"`, not null (no query text either way).
