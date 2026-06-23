using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;

/// <summary>
/// Deterministic hash of the canonical embed-input string used by
/// <see cref="PlaybookEmbeddingService"/>. Shared between the indexing path
/// (which stores the hash in <c>sprk_indexhash</c> at index time) and the
/// nightly drift-detection job (which recomputes the hash to compare against
/// the stored value). Centralizing the composition+hash logic here is a
/// correctness invariant (chat-routing-redesign-r1 FR-13): if the indexer and
/// the drift job ever drift apart, the drift job produces false positives.
/// </summary>
/// <remarks>
/// <para>
/// Implementations MUST call
/// <see cref="PlaybookEmbeddingService.ComposeContentText(PlaybookEmbeddingDocument)"/>
/// (the parameter-less internal-static overload) so the composition logic remains
/// a single source of truth. Implementations MUST be deterministic and pure
/// (no side effects, no I/O). The returned string MUST be lowercase hex with no
/// separators so it round-trips cleanly through the Dataverse
/// <c>sprk_indexhash</c> NVARCHAR(100) column.
/// </para>
/// <para>
/// Per ADR-015 (AI Data Governance), implementations MUST NOT log the playbook
/// content text or the hash input. The hash itself is a deterministic fingerprint
/// of public playbook metadata and is safe to log as a structured field.
/// </para>
/// </remarks>
public interface IPlaybookEmbeddingHashCalculator
{
    /// <summary>
    /// Computes the canonical embed-input hash for the supplied playbook document.
    /// </summary>
    /// <param name="document">Hydrated playbook embedding document (must not be null).
    /// The document's content fields (PlaybookName, Description, TriggerPhrases, Tags,
    /// JpsMatchingMetadata) drive the hash. Vector and identifier fields are ignored.</param>
    /// <returns>SHA-256 hex digest (lowercase, no separators) of the composed embed-input
    /// string. Stable across calls for identical input.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document"/> is null.</exception>
    string ComputeHash(PlaybookEmbeddingDocument document);
}
