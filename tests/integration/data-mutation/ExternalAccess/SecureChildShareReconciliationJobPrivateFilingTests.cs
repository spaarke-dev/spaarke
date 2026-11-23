using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Job = Sprk.Bff.Api.Services.Access.SecureChildShareReconciliationJob;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner 2026-11-22: a private message "always stays 1:1 - no filing"). A member of the
/// Direct team can file a private message outside the BFF; the two-minute share reconcile (<see cref="SecureChildShareReconciliationJob"/>)
/// reads the private-reference and filing columns in its Direct-message reads (the recent-changes listing and the rolling sweep) and
/// unfiles such a row through <see cref="DirectMessageOwnerStep.ClearPrivateFilingAsync"/>, before its shares are reconciled. Where
/// the reference columns are absent from metadata the job selects and does exactly what it did before.
/// </summary>
/// <remarks>Over the same in-memory Dataverse (<see cref="SecureChildShareWorld"/>) and share table as
/// <see cref="SecureChildShareReconciliationJobDirectPassTests"/>. Placement: <c>tests/integration/data-mutation/**</c>.</remarks>
public class SecureChildShareReconciliationJobPrivateFilingTests
{
    private const int Read = 1;
    private const int ThreadTypeDirect = 100000001;
    private const string Communication = "sprk_communication";

    private static readonly Guid DirectTeam = Guid.Parse("d0560000-0000-4000-8000-0000000000d1");
    private static readonly Guid Caller = Guid.Parse("c0560000-0000-4000-8000-0000000000a1");
    private static readonly Guid Other = Guid.Parse("c0560000-0000-4000-8000-0000000000a2");
    private static readonly Guid Matter = Guid.Parse("c0560000-0000-4000-8000-0000000000e1");
    private static readonly Guid FiledMatter = Guid.Parse("c0560000-0000-4000-8000-0000000000e2");
    private static readonly Guid Contact = Guid.Parse("c0560000-0000-4000-8000-0000000000e3");
    private static readonly Guid RecordType = Guid.Parse("c0560000-0000-4000-8000-0000000000e4");

    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly UserFlagDirectoryDouble _users = new UserFlagDirectoryDouble().Internal(Caller, Other);
    private readonly DirectMessageOwnerTeamDouble _directory = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly ListLogger _log = new();
    private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SecureRecord:BusinessUnitName"] = SecureChildShareWorld.SecureBuName,
        ["SecureRecord:OwnerTeamName"] = SecureChildShareWorld.SecureOwnerTeamName,
    }).Build();

    /// <summary>Every query of the job's Dataverse seam, in order.</summary>
    private readonly List<QueryExpression> _queries = new();

    /// <summary>Every clear the job sent (a conditional update carrying a cleared column), as sent.</summary>
    private readonly List<(Guid Id, string Version, IReadOnlyCollection<string> Columns)> _clears = new();

    /// <summary>What the metadata read answers: the columns of <c>sprk_communication</c>, or a fault.</summary>
    private Func<IReadOnlyDictionary<string, ColumnMetadata>> _metadata;

    /// <summary>Runs before a clear of a row is applied (a test changes the row, or throws).</summary>
    private Action<Guid>? _beforeClear;

    /// <summary>Answers (or throws for) a query before the world does; <c>Handled = false</c> lets the world answer.</summary>
    private Func<QueryExpression, (bool Handled, EntityCollection? Answer)>? _intercept;

    private DirectMessageOwnerTeam? _lookup;

    public SecureChildShareReconciliationJobPrivateFilingTests()
    {
        var directUnit = Guid.Parse("d0560000-0000-4000-8000-0000000000b1");
        _world.Add("businessunit", directUnit, ("name", DirectMessageOwnerTeamDouble.BusinessUnitName),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        _world.Team(DirectTeam, directUnit, DirectMessageOwnerTeamDouble.OwnerTeamName, isDefault: false, teamType: 0);
        _metadata = () => Columns(PrivateContextColumns.All.Concat(DirectMessageOwnerStep.PrivateFilingColumns));
    }

    // ── The job, as the host composes it ──

    private Job NewJob()
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                _queries.Add(query);
                return Task.FromResult(_intercept?.Invoke(query) is { Handled: true } handled ? handled.Answer! : _world.Answer(query));
            });
        entities.Setup(e => e.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(_metadata()));
        entities
            .Setup(e => e.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string version, Dictionary<string, object> fields, CancellationToken _) =>
            {
                if (!fields.Values.Any(v => v is DBNull))
                {
                    _world.UpdateIfVersion(table, id, version, fields); // the owner writes of the other passes
                    return Task.CompletedTask;
                }

                table.Should().Be(Communication);
                _clears.Add((id, version, fields.Keys.ToArray()));
                _beforeClear?.Invoke(id);
                var current = _world.ValueOf<long?>(table, id, "versionnumber")?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (current != version)
                    throw new System.Data.DBConcurrencyException("Test: the row changed since it was read.");
                foreach (var (column, _) in fields)
                    _world.Set(table, id, column, null);
                _world.Set(table, id, "versionnumber", (_world.ValueOf<long?>(table, id, "versionnumber") ?? 0L) + 1);
                return Task.CompletedTask;
            });

        var services = new ServiceCollection()
            .AddSingleton(_world.Synchronizer(_shares))
            .AddSingleton(SecureChildShareWorld.CoreAncestorsOver(() => _world))
            .AddSingleton<DataverseWebApiClient>(_users)
            .AddSingleton(entities.Object)
            .AddSingleton<IDataverseRecordShareService>(_shares)
            .AddSingleton(_lookup ??= _directory.Lookup(_time))
            .AddSingleton<IConfiguration>(_configuration);
        return new Job(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _time, _log);
    }

    private static JobRunContext RunContext() =>
        new(Guid.NewGuid(), "corr-056", JobRunTrigger.Scheduled, new Dictionary<string, object>(), Attempt: 1);

    private Task<JobRunResult> RunAsync() => NewJob().ExecuteAsync(RunContext(), CancellationToken.None);

    private static IReadOnlyDictionary<string, ColumnMetadata> Columns(IEnumerable<string> names) =>
        names.Append("sprk_communicationid").Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(c => c, c => new ColumnMetadata(c, "Lookup", true, true, null), StringComparer.OrdinalIgnoreCase);

    // ── The world ──

    private Guid DirectThread()
    {
        var thread = Guid.NewGuid();
        _world.Add("sprk_communicationthread", thread,
            ("ownerid", new EntityReference("systemuser", Caller)),
            ("sprk_threadtype", new OptionSetValue(ThreadTypeDirect)),
            ("sprk_isdefaultthread", false));
        _shares.Seed("sprk_communicationthread", thread, DataversePrincipalRef.User(Other), Read);
        return thread;
    }

    /// <summary>A Direct-team message in its pair's thread, stamped with the pair; <paramref name="reference"/> makes it private, and
    /// <paramref name="filing"/> are the columns set on it outside the BFF. Changed at <paramref name="modifiedOn"/> (never, for null:
    /// only the sweep reaches it).</summary>
    private Guid Message(string? reference, DateTime? modifiedOn, params (string Column, object Value)[] filing)
    {
        var message = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            ("owningteam", new EntityReference("team", DirectTeam)),
            ("sprk_communicationthread", new EntityReference("sprk_communicationthread", DirectThread())),
            ("sprk_sentby", new EntityReference("systemuser", Caller)),
            ("sprk_directrecipient", new EntityReference("systemuser", Other)),
        };
        if (modifiedOn is { } at)
            columns.Add(("modifiedon", DateTime.SpecifyKind(at, DateTimeKind.Utc)));
        if (reference is not null)
            columns.Add((reference, new EntityReference("sprk_matter", Matter)));
        columns.AddRange(filing);
        _world.Add(Communication, message, columns.ToArray());
        return message;
    }

    private static (string, object) RegardingMatter() => ("sprk_regardingmatter", new EntityReference("sprk_matter", FiledMatter));

    private object? Value(Guid message, string column) => _world.ValueOf<object>(Communication, message, column);

    private bool Granted(Guid message) =>
        (_shares.MaskOf(Communication, message, DataversePrincipalRef.User(Caller)) ?? 0) != 0
        && (_shares.MaskOf(Communication, message, DataversePrincipalRef.User(Other)) ?? 0) != 0;

    private static bool IsDirectMessageRead(QueryExpression query) =>
        query.EntityName == Communication
        && query.Criteria.Conditions.Any(c => c.AttributeName == "owningteam" && c.Values.Single() is Guid team && team == DirectTeam);

    private static bool IsSweep(QueryExpression query) =>
        IsDirectMessageRead(query) && !query.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon");

    private static bool IsListing(QueryExpression query) =>
        IsDirectMessageRead(query) && query.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon");

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // A private message filed outside the BFF is unfiled
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(PrivateContextColumns.Matter)]
    [InlineData(PrivateContextColumns.Project)]
    [InlineData(PrivateContextColumns.WorkAssignment)]
    public async Task ARecentlyChangedPrivateMessage_FiledUnderARecord_IsUnfiled_InOneConditionalUpdate_ThenReconciled(string reference)
    {
        var message = Message(reference, (T0 - TimeSpan.FromMinutes(5)).UtcDateTime, RegardingMatter());

        var run = await RunAsync();

        var clear = _clears.Should().ContainSingle("the listing and the sweep both read it; it is cleared once").Subject;
        clear.Id.Should().Be(message);
        clear.Version.Should().Be("1");
        clear.Columns.Should().BeEquivalentTo(new[] { "sprk_regardingmatter" });
        Value(message, "sprk_regardingmatter").Should().BeNull();
        Value(message, reference).Should().NotBeNull("the reference stays as written at create");
        _world.ValueOf<EntityReference>(Communication, message, "owningteam")!.Id.Should().Be(DirectTeam, "no owner change");
        Granted(message).Should().BeTrue("the pair's shares are reconciled as before");
        run.Success.Should().BeTrue(run.ErrorMessage);
        _log.Entries.Should().ContainSingle(e => e.Event.Id == DirectMessageOwnerStep.PrivateFilingClearedEvent.Id);
    }

    [Fact]
    public async Task ASweptPrivateMessage_WithATypedContactRegarding_AndTheRegardingPair_HasExactlyThoseCleared()
    {
        var message = Message(PrivateContextColumns.Project, modifiedOn: null,
            ("sprk_regardingperson", new EntityReference("contact", Contact)),
            (CoreAncestorResolver.RegardingRecordIdColumn, FiledMatter.ToString()),
            (CoreAncestorResolver.RegardingRecordTypeColumn, new EntityReference("sprk_recordtype_ref", RecordType)));

        await RunAsync();

        _clears.Should().ContainSingle().Which.Columns.Should().BeEquivalentTo(new[]
        {
            "sprk_regardingperson", CoreAncestorResolver.RegardingRecordIdColumn, CoreAncestorResolver.RegardingRecordTypeColumn,
        });
        Value(message, "sprk_regardingperson").Should().BeNull();
        Value(message, CoreAncestorResolver.RegardingRecordIdColumn).Should().BeNull();
        Value(message, CoreAncestorResolver.RegardingRecordTypeColumn).Should().BeNull();
    }

    [Fact]
    public async Task ASweptPrivateMessage_WithTheRegardingPair_HasThePairsDisplayColumnsClearedInTheSameUpdate()
    {
        _metadata = () => Columns(PrivateContextColumns.All.Concat(DirectMessageOwnerStep.PrivateFilingColumns)
            .Concat(DirectMessageOwnerStep.PrivateDisplayColumns));
        var message = Message(PrivateContextColumns.Matter, modifiedOn: null,
            (CoreAncestorResolver.RegardingRecordIdColumn, FiledMatter.ToString()),
            (CoreAncestorResolver.RegardingRecordTypeColumn, new EntityReference("sprk_recordtype_ref", RecordType)),
            ("sprk_regardingrecordname", "Acme v. Example"),
            ("sprk_regardingrecordnumber", "MAT-0042"),
            ("sprk_regardingrecordurl", "https://example.invalid/main.aspx?etn=sprk_matter"));

        var run = await RunAsync();

        _clears.Should().ContainSingle().Which.Columns.Should().BeEquivalentTo(new[]
        {
            CoreAncestorResolver.RegardingRecordIdColumn, CoreAncestorResolver.RegardingRecordTypeColumn,
            "sprk_regardingrecordname", "sprk_regardingrecordnumber", "sprk_regardingrecordurl",
        });
        foreach (var column in DirectMessageOwnerStep.PrivateDisplayColumns)
            Value(message, column).Should().BeNull(column);
        Value(message, PrivateContextColumns.Matter).Should().NotBeNull("the reference stays as written at create");
        run.Success.Should().BeTrue(run.ErrorMessage);
    }

    [Fact]
    public async Task WithTheDisplayColumns_BothMessageReadsSelectThem()
    {
        _metadata = () => Columns(PrivateContextColumns.All.Concat(DirectMessageOwnerStep.PrivateFilingColumns)
            .Concat(DirectMessageOwnerStep.PrivateDisplayColumns));
        Message(reference: null, (T0 - TimeSpan.FromMinutes(5)).UtcDateTime);

        await RunAsync();

        _queries.Should().ContainSingle(q => IsSweep(q)).Which.ColumnSet.Columns.Should().Contain(DirectMessageOwnerStep.PrivateDisplayColumns);
        _queries.Should().ContainSingle(q => IsListing(q)).Which.ColumnSet.Columns.Should().Contain(DirectMessageOwnerStep.PrivateDisplayColumns);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Rows that are left alone
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AnOrdinaryDirectMessage_WithAFilingLookup_IsLeftAlone()
    {
        var message = Message(reference: null, (T0 - TimeSpan.FromMinutes(5)).UtcDateTime, RegardingMatter());

        await RunAsync();

        _clears.Should().BeEmpty("only a private message is unfiled");
        Value(message, "sprk_regardingmatter").Should().NotBeNull();
    }

    [Fact]
    public async Task APrivateMessageWithNoFiling_IsNotWritten()
    {
        var message = Message(PrivateContextColumns.Matter, (T0 - TimeSpan.FromMinutes(5)).UtcDateTime);

        var run = await RunAsync();

        _clears.Should().BeEmpty();
        Granted(message).Should().BeTrue();
        run.Success.Should().BeTrue(run.ErrorMessage);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Faults write nothing, and the run goes on
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AClearThatThrows_WritesNothing_AndTheNextRowIsStillCleared_AndBothAreReconciled()
    {
        var failing = Message(PrivateContextColumns.Matter, modifiedOn: null, RegardingMatter());
        var next = Message(PrivateContextColumns.Matter, modifiedOn: null, RegardingMatter());
        _beforeClear = id =>
        {
            if (id == failing)
                throw new InvalidOperationException("Test: Dataverse refused the clear.");
        };

        var run = await RunAsync();

        _clears.Select(c => c.Id).Should().BeEquivalentTo(new[] { failing, next });
        Value(failing, "sprk_regardingmatter").Should().NotBeNull("the failed clear wrote nothing; the next run retries");
        Value(next, "sprk_regardingmatter").Should().BeNull();
        Granted(failing).Should().BeTrue("the run went on");
        Granted(next).Should().BeTrue();
        run.Should().NotBeNull();
    }

    [Fact]
    public async Task ARowChangedAfterItWasRead_IsNotWritten_AndTheNextRunClearsIt()
    {
        var message = Message(PrivateContextColumns.WorkAssignment, modifiedOn: null, RegardingMatter());
        var changedOnce = false;
        _beforeClear = id =>
        {
            if (id == message && !changedOnce)
            {
                changedOnce = true;
                _world.Set(Communication, id, "versionnumber", 7L); // another writer saved the row after the sweep read it
            }
        };
        var job = NewJob();

        await job.ExecuteAsync(RunContext(), CancellationToken.None);

        _clears.Should().ContainSingle();
        Value(message, "sprk_regardingmatter").Should().NotBeNull("a version mismatch writes nothing");
        _log.Entries.Should().ContainSingle(e => e.Event.Id == DirectMessageOwnerStep.PrivateFilingNotClearedEvent.Id);

        await job.ExecuteAsync(RunContext(), CancellationToken.None);

        _clears.Should().HaveCount(2);
        _clears[1].Version.Should().Be("7");
        Value(message, "sprk_regardingmatter").Should().BeNull("the sweep read it again and the clear landed");
    }

    [Fact]
    public async Task AFailedRead_WritesNothing_AndTheRunReturns()
    {
        var message = Message(PrivateContextColumns.Matter, (T0 - TimeSpan.FromMinutes(5)).UtcDateTime, RegardingMatter());
        _intercept = query => IsDirectMessageRead(query) ? throw new InvalidOperationException("Test: the read faulted.") : (false, null);

        var run = await RunAsync();

        _clears.Should().BeEmpty();
        Value(message, "sprk_regardingmatter").Should().NotBeNull();
        run.Success.Should().BeFalse("the listing and the sweep failed; the next run retries");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Reference columns absent from metadata: the job runs as before
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    public static TheoryData<string> AbsentOrUnreadable() => new() { "absent", "unreadable" };

    [Theory]
    [MemberData(nameof(AbsentOrUnreadable))]
    public async Task WithoutTheReferenceColumns_TheReadsSelectAsBefore_NothingIsCleared_AndTheRunSucceeds(string metadata)
    {
        _metadata = metadata switch
        {
            "absent" => () => Columns(DirectMessageOwnerStep.PrivateFilingColumns),
            _ => () => throw new InvalidOperationException("Test: metadata cannot be read."),
        };
        var recent = Message(reference: null, (T0 - TimeSpan.FromMinutes(5)).UtcDateTime);
        var swept = Message(reference: null, modifiedOn: null);

        var run = await RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        Granted(recent).Should().BeTrue();
        Granted(swept).Should().BeTrue();
        _clears.Should().BeEmpty();

        var sweep = _queries.Should().ContainSingle(q => IsSweep(q)).Subject;
        sweep.ColumnSet.AllColumns.Should().BeFalse();
        sweep.ColumnSet.Columns.Should().BeEmpty("the sweep selects ids only, as before");
        _queries.Should().ContainSingle(q => IsListing(q)).Which.ColumnSet.Columns.Should().Equal("modifiedon");
        _queries.Where(q => q.EntityName == Communication).SelectMany(q => q.ColumnSet.Columns)
            .Should().NotContain(PrivateContextColumns.All, "a select naming a missing column fails the whole read");
    }

    [Fact]
    public async Task AFilingColumnAbsentFromMetadata_DoesNotStopTheOthersBeingCleared()
    {
        // One filing column the environment lacks must not leave a private message filed under the columns it has: the reads select
        // (and the clear clears) the columns that exist, and never name the absent one.
        const string absent = "sprk_regardingorganization";
        _metadata = () => Columns(PrivateContextColumns.All.Concat(DirectMessageOwnerStep.PrivateFilingColumns.Where(c => c != absent)));
        var message = Message(PrivateContextColumns.Matter, (T0 - TimeSpan.FromMinutes(5)).UtcDateTime, RegardingMatter());

        var run = await RunAsync();

        run.Success.Should().BeTrue(run.ErrorMessage);
        _clears.Should().ContainSingle().Which.Columns.Should().BeEquivalentTo(new[] { "sprk_regardingmatter" });
        Value(message, "sprk_regardingmatter").Should().BeNull("the filing is cleared though another filing column is absent");
        _queries.Where(q => q.EntityName == Communication).SelectMany(q => q.ColumnSet.Columns)
            .Should().NotContain(absent, "a select naming a missing column fails the whole read");
    }

    [Fact]
    public async Task WithTheReferenceColumns_BothMessageReadsSelectThem()
    {
        Message(reference: null, (T0 - TimeSpan.FromMinutes(5)).UtcDateTime);

        await RunAsync();

        var expected = PrivateContextColumns.All.Concat(DirectMessageOwnerStep.PrivateFilingColumns).Append("versionnumber").ToArray();
        _queries.Should().ContainSingle(q => IsSweep(q)).Which.ColumnSet.Columns.Should().Contain(expected);
        _queries.Should().ContainSingle(q => IsListing(q)).Which.ColumnSet.Columns.Should().Contain(expected).And.Contain("modifiedon");
    }

    // ── a log that keeps each entry's event ──

    private sealed class ListLogger : ILogger<Job>
    {
        private readonly List<(LogLevel Level, EventId Event, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, EventId Event, string Message)> Entries
        {
            get
            {
                lock (_entries)
                    return _entries.ToList();
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
                _entries.Add((logLevel, eventId, formatter(state, exception)));
        }
    }
}
