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
    private static readonly Guid CallerSystemUserId = Guid.Parse("0a0a0a0a-0000-0000-0000-00000000c411");

    private readonly Mock<IDataverseUserClient> _dataverse = new();
    private readonly HttpContextAccessor _httpContextAccessor = new()
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) }
    };
    private readonly Mock<ICallerSystemUserResolver> _callerResolver = new();
    private readonly Mock<ISystemUserIdentityResolver> _identityResolver = new();
    private ICallerSystemUserResolver? _callerResolverOverride;

    public DataverseReadQueryHandlerTests()
    {
        // Default caller: a resolved INTERNAL systemuser (sprk_isexternal not true).
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
        _identityResolver
            .Setup(r => r.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private DataverseReadQueryHandler CreateHandler() =>
        new(_dataverse.Object, CreateLogger<DataverseReadQueryHandler>(), _httpContextAccessor,
            _callerResolverOverride ?? _callerResolver.Object, _identityResolver.Object);

    private void AsExternalCaller() =>
        _identityResolver
            .Setup(r => r.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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

    [Fact]
    public void Metadata_Description_CarriesLiteralDateGuidanceForRelativeDateFilters()
    {
        // Live UAT (2026-08-05): "how many overdue tasks" produced a query with an
        // unsupported GETDATE() call. The tool already told the model GETDATE() is
        // unsupported but never told it what to do INSTEAD — this pins the fix: the
        // description must point the model at the current-date value already injected
        // into its system context (SprkChatAgentFactory "## Current Date" directive)
        // and instruct it to compose a literal date predicate.
        var description = CreateHandler().Metadata.Description;
        description.Should().Contain("GETDATE()",
            because: "the unsupported-function name must still be named explicitly");
        description.Should().Contain("current date already given to you in your system context",
            because: "the model must be told to source the literal from its injected system context, not invent or ask for one");
        description.Should().ContainEquivalentOf("relative-date filters",
            because: "overdue/due-today/due-this-week are the concrete failure case from live UAT");
        description.Should().Contain("dynamic date functions are not supported");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ISS-010 (external-access r3 task 078) — external callers and communications
    // ═════════════════════════════════════════════════════════════════════════════

    // Review K1: the Open-thread half rides in the query through the thread navigation property (solution XML
    // sprk_CommunicationThread.xml; Open = 100000000 in the sprk_privacystate option set).
    private const string GuardClause =
        "sprk_isinternalonly eq false and _sprk_communicationthread_value ne null and sprk_CommunicationThread/sprk_privacystate eq 100000000";
    // Review K4: the thread and flag columns are both selected (and stripped) so both are re-checked after the read.
    private const string GuardedSelect = "sprk_communicationid,sprk_subject,_sprk_communicationthread_value,sprk_isinternalonly";
    private const string OpenState = "100000000";
    private const string PrivateState = "100000001";

    private void SetupCommunicationMetadata() =>
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_communication')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""
                {
                  "EntitySetName": "sprk_communications",
                  "PrimaryIdAttribute": "sprk_communicationid",
                  "Attributes": [
                    { "LogicalName": "sprk_subject", "AttributeType": "String" },
                    { "LogicalName": "sprk_body", "AttributeType": "Memo" },
                    { "LogicalName": "sprk_isinternalonly", "AttributeType": "Boolean" },
                    { "LogicalName": "sprk_communicationthread", "AttributeType": "Lookup" }
                  ]
                }
                """)));

    private List<string> CaptureCommunicationQueries(string rowsJson)
    {
        var captured = new List<string>();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications?")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => captured.Add(p))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(rowsJson)));
        return captured;
    }

    private List<string> CaptureThreadReads(string threadsJson)
    {
        var captured = new List<string>();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communicationthreads?")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => captured.Add(p))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(threadsJson)));
        return captured;
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnCommunicationWithOr_GuardIsAndedAroundWholePredicate()
    {
        AsExternalCaller();
        SetupCommunicationMetadata();
        var captured = CaptureCommunicationQueries("""{ "value": [] }""");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(
            "SELECT sprk_subject FROM sprk_communication WHERE sprk_subject = 'a' OR sprk_subject = 'b'"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        captured.Should().ContainSingle().Which.Should().Be(
            $"sprk_communications?$select={GuardedSelect}" +
            "&$filter=" + Uri.EscapeDataString($"(sprk_subject eq 'a' or sprk_subject eq 'b') and {GuardClause}") +
            "&$top=50",
            because: "the model's whole predicate is parenthesised and ANDed with flag-false + thread-set, so an OR cannot widen it");
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnCommunication_HidesRowsWhoseThreadIsNotOpen()
    {
        AsExternalCaller();
        SetupCommunicationMetadata();
        var openThread = Guid.Parse("70000000-0000-0000-0000-000000000001");
        var privateThread = Guid.Parse("70000000-0000-0000-0000-000000000002");
        var openRow = Guid.Parse("c0000000-0000-0000-0000-000000000001");
        var privateRow = Guid.Parse("c0000000-0000-0000-0000-000000000002");
        var noThreadRow = Guid.Parse("c0000000-0000-0000-0000-000000000003");
        // Dataverse has already applied the flag-false half (the query filter); these rows are the flag-false ones.
        var captured = CaptureCommunicationQueries($$"""
            {
              "value": [
                { "sprk_communicationid": "{{openRow:D}}", "sprk_subject": "Shared update", "_sprk_communicationthread_value": "{{openThread:D}}", "sprk_isinternalonly": false },
                { "sprk_communicationid": "{{privateRow:D}}", "sprk_subject": "Private thread item", "_sprk_communicationthread_value": "{{privateThread:D}}", "sprk_isinternalonly": false },
                { "sprk_communicationid": "{{noThreadRow:D}}", "sprk_subject": "No thread item", "_sprk_communicationthread_value": null, "sprk_isinternalonly": false }
              ]
            }
            """);
        var threadReads = CaptureThreadReads($$"""
            {
              "value": [
                { "sprk_communicationthreadid": "{{openThread:D}}", "sprk_privacystate": {{OpenState}} },
                { "sprk_communicationthreadid": "{{privateThread:D}}", "sprk_privacystate": {{PrivateState}} }
              ]
            }
            """);

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_subject FROM sprk_communication"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        captured.Should().ContainSingle().Which.Should().Be(
            $"sprk_communications?$select={GuardedSelect}" +
            "&$filter=" + Uri.EscapeDataString(GuardClause) + "&$top=50",
            because: "with no model WHERE the filter is the guard alone — true and null flags never leave Dataverse");
        threadReads.Should().ContainSingle().Which.Should().Be(
            "sprk_communicationthreads?$select=sprk_communicationthreadid,sprk_privacystate" +
            $"&$filter=sprk_communicationthreadid eq {openThread:D} or sprk_communicationthreadid eq {privateThread:D}&$top=2");

        var data = result.Data!.Value;
        data.GetProperty("rowCount").GetInt32().Should().Be(1);
        var row = data.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("sprk_communicationid").GetString().Should().Be(openRow.ToString("D"));
        row.TryGetProperty("sprk_communicationthread", out _).Should().BeFalse(
            because: "the thread column was selected only for the guard and is not returned to the model");
        row.TryGetProperty("_sprk_communicationthread_value", out _).Should().BeFalse();
        row.TryGetProperty("sprk_isinternalonly", out _).Should().BeFalse(
            because: "the flag column was selected only for the guard and is not returned to the model");
        ((IEnumerable<ToolResultCitation>)result.Metadata![ToolResultMetadataKeys.Citations]!)
            .Should().ContainSingle(c => c.ChunkId == $"tables/sprk_communication/records/{openRow:D}");
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerThreadPrivacyReadFails_ReturnsErrorWithNoRows()
    {
        AsExternalCaller();
        SetupCommunicationMetadata();
        CaptureCommunicationQueries($$"""
            { "value": [ { "sprk_communicationid": "{{Guid.NewGuid():D}}", "sprk_subject": "x", "_sprk_communicationthread_value": "{{Guid.NewGuid():D}}" } ] }
            """);
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communicationthreads?")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "Server error."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_subject FROM sprk_communication"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeFalse(because: "a thread-privacy fault can never turn into 'show it'");
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
    }

    [Theory]
    [InlineData("sprk_communicationthread")]
    [InlineData("sprk_communicationattachment")]
    [InlineData("sprk_communicationparticipant")]
    public async Task ExecuteChatAsync_ExternalCallerOnOtherCommunicationFamilyTable_RefusedWithNoDataverseCall(string table)
    {
        AsExternalCaller();

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args($"SELECT sprk_name FROM {table}"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        result.ErrorMessage.Should().Contain("sprk_communication table only");
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "the refusal comes before any Dataverse request, the metadata GET included");
    }

    [Fact]
    public async Task ExecuteChatAsync_NoRequestPrincipal_TreatedAsExternal()
    {
        _httpContextAccessor.HttpContext = null;
        // The real resolver: a missing principal is an honest Unresolved result.
        _callerResolverOverride = new CallerSystemUserResolver(_identityResolver.Object, CreateLogger<CallerSystemUserResolver>());

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_name FROM sprk_communicationthread"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied, because: "an unresolvable caller is external (fail closed)");
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_CallerSystemUserUnresolved_TreatedAsExternal()
    {
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-matching-systemuser"));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_name FROM sprk_communicationthread"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied, because: "an unresolvable caller is external (fail closed)");
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _identityResolver.Verify(r => r.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_InternalCallerOnCommunication_RequestUrlUnchangedAndNoThreadRead()
    {
        SetupCommunicationMetadata();
        var rowId = Guid.NewGuid();
        var captured = CaptureCommunicationQueries($$"""
            { "value": [ { "sprk_communicationid": "{{rowId:D}}", "sprk_subject": "Internal-only note" } ] }
            """);

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(
            "SELECT sprk_subject FROM sprk_communication WHERE sprk_subject = 'a' OR sprk_subject = 'b'"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        captured.Should().ContainSingle().Which.Should().Be(
            "sprk_communications?$select=sprk_communicationid,sprk_subject" +
            "&$filter=" + Uri.EscapeDataString("sprk_subject eq 'a' or sprk_subject eq 'b'") + "&$top=50",
            because: "an internal caller's query is byte-identical to the pre-ISS-010 translation");
        result.Data!.Value.GetProperty("rowCount").GetInt32().Should().Be(1);
        _dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communicationthreads?")), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_NonCommunicationTable_RequestUrlUnchangedAndExternalityNotResolved()
    {
        AsExternalCaller();
        SetupAccountMetadata();
        string? captured = null;
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("accounts?")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "value": [] }""")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT name FROM account WHERE name = 'x' OR name = 'y'"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        captured.Should().Be(
            "accounts?$select=accountid,name&$filter=" + Uri.EscapeDataString("name eq 'x' or name eq 'y'") + "&$top=50");
        _callerResolver.Verify(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()), Times.Never,
            "externality is decided only for a communication-family table");
    }

    [Fact]
    public async Task Telemetry_ExternalCommunicationGuard_LogsEntityAndReasonOnly_Adr015()
    {
        const string sensitiveLiteral = "Rothstein Confidential Matter 4711";
        const string sensitiveRowValue = "Settlement figure 8,400,000 privileged";
        AsExternalCaller();
        SetupCommunicationMetadata();
        CaptureCommunicationQueries($$"""
            { "value": [ { "sprk_communicationid": "{{Guid.NewGuid():D}}", "sprk_subject": "{{sensitiveRowValue}}", "_sprk_communicationthread_value": "{{Guid.NewGuid():D}}", "sprk_isinternalonly": false } ] }
            """);
        CaptureThreadReads("""{ "value": [] }""");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(
            $"SELECT sprk_subject FROM sprk_communication WHERE sprk_subject = '{sensitiveLiteral}'"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("rowCount").GetInt32().Should().Be(0);
        CapturedLogMessages.Should().Contain(m =>
            m.FormattedMessage.Contains(ExternalCallerCommunicationGuard.LogEvent) &&
            m.FormattedMessage.Contains("entity=sprk_communication") &&
            m.FormattedMessage.Contains("reason=thread_not_open"));
        AssertTelemetryRespectsAdr015(sensitiveLiteral, sensitiveRowValue);
    }

    // ── ISS-010 review round 1 ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnCommunication_RowWhoseFlagIsNotFalseHiddenAfterRead()
    {
        // Review K4: defence in depth — a row whose flag does not read explicitly false is hidden even if Dataverse
        // returned it (the query filter is not the only check).
        AsExternalCaller();
        SetupCommunicationMetadata();
        var openThread = Guid.Parse("70000000-0000-0000-0000-000000000011");
        var shareableRow = Guid.Parse("c0000000-0000-0000-0000-000000000011");
        var internalRow = Guid.Parse("c0000000-0000-0000-0000-000000000012");
        var unsetRow = Guid.Parse("c0000000-0000-0000-0000-000000000013");
        CaptureCommunicationQueries($$"""
            {
              "value": [
                { "sprk_communicationid": "{{shareableRow:D}}", "sprk_subject": "a", "_sprk_communicationthread_value": "{{openThread:D}}", "sprk_isinternalonly": false },
                { "sprk_communicationid": "{{internalRow:D}}", "sprk_subject": "b", "_sprk_communicationthread_value": "{{openThread:D}}", "sprk_isinternalonly": true },
                { "sprk_communicationid": "{{unsetRow:D}}", "sprk_subject": "c", "_sprk_communicationthread_value": "{{openThread:D}}", "sprk_isinternalonly": null }
              ]
            }
            """);
        CaptureThreadReads($$"""
            { "value": [ { "sprk_communicationthreadid": "{{openThread:D}}", "sprk_privacystate": {{OpenState}} } ] }
            """);

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_subject FROM sprk_communication"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("sprk_communicationid").GetString())
            .Should().Equal(shareableRow.ToString("D"));
    }

    [Theory]
    [InlineData("sprk_emailartifact")]
    [InlineData("sprk_attachmentartifact")]
    [InlineData("sprk_emailreviewlog")]
    [InlineData("sprk_affinity")]
    public async Task ExecuteChatAsync_ExternalCallerOnContentTableOutsidePrefix_RefusedWithNoDataverseCall(string table)
    {
        AsExternalCaller();

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args($"SELECT sprk_name FROM {table}"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        result.ErrorMessage.Should().Be(ExternalCallerCommunicationGuard.RefusalMessage);
        result.Data.Should().BeNull();
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "email content copied outside the sprk_communication prefix is refused before any Dataverse request");
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalityReadThrows_ErrorWithNoData()
    {
        // Review F2: a failing externality read is an error with no data, never "internal".
        _identityResolver
            .Setup(r => r.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));
        SetupCommunicationMetadata();
        CaptureCommunicationQueries("""{ "value": [ { "sprk_communicationid": "c0000000-0000-0000-0000-0000000000ff", "sprk_subject": "x" } ] }""");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_subject FROM sprk_communication"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Data.Should().BeNull();
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_CallerLookupFailed_ErrorWithNoDataverseRequest()
    {
        // Review F2: the resolver's swallowed fault ("lookup-failed") is an error, not "external" and not "internal".
        _callerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved(ExternalCallerCommunicationGuard.LookupFailedReason));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_subject FROM sprk_communication"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_RealResolverLookupFault_ErrorWithNoDataverseRequest()
    {
        // Pins the reason string against the real resolver: a Dataverse fault inside the oid → systemuser lookup.
        _httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString("D")) }, "test"))
        };
        _identityResolver
            .Setup(r => r.ResolveSystemUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));
        _callerResolverOverride = new CallerSystemUserResolver(_identityResolver.Object, CreateLogger<CallerSystemUserResolver>());

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_name FROM sprk_communicationthread"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError,
            because: "a lookup fault is not an external caller (a refusal) — it is an error with no data");
        result.Data.Should().BeNull();
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── sprk_document rows carrying a related communication (review round 1, F1) ──

    private static readonly Guid DocNoComm = Guid.Parse("d0000000-0000-0000-0000-000000000001");
    private static readonly Guid DocShared = Guid.Parse("d0000000-0000-0000-0000-000000000002");
    private static readonly Guid DocInternal = Guid.Parse("d0000000-0000-0000-0000-000000000003");
    private static readonly Guid DocPrivate = Guid.Parse("d0000000-0000-0000-0000-000000000004");
    private static readonly Guid DocNoThread = Guid.Parse("d0000000-0000-0000-0000-000000000005");
    private static readonly Guid DocUnreadable = Guid.Parse("d0000000-0000-0000-0000-000000000006");
    private static readonly Guid CommShared = Guid.Parse("c1000000-0000-0000-0000-000000000002");
    private static readonly Guid CommInternal = Guid.Parse("c1000000-0000-0000-0000-000000000003");
    private static readonly Guid CommPrivate = Guid.Parse("c1000000-0000-0000-0000-000000000004");
    private static readonly Guid CommNoThread = Guid.Parse("c1000000-0000-0000-0000-000000000005");
    private static readonly Guid CommUnreadable = Guid.Parse("c1000000-0000-0000-0000-000000000006");
    private static readonly Guid ThreadOpen = Guid.Parse("71000000-0000-0000-0000-000000000001");
    private static readonly Guid ThreadPrivate = Guid.Parse("71000000-0000-0000-0000-000000000002");

    private void SetupDocumentMetadata() =>
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_document')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""
                {
                  "EntitySetName": "sprk_documents",
                  "PrimaryIdAttribute": "sprk_documentid",
                  "Attributes": [
                    { "LogicalName": "sprk_documentname", "AttributeType": "String" },
                    { "LogicalName": "sprk_relatedcommunication", "AttributeType": "Lookup" }
                  ]
                }
                """)));

    private List<string> CaptureDocumentQueries(string rowsJson)
    {
        var captured = new List<string>();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_documents?")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => captured.Add(p))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(rowsJson)));
        return captured;
    }

    private static string DocRow(Guid id, Guid? relatedCommunication) =>
        $$"""{ "sprk_documentid": "{{id:D}}", "sprk_documentname": "Doc {{id:N}}", "_sprk_relatedcommunication_value": {{(relatedCommunication is { } c ? $"\"{c:D}\"" : "null")}} }""";

    private static string MixedDocumentRowsJson =>
        "{ \"value\": [ " + string.Join(", ",
            DocRow(DocNoComm, null), DocRow(DocShared, CommShared), DocRow(DocInternal, CommInternal),
            DocRow(DocPrivate, CommPrivate), DocRow(DocNoThread, CommNoThread), DocRow(DocUnreadable, CommUnreadable)) + " ] }";

    // CommUnreadable is deliberately absent (the caller cannot read it, or it was deleted).
    private static string RelatedCommunicationsJson => $$"""
        {
          "value": [
            { "sprk_communicationid": "{{CommShared:D}}", "sprk_isinternalonly": false, "_sprk_communicationthread_value": "{{ThreadOpen:D}}" },
            { "sprk_communicationid": "{{CommInternal:D}}", "sprk_isinternalonly": true, "_sprk_communicationthread_value": "{{ThreadOpen:D}}" },
            { "sprk_communicationid": "{{CommPrivate:D}}", "sprk_isinternalonly": false, "_sprk_communicationthread_value": "{{ThreadPrivate:D}}" },
            { "sprk_communicationid": "{{CommNoThread:D}}", "sprk_isinternalonly": false, "_sprk_communicationthread_value": null }
          ]
        }
        """;

    private static string RelatedThreadsJson => $$"""
        {
          "value": [
            { "sprk_communicationthreadid": "{{ThreadOpen:D}}", "sprk_privacystate": {{OpenState}} },
            { "sprk_communicationthreadid": "{{ThreadPrivate:D}}", "sprk_privacystate": {{PrivateState}} }
          ]
        }
        """;

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnDocument_HidesDocumentsWhoseRelatedCommunicationFailsRule()
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        var documentQueries = CaptureDocumentQueries(MixedDocumentRowsJson);
        var communicationReads = CaptureCommunicationQueries(RelatedCommunicationsJson);
        var threadReads = CaptureThreadReads(RelatedThreadsJson);

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        documentQueries.Should().ContainSingle().Which.Should().Be(
            "sprk_documents?$select=sprk_documentid,sprk_documentname,_sprk_relatedcommunication_value&$top=50",
            because: "the related-communication lookup is selected for the guard");
        communicationReads.Should().ContainSingle().Which.Should().Be(
            "sprk_communications?$select=sprk_communicationid,sprk_isinternalonly,_sprk_communicationthread_value" +
            $"&$filter=sprk_communicationid eq {CommShared:D} or sprk_communicationid eq {CommInternal:D} or " +
            $"sprk_communicationid eq {CommPrivate:D} or sprk_communicationid eq {CommNoThread:D} or " +
            $"sprk_communicationid eq {CommUnreadable:D}&$top=5",
            because: "ONE batched read of the related communications through the caller's own user client");
        threadReads.Should().ContainSingle().Which.Should().Be(
            "sprk_communicationthreads?$select=sprk_communicationthreadid,sprk_privacystate" +
            $"&$filter=sprk_communicationthreadid eq {ThreadOpen:D} or sprk_communicationthreadid eq {ThreadPrivate:D}&$top=2");

        var data = result.Data!.Value;
        data.GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("sprk_documentid").GetString())
            .Should().Equal(new[] { DocNoComm.ToString("D"), DocShared.ToString("D") },
                because: "a document with no related communication is unaffected; internal-only, Private-thread, " +
                         "no-thread and unreadable related communications all hide the document");
        data.GetProperty("rowCount").GetInt32().Should().Be(2);
        foreach (var row in data.GetProperty("rows").EnumerateArray())
        {
            row.TryGetProperty("sprk_relatedcommunication", out _).Should().BeFalse(
                because: "the lookup was selected only for the guard and is not returned to the model");
            row.TryGetProperty("_sprk_relatedcommunication_value", out _).Should().BeFalse();
        }
        ((IEnumerable<ToolResultCitation>)result.Metadata![ToolResultMetadataKeys.Citations]!)
            .Select(c => c.ChunkId).Should().Equal(
                $"tables/sprk_document/records/{DocNoComm:D}", $"tables/sprk_document/records/{DocShared:D}");
        CapturedLogMessages.Should().Contain(m =>
            m.FormattedMessage.Contains(ExternalCallerCommunicationGuard.LogEvent) &&
            m.FormattedMessage.Contains("entity=sprk_document") &&
            m.FormattedMessage.Contains("reason=related_communication_hidden"));
        AssertTelemetryRespectsAdr015($"Doc {DocInternal:N}");
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnDocumentWithNoRelatedCommunication_VisibleAndNoExtraRead()
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        CaptureDocumentQueries("{ \"value\": [ " + DocRow(DocNoComm, null) + " ] }");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("rowCount").GetInt32().Should().Be(1);
        _dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications?")), It.IsAny<CancellationToken>()), Times.Never);
        _dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communicationthreads?")), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnDocumentSelectingTheLookup_KeptAndNotDuplicated()
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        var documentQueries = CaptureDocumentQueries("{ \"value\": [ " + DocRow(DocShared, CommShared) + " ] }");
        CaptureCommunicationQueries(RelatedCommunicationsJson);
        CaptureThreadReads(RelatedThreadsJson);

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname, sprk_relatedcommunication FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        documentQueries.Should().ContainSingle().Which.Should().Be(
            "sprk_documents?$select=sprk_documentid,sprk_documentname,_sprk_relatedcommunication_value&$top=50");
        result.Data!.Value.GetProperty("rows").EnumerateArray().Single()
            .GetProperty("sprk_relatedcommunication").GetString().Should().Be(CommShared.ToString("D"),
                because: "a column the model asked for is never stripped");
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnDocumentRelatedCommunicationReadFails_ErrorWithNoData()
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        CaptureDocumentQueries("{ \"value\": [ " + DocRow(DocShared, CommShared) + " ] }");
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications?")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "Server error."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeFalse(because: "a related-communication fault can never turn into 'show it'");
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnDocument_RelatedCommunicationsSpanTwoChunks_RuleAppliedInEachChunk()
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        // 60 documents, each with its own communication: even index = shareable on an Open thread, odd = internal-only.
        var docs = Enumerable.Range(0, 60).Select(i => (Doc: new Guid(i + 1, 0xD0C, 0, new byte[8]), Comm: new Guid(i + 1, 0xC0, 0, new byte[8]))).ToList();
        CaptureDocumentQueries("{ \"value\": [ " + string.Join(", ", docs.Select(d => DocRow(d.Doc, d.Comm))) + " ] }");
        var communicationReads = new List<string>();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications?")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => communicationReads.Add(p))
            .ReturnsAsync((string path, CancellationToken _) => DataverseUserResponse.Ok(200, ParseJson(
                "{ \"value\": [ " + string.Join(", ", docs
                    .Select((d, i) => (d.Comm, Shareable: i % 2 == 0))
                    .Where(c => path.Contains(c.Comm.ToString("D")))
                    .Select(c => $$"""{ "sprk_communicationid": "{{c.Comm:D}}", "sprk_isinternalonly": {{(c.Shareable ? "false" : "true")}}, "_sprk_communicationthread_value": "{{ThreadOpen:D}}" }""")) +
                " ] }")));
        CaptureThreadReads($$"""{ "value": [ { "sprk_communicationthreadid": "{{ThreadOpen:D}}", "sprk_privacystate": {{OpenState}} } ] }""");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT TOP 100 sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        communicationReads.Should().HaveCount(2, because: "60 related communications are read in chunks of 50");
        communicationReads[0].Should().EndWith("&$top=50");
        communicationReads[1].Should().EndWith("&$top=10");
        communicationReads[1].Should().Contain(docs[50].Comm.ToString("D")).And.NotContain(docs[49].Comm.ToString("D"));
        result.Data!.Value.GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("sprk_documentid").GetString())
            .Should().Equal(docs.Where((_, i) => i % 2 == 0).Select(d => d.Doc.ToString("D")),
                because: "the rule holds on both sides of the chunk boundary (48 and 50 visible, 49 and 51 hidden)");
    }

    [Theory]
    [InlineData("\"not-a-guid\"")]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("42")]
    public async Task ExecuteChatAsync_ExternalCallerOnDocumentWithUnreadableRelatedCommunicationLink_Hidden(string rawLink)
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        CaptureDocumentQueries("{ \"value\": [ " + DocRow(DocNoComm, null) + ", " +
            $$"""{ "sprk_documentid": "{{DocShared:D}}", "sprk_documentname": "Doc", "_sprk_relatedcommunication_value": {{rawLink}} }""" + " ] }");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("sprk_documentid").GetString())
            .Should().Equal(new[] { DocNoComm.ToString("D") },
                because: "a link that is present but not a usable id is never read as 'no related communication' (fail closed)");
        _dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications?")), It.IsAny<CancellationToken>()), Times.Never,
            "an unreadable link is never sent to Dataverse as an id");
    }

    [Fact]
    public async Task ExecuteChatAsync_InternalCallerOnDocument_RequestUrlUnchangedAndNoExtraRead()
    {
        SetupDocumentMetadata();
        var documentQueries = CaptureDocumentQueries("{ \"value\": [ " + DocRow(DocInternal, CommInternal) + " ] }");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(
            "SELECT sprk_documentname FROM sprk_document WHERE sprk_documentname = 'a' OR sprk_documentname = 'b'"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        documentQueries.Should().ContainSingle().Which.Should().Be(
            "sprk_documents?$select=sprk_documentid,sprk_documentname" +
            "&$filter=" + Uri.EscapeDataString("sprk_documentname eq 'a' or sprk_documentname eq 'b'") + "&$top=50",
            because: "an internal caller's query is byte-identical to the pre-ISS-010 translation");
        result.Data!.Value.GetProperty("rowCount").GetInt32().Should().Be(1,
            because: "an internal caller sees a document whose related communication is internal-only");
        _dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications?")), It.IsAny<CancellationToken>()), Times.Never);
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
