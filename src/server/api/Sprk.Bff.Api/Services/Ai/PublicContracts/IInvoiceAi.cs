using OpenAI.Chat;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade for the Finance Intelligence module and the Service Bus invoice
/// indexing pipeline. Exposes the precise subset of AI primitives that the invoice
/// classification, extraction, and search flows actually call today.
/// </summary>
/// <remarks>
/// <para>
/// Per refined ADR-013 (2026-05-20), external CRUD code MUST consume AI through this
/// facade rather than injecting <see cref="IOpenAiClient"/> / <see cref="IPlaybookService"/>
/// directly. See ADR-007 for the canonical facade pattern (<see cref="SpeFileStore"/>).
/// </para>
/// <para>
/// Current consumers (Phase 1 inventory, 2026-05-24):
/// - <c>Services/Finance/InvoiceAnalysisService.cs</c> — Playbook A (classification) + Playbook B (extraction)
/// - <c>Services/Finance/InvoiceSearchService.cs</c> — query embedding for hybrid invoice search
/// - <c>Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs</c> — document embedding for invoice indexing
/// </para>
/// <para>
/// Surface intentionally narrow (UQ-07 small-focused default). Three methods cover all
/// observed call sites; <c>ChatMessage</c> is exposed because the Finance models build
/// structured prompts that the deep AI pipeline expects in that shape — replacing it
/// with an SDAP-DTO would require duplicating model-specific assemblies callers already
/// reference.
/// </para>
/// <para>
/// ebilling-r1 task 041 (D-52) added four Action-backed capability methods (<see cref="ClassifyAsync"/>,
/// <see cref="CodeLinesAsync"/>, <see cref="DraftRulesAsync"/>, <see cref="DraftFirmNoticeAsync"/>) with
/// SDAP-domain DTOs (<c>InvoiceAiContracts.cs</c>). <see cref="GetPlaybookByNameAsync"/> and
/// <see cref="GetStructuredCompletionAsync{T}"/> are legacy members kept only until their Finance callers
/// move (ebilling-r1 tasks 042 and 050); do not add new callers.
/// </para>
/// </remarks>
public interface IInvoiceAi
{
    /// <summary>
    /// Look up an analysis playbook by its system name (e.g.
    /// <c>FinanceClassification</c>, <c>FinanceExtraction</c>). The Finance flows use
    /// the playbook <c>Description</c> field as the system prompt (ADR-014: prompts
    /// live in Dataverse, not hard-coded strings).
    /// </summary>
    /// <param name="playbookName">Exact playbook name as registered in Dataverse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The playbook response (includes <see cref="PlaybookResponse.Description"/>
    /// which the Finance services use as the system prompt).</returns>
    /// <exception cref="PlaybookNotFoundException">Thrown when the playbook does not exist.</exception>
    Task<PlaybookResponse> GetPlaybookByNameAsync(
        string playbookName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run a structured (JSON-schema-constrained) chat completion. Used for the
    /// classification + extraction phases — both require guaranteed-valid JSON output
    /// matching a known schema.
    /// </summary>
    /// <typeparam name="T">DTO that the JSON response deserializes into.</typeparam>
    /// <param name="messages">Chat messages (system + user). Caller is responsible for
    /// assembling the system prompt (typically from <see cref="GetPlaybookByNameAsync"/>).</param>
    /// <param name="jsonSchema">JSON schema the response must conform to.</param>
    /// <param name="schemaName">Schema name (passed to the OpenAI structured-output API).</param>
    /// <param name="deploymentName">Azure OpenAI deployment name (e.g. <c>gpt-4o-mini</c>
    /// for classification, <c>gpt-4o</c> for extraction — Finance picks per use case).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    Task<T> GetStructuredCompletionAsync<T>(
        IEnumerable<ChatMessage> messages,
        BinaryData jsonSchema,
        string schemaName,
        string deploymentName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a vector embedding for a single text input. Used for the invoice query
    /// (search-time) and individual document indexing (background job). Both call paths
    /// pin to <c>text-embedding-3-large</c> at 3072 dimensions.
    /// </summary>
    /// <param name="text">Text to embed.</param>
    /// <param name="model">Embedding model override. Pass <c>null</c> to use the
    /// configured default (production: <c>text-embedding-3-large</c>).</param>
    /// <param name="dimensions">Embedding dimensions override. Pass <c>null</c> to use
    /// the configured default (production: 3072).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Vector embedding.</returns>
    Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        string? model = null,
        int? dimensions = null,
        CancellationToken cancellationToken = default);

    // ── Action-backed capability methods (ebilling-r1 task 041, D-52 / ADR-039 / ADR-043) ──────────
    //
    // Each resolves its Action ONLY through the Binding for its ConsumerTypes key and runs it ONLY through
    // IActionRunner: the prompt, output schema and model tier live in the Action / Binding rows, never in
    // C#. All four FAIL CLOSED (FR-46) — they never return a default or fall back to a hard-coded prompt:
    //   - InvoiceAiException(ai.invoice.binding_missing)   — no Binding / Action for the consumer type
    //   - InvoiceAiException(ai.invoice.completion_failed) — the run failed
    //   - InvoiceAiException(ai.invoice.output_invalid)    — the output does not parse into the result
    //   - FeatureDisabledException(ai.invoice.disabled or ai.linear-consumers.disabled) — AI kill switch (ADR-032)
    //   - OperationCanceledException — the caller cancelled

    /// <summary>
    /// Classify a Finance attachment as invoice / not invoice, with best-effort header hints
    /// (consumer type <see cref="ConsumerTypes.InvoiceClassify"/>, FR-46).
    /// </summary>
    /// <exception cref="InvoiceAiException">Binding missing, completion failed or output invalid.</exception>
    Task<InvoiceClassifyResult> ClassifyAsync(
        InvoiceClassifyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assign UTBMS codes and observations to one chunk of invoice lines from the allowed code lists
    /// (consumer type <see cref="ConsumerTypes.InvoiceLineCode"/>, FR-22). The caller refuses any returned
    /// code outside the line's code set.
    /// </summary>
    /// <exception cref="InvoiceAiException">Binding missing, completion failed or output invalid.</exception>
    Task<InvoiceCodeLinesResult> CodeLinesAsync(
        InvoiceCodeLinesRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Propose billing rules from a guideline / rate / procedure document, only from the supplied rule-type
    /// catalogue, listing text that could not be converted (consumer type
    /// <see cref="ConsumerTypes.BillingRuleDraft"/>, FR-27).
    /// </summary>
    /// <exception cref="InvoiceAiException">Binding missing, completion failed or output invalid.</exception>
    Task<BillingRuleDraftResult> DraftRulesAsync(
        BillingRuleDraftRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Draft the subject and explanatory prose of a firm notice from the outcome and findings summary
    /// (consumer type <see cref="ConsumerTypes.InvoiceFirmNotice"/>, FR-50). Prose only: amounts, line
    /// references and clause quotes are inserted by the caller's template.
    /// </summary>
    /// <exception cref="InvoiceAiException">Binding missing, completion failed or output invalid.</exception>
    Task<InvoiceFirmNoticeDraft> DraftFirmNoticeAsync(
        InvoiceFirmNoticeRequest request,
        CancellationToken cancellationToken = default);
}
