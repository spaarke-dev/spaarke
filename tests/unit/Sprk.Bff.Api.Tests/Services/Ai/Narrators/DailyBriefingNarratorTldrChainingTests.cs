// R7 Wave 12 T132 — TLDR ↔ Activity Notes consistency tests.
//
// The narrator chains the TLDR result (Step 2 of the pipeline) into the
// per-channel narrative calls (Step 3) so the LLM can ensure its narrative
// bullets cover items mentioned in TLDR.keyTakeaways / TLDR.topAction.
//
// These tests anchor that contract behavior at the IOpenAiClient boundary —
// they assert that:
//   1. Once the TLDR call returns, EACH per-channel call's prompt contains
//      the TLDR's summary / keyTakeaways / topAction (so the LLM sees them).
//   2. The TLDR is the SAME for every per-channel call in a single Narrate
//      invocation (chaining, not per-channel regeneration).
//
// We mock IOpenAiClient at the boundary (ADR-038 §1 — integration-heavy
// pyramid; mock at module boundary, assert behavior the caller would notice).
// The behavior the caller (operator UAT) notices is "TLDR-referenced items
// have details in Activity Notes" — at the C# layer this surfaces as
// "the TLDR fields are visible in the channel-narrative LLM prompt".

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Narrators;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Narrators;

[Trait("status", "task-132-r7")]
public sealed class DailyBriefingNarratorTldrChainingTests
{
    private const string TldrActionCode    = "BRIEF-NARRATE-TLDR";
    private const string ChannelActionCode = "BRIEF-NARRATE-CHANNEL";

    // ─── Action-row stand-ins (mirrors what AnalysisActionService.GetActionByCodeAsync returns) ───

    private static AnalysisAction MakeTldrAction() => new()
    {
        Id = Guid.NewGuid(),
        Name = "BRIEF-NARRATE-TLDR",
        SystemPrompt = "You are a TLDR generator.",
        OutputSchemaJson = """{ "type":"object", "properties": { "summary":{"type":"string"}, "keyTakeaways":{"type":"array","items":{"type":"string"}}, "topAction":{"type":"string"} }, "required":["summary","keyTakeaways","topAction"], "additionalProperties":false }""",
        SortOrder = 0,
        ExecutorType = ExecutorType.AiAnalysis,
        OwnerType = ScopeOwnerType.System,
        Temperature = 0.0m
    };

    private static AnalysisAction MakeChannelAction() => new()
    {
        Id = Guid.NewGuid(),
        Name = "BRIEF-NARRATE-CHANNEL",
        SystemPrompt = "You are a per-channel narrator. Use the supplied tldr as a coverage hint.",
        OutputSchemaJson = """{ "type":"object", "properties": { "channel":{"type":"string"}, "narrative":{"type":"array","items":{"type":"string"}} }, "required":["channel","narrative"], "additionalProperties":false }""",
        SortOrder = 0,
        ExecutorType = ExecutorType.AiAnalysis,
        OwnerType = ScopeOwnerType.System,
        Temperature = 0.0m
    };

    private static DailyBriefingNarrateRequest MakeRequestWithTwoChannels() => new()
    {
        Categories = [
            new NotificationCategoryDto { Name = "Tasks", Count = 1, UnreadCount = 1 }
        ],
        PriorityItems = [
            new PriorityItemDto { Category = "Tasks", Title = "Review engagement letter" }
        ],
        TotalNotificationCount = 2,
        Channels =
        [
            new ChannelNarrationInput
            {
                Category = "tasks",
                Label = "Tasks Overdue",
                Items = [
                    new ChannelItemDto
                    {
                        Id = "item-tasks-1",
                        Title = "Acme contract renewal",
                        RegardingName = "Acme Corp",
                        RegardingEntityType = "sprk_matter",
                        RegardingId = Guid.NewGuid().ToString()
                    }
                ]
            },
            new ChannelNarrationInput
            {
                Category = "documents",
                Label = "Documents",
                Items = [
                    new ChannelItemDto
                    {
                        Id = "item-doc-1",
                        Title = "Bravo brief.docx",
                        RegardingName = "Bravo Matter",
                        RegardingEntityType = "sprk_document",
                        RegardingId = Guid.NewGuid().ToString()
                    }
                ]
            }
        ]
    };

    // ─── Test 1: TLDR fields visible in every per-channel LLM prompt ───

    [Fact]
    public async Task NarrateAsync_PassesTldrSummaryKeytakeawaysAndTopActionToEveryChannelLlmCall()
    {
        // Arrange — TLDR LLM returns canonical structured response; channel LLM is
        // captured per call so we can assert chaining of TLDR content into the prompt.
        var req = MakeRequestWithTwoChannels();

        const string tldrSummary     = "Three urgent matters need attention today.";
        const string tldrTakeaway1   = "Acme contract overdue";
        const string tldrTakeaway2   = "Bravo brief due tomorrow";
        const string tldrTopAction   = "Review the Acme engagement letter (2 days overdue).";

        var tldrResponseJson = JsonSerializer.Serialize(new
        {
            summary       = tldrSummary,
            keyTakeaways  = new[] { tldrTakeaway1, tldrTakeaway2 },
            topAction     = tldrTopAction
        });

        var actions = new Mock<AnalysisActionService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://example.crm.dynamics.com/api/data/v9.2/") },
            BuildTestConfiguration(),
            new TestNoopTokenCredential(),
            NullLogger<AnalysisActionService>.Instance);
        actions.Setup(s => s.GetActionByCodeAsync(TldrActionCode, It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeTldrAction());
        actions.Setup(s => s.GetActionByCodeAsync(ChannelActionCode, It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeChannelAction());

        var llm = new Mock<IOpenAiClient>(MockBehavior.Strict);
        var capturedChannelPrompts = new List<string>();

        // TLDR call (schemaName == TldrActionCode with hyphens → underscores)
        llm.Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                TldrActionCode.Replace('-', '_'),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tldrResponseJson);

        // Channel call — capture EACH prompt for assertion.
        llm.Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                ChannelActionCode.Replace('-', '_'),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, BinaryData, string, string?, int?, float?, CancellationToken>(
                (prompt, _, _, _, _, _, _) =>
                {
                    lock (capturedChannelPrompts) { capturedChannelPrompts.Add(prompt); }
                })
            .ReturnsAsync((string _, BinaryData _, string _, string? _, int? _, float? _, CancellationToken _) =>
                JsonSerializer.Serialize(new
                {
                    channel = "any",
                    narrative = new[] { "covers Acme contract", "covers Bravo brief" }
                }));

        var scrubber = new Mock<IEntityNameScrubber>(MockBehavior.Loose);
        scrubber.Setup(s => s.Scrub(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns(new EntityNameScrubResult
                {
                    ScrubbedText = "x",
                    RemovedTerms = Array.Empty<string>()
                });

        var sut = new DailyBriefingNarrator(
            actions.Object,
            llm.Object,
            scrubber.Object,
            NullLogger<DailyBriefingNarrator>.Instance);

        // Act
        var response = await sut.NarrateAsync(req, CancellationToken.None);

        // Assert — one channel prompt per input channel; each MUST contain TLDR fields.
        capturedChannelPrompts.Should().HaveCount(req.Channels.Length,
            because: "narrator fans out one LLM call per input channel");

        foreach (var prompt in capturedChannelPrompts)
        {
            prompt.Should().Contain(tldrSummary,
                because: "channel prompt must include TLDR.summary as coverage context");
            prompt.Should().Contain(tldrTakeaway1,
                because: "channel prompt must include each TLDR keyTakeaway");
            prompt.Should().Contain(tldrTakeaway2,
                because: "channel prompt must include each TLDR keyTakeaway");
            prompt.Should().Contain(tldrTopAction,
                because: "channel prompt must include TLDR.topAction");

            // Belt-and-suspenders: the chaining is implemented as a `tldr` JSON
            // field inside the per-channel ## Input payload — assert that shape
            // explicitly so renaming the field shows up as a test failure.
            prompt.Should().Contain("\"tldr\"",
                because: "channel payload exposes the TLDR via a `tldr` JSON field");
        }

        // Response shape preserved.
        response.Should().NotBeNull();
        response.Tldr.Summary.Should().Be(tldrSummary);
        response.Tldr.KeyTakeaways.Should().BeEquivalentTo(new[] { tldrTakeaway1, tldrTakeaway2 });
        response.Tldr.TopAction.Should().Be(tldrTopAction);
        response.ChannelNarratives.Should().HaveCount(req.Channels.Length);
    }

    // ─── Test 2: every channel sees the SAME TLDR (no per-channel regeneration) ───

    [Fact]
    public async Task NarrateAsync_PassesIdenticalTldrContextToEachChannelCall_NoPerChannelRegeneration()
    {
        // Arrange
        var req = MakeRequestWithTwoChannels();

        var tldrResponseJson = JsonSerializer.Serialize(new
        {
            summary       = "ONE-AND-ONLY-SUMMARY",
            keyTakeaways  = new[] { "ONE-AND-ONLY-TAKEAWAY" },
            topAction     = "ONE-AND-ONLY-ACTION"
        });

        var actions = new Mock<AnalysisActionService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://example.crm.dynamics.com/api/data/v9.2/") },
            BuildTestConfiguration(),
            new TestNoopTokenCredential(),
            NullLogger<AnalysisActionService>.Instance);
        actions.Setup(s => s.GetActionByCodeAsync(TldrActionCode, It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeTldrAction());
        actions.Setup(s => s.GetActionByCodeAsync(ChannelActionCode, It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeChannelAction());

        var llm = new Mock<IOpenAiClient>(MockBehavior.Strict);
        var tldrCallCount     = 0;
        var capturedChannelPrompts = new List<string>();

        llm.Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                TldrActionCode.Replace('-', '_'),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref tldrCallCount))
            .ReturnsAsync(tldrResponseJson);

        llm.Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                ChannelActionCode.Replace('-', '_'),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, BinaryData, string, string?, int?, float?, CancellationToken>(
                (prompt, _, _, _, _, _, _) =>
                {
                    lock (capturedChannelPrompts) { capturedChannelPrompts.Add(prompt); }
                })
            .ReturnsAsync(JsonSerializer.Serialize(new { channel = "x", narrative = new[] { "ok" } }));

        var scrubber = new Mock<IEntityNameScrubber>(MockBehavior.Loose);
        scrubber.Setup(s => s.Scrub(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns(new EntityNameScrubResult { ScrubbedText = "x", RemovedTerms = Array.Empty<string>() });

        var sut = new DailyBriefingNarrator(
            actions.Object,
            llm.Object,
            scrubber.Object,
            NullLogger<DailyBriefingNarrator>.Instance);

        // Act
        _ = await sut.NarrateAsync(req, CancellationToken.None);

        // Assert — TLDR called exactly ONCE; the sentinel strings appear in every
        // per-channel prompt (so each channel saw the same TLDR result).
        tldrCallCount.Should().Be(1,
            because: "TLDR is computed once and chained, not regenerated per channel");

        capturedChannelPrompts.Should().HaveCount(req.Channels.Length);
        capturedChannelPrompts.Should().AllSatisfy(prompt =>
        {
            prompt.Should().Contain("ONE-AND-ONLY-SUMMARY");
            prompt.Should().Contain("ONE-AND-ONLY-TAKEAWAY");
            prompt.Should().Contain("ONE-AND-ONLY-ACTION");
        });
    }

    // ─── Test infra: minimal TokenCredential stand-in so AnalysisActionService base ctor
    //                doesn't blow up. The base ctor stores the credential; we never call it
    //                because every public method on AnalysisActionService used by the narrator
    //                is mocked (GetActionByCodeAsync). Avoids depending on Azure.Identity
    //                test doubles for a behavior test that lives strictly above that boundary.

    private static IConfiguration BuildTestConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://example.crm.dynamics.com/api/data/v9.2/"
            })
            .Build();

    private sealed class TestNoopTokenCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new Azure.Core.AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1)));
    }
}
