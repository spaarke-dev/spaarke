// R7 spaarke-ai-platform-unification-r7 — AiCompletionNodeExecutor (task 002 / FR-12).
// Implements ExecutorType.AiCompletion = 1 (enum value present since the original node
// system, but never wired to an executor — R4 /narrate could not ship as a result).
//
// Purpose (prompt-only structured LLM call per FR-12 / FR-13):
//   AiAnalysisNodeExecutor REQUIRES a Tool (handler dispatch) AND a Document (extracted
//   text). That contract is wrong for prompt-only generators like R4 /narrate, channel
//   TL;DR, and other workflows that just need {SystemPrompt + OutputSchema + Temperature}
//   passed straight to IOpenAiClient.GetStructuredCompletionRawAsync. AiCompletion is the
//   leaf executor for that case — it REQUIRES Action FK + SystemPrompt + OutputSchema and
//   PROHIBITS Tool (FR-13). Document is optional (NOT REQUIRED).
//
// Pattern source: EntityNameValidatorNodeExecutor (sibling canonical R4-era shape —
//   private static JsonSerializerOptions, ILogger + injected dep ctor, AiTelemetry.ActivitySource
//   span, structured try/catch with Cancelled / InternalError propagation, terminal
//   NodeOutput.Ok / .Error). The one addition vs EntityNameValidator: inject IOpenAiClient
//   (Singleton-safe). No IServiceScopeFactory — no Scoped deps required.
//
// IMPLEMENTATION STATUS (Wave 1 tasks 002 → 005 complete):
//   - Class compiles and registers in DI as Singleton (UNCONDITIONAL per CLAUDE.md §F.1).
//   - Validate() enforces FR-13 require/prohibit invariants (Action FK + SystemPrompt +
//     OutputSchema + OutputVariable required; Tool + Document prohibited). Error messages
//     match the POML literal contract (Playbook Builder Wave 8 UI surface). [task 005]
//   - ExecuteAsync() end-to-end: PromptSchemaOverrideMerger plug-in [task 003] +
//     PromptSchemaRenderer render [task 003] +
//     IOpenAiClient.GetStructuredCompletionRawAsync call [task 004] +
//     JsonDocument.Parse + RootElement.Clone → NodeOutput.StructuredData binding [task 004].
//   - Privacy-safe telemetry per ADR-015 (lengths + IDs only; never prompt/response content).
//   - Task 006 wires the PlaybookOrchestrationService dispatch path (Wave 2 enum rename /
//     dispatch refactor); tasks 007-009 add unit tests for happy-path + error mapping.
//
// Reference: projects/spaarke-ai-platform-unification-r7/spec.md FR-12, FR-13;
//            projects/spaarke-ai-platform-unification-r7/notes/spikes/aicompletion-pattern-decision.md
//            (task 001 pattern-decision doc, the contract this scaffold follows);
//            .claude/patterns/ai/node-executor-authoring.md;
//            ADR-010 DI Minimalism; ADR-013 BFF AI Architecture; ADR-029 BFF Publish Hygiene.

using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Schemas;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for prompt-only structured LLM completions (R7 FR-12 / FR-13).
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="INodeExecutor"/> for <see cref="ExecutorType.AiCompletion"/>
/// (value 1). Registered as a Singleton in
/// <c>AnalysisServicesModule.AddNodeExecutors</c> (UNCONDITIONAL per CLAUDE.md §F.1
/// asymmetric-registration governance). The injected <see cref="IOpenAiClient"/> is also
/// Singleton, so the executor is Singleton-safe with no <c>IServiceScopeFactory</c>
/// indirection.
/// </para>
/// <para>
/// Contract delta vs <see cref="AiAnalysisNodeExecutor"/> (FR-13):
/// </para>
/// <list type="bullet">
/// <item><description>REQUIRES <c>context.Action</c> (FK), <c>context.Action.SystemPrompt</c>,
///   and <c>context.Action.OutputSchemaJson</c>.</description></item>
/// <item><description>PROHIBITS <c>context.Tool</c> — AiCompletion is a prompt-only
///   call; presence of a Tool implies an AiAnalysis node was mis-typed.</description></item>
/// <item><description>Does NOT require <c>context.Document</c> — payload-driven flows like
///   R4 <c>/narrate</c> have no document context.</description></item>
/// </list>
/// <para>
/// IMPLEMENTATION (Wave 1 task 004 complete): ExecuteAsync calls
/// <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/> with the
/// (rendered prompt, OutputSchemaJson, Temperature, schemaName) tuple produced by the
/// PromptSchemaOverrideMerger + PromptSchemaRenderer pipeline, parses the raw JSON response,
/// and binds the cloned <see cref="JsonElement"/> to <see cref="NodeOutput.StructuredData"/>
/// alongside the raw JSON text on <see cref="NodeOutput.TextContent"/>. Telemetry is
/// privacy-safe per ADR-015 (lengths + IDs only; never prompt/response content). Unit tests
/// follow in Wave 1 tasks 007–009.
/// </para>
/// </remarks>
public sealed class AiCompletionNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IOpenAiClient _openAiClient;
    private readonly PromptSchemaRenderer _promptSchemaRenderer;
    private readonly ILogger<AiCompletionNodeExecutor> _logger;

    public AiCompletionNodeExecutor(
        IOpenAiClient openAiClient,
        PromptSchemaRenderer promptSchemaRenderer,
        ILogger<AiCompletionNodeExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(openAiClient);
        ArgumentNullException.ThrowIfNull(promptSchemaRenderer);
        ArgumentNullException.ThrowIfNull(logger);
        _openAiClient = openAiClient;
        _promptSchemaRenderer = promptSchemaRenderer;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.AiCompletion
    };

    // R7 task 032 / FR-16 — typed config schema for Playbook Builder canvas (Wave 8 FR-23).
    // Derived from this executor's ConfigJson consumption: ApplyPromptSchemaOverride() reads
    // `promptSchemaOverride`; ExtractTemplateParameters() reads `templateParameters`. Both are
    // optional — AiCompletion is prompt-only per FR-13, so no L1/L2/L3 retrieval / $ref /
    // $choices fields apply. See projects/spaarke-ai-platform-unification-r7/notes/spikes/
    // executor-config-fields-inventory.md §2 + getconfigschema-design.md §8 worked example.
    private static readonly ExecutorConfigSchema ConfigSchemaInstance = new(
        ExecutorTypeName: nameof(ExecutorType.AiCompletion),
        ExecutorTypeValue: (int)ExecutorType.AiCompletion,
        Description: "Prompt-only structured LLM completion (FR-12). Requires Action FK with SystemPrompt + OutputSchemaJson. Prohibits Tool + Document.",
        Fields: new ConfigSchemaField[]
        {
            new(
                Name: "templateParameters",
                Type: SchemaFieldType.Object,
                Required: false,
                Description: "Key-to-value map substituted into {{var}} bindings in the JPS prompt instruction section.",
                Default: null),
            new(
                Name: "promptSchemaOverride",
                Type: SchemaFieldType.Object,
                Required: false,
                Description: "Per-node override merged into the Action's base JPS prompt schema (FR-25). Same shape as the Action's SystemPrompt JPS object.",
                Default: null)
        });

    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() => ConfigSchemaInstance;

    /// <summary>
    /// Validates the node's binding contract for AiCompletion (R7 FR-13).
    /// </summary>
    /// <remarks>
    /// <para>
    /// AiCompletion is a <b>prompt-only</b> structured-LLM call. This Validate() enforces
    /// the FR-13 invariants so the orchestrator can fast-fail before any LLM cost is
    /// incurred. Error messages are surfaced verbatim by the Playbook Builder UI (Wave 8) —
    /// the wording is part of the contract.
    /// </para>
    /// <para>
    /// REQUIRED: <c>context.Action</c> (FK) + <c>context.Action.SystemPrompt</c> +
    /// <c>context.Action.OutputSchemaJson</c> + <c>context.Node.OutputVariable</c>.
    /// PROHIBITED: <c>context.Tool</c> (Tools belong to AiAnalysis) and
    /// <c>context.Document</c> (Documents are AiAnalysis grounding input). AiCompletion has
    /// neither — the prompt + payload is self-contained.
    /// </para>
    /// <para>
    /// All failure conditions are aggregated into a single
    /// <see cref="NodeValidationResult.Failure(string[])"/> result — the orchestrator
    /// returns the full diagnostic set to the caller so the UI can show every error in one
    /// pass. The SystemPrompt + OutputSchemaJson + ActionId references are guarded behind
    /// the Action-presence check to avoid NullReferenceException on the per-Action checks.
    /// </para>
    /// <para>
    /// Per ADR-038: validation is deterministic + side-effect-free. No logging here — the
    /// caller (orchestrator) logs the failure with full diagnostic context.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        // FR-06: prompt-driven executors REQUIRE Action FK. context.Action is declared
        // required on NodeExecutionContext but defensive null-check + ActionId empty-check
        // catches orchestrator misconfiguration and test scaffolding alike.
        var actionMissing = context.Action is null || context.Node.ActionId == Guid.Empty;
        if (actionMissing)
        {
            errors.Add(
                "AiCompletion node requires an Action FK (prompt source). Set sprk_actionid on the node.");
        }

        // FR-13 inversion vs AiAnalysis: AiCompletion PROHIBITS Tool presence. A Tool means
        // the playbook author chose the wrong ExecutorType — should be AiAnalysis.
        if (context.Tool is not null)
        {
            errors.Add(
                "AiCompletion node MUST NOT have a Tool. Tools are used by AiAnalysis nodes; AiCompletion is prompt-only.");
        }

        // FR-13 inversion vs AiAnalysis: AiCompletion PROHIBITS Document presence. Documents
        // are AiAnalysis grounding input; AiCompletion is payload-driven (R4 /narrate, etc.).
        if (context.Document is not null)
        {
            errors.Add(
                "AiCompletion node MUST NOT have a Document. Documents are used by AiAnalysis nodes for grounding; AiCompletion is prompt-only.");
        }

        if (string.IsNullOrWhiteSpace(context.Node.OutputVariable))
        {
            errors.Add(
                "AiCompletion node requires OutputVariable to be set on the node.");
        }

        // Per-Action checks — guarded so we don't NRE when Action is null. The Action-missing
        // error above already surfaces the root cause; these add granular detail when Action
        // is present but its required fields are blank.
        if (!actionMissing)
        {
            if (string.IsNullOrWhiteSpace(context.Action!.SystemPrompt))
            {
                errors.Add(
                    $"AiCompletion node's Action {context.Action.Id} has no SystemPrompt. Set sprk_systemprompt on the Action.");
            }

            // OutputSchemaJson is required by IOpenAiClient.GetStructuredCompletionRawAsync
            // (constrained-decoding schema arg per Q4 of the pattern-decision doc). Not in
            // the FR-13 goal-bullet enumeration but required for a successful LLM call.
            if (string.IsNullOrWhiteSpace(context.Action.OutputSchemaJson))
            {
                errors.Add(
                    $"AiCompletion node's Action {context.Action.Id} has no OutputSchemaJson. Set sprk_outputschemajson on the Action (constrained-decoding schema for structured output).");
            }
        }

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        using var activity = AiTelemetry.ActivitySource.StartActivity(
            "ai.completion.node_execute", ActivityKind.Internal);
        activity?.SetTag("node.id", context.Node.Id.ToString());
        activity?.SetTag("node.name", context.Node.Name);
        activity?.SetTag("action_type", (int)ExecutorType.AiCompletion);

        _logger.LogDebug(
            "Executing AiCompletion node {NodeId} ({NodeName})",
            context.Node.Id, context.Node.Name);

        try
        {
            var validation = Validate(context);
            if (!validation.IsValid)
            {
                activity?.SetTag("node.outcome", "validation_failed");
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", validation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // ─────────────────────────────────────────────────────────────────────────
            // Task 003 (R7 spaarke-ai-platform-unification-r7 / FR-12 / FR-25):
            //   payload binding + PromptSchemaOverrideMerger plug-in.
            //
            // Pipeline (mirrors AiAnalysisNodeExecutor.CreateToolExecutionContextAsync's
            // merge-and-render path):
            //   1. basePrompt    = context.Action.SystemPrompt           (guaranteed non-empty by Validate)
            //   2. mergedPrompt  = ApplyPromptSchemaOverride(basePrompt, ConfigJson)   (FR-25 KEEP)
            //   3. templateParams = ExtractTemplateParameters(ConfigJson)             ({{var}} bindings)
            //   4. rendered      = PromptSchemaRenderer.Render(mergedPrompt, …, templateParams, …)
            //   5. STAGE         locals for task 004 LLM call (see "Task 004 binding contract" block)
            //
            // What is intentionally NOT done in this task:
            //   - L1/L2/L3 RAG retrieval — AiCompletion is prompt-only (FR-13 inverts AiAnalysis)
            //   - $ref resolution        — would require IScopeResolverService (Scoped); deferred until a
            //     payload-driven $ref use case appears. AiAnalysis ResolveJpsRefsAsync stays the reference.
            //   - $choices resolution    — same rationale. PromptSchemaRenderer is called with null
            //     downstreamNodes + null preResolvedLookupChoices.
            //   - skillContext / knowledgeContext — payload-driven AiCompletion has no scope-injected
            //     context (the prompt+payload is self-contained). Passed null/empty.
            //   - documentText           — Validate does not require Document (FR-13). Passed null.
            // ─────────────────────────────────────────────────────────────────────────

            // 1. Read Action triple. context.Action is non-null (Validate enforced it) and
            //    SystemPrompt is non-whitespace.
            var basePrompt = context.Action.SystemPrompt;

            // 2. Apply per-node prompt schema override (FR-25 KEEP). Mirrors
            //    AiAnalysisNodeExecutor.ApplyPromptSchemaOverride exactly — see comment on
            //    ApplyPromptSchemaOverride for DRY rationale. If basePrompt is flat text, or
            //    ConfigJson has no promptSchemaOverride section, this returns basePrompt
            //    unchanged.
            var mergedPrompt = ApplyPromptSchemaOverride(basePrompt, context.Node.ConfigJson);

            // 3. Extract template parameters from ConfigJson for {{key}} substitution inside the
            //    JPS instruction section. Mirrors AiAnalysisNodeExecutor.ExtractTemplateParameters
            //    exactly. Returns null when ConfigJson is missing/malformed/has no
            //    templateParameters property (PromptSchemaRenderer handles null gracefully).
            var templateParameters = ExtractTemplateParameters(context.Node.ConfigJson);

            // 3.5. R7 Wave 11 task 111 (Option B): extract the resolved `inputBinding` from
            //      configJson (already template-resolved by the orchestrator's Layer 1
            //      ApplyConfigJsonTemplates) and package as a JsonElement to pass to the
            //      renderer as runtimeInput. The renderer assembles a "## Input" section
            //      with the indented JSON, between Context and Document. This is the
            //      canonical way to pass payload data into an AI prompt WITHOUT coupling
            //      it to the prompt-body text. See:
            //        docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md (T111a)
            //      Defensive: missing inputBinding → null → renderer skips the Input section.
            var runtimeInput = ExtractInputBindingAsJsonElement(context.Node.ConfigJson);

            // 4. Render the (possibly merged) JPS prompt to the final string the LLM receives.
            //    For AiCompletion, all the structural sections (skillContext, knowledgeContext,
            //    documentText) are null — the prompt is self-contained per FR-13.
            //    runtimeInput emits the structured "## Input" section (Wave 11 / Option B).
            var rendered = _promptSchemaRenderer.Render(
                rawPrompt: mergedPrompt,
                skillContext: null,
                knowledgeContext: null,
                documentText: null,
                templateParameters: templateParameters,
                downstreamNodes: null,
                additionalKnowledge: null,
                additionalSkills: null,
                preResolvedLookupChoices: null,
                runtimeInput: runtimeInput);

            // 5. Compute effective temperature. Wave B-G9c1 B6 rule: per-Action override wins;
            //    null → 0.0 (deterministic) per IOpenAiClient.GetStructuredCompletionRawAsync's
            //    null-handling contract. (Action.Temperature is decimal? → float? for the LLM call.)
            var effectiveTemperature = context.Action.Temperature.HasValue
                ? (float)context.Action.Temperature.Value
                : (float?)null;

            // 6. Derive schemaName for the structured-output schemaName argument. Format
            //    "AiCompletion_<ActionName>" matches the Q4 worked example in
            //    notes/spikes/aicompletion-pattern-decision.md. Action.Name is the
            //    sprk_analysisaction display name (e.g. "BRIEF-NARRATE-TLDR").
            var schemaName = DeriveSchemaName(context.Action);

            // Structured logging — node ID, Action ID, merged-prompt length, field count.
            // ADR-015 / spec privacy rule: NEVER log prompt contents — only the diagnostic
            // metadata that supports debugging (length, format, schema field count).
            var outputSchemaJson = context.Action.OutputSchemaJson!; // non-null by Validate
            _logger.LogDebug(
                "AiCompletion node {NodeId} prompt prepared: Action={ActionId}, RenderedFormat={Format}, " +
                "RenderedPromptLength={PromptLength}, OutputSchemaJsonLength={SchemaLength}, " +
                "TemplateParamCount={ParamCount}, EffectiveTemperature={Temperature}",
                context.Node.Id,
                context.Action.Id,
                rendered.Format,
                rendered.PromptText.Length,
                outputSchemaJson.Length,
                templateParameters?.Count ?? 0,
                effectiveTemperature);

            activity?.SetTag("rendered.format", rendered.Format.ToString());
            activity?.SetTag("rendered.prompt_length", rendered.PromptText.Length);
            activity?.SetTag("output_schema.length", outputSchemaJson.Length);

            // ─────────────────────────────────────────────────────────────────────────
            // Task 004 (R7 spaarke-ai-platform-unification-r7 / FR-12):
            //   IOpenAiClient.GetStructuredCompletionRawAsync invocation + JsonElement binding.
            //
            // Per ADR-013 the executor is INSIDE the AI internals boundary, so a direct
            // IOpenAiClient dependency is appropriate — no facade. The call uses constrained
            // decoding (response_format: json_schema) so the returned string is guaranteed valid
            // JSON conforming to outputSchemaJson. JsonDocument.Parse should only fail in
            // pathological cases (Azure OpenAI returns malformed JSON despite strict mode); we
            // still defensively catch JsonException to surface a clean error.
            //
            // CancellationToken is forwarded into the SDK call so caller-driven cancellation
            // propagates (per task 004 acceptance criterion).
            // ─────────────────────────────────────────────────────────────────────────
            var rawJson = await _openAiClient.GetStructuredCompletionRawAsync(
                prompt:            rendered.PromptText,
                jsonSchema:        BinaryData.FromString(outputSchemaJson),
                schemaName:        schemaName,
                model:             context.ModelDeploymentId?.ToString(),
                maxOutputTokens:   context.MaxTokens,
                temperature:       effectiveTemperature,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // Parse the response once + clone the root element. Cloning detaches the JsonElement
            // from the disposed JsonDocument so it remains usable downstream in NodeOutput.
            JsonElement structuredData;
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                structuredData = doc.RootElement.Clone();
            }
            catch (JsonException jsonEx)
            {
                // Privacy-safe: log only metadata (length + exception type), never the response body.
                _logger.LogError(jsonEx,
                    "AiCompletion node {NodeId} (Action {ActionId}) received malformed JSON from LLM: " +
                    "RawJsonLength={RawJsonLength}, ExceptionType={ExceptionType}",
                    context.Node.Id,
                    context.Action.Id,
                    rawJson.Length,
                    jsonEx.GetType().Name);

                activity?.SetTag("node.outcome", "malformed_json");
                activity?.SetStatus(ActivityStatusCode.Error, "AI completion returned malformed JSON");
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    "AI completion returned malformed JSON",
                    NodeErrorCodes.InternalError,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Privacy-safe success telemetry per ADR-015: lengths + IDs only, never content.
            _logger.LogInformation(
                "AiCompletion node {NodeId} (Action {ActionId}) completed: " +
                "RawJsonLength={RawJsonLength}, DurationMs={DurationMs}",
                context.Node.Id,
                context.Action.Id,
                rawJson.Length,
                (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);

            activity?.SetTag("node.outcome", "success");
            activity?.SetTag("response.raw_json_length", rawJson.Length);
            activity?.SetStatus(ActivityStatusCode.Ok);

            // Bind to NodeOutput. We construct directly (instead of NodeOutput.Ok(...)) to avoid
            // re-serializing the already-parsed JsonElement through SerializeToElement.
            return new NodeOutput
            {
                NodeId = context.Node.Id,
                OutputVariable = context.Node.OutputVariable,
                Success = true,
                TextContent = rawJson,
                StructuredData = structuredData,
                Metrics = NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)
            };
        }
        catch (OperationCanceledException)
        {
            // Includes both caller-cancellation and SDK-internal cancellation propagation. We do
            // not distinguish — the orchestrator treats Cancelled identically in both cases.
            _logger.LogWarning(
                "AiCompletion node {NodeId} was cancelled",
                context.Node.Id);

            activity?.SetTag("node.outcome", "cancelled");
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                "Node execution was cancelled",
                NodeErrorCodes.Cancelled,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            // Covers LLM HTTP failures, circuit-breaker open (OpenAiCircuitBrokenException),
            // and any other unexpected exception. Privacy-safe: log exception type + message
            // but NEVER the prompt or response content (ADR-015).
            _logger.LogError(ex,
                "AiCompletion node {NodeId} (Action {ActionId}) failed: ExceptionType={ExceptionType}, " +
                "ErrorMessage={ErrorMessage}",
                context.Node.Id,
                context.Action?.Id,
                ex.GetType().Name,
                ex.Message);

            activity?.SetTag("node.outcome", "error");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"AiCompletion execution failed: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Applies a node-level <c>promptSchemaOverride</c> from ConfigJson to the base
    /// Action system prompt. Only applies when the base prompt is in JPS format and
    /// the override can be extracted and parsed.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="AiAnalysisNodeExecutor"/>.<c>ApplyPromptSchemaOverride</c>
    /// (R7 FR-25 KEEP per-node overrides). The merge semantics live in the static
    /// utility <see cref="PromptSchemaOverrideMerger"/>; this helper is the wiring
    /// glue (extract from ConfigJson → deserialize base → merge → re-serialize).
    /// <para>
    /// DRY note (R7 task 003 decision): copied (not extracted) from
    /// AiAnalysisNodeExecutor. Extraction was considered but deferred — the two
    /// executors differ on scope-resolution semantics and a shared helper would
    /// add cross-executor coupling for a small body of code. If a third executor
    /// adopts this pattern, lift to <c>Sprk.Bff.Api.Services.Ai.PromptOverrideHelper</c>.
    /// </para>
    /// </remarks>
    /// <param name="basePrompt">The Action's system prompt (flat text or JPS JSON).</param>
    /// <param name="configJson">The node's ConfigJson (may contain <c>promptSchemaOverride</c>).</param>
    /// <returns>
    /// The merged JPS JSON string if both base and override are present; otherwise the
    /// original <paramref name="basePrompt"/> unchanged.
    /// </returns>
    private string? ApplyPromptSchemaOverride(string? basePrompt, string? configJson)
    {
        if (string.IsNullOrWhiteSpace(basePrompt) || string.IsNullOrWhiteSpace(configJson))
            return basePrompt;

        // Only merge when the base prompt is JPS format
        if (!IsJpsFormat(basePrompt))
            return basePrompt;

        // Extract override from ConfigJson
        var schemaOverride = PromptSchemaOverrideMerger.ExtractOverride(configJson);
        if (schemaOverride is null)
            return basePrompt;

        try
        {
            // Parse the base prompt as PromptSchema
            var baseSchema = JsonSerializer.Deserialize<PromptSchema>(basePrompt, JpsDeserializeOptions);
            if (baseSchema is null)
                return basePrompt;

            // Merge base + override
            var merged = PromptSchemaOverrideMerger.Merge(baseSchema, schemaOverride);

            // Re-serialize to JSON
            var mergedJson = JsonSerializer.Serialize(merged, JpsSerializeOptions);

            _logger.LogDebug(
                "AiCompletion node applied promptSchemaOverride from ConfigJson to Action system prompt");

            return mergedJson;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "AiCompletion node failed to parse or merge promptSchemaOverride; using base prompt unchanged");
            return basePrompt;
        }
    }

    /// <summary>
    /// Extracts the <c>templateParameters</c> dictionary from a node's ConfigJson.
    /// Returns null if ConfigJson is missing, malformed, or does not contain templateParameters.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="AiAnalysisNodeExecutor"/>.<c>ExtractTemplateParameters</c>.
    /// </remarks>
    /// <param name="configJson">The node's ConfigJson string (may be null or empty).</param>
    /// <returns>A dictionary of template parameter names to values, or null if none found.</returns>
    private Dictionary<string, object?>? ExtractTemplateParameters(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("templateParameters", out var paramsElement))
                return null;

            if (paramsElement.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning(
                    "AiCompletion node ConfigJson templateParameters is not an object (found {ValueKind}); ignoring",
                    paramsElement.ValueKind);
                return null;
            }

            var result = new Dictionary<string, object?>();
            foreach (var prop in paramsElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop.Value.GetRawText()
                };
            }

            return result.Count > 0 ? result : null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "AiCompletion node failed to parse templateParameters from ConfigJson; using null fallback");
            return null;
        }
    }

    /// <summary>
    /// R7 Wave 11 task 111 (Option B Layer 2): extracts the <c>inputBinding</c> object from the
    /// node's ConfigJson as a <see cref="JsonElement"/>, suitable for passing to
    /// <see cref="PromptSchemaRenderer.Render"/> as the <c>runtimeInput</c> parameter. By the
    /// time the executor receives ConfigJson, the orchestrator's Layer 1 has already resolved
    /// all <c>{{X}}</c> Handlebars templates inside it — so the inputBinding values are
    /// concrete strings/objects, not unresolved templates.
    /// </summary>
    /// <remarks>
    /// Defensive: returns null when ConfigJson is missing, malformed, or has no inputBinding
    /// property (PromptSchemaRenderer skips the "## Input" section on null/Null/Undefined input).
    /// Returns the inputBinding object's JsonElement clone (detached from the parsing JsonDocument
    /// so it remains usable downstream).
    /// </remarks>
    /// <param name="configJson">The node's ConfigJson string (may be null or empty).</param>
    /// <returns>JsonElement clone of the inputBinding object, or null if absent/malformed.</returns>
    private JsonElement? ExtractInputBindingAsJsonElement(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("inputBinding", out var bindingElement))
                return null;

            if (bindingElement.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning(
                    "AiCompletion node ConfigJson inputBinding is not an object (found {ValueKind}); ignoring",
                    bindingElement.ValueKind);
                return null;
            }

            return bindingElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "AiCompletion node failed to parse inputBinding from ConfigJson; using null fallback");
            return null;
        }
    }

    /// <summary>
    /// Derives the <c>schemaName</c> argument for the LLM structured-output call.
    /// Format: <c>AiCompletion_&lt;sanitized-action-name&gt;</c>. Sanitization keeps
    /// alphanumeric + underscore + hyphen and replaces other characters with underscore
    /// (Azure OpenAI schema names tolerate alphanumeric / underscore / hyphen).
    /// </summary>
    /// <remarks>
    /// Matches the worked example in
    /// <c>notes/spikes/aicompletion-pattern-decision.md §Q4</c>. Used by task 004 when
    /// calling <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/>.
    /// </remarks>
    private static string DeriveSchemaName(AnalysisAction action)
    {
        var name = action.Name;
        if (string.IsNullOrWhiteSpace(name))
            return "AiCompletion";

        var sanitized = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sanitized.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
        }
        return $"AiCompletion_{sanitized}";
    }

    /// <summary>
    /// Detects whether a raw prompt string is in JPS format.
    /// Matches the same detection logic as <see cref="PromptSchemaRenderer"/> and
    /// <see cref="AiAnalysisNodeExecutor"/>.
    /// </summary>
    private static bool IsJpsFormat(string rawPrompt)
    {
        return rawPrompt.TrimStart().StartsWith('{') && rawPrompt.Contains("\"$schema\"");
    }

    private static readonly JsonSerializerOptions JpsDeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions JpsSerializeOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
