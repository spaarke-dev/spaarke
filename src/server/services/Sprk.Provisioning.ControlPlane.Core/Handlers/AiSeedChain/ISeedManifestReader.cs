// -----------------------------------------------------------------------------
// ISeedManifestReader.cs
//
// L2 abstraction over the manifest.yaml file — reads content, computes the
// SHA-256 hash used by H12a's idempotency key, and runs the defense-in-depth
// retired-artifact scan.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="FileSeedManifestReader"/> — reads the on-disk
//       manifest, computes SHA-256 of the raw bytes, scans for retired-artifact
//       patterns configured via <see cref="AiSeedChainOptions.RetiredArtifactPatterns"/>.
//     - Test: fakes injected per unit test that return canned bytes + hash.
//   Interface earns its keep — no NIH.
//
// DESIGN CHOICE:
//   The retired-artifact scan is layered on TOP of Invoke-SeedManifest.ps1's
//   own retired-artifact enforcement (task 069) so a hand-edited manifest that
//   bypasses the generator ALSO fails loudly at the C# handler layer. Two
//   independent gates against the same ADR-039 violation are cheap insurance
//   (spec.md FR-15 acceptance: "seed chain terminates at playbook consumers
//   per ADR-039; retired dispatcher / embeddings index MUST NOT be
//   re-introduced").
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <summary>
/// Reads the declarative AI seed manifest for H12a and provides the derived
/// content-hash + retired-artifact defense-in-depth check.
/// </summary>
public interface ISeedManifestReader
{
    /// <summary>
    /// Reads the manifest.yaml content from the configured path, computes its
    /// SHA-256 hash, and inspects the body for any retired-artifact pattern
    /// (defense-in-depth per ADR-039 amendment 2026-07-05). Returns a
    /// discriminated result — Success carries the hash + optional retired-hit
    /// diagnostic (<c>null</c> for clean); NotFound is returned when the file
    /// is absent (caller maps to <see cref="AiSeedChainRejectionCodes.ManifestNotFound"/>).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token — the read is cheap but honors cancellation for the enclosing handler timeout.</param>
    Task<SeedManifestReadResult> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated result of <see cref="ISeedManifestReader.ReadAsync"/>.
/// Exhaustive: <see cref="Success"/> | <see cref="NotFound"/>. Callers
/// pattern-match to decide the failure classification without try/catch
/// discipline.
/// </summary>
public abstract record SeedManifestReadResult
{
    private SeedManifestReadResult() { }

    /// <summary>Manifest read succeeded.</summary>
    /// <param name="ContentHash">Lowercase-hex SHA-256 of the raw manifest bytes. Used as the H12a idempotency-key suffix per POML constraint.</param>
    /// <param name="RetiredArtifactViolation">
    /// Non-null when the defense-in-depth scan matched at least one retired-artifact pattern
    /// in an ARTIFACT declaration (as opposed to the governance-only <c>retiredArtifacts:</c>
    /// section). Carries the matched pattern + a short excerpt line for operator triage.
    /// Handler maps a non-null value to <see cref="AiSeedChainRejectionCodes.ManifestContainsRetiredArtifact"/>.
    /// </param>
    public sealed record Success(
        string ContentHash,
        RetiredArtifactViolation? RetiredArtifactViolation) : SeedManifestReadResult;

    /// <summary>Manifest.yaml not present at the configured path.</summary>
    /// <param name="AttemptedPath">Absolute path the reader tried — included in the resulting Diagnostic.</param>
    public sealed record NotFound(string AttemptedPath) : SeedManifestReadResult;
}

/// <summary>
/// Represents a single retired-artifact hit found by the defense-in-depth
/// scan. Carries enough context for the operator diagnostic without
/// dumping the entire manifest.
/// </summary>
/// <param name="Pattern">The retired-artifact pattern that matched (e.g. <c>spaarke-playbook-embeddings</c>).</param>
/// <param name="LineNumber">1-based line number in the manifest where the match occurred.</param>
/// <param name="LineExcerpt">The full line text — bounded to 200 chars — where the match occurred.</param>
public sealed record RetiredArtifactViolation(
    string Pattern,
    int LineNumber,
    string LineExcerpt);
