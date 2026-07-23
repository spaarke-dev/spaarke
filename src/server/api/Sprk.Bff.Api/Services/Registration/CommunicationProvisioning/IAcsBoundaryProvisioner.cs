namespace Sprk.Bff.Api.Services.Registration.CommunicationProvisioning;

/// <summary>
/// Seam over the management-plane provisioning of a per-boundary ACS resource + Event Grid system
/// topic/subscription (task 012, FR-18). Keeping this an interface lets the in-process orchestrator
/// (<see cref="AcsBoundaryProvisioningService"/>) be fully wired and unit-tested now, while the
/// actual Azure resource creation is deferred.
/// </summary>
/// <remarks>
/// R1 registers <see cref="DeferredAcsBoundaryProvisioner"/> — the ADR-032 Null-Object — because
/// live provisioning is Bicep/operator-driven (<c>infrastructure/bicep/customer.bicep</c>; see the
/// task-012 runbook), which keeps the BFF publish-size delta ≈0 (no in-process ARM management SDK).
/// A future live implementation (ARM management SDK) can replace the registration without touching
/// callers, and MUST authenticate management-plane calls with the injected central
/// <c>Azure.Core.TokenCredential</c> (ADR-028 / NFR-05) — never a <c>new</c> credential.
/// </remarks>
public interface IAcsBoundaryProvisioner
{
    /// <summary>Provisions (or, in R1, records intent to provision) the resources in <paramref name="plan"/>.</summary>
    Task<AcsProvisioningResult> ProvisionAsync(AcsProvisioningPlan plan, CancellationToken ct = default);
}
