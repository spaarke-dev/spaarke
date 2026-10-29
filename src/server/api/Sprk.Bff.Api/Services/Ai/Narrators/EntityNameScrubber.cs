// R7 Wave 11 T116 narrator spike (2026-06-30) — EntityNameScrubber service.
//
// PURPOSE: Pure-C# scrubbing service that takes (candidateText, allowList) and returns
// (scrubbedText, removedTerms[]). Extracted from EntityNameValidatorNodeExecutor.cs
// so the spike narrator can use the same algorithm without going through the playbook
// engine.
//
// DUPLICATION NOTE (spike-only): the algorithm below is copy-paste from
// EntityNameValidatorNodeExecutor's private static methods. If the narrator spike
// succeeds and we ship code-based narrators, the executor should be refactored to
// delegate to this service so there is ONE implementation of the algorithm.
// Until that decision is made, the duplication is intentional — keeps the existing
// executor risk-free during the spike.
//
// Reference:
//   src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/EntityNameValidatorNodeExecutor.cs
//     (the original implementation, lines 88-106 for regex constants + 352-577 for
//     the scrubbing pipeline private static methods)
//   projects/spaarke-ai-platform-unification-r7/notes/spikes/narrator-spike-plan.md

using System.Text;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ai.Narrators;

/// <summary>
/// Outcome of a single scrub pass — surviving text plus the proper-noun spans
/// that were stripped because they were not in the allow-list.
/// </summary>
public sealed record EntityNameScrubResult
{
    /// <summary>Sentences that passed the proper-noun-against-allow-list check, reassembled.</summary>
    public required string ScrubbedText { get; init; }

    /// <summary>Proper-noun spans removed (one entry per hallucination occurrence).</summary>
    public required IReadOnlyList<string> RemovedTerms { get; init; }
}

/// <summary>
/// Contract for the post-LLM entity-name scrubber. Pure function: no Dataverse,
/// no Handlebars, no orchestrator context. Easy to unit-test.
/// </summary>
public interface IEntityNameScrubber
{
    EntityNameScrubResult Scrub(string candidateText, IReadOnlyList<string> allowList);
}

/// <summary>
/// Sentence-level proper-noun scrubber. See class-level comment in
/// EntityNameValidatorNodeExecutor.cs for the algorithm rationale.
/// </summary>
public sealed class EntityNameScrubber : IEntityNameScrubber
{
    // Sentence splitter: terminal punctuation followed by whitespace / end-of-string.
    private static readonly Regex SentenceSplitter = new(
        @"(?<=[\.!\?])\s+",
        RegexOptions.Compiled);

    // Proper-Noun span detector. Matches sequences of Title-Case tokens, tolerating
    // short connector words ("of", "and", "&"), and "v." case-citation markers.
    // The word connectors end in \b so a capitalised word followed by one that merely STARTS with a connector
    // ("Decide lane", "Do office") is not read as "Decide la" / "Do of". "v." is excluded from the \b (it ends in a dot).
    private static readonly Regex ProperNounSpan = new(
        @"\b[A-Z][A-Za-z0-9'’]*(?:\s+(?:[A-Z][A-Za-z0-9'’]*|&|(?:of|and|the|von|de|del|la|le)\b|v\.))*",
        RegexOptions.Compiled);

    // Tokenizer for allow-list comparison — splits on whitespace and common punctuation.
    private static readonly Regex WordTokenizer = new(
        @"[A-Za-z0-9'’]+",
        RegexOptions.Compiled);

    public EntityNameScrubResult Scrub(string candidateText, IReadOnlyList<string> allowList)
    {
        ArgumentNullException.ThrowIfNull(allowList);
        if (string.IsNullOrWhiteSpace(candidateText))
        {
            return new EntityNameScrubResult { ScrubbedText = string.Empty, RemovedTerms = Array.Empty<string>() };
        }

        var allowTokens = BuildAllowTokenIndex(allowList);
        var sentences = SplitSentences(candidateText);
        var keptSentences = new List<string>(sentences.Count);
        var removedTerms = new List<string>();

        foreach (var sentence in sentences)
        {
            if (string.IsNullOrWhiteSpace(sentence)) continue;

            var hallucinatedSpans = FindHallucinatedSpans(sentence, allowList, allowTokens);

            if (hallucinatedSpans.Count == 0)
            {
                keptSentences.Add(sentence);
                continue;
            }

            // Sentence is removed. Record every hallucinated span discovered in it.
            foreach (var term in hallucinatedSpans)
            {
                removedTerms.Add(term);
            }
        }

        return new EntityNameScrubResult
        {
            ScrubbedText = ReassembleSentences(keptSentences),
            RemovedTerms = removedTerms
        };
    }

    private static IReadOnlyList<string> SplitSentences(string text)
    {
        var parts = SentenceSplitter.Split(text);
        var result = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0) result.Add(trimmed);
        }
        return result;
    }

    private static string ReassembleSentences(IReadOnlyList<string> keptSentences)
    {
        if (keptSentences.Count == 0) return string.Empty;
        var sb = new StringBuilder(keptSentences.Sum(s => s.Length + 1));
        for (var i = 0; i < keptSentences.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(keptSentences[i]);
        }
        return sb.ToString();
    }

    private static IReadOnlyList<string> FindHallucinatedSpans(
        string sentence,
        IReadOnlyList<string> allowList,
        IReadOnlySet<string> allowTokens)
    {
        var matches = ProperNounSpan.Matches(sentence);
        if (matches.Count == 0) return Array.Empty<string>();

        var hallucinated = new List<string>();
        foreach (Match m in matches)
        {
            var term = m.Value.Trim();
            if (term.Length == 0) continue;
            if (IsLikelySentenceStarterOnly(term)) continue;
            if (!IsAllowed(term, allowList, allowTokens))
            {
                hallucinated.Add(term);
            }
        }
        return hallucinated;
    }

    private static bool IsAllowed(string candidate, IReadOnlyList<string> allowList, IReadOnlySet<string> allowTokens)
    {
        if (allowList.Count == 0) return false;

        var candidateLower = candidate.ToLowerInvariant();
        var candidateTokens = TokenizeLower(candidate);

        // Strategy 1: substring match either direction (case-insensitive).
        foreach (var allowed in allowList)
        {
            if (string.IsNullOrWhiteSpace(allowed)) continue;
            var allowedLower = allowed.ToLowerInvariant();
            if (candidateLower.Contains(allowedLower, StringComparison.Ordinal) ||
                allowedLower.Contains(candidateLower, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // Strategy 2: every meaningful candidate token is in the allow-token index.
        if (candidateTokens.Count > 0 && candidateTokens.All(t => allowTokens.Contains(t)))
        {
            return true;
        }

        return false;
    }

    private static bool IsLikelySentenceStarterOnly(string term)
    {
        // A span of several Title-Case words ("Of the") is skipped only when EVERY word is a function word:
        // a month or a weekday next to another capitalised word ("April May") could still be a person, so
        // the time words below are single-word-only.
        if (term.Contains(' '))
        {
            var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 0 && words.All(w =>
            {
                var lw = w.ToLowerInvariant();
                return lw is not ("may" or "will") && IsFunctionWord(lw);   // "May"/"Will" can be a given name
            });
        }

        var lower = term.ToLowerInvariant();
        if (IsFunctionWord(lower)) return true;

        if (lower is "monday" or "tuesday" or "wednesday" or "thursday" or
            "friday" or "saturday" or "sunday" or
            "january" or "february" or "march" or "april" or "may" or "june" or
            "july" or "august" or "september" or "october" or "november" or "december" or
            "today" or "tomorrow" or "yesterday" or
            "morning" or "afternoon" or "evening" or "tonight" or
            "next" or "last" or "now" or "soon" or "later")
        {
            return true;
        }

        return false;
    }

    // Words that open a sentence in a count-only summary ("There are 3 items...", "Nothing to report", "Most items...",
    // "Three items...") and can never be an entity name. Conservative on purpose: nothing here is a plausible surname
    // or company name. (Task 062 round 2: the worklist narrative carries no names, so with an empty allow list every
    // capitalised word not listed here was scrubbed and the whole narrative dropped.)
    private static bool IsFunctionWord(string lower) =>
        lower is "the" or "a" or "an" or "this" or "that" or "these" or "those" or
            "his" or "her" or "its" or "our" or "their" or "my" or "your" or
            "it" or "we" or "you" or "i" or "he" or "she" or "they" or
            "if" or "and" or "or" or "but" or "so" or "yet" or "for" or "nor" or
            "as" or "at" or "by" or "in" or "of" or "on" or "to" or "up" or
            "is" or "was" or "are" or "were" or "be" or "been" or "being" or
            "has" or "have" or "had" or "do" or "does" or "did" or "will" or "would" or
            "can" or "could" or "should" or "may" or "might" or "must" or
            "no" or "not" or "yes" or
            "there" or "nothing" or "most" or "all" or "several" or "some" or
            "one" or "two" or "three" or "four" or "five" or "six" or "seven" or "eight" or "nine" or "ten";

    private static IReadOnlySet<string> BuildAllowTokenIndex(IReadOnlyList<string> allowList)
    {
        if (allowList.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in allowList)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            foreach (var token in TokenizeLower(entry)) set.Add(token);
        }
        return set;
    }

    private static IReadOnlyList<string> TokenizeLower(string text)
    {
        var matches = WordTokenizer.Matches(text);
        if (matches.Count == 0) return Array.Empty<string>();
        var result = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            var lower = m.Value.ToLowerInvariant();
            if (lower is "the" or "and" or "of" or "a" or "an") continue;
            result.Add(lower);
        }
        return result;
    }
}
