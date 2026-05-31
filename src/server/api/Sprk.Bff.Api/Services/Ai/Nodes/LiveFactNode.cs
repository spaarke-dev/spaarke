using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Insights.LiveFacts;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor that wraps <see cref="ILiveFactResolver"/> for use in node-based Insights
/// playbooks (D-P12 + D-P14). Reads a <c>{subject, predicate}</c> pair from <c>ConfigJson</c>
/// (with template-rendered variables), invokes the resolver, and emits a deterministic
/// <see cref="FactArtifact"/> per design.md §2.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Config schema</b> (read from <see cref="Sprk.Bff.Api.Models.Ai.PlaybookNodeDto.ConfigJson"/>):
/// </para>
/// <code>
/// {
///   "subject": "matter:M-1234",     // required — scheme-prefixed subject (template variables allowed)
///   "predicate": "totalSpend"        // required — claim name supported by ILiveFactResolver
/// }
/// </code>
/// <para>
/// The structured output is the resolved <see cref="FactArtifact"/> serialized into
/// <see cref="NodeOutput.StructuredData"/>. Downstream nodes (e.g.,
/// <c>EvidenceSufficiencyNode</c>, <c>AiCompletion</c>) read it via
/// <see cref="NodeOutput.GetData{T}"/>.
/// </para>
/// <para>
/// <b>Zone A</b> per SPEC §3.5 — lives under <c>Services/Ai/Nodes/</c> alongside the other
/// platform node executors. The Zone B <see cref="ILiveFactResolver"/> is injected as a
/// dependency.
/// </para>
/// <para>
/// <b>Confidence</b> on the returned Fact is always 1.0 per design.md §2.1; the executor
/// surfaces it on <see cref="NodeOutput.Confidence"/> as well so downstream sufficiency gates
/// can read it without deserializing.
/// </para>
/// </remarks>
public sealed class LiveFactNode : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILiveFactResolver _resolver;
    private readonly ILogger<LiveFactNode> _logger;

    public LiveFactNode(ILiveFactResolver resolver, ILogger<LiveFactNode> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.LiveFact
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var config = ParseConfig(context.Node.ConfigJson);
        if (config is null)
            return NodeValidationResult.Failure(
                "LiveFact node requires ConfigJson with 'subject' and 'predicate' fields.");

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(config.Subject))
            errors.Add("ConfigJson.subject is required (e.g., 'matter:M-1234').");
        if (string.IsNullOrWhiteSpace(config.Predicate))
            errors.Add("ConfigJson.predicate is required (e.g., 'totalSpend').");

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

        var validation = Validate(context);
        if (!validation.IsValid)
        {
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                string.Join("; ", validation.Errors),
                NodeErrorCodes.ValidationFailed,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }

        var config = ParseConfig(context.Node.ConfigJson)!;

        try
        {
            _logger.LogDebug(
                "LiveFactNode {NodeId}: resolving subject={Subject} predicate={Predicate}",
                context.Node.Id, config.Subject, config.Predicate);

            var fact = await _resolver.ResolveAsync(
                config.Subject!,
                config.Predicate!,
                context.TenantId,
                cancellationToken).ConfigureAwait(false);

            if (fact is null)
            {
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    $"Subject '{config.Subject}' not found in Dataverse.",
                    NodeErrorCodes.InternalError,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            _logger.LogDebug(
                "LiveFactNode {NodeId} resolved: subject={Subject} predicate={Predicate} id={FactId}",
                context.Node.Id, config.Subject, config.Predicate, fact.Id);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                fact,
                textContent: $"Resolved {config.Predicate} for {config.Subject}",
                confidence: fact.Confidence,
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LiveFactNotSupportedException ex)
        {
            _logger.LogWarning(ex,
                "LiveFactNode {NodeId}: predicate '{Predicate}' not supported on subject '{Subject}'",
                context.Node.Id, config.Predicate, config.Subject);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                ex.Message,
                NodeErrorCodes.InvalidConfiguration,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LiveFactNode {NodeId} failed: {Message}", context.Node.Id, ex.Message);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Live Fact resolution failed: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    private static LiveFactNodeConfig? ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<LiveFactNodeConfig>(configJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Config schema for <see cref="LiveFactNode"/>.
/// </summary>
internal sealed record LiveFactNodeConfig
{
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("predicate")]
    public string? Predicate { get; init; }
}
