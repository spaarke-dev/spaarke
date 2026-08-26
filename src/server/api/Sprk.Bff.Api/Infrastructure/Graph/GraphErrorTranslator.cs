using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Helper for converting Microsoft.Graph SDK exceptions into Spaarke-domain
/// <see cref="SpaarkeStorageException"/>. Lives in <c>Infrastructure.Graph</c>
/// so the import of <c>Microsoft.Graph.Models.ODataErrors</c> stays inside
/// the allowed namespace per ADR-007 §1.
///
/// Usage pattern (in <c>Infrastructure.Graph</c> services that call Graph SDK):
/// <code>
/// try { return await graphCall(...); }
/// catch (ODataError ex) { throw ex.ToSpaarkeStorageException("operation context"); }
/// </code>
///
/// Added 2026-06-26 by ci-cd-unit-test-remediation-r1 task CICD-088 per spec FR-A06.
/// </summary>
public static class GraphErrorTranslator
{
    /// <summary>
    /// Convert an <see cref="ODataError"/> into a <see cref="SpaarkeStorageException"/>
    /// preserving status code, error code, and message. The original exception is
    /// kept as <c>InnerException</c> for diagnostic purposes.
    /// </summary>
    /// <param name="ex">The Graph SDK ODataError to translate.</param>
    /// <param name="contextMessage">Optional caller-supplied context (operation name,
    /// resource id, etc.) prepended to the Graph error message.</param>
    public static SpaarkeStorageException ToSpaarkeStorageException(
        this ODataError ex,
        string? contextMessage = null)
    {
        var graphMessage = ex.Error?.Message ?? ex.Message ?? "Graph API error";
        var fullMessage = string.IsNullOrEmpty(contextMessage)
            ? graphMessage
            : $"{contextMessage}: {graphMessage}";

        return new SpaarkeStorageException(
            message: fullMessage,
            statusCode: ex.ResponseStatusCode,
            errorCode: ex.Error?.Code,
            innerException: ex,
            graphRequestId: ExtractRequestId(ex));
    }

    /// <summary>
    /// Pulls the Microsoft Graph request correlation id out of a failed call — the value an operator
    /// quotes to Microsoft support.
    /// </summary>
    /// <remarks>
    /// Graph reports it in two places and neither is guaranteed: the error body's <c>innerError</c>
    /// (<c>request-id</c> / <c>client-request-id</c>) and the response headers. We check the body first
    /// because it survives SDK layers that do not surface headers, then fall back to the headers.
    /// Added 2026-08-21 by task 001 — before this, nothing in the repo extracted a request id at all, so
    /// <c>ProblemDetailsHelper.FromGraphException</c>'s <c>graphRequestId</c> parameter was permanently null.
    /// </remarks>
    internal static string? ExtractRequestId(ODataError ex)
    {
        var inner = ex.Error?.InnerError;

        if (!string.IsNullOrWhiteSpace(inner?.RequestId))
        {
            return inner.RequestId;
        }

        if (!string.IsNullOrWhiteSpace(inner?.ClientRequestId))
        {
            return inner.ClientRequestId;
        }

        if (ex.ResponseHeaders is { Count: > 0 } headers)
        {
            foreach (var name in new[] { "request-id", "client-request-id" })
            {
                var value = headers
                    .FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase))
                    .Value?
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Maps an upstream Graph status onto the status a SpeAdmin endpoint should return.
    /// </summary>
    /// <remarks>
    /// Passes 4xx/5xx through so the caller sees what Graph said, with ONE exception: an upstream
    /// <c>401</c> becomes <c>502</c>. A Graph 401 means <i>our</i> credential to Graph is bad, not that the
    /// caller's token is stale — but the client's <c>authenticatedFetch</c> reads any 401 as its own token
    /// expiring, clears its cache, burns three silent retries, and throws a generic <c>AuthError</c>,
    /// discarding the real Graph error. Returning 502 keeps the payload this task exists to surface.
    /// Anything outside 4xx/5xx (including an unknown/zero status) is also 502 — the failure is upstream.
    /// <para>Added 2026-08-21 by <c>sdap-SPE-admin-app-r2</c> task 001 (spec FR-A01).</para>
    /// </remarks>
    public static int ClientStatusFor(this SpaarkeStorageException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return ex.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => StatusCodes.Status502BadGateway,
            >= 400 and < 600 => ex.StatusCode.Value,
            _ => StatusCodes.Status502BadGateway
        };
    }

    /// <summary>
    /// Convert a <see cref="SpaarkeStorageException"/> into an RFC 7807 ProblemDetails
    /// response. Mirrors <c>ProblemDetailsHelper.FromGraphException</c> but lives in
    /// <c>Infrastructure.Graph</c> so endpoint files (in <c>Api/</c>) calling this
    /// helper do not import any Microsoft.Graph namespace per ADR-007 §1.
    /// </summary>
    /// <remarks>
    /// Used by 29 call sites across the document/OBO/upload endpoints. Prefer the richer overload below
    /// for new code — it carries a caller-supplied summary, a stable error code, the Graph request id, and
    /// a correlation id, and it does not substitute its own text for the Graph message.
    /// </remarks>
    public static IResult ToProblemDetails(this SpaarkeStorageException ex)
    {
        var status = ex.StatusCode is > 0 ? ex.StatusCode.Value : 500;
        var title = status == 403 ? "forbidden" : status == 401 ? "unauthorized" : "error";
        var code = ex.ErrorCode ?? status.ToString();
        var detail = (status == 403 && code.Contains("Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase))
            ? "missing graph app role (filestoragecontainer.selected) for the api identity."
            : status == 403 ? "api identity lacks required container-type permission for this operation."
            : ProblemDetailsHelper.Redact(ex.Message);

        return Results.Problem(
            title: title,
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["graphErrorCode"] = code,
                ["graphRequestId"] = ex.GraphRequestId
            });
    }

    /// <summary>
    /// Convert a <see cref="SpaarkeStorageException"/> into an RFC 7807 ProblemDetails response that
    /// reports what Graph ACTUALLY said. Lives in <c>Infrastructure.Graph</c> so endpoint files (in
    /// <c>Api/</c>) calling it do not import any Microsoft.Graph namespace per ADR-007 §1.
    /// </summary>
    /// <param name="ex">The translated Graph failure.</param>
    /// <param name="summary">
    /// What the caller was doing, stated WITHOUT asserting why it failed — e.g. "Could not list container
    /// types." A caller MAY add an actionable hint here when the caught exception establishes it (a catch
    /// filtered on a specific status/code); it MUST NOT name a cause the exception never observed.
    /// </param>
    /// <param name="errorCode">Stable Spaarke error code (e.g. <c>spe.containertypes.graph_error</c>).</param>
    /// <param name="statusCode">
    /// HTTP status to return. Callers pass the status they already returned — task 001 changes error
    /// CONTENT, not status semantics. The upstream Graph status is reported separately as
    /// <c>graphStatusCode</c> so the payload stays truthful either way.
    /// </param>
    /// <param name="traceId">Correlation id — <c>HttpContext.TraceIdentifier</c> (ADR-019).</param>
    /// <remarks>
    /// <para>
    /// Added 2026-08-21 by <c>sdap-SPE-admin-app-r2</c> task 001 (spec FR-A01) as an overload rather than a
    /// signature change: the parameterless form has 29 callers in the document/OBO/upload endpoints, which
    /// are outside this task's SpeAdmin scope. Within <c>Api/SpeAdmin/**</c> the parameterless form had no
    /// callers at all — every endpoint hand-wrote its own <c>Results.Problem(detail: "…")</c> instead.
    /// </para>
    /// <para>
    /// Why the status is caller-supplied rather than propagated from Graph: the client's
    /// <c>authenticatedFetch</c> treats a 401 as <i>its own</i> token being stale and burns three silent
    /// retries before throwing <c>AuthError</c>. Propagating an upstream Graph 401 verbatim would bury the
    /// very error this task surfaces. Status-code re-mapping is a separate concern with its own blast
    /// radius; it is deliberately out of scope here.
    /// </para>
    /// </remarks>
    public static IResult ToProblemDetails(
        this SpaarkeStorageException ex,
        string summary,
        string errorCode,
        int statusCode = StatusCodes.Status500InternalServerError,
        string? traceId = null,
        string title = "Graph API Error")
    {
        ArgumentNullException.ThrowIfNull(ex);

        var graphStatus = ex.StatusCode is > 0 ? ex.StatusCode.Value : (int?)null;
        var graphCode = string.IsNullOrWhiteSpace(ex.ErrorCode) ? null : ex.ErrorCode;
        var graphMessage = ProblemDetailsHelper.Redact(ex.Message);

        var reported = (graphCode, graphMessage) switch
        {
            (not null, { Length: > 0 }) => $"Graph reported {graphCode}: {graphMessage}",
            (not null, _) => $"Graph reported {graphCode}.",
            (null, { Length: > 0 }) => $"Graph reported: {graphMessage}",
            _ => null
        };

        return Results.Problem(
            title: title,
            detail: reported is null ? summary : $"{summary} {reported}",
            statusCode: statusCode,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["graphErrorCode"] = graphCode,
                ["graphStatusCode"] = graphStatus,
                ["graphRequestId"] = ex.GraphRequestId,
                ["traceId"] = traceId
            });
    }
}
