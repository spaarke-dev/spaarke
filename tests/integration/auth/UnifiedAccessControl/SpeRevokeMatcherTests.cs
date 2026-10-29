using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using Sprk.Bff.Api.Tests.TestInfrastructure;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The SPE container-permission side of <c>/revoke</c> — finding A-13 (spec FR-16), closed by task 017.
///
/// <para><b>What was wrong.</b> The endpoint carried its own private copy of the SPE revoke logic which
/// set <c>contactIdStr = contactId.ToString()</c> and then looked for a permission whose
/// <c>userPrincipalName</c> <i>contained</i> that GUID. But membership is written with
/// <c>userPrincipalName</c> = the contact's <b>email</b> (<c>SpeContainerMembershipService.GrantMembershipAsync</c>),
/// and an email never contains a GUID — so the predicate matched nothing, ever. It then returned
/// <c>true</c> on no-match ("the permission may have already been removed"), so <c>/revoke</c> reported
/// SPE success while the ACL entry stayed exactly where it was.</para>
///
/// <para><b>The fix was deletion, not repair.</b> <c>SpeContainerMembershipService.RevokeMembershipAsync</c>
/// already matched on email correctly — and had ZERO callers. The endpoint's fork is gone and it calls the
/// service (CLAUDE.md §11: reuse, don't fork). What remains endpoint-side is resolving the contact's
/// email and reporting the outcome honestly.</para>
///
/// <para><b>Why a four-state outcome.</b> A bool cannot separate "removed" from "nothing to remove" from
/// "we could not tell", and the old bool's answer to the last two was <c>true</c> — which is how the bug
/// stayed invisible. Per this task's ADR-003 constraint, "confirmed absent" and "match failed" must be
/// distinguishable, so the response carries <see cref="SpeContainerRevokeOutcome"/>.</para>
///
/// <para><b>Broker-only context.</b> Nothing in this codebase ADDS a container permission:
/// <c>GrantMembershipAsync</c> has no callers, <c>/grant</c> reports
/// <c>SpeContainerMembershipGranted: false</c>, and neither invite endpoint touches SPE. So this is a
/// CLEANUP path for ACLs created by legacy versions or by admins outside Spaarke, and
/// <c>NoPermissionFound</c> is the ordinary healthy answer — not a failure.</para>
///
/// <para><b>Task 020 (FR-16b) extends this to ORGANIZATION grants.</b> An org revoke names no single
/// grantee, so its SPE side is a many-identity sweep: the organization is expanded to its active member
/// contacts via <c>sprk_contactorganization</c> and each member's permission is removed, with the outcome
/// reported at member granularity. <c>NotAttempted</c> is consequently no longer reachable for an org
/// revoke that supplied a <c>ContainerId</c>.</para>
///
/// <para>Seams are the <c>virtual</c> members of <see cref="DataverseWebApiClient"/> and
/// <see cref="SpeContainerMembershipService"/> (ADR-038 §4). No <c>Mock&lt;HttpMessageHandler&gt;</c>
/// (ban B1), no reflection into privates (ban B8).</para>
///
/// <para><b>⚠️ What these tests CANNOT falsify.</b> Everything below stops at the
/// <c>SpeContainerMembershipService</c> seam, so nothing here says anything about real Graph behaviour:
/// <list type="bullet">
/// <item>Whether <c>RevokeMembershipAsync</c>'s "No permission found" is TRUE on a real container. It
/// reads permissions with a single <c>GetAsync</c> and does not follow <c>@odata.nextLink</c>, so on a
/// multi-page container a member's entry beyond page 1 is reported absent while they retain file access.
/// Owned by task 024; a stub that returns <c>NotFound</c> cannot distinguish the two.</item>
/// <item>Whether SPE's permission list is even consistent immediately after a delete (Graph is eventually
/// consistent) — a removal reported here as succeeded may still be observable for some window.</item>
/// <item>Whether <c>userPrincipalName</c> on a real legacy ACL matches <c>contact.emailaddress1</c>
/// exactly. The match is case-insensitive but not alias- or proxy-address-aware, so a member invited
/// under a different address would be reported <c>NoPermissionFound</c>.</item>
/// </list>
/// Each of these is a way the org cleanup could report <c>NoPermissionFound</c> — a healthy-looking
/// answer — for a member who still has access. They need a live SPE environment to falsify.</para>
/// </summary>
public class SpeRevokeMatcherTests
{
    private const string GrantEntitySet = "sprk_externalrecordaccesses";
    private const string ContactEntitySet = "contacts";
    private const string TenantId = "00000000-0000-0000-0000-0000000000cc";
    private const string ContactEmail = "external.counsel@clientfirm.com";

    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ContainerId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid AccessRecordId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static IConfiguration ClientConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            // auth-v4 (master) made DataverseWebApiClient select its credential from THIS FLAG
            // rather than from the presence of a client secret, and it now THROWS when Managed
            // Identity is disabled and neither a TokenCredential nor an IConfidentialClientProvider
            // is supplied. Enabling it takes the MI branch, whose DefaultAzureCredential is
            // constructed lazily and never authenticates — this client is fully stubbed.
            ["Graph:ManagedIdentity:Enabled"] = "true",
            ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
            ["API_CLIENT_SECRET"] = "test-secret",
            ["TENANT_ID"] = TenantId
        }).Build();

    // ─────────────────────────────────────────────────────────────────────────────
    // Test doubles
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A Dataverse client that answers the grant read/sweep and the contact-email lookup.
    /// </summary>
    private static Mock<DataverseWebApiClient> DataverseFor(
        Guid? contactOnGrantRow, string? contactEmail, bool emailLookupThrows = false,
        Guid? boundOid = null, int? identityPlane = null)
    {
        var mock = new Mock<DataverseWebApiClient>(
            ClientConfig(), NullLogger<DataverseWebApiClient>.Instance,
                // Moq matches a class-proxy constructor EXACTLY; master's auth-v4 widened this
                // ctor with two OPTIONAL params (TokenCredential, IConfidentialClientProvider)
                // and optional args do not participate in proxy ctor selection. Passed
                // explicitly as null: this double never authenticates.
                // Positional and null-forgiving, both deliberately. Mock<T> takes `params object[]`
                // for the proxied type's ctor args, so (a) NAMED arguments bind to Mock's own ctor
                // and fail CS1739, and (b) a bare null literal fails CS8625 against the
                // non-nullable element type. This double never authenticates, so both credential
                // slots are genuinely unused.
                null!, null!);

        var grantRow = new ExternalGrantRow
        {
            Id = AccessRecordId,
            ProjectId = ProjectId,
            ContactId = contactOnGrantRow,
            OrganizationId = contactOnGrantRow is null ? OrganizationId : null,
            StateCode = 0
        };

        mock.Setup(c => c.RetrieveAsync<ExternalGrantRow>(
                GrantEntitySet, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grantRow);

        mock.Setup(c => c.QueryAsync<ExternalGrantRow>(
                GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalGrantRow> { grantRow });

        mock.Setup(c => c.UpdateAsync(
                GrantEntitySet, It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var emailSetup = mock.Setup(c => c.RetrieveAsync<RevokeExternalAccessEndpoint.ContactEmailRow>(
            ContactEntitySet, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()));

        if (emailLookupThrows)
            emailSetup.ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));
        else
            emailSetup.ReturnsAsync(new RevokeExternalAccessEndpoint.ContactEmailRow
            {
                emailaddress1 = contactEmail,
                // #2105: the bound Entra object id is the strongest key. A plane marker with no oid is the masking
                // signature (Unreadable), which tests pass explicitly through identityPlane.
                sprk_externalobjectid = boundOid?.ToString(),
                sprk_identityplane = identityPlane ?? (boundOid is null ? null : (int)IdentityPlaneMarker.Workforce),
            });

        // THROW on the membership junction rather than answering it (task 020). Moq's loose default for
        // Task<List<T>> is an EMPTY list, which would let a contact-path double silently satisfy an
        // organization expansion — a permissive default standing in for behaviour nobody modelled. That
        // is the class of hole task 021 found in its own fake (it ignored $top and the team predicates),
        // and it would make "did the contact path accidentally start expanding organizations?"
        // unanswerable. The contact path must never touch this table; if it does, this fails loudly.
        mock.Setup(c => c.QueryAsync<ExternalOrganizationMembership.ContactOrganizationRow>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "The per-contact revoke path must not query sprk_contactorganizations."));

        return mock;
    }

    /// <summary>
    /// The membership service, substituted at its <c>virtual</c> seam. <c>CapturedKey</c> is the point:
    /// A-13 was entirely about WHICH key the revoke matched on, so the tests assert the key that was
    /// actually passed, not merely that some call happened. #2105: the key is the contact's bound object id plus
    /// its email; <c>CapturedEmail</c> is its email half.
    /// </summary>
    private sealed class SpeServiceStub
    {
        public SpeMembershipKey? CapturedKey { get; private set; }
        public string? CapturedEmail => CapturedKey?.Email;
        public string? CapturedContainerId { get; private set; }
        public int CallCount { get; private set; }

        public Mock<SpeContainerMembershipService> Build(SpeContainerMembershipResult result)
        {
            var mock = new Mock<SpeContainerMembershipService>(
                TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<SpeContainerMembershipService>.Instance);

            mock.Setup(s => s.RevokeMembershipAsync(
                    It.IsAny<string>(), It.IsAny<SpeMembershipKey>(), It.IsAny<DataverseWebApiClient>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string containerId, SpeMembershipKey key, DataverseWebApiClient _, CancellationToken _) =>
                {
                    CallCount++;
                    CapturedContainerId = containerId;
                    CapturedKey = key;
                    return result;
                });

            // ISS-004 (#968), task 024: the ORGANIZATION sweep now batches into one call. Modelled
            // here for the same reason as above — an un-stubbed virtual returns a null Task and the
            // endpoint NREs, which is a fixture gap masquerading as a product defect.
            mock.Setup(s => s.RemoveMembershipsAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyCollection<SpeMembershipKey>>(), It.IsAny<DataverseWebApiClient>(),
                    It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string containerId, IReadOnlyCollection<SpeMembershipKey> keys, DataverseWebApiClient _,
                    bool _, CancellationToken _) =>
                {
                    var results = new Dictionary<SpeMembershipKey, SpeContainerMembershipResult>();

                    foreach (var key in keys)
                    {
                        CallCount++;
                        CapturedContainerId = containerId;
                        CapturedKey = key;
                        results[key] = result;
                    }

                    return new SpeMembershipRemoval(results, SpeContainerContactPermissionState.ContactPermissionRemains);
                });

            return mock;
        }
    }

    private static HttpContext AuthenticatedContext()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("tid", TenantId) }, authenticationType: "Test"))
        };
        return context;
    }

    /// <summary>
    /// The grant root's container is DERIVED (uac-r2 task 166, amendment b) — the request no longer carries one.
    /// The default resolver models a SECURE project owning <see cref="ContainerId"/>, which is the world every test
    /// in this file was written against (the client used to send that id).
    /// </summary>
    private static RecordContainerResolver SecureProjectOwning(Guid containerId) =>
        TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, containerId.ToString());

    private static Task<IResult> Revoke(
        Mock<DataverseWebApiClient> dataverse,
        Mock<SpeContainerMembershipService> spe,
        Guid? contactId = null,
        ExternalParticipationService? participations = null,
        RecordContainerResolver? resolver = null,
        IContactIdentityStore? identityStore = null,
        bool withoutIdentityStore = false) =>
        RevokeExternalAccessEndpoint.RevokeAccessAsync(
            new RevokeAccessRequest(AccessRecordId, contactId ?? ContactId, ProjectId),
            dataverse.Object, spe.Object,
            participations ?? new GrantPolicyTestDoubles.FlagStubParticipationService(RootRecordFlags.None),
            AssignedAccessTestDoubles.InertMaterializer(),
            resolver ?? SecureProjectOwning(ContainerId),
            AuthenticatedContext(), NullLogger<Program>.Instance, CancellationToken.None,
            identityStore: withoutIdentityStore ? null : identityStore ?? ProvenIdentityStore());

    /// <summary>An identity store whose identity columns' read is proven (no marked rows): an empty binding is "no binding".</summary>
    private static InMemoryContactIdentityStore ProvenIdentityStore() => new();

    private static RevokeAccessResponse Body(IResult result) =>
        result.Should().BeOfType<Ok<RevokeAccessResponse>>().Subject.Value!;

    /// <summary>
    /// M2 (task 024 → task 065): the <see cref="SpeContainerRevokeOutcome.Failed"/> path is now a 500
    /// ProblemDetails, not a 200 <see cref="RevokeAccessResponse"/> — <see cref="Body"/> intentionally
    /// stays narrow (it must keep failing loudly if a non-Failed test regresses to a Problem shape), and
    /// this sibling asserts the Problem shape for the tests that were flipped by M2. Mirrors
    /// <c>ProjectClosureCascadeTests.Problem</c>'s exact convention (<c>.ProblemDetails.Extensions[...]</c>).
    /// </summary>
    private static ProblemHttpResult ProblemBody(IResult result) =>
        result.Should().BeOfType<ProblemHttpResult>().Subject;

    private static readonly SpeContainerMembershipResult Removed =
        new(true, "permission-abc-123", null);

    private static readonly SpeContainerMembershipResult NotFound =
        new(false, null, $"No permission found for user '{ContactEmail}' in container.");

    private static readonly SpeContainerMembershipResult GraphError =
        new(false, null, "Graph API error (503): service unavailable");

    // ─────────────────────────────────────────────────────────────────────────────
    // A-13 — FLIPPED BY TASK 017 (FR-16). Match on the email, not the GUID.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ FLIPPED — the pre-fix matcher searched for the contact's GUID inside the UPN and so never
    /// matched. This is the whole finding in one assertion: the key handed to the SPE revoke is the
    /// contact's EMAIL, which is the key membership is written with.
    /// </summary>
    [Fact]
    public async Task Revoke_MatchesTheSpePermissionOnTheContactEmail()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        await Revoke(DataverseFor(ContactId, ContactEmail), spe);

        stub.CapturedEmail.Should().Be(ContactEmail,
            "SPE membership is written with userPrincipalName = the contact's email, so the revoke must " +
            "match on that key (A-13)");
        stub.CapturedContainerId.Should().Be(ContainerId.ToString());
    }

    /// <summary>
    /// The negative half of the same assertion, stated explicitly because it is the exact shape of the
    /// bug: the contact's GUID must not be what gets matched on.
    /// </summary>
    [Fact]
    public async Task Revoke_DoesNotMatchTheSpePermissionOnTheContactGuid()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        await Revoke(DataverseFor(ContactId, ContactEmail), spe);

        stub.CapturedEmail.Should().NotContain(ContactId.ToString(),
            "an email never contains the contact GUID — matching on it is why the old predicate could " +
            "never find a permission");
    }

    /// <summary>
    /// FR-16 acceptance: a revoke removes the corresponding container permission, and the response says so.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenThePermissionIsRemoved_ReportsPermissionRemoved()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        var result = await Revoke(DataverseFor(ContactId, ContactEmail), spe);

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.PermissionRemoved);
        body.SpeContainerMembershipRevoked.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — never claim an SPE revoke that did not happen (ADR-003).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FR-16 acceptance, negative case, and this task's ADR-003 constraint verbatim: when no matching
    /// permission is found the endpoint must report genuinely-absent, NOT SPE-revoke success.
    ///
    /// <para>Pre-fix this returned <c>true</c>. That is what made A-13 invisible — the endpoint said
    /// "revoked" for a permission it never even located.</para>
    /// </summary>
    [Fact]
    public async Task Revoke_WhenNoPermissionMatches_ReportsGenuinelyAbsentNotSuccess()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(NotFound);

        var result = await Revoke(DataverseFor(ContactId, ContactEmail), spe);

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.NoPermissionFound,
            "the container was read and this contact holds no permission — expected under broker-only");
        body.SpeContainerMembershipRevoked.Should().BeFalse(
            "nothing was removed, so the flag must not claim otherwise (A-13's false success)");
    }

    /// <summary>
    /// A Graph failure is NOT the same as "no permission". The contact may still hold file access, so it
    /// must be reported as a failure the operator can act on — the distinction ADR-003 asks for.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenGraphFails_ReportsFailedAsA500Problem()
    {
        // ✅ INVERTED BY M2 (task 024 → task 065). Was
        // Revoke_WhenGraphFails_ReportsFailedRatherThanAbsent, asserting a 200
        // Ok<RevokeAccessResponse> with SpeContainerOutcome.Failed buried in the body. /revoke now aligns
        // with /close-project for this exact shape: "we could not tell" is a 500, not a 200.
        var stub = new SpeServiceStub();
        var spe = stub.Build(GraphError);

        var result = await Revoke(DataverseFor(ContactId, ContactEmail), spe);

        var problem = ProblemBody(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError,
            "'we could not tell' must never be reported as a 200 'there was nothing there'");
        problem.ProblemDetails.Extensions["reasonCode"].Should()
            .Be(RevokeExternalAccessEndpoint.RevokeSpeCleanupIncompleteReason);
        problem.ProblemDetails.Extensions["speContainerOutcome"].Should().Be(SpeContainerRevokeOutcome.Failed);
        problem.ProblemDetails.Extensions["deactivatedCount"].Should().Be(1,
            "the Dataverse row WAS deactivated even though the SPE half failed — owner directive 2026-09-10");
    }

    /// <summary>
    /// Task 137 criterion 10, second clause (verifier r3 finding 3): a FAILED revoke whose grant-cache invalidation
    /// ALSO fails still returns its own ProblemDetails — the same status, reason code, title, detail and extensions as
    /// the same failed revoke over a healthy cache. Both runs go through the PRODUCTION invalidation routine
    /// (<see cref="GrantPolicyTestDoubles.RealInvalidationOver"/>); only the cache differs, and in the faulted run
    /// every removal throws.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenGraphFails_AndEveryCacheRemovalThrows_StillReturnsTheSameProblem()
    {
        var healthyCache = new TenantCache(
            new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache(
                Microsoft.Extensions.Options.Options.Create(
                    new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions())),
            NullLogger<TenantCache>.Instance);
        var healthy = ProblemBody(await Revoke(
            DataverseFor(ContactId, ContactEmail), new SpeServiceStub().Build(GraphError),
            participations: GrantPolicyTestDoubles.RealInvalidationOver(healthyCache, AuthenticatedContext())));

        var throwingCache = new Mock<ITenantCache>();
        throwingCache
            .Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("simulated Redis timeout"));

        var faulted = ProblemBody(await Revoke(
            DataverseFor(ContactId, ContactEmail), new SpeServiceStub().Build(GraphError),
            participations: GrantPolicyTestDoubles.RealInvalidationOver(throwingCache.Object, AuthenticatedContext())));

        throwingCache.Invocations.Should().NotBeEmpty("precondition: the removals were attempted, and threw");
        faulted.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        faulted.ProblemDetails.Extensions["reasonCode"].Should()
            .Be(RevokeExternalAccessEndpoint.RevokeSpeCleanupIncompleteReason,
                "the failed revoke's own message survives a failed invalidation — never a bare 500");
        faulted.StatusCode.Should().Be(healthy.StatusCode);
        faulted.ProblemDetails.Title.Should().Be(healthy.ProblemDetails.Title);
        faulted.ProblemDetails.Detail.Should().Be(healthy.ProblemDetails.Detail);
        // traceId is per request by design (ADR-019), so it is the one extension that legitimately differs.
        static Dictionary<string, object?> WithoutTraceId(IDictionary<string, object?> extensions) =>
            extensions.Where(kv => kv.Key != "traceId").ToDictionary(kv => kv.Key, kv => kv.Value);
        faulted.ProblemDetails.Extensions.Should().ContainKey("traceId");
        WithoutTraceId(faulted.ProblemDetails.Extensions).Should().BeEquivalentTo(
            WithoutTraceId(healthy.ProblemDetails.Extensions),
            "an invalidation failure is non-fatal: the failed revoke's problem is unchanged");
    }

    /// <summary>
    /// ✅ FLIPPED BY #2103 (was <c>Revoke_WhenTheContactHasNoEmail_ReportsFailedAsA500Problem</c>). Spaarke never
    /// writes a contact's container permission, and a hand-made one is matched by the contact's bound object id or its
    /// email (#2105) — so for a contact with neither (no binding here, and no email: never had one, or it was removed
    /// after the grant) nothing on the container can be theirs. The container step is <see cref="SpeContainerRevokeOutcome.NotAttempted"/> and the revoke answers 200.
    /// It used to answer 500 <c>container_not_cleared</c> + "retry", which a permanent missing email made
    /// impossible to satisfy (UAC 099b live gate, 2026-10-19).
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Revoke_WhenTheContactHasNoEmail_HasNothingToClear_AndAnswers200NotARetryable500(string? email)
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(NotFound);

        var result = await Revoke(DataverseFor(ContactId, contactEmail: email), spe);

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.NotAttempted,
            "the container was not read for this contact — NoPermissionFound would claim a read that never happened");
        body.SpeContainerMembershipRevoked.Should().BeFalse("nothing was removed");
        body.DeactivatedCount.Should().Be(1, "the Dataverse half of the revoke still ran");
        stub.CallCount.Should().Be(0,
            "with no key to match on there is nothing to ask Graph — and asking with an empty key could " +
            "match the wrong permission");
    }

    /// <summary>
    /// #2105 (was <c>Revoke_WhenTheContactsEmailChangedAfterTheGrant_MatchesTheCurrentEmail</c>, which only showed the
    /// current email was passed). A contact whose email changed after an admin added their permission is still matched:
    /// the revoke hands the service the contact's BOUND Entra object id, the strongest key, with the current email.
    /// The matcher half — the permission made under the former address is found by that object id — is
    /// <c>SpePermissionMatcherTests.FindContactPermission_WhenTheEmailChangedAfterTheGrant_MatchesByTheObjectId</c>
    /// and, through the real service, <c>SpeContainerPagingTests.Revoke_WhenTheEmailChanged_RemovesThePermissionByTheObjectId</c>.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenTheContactsEmailChangedAfterTheGrant_MatchesByTheBoundObjectId()
    {
        const string currentEmail = "renamed.counsel@clientfirm.com";
        var boundOid = Guid.Parse("0b0b0b0b-1111-2222-3333-444444444444");
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        var result = await Revoke(DataverseFor(ContactId, currentEmail, boundOid: boundOid), spe);

        stub.CallCount.Should().Be(1);
        stub.CapturedKey!.ObjectId.Should().Be(boundOid,
            "the bound object id survives an email change; it is the key the permission is matched by first");
        stub.CapturedKey.Email.Should().Be(currentEmail);
        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.PermissionRemoved);
    }

    /// <summary>
    /// #2105: a contact with a bound object id and NO email still has a key, so the container IS read for them — it is
    /// not the #2103 "nothing to clear" answer, which needs both keys absent.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenTheContactHasABoundObjectIdButNoEmail_StillRevokesByTheObjectId()
    {
        var boundOid = Guid.Parse("0c0c0c0c-1111-2222-3333-444444444444");
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        var result = await Revoke(DataverseFor(ContactId, contactEmail: null, boundOid: boundOid), spe);

        stub.CallCount.Should().Be(1, "an object id alone is a key: 'no key' is no object id AND no email");
        stub.CapturedKey.Should().Be(new SpeMembershipKey(boundOid, null));
        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.PermissionRemoved);
    }

    /// <summary>
    /// #2105: a binding that cannot be read (a plane marker with no readable object id — the field-security masking
    /// signature) is not "no object id". With no email either, the contact may hold a permission under an object id
    /// this read cannot see, so "nothing to clear" would be a guess: Failed, a 500, and no SPE call.
    /// </summary>
    /// <summary>
    /// The binding and its plane are field-secured, so a hidden binding reads EMPTY, like none. With no email either,
    /// "nothing to clear" is answered only while the identity columns' read is proven: unproven (the probe reads Masked),
    /// or no identity store to prove it with, is Failed — a 500 and no SPE call, as an unreadable binding is.
    /// </summary>
    [Theory]
    [InlineData(ColumnReadProofOutcome.Unproven, false)]
    [InlineData(ColumnReadProofOutcome.Faulted, false)]
    [InlineData(ColumnReadProofOutcome.Proven, true)]   // no identity store
    public async Task Revoke_WhenTheBindingReadEmptyWithoutProofAndThereIsNoEmail_FailsClosedRatherThanNothingToClear(
        ColumnReadProofOutcome proof, bool withoutIdentityStore)
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);
        var identityStore = new InMemoryContactIdentityStore { IdentityColumnsProof = proof };

        var result = await Revoke(DataverseFor(ContactId, contactEmail: null), spe,
            identityStore: identityStore, withoutIdentityStore: withoutIdentityStore);

        var problem = ProblemBody(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["speContainerOutcome"].Should().Be(SpeContainerRevokeOutcome.Failed);
        stub.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Revoke_WhenTheBindingIsUnreadableAndThereIsNoEmail_FailsClosedRatherThanNothingToClear()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        var result = await Revoke(
            DataverseFor(ContactId, contactEmail: null, identityPlane: (int)IdentityPlaneMarker.External), spe);

        var problem = ProblemBody(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["speContainerOutcome"].Should().Be(SpeContainerRevokeOutcome.Failed);
        stub.CallCount.Should().Be(0);
    }

    /// <summary>
    /// #2104 (pass-1 K3): the contact whose permission is cleared is the grant ROW's contact, never the one the request
    /// names. A request naming a DIFFERENT contact must not redirect the cleanup to that contact's permission, and must
    /// not leave the row's contact uncleared.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenTheRequestNamesADifferentContact_ClearsTheGrantRowsContact()
    {
        var otherContact = Guid.Parse("99999999-1111-1111-1111-111111111111");
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);
        var dataverse = DataverseFor(ContactId, ContactEmail);

        var result = await Revoke(dataverse, spe, contactId: otherContact);

        dataverse.Verify(c => c.RetrieveAsync<RevokeExternalAccessEndpoint.ContactEmailRow>(
            ContactEntitySet, ContactId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "the row's contact is the one whose identity key is read");
        dataverse.Verify(c => c.RetrieveAsync<RevokeExternalAccessEndpoint.ContactEmailRow>(
            ContactEntitySet, otherContact, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "the request's contact is logged only");
        stub.CallCount.Should().Be(1);
        stub.CapturedEmail.Should().Be(ContactEmail);
        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.PermissionRemoved);
    }

    /// <summary>
    /// #2104 (pass-1 K3): a contact-grant revoke whose request names NO contact still clears the row's contact. It used
    /// to answer <c>NotAttempted</c> — a 200 that left the grantee's permission on the container.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAContactGrant_WhenTheRequestNamesNoContact_StillClearsTheGrantRowsContact()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        var result = await Revoke(DataverseFor(ContactId, ContactEmail), spe, contactId: Guid.Empty);

        stub.CallCount.Should().Be(1);
        stub.CapturedEmail.Should().Be(ContactEmail);
        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.PermissionRemoved);
    }

    /// <summary>
    /// A failure reading the contact is the same class: the key could not be obtained, so the permission
    /// state is unknown.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenTheEmailLookupFails_ReportsFailedAsA500ProblemAndCallsNoSpeRevoke()
    {
        // ✅ INVERTED BY M2 (task 024 → task 065) — see Revoke_WhenGraphFails_ReportsFailedAsA500Problem.
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        var result = await Revoke(
            DataverseFor(ContactId, ContactEmail, emailLookupThrows: true), spe);

        ProblemBody(result).ProblemDetails.Extensions["speContainerOutcome"].Should()
            .Be(SpeContainerRevokeOutcome.Failed);
        stub.CallCount.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NotAttempted — honest about the cases where there is nothing to match.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ RE-BASED BY uac-r2 TASK 166 (amendment b) — was <c>Revoke_WithNoContainerId_ReportsNotAttempted</c>.
    /// The request no longer names a container; the grant root's container is derived. A NON-secure root derives
    /// the SHARED business-unit container, which one revoke must never sweep (the grantee may hold other grants it
    /// serves) — so no permission is in scope: <c>NotAttempted</c>, a 200, and no SPE call at all. Distinct from
    /// "we looked and found none".
    /// </summary>
    [Fact]
    public async Task Revoke_OfANonSecureRoot_ReportsNotAttemptedAndNeverTouchesTheSharedContainer()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        var result = await Revoke(DataverseFor(ContactId, ContactEmail), spe,
            resolver: TestRecordContainerResolver.ForNonSecureRecord("sprk_project", ProjectId));

        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.NotAttempted);
        stub.CallCount.Should().Be(0,
            $"the shared business-unit container ({TestRecordContainerResolver.SharedBusinessUnitContainer}) is never swept");
    }

    /// <summary>
    /// uac-r2 task 166 (amendment b): "we could not tell which container" is not "nothing to clean". A SECURE root
    /// with no container (the resolver's FailClosed refusal), a typed resolver refusal and a resolver fault each
    /// report <c>Failed</c> — the M2 500 with the deactivated count — and no SPE call is made against a guessed
    /// container.
    /// </summary>
    [Theory]
    [InlineData("secure-without-container")]
    [InlineData("resolver-problem")]
    [InlineData("resolver-fault")]
    public async Task Revoke_WhenTheRootContainerCannotBeDetermined_ReportsFailedAndCallsNoSpe(string shape)
    {
        var resolver = shape switch
        {
            "secure-without-container" => TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, ownContainerId: null),
            "resolver-problem" => TestRecordContainerResolver.Throwing(new Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException(
                "container_ownership_indeterminate", "Indeterminate", "test", 409)),
            _ => TestRecordContainerResolver.Throwing(new TimeoutException("metadata timed out")),
        };
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        var result = await Revoke(DataverseFor(ContactId, ContactEmail), spe, resolver: resolver);

        var problem = ProblemBody(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Extensions["speContainerOutcome"].Should().Be(SpeContainerRevokeOutcome.Failed);
        problem.ProblemDetails.Extensions["deactivatedCount"].Should().Be(1,
            "the Dataverse grant sweep ran before the container step and still counts");
        stub.CallCount.Should().Be(0, "no container was established, so none may be touched");
    }

    /// <summary>
    /// uac-r2 task 166 (amendment b): the client's <c>containerId</c> is GONE from the contract. A body that still
    /// carries one — another record's container — deserializes (unknown members are skipped) and is IGNORED: the
    /// only container the SPE step ever touches is the one derived from the authorized grant's root.
    /// </summary>
    [Fact]
    public async Task Revoke_AClientSuppliedContainerIdIsIgnored_TheDerivedContainerIsTheOnlyOneTouched()
    {
        const string victimContainer = "b!some-other-matters-container";
        var request = System.Text.Json.JsonSerializer.Deserialize<RevokeAccessRequest>(
            $"{{\"accessRecordId\":\"{AccessRecordId}\",\"contactId\":\"{ContactId}\",\"projectId\":\"{ProjectId}\","
            + $"\"containerId\":\"{victimContainer}\"}}",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        request.Should().NotBeNull();

        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        await RevokeExternalAccessEndpoint.RevokeAccessAsync(
            request!, DataverseFor(ContactId, ContactEmail).Object, spe.Object,
            new GrantPolicyTestDoubles.FlagStubParticipationService(RootRecordFlags.None),
            AssignedAccessTestDoubles.InertMaterializer(), SecureProjectOwning(ContainerId),
            AuthenticatedContext(), NullLogger<Program>.Instance, CancellationToken.None);

        stub.CallCount.Should().Be(1);
        stub.CapturedContainerId.Should().Be(ContainerId.ToString())
            .And.NotBe(victimContainer);
    }

    /// <summary>
    /// ✅ FLIPPED BY TASK 020 (FR-16b) — was
    /// <c>Revoke_OfAnOrganizationGrant_ReportsNotAttemptedRatherThanSuccess</c>.
    ///
    /// <para><b>What was wrong.</b> An org revoke passes <c>ContactId = Guid.Empty</c> (task 073 #7), so
    /// there was no single grantee, no single email, and nothing to match — the endpoint reported
    /// <c>NotAttempted</c> and touched no container permission at all. Honest, but incomplete: the
    /// Dataverse sweep revoked the grant for every member while every member's container ACL stayed
    /// exactly where it was. Task 017 assessed and FILED this; task 020 closes it.</para>
    ///
    /// <para>The organization is now expanded to its active members and each member's permission is
    /// removed, so <c>NotAttempted</c> is no longer reachable for an org revoke that supplied a
    /// <c>ContainerId</c>. This assertion is the flip: seeing <c>NotAttempted</c> here again would mean
    /// the expansion was skipped.</para>
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_NoLongerReportsNotAttempted()
    {
        var org = new OrgRevokeFixture(MemberOne, MemberTwo, MemberThree);
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        var result = await org.Revoke(spe);

        Body(result).SpeContainerOutcome.Should().NotBe(SpeContainerRevokeOutcome.NotAttempted,
            "the organization is expanded to its members now — NotAttempted would mean the expansion " +
            "was skipped and every member kept their container permission (task 017 §6, FR-16b)");
        stub.CallCount.Should().BeGreaterThan(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FR-16b (task 020) — an ORGANIZATION revoke cleans up EVERY active member.
    //
    // These use their own double rather than DataverseFor: an org revoke reads a table the contact path
    // never touches, and the double must ANSWER FROM A MODEL and THROW on anything unmodelled. A double
    // that shrugs at an unrecognised $filter or ignores $top cannot detect a change in what the query
    // DOES — only in what someone thought it was for.
    // ─────────────────────────────────────────────────────────────────────────────

    private const string JunctionEntitySet = "sprk_contactorganizations";

    private static readonly Guid MemberOne = Guid.Parse("44444444-0000-0000-0000-000000000001");
    private static readonly Guid MemberTwo = Guid.Parse("44444444-0000-0000-0000-000000000002");
    private static readonly Guid MemberThree = Guid.Parse("44444444-0000-0000-0000-000000000003");

    /// <summary>The personal grant row of <see cref="MemberOne"/> on the SAME root (task-010 invariant).</summary>
    private static readonly Guid PersonalGrantId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000f");

    /// <summary>
    /// Member emails, held as an explicit map rather than derived from the GUID.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This was a hole in this very double.</b> The first version derived the address as
    /// <c>$"member-{memberId.ToString()[..8]}@…"</c> — and the three member GUIDs share their first
    /// eight characters, so all three "distinct" members had the SAME email. The all-members-removed test
    /// passed on one address matched three times, and per-member routing could not be expressed at all.
    /// Only <see cref="Revoke_OfAnOrganizationGrant_WhenOneMemberFails_ReportsFailedAndStillCleansTheOthers"/>
    /// — which needs to fail exactly ONE member — surfaced it. A double that derives its identities can
    /// collide them; one that states them cannot.
    /// </remarks>
    private static readonly Dictionary<Guid, string> MemberEmails = new()
    {
        [MemberOne] = "alice.partner@clientfirm.com",
        [MemberTwo] = "bob.associate@clientfirm.com",
        [MemberThree] = "carol.paralegal@clientfirm.com",
    };

    private static string EmailFor(Guid memberId) => MemberEmails[memberId];

    /// <summary>
    /// A Dataverse double for the ORGANIZATION revoke path, modelling three tables:
    /// the grant rows (org grant + a member's personal grant on the same root), the
    /// <c>sprk_contactorganization</c> junction, and contact emails.
    /// </summary>
    /// <remarks>
    /// Every seam validates its input and throws on anything it does not model. The junction setup in
    /// particular asserts the production <c>$filter</c> and <c>$top</c> rather than accepting whatever
    /// arrives: a stale column name in a revocation query reads as "nothing to revoke" and is silent, and
    /// dropping the bound turns a truncated sweep into one that looks complete.
    /// </remarks>
    private sealed class OrgRevokeFixture
    {
        private readonly Dictionary<Guid, string?> _members = new();
        private readonly List<ExternalGrantRow> _grantRows = new();

        public Mock<DataverseWebApiClient> Dataverse { get; }

        /// <summary>Ids passed to <c>UpdateAsync</c> — i.e. the rows the Dataverse sweep deactivated.</summary>
        public List<Guid> DeactivatedRowIds { get; } = new();

        public string? CapturedJunctionFilter { get; private set; }
        public int? CapturedJunctionTop { get; private set; }
        public int JunctionQueryCount { get; private set; }

        public OrgRevokeFixture(
            params Guid[] members)
            : this(junctionThrows: false, membersWithoutEmail: null, overflowRows: 0, members) { }

        public OrgRevokeFixture(
            bool junctionThrows,
            IReadOnlyCollection<Guid>? membersWithoutEmail,
            int overflowRows,
            params Guid[] members)
        {
            foreach (var m in members)
                _members[m] = membersWithoutEmail?.Contains(m) == true ? null : EmailFor(m);

            // The ORG grant being revoked, and a member's PERSONAL grant on the same root. Both are
            // active; only the first belongs to the logical grant under revocation.
            _grantRows.Add(new ExternalGrantRow
            {
                Id = AccessRecordId,
                ProjectId = ProjectId,
                ContactId = null,
                OrganizationId = OrganizationId,
                StateCode = 0
            });
            _grantRows.Add(new ExternalGrantRow
            {
                Id = PersonalGrantId,
                ProjectId = ProjectId,
                ContactId = MemberOne,
                OrganizationId = OrganizationId,
                StateCode = 0
            });

            Dataverse = new Mock<DataverseWebApiClient>(
                ClientConfig(), NullLogger<DataverseWebApiClient>.Instance, null!, null!);

            Dataverse.Setup(c => c.RetrieveAsync<ExternalGrantRow>(
                    GrantEntitySet, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string _, CancellationToken _) =>
                    _grantRows.SingleOrDefault(r => r.Id == id));

            // Interprets the production filter from ExternalGrantKey.ToActiveRowsFilter rather than
            // returning a fixed set — otherwise the task-010 isolation assertion below would be vacuous.
            Dataverse.Setup(c => c.QueryAsync<ExternalGrantRow>(
                    GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string? filter, string? _, int? _, int? _, CancellationToken _) =>
                    MatchGrantRows(filter));

            Dataverse.Setup(c => c.UpdateAsync(
                    GrantEntitySet, It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid id, object _, CancellationToken _) =>
                {
                    DeactivatedRowIds.Add(id);
                    return Task.CompletedTask;
                });

            var junction = Dataverse.Setup(c => c.QueryAsync<ExternalOrganizationMembership.ContactOrganizationRow>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()));

            if (junctionThrows)
            {
                junction.ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));
            }
            else
            {
                junction.ReturnsAsync((
                    string entitySet, string? filter, string? select,
                    int? top, int? _, CancellationToken _) =>
                {
                    JunctionQueryCount++;
                    CapturedJunctionFilter = filter;
                    CapturedJunctionTop = top;

                    if (entitySet != JunctionEntitySet)
                        throw new InvalidOperationException(
                            $"Membership must be read from '{JunctionEntitySet}', not '{entitySet}'.");

                    // The predicate is the whole query — answering regardless of it would make a stale
                    // column name (the exact silent failure this task's constraint names) undetectable.
                    if (filter is null || !filter.Contains($"_sprk_organization_value eq {OrganizationId}", StringComparison.Ordinal))
                        throw new InvalidOperationException($"Unmodelled junction $filter: '{filter}'.");

                    if (!filter.Contains("statecode eq 0", StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Junction $filter must restrict to ACTIVE memberships: '{filter}'.");

                    if (select is null || !select.Contains("_sprk_contact_value", StringComparison.Ordinal))
                        throw new InvalidOperationException($"Unmodelled junction $select: '{select}'.");

                    // $top is HONOURED, not ignored: it is the only thing standing between a large
                    // organization and a silently truncated sweep, because DataverseWebApiClient.QueryAsync
                    // reads one page and discards @odata.nextLink.
                    if (top is null)
                        throw new InvalidOperationException(
                            "The membership query must be bounded — an unbounded read truncates silently.");

                    var rows = _members.Keys
                        .Select(id => new ExternalOrganizationMembership.ContactOrganizationRow { ContactId = id })
                        .ToList();

                    // Simulates an organization larger than one sweep may handle.
                    for (var i = 0; i < overflowRows; i++)
                        rows.Add(new ExternalOrganizationMembership.ContactOrganizationRow { ContactId = Guid.NewGuid() });

                    return rows.Take(top.Value).ToList();
                });
            }

            Dataverse.Setup(c => c.RetrieveAsync<RevokeExternalAccessEndpoint.ContactEmailRow>(
                    ContactEntitySet, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string _, CancellationToken _) =>
                {
                    // Unknown contact ⇒ the code asked about somebody the model never described. Answering
                    // with a plausible default here would let a wrong member set pass unnoticed.
                    if (!_members.TryGetValue(id, out var email))
                        throw new InvalidOperationException($"Unmodelled contact lookup: {id}.");

                    return new RevokeExternalAccessEndpoint.ContactEmailRow { emailaddress1 = email };
                });
        }

        private List<ExternalGrantRow> MatchGrantRows(string? filter)
        {
            if (filter is null)
                throw new InvalidOperationException("The grant sweep must carry a $filter.");

            var wantsOrg = filter.Contains("_sprk_contact_value eq null", StringComparison.Ordinal);

            if (wantsOrg)
                return _grantRows
                    .Where(r => r.IsActive && r.ProjectId == ProjectId
                                && r.OrganizationId == OrganizationId && r.ContactId is null)
                    .ToList();

            var contactMatch = System.Text.RegularExpressions.Regex.Match(
                filter, @"_sprk_contact_value eq ([0-9a-fA-F-]{36})");

            if (!contactMatch.Success)
                throw new InvalidOperationException($"Unmodelled grant $filter: '{filter}'.");

            var contactId = Guid.Parse(contactMatch.Groups[1].Value);
            return _grantRows.Where(r => r.IsActive && r.ProjectId == ProjectId && r.ContactId == contactId).ToList();
        }

        public Task<IResult> Revoke(Mock<SpeContainerMembershipService> spe) =>
            RevokeExternalAccessEndpoint.RevokeAccessAsync(
                new RevokeAccessRequest(AccessRecordId, Guid.Empty, ProjectId),
                Dataverse.Object, spe.Object, new GrantPolicyTestDoubles.FlagStubParticipationService(RootRecordFlags.None),
                AssignedAccessTestDoubles.InertMaterializer(),
                SecureProjectOwning(ContainerId),
                AuthenticatedContext(), NullLogger<Program>.Instance, CancellationToken.None,
                identityStore: ProvenIdentityStore());
    }

    /// <summary>
    /// Per-member SPE stub: records the key used for EACH member, because "which key" is the whole of
    /// A-13 and the org path multiplies it by the member count.
    /// </summary>
    private sealed class SpeMemberStub
    {
        private readonly Dictionary<string, SpeContainerMembershipResult> _byEmail;
        private readonly SpeContainerMembershipResult _default;

        public List<string> CapturedEmails { get; } = new();
        public int CallCount => CapturedEmails.Count;

        /// <summary>#2104 round 3: the container-level answer every <c>RemoveMembershipsAsync</c> call gives (three-state
        /// since round 4; "one remains" unless a test says otherwise).</summary>
        public SpeContainerContactPermissionState ContainerState { get; init; } = SpeContainerContactPermissionState.ContactPermissionRemains;

        /// <summary>#2104 round 3: when set, <c>RemoveMembershipsAsync</c> throws it.</summary>
        public Exception? RemoveThrows { get; init; }

        /// <summary>#2104 round 3: each <c>RemoveMembershipsAsync</c> call's key count and <c>confirmNoContactPermission</c>.</summary>
        public List<(int Keys, bool Confirm)> RemoveCalls { get; } = new();

        public SpeMemberStub(
            SpeContainerMembershipResult @default,
            Dictionary<string, SpeContainerMembershipResult>? byEmail = null)
        {
            _default = @default;
            _byEmail = byEmail ?? new Dictionary<string, SpeContainerMembershipResult>();
        }

        public Mock<SpeContainerMembershipService> Build()
        {
            var mock = new Mock<SpeContainerMembershipService>(
                TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<SpeContainerMembershipService>.Instance);

            mock.Setup(s => s.RevokeMembershipAsync(
                    It.IsAny<string>(), It.IsAny<SpeMembershipKey>(), It.IsAny<DataverseWebApiClient>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string containerId, SpeMembershipKey key, DataverseWebApiClient _, CancellationToken _) =>
                {
                    if (containerId != ContainerId.ToString())
                        throw new InvalidOperationException($"Unmodelled container: '{containerId}'.");

                    var email = key.Email ?? string.Empty;
                    CapturedEmails.Add(email);
                    return _byEmail.TryGetValue(email, out var specific) ? specific : _default;
                });

            // ── ISS-004 (#968), task 024 ────────────────────────────────────────────────────────
            // The org sweep now resolves every member's email FIRST and asks for them in ONE call,
            // because the per-member path re-read the whole container each time (N reads, and N ×
            // pages after task 024's paging). The endpoint therefore lands here, not on
            // RevokeMembershipAsync above — which is kept, since the PER-CONTACT revoke path still
            // uses it.
            //
            // CapturedEmails still receives one entry per member, in order, so every existing
            // assertion about WHICH KEY was used — the whole of finding A-13 — is unchanged.
            mock.Setup(s => s.RemoveMembershipsAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyCollection<SpeMembershipKey>>(), It.IsAny<DataverseWebApiClient>(),
                    It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string containerId, IReadOnlyCollection<SpeMembershipKey> keys, DataverseWebApiClient _,
                    bool confirm, CancellationToken _) =>
                {
                    if (containerId != ContainerId.ToString())
                        throw new InvalidOperationException($"Unmodelled container: '{containerId}'.");

                    RemoveCalls.Add((keys.Count, confirm));
                    if (RemoveThrows is not null)
                        throw RemoveThrows;

                    var results = new Dictionary<SpeMembershipKey, SpeContainerMembershipResult>();

                    foreach (var key in keys)
                    {
                        var email = key.Email ?? string.Empty;
                        CapturedEmails.Add(email);
                        results[key] = _byEmail.TryGetValue(email, out var specific) ? specific : _default;
                    }

                    return new SpeMembershipRemoval(results, ContainerState);
                });

            return mock;
        }
    }

    private static SpeContainerMembershipResult NotFoundFor(string email) =>
        new(false, null, $"No permission found for user '{email}' in container.");

    /// <summary>
    /// FR-16b acceptance criterion 1: revoking an organization grant removes the SPE container permission
    /// of EVERY active member, matched on each member's email.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_RemovesEveryActiveMembersPermission()
    {
        var org = new OrgRevokeFixture(MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed);

        var result = await org.Revoke(stub.Build());

        stub.CapturedEmails.Should().BeEquivalentTo(
            new[] { EmailFor(MemberOne), EmailFor(MemberTwo), EmailFor(MemberThree) },
            "every ACTIVE member of the organization loses their container permission, and each is " +
            "matched on the key SPE membership is written with — their email");

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.PermissionRemoved);
        body.SpeContainerMembershipRevoked.Should().BeTrue();
        body.SpeOrgMemberCleanup.Should().Be(
            new SpeOrgMemberCleanupSummary(MembersEnumerated: 3, PermissionsRemoved: 3, PermissionsNotFound: 0, Failed: 0));
    }

    /// <summary>
    /// The negative half, stated separately because it is A-13's exact shape one level up: the key must
    /// be the member's email, never their GUID.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_MatchesMembersOnEmailNotOnGuid()
    {
        var org = new OrgRevokeFixture(MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed);

        await org.Revoke(stub.Build());

        foreach (var memberId in new[] { MemberOne, MemberTwo, MemberThree })
            stub.CapturedEmails.Should().NotContain(e => e.Contains(memberId.ToString(), StringComparison.OrdinalIgnoreCase),
                "an email never contains the contact GUID — matching on it is why the old predicate could never find a permission");
    }

    /// <summary>
    /// FR-16b acceptance criterion 2: one member's removal failing must NOT be reportable as success, and
    /// must NOT stop the others from being cleaned up — stopping early leaves strictly MORE access in place.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_WhenOneMemberFails_ReportsFailedAsA500ProblemAndStillCleansTheOthers()
    {
        // ✅ INVERTED BY M2 (task 024 → task 065) — see Revoke_WhenGraphFails_ReportsFailedAsA500Problem.
        // The per-member sweep behaviour (all three members still attempted) is UNCHANGED; only the
        // outcome's HTTP shape flips from 200-with-Failed-in-body to a 500 Problem.
        var org = new OrgRevokeFixture(MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed, new Dictionary<string, SpeContainerMembershipResult>
        {
            [EmailFor(MemberTwo)] = GraphError
        });

        var result = await org.Revoke(stub.Build());

        stub.CallCount.Should().Be(3,
            "a per-member failure must not abort the loop — the other members must still lose access");

        var problem = ProblemBody(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError,
            "'some members retain access' must never be reportable as a 200 SPE success");
        problem.ProblemDetails.Extensions["speContainerOutcome"].Should().Be(SpeContainerRevokeOutcome.Failed,
            "one member retaining file access is enough to make the org cleanup incomplete");
        problem.ProblemDetails.Extensions["speOrgMemberCleanup"].Should().Be(
            new SpeOrgMemberCleanupSummary(MembersEnumerated: 3, PermissionsRemoved: 2, PermissionsNotFound: 0, Failed: 1),
            "the per-member arithmetic must survive the 500 — it is what lets the caller say '2 of 3 removed'");
    }

    /// <summary>
    /// ✅ FLIPPED BY #2103 (was <c>Revoke_OfAnOrganizationGrant_WhenAMemberHasNoEmail_ReportsFailedAsA500Problem</c>).
    /// The per-contact decision, applied per member: a member with no <c>emailaddress1</c> has nothing on the
    /// container to clear, so they are counted in <c>NoMembershipKey</c>, not <c>Failed</c>, and one such member no
    /// longer turns every revoke of the organization into a permanent 500.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_WhenAMemberHasNoEmail_ClearsTheOthersAndAnswers200()
    {
        var org = new OrgRevokeFixture(
            junctionThrows: false, membersWithoutEmail: new[] { MemberTwo }, overflowRows: 0,
            MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed);

        var result = await org.Revoke(stub.Build());

        stub.CapturedEmails.Should().NotContain(string.Empty);
        stub.CallCount.Should().Be(2,
            "with no key to match on there is nothing to ask Graph for that member — and asking with an " +
            "empty key could match the wrong permission");

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.PermissionRemoved);
        body.SpeOrgMemberCleanup.Should().Be(new SpeOrgMemberCleanupSummary(
            MembersEnumerated: 3, PermissionsRemoved: 2, PermissionsNotFound: 0, Failed: 0,
            RetainedByOtherGrant: 0, NoMembershipKey: 1));
    }

    /// <summary>
    /// #2103: an organization whose every member has no email — nothing on the container can be any member's, the
    /// container is not asked, and the outcome is <see cref="SpeContainerRevokeOutcome.NotAttempted"/> (never read),
    /// not <see cref="SpeContainerRevokeOutcome.NoPermissionFound"/> (read, and empty).
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_WhenNoMemberHasAnEmail_HasNothingToClear_AndAnswers200()
    {
        var org = new OrgRevokeFixture(
            junctionThrows: false, membersWithoutEmail: new[] { MemberOne, MemberTwo }, overflowRows: 0,
            MemberOne, MemberTwo);
        var stub = new SpeMemberStub(Removed);

        var result = await org.Revoke(stub.Build());

        stub.CallCount.Should().Be(0);
        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.NotAttempted);
        body.SpeOrgMemberCleanup!.NoMembershipKey.Should().Be(2);
        body.SpeOrgMemberCleanup.Failed.Should().Be(0);
    }

    /// <summary>
    /// FR-16b acceptance criterion 3: when the member list cannot be established, the response reports a
    /// non-success outcome and attempts NO removals. Counts from a partial sweep off an unknown member
    /// list would read like a complete answer.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_WhenMembersCannotBeEnumerated_ReportsFailedAsA500ProblemAndRemovesNothing()
    {
        // ✅ INVERTED BY M2 (task 024 → task 065) — see Revoke_WhenGraphFails_ReportsFailedAsA500Problem.
        // The load-bearing null (MembersEnumerated) must survive the 500 exactly as it survives the 200 —
        // it is the only signal distinguishing "we swept everyone" from "we do not know who to sweep".
        var org = new OrgRevokeFixture(
            junctionThrows: true, membersWithoutEmail: null, overflowRows: 0,
            MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed);

        var result = await org.Revoke(stub.Build());

        stub.CallCount.Should().Be(0);

        var problem = ProblemBody(result);
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError,
            "'we could not tell who the members are' must never be reported as a 200 'there was nothing there'");
        problem.ProblemDetails.Extensions["speContainerOutcome"].Should().Be(SpeContainerRevokeOutcome.Failed);
        var orgCleanup = problem.ProblemDetails.Extensions["speOrgMemberCleanup"]
            .Should().BeOfType<SpeOrgMemberCleanupSummary>().Subject;
        orgCleanup.MembersEnumerated.Should().BeNull(
            "a null member count is what distinguishes 'we never looked' from 'we looked and it was empty' " +
            "— and it must survive the 500 exactly as it survives the 200 (see the serialization test below)");
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(
            RevokeExternalAccessEndpoint.RevokeSpeCleanupIncompleteReason,
            "a member read that faulted is transient — the retryable code, unlike organization_too_large");
    }

    /// <summary>
    /// FR-16b acceptance criterion 4: an organization with no active members revokes cleanly. The fix must
    /// not turn "nothing to do" into an error.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_WithNoActiveMembers_RevokesCleanly()
    {
        var org = new OrgRevokeFixture();
        var stub = new SpeMemberStub(Removed);

        var result = await org.Revoke(stub.Build());

        stub.CallCount.Should().Be(0);

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.NoPermissionFound,
            "the member list was established and nobody held a permission — the ordinary broker-only answer");
        body.SpeContainerMembershipRevoked.Should().BeFalse();
        body.DeactivatedCount.Should().Be(1, "the Dataverse revocation still happened");
        body.SpeOrgMemberCleanup.Should().Be(
            new SpeOrgMemberCleanupSummary(MembersEnumerated: 0, PermissionsRemoved: 0, PermissionsNotFound: 0, Failed: 0));
    }

    /// <summary>
    /// An organization too large for one sweep is REFUSED, not silently truncated. Task 020's escalation
    /// trigger, enforced in code: a truncated sweep that reports success is the exact failure class this
    /// project exists to remove.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_TooLargeToSweep_ReportsFailedAsA500ProblemRatherThanTruncating()
    {
        // ✅ INVERTED BY M2 (task 024 → task 065) — see Revoke_WhenGraphFails_ReportsFailedAsA500Problem.
        var org = new OrgRevokeFixture(
            junctionThrows: false, membersWithoutEmail: null,
            overflowRows: ExternalOrganizationMembership.MaxMembersPerSweep,
            MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed);

        var result = await org.Revoke(stub.Build());

        stub.CallCount.Should().Be(0, "a partial sweep would produce counts that read like a complete answer");

        var problem = ProblemBody(result);
        problem.ProblemDetails.Extensions["speContainerOutcome"].Should().Be(SpeContainerRevokeOutcome.Failed);
        var orgCleanup = problem.ProblemDetails.Extensions["speOrgMemberCleanup"]
            .Should().BeOfType<SpeOrgMemberCleanupSummary>().Subject;
        orgCleanup.MembersEnumerated.Should().BeNull(
            "the member list is a truncation, not the membership — reporting its length would assert " +
            "something we do not know");
    }

    /// <summary>
    /// #2103 follow-up: the too-large organization is a NON-retryable condition — a retry meets the same sweep bound —
    /// so it carries its own reason code and tells an administrator what to do, never "Retry the revoke". It stays
    /// fail-closed: a 500 with the Dataverse count, because members may retain file access.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_TooLargeToSweep_IsANamedNonRetryableOutcome_NotARetry()
    {
        var org = new OrgRevokeFixture(
            junctionThrows: false, membersWithoutEmail: null,
            overflowRows: ExternalOrganizationMembership.MaxMembersPerSweep,
            MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed);

        var problem = ProblemBody(await org.Revoke(stub.Build()));

        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError, "still fail-closed — never a 200");
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(
            RevokeExternalAccessEndpoint.RevokeOrganizationTooLargeReason);
        RevokeExternalAccessEndpoint.RevokeOrganizationTooLargeReason.Should()
            .Be("sdap.revoke.incomplete.organization_too_large", "a stable code clients switch on");
        problem.ProblemDetails.Detail.Should().NotContain("Retry the revoke")
            .And.Contain("Retrying will not change this")
            .And.Contain("administrator");
        problem.ProblemDetails.Extensions["deactivatedCount"].Should().Be(1,
            "the Dataverse half still ran and the caller must still be told so");
    }

    /// <summary>
    /// #2104 round 3 (item 2, the F1 rule close-project applies): an organization too large to sweep is not swept member
    /// by member — but the container is still asked, with no key, whether it holds ANY permission a contact grant could
    /// account for. A complete read that holds none proves no member holds one: 200, NoPermissionFound, nothing removed.
    /// It used to answer organization_too_large on every revoke of such an organization, whatever the container held.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_TooLargeToSweep_ContainerHoldsNoContactPermission_Answers200()
    {
        var org = new OrgRevokeFixture(
            junctionThrows: false, membersWithoutEmail: null,
            overflowRows: ExternalOrganizationMembership.MaxMembersPerSweep,
            MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed) { ContainerState = SpeContainerContactPermissionState.Clean };

        var body = Body(await org.Revoke(stub.Build()));

        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.NoPermissionFound,
            "the container was read in full and holds no permission any member could have");
        body.SpeContainerMembershipRevoked.Should().BeFalse("nothing was removed");
        body.DeactivatedCount.Should().Be(1);
        body.SpeOrgMemberCleanup!.MembersEnumerated.Should().BeNull(
            "the member list is still a truncation — the 200 rests on the container, not on a member count");
        stub.RemoveCalls.Should().Equal(new[] { (0, true) },
            "one container-wide question, with NO key — no member's permission is matched or removed");
        stub.CallCount.Should().Be(0);
    }

    /// <summary>
    /// #2104 round 3 (item 2), the negative: the container definitely still holds a permission a contact grant could
    /// account for, so the members are not confirmed absent and — a retry reads it again — the revoke stays the
    /// non-retryable organization_too_large.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_TooLargeToSweep_WithAContactPermissionLeft_StaysTooLarge()
    {
        var org = new OrgRevokeFixture(
            junctionThrows: false, membersWithoutEmail: null,
            overflowRows: ExternalOrganizationMembership.MaxMembersPerSweep,
            MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed) { ContainerState = SpeContainerContactPermissionState.ContactPermissionRemains };

        var problem = ProblemBody(await org.Revoke(stub.Build()));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(RevokeExternalAccessEndpoint.RevokeOrganizationTooLargeReason);
        problem.ProblemDetails.Extensions["speContainerOutcome"].Should().Be(SpeContainerRevokeOutcome.Failed);
        stub.RemoveCalls.Should().Equal(new[] { (0, true) }, "the container was asked, and did not prove itself clean");
    }

    /// <summary>
    /// #2104 round 4 (F1), fail closed AND truthful: the container question itself faults (it throws, or the service
    /// answers Unknown — a read, marker or internal-user lookup fault, an incomplete read). That proves nothing — never a
    /// 200 — but it is not "a contact permission remains" either: a retry may prove the container clean. So the revoke is
    /// the RETRYABLE container_not_cleared ("Retry the revoke"), never organization_too_large's "Retrying will not change
    /// this". (Round 3 pinned too_large here, which told the caller a retry could not help when it could.)
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Revoke_OfAnOrganizationGrant_TooLargeToSweep_WhenTheContainerQuestionFaults_IsRetryableNotCleared(
        bool throws)
    {
        var org = new OrgRevokeFixture(
            junctionThrows: false, membersWithoutEmail: null,
            overflowRows: ExternalOrganizationMembership.MaxMembersPerSweep,
            MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed)
        {
            ContainerState = throws ? SpeContainerContactPermissionState.Clean : SpeContainerContactPermissionState.Unknown,
            RemoveThrows = throws ? new InvalidOperationException("Graph unreachable") : null,
        };

        var problem = ProblemBody(await org.Revoke(stub.Build()));

        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError, "still fail-closed — never a 200");
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(RevokeExternalAccessEndpoint.RevokeSpeCleanupIncompleteReason);
        problem.ProblemDetails.Extensions["speContainerOutcome"].Should().Be(SpeContainerRevokeOutcome.Failed);
        problem.ProblemDetails.Detail.Should().Contain("Retry the revoke").And.NotContain("Retrying will not change this");
        stub.RemoveCalls.Should().Equal(new[] { (0, true) }, "the container was asked, with no key, and could not answer");
        stub.CallCount.Should().Be(0, "no member was swept");
    }

    /// <summary>
    /// #2104 round 3 (item 1): the contact's match was ambiguous (two eligible permissions) or unverifiable (no holder
    /// object id), so the service removed nothing — and a retry reads the same permissions and refuses again. The revoke
    /// names it (permission_ambiguous), tells an administrator to resolve the container's permissions in the SPE admin
    /// app, never says "retry", and names no person or address. Still fail-closed: a 500 with the Dataverse count.
    /// </summary>
    [Theory]
    [InlineData(SpeContainerMembershipService.AmbiguousMatchError)]
    [InlineData(SpeContainerMembershipService.UnverifiableHolderError)]
    public async Task Revoke_WhenTheMatchIsAmbiguousOrUnverifiable_IsANamedNonRetryableOutcome(string errorPrefix)
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(new SpeContainerMembershipResult(
            false, null, $"{errorPrefix}: the permission(s) matching user '{ContactEmail}' were not removed."));

        var problem = ProblemBody(await Revoke(DataverseFor(ContactId, ContactEmail), spe));

        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError, "still fail-closed — never a 200");
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(RevokeExternalAccessEndpoint.RevokePermissionAmbiguousReason);
        RevokeExternalAccessEndpoint.RevokePermissionAmbiguousReason.Should()
            .Be("sdap.revoke.incomplete.permission_ambiguous", "a stable code clients switch on");
        problem.ProblemDetails.Extensions["speContainerOutcome"].Should().Be(SpeContainerRevokeOutcome.Failed);
        problem.ProblemDetails.Extensions["deactivatedCount"].Should().Be(1);
        problem.ProblemDetails.Detail.Should().NotContain("Retry the revoke")
            .And.Contain("Retrying will not change this")
            .And.Contain("administrator must resolve the container's permissions in the SPE admin app")
            .And.NotContain(ContactEmail, "the detail reaches the caller's UI and names no PII")
            .And.NotContain(ContactId.ToString());
    }

    /// <summary>
    /// #2104 round 3 (item 1), the negative: a transient failure (the internal-user lookup faulted) is NOT an ambiguity —
    /// a retry can succeed — so it stays the retryable container_not_cleared.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenTheInternalUserCheckFailed_StaysTheRetryableContainerNotCleared()
    {
        var spe = new SpeServiceStub().Build(new SpeContainerMembershipResult(
            false, null, $"{SpeContainerMembershipService.InternalUserCheckFailedError}: could not be checked."));

        var problem = ProblemBody(await Revoke(DataverseFor(ContactId, ContactEmail), spe));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(RevokeExternalAccessEndpoint.RevokeSpeCleanupIncompleteReason);
        problem.ProblemDetails.Detail.Should().Contain("Retry the revoke");
    }

    /// <summary>
    /// #2104 round 3 (item 1) on the organization path, with mixed causes: one member's match is ambiguous and another's
    /// delete failed transiently. A retry could clear only the transient one, so the non-retryable code wins (the rule
    /// close-project applies to organization_too_large versus container_not_cleared) and the detail counts the other
    /// member too. Neither member's address appears.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_AnAmbiguousMemberBesideATransientFailure_IsNonRetryableAndCountsTheOther()
    {
        var org = new OrgRevokeFixture(MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed, new Dictionary<string, SpeContainerMembershipResult>
        {
            [EmailFor(MemberOne)] = new(false, null, $"{SpeContainerMembershipService.AmbiguousMatchError}: 2 permissions."),
            [EmailFor(MemberTwo)] = GraphError,
        });

        var problem = ProblemBody(await org.Revoke(stub.Build()));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(RevokeExternalAccessEndpoint.RevokePermissionAmbiguousReason);
        problem.ProblemDetails.Extensions["speOrgMemberCleanup"].Should().Be(
            new SpeOrgMemberCleanupSummary(MembersEnumerated: 3, PermissionsRemoved: 1, PermissionsNotFound: 0, Failed: 2),
            "the ambiguous member is still counted Failed — it may retain file access");
        problem.ProblemDetails.Detail.Should().Contain("1 member(s) of the organization match more than one permission")
            .And.Contain("1 other member(s) could also not be confirmed removed")
            .And.Contain("Retrying will not change this")
            .And.NotContain(EmailFor(MemberOne))
            .And.NotContain(EmailFor(MemberTwo));
    }

    /// <summary>#2104 round 3 (item 1): transient member failures alone stay the retryable code.</summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_OnlyTransientMemberFailures_StaysRetryable()
    {
        var org = new OrgRevokeFixture(MemberOne, MemberTwo);
        var stub = new SpeMemberStub(Removed, new Dictionary<string, SpeContainerMembershipResult>
        {
            [EmailFor(MemberTwo)] = GraphError,
        });

        var problem = ProblemBody(await org.Revoke(stub.Build()));

        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(RevokeExternalAccessEndpoint.RevokeSpeCleanupIncompleteReason);
    }

    /// <summary>
    /// The membership query must ask for THIS organization's ACTIVE memberships. Pinned explicitly
    /// because a stale column name in a revocation query reads as "nothing to revoke" — silently — which
    /// is what three earlier Phase 0 tasks turned on.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_QueriesTheJunctionForThatOrganizationsActiveMembers()
    {
        var org = new OrgRevokeFixture(MemberOne);

        await org.Revoke(new SpeMemberStub(Removed).Build());

        org.JunctionQueryCount.Should().Be(1);
        org.CapturedJunctionFilter.Should().Contain($"_sprk_organization_value eq {OrganizationId}");
        org.CapturedJunctionFilter.Should().Contain("statecode eq 0",
            "a deactivated membership is a FORMER member — sweeping them would revoke access nobody granted");
    }

    /// <summary>
    /// The membership query must be BOUNDED. Asserted on its own, separately from the filter, because it
    /// guards a different failure: <c>DataverseWebApiClient.QueryAsync</c> reads one page and discards
    /// <c>@odata.nextLink</c>, so an unbounded read on a large organization returns a truncated list that
    /// is indistinguishable from a complete one.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_BoundsTheMembershipQuery()
    {
        var org = new OrgRevokeFixture(MemberOne);

        await org.Revoke(new SpeMemberStub(Removed).Build());

        org.CapturedJunctionTop.Should().Be(ExternalOrganizationMembership.MaxMembersPerSweep + 1,
            "asking for one MORE than the bound is what makes 'there are too many' detectable rather " +
            "than silently truncated at the bound");
    }

    /// <summary>
    /// The task-010 isolation invariant: an org revoke must not deactivate a member's PERSONAL grant on
    /// the same root. A person grant and an org grant are DISTINCT logical grants and must never revoke
    /// each other — the <c>_sprk_contact_value eq null</c> clause is what enforces it, and adding SPE
    /// cleanup must not disturb it.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_DoesNotDeactivateAMembersPersonalGrantRow()
    {
        var org = new OrgRevokeFixture(MemberOne, MemberTwo);

        var result = await org.Revoke(new SpeMemberStub(Removed).Build());

        org.DeactivatedRowIds.Should().ContainSingle().Which.Should().Be(AccessRecordId);
        org.DeactivatedRowIds.Should().NotContain(PersonalGrantId,
            "MemberOne's personal grant on the same root is a different logical grant — the org revoke " +
            "must leave it standing (task 010)");
        Body(result).DeactivatedCount.Should().Be(1);
    }

    /// <summary>
    /// The per-contact path carries no member summary. Guards against the new field leaking a summary
    /// that would describe a sweep that never happened.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAContactGrant_ReportsNoOrgMemberSummary()
    {
        var stub = new SpeServiceStub();

        var result = await Revoke(DataverseFor(ContactId, ContactEmail), stub.Build(Removed));

        Body(result).SpeOrgMemberCleanup.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The AGGREGATOR itself, tested directly.
    //
    // Task 017's own lesson, applied pre-emptively: mocking at a seam proves the CALLER handles a state,
    // never that the mapping producing that state is right. These four cases are the closed set — every
    // summary shape maps somewhere, and exactly one shape maps to success.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AggregateOrgOutcome_WhenMembershipIsUnknown_IsFailed() =>
        RevokeExternalAccessEndpoint.AggregateOrgOutcome(new SpeOrgMemberCleanupSummary(null, 0, 0, 0))
            .Should().Be(SpeContainerRevokeOutcome.Failed,
                "we never established who the members are, so nothing can be claimed clean");

    [Fact]
    public void AggregateOrgOutcome_WithAnyFailure_IsFailedEvenAlongsideRemovals() =>
        RevokeExternalAccessEndpoint.AggregateOrgOutcome(new SpeOrgMemberCleanupSummary(12, 11, 0, 1))
            .Should().Be(SpeContainerRevokeOutcome.Failed,
                "eleven people losing access does not make the twelfth's retained access a success");

    [Fact]
    public void AggregateOrgOutcome_WithRemovalsAndNoFailures_IsPermissionRemoved() =>
        RevokeExternalAccessEndpoint.AggregateOrgOutcome(new SpeOrgMemberCleanupSummary(3, 1, 2, 0))
            .Should().Be(SpeContainerRevokeOutcome.PermissionRemoved);

    /// <summary>
    /// The <c>null</c> member count must survive serialization as an explicit <c>null</c>, not vanish.
    /// </summary>
    /// <remarks>
    /// This pins a contract that is easy to break from a distance. <c>MembersEnumerated == null</c> is the
    /// only thing distinguishing "we could not establish the member list" from "the list was empty", and a
    /// <c>DefaultIgnoreCondition = WhenWritingNull</c> added to the app's HTTP JSON options — or a
    /// <c>[JsonIgnore]</c> on the property — would OMIT the field instead. A JS client reading
    /// <c>body.speOrgMemberCleanup.membersEnumerated</c> would then see <c>undefined</c>, and the
    /// natural <c>=== null</c> check would silently stop detecting the case. The BFF configures no
    /// <c>ConfigureHttpJsonOptions</c>, so <see cref="JsonSerializerDefaults.Web"/> is what actually runs.
    /// </remarks>
    [Fact]
    public void SpeOrgMemberCleanupSummary_UnknownMembership_SerializesAnExplicitNull()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new RevokeAccessResponse(
                SpeContainerMembershipRevoked: false,
                SpeContainerOutcome: SpeContainerRevokeOutcome.Failed,
                DeactivatedCount: 1,
                SpeOrgMemberCleanup: new SpeOrgMemberCleanupSummary(null, 0, 0, 0)),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        json.Should().Contain("\"membersEnumerated\":null",
            "an omitted field reads as 'undefined' to a client, which is not the same claim as " +
            "'we do not know who the members are'");
    }

    /// <summary>
    /// M2's counterpart to <see cref="SpeOrgMemberCleanupSummary_UnknownMembership_SerializesAnExplicitNull"/>
    /// above. As of M2 (task 024 → task 065) a <see cref="SpeContainerRevokeOutcome.Failed"/> outcome can
    /// NEVER reach a 200 <see cref="RevokeAccessResponse"/> any more — the endpoint's Failed branch always
    /// returns <c>RevokeIncomplete</c>'s 500 Problem instead — so the load-bearing null this task's ADR-003
    /// constraint cares about now travels through <c>ProblemDetails.Extensions</c>, not the DTO. Runs the
    /// REAL handler (the "members cannot be enumerated" scenario) rather than hand-building the extensions
    /// dictionary, so a shape change to <see cref="RevokeExternalAccessEndpoint"/>'s Problem construction
    /// is caught here rather than only in a test that duplicates it.
    /// </summary>
    [Fact]
    public async Task RevokeIncomplete_SerializesTheOrgMemberCleanupNullAsAnExplicitNull()
    {
        var org = new OrgRevokeFixture(
            junctionThrows: true, membersWithoutEmail: null, overflowRows: 0,
            MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed);

        var result = await org.Revoke(stub.Build());

        var problem = ProblemBody(result);
        var json = System.Text.Json.JsonSerializer.Serialize(
            problem.ProblemDetails.Extensions,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        json.Should().Contain("\"membersEnumerated\":null",
            "the same load-bearing null the 200 path preserves must survive the 500 path too — an omitted " +
            "field reads as 'undefined' to a client, not 'we do not know who the members are'");
    }

    /// <summary>#2103: members with no email key were never looked up, so "all of them" is NotAttempted.</summary>
    [Fact]
    public void AggregateOrgOutcome_WhenEveryMemberHasNoEmailKey_IsNotAttempted() =>
        RevokeExternalAccessEndpoint.AggregateOrgOutcome(
                new SpeOrgMemberCleanupSummary(3, 0, 0, 0, RetainedByOtherGrant: 1, NoMembershipKey: 2))
            .Should().Be(SpeContainerRevokeOutcome.NotAttempted,
                "nobody's permission was looked up — 'nobody held one' would be a claim this revoke never checked");

    /// <summary>#2103: one member looked up and found absent, the rest with no key — a read happened, and found none.</summary>
    [Fact]
    public void AggregateOrgOutcome_WhenSomeMembersHaveNoEmailKey_AndTheRestHeldNone_IsAbsent() =>
        RevokeExternalAccessEndpoint.AggregateOrgOutcome(
                new SpeOrgMemberCleanupSummary(3, 0, 1, 0, NoMembershipKey: 2))
            .Should().Be(SpeContainerRevokeOutcome.NoPermissionFound);

    [Fact]
    public void AggregateOrgOutcome_WhenNobodyHeldAPermission_IsAbsentNotFailure() =>
        RevokeExternalAccessEndpoint.AggregateOrgOutcome(new SpeOrgMemberCleanupSummary(3, 0, 3, 0))
            .Should().Be(SpeContainerRevokeOutcome.NoPermissionFound,
                "the expected result under broker-only — no member ACLs are created in the first place");

    // ─────────────────────────────────────────────────────────────────────────────
    // The SPE SERVICE itself must report failure — the task-016 constraint.
    //
    // These are deliberately at the service level, not through an endpoint. The closure tests substitute
    // RemoveMembershipsAsync at its seam (RemoveAllExternalMembersAsync until task 166 deleted it), so they
    // never exercise the read error path — a perturbation that re-swallowed read failures passed every
    // endpoint test. That gap is the whole
    // finding task 016 filed onto this task, so it needs its own assertion.
    // ─────────────────────────────────────────────────────────────────────────────

    private static SpeContainerMembershipService ServiceWithFailingGraph(Exception failure)
    {
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Throws(failure);
        return new SpeContainerMembershipService(
            TestSpeOwnership.AllowAll(factory.Object), NullLogger<SpeContainerMembershipService>.Instance);
    }

    /// <summary>
    /// ✅ RE-BASED BY uac-r2 TASK 166 f1 (verifier item 14) — was <c>ListExternalMembersAsync_WhenGraphFails_…</c>, whose
    /// method had no production caller and was deleted. The task-016/017 rule it pinned ("could not ask" is never "has
    /// none") is asserted on the live single-grant revoke: a failing Graph is a failed result, never the benign
    /// <see cref="SpeContainerMembershipService.NoPermissionFoundError"/> absence.
    /// </summary>
    [Fact]
    public async Task RevokeMembershipAsync_WhenGraphFails_IsAFailure_NeverTheBenignAbsence()
    {
        var service = ServiceWithFailingGraph(new InvalidOperationException("Graph unreachable"));

        var result = await service.RevokeMembershipAsync(
            ContainerId.ToString(), new SpeMembershipKey(null, "counsel@client-firm.com"), TestInternalUserDirectory.Empty().Client);

        result.Success.Should().BeFalse();
        result.Error.Should().NotStartWith(SpeContainerMembershipService.NoPermissionFoundError,
            "'we could not ask' must never read as 'the container has none'");
    }

    // RemoveAllExternalMembersAsync_WhenTheListingFails_Propagates and the two SpeBulkRemovalResult tests were
    // DELETED by uac-r2 task 166 (amendment e) with the method and type they tested: that whole-container sweep
    // removed internal users' permissions too. Close-project now removes exactly its revoked grantees through
    // RemoveMembershipsAsync, whose read-failure honesty (every email failed, none "absent") is pinned by
    // SpeContainerPagingTests.RemoveMemberships_WhenTheReadFails_EveryMemberIsFailedAndNoneIsAbsent, and whose
    // closure-level consequence (container_not_cleared) by ProjectClosureCascadeTests.

    // ─────────────────────────────────────────────────────────────────────────────
    // The task-010 invariant — the Dataverse sweep must survive this task.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Task 017's binding constraint from task 010: the SPE work must not disturb the Dataverse sweep that
    /// fixed A-11. An SPE failure in particular must not suppress the deactivation count — the Dataverse
    /// rows were still revoked, and that is the part that actually governs access.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenSpeFails_StillReportsTheDataverseRowsDeactivatedInTheProblemExtensions()
    {
        // ✅ INVERTED BY M2 (task 024 → task 065). Was
        // Revoke_WhenSpeFails_StillReportsTheDataverseRowsDeactivated, reading DeactivatedCount off a 200
        // Ok<RevokeAccessResponse>. The Dataverse sweep is still authoritative and still ran — an SPE
        // cleanup failure must not hide it — but the count now rides in the 500 Problem's extensions
        // (owner directive 2026-09-10: "if revoked, a user message should be provided not just a 500
        // error", and DeactivatedCount is the fact that message is built on).
        var stub = new SpeServiceStub();
        var spe = stub.Build(GraphError);

        var result = await Revoke(DataverseFor(ContactId, ContactEmail), spe);

        var problem = ProblemBody(result);
        problem.ProblemDetails.Extensions["deactivatedCount"].Should().Be(1,
            "the Dataverse sweep is the authoritative revocation; an SPE cleanup failure must not hide it, " +
            "even behind a 500");
        problem.ProblemDetails.Extensions["speContainerOutcome"].Should().Be(SpeContainerRevokeOutcome.Failed);
    }

    /// <summary>
    /// And the reverse: a successful SPE removal must not be reported when the Dataverse sweep failed.
    /// Revoke returns a Problem in that case, so no success body exists at all.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenTheDataverseSweepFails_ReturnsProblemAndNoSuccessBody()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);
        var dataverse = DataverseFor(ContactId, ContactEmail);

        dataverse.Setup(c => c.QueryAsync<ExternalGrantRow>(
                GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        var result = await Revoke(dataverse, spe);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        stub.CallCount.Should().Be(0, "the SPE step must not run when the authoritative revocation failed");
    }
}
