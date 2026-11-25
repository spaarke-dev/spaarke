using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using Commit = Sprk.Bff.Api.Tests.Integration.DataMutation.Signals.DecisionCommitServiceTests;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ISS-010 (#3184): the live four-action decision (Create budget, Assign Task, Add To Do, Assign Work) through the REAL commit
/// path end to end: the REAL <see cref="DecisionCommitService"/> over the commit rig (ledger, Signals, ontology writer), the REAL
/// four executors, the REAL in-process route core (<c>ChildRecordEndpoints.CreateAsync</c>), the REAL
/// <c>RecordCreationService</c> with the REAL owner resolver, core-ancestor restamper, share synchronizer and secure-root gate,
/// and the SHIPPED caller-access probe with only its wire answered. Every Dataverse call is counted at the boundary this harness
/// fakes (<see cref="DecisionCommitCallProfile"/>): by interface method and table for an SDK call, by Web API path for a caller
/// call, with the production method that made it and the decision step it ran in.
/// </summary>
/// <remarks>
/// <para><b>What these tests pin (ADR-038 data-mutation, KEEP).</b> The commit's observable behaviour for this decision, so a
/// latency fix that changes only how often a read is asked can be proven equivalent: the same writes in the same order with the
/// same payloads (the Decision Record last), the same response and warnings, the same replay (a second commit with the same
/// review id writes nothing), the same refusal when an access check says no before or after a write, and the same failure when a
/// check faults (a fault is never an allow).</para>
/// <para><b>Not modelled.</b> The Assign Task email (the follow-up seam is answered in memory; live it is ~3 s of the 27 s), the
/// budget currency read (Create budget's parameter sources, answered in memory), the OBO token exchange (token-cached live), and
/// the "Assigned *" grants (the create's scopes have none registered).</para>
/// <para>Set the environment variable <c>ISS010_PROFILE_OUT</c> to a file path to write the per-site profile there.</para>
/// </remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid PfVersion = Guid.Parse("c0430000-0000-4000-8000-0000000003a0");
    private static readonly Guid PfAssignee = Guid.Parse("c0430000-0000-4000-8000-0000000003a1");
    private static readonly Guid PfReview = Guid.Parse("c0430000-0000-4000-8000-0000000003a2");

    /// <summary>The live decision's plan: Revise budget (Create budget on a matter with none) and Assign Task, then the two Next steps.</summary>
    private const string FourActionPlan = "{\"actions\":[\"revise-budget\",\"assign-task\"],\"nextSteps\":[\"add-todo\",\"assign-work\"]}";

    /// <summary>The world one four-action commit runs in, with every seam counted.</summary>
    private sealed record FourActionWorld(
        Commit.Rig Rig, HttpContext Http, DecisionCommitCallProfile Profile, ScriptedUserClient User, DecisionCommitCallProfile.ProfiledProbe Probe)
    {
        public Task<DecisionCommitResult> CommitAsync(Guid? reviewId = null)
        {
            new HttpContextAccessor().HttpContext = Http;
            return Rig.CommitAsync(FourActionReview(reviewId ?? PfReview), http: Http);
        }
    }

    private FourActionWorld BuildFourActionWorld()
    {
        var profile = new DecisionCommitCallProfile();
        _world.WithUser(Commit.Caller, Commit.CallerOid, Directory.ChildBu); // the commit rig's caller, in the child business unit

        // The caller's own Dataverse for the route core (the create's caller checks and metadata reads).
        var user = new ScriptedUserClient(Commit.Caller) { OnPatch = _world.ApplyPatch };
        user.RecordTypes[Commit.TypeMatter] = "sprk_matter";

        var gateEntities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        gateEntities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EntityCollection());
        gateEntities.AnswersRecordTypeCatalog();

        var appOnly = profile.Wrap(_appOnly.Object, "app-only writer (IFieldMappingDataverseService)");
        var creation = RouteRecordCreation.Over(
            _world.Resolver(seam: e => profile.Wrap(e, "owner resolver (IGenericEntityService)")),
            appOnly,
            new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld(
                seam: e => profile.Wrap(e, "core-ancestor stamp (IGenericEntityService)")).Restamper,
            new SecureChildShareSynchronizer(
                profile.Wrap(SecureChildShareWorld.EntitiesOver(() => ShareWorld).Object, "share sync (IGenericEntityService)"),
                profile.Wrap<IDataverseRecordShareService>(_shareTable, "share sync (IDataverseRecordShareService)"),
                SecureChildShareWorld.NobodyWalled(), SecureChildShareWorld.Configuration(), NullLogger<SecureChildShareSynchronizer>.Instance),
            SecureRootFilingGateFixtures.Over(profile.Wrap(gateEntities.Object, "secure-root gate (IGenericEntityService)")),
            entities: profile.Wrap(RouteRecordCreation.Entities().Object, "record creation (IGenericEntityService)"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(profile.Wrap<IDataverseUserClient>(user, "caller (route core, IDataverseUserClient)"));
        services.AddSingleton(creation);
        services.AddSingleton(appOnly);

        var http = Commit.Http();
        ((ClaimsIdentity)http.User.Identity!).AddClaim(
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Commit.CallerOid.ToString()));
        http.RequestServices = services.BuildServiceProvider();

        var probe = new DecisionCommitCallProfile.ProfiledProbe(profile, Commit.Caller);
        var rig = new Commit.Rig { ActiveBudgets = 0, ProbeOverride = probe };
        rig.AddSignal(Commit.Primary, DecisionLane.Decide, OrdinaryMatter, PfVersion);
        rig.Entities.Setup(e => e.RetrieveAsync("sprk_policyversion", PfVersion, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_policyversion", PfVersion) { ["sprk_decisionplan"] = FourActionPlan });
        rig.FollowUps.Setup(f => f.NotifyAssigneesAsync(It.IsAny<HttpContext>(), It.IsAny<IReadOnlyList<AssignTaskNotice>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        rig.EntitiesSeam = profile.Wrap(rig.Entities.Object, "commit app-only (IGenericEntityService)");
        rig.UserSeam = profile.Wrap(rig.User.Object, "commit caller (IDataverseUserClient)");
        rig.OrgSeam = profile.Wrap(rig.Org.Object, "ontology writer (IOrganizationServiceAsync2)");

        var cores = new DecisionRouteCores();
        var executorEntities = new Mock<IGenericEntityService>();
        foreach (var (table, set) in new[] { ("sprk_matter", "sprk_matters"), ("contact", "contacts"), ("sprk_budget", "sprk_budgets"), ("sprk_event", "sprk_events"), ("sprk_todo", "sprk_todos"), ("sprk_workassignment", "sprk_workassignments") })
            executorEntities.Setup(e => e.GetEntitySetNameAsync(table, It.IsAny<CancellationToken>())).ReturnsAsync(set);
        var entities = profile.Wrap(executorEntities.Object, "executor (IGenericEntityService)");
        rig.RealExecutors = new IDecisionActionExecutor[]
        {
            new CreateBudgetExecutor(probe, cores, entities, rig.Clock, CurrencyOnlySources()),
            new AssignTaskExecutor(probe, cores, entities),
            new AddTodoExecutor(probe, cores, entities),
            new AssignWorkExecutor(probe, cores, entities),
        }.Select(profile.Staged).ToList();

        return new FourActionWorld(rig, http, profile, user, probe);
    }

    private static DecisionReview FourActionReview(Guid reviewId) => new()
    {
        ReviewId = reviewId,
        Confirmed = true,
        Actions =
        [
            new DecisionReviewAction { Code = "create-budget", Taken = true, Values = PfValues(("amount", 90000m), ("reason", "No budget was ever set")) },
            new DecisionReviewAction
            {
                Code = "assign-task", Taken = true,
                Values = PfValues(("assignee", PfAssignee.ToString("D")), ("description", "Call the client about the budget"), ("dueDate", "2026-11-15"), ("notify", true)),
            },
        ],
        NextSteps =
        [
            new DecisionReviewNextStep { Code = "add-todo", Values = PfValues(("title", "Chase the revision"), ("assignee", PfAssignee.ToString("D")), ("dueDate", "2026-10-20")) },
            new DecisionReviewNextStep { Code = "assign-work", Values = PfValues(("assignee", PfAssignee.ToString("D")), ("description", "Review the budget with the firm")) },
        ],
        Because = "The matter never had a budget",
    };

    private static IReadOnlyDictionary<string, JsonElement> PfValues(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value), StringComparer.Ordinal);

    private static string Normalized(DecisionCommitResult result) =>
        DecisionCommitCallProfile.Tokenize(JsonSerializer.Serialize(result));

    private static void WriteProfile(string heading, DecisionCommitCallProfile profile)
    {
        if (Environment.GetEnvironmentVariable("ISS010_PROFILE_OUT") is not { Length: > 0 } path)
            return;
        File.AppendAllText(path, $"## {heading}\n{profile.Table()}\nTOTAL {profile.Calls.Count} calls; writes {profile.Calls.Count(c => c.IsWrite)}\n\n"
            + $"WRITES\n{profile.WriteTranscript()}\n\n");
    }

    [Fact]
    public async Task Iss010_FourActionDecision_IsRecorded_ThroughTheRealPath_AndItsCallsAreProfiled()
    {
        var w = BuildFourActionWorld();

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        _appCreates.Select(c => c.Table).Should().Equal("sprk_budget", "sprk_event", "sprk_todo", "sprk_workassignment");
        w.Rig.Records.Should().ContainSingle().Which.LogicalName.Should().Be("sprk_decisionrecord");
        WriteProfile("four-action commit (no latency)", w.Profile);
    }

    [Fact]
    public async Task Iss010_FourActionDecision_WithAFixedLatencyPerCall_ReportsTheSimulatedTotal()
    {
        const int latencyMs = 20;
        var w = BuildFourActionWorld();
        w.Profile.Latency = TimeSpan.FromMilliseconds(latencyMs);

        var clock = Stopwatch.StartNew();
        var result = await w.CommitAsync();
        clock.Stop();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var calls = w.Profile.Calls.Count;
        if (Environment.GetEnvironmentVariable("ISS010_PROFILE_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, string.Create(CultureInfo.InvariantCulture,
                $"## latency\n{calls} calls x {latencyMs} ms; serial sum {calls * latencyMs} ms; measured {clock.ElapsedMilliseconds} ms\n\n"));
        }
    }
}
