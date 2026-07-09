// R5 task 014 (FR-A5) — binary anchor resolution: TldrResult.ItemRefs[] grounding.
//
// DailyBriefingNarrator.BuildTldrItemRefs is a DETERMINISTIC, server-side-only pass — never
// asked of or trusted from the LLM (TldrFactsDto, the TL;DR call's entire input, carries no
// item ids — see the method's XML doc). This suite drives the narrator end-to-end through
// NarrateAsync (same integration-heavy boundary-mock pattern as
// DailyBriefingNarratorEntityLinkTests), controlling the LLM's returned TL;DR text per case
// and asserting the resulting TldrResult.ItemRefs[].
//
// Per FR-A6 (no groundedness threshold / warn-withhold band), there is no "partial match" or
// "low confidence" itemRefs entry — an item is either linked (its name matched verbatim in the
// TL;DR text, so an entry with a real itemId is emitted) or it isn't (no entry at all). The
// widget-side "drop a non-resolving anchor" behavior (the other half of FR-A5) is covered by
// TldrSection.test.tsx — this suite only covers the server's construction of ItemRefs[].

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Narrators;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Narrators;

[Trait("status", "task-014-r5")]
public sealed class DailyBriefingNarratorItemRefsTests
{
    private const string TldrActionCode = "BRIEF-NARRATE-TLDR";

    [Fact]
    public async Task NarrateAsync_PairsAnchorWithItemId_WhenChannelItemRegardingNameAppearsVerbatimInTldrSummary()
    {
        var item = new ChannelItemDto
        {
            Id = "item-1",
            Title = "Review engagement letter",
            RegardingName = "Acme Matter",
            RegardingEntityType = "sprk_matter",
            RegardingId = "11111111-1111-1111-1111-111111111111",
            SourceEntityType = "sprk_event"
        };
        var req = BuildRequest(item);

        var sut = BuildNarrator(tldrSummary: "You have 1 notification: a follow-up on Acme Matter.");

        var response = await sut.NarrateAsync(req, CancellationToken.None);

        response.Tldr.ItemRefs.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { AnchorText = "Acme Matter", ItemId = "item-1" });
    }

    [Fact]
    public async Task NarrateAsync_LeavesItemRefsEmpty_WhenNoChannelItemNameAppearsInTldrText()
    {
        var item = new ChannelItemDto
        {
            Id = "item-1",
            Title = "Review engagement letter",
            RegardingName = "Acme Matter",
            RegardingEntityType = "sprk_matter",
            RegardingId = "11111111-1111-1111-1111-111111111111",
            SourceEntityType = "sprk_event"
        };
        var req = BuildRequest(item);

        // Summary names no specific record at all — generic prose only.
        var sut = BuildNarrator(tldrSummary: "You have 1 notification today.");

        var response = await sut.NarrateAsync(req, CancellationToken.None);

        response.Tldr.ItemRefs.Should().BeEmpty(
            because: "binary resolution — no channel item name matched verbatim, so no itemRefs entry is fabricated");
    }

    [Fact]
    public async Task NarrateAsync_LeavesItemRefsEmpty_WhenTheMatchedChannelItemHasNoId()
    {
        var item = new ChannelItemDto
        {
            Id = "", // no id to link to
            Title = "Review engagement letter",
            RegardingName = "Acme Matter",
            RegardingEntityType = "sprk_matter",
            RegardingId = "11111111-1111-1111-1111-111111111111",
            SourceEntityType = "sprk_event"
        };
        var req = BuildRequest(item);

        var sut = BuildNarrator(tldrSummary: "A follow-up on Acme Matter is due.");

        var response = await sut.NarrateAsync(req, CancellationToken.None);

        response.Tldr.ItemRefs.Should().BeEmpty(
            because: "an item with no Id has nothing to link to, even when its name is named in the TL;DR text");
    }

    [Fact]
    public async Task NarrateAsync_PopulatesItemRefsFromKeyTakeawaysAndTopAction_NotOnlySummary()
    {
        var item = new ChannelItemDto
        {
            Id = "doc-1",
            Title = "NDA draft.docx",
            RegardingName = "Gamma Matter",
            RegardingEntityType = "sprk_matter",
            RegardingId = "22222222-2222-2222-2222-222222222222",
            SourceEntityType = "sprk_document"
        };
        var req = BuildRequest(item);

        var sut = BuildNarrator(
            tldrSummary: "You have 1 notification today.",
            tldrKeyTakeaways: ["Gamma Matter has a new draft awaiting review."],
            tldrTopAction: "Review the NDA draft.docx for Gamma Matter.");

        var response = await sut.NarrateAsync(req, CancellationToken.None);

        response.Tldr.ItemRefs.Should().ContainSingle(
            r => r.AnchorText == "Gamma Matter" && r.ItemId == "doc-1",
            because: "the match is drawn from the takeaway/topAction text, not just the summary field");
    }

    // ─── Test infrastructure ──────────────────────────────────────────────────────────────

    private static DailyBriefingNarrateRequest BuildRequest(ChannelItemDto item) => new()
    {
        Categories = [new NotificationCategoryDto { Name = item.SourceEntityType, Count = 1, UnreadCount = 1 }],
        PriorityItems = [],
        TotalNotificationCount = 1,
        Channels =
        [
            new ChannelNarrationInput
            {
                Category = item.SourceEntityType,
                Label = item.SourceEntityType,
                Items = [item]
            }
        ]
    };

    private static DailyBriefingNarrator BuildNarrator(
        string tldrSummary,
        string[]? tldrKeyTakeaways = null,
        string tldrTopAction = "")
    {
        var actions = new Mock<AnalysisActionService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://example.crm.dynamics.com/api/data/v9.2/") },
            BuildTestConfiguration(),
            new TestNoopTokenCredential(),
            NullLogger<AnalysisActionService>.Instance);

        actions.Setup(s => s.GetActionByCodeAsync(TldrActionCode, It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeTldrAction());

        var tldrJson = JsonSerializer.Serialize(new
        {
            summary = tldrSummary,
            keyTakeaways = tldrKeyTakeaways ?? Array.Empty<string>(),
            topAction = tldrTopAction
        });

        var llm = new Mock<IOpenAiClient>(MockBehavior.Strict);
        llm.Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                TldrActionCode.Replace('-', '_'),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tldrJson);

        var scrubber = new Mock<IEntityNameScrubber>(MockBehavior.Loose);
        scrubber.Setup(s => s.Scrub(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns(new EntityNameScrubResult { ScrubbedText = string.Empty, RemovedTerms = Array.Empty<string>() });

        return new DailyBriefingNarrator(actions.Object, llm.Object, scrubber.Object,
            NullLogger<DailyBriefingNarrator>.Instance);
    }

    private static AnalysisAction MakeTldrAction() => new()
    {
        Id = Guid.NewGuid(),
        Name = TldrActionCode,
        SystemPrompt = "TLDR.",
        OutputSchemaJson = """{"type":"object","properties":{"summary":{"type":"string"},"keyTakeaways":{"type":"array","items":{"type":"string"}},"topAction":{"type":"string"}},"required":["summary","keyTakeaways","topAction"],"additionalProperties":false}""",
        SortOrder = 0,
        ExecutorType = ExecutorType.AiAnalysis,
        OwnerType = ScopeOwnerType.System,
        Temperature = 0.0m
    };

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
