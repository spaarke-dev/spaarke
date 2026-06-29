// R4 spaarke-daily-update-service-r4 — First-class ReturnResponse node executor.
//
// Purpose (UAT-blocking fix, post StartNodeExecutor deploy 2026-06-25):
//   Same failure shape as the Start node + LoadKnowledge fixes. The deployed
//   DAILY-BRIEFING-NARRATE "ReturnResponse" sprk_playbooknode row carries
//   nodeType=Control + canvasType=returnResponse + a configJson with a `responseBinding`
//   (and an `_validationMetadata` sidecar) but NO `__actionType`. Without an explicit
//   __actionType, the structural fallback routes Control → ExecutorType.Condition →
//   ConditionNodeExecutor, which rejects the node with "Condition expression is
//   required".
//
//   This executor is the first-class pairing for ExecutorType.ReturnResponse (= 143),
//   modelled exactly on StartNodeExecutor.cs (sibling pattern just landed).
//
// Semantics (per daily-briefing-narrate.json ReturnResponse node + design notes
// projects/spaarke-daily-update-service-r4/notes/design/010-daily-briefing-narrate-node-graph.md):
//   - NodeType: Control (no Action FK, no scope resolution required).
//   - ExecutorType: ReturnResponse = 143.
//   - Terminal node — binds the playbook's final outputs to the run return value,
//     projected into the DailyBriefingNarrateResponse contract by the playbook-
//     execution method on AnalysisOrchestrationService.
//   - Reads configJson.responseBinding (a name→template map, plus optional
//     _validationMetadata sub-map for sidecar fields). For each entry, renders the
//     template against scope variables (PreviousOutputs of prior nodes), then binds
//     the resolved object to context.Node.OutputVariable (default "response").
//   - Missing template variables yield empty/null (Handlebars graceful-missing
//     contract per TemplateEngine.cs — does NOT throw). Per playbook design: "missing
//     scope variables should yield empty/null (not throw)".
//   - The InvokePlaybookAi facade reads the final "response" variable as the return
//     value via the existing aggregation contract (code-archaeology §10).
//
// Why a dedicated executor, not orchestrator special-casing (mirrors Start's rationale):
//   - Per canonical-truth §9: NodeType (5 values) and ExecutorType (31+ enum values) are
//     orthogonal. Registry indexes by ExecutorType.
//   - Per node-executor-authoring pattern: every dispatchable node-type has its own
//     INodeExecutor. The orchestrator stays slim.
//
// Pattern source: StartNodeExecutor.cs (commit d9c648e30 — first-class Control node
//   executor with optional configJson, ILogger-only DI, template-substitution-aware).
//
// Reference: spec FR-12 + UAT 2026-06-26;
//   projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json
//   (ReturnResponse node configJson contract);
//   docs/architecture/ai-architecture-playbook-runtime.md §5;
//   .claude/patterns/ai/node-executor-authoring.md.

using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for the canvas-only "ReturnResponse" Control node — terminal
/// projection of upstream node outputs into the playbook run's return value.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="INodeExecutor"/> for <see cref="ExecutorType.ReturnResponse"/>
/// (value 143). Registered as a Singleton in <c>AnalysisServicesModule.AddNodeExecutors</c>
/// (no scope-factory needed — uses <see cref="ITemplateEngine"/> + ILogger only).
/// </para>
/// <para>
/// The ReturnResponse node's responsibility is to read upstream node outputs +
/// run metadata via Handlebars templates and project them into a single response
/// object bound to <see cref="PlaybookNodeDto.OutputVariable"/> (default "response").
/// The playbook-execution method then maps the bound object onto the wrapper's
/// strongly-typed response DTO (e.g., DailyBriefingNarrateResponse).
/// </para>
/// </remarks>
public sealed class ReturnResponseNodeExecutor : INodeExecutor
{
    /// <summary>
    /// Default output-variable name used when <see cref="PlaybookNodeDto.OutputVariable"/>
    /// is unset. Matches the convention in the R4 daily-briefing-narrate playbook's
    /// ReturnResponse node.
    /// </summary>
    public const string DefaultOutputVariable = "response";

    /// <summary>
    /// Reserved key in the responseBinding map — when present, its value is itself
    /// a name→template map whose resolved object becomes a sidecar under the same
    /// key on the bound output. Used by daily-briefing-narrate to surface the
    /// EntityNameValidator scrub metadata (scrubbedText, removedTerms) alongside
    /// the structured response fields.
    /// </summary>
    public const string ValidationMetadataKey = "_validationMetadata";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ITemplateEngine _templateEngine;
    private readonly ILogger<ReturnResponseNodeExecutor> _logger;

    public ReturnResponseNodeExecutor(
        ITemplateEngine templateEngine,
        ILogger<ReturnResponseNodeExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(templateEngine);
        ArgumentNullException.ThrowIfNull(logger);
        _templateEngine = templateEngine;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedActionTypes { get; } = new[]
    {
        ExecutorType.ReturnResponse
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        // ConfigJson is OPTIONAL — if missing, the executor binds an empty object
        // and succeeds (matches the empty-payload contract of the Start node).
        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
            return NodeValidationResult.Success();

        try
        {
            using var _ = JsonDocument.Parse(context.Node.ConfigJson);
        }
        catch (JsonException ex)
        {
            return NodeValidationResult.Failure(
                $"ReturnResponse node ConfigJson is not valid JSON: {ex.Message}");
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
            "Executing ReturnResponse node {NodeId} ({NodeName}) — binding response to '{OutputVariable}'",
            context.Node.Id, context.Node.Name, outputVariable);

        ReturnResponseConfig? config = null;
        if (!string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            try
            {
                config = JsonSerializer.Deserialize<ReturnResponseConfig>(
                    context.Node.ConfigJson, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "ReturnResponse node {NodeId} ConfigJson failed to deserialize into ReturnResponseConfig — proceeding with defaults. Message: {Message}",
                    context.Node.Id, ex.Message);
            }
        }

        // Build the Handlebars template context from upstream node outputs + run metadata.
        var templateContext = BuildTemplateContext(context);

        // Resolve responseBinding — flat name→template, plus the optional
        // _validationMetadata sidecar which is itself a name→template object.
        var resolved = new Dictionary<string, object?>();
        if (config?.ResponseBinding is { Count: > 0 })
        {
            foreach (var kvp in config.ResponseBinding)
            {
                if (string.IsNullOrEmpty(kvp.Key))
                    continue;

                // _validationMetadata is a nested object whose values are themselves
                // templates. Handle it specially so the resolved sidecar is itself a
                // nested object, not a string.
                if (string.Equals(kvp.Key, ValidationMetadataKey, StringComparison.Ordinal))
                {
                    resolved[kvp.Key] = ResolveValidationMetadata(kvp.Value, templateContext, context);
                    continue;
                }

                resolved[kvp.Key] = RenderField(kvp.Value, templateContext, context, kvp.Key);
            }
        }

        // Serialize the resolved object map to JsonElement so the orchestrator + the
        // InvokePlaybookAi facade aggregation contract can read the bound output.
        JsonElement payloadElement = JsonSerializer.SerializeToElement(resolved);

        _logger.LogInformation(
            "ReturnResponse node {NodeId} ({NodeName}) bound response to scope '{OutputVariable}' (fields={FieldCount})",
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
    /// Renders a single responseBinding field. Defensive against missing scope
    /// variables — Handlebars graceful-missing returns empty string; we honour that
    /// and DO NOT throw. Per playbook design: missing template variables yield
    /// empty/null.
    /// </summary>
    private object? RenderField(
        JsonElement? rawTemplate,
        Dictionary<string, object?> templateContext,
        NodeExecutionContext context,
        string fieldKey)
    {
        if (rawTemplate is null || rawTemplate.Value.ValueKind == JsonValueKind.Null)
            return null;

        if (rawTemplate.Value.ValueKind != JsonValueKind.String)
        {
            // Non-template scalar / object — pass through as a converted .NET object so
            // the serializer round-trips it correctly into the output payload.
            return TemplateEngine.ConvertJsonElement(rawTemplate.Value);
        }

        var template = rawTemplate.Value.GetString() ?? string.Empty;
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        string rendered;
        try
        {
            rendered = _templateEngine.Render(template, templateContext);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "ReturnResponse node {NodeId} failed to render template for field '{Field}' — binding empty string. Template={Template}",
                context.Node.Id, fieldKey, template);
            return string.Empty;
        }

        return ParseRenderedValue(rendered);
    }

    /// <summary>
    /// Resolves the optional <c>_validationMetadata</c> sidecar — a nested name→template
    /// object whose resolved values become a sub-object on the bound output. Used by
    /// daily-briefing-narrate to surface EntityNameValidator's scrubbedText + removedTerms
    /// alongside the structured response.
    /// </summary>
    private Dictionary<string, object?> ResolveValidationMetadata(
        JsonElement? metadataValue,
        Dictionary<string, object?> templateContext,
        NodeExecutionContext context)
    {
        var sidecar = new Dictionary<string, object?>();

        if (metadataValue is null || metadataValue.Value.ValueKind != JsonValueKind.Object)
            return sidecar;

        foreach (var prop in metadataValue.Value.EnumerateObject())
        {
            // Skip non-contract metadata fields (e.g., _comment) authored in the playbook
            // JSON for documentation purposes only.
            if (prop.Name.StartsWith("_comment", StringComparison.OrdinalIgnoreCase))
                continue;

            sidecar[prop.Name] = RenderField(prop.Value, templateContext, context, prop.Name);
        }

        return sidecar;
    }

    /// <summary>
    /// Build the Handlebars template context dictionary from previous node outputs +
    /// run metadata. Same shape as LoadKnowledgeNodeExecutor — variables exposed
    /// directly (NOT under .output) because the playbook references e.g.
    /// <c>{{tldrResult}}</c> and <c>{{validationResult.scrubbedText}}</c>.
    /// </summary>
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

        // The "run" metadata bag — exposes completedAtUtc per the daily-briefing-narrate
        // contract (responseBinding.generatedAtUtc = "{{run.completedAtUtc}}").
        templateContext["run"] = new
        {
            id = context.RunId.ToString(),
            playbookId = context.PlaybookId.ToString(),
            tenantId = context.TenantId,
            completedAtUtc = DateTimeOffset.UtcNow.ToString("o")
        };

        return templateContext;
    }

    /// <summary>
    /// Parses a rendered template string into a JSON-friendly value. When the rendered
    /// output parses as JSON we use that (so a rendered upstream object/array survives
    /// as a navigable JsonElement); otherwise we treat it as a literal string.
    /// </summary>
    private static object? ParseRenderedValue(string rendered)
    {
        if (string.IsNullOrEmpty(rendered))
            return string.Empty;

        var trimmed = rendered.Trim();
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
    /// ConfigJson contract for ReturnResponse nodes. All fields are optional; the
    /// daily-briefing-narrate playbook populates responseBinding with the
    /// DailyBriefingNarrateResponse field names. Documentation-only fields (
    /// <c>_comment</c>) are tolerated.
    /// </summary>
    internal sealed record ReturnResponseConfig
    {
        /// <summary>
        /// Name→template map. Each key becomes a top-level field on the bound response;
        /// each value is a Handlebars template OR a nested object (special-cased for
        /// the <c>_validationMetadata</c> sidecar). The map is stored as
        /// <see cref="JsonElement"/> so the executor can distinguish "string template"
        /// from "nested object" without a second deserialization pass.
        /// </summary>
        [JsonPropertyName("responseBinding")]
        public Dictionary<string, JsonElement>? ResponseBinding { get; init; }
    }
}
