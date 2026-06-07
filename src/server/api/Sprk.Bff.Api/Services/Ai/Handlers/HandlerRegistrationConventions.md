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

**Migration**: New handler PRs MUST include the Dataverse seed (CSV or PowerShell snippet) for at least one `sprk_analysistool` row pointing at the handler. Without the seed, the handler is dead code at runtime.

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
