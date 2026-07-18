using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Rung 3.5 — <b>deterministic record-name/number match</b> (email-r4 UAT, owner spec 2026-07-17). Closes the
/// gap that let exact-name records fall through to the fuzzy semantic rung: the deterministic rungs 0–3 match
/// on thread / participant / structure / reference-NUMBER tokens, but NONE matched an email against existing
/// record <b>names</b>. This rung does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Retrieve then verify.</b> The rung uses the records index (<see cref="IRecordMatchingAi"/>, keyword
/// ranking — <see cref="RecordSearchOptions.PreferKeywordRanking"/> so the semantic reranker does not bury
/// exact matches) purely to NARROW to candidate records. It then makes the actual decision DETERMINISTICALLY:
/// a candidate matches only if its <b>name</b> (normalized token subsequence) or a <b>reference number</b>
/// (normalized alphanumeric) appears verbatim in the email subject/body. So the match is exact + reproducible,
/// not a fuzzy score.
/// </para>
/// <para>
/// <b>Surface all, never auto-file.</b> Every verified record type (matter AND project AND invoice) is emitted
/// as its own high-confidence candidate so the reviewer can pick the primary — the owner's explicit workflow.
/// The rung runs in the deterministic pass (<c>IncomingAssociationResolver.IsDeterministic</c> includes
/// <see cref="RungKind.RecordNameMatch"/>) but is deliberately EXCLUDED from the mapper's auto-file-eligible
/// set (and is NOT an AI rung), so a name match lands as <b>Suggested</b>, never <b>Resolved</b>. It also never
/// dedups records (duplicate-named records are legitimate in production).
/// </para>
/// <para>
/// <b>Precision guards.</b> A candidate name must clear <see cref="RecordNameMatchOptions.MinNameLength"/> +
/// <see cref="RecordNameMatchOptions.MinNameTokens"/> (a short/common single-word name is too weak a signal —
/// the semantic rung still covers those fuzzily); a reference number must clear
/// <see cref="RecordNameMatchOptions.MinNumberLength"/>.
/// </para>
/// <para>
/// Best-effort/non-fatal (NFR-06): a throw is treated as a non-match. Consumes the scoped
/// <see cref="IRecordMatchingAi"/> facade from a per-evaluation scope (this rung is a singleton). Registered as
/// a concrete <see cref="IAssociationRung"/> singleton in <c>CommunicationModule</c> (ADR-010).
/// </para>
/// </remarks>
public sealed class RecordNameMatchRung : IAssociationRung
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<RecordNameMatchOptions> _options;
    private readonly ILogger<RecordNameMatchRung> _logger;

    /// <summary>Record types the <c>spaarke-records-index</c> contains (matter / project / invoice).</summary>
    private static readonly IReadOnlyList<string> SearchableRecordTypes = RecordEntityType.ValidTypes;

    public RecordNameMatchRung(
        IServiceScopeFactory scopeFactory,
        IOptions<RecordNameMatchOptions> options,
        ILogger<RecordNameMatchRung> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public RungKind Kind => RungKind.RecordNameMatch;

    // Order sorts evaluation sequence WITHIN the deterministic partition; 3 runs it alongside/after the
    // structural detectors. The deterministic-vs-AI partition keys off Kind, not Order.
    public int Order => 3;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        var opts = _options.Value;

        if (!opts.Enabled)
        {
            _logger.LogDebug("Rung 3.5 (record-name match) skipped — disabled via Communication:RecordNameMatch:Enabled.");
            return Array.Empty<RungMatch>();
        }

        var query = BuildQuery(message, opts.MaxQueryChars);
        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogDebug("Rung 3.5 (record-name match) skipped — no query text (empty subject + body).");
            return Array.Empty<RungMatch>();
        }

        // Verification corpora derived from the SAME envelope text: a token list for name subsequence checks,
        // and an alphanumeric-collapsed string for reference-number checks.
        var emailTokens = Tokenize(query);
        var emailCollapsed = CollapseAlphanumeric(query);
        if (emailTokens.Count == 0)
            return Array.Empty<RungMatch>();

        var startTs = Stopwatch.GetTimestamp();

        RecordSearchResponse response;
        using (var scope = _scopeFactory.CreateScope())
        {
            var matcher = scope.ServiceProvider.GetRequiredService<IRecordMatchingAi>();
            var request = new RecordSearchRequest
            {
                Query = query,
                RecordTypes = SearchableRecordTypes,
                Options = new RecordSearchOptions
                {
                    Limit = opts.Limit,
                    HybridMode = RecordHybridSearchMode.KeywordOnly,
                    PreferKeywordRanking = true, // deterministic verification follows; don't let the reranker reorder
                },
            };

            response = await matcher.SearchAsync(request, ct);
        }

        var matches = VerifyAndMap(response, emailTokens, emailCollapsed, opts);
        var elapsed = Stopwatch.GetElapsedTime(startTs);

        _logger.LogInformation(
            "Rung 3.5 (record-name match) fired | QueryChars: {QueryChars}, Candidates: {CandidateCount}, Verified: {VerifiedCount}, ElapsedMs: {ElapsedMs}",
            query.Length, response.Results.Count, matches.Count, (long)elapsed.TotalMilliseconds);

        return matches;
    }

    /// <summary>
    /// Composes the retrieval/verification text from subject + plain-text body + extracted attachment text
    /// (Phase 2 match signal), whitespace-collapsed, capped. Keyword-only + no embedding on this rung, so the
    /// cap can be generous — verification (exact name/number containment) needs the fuller text.
    /// </summary>
    private static string BuildQuery(NormalizedMessage message, int maxChars)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(message.Subject))
            parts.Add(message.Subject.Trim());
        if (!string.IsNullOrWhiteSpace(message.BodyText))
            parts.Add(message.BodyText.Trim());
        if (!string.IsNullOrWhiteSpace(message.AttachmentText))
            parts.Add(message.AttachmentText.Trim());

        if (parts.Count == 0)
            return string.Empty;

        var query = string.Join(" \n ", parts);
        query = string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return query.Length > maxChars ? query[..maxChars] : query;
    }

    /// <summary>
    /// Deterministically verifies each index candidate against the email text and maps hits to
    /// <see cref="RungMatch"/>. A candidate qualifies when its NAME (normalized token subsequence, subject to the
    /// length/token guards) OR one of its reference NUMBERS (normalized alphanumeric) appears in the email.
    /// Dedups by (field, target) keeping the highest confidence; returns the top N by confidence.
    /// </summary>
    private IReadOnlyList<RungMatch> VerifyAndMap(
        RecordSearchResponse response,
        IReadOnlyList<string> emailTokens,
        string emailCollapsed,
        RecordNameMatchOptions opts)
    {
        var best = new Dictionary<(string Field, Guid Id), RungMatch>();

        foreach (var result in response.Results)
        {
            var field = RegardingFieldMap.FieldFor(result.RecordType);
            if (field is null)
                continue; // record type not in the ADR-024 regarding family

            if (!Guid.TryParse(result.RecordId, out var targetId))
                continue;

            double confidence;
            string provenanceDetail;

            if (NameAppears(result.RecordName, emailTokens, opts))
            {
                confidence = opts.NameConfidence;
                provenanceDetail = $"name=\"{result.RecordName}\"";
            }
            else if (TryMatchNumber(result.ReferenceNumbers, emailCollapsed, opts, out var matchedNumber))
            {
                confidence = opts.NumberConfidence;
                provenanceDetail = $"number=\"{matchedNumber}\"";
            }
            else
            {
                continue; // no exact name/number appearance — leave this candidate to the semantic rung
            }

            var match = new RungMatch
            {
                RegardingFieldName = field,
                Target = new EntityReference(result.RecordType, targetId) { Name = result.RecordName },
                Confidence = confidence,
                Provenance = $"record-name-match:{result.RecordType}:{provenanceDetail}",
                Rung = RungKind.RecordNameMatch,
            };

            var key = (field, targetId);
            if (!best.TryGetValue(key, out var existing) || match.Confidence > existing.Confidence)
                best[key] = match;
        }

        return best.Values
            .OrderByDescending(m => m.Confidence)
            .Take(opts.MaxCandidates)
            .ToArray();
    }

    /// <summary>
    /// True when the record name (after the length + token-count guards) appears as a contiguous token
    /// subsequence in the email tokens. Token-based so "smith" does not match inside "blacksmith".
    /// </summary>
    private static bool NameAppears(string? recordName, IReadOnlyList<string> emailTokens, RecordNameMatchOptions opts)
    {
        if (string.IsNullOrWhiteSpace(recordName))
            return false;

        var normalizedLength = CollapseAlphanumeric(recordName).Length;
        if (normalizedLength < opts.MinNameLength)
            return false;

        var nameTokens = Tokenize(recordName);
        if (nameTokens.Count < opts.MinNameTokens)
            return false;

        return ContainsContiguous(emailTokens, nameTokens);
    }

    /// <summary>
    /// True when any reference number (alphanumeric-collapsed, subject to the min-length guard) appears in the
    /// alphanumeric-collapsed email text. Collapsing ignores separators so "REAL-2026-123456.02" matches
    /// whether the email wrote it with dashes, dots, or spaces.
    /// </summary>
    private static bool TryMatchNumber(
        IReadOnlyList<string>? referenceNumbers, string emailCollapsed, RecordNameMatchOptions opts, out string matched)
    {
        matched = string.Empty;
        if (referenceNumbers is null)
            return false;

        foreach (var refNum in referenceNumbers)
        {
            if (string.IsNullOrWhiteSpace(refNum))
                continue;

            var collapsed = CollapseAlphanumeric(refNum);
            if (collapsed.Length < opts.MinNumberLength)
                continue;

            if (emailCollapsed.Contains(collapsed, StringComparison.Ordinal))
            {
                matched = refNum;
                return true;
            }
        }

        return false;
    }

    // ── Normalization helpers ────────────────────────────────────────────────────

    /// <summary>Lowercases and splits into alphanumeric tokens (all non-alphanumeric are separators).</summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length > 0)
            tokens.Add(sb.ToString());
        return tokens;
    }

    /// <summary>Lowercases and removes ALL non-alphanumeric characters (for separator-insensitive number matching).</summary>
    private static string CollapseAlphanumeric(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>True when <paramref name="needle"/> appears as a contiguous run within <paramref name="haystack"/>.</summary>
    private static bool ContainsContiguous(IReadOnlyList<string> haystack, IReadOnlyList<string> needle)
    {
        if (needle.Count == 0 || needle.Count > haystack.Count)
            return false;

        for (int i = 0; i <= haystack.Count - needle.Count; i++)
        {
            var allMatch = true;
            for (int j = 0; j < needle.Count; j++)
            {
                if (!string.Equals(haystack[i + j], needle[j], StringComparison.Ordinal))
                {
                    allMatch = false;
                    break;
                }
            }
            if (allMatch)
                return true;
        }

        return false;
    }
}
