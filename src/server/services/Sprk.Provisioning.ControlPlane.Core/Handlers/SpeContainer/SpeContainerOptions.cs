// -----------------------------------------------------------------------------
// SpeContainerOptions.cs
//
// Bound options for the H8-B handler's collaborators (container provisioner +
// app-only verifier). Loaded from the "SpeContainerOptions" configuration
// section by Worker/Program.cs and validated at Worker startup (ValidateOnStart).
//
// TASK 245b (G25) — THE SPE OWNER CREDENTIAL IS L2 CONFIGURATION, PER CONTAINER TYPE.
// H8 creates containers app-only with the container type's OWNING app (an E-1
// identity with a certificate — ADR-028). Every reader of that credential — H0's
// SpeCertBootstrap probe, H8, and H13's T6 probe — now takes it from
// <see cref="ContainerTypeOwners"/>, looked up by the run's containerTypeId
// (intake, from spaarke-constants.yaml). Before T245b:
//   - H0 and H8 read the certificate's vault from a run parameter nothing wrote
//     (`keyVaultName`), so every real run stopped at H0;
//   - H0 checked secret `spe-owner-cert-pfx` while H8 read `SPE-OwnerCert-Pfx` — Key
//     Vault names ignore case but not hyphens, so those were two different secrets;
//   - H8 and T6 presented the owner certificate as the CUSTOMER BFF app
//     (InterStepState.BffAppRegId). The certificate is registered on the owning
//     app, and the BFF app is deliberately a different, secret-free identity
//     (SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md §3A "The BFF app registration MUST be
//     separate from the owning app") — so the token request could never succeed.
// Why a list keyed by container type, not one setting: an owning app is bound to
// exactly one container type, permanently (topology R1), and one L2 environment
// provisions into more than one type (Spaarke Trial 1 and Spaarke Model 1).
//
// SUPERSEDES SpeContainerTypeOptions (deleted 2026-08-30 task 214). The rename
// trimmed retired fields that only existed for the deleted shell-out
// collaborators (script paths / pwsh executable / az CLI executable /
// ProvisionTimeout / VerifyTimeout) and for the deleted KV writer
// (ContainerTypeIdSecretName / KvOperationTimeout).
//
// PATTERN PARITY: mirrors Handlers/EntraAppReg/EntraAppRegOptions.cs.
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <summary>
/// Bound options for <see cref="H8SpeContainerHandler"/> collaborators and the SPE owner-credential
/// readers (H0 probe, H13 T6 probe). Configuration key: <c>SpeContainerOptions</c>.
/// </summary>
public sealed class SpeContainerOptions
{
    /// <summary>The canonical secret name for an SPE owner certificate (base64 PFX).</summary>
    public const string DefaultOwnerCertSecretName = "SPE-OwnerCert-Pfx";

    /// <summary>Timeout for a single Graph SDK call (create/activate/get). Graph is normally sub-second; generous ceiling for throttle/backoff. Parity with EntraAppRegOptions.GraphRequestTimeout.</summary>
    public TimeSpan GraphRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Timeout for the T6 cert-from-KV SecretClient.GetSecretAsync read (SpeConfidentialClientGraphFactory.LoadCertificateAsync).</summary>
    public TimeSpan CertLoadTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The SPE container types this L2 deployment provisions into, each with its owning app and the
    /// Key Vault secret holding that app's certificate (task 245b). A run's <c>containerTypeId</c>
    /// (intake) selects the entry. Empty is valid at startup — no container type is set up yet — and
    /// H0 then rejects every run before anything is created.
    /// </summary>
    public List<SpeContainerTypeOwner> ContainerTypeOwners { get; set; } = new();

    /// <summary>Default container display name prefix when the run parameter is absent — customer id is appended.</summary>
    public string DefaultDisplayNamePrefix { get; set; } = "Spaarke Container";

    /// <summary>
    /// The owner entry for <paramref name="containerTypeId"/> (GUID comparison — case and surrounding
    /// whitespace ignored), or <c>false</c> when this L2 deployment has none.
    /// </summary>
    public bool TryGetOwner(string? containerTypeId, [NotNullWhen(true)] out SpeContainerTypeOwner? owner)
    {
        owner = null;
        if (!Guid.TryParse(containerTypeId?.Trim(), out var wanted))
        {
            return false;
        }
        var match = ContainerTypeOwners.FirstOrDefault(o =>
            Guid.TryParse(o.ContainerTypeId?.Trim(), out var id) && id == wanted);
        if (match is null)
        {
            return false;
        }
        // Hand back canonical ids ("D" form) — a braced or upper-case config value never reaches Graph as typed.
        owner = new SpeContainerTypeOwner
        {
            ContainerTypeId = wanted.ToString("D"),
            OwnerAppId = Guid.Parse(match.OwnerAppId.Trim()).ToString("D"),
            OwnerCertKeyVaultName = match.OwnerCertKeyVaultName.Trim(),
            OwnerCertSecretName = match.OwnerCertSecretName.Trim(),
        };
        return true;
    }

    /// <summary>
    /// Startup validation (Worker/Program.cs ValidateOnStart). Every entry names a container type and
    /// owning app (GUIDs), a vault and a secret; no container type appears twice; and no two owning
    /// apps share one certificate secret. Throws <see cref="InvalidOperationException"/> naming the
    /// offending entry.
    /// </summary>
    public void Validate()
    {
        var seenTypes = new HashSet<Guid>();
        var seenOwnerApps = new HashSet<Guid>();
        var certByLocation = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ContainerTypeOwners.Count; i++)
        {
            var entry = ContainerTypeOwners[i] ?? throw new InvalidOperationException(
                $"SpeContainerOptions:ContainerTypeOwners:{i} is empty.");
            var where = $"SpeContainerOptions:ContainerTypeOwners:{i}";
            if (!Guid.TryParse(entry.ContainerTypeId?.Trim(), out var typeId))
            {
                throw new InvalidOperationException(
                    $"{where}:ContainerTypeId must be the SPE container-type GUID (got '{entry.ContainerTypeId}').");
            }
            if (!Guid.TryParse(entry.OwnerAppId?.Trim(), out var appId))
            {
                throw new InvalidOperationException(
                    $"{where}:OwnerAppId must be the owning app registration's client id GUID (got '{entry.OwnerAppId}').");
            }
            if (string.IsNullOrWhiteSpace(entry.OwnerCertKeyVaultName))
            {
                throw new InvalidOperationException(
                    $"{where}:OwnerCertKeyVaultName is required — the Spaarke platform Key Vault holding the owning " +
                    "app's certificate (read by H0's SpeCertBootstrap probe, H8 and H13's T6 probe).");
            }
            if (string.IsNullOrWhiteSpace(entry.OwnerCertSecretName))
            {
                throw new InvalidOperationException(
                    $"{where}:OwnerCertSecretName must not be blank (canonical: {DefaultOwnerCertSecretName}).");
            }
            if (!seenTypes.Add(typeId))
            {
                throw new InvalidOperationException(
                    $"{where}: container type {typeId} is listed more than once — one owning app per container type (topology R1).");
            }
            if (!seenOwnerApps.Add(appId))
            {
                throw new InvalidOperationException(
                    $"{where}: owning app {appId} is already the owner of another container type — an owning app owns " +
                    "exactly one container type, permanently (topology R1).");
            }
            var location = $"{entry.OwnerCertKeyVaultName.Trim()}/{entry.OwnerCertSecretName.Trim()}";
            if (certByLocation.TryGetValue(location, out var otherApp) && otherApp != appId)
            {
                throw new InvalidOperationException(
                    $"{where}: certificate secret '{location}' is already the certificate of owning app {otherApp} — " +
                    "each owning app has its own certificate.");
            }
            certByLocation[location] = appId;
        }
    }
}

/// <summary>One SPE container type and the owning-app credential L2 uses for it (task 245b).</summary>
public sealed class SpeContainerTypeOwner
{
    /// <summary>The container type id (GUID) — matched against the run's intake <c>containerTypeId</c>.</summary>
    public string ContainerTypeId { get; set; } = string.Empty;

    /// <summary>The owning app registration's client id (GUID) — the identity H8 and T6 authenticate as.</summary>
    public string OwnerAppId { get; set; } = string.Empty;

    /// <summary>The Spaarke platform Key Vault holding the owning app's certificate.</summary>
    public string OwnerCertKeyVaultName { get; set; } = string.Empty;

    /// <summary>The secret holding the certificate as base64 PFX (canonical <c>SPE-OwnerCert-Pfx</c>).</summary>
    public string OwnerCertSecretName { get; set; } = SpeContainerOptions.DefaultOwnerCertSecretName;
}
