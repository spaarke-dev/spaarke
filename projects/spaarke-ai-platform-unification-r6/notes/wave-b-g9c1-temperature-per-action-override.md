# Hotfix Wave B-G9c1 — Per-Action Temperature Override (B6)

**Date**: 2026-06-10
**Bug**: B6 — same-file uploaded twice produced materially different summaries
**Status**: SUCCESS — schema applied + BFF code updated + tests added + build/tests green

---

## 1. Decision recap

Per user direction:

> **Per-action override**: Add `sprk_temperature` column on `sprk_analysisaction` Dataverse entity. Default = 0 (deterministic). Per-action override allowed. Handlers read the action's temperature instead of hardcoding.

NULL semantics: missing column / NULL value → handlers default to `0.0` (matches sibling structured methods' hardcoded Temperature=0).

---

## 2. Schema change applied

### Script

- **NEW**: `scripts/Add-AnalysisActionTemperature.ps1`
  - Modeled on `scripts/Add-AnalysisToolRequiredCapability.ps1` (Wave 7b)
  - Idempotent (checks `Test-AttributeExists` before adding)
  - Adds `sprk_temperature` Decimal column to `sprk_analysisaction`:
    - **Type**: `DecimalAttributeMetadata`
    - **Precision**: 1 (matches Azure OpenAI's documented temperature granularity)
    - **MinValue**: 0.0
    - **MaxValue**: 2.0 (Azure OpenAI valid range)
    - **RequiredLevel**: None (nullable)

### Execution evidence (Spaarke Dev, 2026-06-10)

```
Step 1: Verifying sprk_analysisaction entity exists...
  sprk_analysisaction found

Step 2: Adding/verifying sprk_temperature column...
  Adding attribute: sprk_temperature...
    Added: sprk_temperature (Decimal, Precision=1, Range 0.0-2.0)

Step 3: Publishing customizations...
  Customizations published
```

### Manual-run command (for other environments)

```powershell
# Dry-run first (recommended)
.\scripts\Add-AnalysisActionTemperature.ps1 -EnvironmentUrl "https://<env>.crm.dynamics.com" -DryRun

# Deploy
.\scripts\Add-AnalysisActionTemperature.ps1 -EnvironmentUrl "https://<env>.crm.dynamics.com"
```

---

## 3. DTO + mapper updates

### `AnalysisAction` record (DTO)

- `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs`:
  - Added `public decimal? Temperature { get; init; }` to `AnalysisAction` record (line ~626).
  - Null = use deterministic 0.0 downstream.

### `AnalysisActionService` (mapper)

- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisActionService.cs`:
  - `GetActionAsync`: added `sprk_temperature` to `$select` (line ~33).
  - `ListActionsAsync`: added `sprk_temperature` to `$select` (line ~102).
  - `CreateActionAsync` / `GetActionAsync` / `ListActionsAsync` mappers: populate `Temperature = entity.Temperature`.
  - Private `ActionEntity` DTO: added `[JsonPropertyName("sprk_temperature")] public decimal? Temperature { get; set; }`.

### `ToolInvocationContextBase` default

- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolInvocationContextBase.cs`:
  - Changed `public double Temperature { get; init; }` default from `0.3` → `0.0`.
  - Per ADRs-as-defaults principle: surfaced this as an intentional alignment with the sibling structured-output methods (both pin Temperature=0). This default change affects `ToolExecutionContext` and `ChatInvocationContext`.

### `AiAnalysisNodeExecutor` (per-action override flow)

- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs`:
  - `BuildToolExecutionContextAsync` (line ~384): reads `context.Action.Temperature` and sets `ToolExecutionContext.Temperature = action.Temperature ?? 0.0`. Previously sourced from `NodeExecutionContext.Temperature` (default 0.3).

---

## 4. OpenAI client method signature

### `IOpenAiClient.GetStructuredCompletionRawAsync`

- `src/server/api/Sprk.Bff.Api/Services/Ai/IOpenAiClient.cs` (line ~159):
  - Added `float? temperature = null` parameter (positionally before `cancellationToken`).
  - Docstring explains: null = `0.0f` deterministic default (matches sibling structured methods); explicit value flows from per-action override.

### `OpenAiClient.GetStructuredCompletionRawAsync`

- `src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs` (line ~706):
  - Implementation: `var effectiveTemperature = temperature ?? 0.0f;` then `Temperature = effectiveTemperature` on `ChatCompletionOptions`.
  - Removed the previous `_options.Temperature` reference (line ~724).

---

## 5. Per-handler change list (8 handlers)

All 8 handlers now read `(float)context.Temperature` and pass it through. Where the LLM call was wrapped in a `private async Task<ToolResult> ...AndReturnAsync(...)` helper, a `float temperature` parameter was added to the helper and the LLM call.

| # | Handler | File | LLM-call line | Helper updated? |
|---|---|---|---|---|
| 1 | SummaryHandler | `Services/Ai/Tools/SummaryHandler.cs` | ~377 | n/a (in-method) |
| 2 | SemanticSearchToolHandler | `Services/Ai/Tools/SemanticSearchToolHandler.cs` | ~354 | n/a (in-method) |
| 3 | DocumentClassifierHandler | `Services/Ai/Tools/DocumentClassifierHandler.cs` | ~412 | n/a (in-method) |
| 4 | EntityExtractorHandler | `Services/Ai/Handlers/EntityExtractorHandler.cs` | ~389 | yes — `ExtractAndReturnAsync` got `float temperature` |
| 5 | ClauseAnalyzerHandler | `Services/Ai/Handlers/ClauseAnalyzerHandler.cs` | ~399 | yes — `AnalyzeAndReturnAsync` got `float temperature` |
| 6 | RiskDetectorHandler | `Services/Ai/Handlers/RiskDetectorHandler.cs` | ~457 | yes — `DetectAndReturnAsync` got `float temperature` |
| 7 | GenericAnalysisHandler | `Services/Ai/Handlers/GenericAnalysisHandler.cs` | ~298 + ~474 | n/a (in-method; covers async + streaming overloads) |
| 8 | InvoiceExtractionToolHandler | `Services/Ai/Handlers/InvoiceExtractionToolHandler.cs` | ~400 | yes — `ExecuteCoreAsync` got `float temperature` |

The chat path (`ChatInvocationContext`) also forwards `(float)context.Temperature` — defaults to 0.0 from the base type since chat-side has no per-action context.

---

## 6. Tests added

### NEW test files

1. **`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/GetStructuredCompletionRawAsyncTemperatureTests.cs`**
   - Reflection-based interface contract: asserts `temperature` param exists, is `float?`, defaults to `null`, precedes `cancellationToken`.
   - Functional WireMock-based integration tests were attempted but blocked by the lack of a System.ClientModel pipeline mock in the BFF test scaffolding (Azure SDK's request validation prevents naive HTTP-layer mocking). The handler tests below cover functional pass-through.

2. **`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/HandlerTemperaturePassThroughTests.cs`**
   - Two handler coverage (EntityExtractorHandler + RiskDetectorHandler), three temperature values each (0.0 / 0.5 or 0.4 / 0.7 or 1.0):
     - Moq `Callback<>` captures the `temperature` arg actually passed to `IOpenAiClient.GetStructuredCompletionRawAsync`.
     - Assert matches `context.Temperature` (set via the fixture's new `temperature` parameter).
   - Same change is symmetric across the other 6 handlers — repeating the test per-handler would be no-value duplication.

### Fixture extension

- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/TypedToolHandlerTestFixture.cs`:
  - `BuildToolExecutionContext`: added `double? temperature = null` parameter; uses `record with`-expression to set when supplied.

### Existing test updates

- **18 existing test Moq Setup/Verify calls** updated to add `It.IsAny<float?>()` for the new positional parameter:
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/EntityExtractorHandlerTests.cs` (5 occurrences)
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/ClauseAnalyzerHandlerTests.cs` (11 occurrences)
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/RiskDetectorHandlerTests.cs` (5 occurrences)
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/InvoiceExtractionToolHandlerTests.cs` (3 occurrences)
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/HandlerDispatchTests.cs` (1 occurrence)
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/Routing/InsightsIntentClassifierTests.cs` (15 occurrences)
- **2 manual `IOpenAiClient` test implementations** updated to add the new parameter to their declaration:
  - `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/SummarizeSessionEndpointTests.cs`
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookExecutionEngineTests.cs`
- **`InvocationContextTests.cs`**: the pre-R6 contract test asserting `Temperature == 0.3` was split into two tests:
  - `ToolExecutionContext_DefaultMaxTokens_MatchesPreR6Contract` (MaxTokens=4096 — unchanged)
  - `ToolExecutionContext_DefaultTemperature_IsZero_PostHotfixBG9c1` (Temperature=0.0 — new expected value per hotfix)

---

## 7. Build + test verification

### BFF build (`dotnet build src/server/api/Sprk.Bff.Api/`)

```
Build succeeded.
    16 Warning(s)  (pre-existing; unchanged by this hotfix)
    0 Error(s)
Time Elapsed 00:00:06.64
```

### Test build (`dotnet build tests/unit/Sprk.Bff.Api.Tests/`)

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:07.12
```

### Test run (filtered: Handler / Temperature / Insights / Playbook / InvocationContext / GetStructuredCompletion)

Final summary (post all fixes):

```
Passed!  - Failed:     0, Passed:   938, Skipped:     1, Total:   939, Duration: 15 s
```

The single skip is a pre-existing, environment-conditional skip in `SummaryHandlerTests`
(unrelated to this hotfix).

---

## 8. Commit-message paragraph

```
fix(r6 G9c1-B6): per-action temperature override on sprk_analysisaction

Adds sprk_temperature Decimal column (0.0–2.0, nullable, Precision=1) to
sprk_analysisaction. Plumbs through AnalysisAction.Temperature →
ToolExecutionContext.Temperature → 8 tool handlers
(Summary, SemanticSearch, DocumentClassifier, EntityExtractor, ClauseAnalyzer,
RiskDetector, GenericAnalysis, InvoiceExtraction) →
IOpenAiClient.GetStructuredCompletionRawAsync's new optional `temperature` param.

Resolves Bug B6 (same-file → different-summary): the structured-raw method
was previously using DocumentIntelligenceOptions.Temperature (default 0.3),
producing non-deterministic output across the 8 handler callsites. The
sibling structured methods (GetStructuredCompletionAsync<T>,
StreamStructuredCompletionAsync) already hardcode Temperature=0 — this
hotfix aligns the third structured method with the same design intent
while preserving the ability to override per-action via Dataverse.

NULL on sprk_temperature → 0.0 (deterministic default). Operators can set
per-row overrides (e.g., 0.7 for creative summarization) via the maker portal
without code changes. SUM-CHAT@v1 specifically stays NULL (slash /summarize
already pins Temperature=0 via the streaming path).

Schema: scripts/Add-AnalysisActionTemperature.ps1 (idempotent); executed
against Spaarke Dev 2026-06-10. Backward-compat invariant: all existing
sprk_analysisaction rows keep sprk_temperature=NULL.

Also lowered ToolInvocationContextBase.Temperature default from 0.3 to 0.0
(aligns with structured-output design intent; surfaced as intentional
deviation from pre-R6 contract — see InvocationContextTests update).

Test coverage:
- Interface contract test (parameter exists, is float?, default null,
  precedes cancellationToken).
- Handler pass-through tests (EntityExtractor + RiskDetector, 3 temperature
  values each, Moq.Callback captures the value forwarded to OpenAI client).
- All 18 existing Moq Setup/Verify calls updated for the new positional
  parameter.
```

---

## 9. Files touched

### Production code (BFF)

- `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` (Temperature field on AnalysisAction)
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisActionService.cs` (read + map + ActionEntity DTO)
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolInvocationContextBase.cs` (default 0.3 → 0.0)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` (read action.Temperature)
- `src/server/api/Sprk.Bff.Api/Services/Ai/IOpenAiClient.cs` (new temperature parameter)
- `src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs` (use parameter; default 0.0f)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/SummaryHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/SemanticSearchToolHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/DocumentClassifierHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/EntityExtractorHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/ClauseAnalyzerHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/RiskDetectorHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/InvoiceExtractionToolHandler.cs`

### Scripts

- `scripts/Add-AnalysisActionTemperature.ps1` (NEW)

### Tests

- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/GetStructuredCompletionRawAsyncTemperatureTests.cs` (NEW)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/HandlerTemperaturePassThroughTests.cs` (NEW)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/TypedToolHandlerTestFixture.cs` (fixture extended)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/InvocationContextTests.cs` (default value test split)
- `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/SummarizeSessionEndpointTests.cs` (test impl signature)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookExecutionEngineTests.cs` (test impl signature)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/ClauseAnalyzerHandlerTests.cs` (Moq setup)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/EntityExtractorHandlerTests.cs` (Moq setup)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/HandlerDispatchTests.cs` (Moq setup)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/InvoiceExtractionToolHandlerTests.cs` (Moq setup)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/RiskDetectorHandlerTests.cs` (Moq setup)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/Routing/InsightsIntentClassifierTests.cs` (Moq setup)

---

## 10. Stop-and-surface notes

Per the "ADRs Are Defaults" principle, this hotfix made one intentional deviation:

- **Changed `ToolInvocationContextBase.Temperature` default from 0.3 → 0.0**. This default change affects both `ToolExecutionContext` and `ChatInvocationContext`. The chat path doesn't currently flow per-action context, so this means chat-side structured calls now also default to 0.0 deterministic instead of 0.3 non-deterministic — which is what the original design called for (sibling structured methods already pin 0). Surfaced as intentional in the `InvocationContextTests` split (the test name `ToolExecutionContext_DefaultTemperature_IsZero_PostHotfixBG9c1` documents the change).

No public-contract changes; no PublicContracts facade touched; ADR-013 preserved (AI-internal types only).
