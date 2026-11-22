using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Identity;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 055: the Messages list, <c>GET /api/communications/threads?kind=direct</c>
/// (<see cref="CommunicationThreadReadService.ListThreadsAsync"/>). The impersonated query stays the visibility gate; each row it
/// returns is then kept only when the one Direct-pair shape rule reads it a strict 1:1 pair AND the caller is its owner or its
/// other participant. A reader whose thread Read is wider than Basic (an administrator) therefore does not see other people's
/// pairs. Boundary mocks only: the impersonated query, the caller resolver, the app-only entity reads and the share read. The
/// read service and the shape rule (<see cref="DirectMessageRecipient.ReadStrictPairShapeAsync"/>) are production code.
/// </summary>
public class CommunicationListDirectThreadsSeamTests
{
    private const string ThreadSet = "sprk_communicationthreads";
    private const string ThreadTable = "sprk_communicationthread";
    private const int ThreadTypeRecordAnchored = 100000000;
    private const int ThreadTypeDirect = 100000001;
    private const int ReadMask = 1;

    private static readonly Guid Caller = Guid.Parse("55555555-0000-0000-0000-000000000001");
    private static readonly Guid Alice = Guid.Parse("55555555-0000-0000-0000-00000000000a");
    private static readonly Guid Bob = Guid.Parse("55555555-0000-0000-0000-00000000000b");

    private readonly Mock<IImpersonatedCommunicationQuery> _query = new();
    private readonly Mock<ICallerSystemUserResolver> _resolver = new();
    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly Mock<IDataverseRecordShareService> _shares = new();
    private readonly CapturingLogger _log = new();

    private readonly Dictionary<Guid, DataverseEntity> _threads = new();
    private readonly Dictionary<Guid, List<DataversePrincipalAccess>> _threadShares = new();
    private readonly HashSet<Guid> _faultingThreads = new();
    private readonly Dictionary<Guid, string> _names = new()
    {
        [Caller] = "Casey Caller",
        [Alice] = "Alice Archer",
        [Bob] = "Bob Baker",
    };

    private string? _lastThreadQuery;
    private bool _namesFault;

    public CommunicationListDirectThreadsSeamTests()
    {
        _entities
            .Setup(e => e.RetrieveAsync(ThreadTable, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                _faultingThreads.Contains(id)
                    ? throw new HttpRequestException("thread read faulted")
                    : _threads.TryGetValue(id, out var t) ? t : new DataverseEntity(ThreadTable, Guid.Empty));
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync(ThreadTable, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, CancellationToken _) =>
                (IReadOnlyList<DataversePrincipalAccess>)(_threadShares.TryGetValue(id, out var s) ? s : new List<DataversePrincipalAccess>()));
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                if (_namesFault)
                    throw new HttpRequestException("name read faulted");
                var ids = q.Criteria.Conditions.Single(c => c.AttributeName == "systemuserid").Values.Cast<Guid>().ToHashSet();
                var collection = new EntityCollection();
                foreach (var (id, name) in _names.Where(n => ids.Contains(n.Key)))
                    collection.Entities.Add(new DataverseEntity("systemuser", id) { ["fullname"] = name });
                return collection;
            });
    }

    private CommunicationThreadReadService Sut(bool withPairDependencies = true)
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(Caller.ToString("D")));

        return new CommunicationThreadReadService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            Mock.Of<ISystemUserIdentityResolver>(),
            _log,
            appOnlyQuery: null,
            entities: withPairDependencies ? _entities.Object : null,
            recordShares: withPairDependencies ? _shares.Object : null);
    }

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test"));

    private void ImpersonatedRows(params Guid[] threadIds)
    {
        var created = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        var rows = threadIds
            .Select((id, i) => ThreadRow(id, "Direct Conversation", ThreadTypeDirect, created.AddMinutes(-i)))
            .ToArray();
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), Caller, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => _lastThreadQuery = odata)
              .ReturnsAsync(rows);
    }

    /// <summary>A Direct thread as <c>POST /threads/direct</c> creates it: owned by <paramref name="owner"/>, Open, no regarding,
    /// and one Read share to <paramref name="other"/>.</summary>
    private Guid Pair(Guid owner, Guid other)
    {
        var id = Guid.NewGuid();
        _threads[id] = new DataverseEntity(ThreadTable, id)
        {
            ["sprk_threadtype"] = new OptionSetValue(ThreadTypeDirect),
            ["ownerid"] = new EntityReference("systemuser", owner),
            ["sprk_privacystate"] = new OptionSetValue((int)ThreadPrivacyState.Open),
        };
        _threadShares[id] = new List<DataversePrincipalAccess>
        {
            new(DataversePrincipalRef.User(other), ReadMask, DateTimeOffset.UtcNow),
        };
        return id;
    }

    private Task<ThreadListResult> ListDirect(ClaimsPrincipal? principal = null, int? top = null) =>
        Sut().ListThreadsAsync(principal ?? Principal(), search: null, top: top, pageToken: null, CancellationToken.None, kind: "direct");

    // ── Control: a normal pair is listed for both of its participants, each seeing the other's name ─────────────────────────

    [Fact]
    public async Task ListThreads_APair_IsListedForItsOwner_WithTheOtherParticipantsName()
    {
        var thread = Pair(owner: Caller, other: Alice);
        ImpersonatedRows(thread);

        var result = await ListDirect();

        result.Threads.Should().ContainSingle(t => t.ThreadId == thread)
            .Which.CounterpartName.Should().Be("Alice Archer");
    }

    [Fact]
    public async Task ListThreads_APair_IsListedForItsOtherParticipant_WithTheOwnersName()
    {
        var thread = Pair(owner: Alice, other: Caller);
        ImpersonatedRows(thread);

        var result = await ListDirect();

        result.Threads.Should().ContainSingle(t => t.ThreadId == thread)
            .Which.CounterpartName.Should().Be("Alice Archer");
    }

    // ── Membership: a wider reader does not see other people's pairs ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListThreads_APairTheCallerIsNotIn_IsHidden_EvenWhenTheImpersonatedQueryReturnsIt()
    {
        // An administrator (thread Read wider than Basic) is returned every pair by row-level security.
        var theirs = Pair(owner: Alice, other: Bob);
        var mine = Pair(owner: Caller, other: Bob);
        ImpersonatedRows(theirs, mine);

        var result = await ListDirect();

        result.Threads.Select(t => t.ThreadId).Should().Equal(mine);
    }

    [Fact]
    public async Task ListThreads_ACallersPairTheImpersonatedQueryDidNotReturn_IsNotListed()
    {
        // The pair check narrows the impersonated set; it never adds a row to it (the access-model decision: row-level
        // security is the visibility gate).
        Pair(owner: Caller, other: Alice);
        var returned = Pair(owner: Caller, other: Bob);
        ImpersonatedRows(returned);

        var result = await ListDirect();

        result.Threads.Select(t => t.ThreadId).Should().Equal(returned);
        _entities.Verify(
            e => e.RetrieveAsync(ThreadTable, It.Is<Guid>(id => id != returned), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Shape: only a strict 1:1 pair is a Messages row──────────────────────────────────────────────────────────────────────

    public static TheoryData<string> NotAStrictPair() => new()
    {
        "extra-user-share", "team-share", "zero-mask-extra-share", "no-share", "private", "no-privacy",
        "typed-regarding", "team-owned", "record-anchored-type",
    };

    [Theory]
    [MemberData(nameof(NotAStrictPair))]
    public async Task ListThreads_ADirectThreadThatIsNotAStrictPair_IsHidden(string variant)
    {
        var thread = Pair(owner: Caller, other: Alice);
        var row = _threads[thread];
        var shares = _threadShares[thread];
        switch (variant)
        {
            case "extra-user-share":
                shares.Add(new(DataversePrincipalRef.User(Bob), ReadMask, DateTimeOffset.UtcNow));
                break;
            case "team-share":
                shares.Add(new(DataversePrincipalRef.Team(Guid.NewGuid()), ReadMask, DateTimeOffset.UtcNow));
                break;
            case "zero-mask-extra-share":
                shares.Add(new(DataversePrincipalRef.User(Bob), 0, DateTimeOffset.UtcNow));
                break;
            case "no-share": // e.g. a thread the inbound resolver created for one person
                shares.Clear();
                break;
            case "private":
                row["sprk_privacystate"] = new OptionSetValue((int)ThreadPrivacyState.Private);
                break;
            case "no-privacy":
                row.Attributes.Remove("sprk_privacystate");
                break;
            case "typed-regarding":
                row["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid());
                break;
            case "team-owned":
                row["ownerid"] = new EntityReference("team", Guid.NewGuid());
                break;
            case "record-anchored-type":
                row["sprk_threadtype"] = new OptionSetValue(ThreadTypeRecordAnchored);
                break;
        }

        var control = Pair(owner: Caller, other: Bob);
        ImpersonatedRows(thread, control);

        var result = await ListDirect();

        result.Threads.Select(t => t.ThreadId).Should().Equal(new[] { control }, variant + " is not a strict pair");
    }

    // ── Faults: narrower, never wider ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListThreads_ARowWhoseShapeReadFaults_IsDroppedAlone_AndTheCountIsLoggedOnceAtWarning()
    {
        var faulting = Pair(owner: Caller, other: Alice);
        var listed = Pair(owner: Caller, other: Bob);
        _faultingThreads.Add(faulting);
        ImpersonatedRows(faulting, listed);

        var result = await ListDirect();

        result.Threads.Select(t => t.ThreadId).Should().Equal(listed);
        var warnings = _log.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        warnings.Should().ContainSingle();
        warnings[0].EventId.Name.Should().Be("thread_list.direct_rows_faulted");
        warnings[0].Message.Should().Contain("1 of 2");
    }

    [Fact]
    public async Task ListThreads_EveryRowFaulting_Is503_NotAnEmptyList()
    {
        var a = Pair(owner: Caller, other: Alice);
        var b = Pair(owner: Bob, other: Caller);
        _faultingThreads.Add(a);
        _faultingThreads.Add(b);
        ImpersonatedRows(a, b);

        var act = () => ListDirect();

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task ListThreads_AShareReadFault_DropsTheRow()
    {
        var faulting = Pair(owner: Caller, other: Alice);
        var listed = Pair(owner: Caller, other: Bob);
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync(ThreadTable, faulting, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("share read faulted"));
        ImpersonatedRows(faulting, listed);

        var result = await ListDirect();

        result.Threads.Select(t => t.ThreadId).Should().Equal(listed);
    }

    [Fact]
    public async Task ListThreads_ANameReadFault_ListsTheRowsWithoutNames()
    {
        var thread = Pair(owner: Caller, other: Alice);
        ImpersonatedRows(thread);
        _namesFault = true;

        var result = await ListDirect();

        result.Threads.Should().ContainSingle().Which.CounterpartName.Should().BeNull();
    }

    [Fact]
    public async Task ListThreads_TheNameRead_AsksOnlyForTheCounterparts_OfRowsThatPassedTheMembershipCheck()
    {
        var mine = Pair(owner: Caller, other: Alice);
        var theirs = Pair(owner: Bob, other: Guid.NewGuid());
        ImpersonatedRows(mine, theirs);

        await ListDirect();

        var nameQuery = _entities.Invocations
            .Where(i => i.Method.Name == nameof(IGenericEntityService.RetrieveMultipleAsync))
            .Select(i => (QueryExpression)i.Arguments[0])
            .Single(q => q.EntityName == "systemuser");
        nameQuery.Criteria.Conditions.Single().Values.Cast<Guid>().Should().Equal(Alice);
    }

    [Fact]
    public async Task ListThreads_WithoutThePairDependencies_KindDirectIs503_NeverTheWiderList()
    {
        var act = () => Sut(withPairDependencies: false)
            .ListThreadsAsync(Principal(), null, null, null, CancellationToken.None, kind: "direct");

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(503);
        _query.Invocations.Should().BeEmpty();
    }

    // ── The query: kind=direct narrows it; kind absent or all leaves it exactly as it was ────────────────────────────────────

    [Fact]
    public async Task ListThreads_KindDirect_FiltersOnDirectTypeAndNoThreadRegarding_AndCapsThePageAt50()
    {
        ImpersonatedRows();

        await ListDirect(top: 200);

        var filter = _lastThreadQuery!.Split("$filter=")[1].Split('&')[0];
        filter.Should().Contain($"sprk_threadtype eq {ThreadTypeDirect}");
        foreach (var column in ThreadResolver.ThreadRegardingColumns)
            filter.Should().Contain($"_{column}_value eq null");
        filter.Should().NotContain("regardingreportcard", "that lookup is not on the thread table");
        _lastThreadQuery.Should().Contain("$top=51");
    }

    [Fact]
    public async Task ListThreads_KindAll_AndKindAbsent_SendTheSameQuery_AndReadNothingElse()
    {
        ImpersonatedRows(Pair(owner: Alice, other: Bob));

        await Sut().ListThreadsAsync(Principal(), "acme", 25, null, CancellationToken.None);
        var absent = _lastThreadQuery;
        await Sut().ListThreadsAsync(Principal(), "acme", 25, null, CancellationToken.None, kind: "all");
        var all = _lastThreadQuery;
        await Sut().ListThreadsAsync(Principal(), "acme", 25, null, CancellationToken.None, kind: " ALL ");

        all.Should().Be(absent);
        _lastThreadQuery.Should().Be(absent);
        absent.Should().NotContain("sprk_threadtype eq");
        _entities.Invocations.Should().BeEmpty("the all-threads list runs no pair check");
        _shares.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("directs")]
    [InlineData("record")]
    [InlineData("1")]
    public async Task ListThreads_AnyOtherKind_Is400_BeforeAnyRead(string kind)
    {
        var act = () => Sut().ListThreadsAsync(Principal(), null, null, null, CancellationToken.None, kind: kind);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(400);
        _query.Invocations.Should().BeEmpty();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, JsonElement> ThreadRow(Guid id, string name, int threadType, DateTimeOffset created)
    {
        var row = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationthreadid"] = JsonSerializer.SerializeToElement(id.ToString()),
            ["sprk_name"] = JsonSerializer.SerializeToElement(name),
            ["sprk_threadtype"] = JsonSerializer.SerializeToElement(threadType),
            ["createdon"] = JsonSerializer.SerializeToElement(created.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")),
        };
        foreach (var column in ThreadResolver.ThreadRegardingColumns)
            row[$"_{column}_value"] = JsonSerializer.SerializeToElement<string?>(null);
        return row;
    }

    private sealed class CapturingLogger : ILogger<CommunicationThreadReadService>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}
