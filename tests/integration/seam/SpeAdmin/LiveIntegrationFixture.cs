using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.SpeAdmin;

/// <summary>
/// Live-tier fixture for task 041 (sdap-SPE-admin-app-r2, spec FR-D02 / NFR-07). Provisions ONE
/// throwaway SPE container against Spaarke Dev on setup, exposes it, and guarantees it is torn down
/// on disposal — including when a test fails mid-run (xUnit's <see cref="IAsyncLifetime.DisposeAsync"/>
/// runs unconditionally; that guarantee is the entire reason this is fixture-owned teardown and not
/// end-of-test `finally` cleanup, which a failure could skip before reaching it).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why app-only, not delegated, for container lifecycle.</b> Container CRUD (create / list /
/// soft-delete / restore / permanent-delete) works under the owning app's APP-ONLY credential —
/// confirmed live 2026-08-24 (<c>notes/live-verification-credential.md</c> §3: the app-only token
/// carries <c>FileStorageContainer.Selected</c>, enough for containers, the recycle bin, search,
/// security, and audit). That is non-interactive: the client secret comes from Key Vault via
/// <see cref="Azure.Identity.DefaultAzureCredential"/> reaching the vault (picks up the operator's
/// own `az login` locally; a managed identity in Azure), exactly the same production path
/// <c>SpeAdminGraphService.GetClientForConfigAsync</c> uses — this fixture calls that method
/// directly rather than re-implementing it, so the suite exercises production code, not a
/// reimplementation of it.
/// </para>
/// <para>
/// <b>Why NOT delegated OBO for the fixture's own provisioning.</b> The BFF's actual delegated path
/// (task 011) is an OBO exchange keyed off an incoming, already-authenticated
/// <c>HttpContext</c> — which requires a real signed-in user's bearer token as the OBO "assertion".
/// Obtaining one from cold requires an interactive (or device-code) sign-in a human completes in a
/// browser; an automated test runner cannot do that for itself. Container-type OWNER grants (the
/// POML's "role" flow) are the one operation in this suite that is delegated-and-beta-only with no
/// app-only fallback (task 027, <c>notes/task-027-findings.md</c> §2 — confirmed live: 403 app-only
/// on both API versions). <see cref="DelegatedTokenEnvVar"/> lets an operator supply a token obtained
/// out-of-band (e.g. via the existing device-code flow in
/// <c>notes/delegated-diagnostics.py</c>) to exercise that one test manually; absent it, the test is a
/// documented no-op — see <c>ContainerLifecycleLiveTests.ContainerTypeOwnerGrant_RoleFlow_*</c>.
/// </para>
/// <para>
/// <b>Skip-via-return, not <c>[Trait("Category","Live")]</c> CI filtering.</b> The default
/// <c>sdap-ci.yml</c> test job runs <c>dotnet test</c> with no <c>--filter</c> at all (every test
/// project's full suite executes), so a category trait alone would not stop this suite from trying
/// to reach a live tenant in CI. The repo's own established answer to that —
/// <c>tests/integration/Sprk.Bff.Api.IntegrationTests/Membership/Phase2EndToEndTests.cs</c>'s
/// <c>LiveMode_*</c> tests — is an env-var presence check at the top of each test that returns
/// immediately when absent ("Skip-via-return — xUnit's standard pattern ... when SkippableFact isn't
/// referenced"). This fixture and its tests follow the identical convention: <see cref="IsLive"/> is
/// false unless <see cref="EnabledEnvVar"/> is set, so `InitializeAsync` never reaches Key Vault or
/// Graph, and every test in the suite returns as a no-op — a plain `dotnet test` with no tenant
/// credentials configured genuinely executes zero live operations.
/// </para>
/// </remarks>
public sealed class LiveIntegrationFixture : IAsyncLifetime
{
    /// <summary>Opt-in gate. Unset (the default CI baseline) ⇒ <see cref="IsLive"/> is false and
    /// nothing in this suite touches Key Vault, Graph, or the network.</summary>
    public const string EnabledEnvVar = "SPE_LIVE_INTEGRATION_ENABLED";

    /// <summary>Optional delegated bearer token an operator supplies out-of-band (see remarks on the
    /// type) to exercise the one delegated-only ("role" / container-type owner) test.</summary>
    public const string DelegatedTokenEnvVar = "SPE_LIVE_DELEGATED_TOKEN";

    private readonly string _keyVaultUri;
    private readonly string _secretName;
    private readonly string? _delegatedToken;

    /// <summary>Shared across every Graph client this fixture builds (app-only beta, app-only v1,
    /// delegated) — one socket-reusing instance instead of one-per-client, disposed with the
    /// fixture.</summary>
    private readonly HttpClient _httpClient = new();

    public bool IsLive { get; }
    public bool HasDelegatedToken => !string.IsNullOrWhiteSpace(_delegatedToken);

    /// <summary>Spaarke Inc. tenant id. Not a secret — a directory id already published in this
    /// project's own committed notes (e.g. <c>notes/live-verification-2026-08-24.md</c>).</summary>
    public string TenantId { get; }

    /// <summary>SDAP-PCF-CLIENT, the per-customer SPE owning app (ADR-028 exception E-1). Not a
    /// secret — an app registration's client id is a public identifier.</summary>
    public string OwningAppClientId { get; }

    /// <summary>"Spaarke PAYGO 1" — the container type this suite's additive tests run against, and
    /// the parent type for the throwaway container this fixture creates for destructive tests.</summary>
    public string ContainerTypeId { get; }

    /// <summary>The real production service — constructed with a live <see cref="SecretClient"/> and
    /// an unreachable Dataverse credential (this suite never resolves config from Dataverse; it
    /// supplies <see cref="SpeAdminGraphService.ContainerTypeConfig"/> directly). Null until
    /// <see cref="InitializeAsync"/> runs under <see cref="IsLive"/>.</summary>
    public SpeAdminGraphService GraphService { get; private set; } = null!;

    /// <summary>App-only <see cref="GraphServiceClient"/>, authenticated as
    /// <see cref="OwningAppClientId"/> via the secret fetched from Key Vault. Base address is beta —
    /// required for container CRUD (task 020).</summary>
    public GraphServiceClient GraphClient { get; private set; } = null!;

    /// <summary>Same app-only identity as <see cref="GraphClient"/>, based at v1.0 instead of beta.
    /// Some container-TYPE resources (e.g. <c>containerTypeRegistrations</c>, used by the consuming-
    /// app registration flow) live only on v1.0, the mirror image of task 027's finding that
    /// container-type OWNERS live only on beta — see the remarks on <see cref="InitializeAsync"/>.</summary>
    public GraphServiceClient GraphClientV1 { get; private set; } = null!;

    /// <summary>The throwaway container's id, or null when <see cref="IsLive"/> is false. Destructive
    /// tests MUST run every destructive Graph call through
    /// <see cref="ThrowawayContainerGuard.EnsureProvisionedByFixture"/> with this value.</summary>
    public string? ContainerId { get; private set; }

    /// <summary>
    /// Ids of every container that existed under <see cref="ContainerTypeId"/> BEFORE this fixture
    /// created its own throwaway one. Captured so a test can assert, after the destructive lifecycle
    /// runs, that the set is unchanged — an automated proof of NFR-07's "the existing Spaarke Dev
    /// containers hold real working documents" invariant, not just a manual before/after diff.
    /// </summary>
    public IReadOnlyList<string> PreExistingContainerIds { get; private set; } = Array.Empty<string>();

    public LiveIntegrationFixture()
    {
        IsLive = IsTruthy(Environment.GetEnvironmentVariable(EnabledEnvVar));
        _delegatedToken = Environment.GetEnvironmentVariable(DelegatedTokenEnvVar);

        TenantId = Environment.GetEnvironmentVariable("SPE_LIVE_TENANT_ID")
            ?? "a221a95e-6abc-4434-aecc-e48338a1b2f2";
        OwningAppClientId = Environment.GetEnvironmentVariable("SPE_LIVE_OWNING_APP_CLIENT_ID")
            ?? "170c98e1-d486-4355-bcbe-170454e0207c";
        ContainerTypeId = Environment.GetEnvironmentVariable("SPE_LIVE_CONTAINER_TYPE_ID")
            ?? "8a6ce34c-6055-4681-8f87-2f4f9f921c06";
        _keyVaultUri = Environment.GetEnvironmentVariable("SPE_LIVE_KEYVAULT_URI")
            ?? "https://sprk-prod-kv.vault.azure.net/";
        _secretName = Environment.GetEnvironmentVariable("SPE_LIVE_KEYVAULT_SECRET_NAME")
            ?? "spe-owning-app-secret";
    }

    public async Task InitializeAsync()
    {
        if (!IsLive)
        {
            return;
        }

        // AzureCliCredential, not DefaultAzureCredential: on a workstation with no reachable IMDS
        // endpoint, DefaultAzureCredential's ManagedIdentityCredential probe surfaces an
        // AuthenticationFailedException (not the CredentialUnavailableException the chain is built
        // to fall through on) and the whole chain aborts before ever trying Azure CLI — confirmed
        // empirically running this fixture live (see notes/task-041-teardown-proof.md). This mirrors
        // the BFF module's own documented local-dev answer ("az login covers everything except OBO",
        // src/server/api/Sprk.Bff.Api/CLAUDE.md) rather than the production SpeAdminModule credential
        // (ManagedIdentityCredentialFactory, pinned to a UAMI clientId that only exists in Azure) —
        // this is test-only code exercising the operator's own signed-in identity, not a new
        // production credential path.
        var secretClient = new SecretClient(new Uri(_keyVaultUri), new AzureCliCredential());

        GraphService = BuildGraphService(secretClient);

        var config = new SpeAdminGraphService.ContainerTypeConfig(
            ConfigId: Guid.NewGuid(),
            ContainerTypeId: ContainerTypeId,
            ClientId: OwningAppClientId,
            TenantId: TenantId,
            SecretKeyVaultName: _secretName);

        // Exercises the real production Key-Vault-fetch → ClientSecretCredential → GraphServiceClient
        // path (GetClientForConfigAsync) rather than reimplementing it — ADR-038: this is the live
        // tier, it mocks nothing, and that includes not re-deriving the auth plumbing under test.
        GraphClient = await GraphService.GetClientForConfigAsync(config).ConfigureAwait(false);

        GraphClientV1 = await BuildV1GraphClientAsync(secretClient).ConfigureAwait(false);

        await ProvisionThrowawayContainerAsync().ConfigureAwait(false);
    }

    /// <summary>Constructs the real production service with a live Key Vault client and an
    /// intentionally-unreachable Dataverse credential (this suite supplies
    /// <see cref="SpeAdminGraphService.ContainerTypeConfig"/> directly rather than resolving it from
    /// Dataverse, so that dependency exists only to satisfy the constructor — see
    /// <see cref="UnreachableCredential"/>).</summary>
    private SpeAdminGraphService BuildGraphService(SecretClient secretClient)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Satisfies DataverseWebApiClient's ctor requirement only — never actually reached
                // (see UnreachableCredential below, mirroring the existing WireMock contract-test
                // convention in SpeAdminContainerTypeMappingTests.CreateSut()).
                ["Dataverse:ServiceUrl"] = "https://unused.invalid",
            })
            .Build();

        return new SpeAdminGraphService(
            httpClientFactory: new LiveHttpClientFactory(_httpClient),
            secretClient: secretClient,
            dataverseClient: new DataverseWebApiClient(
                configuration, NullLogger<DataverseWebApiClient>.Instance, new UnreachableCredential()),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance,
            tokenProvider: null,
            graphClientFactory: null);
    }

    /// <summary>v1.0-based sibling of <see cref="GraphClient"/>, same app-only credential.
    /// <c>GetClientForConfigAsync</c> always returns a BETA-based client (container CRUD needs beta —
    /// task 020), but <c>containerTypeRegistrations</c> (the resource the consuming-app registration
    /// flow reads/writes) is a v1.0 resource — the mirror image of task 027's finding that container-
    /// type OWNERS live only on beta. Proven live in this task: see
    /// notes/task-041-teardown-proof.md.</summary>
    private async Task<GraphServiceClient> BuildV1GraphClientAsync(SecretClient secretClient)
    {
        var secretValue = await secretClient.GetSecretAsync(_secretName).ConfigureAwait(false);
        var v1Credential = new Azure.Identity.ClientSecretCredential(TenantId, OwningAppClientId, secretValue.Value.Value);
        var v1AuthProvider = new Microsoft.Kiota.Authentication.Azure.AzureIdentityAuthenticationProvider(
            v1Credential, scopes: new[] { "https://graph.microsoft.com/.default" });
        return new GraphServiceClient(_httpClient, v1AuthProvider, "https://graph.microsoft.com/v1.0");
    }

    private async Task ProvisionThrowawayContainerAsync()
    {
        var existing = await GraphService.ListContainersAsync(GraphClient, ContainerTypeId).ConfigureAwait(false);
        PreExistingContainerIds = existing.Select(c => c.Id).ToList();

        var created = await GraphService.CreateContainerAsync(
            GraphClient,
            ContainerTypeId,
            displayName: $"sdap-r2-live-test-{Guid.NewGuid():N}",
            description: "Throwaway container for the sdap-SPE-admin-app-r2 task 041 LiveIntegration " +
                "suite. Created and torn down automatically by LiveIntegrationFixture. Safe to delete " +
                "manually if this is ever found orphaned (indicates a teardown failure).")
            .ConfigureAwait(false);

        ContainerId = created.Id;
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(ContainerId))
        {
            // IsLive was false (or provisioning never reached ContainerId) — _httpClient was
            // constructed in the field initializer either way, so it still needs disposing even
            // though nothing else in this fixture ran.
            _httpClient.Dispose();
            return;
        }

        // Teardown MUST complete regardless of what state a test left the container in (still
        // active, or already recycled) and MUST NOT itself throw — by the time Dispose runs, xUnit
        // has already recorded the test outcome, and a throwing Dispose would only obscure it. Each
        // step is independently guarded so a failure in one does not skip the other. Proven live by
        // forcing a test failure between the two steps (Step 2 HARD STOP) — see
        // notes/task-041-teardown-proof.md for the transcript.
        try
        {
            await GraphService.SoftDeleteContainerAsync(GraphClient, ContainerId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogTeardownFailure("soft-delete", ex);
        }

        try
        {
            await GraphService.PermanentDeleteContainerAsync(GraphClient, ContainerId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogTeardownFailure("permanent-delete", ex);
        }

        _httpClient.Dispose();
    }

    /// <summary>
    /// Builds a delegated <see cref="GraphServiceClient"/> from <see cref="DelegatedTokenEnvVar"/>,
    /// pointed at beta (container-type owners do not exist on v1.0 — task 027). Mirrors
    /// <c>SpeAdminGraphService.StaticBearerTokenProvider</c> (private to that class, so replicated
    /// here rather than exposed) — used only by the one test that needs a delegated token.
    /// </summary>
    public GraphServiceClient BuildDelegatedGraphClient()
    {
        if (!HasDelegatedToken)
        {
            throw new InvalidOperationException(
                $"{DelegatedTokenEnvVar} is not set. Callers must check {nameof(HasDelegatedToken)} first.");
        }

        var authProvider = new Microsoft.Kiota.Abstractions.Authentication.BaseBearerTokenAuthenticationProvider(
            new StaticTokenProvider(_delegatedToken!));

        return new GraphServiceClient(_httpClient, authProvider, "https://graph.microsoft.com/beta");
    }

    private static bool IsTruthy(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");

    private static void LogTeardownFailure(string step, Exception ex) =>
        Console.Error.WriteLine(
            $"[LiveIntegrationFixture] teardown step '{step}' threw — the other step still ran " +
            $"independently. {ex.GetType().Name}: {ex.Message}");

    /// <summary>Returns the fixture's single shared <see cref="HttpClient"/> for any requested name.
    /// Unlike production's named "GraphApiClient" (a distinct DI-registered client carrying resilience
    /// handlers), a live test run does not need retry/circuit-breaker middleware —
    /// <c>SpeAdminGraphService</c>'s own <c>ExecuteWithRetryAsync</c> already covers 429 throttling —
    /// so one connection-reusing instance for the whole fixture is enough.</summary>
    private sealed class LiveHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public LiveHttpClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    /// <summary>Throws if ever asked for a token — proves, by construction, that this suite never
    /// reaches Dataverse. Mirrors the identical <c>UnusableCredential</c> pattern already established
    /// in <c>SpeAdminContainerTypeMappingTests.CreateSut()</c> and its WireMock-fixture siblings.</summary>
    private sealed class UnreachableCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "Dataverse must not be reached from the LiveIntegration suite — it supplies " +
                "ContainerTypeConfig directly rather than resolving it from Dataverse.");

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "Dataverse must not be reached from the LiveIntegration suite — it supplies " +
                "ContainerTypeConfig directly rather than resolving it from Dataverse.");
    }

    private sealed class StaticTokenProvider : Microsoft.Kiota.Abstractions.Authentication.IAccessTokenProvider
    {
        private readonly string _token;

        public StaticTokenProvider(string token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            _token = token;
        }

        public Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_token);

        public Microsoft.Kiota.Abstractions.Authentication.AllowedHostsValidator AllowedHostsValidator { get; }
            = new(new[] { "graph.microsoft.com" });
    }
}

/// <summary>
/// Refuses to let a destructive SPE operation proceed against any container id this suite did not
/// itself provision (NFR-07, task 041 Step 3 HARD STOP — proven before any destructive Graph call was
/// wired to it: see <c>notes/task-041-teardown-proof.md</c>).
/// </summary>
public static class ThrowawayContainerGuard
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when <paramref name="requestedContainerId"/>
    /// does not match <paramref name="fixtureProvisionedContainerId"/>. Every destructive helper in
    /// <c>ContainerLifecycleLiveTests</c> calls this BEFORE issuing the corresponding Graph request —
    /// the refusal is structural (the Graph call is never reached), not just an assertion after the
    /// fact.
    /// </summary>
    public static void EnsureProvisionedByFixture(string requestedContainerId, string fixtureProvisionedContainerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedContainerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureProvisionedContainerId);

        if (!string.Equals(requestedContainerId, fixtureProvisionedContainerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing a destructive SPE operation against container '{requestedContainerId}': it was " +
                $"not provisioned by this test run (the fixture's own throwaway container is " +
                $"'{fixtureProvisionedContainerId}'). NFR-07: a destructive test may only ever target a " +
                "container this suite created for itself.");
        }
    }
}
