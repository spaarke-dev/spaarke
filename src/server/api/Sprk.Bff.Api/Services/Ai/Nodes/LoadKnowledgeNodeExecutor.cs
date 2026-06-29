// R4 spaarke-daily-update-service-r4 — First-class LoadKnowledge node executor.
//
// Purpose (UAT-blocking fix, post StartNodeExecutor deploy 2026-06-25):
//   Same failure shape as the Start node fix in commit d9c648e30. The deployed
//   DAILY-BRIEFING-NARRATE "LoadKnowledge" sprk_playbooknode row carries
//   nodeType=Control + canvasType=loadKnowledge + a placeholder configJson with a
//   `passthroughBinding` map but NO `__actionType`. Without an explicit __actionType,
//   the orchestrator structural fallback (PlaybookOrchestrationService.cs) routes
//   Control → ExecutorType.Condition → ConditionNodeExecutor, which rejects the node
//   with "Condition expression is required" — the same UAT failure class the Start
//   fix closed.
//
//   This executor is the first-class pairing for ExecutorType.LoadKnowledge (= 142),
//   modelled exactly on StartNodeExecutor.cs (sibling pattern just landed).
//
// Semantics (per daily-briefing-narrate.json LoadKnowledge node + R4 spec line 58
// "AI Search matter context knowledge node deferred to R5"):
//   - NodeType: Control (no Action FK, no scope resolution required).
//   - ExecutorType: LoadKnowledge = 142.
//   - Execute (R4 pass-through placeholder):
//       1. Read configJson.passthroughBinding (optional name→template map, e.g.
//          { "channels": "{{start.channels}}" }).
//       2. For each entry, render the template against scope variables built from
//          NodeExecutionContext.PreviousOutputs (same shape as CreateTaskNodeExecutor's
//          BuildTemplateContext — { varName: { output, text, success } }).
//       3. Bind the resolved object map as JsonElement to context.Node.OutputVariable
//          (default "channelRegistry").
//   - If configJson.r5BindingPlan.knowledgeSourceCode is set (non-empty), emit an
//     INFO log so future R5 work knows to wire the AI Search binding here. DO NOT
//     actually fetch in R4 — the node is a pure pass-through for the MVP.
//   - If configJson is missing OR passthroughBinding is empty: emit an empty object,
//     succeed.
//
// Why a dedicated executor, not orchestrator special-casing (mirrors Start's rationale):
//   - Per canonical-truth §9: NodeType (5 values) and ExecutorType (31+ enum values) are
//     orthogonal; dispatch axis is ExecutorType. The registry indexes by ExecutorType.
//   - Per node-executor-authoring pattern: every dispatchable node-type has its own
//     INodeExecutor. Adding LoadKnowledge as a new executor preserves the canonical shape.
//   - Future R5 work substitutes the placeholder bind with the AI Search retrieval
//     in-place — no orchestrator changes needed.
//
// Pattern source: StartNodeExecutor.cs (commit d9c648e30 — first-class Control node
//   executor with optional configJson, ILogger-only DI, template-substitution-aware).
//
// Reference: spec FR-12 + UAT 2026-06-26 ("Node 'LoadKnowledge' failed: Validation
//   failed: Condition expression is required");
//   projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json
//   (LoadKnowledge node configJson contract); docs/architecture/ai-architecture-playbook-runtime.md
//   §5 (Action lookup precedence — now extended to LoadKnowledge);
//   .claude/patterns/ai/node-executor-authoring.md.

using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for the canvas-only "LoadKnowledge" Control node — R4 pass-through
/// placeholder for the R5 AI Search knowledge-source binding.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="INodeExecutor"/> for <see cref="ExecutorType.LoadKnowledge"/>
/// (value 142). Registered as a Singleton in <c>AnalysisServicesModule.AddNodeExecutors</c>
/// (no scope-factory needed — uses <see cref="ITemplateEngine"/> + ILogger only).
/// </para>
/// <para>
/// For R4 (daily-briefing-narrate playbook) this node is a no-op pass-through that
/// exposes upstream scope variables under a stable name (default "channelRegistry")
/// for the parallel fan-out. R5 will substitute the bind with an AI Search query
/// against the channel-registry knowledge source per spec line 58.
/// </para>
/// </remarks>
public sealed class LoadKnowledgeNodeExecutor : INodeExecutor
{
    /// <summary>
    /// Default output-variable name used when <see cref="PlaybookNodeDto.OutputVariable"/>
    /// is unset. Matches the convention in the R4 daily-briefing-narrate playbook's
    /// LoadKnowledge node.
    /// </summary>
    public const string DefaultOutputVariable = "channelRegistry";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ITemplateEngine _templateEngine;
    private readonly ILogger<LoadKnowledgeNodeExecutor> _logger;

    public LoadKnowledgeNodeExecutor(
        ITemplateEngine templateEngine,
        ILogger<LoadKnowledgeNodeExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(templateEngine);
        ArgumentNullException.ThrowIfNull(logger);
        _templateEngine = templateEngine;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.LoadKnowledge
    };

    // R7 task 032 / FR-16 — placeholder schema (no maker-editable fields surfaced yet).
    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() =>
        ExecutorConfigSchema.Empty(
            ExecutorType.LoadKnowledge,
            "Canvas-only Control node — pass-through knowledge binding (R4 control-flow-executor). Evaluates optional passthroughBinding templates against scope variables.");

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        // ConfigJson is OPTIONAL — the R4 playbook deploys a placeholder configJson
        // with `kind: "pass-through-placeholder"` and `passthroughBinding`, but a
        // future deploy could omit it. Both are acceptable; the executor emits an
        // empty object in the missing case.
        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
            return NodeValidationResult.Success();

        try
        {
            using var _ = JsonDocument.Parse(context.Node.ConfigJson);
        }
        catch (JsonException ex)
        {
            return NodeValidationResult.Failure(
                $"LoadKnowledge node ConfigJson is not valid JSON: {ex.Message}");
        }

        return NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var outputVariable = string.IsNullOrWhiteSpace(context.Node.OutputVariable)
            ? DefaultOutputVariable
            : context.Node.OutputVariable;

        _logger.LogDebug(
            "Executing LoadKnowledge node {NodeId} ({NodeName}) — binding pass-through to '{OutputVariable}'",
            context.Node.Id, context.Node.Name, outputVariable);

        LoadKnowledgeConfig? config = null;
        if (!string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            try
            {
                config = JsonSerializer.Deserialize<LoadKnowledgeConfig>(
                    context.Node.ConfigJson, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "LoadKnowledge node {NodeId} ConfigJson failed to deserialize into LoadKnowledgeConfig — proceeding with defaults. Message: {Message}",
                    context.Node.Id, ex.Message);
            }
        }

        // R5 placeholder signal: if r5BindingPlan.knowledgeSourceCode is non-empty,
        // emit an info log so future R5 wiring (AI Search retrieval) knows where to
        // hook in. R4 stays pass-through.
        if (!string.IsNullOrWhiteSpace(config?.R5BindingPlan?.KnowledgeSourceCode))
        {
            _logger.LogInformation(
                "LoadKnowledge node {NodeId} ({NodeName}) R5 binding plan present — knowledgeSourceCode='{KnowledgeSourceCode}'. R4 pass-through executing; R5 will substitute AI Search retrieval here.",
                context.Node.Id, context.Node.Name, config!.R5BindingPlan!.KnowledgeSourceCode);
        }

        // Build the template context from previous-node outputs. Same shape as
        // CreateTaskNodeExecutor.BuildTemplateContext — variables like `start.channels`
        // resolve against PreviousOutputs[start].StructuredData. This is the canonical
        // helper the orchestrator already uses; we reuse it via ITemplateEngine.
        var templateContext = BuildTemplateContext(context);

        // Resolve the passthroughBinding map. Each value is a Handlebars template that
        // we render against the scope; we then re-parse the rendered string as JSON
        // when it looks like a JSON value (object / array / number / bool / quoted
        // string) so downstream nodes can navigate nested fields. When the rendered
        // value is a plain string (no surrounding quotes), we wrap it as a JSON string.
        var resolved = new Dictionary<string, object?>();
        if (config?.PassthroughBinding is { Count: > 0 })
        {
            foreach (var kvp in config.PassthroughBinding)
            {
                if (string.IsNullOrEmpty(kvp.Key))
                    continue;

                var templateValue = kvp.Value ?? string.Empty;
                string rendered;
                try
                {
                    rendered = _templateEngine.Render(templateValue, templateContext);
                }
                catch (Exception ex)
                {
                    // Defensive: a malformed template should NOT fail the run — bind
                    // the entry as null and log. Mirrors Start's payload-parse-warning
                    // shape.
                    _logger.LogWarning(
                        ex,
                        "LoadKnowledge node {NodeId} failed to render template for key '{Key}' — binding null. Template={Template}",
                        context.Node.Id, kvp.Key, templateValue);
                    resolved[kvp.Key] = null;
                    continue;
                }

                resolved[kvp.Key] = ParseRenderedValue(rendered);
            }
        }

        // Serialize the resolved object map to JsonElement so downstream nodes can
        // navigate fields via {{channelRegistry.channels}}. Empty map serializes to {}.
        JsonElement payloadElement = JsonSerializer.SerializeToElement(resolved);

        _logger.LogInformation(
            "LoadKnowledge node {NodeId} ({NodeName}) bound pass-through to scope '{OutputVariable}' (entries={EntryCount})",
            context.Node.Id,
            context.Node.Name,
            outputVariable,
            resolved.Count);

        return Task.FromResult(NodeOutput.Ok(
            context.Node.Id,
            outputVariable,
            data: payloadElement,
            textContent: null,
            confidence: null,
            metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
    }

    /// <summary>
    /// Build the Handlebars template context dictionary from previous node outputs +
    /// run metadata. Mirrors <c>CreateTaskNodeExecutor.BuildTemplateContext</c>: each
    /// upstream variable becomes a navigable object so templates can reference
    /// <c>{{start.channels}}</c>, <c>{{start.priorityItems}}</c>, etc.
    /// </summary>
    /// <remarks>
    /// We intentionally expose <c>{{varName}}</c> AS the StructuredData object directly
    /// (NOT under a <c>.output</c> sub-property) because the LoadKnowledge passthroughBinding
    /// references shape <c>{{start.channels}}</c> not <c>{{start.output.channels}}</c> —
    /// matches the daily-briefing-narrate.json configuration.
    /// </remarks>
    private static Dictionary<string, object?> BuildTemplateContext(NodeExecutionContext context)
    {
        var templateContext = new Dictionary<string, object?>();

        foreach (var (varName, output) in context.PreviousOutputs)
        {
            if (output.StructuredData.HasValue)
            {
                templateContext[varName] = TemplateEngine.ConvertJsonElement(output.StructuredData.Value);
            }
            else
            {
                templateContext[varName] = null;
            }
        }

        templateContext["run"] = new
        {
            id = context.RunId.ToString(),
            playbookId = context.PlaybookId.ToString(),
            tenantId = context.TenantId
        };

        return templateContext;
    }

    /// <summary>
    /// Parses a rendered template string into a JSON-friendly value. Handlebars renders
    /// arrays + objects via their default ToString(), which is not valid JSON — but the
    /// downstream template context navigates the underlying dictionary objects regardless,
    /// so we serialize the resolved string into a JsonElement node. If the rendered
    /// output parses as JSON we use that; otherwise we treat it as a literal string.
    /// </summary>
    private static object? ParseRenderedValue(string rendered)
    {
        if (string.IsNullOrEmpty(rendered))
            return string.Empty;

        var trimmed = rendered.Trim();
        // Heuristic: rendered Handlebars output for object/array references via
        // Render() may not yield valid JSON in all cases. Try JSON parse — if it
        // succeeds, return the JsonElement; else return the raw string.
        if (trimmed.StartsWith('{') || trimmed.StartsWith('[') ||
            trimmed.StartsWith('"') || trimmed.Equals("true", StringComparison.Ordinal) ||
            trimmed.Equals("false", StringComparison.Ordinal) || trimmed.Equals("null", StringComparison.Ordinal) ||
            (trimmed.Length > 0 && (char.IsDigit(trimmed[0]) || trimmed[0] == '-')))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Not JSON — fall through to literal string treatment.
            }
        }

        return rendered;
    }

    /// <summary>
    /// ConfigJson contract for LoadKnowledge nodes. All fields are optional; defaults
    /// match the R4 daily-briefing-narrate playbook. The <c>kind</c> and <c>_comment</c>
    /// fields carried in deployed JSON are ignored here (documentation only).
    /// </summary>
    internal sealed record LoadKnowledgeConfig
    {
        /// <summary>
        /// Documentation-only kind discriminator (e.g., "pass-through-placeholder").
        /// </summary>
        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        /// <summary>
        /// Optional name→template map. Each key becomes a field on the bound output
        /// object; each value is a Handlebars template rendered against the scope.
        /// </summary>
        [JsonPropertyName("passthroughBinding")]
        public Dictionary<string, string>? PassthroughBinding { get; init; }

        /// <summary>
        /// Forward-compat: when set, triggers an INFO log so future R5 wiring (AI
        /// Search retrieval) knows to substitute the placeholder pass-through. NOT
        /// honoured in R4 — pass-through is the only executed behaviour.
        /// </summary>
        [JsonPropertyName("r5BindingPlan")]
        public R5BindingPlanConfig? R5BindingPlan { get; init; }
    }

    /// <summary>
    /// Sub-shape of LoadKnowledgeConfig for R5 forward-compat.
    /// </summary>
    internal sealed record R5BindingPlanConfig
    {
        [JsonPropertyName("knowledgeSourceCode")]
        public string? KnowledgeSourceCode { get; init; }
    }
}
