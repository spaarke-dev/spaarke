using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Intake;

/// <summary>
/// Reads the text of a request's filed document for the Classify stage (spaarke-intake-pipeline-r1 task 013, FR-07
/// stage 3): <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.IAgreementAi.ClassifyAsync"/> takes text, and the run
/// holds only document ids.
/// </summary>
/// <remarks>
/// <para>App-only, like every background read of a filed document (<c>AnalysisDocumentLoader.ExtractDocumentTextAsync</c>,
/// unified-access-control-r2 task 171): the row's SPE pointer is followed AS THE APPLICATION only after
/// <see cref="RecordContainerResolver.IsDocumentPointerContainerAllowedAsync(Guid, string?, string?, CancellationToken)"/>
/// verified it (ADR-007: the bytes come through <see cref="ISpeFileOperations"/>). The text is returned to the caller and
/// never logged; the log carries the document id and a reason code only (ADR-015). Any document it cannot read answers
/// null, which the orchestrator routes to manual triage (FR-10), never a guess.</para>
/// <para>§11: <i>Existing</i> - <c>AnalysisDocumentLoader</c> does the same read but is registered inside the AI gate,
/// takes a <c>DocumentEntity</c>, logs the file name and returns placeholder text on failure; <c>DocumentTextSource</c>
/// wraps it. <i>Extension</i> - neither is resolvable when the AI gate is off, and placeholder text would be classified
/// as if it were the document. <i>Cost of doing nothing</i> - the Classify stage has no text to classify, so no request
/// ever routes to a harness (FR-07 stage 3, FR-10).</para>
/// </remarks>
public class IntakeContentTextReader
{
    private static readonly string[] DocumentColumns = ["sprk_graphdriveid", "sprk_graphitemid", "sprk_filename"];

    private readonly IGenericEntityService _entities;
    private readonly RecordContainerResolver _containers;
    private readonly ISpeFileOperations _spe;
    private readonly ITextExtractor _extractor;
    private readonly ILogger<IntakeContentTextReader> _logger;

    public IntakeContentTextReader(
        IGenericEntityService entities,
        RecordContainerResolver containers,
        ISpeFileOperations spe,
        ITextExtractor extractor,
        ILogger<IntakeContentTextReader> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The document's extracted text, or null when it has no file, its pointer is refused, its type is not extractable,
    /// or extraction fails or yields nothing. Transport faults (Dataverse, SPE) throw, so the run retries.
    /// </summary>
    public virtual async Task<string?> ReadTextAsync(Guid documentId, CancellationToken ct)
    {
        Entity row;
        try
        {
            row = await _entities.RetrieveAsync("sprk_document", documentId, DocumentColumns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (RecordContainerResolver.IsRecordNotFoundInChain(ex))
        {
            return Unreadable(documentId, "document_missing");
        }

        var driveId = row.GetAttributeValue<string>("sprk_graphdriveid");
        var itemId = row.GetAttributeValue<string>("sprk_graphitemid");
        var fileName = row.GetAttributeValue<string>("sprk_filename");
        if (string.IsNullOrWhiteSpace(driveId) || string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(fileName))
        {
            return Unreadable(documentId, "no_file");
        }

        if (!_extractor.IsSupported(Path.GetExtension(fileName).ToLowerInvariant()))
        {
            return Unreadable(documentId, "type_not_extractable");
        }

        if (!await _containers.IsDocumentPointerContainerAllowedAsync(documentId, driveId, itemId, ct).ConfigureAwait(false))
        {
            return Unreadable(documentId, "pointer_refused");
        }

        await using var stream = await _spe.DownloadFileAsync(driveId, itemId, ct).ConfigureAwait(false);
        if (stream is null)
        {
            return Unreadable(documentId, "download_empty");
        }

        var extracted = await _extractor.ExtractAsync(stream, fileName, ct).ConfigureAwait(false);
        if (!extracted.Success || extracted.IsVisionRequired || string.IsNullOrWhiteSpace(extracted.Text))
        {
            return Unreadable(documentId, "extraction_failed");
        }

        return extracted.Text;
    }

    private string? Unreadable(Guid documentId, string reason)
    {
        _logger.LogInformation("Intake content {DocumentId} has no readable text ({Reason})", documentId, reason);
        return null;
    }
}
