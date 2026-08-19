// -----------------------------------------------------------------------------
// IKvSecretManifest.cs
//
// Reader abstraction over the Phase H canonical secret-catalog manifest (spec.md
// FR-36 / task 084). Returns an ordered list of KV secret entries the H4 handler
// consumes to populate the customer's Key Vault.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Interim (wave C4): <see cref="StaticKvSecretManifest"/> — Null-placeholder
//       returning the known-required §7.9 canonical entries hard-coded (Phase G
//       naming enforcement) so H4 can ship + own T1/T5 trap verification
//       BEFORE Phase H manifest generator lands.
//     - Production (Phase H task 084): a real file-backed / manifest-generator
//       impl loaded from `scripts/canonical-secret-catalog/manifest.yaml`
//       (or the runtime-generated JSON). Swap the DI registration only — H4
//       handler + tests remain unchanged (parity with H1's Null probe →
//       real ARM probe transition and H12a's manifest reader shape).
//   Interface earns its keep — no NIH. The Null-placeholder is the established
//   pattern in this project (NullSubscriptionReadinessProbe task 043,
//   NullDataverseEnvironmentRegistryClient task 042, NullAdminConsentVerifier
//   task 046) documented in CLAUDE.md.
//
// PHASE H DEPENDENCY:
//   Per POML dependencies list: task 080/084 (Phase H canonical secret-catalog
//   manifest) is NOT YET SHIPPED. The dispatcher directive is EXPLICIT: "use
//   the established Null-placeholder pattern; author IKvSecretManifest seam
//   with a StaticKvSecretManifest interim impl that returns the known-required
//   set (Dataverse-ClientSecret, BFF-API-ClientSecret, SPE-ContainerTypeId,
//   etc. per spec §7.9 R1-R4 + tasks 018/019/020 canonical names). Real
//   Phase H manifest impl swaps in later without touching H4."
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
/// <c>AiSearch--AdminKey</c>. Grep-visible across the codebase.
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
    /// <summary>Value already exists on ANOTHER Key Vault; writer resolves via UAMI + copies. Common for Bicep-emitted secrets (AiSearch--AdminKey, etc.).</summary>
    FromExistingKvSecret = 1,

    /// <summary>Value comes from a Bicep deploy output (H2a interStepState). Writer resolves via `az deployment group show` or the shared interStepState.</summary>
    FromBicepOutput = 2,

    /// <summary>Value from operator-supplied run parameters (<see cref="Models.RunParameters.Secrets"/>, structurally KV URI ref).</summary>
    FromRunParameters = 3,

    /// <summary>Value is generated in-place (webhook signing keys, random bytes, etc.).</summary>
    Generated = 4,
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
