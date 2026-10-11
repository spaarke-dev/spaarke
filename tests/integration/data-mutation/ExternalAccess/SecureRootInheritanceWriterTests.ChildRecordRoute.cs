using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
/// spaarke-ontology-platform-r1 task 046 (D-21; uac-r2's answer D-113): the Create Work Assignment wizard's and a decision's
/// writer — <c>POST /api/v1/child-records/sprk_workassignment</c> — is one more BFF writer of a work assignment, so it is
/// driven here like the others: its REAL handler, the host's REAL <see cref="SecureRootFilingGate"/>, the app-only create
/// landing in provisioning's fixture. A work assignment it creates under a SECURE matter is created INTO isolation and comes
/// out secure; one whose creator cannot be shared is removed again (the create is refused); under an ordinary matter it is
/// an ordinary row; a caller walled off the secure matter is refused with nothing written.
/// </summary>
public partial class SecureRootInheritanceWriterTests
{
    private Task<IResult> CreateWorkAssignmentThroughTheRoute(Guid matter, IFieldMappingDataverseService appOnly)
    {
        var payload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["sprk_name"] = "Review the lease",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter:D})",
        });

        using var scope = _fixture.Services.CreateScope();
        return ChildRecordEndpoints.CreateAsync(
            "sprk_workassignment", payload,
            new SecureChildOwnershipAiToolTests.ScriptedUserClient(Creator),
            new RecordOwnershipResolver(
                SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
                NullLogger<RecordOwnershipResolver>.Instance),
            appOnly,
            new StampWorld().Restamper,
            scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>(),
            _gate,
            // No AssignedAccessMaterializer in this composition: its inline step logs and leaves the record to the job.
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new DefaultHttpContext(), NullLogger<Program>.Instance, CancellationToken.None);
    }

    private static int? RouteStatus(IResult result) => ((IStatusCodeHttpResult)result).StatusCode;

    private static string? RouteReason(IResult result) =>
        result is ProblemHttpResult problem && problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var code)
            ? code?.ToString()
            : null;

    private static string? RouteDetail(IResult result) => (result as ProblemHttpResult)?.ProblemDetails.Detail;

    private static JsonElement RouteBody(IResult result) => JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value);

    [Fact]
    public async Task ChildRecordRoute_AWorkAssignmentUnderASecureMatter_IsCreatedIntoIsolation_AndComesOutSecure()
    {
        var (secure, _) = Matters();

        var result = await CreateWorkAssignmentThroughTheRoute(secure, AppCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        var (_, created, fields) = _writes.Should().ContainSingle().Subject;
        BoundId(fields, "ownerid@odata.bind").Should().Be(SecureTeam, "created owned by the named team — never the caller's unit first");
        fields["sprk_issecure"].Should().Be(true, "flagged in the create itself");
        BoundId(fields, "sprk_CreatedByPerson@odata.bind").Should().Be(Creator);
        RouteBody(result).GetProperty("id").GetGuid().Should().Be(created);
        RouteBody(result).TryGetProperty("warnings", out _).Should().BeFalse("securing it was completed in the request");
        ShouldBeSecure(created, "created under a secure matter through the route");
    }

    [Fact]
    public async Task ChildRecordRoute_WhenTheCreatorCannotBeShared_TheIsolatedRowIsRemoved_AndTheCreateIsRefused()
    {
        var (secure, _) = Matters();
        _fixture.FailShareForPrincipal = Creator;

        var result = await CreateWorkAssignmentThroughTheRoute(secure, AppCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status500InternalServerError);
        RouteDetail(result).Should().Contain("removed again").And.Contain("Nothing was created");
        var created = _writes.Should().ContainSingle().Subject.Id;
        World.Has("sprk_workassignment", created).Should().BeFalse("the row was deleted again");
    }

    [Fact]
    public async Task ChildRecordRoute_WhenTheCreatorCannotBeSharedNorTheRowRemoved_IsCreated_WithAWarningThatSaysSo()
    {
        var (secure, _) = Matters();
        _fixture.FailShareForPrincipal = Creator;
        World.DeletesFail = true;

        var result = await CreateWorkAssignmentThroughTheRoute(secure, AppCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, "the row exists and the client must be able to name it");
        var warnings = RouteBody(result).GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToList();
        warnings.Should().ContainSingle().Which.Should().Contain("could not be shared to you and could not be removed")
            .And.NotContain("as a secure record shared to you");
        _fixture.IsSecureOf(_writes.Single().Id).Should().BeTrue("still secure — never a business-unit-visible row");
    }

    [Fact]
    public async Task ChildRecordRoute_AWorkAssignmentUnderAnOrdinaryMatter_StaysOrdinary()
    {
        var (_, ordinary) = Matters();

        var result = await CreateWorkAssignmentThroughTheRoute(ordinary, AppCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status201Created, RouteDetail(result));
        ShouldNotBeSecured(_writes.Should().ContainSingle().Subject.Id, "filed under an ordinary matter");
    }

    [Fact]
    public async Task ChildRecordRoute_WhenTheCallerIsOnTheSecureMattersNoAccessList_Is403_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure);

        var result = await CreateWorkAssignmentThroughTheRoute(secure, AppCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status403Forbidden);
        RouteReason(result).Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        _writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRecordRoute_UnderAMatterWhoseFlagCannotBeRead_Is409_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        FlagUnreadable(secure);

        var result = await CreateWorkAssignmentThroughTheRoute(secure, AppCreatesIntoTheWorld().Object);

        RouteStatus(result).Should().Be(StatusCodes.Status409Conflict);
        RouteReason(result).Should().Be(RecordOwnerRefusal.ParentUndetermined);
        _writes.Should().BeEmpty();
    }
}
