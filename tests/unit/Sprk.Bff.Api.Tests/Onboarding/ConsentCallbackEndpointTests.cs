// -----------------------------------------------------------------------------
// ConsentCallbackEndpointTests.cs
//
// Unit tests for the H0.5 consent-callback endpoint (task 042 — customer-
// provisioning-orchestration-r1). Exercises the endpoint handler directly
// with a fake HttpContext + stubbed dependencies. Covers all six POML
// acceptance-criteria branches on the BFF side:
//
//   #1 (happy path)     — valid HMAC + valid body → 202 Accepted with runId
//   #2 (missing sig)    — no X-Signature-256 header → 400 Bad Request (not 500)
//   #3 (invalid HMAC)   — bad signature → 401 Unauthorized
//   #4 (§4D I1)         — missing tid → 400 Bad Request (no default-tenant fallback)
//   plus: idempotency — same (customerId, tid) → same enqueue MessageId
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Endpoints.Onboarding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Onboarding;

public sealed class ConsentCallbackEndpointTests
{
    private const string ValidKey = "test-signing-key-42-chars-of-shared-secret";
    private const string CustomerId = "acme-corp";
    private const string Tid = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task HandleAsync_ValidSignatureAndBody_Returns202WithRunId()
    {
        // Arrange — POML acceptance #1
        var body = SerializeRequest(new ConsentCallbackRequest { CustomerId = CustomerId, Tid = Tid });
        var sig = ComputeHexSignature(ValidKey, body);
        var enqueuer = new CapturingEnqueuer();

        var (ctx, opts) = NewHttpContextAndOptions(body, signatureHeader: sig, key: ValidKey);
        var verifier = NewVerifier(opts.CurrentValue);

        // Act
        var result = await ConsentCallbackEndpoint.HandleAsync(
            ctx, verifier, enqueuer, opts,
            NullLoggerFactory.Instance, CancellationToken.None);

        // Assert
        var accepted = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        accepted.StatusCode.Should().Be(StatusCodes.Status202Accepted);

        var valueResult = result.Should().BeAssignableTo<IValueHttpResult>().Subject;
        var response = valueResult.Value.Should().BeOfType<ConsentCallbackResponse>().Subject;
        response.RunId.Should().NotBeNullOrWhiteSpace();
        response.CorrelationId.Should().NotBeNullOrWhiteSpace();

        enqueuer.Calls.Should().ContainSingle();
        var call = enqueuer.Calls[0];
        call.HandlerId.Should().Be("H0.5");
        call.CustomerId.Should().Be(CustomerId);
        call.RunId.Should().Be(response.RunId, "the RunId in the 202 response body MUST equal the RunId used for the enqueue.");

        // Payload wire-shape MUST carry tenantId (matches L2's ConsentCapturePayload contract).
        using var doc = JsonDocument.Parse(call.ParametersJson);
        doc.RootElement.GetProperty("tenantId").GetString().Should().Be(Tid);
        doc.RootElement.GetProperty("customerId").GetString().Should().Be(CustomerId);
    }

    [Fact]
    public async Task HandleAsync_MissingSignatureHeader_Returns400WithDistinctErrorCode_NotUnhandled500()
    {
        // POML acceptance #2 — missing → 400 (distinct code), NOT 500
        var body = SerializeRequest(new ConsentCallbackRequest { CustomerId = CustomerId, Tid = Tid });
        var (ctx, opts) = NewHttpContextAndOptions(body, signatureHeader: null, key: ValidKey);
        var verifier = NewVerifier(opts.CurrentValue);
        var enqueuer = new CapturingEnqueuer();

        var result = await ConsentCallbackEndpoint.HandleAsync(
            ctx, verifier, enqueuer, opts,
            NullLoggerFactory.Instance, CancellationToken.None);

        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        enqueuer.Calls.Should().BeEmpty("no downstream enqueue when the signature header is missing.");
    }

    [Fact]
    public async Task HandleAsync_InvalidSignature_Returns401_NotUnhandledException()
    {
        // POML acceptance #3 — invalid HMAC → 401 (verifier failure)
        var body = SerializeRequest(new ConsentCallbackRequest { CustomerId = CustomerId, Tid = Tid });
        var wrongSig = ComputeHexSignature("a-completely-different-key", body);
        var (ctx, opts) = NewHttpContextAndOptions(body, signatureHeader: wrongSig, key: ValidKey);
        var verifier = NewVerifier(opts.CurrentValue);
        var enqueuer = new CapturingEnqueuer();

        var result = await ConsentCallbackEndpoint.HandleAsync(
            ctx, verifier, enqueuer, opts,
            NullLoggerFactory.Instance, CancellationToken.None);

        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        enqueuer.Calls.Should().BeEmpty("no downstream enqueue when the signature is invalid.");
    }

    [Fact]
    public async Task HandleAsync_MissingTid_Returns400_NoDefaultTenantFallback()
    {
        // POML acceptance #4 (§4D I1) — missing tid → 400, no downstream enqueue.
        var body = SerializeRequest(new ConsentCallbackRequest { CustomerId = CustomerId, Tid = "" });
        var sig = ComputeHexSignature(ValidKey, body);
        var (ctx, opts) = NewHttpContextAndOptions(body, signatureHeader: sig, key: ValidKey);
        var verifier = NewVerifier(opts.CurrentValue);
        var enqueuer = new CapturingEnqueuer();

        var result = await ConsentCallbackEndpoint.HandleAsync(
            ctx, verifier, enqueuer, opts,
            NullLoggerFactory.Instance, CancellationToken.None);

        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        enqueuer.Calls.Should().BeEmpty("no downstream enqueue when tid is missing (§4D I1).");
    }

    [Fact]
    public async Task HandleAsync_MissingCustomerId_Returns400()
    {
        var body = SerializeRequest(new ConsentCallbackRequest { CustomerId = "", Tid = Tid });
        var sig = ComputeHexSignature(ValidKey, body);
        var (ctx, opts) = NewHttpContextAndOptions(body, signatureHeader: sig, key: ValidKey);
        var verifier = NewVerifier(opts.CurrentValue);
        var enqueuer = new CapturingEnqueuer();

        var result = await ConsentCallbackEndpoint.HandleAsync(
            ctx, verifier, enqueuer, opts,
            NullLoggerFactory.Instance, CancellationToken.None);

        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        enqueuer.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_MalformedJsonBody_Returns400()
    {
        // Body is real (non-empty) bytes so HMAC compare succeeds; JSON parse fails downstream.
        var body = Encoding.UTF8.GetBytes("{ not-json");
        var sig = ComputeHexSignature(ValidKey, body);
        var (ctx, opts) = NewHttpContextAndOptions(body, signatureHeader: sig, key: ValidKey);
        var verifier = NewVerifier(opts.CurrentValue);
        var enqueuer = new CapturingEnqueuer();

        var result = await ConsentCallbackEndpoint.HandleAsync(
            ctx, verifier, enqueuer, opts,
            NullLoggerFactory.Instance, CancellationToken.None);

        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task HandleAsync_IdempotentPayload_YieldsSameEnqueueMessageId()
    {
        // Two identical requests with the SAME explicit correlationId → the
        // payload-hash portion of the MessageId collapses. (When correlationId
        // is unspecified the endpoint falls back to HttpContext.TraceIdentifier
        // which varies per request — that's by design; callers wanting
        // cross-request dedup MUST send an explicit correlationId.)
        var body = SerializeRequest(new ConsentCallbackRequest
        {
            CustomerId = CustomerId,
            Tid = Tid,
            CorrelationId = "op-correlation-42",
        });
        var sig = ComputeHexSignature(ValidKey, body);

        var enqueuer1 = new CapturingEnqueuer();
        var enqueuer2 = new CapturingEnqueuer();

        var (ctx1, opts1) = NewHttpContextAndOptions(body, signatureHeader: sig, key: ValidKey);
        var (ctx2, opts2) = NewHttpContextAndOptions(body, signatureHeader: sig, key: ValidKey);
        var verifier1 = NewVerifier(opts1.CurrentValue);
        var verifier2 = NewVerifier(opts2.CurrentValue);

        var r1 = await ConsentCallbackEndpoint.HandleAsync(ctx1, verifier1, enqueuer1, opts1, NullLoggerFactory.Instance, CancellationToken.None);
        var r2 = await ConsentCallbackEndpoint.HandleAsync(ctx2, verifier2, enqueuer2, opts2, NullLoggerFactory.Instance, CancellationToken.None);

        r1.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        r2.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status202Accepted);

        enqueuer1.Calls.Should().ContainSingle();
        enqueuer2.Calls.Should().ContainSingle();

        // Each enqueue uses a distinct RunId (fresh GUID per request), so the
        // final MessageIds differ. But holding RunId constant and computing
        // over the (customerId, parametersJson) portion MUST yield the same
        // hash — proving the payload-hash portion of level-1 dedup collapses.
        var call1 = enqueuer1.Calls[0];
        var call2 = enqueuer2.Calls[0];
        call1.ParametersJson.Should().Be(call2.ParametersJson,
            "identical (customerId, tid, correlationId) inputs MUST yield byte-identical payload JSON.");

        var mid1 = ServiceBusProvisioningEnqueuer.ComputeMessageId(
            call1.HandlerId, "shared-run", call1.CustomerId, call1.ParametersJson);
        var mid2 = ServiceBusProvisioningEnqueuer.ComputeMessageId(
            call2.HandlerId, "shared-run", call2.CustomerId, call2.ParametersJson);
        mid1.Should().Be(mid2, "payload-hash portion of the MessageId MUST collapse for identical inputs.");
    }

    // ---------------------------------------------------------------------
    // Helpers + stubs
    // ---------------------------------------------------------------------

    private static byte[] SerializeRequest(ConsentCallbackRequest request)
    {
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        return Encoding.UTF8.GetBytes(json);
    }

    private static string ComputeHexSignature(string key, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    private static HmacSignatureVerifier NewVerifier(OnboardingOptions options)
    {
        return new HmacSignatureVerifier(new StaticOptionsMonitor<OnboardingOptions>(options));
    }

    private static (HttpContext Context, IOptionsMonitor<OnboardingOptions> Options)
        NewHttpContextAndOptions(byte[] body, string? signatureHeader, string key)
    {
        var options = new OnboardingOptions
        {
            HmacSigningKey = key,
            QueueName = "sprk-provisioning-jobs",
            SignatureHeaderName = "X-Signature-256",
        };
        var monitor = new StaticOptionsMonitor<OnboardingOptions>(options);

        var ctx = new DefaultHttpContext();
        ctx.TraceIdentifier = $"trace-{Guid.NewGuid():N}";
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream(body);
        ctx.Request.ContentLength = body.LongLength;
        if (!string.IsNullOrWhiteSpace(signatureHeader))
        {
            ctx.Request.Headers[options.SignatureHeaderName] = signatureHeader;
        }

        return (ctx, monitor);
    }

    private sealed class CapturingEnqueuer : IProvisioningEnqueuer
    {
        public List<EnqueueCall> Calls { get; } = new();

        public Task EnqueueAsync(
            string handlerId, string runId, string customerId, string parametersJson,
            CancellationToken cancellationToken)
        {
            Calls.Add(new EnqueueCall(handlerId, runId, customerId, parametersJson));
            return Task.CompletedTask;
        }

        public sealed record EnqueueCall(string HandlerId, string RunId, string CustomerId, string ParametersJson);
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;
        public StaticOptionsMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable OnChange(Action<T, string?> listener) => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
