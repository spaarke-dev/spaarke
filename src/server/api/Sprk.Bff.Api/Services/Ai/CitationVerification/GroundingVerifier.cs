using System.Text;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.CitationVerification;

/// <summary>
/// Default mechanical, zero-LLM implementation of <see cref="IGroundingVerifier"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Algorithm</b> (per D-47, LAVERN ADR 10.6):
/// </para>
/// <list type="number">
///   <item><b>Normalization</b> — collapse whitespace runs to a single space and lowercase both
///   quote and chunk text. Preserves all substantive characters; only adjusts surface noise
///   that an extractor's verbatim-quote step may legitimately fail to reproduce.</item>
///   <item><b>Exact substring</b> — try <c>chunk.Contains(quote)</c>. If hit, return
///   <see cref="VerificationVerdict.Verified"/>.</item>
///   <item><b>Sliding window</b> — for each chunk, slide a window of <see cref="WindowSize"/>
///   characters with step <see cref="WindowStep"/>. For each window, compute token overlap
///   ratio against the quote tokens. If any window's overlap meets <see cref="ApproximateMatchThreshold"/>,
///   return <see cref="VerificationVerdict.VerifiedApproximate"/>.</item>
///   <item><b>NotFound</b> — none of the above passes.</item>
/// </list>
/// <para>
/// <b>DoS protection</b>: chunks over <see cref="IGroundingVerifier.MaxSourceChunkLength"/>
/// characters cause citations checked against them to short-circuit to
/// <see cref="VerificationVerdict.InvalidInput"/>. Without the cap, a runaway 1MB chunk would
/// drive ~20,000 sliding-window comparisons per citation.
/// </para>
/// <para>
/// <b>Singleton-safe</b>: holds no per-call mutable state; the implementation is stateless
/// pure functions over inputs. Registered as a singleton in DI per ADR-010 §"singletons preferred
/// when stateless." Thread-safe by absence of state.
/// </para>
/// </remarks>
public sealed class GroundingVerifier : IGroundingVerifier
{
    /// <summary>Sliding-window length in characters. Long enough to span a typical sentence; short enough that one paragraph contains multiple windows.</summary>
    internal const int WindowSize = 200;

    /// <summary>Step (stride) between successive windows. 50% overlap with the next window prevents misses at window boundaries.</summary>
    internal const int WindowStep = 100;

    /// <summary>
    /// Token-overlap ratio required to accept a window as an approximate match.
    /// 0.70 = at least 70% of quote tokens must appear in the window. Tuned to accept
    /// light paraphrase (word-order tweaks, one-or-two-word substitutions for a short
    /// quote) and reject fabrication (no overlap at all).
    /// </summary>
    internal const double ApproximateMatchThreshold = 0.70;

    /// <summary>Minimum quote length (post-normalization) to attempt approximate matching. Below this we only do exact-substring checks (short quotes are too noisy for token-overlap).</summary>
    internal const int MinApproximateQuoteLength = 12;

    private static readonly char[] TokenSeparators = { ' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '‘', '’', '“', '”', '-', '/', '\\' };

    private readonly ILogger<GroundingVerifier> _logger;

    public GroundingVerifier(ILogger<GroundingVerifier> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VerificationResult>> VerifyAsync(
        IEnumerable<EvidenceRef> citations,
        IEnumerable<ChunkRef> sourceChunks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(citations);
        ArgumentNullException.ThrowIfNull(sourceChunks);

        // Materialize chunks once. We split into (valid, oversized) so we can return
        // InvalidInput per-citation cheaply without re-scanning the input list.
        var allChunks = sourceChunks.ToList();
        var validChunks = new List<NormalizedChunk>(allChunks.Count);
        var oversizedCount = 0;

        foreach (var chunk in allChunks)
        {
            if (chunk is null)
                continue;

            if (chunk.Text is null)
                continue;

            if (chunk.Text.Length > IGroundingVerifier.MaxSourceChunkLength)
            {
                oversizedCount++;
                _logger.LogWarning(
                    "GroundingVerifier: rejecting oversized source chunk {ChunkId} ({Length} chars > {Cap} cap)",
                    chunk.ChunkId,
                    chunk.Text.Length,
                    IGroundingVerifier.MaxSourceChunkLength);
                continue;
            }

            validChunks.Add(new NormalizedChunk(chunk.ChunkId, Normalize(chunk.Text)));
        }

        var results = new List<VerificationResult>();
        var allChunksOversized = allChunks.Count > 0 && validChunks.Count == 0 && oversizedCount > 0;

        foreach (var citation in citations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (citation is null)
                continue;

            // No quote → nothing to verify. Common case for fact-source / comparable-matter refs.
            if (string.IsNullOrWhiteSpace(citation.Quote))
            {
                results.Add(new VerificationResult
                {
                    Citation = citation,
                    Verdict = VerificationVerdict.NoQuote,
                    Reason = "Citation carries no quote to verify (ref type without verbatim text)."
                });
                continue;
            }

            // If every chunk was rejected by the size cap, this citation cannot be verified
            // even though its quote is well-formed. Surface as InvalidInput so the caller can
            // distinguish "no source available due to DoS protection" from "quote actually missing."
            if (allChunksOversized)
            {
                results.Add(new VerificationResult
                {
                    Citation = citation,
                    Verdict = VerificationVerdict.InvalidInput,
                    Reason = $"All {oversizedCount} source chunk(s) exceeded the {IGroundingVerifier.MaxSourceChunkLength}-char DoS cap; no verifiable source available."
                });
                continue;
            }

            results.Add(VerifyOne(citation, validChunks));
        }

        return Task.FromResult<IReadOnlyList<VerificationResult>>(results);
    }

    private static VerificationResult VerifyOne(EvidenceRef citation, List<NormalizedChunk> chunks)
    {
        var quoteNormalized = Normalize(citation.Quote!);

        // Step 1: exact substring match. Cheap; happens first.
        foreach (var chunk in chunks)
        {
            if (chunk.NormalizedText.Contains(quoteNormalized, StringComparison.Ordinal))
            {
                return new VerificationResult
                {
                    Citation = citation,
                    Verdict = VerificationVerdict.Verified,
                    MatchedChunkId = chunk.ChunkId,
                    Reason = "Exact substring match (post-normalization)."
                };
            }
        }

        // Step 2: sliding-window approximate match. Only attempted for reasonably-long quotes;
        // short quotes (e.g., a single word or abbreviation) generate too many false positives
        // under token-overlap matching, so we only accept exact for those.
        if (quoteNormalized.Length >= MinApproximateQuoteLength)
        {
            var quoteTokens = Tokenize(quoteNormalized);
            if (quoteTokens.Count > 0)
            {
                foreach (var chunk in chunks)
                {
                    var match = TryApproximateMatchInChunk(quoteTokens, chunk);
                    if (match is not null)
                    {
                        return new VerificationResult
                        {
                            Citation = citation,
                            Verdict = VerificationVerdict.VerifiedApproximate,
                            MatchedChunkId = chunk.ChunkId,
                            Reason = $"Sliding-window match at offset {match.Value.Offset} (overlap {match.Value.OverlapRatio:F2})."
                        };
                    }
                }
            }
        }

        return new VerificationResult
        {
            Citation = citation,
            Verdict = VerificationVerdict.NotFound,
            Reason = "No exact substring match; no sliding-window match above threshold."
        };
    }

    /// <summary>
    /// Slides a fixed-size window across the chunk and returns the best match offset + overlap
    /// ratio if it meets <see cref="ApproximateMatchThreshold"/>.
    /// </summary>
    private static (int Offset, double OverlapRatio)? TryApproximateMatchInChunk(
        IReadOnlyList<string> quoteTokens,
        NormalizedChunk chunk)
    {
        var text = chunk.NormalizedText;
        if (text.Length == 0)
            return null;

        // For chunks shorter than the window, treat the entire chunk as one window.
        if (text.Length <= WindowSize)
        {
            var windowTokens = Tokenize(text);
            var ratio = TokenOverlapRatio(quoteTokens, windowTokens);
            return ratio >= ApproximateMatchThreshold ? (0, ratio) : null;
        }

        for (var offset = 0; offset + WindowSize <= text.Length; offset += WindowStep)
        {
            var windowText = text.AsSpan(offset, WindowSize).ToString();
            var windowTokens = Tokenize(windowText);
            var ratio = TokenOverlapRatio(quoteTokens, windowTokens);
            if (ratio >= ApproximateMatchThreshold)
                return (offset, ratio);
        }

        // Tail window — ensure we don't miss matches at the end if the text length isn't a clean multiple.
        if (text.Length > WindowSize)
        {
            var tailOffset = text.Length - WindowSize;
            var tailTokens = Tokenize(text.AsSpan(tailOffset, WindowSize).ToString());
            var ratio = TokenOverlapRatio(quoteTokens, tailTokens);
            if (ratio >= ApproximateMatchThreshold)
                return (tailOffset, ratio);
        }

        return null;
    }

    /// <summary>
    /// Ratio of distinct quote tokens that appear in the window (case-sensitive after normalization).
    /// </summary>
    private static double TokenOverlapRatio(IReadOnlyList<string> quoteTokens, IReadOnlyList<string> windowTokens)
    {
        if (quoteTokens.Count == 0)
            return 0.0;

        var windowSet = new HashSet<string>(windowTokens, StringComparer.Ordinal);
        var distinctQuoteTokens = new HashSet<string>(quoteTokens, StringComparer.Ordinal);
        if (distinctQuoteTokens.Count == 0)
            return 0.0;

        var hits = 0;
        foreach (var t in distinctQuoteTokens)
        {
            if (windowSet.Contains(t))
                hits++;
        }

        return (double)hits / distinctQuoteTokens.Count;
    }

    /// <summary>
    /// Collapses whitespace and lowercases. Preserves substantive characters and punctuation
    /// (so "$280K" stays "$280k"); ToLowerInvariant() is the only lossy operation.
    /// </summary>
    internal static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);
        var prevWasWhitespace = false;
        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevWasWhitespace && sb.Length > 0)
                    sb.Append(' ');
                prevWasWhitespace = true;
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevWasWhitespace = false;
            }
        }

        // Trim a single trailing space if present.
        if (sb.Length > 0 && sb[^1] == ' ')
            sb.Length--;

        return sb.ToString();
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var parts = text.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p.Length > 1)
                tokens.Add(p);
            else if (p.Length == 1 && char.IsLetterOrDigit(p[0]))
                tokens.Add(p);
            // Single non-alphanumeric chars (orphan punctuation) skipped.
        }
        return tokens;
    }

    /// <summary>Normalized chunk pre-computed once per VerifyAsync call.</summary>
    private sealed record NormalizedChunk(string ChunkId, string NormalizedText);
}
