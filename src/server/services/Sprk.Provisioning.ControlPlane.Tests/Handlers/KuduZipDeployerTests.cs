// -----------------------------------------------------------------------------
// KuduZipDeployerTests.cs
//
// L2 CONTROL-PLANE unit tests for KuduZipDeployer (task 132, Wave G-3).
// Proves the real HttpClient POST request-construction path (URL shape,
// bearer-token auth header, content-type, body) via a hand-rolled fake
// HttpMessageHandler — NOT Mock&lt;HttpMessageHandler&gt;, which
// docs/standards/TEST-ARCHITECTURE.md / ADR-038 bans. Parity with
// BapRestEnvironmentRateProbeTests.cs's FakeBapHttpMessageHandler pattern
// (the established plain-HttpClient fake-transport idiom in this test
// project, distinct from the ARM-SDK-specific FakeArmHttpMessageHandler
// since Kudu is not an ARM endpoint). ADR-038 path #1.
// -----------------------------------------------------------------------------

using System.Net;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class KuduZipDeployerTests : IDisposable
{
    private const string AppServiceName = "spaarke-bff-acme";
    private const string SlotName = "staging";

    private readonly string _tempZipPath;

    public KuduZipDeployerTests()
    {
        _tempZipPath = Path.Combine(Path.GetTempPath(), $"h9-kudu-test-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(_tempZipPath, new byte[] { 1, 2, 3, 4 });
    }

    public void Dispose()
    {
        if (File.Exists(_tempZipPath))
        {
            try { File.Delete(_tempZipPath); } catch { /* best-effort cleanup */ }
        }
    }

    private static BffDeployOptions NewOptions() => new()
    {
        KuduZipDeployTimeout = TimeSpan.FromSeconds(5),
    };

    // ---------- T1 success — request shape ground-truthed ----------

    [Fact]
    public async Task DeployAsync_Success_PostsToCorrectKuduEndpointWithBearerToken()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new FakeKuduHttpMessageHandler(async request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var deployer = new KuduZipDeployer(
            new HttpClient(handler),
            new FakeTokenCredential(),
            Options.Create(NewOptions()),
            NullLogger<KuduZipDeployer>.Instance);

        var result = await deployer.DeployAsync(
            new KuduZipDeployRequest(AppServiceName, SlotName, _tempZipPath), CancellationToken.None);

        result.Should().BeOfType<KuduZipDeployResult.Success>();

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.Host.Should().Be($"{AppServiceName}-{SlotName}.scm.azurewebsites.net");
        capturedRequest.RequestUri.AbsolutePath.Should().Be("/api/zipdeploy");
        capturedRequest.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("fake-arm-scope-token");
        capturedRequest.Content!.Headers.ContentType!.MediaType.Should().Be("application/zip");
    }

    // ---------- T2 non-2xx — domain Failure, not throw ----------

    [Fact]
    public async Task DeployAsync_NonSuccessStatus_ReturnsFailureWithRawEvidence()
    {
        var handler = new FakeKuduHttpMessageHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("{\"Message\":\"upstream Kudu instance not ready\"}"),
            }));

        var deployer = new KuduZipDeployer(
            new HttpClient(handler),
            new FakeTokenCredential(),
            Options.Create(NewOptions()),
            NullLogger<KuduZipDeployer>.Instance);

        var result = await deployer.DeployAsync(
            new KuduZipDeployRequest(AppServiceName, SlotName, _tempZipPath), CancellationToken.None);

        var failure = result.Should().BeOfType<KuduZipDeployResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("502");
        failure.Diagnostic.Should().Contain("upstream Kudu instance not ready",
            "the raw response body must be captured as evidence per the POML's escalation-trigger requirement");
    }

    // ---------- T3 local zip missing — throws before any HTTP call ----------

    [Fact]
    public async Task DeployAsync_LocalZipMissing_ThrowsWithoutCallingKudu()
    {
        var deployer = new KuduZipDeployer(
            new HttpClient(new FakeKuduHttpMessageHandler(_ => throw new InvalidOperationException("must not call Kudu"))),
            new FakeTokenCredential(),
            Options.Create(NewOptions()),
            NullLogger<KuduZipDeployer>.Instance);

        var act = async () => await deployer.DeployAsync(
            new KuduZipDeployRequest(AppServiceName, SlotName,
                Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.zip")),
            CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    // ---------- T4 HTTP call throws — infra fault propagates ----------

    [Fact]
    public async Task DeployAsync_HttpClientThrows_PropagatesAsInfraFault()
    {
        var handler = new FakeKuduHttpMessageHandler(_ => throw new HttpRequestException("DNS resolution failed"));

        var deployer = new KuduZipDeployer(
            new HttpClient(handler),
            new FakeTokenCredential(),
            Options.Create(NewOptions()),
            NullLogger<KuduZipDeployer>.Instance);

        var act = async () => await deployer.DeployAsync(
            new KuduZipDeployRequest(AppServiceName, SlotName, _tempZipPath), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ---------- T5 timeout — mapped to TimeoutException ----------

    [Fact]
    public async Task DeployAsync_ExceedsTimeout_ThrowsTimeoutException()
    {
        // Handler genuinely honors the CancellationToken SendAsync receives
        // (the linked timeout token KuduZipDeployer constructs) via
        // Task.Delay(..., cancellationToken) — proves the deployer's
        // linked-timeout wiring for real, not just its catch-block wording.
        var handler = new FakeKuduHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var deployer = new KuduZipDeployer(
            new HttpClient(handler),
            new FakeTokenCredential(),
            Options.Create(new BffDeployOptions { KuduZipDeployTimeout = TimeSpan.FromMilliseconds(100) }),
            NullLogger<KuduZipDeployer>.Instance);

        var act = async () => await deployer.DeployAsync(
            new KuduZipDeployRequest(AppServiceName, SlotName, _tempZipPath), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    // ---------- fakes ----------

    /// <summary>
    /// Hand-rolled fake <see cref="HttpMessageHandler"/> for KuduZipDeployer's
    /// plain (non-ARM) HttpClient — NOT Mock&lt;HttpMessageHandler&gt;
    /// (banned per ADR-038 / testing.md). Parity with
    /// BapRestEnvironmentRateProbeTests.FakeBapHttpMessageHandler; extended
    /// with a CancellationToken pass-through so the timeout test can prove
    /// real cooperative cancellation.
    /// </summary>
    private sealed class FakeKuduHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public FakeKuduHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
            : this((request, _) => responder(request))
        {
        }

        public FakeKuduHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-arm-scope-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
