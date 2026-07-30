using System;
using System.Collections.Generic;
using FluentAssertions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for <see cref="ChatEndpoints.SelectMostRecentSession"/> — the reopen-target
/// selection rule backing <c>GET /api/ai/chat/sessions/by-analysis/{analysisId}</c>
/// (ai-advanced-capabilities-analysis-hub-r1 task 031, spec FR-11).
///
/// Pure domain logic (no I/O, no mocks) — an Analysis may have accrued more than one bound
/// session over its lifetime (fork-on-analysis, task 021); this rule decides WHICH one the hub
/// grid's row-open reopens. <see cref="ChatEndpoints.SelectMostRecentSession"/> is <c>internal</c>
/// (not <c>private</c>) specifically so this branchy rule is testable directly via
/// <c>InternalsVisibleTo</c> — no reflection into a private member (tests CLAUDE.md B8 ban).
///
/// @see Sprk.Bff.Api.Api.Ai.ChatEndpoints.SelectMostRecentSession
/// @see Sprk.Bff.Api.Api.Ai.ChatEndpoints.GetSessionByAnalysisAsync
/// @see Sprk.Bff.Api.Models.Ai.Chat.AnalysisSessionSummary
/// </summary>
public class ChatEndpointsSessionByAnalysisTests
{
    private static AnalysisSessionSummary Session(string id, bool archived, DateTimeOffset? createdOn) =>
        new(SessionId: id, MessageCount: 1, IsArchived: archived, CreatedOn: createdOn);

    [Fact]
    public void SelectMostRecentSession_SingleSession_ReturnsIt()
    {
        var only = Session("s1", archived: false, createdOn: DateTimeOffset.Parse("2026-07-01T00:00:00Z"));

        var result = ChatEndpoints.SelectMostRecentSession(new List<AnalysisSessionSummary> { only });

        result.Should().Be(only);
    }

    [Fact]
    public void SelectMostRecentSession_MultipleSessions_ReturnsMostRecentlyCreated()
    {
        var older = Session("older", archived: false, createdOn: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        var newer = Session("newer", archived: false, createdOn: DateTimeOffset.Parse("2026-07-15T00:00:00Z"));
        var middle = Session("middle", archived: false, createdOn: DateTimeOffset.Parse("2026-07-01T00:00:00Z"));

        var result = ChatEndpoints.SelectMostRecentSession(new List<AnalysisSessionSummary> { older, newer, middle });

        result.SessionId.Should().Be("newer");
    }

    [Fact]
    public void SelectMostRecentSession_ArchivedSessionIsMostRecent_StillWins()
    {
        // An archived session (task 022 durability flip) still holds its durable transcript
        // (ADR-040) — reopening it is a legitimate "view history" action, not a resume-writes
        // action, so archived status must NOT disqualify it from being the reopen target.
        var live = Session("live", archived: false, createdOn: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        var archivedNewer = Session("archived-newer", archived: true, createdOn: DateTimeOffset.Parse("2026-07-20T00:00:00Z"));

        var result = ChatEndpoints.SelectMostRecentSession(new List<AnalysisSessionSummary> { live, archivedNewer });

        result.SessionId.Should().Be("archived-newer");
        result.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void SelectMostRecentSession_SessionWithNullCreatedOn_NeverShadowsADatedSession()
    {
        // Defensive case per the method's doc comment: CreateSessionAsync always stamps CreatedOn
        // in practice, but a null must sort LAST (DateTimeOffset.MinValue fallback), never win.
        var dated = Session("dated", archived: false, createdOn: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var undated = Session("undated", archived: false, createdOn: null);

        var result = ChatEndpoints.SelectMostRecentSession(new List<AnalysisSessionSummary> { undated, dated });

        result.SessionId.Should().Be("dated");
    }
}
