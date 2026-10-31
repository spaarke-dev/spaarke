using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 195 (#2352, owner choice (a)): a project is filed under its matter through the ADR-024
/// regarding pair, by ONE server core (<see cref="ProjectMatterFiling"/>) shared by
/// <c>PATCH /api/v1/child-records/sprk_project/{id}</c> (the wizards, the backfill) and the chat update tool. Driven here
/// through the route's REAL handler and the tool, with the host's REAL <see cref="SecureRootFilingGate"/>, the caller's
/// scripted Dataverse, and the application's reader and conditional writer applying to provisioning's fixture.
/// </summary>
public partial class SecureRootInheritanceWriterTests
{
    /// <summary>A project in the fixture, filed under <paramref name="matter"/> when given; returns its id.</summary>
    private Guid Project(Guid? matter = null, bool isSecure = false)
    {
        var project = Guid.NewGuid();
        _fixture.SeedProject(project, isSecure: isSecure);
        if (matter is { } m)
            FilePair(_fixture, "sprk_project", project, m, RecordTypeRef(_fixture, "sprk_matter"));
        return project;
    }

    private static JsonElement FilingBody(Guid matter, Guid typeRef) => JsonSerializer.SerializeToElement(new Dictionary<string, object?>
    {
        ["sprk_regardingrecordid"] = matter.ToString("D"),
        ["sprk_regardingrecordtype@odata.bind"] = $"/sprk_recordtype_refs({typeRef:D})",
    });

    /// <summary>The route's PATCH. The re-file's own collaborators are strict mocks: a project never reaches them.</summary>
    private Task<IResult> FileThroughTheRoute(
        Guid project, JsonElement body, SecureChildOwnershipAiToolTests.ScriptedUserClient user,
        IFieldMappingDataverseService? appOnly, Mock<IRecordOwnershipResolver>? ownership = null) =>
        ChildRecordEndpoints.RefileAsync(
            "sprk_project", project, body, user,
            (ownership ?? new Mock<IRecordOwnershipResolver>(MockBehavior.Strict)).Object,
            new StampWorld().Restamper,
            null!, // SecureChildReconciler: the child re-file's, never reached for a project
            new Mock<IGenericEntityService>(MockBehavior.Strict).Object,
            new DefaultHttpContext(), NullLogger<Program>.Instance, CancellationToken.None,
            _gate, appOnly);

    private string? PairIdOf(Guid project) => World.ValueOf<string>("sprk_project", project, SecureRootInheritance.PairIdColumn);

    private static void NoConditionalWrite(Mock<IFieldMappingDataverseService> appOnly) =>
        appOnly.Verify(s => s.UpdateRecordFieldsIfUnchangedAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);

    private static void NoUnconditionalWrite(Mock<IFieldMappingDataverseService> appOnly) =>
        appOnly.Verify(s => s.UpdateExistingRecordFieldsAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);

    // ── The route ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProjectFiling_Route_FirstFilingUnderAnOrdinaryMatter_Is204_ThePairIsWrittenAtTheVersionRead()
    {
        var (_, ordinary) = Matters();
        var project = Project();
        var typeRef = RecordTypeRef(_fixture, "sprk_matter");
        var user = Scripted();
        var appOnly = AppUpdatesTheWorld();

        var result = await FileThroughTheRoute(project, FilingBody(ordinary, typeRef), user, appOnly.Object);

        RouteStatus(result).Should().Be(StatusCodes.Status204NoContent, RouteDetail(result));
        PairIdOf(project).Should().Be(ordinary.ToString("D"));
        World.ValueOf<Microsoft.Xrm.Sdk.EntityReference>("sprk_project", project, SecureRootInheritance.PairTypeColumn)!.Id
            .Should().Be(typeRef);
        appOnly.Verify(s => s.UpdateRecordFieldsIfUnchangedAsync("sprk_project", project,
            It.Is<Dictionary<string, object?>>(f => f.Count == 2), 1L, It.IsAny<CancellationToken>()), Times.Once);
        NoUnconditionalWrite(appOnly);
        user.Patches.Should().BeEmpty("the caller never writes the pair");
        ShouldNotBeSecured(project, "filed under an ordinary matter");
    }

    [Fact]
    public async Task ProjectFiling_Route_TheSameMatterAgain_Is204_AndNothingIsWritten()
    {
        var (_, ordinary) = Matters();
        var project = Project(ordinary);
        var typeRef = World.ValueOf<Microsoft.Xrm.Sdk.EntityReference>("sprk_project", project, SecureRootInheritance.PairTypeColumn)!.Id;
        var appOnly = AppUpdatesTheWorld();

        var result = await FileThroughTheRoute(project, FilingBody(ordinary, typeRef), Scripted(), appOnly.Object);

        RouteStatus(result).Should().Be(StatusCodes.Status204NoContent, RouteDetail(result));
        NoConditionalWrite(appOnly);
        NoUnconditionalWrite(appOnly);
        _writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectFiling_Route_APairNamingAnotherMatter_IsReFiled()
    {
        var (_, from) = Matters();
        var to = Guid.NewGuid();
        _fixture.SeedMatter(to, isSecure: false);
        var project = Project(from);
        var appOnly = AppUpdatesTheWorld();

        var result = await FileThroughTheRoute(project, FilingBody(to, RecordTypeRef(_fixture, "sprk_matter")), Scripted(),
            appOnly.Object);

        RouteStatus(result).Should().Be(StatusCodes.Status204NoContent, RouteDetail(result));
        PairIdOf(project).Should().Be(to.ToString("D"));
        _writes.Should().ContainSingle();
    }

    [Fact]
    public async Task ProjectFiling_Route_WhenTheProjectChangesBetweenTheReadAndTheWrite_Is409_AndNothingIsWritten()
    {
        var (_, ordinary) = Matters();
        var project = Project();
        var appOnly = AppUpdatesTheWorld(beforeConditionalWrite: () =>
            World.Set("sprk_project", project, "versionnumber", 99L)); // another writer got there first

        var result = await FileThroughTheRoute(project, FilingBody(ordinary, RecordTypeRef(_fixture, "sprk_matter")), Scripted(),
            appOnly.Object);

        RouteStatus(result).Should().Be(StatusCodes.Status409Conflict);
        RouteReason(result).Should().Be(SecureRootInheritance.ReasonChangedConcurrently);
        PairIdOf(project).Should().BeNull();
        _writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectFiling_Route_APairNamingAnythingButAMatter_Is400_NotAMatter_AndNothingIsWritten()
    {
        var other = Project();
        var project = Project();
        var appOnly = AppUpdatesTheWorld();

        var result = await FileThroughTheRoute(project, FilingBody(other, RecordTypeRef(_fixture, "sprk_project")), Scripted(),
            appOnly.Object);

        RouteStatus(result).Should().Be(StatusCodes.Status400BadRequest);
        RouteReason(result).Should().Be(ProjectMatterFiling.NotAMatterCode);
        appOnly.Invocations.Should().BeEmpty("refused from the payload and the catalog, before anything is read");
        _writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectFiling_Route_AMatterTheCallerCannotAppendTo_IsTheSame404AsAMissingMatter_AndNothingIsWritten()
    {
        var (_, ordinary) = Matters();
        var absent = Guid.NewGuid();
        var project = Project();
        var typeRef = RecordTypeRef(_fixture, "sprk_matter");
        var user = Scripted();
        user.NoAppendTo.Add(ordinary);
        user.Missing.Add(absent);

        var unappendable = await FileThroughTheRoute(project, FilingBody(ordinary, typeRef), user, AppUpdatesTheWorld().Object);
        var missing = await FileThroughTheRoute(project, FilingBody(absent, typeRef), user, AppUpdatesTheWorld().Object);

        RouteStatus(unappendable).Should().Be(StatusCodes.Status404NotFound);
        (RouteStatus(unappendable), RouteReason(unappendable), RouteDetail(unappendable))
            .Should().Be((RouteStatus(missing), RouteReason(missing), RouteDetail(missing)));
        _writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]  // a row that does not exist (or the caller's read cannot reach): Dataverse's 404
    [InlineData(false)] // a row that exists but the caller may not read: Dataverse's 403
    public async Task ProjectFiling_Route_AProjectTheCallerCannotSee_IsTheUniform404(bool invisible)
    {
        var (_, ordinary) = Matters();
        var project = Project();
        var user = Scripted();
        (invisible ? user.InvisibleRows : user.UnreadableRows).Add(project);

        var result = await FileThroughTheRoute(project, FilingBody(ordinary, RecordTypeRef(_fixture, "sprk_matter")), user,
            AppUpdatesTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status404NotFound);
        RouteReason(result).Should().Be(ChildRecordEndpoints.NotFoundCode);
        _writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectFiling_Route_WithoutAppendOnTheProject_Is403_AndNothingIsWritten()
    {
        var (_, ordinary) = Matters();
        var project = Project();
        var user = Scripted();
        user.NoAppendTo.Add(project); // the scripted caller then holds ReadAccess,WriteAccess on it only

        var result = await FileThroughTheRoute(project, FilingBody(ordinary, RecordTypeRef(_fixture, "sprk_matter")), user,
            AppUpdatesTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status403Forbidden);
        RouteReason(result).Should().Be(ChildRecordEndpoints.DeniedCode);
        _writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectFiling_Route_WhenTheSecureRootGateRefuses_NothingIsWritten()
    {
        var (secure, _) = Matters();
        var project = Project();
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure); // the gate refuses: creator_no_access

        var result = await FileThroughTheRoute(project, FilingBody(secure, RecordTypeRef(_fixture, "sprk_matter")), Scripted(),
            AppUpdatesTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status403Forbidden);
        RouteReason(result).Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        _writes.Should().BeEmpty();
        _fixture.IsSecureOf(project).Should().BeFalse();
    }

    /// <summary>Item 6, case (a): an ordinary project under a secure matter would leave every record filed under the
    /// project with two secure roots above it (the storage resolver refuses those); refused before anything is written.</summary>
    [Fact]
    public async Task ProjectFiling_Route_UnderASecureMatter_Is409_SecureMatterUnsupported_AndNothingIsWritten()
    {
        var (secure, _) = Matters();
        var project = Project();
        var appOnly = AppUpdatesTheWorld();

        var result = await FileThroughTheRoute(project, FilingBody(secure, RecordTypeRef(_fixture, "sprk_matter")), Scripted(),
            appOnly.Object);

        RouteStatus(result).Should().Be(StatusCodes.Status409Conflict);
        RouteReason(result).Should().Be(ProjectMatterFiling.SecureMatterCode);
        NoConditionalWrite(appOnly);
        _writes.Should().BeEmpty();
        _fixture.IsSecureOf(project).Should().BeFalse();
    }

    /// <summary>Item 6, case (b): an already secure project under a secure matter — the same two-roots condition.</summary>
    [Fact]
    public async Task ProjectFiling_Route_ASecureProjectUnderASecureMatter_Is409_SecureMatterUnsupported()
    {
        var (secure, _) = Matters();
        var project = Project(isSecure: true);

        var result = await FileThroughTheRoute(project, FilingBody(secure, RecordTypeRef(_fixture, "sprk_matter")), Scripted(),
            AppUpdatesTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status409Conflict);
        RouteReason(result).Should().Be(ProjectMatterFiling.SecureMatterCode);
        _writes.Should().BeEmpty();
    }

    /// <summary>Item 2 and case (c): a project filed under a secure matter is never moved out from under it here (leaving
    /// a secure matter must not take it out of isolation).</summary>
    [Fact]
    public async Task ProjectFiling_Route_AwayFromASecureMatter_Is409_LeavesSecureRecord_AndNothingIsWritten()
    {
        var (secure, ordinary) = Matters();
        var project = Project(secure, isSecure: true);

        var result = await FileThroughTheRoute(project, FilingBody(ordinary, RecordTypeRef(_fixture, "sprk_matter")), Scripted(),
            AppUpdatesTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status409Conflict, RouteDetail(result));
        RouteReason(result).Should().Be(ProjectMatterFiling.LeavesSecureCode);
        PairIdOf(project).Should().Be(secure.ToString("D"));
        _writes.Should().BeEmpty();
        _fixture.IsSecureOf(project).Should().BeTrue();
    }

    /// <summary>Item 4: a project takes the filing body only — never the child re-file, never the caller's own PATCH.</summary>
    [Theory]
    [InlineData("""{"sprk_name":"Renamed"}""")]
    [InlineData("""{"sprk_regardingrecordid":"6d1f0b8e-0000-4000-8000-000000000001"}""")]
    [InlineData("""{"sprk_regardingrecordid":"6d1f0b8e-0000-4000-8000-000000000001","sprk_regardingrecordtype@odata.bind":"/sprk_recordtype_refs(6d1f0b8e-0000-4000-8000-000000000002)","sprk_name":"x"}""")]
    [InlineData("""{"sprk_regardingrecordid":null,"sprk_regardingrecordtype@odata.bind":null}""")]
    [InlineData("""[]""")]
    public async Task ProjectFiling_Route_AnyOtherBody_Is400_NotFiling_AndNothingElseIsAsked(string json)
    {
        var project = Project();
        var user = Scripted();
        var ownership = new Mock<IRecordOwnershipResolver>(MockBehavior.Strict);
        var appOnly = AppUpdatesTheWorld();

        var result = await FileThroughTheRoute(project, JsonDocument.Parse(json).RootElement.Clone(), user, appOnly.Object, ownership);

        RouteStatus(result).Should().Be(StatusCodes.Status400BadRequest);
        RouteReason(result).Should().Be(ChildRecordEndpoints.NotFilingCode);
        user.Patches.Should().BeEmpty();
        ownership.Invocations.Should().BeEmpty("the child re-file is never reached");
        appOnly.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectFiling_Route_WithNoApplicationWriter_FailsClosed_AndNothingIsWritten()
    {
        var (_, ordinary) = Matters();
        var project = Project();
        var user = Scripted();

        var result = await FileThroughTheRoute(project, FilingBody(ordinary, RecordTypeRef(_fixture, "sprk_matter")), user, null);

        RouteStatus(result).Should().Be(StatusCodes.Status500InternalServerError);
        user.Patches.Should().BeEmpty();
        PairIdOf(project).Should().BeNull();
    }

    // ── The chat update tool: the same core ─────────────────────────────────────────────────────────────────────────

    /// <summary>Item 5: the tool's project-pair write is the core's conditional write (no <c>If-Match: *</c>); the other
    /// columns go as the caller's own PATCH.</summary>
    [Fact]
    public async Task ProjectFiling_ChatUpdate_ReFilingToAnotherOrdinaryMatter_WithOtherColumns_UsesTheConditionalWrite()
    {
        var (_, from) = Matters();
        var to = Guid.NewGuid();
        _fixture.SeedMatter(to, isSecure: false);
        var project = Project(from);
        var user = Scripted();
        user.SecuredColumns.Add(SecureRootInheritance.PairIdColumn);
        user.SecuredColumns.Add(SecureRootInheritance.PairTypeColumn);
        var appOnly = AppUpdatesTheWorld();

        var result = await ChatUpdateWith("sprk_project", project, user, new()
        {
            ["sprk_name"] = "Renamed",
            ["sprk_regardingrecordid"] = to.ToString("D"),
            ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
        }, appOnly: appOnly.Object);

        result.Success.Should().BeTrue(result.ErrorMessage);
        user.Patches.Should().ContainSingle("the caller's own PATCH carries the other columns")
            .Which.Body.Should().Contain("sprk_name").And.NotContain(SecureRootInheritance.PairIdColumn);
        appOnly.Verify(s => s.UpdateRecordFieldsIfUnchangedAsync("sprk_project", project,
            It.Is<Dictionary<string, object?>>(f => f.Count == 2 && f.ContainsKey(SecureRootInheritance.PairIdColumn)),
            It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
        NoUnconditionalWrite(appOnly);
        PairIdOf(project).Should().Be(to.ToString("D"));
    }

    [Fact]
    public async Task ProjectFiling_ChatUpdate_WhenTheProjectChangesBeforeThePairIsWritten_IsRefused_AndThePairIsNotWritten()
    {
        var (_, from) = Matters();
        var to = Guid.NewGuid();
        _fixture.SeedMatter(to, isSecure: false);
        var project = Project(from);
        var appOnly = AppUpdatesTheWorld(beforeConditionalWrite: () =>
            World.Set("sprk_project", project, "versionnumber", 99L));

        var result = await ChatUpdateWith("sprk_project", project, Scripted(), new()
        {
            ["sprk_regardingrecordid"] = to.ToString("D"),
            ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
        }, appOnly: appOnly.Object);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(SecureRootInheritance.ReasonChangedConcurrently);
        PairIdOf(project).Should().Be(from.ToString("D"));
        NoUnconditionalWrite(appOnly);
    }
}
