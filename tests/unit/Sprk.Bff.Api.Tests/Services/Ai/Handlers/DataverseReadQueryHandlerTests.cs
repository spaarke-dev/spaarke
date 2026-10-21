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

    /// <summary>Task 188: what the BFF reads APP-ONLY when it decides an external caller's documents.</summary>
    private readonly AppOnlyDocumentWorld _appOnly = new();

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
            _callerResolverOverride ?? _callerResolver.Object, _identityResolver.Object, _appOnly.Narrowing);

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

    // ── sprk_document rows for an external caller (review round 1 F1; task 188: THE predicate, decided APP-ONLY) ──
    //
    // The user's read (the mocked client) returns the rows; THE external document predicate re-reads them BY ID in the
    // app-only world (_appOnly) and decides there (main-session decision 2026-10-20). So the user's rows carry only what the
    // query selected, and a link field security masks for the user (null) never reads as "no link".

    private const int OpenPrivacy = 100000000;
    private const int PrivatePrivacy = 100000001;
    private const int RestrictedPermission = 100000002;
    private const int StandardPermission = 100000000;

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
                    { "LogicalName": "sprk_relatedcommunication", "AttributeType": "Lookup" },
                    { "LogicalName": "sprk_parentdocument", "AttributeType": "Lookup" }
                  ]
                }
                """)));

    /// <summary>The user's read of <c>sprk_documents</c>: answers <paramref name="rowsJson"/> and records the request.</summary>
    private List<string> CaptureDocumentQueries(string rowsJson)
    {
        var captured = new List<string>();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_documents?")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => captured.Add(p))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(rowsJson)));
        return captured;
    }

    /// <summary>A row of the user's read: the id, the name, and whatever else the test says the user's read returned.</summary>
    private static string DocRow(Guid id, string? extra = null) =>
        $$"""{ "sprk_documentid": "{{id:D}}", "sprk_documentname": "Doc {{id:N}}"{{(extra is null ? "" : ", " + extra)}} }""";

    private static string DocRows(params string[] rows) => "{ \"value\": [ " + string.Join(", ", rows) + " ] }";

    /// <summary>
    /// The app-only truth for the six mixed documents: no link; a shareable communication on an Open thread; an internal-only
    /// one; one on a Private thread; one with no thread; one the BFF cannot read back (CommUnreadable is not seeded).
    /// </summary>
    private void SeedMixedAppOnlyWorld()
    {
        _appOnly.Document(DocNoComm);
        _appOnly.Document(DocShared, communication: CommShared);
        _appOnly.Document(DocInternal, communication: CommInternal);
        _appOnly.Document(DocPrivate, communication: CommPrivate);
        _appOnly.Document(DocNoThread, communication: CommNoThread);
        _appOnly.Document(DocUnreadable, communication: CommUnreadable);
        _appOnly.Thread(ThreadOpen, OpenPrivacy);
        _appOnly.Thread(ThreadPrivate, PrivatePrivacy);
        _appOnly.Communication(CommShared, internalOnly: false, ThreadOpen);
        _appOnly.Communication(CommInternal, internalOnly: true, ThreadOpen);
        _appOnly.Communication(CommPrivate, internalOnly: false, ThreadPrivate);
        _appOnly.Communication(CommNoThread, internalOnly: false, thread: null);
    }

    private static IEnumerable<string?> RowIds(ToolResult result) =>
        result.Data!.Value.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("sprk_documentid").GetString());

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnDocument_HidesDocumentsWhoseRelatedCommunicationFailsRule_DecidedAppOnly()
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        SeedMixedAppOnlyWorld();
        var documentQueries = CaptureDocumentQueries(DocRows(
            DocRow(DocNoComm), DocRow(DocShared), DocRow(DocInternal), DocRow(DocPrivate), DocRow(DocNoThread), DocRow(DocUnreadable)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        documentQueries.Should().ContainSingle().Which.Should().Be(
            "sprk_documents?$select=sprk_documentid,sprk_documentname&$top=50",
            because: "task 188: the user's query selects nothing for the predicate; the rows are decided by id, app-only");
        _appOnly.Reads("sprk_documents").Should().ContainSingle("ONE batched by-id read of the page, app-only")
            .Which.Should().Contain(DocNoComm.ToString("D")).And.Contain(DocUnreadable.ToString("D"));
        _appOnly.Reads("sprk_communications").Should().ContainSingle("ONE batched read of the communications, app-only")
            .Which.Should().Contain(CommShared.ToString("D")).And.Contain(CommUnreadable.ToString("D"));
        _appOnly.Reads("sprk_communicationthreads").Should().ContainSingle();
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);

        RowIds(result).Should().Equal(new[] { DocNoComm.ToString("D"), DocShared.ToString("D") },
            because: "a document with no related communication is unaffected; internal-only, Private-thread, " +
                     "no-thread and unreadable related communications all hide the document");
        result.Data!.Value.GetProperty("rowCount").GetInt32().Should().Be(2);
        ((IEnumerable<ToolResultCitation>)result.Metadata![ToolResultMetadataKeys.Citations]!)
            .Select(c => c.ChunkId).Should().Equal(
                $"tables/sprk_document/records/{DocNoComm:D}", $"tables/sprk_document/records/{DocShared:D}");
        CapturedLogMessages.Should().Contain(m =>
            m.FormattedMessage.Contains(ExternalCallerCommunicationGuard.LogEvent) &&
            m.FormattedMessage.Contains("entity=sprk_document") &&
            m.FormattedMessage.Contains("reason=related_communication_hidden"));
        AssertTelemetryRespectsAdr015($"Doc {DocInternal:N}");
    }

    /// <summary>
    /// Task 188 round 4a (F1-5): the row-cap warning is measured on the RAW page Dataverse returned, not on what is left after
    /// the narrowing hid rows. A page of <paramref name="returned"/> rows under TOP 50, 3 of them hidden: the model must still be
    /// told the result hit the cap when the raw page did.
    /// </summary>
    [Theory(DisplayName = "Task 188 r4a F1-5: the row-cap warning counts the raw page, before hidden rows are removed")]
    [InlineData(50, true)]
    [InlineData(49, false)]
    public async Task ExecuteChatAsync_ExternalCaller_RowCapWarning_IsMeasuredBeforeTheNarrowing(int returned, bool capped)
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        var ids = Enumerable.Range(0, returned).Select(_ => Guid.NewGuid()).ToList();
        foreach (var (id, i) in ids.Select((id, i) => (id, i)))
        {
            _appOnly.Document(id, permission: i < 3 ? 100000002 : null);
        }
        CaptureDocumentQueries(DocRows(ids.Select(id => DocRow(id)).ToArray()));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT TOP 50 sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        RowIds(result).Should().HaveCount(returned - 3, "the three own-Restricted rows are hidden");
        if (capped)
        {
            result.Warnings.Should().Contain(w => w.Contains("reached the row cap (50)"),
                "the raw page hit TOP: hiding 3 rows must not make a capped result look complete");
        }
        else
        {
            result.Warnings.Should().NotContain(w => w.Contains("row cap"), "the raw page was under TOP");
        }
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnDocument_FieldSecurityMasksTheLinkForTheUser_AppOnlyReadsAnInternalOnlyCommunication_Hidden()
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        SeedMixedAppOnlyWorld();
        // The user's read returns the link as null (a column field security masks reads as null), on both rows.
        CaptureDocumentQueries(DocRows(
            DocRow(DocNoComm, "\"_sprk_relatedcommunication_value\": null"),
            DocRow(DocInternal, "\"_sprk_relatedcommunication_value\": null")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname, sprk_relatedcommunication FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        RowIds(result).Should().Equal(new[] { DocNoComm.ToString("D") },
            because: "the app-only read sees the internal-only communication the user's read could not: the document is hidden");
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnDocument_FieldSecurityMasksTheParentForTheUser_AppOnlyReadsARestrictedParent_Hidden()
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        var restrictedEml = _appOnly.Document(Guid.Parse("e1880000-0000-0000-0000-0000000000f1"), permission: RestrictedPermission);
        _appOnly.Document(DocNoComm);
        _appOnly.Document(DocShared, parent: restrictedEml);
        CaptureDocumentQueries(DocRows(
            DocRow(DocNoComm, "\"_sprk_parentdocument_value\": null"),
            DocRow(DocShared, "\"_sprk_parentdocument_value\": null")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname, sprk_parentdocument FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        RowIds(result).Should().Equal(new[] { DocNoComm.ToString("D") },
            because: "the app-only read sees the Restricted parent .eml the user's read could not: the attachment is hidden");
        _appOnly.Reads("sprk_documents").Should().HaveCount(2, "the page by id, then its parents");
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnDocumentWithNoRelatedCommunication_Visible_NoCommunicationRead()
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        _appOnly.Document(DocNoComm);
        CaptureDocumentQueries(DocRows(DocRow(DocNoComm)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("rowCount").GetInt32().Should().Be(1);
        _appOnly.Reads("sprk_documents").Should().ContainSingle();
        _appOnly.Reads("sprk_communications").Should().BeEmpty();
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnDocumentSelectingTheLookup_ReturnsTheUsersValue_NothingInjected()
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        SeedMixedAppOnlyWorld();
        var documentQueries = CaptureDocumentQueries(DocRows(DocRow(DocShared, $"\"_sprk_relatedcommunication_value\": \"{CommShared:D}\"")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname, sprk_relatedcommunication FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        documentQueries.Should().ContainSingle().Which.Should().Be(
            "sprk_documents?$select=sprk_documentid,sprk_documentname,_sprk_relatedcommunication_value&$top=50",
            because: "the user's query carries what the model asked for and nothing for the predicate");
        result.Data!.Value.GetProperty("rows").EnumerateArray().Single()
            .GetProperty("sprk_relatedcommunication").GetString().Should().Be(CommShared.ToString("D"),
                because: "a column the model asked for is returned as the user read it");
    }

    [Theory]
    [InlineData("sprk_documents")]
    [InlineData("sprk_communications")]
    [InlineData("sprk_communicationthreads")]
    public async Task ExecuteChatAsync_ExternalCallerOnDocument_AnAppOnlyReadFails_ErrorWithNoData(string faultedSet)
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        SeedMixedAppOnlyWorld();
        _appOnly.Faults[faultedSet] = System.Net.HttpStatusCode.ServiceUnavailable;
        CaptureDocumentQueries(DocRows(DocRow(DocShared)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeFalse(because: "a narrowing read fault can never turn into 'show it'");
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
        _appOnly.Thread(ThreadOpen, OpenPrivacy);
        foreach (var (d, i) in docs.Select((d, i) => (d, i)))
        {
            _appOnly.Document(d.Doc, communication: d.Comm);
            _appOnly.Communication(d.Comm, internalOnly: i % 2 != 0, ThreadOpen);
        }

        CaptureDocumentQueries(DocRows(docs.Select(d => DocRow(d.Doc)).ToArray()));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT TOP 100 sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var communicationReads = _appOnly.Reads("sprk_communications");
        communicationReads.Should().HaveCount(2, because: "60 related communications are read in chunks of 50");
        communicationReads.Should().ContainSingle(r => r.EndsWith("&$top=50")).And.ContainSingle(r => r.EndsWith("&$top=10"));
        communicationReads.Single(r => r.EndsWith("&$top=10")).Should().Contain(docs[50].Comm.ToString("D")).And.NotContain(docs[49].Comm.ToString("D"));
        _appOnly.Reads("sprk_documents").Should().HaveCount(2, "the 60 documents by id, in chunks of 50");
        RowIds(result).Should().Equal(docs.Where((_, i) => i % 2 == 0).Select(d => d.Doc.ToString("D")),
            because: "the rule holds on both sides of the chunk boundary (48 and 50 visible, 49 and 51 hidden)");
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData(42)]
    public async Task ExecuteChatAsync_ExternalCallerOnDocumentWithUnreadableRelatedCommunicationLink_Hidden(object rawLink)
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        _appOnly.Document(DocNoComm);
        _appOnly.Document(DocShared, rawCommunication: rawLink);
        CaptureDocumentQueries(DocRows(DocRow(DocNoComm), DocRow(DocShared)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        RowIds(result).Should().Equal(new[] { DocNoComm.ToString("D") },
            because: "a link that is present but not a usable id is never read as 'no related communication' (fail closed)");
        _appOnly.Reads("sprk_communications").Should().BeEmpty("an unreadable link is never sent to Dataverse as an id");
    }

    [Fact]
    public async Task ExecuteChatAsync_InternalCallerOnDocument_RequestUrlUnchangedAndNoExtraRead()
    {
        SetupDocumentMetadata();
        var documentQueries = CaptureDocumentQueries(DocRows(DocRow(DocInternal)));

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
        _appOnly.Calls.Should().Be(0, "an internal caller is not narrowed");
        _appOnly.AllReads.Should().BeEmpty();
        _dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications?")), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Task 188 (D7 T7, #2126): THE external document predicate in read_query ──

    private static readonly Guid DocOwnRestricted = Guid.Parse("d1880000-0000-0000-0000-000000000001");
    private static readonly Guid DocUnderRestrictedEml = Guid.Parse("d1880000-0000-0000-0000-000000000002");
    private static readonly Guid DocUnderInternalEml = Guid.Parse("d1880000-0000-0000-0000-000000000003");
    private static readonly Guid DocUnderStandardEml = Guid.Parse("d1880000-0000-0000-0000-000000000004");
    private static readonly Guid DocUnderRestrictedGrandparent = Guid.Parse("d1880000-0000-0000-0000-000000000005");
    private static readonly Guid DocOwnLinkUnderRestrictedEml = Guid.Parse("d1880000-0000-0000-0000-000000000006");
    private static readonly Guid EmlRestricted = Guid.Parse("e1880000-0000-0000-0000-000000000001");
    private static readonly Guid EmlInternal = Guid.Parse("e1880000-0000-0000-0000-000000000002");
    private static readonly Guid EmlStandard = Guid.Parse("e1880000-0000-0000-0000-000000000003");
    private static readonly Guid EmlUnderRestricted = Guid.Parse("e1880000-0000-0000-0000-000000000004");

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnDocument_Task188_OwnOrAnyAncestorRestrictedOrInternalOnlyParent_Hidden()
    {
        AsExternalCaller();
        SetupDocumentMetadata();
        SeedMixedAppOnlyWorld();
        _appOnly.Document(DocOwnRestricted, permission: RestrictedPermission);
        _appOnly.Document(DocUnderRestrictedEml, parent: EmlRestricted);
        _appOnly.Document(DocUnderInternalEml, parent: EmlInternal);
        _appOnly.Document(DocUnderStandardEml, parent: EmlStandard);
        _appOnly.Document(DocUnderRestrictedGrandparent, parent: EmlUnderRestricted);
        _appOnly.Document(DocOwnLinkUnderRestrictedEml, communication: CommShared, parent: EmlRestricted);
        _appOnly.Document(EmlRestricted, permission: RestrictedPermission);
        _appOnly.Document(EmlInternal, communication: CommInternal);
        _appOnly.Document(EmlStandard, permission: StandardPermission);
        _appOnly.Document(EmlUnderRestricted, parent: EmlRestricted);
        CaptureDocumentQueries(DocRows(
            DocRow(DocNoComm), DocRow(DocOwnRestricted), DocRow(DocUnderRestrictedEml), DocRow(DocUnderInternalEml),
            DocRow(DocUnderStandardEml), DocRow(DocUnderRestrictedGrandparent), DocRow(DocOwnLinkUnderRestrictedEml)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        RowIds(result).Should().Equal(new[] { DocNoComm.ToString("D"), DocUnderStandardEml.ToString("D") },
            because: "own Restricted, a Restricted parent or grandparent (even under the document's own visible link) and a " +
                     "parent .eml of an internal-only email hide the document; a Standard parent with no link does not");
        var documentReads = _appOnly.Reads("sprk_documents");
        documentReads.Should().HaveCount(2, "the page by id, then ONE batched read of the parents; the grandparent was among them");
        documentReads[1].Should().Contain(EmlRestricted.ToString("D")).And.Contain(EmlInternal.ToString("D"))
            .And.Contain(EmlStandard.ToString("D")).And.Contain(EmlUnderRestricted.ToString("D"));
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
    }

    [Fact]
    public async Task ExecuteChatAsync_InternalCallerOnDocument_Task188_OwnRestricted_StillVisible_NoExtraRead()
    {
        SetupDocumentMetadata();
        _appOnly.Document(DocOwnRestricted, permission: RestrictedPermission, parent: EmlRestricted);
        CaptureDocumentQueries(DocRows(DocRow(DocOwnRestricted)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_documentname FROM sprk_document"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("rowCount").GetInt32().Should().Be(1, "the narrowing is for external callers only");
        _appOnly.Calls.Should().Be(0);
    }

    // ── Task 188 round 3 slice C: rows that derive from a document (sprk_analysis, sprk_analysisoutput) ──

    private static readonly Guid AnOwnRestricted = Guid.Parse("a1880000-0000-0000-0000-000000000001");
    private static readonly Guid AnInternalOnly = Guid.Parse("a1880000-0000-0000-0000-000000000002");
    private static readonly Guid AnUndecidable = Guid.Parse("a1880000-0000-0000-0000-000000000003");
    private static readonly Guid AnVisible = Guid.Parse("a1880000-0000-0000-0000-000000000004");
    private static readonly Guid AnNoDocument = Guid.Parse("a1880000-0000-0000-0000-000000000005");
    private static readonly Guid AnUnreadableLink = Guid.Parse("a1880000-0000-0000-0000-000000000006");
    private static readonly Guid AnNotReturned = Guid.Parse("a1880000-0000-0000-0000-000000000007");
    private static readonly Guid AnOutputFileHidden = Guid.Parse("a1880000-0000-0000-0000-000000000008");
    private static readonly Guid AnOutputFileUndecidable = Guid.Parse("a1880000-0000-0000-0000-000000000009");
    private static readonly Guid AnOutputFileUnreadable = Guid.Parse("a1880000-0000-0000-0000-00000000000a");
    private static readonly Guid AnOutputFileVisible = Guid.Parse("a1880000-0000-0000-0000-00000000000b");
    private static readonly Guid DocNeverSeeded = Guid.Parse("d1880000-0000-0000-0000-0000000000ff");

    private static readonly Guid OutOfRestricted = Guid.Parse("0a180000-0000-0000-0000-000000000001");
    private static readonly Guid OutOfVisible = Guid.Parse("0a180000-0000-0000-0000-000000000002");
    private static readonly Guid OutNoAnalysis = Guid.Parse("0a180000-0000-0000-0000-000000000003");
    private static readonly Guid OutAnalysisNotReturned = Guid.Parse("0a180000-0000-0000-0000-000000000004");

    /// <summary>The source links the USER's client returns, by derived table: row id → raw lookup value (absent: not returned).</summary>
    private readonly Dictionary<string, Dictionary<Guid, object?>> _userSourceLinks = new()
    {
        ["sprk_analysises"] = new(),
        ["sprk_analysisoutputs"] = new(),
    };

    /// <summary>The OUTPUT FILE link (<c>_sprk_outputfileid_value</c>) the user's client returns for an analysis (absent: null).</summary>
    private readonly Dictionary<Guid, object?> _userOutputFiles = new();

    /// <summary>Task 188 r4b: the typed REGARDING document link (<c>_sprk_regardingdocument_value</c>) the user's client returns.</summary>
    private readonly Dictionary<Guid, object?> _userRegardingDocuments = new();

    private void SetupDerivedMetadata(string table, string set, string pk) =>
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"EntityDefinitions(LogicalName='{table}')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                {
                  "EntitySetName": "{{set}}",
                  "PrimaryIdAttribute": "{{pk}}",
                  "Attributes": [ { "LogicalName": "sprk_name", "AttributeType": "String" } ]
                }
                """)));

    /// <summary>
    /// The user's client: the model's query of <paramref name="set"/> answers <paramref name="rowsJson"/>; a source-link read
    /// (by id, <c>$select={pk},{links}</c>) answers from <see cref="_userSourceLinks"/> (the first link) and
    /// <see cref="_userOutputFiles"/> (an analysis's output file). Records every request.
    /// </summary>
    private List<string> CaptureDerivedReads(string set, string rowsJson)
    {
        var captured = new List<string>();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_analysis")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => captured.Add(p))
            .ReturnsAsync((string p, CancellationToken _) =>
            {
                var path = Uri.UnescapeDataString(p);
                var linkRead = System.Text.RegularExpressions.Regex.Match(path, @"^(\w+)\?\$select=(\w+),(_\w+_value(?:,_\w+_value)*)&\$filter=");
                if (!linkRead.Success)
                    return p.StartsWith(set + "?") ? DataverseUserResponse.Ok(200, ParseJson(rowsJson)) : DataverseUserResponse.Ok(200, ParseJson("""{ "value": [] }"""));
                var (linkSet, pk, links) = (linkRead.Groups[1].Value, linkRead.Groups[2].Value, linkRead.Groups[3].Value.Split(','));
                var rows = System.Text.RegularExpressions.Regex.Matches(path, $@"{pk} eq ([0-9a-f-]{{36}})")
                    .Select(m => Guid.Parse(m.Groups[1].Value))
                    .Where(_userSourceLinks[linkSet].ContainsKey)
                    .Select(id =>
                    {
                        var row = new Dictionary<string, object?> { [pk] = id.ToString("D"), [links[0]] = _userSourceLinks[linkSet][id] };
                        foreach (var link in links.Skip(1))
                            row[link] = link switch
                            {
                                "_sprk_outputfileid_value" => _userOutputFiles.GetValueOrDefault(id),
                                "_sprk_regardingdocument_value" => _userRegardingDocuments.GetValueOrDefault(id),
                                _ => null,
                            };
                        return row;
                    });
                return DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = rows }));
            });
        return captured;
    }

    /// <summary>
    /// Analyses (as the user reads their source link) over the app-only truth: own Restricted; under an internal-only email;
    /// a document the BFF cannot read back (never seeded); visible; no document; a link that is not a GUID; and one whose
    /// link read does not return it.
    /// </summary>
    private void SeedAnalyses()
    {
        SeedMixedAppOnlyWorld();
        _appOnly.Document(DocOwnRestricted, permission: RestrictedPermission);
        var links = _userSourceLinks["sprk_analysises"];
        links[AnOwnRestricted] = DocOwnRestricted.ToString("D");
        links[AnInternalOnly] = DocInternal.ToString("D");
        links[AnUndecidable] = DocNeverSeeded.ToString("D");
        links[AnVisible] = DocShared.ToString("D");
        links[AnNoDocument] = null;
        links[AnUnreadableLink] = "not-a-guid";
    }

    private static string AnalysisRow(Guid id) => $$"""{ "sprk_analysisid": "{{id:D}}", "sprk_name": "Analysis {{id:N}}" }""";

    private static IEnumerable<string?> AnalysisRowIds(ToolResult result, string pk = "sprk_analysisid") =>
        result.Data!.Value.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty(pk).GetString());

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnAnalysis_HidesRowsWhoseSourceDocumentIsHiddenOrUndecidable_KeepsTheRest()
    {
        AsExternalCaller();
        SetupDerivedMetadata("sprk_analysis", "sprk_analysises", "sprk_analysisid");
        SeedAnalyses();
        var reads = CaptureDerivedReads("sprk_analysises", DocRows(
            AnalysisRow(AnOwnRestricted), AnalysisRow(AnInternalOnly), AnalysisRow(AnUndecidable), AnalysisRow(AnVisible),
            AnalysisRow(AnNoDocument), AnalysisRow(AnUnreadableLink), AnalysisRow(AnNotReturned)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_name FROM sprk_analysis"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        AnalysisRowIds(result).Should().Equal(new[] { AnVisible.ToString("D"), AnNoDocument.ToString("D") },
            because: "an analysis of an own-Restricted document, of an internal-only email's document, of a document the BFF " +
                     "cannot decide, with an unreadable link, or whose link does not come back is hidden; a visible source and " +
                     "no source are unaffected");
        reads.Should().HaveCount(2, "the model's query, then ONE batched read of the page's source links as the user");
        reads[0].Should().Be("sprk_analysises?$select=sprk_analysisid,sprk_name&$top=50", "nothing is injected into the user's query");
        Uri.UnescapeDataString(reads[1]).Should().StartWith(
            "sprk_analysises?$select=sprk_analysisid,_sprk_documentid_value,_sprk_outputfileid_value,_sprk_regardingdocument_value&$filter=");
        _appOnly.Calls.Should().Be(1, "ONE hide-only narrowing call for the page");
        _appOnly.Reads("sprk_documents")[0].Should().Contain(DocOwnRestricted.ToString("D")).And.Contain(DocNeverSeeded.ToString("D"))
            .And.NotContain("not-a-guid");
        DocumentNarrowingReads.VerifyNoNarrowingReadAsTheUser(_dataverse);
        CapturedLogMessages.Should().Contain(m =>
            m.FormattedMessage.Contains(ExternalCallerCommunicationGuard.LogEvent) &&
            m.FormattedMessage.Contains("entity=sprk_analysis") &&
            m.FormattedMessage.Contains("source_document_hidden=3") &&
            m.FormattedMessage.Contains("source_document_undecidable=2"));
        AssertTelemetryRespectsAdr015(AnOwnRestricted.ToString("D"), DocOwnRestricted.ToString("D"), $"Analysis {AnOwnRestricted:N}");
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnAnalysis_HidesRowsWhoseOutputFileIsHiddenOrUndecidable_OdC1()
    {
        AsExternalCaller();
        SetupDerivedMetadata("sprk_analysis", "sprk_analysises", "sprk_analysisid");
        SeedAnalyses();
        var links = _userSourceLinks["sprk_analysises"];
        foreach (var id in new[] { AnOutputFileHidden, AnOutputFileUndecidable, AnOutputFileUnreadable })
            links[id] = DocShared.ToString("D");
        links[AnOutputFileVisible] = null;
        _userOutputFiles[AnOutputFileHidden] = DocOwnRestricted.ToString("D");
        _userOutputFiles[AnOutputFileUndecidable] = DocNeverSeeded.ToString("D");
        _userOutputFiles[AnOutputFileUnreadable] = "not-a-guid";
        _userOutputFiles[AnOutputFileVisible] = DocShared.ToString("D");
        CaptureDerivedReads("sprk_analysises", DocRows(
            AnalysisRow(AnVisible), AnalysisRow(AnOutputFileHidden), AnalysisRow(AnOutputFileUndecidable),
            AnalysisRow(AnOutputFileUnreadable), AnalysisRow(AnOutputFileVisible)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_name FROM sprk_analysis"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        AnalysisRowIds(result).Should().Equal(new[] { AnVisible.ToString("D"), AnOutputFileVisible.ToString("D") },
            because: "an analysis whose OUTPUT FILE is hidden, cannot be decided, or is not a GUID is hidden even when its " +
                     "source document is visible; a visible output file (and a null one) is unaffected");
        _appOnly.Calls.Should().Be(1, "source documents and output files are decided in ONE hide-only call");
        CapturedLogMessages.Should().Contain(m =>
            m.FormattedMessage.Contains("entity=sprk_analysis") &&
            m.FormattedMessage.Contains("source_document_hidden=2") &&
            m.FormattedMessage.Contains("source_document_undecidable=1"),
            "the Restricted and the never-readable output files are decided hidden; the non-GUID link is undecidable");
    }

    [Fact(DisplayName = "Task 188 r4b: read_query hides an analysis whose typed REGARDING document is hidden or undecidable")]
    public async Task ExecuteChatAsync_ExternalCallerOnAnalysis_HidesRowsWhoseRegardingDocumentIsHiddenOrUndecidable_Round4b()
    {
        AsExternalCaller();
        SetupDerivedMetadata("sprk_analysis", "sprk_analysises", "sprk_analysisid");
        SeedAnalyses();
        var links = _userSourceLinks["sprk_analysises"];
        foreach (var id in new[] { AnOutputFileHidden, AnOutputFileUndecidable, AnOutputFileVisible })
            links[id] = DocShared.ToString("D");
        _userRegardingDocuments[AnOutputFileHidden] = DocOwnRestricted.ToString("D");
        _userRegardingDocuments[AnOutputFileUndecidable] = DocNeverSeeded.ToString("D");
        _userRegardingDocuments[AnOutputFileVisible] = DocShared.ToString("D");
        CaptureDerivedReads("sprk_analysises", DocRows(
            AnalysisRow(AnOutputFileHidden), AnalysisRow(AnOutputFileUndecidable), AnalysisRow(AnOutputFileVisible)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_name FROM sprk_analysis"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        AnalysisRowIds(result).Should().Equal(new[] { AnOutputFileVisible.ToString("D") },
            because: "an analysis ABOUT a hidden or undecidable document is hidden even when its source document is visible");
        _appOnly.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnAnalysisOutput_HidesOutputsWhoseAnalysisSourceIsHidden_ThroughTheAnalysis()
    {
        AsExternalCaller();
        SetupDerivedMetadata("sprk_analysisoutput", "sprk_analysisoutputs", "sprk_analysisoutputid");
        SeedAnalyses();
        var outputs = _userSourceLinks["sprk_analysisoutputs"];
        outputs[OutOfRestricted] = AnOwnRestricted.ToString("D");
        outputs[OutOfVisible] = AnVisible.ToString("D");
        outputs[OutNoAnalysis] = null;
        outputs[OutAnalysisNotReturned] = AnNotReturned.ToString("D");
        var reads = CaptureDerivedReads("sprk_analysisoutputs", DocRows(
            $$"""{ "sprk_analysisoutputid": "{{OutOfRestricted:D}}" }""", $$"""{ "sprk_analysisoutputid": "{{OutOfVisible:D}}" }""",
            $$"""{ "sprk_analysisoutputid": "{{OutNoAnalysis:D}}" }""", $$"""{ "sprk_analysisoutputid": "{{OutAnalysisNotReturned:D}}" }"""));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_name FROM sprk_analysisoutput"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        AnalysisRowIds(result, "sprk_analysisoutputid").Should().Equal(new[] { OutOfVisible.ToString("D"), OutNoAnalysis.ToString("D") },
            because: "an output follows its analysis's source document; an analysis that does not come back cannot be decided");
        reads.Should().HaveCount(3, "the query, the outputs' analysis links, then the analyses' document links");
        _appOnly.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteChatAsync_InternalCallerOnAnalysis_Unchanged_NoSourceReadAndNoNarrowing()
    {
        SetupDerivedMetadata("sprk_analysis", "sprk_analysises", "sprk_analysisid");
        SeedAnalyses();
        var reads = CaptureDerivedReads("sprk_analysises", DocRows(AnalysisRow(AnOwnRestricted), AnalysisRow(AnInternalOnly)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_name FROM sprk_analysis"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("rowCount").GetInt32().Should().Be(2, "the narrowing is for external callers only");
        reads.Should().ContainSingle().Which.Should().Be("sprk_analysises?$select=sprk_analysisid,sprk_name&$top=50");
        _appOnly.Calls.Should().Be(0);
        _appOnly.AllReads.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnAnalysis_SourceLinkReadFails_ErrorWithNoData()
    {
        AsExternalCaller();
        SetupDerivedMetadata("sprk_analysis", "sprk_analysises", "sprk_analysisid");
        SeedAnalyses();
        CaptureDerivedReads("sprk_analysises", DocRows(AnalysisRow(AnVisible)));
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.Contains("_sprk_documentid_value")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "down"));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_name FROM sprk_analysis"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

        result.Success.Should().BeFalse("a source-link read fault never turns into 'show it'");
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteChatAsync_ExternalCallerOnAnalysis_AnAppOnlyReadFails_ErrorWithNoData()
    {
        AsExternalCaller();
        SetupDerivedMetadata("sprk_analysis", "sprk_analysises", "sprk_analysisid");
        SeedAnalyses();
        _appOnly.Faults["sprk_documents"] = System.Net.HttpStatusCode.ServiceUnavailable;
        CaptureDerivedReads("sprk_analysises", DocRows(AnalysisRow(AnVisible)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("SELECT sprk_name FROM sprk_analysis"));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildReadQueryTool(), CancellationToken.None);

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
