namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Domain exception for storage / Graph API failures. Wraps Microsoft.Graph
/// SDK exceptions (most commonly <c>Microsoft.Graph.Models.ODataErrors.ODataError</c>)
/// so that *Endpoints classes can catch a Spaarke-domain type WITHOUT importing
/// the Graph SDK namespace.
///
/// Resolves NetArchTest ADR007_GraphIsolationTests.EndpointsShouldNotReferenceGraphSdk:
/// per ADR-007 §1, Microsoft.Graph types stay isolated to <c>Infrastructure.Graph</c>
/// and <c>SpeFileStore</c> namespaces. Endpoints catch <see cref="SpaarkeStorageException"/>
/// instead of <c>ODataError</c> directly.
///
/// Properties intentionally mirror the most-commonly-consumed <c>ODataError</c>
/// fields (<c>ResponseStatusCode</c>, <c>Error.Code</c>, <c>Error.Message</c>) so the
/// existing endpoint-side error-translation logic transfers with minimal change.
///
/// Added 2026-06-26 by ci-cd-unit-test-remediation-r1 task CICD-088 per spec FR-A06.
/// </summary>
public sealed class SpaarkeStorageException : Exception
{
    /// <summary>
    /// HTTP status code returned by the underlying Graph call, when known.
    /// Mirrors <c>ODataError.ResponseStatusCode</c>.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Machine-readable error code from the underlying Graph call, when known.
    /// Mirrors <c>ODataError.Error?.Code</c> (e.g., <c>"itemNotFound"</c>,
    /// <c>"nameAlreadyExists"</c>, <c>"invalidRequest"</c>).
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Microsoft Graph request correlation id for the failed call, when the response carried one.
    /// Sourced from <c>ODataError.Error.InnerError.RequestId</c> / <c>ClientRequestId</c>, falling back
    /// to the <c>request-id</c> / <c>client-request-id</c> response headers.
    /// <para>
    /// This is the value an operator quotes to Microsoft support. Added 2026-08-21 by
    /// <c>sdap-SPE-admin-app-r2</c> task 001 (spec FR-A01): the id was reachable all along but nothing in
    /// the repo extracted it, so <c>ProblemDetailsHelper.FromGraphException</c>'s <c>graphRequestId</c>
    /// parameter had no caller that could populate it.
    /// </para>
    /// </summary>
    public string? GraphRequestId { get; }

    public SpaarkeStorageException(
        string message,
        int? statusCode = null,
        string? errorCode = null,
        Exception? innerException = null,
        string? graphRequestId = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        GraphRequestId = graphRequestId;
    }
}
