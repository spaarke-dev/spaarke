using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Caching.Distributed;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-27 (task 054) — deterministic RE-ANCHORING engine. When a Word round-trip produces a new
/// SPE version of a Compose document, prior Compose anchored-annotations must be re-located
/// against the (possibly drifted) new text. This service scores each prior anchor's
/// <c>textPattern</c> against the reloaded document's paragraphs — content-match similarity
/// (Levenshtein) weighted with a structural position hint — producing a confidence score, and
/// assigns it to one of three bands:
/// <list type="bullet">
///   <item><description><b>AUTO</b> (≥<see cref="ReanchorBands.AutoThreshold"/>) — silently re-anchored.</description></item>
///   <item><description><b>REVIEW</b> (≥<see cref="ReanchorBands.ReviewThreshold"/>) — flagged for the user.</description></item>
///   <item><description><b>ORPHAN</b> (below REVIEW, or no viable match) — surfaced, NEVER silently dropped (FR-27).</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure deterministic scoring — no LLM call, no AI-internal type (ADR-013 / NFR-05)</b>. The
/// human-friendly change summary is a SEPARATE gated capability (<c>compose-summarize-word-changes</c>,
/// FR-10 task 043) that DOES call the model; this engine does not. Tier-1 NetArchTest
/// (<c>ADR013_ComposeFacadeTests</c>) enforces that <c>Services/Compose/</c> never reaches
/// <c>IOpenAiClient</c>, a Nodes executor, or <c>IConsumerRoutingService</c>. This class satisfies
/// that by construction — the only collaborators are Open XML (paragraph text extraction) and
/// <see cref="IDistributedCache"/> (Redis summary persistence).
/// </para>
/// <para>
/// <b>No <c>Microsoft.Graph</c> type (ADR-007)</b>: the SPE download hop stays in the endpoint
/// layer via the <c>ISpeFileOperations</c> facade; this class receives already-downloaded
/// <c>byte[]</c> (or pre-extracted paragraph strings) and returns records.
/// </para>
/// <para>
/// <b>Grounded in Spike 6</b> (<c>notes/spikes/spike-6-word-roundtrip.md</c> §5, task 006). The
/// scoring formula (<c>0.75·contentSim + 0.25·structuralProx</c>) and the three band cut-points
/// were empirically validated against 8 realistic drift scenarios (no-change, reflow, partial /
/// heavy rewrite, deletion, duplicate). Spike 6 §5.3 surfaced ONE material gap the three static
/// bands cannot see — an <b>ambiguity guard</b> — implemented here in
/// <see cref="Reanchor(IReadOnlyList{PriorAnchor}, IReadOnlyList{string}, string?, DateTimeOffset)"/>:
/// when a would-be AUTO match has a competing second-best candidate whose content similarity is
/// within <see cref="ReanchorBands.AmbiguityMargin"/> of the best, the match is a coin-flip, so it
/// is demoted AUTO→REVIEW and flagged <see cref="ReanchoredAnnotation.Ambiguous"/> rather than
/// silently pinned to whichever duplicate the scan found first (Spike 6 scenario G).
/// </para>
/// <para>
/// <b>Bands are named constants</b> (<see cref="ReanchorBands"/>) seeded from Spike 6, adjustable
/// without restructuring — changing a threshold is a one-line edit; the ambiguity guard composes
/// with the bands as an orthogonal fourth rule, not a replacement.
/// </para>
/// <para>
/// <b>ADR-009 Redis</b>: the per-document re-anchor summary is cross-request state (computed when
/// the change is detected; read when the user returns to Compose — "hours later", design §2.5), so
/// <see cref="SaveSummaryAsync"/> / <see cref="GetSummaryAsync"/> persist it via
/// <see cref="IDistributedCache"/> under <c>sdap:compose:reanchor:{documentSpeId}</c> — the same
/// key-naming + serialization convention as <see cref="SpeSyncOrchestrator"/>'s
/// <c>ComposeSyncState</c>, NOT <c>IMemoryCache</c>. The pure scoring surface
/// (<see cref="Reanchor(IReadOnlyList{PriorAnchor}, IReadOnlyList{string}, string?, DateTimeOffset)"/>
/// and <see cref="ExtractParagraphTexts"/>) is <c>static</c> so band-boundary unit tests need no
/// cache.
/// </para>
/// </remarks>
public sealed class AnnotationReanchorService
{
    private const string ReanchorKeyPrefix = "sdap:compose:reanchor:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly TimeProvider _timeProvider;

    public AnnotationReanchorService(IDistributedCache cache, TimeProvider? timeProvider = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // -- Pure scoring surface (static — testable with plain strings, no cache) -------------------

    /// <summary>
    /// Scores each prior anchor against <paramref name="currentParagraphs"/> and assigns a band.
    /// Pure and deterministic — the same inputs always produce the same summary (no wall-clock
    /// dependency beyond the caller-supplied <paramref name="computedAtUtc"/> stamp, no I/O, no
    /// randomness). Zero prior anchors returns an all-zero summary (not an error).
    /// </summary>
    /// <param name="priorAnchors">The Compose annotations whose anchors must be re-located.</param>
    /// <param name="currentParagraphs">The reloaded document's paragraph texts, in document order
    /// (see <see cref="ExtractParagraphTexts"/>).</param>
    /// <param name="documentSpeId">Optional SPE drive-item id, echoed into the summary for the UI.</param>
    /// <param name="computedAtUtc">Timestamp stamped onto the summary (injected for test determinism).</param>
    /// <param name="currentParaIds">FR-11 (task 012) — the reloaded document's <c>w14:paraId</c>s, in
    /// the SAME document order as <paramref name="currentParagraphs"/> (index-aligned; see
    /// <see cref="ExtractParaIds"/>). When supplied, paraId is the PRIMARY anchor: an anchor whose
    /// <see cref="PriorAnchor.ParaId"/> exact-matches a current paragraph's id re-anchors definitively
    /// (AUTO, confidence 1.0) WITHOUT fuzzy scoring — within our own load→edit→save round-trip Word
    /// preserves paraId (S1/S1b). Only when the anchor carries no paraId, or its paraId is absent here
    /// (an external Word edit regenerated ids — Open-XML-SDK #925), does the retained
    /// textPattern+Levenshtein+paragraphHint scorer run. <c>null</c> (the default) disables the
    /// paraId path entirely — every anchor falls to the fuzzy scorer, preserving the pre-FR-11 behavior
    /// for callers that have not extracted the id map.</param>
    public static ReanchorSummary Reanchor(
        IReadOnlyList<PriorAnchor> priorAnchors,
        IReadOnlyList<string> currentParagraphs,
        string? documentSpeId,
        DateTimeOffset computedAtUtc,
        IReadOnlyList<string?>? currentParaIds = null)
    {
        ArgumentNullException.ThrowIfNull(priorAnchors);
        ArgumentNullException.ThrowIfNull(currentParagraphs);

        var results = new List<ReanchoredAnnotation>(priorAnchors.Count);
        var auto = 0;
        var review = 0;
        var orphan = 0;

        foreach (var anchor in priorAnchors)
        {
            // FR-11 (task 012) — paraId is the PRIMARY anchor. A paragraph's w14:paraId is stable
            // across our own load→edit→save round-trip: the E1 save synthesizes the redline in place
            // (ComposeParagraphRedlineSynthesizer, Option C), rewriting only run content — the w:p
            // paraId attribute survives by construction. (This is why the retired Docxodus WmlComparer
            // path was rejected: task 003 proved it STRIPS paraId on real docs.) So an exact id hit is a
            // DEFINITIVE re-anchor — the paragraph may have been edited (content drift) yet still be the
            // same paragraph. Resolve by id first; the fuzzy scorer below runs ONLY when the id is
            // absent (design §5.2 — e.g. Word regenerated the ids across an EXTERNAL edit session).
            var paraIdIdx = ResolveByParaId(anchor.ParaId, currentParaIds);
            if (paraIdIdx >= 0)
            {
                auto++;
                var idPreview = paraIdIdx < currentParagraphs.Count ? Preview(currentParagraphs[paraIdIdx]) : null;
                results.Add(new ReanchoredAnnotation(
                    Id: anchor.Id,
                    Type: anchor.Type,
                    Preview: anchor.Preview,
                    Band: ReanchorBand.Auto,
                    Confidence: 1.0,
                    MatchedParagraphIndex: paraIdIdx,
                    ContentSimilarity: 1.0,
                    StructuralProximity: 1.0,
                    Ambiguous: false,
                    MatchedParagraphPreview: idPreview));
                continue;
            }

            var (bestIdx, bestContent, secondBestContent) =
                FindBestParagraphMatch(anchor.TextPattern, currentParagraphs);

            var structural = StructuralProximity(bestIdx, anchor.ParagraphHint);
            var combined = bestIdx < 0
                ? 0.0
                : (ReanchorBands.ContentWeight * bestContent) + (ReanchorBands.StructuralWeight * structural);

            var band = BandOf(combined);

            // Spike 6 §5.3 ambiguity guard (the mandatory 4th rule): a near-tie between the best
            // and second-best CONTENT candidate is a coin-flip. Only ever LOOKING at the best
            // candidate, the static bands cannot see this — a byte-identical duplicate clause
            // elsewhere scores a perfect 1.0 and lands in AUTO (scenario G). Flag it, and demote a
            // would-be AUTO to REVIEW so a possibly-wrong duplicate is never silently auto-anchored.
            var ambiguous = bestIdx >= 0
                && secondBestContent >= bestContent - ReanchorBands.AmbiguityMargin
                && secondBestContent > ReanchorBands.AmbiguityMinRivalScore;

            if (ambiguous && band == ReanchorBand.Auto)
            {
                band = ReanchorBand.Review;
            }

            switch (band)
            {
                case ReanchorBand.Auto: auto++; break;
                case ReanchorBand.Review: review++; break;
                default: orphan++; break;
            }

            var matchedPreview = bestIdx >= 0 && bestIdx < currentParagraphs.Count
                ? Preview(currentParagraphs[bestIdx])
                : null;

            results.Add(new ReanchoredAnnotation(
                Id: anchor.Id,
                Type: anchor.Type,
                Preview: anchor.Preview,
                Band: band,
                Confidence: Math.Round(combined, 4),
                MatchedParagraphIndex: band == ReanchorBand.Orphan ? -1 : bestIdx,
                ContentSimilarity: Math.Round(bestContent, 4),
                StructuralProximity: Math.Round(structural, 4),
                Ambiguous: ambiguous,
                MatchedParagraphPreview: band == ReanchorBand.Orphan ? null : matchedPreview));
        }

        return new ReanchorSummary(
            DocumentSpeId: documentSpeId,
            Total: priorAnchors.Count,
            AutoCount: auto,
            ReviewCount: review,
            OrphanCount: orphan,
            Annotations: results,
            ComputedAtUtc: computedAtUtc);
    }

    /// <summary>
    /// Extracts each paragraph's live (settled) text from <paramref name="docx"/>, in document
    /// order — the re-anchor scoring corpus. Mirrors <see cref="DocxAnnotationReader"/>'s
    /// direct-child-run accounting (runs wrapped in <c>w:ins</c>/<c>w:del</c> are one level deeper
    /// and excluded, so drift is measured against text Word treats as settled). Pure — no I/O
    /// beyond reading the in-memory bytes.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="docx"/> is null or empty.</exception>
    /// <exception cref="DocxAnnotationException"><paramref name="docx"/> is not a readable DOCX.</exception>
    public static IReadOnlyList<string> ExtractParagraphTexts(byte[] docx) => ReadParagraphs(docx).Texts;

    /// <summary>
    /// FR-11 (task 012) — extracts each paragraph's <c>w14:paraId</c> from <paramref name="docx"/>, in
    /// the SAME document order (and thus index-aligned) as <see cref="ExtractParagraphTexts"/>; the
    /// entry is <c>null</c> for a paragraph that carries no id. This is the CURRENT-document id list
    /// that <see cref="Reanchor(IReadOnlyList{PriorAnchor}, IReadOnlyList{string}, string?, DateTimeOffset, IReadOnlyList{string?})"/>
    /// resolves an anchor's <see cref="PriorAnchor.ParaId"/> against. The same recursive
    /// <c>Descendants&lt;Paragraph&gt;()</c> walk as the text extraction guarantees the alignment
    /// (mirrors <see cref="ParaIdPreParser"/>, incl. table-cell + nested-table paragraphs). Ids are
    /// upper-cased so a Word-lowercased id still matches an anchor's canonical (uppercase) id.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="docx"/> is null or empty.</exception>
    /// <exception cref="DocxAnnotationException"><paramref name="docx"/> is not a readable DOCX.</exception>
    public static IReadOnlyList<string?> ExtractParaIds(byte[] docx) => ReadParagraphs(docx).ParaIds;

    /// <summary>
    /// Single Open XML pass returning BOTH the per-paragraph settled text and the per-paragraph
    /// <c>w14:paraId</c> (null where absent), index-aligned in document order. The two public
    /// extraction methods and <see cref="ComputeAndPersistAsync"/> share this one walk so the text
    /// corpus and the id list can never drift out of alignment.
    /// </summary>
    private static (IReadOnlyList<string> Texts, IReadOnlyList<string?> ParaIds) ReadParagraphs(byte[] docx)
    {
        if (docx is null || docx.Length == 0)
        {
            throw new ArgumentException("docx is required and must be non-empty.", nameof(docx));
        }

        using var buffer = new MemoryStream();
        buffer.Write(docx, 0, docx.Length);
        buffer.Position = 0;

        WordprocessingDocument doc;
        try
        {
            doc = WordprocessingDocument.Open(buffer, isEditable: false);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new DocxAnnotationException(
                DocxAnnotationErrorKind.MalformedDocument,
                "The supplied bytes are not a readable .docx (WordprocessingML) package.",
                ex);
        }

        using (doc)
        {
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return (Array.Empty<string>(), Array.Empty<string?>());
            }

            var texts = new List<string>();
            var paraIds = new List<string?>();
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                texts.Add(GetParagraphText(paragraph));
                var id = paragraph.ParagraphId?.Value;
                paraIds.Add(string.IsNullOrEmpty(id) ? null : id.ToUpperInvariant());
            }

            return (texts, paraIds);
        }
    }

    /// <summary>
    /// FR-11 (task 012) — resolves an anchor's <paramref name="anchorParaId"/> to the 0-based index of
    /// the current paragraph bearing the same <c>w14:paraId</c> (case-insensitive), or <c>-1</c> when
    /// the anchor carries no id, <paramref name="currentParaIds"/> is null, or the id is not present
    /// (an external Word edit regenerated ids). paraIds are unique within a document (task 010
    /// guarantees), so the first match is the only match.
    /// </summary>
    private static int ResolveByParaId(string? anchorParaId, IReadOnlyList<string?>? currentParaIds)
    {
        if (string.IsNullOrEmpty(anchorParaId) || currentParaIds is null)
        {
            return -1;
        }

        for (var i = 0; i < currentParaIds.Count; i++)
        {
            if (string.Equals(currentParaIds[i], anchorParaId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    // -- Redis persistence (ADR-009 — cross-request re-anchor summary) ---------------------------

    /// <summary>
    /// Convenience: extract paragraphs from the reloaded <paramref name="docx"/>, score
    /// <paramref name="priorAnchors"/> against them, persist the summary to Redis keyed by
    /// <paramref name="documentSpeId"/>, and return it. The single call the re-anchor endpoint
    /// makes.
    /// </summary>
    public async Task<ReanchorSummary> ComputeAndPersistAsync(
        string documentSpeId,
        IReadOnlyList<PriorAnchor> priorAnchors,
        byte[] docx,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentSpeId);

        // FR-11 (task 012): extract the paragraph text corpus AND the index-aligned w14:paraId list in
        // ONE Open XML pass, so Reanchor can resolve by paraId first (primary anchor) and fall back to
        // the fuzzy scorer only where the id is absent (external Word edit regenerated ids).
        var (paragraphs, paraIds) = ReadParagraphs(docx);
        var summary = Reanchor(priorAnchors, paragraphs, documentSpeId, _timeProvider.GetUtcNow(), paraIds);
        await SaveSummaryAsync(documentSpeId, summary, ct).ConfigureAwait(false);
        return summary;
    }

    /// <summary>Persists the re-anchor summary to Redis (ADR-009) so the banner survives a page
    /// reload / BFF restart between change-detection and the user's return to Compose.</summary>
    public async Task SaveSummaryAsync(string documentSpeId, ReanchorSummary summary, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentSpeId);
        ArgumentNullException.ThrowIfNull(summary);

        var json = JsonSerializer.Serialize(summary, JsonOptions);
        await _cache.SetStringAsync(ReanchorKeyPrefix + documentSpeId, json, ct).ConfigureAwait(false);
    }

    /// <summary>Reads the persisted re-anchor summary for a document (null if none stored).</summary>
    public async Task<ReanchorSummary?> GetSummaryAsync(string documentSpeId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentSpeId);

        var json = await _cache.GetStringAsync(ReanchorKeyPrefix + documentSpeId, ct).ConfigureAwait(false);
        return json is null ? null : JsonSerializer.Deserialize<ReanchorSummary>(json, JsonOptions);
    }

    // -- Scoring internals (Spike 6 §5.1) --------------------------------------------------------

    /// <summary>
    /// Best- and second-best content-similarity match for <paramref name="anchorText"/> across
    /// <paramref name="currentParagraphs"/>. Below <see cref="ReanchorBands.CandidateFloor"/> the
    /// best match is NOT nominated (returns index -1) — a production re-anchorer treats a
    /// near-random paragraph as an orphan rather than pinning to "the least bad" match (Spike 6
    /// scenario F). The second-best score is returned so the ambiguity guard can detect a near-tie.
    /// </summary>
    private static (int BestIdx, double BestContentSim, double SecondBestContentSim) FindBestParagraphMatch(
        string anchorText, IReadOnlyList<string> currentParagraphs)
    {
        var bestIdx = -1;
        var bestSim = 0.0;
        var secondSim = 0.0;

        for (var i = 0; i < currentParagraphs.Count; i++)
        {
            var sim = ContentSimilarity(anchorText, currentParagraphs[i]);
            if (sim > bestSim)
            {
                secondSim = bestSim;
                bestSim = sim;
                bestIdx = i;
            }
            else if (sim > secondSim)
            {
                secondSim = sim;
            }
        }

        if (bestIdx < 0 || bestSim < ReanchorBands.CandidateFloor)
        {
            return (-1, bestSim, secondSim);
        }

        return (bestIdx, bestSim, secondSim);
    }

    /// <summary>
    /// Structural position hint: 1.0 at the exact prior paragraph index, decaying with distance.
    /// Deliberately weighted only 25% (see <see cref="ReanchorBands.StructuralWeight"/>) — the
    /// design treats structural position as a tie-breaker, not a veto (Spike 6 §5.3 scenario C
    /// validated that a pure one-paragraph reflow must NOT drag a perfect content match below AUTO).
    /// </summary>
    private static double StructuralProximity(int bestIdx, int paragraphHint)
    {
        if (bestIdx < 0 || paragraphHint < 0)
        {
            // No candidate, or the prior anchor carried no usable paragraph hint — let content
            // carry the score (a small non-zero floor so a strong content match can still clear
            // REVIEW on its own).
            return bestIdx < 0 ? 0.0 : 0.3;
        }

        var distance = Math.Abs(bestIdx - paragraphHint);
        return distance switch
        {
            0 => 1.0,
            1 => 0.85,
            <= 3 => 0.6,
            _ => 0.3,
        };
    }

    private static ReanchorBand BandOf(double score) =>
        score >= ReanchorBands.AutoThreshold ? ReanchorBand.Auto
        : score >= ReanchorBands.ReviewThreshold ? ReanchorBand.Review
        : ReanchorBand.Orphan;

    private static double ContentSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

        var distance = Levenshtein(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return maxLen == 0 ? 1.0 : 1.0 - ((double)distance / maxLen);
    }

    /// <summary>Rolling two-row Levenshtein — O(min(n,m)) memory, deterministic.</summary>
    private static int Levenshtein(string a, string b)
    {
        // Keep the shorter string on the inner axis to minimize the row buffers.
        if (a.Length < b.Length)
        {
            (a, b) = (b, a);
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>Direct-child-run plain text only (mirrors <see cref="DocxAnnotationReader"/>).</summary>
    private static string GetParagraphText(Paragraph paragraph)
    {
        var sb = new StringBuilder();
        foreach (var run in paragraph.Elements<Run>())
        {
            var text = run.GetFirstChild<Text>()?.Text;
            if (!string.IsNullOrEmpty(text))
            {
                sb.Append(text);
            }
        }

        return sb.ToString();
    }

    /// <summary>Short, log/UI-safe preview of a paragraph (Tier-1 length cap).</summary>
    private static string Preview(string text)
    {
        const int max = 160;
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= max ? text : text[..max] + "…";
    }
}

/// <summary>
/// Confidence-band cut-points + scoring weights, seeded from Spike 6 (task 006) and adjustable
/// without restructuring the engine. The three thresholds were scope-locked (spec FR-27); the
/// ambiguity-guard constants are Spike 6's complementary 4th rule.
/// </summary>
public static class ReanchorBands
{
    /// <summary>≥ this combined score auto-anchors (unless the ambiguity guard demotes it).</summary>
    public const double AutoThreshold = 0.85;

    /// <summary>≥ this (and below <see cref="AutoThreshold"/>) is flagged for review.</summary>
    public const double ReviewThreshold = 0.60;

    /// <summary>Content-match weight (0.75) — deliberately 3× structural (Spike 6 §5.3).</summary>
    public const double ContentWeight = 0.75;

    /// <summary>Structural-position weight (0.25) — a hint, not a veto.</summary>
    public const double StructuralWeight = 0.25;

    /// <summary>
    /// Ambiguity margin — if the second-best content candidate is within this of the best, the
    /// match is a coin-flip and a would-be AUTO is demoted to REVIEW (Spike 6 §5.3, scenario G at
    /// margin 0.00).
    /// </summary>
    public const double AmbiguityMargin = 0.05;

    /// <summary>The second-best candidate must exceed this to count as a genuine rival (a weak
    /// runner-up is not an ambiguity — Spike 6's <c>secondBestContent &gt; 0.5</c> gate).</summary>
    public const double AmbiguityMinRivalScore = 0.5;

    /// <summary>Below this content similarity, no candidate is nominated (orphan, not a forced
    /// pin to the least-bad paragraph).</summary>
    public const double CandidateFloor = 0.15;
}

/// <summary>The re-anchor band a prior anchor was assigned to (serialized camelCase for the UI).</summary>
[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum ReanchorBand
{
    /// <summary>≥0.85 and unambiguous — re-anchored automatically.</summary>
    Auto,

    /// <summary>0.6–0.85, or a demoted near-tie — flagged for the user to resolve.</summary>
    Review,

    /// <summary>&lt;0.6 or no viable match — surfaced, never silently dropped (FR-27).</summary>
    Orphan,
}

/// <summary>
/// A prior Compose anchored-annotation to re-locate. <see cref="TextPattern"/> is the content-match
/// input (the annotation's <c>anchor.textPattern</c>, design §3); <see cref="ParagraphHint"/> is
/// its prior 0-based document-order paragraph index (the structural half). <see cref="Preview"/> is
/// an optional short body/label the conflict UI shows so the user can recognize the annotation.
/// </summary>
public sealed record PriorAnchor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("textPattern")] string TextPattern,
    [property: JsonPropertyName("paragraphHint")] int ParagraphHint,
    [property: JsonPropertyName("preview")] string? Preview = null,
    // R3 FR-11 (task 012): PRIMARY anchor — the paragraph's w14:paraId. Resolution tries this
    // FIRST; the TextPattern+Levenshtein+ParagraphHint scorer below is the retained fallback for
    // when it is absent (Word regenerated paraIds on an external save — Open-XML-SDK #925). Optional
    // + trailing so existing PriorAnchor construction/deserialization is unaffected (additive).
    [property: JsonPropertyName("paraId")] string? ParaId = null);

/// <summary>One prior anchor's re-anchor outcome — the band, the score components, whether a
/// near-tie made it ambiguous, and (for AUTO/REVIEW) the matched paragraph so the UI can show
/// where it landed. An ORPHAN carries <see cref="MatchedParagraphIndex"/> = -1.</summary>
public sealed record ReanchoredAnnotation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("preview")] string? Preview,
    [property: JsonPropertyName("band")] ReanchorBand Band,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("matchedParagraphIndex")] int MatchedParagraphIndex,
    [property: JsonPropertyName("contentSimilarity")] double ContentSimilarity,
    [property: JsonPropertyName("structuralProximity")] double StructuralProximity,
    [property: JsonPropertyName("ambiguous")] bool Ambiguous,
    [property: JsonPropertyName("matchedParagraphPreview")] string? MatchedParagraphPreview);

/// <summary>The full re-anchor summary — per-band counts + every re-anchored annotation. Drives
/// the Workspace banner ("N re-anchored, M need review") and the conflict UX. Persisted to Redis
/// (ADR-009) so it survives the gap between change-detection and the user's return.</summary>
public sealed record ReanchorSummary(
    [property: JsonPropertyName("documentSpeId")] string? DocumentSpeId,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("autoCount")] int AutoCount,
    [property: JsonPropertyName("reviewCount")] int ReviewCount,
    [property: JsonPropertyName("orphanCount")] int OrphanCount,
    [property: JsonPropertyName("annotations")] IReadOnlyList<ReanchoredAnnotation> Annotations,
    [property: JsonPropertyName("computedAtUtc")] DateTimeOffset ComputedAtUtc);
