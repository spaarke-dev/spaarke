# Handler Registration Conventions (R6 Pillar 2)

> **Audience**: Engineers landing the 8 typed tool handlers (R6 tasks 101–108, FR-13 through FR-20).
> **Status**: Binding — every typed handler PR must comply.
> **Owner**: Sprk.Bff.Api AI subsystem
> **Last Updated**: 2026-06-07 (R6 task 100)

This doc codifies the 4-point handler contract introduced by R6 Pillar 2. The contract is intentionally minimal — discoverability + safety, no architectural ceremony.

---

## The 4-point contract

Every concrete tool handler in `Services/Ai/` MUST satisfy ALL FOUR points below. Wave 1 (101–104; deterministic) and Wave 2 (105–108; LLM-assisted) handler tasks copy these conventions verbatim.

### (1) Implements `IToolHandler`

```csharp
public sealed class YourHandler : IToolHandler
{
    public string HandlerId => nameof(YourHandler);
    public ToolHandlerMetadata Metadata { get; } = new(...);
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.X };
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) { ... }
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken ct) { ... }
}
```

**Notes**:

- `IToolHandler` was renamed from `IAnalysisToolHandler` in task 006. A `global using IAnalysisToolHandler = ...IToolHandler;` alias in `GlobalUsings.cs` keeps existing implementations (GenericAnalysisHandler, DocumentClassifierHandler, SummaryHandler, SemanticSearchToolHandler) compiling unchanged. Prefer the new name `IToolHandler` in new code.
- The interface has 3 default-method members (introduced by task 009 D-A-09) for chat-context dispatch: `SupportedInvocationContexts`, `ValidateChat`, `ExecuteChatAsync`. Handlers that opt INTO chat invocation override all three; otherwise the defaults reject chat invocation with `NotSupportedException`. See "Chat-available handlers" below.

### (2) Auto-discovered (NO manual DI line per ADR-010)

Auto-discovery lives in `Services/Ai/ToolFrameworkExtensions.cs`:

```csharp
public static IServiceCollection AddToolHandlersFromAssembly(
    this IServiceCollection services,
    Assembly assembly)
{
    var handlerInterface = typeof(IAnalysisToolHandler); // alias = IToolHandler
    var handlerTypes = assembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && handlerInterface.IsAssignableFrom(t))
        .ToList();

    foreach (var handlerType in handlerTypes)
        services.AddScoped(handlerInterface, handlerType);

    return services;
}
```

**Binding rules**:

- ❌ **DO NOT** add `services.AddScoped<IToolHandler, YourHandler>()` anywhere — auto-discovery handles it.
- ❌ **DO NOT** register handlers outside `AnalysisServicesModule.cs`. Per ADR-010, ZERO new top-level `Program.cs` lines.
- ✅ **DO** add per-test verification that the new handler is auto-discovered. Use the `HandlerContractTestTemplate` as a starting point (see tests section below).

### (3) Dataverse row with `sprk_handlerclass` field

Each handler ships a corresponding `sprk_analysistool` row whose `sprk_handlerclass` column equals the C# class name (= `nameof(YourHandler)` = `HandlerId`).

| Column | Example | Binding |
|---|---|---|
| `sprk_name` | `Extract Dates` | UI display |
| `sprk_handlerclass` | `DateExtractorHandler` | **Routes runtime invocation → C# class** |
| `sprk_tooltype` | `Extractor` | Indexes into `ToolHandlerRegistry.GetHandlersByType` |
| `sprk_availableincontexts` | `Both` (Playbook + Chat) | Per task 007 D-A-07 |
| `sprk_jsonschema` | `{ "type": "object", ... }` | LLM tool-call argument schema (task 008 D-A-08) |
| `sprk_configuration` | `{ ... }` | Handler-specific config JSON |
| `sprk_requiredcapability` | `verify_citations` (optional) | Per-playbook capability gate (Wave 7b). See "Capability-Gated Tools" below. |

**Migration**: New handler PRs MUST include the Dataverse seed (CSV or PowerShell snippet) for at least one `sprk_analysistool` row pointing at the handler. Without the seed, the handler is dead code at runtime.

### Capability-Gated Tools (`sprk_requiredcapability`)

Some chat tools are intentionally restricted to playbooks that opt-in via the `sprk_analysisplaybook.sprk_playbookcapabilities` multi-select choice. Examples (Waves 7c / 8 / 9):

| Tool | `sprk_requiredcapability` value | Why gated |
|---|---|---|
| VerifyCitations | `verify_citations` | Legal review boundary — only legal playbooks need verify_citations exposed to the LLM |
| LegalResearch | `legal_research` | Bing Grounding cost + governance scope (ADR-015) |
| WebSearch | `web_search` | External egress — only playbooks with explicit web-search permission |
| CodeInterpreter | `code_interpreter` | Sandbox code execution — explicit data-governance opt-in (ADR-018) |
| WorkingDocumentTools (write_back) | `write_back` | Mutating tool — limited to playbooks that target the active document |

**Contract** (R6 Wave 7b infrastructure):

- When `sprk_requiredcapability` is **null/empty**, the tool is **always available** in chat (default — applies to every existing pre-Wave-7b row including the 8 typed handlers + AnalysisQuery + TextRefinement).
- When **set**, the canonical string must match (case-insensitive) one of the values in `Models/Ai/Chat/PlaybookCapabilities.cs` (e.g., `"verify_citations"`, `"write_back"`, `"web_search"`, `"code_interpreter"`, `"legal_research"`, `"reanalyze"`).
- The filter is enforced **at chat-session start** in the data-driven block of `SprkChatAgentFactory.ResolveTools()` (`IsCapabilityGateSatisfied`). Rows whose capability is NOT in the current playbook's set are silently skipped (logged at Debug level for diagnosis, never at Information or Warning — ADR-015 telemetry).
- For **standalone chat** (no playbook), the effective capability set is `PlaybookCapabilities.CoreCapabilities`. Gated tools are NOT exposed in standalone chat unless their capability happens to be in `CoreCapabilities` (today: `search`, `analyze`, `selection_revise`, `summarize`, `insights_query`).

**Important — this is NOT a feature flag (ADR-018)**: feature flags gate underlying service registrations (e.g., the LegalResearch Bing Grounding service has its own `Foundry.LegalResearchAssistant:Enabled` kill-switch). `sprk_requiredcapability` is per-tool authorization on top of those flags. A tool with a working service registration but a missing capability for the current playbook will not be offered to the LLM at all — it remains absent from the function-calling schema.

**Migration responsibility**: Each capability-gated tool migration PR (Waves 7c / 8 / 9) is responsible for:

1. Setting `sprk_requiredcapability` on the migrated row to the canonical `PlaybookCapabilities` value.
2. Removing the corresponding hardcoded `if (capabilities.Contains(PlaybookCapabilities.X))` block from `SprkChatAgentFactory.ResolveTools()`.
3. Updating the per-handler test fixture with at least one positive (capability present) and one negative (capability missing) assertion.

### (4) Tests using `TypedToolHandlerTestFixture` + the 4 contract assertions

Every handler test class MUST include the 4 contract tests from `HandlerContractTestTemplate.cs` (substitute the template type with the handler under test):

| Test | Asserts |
|---|---|
| `HandlerType_IsRegisteredInDi` | Assembly scan picks up the handler |
| `Handler_IsDiscoverableByHandlerClassName` | `handler.HandlerId == nameof(Handler)` |
| `Metadata_IsValid` | Name, Description, Version (semver) all populated |
| `SupportedToolTypes_IsNonEmpty` | Registry can index handler by type |

Additional per-handler tests (positive, error, config-driven, ADR-015 telemetry) inherit from `TypedToolHandlerTestFixture` for shared mocks (`IOpenAiClient`, `IScopeResolverService`), context builders (`BuildToolExecutionContext`, `BuildChatInvocationContext`), and telemetry assertions (`AssertTelemetryRespectsAdr015`).

---

## Worked example: `GenericAnalysisHandler` (existing canonical reference)

The existing `GenericAnalysisHandler` at `Services/Ai/Handlers/GenericAnalysisHandler.cs` is the reference shape:

```csharp
public sealed class GenericAnalysisHandler : IStreamingAnalysisToolHandler // extends IToolHandler
{
    private const string HandlerIdValue = "GenericAnalysisHandler";  // (1) matches class name

    // Constructor injection — auto-resolved by the DI container after assembly scan picks up the class.
    public GenericAnalysisHandler(
        IOpenAiClient openAiClient,                 // ADR-014 cache, ADR-016 rate limit live behind this client
        IScopeResolverService scopeResolver,
        PromptSchemaRenderer promptSchemaRenderer,
        IOptions<ModelSelectorOptions> modelSelectorOptions,
        ILogger<GenericAnalysisHandler> logger) { ... }

    public string HandlerId => HandlerIdValue;                       // (1) routes from sprk_handlerclass

    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Generic Analysis Handler",
        Description: "Executes custom tools defined in Dataverse Tool scopes. Supports extract, classify, validate, and generate operations.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        Parameters: new[] { ... });

    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) { ... }

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            // ADR-015: log identifiers + outcome + timing only — never extractedText body
            _logger.LogInformation(
                "Starting generic tool execution for analysis {AnalysisId}, tool {ToolId} ({ToolName})",
                context.AnalysisId, tool.Id, tool.Name);
            // ... actual work ...
            return ToolResult.Ok(HandlerId, tool.Id, tool.Name, resultData, response, confidence, executionMetadata);
        }
        catch (OperationCanceledException) { return ToolResult.Error(..., ToolErrorCodes.Cancelled, ...); }
        catch (Exception ex) { return ToolResult.Error(..., ToolErrorCodes.InternalError, ...); }
    }
}
```

**Why this is canonical**:

- ✅ Class name = `HandlerId` = `sprk_handlerclass` column value
- ✅ Auto-discovered via the assembly scan (no manual DI line)
- ✅ Ctor injection of `IOpenAiClient` (NOT `OpenAiClient`) routes through ADR-014 cache + ADR-016 rate limit
- ✅ Telemetry emits handler name + outcome + timestamp + IDs ONLY (ADR-015 compliant)
- ✅ Error handling: `OperationCanceledException` → `Cancelled`; other exceptions → `InternalError` with the message

**See also**: `Services/Ai/Handlers/TemplateHandler.cs` for a minimal copy-and-modify template.

---

## Chat-available handlers (Wave 2 LLM-assisted, optional)

To opt INTO chat invocation via LLM function calling, override the three default interface methods introduced by task 009:

```csharp
public sealed class YourChatAwareHandler : IToolHandler
{
    // ...HandlerId / Metadata / SupportedToolTypes / Validate / ExecuteAsync as usual...

    // Opt INTO chat invocation:
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Both;

    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        // ADR-015: ONLY inspect context's IDs + handles. NEVER raw user message text.
        // context.ToolArgumentsJson was already schema-validated at the adapter boundary
        // (task D-A-10) against AnalysisTool.JsonSchema, so its structure is safe.
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");
        return ToolValidationResult.Success();
    }

    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        // Parse context.ToolArgumentsJson, call _openAiClient (ADR-014 + ADR-016 baked in),
        // and return ToolResult.Ok / ToolResult.Error.
    }
}
```

The Dataverse `sprk_availableincontexts` column on the corresponding `sprk_analysistool` row MUST match this declaration (must include `Chat` if `SupportedInvocationContexts` includes `Chat`).

---

## Returning Citations + Widget Events from Chat-side Handlers (R6 Wave 7b)

Chat-side handlers (Wave 7c KnowledgeRetrieval, Wave 8 DocumentSearch / WebSearch / CodeInterpreter / LegalResearch / VerifyCitations) need to emit citations + pane widget events as side effects. The pre-R5 hardcoded tools accomplished this by mutating a shared `CitationContext` accumulator and calling an `SseWriter` delegate captured at construction. Under the R6 `IToolHandler` contract, handlers are pure-input/pure-output via the synchronous `ToolResult` return — the cross-cutting work belongs in the adapter, not the handler.

### The pattern: return metadata from the handler; the adapter does the side effects

```csharp
public async Task<ToolResult> ExecuteChatAsync(
    ChatInvocationContext context,
    AnalysisTool tool,
    CancellationToken cancellationToken)
{
    // ... do retrieval / search / etc. — return pure data ...
    var searchHits = await _ragService.SearchAsync(query, ct: cancellationToken);

    var citations = searchHits.Select(h => new ToolResultCitation(
        ChunkId: h.ChunkId,
        SourceName: h.DocumentName,
        PageNumber: h.PageNumber,
        Excerpt: h.Snippet)).ToArray();

    var widget = new ToolResultWidget(
        PaneType: "source_pane",
        WidgetType: "DocumentViewer",
        Data: new { filename = topHit.DocumentName, page = topHit.PageNumber },
        CitationId: "1");

    return ToolResult.Ok(
        handlerId: HandlerId,
        toolId: tool.Id,
        toolName: tool.Name,
        data: new { hits = searchHits.Select(h => h.Excerpt) },
        summary: $"Found {searchHits.Count} results.") with
    {
        Metadata = new Dictionary<string, object?>
        {
            [ToolResultMetadataKeys.Citations] = citations,
            [ToolResultMetadataKeys.Widget]    = widget,
        }
    };
}
```

The adapter (`ToolHandlerToAIFunctionAdapter`) reads these well-known keys after `ExecuteChatAsync` returns and:

1. Forwards each `ToolResultCitation` into the per-chat-turn `CitationContext.AddCitation(...)` (provided by the chat factory at adapter construction).
2. Emits `ToolResultWidget` as a `source_pane` or `output_pane` `ChatSseEvent` via the constructor-supplied SSE writer delegate.

### Why this shape

| Concern | Where it lives |
|---|---|
| Citation accumulation | Adapter (it has the per-turn `CitationContext`) |
| Widget SSE emission | Adapter (it has the SSE writer) |
| Business logic (search, refine, retrieve) | Handler |
| Telemetry (ADR-015: counts + decisionId only) | Adapter |
| Resilience (writer faults are non-fatal) | Adapter |

The handler stays a pure function: same input → same output, no shared-state surprises, easy to unit-test, easy to dispatch from either playbook or chat contexts.

### Backward compat

- Handlers that don't set `Metadata` (existing 8 typed handlers; AnalysisQuery; TextRefinement) get null and the adapter performs no post-processing.
- Adapter constructor's `citationAccumulator` + `sseWriter` are both optional (default null). When null, metadata is dropped silently — the handler's `ToolResult` still returns to the LLM unchanged.
- The chat factory (`SprkChatAgentFactory.ResolveTools`) passes both when constructing the per-chat-session adapter for each data-driven tool row.

### ADR-015 binding

Citations + widget envelopes carry deterministic source identifiers + display metadata ONLY. NEVER user-message content. The adapter telemetry logs counts/buckets only — verified by sentinel-string test (`PostProcessing_Telemetry_DoesNotLogCitationOrWidgetContent_Adr015`).

### Constants + envelope shapes

| Key | Constant | Envelope record |
|---|---|---|
| `"citations"` | `ToolResultMetadataKeys.Citations` | `ToolResultCitation` (ChunkId, SourceName, PageNumber?, Excerpt, SourceType?, Url?, Snippet?) |
| `"widget"` | `ToolResultMetadataKeys.Widget` | `ToolResultWidget` (PaneType, WidgetType, Data, CitationId?) |

The adapter normalizes the metadata value through JSON serialization so handlers may supply records, anonymous objects, `JsonElement`, or even JSON strings — all round-trip cleanly. Malformed entries are logged-and-skipped (non-fatal).

---

## Cross-cutting binding rules

| ADR | Binding |
|---|---|
| **ADR-010** | Auto-discovery only. ZERO per-handler DI lines outside `ToolFrameworkExtensions`. ZERO new top-level `Program.cs` lines. |
| **ADR-013** | Handlers live in `Services/Ai/Handlers/` (or `Services/Ai/Tools/` for legacy locations). CRUD-side callers route through `Services/Ai/PublicContracts/` facades — NEVER directly into handler types. |
| **ADR-014** | Per-tenant cache: handlers that cache MUST include `context.TenantId` (or `context.MatterId`) as the cache-key prefix. NEVER cache cross-tenant. |
| **ADR-015** | Telemetry surface is handler name + outcome + timestamp + deterministic IDs (analysisId, sessionId, tenantId, matterId, decisionId) ONLY. NEVER log document content, extracted text, prompts, or model responses. Use `TypedToolHandlerTestFixture.AssertTelemetryRespectsAdr015` to enforce this in tests. |
| **ADR-016** | LLM-assisted handlers (Wave 2) use `IOpenAiClient` — the per-tenant rate-limit gate sits behind this interface. NEVER call Azure OpenAI directly. |
| **ADR-029** | Each handler PR reports the BFF publish-size delta. Per-handler delta target ≤+0.5 MB; aggregate Wave 1+Wave 2 target ≤+2 MB. |

---

## Test scaffolding

| Asset | Location | Purpose |
|---|---|---|
| `TypedToolHandlerTestFixture` | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/TypedToolHandlerTestFixture.cs` | Shared mocks + context builders + ADR-015 telemetry assertions |
| `HandlerContractTestTemplate` | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/HandlerContractTestTemplate.cs` | 4 contract tests every handler test class copies |
| `AutoDiscoveryVerificationTests` | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/AutoDiscoveryVerificationTests.cs` | Gate test: assembly scan finds every concrete `IToolHandler` |

Each Wave 1 / Wave 2 handler test class:

```csharp
public sealed class DateExtractorHandlerTests : TypedToolHandlerTestFixture
{
    // 4 contract tests (copy from HandlerContractTestTemplate, substituting DateExtractorHandler)
    [Fact] public void HandlerType_IsRegisteredInDi() { ... }
    [Fact] public void Handler_IsDiscoverableByHandlerClassName() { ... }
    [Fact] public void Metadata_IsValid() { ... }
    [Fact] public void SupportedToolTypes_IsNonEmpty() { ... }

    // Per-handler tests using BuildToolExecutionContext + AssertTelemetryRespectsAdr015
    [Fact] public async Task ExecuteAsync_ExtractsDateFromValidInput() { ... }
    [Fact] public async Task Validate_RejectsMissingTenantId() { ... }
    [Fact] public async Task Telemetry_RespectsAdr015() { ... }
}
```

---

## Cross-references

- Task 006 (D-A-06): `IAnalysisToolHandler` → `IToolHandler` rename + GlobalUsings alias
- Task 007 (D-A-07): `AvailableInContexts` enum + Dataverse column
- Task 008 (D-A-08): `JsonSchema` field on `AnalysisTool` DTO + Dataverse column
- Task 009 (D-A-09): Execution context split (`ToolExecutionContext` + `ChatInvocationContext`)
- Task 010 (D-A-10): `ToolHandlerToAIFunctionAdapter` (adapter that consults `SupportedInvocationContexts`)
- Task 100 (D-H-00): This conventions doc + test fixture + contract test template + auto-discovery verification tests (THE GATE)
- Tasks 101–104 (D-H-01 .. D-H-04): Wave 1 pure-deterministic handlers
- Tasks 105–108 (D-H-05 .. D-H-08): Wave 2 LLM-assisted handlers
- Task 109 (D-H-09/10): Dispatch tests (playbook + chat)

---

*Maintained by R6 Pillar 2. To extend: amend this doc + the corresponding ADRs cited above. Per project CLAUDE.md NFR-04 — no Microsoft Agent Framework references.*
