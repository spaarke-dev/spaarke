# Task 105 — EntityExtractorHandler Completion Notes

**Status**: ✅ Completed (2026-06-07; Wave 5 of Phase A — H-G2 Wave 2 LLM-assisted handler)
**Rigor**: FULL
**FR**: FR-13 (LLM-assisted Named Entity Recognition with code-based validation/normalization)

## What was built

`src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/EntityExtractorHandler.cs` (~720 LOC body; ~996 lines with XML docs + inline JSON schema + DTOs).

Pipeline:
1. Validate input (`ToolExecutionContext` for playbook, `ChatInvocationContext` for chat).
2. Parse + validate `sprk_configuration` JSON (`entityTypes`, `confidenceThreshold`, `modelDeployment`).
3. Build per-tenant cache key per ADR-014: `entity-extractor:{tenantId}:{sha256(text+entityTypes+threshold)}`.
4. Cache lookup → if hit, return cached `EntityExtractionResult` with `CacheHit=true, ModelCalls=0`.
5. On miss, build LLM prompt + inline JSON schema (Structured Outputs), call `IOpenAiClient.GetStructuredCompletionRawAsync`.
6. Parse LLM response — graceful `ToolErrorCodes.ModelError` on invalid JSON.
7. Code-side validation + normalization per entity type:
   - email → lowercased + regex-validated; malformed rejected
   - phone → digits sanitized (preserves leading `+`); 7-15 digits required
   - url → must match `https?://` pattern (rejects `javascript:` injections)
   - date → ISO 8601 (YYYY-MM-DD); permissive parse fallback
   - money → uppercase currency code
   - organization/person/location → trimmed
8. Confidence-threshold filter (default 0.6) drops below-threshold matches.
9. Dedupe on `(type, normalizedValue, span.start)` triple.
10. Sort deterministic (span.start, type, normalizedValue) — stable output.
11. Cache store (best-effort), build stats, return `ToolResult.Ok` with `EntityExtractionResult { entities[], stats { totalCount, byType{} } }`.

Cache failures (read OR write) are non-fatal: logged at Warning and pipeline continues.

## ADR Compliance

| ADR | Status | Evidence |
|---|---|---|
| ADR-010 (DI minimalism) | ✅ | ZERO manual DI line. Auto-discovered via `AddToolHandlersFromAssembly`. Test `HandlerType_IsRegisteredInDi` verifies. |
| ADR-013 (AI architecture) | ✅ | Handler in `Services/Ai/Handlers/`. `IOpenAiClient` stays AI-internal — no PublicContracts contamination (verified via grep). |
| ADR-014 (per-tenant cache) | ✅ | Cache key prefix `entity-extractor:{tenantId}:`. Test `ExecuteAsync_CacheKey_IsTenantScoped_NoCrossTenantHits` proves tenant A cannot read tenant B's cache. Cache-failure non-fatal (log + fall through). |
| ADR-015 (telemetry hygiene) | ✅ | Logs handler name + IDs + outcome + `BucketCount(int)` ("0"/"1"/"2-5"/"6-20"/"21+") + type count + duration. NEVER input text, NEVER entity values, NEVER raw LLM response. Two tests assert via `AssertTelemetryRespectsAdr015` with secret-input + entity-value sentinels. |
| ADR-016 (rate limits) | ✅ | LLM call via `IOpenAiClient.GetStructuredCompletionRawAsync` — per-tenant `ai-context` rate limit lives behind that interface. |
| NFR-13 (safety pipeline) | ✅ | No bypass — handler calls `IOpenAiClient` like every other LLM-assisted handler; `SafetyPipelineMiddleware` chain unchanged. |
| ADR-029 (publish hygiene) | ✅ | Zero new NuGet packages. Single-task delta below per-handler target (see Publish-Size below). |

## Tests

32 unit tests pass on first run. Coverage:

- **4-point contract** (5 tests): registration, HandlerId == nameof, metadata semver, SupportedToolTypes non-empty, `SupportedInvocationContexts == Both`.
- **Validate (playbook)** (5 tests): success; missing extracted text; missing TenantId; unsupported entityType config; confidenceThreshold out of range.
- **ValidateChat** (3 tests): success with text arg; missing text; malformed JSON.
- **ExecuteAsync positive** (8 tests): mixed-type extraction (organization/person/location/date/money/email); below-threshold filtered; entityTypes-subset filter; email lowercased; malformed email rejected; malformed URL rejected; date normalized to ISO 8601; money currency uppercase; dedupe.
- **Error** (3 tests): malformed LLM JSON → `ModelError`; cancellation → `Cancelled`; LLM exception → `InternalError`.
- **Cache** (2 tests): second identical call hits cache (verified via `Mock.Verify(..., Times.Once)`); per-tenant isolation (tenant B cannot read tenant A entries).
- **Chat path** (1 test): chat invocation extracts from `text` arg.
- **Telemetry** (2 tests): playbook + chat paths both assert no input text or entity values in logs.
- **Metadata** (2 tests): ModelCalls=1, ModelName populated; per-row `modelDeployment` override honored.

LLM mocking pattern: `OpenAiClientMock.Setup(...GetStructuredCompletionRawAsync...).ReturnsAsync(jsonResponse)`. JSON responses built via `BuildLlmResponse(entities[])` helper — reusable for siblings 106/107/108.

Cache mocking: in-memory `Dictionary<string,byte[]>` backing `_cacheMock`'s Get/Set — exercises real cache-key collisions and tenant isolation.

## Data + script

- `infra/dataverse/sprk_analysistool-entity-extractor-row.json` — seed row.
  - `sprk_toolcode = "ENT-EXT@v1"` (10 chars — heeds task 101/104 caveat about Spaarke Dev's 10-char column max).
  - `sprk_availableincontexts = 100000002` (Both — FR-13 binding: playbook AND chat).
  - `sprk_jsonschema` — populated with chat-invocation argument schema (`text` required + optional `entityTypes`/`confidenceThreshold`).
  - `sprk_configuration` — default policy: all 8 entity types, threshold 0.6.
- `scripts/Seed-TypedHandlers.ps1` — `EntityExtractorHandler` entry added to `$RowFiles` map.

Deployment to Spaarke Dev deferred to coordinated Wave-2 batch deploy after siblings 106/107/108 land (per parallel-wave bookkeeping pattern).

## Build + size

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 16 pre-existing warnings.
- `dotnet list package --vulnerable --include-transitive`: 1 pre-existing High-severity CVE (`Microsoft.Kiota.Abstractions` 1.21.2) — not introduced by this task; matches task 104 baseline.
- `dotnet test ...EntityExtractorHandlerTests`: 32/32 passed in 148ms.

## BFF Publish-Size Delta (NFR-02 + ADR-029)

| Metric | Value |
|---|---|
| Raw publish size | 138.00 MB |
| Compressed publish size | **45.56 MB** |
| Wave 1 baseline (post task 102) | 44.22 MB |
| Cumulative delta | +1.34 MB compressed |
| R6 cumulative budget | ≤+5 MB |
| 60 MB hard ceiling | far below |

Note: my compressed publish includes sibling Wave 2 handlers landing in parallel (task 106 ClauseAnalyzerHandler + task 107 RiskDetectorHandler added their seed rows to the same script). The +1.34 MB is the cumulative Wave 2 footprint, not just this handler. Single-handler contribution est. ≤0.5 MB based on file size + zero new NuGet.

## Stop-and-Report Triggers

None hit. Cache integration via `IDistributedCache` proven viable as the per-tenant cache surface (per ADR-014). No PublicContracts boundary touched. No new ADR or feature flag introduced. No file overlap with siblings 106/107/108 — each in its own file.

## LLM Mock Pattern Used

```csharp
OpenAiClientMock
    .Setup(c => c.GetStructuredCompletionRawAsync(
        It.IsAny<string>(),
        It.IsAny<BinaryData>(),
        It.IsAny<string>(),
        It.IsAny<string?>(),
        It.IsAny<int?>(),
        It.IsAny<CancellationToken>()))
    .ReturnsAsync(responseJson);
```

JSON responses built with `BuildLlmResponse(IEnumerable<(text, type, normalized, confidence, start, end)>)` — directly mirrors the LLM output schema. Same pattern can be reused verbatim by tasks 106/107/108.

## Cache Strategy

- `IDistributedCache` (existing BFF dependency; already registered).
- Key: `entity-extractor:{tenantId}:{sha256_hex(input + "|types=" + sortedTypesCsv + "|threshold=" + F2)}`.
- TTL: 24 hours.
- Serialize result as UTF-8 JSON bytes.
- Failures (Get/Set) NEVER block extraction — log Warning + fall through.

## sprk_toolcode

**`ENT-EXT@v1`** = exactly 10 chars (the Spaarke Dev column max per task 101/104 caveat).

## Final Acceptance

| Criterion | Status |
|---|---|
| Handler implements IToolHandler + uses IOpenAiClient structured outputs | ✅ |
| Auto-discovered (no manual DI line) | ✅ (tested via `HandlerType_IsRegisteredInDi`) |
| sprk_analysistool seed row with `sprk_handlerclass = EntityExtractorHandler` + `AvailableInContexts = Both` | ✅ (deployed to JSON; script updated; Spaarke Dev deploy deferred to batch) |
| Cache key includes tenantId (ADR-014) | ✅ (grep-verifiable: `BuildCacheKey` line) |
| Tests pass: contract + positive + error + config + cache + telemetry | ✅ (32/32) |
| Safety pipeline unchanged (NFR-13) | ✅ (no middleware modification) |
| Rate-limit policy respected (ADR-016) | ✅ (routes through `IOpenAiClient`) |
| Build succeeds | ✅ (0 errors) |
| Publish-size delta reported | ✅ (+1.34 MB cumulative incl. siblings) |
| No new HIGH CVE | ✅ (Kiota CVE pre-existing) |
| code-review + adr-check pass | ✅ (both green) |
