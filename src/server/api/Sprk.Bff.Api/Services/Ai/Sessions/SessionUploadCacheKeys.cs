namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// The single definition of the <c>doc-upload-*</c> tenant-cache key shape that a chat session upload
/// writes — the four Redis entries that hold a copy of an uploaded file's bytes, its extracted text,
/// its metadata and its SPE-persist idempotency marker for four hours.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists</b> (spaarkeai-compose-r8 FR-B06, task 063). The writer
/// (<c>ChatDocumentEndpoints</c>) and the eraser (<see cref="SessionFileEraser"/>) must agree on the
/// key EXACTLY: an eraser that composes a key one character different from the writer's removes
/// nothing, reports success, and leaves the original bytes in Redis. That failure is invisible — no
/// exception, no count, no log line says anything is wrong. Two independent copies of these constants
/// is therefore not a style problem but the mechanism by which an erasure silently misses a location,
/// so there is one definition and both sides reference it.
/// </para>
/// <para>
/// <b>The on-wire key</b> produced by <c>ITenantCache</c> from
/// <c>(tenantId, resource, CacheId(sessionId, fileId), Version)</c> is
/// <c>spaarke:tenant:{tenantId}:{resource}:{sessionId}:{fileId}:v1</c>. Every entry is tenant-scoped
/// by the wrapper (ADR-014 / spaarke-redis-cache-remediation-r1 FR-05), so a session-scoped eviction
/// can never reach another tenant's cache.
/// </para>
/// <para>
/// <b>Known remaining duplicate.</b> <c>Api/ComposeEndpoints.cs</c> also declares
/// <c>doc-upload-binary</c> / <c>doc-upload-meta</c> / version 1 privately. It is a READ path (it
/// mounts a retained upload into the Compose editor), so drift there degrades to "bytes not found"
/// rather than to a missed erasure — and it belongs to a file several concurrent Compose tasks are
/// editing. Left alone deliberately; folding it in is a safe follow-up.
/// </para>
/// </remarks>
internal static class SessionUploadCacheKeys
{
    /// <summary>Extracted text of the uploaded document (<c>ChatDocumentEndpoints</c> step 9).</summary>
    internal const string TextResource = "doc-upload-text";

    /// <summary>The ORIGINAL uploaded bytes (step 9b). The hot-tier peer of the durable blob copy.</summary>
    internal const string BinaryResource = "doc-upload-binary";

    /// <summary>Filename, token estimate, truncation flag (step 10).</summary>
    internal const string MetaResource = "doc-upload-meta";

    /// <summary>SPE-persist idempotency marker, which carries the filename and the SPE file id.</summary>
    internal const string PersistResource = "doc-upload-persist";

    /// <summary>Schema version of every entry above.</summary>
    internal const int Version = 1;

    /// <summary>
    /// Every resource an erasure must clear for one file. Enumerated rather than listed at the call
    /// site so adding a fifth <c>doc-upload-*</c> entry cannot leave the eraser behind.
    /// </summary>
    internal static readonly IReadOnlyList<string> AllResources =
    [
        TextResource,
        BinaryResource,
        MetaResource,
        PersistResource,
    ];

    /// <summary>
    /// The cache id shared by all four entries for one uploaded file: <c>{sessionId}:{fileId}</c>.
    /// </summary>
    internal static string CacheId(string sessionId, string fileId) => $"{sessionId}:{fileId}";
}
