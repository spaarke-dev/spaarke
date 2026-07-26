using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
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

    /// <summary>
    /// Output-token ceiling for Reasoning-tier (o-series / gpt-5) structured completions.
    /// </summary>
    /// <remarks>
    /// ai-advanced-capabilities-nda-r1 follow-up (post-UAT). Reasoning models count their internal
    /// reasoning tokens against <c>max_completion_tokens</c> (the SDK's <c>MaxOutputTokenCount</c>), so
    /// the 4000-token ceiling that suffices for non-reasoning structured output can be entirely consumed
    /// by reasoning before the JSON body is emitted — truncating the constrained-decoding output
    /// mid-object into a parse failure (surfaced to the user as "couldn't run that action"). The reasoning
    /// ceiling carries headroom for reasoning + the output body. Actual billed output is still bounded by
    /// each Action's output schema (ADR-039: the tier alone selects the ceiling — no per-consumer config
    /// side channel). Tunable if advisory outputs ever approach the cap.
    /// </remarks>
    internal const int MaxReasoningOutputTokensCeiling = 16000;

    public ActionRunner(
        IOpenAiClient openAi,
        PromptSchemaRenderer promptRenderer,
        ILogger<ActionRunner> logger,
        IOptions<DocumentIntelligenceOptions>? modelOptions = null)
    {
        _openAi = openAi;
        _promptRenderer = promptRenderer;
        _logger = logger;
        // ai-advanced-capabilities-nda-r1 task 010: optional with a default-constructed fallback so the
        // 3-arg constructor call sites already in the seam-test suite (which do not exercise model-tier
        // resolution) keep compiling unchanged; production DI always resolves the real bound options.
        _modelOptions = modelOptions?.Value ?? new DocumentIntelligenceOptions();
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
        // ai-advanced-capabilities-nda-r1 follow-up: Reasoning-tier completions need a higher output-token
        // ceiling because reasoning tokens count toward max_completion_tokens (see
        // MaxReasoningOutputTokensCeiling remarks). Keyed off the Action's declared tier (intent), not the
        // resolved deployment — the two-signal split matches OpenAiClient, where temperature omission keys
        // off the concrete reasoning deployment name.
        var maxOutputTokens = action.ModelTier == AiModelTier.Reasoning
            ? MaxReasoningOutputTokensCeiling
            : MaxOutputTokensCeiling;

        // NFR-07: identifiers + counts only — never slice content. Records that the resolved context
        // envelope flows to the executor (ADR-043: the completion consumes context-via-envelope).
        _logger.LogInformation(
            "Linear run: consumer={ConsumerType} action={ActionName} operandChannel={OperandChannel} " +
            "operandKind={OperandKind} promptLen={PromptLen} temp={Temperature} modelTier={ModelTier} " +
            "deployment={Deployment} context=[{ContextSummary}]",
            context.ConsumerType, action.Name, inputs.Operand.Channel, inputs.Operand.Kind,
            prompt.Length, temperature, action.ModelTier, deploymentName,
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
            maxOutputTokens: maxOutputTokens,
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
