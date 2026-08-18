using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Endpoints.Onboarding;

/// <summary>
/// Request body for the H0.5 consent-callback endpoint (task 042 — customer-
/// provisioning-orchestration-r1). Posted by the Microsoft admin-consent
/// redirect flow AFTER HMAC signature verification.
/// </summary>
/// <remarks>
/// All fields are required for successful processing:
/// <list type="bullet">
/// <item><see cref="CustomerId"/> — non-empty; matches <c>sprk_dataverseenvironment.sprk_customerid</c>.</item>
/// <item><see cref="Tid"/> — non-empty Entra tenant id of the CUSTOMER (not the operator); §4D I1 no-default-tenant.</item>
/// <item><see cref="CorrelationId"/> — optional caller-supplied correlation id; endpoint falls back to <c>HttpContext.TraceIdentifier</c>.</item>
/// </list>
/// </remarks>
public sealed record ConsentCallbackRequest
{
    /// <summary>Customer short-id (matches <c>sprk_dataverseenvironment.sprk_customerid</c>).</summary>
    [JsonPropertyName("customerId")]
    public string? CustomerId { get; init; }

    /// <summary>Customer Entra tenant id captured from the admin-consent redirect (<c>tid</c> claim).</summary>
    [JsonPropertyName("tid")]
    public string? Tid { get; init; }

    /// <summary>Optional correlation id supplied by the caller. Endpoint falls back to <c>HttpContext.TraceIdentifier</c>.</summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }
}

/// <summary>
/// 202 Accepted response body for the H0.5 consent-callback endpoint.
/// Carries the deterministic RunId assigned by the endpoint so the caller
/// (Microsoft admin-consent redirect page) can display it to the operator
/// and correlate downstream progress via the L2 status API (Wave C5).
/// </summary>
/// <param name="RunId">Server-assigned run identifier (also carried in Service Bus <c>CorrelationId</c>).</param>
/// <param name="CorrelationId">Server-assigned correlation id — the caller's supplied value or <c>HttpContext.TraceIdentifier</c>.</param>
/// <param name="Message">Human-readable acknowledgement text — safe to log / display verbatim.</param>
public sealed record ConsentCallbackResponse(
    string RunId,
    string CorrelationId,
    string Message);
