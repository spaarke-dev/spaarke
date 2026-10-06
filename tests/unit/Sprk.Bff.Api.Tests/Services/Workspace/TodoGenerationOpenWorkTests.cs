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
                    PagingCookie = $"page{number}",
                };
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
            new CapturingLogger<TodoGenerationService>(),
            Options.Create(new TodoGenerationOptions { EnableEventSourcedGeneration = eventSourced }));

        typeof(TodoGenerationService).GetField("_dataverse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, _dataverse.Object);
        typeof(TodoGenerationService).GetField("_events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, _events.Object);
        svc.SetRegardingBuilderForTest(new TodoRegardingBuilder(
            _comm.Object,
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            Mock.Of<ILogger<TodoRegardingBuilder>>()));
        svc.SetOwnershipResolverForTest(_ownership);
        return svc;
    }
}
