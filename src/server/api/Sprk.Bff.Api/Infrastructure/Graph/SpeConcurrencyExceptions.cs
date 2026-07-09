namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Thrown when an optimistic-concurrency SPE write (<c>PUT …/content</c> with <c>If-Match</c>) is
/// rejected because the drive-item changed under the caller since the ETag was read — Graph returns
/// <b>HTTP 412 Precondition Failed</b>. FR-24 / Spike 7 (task 007) primary gap G-1: the R1 write path
/// sent no <c>If-Match</c>, so a Compose push could silently overwrite a Word-for-Web autosave
/// (last-write-wins). Surfacing 412 makes the lost-update <b>impossible, not just messaged</b>.
/// </summary>
/// <remarks>
/// ADR-007: this is the facade's translation of a Graph <c>ServiceException</c> — no
/// <c>Microsoft.Graph</c> type crosses the <see cref="ISpeFileOperations"/> boundary. The endpoint
/// maps it to a 412 ProblemDetails; the client's recovery is "reload &amp; re-anchor" (Spike 7 §5 C′).
/// </remarks>
public sealed class EtagPreconditionFailedException : Exception
{
    public EtagPreconditionFailedException(string itemId, string? ifMatch, Exception? inner = null)
        : base($"SPE write precondition failed: drive-item '{itemId}' changed since ETag '{ifMatch}' was read.", inner)
    {
        ItemId = itemId;
        IfMatch = ifMatch;
    }

    /// <summary>The SPE drive-item id whose write was rejected.</summary>
    public string ItemId { get; }

    /// <summary>The stale <c>If-Match</c> ETag the caller supplied.</summary>
    public string? IfMatch { get; }
}

/// <summary>
/// Thrown when an SPE write is rejected because the drive-item is locked by a live Word co-authoring
/// session — Graph returns <b>HTTP 423 Locked</b>. FR-24 / Spike 7 (task 007) gap G-2: the R1 write
/// path caught only 404/403/429, so a 423 surfaced as an opaque 500 ("Save failed"). Surfacing a
/// typed 423 lets the UI tell "close Word, then retry" apart from a real server fault (Spike 7 §5 C).
/// </summary>
/// <remarks>
/// ADR-007: facade translation of a Graph <c>ServiceException</c>; no <c>Microsoft.Graph</c> type
/// leaks. The endpoint maps it to a 423 ProblemDetails; the client's recovery is "close Word, then
/// Save again — your Compose changes are safe and still pending" (the pending edits live in the
/// ledger, not the file — ADR-040).
/// </remarks>
public sealed class DocumentLockedByWordException : Exception
{
    public DocumentLockedByWordException(string itemId, Exception? inner = null)
        : base($"SPE write blocked: drive-item '{itemId}' is open in Word for Web (HTTP 423 Locked).", inner)
    {
        ItemId = itemId;
    }

    /// <summary>The SPE drive-item id that is locked.</summary>
    public string ItemId { get; }
}
