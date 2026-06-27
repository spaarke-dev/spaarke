using System.Security.Cryptography;
using System.Text;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;

/// <summary>
/// Default <see cref="IPlaybookEmbeddingHashCalculator"/> implementation. Calls
/// <see cref="PlaybookEmbeddingService.ComposeContentText(PlaybookEmbeddingDocument)"/>
/// and returns the SHA-256 hex digest of the UTF-8 bytes of the resulting string.
/// </summary>
/// <remarks>
/// <para>
/// Stateless, thread-safe, no constructor dependencies — registered as a Singleton in
/// <see cref="Infrastructure.DI.AiModule"/>. SHA-256 fits in 64 hex chars, well within
/// the Dataverse <c>sprk_indexhash</c> NVARCHAR(100) column (per task 030 schema
/// verification — see notes/handoffs/030-schema-verification-evidence.md).
/// </para>
/// <para>
/// Correctness invariant (chat-routing-redesign-r1 FR-13): this calculator MUST be the
/// single source of truth for the embed-input hash. Both
/// <see cref="PlaybookEmbeddingService"/> (at index time) and
/// <see cref="PlaybookIndexDriftDetectionJob"/> (at nightly drift check) consume it.
/// </para>
/// </remarks>
internal sealed class PlaybookEmbeddingHashCalculator : IPlaybookEmbeddingHashCalculator
{
    /// <inheritdoc/>
    public string ComputeHash(PlaybookEmbeddingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Single source of truth: re-use the indexer's composition logic so the
        // drift job never disagrees with the indexer about what the canonical
        // embed-input is.
        var content = PlaybookEmbeddingService.ComposeContentText(document);

        var bytes = Encoding.UTF8.GetBytes(content);
        var digest = SHA256.HashData(bytes);

        // Lowercase hex, no separators — round-trips cleanly through
        // Dataverse sprk_indexhash NVARCHAR(100).
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
