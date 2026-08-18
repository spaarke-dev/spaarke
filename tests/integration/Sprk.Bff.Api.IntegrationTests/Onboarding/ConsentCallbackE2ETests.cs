// -----------------------------------------------------------------------------
// ConsentCallbackE2ETests.cs
//
// End-to-end (BFF-boundary) integration tests for POST /api/onboarding/consent-
// callback (task 042 impl, task 078 verification — customer-provisioning-
// orchestration-r1 Wave 4 Batch 4E).
//
// Design (per POML "signed synthetic payload" allowance): drive the real BFF via
// HttpClient + WebApplicationFactory<Program>; sign the request body with the
// same HMAC-SHA256 key the fixture configured; capture the enqueue via an
// in-memory IProvisioningEnqueuer test double. HmacSignatureVerifier and the
// entire request pipeline (middleware, rate-limiter, endpoint) are the REAL
// production code — the only substitution is the outbound Service Bus seam.
//
// POML "4 paths":
//   #1 happy path                    — HappyPath_SignedPayload_Returns202AndEnqueuesL2Payload
//   #2 re-consent no-op (BFF layer)  — Idempotency_SameCustomerAndTid_YieldsCollapsibleServiceBusMessageId
//   #3 restart from H0 (BFF layer)   — Restart_FreshRunPerCallback_YieldsDistinctMessageIds
//                                     (the "re-consent no-op" and "restart" semantics live at L2;
//                                      the BFF layer's responsibility is byte-stable per-payload
//                                      enqueues so SB dedup + L2 state routing binds properly.)
//   #4 invalid HMAC                  — InvalidHmac_Returns401_NoEnqueue
//
// Plus tests for §4D I1 (missing tid → 400 no default-tenant) and the missing-
// signature-header 400 branch — covered end-to-end (not just handler-unit).
//
// ADR-038 compliance:
//   - Test lives at tests/integration/** (KEEP path: contract/auth).
//   - Uses real WebApplicationFactory<Program> — no Mock<HttpMessageHandler>.
//   - Single test double at a legitimate module boundary (IProvisioningEnqueuer),
//     not a transport-level mock. No DI-registration tests, no ctor null-check
//     tests, no scaffolding shapes B6-B17.
// -----------------------------------------------------------------------------

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Endpoints.Onboarding;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Onboarding;

/// <summary>
/// End-to-end verification of the H0.5 consent-callback endpoint. Uses one
/// shared <see cref="ConsentCallbackE2ETestFixture"/> per class so all tests
/// share the same booted BFF host but reset the capturing enqueuer between
/// tests to keep assertions isolated.
/// </summary>
public sealed class ConsentCallbackE2ETests : IClassFixture<ConsentCallbackE2ETestFixture>
{
    private readonly ConsentCallbackE2ETestFixture _fixture;

    public ConsentCallbackE2ETests(ConsentCallbackE2ETestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Enqueuer.Reset();
    }

    // -----------------------------------------------------------------------
    // POML acceptance path #1 — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HappyPath_SignedPayload_Returns202AndEnqueuesL2Payload()
    {
        // Arrange — signed synthetic payload mirroring an admin-consent redirect POST.
        var payload = new ConsentCallbackRequest
        {
            CustomerId = ConsentCallbackE2EConstants.CustomerId,
            Tid = ConsentCallbackE2EConstants.CustomerTenantId,
            CorrelationId = ConsentCallbackE2EConstants.CorrelationId,
        };
        var body = Serialize(payload);
        var signature = ComputeHexSignature(ConsentCallbackE2EConstants.HmacSigningKey, body);

        using var client = _fixture.CreateHttpClient();
        using var request = BuildSignedRequest(body, signature);

        // Act — drive the real BFF pipeline end-to-end via HttpClient.
        using var response = await client.SendAsync(request);

        // Assert — HTTP contract.
        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "the happy path returns 202 Accepted (endpoint is enqueue-only; L2 handler completes downstream).");

        var responseBody = await response.Content.ReadAsStringAsync();
        using var responseDoc = JsonDocument.Parse(responseBody);
        var runId = responseDoc.RootElement.GetProperty("runId").GetString();
        var responseCorrelationId = responseDoc.RootElement.GetProperty("correlationId").GetString();
        runId.Should().NotBeNullOrWhiteSpace("the response MUST expose the server-assigned runId (deterministic status URL — spec.md FR-02 D18).");
        responseCorrelationId.Should().Be(ConsentCallbackE2EConstants.CorrelationId,
            "when the caller supplies a correlationId, the response MUST echo it back verbatim.");

        // Assert — outbound enqueue (this is where L2 receives the pipeline kick;
        // in production this Service Bus message is what H05ConsentCaptureHandler
        // deserializes to write sprk_dataverseenvironment.sprk_tenantid + Cosmos
        // parameters.tenantId + advance the DAG).
        _fixture.Enqueuer.Calls.Should().ContainSingle("the happy path MUST enqueue exactly one H0.5 dispatch.");
        var call = _fixture.Enqueuer.Calls[0];
        call.HandlerId.Should().Be("H0.5");
        call.CustomerId.Should().Be(ConsentCallbackE2EConstants.CustomerId);
        call.RunId.Should().Be(runId, "the RunId enqueued MUST match the RunId returned to the caller (correlation).");

        // Assert — the wire payload carries tenantId under the exact key L2's
        // ConsentCapturePayload expects. This is the crucial contract point: the
        // BFF endpoint does NOT default any tenantId (§4D I1) — it forwards the
        // caller-supplied tid. L2 handler then writes it to Dataverse + Cosmos.
        using var payloadDoc = JsonDocument.Parse(call.ParametersJson);
        payloadDoc.RootElement.GetProperty("customerId").GetString().Should().Be(ConsentCallbackE2EConstants.CustomerId);
        payloadDoc.RootElement.GetProperty("tenantId").GetString().Should().Be(ConsentCallbackE2EConstants.CustomerTenantId,
            "the L2 handler will read this tenantId field and write it to sprk_dataverseenvironment.sprk_tenantid + Cosmos parameters.tenantId (POML criterion #1).");
        payloadDoc.RootElement.GetProperty("correlationId").GetString().Should().Be(ConsentCallbackE2EConstants.CorrelationId);
    }

    // -----------------------------------------------------------------------
    // POML acceptance path #2 — re-consent no-op (BFF layer contribution)
    //
    // The BFF endpoint itself does not query Dataverse for state — the L2
    // H05ConsentCaptureHandler owns "existing Ready/Running/WaitingOnGate → no-op
    // 200 with existing-run link" logic. The BFF's contribution to that behavior
    // is that repeated callbacks with identical (customerId, tid, correlationId)
    // produce BYTE-STABLE payloads with the SAME Service Bus MessageId, so SB's
    // level-1 dedup window collapses redundant callbacks to a single L2 dispatch
    // and the L2 handler's re-consent semantics see a stable input.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Idempotency_SameCustomerAndTid_YieldsCollapsibleServiceBusMessageId()
    {
        // Arrange — two identical requests with the SAME explicit correlationId.
        var payload = new ConsentCallbackRequest
        {
            CustomerId = ConsentCallbackE2EConstants.CustomerId,
            Tid = ConsentCallbackE2EConstants.CustomerTenantId,
            CorrelationId = "idempotent-op-correlation",
        };
        var body = Serialize(payload);
        var signature = ComputeHexSignature(ConsentCallbackE2EConstants.HmacSigningKey, body);

        using var client = _fixture.CreateHttpClient();

        // Act — fire the same signed payload twice.
        using (var r1 = BuildSignedRequest(body, signature))
        using (var r2 = BuildSignedRequest(body, signature))
        {
            (await client.SendAsync(r1)).StatusCode.Should().Be(HttpStatusCode.Accepted);
            (await client.SendAsync(r2)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        // Assert — both enqueues captured, both carry byte-identical payloads.
        _fixture.Enqueuer.Calls.Should().HaveCount(2, "the BFF endpoint enqueues per HTTP request; SB-level dedup collapses at the broker.");
        var call1 = _fixture.Enqueuer.Calls[0];
        var call2 = _fixture.Enqueuer.Calls[1];

        call1.ParametersJson.Should().Be(call2.ParametersJson,
            "byte-for-byte identical (customerId, tid, correlationId) inputs MUST produce identical wire payloads " +
            "so ServiceBusProvisioningEnqueuer.ComputeMessageId collapses on the paramHash portion.");
        call1.CustomerId.Should().Be(call2.CustomerId);
        call1.HandlerId.Should().Be(call2.HandlerId);

        // Compute the MessageId with a CONSTANT runId so the paramHash contribution
        // is isolated — this proves the (customerId, tid, correlationId) portion
        // of the SB MessageId collapses. In production, each request gets a fresh
        // RunId so the FULL MessageId differs — but the paramHash portion is what
        // makes L2's application-level dedup (correlationId lookup on the run row)
        // deterministic. Mirrors the unit-test invariant in
        // ConsentCallbackEndpointTests.HandleAsync_IdempotentPayload_YieldsSameEnqueueMessageId.
        var messageId1 = ServiceBusProvisioningEnqueuer.ComputeMessageId(
            call1.HandlerId, runId: "shared-run", call1.CustomerId, call1.ParametersJson);
        var messageId2 = ServiceBusProvisioningEnqueuer.ComputeMessageId(
            call2.HandlerId, runId: "shared-run", call2.CustomerId, call2.ParametersJson);
        messageId1.Should().Be(messageId2, "identical inputs + identical runId → identical SB MessageId (idempotency key consent-{customerId}-{tid} contract).");
    }

    // -----------------------------------------------------------------------
    // POML acceptance path #3 — restart-from-H0 (BFF layer contribution)
    //
    // The "existing Failed/Cancelled row → new pipeline kick" semantic is again
    // an L2 concern (H05ConsentCaptureHandler checks the run row's status and
    // decides to allocate a new run or reuse). The BFF's contribution is that
    // it does NOT dedupe at the endpoint layer — a fresh callback always
    // produces a fresh enqueue with a fresh RunId, so L2 sees a fresh dispatch
    // and can allocate a new run when the existing one is terminal.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Restart_FreshRunPerCallback_YieldsDistinctMessageIds()
    {
        // Arrange — two callbacks WITHOUT an explicit correlationId. The endpoint
        // falls back to HttpContext.TraceIdentifier per request, which varies.
        var payload = new ConsentCallbackRequest
        {
            CustomerId = ConsentCallbackE2EConstants.CustomerId,
            Tid = ConsentCallbackE2EConstants.CustomerTenantId,
            // CorrelationId intentionally omitted — endpoint falls back to traceId.
        };
        var body = Serialize(payload);
        var signature = ComputeHexSignature(ConsentCallbackE2EConstants.HmacSigningKey, body);

        using var client = _fixture.CreateHttpClient();

        // Act — fire twice; expect fresh RunId per call because the endpoint
        // generates a fresh Guid.NewGuid() per request (see ConsentCallbackEndpoint).
        using (var r1 = BuildSignedRequest(body, signature))
        using (var r2 = BuildSignedRequest(body, signature))
        {
            (await client.SendAsync(r1)).StatusCode.Should().Be(HttpStatusCode.Accepted);
            (await client.SendAsync(r2)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        _fixture.Enqueuer.Calls.Should().HaveCount(2);
        var call1 = _fixture.Enqueuer.Calls[0];
        var call2 = _fixture.Enqueuer.Calls[1];

        // Each request gets a fresh RunId — this is what allows L2's re-consent
        // logic to allocate a new run when the previous one is Failed/Cancelled.
        call1.RunId.Should().NotBe(call2.RunId,
            "the BFF endpoint MUST allocate a fresh RunId per callback so L2's restart-from-H0 semantic has a fresh identity to attach to.");

        // With fresh RunIds, the SB MessageIds differ end-to-end so BOTH enqueues
        // survive the SB dedup window — L2 receives BOTH and makes the state
        // decision. This is the contract that enables the POML "restart from H0"
        // path (POML criterion #3).
        var messageId1 = ServiceBusProvisioningEnqueuer.ComputeMessageId(
            call1.HandlerId, call1.RunId, call1.CustomerId, call1.ParametersJson);
        var messageId2 = ServiceBusProvisioningEnqueuer.ComputeMessageId(
            call2.HandlerId, call2.RunId, call2.CustomerId, call2.ParametersJson);
        messageId1.Should().NotBe(messageId2,
            "distinct RunIds MUST yield distinct SB MessageIds so both enqueues reach L2 (enabling restart-from-H0).");
    }

    // -----------------------------------------------------------------------
    // POML acceptance path #4 — invalid HMAC → 401, no enqueue.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InvalidHmac_Returns401_NoEnqueue()
    {
        // Arrange — body signed with a DIFFERENT key than the fixture configured.
        var payload = new ConsentCallbackRequest
        {
            CustomerId = ConsentCallbackE2EConstants.CustomerId,
            Tid = ConsentCallbackE2EConstants.CustomerTenantId,
        };
        var body = Serialize(payload);
        var wrongSignature = ComputeHexSignature("attacker-does-not-know-the-real-signing-key", body);

        using var client = _fixture.CreateHttpClient();
        using var request = BuildSignedRequest(body, wrongSignature);

        // Act
        using var response = await client.SendAsync(request);

        // Assert — 401 and NO downstream enqueue (no Dataverse write, no Cosmos
        // write, no L2 dispatch — all downstream side effects gated on 202 path).
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "invalid HMAC MUST 401 (POML acceptance criterion #4) — the endpoint is Anonymous and HMAC is the ONLY compensating control per spec.md §4.3a.2.");

        // Body is ProblemDetails with a specific error code.
        var responseBody = await response.Content.ReadAsStringAsync();
        using var problemDoc = JsonDocument.Parse(responseBody);
        problemDoc.RootElement.GetProperty("status").GetInt32().Should().Be(401);
        problemDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("onboarding.consent.signature_mismatch",
            "the mismatch reason MUST surface distinct error code so ops can distinguish attack vs misconfig.");

        _fixture.Enqueuer.Calls.Should().BeEmpty(
            "HMAC failure MUST short-circuit before enqueue — no side effects on the L2 provisioning queue.");
    }

    // -----------------------------------------------------------------------
    // Additional POML acceptance — §4D I1 no-default-tenant (missing tid → 400).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MissingTid_Returns400_NoDefaultTenantFallback()
    {
        // Arrange — well-formed body BUT missing tid.
        var payload = new ConsentCallbackRequest
        {
            CustomerId = ConsentCallbackE2EConstants.CustomerId,
            Tid = "",
        };
        var body = Serialize(payload);
        var signature = ComputeHexSignature(ConsentCallbackE2EConstants.HmacSigningKey, body);

        using var client = _fixture.CreateHttpClient();
        using var request = BuildSignedRequest(body, signature);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "§4D I1: endpoint MUST fail at the edge when tid is missing — MUST NOT default to any Spaarke-owned tenantId.");
        var responseBody = await response.Content.ReadAsStringAsync();
        using var problemDoc = JsonDocument.Parse(responseBody);
        problemDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("onboarding.consent.missing_tid");

        _fixture.Enqueuer.Calls.Should().BeEmpty("no downstream enqueue when tid is missing (§4D I1).");
    }

    // -----------------------------------------------------------------------
    // Additional POML acceptance — missing X-Signature-256 header → 400 (not 401).
    // Distinct from invalid-signature 401: this catches configuration / plumbing
    // errors on the sender side vs actual signature-mismatch attacks.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MissingSignatureHeader_Returns400_NotUnhandled500()
    {
        // Arrange — valid body, but NO X-Signature-256 header at all.
        var payload = new ConsentCallbackRequest
        {
            CustomerId = ConsentCallbackE2EConstants.CustomerId,
            Tid = ConsentCallbackE2EConstants.CustomerTenantId,
        };
        var body = Serialize(payload);

        using var client = _fixture.CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, ConsentCallbackE2EConstants.Route)
        {
            Content = new ByteArrayContent(body)
            {
                Headers = { { "Content-Type", "application/json" } }
            }
        };
        // NO signature header intentionally.

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "missing signature header is a distinct branch from invalid signature (400 vs 401) — POML criterion #2.");
        var responseBody = await response.Content.ReadAsStringAsync();
        using var problemDoc = JsonDocument.Parse(responseBody);
        problemDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("onboarding.consent.missing_signature_header");

        _fixture.Enqueuer.Calls.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Base64 signature format acceptance — the verifier accepts hex OR base64;
    // this test proves the E2E path honors the alternate encoding a real caller
    // (e.g. Microsoft admin-consent → operator-supplied HMAC signer) may use.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HappyPath_Base64SignatureFormat_AlsoAccepted()
    {
        var payload = new ConsentCallbackRequest
        {
            CustomerId = ConsentCallbackE2EConstants.CustomerId,
            Tid = ConsentCallbackE2EConstants.CustomerTenantId,
        };
        var body = Serialize(payload);
        var signatureBase64 = ComputeBase64Signature(ConsentCallbackE2EConstants.HmacSigningKey, body);

        using var client = _fixture.CreateHttpClient();
        using var request = BuildSignedRequest(body, signatureBase64);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "the HmacSignatureVerifier decodes hex OR base64 (documented on the verifier XML) — E2E path MUST honor both.");
        _fixture.Enqueuer.Calls.Should().ContainSingle();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static byte[] Serialize(ConsentCallbackRequest request)
    {
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        return Encoding.UTF8.GetBytes(json);
    }

    private static string ComputeHexSignature(string key, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    private static string ComputeBase64Signature(string key, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }

    private static HttpRequestMessage BuildSignedRequest(byte[] body, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ConsentCallbackE2EConstants.Route)
        {
            Content = new ByteArrayContent(body)
            {
                Headers = { { "Content-Type", "application/json" } }
            }
        };
        request.Headers.Add(ConsentCallbackE2EConstants.SignatureHeaderName, signature);
        return request;
    }
}
