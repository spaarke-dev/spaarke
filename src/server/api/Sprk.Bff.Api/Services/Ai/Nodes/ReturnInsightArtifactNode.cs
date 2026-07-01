using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Final node of an Insights synthesis playbook (D-P12 + D-P14). Reads the upstream node
/// output that carries the synthesis result, serializes it into a typed
/// <see cref="InsightArtifact"/> envelope per design.md §2.2 (typically an
/// <see cref="InferenceArtifact"/>), and runs the D-A23 / D-48 EvidenceGuard before return —
/// throws / surfaces error on empty evidence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Config schema</b> (read from <see cref="Sprk.Bff.Api.Models.Ai.PlaybookNodeDto.ConfigJson"/>):
/// </para>
/// <code>
/// {
///   "from":     "synthesize",                                    // required — upstream node output variable
///   "artifactKind": "inference",                                  // optional — "inference" (default) | "fact" | "observation"
///   "id":       "inf:predict-cost:M-1234:{runId}",                // optional — template tokens {runId},{playbookId},{subject}
///   "subject":  "matter:M-1234",                                  // required at runtime (may come from upstream)
///   "subjectFrom": "input",                                       // optional — read subject from upstream output instead
///   "predicate":"predictedCost",                                  // required — claim name
///   "displayHint":"currency-usd",                                 // optional — default "text"
///   "producedById":"playbook://predict-matter-cost@v1",           // required — producer identifier
///   "producedByKind":"playbook",                                  // optional — default "playbook"
///   "producedByVersion":"v1",                                     // required for observations per D-05
///   "valueFrom":"value",                                          // optional — JSON property on upstream carrying value; default "value"
///   "evidenceFrom":"evidence",                                    // optional — JSON property carrying EvidenceRef[]; default "evidence"
///   "confidenceFrom":"confidence",                                // optional — JSON property carrying confidence number
///   "reasoningFrom":"reasoning",                                  // optional — Inference reasoning summary
///   "allowEmptyEvidence": false                                   // optional — default false; true bypasses EvidenceGuard (Facts only)
/// }
/// </code>
/// <para>
/// <b>EvidenceGuard (D-A23 / D-48)</b>: by default the node rejects any artifact whose
/// <see cref="InsightArtifact.Evidence"/> is empty unless <c>artifactKind = "fact"</c>
/// (Facts may legitimately have empty evidence per design.md §2.1 — they are deterministic
/// reads from the system of record and carry <c>fact-source</c> refs in practice but the
/// design allows the field to be empty for synthetic Facts). For observations/inferences,
/// empty evidence yields a node-level error so the playbook surfaces the contract violation
/// rather than emitting a hollow artifact.
/// </para>
/// <para>
/// <b>Zone A</b> per SPEC §3.5 — lives under <c>Services/Ai/Nodes/</c>. Deterministic, no
/// LLM. The returned NodeOutput's <see cref="NodeOutput.StructuredData"/> is the typed
/// <see cref="InsightArtifact"/> envelope; the D-P15 endpoint reads it directly.
/// </para>
/// </remarks>
public sealed class ReturnInsightArtifactNode : INodeExecutor
{
    /// <summary>Error code raised when EvidenceGuard rejects the synthesized artifact.</summary>
    public const string EvidenceRequiredErrorCode = "EVIDENCE_REQUIRED";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<ReturnInsightArtifactNode> _logger;

    public ReturnInsightArtifactNode(ILogger<ReturnInsightArtifactNode> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.ReturnInsightArtifact
    };

    // R7 task 085 / FR-23 — typed config schema for Playbook Builder canvas.
    // Derived from ReturnInsightArtifactNodeConfig: from (required), predicate (required),
    // producedById (required), artifactKind (inference/fact/observation, default inference),
    // id, subject, subjectFrom, displayHint, producedByKind, producedByVersion, valueFrom,
    // evidenceFrom, confidenceFrom, reasoningFrom, allowEmptyEvidence.
    private static readonly ExecutorConfigSchema ConfigSchemaInstance = new(
        ExecutorTypeName: nameof(ExecutorType.ReturnInsightArtifact),
        ExecutorTypeValue: (int)ExecutorType.ReturnInsightArtifact,
        Description: "Final node of an Insights synthesis playbook — serializes upstream synthesis result into a typed InsightArtifact envelope (D-P12 / D-P1). Runs D-A23/D-48 EvidenceGuard before emit.",
        Fields: new ConfigSchemaField[]
        {
            new(
                Name: "from",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "Upstream synthesis node's OutputVariable. Required — supplies value + evidence + confidence + reasoning.",
                Default: null),
            new(
                Name: "predicate",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "Claim name (e.g., 'predictedCost', 'outcomeCategory'). Required.",
                Default: null),
            new(
                Name: "producedById",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "Producer identifier (e.g., 'playbook://predict-matter-cost@v1'). Required.",
                Default: null),
            new(
                Name: "artifactKind",
                Type: SchemaFieldType.Enum,
                Required: false,
                Description: "Output artifact type. Defaults to 'inference'. Facts may have empty evidence (deterministic); inferences + observations require non-empty evidence unless allowEmptyEvidence=true.",
                Default: "inference",
                EnumValues: new[] { "inference", "fact", "observation" }),
            new(
                Name: "subject",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "Subject for the artifact (e.g., 'matter:M-1234'). Either subject or subjectFrom MUST resolve to a non-empty value at runtime.",
                Default: null),
            new(
                Name: "subjectFrom",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "JSON property name on the upstream output that supplies the subject (alternative to literal subject).",
                Default: null),
            new(
                Name: "id",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "Optional ID template with tokens {runId}, {playbookId}, {subject}, {tenantId} (e.g., 'inf:predict-cost:{subject}:{runId}'). Defaults to 'art:{subject}:{runId}'.",
                Default: null),
            new(
                Name: "displayHint",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "Hint to consumers for value rendering (e.g., 'currency-usd', 'date', 'text'). Defaults to 'text'.",
                Default: "text"),
            new(
                Name: "producedByKind",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "Producer kind discriminator. Defaults to 'playbook'.",
                Default: "playbook"),
            new(
                Name: "producedByVersion",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "Producer version string. Required for observations per D-05.",
                Default: null),
            new(
                Name: "valueFrom",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "JSON property on the upstream output carrying the value. Defaults to 'value'. When the property is absent, the full upstream StructuredData is used.",
                Default: "value"),
            new(
                Name: "evidenceFrom",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "JSON property on the upstream output carrying EvidenceRef[]. Defaults to 'evidence'.",
                Default: "evidence"),
            new(
                Name: "confidenceFrom",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "JSON property on the upstream output carrying the confidence number. Defaults to 'confidence' (or uses upstream.Confidence if not set).",
                Default: "confidence"),
            new(
                Name: "reasoningFrom",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "JSON property on the upstream output carrying the Inference reasoning summary. Defaults to 'reasoning'.",
                Default: "reasoning"),
            new(
                Name: "allowEmptyEvidence",
                Type: SchemaFieldType.Boolean,
                Required: false,
                Description: "Bypass EvidenceGuard (D-A23/D-48). Defaults to false; set true only for deterministic Facts where empty evidence is legitimate.",
                Default: false)
        });

    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() => ConfigSchemaInstance;

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var config = ParseConfig(context.Node.ConfigJson);
        if (config is null)
            return NodeValidationResult.Failure(
                "ReturnInsightArtifact node requires ConfigJson with at least 'from', 'predicate', 'producedById'.");

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(config.From))
            errors.Add("ConfigJson.from is required (upstream synthesis output variable).");
        if (string.IsNullOrWhiteSpace(config.Predicate))
            errors.Add("ConfigJson.predicate is required.");
        if (string.IsNullOrWhiteSpace(config.ProducedById))
            errors.Add("ConfigJson.producedById is required.");

        var kind = (config.ArtifactKind ?? "inference").ToLowerInvariant();
        if (kind is not ("inference" or "fact" or "observation"))
            errors.Add($"ConfigJson.artifactKind must be one of: inference, fact, observation (got '{config.ArtifactKind}').");

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

        var config = ParseConfig(context.Node.ConfigJson)!;
        var upstream = context.GetPreviousOutput(config.From!);
        if (upstream is null)
        {
            return Task.FromResult(NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Upstream output '{config.From}' was not found.",
                NodeErrorCodes.InvalidConfiguration,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
        }
        if (!upstream.Success)
        {
            return Task.FromResult(NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Upstream '{config.From}' failed; cannot build InsightArtifact.",
                NodeErrorCodes.DependencyFailed,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
        }

        try
        {
            var kind = (config.ArtifactKind ?? "inference").ToLowerInvariant();
            var subject = ResolveSubject(context, upstream, config);
            if (string.IsNullOrWhiteSpace(subject))
            {
                return Task.FromResult(NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    "subject could not be resolved (set ConfigJson.subject or ensure subjectFrom points at a populated upstream field).",
                    NodeErrorCodes.InvalidConfiguration,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
            }

            var (value, evidence, confidence, reasoning) = ExtractArtifactFields(upstream, config);

            // D-A23 / D-48 EvidenceGuard
            var allowEmpty = config.AllowEmptyEvidence ?? false;
            if (!allowEmpty && kind != "fact" && evidence.Count == 0)
            {
                _logger.LogWarning(
                    "ReturnInsightArtifactNode {NodeId}: EvidenceGuard rejected empty-evidence {Kind} for subject={Subject} predicate={Predicate}",
                    context.Node.Id, kind, subject, config.Predicate);
                return Task.FromResult(NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    $"EvidenceGuard: {kind} artifacts must carry non-empty evidence per D-A23 / D-48. " +
                    $"Set allowEmptyEvidence=true only when this is a deterministic Fact.",
                    EvidenceRequiredErrorCode,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
            }

            var id = RenderId(config.Id, context, subject);
            var scope = new Scope { TenantId = context.TenantId };
            var producedBy = new ProducedBy
            {
                Kind = string.IsNullOrWhiteSpace(config.ProducedByKind) ? "playbook" : config.ProducedByKind!,
                Id = config.ProducedById!,
                Version = config.ProducedByVersion
            };

            InsightArtifact artifact = kind switch
            {
                "fact" => new FactArtifact
                {
                    Id = id,
                    Subject = subject!,
                    Predicate = config.Predicate!,
                    Value = value,
                    Evidence = evidence,
                    AsOf = DateTimeOffset.UtcNow,
                    ProducedBy = producedBy,
                    Scope = scope,
                    TenantId = context.TenantId
                },
                "observation" => new ObservationArtifact
                {
                    Id = id,
                    Subject = subject!,
                    Predicate = config.Predicate!,
                    Value = value,
                    Evidence = evidence,
                    AsOf = DateTimeOffset.UtcNow,
                    ProducedBy = producedBy,
                    Scope = scope,
                    TenantId = context.TenantId,
                    Confidence = confidence ?? 0.0
                },
                _ => new InferenceArtifact
                {
                    Id = id,
                    Subject = subject!,
                    Predicate = config.Predicate!,
                    Value = value,
                    Evidence = evidence,
                    AsOf = DateTimeOffset.UtcNow,
                    ProducedBy = producedBy,
                    Scope = scope,
                    TenantId = context.TenantId,
                    Confidence = confidence ?? 0.0,
                    Reasoning = reasoning
                }
            };

            _logger.LogInformation(
                "ReturnInsightArtifactNode {NodeId}: emitted {Kind} subject={Subject} predicate={Predicate} evidence={EvidenceCount}",
                context.Node.Id, kind, subject, config.Predicate, evidence.Count);

            return Task.FromResult(NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                artifact,
                textContent: $"Emitted {kind} for {subject}.{config.Predicate}",
                confidence: confidence,
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ReturnInsightArtifactNode {NodeId} failed: {Message}", context.Node.Id, ex.Message);
            return Task.FromResult(NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Artifact assembly failed: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
        }
    }

    private static string? ResolveSubject(
        NodeExecutionContext context,
        NodeOutput upstream,
        ReturnInsightArtifactNodeConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Subject))
            return config.Subject;

        // Resolve from upstream
        if (!string.IsNullOrWhiteSpace(config.SubjectFrom) && upstream.StructuredData is not null)
        {
            if (upstream.StructuredData.Value.TryGetProperty(config.SubjectFrom!, out var el)
                && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
        }
        return null;
    }

    private static (Value Value, IReadOnlyList<EvidenceRef> Evidence, double? Confidence, string? Reasoning)
        ExtractArtifactFields(NodeOutput upstream, ReturnInsightArtifactNodeConfig config)
    {
        var displayHint = string.IsNullOrWhiteSpace(config.DisplayHint) ? "text" : config.DisplayHint!;

        // Default: raw value is the upstream StructuredData itself or a sub-property.
        JsonElement rawValueElement;
        if (upstream.StructuredData is null)
        {
            rawValueElement = JsonDocument.Parse("null").RootElement;
        }
        else
        {
            var path = string.IsNullOrWhiteSpace(config.ValueFrom) ? "value" : config.ValueFrom!;
            rawValueElement = upstream.StructuredData.Value.TryGetProperty(path, out var v)
                ? v.Clone()
                : upstream.StructuredData.Value.Clone();
        }
        var value = new Value { Raw = rawValueElement, DisplayHint = displayHint };

        var evidence = ExtractEvidence(upstream, config);
        var confidence = ExtractConfidence(upstream, config);
        var reasoning = ExtractReasoning(upstream, config);

        return (value, evidence, confidence, reasoning);
    }

    private static IReadOnlyList<EvidenceRef> ExtractEvidence(NodeOutput upstream, ReturnInsightArtifactNodeConfig config)
    {
        if (upstream.StructuredData is null)
            return Array.Empty<EvidenceRef>();

        var path = string.IsNullOrWhiteSpace(config.EvidenceFrom) ? "evidence" : config.EvidenceFrom!;
        if (!upstream.StructuredData.Value.TryGetProperty(path, out var el))
            return Array.Empty<EvidenceRef>();
        if (el.ValueKind != JsonValueKind.Array)
            return Array.Empty<EvidenceRef>();

        try
        {
            var list = JsonSerializer.Deserialize<List<EvidenceRef>>(el.GetRawText(), JsonOptions);
            return list ?? new List<EvidenceRef>();
        }
        catch
        {
            return Array.Empty<EvidenceRef>();
        }
    }

    private static double? ExtractConfidence(NodeOutput upstream, ReturnInsightArtifactNodeConfig config)
    {
        if (upstream.Confidence.HasValue && string.IsNullOrWhiteSpace(config.ConfidenceFrom))
            return upstream.Confidence;
        if (upstream.StructuredData is null)
            return null;
        var path = string.IsNullOrWhiteSpace(config.ConfidenceFrom) ? "confidence" : config.ConfidenceFrom!;
        if (upstream.StructuredData.Value.TryGetProperty(path, out var el)
            && el.ValueKind == JsonValueKind.Number
            && el.TryGetDouble(out var d))
            return d;
        return upstream.Confidence;
    }

    private static string? ExtractReasoning(NodeOutput upstream, ReturnInsightArtifactNodeConfig config)
    {
        if (upstream.StructuredData is null)
            return null;
        var path = string.IsNullOrWhiteSpace(config.ReasoningFrom) ? "reasoning" : config.ReasoningFrom!;
        if (upstream.StructuredData.Value.TryGetProperty(path, out var el)
            && el.ValueKind == JsonValueKind.String)
            return el.GetString();
        return null;
    }

    private static string RenderId(string? template, NodeExecutionContext context, string subject)
    {
        if (string.IsNullOrWhiteSpace(template))
            return $"art:{subject}:{context.RunId:N}";

        return template!
            .Replace("{runId}", context.RunId.ToString())
            .Replace("{playbookId}", context.PlaybookId.ToString())
            .Replace("{subject}", subject)
            .Replace("{tenantId}", context.TenantId);
    }

    private static ReturnInsightArtifactNodeConfig? ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ReturnInsightArtifactNodeConfig>(configJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Config schema for <see cref="ReturnInsightArtifactNode"/>.
/// </summary>
internal sealed record ReturnInsightArtifactNodeConfig
{
    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("artifactKind")]
    public string? ArtifactKind { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("subjectFrom")]
    public string? SubjectFrom { get; init; }

    [JsonPropertyName("predicate")]
    public string? Predicate { get; init; }

    [JsonPropertyName("displayHint")]
    public string? DisplayHint { get; init; }

    [JsonPropertyName("producedById")]
    public string? ProducedById { get; init; }

    [JsonPropertyName("producedByKind")]
    public string? ProducedByKind { get; init; }

    [JsonPropertyName("producedByVersion")]
    public string? ProducedByVersion { get; init; }

    [JsonPropertyName("valueFrom")]
    public string? ValueFrom { get; init; }

    [JsonPropertyName("evidenceFrom")]
    public string? EvidenceFrom { get; init; }

    [JsonPropertyName("confidenceFrom")]
    public string? ConfidenceFrom { get; init; }

    [JsonPropertyName("reasoningFrom")]
    public string? ReasoningFrom { get; init; }

    [JsonPropertyName("allowEmptyEvidence")]
    public bool? AllowEmptyEvidence { get; init; }
}
