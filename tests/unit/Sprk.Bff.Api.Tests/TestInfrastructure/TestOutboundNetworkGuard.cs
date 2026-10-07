// TEST-INFRASTRUCTURE GUARD — makes a test host's stray outbound network access fail FAST and
// LOUDLY instead of slowly and silently.
//
// ─────────────────────────────────────────────────────────────────────────────────────────────────
// THE INCIDENT THIS FILE EXISTS FOR (2026-08-28)
// ─────────────────────────────────────────────────────────────────────────────────────────────────
// `dotnet test tests/unit/Sprk.Bff.Api.Tests/` produced ~5 failures locally that did NOT occur in
// CI on the identical commit, and the FAILING SET MOVED between runs. Every failure took ~100
// seconds and died with TaskCanceledException / "The client aborted the request" against an
// in-memory WebApplicationFactory client — 100s is the DEFAULT HttpClient.Timeout, i.e. a timeout
// signature, not an assertion failure. A local red therefore carried zero information: you could
// not use it to decide whether your change had broken anything. That — not any individual test —
// was the defect.
//
// MEASURED ROOT CAUSE (not inferred):
//   1. Program.cs registers ONE singleton `Azure.Core.TokenCredential`, built by
//      `ManagedIdentityCredentialFactory.Create()` as a real `DefaultAzureCredential`. No fixture
//      replaces it, so it is live inside every WebApplicationFactory host in this assembly.
//   2. On THIS developer machine the whole DefaultAzureCredential chain is populated: az CLI is
//      installed AND logged in, pwsh has Az.Accounts, and the Visual Studio IdentityService token
//      cache exists. Measured cost of one failed token acquisition: **~6.0 seconds**, and the
//      failure is NOT cached — every subsequent attempt re-pays it in full.
//   3. On a GitHub Actions runner none of those developer credentials are usable, so each leg of
//      the chain reports "unavailable" in ~0 ms and the same code path fails in well under a
//      second.
//   That asymmetry is the entire local-vs-CI divergence. Any Azure SDK retry loop (Cosmos, Search,
//   Key Vault, Dataverse) multiplies the local 6 s by its retry count and crosses the 100 s client
//   timeout; which test crosses it first depends on machine load and process-spawn contention,
//   which is exactly why the failing set moved between runs.
//
// ─────────────────────────────────────────────────────────────────────────────────────────────────
// WHAT THIS FILE DOES — two layers, both zero-touch for existing fixtures
// ─────────────────────────────────────────────────────────────────────────────────────────────────
// LAYER 1 — Credential chain restriction (the determinism fix).
//   A [ModuleInitializer] sets `AZURE_TOKEN_CREDENTIALS=EnvironmentCredential` for the test
//   process. Azure.Identity (1.14+) honours this by restricting the DefaultAzureCredential chain
//   to that single leg. Measured on this machine: 6.00 s → 0.00 s per attempt, throwing the SAME
//   `CredentialUnavailableException` type it threw before. This does not fake anything and does
//   not change any outcome — it removes the local-only developer-credential probing that CI never
//   pays, so a local host now behaves like a CI host. It also guarantees the credential path makes
//   no network call at all (EnvironmentCredential is unavailable without AZURE_CLIENT_* env vars,
//   and reports so offline).
//
// LAYER 2 — Hard block on outbound HTTP (the diagnostic instrument).
//   An `IHttpMessageHandlerBuilderFilter` installs a guard at the PRIMARY-HANDLER position of
//   every `IHttpClientFactory`-created client in the test host. A request to a non-loopback host
//   throws `TestOutboundNetworkBlockedException` immediately, naming the method, the URI and the
//   logical HttpClient name. A 100-second mystery becomes an instant, self-explaining failure that
//   says which call escaped. Reaches every fixture in this assembly with no fixture edits, via
//   ASP.NET Core's own `IHostingStartup` mechanism (see the [ModuleInitializer] below).
//
// WHY THIS IS NOT BANNED ANTIPATTERN B1 (`Mock<HttpMessageHandler>`), per tests/CLAUDE.md:
//   B1 bans mocking the TRANSPORT of a class under test in order to assert on wire format — it
//   encodes request/response bytes into a unit test and breaks on refactors. This is the opposite
//   construct and serves the opposite purpose: it is a DelegatingHandler registered in the test
//   HOST's DI that asserts NOTHING, stubs NOTHING, and returns NO canned response. It only refuses
//   to leave the machine. Nothing here can be asserted against, so nothing here can encode a wire
//   format. It is a hermeticity boundary, in the same family as `WebApplicationFactory` itself.
//
// COVERAGE BOUNDARY — be honest about what Layer 2 does NOT reach:
//   Layer 2 only covers clients built by `IHttpClientFactory`. It does NOT intercept SDKs that own
//   their transport — notably `CosmosClient` (registered `WithConnectionModeDirect()`, so its data
//   path is raw TCP, not HttpClient), the Azure.* SDK clients, or any `new HttpClient()` in
//   product code. Those are covered by Layer 1 instead: they must acquire a token before they can
//   talk to anything, and Layer 1 makes that fail offline in ~0 ms.
//
// ESCAPE HATCH:
//   Set `SPAARKE_TESTS_ALLOW_OUTBOUND=1` to disable Layer 2 for a deliberate live-endpoint run.
//   Set `AZURE_TOKEN_CREDENTIALS` yourself (to anything) to override Layer 1 — an explicit value
//   already present in the environment is never overwritten.

using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

[assembly: HostingStartup(typeof(Sprk.Bff.Api.Tests.TestInfrastructure.TestOutboundNetworkGuardStartup))]

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// Central switches + policy for the test-host outbound-network guard. See the file header for the
/// incident, the measured root cause, and the two layers.
/// </summary>
public static class TestOutboundNetworkGuard
{
    /// <summary>Env var that disables Layer 2 (the hard outbound-HTTP block) for a deliberate live run.</summary>
    public const string AllowOutboundEnvVar = "SPAARKE_TESTS_ALLOW_OUTBOUND";

    /// <summary>Azure.Identity 1.14+ chain-restriction switch. Layer 1.</summary>
    public const string AzureTokenCredentialsEnvVar = "AZURE_TOKEN_CREDENTIALS";

    /// <summary>
    /// The single credential leg the test process is allowed to use. Chosen because it is the only
    /// leg that reports "unavailable" entirely offline and in ~0 ms — no IMDS probe (1 s), no
    /// process spawn (az / pwsh / VS, ~6 s combined on a developer machine).
    /// </summary>
    public const string OfflineCredentialLeg = "EnvironmentCredential";

    /// <summary>True when Layer 2 has been switched off via <see cref="AllowOutboundEnvVar"/>.</summary>
    public static bool OutboundExplicitlyAllowed
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(AllowOutboundEnvVar);
            return string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Loopback is ALWAYS allowed: it is where legitimate test doubles live. `WireMock.Net` servers
    /// (<c>WireMockServer.Start()</c> → <c>http://localhost:{port}</c>) and any locally-hosted stub
    /// must keep working, so scoping the block by destination rather than abandoning it is what
    /// keeps genuinely-doubled paths intact.
    /// </summary>
    public static bool IsAllowedDestination(Uri? requestUri)
    {
        // A null URI cannot be judged; let the platform raise its own (immediate) error rather than
        // attributing a failure to this guard.
        if (requestUri is null)
        {
            return true;
        }

        return requestUri.IsLoopback;
    }
}

/// <summary>
/// Thrown when a test host attempts real outbound HTTP. Derives from <see cref="HttpRequestException"/>
/// ON PURPOSE: every <c>catch (HttpRequestException)</c> and every Polly transient-error predicate in
/// production code continues to behave exactly as it does against a genuine network failure, so the
/// guard changes the SPEED and the MESSAGE of a failure, never its type contract. The distinct type
/// name is what makes it greppable and unmistakable in a failure report.
/// </summary>
public sealed class TestOutboundNetworkBlockedException : HttpRequestException
{
    public TestOutboundNetworkBlockedException(string httpClientName, HttpMethod method, Uri requestUri)
        : base(BuildMessage(httpClientName, method, requestUri))
    {
        HttpClientName = httpClientName;
        RequestUri = requestUri;
    }

    /// <summary>The logical <c>IHttpClientFactory</c> name of the client that tried to leave the machine.</summary>
    public string HttpClientName { get; }

    /// <summary>The destination that was blocked.</summary>
    public Uri RequestUri { get; }

    private static string BuildMessage(string httpClientName, HttpMethod method, Uri requestUri) =>
        $"BLOCKED outbound HTTP from a test host: {method} {requestUri} " +
        $"(IHttpClientFactory client name: '{httpClientName}'). " +
        "Tests in this assembly run against an in-memory WebApplicationFactory and MUST NOT reach the " +
        "network — the fake test hostnames in fixture config (test.crm.dynamics.com, " +
        "test.documents.azure.com, test.search.windows.net, test.openai.azure.com, test.vault.azure.net) " +
        "all RESOLVE to live Microsoft Azure IPs via wildcard DNS and answer TCP, so an escaped call " +
        "becomes a real connection whose latency differs between a developer machine and CI. " +
        "The frames ABOVE this one name the production code path that escaped. " +
        "FIX: double that boundary in the fixture's ConfigureTestServices (RemoveAll<T>() + a Mock<T>), " +
        "or, if the call is genuinely wrong on this code path, stop the production code from making it. " +
        $"To run deliberately against live endpoints, set {TestOutboundNetworkGuard.AllowOutboundEnvVar}=1. " +
        "See tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/TestOutboundNetworkGuard.cs.";
}

/// <summary>
/// Applies Layer 1 (credential-chain restriction) and arms Layer 2 (outbound-HTTP block) for the
/// whole test process, before any test or fixture runs.
/// </summary>
internal static class TestOutboundNetworkGuardModuleInitializer
{
    /// <summary>
    /// Runs once, when this assembly is first touched — i.e. before any fixture builds a host.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        // ── Layer 1 ──────────────────────────────────────────────────────────────────────────────
        // Restrict DefaultAzureCredential to its one offline leg. Never clobber an explicit value:
        // an operator who set this deliberately (e.g. to run something against a live resource)
        // keeps their choice.
        if (string.IsNullOrEmpty(
                Environment.GetEnvironmentVariable(TestOutboundNetworkGuard.AzureTokenCredentialsEnvVar)))
        {
            Environment.SetEnvironmentVariable(
                TestOutboundNetworkGuard.AzureTokenCredentialsEnvVar,
                TestOutboundNetworkGuard.OfflineCredentialLeg);
        }

        // ── Layer 2 ──────────────────────────────────────────────────────────────────────────────
        // Arm the outbound-HTTP block for EVERY WebApplicationFactory host in this assembly without
        // editing ~60 fixtures, using ASP.NET Core's own extension point for exactly this ("add
        // services to a host from outside without modifying it"). The [assembly: HostingStartup]
        // attribute at the top of this file names the IHostingStartup; this env var is what makes
        // the host load it.
        const string HostingStartupEnvVar = "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES";
        var assemblyName = typeof(TestOutboundNetworkGuardModuleInitializer).Assembly.GetName().Name!;
        var existing = Environment.GetEnvironmentVariable(HostingStartupEnvVar);

        if (string.IsNullOrEmpty(existing))
        {
            Environment.SetEnvironmentVariable(HostingStartupEnvVar, assemblyName);
        }
        else if (!existing.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .Contains(assemblyName, StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable(HostingStartupEnvVar, $"{existing};{assemblyName}");
        }
    }
}

/// <summary>
/// Hosting startup that registers the Layer 2 handler filter into every test host. Public +
/// parameterless-constructible because ASP.NET Core activates it by reflection.
/// </summary>
public sealed class TestOutboundNetworkGuardStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        // Registering an IHttpMessageHandlerBuilderFilter is ADDITIVE — all registered filters run —
        // so it does not matter that a hosting startup's ConfigureServices runs BEFORE Program.cs's
        // registrations. (That ordering is precisely why Layer 1 is an env var rather than a DI
        // override of TokenCredential: a last-registration-wins singleton registered here would be
        // shadowed by Program.cs's own AddSingleton<TokenCredential>.)
        builder.ConfigureServices(services =>
            services.AddSingleton<IHttpMessageHandlerBuilderFilter, OutboundHttpGuardFilter>());
    }
}

/// <summary>
/// Installs <see cref="OutboundHttpGuardHandler"/> at the primary-handler position of every
/// <c>IHttpClientFactory</c> client in the host.
/// </summary>
internal sealed class OutboundHttpGuardFilter : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
        builder =>
        {
            // Let every other filter (including the framework's logging filter) build first, then
            // take the primary-handler slot. Wrapping rather than replacing keeps the real socket
            // handler available for allowed (loopback) destinations.
            next(builder);

            if (TestOutboundNetworkGuard.OutboundExplicitlyAllowed)
            {
                return;
            }

            builder.PrimaryHandler = new OutboundHttpGuardHandler(
                builder.Name ?? "(unnamed)",
                builder.PrimaryHandler);
        };
}

/// <summary>
/// Refuses non-loopback requests immediately. Asserts nothing and stubs nothing — see the file
/// header for why this is not banned antipattern B1.
/// </summary>
internal sealed class OutboundHttpGuardHandler : DelegatingHandler
{
    private readonly string _httpClientName;

    internal OutboundHttpGuardHandler(string httpClientName, HttpMessageHandler innerHandler)
    {
        _httpClientName = httpClientName;
        InnerHandler = innerHandler;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!TestOutboundNetworkGuard.IsAllowedDestination(request.RequestUri))
        {
            throw new TestOutboundNetworkBlockedException(
                _httpClientName, request.Method, request.RequestUri!);
        }

        return base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!TestOutboundNetworkGuard.IsAllowedDestination(request.RequestUri))
        {
            throw new TestOutboundNetworkBlockedException(
                _httpClientName, request.Method, request.RequestUri!);
        }

        return base.Send(request, cancellationToken);
    }
}
