using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.Office.Errors;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// field-mapping-server-write-path-r1 task 018b-2 — the Office quick-create of a Project, now on the unified create
/// (<c>RecordCreationService</c>), under a SECURE matter and an ordinary one, over the real route, the real secure-root plan
/// and the real provisioning completion:
/// <list type="bullet">
/// <item>UAC-r2 condition (b)(ii): owned by the Secure Record Owners team (D-11 / round 6), never the matter's business unit's
/// team; itself secure (round 6); Access Permission inherited as a floor (round 87); its search index from its OWNER's
/// business unit — and the ordinary control.</item>
/// <item>Task 015 review: the isolated project needs no caller-BU team (step 7 is never asked), and a secure parent the
/// caller cannot AppendTo is refused exactly as an ordinary one is.</item>
/// <item>The source matter's secure flag EMPTY or UNREADABLE: 403 <c>record_owner_parent_undetermined</c>, nothing written
/// (the step-6 plan).</item>
/// </list>
/// </summary>
public partial class SecureRootCreateRouteTests
{
    /// <summary>The option value of Access Permission "Restricted" (<c>ExternalParticipationService.AccessPermissionRestricted</c>).</summary>
    private const int Restricted = 100000002;

    /// <summary>The secure matter's business unit's own default team — what record-first would answer for a child of it.</summary>
    private static readonly Guid MattersUnitDefaultTeam = ProvisionProjectTestFixture.SecureDefaultTeamId;

    private static readonly Guid SecureIndex = Guid.Parse("a018b200-0000-4000-8000-0000000000a1");
    private static readonly Guid OfficeIndex = Guid.Parse("a018b200-0000-4000-8000-0000000000a2");
    private static readonly Guid OrdinaryUnit = Guid.Parse("a018b200-0000-4000-8000-0000000000b1");
    private static readonly Guid OrdinaryUnitTeam = Guid.Parse("a018b200-0000-4000-8000-0000000000c1");
    private static readonly Guid OrdinaryIndex = Guid.Parse("a018b200-0000-4000-8000-0000000000a3");

    /// <summary>
    /// UAC-r2 condition (b)(ii): a Project quick-created from a SECURE (Restricted) matter is owned by the Secure Record Owners
    /// team — in the create itself, never the matter's unit's team and never asked of the owner resolver; it is secure in
    /// the create itself and after; it inherits the matter's Access Permission as a FLOOR (the access record the isolated
    /// completion writes: inherited from the matter, not its own); and its search index is its OWNER's unit's.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderASecureMatter_IsOwnedByTheSecureTeam_Secure_FlooredByTheMatter_AndIndexedByItsOwnersUnit()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.ChildWorld.Set("sprk_matter", matter, "sprk_accesspermission", new OptionSetValue(Restricted));
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.OwnerResolver.TeamId = MattersUnitDefaultTeam; // what step 7 would answer, were it asked
        _fixture.TeamBusinessUnits[SecureTeam] = ProvisionProjectTestFixture.SecureBuId;
        _fixture.BusinessUnitSearchDefaults[ProvisionProjectTestFixture.SecureBuId] = ("spaarke-secure-index", SecureIndex);
        _fixture.BusinessUnitSearchDefaults[OfficeUnitOf(MattersUnitDefaultTeam)] = ("spaarke-office-index", OfficeIndex);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var project = await CreatedIdOf(response);
        var body = _fixture.OfficeCreateBodies.Should().ContainSingle().Subject;

        // Owned by the Secure Record Owners team, in the create itself (D-11 / round 6).
        BoundTeam(body).Should().Be(SecureTeam, "the create names the Secure Record Owners team")
            .And.NotBe(MattersUnitDefaultTeam, "never the matter's business unit's team");
        _fixture.OwnerResolver.Requests.Should().BeEmpty("a root created into isolation is owned by the plan's team; step 7 is not asked");

        // Itself secure (round 6): flagged in the create, and secure once completed.
        Flag(body).Should().BeTrue("the create itself carries sprk_issecure");
        ShouldBeSecure(project, "quick-created from a secure matter");

        // Access Permission inherited as a floor (round 87): the matter's Restricted, recorded as inherited from it.
        _fixture.ChildWorld.ValueOf<OptionSetValue>("sprk_project", project, "sprk_accesspermission")!.Value
            .Should().Be(Restricted, "the project takes its secure matter's Access Permission");
        var (record, unreadable) = AccessInheritance.Parse(
            _fixture.ChildWorld.ValueOf<string>("sprk_project", project, AccessInheritance.Column));
        unreadable.Should().BeFalse();
        record.Should().NotBeNull("the isolated completion records where the project's access comes from");
        record!.Parents.Should().Equal([$"sprk_matter:{matter:D}"], "inherited from the matter it is filed under");
        record.FloorSecure.Should().BeTrue("the matter sets the Secure floor");
        record.FloorPermission.Should().Be(Restricted, "the matter sets the Access Permission floor");
        record.OwnSecure.Should().BeFalse("its Secure is inherited, not its own: it follows the matter out");
        record.OwnPermission.Should().BeNull("no Access Permission of its own");

        // The search index follows the OWNER: the Secure team's unit, read once.
        _fixture.TeamReads.Should().Equal([SecureTeam], "step 8 reads the owning team's unit");
        body["sprk_searchindexname"].Should().Be("spaarke-secure-index");
        body["sprk_AI_Search_Index@odata.bind"].Should().Be($"/sprk_aisearchindexes({SecureIndex:D})");
    }

    /// <summary>
    /// The ordinary control: under an ORDINARY matter the project is owned by the team record-first gives for the matter
    /// (the matter's unit's team, asked once with the matter as its parent), it is not secure, and its index is that unit's.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderAnOrdinaryMatter_IsOwnedByTheMattersUnitTeam_NotSecure_AndIndexedByThatUnit()
    {
        var matter = Guid.NewGuid();
        _fixture.SeedMatter(matter, isSecure: false);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.OwnerResolver.TeamId = OrdinaryUnitTeam;
        _fixture.TeamBusinessUnits[OrdinaryUnitTeam] = OrdinaryUnit;
        _fixture.BusinessUnitSearchDefaults[OrdinaryUnit] = ("spaarke-ordinary-index", OrdinaryIndex);
        _fixture.BusinessUnitSearchDefaults[ProvisionProjectTestFixture.SecureBuId] = ("spaarke-secure-index", SecureIndex);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = _fixture.OfficeCreateBodies.Should().ContainSingle().Subject;
        BoundTeam(body).Should().Be(OrdinaryUnitTeam, "record-first: the matter's unit's team")
            .And.NotBe(SecureTeam);
        _fixture.OwnerResolver.Requests.Should().ContainSingle("step 7 decides an ordinary project's owner")
            .Which.Parents.Should().Contain(new RecordOwnershipParent("sprk_matter", matter), "over the matter it is filed under");
        Flag(body).Should().BeFalse("an ordinary matter confers nothing");
        _fixture.IsSecureOf(_fixture.OfficeCreates.Single().Id).Should().BeFalse();
        _fixture.TeamReads.Should().Equal([OrdinaryUnitTeam]);
        body["sprk_searchindexname"].Should().Be("spaarke-ordinary-index");
        body["sprk_AI_Search_Index@odata.bind"].Should().Be($"/sprk_aisearchindexes({OrdinaryIndex:D})");
    }

    // ── Task 015 review: the isolated project's ordering ────────────────────────────────────────────────────────────

    /// <summary>
    /// A project under a SECURE matter is created with NO business-unit team the owner resolver could give (every step-7
    /// question would refuse): the isolated path is decided by the plan, so it needs none and asks none.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderASecureMatter_WithNoBusinessUnitTeamToBeHad_IsCreatedSecure_AndAsksNoOwner()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.OwnerResolver.TeamId = null; // no caller-BU (or any) team: step 7 would refuse

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        BoundTeam(_fixture.OfficeCreateBodies.Single()).Should().Be(SecureTeam);
        ShouldBeSecure(await CreatedIdOf(response), "the isolated path needs no business-unit team");
        _fixture.OwnerResolver.Requests.Should().BeEmpty("the plan's team owns it; step 7 runs only for an ordinary create");
    }

    /// <summary>
    /// Must fire (the control for the test above): with no team to be had, an ORDINARY matter's project IS refused — 403
    /// <c>OFFICE_022</c>, nothing written — so the secure create's success is the isolated ordering, not the setup.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderAnOrdinaryMatter_WithNoBusinessUnitTeamToBeHad_IsRefused_AndNothingIsWritten()
    {
        var matter = Guid.NewGuid();
        _fixture.SeedMatter(matter, isSecure: false);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.OwnerResolver.TeamId = null;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeOf(response)).Should().Be(OfficeErrorCodes.RecordOwnerUnresolved);
        _fixture.OwnerResolver.Requests.Should().ContainSingle();
        _fixture.OfficeCreateBodies.Should().BeEmpty();
    }

    /// <summary>
    /// 018b fix round 2 (F1-1): ownership is record-first, so a project filed under an ordinary matter whose owner the
    /// resolver cannot name for a reason that is NOT the caller's own unit (here the matter has no owning business unit) is
    /// 403 <c>OFFICE_022</c> with the service's own words — it must not tell the user to check THEIR business unit.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderAnOrdinaryMatterWhoseOwnerCannotBeNamed_IsOffice022_WithoutBlamingTheCallersBusinessUnit()
    {
        var matter = Guid.NewGuid();
        _fixture.SeedMatter(matter, isSecure: false);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.OwnerResolver.TeamId = null;
        _fixture.OwnerResolver.RefusalCode = RecordOwnerRefusal.ParentUnresolved;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (code, detail) = await RefusalOf(response);
        code.Should().Be(OfficeErrorCodes.RecordOwnerUnresolved);
        detail.Should().NotBeNullOrWhiteSpace().And.NotContainEquivalentOf("your business unit");
        _fixture.OfficeCreateBodies.Should().BeEmpty();
    }

    /// <summary>
    /// 018b fix round 2 (F1-1, filed-under case): the matter's business unit has no default owner team
    /// (<c>record_owner_no_default_team</c>, asked with the matter as the parent). The unit at fault is the MATTER's, not the
    /// caller's, so the 403 <c>OFFICE_022</c> detail is neutral and never tells the caller to check their own unit.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderAnOrdinaryMatterWhoseUnitHasNoOwnerTeam_IsOffice022_WithNeutralWording()
    {
        var matter = Guid.NewGuid();
        _fixture.SeedMatter(matter, isSecure: false);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.OwnerResolver.TeamId = null;
        _fixture.OwnerResolver.RefusalCode = RecordOwnerRefusal.NoDefaultOwnerTeam;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (code, detail) = await RefusalOf(response);
        code.Should().Be(OfficeErrorCodes.RecordOwnerUnresolved);
        detail.Should().Contain("the business unit of the record it is filed under has no default owner team")
            .And.NotContainEquivalentOf("your business unit");
        _fixture.OfficeCreateBodies.Should().BeEmpty();
    }

    /// <summary>
    /// A SECURE parent the caller cannot AppendTo is refused uniformly — even with no business-unit team scripted (the
    /// AppendTo is asked before any owner): the same 403, the same code and the same words as an ORDINARY parent the
    /// caller cannot AppendTo, so the response never tells a secure matter from an ordinary one. Nothing is written.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderASecureMatterTheCallerCannotAppendTo_IsRefusedExactlyAsAnOrdinaryOne_AndNothingIsWritten()
    {
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);
        var ordinary = Guid.NewGuid();
        _fixture.SeedMatter(ordinary, isSecure: false);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.OwnerResolver.TeamId = null;
        _fixture.CallerNoAppendTo.Add(secure);
        _fixture.CallerNoAppendTo.Add(ordinary);
        var client = _fixture.CreateAuthenticatedClient();

        var underSecure = await client.PostAsJsonAsync(OfficeRoute, Body(secure));
        var underOrdinary = await client.PostAsJsonAsync(OfficeRoute, Body(ordinary));

        underSecure.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        underOrdinary.StatusCode.Should().Be(underSecure.StatusCode);
        var (secureCode, secureDetail) = await RefusalOf(underSecure);
        var (ordinaryCode, ordinaryDetail) = await RefusalOf(underOrdinary);
        secureCode.Should().Be(OfficeService.CallerCannotFileUnderParentCode);
        ordinaryCode.Should().Be(secureCode, "one code for both");
        ordinaryDetail.Should().Be(secureDetail, "the same words: a secure matter is never told apart");
        secureDetail.Should().NotContain("secure").And.NotContain(secure.ToString("D"));
        _fixture.OfficeCreateBodies.Should().BeEmpty();
        _fixture.OwnerResolver.Requests.Should().BeEmpty("refused before any owner is asked");
    }

    // ── The source matter's secure flag: EMPTY and UNREADABLE (the step-6 plan) ───────────────────────────────────────

    /// <summary>
    /// The source matter's <c>sprk_issecure</c> is EMPTY: an empty flag is never read as "not secure", so whether the project
    /// must be isolated cannot be decided — 403 <c>record_owner_parent_undetermined</c>, the empty value named as the cause,
    /// and nothing written.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderAMatterWhoseSecureFlagIsEmpty_Is403Undetermined_NamingTheEmptyFlag_AndNothingIsWritten()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.ChildWorld.Set("sprk_matter", matter, "sprk_issecure", null);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (code, detail) = await RefusalOf(response);
        code.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        detail.Should().Contain("has no secure flag value", "the cause is the empty flag")
            .And.NotContain("could not be read", "an empty value is not a read failure");
        _fixture.OfficeCreateBodies.Should().BeEmpty();
        _fixture.OfficeCreates.Should().BeEmpty();
    }

    /// <summary>
    /// The source matter's flag cannot be READ (the read faults): the same 403 <c>record_owner_parent_undetermined</c>, the
    /// failed read named as the cause, and nothing written.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderAMatterWhoseSecureFlagCannotBeRead_Is403Undetermined_NamingTheFailedRead_AndNothingIsWritten()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.ChildWorld.FailingRowReadsOf("sprk_matter", matter);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (code, detail) = await RefusalOf(response);
        code.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        detail.Should().Contain("could not be read", "the cause is the failed read")
            .And.NotContain("has no secure flag value");
        _fixture.OfficeCreateBodies.Should().BeEmpty();
        _fixture.OfficeCreates.Should().BeEmpty();
    }

    // ── The isolated project removed again (the old core's two Office removal tests, ported to the new route) ─────────

    /// <summary>
    /// Owner round 31 item 2, Office: the maker cannot be shared — the project is removed again. The route answers 500 with the
    /// provisioning code that failed (NFR-07, 018b F2-1: the old core passed it through), the detail saying the project was
    /// "removed again" and that a retry can succeed, and nothing is left behind.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_WhenTheMakerCannotBeShared_TheProjectIsRemovedAgain_Is500WithTheShareFailedCode_AndNothingIsLeftBehind()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.FailShareForPrincipal = Creator;

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, await response.Content.ReadAsStringAsync());
        var (code, detail) = await RefusalOf(response);
        code.Should().Be(ProvisionProjectEndpoint.ReasonCreatorShareFailed);
        detail.Should().Contain("removed again").And.Contain("Try again in a few minutes", "a fault: a retry can succeed")
            .And.Contain("Nothing was created");
        var created = _fixture.OfficeCreates.Should().ContainSingle("the create reached Dataverse before it was removed").Subject;
        _fixture.ChildWorld.Deletes.Should().ContainSingle().Which.Id.Should().Be(created.Id, "the project that could not be shared is the one removed");
        _fixture.IsSecureOf(created.Id).Should().BeNull("read back gone");
    }

    /// <summary>
    /// Verifier item 7, Office, the removed case: a maker walled off after the plan — the project is removed again, the route
    /// answers 500 <c>sdap.provision.resume_creator_no_access</c> naming the refusal instead of "try again in a few minutes" (a
    /// retry is refused the same way), and nothing is left behind.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_WhenTheMakerIsWalledOffAfterThePlan_TheProjectIsRemovedAgain_Is500WithTheResumeNoAccessCode_AndNoRetryIsSuggested()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.CatalogMattersAs(RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.AfterOfficeCreate = _ => _fixture.NoAccessList.DenySystemUserOnRecord(Creator, matter);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, await response.Content.ReadAsStringAsync());
        var (code, detail) = await RefusalOf(response);
        code.Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorNoAccess, "the completion's own refusal, passed through");
        detail.Should().Contain("removed again").And.NotContain("Try again");
        detail.Should().NotContain("..").And.NotContain("Nothing was changed")
            .And.EndWith("an administrator needs to review your access to the secure record it would be filed under.");
        var created = _fixture.OfficeCreates.Should().ContainSingle().Subject;
        _fixture.ChildWorld.Deletes.Should().ContainSingle().Which.Id.Should().Be(created.Id);
        _fixture.IsSecureOf(created.Id).Should().BeNull("read back gone");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A team's unit as the fixture answers it (the default Office unit unless a test names another).</summary>
    private Guid OfficeUnitOf(Guid team) =>
        _fixture.TeamBusinessUnits.TryGetValue(team, out var unit) ? unit : Guid.Parse("cb15f587-baa0-f111-aaac-000d3a990158");

    private static Guid? BoundTeam(IReadOnlyDictionary<string, object?> body) =>
        body.TryGetValue("ownerid@odata.bind", out var bind) && bind is string path
        && Guid.TryParse(path.TrimEnd(')').Split('(').Last(), out var team)
            ? team
            : null;

    private static bool Flag(IReadOnlyDictionary<string, object?> body) =>
        body.TryGetValue("sprk_issecure", out var flag) && (flag is true || flag is JsonElement { ValueKind: JsonValueKind.True });

    private static async Task<Guid> CreatedIdOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Guid.Parse(doc.RootElement.GetProperty("id").GetString()!);
    }

    private static async Task<string?> ErrorCodeOf(HttpResponseMessage response) => (await RefusalOf(response)).Code;

    private static async Task<(string? Code, string? Detail)> RefusalOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (doc.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null,
            doc.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null);
    }
}
