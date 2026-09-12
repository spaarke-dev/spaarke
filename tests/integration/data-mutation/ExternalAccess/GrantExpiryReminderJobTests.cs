using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Expiry reminders — spec FR-33 (d), task 100: 30/14/7/3/1 days before an external grant lapses, the internal
/// user who can renew it gets an in-app notification; the external grantee never does.
///
/// <para><b>In scope</b> (the task's closed acceptance set plus the owner's 2026-09-11 recipient decision):
/// which grants are reminded (exactly the five days; active only), who is reminded (granter → record owner →
/// record creator, each only if an enabled non-application user; otherwise counted unroutable; never the
/// grantee), at most once per day across runs, one query per run, the reminder names who loses what and when,
/// and a heartbeat on every run that separates "nothing due" from "the run failed".</para>
///
/// <para><b>Why the fake is strict.</b> <see cref="FakeDataverse"/> evaluates the job's FetchXML the way
/// Dataverse would — its filter conditions, its joins (an inner join drops rows, an outer one does not), and
/// the columns it asks for — against a model of the live schema (verified 2026-09-12). It THROWS on a condition,
/// join or column it does not model. So a query that lost <c>statecode eq 0</c> hands back revoked grants, one
/// that turned a join inner loses the grants the fallback chain exists for, and one that names a column that is
/// not there fails instead of passing against a fake that ignored it.</para>
///
/// <para>Real collaborators: <see cref="NotificationService"/> and <see cref="IdempotencyService"/> (over an
/// in-memory distributed cache). The one seam is <see cref="IGenericEntityService"/>, mocked STRICT — any call
/// other than the FetchXML query and the notification create throws.</para>
/// </summary>
public class GrantExpiryReminderJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 12);

    private static readonly Guid Granter = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid RecordOwner = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly Guid RecordCreator = Guid.Parse("a0000000-0000-0000-0000-000000000003");
    private static readonly Guid DisabledUser = Guid.Parse("a0000000-0000-0000-0000-000000000004");
    private static readonly Guid ApplicationUser = Guid.Parse("a0000000-0000-0000-0000-000000000005");
    private static readonly Guid ContactId = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid OrganizationId = Guid.Parse("d0000000-0000-0000-0000-000000000001");

    private readonly FakeDataverse _dataverse = new();
    private readonly Mock<IGenericEntityService> _entityService = new(MockBehavior.Strict);
    private readonly List<Entity> _notifications = new();
    private readonly CapturingLogger _log = new();
    private readonly GrantExpiryReminderJob _job;
    private int _queries;

    public GrantExpiryReminderJobTests()
    {
        _dataverse.Users[Granter] = new UserRow(IsDisabled: false, IsApplication: false);
        _dataverse.Users[RecordOwner] = new UserRow(IsDisabled: false, IsApplication: false);
        _dataverse.Users[RecordCreator] = new UserRow(IsDisabled: false, IsApplication: false);
        _dataverse.Users[DisabledUser] = new UserRow(IsDisabled: true, IsApplication: false);
        _dataverse.Users[ApplicationUser] = new UserRow(IsDisabled: false, IsApplication: true);
        _dataverse.Contacts[ContactId] = "Jane Doe";
        _dataverse.Organizations[OrganizationId] = "Morrison Foerster LLP";

        _entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FetchExpression fetch, CancellationToken _) =>
            {
                _queries++;
                return _dataverse.Execute(fetch.Query);
            });
        _entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity entity, CancellationToken _) =>
            {
                _notifications.Add(entity);
                return Guid.NewGuid();
            });

        var services = new ServiceCollection();
        services.AddSingleton(_entityService.Object);
        services.AddSingleton(new NotificationService(_entityService.Object, NullLogger<NotificationService>.Instance));
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        services.AddScoped<IIdempotencyService>(sp =>
            new IdempotencyService(sp.GetRequiredService<IDistributedCache>(), NullLogger<IdempotencyService>.Instance));

        _job = new GrantExpiryReminderJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(Now),
            _log);
    }

    // ── Which grants ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(30)]
    [InlineData(14)]
    [InlineData(7)]
    [InlineData(3)]
    [InlineData(1)]
    public async Task ExecuteAsync_GrantExpiringOnAReminderDay_SendsOneReminderToTheGranter(int daysLeft)
    {
        Seed(daysLeft, grantedBy: Granter, owner: RecordOwner, creator: RecordCreator);

        await RunAsync();

        _notifications.Should().ContainSingle();
        _notifications[0].LogicalName.Should().Be("appnotification");
        RecipientOf(_notifications[0]).Should().Be(Granter);
        _notifications[0].GetAttributeValue<string>("title").Should().Be(
            daysLeft == 1 ? "External access expires tomorrow" : $"External access expires in {daysLeft} days");
    }

    [Theory]
    [InlineData(20)]
    [InlineData(31)]
    [InlineData(29)]
    [InlineData(15)]
    [InlineData(2)]
    [InlineData(0)]
    public async Task ExecuteAsync_GrantNotOnAReminderDay_SendsNothing(int daysLeft)
    {
        Seed(daysLeft, grantedBy: Granter);

        await RunAsync();

        _notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_InactiveGrantOnAReminderDay_SendsNothing()
    {
        Seed(7, grantedBy: Granter, stateCode: 1);

        await RunAsync();

        _notifications.Should().BeEmpty();
    }

    // ── Who is reminded ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ContactAndOrganizationGrants_NeverAddressTheGrantee()
    {
        Seed(7, grantedBy: null, creator: RecordCreator);
        Seed(7, grantedBy: null, creator: RecordCreator, organizationGrant: true);

        await RunAsync();

        _notifications.Should().HaveCount(2);
        _notifications.Select(n => n.GetAttributeValue<EntityReference>("ownerid"))
            .Should().OnlyContain(owner => owner.LogicalName == "systemuser" && owner.Id == RecordCreator);
        _notifications.Select(RecipientOf).Should().NotContain(new[] { ContactId, OrganizationId });
    }

    [Fact]
    public async Task ExecuteAsync_NoGranter_RemindsTheRecordOwner()
    {
        Seed(7, grantedBy: null, owner: RecordOwner, creator: RecordCreator);

        await RunAsync();

        _notifications.Select(RecipientOf).Should().Equal(RecordOwner);
    }

    [Fact]
    public async Task ExecuteAsync_NoGranterAndTeamOwnedRecord_RemindsTheRecordCreator()
    {
        // A team-owned record has no owninguser — the shape of 10 of 11 live root records (task 100 notes).
        Seed(7, grantedBy: null, owner: null, creator: RecordCreator);

        await RunAsync();

        _notifications.Select(RecipientOf).Should().Equal(RecordCreator);
    }

    [Theory]
    [InlineData("application user")]
    [InlineData("disabled user")]
    public async Task ExecuteAsync_GranterIsNotAnEnabledPerson_FallsThroughToTheRecordOwner(string granterKind)
    {
        Seed(7, grantedBy: granterKind == "application user" ? ApplicationUser : DisabledUser, owner: RecordOwner);

        await RunAsync();

        _notifications.Select(RecipientOf).Should().Equal(RecordOwner);
    }

    [Fact]
    public async Task ExecuteAsync_NoPersonAnywhereInTheChain_CountsUnroutableAndLogsAnError()
    {
        Seed(7, grantedBy: ApplicationUser, owner: DisabledUser, creator: ApplicationUser);

        var result = await RunAsync();

        _notifications.Should().BeEmpty("an unroutable grant is never redirected — least of all to the grantee");
        Heartbeat()["Unroutable"].Should().Be(1);
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("UNROUTABLE"));
        result.Success.Should().BeTrue("the run itself worked; the unroutable grant is reported, not a run failure");
    }

    // ── What it says ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Reminder_NamesWhoLosesAccessToWhichRecordAndWhen()
    {
        var matterId = Seed(14, grantedBy: Granter, rootName: "Smith v. Smith");
        Seed(14, grantedBy: Granter, rootName: "Smith v. Smith", organizationGrant: true);

        await RunAsync();

        var expires = Today.AddDays(14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _notifications.Select(n => n.GetAttributeValue<string>("body")).Should().BeEquivalentTo(
            $"Jane Doe will lose access to the matter \"Smith v. Smith\" on {expires}. To keep it, set a new expiration date in Manage Access on the matter.",
            $"Members of Morrison Foerster LLP will lose access to the matter \"Smith v. Smith\" on {expires}. To keep it, set a new expiration date in Manage Access on the matter.");
        _notifications[0].GetAttributeValue<string>("data").Should()
            .Contain($"/main.aspx?etn=sprk_matter\\u0026id={matterId}\\u0026pagetype=entityrecord");
    }

    // ── Once, and cheaply ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RunTwiceOnTheSameDay_SendsNoDuplicates()
    {
        Seed(7, grantedBy: Granter);

        await RunAsync();
        await RunAsync();

        _notifications.Should().ContainSingle();
        var second = Heartbeat(last: true);
        second["Sent"].Should().Be(0);
        second["AlreadySent"].Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ManyDueGrants_IssuesExactlyOneQuery()
    {
        foreach (var days in new[] { 30, 14, 7, 3, 1 })
        {
            Seed(days, grantedBy: Granter);
            Seed(days, grantedBy: null, owner: RecordOwner);
            Seed(days, grantedBy: null, creator: RecordCreator);
        }
        Seed(20, grantedBy: Granter);
        Seed(45, grantedBy: Granter);

        await RunAsync();

        _queries.Should().Be(1);
        _notifications.Should().HaveCount(15);
    }

    // ── Heartbeat ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NothingDue_StillEmitsAHeartbeatThatSaysOk()
    {
        Seed(20, grantedBy: Granter);

        var result = await RunAsync();

        var heartbeat = Heartbeat();
        heartbeat["Status"].Should().Be("ok");
        heartbeat["Due"].Should().Be(0);
        heartbeat["Sent"].Should().Be(0);
        heartbeat["Today"].Should().Be("2026-09-12");
        result.Success.Should().BeTrue();
        result.ProcessedItems.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_QueryFails_HeartbeatSaysErrorNotOk()
    {
        Seed(7, grantedBy: Granter);
        _dataverse.QueryFailure = new InvalidOperationException("Dataverse is unavailable.");

        var result = await RunAsync();

        var entry = _log.Entries.Single(e => e.Message.Contains("heartbeat"));
        entry.State["Status"].Should().Be("error");
        entry.Level.Should().Be(LogLevel.Warning);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Dataverse is unavailable.");
        _notifications.Should().BeEmpty();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<JobRunResult> RunAsync()
        => _job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "test-correlation", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
            CancellationToken.None);

    /// <summary>Adds one grant on its own matter. Returns the matter id.</summary>
    private Guid Seed(
        int daysLeft,
        Guid? grantedBy,
        Guid? owner = null,
        Guid? creator = null,
        int stateCode = 0,
        bool organizationGrant = false,
        string rootName = "Real Estate Transaction Matter")
    {
        var matterId = Guid.NewGuid();
        _dataverse.Roots[("sprk_matter", matterId)] = new RootRow(rootName, owner, creator);
        _dataverse.Grants.Add(new GrantRow(
            Id: Guid.NewGuid(),
            StateCode: stateCode,
            Expires: Today.AddDays(daysLeft),
            RootEntity: "sprk_matter",
            RootId: matterId,
            ContactId: organizationGrant ? null : ContactId,
            OrganizationId: organizationGrant ? OrganizationId : null,
            GrantedBy: grantedBy));
        return matterId;
    }

    private static Guid RecipientOf(Entity notification) => notification.GetAttributeValue<EntityReference>("ownerid").Id;

    private IReadOnlyDictionary<string, object?> Heartbeat(bool last = false)
    {
        var heartbeats = _log.Entries.Where(e => e.Message.Contains("heartbeat")).ToList();
        heartbeats.Should().NotBeEmpty("every run must emit a heartbeat");
        return last ? heartbeats[^1].State : heartbeats.Single().State;
    }

    private sealed class CapturingLogger : ILogger<GrantExpiryReminderJob>
    {
        public List<(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> State)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>> ?? Array.Empty<KeyValuePair<string, object?>>();
            Entries.Add((logLevel, formatter(state, exception), values.ToDictionary(kv => kv.Key, kv => kv.Value)));
        }
    }

    private sealed record GrantRow(
        Guid Id, int StateCode, DateOnly? Expires, string RootEntity, Guid RootId, Guid? ContactId, Guid? OrganizationId, Guid? GrantedBy);

    private sealed record RootRow(string Name, Guid? OwningUser, Guid? CreatedBy);

    private sealed record UserRow(bool IsDisabled, bool IsApplication);

    /// <summary>
    /// Evaluates the job's FetchXML against in-memory tables shaped like the live schema (task 100 notes).
    /// Throws on anything it does not model.
    /// </summary>
    private sealed class FakeDataverse
    {
        private static readonly Dictionary<string, string> PrimaryIds = new(StringComparer.Ordinal)
        {
            ["systemuser"] = "systemuserid",
            ["contact"] = "contactid",
            ["sprk_organization"] = "sprk_organizationid",
            ["sprk_project"] = "sprk_projectid",
            ["sprk_matter"] = "sprk_matterid",
            ["sprk_workassignment"] = "sprk_workassignmentid",
        };

        private static readonly Dictionary<string, string> RootNameColumns = new(StringComparer.Ordinal)
        {
            ["sprk_project"] = "sprk_projectname",
            ["sprk_matter"] = "sprk_mattername",
            ["sprk_workassignment"] = "sprk_name",
        };

        public List<GrantRow> Grants { get; } = new();
        public Dictionary<(string Entity, Guid Id), RootRow> Roots { get; } = new();
        public Dictionary<Guid, UserRow> Users { get; } = new();
        public Dictionary<Guid, string> Contacts { get; } = new();
        public Dictionary<Guid, string> Organizations { get; } = new();
        public Exception? QueryFailure { get; set; }

        public EntityCollection Execute(string fetchXml)
        {
            if (QueryFailure is not null)
            {
                throw QueryFailure;
            }

            var entity = XElement.Parse(fetchXml).Element("entity")
                ?? throw new InvalidOperationException("FetchXML has no <entity>.");
            if (entity.Attribute("name")?.Value != "sprk_externalrecordaccess")
            {
                throw new InvalidOperationException($"Unexpected entity '{entity.Attribute("name")?.Value}'.");
            }

            IEnumerable<GrantRow> rows = Grants;
            foreach (var filter in entity.Elements("filter"))
            {
                if (filter.Attribute("type")?.Value != "and")
                {
                    throw new InvalidOperationException("Only an 'and' filter is modelled.");
                }

                foreach (var condition in filter.Elements())
                {
                    rows = ApplyCondition(rows, condition).ToList();
                }
            }

            var selected = entity.Elements("attribute").Select(a => a.Attribute("name")!.Value).ToList();
            var result = new EntityCollection { EntityName = "sprk_externalrecordaccess", MoreRecords = false };

            foreach (var row in rows)
            {
                var output = new Entity("sprk_externalrecordaccess", row.Id);
                foreach (var column in selected)
                {
                    if (GrantValue(row, column) is { } value)
                    {
                        output[column] = value;
                    }
                }

                var keep = entity.Elements("link-entity")
                    .All(link => ApplyLink(output, link, GrantJoinValue(row, link.Attribute("to")!.Value)));
                if (keep)
                {
                    result.Entities.Add(output);
                }
            }

            return result;
        }

        private static IEnumerable<GrantRow> ApplyCondition(IEnumerable<GrantRow> rows, XElement condition)
        {
            if (condition.Name != "condition")
            {
                throw new InvalidOperationException($"Nested <{condition.Name}> is not modelled.");
            }

            var attribute = condition.Attribute("attribute")!.Value;
            var op = condition.Attribute("operator")!.Value;

            return (attribute, op) switch
            {
                ("statecode", "eq") => rows.Where(r => r.StateCode == int.Parse(condition.Attribute("value")!.Value, CultureInfo.InvariantCulture)),
                ("sprk_expiresdate", "in") => rows.Where(r => r.Expires is { } expires && condition.Elements("value")
                    .Select(v => DateOnly.ParseExact(v.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .Contains(expires)),
                _ => throw new InvalidOperationException(
                    $"This fake does not model the condition '{attribute} {op}' — extend it deliberately rather than let it pass unevaluated."),
            };
        }

        private static object? GrantValue(GrantRow row, string column) => column switch
        {
            "sprk_externalrecordaccessid" => row.Id,
            "sprk_expiresdate" => row.Expires?.ToDateTime(TimeOnly.MinValue),
            "sprk_contact" => row.ContactId is { } c ? new EntityReference("contact", c) : null,
            "sprk_organization" => row.OrganizationId is { } o ? new EntityReference("sprk_organization", o) : null,
            "sprk_grantedby" => row.GrantedBy is { } g ? new EntityReference("systemuser", g) : null,
            "sprk_project" or "sprk_matter" or "sprk_workassignment" =>
                row.RootEntity == column ? new EntityReference(column, row.RootId) : null,
            _ => throw new InvalidOperationException($"sprk_externalrecordaccess has no column '{column}' in this fake's model."),
        };

        private static Guid? GrantJoinValue(GrantRow row, string to)
            => (GrantValue(row, to) as EntityReference)?.Id;

        /// <returns><c>false</c> when an INNER join finds no match — Dataverse drops the row.</returns>
        private bool ApplyLink(Entity output, XElement link, Guid? parentValue)
        {
            var name = link.Attribute("name")!.Value;
            var alias = link.Attribute("alias")?.Value
                ?? throw new InvalidOperationException($"link-entity '{name}' has no alias; the job reads aliased columns.");
            var outer = link.Attribute("link-type")?.Value == "outer";

            if (!PrimaryIds.TryGetValue(name, out var primaryId) || link.Attribute("from")?.Value != primaryId)
            {
                throw new InvalidOperationException($"link-entity '{name}' from '{link.Attribute("from")?.Value}' is not modelled.");
            }

            if (link.Elements("filter").Any())
            {
                throw new InvalidOperationException("A filter inside a link-entity is not modelled.");
            }

            if (parentValue is not { } id || !Exists(name, id))
            {
                return outer;
            }

            foreach (var column in link.Elements("attribute").Select(a => a.Attribute("name")!.Value))
            {
                if (TargetValue(name, id, column) is { } value)
                {
                    output[$"{alias}.{column}"] = new AliasedValue(name, column, value);
                }
            }

            foreach (var nested in link.Elements("link-entity"))
            {
                var nestedParent = (TargetValue(name, id, nested.Attribute("to")!.Value) as EntityReference)?.Id;
                if (!ApplyLink(output, nested, nestedParent))
                {
                    return false;
                }
            }

            return true;
        }

        private bool Exists(string entityName, Guid id) => entityName switch
        {
            "systemuser" => Users.ContainsKey(id),
            "contact" => Contacts.ContainsKey(id),
            "sprk_organization" => Organizations.ContainsKey(id),
            _ => Roots.ContainsKey((entityName, id)),
        };

        private object? TargetValue(string entityName, Guid id, string column)
        {
            switch (entityName)
            {
                case "systemuser":
                    var user = Users[id];
                    return column switch
                    {
                        "isdisabled" => user.IsDisabled,
                        "applicationid" => user.IsApplication ? Guid.Parse("b0000000-0000-0000-0000-00000000a991") : null,
                        _ => throw new InvalidOperationException($"systemuser column '{column}' is not modelled."),
                    };
                case "contact":
                    return column == "fullname" ? Contacts[id] : throw new InvalidOperationException($"contact column '{column}' is not modelled.");
                case "sprk_organization":
                    return column == "sprk_organizationname" ? Organizations[id] : throw new InvalidOperationException($"sprk_organization column '{column}' is not modelled.");
                default:
                    var root = Roots[(entityName, id)];
                    if (column == RootNameColumns[entityName])
                    {
                        return root.Name;
                    }

                    return column switch
                    {
                        "owninguser" => root.OwningUser is { } owner ? new EntityReference("systemuser", owner) : null,
                        "createdby" => root.CreatedBy is { } creator ? new EntityReference("systemuser", creator) : null,
                        _ => throw new InvalidOperationException($"{entityName} column '{column}' is not modelled."),
                    };
            }
        }
    }
}
