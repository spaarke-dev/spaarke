// Task 062 (round 2, finding F1) - the worklist narrative is count-only, so its request carries no item and no name.
// The client drops any /narrate answer whose entity-name scrub removed a term (fail closed), so a scrubber that removes
// ordinary count prose ("There are 3 items to decide...") would blank the narrative on every real call.
//
// These tests run the REAL EntityNameScrubber (never a mock) over representative count-only outputs, both directly
// (empty allow list = the worst case) and through DailyBriefingNarrator.NarrateAsync with a request shaped exactly like
// the one worklistNarrative.ts builds. They assert the answers come back with NO validation metadata (nothing removed),
// plus must-fire controls: a sentence naming an entity the model was not given is still scrubbed.

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

[Trait("status", "task-062")]
public sealed class WorklistNarrativeScrubTests
{
    private const string TldrActionCode = "BRIEF-NARRATE-TLDR";

    // The PR's own fixture text (worklistNarrative.test.tsx TEXT) first, then the shapes a model produces over three counts.
    public static TheoryData<string> CountOnlyOutputs => new()
    {
        "There are 3 items to decide and 5 items to do.",
        "4 of the items to do are past due.",
        "You have 3 items to decide and 5 items to do, 4 of which are past due.",
        "Today you have 8 open items: 3 to decide and 5 to do.",
        "Three items need a decision and five need action.",
        "Nothing to report today",
        "Of the 5 items to do, 4 are past due.",
        "Most items to do are past due.",
        "All 5 items to do are past due.",
        "Several items to decide are waiting.",
        "Some items to do are past due.",
        "Your worklist has 8 items.",
        "A total of 8 items are open.",
        // A word that merely STARTS with a connector ("la" in lane, "of" in office, "the" in theme) is not a connector.
        "Do lane has 5 items",
        "Do theme items are past due.",
    };

    // Lane names the model is given (the worklist category names are "Items to decide" / "Items to do" ).
    public static TheoryData<string> LaneNameOutputs => new()
    {
        "Decide lane holds 3 items",
        "Decide office items are past due.",
        "Decide theme items are past due.",
        "Decide lane holds 3 items. Do lane has 5 items.",
    };

    [Theory]
    [MemberData(nameof(CountOnlyOutputs))]
    public void Scrub_WithEmptyAllowList_RemovesNothing_FromCountOnlyProse(string text)
    {
        var result = new EntityNameScrubber().Scrub(text, []);

        result.RemovedTerms.Should().BeEmpty();
        result.ScrubbedText.Should().Be(text);
    }

    [Theory]
    [MemberData(nameof(LaneNameOutputs))]
    public async Task Narrate_OfAWorklistRequest_ReturnsNoValidationMetadata_ForLaneNameOutputs(string summary)
    {
        var sut = BuildNarrator(summary, []);

        var response = await sut.NarrateAsync(WorklistShapedRequest(), CancellationToken.None);

        response.ValidationMetadata.Should().BeNull();
        response.Tldr.Summary.Should().Be(summary);
    }

    [Theory]
    [InlineData("Acme Corp has 3 items.")]
    [InlineData("There are 3 items for Johnson & Lee.")]
    [InlineData("Davis v. Metro Transit has 2 items to do.")]
    [InlineData("May Chen has 3 items to do.")]
    [InlineData("Will Parker has 3 items to do.")]
    [InlineData("Maria de la Cruz has 3 items to do.")]
    [InlineData("Jean le Blanc has 3 items to do.")]
    [InlineData("Carlos del Rio has 3 items to do.")]
    public void Scrub_WithEmptyAllowList_StillRemoves_AnUnlistedProperName(string text)
    {
        var result = new EntityNameScrubber().Scrub(text, []);

        result.RemovedTerms.Should().NotBeEmpty();
        result.ScrubbedText.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(CountOnlyOutputs))]
    public async Task Narrate_OfAWorklistRequest_ReturnsNoValidationMetadata_ForCountOnlyOutputs(string summary)
    {
        var sut = BuildNarrator(summary, ["4 of the items to do are past due."]);

        var response = await sut.NarrateAsync(WorklistShapedRequest(), CancellationToken.None);

        response.ValidationMetadata.Should().BeNull("nothing in a count-only answer is an entity name");
        response.Tldr.Summary.Should().Be(summary);
    }

    [Theory]
    [InlineData("Items to decide: 3.")]
    [InlineData("You have 3 Items to decide and 5 Items to do.")]
    public async Task Narrate_OfAWorklistRequest_AllowsTheCategoryNamesItWasGiven(string summary)
    {
        var sut = BuildNarrator(summary, []);

        var response = await sut.NarrateAsync(WorklistShapedRequest(), CancellationToken.None);

        response.ValidationMetadata.Should().BeNull();
    }

    [Fact]
    public async Task Narrate_OfAWorklistRequest_MustFire_ScrubsAnEntityTheModelWasNotGiven()
    {
        var sut = BuildNarrator("Acme Corp has 3 items to decide.", ["Johnson & Lee has 5 items to do."]);

        var response = await sut.NarrateAsync(WorklistShapedRequest(), CancellationToken.None);

        response.ValidationMetadata.Should().NotBeNull();
        response.ValidationMetadata!.RemovedTerms.Should().Contain("Acme Corp");
        response.ValidationMetadata.RemovedTerms.Should().Contain("Johnson & Lee");
    }

    [Fact]
    public async Task Narrate_AllowsRecordNamesInTheRequestsTldrFacts()
    {
        var req = WorklistShapedRequest() with
        {
            TldrFacts = new TldrFactsDto { TotalNotificationCount = 8, RecordNames = ["Acme Corp"] }
        };
        var sut = BuildNarrator("Acme Corp has 3 items to decide.", []);

        var response = await sut.NarrateAsync(req, CancellationToken.None);

        response.ValidationMetadata.Should().BeNull("Acme Corp is a record name the request itself carried");
    }

    // ─── Test infrastructure ──────────────────────────────────────────────────────────────

    /// <summary>The request worklistNarrative.ts builds: category counts only, no item, no channel, no record name.</summary>
    private static DailyBriefingNarrateRequest WorklistShapedRequest()
    {
        NotificationCategoryDto[] categories =
        [
            new() { Name = "Items to decide", Count = 3, UnreadCount = 3 },
            new() { Name = "Items to do", Count = 5, UnreadCount = 5 },
            new() { Name = "Items to do that are past due", Count = 4, UnreadCount = 4 },
        ];
        return new DailyBriefingNarrateRequest
        {
            Categories = categories,
            PriorityItems = [],
            TotalNotificationCount = 8,
            Channels = [],
            TldrFacts = new TldrFactsDto
            {
                TotalNotificationCount = 8,
                CategoryCounts = categories,
                PriorityItemCount = 0,
                KeyDates = [],
                RecordNames = [],
            },
        };
    }

    private static DailyBriefingNarrator BuildNarrator(string tldrSummary, string[] keyTakeaways)
    {
        var actions = new Mock<AnalysisActionService>(MockBehavior.Loose,
            new HttpClient { BaseAddress = new Uri("https://example.crm.dynamics.com/api/data/v9.2/") },
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dataverse:ServiceUrl"] = "https://example.crm.dynamics.com/api/data/v9.2/"
                })
                .Build(),
            new TestNoopTokenCredential(),
            NullLogger<AnalysisActionService>.Instance);

        actions.Setup(s => s.GetActionByCodeAsync(TldrActionCode, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AnalysisAction
               {
                   Id = Guid.NewGuid(),
                   Name = TldrActionCode,
                   SystemPrompt = "TLDR.",
                   OutputSchemaJson = """{"type":"object","properties":{"summary":{"type":"string"},"keyTakeaways":{"type":"array","items":{"type":"string"}}},"required":["summary","keyTakeaways"],"additionalProperties":false}""",
                   SortOrder = 0,
                   ExecutorType = ExecutorType.AiAnalysis,
                   OwnerType = ScopeOwnerType.System,
                   Temperature = 0.0m
               });

        var tldrJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["summary"] = tldrSummary,
            ["keyTakeaways"] = keyTakeaways,
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

        // The REAL scrubber: this suite exists to prove the production algorithm, not a stand-in for it.
        return new DailyBriefingNarrator(actions.Object, llm.Object, new EntityNameScrubber(),
            NullLogger<DailyBriefingNarrator>.Instance);
    }

    private sealed class TestNoopTokenCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new Azure.Core.AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1)));
    }
}
