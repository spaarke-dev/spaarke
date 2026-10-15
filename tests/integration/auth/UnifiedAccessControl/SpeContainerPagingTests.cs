using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;
using Sprk.Bff.Api.Tests.TestInfrastructure;

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

    private static Permission UserPermission(string permissionId, string upn) => new()
    {
        Id = permissionId,
        Roles = ["reader"],
        GrantedToV2 = new SharePointIdentitySet
        {
            User = new SharePointIdentity
            {
                AdditionalData = new Dictionary<string, object> { ["userPrincipalName"] = upn }
            }
        }
    };

    private static PermissionCollectionResponse Page(string? nextLink, params Permission[] permissions) =>
        new() { Value = [.. permissions], OdataNextLink = nextLink };

    private static SpeContainerMembershipService ServiceOver(params PermissionCollectionResponse[] pages)
    {
        var adapter = new ScriptedPermissionAdapter(pages);
        var graphClient = new GraphServiceClient(adapter);

        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(graphClient);

        return new SpeContainerMembershipService(
            TestSpeOwnership.AllowAll(factory.Object), NullLogger<SpeContainerMembershipService>.Instance);
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

        var results = await service.RemoveMembershipsAsync(ContainerId, ["second@client-firm.com"]);

        results["second@client-firm.com"].PermissionId.Should().Be("perm-2",
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

        var result = await service.RevokeMembershipAsync(ContainerId, TargetEmail);

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

        var results = await service.RemoveMembershipsAsync(ContainerId, ["c@client-firm.com"]);

        results["c@client-firm.com"].PermissionId.Should().Be("perm-3",
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

        await service.RemoveMembershipsAsync(ContainerId, ["absent@client-firm.com"]);

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

        await service.RemoveMembershipsAsync(ContainerId, ["absent@client-firm.com"]);

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

        var result = await ServiceOver(pages).RevokeMembershipAsync(ContainerId, TargetEmail);

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

        var results = await ServiceOver(pages)
            .RemoveMembershipsAsync(ContainerId, ["member0@client-firm.com", "ghost@client-firm.com"]);

        results["member0@client-firm.com"].Success.Should().BeTrue(
            "a grantee the read DID see is removed — an unread tail cannot retract a deletion that happened");
        results["ghost@client-firm.com"].Success.Should().BeFalse();
        results["ghost@client-firm.com"].Error.Should().NotStartWith(SpeContainerMembershipService.NoPermissionFoundError,
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

        var results = await service.RemoveMembershipsAsync(
            ContainerId, ["a@client-firm.com", "b@client-firm.com", "c@client-firm.com"]);

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

        var results = await service.RemoveMembershipsAsync(
            ContainerId, ["a@client-firm.com", "absent@client-firm.com", "c@client-firm.com"]);

        results["a@client-firm.com"].PermissionId.Should().Be("perm-a");
        results["c@client-firm.com"].PermissionId.Should().Be("perm-c");
        results["absent@client-firm.com"].Success.Should().BeFalse();
        results["absent@client-firm.com"].Error.Should()
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

        var results = await ServiceOver(pages)
            .RemoveMembershipsAsync(ContainerId, ["ghost@client-firm.com"]);

        results["ghost@client-firm.com"].Success.Should().BeFalse();
        results["ghost@client-firm.com"].Error.Should()
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

        var results = await service.RemoveMembershipsAsync(
            ContainerId, ["a@client-firm.com", "b@client-firm.com"]);

        results.Should().HaveCount(2);
        results.Values.Should().OnlyContain(r => !r.Success);
        results.Values.Should().OnlyContain(
            r => !r.Error!.StartsWith(SpeContainerMembershipService.NoPermissionFoundError));
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

        var results = await service.RemoveMembershipsAsync(ContainerId, []);

        results.Should().BeEmpty();
        adapter.RequestCount.Should().Be(0);
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
        private int _index;

        public ScriptedPermissionAdapter(PermissionCollectionResponse[] pages) => _pages = pages;

        public List<string> RequestedUrls { get; } = [];

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
            // The permission DELETE lands here. Recorded, and treated as succeeding.
            RequestedUrls.Add(requestInfo.URI.ToString());
            return Task.CompletedTask;
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
