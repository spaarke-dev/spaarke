// Task 138 (#1061) — the WRITE-time access-permission policy on the grant routes, as a wire contract.
// KEEP path: endpoint-contract.
//
// Tasks 139, 140 and 142 and the Manage Access dialog depend on these reason codes, status codes and the
// human-readable detail, so they are asserted as LITERALS. The decision itself (one function, the order of its
// checks, absent = unreadable) is pinned in PolymorphicGrantWriteTests; the grant core's typed refusal in
// GrantLifecycleCharacterizationTests; authorization ordering (a caller without Write never learns the policy) in
// GrantPolicyOrderingTests. This fixture's caller always holds Write (EntitledCallerRecordAccessProbe).
//
// The owner's model (round 2, 2026-09-30): Restricted = no contact-based access at all, internal users unaffected;
// Secure and Limited = contacts only through named, direct grants; Standard = every grant type.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

public sealed class GrantPolicyContractTests : IClassFixture<ExternalAccessContractFixture>
{
    private const string GrantPath = "/api/v1/external-access/grant";
    private const string InvitePath = "/api/v1/external-access/invite";
    private const string InviteAndGrantPath = "/api/v1/external-access/invite-and-grant";
    private const string ShareUserPath = "/api/v1/external-access/share-user";

    private const string RecordRestricted = "sdap.access.grant.record_restricted";
    private const string OrgGrantDirectOnly = "sdap.access.grant.org_grant_direct_only_record";
    private const string PolicyUnreadable = "sdap.access.grant.policy_unreadable";

    private const string GrantTable = "sprk_externalrecordaccesses";

    private static readonly Guid RootId = Guid.Parse("13813813-8138-1381-3813-813813813813");
    private static readonly Guid GranteeContactId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid OrganizationId = Guid.Parse("67577f8c-4301-f111-8407-7ced8d1dc988");
    private static readonly Guid InternalUserId = Guid.Parse("abababab-abab-abab-abab-abababababab");

    private readonly ExternalAccessContractFixture _fixture;

    public GrantPolicyContractTests(ExternalAccessContractFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>An admin (Write-holding) client whose target record carries the given policy flags.</summary>
    private HttpClient ClientFor(bool restricted = false, bool limited = false, bool secure = false, string? rootFlags = null)
    {
        var client = _fixture.CreateAdminClient();
        if (restricted) client.DefaultRequestHeaders.Add("X-Test-RestrictedProjects", RootId.ToString());
        if (limited) client.DefaultRequestHeaders.Add("X-Test-LimitedProjects", RootId.ToString());
        if (secure) client.DefaultRequestHeaders.Add("X-Test-SecureProjects", RootId.ToString());
        if (rootFlags is not null) client.DefaultRequestHeaders.Add("X-Test-RootFlags", rootFlags);
        return client;
    }

    private static object ContactGrant(string recordType = "project") => new
    {
        contactId = GranteeContactId,
        recordType,
        recordId = RootId,
        accessLevel = (int)ExternalAccessLevel.Collaborate
    };

    private static object OrganizationGrant(string recordType = "project") => new
    {
        contactId = Guid.Empty,
        organizationId = OrganizationId,
        recordType,
        recordId = RootId,
        accessLevel = (int)ExternalAccessLevel.ViewOnly
    };

    private static object Invite() => new
    {
        email = "policy@firm.example",
        recordType = "project",
        recordId = RootId,
        accessLevel = (int)ExternalAccessLevel.ViewOnly,
        firstName = "Pat",
        lastName = "Policy"
    };

    private static async Task<(string? ReasonCode, string? Detail, string? TraceId)> ProblemOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        return (
            root.TryGetProperty("reasonCode", out var code) ? code.GetString() : null,
            root.TryGetProperty("detail", out var detail) ? detail.GetString() : null,
            root.TryGetProperty("traceId", out var trace) ? trace.GetString() : null);
    }

    private void AssertNothingWritten()
    {
        _fixture.Dataverse.CreatedEntitySets.Should().BeEmpty("a refused request writes no row and creates no Contact");
        _fixture.Dataverse.ContactUpdates.Should().BeEmpty("a refused request binds no CIAM oid");
        _fixture.IdentityStore.Writes.Should().BeEmpty(
            "nor through the identity store (task 141: the oid bind and any collision flag are written there)");
        _fixture.Dataverse.QueriedEntitySets.Should().NotContain(GrantTable,
            "the policy runs before the upsert's own pre-existence query");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Restricted — criterion 6
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("project")]
    [InlineData("matter")]
    [InlineData("workassignment")]
    public async Task PostGrant_ForAContactOnARestrictedRecord_Returns422RecordRestricted_AndWritesNoRow(string recordType)
    {
        using var client = ClientFor(restricted: true);

        var response = await client.PostAsJsonAsync(GrantPath, ContactGrant(recordType));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "a refusal is a 422 with a message, never the grant route's catch-all 500 (criterion 8)");
        var (reasonCode, detail, traceId) = await ProblemOf(response);
        reasonCode.Should().Be(RecordRestricted);
        detail.Should().Contain("Restricted").And.Contain("Nothing was granted");
        traceId.Should().NotBeNullOrWhiteSpace();
        AssertNothingWritten();
    }

    [Fact]
    public async Task PostGrant_ForAnOrganizationOnARestrictedRecord_Returns422RecordRestricted_AndWritesNoRow()
    {
        using var client = ClientFor(restricted: true);

        var response = await client.PostAsJsonAsync(GrantPath, OrganizationGrant());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemOf(response)).ReasonCode.Should().Be(RecordRestricted,
            "Restricted is checked before the organization rule, so it names the stronger reason");
        AssertNothingWritten();
    }

    [Fact]
    public async Task InviteAndGrant_OnARestrictedRecord_Returns422BeforeAnyContactOrCiamWork()
    {
        using var client = ClientFor(restricted: true);

        var response = await client.PostAsJsonAsync(InviteAndGrantPath, Invite());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemOf(response)).ReasonCode.Should().Be(RecordRestricted);
        _fixture.IdentityStore.Reads.Should().BeEmpty(
            "the onboarding seam's first step — the Contact lookup, through the identity binder since task 141 — is " +
            "never reached, so no CIAM account is provisioned");
        AssertNothingWritten();
    }

    [Fact]
    public async Task Invite_OnARestrictedRecord_Returns422_AndCreatesNoContactOrAccount()
    {
        using var client = ClientFor(restricted: true);

        var response = await client.PostAsJsonAsync(InvitePath, Invite());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemOf(response)).ReasonCode.Should().Be(RecordRestricted);
        _fixture.IdentityStore.Reads.Should().BeEmpty("the Contact lookup (the identity binder, task 141) is never reached");
        AssertNothingWritten();
    }

    [Fact]
    public async Task PostGrant_ForAContactOnARestrictedAndSecureRecord_IsStillRecordRestricted()
    {
        // Restricted wins where both are present. Also the twin of the unreadable case below: a REAL
        // Secure + Restricted record is 422 record_restricted, a fault is 503 policy_unreadable.
        using var client = ClientFor(restricted: true, secure: true);

        var response = await client.PostAsJsonAsync(GrantPath, ContactGrant());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemOf(response)).ReasonCode.Should().Be(RecordRestricted);
        AssertNothingWritten();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Secure or Limited — criterion 7
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, false, "This record is Secure")]
    [InlineData(false, true, "This record's Access Permission is Limited")]
    [InlineData(true, true, "This record is Secure")]
    public async Task PostGrant_ForAnOrganizationOnADirectOnlyRecord_Returns422_AndWritesNoRow(
        bool secure, bool limited, string expectedOpening)
    {
        using var client = ClientFor(secure: secure, limited: limited);

        var response = await client.PostAsJsonAsync(GrantPath, OrganizationGrant());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var (reasonCode, detail, _) = await ProblemOf(response);
        reasonCode.Should().Be(OrgGrantDirectOnly);
        detail.Should().StartWith(expectedOpening).And.Contain("Nothing was granted");
        AssertNothingWritten();
    }

    /// <summary>The positive twin: a NAMED contact grant on the same Secure or Limited record is written.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PostGrant_ForANamedContactOnADirectOnlyRecord_Succeeds_AndWritesTheRow(bool secure, bool limited)
    {
        using var client = ClientFor(secure: secure, limited: limited);

        var response = await client.PostAsJsonAsync(GrantPath, ContactGrant());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Dataverse.CreatedEntitySets.Should().ContainSingle().Which.Should().Be(GrantTable);
    }

    /// <summary>Standard admits every grant type — the organization grant the two tests above refuse.</summary>
    [Fact]
    public async Task PostGrant_ForAnOrganizationOnAStandardRecord_Succeeds()
    {
        using var client = ClientFor();

        var response = await client.PostAsJsonAsync(GrantPath, OrganizationGrant());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Dataverse.CreatedEntitySets.Should().ContainSingle().Which.Should().Be(GrantTable);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Unreadable policy — criteria 5 and 9
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("unreadable")]
    [InlineData("absent")]
    public async Task PostGrant_WhenTheRecordsFlagsCannotBeRead_Returns503PolicyUnreadable_NotRecordRestricted(string rootFlags)
    {
        using var client = ClientFor(rootFlags: rootFlags);

        var response = await client.PostAsJsonAsync(GrantPath, ContactGrant());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var (reasonCode, detail, traceId) = await ProblemOf(response);
        reasonCode.Should().Be(PolicyUnreadable,
            "a fault is reported as itself — RootRecordFlags.Unreadable also carries IsRestricted, so checking that " +
            "first would tell the operator a falsehood; and an ABSENT key is unreadable, never 'no veto'");
        detail.Should().Be("The record's access settings could not be read; nothing was granted. Try again in a moment.");
        traceId.Should().NotBeNullOrWhiteSpace();
        AssertNothingWritten();
    }

    [Fact]
    public async Task InviteAndGrant_WhenTheRecordsFlagsCannotBeRead_Returns503_AndOnboardsNothing()
    {
        using var client = ClientFor(rootFlags: "unreadable");

        var response = await client.PostAsJsonAsync(InviteAndGrantPath, Invite());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ProblemOf(response)).ReasonCode.Should().Be(PolicyUnreadable);
        _fixture.IdentityStore.Reads.Should().BeEmpty("the Contact lookup (the identity binder, task 141) is never reached");
        AssertNothingWritten();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Internal access is unaffected — criterion 10 (positive half)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "+ User" is an internal POA share. Restricted removes CONTACT access only, and Secure/Limited govern contact
    /// grant types only, so a Write-holder still shares the record with a colleague on all three.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task PostShareUser_ByAWriteHolder_OnARestrictedLimitedOrSecureRecord_StillSucceeds(
        bool restricted, bool limited, bool secure)
    {
        _fixture.Dataverse.ContactQueryResult =
            $$"""[{"systemuserid":"{{InternalUserId}}","fullname":"Ada Lovelace","isdisabled":false,"accessmode":0,"applicationid":null,"sprk_isexternal":false}]""";
        // Task 143: on a SECURE record /share-user asks the No Access list about the user, whose link must be readable
        // (an unreadable link refuses). The user exists and is linked to no contact; the list names nobody.
        _fixture.IdentityStore.AddSystemUser(InternalUserId, oid: null, email: "ada@customer.example");
        using var client = ClientFor(restricted: restricted, limited: limited, secure: secure);

        var response = await client.PostAsJsonAsync(ShareUserPath, new
        {
            recordType = "project",
            recordId = RootId,
            systemUserId = InternalUserId,
            accessLevel = 100000001
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.RecordShares.Writes.Should().NotBeEmpty("the colleague's share was written");
    }
}
