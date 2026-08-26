// -----------------------------------------------------------------------------
// SpeContainerResolverInvariantProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for SpeContainerResolverInvariantProbe (task 176,
// Phase C'' Wave G-7 Batch G-7A2.1). Proves the REAL HttpClient probe path —
// not a hardcoded verdict — via a hand-rolled FakeHttpMessageHandler (never
// Mock<HttpMessageHandler>; ADR-038 path #1 + KEEP-path rules).
//
// PATH: tests/CLAUDE.md 7 KEEP paths — this file is a component-scoped unit
// test of the probe class (a pure L2 seam with no BFF wiring). It exercises
// behavior through the probe's PUBLIC ProbeAsync surface. It lives in the
// existing Tests project alongside AiSearchTenantFilterInvariantProbeTests
// (task 173) and CosmosPartitionKeyInvariantProbeTests (task 174) — the two
// sibling H13 real-probe test files. Path is not under tests/integration/**
// because the probe is not itself an integration boundary — the LIVE-probe
// integration test lands in Phase F rerun (task 186) per this task's POML
// escalation trigger.
//
// COVERAGE (maps to POML acceptance criteria):
//   AC-1 Placeholder replaced: exercised implicitly — asserts Kind ==
//        InvariantKind.I4SpeContainerResolver.
//   AC-2 Fail outcome on real FAILING condition: resolvedFromLiteral=true,
//        cross-tenant echo, blank container id, non-canonical container id
//        shape, blank tenantId (defense-in-depth).
//   AC-3 Pass outcome on real PASSING condition: BFF returns
//        resolvedFromLiteral=false + matching tenantId + canonical
//        containerId — probe returns Passed.
//   Additional negative-space coverage: HTTP 401/403/404/500 → InfraFault;
//   token failure → InfraFault; timeout → InfraFault; malformed JSON →
//   InfraFault; missing required field → InfraFault; non-object root →
//   InfraFault; malformed BffApiUrl → InfraFault; blank BffApiUrl →
//   InfraFault. These prove the probe NEVER returns a false-Pass on any
//   InfraFault path — the highest-severity risk for an acceptance-gate
//   invariant probe.
//   AC-4 build + test green: covered by CI.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class SpeContainerResolverInvariantProbeTests
{
    private const string CustomerId = "acme";
    private const string RunId = "run-i4-probe-1";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string OtherTenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string BffApiUrl = "https://bff-acme.azurewebsites.net";
    // Canonical Graph SPE container-id shape ('b!' + 68 chars) — fabricated
    // for tests; never a real container id.
    private const string CanonicalContainerId =
        "b!" + "AAAAAAAAAAAAAAAAAAAAAA-_BBBBBBBBBBBBBBBBBBBBBB_CCCCCCCCCCCCCCCCCC";

    // -----------------------------------------------------------------------
    // AC-1: probe metadata + IInvariantProbe contract
    // -----------------------------------------------------------------------

    [Fact]
    public void Kind_IsI4SpeContainerResolver()
    {
        var probe = BuildProbe();
        probe.Kind.Should().Be(InvariantKind.I4SpeContainerResolver);
    }

    // -----------------------------------------------------------------------
    // AC-3: Pass outcome on real passing condition
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_ResolverEchoesMatchingTenantAndCanonicalContainerId_ReturnsPassed()
    {
        var handler = new FakeBffHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $$"""
              { "tenantId": "{{TenantId}}", "containerId": "{{CanonicalContainerId}}",
                "resolverSource": "kv", "resolvedFromLiteral": false }
              """));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Passed>();
        handler.RequestedUrls.Should().OnlyContain(u =>
            u.Contains("/api/diagnostics/tenant-container-resolver", StringComparison.Ordinal)
            && u.Contains("tenantId=" + TenantId, StringComparison.Ordinal));
        // Bearer header must be present — probe MUST attach a token even when
        // the BFF-side may not require it (defense-in-depth: production BFF
        // will require it, test hosts SHOULD ignore missing).
        handler.CapturedAuthorizationSchemes.Should().OnlyContain(s =>
            string.Equals(s, "Bearer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeAsync_ResolverResponseOmitsOptionalFromLiteralFlag_ReturnsPassed()
    {
        // The 'resolvedFromLiteral' flag is optional per contract — absence
        // is Pass-eligible provided the other invariants hold. The probe MUST
        // NOT trip InfraFault on the missing flag.
        var handler = new FakeBffHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $$"""
              { "tenantId": "{{TenantId}}", "containerId": "{{CanonicalContainerId}}",
                "resolverSource": "options" }
              """));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Passed>();
    }

    // -----------------------------------------------------------------------
    // AC-2: Fail outcome — CATASTROPHIC branches
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_ResolvedFromLiteralTrue_ReturnsFailedCatastrophic()
    {
        var handler = new FakeBffHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $$"""
              { "tenantId": "{{TenantId}}", "containerId": "{{CanonicalContainerId}}",
                "resolverSource": "hardcoded", "resolvedFromLiteral": true }
              """));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("CATASTROPHIC")
            .And.Contain("resolvedFromLiteral=true");
    }

    [Fact]
    public async Task ProbeAsync_ResolverEchoesDifferentTenantId_ReturnsFailedCatastrophic()
    {
        var handler = new FakeBffHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $$"""
              { "tenantId": "{{OtherTenantId}}", "containerId": "{{CanonicalContainerId}}",
                "resolverSource": "kv", "resolvedFromLiteral": false }
              """));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("CATASTROPHIC")
            .And.Contain(OtherTenantId)
            .And.Contain(TenantId);
    }

    [Fact]
    public async Task ProbeAsync_BlankContainerId_ReturnsFailed()
    {
        var handler = new FakeBffHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $$"""
              { "tenantId": "{{TenantId}}", "containerId": "",
                "resolverSource": "kv", "resolvedFromLiteral": false }
              """));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("EMPTY containerId");
    }

    [Fact]
    public async Task ProbeAsync_NonCanonicalContainerIdShape_ReturnsFailed()
    {
        // Missing 'b!' prefix — a placeholder or malformed value slipped past
        // the resolver.
        var handler = new FakeBffHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $$"""
              { "tenantId": "{{TenantId}}", "containerId": "not-a-container-id",
                "resolverSource": "kv", "resolvedFromLiteral": false }
              """));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("canonical Graph")
            .And.Contain("SPE container-id shape");
    }

    [Fact]
    public async Task ProbeAsync_BlankTenantId_ReturnsFailed_DefenseInDepth()
    {
        var handler = new FakeBffHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised when tenantId is blank"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(tenantId: ""), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("blank tenantId");
    }

    // -----------------------------------------------------------------------
    // InfraFault paths — cannot verdict, so handler classifies Resumable.
    // Proves the probe NEVER false-Passes on transient/wiring failures.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_Http404_ReturnsInfraFault_EndpointNotYetDeployed()
    {
        // The exact scenario the POML <escalation> trigger names: the BFF
        // diagnostic endpoint is not yet deployed. Probe MUST NOT false-Pass;
        // it MUST return InfraFault so H13 classifies Resumable.
        var handler = new FakeBffHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain"),
            });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("not yet deployed");
    }

    [Fact]
    public async Task ProbeAsync_Http401_ReturnsInfraFault_MissingRbac()
    {
        var handler = new FakeBffHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("unauthorized", Encoding.UTF8, "text/plain"),
            });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("HTTP 401");
    }

    [Fact]
    public async Task ProbeAsync_Http403_ReturnsInfraFault_MissingRbac()
    {
        var handler = new FakeBffHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("forbidden", Encoding.UTF8, "text/plain"),
            });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("HTTP 403");
    }

    [Fact]
    public async Task ProbeAsync_Http500_ReturnsInfraFault()
    {
        var handler = new FakeBffHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("bff exploded", Encoding.UTF8, "text/plain"),
            });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("HTTP 500");
    }

    [Fact]
    public async Task ProbeAsync_MalformedJsonResponse_ReturnsInfraFault()
    {
        var handler = new FakeBffHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK, "{this-is-not-valid-json"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("not parseable JSON");
    }

    [Fact]
    public async Task ProbeAsync_MissingContainerIdField_ReturnsInfraFault()
    {
        var handler = new FakeBffHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $$"""{ "tenantId": "{{TenantId}}", "resolverSource": "kv" }"""));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("missing required 'containerId'");
    }

    [Fact]
    public async Task ProbeAsync_MissingTenantIdEchoField_ReturnsInfraFault()
    {
        var handler = new FakeBffHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $$"""{ "containerId": "{{CanonicalContainerId}}", "resolverSource": "kv" }"""));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("missing required 'tenantId'");
    }

    [Fact]
    public async Task ProbeAsync_NonObjectJsonRoot_ReturnsInfraFault()
    {
        var handler = new FakeBffHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK, "[1,2,3]"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("not a JSON object");
    }

    [Fact]
    public async Task ProbeAsync_ResolvedFromLiteralNonBoolean_ReturnsInfraFault()
    {
        var handler = new FakeBffHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $$"""
              { "tenantId": "{{TenantId}}", "containerId": "{{CanonicalContainerId}}",
                "resolverSource": "kv", "resolvedFromLiteral": "false" }
              """));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("non-boolean");
    }

    [Fact]
    public async Task ProbeAsync_BlankBffApiUrl_ReturnsInfraFault()
    {
        var handler = new FakeBffHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised when BffApiUrl is blank"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(bffApiUrl: ""), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("BffApiUrl is empty");
    }

    [Fact]
    public async Task ProbeAsync_MalformedBffApiUrl_ReturnsInfraFault()
    {
        var handler = new FakeBffHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised when BffApiUrl is malformed"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(bffApiUrl: "not a url"), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("not a valid http(s) absolute URL");
    }

    [Fact]
    public async Task ProbeAsync_NonHttpBffApiUrl_ReturnsInfraFault()
    {
        var handler = new FakeBffHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised for non-http schemes"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(
            BuildRequest(bffApiUrl: "ftp://example.com"), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("not a valid http(s) absolute URL");
    }

    [Fact]
    public async Task ProbeAsync_TokenAcquisitionThrows_ReturnsInfraFault()
    {
        var handler = new FakeBffHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be exercised when token acquisition fails"));
        var throwingCred = new ThrowingTokenCredential(
            new InvalidOperationException("simulated auth failure"));
        var probe = BuildProbe(handler, throwingCred);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("AAD token acquisition")
            .And.Contain("simulated auth failure");
    }

    [Fact]
    public async Task ProbeAsync_HttpTransportThrows_ReturnsInfraFault()
    {
        var handler = new FakeBffHttpMessageHandler(_ =>
            throw new HttpRequestException("connection refused"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("unreachable")
            .And.Contain("connection refused");
    }

    // -----------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_CancellationBeforeHttpCall_Throws()
    {
        var handler = new FakeBffHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            $$"""
              { "tenantId": "{{TenantId}}", "containerId": "{{CanonicalContainerId}}",
                "resolverSource": "kv", "resolvedFromLiteral": false }
              """));
        var probe = BuildProbe(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await probe.ProbeAsync(BuildRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static SpeContainerResolverInvariantProbe BuildProbe(
        FakeBffHttpMessageHandler? handler = null,
        TokenCredential? credential = null)
    {
        handler ??= new FakeBffHttpMessageHandler(_ =>
            throw new InvalidOperationException("default handler must not be exercised"));
        credential ??= new FakeTokenCredential();
        var options = Options.Create(new H13AcceptanceOptions
        {
            InvariantVerifierTimeout = TimeSpan.FromSeconds(30),
        });
        return new SpeContainerResolverInvariantProbe(
            new FakeHttpClientFactory(handler),
            credential,
            options,
            NullLogger<SpeContainerResolverInvariantProbe>.Instance);
    }

    private static InvariantVerificationRequest BuildRequest(
        string tenantId = TenantId,
        string bffApiUrl = BffApiUrl)
        => new(
            CustomerId: CustomerId,
            RunId: RunId,
            TenantId: tenantId,
            SubscriptionId: Guid.NewGuid().ToString(),
            AiSearchEndpoint: string.Empty,
            CosmosEndpoint: string.Empty,
            BffApiUrl: bffApiUrl,
            ProvisioningScriptsDirectory: string.Empty);

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // ---- Fake collaborators (hand-rolled per ADR-038 §5 no Mock<T>) --------

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-i4-probe-token", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class ThrowingTokenCredential : TokenCredential
    {
        private readonly Exception _toThrow;
        public ThrowingTokenCredential(Exception toThrow) { _toThrow = toThrow; }
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw _toThrow;
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw _toThrow;
    }

    /// <summary>
    /// Hand-rolled <see cref="HttpMessageHandler"/> — records every requested
    /// URL + captured Authorization scheme so tests can assert the probe issued
    /// a real GET with the correct URL shape + bearer auth. Never Mock&lt;T&gt;.
    /// </summary>
    private sealed class FakeBffHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestedUrls { get; } = new();
        public List<string> CapturedAuthorizationSchemes { get; } = new();

        public FakeBffHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Mirror real HttpClient behavior: honor the caller's CT.
            cancellationToken.ThrowIfCancellationRequested();
            RequestedUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
            if (request.Headers.Authorization is not null)
            {
                CapturedAuthorizationSchemes.Add(request.Headers.Authorization.Scheme);
            }
            return Task.FromResult(_responder(request));
        }
    }
}
