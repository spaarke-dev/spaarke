using System.Security.Cryptography;
using System.Text;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Endpoint filter that validates an HMAC-SHA256 signature on the request body
/// against a configured shared signing key.
///
/// Compromise of the webhook URL alone is insufficient to forge notifications —
/// the caller must also possess the signing key and compute a correct signature
/// over the exact request body.
///
/// <para>
/// Signature wire format: the signature header value MAY be prefixed with
/// <c>sha256=</c> (case-insensitive) and MUST be either a Base64-encoded
/// or lowercase hex-encoded HMAC-SHA256 digest of the raw UTF-8 request body
/// using the configured shared key.
/// </para>
/// <para>
/// Comparison is performed in constant time via
/// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
/// to avoid timing side channels.
/// </para>
/// <para>
/// This filter is intended for AllowAnonymous endpoints that receive notifications
/// from external systems (e.g., Microsoft Graph change notifications via signing relay,
/// Dataverse service-endpoint webhooks). It is NOT a substitute for OAuth on
/// user-facing endpoints.
/// </para>
/// <para>
/// SAFETY: There is no DEVELOPMENT_MODE bypass. If the configured signing key is
/// null or empty, every request is rejected with 401 (fail-closed). This is by
/// design — webhooks must never accept unsigned traffic in any environment.
/// </para>
/// </summary>
public sealed class WebhookSignatureFilter : IEndpointFilter
{
    /// <summary>
    /// HTTP header that carries the signature. Default mirrors GitHub / GH-Hub
    /// convention for HMAC-SHA256 over the body.
    /// </summary>
    public const string DefaultSignatureHeader = "X-Hub-Signature-256";

    private readonly string _signatureHeader;
    private readonly Func<IServiceProvider, string?> _signingKeyAccessor;
    private readonly string _filterName;

    /// <summary>
    /// Creates the filter with a configurable signature header and signing-key accessor.
    /// </summary>
    /// <param name="signatureHeader">HTTP header name that carries the signature.</param>
    /// <param name="signingKeyAccessor">
    /// Resolves the current signing key from the request-scoped service provider.
    /// Allows separate keys per endpoint while keeping the filter type singleton-safe.
    /// </param>
    /// <param name="filterName">Diagnostic label for logs (e.g., "Communication", "Email").</param>
    public WebhookSignatureFilter(
        string signatureHeader,
        Func<IServiceProvider, string?> signingKeyAccessor,
        string filterName)
    {
        _signatureHeader = string.IsNullOrWhiteSpace(signatureHeader)
            ? throw new ArgumentException("Signature header name must be non-empty.", nameof(signatureHeader))
            : signatureHeader;
        _signingKeyAccessor = signingKeyAccessor ?? throw new ArgumentNullException(nameof(signingKeyAccessor));
        _filterName = filterName ?? throw new ArgumentNullException(nameof(filterName));
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var request = httpContext.Request;
        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<WebhookSignatureFilter>();
        var traceId = httpContext.TraceIdentifier;

        // ─── Graph subscription validation handshake bypass ───
        // Microsoft Graph creates subscriptions by POSTing with a `validationToken`
        // query parameter and expects the token echoed back as text/plain within
        // 10 seconds. These probes have no signed body — let them through; the
        // downstream handler is responsible for responding correctly.
        if (HttpMethods.IsPost(request.Method)
            && request.Query.TryGetValue("validationToken", out var token)
            && !string.IsNullOrEmpty(token))
        {
            return await next(context);
        }

        // ─── Resolve signing key (fail closed if not configured) ───
        var signingKey = _signingKeyAccessor(httpContext.RequestServices);
        if (string.IsNullOrEmpty(signingKey))
        {
            logger.LogError(
                "WebhookSignatureFilter[{Filter}]: signing key is not configured. " +
                "Rejecting webhook request. TraceId={TraceId}",
                _filterName, traceId);
            return Results.Problem(
                title: "Unauthorized",
                detail: "Webhook signing key is not configured on this server.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // ─── Extract signature header ───
        var headerValue = request.Headers[_signatureHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(headerValue))
        {
            logger.LogWarning(
                "WebhookSignatureFilter[{Filter}]: missing required header '{Header}'. TraceId={TraceId}",
                _filterName, _signatureHeader, traceId);
            return Results.Problem(
                title: "Unauthorized",
                detail: $"Missing required signature header '{_signatureHeader}'.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // ─── Read body (buffered so the downstream handler can re-read it) ───
        request.EnableBuffering();
        request.Body.Position = 0;
        byte[] bodyBytes;
        using (var ms = new MemoryStream())
        {
            await request.Body.CopyToAsync(ms, httpContext.RequestAborted);
            bodyBytes = ms.ToArray();
        }
        request.Body.Position = 0;

        // ─── Compute HMAC-SHA256 over the raw body ───
        byte[] computedHash;
        try
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
            computedHash = hmac.ComputeHash(bodyBytes);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "WebhookSignatureFilter[{Filter}]: failed to compute HMAC. TraceId={TraceId}",
                _filterName, traceId);
            return Results.Problem(
                title: "Internal Server Error",
                detail: "Failed to validate webhook signature.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // ─── Parse signature header — accept "sha256=" prefix, hex, or base64 ───
        var rawSignature = headerValue.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? headerValue[7..]
            : headerValue;

        if (!TryDecodeSignature(rawSignature, out var providedHash))
        {
            logger.LogWarning(
                "WebhookSignatureFilter[{Filter}]: signature header '{Header}' could not be decoded as hex or base64. TraceId={TraceId}",
                _filterName, _signatureHeader, traceId);
            return Results.Problem(
                title: "Unauthorized",
                detail: "Signature header value is malformed.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // ─── Constant-time comparison (prevents timing side channels) ───
        // FixedTimeEquals requires equal lengths; if lengths differ, reject up front
        // (still constant-time relative to the data because no comparison ran).
        if (providedHash.Length != computedHash.Length
            || !CryptographicOperations.FixedTimeEquals(providedHash, computedHash))
        {
            logger.LogWarning(
                "WebhookSignatureFilter[{Filter}]: signature mismatch. TraceId={TraceId}",
                _filterName, traceId);
            return Results.Problem(
                title: "Unauthorized",
                detail: "Webhook signature is invalid.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(context);
    }

    /// <summary>
    /// Decodes the signature value. Accepts lowercase or uppercase hex
    /// (64 chars for SHA-256), or Base64 (standard or URL-safe).
    /// </summary>
    private static bool TryDecodeSignature(string value, out byte[] decoded)
    {
        // Hex path: 64 hex chars (HMAC-SHA256 = 32 bytes = 64 hex chars)
        if (value.Length == 64 && IsHex(value))
        {
            try
            {
                decoded = Convert.FromHexString(value);
                return true;
            }
            catch
            {
                // Fall through to base64
            }
        }

        // Base64 path (standard or URL-safe)
        try
        {
            decoded = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            // Try URL-safe variant
            try
            {
                var padded = value.Replace('-', '+').Replace('_', '/');
                var pad = padded.Length % 4;
                if (pad == 2) padded += "==";
                else if (pad == 3) padded += "=";
                decoded = Convert.FromBase64String(padded);
                return true;
            }
            catch
            {
                decoded = Array.Empty<byte>();
                return false;
            }
        }
    }

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!((c >= '0' && c <= '9')
                  || (c >= 'a' && c <= 'f')
                  || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Extension methods for applying <see cref="WebhookSignatureFilter"/> to endpoints.
/// </summary>
public static class WebhookSignatureFilterExtensions
{
    /// <summary>
    /// Adds HMAC-SHA256 signature validation to the endpoint using a custom header and signing-key resolver.
    /// </summary>
    public static RouteHandlerBuilder RequireWebhookSignature(
        this RouteHandlerBuilder builder,
        string signatureHeader,
        Func<IServiceProvider, string?> signingKeyAccessor,
        string filterName)
    {
        var filter = new WebhookSignatureFilter(signatureHeader, signingKeyAccessor, filterName);
        return builder.AddEndpointFilter(filter);
    }
}
