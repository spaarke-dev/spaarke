using Microsoft.AspNetCore.Http;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade (ADR-013 / ADR-007) that profiles a saved <c>sprk_document</c> record —
/// classification + summary + entity extraction into the profile fields — UNDER THE CALLING
/// USER'S OBO IDENTITY.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this facade exists (spaarkeai-compose-r2, UAT #7b):</b> when Compose saves a document
/// on the create-on-save path it writes the SPE drive-item under the user's OBO identity
/// (<c>ISpeFileOperations.UploadSmallAsUserAsync</c> / <c>ReplaceFileContentAsUserAsync</c>).
/// The BFF Managed Identity is intentionally NOT registered on the SPE container type
/// (Pattern 4 / <c>managed-identity-resource-rbac.md</c>), so the app-only
/// <c>AppOnlyDocumentAnalysis</c> background job 403s when it tries to download that same file to
/// profile it — the profile fields silently never populate. This is the EXACT constraint the RAG
/// indexing step already solved by running INLINE under OBO in the request scope
/// (<see cref="IPostUploadIndexingEnqueuer.EnqueueIfApplicableAsync"/> →
/// <c>IFileIndexingService.IndexFileAsync</c>, which uses
/// <c>ISpeFileOperations.DownloadFileAsUserAsync</c>). This facade mirrors that pattern for
/// profiling: download under OBO, then reuse the EXISTING profiling pipeline unchanged.
/// </para>
/// <para>
/// <b>ADR-013:</b> this is the CRUD-safe seam. <c>ComposeService</c> (CRUD code) injects only this
/// facade — never <c>IAppOnlyAnalysisService</c>, <c>IOpenAiClient</c>, <c>IPlaybookService</c>, or
/// <c>IConsumerRoutingService</c>. The implementation lives under <c>Services/Ai/</c> (the sanctioned
/// AI zone) and is free to depend on those AI-internal types. See
/// <c>ADR013_ComposeFacadeTests</c> / <c>ADR013_AiBoundaryTests</c>.
/// </para>
/// <para>
/// <b>Reuse (§11 default-to-reuse):</b> the implementation delegates the extract → classify →
/// summarize → field-map → Dataverse-write pipeline to the existing app-only profiling method;
/// only the file-download identity changes to OBO. No profiling logic, playbook resolution, or
/// field mapping is duplicated.
/// </para>
/// <para>
/// <b>ADR Tension (project-scoped exception, CLAUDE.md §6.5 path A):</b> the canonical
/// <c>IDocumentProfileAi</c> is slated to be core-owned by the paused <c>spaarke-ai-architecture
/// -redesign-r2</c> effort. This compose-r2 facade is a narrowly-scoped stand-in so a saved Compose
/// document lands as a complete DMS record now; it should be reconciled with the core contract when
/// that work resumes. Documented in <c>projects/spaarkeai-compose-r2/design.md</c> "ADR Tensions".
/// </para>
/// </remarks>
public interface IDocumentProfileAi
{
    /// <summary>
    /// Profiles the given <c>sprk_document</c> record under the caller's OBO identity: downloads the
    /// record's SPE file as the user, extracts its text, runs the Document Profile
    /// classification/summary, and writes the resulting profile fields back onto the record.
    /// </summary>
    /// <param name="documentId">The Dataverse <c>sprk_document</c> id to profile. The record must
    /// already carry its SPE pointers (<c>sprk_graphdriveid</c> + <c>sprk_graphitemid</c>).</param>
    /// <param name="httpContext">The current request context — required for OBO token exchange. The
    /// download runs as the same user that wrote the file to SPE, which is the only identity
    /// guaranteed read access on the file's SPE ACL (Pattern 4).</param>
    /// <param name="cancellationToken">Request-scope cancellation token.</param>
    /// <returns>A best-effort outcome. Callers treat this as advisory — profiling NEVER throws to
    /// the caller (implementation swallows + logs), so a save is never blocked or failed by it.</returns>
    Task<DocumentProfileOutcome> ProfileDocumentAsUserAsync(
        Guid documentId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of an OBO document-profile attempt. Mirrors <c>PostUploadIndexingResult</c> so callers
/// can project a per-step completion signal the same way they do for indexing.
/// </summary>
public sealed record DocumentProfileOutcome
{
    /// <summary>True when the record's profile fields were updated.</summary>
    public required bool Success { get; init; }

    /// <summary>If the profile ran but failed (download null, extraction/playbook error), the reason.
    /// Null when successful or skipped.</summary>
    public string? FailureReason { get; init; }

    /// <summary>If the profile was skipped before running (no record, no SPE pointer, unsupported
    /// file), the reason. Null when successful or failed.</summary>
    public string? SkipReason { get; init; }

    public static DocumentProfileOutcome Succeeded() => new() { Success = true };

    public static DocumentProfileOutcome Failed(string reason) =>
        new() { Success = false, FailureReason = reason };

    public static DocumentProfileOutcome Skipped(string reason) =>
        new() { Success = false, SkipReason = reason };
}
