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
/// <b>r2 Wave D5 (task 034) — multi-entity dispatch</b>: per design-a6 §3.5 + A6-D1, this
/// node now dispatches to one of three per-entity <see cref="ILiveFactResolver"/>
/// implementations (matter, project, invoice) via
/// <c>IReadOnlyDictionary&lt;string, ILiveFactResolver&gt;</c> keyed by entity-type name.
/// Subject is parsed via <see cref="ISubjectParser"/> first; the parsed entity type is
/// used as the dictionary key. Unknown schemes surface as <c>InvalidConfiguration</c>.
/// </para>
/// <para>
/// <b>Backward compatibility</b>: existing <c>matter:&lt;guid&gt;</c> subjects continue to
/// resolve identically (the matter resolver is now <see cref="MatterLiveFactResolver"/>
/// with behavior preserved 1:1 from r1's <c>DataverseLiveFactResolver</c>). The Phase 1
/// <c>predict-matter-cost</c> playbook works without playbook-side changes.
/// </para>
/// <para>
/// <b>Zone A</b> per SPEC §3.5 — lives under <c>Services/Ai/Nodes/</c> alongside the other
/// platform node executors. The Zone B <see cref="ILiveFactResolver"/> implementations are
/// injected as a registry.
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

    private readonly IReadOnlyDictionary<string, ILiveFactResolver> _resolvers;
    private readonly ISubjectParser _subjectParser;
    private readonly ILogger<LiveFactNode> _logger;

    public LiveFactNode(
        IReadOnlyDictionary<string, ILiveFactResolver> resolvers,
        ISubjectParser subjectParser,
        ILogger<LiveFactNode> logger)
    {
        _resolvers = resolvers ?? throw new ArgumentNullException(nameof(resolvers));
        _subjectParser = subjectParser ?? throw new ArgumentNullException(nameof(subjectParser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.LiveFact
    };

    // R7 task 085 / FR-23 — typed config schema for Playbook Builder canvas.
    // Derived from LiveFactNodeConfig: subject (required, scheme-prefixed), predicate (required).
    private static readonly ExecutorConfigSchema ConfigSchemaInstance = new(
        ExecutorTypeName: nameof(ExecutorType.LiveFact),
        ExecutorTypeValue: (int)ExecutorType.LiveFact,
        Description: "Resolves a deterministic Live Fact about a Dataverse subject via ILiveFactResolver (per-entity dispatch: matter, project, invoice). Confidence is always 1.0 per design.md §2.1.",
        Fields: new ConfigSchemaField[]
        {
            new(
                Name: "subject",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "Scheme-prefixed subject (e.g., 'matter:M-1234', 'project:p-abc', 'invoice:i-xyz'). Required. Supports {{var}} substitution. Scheme determines the per-entity resolver.",
                Default: null),
            new(
                Name: "predicate",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "Claim name supported by the resolver (e.g., 'totalSpend', 'matterType'). Required. Unknown predicates surface as LiveFactNotSupportedException → InvalidConfiguration.",
                Default: null)
        });

    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() => ConfigSchemaInstance;

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

        // Parse the subject to extract the entity-type used for resolver dispatch
        // (r2 Wave D5 / design-a6 §3.5). Unknown schemes surface as InvalidConfiguration
        // so playbook authoring errors are loud, not silent.
        if (!_subjectParser.TryParse(config.Subject!, out var parsedSubject, out var parseError))
        {
            _logger.LogWarning(
                "LiveFactNode {NodeId}: subject parse failed: subject={Subject} error={Error}",
                context.Node.Id, config.Subject, parseError);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                parseError,
                NodeErrorCodes.InvalidConfiguration,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }

        // Dispatch by entity-type to the per-entity resolver. The dictionary uses
        // case-insensitive lookup (registered with StringComparer.OrdinalIgnoreCase in
        // InsightsModule per design-a6 §3.4).
        if (!_resolvers.TryGetValue(parsedSubject.EntityType, out var resolver))
        {
            var msg = $"No ILiveFactResolver registered for subject entity-type '{parsedSubject.EntityType}'. " +
                      $"Register an implementation in InsightsModule and add the scheme to " +
                      $"Insights:Subject:Schemes[] (per design-a6 §3.4).";
            _logger.LogWarning(
                "LiveFactNode {NodeId}: no resolver for entity-type {EntityType} (subject={Subject})",
                context.Node.Id, parsedSubject.EntityType, config.Subject);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                msg,
                NodeErrorCodes.InvalidConfiguration,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }

        try
        {
            _logger.LogDebug(
                "LiveFactNode {NodeId}: resolving entityType={EntityType} subject={Subject} predicate={Predicate}",
                context.Node.Id, parsedSubject.EntityType, config.Subject, config.Predicate);

            var fact = await resolver.ResolveAsync(
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
