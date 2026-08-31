// -----------------------------------------------------------------------------
// T6SpeConfidentialClientTrapProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for T6SpeConfidentialClientTrapProbe (task 175,
// Wave G-7 pipelined with H8 / task 131).
//
// ADR-038 CATEGORY:
//   Path #1 -- pure C# unit test. NO live pwsh / az CLI / Graph / Dataverse
//   / KV. A REAL Azure.Security.KeyVault.Secrets.SecretClient runs against a
//   fake Azure.Core.Pipeline.HttpClientTransport (same pattern as
//   SpeConfidentialClientGraphFactoryTests.cs / task 131). The Graph half is
//   substituted with FakeT6GraphAppOnlyProbe returning canned results -- the
//   real GraphContainerTypesListAppOnlyProbe is out-of-scope per the same
//   posture that keeps GraphContainerTypeProvisioner / GraphAppOnlyContainer
//   Verifier off the CI unit-test surface (Phase F acceptance -- task 186 --
//   covers the live Graph path).
// -----------------------------------------------------------------------------

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class T6SpeConfidentialClientTrapProbeTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h13-run";
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string SubscriptionId = "sub-cus-acme-prod";
    private const string DataverseUrl = "https://sprk-acme.crm.dynamics.com";
    private const string BffAppRegId = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";
    private const string UamiClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string AppServiceName = "sprk-bff-acme";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";

    // ---------- PASS-1 -- happy path ----------

    [Fact]
    public async Task ProbeAsync_KvCertReadable_GraphSucceeds_ReturnsPassedT6()
    {
        using var sourceCert = CreateSelfSignedTestCertificate();
        var base64Pfx = Convert.ToBase64String(sourceCert.Export(X509ContentType.Pfx));
        var kvHandler = new FakeKvSecretGetHandler(base64Pfx);
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(T6GraphAppOnlyProbeResults.Succeeded);

        var probe = BuildProbe(kvHandler, graphProbe);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>()
            .Which.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        graphProbe.CallCount.Should().Be(1);
        graphProbe.LastTenantId.Should().Be(TenantId);
        graphProbe.LastClientAppId.Should().Be(BffAppRegId);
        graphProbe.LastCertThumbprint.Should().Be(sourceCert.Thumbprint);
    }

    // ---------- FAIL-1 -- T6 silent-fail manifested ----------

    [Fact]
    public async Task ProbeAsync_KvCertReadable_GraphReturnsDelegatedTokenTrap_ReturnsFailedT6()
    {
        using var sourceCert = CreateSelfSignedTestCertificate();
        var base64Pfx = Convert.ToBase64String(sourceCert.Export(X509ContentType.Pfx));
        var kvHandler = new FakeKvSecretGetHandler(base64Pfx);
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(
            new T6GraphAppOnlyProbeResult.DelegatedTokenTrapDetectedResult(
                StatusCode: 403,
                Diagnostic: "Graph error code=InvalidClientToken message='Public client not allowed for this resource.'"));

        var probe = BuildProbe(kvHandler, graphProbe);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        failed.Diagnostic.Should().Contain("T6 silent-fail trap MANIFESTED");
        failed.Diagnostic.Should().Contain("public client not allowed");
        failed.Diagnostic.Should().Contain("403");
        failed.Diagnostic.Should().Contain("FR-33");
    }

    // ---------- INFRA-1 -- KV secret missing (404) ----------

    [Fact]
    public async Task ProbeAsync_KvSecretMissing_ReturnsInfraFaultT6_GraphNeverInvoked()
    {
        var kvHandler = new FakeKvSecretGetHandler(base64PfxOrNull: null);
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(T6GraphAppOnlyProbeResults.Succeeded);

        var probe = BuildProbe(kvHandler, graphProbe);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        infra.Diagnostic.Should().Contain("SPE-OwnerCert-Pfx");
        infra.Diagnostic.Should().Contain(KeyVaultName);
        infra.Diagnostic.Should().ContainAny("unreadable", "RequestFailedException");
        graphProbe.CallCount.Should().Be(0);
    }

    // ---------- INFRA-2 -- KV secret malformed ----------

    [Fact]
    public async Task ProbeAsync_KvSecretMalformed_ReturnsInfraFaultT6_GraphNeverInvoked()
    {
        var kvHandler = new FakeKvSecretGetHandler("not-valid-base64!!!");
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(T6GraphAppOnlyProbeResults.Succeeded);

        var probe = BuildProbe(kvHandler, graphProbe);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        infra.Diagnostic.Should().Contain("SPE-OwnerCert-Pfx");
        infra.Diagnostic.Should().Contain("not a usable base64-encoded PFX");
        graphProbe.CallCount.Should().Be(0);
    }

    // ---------- INFRA-3 -- Graph 404 replication-pending ----------

    [Fact]
    public async Task ProbeAsync_GraphReturnsReplicationPending_ReturnsInfraFaultT6_NotFailed()
    {
        using var sourceCert = CreateSelfSignedTestCertificate();
        var base64Pfx = Convert.ToBase64String(sourceCert.Export(X509ContentType.Pfx));
        var kvHandler = new FakeKvSecretGetHandler(base64Pfx);
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(
            new T6GraphAppOnlyProbeResult.ReplicationPendingResult(
                "Graph containerTypes GET returned 404 Not Found (ResourceNotFound not found)."));

        var probe = BuildProbe(kvHandler, graphProbe);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        infra.Diagnostic.Should().Contain("24h");
        infra.Diagnostic.Should().Contain("replication window");
        infra.Diagnostic.Should().Contain("NOT a T6 trap");
        outcome.Should().NotBeOfType<TrapVerificationOutcome.Failed>();
    }

    // ---------- INFRA-4 -- Graph seam InfraFault ----------

    [Fact]
    public async Task ProbeAsync_GraphReturnsInfraFault_ReturnsInfraFaultT6()
    {
        using var sourceCert = CreateSelfSignedTestCertificate();
        var base64Pfx = Convert.ToBase64String(sourceCert.Export(X509ContentType.Pfx));
        var kvHandler = new FakeKvSecretGetHandler(base64Pfx);
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(
            new T6GraphAppOnlyProbeResult.InfraFaultResult(
                "Graph containerTypes GET ODataError status=503: ServiceUnavailable Service unavailable."));

        var probe = BuildProbe(kvHandler, graphProbe);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        infra.Diagnostic.Should().Contain("verdict deferred");
        infra.Diagnostic.Should().Contain("503");
    }

    // ---------- INFRA-5 -- Graph seam throws unexpectedly ----------

    [Fact]
    public async Task ProbeAsync_GraphSeamThrows_ReturnsInfraFaultT6_NoLeak()
    {
        using var sourceCert = CreateSelfSignedTestCertificate();
        var base64Pfx = Convert.ToBase64String(sourceCert.Export(X509ContentType.Pfx));
        var kvHandler = new FakeKvSecretGetHandler(base64Pfx);
        var graphProbe = FakeT6GraphAppOnlyProbe.WithThrower(
            new InvalidCastException("rogue impl thrown"));

        var probe = BuildProbe(kvHandler, graphProbe);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        infra.Diagnostic.Should().Contain("InvalidCastException");
        infra.Diagnostic.Should().Contain("verdict deferred");
    }

    // ---------- GUARD-1 -- caller bug (missing inputs) ----------

    [Fact]
    public async Task ProbeAsync_MissingCustomerId_Throws()
    {
        var probe = BuildProbe(new FakeKvSecretGetHandler(base64PfxOrNull: null),
            FakeT6GraphAppOnlyProbe.WithResult(T6GraphAppOnlyProbeResults.Succeeded));

        var act = async () => await probe.ProbeAsync(
            BuildRequest() with { CustomerId = string.Empty }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ProbeAsync_MissingTenantId_Throws()
    {
        var probe = BuildProbe(new FakeKvSecretGetHandler(base64PfxOrNull: null),
            FakeT6GraphAppOnlyProbe.WithResult(T6GraphAppOnlyProbeResults.Succeeded));

        var act = async () => await probe.ProbeAsync(
            BuildRequest() with { TenantId = string.Empty }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ProbeAsync_MissingBffAppRegId_Throws()
    {
        var probe = BuildProbe(new FakeKvSecretGetHandler(base64PfxOrNull: null),
            FakeT6GraphAppOnlyProbe.WithResult(T6GraphAppOnlyProbeResults.Succeeded));

        var act = async () => await probe.ProbeAsync(
            BuildRequest() with { BffAppRegId = string.Empty }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ProbeAsync_MissingKeyVaultName_Throws()
    {
        var probe = BuildProbe(new FakeKvSecretGetHandler(base64PfxOrNull: null),
            FakeT6GraphAppOnlyProbe.WithResult(T6GraphAppOnlyProbeResults.Succeeded));

        var act = async () => await probe.ProbeAsync(
            BuildRequest() with { KeyVaultName = string.Empty }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---------- INV-1 -- trap kind is always T6 ----------

    [Fact]
    public async Task ProbeAsync_EveryBranch_AlwaysReturnsT6Kind()
    {
        using var sourceCert = CreateSelfSignedTestCertificate();
        var base64Pfx = Convert.ToBase64String(sourceCert.Export(X509ContentType.Pfx));

        var pass = await BuildProbe(new FakeKvSecretGetHandler(base64Pfx),
            FakeT6GraphAppOnlyProbe.WithResult(T6GraphAppOnlyProbeResults.Succeeded))
            .ProbeAsync(BuildRequest(), CancellationToken.None);

        var fail = await BuildProbe(new FakeKvSecretGetHandler(base64Pfx),
            FakeT6GraphAppOnlyProbe.WithResult(
                new T6GraphAppOnlyProbeResult.DelegatedTokenTrapDetectedResult(403, "trap")))
            .ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = await BuildProbe(new FakeKvSecretGetHandler(base64PfxOrNull: null),
            FakeT6GraphAppOnlyProbe.WithResult(T6GraphAppOnlyProbeResults.Succeeded))
            .ProbeAsync(BuildRequest(), CancellationToken.None);

        GetKind(pass).Should().Be(TrapKind.T6SpeConfidentialClient);
        GetKind(fail).Should().Be(TrapKind.T6SpeConfidentialClient);
        GetKind(infra).Should().Be(TrapKind.T6SpeConfidentialClient);
    }

    // ---------- helpers ----------

    private static TrapKind GetKind(TrapVerificationOutcome outcome) => outcome switch
    {
        TrapVerificationOutcome.Passed p => p.Kind,
        TrapVerificationOutcome.Failed f => f.Kind,
        TrapVerificationOutcome.InfraFault i => i.Kind,
        _ => throw new InvalidOperationException("unknown outcome"),
    };

    private static TrapVerificationRequest BuildRequest() => new(
        CustomerId: CustomerId,
        RunId: RunId,
        TenantId: TenantId,
        SubscriptionId: SubscriptionId,
        DataverseUrl: DataverseUrl,
        BffAppRegId: BffAppRegId,
        UamiClientId: UamiClientId,
        KeyVaultName: KeyVaultName,
        AppServiceName: AppServiceName,
        ResourceGroupName: ResourceGroupName);

    private static T6SpeConfidentialClientTrapProbe BuildProbe(
        FakeKvSecretGetHandler kvHandler, IT6GraphAppOnlyProbe graphProbe)
    {
        var kvOptions = new SecretClientOptions
        {
            Transport = new HttpClientTransport(new HttpClient(kvHandler)),
        };
        var options = Options.Create(new H13AcceptanceOptions
        {
            TrapVerifierTimeout = TimeSpan.FromSeconds(30),
        });
        return new T6SpeConfidentialClientTrapProbe(
            sharedCredential: new FakeCredential(),
            clientOptions: kvOptions,
            graphProbe: graphProbe,
            options: options,
            logger: NullLogger<T6SpeConfidentialClientTrapProbe>.Instance);
    }

    private static X509Certificate2 CreateSelfSignedTestCertificate()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=t6-probe-test-cert", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-kv-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class FakeKvSecretGetHandler : HttpMessageHandler
    {
        private readonly string? _base64PfxOrNull;

        public FakeKvSecretGetHandler(string? base64PfxOrNull)
        {
            _base64PfxOrNull = base64PfxOrNull;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_base64PfxOrNull is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"error":{"code":"SecretNotFound","message":"not found"}}""",
                        System.Text.Encoding.UTF8, "application/json"),
                });
            }

            var name = request.RequestUri!.AbsolutePath.Trim('/').Split('/').First();
            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                value = _base64PfxOrNull,
                id = $"https://{KeyVaultName}.vault.azure.net/secrets/{name}/v1",
                attributes = new { enabled = true },
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FakeT6GraphAppOnlyProbe : IT6GraphAppOnlyProbe
    {
        private readonly T6GraphAppOnlyProbeResult? _result;
        private readonly Exception? _thrower;

        private FakeT6GraphAppOnlyProbe(T6GraphAppOnlyProbeResult? result, Exception? thrower)
        {
            _result = result;
            _thrower = thrower;
        }

        public static FakeT6GraphAppOnlyProbe WithResult(T6GraphAppOnlyProbeResult result)
            => new(result, thrower: null);

        public static FakeT6GraphAppOnlyProbe WithThrower(Exception ex)
            => new(result: null, thrower: ex);

        public int CallCount { get; private set; }

        public string? LastTenantId { get; private set; }

        public string? LastClientAppId { get; private set; }

        public string? LastCertThumbprint { get; private set; }

        public Task<T6GraphAppOnlyProbeResult> ProbeAsync(
            string tenantId, string clientAppId, X509Certificate2 cert,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastTenantId = tenantId;
            LastClientAppId = clientAppId;
            LastCertThumbprint = cert.Thumbprint;

            if (_thrower is not null)
            {
                throw _thrower;
            }
            return Task.FromResult(_result!);
        }
    }
}
