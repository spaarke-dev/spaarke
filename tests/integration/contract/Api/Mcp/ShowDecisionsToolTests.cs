// CONTRACT — spaarke_show_decisions and spaarke_list_decision_page on POST /mcp/legal-operations (spaarkeai-mcp-bff-api-r1 task 022;
// spec FR-03, FR-05, FR-14, NFR-02, NFR-05). KEEP path: endpoint-contract.
//
// WHAT RUNS: the two PRODUCTION tools on the production MCP host (DecisionToolsTestHost: AddMcpModule, MapMcpEndpoints, RateLimitingModule,
// Kestrel, JwtBearer), over a boundary double of IDecisionCapability that pages a 200-item seed through the PRODUCTION DecideLanePaging
// and DecideCursor. NOT HERE: the FR-04 privilege reduction (tests/integration/auth/Mcp/DecisionsPrivilegeReductionTests.cs); the
// capability's own reads, ordering and privilege derivation (tasks 015 and 018, DecideLanePagingTests); the category partition's parity
// with the Console (DecideLaneCategoriesParityTests and laneCountFilters.parity.test.ts, one shared fixture).

using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp;

public sealed class ShowDecisionsToolTests
{
    private const int MaxResultBytes = 64 * 1024;
    private const string ConsoleLink =
        DecisionToolsTestHost.EnvironmentUrl + "/main.aspx?pagetype=webresource&webresourceName=" + DecisionsTextSummary.ConsoleWebResource;

    private static readonly string Token = DecisionToolsTestHost.MintToken(Guid.Parse("0220aaaa-0000-4000-8000-000000000022"));

    // ---- tools/list -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_ShowDecisions_IsReadOnly_ModelAndAppVisible_WithTheWidget_AndAOneEnumSchema()
    {
        await using var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(DecideLaneSeed.Build()));

        var show = (await host.ListToolsAsync(Token))[ShowDecisionsTool.Name];

        show.GetProperty("title").GetString().Should().Be("Show my decisions");
        var description = show.GetProperty("description").GetString()!;
        description.Length.Should().BeLessThanOrEqualTo(1024);
        description.Should().Contain("Use for:").And.Contain("Not for:");
        show.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
        show.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
        var ui = show.GetProperty("_meta").GetProperty("ui");
        ui.GetProperty("resourceUri").GetString().Should().Be("ui://spaarke/decisions.html");
        ui.GetProperty("visibility").EnumerateArray().Select(v => v.GetString()).Should().Equal("model", "app");

        var schema = show.GetProperty("inputSchema");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.TryGetProperty("required", out _).Should().BeFalse("category is optional");
        var properties = schema.GetProperty("properties").EnumerateObject().ToList();
        properties.Select(p => p.Name).Should().Equal("category");
        properties[0].Value.GetProperty("enum").EnumerateArray().Select(v => v.GetString())
            .Should().Equal("all", "invoiceDecisions", "askOutsideCounsel", "approveOrRebudget", "chaseReply", "other");
    }

    [Fact]
    public async Task ToolsList_ThePagingTool_IsAppOnly_AndReadOnly_WithOnlyACursor_AndNoWriteToolIsModelVisible()
    {
        await using var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(DecideLaneSeed.Build()));

        var tools = await host.ListToolsAsync(Token);
        var page = tools[ListDecisionPageTool.Name];

        page.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
        page.GetProperty("_meta").GetProperty("ui").GetProperty("visibility").EnumerateArray().Select(v => v.GetString()).Should().Equal("app");
        page.GetProperty("inputSchema").GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().Equal("cursor");
        page.GetProperty("inputSchema").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();

        // FR-14: every tool the model can see is read-only (a tool with no visibility is model-visible by default).
        foreach (var (name, tool) in tools)
        {
            var modelVisible = !tool.TryGetProperty("_meta", out var meta)
                               || !meta.TryGetProperty("ui", out var toolUi)
                               || !toolUi.TryGetProperty("visibility", out var visibility)
                               || visibility.EnumerateArray().Any(v => v.GetString() == "model");
            if (modelVisible)
            {
                tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().Should().BeTrue($"{name} is model-visible");
            }
        }
    }

    // ---- arguments ----------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("\"decisions\"")]
    [InlineData("\"ChaseReply\"")]
    [InlineData("5")]
    public async Task ShowDecisions_AnInvalidCategory_IsAnIsErrorResult_ListingTheValidValues_AndReadsNothing(string categoryJson)
    {
        var lane = new DecideLaneDouble(DecideLaneSeed.Build());
        await using var host = await DecisionToolsTestHost.StartAsync(lane);

        var result = await host.CallAsync(ShowDecisionsTool.Name, new { category = JsonDocument.Parse(categoryJson).RootElement }, Token);

        result.GetProperty("isError").GetBoolean().Should().BeTrue("a bad argument is a tool result the model can fix, not a transport error");
        var error = result.GetProperty("structuredContent");
        error.GetProperty("reasonCode").GetString().Should().Be(DecisionCommitReasons.InvalidParameter);
        var message = error.GetProperty("message").GetString()!;
        foreach (var key in DecideLaneCategories.Keys)
        {
            message.Should().Contain(key);
        }

        lane.LaneReads.Should().Be(0);
        lane.PageReads.Should().Be(0);
    }

    [Fact]
    public async Task BothTools_AnArgumentTheyDoNotTake_IsAnIsErrorResult_NotIgnored()
    {
        var lane = new DecideLaneDouble(DecideLaneSeed.Build());
        await using var host = await DecisionToolsTestHost.StartAsync(lane);

        var show = await host.CallAsync(ShowDecisionsTool.Name, new { category = "all", matter = "MAT-0001" }, Token);
        var page = await host.CallAsync(ListDecisionPageTool.Name, new { cursor = (string?)null, pageSize = 200 }, Token);

        foreach (var result in new[] { show, page })
        {
            result.GetProperty("isError").GetBoolean().Should().BeTrue();
            result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(DecisionCommitReasons.InvalidParameter);
        }

        lane.LaneReads.Should().Be(0);
        lane.PageReads.Should().Be(0);
    }

    // ---- the caller (FR-10) --------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task BothTools_ReadAsTheRequestsCaller_HandingTheCapabilityThePrincipalOfTheRequestsToken()
    {
        var oid = Guid.Parse("0220cccc-0000-4000-8000-000000000022");
        var lane = new DecideLaneDouble(DecideLaneSeed.Build());
        await using var host = await DecisionToolsTestHost.StartAsync(lane);
        var token = DecisionToolsTestHost.MintToken(oid);

        await host.CallAsync(ShowDecisionsTool.Name, null, token);
        await host.CallAsync(ListDecisionPageTool.Name, null, token);

        lane.Users.Should().HaveCount(3, "show reads the lane and page 1; the paging tool reads one page");
        lane.Users.Should().AllSatisfy(user =>
        {
            user.Should().NotBeNull();
            user!.Identity!.IsAuthenticated.Should().BeTrue();
            var claim = user.FindFirst("oid") ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier");
            claim!.Value.Should().Be(oid.ToString(), "the capability reads as the request's own caller, not a service identity");
        });
    }

    [Fact]
    public async Task BothTools_WithNoContextOrAnUnauthenticatedPrincipal_AreUserContextRequired_AndCallNoCapabilityMember()
    {
        var lane = new DecideLaneDouble(DecideLaneSeed.Build());
        var anonymous = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var signedIn = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", Guid.NewGuid().ToString())], authenticationType: "test")),
        };

        foreach (HttpContext? http in new[] { null, anonymous })
        {
            var show = await ShowDecisionsTool.RunAsync(null, http, lane, DecisionToolsTestHost.EnvironmentUrl, CancellationToken.None);
            var page = await ListDecisionPageTool.RunAsync(null, http, lane, CancellationToken.None);

            foreach (var result in new[] { show.Result, page.Result })
            {
                result.IsError.Should().BeTrue();
                var body = result.StructuredContent!.Value;
                body.GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
                body.GetProperty("status").GetInt32().Should().Be(401);
            }
        }

        lane.LaneReads.Should().Be(0);
        lane.PageReads.Should().Be(0);
        lane.Users.Should().BeEmpty("no principal was ever handed to the capability, so nothing fell back to an app-only read");

        // The control: a signed-in context is not refused by the same guard.
        var ok = await ListDecisionPageTool.RunAsync(null, signedIn, lane, CancellationToken.None);
        ok.Result.IsError.Should().NotBe(true);
        lane.Users.Should().ContainSingle().Which.Should().BeSameAs(signedIn.User);
    }

    // ---- counts and the text (FR-03, FR-14) -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ShowDecisions_With200Items_CountsEveryCategoryOverTheWholeLane_AndContentAloneListsCountsTopItemsAndLinks()
    {
        var seed = DecideLaneSeed.Build();
        await using var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(seed));

        var result = await host.CallAsync(ShowDecisionsTool.Name, null, Token);

        AssertNotError(result);
        var structured = result.GetProperty("structuredContent");
        structured.GetProperty("total").GetInt32().Should().Be(200, "the Do-lane item is not counted");
        structured.GetProperty("category").GetString().Should().Be("all");
        structured.GetProperty("categories").EnumerateArray()
            .Select(c => (c.GetProperty("key").GetString(), c.GetProperty("count").GetInt32()))
            .Should().Equal(("all", 200), ("invoiceDecisions", 40), ("askOutsideCounsel", 40), ("approveOrRebudget", 40), ("chaseReply", 40), ("other", 40));
        structured.GetProperty("consoleBaseUrl").GetString().Should().Be(DecisionToolsTestHost.EnvironmentUrl);

        // FR-14: the text alone answers. Page 1 in Console order is P's 10 (privileged) then A-0 .. A-39; the top five visible are A-0
        // (billing), A-1, A-3, A-4 and A-6.
        var text = Text(result);
        text.Should().Contain("Spaarke Decide lane: 200 items need your decision.");
        text.Should().Contain("By category: Invoice decisions 40; Ask outside counsel 40; Approve or rebudget 40; Chase a reply 40; Other 40.");
        var top = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 2 && char.IsDigit(l[0]) && l[1] == '.').ToList();
        top.Should().HaveCount(DecisionsTextSummary.TopItems);
        top.Select(Headline).Should().Equal("Subject A-0", "Subject A-1", "Subject A-3", "Subject A-4", "Subject A-6");
        top.Should().AllSatisfy(l => l.Should().Contain("(Alpha matter)"));

        var a0 = seed.Groups[1].Items[0];
        top[0].Should().EndWith(
            ". Finding: decide it in Invoice Review: " + DecisionToolsTestHost.EnvironmentUrl
            + "/main.aspx?pagetype=webresource&webresourceName=sprk_invoicereviewpage&data="
            + Uri.EscapeDataString($"invoiceId={a0.InvoiceReview!.InvoiceId:D}&lineId={a0.InvoiceReview.LineId!.Value:D}"));
        top.Skip(1).Should().AllSatisfy(l => l.Should().EndWith(". Open in Spaarke: " + ConsoleLink));
        text.Should().Contain("Open the Spaarke Console to see and decide every item: " + ConsoleLink);
    }

    [Fact]
    public async Task ShowDecisions_ACategory_NarrowsTheTopItemsOnly_AndTheCountsStayWhole()
    {
        await using var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(DecideLaneSeed.Build()));

        var result = await host.CallAsync(ShowDecisionsTool.Name, new { category = "chaseReply" }, Token);

        var structured = result.GetProperty("structuredContent");
        structured.GetProperty("category").GetString().Should().Be("chaseReply");
        structured.GetProperty("total").GetInt32().Should().Be(200);
        var text = Text(result);
        text.Should().Contain("Showing Chase a reply (40).");
        // Chase-reply items of page 1 are A-3, A-8, A-13, ... (global index % 5 == 3, after P's 10).
        var top = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 2 && char.IsDigit(l[0]) && l[1] == '.').Select(Headline);
        top.Should().Equal("Subject A-3", "Subject A-8", "Subject A-13", "Subject A-18", "Subject A-23");
    }

    // ---- size and paging (FR-05, NFR-02) ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task With200Items_EveryResultIsUnder64KiB_AndPagingReturnsEachItemOnceInConsoleOrder()
    {
        var seed = DecideLaneSeed.Build();
        await using var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(seed));

        var show = await host.CallAsync(ShowDecisionsTool.Name, null, Token);
        Bytes(show).Should().BeLessThan(MaxResultBytes);

        var first = await host.CallAsync(ListDecisionPageTool.Name, null, Token);
        Bytes(first).Should().BeLessThan(MaxResultBytes);
        var ids = Ids(first).ToList();
        ids.Should().HaveCount(50);

        // The widget continues from spaarke_show_decisions' cursor: page 1 of both tools ends at the same place.
        var cursor = show.GetProperty("structuredContent").GetProperty("nextCursor").GetString();
        cursor.Should().Be(first.GetProperty("structuredContent").GetProperty("nextCursor").GetString());
        var pages = 1;
        while (cursor is not null)
        {
            var page = await host.CallAsync(ListDecisionPageTool.Name, new { cursor }, Token);
            Bytes(page).Should().BeLessThan(MaxResultBytes);
            ids.AddRange(Ids(page));
            cursor = page.GetProperty("structuredContent").TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
            pages++;
        }

        pages.Should().Be(4);
        ids.Should().Equal(seed.ConsoleOrder, "every item exactly once, in Console order");
    }

    [Fact]
    public async Task ThePagingTool_AStaleCursor_ReturnsTheRemainingItems_WithNoDuplicate()
    {
        var seed = DecideLaneSeed.Build();
        var lane = new DecideLaneDouble(seed);
        await using var host = await DecisionToolsTestHost.StartAsync(lane);

        var first = await host.CallAsync(ListDecisionPageTool.Name, null, Token);
        var seen = Ids(first).ToList();
        var cursor = first.GetProperty("structuredContent").GetProperty("nextCursor").GetString();

        // The lane moves on: two items of page 1 are decided, and two items not yet returned (in A after the cursor, and in C) go away.
        var gone = new HashSet<Guid> { seen[12], seen[30], seed.Groups[1].Items[45].SignalId, seed.Groups[3].Items[10].SignalId };
        for (var g = 0; g < lane.Groups.Count; g++)
        {
            lane.Groups[g] = lane.Groups[g] with { Items = lane.Groups[g].Items.Where(i => !gone.Contains(i.SignalId)).ToList() };
        }

        var rest = new List<Guid>();
        while (cursor is not null)
        {
            var page = await host.CallAsync(ListDecisionPageTool.Name, new { cursor }, Token);
            AssertNotError(page);
            rest.AddRange(Ids(page));
            cursor = page.GetProperty("structuredContent").TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
        }

        rest.Should().OnlyHaveUniqueItems().And.NotContain(seen, "nothing returned before comes again");
        rest.Should().Equal(seed.ConsoleOrder.Skip(50).Where(id => !gone.Contains(id)), "what remains, once, in Console order");
    }

    [Theory]
    [InlineData("\"not-a-cursor\"")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("42")]
    public async Task ThePagingTool_AMalformedCursor_IsAnIsErrorResult_CursorInvalid(string cursorJson)
    {
        await using var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(DecideLaneSeed.Build()));

        var result = await host.CallAsync(ListDecisionPageTool.Name, new { cursor = JsonDocument.Parse(cursorJson).RootElement }, Token);

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(SignalReadEndpoints.CursorInvalidReasonCode);
    }

    // ---- failures: an isError result through DecisionResultMapper, never a partial list ---------------------------------------------------

    [Theory]
    [InlineData("too_many_signals")]
    [InlineData("core_pair_read_failed")]
    public async Task ShowDecisions_WhenTheLanesReadIsUnavailable_IsAnIsErrorResult_WithTheWorklistsReasonCode_AndNoList(string reason)
    {
        var lane = new DecideLaneDouble(DecideLaneSeed.Build()) { LaneFailure = SignalReadResult.Unavailable(reason) };
        await using var host = await DecisionToolsTestHost.StartAsync(lane);

        var result = await host.CallAsync(ShowDecisionsTool.Name, null, Token);

        AssertWorklistUnavailable(result, reason);
        lane.PageReads.Should().Be(0, "no page is read once the lane failed");
    }

    [Theory]
    [InlineData("too_many_signals")]
    [InlineData("core_pair_read_failed")]
    public async Task BothTools_WhenThePageReadFails_AreAnIsErrorResult_NeverAPartialList(string reason)
    {
        var lane = new DecideLaneDouble(DecideLaneSeed.Build()) { PageFailure = DecidePageResult.ReadFailed(SignalReadResult.Unavailable(reason)) };
        await using var host = await DecisionToolsTestHost.StartAsync(lane);

        AssertWorklistUnavailable(await host.CallAsync(ShowDecisionsTool.Name, null, Token), reason);
        AssertWorklistUnavailable(await host.CallAsync(ListDecisionPageTool.Name, null, Token), reason);
    }

    // ---- telemetry (NFR-05) -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EachCall_LogsOneLine_WithToolLatencyRowsOutcomeAndCorrelation_AndNoArgumentOrRecordText()
    {
        var seed = DecideLaneSeed.Build();
        await using var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(seed));

        await host.CallAsync(ShowDecisionsTool.Name, new { category = "chaseReply" }, Token);
        var first = await host.CallAsync(ListDecisionPageTool.Name, null, Token);
        var cursor = first.GetProperty("structuredContent").GetProperty("nextCursor").GetString()!;
        await host.CallAsync(ListDecisionPageTool.Name, new { cursor }, Token);
        await host.CallAsync(ShowDecisionsTool.Name, new { category = "notACategory" }, Token);

        var lines = host.Logs.Entries.Where(e => e.Category.EndsWith("." + nameof(ShowDecisionsTool), StringComparison.Ordinal)
                                                 || e.Category.EndsWith("." + nameof(ListDecisionPageTool), StringComparison.Ordinal)).ToList();
        lines.Select(e => ((string)e.State["ToolName"]!, (string)e.State["Outcome"]!, (int)e.State["RowCount"]!)).Should().Equal(
            (ShowDecisionsTool.Name, "ok", 50),
            (ListDecisionPageTool.Name, "ok", 50),
            (ListDecisionPageTool.Name, "ok", 50),
            (ShowDecisionsTool.Name, DecisionCommitReasons.InvalidParameter, 0));
        lines.Should().AllSatisfy(e =>
        {
            ((long)e.State["ElapsedMs"]!).Should().BeGreaterThanOrEqualTo(0);
            ((string?)e.State["CorrelationId"]).Should().NotBeNullOrWhiteSpace();
        });

        // Nothing the caller sent or the lane holds in anything the tools log: no argument value, no cursor, no record text, no token.
        var logged = string.Join("\n", lines.Select(e => e.Message + " " + string.Join(" ", e.State.Values)));
        logged.Should().NotContain("chaseReply").And.NotContain("notACategory").And.NotContain(cursor).And.NotContain(Token);
        logged.Should().NotContain(DecideLaneSeed.Marker).And.NotContain("Subject ").And.NotContain("Alpha matter").And.NotContain("Title ");
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------------------------

    private static void AssertWorklistUnavailable(JsonElement result, string internalReason)
    {
        result.GetProperty("isError").GetBoolean().Should().BeTrue("a read failure is a tool result, not a transport error");
        var error = result.GetProperty("structuredContent");
        error.EnumerateObject().Select(p => p.Name).Should().Equal(["reasonCode", "message", "status", "correlationId"], "the error only: no items, no counts");
        error.GetProperty("status").GetInt32().Should().Be(503, "the status the worklist route answers");
        error.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        error.GetProperty("reasonCode").GetString().Should().Be(SignalReadEndpoints.WorklistUnavailableReasonCode);
        result.GetRawText().Should().NotContain(internalReason, "the worklist route's 503 hides the internal reason; so does the tool");
        result.GetRawText().Should().NotContain("Subject ").And.NotContain("Alpha matter");
    }

    private static void AssertNotError(JsonElement result) =>
        (!result.TryGetProperty("isError", out var isError) || isError.ValueKind == JsonValueKind.False)
            .Should().BeTrue($"the call succeeds: {result.GetRawText()}");

    private static string Text(JsonElement result) =>
        string.Join("\n", result.GetProperty("content").EnumerateArray().Select(c => c.GetProperty("text").GetString()));

    private static string Headline(string topLine) => topLine[(topLine.IndexOf(". ", StringComparison.Ordinal) + 2)..].Split(':')[0];

    private static int Bytes(JsonElement result) => Encoding.UTF8.GetByteCount(result.GetRawText());

    private static IEnumerable<Guid> Ids(JsonElement pageResult) =>
        pageResult.GetProperty("structuredContent").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("signalId").GetGuid());
}
