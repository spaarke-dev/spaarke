// Task 030 — CIAM external-access surface integration/contract tests (KEEP path: contract + security-auth).
//
// Scope (per POML 030 §goal + spec FR-08 / NFR-03 + project CLAUDE.md §"Broker-only" + "Authz-before-stream"):
//   - The download authz-before-stream property (NFR-03, the single highest-consequence property):
//     an unauthorized caller receives 403 with NO bytes and NO SPE-pointer resolution / Graph read.
//   - The CIAM external group's auth gate (401 when unauthenticated — CiamExternal policy).
//   - The admin invite-and-grant composition: provisioner idempotency (oid-present ⇒ no second CIAM
//     account, FR-08) and the broker-only grant (writes sprk_externalrecordaccess + invalidates the
//     participation cache, NO synthetic SPE permission — task 026 / ADR-028 Amendment A1).
//
// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Categories: endpoint-contract (route + status + payload shape) and security-auth (authz gate,
//     authz-before-side-effect). Physically at tests/integration/contract/**; the Sprk.Bff.Api.Tests
//     csproj auto-includes ../../integration/contract/**/*.cs so this compiles into that assembly.
//
// Placement note (CLAUDE.md §6.5 — Path C, ADR-compliant placement): POML 030 <outputs> names
//   tests/unit/Sprk.Bff.Api.Tests/. That path predates the ADR-038 KEEP-path reorg; these are
//   integration tests through the HTTP surface, so they live at the canonical contract KEEP path
//   (mirrors ComposeEndpointsContractTests). The wrap-up test-diet references them there.
//
// Banned-pattern compliance (ADR-038 §4 / tests/CLAUDE.md 17 bans):
//   - B1: NO Mock<HttpMessageHandler>. The Dataverse-backed services (ExternalParticipationService,
//     ExternalDataService, DataverseWebApiClient) are raw-HttpClient designs; mocking them at the
//     HttpClient/handler level is banned. Instead we mock at the MODULE BOUNDARY via the (additive,
//     backward-compatible) `virtual` seams added in this task + the existing ISpeFileOperations /
//     IDocumentStorageResolver / ITenantCache interfaces.
//   - B3/B4: NO DI-registration or ctor-null tests. Every test asserts HTTP-observable behavior.
//   - B5: in-process collaborators are NOT mocked except at the sanctioned module boundaries above.
//   - B13: names are {Method}_{Scenario}_{ExpectedResult}. B6/B7/B9/B10: behavior-shaped assertions.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// Integration-contract + security-auth tests for the CIAM external-access surface (Phase-2 tasks
/// 020–027). Boots the BFF in-process via <see cref="WebApplicationFactory{TEntryPoint}"/>, replaces
/// the Dataverse-backed services at their module boundaries (the <c>virtual</c> seams + existing
/// interfaces), and asserts the observable HTTP contract — with the download authz-before-stream
/// negative case (NFR-03) as the centerpiece.
/// </summary>
public sealed class ExternalAccessContractTests : IClassFixture<ExternalAccessContractFixture>
{
    private static readonly Guid ProjectA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DocumentX = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly ExternalAccessContractFixture _fixture;

    public ExternalAccessContractTests(ExternalAccessContractFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    // ================================================================================
    // ===== (1) CIAM external group auth gate ========================================
    // ================================================================================

    [Theory]
    [InlineData("/api/v1/external/me")]
    [InlineData("/api/v1/external/projects/11111111-1111-1111-1111-111111111111/documents/33333333-3333-3333-3333-333333333333/content")]
    public async Task ExternalGroup_WhenUnauthenticated_Returns401(string path)
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the /api/v1/external group is pinned to the CiamExternal policy (RequireAuthenticatedUser); " +
            "a call with no token must be rejected with 401 before any handler or filter runs");
    }

    // ================================================================================
    // ===== (2) Download authz-before-stream — NEGATIVE (THE centerpiece, NFR-03) =====
    // ================================================================================

    [Fact]
    public async Task DownloadDocument_WhenCallerLacksProjectAccess_Returns403_AndNeverResolvesPointersOrReadsContent()
    {
        // Caller is authenticated but has NO participation for ProjectA.
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: Array.Empty<Guid>());

        var response = await client.GetAsync(
            $"/api/v1/external/projects/{ProjectA}/documents/{DocumentX}/content");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "project access is checked BEFORE any storage work; a caller with no participation gets 403");

        // Authz-before-stream: neither the SPE pointer resolver NOR the SPE content read may be touched.
        _fixture.StorageResolverMock.Verify(
            r => r.GetSpePointersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "an unauthorized caller must never trigger SPE pointer resolution (no Graph-adjacent read)");
        _fixture.SpeFileOperationsMock.Verify(
            s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "an unauthorized caller must never trigger the app-only SPE content download (no bytes)");
    }

    // ================================================================================
    // ===== (3) Download document→project scoping — NEGATIVE ==========================
    // ================================================================================

    [Fact]
    public async Task DownloadDocument_WhenDocumentNotInRequestedProject_Returns403_AndNeverResolvesPointers()
    {
        // Caller HAS access to ProjectA, but the document belongs to ProjectB (scoping mismatch).
        using var client = _fixture.CreateAuthenticatedClient(
            accessibleProjects: new[] { ProjectA },
            documentProjectId: ProjectB);

        var response = await client.GetAsync(
            $"/api/v1/external/projects/{ProjectA}/documents/{DocumentX}/content");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "document→project scoping is enforced before streaming; a doc outside the requested project is 403");

        _fixture.StorageResolverMock.Verify(
            r => r.GetSpePointersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "scoping denial must also happen before any SPE pointer resolution");
    }

    // ================================================================================
    // ===== (4) Download authz-before-stream — POSITIVE ==============================
    // ================================================================================

    [Fact]
    public async Task DownloadDocument_WhenAuthorizedAndDocumentInProject_Returns200_AndStreamsBytes()
    {
        var payload = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // "%PDF"
        _fixture.StorageResolverMock
            .Setup(r => r.GetSpePointersAsync(DocumentX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("drive-ext-1", "item-ext-1"));
        _fixture.SpeFileOperationsMock
            .Setup(s => s.DownloadFileAsync("drive-ext-1", "item-ext-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(payload));

        using var client = _fixture.CreateAuthenticatedClient(
            accessibleProjects: new[] { ProjectA },
            documentProjectId: ProjectA);

        var response = await client.GetAsync(
            $"/api/v1/external/projects/{ProjectA}/documents/{DocumentX}/content");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsByteArrayAsync();
        body.Should().Equal(payload, "an authorized, in-project download streams the app-only SPE bytes");
    }

    // ================================================================================
    // ===== (5) Provisioner idempotency (FR-08) ======================================
    // ================================================================================

    [Fact]
    public async Task InviteAndGrant_WhenContactAlreadyHasOid_ReturnsAlreadyProvisioned_AndCreatesNoSecondCiamAccount()
    {
        // The Contact lookup returns an existing Contact whose oid is already bound.
        var boundContactId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        _fixture.Dataverse.ContactQueryResult =
            $$"""[{"contactid":"{{boundContactId}}","sprk_externalobjectid":"existing-oid-abc"}]""";

        using var client = _fixture.CreateAdminClient();

        var request = new
        {
            email = "attorney@firm.example",
            projectId = ProjectA,
            accessLevel = (int)ExternalAccessLevel.Collaborate,
            firstName = "Ada",
            lastName = "Counsel"
        };

        var response = await client.PostAsJsonAsync("/api/v1/external-access/invite-and-grant", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("onboardStatus").GetString().Should().Be("AlreadyProvisioned",
            "an oid-bound Contact takes the idempotent path — the CIAM provisioner is never invoked, " +
            "so no second CIAM account is created (FR-08). If it HAD been invoked, the real provisioner " +
            "would attempt live MSAL/Key-Vault and the request would 500 — a 200/AlreadyProvisioned proves " +
            "the early return.");
        _fixture.Dataverse.ContactUpdates.Should().BeEmpty(
            "the idempotent path must not re-bind the oid (no contacts UpdateAsync)");
    }

    // ================================================================================
    // ===== (6) Broker-only grant: writes record + invalidates cache, no synthetic SPE ==
    // ================================================================================

    [Fact]
    public async Task InviteAndGrant_WhenGranting_WritesExternalRecordAccess_InvalidatesCache_AndGrantsNoSyntheticSpePermission()
    {
        var boundContactId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        _fixture.Dataverse.ContactQueryResult =
            $$"""[{"contactid":"{{boundContactId}}","sprk_externalobjectid":"existing-oid-xyz"}]""";

        using var client = _fixture.CreateAdminClient();

        var request = new
        {
            email = "counsel@firm.example",
            projectId = ProjectA,
            accessLevel = (int)ExternalAccessLevel.ViewOnly,
            firstName = "Grace",
            lastName = "Advocate"
        };

        var response = await client.PostAsJsonAsync("/api/v1/external-access/invite-and-grant", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("accessRecordId").GetGuid().Should().NotBeEmpty();

        _fixture.Dataverse.CreatedEntitySets.Should().ContainSingle()
            .Which.Should().Be("sprk_externalrecordaccesses",
                "the grant writes exactly one sprk_externalrecordaccess record (grantee = Contact)");

        _fixture.TenantCacheMock.Verify(
            c => c.RemoveAsync(It.IsAny<string>(), "external-access-grant", boundContactId.ToString(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once(),
            "granting must invalidate the Contact's participation cache (ADR-009)");

        _fixture.SpeFileOperationsMock.Verify(
            s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _fixture.SpeFileOperationsMock.VerifyNoOtherCalls();
        // Broker-only (ADR-028 A1 / task 026): no synthetic SPE container membership is written on grant —
        // the grant path touches no SPE facade at all.
    }
}

// ================================================================================
// ===== Fixture ==================================================================
// ================================================================================

/// <summary>
/// In-process BFF fixture for the external-access contract tests. Mirrors the canonical config-key set
/// (per <c>.claude/constraints/bff-extensions.md §F.2</c>) plus the <c>Ciam:*</c> keys so the "Ciam"
/// JwtBearer scheme + <c>CiamGraphClientFactory</c> construct at startup, then swaps the Dataverse-backed
/// services + SPE facade for module-boundary doubles that scenarios drive via request headers.
/// </summary>
public sealed class ExternalAccessContractFixture : WebApplicationFactory<Program>
{
    private const string TestScheme = "TestExternalAuth";

    public Mock<IDocumentStorageResolver> StorageResolverMock { get; } = new(MockBehavior.Loose);
    public Mock<ISpeFileOperations> SpeFileOperationsMock { get; } = new(MockBehavior.Loose);
    public Mock<ITenantCache> TenantCacheMock { get; } = new(MockBehavior.Loose);
    public StubDataverseWebApiClient Dataverse { get; } = new();

    /// <summary>Reset per-test mutable double state so tests are independent.</summary>
    public void Reset()
    {
        StorageResolverMock.Reset();
        SpeFileOperationsMock.Reset();
        TenantCacheMock.Reset();
        Dataverse.Reset();
    }

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
                // CIAM second scheme (task 020) — keys required so AddJwtBearer("Ciam") + CiamGraphClientFactory bind.
                ["Ciam:Instance"] = "https://spaarketest.ciamlogin.com",
                ["Ciam:TenantId"] = "00000000-0000-0000-0000-0000000000c1",
                ["Ciam:ClientId"] = "ciam-api-client-id",
                ["Ciam:Audience"] = "api://ciam-api-client-id",
                ["Ciam:Domain"] = "spaarketest.onmicrosoft.com",
                ["Ciam:GraphProvisioner:ClientId"] = "ciam-graph-provisioner-id",
                ["Ciam:GraphProvisioner:CertificateName"] = "ciam-graph-cert",
                ["ExternalAccess:PortalUrl"] = "https://external.spaarke.test",
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
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["Analysis:Enabled"] = "true",
                ["Analysis:UseStubResolver"] = "true",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
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
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);

            // Fake auth scheme serving BOTH the workforce default (admin group) and the CiamExternal policy.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestScheme;
                options.DefaultChallengeScheme = TestScheme;
            }).AddScheme<AuthenticationSchemeOptions, ExternalTestAuthHandler>(TestScheme, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestScheme;
                options.DefaultChallengeScheme = TestScheme;
            });

            // Re-point the CiamExternal policy at the fake scheme (real "Ciam" JwtBearer needs live token infra).
            services.Configure<AuthorizationOptions>(options =>
            {
                options.AddPolicy(AuthPolicies.CiamExternal, policy =>
                {
                    policy.AuthenticationSchemes = new[] { TestScheme };
                    policy.RequireAuthenticatedUser();
                });
            });

            services.RemoveAll<IHostedService>();

            // Avoid real MSAL on incidental Graph paths.
            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // ── Module-boundary doubles ──────────────────────────────────────────
            services.RemoveAll<IDocumentStorageResolver>();
            services.AddScoped(_ => StorageResolverMock.Object);

            services.RemoveAll<ISpeFileOperations>();
            services.AddScoped(_ => SpeFileOperationsMock.Object);

            services.RemoveAll<ITenantCache>();
            services.AddSingleton(TenantCacheMock.Object);

            services.RemoveAll<DataverseWebApiClient>();
            services.AddSingleton<DataverseWebApiClient>(Dataverse);

            // Replace the Dataverse-backed external services with header-driven stubs (virtual seams).
            services.RemoveAll<ExternalParticipationService>();
            services.AddScoped<ExternalParticipationService>(sp =>
                new StubExternalParticipationService(sp.GetRequiredService<IHttpContextAccessor>()));

            services.RemoveAll<ExternalDataService>();
            services.AddScoped<ExternalDataService>(sp =>
                new StubExternalDataService(sp.GetRequiredService<IHttpContextAccessor>()));
        });
    }

    public HttpClient CreateUnauthenticatedClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>External (CIAM) caller. Scenario is carried on request headers read by the stub services.</summary>
    public HttpClient CreateAuthenticatedClient(IReadOnlyList<Guid> accessibleProjects, Guid? documentProjectId = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        client.DefaultRequestHeaders.Add("X-Test-Contact", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Projects", string.Join(",", accessibleProjects));
        if (documentProjectId.HasValue)
            client.DefaultRequestHeaders.Add("X-Test-DocProject", documentProjectId.Value.ToString());
        return client;
    }

    /// <summary>Workforce (admin) caller for /api/v1/external-access/*.</summary>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

// ================================================================================
// ===== Test doubles =============================================================
// ================================================================================

/// <summary>
/// Authenticates when an <c>Authorization</c> header is present; fails (⇒ 401) otherwise. Emits stable
/// <c>oid</c> + <c>tid</c> claims so the auth filter, audited grant (sprk_grantedby), and cache tenant
/// scoping have deterministic values.
/// </summary>
internal sealed class ExternalTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public ExternalTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));

        var claims = new[]
        {
            new Claim("oid", "00000000-0000-0000-0000-0000000000a1"),
            new Claim("tid", "00000000-0000-0000-0000-0000000000b1"),
            new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-0000000000a1"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

/// <summary>
/// Header-driven <see cref="ExternalParticipationService"/> stub (overrides the <c>virtual</c> module-boundary
/// methods added in task 030). <c>X-Test-Contact</c> = "none" ⇒ unresolvable Contact; <c>X-Test-Projects</c> =
/// comma-separated project GUIDs the caller may access.
/// </summary>
internal sealed class StubExternalParticipationService : ExternalParticipationService
{
    private readonly IHttpContextAccessor _accessor;

    public StubExternalParticipationService(IHttpContextAccessor accessor)
        : base(new HttpClient(), Mock.Of<ITenantCache>(), new ConfigurationBuilder().Build(),
               Mock.Of<TokenCredential>(), accessor, NullLogger<ExternalParticipationService>.Instance)
    {
        _accessor = accessor;
    }

    private string? Header(string name) =>
        _accessor.HttpContext?.Request.Headers.TryGetValue(name, out var v) == true ? v.ToString() : null;

    public override Task<Guid?> ResolveExternalContactAsync(string? oid, string? email, CancellationToken ct = default)
    {
        var contact = Header("X-Test-Contact");
        if (string.Equals(contact, "none", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<Guid?>(null);
        return Task.FromResult<Guid?>(Guid.TryParse(contact, out var id) ? id : Guid.NewGuid());
    }

    public override Task<IReadOnlyList<ExternalParticipation>> GetParticipationsAsync(Guid contactId, CancellationToken ct = default)
    {
        var raw = Header("X-Test-Projects");
        IReadOnlyList<ExternalParticipation> result = string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<ExternalParticipation>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Where(p => Guid.TryParse(p, out _))
                 .Select(p => new ExternalParticipation { ProjectId = Guid.Parse(p), AccessLevel = ExternalAccessLevel.FullAccess })
                 .ToList();
        return Task.FromResult(result);
    }
}

/// <summary>
/// Header-driven <see cref="ExternalDataService"/> stub. <c>X-Test-DocProject</c> = the project GUID the
/// requested document belongs to (absent ⇒ document not found / null project).
/// </summary>
internal sealed class StubExternalDataService : ExternalDataService
{
    private readonly IHttpContextAccessor _accessor;

    public StubExternalDataService(IHttpContextAccessor accessor)
        : base(new HttpClient(), new ConfigurationBuilder().Build(),
               Mock.Of<TokenCredential>(), NullLogger<ExternalDataService>.Instance)
    {
        _accessor = accessor;
    }

    public override Task<(Guid? ProjectId, string? DocumentName)> GetDocumentProjectAndNameAsync(Guid documentId, CancellationToken ct = default)
    {
        var raw = _accessor.HttpContext?.Request.Headers.TryGetValue("X-Test-DocProject", out var v) == true ? v.ToString() : null;
        return Guid.TryParse(raw, out var projectId)
            ? Task.FromResult<(Guid?, string?)>((projectId, "external-doc.bin"))
            : Task.FromResult<(Guid?, string?)>((null, null));
    }
}

/// <summary>
/// <see cref="DataverseWebApiClient"/> double driven off the (additive, backward-compatible) <c>virtual</c>
/// seams. Returns a configured JSON query result, records creates/updates by entity set. No HTTP.
/// </summary>
public sealed class StubDataverseWebApiClient : DataverseWebApiClient
{
    public StubDataverseWebApiClient()
        : base(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["Dataverse:ClientId"] = "x",
            ["Dataverse:ClientSecret"] = "x",
            ["Dataverse:TenantId"] = "x",
        }).Build(), NullLogger<DataverseWebApiClient>.Instance)
    { }

    /// <summary>JSON array returned from the next <see cref="QueryAsync{T}"/> (deserialized to List&lt;T&gt;).</summary>
    public string ContactQueryResult { get; set; } = "[]";
    public List<string> CreatedEntitySets { get; } = new();
    public List<string> ContactUpdates { get; } = new();

    public void Reset()
    {
        ContactQueryResult = "[]";
        CreatedEntitySets.Clear();
        ContactUpdates.Clear();
    }

    public override Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null,
        int? top = null, int? skip = null, CancellationToken cancellationToken = default)
        => Task.FromResult(JsonSerializer.Deserialize<List<T>>(ContactQueryResult) ?? new List<T>());

    public override Task<Guid> CreateAsync(string entitySetName, object entity, CancellationToken cancellationToken = default)
    {
        CreatedEntitySets.Add(entitySetName);
        return Task.FromResult(Guid.NewGuid());
    }

    public override Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken cancellationToken = default)
    {
        if (entitySetName == "contacts") ContactUpdates.Add(id.ToString());
        return Task.CompletedTask;
    }
}
