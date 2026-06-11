# Placement Justification — Task 102 (BFF lookup-first resolver + DataverseAllowedIndexesProvider)

> **Binding per** [`.claude/constraints/bff-extensions.md`](../../../../.claude/constraints/bff-extensions.md)
> **Task**: `projects/spaarke-multi-container-multi-index-r1/tasks/102-bff-lookup-resolver-and-allowlist-provider.poml`
> **Date**: 2026-06-10
> **Author**: BFF developer (sub-agent under task-execute / FULL rigor)

---

## What changed in this task

1. **Refactored** `Services/Ai/SearchIndexNameResolver.cs` to use a single FetchXml `link-entity` outer-join per step (doc → parent → BU), reading the new `sprk_ai_search_index` lookup AND the legacy `sprk_searchindexname` text column in one round trip. The text column is retained as a migration-safety fallback (logged as `PhaseG.TextFallback` for App Insights soak verification).
2. **Created** `Services/Ai/IAllowedIndexesProvider.cs` and `Services/Ai/DataverseAllowedIndexesProvider.cs` — caches the active `sprk_aisearchindex` rows in `IMemoryCache` (5-min TTL, single key `sprk_aisearchindex:active`) and falls back to appsettings `AllowedIndexes` on Dataverse failure / empty result (with single WARNING log per TTL cycle).
3. **Updated** `Services/Ai/KnowledgeDeploymentService.cs` to accept an optional `IAllowedIndexesProvider` (constructor parameter is nullable for back-compat); when present, the provider is consulted; otherwise the legacy appsettings array is used directly (test fixtures + AI-OFF DI graph unchanged). ProblemDetails 400 `INDEX_NOT_ALLOWED` wire shape unchanged.
4. **Extended** `Spaarke.Dataverse.IGenericEntityService` with a new `RetrieveMultipleAsync(FetchExpression, CancellationToken)` overload (implemented in `DataverseServiceClientImpl`; `DataverseWebApiService` throws `NotImplementedException` per existing pattern). Required by both consumers above.
5. **Registered** `IAllowedIndexesProvider → DataverseAllowedIndexesProvider` as a Singleton at the top of `Infrastructure/DI/AnalysisServicesModule.cs`, alongside the existing `ISearchIndexNameResolver` registration (TRULY UNCONDITIONAL — symmetric with the resolver).

---

## In-BFF placement (decision criteria)

| Question (from `bff-extensions.md`) | Answer | Rationale |
|---|---|---|
| Latency / TTFB budget against BFF state? | YES | The resolver is called inline on every background indexing job and on every chat / RAG request via `KnowledgeDeploymentService`. Sub-100ms p95 is the implicit budget. |
| Writes to BFF-managed session/audit/safety state in same request lifecycle? | YES | The allow-list provider validates the per-request `searchIndexName` before binding a `SearchClient`; rejection emits ProblemDetails 400 inline. |
| Retroactive annotation of a streaming response? | N/A | These are validation + lookup primitives, not streaming surfaces. |
| Event-driven (timer / queue / webhook) with no synchronous user wait? | NO | Both code paths are synchronous to caller's request. |
| Thin facade exposing capabilities to EXTERNAL consumers? | NO | These types are internal AI infrastructure. |

**Verdict**: In-BFF. Both files live in `Services/Ai/` per refined ADR-013 (AI internals stay in BFF).

---

## No facade extraction needed (ADR-013 §3.5)

The resolver + allow-list provider are **AI-internal**. Their only consumers are also `Services/Ai/`:

- `SearchIndexNameResolver` ← `RagIndexingJobHandler`, `BulkRagIndexingJobHandler`, `IndexingWorkerHostedService` (all under `Services/Ai/Jobs/` post-Outcome E reorganization)
- `IAllowedIndexesProvider` ← `KnowledgeDeploymentService` (`Services/Ai/`)

No external CRUD consumers, so no facade in `Services/Ai/PublicContracts/` is needed (the facade pattern in `bff-extensions.md` §A.4 applies to **CRUD → AI** crossings; this is **AI → AI**).

---

## DI registration

| Component | Lifetime | Where registered | Notes |
|---|---|---|---|
| `ISearchIndexNameResolver → SearchIndexNameResolver` | Scoped | `AnalysisServicesModule.cs` (top, unconditional) | Unchanged from prior task. |
| `IAllowedIndexesProvider → DataverseAllowedIndexesProvider` | **Singleton** | `AnalysisServicesModule.cs` (top, unconditional) | **NEW**. Singleton because cache state is process-wide; uses `IServiceProvider.CreateScope` per cache-miss load to consume scoped `IGenericEntityService` (no captive dependency). |
| `IMemoryCache` | Singleton | `CacheModule.cs` (existing, line 68) | Reused; no new registration. |
| `IOptions<AiSearchOptions>` | Singleton | `JobProcessingModule.cs` (existing) | Reused; no new binding. |

**Symmetric to** `ISearchIndexNameResolver` placement — both Phase G primitives live at the top of `AnalysisServicesModule.AddAnalysisServicesModule` (above the documentIntelligence/analysis conditionals) for the same reason: their consumers may resolve on either AI-ON or AI-OFF paths, and the provider has zero AI dependencies (only `IGenericEntityService` + `IMemoryCache`, both unconditional).

---

## NuGet package impact: zero

No new packages added. All types used already exist in the BFF dependency graph:

- `Microsoft.Extensions.Caching.Memory.IMemoryCache` — already in BFF
- `Microsoft.Xrm.Sdk.FetchExpression` — already in `Spaarke.Dataverse` chain
- `Microsoft.Xrm.Sdk.AliasedValue` — already in `Spaarke.Dataverse` chain

---

## Publish-size impact

**Expected**: ≤ +0.1 MB (logic-only additions to existing assembly).

**Measured**: see task 102 final report below. Baseline ~45.65 MB; target ceiling 60 MB compressed (NFR-01).

---

## §F.1 Asymmetric-Registration Tier 1.5 anti-pattern check

**Pattern (per `bff-extensions.md` §F.1)**: For every new service registration added to a `*Module.cs` file inside an `if (flag) { ... }` block, verify all endpoint handlers + consumers can resolve on the OFF path (apply ADR-030 Null-Object Kill-Switch if conditional).

**This task's registrations are NOT inside `if (flag)` blocks** — they are at the top of `AnalysisServicesModule.AddAnalysisServicesModule`, unconditional. The anti-pattern does NOT apply.

**Cross-check on `KnowledgeDeploymentService` consumer**: `KnowledgeDeploymentService` is registered behind the AI-Search-keys sub-gate in `AddRagServices` (compound-AI-ON only). The new optional ctor param `IAllowedIndexesProvider? allowedIndexesProvider = null` defaults to null, so when `KnowledgeDeploymentService` is NOT registered (AI-OFF path), there is nothing to resolve — symmetric with the existing `IOptions<AiSearchOptions>?` pattern. When `KnowledgeDeploymentService` IS registered (AI-ON), DI will inject the (unconditionally-registered) `IAllowedIndexesProvider`.

**Static-scan recipe verified**:
```bash
# Find consumers of IAllowedIndexesProvider:
rg -t cs -n "IAllowedIndexesProvider" src/server/api/Sprk.Bff.Api/
# Only KnowledgeDeploymentService (constructor param) — registered in AddRagServices behind compound gate.
# IAllowedIndexesProvider itself is registered UNCONDITIONALLY at module top.
# No asymmetry.
```

---

## Test update obligation (§F per `bff-extensions.md`)

- **Rewritten**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/SearchIndexNameResolverTests.cs` — 9 tests covering the new FetchXml path: lookup wins, text-fallback logs warning, chain progression (doc → parent → BU), exception fall-through, malformed GUIDs, lookup-overrides-text precedence.
- **Added**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/DataverseAllowedIndexesProviderTests.cs` — 9 tests covering: lookup match / miss, case-insensitive, cache (100 calls → 1 fetch), Dataverse-empty fallback + warning, Dataverse-exception fallback + warning, null/empty input short-circuit, blank-name defensive handling.
- **Existing**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/KnowledgeDeploymentServiceTests.cs` — UNCHANGED (the new ctor param is optional and defaults to null; legacy appsettings path is preserved exactly). All 16 existing tests (incl. the FR-BFF-01..04 explicit-indexName + allow-list suite) continue to pass.

---

## ADR alignment

| ADR | Applied? | Notes |
|---|---|---|
| ADR-001 (Minimal API) | ✅ | No new endpoints; service layer only. |
| ADR-010 (DI minimalism) | ✅ | Two new types, one new interface; both registered in feature module (`AnalysisServicesModule`); Singleton for provider, Scoped for resolver. |
| ADR-013 (AI architecture, refined 2026-05-20) | ✅ | Both files in `Services/Ai/`; no facade needed (AI-internal). |
| ADR-019 (ProblemDetails) | ✅ | `INDEX_NOT_ALLOWED` 400 shape unchanged. |
| ADR-029 (BFF publish hygiene) | ✅ | Zero new packages; expected <0.1 MB delta. |

---

## §F.4 Deploy coordination

This task does NOT deploy. Task 103 handles the BFF deploy (per Phase G plan) and runs the App Insights verification.

---

*Generated by task-execute (FULL rigor) for task 102.*
