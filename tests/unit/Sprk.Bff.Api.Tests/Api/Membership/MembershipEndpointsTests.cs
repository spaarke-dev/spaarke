// R3 task 035 — unit tests for GET /api/users/me/memberships/{entityType}.
// Validates the FR-1A.9 contract:
//   - Auth gate: 401 unauthenticated, 200 authenticated
//   - 400 on malformed inputs (limit <= 0)
//   - 200 + MembershipResponse for happy path with mocked resolver
//   - Query params correctly mapped to MembershipResolveOptions (roles, identityTypes,
//     includeRelated, limit, continuationToken)
//   - includeRelated passed through (Phase 1A resolver ignores it, but the endpoint
//     MUST surface it to the resolver — task 054 activates the field)
//   - Helper-level coverage of ExtractAadObjectId + ParseCsv (no host required)
//
// Integration tests (AC-1A.3 + AC-1A.5) are deferred to P4 UAT against spaarkedev1
// per the task brief.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1A.9 + AC-1A.3;
//            design.md Part 1 § "Endpoint contract".

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Membership;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Membership;

/// <summary>
/// End-to-end-shape tests for <c>GET /api/users/me/memberships/{entityType}</c>.
/// Uses an in-process WebApplicationFactory with the resolver and Dataverse service
/// mocked, plus a fake auth scheme that emits the AAD <c>oid</c> claim per request.
/// </summary>
public sealed class MembershipEndpointsTests : IClassFixture<MembershipEndpointsTestFixture>
{
    private readonly MembershipEndpointsTestFixture _fixture;

    public MembershipEndpointsTests(MembershipEndpointsTestFixture fixture)
    {
        _fixture = fixture;
    }

    // ================================================================================
    // ===== Auth gate ================================================================
    // ================================================================================

    [Fact]
    public async Task GetMyMemberships_Returns401_WhenUnauthenticated()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/api/users/me/memberships/sprk_matter");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMyMemberships_Returns200_WhenAuthenticated_WithMockedResolver()
    {
        // Arrange — authenticated client, mocked resolver returns a tiny shape.
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        var expected = BuildSampleResponse(systemUserId, "sprk_matter", count: 2);
        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(systemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        // Act
        var response = await client.GetAsync("/api/users/me/memberships/sprk_matter");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MembershipResponse>();
        body.Should().NotBeNull();
        body!.EntityType.Should().Be("sprk_matter");
        body.Count.Should().Be(2);
        body.PersonIdentity.SystemUserId.Should().Be(systemUserId);
    }

    [Fact]
    public async Task GetMyMemberships_Returns401_WhenAaPrincipalHasNoSystemUserRow()
    {
        // Arrange — authenticated principal but Dataverse lookup returns 0 rows.
        var aadOid = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId: null);

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        // Act
        var response = await client.GetAsync("/api/users/me/memberships/sprk_matter");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================================
    // ===== Malformed input ==========================================================
    // ================================================================================

    [Fact]
    public async Task GetMyMemberships_Returns400_WhenLimitIsZero()
    {
        var aadOid = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, Guid.NewGuid());

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        var response = await client.GetAsync("/api/users/me/memberships/sprk_matter?limit=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMyMemberships_Returns400_WhenLimitIsNegative()
    {
        var aadOid = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, Guid.NewGuid());

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        var response = await client.GetAsync("/api/users/me/memberships/sprk_matter?limit=-5");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ================================================================================
    // ===== Query-param mapping → MembershipResolveOptions ==========================
    // ================================================================================

    [Fact]
    public async Task GetMyMemberships_MapsRolesAndIdentityTypesCsv_ToResolverOptions()
    {
        // Arrange
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        MembershipResolveOptions? captured = null;
        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(systemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MembershipResolveOptions?, CancellationToken>((_, _, opts, _) => captured = opts)
            .ReturnsAsync(BuildSampleResponse(systemUserId, "sprk_matter", 0));

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        // Act — pass roles + identityTypes as CSV
        var response = await client.GetAsync(
            "/api/users/me/memberships/sprk_matter?roles=owner,assignedAttorney&identityTypes=SystemUser,Contact");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.Roles.Should().NotBeNull();
        captured.Roles!.Should().BeEquivalentTo("owner", "assignedAttorney");
        captured.IdentityTypes.Should().NotBeNull();
        captured.IdentityTypes!.Should().BeEquivalentTo("SystemUser", "Contact");
    }

    [Fact]
    public async Task GetMyMemberships_MapsIncludeRelated_ToResolverOptions_ForPhase1DPassthrough()
    {
        // Arrange — Phase 1A resolver ignores includeRelated, but the endpoint MUST
        // surface it so task 054 can activate the field without touching MembershipEndpoints.cs.
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        MembershipResolveOptions? captured = null;
        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(systemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MembershipResolveOptions?, CancellationToken>((_, _, opts, _) => captured = opts)
            .ReturnsAsync(BuildSampleResponse(systemUserId, "sprk_matter", 0));

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        // Act
        var response = await client.GetAsync(
            "/api/users/me/memberships/sprk_matter?includeRelated=documents,events");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.IncludeRelated.Should().NotBeNull();
        captured.IncludeRelated!.Should().BeEquivalentTo("documents", "events");
    }

    [Fact]
    public async Task GetMyMemberships_MapsLimitAndContinuationToken_ToResolverOptions()
    {
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        MembershipResolveOptions? captured = null;
        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(systemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MembershipResolveOptions?, CancellationToken>((_, _, opts, _) => captured = opts)
            .ReturnsAsync(BuildSampleResponse(systemUserId, "sprk_matter", 0));

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        // Act
        var response = await client.GetAsync(
            "/api/users/me/memberships/sprk_matter?limit=250&continuationToken=opaque-cursor-abc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.Limit.Should().Be(250);
        captured.ContinuationToken.Should().Be("opaque-cursor-abc");
    }

    [Fact]
    public async Task GetMyMemberships_DefaultsLimitTo500_WhenNotSpecified()
    {
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        MembershipResolveOptions? captured = null;
        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(systemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MembershipResolveOptions?, CancellationToken>((_, _, opts, _) => captured = opts)
            .ReturnsAsync(BuildSampleResponse(systemUserId, "sprk_matter", 0));

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        var response = await client.GetAsync("/api/users/me/memberships/sprk_matter");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.Limit.Should().Be(MembershipResolveOptions.DefaultLimit);
    }

    [Fact]
    public async Task GetMyMemberships_ClampsLimitToMaxLimit_WhenAboveCeiling()
    {
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        MembershipResolveOptions? captured = null;
        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(systemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MembershipResolveOptions?, CancellationToken>((_, _, opts, _) => captured = opts)
            .ReturnsAsync(BuildSampleResponse(systemUserId, "sprk_matter", 0));

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        // Act — 99,999 is well above MaxLimit (5000)
        var response = await client.GetAsync(
            "/api/users/me/memberships/sprk_matter?limit=99999");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.Limit.Should().Be(MembershipResolveOptions.MaxLimit);
    }

    [Fact]
    public async Task GetMyMemberships_NullsOutEmptyQueryParams_SoResolverTreatsAsUseAll()
    {
        // Arrange — empty roles= and identityTypes= should resolve to null lists
        // (not empty lists) so MembershipResolverService treats them as "use all".
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        MembershipResolveOptions? captured = null;
        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(systemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, MembershipResolveOptions?, CancellationToken>((_, _, opts, _) => captured = opts)
            .ReturnsAsync(BuildSampleResponse(systemUserId, "sprk_matter", 0));

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        var response = await client.GetAsync(
            "/api/users/me/memberships/sprk_matter?roles=&identityTypes=&continuationToken=");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.Roles.Should().BeNull("empty CSV must be treated as 'use all'");
        captured.IdentityTypes.Should().BeNull("empty CSV must be treated as 'use all'");
        captured.ContinuationToken.Should().BeNull("empty token must be normalized to null");
    }

    // ================================================================================
    // ===== R3 Part 1D — task 054 — includeRelated 400 mapping ======================
    // ================================================================================

    [Fact]
    public async Task GetMyMemberships_Returns400_WhenResolverThrowsDepthExceeded_ExplicitChainSyntax()
    {
        // FR-1D.2 / Q3: explicit chain syntax in includeRelated → 400 BadRequest with
        // structured offendingEntry + reasonTag extensions.
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(systemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MembershipDepthExceededException(
                offendingEntry: "sprk_document.sprk_event",
                reasonTag: "explicit-chain-syntax",
                message: "includeRelated entry 'sprk_document.sprk_event' exceeds 1-hop max."));

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        var response = await client.GetAsync(
            "/api/users/me/memberships/sprk_matter?includeRelated=sprk_document.sprk_event");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"offendingEntry\":\"sprk_document.sprk_event\"");
        body.Should().Contain("\"reasonTag\":\"explicit-chain-syntax\"");
        body.Should().Contain("\"maxHops\":1");
    }

    [Fact]
    public async Task GetMyMemberships_Returns400_WhenResolverThrowsDepthExceeded_NotADirectLookupTarget()
    {
        // Related entity exists but has no 1-hop Lookup back to the primary → 400.
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(systemUserId, "sprk_matter", It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MembershipDepthExceededException(
                offendingEntry: "sprk_unrelated",
                reasonTag: "not-a-direct-lookup-target",
                message: "includeRelated entry 'sprk_unrelated' has no Lookup targeting 'sprk_matter'."));

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        var response = await client.GetAsync(
            "/api/users/me/memberships/sprk_matter?includeRelated=sprk_unrelated");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"reasonTag\":\"not-a-direct-lookup-target\"");
    }

    // ================================================================================
    // ===== Helper unit tests (no host) =============================================
    // ================================================================================

    [Fact]
    public void ExtractAadObjectId_ReturnsGuid_WhenOidClaimPresent()
    {
        var oid = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[] { new Claim("oid", oid.ToString()) }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = MembershipEndpoints.ExtractAadObjectId(principal);

        result.Should().Be(oid);
    }

    [Fact]
    public void ExtractAadObjectId_AcceptsLongFormSchemaUri_AsFallback()
    {
        var oid = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", oid.ToString())
        }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = MembershipEndpoints.ExtractAadObjectId(principal);

        result.Should().Be(oid);
    }

    [Fact]
    public void ExtractAadObjectId_ReturnsNull_WhenNoUsableClaim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "test"));

        var result = MembershipEndpoints.ExtractAadObjectId(principal);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractAadObjectId_ReturnsNull_WhenClaimNotParseableAsGuid()
    {
        var identity = new ClaimsIdentity(new[] { new Claim("oid", "not-a-guid") }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = MembershipEndpoints.ExtractAadObjectId(principal);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseCsv_ReturnsNull_WhenInputIsNullOrWhitespace()
    {
        MembershipEndpoints.ParseCsv(null).Should().BeNull();
        MembershipEndpoints.ParseCsv("").Should().BeNull();
        MembershipEndpoints.ParseCsv("   ").Should().BeNull();
    }

    [Fact]
    public void ParseCsv_SplitsAndTrimsTokens()
    {
        MembershipEndpoints.ParseCsv("a,b,c").Should().BeEquivalentTo("a", "b", "c");
        MembershipEndpoints.ParseCsv(" owner , assignedAttorney ").Should().BeEquivalentTo("owner", "assignedAttorney");
    }

    [Fact]
    public void ParseCsv_DropsEmptyTokens_AndReturnsNullIfAllEmpty()
    {
        MembershipEndpoints.ParseCsv(",,,").Should().BeNull();
        MembershipEndpoints.ParseCsv("a,,b").Should().BeEquivalentTo("a", "b");
    }

    // ================================================================================
    // ===== Helpers =================================================================
    // ================================================================================

    private static MembershipResponse BuildSampleResponse(Guid systemUserId, string entityType, int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        var identity = new PersonIdentity(systemUserId);
        var byRole = (IReadOnlyDictionary<string, IReadOnlyList<Guid>>)new Dictionary<string, IReadOnlyList<Guid>>();
        return new MembershipResponse(
            EntityType: entityType,
            PersonIdentity: identity,
            Ids: ids,
            ByRole: byRole,
            Count: count,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
            ContinuationToken: null);
    }
}

// ================================================================================
// ===== Fixture ==================================================================
// ================================================================================

/// <summary>
/// Test fixture for the membership endpoint tests. Bootstraps the BFF in-process
/// with the resolver and Dataverse service mocked + a fake authentication handler
/// that emits a configurable AAD <c>oid</c> claim per request.
/// </summary>
/// <remarks>
/// Mirrors <c>AdminJobsTestFixture</c>'s canonical config-key set so the host
/// builds even when this fixture is used in isolation. Per
/// <c>test-fixture-contracts.md</c> §F.2, any new validator added to <c>Program.cs</c>
/// needs a corresponding key added here.
/// </remarks>
public class MembershipEndpointsTestFixture : WebApplicationFactory<Program>
{
    public Mock<IMembershipResolverService> ResolverMock { get; } = new(MockBehavior.Strict);
    public Mock<IDataverseService> DataverseMock { get; } = new(MockBehavior.Loose);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["Analysis:Enabled"] = "true",
                ["Analysis:UseStubResolver"] = "true",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                // spaarke-redis-cache-remediation-r1 task 003 (FR-02): opt into in-memory fallback for tests.
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                ["AgentService:AgentId"] = "test-agent-id",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",
            };
            config.AddInMemoryCollection(dict);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // spaarke-redis-cache-remediation-r1 task 003 (FR-02): switch to Development for in-memory
        // cache fallback; disable ValidateScopes to preserve pre-existing test behavior.
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            // Override auth — fake handler that emits oid claim per X-Test-Oid header.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = MembershipFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = MembershipFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, MembershipFakeAuthHandler>(
                MembershipFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = MembershipFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = MembershipFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            // Substitute the mocked IDataverseService + IMembershipResolverService.
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(DataverseMock.Object);
            services.RemoveAll<IMembershipResolverService>();
            services.AddSingleton(ResolverMock.Object);
        });
    }

    public HttpClient CreateUnauthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        return client;
    }

    public HttpClient CreateAuthenticatedClient(Guid aadOid)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Oid", aadOid.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    /// <summary>
    /// Configure the mocked <see cref="IDataverseService.RetrieveMultipleAsync"/> to return
    /// the supplied systemuserid (or zero rows when <paramref name="systemUserId"/> is null)
    /// for the canonical AAD-oid → systemuserid cross-reference query.
    /// </summary>
    public void SeedSystemUserLookup(Guid aadOid, Guid? systemUserId)
    {
        DataverseMock.Reset();
        DataverseMock
            .Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q =>
                q.EntityName == "systemuser" &&
                q.Criteria.Conditions.Any(c =>
                    c.AttributeName == "azureactivedirectoryobjectid" &&
                    c.Values.Count == 1 &&
                    (Guid)c.Values[0]! == aadOid)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var collection = new EntityCollection();
                if (systemUserId.HasValue && systemUserId.Value != Guid.Empty)
                {
                    var entity = new Entity("systemuser") { Id = systemUserId.Value };
                    entity["systemuserid"] = systemUserId.Value;
                    collection.Entities.Add(entity);
                }
                return collection;
            });
    }
}

/// <summary>
/// Fake authentication handler for membership-endpoint tests. Emits an
/// authenticated identity carrying the AAD <c>oid</c> claim from the
/// <c>X-Test-Oid</c> header. No header → no Authorization → 401.
/// </summary>
internal sealed class MembershipFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "MembershipFakeAuth";

    public MembershipFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));
        }

        var oid = Request.Headers["X-Test-Oid"].ToString();
        if (string.IsNullOrWhiteSpace(oid))
        {
            return Task.FromResult(AuthenticateResult.Fail("No X-Test-Oid header"));
        }

        var claims = new List<Claim>
        {
            new("oid", oid),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, oid),
            new(System.Security.Claims.ClaimTypes.Name, $"Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
