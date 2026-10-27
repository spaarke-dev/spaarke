using Microsoft.AspNetCore.Mvc;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// The ONE map from a <see cref="RecordCreationRefusal"/> to RFC 7807 ProblemDetails (field-mapping-server-write-path-r1
/// task 013, spec FR-12, ADR-019): every <see cref="RecordCreationRefusalKind"/> has one status, every response carries the
/// stable <c>errorCode</c> (and the same value as <c>reasonCode</c>, the extension the child-record route's clients read)
/// and a <c>correlationId</c>.
/// </summary>
/// <remarks>
/// <para><b>No upstream text (ADR-019).</b> An <see cref="RecordCreationRefusalKind.Upstream"/> or
/// <see cref="RecordCreationRefusalKind.Unavailable"/> refusal is rendered with fixed words per status; the Dataverse
/// message and any exception text stay in the logs. Every other kind renders the <see cref="RecordCreationRefusal.Detail"/>
/// the service decided.</para>
/// <para><b>Uniform not-found (NFR-01).</b> A <see cref="RecordCreationRefusalKind.NotFoundOrDenied"/> refusal — and an
/// upstream 404 — renders ONE body whatever its detail says, so "does not exist" and "cannot see" are indistinguishable.</para>
/// <para><b>Upstream statuses.</b> The caller's Dataverse failure keeps its meaning without its text: 400 → 400,
/// 401/403 → 403, 404 → the uniform 404, 429 → 429, 408/504 → 504, anything else (a 5xx, a request that never left the
/// BFF) → 503. ADR-019's 429/503/500 mapping, plus the caller's own refusals.</para>
/// <para><b>ADR-010.</b> A static class: no DI registration, no interface.</para>
/// </remarks>
internal static class RecordCreationProblemMap
{
    /// <summary>The HTTP status of <paramref name="refusal"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A kind this map does not know — never shipped
    /// (<c>RecordCreationProblemMapTests</c> maps every member).</exception>
    internal static int StatusOf(RecordCreationRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return refusal.Kind switch
        {
            RecordCreationRefusalKind.PrincipalUnresolved => StatusCodes.Status403Forbidden,
            RecordCreationRefusalKind.BlockedColumn => StatusCodes.Status403Forbidden,
            RecordCreationRefusalKind.Forbidden => StatusCodes.Status403Forbidden,
            RecordCreationRefusalKind.NotFoundOrDenied => StatusCodes.Status404NotFound,
            RecordCreationRefusalKind.Invalid => StatusCodes.Status400BadRequest,
            RecordCreationRefusalKind.OwnerUnresolved => StatusCodes.Status409Conflict,
            RecordCreationRefusalKind.Conflict => StatusCodes.Status409Conflict,
            RecordCreationRefusalKind.Undecided => StatusCodes.Status500InternalServerError,
            RecordCreationRefusalKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            RecordCreationRefusalKind.Upstream => UpstreamStatus(refusal.ClientFailure?.StatusCode),
            _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal.Kind, "A record-creation refusal kind with no HTTP mapping."),
        };
    }

    /// <summary>
    /// <paramref name="refusal"/> as ProblemDetails for a create of <paramref name="table"/>, correlated by
    /// <paramref name="correlationId"/> (the request's trace id).
    /// </summary>
    internal static ProblemDetails ToProblemDetails(RecordCreationRefusal refusal, string table, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var status = StatusOf(refusal);
        var noun = RecordCreationService.NounOf(table);
        var uniformNotFound = status == StatusCodes.Status404NotFound;
        var code = uniformNotFound ? RecordCreationRefusal.NotFoundCode : refusal.Code;
        var detail = uniformNotFound
            ? $"A record this {noun} is filed under was not found. The {noun} was not saved."
            : refusal.Kind switch
            {
                RecordCreationRefusalKind.OwnerUnresolved => $"The {noun} was not saved: {refusal.Detail}.",
                RecordCreationRefusalKind.Unavailable =>
                    $"The {noun} could not be saved right now, so nothing was created. Try again in a few minutes.",
                RecordCreationRefusalKind.Upstream => UpstreamDetail(status, noun),
                _ => refusal.Detail,
            };

        return new ProblemDetails
        {
            Status = status,
            Title = TitleOf(status, refusal.Kind),
            Detail = detail,
            Extensions =
            {
                ["errorCode"] = code,
                ["reasonCode"] = code,
                ["correlationId"] = correlationId,
            },
        };
    }

    /// <summary><see cref="ToProblemDetails"/> as a minimal-API result.</summary>
    internal static IResult ToResult(RecordCreationRefusal refusal, string table, string correlationId) =>
        Results.Problem(ToProblemDetails(refusal, table, correlationId));

    private static int UpstreamStatus(int? status) => status switch
    {
        StatusCodes.Status400BadRequest => StatusCodes.Status400BadRequest,
        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => StatusCodes.Status403Forbidden,
        StatusCodes.Status404NotFound => StatusCodes.Status404NotFound,
        StatusCodes.Status429TooManyRequests => StatusCodes.Status429TooManyRequests,
        StatusCodes.Status408RequestTimeout or StatusCodes.Status504GatewayTimeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status503ServiceUnavailable,
    };

    private static string UpstreamDetail(int status, string noun) => status switch
    {
        StatusCodes.Status400BadRequest => $"Dataverse refused the {noun} as written, so nothing was created.",
        StatusCodes.Status403Forbidden => $"You do not have permission to create this {noun}, so nothing was created.",
        StatusCodes.Status429TooManyRequests => $"Dataverse is busy, so the {noun} was not created. Try again in a moment.",
        StatusCodes.Status504GatewayTimeout => $"Dataverse did not answer in time, so the {noun} was not created. Try again.",
        _ => $"Dataverse could not be reached, so the {noun} was not created. Try again in a few minutes.",
    };

    private static string TitleOf(int status, RecordCreationRefusalKind kind) => status switch
    {
        StatusCodes.Status400BadRequest => "Invalid request",
        StatusCodes.Status403Forbidden => "Not permitted",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict when kind == RecordCreationRefusalKind.OwnerUnresolved => "Record owner unresolved",
        StatusCodes.Status429TooManyRequests => "Too many requests",
        StatusCodes.Status503ServiceUnavailable => "Service unavailable",
        StatusCodes.Status504GatewayTimeout => "Upstream timeout",
        _ => "Record not saved",
    };
}
