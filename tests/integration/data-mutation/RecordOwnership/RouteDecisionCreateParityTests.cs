using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 017 (phase 2b): the child-records route and a decision's follow-on creates
/// (<see cref="AddTodoExecutor"/>, <see cref="AssignWorkExecutor"/>, through <see cref="DecisionRouteCores.CreateChildAsync"/>)
/// both reach the ONE server create (<see cref="RecordCreationService"/>), so the same record under the same parent gets the
/// same owner and the same I-1 stamp whichever asked; a refusal or a fault creates nothing and falls back to nothing; the
/// route's create publishes its membership event once; and (item 9) a service request created by the service gets what the
/// owned create (<see cref="OwnedChildWrite.CreateAsync"/>, the budget inquiry's create today) gives it.
/// </summary>
/// <remarks>
/// A partial of <see cref="SecureChildOwnershipAiToolTests"/> on purpose: the route and the decision cores run over the SAME
/// harness (the caller's scripted Dataverse, the REAL resolver over its directory, the app-only create it records), so a
/// parity is between two paths through one world, not between two worlds.
/// </remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid PtyAssignee = Guid.Parse("a0170000-0000-4000-8000-0000000000c1");
    private static readonly Guid PtyFirm = Guid.Parse("a0170000-0000-4000-8000-0000000000f1");

    private static readonly string[] I1StampBinds = ["sprk_RegardingMatter@odata.bind", "sprk_RegardingProject@odata.bind"];

    /// <summary>The owner, the creator person and the I-1 stamp the create wrote.</summary>
    private static (Guid Owner, string? Creator, string?[] Stamp) Placement(IReadOnlyDictionary<string, object?> fields) =>
        (Owner(fields), fields.TryGetValue("sprk_CreatedByPerson@odata.bind", out var creator) ? creator?.ToString() : null,
            I1StampBinds.Select(b => Bind(fields, b)).ToArray());

    // =====================================================================================
    // Item 2: the route and a decision's follow-on create — one owner, one I-1 stamp
    // =====================================================================================

    [Fact]
    public async Task ATodo_ThroughTheRoute_AndThroughADecisionsAddTodo_UnderTheSameMatter_GetsTheSameOwnerAndI1Stamp()
    {
        var route = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "Chase the revision",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        });
        Status(route).Should().Be(StatusCodes.Status201Created, Detail(route));

        var decision = await new AddTodoExecutor(GrantingProbe(), new DecisionRouteCores(), EntitySets())
            .ExecuteAsync(DecisionRequest(OrdinaryMatter, new()
            {
                ["title"] = "Chase the revision",
                ["assignee"] = PtyAssignee.ToString("D"),
                ["dueDate"] = "2026-10-20",
            }), CancellationToken.None);
        decision.Status.Should().Be(DecisionActionStatus.Done, decision.Detail);

        _appCreates.Should().HaveCount(2);
        _appCreates.Should().OnlyContain(c => c.Table == "sprk_todo");
        var (viaRoute, viaDecision) = (Placement(_appCreates[0].Fields), Placement(_appCreates[1].Fields));
        viaRoute.Owner.Should().Be(Directory.ChildTeam, "record-first: the matter's business-unit team");
        viaDecision.Should().BeEquivalentTo(viaRoute, "one create, whichever path asked");
        viaRoute.Stamp[0].Should().Be($"/sprk_matters({OrdinaryMatter:D})");
    }

    [Fact]
    public async Task AWorkAssignment_ThroughTheRoute_AndThroughADecisionsAssignWork_UnderTheSameMatter_GetsTheSameOwnerAndI1Stamp()
    {
        var route = await CreateChild("sprk_workassignment", new()
        {
            ["sprk_name"] = "Review the revised budget",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_AssignedToInternal@odata.bind"] = $"/contacts({PtyAssignee:D})",
        });
        Status(route).Should().Be(StatusCodes.Status201Created, Detail(route));

        var decision = await new AssignWorkExecutor(GrantingProbe(), new DecisionRouteCores(), EntitySets())
            .ExecuteAsync(DecisionRequest(OrdinaryMatter, new()
            {
                ["assignee"] = PtyAssignee.ToString("D"),
                ["description"] = "Review the revised budget",
            }), CancellationToken.None);
        decision.Status.Should().Be(DecisionActionStatus.Done, decision.Detail);

        _appCreates.Should().HaveCount(2);
        _appCreates.Should().OnlyContain(c => c.Table == "sprk_workassignment");
        var (viaRoute, viaDecision) = (Placement(_appCreates[0].Fields), Placement(_appCreates[1].Fields));
        viaRoute.Owner.Should().Be(Directory.ChildTeam);
        viaDecision.Should().BeEquivalentTo(viaRoute, "one create, whichever path asked");
        viaRoute.Stamp[0].Should().Be($"/sprk_matters({OrdinaryMatter:D})");
    }

    // =====================================================================================
    // Negative: no AppendTo, a refusal, a fault — nothing created, no fallback
    // =====================================================================================

    [Fact]
    public async Task ACallerWithoutAppendToOnTheMatter_GetsTheRoutesUniformNotFound_OnBothPaths_AndNothingIsCreated()
    {
        var missing = Guid.Parse("a0170000-0000-4000-8000-0000000000ff");
        _user.NoAppendTo.Add(OrdinaryMatter);
        _user.Missing.Add(missing);

        var denied = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "x",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        });
        var absent = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "x",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({missing:D})",
        });
        var decision = await new AddTodoExecutor(GrantingProbe(), new DecisionRouteCores(), EntitySets())
            .ExecuteAsync(DecisionRequest(OrdinaryMatter, new()
            {
                ["title"] = "x",
                ["assignee"] = PtyAssignee.ToString("D"),
                ["dueDate"] = "2026-10-20",
            }), CancellationToken.None);

        Status(denied).Should().Be(StatusCodes.Status404NotFound);
        ReasonCode(denied).Should().Be(ChildRecordEndpoints.NotFoundCode, "the route's own code, not record_creation.not_found");
        (await WireOf(denied)).Should().Be(await WireOf(absent), "NFR-07: denied and missing answer byte-identically");
        decision.Status.Should().Be(DecisionActionStatus.Refused);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty("no create as the caller either");
    }

    [Fact]
    public async Task WhenTheServiceRefuses_TheRouteCreatesNothing_AndFallsBackToNothing()
    {
        var refused = await CreateChild("sprk_workassignment", new()
        {
            ["sprk_name"] = "x",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_containerid"] = "b!container",
        });

        Status(refused).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(refused).Should().Be(ChildRecordEndpoints.DeniedCode, "#1608: a root's server-owned column, as before");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheServiceThrows_TheRouteCreatesNothing_AndFallsBackToNothing()
    {
        var faulting = new Mock<IRecordOwnershipResolver>(MockBehavior.Strict);
        faulting.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the directory is down"));
        faulting.Setup(r => r.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the directory is down"));

        IResult? result = null;
        Exception? thrown = null;
        try
        {
            result = await ChildRecordEndpoints.CreateAsync(
                "sprk_todo",
                JsonSerializer.SerializeToElement(new Dictionary<string, object?>
                {
                    ["sprk_name"] = "x",
                    ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
                }),
                _user,
                RouteRecordCreation.Over(faulting.Object, _appOnly.Object, Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure()),
                HttpContextOfCaller(), NullLogger<Program>.Instance, CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        (thrown is not null || Status(result!) >= StatusCodes.Status400BadRequest).Should().BeTrue(
            "a fault is never a 201: it surfaces (the host's 500) or is a refusal");
        _appCreates.Should().BeEmpty("no row without an owner");
        _user.Posts.Should().BeEmpty("and no create as the caller in its place");
    }

    // =====================================================================================
    // K-4: the route's create publishes its membership event exactly once
    // =====================================================================================

    [Theory]
    [InlineData("sprk_event", "sprk_eventname", "sprk_RegardingMatter@odata.bind")]
    [InlineData("sprk_document", "sprk_documentname", "sprk_Matter@odata.bind")]
    public async Task ARouteCreateOfAMembershipTable_PublishesItsOwnerEvent_ExactlyOnce(string table, string nameColumn, string matterBind)
    {
        var result = await CreateChild(table, new()
        {
            [nameColumn] = "Hearing",
            [matterBind] = $"/sprk_matters({OrdinaryMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _routeMembership.Verify(m => m.PublishAsync(It.IsAny<MembershipChangedEvent>(), It.IsAny<CancellationToken>()), Times.Once(),
            "the service publishes once; the route publishes nothing of its own");
    }

    /// <summary>Must not fire: a table that publishes nothing (a to-do) publishes nothing through the route.</summary>
    [Fact]
    public async Task ARouteCreateOfANonMembershipTable_PublishesNothing()
    {
        var result = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "x",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _routeMembership.Verify(m => m.PublishAsync(It.IsAny<MembershipChangedEvent>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    // =====================================================================================
    // Item 9: the budget inquiry's service request — the service gives what the owned create gives
    // =====================================================================================

    /// <summary>The row the budget inquiry builds (BudgetInquiryExecutor.BuildItem), for <paramref name="matter"/>.</summary>
    private static DataverseWriteItemMapper.MappedItem InquiryServiceRequest(Guid matter)
    {
        var body = BudgetInquiryExecutor.BuildBody(
            new BudgetInquiryRequest(matter, ["firm@example.com"], "Budget question", "Please explain.", Confirmed: true, PtyFirm),
            DcRecordTypeMatter);
        return new DataverseWriteItemMapper.MappedItem(body,
        [
            "sprk_name", "sprk_direction", "sprk_regardingmatter", "sprk_regardingrecordid", "sprk_regardingrecordtype",
            "sprk_regardingrecordtypelogicalname", "sprk_regardingorganization",
        ])
        {
            Lookups =
            [
                new("sprk_regardingmatter", "sprk_matter", "sprk_matters", matter),
                new("sprk_regardingrecordtype", "sprk_recordtype_ref", "sprk_recordtype_refs", DcRecordTypeMatter),
                new("sprk_regardingorganization", "sprk_organization", "sprk_organizations", PtyFirm),
            ],
        };
    }

    private void InquiryCallerMayCreate()
    {
        _user.RecordTypes[DcRecordTypeMatter] = "sprk_matter";
        _user.Held.UnionWith(["prvCreatesprk_servicerequest", "prvAppendsprk_servicerequest"]);
    }

    [Fact]
    public async Task AServiceRequestUnderAMatter_ThroughTheService_GetsTheOwnerCreatorAndStampTheOwnedCreateGives()
    {
        InquiryCallerMayCreate();

        var owned = await OwnedChildWrite.CreateAsync(
            _user, _world.Resolver(), _appOnly.Object, BudgetInquiryExecutor.Table, InquiryServiceRequest(OrdinaryMatter),
            serverSetLookupColumns: null, CallerOid, CancellationToken.None);
        var unified = await RouteRecordCreation
            .Over(_world.Resolver(), _appOnly.Object, Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure())
            .CreateAsync(
                new RecordCreationRequest(BudgetInquiryExecutor.Table, new RecordCreationPayload.Mapped(InquiryServiceRequest(OrdinaryMatter))),
                RecordCreationPrincipal.ForCaller(_user, CallerOid), CancellationToken.None);

        owned.CreatedId.Should().NotBeNull(owned.Denied ?? owned.OwnerRefusal?.Reason ?? owned.SecureFilingRefused);
        unified.Succeeded.Should().BeTrue(unified.Refusal?.Detail);
        _appCreates.Should().HaveCount(2).And.OnlyContain(c => c.Table == "sprk_servicerequest");
        var (viaOwned, viaService) = (Placement(_appCreates[0].Fields), Placement(_appCreates[1].Fields));
        viaOwned.Owner.Should().Be(Directory.ChildTeam, "the regarding matter's business-unit team");
        viaService.Should().BeEquivalentTo(viaOwned, "item 9: the registry entry reproduces the owned create");
        Bind(_appCreates[1].Fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})",
            "the I-1 stamp the row carries");
        Bind(_appCreates[1].Fields, "sprk_RegardingOrganization@odata.bind").Should().Be($"/sprk_organizations({PtyFirm:D})");
    }

    [Fact]
    public async Task AServiceRequestUnderASecureMatter_IsRefusedOnBothPaths_AndNothingIsCreated()
    {
        InquiryCallerMayCreate();

        var owned = await OwnedChildWrite.CreateAsync(
            _user, _world.Resolver(), _appOnly.Object, BudgetInquiryExecutor.Table, InquiryServiceRequest(SecureMatter),
            serverSetLookupColumns: null, CallerOid, CancellationToken.None);
        var unified = await RouteRecordCreation
            .Over(_world.Resolver(), _appOnly.Object, Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure())
            .CreateAsync(
                new RecordCreationRequest(BudgetInquiryExecutor.Table, new RecordCreationPayload.Mapped(InquiryServiceRequest(SecureMatter))),
                RecordCreationPrincipal.ForCaller(_user, CallerOid), CancellationToken.None);

        owned.CreatedId.Should().BeNull();
        owned.SecureFilingRefused.Should().NotBeNull("a service request is no reparentable child");
        unified.Succeeded.Should().BeFalse();
        unified.Refusal!.Kind.Should().NotBe(RecordCreationRefusalKind.Invalid, "the table is registered (not table_unsupported)");
        _appCreates.Should().BeEmpty();
    }
}
