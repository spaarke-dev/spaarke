using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Deterministic exit node that emits a structured <see cref="DeclineResponse"/> per D-49
/// (LAVERN Pattern #7) and D-P12. Invoked when <see cref="EvidenceSufficiencyNode"/> returns
/// <c>insufficient</c>. Zero LLM — the response is composed from upstream gap analysis +
/// a config-driven template, never reasoned about by an Agent.
/// </summary>
/// <remarks>
/// <para>
/// <b>Config schema</b> (read from <see cref="Sprk.Bff.Api.Models.Ai.PlaybookNodeDto.ConfigJson"/>):
/// </para>
/// <code>
/// {
///   "reason":         "insufficient-evidence",                  // optional — default "insufficient-evidence"
///   "from":           "checkSufficiency",                        // required — upstream EvidenceSufficiencyNode output variable
///   "explanationTemplate": "Only {have} comparable matters were found; {need} are required.",  // optional
///   "suggestedActions": [
///     "Broaden the matter-type filter from 'IP licensing' to 'IP'",
///     "Author a Precedent for this opposing counsel"
///   ],
///   "confidenceInDecline": 0.95                                  // optional — default 0.95
/// }
/// </code>
/// <para>
/// Per D-49 the response shape is non-negotiable — five fields enforced by
/// <see cref="DeclineResponse"/>'s required-init constraint. This node guarantees the shape;
/// the playbook only controls reason / template / suggested actions.
/// </para>
/// <para>
/// <b>Zone A</b> per SPEC §3.5 — lives under <c>Services/Ai/Nodes/</c>. Deterministic, no
/// LLM, no I/O.
/// </para>
/// </remarks>
public sealed class DeclineToFindNode : INodeExecutor
{
    /// <summary>Default reason code per SPEC §3.4 worked examples.</summary>
    public const string DefaultReason = "insufficient-evidence";

    /// <summary>Default confidence — high because evidence sufficiency rules are deterministic.</summary>
    public const double DefaultConfidenceInDecline = 0.95;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<DeclineToFindNode> _logger;

    public DeclineToFindNode(ILogger<DeclineToFindNode> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.DeclineToFind
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var config = ParseConfig(context.Node.ConfigJson);
        if (config is null)
            return NodeValidationResult.Failure(
                "DeclineToFind node requires ConfigJson with 'from' field referencing upstream EvidenceSufficiency output.");

        return string.IsNullOrWhiteSpace(config.From)
            ? NodeValidationResult.Failure("ConfigJson.from is required (upstream EvidenceSufficiency output variable).")
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

        // Read the upstream gap analysis. Missing or non-EvidenceSufficiency upstream is tolerated:
        // we emit a generic decline rather than throwing, so authoring errors degrade safely.
        var upstream = context.GetPreviousOutput(config.From!);
        var sufficiencyResult = upstream?.GetData<EvidenceSufficiencyResult>();
        var gaps = sufficiencyResult?.Gaps ?? Array.Empty<EvidenceGap>();

        var reason = string.IsNullOrWhiteSpace(config.Reason) ? DefaultReason : config.Reason!;
        var explanation = BuildExplanation(config.ExplanationTemplate, gaps);
        var minimumEvidence = BuildMinimumEvidence(gaps);
        IReadOnlyList<string> suggested = config.SuggestedActions ?? new List<string>();
        var confidence = config.ConfidenceInDecline ?? DefaultConfidenceInDecline;

        var decline = new DeclineResponse
        {
            Reason = reason,
            Explanation = explanation,
            MinimumEvidenceNeeded = minimumEvidence,
            SuggestedActions = suggested,
            ConfidenceInDecline = confidence
        };

        _logger.LogInformation(
            "DeclineToFindNode {NodeId}: emitted DeclineResponse (reason={Reason}, gaps={GapCount})",
            context.Node.Id, reason, gaps.Count);

        return Task.FromResult(NodeOutput.Ok(
            context.Node.Id,
            context.Node.OutputVariable,
            decline,
            textContent: explanation,
            confidence: confidence,
            metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
    }

    /// <summary>
    /// Renders the explanation. If a template is provided, it supports the tokens
    /// <c>{rule}</c>, <c>{from}</c>, <c>{have}</c>, <c>{need}</c>, <c>{reason}</c> for the
    /// FIRST gap (the most common case for synthesis playbooks with one anchor rule).
    /// Without a template, builds a uniform message from the gap list.
    /// </summary>
    private static string BuildExplanation(string? template, IReadOnlyList<EvidenceGap> gaps)
    {
        if (gaps.Count == 0)
        {
            return string.IsNullOrWhiteSpace(template)
                ? "The question cannot be answered with available evidence."
                : template!;
        }

        if (!string.IsNullOrWhiteSpace(template))
        {
            var primary = gaps[0];
            return template!
                .Replace("{rule}", primary.RuleName)
                .Replace("{from}", primary.From)
                .Replace("{have}", primary.Have.ToString())
                .Replace("{need}", primary.Need.ToString())
                .Replace("{reason}", primary.Reason);
        }

        // Uniform fallback rendering — one line per gap.
        var lines = gaps.Select(g => $"{g.RuleName}: have {g.Have}, need {g.Need}.");
        return "Insufficient evidence: " + string.Join(" ", lines);
    }

    /// <summary>
    /// Projects the gap list into the wire shape required by
    /// <see cref="DeclineResponse.MinimumEvidenceNeeded"/>.
    /// </summary>
    private static IReadOnlyDictionary<string, object> BuildMinimumEvidence(IReadOnlyList<EvidenceGap> gaps)
    {
        if (gaps.Count == 0)
            return new Dictionary<string, object>();

        var dict = new Dictionary<string, object>(gaps.Count);
        foreach (var gap in gaps)
        {
            dict[gap.RuleName] = new
            {
                have = gap.Have,
                need = gap.Need,
                from = gap.From,
                reason = gap.Reason
            };
        }
        return dict;
    }

    private static DeclineToFindNodeConfig? ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<DeclineToFindNodeConfig>(configJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Config schema for <see cref="DeclineToFindNode"/>.
/// </summary>
internal sealed record DeclineToFindNodeConfig
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("explanationTemplate")]
    public string? ExplanationTemplate { get; init; }

    [JsonPropertyName("suggestedActions")]
    public List<string>? SuggestedActions { get; init; }

    [JsonPropertyName("confidenceInDecline")]
    public double? ConfidenceInDecline { get; init; }
}
