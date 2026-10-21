using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// Ontology task 126 (#1399): G5 AppendTo on a BUSINESS-OWNED lookup target (<c>systemuser</c>, <c>team</c>,
/// <c>businessunit</c>), uac-r2's rule (#1355 comment 6089838200 §6, D-113). Dataverse refuses RetrievePrincipalAccess on
/// those tables (live, spaarkedev1 2026-10-08), so before this change every create binding one was the uniform 404 for
/// every caller. The rule (as amended by uac-r2's review of PR #2012): the caller holds the table's AppendTo privilege and
/// some grant of it covers the ROW's business unit from the grant's OWN anchor — Global any, Deep the anchor or one under
/// it, Local the anchor only, Basic only a systemuser that is the caller — and the row is readable as the caller; anything
/// else is the uniform 404. By default a grant is anchored at the caller's business unit (a role assigned to them). One positive and one negative per depth, and a
/// cross-business-unit refusal, through the REAL <c>POST /api/v1/child-records/sprk_analysis</c> handler (its live
/// <c>sprk_ReviewerBy</c> systemuser lookup) and, for the team and business-unit targets no create route binds today,
/// through the REAL G5 core.
/// </summary>
/// <remarks>The caller sits in <see cref="Directory.ChildBu"/> (under <see cref="Directory.GeneralBu"/>; the secure business
/// unit is its sibling). <see cref="GrandchildBu"/> is scripted under the caller's.</remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid GrandchildBu = Guid.Parse("a1260000-0000-4000-8000-0000000000b0");
    private static readonly Guid Colleague = Guid.Parse("a1260000-0000-4000-8000-0000000000c1");
    private static readonly Guid GrandchildUser = Guid.Parse("a1260000-0000-4000-8000-0000000000c2");
    private static readonly Guid ParentBuUser = Guid.Parse("a1260000-0000-4000-8000-0000000000c3");
    private static readonly Guid SiblingBuUser = Guid.Parse("a1260000-0000-4000-8000-0000000000c4");
    private static readonly Guid ChildBuTeam = Guid.Parse("a1260000-0000-4000-8000-0000000000d1");
    private static readonly Guid ParentBuTeam = Guid.Parse("a1260000-0000-4000-8000-0000000000d2");

    private const string AppendToUser = "prvAppendToUser";
    private const string AppendToTeam = "prvAppendToTeam";
    private const string AppendToBusinessUnit = "prvAppendToBusinessUnit";

    private void SeedBusinessOwnedWorld()
    {
        _user.BusinessUnitParents[GrandchildBu] = Directory.ChildBu;
        _user.BusinessUnitOf[Caller] = Directory.ChildBu;
        _user.BusinessUnitOf[Colleague] = Directory.ChildBu;
        _user.BusinessUnitOf[GrandchildUser] = GrandchildBu;
        _user.BusinessUnitOf[ParentBuUser] = Directory.GeneralBu;
        _user.BusinessUnitOf[SiblingBuUser] = Directory.SecureBu;
        _user.BusinessUnitOf[ChildBuTeam] = Directory.ChildBu;
        _user.BusinessUnitOf[ParentBuTeam] = Directory.GeneralBu;
    }

    /// <summary>The caller holds <paramref name="privilege"/> at <paramref name="depth"/> (anchored at their own business unit).</summary>
    private void Holds(string privilege, string depth)
    {
        SeedBusinessOwnedWorld();
        _user.Held.Add(privilege);
        _user.Depths[privilege] = depth;
    }

    /// <summary>An analysis (no filing) whose reviewer is <paramref name="reviewer"/> — a caller-chosen systemuser lookup.</summary>
    private Task<IResult> CreateAnalysisReviewedBy(Guid reviewer) =>
        CreateChild("sprk_analysis", new()
        {
            ["sprk_name"] = "zz-126 review",
            ["sprk_ReviewerBy@odata.bind"] = $"/systemusers({reviewer:D})",
        });

    private void ShouldBeCreatedWithReviewer(IResult result, Guid reviewer)
    {
        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        Bind(_appCreates.Should().ContainSingle().Subject.Fields, "sprk_ReviewerBy@odata.bind")
            .Should().Be($"/systemusers({reviewer:D})");
    }

    private void ShouldBeTheUniform404(IResult result)
    {
        Status(result).Should().Be(StatusCodes.Status404NotFound);
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFoundCode);
        _appCreates.Should().BeEmpty("a refused create writes nothing");
        _user.Posts.Should().BeEmpty();
    }

    /// <summary>The REAL G5 core's AppendTo answer on one business-owned record, as the caller.</summary>
    private Task<OwnedChildWrite.Outcome> AppendToAsTheCaller(string table, string entitySet, Guid id) =>
        CallerWriteAuthorization.CheckCallerMayAppendToAsync(
            _user, Caller, new[] { new DataverseWriteItemMapper.MappedLookup("sprk_x", table, entitySet, id) }, CancellationToken.None);

    // ── The harness models the refusal (so a pass below is the new branch, not RetrievePrincipalAccess) ─────────

    [Fact]
    public async Task BusinessOwned_TheHarnessRefusesRetrievePrincipalAccessOnSystemuserTeamAndBusinessUnit_AsDataverseDoes()
    {
        foreach (var set in new[] { "systemusers", "teams", "businessunits" })
        {
            var target = Uri.EscapeDataString($"{{\"@odata.id\":\"{set}({Colleague:D})\"}}");
            var answer = await _user.GetAsync(
                $"systemusers({Caller:D})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)?@p1={target}", CancellationToken.None);
            answer.IsSuccess.Should().BeFalse(set);
            answer.StatusCode.Should().Be(400, set);
        }
    }

    [Fact]
    public async Task BusinessOwned_WithoutTheAppendToPrivilege_IsTheUniform404_EvenForTheCallersOwnRecord()
    {
        SeedBusinessOwnedWorld();

        ShouldBeTheUniform404(await CreateAnalysisReviewedBy(Caller));
    }

    // ── Global ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Global_AUserInAnyBusinessUnit_IsBound()
    {
        Holds(AppendToUser, "Global");

        ShouldBeCreatedWithReviewer(await CreateAnalysisReviewedBy(SiblingBuUser), SiblingBuUser);
    }

    [Fact]
    public async Task Global_AUserTheCallerCannotRead_IsTheUniform404()
    {
        Holds(AppendToUser, "Global");
        _user.UnreadableRows.Add(SiblingBuUser);

        ShouldBeTheUniform404(await CreateAnalysisReviewedBy(SiblingBuUser));
    }

    // ── Deep ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deep_AUserInABusinessUnitUnderTheCallers_IsBound_AskedAsTheCallerWithUnderOrEqual()
    {
        Holds(AppendToUser, "Deep");

        ShouldBeCreatedWithReviewer(await CreateAnalysisReviewedBy(GrandchildUser), GrandchildUser);
        _user.UnderQuestions.Should().Contain((GrandchildBu, Directory.ChildBu));
    }

    [Fact]
    public async Task Deep_AUserInTheParentBusinessUnit_IsTheUniform404()
    {
        Holds(AppendToUser, "Deep");

        ShouldBeTheUniform404(await CreateAnalysisReviewedBy(ParentBuUser));
    }

    // ── Local ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Local_AColleagueInTheCallersBusinessUnit_IsBound()
    {
        Holds(AppendToUser, "Local");

        ShouldBeCreatedWithReviewer(await CreateAnalysisReviewedBy(Colleague), Colleague);
    }

    [Fact]
    public async Task Local_AUserInABusinessUnitUnderTheCallers_IsTheUniform404()
    {
        Holds(AppendToUser, "Local");

        ShouldBeTheUniform404(await CreateAnalysisReviewedBy(GrandchildUser));
    }

    // ── Basic ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Basic_TheCallerThemselves_IsBound()
    {
        Holds(AppendToUser, "Basic");

        ShouldBeCreatedWithReviewer(await CreateAnalysisReviewedBy(Caller), Caller);
    }

    [Fact]
    public async Task Basic_AColleagueInTheCallersOwnBusinessUnit_IsTheUniform404()
    {
        Holds(AppendToUser, "Basic");

        ShouldBeTheUniform404(await CreateAnalysisReviewedBy(Colleague));
    }

    // ── Cross business unit (the live Spaarke Basic User grant: prvAppendToUser at Local) ─────────────────────

    [Fact]
    public async Task CrossBusinessUnit_ALocalCallerBindingAUserInASiblingBusinessUnit_IsRefused_ExactlyLikeAMissingUser()
    {
        Holds(AppendToUser, "Local");

        var crossBu = await CreateAnalysisReviewedBy(SiblingBuUser);
        _user.InvisibleRows.Add(Colleague);
        var missing = await CreateAnalysisReviewedBy(Colleague);

        ShouldBeTheUniform404(crossBu);
        (Status(crossBu), ReasonCode(crossBu), Detail(crossBu)).Should().Be((Status(missing), ReasonCode(missing), Detail(missing)),
            "a user in another business unit and one that does not exist answer alike");
    }

    // ── Fail closed on what the rule does not describe ─────────────────────────────────────────────────────────

    // ── Each grant applies at ITS OWN anchor (D-113 as amended by uac-r2's review of PR #2012): a privilege inherited
    //    through a team in another business unit reaches that business unit, as Dataverse evaluates it ─────────────

    [Fact]
    public async Task Local_AnchoredAtAnotherBusinessUnit_CoversThatBusinessUnit()
    {
        Holds(AppendToUser, "Local");
        _user.Anchors[AppendToUser] = Directory.SecureBu;

        ShouldBeCreatedWithReviewer(await CreateAnalysisReviewedBy(SiblingBuUser), SiblingBuUser);
    }

    [Fact]
    public async Task Local_AnchoredAtAnotherBusinessUnit_DoesNotCoverTheCallersOwn()
    {
        Holds(AppendToUser, "Local");
        _user.Anchors[AppendToUser] = Directory.SecureBu;

        ShouldBeTheUniform404(await CreateAnalysisReviewedBy(Colleague));
    }

    [Fact]
    public async Task Deep_AnchoredAtAnotherBusinessUnit_CoversABusinessUnitUnderIt_AskedFromTheAnchor()
    {
        Holds(AppendToUser, "Deep");
        _user.Anchors[AppendToUser] = Directory.GeneralBu;

        ShouldBeCreatedWithReviewer(await CreateAnalysisReviewedBy(SiblingBuUser), SiblingBuUser);
        _user.UnderQuestions.Should().Equal(new[] { (Directory.SecureBu, Directory.GeneralBu) });
    }

    /// <summary>D-148: a grant Dataverse reports with no anchor is denied at every depth — each case names a target the
    /// same grant WITH an anchor would cover (Basic: the caller's own row), and Deep never reaches the hierarchy question.</summary>
    [Theory]
    [InlineData("Global", false)]
    [InlineData("Deep", false)]
    [InlineData("Local", false)]
    [InlineData("Basic", true)]
    public async Task AGrantWithNoAnchor_IsTheUniform404_AtEveryDepth(string depth, bool theCallersOwnRow)
    {
        Holds(AppendToUser, depth);
        _user.Anchors[AppendToUser] = Guid.Empty;

        ShouldBeTheUniform404(await CreateAnalysisReviewedBy(theCallersOwnRow ? Caller : Colleague));
        _user.UnderQuestions.Should().BeEmpty("an unanchored grant is refused before any hierarchy question");
    }

    // ── Several grants of one privilege: any one that covers is enough; none covering is the 404 ─────────────

    [Fact]
    public async Task SeveralGrants_AnUncountedGrantBeforeACoveringOne_IsBound()
    {
        Holds(AppendToUser, "Local");
        _user.Grants[AppendToUser] = new() { ("Local", Directory.SecureBu), ("Local", Directory.ChildBu) };

        ShouldBeCreatedWithReviewer(await CreateAnalysisReviewedBy(Colleague), Colleague);
    }

    [Fact]
    public async Task SeveralGrants_AnUncountedGrantBeforeAGlobalOne_IsBound()
    {
        Holds(AppendToUser, "Local");
        _user.Grants[AppendToUser] = new() { ("Local", Directory.SecureBu), ("Global", Directory.GeneralBu) };

        ShouldBeCreatedWithReviewer(await CreateAnalysisReviewedBy(Colleague), Colleague);
    }

    [Fact]
    public async Task SeveralGrants_NoneCovering_IsTheUniform404()
    {
        Holds(AppendToUser, "Local");
        _user.Grants[AppendToUser] = new() { ("Local", Directory.SecureBu), ("Deep", Directory.SecureBu), ("Basic", Directory.ChildBu) };

        ShouldBeTheUniform404(await CreateAnalysisReviewedBy(Colleague));
    }

    // ── Fail closed when Dataverse does not answer ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(503)]
    public async Task ThePrivilegeQuestionFailing_IsTheUniform404(int status)
    {
        Holds(AppendToUser, "Global");
        _user.PrivilegesFault = (AppendToUser, status);

        ShouldBeTheUniform404(await CreateAnalysisReviewedBy(Colleague));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task TheHierarchyQuestionFailing_IsTheUniform404(int status)
    {
        Holds(AppendToUser, "Deep");
        _user.HierarchyFault = status;

        ShouldBeTheUniform404(await CreateAnalysisReviewedBy(GrandchildUser));
        _user.UnderQuestions.Should().ContainSingle("the hierarchy question was asked, and its failure denied");
    }

    [Theory]
    [InlineData("None")]
    [InlineData("8")]
    [InlineData("")]
    public async Task AnUnknownDepth_IsTheUniform404(string depth)
    {
        Holds(AppendToUser, depth);

        ShouldBeTheUniform404(await CreateAnalysisReviewedBy(Caller));
    }

    [Fact]
    public async Task AUserRowReportingNoBusinessUnit_IsTheUniform404()
    {
        Holds(AppendToUser, "Global");
        _user.BusinessUnitOf.Remove(SiblingBuUser);

        ShouldBeTheUniform404(await CreateAnalysisReviewedBy(SiblingBuUser));
    }

    // ── Team and business-unit targets, through the REAL G5 core ─────────────────────────────────────────────

    [Fact]
    public async Task Team_Local_ATeamInTheCallersBusinessUnit_IsAllowed_AndOneInTheParentIsDenied()
    {
        Holds(AppendToTeam, "Local");

        (await AppendToAsTheCaller("team", "teams", ChildBuTeam)).Succeeded.Should().BeTrue();
        var denied = await AppendToAsTheCaller("team", "teams", ParentBuTeam);
        denied.Succeeded.Should().BeFalse();
        denied.ParentUnavailable.Should().BeTrue("the uniform not-found");
    }

    [Fact]
    public async Task Team_Basic_IsDeniedEvenInTheCallersOwnBusinessUnit_BasicCoversOnlyTheCallersOwnUserRow()
    {
        Holds(AppendToTeam, "Basic");

        var denied = await AppendToAsTheCaller("team", "teams", ChildBuTeam);
        denied.Succeeded.Should().BeFalse();
        denied.ParentUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task BusinessUnit_Deep_ABusinessUnitUnderTheCallers_IsAllowed_AndTheParentIsDenied()
    {
        Holds(AppendToBusinessUnit, "Deep");

        (await AppendToAsTheCaller("businessunit", "businessunits", GrandchildBu)).Succeeded.Should().BeTrue();
        (await AppendToAsTheCaller("businessunit", "businessunits", Directory.ChildBu)).Succeeded.Should().BeTrue("Deep includes the caller's own");
        var denied = await AppendToAsTheCaller("businessunit", "businessunits", Directory.GeneralBu);
        denied.Succeeded.Should().BeFalse();
        denied.ParentUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task BusinessUnit_Local_OnlyTheCallersOwn()
    {
        Holds(AppendToBusinessUnit, "Local");

        (await AppendToAsTheCaller("businessunit", "businessunits", Directory.ChildBu)).Succeeded.Should().BeTrue();
        (await AppendToAsTheCaller("businessunit", "businessunits", GrandchildBu)).Succeeded.Should().BeFalse();
        (await AppendToAsTheCaller("businessunit", "businessunits", Directory.SecureBu)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AUserOwnedTarget_IsStillAnsweredByRetrievePrincipalAccess_WithNoPrivilegeQuestion()
    {
        Holds(AppendToUser, "Global");
        Holds("prvAppendTosprk_matter", "Global");
        _user.NoAppendToIn.Add(("sprk_matters", OrdinaryMatter));

        var denied = await AppendToAsTheCaller("sprk_matter", "sprk_matters", OrdinaryMatter);

        denied.Succeeded.Should().BeFalse(
            "a user-owned row is answered by its own access rights, never by a table privilege — even one held at Global");
        denied.ParentUnavailable.Should().BeTrue();
        _user.PrivilegeQuestions.Should().BeEmpty("no privilege question is asked about a user-owned target");
        _user.UnderQuestions.Should().BeEmpty();
    }

    // ── The response reader (live shape, spaarkedev1 2026-10-16) ───────────────────────────────────────────────

    [Fact]
    public void PrivilegeGrantsIn_ReadsEachGrantsDepthAndAnchor_AndDropsWhatItCannotRead()
    {
        var body = JsonSerializer.SerializeToElement(new
        {
            RolePrivileges = new object[]
            {
                new { Depth = "Local", PrivilegeId = Guid.NewGuid(), BusinessUnitId = Directory.ChildBu, PrivilegeName = "prvAppendToUser" },
                new { Depth = "Deep", PrivilegeName = "prvappendtouser" },
                new { Depth = "Unknown", BusinessUnitId = Directory.ChildBu, PrivilegeName = "prvAppendToUser" },
                new { Depth = "Global", BusinessUnitId = Directory.ChildBu, PrivilegeName = "prvAppendToTeam" },
            },
        });

        CallerWriteAuthorization.PrivilegeGrantsIn(body, "prvAppendToUser").Should().Equal(
            (PrivilegeDepth.Local, Directory.ChildBu),
            (PrivilegeDepth.Deep, Guid.Empty));
        CallerWriteAuthorization.PrivilegeGrantsIn(JsonSerializer.SerializeToElement(new { value = 1 }), "prvAppendToUser").Should().BeEmpty();
        CallerWriteAuthorization.PrivilegeGrantsIn(null, "prvAppendToUser").Should().BeEmpty();
    }
}
