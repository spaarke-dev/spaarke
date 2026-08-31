// -----------------------------------------------------------------------------
// SpeContainerOptions.cs
//
// Bound options for the H8-B handler's collaborators (container provisioner +
// app-only verifier). Loaded from the "SpeContainer" configuration section by
// Worker/Program.cs — runtime-configurable so the linux-x64 App Service publish
// layout can be honored without recompiling.
//
// SUPERSEDES SpeContainerTypeOptions (deleted 2026-08-30 task 214). The rename
// trimmed retired fields that only existed for the deleted shell-out
// collaborators (script paths / pwsh executable / az CLI executable /
// ProvisionTimeout / VerifyTimeout) and for the deleted KV writer
// (ContainerTypeIdSecretName / KvOperationTimeout).
//
// PATTERN PARITY: mirrors Handlers/EntraAppReg/EntraAppRegOptions.cs.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <summary>
/// Bound options for <see cref="H8SpeContainerHandler"/> collaborators.
/// Configuration key: <c>SpeContainer</c>.
/// </summary>
public sealed class SpeContainerOptions
{
    /// <summary>Timeout for a single Graph SDK call (create/activate/get). Graph is normally sub-second; generous ceiling for throttle/backoff. Parity with EntraAppRegOptions.GraphRequestTimeout.</summary>
    public TimeSpan GraphRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Timeout for the T6 cert-from-KV SecretClient.GetSecretAsync read (SpeConfidentialClientGraphFactory.LoadCertificateAsync).</summary>
    public TimeSpan CertLoadTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// KV secret name holding the base64 PFX SPE owner cert, used when the run
    /// parameter <c>speCertSecretName</c> is absent. Reused by H13's
    /// T6SpeConfidentialClientTrapProbe (its own <c>SpeOwnerCertSecretName</c>
    /// mirrors this default). Flagged for reconciliation into the Phase H
    /// canonical secret-catalog manifest (task 084).
    /// </summary>
    public string DefaultCertSecretName { get; set; } = "SPE-OwnerCert-Pfx";

    /// <summary>Default container display name prefix when the run parameter is absent — customer id is appended.</summary>
    public string DefaultDisplayNamePrefix { get; set; } = "Spaarke Container";
}
