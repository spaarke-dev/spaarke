using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Safety.Citations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="VerifyCitationsHandler"/> (R6 Wave 7c migration).
/// </summary>
/// <remarks>
/// Inherits <see cref="TypedToolHandlerTestFixture"/> for shared mocks + ADR-015 telemetry
/// assertions. Covers the 4-point contract + per-handler tests:
/// <list type="bullet">
/// <item>Auto-discovery via assembly scan (ADR-010)</item>
/// <item>HandlerId == nameof(VerifyCitationsHandler) (R6 Pillar 2 routing)</item>
/// <item>Metadata + SupportedToolTypes + SupportedInvocationContexts.Both</item>
/// <item>Happy path: mixed verified / unverified / errors citations</item>
/// <item>Empty-text input → ValidationFailed result</item>
/// <item>Zero-citation input → graceful empty result</item>
/// <item>TenantId enforcement (ADR-014)</item>
/// <item>Service failure → ToolResult.Error with InternalError code</item>
/// <item>Cancellation → ToolResult.Error with Cancelled code</item>
/// <item>Playbook config parsing (text from sprk_configuration.text)</item>
/// <item>Chat args parsing (text from ChatInvocationContext.ToolArgumentsJson)</item>
/// <item>ADR-015 telemetry: citation text NEVER appears in logs (sentinel scan)</item>
/// <item>NFR-13 binding: middleware verification path untouched (see SprkChatAgentFactory comment)</item>
/// </list>
/// </remarks>
public sealed class VerifyCitationsHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    // Sentinel fixtures — deliberately recognisable strings so the ADR-015 telemetry
    // scan can assert they never appear in any captured log message.
    private const string SentinelCitationRaw = "Acme Corp v. Widget Industries, 999 U.S. 123 (2099)";
    private const string SentinelTextWithCitation =
        "The court in Acme Corp v. Widget Industries, 999 U.S. 123 (2099) held that " +
        "patent claim construction is a question of law.";
    private const string SentinelTextNoCitation = "This is a paragraph with no citations whatsoever.";

    private readonly Mock<ICitationVerificationService> _verificationServiceMock = new();

    private VerifyCitationsHandler CreateHandler() => new(
        _verificationServiceMock.Object,
        CreateLogger<VerifyCitationsHandler>());

    private static Citation BuildSentinelCitation() => new(
        RawText: SentinelCitationRaw,
        CitationType: CitationType.CaseLaw,
        NormalizedKey: "999 U.S. 123");

    private static CitationVerificationResult BuildVerified(Citation citation, string providerName = "TestProvider") =>
        new(
            Citation: citation,
            IsVerified: true,
            ConfidenceScore: 0.95f,
            SourceUrl: "https://example.test/cases/999-us-123",
            VerifiedText: null,
            VerificationProvider: providerName,
            LatencyMs: 12.5);

    private static AnalysisTool BuildVerifyTool(string? configuration = null) =>
        BuildAnalysisTool(
            handlerClass: nameof(VerifyCitationsHandler),
            configuration: configuration,
            toolType: ToolType.Custom);

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
            typeof(VerifyCitationsHandler),
            because: "the handler must be auto-discovered via assembly scan (R6 Pillar 2: no manual DI lines)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(VerifyCitationsHandler),
            because: "R6 Pillar 2 binding: HandlerId == nameof(handler class) routes sprk_handlerclass to this handler at runtime");
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
            because: "Wave 7c migration requires VerifyCitationsHandler to be invocable from both playbook orchestration and chat-driven function calling");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Validate (playbook)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_Succeeds_WhenTenantIdPresent()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildVerifyTool();

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenTenantIdMissing()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(tenantId: "");
        var tool = BuildVerifyTool();

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Succeeds_WithValidArgs()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: "{\"text\":\"Some passage with 42 U.S.C. § 1983.\"}");
        var tool = BuildVerifyTool();

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenTenantIdMissing()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: "{\"text\":\"Some passage.\"}",
            tenantId: "");
        var tool = BuildVerifyTool();

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenToolArgumentsMissing()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "");
        var tool = BuildVerifyTool();

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("arguments", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenTextFieldMissing()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildVerifyTool();

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenJsonMalformed()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{not json");
        var tool = BuildVerifyTool();

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteChatAsync — happy path + variants
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsOk_WithMixedVerifiedUnverifiedErrors()
    {
        // Arrange
        var verifiedCitation = BuildSentinelCitation();
        var unverifiedCitation = new Citation("11 U.S.C. § 362", CitationType.Statute, "11 U.S.C. § 362");
        var erroredCitation = new Citation("Form 10-K", CitationType.SecFiling, "10-K");

        var report = new CitationVerificationReport(
            Verified: new[] { BuildVerified(verifiedCitation) },
            Unverified: new[] { CitationVerificationResult.NoProvider(unverifiedCitation) },
            Errors: new[]
            {
                CitationVerificationResult.FromError(
                    erroredCitation, "TestProvider", "boom", 5.0)
            });

        _verificationServiceMock
            .Setup(s => s.VerifyAllAsync(SentinelTextWithCitation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { text = SentinelTextWithCitation }));
        var tool = BuildVerifyTool();

        // Act
        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.HandlerId.Should().Be(nameof(VerifyCitationsHandler));

        var payload = result.GetData<VerifyCitationsHandler.VerifyCitationsPayload>();
        payload.Should().NotBeNull();
        payload!.Total.Should().Be(3);
        payload.VerifiedCount.Should().Be(1);
        payload.UnverifiedCount.Should().Be(1);
        payload.ErrorCount.Should().Be(1);
        payload.Citations.Should().HaveCount(3);

        // ADR-015 sentinel: the raw citation text MUST NOT appear in app logs.
        AssertTelemetryRespectsAdr015(SentinelTextWithCitation, SentinelCitationRaw);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsOk_WhenNoCitationsFound()
    {
        // Arrange — empty report (zero citations)
        var report = new CitationVerificationReport(
            Verified: Array.Empty<CitationVerificationResult>(),
            Unverified: Array.Empty<CitationVerificationResult>(),
            Errors: Array.Empty<CitationVerificationResult>());

        _verificationServiceMock
            .Setup(s => s.VerifyAllAsync(SentinelTextNoCitation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { text = SentinelTextNoCitation }));
        var tool = BuildVerifyTool();

        // Act
        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var payload = result.GetData<VerifyCitationsHandler.VerifyCitationsPayload>();
        payload.Should().NotBeNull();
        payload!.Citations.Should().BeEmpty();
        payload.Message.Should().NotBeNullOrWhiteSpace();

        AssertTelemetryRespectsAdr015(SentinelTextNoCitation);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsValidationFailed_WhenTextMissingFromArgs()
    {
        // Arrange — args present but no "text" field; handler treats this as missing input.
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildVerifyTool();

        // Act
        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsInternalError_WhenServiceThrows()
    {
        // Arrange — service throws unrecoverable exception.
        _verificationServiceMock
            .Setup(s => s.VerifyAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider exploded"));

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { text = "Some text with citations." }));
        var tool = BuildVerifyTool();

        // Act
        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsCancelled_WhenCancellationRequested()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { text = "Some text." }));
        var tool = BuildVerifyTool();

        // Act
        var result = await handler.ExecuteChatAsync(context, tool, cts.Token);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync (playbook) — config path + fallback path
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ReadsText_FromConfiguration_WhenPresent()
    {
        // Arrange — text supplied via tool.Configuration.text; fixture document text is ignored.
        var citation = BuildSentinelCitation();
        var report = new CitationVerificationReport(
            Verified: new[] { BuildVerified(citation) },
            Unverified: Array.Empty<CitationVerificationResult>(),
            Errors: Array.Empty<CitationVerificationResult>());

        _verificationServiceMock
            .Setup(s => s.VerifyAllAsync(SentinelTextWithCitation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "irrelevant — config takes precedence");
        var tool = BuildVerifyTool(
            configuration: JsonSerializer.Serialize(new { text = SentinelTextWithCitation }));

        // Act
        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _verificationServiceMock.Verify(
            s => s.VerifyAllAsync(SentinelTextWithCitation, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToDocumentText_WhenConfigurationMissing()
    {
        // Arrange — no configuration; handler reads from DocumentContext.ExtractedText.
        var report = new CitationVerificationReport(
            Verified: Array.Empty<CitationVerificationResult>(),
            Unverified: Array.Empty<CitationVerificationResult>(),
            Errors: Array.Empty<CitationVerificationResult>());

        _verificationServiceMock
            .Setup(s => s.VerifyAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: SentinelTextNoCitation);
        var tool = BuildVerifyTool(configuration: null);

        // Act
        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _verificationServiceMock.Verify(
            s => s.VerifyAllAsync(SentinelTextNoCitation, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsValidationFailed_WhenBothConfigAndDocumentTextMissing()
    {
        // Arrange — neither config nor document text.
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "");
        var tool = BuildVerifyTool(configuration: null);

        // Act
        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry sentinel scan (citation text NEVER in logs)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_RespectsAdr015_NoCitationTextInLogs()
    {
        // Arrange — produce a verified citation whose raw text is the sentinel string.
        var verifiedCitation = BuildSentinelCitation();
        var report = new CitationVerificationReport(
            Verified: new[] { BuildVerified(verifiedCitation) },
            Unverified: Array.Empty<CitationVerificationResult>(),
            Errors: Array.Empty<CitationVerificationResult>());

        _verificationServiceMock
            .Setup(s => s.VerifyAllAsync(SentinelTextWithCitation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { text = SentinelTextWithCitation }));
        var tool = BuildVerifyTool();

        // Act
        await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        // Assert — neither the raw citation text nor the full input passage may appear in logs.
        AssertTelemetryRespectsAdr015(SentinelTextWithCitation, SentinelCitationRaw);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Bucket helper (ADR-015 coarse-grained citation count)
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0, "<5")]
    [InlineData(4, "<5")]
    [InlineData(5, "5-20")]
    [InlineData(20, "5-20")]
    [InlineData(21, ">20")]
    [InlineData(500, ">20")]
    public void BucketCitationCount_Buckets(int count, string expected)
    {
        VerifyCitationsHandler.BucketCitationCount(count).Should().Be(expected);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }
}
