// R7 Wave 12 task 135 (origin) — Per-bullet entity-link resolution across all 6 entity types.
// REWRITTEN by R5 task 010 (FR-A3, 2026-07-08): the per-channel LLM narrate leg that this
// suite originally exercised was REMOVED (R4 / R7 W12 tried prompt-steering to stop
// cross-item hallucination and failed; the per-channel LLM call was the primary source).
// Channel/section content is now built DETERMINISTICALLY, straight from the collector's
// req.Channels view model — one bullet per source item, no LLM call, no free-text
// matching. See DailyBriefingNarrator.BuildDeterministicBullet.
//
// Behavior contract under test (still the operator-facing wave12 plan §2.1 AC5, now
// satisfied structurally instead of via LLM-narrative substring matching):
//   "Each Activity Notes bullet has working entity link" — i.e., every rendered
//   NarrativeBulletDto MUST have PrimaryEntityType/Id/Name populated (when resolvable)
//   so the widget can build a click-through to the underlying Dataverse record.
//
// We drive the narrator end-to-end through NarrateAsync, mocking ONLY the TL;DR LLM call
// at the IOpenAiClient boundary (ADR-038 §1 — integration-heavy pyramid, mock at module
// boundary, assert observable behavior). The mock is Strict, so any attempt to call the
// LLM for anything other than the TL;DR action fails the test — that IS the "exactly one
// LLM call" assertion for this suite.

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

[Trait("status", "task-010-r5")]
public sealed class DailyBriefingNarratorEntityLinkTests
{
    private const string TldrActionCode = "BRIEF-NARRATE-TLDR";

    // ─── Per-entity-type theory data ────────────────────────────────────────
    //
    // For each of the 6 channels T131 introduced, we synthesize a single-item request and
    // assert the resulting deterministic bullet's PrimaryEntity* fields match what the
    // widget needs to navigate.
    //
    // Cases:
    //   1. sprk_event (Task) with regarding matter   → link points to matter
    //   2. sprk_document with regarding matter       → link points to matter
    //   3. sprk_matter (self-regarding)              → link points to matter
    //   4. sprk_project (self-regarding)              → link points to project
    //   5. sprk_todo with regarding matter            → link points to matter
    //   6. sprk_event (orphan: no regarding matter)   → link FALLS BACK to event
    //   7. sprk_todo (orphan)                          → link FALLS BACK to todo

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
            ExpectedPrimaryEntityType = "sprk_matter",
            ExpectedPrimaryEntityId = "55555555-5555-5555-5555-555555555555",
            ExpectedPrimaryEntityName = "Echo Settlement Matter"
        },

        // 6. sprk_event ORPHAN (no regarding matter). Tier 2 fallback: link points to the
        //    source event row.
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
            ExpectedPrimaryEntityType = "sprk_event",
            ExpectedPrimaryEntityId = "event-orphan-1",
            ExpectedPrimaryEntityName = "Submit independent CLE attestation"
        },

        // 7. sprk_todo ORPHAN (no regarding matter). Tier 2 fallback: link points to the
        //    source todo row.
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
            ExpectedPrimaryEntityType = "sprk_todo",
            ExpectedPrimaryEntityId = "todo-orphan-1",
            ExpectedPrimaryEntityName = "Reschedule personal dentist appointment"
        }
    };

    [Theory]
    [MemberData(nameof(EntityLinkCases))]
    public async Task NarrateAsync_PopulatesPrimaryEntityFieldsDeterministically_ForEachEntityType(EntityLinkCase tc)
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

        var (actions, llm, scrubber) = BuildBoundaryMocks();

        var sut = new DailyBriefingNarrator(
            actions.Object,
            llm.Object,
            scrubber.Object,
            NullLogger<DailyBriefingNarrator>.Instance);

        // Act
        var response = await sut.NarrateAsync(req, CancellationToken.None);

        // Assert — exactly one channel narrative with one bullet; bullet's Narrative is the
        // source item's Title VERBATIM (deterministic — no LLM authorship), and PrimaryEntity*
        // fields point to the expected target.
        response.ChannelNarratives.Should().HaveCount(1, because: tc.Name);
        var bullets = response.ChannelNarratives[0].Bullets;
        bullets.Should().HaveCount(1, because: tc.Name);

        var bullet = bullets[0];
        bullet.Narrative.Should().Be(tc.Item.Title, because: tc.Name + " — deterministic bullet echoes the source Title verbatim");
        bullet.PrimaryEntityType.Should().Be(tc.ExpectedPrimaryEntityType, because: tc.Name);
        bullet.PrimaryEntityId.Should().Be(tc.ExpectedPrimaryEntityId, because: tc.Name);
        bullet.PrimaryEntityName.Should().Be(tc.ExpectedPrimaryEntityName, because: tc.Name);

        // ItemIds carries the source item's own Id — widget uses this for "Add To Do" +
        // "Dismiss" actions, both of which key off the SOURCE row id.
        bullet.ItemIds.Should().Contain(tc.Item.Id, because: tc.Name);

        // The Strict LLM mock only stubs the TL;DR action — if the narrator had called
        // anything else (e.g., a resurrected per-channel leg), Moq would throw and this
        // test would fail. That IS the "exactly one LLM call" assertion.
        llm.Verify(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), TldrActionCode.Replace('-', '_'),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── Negative path: item with neither regarding nor source-entity data → no link ────

    [Fact]
    public async Task NarrateAsync_LeavesBulletUnlinked_WhenItemHasNoRegardingOrSourceEntityData()
    {
        var item = new ChannelItemDto
        {
            Id = "",
            Title = "Some specific task name",
            RegardingName = "",
            RegardingEntityType = "",
            RegardingId = "",
            SourceEntityType = ""
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

        var (actions, llm, scrubber) = BuildBoundaryMocks();

        var sut = new DailyBriefingNarrator(actions.Object, llm.Object, scrubber.Object,
            NullLogger<DailyBriefingNarrator>.Instance);

        var response = await sut.NarrateAsync(req, CancellationToken.None);

        response.ChannelNarratives.Should().HaveCount(1);
        var bullet = response.ChannelNarratives[0].Bullets.Single();
        bullet.Narrative.Should().Be(item.Title);
        bullet.PrimaryEntityType.Should().BeEmpty(
            because: "neither RegardingId nor SourceEntityType+Id is populated, so no entity-link resolution is possible");
        bullet.PrimaryEntityId.Should().BeEmpty();
        bullet.PrimaryEntityName.Should().BeEmpty();
    }

    // ─── Multi-item channel: deterministic pass-through, one bullet per item ────────────
    // Pre-R5 the LLM could aggregate multiple items into one bullet ("several tasks...").
    // Post-R5 each source item gets its own bullet — no aggregation, no LLM authorship.

    [Fact]
    public async Task NarrateAsync_ProducesOneBulletPerSourceItem_WithNoCrossItemAggregation()
    {
        var orphan = new ChannelItemDto
        {
            Id = "event-orphan",
            Title = "Alpha task",
            RegardingName = "",
            RegardingEntityType = "",
            RegardingId = "",
            SourceEntityType = "sprk_event"
        };
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
                    Items = [orphan, regarding]
                }
            ]
        };

        var (actions, llm, scrubber) = BuildBoundaryMocks();
        var sut = new DailyBriefingNarrator(actions.Object, llm.Object, scrubber.Object,
            NullLogger<DailyBriefingNarrator>.Instance);

        var response = await sut.NarrateAsync(req, CancellationToken.None);

        // Two source items → two independent bullets (no LLM aggregation).
        var bullets = response.ChannelNarratives[0].Bullets;
        bullets.Should().HaveCount(2);

        bullets[0].Narrative.Should().Be(orphan.Title);
        bullets[0].PrimaryEntityType.Should().Be("sprk_event", because: "orphan item falls back to its own source row");
        bullets[0].PrimaryEntityId.Should().Be(orphan.Id);
        bullets[0].ItemIds.Should().BeEquivalentTo(new[] { orphan.Id });

        bullets[1].Narrative.Should().Be(regarding.Title);
        bullets[1].PrimaryEntityType.Should().Be("sprk_matter");
        bullets[1].PrimaryEntityId.Should().Be(regarding.RegardingId);
        bullets[1].ItemIds.Should().BeEquivalentTo(new[] { regarding.Id });
    }

    // ─── Test infrastructure ──────────────────────────────────────────────────────────────

    private static (Mock<AnalysisActionService> actions, Mock<IOpenAiClient> llm, Mock<IEntityNameScrubber> scrubber)
        BuildBoundaryMocks()
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
            summary = "summary",
            keyTakeaways = new[] { "k1" },
            topAction = "action"
        });

        // Strict — the TL;DR action is the ONLY LLM call this narrator is allowed to make
        // post-R5 task 010. Any other invocation (e.g. a resurrected per-channel call)
        // throws MockException and fails the test.
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
        public required string ExpectedPrimaryEntityType { get; init; }
        public required string ExpectedPrimaryEntityId { get; init; }
        public required string ExpectedPrimaryEntityName { get; init; }

        public override string ToString() => Name;
    }
}
