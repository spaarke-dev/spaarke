using Microsoft.Graph.Models.ODataErrors;

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
            innerException: ex);
    }

    /// <summary>
    /// Convert a <see cref="SpaarkeStorageException"/> into an RFC 7807 ProblemDetails
    /// response. Mirrors <c>ProblemDetailsHelper.FromGraphException</c> but lives in
    /// <c>Infrastructure.Graph</c> so endpoint files (in <c>Api/</c>) calling this
    /// helper do not import any Microsoft.Graph namespace per ADR-007 §1.
    /// </summary>
    public static IResult ToProblemDetails(this SpaarkeStorageException ex)
    {
        var status = ex.StatusCode is > 0 ? ex.StatusCode.Value : 500;
        var title = status == 403 ? "forbidden" : status == 401 ? "unauthorized" : "error";
        var code = ex.ErrorCode ?? status.ToString();
        var detail = (status == 403 && code.Contains("Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase))
            ? "missing graph app role (filestoragecontainer.selected) for the api identity."
            : status == 403 ? "api identity lacks required container-type permission for this operation."
            : ex.Message;

        return Results.Problem(
            title: title,
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["graphErrorCode"] = code
            });
    }
}
