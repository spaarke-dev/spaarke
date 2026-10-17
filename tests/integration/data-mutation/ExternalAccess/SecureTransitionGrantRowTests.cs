using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 099b review round 2 (F1, Make Secure): a record's ACTIVE external-access grant rows follow it
/// across the isolation boundary. Since 099b a grant row is owned by its root's team (D-11): created on an ordinary record it is
/// that business unit's default team's, readable by every user of the unit (Deep) — the record's name, the contact and the
/// level. Before this fix Make Secure left it there; Unsecure left a secure record's grant on the memberless Secure team.
/// </summary>
/// <remarks>
/// Driven through the REAL <c>/provision-project</c> and <c>/unsecure-project</c> endpoints, the REAL
/// <see cref="SecureChildReconciler"/> (the one pass every secure transition runs) and the REAL ownership resolver over one
/// in-memory Dataverse (<see cref="SecureChildShareWorld"/>). Decoys — an INACTIVE grant of the record and an active grant of
/// another record — are never written. KEEP path: <c>tests/integration/data-mutation/**</c>.
/// </remarks>
public class SecureTransitionGrantRowTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string ProvisionRoute = "/api/v1/external-access/provision-project";
    private const string UnsecureRoute = "/api/v1/external-access/unsecure-project";
    private const string GrantTable = "sprk_externalrecordaccess";

    private static readonly Guid Creator = ProvisionProjectTestFixture.CallerSystemUserId;
    private static readonly Guid SecureTeam = ProvisionProjectTestFixture.SecureOwnerTeamId;
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;

    private readonly ProvisionProjectTestFixture _fixture;

    public SecureTransitionGrantRowTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private sealed record Grants(Guid TeamOwned, Guid AppOwned, Guid InactiveDecoy, Guid OtherRecordsDecoy)
    {
        public IEnumerable<Guid> Moving() => new[] { TeamOwned, AppOwned };
    }

    private void SeedRoot(string rootTable, Guid rootId, Guid? owningTeam = null)
    {
        switch (rootTable)
        {
            case "sprk_project": _fixture.SeedProject(rootId, owningTeamId: owningTeam); break;
            case "sprk_matter": _fixture.SeedMatter(rootId, owningTeamId: owningTeam); break;
            default: _fixture.SeedWorkAssignment(rootId, owningTeamId: owningTeam); break;
        }
    }

    private static string RecordType(string rootTable) => rootTable switch
    {
        "sprk_project" => "project",
        "sprk_matter" => "matter",
        _ => "workassignment",
    };

    /// <summary>
    /// The record's grants: one owned by <paramref name="owner"/> (the team 099b gave it), one still owned by the BFF
    /// application user (pre-099b), one INACTIVE (a decoy: only active grants move), and an active grant of another record.
    /// </summary>
    private Grants SeedGrants(string rootTable, Guid rootId, Guid owner)
    {
        var world = _fixture.ChildWorld;
        var grants = new Grants(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var root = (rootTable, (object)new EntityReference(rootTable, rootId));
        var active = ("statecode", (object)new OptionSetValue(0));

        world.Add(GrantTable, grants.TeamOwned, ("owningteam", new EntityReference("team", owner)), root, active);
        world.Add(GrantTable, grants.AppOwned,
            ("owninguser", new EntityReference("systemuser", SecureChildShareWorld.SomeUser)), root, active);
        world.Add(GrantTable, grants.InactiveDecoy, ("owningteam", new EntityReference("team", owner)), root,
            ("statecode", new OptionSetValue(1)));

        var otherRecord = Guid.NewGuid();
        world.OrdinaryRoot(rootTable, otherRecord);
        world.Add(GrantTable, grants.OtherRecordsDecoy, ("owningteam", new EntityReference("team", GeneralTeam)),
            (rootTable, new EntityReference(rootTable, otherRecord)), active);
        return grants;
    }

    private DataversePrincipalRef? OwnerOf(Guid grant) => _fixture.ChildWorld.OwnerOf(GrantTable, grant);

    private void AssertDecoysUntouched(Grants grants)
    {
        foreach (var decoy in new[] { grants.InactiveDecoy, grants.OtherRecordsDecoy })
            _fixture.ChildWorld.OwnerWrites.Should().NotContain(w => w.Id == decoy, "an inactive grant, or another record's, is not moved");
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static int ReownedIn(JsonElement children, string table) =>
        children.GetProperty("tables").EnumerateArray()
            .Single(t => t.GetProperty("table").GetString() == table)
            .GetProperty("reowned").GetInt32();

    // ── Make Secure ──────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_workassignment")]
    public async Task MakeSecure_ReownsTheRecordsActiveGrantRows_ToSecureRecordOwners(string rootTable)
    {
        var rootId = Guid.NewGuid();
        SeedRoot(rootTable, rootId);
        _fixture.UseChildWorldForRoots();
        var grants = SeedGrants(rootTable, rootId, owner: GeneralTeam);

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(ProvisionRoute, new { recordType = RecordType(rootTable), recordId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        foreach (var grant in grants.Moving())
        {
            OwnerOf(grant).Should().Be(DataversePrincipalRef.Team(SecureTeam),
                "a grant row of a secure record is owned by Secure Record Owners, never left readable by its old unit");
        }

        OwnerOf(grants.InactiveDecoy).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        OwnerOf(grants.OtherRecordsDecoy).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        AssertDecoysUntouched(grants);
        _fixture.OwnerPrivileges.TeamQuestions.Should().OnlyContain(q => q.TeamId == SecureTeam && q.Table == GrantTable,
            "the new owner team is asked whether it may own a grant row before each assignment");

        var children = (await JsonOf(response)).GetProperty("children");
        children.GetProperty("status").GetString().Should().Be("Completed");
        ReownedIn(children, GrantTable).Should().Be(2);
    }

    [Fact]
    public async Task MakeSecure_WhenSecureRecordOwnersMayNotOwnAGrant_IsChildrenIncomplete_WritesNoGrant_AndASecondCallCompletesIt()
    {
        // Before the Secure Record Owner role holds prvReadsprk_ExternalRecordAccess: the transition reports the grant it
        // could not move (children_incomplete, like any related record) and never attempts the write.
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        var grants = SeedGrants("sprk_project", rootId, owner: GeneralTeam);
        _fixture.OwnerPrivileges = TeamPrivilegeCheckerDouble.Lacking(SecureTeam);
        var client = _fixture.CreateAuthenticatedClient();

        var first = await client.PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(first);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonChildrenIncomplete);
        _fixture.OwningTeamOf(rootId).Should().Be(SecureTeam, "the record's own steps are not undone");
        foreach (var grant in grants.Moving())
        {
            _fixture.ChildWorld.OwnerWrites.Should().NotContain(w => w.Id == grant,
                "no assignment is attempted to a team that may not own a grant row");
        }

        OwnerOf(grants.TeamOwned).Should().Be(DataversePrincipalRef.Team(GeneralTeam));

        _fixture.OwnerPrivileges = new TeamPrivilegeCheckerDouble(); // the administrator added the privilege
        var second = await client.PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        foreach (var grant in grants.Moving())
            OwnerOf(grant).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        AssertDecoysUntouched(grants);
    }

    // ── Unsecure ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_workassignment")]
    public async Task Unsecure_ReownsTheRecordsActiveGrantRows_BackToTheBusinessUnitsTeam(string rootTable)
    {
        var rootId = Guid.NewGuid();
        SeedRoot(rootTable, rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        var grants = SeedGrants(rootTable, rootId, owner: SecureTeam);
        _fixture.ChildWorld.MoveOwner(GrantTable, grants.AppOwned, DataversePrincipalRef.Team(SecureTeam));

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(UnsecureRoute, new { recordType = RecordType(rootTable), recordId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        foreach (var grant in grants.Moving())
        {
            OwnerOf(grant).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
                "a grant row of an ordinary record is owned by its business unit's team, not the memberless Secure team");
        }

        OwnerOf(grants.InactiveDecoy).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        AssertDecoysUntouched(grants);
        _fixture.IsSecureOf(rootId).Should().BeFalse();
    }
}
