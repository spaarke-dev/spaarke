using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Reads prior node outputs and evaluates a configured evidence-sufficiency rule per D-P12 +
/// D-49 (LAVERN Pattern #7). Emits a deterministic <c>sufficient</c> / <c>insufficient</c>
/// verdict + structured gap analysis. Used as the pre-condition gate before a
/// <see cref="ExecutorType.DeclineToFind"/> branch in Insights synthesis playbooks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Config schema</b> (read from <see cref="Sprk.Bff.Api.Models.Ai.PlaybookNodeDto.ConfigJson"/>):
/// </para>
/// <code>
/// {
///   "rules": [
///     {
///       "name": "comparableMatters",
///       "from": "retrieveComparableMatters",       // upstream node output variable name
///       "countFrom": "count",                       // optional — JSON property carrying the count; default "count"
///       "minCount": 12                              // required for count-based rules
///     },
///     {
///       "name": "confirmedPrecedent",
///       "from": "retrievePrecedent",
///       "requireNonEmpty": true                     // alternative — rule passes when array is non-empty
///     },
///     {
///       "name": "outcomeBearingClassification",
///       "from": "layer1",                           // upstream node output variable name
///       "readFrom": "classification",                // dotted path into upstream StructuredData; reads as string
///       "predicate": "in",                           // membership check (NEW in Wave C1 task 020 per design-a5 Gap #1)
///       "value": ["Order", "Settlement", "Verdict", "Judgment"]  // array — rule passes when upstream.readFrom value is in this array
///     }
///   ]
/// }
/// </code>
/// <para>
/// All rules MUST pass for the verdict to be <c>sufficient</c>. When any rule fails, the
/// verdict is <c>insufficient</c> and the gap analysis records every failing rule with its
/// observed-vs-required values so <see cref="ExecutorType.DeclineToFind"/> can render a
/// structured <see cref="Sprk.Bff.Api.Models.Insights.DeclineResponse"/> per D-49.
/// </para>
/// <para>
/// <b>Branching</b>: the output exposes <c>selectedBranch</c> matching the
/// <see cref="ConditionResult"/> convention so the orchestrator can route to either
/// the configured <c>sufficientBranch</c> or <c>insufficientBranch</c> via the same path
/// resolution it uses for <see cref="ConditionNodeExecutor"/>.
/// </para>
/// <para>
/// <b>Zone A</b> per SPEC §3.5 — lives under <c>Services/Ai/Nodes/</c>. Deterministic, no
/// LLM, no Dataverse — pure rule evaluation over upstream <see cref="NodeOutput"/>.
/// </para>
/// </remarks>
public sealed class EvidenceSufficiencyNode : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<EvidenceSufficiencyNode> _logger;

    public EvidenceSufficiencyNode(ILogger<EvidenceSufficiencyNode> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.EvidenceSufficiency
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var config = ParseConfig(context.Node.ConfigJson);
        if (config is null)
            return NodeValidationResult.Failure(
                "EvidenceSufficiency node requires ConfigJson with 'rules' array.");

        if (config.Rules is null || config.Rules.Count == 0)
            return NodeValidationResult.Failure("ConfigJson.rules must contain at least one rule.");

        var errors = new List<string>();
        for (var i = 0; i < config.Rules.Count; i++)
        {
            var rule = config.Rules[i];
            if (string.IsNullOrWhiteSpace(rule.Name))
                errors.Add($"rules[{i}].name is required.");
            if (string.IsNullOrWhiteSpace(rule.From))
                errors.Add($"rules[{i}].from is required (upstream node output variable name).");
            if (rule.MinCount is null && rule.RequireNonEmpty != true && string.IsNullOrWhiteSpace(rule.Predicate))
                errors.Add($"rules[{i}] must specify minCount, requireNonEmpty=true, or predicate.");

            // Wave C1 task 020 — predicate rules require Value + ReadFrom
            if (!string.IsNullOrWhiteSpace(rule.Predicate))
            {
                if (rule.Value is null)
                    errors.Add($"rules[{i}] predicate '{rule.Predicate}' requires a 'value' field.");
                if (string.IsNullOrWhiteSpace(rule.ReadFrom))
                    errors.Add($"rules[{i}] predicate '{rule.Predicate}' requires a 'readFrom' path into upstream StructuredData.");
                if (!string.Equals(rule.Predicate, "in", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"rules[{i}] predicate '{rule.Predicate}' is not supported. Only 'in' is implemented in Wave C1.");
            }
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
        var gaps = new List<EvidenceGap>();

        foreach (var rule in config.Rules!)
        {
            var upstream = context.GetPreviousOutput(rule.From!);
            if (upstream is null || !upstream.Success)
            {
                gaps.Add(new EvidenceGap
                {
                    RuleName = rule.Name!,
                    From = rule.From!,
                    Reason = upstream is null
                        ? $"Upstream node output '{rule.From}' was not found."
                        : $"Upstream node '{rule.From}' failed.",
                    Have = 0,
                    Need = rule.MinCount ?? 1
                });
                continue;
            }

            // Wave C1 task 020 — Gap #1 patch: predicate-based rules (membership "in").
            // Per design-a5 §7.1: enables outcomeBearingClassification rule shape for universal-ingest@v1.
            // Membership rules consult upstream.StructuredData via rule.ReadFrom path; pass when value
            // matches any item in rule.Value array.
            if (!string.IsNullOrWhiteSpace(rule.Predicate))
            {
                var (predicatePasses, observedValue) = EvaluatePredicate(upstream, rule);
                if (!predicatePasses)
                {
                    gaps.Add(new EvidenceGap
                    {
                        RuleName = rule.Name!,
                        From = rule.From!,
                        Reason = $"Upstream '{rule.From}' value '{observedValue ?? "<null>"}' at path '{rule.ReadFrom}' did not match predicate '{rule.Predicate}'.",
                        Have = 0,
                        Need = 1
                    });
                }
                continue;
            }

            var observed = ExtractCount(upstream, rule);
            var required = rule.MinCount ?? 1;
            var passes = rule.RequireNonEmpty == true ? observed > 0 : observed >= required;

            if (!passes)
            {
                gaps.Add(new EvidenceGap
                {
                    RuleName = rule.Name!,
                    From = rule.From!,
                    Reason = rule.RequireNonEmpty == true
                        ? $"Upstream '{rule.From}' had zero items; non-empty required."
                        : $"Upstream '{rule.From}' had {observed} item(s); minCount={required}.",
                    Have = observed,
                    Need = required
                });
            }
        }

        var sufficient = gaps.Count == 0;
        var selectedBranch = sufficient
            ? (config.SufficientBranch ?? "sufficient")
            : (config.InsufficientBranch ?? "insufficient");

        var output = new EvidenceSufficiencyResult
        {
            Sufficient = sufficient,
            SelectedBranch = selectedBranch,
            SufficientBranch = config.SufficientBranch,
            InsufficientBranch = config.InsufficientBranch,
            Gaps = gaps
        };

        _logger.LogInformation(
            "EvidenceSufficiencyNode {NodeId}: verdict={Verdict}, gaps={GapCount}, branch={Branch}",
            context.Node.Id, sufficient ? "sufficient" : "insufficient", gaps.Count, selectedBranch);

        return Task.FromResult(NodeOutput.Ok(
            context.Node.Id,
            context.Node.OutputVariable,
            output,
            textContent: sufficient
                ? "Evidence sufficient — all rules passed."
                : $"Evidence insufficient — {gaps.Count} rule(s) failed.",
            metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
    }

    private static EvidenceSufficiencyConfig? ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<EvidenceSufficiencyConfig>(configJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the observed count from the upstream node output per the rule's
    /// <see cref="EvidenceSufficiencyRule.CountFrom"/> path (default "count"). Falls back to
    /// counting array elements when the configured property is itself an array.
    /// </summary>
    private static int ExtractCount(NodeOutput upstream, EvidenceSufficiencyRule rule)
    {
        if (upstream.StructuredData is null)
            return 0;

        var data = upstream.StructuredData.Value;
        var path = string.IsNullOrWhiteSpace(rule.CountFrom) ? "count" : rule.CountFrom!;

        if (!data.TryGetProperty(path, out var element))
        {
            // Common alternative: the upstream emits an "artifacts" or "items" array directly.
            foreach (var alt in new[] { "artifacts", "items", "results", "rows" })
            {
                if (data.TryGetProperty(alt, out var altEl) && altEl.ValueKind == JsonValueKind.Array)
                    return altEl.GetArrayLength();
            }
            return 0;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var i) => i,
            JsonValueKind.Number when element.TryGetInt64(out var l) => (int)Math.Min(l, int.MaxValue),
            JsonValueKind.Array => element.GetArrayLength(),
            _ => 0
        };
    }

    /// <summary>
    /// Evaluates a predicate-based rule (Wave C1 Gap #1 per design-a5 §7.1). Currently supports
    /// only <c>predicate: "in"</c> (membership check) — extends EvidenceSufficiencyNode beyond
    /// the existing <c>minCount</c> / <c>requireNonEmpty</c> shapes so universal-ingest@v1's
    /// <c>outcomeBearingClassification</c> rule can be declared in JPS data rather than computed
    /// in the layer1Classify executor (the design's "Option (a)" choice — see §7.1).
    /// </summary>
    /// <param name="upstream">Upstream node output to read the value from.</param>
    /// <param name="rule">Rule definition; <see cref="EvidenceSufficiencyRule.ReadFrom"/> is the dotted
    /// path into <see cref="NodeOutput.StructuredData"/>; <see cref="EvidenceSufficiencyRule.Value"/>
    /// is the array of strings the value must match.</param>
    /// <returns>Tuple of (passes, observedValue). Observed value is the upstream's actual value at
    /// ReadFrom (rendered as string for gap reasons). When the upstream has no StructuredData or
    /// the path is missing, the predicate fails with observedValue=null.</returns>
    private static (bool passes, string? observedValue) EvaluatePredicate(
        NodeOutput upstream,
        EvidenceSufficiencyRule rule)
    {
        if (upstream.StructuredData is null)
            return (false, null);

        var observed = ReadPathValue(upstream.StructuredData.Value, rule.ReadFrom!);
        if (observed is null)
            return (false, null);

        // Only "in" supported in Wave C1; Validate() rejects other predicates upstream.
        if (string.Equals(rule.Predicate, "in", StringComparison.OrdinalIgnoreCase))
        {
            var allowedValues = MaterializeValueArray(rule.Value);
            var matches = allowedValues.Any(v => string.Equals(v, observed, StringComparison.OrdinalIgnoreCase));
            return (matches, observed);
        }

        // Should not reach — Validate() catches this earlier.
        return (false, observed);
    }

    /// <summary>
    /// Reads a single string value from a JsonElement at the specified path. Returns null when the
    /// path is missing or the value is not a primitive convertible to string. Used by
    /// <see cref="EvaluatePredicate"/> for predicate-based rules.
    /// </summary>
    private static string? ReadPathValue(JsonElement data, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !data.TryGetProperty(path, out var element))
            return null;

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    /// <summary>
    /// Coerces <see cref="EvidenceSufficiencyRule.Value"/> (declared as <c>object</c> for JSON
    /// flexibility) into a string array. Handles JsonElement (the typical deserialized shape),
    /// IEnumerable, and single scalar fallback.
    /// </summary>
    private static IReadOnlyList<string> MaterializeValueArray(object? raw)
    {
        if (raw is null) return Array.Empty<string>();

        if (raw is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>(json.GetArrayLength());
                foreach (var item in json.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        list.Add(item.GetString() ?? string.Empty);
                    else
                        list.Add(item.ToString());
                }
                return list;
            }
            if (json.ValueKind == JsonValueKind.String)
                return new[] { json.GetString() ?? string.Empty };
            return new[] { json.ToString() };
        }

        if (raw is IEnumerable<object> enumerable)
            return enumerable.Select(o => o?.ToString() ?? string.Empty).ToList();

        return new[] { raw.ToString() ?? string.Empty };
    }
}

/// <summary>
/// Config schema for <see cref="EvidenceSufficiencyNode"/>.
/// </summary>
internal sealed record EvidenceSufficiencyConfig
{
    [JsonPropertyName("rules")]
    public List<EvidenceSufficiencyRule>? Rules { get; init; }

    [JsonPropertyName("sufficientBranch")]
    public string? SufficientBranch { get; init; }

    [JsonPropertyName("insufficientBranch")]
    public string? InsufficientBranch { get; init; }
}

/// <summary>
/// A single evidence-sufficiency rule.
/// </summary>
internal sealed record EvidenceSufficiencyRule
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("countFrom")]
    public string? CountFrom { get; init; }

    [JsonPropertyName("minCount")]
    public int? MinCount { get; init; }

    [JsonPropertyName("requireNonEmpty")]
    public bool? RequireNonEmpty { get; init; }

    /// <summary>
    /// Wave C1 task 020 — predicate-based rule shape (Gap #1 per design-a5 §7.1).
    /// Currently only <c>"in"</c> (membership check) is supported. Coexists with
    /// <see cref="MinCount"/> / <see cref="RequireNonEmpty"/>; Validate() enforces
    /// exactly one of the three shapes.
    /// </summary>
    [JsonPropertyName("predicate")]
    public string? Predicate { get; init; }

    /// <summary>
    /// Value to test against — array for <c>"in"</c> predicate. Declared as <c>object</c>
    /// to accept either a JsonElement array (typical) or a templated string array after
    /// <c>{{var}}</c> substitution. Materialized via
    /// <c>MaterializeValueArray</c>.
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; init; }

    /// <summary>
    /// Dotted path into the upstream's StructuredData to read the value for predicate evaluation.
    /// Example: <c>"classification"</c> reads <c>upstream.StructuredData.classification</c>.
    /// Only single-property paths are supported in Wave C1 (no <c>"a.b.c"</c> deep paths).
    /// </summary>
    [JsonPropertyName("readFrom")]
    public string? ReadFrom { get; init; }
}

/// <summary>
/// Structured output of <see cref="EvidenceSufficiencyNode"/>.
/// Consumed by downstream <see cref="ExecutorType.DeclineToFind"/> +
/// <see cref="ExecutorType.ReturnInsightArtifact"/> nodes.
/// </summary>
public sealed record EvidenceSufficiencyResult
{
    public required bool Sufficient { get; init; }
    public required string SelectedBranch { get; init; }
    public string? SufficientBranch { get; init; }
    public string? InsufficientBranch { get; init; }
    public required IReadOnlyList<EvidenceGap> Gaps { get; init; }
}

/// <summary>
/// One failing rule's gap analysis. Aggregated into
/// <see cref="Sprk.Bff.Api.Models.Insights.DeclineResponse.MinimumEvidenceNeeded"/> by
/// <see cref="DeclineToFindNode"/>.
/// </summary>
public sealed record EvidenceGap
{
    public required string RuleName { get; init; }
    public required string From { get; init; }
    public required string Reason { get; init; }
    public required int Have { get; init; }
    public required int Need { get; init; }
}
