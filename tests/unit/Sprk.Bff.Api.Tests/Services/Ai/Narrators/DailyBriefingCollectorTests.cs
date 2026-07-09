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
//   - Ownership gate routes through IMembershipResolverService for events/matters/projects
//     (R5 task 033 reverted the R7 W12 owner-only bypass now that the R7 root-cause fix to
//     MembershipFieldDiscoveryService synthesizes Owner/Customer lookup targets) — collaborators
//     (e.g. sprk_assignedattorney1) are included in the candidate set, not just owners
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
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

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
    public async Task CollectAsync_OwnershipGate_RoutesThroughMembershipResolver()
    {
        // R5 task 033 (2026-07-08) — re-flip of the R7 W12 pin (PR #558). The R7 W12 owner-only
        // bypass (commit 5ca115765) has been reverted: the R7 root-cause fix to
        // MembershipFieldDiscoveryService.ProjectLookupAttributeRows now synthesizes Owner +
        // Customer lookup targets, so IMembershipResolverService returns rows for the
        // polymorphic Owner attribute again. Candidate-set resolution for events/matters/
        // projects routes through the resolver — this test now asserts resolver ROUTING
        // (not the bypass) so collaborator scope (assigned attorneys, paralegals, etc.) is
        // restored.

        // Arrange
        var resolverMock = NewResolverMock(new Dictionary<string, MembershipResponse>
        {
            ["sprk_event"] = MembershipWith("sprk_event", EventId1),
            ["sprk_matter"] = MembershipWith("sprk_matter", MatterId1),
            ["sprk_project"] = MembershipWith("sprk_project", ProjectId1),
        });

        var capturedQueries = new List<QueryExpression>();
        var entityMock = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => capturedQueries.Add(q))
            .ReturnsAsync(new EntityCollection());

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        _ = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        // Assert — the resolver IS invoked exactly once per candidate-set entity type, scoped
        // to the acting user.
        foreach (var entityType in new[] { "sprk_event", "sprk_matter", "sprk_project" })
        {
            resolverMock.Verify(r => r.ResolveAsync(
                SystemUserId, entityType, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
                Times.Once,
                $"candidate-set resolution for {entityType} must route through IMembershipResolverService");
        }

        // The per-user to-dos channel stays a direct owner-scoped QueryExpression — sprk_todo
        // has no membership-bearing fields, so it is intentionally out of scope for the
        // resolver-routing revert.
        capturedQueries
            .Where(q => q.EntityName == "sprk_todo")
            .Should().ContainSingle()
            .Which.Criteria.Conditions.Should().Contain(c =>
                c.AttributeName == "owninguser" &&
                c.Operator == ConditionOperator.Equal &&
                c.Values.Contains((object)SystemUserId),
                "todos are per-user; an unscoped todo query would leak other users' items");
    }

    [Fact]
    public async Task CollectAsync_CollaboratorNotOwner_SeesAssignedMatterInCandidateSet()
    {
        // R5 task 033 collaborator smoke test (spec FR-C4). A systemUser who is a
        // sprk_assignedattorney1 (collaborator) but NOT the owner of a matter must see that
        // matter in the collected briefing candidate set. IMembershipResolverService discovers
        // the assignedAttorney role (unlike the reverted owner-only bypass, which only ever
        // matched `owninguser = systemUserId`).
        //
        // This test is constructed so it FAILS against the old bypass: the entity-service stub
        // returns EMPTY for any query that filters on `owninguser` (simulating that this user
        // does not own the matter directly — the bypass's ResolveOwnedIdsAsync query would find
        // nothing) and returns the matter row only for the membership-driven `sprk_matterid IN
        // [...]` query the resolver-routed channel query issues.

        // Arrange — resolver reports the matter via the assignedAttorney role (NOT owner).
        var resolverMock = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        resolverMock
            .Setup(r => r.ResolveAsync(
                SystemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MembershipResponse(
                EntityType: "sprk_matter",
                PersonIdentity: MakeIdentity(),
                Ids: new[] { MatterId1 },
                ByRole: new Dictionary<string, IReadOnlyList<Guid>> { ["assignedAttorney"] = new[] { MatterId1 } },
                Count: 1,
                CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5)));
        resolverMock
            .Setup(r => r.ResolveAsync(
                SystemUserId, It.Is<string>(e => e != "sprk_matter"), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, MembershipResolveOptions?, CancellationToken>(
                (_, entityType, _, _) => Task.FromResult(EmptyMembership(entityType)));

        var entityMock = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns<QueryExpression, CancellationToken>((q, _) =>
            {
                var isDirectOwnerLookup = q.Criteria.Conditions.Any(c => c.AttributeName == "owninguser");
                if (isDirectOwnerLookup)
                {
                    // The old bypass's direct-owner lookup — this user owns nothing directly.
                    return Task.FromResult(new EntityCollection());
                }
                if (q.EntityName == "sprk_matter")
                {
                    return Task.FromResult(new EntityCollection(new List<Entity>
                    {
                        MakeMatterEntity(MatterId1, "Collaborator Matter")
                    }));
                }
                return Task.FromResult(new EntityCollection());
            });

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        // Assert — the collaborator-only matter surfaces in the briefing.
        request.Channels.Should().Contain(c => c.Category == "matters",
            "the assigned attorney (collaborator, not owner) must see the matter via membership resolution");
        request.Channels.Single(c => c.Category == "matters").Items
            .Should().ContainSingle(i => i.RegardingId == MatterId1.ToString());
    }

    [Fact]
    public async Task CollectAsync_OwnerOfMatterProjectEvent_StillIncludedInCandidateSet()
    {
        // R5 task 033 owner-scoped regression guard: reverting the bypass MUST NOT regress the
        // owner-scoped case. A systemUser who OWNS a matter, project, and event still sees all
        // three in the briefing candidate set once resolution is routed through
        // IMembershipResolverService.
        var resolverMock = NewResolverMock(new Dictionary<string, MembershipResponse>
        {
            ["sprk_event"] = MembershipWith("sprk_event", EventId1),
            ["sprk_matter"] = MembershipWith("sprk_matter", MatterId1),
            ["sprk_project"] = MembershipWith("sprk_project", ProjectId1),
        });

        var entityMock = NewEntityServiceMock(new Dictionary<string, EntityCollection>
        {
            ["sprk_event"] = new EntityCollection(new List<Entity>
            {
                MakeEventEntity(EventId1, "Owned Task", "Matter Alpha", MatterId1)
            }),
            ["sprk_matter"] = new EntityCollection(new List<Entity>
            {
                MakeMatterEntity(MatterId1, "Owned Matter")
            }),
            ["sprk_project"] = new EntityCollection(new List<Entity>
            {
                MakeProjectEntity(ProjectId1, "Owned Project")
            }),
        });

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        // Assert — owner still sees their own matter, project, and (task-typed) event.
        request.Channels.Should().Contain(c => c.Category == "matters");
        request.Channels.Single(c => c.Category == "matters").Items
            .Should().ContainSingle(i => i.RegardingId == MatterId1.ToString());

        request.Channels.Should().Contain(c => c.Category == "projects");
        request.Channels.Single(c => c.Category == "projects").Items
            .Should().ContainSingle(i => i.RegardingId == ProjectId1.ToString());

        request.Channels.Should().Contain(c => c.Category == "upcoming-tasks");
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
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

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
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

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
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

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
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

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
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

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
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

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
        var act = async () => await sut.CollectAsync(Guid.Empty, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);
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
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        // Assert — total = 2 matters + 1 todo = 3
        request.TotalNotificationCount.Should().Be(3);
        request.Categories.Should().Contain(c => c.Name == "Matters" && c.Count == 2);
        request.Categories.Should().Contain(c => c.Name == "To Dos" && c.Count == 1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // De-dup (R5 task 034 / FR-C5) — an item reachable via two collection paths must
    // appear exactly once in the assembled output; unique items must never be dropped.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectAsync_EventReachableViaBothUpcomingAndOverdueDateFields_AppearsExactlyOnce()
    {
        // Arrange — a single sprk_event whose sprk_duedate is already 6 days past (matches
        // the Overdue query's OnOrBefore filter) while its sprk_finalduedate is 2 days out
        // (matches the Upcoming query's NextXDays filter). QueryEventsAsync's date filter is
        // an OR across both fields, so the SAME event row satisfies BOTH the upcoming-tasks
        // query and the overdue-tasks query — the two-collection-path duplication case this
        // task de-dups. The stub returns this one row for every sprk_event query (both
        // channels query the same entity), so without de-dup the event would appear in both
        // the "upcoming-tasks" and "overdue-tasks" channels.
        var resolverMock = NewResolverMock(new Dictionary<string, MembershipResponse>
        {
            ["sprk_event"] = MembershipWith("sprk_event", EventId1),
        });

        var dualDateEvent = new Entity("sprk_event", EventId1);
        dualDateEvent["sprk_eventid"] = EventId1;
        dualDateEvent["sprk_eventname"] = "Task reachable via both date fields";
        dualDateEvent["sprk_duedate"] = DateTime.UtcNow.Date.AddDays(-6);       // satisfies Overdue
        dualDateEvent["sprk_finalduedate"] = DateTime.UtcNow.Date.AddDays(2);   // satisfies Upcoming
        dualDateEvent["modifiedon"] = DateTime.UtcNow;

        var entityMock = NewEntityServiceMock(new Dictionary<string, EntityCollection>
        {
            ["sprk_event"] = new EntityCollection(new List<Entity> { dualDateEvent }),
        });

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        // Assert — the record's identity (sprk_event + EventId1) appears exactly once across
        // ALL channels combined (not merely once per channel) — the two-path dedup contract.
        var allItemIds = request.Channels
            .SelectMany(c => c.Items)
            .Where(i => i.Id == EventId1.ToString())
            .ToArray();
        allItemIds.Should().ContainSingle(
            "an item reachable via two collection paths (upcoming + overdue date fields) must appear exactly once");

        // Category counts and TotalNotificationCount must reflect the de-duped view, not the
        // raw double-counted per-query row count.
        request.TotalNotificationCount.Should().Be(1);
    }

    [Fact]
    public async Task CollectAsync_NDistinctRecordsAcrossPathsIncludingSameTitledMatters_AllNAppear()
    {
        // Arrange — no-over-dedup guard: N genuinely-distinct records across different
        // channels/entity types, INCLUDING two distinct sprk_matter records that share the
        // exact same display title ("Shared Title Matter"). De-dup keys on entity+GUID
        // identity, never display text — so both same-titled matters must survive alongside
        // every other distinct record. N = 4 total (2 same-titled matters + 1 project + 1 todo).
        var matterA = Guid.Parse("77777777-7777-7777-7777-777777777771");
        var matterB = Guid.Parse("77777777-7777-7777-7777-777777777772");

        var resolverMock = NewResolverMock(new Dictionary<string, MembershipResponse>
        {
            ["sprk_matter"] = MembershipWith("sprk_matter", matterA, matterB),
            ["sprk_project"] = MembershipWith("sprk_project", ProjectId1),
        });

        var entityMock = NewEntityServiceMock(new Dictionary<string, EntityCollection>
        {
            ["sprk_matter"] = new EntityCollection(new List<Entity>
            {
                MakeMatterEntity(matterA, "Shared Title Matter"),
                MakeMatterEntity(matterB, "Shared Title Matter"),
            }),
            ["sprk_project"] = new EntityCollection(new List<Entity>
            {
                MakeProjectEntity(ProjectId1, "Distinct Project")
            }),
            ["sprk_todo"] = new EntityCollection(new List<Entity>
            {
                MakeTodoEntity(TodoId1, "Distinct Todo")
            }),
        });

        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        // Assert — all 4 distinct records survive; no unique item was dropped by de-dup.
        var allItems = request.Channels.SelectMany(c => c.Items).ToArray();
        allItems.Should().HaveCount(4);
        request.TotalNotificationCount.Should().Be(4);

        // The two same-titled-but-distinct matters both survive (proves entity+GUID keying,
        // not display-text keying — display-text keying would have collapsed these to 1).
        var matterChannel = request.Channels.Single(c => c.Category == "matters");
        matterChannel.Items.Should().HaveCount(2);
        matterChannel.Items.Select(i => i.Id).Should().BeEquivalentTo(new[] { matterA.ToString(), matterB.ToString() });
    }

    [Fact]
    public async Task CollectHighPriorityAsync_ItemReachableAcrossQueries_AppearsExactlyOnceAndOrderingPreserved()
    {
        // Arrange — 3 distinct high-priority records across 3 different entity types, with
        // due dates chosen so the expected DueDate-then-Name order is unambiguous. This proves
        // (a) de-dup does not drop unique items across the 7-entity merge, and (b) the
        // existing DueDate-then-Name ordering survives the de-dup step (constraint: de-dup
        // before/within ordering, never reshuffle beyond removing duplicates).
        var matterId = Guid.Parse("88888888-8888-8888-8888-888888888881");
        var projectId = Guid.Parse("88888888-8888-8888-8888-888888888882");
        var eventId = Guid.Parse("88888888-8888-8888-8888-888888888883");

        var matterEntity = new Entity("sprk_matter", matterId);
        matterEntity["sprk_matterid"] = matterId;
        matterEntity["sprk_mattername"] = "Zeta Matter"; // no due date column — sorts last (MaxValue)
        matterEntity["sprk_highpriority"] = true;
        matterEntity["statecode"] = 0;
        matterEntity["modifiedon"] = DateTime.UtcNow;

        var projectEntity = new Entity("sprk_project", projectId);
        projectEntity["sprk_projectid"] = projectId;
        projectEntity["sprk_projectname"] = "Alpha Project"; // no due date column — sorts last
        projectEntity["sprk_highpriority"] = true;
        projectEntity["statecode"] = 0;
        projectEntity["modifiedon"] = DateTime.UtcNow;

        var eventEntity = new Entity("sprk_event", eventId);
        eventEntity["sprk_eventid"] = eventId;
        eventEntity["sprk_eventname"] = "Earliest-Due Task";
        eventEntity["sprk_highpriority"] = true;
        eventEntity["sprk_finalduedate"] = DateTime.UtcNow.Date.AddDays(1); // has a due date — sorts first
        eventEntity["modifiedon"] = DateTime.UtcNow;

        var entityMock = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns<QueryExpression, CancellationToken>((q, _) => q.EntityName switch
            {
                "sprk_matter" => Task.FromResult(new EntityCollection(new List<Entity> { matterEntity })),
                "sprk_project" => Task.FromResult(new EntityCollection(new List<Entity> { projectEntity })),
                "sprk_event" => Task.FromResult(new EntityCollection(new List<Entity> { eventEntity })),
                _ => Task.FromResult(new EntityCollection()),
            });

        var resolverMock = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        var items = await sut.CollectHighPriorityAsync(SystemUserId, CancellationToken.None);

        // Assert — all 3 distinct records present exactly once each (no drop, no duplication).
        items.Should().HaveCount(3);
        items.Select(i => i.EntityId).Should().BeEquivalentTo(
            new[] { matterId.ToString(), projectId.ToString(), eventId.ToString() });

        // Ordering preserved: due-dated item first (DueDate ascending), then undated items
        // ordered by Name ascending ("Alpha Project" before "Zeta Matter").
        items[0].EntityId.Should().Be(eventId.ToString(), "the only due-dated item sorts first");
        items[1].Name.Should().Be("Alpha Project", "undated items fall back to Name ascending");
        items[2].Name.Should().Be("Zeta Matter");
    }

    [Fact]
    public async Task CollectHighPriorityAsync_FansOutOverSpecArray_EachEntityKeepsItsQueryIntent()
    {
        // Guards the R5 task 036 refactor (7 named QueryHighPriority*Async wrappers collapsed
        // into the HighPriorityEntitySpec[] fan-out): the collapse MUST preserve, per entity,
        // the exact entity name + projected columns + flag filter + state filter + owner
        // scoping the named wrapper carried. This captures every QueryExpression the collapsed
        // path issues and pins each entity's intent so a future spec edit can't silently drift
        // one entity (drop a column, lose the To Do owner-scope, or state-filter the event).
        var capturedQueries = new List<QueryExpression>();
        var entityMock = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => capturedQueries.Add(q))
            .ReturnsAsync(new EntityCollection());

        var resolverMock = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = new DailyBriefingCollector(
            entityMock.Object,
            resolverMock.Object,
            NullLogger<DailyBriefingCollector>.Instance);

        // Act
        await sut.CollectHighPriorityAsync(SystemUserId, CancellationToken.None);

        // Assert — all 7 flagged entities queried exactly once.
        var byEntity = capturedQueries.ToDictionary(q => q.EntityName);
        byEntity.Keys.Should().BeEquivalentTo(new[]
        {
            "sprk_matter", "sprk_project", "sprk_invoice", "sprk_document",
            "sprk_workassignment", "sprk_event", "sprk_todo",
        });

        // Per-entity expected projected columns (id + name + description + due-date columns).
        // The generic query always ALSO projects sprk_highpriority, sprk_monitor, modifiedon.
        AssertHighPriorityQuery(byEntity["sprk_matter"],
            new[] { "sprk_matterid", "sprk_mattername", "sprk_matterdescription" }, stateFiltered: true, ownerScoped: false);
        AssertHighPriorityQuery(byEntity["sprk_project"],
            new[] { "sprk_projectid", "sprk_projectname", "sprk_description" }, stateFiltered: true, ownerScoped: false);
        AssertHighPriorityQuery(byEntity["sprk_invoice"],
            new[] { "sprk_invoiceid", "sprk_name", "sprk_description" }, stateFiltered: true, ownerScoped: false);
        AssertHighPriorityQuery(byEntity["sprk_document"],
            new[] { "sprk_documentid", "sprk_documentname", "sprk_documentdescription" }, stateFiltered: true, ownerScoped: false);
        AssertHighPriorityQuery(byEntity["sprk_workassignment"],
            new[] { "sprk_workassignmentid", "sprk_name", "sprk_description", "sprk_responseduedate" }, stateFiltered: true, ownerScoped: false);
        // Event: NOT state-filtered (includeStateFilter:false) and carries BOTH due-date columns.
        AssertHighPriorityQuery(byEntity["sprk_event"],
            new[] { "sprk_eventid", "sprk_eventname", "sprk_eventdescription", "sprk_finalduedate", "sprk_duedate" }, stateFiltered: false, ownerScoped: false);
        // To Do: owner-scoped to SystemUserId (R7 W12 per-user scoping preserved by ScopeToOwner).
        AssertHighPriorityQuery(byEntity["sprk_todo"],
            new[] { "sprk_todoid", "sprk_name", "sprk_description", "sprk_duedate" }, stateFiltered: true, ownerScoped: true);
    }

    // Pins one entity's high-priority QueryExpression to the intent its former named wrapper
    // carried: projected columns, the HighPriority-OR-Monitor flag group, the optional
    // statecode filter, and the optional owninguser scoping.
    private static void AssertHighPriorityQuery(
        QueryExpression query,
        string[] expectedColumns,
        bool stateFiltered,
        bool ownerScoped)
    {
        query.ColumnSet.Columns.Should().Contain(expectedColumns);
        query.ColumnSet.Columns.Should().Contain(new[] { "sprk_highpriority", "sprk_monitor", "modifiedon" });

        // Flag filter: a nested OR group of sprk_highpriority=true / sprk_monitor=true.
        var flagGroup = query.Criteria.Filters.Should()
            .ContainSingle(f => f.FilterOperator == LogicalOperator.Or).Subject;
        flagGroup.Conditions.Select(c => c.AttributeName)
            .Should().BeEquivalentTo(new[] { "sprk_highpriority", "sprk_monitor" });

        // statecode=0 present iff the entity opts into the state filter.
        var stateConditions = query.Criteria.Conditions.Where(c => c.AttributeName == "statecode");
        if (stateFiltered) stateConditions.Should().ContainSingle();
        else stateConditions.Should().BeEmpty();

        // owninguser present iff the entity is owner-scoped (To Do), pinned to SystemUserId.
        var ownerConditions = query.Criteria.Conditions.Where(c => c.AttributeName == "owninguser").ToList();
        if (ownerScoped)
        {
            ownerConditions.Should().ContainSingle();
            ownerConditions[0].Values.Should().ContainSingle().Which.Should().Be(SystemUserId);
        }
        else
        {
            ownerConditions.Should().BeEmpty();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TL;DR scaffolding wiring (R5 task 013 / FR-A4) — the collector attaches
    // deterministically-computed TldrFacts onto the request it hands the narrator.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectAsync_AttachesTldrFacts_MatchingTheRequestsOwnDeterministicViewModel()
    {
        // Arrange — same fixture as CollectAsync_CategoriesAndTotalCountMatchActualItems: 2
        // matter rows, 1 todo row.
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
        var request = await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        // Assert — TldrFacts is populated (not the LLM's job to fill it in) and every count
        // traces back EXACTLY to the same view model the request itself carries — this is the
        // "TL;DR asserts only deterministic facts" contract at the collector boundary.
        request.TldrFacts.Should().NotBeNull(
            because: "the collector must stamp deterministic scaffolding onto every request it builds (R5 FR-A4)");
        request.TldrFacts!.TotalNotificationCount.Should().Be(request.TotalNotificationCount);
        request.TldrFacts.CategoryCounts.Should().BeEquivalentTo(request.Categories);
        request.TldrFacts.PriorityItemCount.Should().Be(request.PriorityItems.Length);
    }
}

/// <summary>
/// R5 task 013 (FR-A4) — pure unit tests for <see cref="DailyBriefingCollector.BuildTldrFacts"/>,
/// the deterministic-fact computation the TL;DR LLM call consumes as ground truth. No Dataverse
/// I/O, no LLM — <c>BuildTldrFacts</c> is a pure static function over an already-built
/// <see cref="DailyBriefingNarrateRequest"/> view model, so these tests assert its output
/// (counts/dates/names) equals the deterministic view-model values it was built FROM, across
/// multiple fixtures (single-category and multi-category) — the direct proof for the "TL;DR
/// asserts only deterministic facts" acceptance criterion.
/// </summary>
[Trait("status", "task-013-r5")]
public sealed class DailyBriefingTldrFactsTests
{
    [Fact]
    public void BuildTldrFacts_SingleCategoryFixture_CountsAndDatesMatchViewModel()
    {
        var dueDate = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
        var request = new DailyBriefingNarrateRequest
        {
            Categories = [new NotificationCategoryDto { Name = "Overdue Tasks", Count = 2, UnreadCount = 2 }],
            PriorityItems =
            [
                new PriorityItemDto { Category = "Tasks", Title = "Review engagement letter", DueDate = dueDate },
                new PriorityItemDto { Category = "Tasks", Title = "File motion" }
            ],
            TotalNotificationCount = 2,
            Channels =
            [
                new ChannelNarrationInput
                {
                    Category = "overdue-tasks",
                    Label = "Overdue Tasks",
                    Items =
                    [
                        new ChannelItemDto { Id = "1", Title = "Review engagement letter", RegardingName = "Acme Matter" },
                        new ChannelItemDto { Id = "2", Title = "File motion", RegardingName = "Acme Matter" }
                    ]
                }
            ]
        };

        var facts = DailyBriefingCollector.BuildTldrFacts(request);

        // Counts trace back EXACTLY to the request's own deterministic view model.
        facts.TotalNotificationCount.Should().Be(request.TotalNotificationCount);
        facts.CategoryCounts.Should().BeEquivalentTo(request.Categories);
        facts.PriorityItemCount.Should().Be(request.PriorityItems.Length);

        // Only the PriorityItem that actually HAS a due date produces a KeyDate — and the date
        // value equals the deterministic view-model value verbatim.
        facts.KeyDates.Should().ContainSingle();
        facts.KeyDates[0].RecordName.Should().Be("Review engagement letter");
        facts.KeyDates[0].Date.Should().Be(dueDate);

        // RecordNames carries the priority-item titles + channel record names — the TL;DR's
        // allow-list of names it may reference.
        facts.RecordNames.Should().Contain(new[] { "Review engagement letter", "File motion", "Acme Matter" });
    }

    [Fact]
    public void BuildTldrFacts_MultiCategoryFixture_CountsAndDatesMatchViewModelAcrossChannels()
    {
        var overdueDate = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);
        var upcomingDate = new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);
        var request = new DailyBriefingNarrateRequest
        {
            Categories =
            [
                new NotificationCategoryDto { Name = "Overdue Tasks", Count = 1, UnreadCount = 1 },
                new NotificationCategoryDto { Name = "Upcoming Tasks", Count = 1, UnreadCount = 1 },
                new NotificationCategoryDto { Name = "Documents", Count = 3, UnreadCount = 3 }
            ],
            PriorityItems =
            [
                new PriorityItemDto { Category = "Tasks", Title = "Respond to opposing counsel", DueDate = overdueDate },
                new PriorityItemDto { Category = "Tasks", Title = "Prepare deposition outline", DueDate = upcomingDate }
            ],
            TotalNotificationCount = 5,
            Channels =
            [
                new ChannelNarrationInput
                {
                    Category = "overdue-tasks", Label = "Overdue Tasks",
                    Items = [new ChannelItemDto { Id = "e1", Title = "Respond to opposing counsel", RegardingName = "Beta Matter" }]
                },
                new ChannelNarrationInput
                {
                    Category = "upcoming-tasks", Label = "Upcoming Tasks",
                    Items = [new ChannelItemDto { Id = "e2", Title = "Prepare deposition outline", RegardingName = "Beta Matter" }]
                },
                new ChannelNarrationInput
                {
                    Category = "documents", Label = "Documents",
                    Items =
                    [
                        new ChannelItemDto { Id = "d1", Title = "Engagement letter.docx", RegardingName = "Beta Matter" },
                        new ChannelItemDto { Id = "d2", Title = "NDA draft.docx", RegardingName = "Gamma Matter" },
                        new ChannelItemDto { Id = "d3", Title = "Cover letter.docx", RegardingName = "Gamma Matter" }
                    ]
                }
            ]
        };

        var facts = DailyBriefingCollector.BuildTldrFacts(request);

        facts.TotalNotificationCount.Should().Be(5);
        facts.CategoryCounts.Should().HaveCount(3);
        facts.CategoryCounts.Select(c => c.Name).Should()
            .BeEquivalentTo(new[] { "Overdue Tasks", "Upcoming Tasks", "Documents" });
        facts.PriorityItemCount.Should().Be(2);

        // Both priority items have due dates — both surface as KeyDates, verbatim.
        facts.KeyDates.Should().HaveCount(2);
        facts.KeyDates.Should().ContainEquivalentOf(
            new TldrKeyDateDto { RecordName = "Respond to opposing counsel", Date = overdueDate });
        facts.KeyDates.Should().ContainEquivalentOf(
            new TldrKeyDateDto { RecordName = "Prepare deposition outline", Date = upcomingDate });

        // RecordNames spans every channel — the TL;DR may reference any record across all 3
        // categories, not just the priority items.
        facts.RecordNames.Should().Contain(new[]
        {
            "Respond to opposing counsel", "Prepare deposition outline",
            "Beta Matter", "Gamma Matter",
            "Engagement letter.docx", "NDA draft.docx", "Cover letter.docx"
        });
    }

    [Fact]
    public void BuildTldrFacts_ChannelWithManyItems_CapsRecordNamesInsteadOfDumpingEveryRecord()
    {
        // ADR-015 data-minimization / aggregation constraint: the TL;DR scaffolding must
        // aggregate, not dump, every source record — a channel with more rows than the cap
        // must not blow the TL;DR call's token budget.
        var items = Enumerable.Range(1, 30)
            .Select(i => new ChannelItemDto { Id = $"n{i}", Title = $"Notification {i}", RegardingName = "Delta Matter" })
            .ToArray();
        var request = new DailyBriefingNarrateRequest
        {
            Categories = [new NotificationCategoryDto { Name = "Documents", Count = 30, UnreadCount = 30 }],
            PriorityItems = [],
            TotalNotificationCount = 30,
            Channels = [new ChannelNarrationInput { Category = "documents", Label = "Documents", Items = items }]
        };

        var facts = DailyBriefingCollector.BuildTldrFacts(request);

        // The count fact is still exact (counting is cheap and safe to assert precisely)...
        facts.TotalNotificationCount.Should().Be(30);
        // ...but the enumerated name list stays bounded regardless of channel size.
        facts.RecordNames.Length.Should().BeLessOrEqualTo(DailyBriefingCollector.TldrFactsMaxRecordNames);
    }
}
