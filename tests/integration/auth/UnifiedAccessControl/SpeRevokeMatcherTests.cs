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
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

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
/// <para>Seams are the <c>virtual</c> members of <see cref="DataverseWebApiClient"/> and
/// <see cref="SpeContainerMembershipService"/> (ADR-038 §4). No <c>Mock&lt;HttpMessageHandler&gt;</c>
/// (ban B1), no reflection into privates (ban B8).</para>
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
        Guid? contactOnGrantRow, string? contactEmail, bool emailLookupThrows = false)
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
            emailSetup.ReturnsAsync(new RevokeExternalAccessEndpoint.ContactEmailRow { emailaddress1 = contactEmail });

        return mock;
    }

    /// <summary>
    /// The membership service, substituted at its <c>virtual</c> seam. <c>CapturedEmail</c> is the point:
    /// A-13 was entirely about WHICH key the revoke matched on, so the tests assert the key that was
    /// actually passed, not merely that some call happened.
    /// </summary>
    private sealed class SpeServiceStub
    {
        public string? CapturedEmail { get; private set; }
        public string? CapturedContainerId { get; private set; }
        public int CallCount { get; private set; }

        public Mock<SpeContainerMembershipService> Build(SpeContainerMembershipResult result)
        {
            var mock = new Mock<SpeContainerMembershipService>(
                Mock.Of<IGraphClientFactory>(), NullLogger<SpeContainerMembershipService>.Instance);

            mock.Setup(s => s.RevokeMembershipAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string containerId, string email, CancellationToken _) =>
                {
                    CallCount++;
                    CapturedContainerId = containerId;
                    CapturedEmail = email;
                    return result;
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

    private static Task<IResult> Revoke(
        Mock<DataverseWebApiClient> dataverse,
        Mock<SpeContainerMembershipService> spe,
        Guid? contactId = null,
        Guid? containerId = null) =>
        RevokeExternalAccessEndpoint.RevokeAccessAsync(
            new RevokeAccessRequest(
                AccessRecordId, contactId ?? ContactId, ProjectId, containerId ?? ContainerId),
            dataverse.Object, spe.Object, Mock.Of<ITenantCache>(),
            AuthenticatedContext(), NullLogger<Program>.Instance, CancellationToken.None);

    private static RevokeAccessResponse Body(IResult result) =>
        result.Should().BeOfType<Ok<RevokeAccessResponse>>().Subject.Value!;

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
    public async Task Revoke_WhenGraphFails_ReportsFailedRatherThanAbsent()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(GraphError);

        var result = await Revoke(DataverseFor(ContactId, ContactEmail), spe);

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.Failed,
            "'we could not tell' must never be reported as 'there was nothing there'");
        body.SpeContainerMembershipRevoked.Should().BeFalse();
    }

    /// <summary>
    /// Without the email there is no way to identify the contact's ACL entry, so any permission that DOES
    /// exist is unfindable. That is an unknown state, not an absence.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenTheContactHasNoEmail_ReportsFailedRatherThanAbsent()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(NotFound);

        var result = await Revoke(DataverseFor(ContactId, contactEmail: null), spe);

        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.Failed);
        stub.CallCount.Should().Be(0,
            "with no key to match on there is nothing to ask Graph — and asking with an empty key could " +
            "match the wrong permission");
    }

    /// <summary>
    /// A failure reading the contact is the same class: the key could not be obtained, so the permission
    /// state is unknown.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenTheEmailLookupFails_ReportsFailedAndCallsNoSpeRevoke()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        var result = await Revoke(
            DataverseFor(ContactId, ContactEmail, emailLookupThrows: true), spe);

        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.Failed);
        stub.CallCount.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NotAttempted — honest about the cases where there is nothing to match.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// No container was named, so no permission was in scope. Distinct from "we looked and found none".
    /// </summary>
    [Fact]
    public async Task Revoke_WithNoContainerId_ReportsNotAttempted()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        var result = await RevokeExternalAccessEndpoint.RevokeAccessAsync(
            new RevokeAccessRequest(AccessRecordId, ContactId, ProjectId, ContainerId: null),
            DataverseFor(ContactId, ContactEmail).Object, spe.Object, Mock.Of<ITenantCache>(),
            AuthenticatedContext(), NullLogger<Program>.Instance, CancellationToken.None);

        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.NotAttempted);
        stub.CallCount.Should().Be(0);
    }

    /// <summary>
    /// An ORGANIZATION-grant revoke passes an empty ContactId (task 073 #7) — there is no single grantee,
    /// so no single email, so no permission to match. Reporting <c>NotAttempted</c> is the honest answer;
    /// claiming success here would recreate A-13 in a new place.
    ///
    /// <para>⚠️ This is also a KNOWN GAP, deliberately left and filed: the Dataverse sweep revokes the org
    /// grant for every member, but their container ACLs (if any) are not touched, because that needs an
    /// organization → members → emails expansion this path does not have. See
    /// <c>notes/task-017-spe-revoke-matcher.md</c>. Under broker-only no such ACLs are created, which is
    /// what bounds it.</para>
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_ReportsNotAttemptedRatherThanSuccess()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(Removed);

        var result = await Revoke(
            DataverseFor(contactOnGrantRow: null, contactEmail: null), spe, contactId: Guid.Empty);

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.NotAttempted);
        body.SpeContainerMembershipRevoked.Should().BeFalse();
        stub.CallCount.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The SPE SERVICE itself must report failure — the task-016 constraint.
    //
    // These are deliberately at the service level, not through an endpoint. The closure tests substitute
    // RemoveAllExternalMembersAsync at its seam, so they never exercise the listing error path — a
    // perturbation that re-swallowed listing failures passed every endpoint test. That gap is the whole
    // finding task 016 filed onto this task, so it needs its own assertion.
    // ─────────────────────────────────────────────────────────────────────────────

    private static SpeContainerMembershipService ServiceWithFailingGraph(Exception failure)
    {
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Throws(failure);
        return new SpeContainerMembershipService(
            factory.Object, NullLogger<SpeContainerMembershipService>.Instance);
    }

    /// <summary>
    /// ✅ FLIPPED BY TASK 017 (filed by task 016). <c>ListExternalMembersAsync</c> used to catch
    /// <c>ServiceException</c> AND <c>Exception</c> and return <c>[]</c> in both — so "Graph is
    /// unreachable" and "this container has no external members" were the same answer.
    ///
    /// <para>That is why close-project could report <c>200 OK</c> with
    /// <c>SpeContainerMembersRemoved: 0</c> while every external user still held file permission: the one
    /// signal that would have revealed it was being discarded one layer down. An empty list must now mean
    /// exactly one thing.</para>
    /// </summary>
    [Fact]
    public async Task ListExternalMembersAsync_WhenGraphFails_ThrowsRatherThanReturningEmpty()
    {
        var service = ServiceWithFailingGraph(new InvalidOperationException("Graph unreachable"));

        var act = () => service.ListExternalMembersAsync(ContainerId.ToString());

        await act.Should().ThrowAsync<InvalidOperationException>(
            "an empty member list must mean 'the container has none', never 'we could not ask'");
    }

    /// <summary>
    /// And the failure must reach the caller through the bulk-removal method, which is what
    /// close-project actually calls — otherwise the propagation above would be academic.
    /// </summary>
    [Fact]
    public async Task RemoveAllExternalMembersAsync_WhenTheListingFails_Propagates()
    {
        var service = ServiceWithFailingGraph(new InvalidOperationException("Graph unreachable"));

        var act = () => service.RemoveAllExternalMembersAsync(ContainerId.ToString());

        await act.Should().ThrowAsync<InvalidOperationException>(
            "nothing was removed, so answering with a count would be a false success " +
            "(this is what makes ProjectClosureEndpoint's container_not_cleared reachable)");
    }

    /// <summary>
    /// A container with genuinely no external members is still the quiet, successful case — the fix must
    /// not turn "nothing to do" into an error.
    /// </summary>
    [Fact]
    public void SpeBulkRemovalResult_WithNoFailures_IsComplete()
    {
        new SpeBulkRemovalResult(0, 0).IsComplete.Should().BeTrue();
        new SpeBulkRemovalResult(7, 0).IsComplete.Should().BeTrue();
    }

    /// <summary>
    /// Any member left behind means the container is not cleared. <c>Removed</c> alone cannot express this
    /// — which is exactly why the old bare <c>int</c> return hid it.
    /// </summary>
    [Fact]
    public void SpeBulkRemovalResult_WithAnyFailure_IsNotComplete()
    {
        new SpeBulkRemovalResult(11, 1).IsComplete.Should().BeFalse(
            "one person retaining file access is enough to make the closure incomplete");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The task-010 invariant — the Dataverse sweep must survive this task.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Task 017's binding constraint from task 010: the SPE work must not disturb the Dataverse sweep that
    /// fixed A-11. An SPE failure in particular must not suppress the deactivation count — the Dataverse
    /// rows were still revoked, and that is the part that actually governs access.
    /// </summary>
    [Fact]
    public async Task Revoke_WhenSpeFails_StillReportsTheDataverseRowsDeactivated()
    {
        var stub = new SpeServiceStub();
        var spe = stub.Build(GraphError);

        var result = await Revoke(DataverseFor(ContactId, ContactEmail), spe);

        var body = Body(result);
        body.DeactivatedCount.Should().Be(1,
            "the Dataverse sweep is the authoritative revocation; an SPE cleanup failure must not hide it");
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.Failed);
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
