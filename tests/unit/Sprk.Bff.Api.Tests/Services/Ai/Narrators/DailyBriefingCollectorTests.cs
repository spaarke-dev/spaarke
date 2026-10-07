// R7 Wave 12 T131 — DailyBriefingCollector unit tests (6-entity expansion).
// unified-access-control-r2 task 152 (2026-10-02) — rewritten for PEOPLE targeting + caller-context reads.
//
// Mocks at the module boundary per ADR-038 §1:
//   - IMembershipResolverService       — WHO each record is for (the people-targeting surface, ADR-034 A3). The
//                                        surface's own rules (human Created By, Assigned To via the linked contact,
//                                        personal ownership, never team/BU) are pinned in
//                                        MembershipResolverPeopleTargetingTests; here the resolver is the oracle.
//   - IImpersonatedCommunicationQuery  — WHAT the caller may see. The fake behaves like Dataverse under impersonation:
//                                        it returns a row only when the query names its id AND the caller may read it.
//
// Asserts what the caller (the widget, the email leg) would notice:
//   - every returned row came through the caller-context seam, carrying the caller's systemuserid;
//   - the collector has no app-only client at all (structural), so no returned row can be app-only;
//   - a row Dataverse denies is absent; a flagged record outside the people set is never requested;
//   - a failed read (or failed chunk, or failed candidate set) is a FAILED channel — named in FailedChannels,
//     distinguishable from an empty one — and every channel failing throws;
//   - candidate ids are chunked under MaxIdsPerImpersonatedRequest.
//
// Per tests/CLAUDE.md anti-pattern bans: NO Mock<HttpMessageHandler>, NO DI-registration tests, NO ctor null tests.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Ai.Narrators;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Narrators;

[Trait("status", "task-152-uac-r2")]
public sealed class DailyBriefingCollectorTests
{
    private static readonly Guid SystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MatterId1 = Guid.Parse("22222222-2222-2222-2222-222222222221");
    private static readonly Guid MatterId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProjectId1 = Guid.Parse("33333333-3333-3333-3333-333333333331");
    private static readonly Guid EventId1 = Guid.Parse("44444444-4444-4444-4444-444444444441");
    private static readonly Guid DocId1 = Guid.Parse("55555555-5555-5555-5555-555555555551");
    private static readonly Guid DocId2 = Guid.Parse("55555555-5555-5555-5555-555555555552");
    private static readonly Guid TodoId1 = Guid.Parse("66666666-6666-6666-6666-666666666661");

    // ─────────────────────────────────────────────────────────────────────────
    // Fakes
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dataverse-under-impersonation stand-in. A row is returned when the query names one of its id values (its own
    /// id or a parent lookup) and the caller is not denied it. Records every call for assertions.
    /// </summary>
    private sealed class FakeCallerQuery : IImpersonatedCommunicationQuery
    {
        private readonly Dictionary<string, List<(Dictionary<string, JsonElement> Row, string[] Ids)>> _rows = new();

        public List<(string EntitySet, string Query, Guid Caller)> Calls { get; } = new();
        public HashSet<Guid> DeniedToCaller { get; } = new();
        public HashSet<string> FailingEntitySets { get; } = new();
        public int FailOnCallNumber { get; set; } = -1;

        public void Add(string entitySet, Dictionary<string, JsonElement> row, params Guid[] matchIds)
        {
            if (!_rows.TryGetValue(entitySet, out var list))
            {
                _rows[entitySet] = list = new();
            }
            list.Add((row, matchIds.Select(i => i.ToString("D")).ToArray()));
        }

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            Calls.Add((entitySetName, odataQuery ?? string.Empty, callerSystemUserId));
            if (FailingEntitySets.Contains(entitySetName) || Calls.Count == FailOnCallNumber)
            {
                throw new HttpRequestException("Dataverse refused the impersonated read");
            }

            var query = odataQuery ?? string.Empty;
            IReadOnlyList<Dictionary<string, JsonElement>> result = _rows.TryGetValue(entitySetName, out var list)
                ? list.Where(r => r.Ids.Any(id => query.Contains(id, StringComparison.OrdinalIgnoreCase))
                                  && !r.Ids.Any(id => DeniedToCaller.Contains(Guid.Parse(id))))
                      .Select(r => r.Row)
                      .ToList()
                : new List<Dictionary<string, JsonElement>>();
            return Task.FromResult(result);
        }
    }

    private static Mock<IMembershipResolverService> PeopleResolver(
        IReadOnlyDictionary<string, Guid[]> idsByEntity,
        params string[] failingEntities)
    {
        var mock = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        mock.Setup(r => r.ResolveAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, MembershipResolveOptions?, CancellationToken>((_, entity, _, _) =>
            {
                if (failingEntities.Contains(entity))
                {
                    throw new InvalidOperationException($"resolver failed for {entity}");
                }
                var ids = idsByEntity.TryGetValue(entity, out var found) ? found : Array.Empty<Guid>();
                return Task.FromResult(new MembershipResponse(
                    entity, new PersonIdentity(SystemUserId), ids,
                    new Dictionary<string, IReadOnlyList<Guid>>(), ids.Length, DateTimeOffset.UtcNow.AddMinutes(5)));
            });
        return mock;
    }

    /// <summary>
    /// Like <see cref="PeopleResolver"/>, but <paramref name="pagedEntity"/>'s first page comes back FULL (with a
    /// continuation token) and the follow-up page returns <paramref name="confirmationIds"/> and no further token.
    /// </summary>
    private static Mock<IMembershipResolverService> PagingResolver(
        IReadOnlyDictionary<string, Guid[]> idsByEntity, string pagedEntity, Guid[] confirmationIds)
    {
        const string NextPage = "page-2";
        var mock = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        mock.Setup(r => r.ResolveAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, MembershipResolveOptions?, CancellationToken>((_, entity, options, _) =>
            {
                var ids = idsByEntity.TryGetValue(entity, out var found) ? found : Array.Empty<Guid>();
                string? token = null;
                if (entity == pagedEntity)
                {
                    if (options?.ContinuationToken == NextPage)
                    {
                        ids = confirmationIds;
                    }
                    else
                    {
                        token = NextPage;
                    }
                }
                return Task.FromResult(new MembershipResponse(
                    entity, new PersonIdentity(SystemUserId), ids,
                    new Dictionary<string, IReadOnlyList<Guid>>(), ids.Length, DateTimeOffset.UtcNow.AddMinutes(5),
                    ContinuationToken: token));
            });
        return mock;
    }

    private static DailyBriefingCollector Sut(FakeCallerQuery query, Mock<IMembershipResolverService> resolver) =>
        new(query, resolver.Object, NullLogger<DailyBriefingCollector>.Instance);

    private static Dictionary<string, JsonElement> Row(Dictionary<string, object?> values) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!;

    private static Dictionary<string, JsonElement> EventRow(Guid id, string name, Guid? matterId = null, string? matterName = null, int dueInDays = 1) =>
        Row(new Dictionary<string, object?>
        {
            ["sprk_eventid"] = id.ToString("D"),
            ["sprk_eventname"] = name,
            ["sprk_duedate"] = DateTime.UtcNow.Date.AddDays(dueInDays).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["modifiedon"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["_sprk_regardingmatter_value"] = matterId?.ToString("D"),
            ["_sprk_regardingmatter_value@OData.Community.Display.V1.FormattedValue"] = matterName,
        });

    private static Dictionary<string, JsonElement> DocumentRow(Guid id, string name, Guid? matterId = null, string? matterName = null) =>
        Row(new Dictionary<string, object?>
        {
            ["sprk_documentid"] = id.ToString("D"),
            ["sprk_documentname"] = name,
            ["modifiedon"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["_sprk_matter_value"] = matterId?.ToString("D"),
            ["_sprk_matter_value@OData.Community.Display.V1.FormattedValue"] = matterName,
        });

    private static Dictionary<string, JsonElement> MatterRow(Guid id, string name) =>
        Row(new Dictionary<string, object?>
        {
            ["sprk_matterid"] = id.ToString("D"),
            ["sprk_mattername"] = name,
            ["modifiedon"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        });

    private static Dictionary<string, JsonElement> ProjectRow(Guid id, string name) =>
        Row(new Dictionary<string, object?>
        {
            ["sprk_projectid"] = id.ToString("D"),
            ["sprk_projectname"] = name,
            ["modifiedon"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        });

    private static Dictionary<string, JsonElement> TodoRow(Guid id, string name, Guid? matterId = null, string? matterName = null) =>
        Row(new Dictionary<string, object?>
        {
            ["sprk_todoid"] = id.ToString("D"),
            ["sprk_name"] = name,
            ["sprk_duedate"] = DateTime.UtcNow.Date.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["modifiedon"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["_sprk_regardingmatter_value"] = matterId?.ToString("D"),
            ["_sprk_regardingmatter_value@OData.Community.Display.V1.FormattedValue"] = matterName,
        });

    private static Dictionary<string, JsonElement> FlaggedRow(string idColumn, string nameColumn, Guid id, string name, params (string Key, object? Value)[] extra)
    {
        var values = new Dictionary<string, object?>
        {
            [idColumn] = id.ToString("D"),
            [nameColumn] = name,
            ["sprk_highpriority"] = true,
            ["sprk_monitor"] = false,
            ["modifiedon"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };
        foreach (var (key, value) in extra)
        {
            values[key] = value;
        }
        return Row(values);
    }

    private static readonly Dictionary<string, Guid[]> AllSets = new()
    {
        ["sprk_event"] = new[] { EventId1 },
        ["sprk_matter"] = new[] { MatterId1, MatterId2 },
        ["sprk_project"] = new[] { ProjectId1 },
        ["sprk_document"] = new[] { DocId2 },
        ["sprk_todo"] = new[] { TodoId1 },
    };

    private static FakeCallerQuery AllChannelsQuery()
    {
        var q = new FakeCallerQuery();
        q.Add("sprk_events", EventRow(EventId1, "Task A", MatterId1, "Matter Alpha"), EventId1, MatterId1);
        q.Add("sprk_documents", DocumentRow(DocId1, "Contract draft.pdf", MatterId1, "Matter Alpha"), DocId1, MatterId1);
        q.Add("sprk_matters", MatterRow(MatterId1, "Matter Alpha"), MatterId1);
        q.Add("sprk_projects", ProjectRow(ProjectId1, "Project Beta"), ProjectId1);
        q.Add("sprk_todos", TodoRow(TodoId1, "Send agenda", MatterId1, "Matter Alpha"), TodoId1);
        return q;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Who a record is for — every candidate set comes from the people-targeting surface
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectAsync_ResolvesEveryCandidateSetThroughThePeopleTargetingSurface()
    {
        var resolver = PeopleResolver(AllSets);
        var sut = Sut(AllChannelsQuery(), resolver);

        await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        foreach (var entity in new[] { "sprk_event", "sprk_matter", "sprk_project", "sprk_document", "sprk_todo" })
        {
            resolver.Verify(r => r.ResolveAsync(
                    SystemUserId, entity,
                    It.Is<MembershipResolveOptions?>(o => o != null && o.PeopleTargeting && !o.AccessConferringOnly),
                    It.IsAny<CancellationToken>()),
                Times.Once, $"{entity} must be selected by the people-targeting surface");
        }
    }

    [Fact]
    public async Task CollectAsync_AllSixChannels_EveryRowReadAsTheCaller()
    {
        var query = AllChannelsQuery();
        var request = await Sut(query, PeopleResolver(AllSets))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        request.Channels.Select(c => c.Category).Should().Contain(new[] { "upcoming-tasks", "documents", "matters", "projects", "to-dos" });
        request.FailedChannels.Should().BeEmpty();
        query.Calls.Should().NotBeEmpty().And.OnlyContain(c => c.Caller == SystemUserId,
            "every projection read carries the caller's systemuserid (MSCRMCallerID)");
    }

    [Fact]
    public void Collector_HasNoAppOnlyDataverseClient()
    {
        // Criterion 10 made structural: the collector cannot issue an app-only read of a returned row because it
        // holds no app-only client. Its only Dataverse access is the resolver (ids, never row content) and the
        // caller-context seam.
        typeof(DailyBriefingCollector).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .Should().NotContain(t => typeof(IGenericEntityService).IsAssignableFrom(t) || t == typeof(IGenericEntityService));
    }

    [Fact]
    public async Task CollectAsync_NoQueryCarriesItsOwnOwnerOrCreatorCondition()
    {
        // ADR-034 MUST (single mechanism): who a record is for comes from the resolver only; the pre-task to-do and
        // High Priority owninguser conditions are gone.
        var query = AllChannelsQuery();
        var sut = Sut(query, PeopleResolver(AllSets));

        await sut.CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);
        await sut.CollectHighPriorityAsync(SystemUserId, CancellationToken.None);

        query.Calls.Should().OnlyContain(c =>
            !c.Query.Contains("owninguser", StringComparison.OrdinalIgnoreCase)
            && !c.Query.Contains("_ownerid_value", StringComparison.OrdinalIgnoreCase)
            && !c.Query.Contains("createdby", StringComparison.OrdinalIgnoreCase)
            && !c.Query.Contains("owningteam", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CollectAsync_Todos_SelectedOnlyByThePeopleSet()
    {
        // A to-do the resolver does not name (e.g. team-owned, naming someone else) is never requested; one it names
        // (created by the caller, or assigned to the caller's contact) is.
        var otherTodo = Guid.Parse("66666666-6666-6666-6666-666666666669");
        var query = AllChannelsQuery();
        query.Add("sprk_todos", TodoRow(otherTodo, "Team to-do for someone else"), otherTodo);

        var request = await Sut(query, PeopleResolver(AllSets))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        var todos = request.Channels.Single(c => c.Category == "to-dos").Items;
        todos.Select(i => i.Id).Should().Equal(TodoId1.ToString());
        query.Calls.Where(c => c.EntitySet == "sprk_todos")
            .Should().ContainSingle().Which.Query.Should().Contain($"sprk_todoid eq {TodoId1:D}").And.NotContain(otherTodo.ToString("D"));
    }

    [Fact]
    public async Task CollectAsync_Documents_OwnPersonTermsOrParentForTheCaller()
    {
        var query = AllChannelsQuery();
        // DocId2: created by the caller on a parent that names nobody — reachable through the document's own set.
        query.Add("sprk_documents", DocumentRow(DocId2, "My upload.docx"), DocId2);

        var request = await Sut(query, PeopleResolver(AllSets))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        request.Channels.Single(c => c.Category == "documents").Items.Select(i => i.Id)
            .Should().BeEquivalentTo(new[] { DocId1.ToString(), DocId2.ToString() });
        var documentQueries = string.Join("\n", query.Calls.Where(c => c.EntitySet == "sprk_documents").Select(c => c.Query));
        documentQueries.Should().Contain($"sprk_documentid eq {DocId2:D}")
            .And.Contain($"_sprk_matter_value eq {MatterId1:D}")
            .And.Contain($"_sprk_project_value eq {ProjectId1:D}");
    }

    [Fact]
    public async Task CollectAsync_DocumentNamingNoOneOnAParentNamingNoOne_AppearsForNoOne()
    {
        var orphanDoc = Guid.Parse("55555555-5555-5555-5555-555555555559");
        var orphanMatter = Guid.Parse("22222222-2222-2222-2222-222222222229");
        var query = new FakeCallerQuery();
        query.Add("sprk_documents", DocumentRow(orphanDoc, "App-created.pdf", orphanMatter), orphanDoc, orphanMatter);

        var request = await Sut(query, PeopleResolver(new Dictionary<string, Guid[]>()))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        request.Channels.Should().BeEmpty();
        query.Calls.Should().BeEmpty("with nothing FOR the caller there is nothing to read");
        request.FailedChannels.Should().BeEmpty("empty is not failed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // What the caller may see — readability (criterion 10)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectAsync_RecordDataverseDeniesTheCaller_IsAbsentFromEveryChannel()
    {
        var query = AllChannelsQuery();
        query.DeniedToCaller.Add(MatterId1); // e.g. re-owned into the Secure Record BU; the caller has no share

        var request = await Sut(query, PeopleResolver(AllSets))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        request.Channels.SelectMany(c => c.Items).Should().NotContain(i => i.Id == MatterId1.ToString());
        request.FailedChannels.Should().BeEmpty("a trimmed row is a correct answer, not a failure");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fail closed (criterion 11)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectAsync_OneChannelReadFails_ChannelMarkedFailed_OthersStillReturned()
    {
        var query = AllChannelsQuery();
        query.FailingEntitySets.Add("sprk_matters");

        var request = await Sut(query, PeopleResolver(AllSets))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        request.FailedChannels.Should().Equal("matters");
        request.Channels.Should().NotContain(c => c.Category == "matters");
        request.Channels.Should().Contain(c => c.Category == "projects");
    }

    [Fact]
    public async Task CollectAsync_FailedChannelIsDistinguishableFromAnEmptyOne()
    {
        var emptySets = new Dictionary<string, Guid[]>(AllSets) { ["sprk_project"] = Array.Empty<Guid>() };
        var query = AllChannelsQuery();
        query.FailingEntitySets.Add("sprk_matters");

        var request = await Sut(query, PeopleResolver(emptySets))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        request.Channels.Should().NotContain(c => c.Category == "projects" || c.Category == "matters");
        request.FailedChannels.Should().Contain("matters").And.NotContain("projects");
    }

    [Fact]
    public async Task CollectAsync_FailedCandidateSet_FailsEveryDependentChannel()
    {
        var request = await Sut(AllChannelsQuery(), PeopleResolver(AllSets, "sprk_matter"))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        request.FailedChannels.Should().BeEquivalentTo(new[] { "upcoming-tasks", "overdue-tasks", "documents", "matters" },
            "a channel that would have depended on the failed matter set must not silently shrink");
        request.Channels.Should().Contain(c => c.Category == "projects");
    }

    [Fact]
    public async Task CollectAsync_FailedChunk_FailsTheChannel_NeverShrinksIt()
    {
        var manyMatters = Enumerable.Range(1, DailyBriefingCollector.MaxIdsPerImpersonatedRequest + 5)
            .Select(i => Guid.Parse($"22222222-0000-0000-0000-{i:D12}"))
            .ToArray();
        var sets = new Dictionary<string, Guid[]> { ["sprk_matter"] = manyMatters };
        var query = new FakeCallerQuery();
        foreach (var id in manyMatters)
        {
            query.Add("sprk_matters", MatterRow(id, $"Matter {id}"), id);
        }

        // Fail the SECOND matter chunk only (calls run per channel; isolate by making it the 2nd sprk_matters call).
        var failing = new FakeCallerQueryFailingNthCallFor("sprk_matters", 2, query);
        var request = await new DailyBriefingCollector(failing, PeopleResolver(sets).Object, NullLogger<DailyBriefingCollector>.Instance)
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        request.FailedChannels.Should().Contain("matters");
        request.Channels.Should().NotContain(c => c.Category == "matters",
            "the first chunk's rows must not be served as if they were the whole channel");
    }

    [Fact]
    public async Task CollectAsync_EveryChannelFails_Throws_NeverAnEmptyBriefing()
    {
        var query = AllChannelsQuery();
        foreach (var set in new[] { "sprk_events", "sprk_documents", "sprk_matters", "sprk_projects", "sprk_todos" })
        {
            query.FailingEntitySets.Add(set);
        }

        var act = () => Sut(query, PeopleResolver(AllSets))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*every channel*");
    }

    [Fact]
    public async Task CollectAsync_CandidateIdsAreChunked()
    {
        var manyMatters = Enumerable.Range(1, (DailyBriefingCollector.MaxIdsPerImpersonatedRequest * 2) + 1)
            .Select(i => Guid.Parse($"22222222-0000-0000-0000-{i:D12}"))
            .ToArray();
        var query = new FakeCallerQuery();

        await Sut(query, PeopleResolver(new Dictionary<string, Guid[]> { ["sprk_matter"] = manyMatters }))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        var matterCalls = query.Calls.Where(c => c.EntitySet == "sprk_matters").ToList();
        matterCalls.Should().HaveCount(3);
        matterCalls.Should().OnlyContain(c =>
            CountOf(c.Query, "sprk_matterid eq") <= DailyBriefingCollector.MaxIdsPerImpersonatedRequest);
    }

    // Verifier round 1 item 4: a people-targeted set is read to completion (up to the resolver's ceiling); a set
    // LARGER than the ceiling fails its channels instead of serving an arbitrary GUID-ordered subset.

    [Fact]
    public async Task CollectAsync_PeopleSetLargerThanTheResolverCeiling_FailsItsChannel_NeverATruncatedList()
    {
        var beyondTheCeiling = Guid.Parse("66666666-6666-6666-6666-6666666666ff");
        var resolver = PagingResolver(AllSets, pagedEntity: "sprk_todo", confirmationIds: new[] { beyondTheCeiling });

        var request = await Sut(AllChannelsQuery(), resolver)
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        request.FailedChannels.Should().Contain("to-dos",
            "more to-dos are FOR the caller than one read carries — 'could not be loaded', never a silent subset");
        request.Channels.Should().NotContain(c => c.Category == "to-dos");
        request.Channels.Should().Contain(c => c.Category == "matters", "other channels are unaffected");
        resolver.Verify(r => r.ResolveAsync(
                SystemUserId, "sprk_todo",
                It.Is<MembershipResolveOptions?>(o => o != null && o.PeopleTargeting
                    && o.Limit == MembershipResolveOptions.MaxLimit && o.ContinuationToken == null),
                It.IsAny<CancellationToken>()),
            Times.Once, "the candidate set is read at the resolver's ceiling, not the 500-row default page");
    }

    [Fact]
    public async Task CollectAsync_PeopleSetEndingExactlyAtTheCeiling_IsComplete_NotFailed()
    {
        // The resolver emits a token whenever a page comes back full; the confirmation read finds nothing more.
        var resolver = PagingResolver(AllSets, pagedEntity: "sprk_todo", confirmationIds: Array.Empty<Guid>());

        var request = await Sut(AllChannelsQuery(), resolver)
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        request.FailedChannels.Should().BeEmpty();
        request.Channels.Single(c => c.Category == "to-dos").Items.Select(i => i.Id).Should().Equal(TodoId1.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // High Priority (criterion 9) — flagged records FOR the caller, read as the caller
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectHighPriorityAsync_FlaggedRecordOutsideThePeopleSet_IsNeverReturned_ForAnyEntity()
    {
        var specs = new (string Entity, string Set, string IdCol, string NameCol)[]
        {
            ("sprk_matter", "sprk_matters", "sprk_matterid", "sprk_mattername"),
            ("sprk_project", "sprk_projects", "sprk_projectid", "sprk_projectname"),
            ("sprk_invoice", "sprk_invoices", "sprk_invoiceid", "sprk_name"),
            ("sprk_document", "sprk_documents", "sprk_documentid", "sprk_documentname"),
            ("sprk_workassignment", "sprk_workassignments", "sprk_workassignmentid", "sprk_name"),
            ("sprk_event", "sprk_events", "sprk_eventid", "sprk_eventname"),
            ("sprk_todo", "sprk_todos", "sprk_todoid", "sprk_name"),
        };

        var sets = new Dictionary<string, Guid[]>();
        var query = new FakeCallerQuery();
        var mine = new List<Guid>();
        var notMine = new List<Guid>();
        foreach (var (entity, set, idCol, nameCol) in specs)
        {
            var forMe = Guid.NewGuid();
            var notForMe = Guid.NewGuid();
            sets[entity] = new[] { forMe };
            query.Add(set, FlaggedRow(idCol, nameCol, forMe, $"{entity} for me"), forMe);
            query.Add(set, FlaggedRow(idCol, nameCol, notForMe, $"{entity} flagged for nobody"), notForMe);
            mine.Add(forMe);
            notMine.Add(notForMe);
        }

        var result = await Sut(query, PeopleResolver(sets)).CollectHighPriorityAsync(SystemUserId, CancellationToken.None);

        result.Items.Select(i => Guid.Parse(i.EntityId)).Should().BeEquivalentTo(mine);
        result.FailedEntityTypes.Should().BeEmpty();
        query.Calls.Should().OnlyContain(c => c.Caller == SystemUserId);
        query.Calls.Should().OnlyContain(c => notMine.All(n => !c.Query.Contains(n.ToString("D"))),
            "a flagged record outside the caller's people-targeted set is never even requested");
    }

    [Fact]
    public async Task CollectHighPriorityAsync_Document_IncludesDocumentsOnAParentForTheCaller()
    {
        var parentDoc = Guid.Parse("55555555-5555-5555-5555-55555555555a");
        var query = new FakeCallerQuery();
        query.Add("sprk_documents", FlaggedRow("sprk_documentid", "sprk_documentname", parentDoc, "On my matter",
            ("_sprk_matter_value", MatterId1.ToString("D"))), parentDoc, MatterId1);

        var result = await Sut(query, PeopleResolver(new Dictionary<string, Guid[]> { ["sprk_matter"] = new[] { MatterId1 } }))
            .CollectHighPriorityAsync(SystemUserId, CancellationToken.None);

        result.Items.Should().ContainSingle(i => i.EntityId == parentDoc.ToString());
    }

    [Fact]
    public async Task CollectHighPriorityAsync_OneEntityFails_NamedInFailedEntityTypes()
    {
        var query = new FakeCallerQuery();
        query.Add("sprk_matters", FlaggedRow("sprk_matterid", "sprk_mattername", MatterId1, "Flagged"), MatterId1);
        query.FailingEntitySets.Add("sprk_invoices");
        var sets = new Dictionary<string, Guid[]> { ["sprk_matter"] = new[] { MatterId1 }, ["sprk_invoice"] = new[] { Guid.NewGuid() } };

        var result = await Sut(query, PeopleResolver(sets)).CollectHighPriorityAsync(SystemUserId, CancellationToken.None);

        result.FailedEntityTypes.Should().Equal("sprk_invoice");
        result.Items.Should().ContainSingle(i => i.EntityId == MatterId1.ToString());
    }

    [Fact]
    public async Task CollectHighPriorityAsync_EveryEntityFails_Throws()
    {
        var act = () => Sut(new FakeCallerQuery(), PeopleResolver(new Dictionary<string, Guid[]>(),
                "sprk_matter", "sprk_project", "sprk_invoice", "sprk_document", "sprk_workassignment", "sprk_event", "sprk_todo"))
            .CollectHighPriorityAsync(SystemUserId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CollectHighPriorityAsync_OrderingAndDedupPreserved()
    {
        var eventId = Guid.Parse("88888888-8888-8888-8888-888888888883");
        var projectId = Guid.Parse("88888888-8888-8888-8888-888888888882");
        var matterId = Guid.Parse("88888888-8888-8888-8888-888888888881");
        var query = new FakeCallerQuery();
        query.Add("sprk_matters", FlaggedRow("sprk_matterid", "sprk_mattername", matterId, "Zeta Matter"), matterId);
        query.Add("sprk_projects", FlaggedRow("sprk_projectid", "sprk_projectname", projectId, "Alpha Project"), projectId);
        query.Add("sprk_events", FlaggedRow("sprk_eventid", "sprk_eventname", eventId, "Earliest-Due Task",
            ("sprk_duedate", DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd"))), eventId);
        var sets = new Dictionary<string, Guid[]>
        {
            ["sprk_matter"] = new[] { matterId }, ["sprk_project"] = new[] { projectId }, ["sprk_event"] = new[] { eventId },
        };

        var items = (await Sut(query, PeopleResolver(sets)).CollectHighPriorityAsync(SystemUserId, CancellationToken.None)).Items;

        items.Should().HaveCount(3);
        items[0].EntityId.Should().Be(eventId.ToString(), "the only due-dated item sorts first");
        items[1].Name.Should().Be("Alpha Project");
        items[2].Name.Should().Be("Zeta Matter");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Projection shape (unchanged contract)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectAsync_PerBulletEntityLinkMetadataPopulated()
    {
        var request = await Sut(AllChannelsQuery(), PeopleResolver(AllSets))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        foreach (var item in request.Channels.SelectMany(c => c.Items))
        {
            item.RegardingId.Should().NotBeNullOrEmpty();
            item.RegardingEntityType.Should().NotBeNullOrEmpty();
            item.RegardingName.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task CollectAsync_MatterAndProjectItems_AreSelfRegarding()
    {
        var request = await Sut(AllChannelsQuery(), PeopleResolver(AllSets))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        var matter = request.Channels.Single(c => c.Category == "matters").Items.Single();
        matter.RegardingEntityType.Should().Be("sprk_matter");
        matter.RegardingId.Should().Be(MatterId1.ToString());
        matter.RegardingName.Should().Be("Matter Alpha");

        var project = request.Channels.Single(c => c.Category == "projects").Items.Single();
        project.RegardingEntityType.Should().Be("sprk_project");
        project.RegardingId.Should().Be(ProjectId1.ToString());
    }

    [Fact]
    public async Task CollectAsync_WithEmptySystemUserId_Throws()
    {
        var act = () => Sut(new FakeCallerQuery(), PeopleResolver(AllSets))
            .CollectAsync(Guid.Empty, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("systemUserId is required*");
    }

    [Fact]
    public async Task CollectAsync_CategoriesTotalAndTldrFactsMatchActualItems()
    {
        var query = new FakeCallerQuery();
        query.Add("sprk_matters", MatterRow(MatterId1, "Matter Alpha"), MatterId1);
        query.Add("sprk_matters", MatterRow(MatterId2, "Matter Beta"), MatterId2);
        query.Add("sprk_todos", TodoRow(TodoId1, "Send agenda"), TodoId1);
        var sets = new Dictionary<string, Guid[]> { ["sprk_matter"] = new[] { MatterId1, MatterId2 }, ["sprk_todo"] = new[] { TodoId1 } };

        var request = await Sut(query, PeopleResolver(sets))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        request.TotalNotificationCount.Should().Be(3);
        request.Categories.Should().Contain(c => c.Name == "Matters" && c.Count == 2);
        request.Categories.Should().Contain(c => c.Name == "To Dos" && c.Count == 1);
        request.TldrFacts.Should().NotBeNull();
        request.TldrFacts!.TotalNotificationCount.Should().Be(request.TotalNotificationCount);
    }

    [Fact]
    public async Task CollectAsync_EventReachableViaBothTaskChannels_AppearsExactlyOnce()
    {
        var query = new FakeCallerQuery();
        query.Add("sprk_events", EventRow(EventId1, "Task reachable via both date fields"), EventId1);

        var request = await Sut(query, PeopleResolver(new Dictionary<string, Guid[]> { ["sprk_event"] = new[] { EventId1 } }))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        request.Channels.SelectMany(c => c.Items).Where(i => i.Id == EventId1.ToString()).Should().ContainSingle();
        request.TotalNotificationCount.Should().Be(1);
    }

    [Fact]
    public async Task CollectHighPriorityAsync_EventQuery_SelectsTheRealDescriptionColumn()
    {
        // master #1032 (spaarke-ontology-platform-r1): "sprk_eventdescription" does NOT exist on sprk_event, so selecting
        // it made Dataverse reject the whole retrieve and the briefing could see no tasks at all. The real column is
        // "sprk_description". Pinned here against task 152's impersonated, people-targeted query shape.
        var eventId = Guid.Parse("66666666-6666-6666-6666-66666666666e");
        var query = new FakeCallerQuery();
        query.Add("sprk_events", FlaggedRow("sprk_eventid", "sprk_eventname", eventId, "Task"), eventId);

        await Sut(query, PeopleResolver(new Dictionary<string, Guid[]> { ["sprk_event"] = new[] { eventId } }))
            .CollectHighPriorityAsync(SystemUserId, CancellationToken.None);

        var eventCalls = query.Calls.Where(c => c.EntitySet == "sprk_events").ToList();
        eventCalls.Should().NotBeEmpty();
        eventCalls.Should().OnlyContain(c => c.Query.Contains("sprk_description") && !c.Query.Contains("sprk_eventdescription"));
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    /// <summary>Delegates to an inner fake but throws on the Nth call for one entity set.</summary>
    private sealed class FakeCallerQueryFailingNthCallFor : IImpersonatedCommunicationQuery
    {
        private readonly string _entitySet;
        private readonly int _failOn;
        private readonly IImpersonatedCommunicationQuery _inner;
        private int _seen;

        public FakeCallerQueryFailingNthCallFor(string entitySet, int failOn, IImpersonatedCommunicationQuery inner)
        {
            _entitySet = entitySet;
            _failOn = failOn;
            _inner = inner;
        }

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            if (entitySetName == _entitySet && Interlocked.Increment(ref _seen) == _failOn)
            {
                throw new HttpRequestException("chunk refused");
            }
            return _inner.QueryAsync(entitySetName, odataQuery, callerSystemUserId, ct);
        }
    }
    // ── Task 098: "today" is the CALLER's local day, not the UTC day ────────────────────────────────────────────────
    // Pinned at 2026-10-06T01:00Z = 21:00 on Oct 5 in New York: the UTC day is already Oct 6. sprk_event's due dates
    // are Dataverse Date Only ("yyyy-MM-dd"). The former code took "today" from DateTime.UtcNow, so at this instant a
    // task due Oct 5 read as Overdue, the overdue cutoff moved a day, and a to-do due Oct 5 dropped out of the digest.

    private static readonly DateTimeOffset EveningEastern = DateTimeOffset.Parse("2026-10-06T01:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The caller-context seam, answering the caller's own usersettings / time-zone reads as an Eastern user.</summary>
    private sealed class EasternCallerQuery(IImpersonatedCommunicationQuery inner) : IImpersonatedCommunicationQuery
    {
        public List<string> ZoneReads { get; } = new();

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            IReadOnlyList<Dictionary<string, JsonElement>> one(string key, object value) =>
                new List<Dictionary<string, JsonElement>> { Row(new Dictionary<string, object?> { [key] = value }) };
            switch (entitySetName)
            {
                case "usersettingscollection":
                    ZoneReads.Add($"{entitySetName}?{odataQuery} as {callerSystemUserId}");
                    return Task.FromResult(one("timezonecode", 35));
                case "timezonedefinitions":
                    ZoneReads.Add($"{entitySetName}?{odataQuery} as {callerSystemUserId}");
                    return Task.FromResult(one("standardname", "Eastern Standard Time"));
                default:
                    return inner.QueryAsync(entitySetName, odataQuery, callerSystemUserId, ct);
            }
        }
    }

    private static DailyBriefingCollector SutAt(IImpersonatedCommunicationQuery query, Mock<IMembershipResolverService> resolver) =>
        new(query, resolver.Object, NullLogger<DailyBriefingCollector>.Instance, new FakeTimeProvider(EveningEastern));

    [Fact]
    public async Task CollectHighPriorityAsync_ClassifiesAgainstTheCallersLocalToday()
    {
        var dueToday = Guid.NewGuid();
        var dueYesterday = Guid.NewGuid();
        var dueTomorrow = Guid.NewGuid();
        var inner = new FakeCallerQuery();
        inner.Add("sprk_events", FlaggedRow("sprk_eventid", "sprk_eventname", dueToday, "due today (Eastern)", ("sprk_duedate", "2026-10-05")), dueToday);
        inner.Add("sprk_events", FlaggedRow("sprk_eventid", "sprk_eventname", dueYesterday, "due yesterday", ("sprk_duedate", "2026-10-04")), dueYesterday);
        inner.Add("sprk_events", FlaggedRow("sprk_eventid", "sprk_eventname", dueTomorrow, "due tomorrow", ("sprk_duedate", "2026-10-06")), dueTomorrow);
        var query = new EasternCallerQuery(inner);

        var result = await SutAt(query, PeopleResolver(new Dictionary<string, Guid[]>
        {
            ["sprk_event"] = new[] { dueToday, dueYesterday, dueTomorrow },
        })).CollectHighPriorityAsync(SystemUserId, CancellationToken.None);

        string ActionOf(Guid id) => result.Items.Single(i => i.EntityId == id.ToString()).Action;
        ActionOf(dueToday).Should().Be("DueToday", "Oct 5 is TODAY for the caller at 21:00 Eastern; the UTC day (Oct 6) made it Overdue");
        ActionOf(dueYesterday).Should().Be("Overdue");
        ActionOf(dueTomorrow).Should().Be("DueSoon");
        query.ZoneReads.Should().OnlyContain(r => r.EndsWith($"as {SystemUserId}"), "the time zone is read AS the caller (no app-only client)");
    }

    [Fact]
    public async Task CollectAsync_OverdueCutoffAndToDoFloor_AreTheCallersLocalDay()
    {
        var inner = AllChannelsQuery();
        var query = new EasternCallerQuery(inner);

        await SutAt(query, PeopleResolver(AllSets)).CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        inner.Calls.Where(c => c.EntitySet == "sprk_events" && c.Query.Contains("OnOrBefore"))
            .Should().NotBeEmpty().And.OnlyContain(c => c.Query.Contains("PropertyValue='2026-09-30'"),
                "overdue = on or before (local today Oct 5 - 5 days); the UTC day gave 2026-10-01");
        inner.Calls.Where(c => c.EntitySet == "sprk_todos")
            .Should().NotBeEmpty().And.OnlyContain(c => c.Query.Contains("OnOrAfter(PropertyName='sprk_duedate',PropertyValue='2026-10-05')"),
                "a to-do due today (Oct 5 for the caller) stays in the digest");
    }

    [Fact]
    public async Task CollectAsync_WhenTheCallersZoneCannotBeRead_UsesTheUtcDay()
    {
        var query = AllChannelsQuery(); // no usersettings row for the caller → no-timezonecode → UTC fallback

        await SutAt(query, PeopleResolver(AllSets)).CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        query.Calls.Where(c => c.EntitySet == "sprk_events" && c.Query.Contains("OnOrBefore"))
            .Should().OnlyContain(c => c.Query.Contains("PropertyValue='2026-10-01'"));
    }

    /// <summary>A caller-context seam whose time-zone read times out (HttpClient: a TaskCanceledException nobody requested).</summary>
    private sealed class TimingOutZoneQuery(IImpersonatedCommunicationQuery inner) : IImpersonatedCommunicationQuery
    {
        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct) =>
            entitySetName == "usersettingscollection"
                ? throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout")
                : inner.QueryAsync(entitySetName, odataQuery, callerSystemUserId, ct);
    }

    [Fact]
    public async Task CollectAsync_WhenTheZoneReadTimesOut_FallsBackToTheUtcDay_InsteadOfFailingTheBriefing()
    {
        var inner = AllChannelsQuery();

        var act = () => SutAt(new TimingOutZoneQuery(inner), PeopleResolver(AllSets))
            .CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        await act.Should().NotThrowAsync("only the caller's own cancellation may propagate; a timeout dates by UTC");
        inner.Calls.Where(c => c.EntitySet == "sprk_events" && c.Query.Contains("OnOrBefore"))
            .Should().OnlyContain(c => c.Query.Contains("PropertyValue='2026-10-01'"));
    }

    [Theory]
    [InlineData("2026-10-05", "2026-10-05")]          // Date Only value: the day as written
    [InlineData("2026-10-06T03:30:00Z", "2026-10-05")] // UserLocal instant: 23:30 Oct 5 in the caller's zone
    public void DueDayOf_IsTheCalendarDayInTheCallersZone(string raw, string expected) =>
        DailyBriefingCollector.DueDayOf(raw, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"))
            .Should().Be(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    // ─────────────────────────────────────────────────────────────────────────
    // D-27 (task 065, folded into 098): sprk_duedate is THE due date — the one the Do lane shows and Reschedule
    // writes. sprk_finalduedate is informational and decides nothing: not membership, not order, not the date shown.
    // The caller's local today is 2026-10-05 (EveningEastern); every event below has two DIFFERENT dates.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectHighPriorityAsync_ClassifiesAndOrdersTaskEventsBySprkDuedate_NotTheFinalDueDate()
    {
        var overdue = Guid.NewGuid();
        var rescheduled = Guid.NewGuid();
        var inner = new FakeCallerQuery();
        inner.Add("sprk_events", FlaggedRow("sprk_eventid", "sprk_eventname", overdue, "due yesterday, final due later",
            ("sprk_duedate", "2026-10-04"), ("sprk_finalduedate", "2026-10-20")), overdue);
        inner.Add("sprk_events", FlaggedRow("sprk_eventid", "sprk_eventname", rescheduled, "rescheduled to Oct 8, final due passed",
            ("sprk_duedate", "2026-10-08"), ("sprk_finalduedate", "2026-10-01")), rescheduled);

        var items = (await SutAt(new EasternCallerQuery(inner), PeopleResolver(new Dictionary<string, Guid[]>
        {
            ["sprk_event"] = new[] { overdue, rescheduled },
        })).CollectHighPriorityAsync(SystemUserId, CancellationToken.None)).Items;

        string ActionOf(Guid id) => items.Single(i => i.EntityId == id.ToString()).Action;
        ActionOf(overdue).Should().Be("Overdue", "sprk_duedate (Oct 4) has passed; the later final due date does not rescue it");
        ActionOf(rescheduled).Should().Be("DueSoon", "a reschedule moved sprk_duedate to Oct 8; the passed final due date is informational");
        items.Select(i => i.EntityId).Should().ContainInOrder(new[] { overdue.ToString(), rescheduled.ToString() },
            "ordered by sprk_duedate (Oct 4 before Oct 8); the final due dates would order them the other way");
    }

    /// <summary>
    /// Evaluates the task channels' date clauses on sprk_events the way Dataverse would, for a caller whose local
    /// today is <paramref name="today"/>: <c>NextXDays(X, N)</c> is today ≤ X ≤ today + N, <c>OnOrBefore(X, D)</c> is
    /// X ≤ D, clauses joined by <c>or</c>. A fake that ignored the filter could not tell which column decides.
    /// </summary>
    private sealed class DateEvaluatingEventsQuery(IImpersonatedCommunicationQuery inner, DateOnly today) : IImpersonatedCommunicationQuery
    {
        private static readonly System.Text.RegularExpressions.Regex Clause = new(
            @"Microsoft\.Dynamics\.CRM\.(?<op>NextXDays|OnOrBefore)\(PropertyName='(?<col>\w+)',PropertyValue='?(?<val>[^')]+)'?\)");

        public async Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            var rows = await inner.QueryAsync(entitySetName, odataQuery, callerSystemUserId, ct);
            var clauses = Clause.Matches(odataQuery ?? string.Empty);
            if (entitySetName != "sprk_events" || clauses.Count == 0)
                return rows;

            bool Holds(Dictionary<string, JsonElement> row, System.Text.RegularExpressions.Match m)
            {
                if (!row.TryGetValue(m.Groups["col"].Value, out var v) || v.ValueKind != JsonValueKind.String)
                    return false;
                var day = DateOnly.Parse(v.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var arg = m.Groups["val"].Value;
                return m.Groups["op"].Value == "NextXDays"
                    ? day >= today && day <= today.AddDays(int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture))
                    : day <= DateOnly.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
            }

            return rows.Where(r => clauses.Any(m => Holds(r, m))).ToList();
        }
    }

    private static Dictionary<string, JsonElement> DatedEventRow(Guid id, string name, string dueDate, string finalDueDate) =>
        Row(new Dictionary<string, object?>
        {
            ["sprk_eventid"] = id.ToString("D"),
            ["sprk_eventname"] = name,
            ["sprk_duedate"] = dueDate,
            ["sprk_finalduedate"] = finalDueDate,
        });

    [Fact]
    public async Task CollectAsync_TaskChannels_SelectOrderAndShowBySprkDuedate_NotTheFinalDueDate()
    {
        var overdue = Guid.NewGuid();     // due Sep 28 (past the 5-day cutoff, Sep 30); final due Oct 7 (in the window)
        var rescheduled = Guid.NewGuid(); // a reschedule moved sprk_duedate to Oct 8; the final due date Sep 20 has passed
        var dueOct9 = Guid.NewGuid();     // due Oct 9, final due Oct 6
        var dueOct7 = Guid.NewGuid();     // due Oct 7, final due Oct 30 (outside the window)
        var inner = new FakeCallerQuery();
        inner.Add("sprk_events", DatedEventRow(overdue, "Overdue by its due date", "2026-09-28", "2026-10-07"), overdue);
        inner.Add("sprk_events", DatedEventRow(rescheduled, "Rescheduled", "2026-10-08", "2026-09-20"), rescheduled);
        inner.Add("sprk_events", DatedEventRow(dueOct9, "Due Oct 9", "2026-10-09", "2026-10-06"), dueOct9);
        inner.Add("sprk_events", DatedEventRow(dueOct7, "Due Oct 7", "2026-10-07", "2026-10-30"), dueOct7);
        var query = new EasternCallerQuery(new DateEvaluatingEventsQuery(inner, new DateOnly(2026, 10, 5)));

        var request = await SutAt(query, PeopleResolver(new Dictionary<string, Guid[]>
        {
            ["sprk_event"] = new[] { overdue, rescheduled, dueOct9, dueOct7 },
        })).CollectAsync(SystemUserId, DailyBriefingCollector.BriefingWindowOptions.Default, CancellationToken.None);

        string[] Ids(string category) =>
            request.Channels.SingleOrDefault(c => c.Category == category)?.Items.Select(i => i.Id).ToArray() ?? Array.Empty<string>();

        Ids(DailyBriefingCollector.ChannelOverdueTasks).Should().Equal(new[] { overdue.ToString() },
            "overdue by sprk_duedate; the rescheduled task's passed final due date does not make it overdue");
        Ids(DailyBriefingCollector.ChannelUpcomingTasks).Should().Equal(
            new[] { dueOct7.ToString(), rescheduled.ToString(), dueOct9.ToString() },
            "upcoming = sprk_duedate within 5 days, ordered by sprk_duedate (Oct 7, 8, 9); the overdue task's final due "
            + "date (Oct 7) does not pull it in");
        request.PriorityItems.Single(p => p.Title == "Due Oct 7").DueDate!.Value.Date
            .Should().Be(new DateTime(2026, 10, 7), "the date shown is sprk_duedate, not the final due date (Oct 30)");
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
