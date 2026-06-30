using System.Security.Claims;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Compose SPE plumbing surface — loads DOCX bytes from a SPE drive-item and saves
/// new versions back to SPE. Isolates Graph SDK at the boundary so the higher-level
/// <c>ComposeService</c> (task 021) and <c>ComposeEndpoints</c> (task 024) stay testable
/// per ADR-038's mock-at-boundary rule.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope</b>: SPE drive-item read + write only. This service does NOT own check-out lock
/// semantics, version metadata in Dataverse, or ChatSession binding — those are
/// <c>DocumentCheckoutService</c> (existing, spike #3 §2.2 reuse pivot) and
/// <c>ComposeSessionService</c> (task 023) respectively.
/// </para>
/// <para>
/// <b>Writer-identity rule</b>: per
/// <see href="https://github.com/spaarke/spaarke/blob/master/.claude/patterns/auth/spe-writer-identity-matching.md">
/// SPE Writer-Identity Matching</see>, save MUST run under the user's OBO identity (user is on
/// the file's SPE ACL) — hence <see cref="SaveDocxAsync"/> requires the <see cref="HttpContext"/>.
/// Load is also OBO in R1 (user-initiated open). App-only Graph is reserved for future background
/// re-index paths (Phase 5+).
/// </para>
/// <para>
/// <b>Check-out methods are R1 stubs</b>: Phase 5 (tasks 050/051/052) fills these in by reusing
/// the existing <c>DocumentCheckoutService</c> Dataverse-side lock substrate (per spike #3 §2.4,
/// 6-2 decision matrix). They throw <see cref="NotImplementedException"/> in R1 so callers fail
/// loudly rather than silently no-op.
/// </para>
/// </remarks>
public interface IComposeDocumentService
{
    /// <summary>
    /// Load the raw DOCX byte stream for a SPE drive-item under the caller's OBO identity.
    /// </summary>
    /// <param name="httpContext">The current HTTP context — required for OBO token exchange per writer-identity rule.</param>
    /// <param name="driveId">SPE drive (container) id.</param>
    /// <param name="itemId">SPE drive-item id of the DOCX.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ComposeLoadResult"/> with the DOCX stream + file metadata, or
    /// <see cref="ComposeLoadResult.NotFound"/> if the drive-item does not exist.
    /// </returns>
    /// <exception cref="UnauthorizedAccessException">User OBO token rejected by SPE ACL.</exception>
    Task<ComposeLoadResult> LoadDocxAsync(
        HttpContext httpContext,
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Save new DOCX bytes to a SPE drive-item under the caller's OBO identity, committing a new SPE version.
    /// </summary>
    /// <param name="httpContext">The current HTTP context — required for OBO per writer-identity rule.</param>
    /// <param name="driveId">SPE drive (container) id.</param>
    /// <param name="itemId">SPE drive-item id of the existing DOCX (Save is in-place; FR-06).</param>
    /// <param name="content">DOCX byte stream produced by the client-side TipTap → DOCX bridge.</param>
    /// <param name="user">Caller principal for audit logging (not used for auth — OBO handles that).</param>
    /// <param name="correlationId">Request correlation id for tracing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ComposeSaveResult"/> with the new SPE version id + size, or
    /// <see cref="ComposeSaveResult.NotFound"/> if the drive-item does not exist.
    /// </returns>
    /// <exception cref="UnauthorizedAccessException">User OBO token rejected by SPE ACL.</exception>
    /// <remarks>
    /// R1 always runs Save under OBO (user clicked Save in the editor). Future background re-saves
    /// (e.g., post-analysis re-index) would route through MI per the writer-identity rule.
    /// </remarks>
    Task<ComposeSaveResult> SaveDocxAsync(
        HttpContext httpContext,
        string driveId,
        string itemId,
        Stream content,
        ClaimsPrincipal user,
        string correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// R1 stub — Phase 5 task 050 implements check-out acquisition via existing
    /// <c>DocumentCheckoutService</c> (per spike #3 §9 row 050 — REVISED to call existing endpoint
    /// from the React layer, not from a new BFF service method). Kept on the interface so the
    /// editor host can program against a stable surface; will be removed or delegated when Phase 5
    /// lands.
    /// </summary>
    /// <exception cref="NotImplementedException">Always — Phase 5 fills this in or removes it.</exception>
    Task AcquireCheckOutAsync(
        Guid documentId,
        ClaimsPrincipal user,
        CancellationToken ct = default);

    /// <summary>
    /// R1 stub — Phase 5 task 050 routes release through the existing <c>DocumentCheckoutService</c>
    /// (CheckIn or Discard endpoint, per spike #3 §8 endpoint table).
    /// </summary>
    /// <exception cref="NotImplementedException">Always — Phase 5 fills this in or removes it.</exception>
    Task ReleaseCheckOutAsync(
        Guid documentId,
        ClaimsPrincipal user,
        CancellationToken ct = default);
}

/// <summary>
/// Result of <see cref="IComposeDocumentService.LoadDocxAsync"/>.
/// </summary>
/// <param name="Found">True if the drive-item was found and the stream is populated.</param>
/// <param name="Content">DOCX byte stream when <see cref="Found"/> is true; otherwise <c>null</c>. Caller must dispose.</param>
/// <param name="FileName">File name from SPE metadata when <see cref="Found"/> is true.</param>
/// <param name="ETag">SPE ETag for optimistic concurrency on subsequent save (R2+ use).</param>
/// <param name="Size">File size in bytes from SPE metadata.</param>
public sealed record ComposeLoadResult(
    bool Found,
    Stream? Content,
    string? FileName,
    string? ETag,
    long? Size)
{
    /// <summary>Singleton for the "drive-item not found" case.</summary>
    public static ComposeLoadResult NotFound { get; } = new(false, null, null, null, null);
}

/// <summary>
/// Result of <see cref="IComposeDocumentService.SaveDocxAsync"/>.
/// </summary>
/// <param name="Found">True if the drive-item was found and Save succeeded.</param>
/// <param name="VersionId">New SPE version id (Graph drive-item version id) when <see cref="Found"/> is true.</param>
/// <param name="ETag">Updated ETag after the save (matches Graph's response ETag).</param>
/// <param name="Size">New file size after save.</param>
public sealed record ComposeSaveResult(
    bool Found,
    string? VersionId,
    string? ETag,
    long? Size)
{
    /// <summary>Singleton for the "drive-item not found" case.</summary>
    public static ComposeSaveResult NotFound { get; } = new(false, null, null, null);
}
