using FluentAssertions;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// <see cref="SpeContainerMembershipService.FindPermissionByEmail"/> — the A-13 revoke matcher itself.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists (task 025, finding H6 seam 1).</b> This matcher IS finding A-13. The
/// original defect compared the container permission's <c>userPrincipalName</c> against the contact's
/// <b>GUID</b>; an email never contains a GUID, so it matched nothing, ever — and <c>/revoke</c> then
/// reported SPE success while the ACL entry stayed exactly where it was.</para>
///
/// <para><b>And after the fix it was still executed by no test.</b> <c>SpeRevokeMatcherTests</c>
/// substitutes <see cref="SpeContainerMembershipService"/> wholesale
/// (<c>Mock&lt;SpeContainerMembershipService&gt;</c> + <c>Setup(s =&gt; s.RevokeMembershipAsync(…))</c>),
/// so it proves the ENDPOINT reacts correctly to each outcome — genuinely valuable, and untouched by
/// this file — but never runs the matcher. Replacing the comparison with <c>return false</c> failed
/// ZERO tests on 2026-09-08, measured, not inherited. Substituting at a seam proves the caller, never
/// the callee (task 017's lesson, restated by task 045).</para>
///
/// <para><b>Level.</b> The matcher is a pure function over Graph models, so these are direct calls: no
/// transport, no <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038 ban B1), no reflection (ban B8) — the
/// method was widened to <c>internal</c> instead, as <c>ExpiryPredicate</c> already is.</para>
/// </remarks>
public class SpePermissionMatcherTests
{
    private const string ContactEmail = "external.user@client-firm.com";

    private static Permission PermissionFor(string? upn, string id = "perm-1")
    {
        var permission = new Permission { Id = id };

        if (upn is null)
            return permission;

        permission.GrantedToV2 = new SharePointIdentitySet
        {
            User = new SharePointIdentity
            {
                AdditionalData = new Dictionary<string, object> { ["userPrincipalName"] = upn }
            }
        };

        return permission;
    }

    [Fact]
    public void MatchesThePermissionWhoseUpnIsTheContactsEmail()
    {
        var permissions = new List<Permission>
        {
            PermissionFor("someone.else@client-firm.com", "perm-other"),
            PermissionFor(ContactEmail, "perm-target"),
        };

        var match = SpeContainerMembershipService.FindPermissionByEmail(permissions, ContactEmail);

        match.Should().NotBeNull("membership is WRITTEN with userPrincipalName = the contact's email, "
            + "so revoke must match on that same key — the mismatch between write-key and match-key IS "
            + "finding A-13");
        match!.Id.Should().Be("perm-target");
    }

    [Fact]
    public void MatchIsCaseInsensitive_BecauseUpnsAreNotCaseSensitive()
    {
        var permissions = new List<Permission> { PermissionFor(ContactEmail.ToUpperInvariant()) };

        SpeContainerMembershipService
            .FindPermissionByEmail(permissions, ContactEmail)
            .Should().NotBeNull("a UPN differing only in case is the SAME principal; failing to match "
                + "it would leave a live ACL entry behind and report the revoke as clean");
    }

    [Fact]
    public void DoesNotMatchADifferentUser()
    {
        var permissions = new List<Permission> { PermissionFor("someone.else@client-firm.com") };

        SpeContainerMembershipService
            .FindPermissionByEmail(permissions, ContactEmail)
            .Should().BeNull("matching the wrong permission would DELETE another user's container access");
    }

    /// <summary>
    /// The original A-13 shape: a GUID is not an email, and must not match.
    /// </summary>
    /// <remarks>
    /// The defect searched for the contact's GUID inside the UPN. This pins the inverse — that a
    /// GUID-shaped needle finds nothing in an email-keyed list — so a regression back to GUID matching
    /// cannot pass by accident.
    /// </remarks>
    [Fact]
    public void DoesNotMatchAContactGuid_TheOriginalA13Defect()
    {
        var permissions = new List<Permission> { PermissionFor(ContactEmail) };

        SpeContainerMembershipService
            .FindPermissionByEmail(permissions, "9f8b6a51-1c2d-4e3f-8a9b-0c1d2e3f4a5b")
            .Should().BeNull();
    }

    [Fact]
    public void ReturnsNullForANullList()
    {
        SpeContainerMembershipService.FindPermissionByEmail(null, ContactEmail).Should().BeNull();
    }

    [Fact]
    public void SkipsPermissionsThatCarryNoUserIdentity()
    {
        // App and container-type-level grants have no GrantedToV2.User. They must be stepped over
        // rather than throwing — a container always carries some of them.
        var permissions = new List<Permission>
        {
            PermissionFor(null, "app-grant"),
            PermissionFor(ContactEmail, "perm-target"),
        };

        SpeContainerMembershipService
            .FindPermissionByEmail(permissions, ContactEmail)!
            .Id.Should().Be("perm-target");
    }
}
