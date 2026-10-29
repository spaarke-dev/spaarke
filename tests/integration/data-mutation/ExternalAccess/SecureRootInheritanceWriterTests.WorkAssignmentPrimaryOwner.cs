using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 (owner decision 2026-10-29): a work assignment's PRIMARY parent is its matter, then its project,
/// invoice, event — its filing order (<c>SecureRootInheritance.TypedLookupsOf</c>, now <c>SecureChildLineage.RootLookupOrder</c>).
/// Driven through <c>POST /api/v1/child-records/sprk_workassignment</c> and the REAL <see cref="RecordOwnershipResolver"/>: a
/// work assignment filed under an ordinary matter in one business unit and an ordinary event in another is owned by the
/// MATTER's unit's default team, whatever order the body names them in (before the fix the event came first, by column
/// name). Secure-if-any does not depend on that order: a secure event still makes it secure.
/// </summary>
public partial class SecureRootInheritanceWriterTests
{
    /// <summary>Business unit A: the matter's. Its default Owner team, and the named team that owns the matter.</summary>
    private static readonly Guid UnitA = Guid.Parse("a1d0c0a0-0000-4000-8000-0000000000a0");
    private static readonly Guid UnitADefaultTeam = Guid.Parse("a1d0c0a0-0000-4000-8000-0000000000a1");
    private static readonly Guid UnitAMatterTeam = Guid.Parse("a1d0c0a0-0000-4000-8000-0000000000a2");

    /// <summary>Business unit B (≠ A): the event's. Its default Owner team, and the named team that owns the event.</summary>
    private static readonly Guid UnitB = Guid.Parse("a1d0c0a0-0000-4000-8000-0000000000b0");
    private static readonly Guid UnitBDefaultTeam = Guid.Parse("a1d0c0a0-0000-4000-8000-0000000000b1");
    private static readonly Guid UnitBEventTeam = Guid.Parse("a1d0c0a0-0000-4000-8000-0000000000b2");

    /// <summary>
    /// Two ordinary business units under the root, each with its default Owner team and a named Owner team that owns rows —
    /// so "the primary's unit's DEFAULT team" is told apart from "the primary's own owner".
    /// </summary>
    private void TwoBusinessUnits()
    {
        foreach (var (unit, name, defaultTeam, namedTeam) in new[]
                 {
                     (UnitA, "Unit A", UnitADefaultTeam, UnitAMatterTeam),
                     (UnitB, "Unit B", UnitBDefaultTeam, UnitBEventTeam),
                 })
        {
            World.Add("businessunit", unit, ("name", name),
                ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
            World.Team(defaultTeam, unit, name, isDefault: true, teamType: 0);
            World.Team(namedTeam, unit, name + " Owners", isDefault: false, teamType: 0);
        }
    }

    /// <summary>The route's create of a work assignment whose body names the EVENT before the matter.</summary>
    private async Task<IResult> CreateEventFirstWorkAssignmentThroughTheRoute(Guid @event, Guid matter)
    {
        var payload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["sprk_name"] = "Prepare the hearing bundle",
            ["sprk_RegardingEvent@odata.bind"] = $"/sprk_events({@event:D})",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter:D})",
        });

        using var scope = _fixture.Services.CreateScope();
        return await ChildRecordEndpoints.CreateAsync(
            "sprk_workassignment", payload,
            new SecureChildOwnershipAiToolTests.ScriptedUserClient(Creator),
            new RecordOwnershipResolver(
                SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
                NullLogger<RecordOwnershipResolver>.Instance),
            AppCreatesIntoTheWorld().Object,
            new StampWorld().Restamper,
            scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(),
            _gate,
            // No AssignedAccessMaterializer in this composition: its inline step logs and leaves the record to the job.
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new DefaultHttpContext(), NullLogger<Program>.Instance, CancellationToken.None);
    }

    [Fact]
    public async Task WorkAssignmentPrimaryOwner_UnderAnOrdinaryMatterAndAnOrdinaryEventInAnotherUnit_IsOwnedByTheMattersUnitsDefaultTeam()
    {
        TwoBusinessUnits();
        var (matter, @event) = (Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, owningTeamId: UnitAMatterTeam, isSecure: false);
        World.Add("sprk_event", @event, ("owningteam", new EntityReference("team", UnitBEventTeam)));

        var result = await CreateEventFirstWorkAssignmentThroughTheRoute(@event, matter);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        var (_, created, fields) = _writes.Should().ContainSingle().Subject;
        BoundId(fields, "ownerid@odata.bind").Should().Be(UnitADefaultTeam,
            "the matter is the primary parent (owner decision 2026-10-29): its unit's default team, never the event's unit's "
            + $"({UnitBDefaultTeam:D}) though the body names the event first");
        ShouldNotBeSecured(created, "filed under an ordinary matter and an ordinary event");
    }

    [Fact]
    public async Task WorkAssignmentPrimaryOwner_UnderAnOrdinaryMatterAndASecureEvent_IsSecure_NeverTheMattersUnit()
    {
        TwoBusinessUnits();
        var (matter, @event, secureMatter) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, owningTeamId: UnitAMatterTeam, isSecure: false);
        // A secure event: owned by the named team, filed under a secure matter (what the inheritance reads an event by).
        SecureMatter(_fixture, secureMatter);
        World.SecureChild("sprk_event", @event, ("sprk_regardingmatter", "sprk_matter", secureMatter));

        var result = await CreateEventFirstWorkAssignmentThroughTheRoute(@event, matter);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        var (_, created, fields) = _writes.Should().ContainSingle().Subject;
        BoundId(fields, "ownerid@odata.bind").Should().Be(SecureTeam,
            "secure-if-any: a secure event makes it secure though the ordinary matter is its primary — never unit A's "
            + $"default team ({UnitADefaultTeam:D})");
        fields["sprk_issecure"].Should().Be(true, "created INTO isolation: flagged in the create itself");
        ShouldBeSecure(created, "filed under an ordinary matter and a secure event");
    }

    // ── Owner decision 2026-10-29: a work assignment filed ONLY under an invoice or event belongs to that record's matter ──

    /// <summary>The route's create of a work assignment whose body is <paramref name="filing"/> (bind key → bind path).</summary>
    private async Task<IResult> CreateFiledWorkAssignmentThroughTheRoute(params (string Key, string Bind)[] filing)
    {
        var body = new Dictionary<string, object?> { ["sprk_name"] = "Prepare the hearing bundle" };
        foreach (var (key, bind) in filing)
            body[key] = bind;

        using var scope = _fixture.Services.CreateScope();
        return await ChildRecordEndpoints.CreateAsync(
            "sprk_workassignment", JsonSerializer.SerializeToElement(body),
            new SecureChildOwnershipAiToolTests.ScriptedUserClient(Creator),
            WorldResolver(),
            AppCreatesIntoTheWorld().Object,
            new StampWorld().Restamper,
            scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(),
            _gate,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new DefaultHttpContext(), NullLogger<Program>.Instance, CancellationToken.None);
    }

    private RecordOwnershipResolver WorldResolver() =>
        new(SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
            NullLogger<RecordOwnershipResolver>.Instance);

    /// <summary>
    /// The owner the un-secure cascade gives <paramref name="workAssignment"/> (<c>UnsecureFollowingParentsAsync</c>): the
    /// ownership rule over the records it is filed under directly, as the cascade's own read names them
    /// (<see cref="SecureParentsAnswer.DirectParents"/>), in that order.
    /// </summary>
    private async Task<Guid?> CascadeOwnerOf(Guid workAssignment)
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object;
        var ancestry = await SecureRootInheritance.ReadSecureParentsAsync(
            entities, NullLogger.Instance, SecureRootInheritance.WorkAssignment, workAssignment, CancellationToken.None,
            maxDepth: SecureRootInheritance.MaxFilingDepth);
        ancestry.IsKnown.Should().BeTrue(ancestry.Unverifiable);
        var owner = await WorldResolver().ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(ancestry.DirectParents.Select(p =>
                (RecordOwnershipParent?)new RecordOwnershipParent(p.Parent.Table, p.Parent.Id))),
            CancellationToken.None);
        return owner.OwningTeamId;
    }

    /// <summary>An ordinary matter in unit A and an ordinary <paramref name="table"/> in unit B that belongs to it.</summary>
    private (Guid Matter, Guid Intermediate) AnIntermediateInUnitBUnderAMatterInUnitA(string table, string matterColumn)
    {
        TwoBusinessUnits();
        var (matter, intermediate) = (Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, owningTeamId: UnitAMatterTeam, isSecure: false);
        World.Add(table, intermediate,
            ("owningteam", new EntityReference("team", UnitBEventTeam)),
            (matterColumn, new EntityReference("sprk_matter", matter)));
        return (matter, intermediate);
    }

    [Theory]
    [InlineData("sprk_event", "sprk_regardingmatter", "sprk_RegardingEvent@odata.bind", "sprk_events")]
    [InlineData("sprk_invoice", "sprk_matter", "sprk_RegardingInvoice@odata.bind", "sprk_invoices")]
    public async Task WorkAssignmentPrimaryOwner_UnderOnlyAnIntermediateInUnitB_WhoseMatterIsInUnitA_IsOwnedByUnitAsDefaultTeam_AsTheCascadeNames(
        string table, string matterColumn, string bindKey, string entitySet)
    {
        var (_, intermediate) = AnIntermediateInUnitBUnderAMatterInUnitA(table, matterColumn);

        var result = await CreateFiledWorkAssignmentThroughTheRoute((bindKey, $"/{entitySet}({intermediate:D})"));

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        var (_, created, fields) = _writes.Should().ContainSingle().Subject;
        BoundId(fields, "ownerid@odata.bind").Should().Be(UnitADefaultTeam,
            $"a work assignment belongs to its core record (owner decision 2026-10-29): the matter the {table} belongs to, in "
            + $"unit A — never the {table}'s own unit B ({UnitBDefaultTeam:D})");
        ShouldNotBeSecured(created, $"filed under an ordinary {table} of an ordinary matter");
        (await CascadeOwnerOf(created)).Should().Be(UnitADefaultTeam,
            "creation and the un-secure cascade name the same parents, so they land in the same business unit");
    }

    [Fact]
    public async Task WorkAssignmentPrimaryOwner_UnderOnlyAnEventOfASecureMatter_IsSecure()
    {
        TwoBusinessUnits();
        var (secureMatter, @event) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, secureMatter);
        World.Add("sprk_event", @event,
            ("owningteam", new EntityReference("team", UnitBEventTeam)),
            ("sprk_regardingmatter", new EntityReference("sprk_matter", secureMatter)));

        var result = await CreateFiledWorkAssignmentThroughTheRoute(("sprk_RegardingEvent@odata.bind", $"/sprk_events({@event:D})"));

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        var (_, created, fields) = _writes.Should().ContainSingle().Subject;
        BoundId(fields, "ownerid@odata.bind").Should().Be(SecureTeam, "filed through the event under a secure matter (#1630)");
        ShouldBeSecure(created, "filed under an event of a secure matter");
    }

    [Fact]
    public async Task WorkAssignmentPrimaryOwner_UnderOnlyAnEventThatCannotBeRead_IsRefused_AndNothingIsCreated()
    {
        var (_, @event) = AnIntermediateInUnitBUnderAMatterInUnitA("sprk_event", "sprk_regardingmatter");
        World.FailingRowReadsOf("sprk_event", @event);

        var result = await CreateFiledWorkAssignmentThroughTheRoute(("sprk_RegardingEvent@odata.bind", $"/sprk_events({@event:D})"));

        RouteStatus(result).Should().NotBe(StatusCodes.Status201Created, RouteDetail(result));
        _writes.Should().BeEmpty("what the event belongs to cannot be read: never \"filed under nothing\"");
    }

    [Fact]
    public async Task WorkAssignmentPrimaryOwner_UnderAMatterAndAnEventOfAnotherMatter_IsOwnedByItsOwnMattersUnit()
    {
        // The event belongs to a matter in unit B; the work assignment's own matter (unit A) is its core record.
        TwoBusinessUnits();
        var (matter, otherMatter, @event) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _fixture.SeedMatter(matter, owningTeamId: UnitAMatterTeam, isSecure: false);
        _fixture.SeedMatter(otherMatter, owningTeamId: UnitBEventTeam, isSecure: false);
        World.Add("sprk_event", @event,
            ("owningteam", new EntityReference("team", UnitBEventTeam)),
            ("sprk_regardingmatter", new EntityReference("sprk_matter", otherMatter)));

        var result = await CreateEventFirstWorkAssignmentThroughTheRoute(@event, matter);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        BoundId(_writes.Should().ContainSingle().Subject.Fields, "ownerid@odata.bind").Should().Be(UnitADefaultTeam,
            "a work assignment that holds a matter is unchanged: its matter first, never the event's matter");
    }

    [Fact]
    public void OwnerLookupsThroughIntermediates_AddNothing_ForAWorkAssignmentThatHoldsAMatterOrAProject()
    {
        var @event = KeyValuePair.Create("sprk_regardingevent", (RecordOwnershipParent?)new RecordOwnershipParent("sprk_event", Guid.NewGuid()));
        foreach (var (column, table) in new[] { ("sprk_regardingmatter", "sprk_matter"), ("sprk_regardingproject", "sprk_project") })
        {
            SecureRootInheritance.OwnerIntermediatesOf("sprk_workassignment",
                    [@event, KeyValuePair.Create(column, (RecordOwnershipParent?)new RecordOwnershipParent(table, Guid.NewGuid()))])
                .Should().BeEmpty($"a work assignment holding a {table} is filed under it directly; nothing is read");
        }

        SecureRootInheritance.OwnerIntermediatesOf("sprk_workassignment", [@event])
            .Should().Equal(("sprk_event", @event.Value!.RecordId));
        SecureRootInheritance.OwnerIntermediatesOf("sprk_project", [@event]).Should().BeEmpty("a project has no intermediate");
    }

    /// <summary>
    /// The shared helper's read contract (what the resolver's read path calls with its own reader): the intermediate's matter
    /// keyed under the work assignment's matter column; an intermediate that cannot be read is unverifiable; one that does
    /// not exist confers nothing.
    /// </summary>
    [Fact]
    public async Task OwnerLookupsThroughIntermediates_NameTheMatter_RefuseAnUnreadableIntermediate_AndIgnoreAMissingOne()
    {
        var (matter, @event) = AnIntermediateInUnitBUnderAMatterInUnitA("sprk_event", "sprk_regardingmatter");
        var entities = SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object;
        Task<SecureRootInheritance.OwnerFilingThroughIntermediates> Ask(Guid id) =>
            SecureRootInheritance.OwnerLookupsThroughIntermediatesAsync(entities, NullLogger.Instance, "sprk_workassignment",
                [KeyValuePair.Create("sprk_regardingevent", (RecordOwnershipParent?)new RecordOwnershipParent("sprk_event", id))],
                CancellationToken.None);

        var found = await Ask(@event);
        found.Unverifiable.Should().BeNull();
        var named = found.Lookups.Should().ContainSingle().Subject;
        named.Key.Should().Be("sprk_regardingmatter", "keyed under the column that files a work assignment under a matter");
        named.Value.Should().Be(new RecordOwnershipParent("sprk_matter", matter));

        var missing = await Ask(Guid.NewGuid());
        missing.Unverifiable.Should().BeNull("a record that does not exist confers nothing");
        missing.Lookups.Should().BeEmpty();

        World.FailingRowReadsOf("sprk_event", @event);
        var unreadable = await Ask(@event);
        unreadable.Unverifiable.Should().NotBeNull("an unreadable intermediate is unverifiable, never \"under nothing\"");
        unreadable.Lookups.Should().BeEmpty();
    }
}
