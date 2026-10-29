using FluentAssertions;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// <see cref="SpeContainerMembershipService.FindContactPermissions"/> — the A-13 revoke matcher itself, keyed by
/// identity since #2105 (the bound object id, then the email against the permission's email and UPN; never a
/// Spaarke-managed marker grant).
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
    private static readonly Guid ContactObjectId = Guid.Parse("0d0d0d0d-2105-4000-8000-000000000001");
    private static readonly IReadOnlySet<string> NoneManaged = new HashSet<string>();

    private static SpeMembershipKey ByEmail(string email) => new(null, email);

    private static Permission PermissionFor(string? upn, string id = "perm-1", string? email = null, Guid? userId = null)
    {
        var permission = new Permission { Id = id };

        if (upn is null && email is null && userId is null)
            return permission;

        var data = new Dictionary<string, object>();
        if (upn is not null) data["userPrincipalName"] = upn;
        if (email is not null) data["email"] = email;

        permission.GrantedToV2 = new SharePointIdentitySet
        {
            User = new SharePointIdentity { Id = userId?.ToString(), AdditionalData = data }
        };

        return permission;
    }

    /// <summary>The single match, or null when there is none. #2104 round 2 (K2): the matcher returns EVERY match under
    /// the deciding key; these single-match tests assert there is at most one (the ambiguity tests call it directly).</summary>
    private static Permission? Find(IReadOnlyList<Permission>? permissions, SpeMembershipKey key,
        IReadOnlySet<string>? managed = null)
        => SpeContainerMembershipService.FindContactPermissions(permissions, key, managed ?? NoneManaged).SingleOrDefault();

    [Fact]
    public void MatchesThePermissionWhoseUpnIsTheContactsEmail()
    {
        var permissions = new List<Permission>
        {
            PermissionFor("someone.else@client-firm.com", "perm-other"),
            PermissionFor(ContactEmail, "perm-target"),
        };

        var match = Find(permissions, ByEmail(ContactEmail));

        match.Should().NotBeNull("a permission added by UPN = the contact's email is the contact's, so revoke must "
            + "match on that same key — the mismatch between write-key and match-key IS finding A-13");
        match!.Id.Should().Be("perm-target");
    }

    [Fact]
    public void MatchIsCaseInsensitive_BecauseUpnsAreNotCaseSensitive()
    {
        var permissions = new List<Permission> { PermissionFor(ContactEmail.ToUpperInvariant()) };

        Find(permissions, ByEmail(ContactEmail))
            .Should().NotBeNull("a UPN differing only in case is the SAME principal; failing to match "
                + "it would leave a live ACL entry behind and report the revoke as clean");
    }

    [Fact]
    public void DoesNotMatchADifferentUser()
    {
        var permissions = new List<Permission> { PermissionFor("someone.else@client-firm.com") };

        Find(permissions, ByEmail(ContactEmail))
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

        Find(permissions, ByEmail("9f8b6a51-1c2d-4e3f-8a9b-0c1d2e3f4a5b")).Should().BeNull();
    }

    [Fact]
    public void ReturnsNullForANullList()
    {
        Find(null, ByEmail(ContactEmail)).Should().BeNull();
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

        Find(permissions, ByEmail(ContactEmail))!.Id.Should().Be("perm-target");
    }

    // ── #2105 ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// #2105: a Spaarke-managed marker grant (standing / JIT / demo writer) is NEVER a contact's, whatever its UPN or
    /// email. An internal user who is also a contact with the same address used to lose that role on a contact revoke,
    /// leaving its marker dangling. Both address fields are pinned: the managed permission is skipped on either.
    /// </summary>
    [Theory]
    [InlineData("userPrincipalName")]
    [InlineData("email")]
    public void NeverMatchesASpaarkeManagedPermission_EvenWhenItsAddressMatches(string field)
    {
        var managedPermission = field == "email"
            ? PermissionFor("internal.user@spaarke.example", "perm-managed", email: ContactEmail)
            : PermissionFor(ContactEmail, "perm-managed");

        Find(new List<Permission> { managedPermission }, ByEmail(ContactEmail),
                new HashSet<string> { "perm-managed" })
            .Should().BeNull("a marker-owned permission belongs to the sync / demo expiry, never to a contact revoke");
    }

    /// <summary>#2105: the managed exclusion holds for an object-id match too.</summary>
    [Fact]
    public void NeverMatchesASpaarkeManagedPermission_EvenByObjectId()
    {
        var permissions = new List<Permission> { PermissionFor(null, "perm-managed", userId: ContactObjectId) };

        Find(permissions, new SpeMembershipKey(ContactObjectId, null), new HashSet<string> { "perm-managed" })
            .Should().BeNull();
    }

    /// <summary>
    /// #2105: beside a managed permission with the same address, the contact's OWN unmanaged permission is the one
    /// found — the exclusion skips, it does not stop the search.
    /// </summary>
    [Fact]
    public void SkipsTheManagedPermissionAndFindsTheContactsOwn()
    {
        var permissions = new List<Permission>
        {
            PermissionFor(ContactEmail, "perm-managed"),
            PermissionFor(ContactEmail, "perm-contact"),
        };

        Find(permissions, ByEmail(ContactEmail), new HashSet<string> { "perm-managed" })!
            .Id.Should().Be("perm-contact");
    }

    /// <summary>#2105: the bound Entra object id matches the permission's <c>grantedToV2.user.id</c>.</summary>
    [Fact]
    public void MatchesByTheBoundObjectId()
    {
        var permissions = new List<Permission>
        {
            PermissionFor("someone.else@client-firm.com", "perm-other", userId: Guid.NewGuid()),
            PermissionFor("unrelated.upn@client-firm.com", "perm-target", userId: ContactObjectId),
        };

        Find(permissions, new SpeMembershipKey(ContactObjectId, null))!.Id.Should().Be("perm-target");
    }

    /// <summary>
    /// #2105: the legacy <c>grantedTo.user.id</c> is read too, so a permission Graph lists only in the legacy shape is
    /// still matched by object id.
    /// </summary>
    [Fact]
    public void MatchesByObjectIdOnTheLegacyGrantedToShape()
    {
        var permission = new Permission
        {
            Id = "perm-legacy",
            GrantedTo = new IdentitySet { User = new Identity { Id = ContactObjectId.ToString() } },
        };

        Find(new List<Permission> { permission }, new SpeMembershipKey(ContactObjectId, null))!
            .Id.Should().Be("perm-legacy");
    }

    /// <summary>
    /// #2105 (was the email-changed test's missing half): a permission made under the contact's FORMER address is still
    /// theirs — matched by the bound object id, which an email change does not touch.
    /// </summary>
    [Fact]
    public void FindContactPermission_WhenTheEmailChangedAfterTheGrant_MatchesByTheObjectId()
    {
        var permissions = new List<Permission>
        {
            PermissionFor("former.address@client-firm.com", "perm-target", userId: ContactObjectId),
        };

        Find(permissions, new SpeMembershipKey(ContactObjectId, "renamed.user@client-firm.com"))!
            .Id.Should().Be("perm-target");
    }

    /// <summary>
    /// #2105 (K2): a B2B guest's UPN is <c>user_domain#EXT#@tenant</c>, never their address; their <c>email</c> is. The
    /// old UPN-first read compared only the UPN and missed every such guest.
    /// </summary>
    [Fact]
    public void MatchesAB2BGuestByTheirEmailWhenTheUpnIsTheExtForm()
    {
        var permissions = new List<Permission>
        {
            PermissionFor("external.user_client-firm.com#EXT#@spaarke.onmicrosoft.com", "perm-guest", email: ContactEmail),
        };

        Find(permissions, ByEmail(ContactEmail))!.Id.Should().Be("perm-guest");
    }

    /// <summary>#2105: an object id that matches nothing falls back to the email.</summary>
    [Fact]
    public void FallsBackToTheEmailWhenTheObjectIdMatchesNothing()
    {
        var permissions = new List<Permission> { PermissionFor(ContactEmail, "perm-target", userId: Guid.NewGuid()) };

        Find(permissions, new SpeMembershipKey(ContactObjectId, ContactEmail))!.Id.Should().Be("perm-target");
    }

    /// <summary>#2103/#2105: no object id and no email is no key — nothing can be attributed, nothing is matched.</summary>
    [Fact]
    public void AKeyWithNoObjectIdAndNoEmail_MatchesNothing()
    {
        var key = new SpeMembershipKey(null, "  ");
        key.IsEmpty.Should().BeTrue();

        Find(new List<Permission> { PermissionFor(ContactEmail) }, key).Should().BeNull();
    }

    /// <summary>#2104 round 2 (K2): two eligible permissions under the email are BOTH returned — never just the first, so
    /// the caller can refuse to guess.</summary>
    [Fact]
    public void ReturnsEveryEligiblePermissionTheEmailMatches_NeverJustTheFirst()
    {
        var permissions = new List<Permission>
        {
            PermissionFor("counsel_client-firm.com#EXT#@spaarke.onmicrosoft.com", "perm-guest", email: ContactEmail),
            PermissionFor(ContactEmail, "perm-member"),
        };

        SpeContainerMembershipService.FindContactPermissions(permissions, ByEmail(ContactEmail), NoneManaged)
            .Select(p => p.Id).Should().Equal("perm-guest", "perm-member");
    }

    /// <summary>#2104 round 2 (K2): the same under the object id.</summary>
    [Fact]
    public void ReturnsEveryPermissionTheObjectIdMatches_NeverJustTheFirst()
    {
        var permissions = new List<Permission>
        {
            PermissionFor("a@x.example", "perm-1", userId: ContactObjectId),
            PermissionFor("b@x.example", "perm-2", userId: ContactObjectId),
        };

        SpeContainerMembershipService.FindContactPermissions(permissions, new SpeMembershipKey(ContactObjectId, null), NoneManaged)
            .Should().HaveCount(2);
    }

    /// <summary>#2104 round 2: an object-id match decides — another principal's permission that merely shares the email is
    /// not added to it (and so never makes the contact's own match ambiguous).</summary>
    [Fact]
    public void AnObjectIdMatchDecides_TheEmailIsNotConsulted()
    {
        var permissions = new List<Permission>
        {
            PermissionFor("former@client-firm.com", "perm-own", userId: ContactObjectId),
            PermissionFor(ContactEmail, "perm-other", userId: Guid.NewGuid()),
        };

        SpeContainerMembershipService.FindContactPermissions(
                permissions, new SpeMembershipKey(ContactObjectId, ContactEmail), NoneManaged)
            .Select(p => p.Id).Should().Equal("perm-own");
    }

    /// <summary>#2104 round 2: the key's email is trimmed when the key is made, and a permission's address is trimmed when
    /// compared.</summary>
    [Fact]
    public void TrimsBothTheKeysEmailAndThePermissionsAddress()
    {
        new SpeMembershipKey(null, "  External.User@client-firm.com ").Email.Should().Be("External.User@client-firm.com");

        Find(new List<Permission> { PermissionFor(" " + ContactEmail + "  ", "perm-padded") }, ByEmail(ContactEmail))!
            .Id.Should().Be("perm-padded");
    }

    /// <summary>
    /// #2104 round 3: <see cref="SpeContainerMembershipService.IsAmbiguousPermissionMatch"/> is the line between the two
    /// failures a retry cannot fix (an ambiguous match, an unverifiable holder) and every transient one. The endpoints name
    /// the first <c>permission_ambiguous</c>; misclassifying a transient failure would take away a retry that can work.
    /// </summary>
    [Theory]
    [InlineData(SpeContainerMembershipService.AmbiguousMatchError + ": 2 permissions on the container match user 'x'", true)]
    [InlineData(SpeContainerMembershipService.UnverifiableHolderError + ": the permission matching user 'x' names no object id", true)]
    [InlineData(SpeContainerMembershipService.InternalUserCheckFailedError + ": the matched permission holders could not be checked", false)]
    [InlineData(SpeContainerMembershipService.NoPermissionFoundError + " for user 'x' in container.", false)]
    [InlineData("Container permissions could not be fully enumerated, so no permission for user 'x' can be confirmed absent.", false)]
    [InlineData("The container's grant markers could not be read, so no permission for user 'x' can be removed.", false)]
    [InlineData("Graph error deleting permission: 503", false)]
    [InlineData("Container permissions could not be read: timeout", false)]
    public void IsAmbiguousPermissionMatch_OnlyTheTwoUnfixableFailures(string error, bool expected) =>
        SpeContainerMembershipService.IsAmbiguousPermissionMatch(new SpeContainerMembershipResult(false, null, error))
            .Should().Be(expected);

    /// <summary>#2104 round 3: a success is never ambiguous, whatever text it carries.</summary>
    [Fact]
    public void IsAmbiguousPermissionMatch_IsFalseForASuccess() =>
        SpeContainerMembershipService.IsAmbiguousPermissionMatch(
                new SpeContainerMembershipResult(true, "perm-1", SpeContainerMembershipService.AmbiguousMatchError))
            .Should().BeFalse();
}
