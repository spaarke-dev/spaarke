// -----------------------------------------------------------------------------
// NullAdminConsentVerifier.cs
//
// L2 CONTROL-PLANE placeholder impl of IAdminConsentVerifier (task 046 —
// Wave C4 scaffold). Wave C5 replaces this with a real Microsoft.Graph SDK
// v6 impl querying oauth2PermissionGrants / servicePrincipals.appRoleAssignments
// against the customer tenant using DefaultAzureCredential.
//
// SEMANTICS:
//   Returns Verified(GrantedCount = ExpectedCount) unconditionally. This is
//   the SAFE default for dev-env exercise:
//     - A stale Pass in a real environment would be caught downstream: H4
//       (KV secrets) proceeds against the appId + KV secret URI ref, but the
//       BFF will fail at startup or first Graph call if admin consent is
//       genuinely missing — the operator sees the failure at boot/runtime.
//     - The Wave C5 real impl SHOULD default to Pending if it cannot reach
//       Graph — the semantic flips from "assume consented" (dev scaffold) to
//       "prove consented" (production). ADR-028 MI-outbound MUST rule.
//
// WHY NOT LEAVE THE INTERFACE UNIMPLEMENTED:
//   Following the same rationale as NullSubscriptionReadinessProbe (task 043):
//   landing the interface + a null-object impl now lets H3 be fully
//   unit-testable (verifier swaps in mocked results) without creating a
//   phantom dependency that Wave C5 has to backfill. The DI registration is
//   a one-line swap when the real impl ships.
//
// LOGGING:
//   Emits a single Warning-level log per invocation so operators running the
//   scaffold notice the null placeholder is active before Wave C5 replaces
//   it. Suppressed to Debug once the real impl ships (bicep app-setting
//   `EntraAppReg:UseNullConsentVerifier = false`).
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// Placeholder <see cref="IAdminConsentVerifier"/> that returns
/// <see cref="AdminConsentVerificationResult.Verified"/> for every check.
/// Registered in Wave C4; replaced by a Graph-SDK-backed impl in Wave C5.
/// </summary>
public sealed class NullAdminConsentVerifier : IAdminConsentVerifier
{
    private readonly ILogger<NullAdminConsentVerifier> _logger;

    /// <summary>
    /// Initializes the placeholder. Logger emits a single Warning per check
    /// so scaffold environments surface the "no real verifier yet" state.
    /// </summary>
    public NullAdminConsentVerifier(
        ILogger<NullAdminConsentVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<AdminConsentVerificationResult> VerifyAsync(
        string bffAppRegId,
        string tenantId,
        int expectedRoleCount,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bffAppRegId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (expectedRoleCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedRoleCount),
                expectedRoleCount,
                "expectedRoleCount must be >= 1 (typically 14 per GraphAppRoles.cs).");
        }

        _logger.LogWarning(
            "NullAdminConsentVerifier in use — VerifyAsync returning " +
            "Verified for bffAppRegId={BffAppRegId} tenantId={TenantId} " +
            "expectedRoleCount={ExpectedRoleCount}. Wave C5 must replace this " +
            "placeholder with a real Graph-SDK-backed lookup before H3 can " +
            "honor its FR-06 'oauth2PermissionGrants query returns 14 grants' " +
            "acceptance criterion.",
            bffAppRegId, tenantId, expectedRoleCount);

        return Task.FromResult<AdminConsentVerificationResult>(
            new AdminConsentVerificationResult.Verified(
                GrantedCount: expectedRoleCount,
                ExpectedCount: expectedRoleCount,
                Evidence: (JsonElement?)null));
    }
}
