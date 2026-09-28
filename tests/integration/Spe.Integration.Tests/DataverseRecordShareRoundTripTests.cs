using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Xunit;

namespace Spe.Integration.Tests;

/// <summary>
/// unified-access-control-r2 task 060 — the consolidated POA seam grants, reads back and REVOKES, for
/// both principal kinds, through one parameterized method set.
/// </summary>
/// <remarks>
/// <para><b>Why this is LIVE-gated rather than mocked.</b> The claims worth making here are claims about
/// Dataverse: that <c>RevokeAccess</c> actually removes the row <c>GrantAccess</c> wrote when both are
/// keyed the same way, and that revoking a share that was never granted is a no-op rather than a fault.
/// A mock cannot be wrong about either — it would assert our own assumptions back at us. ADR-038's
/// "real test tenant &gt; emulator" plus its <c>Mock&lt;HttpMessageHandler&gt;</c> ban point at exactly
/// this shape: <see cref="SkippableFactAttribute"/>, real environment, skipped when unconfigured.
/// Matches <see cref="DataverseWebApiFieldMappingRegressionTests"/>.</para>
///
/// <para><b>What runs in CI instead.</b> The always-on coverage of this seam is
/// <c>PoaShareClientSingletonGuardTests</c> (only one POA client exists, and it exposes revoke),
/// <c>DataversePrincipalRefTests</c> (the principal parameterization), and
/// <c>DirectThreadAccessServiceTests</c> (the no-leak negatives at the module boundary).</para>
///
/// <para><b>Test data</b>: set <c>SPAARKE_TEST_SHARE_RECORD_ID</c> (an <c>sprk_document</c> row),
/// <c>SPAARKE_TEST_SHARE_USER_ID</c> (a systemuser) and optionally <c>SPAARKE_TEST_SHARE_TEAM_ID</c>.
/// Each test grants and then revokes, leaving the record as it found it.</para>
/// </remarks>
public class DataverseRecordShareRoundTripTests
{
    private const string EntityLogicalName = "sprk_document";
    private const string EntitySetName = "sprk_documents";

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder().AddEnvironmentVariables().Build();

    private static bool IsLiveConfigured(IConfiguration cfg)
    {
        var url = cfg["Dataverse:ServiceUrl"];
        return !string.IsNullOrEmpty(url)
            && !url.Contains("test.crm.dynamics.com", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(cfg["Dataverse:ClientSecret"])
            && !string.IsNullOrEmpty(cfg["TENANT_ID"])
            && !string.IsNullOrEmpty(cfg["API_APP_ID"]);
    }

    private static Guid? ConfiguredGuid(IConfiguration cfg, string key) =>
        Guid.TryParse(cfg[key], out var id) && id != Guid.Empty ? id : null;

    private static DataverseWebApiService BuildService(IConfiguration cfg, HttpClient http) =>
        new(http, cfg, NullLogger<DataverseWebApiService>.Instance);

    /// <summary>
    /// Grant → read → revoke → read, for a systemuser principal. The second read is the assertion that
    /// matters: it is what proves revoke matched the row grant wrote (the A-13/FR-16 matcher lesson).
    /// </summary>
    [SkippableFact]
    [Trait("Category", "Authorization")]
    public async Task GrantThenRevoke_SystemUserPrincipal_RoundTripsAndLeavesNoAccess()
    {
        var cfg = BuildConfig();
        Skip.IfNot(IsLiveConfigured(cfg), "Live Dataverse not configured.");

        var recordId = ConfiguredGuid(cfg, "SPAARKE_TEST_SHARE_RECORD_ID");
        var userId = ConfiguredGuid(cfg, "SPAARKE_TEST_SHARE_USER_ID");
        Skip.If(recordId is null || userId is null,
            "Set SPAARKE_TEST_SHARE_RECORD_ID + SPAARKE_TEST_SHARE_USER_ID to run the POA round-trip.");

        using var http = new HttpClient();
        var sut = BuildService(cfg, http);
        var principal = DataversePrincipalRef.User(userId!.Value);

        try
        {
            await sut.GrantAccessAsync(EntitySetName, recordId!.Value, principal, "ReadAccess");

            var afterGrant = await sut.GetPrincipalAccessAsync(EntityLogicalName, recordId.Value);
            afterGrant.Select(s => s.Principal).Should().Contain(principal,
                "the grant must be readable back through the same seam that wrote it");
            afterGrant.Single(s => s.Principal == principal).Principal.Kind
                .Should().Be(DataversePrincipalKind.SystemUser,
                    "the read is principal-kind-typed, not assumed");
        }
        finally
        {
            await sut.RevokeAccessAsync(EntitySetName, recordId!.Value, principal);
        }

        var afterRevoke = await sut.GetPrincipalAccessAsync(EntityLogicalName, recordId.Value);
        afterRevoke.Select(s => s.Principal).Should().NotContain(principal,
            "revoke takes the SAME key shape as grant, so it must remove the row grant created");
    }

    /// <summary>
    /// The identical round-trip for a team principal, through the identical methods — the acceptance
    /// criterion that the seam is PARAMETERIZED rather than two overloaded copies.
    /// </summary>
    [SkippableFact]
    [Trait("Category", "Authorization")]
    public async Task GrantThenRevoke_TeamPrincipal_UsesTheSameMethodsAndRoundTrips()
    {
        var cfg = BuildConfig();
        Skip.IfNot(IsLiveConfigured(cfg), "Live Dataverse not configured.");

        var recordId = ConfiguredGuid(cfg, "SPAARKE_TEST_SHARE_RECORD_ID");
        var teamId = ConfiguredGuid(cfg, "SPAARKE_TEST_SHARE_TEAM_ID");
        Skip.If(recordId is null || teamId is null,
            "Set SPAARKE_TEST_SHARE_RECORD_ID + SPAARKE_TEST_SHARE_TEAM_ID to run the team round-trip.");

        using var http = new HttpClient();
        var sut = BuildService(cfg, http);
        var principal = DataversePrincipalRef.Team(teamId!.Value);

        try
        {
            await sut.GrantAccessAsync(EntitySetName, recordId!.Value, principal, "ReadAccess");

            var afterGrant = await sut.GetPrincipalAccessAsync(EntityLogicalName, recordId.Value);
            afterGrant.Select(s => s.Principal).Should().Contain(principal);
            afterGrant.Single(s => s.Principal == principal).Principal.Kind
                .Should().Be(DataversePrincipalKind.Team,
                    "a team share must read back AS a team — the pre-060 reads reported every share as "
                    + "whichever kind the caller happened to expect");
        }
        finally
        {
            await sut.RevokeAccessAsync(EntitySetName, recordId!.Value, principal);
        }

        var afterRevoke = await sut.GetPrincipalAccessAsync(EntityLogicalName, recordId.Value);
        afterRevoke.Select(s => s.Principal).Should().NotContain(principal);
    }

    /// <summary>
    /// Pins the real Dataverse semantics of revoking a share that does not exist. The POML asks for
    /// whichever is real to be pinned rather than assumed; Dataverse treats it as a no-op, which is what
    /// makes revoke safely idempotent for the FR-29 "+ User" picker.
    /// </summary>
    [SkippableFact]
    [Trait("Category", "Authorization")]
    public async Task Revoke_WithoutAPriorGrant_IsANoOpNotAFault()
    {
        var cfg = BuildConfig();
        Skip.IfNot(IsLiveConfigured(cfg), "Live Dataverse not configured.");

        var recordId = ConfiguredGuid(cfg, "SPAARKE_TEST_SHARE_RECORD_ID");
        var userId = ConfiguredGuid(cfg, "SPAARKE_TEST_SHARE_USER_ID");
        Skip.If(recordId is null || userId is null,
            "Set SPAARKE_TEST_SHARE_RECORD_ID + SPAARKE_TEST_SHARE_USER_ID to run this.");

        using var http = new HttpClient();
        var sut = BuildService(cfg, http);
        var principal = DataversePrincipalRef.User(userId!.Value);

        // Ensure there is nothing to revoke, then revoke again.
        await sut.RevokeAccessAsync(EntitySetName, recordId!.Value, principal);

        var act = async () => await sut.RevokeAccessAsync(EntitySetName, recordId.Value, principal);

        await act.Should().NotThrowAsync(
            "revoking an absent share is a Dataverse no-op; callers rely on that for idempotent unshare");
    }

    /// <summary>
    /// NFR-01: a failed action call surfaces, never silently succeeds. A non-existent target record is
    /// the cheapest real failure to provoke.
    /// </summary>
    [SkippableFact]
    [Trait("Category", "Authorization")]
    public async Task Grant_AgainstAMissingRecord_SurfacesTheFailure()
    {
        var cfg = BuildConfig();
        Skip.IfNot(IsLiveConfigured(cfg), "Live Dataverse not configured.");

        using var http = new HttpClient();
        var sut = BuildService(cfg, http);

        var act = async () => await sut.GrantAccessAsync(
            EntitySetName, Guid.NewGuid(), DataversePrincipalRef.User(Guid.NewGuid()), "ReadAccess");

        await act.Should().ThrowAsync<HttpRequestException>(
            "the write path propagates errors (EnsureSuccessStatusCode) — a share that did not happen "
            + "must never be reported as one that did");
    }
}
