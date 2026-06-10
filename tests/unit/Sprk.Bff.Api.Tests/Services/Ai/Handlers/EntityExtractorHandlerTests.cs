using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="EntityExtractorHandler"/> (R6 Pillar 2, FR-13, Wave 2).
/// </summary>
/// <remarks>
/// Covers: 4-point contract; positive (mixed entity types); error (invalid LLM JSON,
/// below-threshold filter); config-driven (entityTypes subset); cache hit/miss
/// (second identical call doesn't re-invoke LLM); telemetry (no input text or entity values in
/// logs per ADR-015); chat path; per-tenant cache isolation (ADR-014).
/// </remarks>
public sealed class EntityExtractorHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    private readonly Mock<IDistributedCache> _cacheMock = new();
    private readonly Dictionary<string, byte[]> _cacheStore = new(StringComparer.Ordinal);

    public EntityExtractorHandlerTests()
    {
        // Wire the cache mock to a simple in-memory store so we can verify hit/miss behavior.
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _cacheStore.TryGetValue(key, out var bytes) ? bytes : null!);

        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, byte[] bytes, DistributedCacheEntryOptions _, CancellationToken _) =>
            {
                _cacheStore[key] = bytes;
                return Task.CompletedTask;
            });
    }

    private EntityExtractorHandler CreateHandler() => new(
        OpenAiClientMock.Object,
        _cacheMock.Object,
        Options.Create(new ModelSelectorOptions
        {
            ToolHandlerModel = "gpt-4o-mini",
            DefaultModel = "gpt-4o-mini"
        }),
        CreateLogger<EntityExtractorHandler>());

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests (per HandlerContractTestTemplate, retargeted)
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
            typeof(EntityExtractorHandler),
            because: "the handler type must be auto-discovered by the assembly scan (R6 Pillar 2: no manual DI lines per handler)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(EntityExtractorHandler),
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
        handler.SupportedToolTypes.Should().Contain(ToolType.EntityExtractor);
    }

    [Fact]
    public void SupportedInvocationContexts_IncludesBoth()
    {
        var handler = CreateHandler();

        handler.SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Both,
            because: "FR-13 requires EntityExtractorHandler to be invocable from both playbook orchestration and chat-driven function calling");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Validate (playbook context)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_Succeeds_WithValidContext()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Acme Corp signed the deal.");
        var tool = BuildAnalysisTool(handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenExtractedTextIsEmpty()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "");
        var tool = BuildAnalysisTool(handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("extracted text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenTenantIdIsMissing()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text", tenantId: "");
        var tool = BuildAnalysisTool(handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenUnsupportedEntityTypeInConfig()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler),
            toolType: ToolType.EntityExtractor,
            configuration: "{\"entityTypes\":[\"chemical\"]}");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("chemical", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenConfidenceThresholdOutOfRange()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler),
            toolType: ToolType.EntityExtractor,
            configuration: "{\"confidenceThreshold\":1.5}");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("confidenceThreshold", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat (chat invocation context)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Succeeds_WithTextArgument()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{\"text\":\"Acme Corp.\"}");
        var tool = BuildAnalysisTool(handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenTextMissing()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildAnalysisTool(handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenJsonMalformed()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{not json");
        var tool = BuildAnalysisTool(handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync — positive (mixed entity types with mocked LLM)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ExtractsMixedEntityTypes()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Acme Corp", "organization", "Acme Corp", 0.95, 0, 9),
            ("Alice Smith", "person", "Alice Smith", 0.92, 14, 25),
            ("New York", "location", "New York", 0.9, 30, 38),
            ("2026-03-15", "date", "2026-03-15", 0.97, 44, 54),
            ("1000 USD", "money", "1000 USD", 0.93, 60, 68),
            ("alice@acme.com", "email", "alice@acme.com", 0.98, 74, 88)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText:
            "Acme Corp by Alice Smith of New York on 2026-03-15 for 1000 USD: alice@acme.com.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Stats.TotalCount.Should().Be(6);
        data.Stats.ByType.Should().ContainKeys("organization", "person", "location", "date", "money", "email");
        data.Entities.Should().Contain(e => e.Type == "email" && e.NormalizedValue == "alice@acme.com");
    }

    [Fact]
    public async Task ExecuteAsync_FiltersBelowConfidenceThreshold()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Acme Corp", "organization", "Acme Corp", 0.95, 0, 9),
            ("Bob", "person", "Bob", 0.4, 12, 15) // below default 0.6
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Acme Corp and Bob signed.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Entities.Should().HaveCount(1);
        data.Entities[0].Type.Should().Be("organization");
        data.Entities[0].NormalizedValue.Should().Be("Acme Corp");
    }

    [Fact]
    public async Task ExecuteAsync_ConfigEntityTypesFilter_KeepsOnlySubset()
    {
        // LLM returns all types, but config restricts to organization only — handler must
        // drop the others.
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Acme Corp", "organization", "Acme Corp", 0.9, 0, 9),
            ("Alice", "person", "Alice", 0.9, 14, 19),
            ("New York", "location", "New York", 0.9, 24, 32)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Acme Corp by Alice of New York.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler),
            toolType: ToolType.EntityExtractor,
            configuration: "{\"entityTypes\":[\"organization\"]}");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Entities.Should().OnlyContain(e => e.Type == "organization");
        data.Entities.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesEmail_ToLowercase()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("ALICE@ACME.COM", "email", "ALICE@ACME.COM", 0.95, 0, 14)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "ALICE@ACME.COM");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Entities.Should().ContainSingle();
        data.Entities[0].NormalizedValue.Should().Be("alice@acme.com");
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMalformedEmail()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("not-an-email", "email", "not-an-email", 0.95, 0, 12),
            ("alice@acme.com", "email", "alice@acme.com", 0.95, 14, 28)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "not-an-email, alice@acme.com");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Entities.Should().HaveCount(1);
        data.Entities[0].NormalizedValue.Should().Be("alice@acme.com");
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMalformedUrl()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("javascript:alert(1)", "url", "javascript:alert(1)", 0.95, 0, 19),
            ("https://example.com/path", "url", "https://example.com/path", 0.95, 21, 45)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(
            extractedText: "javascript:alert(1) https://example.com/path");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Entities.Should().HaveCount(1);
        data.Entities[0].NormalizedValue.Should().Be("https://example.com/path");
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesDateToIso8601()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("March 15, 2026", "date", "March 15, 2026", 0.92, 0, 14)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "March 15, 2026");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Entities.Should().ContainSingle();
        data.Entities[0].NormalizedValue.Should().Be("2026-03-15");
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesMoneyCurrency_ToUppercase()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("$1000", "money", "1000 usd", 0.95, 0, 5)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "$1000");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Entities.Should().ContainSingle();
        data.Entities[0].NormalizedValue.Should().Be("1000 USD");
    }

    [Fact]
    public async Task ExecuteAsync_DeduplicatesIdenticalEntities()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Acme Corp", "organization", "Acme Corp", 0.95, 0, 9),
            ("Acme Corp", "organization", "Acme Corp", 0.95, 0, 9), // dup at same span
            ("Acme Corp", "organization", "Acme Corp", 0.95, 20, 29) // distinct span
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Acme Corp signed; Acme Corp again.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        // Two distinct spans → two entries; the exact duplicate at the same span is dropped.
        data.Entities.Should().HaveCount(2);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync — error cases
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ReturnsModelError_OnMalformedLlmJson()
    {
        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{not valid json");

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Acme Corp.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ModelError);
        result.Execution.ModelCalls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCancelled_WhenTokenCancelled()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Acme Corp.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.ExecuteAsync(context, tool, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsInternalError_OnLlmException()
    {
        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upstream timeout"));

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Acme Corp.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-014: per-tenant cache hit/miss + isolation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_SecondIdenticalCall_HitsCache_NotLlm()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Acme Corp", "organization", "Acme Corp", 0.95, 0, 9)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Acme Corp.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var first = await handler.ExecuteAsync(context, tool, CancellationToken.None);
        var second = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        first.Success.Should().BeTrue();
        first.Execution.CacheHit.Should().BeFalse();
        first.Execution.ModelCalls.Should().Be(1);

        second.Success.Should().BeTrue();
        second.Execution.CacheHit.Should().BeTrue();
        second.Execution.ModelCalls.Should().Be(0);

        // LLM mock invoked exactly once across the two calls
        OpenAiClientMock.Verify(c => c.GetStructuredCompletionRawAsync(
            It.IsAny<string>(),
            It.IsAny<BinaryData>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<int?>(),
            It.IsAny<float?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CacheKey_IsTenantScoped_NoCrossTenantHits()
    {
        // Tenant A populates the cache; Tenant B requests same text → MUST miss.
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Acme Corp", "organization", "Acme Corp", 0.95, 0, 9)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var ctxA = BuildToolExecutionContext(extractedText: "Acme Corp.", tenantId: "tenant-A");
        var ctxB = BuildToolExecutionContext(extractedText: "Acme Corp.", tenantId: "tenant-B");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        await handler.ExecuteAsync(ctxA, tool, CancellationToken.None);
        var resultB = await handler.ExecuteAsync(ctxB, tool, CancellationToken.None);

        resultB.Execution.CacheHit.Should().BeFalse(
            because: "ADR-014: cache key MUST be per-tenant — tenant B cannot read tenant A's entries");

        // Two distinct LLM calls (one per tenant)
        OpenAiClientMock.Verify(c => c.GetStructuredCompletionRawAsync(
            It.IsAny<string>(),
            It.IsAny<BinaryData>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<int?>(),
            It.IsAny<float?>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // And cache holds keys for both tenants (different keys)
        _cacheStore.Keys.Should().Contain(k => k.Contains(":tenant-A:"));
        _cacheStore.Keys.Should().Contain(k => k.Contains(":tenant-B:"));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Chat invocation path
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ExtractsEntitiesFromTextArgument()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Acme Corp", "organization", "Acme Corp", 0.95, 0, 9)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: "{\"text\":\"Acme Corp signed today.\"}");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Stats.TotalCount.Should().Be(1);
        data.Entities[0].Type.Should().Be("organization");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry compliance
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_DoesNotLogInputTextOrEntityValues()
    {
        const string secretInputText =
            "ConfidentialClient-XYZ123 signed with PrivilegedSecretValue456.";
        const string secretEntityText = "ConfidentialClient-XYZ123";
        const string secretEntityNormalized = "ConfidentialClient-XYZ123";

        var llmResponse = BuildLlmResponse(new[]
        {
            (secretEntityText, "organization", secretEntityNormalized, 0.95, 0, 24)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: secretInputText);
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        await handler.ExecuteAsync(context, tool, CancellationToken.None);

        // ADR-015: NEITHER input text NOR extracted entity values may appear in logs.
        AssertTelemetryRespectsAdr015(
            secretInputText,
            secretEntityText,
            "PrivilegedSecretValue456");
    }

    [Fact]
    public async Task Telemetry_ChatPath_DoesNotLogInputTextOrEntityValues()
    {
        const string secretInputText =
            "ConfidentialClient-XYZ123 with PrivilegedSecretValue456.";

        var llmResponse = BuildLlmResponse(new[]
        {
            ("ConfidentialClient-XYZ123", "organization", "ConfidentialClient-XYZ123", 0.95, 0, 24)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { text = secretInputText }));
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(
            secretInputText,
            "ConfidentialClient-XYZ123",
            "PrivilegedSecretValue456");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync — model + execution metadata
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ReportsOneModelCall_AndModelName()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Acme Corp", "organization", "Acme Corp", 0.95, 0, 9)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Acme Corp.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler), toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Execution.ModelCalls.Should().Be(1);
        result.Execution.ModelName.Should().NotBeNullOrWhiteSpace();
        result.Execution.CacheHit.Should().BeFalse();
        result.Execution.InputTokens.Should().NotBeNull();
        result.Execution.OutputTokens.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_UsesConfigModelDeployment_WhenSpecified()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Acme Corp", "organization", "Acme Corp", 0.95, 0, 9)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Acme Corp.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler),
            toolType: ToolType.EntityExtractor,
            configuration: "{\"modelDeployment\":\"gpt-4o\"}");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Execution.ModelName.Should().Be("gpt-4o");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private void SetupLlmResponse(string responseJson)
    {
        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseJson);
    }

    private static string BuildLlmResponse(
        IEnumerable<(string Text, string Type, string Normalized, double Confidence, int Start, int End)> entities)
    {
        var sb = new StringBuilder();
        sb.Append("{\"entities\":[");
        var first = true;
        foreach (var (text, type, normalized, confidence, start, end) in entities)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('{');
            sb.Append($"\"text\":{JsonSerializer.Serialize(text)},");
            sb.Append($"\"type\":\"{type}\",");
            sb.Append($"\"normalizedValue\":{JsonSerializer.Serialize(normalized)},");
            sb.Append($"\"confidence\":{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"span\":{{\"start\":{start},\"end\":{end}}}");
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static EntityExtractorHandler.EntityExtractionResult GetData(ToolResult result)
    {
        result.Data.Should().NotBeNull("ToolResult.Data must be populated on success");
        var parsed = result.GetData<EntityExtractorHandler.EntityExtractionResult>();
        parsed.Should().NotBeNull();
        return parsed!;
    }

    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }
}
