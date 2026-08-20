// -----------------------------------------------------------------------------
// IExchangePolicyApplier.cs
//
// Seam abstraction over Exchange Online's ApplicationAccessPolicy
// action-and-verify semantics (spec.md T4). Owns the FULL T4 dance in one
// call: list existing policies for the expected AppId set, create any
// missing (0 or 1 present), and — critically — VERIFY parity when 2+ are
// already present rather than silently overwriting (the T4 silent-fail trap
// this seam exists to close).
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production (task 161, Wave G-6): <see cref="ExchangePolicySidecarClient"/>
//       — HttpClient POSTing to the sitecontainer-private sidecar (task 114's
//       `Listener.ps1` on http://127.0.0.1:8091/apply-policy) with the
//       per-boot X-Sidecar-Auth shared-secret header sourced from platform KV
//       via <see cref="IKvSecretReader"/> (task 160's SecretClientKvReader).
//       The sidecar owns the EXO module + Connect-ExchangeOnline; the client
//       maps the sidecar's 4-outcome wire envelope
//       (Success|AlreadyCompliant|Drift|Failure — see task 114's Listener.ps1
//       .DESCRIPTION envelope-mapping rules; the 4th outcome is deliberate,
//       preserving Drift as a distinct state to close the T4 silent-fail
//       regression this seam exists to close) onto the same 3-case
//       <see cref="ExchangePolicyApplyOutcome"/> the retired script applier
//       mapped exit codes onto — wire Success/AlreadyCompliant collapse to
//       <see cref="ExchangePolicyApplyOutcome.Applied"/> (differentiated by
//       CreatedCount).
//     - Retired (task 073, kept on disk unregistered): <see cref="ExchangePolicyScriptApplier"/>
//       — shelled out to scripts/Set-ExchangeApplicationAccessPolicy.ps1.
//       Superseded by the sidecar client for security-and-supply-chain reasons
//       per DS-1b §3 (defense-in-depth: the sole EXO shell-out lives in a
//       non-routable sidecar container, one localhost route, one signed
//       Microsoft module — instead of the main Worker process carrying pwsh +
//       ExchangeOnlineManagement + an ambient auth session).
//     - Test: per-unit-test fakes returning canned outcomes.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Applies + verifies Exchange Online <c>ApplicationAccessPolicy</c> entries
/// for the customer's BFF app-registration + UAMI. Encapsulates the full T4
/// action-and-verify semantics (spec.md FR-33 T4).
/// </summary>
public interface IExchangePolicyApplier
{
    /// <summary>
    /// Executes the T4 action-and-verify sequence: list existing policies for
    /// <paramref name="request"/>'s expected AppId set; if 0 or 1 present,
    /// create the missing entries; if 2+ present, verify the observed AppIds
    /// equal the expected set exactly — MUST NOT silently overwrite on a
    /// mismatch (returns <see cref="ExchangePolicyApplyOutcome.Drift"/> instead).
    /// </summary>
    Task<ExchangePolicyApplyOutcome> ApplyAsync(
        ExchangePolicyApplyRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// One T4 apply invocation input.
/// </summary>
/// <param name="TenantId">Explicit Entra tenant id (§4D I1 + I5 — never an ambient default-tenant credential).</param>
/// <param name="ExpectedAppIds">The exactly-2-entry expected AppId set: [BFF app-reg id, UAMI client id].</param>
/// <param name="PolicyScopeGroupId">Mail-enabled security group id scoping the ApplicationAccessPolicy (New-ApplicationAccessPolicy -PolicyScopeGroupId — customer-tenant-specific, run-parameter supplied).</param>
/// <param name="DescriptionPrefix">Description prefix applied to newly created policies (greppability).</param>
/// <param name="CorrelationId">
/// ProvisioningRun id (invoking handler's <c>envelope.RunId</c>) — placed on the
/// sidecar's <c>correlationId</c> wire field so its structured stdout log lines
/// interleave with the main Worker's own <c>correlationId=RunId</c> logs in the
/// shared Log Analytics workspace (task 114's Listener.ps1 .OBSERVABILITY note).
/// Defaulted to <c>""</c> so the retired <see cref="ExchangePolicyScriptApplier"/>
/// remains constructible without ceremony; production callers (H14a sub-handler)
/// MUST pass the RunId — the sidecar client's own validation returns a
/// <see cref="ExchangePolicyApplyOutcome.Failure"/> if CorrelationId is empty.
/// </param>
public sealed record ExchangePolicyApplyRequest(
    string TenantId,
    IReadOnlyList<string> ExpectedAppIds,
    string PolicyScopeGroupId,
    string DescriptionPrefix,
    string CorrelationId = "");

/// <summary>
/// Discriminated outcome of <see cref="IExchangePolicyApplier.ApplyAsync"/>.
/// Exhaustive: <see cref="Applied"/> | <see cref="Drift"/> | <see cref="Failure"/>.
/// </summary>
public abstract record ExchangePolicyApplyOutcome
{
    private ExchangePolicyApplyOutcome() { }

    /// <summary>
    /// Both expected AppIds are confirmed present after the apply — either
    /// because they already were (CreatedCount == 0, a natural no-op) or
    /// because this call created the missing ones.
    /// </summary>
    /// <param name="CreatedCount">Number of policies newly created by this invocation (0, 1, or 2).</param>
    /// <param name="ObservedAppIds">The final observed AppId set post-apply.</param>
    public sealed record Applied(int CreatedCount, IReadOnlyList<string> ObservedAppIds) : ExchangePolicyApplyOutcome;

    /// <summary>
    /// T4 SILENT-FAIL TRAP: 2+ policies already exist for the expected AppIds
    /// but the observed set does NOT match the expected set. The applier did
    /// NOT create or modify anything — per the POML constraint, no silent
    /// overwrite. Caller classifies this as Quarantine-required.
    /// </summary>
    public sealed record Drift(IReadOnlyList<string> ExpectedAppIds, IReadOnlyList<string> ObservedAppIds) : ExchangePolicyApplyOutcome;

    /// <summary>
    /// Infrastructure-level failure (connect, throttle, PS exception) before
    /// a conclusive Applied/Drift outcome could be determined.
    /// </summary>
    public sealed record Failure(string Diagnostic) : ExchangePolicyApplyOutcome;
}
