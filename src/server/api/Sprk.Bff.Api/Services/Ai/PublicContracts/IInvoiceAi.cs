namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade for the Finance Intelligence module and the Service Bus invoice
/// indexing pipeline. Exposes the precise subset of AI primitives that the invoice
/// classification, reading, coding and search flows actually call today.
/// </summary>
/// <remarks>
/// <para>
/// Per refined ADR-013 (2026-05-20), external CRUD code MUST consume AI through this
/// facade rather than injecting <see cref="IOpenAiClient"/> / <see cref="IPlaybookService"/>
/// directly. See ADR-007 for the canonical facade pattern (<see cref="SpeFileStore"/>).
/// </para>
/// <para>
/// Embeddings (<see cref="GenerateEmbeddingAsync"/>) serve:
/// - <c>Services/Finance/InvoiceSearchService.cs</c> — query embedding for hybrid invoice search
/// - <c>Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs</c> — document embedding for invoice indexing
/// </para>
/// <para>
/// ebilling-r1 task 041 (D-52) added the Action-backed capability methods (<see cref="ClassifyAsync"/>,
/// <see cref="CodeLinesAsync"/>, <see cref="DraftRulesAsync"/>, <see cref="DraftFirmNoticeAsync"/>; later
/// <see cref="MatchCodeSetAsync"/>) with SDAP-domain DTOs (<c>InvoiceAiContracts.cs</c>), and task 043 added
/// <see cref="ReadInvoiceAsync"/>. The legacy by-name playbook lookup and C#-schema structured completion were
/// removed in task 050b: the prompt, output schema and model tier live in the Action / Binding rows (D-52).
/// </para>
/// </remarks>
public interface IInvoiceAi
{
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

    // ── Action-backed capability methods (ebilling-r1 task 041, D-52 / ADR-039 / ADR-043; MatchCodeSetAsync task 054) ──
    //
    // Each resolves its Action ONLY through the Binding for its ConsumerTypes key and runs it ONLY through
    // IActionRunner: the prompt, output schema and model tier live in the Action / Binding rows, never in
    // C#. All five FAIL CLOSED (FR-46) — they never return a default or fall back to a hard-coded prompt:
    //   - InvoiceAiException(ai.invoice.binding_missing)   — the Action could not be resolved for the consumer type
    //   - InvoiceAiException(ai.invoice.completion_failed) — the run failed
    //   - InvoiceAiException(ai.invoice.output_invalid)    — the output does not parse into the result
    //   - FeatureDisabledException(ai.invoice.disabled or ai.linear-consumers.disabled) — AI kill switch (ADR-032)
    //   - OperationCanceledException — the caller cancelled
    //
    // The reason codes classify WHERE the failure happened, not whether retrying helps:
    //   - binding_missing is NOT proof the Binding row is absent. The platform's routing read swallows every
    //     non-cancellation exception and reports "no Action routed", so a transient routing-table read failure
    //     (platform issue #1936) produces the same code as a genuinely missing Binding. Callers retry it a
    //     bounded number of times before marking work Failed.
    //   - completion_failed covers transient model / transport failures AND permanent Action misconfiguration
    //     (for example an empty SystemPrompt or OutputSchemaJson on the Action row), so it is not always
    //     retryable either.
    //   - output_invalid means the model answered but not in the contract; a retry may or may not help.

    /// <summary>
    /// Classify a Finance attachment as invoice / not invoice, with best-effort header hints
    /// (consumer type <see cref="ConsumerTypes.InvoiceClassify"/>, FR-46).
    /// </summary>
    /// <exception cref="InvoiceAiException">Binding missing, completion failed or output invalid; the code classifies where the failure happened, not whether a retry helps (see the notes above).</exception>
    /// <exception cref="ArgumentException"><see cref="InvoiceClassifyRequest.DocumentText"/> is empty or whitespace.</exception>
    Task<InvoiceClassifyResult> ClassifyAsync(
        InvoiceClassifyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assign UTBMS codes and observations to one chunk of invoice lines from the allowed code lists
    /// (consumer type <see cref="ConsumerTypes.InvoiceLineCode"/>, FR-22). The caller refuses any returned
    /// code outside the line's code set.
    /// </summary>
    /// <exception cref="InvoiceAiException">Binding missing, completion failed or output invalid; the code classifies where the failure happened, not whether a retry helps (see the notes above).</exception>
    /// <exception cref="ArgumentException"><see cref="InvoiceCodeLinesRequest.Lines"/> is empty.</exception>
    Task<InvoiceCodeLinesResult> CodeLinesAsync(
        InvoiceCodeLinesRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decide which candidate code set a matter type or practice area should be mapped to, or that none fits, with a 0-1
    /// confidence and a short reason (consumer type <see cref="ConsumerTypes.InvoiceCodeSetMatch"/>, FR-22, D-93). The caller
    /// refuses any returned id that is not one of the request's candidates.
    /// </summary>
    /// <exception cref="InvoiceAiException">Binding missing, completion failed or output invalid; the code classifies where the failure happened, not whether a retry helps (see the notes above).</exception>
    /// <exception cref="ArgumentException"><see cref="InvoiceCodeSetMatchRequest.ReferenceName"/> is empty or <see cref="InvoiceCodeSetMatchRequest.Candidates"/> is empty.</exception>
    Task<InvoiceCodeSetMatchResult> MatchCodeSetAsync(
        InvoiceCodeSetMatchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Propose billing rules from a guideline / rate / procedure document, only from the supplied rule-type
    /// catalogue, listing text that could not be converted (consumer type
    /// <see cref="ConsumerTypes.BillingRuleDraft"/>, FR-27).
    /// </summary>
    /// <exception cref="InvoiceAiException">Binding missing, completion failed or output invalid; the code classifies where the failure happened, not whether a retry helps (see the notes above).</exception>
    /// <exception cref="ArgumentException"><see cref="BillingRuleDraftRequest.DocumentText"/> is empty or whitespace, <see cref="BillingRuleDraftRequest.RuleTypes"/> is empty, or a rule type's <see cref="BillingRuleTypeOption.ParameterSchemaJson"/> is not a valid JSON object.</exception>
    Task<BillingRuleDraftResult> DraftRulesAsync(
        BillingRuleDraftRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Draft the subject and explanatory prose of a firm notice from the outcome and findings summary
    /// (consumer type <see cref="ConsumerTypes.InvoiceFirmNotice"/>, FR-50). Prose only: amounts, line
    /// references and clause quotes are inserted by the caller's template.
    /// </summary>
    /// <exception cref="InvoiceAiException">Binding missing, completion failed or output invalid; the code classifies where the failure happened, not whether a retry helps (see the notes above).</exception>
    /// <exception cref="ArgumentNullException">The request or its <see cref="InvoiceFirmNoticeRequest.Findings"/> is null.</exception>
    Task<InvoiceFirmNoticeDraft> DraftFirmNoticeAsync(
        InvoiceFirmNoticeRequest request,
        CancellationToken cancellationToken = default);

    // ── Invoice reading (ebilling-r1 task 043; FR-21, FR-04, D-49, D-81) ─────────────────────────────────────
    //
    // Reads the invoice with the configured Content Understanding custom analyzer (the D-81 preview analyzer by
    // default; the GA twin by configuration alone), then applies the deterministic completion step (clock-notation
    // hours, missing rates, day-first dates). It does NOT go through an Action or Binding: extraction is a
    // document-model call, not a prompt. Fails closed: never a partial result. Reason codes (InvoiceAiException,
    // ConsumerType InvoiceAiException.ReadCapability) classify WHERE the failure happened, not whether a retry helps:
    //   - invoice.read.unreadable      — the service could not open the document (encrypted, corrupt, empty)
    //   - invoice.read.page_limit      — more pages than ContentUnderstanding:MaxPages
    //   - invoice.read.no_invoice      — the document was read but no invoice data was found
    //   - invoice.read.service_failed  — the service call failed (transport, auth, throttling after retries, a
    //                                    missing analyzer, the operation timing out or a malformed result)
    //   - FeatureDisabledException(ai.invoice.read.not_configured) — no ContentUnderstanding:Endpoint
    //   - FeatureDisabledException(ai.invoice.disabled) — AI kill switch (ADR-032)
    //   - OperationCanceledException — the caller cancelled

    /// <summary>
    /// Read one invoice document into header fields, fee and expense lines and the timekeeper summary, each value
    /// with its confidence, page and normalised source box (FR-21, FR-04). Non-USD currencies are returned as read.
    /// </summary>
    /// <exception cref="InvoiceAiException">The read failed; <see cref="InvoiceAiException.ReasonCode"/> is one of the <c>invoice.read.*</c> codes above.</exception>
    /// <exception cref="FeatureDisabledException">The reader is not configured, or AI is disabled.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    Task<ExtractedInvoice> ReadInvoiceAsync(
        InvoiceReadRequest request,
        CancellationToken cancellationToken = default);
}
