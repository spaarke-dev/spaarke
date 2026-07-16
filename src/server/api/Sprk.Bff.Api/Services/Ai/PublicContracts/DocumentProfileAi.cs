using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IDocumentProfileAi"/>: profiles a saved <c>sprk_document</c>
/// record under the caller's OBO identity by resolving + running the <b>"Document Profiler"</b> Action
/// (catalog code <c>ACT-011</c>) directly on the ADR-043 completion-engine spine, then mapping its
/// structured output onto the record's profile columns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Execution pattern (ADR-043 AI Capability Execution Spine — completion engine).</b> This mirrors
/// the shipped <c>AnalysisEndpoints.ExecuteDocumentProfilePipelineAsync</c> (the Document Upload
/// wizard's profiler), which is the platform's canonical consumer of this SAME Action via the SAME
/// engine. The flow is: resolve the <c>document-profile</c> Binding's Action
/// (<see cref="IActionResolver"/> → <c>sprk_playbookconsumer.sprk_action</c> = ACT-011) → extract the
/// document text under OBO (<see cref="IDocumentTextSource"/>) → run the Action's prompt + output
/// schema through the ADR-043-canonical completion engine (<see cref="IActionRunner"/>, the
/// <c>ActionRunner</c> that consumes the ADR-043 <c>BoundInputs</c>/<c>OperandChannel.Document</c>
/// overload) → map the structured output onto <c>sprk_document</c> columns
/// (<see cref="DocumentProfileOutputMapper"/>) → persist via
/// <see cref="IDocumentDataverseService.UpdateDocumentFieldsAsync"/>.
/// </para>
/// <para>
/// <b>Why NOT the chat dispatch spine / node engine.</b> ADR-043's convergence layer
/// (disposition → <c>OutputRouter</c> → session ledger → <c>OutcomeCard</c>, and the chat
/// <c>SessionDispatchOrchestrator</c>) is chat-session-bound (requires a <c>ChatSession</c>, renders a
/// <c>SessionOutput</c>); its record-write leg is explicitly NotImplemented. A save-time document
/// profile writes to a Dataverse record, not a chat ledger, so the applicable ADR-043 surface is the
/// completion engine only. The retired playbook/node engine
/// (<c>PlaybookOrchestrationService</c> / <c>AppOnlyAnalysisService.ExecutePlaybookAnalysisAsync</c>)
/// is NOT in this path.
/// </para>
/// <para>
/// <b>OBO identity.</b> A Compose-saved file is written to SPE under the user's OBO identity; the BFF
/// Managed Identity is intentionally NOT registered on the SPE container type (Pattern 4), so an
/// app-only download 403s. <see cref="IDocumentTextSource.ExtractFromDocumentIdAsync"/> downloads via
/// OBO (the same identity + SPE call the RAG indexing step uses), which is the one behavioral
/// requirement that distinguishes this facade from the wizard endpoint (which already runs under OBO).
/// </para>
/// <para>
/// <b>ADR-013:</b> <c>ComposeService</c> (CRUD code) injects ONLY this facade. This facade lives under
/// <c>Services/Ai/</c> (the sanctioned AI zone) and may depend on the AI-internal execution seams
/// (<see cref="IActionResolver"/>, <see cref="IActionRunner"/>, <see cref="IDocumentTextSource"/>).
/// </para>
/// <para>
/// <b>Lifetime:</b> Scoped — registered inside the compound AI gate alongside the Linear-consumer
/// stack, so at runtime the three AI seams resolve to their real implementations.
/// <b>Optional AI seams:</b> the three AI-internal seams are optional (nullable) ctor params
/// (ADR-032 optional-via-null-tolerance, the same pattern <c>MatterPreFillService</c> uses) — this
/// keeps unit construction ergonomic and tolerates any registration reordering by degrading to a
/// <see cref="DocumentProfileOutcome.Skipped(string)"/> outcome rather than failing DI when a seam is
/// absent.
/// </para>
/// <para>
/// <b>Best-effort:</b> every failure mode returns a <see cref="DocumentProfileOutcome"/> (never
/// throws) so a caller's save is never blocked or failed by profiling.
/// </para>
/// </remarks>
public sealed class DocumentProfileAi : IDocumentProfileAi
{
    private readonly IDocumentDataverseService _documents;
    private readonly IActionResolver? _actionResolver;
    private readonly IDocumentTextSource? _textSource;
    private readonly IActionRunner? _actionRunner;
    private readonly ILogger<DocumentProfileAi> _logger;

    public DocumentProfileAi(
        IDocumentDataverseService documents,
        ILogger<DocumentProfileAi> logger,
        IActionResolver? actionResolver = null,
        IDocumentTextSource? textSource = null,
        IActionRunner? actionRunner = null)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _actionResolver = actionResolver;
        _textSource = textSource;
        _actionRunner = actionRunner;
    }

    /// <inheritdoc />
    public async Task<DocumentProfileOutcome> ProfileDocumentAsUserAsync(
        Guid documentId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // AI seams are gated under the compound AI gate; when disabled they are unregistered and
        // resolve to null. Degrade cleanly rather than throwing (best-effort contract).
        if (_actionResolver is null || _textSource is null || _actionRunner is null)
        {
            _logger.LogWarning(
                "OBO document-profile: AI execution seams unavailable (AI features disabled) — profile skipped for {DocumentId}.",
                documentId);
            return DocumentProfileOutcome.Skipped("AI features disabled");
        }

        try
        {
            // 1) Resolve the record's SPE pointers + file name from Dataverse. Keeps the clean
            //    skip semantics (no LLM call) when the record is missing or has no SPE file.
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

            var runContext = new LinearRunContext
            {
                ConsumerType = ConsumerTypes.DocumentProfile,
                CorrelationId = httpContext.TraceIdentifier,
                TenantId = httpContext.User?.FindFirst("tid")?.Value
                    ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value,
            };

            // 2) Resolve the "Document Profiler" Action (ACT-011) via the document-profile Binding.
            //    IActionResolver reads sprk_playbookconsumer.sprk_action for ConsumerTypes.DocumentProfile
            //    — the SAME single routing surface the wizard endpoint uses. NOT the playbook engine.
            var action = await _actionResolver.ResolveAsync(ConsumerTypes.DocumentProfile, cancellationToken)
                .ConfigureAwait(false);

            // 3) Extract the document text UNDER THE USER'S OBO IDENTITY. IDocumentTextSource loads the
            //    Dataverse row, downloads the SPE file via OBO, and extracts text (Redis ETag cache) —
            //    the same OBO download the RAG indexing step uses (Pattern 4).
            _logger.LogInformation(
                "OBO document-profile: extracting text for sprk_document {DocumentId} as user " +
                "(drive={DriveId} item={ItemId}) then running action '{ActionName}'.",
                documentId, document.GraphDriveId, document.GraphItemId, action.Name);

            var docText = await _textSource.ExtractFromDocumentIdAsync(documentId, httpContext, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(docText.ExtractedText))
            {
                _logger.LogWarning(
                    "OBO document-profile: no extractable text for sprk_document {DocumentId} — profile failed.",
                    documentId);
                return DocumentProfileOutcome.Failed("document has no extractable text");
            }

            // 4) Run the Action on the ADR-043 completion engine (ActionRunner) — resolve declared
            //    input → render prompt (Action.SystemPrompt) → LLM → structured output (constrained by
            //    Action.OutputSchemaJson). The output's top-level property names ARE the target
            //    sprk_document field names.
            JsonElement aiOutput = await _actionRunner.RunAsync(action, docText, runContext, cancellationToken)
                .ConfigureAwait(false);

            // 5) Map the structured output → sprk_document columns via the shared mapper (the SAME
            //    mapping the wizard pipeline uses — one source of truth, no drift).
            var fields = DocumentProfileOutputMapper.BuildFields(
                aiOutput, docText.FileName, parentEntity: null, _logger);

            if (fields.Count == 0)
            {
                _logger.LogWarning(
                    "OBO document-profile: action produced no mappable fields for sprk_document {DocumentId}.",
                    documentId);
                return DocumentProfileOutcome.Failed("action produced no mappable profile fields");
            }

            // 6) Persist under OBO via the SDK-based document service.
            await _documents.UpdateDocumentFieldsAsync(documentId.ToString(), fields, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "OBO document-profile: sprk_document {DocumentId} profiled + {FieldCount} fields written under OBO [{Fields}].",
                documentId, fields.Count, string.Join(",", fields.Keys));
            return DocumentProfileOutcome.Succeeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
