using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
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

    // ── Tests: FR-15 — `BuildChannelNarrationPrompt` emits `regardingId=` per item ────────────
    //
    // Task 030 (R2 spec §P2b FR-15/FR-16) updated `BuildChannelNarrationPrompt` so that
    // every item line carries the item's regardingId in the bracketed prefix when present.
    // This gives the LLM a real ID to echo back as `primaryEntityId` instead of inventing
    // one. The defensive graceful-degradation case: items WITHOUT a regardingId emit only
    // `[id={id}]` (no empty `regardingId=` token the LLM might literalize back).

    [Fact]
    public void BuildChannelNarrationPrompt_Includes_RegardingId_Per_Item_With_Regarding()
    {
        // Arrange — channel with 2 items, both have non-empty RegardingId
        var matterId = Guid.NewGuid().ToString();
        var taskId = Guid.NewGuid().ToString();
        var channel = new ChannelNarrationInput
        {
            Category = "tasks",
            Label = "Tasks Overdue",
            Items =
            [
                new ChannelItemDto
                {
                    Id = "notif-1",
                    Title = "Review engagement letter",
                    RegardingName = "Acme Corp",
                    RegardingEntityType = "sprk_matter",
                    RegardingId = matterId,
                    Priority = "high"
                },
                new ChannelItemDto
                {
                    Id = "notif-2",
                    Title = "File response brief",
                    RegardingName = "Acme v Bravo",
                    RegardingEntityType = "sprk_matter",
                    RegardingId = taskId,
                    Priority = "normal"
                },
            ]
        };

        // Act
        var prompt = DailyBriefingEndpoints.BuildChannelNarrationPrompt(channel);

        // Assert — prompt MUST contain `regardingId={guid}` for each item that has one (FR-15).
        prompt.Should().Contain($"regardingId={matterId}");
        prompt.Should().Contain($"regardingId={taskId}");

        // Each item's `[id=… regardingId=…]` prefix is present in the per-item line.
        prompt.Should().Contain($"[id=notif-1 regardingId={matterId}]");
        prompt.Should().Contain($"[id=notif-2 regardingId={taskId}]");

        // FR-16 — instruction text MUST tell the model to use supplied regardingId, not the notification id.
        prompt.Should().Contain("Set `primaryEntityId` to the matching `regardingId`");
        prompt.Should().Contain("Do NOT use the `[id=...]` notification ID");
        prompt.Should().Contain("Do NOT invent IDs");
    }

    [Fact]
    public void BuildChannelNarrationPrompt_Omits_RegardingId_Token_When_Item_Has_No_Regarding()
    {
        // Arrange — single item with empty RegardingId. Task 030 defensive path: emit `[id={id}]`
        // WITHOUT a `regardingId=` token, so the LLM cannot echo back an empty string ID.
        var channel = new ChannelNarrationInput
        {
            Category = "system",
            Label = "System Notifications",
            Items =
            [
                new ChannelItemDto
                {
                    Id = "notif-orphan",
                    Title = "Welcome to Spaarke",
                    RegardingName = "",
                    RegardingEntityType = "",
                    RegardingId = "",
                    Priority = "normal"
                },
            ]
        };

        // Act
        var prompt = DailyBriefingEndpoints.BuildChannelNarrationPrompt(channel);

        // Assert — prefix is `[id=notif-orphan]` only; `regardingId=` token MUST be absent for this item.
        prompt.Should().Contain("[id=notif-orphan]");
        prompt.Should().NotContain("regardingId="); // No item has regardingId in this channel → no token anywhere.
    }

    // ── Tests: FR-17 — server-side validation of LLM-returned `primaryEntityId` ──────────────
    //
    // Task 031 added `BuildAllowedRegardingIdSet` + `ValidateBulletPrimaryEntityIds` (both
    // `internal static`) to enforce server-side that any `primaryEntityId` the LLM returns
    // matches a supplied `regardingId`. Defense-in-depth: even if FR-15/FR-16 prompt
    // improvements nudge the model correctly, this guarantees no hallucinated ID reaches
    // the client (frontend would render a broken link). Mock the LLM here by feeding a
    // synthetic parsed-bullet array as the function-under-test input.

    [Fact]
    public void ValidateBulletPrimaryEntityIds_Nulls_Fields_And_Logs_Warning_When_Id_Hallucinated()
    {
        // Arrange — channel with 1 supplied RegardingId (the "allowed" set).
        var suppliedId = Guid.NewGuid().ToString();
        var channel = new ChannelNarrationInput
        {
            Category = "tasks",
            Label = "Tasks Overdue",
            Items =
            [
                new ChannelItemDto
                {
                    Id = "notif-1",
                    Title = "Review document",
                    RegardingId = suppliedId,
                    RegardingEntityType = "sprk_matter",
                    RegardingName = "Acme Corp"
                }
            ]
        };
        var allowedRegardingIds = DailyBriefingEndpoints.BuildAllowedRegardingIdSet(channel);

        // Mocked LLM response: bullet whose primaryEntityId is NOT in the supplied set.
        var hallucinatedId = Guid.NewGuid().ToString();
        var bullets = new[]
        {
            new NarrativeBulletDto
            {
                Narrative = "Review the Acme Corp engagement letter today.",
                ItemIds = ["notif-1"],
                PrimaryEntityType = "sprk_matter",
                PrimaryEntityId = hallucinatedId, // hallucinated — not in allowedRegardingIds
                PrimaryEntityName = "Hallucinated Matter"
            }
        };
        var logger = new CapturingTestLogger();

        // Act
        var validated = DailyBriefingEndpoints.ValidateBulletPrimaryEntityIds(
            bullets, allowedRegardingIds, channel, logger);

        // Assert — primaryEntity* fields are nulled (string.Empty) so frontend renders no link.
        validated.Should().HaveCount(1);
        validated[0].PrimaryEntityType.Should().BeEmpty();
        validated[0].PrimaryEntityId.Should().BeEmpty();
        validated[0].PrimaryEntityName.Should().BeEmpty();

        // Narrative + itemIds preserved — only the (untrusted) ID trio is scrubbed.
        validated[0].Narrative.Should().Be("Review the Acme Corp engagement letter today.");
        validated[0].ItemIds.Should().BeEquivalentTo(new[] { "notif-1" });

        // Structured warning logged with the FR-17 marker text and the hallucinated ID for App Insights.
        logger.WarningCount.Should().Be(1);
        logger.LastWarningMessage.Should().Contain("FR-17 validation");
        logger.LastWarningMessage.Should().Contain(hallucinatedId);
    }

    [Fact]
    public void ValidateBulletPrimaryEntityIds_Retains_Fields_When_Id_Is_In_Allowed_Set()
    {
        // Arrange — channel with 1 supplied RegardingId; the LLM returns the SAME id.
        var suppliedId = Guid.NewGuid().ToString();
        var channel = new ChannelNarrationInput
        {
            Category = "tasks",
            Label = "Tasks Overdue",
            Items =
            [
                new ChannelItemDto
                {
                    Id = "notif-1",
                    Title = "Review document",
                    RegardingId = suppliedId,
                    RegardingEntityType = "sprk_matter",
                    RegardingName = "Acme Corp"
                }
            ]
        };
        var allowedRegardingIds = DailyBriefingEndpoints.BuildAllowedRegardingIdSet(channel);

        var bullets = new[]
        {
            new NarrativeBulletDto
            {
                Narrative = "Review the Acme Corp engagement letter today.",
                ItemIds = ["notif-1"],
                PrimaryEntityType = "sprk_matter",
                PrimaryEntityId = suppliedId, // valid — matches supplied regardingId
                PrimaryEntityName = "Acme Corp"
            }
        };
        var logger = new CapturingTestLogger();

        // Act
        var validated = DailyBriefingEndpoints.ValidateBulletPrimaryEntityIds(
            bullets, allowedRegardingIds, channel, logger);

        // Assert — all fields preserved; no warning emitted (happy path).
        validated.Should().HaveCount(1);
        validated[0].PrimaryEntityType.Should().Be("sprk_matter");
        validated[0].PrimaryEntityId.Should().Be(suppliedId);
        validated[0].PrimaryEntityName.Should().Be("Acme Corp");
        logger.WarningCount.Should().Be(0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Captures warning-level log messages so FR-17 validation tests can assert
    /// the structured warning was emitted (per BFF §10 bullet 6 test obligation).
    /// Mirrors the <c>TestLogger</c> pattern used by
    /// <see cref="Sprk.Bff.Api.Tests.Services.Ai.AnalysisToolDtoTests"/>.
    /// </summary>
    private sealed class CapturingTestLogger : ILogger
    {
        public int WarningCount { get; private set; }
        public string? LastWarningMessage { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
                LastWarningMessage = formatter(state, exception);
            }
        }
    }
}
