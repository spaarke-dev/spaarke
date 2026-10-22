using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// field-mapping-server-write-path-r1 task 018c — the Office quick-create's filing and isolation behaviours that the deleted
/// Office creation core's tests pinned (<c>SecureRootInheritanceWriterTests</c>, task 158 and #2191), re-pointed to the real
/// route over the unified create: an isolated project that cannot be shared and cannot be removed again, a maker on the secure
/// matter's No Access list, a mapping source that cannot be read, and a project created from a project.
/// </summary>
public partial class SecureRootCreateRouteTests
{
    // ── The isolated project not removable (owner round 31 item 2; verifier items 2 and 7) ──────────────────────────

    /// <summary>
    /// Owner round 31 item 2: the maker could not be shared AND the project could not be removed again — 201, and the warning
    /// says exactly that (never "shared to you"); the project stays secure and team-owned, and the job shares it later.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_WhenTheMakerCannotBeSharedNorTheProjectRemoved_Is201_AndWarnsSo()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.FailShareForPrincipal = Creator;
        _fixture.ChildWorld.DeletesFail = true;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var project = await CreatedIdOf(response);
        var warnings = await WarningsOf(response);
        warnings.Should().ContainSingle(w => w.Contains("could not be shared to you and could not be removed")
            && w.Contains("shared to you automatically once that step succeeds"), "a fault: the job's retry does share it");
        warnings.Should().NotContain(w => w.Contains("as a secure record shared to you"));
        _fixture.IsSecureOf(project).Should().BeTrue();
        _fixture.OwningTeamOf(project).Should().Be(SecureTeam);
        _fixture.ShareMaskOf(project, Creator).Should().Be(0);
    }

    /// <summary>
    /// Verifier item 2: the compensation delete ANSWERS success but the project survives — the read-back finds it, so the
    /// create is 201 with the warning that it exists and could not be shared or removed, never refused as removed.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_WhenTheCompensationDeleteAnswersSuccessButTheProjectSurvives_Is201_NeverRefusedAsRemoved()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.FailShareForPrincipal = Creator;
        _fixture.ChildWorld.DeletesIgnored = true;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the project exists: the delete did not remove it");
        var project = await CreatedIdOf(response);
        (await WarningsOf(response)).Should().ContainSingle(w => w.Contains("could not be shared to you and could not be removed"));
        _fixture.ChildWorld.Deletes.Should().ContainSingle(d => d.Id == project);
        _fixture.ChildWorld.Has("sprk_project", project).Should().BeTrue();
        _fixture.IsSecureOf(project).Should().BeTrue();
    }

    /// <summary>
    /// Verifier item 7: a maker walled off after the plan, and the project not removable — 201, and no self-heal is promised
    /// (every job run refuses the walled maker again).
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_WhenTheMakerIsWalledOffAfterThePlan_AndTheProjectCannotBeRemoved_PromisesNoSelfHeal()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.ChildWorld.DeletesFail = true;
        _fixture.AfterOfficeCreate = _ => _fixture.NoAccessList.DenySystemUserOnRecord(Creator, matter);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var warnings = await WarningsOf(response);
        warnings.Should().ContainSingle(w => w.Contains("could not be shared to you and could not be removed")
            && w.Contains("will not be shared to you automatically"));
        warnings.Should().NotContain(w => w.Contains("automatically once"));
    }

    // ── The maker on the secure matter's No Access list (owner round 31 item 1) ─────────────────────────────────────

    /// <summary>Owner round 31 item 1: the maker is on the secure matter's No Access list — refused, nothing written.</summary>
    [Fact]
    public async Task OfficeQuickCreate_WhenTheMakerIsOnTheSecureMattersNoAccessList_IsRefused_AndNothingIsWritten()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, matter);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        var (code, detail) = await RefusalOf(response);
        code.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        detail.Should().Contain("the No Access list of the secure matter it would be filed under");
        _fixture.OfficeCreateBodies.Should().BeEmpty();
        _fixture.OfficeCreates.Should().BeEmpty();
    }

    // ── #2191 K2: filed under the record it is created from, whatever the mapping could read ──────────────────────────

    /// <summary>
    /// #2191 K2, must fire: the mapping source cannot be read — by the caller (a 429) or app-only (a timeout) — so the mapping
    /// copies nothing and warns; the project made from a SECURE matter is still filed under it and created INTO isolation,
    /// never unfiled and ordinary.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OfficeQuickCreate_FromASecureMatter_WhenTheSourceCannotBeRead_IsStillFiledUnderIt_AndIsolated(bool callerReadFails)
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        _fixture.MapProjectsUnder(matter, matterType);
        if (callerReadFails)
            _fixture.SourceCallerReadFailStatus = 429;
        else
            _fixture.SourceAppReadFails = true;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await WarningsOf(response)).Should().Contain(MappingSourceReadProbe.SourceUnavailableWarning, "the mapping source was unavailable");
        var project = ShouldBeFiledUnderTheDerivedPair(matter, matterType);
        _fixture.OfficeCreates.Single().GetAttributeValue<bool>("sprk_issecure").Should().BeTrue("created INTO isolation");
        _fixture.OfficeCreates.Single().GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("ownerid").Id.Should().Be(SecureTeam);
        ShouldBeSecure(project, "filed under the secure matter it was created from");
    }

    /// <summary>
    /// #2191 K2, must not fire: a project made from an ORDINARY matter is an ordinary project — filed under the matter, owned
    /// by the record-first team, unflagged — also when the mapping source could not be read.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfficeQuickCreate_FromAnOrdinaryMatter_IsAnOrdinaryProject_FiledUnderIt(bool callerReadFails)
    {
        var matter = Guid.NewGuid();
        _fixture.SeedMatter(matter, isSecure: false);
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        _fixture.MapProjectsUnder(matter, matterType);
        if (callerReadFails)
            _fixture.SourceCallerReadFailStatus = 503;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var project = ShouldBeFiledUnderTheDerivedPair(matter, matterType);
        _fixture.OfficeCreates.Single().GetAttributeValue<bool>("sprk_issecure").Should().BeFalse("an ordinary matter passes no isolation on");
        _fixture.OwningTeamOf(project).Should().Be(RecordOwnershipResolverDouble.DefaultTeamId, "the record-first owner, never the secure team");
        _fixture.SharesOn(project).Should().BeEmpty();
        if (callerReadFails)
            (await WarningsOf(response)).Should().Contain(MappingSourceReadProbe.SourceUnavailableWarning,
                "the case must prove the source read really failed, or it pins nothing (018c review F3)");
    }

    /// <summary>#2191 K2 review F3: a project created from a SECURE PROJECT is filed under that project and created into isolation.</summary>
    [Fact]
    public async Task OfficeQuickCreate_FromASecureProject_IsFiledUnderIt_AndIsolated()
    {
        var parent = Guid.NewGuid();
        SecureProject(_fixture, parent);
        var projectType = RecordTypeRef(_fixture, "sprk_project");
        _fixture.CatalogMattersAs(projectType);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, ProjectBody(parent));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var project = ShouldBeFiledUnderTheDerivedPair(parent, projectType);
        _fixture.OfficeCreates.Single().GetAttributeValue<bool>("sprk_issecure").Should().BeTrue();
        ShouldBeSecure(project, "filed under the secure project it was created from");
    }

    // ── UAC-r2 re-check of #2191: the caller's AppendTo on an ORDINARY project the project is filed under ───────────────

    /// <summary>
    /// UAC-r2 re-check, must fire: an ORDINARY project source the caller cannot AppendTo — the app-only create would file the
    /// project under it regardless; refused 403 <c>caller_cannot_file_under_parent</c>, nothing written. (The ordinary matter
    /// source is <see cref="OfficeQuickCreate_UnderASecureMatterTheCallerCannotAppendTo_IsRefusedExactlyAsAnOrdinaryOne_AndNothingIsWritten"/>.)
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_FromAnOrdinaryProject_WhenTheCallerCannotAppendToIt_IsRefused_AndNothingIsWritten()
    {
        var parent = OrdinaryParentProject();
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_project"));
        _fixture.CallerNoAppendTo.Add(parent);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, ProjectBody(parent));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeOf(response)).Should().Be(OfficeService.CallerCannotFileUnderParentCode);
        _fixture.OfficeCreateBodies.Should().BeEmpty();
    }

    /// <summary>
    /// UAC-r2 re-check, must NOT fire: an ordinary project the caller holds AppendTo on — created, ordinary, filed under it;
    /// the rights were asked as the caller for that very record.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_FromAnOrdinaryProject_WhenTheCallerCanAppendToIt_IsCreated_FiledUnderIt()
    {
        var parent = OrdinaryParentProject();
        var projectType = RecordTypeRef(_fixture, "sprk_project");
        _fixture.CatalogMattersAs(projectType);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, ProjectBody(parent));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        ShouldBeFiledUnderTheDerivedPair(parent, projectType);
        _fixture.OfficeCreates.Single().GetAttributeValue<bool>("sprk_issecure").Should().BeFalse();
        _fixture.CallerDataverse.Requests.Should().Contain(
            r => r.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                 && r.Contains($"sprk_projects({parent:D})", StringComparison.Ordinal),
            "AppendTo on the project it is filed under is asked through the caller's own Dataverse");
    }

    private static object ProjectBody(Guid parent) =>
        new { name = "Lease review", sourceEntityType = "sprk_project", sourceRecordId = parent };

    private Guid OrdinaryParentProject()
    {
        var id = Guid.NewGuid();
        _fixture.SeedProject(id, owningTeamId: SecureChildShareWorld.GeneralTeam, isSecure: false);
        return id;
    }
}
