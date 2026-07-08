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
    // R5-C — LOOKUP column rewrite to `_{name}_value` (G-P3 UAT round-5, 2026-07-07)
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>Metadata mock INCLUDING attribute types (the R5-C $expand shape).</summary>
    private void SetupMatterMetadataWithAttributes() =>
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_matter')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""
                {
                  "EntitySetName": "sprk_matters",
                  "PrimaryIdAttribute": "sprk_matterid",
                  "Attributes": [
                    { "LogicalName": "sprk_mattername", "AttributeType": "String" },
                    { "LogicalName": "sprk_practicearea", "AttributeType": "Lookup" },
                    { "LogicalName": "sprk_mattertype", "AttributeType": "Lookup" },
                    { "LogicalName": "ownerid", "AttributeType": "Owner" },
                    { "LogicalName": "statecode", "AttributeType": "State" }
                  ]
                }
                """)));

    /// <summary>
    /// The exact round-5 operator failure: "distribution of matters by practice area" →
    /// SELECT sprk_practicearea FROM sprk_matter failed at the Web API because a LOOKUP
    /// column must be addressed as `_{name}_value`. The handler now rewrites it (and maps
    /// the response property back to the requested logical name).
    /// </summary>
    [Fact]
    public async Task ExecuteChatAsync_SelectingLookupColumn_RewritesToValueForm_AndMapsRowsBack()
    {
        SetupMatterMetadataWithAttributes();
        var id = Guid.NewGuid();
        var practiceAreaRef = Guid.NewGuid();
        string? capturedQuery = null;
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_matters?")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((path, _) => capturedQuery = path)
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                { "value": [ { "sprk_matterid": "{{id:D}}", "sprk_mattername": "Acme MSA", "_sprk_practicearea_value": "{{practiceAreaRef:D}}" } ] }
                """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(
            "SELECT sprk_mattername, sprk_practicearea FROM sprk_matter"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        capturedQuery.Should().Contain("_sprk_practicearea_value",
            because: "the Web API addresses lookup columns as _{name}_value — selecting the logical name 400s");
        capturedQuery.Should().NotContain("$select=sprk_matterid,sprk_mattername,sprk_practicearea&",
            because: "the raw lookup logical name must not remain in the $select");

        // The model asked for 'sprk_practicearea' — the row key maps BACK to it.
        var rowsJson = result.Data!.Value.GetProperty("rows");
        var firstRow = rowsJson.EnumerateArray().First();
        firstRow.GetProperty("sprk_practicearea").GetString().Should().Be(practiceAreaRef.ToString("D"));
        firstRow.TryGetProperty("_sprk_practicearea_value", out _).Should().BeFalse();

        // Transparency: the model learns the values are GUIDs of the referenced table.
        result.Warnings.Should().Contain(w => w.Contains("'sprk_practicearea'") && w.Contains("GUID"));
    }

    [Fact]
    public async Task ExecuteChatAsync_LookupColumnInWhereAndOrderBy_RewritesOutsideStringLiterals()
    {
        SetupMatterMetadataWithAttributes();
        string? capturedQuery = null;
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_matters?")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((path, _) => capturedQuery = path)
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "value": [] }""")));

        var refId = Guid.NewGuid().ToString("D");
        // The string literal deliberately CONTAINS the column name — it must NOT be rewritten.
        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(
            $"SELECT sprk_mattername FROM sprk_matter WHERE sprk_practicearea = '{refId}' AND sprk_mattername = 'about sprk_practicearea' ORDER BY sprk_mattertype DESC"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var decoded = Uri.UnescapeDataString(capturedQuery!);
        decoded.Should().Contain("_sprk_practicearea_value eq",
            because: "lookup references in $filter rewrite to the _value form");
        decoded.Should().Contain("'about sprk_practicearea'",
            because: "text INSIDE string literals must never be rewritten");
        decoded.Should().Contain("_sprk_mattertype_value desc",
            because: "lookup references in $orderby rewrite too");
    }

    [Fact]
    public void RewriteLookupReferences_QuoteSafety_AndFromValueForm_RoundTrip()
    {
        var lookups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_practicearea" };

        DataverseReadQueryHandler.RewriteLookupReferences(
                "sprk_practicearea eq 'x sprk_practicearea y' or sprk_name eq 'a'", lookups)
            .Should().Be("_sprk_practicearea_value eq 'x sprk_practicearea y' or sprk_name eq 'a'");

        DataverseReadQueryHandler.FromValueForm("_sprk_practicearea_value", lookups)
            .Should().Be("sprk_practicearea");
        DataverseReadQueryHandler.FromValueForm("_unknown_value", lookups)
            .Should().Be("_unknown_value", because: "only KNOWN lookup columns map back");
        DataverseReadQueryHandler.FromValueForm("sprk_name", lookups).Should().Be("sprk_name");
    }

    [Fact]
    public async Task ExecuteChatAsync_NoLookupColumnsReferenced_QueryUnchanged()
    {
        SetupMatterMetadataWithAttributes();
        string? capturedQuery = null;
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_matters?")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((path, _) => capturedQuery = path)
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "value": [] }""")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(
            "SELECT sprk_mattername FROM sprk_matter"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        capturedQuery.Should().Contain("$select=sprk_matterid,sprk_mattername",
            because: "non-lookup selections keep the pre-R5-C shape verbatim");
    }

    [Fact]
    public void Metadata_Description_CarriesAggregateYourselfAndLookupGuidance()
    {
        var description = CreateHandler().Metadata.Description;
        description.Should().Contain("aggregate the values YOURSELF",
            because: "GROUP BY is unsupported — distributions come from selecting rows and counting");
        description.Should().Contain("GUID",
            because: "lookup columns return referenced-record GUIDs, not labels");
        description.Should().Contain("dataverse.describe");
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
