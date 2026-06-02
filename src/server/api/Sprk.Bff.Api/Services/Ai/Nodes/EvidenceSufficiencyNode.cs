using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Reads prior node outputs and evaluates a configured evidence-sufficiency rule per D-P12 +
/// D-49 (LAVERN Pattern #7). Emits a deterministic <c>sufficient</c> / <c>insufficient</c>
/// verdict + structured gap analysis. Used as the pre-condition gate before a
/// <see cref="ActionType.DeclineToFind"/> branch in Insights synthesis playbooks.
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
///     }
///   ]
/// }
/// </code>
/// <para>
/// All rules MUST pass for the verdict to be <c>sufficient</c>. When any rule fails, the
/// verdict is <c>insufficient</c> and the gap analysis records every failing rule with its
/// observed-vs-required values so <see cref="ActionType.DeclineToFind"/> can render a
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
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.EvidenceSufficiency
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
            if (rule.MinCount is null && rule.RequireNonEmpty != true)
                errors.Add($"rules[{i}] must specify either minCount or requireNonEmpty=true.");
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
}

/// <summary>
/// Structured output of <see cref="EvidenceSufficiencyNode"/>.
/// Consumed by downstream <see cref="ActionType.DeclineToFind"/> +
/// <see cref="ActionType.ReturnInsightArtifact"/> nodes.
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
