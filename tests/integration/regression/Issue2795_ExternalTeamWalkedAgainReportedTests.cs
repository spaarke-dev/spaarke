using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using Job = Sprk.Bff.Api.Services.Access.SecureChildShareReconciliationJob;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// #2795: <see cref="Job"/>'s External-team pass counted the family walks it retried (a walk that stopped short in an earlier run
/// is walked again, up to <see cref="Job.MaxExternalWalkAttempts"/> times) but reported the count nowhere: neither in
/// <c>ResultJson.externalTeam</c> nor in the <c>[EXTERNAL-TEAM-SHARE] heartbeat</c> line, while its sibling <c>carriedIn</c> is in
/// both. An operator could not see the pass retrying a walk until it gave up on it.
/// <para>Over the in-memory Dataverse (<see cref="SecureChildShareWorld"/>) with the REAL job and the REAL
/// <see cref="ExternalTeamShareSynchronizer"/>; the visibility predicate is the one substituted seam, as in
/// <c>ExternalTeamShareTests</c>. The arrange is the External Collaborators unit and team and a recently changed matter: an
/// inherent multi-record setup at a KEEP integration path (copied from <c>Issue2735_HeldFirstRunExternalTeamWatermarkTests</c>).</para>
/// </summary>
public class Issue2795_ExternalTeamWalkedAgainReportedTests
{
    private static readonly Guid ExternalBu = Guid.Parse("e2795000-0000-4000-8000-000000000001");
    private static readonly Guid ExternalTeam = Guid.Parse("e2795000-0000-4000-8000-000000000002");
    private static readonly Guid DirectBu = Guid.Parse("e2795000-0000-4000-8000-000000000003");
    private static readonly Guid DirectTeam = Guid.Parse("e2795000-0000-4000-8000-000000000004");
    private static readonly Guid Matter = Guid.Parse("e2795000-0000-4000-8000-0000000000a1");
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly FakeTimeProvider _time = new(T0);
    private readonly HeartbeatLog _log = new();
    private bool _walksFault;

    public Issue2795_ExternalTeamWalkedAgainReportedTests()
    {
        _world.Add("businessunit", ExternalBu, ("name", "External Collaborators"),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        _world.Team(ExternalTeam, ExternalBu, "External Collaborators", isDefault: true, teamType: 0);
        // A matter changed inside the first run's window: the recent pass walks the rows filed under it.
        _world.OrdinaryRoot("sprk_matter", Matter).Modified("sprk_matter", Matter, (T0 - TimeSpan.FromMinutes(10)).UtcDateTime);
    }

    [Fact]
    public async Task ExecuteAsync_AFamilyWalkThatStoppedShortIsWalkedAgain_ReportsWalkedAgainInTheResultAndTheHeartbeat()
    {
        var job = NewJob();
        _walksFault = true;
        var first = await RunAsync(job);
        Int(first, "walkedAgain").Should().Be(0, "the first run's walk is the recent pass's own, not a retry");
        _log.WalkedAgain.Should().Equal([0]);

        _walksFault = false;
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);

        Int(second, "walkedAgain").Should().Be(1, "the walk that stopped short in run 1 was walked again in run 2");
        _log.WalkedAgain.Should().Equal([0, 1], "the heartbeat carries the same count");
    }

    // ── The job, as the host composes it ──

    private Job NewJob()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecureRecord:BusinessUnitName"] = SecureChildShareWorld.SecureBuName,
            ["SecureRecord:OwnerTeamName"] = SecureChildShareWorld.SecureOwnerTeamName,
            [ExternalTeamShareSynchronizer.WritesEnabledConfigKey] = "true",
        }).Build();

        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) => _walksFault && IsFamilyWalk(query)
                ? throw new InvalidOperationException("Test: the rows filed under the matter could not be read.")
                : Task.FromResult(_world.Answer(query)));

        var engine = new ExternalTeamShareSynchronizer(
            entities.Object, _shares, Decide, configuration, NullLogger<ExternalTeamShareSynchronizer>.Instance,
            ownership: new RecordOwnershipResolver(entities.Object, configuration, NullLogger<RecordOwnershipResolver>.Instance))
        {
            DirectTeamIdentity = _ => Task.FromResult(new DirectMessageOwnerTeamIdentity(DirectBu, DirectTeam, null, null)),
        };
        var services = new ServiceCollection()
            .AddSingleton(new SecureChildShareSynchronizer(entities.Object, _shares, SecureChildShareWorld.NobodyWalled(),
                configuration, NullLogger<SecureChildShareSynchronizer>.Instance, engine))
            .AddSingleton(engine);

        return new Job(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _time, _log);
    }

    private static Task<JobRunResult> RunAsync(Job job) =>
        job.ExecuteAsync(new JobRunContext(Guid.NewGuid(), "corr-2795", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
            CancellationToken.None);

    /// <summary>The downward family walk: ids only, one <c>Or</c> group of <c>In</c> conditions, not limited.</summary>
    private static bool IsFamilyWalk(QueryExpression q) =>
        q.TopCount is null && !q.ColumnSet.AllColumns && q.ColumnSet.Columns.Count == 0
        && q.Criteria.Filters.Any(f => f.FilterOperator == LogicalOperator.Or && f.Conditions.Any(c => c.Operator == ConditionOperator.In));

    /// <summary>The predicate seam: every row Visible, filed under <see cref="Matter"/> (a root under itself).</summary>
    private static Task<ExternalVisibilityResult> Decide(IReadOnlyCollection<(string Table, Guid Id)> rows, CancellationToken ct)
    {
        var decided = new Dictionary<ExternalVisibility.Node, ExternalVisibilityDecision>();
        var reached = new Dictionary<ExternalVisibility.Node, IReadOnlyCollection<ExternalVisibility.Node>>();
        foreach (var (table, id) in rows)
        {
            var node = new ExternalVisibility.Node(table, id);
            decided[node] = new ExternalVisibilityDecision(ExternalVisibilityVerdict.Visible, ExternalVisibilityReason.Visible, table, id);
            reached[node] = [SecureChildLineage.IsRoot(table) ? node : new ExternalVisibility.Node("sprk_matter", Matter)];
        }

        return Task.FromResult(new ExternalVisibilityResult(decided, faulted: false)
        {
            RootsReached = reached,
            EffectivelySecureRoots = new HashSet<ExternalVisibility.Node>(),
        });
    }

    /// <summary>The job's <c>externalTeam.{property}</c> count; throws when the result does not carry it.</summary>
    private static int Int(JobRunResult run, string property) =>
        JsonDocument.Parse(run.ResultJson!).RootElement.GetProperty("externalTeam").GetProperty(property).GetInt32();

    /// <summary>Records the <c>WalkedAgain</c> property of each <c>[EXTERNAL-TEAM-SHARE] heartbeat</c> line; a heartbeat
    /// without the property records -1.</summary>
    private sealed class HeartbeatLog : ILogger<Job>
    {
        public List<int> WalkedAgain { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is not IReadOnlyList<KeyValuePair<string, object?>> properties)
                return;
            var template = properties.FirstOrDefault(p => p.Key == "{OriginalFormat}").Value as string;
            if (template is null || !template.StartsWith("[EXTERNAL-TEAM-SHARE] heartbeat", StringComparison.Ordinal))
                return;
            WalkedAgain.Add(properties.FirstOrDefault(p => p.Key == "WalkedAgain").Value is int count ? count : -1);
        }
    }
}
