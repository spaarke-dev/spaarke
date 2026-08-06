using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.Schemas;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// <see cref="IActionRunner"/> implementation that wraps
/// <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered as Singleton — stateless, dependencies are Singleton-safe.
/// </para>
/// <para>
/// Uses the same <see cref="PromptSchemaRenderer"/> the Playbook Engine's
/// <c>AiCompletionNodeExecutor</c> uses so JPS-format Action prompts render
/// identically on both paths (natural-language sections vs raw JSON-as-prompt).
/// Plain-text prompts pass through unchanged.
/// </para>
/// </remarks>
public sealed class ActionRunner : IActionRunner
{
    private readonly IOpenAiClient _openAi;
    private readonly PromptSchemaRenderer _promptRenderer;
    private readonly ILogger<ActionRunner> _logger;
    private readonly DocumentIntelligenceOptions _modelOptions;
    private readonly ReferenceRetrievalService? _referenceRetrieval;

    /// <summary>
    /// TopK for linear-path reference grounding. The <c>spaarke-rag-references</c> corpus is small and
    /// curated; a generous cap pulls the whole relevant standard (e.g. all KNW-011 clauses) rather than
    /// only the top-5 nearest — omissions (standard clauses the operand does NOT match) are exactly what
    /// an advisory review must be able to flag.
    /// </summary>
    private const int ReferenceGroundingTopK = 12;

    private const string PlaceholderExtractedText = "{{document.extractedText}}";

    /// <summary>
    /// Output-token ceiling for prompted-executor structured completions.
    /// </summary>
    /// <remarks>
    /// FR-P3-01 / task 040 Step 9.5 W-1: the deleted per-consumer
    /// <c>LinearConsumers:MaxOutputTokens</c> config map was LIVE in the deployed dev
    /// environment (<c>LinearConsumers__MaxOutputTokens__summarize_file = 4000</c>,
    /// verified 2026-07-06) because SUM-CHAT@v1 emits up to ~2500 output tokens — far
    /// above the 500-token DocumentIntelligence default that a <c>null</c> cap falls
    /// back to. A structured output truncated mid-JSON is a parse failure, so the
    /// prompted executor pins the SAME deterministic ceiling for every Action instead
    /// of a per-consumer config surface (ADR-039: no config routing/tuning side
    /// channels; actual output length — and therefore cost — is bounded by each
    /// Action's constrained-decoding output schema, not by this ceiling).
    /// </remarks>
    internal const int MaxOutputTokensCeiling = 4000;

    public ActionRunner(
        IOpenAiClient openAi,
        PromptSchemaRenderer promptRenderer,
        ILogger<ActionRunner> logger,
        IOptions<DocumentIntelligenceOptions>? modelOptions = null,
        ReferenceRetrievalService? referenceRetrieval = null)
    {
        _openAi = openAi;
        _promptRenderer = promptRenderer;
        _logger = logger;
        // ai-advanced-capabilities-nda-r1 task 010: optional with a default-constructed fallback so the
        // 3-arg constructor call sites already in the seam-test suite (which do not exercise model-tier
        // resolution) keep compiling unchanged; production DI always resolves the real bound options.
        _modelOptions = modelOptions?.Value ?? new DocumentIntelligenceOptions();
        // nda-r1 follow-up: reference grounding for AllowsKnowledge Actions on the linear path (the
        // linear-path equivalent of the playbook node executor's L1 retrieval). Optional/nullable —
        // registered only when DocumentIntelligence:Enabled (AiModule), and left null for the seam-test
        // constructor call sites; a null instance simply means "no grounding" (graceful degrade).
        _referenceRetrieval = referenceRetrieval;
    }

    /// <summary>
    /// Pre-ADR-043 overload: wraps <paramref name="documentText"/> as a
    /// <see cref="OperandChannel.Document"/> operand (empty context envelope) and delegates to the
    /// canonical <see cref="BoundInputs"/> path — byte-identical to the pre-E-10 behavior for the
    /// linear-consumer callers (Doc Upload / File Summarize / Prefills / Event rules / etc.).
    /// </summary>
    public Task<JsonElement> RunAsync(
        AnalysisAction action,
        DocumentText documentText,
        LinearRunContext context,
        CancellationToken cancellationToken)
    {
        var inputs = new BoundInputs
        {
            Context = ContextEnvelopeReferenceProducer.Assemble(),
            Operand = new ResolvedOperand
            {
                Channel = OperandChannel.Document,
                Kind = OperandKind.FileDocument,
                Document = documentText,
            },
        };
        return RunAsync(action, inputs, context, cancellationToken);
    }

    public async Task<JsonElement> RunAsync(
        AnalysisAction action,
        BoundInputs inputs,
        LinearRunContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (string.IsNullOrWhiteSpace(action.SystemPrompt))
        {
            throw new InvalidOperationException(
                $"Action {action.Id} ({action.Name}) has empty SystemPrompt.");
        }

        if (string.IsNullOrWhiteSpace(action.OutputSchemaJson))
        {
            throw new InvalidOperationException(
                $"Action {action.Id} ({action.Name}) has empty OutputSchemaJson; " +
                $"linear consumers require a constrained-decoding schema.");
        }

        // ai-advanced-capabilities-nda-r1 task 010: resolve the Action's model tier to a concrete
        // deployment name — the last-mile completion of the previously dead-ended AiModelTier vocabulary
        // (ADR-016: catalog stores intent, code/config stores the deployment mapping). Mirrors the
        // Temperature plumbing below (action column → local variable → GetStructuredCompletionRawAsync
        // argument), just for `model` instead of `temperature`.
        var deploymentName = ModelTierDeploymentResolver.Resolve(action.ModelTier, _modelOptions);

        // nda-r1 follow-up: reference-knowledge grounding for AllowsKnowledge Actions (e.g. NDA Review vs
        // the firm standard KNW-011). Retrieved from spaarke-rag-references BEFORE the completion and
        // injected into the prompt below — without this the advisory review runs UNGROUNDED (the model has
        // no firm standard to compare against, so it returns empty findings). This is the linear-path
        // equivalent of the playbook node executor's L1 retrieval (AiAnalysisNodeExecutor).
        var referenceKnowledge = action.AllowsKnowledge
            ? await RetrieveReferenceGroundingAsync(action, inputs, context, cancellationToken)
            : null;

        var prompt = BuildPrompt(action.SystemPrompt, inputs.Operand, referenceKnowledge);
        // D6 / PE-D8(b) (F-1/F-2/F-7 envelope-convergence task): render the bound envelope's grounding
        // context into the dispatched capability's prompt — the SAME BoundInputs the dispatch path already
        // binds (no second bind). Deterministic position: the stable-prefix additions (User + Business,
        // now incl. user memory per D3) then the host record-memory fragment, ABOVE the instruction +
        // operand section. A dispatch with NO host/memory renders every part empty → the prompt is
        // byte-IDENTICAL to pre-change (the seam regression pin); dispatch prompts carry no date suffix
        // today, so none is added (match what exists, don't invent).
        prompt = ComposeGroundingContext(prompt, inputs);
        var jsonSchema = BinaryData.FromString(action.OutputSchemaJson);
        var schemaName = SanitizeSchemaName(action.Name);
        var temperature = (float?)action.Temperature;

        // NFR-07: identifiers + counts only — never slice content. Records that the resolved context
        // envelope flows to the executor (ADR-043: the completion consumes context-via-envelope).
        _logger.LogInformation(
            "Linear run: consumer={ConsumerType} action={ActionName} operandChannel={OperandChannel} " +
            "operandKind={OperandKind} promptLen={PromptLen} temp={Temperature} modelTier={ModelTier} " +
            "deployment={Deployment} allowsKnowledge={AllowsKnowledge} groundingChars={GroundingChars} " +
            "context=[{ContextSummary}]",
            context.ConsumerType, action.Name, inputs.Operand.Channel, inputs.Operand.Kind,
            prompt.Length, temperature, action.ModelTier, deploymentName,
            action.AllowsKnowledge, referenceKnowledge?.Length ?? 0,
            ContextEnvelopeReferenceConsumer.RenderPresenceSummary(inputs.Context));

        // FR-P3-01 (task 040): the per-consumer ModelDeployments/MaxOutputTokens config map was retired
        // with the LinearConsumers appsettings block. Model intent lives on the catalog
        // (Binding.EffectiveModelTier / Action.ModelTier); tier→deployment mapping is resolved just
        // above via ModelTierDeploymentResolver (task 010 — completes the ADR-016 deferred enhancement
        // this comment used to describe). The output cap is still the fixed executor ceiling (see
        // MaxOutputTokensCeiling remarks — replaces the live per-consumer env override).
        var rawJson = await _openAi.GetStructuredCompletionRawAsync(
            prompt,
            jsonSchema,
            schemaName,
            model: deploymentName,
            maxOutputTokens: MaxOutputTokensCeiling,
            temperature: temperature,
            cancellationToken: cancellationToken);

        using var doc = JsonDocument.Parse(rawJson);
        // Clone so the JsonElement remains valid after `doc` is disposed.
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Build the prompt from the resolved operand (ADR-043 Move 1 / E-10). Routes by
    /// <see cref="ResolvedOperand.Channel"/>:
    /// <list type="bullet">
    /// <item><see cref="OperandChannel.Document"/> — a file-grounding document renders under
    ///   <c>## Document</c> (JPS) / the flat-text "Document:" append. This is the <b>verbatim pre-E-10
    ///   path</b> (byte-for-behavior — the shipped summarize path is unregressed).</item>
    /// <item><see cref="OperandChannel.Input"/> — a structured operand renders through the single-source
    ///   <c>## Input</c> producer (<see cref="PromptSchemaRenderer"/> Layer 2 for JPS actions;
    ///   <see cref="PromptInputSection"/> appended for flat-text actions).</item>
    /// <item><see cref="OperandChannel.None"/> — a prompt-only run (the relaxed no-file path): the JPS
    ///   instruction sections / the flat prompt, with no operand section.</item>
    /// </list>
    /// </summary>
    /// <summary>
    /// D6 / PE-D8(b): prepends the bound envelope's rendered grounding context to the executor prompt —
    /// the stable-prefix additions (<see cref="ContextEnvelopeRenderer.RenderStablePrefixAdditions"/>: User
    /// + Business fragments) then the host record-memory prompt fragment (<see cref="BoundInputs.RecordMemoryFragment"/>).
    /// Byte-IDENTICAL when both are empty (returns <paramref name="prompt"/> unchanged) — the no-host/no-memory
    /// dispatch regression pin, and the non-regression guarantee for the pre-E-10 document overload (which
    /// binds an empty envelope with no record memory). NFR-07: this composes prompt text only; it logs nothing.
    /// </summary>
    private static string ComposeGroundingContext(string prompt, BoundInputs inputs)
    {
        var parts = new List<string>(2);

        var stablePrefix = ContextEnvelopeRenderer.RenderStablePrefixAdditions(inputs.Context);
        if (!string.IsNullOrEmpty(stablePrefix))
        {
            parts.Add(stablePrefix);
        }

        if (!string.IsNullOrEmpty(inputs.RecordMemoryFragment))
        {
            parts.Add(inputs.RecordMemoryFragment!);
        }

        return parts.Count == 0 ? prompt : string.Join("\n\n", parts) + "\n\n" + prompt;
    }

    /// <summary>
    /// nda-r1 follow-up: retrieve reference-knowledge grounding for an <c>AllowsKnowledge</c> Action from
    /// the <c>spaarke-rag-references</c> corpus (the linear-path equivalent of the playbook node executor's
    /// L1 retrieval). The query is the operand document (the material to measure against the standard),
    /// falling back to the Action's own description for prompt-only runs. <see cref="ReferenceRetrievalService"/>
    /// unions the caller tenant with the <c>"system"</c> tenant (NFR-06 pin), so Spaarke-seeded golden
    /// references (e.g. KNW-011) are retrievable under any execution tenant. Never throws — a null return
    /// means "no grounding available", and the Action's own prompt instructs the model to decline/omit
    /// rather than fabricate when the standard was not supplied (grounding is mode-independent, ADR-039).
    /// </summary>
    private async Task<string?> RetrieveReferenceGroundingAsync(
        AnalysisAction action,
        BoundInputs inputs,
        LinearRunContext context,
        CancellationToken cancellationToken)
    {
        if (_referenceRetrieval is null)
        {
            _logger.LogWarning(
                "Action {ActionName} allows knowledge but ReferenceRetrievalService is not registered " +
                "(DocumentIntelligence:Enabled=false?); running UNGROUNDED.", action.Name);
            return null;
        }

        if (string.IsNullOrWhiteSpace(context.TenantId))
        {
            _logger.LogWarning(
                "Action {ActionName} allows knowledge but LinearRunContext.TenantId is null; running UNGROUNDED.",
                action.Name);
            return null;
        }

        var query = inputs.Operand.Document?.ExtractedText;
        if (string.IsNullOrWhiteSpace(query))
        {
            query = string.IsNullOrWhiteSpace(action.Description) ? action.Name : action.Description;
        }
        // Bound the embedding input (text-embedding-3-large accepts ~8k tokens; keep well under).
        if (query.Length > 8000)
        {
            query = query[..8000];
        }

        try
        {
            var response = await _referenceRetrieval.SearchReferencesAsync(
                query,
                new ReferenceSearchOptions
                {
                    TenantId = context.TenantId!,
                    TopK = ReferenceGroundingTopK,
                    // Never drop a standard clause the operand does not resemble — omissions (clauses the
                    // NDA is MISSING) are exactly what an advisory review must be able to flag.
                    MinScore = 0.0f,
                    // ai-advanced-capabilities-agreements-r1 task 021 (FR-08 pack binding): scope the
                    // search to the classified type's own knowledge pack when the dispatch context
                    // resolved one (SessionDispatchOrchestrator, from the sprk_agreementtype registry's
                    // sprk_knowledgepackref). Null/empty (every pre-021 call site + every non-agreement
                    // Action) preserves the EXACT prior unscoped, whole-corpus behavior —
                    // ReferenceRetrievalService/RagService both no-op the filter when this is empty.
                    KnowledgeSourceIds = context.KnowledgeSourceIds,
                },
                cancellationToken);

            if (response.Results.Count == 0)
            {
                _logger.LogWarning(
                    "Reference grounding for action {ActionName} tenant {TenantId} returned ZERO chunks — " +
                    "the run will be ungrounded (check RAG ingest + NFR-06 tenant pin).",
                    action.Name, context.TenantId);
                return null;
            }

            var sb = new StringBuilder();
            foreach (var r in response.Results
                         .OrderBy(r => r.KnowledgeSourceName, StringComparer.Ordinal)
                         .ThenBy(r => r.ChunkIndex))
            {
                sb.Append('[').Append(r.KnowledgeSourceName).Append("] ").AppendLine(r.Content.Trim());
                sb.AppendLine();
            }

            _logger.LogInformation(
                "Reference grounding: action={ActionName} tenant={TenantId} chunks={Count} sources={Sources} chars={Chars}",
                action.Name, context.TenantId, response.Results.Count,
                response.Results.Select(r => r.KnowledgeSourceId).Distinct().Count(), sb.Length);

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Reference grounding failed for action {ActionName} tenant {TenantId}; running UNGROUNDED.",
                action.Name, context.TenantId);
            return null;
        }
    }

    private string BuildPrompt(string systemPrompt, ResolvedOperand operand, string? knowledgeContext) => operand.Channel switch
    {
        OperandChannel.Document => BuildDocumentPrompt(systemPrompt, RequireDocument(operand), knowledgeContext),
        OperandChannel.Input => BuildInputPrompt(systemPrompt, RequireInput(operand), knowledgeContext),
        _ => BuildNoOperandPrompt(systemPrompt, knowledgeContext),
    };

    /// <summary>
    /// nda-r1 follow-up: the labeled reference-standard block injected into the flat-text (non-JPS) prompt
    /// paths for an <c>AllowsKnowledge</c> Action. Empty when there is no retrieved grounding, so the
    /// prompt is byte-identical to the pre-change flat-text output for non-knowledge Actions. (JPS Actions
    /// route the same string through the renderer's <c>knowledgeContext</c> "Reference Knowledge" section.)
    /// </summary>
    private static string ReferenceBlock(string? knowledgeContext) =>
        string.IsNullOrWhiteSpace(knowledgeContext)
            ? string.Empty
            : "\n\n## Reference Standard (grounding — every finding MUST cite the clause it is measured against)\n\n"
                + knowledgeContext.Trim();

    /// <summary>
    /// The pre-E-10 document-operand prompt build, preserved verbatim (non-regression). JPS renders the
    /// document under <c>## Document</c>; flat text appends the "Document:" block or substitutes the
    /// <c>{{document.extractedText}}</c> placeholder. Empty extracted text is a hard error (unchanged).
    /// </summary>
    private string BuildDocumentPrompt(string systemPrompt, DocumentText documentText, string? knowledgeContext)
    {
        if (string.IsNullOrWhiteSpace(documentText.ExtractedText))
        {
            throw new InvalidOperationException(
                $"DocumentText for {documentText.FileName} is empty; nothing to send to the LLM.");
        }

        var rendered = _promptRenderer.Render(
            rawPrompt: systemPrompt,
            skillContext: null,
            knowledgeContext: knowledgeContext,
            documentText: documentText.ExtractedText,
            templateParameters: null,
            downstreamNodes: null);

        if (rendered.Format == PromptFormat.JsonPromptSchema && !string.IsNullOrWhiteSpace(rendered.PromptText))
        {
            return rendered.PromptText;
        }

        // Flat-text path — the renderer only injects knowledgeContext for JPS prompts, so the reference
        // standard is appended here (between the instruction and the document under review).
        if (!systemPrompt.Contains(PlaceholderExtractedText, StringComparison.Ordinal))
        {
            return systemPrompt +
                ReferenceBlock(knowledgeContext) +
                "\n\n## Input\n\n" +
                "Document: " + documentText.FileName + "\n\n" +
                documentText.ExtractedText;
        }

        return systemPrompt.Replace(PlaceholderExtractedText, documentText.ExtractedText, StringComparison.Ordinal)
            + ReferenceBlock(knowledgeContext);
    }

    /// <summary>
    /// The structured-operand prompt build (ADR-043). A JPS action passes the operand as the renderer's
    /// <c>runtimeInput</c> (rendered as <c>## Input</c> by <see cref="PromptSchemaRenderer"/>); a flat-text
    /// action (e.g. compose actions) appends the single-source <see cref="PromptInputSection"/> directly —
    /// so both formats emit byte-identical <c>## Input</c> (the frozen, golden-pinned producer).
    /// </summary>
    private string BuildInputPrompt(string systemPrompt, JsonElement input, string? knowledgeContext)
    {
        var rendered = _promptRenderer.Render(
            rawPrompt: systemPrompt,
            skillContext: null,
            knowledgeContext: knowledgeContext,
            documentText: null,
            templateParameters: null,
            downstreamNodes: null,
            runtimeInput: input);

        if (rendered.Format == PromptFormat.JsonPromptSchema && !string.IsNullOrWhiteSpace(rendered.PromptText))
        {
            return rendered.PromptText;
        }

        // Flat-text action — append the reference standard (flat path only) then the single-source
        // `## Input` section after the instruction prompt.
        return systemPrompt + ReferenceBlock(knowledgeContext) + "\n\n" + PromptInputSection.Render(input);
    }

    /// <summary>
    /// The prompt-only build (no operand — the relaxed no-file / args-less run). JPS renders the
    /// instruction sections; flat text returns the prompt unchanged.
    /// </summary>
    private string BuildNoOperandPrompt(string systemPrompt, string? knowledgeContext)
    {
        var rendered = _promptRenderer.Render(
            rawPrompt: systemPrompt,
            skillContext: null,
            knowledgeContext: knowledgeContext,
            documentText: null,
            templateParameters: null,
            downstreamNodes: null);

        return rendered.Format == PromptFormat.JsonPromptSchema && !string.IsNullOrWhiteSpace(rendered.PromptText)
            ? rendered.PromptText
            : systemPrompt + ReferenceBlock(knowledgeContext);
    }

    private static DocumentText RequireDocument(ResolvedOperand operand) =>
        operand.Document ?? throw new InvalidOperationException(
            "ActionRunner: Document-channel operand carries no DocumentText (ContextBinder contract violation).");

    private static JsonElement RequireInput(ResolvedOperand operand) =>
        operand.Input ?? throw new InvalidOperationException(
            "ActionRunner: Input-channel operand carries no element (ContextBinder contract violation).");

    /// <summary>
    /// Azure OpenAI structured-output schema names must be alphanumeric +
    /// underscores. Sanitize the Action name into a valid identifier.
    /// </summary>
    private static string SanitizeSchemaName(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName)) return "linear_action";
        var chars = actionName
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        return new string(chars).Trim('_');
    }
}
