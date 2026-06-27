using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Pure-deterministic clause-comparison handler (R6 Pillar 2, FR-16).
/// </summary>
/// <remarks>
/// <para>
/// Computes a structural text-diff between two clause texts (<c>clauseA</c> + <c>clauseB</c>)
/// using a longest-common-subsequence (LCS) algorithm at a configurable granularity
/// (word / sentence / paragraph). Emits the diff as ordered segments
/// (added / removed / unchanged / modified) plus a normalized similarity score [0.0, 1.0].
/// </para>
/// <para>
/// <strong>Zero LLM dependencies</strong> — no <c>IOpenAiClient</c>, no Azure OpenAI,
/// no <c>ToolHandlerModel</c> resolution, no token budgeting. Identical inputs + identical
/// configuration produce identical output (litigation-defensibility invariant per FR-16).
/// </para>
/// <para>
/// <strong>Structural awareness</strong> means: cosmetic whitespace differences are
/// ignored by default; sentence boundaries are preserved in the segment output; the caller
/// can configure paragraph-level vs sentence-level vs word-level granularity. Case-sensitivity
/// and punctuation-sensitivity are also configurable.
/// </para>
/// <para>
/// <strong>Algorithm</strong>: classic Hunt–McIlroy LCS via dynamic programming on
/// tokens (granularity-driven tokenizer). For two clause texts at typical legal-document
/// length (≤ a few KB), the O(n×m) DP table fits comfortably in memory. Input length is
/// capped at <see cref="MaxClauseLength"/> characters per side to bound worst-case work.
/// </para>
/// <para>
/// <strong>Telemetry (ADR-015)</strong>: logs handler name + execution outcome + timestamp
/// + analysis/tool IDs ONLY. NEVER logs clause text content (the clauses are user input
/// and may contain privileged material).
/// </para>
/// <para>
/// <strong>Registration</strong>: auto-discovered via <c>ToolFrameworkExtensions</c> per
/// ADR-010. A corresponding <c>sprk_analysistool</c> row with
/// <c>sprk_handlerclass = "ClauseComparisonHandler"</c> must exist in Dataverse for runtime
/// invocation.
/// </para>
/// </remarks>
public sealed class ClauseComparisonHandler : IToolHandler
{
    private const string HandlerIdValue = "ClauseComparisonHandler";

    /// <summary>
    /// Hard cap on per-clause input length. Bounds the LCS DP table at
    /// O(MaxClauseLength²) tokens worst case (word-granularity, average token = 1 char).
    /// </summary>
    private const int MaxClauseLength = 50_000;

    private readonly ILogger<ClauseComparisonHandler> _logger;

    /// <summary>
    /// JSON Schema (Draft 07) for the handler's configuration payload. Surfaced via
    /// <see cref="ToolHandlerMetadata.ConfigurationSchema"/> so admins / scope-editors see
    /// the supported config shape.
    /// </summary>
    private static readonly object ConfigSchema = new
    {
        schema = "https://json-schema.org/draft-07/schema#",
        title = "Clause Comparison Handler Configuration",
        type = "object",
        properties = new
        {
            granularity = new
            {
                type = "string",
                description = "Diff granularity. Sentence and paragraph preserve structural boundaries; word is the finest.",
                @enum = new[] { "word", "sentence", "paragraph" },
                @default = "sentence"
            },
            caseSensitive = new
            {
                type = "boolean",
                description = "When false (default), 'Agreement' and 'agreement' compare equal.",
                @default = false
            },
            punctuationSensitive = new
            {
                type = "boolean",
                description = "When false, '.', ',', ';' etc. are stripped before token comparison.",
                @default = true
            },
            normalizeWhitespace = new
            {
                type = "boolean",
                description = "When true (default), runs of whitespace collapse to a single space before tokenization.",
                @default = true
            }
        },
        required = Array.Empty<string>()
    };

    public ClauseComparisonHandler(ILogger<ClauseComparisonHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Clause Comparison Handler",
        Description: "Pure-deterministic structural diff between two clause texts. Emits added/removed/unchanged/modified segments plus a similarity score [0.0, 1.0]. No LLM call.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                Name: "clauseA",
                Description: "Original clause text (the baseline). Required.",
                Type: ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                Name: "clauseB",
                Description: "Proposed/revised clause text (the comparand). Required.",
                Type: ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                Name: "granularity",
                Description: "Diff granularity: 'word', 'sentence', or 'paragraph'. Default 'sentence'.",
                Type: ToolParameterType.String,
                Required: false,
                DefaultValue: "sentence"),
            new ToolParameterDefinition(
                Name: "caseSensitive",
                Description: "Whether comparison is case-sensitive. Default false.",
                Type: ToolParameterType.Boolean,
                Required: false,
                DefaultValue: false),
            new ToolParameterDefinition(
                Name: "punctuationSensitive",
                Description: "Whether punctuation is considered. Default true.",
                Type: ToolParameterType.Boolean,
                Required: false,
                DefaultValue: true),
            new ToolParameterDefinition(
                Name: "normalizeWhitespace",
                Description: "Whether whitespace runs collapse to a single space. Default true.",
                Type: ToolParameterType.Boolean,
                Required: false,
                DefaultValue: true)
        },
        ConfigurationSchema: ConfigSchema);

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.ClauseComparison };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required (ADR-014 cache-key invariant).");

        // ClauseComparisonHandler reads clauseA + clauseB from tool.Configuration JSON,
        // NOT from context.Document.ExtractedText. Document context is optional here —
        // a comparison may be invoked with the two texts inline (e.g., from a playbook
        // template-parameter substitution) without a backing document.
        if (string.IsNullOrWhiteSpace(tool.Configuration))
        {
            errors.Add("Tool configuration is required: must provide clauseA + clauseB.");
        }
        else
        {
            try
            {
                var config = ParseConfiguration(tool.Configuration);

                if (string.IsNullOrEmpty(config.ClauseA))
                    errors.Add("clauseA is required (the baseline clause text).");
                if (string.IsNullOrEmpty(config.ClauseB))
                    errors.Add("clauseB is required (the comparand clause text).");

                if (config.ClauseA?.Length > MaxClauseLength)
                    errors.Add($"clauseA exceeds maximum length of {MaxClauseLength} characters.");
                if (config.ClauseB?.Length > MaxClauseLength)
                    errors.Add($"clauseB exceeds maximum length of {MaxClauseLength} characters.");

                if (!string.IsNullOrEmpty(config.Granularity) &&
                    !SupportedGranularities.Contains(config.Granularity, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Unsupported granularity '{config.Granularity}'. Supported: {string.Join(", ", SupportedGranularities)}.");
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"Invalid configuration JSON: {ex.Message}");
            }
        }

        return errors.Count == 0
            ? ToolValidationResult.Success()
            : ToolValidationResult.Failure(errors);
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            // ADR-015: log handler name + IDs + outcome ONLY. NEVER clause text.
            _logger.LogInformation(
                "Starting clause comparison for analysis {AnalysisId}, tool {ToolId} ({ToolName})",
                context.AnalysisId, tool.Id, tool.Name);

            cancellationToken.ThrowIfCancellationRequested();

            var config = ParseConfiguration(tool.Configuration!);
            var clauseA = config.ClauseA ?? string.Empty;
            var clauseB = config.ClauseB ?? string.Empty;
            var granularity = NormalizeGranularity(config.Granularity);

            // Tokenize both sides per granularity + normalization config.
            var tokensA = Tokenize(clauseA, granularity, config);
            var tokensB = Tokenize(clauseB, granularity, config);

            cancellationToken.ThrowIfCancellationRequested();

            // Compute LCS-based diff.
            var segments = ComputeDiffSegments(tokensA, tokensB);
            var similarity = ComputeSimilarity(segments, tokensA.Count, tokensB.Count);

            stopwatch.Stop();

            var resultData = new
            {
                similarity,
                granularity,
                segments = segments.Select(s => new
                {
                    type = s.Type.ToString().ToLowerInvariant(),
                    text = s.Text,
                    indexA = s.IndexA,
                    indexB = s.IndexB
                }).ToArray(),
                structural = new
                {
                    granularity,
                    totalSegmentsA = tokensA.Count,
                    totalSegmentsB = tokensB.Count,
                    addedCount = segments.Count(s => s.Type == DiffSegmentType.Added),
                    removedCount = segments.Count(s => s.Type == DiffSegmentType.Removed),
                    unchangedCount = segments.Count(s => s.Type == DiffSegmentType.Unchanged),
                    modifiedCount = segments.Count(s => s.Type == DiffSegmentType.Modified)
                }
            };

            var executionMetadata = new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ModelCalls = 0,    // pure deterministic — no LLM call
                ModelName = null
            };

            // Bucket similarity for telemetry without leaking clause content.
            var similarityBucket = similarity switch
            {
                >= 0.85 => "high",
                >= 0.50 => "medium",
                _ => "low"
            };

            _logger.LogInformation(
                "Clause comparison complete for analysis {AnalysisId}: granularity={Granularity}, similarityBucket={SimilarityBucket}, segments={SegmentCount}, duration={DurationMs}ms",
                context.AnalysisId, granularity, similarityBucket, segments.Count, stopwatch.ElapsedMilliseconds);

            return Task.FromResult(ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                resultData,
                summary: $"Clause similarity: {similarity:F2} ({similarityBucket}) at {granularity} granularity.",
                confidence: 1.0,    // deterministic algorithm — confidence is always full
                execution: executionMetadata));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Clause comparison cancelled for analysis {AnalysisId}", context.AnalysisId);
            return Task.FromResult(ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Clause comparison was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                }));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Clause comparison failed for analysis {AnalysisId}", context.AnalysisId);
            return Task.FromResult(ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Clause comparison failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                }));
        }
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // Configuration parsing
    // ────────────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> SupportedGranularities = new(StringComparer.OrdinalIgnoreCase)
    {
        "word", "sentence", "paragraph"
    };

    private static ClauseComparisonConfig ParseConfiguration(string configurationJson)
    {
        var config = JsonSerializer.Deserialize<ClauseComparisonConfig>(
            configurationJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return config ?? new ClauseComparisonConfig();
    }

    private static string NormalizeGranularity(string? granularity)
    {
        if (string.IsNullOrWhiteSpace(granularity))
            return "sentence";

        return granularity.ToLowerInvariant() switch
        {
            "word" or "sentence" or "paragraph" => granularity.ToLowerInvariant(),
            _ => "sentence"
        };
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // Tokenization
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Split + normalize per granularity + config flags. Returns the normalized token list
    /// used for comparison; segment.Text in the output preserves the ORIGINAL text for the
    /// matched index.
    /// </summary>
    private static IReadOnlyList<Token> Tokenize(string text, string granularity, ClauseComparisonConfig config)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<Token>();

        // Split into raw segments per granularity. Paragraph-level splitting MUST happen
        // BEFORE whitespace normalization — the blank-line boundary that defines a paragraph
        // is destroyed by collapsing whitespace runs. Sentence- + word-granularity tokenization
        // is whitespace-tolerant either way, so we normalize within each raw segment.
        var paragraphSplitFirst = granularity == "paragraph";
        var normalizeWhitespace = config.NormalizeWhitespace ?? true;

        IReadOnlyList<string> rawSegments;
        if (paragraphSplitFirst)
        {
            // Split paragraphs from the ORIGINAL (un-normalized) text; then collapse internal
            // whitespace within each paragraph if normalization is enabled.
            rawSegments = SplitParagraphs(text);
            if (normalizeWhitespace)
            {
                var collapsed = new List<string>(rawSegments.Count);
                foreach (var p in rawSegments)
                    collapsed.Add(WhitespaceRegex.Replace(p, " ").Trim());
                rawSegments = collapsed;
            }
        }
        else
        {
            var working = text;
            if (normalizeWhitespace)
                working = WhitespaceRegex.Replace(working, " ").Trim();

            rawSegments = granularity switch
            {
                "sentence" => SplitSentences(working),
                _ => SplitWords(working)
            };
        }

        // Build Token records: original text preserved + normalized form for comparison.
        var caseSensitive = config.CaseSensitive ?? false;
        var punctuationSensitive = config.PunctuationSensitive ?? true;

        var tokens = new List<Token>(rawSegments.Count);
        foreach (var raw in rawSegments)
        {
            var normalized = raw;
            if (!punctuationSensitive)
                normalized = PunctuationRegex.Replace(normalized, string.Empty);
            if (!caseSensitive)
                normalized = normalized.ToLowerInvariant();
            normalized = normalized.Trim();

            // Skip wholly-empty post-normalization tokens (handles "  " sequences after stripping).
            if (normalized.Length == 0)
                continue;

            tokens.Add(new Token(raw, normalized));
        }

        return tokens;
    }

    private static IReadOnlyList<string> SplitParagraphs(string text)
    {
        // Paragraph = consecutive non-empty lines separated by blank lines. After whitespace
        // normalization collapses everything to single spaces, blank-line boundaries are lost,
        // so we re-split on the original text's newline boundaries first.
        var byBlank = ParagraphRegex.Split(text);
        var result = new List<string>(byBlank.Length);
        foreach (var p in byBlank)
        {
            var trimmed = p.Trim();
            if (trimmed.Length > 0)
                result.Add(trimmed);
        }
        return result;
    }

    private static IReadOnlyList<string> SplitSentences(string text)
    {
        // Sentence = run of non-terminal characters ending in . ! ? (or end-of-input).
        var matches = SentenceRegex.Matches(text);
        if (matches.Count == 0)
            return text.Trim().Length > 0 ? new[] { text.Trim() } : Array.Empty<string>();

        var result = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            var trimmed = m.Value.Trim();
            if (trimmed.Length > 0)
                result.Add(trimmed);
        }
        return result;
    }

    private static IReadOnlyList<string> SplitWords(string text)
    {
        var matches = WordRegex.Matches(text);
        if (matches.Count == 0)
            return Array.Empty<string>();

        var result = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            if (m.Value.Length > 0)
                result.Add(m.Value);
        }
        return result;
    }

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex PunctuationRegex = new(@"[\p{P}\p{S}]", RegexOptions.Compiled);
    private static readonly Regex ParagraphRegex = new(@"\r?\n\s*\r?\n", RegexOptions.Compiled);
    private static readonly Regex SentenceRegex = new(@"[^.!?]+[.!?]+|[^.!?]+$", RegexOptions.Compiled);
    private static readonly Regex WordRegex = new(@"\S+", RegexOptions.Compiled);

    // ────────────────────────────────────────────────────────────────────────────────
    // LCS-based diff
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute ordered diff segments via LCS dynamic programming.
    /// </summary>
    /// <remarks>
    /// Classic O(n×m) LCS table backtrack. Produces three primitive segment types
    /// (Unchanged / Added / Removed). Adjacent Added/Removed pairs are then post-processed
    /// into Modified segments (a Modified represents a Removed-then-Added pair at the same
    /// position, capturing "this token replaced that one" semantics).
    /// </remarks>
    private static IReadOnlyList<DiffSegment> ComputeDiffSegments(
        IReadOnlyList<Token> tokensA,
        IReadOnlyList<Token> tokensB)
    {
        var n = tokensA.Count;
        var m = tokensB.Count;

        if (n == 0 && m == 0)
            return Array.Empty<DiffSegment>();

        if (n == 0)
        {
            var segs = new List<DiffSegment>(m);
            for (var j = 0; j < m; j++)
                segs.Add(new DiffSegment(DiffSegmentType.Added, tokensB[j].Original, null, j));
            return segs;
        }

        if (m == 0)
        {
            var segs = new List<DiffSegment>(n);
            for (var i = 0; i < n; i++)
                segs.Add(new DiffSegment(DiffSegmentType.Removed, tokensA[i].Original, i, null));
            return segs;
        }

        // LCS DP table.
        var lcs = new int[n + 1, m + 1];
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                if (tokensA[i - 1].Normalized == tokensB[j - 1].Normalized)
                    lcs[i, j] = lcs[i - 1, j - 1] + 1;
                else
                    lcs[i, j] = Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
            }
        }

        // Backtrack to produce ordered primitive segments.
        var primitives = new List<DiffSegment>();
        var pi = n;
        var pj = m;
        while (pi > 0 && pj > 0)
        {
            if (tokensA[pi - 1].Normalized == tokensB[pj - 1].Normalized)
            {
                primitives.Add(new DiffSegment(
                    DiffSegmentType.Unchanged,
                    tokensA[pi - 1].Original,
                    pi - 1,
                    pj - 1));
                pi--; pj--;
            }
            else if (lcs[pi - 1, pj] >= lcs[pi, pj - 1])
            {
                primitives.Add(new DiffSegment(
                    DiffSegmentType.Removed,
                    tokensA[pi - 1].Original,
                    pi - 1,
                    null));
                pi--;
            }
            else
            {
                primitives.Add(new DiffSegment(
                    DiffSegmentType.Added,
                    null,
                    null,
                    pj - 1)
                { Text = tokensB[pj - 1].Original });
                pj--;
            }
        }
        while (pi > 0)
        {
            primitives.Add(new DiffSegment(
                DiffSegmentType.Removed,
                tokensA[pi - 1].Original,
                pi - 1,
                null));
            pi--;
        }
        while (pj > 0)
        {
            primitives.Add(new DiffSegment(
                DiffSegmentType.Added,
                tokensB[pj - 1].Original,
                null,
                pj - 1));
            pj--;
        }

        primitives.Reverse();

        // Post-process: pair adjacent Removed → Added into Modified segments.
        var merged = new List<DiffSegment>(primitives.Count);
        for (var k = 0; k < primitives.Count; k++)
        {
            if (k + 1 < primitives.Count &&
                primitives[k].Type == DiffSegmentType.Removed &&
                primitives[k + 1].Type == DiffSegmentType.Added)
            {
                merged.Add(new DiffSegment(
                    DiffSegmentType.Modified,
                    $"{primitives[k].Text} → {primitives[k + 1].Text}",
                    primitives[k].IndexA,
                    primitives[k + 1].IndexB));
                k++;    // skip the paired Added
            }
            else
            {
                merged.Add(primitives[k]);
            }
        }

        return merged;
    }

    /// <summary>
    /// Similarity = 2 × |LCS| / (|A| + |B|). Equivalent to a Sørensen–Dice-style
    /// coefficient on token sets but preserves order via the LCS measure.
    /// </summary>
    private static double ComputeSimilarity(IReadOnlyList<DiffSegment> segments, int tokensACount, int tokensBCount)
    {
        if (tokensACount == 0 && tokensBCount == 0)
            return 1.0;
        if (tokensACount == 0 || tokensBCount == 0)
            return 0.0;

        // LCS length = unchanged segments + (modified segments count as "half match each side").
        // For similarity, we count unchanged only — modified tokens are NOT identical.
        var unchanged = segments.Count(s => s.Type == DiffSegmentType.Unchanged);
        return (2.0 * unchanged) / (tokensACount + tokensBCount);
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // Internal types
    // ────────────────────────────────────────────────────────────────────────────────

    private readonly record struct Token(string Original, string Normalized);

    private enum DiffSegmentType
    {
        Unchanged,
        Added,
        Removed,
        Modified
    }

    private sealed record DiffSegment(
        DiffSegmentType Type,
        string? Text,
        int? IndexA,
        int? IndexB);
}

/// <summary>
/// Configuration payload for <see cref="ClauseComparisonHandler"/>. Deserialized from
/// the <c>sprk_analysistool.sprk_configuration</c> field (or supplied inline by the
/// playbook node executor).
/// </summary>
internal sealed class ClauseComparisonConfig
{
    /// <summary>The original / baseline clause text.</summary>
    public string? ClauseA { get; set; }

    /// <summary>The proposed / revised clause text.</summary>
    public string? ClauseB { get; set; }

    /// <summary>Diff granularity: 'word', 'sentence' (default), or 'paragraph'.</summary>
    public string? Granularity { get; set; }

    /// <summary>Whether comparison is case-sensitive (default false).</summary>
    public bool? CaseSensitive { get; set; }

    /// <summary>Whether punctuation is considered (default true).</summary>
    public bool? PunctuationSensitive { get; set; }

    /// <summary>Whether to collapse whitespace runs (default true).</summary>
    public bool? NormalizeWhitespace { get; set; }
}
