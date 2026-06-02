using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for <see cref="DailyBriefingEndpoints"/>.
///
/// Verifies fix for task 083: an empty narrate payload (no categories, no priority
/// items, no channels) returns 200 with empty bullets/channels instead of 400
/// "Bad Request". This unblocks the frontend `useDailyBriefing` hook in fresh
/// dev environments where the user has no notifications to narrate.
///
/// Task 070 repair (2026-05-31): the production HandleNarrate signature was changed
/// — the old `IOpenAiClient` parameter was replaced by an optional `IBriefingAi?` and
/// reordered (`request, loggerFactory, httpContext, cancellationToken, briefingAi`).
/// The reflection invocation has been updated to match the current signature and
/// pass <c>null</c> for the briefingAi (the AI-disabled path is what the existing
/// "empty payload" assertions exercise).
/// </summary>
[Trait("status", "repaired")]
public sealed class DailyBriefingEndpointsTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes the private static HandleNarrate handler via reflection.
    /// Current production signature (task 070 repair):
    ///   HandleNarrate(DailyBriefingNarrateRequest request, ILoggerFactory loggerFactory,
    ///                 HttpContext httpContext, CancellationToken cancellationToken,
    ///                 IBriefingAi? briefingAi = null)
    /// The empty-payload short-circuit returns 200 only AFTER the AI-availability
    /// check; tests must therefore pass a non-null <c>briefingAi</c> mock. The mock
    /// uses <see cref="MockBehavior.Strict"/> so any unexpected call would fail.
    /// </summary>
    private static async Task<IResult> InvokeHandleNarrateAsync(
        DailyBriefingNarrateRequest request,
        IBriefingAi briefingAi,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var method = typeof(DailyBriefingEndpoints)
            .GetMethod("HandleNarrate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("HandleNarrate not found via reflection");

        var task = (Task<IResult>)method.Invoke(null, new object?[]
        {
            request,
            NullLoggerFactory.Instance,
            httpContext,
            cancellationToken,
            briefingAi
        })!;

        return await task;
    }

    private static HttpContext MakeContext() => new DefaultHttpContext();

    // ── Tests: empty payload → 200 ────────────────────────────────────────────

    [Fact]
    public async Task HandleNarrate_Returns_200_Empty_On_Empty_Payload()
    {
        // Arrange — fully empty request (matches frontend buildEmptyNarrateRequest())
        var request = new DailyBriefingNarrateRequest
        {
            Categories = [],
            PriorityItems = [],
            TotalNotificationCount = 0,
            Channels = []
        };

        // BriefingAi must NOT be invoked — empty payload short-circuits before any AI call.
        var briefingAi = new Mock<IBriefingAi>(MockBehavior.Strict);
        var context = MakeContext();

        // Act
        var result = await InvokeHandleNarrateAsync(request, briefingAi.Object, context);

        // Assert — 200 with empty narrative response
        result.Should().BeOfType<Ok<DailyBriefingNarrateResponse>>();
        var ok = (Ok<DailyBriefingNarrateResponse>)result;
        ok.Value.Should().NotBeNull();
        ok.Value!.Tldr.Should().NotBeNull();
        ok.Value.Tldr.Briefing.Should().BeEmpty();
        ok.Value.Tldr.TopAction.Should().BeEmpty();
        ok.Value.Tldr.CategoryCount.Should().Be(0);
        ok.Value.Tldr.PriorityItemCount.Should().Be(0);
        ok.Value.ChannelNarratives.Should().BeEmpty();

        // Strict mock — verifies BriefingAi was never called
        briefingAi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleNarrate_Returns_200_Empty_On_All_Empty_Even_With_Nonzero_TotalCount()
    {
        // Arrange — TotalNotificationCount > 0 but no actionable content (edge case)
        var request = new DailyBriefingNarrateRequest
        {
            Categories = [],
            PriorityItems = [],
            TotalNotificationCount = 5,
            Channels = []
        };

        var briefingAi = new Mock<IBriefingAi>(MockBehavior.Strict);
        var context = MakeContext();

        // Act
        var result = await InvokeHandleNarrateAsync(request, briefingAi.Object, context);

        // Assert — still 200 because the three actionable arrays are all empty
        result.Should().BeOfType<Ok<DailyBriefingNarrateResponse>>();
        briefingAi.VerifyNoOtherCalls();
    }
}
