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
            Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.RouteRecordCreation.Over(
                new RecordOwnershipResolver(
                    SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
                    NullLogger<RecordOwnershipResolver>.Instance),
                AppCreatesIntoTheWorld().Object,
                new StampWorld().Restamper,
                scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(),
                _gate,
                // No AssignedAccessMaterializer in this composition: its inline step logs and leaves the record to the job.
                new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>()),
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
}
