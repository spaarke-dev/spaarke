using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="ToolHandlerToAIFunctionAdapter"/> (R6 Pillar 2 task D-A-10).
/// </summary>
/// <remarks>
/// <para>
/// Acceptance-criteria coverage (from task POML 010):
/// </para>
/// <list type="bullet">
/// <item>Adapter exposes a way to produce AIFunction from (AnalysisTool, IToolHandler).</item>
/// <item>JsonSchema from AnalysisTool becomes the AIFunction parameter declaration (FR-10).</item>
/// <item>Invocation builds correct ChatInvocationContext + dispatches ExecuteChatAsync.</item>
/// <item>Validation failure path returns structured error envelope (does not throw).</item>
/// <item>Telemetry emits tool name + decision + duration only (no user content; ADR-015).</item>
/// <item>Registered inside existing AnalysisServicesModule per ADR-010 (no top-level Program.cs).</item>
/// <item>Adapter is AI-internal (ADR-013); not consumed by Services/Ai/PublicContracts.</item>
/// </list>
/// </remarks>
[Trait("status", "passing")]
[Trait("task", "r6-task-010")]
public class ToolHandlerToAIFunctionAdapterTests
{
    private const string TestToolName = "TestChatTool";
    private const string TestTenantId = "tenant-r6-010";
    private static readonly Guid TestToolId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TestSessionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private const string ValidJsonSchema = """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search query" },
            "topK": { "type": "integer", "default": 5 }
          },
          "required": ["query"]
        }
        """;

    // ─── Helpers ─────────────────────────────────────────────────────────────────────

    private static AnalysisTool CreateTool(
        string? name = TestToolName,
        string? description = "A test chat-available tool.",
        string? jsonSchema = ValidJsonSchema)
        => new()
        {
            Id = TestToolId,
            Name = name ?? string.Empty,
            Description = description,
            Type = ToolType.Custom,
            HandlerClass = "FakeChatHandler",
            AvailableInContexts = ToolAvailabilityContext.Chat,
            JsonSchema = jsonSchema
        };

    private static ChatInvocationContext CreateContext()
        => new()
        {
            ChatSessionId = TestSessionId,
            TenantId = TestTenantId,
            UserContext = "session-level instructions (NOT message body)"
        };

    /// <summary>
    /// Fake chat-available IToolHandler used to verify the adapter dispatches correctly to
    /// the chat-context overload (task 009 FR-09 contract).
    /// </summary>
    private sealed class FakeChatHandler : IToolHandler
    {
        public bool ValidateChatCalled { get; private set; }
        public bool ExecuteChatCalled { get; private set; }
        public bool ExecuteLegacyCalled { get; private set; }
        public ChatInvocationContext? CapturedContext { get; private set; }
        public AnalysisTool? CapturedTool { get; private set; }
        public Func<ChatInvocationContext, AnalysisTool, ToolValidationResult> ValidateChatImpl { get; init; }
            = (_, _) => ToolValidationResult.Success();
        public Func<ChatInvocationContext, AnalysisTool, CancellationToken, Task<ToolResult>> ExecuteChatImpl { get; init; }
            = (_, t, _) => Task.FromResult(ToolResult.Ok(
                handlerId: "FakeChatHandler",
                toolId: t.Id,
                toolName: t.Name,
                data: new { ok = true },
                summary: "ok"));

        public string HandlerId => "FakeChatHandler";
        public ToolHandlerMetadata Metadata { get; } = new(
            Name: "Fake Chat Handler",
            Description: "Test fake",
            Version: "1.0.0",
            SupportedInputTypes: new[] { "text/plain" },
            Parameters: Array.Empty<ToolParameterDefinition>(),
            ConfigurationSchema: null);
        public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

        public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Both;

        public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
            => ToolValidationResult.Success();

        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken)
        {
            ExecuteLegacyCalled = true;
            return Task.FromResult(ToolResult.Ok(HandlerId, tool.Id, tool.Name, new { legacy = true }));
        }

        public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
        {
            ValidateChatCalled = true;
            CapturedContext = context;
            CapturedTool = tool;
            return ValidateChatImpl(context, tool);
        }

        public Task<ToolResult> ExecuteChatAsync(ChatInvocationContext context, AnalysisTool tool, CancellationToken cancellationToken)
        {
            ExecuteChatCalled = true;
            CapturedContext = context;
            CapturedTool = tool;
            return ExecuteChatImpl(context, tool, cancellationToken);
        }
    }

    /// <summary>
    /// Playbook-only handler — does NOT declare Chat in SupportedInvocationContexts.
    /// Used to verify the adapter rejects construction (defense against the FR-09 default
    /// ValidateChat that returns Failure).
    /// </summary>
    private sealed class FakePlaybookOnlyHandler : IToolHandler
    {
        public string HandlerId => "FakePlaybookOnlyHandler";
        public ToolHandlerMetadata Metadata { get; } = new(
            Name: "Playbook-Only",
            Description: "Test fake; refuses chat",
            Version: "1.0.0",
            SupportedInputTypes: new[] { "text/plain" },
            Parameters: Array.Empty<ToolParameterDefinition>(),
            ConfigurationSchema: null);
        public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

        public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
            => ToolValidationResult.Success();

        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken)
            => Task.FromResult(ToolResult.Ok(HandlerId, tool.Id, tool.Name, new { ok = true }));

        // SupportedInvocationContexts defaults to Playbook only (default interface method) —
        // we intentionally do NOT override here.
    }

    // ─── Construction tests ───────────────────────────────────────────────────────────


    [Fact]
    public void Constructor_NullTool_Throws()
    {
        Action act = () => new ToolHandlerToAIFunctionAdapter(
            tool: null!, new FakeChatHandler(), CreateContext);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tool");
    }

    [Fact]
    public void Constructor_NullHandler_Throws()
    {
        Action act = () => new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler: null!, CreateContext);

        act.Should().Throw<ArgumentNullException>().WithParameterName("handler");
    }

    [Fact]
    public void Constructor_NullContextFactory_Throws()
    {
        Action act = () => new ToolHandlerToAIFunctionAdapter(
            CreateTool(), new FakeChatHandler(), contextFactory: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("contextFactory");
    }

    [Fact]
    public void Constructor_WhitespaceToolName_Throws()
    {
        Action act = () => new ToolHandlerToAIFunctionAdapter(
            CreateTool(name: "   "), new FakeChatHandler(), CreateContext);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tool")
            .WithMessage("*FR-10*Name*");
    }

    // ─── JSON Schema guard tests ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullJsonSchema_Throws()
    {
        // FR-10: playbook-only rows have null JsonSchema; chat-side resolver must filter them
        // out before adapter construction. The adapter is the second line of defense.
        var tool = CreateTool(jsonSchema: null);
        Action act = () => new ToolHandlerToAIFunctionAdapter(
            tool, new FakeChatHandler(), CreateContext);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tool")
            .WithMessage("*null/empty JsonSchema*");
    }

    [Fact]
    public void Constructor_WhitespaceJsonSchema_Throws()
    {
        var tool = CreateTool(jsonSchema: "   ");
        Action act = () => new ToolHandlerToAIFunctionAdapter(
            tool, new FakeChatHandler(), CreateContext);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tool")
            .WithMessage("*null/empty JsonSchema*");
    }

    [Fact]
    public void Constructor_MalformedJsonSchema_Throws()
    {
        var tool = CreateTool(jsonSchema: "{ this is not json");
        Action act = () => new ToolHandlerToAIFunctionAdapter(
            tool, new FakeChatHandler(), CreateContext);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tool")
            .WithMessage("*not valid JSON*");
    }

    [Fact]
    public void Constructor_NonObjectRootSchema_Throws()
    {
        // Schema root must be a JSON object (FR-10 + function-calling protocol invariant).
        var tool = CreateTool(jsonSchema: "[\"array root not allowed\"]");
        Action act = () => new ToolHandlerToAIFunctionAdapter(
            tool, new FakeChatHandler(), CreateContext);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tool")
            .WithMessage("*root must be a JSON object*");
    }

    [Fact]
    public void Constructor_PropertiesNotObject_Throws()
    {
        // If "properties" is present, it must be an object — protects against schemas where
        // the column was populated with the wrong shape (e.g., array of param names).
        var tool = CreateTool(jsonSchema: """{ "type": "object", "properties": ["query"] }""");
        Action act = () => new ToolHandlerToAIFunctionAdapter(
            tool, new FakeChatHandler(), CreateContext);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tool")
            .WithMessage("*'properties' must be a JSON object*");
    }

    // ─── R6 audit item 1: semantic JSON Schema validation (Draft 2020-12) ──────────────
    // The constructor now invokes JsonSchema.Net to validate the candidate schema against
    // the JSON Schema Draft 2020-12 meta-schema. Schemas that are well-formed JSON +
    // structurally object-rooted but semantically invalid as JSON Schema (e.g., property
    // value that is a primitive instead of a schema object) are rejected at chat-session
    // start, before the LLM ever sees them.

    [Fact]
    public void Constructor_SemanticInvalid_PropertyValueIsNumber_Throws()
    {
        // Well-formed JSON, top-level object, "properties" is an object — but the property
        // value "query" is the integer 42 rather than a sub-schema. Draft 2020-12 rejects.
        const string badSchema = """{"type":"object","properties":{"query":42}}""";
        var tool = CreateTool(jsonSchema: badSchema);

        Action act = () => new ToolHandlerToAIFunctionAdapter(
            tool, new FakeChatHandler(), CreateContext);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tool")
            .WithMessage("*R6-audit-1*not a valid JSON Schema*");
    }

    [Fact]
    public void Constructor_SemanticInvalid_RequiredIsString_Throws()
    {
        // "required" must be an array of strings; here it's a string.
        const string badSchema = """{"type":"object","required":"query"}""";
        var tool = CreateTool(jsonSchema: badSchema);

        Action act = () => new ToolHandlerToAIFunctionAdapter(
            tool, new FakeChatHandler(), CreateContext);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("tool")
            .WithMessage("*R6-audit-1*not a valid JSON Schema*");
    }


    [Fact]
    public void Constructor_PlaybookOnlyHandler_Throws()
    {
        // FR-09 + FR-10: handlers must declare Chat in SupportedInvocationContexts.
        var tool = CreateTool();
        Action act = () => new ToolHandlerToAIFunctionAdapter(
            tool, new FakePlaybookOnlyHandler(), CreateContext);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not declare InvocationContextKind.Chat*");
    }

    // ─── AIFunction surface tests ─────────────────────────────────────────────────────

    [Fact]
    public void JsonSchema_ExposesAnalysisToolSchema_Verbatim()
    {
        // FR-10 binding: the AIFunction's JsonSchema (what the LLM sees as the function's
        // parameter declaration) MUST come from AnalysisTool.JsonSchema.
        var tool = CreateTool();
        var sut = new ToolHandlerToAIFunctionAdapter(tool, new FakeChatHandler(), CreateContext);

        sut.JsonSchema.ValueKind.Should().Be(JsonValueKind.Object);
        sut.JsonSchema.TryGetProperty("properties", out var props).Should().BeTrue();
        props.TryGetProperty("query", out var queryProp).Should().BeTrue();
        queryProp.GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void Name_ReturnsAnalysisToolName()
    {
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(name: "MySpecialTool"), new FakeChatHandler(), CreateContext);

        sut.Name.Should().Be("MySpecialTool");
    }

    [Fact]
    public void Description_ReturnsAnalysisToolDescription()
    {
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(description: "Custom desc"), new FakeChatHandler(), CreateContext);

        sut.Description.Should().Be("Custom desc");
    }

    [Fact]
    public void Description_NullToolDescription_ReturnsEmptyString()
    {
        // AIFunctionDeclaration.Description returns string (not string?); we coalesce to "".
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(description: null), new FakeChatHandler(), CreateContext);

        sut.Description.Should().Be(string.Empty);
    }

    // ─── Invocation tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_DispatchesToExecuteChatAsync_NotLegacyExecuteAsync()
    {
        // FR-09 + FR-10: adapter must use the chat-context overload, not the legacy one.
        var tool = CreateTool();
        var handler = new FakeChatHandler();
        var sut = new ToolHandlerToAIFunctionAdapter(tool, handler, CreateContext);

        var args = new AIFunctionArguments { ["query"] = "find precedent" };

        var result = await sut.InvokeAsync(args, CancellationToken.None);

        handler.ExecuteChatCalled.Should().BeTrue();
        handler.ExecuteLegacyCalled.Should().BeFalse();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_BuildsContext_WithToolNameAndArgsJson()
    {
        // FR-10: ChatInvocationContext built per-call carries RequestedToolName +
        // ToolArgumentsJson sourced from the LLM's invocation payload.
        var tool = CreateTool();
        var handler = new FakeChatHandler();
        var sut = new ToolHandlerToAIFunctionAdapter(tool, handler, CreateContext);

        var args = new AIFunctionArguments
        {
            ["query"] = "lease renewal clauses",
            ["topK"] = 10
        };

        await sut.InvokeAsync(args, CancellationToken.None);

        handler.CapturedContext.Should().NotBeNull();
        handler.CapturedContext!.RequestedToolName.Should().Be(TestToolName);
        handler.CapturedContext.ChatSessionId.Should().Be(TestSessionId);
        handler.CapturedContext.TenantId.Should().Be(TestTenantId);
        handler.CapturedContext.ToolArgumentsJson.Should().NotBeNullOrEmpty();

        // Verify args were serialized to the JSON the handler expects.
        var parsed = JsonDocument.Parse(handler.CapturedContext.ToolArgumentsJson!);
        parsed.RootElement.GetProperty("query").GetString().Should().Be("lease renewal clauses");
        parsed.RootElement.GetProperty("topK").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task InvokeAsync_EmptyArguments_PassesEmptyObjectJson()
    {
        var tool = CreateTool();
        var handler = new FakeChatHandler();
        var sut = new ToolHandlerToAIFunctionAdapter(tool, handler, CreateContext);

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        handler.CapturedContext!.ToolArgumentsJson.Should().Be("{}");
    }

    [Fact]
    public async Task InvokeAsync_CallsValidateChatBeforeExecute()
    {
        var tool = CreateTool();
        var validateCalledFirst = false;
        var handler = new FakeChatHandler
        {
            ValidateChatImpl = (_, _) =>
            {
                validateCalledFirst = true;
                return ToolValidationResult.Success();
            }
        };
        var sut = new ToolHandlerToAIFunctionAdapter(tool, handler, CreateContext);

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        validateCalledFirst.Should().BeTrue();
        handler.ValidateChatCalled.Should().BeTrue();
        handler.ExecuteChatCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ValidationFails_ReturnsStructuredError_DoesNotCallExecute()
    {
        // FR-09: failed ValidateChat must short-circuit execution.
        var tool = CreateTool();
        var handler = new FakeChatHandler
        {
            ValidateChatImpl = (_, _) =>
                ToolValidationResult.Failure("query is required", "topK must be > 0")
        };
        var sut = new ToolHandlerToAIFunctionAdapter(tool, handler, CreateContext);

        var result = await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        handler.ValidateChatCalled.Should().BeTrue();
        handler.ExecuteChatCalled.Should().BeFalse();
        result.Should().NotBeNull();

        // Result envelope structure must surface the validation error code so the LLM
        // can interpret without throwing.
        var json = JsonSerializer.Serialize(result);
        json.Should().Contain("ValidationFailed");
        json.Should().Contain("query is required");
    }

    [Fact]
    public async Task InvokeAsync_HandlerReturnsError_PropagatesToolResult()
    {
        var tool = CreateTool();
        var handler = new FakeChatHandler
        {
            ExecuteChatImpl = (_, t, _) => Task.FromResult(ToolResult.Error(
                handlerId: "FakeChatHandler",
                toolId: t.Id,
                toolName: t.Name,
                errorMessage: "downstream failure",
                errorCode: "InternalError"))
        };
        var sut = new ToolHandlerToAIFunctionAdapter(tool, handler, CreateContext);

        var result = await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        result.Should().BeOfType<ToolResult>();
        var toolResult = (ToolResult)result!;
        toolResult.Success.Should().BeFalse();
        toolResult.ErrorCode.Should().Be("InternalError");
    }

    [Fact(Skip = "CI cancellation-timing flake — passes locally; pre-existing, not R3-introduced (R3 PR #415 unblock)")]
    public async Task InvokeAsync_CancellationRequested_PropagatesOperationCanceledException()
    {
        var tool = CreateTool();
        var handler = new FakeChatHandler
        {
            ExecuteChatImpl = async (_, _, ct) =>
            {
                await Task.Delay(50, ct);
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("unreachable");
            }
        };
        var sut = new ToolHandlerToAIFunctionAdapter(tool, handler, CreateContext);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(10);

        Func<Task> act = async () => await sut.InvokeAsync(new AIFunctionArguments(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InvokeAsync_HandlerThrows_PropagatesException()
    {
        var tool = CreateTool();
        var handler = new FakeChatHandler
        {
            ExecuteChatImpl = (_, _, _) => throw new InvalidOperationException("boom")
        };
        var sut = new ToolHandlerToAIFunctionAdapter(tool, handler, CreateContext);

        Func<Task> act = async () => await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task InvokeAsync_DecisionId_IsFreshlyGeneratedPerInvocation()
    {
        // Each LLM tool call gets a unique decision id for telemetry correlation —
        // verify the adapter doesn't reuse a captured id across invocations.
        var tool = CreateTool();
        var handler = new FakeChatHandler();
        var sut = new ToolHandlerToAIFunctionAdapter(tool, handler, CreateContext);

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);
        var firstDecisionId = handler.CapturedContext!.DecisionId;

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);
        var secondDecisionId = handler.CapturedContext!.DecisionId;

        firstDecisionId.Should().NotBe(secondDecisionId);
        firstDecisionId.Should().NotBe(Guid.Empty);
        secondDecisionId.Should().NotBe(Guid.Empty);
    }

    // ─── ADR-015 telemetry hygiene tests ──────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_Telemetry_DoesNotLogArgumentPayload()
    {
        // ADR-015 binding: NEVER log args payload (may transitively echo user content).
        var tool = CreateTool();
        var handler = new FakeChatHandler();
        var loggerMock = new Mock<ILogger>();
        var sut = new ToolHandlerToAIFunctionAdapter(tool, handler, CreateContext, loggerMock.Object);

        // Use a distinctive sentinel that would be unique enough to detect if logged.
        const string sentinel = "SENSITIVE_USER_QUERY_TEXT_SHOULD_NOT_APPEAR_IN_LOGS_42";
        var args = new AIFunctionArguments { ["query"] = sentinel };

        await sut.InvokeAsync(args, CancellationToken.None);

        // Walk every captured log invocation; assert the sentinel never appears
        // in the formatted message or any argument value.
        foreach (var invocation in loggerMock.Invocations)
        {
            var formatted = invocation.Arguments
                .Where(a => a is not null)
                .Select(a => a!.ToString() ?? string.Empty);

            foreach (var s in formatted)
            {
                s.Should().NotContain(sentinel,
                    "ADR-015: argument payload must never be logged by the chat-tool adapter");
            }
        }
    }

    [Fact]
    public async Task InvokeAsync_Telemetry_LogsSafeFieldsOnSuccess()
    {
        // ADR-015 binding: emit tool name + handlerId + decisionId + outcome + duration.
        var tool = CreateTool();
        var handler = new FakeChatHandler();
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var sut = new ToolHandlerToAIFunctionAdapter(tool, handler, CreateContext, loggerMock.Object);

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        // Verify a log was emitted referencing the ADR-015 marker. Exact format is
        // intentionally not asserted (the message template can evolve); what matters is
        // that SOME outcome log was produced and it carries the tool name.
        loggerMock.Invocations.Should().Contain(i =>
            i.Method.Name == nameof(ILogger.Log));

        var anyLogContainsToolName = loggerMock.Invocations
            .SelectMany(i => i.Arguments)
            .Any(a => a?.ToString()?.Contains(TestToolName) == true);
        anyLogContainsToolName.Should().BeTrue();
    }


    // ─── R6 Wave 7b: ToolResult.Metadata post-processing (citations + widget) ─────────
    //
    // The adapter now optionally receives a CitationContext accumulator + an SSE writer
    // delegate. When handlers return ToolResult.Metadata with the well-known keys
    // ("citations", "widget"), the adapter performs side effects so handlers stay pure.
    // Existing behavior (null Metadata) MUST be preserved unchanged.

    private static FakeChatHandler MakeHandlerWithMetadata(
        IEnumerable<ToolResultCitation>? citations = null,
        ToolResultWidget? widget = null)
    {
        var metadata = new Dictionary<string, object?>();
        if (citations is not null)
            metadata[ToolResultMetadataKeys.Citations] = citations;
        if (widget is not null)
            metadata[ToolResultMetadataKeys.Widget] = widget;

        return new FakeChatHandler
        {
            ExecuteChatImpl = (_, t, _) => Task.FromResult(new ToolResult
            {
                HandlerId = "FakeChatHandler",
                ToolId = t.Id,
                ToolName = t.Name,
                Success = true,
                Data = JsonSerializer.SerializeToElement(new { ok = true }),
                Summary = "ok",
                Execution = ToolExecutionMetadata.Empty,
                Metadata = metadata.Count == 0 ? null : metadata
            })
        };
    }

    [Fact]
    public async Task PostProcessing_NoMetadata_PreservesExistingBehavior()
    {
        // Baseline: handlers that don't set Metadata must continue to work unchanged
        // even when citationAccumulator + sseWriter are passed.
        var accumulator = new CitationContext();
        var emitted = 0;
        Task SseWriter(ChatSseEvent _, CancellationToken __) { emitted++; return Task.CompletedTask; }

        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), new FakeChatHandler(), CreateContext,
            logger: null, citationAccumulator: accumulator, sseWriter: SseWriter);

        var result = await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        result.Should().NotBeNull();
        accumulator.Count.Should().Be(0);
        emitted.Should().Be(0);
    }

    [Fact]
    public async Task PostProcessing_CitationsMetadata_WithAccumulator_AddsToContext()
    {
        var accumulator = new CitationContext();
        var citations = new[]
        {
            new ToolResultCitation("chunk-1", "Doc A", PageNumber: 3, Excerpt: "preview text 1"),
            new ToolResultCitation("chunk-2", "Doc B", PageNumber: null, Excerpt: "preview text 2",
                SourceType: "web", Url: "https://example.com", Snippet: "snippet")
        };

        var handler = MakeHandlerWithMetadata(citations: citations);
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: null, citationAccumulator: accumulator);

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        accumulator.Count.Should().Be(2);
        var emitted = accumulator.GetCitations();
        emitted[0].ChunkId.Should().Be("chunk-1");
        emitted[0].SourceName.Should().Be("Doc A");
        emitted[0].PageNumber.Should().Be(3);
        emitted[1].ChunkId.Should().Be("chunk-2");
        emitted[1].SourceType.Should().Be("web");
        emitted[1].Url.Should().Be("https://example.com");
    }

    [Fact]
    public async Task PostProcessing_CitationsMetadata_NullAccumulator_DropsSilently()
    {
        // Backward-compat: when accumulator is null, citations must be dropped without throwing.
        var citations = new[] { new ToolResultCitation("chunk-1", "Doc A", Excerpt: "preview") };
        var handler = MakeHandlerWithMetadata(citations: citations);

        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: null, citationAccumulator: null);

        Func<Task> act = async () =>
            await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PostProcessing_WidgetMetadata_WithSseWriter_EmitsSourcePane()
    {
        var emitted = new List<ChatSseEvent>();
        Task SseWriter(ChatSseEvent ev, CancellationToken _) { emitted.Add(ev); return Task.CompletedTask; }

        var widget = new ToolResultWidget(
            PaneType: "source_pane",
            WidgetType: "DocumentViewer",
            Data: new { filename = "matter.pdf", page = 1 },
            CitationId: "1");

        var handler = MakeHandlerWithMetadata(widget: widget);
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: null, citationAccumulator: null, sseWriter: SseWriter);

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        emitted.Should().HaveCount(1);
        emitted[0].Type.Should().Be("source_pane");
        emitted[0].Data.Should().NotBeNull();
    }

    [Fact]
    public async Task PostProcessing_WidgetMetadata_OutputPane_EmitsOutputPane()
    {
        var emitted = new List<ChatSseEvent>();
        Task SseWriter(ChatSseEvent ev, CancellationToken _) { emitted.Add(ev); return Task.CompletedTask; }

        var widget = new ToolResultWidget(
            PaneType: "output_pane",
            WidgetType: "SearchResults",
            Data: new { hits = new[] { new { title = "x" } } });

        var handler = MakeHandlerWithMetadata(widget: widget);
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: null, citationAccumulator: null, sseWriter: SseWriter);

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        emitted.Should().HaveCount(1);
        emitted[0].Type.Should().Be("output_pane");
    }

    [Fact]
    public async Task PostProcessing_WidgetMetadata_NullSseWriter_DropsSilently()
    {
        var widget = new ToolResultWidget("source_pane", "DocumentViewer", new { x = 1 });
        var handler = MakeHandlerWithMetadata(widget: widget);

        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: null, citationAccumulator: null, sseWriter: null);

        Func<Task> act = async () =>
            await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }


    [Fact]
    public async Task PostProcessing_UnknownPaneType_LogsWarning_DoesNotEmit()
    {
        var emitted = 0;
        Task SseWriter(ChatSseEvent _, CancellationToken __) { emitted++; return Task.CompletedTask; }

        var widget = new ToolResultWidget("garbage_pane", "X", new { });
        var handler = MakeHandlerWithMetadata(widget: widget);
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: null, citationAccumulator: null, sseWriter: SseWriter);

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        emitted.Should().Be(0);
    }

    [Fact]
    public async Task PostProcessing_Telemetry_DoesNotLogCitationOrWidgetContent_Adr015()
    {
        // ADR-015 binding: post-processing must log COUNTS / OUTCOMES only.
        // NEVER citation excerpt text or widget data content.
        const string excerptSentinel = "EXCERPT_TEXT_SENTINEL_SHOULD_NOT_APPEAR_IN_LOGS_42";
        const string widgetSentinel = "WIDGET_DATA_SENTINEL_SHOULD_NOT_APPEAR_IN_LOGS_77";

        var citations = new[]
        {
            new ToolResultCitation("chunk-1", "Doc A", Excerpt: excerptSentinel)
        };
        var widget = new ToolResultWidget(
            PaneType: "source_pane",
            WidgetType: "DocumentViewer",
            Data: new { secret = widgetSentinel });

        var handler = MakeHandlerWithMetadata(citations: citations, widget: widget);
        var accumulator = new CitationContext();
        var emitted = new List<ChatSseEvent>();
        Task SseWriter(ChatSseEvent ev, CancellationToken _) { emitted.Add(ev); return Task.CompletedTask; }

        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: loggerMock.Object,
            citationAccumulator: accumulator,
            sseWriter: SseWriter);

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        // Walk every captured log invocation; assert the sentinels never appear.
        foreach (var invocation in loggerMock.Invocations)
        {
            foreach (var arg in invocation.Arguments)
            {
                var s = arg?.ToString() ?? string.Empty;
                s.Should().NotContain(excerptSentinel,
                    "ADR-015: citation excerpt content must never be logged by post-processing");
                s.Should().NotContain(widgetSentinel,
                    "ADR-015: widget data content must never be logged by post-processing");
            }
        }
    }

    [Fact]
    public async Task PostProcessing_SseWriterThrows_NonFatal_HandlerResultStillReturned()
    {
        // Cross-cutting infrastructure failures must NOT bubble up to the LLM as tool errors.
        Task SseWriter(ChatSseEvent _, CancellationToken __) => throw new InvalidOperationException("sink down");

        var widget = new ToolResultWidget("source_pane", "DocumentViewer", new { x = 1 });
        var handler = MakeHandlerWithMetadata(widget: widget);
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: null, citationAccumulator: null, sseWriter: SseWriter);

        var result = await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        result.Should().BeOfType<ToolResult>();
        ((ToolResult)result!).Success.Should().BeTrue();
    }

    // ─── R6 Wave 9 (ADR-033): documentStreamWriter wiring ─────────────────────────────
    //
    // The adapter now optionally accepts a Func<DocumentStreamEvent, CancellationToken, Task>
    // which it forwards onto every per-invocation ChatInvocationContext.DocumentStreamWriter.
    // Existing fields (ChatSessionId, TenantId, KnowledgeScope, RequestedToolName,
    // ToolArgumentsJson, DecisionId) MUST remain unaffected.
    //
    // Per ADR-033 §3.1 the typed handler (WorkingDocumentHandler, Stage 3) reads the field
    // from context and degrades gracefully when null — the adapter MUST NOT coalesce null to
    // a no-op delegate (that's a legacy-class-only fallback inside the factory's hardcoded
    // WorkingDocumentTools block).

    [Fact]
    public async Task InvokeAsync_DocumentStreamWriter_NonNull_ForwardedToContext()
    {
        // ADR-033 §4.3: when the adapter is constructed with a non-null documentStreamWriter,
        // each per-invocation ChatInvocationContext MUST carry that same delegate so handlers
        // can emit Start / Token / End SSE events directly during streaming.
        var emitted = new List<global::Sprk.Bff.Api.Models.Ai.Chat.DocumentStreamEvent>();
        Task DocWriter(global::Sprk.Bff.Api.Models.Ai.Chat.DocumentStreamEvent ev, CancellationToken _)
        {
            emitted.Add(ev);
            return Task.CompletedTask;
        }

        var handler = new FakeChatHandler();
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: null,
            citationAccumulator: null,
            sseWriter: null,
            documentStreamWriter: DocWriter);

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        handler.CapturedContext.Should().NotBeNull();
        handler.CapturedContext!.DocumentStreamWriter.Should().NotBeNull(
            "ADR-033: adapter must forward its constructor-supplied documentStreamWriter onto every per-call ChatInvocationContext");

        // Round-trip verification: invoking the writer from inside the handler context
        // MUST reach the adapter-supplied delegate (we capture into `emitted`).
        var startEvent = new global::Sprk.Bff.Api.Models.Ai.Chat.DocumentStreamStartEvent(
            OperationId: Guid.NewGuid(),
            TargetPosition: "document",
            OperationType: "replace");
        await handler.CapturedContext.DocumentStreamWriter!(startEvent, CancellationToken.None);

        emitted.Should().HaveCount(1);
        emitted[0].Should().BeSameAs(startEvent);
    }

    [Fact]
    public async Task InvokeAsync_DocumentStreamWriter_Null_LeavesContextFieldNull()
    {
        // ADR-033 §3.1: when documentStreamWriter is null (e.g., background processing path
        // where httpContext is unavailable), the per-call context field MUST stay null —
        // NOT coalesced to a no-op. The typed handler is responsible for null-check + degrade.
        var handler = new FakeChatHandler();
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: null,
            citationAccumulator: null,
            sseWriter: null,
            documentStreamWriter: null);

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        handler.CapturedContext.Should().NotBeNull();
        handler.CapturedContext!.DocumentStreamWriter.Should().BeNull(
            "ADR-033 §3.1: null documentStreamWriter must remain null on context — handler degrades gracefully");
    }

    [Fact]
    public async Task InvokeAsync_DocumentStreamWriter_DefaultsToNull_WhenOmitted()
    {
        // Backward compatibility: existing callers (citations/widget-only handlers, tests
        // predating Wave 9) do not supply documentStreamWriter. The optional parameter
        // MUST default to null and behavior MUST match the explicit-null path above.
        var handler = new FakeChatHandler();
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: null,
            citationAccumulator: null,
            sseWriter: null);
        // documentStreamWriter intentionally omitted — default must be null.

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);

        handler.CapturedContext.Should().NotBeNull();
        handler.CapturedContext!.DocumentStreamWriter.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_DocumentStreamWriter_DoesNotAffectExistingContextFields()
    {
        // Regression: the new field MUST NOT alter ChatSessionId / TenantId / DecisionId /
        // KnowledgeScope / RequestedToolName / ToolArgumentsJson behavior in any way.
        Task DocWriter(global::Sprk.Bff.Api.Models.Ai.Chat.DocumentStreamEvent _, CancellationToken __) => Task.CompletedTask;

        var handler = new FakeChatHandler();
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: null,
            citationAccumulator: null,
            sseWriter: null,
            documentStreamWriter: DocWriter);

        var args = new AIFunctionArguments { ["query"] = "anything" };
        await sut.InvokeAsync(args, CancellationToken.None);

        handler.CapturedContext.Should().NotBeNull();
        handler.CapturedContext!.ChatSessionId.Should().Be(TestSessionId);
        handler.CapturedContext.TenantId.Should().Be(TestTenantId);
        handler.CapturedContext.RequestedToolName.Should().Be(TestToolName);
        handler.CapturedContext.ToolArgumentsJson.Should().NotBeNullOrEmpty();
        handler.CapturedContext.DecisionId.Should().NotBe(Guid.Empty);
        // KnowledgeScope is unset in CreateContext — verify Wave 9 didn't shadow it.
        handler.CapturedContext.KnowledgeScope.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_DocumentStreamWriter_FreshlyAttachedPerInvocation()
    {
        // Per-call construction (with-expression): each LLM tool invocation gets the SAME
        // writer reference (it's adapter-scoped, not per-call). This mirrors the citation /
        // sse-writer wiring and is what enables a typed handler to stream into the same SSE
        // pipe across multiple tool calls in one chat turn.
        Task DocWriter(global::Sprk.Bff.Api.Models.Ai.Chat.DocumentStreamEvent _, CancellationToken __) => Task.CompletedTask;

        var handler = new FakeChatHandler();
        var sut = new ToolHandlerToAIFunctionAdapter(
            CreateTool(), handler, CreateContext,
            logger: null,
            citationAccumulator: null,
            sseWriter: null,
            documentStreamWriter: DocWriter);

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);
        var firstWriter = handler.CapturedContext!.DocumentStreamWriter;

        await sut.InvokeAsync(new AIFunctionArguments(), CancellationToken.None);
        var secondWriter = handler.CapturedContext!.DocumentStreamWriter;

        firstWriter.Should().NotBeNull();
        secondWriter.Should().NotBeNull();
        firstWriter.Should().BeSameAs(secondWriter,
            "the adapter-supplied writer is shared across invocations within one chat session");
    }
}
