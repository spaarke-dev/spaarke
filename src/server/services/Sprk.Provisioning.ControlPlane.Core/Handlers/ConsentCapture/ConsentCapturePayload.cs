// -----------------------------------------------------------------------------
// ConsentCapturePayload.cs
//
// Wire-shape payload for the H0.5 consent-capture handler (task 042).
//
// PURPOSE:
//   The BFF's ConsentCallbackEndpoint enqueues a HandlerEnvelope onto the
//   fleet-scoped Service Bus queue (`sprk-provisioning-jobs`) whose
//   ParametersJson is a serialization of this record. The L2 H05ConsentCaptureHandler
//   deserializes it out of HandlerEnvelope.ParametersJson at HandleAsync time.
//
// DESIGN REF:
//   - spec.md FR-02 (Model 2 self-service consent-capture)
//   - design.md §4.1 H0.5 row + §4.3a.2 auth flow (Anonymous+HMAC exception)
//   - design.md D18 (BFF-as-consent-callback client)
//   - projects/customer-provisioning-orchestration-r1/tasks/042-*.poml constraints
//
// WIRE FORMAT (System.Text.Json, camelCase — parity with HandlerEnvelope):
//
//     {
//       "customerId":    "acme-corp",
//       "tenantId":      "00000000-0000-0000-0000-000000000000",
//       "correlationId": "0HN..." (optional; caller TraceIdentifier)
//     }
//
// NON-SECRETS ONLY: this payload MUST NOT carry cleartext secrets — the
// KeyVaultSecretRef structural rule (RunParameters.Secrets) applies to any
// secret channel used by downstream handlers.
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Handlers.ConsentCapture;

/// <summary>
/// Deserialized shape of the H0.5 consent-capture Service Bus message body.
/// Bound from <c>HandlerEnvelope.ParametersJson</c> by H05ConsentCaptureHandler.
/// </summary>
/// <remarks>
/// All fields are required for HandleAsync to proceed:
/// <see cref="CustomerId"/> and <see cref="TenantId"/> are load-bearing;
/// <see cref="CorrelationId"/> is optional (defaults to null; the handler
/// falls back to the envelope's RunId for correlation).
/// </remarks>
public sealed record ConsentCapturePayload
{
    /// <summary>
    /// Customer short-id — matches <c>sprk_dataverseenvironment.sprk_customerid</c>
    /// and Cosmos <c>ProvisioningRun.CustomerId</c> (partition-key value).
    /// </summary>
    [JsonPropertyName("customerId")]
    public required string CustomerId { get; init; }

    /// <summary>
    /// Customer Entra tenant id captured from the Microsoft admin-consent
    /// flow's redirect (the <c>tid</c> claim). This is the ONLY place a
    /// customer-tenant identifier enters the provisioning pipeline —
    /// downstream handlers MUST parameterize every Azure API call with this
    /// value explicitly (spec.md §4D I1 no-hardcoded-tenant).
    /// </summary>
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }

    /// <summary>
    /// Optional correlation id from the BFF request. Falls back to the
    /// envelope's RunId when null. Used only for observability — never as
    /// a dedupe key.
    /// </summary>
    [JsonPropertyName("correlationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrelationId { get; init; }
}
