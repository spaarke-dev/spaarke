using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;
using Harness = Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.Harness;
using OwnershipDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// field-mapping-server-write-path-r1 task 049 fix round 2 (UAC-r2 rulings D1 and the smaller rulings): a project the wizard
/// creates as secure is secure from creation — <c>POST /api/v1/child-records/sprk_project?secure=true</c>, driven through its
/// REAL handler, the host's REAL <see cref="SecureRootFilingGate"/> and provisioning's fixture.
/// </summary>
/// <remarks>
/// The gate (D1): the caller first needs, on the record as an ordinary create would make it (owned by the team the owner
/// rule names: <see cref="OrdinaryTeam"/>, in the child business unit), the rights <c>provision-project</c>'s delegation rule
/// asks for — Write and the table's Share privilege — refused in that filter's own 403 shape, with nothing written.
/// </remarks>
public partial class SecureRootInheritanceWriterTests
{
    private static readonly Guid OrdinaryTeam = OwnershipDirectory.ChildTeam;
    private static readonly Guid ProjectAssignee = Guid.Parse("a0490000-0000-4000-8000-0000000000b2");
    private const string SecureTrace = "trace-049-secure";

    /// <summary>The caller's Dataverse with Write and Share on projects at <paramref name="writeDepth"/>/<paramref name="shareDepth"/>
    /// (anchored at the general business unit, above the child unit the ordinary owner team sits in), or not held at all.</summary>
    private SecureChildOwnershipAiToolTests.ScriptedUserClient SecureCaller(string? writeDepth = "Deep", string? shareDepth = "Deep",
        Guid? anchor = null)
    {
        var user = Scripted();
        user.BusinessUnitOf[OrdinaryTeam] = OwnershipDirectory.ChildBu;
        foreach (var (privilege, depth) in new[] { ("prvWritesprk_project", writeDepth), ("prvSharesprk_project", shareDepth) })
        {
            if (depth is null)
                continue;
            user.Held.Add(privilege);
            user.Depths[privilege] = depth;
            user.Anchors[privilege] = anchor ?? OwnershipDirectory.GeneralBu;
        }

        return user;
    }

    private Task<IResult> CreateProjectThroughTheRoute(
        SecureChildOwnershipAiToolTests.ScriptedUserClient user, string? secure, IFieldMappingDataverseService appOnly,
        IServiceScopeFactory? scopes = null, string table = "sprk_project", RecordOwnershipResolverDouble? owners = null)
    {
        var payload = JsonSerializer.SerializeToElement(table == "sprk_project"
            ? new Dictionary<string, object?>
            {
                ["sprk_projectname"] = "Phase 2",
                ["sprk_AssignedToInternal@odata.bind"] = $"/contacts({ProjectAssignee:D})",
            }
            : new Dictionary<string, object?> { ["sprk_name"] = "Review the lease" });

        using var scope = _fixture.Services.CreateScope();
        return ChildRecordEndpoints.CreateAsync(
            table, payload, user,
            RouteRecordCreation.Over(
                owners ?? new RecordOwnershipResolverDouble { TeamId = OrdinaryTeam },
                appOnly,
                new StampWorld().Restamper,
                scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(),
                _gate,
                scopes ?? new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>()),
            new DefaultHttpContext
            {
                TraceIdentifier = SecureTrace,
                // A project infers its mapping source from its filing (none here): the handler needs the probe, never asks it.
                RequestServices = new ServiceCollection()
                    .AddSingleton(new CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(),
                        NullLogger<CallerRecordAccessProbe>.Instance))
                    .BuildServiceProvider(),
            },
            NullLogger<Program>.Instance, CancellationToken.None, secure: secure);
    }

    /// <summary>The refusal is the delegation filter's own: the same status, title, type, reason code, words and trace id.</summary>
    private static void ShouldBeTheDelegationDenial(IResult result, string reasonCode, string detail)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        var expected = ((ProblemHttpResult)DelegationRuleFilter.DenyResult(reasonCode, detail, SecureTrace)).ProblemDetails;
        problem.Status.Should().Be(StatusCodes.Status403Forbidden);
        problem.Status.Should().Be(expected.Status);
        problem.Title.Should().Be(expected.Title);
        problem.Type.Should().Be(expected.Type);
        problem.Detail.Should().Be(expected.Detail);
        problem.Extensions.Should().BeEquivalentTo(expected.Extensions);
    }

    private void NothingWritten()
    {
        _writes.Should().BeEmpty("refused before anything is created");
        _fixture.Grants.Should().BeEmpty("nothing is shared");
        _fixture.Updates.Should().BeEmpty("nothing is updated");
    }

    /// <summary>
    /// Records the new row's isolation state at the moment the "Assigned *" pass asks for its scope (the first scope after the
    /// create), as <c>RecordCreationServiceSecureRootSeamTests._creatorShareAtAssignedAccess</c> does for the seam.
    /// </summary>
    private sealed class AssignedPassProbe(SecureRootInheritanceWriterTests owner, IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public (int CreatorShare, bool IsSecure, Guid? OwningTeam, string? ContainerId)? Observed { get; private set; }

        public IServiceScope CreateScope()
        {
            if (Observed is null && owner._writes.LastOrDefault() is { Id: var id } && id != Guid.Empty)
            {
                Observed = (owner._fixture.ShareMaskOf(id, Creator), owner._fixture.IsSecureOf(id) == true,
                    owner._fixture.OwningTeamOf(id), owner._fixture.ContainerIdOf(id));
            }

            return inner.CreateScope();
        }
    }

    // ── D1: the gate is provision-project's ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SecureFromCreation_WithoutTheSharePrivilege_Is403ShareRequired_InTheFiltersShape_AndNothingIsWritten()
    {
        var result = await CreateProjectThroughTheRoute(
            SecureCaller(shareDepth: null), "true", new Mock<IFieldMappingDataverseService>(MockBehavior.Strict).Object);

        ShouldBeTheDelegationDenial(result, DelegationRuleFilter.DenyShareRequired, DelegationRuleFilter.RightsRequiredDetail);
        NothingWritten();
    }

    [Fact]
    public async Task SecureFromCreation_WithShare_ButWriteNotAtADepthReachingTheOwnerTeam_Is403WriteRequired_AndNothingIsWritten()
    {
        // Write at Local in the general unit: the ordinary owner team sits in the child unit, which Local does not reach.
        var user = SecureCaller(writeDepth: "Local", shareDepth: "Global");

        var result = await CreateProjectThroughTheRoute(
            user, "true", new Mock<IFieldMappingDataverseService>(MockBehavior.Strict).Object);

        ShouldBeTheDelegationDenial(result, DelegationRuleFilter.DenyWriteRequired, DelegationRuleFilter.RightsRequiredDetail);
        NothingWritten();
    }

    [Fact]
    public async Task SecureFromCreation_WithBasicDepth_AndNotAMemberOfTheOwnerTeam_Is403WriteRequired()
    {
        var user = SecureCaller(writeDepth: "Basic", shareDepth: "Basic");

        var result = await CreateProjectThroughTheRoute(
            user, "true", new Mock<IFieldMappingDataverseService>(MockBehavior.Strict).Object);

        ShouldBeTheDelegationDenial(result, DelegationRuleFilter.DenyWriteRequired, DelegationRuleFilter.RightsRequiredDetail);
        NothingWritten();
    }

    [Fact]
    public async Task SecureFromCreation_WhenTheRightsCheckThrows_Is403CheckFailed_AndNothingIsWritten()
    {
        var user = SecureCaller();
        user.PrivilegesThrowFor = "prvSharesprk_project";

        var result = await CreateProjectThroughTheRoute(
            user, "true", new Mock<IFieldMappingDataverseService>(MockBehavior.Strict).Object);

        ShouldBeTheDelegationDenial(result, DelegationRuleFilter.DenyCheckFailed, DelegationRuleFilter.CheckFailedDetail);
        NothingWritten();
    }

    [Fact]
    public async Task SecureFromCreation_WhenTheRightsQuestionFails_Is403CheckFailed_AndNothingIsWritten()
    {
        var user = SecureCaller();
        user.PrivilegesFault = ("prvWritesprk_project", 503);

        var result = await CreateProjectThroughTheRoute(
            user, "true", new Mock<IFieldMappingDataverseService>(MockBehavior.Strict).Object);

        ShouldBeTheDelegationDenial(result, DelegationRuleFilter.DenyCheckFailed, DelegationRuleFilter.CheckFailedDetail);
        NothingWritten();
    }

    [Fact]
    public async Task SecureFromCreation_WhenTheOwnerTeamsUnitCannotBeRead_Is403CheckFailed_AndNothingIsWritten()
    {
        var user = SecureCaller();
        user.BusinessUnitOf.Remove(OrdinaryTeam);

        var result = await CreateProjectThroughTheRoute(
            user, "true", new Mock<IFieldMappingDataverseService>(MockBehavior.Strict).Object);

        ShouldBeTheDelegationDenial(result, DelegationRuleFilter.DenyCheckFailed, DelegationRuleFilter.CheckFailedDetail);
        NothingWritten();
    }

    [Fact]
    public async Task SecureFromCreation_WithWriteAndShare_IsCreatedIntoIsolation_ComesOutSecure_AndTheAssigneeIsOnlySuggested()
    {
        var harness = new Harness { Participations = new(new RootRecordFlags(IsSecure: true, IsRestricted: false)) };
        harness.Contact(ProjectAssignee);
        var services = new ServiceCollection();
        services.AddScoped(_ => harness.Materializer);
        var appOnly = AppCreatesIntoTheWorld(afterCreate: id =>
        {
            var fields = _writes.Single(w => w.Id == id).Fields;
            fields["sprk_issecure"].Should().Be(true, "flagged in the create itself, before the \"Assigned *\" step");
            harness.Store.Root(ExternalGrantRootType.Project, id);
            harness.Store.Assign(ExternalGrantRootType.Project, id, "sprk_assignedtointernal", ProjectAssignee);
        });

        var probe = new AssignedPassProbe(this, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
        var result = await CreateProjectThroughTheRoute(SecureCaller(), "true", appOnly.Object, probe);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        var (table, created, fields) = _writes.Should().ContainSingle().Subject;

        // Fix round 3 (F3): the order, observed at the moment the "Assigned *" pass starts — not inferred from a stub that
        // answers "secure" whenever it is asked. Isolation is complete before the pass runs on the intent path.
        probe.Observed.Should().NotBeNull("the \"Assigned *\" pass ran");
        probe.Observed!.Value.CreatorShare.Should().Be(RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CreatorAccessRights),
            "the creator's share was made (and read back) before the \"Assigned *\" pass");
        probe.Observed.Value.IsSecure.Should().BeTrue("flagged secure before the pass");
        probe.Observed.Value.OwningTeam.Should().Be(SecureTeam, "owned by the named team before the pass");
        probe.Observed.Value.ContainerId.Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId, "the container exists before the pass: securing is complete");
        table.Should().Be("sprk_project");
        BoundId(fields, "ownerid@odata.bind").Should().Be(SecureTeam, "created owned by the named Secure Record Owners team");
        BoundId(fields, "sprk_CreatedByPerson@odata.bind").Should().Be(Creator);
        ShouldBeSecure(created, "a project the wizard creates as secure is secure from creation");

        var row = harness.Store.RowsOf(created, ProjectAssignee).Should().ContainSingle(
            "the \"Assigned *\" pass evaluated the assignee on the secured record").Subject;
        row.State.Should().Be(AssignedAccessState.PendingConfirmation, "Secure: a suggestion only");
        harness.Grants.ActiveRowsOf(created, ProjectAssignee).Should().BeEmpty("nothing is granted");
        harness.Shares.Writes.Should().BeEmpty("nothing is shared to the assignee");
    }

    // Fix round 3 (item 10, UAC-r2): Basic is not counted for a team-owned target, even for a member of the owning team.
    [Fact]
    public async Task SecureFromCreation_WithBasicDepth_AsAMemberOfTheOwnerTeam_IsRefused403_AndWritesNothing()
    {
        var user = SecureCaller(writeDepth: "Basic", shareDepth: "Basic");
        user.TeamMemberships.Add(OrdinaryTeam);

        var result = await CreateProjectThroughTheRoute(
            user, "true", new Mock<IFieldMappingDataverseService>(MockBehavior.Strict).Object);

        ShouldBeTheDelegationDenial(result, DelegationRuleFilter.DenyWriteRequired, DelegationRuleFilter.RightsRequiredDetail);
        NothingWritten();
        _writes.Should().BeEmpty("no project");
        _fixture.Grants.Should().BeEmpty("no share");
    }

    // Local, Deep and Global (anchored at the owner team's own unit, which each covers) create into isolation, member or not.
    [Theory]
    [InlineData("Local", false)]
    [InlineData("Local", true)]
    [InlineData("Deep", false)]
    [InlineData("Deep", true)]
    [InlineData("Global", false)]
    [InlineData("Global", true)]
    public async Task SecureFromCreation_WithLocalDeepOrGlobal_IsCreatedIntoIsolation(string depth, bool member)
    {
        var user = SecureCaller(writeDepth: depth, shareDepth: depth, anchor: OwnershipDirectory.ChildBu);
        if (member)
            user.TeamMemberships.Add(OrdinaryTeam);

        var result = await CreateProjectThroughTheRoute(user, "true", AppCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        ShouldBeSecure(_writes.Should().ContainSingle().Subject.Id, $"{depth} reaches the record the owner team will own");
    }

    [Fact]
    public async Task SecureFromCreation_WhenTheCreatorCannotBeShared_TheRowIsRemoved_AndNothingIsGranted()
    {
        _fixture.FailShareForPrincipal = Creator;

        var result = await CreateProjectThroughTheRoute(SecureCaller(), "true", AppCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status500InternalServerError, RouteDetail(result));
        RouteDetail(result).Should().Be("The project could not be secured, so it was not kept. Please try again or ask an administrator.");
        var created = _writes.Should().ContainSingle().Subject.Id;
        _fixture.Grants.Should().NotContain(g => g.RecordId == created, "nothing is granted when isolation does not complete");
    }

    // ── Fix round 3 (F1/F2): an unfinished secure-from-creation create promises no repair that never comes ───────────────

    [Fact]
    public async Task SecureFromCreation_WhenTheCreatorCannotBeSharedNorTheRowRemoved_SaysAnAdministratorMustAct_AndPromisesNoRetry()
    {
        _fixture.FailShareForPrincipal = Creator;
        World.DeletesFail = true;
        _fixture.Logs.Clear();

        var result = await CreateProjectThroughTheRoute(SecureCaller(), "true", AppCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        var warnings = RouteBody(result).GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToList();
        warnings.Should().ContainSingle().Which.Should().Contain("could not be shared to you and could not be removed")
            .And.Contain("an administrator needs to review and remove it")
            .And.NotContain("automatically once that step succeeds").And.NotContain("retried");
        var critical = _fixture.Logs.Entries.Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Critical).ToList();
        critical.Should().ContainSingle().Which.Message.Should().Contain("ACTION NEEDED").And.Contain("must review and remove")
            .And.NotContain("retries every");
    }

    [Fact]
    public async Task SecureFromCreation_WhenTheContainerCannotBeCreated_SaysAnAdministratorCanFinishSecuringIt()
    {
        _fixture.SpeContainerCreationSucceeds = false;
        _fixture.Logs.Clear();

        var result = await CreateProjectThroughTheRoute(SecureCaller(), "true", AppCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        var warnings = RouteBody(result).GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToList();
        warnings.Should().ContainSingle().Which.Should().Contain("shared to you, but securing it could not be finished yet")
            .And.Contain("it is not finished automatically; an administrator can finish securing the project.")
            .And.NotContain("provision-project")
            .And.NotContain("completed automatically");
        _fixture.Logs.Entries.Should().NotContain(e => e.Message.Contains("the secure-root inheritance job completes it"),
            "the job does not visit it, so the log promises nothing from the job");
        World.Deletes.Should().BeEmpty("a record its creator can open is never removed");
    }

    // ── No intent: unchanged ──────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("FALSE")]
    public async Task NoSecureIntent_TheCreateIsOrdinary_AndAsksNoDelegationRights(string? secure)
    {
        var user = SecureCaller(writeDepth: null, shareDepth: null); // holds neither: the intent's gate is never asked

        var result = await CreateProjectThroughTheRoute(user, secure, AppCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        var (_, created, fields) = _writes.Should().ContainSingle().Subject;
        BoundId(fields, "ownerid@odata.bind").Should().Be(OrdinaryTeam, "owned by the team the owner rule names");
        ShouldNotBeSecured(created, "no intent: an ordinary create");
        user.PrivilegeQuestions.SelectMany(q => q).Should().NotContain(
            ["prvWritesprk_project", "prvSharesprk_project"], "the delegation rule is asked only for an intent create");
    }

    // ── The route's argument rules (the smaller rulings) ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_project", "yes")]
    [InlineData("sprk_project", "1")]
    [InlineData("sprk_project", "true,true")]
    [InlineData("sprk_todo", "true")]
    [InlineData("sprk_matter", "true")]
    [InlineData("sprk_todo", "maybe")]
    public async Task SecureIntent_ThatIsMalformed_OrOnATableThatCannotBeSecure_Is400_AndNothingIsAsked(string table, string secure)
    {
        var user = SecureCaller();

        var result = await CreateProjectThroughTheRoute(
            user, secure, new Mock<IFieldMappingDataverseService>(MockBehavior.Strict).Object, table: table,
            owners: new RecordOwnershipResolverDouble { Fault = new InvalidOperationException("never asked") });

        RouteStatus(result).Should().Be(StatusCodes.Status400BadRequest, RouteDetail(result));
        RouteReason(result).Should().Be(ChildRecordEndpoints.SecureIntentInvalidCode);
        user.PrivilegeQuestions.Should().BeEmpty("refused before anything is read");
        NothingWritten();
    }

    [Fact]
    public async Task SecureIntent_FalseOnATableThatCannotBeSecure_IsNoIntent()
    {
        ChildRecordEndpoints.SecureIntentOf("sprk_todo", "false", out var problem).Should().BeFalse();
        problem.Should().BeNull();
        ChildRecordEndpoints.SecureIntentOf("sprk_workassignment", " True ", out problem).Should().BeTrue();
        problem.Should().BeNull();
        await Task.CompletedTask;
    }
}
