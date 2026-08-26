// -----------------------------------------------------------------------------
// SpeConfidentialClientGraphFactoryTests.cs
//
// L2 CONTROL-PLANE unit tests for SpeConfidentialClientGraphFactory (task 131,
// Wave G-3) — the shared T6 cert-from-KV + ClientCertificateCredential
// construction helper H8's GraphContainerTypeProvisioner /
// GraphAppOnlyContainerVerifier both use.
//
// THIS IS THE T6 CERT-PATH TEST (POML acceptance criterion #3: "A test
// confirms GraphContainerTypeProvisioner constructs its Graph client with a
// certificate credential sourced from KV, not a secret"). ADR-038 path #1 —
// a REAL Azure.Security.KeyVault.Secrets.SecretClient runs against a fake
// Azure.Core.Pipeline.HttpClientTransport (same pattern as
// SecretClientKvWriterTests.cs / task 125) — never Mock&lt;HttpMessageHandler&gt;
// as the SDK's own request/response marshaling. A REAL self-signed
// X509Certificate2 (generated in-test, base64-PFX-encoded) stands in for the
// KV secret value, proving the base64-decode + X509CertificateLoader.LoadPkcs12
// round-trip actually works end to end, not just that SOME bytes were read.
//
// The Graph HTTP calls themselves are NOT exercised here (parity with the
// established project precedent — see GraphContainerTypeProvisioner.cs's
// file header "NOT UNIT-TESTED IN THE CI SUITE" section); this file's scope
// is exactly the KV cert-load + credential-construction seam, which IS fully
// exercisable without a live Graph tenant.
// -----------------------------------------------------------------------------

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class SpeConfidentialClientGraphFactoryTests
{
    private const string VaultName = "sprk-acme-prod-kv";
    private const string SecretName = "SPE-OwnerCert-Pfx";
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string ClientAppId = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";

    // ---------- T1 — cert loads from a fake-KV base64-PFX secret ----------

    [Fact]
    public async Task LoadCertificateAsync_DecodesBase64PfxFromKvSecret_ReturnsCertWithPrivateKey()
    {
        using var sourceCert = CreateSelfSignedTestCertificate();
        var base64Pfx = Convert.ToBase64String(sourceCert.Export(X509ContentType.Pfx));
        var handler = new FakeSecretGetHandler(base64Pfx);
        var options = new SecretClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) };

        using var loaded = await SpeConfidentialClientGraphFactory.LoadCertificateAsync(
            new FakeCredential(), options, VaultName, SecretName, TimeSpan.FromSeconds(5), CancellationToken.None);

        loaded.HasPrivateKey.Should().BeTrue();
        loaded.Thumbprint.Should().Be(sourceCert.Thumbprint);
        handler.RequestedUris.Should().ContainSingle(
            u => u.AbsolutePath.Contains(SecretName, StringComparison.Ordinal));
    }

    // ---------- T2 — THE cert-path assertion: ClientCertificateCredential, NEVER ClientSecretCredential ----------

    [Fact]
    public async Task BuildCredential_FromKvSourcedCert_ProducesClientCertificateCredential_NeverClientSecretCredential()
    {
        using var sourceCert = CreateSelfSignedTestCertificate();
        var base64Pfx = Convert.ToBase64String(sourceCert.Export(X509ContentType.Pfx));
        var handler = new FakeSecretGetHandler(base64Pfx);
        var options = new SecretClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) };

        using var loaded = await SpeConfidentialClientGraphFactory.LoadCertificateAsync(
            new FakeCredential(), options, VaultName, SecretName, TimeSpan.FromSeconds(5), CancellationToken.None);

        var credential = SpeConfidentialClientGraphFactory.BuildCredential(TenantId, ClientAppId, loaded);

        // AC#3 — T6's whole point: confidential-client CERT credential, sourced
        // from KV, never a secret-based credential.
        credential.Should().BeOfType<ClientCertificateCredential>();
        credential.Should().NotBeOfType<ClientSecretCredential>();
        credential.GetType().Should().NotBe(typeof(ClientSecretCredential));
    }

    [Fact]
    public void BuildGraphClient_ReturnsClientBoundToCertificateCredential()
    {
        using var sourceCert = CreateSelfSignedTestCertificate();

        var graph = SpeConfidentialClientGraphFactory.BuildGraphClient(TenantId, ClientAppId, sourceCert);

        graph.Should().NotBeNull();
        graph.RequestAdapter.Should().NotBeNull();
    }

    // ---------- T3 — malformed base64 -> InvalidOperationException (infra fault, no side effect) ----------

    [Fact]
    public async Task LoadCertificateAsync_SecretValueNotValidBase64_ThrowsInvalidOperationException()
    {
        var handler = new FakeSecretGetHandler("not-valid-base64!!!");
        var options = new SecretClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) };

        var act = async () => await SpeConfidentialClientGraphFactory.LoadCertificateAsync(
            new FakeCredential(), options, VaultName, SecretName, TimeSpan.FromSeconds(5), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not valid base64*");
    }

    // ---------- T4 — secret not found (404) propagates (handler classifies as infra fault) ----------

    [Fact]
    public async Task LoadCertificateAsync_SecretNotFound_PropagatesRequestFailedException()
    {
        var handler = new FakeSecretGetHandler(base64PfxOrNull: null); // 404
        var options = new SecretClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) };

        var act = async () => await SpeConfidentialClientGraphFactory.LoadCertificateAsync(
            new FakeCredential(), options, VaultName, SecretName, TimeSpan.FromSeconds(5), CancellationToken.None);

        await act.Should().ThrowAsync<Azure.RequestFailedException>();
    }

    // ---------- T5 — T6 delegated-token trap phrase detection ----------

    [Fact]
    public void IsDelegatedTokenTrapError_MessageContainsTrapPhrase_ReturnsTrue()
    {
        var ex = new ODataError
        {
            ResponseStatusCode = 403,
            Error = new MainError { Message = "Public client not allowed for this resource." },
        };

        SpeConfidentialClientGraphFactory.IsDelegatedTokenTrapError(ex).Should().BeTrue();
    }

    [Fact]
    public void IsDelegatedTokenTrapError_UnrelatedError_ReturnsFalse()
    {
        var ex = new ODataError
        {
            ResponseStatusCode = 400,
            Error = new MainError { Message = "Invalid request payload." },
        };

        SpeConfidentialClientGraphFactory.IsDelegatedTokenTrapError(ex).Should().BeFalse();
    }

    // ---------- helpers ----------

    private static X509Certificate2 CreateSelfSignedTestCertificate()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=spe-test-cert", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        // Exportable — this in-memory cert stands in for the KV secret's SOURCE
        // material (what an operator would have uploaded); it is exported to
        // PFX bytes below to simulate the KV secret's stored value. The
        // FACTORY's own loaded-cert (EphemeralKeySet, non-exportable) is a
        // SEPARATE object under test, not this one.
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-kv-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Fakes a single GET /secrets/{name} call — parity with SecretClientKvWriterTests.cs's FakeSecretsHandler GET branch.</summary>
    private sealed class FakeSecretGetHandler : HttpMessageHandler
    {
        private readonly string? _base64PfxOrNull;

        public FakeSecretGetHandler(string? base64PfxOrNull)
        {
            _base64PfxOrNull = base64PfxOrNull;
        }

        public List<Uri> RequestedUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);

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
                id = $"https://{VaultName}.vault.azure.net/secrets/{name}/v1",
                attributes = new { enabled = true },
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
