using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="AnalysisQueryHandler"/> (R6 Wave 7 — legacy
/// <c>AnalysisQueryTools</c> migration to typed-handler contract).
/// </summary>
/// <remarks>
/// Inherits <see cref="TypedToolHandlerTestFixture"/> for shared mocks + ADR-015 telemetry
/// assertions. Includes the 4-point contract tests + per-handler tests covering:
/// method-discriminator dispatch (GetAnalysisResult vs GetAnalysisSummary), tenantId
/// enforcement (ADR-014), invalid id handling, KeyNotFound mapping, cancellation,
/// playbook + chat context paths, telemetry compliance (ADR-015).
/// </remarks>
public sealed class AnalysisQueryHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    private readonly Mock<IAnalysisOrchestrationService> _analysisServiceMock = new();

    private AnalysisQueryHandler CreateHandler() => new(
        _analysisServiceMock.Object,
        CreateLogger<AnalysisQueryHandler>());

    private static AnalysisDetailResult BuildAnalysisDetailResult(
        Guid? id = null,
        string documentName = "fixture-doc.pdf",
        string status = "Completed",
        string? workingDoc = null,
        string? finalOutput = null,
        TokenUsage? tokens = null,
        DateTime? startedOn = null)
    {
        return new AnalysisDetailResult
        {
            Id = id ?? Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            DocumentName = documentName,
            Action = new AnalysisActionInfo(Guid.NewGuid(), "fixture-action"),
            Status = status,
            WorkingDocument = workingDoc,
            FinalOutput = finalOutput,
            TokenUsage = tokens,
            StartedOn = startedOn ?? new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests (copy of HandlerContractTestTemplate, retargeted)
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
            typeof(AnalysisQueryHandler),
            because: "the handler type must be auto-discovered by the assembly scan (R6 Pillar 2: no manual DI lines per handler)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(AnalysisQueryHandler),
            because: "R6 Pillar 2 binding: HandlerId == nameof(handler class) so the Dataverse sprk_handlerclass field routes to this handler at runtime");
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
    public void SupportedInvocationContexts_IncludesBoth()
    {
        var handler = CreateHandler();

        handler.SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Both,
            because: "R6 Wave 7 FR-12: AnalysisQueryHandler must be invocable from both playbook orchestration and chat-driven function calling");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Validate (playbook context) — tenantId enforcement (ADR-014)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_Succeeds_WithValidContext()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler), toolType: ToolType.Custom);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenTenantIdIsMissing()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(tenantId: "");
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler), toolType: ToolType.Custom);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat — argument-shape validation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Succeeds_WithDocumentIdAndDefaultMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { documentId = Guid.NewGuid().ToString() }));
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("GetAnalysisResult")]
    [InlineData("GetAnalysisSummary")]
    public void ValidateChat_Succeeds_WithEachSupportedMethod(string method)
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new
            {
                documentId = Guid.NewGuid().ToString(),
                method
            }));
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenMethodIsUnsupported()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new
            {
                documentId = Guid.NewGuid().ToString(),
                method = "GetSecretsForFreePlease"
            }));
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("method", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenDocumentIdIsMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("documentId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenTenantIdIsMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            tenantId: "",
            toolArgumentsJson: JsonSerializer.Serialize(new { documentId = Guid.NewGuid().ToString() }));
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenJsonIsMalformed()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{not json");
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteChatAsync — dispatcher behavior
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_GetAnalysisResult_ReturnsFullFormattedOutput()
    {
        var analysisId = Guid.NewGuid();
        var detail = BuildAnalysisDetailResult(
            id: analysisId,
            documentName: "contract.pdf",
            status: "Completed",
            workingDoc: "## Executive Summary\nKey terms agreed.\n\n## Risks\nTermination clause unbalanced.",
            tokens: new TokenUsage(1234, 567),
            startedOn: new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));

        _analysisServiceMock
            .Setup(s => s.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new
            {
                documentId = analysisId.ToString(),
                method = "GetAnalysisResult"
            }));
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<AnalysisQueryHandler.AnalysisQueryPayload>();
        payload.Should().NotBeNull();
        payload!.Method.Should().Be("GetAnalysisResult");
        payload.Found.Should().BeTrue();
        payload.AnalysisId.Should().Be(analysisId);
        payload.Content.Should().Contain("contract.pdf");
        payload.Content.Should().Contain("Completed");
        payload.Content.Should().Contain("1234 input, 567 output");
        payload.Content.Should().Contain("Termination clause unbalanced");
        result.Execution.ModelCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteChatAsync_GetAnalysisSummary_ExtractsExecutiveSummarySection()
    {
        var analysisId = Guid.NewGuid();
        var detail = BuildAnalysisDetailResult(
            id: analysisId,
            documentName: "contract.pdf",
            workingDoc: "## Executive Summary\nKey terms agreed across both parties.\n\n## Risks\nTermination clause unbalanced.");

        _analysisServiceMock
            .Setup(s => s.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new
            {
                documentId = analysisId.ToString(),
                method = "GetAnalysisSummary"
            }));
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<AnalysisQueryHandler.AnalysisQueryPayload>();
        payload!.Method.Should().Be("GetAnalysisSummary");
        payload.Found.Should().BeTrue();
        payload.Content.Should().Contain("Executive Summary: contract.pdf");
        payload.Content.Should().Contain("Key terms agreed across both parties");
        payload.Content.Should().NotContain("Termination clause unbalanced");
    }

    [Fact]
    public async Task ExecuteChatAsync_GetAnalysisSummary_FallsBackToPreviewWhenNoSummarySection()
    {
        var analysisId = Guid.NewGuid();
        var longBody = new string('x', 800);
        var detail = BuildAnalysisDetailResult(
            id: analysisId,
            workingDoc: longBody);

        _analysisServiceMock
            .Setup(s => s.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new
            {
                documentId = analysisId.ToString(),
                method = "GetAnalysisSummary"
            }));
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<AnalysisQueryHandler.AnalysisQueryPayload>();
        payload!.Method.Should().Be("GetAnalysisSummary");
        payload.Content.Should().Contain("Summary truncated");
    }

    [Fact]
    public async Task ExecuteChatAsync_DefaultsMethodToGetAnalysisResult_WhenOmitted()
    {
        var analysisId = Guid.NewGuid();
        var detail = BuildAnalysisDetailResult(id: analysisId, workingDoc: "Some analysis output.");

        _analysisServiceMock
            .Setup(s => s.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { documentId = analysisId.ToString() }));
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<AnalysisQueryHandler.AnalysisQueryPayload>();
        payload!.Method.Should().Be("GetAnalysisResult");
        payload.Found.Should().BeTrue();
        payload.Content.Should().Contain("Some analysis output");
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsFoundFalse_WhenAnalysisNotFound()
    {
        var analysisId = Guid.NewGuid();
        _analysisServiceMock
            .Setup(s => s.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("not found"));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { documentId = analysisId.ToString() }));
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // Wraps not-found as a successful tool call with Found=false so the LLM can include
        // it in its reasoning (the chat agent still treats this as a non-error tool response).
        result.Success.Should().BeTrue();
        var payload = result.GetData<AnalysisQueryHandler.AnalysisQueryPayload>();
        payload!.Found.Should().BeFalse();
        payload.Message.Should().Contain("not found");
        payload.AnalysisId.Should().Be(analysisId);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsFoundFalse_WhenDocumentIdIsNotAGuid()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { documentId = "not-a-guid" }));
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<AnalysisQueryHandler.AnalysisQueryPayload>();
        payload!.Found.Should().BeFalse();
        payload.Message.Should().Contain("Invalid analysis ID format");

        // GetAnalysisAsync should not be called when the id is unparseable.
        _analysisServiceMock.Verify(
            s => s.GetAnalysisAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsCancelled_WhenTokenCancelled()
    {
        var analysisId = Guid.NewGuid();
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { documentId = analysisId.ToString() }));
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.ExecuteChatAsync(ctx, tool, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync (playbook context) — reads documentId + method from Configuration
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_PlaybookContext_DispatchesToGetAnalysisSummary()
    {
        var analysisId = Guid.NewGuid();
        var detail = BuildAnalysisDetailResult(
            id: analysisId,
            workingDoc: "## Executive Summary\nQuarterly highlights.\n\n## Details\nLong body.");

        _analysisServiceMock
            .Setup(s => s.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(
            handlerClass: nameof(AnalysisQueryHandler),
            configuration: JsonSerializer.Serialize(new
            {
                documentId = analysisId.ToString(),
                method = "GetAnalysisSummary"
            }));

        var result = await handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<AnalysisQueryHandler.AnalysisQueryPayload>();
        payload!.Method.Should().Be("GetAnalysisSummary");
        payload.Content.Should().Contain("Quarterly highlights");
    }

    [Fact]
    public async Task ExecuteAsync_PlaybookContext_DefaultsToGetAnalysisResult_WhenConfigEmpty()
    {
        var analysisId = Guid.NewGuid();
        var detail = BuildAnalysisDetailResult(id: analysisId, workingDoc: "Body.");

        _analysisServiceMock
            .Setup(s => s.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        // documentId supplied; no method
        var tool = BuildAnalysisTool(
            handlerClass: nameof(AnalysisQueryHandler),
            configuration: JsonSerializer.Serialize(new { documentId = analysisId.ToString() }));

        var result = await handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<AnalysisQueryHandler.AnalysisQueryPayload>();
        payload!.Method.Should().Be("GetAnalysisResult");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry — analysis content must never leak into logs
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_RespectsAdr015_ForExecuteChatAsync()
    {
        const string secretBody = "CONFIDENTIAL-CLIENT-DETAIL-ConfidentialClientName-12345 with privileged content.";
        var analysisId = Guid.NewGuid();
        var detail = BuildAnalysisDetailResult(
            id: analysisId,
            documentName: "fixture-doc.pdf",
            workingDoc: secretBody);

        _analysisServiceMock
            .Setup(s => s.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { documentId = analysisId.ToString() }));
        var tool = BuildAnalysisTool(handlerClass: nameof(AnalysisQueryHandler));

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(
            "ConfidentialClientName-12345",
            "privileged content",
            secretBody);
    }

    [Fact]
    public async Task Telemetry_RespectsAdr015_ForExecuteAsync()
    {
        const string secretBody = "CONFIDENTIAL-CLIENT-DETAIL-ConfidentialClientName-12345 with privileged content.";
        var analysisId = Guid.NewGuid();
        var detail = BuildAnalysisDetailResult(
            id: analysisId,
            workingDoc: secretBody);

        _analysisServiceMock
            .Setup(s => s.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(
            handlerClass: nameof(AnalysisQueryHandler),
            configuration: JsonSerializer.Serialize(new { documentId = analysisId.ToString() }));

        await handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(
            "ConfidentialClientName-12345",
            "privileged content",
            secretBody);
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
