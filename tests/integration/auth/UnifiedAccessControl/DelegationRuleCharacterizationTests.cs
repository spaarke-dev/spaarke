using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The delegation rule — finding A-6 (spec FR-07), closed by task 008: <b>a caller may change who can
/// access a record only if they hold Write on that record</b> (owner decision B-14), evaluated as the
/// caller.
///
/// <para><b>What was wrong.</b> The whole <c>/api/v1/external-access</c> group sat behind a bare
/// <c>RequireAuthorization()</c>. Minting a grant, revoking one, onboarding a CIAM identity,
/// cascade-closing a project and provisioning a business unit were all reachable by ANY authenticated
/// caller, and then executed app-only. design.md §6 names this the blocking prerequisite for the
/// Manage Access PCF: without it the "+ User" button is a one-click path from read-only to Full
/// Access on a confidential matter.</para>
///
/// <para><b>Why these tests can tell the difference.</b> The probe is substituted
/// (<see cref="DelegationRuleTestFixture"/>) so a caller can genuinely hold Write. Every negative
/// below has a positive twin that differs ONLY in the caller's rights — without that pairing a 403
/// assertion would pass against a filter that denied unconditionally, which is the vacuous-pass trap
/// this project keeps meeting. The positives assert the request reaches the handler's own validation
/// (400 for a body the handler rejects), which can only happen after the gate allowed it; the
/// handler's downstream Dataverse failure is irrelevant and deliberately not asserted.</para>
/// </summary>
public class DelegationRuleCharacterizationTests : IClassFixture<DelegationRuleTestFixture>
{
    private readonly DelegationRuleTestFixture _fixture;

    private static readonly Guid ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>Dataverse's own wire spelling for a caller who can read but not write.</summary>
    private const string ReadOnly = "ReadAccess";

    /// <summary>A caller who can write — and therefore may delegate (B-14).</summary>
    private const string ReadWrite = "ReadAccess,WriteAccess";

    /// <summary>
    /// Everything EXCEPT Write. Guards against a check written as "any rights at all" or as a
    /// non-empty-string test, both of which would admit a read-only caller.
    /// </summary>
    private const string EveryRightExceptWrite =
        "ReadAccess,DeleteAccess,CreateAccess,AppendAccess,AppendToAccess,ShareAccess";

    public DelegationRuleCharacterizationTests(DelegationRuleTestFixture fixture) => _fixture = fixture;

    // ─────────────────────────────────────────────────────────────────────────────
    // FR-07 acceptance — one negative per mutation route
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FR-07 acceptance criterion 1: a caller WITHOUT Write on the target record is refused by every
    /// mutation route, with the delegation deny code so the refusal is attributable to this rule and
    /// not to some unrelated failure further down.
    /// </summary>
    [Theory]
    [InlineData("grant")]
    [InlineData("invite")]
    [InlineData("invite-and-grant")]
    [InlineData("revoke")]
    [InlineData("close-project")]
    [InlineData("provision-project")]
    public async Task ExternalAccessMutation_ForCallerWithoutWriteOnTarget_DeniedForDelegationRule(string route)
    {
        // Arrange — a caller who can READ the record but not write it.
        using var client = _fixture.CreateClientWithRights(ReadOnly);
        var (path, body) = RequestFor(route, Guid.NewGuid());

        // Act
        var response = await client.PostAsJsonAsync(path, body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "FR-07/B-14: {0} changes who can reach a record, so it requires Write on that record", route);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyWriteRequired);
    }

    /// <summary>
    /// FR-07 acceptance criterion 2 — the other half, and the one that keeps the negatives honest: the
    /// SAME routes with the SAME bodies admit a caller who holds Write. The request then reaches the
    /// handler's own validation (400 on a body the handler rejects), which is only possible once
    /// authorization has allowed it.
    /// </summary>
    [Theory]
    [InlineData("grant")]
    [InlineData("invite-and-grant")]
    public async Task ExternalAccessMutation_ForCallerWithWriteOnTarget_ReachesHandlerValidation(string route)
    {
        // Arrange — Write on the target, but a body the handler itself rejects.
        using var client = _fixture.CreateClientWithRights(ReadWrite);
        var (path, body) = RequestWithHandlerInvalidBody(route, Guid.NewGuid());

        // Act
        var response = await client.PostAsJsonAsync(path, body);

        // Assert — 400 proves handler entry: the delegation gate passed and only the payload stopped it.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a caller WITH Write must not be over-denied — {0} should reach its own validation", route);
    }

    /// <summary>
    /// The no-over-denial half for the routes whose handlers have no reachable 400 offline: assert the
    /// gate itself allowed the request. A downstream 500 from the unavailable test-host Dataverse is
    /// expected and irrelevant; what matters is that the refusal is NOT this filter's.
    /// </summary>
    [Theory]
    [InlineData("invite")]
    [InlineData("revoke")]
    [InlineData("close-project")]
    [InlineData("provision-project")]
    public async Task ExternalAccessMutation_ForCallerWithWriteOnTarget_IsNotDeniedByTheDelegationRule(string route)
    {
        // Arrange
        using var client = _fixture.CreateClientWithRights(ReadWrite);
        var (path, body) = RequestFor(route, Guid.NewGuid());

        // Act
        var response = await client.PostAsJsonAsync(path, body);

        // Assert — no reason code at all is the common (and best) outcome: the gate allowed the
        // request and the handler failed downstream on the unavailable test-host Dataverse.
        (await ReasonCodeOf(response) ?? string.Empty).Should().NotStartWith("sdap.access.deny.delegation",
            "a caller WITH Write on the target must pass the delegation gate on {0}", route);
    }

    /// <summary>
    /// Read is not licence to grant. A caller holding every Dataverse right EXCEPT Write is still
    /// refused — this fails if the rule is ever weakened to "has some access" or "rights string is
    /// non-empty", which would readmit exactly the read-only caller A-6 is about.
    /// </summary>
    [Fact]
    public async Task PostGrant_ForCallerHoldingEveryRightExceptWrite_IsStillDenied()
    {
        using var client = _fixture.CreateClientWithRights(EveryRightExceptWrite);
        var (path, body) = RequestFor("grant", Guid.NewGuid());

        var response = await client.PostAsJsonAsync(path, body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyWriteRequired,
            "Write is the delegation right (B-14) — Share, Append and the rest do not substitute for it");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The check must be aimed at the RIGHT record
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A denial proves nothing if the filter asked about the wrong record. <c>/grant</c> must check the
    /// grant ROOT named by <c>recordType</c> + <c>recordId</c>, at that root's own entity set — not the
    /// legacy <c>projectId</c>, and not <c>sprk_documents</c> (which is what the pre-existing
    /// <c>IAccessDataSource</c> path hard-codes, and the reason this filter does not use it).
    /// </summary>
    [Fact]
    public async Task PostGrant_WithMatterRoot_ChecksTheCallersWriteOnThatMatter()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        var unrelatedLegacyProjectId = Guid.NewGuid();
        using var client = _fixture.CreateClientWithRights(ReadOnly);

        // Act
        await client.PostAsJsonAsync("/api/v1/external-access/grant", new
        {
            contactId = ContactId,
            projectId = unrelatedLegacyProjectId,
            recordType = "matter",
            recordId = matterId,
            accessLevel = (int)ExternalAccessLevel.ViewOnly
        });

        // Assert
        _fixture.ProbedTargets.Should().Contain(("sprk_matters", matterId),
            "an explicit recordType+recordId root takes precedence over the legacy projectId shorthand");
        _fixture.ProbedTargets.Should().NotContain(t => t.RecordId == unrelatedLegacyProjectId,
            "checking Write on the ignored legacy field would authorize against a record the grant " +
            "will not be written to");
    }

    /// <summary>
    /// <c>/revoke</c> names an ACCESS RECORD, not the record whose access changes. The rights check has
    /// to follow the row to its root — otherwise the rule is unenforceable on the one route that can
    /// silently strip someone's access.
    ///
    /// <para>This also pins that the resolution goes through the row rather than the request's
    /// <c>projectId</c> field: the body carries a different, unrelated project id, and checking that
    /// one would let a caller with Write on any project of their choosing revoke grants on a matter
    /// they cannot touch.</para>
    /// </summary>
    [Fact]
    public async Task PostRevoke_ChecksTheCallersWriteOnTheGrantRowsRoot_NotTheRequestBody()
    {
        // Arrange — the row hangs off a MATTER; the request body names an unrelated project.
        var accessRecordId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var unrelatedProjectId = Guid.NewGuid();
        _fixture.SeedGrantRow(accessRecordId, ContactId, matterId, ExternalGrantRootType.Matter);

        using var client = _fixture.CreateClientWithRights(ReadOnly);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/external-access/revoke", new
        {
            accessRecordId,
            contactId = ContactId,
            projectId = unrelatedProjectId
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.ProbedTargets.Should().Contain(("sprk_matters", matterId),
            "the record whose access is being revoked is the grant row's root");
        _fixture.ProbedTargets.Should().NotContain(t => t.RecordId == unrelatedProjectId,
            "the body's projectId is back-compat metadata, not the authorization target");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Fail-closed exits (ADR-003)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FR-07 acceptance criterion 3. An unresolvable target denies rather than proceeding — and denies
    /// with 403, not 404, so an unauthorized caller cannot use the response to learn which
    /// access-record ids exist.
    /// </summary>
    [Fact]
    public async Task PostRevoke_ForAnAccessRecordThatDoesNotExist_IsDeniedNotDisclosed()
    {
        using var client = _fixture.CreateClientWithRights(ReadWrite);

        var response = await client.PostAsJsonAsync("/api/v1/external-access/revoke", new
        {
            accessRecordId = Guid.NewGuid(),   // never seeded
            contactId = ContactId
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "404 before authorization would turn the endpoint into an access-record enumeration oracle");
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyTargetUnresolved);
    }

    /// <summary>
    /// FR-07 acceptance criterion 3, the grant side: a body naming no usable root is refused by
    /// authorization, before the handler's own validation — so no write is attempted app-only.
    /// </summary>
    [Fact]
    public async Task PostGrant_WithNoResolvableGrantRoot_IsDeniedByAuthorizationNotValidation()
    {
        using var client = _fixture.CreateClientWithRights(ReadWrite);

        var response = await client.PostAsJsonAsync("/api/v1/external-access/grant", new
        {
            contactId = ContactId,
            accessLevel = (int)ExternalAccessLevel.ViewOnly
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyTargetUnresolved,
            "with no target there is nothing to hold Write on; ADR-003 says deny, not proceed");
    }

    /// <summary>
    /// FR-07 acceptance criterion 4. An error inside the access check denies — it never falls through
    /// to the handler, which would execute the mutation app-only.
    /// </summary>
    [Fact]
    public async Task PostGrant_WhenTheAccessCheckThrows_IsDeniedRatherThanFallingThrough()
    {
        using var client = _fixture.CreateClientWithRights("THROW");
        var (path, body) = RequestFor("grant", Guid.NewGuid());

        var response = await client.PostAsJsonAsync(path, body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyCheckFailed);
    }

    /// <summary>
    /// A caller who authenticates but presents no BEARER credential cannot be evaluated as themselves.
    /// The check denies rather than degrading to an app-only evaluation — which would answer "can the
    /// application write", the shape of finding A-2.
    /// </summary>
    [Fact]
    public async Task PostGrant_WhenAuthenticatedWithoutABearerToken_IsDeniedRatherThanEvaluatedAppOnly()
    {
        using var client = _fixture.CreateClientWithoutBearerToken();
        var (path, body) = RequestFor("grant", Guid.NewGuid());

        var response = await client.PostAsJsonAsync(path, body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCodeOf(response)).Should().Be(DelegationRuleFilter.DenyNoCallerToken);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A well-formed request per route, targeting <paramref name="projectId"/>.</summary>
    private (string Path, object Body) RequestFor(string route, Guid projectId) => route switch
    {
        "grant" => ("/api/v1/external-access/grant", new
        {
            contactId = ContactId,
            projectId,
            accessLevel = (int)ExternalAccessLevel.ViewOnly
        }),
        "invite" => ("/api/v1/external-access/invite", new
        {
            email = "counsel@example.com",
            projectId,
            accessLevel = (int)ExternalAccessLevel.ViewOnly
        }),
        "invite-and-grant" => ("/api/v1/external-access/invite-and-grant", new
        {
            email = "counsel@example.com",
            projectId,
            accessLevel = (int)ExternalAccessLevel.ViewOnly
        }),
        "revoke" => ("/api/v1/external-access/revoke", RevokeBodyFor(projectId)),
        "close-project" => ("/api/v1/external-access/close-project", new { projectId }),
        "provision-project" => ("/api/v1/external-access/provision-project", new
        {
            projectId,
            projectRef = "P-TEST-0001"
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unmapped route in this test's helper.")
    };

    /// <summary>
    /// Revoke needs a seeded row for its target to resolve at all — otherwise every revoke case would
    /// deny as "target unresolved" and never reach the rights comparison the test is about.
    /// </summary>
    private object RevokeBodyFor(Guid projectId)
    {
        var accessRecordId = Guid.NewGuid();
        _fixture.SeedGrantRow(accessRecordId, ContactId, projectId, ExternalGrantRootType.Project);
        return new { accessRecordId, contactId = ContactId, projectId };
    }

    /// <summary>
    /// A request the DELEGATION gate accepts (resolvable target) but the HANDLER rejects — so a 400
    /// unambiguously means "authorization allowed, validation refused".
    /// </summary>
    private static (string Path, object Body) RequestWithHandlerInvalidBody(string route, Guid projectId) => route switch
    {
        // Root resolves, but the access level is not a defined ExternalAccessLevel.
        "grant" => ("/api/v1/external-access/grant", new
        {
            contactId = ContactId,
            projectId,
            accessLevel = 7777
        }),
        // Root resolves, but the handler requires a non-empty Email.
        "invite-and-grant" => ("/api/v1/external-access/invite-and-grant", new
        {
            email = "",
            projectId,
            accessLevel = (int)ExternalAccessLevel.ViewOnly
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unmapped route in this test's helper.")
    };

    /// <summary>The ProblemDetails <c>reasonCode</c>, or <c>null</c> when the response carries none.</summary>
    private static async Task<string?> ReasonCodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
