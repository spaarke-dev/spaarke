using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Linear AI Consumer for the Document Upload / Profile Document flow.
/// Replaces the Playbook Engine dispatch of the "Document Profile" playbook
/// (see <c>projects/spaarke-ai-platform-unification-r7/notes/wave12-linear-consumer-migration.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// R7 Wave 12 (2026-07-02). Composes the four shared primitives:
/// <see cref="IActionResolver"/>, <see cref="IDocumentTextSource"/>,
/// <see cref="IActionRunner"/>, plus <see cref="IDocumentDataverseService"/>
/// (typed persistence via SDK — no PATCH construction, no metadata calls)
/// and <see cref="IPostUploadIndexingEnqueuer"/> (RAG indexing).
/// </para>
/// <para>
/// Emits <see cref="AnalysisStreamChunk"/> events so the endpoint can write
/// SSE identically to the engine path — preserving the client contract during
/// migration.
/// </para>
/// </remarks>
public sealed class DocumentProfileService
{
    private readonly IActionResolver _actionResolver;
    private readonly IDocumentTextSource _textSource;
    private readonly IActionRunner _actionRunner;
    private readonly IDocumentDataverseService _documentService;
    private readonly IPostUploadIndexingEnqueuer _indexingEnqueuer;
    private readonly ILogger<DocumentProfileService> _logger;

    public DocumentProfileService(
        IActionResolver actionResolver,
        IDocumentTextSource textSource,
        IActionRunner actionRunner,
        IDocumentDataverseService documentService,
        IPostUploadIndexingEnqueuer indexingEnqueuer,
        ILogger<DocumentProfileService> logger)
    {
        _actionResolver = actionResolver;
        _textSource = textSource;
        _actionRunner = actionRunner;
        _documentService = documentService;
        _indexingEnqueuer = indexingEnqueuer;
        _logger = logger;
    }

    /// <summary>
    /// Execute the Document Profile linear pipeline. Emits progress + done
    /// chunks the endpoint can write to the SSE response stream.
    /// </summary>
    /// <param name="documentId">The <c>sprk_document</c> row id being profiled.</param>
    /// <param name="httpContext">HTTP context (OBO token exchange for SPE download).</param>
    /// <param name="parentEntity">Optional parent entity for search-profile enrichment / RAG index cascade.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<AnalysisStreamChunk> ExecuteAsync(
        Guid documentId,
        HttpContext httpContext,
        ParentEntityContext? parentEntity,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return AnalysisStreamChunk.Metadata(documentId, $"document-profile:{documentId}");

        // Build the per-request context. TenantId is required for RAG indexing —
        // resolve it from the caller's JWT (same claim path AnalysisDocumentLoader uses).
        var tenantId = httpContext.User?.FindFirst("tid")?.Value
            ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        var runContext = new LinearRunContext
        {
            ConsumerType = ConsumerTypes.DocumentProfile,
            CorrelationId = httpContext.TraceIdentifier,
            TenantId = tenantId,
        };

        // Step 1: Resolve the Action row (SystemPrompt + OutputSchemaJson + Temperature).
        yield return AnalysisStreamChunk.Progress("resolving_action", "Resolving action configuration…");
        var (action, actionError) = await TryResolveActionAsync(cancellationToken);
        if (actionError != null)
        {
            yield return AnalysisStreamChunk.FromError(actionError);
            yield break;
        }

        // Step 2: Extract document text (SPE download + text extraction with Redis ETag cache).
        yield return AnalysisStreamChunk.Progress("extracting_text", "Extracting document text…");
        var (docText, textError) = await TryExtractTextAsync(documentId, httpContext, cancellationToken);
        if (textError != null)
        {
            yield return AnalysisStreamChunk.FromError(textError);
            yield break;
        }
        if (string.IsNullOrWhiteSpace(docText!.ExtractedText))
        {
            _logger.LogWarning("Document {DocumentId} has no extractable text; skipping profile", documentId);
            yield return AnalysisStreamChunk.FromError("Document has no extractable text.");
            yield break;
        }

        // Step 3: Run the LLM via the Action's prompt + schema.
        yield return AnalysisStreamChunk.Progress("calling_llm", "Analyzing document with AI…");
        var (aiOutput, llmError) = await TryRunLlmAsync(action!, docText, runContext, documentId, cancellationToken);
        if (llmError != null)
        {
            yield return AnalysisStreamChunk.FromError(llmError);
            yield break;
        }

        // Step 4: Build Dataverse field mapping using the existing DocumentProfileFieldMapper.
        //         Convert JsonElement properties → string dict → sprk_* field dict.
        var outputsAsStrings = ExtractOutputsAsStrings(aiOutput);
        var parentName = parentEntity?.EntityName;
        var parentType = parentEntity?.EntityType;
        var fields = DocumentProfileFieldMapper.CreateFieldMapping(
            outputsAsStrings,
            parentEntityName: parentName,
            parentEntityType: parentType,
            fileName: docText.FileName);

        // Choice-field coercion for sprk_documenttype (string label → int option value).
        if (fields.TryGetValue("sprk_documenttype", out var docTypeRaw) && docTypeRaw is string docTypeLabel)
        {
            var optionValue = DocumentTypeMapper.ToDataverseValue(docTypeLabel);
            if (optionValue.HasValue)
            {
                fields["sprk_documenttype"] = optionValue.Value;
            }
            else
            {
                _logger.LogWarning(
                    "Could not coerce documentType='{DocType}' to a Dataverse Choice value; dropping the field",
                    docTypeLabel);
                fields.Remove("sprk_documenttype");
            }
        }

        // Step 5: Persist via the SDK-based document service (no PATCH construction).
        yield return AnalysisStreamChunk.Progress("updating_record", "Updating document record…");
        var updateError = await TryUpdateFieldsAsync(documentId, fields, cancellationToken);
        if (updateError != null)
        {
            yield return AnalysisStreamChunk.FromError(updateError);
            yield break;
        }

        // Step 6: Enqueue RAG indexing (best-effort — failure is logged but non-fatal).
        yield return AnalysisStreamChunk.Progress("enqueuing_indexing", "Queuing document for search indexing…");
        _ = await TryEnqueueIndexingAsync(
            documentId, docText, tenantId, parentEntity, httpContext, cancellationToken);

        // Terminator — clients look for Type="done" then [DONE] SSE terminator to close.
        yield return AnalysisStreamChunk.Completed(
            documentId,
            new TokenUsage(Input: 0, Output: 0));
    }

    private async Task<(AnalysisAction? action, string? error)> TryResolveActionAsync(CancellationToken ct)
    {
        try
        {
            var action = await _actionResolver.ResolveAsync(ConsumerTypes.DocumentProfile, ct);
            return (action, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve Document Profile action");
            return (null, $"Failed to resolve action: {ex.Message}");
        }
    }

    private async Task<(DocumentText? text, string? error)> TryExtractTextAsync(
        Guid documentId, HttpContext httpContext, CancellationToken ct)
    {
        try
        {
            var text = await _textSource.ExtractFromDocumentIdAsync(documentId, httpContext, ct);
            return (text, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text for document {DocumentId}", documentId);
            return (null, $"Failed to extract document text: {ex.Message}");
        }
    }

    private async Task<(JsonElement output, string? error)> TryRunLlmAsync(
        AnalysisAction action, DocumentText docText, LinearRunContext ctx, Guid documentId, CancellationToken ct)
    {
        try
        {
            var output = await _actionRunner.RunAsync(action, docText, ctx, ct);
            return (output, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed for document {DocumentId}", documentId);
            return (default, $"AI analysis failed: {ex.Message}");
        }
    }

    private async Task<string?> TryUpdateFieldsAsync(
        Guid documentId, Dictionary<string, object?> fields, CancellationToken ct)
    {
        try
        {
            await _documentService.UpdateDocumentFieldsAsync(documentId.ToString(), fields, ct);
            _logger.LogInformation(
                "Updated document {DocumentId} with {FieldCount} profile fields",
                documentId, fields.Count);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update document {DocumentId} with profile fields", documentId);
            return $"Failed to update document record: {ex.Message}";
        }
    }

    private async Task<bool> TryEnqueueIndexingAsync(
        Guid documentId,
        DocumentText docText,
        string? tenantId,
        ParentEntityContext? parentEntity,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(docText.GraphDriveId) || string.IsNullOrEmpty(docText.GraphItemId))
        {
            _logger.LogInformation(
                "Skipping RAG indexing for document {DocumentId}: missing SPE identifiers", documentId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning(
                "Skipping RAG indexing for document {DocumentId}: no tenantId in caller claims", documentId);
            return false;
        }

        var request = new PostUploadIndexingRequest(
            TenantId: tenantId,
            DriveId: docText.GraphDriveId,
            ItemId: docText.GraphItemId,
            FileName: docText.FileName,
            FileSizeBytes: null,
            ContentType: null,
            DocumentId: documentId.ToString(),
            ParentEntity: parentEntity,
            SearchIndexName: null,
            Source: "LinearDocumentProfile",
            CorrelationId: httpContext.TraceIdentifier);

        try
        {
            var result = await _indexingEnqueuer.EnqueueIfApplicableAsync(request, httpContext, cancellationToken);
            return result.JobSubmitted;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG indexing enqueue threw for document {DocumentId}", documentId);
            return false;
        }
    }

    /// <summary>
    /// Convert the structured-output JSON's top-level properties into a
    /// <c>Dictionary&lt;string, string?&gt;</c> compatible with
    /// <see cref="DocumentProfileFieldMapper.CreateFieldMapping"/>. Complex
    /// values (arrays, objects) are serialized as their JSON representation
    /// so the mapper's Entities branch (JSON validation) works as-is.
    /// </summary>
    private static Dictionary<string, string?> ExtractOutputsAsStrings(JsonElement root)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object) return result;

        foreach (var prop in root.EnumerateObject())
        {
            var value = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Null => null,
                JsonValueKind.True or JsonValueKind.False => prop.Value.GetBoolean().ToString(),
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.Array or JsonValueKind.Object => prop.Value.GetRawText(),
                _ => prop.Value.GetRawText(),
            };
            result[prop.Name] = value;
        }

        return result;
    }
}
