using System;
using System.Collections.Generic;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Auth;

/// <summary>
/// FR-A2 (auth-v4 task 011) — confidential-client sharing seam.
///
/// <para><b>The hazard.</b> <c>DataverseAccessDataSource</c> is a transient typed HttpClient
/// (<c>SpaarkeCore</c>) and <c>AgentTokenService</c> was registered scoped (<c>AgentModule</c>), so
/// each resolution built a fresh MSAL confidential client and discarded its OBO token cache —
/// forcing a network token exchange per request. From task 020 the credential becomes a
/// Managed-Identity client assertion, at which point a per-resolution client ALSO re-mints a signed
/// assertion (an IMDS round-trip) on every call. Client sharing stops being an optimization and
/// becomes a cost/correctness property of the credential.</para>
///
/// <para><b>How this is asserted, and why this shape.</b> The MSAL client lives in a private field.
/// Reading it across two instances would require reflection — <b>ADR-038 ban B8</b>; resolving twice
/// from a container is <b>ban B3</b>. So sharing is asserted through the one thing that is genuinely
/// observable: a <b>per-key build count</b>. Construct N instances under one key and exactly one
/// client must have been built. If an instance built its own instead of taking the cached one, the
/// count grows. That is a real behavioural difference, not a proxy for one.</para>
///
/// <para><b>Why per-key and not a process-wide total.</b> An earlier version asserted a delta on a
/// process-wide count, which is genuinely flaky: this assembly holds ~10,500 tests, there is no
/// <c>xunit.runner.json</c> and no assembly-level <c>CollectionBehavior</c>, so collections run in
/// parallel by default. Contract fixtures boot the real <c>Program.cs</c> with
/// <c>TENANT_ID</c> / <c>API_APP_ID</c> / <c>API_CLIENT_SECRET</c> set and do NOT override
/// <c>IAccessDataSource</c>, so the first such resolution adds an entry — and if that landed inside a
/// delta window, the assertion failed. Per-key counting is immune: other keys cannot perturb it. The
/// <c>DataverseCredentialSeam</c> collection is retained as belt-and-braces rather than as the
/// load-bearing guard it was.</para>
///
/// <para><b>What is still deferred to task 060.</b> These assertions prove the constructor consults
/// the shared cache under a correctly-scoped key. They cannot prove some FUTURE call site does not
/// bypass it — a bypassing site simply would not touch the counter. That guard is source analysis
/// over <c>ConfidentialClientApplicationBuilder.Create</c> call sites, which is the shape ADR-038
/// sanctions and which task 060 already builds. See notes/decisions/011-adr009-token-cache-decision.md.</para>
/// </summary>
[Collection(DataverseCredentialSeamCollection.Name)]
public class ConfidentialClientSharingSeamTests
{
    private const string DataverseUrl = "https://example.crm.dynamics.com";
    private const string Secret = "test-secret-value";

    // ---------------------------------------------------------------------------------------------
    // DataverseAccessDataSource — transient typed HttpClient, shares via the static client cache
    // ---------------------------------------------------------------------------------------------

    private static DataverseAccessDataSource CreateAccessDataSource(
        string tenantId, string clientId, string clientSecret = Secret)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = DataverseUrl,
            ["TENANT_ID"] = tenantId,
            ["API_APP_ID"] = clientId,
            ["API_CLIENT_SECRET"] = clientSecret,
            ["Graph:ManagedIdentity:Enabled"] = "true",
        }).Build();

        return new DataverseAccessDataSource(
            Mock.Of<IDataverseService>(),
            new HttpClient(),
            config,
            NullLogger<DataverseAccessDataSource>.Instance);
    }

    [Fact]
    public void AccessDataSource_ManyInstances_SameCredentials_BuildExactlyOneConfidentialClient()
    {
        // Unique key per run, so this assertion measures only what this test constructs.
        var tenantId = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid().ToString();

        // Five resolutions stands in for five requests against a transient typed HttpClient.
        for (var i = 0; i < 5; i++)
        {
            CreateAccessDataSource(tenantId, clientId).Should().NotBeNull();
        }

        DataverseAccessDataSource.ConfidentialClientBuildCountFor(tenantId, clientId, Secret)
            .Should().Be(1,
                "five instances sharing one credential must build ONE confidential client — one per " +
                "instance would discard MSAL's OBO token cache on every request");
    }

    [Fact]
    public void AccessDataSource_DifferentTenants_DoNotShareAConfidentialClient()
    {
        // The negative half: sharing must be KEYED, not global. A client shared across tenants would
        // be a cross-tenant token-cache leak — a far worse defect than the one this task fixes.
        var clientId = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid().ToString();
        var tenantB = Guid.NewGuid().ToString();

        CreateAccessDataSource(tenantA, clientId);
        CreateAccessDataSource(tenantB, clientId);

        DataverseAccessDataSource.ConfidentialClientBuildCountFor(tenantA, clientId, Secret)
            .Should().Be(1, "tenant A gets its own client");
        DataverseAccessDataSource.ConfidentialClientBuildCountFor(tenantB, clientId, Secret)
            .Should().Be(1, "tenant B gets a DIFFERENT client — token caches must not cross tenants");
    }

    [Fact]
    public void AccessDataSource_RotatedSecret_BuildsANewConfidentialClient_NotTheStaleOne()
    {
        // MSAL binds the credential at Build() and holds it for the client's lifetime. If the cache
        // key omitted the secret, a rotation would silently keep handing back a client built with the
        // OLD secret — presenting as AADSTS7000215 on OBO while the app-only path kept working.
        // This locks in that the secret participates in the key.
        var tenantId = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid().ToString();

        CreateAccessDataSource(tenantId, clientId, "secret-v1");
        CreateAccessDataSource(tenantId, clientId, "secret-v2");

        DataverseAccessDataSource.ConfidentialClientBuildCountFor(tenantId, clientId, "secret-v1")
            .Should().Be(1);
        DataverseAccessDataSource.ConfidentialClientBuildCountFor(tenantId, clientId, "secret-v2")
            .Should().Be(1, "a rotated secret must produce a NEW client, never reuse the stale one");
    }

    // ---------------------------------------------------------------------------------------------
    // AgentTokenService — now singleton, and shares structurally regardless of DI lifetime
    // ---------------------------------------------------------------------------------------------

    private static AgentTokenService CreateAgentTokenService(
        string tenantId, string clientId, string clientSecret = Secret)
        => new(
            Mock.Of<ITenantCache>(),
            Options.Create(new AgentTokenOptions
            {
                TenantId = tenantId,
                ClientId = clientId,
                ClientSecret = clientSecret,
                AgentAppId = Guid.NewGuid().ToString(),
                DataverseEnvironmentUrl = DataverseUrl,
            }),
            NullLogger<AgentTokenService>.Instance);

    [Fact]
    public void AgentTokenService_ManyInstances_SameCredentials_BuildExactlyOneConfidentialClient()
    {
        // Asserted by direct construction rather than through a container: ADR-038 ban B3 forbids a
        // DI-registration test, and the point here is that sharing does NOT depend on the
        // registration. AgentModule registers this singleton; the static cache is what makes the
        // guarantee survive a future lifetime regression.
        var tenantId = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid().ToString();

        for (var i = 0; i < 5; i++)
        {
            CreateAgentTokenService(tenantId, clientId).Should().NotBeNull();
        }

        AgentTokenService.ConfidentialClientBuildCountFor(tenantId, clientId, Secret)
            .Should().Be(1, "five instances sharing one credential must build ONE confidential client");
    }

    [Fact]
    public void AgentTokenService_DifferentTenants_DoNotShareAConfidentialClient()
    {
        var clientId = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid().ToString();
        var tenantB = Guid.NewGuid().ToString();

        CreateAgentTokenService(tenantA, clientId);
        CreateAgentTokenService(tenantB, clientId);

        AgentTokenService.ConfidentialClientBuildCountFor(tenantA, clientId, Secret).Should().Be(1);
        AgentTokenService.ConfidentialClientBuildCountFor(tenantB, clientId, Secret)
            .Should().Be(1, "agent OBO clients must be keyed per tenant so token caches cannot cross tenants");
    }
}

/// <summary>
/// Serialises the seam tests that construct <c>DataverseAccessDataSource</c> /
/// <c>AgentTokenService</c>.
///
/// <para><b>No longer load-bearing.</b> Since task 011's code-review pass the sharing assertions
/// count builds PER KEY, so concurrent construction under other keys cannot perturb them. This
/// collection is retained as defence in depth. If you add a test class that constructs either type,
/// joining this collection is good hygiene but is not required for correctness.</para>
/// </summary>
[CollectionDefinition(Name)]
public class DataverseCredentialSeamCollection
{
    public const string Name = "DataverseCredentialSeam";
}
