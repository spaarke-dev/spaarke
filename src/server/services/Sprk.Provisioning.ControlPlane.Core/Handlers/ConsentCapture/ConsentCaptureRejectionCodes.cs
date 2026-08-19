// -----------------------------------------------------------------------------
// ConsentCaptureRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H05ConsentCaptureHandler on
// HandlerResult.Failure. Operators + BFF surface these verbatim so external
// tooling (dashboards, runbook lookups) can pattern-match without parsing
// English diagnostics.
//
// DESIGN REF:
//   - HandlerResult.Failure.RejectionCode (Handlers/HandlerResult.cs)
//   - spec.md FR-02 (H0.5 acceptance — each failure branch has a distinct code)
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.ConsentCapture;

/// <summary>
/// Stable rejection codes returned by <c>H05ConsentCaptureHandler</c> on
/// <c>HandlerResult.Failure</c>. Values are lowercase kebab-case so grep +
/// runbook lookup are trivial.
/// </summary>
public static class ConsentCaptureRejectionCodes
{
    /// <summary>Envelope arrived without a non-empty ParametersJson body.</summary>
    public const string MissingParameters = "consent-missing-parameters";

    /// <summary>ParametersJson deserialized but was semantically invalid (bad JSON, wrong shape).</summary>
    public const string MalformedPayload = "consent-malformed-payload";

    /// <summary>Payload.CustomerId was null / whitespace.</summary>
    public const string MissingCustomerId = "consent-missing-customer-id";

    /// <summary>Payload.TenantId was null / whitespace (violates §4D I1).</summary>
    public const string MissingTenantId = "consent-missing-tenant-id";

    /// <summary>Envelope.CustomerId did NOT match Payload.CustomerId (cross-partition mismatch).</summary>
    public const string CustomerIdMismatch = "consent-customer-id-mismatch";

    /// <summary>
    /// Registry (Dataverse) was unreachable AND no fallback route exists.
    /// Currently NOT emitted (NullDataverseEnvironmentRegistryClient never
    /// throws), reserved for Wave C5's real impl.
    /// </summary>
    public const string RegistryUnavailable = "consent-registry-unavailable";
}

/// <summary>
/// Well-known gate identifiers written to <c>ProvisioningRun.GateStates</c>
/// by consent-capture. Kept as string constants so grep across the codebase
/// finds every read/write of the same gate name (design.md §6.2).
/// </summary>
public static class ConsentCaptureGates
{
    /// <summary>
    /// The gate H0.5 owns — flipped from <c>Pending</c> to <c>Verified</c>
    /// when the consent-callback envelope is successfully processed. Matches
    /// design.md §6.2 <c>gateStates</c> example: <c>admin-consent</c>.
    /// </summary>
    public const string AdminConsent = "admin-consent";
}
