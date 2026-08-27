// -----------------------------------------------------------------------------
// CallerIdentityTests.cs
//
// Unit tests for Spaarke.Core.Auth.CallerIdentity — the single caller-kind
// classifier (unified-access-control-r2 task 081).
//
// The centrepiece is TheTrap_*: a USER-DELEGATED token carrying an appid that a
// route's operator allow-list contains. An allow-list keyed on appid alone would
// admit it, because appid/azp names the client application and is present in
// user-delegated tokens too. That test is the reason this type exists, and the
// perturbation record in notes/task-081-caller-classification.md shows it going
// RED when the positive app-only determination is removed.
//
// Claim shapes are written in BOTH the short JWT form and the mapped WS-Fed URI
// form, because Microsoft.IdentityModel's inbound claim mapping is process-global
// state and a classifier that reads only one form is an environment-dependent bug.
// -----------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Spaarke.Core.Auth;
using Xunit;

namespace Spaarke.Core.Tests.Auth;

public sealed class CallerIdentityTests
{
    private const string UserObjectId = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string ServicePrincipalObjectId = "bbbbbbbb-5555-6666-7777-888888888888";
    private const string OperatorAppId = "cccccccc-9999-0000-1111-222222222222";
    private const string TenantId = "dddddddd-3333-4444-5555-666666666666";

    // A v2 user token's `sub` is a pairwise subject identifier scoped to (user, application).
    // It is deliberately NOT equal to the user's oid — that inequality is what the structural
    // app-only signal relies on.
    private const string PairwiseSubject = "L1cAbCdEf-pairwise-subject-not-an-object-id";

    // ---------------------------------------------------------------- the trap

    [Fact]
    public void TheTrap_UserDelegatedTokenCarryingAnOperatorAppId_IsNotClassifiedAsApplication()
    {
        // A human interactively signed into the operator's app registration. Its appid is
        // EXACTLY the value an operator allow-list would contain.
        var principal = Authenticated(
            ("appid", OperatorAppId),
            ("scp", "user_impersonation"),
            ("oid", UserObjectId),
            ("sub", PairwiseSubject),
            ("tid", TenantId));

        var caller = CallerIdentity.FromPrincipal(principal);

        caller.Kind.Should().Be(CallerKind.UserDelegated);
        caller.IsApplication.Should().BeFalse(
            "appid names the CLIENT APPLICATION, never the caller's kind — a gate keyed on appid " +
            "alone would hand this human the operator capability");

        // The app id is still surfaced (audit logging wants it); it simply proves nothing on its own.
        caller.ApplicationId.Should().Be(OperatorAppId);
    }

    // ------------------------------------------------- positive app-only shapes

    [Fact]
    public void AppOnlyToken_SubEqualsOid_IsApplication()
    {
        // The shape the L2 managed-identity probe actually presents: no idtyp (this platform's
        // Entra provisioning configures no optional claims), sub == oid == the SP object id.
        var principal = Authenticated(
            ("appid", OperatorAppId),
            ("oid", ServicePrincipalObjectId),
            ("sub", ServicePrincipalObjectId),
            ("tid", TenantId));

        var caller = CallerIdentity.FromPrincipal(principal);

        caller.Kind.Should().Be(CallerKind.Application);
        caller.ApplicationId.Should().Be(OperatorAppId);
        caller.ObjectId.Should().Be(ServicePrincipalObjectId);
        caller.TenantId.Should().Be(TenantId);
        caller.DeterminationReason.Should().Contain("sub equals oid");
    }

    [Fact]
    public void AppOnlyToken_SubEqualsOidDifferingOnlyByCase_IsApplication()
    {
        // GUID casing is not semantic; a case-sensitive compare here would misclassify a real
        // app-only token as Indeterminate and break the probe.
        var principal = Authenticated(
            ("appid", OperatorAppId),
            ("oid", ServicePrincipalObjectId.ToUpperInvariant()),
            ("sub", ServicePrincipalObjectId.ToLowerInvariant()));

        CallerIdentity.FromPrincipal(principal).Kind.Should().Be(CallerKind.Application);
    }

    [Fact]
    public void AppOnlyToken_IdtypApp_IsApplication()
    {
        var principal = Authenticated(
            ("idtyp", "app"),
            ("appid", OperatorAppId),
            ("oid", ServicePrincipalObjectId));

        var caller = CallerIdentity.FromPrincipal(principal);

        caller.Kind.Should().Be(CallerKind.Application);
        caller.DeterminationReason.Should().Be("idtyp=app");
    }

    [Fact]
    public void AppOnlyToken_V2AzpClaimInsteadOfAppid_StillResolvesApplicationId()
    {
        // v1 tokens emit `appid`; v2 tokens emit `azp`. Both name the calling app.
        var principal = Authenticated(
            ("azp", OperatorAppId),
            ("oid", ServicePrincipalObjectId),
            ("sub", ServicePrincipalObjectId));

        var caller = CallerIdentity.FromPrincipal(principal);

        caller.Kind.Should().Be(CallerKind.Application);
        caller.ApplicationId.Should().Be(OperatorAppId);
    }

    [Fact]
    public void AppOnlyToken_MappedUriClaimForms_AreUnderstood()
    {
        // The MapInboundClaims=true world. Same token, URI claim names.
        var principal = Authenticated(
            ("http://schemas.microsoft.com/identity/claims/appid", OperatorAppId),
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", ServicePrincipalObjectId),
            (ClaimTypes.NameIdentifier, ServicePrincipalObjectId),
            ("http://schemas.microsoft.com/identity/claims/tenantid", TenantId));

        var caller = CallerIdentity.FromPrincipal(principal);

        caller.Kind.Should().Be(CallerKind.Application);
        caller.ApplicationId.Should().Be(OperatorAppId);
        caller.TenantId.Should().Be(TenantId);
    }

    // ------------------------------------------------ positive user-side shapes

    [Fact]
    public void UserToken_DelegatedScopeAndOid_IsUserDelegated()
    {
        var principal = Authenticated(
            ("scp", "Files.Read"),
            ("oid", UserObjectId),
            ("sub", PairwiseSubject));

        var caller = CallerIdentity.FromPrincipal(principal);

        caller.Kind.Should().Be(CallerKind.UserDelegated);
        caller.IsUserDelegated.Should().BeTrue();
    }

    [Fact]
    public void UserToken_MappedUriScopeClaim_IsUserDelegated()
    {
        var principal = Authenticated(
            ("http://schemas.microsoft.com/identity/claims/scope", "Files.Read"),
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", UserObjectId));

        CallerIdentity.FromPrincipal(principal).Kind.Should().Be(CallerKind.UserDelegated);
    }

    [Fact]
    public void UserToken_IdtypUserWithoutScope_IsUserDelegated()
    {
        var principal = Authenticated(("idtyp", "user"), ("oid", UserObjectId));

        CallerIdentity.FromPrincipal(principal).Kind.Should().Be(CallerKind.UserDelegated);
    }

    [Fact]
    public void ContradictoryToken_IdtypAppWithDelegatedScope_ResolvesToUserDelegated()
    {
        // Entra does not issue this combination. If one ever appears, the fail-closed reading for an
        // authorization gate is "there may be a user behind this" — so the delegated-scope branch is
        // deliberately evaluated BEFORE any application branch.
        var principal = Authenticated(
            ("idtyp", "app"),
            ("scp", "user_impersonation"),
            ("oid", UserObjectId),
            ("appid", OperatorAppId));

        CallerIdentity.FromPrincipal(principal).Kind.Should().Be(CallerKind.UserDelegated);
    }

    // ------------------------------------------------------- fail-closed shapes

    [Fact]
    public void NullPrincipal_IsIndeterminate()
    {
        var caller = CallerIdentity.FromPrincipal(null);

        caller.Kind.Should().Be(CallerKind.Indeterminate);
        caller.IsApplication.Should().BeFalse();
        caller.IsUserDelegated.Should().BeFalse();
    }

    [Fact]
    public void UnauthenticatedPrincipal_IsIndeterminate()
    {
        // No authenticationType ⇒ ClaimsIdentity.IsAuthenticated is false, even with rich claims.
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("appid", OperatorAppId),
            new Claim("oid", ServicePrincipalObjectId),
            new Claim("sub", ServicePrincipalObjectId),
        });

        CallerIdentity.FromPrincipal(new ClaimsPrincipal(identity)).Kind
            .Should().Be(CallerKind.Indeterminate);
    }

    [Fact]
    public void DelegatedScopeWithoutAnyUserObjectId_IsIndeterminate_NotApplication()
    {
        // A delegated scope with no user behind it is a shape we do not model. It must NOT fall
        // through to an application branch.
        var principal = Authenticated(("scp", "Files.Read"), ("appid", OperatorAppId));

        var caller = CallerIdentity.FromPrincipal(principal);

        caller.Kind.Should().Be(CallerKind.Indeterminate);
        caller.IsApplication.Should().BeFalse();
    }

    [Fact]
    public void TokenWithNoDeterminativeClaims_IsIndeterminate_NotApplication()
    {
        // The whole point of "positive determination": absence of a user claim must never be read
        // as evidence of a service principal.
        var principal = Authenticated(("appid", OperatorAppId), ("tid", TenantId));

        var caller = CallerIdentity.FromPrincipal(principal);

        caller.Kind.Should().Be(CallerKind.Indeterminate);
        caller.IsApplication.Should().BeFalse(
            "app-only must never be inferred from the absence of scp/oid — a malformed or " +
            "unmodelled token would otherwise be handed service-principal authority");
    }

    [Fact]
    public void BlankSubAndBlankOid_AreNotTreatedAsEqual()
    {
        // Guards the string.Equals("", "") == true hazard: two empty claims must not satisfy the
        // sub == oid signal and promote a claimless token to Application.
        var principal = Authenticated(("appid", OperatorAppId), ("sub", ""), ("oid", ""));

        var caller = CallerIdentity.FromPrincipal(principal);

        caller.Kind.Should().Be(CallerKind.Indeterminate);
        caller.ObjectId.Should().BeNull("a blank claim is reported as absent, not as an empty value");
    }

    [Fact]
    public void SubPresentButOidAbsent_IsIndeterminate()
    {
        var principal = Authenticated(("appid", OperatorAppId), ("sub", ServicePrincipalObjectId));

        CallerIdentity.FromPrincipal(principal).Kind.Should().Be(CallerKind.Indeterminate);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Builds an AUTHENTICATED principal. The non-null authenticationType is what makes
    /// <c>ClaimsIdentity.IsAuthenticated</c> true — omitting it is how the unauthenticated case above
    /// is constructed, so it is never defaulted silently.
    /// </summary>
    private static ClaimsPrincipal Authenticated(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "TestJwt"));
}
