using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// spaarke-ontology-platform-r1 task 043 (the #1634 review; uac-r2's pair rule): a decision's child creates go through the
/// REAL executors (<see cref="AddTodoExecutor"/>, <see cref="AssignWorkExecutor"/>), the REAL in-process
/// <see cref="DecisionRouteCores.CreateChildAsync"/>, the REAL <c>ChildRecordEndpoints.CreateAsync</c> and so the REAL
/// <c>CallerWriteAuthorization.PairTargetAsync</c> over this harness's caller and resolver. The request is the one the commit route builds:
/// the core comes from the Signal's server-written <c>sprk_corerecordtype</c> / <c>sprk_corerecordid</c>, so the create sends the
/// TYPED ADR-024 pair, and a work assignment also binds its typed regarding lookup (uac-r2's pair rule).
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid DcRecordTypeMatter = Guid.Parse("a0430000-0000-4000-8000-000000000001");
    private static readonly Guid DcAssignee = Guid.Parse("a0430000-0000-4000-8000-000000000002");
    private static readonly Guid DcSignal = Guid.Parse("a0430000-0000-4000-8000-000000000003");

    /// <summary>The commit route's request: the caller's HTTP context carrying the services the in-process core resolves.</summary>
    private DecisionActionRequest DecisionRequest(Guid matter, Dictionary<string, string?> values)
    {
        _user.RecordTypes[DcRecordTypeMatter] = "sprk_matter"; // the caller reads the catalog row (the core's pair rule)
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataverseUserClient>(_user);
        // Task 017: the route's create takes the ONE server create, built over this harness's collaborators.
        services.AddSingleton(RouteRecordCreation.Over(
            _world.Resolver(), _appOnly.Object, Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure()));
        var http = HttpContextOfCaller();
        http.RequestServices = services.BuildServiceProvider();
        return new DecisionActionRequest(http, DcSignal, null, new DecisionCoreRecord("sprk_matter", matter, DcRecordTypeMatter), matter, values);
    }

    /// <summary>The executors' own pre-write checks answered yes, so the test is about the create core alone.</summary>
    private static CallerRecordAccessProbe GrantingProbe()
    {
        var probe = new Mock<CallerRecordAccessProbe>(
            new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!)
        { CallBase = false };
        probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read | AccessRights.Write | AccessRights.AppendTo | AccessRights.Append);
        probe.Setup(p => p.CallerHoldsPrivilegeAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return probe.Object;
    }

    private static IGenericEntityService EntitySets()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_matters");
        return entities.Object;
    }

    [Fact]
    public async Task DecisionAddTodo_SendsTheTypedPair_ThroughTheRealPairRule_AndIsCreated()
    {
        var executor = new AddTodoExecutor(GrantingProbe(), new DecisionRouteCores(), EntitySets());

        var outcome = await executor.ExecuteAsync(DecisionRequest(OrdinaryMatter, new()
        {
            ["title"] = "Chase the revision",
            ["assignee"] = DcAssignee.ToString("D"),
            ["dueDate"] = "2026-10-20",
        }), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done, outcome.Detail);
        var (table, id, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_todo");
        outcome.Written.Should().Equal(new DecisionRecordRef("sprk_todo", id));
        Bind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})");
        Bind(fields, "sprk_RegardingRecordType@odata.bind").Should().Be($"/sprk_recordtype_refs({DcRecordTypeMatter:D})",
            "the #1634 review: the typed pair, not an untyped id");
        Owner(fields).Should().Be(Directory.ChildTeam, "record-first: the matter's business-unit team");
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task DecisionAssignWork_BindsTheTypedRegardingLookup_AsWellAsThePair_AndIsCreated()
    {
        var executor = new AssignWorkExecutor(GrantingProbe(), new DecisionRouteCores(), EntitySets());

        var outcome = await executor.ExecuteAsync(DecisionRequest(OrdinaryMatter, new()
        {
            ["assignee"] = DcAssignee.ToString("D"),
            ["description"] = "Review the revised budget\nwith the firm",
        }), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done, outcome.Detail);
        var (table, id, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_workassignment");
        outcome.Written.Should().Equal(new DecisionRecordRef("sprk_workassignment", id));
        Bind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})",
            "uac-r2's pair rule: a work assignment's pair counts only when its typed lookup repeats it");
        Bind(fields, "sprk_RegardingRecordType@odata.bind").Should().Be($"/sprk_recordtype_refs({DcRecordTypeMatter:D})");
        Bind(fields, "sprk_AssignedToInternal@odata.bind").Should().Be($"/contacts({DcAssignee:D})");
        fields["sprk_name"]?.ToString().Should().Be("Review the revised budget", "the first line of the description");
        Owner(fields).Should().Be(Directory.ChildTeam);
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task DecisionAddTodo_UnderASecureMatterTheCallerCannotAppendTo_IsRefused_AndNothingIsCreated()
    {
        _user.NoAppendTo.Add(SecureMatter);
        var executor = new AddTodoExecutor(GrantingProbe(), new DecisionRouteCores(), EntitySets());

        var outcome = await executor.ExecuteAsync(DecisionRequest(SecureMatter, new()
        {
            ["title"] = "Chase the revision",
            ["assignee"] = DcAssignee.ToString("D"),
            ["dueDate"] = "2026-10-20",
        }), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Refused, "the create core's own AppendTo check, as the caller");
        outcome.Written.Should().BeEmpty();
        _appCreates.Should().BeEmpty();
    }
}
