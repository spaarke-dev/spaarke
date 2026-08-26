// -----------------------------------------------------------------------------
// ISharedKvSecretAccessor.cs
//
// Task 200 — H4-shared per-secret KV read+write seam. H4-shared can't reuse
// IKvSecretsWriter directly because that seam is BATCH-oriented (a whole
// manifest at once with per-entry KvSecretWriteRequest.SecretParameters +
// KvSecretValueResolver dispatch) and its cleartext-value delivery path is
// via IKvSecretValueResolver (which cannot resolve FromSharedService values —
// they come from the extractor, not from another KV or param).
//
// This seam is deliberately NARROW:
//   - ReadAsync(vault, name)   → Success(value) | NotFound() | Failure(diag)
//   - WriteAsync(vault, name, value) → Success | Failure(diag)
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: SecretClientKvSharedSecretAccessor (SDK — Azure.Security
//       .KeyVault.Secrets.SecretClient; parity with SecretClientKvWriter +
//       SecretClientKvReader).
//     - Test: per-unit-test fakes.
//
// COMPONENT JUSTIFICATION (CLAUDE.md §11 three-question gate):
//   1. Existing — IKvSecretsWriter (batch, value-resolver-mediated) and
//      IntegrationWiring.IKvSecretReader (single-secret READ only, no write).
//   2. Extension — Extending IKvSecretsWriter would widen its cleartext
//      contract from "resolved-via-resolver" to "caller-supplied cleartext",
//      breaking the ADR-028 discipline that keeps cleartext OUT of handler
//      code. Extending IKvSecretReader with a write method would inverse a
//      read-only contract that H14 relies on staying read-only.
//   3. Cost of doing nothing — H4-shared would either (a) duplicate ~40 lines
//      of SecretClient wire-up in the handler (untestable, drift-prone) or
//      (b) shell out to `az` (contrary to task 125 Option D hybrid).
//
// CLEARTEXT DISCIPLINE (ADR-028 MUST rule):
//   Values flow ONLY as method arguments / return values. Diagnostics carry
//   canonical names + vault names + status codes, NEVER the values. Callers
//   MUST NOT Log* the returned value.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Narrow per-secret read+write seam over a target Key Vault. Used by
/// <see cref="H4SharedKvSecretsPopulationHandler"/> to compare current-vault
/// value against a fresh source extraction (drift detection) and write on
/// drift or absence.
/// </summary>
public interface ISharedKvSecretAccessor
{
    /// <summary>
    /// Reads the current value of <paramref name="secretName"/> from
    /// <paramref name="vaultName"/>. Domain outcomes (NotFound / Failure) never
    /// throw; only genuine infra faults do (network fault before any Azure
    /// response), and the handler wraps those at its boundary.
    /// </summary>
    Task<SharedKvSecretReadResult> ReadAsync(
        string vaultName,
        string secretName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes <paramref name="value"/> to <paramref name="secretName"/> on
    /// <paramref name="vaultName"/> (upsert). Returns a discriminated result;
    /// never throws on domain failures.
    /// </summary>
    Task<SharedKvSecretWriteResult> WriteAsync(
        string vaultName,
        string secretName,
        string value,
        CancellationToken cancellationToken);
}

/// <summary>Discriminated result of a shared-KV read.</summary>
public abstract record SharedKvSecretReadResult
{
    private SharedKvSecretReadResult() { }

    /// <summary>Secret exists — <paramref name="Value"/> is the cleartext (caller MUST NOT log).</summary>
    public sealed record Success(string Value) : SharedKvSecretReadResult;

    /// <summary>Secret does not exist on the target vault (H4-shared will WriteAsync fresh).</summary>
    public sealed record NotFound() : SharedKvSecretReadResult;

    /// <summary>Read failed; <paramref name="Diagnostic"/> carries operator-facing detail (never a value).</summary>
    public sealed record Failure(string Diagnostic) : SharedKvSecretReadResult;
}

/// <summary>Discriminated result of a shared-KV write.</summary>
public abstract record SharedKvSecretWriteResult
{
    private SharedKvSecretWriteResult() { }

    /// <summary>Write succeeded.</summary>
    public sealed record Success() : SharedKvSecretWriteResult;

    /// <summary>Write failed; <paramref name="Diagnostic"/> carries operator-facing detail (never a value).</summary>
    public sealed record Failure(string Diagnostic) : SharedKvSecretWriteResult;
}
