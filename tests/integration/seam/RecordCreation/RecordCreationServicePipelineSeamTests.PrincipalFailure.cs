using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 018b-1s, G2 (decisions.md, "018b-1 inventory: answers to G1-G7"): the step-1
/// <see cref="RecordCreationRefusalKind.PrincipalUnresolved"/> refusal carries the WhoAmI failure as
/// <see cref="RecordCreationRefusal.ClientFailure"/>, so an adapter can tell "no token" (<c>UserContextRequired</c>,
/// <c>OboNotConfigured</c>, <c>OboExchangeFailed</c>) from "no Dataverse user" (<c>AccessDenied</c>). A PARTIAL of the
/// pipeline seam fixture, which already scripts the caller's WhoAmI. The refusal's code and status are unchanged.
/// </summary>
public sealed partial class RecordCreationServicePipelineSeamTests
{
    [Theory]
    [InlineData(DataverseUserClientErrorCodes.AccessDenied, 403)]
    [InlineData(DataverseUserClientErrorCodes.UserContextRequired, 401)]
    [InlineData(DataverseUserClientErrorCodes.OboExchangeFailed, 401)]
    [InlineData(DataverseUserClientErrorCodes.OboNotConfigured, 500)]
    public async Task CreateAsync_PrincipalUnresolved_CarriesTheWhoAmIFailureKind_AndKeepsItsCodeAndStatus(string errorCode, int upstreamStatus)
    {
        var failure = DataverseUserResponse.Fail(upstreamStatus, errorCode, "0x80040220 detail that must not be rendered");
        _user.WhoAmIFailure = failure;

        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        var refusal = outcome.Refusal!;
        refusal.Kind.Should().Be(RecordCreationRefusalKind.PrincipalUnresolved);
        refusal.ClientFailure.Should().BeSameAs(failure);
        refusal.ClientFailure!.ErrorCode.Should().Be(errorCode, "each of the four kinds round-trips");
        refusal.Code.Should().Be(Sprk.Bff.Api.Api.ChildRecordEndpoints.CallerUnresolvedReasonCode, "the code does not follow the kind");
        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status403Forbidden, "the status does not follow the kind either");
        problem.Detail.Should().NotContain("0x80040220");
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AWhoAmIFailureThatIsNotAnUnresolvedCaller_IsStillAnUpstreamRefusal_NotPrincipalUnresolved()
    {
        _user.WhoAmIFailure = DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "down");

        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Upstream);
        outcome.Refusal.ClientFailure!.ErrorCode.Should().Be(DataverseUserClientErrorCodes.ServiceError);
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ARefusalOtherThanPrincipalUnresolved_CarriesNoClientFailure()
    {
        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", createdon = "2020-01-01T00:00:00Z" }), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.BlockedColumn);
        outcome.Refusal.ClientFailure.Should().BeNull();
    }
}
