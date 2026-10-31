using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Task 024 / finding M1 — the SPE permission reads must follow <c>@odata.nextLink</c>, and anything
/// they could NOT finish reading must be reported as incomplete rather than as an absence.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists.</b> The POML's constraint is the load-bearing one: <i>"Mocking at a
/// seam proves the CALLER, never the CALLEE. Test the paging at the SERVICE level."</i>
/// <c>SpeRevokeMatcherTests</c> and <c>ProjectClosureCascadeTests</c> both substitute
/// <see cref="SpeContainerMembershipService"/> (or its bulk method) wholesale, so no existing test can
/// see whether a second page is fetched — exactly the gap that let finding A-13 and the task-016 swallow
/// both hide behind green suites.</para>
///
/// <para><b>Why a fake <see cref="IRequestAdapter"/> and not a transport mock.</b> ADR-038 B1 bans
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, and rightly: it pins HTTP wire details that are not our
/// contract. <see cref="IRequestAdapter"/> is a different thing — it is the Kiota SDK's OWN abstraction
/// boundary, the seam the SDK publishes for exactly this purpose, and it is what
/// <see cref="GraphServiceClient"/> is constructed from. Substituting it proves the real
/// <c>PermissionsRequestBuilder</c> chain, the real <c>OdataNextLink</c> plumbing and OUR real loop,
/// with no HTTP anywhere. That is a module boundary, not a transport mock.</para>
///
/// <para><b>Test-double discipline, inherited from task 020's own mistake.</b> Its first double derived
/// member emails from the first 8 GUID characters, which every fixture member shared — so "three
/// members" was ONE email three times and the fan-out was never exercised. Identities here are STATED
/// in the page builders, never derived.</para>
/// </remarks>
public class SpeContainerPagingTests
{
    private const string ContainerId = "b!Kx9wPq7RmkS3vN2cJdEfGh4iLmNoPqRs";
    private const string TargetEmail = "counsel@client-firm.com";
    private const string NextLink =
        "https://graph.microsoft.com/v1.0/storage/fileStorage/containers/" + ContainerId +
        "/permissions?$skiptoken=PAGE2";

    // ─────────────────────────────────────────────────────────────────────────────
    // Fixtures — identities are STATED, never derived (task 020's lesson).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A user permission as Graph lists it: its holder's object id (a fresh one unless stated — the documented
    /// shape always carries <c>user.id</c>, and #2104 round 2 (K3) never deletes a permission whose holder it cannot check),
    /// UPN and, optionally, email.</summary>
    private static Permission UserPermission(string permissionId, string upn, string? email = null, Guid? userId = null)
    {
        var data = new Dictionary<string, object> { ["userPrincipalName"] = upn };
        if (email is not null) data["email"] = email;

        return new Permission
        {
            Id = permissionId,
            Roles = ["reader"],
            GrantedToV2 = new SharePointIdentitySet
            {
                User = new SharePointIdentity { Id = (userId ?? Guid.NewGuid()).ToString(), AdditionalData = data }
            }
        };
    }

    /// <summary>#2104 round 2 (K3): no holder these tests name is an internal user unless a test says so.</summary>
    private static DataverseWebApiClient NoInternalUsers => TestInternalUserDirectory.Empty().Client;

    /// <summary>A contact key with only an email (#2105: the object id half is null).</summary>
    private static SpeMembershipKey Key(string email) => new(null, email);

    private static IReadOnlyCollection<SpeMembershipKey> Keys(params string[] emails) => emails.Select(Key).ToList();

    private static PermissionCollectionResponse Page(string? nextLink, params Permission[] permissions) =>
        new() { Value = [.. permissions], OdataNextLink = nextLink };

    private static SpeContainerMembershipService ServiceOver(params PermissionCollectionResponse[] pages)
        => ServiceOver(new ScriptedPermissionAdapter(pages));

    private static SpeContainerMembershipService ServiceOver(ScriptedPermissionAdapter adapter)
        => ServiceOver(adapter, NullLogger<SpeContainerMembershipService>.Instance);

    private static SpeContainerMembershipService ServiceOver(
        ScriptedPermissionAdapter adapter, ILogger<SpeContainerMembershipService> logger)
    {
        var graphClient = new GraphServiceClient(adapter);

        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(graphClient);

        return new SpeContainerMembershipService(
            TestSpeOwnership.AllowAll(factory.Object), logger);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Layer 3 (made offline) — the loop actually follows the link.
    // ─────────────────────────────────────────────────────────────────────────────

    // ✅ RE-BASED BY uac-r2 TASK 166 f1 (verifier item 14): the four ListExternalMembers_* tests drove the paged reader
    // through ListExternalMembersAsync, which had no production caller and was deleted. The SAME reader is now driven
    // through the two live paths that use it — RemoveMembershipsAsync (close-project, organization revoke) and
    // RevokeMembershipAsync (single-grant revoke) — so the paging is proven where it matters.

    /// <summary>
    /// The finding in one test: a grantee on page 2 must be visible. Before task 024 both reads issued a
    /// single <c>.GetAsync()</c> and used <c>permissions?.Value</c>, so page 2 did not exist.
    /// </summary>
    [Fact]
    public async Task RemoveMemberships_SeesAGranteeOnTheSecondPage()
    {
        var service = ServiceOver(
            Page(NextLink, UserPermission("perm-1", "first@client-firm.com")),
            Page(null, UserPermission("perm-2", "second@client-firm.com")));

        var results = (await service.RemoveMembershipsAsync(ContainerId, Keys("second@client-firm.com"), NoInternalUsers)).Results;

        results[Key("second@client-firm.com")].PermissionId.Should().Be("perm-2",
            "a member past the first page is still a member holding file access; a read that cannot see "
            + "them reports a container clean while they retain it");
    }

    /// <summary>
    /// The revoke half of the same defect: the target sits on page 2, so a single-page read reports
    /// "no permission found" for a permission that is right there.
    /// </summary>
    [Fact]
    public async Task RevokeMembership_FindsAndRemovesATargetOnTheSecondPage()
    {
        var service = ServiceOver(
            Page(NextLink, UserPermission("perm-other", "someone.else@client-firm.com")),
            Page(null, UserPermission("perm-target", TargetEmail)));

        var result = await service.RevokeMembershipAsync(ContainerId, Key(TargetEmail), NoInternalUsers);

        result.Success.Should().BeTrue();
        result.PermissionId.Should().Be("perm-target");
    }

    /// <summary>
    /// Three pages — proves the loop keeps going rather than following exactly one link.
    /// </summary>
    [Fact]
    public async Task RemoveMemberships_FollowsMoreThanOneLink()
    {
        var service = ServiceOver(
            Page(NextLink, UserPermission("perm-1", "a@client-firm.com")),
            Page(NextLink, UserPermission("perm-2", "b@client-firm.com")),
            Page(null, UserPermission("perm-3", "c@client-firm.com")));

        var results = (await service.RemoveMembershipsAsync(ContainerId, Keys("c@client-firm.com"), NoInternalUsers)).Results;

        results[Key("c@client-firm.com")].PermissionId.Should().Be("perm-3",
            "following one link is not the same as enumerating a collection");
    }

    /// <summary>
    /// The single-page case must stay a single request — paging is not an excuse to re-read.
    /// </summary>
    [Fact]
    public async Task SinglePageContainer_IssuesExactlyOneRequest()
    {
        var adapter = new ScriptedPermissionAdapter([Page(null, UserPermission("perm-1", TargetEmail))]);
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(new GraphServiceClient(adapter));
        var service = new SpeContainerMembershipService(
            TestSpeOwnership.AllowAll(factory.Object), NullLogger<SpeContainerMembershipService>.Instance);

        await service.RemoveMembershipsAsync(ContainerId, Keys("absent@client-firm.com"), NoInternalUsers);

        adapter.GetRequestCount.Should().Be(1,
            "a collection with no nextLink is one request; re-reading it would double every cost");
    }

    /// <summary>
    /// And each follow-up request must actually USE the nextLink URL the server gave us.
    /// </summary>
    [Fact]
    public async Task FollowUpRequest_UsesTheServerSuppliedNextLink()
    {
        var adapter = new ScriptedPermissionAdapter(
        [
            Page(NextLink, UserPermission("perm-1", "a@client-firm.com")),
            Page(null, UserPermission("perm-2", "b@client-firm.com"))
        ]);
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(new GraphServiceClient(adapter));
        var service = new SpeContainerMembershipService(
            TestSpeOwnership.AllowAll(factory.Object), NullLogger<SpeContainerMembershipService>.Instance);

        await service.RemoveMembershipsAsync(ContainerId, Keys("absent@client-firm.com"), NoInternalUsers);

        adapter.GetRequestCount.Should().Be(2);
        adapter.RequestedUrls[1].Should().Contain("$skiptoken=PAGE2",
            "the second request must be the URL the SERVER handed us — synthesising our own skip token "
            + "is the DriveItemOperations client-pagination shape, which enumerates nothing");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The honesty semantics — an unfinished read is never an absence (design §3).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 The crux. A read that ran out of pages must NOT let the caller conclude the contact holds no
    /// permission — <c>NoPermissionFound</c> promises "read successfully … genuinely absent", and a
    /// partial read cannot support that sentence.
    /// </summary>
    [Fact]
    public async Task RevokeMembership_WhenEnumerationIsIncompleteAndNothingMatched_ReportsFailureNotAbsence()
    {
        // Every page dangles a nextLink, so the bound is hit and the read never completes.
        var pages = Enumerable
            .Range(0, SpeContainerMembershipService.MaxPermissionPages + 2)
            .Select(i => Page(NextLink, UserPermission($"perm-{i}", $"member{i}@client-firm.com")))
            .ToArray();

        var result = await ServiceOver(pages).RevokeMembershipAsync(ContainerId, Key(TargetEmail), NoInternalUsers);

        result.Success.Should().BeFalse();
        result.Error.Should().NotStartWith(SpeContainerMembershipService.NoPermissionFoundError,
            "'no permission found' is what RevokeExternalAccessEndpoint maps to the benign "
            + "NoPermissionFound outcome; saying it here would report a container clean on a set we "
            + "never finished reading");
        result.Error.Should().Contain("could not be fully enumerated");
    }

    // ListExternalMembers_WhenThePageBoundIsHit_ThrowsRatherThanReturningAPrefix DELETED with the method (task 166 f1):
    // "the bound is never reportable as a complete read" is pinned on the live paths by
    // RevokeMembership_WhenEnumerationIsIncompleteAndNothingMatched_ReportsFailureNotAbsence (above) and
    // RemoveMemberships_OnAnIncompleteRead_ReportsUnmatchedMembersAsFailures (below).

    /// <summary>
    /// But the removal must still remove everyone it DID see. Aborting leaves strictly MORE access in place — the
    /// same reasoning tasks 016/017 used for not aborting on a per-member failure.
    /// </summary>
    /// <remarks>
    /// ✅ RE-BASED BY uac-r2 TASK 166 (amendment e) — was
    /// <c>RemoveAllExternalMembers_OnAnIncompleteRead_StillRemovesWhatItSawAndReportsNotCleared</c> (and its
    /// complete-read twin, now covered by <c>RemoveMemberships_ReadsTheContainerOnce_RegardlessOfMemberCount</c>).
    /// The whole-container sweep was deleted — it removed internal users too; close-project now calls
    /// <see cref="SpeContainerMembershipService.RemoveMembershipsAsync"/> with exactly the revoked grantees. The
    /// "not cleared" half lives on the unseen grantee's failed result, which close-project counts as unresolved.
    /// </remarks>
    [Fact]
    public async Task RemoveMemberships_OnAnIncompleteRead_StillRemovesTheGranteesItSaw()
    {
        var pages = Enumerable
            .Range(0, SpeContainerMembershipService.MaxPermissionPages + 2)
            .Select(i => Page(NextLink, UserPermission($"perm-{i}", $"member{i}@client-firm.com")))
            .ToArray();

        var results = (await ServiceOver(pages)
            .RemoveMembershipsAsync(ContainerId, Keys("member0@client-firm.com", "ghost@client-firm.com"), NoInternalUsers)).Results;

        results[Key("member0@client-firm.com")].Success.Should().BeTrue(
            "a grantee the read DID see is removed — an unread tail cannot retract a deletion that happened");
        results[Key("ghost@client-firm.com")].Success.Should().BeFalse();
        results[Key("ghost@client-firm.com")].Error.Should().NotStartWith(SpeContainerMembershipService.NoPermissionFoundError,
            "a grantee the read never reached is UNSEEN, not absent — close-project reports container_not_cleared");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ISS-004 (#968) — the org sweep reads the container ONCE, not once per member.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 The whole point of the convergence. Three members used to mean three full permission reads —
    /// and after task 024's paging, three × pages. It is now ONE read regardless of member count.
    /// </summary>
    [Fact]
    public async Task RemoveMemberships_ReadsTheContainerOnce_RegardlessOfMemberCount()
    {
        // Two pages, so a per-member read would cost 2 GETs each = 6; converged it is 2 total.
        var adapter = new ScriptedPermissionAdapter(
        [
            Page(NextLink,
                UserPermission("perm-a", "a@client-firm.com"),
                UserPermission("perm-b", "b@client-firm.com")),
            Page(null, UserPermission("perm-c", "c@client-firm.com"))
        ]);
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(new GraphServiceClient(adapter));
        var service = new SpeContainerMembershipService(
            TestSpeOwnership.AllowAll(factory.Object), NullLogger<SpeContainerMembershipService>.Instance);

        var results = (await service.RemoveMembershipsAsync(
            ContainerId, Keys("a@client-firm.com", "b@client-firm.com", "c@client-firm.com"), NoInternalUsers)).Results;

        results.Should().HaveCount(3);
        results.Values.Should().OnlyContain(r => r.Success);

        // 2 page GETs + 3 permission DELETEs. The GET count is the invariant under test: it must not
        // scale with the number of members.
        adapter.GetRequestCount.Should().Be(2,
            "the container is read ONCE (two pages); a per-member read would make this 6 and would grow "
            + "with both member count AND page count (ISS-004)");
    }

    /// <summary>
    /// Fan-out must be real: each member gets their OWN outcome, matched on their OWN address.
    /// Identities are stated, never derived — task 020's double derived them from shared GUID prefixes
    /// and tested "one email three times" while believing it tested three members.
    /// </summary>
    [Fact]
    public async Task RemoveMemberships_GivesEachMemberTheirOwnOutcome()
    {
        var service = ServiceOver(Page(null,
            UserPermission("perm-a", "a@client-firm.com"),
            UserPermission("perm-c", "c@client-firm.com")));

        var results = (await service.RemoveMembershipsAsync(
            ContainerId, Keys("a@client-firm.com", "absent@client-firm.com", "c@client-firm.com"), NoInternalUsers)).Results;

        results[Key("a@client-firm.com")].PermissionId.Should().Be("perm-a");
        results[Key("c@client-firm.com")].PermissionId.Should().Be("perm-c");
        results[Key("absent@client-firm.com")].Success.Should().BeFalse();
        results[Key("absent@client-firm.com")].Error.Should()
            .StartWith(SpeContainerMembershipService.NoPermissionFoundError,
                "a COMPLETE read that did not find them is the genuine broker-only absence");
    }

    /// <summary>
    /// The honesty rule survives the convergence: on an incomplete read, an unmatched member is a
    /// failure, not an absence — for every member at once.
    /// </summary>
    [Fact]
    public async Task RemoveMemberships_OnAnIncompleteRead_ReportsUnmatchedMembersAsFailures()
    {
        var pages = Enumerable
            .Range(0, SpeContainerMembershipService.MaxPermissionPages + 2)
            .Select(i => Page(NextLink, UserPermission($"perm-{i}", $"member{i}@client-firm.com")))
            .ToArray();

        var results = (await ServiceOver(pages)
            .RemoveMembershipsAsync(ContainerId, Keys("ghost@client-firm.com"), NoInternalUsers)).Results;

        results[Key("ghost@client-firm.com")].Success.Should().BeFalse();
        results[Key("ghost@client-firm.com")].Error.Should()
            .NotStartWith(SpeContainerMembershipService.NoPermissionFoundError);
    }

    /// <summary>
    /// A read failure makes EVERY member unanswerable — nobody is reported absent, and the method does
    /// not throw, because the caller's contract is "every member gets an outcome".
    /// </summary>
    [Fact]
    public async Task RemoveMemberships_WhenTheReadFails_EveryMemberIsFailedAndNoneIsAbsent()
    {
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Throws(new InvalidOperationException("Graph unreachable"));
        var service = new SpeContainerMembershipService(
            TestSpeOwnership.AllowAll(factory.Object), NullLogger<SpeContainerMembershipService>.Instance);

        var results = (await service.RemoveMembershipsAsync(
            ContainerId, Keys("a@client-firm.com", "b@client-firm.com"), NoInternalUsers)).Results;

        results.Should().HaveCount(2);
        results.Values.Should().OnlyContain(r => !r.Success);
        results.Values.Should().OnlyContain(
            r => !r.Error!.StartsWith(SpeContainerMembershipService.NoPermissionFoundError));
    }

    /// <summary>#2104 round 4 (F1): a read that faults establishes nothing about the container — Unknown (a retry may
    /// answer it), never "a contact permission remains", which would tell the caller a retry cannot help.</summary>
    [Fact]
    public async Task RemoveMemberships_Confirming_WhenTheReadFails_TheContainerAnswerIsUnknown()
    {
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Throws(new InvalidOperationException("Graph unreachable"));
        var service = new SpeContainerMembershipService(
            TestSpeOwnership.AllowAll(factory.Object), NullLogger<SpeContainerMembershipService>.Instance);

        var removal = await service.RemoveMembershipsAsync(ContainerId, [], NoInternalUsers, confirmNoContactPermission: true);

        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.Unknown);
    }

    /// <summary>
    /// Empty input must not touch Graph at all — an org whose members all failed email resolution
    /// should not cost a container read.
    /// </summary>
    [Fact]
    public async Task RemoveMemberships_WithNoEmails_IssuesNoRequest()
    {
        var adapter = new ScriptedPermissionAdapter([]);
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(new GraphServiceClient(adapter));
        var service = new SpeContainerMembershipService(
            TestSpeOwnership.AllowAll(factory.Object), NullLogger<SpeContainerMembershipService>.Instance);

        var results = (await service.RemoveMembershipsAsync(ContainerId, [], NoInternalUsers)).Results;

        results.Should().BeEmpty();
        adapter.RequestCount.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // #2105 — through the REAL service: marker exclusion, object-id and B2B matching.
    // ─────────────────────────────────────────────────────────────────────────────

    private static readonly Guid InternalUserId = Guid.Parse("0e0e0e0e-2105-4000-8000-000000000001");
    private static readonly Guid ContactObjectId = Guid.Parse("0e0e0e0e-2105-4000-8000-000000000002");

    /// <summary>
    /// #2105: a standing / just-in-time / demo writer role is recorded by a container marker
    /// (<c>&lt;prefix&gt;&lt;holder&gt; = &lt;permission id&gt;</c>). A contact whose address equals that internal user's
    /// UPN used to DELETE it on a revoke, leaving the marker dangling. It is now left in place — under every prefix.
    /// </summary>
    [Theory]
    [InlineData(SpeContainerMembershipService.StandingWriterMarkerPrefix)]
    [InlineData(SpeContainerMembershipService.JitWriterMarkerPrefix)]
    [InlineData(SpeContainerMembershipService.DemoWriterMarkerPrefix)]
    public async Task RevokeMembership_NeverDeletesAMarkerOwnedPermission_EvenWhenItsUpnMatches(string prefix)
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, UserPermission("perm-managed", TargetEmail))],
            markers: new Dictionary<string, string> { [$"{prefix}{InternalUserId:N}"] = "perm-managed" });

        var result = await ServiceOver(adapter).RevokeMembershipAsync(ContainerId, Key(TargetEmail), NoInternalUsers);

        adapter.DeletedUrls.Should().BeEmpty("a Spaarke-managed role belongs to its own lifecycle, never a contact revoke");
        result.Success.Should().BeFalse();
        result.Error.Should().StartWith(SpeContainerMembershipService.NoPermissionFoundError,
            "the read was complete and no permission of THIS contact's exists");
    }

    /// <summary>#2105, the organization / closure path: a managed permission matched by its <c>email</c> is not removed
    /// either, while an unmanaged contact permission in the same sweep is.</summary>
    [Fact]
    public async Task RemoveMemberships_NeverDeletesAMarkerOwnedPermission_AndStillRemovesTheContactsOwn()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null,
                UserPermission("perm-managed", "internal.user@spaarke.example", email: "a@client-firm.com"),
                UserPermission("perm-b", "b@client-firm.com"))],
            markers: new Dictionary<string, string>
            {
                [$"{SpeContainerMembershipService.StandingWriterMarkerPrefix}{InternalUserId:N}"] = "perm-managed",
            });

        var results = (await ServiceOver(adapter)
            .RemoveMembershipsAsync(ContainerId, Keys("a@client-firm.com", "b@client-firm.com"), NoInternalUsers)).Results;

        adapter.DeletedUrls.Should().ContainSingle().Which.Should().Contain("perm-b");
        results[Key("a@client-firm.com")].Error.Should().StartWith(SpeContainerMembershipService.NoPermissionFoundError);
        results[Key("b@client-firm.com")].PermissionId.Should().Be("perm-b");
        adapter.MarkerReadCount.Should().Be(1, "the markers are read once per sweep, not per contact");
    }

    /// <summary>
    /// #2105: the markers could not be read (the container answered 404), so no matched permission can be told apart
    /// from a managed one. Nothing is deleted, and the contact is NOT reported absent — a retryable failure.
    /// </summary>
    [Fact]
    public async Task RevokeMembership_WhenTheMarkersCannotBeRead_DeletesNothingAndIsNotAbsent()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, UserPermission("perm-target", TargetEmail))],
            markerFailure: new Microsoft.Graph.Models.ODataErrors.ODataError { ResponseStatusCode = 404 });

        var result = await ServiceOver(adapter).RevokeMembershipAsync(ContainerId, Key(TargetEmail), NoInternalUsers);

        adapter.DeletedUrls.Should().BeEmpty();
        result.Success.Should().BeFalse();
        result.Error.Should().NotStartWith(SpeContainerMembershipService.NoPermissionFoundError)
            .And.Contain("grant markers could not be read");
    }

    /// <summary>#2105, the organization / closure path: unreadable markers fail every contact — nothing deleted, nobody absent.</summary>
    [Fact]
    public async Task RemoveMemberships_WhenTheMarkersCannotBeRead_DeletesNothingAndNobodyIsAbsent()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, UserPermission("perm-a", "a@client-firm.com"))],
            markerFailure: new Microsoft.Graph.Models.ODataErrors.ODataError { ResponseStatusCode = 404 });

        var results = (await ServiceOver(adapter)
            .RemoveMembershipsAsync(ContainerId, Keys("a@client-firm.com", "absent@client-firm.com"), NoInternalUsers)).Results;

        adapter.DeletedUrls.Should().BeEmpty();
        results.Values.Should().OnlyContain(r => !r.Success
            && !r.Error!.StartsWith(SpeContainerMembershipService.NoPermissionFoundError));
    }

    /// <summary>
    /// #2105: the broker-only common case — nobody matches — costs no marker read: the exclusion can only remove a
    /// candidate, so it is not asked for when there is none.
    /// </summary>
    [Fact]
    public async Task RevokeMembership_WhenNothingMatches_DoesNotReadTheMarkers()
    {
        var adapter = new ScriptedPermissionAdapter([Page(null, UserPermission("perm-other", "someone.else@client-firm.com"))]);

        await ServiceOver(adapter).RevokeMembershipAsync(ContainerId, Key(TargetEmail), NoInternalUsers);

        adapter.MarkerReadCount.Should().Be(0);
    }

    /// <summary>
    /// #2105 (the email-changed case, through the real service): the permission was made under the contact's FORMER
    /// address; the bound object id still finds it, and it is removed.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenTheEmailChanged_RemovesThePermissionByTheObjectId()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, UserPermission("perm-target", "former.address@client-firm.com", userId: ContactObjectId))]);

        var result = await ServiceOver(adapter)
            .RevokeMembershipAsync(ContainerId, new SpeMembershipKey(ContactObjectId, "renamed@client-firm.com"), NoInternalUsers);

        result.Success.Should().BeTrue();
        result.PermissionId.Should().Be("perm-target");
        adapter.DeletedUrls.Should().ContainSingle().Which.Should().Contain("perm-target");
    }

    /// <summary>
    /// #2105 (K2, through the real service): a B2B guest's UPN is the <c>#EXT#</c> form, their <c>email</c> the real
    /// address. Matching the UPN alone reported "no permission found" while the guest kept file access.
    /// </summary>
    [Fact]
    public async Task RevokeMembership_RemovesAB2BGuestWhoseEmailMatchesBesideAnExtUpn()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, UserPermission("perm-guest", "counsel_client-firm.com#EXT#@spaarke.onmicrosoft.com", email: TargetEmail))]);

        var result = await ServiceOver(adapter).RevokeMembershipAsync(ContainerId, Key(TargetEmail), NoInternalUsers);

        result.Success.Should().BeTrue();
        result.PermissionId.Should().Be("perm-guest");
    }

    /// <summary>#2105: an empty key reaching the service is never looked for, so it is a failure — never "absent".</summary>
    [Fact]
    public async Task RevokeMembership_WithNoKey_IsAFailureAndIssuesNoRequest()
    {
        var adapter = new ScriptedPermissionAdapter([]);

        var result = await ServiceOver(adapter).RevokeMembershipAsync(ContainerId, new SpeMembershipKey(null, null), NoInternalUsers);

        result.Success.Should().BeFalse();
        result.Error.Should().NotStartWith(SpeContainerMembershipService.NoPermissionFoundError);
        adapter.RequestCount.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // #2104 round 2 — through the REAL service: never a guess (K2), never an internal user's own permission (K3), a
    // marker read that faults (K5), the trimmed email, and whether a contact permission remains (F1).
    // ─────────────────────────────────────────────────────────────────────────────

    private static readonly Guid GuestObjectId = Guid.Parse("0e0e0e0e-2104-4000-8000-000000000003");
    private static readonly Guid MemberObjectId = Guid.Parse("0e0e0e0e-2104-4000-8000-000000000004");

    /// <summary>A permission that names its holder by UPN only — no <c>user.id</c>.</summary>
    private static Permission PermissionWithNoHolderId(string permissionId, string upn) => new()
    {
        Id = permissionId,
        Roles = ["reader"],
        GrantedToV2 = new SharePointIdentitySet
        {
            User = new SharePointIdentity { AdditionalData = new Dictionary<string, object> { ["userPrincipalName"] = upn } },
        },
    };

    private static Permission ManagerPermission(string permissionId, string upn, Guid userId)
    {
        var permission = UserPermission(permissionId, upn, userId: userId);
        permission.Roles = ["manager"];
        return permission;
    }

    /// <summary>
    /// K2: two eligible permissions match the contact's email (a B2B guest's <c>email</c> and another principal's UPN).
    /// Deleting "the first" could remove somebody else's access, so NOTHING is deleted and the outcome is a distinct
    /// ambiguous failure — never Success, never the benign absence.
    /// </summary>
    [Fact]
    public async Task RevokeMembership_WhenTwoEligiblePermissionsMatchTheEmail_DeletesNothingAndIsAmbiguous()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null,
                UserPermission("perm-guest", "counsel_client-firm.com#EXT#@spaarke.onmicrosoft.com", email: TargetEmail),
                UserPermission("perm-member", TargetEmail))]);

        var result = await ServiceOver(adapter).RevokeMembershipAsync(ContainerId, Key(TargetEmail), NoInternalUsers);

        adapter.DeletedUrls.Should().BeEmpty("neither permission is provably the contact's");
        result.Success.Should().BeFalse();
        result.Error.Should().StartWith(SpeContainerMembershipService.AmbiguousMatchError)
            .And.NotStartWith(SpeContainerMembershipService.NoPermissionFoundError);
        SpeContainerMembershipService.IsAmbiguousPermissionMatch(result).Should().BeTrue(
            "#2104 round 3: the endpoints name this outcome permission_ambiguous (non-retryable) off this classifier");
    }

    /// <summary>K2 on the organization / closure path: the ambiguous key deletes nothing; the other keys are unaffected.</summary>
    [Fact]
    public async Task RemoveMemberships_AnAmbiguousKey_DeletesNothingForIt_AndStillRemovesTheOthers()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null,
                UserPermission("perm-a1", "a@client-firm.com"),
                UserPermission("perm-a2", "someone_x.com#EXT#@spaarke.onmicrosoft.com", email: "a@client-firm.com"),
                UserPermission("perm-b", "b@client-firm.com"))]);

        var results = (await ServiceOver(adapter)
            .RemoveMembershipsAsync(ContainerId, Keys("a@client-firm.com", "b@client-firm.com"), NoInternalUsers)).Results;

        adapter.DeletedUrls.Should().ContainSingle().Which.Should().Contain("perm-b");
        results[Key("a@client-firm.com")].Success.Should().BeFalse();
        results[Key("a@client-firm.com")].Error.Should().StartWith(SpeContainerMembershipService.AmbiguousMatchError);
        SpeContainerMembershipService.IsAmbiguousPermissionMatch(results[Key("a@client-firm.com")]).Should().BeTrue();
        SpeContainerMembershipService.IsAmbiguousPermissionMatch(results[Key("b@client-firm.com")]).Should().BeFalse();
        results[Key("b@client-firm.com")].PermissionId.Should().Be("perm-b");
    }

    /// <summary>
    /// K3: an internal user's hand-granted MANAGER role whose UPN equals the contact's email is that user's own access —
    /// governed by their internal roles, not by a contact grant — so the contact cleanup never removes it. The contact
    /// holds no permission of their own: the healthy absence. The holder was checked in one systemusers query.
    /// </summary>
    [Fact]
    public async Task RevokeMembership_NeverRemovesAnInternalUsersHandGrantedRole_WhoseUpnIsTheContactsEmail()
    {
        var adapter = new ScriptedPermissionAdapter([Page(null, ManagerPermission("perm-manager", TargetEmail, InternalUserId))]);
        var directory = TestInternalUserDirectory.With(InternalUserId);

        var result = await ServiceOver(adapter).RevokeMembershipAsync(ContainerId, Key(TargetEmail), directory.Client);

        adapter.DeletedUrls.Should().BeEmpty("the permission is an internal user's own, never a contact grant's to remove");
        result.Success.Should().BeFalse();
        result.Error.Should().StartWith(SpeContainerMembershipService.NoPermissionFoundError,
            "the read was complete and no permission of THIS contact's exists");
        directory.Filters.Should().ContainSingle().Which.Should().Contain(InternalUserId.ToString());
    }

    /// <summary>K3 on the organization / closure path: same rule, and an unmatched holder is not asked about.</summary>
    [Fact]
    public async Task RemoveMemberships_NeverRemovesAnInternalUsersOwnPermission()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, ManagerPermission("perm-manager", "a@client-firm.com", InternalUserId))]);

        var removal = await ServiceOver(adapter).RemoveMembershipsAsync(
            ContainerId, Keys("a@client-firm.com"), TestInternalUserDirectory.With(InternalUserId).Client);

        adapter.DeletedUrls.Should().BeEmpty();
        removal.Results[Key("a@client-firm.com")].Error.Should().StartWith(SpeContainerMembershipService.NoPermissionFoundError);
        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.Clean,
            "#2104 round 4: the role left in place is an internal user's own (checked), not a contact permission");
    }

    /// <summary>
    /// K3, the exclusion runs BEFORE the ambiguity check: an internal user's role and a B2B guest's permission both match
    /// the email; the internal one is never the contact's, so the guest's is the one match and is removed.
    /// </summary>
    [Fact]
    public async Task RevokeMembership_AnInternalUsersRoleBesideTheGuestsPermission_RemovesOnlyTheGuests()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null,
                ManagerPermission("perm-internal", TargetEmail, InternalUserId),
                UserPermission("perm-guest", "counsel_client-firm.com#EXT#@spaarke.onmicrosoft.com", email: TargetEmail,
                    userId: GuestObjectId))]);

        var result = await ServiceOver(adapter)
            .RevokeMembershipAsync(ContainerId, Key(TargetEmail), TestInternalUserDirectory.With(InternalUserId).Client);

        result.PermissionId.Should().Be("perm-guest");
        adapter.DeletedUrls.Should().ContainSingle().Which.Should().Contain("perm-guest");
    }

    /// <summary>K3: the internal-user lookup failing is "could not check", so NOTHING is deleted and the contact is Failed —
    /// never the benign absence.</summary>
    [Fact]
    public async Task RevokeMembership_WhenTheInternalUserCheckFails_DeletesNothingAndIsFailed()
    {
        var adapter = new ScriptedPermissionAdapter([Page(null, UserPermission("perm-target", TargetEmail))]);

        var result = await ServiceOver(adapter).RevokeMembershipAsync(
            ContainerId, Key(TargetEmail), TestInternalUserDirectory.Failing(new InvalidOperationException("Dataverse down")).Client);

        adapter.DeletedUrls.Should().BeEmpty();
        result.Success.Should().BeFalse();
        result.Error.Should().StartWith(SpeContainerMembershipService.InternalUserCheckFailedError)
            .And.NotStartWith(SpeContainerMembershipService.NoPermissionFoundError);
        SpeContainerMembershipService.IsAmbiguousPermissionMatch(result).Should().BeFalse(
            "#2104 round 3: a lookup that faulted is transient — a retry can succeed, so it stays retryable");
    }

    /// <summary>K3 on the organization / closure path: a failed lookup deletes nothing for anyone, and proves nothing.</summary>
    [Fact]
    public async Task RemoveMemberships_WhenTheInternalUserCheckFails_DeletesNothingAndNobodyIsAbsent()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, UserPermission("perm-a", "a@client-firm.com"), UserPermission("perm-b", "b@client-firm.com"))]);

        var removal = await ServiceOver(adapter).RemoveMembershipsAsync(
            ContainerId, Keys("a@client-firm.com", "b@client-firm.com"),
            TestInternalUserDirectory.Failing(new InvalidOperationException("Dataverse down")).Client,
            confirmNoContactPermission: true);

        adapter.DeletedUrls.Should().BeEmpty();
        removal.Results.Values.Should().OnlyContain(r => !r.Success
            && r.Error!.StartsWith(SpeContainerMembershipService.InternalUserCheckFailedError));
        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.Unknown, "#2104 round 4: a lookup fault establishes nothing");
    }

    /// <summary>K3: the lookup is BATCHED — one systemusers query for every candidate holder, not one per permission.</summary>
    [Fact]
    public async Task RemoveMemberships_ChecksEveryCandidateHolderInOneQuery()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null,
                UserPermission("perm-a", "a@client-firm.com", userId: GuestObjectId),
                UserPermission("perm-b", "b@client-firm.com", userId: MemberObjectId),
                UserPermission("perm-other", "other@client-firm.com", userId: InternalUserId))]);
        var directory = TestInternalUserDirectory.Empty();

        await ServiceOver(adapter).RemoveMembershipsAsync(ContainerId, Keys("a@client-firm.com", "b@client-firm.com"), directory.Client);

        directory.Filters.Should().ContainSingle("one query for all candidates");
        directory.Filters[0].Should().Contain(GuestObjectId.ToString()).And.Contain(MemberObjectId.ToString())
            .And.NotContain(InternalUserId.ToString(), "a permission no key matches is not a candidate");
        adapter.DeletedUrls.Should().HaveCount(2);
    }

    /// <summary>K3: nothing matched, nothing to check — the common broker-only revoke costs no Dataverse query.</summary>
    [Fact]
    public async Task RevokeMembership_WhenNothingMatches_DoesNotQueryTheDirectory()
    {
        var adapter = new ScriptedPermissionAdapter([Page(null, UserPermission("perm-other", "someone.else@client-firm.com"))]);
        var directory = TestInternalUserDirectory.Empty();

        await ServiceOver(adapter).RevokeMembershipAsync(ContainerId, Key(TargetEmail), directory.Client);

        directory.Filters.Should().BeEmpty();
    }

    /// <summary>K3: the one matching permission names no holder object id, so it cannot be told apart from an internal
    /// user's — it is NOT deleted, and the contact is Failed (never the benign absence).</summary>
    [Fact]
    public async Task RevokeMembership_WhenTheMatchNamesNoHolderObjectId_DeletesNothingAndIsFailed()
    {
        var adapter = new ScriptedPermissionAdapter([Page(null, PermissionWithNoHolderId("perm-noid", TargetEmail))]);

        var result = await ServiceOver(adapter).RevokeMembershipAsync(ContainerId, Key(TargetEmail), NoInternalUsers);

        adapter.DeletedUrls.Should().BeEmpty();
        result.Success.Should().BeFalse();
        result.Error.Should().StartWith(SpeContainerMembershipService.UnverifiableHolderError)
            .And.NotStartWith(SpeContainerMembershipService.NoPermissionFoundError);
        SpeContainerMembershipService.IsAmbiguousPermissionMatch(result).Should().BeTrue(
            "#2104 round 3: a retry reads the same holder-less permission — named permission_ambiguous, not a retry");
    }

    /// <summary>
    /// #2111 (CODE-QUALITY-RUBRIC D9): the contact's email is a match key, never log or error text. Every path that names
    /// the key — a removal, a benign absence, an ambiguous match, an unverifiable holder, a grant — logs the object id and
    /// only WHETHER an email key is set; the result <c>Error</c> (which the revoke endpoint logs as <c>{Error}</c>) is the
    /// same. Must-fire against <c>SpeMembershipKey.ToString()</c> printing the address, or an <c>{Email}</c> log field.
    /// </summary>
    [Fact]
    public async Task NoLogLineOrErrorText_CarriesTheContactsEmail()
    {
        const string probeEmail = "pii.probe@example.test";
        var oid = Guid.NewGuid();
        var log = new BindingLogCapture<SpeContainerMembershipService>();
        var errors = new List<string?>();

        async Task Revoke(SpeMembershipKey key, params Permission[] permissions)
        {
            var result = await ServiceOver(new ScriptedPermissionAdapter([Page(null, permissions)]), log)
                .RevokeMembershipAsync(ContainerId, key, NoInternalUsers);
            errors.Add(result.Error);
        }

        // Removed (email decides: the oid matches nothing).
        await Revoke(new SpeMembershipKey(oid, probeEmail), UserPermission("perm-1", probeEmail));
        // Benign absence, with both keys and with the email alone.
        await Revoke(new SpeMembershipKey(oid, probeEmail), UserPermission("perm-2", "other@example.test"));
        await Revoke(new SpeMembershipKey(null, probeEmail), UserPermission("perm-3", "other@example.test"));
        // Ambiguous (K2).
        await Revoke(new SpeMembershipKey(oid, probeEmail),
            UserPermission("perm-4", probeEmail),
            UserPermission("perm-5", "pii.probe_example.test#EXT#@tenant.example.test", email: probeEmail));
        // Unverifiable holder (K3).
        await Revoke(new SpeMembershipKey(oid, probeEmail), PermissionWithNoHolderId("perm-6", probeEmail));
        // The (caller-less) grant.
        var grant = await ServiceOver(new ScriptedPermissionAdapter([Page(null)]), log)
            .GrantMembershipAsync(ContainerId, probeEmail, ExternalAccessLevel.ViewOnly);
        errors.Add(grant.Error);

        log.Messages.Should().NotBeEmpty();
        log.Messages.Should().Contain(m => m.Contains(oid.ToString()), "the object id is what identifies the contact");
        log.Messages.Should().Contain(m => m.Contains("email key only"), "an email-only key says so, without the address");
        log.Messages.Should().AllSatisfy(m => m.Should().NotContain("pii.probe"));
        errors.Should().AllSatisfy(e => (e ?? string.Empty).Should().NotContain("pii.probe"));
    }

    /// <summary>K5: a marker read that FAULTS (not a 404) is as unanswered as a 404 — nothing deleted, Failed.</summary>
    [Fact]
    public async Task RevokeMembership_WhenTheMarkerReadFailsWithANon404_DeletesNothingAndIsFailed()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, UserPermission("perm-target", TargetEmail))],
            markerFailure: new Microsoft.Graph.Models.ODataErrors.ODataError { ResponseStatusCode = 503 });

        var result = await ServiceOver(adapter).RevokeMembershipAsync(ContainerId, Key(TargetEmail), NoInternalUsers);

        adapter.MarkerReadCount.Should().Be(1, "precondition: the markers were asked for");
        adapter.DeletedUrls.Should().BeEmpty();
        result.Success.Should().BeFalse();
        result.Error.Should().NotStartWith(SpeContainerMembershipService.NoPermissionFoundError);
    }

    /// <summary>K5 on the organization / closure path: a faulting marker read fails every contact and proves nothing.</summary>
    [Fact]
    public async Task RemoveMemberships_WhenTheMarkerReadFailsWithANon404_DeletesNothingAndNobodyIsAbsent()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, UserPermission("perm-a", "a@client-firm.com"))],
            markerFailure: new InvalidOperationException("Graph unreachable"));

        var removal = await ServiceOver(adapter)
            .RemoveMembershipsAsync(ContainerId, Keys("a@client-firm.com", "absent@client-firm.com"), NoInternalUsers);

        adapter.DeletedUrls.Should().BeEmpty();
        removal.Results.Values.Should().OnlyContain(r => !r.Success
            && !r.Error!.StartsWith(SpeContainerMembershipService.NoPermissionFoundError));
        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.Unknown, "#2104 round 4: a marker-read fault establishes nothing");
    }

    /// <summary>The contact's email is trimmed (and compared case-insensitively) before it is matched.</summary>
    [Fact]
    public async Task RevokeMembership_TrimsTheContactsEmailBeforeComparing()
    {
        var adapter = new ScriptedPermissionAdapter([Page(null, UserPermission("perm-target", TargetEmail))]);

        var result = await ServiceOver(adapter)
            .RevokeMembershipAsync(ContainerId, Key("  " + TargetEmail.ToUpperInvariant() + " "), NoInternalUsers);

        result.PermissionId.Should().Be("perm-target");
    }

    private static Dictionary<string, string> JitMarkerFor(string permissionId) => new()
    {
        [$"{SpeContainerMembershipService.JitWriterMarkerPrefix}{InternalUserId:N}"] = permissionId,
    };

    /// <summary>
    /// F1: asked to confirm, with NO key, over a container whose only user permission is a marker-owned (Spaarke-managed)
    /// role: the read is made, the markers are read, and no contact permission remains — every revoked grantee, whoever
    /// they are, is absent.
    /// </summary>
    [Fact]
    public async Task RemoveMemberships_Confirming_WithOnlyMarkerOwnedPermissions_IsClean()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, UserPermission("perm-jit", "internal.user@spaarke.example", userId: InternalUserId))],
            markers: JitMarkerFor("perm-jit"));

        var removal = await ServiceOver(adapter).RemoveMembershipsAsync(ContainerId, [], NoInternalUsers, confirmNoContactPermission: true);

        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.Clean);
        removal.Results.Should().BeEmpty();
        adapter.GetRequestCount.Should().Be(1);
        adapter.MarkerReadCount.Should().Be(1);
        adapter.DeletedUrls.Should().BeEmpty();
    }

    /// <summary>F1, the negative: one unmanaged user permission is left, which a contact grant could account for.</summary>
    [Fact]
    public async Task RemoveMemberships_Confirming_WithAnUnmanagedUserPermission_ReportsOneRemains()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null,
                UserPermission("perm-jit", "internal.user@spaarke.example", userId: InternalUserId),
                UserPermission("perm-unknown", "someone@elsewhere.example"))],
            markers: JitMarkerFor("perm-jit"));

        var removal = await ServiceOver(adapter).RemoveMembershipsAsync(ContainerId, [], NoInternalUsers, confirmNoContactPermission: true);

        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.ContactPermissionRemains,
            "its holder was checked and is not an internal user — a retry reads it again");
        adapter.DeletedUrls.Should().BeEmpty("confirming never deletes a permission no key matched");
    }

    /// <summary>
    /// #2104 round 4 (K), must fire: the only permission left is an internal user's HAND-GRANTED role (no marker) — that
    /// user's own access, never a contact permission. Confirming checks its holder in the one batched systemusers lookup,
    /// and the container is Clean. (Round 3 answered "one remains", which made an oversized organization's revoke and
    /// closure non-retryable for good on any container an internal user had been added to by hand.)
    /// </summary>
    [Fact]
    public async Task RemoveMemberships_Confirming_WithOnlyAnInternalUsersHandGrantedRole_IsClean()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null,
                UserPermission("perm-jit", "jit.user@spaarke.example", userId: MemberObjectId),
                ManagerPermission("perm-hand", "internal.user@spaarke.example", InternalUserId))],
            markers: new Dictionary<string, string>
            {
                [$"{SpeContainerMembershipService.JitWriterMarkerPrefix}{Guid.NewGuid():N}"] = "perm-jit",
            });
        var directory = TestInternalUserDirectory.With(InternalUserId);

        var removal = await ServiceOver(adapter).RemoveMembershipsAsync(ContainerId, [], directory.Client, confirmNoContactPermission: true);

        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.Clean);
        directory.Filters.Should().ContainSingle("one batched lookup").Which.Should().Contain(InternalUserId.ToString())
            .And.NotContain(MemberObjectId.ToString(), "a marker-owned permission's holder is not asked about");
        adapter.DeletedUrls.Should().BeEmpty();
    }

    /// <summary>
    /// #2104 round 4 (K), must not fire: beside the internal user's hand-granted role, a guest's permission (an object id
    /// that is no systemuser) is left — a permission a contact grant could account for, so ContactPermissionRemains.
    /// </summary>
    [Fact]
    public async Task RemoveMemberships_Confirming_WithAGuestPermissionBesideAnInternalUsersRole_ContactPermissionRemains()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null,
                ManagerPermission("perm-hand", "internal.user@spaarke.example", InternalUserId),
                UserPermission("perm-guest", "guest_x.com#EXT#@spaarke.onmicrosoft.com", userId: GuestObjectId))],
            markers: new Dictionary<string, string>());
        var directory = TestInternalUserDirectory.With(InternalUserId);

        var removal = await ServiceOver(adapter).RemoveMembershipsAsync(ContainerId, [], directory.Client, confirmNoContactPermission: true);

        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.ContactPermissionRemains);
        directory.Filters.Should().ContainSingle().Which.Should().Contain(GuestObjectId.ToString());
    }

    /// <summary>#2104 round 4 (K): the lookup over the remaining holders faulting establishes nothing — Unknown, never
    /// Clean and never "one remains".</summary>
    [Fact]
    public async Task RemoveMemberships_Confirming_WhenTheRemainingHoldersLookupFails_IsUnknown()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, ManagerPermission("perm-hand", "internal.user@spaarke.example", InternalUserId))],
            markers: new Dictionary<string, string>());
        var directory = TestInternalUserDirectory.Failing(new InvalidOperationException("Dataverse down"));

        var removal = await ServiceOver(adapter).RemoveMembershipsAsync(ContainerId, [], directory.Client, confirmNoContactPermission: true);

        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.Unknown);
        directory.Filters.Should().ContainSingle("precondition: the lookup was attempted");
    }

    /// <summary>
    /// #2104 round 4: a permission definitely left wins over a fault elsewhere — a key's ambiguous match is still there
    /// after any retry, so the answer is ContactPermissionRemains even though another key's delete failed.
    /// </summary>
    [Fact]
    public async Task RemoveMemberships_AnAmbiguousMatchBesideAFailedDelete_ContactPermissionRemains()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null,
                UserPermission("perm-a1", "a@client-firm.com"),
                UserPermission("perm-a2", "someone_x.com#EXT#@spaarke.onmicrosoft.com", email: "a@client-firm.com"),
                UserPermission("perm-b", "b@client-firm.com"))],
            deleteFailure: new InvalidOperationException("Graph refused the delete"));

        var removal = await ServiceOver(adapter).RemoveMembershipsAsync(
            ContainerId, Keys("a@client-firm.com", "b@client-firm.com"), NoInternalUsers, confirmNoContactPermission: true);

        removal.Results[Key("b@client-firm.com")].Success.Should().BeFalse("precondition: b's delete failed");
        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.ContactPermissionRemains);
    }

    /// <summary>
    /// #2104 round 4 (F3): the internal-user lookup is batched at <see cref="SpeContainerMembershipService.InternalUserLookupBatchSize"/>
    /// (50) object ids per <c>systemusers</c> query — 51 candidate holders are exactly two queries, neither over 50 — and
    /// it never narrows by <c>isdisabled</c> or <c>statecode</c>: a disabled internal user is still internal, and their
    /// own permission is still never a contact cleanup's to remove.
    /// </summary>
    [Fact]
    public async Task RemoveMemberships_LooksUp51CandidateHoldersInTwoBatchesOfAtMost50_WithNoStateFilter()
    {
        var emails = Enumerable.Range(0, 51).Select(i => $"member{i}@client-firm.com").ToArray();
        var holders = emails.Select(_ => Guid.NewGuid()).ToArray();
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, [.. emails.Select((email, i) => UserPermission($"perm-{i}", email, userId: holders[i]))])]);
        var directory = TestInternalUserDirectory.Empty();

        await ServiceOver(adapter).RemoveMembershipsAsync(ContainerId, Keys(emails), directory.Client);

        directory.Filters.Should().HaveCount(2, "51 holders at 50 per query");
        var idsPerFilter = directory.Filters
            .Select(f => System.Text.RegularExpressions.Regex.Matches(f, "azureactivedirectoryobjectid eq ").Count)
            .ToList();
        idsPerFilter.Should().OnlyContain(n => n <= SpeContainerMembershipService.InternalUserLookupBatchSize);
        idsPerFilter.Sum().Should().Be(51);
        holders.Should().OnlyContain(h => directory.Filters.Any(f => f.Contains(h.ToString())), "every holder is asked about");
        directory.Filters.Should().OnlyContain(f => !f.Contains("isdisabled") && !f.Contains("statecode"),
            "disabled internal users are still internal");
        SpeContainerMembershipService.InternalUserLookupBatchSize.Should().Be(50);
    }

    /// <summary>F1: an INCOMPLETE read proves nothing about the unread tail — never "none remains", even when every
    /// permission it did see is accounted for: the contact's own (removed here) and marker-owned ones (the key's match
    /// makes the markers read).</summary>
    [Fact]
    public async Task RemoveMemberships_Confirming_OnAnIncompleteRead_NeverReportsNoneRemains()
    {
        var pages = Enumerable
            .Range(0, SpeContainerMembershipService.MaxPermissionPages + 2)
            .Select(i => i == 0
                ? Page(NextLink, UserPermission("perm-a", "a@client-firm.com"), UserPermission($"perm-{i}", $"writer{i}@spaarke.example"))
                : Page(NextLink, UserPermission($"perm-{i}", $"writer{i}@spaarke.example")))
            .ToArray();
        var markers = Enumerable.Range(0, SpeContainerMembershipService.MaxPermissionPages + 2)
            .ToDictionary(i => $"{SpeContainerMembershipService.JitWriterMarkerPrefix}{Guid.NewGuid():N}", i => $"perm-{i}");
        var adapter = new ScriptedPermissionAdapter(pages, markers: markers);

        var removal = await ServiceOver(adapter)
            .RemoveMembershipsAsync(ContainerId, Keys("a@client-firm.com"), NoInternalUsers, confirmNoContactPermission: true);

        adapter.MarkerReadCount.Should().Be(1, "precondition: every permission seen is marker-owned or the contact's");
        adapter.DeletedUrls.Should().ContainSingle().Which.Should().Contain("perm-a");
        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.Unknown, "the unread tail may hold a contact permission");
    }

    /// <summary>F1: a contact permission whose DELETE failed is still on the container — never "none remains".</summary>
    [Fact]
    public async Task RemoveMemberships_Confirming_WhenTheContactsDeleteFails_NeverReportsNoneRemains()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null,
                UserPermission("perm-jit", "internal.user@spaarke.example", userId: InternalUserId),
                UserPermission("perm-a", "a@client-firm.com"))],
            markers: JitMarkerFor("perm-jit"),
            deleteFailure: new InvalidOperationException("Graph refused the delete"));

        var removal = await ServiceOver(adapter)
            .RemoveMembershipsAsync(ContainerId, Keys("a@client-firm.com"), NoInternalUsers, confirmNoContactPermission: true);

        adapter.DeletedUrls.Should().ContainSingle().Which.Should().Contain("perm-a");
        removal.Results[Key("a@client-firm.com")].Success.Should().BeFalse();
        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.Unknown,
            "the contact's permission was not removed — but a retry may remove it (#2104 round 4)");
    }

    /// <summary>F1: the contact's own permission was the only unmanaged one, and this call removed it — none remains.</summary>
    [Fact]
    public async Task RemoveMemberships_AfterRemovingTheOnlyContactPermission_ReportsNoneRemains()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null,
                UserPermission("perm-jit", "internal.user@spaarke.example", userId: InternalUserId),
                UserPermission("perm-a", "a@client-firm.com"))],
            markers: JitMarkerFor("perm-jit"));

        var removal = await ServiceOver(adapter).RemoveMembershipsAsync(ContainerId, Keys("a@client-firm.com"), NoInternalUsers);

        removal.Results[Key("a@client-firm.com")].PermissionId.Should().Be("perm-a");
        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.Clean);
    }

    /// <summary>F1: not asked to confirm and nothing matched, so the markers are not read (the broker-only common case
    /// stays one GET) — and without them nothing is proven.</summary>
    [Fact]
    public async Task RemoveMemberships_WithoutConfirming_DoesNotReadTheMarkersWhenNothingMatches_AndProvesNothing()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, UserPermission("perm-jit", "internal.user@spaarke.example", userId: InternalUserId))],
            markers: JitMarkerFor("perm-jit"));

        var removal = await ServiceOver(adapter).RemoveMembershipsAsync(ContainerId, Keys("absent@client-firm.com"), NoInternalUsers);

        adapter.MarkerReadCount.Should().Be(0);
        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.Unknown, "the unmatched permission was not judged");
        removal.Results[Key("absent@client-firm.com")].Error.Should().StartWith(SpeContainerMembershipService.NoPermissionFoundError);
    }

    /// <summary>F1: confirming over unreadable markers (404) with nothing matched — each contact's own answer stands (none
    /// of them matched anything), but nothing is proven about the container.</summary>
    [Fact]
    public async Task RemoveMemberships_Confirming_WhenTheMarkersCannotBeRead_KeepsTheAbsenceAndProvesNothing()
    {
        var adapter = new ScriptedPermissionAdapter(
            [Page(null, UserPermission("perm-jit", "internal.user@spaarke.example", userId: InternalUserId))],
            markerFailure: new Microsoft.Graph.Models.ODataErrors.ODataError { ResponseStatusCode = 404 });

        var removal = await ServiceOver(adapter)
            .RemoveMembershipsAsync(ContainerId, Keys("absent@client-firm.com"), NoInternalUsers, confirmNoContactPermission: true);

        removal.ContactPermissionState.Should().Be(SpeContainerContactPermissionState.Unknown);
        removal.Results[Key("absent@client-firm.com")].Error.Should().StartWith(SpeContainerMembershipService.NoPermissionFoundError);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Layer 1 — the honesty rule as a pure, exhaustive truth table (design §3.2).
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_CompleteAndFound_IsSuccess() =>
        SpeContainerMembershipService.ClassifyRevokeResult(true, "perm-1", TargetEmail)
            .Success.Should().BeTrue();

    [Fact]
    public void Classify_CompleteAndNotFound_IsTheBenignAbsence() =>
        SpeContainerMembershipService.ClassifyRevokeResult(true, null, TargetEmail)
            .Error.Should().StartWith(SpeContainerMembershipService.NoPermissionFoundError,
                "a finished read that found nothing IS the expected broker-only answer");

    /// <summary>
    /// A deletion that happened, happened — an unread tail cannot retract it.
    /// </summary>
    [Fact]
    public void Classify_IncompleteButFound_IsStillSuccess() =>
        SpeContainerMembershipService.ClassifyRevokeResult(false, "perm-1", TargetEmail)
            .Success.Should().BeTrue();

    /// <summary>
    /// 🔴 The row the whole task exists for.
    /// </summary>
    [Fact]
    public void Classify_IncompleteAndNotFound_IsFailureAndNeverTheAbsenceText()
    {
        var result = SpeContainerMembershipService.ClassifyRevokeResult(false, null, TargetEmail);

        result.Success.Should().BeFalse();
        result.Error.Should().NotStartWith(SpeContainerMembershipService.NoPermissionFoundError);
    }

    // SpeBulkRemovalResult (and its IsComplete truth test) was DELETED by uac-r2 task 166 with
    // RemoveAllExternalMembersAsync. ProjectClosureEndpoint.GranteeRemoval.Complete replaces it; its "unseen is
    // unresolved" half is the incomplete-read test above.

    // ═════════════════════════════════════════════════════════════════════════════
    // The fake Kiota adapter.
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Serves a scripted sequence of <see cref="PermissionCollectionResponse"/> pages and records the URL
    /// of every request, so a test can assert BOTH how many requests were made and that the follow-up
    /// used the server-supplied nextLink.
    /// </summary>
    /// <remarks>
    /// Throws on any unmodelled call rather than returning a default — the permissive-fallback fixture is
    /// what let task 020's fan-out go untested. Preserved deliberately.
    /// </remarks>
    private sealed class ScriptedPermissionAdapter : IRequestAdapter
    {
        private readonly PermissionCollectionResponse[] _pages;
        private readonly IReadOnlyDictionary<string, string>? _markers;
        private readonly Exception? _markerFailure;
        private readonly Exception? _deleteFailure;
        private int _index;

        /// <param name="pages">The permission pages, served in order.</param>
        /// <param name="markers">#2105: the container's grant markers (custom property name to permission id), served
        /// to the marker read. None by default.</param>
        /// <param name="markerFailure">#2105: thrown by the marker read instead.</param>
        /// <param name="deleteFailure">#2104 round 2: thrown by every permission DELETE (after it is recorded).</param>
        public ScriptedPermissionAdapter(
            PermissionCollectionResponse[] pages,
            IReadOnlyDictionary<string, string>? markers = null,
            Exception? markerFailure = null,
            Exception? deleteFailure = null)
        {
            _pages = pages;
            _markers = markers;
            _markerFailure = markerFailure;
            _deleteFailure = deleteFailure;
        }

        public List<string> RequestedUrls { get; } = [];

        /// <summary>Permission DELETEs only.</summary>
        public List<string> DeletedUrls { get; } = [];

        /// <summary>#2105: container (marker) reads, kept out of <see cref="GetRequestCount"/>, which counts the
        /// permission-collection reads the ISS-004 invariant is about.</summary>
        public int MarkerReadCount { get; private set; }

        public int RequestCount => RequestedUrls.Count;

        /// <summary>
        /// Collection READS only — deletes land on <see cref="SendNoContentAsync"/> and are excluded.
        /// The ISS-004 invariant is about read count, which must not scale with member count.
        /// </summary>
        public int GetRequestCount { get; private set; }

        public ISerializationWriterFactory SerializationWriterFactory =>
            throw new NotSupportedException("Serialization is not exercised by these tests.");

        public string? BaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

        public void EnableBackingStore(IBackingStoreFactory backingStoreFactory) { }

        public Task<ModelType?> SendAsync<ModelType>(
            RequestInformation requestInfo,
            ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default)
            where ModelType : IParsable
        {
            if (typeof(ModelType) == typeof(FileStorageContainer))
            {
                // #2105: the grant-marker read (GET the container's customProperties), modelled explicitly.
                MarkerReadCount++;
                if (_markerFailure is not null)
                    throw _markerFailure;

                return Task.FromResult((ModelType?)(object)ContainerWithMarkers(_markers));
            }

            if (typeof(ModelType) != typeof(PermissionCollectionResponse))
            {
                throw new InvalidOperationException(
                    $"Unmodelled read of {typeof(ModelType).Name}; this adapter scripts permission pages and markers only.");
            }

            RequestedUrls.Add(requestInfo.URI.ToString());
            GetRequestCount++;

            if (_index >= _pages.Length)
            {
                throw new InvalidOperationException(
                    $"The service requested page {_index + 1} but only {_pages.Length} were scripted. "
                    + "An unmodelled call is a test-design error, not a default to absorb.");
            }

            return Task.FromResult((ModelType?)(object)_pages[_index++]);
        }

        public Task SendNoContentAsync(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default)
        {
            // The permission DELETE lands here. Recorded, and treated as succeeding unless a failure was scripted.
            RequestedUrls.Add(requestInfo.URI.ToString());
            DeletedUrls.Add(requestInfo.URI.ToString());
            return _deleteFailure is null ? Task.CompletedTask : Task.FromException(_deleteFailure);
        }

        /// <summary>The container as Kiota deserializes it: each custom property an <see cref="UntypedObject"/> whose
        /// <c>value</c> is an <see cref="UntypedString"/> (the shape <c>SpeAdminGraphService.ReadCustomProperties</c>
        /// reads).</summary>
        private static FileStorageContainer ContainerWithMarkers(IReadOnlyDictionary<string, string>? markers)
        {
            var properties = new Dictionary<string, object>();
            foreach (var (name, value) in markers ?? new Dictionary<string, string>())
            {
                properties[name] = new UntypedObject(new Dictionary<string, UntypedNode> { ["value"] = new UntypedString(value) });
            }

            return new FileStorageContainer
            {
                Id = ContainerId,
                CustomProperties = new FileStorageContainerCustomPropertyDictionary { AdditionalData = properties },
            };
        }

        public Task<IEnumerable<ModelType>?> SendCollectionAsync<ModelType>(
            RequestInformation requestInfo,
            ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default)
            where ModelType : IParsable =>
            throw new NotSupportedException("SendCollectionAsync is not on the container-permission path.");

        public Task<ModelType?> SendPrimitiveAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SendPrimitiveAsync is not on the container-permission path.");

        public Task<IEnumerable<ModelType>?> SendPrimitiveCollectionAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "SendPrimitiveCollectionAsync is not on the container-permission path.");

        public Task<NativeResponseType?> ConvertToNativeRequestAsync<NativeResponseType>(
            RequestInformation requestInfo,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("ConvertToNativeRequestAsync is not used by these tests.");
    }
}
