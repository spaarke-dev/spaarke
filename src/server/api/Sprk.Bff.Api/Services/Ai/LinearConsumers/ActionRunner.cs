using System.Text.Json;
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
        ILogger<ActionRunner> logger)
    {
        _openAi = openAi;
        _promptRenderer = promptRenderer;
        _logger = logger;
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

        var prompt = BuildPrompt(action.SystemPrompt, inputs.Operand);
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
            "operandKind={OperandKind} promptLen={PromptLen} temp={Temperature} context=[{ContextSummary}]",
            context.ConsumerType, action.Name, inputs.Operand.Channel, inputs.Operand.Kind,
            prompt.Length, temperature,
            ContextEnvelopeReferenceConsumer.RenderPresenceSummary(inputs.Context));

        // FR-P3-01 (task 040): the per-consumer ModelDeployments/MaxOutputTokens config
        // maps were retired with the LinearConsumers appsettings block. Model intent lives
        // on the catalog (Binding.EffectiveModelTier / Action model tier); tier→deployment
        // mapping is a deferred enhancement per ADR-016 — the platform default deployment
        // applies. The output cap is the fixed executor ceiling (see
        // MaxOutputTokensCeiling remarks — replaces the live per-consumer env override).
        var rawJson = await _openAi.GetStructuredCompletionRawAsync(
            prompt,
            jsonSchema,
            schemaName,
            model: null,
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

    private string BuildPrompt(string systemPrompt, ResolvedOperand operand) => operand.Channel switch
    {
        OperandChannel.Document => BuildDocumentPrompt(systemPrompt, RequireDocument(operand)),
        OperandChannel.Input => BuildInputPrompt(systemPrompt, RequireInput(operand)),
        _ => BuildNoOperandPrompt(systemPrompt),
    };

    /// <summary>
    /// The pre-E-10 document-operand prompt build, preserved verbatim (non-regression). JPS renders the
    /// document under <c>## Document</c>; flat text appends the "Document:" block or substitutes the
    /// <c>{{document.extractedText}}</c> placeholder. Empty extracted text is a hard error (unchanged).
    /// </summary>
    private string BuildDocumentPrompt(string systemPrompt, DocumentText documentText)
    {
        if (string.IsNullOrWhiteSpace(documentText.ExtractedText))
        {
            throw new InvalidOperationException(
                $"DocumentText for {documentText.FileName} is empty; nothing to send to the LLM.");
        }

        var rendered = _promptRenderer.Render(
            rawPrompt: systemPrompt,
            skillContext: null,
            knowledgeContext: null,
            documentText: documentText.ExtractedText,
            templateParameters: null,
            downstreamNodes: null);

        if (rendered.Format == PromptFormat.JsonPromptSchema && !string.IsNullOrWhiteSpace(rendered.PromptText))
        {
            return rendered.PromptText;
        }

        // Flat-text path — original single-placeholder substitution.
        if (!systemPrompt.Contains(PlaceholderExtractedText, StringComparison.Ordinal))
        {
            return systemPrompt +
                "\n\n## Input\n\n" +
                "Document: " + documentText.FileName + "\n\n" +
                documentText.ExtractedText;
        }

        return systemPrompt.Replace(PlaceholderExtractedText, documentText.ExtractedText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The structured-operand prompt build (ADR-043). A JPS action passes the operand as the renderer's
    /// <c>runtimeInput</c> (rendered as <c>## Input</c> by <see cref="PromptSchemaRenderer"/>); a flat-text
    /// action (e.g. compose actions) appends the single-source <see cref="PromptInputSection"/> directly —
    /// so both formats emit byte-identical <c>## Input</c> (the frozen, golden-pinned producer).
    /// </summary>
    private string BuildInputPrompt(string systemPrompt, JsonElement input)
    {
        var rendered = _promptRenderer.Render(
            rawPrompt: systemPrompt,
            skillContext: null,
            knowledgeContext: null,
            documentText: null,
            templateParameters: null,
            downstreamNodes: null,
            runtimeInput: input);

        if (rendered.Format == PromptFormat.JsonPromptSchema && !string.IsNullOrWhiteSpace(rendered.PromptText))
        {
            return rendered.PromptText;
        }

        // Flat-text action — append the single-source `## Input` section after the instruction prompt.
        return systemPrompt + "\n\n" + PromptInputSection.Render(input);
    }

    /// <summary>
    /// The prompt-only build (no operand — the relaxed no-file / args-less run). JPS renders the
    /// instruction sections; flat text returns the prompt unchanged.
    /// </summary>
    private string BuildNoOperandPrompt(string systemPrompt)
    {
        var rendered = _promptRenderer.Render(
            rawPrompt: systemPrompt,
            skillContext: null,
            knowledgeContext: null,
            documentText: null,
            templateParameters: null,
            downstreamNodes: null);

        return rendered.Format == PromptFormat.JsonPromptSchema && !string.IsNullOrWhiteSpace(rendered.PromptText)
            ? rendered.PromptText
            : systemPrompt;
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
