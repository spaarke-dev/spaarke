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

        // Location-scoped verification corpora: subject / body / attachment are verified SEPARATELY so a
        // candidate's confidence reflects WHERE its name appeared (subject > body > attachment) — a name in
        // the subject is a far stronger association signal than one buried in a long attachment.
        var corpora = BuildCorpora(message, opts.MaxQueryChars);
        if (corpora.IsEmpty)
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

        var matches = VerifyAndMap(response, corpora, opts);
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
    /// Deterministically verifies each index candidate against the location-scoped email corpora and maps hits
    /// to <see cref="RungMatch"/>. A candidate qualifies when its NAME (normalized token subsequence, subject to
    /// the length/token guards) OR a reference NUMBER (normalized alphanumeric) appears — with confidence tiered
    /// by WHERE it matched (number &gt; subject &gt; body &gt; attachment). Provenance carries a parseable
    /// where/matched/name/number/reason payload for the review UI. Dedups by (field, target) keeping the highest
    /// confidence; returns the top N by confidence (so subject matches survive the cap over attachment noise).
    /// </summary>
    private IReadOnlyList<RungMatch> VerifyAndMap(
        RecordSearchResponse response, MatchCorpora corpora, RecordNameMatchOptions opts)
    {
        var best = new Dictionary<(string Field, Guid Id), RungMatch>();

        foreach (var result in response.Results)
        {
            var field = RegardingFieldMap.FieldFor(result.RecordType);
            if (field is null)
                continue; // record type not in the ADR-024 regarding family

            if (!Guid.TryParse(result.RecordId, out var targetId))
                continue;

            var verified = DetermineBestMatch(result, corpora, opts);
            if (verified is null)
                continue; // no exact name/number appearance — leave this candidate to the semantic rung

            var v = verified.Value;
            var recordNumber = result.ReferenceNumbers is { Count: > 0 } ? result.ReferenceNumbers[0] : string.Empty;
            var reason = v.Kind == "number"
                ? $"reference number in {v.Location}"
                : $"name in {v.Location}";

            // Parseable provenance for the CommunicationConnections review UI (match reason + record number).
            var provenance =
                $"record-name-match:{result.RecordType}:where={v.Location}:matched={v.Kind}:" +
                $"name=\"{result.RecordName}\":number=\"{recordNumber}\":reason=\"{reason}\"";

            var match = new RungMatch
            {
                RegardingFieldName = field,
                Target = new EntityReference(result.RecordType, targetId) { Name = result.RecordName },
                Confidence = v.Confidence,
                Provenance = provenance,
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
    /// Determines a candidate's strongest verified match: a reference number (top priority, most discriminating)
    /// or a name, each located in subject → body → attachment (highest-confidence location wins). Returns null
    /// when nothing appears verbatim.
    /// </summary>
    private static VerifiedMatch? DetermineBestMatch(
        RecordSearchResult result, MatchCorpora c, RecordNameMatchOptions opts)
    {
        // Reference number first — a number appearance is the most discriminating signal.
        if (result.ReferenceNumbers is { Count: > 0 })
        {
            foreach (var refNum in result.ReferenceNumbers)
            {
                if (string.IsNullOrWhiteSpace(refNum))
                    continue;
                var collapsed = CollapseAlphanumeric(refNum);
                if (collapsed.Length < opts.MinNumberLength)
                    continue;

                var loc = c.SubjectCollapsed.Contains(collapsed, StringComparison.Ordinal) ? "subject"
                        : c.BodyCollapsed.Contains(collapsed, StringComparison.Ordinal) ? "body"
                        : c.AttachmentCollapsed.Contains(collapsed, StringComparison.Ordinal) ? "attachment"
                        : null;
                if (loc is not null)
                    return new VerifiedMatch(opts.NumberConfidence, loc, "number", refNum);
            }
        }

        // Name, tiered by location. Guards: normalized length + token count (a short/common single word is too
        // weak a deterministic signal — the semantic rung still covers those).
        if (!string.IsNullOrWhiteSpace(result.RecordName))
        {
            var normalizedLength = CollapseAlphanumeric(result.RecordName).Length;
            var nameTokens = Tokenize(result.RecordName);
            if (normalizedLength >= opts.MinNameLength && nameTokens.Count >= opts.MinNameTokens)
            {
                if (ContainsContiguous(c.SubjectTokens, nameTokens))
                    return new VerifiedMatch(opts.SubjectNameConfidence, "subject", "name", result.RecordName);
                if (ContainsContiguous(c.BodyTokens, nameTokens))
                    return new VerifiedMatch(opts.BodyNameConfidence, "body", "name", result.RecordName);
                if (ContainsContiguous(c.AttachmentTokens, nameTokens))
                    return new VerifiedMatch(opts.AttachmentNameConfidence, "attachment", "name", result.RecordName);
            }
        }

        return null;
    }

    /// <summary>Builds the location-scoped verification corpora (subject / body / attachment), each tokenized and alphanumeric-collapsed. Attachment text is bounded by <paramref name="maxChars"/>.</summary>
    private static MatchCorpora BuildCorpora(NormalizedMessage message, int maxChars)
    {
        var subject = CollapseWhitespace(message.Subject);
        var body = CollapseWhitespace(message.BodyText);
        var attachment = CollapseWhitespace(message.AttachmentText);
        if (attachment.Length > maxChars)
            attachment = attachment[..maxChars];

        return new MatchCorpora(
            Tokenize(subject), Tokenize(body), Tokenize(attachment),
            CollapseAlphanumeric(subject), CollapseAlphanumeric(body), CollapseAlphanumeric(attachment));
    }

    private static string CollapseWhitespace(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A verified match: its confidence, where it matched (subject/body/attachment), what matched (name/number), and the matched value.</summary>
    private readonly record struct VerifiedMatch(double Confidence, string Location, string Kind, string Value);

    /// <summary>Location-scoped verification corpora derived from the envelope.</summary>
    private sealed record MatchCorpora(
        IReadOnlyList<string> SubjectTokens,
        IReadOnlyList<string> BodyTokens,
        IReadOnlyList<string> AttachmentTokens,
        string SubjectCollapsed,
        string BodyCollapsed,
        string AttachmentCollapsed)
    {
        public bool IsEmpty => SubjectTokens.Count == 0 && BodyTokens.Count == 0 && AttachmentTokens.Count == 0;
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
