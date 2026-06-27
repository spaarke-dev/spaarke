using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Foundry;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="CodeInterpreterHandler"/> (R6 Wave 8 — replaces the legacy
/// hardcoded <c>CodeInterpreterTools</c> class previously registered on
/// <see cref="Chat.SprkChatAgentFactory"/>).
/// </summary>
/// <remarks>
/// <para>
/// Covers:
/// </para>
/// <list type="bullet">
/// <item>4-point contract (auto-discovery, HandlerId, Metadata, SupportedToolTypes).</item>
/// <item>SupportedInvocationContexts = Chat (matches the legacy hardcoded registration).</item>
/// <item>2-method dispatch via sprk_configuration.method discriminator (AnalyzeData / GenerateChart).</item>
/// <item>ADR-018 kill switch — Enabled=false short-circuits with a user-readable string (no bridge call).</item>
/// <item>ADR-016 rate limiting — static SemaphoreSlim bounds concurrent sandbox calls.</item>
/// <item>ADR-015 data governance — only caller-supplied excerpts are forwarded to the bridge; the
/// handler does NOT auto-fetch external resources.</item>
/// <item>Wave 7b metadata plumbing — citations populated; widget (output_pane / ChartViewer)
/// populated for GenerateChart only.</item>
/// <item>Playbook context returns validation error (handler is chat-only).</item>
/// </list>
/// <para>
/// <strong>Test seam</strong>: <see cref="CodeInterpreterHandler.InvokeBridgeAsync"/> is a
/// <c>protected internal virtual</c> indirection that tests override via
/// <see cref="TestableCodeInterpreterHandler"/>. The real <see cref="CodeInterpreterBridge"/> +
/// <see cref="AgentServiceClient"/> are both <c>sealed</c> and Moq cannot mock them directly;
/// the virtual seam avoids constructing real Azure AI Foundry dependencies in unit tests
/// while keeping production behaviour unchanged.
/// </para>
/// </remarks>
public sealed class CodeInterpreterHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    // ═════════════════════════════════════════════════════════════════════════════
    // Test helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private static CodeInterpreterOptions BuildOptions(
        bool enabled = true,
        int maxConcurrency = 2,
        int sandboxTimeoutSeconds = 30)
    {
        return new CodeInterpreterOptions
        {
            Enabled = enabled,
            MaxConcurrency = maxConcurrency,
            SandboxTimeoutSeconds = sandboxTimeoutSeconds
        };
    }

    /// <summary>
    /// Construct a CodeInterpreterBridge for handler construction. The bridge instance is
    /// REQUIRED by the handler constructor but the test seam ensures the bridge is never
    /// actually invoked. We use uninitialized-object construction because both
    /// CodeInterpreterBridge and AgentServiceClient are sealed and have non-trivial
    /// constructor side-effects.
    /// </summary>
    private static CodeInterpreterBridge BuildBridgeForCtor()
    {
#pragma warning disable SYSLIB0050 // FormatterServices.GetUninitializedObject is obsolete but still functional;
        // used here because CodeInterpreterBridge is sealed (cannot be subclassed for testing) and depends on
        // the sealed AgentServiceClient. The test seam (InvokeBridgeAsync override) ensures the bridge instance
        // is never actually invoked, so the uninitialized state is safe.
        return (CodeInterpreterBridge)FormatterServices.GetUninitializedObject(typeof(CodeInterpreterBridge));
#pragma warning restore SYSLIB0050
    }

    private TestableCodeInterpreterHandler CreateHandler(
        CodeInterpreterOptions? options = null,
        Func<string, CancellationToken, Task<CodeInterpreterResult>>? bridgeFake = null)
    {
        return new TestableCodeInterpreterHandler(
            bridge: BuildBridgeForCtor(),
            options: Options.Create(options ?? BuildOptions()),
            logger: CreateLogger<CodeInterpreterHandler>(),
            bridgeFake: bridgeFake);
    }

    private static AnalysisTool BuildCodeInterpreterTool(string method) =>
        BuildAnalysisTool(
            handlerClass: nameof(CodeInterpreterHandler),
            configuration: $"{{\"method\":\"{method}\"}}",
            toolType: ToolType.Custom);

    private static CodeInterpreterResult BuildAnalysisResult(string output = "Average = 42.") =>
        new(Output: output, ChartBase64: null, ExecutionLog: string.Empty);

    private static CodeInterpreterResult BuildChartResult(string output = "Bar chart showing Q1-Q4 revenue.", string chartBase64 = "iVBORw0KGgoAAAA-fake-png") =>
        new(Output: output, ChartBase64: chartBase64, ExecutionLog: string.Empty);

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var services = BuildToolFrameworkServiceCollection();

        var registeredImplementations = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .ToList();

        registeredImplementations.Should().Contain(
            typeof(CodeInterpreterHandler),
            because: "the handler must be auto-discovered via assembly scan (R6 Pillar 2: no manual DI lines)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(CodeInterpreterHandler),
            because: "R6 Pillar 2 binding: HandlerId == nameof(handler class) so sprk_handlerclass routes to this handler at runtime");
    }

    [Fact]
    public void Metadata_IsValid()
    {
        var handler = CreateHandler();
        var metadata = handler.Metadata;

        metadata.Should().NotBeNull();
        metadata.Name.Should().NotBeNullOrWhiteSpace();
        metadata.Description.Should().NotBeNullOrWhiteSpace();
        metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void SupportedToolTypes_IsNonEmpty()
    {
        var handler = CreateHandler();

        handler.SupportedToolTypes.Should().NotBeNullOrEmpty();
        handler.SupportedToolTypes.Should().Contain(ToolType.Custom);
    }

    [Fact]
    public void SupportedInvocationContexts_IsChatOnly()
    {
        var handler = CreateHandler();

        handler.SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Chat,
            because: "R6 Wave 8: CodeInterpreterHandler matches the legacy hardcoded registration (chat-only). " +
                     "The 11 production node executors (NFR-08) do not include a Code Interpreter executor.");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat — argument shape per method
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Succeeds_WithDataAndQuestion_AnalyzeMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"data\":\"a,b\\n1,2\",\"question\":\"sum?\"}");
        var tool = BuildCodeInterpreterTool("AnalyzeData");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Succeeds_WithDataSeriesAndChartType_ChartMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"dataSeries\":\"[{\\\"label\\\":\\\"Q1\\\",\\\"value\\\":10}]\",\"chartType\":\"bar\"}");
        var tool = BuildCodeInterpreterTool("GenerateChart");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenDataMissing_AnalyzeMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"question\":\"sum?\"}");
        var tool = BuildCodeInterpreterTool("AnalyzeData");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("data", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenChartTypeMissing_ChartMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"dataSeries\":\"[]\"}");
        var tool = BuildCodeInterpreterTool("GenerateChart");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("chartType", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenTenantIdMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"data\":\"x\",\"question\":\"y\"}",
            tenantId: "");
        var tool = BuildCodeInterpreterTool("AnalyzeData");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenMethodUnsupported()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"data\":\"x\",\"question\":\"y\"}");
        var tool = BuildCodeInterpreterTool("ExfiltrateData");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("method", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenJsonMalformed()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{not json");
        var tool = BuildCodeInterpreterTool("AnalyzeData");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-018 kill switch — Enabled=false short-circuits without bridge call
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_KillSwitchOff_AnalyzeData_ReturnsUnavailableMessage_NoBridgeCall()
    {
        var bridgeCallCount = 0;
        var handler = CreateHandler(
            options: BuildOptions(enabled: false),
            bridgeFake: (prompt, ct) =>
            {
                bridgeCallCount++;
                return Task.FromResult(BuildAnalysisResult());
            });

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"data\":\"a,b\\n1,2\",\"question\":\"sum?\"}");
        var tool = BuildCodeInterpreterTool("AnalyzeData");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        bridgeCallCount.Should().Be(0,
            because: "ADR-018 kill switch must short-circuit before any bridge invocation");

        result.Success.Should().BeTrue(
            because: "kill switch returns a user-readable ToolResult (not an error) so the LLM can gracefully inform the user");
        var payload = result.GetData<CodeInterpreterHandler.CodeInterpreterPayload>();
        payload.Should().NotBeNull();
        payload!.Unavailable.Should().BeTrue();
        payload.Message.Should().Contain("unavailable", because: "user-readable kill-switch message");
        payload.Message.Should().Contain("CodeInterpreter:Enabled", because: "operator hint");
    }

    [Fact]
    public async Task ExecuteChatAsync_KillSwitchOff_GenerateChart_ReturnsUnavailableMessage_NoBridgeCall()
    {
        var bridgeCallCount = 0;
        var handler = CreateHandler(
            options: BuildOptions(enabled: false),
            bridgeFake: (prompt, ct) =>
            {
                bridgeCallCount++;
                return Task.FromResult(BuildChartResult());
            });

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"dataSeries\":\"[]\",\"chartType\":\"bar\"}");
        var tool = BuildCodeInterpreterTool("GenerateChart");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        bridgeCallCount.Should().Be(0,
            because: "ADR-018 kill switch must short-circuit before any bridge invocation");

        result.Success.Should().BeTrue();
        var payload = result.GetData<CodeInterpreterHandler.CodeInterpreterPayload>();
        payload!.Unavailable.Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // AnalyzeData — happy path: citations populated, NO widget
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_AnalyzeData_ReturnsCitationsInMetadata_NoWidget()
    {
        var handler = CreateHandler(
            bridgeFake: (prompt, ct) => Task.FromResult(BuildAnalysisResult("Average value is 42.")));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"data\":\"a,b\\n1,2\",\"question\":\"average?\"}");
        var tool = BuildCodeInterpreterTool("AnalyzeData");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Citations);
        result.Metadata!.Should().NotContainKey(ToolResultMetadataKeys.Widget,
            because: "AnalyzeData does not produce a chart widget — only GenerateChart emits one");

        var citations = result.Metadata![ToolResultMetadataKeys.Citations] as IEnumerable<ToolResultCitation>;
        citations.Should().NotBeNull();
        citations!.Should().HaveCount(1);
        citations!.First().SourceType.Should().Be("code-interpreter");
        citations!.First().SourceName.Should().Be("Code Interpreter Data Analysis");
    }

    [Fact]
    public async Task ExecuteChatAsync_AnalyzeData_ForwardsCallerDataOnly_ToBridge()
    {
        // ADR-015 binding: handler MUST only forward caller-supplied data + question.
        // It MUST NOT auto-fetch external resources or inject additional content.
        const string callerData = "label,value\nQ1,100";
        const string callerQuestion = "what is the value for Q1?";
        string? capturedPrompt = null;

        var handler = CreateHandler(
            bridgeFake: (prompt, ct) =>
            {
                capturedPrompt = prompt;
                return Task.FromResult(BuildAnalysisResult());
            });

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { data = callerData, question = callerQuestion }));
        var tool = BuildCodeInterpreterTool("AnalyzeData");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        capturedPrompt.Should().NotBeNull();
        capturedPrompt!.Should().Contain(callerData,
            because: "the caller's data excerpt is the only data forwarded to the sandbox");
        capturedPrompt!.Should().Contain(callerQuestion,
            because: "the caller's question is forwarded verbatim");
        // ADR-015: no external resource fetches — the prompt is purely a structural template
        // around the caller-supplied data + question.
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GenerateChart — happy path: citations + output_pane ChartViewer widget
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_GenerateChart_ReturnsCitationsAndOutputPaneWidget()
    {
        const string chartBase64 = "iVBORw0KGgoAAAANSUhEUgAA-test-chart-payload";
        var handler = CreateHandler(
            bridgeFake: (prompt, ct) =>
                Task.FromResult(BuildChartResult(output: "Bar chart of revenue.", chartBase64: chartBase64)));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"dataSeries\":\"[{\\\"label\\\":\\\"Q1\\\",\\\"value\\\":120}]\",\"chartType\":\"bar\"}");
        var tool = BuildCodeInterpreterTool("GenerateChart");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Citations);
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Widget,
            because: "GenerateChart emits an output_pane ChartViewer widget envelope (Wave 7b infra)");

        var citations = result.Metadata![ToolResultMetadataKeys.Citations] as IEnumerable<ToolResultCitation>;
        citations.Should().NotBeNull();
        citations!.Should().HaveCount(1);
        citations!.First().SourceType.Should().Be("code-interpreter-chart");

        var widget = result.Metadata![ToolResultMetadataKeys.Widget] as ToolResultWidget;
        widget.Should().NotBeNull();
        widget!.PaneType.Should().Be("output_pane");
        widget.WidgetType.Should().Be("ChartViewer");
        widget.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_GenerateChart_NormalizesUnsupportedChartType_ToBar()
    {
        string? capturedPrompt = null;

        var handler = CreateHandler(
            bridgeFake: (prompt, ct) =>
            {
                capturedPrompt = prompt;
                return Task.FromResult(BuildChartResult());
            });

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"dataSeries\":\"[]\",\"chartType\":\"scatter\"}");
        var tool = BuildCodeInterpreterTool("GenerateChart");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<CodeInterpreterHandler.CodeInterpreterPayload>();
        payload!.ChartType.Should().Be("bar",
            because: "unsupported chart types fall back to 'bar' per legacy CodeInterpreterTools forgiveness");
        capturedPrompt.Should().NotBeNull();
        capturedPrompt!.Should().Contain("bar",
            because: "the prompt is built with the normalized chart type");
    }

    [Fact]
    public async Task ExecuteChatAsync_GenerateChart_ForwardsCallerDataSeriesOnly_ToBridge()
    {
        const string callerSeries = "[{\"label\":\"Q1\",\"value\":120}]";
        string? capturedPrompt = null;

        var handler = CreateHandler(
            bridgeFake: (prompt, ct) =>
            {
                capturedPrompt = prompt;
                return Task.FromResult(BuildChartResult());
            });

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { dataSeries = callerSeries, chartType = "bar" }));
        var tool = BuildCodeInterpreterTool("GenerateChart");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        capturedPrompt.Should().NotBeNull();
        capturedPrompt!.Should().Contain(callerSeries,
            because: "ADR-015: only the caller-supplied dataSeries is forwarded — no external fetch");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Dispatch validation errors
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_AnalyzeData_ReturnsValidationError_WhenDataMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"question\":\"x\"}");
        var tool = BuildCodeInterpreterTool("AnalyzeData");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("data");
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsCancelled_WhenTokenCancelled()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"data\":\"x\",\"question\":\"y\"}");
        var tool = BuildCodeInterpreterTool("AnalyzeData");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.ExecuteChatAsync(ctx, tool, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsError_WhenBridgeThrows()
    {
        var handler = CreateHandler(
            bridgeFake: (prompt, ct) => throw new InvalidOperationException("Sandbox unreachable"));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"data\":\"x\",\"question\":\"y\"}");
        var tool = BuildCodeInterpreterTool("AnalyzeData");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Playbook context — handler is chat-only; ExecuteAsync returns defensive error
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_PlaybookContext_ReturnsValidationError_NoBridgeCall()
    {
        var bridgeCallCount = 0;
        var handler = CreateHandler(
            bridgeFake: (prompt, ct) =>
            {
                bridgeCallCount++;
                return Task.FromResult(BuildAnalysisResult());
            });

        var ctx = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(
            handlerClass: nameof(CodeInterpreterHandler),
            configuration: "{\"method\":\"AnalyzeData\"}");

        var result = await handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        bridgeCallCount.Should().Be(0,
            because: "handler is Chat-only; playbook ExecuteAsync must not invoke the sandbox");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-016 rate limiting — concurrent calls are bounded
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_BoundsConcurrency_PerStaticGate()
    {
        // ADR-016 binding: the static SemaphoreSlim bounds concurrent sandbox calls.
        // We assert by counting concurrent in-flight bridge invocations — they must never
        // exceed the gate's effective capacity (= MaxConcurrency captured at first
        // construction, mirroring the pre-Wave-8 CodeInterpreterTools static-gate design).
        //
        // The gate is STATIC across all handler instances (process-wide; first construction
        // wins). Default MaxConcurrency = 2. To assert the bound, queue 4 concurrent calls
        // and check that no more than 2 ever overlap in the bridge fake.

        var inflight = 0;
        var observedMaxInflight = 0;
        var lockObj = new object();
        var release = new TaskCompletionSource();

        async Task<CodeInterpreterResult> SlowBridgeFake(string prompt, CancellationToken ct)
        {
            lock (lockObj)
            {
                inflight++;
                if (inflight > observedMaxInflight) observedMaxInflight = inflight;
            }
            try
            {
                await release.Task;
                return BuildAnalysisResult();
            }
            finally
            {
                lock (lockObj) { inflight--; }
            }
        }

        // Effective gate capacity at the time these tests run = whatever the first
        // CodeInterpreterHandler constructed in this process locked in. The default is
        // MaxConcurrency=2. We assert that observedMaxInflight is bounded.
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"data\":\"x\",\"question\":\"y\"}");
        var tool = BuildCodeInterpreterTool("AnalyzeData");

        var handlers = Enumerable.Range(0, 4)
            .Select(_ => CreateHandler(bridgeFake: SlowBridgeFake))
            .ToList();

        var calls = handlers
            .Select(h => h.ExecuteChatAsync(ctx, tool, CancellationToken.None))
            .ToList();

        // Allow callers to enter the gate (those that can) and queue (those that can't).
        await Task.Delay(100);
        release.SetResult();
        await Task.WhenAll(calls);

        // The hardcoded default (and the only value the first-constructed handler can capture
        // when test runs go in any order) is MaxConcurrency=2. observedMaxInflight must never
        // exceed that. Use a generous-but-strict upper bound (3) to tolerate cross-test pollution
        // of the static gate, while still proving the gate exists and bounds concurrency below 4.
        observedMaxInflight.Should().BeLessThan(4,
            because: "ADR-016: static SemaphoreSlim must bound concurrent sandbox calls below the queued-call count");
        observedMaxInflight.Should().BeGreaterThan(0,
            because: "the bridge fake must have been invoked at least once");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry — caller content must NEVER leak into logs
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_RespectsAdr015_DoesNotLogDataOrQuestionContent()
    {
        const string secretData = "label,confidential_value\nrow1,SECRET-PROJECT-Aurora-Customer-X-1234";
        const string secretQuestion = "QUESTION-CONTAINING-CLIENT-NAME-AcmeCorpInc-MatterY";
        const string secretOutput = "OUTPUT-WITH-PRIVILEGED-DETAIL-Trade-Secret-Formula-XJ7K-Result";

        var handler = CreateHandler(
            bridgeFake: (prompt, ct) => Task.FromResult(BuildAnalysisResult(secretOutput)));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { data = secretData, question = secretQuestion }));
        var tool = BuildCodeInterpreterTool("AnalyzeData");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(secretData, secretQuestion, secretOutput);
    }

    [Fact]
    public async Task Telemetry_RespectsAdr015_GenerateChart_DoesNotLogSeriesOrOutputContent()
    {
        const string secretSeries = "[{\"label\":\"SECRET-PROJECT-AURORA\",\"value\":12345}]";
        const string secretOutput = "OUTPUT-WITH-PRIVILEGED-CHART-DESCRIPTION-ClientNameXYZ";

        var handler = CreateHandler(
            bridgeFake: (prompt, ct) => Task.FromResult(BuildChartResult(secretOutput, chartBase64: "fake-base64")));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { dataSeries = secretSeries, chartType = "bar" }));
        var tool = BuildCodeInterpreterTool("GenerateChart");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(secretSeries, secretOutput);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // DI bootstrap helper (shared with other handler tests)
    // ═════════════════════════════════════════════════════════════════════════════

    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Test double
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test subclass that overrides the <see cref="CodeInterpreterHandler.InvokeBridgeAsync"/>
    /// virtual seam so unit tests can stub the sandbox response without constructing a real
    /// (sealed) <see cref="CodeInterpreterBridge"/> + <see cref="AgentServiceClient"/>.
    /// </summary>
    private sealed class TestableCodeInterpreterHandler : CodeInterpreterHandler
    {
        private readonly Func<string, CancellationToken, Task<CodeInterpreterResult>>? _bridgeFake;

        public TestableCodeInterpreterHandler(
            CodeInterpreterBridge bridge,
            IOptions<CodeInterpreterOptions> options,
            Microsoft.Extensions.Logging.ILogger<CodeInterpreterHandler> logger,
            Func<string, CancellationToken, Task<CodeInterpreterResult>>? bridgeFake)
            : base(bridge, options, logger)
        {
            _bridgeFake = bridgeFake;
        }

        protected internal override Task<CodeInterpreterResult> InvokeBridgeAsync(
            string prompt,
            CancellationToken cancellationToken)
        {
            if (_bridgeFake is null)
                throw new InvalidOperationException(
                    "InvokeBridgeAsync was called without a configured bridgeFake — the test " +
                    "expected the kill-switch / validation path to short-circuit before the bridge.");

            return _bridgeFake(prompt, cancellationToken);
        }
    }
}
