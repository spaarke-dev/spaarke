// -----------------------------------------------------------------------------
// KeyVaultCertBootstrapProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for KeyVaultCertBootstrapProbe (task 120,
// Wave G-2). Same two-layer approach as the two ARM probe test files:
//   1. Evaluate() boundary-case tests — not-found / missing-CreatedOn / under
//      / at / over the 24h replication threshold (FR-11 T6).
//   2. CheckAsync() end-to-end tests against a REAL SecretClient built with a
//      fake Azure.Core.Pipeline.HttpClientTransport (same philosophy as
//      ArmSdkTestFakes, applied to the KV SDK instead of ARM) so the SDK's
//      real request/response marshaling runs unmodified.
//
// ADR-038 alignment: pure C# unit tests. No live Azure, no
// Mock&lt;HttpMessageHandler&gt;, no SDK-client wrapper mock.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Sprk.Provisioning.ControlPlane.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class KeyVaultCertBootstrapProbeTests
{
    private const string KeyVaultName = "spaarke-platform-kv";
    private const string CertSecretName = "SPE-OwnerCert-Pfx";
    private const string ContainerTypeId = "cccccccc-dddd-eeee-ffff-000000000001";

    // Task 245b: the certificate checked is the run's container type's owning-app certificate, from
    // SpeContainerOptions.ContainerTypeOwners — the same entry H8 and the T6 probe use.
    private static IOptions<SpeContainerOptions> OwnerOptions() => Options.Create(new SpeContainerOptions
    {
        ContainerTypeOwners =
        [
            new SpeContainerTypeOwner
            {
                ContainerTypeId = ContainerTypeId,
                OwnerAppId = "77777777-8888-9999-aaaa-bbbbbbbbbbbb",
                OwnerCertKeyVaultName = KeyVaultName,
                OwnerCertSecretName = CertSecretName,
            },
        ],
    });

    private static PreflightProbeInput InputFor(string? containerTypeId) => new(
        "acme", "tenant-1",
        containerTypeId is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [IntakeParameterCatalog.ContainerTypeId] = containerTypeId });
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    // ---------- Evaluate() boundary cases ----------

    [Fact]
    public void Evaluate_SecretNotFound_FailsWithMissingDiagnostic()
    {
        var result = KeyVaultCertBootstrapProbe.Evaluate(
            KeyVaultName, CertSecretName, "https://x/secrets/y", minAgeHours: 24, Now,
            new SecretCreatedOnResult.NotFound());

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("MISSING");
    }

    [Fact]
    public void Evaluate_MissingCreatedOnMetadata_FailsAsShapeDrift()
    {
        var result = KeyVaultCertBootstrapProbe.Evaluate(
            KeyVaultName, CertSecretName, "https://x/secrets/y", minAgeHours: 24, Now,
            new SecretCreatedOnResult.MissingCreatedOn());

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("Escalate per root CLAUDE.md §6");
    }

    [Fact]
    public void Evaluate_UnderMinAge_FailsWithWaitDiagnostic()
    {
        // Created 10h ago; needs 24h.
        var result = KeyVaultCertBootstrapProbe.Evaluate(
            KeyVaultName, CertSecretName, "https://x/secrets/y", minAgeHours: 24, Now,
            new SecretCreatedOnResult.Found(Now.AddHours(-10)));

        result.Passed.Should().BeFalse("10h < 24h replication requirement");
        result.Diagnostic.Should().Contain("NOT YET REPLICATED");
        result.Diagnostic.Should().Contain("14h more");
    }

    [Fact]
    public void Evaluate_ExactlyAtMinAge_Passes()
    {
        var result = KeyVaultCertBootstrapProbe.Evaluate(
            KeyVaultName, CertSecretName, "https://x/secrets/y", minAgeHours: 24, Now,
            new SecretCreatedOnResult.Found(Now.AddHours(-24)));

        result.Passed.Should().BeTrue("24h == 24h requirement is within threshold (>=)");
    }

    [Fact]
    public void Evaluate_OverMinAge_Passes()
    {
        var result = KeyVaultCertBootstrapProbe.Evaluate(
            KeyVaultName, CertSecretName, "https://x/secrets/y", minAgeHours: 24, Now,
            new SecretCreatedOnResult.Found(Now.AddHours(-48)));

        result.Passed.Should().BeTrue();
        result.Diagnostic.Should().Contain("OK");
    }

    // ---------- CheckAsync() end-to-end via real SecretClient + fake transport ----------

    [Fact]
    public async Task CheckAsync_SecretFoundAndOldEnough_ReturnsPassedViaGenuineKvCall()
    {
        var handler = new FakeArmHttpMessageHandler(request =>
        {
            request.RequestUri!.AbsolutePath.Should().Be($"/secrets/{CertSecretName}/",
                "the probe must call the REAL KV secret-show endpoint, not a hard-coded stub");
            var createdUnix = Now.AddHours(-48).ToUnixTimeSeconds();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{ "value": "hidden", "id": "https://{{KeyVaultName}}.vault.azure.net/secrets/{{CertSecretName}}/v1", "attributes": { "enabled": true, "created": {{createdUnix}} } }""",
                    Encoding.UTF8, "application/json"),
            };
        });
        var timeProvider = new TestTimeProvider(Now);
        var probe = new KeyVaultCertBootstrapProbe(
            new FakeCredential(),
            new SecretClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) },
            OwnerOptions(),
            NullLogger<KeyVaultCertBootstrapProbe>.Instance,
            timeProvider);
        var input = InputFor(ContainerTypeId);

        var result = await probe.CheckAsync(input, CancellationToken.None);

        result.Passed.Should().BeTrue();
        result.CheckName.Should().Be(PreflightCheckNames.SpeCertBootstrap);
        handler.RequestedUris.Should().ContainSingle();
    }

    [Fact]
    public async Task CheckAsync_SecretNotFound_ReturnsFailedViaGenuine404()
    {
        var handler = new FakeArmHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{ "error": { "code": "SecretNotFound", "message": "not found" } }""", Encoding.UTF8, "application/json"),
        });
        var probe = new KeyVaultCertBootstrapProbe(
            new FakeCredential(),
            new SecretClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) },
            OwnerOptions(),
            NullLogger<KeyVaultCertBootstrapProbe>.Instance,
            new TestTimeProvider(Now));
        var input = InputFor(ContainerTypeId);

        var result = await probe.CheckAsync(input, CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("MISSING");
    }

    [Fact]
    public async Task CheckAsync_MissingContainerTypeIdParameter_ReturnsConfigErrorWithoutCallingKv()
    {
        var handler = new FakeArmHttpMessageHandler(_ => throw new InvalidOperationException("must not call KV"));
        var probe = new KeyVaultCertBootstrapProbe(
            new FakeCredential(),
            new SecretClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) },
            OwnerOptions(),
            NullLogger<KeyVaultCertBootstrapProbe>.Instance,
            new TestTimeProvider(Now));
        var input = InputFor(null);

        var result = await probe.CheckAsync(input, CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("'containerTypeId'");
        handler.RequestedUris.Should().BeEmpty("config error must short-circuit before any KV call");
    }

    [Fact]
    public async Task CheckAsync_NoOwnerConfiguredForTheContainerType_FailsWithoutCallingKv()
    {
        var handler = new FakeArmHttpMessageHandler(_ => throw new InvalidOperationException("must not call KV"));
        var probe = new KeyVaultCertBootstrapProbe(
            new FakeCredential(),
            new SecretClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) },
            OwnerOptions(),
            NullLogger<KeyVaultCertBootstrapProbe>.Instance,
            new TestTimeProvider(Now));

        var result = await probe.CheckAsync(InputFor("dddddddd-0000-0000-0000-000000000009"), CancellationToken.None);

        result.Passed.Should().BeFalse("H0 must stop a run whose container type has no owning-app credential, before anything is created");
        result.Diagnostic.Should().Contain("ContainerTypeOwners");
        handler.RequestedUris.Should().BeEmpty();
    }

    /// <summary>Minimal fake <see cref="TokenCredential"/> — this file's own copy (SecretClient needs a credential; no live token acquisition happens against the fake transport).</summary>
    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-kv-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Minimal TimeProvider double — avoids adding Microsoft.Extensions.TimeProvider.Testing as a new package dep (per tests/CLAUDE.md guidance; matches HandlerOutcomeApplierTests.cs's convention).</summary>
    private sealed class TestTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
