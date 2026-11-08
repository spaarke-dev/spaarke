using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// field-mapping-server-write-path-r1 task 044 fix (ontology D-210): <see cref="SignalCoreRecordAccess.AuthorizeAsync"/> reads the core-record
/// pair app-only when the caller's read returns neither half, and denies when a pair exists. Only an app-only read that succeeds with neither
/// half takes the no-core path; a stored pair (whole or half) or a failed app-only read is the same denial as an unreadable core. The distinct
/// event carries the Signal id and lane as scope fields only. Maintain-class (ADR-038, <c>tests/unit/domain/**</c>).
/// </summary>
public sealed class CorePairAppOnlyReadTests
{
    private static readonly Guid SignalId = Guid.Parse("44000000-0000-4000-8000-000000000001");
    private static readonly Guid VersionId = Guid.Parse("44000000-0000-4000-8000-000000000002");
    private static readonly Guid MatterType = Guid.Parse("44000000-0000-4000-8000-000000000003");
    private static readonly Guid MatterId = Guid.Parse("44000000-0000-4000-8000-000000000004");
    private static readonly Guid TodoId = Guid.Parse("44000000-0000-4000-8000-000000000005");

    // ---- Must-not-fire: a genuine no-pair Signal takes the no-core path, unchanged -----------------------------------------------

    [Fact]
    public async Task AGenuineNoPairDoSignal_TakesTheNoCorePath_Unchanged()
    {
        var rig = new Rig(DecisionLane.Do, entities => entities.WithNoStoredCorePair());

        var decision = await rig.Access.AuthorizeAsync(SignalId, CancellationToken.None, enforceDoLaneMine: false);

        decision.Outcome.Should().Be(SignalAccessOutcome.Allowed);
        decision.SubjectTable.Should().Be("sprk_todo");
        decision.SubjectId.Should().Be(TodoId);
        decision.CoreId.Should().BeNull("a no-core Signal carries no core record");
        rig.Requests.Should().Contain(p => p.StartsWith("sprk_todos(", StringComparison.Ordinal), "the no-core path reads the subject as the caller");
        rig.Logs.Should().NotContain(l => l.EventId == SignalAccessEvents.CorePairPresentAppOnly || l.EventId == SignalAccessEvents.CorePairAppOnlyReadFailed);
    }

    [Fact]
    public async Task APairTheCallerCanSee_IsDecidedByTheCoreRecord_AndNeverReReadAppOnly()
    {
        // Pair visible to the caller: unchanged. A strict "no app-only Signal read" mock proves the re-read is not asked.
        var rig = new Rig(DecisionLane.Decide, entities => entities.WithCorePairReadFault(new InvalidOperationException("must not be asked")), callerSeesPair: true);

        var decision = await rig.Access.AuthorizeAsync(SignalId, CancellationToken.None);

        decision.Outcome.Should().Be(SignalAccessOutcome.Allowed);
        decision.CoreId.Should().Be(MatterId);
        rig.AppOnlySignalReads.Should().Be(0);
    }

    // ---- Must-fire: the caller's read has no pair, the app-only read finds one → deny, distinct event --------------------------

    public static IEnumerable<object?[]> StoredPairs() =>
    [
        [MatterType, MatterId],   // whole
        [MatterType, null],       // type half only
        [null, MatterId],         // id half only
    ];

    [Theory]
    [MemberData(nameof(StoredPairs))]
    public async Task APairHiddenFromTheCaller_IsDenied_ExactlyAsAnUnreadableCore_AndLogsTheDistinctEvent(Guid? storedType, Guid? storedId)
    {
        var rig = new Rig(DecisionLane.Do, entities => entities.WithStoredCorePair(storedType, storedId));

        var decision = await rig.Access.AuthorizeAsync(SignalId, CancellationToken.None);

        decision.Should().Be(SignalAccessDecision.NotFound, "the same outcome and response as an unreadable core: the route is not an existence oracle");
        decision.Should().Be(await UnreadableCoreDecision(), "a caller cannot tell a hidden pair from a core they cannot read");
        rig.Requests.Should().NotContain(p => p.StartsWith("sprk_todos(", StringComparison.Ordinal), "the no-core path is never reached");

        var logged = rig.Logs.Should().ContainSingle(l => l.EventId == SignalAccessEvents.CorePairPresentAppOnly).Subject;
        logged.Scope.Should().BeEquivalentTo(new Dictionary<string, object> { ["SignalId"] = SignalId, ["Lane"] = nameof(DecisionLane.Do) });
        logged.Message.Should().NotMatchRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}", "the message text carries no id; the id is a scope field");
        logged.Message.Should().NotContain(MatterId.ToString("D")).And.NotContain(MatterType.ToString("D"));
    }

    [Fact]
    public async Task ADecideLaneSignalWithAHiddenPair_IsDenied_NotFound()
    {
        // The Decide lane's existing denial shape for a Signal it cannot check is the uniform NotFound (404), not a silent drop.
        var rig = new Rig(DecisionLane.Decide, entities => entities.WithStoredCorePair(MatterType, MatterId));

        (await rig.Access.AuthorizeAsync(SignalId, CancellationToken.None)).Should().Be(SignalAccessDecision.NotFound);
        rig.Logs.Should().ContainSingle(l => l.EventId == SignalAccessEvents.CorePairPresentAppOnly)
            .Which.Scope["Lane"].Should().Be(nameof(DecisionLane.Decide));
    }

    // ---- Must-fire: the caller's read has no pair and the app-only read fails → deny ------------------------------------------

    public enum AppOnlyFault { Throws, Throttled, NoRow }

    [Theory]
    [InlineData(AppOnlyFault.Throws)]
    [InlineData(AppOnlyFault.Throttled)]
    [InlineData(AppOnlyFault.NoRow)]
    public async Task WhenTheAppOnlyReadFails_TheSignalIsDenied_NeverTheNoCorePath(AppOnlyFault fault)
    {
        var rig = new Rig(DecisionLane.Do, entities => fault switch
        {
            AppOnlyFault.Throws => entities.WithCorePairReadFault(new TimeoutException("no answer")),
            AppOnlyFault.Throttled => entities.WithCorePairReadFault(new System.ServiceModel.FaultException("throttled")),
            _ => NoRow(entities),
        });

        var decision = await rig.Access.AuthorizeAsync(SignalId, CancellationToken.None);

        decision.Should().Be(SignalAccessDecision.NotFound);
        rig.Requests.Should().NotContain(p => p.StartsWith("sprk_todos(", StringComparison.Ordinal), "the no-core path is never reached");
        rig.Logs.Should().ContainSingle(l => l.EventId == SignalAccessEvents.CorePairAppOnlyReadFailed)
            .Which.Scope.Should().BeEquivalentTo(new Dictionary<string, object> { ["SignalId"] = SignalId, ["Lane"] = nameof(DecisionLane.Do) });

        static Mock<IGenericEntityService> NoRow(Mock<IGenericEntityService> entities)
        {
            entities.Setup(e => e.RetrieveAsync(SignalCoreRecordAccess.SignalTable, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Entity)null!);
            return entities;
        }
    }

    // ---- One rule per route (UAC-r2, fix round 2): a re-read fault answers what a core-read fault answers ----------------------
    // The decision-plan, suppression and commit routes all decide through AuthorizeAsync. A fault reading the core record (a 5xx,
    // throttling, a thrown transport fault) is the uniform NotFound (404) there, so a fault of the app-only re-read is the same 404.
    // Known limit K (task-044 notes): a transient fault on these two reads looks like "not found", not 503.

    public enum CoreReadFault { ServiceError, RateLimited, Throws }

    [Theory]
    [InlineData(DecisionLane.Do, CoreReadFault.ServiceError)]
    [InlineData(DecisionLane.Do, CoreReadFault.RateLimited)]
    [InlineData(DecisionLane.Do, CoreReadFault.Throws)]
    [InlineData(DecisionLane.Decide, CoreReadFault.ServiceError)]
    [InlineData(DecisionLane.Decide, CoreReadFault.RateLimited)]
    [InlineData(DecisionLane.Decide, CoreReadFault.Throws)]
    public async Task AReReadFault_GivesTheSameDecisionAsACoreReadFault(DecisionLane lane, CoreReadFault coreFault)
    {
        var onCoreFault = await new Rig(lane, entities => entities, callerSeesPair: true, coreFault: coreFault)
            .Access.AuthorizeAsync(SignalId, CancellationToken.None);
        var onReReadThrow = await new Rig(lane, entities => entities.WithCorePairReadFault(new TimeoutException("no answer")))
            .Access.AuthorizeAsync(SignalId, CancellationToken.None);
        var onReReadThrottle = await new Rig(lane, entities => entities.WithCorePairReadFault(new System.ServiceModel.FaultException("throttled")))
            .Access.AuthorizeAsync(SignalId, CancellationToken.None);

        onReReadThrow.Should().Be(onCoreFault, "one rule per route: a re-read fault answers what a core-read fault answers");
        onReReadThrottle.Should().Be(onCoreFault);
        onCoreFault.Should().Be(SignalAccessDecision.NotFound, "the core-record read's fault is the uniform 404 today (known limit K)");
    }

    [Fact]
    public async Task CancellationOfTheAppOnlyRead_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var rig = new Rig(DecisionLane.Do, entities => entities.WithCorePairReadFault(new OperationCanceledException(cts.Token)));

        var act = () => rig.Access.AuthorizeAsync(SignalId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // =================================================================================================
    // Fixtures
    // =================================================================================================

    /// <summary>The decision for a Signal whose core the caller cannot read (the existing denial), for comparison.</summary>
    private static async Task<SignalAccessDecision> UnreadableCoreDecision()
    {
        var rig = new Rig(DecisionLane.Do, entities => entities, callerSeesPair: true, coreReadable: false);
        return await rig.Access.AuthorizeAsync(SignalId, CancellationToken.None);
    }

    private sealed record LogEntry(EventId EventId, string Message, IReadOnlyDictionary<string, object> Scope);

    private sealed class Rig
    {
        public List<string> Requests { get; } = [];
        public List<LogEntry> Logs { get; } = [];
        public int AppOnlySignalReads { get; private set; }
        public SignalCoreRecordAccess Access { get; }

        public Rig(DecisionLane lane, Func<Mock<IGenericEntityService>, Mock<IGenericEntityService>> appOnly, bool callerSeesPair = false, bool coreReadable = true,
            CoreReadFault? coreFault = null)
        {
            var users = new Mock<IDataverseUserClient>();
            users.Setup(u => u.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string path, CancellationToken _) =>
                {
                    Requests.Add(path);
                    if (path.StartsWith("sprk_signals(", StringComparison.Ordinal))
                    {
                        var row = new Dictionary<string, object?>
                        {
                            ["sprk_lane"] = (int)lane,
                            ["_sprk_policyversion_value"] = VersionId.ToString("D"),
                            [$"_{SignalWriter.RegardingLookupByEntity["sprk_todo"]}_value"] = TodoId.ToString("D"),
                            ["sprk_regardingrecordid"] = TodoId.ToString("D"),
                        };
                        if (callerSeesPair)
                        {
                            row["_sprk_corerecordtype_value"] = MatterType.ToString("D");
                            row["sprk_corerecordid"] = MatterId.ToString("D");
                        }

                        return Ok(row);
                    }

                    if (path is "WhoAmI" or "WhoAmI()")
                    {
                        return Ok(new { UserId = Guid.NewGuid().ToString("D") });
                    }

                    if (path.StartsWith("sprk_matters(", StringComparison.Ordinal) && coreFault is { } fault)
                    {
                        return fault switch
                        {
                            CoreReadFault.ServiceError => DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "unavailable"),
                            CoreReadFault.RateLimited => DataverseUserResponse.Fail(429, DataverseUserClientErrorCodes.RateLimited, "throttled"),
                            _ => throw new HttpRequestException("transport fault"),
                        };
                    }

                    if (path.StartsWith("sprk_matters(", StringComparison.Ordinal) && !coreReadable)
                    {
                        return DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "no");
                    }

                    return path.StartsWith("sprk_matters(", StringComparison.Ordinal) || path.StartsWith("sprk_todos(", StringComparison.Ordinal)
                        ? Ok(new { createdon = "2026-11-01T00:00:00Z" })
                        : DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "unscripted");
                });

            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", MatterType, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_recordtype_ref", MatterType) { ["sprk_recordlogicalname"] = "sprk_matter" });
            entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string logical, CancellationToken _) => logical + "s");
            appOnly(entities);
            entities.Invocations.Clear();

            var counted = new Mock<IGenericEntityService>();
            counted.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string table, Guid id, string[] columns, CancellationToken ct) =>
                {
                    if (table == SignalCoreRecordAccess.SignalTable)
                    {
                        AppOnlySignalReads++;
                        columns.Should().BeEquivalentTo([SignalCoreRecordAccess.CoreRecordTypeColumn, SignalCoreRecordAccess.CoreRecordIdColumn],
                            "the app-only read selects only the two pair columns");
                    }

                    return entities.Object.RetrieveAsync(table, id, columns, ct);
                });
            counted.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string logical, CancellationToken ct) => entities.Object.GetEntitySetNameAsync(logical, ct));

            Access = new SignalCoreRecordAccess(users.Object, counted.Object, Mock.Of<IIdentityNormalizationService>(), new CapturingLogger(Logs));
        }

        private static DataverseUserResponse Ok(object body) => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));
    }

    /// <summary>Records each entry with the scope fields active when it was written.</summary>
    private sealed class CapturingLogger(List<LogEntry> logs) : ILogger<SignalCoreRecordAccess>
    {
        private readonly Stack<IReadOnlyDictionary<string, object>> _scopes = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            _scopes.Push(state as IReadOnlyDictionary<string, object> ?? new Dictionary<string, object>());
            return new Pop(_scopes);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            logs.Add(new LogEntry(eventId, formatter(state, exception),
                _scopes.SelectMany(s => s).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.First().Value)));

        private sealed class Pop(Stack<IReadOnlyDictionary<string, object>> scopes) : IDisposable
        {
            public void Dispose() => scopes.Pop();
        }
    }
}
