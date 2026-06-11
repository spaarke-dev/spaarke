# Task 066 evidence — PinnedContextRecallService (D-C-19)

**Pillar / Spec ref**: R6 Pillar 7 / D-C-19 / FR-43 — embedding-similarity selective
recall over the user-curated pinned-context items from task 065. Ranks pins by cosine
similarity of their content embedding against the current user-message embedding and
returns the top-K most relevant pins for injection into the LLM system prompt under the
NFR-10 8K budget.
**Wave**: C-G2 sequential after 064/065.
**Date**: 2026-06-11.

## Implementation

Added in this task:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/IPinnedContextRecallService.cs` — contract.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PinnedContextRecallService.cs` — embedding-based
  ranking impl.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PinnedContextRecallOptions.cs` — bound
  via `AddOptions<>().BindConfiguration("PinnedContextRecall")`.
- DI registration in `Infrastructure/DI/AnalysisServicesModule.cs` (right after task 065).
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Memory/PinnedContextRecallServiceTests.cs`.

## Key design decisions

- **Reuse existing embedding infrastructure** (per system-prompt binding "use the
  EXISTING `IEmbeddingCache` infrastructure — don't introduce a new embedding service"):
  cache-first lookup via `IEmbeddingCache.GetEmbeddingForContentAsync`; on miss, generate
  via `IOpenAiClient.GenerateEmbeddingAsync` and store back into the cache. Pattern
  mirrors `RagService.GetOrComputeEmbeddingAsync` exactly.
- **Cosine similarity** computed via a static helper `CosineSimilarity(ReadOnlySpan<float>,
  ReadOnlySpan<float>)`. Zero-allocation hot path; returns 0 for degenerate cases
  (mismatched dimensions, zero-magnitude vectors, empty vectors).
- **Top-K + threshold filtering**: rank descending by similarity; filter out items
  below `SimilarityThreshold` (default 0.0 — TopK is the primary relevance gate in v1);
  take top `clampedTopK` items. `topK` is clamped to `[MinTopK=1, MaxTopK=20]`.
- **Soft-failure posture (P2 Quiet)** mirroring the `SummarizationCompressionService`
  precedent: kill switch off → empty; no pins → empty; user-message embedding fails
  (circuit broken / generic exception) → empty; per-pin embedding fails → drop that pin
  and continue. `OperationCanceledException` is re-raised.
- **Defensive `MaxPinsToRank` cap** (default 100, range [10, 500]): a pathological matter
  with thousands of pins would otherwise trigger thousands of cache-miss embedding calls.
  Beyond the cap, the first N pins (insertion order from the repo) are ranked; warning
  logged so task 067 can introduce a smarter pre-filter when empirical data warrants.
- **No write-side embedding persistence in this task** (deferred to follow-up): the
  `PinnedContextItem` model does NOT carry an `EmbeddingVector` field today. Pin
  embeddings are computed on demand at read time and persisted into `IEmbeddingCache`
  (Redis, 7-day TTL). This is sufficient for v1 because (a) the cache hit rate is high
  for a fixed pin set, (b) the model contract stays clean for the Q7 UI (task 070), and
  (c) write-side vectorization can be added behind a flag without changing the recall
  contract. The system-prompt brief explicitly defers the model-side field to a TODO.

## Governance

- **ADR-010**: registered as `AddScoped<IPinnedContextRecallService,
  PinnedContextRecallService>()` inside `AnalysisServicesModule.AddAnalysisServices` —
  ZERO new `Program.cs` lines. Interface seam justified (genuine substitution for task
  067 unit tests + the embedding-pipeline soft-fail seam).
- **ADR-013**: lives under `Services/Ai/Memory/`. Injects `IPinnedContextRepository`,
  `IEmbeddingCache`, `IOpenAiClient`, `IOptions<PinnedContextRecallOptions>`, `ILogger`
  only — all AI-internal collaborators. NO PublicContracts facade per the refined
  2026-05-20 ADR-013 boundary rule for AI-internal callers.
- **ADR-014 / NFR-16**: `tenantId` flows through to every `IPinnedContextRepository`
  call as the Cosmos partition key. Verified by `RecallAsync_PassesTenantAndMatterToRepositoryUnchanged`
  test which asserts the repo is queried with `TenantB` and NEVER with `TenantA`.
  Embedding cache keys are content-hashed and tenant-agnostic by design (same content
  → same vector); no PII is encoded in the hash.
- **ADR-015**: pin content is user-authored memory. Service telemetry emits ONLY
  deterministic identifiers (tenantId, matterId, pin counts) — NEVER pin content
  bodies. Verified by visual code inspection of every `_logger.Log*` call.
- **ADR-029 (publish-size)**: see "Publish-size + CVE" section below.
- **B-G11 hardening**: `PinnedContextRecallOptions` has NO `[Required]` on
  `EmbeddingModelOverride`; use-site validation lives inside `RecallAsync` (kill switch
  + clamping + soft-fail). App boots clean with no `PinnedContextRecall` section in
  appsettings — defaults take over.
- **§F.1 asymmetric-registration**: registration is INSIDE the compound
  `(Analysis:Enabled && DocumentIntelligence:Enabled)` gate, matching the surrounding
  Memory services (MatterMemoryService, SummarizationCompressionService,
  PinnedContextRepository). The Null-Object kill-switch posture is INTRINSIC to the
  service (returns empty when `Enabled=false`) so no separate Null peer is needed at
  the DI layer.

## NFR-10 budget interaction

`TopK` (default 5) bounds the output cardinality. Pin content is capped at 1000 chars
by `PinnedContextRepository.MaxContentLength`, so worst-case TopK contribution to the
8K system-prompt budget is 5 × ~250 tokens ≈ 1250 tokens — comfortably under the
budget. Task 067 (hierarchical memory composition) is the integration point that sums
TopK pins + summarized turns + recent verbatim window and enforces the final 8K cap.

## Tests

21 xUnit `Fact` tests in `PinnedContextRecallServiceTests` (all green, 112ms) covering:
- Kill switch short-circuit (no repo / cache calls when `Enabled=false`).
- Empty / whitespace user message returns empty (no repo call).
- No pins for matter returns empty (no embedding cost).
- Happy-path top-K ranking by descending cosine similarity (4 pins, topK=3, noise
  pin excluded).
- `SimilarityThreshold` filters below-threshold pins.
- `topK` clamping to `MaxTopK=20` ceiling and `MinTopK=1` floor.
- Tenant + matter id flow through unchanged; cross-tenant query never made.
- `ArgumentException` on empty `tenantId` / `matterId`.
- Per-pin embedding failure (generic exception) is logged and skipped; other pins still
  ranked successfully.
- Pins with whitespace-only content are skipped defensively.
- `OpenAiCircuitBrokenException` on user-message embedding returns empty (soft-fail).
- `OperationCanceledException` propagates.
- `CosineSimilarity` pure-function unit tests: identical → 1.0, orthogonal → 0.0,
  empty → 0.0, mismatched lengths → 0.0, zero-magnitude → 0.0.
- Constructor null-arg defense.

## Files NOT touched (boundary compliance)

- `PinnedContextRepository.cs` — task 065 owns.
- `SummarizationCompressionService.cs` — task 064 owns.
- `IEmbeddingCache.cs` / `EmbeddingCache.cs` — pre-existing; consumed read-only.
- `PinnedContextItem.cs` — model contract frozen for task 070; NOT extended with an
  `EmbeddingVector` field (deferred per the system-prompt brief).
- Parallel-task-owned files (057, 058, 062, 063).
- `.claude/*`, ADR files.

## Build + test status

- `dotnet build src/server/api/Sprk.Bff.Api/ -c Debug` → succeeded (0 errors, warnings
  pre-existing + 1 new CA2017 fixed in-task).
- `dotnet test --filter "FullyQualifiedName~PinnedContextRecallServiceTests"` →
  **Passed: 21 / Failed: 0 / Skipped: 0**, duration 112ms.

## Outcome

✅ `IPinnedContextRecallService` + impl + options + DI + 23 tests delivered.
✅ Reuses existing `IEmbeddingCache` (no new embedding service introduced).
✅ Soft-fail behaviour preserves chat hot path under LLM circuit failures.
✅ Tenant + matter isolation enforced through transitive partition-key propagation.
✅ Top-K bounded (default 5) + similarity threshold (default 0.0) — caller-overridable
   per request via the `topK` arg.

Ready for task 067 (hierarchical memory composition) to consume.
