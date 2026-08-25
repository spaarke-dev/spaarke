using System;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for the R4 task 010 / FR-03 per-Action bounded grounded-tool allow-list parse
/// (<see cref="AnalysisActionService.ParseGroundedToolAllowList"/>). The allow-list is catalog DATA
/// (a JSON array of grounded-tool ids on the <c>sprk_groundedtoolallowlist</c> column) mirroring the
/// <c>sprk_allowsknowledge</c> per-Action opt-in: an Action WITHOUT it resolves to an EMPTY set
/// (ack-tier / opt-out); an Action WITH it resolves to EXACTLY its declared set — never a code-side
/// tool-name list (ADR-039). Fail-closed: malformed input never widens the mounted tool set.
/// </summary>
public class AnalysisActionAllowListTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseGroundedToolAllowList_OptOut_ReturnsEmpty(string? raw)
    {
        AnalysisActionService.ParseGroundedToolAllowList(raw).Should().BeEmpty();
    }

    [Fact]
    public void ParseGroundedToolAllowList_EmptyJsonArray_ReturnsEmpty()
    {
        AnalysisActionService.ParseGroundedToolAllowList("[]").Should().BeEmpty();
    }

    [Fact]
    public void ParseGroundedToolAllowList_OptIn_ReturnsExactDeclaredSetInOrder()
    {
        var raw = "[\"spaarke.grid_overview\",\"spaarke.daily_briefing_overview\"]";

        var result = AnalysisActionService.ParseGroundedToolAllowList(raw);

        // Exactly the declared set, in declared order — the DATA, not a code-derived list.
        result.Should().Equal("spaarke.grid_overview", "spaarke.daily_briefing_overview");
    }

    [Theory]
    [InlineData("not json")]                              // not JSON at all
    [InlineData("{\"tool\":\"spaarke.grid_overview\"}")]   // object, not array
    [InlineData("\"spaarke.grid_overview\"")]              // bare string, not array
    [InlineData("42")]                                      // number, not array
    public void ParseGroundedToolAllowList_Malformed_FailsClosedToEmpty(string raw)
    {
        // Fail-closed: a mis-authored row must NEVER widen the mounted tool set.
        AnalysisActionService.ParseGroundedToolAllowList(raw).Should().BeEmpty();
    }

    [Fact]
    public void ParseGroundedToolAllowList_Duplicates_DeduplicatedPreservingFirstSeenOrder()
    {
        var raw = "[\"spaarke.grid_overview\",\"spaarke.grid_overview\",\"spaarke.daily_briefing_overview\"]";

        var result = AnalysisActionService.ParseGroundedToolAllowList(raw);

        result.Should().Equal("spaarke.grid_overview", "spaarke.daily_briefing_overview");
    }

    [Fact]
    public void ParseGroundedToolAllowList_BlankEntries_Dropped()
    {
        var raw = "[\"spaarke.grid_overview\",\"\",\"   \",\"spaarke.daily_briefing_overview\"]";

        var result = AnalysisActionService.ParseGroundedToolAllowList(raw);

        result.Should().Equal("spaarke.grid_overview", "spaarke.daily_briefing_overview");
    }

    [Fact]
    public void AnalysisAction_DefaultGroundedToolAllowList_IsEmptyNotNull()
    {
        // An Action materialized without the opt-in column stays ack-tier: empty, never null.
        var action = new AnalysisAction
        {
            Id = Guid.NewGuid(),
            Name = "Ack-tier action",
            SystemPrompt = "You are an AI assistant.",
        };

        action.GroundedToolAllowList.Should().NotBeNull().And.BeEmpty();
    }
}
