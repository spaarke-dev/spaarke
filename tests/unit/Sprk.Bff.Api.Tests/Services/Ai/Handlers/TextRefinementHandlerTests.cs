using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="TextRefinementHandler"/> (R6 Wave 7 Q9 batch migration —
/// trivial chat-tool group).
/// </summary>
/// <remarks>
/// Covers: 4-point contract; 3-method dispatch via sprk_configuration.method
/// discriminator (refine / keypoints / summary); chat + playbook execution paths;
/// validation; ADR-015 telemetry hygiene (no input text, no instruction, no output content);
/// tenantId enforcement (ADR-014).
/// </remarks>
public sealed class TextRefinementHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    private readonly Mock<IChatClient> _chatClientMock = new();

    private TextRefinementHandler CreateHandler() => new(
        _chatClientMock.Object,
        CreateLogger<TextRefinementHandler>());

    private void SetupChatResponse(string text)
    {
        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]));
    }

    private static AnalysisTool BuildTextTool(string method, string? extraConfig = null)
    {
        var config = extraConfig is null
            ? $"{{\"method\":\"{method}\"}}"
            : $"{{\"method\":\"{method}\",{extraConfig}}}";
        return BuildAnalysisTool(
            handlerClass: nameof(TextRefinementHandler),
            configuration: config,
            toolType: ToolType.Custom);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);

        var registeredImplementations = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .ToList();

        registeredImplementations.Should().Contain(
            typeof(TextRefinementHandler),
            because: "the handler must be auto-discovered via assembly scan (R6 Pillar 2: no manual DI lines)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(TextRefinementHandler),
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
    public void SupportedInvocationContexts_IsBoth()
    {
        var handler = CreateHandler();

        handler.SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Both,
            because: "Wave 7 Q9 migration requires TextRefinementHandler to be invocable from both playbook and chat-driven function calling");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Validate (playbook) — happy path + required-field failures
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_Succeeds_WithValidContextAndConfig()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some document text.");
        var tool = BuildTextTool("refine");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenExtractedTextIsEmpty()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "");
        var tool = BuildTextTool("refine");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("extracted text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenTenantIdIsMissing()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text", tenantId: "");
        var tool = BuildTextTool("refine");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenConfigurationMissingMethod()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(TextRefinementHandler),
            configuration: "{}",
            toolType: ToolType.Custom);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("method", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenMethodIsUnknown()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text");
        var tool = BuildTextTool("translate"); // not supported

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("method", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenSummaryFormatInvalid()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text");
        var tool = BuildTextTool("summary", "\"format\":\"haiku\"");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("format", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Succeeds_WithTextArgument()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{\"text\":\"Hello.\"}");
        var tool = BuildTextTool("refine");

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenTextMissing()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildTextTool("refine");

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenTenantIdMissing()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: "{\"text\":\"Hello.\"}",
            tenantId: "");
        var tool = BuildTextTool("refine");

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenJsonMalformed()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{not json");
        var tool = BuildTextTool("refine");

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteChatAsync — method dispatch (refine / keypoints / summary)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_RefineMethod_CallsChatClient_AndReturnsRefinedText()
    {
        const string refined = "This is the refined paragraph.";
        SetupChatResponse(refined);

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: "{\"text\":\"raw input\",\"instruction\":\"make formal\"}");
        var tool = BuildTextTool("refine");

        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.GetData<TextRefinementHandler.TextRefinementResult>();
        data.Should().NotBeNull();
        data!.Method.Should().Be("refine");
        data.Text.Should().Be(refined);
        _chatClientMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteChatAsync_RefineMethod_Fails_WhenInstructionMissing()
    {
        SetupChatResponse("anything");

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{\"text\":\"raw input\"}");
        var tool = BuildTextTool("refine");

        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("instruction", because: "refine requires the instruction parameter");
    }

    [Fact]
    public async Task ExecuteChatAsync_KeypointsMethod_CallsChatClient_AndReturnsBullets()
    {
        const string bullets = "- Point A\n- Point B\n- Point C";
        SetupChatResponse(bullets);

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: "{\"text\":\"some paragraph\",\"maxPoints\":3}");
        var tool = BuildTextTool("keypoints");

        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.GetData<TextRefinementHandler.TextRefinementResult>();
        data!.Method.Should().Be("keypoints");
        data.Text.Should().Be(bullets);
    }

    [Theory]
    [InlineData("bullet")]
    [InlineData("paragraph")]
    [InlineData("tldr")]
    public async Task ExecuteChatAsync_SummaryMethod_AcceptsAllSupportedFormats(string format)
    {
        const string output = "TL;DR: This is the summary.";
        SetupChatResponse(output);

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"text\":\"some content\",\"format\":\"{format}\"}}");
        var tool = BuildTextTool("summary");

        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.GetData<TextRefinementHandler.TextRefinementResult>();
        data!.Method.Should().Be("summary");
        data.Text.Should().Be(output);
    }

    [Fact]
    public async Task ExecuteChatAsync_Fails_WhenTextEmpty()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildTextTool("summary");

        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task ExecuteChatAsync_OperationCancelled_ReturnsCancelledErrorCode()
    {
        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("simulated cancel"));

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: "{\"text\":\"some content\"}");
        var tool = BuildTextTool("summary");

        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    [Fact]
    public async Task ExecuteChatAsync_UnexpectedException_ReturnsInternalError()
    {
        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("downstream blew up"));

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: "{\"text\":\"some content\"}");
        var tool = BuildTextTool("summary");

        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync (playbook path) — uses extracted text + action system prompt
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_PlaybookPath_UsesExtractedText()
    {
        const string output = "Refined output.";
        SetupChatResponse(output);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(
            extractedText: "This is the document body to refine.",
            actionSystemPrompt: "Make this more formal.");
        var tool = BuildTextTool("refine");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.GetData<TextRefinementHandler.TextRefinementResult>();
        data!.Method.Should().Be("refine");
        data.Text.Should().Be(output);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry hygiene
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_DoesNotLeakInputText_OutputContent_OrInstruction()
    {
        const string secretInput = "TOPSECRET-DOCUMENT-BODY-PARAGRAPH-1";
        const string secretInstruction = "TOPSECRET-INSTRUCTION-PHRASE-XYZ";
        const string secretOutput = "TOPSECRET-MODEL-RESPONSE-ABCDEFG";

        SetupChatResponse(secretOutput);

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"text\":\"{secretInput}\",\"instruction\":\"{secretInstruction}\"}}");
        var tool = BuildTextTool("refine");

        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();

        // ADR-015: NONE of input text / instruction / output text may appear in logs.
        AssertTelemetryRespectsAdr015(secretInput, secretInstruction, secretOutput);
    }

    [Fact]
    public async Task Telemetry_LogsMethodDiscriminator_ForCorrelation()
    {
        SetupChatResponse("ok");

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: "{\"text\":\"some content\"}");
        var tool = BuildTextTool("keypoints");

        await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        CapturedLogMessages.Should().Contain(m =>
            m.FormattedMessage.Contains("keypoints", StringComparison.OrdinalIgnoreCase),
            because: "ADR-015 allows logging the deterministic method discriminator for correlation");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Method-dispatch coverage: all three methods, single handler
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MethodDispatch_AllThreeMethods_RouteToCorrectPipeline()
    {
        SetupChatResponse("response-content");

        var handler = CreateHandler();

        foreach (var method in new[] { "refine", "keypoints", "summary" })
        {
            var argsJson = method == "refine"
                ? "{\"text\":\"input\",\"instruction\":\"do something\"}"
                : "{\"text\":\"input\"}";

            var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);
            var tool = BuildTextTool(method);

            var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

            result.Success.Should().BeTrue(because: $"method '{method}' should succeed with valid args");
            var data = result.GetData<TextRefinementHandler.TextRefinementResult>();
            data!.Method.Should().Be(method);
        }
    }
}
