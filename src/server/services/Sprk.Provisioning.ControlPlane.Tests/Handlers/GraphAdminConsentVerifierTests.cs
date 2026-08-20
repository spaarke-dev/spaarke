// -----------------------------------------------------------------------------
// GraphAdminConsentVerifierTests.cs
//
// Task 130 (Wave G-3) — pure-logic unit tests over
// GraphAdminConsentVerifier.EvaluateGrantedScopes, the scope-matching decision
// function extracted from VerifyAsync so it can be exercised WITHOUT a live
// or fake Graph transport (parity with GraphAppRegistrationProvisioner.cs's
// documented "collaborator I/O untested elsewhere in this project" posture —
// this file tests the DECISION LOGIC only, not the Graph HTTP calls).
//
// This is the direct test of DS-4 §3's primary defect finding: the RETIRED
// NullAdminConsentVerifier always returned Verified unconditionally ("the
// consent gate can advance on fiction"). These tests prove the REAL
// evaluation logic returns Pending when the required delegated scopes are
// NOT present and Verified only when they ALL are.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class GraphAdminConsentVerifierTests
{
    private const string ServicePrincipalId = "sp-object-id-0000-0000-000000000000";
    private const int ExpectedScopeCount = 5;

    [Fact]
    public void EvaluateGrantedScopes_NoGrantsAtAll_ReturnsPending_NotVerified()
    {
        var result = GraphAdminConsentVerifier.EvaluateGrantedScopes(
            ServicePrincipalId, grantScopeStrings: Array.Empty<string>(), ExpectedScopeCount);

        var pending = result.Should().BeOfType<AdminConsentVerificationResult.Pending>().Subject;
        pending.GrantedCount.Should().Be(0);
        pending.ExpectedCount.Should().Be(ExpectedScopeCount);
        pending.Diagnostic.Should().Contain("Files.ReadWrite.All");
        pending.Diagnostic.Should().Contain("user_impersonation");
    }

    [Fact]
    public void EvaluateGrantedScopes_MissingOneOfFive_ReturnsPending_ListsExactlyTheMissingOne()
    {
        // 4 of 5 present (all Graph delegated scopes, missing Dynamics user_impersonation).
        var grantStrings = new[] { "Files.ReadWrite.All Sites.ReadWrite.All User.Read Mail.Send" };

        var result = GraphAdminConsentVerifier.EvaluateGrantedScopes(ServicePrincipalId, grantStrings, ExpectedScopeCount);

        var pending = result.Should().BeOfType<AdminConsentVerificationResult.Pending>().Subject;
        pending.GrantedCount.Should().Be(4);
        pending.Diagnostic.Should().Contain("user_impersonation");
        pending.Diagnostic.Should().NotContain("Files.ReadWrite.All is missing");
    }

    [Fact]
    public void EvaluateGrantedScopes_AllFivePresentAcrossMultipleGrantRecords_ReturnsVerified()
    {
        // Real Graph shape: multiple oauth2PermissionGrant records (one per
        // resource) each carry a space-delimited Scope string — the Graph
        // resource's 4 scopes in one grant, the Dynamics resource's 1 scope
        // in a second grant.
        var grantStrings = new[]
        {
            "Files.ReadWrite.All Sites.ReadWrite.All User.Read Mail.Send",
            "user_impersonation",
        };

        var result = GraphAdminConsentVerifier.EvaluateGrantedScopes(ServicePrincipalId, grantStrings, ExpectedScopeCount);

        var verified = result.Should().BeOfType<AdminConsentVerificationResult.Verified>().Subject;
        verified.GrantedCount.Should().Be(ExpectedScopeCount);
        verified.ExpectedCount.Should().Be(ExpectedScopeCount);
        verified.Evidence.Should().NotBeNull();
    }

    [Fact]
    public void EvaluateGrantedScopes_ExtraUnrelatedScopesPresent_StillVerifiedWhenAllRequiredPresent()
    {
        // A tenant admin may have consented to MORE than the 5 required
        // scopes (e.g. a broader admin-consent click) — extras must not
        // cause a false Pending.
        var grantStrings = new[]
        {
            "Files.ReadWrite.All Sites.ReadWrite.All User.Read Mail.Send openid profile offline_access",
            "user_impersonation",
        };

        var result = GraphAdminConsentVerifier.EvaluateGrantedScopes(ServicePrincipalId, grantStrings, ExpectedScopeCount);

        result.Should().BeOfType<AdminConsentVerificationResult.Verified>();
    }

    [Fact]
    public void EvaluateGrantedScopes_EmptyOrWhitespaceScopeStringsIgnored_NoThrow()
    {
        var grantStrings = new[] { "", "   ", "Files.ReadWrite.All Sites.ReadWrite.All User.Read Mail.Send user_impersonation" };

        var result = GraphAdminConsentVerifier.EvaluateGrantedScopes(ServicePrincipalId, grantStrings, ExpectedScopeCount);

        result.Should().BeOfType<AdminConsentVerificationResult.Verified>();
    }
}
