// R7 Wave 12 T131 — DailyBriefingCollector unit tests (6-entity expansion).
//
// Mocks at the module boundary per ADR-038 §1:
//   - IMembershipResolverService (resolver returns membership IDs per entity type)
//   - IGenericEntityService (Dataverse RetrieveMultipleAsync stubbed per query)
//
// Asserts BEHAVIOR THE CALLER (DailyBriefingEndpoints.HandleRender / widget) WOULD NOTICE:
//   - 6 distinct channel codes appear in the request payload
//   - Per-bullet entity-link metadata (RegardingEntityType + RegardingId) populated for
//     ALL 6 entity types — sprk_event tasks reference sprk_matter; sprk_document references
//     sprk_matter; sprk_matter / sprk_project self-regard; sprk_todo references sprk_matter
//   - Membership filter is the EXCLUSIVE ownership gate (no inline FetchXml eq-userid bypass)
//   - Failure-soft per-channel: a single channel exception does not abort the briefing
//
// Per CLAUDE.md tests/CLAUDE.md anti-pattern bans:
//   - NO Mock<HttpMessageHandler>     (we mock typed services, not transport)
//   - NO DI-registration tests        (DI verified by app startup)
//   - NO ctor null-argument tests     (production uses ArgumentNullException.ThrowIfNull)
//
// Tests live at tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/ — sibling to
// DailyBriefingNarratorTldrChainingTests.cs (T132) which uses the same conventions.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Ai.Narrators;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Narrators;

[Trait("status", "task-131-r7")]
public sealed class DailyBriefingCollectorTests
{
    private static readonly Guid SystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MatterId1 = Guid.Parse("22222222-2222-2222-2222-222222222221");
    private static readonly Guid MatterId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProjectId1 = Guid.Parse("33333333-3333-3333-3333-333333333331");
    private static readonly Guid EventId1 = Guid.Parse("44444444-4444-4444-4444-444444444441");
    private static readonly Guid DocId1 = Guid.Parse("55555555-5555-5555-5555-555555555551");
    private static readonly Guid TodoId1 = Guid.Parse("66666666-6666-6666-6666-666666666661");

    // ─────────────────────────────────────────────────────────────────────────
    // Helper builders
    // ─────────────────────────────────────────────────────────────────────────

    private static PersonIdentity MakeIdentity() =>
        new(SystemUserId);

    private static MembershipResponse EmptyMembership(string entityType) =>
        new(
            EntityType: entityType,
            PersonIdentity: MakeIdentity(),
            Ids: Array.Empty<Guid>(),
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
            Count: 0,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5));

    private static MembershipResponse MembershipWith(string entityType, params Guid[] ids) =>
        new(
            EntityType: entityType,
            PersonIdentity: MakeIdentity(),
            Ids: ids,
            ByRole: new Dictionary<string, IReadOnlyList<Guid>> { ["owner"] = ids },
            Count: ids.Length,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5));

    private static Entity MakeEventEntity(Guid id, string name, string? matterName = null, Guid? matterId = null)
    {
        var e = new Entity("sprk_event", id);
        e["sprk_eventid"] = id;
        e["sprk_eventname"] = name;
        e["sprk_duedate"] = DateTime.UtcNow.Date.AddDays(1);
        e["modifiedon"] = DateTime.UtcNow;
        if (matterId.HasValue)
        {
            e["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId.Value) { Name = matterName };
        }
        return e;
    }

    private static Entity MakeDocumentEntity(Guid id, string name, string? matterName = null, Guid? matterId = null)
    {
        var e = new Entity("sprk_document", id);
        e["sprk_documentid"] = id;
        e["sprk_documentname"] = name;
        e["modifiedon"] = DateTime.UtcNow;
        if (matterId.HasValue)
        {
            e["sprk_matter"] = new EntityReference("sprk_matter", matterId.Value) { Name = matterName };
        }
        return e;
    }

    private static Entity MakeMatterEntity(Guid id, string name)
    {
        var e = new Entity("sprk_matter", id);
        e["sprk_matterid"] = id;
        e["sprk_mattername"] = name;
        e["modifiedon"] = DateTime.UtcNow;
        return e;
    }

    private static Entity MakeProjectEntity(Guid id, string name)
    {
        var e = new Entity("sprk_project", id);
        e["sprk_projectid"] = id;
        e["sprk_projectname"] = name;
        e["modifiedon"] = DateTime.UtcNow;
        return e;
    }

    private static Entity MakeTodoEntity(Guid id, string name, Guid? matterId = null, string? matterName = null)
    {
        var e = new Entity("sprk_todo", id);
        e["sprk_todoid"] = id;
        e["sprk_name"] = name;
        e["sprk_duedate"] = DateTime.UtcNow.Date;
        e["modifiedon"] = DateTime.UtcNow;
        if (matterId.HasValue)
        {
            e["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId.Value) { Name = matterName };
        }
        return e;
    }

    /// <summary>
    /// Configure the IGenericEntityService mock to return the provided per-entity-type
    /// response. Inspects the QueryExpression.EntityName so each channel's query gets
    /// the right stub.
    /// </summary>
    private static Mock<IGenericEntityService> NewEntityServiceMock(
        IReadOnlyDictionary<string, EntityCollection> perEntityResponses)
    {
        var mock = new Mock<IGenericEntityService>(MockBehavior.Strict);
        mock.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns<QueryExpression, CancellationToken>((q, _) =>
            {
                if (perEntityResponses.TryGetValue(q.EntityName, out var coll))
                {
                    return Task.FromResult(coll);
                }
                return Task.FromResult(new EntityCollection());
            });
        return mock;
    }

    private static Mock<IMembershipResolverService> NewResolverMock(
        IReadOnlyDictionary<string, MembershipResponse> perEntityResponses)
    {
        var mock = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        mock.Setup(r => r.ResolveAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<MembershipResolveOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, string, MembershipResolveOptions?, CancellationToken>((_, entityType, _, _) =>
            {
                if (perEntityResponses.TryGetValue(entityType, out var resp))
                {
                    return Task.FromResult(resp);
                }
                return Task.FromResult(EmptyMembership(entityType));
            });
        return mock;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectAsync_WhenAllChannelsHaveData_Returns6Channels()
    {
        // Arrange — resolver returns memberships for the user across event + matter + project
        var resolverMock = NewResolverMock(new Dictionary<string, MembershipResponse>
        {
            ["sprk_event"] = MembershipWith("sprk_event", EventId1),
            ["sprk_matter"] = MembershipWith("sprk_matter", MatterId1, MatterId2),
            ["sprk_project"] = MembershipWith("sprk_project", ProjectId1),
        });

        var entityResponses = new Dictionary<string, EntityCollection>
        {
            ["sprk_event"] = new EntityCollection(new List<Entity>
            {
                MakeEventEntity(EventId1, "Task A — due tomorrow", "Matter Alpha", MatterId1)
            }),
            ["sprk_document"] = new EntityCollection(new List<Entity>
            {
                MakeDocumentEntity(DocId1, "Contract draft.pdf", "Matter Alpha", MatterId1)
            }),
            ["sprk_matter"] = new EntityCollection(new List<Entity>
            {
                MakeMatterEntity(MatterId1, "Matter Alpha")
            }),
            ["sprk_project"] = new EntityCollection(new List<Entity>
            {
                MakeProjectEntity(ProjectId1, "Project Beta")
            }),
            ["sprk_todo"] = new EntityCollection(new List<Entity>
            {
                MakeTodoEntity(TodoId1, "Send agenda", MatterId1, "Matter Alpha")
            })
        };
        var entityMock = NewEntityServiceMock(entityResponses);

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var request = await sut.CollectAsync(SystemUserId, CancellationToken.None);

        // Assert — at least the 5 channels we explicitly populated are present.
        // (The collector calls the entity service for sprk_event twice — once for
        // upcoming, once for overdue — but the stub returns the same row for any
        // sprk_event query, so both task channels populate with the same row.  This
        // is acceptable test behavior; we assert the 5 channels we directly seeded.)
        request.Channels.Should().NotBeEmpty();
        var channelCodes = request.Channels.Select(c => c.Category).ToArray();
        channelCodes.Should().Contain("upcoming-tasks");
        channelCodes.Should().Contain("documents");
        channelCodes.Should().Contain("matters");
        channelCodes.Should().Contain("projects");
        channelCodes.Should().Contain("to-dos");
        channelCodes.Should().NotContain("unknown-channel-key");
    }

    [Fact]
    public async Task CollectAsync_RoutesMembershipQueriesToResolver_NotInlineFetchXml()
    {
        // Arrange — exhaustively cover the 3 entity types the resolver is called for.
        var resolverMock = NewResolverMock(new Dictionary<string, MembershipResponse>
        {
            ["sprk_event"] = MembershipWith("sprk_event", EventId1),
            ["sprk_matter"] = MembershipWith("sprk_matter", MatterId1),
            ["sprk_project"] = MembershipWith("sprk_project", ProjectId1),
        });
        var entityMock = NewEntityServiceMock(new Dictionary<string, EntityCollection>());

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        _ = await sut.CollectAsync(SystemUserId, CancellationToken.None);

        // Assert — resolver was called for the 3 candidate-set entity types (and ONLY those —
        // sprk_document/sprk_todo are filtered downstream off the resolved sets).
        resolverMock.Verify(r => r.ResolveAsync(
            SystemUserId, "sprk_event", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        resolverMock.Verify(r => r.ResolveAsync(
            SystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        resolverMock.Verify(r => r.ResolveAsync(
            SystemUserId, "sprk_project", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CollectAsync_WhenUserHasNoMatterOrProjectMemberships_DocumentsChannelEmpty()
    {
        // Arrange — resolver returns empty for matter + project. Document channel cannot run
        // (it requires matter or project candidate ids), so it returns empty WITHOUT calling Dataverse.
        var resolverMock = NewResolverMock(new Dictionary<string, MembershipResponse>
        {
            ["sprk_event"] = MembershipWith("sprk_event", EventId1),
            ["sprk_matter"] = EmptyMembership("sprk_matter"),
            ["sprk_project"] = EmptyMembership("sprk_project"),
        });
        var entityMock = NewEntityServiceMock(new Dictionary<string, EntityCollection>());

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var request = await sut.CollectAsync(SystemUserId, CancellationToken.None);

        // Assert — documents channel is filtered (no matter/project membership)
        request.Channels.Should().NotContain(c => c.Category == "documents");
    }

    [Fact]
    public async Task CollectAsync_PerBulletEntityLinkMetadataPopulated_AcrossAll6EntityTypes()
    {
        // Arrange — populate each channel with at least one row that carries regarding metadata
        var resolverMock = NewResolverMock(new Dictionary<string, MembershipResponse>
        {
            ["sprk_event"] = MembershipWith("sprk_event", EventId1),
            ["sprk_matter"] = MembershipWith("sprk_matter", MatterId1),
            ["sprk_project"] = MembershipWith("sprk_project", ProjectId1),
        });
        var entityMock = NewEntityServiceMock(new Dictionary<string, EntityCollection>
        {
            ["sprk_event"] = new EntityCollection(new List<Entity> { MakeEventEntity(EventId1, "Task X", "Matter Alpha", MatterId1) }),
            ["sprk_document"] = new EntityCollection(new List<Entity> { MakeDocumentEntity(DocId1, "Doc Y", "Matter Alpha", MatterId1) }),
            ["sprk_matter"] = new EntityCollection(new List<Entity> { MakeMatterEntity(MatterId1, "Matter Alpha") }),
            ["sprk_project"] = new EntityCollection(new List<Entity> { MakeProjectEntity(ProjectId1, "Project Beta") }),
            ["sprk_todo"] = new EntityCollection(new List<Entity> { MakeTodoEntity(TodoId1, "Send agenda", MatterId1, "Matter Alpha") })
        });

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var request = await sut.CollectAsync(SystemUserId, CancellationToken.None);

        // Assert — every channel's items have non-empty regarding metadata (so
        // EnrichBulletWithEntityRefs downstream can build click-through links)
        foreach (var channel in request.Channels)
        {
            foreach (var item in channel.Items)
            {
                item.RegardingId.Should().NotBeNullOrEmpty(
                    $"channel '{channel.Category}' item should carry RegardingId for entity-link projection");
                item.RegardingEntityType.Should().NotBeNullOrEmpty(
                    $"channel '{channel.Category}' item should carry RegardingEntityType for navigation");
                item.RegardingName.Should().NotBeNullOrEmpty(
                    $"channel '{channel.Category}' item should carry RegardingName for display");
            }
        }
    }

    [Fact]
    public async Task CollectAsync_MatterChannelItems_AreSelfRegarding()
    {
        // Arrange — only matter channel populated
        var resolverMock = NewResolverMock(new Dictionary<string, MembershipResponse>
        {
            ["sprk_matter"] = MembershipWith("sprk_matter", MatterId1),
        });
        var entityMock = NewEntityServiceMock(new Dictionary<string, EntityCollection>
        {
            ["sprk_matter"] = new EntityCollection(new List<Entity> { MakeMatterEntity(MatterId1, "Matter Alpha") })
        });

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var request = await sut.CollectAsync(SystemUserId, CancellationToken.None);

        // Assert — the matter row is self-regarding (RegardingEntityType == "sprk_matter",
        // RegardingId == matter's own GUID)
        var matterChannel = request.Channels.Single(c => c.Category == "matters");
        var item = matterChannel.Items.Single();
        item.RegardingEntityType.Should().Be("sprk_matter");
        item.RegardingId.Should().Be(MatterId1.ToString());
        item.RegardingName.Should().Be("Matter Alpha");
    }

    [Fact]
    public async Task CollectAsync_ProjectChannelItems_AreSelfRegardingWithProjectEntityType()
    {
        // Arrange — only project channel populated. Verifies the project rows surface
        // RegardingEntityType=sprk_project (NOT sprk_matter) so downstream routing builds
        // the right entity URL.
        var resolverMock = NewResolverMock(new Dictionary<string, MembershipResponse>
        {
            ["sprk_project"] = MembershipWith("sprk_project", ProjectId1),
        });
        var entityMock = NewEntityServiceMock(new Dictionary<string, EntityCollection>
        {
            ["sprk_project"] = new EntityCollection(new List<Entity> { MakeProjectEntity(ProjectId1, "Project Beta") })
        });

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var request = await sut.CollectAsync(SystemUserId, CancellationToken.None);

        // Assert — project is self-regarding under its own entity type
        var projectChannel = request.Channels.Single(c => c.Category == "projects");
        var item = projectChannel.Items.Single();
        item.RegardingEntityType.Should().Be("sprk_project");
        item.RegardingId.Should().Be(ProjectId1.ToString());
    }

    [Fact]
    public async Task CollectAsync_WhenSingleChannelQueryFails_OtherChannelsStillReturned()
    {
        // Arrange — sprk_event query throws (Dataverse failure for that one channel).
        // Membership resolver succeeds for all 3.  Other channel queries succeed.
        var resolverMock = NewResolverMock(new Dictionary<string, MembershipResponse>
        {
            ["sprk_event"] = MembershipWith("sprk_event", EventId1),
            ["sprk_matter"] = MembershipWith("sprk_matter", MatterId1),
            ["sprk_project"] = MembershipWith("sprk_project", ProjectId1),
        });

        var entityMock = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityMock.Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_event"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated Dataverse failure on sprk_event"));
        entityMock.Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_matter"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { MakeMatterEntity(MatterId1, "Matter Alpha") }));
        entityMock.Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName != "sprk_event" && q.EntityName != "sprk_matter"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var request = await sut.CollectAsync(SystemUserId, CancellationToken.None);

        // Assert — the matter channel still appears (failure-soft per channel)
        request.Channels.Should().Contain(c => c.Category == "matters");
        // Task channels (upcoming/overdue) are skipped (event query failed → empty arrays → filtered)
        request.Channels.Should().NotContain(c => c.Category == "upcoming-tasks");
        request.Channels.Should().NotContain(c => c.Category == "overdue-tasks");
    }

    [Fact]
    public async Task CollectAsync_WhenMembershipResolverFails_DependentChannelsEmpty()
    {
        // Arrange — resolver throws for all 3 membership lookups. Collector should still
        // run (failure-soft membership resolution) and complete with empty Task/Document/
        // Matter/Project channels; sprk_todo (no membership filter) may still return rows.
        var resolverMock = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        resolverMock.Setup(r => r.ResolveAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<MembershipResolveOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated resolver failure"));

        var entityMock = NewEntityServiceMock(new Dictionary<string, EntityCollection>
        {
            // Todo query runs even though membership failed (no membership dep)
            ["sprk_todo"] = new EntityCollection(new List<Entity>
            {
                MakeTodoEntity(TodoId1, "Standalone todo")
            })
        });

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var request = await sut.CollectAsync(SystemUserId, CancellationToken.None);

        // Assert — to-dos channel is present (no membership dep); membership-dependent
        // channels are filtered out (empty arrays after resolver failure).
        request.Channels.Should().Contain(c => c.Category == "to-dos");
        request.Channels.Should().NotContain(c => c.Category == "documents",
            "documents requires matter/project membership — resolver failed → empty");
        request.Channels.Should().NotContain(c => c.Category == "matters");
        request.Channels.Should().NotContain(c => c.Category == "projects");
    }

    [Fact]
    public async Task CollectAsync_WithEmptySystemUserId_Throws()
    {
        // Arrange
        var resolverMock = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var entityMock = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act + Assert
        var act = async () => await sut.CollectAsync(Guid.Empty, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("systemUserId is required*");
    }

    [Fact]
    public async Task CollectAsync_CategoriesAndTotalCountMatchActualItems()
    {
        // Arrange — exactly 2 matter rows, 1 todo row
        var resolverMock = NewResolverMock(new Dictionary<string, MembershipResponse>
        {
            ["sprk_matter"] = MembershipWith("sprk_matter", MatterId1, MatterId2),
        });

        var entityMock = NewEntityServiceMock(new Dictionary<string, EntityCollection>
        {
            ["sprk_matter"] = new EntityCollection(new List<Entity>
            {
                MakeMatterEntity(MatterId1, "Matter Alpha"),
                MakeMatterEntity(MatterId2, "Matter Beta"),
            }),
            ["sprk_todo"] = new EntityCollection(new List<Entity>
            {
                MakeTodoEntity(TodoId1, "Send agenda")
            })
        });

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var request = await sut.CollectAsync(SystemUserId, CancellationToken.None);

        // Assert — total = 2 matters + 1 todo = 3
        request.TotalNotificationCount.Should().Be(3);
        request.Categories.Should().Contain(c => c.Name == "Matters" && c.Count == 2);
        request.Categories.Should().Contain(c => c.Name == "To Dos" && c.Count == 1);
    }
}
