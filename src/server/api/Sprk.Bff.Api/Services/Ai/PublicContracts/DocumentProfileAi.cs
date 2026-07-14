using Microsoft.AspNetCore.Http;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IDocumentProfileAi"/>: profiles a saved document under the
/// caller's OBO identity by downloading its SPE file as the user, then delegating the
/// extract → classify → summarize → field-map → Dataverse-write pipeline to the EXISTING app-only
/// profiling method.
/// </summary>
/// <remarks>
/// <para>
/// <b>The single change from the broken app-only path is the download identity.</b>
/// <see cref="AppOnlyAnalysisService.AnalyzeDocumentAsync"/> downloads via
/// <c>ISpeFileOperations.DownloadFileAsync</c> (app-only / Managed Identity), which 403s on a
/// user-OBO-written SPE item. This facade instead downloads via
/// <see cref="ISpeFileOperations.DownloadFileAsUserAsync"/> (OBO — the same identity + the same SPE
/// call the RAG indexing step already uses in <c>FileIndexingService.IndexFileAsync</c>) and hands
/// the resulting stream to <see cref="IAppOnlyAnalysisService.AnalyzeDocumentFromStreamAsync"/>,
/// which performs text extraction, playbook resolution, tool execution, the
/// <c>BuildProfileUpdateFromOutputs</c> field mapping, and the
/// <c>IDocumentDataverseService.UpdateDocumentAsync</c> write — all unchanged. No profiling or
/// mapping logic is duplicated here (§11 default-to-reuse).
/// </para>
/// <para>
/// <b>Lifetime:</b> Scoped — depends on the scoped <see cref="IAppOnlyAnalysisService"/> and runs in
/// the OBO request scope.
/// </para>
/// <para>
/// <b>Best-effort:</b> every failure mode returns a <see cref="DocumentProfileOutcome"/> (never
/// throws) so a caller's save is never blocked or failed by profiling.
/// </para>
/// </remarks>
public sealed class DocumentProfileAi : IDocumentProfileAi
{
    private readonly IDocumentDataverseService _documents;
    private readonly ISpeFileOperations _spe;
    private readonly IAppOnlyAnalysisService _analysis;
    private readonly ILogger<DocumentProfileAi> _logger;

    public DocumentProfileAi(
        IDocumentDataverseService documents,
        ISpeFileOperations spe,
        IAppOnlyAnalysisService analysis,
        ILogger<DocumentProfileAi> logger)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DocumentProfileOutcome> ProfileDocumentAsUserAsync(
        Guid documentId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        try
        {
            // 1) Resolve the record's SPE pointers + file name from Dataverse (same lookup the
            //    app-only worker does — AppOnlyAnalysisService.AnalyzeDocumentAsync step 1/2).
            var document = await _documents.GetDocumentAsync(documentId.ToString(), cancellationToken)
                .ConfigureAwait(false);
            if (document is null)
            {
                _logger.LogWarning(
                    "OBO document-profile: sprk_document {DocumentId} not found — profile skipped.",
                    documentId);
                return DocumentProfileOutcome.Skipped("document not found in Dataverse");
            }

            if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
            {
                _logger.LogWarning(
                    "OBO document-profile: sprk_document {DocumentId} has no SPE pointer " +
                    "(drive={DriveId} item={ItemId}) — profile skipped.",
                    documentId, document.GraphDriveId, document.GraphItemId);
                return DocumentProfileOutcome.Skipped("document has no SPE file reference");
            }

            var fileName = document.FileName ?? document.Name ?? "document";

            // 2) Download the file UNDER THE USER'S OBO IDENTITY. This is the ONLY behavioral change
            //    from the broken app-only path: the user wrote the file to SPE, so only the user
            //    (or an app registered on the container type, which the BFF MI is intentionally not)
            //    can read it (Pattern 4). Same SPE call the RAG indexing step already uses.
            _logger.LogInformation(
                "OBO document-profile: downloading sprk_document {DocumentId} as user " +
                "(drive={DriveId} item={ItemId}) for profiling.",
                documentId, document.GraphDriveId, document.GraphItemId);

            await using var stream = await _spe.DownloadFileAsUserAsync(
                    httpContext, document.GraphDriveId, document.GraphItemId, cancellationToken)
                .ConfigureAwait(false);

            if (stream is null)
            {
                _logger.LogWarning(
                    "OBO document-profile: OBO download returned null for sprk_document {DocumentId} " +
                    "(drive={DriveId} item={ItemId}) — profile failed.",
                    documentId, document.GraphDriveId, document.GraphItemId);
                return DocumentProfileOutcome.Failed("OBO SPE download returned null");
            }

            // 3) Reuse the EXISTING profiling pipeline unchanged (extract → playbook → field-map →
            //    UpdateDocumentAsync). AnalyzeDocumentFromStreamAsync is the stream-based entry the
            //    email path already uses when the file is in hand — here the hand is OBO.
            var result = await _analysis.AnalyzeDocumentFromStreamAsync(
                    documentId, fileName, stream, playbookName: null, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "OBO document-profile: sprk_document {DocumentId} profiled + fields written under OBO.",
                    documentId);
                return DocumentProfileOutcome.Succeeded();
            }

            _logger.LogWarning(
                "OBO document-profile: profiling returned failure for sprk_document {DocumentId}: {Error}",
                documentId, result.ErrorMessage ?? "(no message)");
            return DocumentProfileOutcome.Failed(result.ErrorMessage ?? "profiling failed");
        }
        catch (Exception ex)
        {
            // Best-effort: never propagate. The caller's save has already succeeded on its own terms.
            _logger.LogWarning(ex,
                "OBO document-profile: threw while profiling sprk_document {DocumentId} — best-effort, caller unaffected.",
                documentId);
            return DocumentProfileOutcome.Failed(ex.GetType().Name);
        }
    }
}
