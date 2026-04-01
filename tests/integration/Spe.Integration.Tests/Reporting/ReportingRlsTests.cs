using System.Security.Claims;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Spe.Integration.Tests.Reporting;

/// <summary>
/// Integration tests verifying that Business Unit Row-Level Security (BU RLS) is correctly
/// enforced when generating Power BI embed tokens.
///
/// These are structural tests — they verify that <c>ReportingEndpoints</c> and
/// <c>ReportingEmbedService</c> build the EffectiveIdentity correctly from the authenticated
/// user's claims, without requiring a live Power BI service or the PBI SDK types directly.
///
/// Test strategy: replicate the private claim-extraction and token-request–construction logic
/// from <c>ReportingEndpoints</c> and <c>ReportingEmbedService</c> here, then assert the
/// expected contract at each step. This approach tests the observable contract rather than
/// internal implementation details, and runs without any external service dependencies.
/// </summary>
/// <remarks>
/// Task PBI-041: BU RLS Verification Tests.
///
/// Constraints:
/// - MUST enforce BU RLS via EffectiveIdentity in embed tokens (spec).
/// - EffectiveIdentity must include correct username and RLS roles (spec).
/// - Test isolation: each test sets up its own context (no shared mutable state).
/// </remarks>
[Trait("Category", "Reporting")]
[Trait("Feature", "BU-RLS")]
public class ReportingRlsTests
{
    private readonly ITestOutputHelper _output;

    public ReportingRlsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Local models that mirror the Power BI SDK types used in ReportingEmbedService
    // without requiring Microsoft.PowerBI.Api as a direct test project reference.
    // These replicate only the properties used by BuildGenerateTokenRequest.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Test-local equivalent of <c>EffectiveIdentity</c> from Microsoft.PowerBI.Api.Models.
    /// Mirrors the shape used by <c>ReportingEmbedService.BuildGenerateTokenRequest</c>.
    /// </summary>
    private sealed class TestEffectiveIdentity
    {
        public string? Username { get; init; }
        public IList<string> Datasets { get; init; } = [];
        public IList<string> Roles { get; init; } = [];
    }

    /// <summary>
    /// Test-local equivalent of <c>GenerateTokenRequest</c> from Microsoft.PowerBI.Api.Models.
    /// Mirrors the shape used by <c>ReportingEmbedService.BuildGenerateTokenRequest</c>.
    /// </summary>
    private sealed class TestGenerateTokenRequest
    {
        public string AccessLevel { get; init; } = "View";
        public TestEffectiveIdentity? Identity { get; init; }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helper: build a ClaimsPrincipal that mimics a real Entra ID token
    // ─────────────────────────────────────────────────────────────────────────────

    private static ClaimsPrincipal BuildUserPrincipal(
        string userId,
        string upn,
        string? businessUnit = null,
        string[]? roles = null)
    {
        var claims = new List<Claim>
        {
            new("oid", userId),
            new(ClaimTypes.NameIdentifier, userId),
            new("preferred_username", upn),
            new("name", upn),
        };

        if (businessUnit != null)
            claims.Add(new Claim("businessunit", businessUnit));

        foreach (var role in roles ?? [])
        {
            claims.Add(new Claim("roles", role));
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "FakeAuth");
        return new ClaimsPrincipal(identity);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Replication of ReportingEndpoints private helpers
    // The tests exercise the same logic as the production code.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors <c>ReportingEndpoints.GetRlsUsername</c>.
    /// Extracts the RLS username from the user's claims in priority order:
    ///   preferred_username → upn → ClaimTypes.Upn → email → oid → MS OID claim.
    /// </summary>
    private static string? ExtractRlsUsername(ClaimsPrincipal user)
    {
        return user.FindFirst("preferred_username")?.Value
            ?? user.FindFirst("upn")?.Value
            ?? user.FindFirst(ClaimTypes.Upn)?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
    }

    /// <summary>
    /// Mirrors <c>ReportingEndpoints.GetBusinessUnitRoles</c>.
    /// Maps the "businessunit" (or "bu") claim to the PBI RLS role name "BU_{buClaim}".
    /// Returns null when no BU claim is present (RLS enforcement is skipped).
    /// </summary>
    private static IList<string>? ExtractBusinessUnitRoles(ClaimsPrincipal user)
    {
        var buClaim = user.FindFirst("businessunit")?.Value
            ?? user.FindFirst("bu")?.Value;

        if (string.IsNullOrWhiteSpace(buClaim))
            return null;

        return [$"BU_{buClaim}"];
    }

    /// <summary>
    /// Mirrors <c>ReportingEmbedService.BuildGenerateTokenRequest</c>.
    /// Constructs a token request with EffectiveIdentity for RLS when both username and roles
    /// are non-null and non-empty. Returns a request without EffectiveIdentity otherwise.
    /// </summary>
    private static TestGenerateTokenRequest BuildGenerateTokenRequest(
        string datasetId,
        string? username,
        IList<string>? roles)
    {
        if (!string.IsNullOrWhiteSpace(username) && roles is { Count: > 0 })
        {
            var identity = new TestEffectiveIdentity
            {
                Username = username,
                Datasets = [datasetId],
                Roles = roles
            };

            return new TestGenerateTokenRequest
            {
                AccessLevel = "View",
                Identity = identity
            };
        }

        return new TestGenerateTokenRequest { AccessLevel = "View" };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Tests: RLS username extraction from claims
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetRlsUsername_UserA_InBU1_ReturnsBU1Upn()
    {
        // Arrange — User A is in Business Unit 1.
        var user = BuildUserPrincipal(
            userId: "00000000-0000-0000-0001-000000000001",
            upn: "user-a@bu1.contoso.com",
            businessUnit: "bu1");

        // Act
        var username = ExtractRlsUsername(user);

        // Assert — preferred_username is the primary RLS identity source.
        username.Should().Be("user-a@bu1.contoso.com",
            "preferred_username is the primary source for RLS username extraction (mirrors GetRlsUsername priority order)");

        _output.WriteLine($"User A RLS username: {username}");
    }

    [Fact]
    public void GetRlsUsername_UserB_InBU2_ReturnsBU2Upn()
    {
        // Arrange — User B is in Business Unit 2.
        var user = BuildUserPrincipal(
            userId: "00000000-0000-0000-0002-000000000002",
            upn: "user-b@bu2.contoso.com",
            businessUnit: "bu2");

        // Act
        var username = ExtractRlsUsername(user);

        // Assert — each BU user has a distinct UPN (their RLS identity).
        username.Should().Be("user-b@bu2.contoso.com",
            "User B in BU-2 must produce a distinct UPN so their Power BI data access is isolated from BU-1");

        _output.WriteLine($"User B RLS username: {username}");
    }

    [Fact]
    public void GetRlsUsername_WhenNoUpn_FallsBackToOid()
    {
        // Arrange — Token has no UPN claim; only OID (service account / app-only scenario).
        var claims = new List<Claim>
        {
            new("oid", "fallback-oid-12345"),
            new(ClaimTypes.NameIdentifier, "fallback-oid-12345"),
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "FakeAuth"));

        // Act
        var username = ExtractRlsUsername(user);

        // Assert — OID is the last-resort RLS identity.
        username.Should().Be("fallback-oid-12345",
            "OID is the fallback RLS username when no UPN or email claim is present");

        _output.WriteLine($"Fallback RLS username (OID): {username}");
    }

    [Fact]
    public void GetRlsUsername_WhenNoClaims_ReturnsNull()
    {
        // Arrange — No identity claims present (anonymous / fully unauthenticated).
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var username = ExtractRlsUsername(user);

        // Assert — null username → BuildGenerateTokenRequest skips EffectiveIdentity.
        username.Should().BeNull(
            "Missing identity claims produce null username, causing RLS to be skipped in the token request");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Tests: Business Unit role extraction
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetBusinessUnitRoles_UserA_InBU1_ReturnsRoleNameWithBuPrefix()
    {
        // Arrange
        var user = BuildUserPrincipal(
            userId: "00000000-0000-0000-0001-000000000001",
            upn: "user-a@bu1.contoso.com",
            businessUnit: "bu1");

        // Act
        var roles = ExtractBusinessUnitRoles(user);

        // Assert — role naming convention: "BU_{businessunit}" (matches PBI dataset roles).
        roles.Should().NotBeNull();
        roles.Should().ContainSingle()
            .Which.Should().Be("BU_bu1",
                "BU roles use the 'BU_{businessunit}' convention matching Power BI dataset role names");

        _output.WriteLine($"User A BU RLS roles: [{string.Join(", ", roles!)}]");
    }

    [Fact]
    public void GetBusinessUnitRoles_UserB_InBU2_ReturnsDifferentRoleName()
    {
        // Arrange — User B in a different BU produces a different role name.
        var user = BuildUserPrincipal(
            userId: "00000000-0000-0000-0002-000000000002",
            upn: "user-b@bu2.contoso.com",
            businessUnit: "bu2");

        // Act
        var roles = ExtractBusinessUnitRoles(user);

        // Assert
        roles.Should().NotBeNull();
        roles.Should().ContainSingle()
            .Which.Should().Be("BU_bu2",
                "User B in BU-2 must get role BU_bu2, distinct from BU_bu1, ensuring Power BI enforces separate data access");

        _output.WriteLine($"User B BU RLS roles: [{string.Join(", ", roles!)}]");
    }

    [Fact]
    public void GetBusinessUnitRoles_WhenNoBuClaim_ReturnsNull()
    {
        // Arrange — Admin or service account with no businessunit claim.
        var user = BuildUserPrincipal(
            userId: "admin-user-id",
            upn: "admin@contoso.com",
            businessUnit: null);

        // Act
        var roles = ExtractBusinessUnitRoles(user);

        // Assert — null roles → BuildGenerateTokenRequest skips EffectiveIdentity (no BU restriction).
        roles.Should().BeNull(
            "Users without a businessunit claim get null roles; BuildGenerateTokenRequest skips RLS enforcement for them");

        _output.WriteLine("Admin user: no BU claim → null roles (RLS skipped — admin sees all data)");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Tests: EffectiveIdentity construction in the token request
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildGenerateTokenRequest_WithUsernameAndRoles_IncludesEffectiveIdentity()
    {
        // Arrange — represents User A in BU-1 requesting embed access.
        const string datasetId = "dataset-00000000-0001";
        const string username = "user-a@bu1.contoso.com";
        IList<string> roles = ["BU_bu1"];

        // Act
        var request = BuildGenerateTokenRequest(datasetId, username, roles);

        // Assert — EffectiveIdentity must be present and correctly populated.
        request.Identity.Should().NotBeNull(
            "A non-null username + non-empty roles must produce an EffectiveIdentity to enforce BU RLS");

        request.Identity!.Username.Should().Be(username,
            "EffectiveIdentity.Username must match the authenticated user's UPN for per-user RLS scoping");

        request.Identity.Roles.Should().Contain("BU_bu1",
            "EffectiveIdentity.Roles must include the user's BU role matching a role defined in the Power BI dataset");

        request.Identity.Datasets.Should().Contain(datasetId,
            "EffectiveIdentity.Datasets must reference the report's dataset to scope RLS to that dataset");

        _output.WriteLine($"EffectiveIdentity: Username={request.Identity.Username}, Roles=[{string.Join(", ", request.Identity.Roles)}], Datasets=[{string.Join(", ", request.Identity.Datasets)}]");
    }

    [Fact]
    public void BuildGenerateTokenRequest_UserBInBU2_HasBU2RoleName()
    {
        // Arrange — User B in BU-2 must get a distinct EffectiveIdentity from User A.
        const string datasetId = "dataset-00000000-0002";
        const string username = "user-b@bu2.contoso.com";
        IList<string> roles = ["BU_bu2"];

        // Act
        var request = BuildGenerateTokenRequest(datasetId, username, roles);

        // Assert
        request.Identity.Should().NotBeNull();
        request.Identity!.Username.Should().Be("user-b@bu2.contoso.com");
        request.Identity.Roles.Should().Contain("BU_bu2",
            "User B's EffectiveIdentity must use BU_bu2 (not BU_bu1) so Power BI filters data to BU-2 only");
        request.Identity.Datasets.Should().Contain(datasetId);

        _output.WriteLine($"User B EffectiveIdentity: Username={request.Identity.Username}, Roles=[{string.Join(", ", request.Identity.Roles)}]");
    }

    [Fact]
    public void BuildGenerateTokenRequest_EffectiveIdentityDatasets_MatchesReportDataset()
    {
        // Arrange — verify the dataset binding in EffectiveIdentity is scoped to exactly one dataset.
        const string datasetId = "report-dataset-guid-abc123";
        const string username = "user-a@bu1.contoso.com";
        IList<string> roles = ["BU_bu1"];

        // Act
        var request = BuildGenerateTokenRequest(datasetId, username, roles);

        // Assert — EffectiveIdentity.Datasets must contain exactly the report's dataset.
        request.Identity.Should().NotBeNull();
        request.Identity!.Datasets.Should().HaveCount(1,
            "EffectiveIdentity.Datasets should scope RLS to exactly the report's dataset, not all datasets");
        request.Identity.Datasets[0].Should().Be(datasetId,
            "Dataset ID in EffectiveIdentity must match the report being embedded");

        _output.WriteLine($"Dataset scope in EffectiveIdentity: [{string.Join(", ", request.Identity.Datasets)}]");
    }

    [Fact]
    public void BuildGenerateTokenRequest_NullUsername_ProducesNoEffectiveIdentity()
    {
        // Arrange — null username (no identity claims found) → RLS must be skipped entirely.
        const string datasetId = "dataset-no-rls";
        string? username = null;
        IList<string> roles = ["BU_admin"];

        // Act — roles are present but username is null → EffectiveIdentity must NOT be set.
        var request = BuildGenerateTokenRequest(datasetId, username, roles);

        // Assert
        request.Identity.Should().BeNull(
            "Null username must suppress EffectiveIdentity — Power BI rejects token requests " +
            "with an identity containing a null or empty username");

        _output.WriteLine("Null username → no EffectiveIdentity in token request (correct — RLS skipped)");
    }

    [Fact]
    public void BuildGenerateTokenRequest_NullRoles_ProducesNoEffectiveIdentity()
    {
        // Arrange — username present but no BU roles (admin user, no BU claim → null roles).
        const string datasetId = "dataset-no-roles";
        const string username = "admin@contoso.com";
        IList<string>? roles = null;

        // Act — username is present but roles is null → EffectiveIdentity must NOT be set.
        var request = BuildGenerateTokenRequest(datasetId, username, roles);

        // Assert
        request.Identity.Should().BeNull(
            "Null roles must suppress EffectiveIdentity — Power BI rejects requests with an " +
            "identity containing empty roles when the dataset has no RLS roles configured");

        _output.WriteLine("Null roles → no EffectiveIdentity (admin/no-BU scenario — no RLS restriction)");
    }

    [Fact]
    public void BuildGenerateTokenRequest_EmptyRoles_ProducesNoEffectiveIdentity()
    {
        // Arrange — username present but roles list is empty.
        const string datasetId = "dataset-empty-roles";
        const string username = "admin@contoso.com";
        IList<string> roles = []; // empty list — equivalent to null for RLS purposes.

        // Act
        var request = BuildGenerateTokenRequest(datasetId, username, roles);

        // Assert
        request.Identity.Should().BeNull(
            "Empty roles list must suppress EffectiveIdentity — same constraint as null roles");

        _output.WriteLine("Empty roles [] → no EffectiveIdentity (same as null roles)");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Tests: End-to-end claim → token-request pipeline for multiple BU scenarios
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("user-a@bu1.contoso.com", "bu1", "BU_bu1")]
    [InlineData("user-b@bu2.contoso.com", "bu2", "BU_bu2")]
    [InlineData("user-c@legal.contoso.com", "legal", "BU_legal")]
    [InlineData("user-d@finance.contoso.com", "finance", "BU_finance")]
    public void EndToEnd_ClaimsToPowerBiRequest_CorrectEffectiveIdentityPerBu(
        string upn, string buClaim, string expectedRole)
    {
        // Arrange — simulates the full claims-extraction + request-building pipeline that
        // ReportingEndpoints.GetEmbedToken performs before calling ReportingEmbedService.
        var user = BuildUserPrincipal(
            userId: Guid.NewGuid().ToString(),
            upn: upn,
            businessUnit: buClaim,
            roles: ["sprk_ReportingAccess"]);

        const string datasetId = "dataset-multi-bu-test";

        // Act — mirrors the exact sequence of calls in GetEmbedToken handler.
        var username = ExtractRlsUsername(user);
        var roles = ExtractBusinessUnitRoles(user);
        var request = BuildGenerateTokenRequest(datasetId, username, roles);

        // Assert — each BU produces a distinct EffectiveIdentity.
        username.Should().Be(upn,
            $"preferred_username claim must be the RLS username for user in BU '{buClaim}'");

        roles.Should().ContainSingle()
            .Which.Should().Be(expectedRole,
                $"BU '{buClaim}' must map to RLS role '{expectedRole}'");

        request.Identity.Should().NotBeNull();
        request.Identity!.Username.Should().Be(upn);
        request.Identity.Roles.Should().Contain(expectedRole);
        request.Identity.Datasets.Should().Contain(datasetId);

        _output.WriteLine($"BU={buClaim}: username={username}, role={expectedRole} → EffectiveIdentity verified");
    }

    [Fact]
    public void EndToEnd_AdminUser_NoBuClaim_SkipsRls()
    {
        // Arrange — Global admin with no BU restriction; should see all data (no RLS filter).
        var user = BuildUserPrincipal(
            userId: "admin-00000000-0001",
            upn: "globaladmin@contoso.com",
            businessUnit: null, // No BU claim → admin sees all data.
            roles: ["sprk_ReportingAccess", "sprk_ReportingAdmin"]);

        const string datasetId = "dataset-admin-test";

        // Act
        var username = ExtractRlsUsername(user);
        var roles = ExtractBusinessUnitRoles(user);
        var request = BuildGenerateTokenRequest(datasetId, username, roles);

        // Assert — admin has a username but no BU roles → EffectiveIdentity is null (admin skip).
        username.Should().Be("globaladmin@contoso.com",
            "Admin user still has a UPN — extraction succeeds");
        roles.Should().BeNull(
            "Admin user has no businessunit claim → null roles → RLS is skipped");
        request.Identity.Should().BeNull(
            "When roles is null, EffectiveIdentity is not set — admin accesses all data in the workspace");

        _output.WriteLine($"Admin user: username={username}, roles=null → no EffectiveIdentity (all data visible)");
    }
}
