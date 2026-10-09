using System.Text.Json;
using System.Text.Json.Nodes;
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
///   "annotationText": "[citation could not be verified]",  // optional — defaults to D-47 string
///   "sources": [ { "from": "assessments", "jsonPath": "items" } ]  // optional — further source-chunk upstreams
/// }
/// </code>
/// <para>
/// <b>Citation and chunk shapes (task 135).</b> A citation is an <see cref="EvidenceRef"/>
/// (<c>refType</c>/<c>ref</c>/<c>quote</c>) or the synthesis-prompt shape (<c>type</c>/<c>id</c>/<c>excerpt</c>,
/// e.g. matter-health-single's <c>citations[]</c>); a field of the first form wins. A source chunk is a
/// <see cref="ChunkRef"/> (<c>chunkId</c>/<c>text</c>), a string, or any other object — then its id is
/// <c>chunkId</c>, <c>id</c> or the first string property whose name ends in "id", and its text is every string value
/// it holds (so a KPI assessment row's notes and an index row's <c>valueJson</c> are verifiable). <c>sources</c> adds
/// upstreams to <c>sourceChunksFrom</c>; at least one of the two is required.
/// </para>
/// <para>
/// The structured output contains the verification verdict per citation. Downstream nodes
/// (e.g., <c>ReturnInsightArtifactNode</c>, D-P12) consume the results to either strip
/// failed citations or annotate them inline before emission. It also carries
/// <c>verifiedEvidence</c> (the passing citations as <see cref="EvidenceRef"/>s) and <c>groundedOutput</c> (the
/// citations upstream's output with its citation array cut down to the passing citations), so a terminal node can
/// emit only what was verified.
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

    // R7 task 085 / FR-23 — typed config schema for Playbook Builder canvas.
    // Derived from GroundingVerifyConfig: citationsFrom (required), sourceChunksFrom (required),
    // citationsJsonPath (default 'evidence'), sourceChunksJsonPath (default 'chunks'),
    // annotationText (default '[citation could not be verified]').
    private static readonly ExecutorConfigSchema ConfigSchemaInstance = new(
        ExecutorTypeName: nameof(ExecutorType.GroundingVerify),
        ExecutorTypeValue: (int)ExecutorType.GroundingVerify,
        Description: "Zero-LLM citation verification — checks quoted evidence from prior AI nodes against source chunks. Annotates failures per D-47 / LAVERN ADR 10.6.",
        Fields: new ConfigSchemaField[]
        {
            new(
                Name: "citationsFrom",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "OutputVariable of the prior node producing citations (EvidenceRef[]). Required.",
                Default: null),
            new(
                Name: "sourceChunksFrom",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "OutputVariable of the prior node producing source chunks (ChunkRef[]). Required unless 'sources' is set.",
                Default: null),
            new(
                Name: "citationsJsonPath",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "JSON property name on the citations upstream that holds the EvidenceRef[] array. Defaults to 'evidence'.",
                Default: "evidence"),
            new(
                Name: "sourceChunksJsonPath",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "JSON property name on the source-chunks upstream that holds the ChunkRef[] array. Defaults to 'chunks'.",
                Default: "chunks"),
            new(
                Name: "annotationText",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "Annotation appended to failed citations. Defaults to '[citation could not be verified]' per D-47.",
                Default: "[citation could not be verified]")
        });

    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() => ConfigSchemaInstance;

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var config = ParseConfig(context.Node.ConfigJson);
        if (config is null)
            return NodeValidationResult.Failure("GroundingVerify node requires ConfigJson with citationsFrom + sourceChunksFrom.");

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(config.CitationsFrom))
            errors.Add("ConfigJson.citationsFrom is required (the upstream output variable producing citations).");
        var hasSources = config.Sources is { Count: > 0 };
        if (string.IsNullOrWhiteSpace(config.SourceChunksFrom) && !hasSources)
            errors.Add("ConfigJson.sourceChunksFrom (or a non-empty 'sources' list) is required (the upstream output variable(s) producing source chunks).");
        if (hasSources && config.Sources!.Any(src => string.IsNullOrWhiteSpace(src.From)))
            errors.Add("Every ConfigJson.sources entry requires 'from' (an upstream output variable).");

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
            var parsedCitations = ExtractCitations(context, config);
            var citations = parsedCitations.Select(c => c.Ref).ToList();
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
            var verifiedEvidence = new List<EvidenceRef>(results.Count);
            var passedOriginals = new List<JsonElement>(results.Count);
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

                if (!isFailure)
                {
                    verifiedEvidence.Add(r.Citation);
                    var original = parsedCitations.FirstOrDefault(c => ReferenceEquals(c.Ref, r.Citation));
                    if (original.Ref is not null)
                        passedOriginals.Add(original.Original);
                }

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
                AnnotatedCitations = annotated,
                VerifiedEvidence = verifiedEvidence,
                GroundedOutput = BuildGroundedOutput(context, config, passedOriginals)
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

    private static IReadOnlyList<(EvidenceRef Ref, JsonElement Original)> ExtractCitations(
        NodeExecutionContext context, GroundingVerifyConfig config)
    {
        var upstream = context.GetPreviousOutput(config.CitationsFrom!)
            ?? throw new GroundingVerifyConfigException(
                $"No previous output found for variable '{config.CitationsFrom}'.");

        if (!upstream.Success)
            throw new GroundingVerifyConfigException(
                $"Upstream node '{config.CitationsFrom}' failed; cannot verify citations.");

        if (upstream.StructuredData is null)
            return Array.Empty<(EvidenceRef, JsonElement)>();

        var path = CitationsPath(config);
        if (!TryGetPropertyIgnoreCase(upstream.StructuredData.Value, path, out var citationsElement))
            return Array.Empty<(EvidenceRef, JsonElement)>();

        if (citationsElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<(EvidenceRef, JsonElement)>();

        var list = new List<(EvidenceRef, JsonElement)>();
        foreach (var item in citationsElement.EnumerateArray())
        {
            list.Add((ToEvidenceRef(item, config, path), item.Clone()));
        }

        return list;
    }

    /// <summary>
    /// Maps one citation to an <see cref="EvidenceRef"/>: <c>refType</c>/<c>ref</c>/<c>quote</c>, or the
    /// synthesis-prompt fields <c>type</c>/<c>id</c> (or <c>chunkId</c>)/<c>excerpt</c>. A citation without a type or
    /// a reference is a configuration error, as before.
    /// </summary>
    private static EvidenceRef ToEvidenceRef(JsonElement item, GroundingVerifyConfig config, string path)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw new GroundingVerifyConfigException(
                $"Could not deserialize citations from '{config.CitationsFrom}.{path}': a citation is not an object.");

        var refType = StringProperty(item, "refType") ?? StringProperty(item, "type");
        var reference = StringProperty(item, "ref") ?? StringProperty(item, "id") ?? StringProperty(item, "chunkId");
        if (string.IsNullOrWhiteSpace(refType) || string.IsNullOrWhiteSpace(reference))
            throw new GroundingVerifyConfigException(
                $"Could not deserialize citations from '{config.CitationsFrom}.{path}': a citation needs refType (or type) and ref (or id).");

        return new EvidenceRef
        {
            RefType = refType!,
            Ref = reference!,
            Quote = StringProperty(item, "quote") ?? StringProperty(item, "excerpt")
        };
    }

    private static IReadOnlyList<ChunkRef> ExtractSourceChunks(NodeExecutionContext context, GroundingVerifyConfig config)
    {
        var chunks = new List<ChunkRef>();

        if (!string.IsNullOrWhiteSpace(config.SourceChunksFrom))
        {
            var path = string.IsNullOrWhiteSpace(config.SourceChunksJsonPath) ? "chunks" : config.SourceChunksJsonPath!;
            chunks.AddRange(ExtractChunksFrom(context, config.SourceChunksFrom!, path));
        }

        foreach (var source in config.Sources ?? new List<GroundingVerifySource>())
        {
            var path = string.IsNullOrWhiteSpace(source.JsonPath) ? "chunks" : source.JsonPath!;
            chunks.AddRange(ExtractChunksFrom(context, source.From!, path));
        }

        return chunks;
    }

    private static IReadOnlyList<ChunkRef> ExtractChunksFrom(NodeExecutionContext context, string from, string path)
    {
        var upstream = context.GetPreviousOutput(from)
            ?? throw new GroundingVerifyConfigException(
                $"No previous output found for variable '{from}'.");

        if (!upstream.Success)
            throw new GroundingVerifyConfigException(
                $"Upstream node '{from}' failed; cannot verify against missing source chunks.");

        if (upstream.StructuredData is null)
            return Array.Empty<ChunkRef>();

        if (!TryGetPropertyIgnoreCase(upstream.StructuredData.Value, path, out var chunksElement))
            return Array.Empty<ChunkRef>();

        if (chunksElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<ChunkRef>();

        var list = new List<ChunkRef>();
        var index = 0;
        foreach (var item in chunksElement.EnumerateArray())
        {
            var fallbackId = $"{from}[{index++}]";
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    list.Add(new ChunkRef(fallbackId, item.GetString() ?? string.Empty));
                    break;
                case JsonValueKind.Object:
                    var text = StringProperty(item, "text");
                    var chunkId = StringProperty(item, "chunkId") ?? StringProperty(item, "id") ?? FirstIdProperty(item) ?? fallbackId;
                    list.Add(new ChunkRef(chunkId, text ?? string.Join("\n", StringValues(item))));
                    break;
            }
        }

        return list;
    }

    /// <summary>
    /// The citations upstream's output with its citation array replaced by the citations that passed, or null when
    /// that output is not a JSON object.
    /// </summary>
    private static JsonElement? BuildGroundedOutput(
        NodeExecutionContext context, GroundingVerifyConfig config, IReadOnlyList<JsonElement> passedOriginals)
    {
        var upstream = context.GetPreviousOutput(config.CitationsFrom!);
        if (upstream?.StructuredData is not { ValueKind: JsonValueKind.Object } data)
            return null;

        var node = JsonNode.Parse(data.GetRawText())!.AsObject();
        var path = CitationsPath(config);
        var existing = node.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, path, StringComparison.OrdinalIgnoreCase));
        node[existing ?? path] = new JsonArray(passedOriginals.Select(o => JsonNode.Parse(o.GetRawText())).ToArray());
        return JsonSerializer.SerializeToElement(node);
    }

    private static string CitationsPath(GroundingVerifyConfig config) =>
        string.IsNullOrWhiteSpace(config.CitationsJsonPath) ? "evidence" : config.CitationsJsonPath!;

    /// <summary>JSON paths are matched case-insensitively: executor outputs serialize PascalCase (Artifacts), LLM
    /// outputs camelCase (citations), and authors write either.</summary>
    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out value))
                return true;

            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? StringProperty(JsonElement item, string name)
    {
        foreach (var prop in item.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                return prop.Value.GetString();
        }

        return null;
    }

    private static string? FirstIdProperty(JsonElement item)
    {
        foreach (var prop in item.EnumerateObject())
        {
            if (prop.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                return prop.Value.GetString();
        }

        return null;
    }

    private static IEnumerable<string> StringValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value!;
                break;
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                    foreach (var nested in StringValues(prop.Value))
                        yield return nested;
                break;
            case JsonValueKind.Array:
                foreach (var entry in element.EnumerateArray())
                    foreach (var nested in StringValues(entry))
                        yield return nested;
                break;
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

    /// <summary>Further source-chunk upstreams, read in addition to <see cref="SourceChunksFrom"/>.</summary>
    [JsonPropertyName("sources")]
    public List<GroundingVerifySource>? Sources { get; init; }
}

/// <summary>One further source-chunk upstream of <see cref="GroundingVerifyNode"/>.</summary>
internal sealed record GroundingVerifySource
{
    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("jsonPath")]
    public string? JsonPath { get; init; }
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

    /// <summary>The citations that passed (not NotFound, not InvalidInput), as evidence references.</summary>
    public IReadOnlyList<EvidenceRef> VerifiedEvidence { get; init; } = Array.Empty<EvidenceRef>();

    /// <summary>
    /// The citations upstream's output with its citation array cut down to the citations that passed; null when
    /// that output is not a JSON object.
    /// </summary>
    public JsonElement? GroundedOutput { get; init; }
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
