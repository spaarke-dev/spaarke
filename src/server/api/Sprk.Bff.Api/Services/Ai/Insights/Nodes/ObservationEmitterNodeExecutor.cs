using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.Insights.Extraction;
using Sprk.Bff.Api.Services.Ai.Insights.Ingest;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Insights.Nodes;

/// <summary>
/// Wave C1 task 020 — node executor for <see cref="ExecutorType.ObservationEmit"/> (140). Final
/// node of the universal-ingest@v1 JPS playbook per design-a5 §4 Node 6. Wraps
/// <see cref="IObservationEmitter"/> + <see cref="IObservationIndexUpserter"/> +
/// <see cref="IObservationMirror"/>. Replaced the per-field Observation emission portion of
/// the code-defined <c>IngestOrchestrator</c> (retired Wave C-G4 / task 022).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone A</b> per SPEC §3.5 — discovered automatically by <see cref="NodeExecutorRegistry"/>
/// via <see cref="SupportedActionTypes"/>; registered in
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.InsightsIngestModule"/>.
/// </para>
/// <para>
/// <b>Inputs</b>:
/// <list type="bullet">
///   <item>Upstream <c>grounded</c> output — JSON array of surviving candidates (post grounding
///   verification). Each candidate has <c>{ fieldName, value, quote, confidence, displayHint }</c>.
///   When upstream <c>groundingVerify</c> was branch-skipped (insufficient path), this upstream is
///   <c>Ok(null)</c>; the executor emits zero L2 observations and returns
///   <c>observationsEmitted: 1</c> (L1 only).</item>
///   <item>Upstream <c>layer1</c> output — read for the classification string (for parity-check
///   logging + result shape matching r1 <c>InsightsIngestResult</c>).</item>
///   <item><c>parameters.matterId</c>, <c>parameters.tenantId</c>, <c>parameters.documentRef</c>
///   — used to construct <see cref="ExtractionResult.Subject"/> / <see cref="ExtractionResult.Scope"/>
///   for the emitter.</item>
/// </list>
/// </para>
/// <para>
/// <b>Output</b> (<c>outputVariable: emission</c>): shape matches the existing
/// <see cref="Sprk.Bff.Api.Models.Ai.PublicContracts.InsightsIngestResult"/> exactly:
/// <c>{ observationsEmitted: int, layer1Classification: string, layer2Triggered: bool }</c>.
/// Wave C4 (<c>IInsightsAi.RunIngestAsync</c>) reads this from the final node output and returns
/// it to callers.
/// </para>
/// <para>
/// <b>Layer 1 observation emission</b> is NOT performed here — it stays in the layer1Classify
/// executor's post-LLM hook (see <c>universal-ingest.playbook.json</c> §node 2 comment-emission).
/// This executor only emits L2 per-field observations.
/// </para>
/// </remarks>
public sealed class ObservationEmitterNodeExecutor : INodeExecutor
{
    private const string ParamMatterId = "matterId";
    private const string ParamTenantId = "tenantId";
    private const string ParamDocumentRef = "documentRef";

    private const string UpstreamGrounded = "grounded";
    private const string UpstreamLayer1 = "layer1";

    /// <summary>Producer identity matching the retired r1 <c>IngestOrchestrator.Layer2Producer</c>
    /// shape (preserved across Wave C-G4 / task 022 for substrate parity).</summary>
    private static readonly ProducerIdentity Layer2Producer = new()
    {
        Kind = "playbook",
        Id = "playbook://outcome-extraction@v1",
        Version = "v1"
    };

    private readonly IObservationEmitter _emitter;
    private readonly IObservationIndexUpserter _indexUpserter;
    private readonly IObservationMirror _mirror;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ObservationEmitterNodeExecutor> _logger;

    public ObservationEmitterNodeExecutor(
        IObservationEmitter emitter,
        IObservationIndexUpserter indexUpserter,
        IObservationMirror mirror,
        TimeProvider timeProvider,
        ILogger<ObservationEmitterNodeExecutor> logger)
    {
        _emitter = emitter ?? throw new ArgumentNullException(nameof(emitter));
        _indexUpserter = indexUpserter ?? throw new ArgumentNullException(nameof(indexUpserter));
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedActionTypes { get; } = new[]
    {
        ExecutorType.ObservationEmit
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();
        if (context.Parameters is null || !context.Parameters.ContainsKey(ParamMatterId))
            errors.Add($"ObservationEmitterNode requires parameters.{ParamMatterId}.");
        if (context.Parameters is null || !context.Parameters.ContainsKey(ParamTenantId))
            errors.Add($"ObservationEmitterNode requires parameters.{ParamTenantId}.");
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

        var matterId = context.Parameters[ParamMatterId];
        var tenantId = context.Parameters[ParamTenantId];
        var documentRef = context.Parameters.TryGetValue(ParamDocumentRef, out var dr)
            ? dr : string.Empty;

        // Read upstream layer1 (for classification + L1-only result when gate=insufficient).
        var layer1Output = context.GetPreviousOutput(UpstreamLayer1);
        var layer1Classification = ExtractClassification(layer1Output);

        // Read upstream grounded — null/missing when checkLayer2Gate routed to "emitObservations"
        // (insufficient path) and groundingVerify/layer2Extract were branch-skipped.
        var groundedOutput = context.GetPreviousOutput(UpstreamGrounded);
        var layer2Triggered = groundedOutput is not null
            && groundedOutput.Success
            && groundedOutput.StructuredData is not null;

        var emittedCount = 0;
        if (layer2Triggered)
        {
            try
            {
                var fields = ParseCandidates(groundedOutput!.StructuredData!.Value);
                if (fields.Count > 0)
                {
                    var extraction = new ExtractionResult
                    {
                        Subject = $"matter:{matterId}",
                        DocumentRef = documentRef,
                        TenantId = tenantId,
                        ProducedBy = Layer2Producer,
                        AsOf = _timeProvider.GetUtcNow(),
                        Fields = fields,
                        Scope = new ExtractionScope { MatterId = matterId }
                    };

                    var emitted = await _emitter.EmitFromExtractionAsync(
                        extraction: extraction,
                        upsertAsync: async (obs, innerCt) =>
                        {
                            await _indexUpserter.UpsertAsync(obs, innerCt).ConfigureAwait(false);
                            await TryMirrorAsync(obs, innerCt).ConfigureAwait(false);
                        },
                        ct: cancellationToken).ConfigureAwait(false);

                    emittedCount = emitted.Count;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ObservationEmitterNode {NodeId} failed during L2 emission: {Message}",
                    context.Node.Id, ex.Message);
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    $"L2 observation emission failed: {ex.Message}",
                    NodeErrorCodes.InternalError,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }
        }

        // L1 observation is emitted by layer1Classify executor (per design-a5 §4 Node 2 dual-
        // responsibility fusion). Count = 1 for L1 + N for L2 candidates that survived.
        var totalEmitted = 1 + emittedCount;

        var output = new ObservationEmissionResult
        {
            ObservationsEmitted = totalEmitted,
            Layer1Classification = layer1Classification,
            Layer2Triggered = layer2Triggered
        };

        _logger.LogInformation(
            "ObservationEmitterNode {NodeId}: matterId={MatterId} tenantId={TenantId} layer1Classification={Layer1Classification} layer2Triggered={Layer2Triggered} observationsEmitted={ObservationsEmitted}",
            context.Node.Id, matterId, tenantId, layer1Classification, layer2Triggered, totalEmitted);

        return NodeOutput.Ok(
            context.Node.Id,
            context.Node.OutputVariable,
            output,
            textContent: $"Emitted {totalEmitted} observation(s) ({(layer2Triggered ? "L1 + L2" : "L1 only")})",
            metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Extracts the classification string from the upstream <c>layer1</c> output. Returns null
    /// when the upstream is missing, failed, or has no <c>classification</c> property.
    /// </summary>
    private static string? ExtractClassification(NodeOutput? layer1Output)
    {
        if (layer1Output is null || !layer1Output.Success || layer1Output.StructuredData is null)
            return null;

        var data = layer1Output.StructuredData.Value;
        if (data.ValueKind != JsonValueKind.Object) return null;

        if (data.TryGetProperty("classification", out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        if (data.TryGetProperty("Classification", out var propPascal) && propPascal.ValueKind == JsonValueKind.String)
            return propPascal.GetString();

        return null;
    }

    /// <summary>
    /// Parses the upstream <c>grounded</c> candidates array (post grounding) into a Fields
    /// dictionary keyed by field name. Each candidate carries <c>{ fieldName, value, quote,
    /// confidence, displayHint }</c> — same shape as r1 <c>IngestOrchestrator</c> projection.
    /// </summary>
    private static IReadOnlyDictionary<string, ExtractionField> ParseCandidates(JsonElement grounded)
    {
        var fields = new Dictionary<string, ExtractionField>();

        // grounded may be the candidates array directly, OR an object wrapping {candidates: [...]}.
        JsonElement candidatesArray;
        if (grounded.ValueKind == JsonValueKind.Array)
        {
            candidatesArray = grounded;
        }
        else if (grounded.ValueKind == JsonValueKind.Object &&
                 grounded.TryGetProperty("candidates", out var nested) &&
                 nested.ValueKind == JsonValueKind.Array)
        {
            candidatesArray = nested;
        }
        else
        {
            return fields;
        }

        foreach (var candidate in candidatesArray.EnumerateArray())
        {
            if (candidate.ValueKind != JsonValueKind.Object) continue;

            var fieldName = TryGetString(candidate, "fieldName") ?? TryGetString(candidate, "FieldName");
            if (string.IsNullOrWhiteSpace(fieldName)) continue;

            JsonElement value = default;
            if (candidate.TryGetProperty("value", out var v)) value = v.Clone();
            else if (candidate.TryGetProperty("Value", out var vp)) value = vp.Clone();
            if (value.ValueKind == JsonValueKind.Undefined) continue;

            var quote = TryGetString(candidate, "quote") ?? TryGetString(candidate, "Quote") ?? string.Empty;
            var displayHint = TryGetString(candidate, "displayHint") ?? TryGetString(candidate, "DisplayHint") ?? string.Empty;
            var confidence = TryGetDouble(candidate, "confidence") ?? TryGetDouble(candidate, "Confidence") ?? 0.0;

            fields[fieldName] = new ExtractionField
            {
                Value = value,
                Quote = quote,
                Confidence = confidence,
                DisplayHint = displayHint
            };
        }

        return fields;
    }

    private static string? TryGetString(JsonElement obj, string propertyName)
    {
        if (obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static double? TryGetDouble(JsonElement obj, string propertyName)
    {
        if (obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetDouble();
        return null;
    }

    private async Task TryMirrorAsync(ObservationArtifact observation, CancellationToken ct)
    {
        try
        {
            await _mirror.MirrorAsync(observation, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Mirror failures are non-fatal per IObservationMirror contract (matches r1
            // IngestOrchestrator.TryMirrorAsync).
            _logger.LogWarning(ex,
                "ObservationMirror failed (non-fatal; substrate write was successful): observationId={ObservationId} predicate={Predicate}",
                observation.Id, observation.Predicate);
        }
    }
}

/// <summary>
/// Structured output of <see cref="ObservationEmitterNodeExecutor"/>. Shape mirrors r1
/// <see cref="Sprk.Bff.Api.Models.Ai.PublicContracts.InsightsIngestResult"/> exactly so
/// Wave C4 <c>IInsightsAi.RunIngestAsync</c> can return it without translation.
/// </summary>
public sealed record ObservationEmissionResult
{
    [JsonPropertyName("observationsEmitted")]
    public required int ObservationsEmitted { get; init; }

    [JsonPropertyName("layer1Classification")]
    public string? Layer1Classification { get; init; }

    [JsonPropertyName("layer2Triggered")]
    public required bool Layer2Triggered { get; init; }
}
