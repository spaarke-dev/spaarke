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
                new RevokeAccessRequest(AccessRecordId, Guid.Empty, ProjectId, ContainerId),
                Dataverse.Object, spe.Object, Mock.Of<ITenantCache>(),
                AuthenticatedContext(), NullLogger<Program>.Instance, CancellationToken.None);
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
                Mock.Of<IGraphClientFactory>(), NullLogger<SpeContainerMembershipService>.Instance);

            mock.Setup(s => s.RevokeMembershipAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string containerId, string email, CancellationToken _) =>
                {
                    if (containerId != ContainerId.ToString())
                        throw new InvalidOperationException($"Unmodelled container: '{containerId}'.");

                    CapturedEmails.Add(email);
                    return _byEmail.TryGetValue(email, out var specific) ? specific : _default;
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
    public async Task Revoke_OfAnOrganizationGrant_WhenOneMemberFails_ReportsFailedAndStillCleansTheOthers()
    {
        var org = new OrgRevokeFixture(MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed, new Dictionary<string, SpeContainerMembershipResult>
        {
            [EmailFor(MemberTwo)] = GraphError
        });

        var result = await org.Revoke(stub.Build());

        stub.CallCount.Should().Be(3,
            "a per-member failure must not abort the loop — the other members must still lose access");

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.Failed,
            "one member retaining file access is enough to make the org cleanup incomplete");
        body.SpeContainerMembershipRevoked.Should().BeFalse(
            "'some members retain access' must never be reportable as an SPE success");
        body.SpeOrgMemberCleanup.Should().Be(
            new SpeOrgMemberCleanupSummary(MembersEnumerated: 3, PermissionsRemoved: 2, PermissionsNotFound: 0, Failed: 1));
    }

    /// <summary>
    /// A member with no <c>emailaddress1</c> has no findable ACL entry. That is an unknown state, not an
    /// absence — the same judgement task 017 made per-contact, applied per member.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_WhenAMemberHasNoEmail_ReportsFailedRatherThanAbsent()
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
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.Failed);
        body.SpeOrgMemberCleanup!.Failed.Should().Be(1);
    }

    /// <summary>
    /// FR-16b acceptance criterion 3: when the member list cannot be established, the response reports a
    /// non-success outcome and attempts NO removals. Counts from a partial sweep off an unknown member
    /// list would read like a complete answer.
    /// </summary>
    [Fact]
    public async Task Revoke_OfAnOrganizationGrant_WhenMembersCannotBeEnumerated_ReportsFailedAndRemovesNothing()
    {
        var org = new OrgRevokeFixture(
            junctionThrows: true, membersWithoutEmail: null, overflowRows: 0,
            MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed);

        var result = await org.Revoke(stub.Build());

        stub.CallCount.Should().Be(0);

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.Failed,
            "'we could not tell who the members are' must never be reported as 'there was nothing there'");
        body.SpeOrgMemberCleanup!.MembersEnumerated.Should().BeNull(
            "a null member count is what distinguishes 'we never looked' from 'we looked and it was empty'");
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
    public async Task Revoke_OfAnOrganizationGrant_TooLargeToSweep_ReportsFailedRatherThanTruncating()
    {
        var org = new OrgRevokeFixture(
            junctionThrows: false, membersWithoutEmail: null,
            overflowRows: ExternalOrganizationMembership.MaxMembersPerSweep,
            MemberOne, MemberTwo, MemberThree);
        var stub = new SpeMemberStub(Removed);

        var result = await org.Revoke(stub.Build());

        stub.CallCount.Should().Be(0, "a partial sweep would produce counts that read like a complete answer");

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.Failed);
        body.SpeOrgMemberCleanup!.MembersEnumerated.Should().BeNull(
            "the member list is a truncation, not the membership — reporting its length would assert " +
            "something we do not know");
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

    [Fact]
    public void AggregateOrgOutcome_WhenNobodyHeldAPermission_IsAbsentNotFailure() =>
        RevokeExternalAccessEndpoint.AggregateOrgOutcome(new SpeOrgMemberCleanupSummary(3, 0, 3, 0))
            .Should().Be(SpeContainerRevokeOutcome.NoPermissionFound,
                "the expected result under broker-only — no member ACLs are created in the first place");

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
