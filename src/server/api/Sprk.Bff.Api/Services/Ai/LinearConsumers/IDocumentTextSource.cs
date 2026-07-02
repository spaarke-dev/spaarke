using Microsoft.AspNetCore.Http;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Extracts <see cref="DocumentText"/> from either a Dataverse document row
/// (Doc Upload / Profile path) or a directly-uploaded file (File Summarize
/// path). Shared primitive across Linear AI Consumer services.
/// </summary>
/// <remarks>
/// R7 Wave 12 (2026-07-02). See
/// <c>docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md</c>.
/// Registered as Scoped because underlying <see cref="AnalysisDocumentLoader"/>
/// depends on <see cref="IHttpContextAccessor"/> for tenant scoping.
/// </remarks>
public interface IDocumentTextSource
{
    /// <summary>
    /// Extract text from a Dataverse document row. Loads the row, downloads the
    /// SPE file via OBO, runs extraction (with Redis ETag cache).
    /// </summary>
    /// <param name="documentId">Row id of the <c>sprk_document</c> record.</param>
    /// <param name="httpContext">HTTP context (needed for OBO token exchange).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DocumentText> ExtractFromDocumentIdAsync(
        Guid documentId,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Extract text from a directly-uploaded file (File Summarize path).
    /// No Dataverse row involved; SPE fields on the returned <see cref="DocumentText"/>
    /// will be null.
    /// </summary>
    Task<DocumentText> ExtractFromFileAsync(
        IFormFile file,
        CancellationToken cancellationToken);
}
