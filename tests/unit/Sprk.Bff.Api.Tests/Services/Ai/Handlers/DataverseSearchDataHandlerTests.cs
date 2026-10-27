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

    /// <summary>Task 188: what the BFF reads APP-ONLY when it decides an external caller's document hits.</summary>
    private readonly AppOnlyDocumentWorld _appOnly = new();

    /// <summary>Task 098: the assistant tools' (a') invoice-family seam (a test that reaches it sets one).</summary>
    private Sprk.Bff.Api.Infrastructure.ExternalAccess.AppOnlyInvoiceFamilyHiding _hideInvoiceFamily = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyHidingStubs.NeverCalled;

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
            _callerResolver.Object, _identityResolver.Object, _appOnly.Narrowing, _hideInvoiceFamily);

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

    // ═════════════════════════════════════════════════════════════════════════════
    // ebilling-r1 task 098 surface (d): the (a') invoice predicate on invoice-family hits
    // ═════════════════════════════════════════════════════════════════════════════

    private static readonly Guid InvoiceHitVisible = Guid.Parse("09800000-0000-4000-8000-0000000000b1");
    private static readonly Guid InvoiceHitHidden = Guid.Parse("09800000-0000-4000-8000-0000000000b2");
    private static readonly Guid SignalHitHidden = Guid.Parse("09800000-0000-4000-8000-0000000000b3");

    private static string InvoiceFamilyHitsJson =>
        "{ \"value\": [ "
        + "{ \"@search.score\": 4.0, \"entityname\": \"sprk_matter\", \"objectid\": \"" + MatterHit.ToString("D") + "\" }, "
        + "{ \"@search.score\": 3.0, \"entityname\": \"sprk_invoice\", \"objectid\": \"" + InvoiceHitVisible.ToString("D") + "\" }, "
        + "{ \"@search.score\": 2.0, \"entityname\": \"sprk_invoice\", \"objectid\": \"" + InvoiceHitHidden.ToString("D") + "\" }, "
        + "{ \"@search.score\": 1.0, \"entityname\": \"sprk_signal\", \"objectid\": \"" + SignalHitHidden.ToString("D") + "\" } "
        + "] }";

    [Fact]
    public async Task ExecuteChatAsync_Task098_ExternalCaller_HiddenInvoiceFamilyHitsAreDropped_OneSeamCallPerTable()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(InvoiceFamilyHitsJson)));
        var calls = new List<string>();
        _hideInvoiceFamily = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyHidingStubs.Hiding(calls, InvoiceHitHidden, SignalHitHidden);

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        ResultEntities(result).Should().Equal("sprk_matter", "sprk_invoice");
        result.Data!.Value.GetProperty("count").GetInt32().Should().Be(2);
        result.Warnings.Should().BeEmpty("nothing tells the model that hits were dropped");
        calls.Should().BeEquivalentTo(new[] { "sprk_invoice:2", "sprk_signal:1" });
    }

    [Fact]
    public async Task ExecuteChatAsync_Task098_ExternalCaller_ThePredicateReadFails_ErrorWithNoData()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(InvoiceFamilyHitsJson)));
        _hideInvoiceFamily = Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceFamilyHidingStubs.Throwing;

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeFalse("a predicate read fault can never turn into 'show it'");
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_Task098_InternalCaller_InvoiceFamilyHitsNeverReachThePredicate()
    {
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(InvoiceFamilyHitsJson)));
        // _hideInvoiceFamily stays NeverCalled: any call would fail the tool.

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        ResultEntities(result).Should().Equal("sprk_matter", "sprk_invoice", "sprk_invoice", "sprk_signal");
    }

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

    /// <summary>
    /// Task 188: the app-only truth for the document hits (the narrowing reads them by id APP-ONLY, never through the user's
    /// client). DocNotReturned is not seeded (fail closed); CommUnreadable is not seeded (unreadable or deleted).
    /// </summary>
    private void SeedAppOnlyDocuments()
    {
        _appOnly.Document(DocNoComm);
        _appOnly.Document(DocShared, communication: CommShared);
        _appOnly.Document(DocInternal, communication: CommInternal);
        _appOnly.Document(DocUnreadableComm, communication: CommUnreadable);
        _appOnly.Thread(ThreadOpen, 100000000);
        _appOnly.Communication(CommShared, internalOnly: false, ThreadOpen);
        _appOnly.Communication(CommInternal, internalOnly: true, ThreadOpen);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocumentHits_DropsDocumentsWhoseRelatedCommunicationFailsRule_DecidedAppOnly()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(DocumentHitsJson)));
        SeedAppOnlyDocuments();

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _appOnly.Reads("sprk_documents").Should().ContainSingle().Which.Should().EndWith(
            "sprk_documents?$select=sprk_documentid,sprk_accesspermission,_sprk_relatedcommunication_value,_sprk_parentdocument_value" +
            $"&$filter=sprk_documentid eq {DocNoComm:D} or sprk_documentid eq {DocShared:D} or sprk_documentid eq {DocInternal:D} or " +
            $"sprk_documentid eq {DocNotReturned:D} or sprk_documentid eq {DocUnreadableComm:D}&$top=5",
            because: "ONE batched by-id read of the document hits, with the predicate's columns, APP-ONLY");
        _appOnly.Reads("sprk_communications").Should().ContainSingle();
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
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
    public async Task ExecuteChatAsync_ExternalCallerDocumentHit_TheUsersReadWouldShowNoLink_AppOnlyReadsAnInternalOnlyCommunication_Dropped()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson($$"""
            { "value": [ { "entityname": "sprk_document", "objectid": "{{DocInternal:D}}" } ] }
            """)));
        SeedAppOnlyDocuments();
        // Were the narrowing to read through the user's client, field security would hand it a null link.
        CaptureGets("sprk_documents?", $$"""
            { "value": [ { "sprk_documentid": "{{DocInternal:D}}", "_sprk_relatedcommunication_value": null } ] }
            """);

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("results").EnumerateArray().Should().BeEmpty(
            "the app-only read sees the internal-only communication: the hit is dropped");
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocumentHitWithUnreadableRelatedCommunicationLink_Dropped()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson($$"""
            {
              "value": [
                { "entityname": "sprk_document", "objectid": "{{DocNoComm:D}}" },
                { "entityname": "sprk_document", "objectid": "{{DocShared:D}}" }
              ]
            }
            """)));
        _appOnly.Document(DocNoComm);
        _appOnly.Document(DocShared, rawCommunication: "not-a-guid");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("recordId").GetString())
            .Should().Equal(new[] { DocNoComm.ToString("D") },
                because: "a link that is present but not a usable id hides the hit (fail closed)");
        _appOnly.Reads("sprk_communications").Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocumentRelatedReadFails_ErrorWithNoData()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(DocumentHitsJson)));
        SeedAppOnlyDocuments();
        _appOnly.Faults["sprk_documents"] = System.Net.HttpStatusCode.InternalServerError;

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
        _appOnly.Calls.Should().Be(0, "an internal caller is not narrowed");
    }

    // ── Task 188 (D7 T7, #2126): THE external document predicate in search_data ──

    private static readonly Guid DocOwnRestricted = Guid.Parse("d1880000-0000-0000-0000-000000000011");
    private static readonly Guid DocUnderRestrictedEml = Guid.Parse("d1880000-0000-0000-0000-000000000012");
    private static readonly Guid DocUnderInternalEml = Guid.Parse("d1880000-0000-0000-0000-000000000013");
    private static readonly Guid DocUnderStandardEml = Guid.Parse("d1880000-0000-0000-0000-000000000014");
    private static readonly Guid DocOwnLinkUnderRestrictedEml = Guid.Parse("d1880000-0000-0000-0000-000000000015");
    private static readonly Guid EmlRestricted = Guid.Parse("e1880000-0000-0000-0000-000000000011");
    private static readonly Guid EmlInternal = Guid.Parse("e1880000-0000-0000-0000-000000000012");
    private static readonly Guid EmlStandard = Guid.Parse("e1880000-0000-0000-0000-000000000013");

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDocumentHits_Task188_OwnOrParentRestrictedOrInternalOnlyParent_Dropped()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson($$"""
            {
              "value": [
                { "entityname": "sprk_document", "objectid": "{{DocOwnRestricted:D}}" },
                { "entityname": "sprk_document", "objectid": "{{DocUnderRestrictedEml:D}}" },
                { "entityname": "sprk_document", "objectid": "{{DocUnderInternalEml:D}}" },
                { "entityname": "sprk_document", "objectid": "{{DocUnderStandardEml:D}}" },
                { "entityname": "sprk_document", "objectid": "{{DocOwnLinkUnderRestrictedEml:D}}" }
              ]
            }
            """)));
        SeedAppOnlyDocuments();
        _appOnly.Document(DocOwnRestricted, permission: 100000002);
        _appOnly.Document(DocUnderRestrictedEml, parent: EmlRestricted);
        _appOnly.Document(DocUnderInternalEml, parent: EmlInternal);
        _appOnly.Document(DocUnderStandardEml, parent: EmlStandard);
        _appOnly.Document(DocOwnLinkUnderRestrictedEml, communication: CommShared, parent: EmlRestricted);
        _appOnly.Document(EmlRestricted, permission: 100000002);
        _appOnly.Document(EmlInternal, communication: CommInternal);
        _appOnly.Document(EmlStandard, permission: 100000000);

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("recordId").GetString())
            .Should().Equal(new[] { DocUnderStandardEml.ToString("D") },
                because: "own Restricted, a Restricted parent .eml (even under the hit's own visible link) and a parent .eml of " +
                         "an internal-only email hide the hit");
        _appOnly.Reads("sprk_documents").Should().HaveCount(2, "the hits, then one batched read of their parents");
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
    }

    // ── Task 188 round 3 slice C: hits on tables that derive from a document (sprk_analysis, sprk_analysisoutput) ──

    private static readonly Guid AnOwnRestricted = Guid.Parse("a1880000-0000-0000-0000-000000000011");
    private static readonly Guid AnInternalOnly = Guid.Parse("a1880000-0000-0000-0000-000000000012");
    private static readonly Guid AnUndecidable = Guid.Parse("a1880000-0000-0000-0000-000000000013");
    private static readonly Guid AnVisible = Guid.Parse("a1880000-0000-0000-0000-000000000014");
    private static readonly Guid AnNoDocument = Guid.Parse("a1880000-0000-0000-0000-000000000015");
    private static readonly Guid AnStaleHit = Guid.Parse("a1880000-0000-0000-0000-000000000016");
    private static readonly Guid AnOutputFileHidden = Guid.Parse("a1880000-0000-0000-0000-000000000017");
    private static readonly Guid AnOutputFileUndecidable = Guid.Parse("a1880000-0000-0000-0000-000000000018");
    private static readonly Guid AnOutputFileVisible = Guid.Parse("a1880000-0000-0000-0000-000000000019");
    private static readonly Guid OutOfRestricted = Guid.Parse("0a180000-0000-0000-0000-000000000011");
    private static readonly Guid OutOfVisible = Guid.Parse("0a180000-0000-0000-0000-000000000012");
    private static readonly Guid DocNeverSeeded = Guid.Parse("d1880000-0000-0000-0000-0000000000fe");

    private static string DerivedHitsJson => $$"""
        {
          "value": [
            { "entityname": "sprk_matter", "objectid": "{{MatterHit:D}}" },
            { "entityname": "sprk_analysis", "objectid": "{{AnOwnRestricted:D}}", "highlights": { "sprk_name": ["Restricted analysis text"] } },
            { "entityname": "sprk_analysis", "objectid": "{{AnInternalOnly:D}}" },
            { "entityname": "sprk_analysis", "objectid": "{{AnUndecidable:D}}" },
            { "entityname": "sprk_analysis", "objectid": "{{AnVisible:D}}" },
            { "entityname": "sprk_analysis", "objectid": "{{AnNoDocument:D}}" },
            { "entityname": "sprk_analysis", "objectid": "{{AnStaleHit:D}}" },
            { "entityname": "sprk_analysisoutput", "objectid": "{{OutOfRestricted:D}}" },
            { "entityname": "sprk_analysisoutput", "objectid": "{{OutOfVisible:D}}" }
          ]
        }
        """;

    /// <summary>
    /// The user's source-link reads (by id): analyses → their document and output file; outputs → their analysis. AnStaleHit
    /// is not returned (deleted since it was indexed, or no longer readable).
    /// </summary>
    private List<string> SeedDerivedSources()
    {
        SeedAppOnlyDocuments();
        _appOnly.Document(DocOwnRestricted, permission: 100000002);
        var analyses = new Dictionary<Guid, string?>
        {
            [AnOwnRestricted] = DocOwnRestricted.ToString("D"),
            [AnInternalOnly] = DocInternal.ToString("D"),
            [AnUndecidable] = DocNeverSeeded.ToString("D"),
            [AnVisible] = DocShared.ToString("D"),
            [AnNoDocument] = null,
            [AnOutputFileHidden] = DocShared.ToString("D"),
            [AnOutputFileUndecidable] = DocShared.ToString("D"),
            [AnOutputFileVisible] = null,
        };
        var outputFiles = new Dictionary<Guid, string?>
        {
            [AnOutputFileHidden] = DocOwnRestricted.ToString("D"),
            [AnOutputFileUndecidable] = DocNeverSeeded.ToString("D"),
            [AnOutputFileVisible] = DocShared.ToString("D"),
        };
        var outputs = new Dictionary<Guid, string?> { [OutOfRestricted] = AnOwnRestricted.ToString("D"), [OutOfVisible] = AnVisible.ToString("D") };
        var captured = new List<string>();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_analysis")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => captured.Add(p))
            .ReturnsAsync((string p, CancellationToken _) =>
            {
                var (seed, pk, link) = p.StartsWith("sprk_analysisoutputs?")
                    ? (outputs, "sprk_analysisoutputid", "_sprk_analysisid_value")
                    : (analyses, "sprk_analysisid", "_sprk_documentid_value");
                var rows = System.Text.RegularExpressions.Regex.Matches(Uri.UnescapeDataString(p), $@"{pk} eq ([0-9a-f-]{{36}})")
                    .Select(m => Guid.Parse(m.Groups[1].Value))
                    .Where(seed.ContainsKey)
                    .Select(id =>
                    {
                        var row = new Dictionary<string, object?> { [pk] = id.ToString("D"), [link] = seed[id] };
                        if (Uri.UnescapeDataString(p).Contains(",_sprk_outputfileid_value"))
                            row["_sprk_outputfileid_value"] = outputFiles.GetValueOrDefault(id);
                        if (Uri.UnescapeDataString(p).Contains(",_sprk_regardingdocument_value"))
                            row["_sprk_regardingdocument_value"] = RegardingDocuments.GetValueOrDefault(id);
                        return row;
                    });
                return DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = rows }));
            });
        return captured;
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerAnalysisHits_DropsThoseWhoseOutputFileIsHiddenOrUndecidable_OdC1()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson($$"""
            {
              "value": [
                { "entityname": "sprk_analysis", "objectid": "{{AnVisible:D}}" },
                { "entityname": "sprk_analysis", "objectid": "{{AnOutputFileHidden:D}}" },
                { "entityname": "sprk_analysis", "objectid": "{{AnOutputFileUndecidable:D}}" },
                { "entityname": "sprk_analysis", "objectid": "{{AnOutputFileVisible:D}}" }
              ]
            }
            """)));
        var userReads = SeedDerivedSources();

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("recordId").GetString())
            .Should().Equal(new[] { AnVisible.ToString("D"), AnOutputFileVisible.ToString("D") },
                because: "an analysis whose OUTPUT FILE is hidden or cannot be decided is dropped even when its source document " +
                         "is visible; a visible output file is unaffected");
        Uri.UnescapeDataString(userReads.Should().ContainSingle().Which).Should().StartWith(
            "sprk_analysises?$select=sprk_analysisid,_sprk_documentid_value,_sprk_outputfileid_value,_sprk_regardingdocument_value&$filter=");
        _appOnly.Calls.Should().Be(1);
    }

    /// <summary>Task 188 r4b: the typed REGARDING document link the user's client returns for an analysis (absent: null).</summary>
    private Dictionary<Guid, string?> RegardingDocuments { get; } = new();

    [Fact(DisplayName = "Task 188 r4b: search_data drops an analysis hit whose typed REGARDING document is hidden or undecidable")]
    public async Task ExecuteChatAsync_ExternalCallerAnalysisHits_DropsThoseWhoseRegardingDocumentIsHiddenOrUndecidable_Round4b()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson($$"""
            {
              "value": [
                { "entityname": "sprk_analysis", "objectid": "{{AnVisible:D}}" },
                { "entityname": "sprk_analysis", "objectid": "{{AnOutputFileVisible:D}}" }
              ]
            }
            """)));
        SeedDerivedSources();
        RegardingDocuments[AnVisible] = DocOwnRestricted.ToString("D");
        RegardingDocuments[AnOutputFileVisible] = DocShared.ToString("D");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("recordId").GetString())
            .Should().Equal(new[] { AnOutputFileVisible.ToString("D") },
                because: "an analysis ABOUT a Restricted document is dropped even when its source document is visible");
        _appOnly.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDerivedHits_DropsAnalysesAndOutputsWhoseSourceDocumentIsHiddenOrUndecidable()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(DerivedHitsJson)));
        var userReads = SeedDerivedSources();

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("recordId").GetString())
            .Should().Equal(new[] { MatterHit.ToString("D"), AnVisible.ToString("D"), AnNoDocument.ToString("D"), OutOfVisible.ToString("D") },
                because: "an analysis (or an output, through its analysis) of an own-Restricted document, of an internal-only " +
                         "email's document, of a document the BFF cannot decide, or one the source read does not return is " +
                         "dropped; a visible source and no source are unaffected");
        result.Data!.Value.GetProperty("count").GetInt32().Should().Be(4, because: "the count is the post-filter count");
        result.Warnings.Should().BeEmpty(because: "nothing tells the model that hits were dropped");
        userReads.Should().HaveCount(3, "one batched source read of the analysis hits, one of the output hits, one of their analyses");
        _appOnly.Calls.Should().Be(1, "ONE hide-only narrowing call for every source document of the page");
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
        CapturedLogMessages.Should().Contain(m =>
            m.FormattedMessage.Contains(ExternalCallerCommunicationGuard.LogEvent) &&
            m.FormattedMessage.Contains("source_document_hidden=4") &&
            m.FormattedMessage.Contains("source_document_undecidable=1"));
        AssertTelemetryRespectsAdr015("Restricted analysis text", AnOwnRestricted.ToString("D"));
    }

    [Fact]
    public async Task ExecuteChatAsync_InternalCallerDerivedHits_UnchangedAndNoExtraRead()
    {
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(DerivedHitsJson)));
        SeedDerivedSources();

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("count").GetInt32().Should().Be(9);
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "an internal caller's search makes no extra Dataverse read");
        _appOnly.Calls.Should().Be(0, "an internal caller is not narrowed");
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDerivedHits_SourceReadFails_ErrorWithNoData()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(DerivedHitsJson)));
        SeedDerivedSources();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_analysises?")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "down"));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeFalse("a source read fault never turns into 'show it'");
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerDerivedHits_NarrowingFails_ErrorWithNoData()
    {
        AsExternalCaller();
        CaptureSearchBodies(DataverseUserResponse.Ok(200, ParseJson(DerivedHitsJson)));
        SeedDerivedSources();
        _appOnly.Faults["sprk_documents"] = System.Net.HttpStatusCode.InternalServerError;

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"query":"contoso"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildSearchTool(), CancellationToken.None);

        result.Success.Should().BeFalse("a narrowing fault never shows an analysis");
        result.Data.Should().BeNull();
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
