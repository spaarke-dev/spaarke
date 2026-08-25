using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Auth;

/// <summary>
/// FR-B3 (auth-v4 task 022) — the migration of the BFF-identity confidential clients onto ordered
/// credential selection.
///
/// <para><b>What this file protects, and what it deliberately does not.</b> The migration's headline
/// claim — "the credential moved" — is largely structural, and structural claims belong in
/// <c>tests/Spaarke.ArchTests/</c> as source analysis (task 060), not here: a runtime test cannot
/// observe which credential a private MSAL client was built with without reflection (ADR-038 ban B8).
/// What IS observable, and what actually matters if the migration is wrong, is the <b>fail-closed
/// contract</b>: every migrated path must deny rather than degrade when no credential can be obtained.
/// That is NFR-03, it is the property whose violation locks out or — far worse — silently admits every
/// user, and it is what this file asserts on each of the four OBO paths.</para>
///
/// <para><b>Why "no provider" is the right stand-in for "no credential".</b> Exercising a real
/// exhausted-credential path needs either a live identity or a network round trip, neither of which is
/// deterministic across a developer workstation and a GitHub-hosted runner (see
/// <c>CredentialOrderingSeamTests</c>'s remarks on why). An absent provider reaches the same branch —
/// the request cannot be authenticated as the BFF — through the one door that is deterministic
/// everywhere. Credential SELECTION itself, including both orderings and the fall-through table, is
/// covered by <c>CredentialOrderingSeamTests</c>; this file is about what the CONSUMERS do.</para>
/// </summary>
public class ConfidentialClientMigrationSeamTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string AppId = "22222222-2222-2222-2222-222222222222";
    private const string DataverseUrl = "https://example.crm.dynamics.com";

    // =============================================================================================
    // Fail-closed, path by path. One test per migrated OBO consumer.
    // =============================================================================================

    [Fact]
    public async Task DataverseAccessDataSource_WithNoObtainableCredential_ReturnsAccessRightsNone_NeverErrorOpen()
    {
        // THE highest-blast-radius assertion in the project. This type backs row-level authorization on
        // every document and AI endpoint that runs an authorization filter. If a credential failure ever
        // surfaced as anything other than AccessRights.None — an exception that some caller treats as
        // "allow", or a partially-populated snapshot — the migration would have converted a lockout into
        // a data leak. Fails CLOSED, always.
        var sut = new DataverseAccessDataSource(
            Mock.Of<IDataverseService>(),
            new HttpClient(),
            Config(),
            NullLogger<DataverseAccessDataSource>.Instance,
            credential: null,
            confidentialClients: null);

        var snapshot = await sut.GetUserAccessAsync(
            userId: Guid.NewGuid().ToString(),
            resourceId: Guid.NewGuid().ToString(),
            userAccessToken: "a-user-bearer-token",
            ct: CancellationToken.None);

        snapshot.AccessRights.Should().Be(AccessRights.None,
            "an unobtainable confidential credential must deny access, never error open");
        snapshot.Roles.Should().BeEmpty();
        snapshot.TeamMemberships.Should().BeEmpty();
    }

    [Fact]
    public async Task DataverseUserClient_WithNoCredentialProvider_FailsClosed_WithNoAppOnlyFallback()
    {
        // The dataverse.* tool namespace has, by design, NO app-only path: falling back to a service
        // identity here would be an authorization side-channel, not a degradation. The migration must
        // not have introduced one.
        var sut = new DataverseUserClient(
            new HttpClient(),
            Options.Create(new DataverseOptions { EnvironmentUrl = DataverseUrl }),
            Config(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            NullLogger<DataverseUserClient>.Instance,
            confidentialClients: null);

        var response = await sut.GetAsync("accounts", CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.ErrorCode.Should().Be(DataverseUserClientErrorCodes.OboNotConfigured,
            "dataverse.* tools require user-context OBO and must fail closed without it");
    }

    [Fact]
    public async Task AgentTokenService_WithNoCredentialProvider_ReturnsFailure_NeverAToken()
    {
        // The M365 Copilot agent path. A silent success here would mean the agent acting with no proven
        // identity; the contract is an explicit failure result.
        var sut = new AgentTokenService(
            Mock.Of<ITenantCache>(),
            Options.Create(new AgentTokenOptions
            {
                TenantId = Tenant,
                ClientId = AppId,
                AgentAppId = Guid.NewGuid().ToString(),
                DataverseEnvironmentUrl = DataverseUrl,
            }),
            NullLogger<AgentTokenService>.Instance,
            confidentialClients: null);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer not-a-real-token";

        var result = await sut.AcquireGraphTokenAsync(httpContext, CancellationToken.None);

        result.IsSuccess.Should().BeFalse("no credential provider means no proven identity");
        result.Token.Should().BeNull();
    }

    // =============================================================================================
    // The app-only adapter routes through ordered selection rather than being a second credential
    // source of its own.
    // =============================================================================================

    [Fact]
    public async Task ConfidentialClientTokenCredential_AsksTheOrderedProvider_ForEveryTokenRequest()
    {
        // ConfidentialClientTokenCredential is what replaced five inline ClientSecretCredential /
        // AuthType=ClientSecret constructions. Its whole value is that it is a PASS-THROUGH to ordered
        // selection: if it ever resolved a credential itself, the project would have removed five
        // per-call-site credential sites by adding a sixth.
        //
        // Asked EVERY time, not once and cached: the provider re-evaluates when a suppressed
        // higher-priority credential recovers, so an adapter that cached the client would pin the
        // process to a fallback after a single transient blip.
        var provider = new RecordingProvider();
        var sut = new ConfidentialClientTokenCredential(provider, Tenant, AppId);

        var context = new TokenRequestContext(new[] { $"{DataverseUrl}/.default" });

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<RecordingProvider.Sentinel>(
                async () => await sut.GetTokenAsync(context, CancellationToken.None));
        }

        provider.Calls.Should().Be(3, "the credential must consult ordered selection on every acquisition");
        provider.LastTenantId.Should().Be(Tenant);
        provider.LastClientId.Should().Be(AppId,
            "the app-only token must be requested for the APP REGISTRATION, never the managed identity's "
            + "clientId — that conflation is the FR-B4 silent-failure mode");
    }

    // NOT asserted here, deliberately: that ConfidentialClientTokenCredential's constructor rejects a
    // blank tenant/client id. The guard exists (ArgumentException.ThrowIfNullOrWhiteSpace) but a test
    // for it sits on the wrong side of ADR-038 ban B4 — it would assert a framework one-liner, and every
    // consumer already refuses to reach the adapter without a configured identity (DataverseAccessDataSource
    // and DataverseUserClient via OboAvailable, the rest via explicit constructor checks). That is where
    // the property is worth asserting, and where the fail-closed tests above assert it.

    // =============================================================================================

    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = DataverseUrl,
            ["TENANT_ID"] = Tenant,
            ["API_APP_ID"] = AppId,
            // Managed identity ON, so the app-only branch does not demand a provider and this file's
            // subject stays the OBO paths.
            ["Graph:ManagedIdentity:Enabled"] = "true",
        }).Build();

    /// <summary>
    /// Records what the consumer asked for and then refuses to hand back a client. Refusing is
    /// deliberate: a stub that returned a real <see cref="IConfidentialClientApplication"/> would make
    /// the next line a live token request, and the assertions here are about the CALL, not the token.
    /// </summary>
    private sealed class RecordingProvider : IConfidentialClientProvider
    {
        public int Calls { get; private set; }
        public string? LastTenantId { get; private set; }
        public string? LastClientId { get; private set; }

        public Task<IConfidentialClientApplication> GetClientAsync(
            string tenantId, string clientId, CancellationToken ct = default)
        {
            Calls++;
            LastTenantId = tenantId;
            LastClientId = clientId;
            throw new Sentinel();
        }

        internal sealed class Sentinel : Exception;
    }
}
