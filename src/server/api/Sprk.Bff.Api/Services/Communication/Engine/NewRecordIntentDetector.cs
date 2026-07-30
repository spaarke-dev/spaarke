using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// FR-12 (regarding-vs-related intent) — deterministic, high-precision detector of the
/// "<b>presents a NEW record while referencing an existing one</b>" case (owner decision 2026-07-30,
/// <c>notes/042-regarding-vs-related-owner-decision.md</c>). Pure/static (mirrors
/// <see cref="AutoFileGate"/>-adjacent helpers + <c>CitationVerifier</c> / <c>AttachmentActionGate</c>) so it
/// is unit-testable and carries no dependency, no LLM call, and no capture-path latency (NFR-04).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it decides.</b> Given the envelope text, it detects framing like
/// <i>"this is a new litigation matter related to LIT-123456"</i> / <i>"new filing based on PAT-908068"</i>
/// and returns the identifier value(s) that are <b>referenced, not filed onto</b>, plus a proposed
/// record-type hint. It does NOT resolve records or write anything.
/// </para>
/// <para>
/// <b>Why deterministic + why safe.</b> This is the misfile-critical guard (misfiling = #1 trust-killer).
/// It never needs to be perfect because the only action it drives is a <b>demotion</b>: the referenced
/// identifier's explicit-ID match is capped to sub-threshold in
/// <see cref="Rungs.IdentifierReverseLookupRung"/>, so the email lands <c>Suggested</c> instead of
/// auto-filing onto the referenced record. A false positive costs a human review (safe); a false negative is
/// just the pre-existing auto-file behavior (never worse). ADR-024 (amended 2026-07-30): there is ONE
/// direct relationship (<c>regarding</c>); the cross-reference is noted in <c>sprk_triagesummary</c>, not a
/// second field.
/// </para>
/// <para>
/// <b>Precision policy.</b> The "new record" trigger requires the literal word <c>new</c> within a few words
/// of a RECORD-TYPE noun (matter/case/project/invoice/…), NOT "new" + any noun — so "a new update on matter
/// LIT-1" does not fire. An identifier is marked referenced when (a) it shares a sentence with a trigger, OR
/// (b) a trigger exists anywhere AND the identifier is introduced by a reference connector
/// ("based on"/"related to"/"referencing"/…). Same well-formed identifier shape as the rung.
/// </para>
/// </remarks>
public static class NewRecordIntentDetector
{
    /// <summary>
    /// "new [modifier]{0,3} TYPE" — the literal word <c>new</c> followed within a few words by a record-type
    /// noun. Group <c>type</c> captures the noun (used for the proposed-record-type hint). Bias to precision:
    /// requires an actual record-type noun, so "new email"/"new update"/"new correspondence" do NOT trigger.
    /// </summary>
    /// <remarks>
    /// The filler between <c>new</c> and the type noun excludes prepositions/conjunctions via a negative
    /// lookahead, so only ADJECTIVE modifiers bridge (<i>new litigation matter</i>, <i>new corporate case</i>)
    /// — <i>"a new update ON matter MAT-1"</i> does NOT match (the type noun there is the object of a
    /// preposition, i.e. an existing record, not a new one). This is the key precision guard.
    /// </remarks>
    private static readonly Regex NewRecordTriggerPattern = new(
        @"\bnew\s+(?:(?!(?:on|of|for|to|about|from|with|in|at|by|and|or|the|a|an)\b)[A-Za-z]+\s+){0,2}?(?<type>matters?|litigations?|lawsuits?|cases?|projects?|invoices?|filings?|engagements?|budgets?|applications?|deals?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Same well-formed identifier shape as <see cref="Rungs.IdentifierReverseLookupRung"/> (2+ letter prefix,
    /// <c>-</c>/<c>.</c> separator, alphanumerics). Bare-numeric tokens are intentionally NOT matched here — a
    /// bare number never auto-files alone anyway, so it needs no new-record suppression.
    /// </summary>
    private static readonly Regex WellFormedIdentifierPattern = new(
        @"\b[A-Za-z]{2,}[-.][A-Za-z0-9][A-Za-z0-9.\-]*[A-Za-z0-9]\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// A reference connector immediately introducing a well-formed identifier: "based on LIT-1", "related to
    /// LIT-1", etc. Group <c>id</c> captures the identifier. Used for the document-level rule (trigger present
    /// anywhere + connector-introduced identifier) so "This is a new matter. It relates to LIT-1." is caught.
    /// </summary>
    private static readonly Regex ReferenceConnectorPattern = new(
        @"\b(?:based\s+on|relat(?:e|es|ed|ing)?\s+to|referencing|reference[sd]?|re:|stemming\s+from|arising\s+(?:out\s+of|from)|continuation\s+of|successor\s+to|following\s+up\s+on|off\s+of|per)\s+(?:[A-Za-z]+\s+){0,3}?(?<id>[A-Za-z]{2,}[-.][A-Za-z0-9][A-Za-z0-9.\-]*[A-Za-z0-9])\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Record-type noun (lower-cased, singularized loosely) → target entity logical name; null = generic.</summary>
    private static readonly IReadOnlyDictionary<string, string?> TypeNounToEntity =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["matter"] = "sprk_matter",
            ["litigation"] = "sprk_matter",
            ["lawsuit"] = "sprk_matter",
            ["case"] = "sprk_matter",
            ["project"] = "sprk_project",
            ["invoice"] = "sprk_invoice",
            ["budget"] = "sprk_budget",
            // Generic/ambiguous nouns: a new record is intended but the target type is not pinned.
            ["filing"] = null,
            ["engagement"] = null,
            ["application"] = null,
            ["deal"] = null,
        };

    /// <summary>
    /// Detect the "presents a new record while referencing an existing one" intent over the envelope. Returns
    /// <c>null</c> when no new-record framing is present (the overwhelming common case → zero effect on the
    /// association decision). Best-effort: a regex timeout on pathological input yields <c>null</c>.
    /// </summary>
    public static NewRecordIntent? Detect(string? subject, string? bodyText)
    {
        try
        {
            var text = string.Join(". ",
                new[] { subject, bodyText }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var triggerMatch = NewRecordTriggerPattern.Match(text);
            if (!triggerMatch.Success)
                return null;

            var referenced = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // (a) Sentence-level: an identifier sharing a sentence with a new-record trigger is referenced.
            foreach (var sentence in SplitSentences(text))
            {
                if (!NewRecordTriggerPattern.IsMatch(sentence))
                    continue;
                foreach (Match idm in WellFormedIdentifierPattern.Matches(sentence))
                    AddReferenced(idm.Value, referenced, seen);
            }

            // (b) Document-level: a trigger exists somewhere AND the identifier is connector-introduced.
            foreach (Match cm in ReferenceConnectorPattern.Matches(text))
                AddReferenced(cm.Groups["id"].Value, referenced, seen);

            if (referenced.Count == 0)
                return null;

            var typeNoun = NormalizeTypeNoun(triggerMatch.Groups["type"].Value);
            TypeNounToEntity.TryGetValue(typeNoun, out var entityHint);

            return new NewRecordIntent(
                ReferencedIdentifiers: referenced,
                ProposedEntityHint: entityHint,
                ProposedTypeLabel: typeNoun,
                TriggerPhrase: CollapseWhitespace(triggerMatch.Value));
        }
        catch (RegexMatchTimeoutException)
        {
            return null; // defensive (NFR-04): pathological input never breaks capture/enrichment
        }
    }

    /// <summary>True when <paramref name="identifierValue"/> is referenced-not-filed per this envelope.</summary>
    public static bool IsReferencedNotFiled(NewRecordIntent? intent, string identifierValue) =>
        intent is not null
        && !string.IsNullOrWhiteSpace(identifierValue)
        && intent.ReferencedIdentifiers.Contains(identifierValue.Trim(), StringComparer.OrdinalIgnoreCase);

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static void AddReferenced(string value, List<string> referenced, HashSet<string> seen)
    {
        var v = value.Trim();
        if (v.Length > 0 && seen.Add(v))
            referenced.Add(v);
    }

    private static IEnumerable<string> SplitSentences(string text) =>
        text.Split(new[] { '.', '!', '?', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries);

    private static string NormalizeTypeNoun(string noun)
    {
        var n = noun.Trim().ToLowerInvariant();
        // Loose singularization for the plural alternations (matters → matter, cases → case).
        if (n.EndsWith("s", StringComparison.Ordinal) && n.Length > 3)
            n = n[..^1];
        return n;
    }

    private static string CollapseWhitespace(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// Result of <see cref="NewRecordIntentDetector.Detect"/>: the envelope presents a NEW record while
/// referencing existing one(s). Carries the referenced identifier value(s) (to demote at capture), a proposed
/// target-entity hint for the "create new record" proposal (null = generic/unpinned), the human-readable type
/// label, and the matched trigger phrase (for provenance + the triage-summary note).
/// </summary>
public sealed record NewRecordIntent(
    IReadOnlyList<string> ReferencedIdentifiers,
    string? ProposedEntityHint,
    string ProposedTypeLabel,
    string TriggerPhrase);
