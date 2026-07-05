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
/// Unit tests for <see cref="DataverseDescribeHandler"/> (spaarke-ai-architecture-redesign-r1
/// task 008, FR-P0-07 read half).
/// </summary>
/// <remarks>
/// <para>
/// The Dataverse boundary is mocked at <see cref="IDataverseUserClient"/> (module boundary —
/// NOT an HttpMessageHandler mock, per docs/standards/TEST-ARCHITECTURE.md mock rules /
/// ADR-038). Tests exercise the handler under a simulated test USER's security context: the
/// mocked boundary returns exactly what Dataverse would return for that user (403 for records
/// the user cannot read, 404 for records invisible to the user) and the tests assert the
/// handler surfaces the user-scoped outcome instead of escalating.
/// </para>
/// </remarks>
public sealed class DataverseDescribeHandlerTests : TypedToolHandlerTestFixture
{
    private readonly Mock<IDataverseUserClient> _dataverse = new();

    private DataverseDescribeHandler CreateHandler() =>
        new(_dataverse.Object, CreateLogger<DataverseDescribeHandler>());

    private static AnalysisTool BuildDescribeTool() =>
        BuildAnalysisTool(handlerClass: nameof(DataverseDescribeHandler), name: "SYS-Dataverse Describe");

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests (HandlerContractTestTemplate, retargeted)
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
            typeof(DataverseDescribeHandler),
            because: "the handler type must be auto-discovered by the assembly scan (ADR-010: no manual DI lines per handler)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        CreateHandler().HandlerId.Should().Be(
            nameof(DataverseDescribeHandler),
            because: "HandlerId == nameof(handler class) routes sprk_handlerclass at runtime");
    }

    [Fact]
    public void Metadata_IsValid()
    {
        var metadata = CreateHandler().Metadata;
        metadata.Should().NotBeNull();
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
    // Playbook-context rejection (chat-only tool)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_InPlaybookContext_Rejects()
    {
        var result = CreateHandler().Validate(BuildToolExecutionContext(), BuildDescribeTool());
        result.IsValid.Should().BeFalse(because: "dataverse.describe is an agent-loop (chat) tool");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Argument validation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_MissingPath_Fails()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var result = CreateHandler().ValidateChat(ctx, BuildDescribeTool());
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainMatch("*'path'*");
    }

    [Fact]
    public void ValidateChat_MissingTenantId_Fails()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"path":"tables/"}""") with { TenantId = "" };
        var result = CreateHandler().ValidateChat(ctx, BuildDescribeTool());
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteChatAsync_GaOnlyPathSegment_ReturnsStructuredValidationError()
    {
        // skills/ + scopes/ exist on the GA MCP transport only — the native handler must
        // refuse with a clear message, never guess.
        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"path":"skills/"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("MCP transport");
        _dataverse.VerifyNoOtherCalls();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // tables/ — table list under the user's metadata visibility
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_TableListPath_ReturnsTablesWithGaStylePaths()
    {
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions?")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""
                {
                  "value": [
                    { "LogicalName": "account", "EntitySetName": "accounts", "IsPrivate": false,
                      "DisplayName": { "UserLocalizedLabel": { "Label": "Account" } } },
                    { "LogicalName": "sprk_matter", "EntitySetName": "sprk_matters", "IsPrivate": false,
                      "DisplayName": { "UserLocalizedLabel": { "Label": "Matter" } } },
                    { "LogicalName": "privatething", "EntitySetName": "privatethings", "IsPrivate": true }
                  ]
                }
                """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"path":"tables/"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.Data!.Value;
        data.GetProperty("kind").GetString().Should().Be("table_list");
        data.GetProperty("count").GetInt32().Should().Be(2, because: "IsPrivate tables are excluded");
        var paths = data.GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("path").GetString()).ToList();
        paths.Should().Contain("tables/account");
        paths.Should().Contain("tables/sprk_matter");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // tables/{t}/records/{guid} — record read under the USER's row-level security
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_RecordPath_ReturnsFieldsAndCitation()
    {
        var recordId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='account')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "accounts", "PrimaryIdAttribute": "accountid" }""")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"accounts({recordId:D})")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                {
                  "@odata.etag": "W/\"123\"",
                  "accountid": "{{recordId:D}}",
                  "name": "Contoso Legal Fixture",
                  "revenue": 125000.5
                }
                """)));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $$"""{"path":"tables/account/records/{{recordId:D}}"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.Data!.Value;
        data.GetProperty("kind").GetString().Should().Be("record");
        data.GetProperty("entity").GetString().Should().Be("account");
        data.GetProperty("fields").GetProperty("name").GetString().Should().Be("Contoso Legal Fixture");
        data.TryGetProperty("fields", out var fields).Should().BeTrue();
        fields.EnumerateObject().Select(p => p.Name).Should().NotContain(n => n.StartsWith("@odata"),
            because: "OData annotations are stripped from the LLM-facing payload");

        // ADR-039 grounding: adapter-level citation metadata present with the GA-style path.
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Citations);
        var citations = (IEnumerable<ToolResultCitation>)result.Metadata![ToolResultMetadataKeys.Citations]!;
        citations.Should().ContainSingle(c => c.ChunkId == $"tables/account/records/{recordId:D}" && c.SourceName == "account");
    }

    [Fact]
    public async Task ExecuteChatAsync_RecordUserCannotRead_SurfacesAccessDeniedNotEscalation()
    {
        // Test-user security context: Dataverse (via the user's OBO token) refuses the read.
        // The handler MUST surface the denial — never retry app-only (there is no app-only path).
        var recordId = Guid.NewGuid();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='account')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "accounts", "PrimaryIdAttribute": "accountid" }""")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("accounts(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied,
                "The user does not have read access to this record."));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $$"""{"path":"tables/account/records/{{recordId:D}}"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            because: "row-level security outcomes flow through unchanged — the user's context is the only context");
    }

    [Fact]
    public async Task ExecuteChatAsync_MissingUserContext_FailsClosed()
    {
        // Simulates the boundary refusing because no user bearer token exists on the request —
        // the MUST-rule fail-closed path (never app-only).
        _dataverse
            .Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired,
                "No bearer token on the current request; dataverse.* tools execute only under an authenticated user's session."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"path":"tables/"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 / NFR-07 telemetry — record content never appears in logs
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_NeverLogsRecordContent_Adr015()
    {
        const string sensitiveValue = "Contoso Confidential Settlement Amount 9,750,000";
        var recordId = Guid.NewGuid();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='account')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "accounts", "PrimaryIdAttribute": "accountid" }""")));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("accounts(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                { "accountid": "{{recordId:D}}", "name": "{{sensitiveValue}}" }
                """)));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $$"""{"path":"tables/account/records/{{recordId:D}}"}""");
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildDescribeTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        AssertTelemetryRespectsAdr015(sensitiveValue);
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
