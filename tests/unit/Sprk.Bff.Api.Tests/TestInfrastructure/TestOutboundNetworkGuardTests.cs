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
    //
    // ⚠️ REWRITTEN 2026-08-30, and the reason is worth more than the test.
    //
    // This assertion used to require the host's TokenCredential to be the REAL DefaultAzureCredential
    // with its chain narrowed to one offline leg, and to prove it by catching a
    // CredentialUnavailableException naming EnvironmentCredential. When master merged in, it failed —
    // not as a regression, but because master had independently fixed the SAME root cause, better.
    //
    // Master's `TestTokenCredential.UseStubTokenCredential()` REPLACES the credential in every test
    // host's DI with a stub that answers instantly and never touches the network, and
    // `Spaarke.ArchTests/TestHostCredentialGuardTests` fails the build if any
    // WebApplicationFactory<Program> subclass forgets to call it. Its docstring reaches the identical
    // diagnosis this guard did — the ~100 s HttpClient timeout, the failing set that rotates between
    // runs, the test that passes in the suite and fails alone — from a different starting point.
    // Substituting the credential is strictly stronger than narrowing the real one's chain, and it is
    // enforced structurally rather than by one runtime assertion.
    //
    // So this test now defers to master's mechanism instead of forking it, and asserts only what it
    // still uniquely owns: that the module initializer ran (layer 1 remains defence-in-depth for any
    // credential constructed OUTSIDE a fixture's DI, which the stub cannot reach), and that resolving
    // a token in a test host is instant and offline — which is the property that actually mattered all
    // along. The specific exception type never did; it was evidence for the property, and evidence
    // goes stale when the mechanism improves.
    [Fact]
    public async Task TestHostTokenCredential_ResolvesOfflineAndInstantly()
    {
        Environment.GetEnvironmentVariable(TestOutboundNetworkGuard.AzureTokenCredentialsEnvVar)
            .Should().Be(TestOutboundNetworkGuard.OfflineCredentialLeg,
                "the module initializer must still restrict the chain before any host is built — it is "
                + "the only defence for a credential constructed outside a fixture's DI, which master's "
                + "DI-level stub by construction cannot reach");

        var credential = _factory.Services.GetRequiredService<TokenCredential>();

        // The property under test is "offline", and a wall-clock bound is how you observe it: a real
        // probe chain on a developer machine costs ~6 s per developer leg, and a network-reaching leg
        // costs up to HttpClient's 100 s default. Anything resolving in well under a second cannot have
        // touched either. (tests/CLAUDE.md bans Stopwatch for TIME-DEPENDENT LOGIC, where FakeTimeProvider
        // is the fix; here the elapsed time IS the observation, and no fake can substitute for it.)
        var started = System.Diagnostics.Stopwatch.StartNew();

        var act = () => credential.GetTokenAsync(
            new TokenRequestContext(new[] { "https://test.documents.azure.com/.default" }),
            CancellationToken.None).AsTask();

        // Either outcome is correct and both prove the point: master's stub RETURNS a token instantly,
        // and a chain-restricted real credential THROWS CredentialUnavailable instantly. What must never
        // happen is a slow answer, because slow means a probe chain or the network.
        try
        {
            await act();
        }
        catch (CredentialUnavailableException)
        {
            // Fine — the offline leg reported unavailable without leaving the process.
        }

        started.Stop();
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "a test host must never pay for a credential probe. ~6 s means a developer leg (az CLI, "
            + "Az.Accounts, the VS identity cache) is being probed — present locally, absent in CI, and "
            + "the exact asymmetry that made local runs disagree with CI. ~100 s means HttpClient's "
            + "default timeout, i.e. the host reached the network");
    }
}
