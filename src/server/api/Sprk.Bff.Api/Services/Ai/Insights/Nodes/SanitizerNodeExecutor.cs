using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Insights.Sanitization;
using Sprk.Bff.Api.Services.Ai.Nodes;

namespace Sprk.Bff.Api.Services.Ai.Insights.Nodes;

/// <summary>
/// Wave C1 task 020 — node executor for <see cref="ExecutorType.Sanitization"/> (130). Wraps
/// <see cref="IInsightsContentSanitizer"/> (D-50 / D-A25) as the first node of the
/// universal-ingest@v1 JPS playbook per design-a5 §4 Node 1. Replaced the inline sanitization
/// step in the code-defined <c>IngestOrchestrator.RunAsync</c> (retired Wave C-G4 / task 022).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone A</b> per SPEC §3.5 — lives under <c>Services/Ai/Insights/Nodes/</c> and freely
/// imports <see cref="IInsightsContentSanitizer"/> (Zone A). Discovered automatically by
/// <see cref="NodeExecutorRegistry"/> via <see cref="SupportedExecutorTypes"/>; the registration
/// is added in <see cref="Sprk.Bff.Api.Infrastructure.DI.InsightsIngestModule"/> alongside the
/// other ingest pipeline services.
/// </para>
/// <para>
/// <b>Inputs</b>:
/// <list type="bullet">
///   <item><c>parameters.documentText</c> — concatenated raw document text. Resolved by
///   <c>IInsightsAi.RunIngestAsync</c> (Wave C4) from <c>IIngestDocumentSource.FetchAsync</c>.</item>
///   <item><c>parameters.chunksJson</c> — JSON-serialized array of raw chunks for grounding.
///   Pass-through (unmodified — grounding verifier requires raw text to match verbatim quotes,
///   matching the contract from the retired r1 <c>IngestOrchestrator</c>).</item>
///   <item><c>parameters.documentRef</c> — stable document reference (used downstream by
///   grounding + observation emission).</item>
/// </list>
/// </para>
/// <para>
/// <b>Output</b> (<c>outputVariable: sanitization</c>):
/// <code>
/// {
///   "sanitizedText": "string",
///   "originalLength": int,
///   "documentRef": "string",
///   "chunks": [ { /* raw chunks passed through */ } ]
/// }
/// </code>
/// Empty <c>sanitizedText</c> → fails the node with errorCode = <c>SANITIZE_EMPTY</c>; downstream
/// <c>layer1Classify</c> sees the failed upstream and is skipped per the existing
/// dependency-failure-skip rule.
/// </para>
/// <para>
/// <b>ConfigJson</b> is reserved for future per-invocation overrides (e.g., per-tenant sanitizer
/// variant); Wave C1 ignores it.
/// </para>
/// </remarks>
public sealed class SanitizerNodeExecutor : INodeExecutor
{
    private const string ParamDocumentText = "documentText";
    private const string ParamChunksJson = "chunksJson";
    private const string ParamDocumentRef = "documentRef";

    private const string ErrSanitizeEmpty = "SANITIZE_EMPTY";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IInsightsContentSanitizer _sanitizer;
    private readonly ILogger<SanitizerNodeExecutor> _logger;

    public SanitizerNodeExecutor(
        IInsightsContentSanitizer sanitizer,
        ILogger<SanitizerNodeExecutor> logger)
    {
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.Sanitization
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        if (context.Parameters is null || !context.Parameters.ContainsKey(ParamDocumentText))
            return NodeValidationResult.Failure(
                $"SanitizerNode requires parameters.{ParamDocumentText} (concatenated document text).");

        return NodeValidationResult.Success();
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

        var documentText = context.Parameters[ParamDocumentText];
        var documentRef = context.Parameters.TryGetValue(ParamDocumentRef, out var refValue)
            ? refValue : string.Empty;

        try
        {
            var sanitization = await _sanitizer.SanitizeAsync(documentText, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(sanitization.SanitizedText))
            {
                _logger.LogInformation(
                    "SanitizerNode {NodeId}: sanitized text is empty (originalLength={OriginalLength}); downstream nodes will be skipped.",
                    context.Node.Id, sanitization.OriginalLength);
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    "Sanitized text is empty; document may have only retrieval blocks or non-content characters.",
                    ErrSanitizeEmpty,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Parse chunks JSON pass-through (raw chunks remain unmodified — GroundingVerifier
            // needs verbatim quotes against unmodified chunks, matching the contract from the
            // retired r1 IngestOrchestrator).
            JsonElement chunksElement = default;
            var hasChunks = false;
            if (context.Parameters.TryGetValue(ParamChunksJson, out var chunksJson) &&
                !string.IsNullOrWhiteSpace(chunksJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(chunksJson);
                    chunksElement = doc.RootElement.Clone();
                    hasChunks = true;
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex,
                        "SanitizerNode {NodeId}: failed to parse parameters.{ParamChunksJson}; grounding will receive empty chunks.",
                        context.Node.Id, ParamChunksJson);
                }
            }

            var output = new SanitizationNodeOutput
            {
                SanitizedText = sanitization.SanitizedText,
                OriginalLength = sanitization.OriginalLength,
                DocumentRef = documentRef,
                Chunks = hasChunks ? chunksElement : default
            };

            _logger.LogDebug(
                "SanitizerNode {NodeId}: sanitized {SanitizedLength} chars (from {OriginalLength}); documentRef={DocumentRef}",
                context.Node.Id, sanitization.SanitizedText.Length, sanitization.OriginalLength, documentRef);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                output,
                textContent: $"Sanitized {sanitization.SanitizedText.Length} chars",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SanitizerNode {NodeId} failed: {Message}", context.Node.Id, ex.Message);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Sanitization failed: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }
}

/// <summary>
/// Structured output of <see cref="SanitizerNodeExecutor"/>. Consumed by downstream
/// <c>layer1Classify</c>, <c>layer2Extract</c> (via <c>sanitization.sanitizedText</c>) and
/// <c>groundingVerify</c> (via <c>sanitization.chunks</c> pass-through).
/// </summary>
public sealed record SanitizationNodeOutput
{
    [JsonPropertyName("sanitizedText")]
    public required string SanitizedText { get; init; }

    [JsonPropertyName("originalLength")]
    public required int OriginalLength { get; init; }

    [JsonPropertyName("documentRef")]
    public required string DocumentRef { get; init; }

    [JsonPropertyName("chunks")]
    public JsonElement Chunks { get; init; }
}
