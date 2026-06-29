// R4 spaarke-daily-update-service-r4 — Post-LLM Entity-Name Validator (task 003 / FR-3)
// Implements ExecutorType.EntityNameValidator = 141 (added by task 002).
//
// Purpose (defense-in-depth post-LLM scrubbing, per FR-3 / AC-3a / AC-3b):
//   R3 UAT verified the LLM emits fictional firm/case names (e.g., "Johnson & Lee LLP",
//   "Davis v. Metro Transit") into the narration output even with grounding instructions
//   + temperature 0 in the prompt. This NodeExecutor scrubs LLM-emitted entity names that
//   are NOT present in an explicit allow-list derived from the input payload. Each removal
//   is logged as a structured `hallucination_detected` event for App Insights monitoring
//   (per docs/guides/AI-MONITORING-DASHBOARD.md).
//
// Pattern source: LookupUserMembershipNodeExecutor.cs (sibling — Validate + ExecuteAsync
//   canonical shape). This executor is simpler — pure string analysis, no external deps
//   beyond ILogger, so it does NOT need IServiceScopeFactory (matches SanitizerNodeExecutor).
//
// Input contract (PlaybookNodeDto.ConfigJson):
//   {
//     "candidateText": "ACME Corp received an engagement letter from Johnson & Lee LLP yesterday.",
//     "allowList": ["ACME Corp", "Sprk Industries"]
//   }
//
// Output contract (NodeOutput.StructuredData, bound to OutputVariable):
//   {
//     "scrubbedText": "ACME Corp received an engagement letter.",
//     "removedTerms": ["Johnson & Lee LLP"]
//   }
//
// Scrub strategy (sentence-level; LLM-phrasing-tolerant):
//   1. Sentence-split candidateText on terminal punctuation (. ! ?), preserving the punctuation.
//   2. For each sentence, extract Proper-Noun spans (Title-Case word sequences, optionally with
//      embedded "&", "of", "and", etc., and tolerant of "v." case-citation markers).
//   3. For each Proper-Noun span, test against the allow-list using case-insensitive partial
//      substring matching (the candidate matches an allow-list entry if either contains the other
//      as a word-boundary token, accommodating LLM lowercase / abbreviation variants).
//   4. If a span has no allow-list match, the sentence is treated as a hallucination carrier and
//      removed; the offending span(s) recorded in `removedTerms`; one structured log event per
//      removed term.
//
// Empty allowList is VALID (means "scrub all proper-noun-bearing sentences"); null is INVALID.
//
// Reference: spec.md FR-3 (lines 116-122), AC-3a, AC-3b; task 003 POML; node-executor-authoring
//            pattern (.claude/patterns/ai/node-executor-authoring.md); ADR-010 DI Minimalism;
//            ADR-013 BFF AI Architecture.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor that scrubs LLM-emitted entity names not present in an explicit
/// allow-list (post-LLM defense-in-depth against AI hallucination per FR-3).
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="INodeExecutor"/> for <see cref="ExecutorType.EntityNameValidator"/>
/// (value 141, added by task 002). Registered as a Singleton in
/// <c>AnalysisServicesModule.AddNodeExecutors</c> alongside the other executors (no
/// scope-factory needed — pure string analysis, no external deps beyond ILogger).
/// </para>
/// <para>
/// Per-removal, emits a structured <c>hallucination_detected</c> warning event via
/// <see cref="ILogger"/> with fields <c>candidate_term</c>, <c>position</c>,
/// <c>playbook_id</c>, <c>correlation_id</c> — App Insights query target per
/// <c>docs/guides/AI-MONITORING-DASHBOARD.md</c>.
/// </para>
/// </remarks>
public sealed class EntityNameValidatorNodeExecutor : INodeExecutor
{
    /// <summary>
    /// Literal event name used in the structured-log payload for App Insights
    /// monitoring queries. MUST remain stable — referenced from AI-MONITORING-DASHBOARD.md.
    /// </summary>
    public const string HallucinationDetectedEvent = "hallucination_detected";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // Sentence splitter: terminal punctuation followed by whitespace / end-of-string.
    // Preserves the terminal punctuation with the preceding sentence so we can reassemble
    // surviving sentences without losing readability.
    private static readonly Regex SentenceSplitter = new(
        @"(?<=[\.!\?])\s+",
        RegexOptions.Compiled);

    // Proper-Noun span detector. Matches sequences of Title-Case tokens, tolerating
    // short connector words ("of", "and", "&"), trailing acronym-ish tokens (LLP, LLC, Inc),
    // and "v." case-citation markers between two Title-Case spans (e.g., "Davis v. Metro Transit").
    // Conservative: a span must begin with an UPPERCASE letter; "the firm" is not a span.
    private static readonly Regex ProperNounSpan = new(
        @"\b[A-Z][A-Za-z0-9'’]*(?:\s+(?:[A-Z][A-Za-z0-9'’]*|&|of|and|the|von|de|del|la|le|v\.))*",
        RegexOptions.Compiled);

    // Tokenizer for allow-list comparison — splits on whitespace and common punctuation.
    private static readonly Regex WordTokenizer = new(
        @"[A-Za-z0-9'’]+",
        RegexOptions.Compiled);

    private readonly ILogger<EntityNameValidatorNodeExecutor> _logger;

    public EntityNameValidatorNodeExecutor(ILogger<EntityNameValidatorNodeExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.EntityNameValidator
    };

    // R7 task 032 / FR-16 — typed config schema for Playbook Builder canvas (Wave 8 FR-23).
    // Derived from this executor's ConfigJson consumption via EntityNameValidatorNodeConfig
    // (camelCase via [JsonPropertyName]): CandidateText (required), AllowList (required; empty
    // array means scrub all proper-noun-bearing sentences; null is a validation error).
    // See projects/spaarke-ai-platform-unification-r7/notes/spikes/executor-config-fields-inventory.md §4.
    private static readonly ExecutorConfigSchema ConfigSchemaInstance = new(
        ExecutorTypeName: nameof(ExecutorType.EntityNameValidator),
        ExecutorTypeValue: (int)ExecutorType.EntityNameValidator,
        Description: "Post-LLM defense-in-depth scrubber. Removes hallucinated entity names from LLM output by comparing against an explicit allow-list (R4 FR-3 / AC-3a).",
        Fields: new ConfigSchemaField[]
        {
            new(
                Name: "candidateText",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "Raw text emitted by the upstream LLM node that needs scrubbing. Typically a template like '{{summarize.output.narration}}'.",
                Default: null),
            new(
                Name: "allowList",
                Type: SchemaFieldType.Array,
                Required: true,
                Description: "Array of entity names known to be present in the input payload (matters, contacts, parties, etc.). Empty array opts into scrubbing every proper-noun-bearing sentence; null is invalid.",
                Default: null)
        });

    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() => ConfigSchemaInstance;

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Node.OutputVariable))
        {
            errors.Add("EntityNameValidator node requires OutputVariable to be set on the node");
        }

        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            errors.Add("EntityNameValidator node requires configuration (ConfigJson with 'candidateText' and 'allowList')");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        EntityNameValidatorNodeConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<EntityNameValidatorNodeConfig>(
                context.Node.ConfigJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid EntityNameValidator node configuration JSON: {ex.Message}");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        if (config is null)
        {
            errors.Add("Failed to parse EntityNameValidator node configuration");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        if (string.IsNullOrWhiteSpace(config.CandidateText))
        {
            errors.Add("EntityNameValidator node requires 'candidateText' in ConfigJson");
        }

        // allowList may be empty (means "scrub all proper-noun-bearing sentences"), but
        // must NOT be null — caller must consciously opt-in to scrub-everything semantics.
        if (config.AllowList is null)
        {
            errors.Add("EntityNameValidator node requires 'allowList' in ConfigJson (use [] to scrub all proper-noun names)");
        }

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        using var activity = AiTelemetry.ActivitySource.StartActivity(
            "ai.entity_name_validator.node_execute", ActivityKind.Internal);
        activity?.SetTag("node.id", context.Node.Id.ToString());
        activity?.SetTag("node.name", context.Node.Name);
        activity?.SetTag("action_type", (int)ExecutorType.EntityNameValidator);

        _logger.LogDebug(
            "Executing EntityNameValidator node {NodeId} ({NodeName})",
            context.Node.Id, context.Node.Name);

        try
        {
            var validation = Validate(context);
            if (!validation.IsValid)
            {
                activity?.SetTag("node.outcome", "validation_failed");
                return Task.FromResult(NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", validation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
            }

            var config = JsonSerializer.Deserialize<EntityNameValidatorNodeConfig>(
                context.Node.ConfigJson!, JsonOptions)!;
            var candidateText = config.CandidateText!;
            var allowList = config.AllowList ?? Array.Empty<string>();

            var allowTokens = BuildAllowTokenIndex(allowList);

            var sentences = SplitSentences(candidateText);
            var keptSentences = new List<string>(sentences.Count);
            var removedTerms = new List<string>();

            foreach (var sentence in sentences)
            {
                if (string.IsNullOrWhiteSpace(sentence))
                {
                    continue;
                }

                var hallucinatedSpans = FindHallucinatedSpans(sentence, allowList, allowTokens);

                if (hallucinatedSpans.Count == 0)
                {
                    keptSentences.Add(sentence);
                    continue;
                }

                // Sentence is removed. Record + log every hallucinated span discovered in it.
                foreach (var span in hallucinatedSpans)
                {
                    removedTerms.Add(span.Term);
                    EmitHallucinationDetected(
                        context,
                        span.Term,
                        span.PositionInCandidateText,
                        allowList.Count);
                }
            }

            var scrubbedText = ReassembleSentences(keptSentences);

            _logger.LogInformation(
                "EntityNameValidator node {NodeId} completed -- inputLength={InputLen}, outputLength={OutputLen}, removedCount={RemovedCount}",
                context.Node.Id, candidateText.Length, scrubbedText.Length, removedTerms.Count);

            activity?.SetTag("node.outcome", "success");
            activity?.SetTag("validator.removed_count", removedTerms.Count);
            activity?.SetTag("validator.allow_list_size", allowList.Count);

            var outputData = new
            {
                scrubbedText,
                removedTerms = (IReadOnlyList<string>)removedTerms
            };

            return Task.FromResult(NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                outputData,
                textContent: scrubbedText,
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "EntityNameValidator node {NodeId} was cancelled",
                context.Node.Id);

            activity?.SetTag("node.outcome", "cancelled");
            return Task.FromResult(NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                "Node execution was cancelled",
                NodeErrorCodes.Cancelled,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EntityNameValidator node {NodeId} failed: {ErrorMessage}",
                context.Node.Id, ex.Message);

            activity?.SetTag("node.outcome", "error");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Task.FromResult(NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Entity-name validation failed: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow)));
        }
    }

    /// <summary>
    /// Emits a structured <c>hallucination_detected</c> warning event per removed term.
    /// Uses the canonical key <see cref="HallucinationDetectedEvent"/> in the message
    /// template so App Insights queries can pivot on it.
    /// </summary>
    private void EmitHallucinationDetected(
        NodeExecutionContext context,
        string candidateTerm,
        int position,
        int allowListSize)
    {
        // The literal "hallucination_detected" key is in the message template so it
        // appears verbatim in App Insights logs (queryable via traces | where message contains "hallucination_detected").
        _logger.LogWarning(
            "hallucination_detected: candidate_term={CandidateTerm}, position={Position}, playbook_id={PlaybookId}, correlation_id={CorrelationId}, allow_list_size={AllowListSize}, node_id={NodeId}",
            candidateTerm,
            position,
            context.PlaybookId,
            context.CorrelationId ?? string.Empty,
            allowListSize,
            context.Node.Id);
    }

    /// <summary>
    /// Splits the input text into sentences, preserving terminal punctuation with each
    /// sentence. Defensive against missing-terminal-punctuation (the entire text is one
    /// sentence) and short input.
    /// </summary>
    private static IReadOnlyList<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var parts = SentenceSplitter.Split(text);
        var result = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                result.Add(trimmed);
            }
        }
        return result;
    }

    /// <summary>
    /// Reassembles surviving sentences into a single output string. Joins sentences
    /// with a single space (terminal punctuation already lives in each sentence).
    /// </summary>
    private static string ReassembleSentences(IReadOnlyList<string> keptSentences)
    {
        if (keptSentences.Count == 0)
        {
            return string.Empty;
        }
        var sb = new StringBuilder(keptSentences.Sum(s => s.Length + 1));
        for (var i = 0; i < keptSentences.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }
            sb.Append(keptSentences[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Walks a sentence, identifies all Proper-Noun spans, and returns those not
    /// represented in the allow-list. Empty allow-list → all proper nouns are
    /// hallucinations (caller opt-in).
    /// </summary>
    private static IReadOnlyList<HallucinationSpan> FindHallucinatedSpans(
        string sentence,
        IReadOnlyList<string> allowList,
        IReadOnlySet<string> allowTokens)
    {
        var matches = ProperNounSpan.Matches(sentence);
        if (matches.Count == 0)
        {
            return Array.Empty<HallucinationSpan>();
        }

        var hallucinated = new List<HallucinationSpan>();
        foreach (Match m in matches)
        {
            var term = m.Value.Trim();
            if (term.Length == 0)
            {
                continue;
            }

            // Skip single-word common-case starters ("The", "A", "An") — defensible
            // because they don't qualify as named-entity hallucinations on their own.
            if (IsLikelySentenceStarterOnly(term))
            {
                continue;
            }

            if (!IsAllowed(term, allowList, allowTokens))
            {
                hallucinated.Add(new HallucinationSpan(term, m.Index));
            }
        }

        // De-duplicate spans on the same term (a sentence might have the same entity
        // mentioned twice; we want one log entry per term-occurrence-in-sentence so
        // monitoring counts match what the user perceives).
        return hallucinated;
    }

    /// <summary>
    /// Returns true if the candidate term is allowed: either it matches an allow-list
    /// entry case-insensitively (full substring either direction), or every meaningful
    /// word in the candidate is present in the allow-token index.
    /// </summary>
    private static bool IsAllowed(
        string candidate,
        IReadOnlyList<string> allowList,
        IReadOnlySet<string> allowTokens)
    {
        if (allowList.Count == 0)
        {
            // Empty allow-list — explicit opt-in by caller to scrub all entity names.
            return false;
        }

        var candidateLower = candidate.ToLowerInvariant();
        var candidateTokens = TokenizeLower(candidate);

        // Strategy 1: direct substring match against any allow-list entry (either direction).
        // Tolerates case-only LLM variations ("acme corp" matches "ACME Corp").
        foreach (var allowed in allowList)
        {
            if (string.IsNullOrWhiteSpace(allowed))
            {
                continue;
            }
            var allowedLower = allowed.ToLowerInvariant();
            if (candidateLower.Contains(allowedLower, StringComparison.Ordinal) ||
                allowedLower.Contains(candidateLower, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // Strategy 2: every meaningful candidate token is in the allow-token index.
        // Tolerates LLM word-reordering ("Corp ACME" → allow ["ACME", "Corp"]).
        if (candidateTokens.Count > 0 && candidateTokens.All(t => allowTokens.Contains(t)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// True for single-word terms that are common English sentence starters, stop-words,
    /// or temporal/calendar words — avoids false-positive scrubs on sentences like
    /// "The report was filed Friday." Single-word proper-nouns that are days of the
    /// week, months, or generic time-words are NOT named-entity hallucinations on their
    /// own (they are domain-neutral vocabulary the LLM correctly capitalises).
    /// </summary>
    private static bool IsLikelySentenceStarterOnly(string term)
    {
        if (term.Contains(' '))
        {
            return false;
        }

        var lower = term.ToLowerInvariant();

        // Pronouns / determiners / conjunctions
        if (lower is "the" or "a" or "an" or "this" or "that" or "these" or "those" or
            "his" or "her" or "its" or "our" or "their" or "my" or "your" or
            "it" or "we" or "you" or "i" or "he" or "she" or "they" or
            "if" or "and" or "or" or "but" or "so" or "yet" or "for" or "nor" or
            "as" or "at" or "by" or "in" or "of" or "on" or "to" or "up" or
            "is" or "was" or "are" or "were" or "be" or "been" or "being" or
            "has" or "have" or "had" or "do" or "does" or "did" or "will" or "would" or
            "can" or "could" or "should" or "may" or "might" or "must" or
            "no" or "not" or "yes")
        {
            return true;
        }

        // Calendar / temporal words (LLM correctly capitalises these but they are not
        // legal-entity names — they appear in narration text as ordinary vocabulary).
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

    /// <summary>
    /// Builds a flat set of lower-cased word tokens from every allow-list entry,
    /// for fast O(1) Strategy-2 lookup.
    /// </summary>
    private static IReadOnlySet<string> BuildAllowTokenIndex(IReadOnlyList<string> allowList)
    {
        if (allowList.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in allowList)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }
            foreach (var token in TokenizeLower(entry))
            {
                set.Add(token);
            }
        }
        return set;
    }

    /// <summary>
    /// Tokenizes a string into lower-cased word tokens, ignoring trivial stop-words
    /// (the, and, of, &amp;) so they don't dominate the allow-token index.
    /// </summary>
    private static IReadOnlyList<string> TokenizeLower(string text)
    {
        var matches = WordTokenizer.Matches(text);
        if (matches.Count == 0)
        {
            return Array.Empty<string>();
        }
        var result = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            var lower = m.Value.ToLowerInvariant();
            if (lower is "the" or "and" or "of" or "a" or "an")
            {
                continue;
            }
            result.Add(lower);
        }
        return result;
    }

    /// <summary>
    /// Internal value record describing one hallucinated proper-noun span — the term
    /// itself and its byte offset within the original candidateText (for log correlation).
    /// </summary>
    private readonly record struct HallucinationSpan(string Term, int PositionInCandidateText);
}

/// <summary>
/// Configuration for <see cref="EntityNameValidatorNodeExecutor"/> read from
/// <c>PlaybookNodeDto.ConfigJson</c>. Property names use camelCase per the
/// playbook JSON convention.
/// </summary>
internal sealed record EntityNameValidatorNodeConfig
{
    /// <summary>
    /// The raw text emitted by the upstream LLM node that needs scrubbing. Required.
    /// </summary>
    [JsonPropertyName("candidateText")]
    public string? CandidateText { get; init; }

    /// <summary>
    /// Explicit allow-list of entity names known to be present in the input payload
    /// (matters, contacts, parties, etc.). Required (may be empty to opt into
    /// scrubbing every proper-noun-bearing sentence; null is a validation error).
    /// </summary>
    [JsonPropertyName("allowList")]
    public IReadOnlyList<string>? AllowList { get; init; }
}
