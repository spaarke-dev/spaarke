// AUTH / CONFIDENTIALITY — FR-04 on spaarke_show_decisions (spaarkeai-mcp-bff-api-r1 task 022; owner ruling FR-04 option A widened,
// 2026-11-08; ADR-015). KEEP path: auth.
//
// A privileged Work Item reaches model-visible output (content AND structuredContent of spaarke_show_decisions) only as the count line
// "N privileged items — open to review": no title, subject name, reason, matter name or number, and no row. The app-only paging tool
// carries the detail. The seed (DecideLaneSeed) puts a unique marker at the start of every privileged item's title, subject name and
// reason and in the name and number of a matter that holds only privileged items, all on page 1. The privilege FLAG is the capability's
// (task 018 derives it from communications; DecideLanePagingTests proves a Privileged or Potentially Privileged communication subject is
// flagged); the double here sets it as 018 does, and these tests prove what the tool does with a flagged row.

using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Tests.Api.Mcp;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Mcp;

public sealed class DecisionsPrivilegeReductionTests
{
    private static readonly string Token = DecisionToolsTestHost.MintToken(Guid.Parse("0220bbbb-0000-4000-8000-000000000022"));

    [Theory]
    [InlineData(null)]
    [InlineData("all")]
    [InlineData("invoiceDecisions")]
    [InlineData("askOutsideCounsel")]
    [InlineData("approveOrRebudget")]
    [InlineData("chaseReply")]
    [InlineData("other")]
    public async Task ShowDecisions_NeverCarriesAPrivilegedItemsDetail_InContentOrStructuredContent_AndCountsIt(string? category)
    {
        var seed = DecideLaneSeed.Build();
        await using var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(seed));

        var result = await host.CallAsync(ShowDecisionsTool.Name, category is null ? null : new { category }, Token);

        // The marker is nowhere in the result: not in the text, not in a row, not in a core record.
        result.GetRawText().Should().NotContain(DecideLaneSeed.Marker);

        var structured = result.GetProperty("structuredContent");
        structured.GetProperty("pageItemCount").GetInt32().Should().Be(50);
        var items = structured.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("signalId").GetGuid()).ToList();
        items.Should().HaveCount(50 - DecideLaneSeed.PrivilegedOnPageOne).And.NotContain(seed.Privileged);
        structured.GetProperty("cores").EnumerateObject().Select(c => Guid.Parse(c.Name))
            .Should().NotContain(seed.PrivilegedMatterId, "a matter only privileged rows sit in is not named");

        // The lane continues after page 1, so the count is page 1's and says so (a later page may hold more).
        var line = $"{DecideLaneSeed.PrivilegedOnPageOne} privileged items among the first 50 — more of the lane may be privileged; open to review";
        structured.GetProperty("privileged").GetProperty("scope").GetString().Should().Be("firstPage");
        structured.GetProperty("privileged").GetProperty("count").GetInt32().Should().Be(DecideLaneSeed.PrivilegedOnPageOne);
        structured.GetProperty("privileged").GetProperty("text").GetString().Should().Be(line);
        Text(result).Should().Contain(line);
    }

    [Fact]
    public async Task ShowDecisions_APrivilegedItemPastPageOne_IsNotCounted_SoTheLineAndScopeSayTheCountIsTheFirstPages()
    {
        // One more privileged item at Console row 145. Page 1 (rows 1-50) cannot see it, so its count stays 13 and must not read as the lane's.
        var seed = DecideLaneSeed.Build(latePrivileged: true);
        seed.Privileged.Should().HaveCount(DecideLaneSeed.PrivilegedOnPageOne + 1);
        await using var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(seed));

        var result = await host.CallAsync(ShowDecisionsTool.Name, null, Token);

        var privileged = result.GetProperty("structuredContent").GetProperty("privileged");
        privileged.GetProperty("count").GetInt32().Should().Be(DecideLaneSeed.PrivilegedOnPageOne);
        privileged.GetProperty("scope").GetString().Should().Be("firstPage");
        privileged.GetProperty("text").GetString().Should().Be(
            "13 privileged items among the first 50 — more of the lane may be privileged; open to review");
        Text(result).Should().Contain("13 privileged items among the first 50 — more of the lane may be privileged; open to review");
        result.GetRawText().Should().NotContain(DecideLaneSeed.Marker, "FR-04: still no privileged detail");
    }

    [Fact]
    public async Task ShowDecisions_WithNoPrivilegedItemOnPageOne_ButMorePages_StillSaysSo_SinceALaterPageMayHoldSome()
    {
        // Only the late item is flagged: page 1 holds none, and the line must still appear because the lane continues.
        var seed = DecideLaneSeed.Build(latePrivileged: true);
        var lateOnly = seed.Groups[3].Items[DecideLaneSeed.LatePrivilegedIndexInC].SignalId;
        await using var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(seed with { Privileged = new HashSet<Guid> { lateOnly } }));

        var result = await host.CallAsync(ShowDecisionsTool.Name, null, Token);

        var privileged = result.GetProperty("structuredContent").GetProperty("privileged");
        privileged.GetProperty("count").GetInt32().Should().Be(0);
        privileged.GetProperty("scope").GetString().Should().Be("firstPage");
        privileged.GetProperty("text").GetString().Should().Be(
            "0 privileged items among the first 50 — more of the lane may be privileged; open to review");
        Text(result).Should().Contain("0 privileged items among the first 50 — more of the lane may be privileged; open to review");
    }

    [Fact]
    public async Task ShowDecisions_WhenPageOneIsTheWholeLane_TheCountIsTheLanes_WithTheUnqualifiedLine_AndNoLineWhenNoneArePrivileged()
    {
        var seed = DecideLaneSeed.Build();

        // The lane is matter P only (10 items, all privileged): one page, no cursor.
        await using (var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(seed with { Groups = [seed.Groups[0]] })))
        {
            var result = await host.CallAsync(ShowDecisionsTool.Name, null, Token);

            result.GetProperty("structuredContent").TryGetProperty("nextCursor", out _).Should().BeFalse();
            var privileged = result.GetProperty("structuredContent").GetProperty("privileged");
            privileged.GetProperty("scope").GetString().Should().Be("lane");
            privileged.GetProperty("text").GetString().Should().Be("10 privileged items — open to review");
            Text(result).Should().Contain("10 privileged items — open to review").And.NotContain("among the first");
        }

        // The lane is the first 20 items of matter B: one page, nothing privileged, so no line at all.
        var b = seed.Groups[2] with { Items = seed.Groups[2].Items.Take(20).ToList() };
        await using (var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(seed with { Groups = [b] })))
        {
            var result = await host.CallAsync(ShowDecisionsTool.Name, null, Token);

            result.GetProperty("structuredContent").GetProperty("privileged").GetProperty("scope").GetString().Should().Be("lane");
            Text(result).Should().NotContain("privileged items", "a whole lane with none privileged needs no line");
        }
    }

    [Fact]
    public async Task ShowDecisions_APrivilegedCommunicationSubject_HasNoSubjectNameOrTitle_InModelVisibleOutput()
    {
        var seed = DecideLaneSeed.Build();
        await using var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(seed));
        var communication = seed.Groups[1].Items.Single(i => i.SignalId == seed.CommunicationItemId);
        communication.SubjectRecordType.Should().Be("sprk_communication", "the seed's item is about a communication");

        var result = await host.CallAsync(ShowDecisionsTool.Name, null, Token);

        var raw = result.GetRawText();
        raw.Should().NotContain(communication.SubjectName!).And.NotContain(communication.Title!);
        raw.Should().NotContain(seed.CommunicationItemId.ToString("D"), "not even the row");
    }

    [Fact]
    public async Task ThePagingTool_CarriesThePrivilegedItemsDetail_ForTheWidget()
    {
        var seed = DecideLaneSeed.Build();
        await using var host = await DecisionToolsTestHost.StartAsync(new DecideLaneDouble(seed));

        var page = await host.CallAsync(ListDecisionPageTool.Name, null, Token);

        var structured = page.GetProperty("structuredContent");
        var flagged = structured.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("privilegeFlagged").GetBoolean()).ToList();
        flagged.Select(i => i.GetProperty("signalId").GetGuid()).Should().BeEquivalentTo(seed.Privileged.Where(id => seed.ConsoleOrder.Take(50).Contains(id)));
        flagged.Should().HaveCount(DecideLaneSeed.PrivilegedOnPageOne);
        flagged.Should().AllSatisfy(i =>
        {
            i.GetProperty("title").GetString().Should().StartWith(DecideLaneSeed.Marker);
            i.GetProperty("subjectName").GetString().Should().StartWith(DecideLaneSeed.Marker);
            i.GetProperty("ruleShortName").GetString().Should().StartWith(DecideLaneSeed.Marker);
        });
        structured.GetProperty("cores").GetProperty(seed.PrivilegedMatterId.ToString("D")).GetProperty("name").GetString()
            .Should().StartWith(DecideLaneSeed.Marker);

        // The text the paging tool returns says how many rows the page holds, and nothing of them.
        Text(page).Should().NotContain(DecideLaneSeed.Marker).And.NotContain("Subject ");
    }

    private static string Text(JsonElement result) =>
        string.Join("\n", result.GetProperty("content").EnumerateArray().Select(c => c.GetProperty("text").GetString()));
}
