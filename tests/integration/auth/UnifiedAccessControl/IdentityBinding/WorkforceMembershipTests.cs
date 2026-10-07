using FluentAssertions;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// unified-access-control-r2 task 141 — the workforce member test that gates the first-sign-in email bind and
/// contact creation, and the per-deployment customer-tenant setting it reads (owner decision I1 = (b)).
/// </summary>
public class WorkforceMembershipTests
{
    private static readonly IReadOnlySet<Guid> OnlyT = new HashSet<Guid> { CustomerTenant };

    [Fact]
    public void AUserTokenFromAConfiguredTenantWithAcctZero_IsAMember()
        => WorkforceMembershipTest.Evaluate(CallerKind.UserDelegated, CustomerTenant.ToString(), "0", OnlyT)
            .Should().Be(WorkforceMembership.Member);

    [Theory]
    [InlineData(CallerKind.Application)]
    [InlineData(CallerKind.Indeterminate)]
    public void ANonUserToken_IsNeverAMember_WhateverElseItCarries(CallerKind kind)
        => WorkforceMembershipTest.Evaluate(kind, CustomerTenant.ToString(), "0", OnlyT)
            .Should().Be(WorkforceMembership.NotUserToken);

    [Fact]
    public void AnAppOnlyTokenWithNoIdtyp_IsClassifiedByCallerIdentity_AndIsNotAMember()
    {
        // idtyp is optional and NOT configured on this repo's registrations; the classifier must still say
        // Application, from sub == oid, so an app-only token is never bound.
        var token = AppOnlyToken(Guid.NewGuid(), CustomerTenant);
        var claims = WorkforceCallerClaims.From(token);

        claims.Kind.Should().Be(CallerKind.Application);
        WorkforceMembershipTest.Evaluate(claims.Kind, claims.TenantId, claims.Acct, OnlyT)
            .Should().Be(WorkforceMembership.NotUserToken);
    }

    [Fact]
    public void AnEmptyTenantList_MakesNobodyAMember()
        => WorkforceMembershipTest.Evaluate(CallerKind.UserDelegated, CustomerTenant.ToString(), "0", new HashSet<Guid>())
            .Should().Be(WorkforceMembership.TenantListEmpty);

    [Fact]
    public void Model1_AMemberOfTheRegistrationsOwnTenant_IsNotAMember_AndAMemberOfTheCustomerTenantIs()
    {
        // T configured as the customer tenant; the app registration (AzureAd:TenantId) lives in S. The member test
        // never consults AzureAd:TenantId — S's staff are a FOREIGN tenant here, T's employees are members.
        WorkforceMembershipTest.Evaluate(CallerKind.UserDelegated, SpaarkeTenant.ToString(), "0", OnlyT)
            .Should().Be(WorkforceMembership.ForeignTenant);
        WorkforceMembershipTest.Evaluate(CallerKind.UserDelegated, CustomerTenant.ToString(), "0", OnlyT)
            .Should().Be(WorkforceMembership.Member);
    }

    [Theory]
    [InlineData(null, WorkforceMembership.AcctMissing)]
    [InlineData("", WorkforceMembership.AcctMissing)]
    [InlineData("1", WorkforceMembership.Guest)]
    [InlineData("2", WorkforceMembership.AcctUnrecognized)]
    [InlineData("member", WorkforceMembership.AcctUnrecognized)]
    public void Acct_OnlyZeroIsAMember_AndAbsenceFailsClosed(string? acct, WorkforceMembership expected)
        => WorkforceMembershipTest.Evaluate(CallerKind.UserDelegated, CustomerTenant.ToString(), acct, OnlyT)
            .Should().Be(expected);

    [Fact]
    public void AnUnparseableTid_IsForeign()
        => WorkforceMembershipTest.Evaluate(CallerKind.UserDelegated, "not-a-tenant", "0", OnlyT)
            .Should().Be(WorkforceMembership.ForeignTenant);

    [Fact]
    public void MembershipIsNeverInferredFromTheEmailDomainOrAnExtUpn()
    {
        // A token from a configured tenant with an #EXT#-shaped UPN but acct=0 is a member by acct — the UPN is
        // not consulted. And a guest whose email is in the customer's domain is still a guest.
        var claims = WorkforceCallerClaims.From(WorkforceUser(Guid.NewGuid(), CustomerTenant, acct: "1",
            email: "employee@customer.example"));
        WorkforceMembershipTest.Evaluate(claims.Kind, claims.TenantId, claims.Acct, OnlyT)
            .Should().Be(WorkforceMembership.Guest);
    }

    [Fact]
    public void EveryNonMemberOutcome_HasItsOwnDenyCode()
    {
        var codes = Enum.GetValues<WorkforceMembership>()
            .Where(m => m != WorkforceMembership.Member)
            .Select(WorkforceMembershipTest.DenyCodeFor)
            .ToList();

        codes.Should().OnlyHaveUniqueItems().And.AllSatisfy(c => c.Should().StartWith("sdap.access.deny.workforce_"));
        WorkforceMembershipTest.DenyCodeFor(WorkforceMembership.Member).Should().BeNull();
    }

    // ── The setting ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validator_AnEmptyListIsValid_ItMeansDeny()
        => WorkforceIdentityOptionsValidator.Validate(new WorkforceIdentityOptions(), ciamTenantId: null)
            .Succeeded.Should().BeTrue();

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("#{CUSTOMER_WORKFORCE_TENANT_ID}#")]
    public void Validator_AMalformedEntry_FailsStartup(string entry)
        => WorkforceIdentityOptionsValidator.Validate(
                new WorkforceIdentityOptions { CustomerTenantIds = { entry } }, ciamTenantId: null)
            .Failed.Should().BeTrue();

    [Fact]
    public void Validator_TheCiamTenant_IsRefused()
    {
        var ciam = Guid.NewGuid();
        WorkforceIdentityOptionsValidator.Validate(
                new WorkforceIdentityOptions { CustomerTenantIds = { ciam.ToString() } }, ciam.ToString())
            .Failed.Should().BeTrue("external (CIAM) users are never workforce members");
    }

    [Fact]
    public void ParsedTenantIds_IgnoreWhatCannotBeATenant()
        => new WorkforceIdentityOptions { CustomerTenantIds = { CustomerTenant.ToString().ToUpperInvariant(), "junk", " " } }
            .ParsedCustomerTenantIds().Should().Equal(CustomerTenant);
}
