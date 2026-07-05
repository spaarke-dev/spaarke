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
/// Unit tests for <see cref="DataverseCreateRecordHandler"/> (spaarke-ai-architecture-redesign-r1
/// task 009, FR-P0-07 write half).
/// </summary>
/// <remarks>
/// <para>
/// The Dataverse boundary is mocked at <see cref="IDataverseUserClient"/> (module boundary —
/// NOT an HttpMessageHandler mock, per docs/standards/TEST-ARCHITECTURE.md mock rules /
/// ADR-038). Tests exercise the handler under a simulated test USER's security context: the
/// mocked boundary returns exactly what Dataverse would return for that user (403 when the
/// user lacks create privilege, 404 for tables invisible to the user) and the tests assert the
/// handler surfaces the user-scoped outcome instead of escalating.
/// </para>
/// </remarks>
public sealed class DataverseCreateRecordHandlerTests : TypedToolHandlerTestFixture
{
    private readonly Mock<IDataverseUserClient> _dataverse = new();

    private DataverseCreateRecordHandler CreateHandler() =>
        new(_dataverse.Object, CreateLogger<DataverseCreateRecordHandler>());

    private static AnalysisTool BuildCreateTool() =>
        BuildAnalysisTool(handlerClass: nameof(DataverseCreateRecordHandler), name: "SYS-Dataverse Create Record");

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private void SetupEntityMetadata(string logicalName, string entitySetName, string primaryIdAttribute)
    {
        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.StartsWith($"EntityDefinitions(LogicalName='{logicalName}')?$select=EntitySetName")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(
                $$"""{ "EntitySetName": "{{entitySetName}}", "PrimaryIdAttribute": "{{primaryIdAttribute}}" }""")));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests (HandlerContractTestTemplate, retargeted)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);

        services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .Should().Contain(
                typeof(DataverseCreateRecordHandler),
                because: "the handler type must be auto-discovered by the assembly scan (ADR-010) — the FR-P0-04 bijection health check requires a registered handler per seeded dataverse.* row");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        CreateHandler().HandlerId.Should().Be(
            nameof(DataverseCreateRecordHandler),
            because: "HandlerId == nameof(handler class) routes sprk_handlerclass at runtime");
    }

    [Fact]
    public void Validate_InPlaybookContext_Rejects()
    {
        var result = CreateHandler().Validate(BuildToolExecutionContext(), BuildCreateTool());
        result.IsValid.Should().BeFalse(because: "dataverse.create_record is an agent-loop (chat) tool");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Argument validation (GA contract: create_record(tablename, item))
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("{}", "*tablename*")]
    [InlineData("""{"tablename":"account"}""", "*item*")]
    [InlineData("""{"tablename":"account","item":"not-an-object"}""", "*item*")]
    [InlineData("""{"tablename":"Robert'); DROP","item":{"name":"x"}}""", "*logical name*")]
    public void ValidateChat_InvalidArgs_Fails(string argsJson, string expectedErrorPattern)
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);
        var result = CreateHandler().ValidateChat(ctx, BuildCreateTool());
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainMatch(expectedErrorPattern);
    }

    [Fact]
    public async Task ExecuteChatAsync_EmptyItem_FailsValidation_WithoutWriting()
    {
        SetupEntityMetadata("account", "accounts", "accountid");
        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"tablename":"account","item":{}}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildCreateTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        _dataverse.Verify(
            d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never,
            failMessage: "an empty item must never reach Dataverse");
    }

    [Fact]
    public async Task ExecuteChatAsync_ItemKeyWithODataAnnotation_FailsValidation()
    {
        // The LLM must not be able to smuggle raw @odata.bind annotations through 'item'.
        SetupEntityMetadata("account", "accounts", "accountid");
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: """{"tablename":"account","item":{"ownerid@odata.bind":"/systemusers(00000000-0000-0000-0000-000000000001)"}}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildCreateTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        _dataverse.Verify(
            d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Successful create — created-record citation shape (ADR-039 grounding)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ValidCreate_ReturnsCreatedRecordIdAndCitation()
    {
        var createdId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        SetupEntityMetadata("account", "accounts", "accountid");

        string? postedPath = null;
        string? postedBody = null;
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, CancellationToken>((path, body, _, _) => { postedPath = path; postedBody = body; })
            .ReturnsAsync(DataverseUserResponse.Ok(201, ParseJson(
                $$"""{ "@odata.etag": "W/\"1\"", "accountid": "{{createdId:D}}", "name": "Contoso" }""")));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: """{"tablename":"account","item":{"name":"Contoso","numberofemployees":25,"sprk_active":true}}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildCreateTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.Data!.Value;
        data.GetProperty("tool").GetString().Should().Be("dataverse.create_record");
        data.GetProperty("tablename").GetString().Should().Be("account");
        data.GetProperty("recordId").GetString().Should().Be(createdId.ToString("D"));
        data.GetProperty("path").GetString().Should().Be($"tables/account/records/{createdId:D}");
        data.GetProperty("columnCount").GetInt32().Should().Be(3);

        // The POST goes to the resolved entity set (metadata-resolved, never guessed) and the
        // JSON body preserves the original value types.
        postedPath.Should().Be("/api/data/v9.2/accounts");
        using var bodyDoc = JsonDocument.Parse(postedBody!);
        bodyDoc.RootElement.GetProperty("name").GetString().Should().Be("Contoso");
        bodyDoc.RootElement.GetProperty("numberofemployees").GetInt32().Should().Be(25);
        bodyDoc.RootElement.GetProperty("sprk_active").GetBoolean().Should().BeTrue();

        // ADR-039 grounding: adapter-level citation for the CREATED record.
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Citations);
        var citations = (IEnumerable<ToolResultCitation>)result.Metadata![ToolResultMetadataKeys.Citations]!;
        citations.Should().ContainSingle(c =>
            c.ChunkId == $"tables/account/records/{createdId:D}" && c.SourceName == "account" && c.SourceType == "dataverse");
    }

    [Fact]
    public async Task ExecuteChatAsync_LookupValue_BindsViaMetadataResolvedNavigationProperty()
    {
        var relatedId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var createdId = Guid.NewGuid();
        SetupEntityMetadata("sprk_event", "sprk_events", "sprk_eventid");

        // Navigation-property metadata: custom lookup casing differs from the column logical
        // name — the mapper must use ReferencingEntityNavigationPropertyName, never guess.
        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.Contains("ManyToOneRelationships")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""
                {
                  "LogicalName": "sprk_event",
                  "ManyToOneRelationships": [
                    { "ReferencingAttribute": "sprk_matterid", "ReferencingEntityNavigationPropertyName": "sprk_MatterId", "ReferencedEntity": "sprk_matter" }
                  ]
                }
                """)));
        _dataverse
            .Setup(d => d.GetAsync(
                It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_matter')")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "sprk_matters" }""")));

        string? postedBody = null;
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, CancellationToken>((_, body, _, _) => postedBody = body)
            .ReturnsAsync(DataverseUserResponse.Ok(201, ParseJson($$"""{ "sprk_eventid": "{{createdId:D}}" }""")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: $$$"""
            {"tablename":"sprk_event","item":{
              "sprk_name":"Follow up",
              "sprk_matterid":{"relatedTable":"sprk_matter","name":"Ignored Display Name","recordId":"{{{relatedId:D}}}"}
            }}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildCreateTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        using var bodyDoc = JsonDocument.Parse(postedBody!);
        bodyDoc.RootElement.GetProperty("sprk_MatterId@odata.bind").GetString()
            .Should().Be($"/sprk_matters({relatedId:D})",
                because: "lookups bind via the metadata-resolved navigation property + related entity set");
        bodyDoc.RootElement.TryGetProperty("sprk_matterid", out _).Should().BeFalse(
            because: "the raw lookup object must not pass through as a column value");
    }

    [Fact]
    public async Task ExecuteChatAsync_LookupWithoutRecordId_FailsWithSearchGuidance()
    {
        SetupEntityMetadata("sprk_event", "sprk_events", "sprk_eventid");
        var ctx = BuildChatInvocationContext(toolArgumentsJson: """
            {"tablename":"sprk_event","item":{"sprk_matterid":{"relatedTable":"sprk_matter","name":"Contoso v. Fabrikam"}}}
            """);
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildCreateTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("recordId",
            because: "the native transport never resolves lookups by name — the model is redirected to search first");
        _dataverse.Verify(
            d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // User-context outcomes — privilege-denied flow-through + fail-closed (spec MUST)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_UserLacksCreatePrivilege_SurfacesAccessDeniedNotEscalation()
    {
        // Test-user security context: Dataverse (via the user's OBO token) refuses the create.
        // The handler MUST surface the denial — never retry app-only (there is no app-only path).
        SetupEntityMetadata("account", "accounts", "accountid");
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied,
                "The user does not have create access to the account entity."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"tablename":"account","item":{"name":"Contoso"}}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildCreateTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            because: "a write the user lacks privileges for fails with the USER's own access error (spec MUST rule)");
    }

    [Fact]
    public async Task ExecuteChatAsync_TableInvisibleToUser_FailsNotFoundBeforeAnyWrite()
    {
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound,
                "The entity with a name = 'secrettable' was not found in the MetadataCache."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"tablename":"secrettable","item":{"name":"x"}}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildCreateTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.NotFound);
        _dataverse.Verify(
            d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never,
            failMessage: "a table invisible to the user must 404 at metadata resolution BEFORE any write is attempted");
    }

    [Fact]
    public async Task ExecuteChatAsync_MissingUserContext_FailsClosed()
    {
        // The MUST-rule fail-closed path: no user bearer token → no Dataverse call, never app-only.
        _dataverse
            .Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired,
                "No bearer token on the current request; dataverse.* tools execute only under an authenticated user's session."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"tablename":"account","item":{"name":"Contoso"}}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildCreateTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 / NFR-07 telemetry — column VALUES never appear in logs
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_NeverLogsColumnValues_Adr015()
    {
        const string sensitiveValue = "Confidential settlement amount 9,750,000 payable to claimant";
        var createdId = Guid.NewGuid();
        SetupEntityMetadata("account", "accounts", "accountid");
        _dataverse
            .Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(201, ParseJson($$"""{ "accountid": "{{createdId:D}}" }""")));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $$$"""{"tablename":"account","item":{"description":"{{{sensitiveValue}}}"}}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildCreateTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        AssertTelemetryRespectsAdr015(sensitiveValue);
    }
}
