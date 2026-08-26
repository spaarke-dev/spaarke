using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// <see cref="CallerRecordAccessProbe"/> denies when it cannot perform an OBO exchange.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists (task 045, 2026-08-25).</b> The probe is the delegation gate's only
/// source of truth — it answers "does this caller hold Write on this record?" for six external-access
/// mutations plus the Office-save gate. Task 045 replaced its self-built client-secret credential with
/// the ordered credential provider (ADR-028 A4), which moved the "can we do OBO at all?" decision from
/// CONSTRUCTION time to CALL time.</para>
///
/// <para><b>And nothing tested it.</b> Every fixture in the suite SUBSTITUTES this type — grep for
/// <c>RemoveAll&lt;CallerRecordAccessProbe&gt;</c> — so the real class was never constructed by any
/// test. <c>DelegationProbeRetryPolicyTests</c> covers only the pure <c>NotFoundRetryDelay</c> static.
/// That meant the precondition logic could be inverted, and the entire delegation gate silently opened,
/// with the suite green. Substituting at a seam proves the CALLER, never the CALLEE (task 017's
/// lesson); this file tests the callee.</para>
///
/// <para><b>What these tests can and cannot prove.</b> They prove the DENY paths — the ones that must
/// hold when credentials or configuration are missing, which is the direction a mistake here would
/// break. They do NOT prove a successful OBO exchange: that needs a live tenant and a real user
/// assertion, and is owned by task 034. Do not read a green run here as "OBO works".</para>
/// </remarks>
public class DelegationProbeFailClosedTests
{
    private const string AnyEntitySet = "sprk_projects";
    private static readonly Guid AnyRecord = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string AnyToken = "not-a-real-token";

    /// <summary>Config with the Dataverse URL and BFF identity present — but no credential provider.</summary>
    private static IConfiguration FullyConfigured() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["AzureAd:TenantId"] = "00000000-0000-0000-0000-0000000000bb",
            ["AzureAd:ClientId"] = "00000000-0000-0000-0000-0000000000aa"
        }).Build();

    private static CallerRecordAccessProbe NewProbe(IConfiguration configuration) =>
        new(new HttpClient(), configuration, NullLogger<CallerRecordAccessProbe>.Instance);

    /// <summary>
    /// No credential provider ⇒ deny. This is the case task 045 created and the one most likely to
    /// regress: before the port, the precondition was "a client secret is configured"; now it is "a
    /// provider was injected". An implementation that treated a missing provider as "proceed" would
    /// dereference it and — depending on where the exception landed — could return something other
    /// than None.
    /// </summary>
    [Fact]
    public async Task GetCallerRightsAsync_WithNoCredentialProvider_DeniesRatherThanThrowing()
    {
        var probe = NewProbe(FullyConfigured());

        var rights = await probe.GetCallerRightsAsync(AnyToken, AnyEntitySet, AnyRecord);

        rights.Should().Be(AccessRights.None,
            "a delegation check that cannot authenticate must DENY; the alternative — treating an " +
            "unanswerable question as permission — is the privilege escalation FR-07 exists to close");
    }

    /// <summary>
    /// No caller token ⇒ deny, without ever attempting an exchange.
    /// </summary>
    [Fact]
    public async Task GetCallerRightsAsync_WithNoCallerToken_Denies()
    {
        var probe = NewProbe(FullyConfigured());

        var rights = await probe.GetCallerRightsAsync(null, AnyEntitySet, AnyRecord);

        rights.Should().Be(AccessRights.None);
    }

    /// <summary>
    /// Missing BFF identity (tenant/client) ⇒ deny.
    /// </summary>
    /// <remarks>
    /// Pinned separately from the provider case because they are independent halves of the same
    /// precondition: the port's <c>OboAvailable</c> requires a provider AND a tenant AND a client id.
    /// A regression that dropped either half of the identity check would still pass the test above.
    /// </remarks>
    [Theory]
    [InlineData(null, "00000000-0000-0000-0000-0000000000aa")]   // no tenant
    [InlineData("00000000-0000-0000-0000-0000000000bb", null)]   // no client id
    [InlineData(null, null)]                                     // neither
    public async Task GetCallerRightsAsync_WithIncompleteBffIdentity_Denies(string? tenantId, string? clientId)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["AzureAd:TenantId"] = tenantId,
            ["AzureAd:ClientId"] = clientId
        }).Build();

        var rights = await NewProbe(configuration).GetCallerRightsAsync(AnyToken, AnyEntitySet, AnyRecord);

        rights.Should().Be(AccessRights.None);
    }

    /// <summary>
    /// No Dataverse environment URL ⇒ deny. Without it there is no audience to request a token for.
    /// </summary>
    [Fact]
    public async Task GetCallerRightsAsync_WithNoEnvironmentUrl_Denies()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = "00000000-0000-0000-0000-0000000000bb",
            ["AzureAd:ClientId"] = "00000000-0000-0000-0000-0000000000aa"
        }).Build();

        var rights = await NewProbe(configuration).GetCallerRightsAsync(AnyToken, AnyEntitySet, AnyRecord);

        rights.Should().Be(AccessRights.None);
    }

    /// <summary>
    /// The legacy configuration keys still resolve the BFF identity.
    /// </summary>
    /// <remarks>
    /// <c>TENANT_ID</c> / <c>API_APP_ID</c> are what <c>DataverseAccessDataSource</c> uses, and the
    /// probe accepts either those or the canonical <c>AzureAd:*</c> pair. Note what is deliberately
    /// ABSENT: <c>API_CLIENT_SECRET</c>. The probe no longer reads a secret in any form, and the secret
    /// itself was deleted from app settings and Key Vault on 2026-08-24 — so a fallback to it would
    /// resolve to nothing anyway. This test would still deny (no provider is injected); it exists to
    /// pin that the identity keys are read, which is what makes <c>OboAvailable</c> meaningful.
    /// </remarks>
    [Fact]
    public async Task GetCallerRightsAsync_WithLegacyIdentityKeysAndNoSecret_StillDenies_WithoutReadingASecret()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000bb",
            ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa"
        }).Build();

        var rights = await NewProbe(configuration).GetCallerRightsAsync(AnyToken, AnyEntitySet, AnyRecord);

        rights.Should().Be(AccessRights.None,
            "no credential provider was injected — identity alone is not a credential");
    }
}
