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
/// Unit tests for <see cref="GridOverviewHandler"/> — the ONE parameterized configId-driven
/// overview tool (spaarkeai-assistant-enhancements-r3 task 020, FR-06 the overview DoD driver).
/// </summary>
/// <remarks>
/// <para>
/// Behavior is exercised through the handler's PUBLIC surface (ValidateChat for rejections;
/// ExecuteChatAsync + the mocked <see cref="IDataverseUserClient"/> boundary for the OBO round-trips).
/// The mocked boundary simulates the caller's Dataverse security context — a table the user cannot
/// read 403s; no user context fails closed. <see cref="FakeTimeProvider"/> pins the server date so
/// the today-injection assertions are deterministic (never <c>DateTime.UtcNow</c>).
/// </para>
/// </remarks>
public sealed class GridOverviewHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-08-10T09:00:00Z");
    private static readonly Guid ConfigId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IDataverseUserClient> _dataverse = new();
    private readonly TimeProvider _clock = new FixedTimeProvider(FixedNow);

    /// <summary>Minimal deterministic <see cref="TimeProvider"/> (server today is fixed; never DateTime.UtcNow).</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private GridOverviewHandler CreateHandler() =>
        new(_dataverse.Object, _clock, CreateLogger<GridOverviewHandler>());

    private static AnalysisTool BuildTool() =>
        BuildAnalysisTool(handlerClass: nameof(GridOverviewHandler), name: "SYS-Grid Overview");

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string Args(string configId) =>
        JsonSerializer.Serialize(new { configId });

    /// <summary>The "My Tasks / overdue" saved view: overdue = dueDate &lt; today (server-injected).</summary>
    private const string OverdueTasksFetchXml =
        "<fetch><entity name=\"sprk_task\">" +
        "<attribute name=\"sprk_taskid\" /><attribute name=\"sprk_subject\" /><attribute name=\"sprk_duedate\" />" +
        "<filter><condition attribute=\"sprk_duedate\" operator=\"lt\" value=\"{{today}}\" />" +
        "<condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter>" +
        "</entity></fetch>";

    // The grid config carries its query inside sprk_configjson's `source` block — NOT a
    // top-level sprk_fetchxml column, which does not exist on sprk_gridconfiguration.
    // Corrected 2026-08-19: these tests still seeded the non-existent column after
    // fb06291c0 ("grid_overview reads sprk_configjson (savedquery/inline) not
    // non-existent sprk_fetchxml — P1 grounding (R4 UAT)") changed the handler, so every
    // test here failed with "has no configuration JSON". Shape is authoritative per
    // GridOverviewConfigSourceTests + GridOverviewHandler.ParseGridSource.
    private void SetupConfig(string fetchXml, string entity = "sprk_task", string name = "My Overdue Tasks") =>
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"sprk_gridconfigurations({ConfigId:D})")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson(JsonSerializer.Serialize(new
            {
                sprk_entitylogicalname = entity,
                sprk_configjson = JsonSerializer.Serialize(new
                {
                    _version = "1.0",
                    source = new { type = "inline", fetchXml }
                }),
                sprk_name = name
            }))));

    private void SetupTaskMetadata() =>
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_task')")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "EntitySetName": "sprk_tasks", "PrimaryIdAttribute": "sprk_taskid" }""")));

    // ═════════════════════════════════════════════════════════════════════════════
    // Contract / discovery
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
            .Should().Contain(typeof(GridOverviewHandler),
                because: "the handler must be auto-discovered by the assembly scan (ADR-010) — no per-grid handlers");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        CreateHandler().HandlerId.Should().Be(nameof(GridOverviewHandler));
    }

    [Fact]
    public void Metadata_IsValid_AndChatOnly()
    {
        var handler = CreateHandler();
        handler.Metadata.Name.Should().NotBeNullOrWhiteSpace();
        handler.Metadata.Description.Should().NotBeNullOrWhiteSpace();
        handler.Metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
        handler.SupportedInvocationContexts.Should().Be(InvocationContextKind.Chat);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Argument validation — configId is the closed-set contract; no model SQL is accepted
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_MissingConfigId_Rejected()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        CreateHandler().ValidateChat(ctx, BuildTool()).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateChat_NonGuidConfigId_Rejected()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args("not-a-guid"));
        var result = CreateHandler().ValidateChat(ctx, BuildTool());
        result.IsValid.Should().BeFalse();
        string.Join(" ", result.Errors).Should().ContainEquivalentOf("GUID");
    }

    [Fact]
    public void ValidateChat_ValidConfigId_Passes()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(ConfigId.ToString("D")));
        CreateHandler().ValidateChat(ctx, BuildTool()).IsValid.Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // (a) executes a configId's saved query server-side (reusing the DEFINITION, not model SQL)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_RunsTheConfigsSavedQueryServerSide_OverTheOboClient()
    {
        SetupConfig(OverdueTasksFetchXml);
        SetupTaskMetadata();
        string? capturedFetch = null;
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_tasks?fetchXml=")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => capturedFetch = p)
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "value": [] }""")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(ConfigId.ToString("D")));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        capturedFetch.Should().NotBeNull(because: "the saved query executes server-side via the Web API fetchXml parameter");
        var decoded = Uri.UnescapeDataString(capturedFetch!);
        decoded.Should().Contain("<entity name=\"sprk_task\">",
            because: "it runs the grid's OWN saved FetchXML — never a model-authored query");
        // Config resolution + execution both routed through the OBO user client (no app-only path).
        _dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith($"sprk_gridconfigurations({ConfigId:D})")), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // (b) today is injected SERVER-SIDE (resolves against server today, not a client value)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_InjectsServerToday_IntoRelativeDatePredicate()
    {
        SetupConfig(OverdueTasksFetchXml);
        SetupTaskMetadata();
        string? capturedFetch = null;
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_tasks?fetchXml=")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => capturedFetch = p)
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""{ "value": [] }""")));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(ConfigId.ToString("D")));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var decoded = Uri.UnescapeDataString(capturedFetch!);
        decoded.Should().Contain("value=\"2026-08-10\"",
            because: "the server date from the injected clock replaces {{today}} — not a client-supplied date");
        decoded.Should().NotContain("{{today}}", because: "every placeholder must be substituted before execution");
        decoded.Should().Contain("returntotalrecordcount=\"true\"",
            because: "a non-aggregate saved view is executed with an accurate total-count request");
        result.Data!.Value.GetProperty("today").GetString().Should().Be("2026-08-10",
            because: "the injected server date is surfaced for transparency");
    }

    [Fact]
    public void InjectToday_SupportsSignedDayOffsets_ForRangePredicates()
    {
        var today = new DateOnly(2026, 8, 10);
        var (plus, injectedPlus) = GridOverviewHandler.InjectToday("value=\"{{today+7}}\"", today);
        injectedPlus.Should().BeTrue();
        plus.Should().Be("value=\"2026-08-17\"");

        var (minus, _) = GridOverviewHandler.InjectToday("value=\"{{ today - 1 }}\"", today);
        minus.Should().Be("value=\"2026-08-09\"", because: "whitespace-tolerant negative offsets resolve too");

        var (none, injectedNone) = GridOverviewHandler.InjectToday("value=\"2026-01-01\"", today);
        injectedNone.Should().BeFalse(because: "a query with no placeholder is left untouched");
        none.Should().Be("value=\"2026-01-01\"");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // (c) returns counts/rows suitable for narration
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsAccurateCountAndRows_ForNarration()
    {
        SetupConfig(OverdueTasksFetchXml);
        SetupTaskMetadata();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_tasks?fetchXml=")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                {
                  "@Microsoft.Dynamics.CRM.totalrecordcount": 3,
                  "@Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded": false,
                  "value": [
                    { "sprk_taskid": "{{Guid.NewGuid():D}}", "sprk_subject": "File motion" },
                    { "sprk_taskid": "{{Guid.NewGuid():D}}", "sprk_subject": "Review NDA" },
                    { "sprk_taskid": "{{Guid.NewGuid():D}}", "sprk_subject": "Call client" }
                  ]
                }
                """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(ConfigId.ToString("D")));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("count").GetInt64().Should().Be(3, because: "the accurate total comes from returntotalrecordcount");
        data.GetProperty("rowCount").GetInt32().Should().Be(3);
        data.GetProperty("entity").GetString().Should().Be("sprk_task");
        var rows = data.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().HaveCount(3);
        rows[0].GetProperty("sprk_subject").GetString().Should().Be("File motion");
        result.Summary.Should().Contain("3", because: "the model narrates the count from the summary/data");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // (e) the "overdue tasks" DoD shape that generic read_query rejects now works here
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_OverdueTasksDoD_AnswersWithCount_NoQueryError()
    {
        // Live UAT defect (R2): "how many overdue tasks do I have?" made the model author
        // WHERE createdon > GETUTCDATE() → read_query rejects GETDATE()/COUNT → query error +
        // duplicate tab. This tool answers it by running the grid's OWN overdue saved query
        // with today injected — no GETDATE(), no model SQL, no error.
        SetupConfig(OverdueTasksFetchXml, name: "My Overdue Tasks");
        SetupTaskMetadata();
        string? capturedFetch = null;
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_tasks?fetchXml=")), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => capturedFetch = p)
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson("""
                { "@Microsoft.Dynamics.CRM.totalrecordcount": 5, "value": [] }
                """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(ConfigId.ToString("D")));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeTrue(because: "no query error is produced (no GETDATE()/aggregate SQL)");
        result.Data!.Value.GetProperty("count").GetInt64().Should().Be(5);
        var decoded = Uri.UnescapeDataString(capturedFetch!);
        decoded.Should().NotContainEquivalentOf("getdate", because: "the derived overdue predicate is a literal server date, never GETDATE()");
        decoded.Should().Contain("operator=\"lt\"").And.Contain("value=\"2026-08-10\"");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // record-id citations (ADR-015 grounding)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_RowsCarryRecordIdCitations()
    {
        var id1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var id2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
        SetupConfig(OverdueTasksFetchXml);
        SetupTaskMetadata();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_tasks?fetchXml=")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                {
                  "value": [
                    { "sprk_taskid": "{{id1:D}}", "sprk_subject": "Alpha" },
                    { "sprk_taskid": "{{id2:D}}", "sprk_subject": "Beta" }
                  ]
                }
                """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(ConfigId.ToString("D")));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var rows = result.Data!.Value.GetProperty("rows").EnumerateArray().ToList();
        rows[0].GetProperty("@citation.path").GetString().Should().Be($"tables/sprk_task/records/{id1:D}");

        result.Metadata.Should().ContainKey(ToolResultMetadataKeys.Citations);
        var citations = ((IEnumerable<ToolResultCitation>)result.Metadata![ToolResultMetadataKeys.Citations]!).ToList();
        citations.Should().HaveCount(2);
        citations.Select(c => c.ChunkId).Should().Contain($"tables/sprk_task/records/{id2:D}");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // (d) OBO / user-scoping is honored — denials flow through, never app-only fallback
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_TableUserCannotSee_SurfacesAccessOutcome()
    {
        SetupConfig(OverdueTasksFetchXml);
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied,
                "Principal user is missing prvRead privilege."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(ConfigId.ToString("D")));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            because: "the query runs under the caller's OBO identity — row-level denials flow through");
    }

    [Fact]
    public async Task ExecuteChatAsync_MissingUserContext_FailsClosed()
    {
        _dataverse
            .Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired,
                "No bearer token on the current request."));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(ConfigId.ToString("D")));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired,
            because: "no user context means no Dataverse call — never an app-only fallback (ADR-028/ADR-015)");
    }

    [Fact]
    public async Task ExecuteChatAsync_ConfigWithoutSavedQuery_ReturnsValidationError()
    {
        SetupConfig(fetchXml: "");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(ConfigId.ToString("D")));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 / NFR-07 — row content + FetchXML body never logged
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_NeverLogsRowContentOrFetchXml_Adr015()
    {
        const string sensitiveRowValue = "Settlement figure 8,400,000 privileged";
        SetupConfig(OverdueTasksFetchXml);
        SetupTaskMetadata();
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_tasks?fetchXml=")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, ParseJson($$"""
                { "value": [ { "sprk_taskid": "{{Guid.NewGuid():D}}", "sprk_subject": "{{sensitiveRowValue}}" } ] }
                """)));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: Args(ConfigId.ToString("D")));
        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildTool(), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        AssertTelemetryRespectsAdr015(sensitiveRowValue);
    }
}
