using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Sprk.Bff.Api.Endpoints.Onboarding;

/// <summary>
/// H0.5 consent-callback endpoint (task 042 — customer-provisioning-
/// orchestration-r1). Anonymous+HMAC-verified POST that captures the
/// customer admin's <c>tid</c> from the Microsoft admin-consent redirect
/// and enqueues the L2 provisioning pipeline.
///
/// <para>
/// Placement Justification (CLAUDE.md §10 + spec.md §10 ADR-013 row): this
/// is the ONE BFF endpoint that MUST be Anonymous — the customer admin is
/// authenticating to THEIR tenant via the Microsoft consent flow, not to
/// Spaarke's control plane. HMAC-over-the-body is the compensating control
/// (§4.3a.2). Consumes NO AI-internal types (ADR-013 forcing-function rule).
/// </para>
///
/// <para>
/// Route: <c>POST /api/onboarding/consent-callback</c>. Returns:
/// <list type="bullet">
/// <item><b>202 Accepted</b> with <see cref="ConsentCallbackResponse"/> on happy path (POML criterion #1).</item>
/// <item><b>400 Bad Request</b> when the signature header is missing (POML criterion #2).</item>
/// <item><b>400 Bad Request</b> when the body is missing customerId or tid (§4D I1 no-default-tenant).</item>
/// <item><b>401 Unauthorized</b> when the signature is invalid, malformed, or the server has no key (POML criterion #3).</item>
/// </list>
/// </para>
///
/// <para>
/// Idempotency: the endpoint enqueues via <see cref="IProvisioningEnqueuer"/>
/// whose MessageId is deterministic per (H0.5, RunId, CustomerId, paramHash).
/// The RunId is generated fresh per request BUT the paramHash collapses over
/// (customerId, tid) — the SAME (customerId, tid) always maps to the SAME
/// MessageId regardless of RunId when the enqueue payload is identical.
/// Two callbacks with the same customerId+tid within the SB dedup window
/// collapse to a single downstream H0.5 dispatch.
/// </para>
/// </summary>
public static class ConsentCallbackEndpoint
{
    /// <summary>Endpoint route.</summary>
    public const string Route = "/api/onboarding/consent-callback";

    /// <summary>Handler identifier the endpoint enqueues (must equal <c>H0.5</c>).</summary>
    public const string HandlerId = "H0.5";

    // Shared serializer options — camelCase in/out (JSON convention parity
    // with rest of BFF), case-insensitive property matching so callers can
    // send the header case they want (`tid` vs `Tid`).
    private static readonly JsonSerializerOptions RequestReaderOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions PayloadWriterOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>Maps <c>POST /api/onboarding/consent-callback</c> onto the application.</summary>
    public static IEndpointRouteBuilder MapConsentCallbackEndpoint(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(Route, HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting("anonymous")
            .WithName("ConsentCallback")
            .WithTags("Onboarding")
            .WithSummary("H0.5 consent-callback — captures customer admin tid after Microsoft admin-consent grant.")
            .WithDescription("Anonymous+HMAC-verified. Validates signature over raw body, extracts customerId + tid, " +
                             "generates a runId, enqueues the L2 pipeline via Service Bus, returns 202 Accepted with runId.")
            .Produces<ConsentCallbackResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// Endpoint handler. Exposed <c>public</c> for direct unit-test
    /// invocation (POML step 8 acceptance).
    /// </summary>
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        HmacSignatureVerifier signatureVerifier,
        IProvisioningEnqueuer enqueuer,
        Microsoft.Extensions.Options.IOptionsMonitor<OnboardingOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(signatureVerifier);
        ArgumentNullException.ThrowIfNull(enqueuer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger(typeof(ConsentCallbackEndpoint).FullName!);
        var traceId = httpContext.TraceIdentifier;
        var opts = options.CurrentValue;

        // -------- Read raw body (needed for HMAC verify + deserialization) --------
        httpContext.Request.EnableBuffering();
        httpContext.Request.Body.Position = 0;
        byte[] bodyBytes;
        using (var ms = new MemoryStream())
        {
            await httpContext.Request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            bodyBytes = ms.ToArray();
        }
        httpContext.Request.Body.Position = 0;

        // -------- Signature header presence check (POML criterion #2 → 400) --------
        var signatureHeader = httpContext.Request.Headers[opts.SignatureHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            logger.LogWarning(
                "ConsentCallback: missing required signature header '{Header}'. TraceId={TraceId}",
                opts.SignatureHeaderName, traceId);
            return Results.Problem(
                title: "Bad Request",
                detail: $"Missing required signature header '{opts.SignatureHeaderName}'.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "onboarding.consent.missing_signature_header",
                    ["correlationId"] = traceId,
                });
        }

        // -------- HMAC verify (POML criterion #3 → 401 on mismatch/malformed/no-key) --------
        var verifyResult = signatureVerifier.Verify(bodyBytes, signatureHeader);
        if (verifyResult != HmacSignatureVerifyResult.Valid)
        {
            logger.LogWarning(
                "ConsentCallback: signature verification failed with result {Result}. TraceId={TraceId}",
                verifyResult, traceId);
            return Results.Problem(
                title: "Unauthorized",
                detail: verifyResult switch
                {
                    HmacSignatureVerifyResult.MalformedSignature => "Signature header value is malformed (expected hex or base64 SHA-256).",
                    HmacSignatureVerifyResult.KeyNotConfigured => "Consent-callback signing key is not configured on this server.",
                    HmacSignatureVerifyResult.SignatureMismatch => "Consent-callback signature does not match the request body.",
                    _ => "Signature verification failed.",
                },
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = verifyResult switch
                    {
                        HmacSignatureVerifyResult.MalformedSignature => "onboarding.consent.malformed_signature",
                        HmacSignatureVerifyResult.KeyNotConfigured => "onboarding.consent.signing_key_not_configured",
                        HmacSignatureVerifyResult.SignatureMismatch => "onboarding.consent.signature_mismatch",
                        _ => "onboarding.consent.signature_invalid",
                    },
                    ["correlationId"] = traceId,
                });
        }

        // -------- Deserialize request --------
        ConsentCallbackRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ConsentCallbackRequest>(bodyBytes, RequestReaderOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "ConsentCallback: request body deserialization failed. TraceId={TraceId}", traceId);
            return Results.Problem(
                title: "Bad Request",
                detail: "Request body could not be parsed as JSON.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "onboarding.consent.malformed_body",
                    ["correlationId"] = traceId,
                });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.CustomerId))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "Field 'customerId' is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "onboarding.consent.missing_customer_id",
                    ["correlationId"] = traceId,
                });
        }

        if (string.IsNullOrWhiteSpace(request.Tid))
        {
            // §4D I1: no hardcoded default tenant. Fail here rather than
            // enqueue an H0.5 dispatch with an empty tid (which the L2
            // handler would reject anyway — better to fail fast at the edge).
            return Results.Problem(
                title: "Bad Request",
                detail: "Field 'tid' (customer tenant id) is required (§4D I1 — no default-tenant fallback).",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "onboarding.consent.missing_tid",
                    ["correlationId"] = traceId,
                });
        }

        // -------- Enqueue + return 202 --------
        var runId = Guid.NewGuid().ToString("N");
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? traceId : request.CorrelationId!;

        // Payload wire-shape MIRRORS L2's ConsentCapturePayload verbatim so
        // H05ConsentCaptureHandler.ParametersJson deserialization succeeds
        // without a translation shim (BFF cannot compile-reference the L2
        // assembly — the schema is contract, kept in sync by design.md §6.2).
        var payloadJson = JsonSerializer.Serialize(new
        {
            customerId = request.CustomerId,
            tenantId = request.Tid,
            correlationId,
        }, PayloadWriterOptions);

        try
        {
            await enqueuer.EnqueueAsync(
                handlerId: HandlerId,
                runId: runId,
                customerId: request.CustomerId!,
                parametersJson: payloadJson,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ConsentCallback: enqueue failed for CustomerId={CustomerId} RunId={RunId} TraceId={TraceId}",
                request.CustomerId, runId, traceId);
            return Results.Problem(
                title: "Internal Server Error",
                detail: "Failed to enqueue the consent-capture dispatch.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "onboarding.consent.enqueue_failed",
                    ["correlationId"] = traceId,
                });
        }

        logger.LogInformation(
            "ConsentCallback: enqueued H0.5 dispatch. CustomerId={CustomerId} RunId={RunId} " +
            "CorrelationId={CorrelationId} TraceId={TraceId}",
            request.CustomerId, runId, correlationId, traceId);

        return Results.Accepted(
            uri: null,
            value: new ConsentCallbackResponse(
                RunId: runId,
                CorrelationId: correlationId,
                Message: "Consent captured; provisioning pipeline enqueued."));
    }
}
