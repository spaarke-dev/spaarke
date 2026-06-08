using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="InvokePlaybookHandler"/> (R6 Pillar 3 / Q11 / task 021).
/// </summary>
/// <remarks>
/// <para>
/// Covers the 4-point contract from
/// <c>Services/Ai/Handlers/HandlerRegistrationConventions.md</c> plus per-handler scenarios:
/// </para>
/// <list type="bullet">
/// <item>Auto-discovery via assembly scan (ADR-010)</item>
/// <item><c>HandlerId == nameof(InvokePlaybookHandler)</c> (R6 Pillar 2 routing)</item>
/// <item>Metadata + SupportedToolTypes + SupportedInvocationContexts.Chat</item>
/// <item>Happy path: valid playbookId + parameters → facade invoked once → ToolResult.Success</item>
/// <item>Wave 7b citations envelope projected into <c>ToolResult.Metadata</c></item>
/// <item>ValidateChat catches: missing/empty/non-GUID playbookId, malformed JSON, non-object parameters</item>
/// <item>Tenant visibility: non-existent playbookId → ValidationFailed; facade NOT invoked</item>
/// <item>Tenant isolation: cache key prevents cross-tenant leakage (per-tenant lookup)</item>
/// <item>Tenant visibility result CACHED so a second dispatch reuses the lookup</item>
/// <item>Facade failure → ToolResult.Error projected from PlaybookInvocationResult</item>
/// <item>Cancellation → ToolResult.Error with Cancelled code</item>
/// <item><see cref="FeatureDisabledException"/> from facade → DependencyUnavailable</item>
/// <item>Missing HttpContext → DependencyUnavailable (no NRE)</item>
/// <item>Playbook-context invocation rejected (chat-only handler)</item>
/// <item>ADR-015 telemetry: parameter VALUES never appear in logs (sentinel scan)</item>
/// </list>
/// </remarks>
public sealed class InvokePlaybookHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    // Sentinel — distinctive token any captured log message must NOT contain. Used to
    // assert that ADR-015 governance is respected (no parameter VALUES in logs).
    private const string SentinelParameterValue = "PARAMETER-VALUE-WITH-PRIVILEGED-CONTENT-MatterXYZ-Trade-Secret-Acme-2099";
    private const string SentinelClientName = "AcmeCorpConfidentialClient-MatterDocketNumber-12345";

    private readonly Mock<IInvokePlaybookAi> _invokeFacadeMock = new();
    private readonly Mock<IPlaybookService> _playbookServiceMock = new();
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMemoryCache _memoryCache;

    public InvokePlaybookHandlerTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1024 });
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        _httpContextAccessor = accessor;
    }

    private InvokePlaybookHandler CreateHandler() => new(
        _invokeFacadeMock.Object,
        _playbookServiceMock.Object,
        _httpContextAccessor,
        _memoryCache,
        CreateLogger<InvokePlaybookHandler>());

    private static AnalysisTool BuildInvokeTool() =>
        BuildAnalysisTool(
            handlerClass: nameof(InvokePlaybookHandler),
            configuration: "{}",
            toolType: ToolType.Custom,
            name: "Invoke Playbook");

    private static PlaybookResponse BuildVisiblePlaybook(Guid id, bool isActive = true) => new()
    {
        Id = id,
        Name = "Test Playbook",
        Description = "test",
        IsPublic = true,
        IsActive = isActive,
        OwnerId = Guid.NewGuid()
    };

    private static PlaybookInvocationResult BuildSuccessResult(
        Guid playbookId,
        string? text = "Aggregated text from playbook.",
        IReadOnlyList<ToolResultCitation>? citations = null)
    {
        return new PlaybookInvocationResult
        {
            RunId = Guid.NewGuid(),
            Success = true,
            TextContent = text,
            Citations = citations ?? Array.Empty<ToolResultCitation>(),
            Confidence = 0.95,
            Duration = TimeSpan.FromMilliseconds(123)
        };
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract
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
            typeof(InvokePlaybookHandler),
            because: "the handler must be auto-discovered via assembly scan (R6 Pillar 2: no manual DI lines)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(InvokePlaybookHandler),
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
        metadata.Parameters.Should().Contain(p => p.Name == "playbookId" && p.Required);
        metadata.Parameters.Should().Contain(p => p.Name == "parameters" && !p.Required);
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
            because: "Pillar 3 generic playbook dispatcher is chat-only — playbooks don't chain to other playbooks via chat-tool indirection");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat — argument shape
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Succeeds_WithValidGuidPlaybookId()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{Guid.NewGuid()}\"}}");
        var tool = BuildInvokeTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Succeeds_WithValidPlaybookIdAndObjectParameters()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{Guid.NewGuid()}\",\"parameters\":{{\"k\":\"v\"}}}}");
        var tool = BuildInvokeTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenTenantIdMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{Guid.NewGuid()}\"}}",
            tenantId: "");
        var tool = BuildInvokeTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenPlaybookIdMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildInvokeTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("playbookId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenPlaybookIdNotGuid()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"playbookId\":\"not-a-guid\"}");
        var tool = BuildInvokeTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("GUID", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenParametersIsArray()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{Guid.NewGuid()}\",\"parameters\":[\"x\"]}}");
        var tool = BuildInvokeTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("parameters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenJsonMalformed()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{not json");
        var tool = BuildInvokeTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteChatAsync — happy path + facade dispatch
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_DispatchesFacade_OnceWithCorrectArgs_WhenPlaybookVisible()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVisiblePlaybook(playbookId));

        IReadOnlyDictionary<string, string>? capturedParameters = null;
        PlaybookInvocationContext? capturedContext = null;
        _invokeFacadeMock
            .Setup(f => f.InvokePlaybookAsync(
                playbookId,
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyDictionary<string, string>?, PlaybookInvocationContext, CancellationToken>(
                (_, p, ctx, _) =>
                {
                    capturedParameters = p;
                    capturedContext = ctx;
                })
            .ReturnsAsync(BuildSuccessResult(playbookId));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\",\"parameters\":{{\"matter\":\"M-123\",\"depth\":3}}}}");
        var tool = BuildInvokeTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        _invokeFacadeMock.Verify(f => f.InvokePlaybookAsync(
            playbookId,
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<PlaybookInvocationContext>(),
            It.IsAny<CancellationToken>()), Times.Once);

        capturedParameters.Should().NotBeNull();
        capturedParameters!["matter"].Should().Be("M-123");
        capturedParameters["depth"].Should().Be("3", because: "numeric parameter values are coerced to JSON string form for the facade contract");

        capturedContext.Should().NotBeNull();
        capturedContext!.TenantId.Should().Be(DefaultTenantId);
        capturedContext.HttpContext.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_ProjectsCitations_IntoMetadata_PerWave7b()
    {
        var playbookId = Guid.NewGuid();
        var citations = new[]
        {
            new ToolResultCitation(ChunkId: "c1", SourceName: "Brief.pdf"),
            new ToolResultCitation(ChunkId: "c2", SourceName: "Policy.pdf", PageNumber: 5)
        };

        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVisiblePlaybook(playbookId));
        _invokeFacadeMock
            .Setup(f => f.InvokePlaybookAsync(
                playbookId, It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResult(playbookId, citations: citations));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}");
        var tool = BuildInvokeTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Citations);

        var projected = result.Metadata![ToolResultMetadataKeys.Citations] as IReadOnlyList<ToolResultCitation>
                        ?? (result.Metadata[ToolResultMetadataKeys.Citations] as IEnumerable<ToolResultCitation>)?.ToList();
        projected.Should().NotBeNull();
        projected!.Should().HaveCount(2);
        projected![0].ChunkId.Should().Be("c1");
    }

    [Fact]
    public async Task ExecuteChatAsync_OmitsCitationsMetadata_WhenFacadeReturnsNone()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVisiblePlaybook(playbookId));
        _invokeFacadeMock
            .Setup(f => f.InvokePlaybookAsync(
                playbookId, It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResult(playbookId));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}");
        var tool = BuildInvokeTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        if (result.Metadata is not null)
        {
            result.Metadata.Should().NotContainKey(ToolResultMetadataKeys.Citations);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Tenant visibility / isolation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsValidationFailed_WhenPlaybookNotFound_AndDoesNotDispatchFacade()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookResponse?)null);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}");
        var tool = BuildInvokeTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        _invokeFacadeMock.Verify(f => f.InvokePlaybookAsync(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<PlaybookInvocationContext>(),
            It.IsAny<CancellationToken>()),
            Times.Never,
            "tenant-visibility check must short-circuit before facade dispatch");
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsValidationFailed_WhenPlaybookIsInactive()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVisiblePlaybook(playbookId, isActive: false));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}");
        var tool = BuildInvokeTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        _invokeFacadeMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteChatAsync_TenantIsolation_DifferentTenantsDoNotShareVisibilityCache()
    {
        // Cross-tenant scenario: playbook P is visible to tenant A but not to tenant B.
        // Implementation: two parallel mock setups, keyed by the cancellation-token capture
        // of the per-tenant call. We simulate by returning null on the SECOND call to
        // GetPlaybookAsync and verifying the second tenant gets a denial.
        var playbookId = Guid.NewGuid();
        var callCount = 0;
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // Tenant A's call returns visible; tenant B's call returns null.
                return callCount == 1 ? BuildVisiblePlaybook(playbookId) : null;
            });

        _invokeFacadeMock
            .Setup(f => f.InvokePlaybookAsync(
                playbookId, It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResult(playbookId));

        var handler = CreateHandler();
        var tool = BuildInvokeTool();

        // Tenant A dispatch — visible
        var ctxA = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}",
            tenantId: "tenant-A");
        var resultA = await handler.ExecuteChatAsync(ctxA, tool, CancellationToken.None);
        resultA.Success.Should().BeTrue(because: "tenant A can see the playbook");

        // Tenant B dispatch — denied (separate cache key, fresh lookup returns null)
        var ctxB = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}",
            tenantId: "tenant-B");
        var resultB = await handler.ExecuteChatAsync(ctxB, tool, CancellationToken.None);
        resultB.Success.Should().BeFalse(because: "tenant B cannot see the playbook (ADR-014 per-tenant cache prefix)");
        resultB.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);

        callCount.Should().Be(2, because: "per-tenant cache keys force separate Dataverse lookups for tenant A and tenant B");
    }

    [Fact]
    public async Task ExecuteChatAsync_CachesVisibilityLookup_ForSameTenantAndPlaybook()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVisiblePlaybook(playbookId));
        _invokeFacadeMock
            .Setup(f => f.InvokePlaybookAsync(
                playbookId, It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResult(playbookId));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}");
        var tool = BuildInvokeTool();

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);
        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        _playbookServiceMock.Verify(
            p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()),
            Times.Once,
            "ADR-014 per-tenant cache: second lookup for the same tenant+playbook must be cached");
        _invokeFacadeMock.Verify(f => f.InvokePlaybookAsync(
            playbookId, It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<PlaybookInvocationContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Facade outcomes
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsError_WhenFacadeResultIsFailure()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVisiblePlaybook(playbookId));
        _invokeFacadeMock
            .Setup(f => f.InvokePlaybookAsync(
                playbookId, It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookInvocationResult
            {
                RunId = Guid.NewGuid(),
                Success = false,
                ErrorMessage = "Node SUM-CHAT failed: timeout.",
                ErrorCode = "PLAYBOOK_INVOCATION_FAILED"
            });

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}");
        var tool = BuildInvokeTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("PLAYBOOK_INVOCATION_FAILED");
        result.ErrorMessage.Should().Contain("timeout");
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsCancelled_WhenTokenCancelledBeforeDispatch()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVisiblePlaybook(playbookId));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}");
        var tool = BuildInvokeTool();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.ExecuteChatAsync(ctx, tool, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsDependencyUnavailable_WhenFacadeThrowsFeatureDisabled()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVisiblePlaybook(playbookId));
        _invokeFacadeMock
            .Setup(f => f.InvokePlaybookAsync(
                playbookId, It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureDisabledException("invoke_playbook", "Compound AI disabled."));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}");
        var tool = BuildInvokeTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.DependencyUnavailable);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsInternalError_WhenFacadeThrowsUnexpected()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVisiblePlaybook(playbookId));
        _invokeFacadeMock
            .Setup(f => f.InvokePlaybookAsync(
                playbookId, It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected orchestrator failure"));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}");
        var tool = BuildInvokeTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsInternalError_WhenVisibilityLookupThrows()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse temporarily unavailable."));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}");
        var tool = BuildInvokeTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        _invokeFacadeMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsDependencyUnavailable_WhenHttpContextMissing()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVisiblePlaybook(playbookId));

        var noHttpAccessor = new HttpContextAccessor { HttpContext = null };
        var handler = new InvokePlaybookHandler(
            _invokeFacadeMock.Object,
            _playbookServiceMock.Object,
            noHttpAccessor,
            _memoryCache,
            CreateLogger<InvokePlaybookHandler>());

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\"}}");
        var tool = BuildInvokeTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.DependencyUnavailable);
        _invokeFacadeMock.VerifyNoOtherCalls();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Playbook-context (rejected — chat-only handler)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_PlaybookContext_Rejects()
    {
        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildInvokeTool();

        var result = handler.Validate(ctx, tool);

        result.IsValid.Should().BeFalse(because: "InvokePlaybookHandler is chat-context-only");
    }

    [Fact]
    public async Task ExecuteAsync_PlaybookContext_ReturnsValidationFailed()
    {
        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildInvokeTool();

        var result = await handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry — parameter VALUES must NEVER appear in logs
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_RespectsAdr015_DoesNotLogParameterValues_OrFacadeTextContent()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVisiblePlaybook(playbookId));
        _invokeFacadeMock
            .Setup(f => f.InvokePlaybookAsync(
                playbookId, It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSuccessResult(
                playbookId,
                text: $"Playbook summary referencing {SentinelClientName} sensitive details."));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\",\"parameters\":{{\"secret\":\"{SentinelParameterValue}\"}}}}");
        var tool = BuildInvokeTool();

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(SentinelParameterValue, SentinelClientName);
    }

    [Fact]
    public async Task Telemetry_RespectsAdr015_VisibilityDenial_DoesNotLogParameterValues()
    {
        var playbookId = Guid.NewGuid();
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookResponse?)null);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"playbookId\":\"{playbookId}\",\"parameters\":{{\"secret\":\"{SentinelParameterValue}\"}}}}");
        var tool = BuildInvokeTool();

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(SentinelParameterValue);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // DI bootstrap helper (shared shape with other handler tests)
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
