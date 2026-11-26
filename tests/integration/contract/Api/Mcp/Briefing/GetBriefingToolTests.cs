// CONTRACT — spaarke_get_briefing on POST /mcp/legal-operations (spaarkeai-mcp-bff-api-r2 task 034; spec FR-09, FR-14, NFR-02, NFR-05; ADR-013,
// ADR-015, ADR-028). KEEP path: endpoint-contract.
//
// WHAT RUNS: the REAL BFF host (Program, through McpProgramHostFactory: production AddMcpModule, the A10 route filter, the per-tool policy, the
// production DailyBriefingEndpoints routes) over module-boundary doubles only: the coded composite (DailyBriefingCompositeService, whose own
// dispatch is DailyBriefingCompositeServiceTests') and the systemuser lookup (IGenericEntityService). The tool and POST
// /api/ai/daily-briefing/render are both called over HTTP with the same token, so "structuredContent equals the REST body" compares two real answers.
// NOT HERE: the composite's collection and narration; the widget (its jest suite); the MCP protocol suite (the mcp-conformance CI job).

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Briefing;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Narrators;
using Sprk.Bff.Api.Tests.Api.Mcp.Portfolio;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Briefing;

public sealed class GetBriefingToolTests : IClassFixture<BriefingProgramHostFactory>
{
    private const string RestRoute = "/api/ai/daily-briefing/render";

    private static readonly Guid OidA = Guid.Parse("0340aaaa-0000-4000-8000-000000000034");
    private static readonly Guid OidB = Guid.Parse("0340bbbb-0000-4000-8000-000000000034");
    private static readonly Guid OidNobody = Guid.Parse("0340cccc-0000-4000-8000-000000000034");

    private const string SecretMatter = "Confidential Matter Alpha";

    private readonly BriefingProgramHostFactory _factory;

    public GetBriefingToolTests(BriefingProgramHostFactory factory)
    {
        _factory = factory;
        _factory.Reset();
        _factory.Seed(OidA);
        _factory.Seed(OidB);
    }

    // ---- tools/list -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_GetBriefing_IsReadOnly_Idempotent_ModelAndAppVisible_WithTheBriefingWidget_AndAClosedSchema()
    {
        using var client = _factory.CreateClient();

        var tools = await McpCall.ListToolsAsync(client, BriefingProgramHostFactory.MintToken(OidA));

        var tool = tools[GetBriefingTool.Name];
        tool.GetProperty("title").GetString().Should().Be("Get my daily briefing");
        var description = tool.GetProperty("description").GetString()!;
        description.Length.Should().BeLessThanOrEqualTo(1024);
        description.Should().Contain("Use for:").And.Contain("Not for:");
        var annotations = tool.GetProperty("annotations");
        annotations.GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
        annotations.GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
        annotations.GetProperty("idempotentHint").GetBoolean().Should().BeTrue();
        annotations.GetProperty("openWorldHint").GetBoolean().Should().BeFalse();

        var ui = tool.GetProperty("_meta").GetProperty("ui");
        ui.GetProperty("resourceUri").GetString().Should().Be("ui://spaarke/briefing.html");
        ui.GetProperty("visibility").EnumerateArray().Select(v => v.GetString()).Should().BeEquivalentTo(["model", "app"]);

        var schema = tool.GetProperty("inputSchema");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        var properties = schema.GetProperty("properties");
        properties.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["dueWithinDays", "recencyHours"]);
        properties.GetProperty("dueWithinDays").GetProperty("maximum").GetInt32().Should().Be(31);
        properties.GetProperty("recencyHours").GetProperty("maximum").GetInt32().Should().Be(744);
    }

    // ---- parity with REST --------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetBriefing_StructuredContent_EqualsTheRestBody_ForTheSameCallerAndWindows_WhicheverIsCalledFirst(bool restFirst)
    {
        using var client = _factory.CreateClient();
        var token = BriefingProgramHostFactory.MintToken(OidA);
        _factory.Briefing = SmallBriefing();

        string rest;
        JsonElement tool;
        if (restFirst)
        {
            rest = await PostRestAsync(client, token, new { dueWithinDays = 7, recencyHours = 48 });
            tool = await McpCall.CallAsync(client, GetBriefingTool.Name, new { dueWithinDays = 7, recencyHours = 48 }, token);
        }
        else
        {
            tool = await McpCall.CallAsync(client, GetBriefingTool.Name, new { dueWithinDays = 7, recencyHours = 48 }, token);
            rest = await PostRestAsync(client, token, new { dueWithinDays = 7, recencyHours = 48 });
        }

        tool.TryGetProperty("isError", out var isError).Should().BeFalse("a success result carries no isError (got " + isError + ")");
        var structured = tool.GetProperty("structuredContent");
        AssertSameJson(structured, rest, "FR-09: the tool answers what the REST route answers for the same caller and windows");

        // Not a coincidence of two empty answers: the fixture is in it.
        structured.GetProperty("tldr").GetProperty("summary").GetString().Should().Be("Two things need you today.");
        structured.GetProperty("channelNarratives").EnumerateArray().Should().HaveCount(2);
        structured.GetRawText().Should().Contain(SecretMatter, "the widget draws the per-channel detail from structuredContent");
    }

    [Fact]
    public async Task GetBriefing_ForwardsTheWindowsLikeTheRoute_AndDefaultsWhenNoneAreGiven()
    {
        using var client = _factory.CreateClient();
        var token = BriefingProgramHostFactory.MintToken(OidA);

        await McpCall.CallAsync(client, GetBriefingTool.Name, new { dueWithinDays = 9, recencyHours = 36 }, token);
        _factory.LastWindows.Should().BeEquivalentTo(new { DueWithinDays = 9, RecencyHours = 36 });

        await McpCall.CallAsync(client, GetBriefingTool.Name, null, token);
        var defaults = DailyBriefingCollector.BriefingWindowOptions.Default;
        _factory.LastWindows.Should().BeEquivalentTo(new { defaults.DueWithinDays, defaults.RecencyHours });

        await McpCall.CallAsync(client, GetBriefingTool.Name, new { dueWithinDays = 2 }, token);
        _factory.LastWindows!.DueWithinDays.Should().Be(2);
        _factory.LastWindows.RecencyHours.Should().Be(defaults.RecencyHours, "an omitted window is the route's default");
    }

    [Fact]
    public async Task GetBriefing_AnswersForTheTokensCaller_NotAnotherCaller()
    {
        using var client = _factory.CreateClient();

        await McpCall.CallAsync(client, GetBriefingTool.Name, null, BriefingProgramHostFactory.MintToken(OidA));
        await McpCall.CallAsync(client, GetBriefingTool.Name, null, BriefingProgramHostFactory.MintToken(OidB));

        // The composite was asked for the systemuserid of the oid in each token, and the tenant the token carries, never a shared or earlier one.
        _factory.RenderCalls.Select(c => c.SystemUserId).Should().Equal(_factory.SystemUserOf(OidA), _factory.SystemUserOf(OidB));
        _factory.RenderCalls.Should().OnlyContain(c => c.TenantId == BriefingProgramHostFactory.TenantId);
    }

    [Fact]
    public async Task GetBriefing_ACallerWithNoSpaarkeUser_GetsTheRoutes403_AndNothingIsRendered()
    {
        using var client = _factory.CreateClient();
        var token = BriefingProgramHostFactory.MintToken(OidNobody); // no systemuser for this oid

        var result = await McpCall.CallAsync(client, GetBriefingTool.Name, null, token);
        using var restResponse = await client.SendAsync(RestRequest(token, null));

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        var error = result.GetProperty("structuredContent");
        error.GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        error.GetProperty("status").GetInt32().Should().Be(403);
        restResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the route answers the same caller with a 403");
        _factory.RenderCalls.Should().BeEmpty("no Spaarke user, no briefing");
    }

    // ---- text (ADR-015, NFR-02) --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_Text_IsTheTldrAndCounts_OnSingleLines_WithNoRecordNames_AtMostOneThousandCharacters()
    {
        using var client = _factory.CreateClient();
        _factory.Briefing = SmallBriefing(summary: "Two things\nneed you today.", takeaways: ["Review the\nAcme filing", "Call\x2028the client"]);

        var result = await McpCall.CallAsync(client, GetBriefingTool.Name, null, BriefingProgramHostFactory.MintToken(OidA));

        var text = result.GetProperty("content").EnumerateArray().Single().GetProperty("text").GetString()!;
        text.Length.Should().BeLessThanOrEqualTo(GetBriefingTool.MaxTextLength);
        text.Should().Contain("2 section(s), 2 highlight(s)")
            .And.Contain("Two things need you today.")
            .And.Contain("- Review the Acme filing")
            .And.Contain("- Call the client");
        text.Split('\n').Should().HaveCount(4, "the counts line, the summary and the two takeaways: user text never adds a line");
        text.Should().NotContain(SecretMatter, "ADR-015: the per-channel detail is in structuredContent, not the model-visible text");
    }

    [Fact]
    public async Task GetBriefing_Text_IsClippedToTheLimit_WhenTheTldrIsLong()
    {
        using var client = _factory.CreateClient();
        _factory.Briefing = SmallBriefing(summary: new string('s', 3000), takeaways: [.. Enumerable.Range(0, 30).Select(i => $"takeaway {i} " + new string('t', 200))]);

        var result = await McpCall.CallAsync(client, GetBriefingTool.Name, null, BriefingProgramHostFactory.MintToken(OidA));

        var text = result.GetProperty("content").EnumerateArray().Single().GetProperty("text").GetString()!;
        text.Length.Should().BeLessThanOrEqualTo(GetBriefingTool.MaxTextLength);
    }

    [Fact]
    public async Task GetBriefing_AnEmptyBriefing_SaysNothingNeedsAttention()
    {
        using var client = _factory.CreateClient();
        _factory.Briefing = new DailyBriefingNarrateResponse { GeneratedAtUtc = DateTimeOffset.UtcNow };

        var result = await McpCall.CallAsync(client, GetBriefingTool.Name, null, BriefingProgramHostFactory.MintToken(OidA));

        result.GetProperty("content").EnumerateArray().Single().GetProperty("text").GetString().Should().Be("Your daily briefing is empty: nothing needs attention right now.");
        result.TryGetProperty("isError", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildText_NamesTheChannelsThatCouldNotBeLoaded_AndHowManyHighlightsWereLeftOut()
    {
        var briefing = SmallBriefing() with { FailedChannels = ["matters\nand more", "documents"] };

        var text = GetBriefingTool.BuildText(briefing, omittedHighlights: 7);

        text.Should().Contain("7 more were left out").And.Contain("Could not load: matters and more, documents.");
        text.Should().NotContain("\n\n");
    }

    // ---- arguments (must not leak, must not guess) ---------------------------------------------------------------------------------------

    public static TheoryData<string, object> BadArguments => new()
    {
        { "an unknown argument", new { query = "secret-attacker-value" } },
        { "a caller id argument (identity comes from the token only)", new { systemUserId = "11111111-1111-1111-1111-111111111111" } },
        { "dueWithinDays 0", new { dueWithinDays = 0 } },
        { "dueWithinDays 32", new { dueWithinDays = 32 } },
        { "dueWithinDays a string", new { dueWithinDays = "secret-attacker-value" } },
        { "dueWithinDays fractional", new { dueWithinDays = 1.5 } },
        { "recencyHours 745", new { recencyHours = 745 } },
        { "recencyHours negative", new { recencyHours = -1 } },
    };

    [Theory]
    [MemberData(nameof(BadArguments))]
    public async Task GetBriefing_ABadArgument_IsAnIsErrorResult_NotIgnored_NamesTheFix_AndRendersNothing(string _, object arguments)
    {
        using var client = _factory.CreateClient();

        var result = await McpCall.CallAsync(client, GetBriefingTool.Name, arguments, BriefingProgramHostFactory.MintToken(OidA));

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        var error = result.GetProperty("structuredContent");
        error.GetProperty("reasonCode").GetString().Should().Be(GetBriefingTool.InvalidParameterReason);
        error.GetProperty("status").GetInt32().Should().Be(400);
        result.GetRawText().Should().NotContain("secret-attacker-value", "an argument value is never echoed back");
        _factory.RenderCalls.Should().BeEmpty("a refused call renders nothing");
    }

    // ---- no caller (ADR-028, FR-10) ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_WithNoRequestContext_IsUserContextRequired_AndRendersNothing()
    {
        var result = await GetBriefingTool.RunAsync(null, null, TimeSpan.FromSeconds(5), CancellationToken.None);

        AssertUserContextRequired(result);
        _factory.RenderCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, true)] // not authenticated, has an oid claim
    [InlineData(true, false)] // authenticated, no oid claim
    public async Task GetBriefing_WithAnUnauthenticatedCallerOrNoOid_IsUserContextRequired_AndRendersNothing(bool authenticated, bool hasOid)
    {
        using var scope = _factory.Services.CreateScope();
        var claims = hasOid ? new[] { new Claim("oid", OidA.ToString()) } : Array.Empty<Claim>();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null)),
            RequestServices = scope.ServiceProvider,
        };

        var result = await GetBriefingTool.RunAsync(null, http, TimeSpan.FromSeconds(5), CancellationToken.None);

        AssertUserContextRequired(result);
        _factory.RenderCalls.Should().BeEmpty("no caller, no render, no app-only fallback");
    }

    [Fact]
    public async Task GetBriefing_WithoutAToken_Returns401_AndWithMcpOff_Returns503()
    {
        using var client = _factory.CreateClient();
        using var anonymous = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"spaarke_get_briefing"}}""", Encoding.UTF8, "application/json"),
        };
        anonymous.Headers.Accept.ParseAdd("application/json");
        anonymous.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await client.SendAsync(anonymous);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.RenderCalls.Should().BeEmpty();

        await using var off = new BriefingProgramHostFactory(mcpEnabled: false);
        using var offClient = off.CreateClient();
        using var offRequest = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json"),
        };
        offRequest.Headers.Accept.ParseAdd("application/json");
        offRequest.Headers.Accept.ParseAdd("text/event-stream");
        offRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BriefingProgramHostFactory.MintToken(OidA));
        using var offResponse = await offClient.SendAsync(offRequest);
        offResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "the kill switch hides every tool");
    }

    // ---- failures (no exception text reaches the model) ---------------------------------------------------------------------------------------

    public static TheoryData<string, Func<Exception>, string, int> Faults => new()
    {
        { "an unexpected fault", () => new InvalidOperationException("redis-exploded-secret-detail"), GetBriefingTool.FailedReason, 500 },
        { "the feature switched off", () => new FeatureDisabledException("ai.briefing.disabled", "secret-binding-detail"), GetBriefingTool.UnavailableReason, 503 },
        { "an unconfigured dispatch", () => new DailyBriefingDispatchUnconfiguredException("secret-binding-detail"), GetBriefingTool.UnavailableReason, 503 },
    };

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task GetBriefing_AFault_IsAnIsErrorResult_NotATransportError_AndLeaksNoExceptionOrBindingText(
        string _, Func<Exception> fault, string reason, int status)
    {
        using var client = _factory.CreateClient();
        _factory.Fault = fault();

        var result = await McpCall.CallAsync(client, GetBriefingTool.Name, null, BriefingProgramHostFactory.MintToken(OidA));

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        var error = result.GetProperty("structuredContent");
        error.GetProperty("reasonCode").GetString().Should().Be(reason);
        error.GetProperty("status").GetInt32().Should().Be(status);
        result.GetRawText().Should().NotContain("secret").And.NotContain("exploded");
    }

    [Fact]
    public async Task GetBriefing_ABriefingThatIsNotReadyInTheBudget_IsATimeoutResult_NotAHang()
    {
        using var scope = _factory.Services.CreateScope();
        _factory.Hang = true;
        var http = AuthenticatedContext(scope, OidA);

        var started = DateTimeOffset.UtcNow;
        var result = await GetBriefingTool.RunAsync(null, http, TimeSpan.FromMilliseconds(150), CancellationToken.None);

        (DateTimeOffset.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(10));
        result.IsError.Should().BeTrue();
        var body = result.StructuredContent!.Value;
        body.GetProperty("reasonCode").GetString().Should().Be(GetBriefingTool.TimeoutReason);
        body.GetProperty("status").GetInt32().Should().Be(504);
        GetBriefingTool.GenerationBudget.Should().BeLessThan(McpLongRunning.Budget, "FR-14: the render stops before the 30 s tool budget");
    }

    [Fact]
    public async Task GetBriefing_WhenTheClientCancels_TheCancellationPropagates_NotATimeoutResult()
    {
        using var scope = _factory.Services.CreateScope();
        _factory.Hang = true;
        var http = AuthenticatedContext(scope, OidA);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => GetBriefingTool.RunAsync(null, http, TimeSpan.FromSeconds(30), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- result size (NFR-02: under 64 KiB at the route's worst case) -----------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_AWorstCaseBriefing_StaysUnder64KiB_KeepsEverySection_AndSaysHowManyWereLeftOut()
    {
        using var client = _factory.CreateClient();
        _factory.Briefing = WorstCaseBriefing();
        var token = BriefingProgramHostFactory.MintToken(OidA);

        var rest = await PostRestAsync(client, token, null);
        rest.Length.Should().BeGreaterThan(64 * 1024, "control: the route's own body for this briefing is over the limit");

        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = GetBriefingTool.Name } }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsByteArrayAsync();
        raw.Length.Should().BeLessThan(64 * 1024, "NFR-02: the whole JSON-RPC response stays under 64 KiB");

        var result = await McpCall.CallAsync(client, GetBriefingTool.Name, null, token);
        var structured = result.GetProperty("structuredContent");
        structured.GetProperty("channelNarratives").GetArrayLength().Should().Be(WorstCaseChannels, "no section disappears");
        var kept = structured.GetProperty("channelNarratives").EnumerateArray().Sum(c => c.GetProperty("bullets").GetArrayLength());
        kept.Should().BeLessThan(WorstCaseChannels * WorstCaseBullets);
        var text = result.GetProperty("content").EnumerateArray().Single().GetProperty("text").GetString()!;
        text.Should().Contain($"{WorstCaseChannels * WorstCaseBullets - kept} more were left out");
    }

    [Fact]
    public void SizeBound_AFittingBriefing_IsReturnedUntouched()
    {
        var briefing = SmallBriefing();

        var (fitted, omitted) = BriefingSizeBound.Fit(briefing);

        fitted.Should().BeSameAs(briefing);
        omitted.Should().Be(0);
    }

    [Fact]
    public void SizeBound_DropsDescriptionsFirst_AndKeepsEveryHighlight_WhenThatIsEnough()
    {
        var briefing = WorstCaseBriefing(descriptionLength: 1500, bullets: 12);
        GetBriefingTool.SerializedSize(briefing).Should().BeGreaterThan(BriefingSizeBound.BudgetBytes, "control: it does not fit as it is");

        var (fitted, omitted) = BriefingSizeBound.Fit(briefing);

        omitted.Should().Be(0);
        fitted.ChannelNarratives.Sum(c => c.Bullets.Length).Should().Be(briefing.ChannelNarratives.Sum(c => c.Bullets.Length));
        fitted.ChannelNarratives.SelectMany(c => c.Bullets).Should().OnlyContain(b => b.Description.Length == 0);
        GetBriefingTool.SerializedSize(fitted).Should().BeLessThanOrEqualTo(BriefingSizeBound.BudgetBytes);
    }

    [Fact]
    public void SizeBound_ThenTrimsTheSameNumberFromEverySection_AndCountsWhatItDropped()
    {
        var briefing = WorstCaseBriefing();

        var (fitted, omitted) = BriefingSizeBound.Fit(briefing);

        GetBriefingTool.SerializedSize(fitted).Should().BeLessThanOrEqualTo(BriefingSizeBound.BudgetBytes);
        fitted.ChannelNarratives.Should().HaveCount(WorstCaseChannels);
        fitted.ChannelNarratives.Select(c => c.Bullets.Length).Distinct().Should().ContainSingle("the same number is kept in every section");
        omitted.Should().Be(briefing.ChannelNarratives.Sum(c => c.Bullets.Length) - fitted.ChannelNarratives.Sum(c => c.Bullets.Length));
        omitted.Should().BeGreaterThan(0);
        fitted.Tldr.Should().Be(briefing.Tldr, "the summary is never trimmed");
    }

    [Fact]
    public void SizeBound_ABriefingWithNoSections_DoesNotThrow()
    {
        var briefing = new DailyBriefingNarrateResponse { Tldr = new TldrResult { Summary = new string('x', 100) }, GeneratedAtUtc = DateTimeOffset.UtcNow };

        var (fitted, omitted) = BriefingSizeBound.Fit(briefing);

        fitted.Should().BeSameAs(briefing);
        omitted.Should().Be(0);
    }

    // ---- telemetry ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetBriefing_WritesOneTelemetryLine_WithToolOutcomeAndCorrelation_AndNoIdentityOrRecordText()
    {
        var logs = new PortfolioProgramHostFactory.CapturedLogs();
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(logs)));
        using var client = factory.CreateClient();
        _factory.Briefing = SmallBriefing();

        await McpCall.CallAsync(client, GetBriefingTool.Name, null, BriefingProgramHostFactory.MintToken(OidA));

        var line = logs.Messages.Where(m => m.Contains("MCP tool spaarke_get_briefing completed", StringComparison.Ordinal)).Should().ContainSingle().Subject;
        line.Should().Contain("outcome ok").And.Contain("correlation");
        logs.Messages.Where(m => m.Contains("MCP tool", StringComparison.Ordinal))
            .Should().OnlyContain(m => !m.Contains(OidA.ToString()) && !m.Contains(SecretMatter));
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------------------------------

    private const int WorstCaseChannels = 4;
    private const int WorstCaseBullets = 50;

    private static HttpContext AuthenticatedContext(IServiceScope scope, Guid oid) => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", oid.ToString()), new Claim("tid", BriefingProgramHostFactory.TenantId)], "test")),
        RequestServices = scope.ServiceProvider,
    };

    private static void AssertUserContextRequired(ModelContextProtocol.Protocol.CallToolResult result)
    {
        result.IsError.Should().BeTrue();
        var body = result.StructuredContent!.Value;
        body.GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        body.GetProperty("status").GetInt32().Should().Be(401);
    }

    private static DailyBriefingNarrateResponse SmallBriefing(string summary = "Two things need you today.", string[]? takeaways = null) => new()
    {
        Tldr = new TldrResult { Summary = summary, KeyTakeaways = takeaways ?? ["Review the Acme filing"], CategoryCount = 2, PriorityItemCount = 1 },
        ChannelNarratives =
        [
            new ChannelNarrationResult
            {
                Category = "matters",
                Bullets =
                [
                    new NarrativeBulletDto
                    {
                        Narrative = SecretMatter + " has a hearing tomorrow",
                        ItemIds = ["item-1"],
                        PrimaryEntityType = "sprk_matter",
                        PrimaryEntityId = "11111111-1111-1111-1111-111111111111",
                        PrimaryEntityName = SecretMatter,
                        Description = "Hearing on the motion to compel",
                    },
                ],
            },
            new ChannelNarrationResult
            {
                Category = "documents",
                Bullets =
                [
                    new NarrativeBulletDto
                    {
                        Narrative = "Master services agreement was revised",
                        ItemIds = ["item-2"],
                        PrimaryEntityType = "sprk_document",
                        PrimaryEntityId = "33333333-3333-3333-3333-333333333333",
                        PrimaryEntityName = "Master services agreement",
                    },
                ],
            },
        ],
        GeneratedAtUtc = new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero),
    };

    /// <summary>The route's worst case: up to 50 rows in each channel, with long non-ASCII names (the encoder writes up to six bytes for one).</summary>
    private static DailyBriefingNarrateResponse WorstCaseBriefing(int descriptionLength = 600, int bullets = WorstCaseBullets) => new()
    {
        Tldr = new TldrResult { Summary = "Many things need you today.", KeyTakeaways = ["Start with the oldest"] },
        ChannelNarratives =
        [
            .. Enumerable.Range(0, WorstCaseChannels).Select(c => new ChannelNarrationResult
            {
                Category = $"channel{c}",
                Bullets =
                [
                    .. Enumerable.Range(0, bullets).Select(i => new NarrativeBulletDto
                    {
                        Narrative = $"Row {c}-{i}: " + new string('é', 80),
                        ItemIds = [$"id-{c}-{i}"],
                        PrimaryEntityType = "sprk_matter",
                        PrimaryEntityId = Guid.NewGuid().ToString(),
                        PrimaryEntityName = "Matter " + new string('ü', 40),
                        Description = new string('d', descriptionLength),
                    }),
                ],
            }),
        ],
        GeneratedAtUtc = new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero),
    };

    /// <summary>The same JSON value (the two hosts may escape characters differently; the value is what FR-09 compares).</summary>
    private static void AssertSameJson(JsonElement tool, string restBody, string because)
    {
        var expected = System.Text.Json.Nodes.JsonNode.Parse(restBody);
        var actual = System.Text.Json.Nodes.JsonNode.Parse(tool.GetRawText());
        System.Text.Json.Nodes.JsonNode.DeepEquals(actual, expected).Should().BeTrue(because + $"; tool {actual?.ToJsonString()} vs REST {expected?.ToJsonString()}");
    }

    private static HttpRequestMessage RestRequest(string token, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RestRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static async Task<string> PostRestAsync(HttpClient client, string token, object? body)
    {
        using var response = await client.SendAsync(RestRequest(token, body));
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return text;
    }
}

/// <summary>
/// TESTS ONLY (task 034). The real BFF host with two module-boundary doubles in place of Dataverse and the AI composite: the systemuser lookup
/// (<see cref="IGenericEntityService"/>, answered from <see cref="Seed"/>) and the coded composite (<see cref="StubComposite"/>, which records
/// every render and answers <see cref="Briefing"/>, a fault, or never). Everything between them is production code.
/// </summary>
public sealed class BriefingProgramHostFactory : McpProgramHostFactory
{
    private readonly bool _mcpEnabled;
    private readonly Dictionary<string, Guid> _systemUsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly List<RenderCall> _renderCalls = [];

    public BriefingProgramHostFactory() : this(mcpEnabled: true)
    {
    }

    internal BriefingProgramHostFactory(bool mcpEnabled) => _mcpEnabled = mcpEnabled;

    /// <summary>One render the composite received.</summary>
    internal sealed record RenderCall(Guid SystemUserId, string TenantId, DailyBriefingCollector.BriefingWindowOptions Windows);

    /// <summary>What the composite answers (null: a small fixed briefing).</summary>
    internal DailyBriefingNarrateResponse? Briefing { get; set; }

    /// <summary>When set the composite throws it.</summary>
    internal Exception? Fault { get; set; }

    /// <summary>When true the composite never answers until cancelled.</summary>
    internal bool Hang { get; set; }

    internal IReadOnlyList<RenderCall> RenderCalls
    {
        get
        {
            lock (_gate)
            {
                return _renderCalls.ToList();
            }
        }
    }

    internal DailyBriefingCollector.BriefingWindowOptions? LastWindows => RenderCalls.LastOrDefault()?.Windows;

    /// <summary>Gives <paramref name="oid"/> a systemuser (a different id from the oid on purpose).</summary>
    internal void Seed(Guid oid)
    {
        lock (_gate)
        {
            _systemUsers.TryAdd(oid.ToString("D"), Guid.NewGuid());
        }
    }

    internal Guid SystemUserOf(Guid oid)
    {
        lock (_gate)
        {
            return _systemUsers[oid.ToString("D")];
        }
    }

    /// <summary>Clears what a previous test left (the host is shared by a test class).</summary>
    internal void Reset()
    {
        lock (_gate)
        {
            _renderCalls.Clear();
        }

        Briefing = null;
        Fault = null;
        Hang = false;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        if (!_mcpEnabled)
        {
            builder.UseSetting("Mcp:Enabled", "false");
        }
    }

    protected override void ConfigureSeededServices(IServiceCollection services)
    {
        services.RemoveAll<DailyBriefingCompositeService>();
        services.AddSingleton<DailyBriefingCompositeService>(new StubComposite(this));
        services.RemoveAll<IGenericEntityService>();
        services.AddSingleton(SystemUserLookup());
    }

    /// <summary>The oid -> systemuserid FetchXML lookup the route makes, answered from the seed (no row for an unseeded oid).</summary>
    private IGenericEntityService SystemUserLookup()
    {
        var entities = new Mock<IGenericEntityService>();
        entities
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FetchExpression fetch, CancellationToken _) =>
            {
                Guid? found = null;
                lock (_gate)
                {
                    foreach (var (oid, systemUser) in _systemUsers)
                    {
                        if (fetch.Query.Contains(oid, StringComparison.OrdinalIgnoreCase))
                        {
                            found = systemUser;
                        }
                    }
                }

                return found is { } id
                    ? new EntityCollection([new Entity("systemuser", id) { ["systemuserid"] = id }])
                    : new EntityCollection([]);
            });
        return entities.Object;
    }

    private sealed class StubComposite(BriefingProgramHostFactory host)
        : DailyBriefingCompositeService(Microsoft.Extensions.Logging.Abstractions.NullLogger<DailyBriefingCompositeService>.Instance)
    {
        public override async Task<DailyBriefingNarrateResponse> RenderAsync(
            Guid systemUserId, string tenantId, DailyBriefingCollector.BriefingWindowOptions windows, CancellationToken cancellationToken)
        {
            lock (host._gate)
            {
                host._renderCalls.Add(new RenderCall(systemUserId, tenantId, windows));
            }

            if (host.Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (host.Fault is { } fault)
            {
                throw fault;
            }

            return host.Briefing ?? new DailyBriefingNarrateResponse
            {
                Tldr = new TldrResult { Summary = "Two things need you today.", KeyTakeaways = ["Review the Acme filing"] },
                ChannelNarratives = [new ChannelNarrationResult { Category = "matters", Bullets = [new NarrativeBulletDto { Narrative = "Acme v. Globex", PrimaryEntityName = "Acme v. Globex" }] }],
                GeneratedAtUtc = new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero),
            };
        }
    }
}
