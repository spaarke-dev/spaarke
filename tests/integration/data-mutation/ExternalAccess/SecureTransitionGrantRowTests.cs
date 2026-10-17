using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 099b review round 2 (F1, Make Secure): a record's external-access grant rows follow it across
/// the isolation boundary — in every state since review round 3 (F1): an inactive grant names the record and the contact too.
/// Since 099b a grant row is owned by its root's team (D-11): created on an ordinary record it is that business unit's default
/// team's, readable by every user of the unit (Deep) — the record's name, the contact and the level. Before this fix Make
/// Secure left it there; Unsecure left a secure record's grant on the memberless Secure team.
/// </summary>
/// <remarks>
/// Driven through the REAL <c>/provision-project</c> and <c>/unsecure-project</c> endpoints, the REAL
/// <see cref="SecureChildReconciler"/> (the one pass every secure transition runs) and the REAL ownership resolver over one
/// in-memory Dataverse (<see cref="SecureChildShareWorld"/>). The decoy — a grant of another record — is never written. KEEP
/// path: <c>tests/integration/data-mutation/**</c>.
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

    private sealed record Grants(Guid TeamOwned, Guid AppOwned, Guid Inactive, Guid OtherRecordsDecoy)
    {
        public IEnumerable<Guid> Moving() => new[] { TeamOwned, AppOwned, Inactive };
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
    /// application user (pre-099b), one INACTIVE on <paramref name="owner"/> (it moves too, review round 3 F1), and the decoy:
    /// an active grant of another record.
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
        world.Add(GrantTable, grants.Inactive, ("owningteam", new EntityReference("team", owner)), root,
            ("statecode", new OptionSetValue(1)));

        var otherRecord = Guid.NewGuid();
        world.OrdinaryRoot(rootTable, otherRecord);
        world.Add(GrantTable, grants.OtherRecordsDecoy, ("owningteam", new EntityReference("team", GeneralTeam)),
            (rootTable, new EntityReference(rootTable, otherRecord)), active);
        return grants;
    }

    private DataversePrincipalRef? OwnerOf(Guid grant) => _fixture.ChildWorld.OwnerOf(GrantTable, grant);

    private void AssertDecoyUntouched(Grants grants) =>
        _fixture.ChildWorld.OwnerWrites.Should().NotContain(w => w.Id == grants.OtherRecordsDecoy, "another record's grant is not moved");

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
    public async Task MakeSecure_ReownsTheRecordsGrantRows_InEveryState_ToSecureRecordOwners(string rootTable)
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

        OwnerOf(grants.OtherRecordsDecoy).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        AssertDecoyUntouched(grants);
        _fixture.OwnerPrivileges.TeamQuestions.Should().OnlyContain(q => q.TeamId == SecureTeam && q.Table == GrantTable,
            "the new owner team is asked whether it may own a grant row before each assignment");

        var children = (await JsonOf(response)).GetProperty("children");
        children.GetProperty("status").GetString().Should().Be("Completed");
        ReownedIn(children, GrantTable).Should().Be(3, "the active grants and the inactive one");
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
        // Review round 3 (K2): the cause is named, and a repeat call is not promised to complete it before the role is fixed.
        problem.GetProperty(SecureChildPassSummary.ChildrenBlockedByKey).GetString()
            .Should().Be(ExternalGrantLifecycle.GrantOwnerPrivilegeMissingReasonCode);
        var detail = problem.GetProperty("detail").GetString();
        detail.Should().Contain("until an administrator").And.Contain("does not complete them")
            .And.NotContain("completes them regardless", "the reconciliation cannot complete them either")
            .And.NotContain(SecureTeam.ToString(), "the detail names no team id");
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
        AssertDecoyUntouched(grants);
    }

    [Fact]
    public async Task MakeSecure_WhenAGrantRowFailsForAnotherReason_IsChildrenIncomplete_AndStillPromisesTheRetry()
    {
        // Twin of the privilege case: the team may own the row, but its assignment fails (a transient write fault). A repeat
        // call does complete that, so the detail keeps its promise and names no blocking cause.
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        var grants = SeedGrants("sprk_project", rootId, owner: GeneralTeam);
        _fixture.ChildWorld.RefusingOwnerWritesOf(grants.TeamOwned);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonChildrenIncomplete);
        problem.TryGetProperty(SecureChildPassSummary.ChildrenBlockedByKey, out _).Should().BeFalse();
        problem.GetProperty("detail").GetString().Should().Contain("Calling provisioning again (the same caller may) completes them");
    }

    // ── Review round 5: a refused assignment is classified fresh; one privilege read per team per pass ──────────────

    private static HttpRequestException Forbidden() =>
        new("Simulated Dataverse 403 on the owner write.", null, HttpStatusCode.Forbidden);

    [Fact]
    public async Task MakeSecure_WhenARememberedYesLetsTheAssignThrough_AndDataverseRefusesItOnPrivilege_NamesTheOwnerPrivilege_AndDropsTheYes()
    {
        // K2: the team's YES was remembered before an administrator removed the privilege, so the pre-check passes and the
        // assignment reaches Dataverse, which refuses it (a bare 403). The refusal is confirmed fresh: the detail names the
        // owner privilege (childrenBlockedBy) and the stale YES is dropped.
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        var grants = SeedGrants("sprk_project", rootId, owner: GeneralTeam);
        _fixture.OwnerPrivileges = TeamPrivilegeCheckerDouble.Lacking(SecureTeam);
        _fixture.OwnerPrivileges.RememberedYes.Add(SecureTeam);
        foreach (var grant in grants.Moving())
            _fixture.ChildWorld.RefusingOwnerWritesOf(grant, Forbidden());

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonChildrenIncomplete);
        problem.GetProperty(SecureChildPassSummary.ChildrenBlockedByKey).GetString()
            .Should().Be(ExternalGrantLifecycle.GrantOwnerPrivilegeMissingReasonCode);
        _fixture.OwnerPrivileges.TeamQuestions.Should().Contain(q => q.TeamId == SecureTeam && q.Fresh,
            "the refused assignment is confirmed past the cache");
        _fixture.OwnerPrivileges.RememberedYes.Should().NotContain(SecureTeam, "the stale YES is evicted");
        _fixture.ChildWorld.OwnerWrites.Where(w => w.Table == GrantTable).Should().ContainSingle(
            "once the pass knows the team may not own a grant, it attempts no further assignment to it");
        foreach (var grant in grants.Moving())
            OwnerOf(grant).Should().NotBe(DataversePrincipalRef.Team(SecureTeam));
    }

    [Fact]
    public async Task MakeSecure_WhenDataverseRefusesAGrantAssignWith403_ButTheTeamHoldsRead_IsTheGenericFailure()
    {
        // Twin: the 403 is not the owner privilege (the fresh check says the team holds Read), so the generic failure stands
        // and the retry is still promised.
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        var grants = SeedGrants("sprk_project", rootId, owner: GeneralTeam);
        _fixture.ChildWorld.RefusingOwnerWritesOf(grants.TeamOwned, Forbidden());

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonChildrenIncomplete);
        problem.TryGetProperty(SecureChildPassSummary.ChildrenBlockedByKey, out _).Should().BeFalse();
        problem.GetProperty("detail").GetString().Should().Contain("Calling provisioning again (the same caller may) completes them");
        _fixture.OwnerPrivileges.TeamQuestions.Should().Contain(q => q.TeamId == SecureTeam && q.Fresh);
        OwnerOf(grants.AppOwned).Should().Be(DataversePrincipalRef.Team(SecureTeam), "the other grants still move");
    }

    [Fact]
    public async Task MakeSecure_WhenDataverseRefusesAGrantAssignWith403_AndThePrivilegeCheckFaults_IsTheGenericFailure()
    {
        // Twin: the fresh check cannot be read — fail closed, the generic failure, no named cause.
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        var grants = SeedGrants("sprk_project", rootId, owner: GeneralTeam);
        _fixture.OwnerPrivileges = new TeamPrivilegeCheckerDouble { Fault = new HttpRequestException("Simulated Dataverse 503.") };
        _fixture.OwnerPrivileges.RememberedYes.Add(SecureTeam);
        _fixture.ChildWorld.RefusingOwnerWritesOf(grants.TeamOwned, Forbidden());

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(ProvisionProjectEndpoint.ReasonChildrenIncomplete);
        problem.TryGetProperty(SecureChildPassSummary.ChildrenBlockedByKey, out _).Should().BeFalse();
        // Review round 6 (K3): the generic failure must come from the FRESH read faulting, not from the remembered YES.
        _fixture.OwnerPrivileges.TeamQuestions.Should().Contain(q => q.TeamId == SecureTeam && q.Fresh);
        OwnerOf(grants.TeamOwned).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
    }

    [Fact]
    public async Task MakeSecure_WhenSecureRecordOwnersMayNotOwnAGrant_ReadsItsPrivilegesOnce_NotOncePerGrantRow()
    {
        // K perf: three grant rows to the one team; the pass asks once.
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        SeedGrants("sprk_project", rootId, owner: GeneralTeam);
        _fixture.OwnerPrivileges = TeamPrivilegeCheckerDouble.Lacking(SecureTeam);

        await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        _fixture.OwnerPrivileges.TeamQuestions.Should().ContainSingle().Which.Should().Be((SecureTeam, GrantTable, false));
    }

    [Fact]
    public async Task MakeSecure_WhenSecureRecordOwnersMayOwnAGrant_ReadsItsPrivilegesOnce_AndMovesEveryGrantRow()
    {
        // Twin: the YES is reused across the pass's grant rows too, and every one of them is still moved.
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        var grants = SeedGrants("sprk_project", rootId, owner: GeneralTeam);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(ProvisionRoute, new { projectId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.OwnerPrivileges.TeamQuestions.Should().ContainSingle();
        foreach (var grant in grants.Moving())
            OwnerOf(grant).Should().Be(DataversePrincipalRef.Team(SecureTeam));
    }

    // ── Unsecure ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_workassignment")]
    public async Task Unsecure_ReownsTheRecordsGrantRows_InEveryState_BackToTheBusinessUnitsTeam(string rootTable)
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

        AssertDecoyUntouched(grants);
        _fixture.IsSecureOf(rootId).Should().BeFalse();
    }

    /// <summary>A secure record (shared with its creator) whose grants are all on the Secure team, ready to unsecure.</summary>
    private Grants SeedSecureRecordWithGrants(Guid rootId)
    {
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        var grants = SeedGrants("sprk_project", rootId, owner: SecureTeam);
        _fixture.ChildWorld.MoveOwner(GrantTable, grants.AppOwned, DataversePrincipalRef.Team(SecureTeam));
        return grants;
    }

    [Fact]
    public async Task Unsecure_WhenTheBusinessUnitsTeamMayNotOwnAGrant_IsChildrenIncomplete_NamesTheCause_AndASecondCallCompletesIt()
    {
        // Review round 4 (round 3's K2 on this endpoint): the team the ordinary rule gives the grant may not own one. A repeat
        // call cannot complete that until an administrator adds the privilege, so the detail does not promise it.
        var rootId = Guid.NewGuid();
        var grants = SeedSecureRecordWithGrants(rootId);
        _fixture.OwnerPrivileges = TeamPrivilegeCheckerDouble.Lacking(GeneralTeam);
        var client = _fixture.CreateAuthenticatedClient();

        var first = await client.PostAsJsonAsync(UnsecureRoute, new { recordType = "project", recordId = rootId });

        first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(first);
        problem.GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete,
            "the reason code is unchanged");
        problem.GetProperty(SecureChildPassSummary.ChildrenBlockedByKey).GetString()
            .Should().Be(ExternalGrantLifecycle.GrantOwnerPrivilegeMissingReasonCode);
        var detail = problem.GetProperty("detail").GetString();
        detail.Should().Contain("until an administrator").And.Contain("does not complete it")
            .And.NotContain("Calling again completes it", "a repeat call does not complete it before the role is fixed")
            .And.NotContain(GeneralTeam.ToString(), "the detail names no team id");
        _fixture.IsSecureOf(rootId).Should().BeTrue("an incomplete child pass stops before the flag is cleared");
        foreach (var grant in grants.Moving())
            OwnerOf(grant).Should().Be(DataversePrincipalRef.Team(SecureTeam), "no grant is assigned to a team that may not own it");

        _fixture.OwnerPrivileges = new TeamPrivilegeCheckerDouble(); // the administrator added the privilege
        var second = await client.PostAsJsonAsync(UnsecureRoute, new { recordType = "project", recordId = rootId });

        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        foreach (var grant in grants.Moving())
            OwnerOf(grant).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        AssertDecoyUntouched(grants);
    }

    [Fact]
    public async Task Unsecure_WhenAGrantRowFailsForAnotherReason_IsChildrenIncomplete_AndStillPromisesTheRetry()
    {
        // Twin of the privilege case: the team may own the row, but its assignment fails (a transient write fault). A repeat
        // call does complete that, so the detail keeps its promise and names no blocking cause.
        var rootId = Guid.NewGuid();
        var grants = SeedSecureRecordWithGrants(rootId);
        _fixture.ChildWorld.RefusingOwnerWritesOf(grants.TeamOwned);

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(UnsecureRoute, new { recordType = "project", recordId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await JsonOf(response);
        problem.GetProperty("reasonCode").GetString().Should().Be(UnsecureProjectEndpoint.ReasonChildrenIncomplete);
        problem.TryGetProperty(SecureChildPassSummary.ChildrenBlockedByKey, out _).Should().BeFalse();
        problem.GetProperty("detail").GetString().Should().Contain("Calling again completes it")
            .And.NotContain("until an administrator");
    }

    // ── The grant row's owner comes from its ROOT lookup only (review round 3, K1) ──────────────────────────────────

    /// <summary>A grant of <paramref name="rootId"/> owned by <paramref name="owner"/> that also names an invoice.</summary>
    private Guid SeedGrantNamingAnInvoice(Guid rootId, Guid owner, Guid invoiceId)
    {
        var grant = Guid.NewGuid();
        _fixture.ChildWorld.Add(GrantTable, grant, ("owningteam", new EntityReference("team", owner)),
            ("sprk_project", new EntityReference("sprk_project", rootId)),
            ("sprk_invoice", new EntityReference("sprk_invoice", invoiceId)),
            ("statecode", new OptionSetValue(0)));
        return grant;
    }

    [Fact]
    public async Task Unsecure_DecidesAGrantRowFromItsRootOnly_NotFromAnIsolatedInvoiceItAlsoNames()
    {
        // The grant core creates a grant owned by its ROOT's team (ResolveGrantOwnerAsync names the root alone). The row's
        // sprk_invoice lookup is an ownership parent: read as one, an invoice still owned by the Secure team would hold the
        // grant on that team after the record is unsecured, a different owner than a create would give it.
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId, owningTeam: SecureTeam);
        _fixture.UseChildWorldForRoots();
        _fixture.SeedShare(rootId, DataversePrincipalRef.User(Creator), RecordShareLevels.CollaborateRights);
        var isolatedInvoice = Guid.NewGuid();
        _fixture.ChildWorld.SecureChild("sprk_invoice", isolatedInvoice);
        var grant = SeedGrantNamingAnInvoice(rootId, SecureTeam, isolatedInvoice);

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(UnsecureRoute, new { recordType = "project", recordId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        OwnerOf(grant).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "the grant follows its root, as a create on the ordinary record would own it");
        _fixture.ChildWorld.OwnerOf("sprk_invoice", isolatedInvoice).Should().Be(DataversePrincipalRef.Team(SecureTeam),
            "the invoice is another record's: this transition does not touch it");
    }

    [Fact]
    public async Task MakeSecure_DecidesAGrantRowFromItsRootOnly_AnOrdinaryInvoiceItAlsoNamesDoesNotHoldItBack()
    {
        // Twin: the invoice the grant names is owned by an ordinary team; the grant still follows its root into isolation.
        var rootId = Guid.NewGuid();
        SeedRoot("sprk_project", rootId);
        _fixture.UseChildWorldForRoots();
        var ordinaryInvoice = Guid.NewGuid();
        _fixture.ChildWorld.OrdinaryChild("sprk_invoice", ordinaryInvoice);
        var grant = SeedGrantNamingAnInvoice(rootId, GeneralTeam, ordinaryInvoice);

        var response = await _fixture.CreateAuthenticatedClient()
            .PostAsJsonAsync(ProvisionRoute, new { recordType = "project", recordId = rootId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        OwnerOf(grant).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        _fixture.ChildWorld.OwnerOf("sprk_invoice", ordinaryInvoice).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
    }
}
