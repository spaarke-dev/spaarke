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
/// Unit tests for <see cref="DataverseReadQueryHandler"/> + the GA read_query SQL dialect
/// (spaarke-ai-architecture-redesign-r1 task 008, FR-P0-07 read half).
/// </summary>
/// <remarks>
/// <para>
/// Dialect behavior is exercised through the handler's PUBLIC surface (ValidateChat for
/// rejections; ExecuteChatAsync + the mocked <see cref="IDataverseUserClient"/> boundary for
/// OData generation) — no internal-method testing (ADR-038 B8). The mocked boundary simulates
/// the test USER's security context: rows Dataverse withholds from the user simply don't
/// appear; denied tables return 403.
/// </para>
/// </remarks>
public sealed class DataverseReadQueryHandlerTests : TypedToolHandlerTestFixture
{
    private readonly Mock<IDataverseUserClient> _dataverse = new();

    private DataverseReadQueryHandler CreateHandler() =>
        new(_dataverse.Object, CreateLogger<DataverseReadQueryHandler>());

    private static AnalysisTool BuildReadQueryTool() =>
        BuildAnalysisTool(handlerClass: nameof(DataverseReadQueryHandler), name: "SYS-Dataverse Read Query");

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string Args(string querytext) =>
        JsonSerializer.Serialize(new { querytext });

    private void SetupAccountMetadata() =>
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='account')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "accounts", "PrimaryIdAttribute": "accountid" }""")));

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
            .Should().Contain(typeof(DataverseReadQueryHandler),
                because: "the handler type must be auto-discovered by the assembly scan (ADR-010)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        CreateHandler().HandlerId.Should().Be(nameof(DataverseReadQueryHandler));
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
    // Read-only enforcement (side_effect_class=read is mechanical, not declarative)
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("INSERT INTO account (name) VALUES ('x')")]
    [InlineData("UPDATE account SET name = 'x'")]
    [InlineData("DELETE FROM account WHERE name = 'x'")]
    [InlineData("DROP TABLE account")]
    public void ValidateChat_MutationStatements_RejectedAsReadOnly(string querytext)
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(querytext));
        var result = CreateHandler().ValidateChat(ctx, BuildReadQueryTool());

        result.IsValid.Should().BeFalse(because: "dataverse.read_query is strictly read-only");
        result.Errors.Should().ContainMatch("*read-only*");
    }

    [Fact]
    public void ValidateChat_StatementBatching_Rejected()
    {
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: Args("SELECT name FROM account; DELETE FROM account"));
        CreateHandler().ValidateChat(ctx, BuildReadQueryTool()).IsValid.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GA dialect rejections — LLM-actionable messages
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("SELECT * FROM account", "explicit column")]
    [InlineData("SELECT a.name FROM account a JOIN contact c ON a.accountid = c.parentcustomerid", "JOIN")]
    [InlineData("SELECT name, COUNT(accountid) FROM account GROUP BY name", "aggregate")]
    [InlineData("SELECT DISTINCT name FROM account", "DISTINCT")]
    [InlineData("SELECT name FROM account WHERE createdon > GETUTCDATE()", "GETDATE")]
    public void ValidateChat_UnsupportedDialectFeatures_RejectedWithActionableMessage(string querytext, string expectedTopic)
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(querytext));
        var result = CreateHandler().ValidateChat(ctx, BuildReadQueryTool());

        result.IsValid.Should().BeFalse();
        string.Join(" ", result.Errors).Should().ContainEquivalentOf(expectedTopic);
    }

    [Fact]
    public void ValidateChat_SupportedQuery_Passes()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(
            "SELECT TOP 5 name, revenue FROM account WHERE (revenue >= 1000 AND name LIKE 'Con%') OR statecode = 0 ORDER BY name DESC"));
        var result = CreateHandler().ValidateChat(ctx, BuildReadQueryTool());
        result.IsValid.Should().BeTrue(result.Errors.FirstOrDefault());
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // OData generation — observed at the mocked user-OBO boundary
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_TranslatesSqlToODataAndInjectsPrimaryId()
    {
        SetupAccountMetadata();

        string? capturedQuery = null;
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("accounts?")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => capturedQuery = p)
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "value": [] }""")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(
            "SELECT TOP 3 name FROM account WHERE name LIKE '%legal%' ORDER BY name"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        capturedQuery.Should().NotBeNull();
        capturedQuery.Should().StartWith("accounts?$select=accountid,name",
            because: "the primary id is injected for citability (ADR-039 grounding)");
        Uri.UnescapeDataString(capturedQuery!).Should().Contain("contains(name,'legal')");
        capturedQuery.Should().EndWith("$top=3");
        result.Warnings.Should().ContainMatch("*accountid*",
            because: "the auto-injected primary id is disclosed to the model");
    }

    [Fact]
    public async Task ExecuteChatAsync_RowsCarryCitationPathsAndMetadata()
    {
        var id1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var id2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
        SetupAccountMetadata();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("accounts?")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                {
                  "value": [
                    { "@odata.etag": "W/\"1\"", "accountid": "{{id1:D}}", "name": "Alpha" },
                    { "@odata.etag": "W/\"2\"", "accountid": "{{id2:D}}", "name": "Beta" }
                  ]
                }
                """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT name FROM account"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("rowCount").GetInt32().Should().Be(2);
        var rows = data.GetProperty("rows").EnumerateArray().ToList();
        rows[0].GetProperty("@citation.path").GetString().Should().Be($"tables/account/records/{id1:D}");
        rows[1].GetProperty("@citation.path").GetString().Should().Be($"tables/account/records/{id2:D}");

        result.Metadata.Should().ContainKey(ToolResultMetadataKeys.Citations);
        var citations = ((IEnumerable<ToolResultCitation>)result.Metadata![ToolResultMetadataKeys.Citations]!).ToList();
        citations.Should().HaveCount(2);
        citations.Select(c => c.ChunkId).Should().Contain($"tables/account/records/{id1:D}");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // User security context — denials flow through, never escalate
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_TableUserCannotSee_SurfacesAccessOutcome()
    {
        // The user's OBO token cannot read this table's metadata → Dataverse 404s/403s.
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied,
                "Principal user is missing prvRead privilege."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT name FROM account"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
    }

    [Fact]
    public async Task ExecuteChatAsync_MissingUserContext_FailsClosed()
    {
        _dataverse
            .Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired,
                "No bearer token on the current request."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT name FROM account"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired,
            because: "no user context means no Dataverse call — never an app-only fallback");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 / NFR-07 telemetry — querytext literals + row content never logged
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_NeverLogsQuerytextLiteralsOrRowContent_Adr015()
    {
        const string sensitiveLiteral = "Rothstein Confidential Matter 4711";
        const string sensitiveRowValue = "Settlement figure 8,400,000 privileged";
        var id = Guid.NewGuid();
        SetupAccountMetadata();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("accounts?")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                { "value": [ { "accountid": "{{id:D}}", "name": "{{sensitiveRowValue}}" } ] }
                """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(
            $"SELECT name FROM account WHERE name = '{sensitiveLiteral}'"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        AssertTelemetryRespectsAdr015(sensitiveLiteral, sensitiveRowValue);
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
