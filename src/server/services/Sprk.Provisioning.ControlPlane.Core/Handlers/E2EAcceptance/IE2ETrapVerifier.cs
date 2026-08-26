// -----------------------------------------------------------------------------
// IE2ETrapVerifier.cs
//
// L2 abstraction over the 6 §4B silent-fail trap RE-verifications H13 performs
// per spec.md SC #6. Independent from the owning handler's own post-condition
// verify — H13 asserts EFFECTS (per R7) rather than trusting upstream handler
// telemetry alone.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 impls exist by design (production trap verifier + test fakes per unit
//   test). One aggregate seam (rather than 6 separate seams) mirrors H9's
//   IR3GateVerifier composition — the handler-side orchestration stays flat +
//   the verifier owns the trap-specific probe details.
//
// LIVE-PROBE POSTURE:
//   Some traps require a live customer stamp reachable from the L2 App Service:
//     - T1 keyVaultReferenceIdentity (ARM read against App Service both slots)
//     - T2 Dataverse App User (Web API systemusers filter)
//     - T3 Graph appRoleAssignments parity (Graph REST /servicePrincipals/{id}/appRoleAssignments)
//     - T4 Exchange ApplicationAccessPolicy count (pwsh Exchange Online — same
//       shell-out surface as H14a's IExchangePolicyApplier)
//     - T5 slot MI KV RBAC OR UAMI structural (ARM read)
//     - T6 SPE confidential-client creation audit (KV secret + Graph audit log
//       sampling as a proxy for "creation was app-only")
//   Per POML escalation trigger: if any trap verifier cannot be exercised from
//   the test host, the production impl surfaces a distinct <see cref="TrapVerificationOutcome.InfraFault"/>
//   which the handler classifies Resumable — the operator restarts the run
//   after fixing the environment. Unit tests exercise each trap's PASS + FAIL +
//   InfraFault branches via injected canned outcomes; live-Azure coverage
//   belongs in the Phase F acceptance suite (task 089) per ADR-038 test-pyramid
//   categorization.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Re-verifies all 6 §4B silent-fail traps (T1–T6) independently of the owning
/// handler's own post-condition. Production impl issues live Azure/Graph/
/// Dataverse probes; test impls return canned results.
/// </summary>
public interface IE2ETrapVerifier
{
    /// <summary>
    /// Runs all 6 trap probes in a bounded fan-out and returns per-trap
    /// outcomes. Individual probe failures do NOT throw — the handler decides
    /// how to react (Quarantined on any trap failure per §4B). Aggregate infra
    /// faults MAY throw; individual probe infra faults are surfaced as
    /// <see cref="TrapVerificationOutcome.InfraFault"/> so the handler can
    /// distinguish TRAP fail (QuarantineRequired) from VERIFIER fail (Resumable).
    /// </summary>
    Task<TrapCatalogVerificationResult> VerifyAllAsync(TrapVerificationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single trap-catalog verification pass. Immutable record.
/// </summary>
/// <param name="CustomerId">Customer id — probes scope to the customer stamp.</param>
/// <param name="RunId">RunId — for log correlation.</param>
/// <param name="TenantId">Explicit tenantId (§4D I1 no-hardcoded-tenant — probes MUST use this scope).</param>
/// <param name="SubscriptionId">Customer subscription id (ADR-027 D4) — probes scope to this subscription.</param>
/// <param name="DataverseUrl">Target Dataverse environment URL for T2 systemusers filter.</param>
/// <param name="BffAppRegId">BFF app-reg id (H3 output) — expected T2/T4 principal.</param>
/// <param name="UamiClientId">UAMI client id (H2a output) — expected T1/T3/T4 principal.</param>
/// <param name="KeyVaultName">Customer KV name for T1 ref probe.</param>
/// <param name="AppServiceName">BFF App Service name for T1/T5 ARM probe.</param>
/// <param name="ResourceGroupName">App Service resource group for T1/T5 ARM probe.</param>
public sealed record TrapVerificationRequest(
    string CustomerId,
    string RunId,
    string TenantId,
    string SubscriptionId,
    string DataverseUrl,
    string BffAppRegId,
    string UamiClientId,
    string KeyVaultName,
    string AppServiceName,
    string ResourceGroupName);

/// <summary>
/// The 6 §4B silent-fail traps, enumerated (matches design.md §4B verbatim).
/// </summary>
public enum TrapKind
{
    /// <summary>T1 — App Service <c>keyVaultReferenceIdentity</c> == customer UAMI on BOTH slots.</summary>
    T1KeyVaultReferenceIdentity = 1,

    /// <summary>T2 — Dataverse App User systemusers filter returns the expected UAMI + BFF app-reg pair.</summary>
    T2DataverseAppUser = 2,

    /// <summary>T3 — UAMI SP appRoleAssignments == all 14 in GraphAppRoles.cs.</summary>
    T3GraphAppRoleParity = 3,

    /// <summary>T4 — Exchange <c>Get-ApplicationAccessPolicy</c> returns 2 entries with matching principals.</summary>
    T4ExchangePolicyCount = 4,

    /// <summary>T5 — both-slot MI KV RBAC OR UAMI structural (post-Phase-C).</summary>
    T5SlotMiKvRbac = 5,

    /// <summary>T6 — SPE container-type creation audit reveals confidential-client (app-only) used.</summary>
    T6SpeConfidentialClient = 6,
}

/// <summary>
/// Per-trap outcome. Discriminated union: Passed / Failed / InfraFault.
/// </summary>
public abstract record TrapVerificationOutcome
{
    private TrapVerificationOutcome() { }

    /// <summary>Trap probe ran and confirmed the expected state.</summary>
    public sealed record Passed(TrapKind Kind) : TrapVerificationOutcome;

    /// <summary>Trap probe ran and confirmed the trap CONDITION is present (silent-fail was NOT prevented) — QuarantineRequired per §4B.</summary>
    /// <param name="Kind">Which trap fired.</param>
    /// <param name="Diagnostic">Operator-facing diagnostic — MUST cite BOTH observed state AND expected state.</param>
    public sealed record Failed(TrapKind Kind, string Diagnostic) : TrapVerificationOutcome;

    /// <summary>Trap probe could not conclusively run (SDK exception, endpoint unreachable, timeout) — Resumable per H13 handler classification.</summary>
    /// <param name="Kind">Which trap could not be verified.</param>
    /// <param name="Diagnostic">Operator-facing diagnostic — MUST identify the specific infra fault + suggest remediation.</param>
    public sealed record InfraFault(TrapKind Kind, string Diagnostic) : TrapVerificationOutcome;
}

/// <summary>
/// Aggregate result of a trap-catalog verification pass. The list contains
/// exactly one entry per <see cref="TrapKind"/>, in enum-declaration order.
/// </summary>
public sealed record TrapCatalogVerificationResult(IReadOnlyList<TrapVerificationOutcome> Outcomes)
{
    /// <summary>True iff no trap returned <see cref="TrapVerificationOutcome.Failed"/>.</summary>
    public bool AllTrapsClear => Outcomes.All(o => o is not TrapVerificationOutcome.Failed);

    /// <summary>True iff at least one trap could not be verified due to infra fault.</summary>
    public bool AnyInfraFault => Outcomes.Any(o => o is TrapVerificationOutcome.InfraFault);

    /// <summary>The first FAILED trap (if any) — the handler uses this to pick the distinct rejection code.</summary>
    public TrapVerificationOutcome.Failed? FirstFailure
        => Outcomes.OfType<TrapVerificationOutcome.Failed>().FirstOrDefault();

    /// <summary>The first INFRA FAULT (if any) — the handler uses this when no trap Failed but at least one probe could not run.</summary>
    public TrapVerificationOutcome.InfraFault? FirstInfraFault
        => Outcomes.OfType<TrapVerificationOutcome.InfraFault>().FirstOrDefault();

    /// <summary>Convenience helper for logs: comma-separated <c>{Kind}={Status}</c> per trap.</summary>
    public string ToLogSummary()
        => string.Join(", ", Outcomes.Select(o => o switch
        {
            TrapVerificationOutcome.Passed p => $"{p.Kind}=Passed",
            TrapVerificationOutcome.Failed f => $"{f.Kind}=Failed",
            TrapVerificationOutcome.InfraFault i => $"{i.Kind}=InfraFault",
            _ => o.ToString() ?? "unknown",
        }));
}
