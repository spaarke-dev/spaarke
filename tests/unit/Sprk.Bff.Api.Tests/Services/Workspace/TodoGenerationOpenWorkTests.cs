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

    [Theory]
    [MemberData(nameof(EveryLiveStatus))]
    public async Task Rule5_AnAssignedEvent_GetsAnAssignedTodo_ExactlyWhenItIsOpenWork(int status, bool expected)
    {
        StoreAssignedEvent(status);

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        _created.Any(t => ((string)t["sprk_name"]).StartsWith("Assigned:")).Should().Be(expected,
            $"{EventStatusCode.GetDisplayName(status)} {(expected ? "is" : "is not")} open work (owner decision A)");
        expected.Should().Be(EventStatusCode.IsOpenWork(status), "the expectation table IS the predicate");
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

    /// <summary>One sprk_event row with <paramref name="status"/>; the QueryExpression Rule 5 sends is evaluated on it.</summary>
    private void StoreAssignedEvent(int status)
    {
        var row = new Entity("sprk_event", EventId)
        {
            ["sprk_eventname"] = "Review NDA",
            ["statuscode"] = new OptionSetValue(status),
            ["statecode"] = new OptionSetValue(EventStatusCode.GetStateCode(status)),
        };
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_event"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
                new EntityCollection(Matches(q.Criteria, row) ? new List<Entity> { row } : new List<Entity>()));
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

    /// <summary>Equal / NotEqual conditions on option-set columns, AND-ed (the only shapes the rules send).</summary>
    private static bool Matches(FilterExpression filter, Entity row) =>
        filter.Conditions.All(c =>
        {
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
