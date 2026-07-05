using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="DataverseSearchDataHandler"/> (spaarke-ai-architecture-redesign-r1
/// task 008, FR-P0-07 read half).
/// </summary>
/// <remarks>
/// The Dataverse Search boundary is mocked at <see cref="IDataverseUserClient"/> (module
/// boundary — ADR-038 compliant). The mock simulates the test USER's security context —
/// Dataverse Search only returns hits the user is entitled to see, and access failures flow
/// through as denials, never escalation.
/// </remarks>
public sealed class DataverseSearchDataHandlerTests : TypedToolHandlerTestFixture
{
    private readonly Mock<IDataverseUserClient> _dataverse = new();

    private DataverseSearchDataHandler CreateHandler() =>
        new(_dataverse.Object, CreateLogger<DataverseSearchDataHandler>());

    private static AnalysisTool BuildSearchTool() =>
        BuildAnalysisTool(handlerClass: nameof(DataverseSearchDataHandler), name: "SYS-Dataverse Search Data");

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var services = BuildToolFrameworkServiceCollection();

        services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .Should().Contain(typeof(DataverseSearchDataHandler),
                because: "the handler type must be auto-discovered by the assembly scan (ADR-010)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        CreateHandler().HandlerId.Should().Be(nameof(DataverseSearchDataHandler));
    }

    [Fact]
    public void Metadata_IsValid()
    {
        var metadata = CreateHandler().Metadata;
        metadata.Name.Should().NotBeNullOrWhiteSpace();
        metadata.Description.Should().NotBeNullOrWhiteSpace();
        metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void SupportedToolTypes_IsNonEmpty()
    {
        CreateHandler().SupportedToolTypes.Should().NotBeNullOrEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Argument validation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_MissingQuery_Fails()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var result = CreateHandler().ValidateChat(ctx, BuildSearchTool());
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainMatch("*'query'*");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Search execution — GA-style record paths + citations
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsGaStyleRecordPathsAndCitations()
    {
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        _dataverse
            .Setup(d => d.PostAsync("/api/search/v2.0/query", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                {
                  "value": [
                    { "@search.score": 3.71, "entityname": "sprk_matter", "objectid": "{{id:D}}",
                      "highlights": { "sprk_name": ["{crmhit}Contoso{/crmhit} acquisition"] } }
                  ]
                }
                """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("count").GetInt32().Should().Be(1);
        var hit = data.GetProperty("results").EnumerateArray().Single();
        hit.GetProperty("path").GetString().Should().Be($"tables/sprk_matter/records/{id:D}",
            because: "search_data returns GA-MCP-style filesystem paths replayable via dataverse.describe");
        hit.GetProperty("entity").GetString().Should().Be("sprk_matter");

        result.Metadata.Should().ContainKey(ToolResultMetadataKeys.Citations);
        var citations = (IEnumerable<ToolResultCitation>)result.Metadata![ToolResultMetadataKeys.Citations]!;
        citations.Should().ContainSingle(c => c.ChunkId == $"tables/sprk_matter/records/{id:D}");
    }

    [Fact]
    public async Task ExecuteChatAsync_ScopeAsTableList_RestrictsSearchEntities()
    {
        string? capturedBody = null;
        _dataverse
            .Setup(d => d.PostAsync("/api/search/v2.0/query", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, body, _) => capturedBody = body)
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "value": [] }""")));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: """{"query":"smith", "scope":"account, contact"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("search").GetString().Should().Be("smith");
        var entities = doc.RootElement.GetProperty("entities").GetString();
        entities.Should().Contain("account").And.Contain("contact",
            because: "the native transport interprets scope as a table logical-name filter (documented GA deviation)");
    }

    [Fact]
    public async Task ExecuteChatAsync_SearchNotProvisioned_ReturnsDependencyUnavailable()
    {
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "Not found."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"anything"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.DependencyUnavailable);
        result.ErrorMessage.Should().Contain("dataverse.read_query",
            because: "the model gets a concrete fallback tool suggestion");
    }

    [Fact]
    public async Task ExecuteChatAsync_UserDenied_SurfacesAccessDenied()
    {
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "Access denied."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"anything"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            because: "the user's security context is the only context — denials flow through");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Response-shape resilience (v2.0 has shipped nested/string response variants)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_NestedResponseShape_StillParses()
    {
        var id = Guid.NewGuid();
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                { "response": { "value": [ { "entityname": "account", "objectid": "{{id:D}}" } ] } }
                """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"x"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Value.GetProperty("count").GetInt32().Should().Be(1);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 / NFR-07 telemetry — query text (user-authored) never logged
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_NeverLogsQueryTextOrHitContent_Adr015()
    {
        const string sensitiveQuery = "privileged acquisition target Northwind 2027";
        const string sensitiveHighlight = "board approved confidential purchase at 47 per share";
        var id = Guid.NewGuid();
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                {
                  "value": [
                    { "entityname": "sprk_document", "objectid": "{{id:D}}",
                      "highlights": { "content": ["{{sensitiveHighlight}}"] } }
                  ]
                }
                """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new { query = sensitiveQuery }));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        AssertTelemetryRespectsAdr015(sensitiveQuery, sensitiveHighlight);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // shared plumbing
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
