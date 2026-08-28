// CONTROLS for the test-host outbound-network guard (TestOutboundNetworkGuard.cs).
//
// tests/CLAUDE.md's authoring rules for guard-shaped tests apply here, and they are the reason this
// file exists at all:
//   - "Every rule carries a NEGATIVE control proving the detector fires on a seeded violation —
//      a detector nobody has seen fail is a detector nobody knows works."
//   - "Every rule carries a POSITIVE control proving it does NOT fire on the sanctioned shape —
//      a guard that flags the code it protects gets deleted rather than obeyed."
// The sanctioned shape here is a LOOPBACK test double (WireMock.Net, which every fixture is free to
// use). If the guard blocked that too, the correct response would be to delete the guard.
//
// These are controls over test INFRASTRUCTURE, not over product behaviour, so they deliberately sit
// beside the guard rather than under one of the seven ADR-038 KEEP paths — the same reasoning
// tests/CLAUDE.md records for tests/Spaarke.ArchTests/**: the RULE is the subject, and there is no
// "method under test" to name in a {Method}_{Scenario}_{ExpectedResult} triple.

using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

public sealed class TestOutboundNetworkGuardTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public TestOutboundNetworkGuardTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    // ── LAYER 2 · NEGATIVE CONTROL ───────────────────────────────────────────────────────────────
    // Seeds the violation the guard exists to catch: an IHttpClientFactory client in a test host
    // reaching a real, wildcard-resolving Azure hostname. Also proves the whole delivery mechanism
    // works end to end — [ModuleInitializer] → ASPNETCORE_HOSTINGSTARTUPASSEMBLIES → [HostingStartup]
    // → IHttpMessageHandlerBuilderFilter — inside a fixture that contains no guard wiring of its own.
    [Fact]
    public async Task OutboundHttpToNonLoopbackHost_FromTestHostHttpClientFactory_IsBlockedWithNamedException()
    {
        var httpClientFactory = _factory.Services.GetRequiredService<IHttpClientFactory>();
        using var client = httpClientFactory.CreateClient("guard-negative-control");

        var act = () => client.GetAsync("https://test.crm.dynamics.com/api/data/v9.2/WhoAmI");

        var blocked = (await act.Should().ThrowAsync<TestOutboundNetworkBlockedException>(
            "a test host must not be able to open a real connection to Azure — that is what turned a " +
            "stray call into an opaque ~100 s timeout instead of an immediate, named failure"))
            .Which;

        blocked.RequestUri.Host.Should().Be("test.crm.dynamics.com");
        blocked.HttpClientName.Should().Be("guard-negative-control",
            "the message must name the client that escaped, or it does not shorten the diagnosis");
        blocked.Message.Should().Contain("BLOCKED outbound HTTP");
    }

    // ── LAYER 2 · POSITIVE CONTROL ───────────────────────────────────────────────────────────────
    // The sanctioned shape: a loopback destination, i.e. where legitimate doubles live
    // (WireMockServer.Start() binds http://localhost:{port}). Port 1 is used because nothing listens
    // there, so the request fails for the ORDINARY reason (connection refused) — the assertion is
    // that the failure did NOT come from this guard.
    [Fact]
    public async Task OutboundHttpToLoopback_FromTestHostHttpClientFactory_IsNotBlockedByTheGuard()
    {
        var httpClientFactory = _factory.Services.GetRequiredService<IHttpClientFactory>();
        using var client = httpClientFactory.CreateClient("guard-positive-control");

        var act = () => client.GetAsync("http://localhost:1/wiremock-stands-in-here");

        var thrown = (await act.Should().ThrowAsync<HttpRequestException>(
            "nothing is listening on port 1, so an ordinary connection failure is expected"))
            .Which;

        thrown.Should().NotBeOfType<TestOutboundNetworkBlockedException>(
            "loopback is where WireMock.Net and every other in-process double lives; a guard that " +
            "blocked it would break legitimately-doubled paths and would rightly be deleted");
    }

    // ── LAYER 1 · CONTROL ────────────────────────────────────────────────────────────────────────
    // The determinism fix. The test host's TokenCredential is the real DefaultAzureCredential from
    // Program.cs; the guard restricts its chain to the one leg that resolves entirely offline.
    // The developer legs named below are the ones that cost ~6.0 s per attempt on a machine with az
    // CLI + Az.Accounts + the Visual Studio identity cache present, and ~0 ms on a CI runner where
    // they are absent. That asymmetry — not any product bug — is what made local runs disagree with
    // CI and made the failing set move between runs.
    [Fact]
    public async Task TestHostTokenCredential_ChainExcludesDeveloperCredentials_AndFailsOffline()
    {
        Environment.GetEnvironmentVariable(TestOutboundNetworkGuard.AzureTokenCredentialsEnvVar)
            .Should().Be(TestOutboundNetworkGuard.OfflineCredentialLeg,
                "the module initializer must have restricted the chain before any host was built");

        var credential = _factory.Services.GetRequiredService<TokenCredential>();

        var act = () => credential.GetTokenAsync(
            new TokenRequestContext(new[] { "https://test.documents.azure.com/.default" }),
            CancellationToken.None).AsTask();

        var thrown = (await act.Should().ThrowAsync<CredentialUnavailableException>(
            "with no AZURE_CLIENT_* variables set, the single permitted leg reports unavailable " +
            "without touching the network"))
            .Which;

        thrown.Message.Should().Contain("EnvironmentCredential");
        foreach (var developerLeg in new[]
                 {
                     "AzureCliCredential",
                     "AzurePowerShellCredential",
                     "VisualStudioCredential",
                     "VisualStudioCodeCredential",
                 })
        {
            thrown.Message.Should().NotContain(developerLeg,
                $"{developerLeg} spawns a process to probe for a developer login; it is present on a " +
                "developer machine and absent in CI, which is precisely the local-vs-CI divergence");
        }
    }
}
