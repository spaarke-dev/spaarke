using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology platform R1 task 150 (owner D-141, answering uac-r2's DI-86 #1883): Do items are private to their assignee on every
/// per-Signal route. <see cref="SignalCoreRecordAccess.AuthorizeAsync"/> is the one gate of the decision-plan, suppression and
/// commit routes; after its existing reads allow a Do-lane Signal, the worklist's own "mine" read (as the caller) must return the
/// Signal's subject. The caller-identity client is a small in-memory Dataverse that evaluates the "mine" clause the gate sends, so
/// who is admitted comes from the shared rule, not from the test. Maintain-class (ADR-038, <c>tests/unit/domain/**</c>).
/// The commit route's colleague case is in <c>DecisionCommitServiceTests</c> (its rig).
/// </summary>
public sealed class DoLanePrivacyTests
{
    private static readonly Guid SignalId = Guid.Parse("15000000-0000-4000-8000-000000000001");
    private static readonly Guid VersionId = Guid.Parse("15000000-0000-4000-8000-000000000002");
    private static readonly Guid MatterType = Guid.Parse("15000000-0000-4000-8000-000000000003");
    private static readonly Guid MatterId = Guid.Parse("15000000-0000-4000-8000-000000000004");
    private static readonly Guid SubjectId = Guid.Parse("15000000-0000-4000-8000-000000000005");
    private static readonly Guid Alice = Guid.Parse("15000000-0000-4000-8000-0000000000a1");
    private static readonly Guid AliceContact = Guid.Parse("15000000-0000-4000-8000-0000000000c1");
    private static readonly Guid Bob = Guid.Parse("15000000-0000-4000-8000-0000000000a2");
    private static readonly Guid BobContact = Guid.Parse("15000000-0000-4000-8000-0000000000c2");
    private static readonly Guid Team = Guid.Parse("15000000-0000-4000-8000-0000000000e1");

    private static string PersonColumn(string table) => $"_{SignalReadService.DoLaneMineRules[table].PersonColumn}_value";

    private static readonly string CreatorColumn = $"_{RecordCreatorPersonColumn.LogicalName}_value";

    public static IEnumerable<object[]> EveryMineTableWithAndWithoutACore() =>
        SignalReadService.DoLaneMineRules.Keys.SelectMany(t => new[] { new object[] { t, true }, new object[] { t, false } });

    // ---- Allowed: the people the worklist's "mine" rule admits ----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EveryMineTableWithAndWithoutACore))]
    public async Task ADoItemAssignedToTheCallersContact_IsAllowed_AndASameBuColleagueWhoReadsEverything_IsNot(string table, bool hasCore)
    {
        // The team-owned item (D-129): its person column names Alice's contact. Bob reads the Signal, the matter and the subject.
        var world = new World(table, hasCore) { Subject = { [PersonColumn(table)] = AliceContact, ["_ownerid_value"] = Team } };

        (await world.As(Alice).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.Allowed);
        (await world.As(Bob).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    [Theory]
    [InlineData("sprk_todo", true)]
    [InlineData("sprk_event", true)]
    [InlineData("sprk_workassignment", false)]  // D-44: a work assignment is the assigner's (creator) and the assignee's, never its owner's
    public async Task ADoItemOwnedByTheCaller_IsAllowedOnlyWhereTheTablesRuleCountsTheOwner(string table, bool allowed)
    {
        var world = new World(table, hasCore: true) { Subject = { ["_ownerid_value"] = Alice } };

        (await world.As(Alice).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome
            .Should().Be(allowed ? SignalAccessOutcome.Allowed : SignalAccessOutcome.NotFound);
    }

    [Theory]
    [InlineData("sprk_event", true)]
    [InlineData("sprk_workassignment", true)]
    [InlineData("sprk_todo", false)]
    public async Task ADoItemCreatedByTheCaller_IsAllowedOnlyWhereTheTablesRuleCountsTheCreator(string table, bool allowed)
    {
        var world = new World(table, hasCore: false) { Subject = { [CreatorColumn] = Alice, ["_ownerid_value"] = Team } };

        (await world.As(Alice).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome
            .Should().Be(allowed ? SignalAccessOutcome.Allowed : SignalAccessOutcome.NotFound);
    }

    // ---- The colleague: every per-Signal route answers exactly as for an unreadable Signal ------------------------------------

    [Fact]
    public async Task TheDecisionPlanRoute_AnswersTheColleague_ExactlyAsAnUnreadableSignal()
    {
        var notMine = await PlanRoute(new World("sprk_todo", hasCore: true) { Subject = { [PersonColumn("sprk_todo")] = AliceContact } }.As(Bob));
        var unreadable = await PlanRoute(new World("sprk_todo", hasCore: true) { SignalUnreadable = true }.As(Bob));

        Problem(notMine).Should().BeEquivalentTo(Problem(unreadable));
        Problem(notMine).Status.Should().Be(404);
    }

    [Fact]
    public async Task TheSuppressionRoute_AnswersTheColleague_ExactlyAsAnUnreadableSignal_AndCountsNothing()
    {
        var notMine = await SuppressionRoute(new World("sprk_todo", hasCore: true) { Subject = { [PersonColumn("sprk_todo")] = AliceContact } }, Bob);
        var unreadable = await SuppressionRoute(new World("sprk_todo", hasCore: true) { SignalUnreadable = true }, Bob);

        Problem(notMine).Should().BeEquivalentTo(Problem(unreadable));
        Problem(notMine).Status.Should().Be(404, "a strict count service would have thrown (503) had the route gone past the gate");
    }

    // ---- Fail closed ----------------------------------------------------------------------------------------------------------

    public enum Fault { ContactUnreadable, IdentityThrows, WhoAmIFaults, EntitySetThrows, MineReadFaults, MineReadThrows, MineReadUnparseable }

    [Theory]
    [InlineData(Fault.ContactUnreadable)]
    [InlineData(Fault.IdentityThrows)]
    [InlineData(Fault.WhoAmIFaults)]
    [InlineData(Fault.EntitySetThrows)]
    [InlineData(Fault.MineReadFaults)]
    [InlineData(Fault.MineReadThrows)]
    [InlineData(Fault.MineReadUnparseable)]
    public async Task AFaultInTheMineRead_IsUnavailable_NeverAllowed(Fault fault)
    {
        var world = new World("sprk_todo", hasCore: true) { Subject = { [PersonColumn("sprk_todo")] = AliceContact } };
        world.Inject(fault);

        (await world.As(Alice).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.Unavailable);
    }

    [Fact]
    public async Task AFaultInTheMineRead_Is503OnTheRoute_AsTheWorklistAnswers()
    {
        var world = new World("sprk_todo", hasCore: true) { Subject = { [PersonColumn("sprk_todo")] = AliceContact } };
        world.Inject(Fault.MineReadFaults);

        Problem(await PlanRoute(world.As(Alice))).Status.Should().Be(503);
    }

    // ---- Decide lane unchanged ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ADecideSignal_IsDecidedAsBefore_WithNoMineRead()
    {
        var world = new World("sprk_todo", hasCore: true) { Lane = DecisionLane.Decide, Subject = { [PersonColumn("sprk_todo")] = AliceContact } };

        (await world.As(Bob).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.Allowed);
        world.Requests.Should().NotContain(p => p == "WhoAmI()" || p.Contains("&$filter=", StringComparison.Ordinal));
    }

    // ---- One rule: the worklist and the gate send the same request ------------------------------------------------------------

    [Fact]
    public async Task TheGate_SendsExactlyTheWorklistsMineRequest()
    {
        var world = new World("sprk_event", hasCore: true) { Subject = { [PersonColumn("sprk_event")] = AliceContact } };

        await world.As(Alice).AuthorizeAsync(SignalId, CancellationToken.None);

        world.Requests.Where(p => p.Contains("&$filter=", StringComparison.Ordinal)).Should().Equal(
            SignalReadService.MineSubjectQuery(SignalReadService.DoLaneMineRules["sprk_event"], "sprk_events", [SubjectId], Alice, AliceContact));
    }

    [Fact]
    public void TheMinePredicate_IsBuiltInOnePlace_AndBothTheWorklistAndTheGateUseIt()
    {
        var signals = Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Signals");
        var read = File.ReadAllText(Path.Combine(signals, "SignalReadService.cs"));
        var gate = File.ReadAllText(Path.Combine(signals, "SignalCoreRecordAccess.cs"));

        // MineFilter is called only by MineSubjectQuery (its definition is the other match).
        Regex.Matches(read, @"\bMineFilter\(").Count.Should().Be(2);
        Regex.Matches(read, @"\bMineSubjectQuery\(").Count.Should().Be(2, "its definition and the worklist's Do-lane read");
        Regex.Matches(read, @"\bResolveMineCallerAsync\(").Count.Should().Be(2, "its definition and the worklist's caller resolution");
        gate.Should().Contain("SignalReadService.MineSubjectQuery(").And.Contain("SignalReadService.ResolveMineCallerAsync(");
        gate.Should().NotContain("MineFilter(").And.NotContain("_ownerid_value").And.NotContain("sprk_createdbyperson",
            "the gate never builds the predicate itself");
    }

    // =================================================================================================
    // Fixtures
    // =================================================================================================

    private static Task<IResult> PlanRoute(SignalCoreRecordAccess access) =>
        DecisionPlanEndpoints.GetDecisionPlanAsync(
            SignalId, Http(), access,
            new DecisionPlanService(new Mock<IGenericEntityService>(MockBehavior.Strict).Object, NullLogger<DecisionPlanService>.Instance),
            new RuleBodyDescriber(
                new PolicyVersionValidator(new RuleBodySchemaValidator(), new PredicateCompiler(new RuleBodySchemaValidator(), TimeProvider.System),
                    NullLogger<PolicyVersionValidator>.Instance),
                new Mock<IGenericEntityService>().Object, NullLogger<RuleBodyDescriber>.Instance),
            CancellationToken.None);

    private static Task<IResult> SuppressionRoute(World world, Guid actingAs)
    {
        var access = world.As(actingAs);
        var strict = new SignalSuppressionService(
            new Mock<IGenericEntityService>(MockBehavior.Strict).Object,
            new OntologyWriterDataverseClient(() => new Mock<IOrganizationServiceAsync2>(MockBehavior.Strict).Object, NullLogger<OntologyWriterDataverseClient>.Instance),
            TimeProvider.System, new ConfigurationBuilder().Build(),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SignalSuppressionService>.Instance);
        return DecisionPlanEndpoints.GetSuppressionAsync(
            SignalId, Http(), access, strict, world.Client, NullLogger<SignalSuppressionService>.Instance, CancellationToken.None);
    }

    private static HttpContext Http() => new DefaultHttpContext { TraceIdentifier = "trace-150" };

    private static Microsoft.AspNetCore.Mvc.ProblemDetails Problem(IResult result) =>
        result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    /// <summary>
    /// One Signal on a matter (or with no core), whose subject is one row of <see cref="Table"/>. Alice and Bob are in the same BU:
    /// both read the Signal, the matter and the subject. The "mine" read is evaluated like Dataverse would: the id list, then any
    /// <c>_column_value eq id</c> part of the OR-ed clause the gate sent.
    /// </summary>
    private sealed class World(string table, bool hasCore)
    {
        public string Table { get; } = table;
        public DecisionLane Lane { get; init; } = DecisionLane.Do;
        public bool SignalUnreadable { get; init; }
        public Dictionary<string, Guid> Subject { get; } = new(StringComparer.Ordinal);
        public List<string> Requests { get; } = new();
        public IDataverseUserClient Client => _client.Object;

        private readonly Mock<IDataverseUserClient> _client = new();
        private Guid _actingAs;
        private Fault? _fault;

        public void Inject(Fault fault) => _fault = fault;

        public SignalCoreRecordAccess As(Guid user)
        {
            _actingAs = user;
            _client.Setup(u => u.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken _) =>
                {
                    Requests.Add(path);
                    if (_fault == Fault.MineReadThrows && path.Contains("&$filter=", StringComparison.Ordinal))
                    {
                        return Task.FromException<DataverseUserResponse>(new TimeoutException("no answer"));
                    }

                    return Task.FromResult(Answer(path));
                });

            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", MatterType, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_recordtype_ref", MatterType) { ["sprk_recordlogicalname"] = "sprk_matter" });
            entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string logical, CancellationToken _) => _fault == Fault.EntitySetThrows && logical == Table
                    ? Task.FromException<string>(new TimeoutException("no metadata"))
                    : Task.FromResult(logical + "s"));

            var identity = new Mock<IIdentityNormalizationService>();
            identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, CancellationToken _) => _fault switch
                {
                    Fault.IdentityThrows => Task.FromException<PersonIdentity>(new TimeoutException("no answer")),
                    Fault.ContactUnreadable => Task.FromResult(new PersonIdentity(id) { Faults = IdentityReadFaults.SystemUser }),
                    _ => Task.FromResult(new PersonIdentity(id, id == Alice ? AliceContact : id == Bob ? BobContact : null)),
                });

            return new SignalCoreRecordAccess(_client.Object, entities.Object, identity.Object, NullLogger<SignalCoreRecordAccess>.Instance);
        }

        private DataverseUserResponse Answer(string path)
        {
            if (path is "WhoAmI" or "WhoAmI()")
            {
                return _fault == Fault.WhoAmIFaults && path == "WhoAmI()"
                    ? DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "boom")
                    : Ok(new { UserId = _actingAs.ToString("D") });
            }

            if (path.StartsWith("sprk_signals(", StringComparison.Ordinal))
            {
                if (SignalUnreadable)
                {
                    return DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "no");
                }

                var row = new Dictionary<string, object?>
                {
                    ["sprk_lane"] = (int)Lane,
                    ["_sprk_policyversion_value"] = VersionId.ToString("D"),
                    [$"_{SignalWriter.RegardingLookupByEntity[Table]}_value"] = SubjectId.ToString("D"),
                    ["sprk_regardingrecordid"] = SubjectId.ToString("D"),
                };
                if (hasCore)
                {
                    row["_sprk_corerecordtype_value"] = MatterType.ToString("D");
                    row["sprk_corerecordid"] = MatterId.ToString("D");
                }

                return Ok(row);
            }

            if (path.Contains("&$filter=", StringComparison.Ordinal))
            {
                if (_fault == Fault.MineReadFaults)
                {
                    return DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "boom");
                }

                if (_fault == Fault.MineReadUnparseable)
                {
                    return Ok(new { unexpected = true });
                }

                var filter = path[(path.IndexOf("$filter=", StringComparison.Ordinal) + 8)..];
                var ids = Regex.Matches(filter[..(filter.IndexOf("])", StringComparison.Ordinal) + 2)], "'([0-9a-f-]{36})'")
                    .Select(m => Guid.Parse(m.Groups[1].Value));
                var mine = Regex.Matches(filter, @"(_\w+_value) eq ([0-9a-f-]{36})")
                    .Any(m => Subject.TryGetValue(m.Groups[1].Value, out var v) && v == Guid.Parse(m.Groups[2].Value));
                var pk = $"{Table}id";
                return Ok(new { value = ids.Contains(SubjectId) && mine ? new[] { new Dictionary<string, object> { [pk] = SubjectId.ToString("D") } } : [] });
            }

            // The matter and the subject row (createdon): both readable to Alice and Bob.
            return path.StartsWith("sprk_matters(", StringComparison.Ordinal) || path.StartsWith($"{Table}s(", StringComparison.Ordinal)
                ? Ok(new { createdon = "2026-10-01T00:00:00Z" })
                : DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "unscripted");
        }

        private static DataverseUserResponse Ok(object body) => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));
    }
}
