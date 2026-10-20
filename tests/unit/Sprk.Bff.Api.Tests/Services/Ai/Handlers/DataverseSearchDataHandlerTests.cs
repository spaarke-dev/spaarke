using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Identity;
using Xunit;
using Sprk.Bff.Api.Infrastructure.Dataverse;

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
    private static readonly Guid CallerSystemUserId = Guid.Parse("0a0a0a0a-0000-0000-0000-00000000c412");

    private readonly Mock<IDataverseUserClient> _dataverse = new();
    private readonly HttpContextAccessor _httpContextAccessor = new()
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) }
    };
    private readonly Mock<ICallerSystemUserResolver> _callerResolver = new();
    private readonly Mock<ISystemUserIdentityResolver> _identityResolver = new();

    public DataverseSearchDataHandlerTests()
    {
        // Default caller: a resolved INTERNAL systemuser (sprk_isexternal not true).
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
        _identityResolver
            .Setup(r => r.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private DataverseSearchDataHandler CreateHandler() =>
        new(_dataverse.Object, CreateLogger<DataverseSearchDataHandler>(), _httpContextAccessor,
            _callerResolver.Object, _identityResolver.Object);

    private void AsExternalCaller() =>
        _identityResolver
            .Setup(r => r.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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
    // G-P3 UAT round-1 H8 — canonical scope names + scoped-failure retry
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("matter", "sprk_matter")]
    [InlineData("matters", "sprk_matter")]
    [InlineData("project", "sprk_project")]
    [InlineData("invoices", "sprk_invoice")]
    [InlineData("documents", "sprk_document")]
    [InlineData("sprk_matter", "sprk_matter")]
    [InlineData("account", "account")]
    public void ToTableLogicalName_CanonicalAndPluralForms_MapToLogicalNames(string input, string expected)
    {
        DataverseSearchDataHandler.ToTableLogicalName(input).Should().Be(expected,
            "the model naturally uses the canonical vocabulary the host context puts in front of it — " +
            "sending 'matter' verbatim to the Search API rejected the WHOLE query (the G-P3 'technical issue')");
    }

    [Fact]
    public async Task ExecuteChatAsync_CanonicalScopeName_NormalizedToLogicalNameInRequest()
    {
        string? capturedBody = null;
        _dataverse
            .Setup(d => d.PostAsync("/api/search/v2.0/query", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, body, _) => capturedBody = body)
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "value": [] }""")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"commercial", "scope":"matter"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("entities").GetString().Should().Contain("sprk_matter")
            .And.NotContain("\"matter\"",
                "H8: canonical names normalize to the logical name the Search API accepts");
    }

    [Fact]
    public async Task ExecuteChatAsync_ScopedSearchRejected_RetriesOnceWithoutScopeAndSucceeds()
    {
        // The incident shape: the scoped query is rejected by the Search API; the fix
        // retries ONCE without the entity filter (deterministically — the exact recovery
        // the model attempted manually in the G-P3 transcript) instead of surfacing
        // "a technical issue with the search".
        var id = Guid.NewGuid();
        var bodies = new List<string>();
        _dataverse
            .Setup(d => d.PostAsync("/api/search/v2.0/query", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, body, _) => bodies.Add(body))
            .ReturnsAsync(() => bodies.Count == 1
                ? DataverseUserResponse.Fail(400, "BadRequest", "Entity some_bogus_table is not search-enabled.")
                : DataverseUserResponse.Ok(200, ParseJson($$"""
                    { "value": [ { "entityname": "sprk_matter", "objectid": "{{id:D}}" } ] }
                    """)));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: """{"query":"commercial matter", "scope":"some_bogus_table"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        bodies.Should().HaveCount(2, "exactly one scope-less retry after a scoped rejection");
        using (var first = JsonDocument.Parse(bodies[0]))
        {
            first.RootElement.TryGetProperty("entities", out _).Should().BeTrue("first attempt carries the scope");
        }
        using (var second = JsonDocument.Parse(bodies[1]))
        {
            second.RootElement.TryGetProperty("entities", out _).Should().BeFalse("the retry drops the entity filter");
        }

        result.Data!.Value.GetProperty("count").GetInt32().Should().Be(1);
        result.Warnings.Should().ContainMatch("*scope filter*",
            "the model is told the scope was dropped so it can relay honestly");
    }

    [Fact]
    public async Task ExecuteChatAsync_UnscopedSearchRejected_NoRetry_FailureSurfaces()
    {
        var calls = 0;
        _dataverse
            .Setup(d => d.PostAsync("/api/search/v2.0/query", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls++)
            .ReturnsAsync(DataverseUserResponse.Fail(400, "BadRequest", "Malformed search."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"anything"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        calls.Should().Be(1, "the H8 retry exists only for scoped rejections — nothing to drop otherwise");
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
    // ISS-010 (external-access r3 task 078) — external callers and communications
    // ═════════════════════════════════════════════════════════════════════════════

    private static readonly Guid MatterHit = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid CommunicationHit = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly Guid AttachmentHit = Guid.Parse("a0000000-0000-0000-0000-000000000003");

    private static string MixedHitsJson => $$"""
        {
          "value": [
            { "@search.score": 3.0, "entityname": "sprk_matter", "objectid": "{{MatterHit:D}}",
              "highlights": { "sprk_mattername": ["Contoso acquisition"] } },
            { "@search.score": 2.0, "entityname": "sprk_communication", "objectid": "{{CommunicationHit:D}}",
              "highlights": { "sprk_body": ["Internal-only strategy for Contoso"] } },
            { "@search.score": 1.0, "entityname": "sprk_communicationattachment", "objectid": "{{AttachmentHit:D}}" }
          ]
        }
        """;

    private List<string> CaptureSearchBodies(params DataverseUserResponse[] responses)
    {
        var bodies = new List<string>();
        var queue = new Queue<DataverseUserResponse>(responses);
        _dataverse
            .Setup(d => d.PostAsync("/api/search/v2.0/query", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, body, _) => bodies.Add(body))
            .ReturnsAsync(() => queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        return bodies;
    }

    private static List<string> ResultEntities(ToolResult result) =>
        result.Data!.Value.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("entity").GetString()!).ToList();

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerUnscoped_DropsCommunicationFamilyHitsOnly()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(MixedHitsJson)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        ResultEntities(result).Should().Equal("sprk_matter");
        result.Data!.Value.GetProperty("count").GetInt32().Should().Be(1, because: "the count is the post-filter count");
        result.Warnings.Should().BeEmpty(because: "nothing tells the model that hits were dropped");
        ((IEnumerable<ToolResultCitation>)result.Metadata![ToolResultMetadataKeys.Citations]!)
            .Should().ContainSingle(c => c.ChunkId == $"tables/sprk_matter/records/{MatterHit:D}");
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerScopeNamesCommunication_FilterEntryRemovedAndHitsDropped()
    {
        AsExternalCaller();
        var bodies = CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(MixedHitsJson)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso", "scope":"sprk_communication,sprk_matter"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        using var doc = JsonDocument.Parse(bodies.Single());
        doc.RootElement.GetProperty("entities").GetString().Should().Be("[\"sprk_matter\"]");
        ResultEntities(result).Should().Equal("sprk_matter");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerScopeNamesOnlyCommunicationFamily_NoSearchCallEmptyResult()
    {
        AsExternalCaller();

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso", "scope":"sprk_communication, sprk_communicationthread"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("count").GetInt32().Should().Be(0);
        result.Summary.Should().StartWith("No matching records were found");
        result.Warnings.Should().BeEmpty();
        _dataverse.Verify(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "never a search for a removed filter — and never an unscoped one");
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerScopedSearchRetriedUnscoped_CommunicationHitsStillDropped()
    {
        AsExternalCaller();
        var bodies = CaptureSearchBodies(
            DataverseUserResponse.Fail(400, DataverseUserClientErrorCodes.BadRequest, "Bad entity."),
            DataverseUserResponse.Ok(200, ParseJson(MixedHitsJson)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso", "scope":"sprk_communication,not_searchable"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        bodies.Should().HaveCount(2, because: "the H8 retry runs without the entity filter");
        ResultEntities(result).Should().Equal(new[] { "sprk_matter" },
            because: "family hits are dropped by entityname after every call, the unscoped retry included");
    }

    [Fact]
    public async Task ExecuteChatAsync_UnresolvedCaller_TreatedAsExternal()
    {
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-oid-claim"));
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(MixedHitsJson)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        ResultEntities(result).Should().Equal(new[] { "sprk_matter" }, because: "an unresolvable caller is external (fail closed)");
    }

    [Fact]
    public async Task ExecuteChatAsync_InternalCallerScopeNamesCommunication_RequestAndHitsUnchanged()
    {
        var bodies = CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(MixedHitsJson)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso", "scope":"sprk_communication,sprk_matter"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        bodies.Should().ContainSingle().Which.Should().Be(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["search"] = "contoso",
                ["top"] = DataverseSearchDataHandler.SearchTop,
                ["entities"] = "[\"sprk_communication\",\"sprk_matter\"]"
            }));
        ResultEntities(result).Should().Equal("sprk_matter", "sprk_communication", "sprk_communicationattachment");
        result.Data!.Value.GetProperty("count").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Telemetry_ExternalCommunicationGuard_LogsEntityAndReasonOnly_Adr015()
    {
        const string sensitiveQuery = "Rothstein confidential settlement strategy";
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(MixedHitsJson)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new { query = sensitiveQuery }));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        CapturedLogMessages.Should().Contain(m =>
            m.FormattedMessage.Contains(ExternalCallerCommunicationGuard.LogEvent) &&
            m.FormattedMessage.Contains("reason=family_hits_dropped"));
        AssertTelemetryRespectsAdr015(sensitiveQuery, "Internal-only strategy for Contoso");
    }

    // ── ISS-010 review round 1 ────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_emailartifact")]
    [InlineData("sprk_attachmentartifact")]
    [InlineData("sprk_emailreviewlog")]
    [InlineData("sprk_affinity")]
    public async Task ExecuteChatAsync_ExternalCallerContentTableOutsidePrefix_FilterRemovedAndHitsDropped(string table)
    {
        AsExternalCaller();
        var contentHit = Guid.NewGuid();
        var bodies = CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson($$"""
            {
              "value": [
                { "entityname": "sprk_matter", "objectid": "{{MatterHit:D}}" },
                { "entityname": "{{table}}", "objectid": "{{contentHit:D}}", "highlights": { "sprk_bodypreview": ["Internal-only body"] } }
              ]
            }
            """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$"""{"query":"contoso", "scope":"{{table}},sprk_matter"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        using var doc = JsonDocument.Parse(bodies.Single());
        doc.RootElement.GetProperty("entities").GetString().Should().Be("[\"sprk_matter\"]");
        ResultEntities(result).Should().Equal("sprk_matter");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerScopeNamesOnlyContentTable_NoSearchCall()
    {
        AsExternalCaller();

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso", "scope":"sprk_emailartifact"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("count").GetInt32().Should().Be(0);
        _dataverse.Verify(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalityReadThrows_ErrorWithNoData()
    {
        // Review F2: a sprk_communication hit triggers the lazy externality check; its fault is an error with no data.
        _identityResolver
            .Setup(r => r.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(MixedHitsJson)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_CallerLookupFailed_ErrorWithNoData()
    {
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved(ExternalCallerCommunicationGuard.LookupFailedReason));
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(MixedHitsJson)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeFalse(because: "a lookup fault decides nothing — never 'internal'");
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
    }

    // ── sprk_document hits carrying a related communication (review round 1, F1) ──

    private static readonly Guid DocNoComm = Guid.Parse("d0000000-0000-0000-0000-000000000011");
    private static readonly Guid DocShared = Guid.Parse("d0000000-0000-0000-0000-000000000012");
    private static readonly Guid DocInternal = Guid.Parse("d0000000-0000-0000-0000-000000000013");
    private static readonly Guid DocNotReturned = Guid.Parse("d0000000-0000-0000-0000-000000000014");
    private static readonly Guid DocUnreadableComm = Guid.Parse("d0000000-0000-0000-0000-000000000015");
    private static readonly Guid CommShared = Guid.Parse("c1000000-0000-0000-0000-000000000012");
    private static readonly Guid CommInternal = Guid.Parse("c1000000-0000-0000-0000-000000000013");
    private static readonly Guid CommUnreadable = Guid.Parse("c1000000-0000-0000-0000-000000000015");
    private static readonly Guid ThreadOpen = Guid.Parse("71000000-0000-0000-0000-000000000011");

    private static string DocumentHitsJson => $$"""
        {
          "value": [
            { "entityname": "sprk_matter", "objectid": "{{MatterHit:D}}" },
            { "entityname": "sprk_document", "objectid": "{{DocNoComm:D}}" },
            { "entityname": "sprk_document", "objectid": "{{DocShared:D}}" },
            { "entityname": "sprk_document", "objectid": "{{DocInternal:D}}", "highlights": { "sprk_filesummary": ["Internal-only summary"] } },
            { "entityname": "sprk_document", "objectid": "{{DocNotReturned:D}}" },
            { "entityname": "sprk_document", "objectid": "{{DocUnreadableComm:D}}" }
          ]
        }
        """;

    private List<string> CaptureGets(string prefix, string json)
    {
        var captured = new List<string>();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith(prefix)), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => captured.Add(p))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(json)));
        return captured;
    }

    private void SetupDocumentVisibilityReads(out List<string> documentReads, out List<string> communicationReads)
    {
        // DocNotReturned is absent from the document read (fail closed); CommUnreadable is absent from the
        // communication read (unreadable or deleted).
        documentReads = CaptureGets("sprk_documents?", $$"""
            {
              "value": [
                { "sprk_documentid": "{{DocNoComm:D}}", "_sprk_relatedcommunication_value": null },
                { "sprk_documentid": "{{DocShared:D}}", "_sprk_relatedcommunication_value": "{{CommShared:D}}" },
                { "sprk_documentid": "{{DocInternal:D}}", "_sprk_relatedcommunication_value": "{{CommInternal:D}}" },
                { "sprk_documentid": "{{DocUnreadableComm:D}}", "_sprk_relatedcommunication_value": "{{CommUnreadable:D}}" }
              ]
            }
            """);
        communicationReads = CaptureGets("sprk_communications?", $$"""
            {
              "value": [
                { "sprk_communicationid": "{{CommShared:D}}", "sprk_isinternalonly": false, "_sprk_communicationthread_value": "{{ThreadOpen:D}}" },
                { "sprk_communicationid": "{{CommInternal:D}}", "sprk_isinternalonly": true, "_sprk_communicationthread_value": "{{ThreadOpen:D}}" }
              ]
            }
            """);
        CaptureGets("sprk_communicationthreads?", $$"""
            { "value": [ { "sprk_communicationthreadid": "{{ThreadOpen:D}}", "sprk_privacystate": 100000000 } ] }
            """);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocumentHits_DropsDocumentsWhoseRelatedCommunicationFailsRule()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(DocumentHitsJson)));
        SetupDocumentVisibilityReads(out var documentReads, out var communicationReads);

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        documentReads.Should().ContainSingle().Which.Should().Be(
            "sprk_documents?$select=sprk_documentid,_sprk_relatedcommunication_value" +
            $"&$filter=sprk_documentid eq {DocNoComm:D} or sprk_documentid eq {DocShared:D} or sprk_documentid eq {DocInternal:D} or " +
            $"sprk_documentid eq {DocNotReturned:D} or sprk_documentid eq {DocUnreadableComm:D}&$top=5",
            because: "ONE batched read of the document hits' related communication");
        communicationReads.Should().ContainSingle();
        result.Data!.Value.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("recordId").GetString())
            .Should().Equal(new[] { MatterHit.ToString("D"), DocNoComm.ToString("D"), DocShared.ToString("D") },
                because: "internal-only, unreadable and unconfirmed related communications hide the document; others are unchanged");
        result.Data!.Value.GetProperty("count").GetInt32().Should().Be(3, because: "the count is the post-filter count");
        result.Warnings.Should().BeEmpty(because: "nothing tells the model that hits were dropped");
        CapturedLogMessages.Should().Contain(m =>
            m.FormattedMessage.Contains(ExternalCallerCommunicationGuard.LogEvent) &&
            m.FormattedMessage.Contains("entity=sprk_document") &&
            m.FormattedMessage.Contains("reason=related_communication_hidden"));
        AssertTelemetryRespectsAdr015("Internal-only summary");
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocumentRelatedReadFails_ErrorWithNoData()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(DocumentHitsJson)));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_documents?")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "Server error."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_InternalCallerDocumentHits_UnchangedAndNoExtraRead()
    {
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(DocumentHitsJson)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("count").GetInt32().Should().Be(6);
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "an internal caller's search makes no extra Dataverse read");
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
