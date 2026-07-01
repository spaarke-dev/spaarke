// R7 Wave 12 task 135 — Per-bullet entity-link resolution across all 6 entity types.
//
// Behavior contract under test (the operator-facing wave12 plan §2.1 AC5):
//   "Each Activity Notes bullet has working entity link" — i.e., every rendered
//   NarrativeBulletDto MUST have PrimaryEntityType/Id/Name populated so the
//   widget can build a click-through to the underlying Dataverse record.
//
// Source of the gap (pre-task-135): EnrichBulletWithEntityRefs only checked
// RegardingId; for orphan items (no regarding matter — common for sprk_event
// tasks with no sprk_regardingmatter, and for sprk_todo with no regarding),
// PrimaryEntityType/Id came out empty and the widget hid the link.
//
// Fix verified here: when no match has a populated RegardingId, the enrichment
// falls back to the source entity (SourceEntityType + Id + Title) so the
// widget can still navigate.
//
// We drive the narrator end-to-end through NarrateAsync (mocking only the LLM
// at the IOpenAiClient boundary, per ADR-038 §1 — integration-heavy pyramid,
// mock at module boundary, assert observable behavior). The LLM is stubbed to
// emit a narrative that mentions a specific item's title, then we assert the
// resulting NarrativeBulletDto carries the correct PrimaryEntity* fields for
// each of the 6 entity types operator-configured in T131.

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

[Trait("status", "task-135-r7")]
public sealed class DailyBriefingNarratorEntityLinkTests
{
    private const string TldrActionCode = "BRIEF-NARRATE-TLDR";
    private const string ChannelActionCode = "BRIEF-NARRATE-CHANNEL";

    // ─── Per-entity-type theory data ────────────────────────────────────────
    //
    // For each of the 6 channels T131 introduced, we synthesize a single-item
    // request, stub the channel LLM to return a narrative mentioning the item
    // by Title, and assert the resulting bullet's PrimaryEntity* fields match
    // what the widget needs to navigate.
    //
    // Cases:
    //   1. sprk_event (Task) with regarding matter   → link points to matter
    //   2. sprk_document with regarding matter       → link points to matter
    //   3. sprk_matter (self-regarding)              → link points to matter
    //   4. sprk_project (self-regarding)             → link points to project
    //   5. sprk_todo with regarding matter           → link points to matter
    //   6. sprk_event (orphan: no regarding matter)  → link FALLS BACK to event
    //   7. sprk_todo (orphan)                        → link FALLS BACK to todo

    public static TheoryData<EntityLinkCase> EntityLinkCases() => new()
    {
        // 1. sprk_event with regarding matter — most common Task case.
        new EntityLinkCase
        {
            Name = "sprk_event task with regarding matter links to matter",
            Item = new ChannelItemDto
            {
                Id = "event-1",
                Title = "Review engagement letter",
                RegardingName = "Acme Corp Matter",
                RegardingEntityType = "sprk_matter",
                RegardingId = "11111111-1111-1111-1111-111111111111",
                SourceEntityType = "sprk_event"
            },
            NarrativeMentionsTitle = true,
            ExpectedPrimaryEntityType = "sprk_matter",
            ExpectedPrimaryEntityId = "11111111-1111-1111-1111-111111111111",
            ExpectedPrimaryEntityName = "Acme Corp Matter"
        },

        // 2. sprk_document with regarding matter.
        new EntityLinkCase
        {
            Name = "sprk_document with regarding matter links to matter",
            Item = new ChannelItemDto
            {
                Id = "doc-1",
                Title = "Bravo brief.docx",
                RegardingName = "Bravo Matter",
                RegardingEntityType = "sprk_matter",
                RegardingId = "22222222-2222-2222-2222-222222222222",
                SourceEntityType = "sprk_document"
            },
            NarrativeMentionsTitle = true,
            ExpectedPrimaryEntityType = "sprk_matter",
            ExpectedPrimaryEntityId = "22222222-2222-2222-2222-222222222222",
            ExpectedPrimaryEntityName = "Bravo Matter"
        },

        // 3. sprk_matter (self-regarding — collector sets RegardingId == EntityId).
        new EntityLinkCase
        {
            Name = "sprk_matter self-regarding links to itself",
            Item = new ChannelItemDto
            {
                Id = "matter-1",
                Title = "Charlie Litigation Matter",
                RegardingName = "Charlie Litigation Matter",
                RegardingEntityType = "sprk_matter",
                RegardingId = "33333333-3333-3333-3333-333333333333",
                SourceEntityType = "sprk_matter"
            },
            NarrativeMentionsTitle = true,
            ExpectedPrimaryEntityType = "sprk_matter",
            ExpectedPrimaryEntityId = "33333333-3333-3333-3333-333333333333",
            ExpectedPrimaryEntityName = "Charlie Litigation Matter"
        },

        // 4. sprk_project (self-regarding — collector sets RegardingId == EntityId
        //    AND RegardingEntityType == sprk_project, NOT sprk_matter).
        new EntityLinkCase
        {
            Name = "sprk_project self-regarding links to itself with project entity type",
            Item = new ChannelItemDto
            {
                Id = "project-1",
                Title = "Delta Initiative Project",
                RegardingName = "Delta Initiative Project",
                RegardingEntityType = "sprk_project",
                RegardingId = "44444444-4444-4444-4444-444444444444",
                SourceEntityType = "sprk_project"
            },
            NarrativeMentionsTitle = true,
            ExpectedPrimaryEntityType = "sprk_project",
            ExpectedPrimaryEntityId = "44444444-4444-4444-4444-444444444444",
            ExpectedPrimaryEntityName = "Delta Initiative Project"
        },

        // 5. sprk_todo with regarding matter.
        new EntityLinkCase
        {
            Name = "sprk_todo with regarding matter links to matter",
            Item = new ChannelItemDto
            {
                Id = "todo-1",
                Title = "Call opposing counsel about Echo Settlement",
                RegardingName = "Echo Settlement Matter",
                RegardingEntityType = "sprk_matter",
                RegardingId = "55555555-5555-5555-5555-555555555555",
                SourceEntityType = "sprk_todo"
            },
            NarrativeMentionsTitle = true,
            ExpectedPrimaryEntityType = "sprk_matter",
            ExpectedPrimaryEntityId = "55555555-5555-5555-5555-555555555555",
            ExpectedPrimaryEntityName = "Echo Settlement Matter"
        },

        // 6. sprk_event ORPHAN (no regarding matter) — pre-task-135 BUG path.
        //    Tier 2 fallback should kick in: link points to the source event row.
        new EntityLinkCase
        {
            Name = "orphan sprk_event task falls back to source event row",
            Item = new ChannelItemDto
            {
                Id = "event-orphan-1",
                Title = "Submit independent CLE attestation",
                RegardingName = "",            // no regarding matter
                RegardingEntityType = "",
                RegardingId = "",
                SourceEntityType = "sprk_event"
            },
            NarrativeMentionsTitle = true,
            ExpectedPrimaryEntityType = "sprk_event",
            ExpectedPrimaryEntityId = "event-orphan-1",
            ExpectedPrimaryEntityName = "Submit independent CLE attestation"
        },

        // 7. sprk_todo ORPHAN (no regarding matter) — pre-task-135 BUG path.
        //    Tier 2 fallback should kick in: link points to the source todo row.
        new EntityLinkCase
        {
            Name = "orphan sprk_todo falls back to source todo row",
            Item = new ChannelItemDto
            {
                Id = "todo-orphan-1",
                Title = "Reschedule personal dentist appointment",
                RegardingName = "",
                RegardingEntityType = "",
                RegardingId = "",
                SourceEntityType = "sprk_todo"
            },
            NarrativeMentionsTitle = true,
            ExpectedPrimaryEntityType = "sprk_todo",
            ExpectedPrimaryEntityId = "todo-orphan-1",
            ExpectedPrimaryEntityName = "Reschedule personal dentist appointment"
        }
    };

    [Theory]
    [MemberData(nameof(EntityLinkCases))]
    public async Task NarrateAsync_PopulatesPrimaryEntityFields_ForEachEntityType(EntityLinkCase tc)
    {
        // Arrange — build a single-channel request carrying just the one item.
        var req = new DailyBriefingNarrateRequest
        {
            Categories =
            [
                new NotificationCategoryDto { Name = tc.Item.SourceEntityType, Count = 1, UnreadCount = 1 }
            ],
            PriorityItems =
            [
                new PriorityItemDto { Category = tc.Item.SourceEntityType, Title = tc.Item.Title }
            ],
            TotalNotificationCount = 1,
            Channels =
            [
                new ChannelNarrationInput
                {
                    Category = tc.Item.SourceEntityType,
                    Label = tc.Item.SourceEntityType,
                    Items = [tc.Item]
                }
            ]
        };

        var tldrJson = JsonSerializer.Serialize(new
        {
            summary = "summary",
            keyTakeaways = new[] { "k1" },
            topAction = "action"
        });

        // Channel LLM stub — emit a narrative that mentions the item's Title so
        // EnrichBulletWithEntityRefs's substring matcher fires.
        var narrative = tc.NarrativeMentionsTitle
            ? $"You should {tc.Item.Title} as your top priority."
            : "Some unrelated text that mentions nothing.";

        var channelJson = JsonSerializer.Serialize(new
        {
            channel = tc.Item.SourceEntityType,
            narrative = new[] { narrative }
        });

        var (actions, llm, scrubber) = BuildBoundaryMocks(tldrJson, channelJson);

        var sut = new DailyBriefingNarrator(
            actions.Object,
            llm.Object,
            scrubber.Object,
            NullLogger<DailyBriefingNarrator>.Instance);

        // Act
        var response = await sut.NarrateAsync(req, CancellationToken.None);

        // Assert — exactly one channel narrative with one bullet; bullet's
        // PrimaryEntity* fields point to the expected target.
        response.ChannelNarratives.Should().HaveCount(1, because: tc.Name);
        var bullets = response.ChannelNarratives[0].Bullets;
        bullets.Should().HaveCount(1, because: tc.Name);

        var bullet = bullets[0];
        bullet.Narrative.Should().Be(narrative, because: tc.Name);
        bullet.PrimaryEntityType.Should().Be(tc.ExpectedPrimaryEntityType, because: tc.Name);
        bullet.PrimaryEntityId.Should().Be(tc.ExpectedPrimaryEntityId, because: tc.Name);
        bullet.PrimaryEntityName.Should().Be(tc.ExpectedPrimaryEntityName, because: tc.Name);

        // The ItemIds collection includes the matched item's Id regardless of
        // tier — widget uses this for "Add To Do" + "Dismiss" actions, both
        // of which key off the SOURCE row id (not the regarding row id).
        bullet.ItemIds.Should().Contain(tc.Item.Id, because: tc.Name);
    }

    // ─── Negative path: narrative mentions nothing → bullet has no link ─────

    [Fact]
    public async Task NarrateAsync_LeavesBulletUnlinked_WhenNarrativeDoesNotMentionAnyItemName()
    {
        var item = new ChannelItemDto
        {
            Id = "event-x",
            Title = "Some specific task name",
            RegardingName = "Some Matter Name",
            RegardingEntityType = "sprk_matter",
            RegardingId = Guid.NewGuid().ToString(),
            SourceEntityType = "sprk_event"
        };

        var req = new DailyBriefingNarrateRequest
        {
            Categories = [new NotificationCategoryDto { Name = "tasks", Count = 1, UnreadCount = 1 }],
            PriorityItems = [new PriorityItemDto { Category = "tasks", Title = item.Title }],
            TotalNotificationCount = 1,
            Channels =
            [
                new ChannelNarrationInput { Category = "tasks", Label = "Tasks", Items = [item] }
            ]
        };

        var tldrJson = JsonSerializer.Serialize(new { summary = "s", keyTakeaways = new[] { "k" }, topAction = "a" });

        // Narrative mentions NEITHER the item title NOR the regarding name.
        var channelJson = JsonSerializer.Serialize(new
        {
            channel = "tasks",
            narrative = new[] { "Generic prose that references no specific entity at all." }
        });

        var (actions, llm, scrubber) = BuildBoundaryMocks(tldrJson, channelJson);

        var sut = new DailyBriefingNarrator(actions.Object, llm.Object, scrubber.Object,
            NullLogger<DailyBriefingNarrator>.Instance);

        var response = await sut.NarrateAsync(req, CancellationToken.None);

        response.ChannelNarratives.Should().HaveCount(1);
        var bullet = response.ChannelNarratives[0].Bullets.Single();
        bullet.PrimaryEntityType.Should().BeEmpty(
            because: "no item name was mentioned, so no entity-link resolution is possible");
        bullet.PrimaryEntityId.Should().BeEmpty();
        bullet.PrimaryEntityName.Should().BeEmpty();
    }

    // ─── Mixed channel: one orphan + one regarding-matter item ──────────────
    // Tier 1 should win — the matter-linked match comes first in the resolution
    // order even if the orphan is earlier in the items array.

    [Fact]
    public async Task NarrateAsync_PrefersRegardingMatch_OverOrphanFallback_WhenBothItemsMatch()
    {
        // Orphan item (no regarding) listed FIRST.
        var orphan = new ChannelItemDto
        {
            Id = "event-orphan",
            Title = "Alpha task",
            RegardingName = "",
            RegardingEntityType = "",
            RegardingId = "",
            SourceEntityType = "sprk_event"
        };
        // Regarding-matter item listed SECOND.
        var regarding = new ChannelItemDto
        {
            Id = "event-regarding",
            Title = "Beta task",
            RegardingName = "Beta Matter",
            RegardingEntityType = "sprk_matter",
            RegardingId = "99999999-9999-9999-9999-999999999999",
            SourceEntityType = "sprk_event"
        };

        var req = new DailyBriefingNarrateRequest
        {
            Categories = [new NotificationCategoryDto { Name = "tasks", Count = 2, UnreadCount = 2 }],
            PriorityItems = [new PriorityItemDto { Category = "tasks", Title = orphan.Title }],
            TotalNotificationCount = 2,
            Channels =
            [
                new ChannelNarrationInput
                {
                    Category = "tasks",
                    Label = "Tasks",
                    Items = [orphan, regarding]  // orphan FIRST
                }
            ]
        };

        // Narrative mentions BOTH titles. Resolution should still prefer
        // the regarding-matter match (Tier 1) over the orphan (Tier 2),
        // because Tier 1 produces a richer click-through to the parent matter.
        var tldrJson = JsonSerializer.Serialize(new { summary = "s", keyTakeaways = new[] { "k" }, topAction = "a" });
        var channelJson = JsonSerializer.Serialize(new
        {
            channel = "tasks",
            narrative = new[] { $"Both {orphan.Title} and {regarding.Title} need attention." }
        });

        var (actions, llm, scrubber) = BuildBoundaryMocks(tldrJson, channelJson);
        var sut = new DailyBriefingNarrator(actions.Object, llm.Object, scrubber.Object,
            NullLogger<DailyBriefingNarrator>.Instance);

        var response = await sut.NarrateAsync(req, CancellationToken.None);

        var bullet = response.ChannelNarratives[0].Bullets.Single();
        bullet.PrimaryEntityType.Should().Be("sprk_matter",
            because: "Tier 1 (regarding-matter) wins over Tier 2 (orphan source-entity fallback)");
        bullet.PrimaryEntityId.Should().Be(regarding.RegardingId);
        bullet.PrimaryEntityName.Should().Be(regarding.RegardingName);

        // BOTH matched items contribute to ItemIds (widget aggregates actions).
        bullet.ItemIds.Should().BeEquivalentTo(new[] { orphan.Id, regarding.Id });
    }

    // ─── Test infrastructure (mirrors DailyBriefingNarratorTldrChainingTests) ─

    private static (Mock<AnalysisActionService> actions, Mock<IOpenAiClient> llm, Mock<IEntityNameScrubber> scrubber)
        BuildBoundaryMocks(string tldrResponseJson, string channelResponseJson)
    {
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
        llm.Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                TldrActionCode.Replace('-', '_'),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tldrResponseJson);
        llm.Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                ChannelActionCode.Replace('-', '_'),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(channelResponseJson);

        var scrubber = new Mock<IEntityNameScrubber>(MockBehavior.Loose);
        scrubber.Setup(s => s.Scrub(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns(new EntityNameScrubResult
                {
                    ScrubbedText = string.Empty,
                    RemovedTerms = Array.Empty<string>()
                });

        return (actions, llm, scrubber);
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

    private static AnalysisAction MakeChannelAction() => new()
    {
        Id = Guid.NewGuid(),
        Name = ChannelActionCode,
        SystemPrompt = "CHANNEL.",
        OutputSchemaJson = """{"type":"object","properties":{"channel":{"type":"string"},"narrative":{"type":"array","items":{"type":"string"}}},"required":["channel","narrative"],"additionalProperties":false}""",
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

    // Per-case test data.
    public sealed class EntityLinkCase
    {
        public required string Name { get; init; }
        public required ChannelItemDto Item { get; init; }
        public bool NarrativeMentionsTitle { get; init; } = true;
        public required string ExpectedPrimaryEntityType { get; init; }
        public required string ExpectedPrimaryEntityId { get; init; }
        public required string ExpectedPrimaryEntityName { get; init; }

        public override string ToString() => Name;
    }
}
