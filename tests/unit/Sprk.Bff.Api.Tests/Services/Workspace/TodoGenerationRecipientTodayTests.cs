// spaarke-ontology-platform-r1 task 098 — owner decision D-25 (2026-10-06): To Do generation judges "overdue" (Rule 1)
// and "due within N days" (Rule 3) on the "today" of the person the To Do is FOR — the assignee's time zone, else the
// owner's, else UTC with a warning — and reads each distinct user's zone once per run.
//
// The clock is pinned to 2026-10-07 01:00 UTC = 21:00 on 10-06 in New York (EDT), 18:00 on 10-06 in Los Angeles and
// 10:00 on 10-07 in Tokyo. The UTC date is 10-07; a New York recipient's today is 10-06. The event source EVALUATES the
// due-date bounds the service sends, the way Dataverse would, so a rule whose query window is the UTC day visibly
// misses (or wrongly includes) an event.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

[Trait("status", "task-098-ontology-r1")]
public class TodoGenerationRecipientTodayTests
{
    private static readonly DateTimeOffset NinePmEastern = new(2026, 10, 7, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly NewYorkToday = new(2026, 10, 6);

    // Time-zone codes no other test uses: DataverseUserTimeZone caches code → zone for the process.
    private const int EasternCode = 9801;
    private const int PacificCode = 9802;
    private const int TokyoCode = 9803;

    private static readonly Dictionary<int, string> ZoneNames = new()
    {
        [EasternCode] = "Eastern Standard Time",
        [PacificCode] = "Pacific Standard Time",
        [TokyoCode] = "Tokyo Standard Time",
    };

    private readonly Mock<IDataverseService> _dataverse = new(MockBehavior.Loose);
    private readonly Mock<IEventDataverseService> _events = new(MockBehavior.Loose);
    private readonly Mock<ICommunicationDataverseService> _comm = new(MockBehavior.Loose);
    private readonly CapturingLogger<TodoGenerationService> _logger = new();
    private readonly Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble _ownership = new();
    private readonly List<Entity> _created = new();

    // The people: contact → systemuser (task 141 link, or oid binding), systemuser → timezonecode.
    private readonly Dictionary<Guid, Guid> _userByPrimaryContact = new();
    private readonly Dictionary<Guid, Guid> _oidOfContact = new();
    private readonly Dictionary<Guid, Guid> _userByOid = new();
    private readonly Dictionary<Guid, int?> _zoneOfUser = new();
    private readonly Dictionary<Guid, (Guid? Assignee, EntityReference? Owner)> _eventPeople = new();
    private readonly List<EventEntity> _eventRows = new();

    private readonly List<Guid> _zoneReads = new();
    private readonly List<Guid> _contactToUserQueries = new();

    public TodoGenerationRecipientTodayTests()
    {
        _comm.Setup(c => c.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);
        _dataverse.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => _created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => Query(q));
        _dataverse.Setup(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) => Retrieve(entity, id));
        _events.Setup(e => e.QueryEventsAsync(
                It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int? _, Guid? _, Guid? _, int? _, int? _, DateTime? from, DateTime? to, int skip, int top, Guid? _,
                IReadOnlyCollection<int>? _, CancellationToken _) =>
            {
                // sprk_duedate ge {from:yyyy-MM-dd} and le {to:yyyy-MM-dd} — the bounds DataverseWebApiService sends.
                var matching = _eventRows
                    .Where(e => e.DueDate is { } due
                        && (from is null || due >= DateOnly.FromDateTime(from.Value))
                        && (to is null || due <= DateOnly.FromDateTime(to.Value)))
                    .ToArray();
                return (matching.Skip(skip).Take(top).ToArray(), matching.Length);
            });
    }

    // ── Rule 1: overdue ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rule1_AtNinePmInNewYork_AnEventDueTodayThere_IsNotOverdue_AndOneDueTheDayBeforeIs()
    {
        var newYorker = Person(EasternCode);
        var other = Person(PacificCode);
        AddEvent("Due today in New York", NewYorkToday, assignee: newYorker.Contact);
        AddEvent("Due yesterday in New York", NewYorkToday.AddDays(-1), assignee: other.Contact);

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        Names("Overdue:").Should().BeEquivalentTo(new[] { "Overdue: Due yesterday in New York" },
            "it is still 10-06 for the assignee, so an event due 10-06 is due today, not overdue — the UTC date (10-07) "
            + "would call it overdue a day early");
        _zoneReads.Should().NotContain(other.User,
            "an event due before every possible today is overdue for anyone, so its recipient's zone is never read");
    }

    // ── Rule 3: due within N days ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rule3_AtNinePmInNewYork_TheWindowIsNewYorksDays_NotTheUtcDays()
    {
        var newYorker = Person(EasternCode);
        AddEvent("Due today", NewYorkToday, assignee: newYorker.Contact);
        AddEvent("Last day of the window", NewYorkToday.AddDays(14), assignee: newYorker.Contact);
        AddEvent("Day after the window", NewYorkToday.AddDays(15), assignee: newYorker.Contact);

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        Names("Deadline:").Should().BeEquivalentTo(new[]
        {
            "Deadline: Due today (due 2026-10-06)",
            "Deadline: Last day of the window (due 2026-10-20)",
        }, "the 14-day window runs from the assignee's today (10-06) to 10-20; the UTC window (10-07 to 10-21) would "
           + "drop today's event and add one that is 15 days away");
    }

    // ── The fallback chain: assignee → owner → UTC with a warning ───────────────────────────────────────────────

    [Fact]
    public async Task TheRecipientsToday_IsTheAssignees_ElseTheOwners_ElseUtcWithAWarning()
    {
        var newYorker = Person(EasternCode);
        var tokyoOwner = Person(TokyoCode);
        var laOwner = Person(PacificCode);
        var noZone = Person(zoneCode: null);
        var external = Guid.NewGuid(); // a contact no user is linked to
        var oidBound = Person(EasternCode, linkByOid: true);
        var team = new EntityReference("team", Guid.NewGuid());

        // Every event is due 10-06: overdue on a Tokyo or UTC today (10-07), not on a New York or Los Angeles one (10-06).
        AddEvent("Assignee before owner", NewYorkToday, newYorker.Contact, User(tokyoOwner));
        AddEvent("Owner when the assignee is not a user", NewYorkToday, external, User(laOwner));
        AddEvent("Owner when the assignee has no zone", NewYorkToday, noZone.Contact, User(laOwner));
        AddEvent("Assignee by oid binding", NewYorkToday, oidBound.Contact, team);
        AddEvent("UTC when neither has a zone", NewYorkToday, assignee: null, owner: team);

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        Names("Overdue:").Should().BeEquivalentTo(new[] { "Overdue: UTC when neither has a zone" },
            "only the event whose assignee and owner both lack a zone is judged on the UTC date (10-07)");

        var fallbackWarnings = _logger.Entries
            .Where(e => e.Level == LogLevel.Warning && e.Message.Contains("todo_today_utc_fallback"))
            .ToList();
        fallbackWarnings.Should().NotBeEmpty();
        fallbackWarnings.Should().OnlyContain(e => e.Message.Contains(EventIdOf("UTC when neither has a zone").ToString()),
            "the warning names the event judged on UTC, and no other");
        fallbackWarnings.Should().OnlyContain(e => e.Message.Contains("no-assignee;owner-is-team"));
    }

    // ── One time-zone read per distinct user per run ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EachDistinctUsersZone_IsReadOncePerRun_AcrossEventsAndRules()
    {
        var first = Person(EasternCode);
        var second = Person(PacificCode);

        // Due 10-06 and 10-07: within a day of both rules' boundaries, so every one needs its recipient's today, in Rule 1
        // AND in Rule 3.
        AddEvent("A1", NewYorkToday, first.Contact);
        AddEvent("A2", NewYorkToday, first.Contact);
        AddEvent("A3", NewYorkToday.AddDays(1), first.Contact);
        AddEvent("B1", NewYorkToday, second.Contact);
        AddEvent("B2", NewYorkToday.AddDays(1), second.Contact);

        await CreateService().RunGenerationPassAsync(CancellationToken.None);

        _zoneReads.Should().BeEquivalentTo(new[] { first.User, second.User },
            "five events over two rules have two distinct recipients: one usersettings read each");
        _contactToUserQueries.Should().HaveCount(2, "each distinct assignee contact is mapped to its user once");
    }

    // ── Harness ─────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record PersonIds(Guid Contact, Guid User);

    private PersonIds Person(int? zoneCode, bool linkByOid = false)
    {
        var contact = Guid.NewGuid();
        var user = Guid.NewGuid();
        if (linkByOid)
        {
            var oid = Guid.NewGuid();
            _oidOfContact[contact] = oid;
            _userByOid[oid] = user;
        }
        else
        {
            _userByPrimaryContact[contact] = user;
        }

        _zoneOfUser[user] = zoneCode;
        return new PersonIds(contact, user);
    }

    private static EntityReference User(PersonIds person) => new("systemuser", person.User);

    private void AddEvent(string name, DateOnly due, Guid? assignee, EntityReference? owner = null)
    {
        var id = Guid.NewGuid();
        _eventRows.Add(new EventEntity { Id = id, Name = name, DueDate = due, StatusCode = EventStatusCode.Open });
        _eventPeople[id] = (assignee, owner);
    }

    private Guid EventIdOf(string name) => _eventRows.Single(e => e.Name == name).Id;

    private IEnumerable<string> Names(string prefix) =>
        _created.Select(t => (string)t["sprk_name"]).Where(n => n.StartsWith(prefix, StringComparison.Ordinal));

    private EntityCollection Query(QueryExpression q)
    {
        switch (q.EntityName)
        {
            case "systemuser":
            {
                var condition = q.Criteria.Conditions.Single(c => c.AttributeName != "isdisabled");
                var value = (Guid)condition.Values[0];
                if (condition.AttributeName == "sprk_primarycontact")
                    _contactToUserQueries.Add(value);
                var user = condition.AttributeName switch
                {
                    "sprk_primarycontact" => _userByPrimaryContact.TryGetValue(value, out var u) ? u : (Guid?)null,
                    "azureactivedirectoryobjectid" => _userByOid.TryGetValue(value, out var u) ? u : null,
                    _ => null,
                };
                return user is { } found
                    ? new EntityCollection(new List<Entity> { new("systemuser", found) })
                    : new EntityCollection();
            }

            case "timezonedefinition":
            {
                var code = Convert.ToInt32(q.Criteria.Conditions.Single().Values[0]);
                return ZoneNames.TryGetValue(code, out var name)
                    ? new EntityCollection(new List<Entity> { new("timezonedefinition") { ["standardname"] = name } })
                    : new EntityCollection();
            }

            default:
                return new EntityCollection(); // sprk_todo dedupe: nothing exists yet
        }
    }

    private Entity Retrieve(string entity, Guid id)
    {
        var row = new Entity(entity, id);
        switch (entity)
        {
            case "sprk_event" when _eventPeople.TryGetValue(id, out var people):
                if (people.Assignee is { } assignee)
                    row["sprk_assignedtointernal"] = new EntityReference("contact", assignee);
                if (people.Owner is { } owner)
                    row["ownerid"] = owner;
                break;
            case "usersettings":
                _zoneReads.Add(id);
                if (_zoneOfUser.TryGetValue(id, out var code) && code is { } c)
                    row["timezonecode"] = c;
                break;
            case "contact" when _oidOfContact.TryGetValue(id, out var oid):
                row["sprk_externalobjectid"] = oid.ToString();
                break;
        }

        return row;
    }

    private TodoGenerationService CreateService()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_dataverse.Object);
        services.AddSingleton(_events.Object);
        services.AddSingleton(_comm.Object);
        services.AddSingleton<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>(_ownership);
        var svc = new TodoGenerationService(
            services.BuildServiceProvider(),
            _logger,
            Options.Create(new TodoGenerationOptions { EnableEventSourcedGeneration = true, DeadlineWindowDays = 14 }),
            new FakeTimeProvider(NinePmEastern));

        svc.SetDataverseForTest(_dataverse.Object, _events.Object);
        svc.SetRegardingBuilderForTest(new TodoRegardingBuilder(
            _comm.Object,
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            Mock.Of<ILogger<TodoRegardingBuilder>>()));
        svc.SetOwnershipResolverForTest(_ownership);
        return svc;
    }
}
