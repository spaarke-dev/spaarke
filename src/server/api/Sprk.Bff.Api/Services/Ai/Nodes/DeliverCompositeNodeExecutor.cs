using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Multi-section composite delivery node executor (FR-52 / Phase 5R Wave 5-C task 114R).
/// Gathers N upstream Action node outputs keyed by declared <c>sectionName</c> and emits ONE
/// composite output containing a section map plus consumer routing (destination + widget type).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists</b>: The legacy single-action Output Node model couples (1) schema
/// declaration on the Action, (2) field-position contracts in the schema, (3) a schema-aware
/// widget renderer, and (4) a schema-aware streaming protocol. Adding a section requires
/// touching 5 coordination points. Composite Output collapses this to 2 coordination points
/// (the section name and the section state). Per-section streaming is task 114a's territory;
/// 114R's scope is the in-process composition + the one composite output emission.
/// </para>
/// <para>
/// <b>Config shape</b> (<c>sprk_configjson</c>):
/// </para>
/// <code>
/// {
///   "sections": [
///     { "sectionName": "summary",     "inputVariable": "summarize.output",  "displayLabel": "Summary" },
///     { "sectionName": "keyTerms",    "inputVariable": "extract_terms",     "displayLabel": "Key Terms" },
///     { "sectionName": "actionItems", "inputVariable": "extract_actions",   "displayLabel": "Action Items" }
///   ],
///   "destination": "workspace",                   // or "chat" or "formPrefill"
///   "widgetType": "structured-output-stream"      // consumer-defined widget identifier
/// }
/// </code>
/// <para>
/// Each <c>inputVariable</c> is matched against <see cref="NodeExecutionContext.PreviousOutputs"/>.
/// Missing inputs are silently dropped + logged at Information level (per FR-52: a partial
/// composite is still a valid composite). The output <c>StructuredData</c> is a section map
/// keyed by <c>sectionName</c>, plus the routing metadata, suitable for SSE serialization by
/// the streaming layer (task 114a wires per-section streaming on top of this contract).
/// </para>
/// <para>
/// <b>Backward-compat invariant (FR-52)</b>: existing <see cref="NodeType.Output"/> nodes are
/// dispatched to <see cref="DeliverOutputNodeExecutor"/> via <see cref="ExecutorType.DeliverOutput"/>
/// and are UNCHANGED. This executor handles ONLY the new <see cref="ExecutorType.DeliverComposite"/>
/// path emitted from <see cref="NodeType.DeliverComposite"/>.
/// </para>
/// <para>
/// <b>Telemetry (ADR-015 tier-1)</b>: composite execution logs <c>(sectionCount, destination,
/// widgetType, totalLatencyMs)</c> only — section content is NEVER logged (content is the
/// payload of the SSE event, which itself flows through tier-1 metadata-only logging guards).
/// </para>
/// </remarks>
public sealed class DeliverCompositeNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<DeliverCompositeNodeExecutor> _logger;

    public DeliverCompositeNodeExecutor(ILogger<DeliverCompositeNodeExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.DeliverComposite
    };

    // R7 task 032 / FR-16 — placeholder schema (no maker-editable fields surfaced yet).
    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() =>
        ExecutorConfigSchema.Empty(
            ExecutorType.DeliverComposite,
            "Multi-section composite delivery — assembles N upstream Action node outputs keyed by sectionName for consumer routing (FR-52).");

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var config = ParseConfig(context.Node.ConfigJson);
        if (config is null)
        {
            // Empty / missing config is permitted — treated as an empty composite at runtime.
            // The orchestrator + downstream consumer can decide whether an empty composite is
            // a hard error or a soft no-op (per FR-52: a partial composite is still valid).
            return NodeValidationResult.Success();
        }

        var errors = new List<string>();

        if (config.Sections is { Count: > 0 })
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < config.Sections.Count; i++)
            {
                var s = config.Sections[i];
                if (string.IsNullOrWhiteSpace(s.SectionName))
                {
                    errors.Add($"sections[{i}].sectionName is required");
                    continue;
                }
                if (!seen.Add(s.SectionName))
                {
                    errors.Add($"sections[{i}].sectionName '{s.SectionName}' is duplicated; section names must be unique within a composite node");
                }
                if (string.IsNullOrWhiteSpace(s.InputVariable))
                {
                    errors.Add($"sections[{i}].inputVariable is required (declares which upstream Action output supplies this section)");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(config.Destination) && !IsValidDestination(config.Destination))
        {
            errors.Add($"destination '{config.Destination}' is not valid (allowed: workspace, chat, formPrefill)");
        }

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var validation = Validate(context);
            if (!validation.IsValid)
            {
                return Task.FromResult(NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", validation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
            }

            var config = ParseConfig(context.Node.ConfigJson) ?? new CompositeNodeConfig();
            var declaredSections = config.Sections ?? Array.Empty<CompositeSectionSpec>();

            // Build the section map: preserve declaration ORDER (per FR-52 design intent — the
            // section ordering in the config is the canonical "as-declared" presentation order;
            // the streaming layer is free to reorder by completion if it chooses, but the
            // in-process composite preserves the author's order).
            var sectionMap = new List<CompositeSectionResult>(declaredSections.Count);
            var droppedCount = 0;

            foreach (var spec in declaredSections)
            {
                var inputVariable = spec.InputVariable ?? string.Empty;
                var upstream = context.GetPreviousOutput(inputVariable);
                if (upstream is null)
                {
                    droppedCount++;
                    _logger.LogInformation(
                        "DeliverComposite node {NodeId}: section '{SectionName}' upstream variable '{InputVariable}' " +
                        "not found in PreviousOutputs — dropping section silently (FR-52: partial composite is valid)",
                        context.Node.Id, spec.SectionName, inputVariable);
                    continue;
                }
                if (!upstream.Success)
                {
                    droppedCount++;
                    _logger.LogInformation(
                        "DeliverComposite node {NodeId}: section '{SectionName}' upstream variable '{InputVariable}' " +
                        "failed (ErrorCode={ErrorCode}) — dropping section silently (FR-52: partial composite is valid)",
                        context.Node.Id, spec.SectionName, inputVariable, upstream.ErrorCode ?? "(none)");
                    continue;
                }

                sectionMap.Add(new CompositeSectionResult
                {
                    SectionName = spec.SectionName!,
                    DisplayLabel = spec.DisplayLabel,
                    TextContent = upstream.TextContent,
                    StructuredData = upstream.StructuredData,
                    SourceNodeId = upstream.NodeId,
                    SourceVariable = inputVariable
                });
            }

            var compositePayload = new CompositeOutputPayload
            {
                Destination = NormalizeDestination(config.Destination),
                WidgetType = config.WidgetType,
                Sections = sectionMap
            };

            stopwatch.Stop();

            // ADR-015 tier-1 telemetry: metadata-only — section content is NOT logged. This is
            // the payload of the SSE event, which itself flows through tier-1 metadata-only
            // logging guards in the streaming layer (task 114a's wiring).
            _logger.LogInformation(
                "DeliverComposite node {NodeId} ({NodeName}) completed: sectionCount={SectionCount}, " +
                "dropped={DroppedCount}, destination={Destination}, widgetType={WidgetType}, latencyMs={LatencyMs}",
                context.Node.Id,
                context.Node.Name,
                sectionMap.Count,
                droppedCount,
                compositePayload.Destination,
                compositePayload.WidgetType ?? "(none)",
                stopwatch.ElapsedMilliseconds);

            // TODO (task 114a): wire per-section SSE streaming here. The streaming layer will
            // re-iterate this section list and emit `section_started` / `section_data` /
            // `section_completed` events keyed by sectionName. For 114R's scope, the executor
            // returns a single composite NodeOutput; the orchestration layer is responsible for
            // dispatch to the SSE writer (114a) or non-streaming consumer.

            var output = NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                compositePayload,
                textContent: null,
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow))
                with
            { IsDeliverOutput = true };

            return Task.FromResult(output);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "DeliverComposite node {NodeId} failed unexpectedly: {ErrorMessage}",
                context.Node.Id, ex.Message);

            return Task.FromResult(NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Composite delivery failed: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
        }
    }

    /// <summary>
    /// Normalize destination strings to the canonical lowercase tokens. Unknown/empty values
    /// default to <c>"workspace"</c> since that's the most common composite destination
    /// (the Phase 5R Wave 5-C anchor case).
    /// </summary>
    private static string NormalizeDestination(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "workspace";
        var trimmed = raw.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "workspace" => "workspace",
            "chat" => "chat",
            "formprefill" or "form-prefill" or "form_prefill" => "formPrefill",
            _ => trimmed // pass-through unknown — Validate already flagged it as error if reached here
        };
    }

    private static bool IsValidDestination(string destination)
    {
        return destination.Trim().ToLowerInvariant() switch
        {
            "workspace" or "chat" or "formprefill" or "form-prefill" or "form_prefill" => true,
            _ => false
        };
    }

    private static CompositeNodeConfig? ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<CompositeNodeConfig>(configJson, ConfigJsonOptions);
        }
        catch
        {
            // Caller treats null as "no/invalid config" — Validate will surface a clearer error
            // when it tries to read sections out of a null config.
            return null;
        }
    }
}

/// <summary>
/// Configuration for a <see cref="NodeType.DeliverComposite"/> node parsed from
/// <c>sprk_configjson</c>. See <see cref="DeliverCompositeNodeExecutor"/> for the JSON shape.
/// </summary>
internal sealed record CompositeNodeConfig
{
    [JsonPropertyName("sections")]
    public IReadOnlyList<CompositeSectionSpec>? Sections { get; init; }

    [JsonPropertyName("destination")]
    public string? Destination { get; init; }

    [JsonPropertyName("widgetType")]
    public string? WidgetType { get; init; }
}

/// <summary>
/// A single section binding inside a composite node's config — declares the section name, the
/// upstream variable that supplies the section's content, and a display label for the consumer.
/// </summary>
internal sealed record CompositeSectionSpec
{
    [JsonPropertyName("sectionName")]
    public string? SectionName { get; init; }

    [JsonPropertyName("inputVariable")]
    public string? InputVariable { get; init; }

    [JsonPropertyName("displayLabel")]
    public string? DisplayLabel { get; init; }
}

/// <summary>
/// Resolved section result composed by the executor — emitted as part of the composite output
/// payload. The streaming layer (task 114a) will re-iterate these to produce per-section SSE
/// events keyed by <see cref="SectionName"/>.
/// </summary>
public sealed record CompositeSectionResult
{
    [JsonPropertyName("sectionName")]
    public required string SectionName { get; init; }

    [JsonPropertyName("displayLabel")]
    public string? DisplayLabel { get; init; }

    [JsonPropertyName("textContent")]
    public string? TextContent { get; init; }

    [JsonPropertyName("structuredData")]
    public JsonElement? StructuredData { get; init; }

    [JsonPropertyName("sourceNodeId")]
    public Guid SourceNodeId { get; init; }

    [JsonPropertyName("sourceVariable")]
    public string? SourceVariable { get; init; }
}

/// <summary>
/// Top-level composite output payload — serialized into
/// <see cref="NodeOutput.StructuredData"/> by the executor. Consumed by the streaming layer
/// (task 114a) and by the front-end widget renderer (task 114b).
/// </summary>
public sealed record CompositeOutputPayload
{
    [JsonPropertyName("destination")]
    public required string Destination { get; init; }

    [JsonPropertyName("widgetType")]
    public string? WidgetType { get; init; }

    [JsonPropertyName("sections")]
    public required IReadOnlyList<CompositeSectionResult> Sections { get; init; }
}
