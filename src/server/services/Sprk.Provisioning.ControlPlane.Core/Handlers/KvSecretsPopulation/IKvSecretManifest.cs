// -----------------------------------------------------------------------------
// IKvSecretManifest.cs
//
// Reader abstraction over the Phase H canonical secret-catalog manifest (spec.md
// FR-36 / task 084). Returns an ordered list of KV secret entries the H4 handler
// consumes to populate the customer's Key Vault.
//
// SEAM JUSTIFICATION (ADR-010):
//   Production implementation: <see cref="FileKvSecretManifest"/>, reading the
//   embedded `scripts/canonical-secret-catalog/manifest.yaml` (task 126).
//   H4's tests substitute hand-built entry lists through this seam, so the
//   handler's ordering, omit and BINDING-guard logic is tested without the
//   real catalog. The interim StaticKvSecretManifest (wave C4 Null-placeholder,
//   written before task 084 shipped the manifest) was deleted by T226
//   (2026-09-30): it was no longer registered, and its hard-coded list had
//   drifted from the catalog — reverting to it would have written keys the
//   owner removed from the process.
//
// THREAD-SAFETY:
//   Implementations MUST be thread-safe (Singleton lifetime). The manifest is
//   effectively immutable for the lifetime of the L2 process — reload requires
//   a redeploy (parity with H12a ISeedManifestReader).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Reads the canonical KV secret-catalog manifest (spec.md FR-36 / Phase H) —
/// returns the ordered list of secrets H4 must populate to a customer's Key
/// Vault.
/// </summary>
public interface IKvSecretManifest
{
    /// <summary>
    /// Loads + returns the manifest entries. Returns a
    /// <see cref="KvSecretManifestReadResult.Success"/> on success or a
    /// <see cref="KvSecretManifestReadResult.Failure"/> with an operator-facing
    /// diagnostic when the manifest source is unreadable. Domain outcomes
    /// (success / no entries) never throw; only genuine infra faults (network,
    /// I/O) throw so the handler can classify per §4C.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<KvSecretManifestReadResult> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Ordered manifest entry — one row per canonical secret the H4 handler must
/// upsert or delete on the target Key Vault.
/// </summary>
/// <param name="CanonicalName">
/// Canonical secret name per §7.9 R1 (env-agnostic) + R2 (one canonical
/// casing). Examples: <c>Dataverse-ClientSecret</c>, <c>BFF-API-ClientSecret</c>,
/// <c>Redis-ConnectionString</c>. Grep-visible across the codebase.
/// </param>
/// <param name="Operation">
/// What H4 must do with the entry — <see cref="KvSecretOperation.Upsert"/> is
/// the default; <see cref="KvSecretOperation.Delete"/> is intentionally
/// disallowed for <c>Dataverse-ClientSecret</c> and <c>BFF-API-ClientSecret</c>
/// by the H4 BINDING pre-check (spec.md MUST rule).
/// </param>
/// <param name="ValueSource">
/// Where the actual cleartext value comes from — a hint to the
/// <see cref="IKvSecretsWriter"/> impl. H4 does NOT resolve the value itself
/// (cleartext never traverses handler code — ADR-028 MUST rule); it delegates
/// value resolution to the writer.
/// </param>
public sealed record KvSecretEntry(
    string CanonicalName,
    KvSecretOperation Operation,
    KvSecretValueSource ValueSource);

/// <summary>What H4 must do with a manifest entry.</summary>
public enum KvSecretOperation
{
    /// <summary>Create the secret if absent; leave existing value untouched unless <c>rotate=true</c>.</summary>
    Upsert = 1,

    /// <summary>Remove the secret from the target vault. Forbidden for Dataverse-ClientSecret / BFF-API-ClientSecret (BINDING pre-check).</summary>
    Delete = 2,
}

/// <summary>
/// Hint to <see cref="IKvSecretsWriter"/> for where to resolve the cleartext
/// value from — H4 itself never touches cleartext values (ADR-028 MUST rule).
/// </summary>
public enum KvSecretValueSource
{
    /// <summary>Value already exists on ANOTHER Key Vault; writer resolves via UAMI + copies (e.g. <c>Dataverse-ClientSecret</c>, <c>BFF-API-ClientSecret</c>).</summary>
    FromExistingKvSecret = 1,

    /// <summary>Value is written to the customer vault by <c>customer.bicep</c>'s kvSecrets module at H2a. The writer checks that it exists; H4 has no other source for it (see <see cref="KvSecretValueResolver"/>).</summary>
    FromBicepOutput = 2,

    /// <summary>Value from operator-supplied run parameters (<see cref="Models.RunParameters.Secrets"/>, structurally KV URI ref).</summary>
    FromRunParameters = 3,

    /// <summary>Value is generated in-place (webhook signing keys, random bytes, etc.).</summary>
    Generated = 4,

    // 5 was FromSharedService (task 200 H4-shared) — retired T226 (2026-09-30); not reused.

    /// <summary>
    /// T226 (2026-09-30) — value is a Spaarke topology constant carried on the run as a NON-SECRET
    /// parameter (manifest <c>value_source: from-topology-constants</c>, introduced by task 214 for
    /// <c>SPE-ContainerTypeId</c>, whose value comes from <c>spaarke-constants.yaml
    /// per_env_constants.&lt;env&gt;.containerTypeId</c>). H4 supplies it via
    /// <see cref="KvSecretWriteRequest.TopologyConstantValues"/>.
    /// </summary>
    FromTopologyConstants = 6,
}

/// <summary>
/// Discriminated result of <see cref="IKvSecretManifest.ReadAsync"/>. Success
/// carries the ordered entries; Failure carries an operator-facing diagnostic
/// (mapped by the handler to <see cref="KvSecretsPopulationRejectionCodes.ManifestReadFailed"/>).
/// </summary>
public abstract record KvSecretManifestReadResult
{
    private KvSecretManifestReadResult() { }

    /// <summary>Manifest read OK — entries in canonical order.</summary>
    public sealed record Success(IReadOnlyList<KvSecretEntry> Entries) : KvSecretManifestReadResult;

    /// <summary>Manifest read failed — operator-facing diagnostic.</summary>
    public sealed record Failure(string Diagnostic) : KvSecretManifestReadResult;
}
