using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
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

        // Emit the summary text as a chunk so the client's summary display area
        // renders the actual content (matches the engine path's streaming-tokens
        // behavior). Prefer sprk_filesummary; fall back to sprk_filetldr.
        if (TryGetStringProperty(aiOutput, "sprk_filesummary", out var summaryText)
            || TryGetStringProperty(aiOutput, "sprk_filetldr", out summaryText))
        {
            if (!string.IsNullOrWhiteSpace(summaryText))
            {
                yield return AnalysisStreamChunk.TextChunk(summaryText);
            }
        }

        // Step 4: Build Dataverse field mapping directly from the AI output.
        //         The output schema uses sprk_* property names natively, so no
        //         name-translation layer is needed — pass through with typed
        //         Choice coercion for sprk_documenttype + a search profile.
        var fields = BuildDataverseFields(aiOutput, docText.FileName, parentEntity);

        // sprk_filetype is deterministic (comes from the file extension) — don't
        // ask the LLM for it. Column is 10 chars; extension without leading dot,
        // upper-cased, capped defensively.
        var extension = Path.GetExtension(docText.FileName)?.TrimStart('.').ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(extension))
        {
            fields["sprk_filetype"] = extension.Length > 10 ? extension[..10] : extension;
        }

        _logger.LogInformation(
            "Document {DocumentId} profile — mapped {FieldCount} fields for update: {Fields}",
            documentId, fields.Count, string.Join(",", fields.Keys));

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
    /// Case-insensitive top-level string property accessor for the AI structured
    /// output. Returns false when the property is absent or not a string.
    /// </summary>
    private static bool TryGetStringProperty(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object) return false;

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == JsonValueKind.String)
            {
                value = prop.Value.GetString();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Directly maps the AI structured output (whose top-level property names
    /// are the target sprk_document field names per the Action's
    /// <c>sprk_outputschemajson</c>) into a Dataverse-ready field dictionary.
    /// Special handling:
    /// <list type="bullet">
    /// <item><c>sprk_documenttype</c> — string label coerced to Choice option
    /// value via <see cref="DocumentTypeMapper.ToDataverseValue"/>.</item>
    /// <item><c>sprk_entities</c> — array/object values serialized to JSON.</item>
    /// <item><c>sprk_searchprofile</c> — computed from the collected outputs
    /// via <see cref="DocumentProfileFieldMapper.BuildSearchProfile"/>.</item>
    /// </list>
    /// Non-string, non-array, non-object properties (numbers, booleans, nulls)
    /// are copied through as their string representation for logging safety;
    /// consumer schemas today are string-typed so this branch is defensive.
    /// </summary>
    private Dictionary<string, object?> BuildDataverseFields(
        JsonElement root,
        string fileName,
        ParentEntityContext? parentEntity)
    {
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object) return fields;

        // Sibling dict of stringified outputs used for BuildSearchProfile below.
        var stringOutputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in root.EnumerateObject())
        {
            var name = prop.Name;
            var kind = prop.Value.ValueKind;

            switch (kind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    continue;

                case JsonValueKind.String:
                {
                    var stringValue = prop.Value.GetString();
                    if (string.IsNullOrWhiteSpace(stringValue)) continue;

                    if (name.Equals("sprk_documenttype", StringComparison.OrdinalIgnoreCase))
                    {
                        var optionValue = DocumentTypeMapper.ToDataverseValue(stringValue);
                        if (optionValue.HasValue)
                        {
                            // Dataverse SDK Choice/OptionSet attributes require OptionSetValue,
                            // not a raw int. R5 Doc 06 Choice-field coercion pattern.
                            fields[name] = new OptionSetValue(optionValue.Value);
                            stringOutputs[name] = stringValue;
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Could not coerce documentType='{DocType}' to a Dataverse Choice value; dropping the field",
                                stringValue);
                        }
                    }
                    else
                    {
                        fields[name] = stringValue;
                        stringOutputs[name] = stringValue;
                    }
                    break;
                }

                case JsonValueKind.Array:
                case JsonValueKind.Object:
                {
                    var jsonBlob = prop.Value.GetRawText();
                    fields[name] = jsonBlob;
                    stringOutputs[name] = jsonBlob;
                    break;
                }

                default:
                {
                    var raw = prop.Value.GetRawText();
                    fields[name] = raw;
                    stringOutputs[name] = raw;
                    break;
                }
            }
        }

        var searchProfile = DocumentProfileFieldMapper.BuildSearchProfile(
            stringOutputs,
            parentEntityName: parentEntity?.EntityName,
            parentEntityType: parentEntity?.EntityType,
            fileName: fileName);
        if (!string.IsNullOrWhiteSpace(searchProfile))
        {
            fields["sprk_searchprofile"] = searchProfile;
        }

        return fields;
    }
}
