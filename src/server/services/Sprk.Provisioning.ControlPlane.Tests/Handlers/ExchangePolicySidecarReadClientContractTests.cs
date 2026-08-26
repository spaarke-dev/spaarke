// -----------------------------------------------------------------------------
// ExchangePolicySidecarReadClientContractTests.cs
//
// L2 CONTROL-PLANE contract tests for ExchangePolicySidecarReadClient
// (task 180, Wave G-7 -- H13 T4 acceptance-gate probe sidecar read-route
// client). Proves the wire contract between the C# read client and task
// 114's Listener.ps1 GET /policies route (task 180's read-route extension).
//
// ADR-038 CATEGORY: Path #1 -- pure C# unit test with a hand-rolled
// HttpMessageHandler as the transport fake. Never Mock<HttpMessageHandler>.
// No live sidecar / no live pwsh -- end-to-end verification against a live
// sidecar is out of scope (task 162 pattern would cover if / when task 180's
// route is exercised live).
//
// COVERAGE (maps to sibling contract-test pattern in
// ExchangePolicySidecarClientContractTests):
//   R1  Request shape -- GET /policies?tenantId=X&correlationId=Y with
//       X-Sidecar-Auth header on every outbound request.
//   R2  Wire Success + policies list -> ExchangePolicyReadOutcome.Success
//       with observedAppIds + full per-policy projection.
//   R3  Wire Success + empty policies -> Success with empty lists.
//   R4  Wire Success + null arrays (defensive normalization) -> Success with
//       empty lists (never throw).
//   R5  Wire Failure -> ExchangePolicyReadOutcome.Failure passing through
//       sidecar diagnostic.
//   R6  Wire unknown outcome -> Failure naming expected outcome set (never
//       silent Success fall-through).
//   R7  Malformed JSON 200 -> Failure with parse error.
//   R8  Connection-refused (HttpRequestException) -> Failure (H13
//       classifies Resumable).
//   R9  HttpClient timeout -> Failure.
//   R10 HTTP 401 -> Failure naming KV secret + vault to check.
//   R11 HTTP 404 -> Failure naming deployment-gap.
//   R12 HTTP 5xx -> Failure (NO in-client retry per read-probe policy).
//   R13 Empty CorrelationId -> Failure BEFORE any HTTP call.
//   R14 Empty TenantId (I1 no-hardcoded-tenant guard) -> Failure BEFORE any
//       HTTP call.
//   R15 Empty shared-secret config -> Failure BEFORE any HTTP call (never
//       proceed with empty X-Sidecar-Auth).
//   R16 KV NotFound -> Failure naming secret + vault.
//   R17 KV Failure -> Failure passing through KV diagnostic.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ExchangePolicySidecarReadClientContractTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string RunId = "01j7q3zp-t4-probe-run";
    private const string PlatformKvVault = "sprk-controlplane-dev-kv";
    private const string PlatformKvSubscription = "22222222-3333-4444-5555-666666666666";
    private const string SharedSecretName = "Sidecar-Shared-Secret";
    private const string SharedSecretValue = "per-boot-shared-secret-value-42";

    // ========== R1 REQUEST SHAPE ==========

    [Fact]
    public async Task R1_Request_IsGetPoliciesWithTenantAndCorrelationQueryAndAuthHeader()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson(WireSuccess(BffAppRegId, UamiClientId)),
        };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        result.Should().BeOfType<ExchangePolicyReadOutcome.Success>();
        handler.CapturedRequests.Should().ContainSingle();
        var captured = handler.CapturedRequests[0];
        captured.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.AbsolutePath.Should().Be(ExchangePolicySidecarReadClient.ReadPoliciesPath);
        captured.RequestUri.Query.Should()
            .Contain($"tenantId={Uri.EscapeDataString(TenantId)}")
            .And.Contain($"correlationId={Uri.EscapeDataString(RunId)}");
        captured.Headers.Should().ContainKey(ExchangePolicySidecarClient.SharedSecretHeaderName);
        captured.Headers[ExchangePolicySidecarClient.SharedSecretHeaderName].Should().Be(SharedSecretValue);
    }

    // ========== R2 WIRE SUCCESS WITH POLICIES ==========

    [Fact]
    public async Task R2_WireSuccess_WithBothExpectedAppIds_MapsToSuccessOutcome()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson(WireSuccess(BffAppRegId, UamiClientId)),
        };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        var success = result.Should().BeOfType<ExchangePolicyReadOutcome.Success>().Subject;
        success.ObservedAppIds.Should().BeEquivalentTo(new[] { BffAppRegId, UamiClientId });
        success.Policies.Should().HaveCount(2);
        success.Policies.Select(p => p.AppId).Should().BeEquivalentTo(new[] { BffAppRegId, UamiClientId });
        success.Policies.All(p => !string.IsNullOrEmpty(p.Description)).Should().BeTrue();
        success.Policies.All(p => !string.IsNullOrEmpty(p.PolicyScopeGroupId)).Should().BeTrue();
    }

    // ========== R3 WIRE SUCCESS EMPTY ==========

    [Fact]
    public async Task R3_WireSuccess_EmptyPolicies_MapsToSuccessWithEmptyLists()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson(WireSuccessEmpty()),
        };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        var success = result.Should().BeOfType<ExchangePolicyReadOutcome.Success>().Subject;
        success.ObservedAppIds.Should().BeEmpty();
        success.Policies.Should().BeEmpty();
    }

    // ========== R4 DEFENSIVE NORMALIZATION (null arrays) ==========

    [Fact]
    public async Task R4_WireSuccess_NullArrays_DefensivelyNormalizesToEmptyLists()
    {
        // Listener.ps1 always emits [] but a proxy/serializer variant could
        // produce null. Client MUST tolerate this rather than throw.
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson("""
                {
                  "outcome": "Success",
                  "observedAppIds": null,
                  "observedCount": 0,
                  "policies": null,
                  "diagnostic": "test-null-arrays"
                }
                """),
        };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        var success = result.Should().BeOfType<ExchangePolicyReadOutcome.Success>().Subject;
        success.ObservedAppIds.Should().BeEmpty();
        success.Policies.Should().BeEmpty();
    }

    // ========== R5 WIRE FAILURE ==========

    [Fact]
    public async Task R5_WireFailure_MapsToFailureOutcome_WithPassThroughDiagnostic()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson("""
                {
                  "outcome": "Failure",
                  "observedAppIds": [],
                  "observedCount": 0,
                  "policies": [],
                  "diagnostic": "Get-ApplicationAccessPolicy failed: Exchange throttling."
                }
                """),
        };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("Get-ApplicationAccessPolicy failed").And.Contain("Exchange throttling");
    }

    // ========== R6 UNKNOWN OUTCOME ==========

    [Fact]
    public async Task R6_UnknownOutcome_MapsToFailure_NamingExpectedOutcomeSet()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => OkJson("""
                {
                  "outcome": "Weird",
                  "observedAppIds": [],
                  "observedCount": 0,
                  "policies": [],
                  "diagnostic": ""
                }
                """),
        };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("unknown outcome 'Weird'")
            .And.Contain(ExchangePolicySidecarReadClient.WireOutcomeSuccess)
            .And.Contain(ExchangePolicySidecarReadClient.WireOutcomeFailure);
    }

    // ========== R7 MALFORMED JSON ==========

    [Fact]
    public async Task R7_MalformedJson_MapsToFailure_WithParseError()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>not json</html>", System.Text.Encoding.UTF8, "application/json"),
            },
        };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("unparseable JSON");
    }

    // ========== R8 CONNECTION REFUSED ==========

    [Fact]
    public async Task R8_ConnectionRefused_MapsToFailure()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => throw new HttpRequestException("Connection refused."),
        };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("transport failure").And.Contain("Connection refused");
    }

    // ========== R9 TIMEOUT ==========

    [Fact]
    public async Task R9_HttpClientTimeout_MapsToFailure()
    {
        var handler = new CapturingHandler
        {
            DelayBeforeResponse = TimeSpan.FromSeconds(30),
            ResponseFactory = _ => OkJson(WireSuccessEmpty()),
        };
        var client = NewClient(handler, configureHttpClient: hc => hc.Timeout = TimeSpan.FromMilliseconds(100));

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("timed out");
    }

    // ========== R10 HTTP 401 ==========

    [Fact]
    public async Task R10_Http401_MapsToFailure_NamingSecretAndVault()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"outcome":"Failure","diagnostic":"Missing or invalid X-Sidecar-Auth header."}"""),
            },
        };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("HTTP 401").And.Contain(SharedSecretName).And.Contain(PlatformKvVault);
    }

    // ========== R11 HTTP 404 ==========

    [Fact]
    public async Task R11_Http404_MapsToFailure_NamingDeploymentGap()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("Unknown route"),
            },
        };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("HTTP 404")
            .And.Contain("deployment gap")
            .And.Contain(ExchangePolicySidecarReadClient.ReadPoliciesPath);
    }

    // ========== R12 HTTP 5xx (NO in-client retry) ==========

    [Fact]
    public async Task R12_Http5xx_MapsToFailure_WithoutInClientRetry()
    {
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom"),
            },
        };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        // Explicitly ONE call -- read client MUST NOT retry per read-probe
        // policy (H13's reconciler owns the retry-vs-quarantine budget).
        handler.CapturedRequests.Should().ContainSingle("read client does not retry 5xx in-client (H13 owns retry)");
        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("HTTP 500");
    }

    // ========== R13 EMPTY CORRELATION ID ==========

    [Fact]
    public async Task R13_EmptyCorrelationId_ReturnsFailure_BeforeAnyHttpCall()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireSuccessEmpty()) };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, CorrelationId: ""), CancellationToken.None);

        handler.CapturedRequests.Should().BeEmpty();
        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("CorrelationId is required");
    }

    // ========== R14 EMPTY TENANT ID (I1 guard) ==========

    [Fact]
    public async Task R14_EmptyTenantId_ReturnsFailure_BeforeAnyHttpCall_I1Guard()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireSuccessEmpty()) };
        var client = NewClient(handler);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId: "", CorrelationId: RunId), CancellationToken.None);

        handler.CapturedRequests.Should().BeEmpty();
        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("TenantId is required").And.Contain("I1");
    }

    // ========== R15 EMPTY SHARED-SECRET CONFIG ==========

    [Fact]
    public async Task R15_EmptySharedSecretConfig_ReturnsFailure_BeforeAnyHttpCall()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireSuccessEmpty()) };
        var options = NewOptions();
        options.SidecarSharedSecretVaultName = "";
        options.SidecarSharedSecretSubscriptionId = "";
        options.SidecarSharedSecretName = "";
        var client = NewClient(handler, options: options);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        handler.CapturedRequests.Should().BeEmpty(
            "silent-fail-audit: missing shared-secret config must fail BEFORE the wire, never proceed with empty X-Sidecar-Auth");
        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("Sidecar shared secret config missing");
    }

    // ========== R16 KV NotFound ==========

    [Fact]
    public async Task R16_SharedSecretKvNotFound_ReturnsFailure_NamingSecretAndVault()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireSuccessEmpty()) };
        var reader = new FakeKvSecretReader { Result = new KvSecretReadResult.NotFound() };
        var client = NewClient(handler, kvReader: reader);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        handler.CapturedRequests.Should().BeEmpty();
        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("not found").And.Contain(SharedSecretName).And.Contain(PlatformKvVault);
    }

    // ========== R17 KV Failure ==========

    [Fact]
    public async Task R17_SharedSecretKvFailure_ReturnsFailure_PassingThroughKvDiagnostic()
    {
        var handler = new CapturingHandler { ResponseFactory = _ => OkJson(WireSuccessEmpty()) };
        var reader = new FakeKvSecretReader
        {
            Result = new KvSecretReadResult.Failure("Access denied (403) reading 'X' on vault 'Y'."),
        };
        var client = NewClient(handler, kvReader: reader);

        var result = await client.ReadAsync(new ExchangePolicyReadRequest(TenantId, RunId), CancellationToken.None);

        handler.CapturedRequests.Should().BeEmpty();
        var failure = result.Should().BeOfType<ExchangePolicyReadOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("Access denied (403)");
    }

    // ---------- helpers ----------

    private static IntegrationWiringOptions NewOptions() => new()
    {
        SidecarBaseUrl = "http://127.0.0.1:8091/",
        SidecarRequestTimeout = TimeSpan.FromMinutes(6),
        SidecarTransientRetryDelay = TimeSpan.FromMilliseconds(100),
        SidecarSharedSecretVaultName = PlatformKvVault,
        SidecarSharedSecretSubscriptionId = PlatformKvSubscription,
        SidecarSharedSecretName = SharedSecretName,
    };

    private static ExchangePolicySidecarReadClient NewClient(
        CapturingHandler handler,
        IntegrationWiringOptions? options = null,
        FakeKvSecretReader? kvReader = null,
        Action<HttpClient>? configureHttpClient = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8091/"),
        };
        configureHttpClient?.Invoke(httpClient);
        return new ExchangePolicySidecarReadClient(
            httpClient,
            kvReader ?? new FakeKvSecretReader { Result = new KvSecretReadResult.Success(SharedSecretValue) },
            Options.Create(options ?? NewOptions()),
            NullLogger<ExchangePolicySidecarReadClient>.Instance);
    }

    private static HttpResponseMessage OkJson(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static string WireSuccess(params string[] appIds)
    {
        var appJson = string.Join(",", appIds.Select(x => $"\"{x}\""));
        var policiesJson = string.Join(",", appIds.Select(a => $$"""
            {
              "appId": "{{a}}",
              "description": "Spaarke-Provisioning-AppAccessPolicy-{{a}}",
              "policyScopeGroupId": "77777777-8888-9999-0000-111111111111"
            }
            """));
        return $$"""
            {
              "outcome": "Success",
              "observedAppIds": [{{appJson}}],
              "observedCount": {{appIds.Length}},
              "policies": [{{policiesJson}}],
              "diagnostic": "Enumerated {{appIds.Length}} policies."
            }
            """;
    }

    private static string WireSuccessEmpty() => """
        {
          "outcome": "Success",
          "observedAppIds": [],
          "observedCount": 0,
          "policies": [],
          "diagnostic": "Enumerated 0 policies."
        }
        """;

    /// <summary>Records every outbound request's headers + body so tests can assert against them.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; init; }
        public TimeSpan? DelayBeforeResponse { get; init; }
        public List<CapturedRequest> CapturedRequests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in request.Headers)
            {
                headers[h.Key] = string.Join(", ", h.Value);
            }
            CapturedRequests.Add(new CapturedRequest(request.RequestUri, request.Method, headers));

            if (DelayBeforeResponse is { } d)
            {
                await Task.Delay(d, cancellationToken).ConfigureAwait(false);
            }
            return ResponseFactory(request);
        }
    }

    private sealed record CapturedRequest(
        Uri? RequestUri,
        HttpMethod Method,
        IReadOnlyDictionary<string, string> Headers);

    private sealed class FakeKvSecretReader : IKvSecretReader
    {
        public required KvSecretReadResult Result { get; init; }

        public Task<KvSecretReadResult> ReadSecretAsync(string vaultName, string subscriptionId, string secretName, CancellationToken cancellationToken)
            => Task.FromResult(Result);
    }
}
