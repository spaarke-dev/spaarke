using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.CitationVerification;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor that wraps <see cref="IGroundingVerifier"/> for use in node-based playbooks.
/// Reads citations from a prior node's output, verifies them against source chunks, and
/// annotates failures per D-47 / LAVERN ADR 10.6.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliverable</b>: part of the D-P9 verifier + D-P12 node-executor set per SPEC §3.1.
/// </para>
/// <para>
/// <b>Config schema</b> (read from <c>Node.ConfigJson</c>):
/// </para>
/// <code>
/// {
///   "citationsFrom": "extractOutcomes",          // required — output variable of the prior node
///   "sourceChunksFrom": "loadDocument",          // required — output variable carrying source chunks
///   "citationsJsonPath": "evidence",             // optional — defaults to "evidence"; JSON property holding EvidenceRef[]
///   "sourceChunksJsonPath": "chunks",            // optional — defaults to "chunks"; JSON property holding ChunkRef[]
///   "annotationText": "[citation could not be verified]"   // optional — defaults to D-47 string
/// }
/// </code>
/// <para>
/// The structured output contains the verification verdict per citation. Downstream nodes
/// (e.g., <c>ReturnInsightArtifactNode</c>, D-P12) consume the results to either strip
/// failed citations or annotate them inline before emission.
/// </para>
/// <para>
/// <b>Zone A</b> per SPEC §3.5 — lives under <c>Services/Ai/Nodes/</c> alongside the other
/// platform node executors and freely imports <see cref="IGroundingVerifier"/>.
/// </para>
/// </remarks>
public sealed class GroundingVerifyNode : INodeExecutor
{
    /// <summary>Default annotation text per D-47 / LAVERN ADR 10.6.</summary>
    public const string DefaultAnnotation = "[citation could not be verified]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // EvidenceRef uses [JsonPropertyName] — case-insensitive read + the attributes handle both casings.
    };

    private readonly IGroundingVerifier _verifier;
    private readonly ILogger<GroundingVerifyNode> _logger;

    public GroundingVerifyNode(IGroundingVerifier verifier, ILogger<GroundingVerifyNode> logger)
    {
        _verifier = verifier;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.GroundingVerify
    };

    // R7 task 032 / FR-16 — placeholder schema (no maker-editable fields surfaced yet).
    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() =>
        ExecutorConfigSchema.Empty(
            ExecutorType.GroundingVerify,
            "Zero-LLM citation verification — checks quoted evidence from prior AI nodes against source chunks (D-P9 / D-47 / LAVERN 10.6).");

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var config = ParseConfig(context.Node.ConfigJson);
        if (config is null)
            return NodeValidationResult.Failure("GroundingVerify node requires ConfigJson with citationsFrom + sourceChunksFrom.");

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(config.CitationsFrom))
            errors.Add("ConfigJson.citationsFrom is required (the upstream output variable producing citations).");
        if (string.IsNullOrWhiteSpace(config.SourceChunksFrom))
            errors.Add("ConfigJson.sourceChunksFrom is required (the upstream output variable producing source chunks).");

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<NodeOutput> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
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
            var citations = ExtractCitations(context, config);
            var chunks = ExtractSourceChunks(context, config);

            _logger.LogDebug(
                "GroundingVerifyNode {NodeId}: verifying {CitationCount} citation(s) against {ChunkCount} source chunk(s)",
                context.Node.Id,
                citations.Count,
                chunks.Count);

            var results = await _verifier.VerifyAsync(citations, chunks, cancellationToken).ConfigureAwait(false);

            // Build the structured output: per-citation verdict + annotated citation list.
            var annotation = string.IsNullOrWhiteSpace(config.AnnotationText)
                ? DefaultAnnotation
                : config.AnnotationText!;

            var annotated = new List<AnnotatedCitation>(results.Count);
            var verifiedCount = 0;
            var approximateCount = 0;
            var notFoundCount = 0;
            var noQuoteCount = 0;
            var invalidInputCount = 0;

            foreach (var r in results)
            {
                var isFailure = r.Verdict is VerificationVerdict.NotFound or VerificationVerdict.InvalidInput;
                annotated.Add(new AnnotatedCitation
                {
                    Citation = r.Citation,
                    Verdict = r.Verdict.ToString(),
                    Reason = r.Reason,
                    MatchedChunkId = r.MatchedChunkId,
                    Annotation = isFailure ? annotation : null
                });

                switch (r.Verdict)
                {
                    case VerificationVerdict.Verified: verifiedCount++; break;
                    case VerificationVerdict.VerifiedApproximate: approximateCount++; break;
                    case VerificationVerdict.NotFound: notFoundCount++; break;
                    case VerificationVerdict.NoQuote: noQuoteCount++; break;
                    case VerificationVerdict.InvalidInput: invalidInputCount++; break;
                }
            }

            var output = new GroundingVerifyOutput
            {
                TotalCitations = results.Count,
                VerifiedCount = verifiedCount,
                ApproximateCount = approximateCount,
                NotFoundCount = notFoundCount,
                NoQuoteCount = noQuoteCount,
                InvalidInputCount = invalidInputCount,
                AllVerified = (notFoundCount + invalidInputCount) == 0,
                AnnotatedCitations = annotated
            };

            var warnings = new List<string>();
            if (notFoundCount > 0)
                warnings.Add($"{notFoundCount} citation(s) could not be verified against source.");
            if (invalidInputCount > 0)
                warnings.Add($"{invalidInputCount} citation(s) hit DoS cap on source chunk size.");

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                output,
                textContent: $"Verified {verifiedCount + approximateCount}/{results.Count} citations ({notFoundCount} not found, {invalidInputCount} invalid input).",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow),
                warnings: warnings.Count > 0 ? warnings : null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GroundingVerifyConfigException ex)
        {
            _logger.LogWarning(ex, "GroundingVerifyNode {NodeId}: configuration extraction failed", context.Node.Id);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                ex.Message,
                NodeErrorCodes.InvalidConfiguration,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GroundingVerifyNode {NodeId} failed: {Message}", context.Node.Id, ex.Message);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Citation verification failed: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    private static GroundingVerifyConfig? ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GroundingVerifyConfig>(configJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<EvidenceRef> ExtractCitations(NodeExecutionContext context, GroundingVerifyConfig config)
    {
        var upstream = context.GetPreviousOutput(config.CitationsFrom!)
            ?? throw new GroundingVerifyConfigException(
                $"No previous output found for variable '{config.CitationsFrom}'.");

        if (!upstream.Success)
            throw new GroundingVerifyConfigException(
                $"Upstream node '{config.CitationsFrom}' failed; cannot verify citations.");

        if (upstream.StructuredData is null)
            return Array.Empty<EvidenceRef>();

        var path = string.IsNullOrWhiteSpace(config.CitationsJsonPath) ? "evidence" : config.CitationsJsonPath!;
        if (!upstream.StructuredData.Value.TryGetProperty(path, out var citationsElement))
            return Array.Empty<EvidenceRef>();

        if (citationsElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<EvidenceRef>();

        try
        {
            var list = JsonSerializer.Deserialize<List<EvidenceRef>>(citationsElement.GetRawText(), JsonOptions);
            return list ?? new List<EvidenceRef>();
        }
        catch (JsonException ex)
        {
            throw new GroundingVerifyConfigException(
                $"Could not deserialize citations from '{config.CitationsFrom}.{path}': {ex.Message}");
        }
    }

    private static IReadOnlyList<ChunkRef> ExtractSourceChunks(NodeExecutionContext context, GroundingVerifyConfig config)
    {
        var upstream = context.GetPreviousOutput(config.SourceChunksFrom!)
            ?? throw new GroundingVerifyConfigException(
                $"No previous output found for variable '{config.SourceChunksFrom}'.");

        if (!upstream.Success)
            throw new GroundingVerifyConfigException(
                $"Upstream node '{config.SourceChunksFrom}' failed; cannot verify against missing source chunks.");

        if (upstream.StructuredData is null)
            return Array.Empty<ChunkRef>();

        var path = string.IsNullOrWhiteSpace(config.SourceChunksJsonPath) ? "chunks" : config.SourceChunksJsonPath!;
        if (!upstream.StructuredData.Value.TryGetProperty(path, out var chunksElement))
            return Array.Empty<ChunkRef>();

        if (chunksElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<ChunkRef>();

        try
        {
            var list = JsonSerializer.Deserialize<List<ChunkRef>>(chunksElement.GetRawText(), JsonOptions);
            return list ?? new List<ChunkRef>();
        }
        catch (JsonException ex)
        {
            throw new GroundingVerifyConfigException(
                $"Could not deserialize source chunks from '{config.SourceChunksFrom}.{path}': {ex.Message}");
        }
    }
}

/// <summary>
/// Config schema for <see cref="GroundingVerifyNode"/>.
/// </summary>
internal sealed record GroundingVerifyConfig
{
    [JsonPropertyName("citationsFrom")]
    public string? CitationsFrom { get; init; }

    [JsonPropertyName("sourceChunksFrom")]
    public string? SourceChunksFrom { get; init; }

    [JsonPropertyName("citationsJsonPath")]
    public string? CitationsJsonPath { get; init; }

    [JsonPropertyName("sourceChunksJsonPath")]
    public string? SourceChunksJsonPath { get; init; }

    [JsonPropertyName("annotationText")]
    public string? AnnotationText { get; init; }
}

/// <summary>
/// Structured output of <see cref="GroundingVerifyNode"/>.
/// </summary>
public sealed record GroundingVerifyOutput
{
    public int TotalCitations { get; init; }
    public int VerifiedCount { get; init; }
    public int ApproximateCount { get; init; }
    public int NotFoundCount { get; init; }
    public int NoQuoteCount { get; init; }
    public int InvalidInputCount { get; init; }

    /// <summary>True if every citation was verified (exact / approximate / no-quote). False if any NotFound or InvalidInput.</summary>
    public bool AllVerified { get; init; }

    public IReadOnlyList<AnnotatedCitation> AnnotatedCitations { get; init; } = Array.Empty<AnnotatedCitation>();
}

/// <summary>
/// A citation with its verification verdict and the annotation that consumers (e.g., the
/// <c>ReturnInsightArtifactNode</c>) should surface for failed citations.
/// </summary>
public sealed record AnnotatedCitation
{
    public required EvidenceRef Citation { get; init; }
    public required string Verdict { get; init; }
    public required string Reason { get; init; }
    public string? MatchedChunkId { get; init; }

    /// <summary>The annotation text for failed citations (null for verified ones).</summary>
    public string? Annotation { get; init; }
}

/// <summary>
/// Internal exception used to flag config-extraction problems distinct from infra errors.
/// Mapped to <see cref="NodeErrorCodes.InvalidConfiguration"/> by the executor.
/// </summary>
internal sealed class GroundingVerifyConfigException : Exception
{
    public GroundingVerifyConfigException(string message) : base(message) { }
}
