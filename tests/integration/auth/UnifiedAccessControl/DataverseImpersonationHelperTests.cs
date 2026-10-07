using System.Net.Http;
using FluentAssertions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The fail-closed contract of the shared Dataverse impersonation helper (unified-access-control-r2 task 104,
/// #990; prerequisite P3 of ADR-052 §6).
/// </summary>
/// <remarks>
/// <para>Replaces the 2026-07-16 messaging tests, which pinned the opposite for an empty id ("adds no header").
/// That silent no-op is how a new call site could degrade to an app-only, unscoped query that still returns
/// HTTP 200.</para>
/// <para>Verified on the per-request message the helper stamps, with no transport (ADR-038 bans
/// <c>Mock&lt;HttpMessageHandler&gt;</c>). What the production request builder then sends is pinned in
/// <see cref="DataverseWebApiServiceImpersonationTests"/>.</para>
/// </remarks>
public class DataverseImpersonationHelperTests
{
    private static readonly Guid SystemUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid EntraObjectId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid OrgTenant = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid OtherTenant = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private static HttpRequestMessage NewRequest() => new(HttpMethod.Get, "sprk_matters");

    [Fact]
    public void ApplyAsSystemUser_WithSystemUserId_StampsOnlyMscrmCallerId()
    {
        using var request = NewRequest();

        DataverseImpersonation.ApplyAsSystemUser(request, SystemUserId);

        // The wire value is the contract Dataverse reads. Pinned as a literal so a typo in the constant fails here,
        // not only in the live canary.
        DataverseImpersonation.CallerIdHeader.Should().Be("MSCRMCallerID");
        request.Headers.GetValues("MSCRMCallerID")
            .Should().ContainSingle().Which.Should().Be(SystemUserId.ToString());
        request.Headers.Contains(DataverseImpersonation.CallerObjectIdHeader).Should().BeFalse();
    }

    /// <summary>The load-bearing refusal: an empty id is an error, never "send it app-only".</summary>
    [Fact]
    public void ApplyAsSystemUser_WithEmptyId_ThrowsAndStampsNothing()
    {
        using var request = NewRequest();

        var act = () => DataverseImpersonation.ApplyAsSystemUser(request, Guid.Empty);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("systemUserId");
        act.Should().Throw<ArgumentException>().WithMessage("*fail closed*");
        request.Headers.Contains(DataverseImpersonation.CallerIdHeader).Should().BeFalse();
        request.Headers.Contains(DataverseImpersonation.CallerObjectIdHeader).Should().BeFalse();
    }

    [Fact]
    public void ApplyAsEntraUser_WhenTokenTenantIsTheOrgTenant_StampsOnlyCallerObjectId()
    {
        using var request = NewRequest();

        DataverseImpersonation.ApplyAsEntraUser(request, EntraObjectId, OrgTenant, OrgTenant);

        DataverseImpersonation.CallerObjectIdHeader.Should().Be("CallerObjectId");
        request.Headers.GetValues(DataverseImpersonation.CallerObjectIdHeader)
            .Should().ContainSingle().Which.Should().Be(EntraObjectId.ToString());
        request.Headers.Contains(DataverseImpersonation.CallerIdHeader).Should().BeFalse();
    }

    [Theory]
    [InlineData("entraObjectId")]
    [InlineData("callerTenantId")]
    [InlineData("dataverseTenantId")]
    public void ApplyAsEntraUser_WithAnEmptyId_ThrowsNamingItAndStampsNothing(string emptyParameter)
    {
        using var request = NewRequest();
        var objectId = emptyParameter == "entraObjectId" ? Guid.Empty : EntraObjectId;
        var callerTenant = emptyParameter == "callerTenantId" ? Guid.Empty : OrgTenant;
        var orgTenant = emptyParameter == "dataverseTenantId" ? Guid.Empty : OrgTenant;

        var act = () => DataverseImpersonation.ApplyAsEntraUser(request, objectId, callerTenant, orgTenant);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be(emptyParameter);
        request.Headers.Contains(DataverseImpersonation.CallerObjectIdHeader).Should().BeFalse();
    }

    /// <summary>
    /// Microsoft does not document what Dataverse does with an object id from another tenant, so the helper
    /// refuses it rather than finding out in production.
    /// </summary>
    [Fact]
    public void ApplyAsEntraUser_WhenTokenTenantIsNotTheOrgTenant_ThrowsAndStampsNothing()
    {
        using var request = NewRequest();

        var act = () => DataverseImpersonation.ApplyAsEntraUser(request, EntraObjectId, OtherTenant, OrgTenant);

        act.Should().Throw<InvalidOperationException>().WithMessage("*fail closed*");
        request.Headers.Contains(DataverseImpersonation.CallerObjectIdHeader).Should().BeFalse();
    }

    /// <summary>
    /// Exactly one impersonation identity per request: what Dataverse does with both headers is not documented,
    /// so switching identity type must replace, never add.
    /// </summary>
    [Fact]
    public void ApplyAsEntraUser_AfterApplyAsSystemUser_LeavesOnlyCallerObjectId()
    {
        using var request = NewRequest();

        DataverseImpersonation.ApplyAsSystemUser(request, SystemUserId);
        DataverseImpersonation.ApplyAsEntraUser(request, EntraObjectId, OrgTenant, OrgTenant);

        request.Headers.Contains(DataverseImpersonation.CallerIdHeader).Should().BeFalse();
        request.Headers.GetValues(DataverseImpersonation.CallerObjectIdHeader)
            .Should().ContainSingle().Which.Should().Be(EntraObjectId.ToString());
    }

    [Fact]
    public void ApplyAsSystemUser_AfterApplyAsEntraUser_LeavesOnlyMscrmCallerId()
    {
        using var request = NewRequest();

        DataverseImpersonation.ApplyAsEntraUser(request, EntraObjectId, OrgTenant, OrgTenant);
        DataverseImpersonation.ApplyAsSystemUser(request, SystemUserId);

        request.Headers.Contains(DataverseImpersonation.CallerObjectIdHeader).Should().BeFalse();
        request.Headers.GetValues(DataverseImpersonation.CallerIdHeader)
            .Should().ContainSingle().Which.Should().Be(SystemUserId.ToString());
    }

    [Fact]
    public void ApplyAsSystemUser_CalledTwice_ReplacesRatherThanAccumulatesTheIdentity()
    {
        using var request = NewRequest();
        var second = Guid.Parse("55555555-5555-5555-5555-555555555555");

        DataverseImpersonation.ApplyAsSystemUser(request, SystemUserId);
        DataverseImpersonation.ApplyAsSystemUser(request, second);

        request.Headers.GetValues(DataverseImpersonation.CallerIdHeader)
            .Should().ContainSingle().Which.Should().Be(second.ToString());
    }
}
