// -----------------------------------------------------------------------------
// IStampKeylessVerifier.cs — task 230b (owner D13)
//
// The ARM half of H13's keyless gate: every stamp resource that can accept a key has key/local auth disabled, and
// no slot of the stamp's BFF carries a key setting. (The runtime half — one real managed-identity call per service,
// made by the BFF — is E2EValidationRunner's keyless proof.) Templates intend this (customer.bicep, pinned by
// tests/Spaarke.ArchTests/CustomerStampKeylessTemplateTests); this proves the DEPLOYED stamp, which a template
// edit, a portal change or an older template version could have left otherwise.
//
// SEAM JUSTIFICATION (ADR-010): ≥2 implementations by design — ArmStampKeylessVerifier and the H13 handler tests'
// fakes, so the handler's decision logic is testable without ARM.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>Proves from ARM that a deployed customer stamp is keyless (task 230b).</summary>
public interface IStampKeylessVerifier
{
    /// <summary>
    /// Checks every keyed resource in the stamp's resource group and every slot of the stamp's App Service.
    /// Never throws for a domain result; an ARM or transport fault is <see cref="StampKeylessOutcome.InfraFault"/>.
    /// </summary>
    Task<StampKeylessOutcome> VerifyAsync(StampKeylessRequest request, CancellationToken cancellationToken);
}

/// <summary>Inputs: the stamp's coordinates (H2a outputs + intake).</summary>
public sealed record StampKeylessRequest(
    string CustomerId,
    string RunId,
    string SubscriptionId,
    string ResourceGroupName,
    string AppServiceName);

/// <summary>Typed outcome of the ARM keyless check.</summary>
public abstract record StampKeylessOutcome
{
    private StampKeylessOutcome() { }

    /// <summary>Every keyed resource has key auth disabled and no slot carries a key setting.</summary>
    /// <param name="Checked">What was checked (resource names and slots), for the gate log.</param>
    public sealed record Passed(IReadOnlyList<string> Checked) : StampKeylessOutcome;

    /// <summary>At least one resource accepts keys, a slot carries a key setting, or an expected resource is absent.</summary>
    /// <param name="Violations">One line per violation — resource or setting NAMES only, never a value.</param>
    public sealed record Failed(IReadOnlyList<string> Violations) : StampKeylessOutcome;

    /// <summary>ARM could not be read (permission, transport, throttling) — no verdict.</summary>
    public sealed record InfraFault(string Diagnostic) : StampKeylessOutcome;
}
