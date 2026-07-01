# AiCompletionNodeExecutor — Pattern Decision

> **Task**: 001 — Audit AiAnalysis + EntityNameValidator for AiCompletion patterns
> **Wave**: 1 (FR-12 to FR-15)
> **Status**: ✅ Audit complete — answers below are the contract task 002 follows
> **Sources audited**: `EntityNameValidatorNodeExecutor.cs`, `AiAnalysisNodeExecutor.cs`, `INodeExecutor.cs`, `IOpenAiClient.cs`, `PromptSchemaOverrideMerger.cs`, `AnalysisServicesModule.cs::AddNodeExecutors`, `IScopeResolverService.cs::AnalysisAction`

---

## Goal Q1 — Sibling pattern for class structure

**Adopt `EntityNameValidatorNodeExecutor` structure verbatim** (spec.md §"Existing Patterns to Follow"; CLAUDE.md). The class shape is:

```csharp
public sealed class AiCompletionNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { /* PropertyNameCaseInsensitive, etc. */ };
    private readonly ILogger<AiCompletionNodeExecutor> _logger;
    private readonly IOpenAiClient _openAiClient;        // NEW — required for LLM call

    public AiCompletionNodeExecutor(
        ILogger<AiCompletionNodeExecutor> logger,
        IOpenAiClient openAiClient)            // Singleton-safe (concrete is Singleton in DI)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(openAiClient);
        _logger = logger;
        _openAiClient = openAiClient;
    }

    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[] { ActionType.AiCompletion };

    public NodeValidationResult Validate(NodeExecutionContext context) { /* see Q2 */ }

    public async Task<NodeOutput> ExecuteAsync(NodeExecutionContext context, CancellationToken ct) { /* see Q4 */ }
}
```

**Why mirror `EntityNameValidator` and NOT `AiAnalysis`**:
- AiAnalysis injects 4 collaborators (`IServiceProvider`, `ReferenceRetrievalService`, `IRagService`, `IRecordSearchService`) because it bridges to the tool-handler pipeline and runs L1/L2/L3 RAG retrieval. AiCompletion is a **prompt-only** payload-driven LLM call — none of that machinery applies.
- EntityNameValidator's shape (private static JsonSerializerOptions, ILogger-only ctor, `using var activity = AiTelemetry.ActivitySource.StartActivity(...)`, structured try/catch/Cancel block, terminal `NodeOutput.Ok` / `.Error`) is the canonical R4-era pattern the spec blesses.

**The single addition** vs EntityNameValidator: inject `IOpenAiClient`. It is registered Singleton (verified — see Q5), so this stays Singleton-safe. No `IServiceScopeFactory` needed — no Scoped deps required by an AiCompletion node.

---

## Goal Q2 — REQUIRE vs PROHIBIT in `Validate()` (FR-13)

`AiCompletionNodeExecutor.Validate(NodeExecutionContext)` MUST enforce:

| Field | Requirement | Source |
|---|---|---|
| `context.Node.OutputVariable` | **REQUIRED** non-empty | matches EntityNameValidator line 127 |
| `context.Action` | **REQUIRED** non-null (Action FK on node) | FR-06 — prompt-driven executors require Action FK |
| `context.Action.SystemPrompt` | **REQUIRED** non-whitespace (the prompt template) | spec §FR-12, FR-13 |
| OutputSchema (JPS `output.fields` OR sidecar `sprk_outputschemajson`) | **REQUIRED** — needed for `GetStructuredCompletionRawAsync`'s `BinaryData jsonSchema` arg | spec §FR-13 "missing required output schema" |
| `context.Tool` | **PROHIBITED** — Validate MUST NOT require Tool | FR-13 — **this is the core difference from AiAnalysis** |
| `context.Document` | **NOT REQUIRED** — Validate MUST NOT require Document | FR-13 — payload-driven `/narrate`, no Document needed |
| `context.Node.ConfigJson` | Optional; carries `templateParameters` + `promptSchemaOverride` | matches AiAnalysis ConfigJson semantics |

**The bug AiCompletion fixes**: `AiAnalysisNodeExecutor.Validate` lines 65-71 + 94-101 REQUIRE Tool and Document. R4's `/narrate` is a TL;DR / channel-narration generator with neither — the Validate fails before the LLM call. `AiCompletion = 1` enum was added for this case but never implemented (R4 graduation gate). FR-13 inverts both requirements.

**Open question for task 002**: where does `OutputSchemaJson` live on the `AnalysisAction` record? Today `AnalysisAction` (in `IScopeResolverService.cs` line 591) exposes `SystemPrompt` + `Temperature` but **NOT** `OutputSchemaJson`. `PlaybookExecutionEngine.cs` reads `sprk_outputschemajson` separately. Task 002 must decide: (a) extend `AnalysisAction` with `OutputSchemaJson`, or (b) read it from `context.Node.ConfigJson` (per-node schema). Recommend (a) — it makes the spec's `Action SystemPrompt + OutputSchema + Temperature` triple a first-class shape.

---

## Goal Q3 — Where `PromptSchemaOverrideMerger` plugs in (Q2 KEEP)

Reuse `AiAnalysisNodeExecutor`'s `ApplyPromptSchemaOverride` helper logic verbatim (lines 534-573). The plug-in point is **immediately before the LLM call**, after Action SystemPrompt is read and before it is passed to `IOpenAiClient.GetStructuredCompletionRawAsync`:

```csharp
// 1. Read Action SystemPrompt (JPS string)
var basePrompt = context.Action.SystemPrompt;

// 2. If basePrompt is JPS format AND node has promptSchemaOverride → merge
if (IsJpsFormat(basePrompt))
{
    var schemaOverride = PromptSchemaOverrideMerger.ExtractOverride(context.Node.ConfigJson);
    if (schemaOverride is not null)
    {
        var baseSchema = JsonSerializer.Deserialize<PromptSchema>(basePrompt, JpsDeserializeOptions);
        var merged    = PromptSchemaOverrideMerger.Merge(baseSchema!, schemaOverride);
        basePrompt    = JsonSerializer.Serialize(merged, JpsSerializeOptions);
    }
}

// 3. Render basePrompt (still JPS or flat text) to the actual prompt string passed to IOpenAiClient.
//    For AiAnalysis this is PromptSchemaRenderer; AiCompletion uses the same renderer.
```

**Key reuse rules**:
- `PromptSchemaOverrideMerger` is a **static utility** (no DI) — instantiate-free reuse.
- `Merge` semantics: scalars replace, arrays concatenate, `__replace` marker triggers full-section replacement on `constraints` + `output.fields`.
- `ExtractOverride` gracefully returns null on missing / malformed `promptSchemaOverride` — task 002 must NOT throw on absent overrides.

**DRY opportunity (defer to task 003)**: AiAnalysisNodeExecutor's `ApplyPromptSchemaOverride` is private. Task 003 may either copy it or extract to a shared internal helper. Per CLAUDE.md §11 "default to reuse", prefer extraction if both executors need identical logic.

---

## Goal Q4 — `IOpenAiClient.GetStructuredCompletionRawAsync` return shape → `node.OutputVariable`

**The signature** (`IOpenAiClient.cs` lines 163-170):

```csharp
Task<string> GetStructuredCompletionRawAsync(
    string prompt,
    BinaryData jsonSchema,
    string schemaName,
    string? model = null,
    int? maxOutputTokens = null,
    float? temperature = null,
    CancellationToken ct = default);
```

**Returns a raw JSON string** conforming to `jsonSchema` (json_schema strict mode, constrained decoding). NOT a `JsonElement` — caller parses if needed.

**Mapping into `NodeOutput`** (mirroring `EntityNameValidator` lines 252-263):

```csharp
var rawJson = await _openAiClient.GetStructuredCompletionRawAsync(
    prompt:        renderedPrompt,                                  // post-merge, post-render
    jsonSchema:    BinaryData.FromString(action.OutputSchemaJson),  // see Q2 open question
    schemaName:    deriveSchemaName(action),                        // e.g. "AiCompletion_BRIEF-NARRATE-TLDR"
    model:         context.ModelDeploymentId ?? context.Node.ModelDeploymentId,
    maxOutputTokens: context.MaxTokens,
    temperature:   (float?)effectiveTemperature,                    // null → defaults to 0.0
    cancellationToken: ct);

// Parse once for structured access; pass raw string as TextContent.
using var doc      = JsonDocument.Parse(rawJson);
var structuredData = doc.RootElement.Clone();   // disposed-safe snapshot

return NodeOutput.Ok(
    nodeId:         context.Node.Id,
    outputVariable: context.Node.OutputVariable,    // <-- binding point
    structuredData: structuredData,                  // queryable JsonElement for downstream nodes
    textContent:    rawJson,                         // raw for ReturnResponse / consumer mapping
    metrics:        NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
```

`NodeOutput.OutputVariable` is the scope-variable name set by the playbook author (e.g. `"narration"`); downstream nodes read it via `context.PreviousOutputs["narration"]`. Same wiring as EntityNameValidator — no new mechanism needed.

**Temperature override** (per spec FR-13 / FR-14 test coverage): pass `(float?)context.Action.Temperature` (`AnalysisAction.Temperature` is `decimal?` — null = "deterministic 0.0 default", which `GetStructuredCompletionRawAsync` already handles internally per Wave B-G9c1 B6 fix).

---

## Goal Q5 — DI registration (ADR-010)

**Singleton, via existing `AnalysisServicesModule.AddNodeExecutors` block**. Verified by reading `AnalysisServicesModule.cs` lines 855-953: every existing executor (CreateTask, SendEmail, AiAnalysis, EntityNameValidator, Start, LoadKnowledge, ReturnResponse, etc.) is registered as Singleton. `NodeExecutorRegistry` (line 696) is also Singleton and requires Singleton executors.

**The exact line to add** (task 006 territory, but specified here for clarity):

```csharp
// AiCompletionNodeExecutor — ActionType.AiCompletion = 1 (R7 spaarke-ai-platform-unification-r7 / FR-12-15).
// Closes R4 /narrate graduation gate. Singleton: ILogger + IOpenAiClient (both Singleton-safe).
// No Scoped deps. UNCONDITIONAL registration per CLAUDE.md §10 BFF Hygiene §F.1
// (asymmetric-registration anti-pattern avoidance).
services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor,
    Sprk.Bff.Api.Services.Ai.Nodes.AiCompletionNodeExecutor>();
```

**ADR-010 compliance**:
- No new interface layer (executor implements existing `INodeExecutor`).
- No feature-gate / `if (flag)` block — UNCONDITIONAL per CLAUDE.md §F.1 to avoid the asymmetric-registration anti-pattern (R7 design.md confirms `AiCompletion` ships unconditionally per FR-12).
- `IOpenAiClient` is already a Singleton (verified — `OpenAiClient` is registered Singleton; the interface exists per ADR-010 testability allowance).

**Verification gate for task 006**: confirm the registration line is placed inside `AddNodeExecutors` (line 855) NOT inside a conditional block. If a conditional block creeps in during review, apply CLAUDE.md §F.1 static-scan recipe + ADR-032 Null-Object Pattern.

---

## Cross-cutting notes for task 002 scaffold

1. **Telemetry**: copy `using var activity = AiTelemetry.ActivitySource.StartActivity("ai.completion.node_execute", ActivityKind.Internal);` from EntityNameValidator. Add tags `node.id`, `node.name`, `action_type` (`(int)ActionType.AiCompletion`), `node.outcome`.
2. **Cancellation**: mirror EntityNameValidator's `catch (OperationCanceledException)` + `catch (Exception ex)` blocks for consistent `NodeErrorCodes.Cancelled` / `.InternalError` propagation.
3. **NodeErrorCodes**: reuse `ValidationFailed` (validation failures), `InternalError` (LLM throws), `Cancelled` (cancellation). No new error codes needed.
4. **Tests (FR-14, task 007-009)**: mock `IOpenAiClient` only — per CLAUDE.md ADR-038 testing strategy. NO `Mock<HttpMessageHandler>`. Cover: missing Action FK, missing SystemPrompt, missing OutputSchema, valid call → structured output binding, temperature override (Action vs config), promptSchemaOverride merge, OpenAiCircuitBrokenException propagation, OperationCanceledException propagation, missing OutputVariable, malformed ConfigJson.

---

**Decision confidence**: HIGH. All five questions answered from primary source. Open follow-up for task 002 scaffold: confirm `OutputSchemaJson` carrier on `AnalysisAction` record (Q2 open question above).
