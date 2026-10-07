// spaarke-ontology-platform-r1 task 097 round 9 — OWNER DECISION A (2026-10-06): ONE predicate, EventStatusCode.IsOpenWork
// (Draft, Open, On Hold, Reassigned), decides which events the To Do generation rules act on. Rules 1 and 3 (overdue /
// upcoming) exclude the NOT-open-work statuses in their query; Rule 5 (assigned tasks) selects open work the same way.
//
// Each test runs the real generation pass against fakes that EVALUATE the query the service sends — the way Dataverse
// would — so a rule whose filter disagrees with the predicate visibly creates (or fails to create) a To Do. A fake that
// returned a fixed row set regardless of the filter could not catch that (review M4 survived 71/71 for exactly that reason).

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

[Trait("status", "task-097-ontology-r1")]
public class TodoGenerationOpenWorkTests
{
    private static readonly Guid EventId = Guid.Parse("97097097-0009-4000-8000-000000000001");

    private readonly Mock<IDataverseService> _dataverse = new(MockBehavior.Loose);
    private readonly Mock<IEventDataverseService> _events = new(MockBehavior.Loose);
    private readonly Mock<ICommunicationDataverseService> _comm = new(MockBehavior.Loose);
    private readonly List<Entity> _created = new();
    private readonly Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble _ownership = new();

    public TodoGenerationOpenWorkTests()
    {
        _comm.Setup(c => c.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);
        _dataverse.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => _created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName != "sprk_event"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        _dataverse.Setup(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) => new Entity(entity, id));
        StoreEventsForRules1And3(overdue: null, upcoming: null);
        StoreEvents();
    }

    public static TheoryData<int, bool> EveryLiveStatus => new()
    {
        { EventStatusCode.Draft, true },
        { EventStatusCode.Open, true },
        { EventStatusCode.OnHold, true },
        { EventStatusCode.Reassigned, true },
        { EventStatusCode.Completed, false },
        { EventStatusCode.Closed, false },
        { EventStatusCode.Transferred, false },
        { EventStatusCode.Cancelled, false },
        { EventStatusCode.NoFurtherAction, false },
    };

    // ── Rule 5: assigned tasks ────────────────────────────────────────────────────────────────────────────────

    private static readonly Guid AssigneeContact = Guid.Parse("97097097-0010-4000-8000-0000000000c1");

    [Theory]
    [MemberData(nameof(EveryLiveStatus))]
    public async Task Rule5_AnAssignedEvent_GetsAnAssignedTodo_ExactlyWhenItIsOpenWork(int status, bool expected)
    {
        StoreEvents(Event(EventId, "Review NDA", status, AssigneeContact));

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        _created.Any(t => ((string)t["sprk_name"]).StartsWith("Assigned:")).Should().Be(expected,
            $"{EventStatusCode.GetDisplayName(status)} {(expected ? "is" : "is not")} open work (owner decision A)");
        expected.Should().Be(EventStatusCode.IsOpenWork(status), "the expectation table IS the predicate");
    }

    // ── Round 10: Rule 5 is for ASSIGNED events (sprk_assignedto set), ordered, and paged ─────────────────────

    [Theory]
    [InlineData(1)]          // Draft — every new event's default
    [InlineData(659490001)]  // Open
    public async Task Rule5_AnUnassignedOpenWorkEvent_GetsNoTodo(int status)
    {
        StoreEvents(Event(EventId, "Unassigned filing", status, assignee: null));

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        _created.Should().NotContain(t => ((string)t["sprk_name"]).StartsWith("Assigned:"),
            "the rule is for ASSIGNED events; an event nobody is assigned to has no one to remind");
    }

    [Theory]
    [InlineData(1)]          // Draft
    [InlineData(659490006)]  // On Hold
    [InlineData(659490007)]  // Reassigned
    public async Task Rule5_AnAssignedDraftOnHoldOrReassignedEvent_GetsOne(int status)
    {
        StoreEvents(Event(EventId, "Assigned filing", status, AssigneeContact));

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        _created.Should().ContainSingle(t => (string)t["sprk_name"] == "Assigned: Assigned filing");
    }

    [Fact]
    public async Task Rule5_SendsADeterministicOrder_LikeRules1And3()
    {
        StoreEvents();

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        _assignedQueries.Should().NotBeEmpty();
        _assignedQueries[0].Orders.Select(o => (o.AttributeName, o.OrderType)).Should().Equal(
            ("sprk_duedate", OrderType.Ascending), ("createdon", OrderType.Descending), ("sprk_eventid", OrderType.Ascending));
        _assignedQueries[0].TopCount.Should().BeNull("paged, never TopCount-capped");
    }

    [Fact]
    public async Task Rule5_ReadsEveryPage_NotOnlyTheFirstHundred()
    {
        // 150 assigned open events: the former TopCount=100 silently dropped the last 50.
        var events = Enumerable.Range(1, 150)
            .Select(i => Event(Guid.NewGuid(), $"Task {i:000}", EventStatusCode.Open, AssigneeContact))
            .ToArray();
        StoreEvents(events);

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        _created.Count(t => ((string)t["sprk_name"]).StartsWith("Assigned:")).Should().Be(150);
        _assignedQueries.Select(q => q.PageInfo.PageNumber).Should().Equal(1, 2);
        _cookieViolations.Should().BeEmpty("page N+1 must carry page N's returned paging cookie");
    }

    // ── Round 11: every rule reads every page through the ONE pager; the cap is logged once; cancellation propagates ──

    [Fact]
    public async Task Rule1_ReadsEveryPage_NotOnlyTheFirstHundred()
    {
        var overdue = Enumerable.Range(1, 150).Select(i => new EventEntity
        {
            Id = Guid.NewGuid(), Name = $"Overdue {i:000}", StatusCode = EventStatusCode.Open, DueDate = DateTime.UtcNow.Date.AddDays(-2),
        }).ToArray();
        StoreManyEventsForRules1And3(overdue, Array.Empty<EventEntity>());

        await CreateService(eventSourced: true).RunGenerationPassAsync(CancellationToken.None);

        _created.Count(t => ((string)t["sprk_name"]).StartsWith("Overdue:")).Should().Be(150);
        _eventWindows.Where(w => w.From is null).Select(w => (w.Skip, w.Top)).Should().Equal((0, 100), (100, 100));
    }

    [Fact]
    public async Task Rule3_ReadsEveryPage_NotOnlyTheFirstHundred()
    {
        var upcoming = Enumerable.Range(1, 150).Select(i => new EventEntity
        {
            Id = Guid.NewGuid(), Name = $"Hearing {i:000}", StatusCode = EventStatusCode.Open, DueDate = DateTime.UtcNow.Date.AddDays(3),
        }).ToArray();
        StoreManyEventsForRules1And3(Array.Empty<EventEntity>(), upcoming);

        await CreateService(eventSourced: true).RunGenerationPassAsync(CancellationToken.None);

        _created.Count(t => ((string)t["sprk_name"]).StartsWith("Deadline:")).Should().Be(150);
        _eventWindows.Where(w => w.From is not null).Select(w => (w.Skip, w.Top)).Should().Equal((0, 100), (100, 100));
    }

    [Fact]
    public async Task Rule2_ReadsEveryPage_CarryingTheCookie_InAnOrderEndingOnTheId()
    {
        StoreRows("sprk_matter", Enumerable.Range(1, 150).Select(i => new Entity("sprk_matter", Guid.NewGuid())
        {
            ["sprk_name"] = $"Matter {i:000}", ["sprk_utilizationpercent"] = 120m,
        }).ToArray());

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        _created.Count(t => ((string)t["sprk_name"]).StartsWith("Budget Alert:")).Should().Be(150);
        Queries("sprk_matter").Select(q => q.PageInfo.PageNumber).Should().Equal(1, 2);
        Queries("sprk_matter").Should().OnlyContain(q => q.TopCount == null);
        Queries("sprk_matter")[0].Orders.Last().AttributeName.Should().Be("sprk_matterid");
        _cookieViolations.Should().BeEmpty();
    }

    [Fact]
    public async Task Rule4_ReadsEveryPage_CarryingTheCookie_InAnOrderEndingOnTheId()
    {
        StoreRows("sprk_invoice", Enumerable.Range(1, 150).Select(i => new Entity("sprk_invoice", Guid.NewGuid())
        {
            ["sprk_name"] = $"Invoice {i:000}",
        }).ToArray());

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        _created.Count(t => ((string)t["sprk_name"]).StartsWith("Invoice Pending:")).Should().Be(150);
        Queries("sprk_invoice").Select(q => q.PageInfo.PageNumber).Should().Equal(1, 2);
        Queries("sprk_invoice").Should().OnlyContain(q => q.TopCount == null);
        Queries("sprk_invoice")[0].Orders.Last().AttributeName.Should().Be("sprk_invoiceid");
        _cookieViolations.Should().BeEmpty();
    }

    [Fact]
    public async Task APagerThatAlwaysHasMore_StopsAtTheCap_AndWarnsOnce()
    {
        // A source that never runs out: one row per page, MoreRecords always true. Without the cap the pass would never
        // end (the fake throws past cap + 5 so a missing cap fails here instead of hanging).
        var pages = 0;
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_invoice"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                pages++;
                if (q.PageInfo.PageNumber > TodoGenerationService.MaxScanPages + 5)
                    throw new InvalidOperationException("the pager did not stop at its cap");
                return new EntityCollection(new List<Entity> { new("sprk_invoice", Guid.NewGuid()) { ["sprk_name"] = $"Inv {pages}" } })
                {
                    MoreRecords = true,
                    PagingCookie = $"cookie-{pages}",
                };
            });

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        pages.Should().Be(TodoGenerationService.MaxScanPages);
        _created.Count(t => ((string)t["sprk_name"]).StartsWith("Invoice Pending:")).Should().Be(TodoGenerationService.MaxScanPages);
        _logger.Entries.Where(e => e.Level == LogLevel.Warning && e.Message.Contains("stopped at the cap"))
            .Should().ContainSingle().Which.Message.Should().Contain("Rule 4").And.Contain("NOT processed this run");
    }

    [Fact]
    public async Task CancellingTheRun_InsideThePerEventLoop_Propagates_AndLogsNoPerEventErrors()
    {
        StoreEvents(
            Event(Guid.NewGuid(), "A", EventStatusCode.Open, AssigneeContact),
            Event(Guid.NewGuid(), "B", EventStatusCode.Open, AssigneeContact),
            Event(Guid.NewGuid(), "C", EventStatusCode.Open, AssigneeContact));
        using var cts = new CancellationTokenSource();
        var creates = 0;
        _dataverse.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                creates++;
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var act = () => CreateService().RunGenerationPassAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        creates.Should().Be(1, "the first cancelled create ends the run; the remaining events are not attempted");
        _logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ATimeoutThatIsNotTheRunsCancellation_IsStillAFailedRow_AndThePassContinues()
    {
        StoreEvents(
            Event(Guid.NewGuid(), "A", EventStatusCode.Open, AssigneeContact),
            Event(Guid.NewGuid(), "B", EventStatusCode.Open, AssigneeContact));
        _dataverse.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("HttpClient timeout"));

        var (_, _, failed) = await CreateService().RunGenerationPassAsync(CancellationToken.None);

        failed.Should().Be(2);
    }

    // ── Rules 1 and 3: overdue and upcoming events ───────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryLiveStatus))]
    public async Task Rule1_AnOverdueEvent_GetsAnOverdueTodo_ExactlyWhenItIsOpenWork(int status, bool expected)
    {
        StoreEventsForRules1And3(overdue: new EventEntity
        {
            Id = EventId, Name = "Filing", StatusCode = status, DueDate = DateTime.UtcNow.Date.AddDays(-3),
        }, upcoming: null);

        await CreateService(eventSourced: true).RunGenerationPassAsync(CancellationToken.None);

        _created.Any(t => ((string)t["sprk_name"]).StartsWith("Overdue:")).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(EveryLiveStatus))]
    public async Task Rule3_AnUpcomingEvent_GetsADeadlineTodo_ExactlyWhenItIsOpenWork(int status, bool expected)
    {
        StoreEventsForRules1And3(overdue: null, upcoming: new EventEntity
        {
            Id = EventId, Name = "Hearing", StatusCode = status, DueDate = DateTime.UtcNow.Date.AddDays(4),
        });

        await CreateService(eventSourced: true).RunGenerationPassAsync(CancellationToken.None);

        _created.Any(t => ((string)t["sprk_name"]).StartsWith("Deadline:")).Should().Be(expected);
    }

    // ── Fakes that evaluate the query, as Dataverse would ────────────────────────────────────────────────────

    private readonly List<QueryExpression> _assignedQueries = new();
    private readonly CapturingLogger<TodoGenerationService> _logger = new();

    private static Entity Event(Guid id, string name, int status, Guid? assignee)
    {
        var row = new Entity("sprk_event", id)
        {
            ["sprk_eventname"] = name,
            ["statuscode"] = new OptionSetValue(status),
            ["statecode"] = new OptionSetValue(EventStatusCode.GetStateCode(status)),
        };
        if (assignee is { } a)
            row["sprk_assignedto"] = new EntityReference("contact", a);
        return row;
    }

    /// <summary>
    /// sprk_event rows; the QueryExpression Rule 5 sends is evaluated on them and PAGED by its PageInfo, the way Dataverse
    /// answers (MoreRecords while rows remain).
    /// </summary>
    private void StoreEvents(params Entity[] rows)
    {
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_event"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                _assignedQueries.Add(q);
                var matching = rows.Where(r => Matches(q.Criteria, r)).ToList();
                var size = q.PageInfo?.Count > 0 ? q.PageInfo.Count : (q.TopCount ?? matching.Count);
                var number = q.PageInfo?.PageNumber > 0 ? q.PageInfo.PageNumber : 1;
                var pageRows = matching.Skip((number - 1) * size).Take(size).ToList();
                return new EntityCollection(pageRows)
                {
                    MoreRecords = q.PageInfo is not null && matching.Count > number * size,
                    PagingCookie = NextCookie("sprk_event", q),
                };
            });
    }

    /// <summary>
    /// Rows of a non-event entity (Rules 2 / 4), paged by PageInfo like Dataverse. Criteria are not evaluated here: these
    /// tests pin PAGING (every page read, the cookie carried), not the rules' filters.
    /// </summary>
    private void StoreRows(string entityName, params Entity[] rows)
    {
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == entityName), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                _queries.Add(q);
                var size = q.PageInfo?.Count > 0 ? q.PageInfo.Count : (q.TopCount ?? rows.Length);
                var number = q.PageInfo?.PageNumber > 0 ? q.PageInfo.PageNumber : 1;
                return new EntityCollection(rows.Skip((number - 1) * size).Take(size).ToList())
                {
                    MoreRecords = q.PageInfo is not null && rows.Length > number * size,
                    PagingCookie = NextCookie(entityName, q),
                };
            });
    }

    private readonly List<QueryExpression> _queries = new();
    private List<QueryExpression> Queries(string entityName) => _queries.Where(q => q.EntityName == entityName).ToList();

    // The cookie each source last RETURNED; page N+1 must send exactly it (a fake that ignored the cookie let a deleted
    // "carry the cookie forward" line pass every test — round 10 check).
    private readonly Dictionary<string, string> _lastCookie = new();
    private readonly List<string> _cookieViolations = new();

    private string NextCookie(string source, QueryExpression q)
    {
        var page = q.PageInfo?.PageNumber ?? 1;
        if (page > 1)
        {
            var expected = _lastCookie.GetValueOrDefault(source);
            if (q.PageInfo!.PagingCookie != expected)
                _cookieViolations.Add($"{source} page {page}: sent '{q.PageInfo.PagingCookie}', page {page - 1} returned '{expected}'");
        }

        var cookie = $"<cookie page=\"{page}\" source=\"{source}\" nonce=\"{Guid.NewGuid():N}\" />";
        _lastCookie[source] = cookie;
        return cookie;
    }

    private readonly List<(DateTime? From, int Skip, int Top)> _eventWindows = new();

    /// <summary>
    /// Rules 1 / 3 with many rows: the Web API query's ordered window — the first <c>skip + top</c> rows, the first
    /// <c>skip</c> dropped — and its <c>$count</c>, which Dataverse caps at 5000.
    /// </summary>
    private void StoreManyEventsForRules1And3(EventEntity[] overdue, EventEntity[] upcoming)
    {
        _events.Setup(e => e.QueryEventsAsync(
                It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int? _, Guid? _, Guid? _, int? _, int? _, DateTime? from, DateTime? _, int skip, int top, Guid? _,
                IReadOnlyCollection<int>? exclude, CancellationToken _) =>
            {
                _eventWindows.Add((from, skip, top));
                var rows = (from is null ? overdue : upcoming)
                    .Where(r => !(exclude ?? Array.Empty<int>()).Contains(r.StatusCode)).ToArray();
                return (rows.Skip(skip).Take(top).ToArray(), Math.Min(rows.Length, 5000));
            });
    }

    /// <summary>Rules 1 / 3: the positional app-only query, applying its <c>excludeStatusCodes</c> to the stored row.</summary>
    private void StoreEventsForRules1And3(EventEntity? overdue, EventEntity? upcoming)
    {
        _events.Setup(e => e.QueryEventsAsync(
                It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int? _, Guid? _, Guid? _, int? statusCode, int? _, DateTime? from, DateTime? _, int _, int _, Guid? _,
                IReadOnlyCollection<int>? exclude, CancellationToken _) =>
            {
                var candidate = from is null ? overdue : upcoming;
                var visible = candidate is not null
                    && (statusCode is null || candidate.StatusCode == statusCode)
                    && !(exclude ?? Array.Empty<int>()).Contains(candidate.StatusCode);
                return visible ? (new[] { candidate! }, 1) : (Array.Empty<EventEntity>(), 0);
            });
    }

    /// <summary>Equal / NotEqual on option-set columns and Null / NotNull on any column, AND-ed (the shapes the rules send).</summary>
    private static bool Matches(FilterExpression filter, Entity row) =>
        filter.Conditions.All(c =>
        {
            if (c.Operator is ConditionOperator.NotNull or ConditionOperator.Null)
            {
                var present = row.Contains(c.AttributeName) && row[c.AttributeName] is not null;
                return c.Operator == ConditionOperator.NotNull ? present : !present;
            }

            var actual = row.GetAttributeValue<OptionSetValue>(c.AttributeName)?.Value;
            var value = c.Values.Count == 1 ? Convert.ToInt32(c.Values[0]) : throw new NotSupportedException();
            return c.Operator switch
            {
                ConditionOperator.Equal => actual == value,
                ConditionOperator.NotEqual => actual != value,
                _ => throw new NotSupportedException($"{c.Operator} is not evaluated by this fake"),
            };
        })
        && filter.Filters.All(f => Matches(f, row));

    private TodoGenerationService CreateService(bool eventSourced = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_dataverse.Object);
        services.AddSingleton(_events.Object);
        services.AddSingleton(_comm.Object);
        services.AddSingleton<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>(_ownership);
        var svc = new TodoGenerationService(
            services.BuildServiceProvider(),
            _logger,
            Options.Create(new TodoGenerationOptions { EnableEventSourcedGeneration = eventSourced }));

        svc.SetDataverseForTest(_dataverse.Object, _events.Object);

        svc.SetRegardingBuilderForTest(new TodoRegardingBuilder(
            _comm.Object,
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            Mock.Of<ILogger<TodoRegardingBuilder>>()));
        svc.SetOwnershipResolverForTest(_ownership);
        return svc;
    }
}
