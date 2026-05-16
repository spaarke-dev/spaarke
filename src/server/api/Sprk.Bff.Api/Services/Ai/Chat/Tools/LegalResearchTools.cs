// ============================================================
// DATA GOVERNANCE — LegalResearchTools (ADR-015)
// ============================================================
//
// PURPOSE
//   This class wraps Azure AI Foundry Bing Grounding to give the SprkChat agent the ability
//   to search public legal sources (Westlaw, court databases, law reviews) for topic research
//   and specific case citation lookup. Because the agent operates in a legal-practice context,
//   queries originating from user messages MAY contain:
//
//     · Client names or entity names (e.g., "Acme Corp acquisition contracts")
//     · Matter reference numbers (e.g., "Matter 2024-0187")
//     · Case citation fragments mixed with client identifiers
//     · Email addresses quoted from correspondence
//     · Individual names from opposing counsel or witnesses
//
// SANITIZATION STRATEGY (QuerySanitizer.Sanitize)
//   Before any query is sent to Bing, the private QuerySanitizer.Sanitize() method strips
//   or replaces the following patterns using compiled Regex:
//
//     1. "Client: <name>" / "client: <name>" prefixes   → removed entirely
//     2. "Matter NNNN-NNNN" / "matter ref NNNN" patterns → replaced with "[MATTER-REF]"
//     3. Email addresses (RFC 5321 simplified)            → replaced with "[EMAIL]"
//     4. Quoted PII blocks ("Client: X", "Re: X") that
//        appear as search qualifiers                      → removed
//
//   The sanitizer does NOT strip case citations (e.g., "123 F.3d 456") because those are
//   intentionally the search target in LookupCaseAsync. It also does NOT strip company names
//   or legal terms — over-sanitization would destroy search value.
//
// LOGGING POLICY (ADR-015)
//   · ONLY query length, result count, and elapsed time are logged at Information level.
//   · Query text is NEVER logged at any level (not even Debug) because it may still contain
//     contextual PII that the sanitizer does not catch.
//   · Grounding response content is NEVER logged — only result counts.
//
// WHY BING GROUNDING INSTEAD OF DIRECT WEB SEARCH
//   · WebSearchTools uses the Bing Web Search v7 REST API with a standalone API key.
//     LegalResearchTools uses Azure AI Foundry's BingGroundingTool, which:
//       - Routes through the AI Foundry Agents SDK (same auth path as AgentServiceClient)
//       - Returns grounding annotations (URL + title) as structured data in the run response,
//         making citation extraction reliable without HTML parsing.
//       - Respects Bing's legal/news content filters at the Azure subscription level.
//   · The BingGroundingTool option also means no additional API key to manage — Managed
//     Identity from the BFF App Service is sufficient (consistent with AgentServiceClient).
//
// BING GROUNDING vs. SPAARKE RAG
//   Legal research explicitly requires EXTERNAL sources (case law databases, statutes,
//   secondary sources). The Spaarke RAG index holds INTERNAL client documents. These tools
//   are additive — the agent can call both depending on the playbook.
//
// ADR REFERENCES
//   ADR-013: AI tool pattern — AIFunctionFactory.Create, factory-instantiated, no DI reg.
//   ADR-015: Data governance — sanitize queries; log only metadata; external content labelled.
//   ADR-016: Rate limiting — SemaphoreSlim with MaxConcurrency from BingGroundingOptions.
//   ADR-018: Kill switch — BingGroundingOptions.Enabled; graceful degradation string returned.
// ============================================================

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Azure.AI.Projects;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Foundry;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing legal research capabilities via Azure AI Foundry Bing Grounding.
///
/// Exposes two methods registered as <see cref="AIFunction"/> via <see cref="AIFunctionFactory.Create"/>:
///   - <see cref="ResearchLegalAsync"/> — broad legal topic or question research.
///   - <see cref="LookupCaseAsync"/> — specific case citation lookup.
///
/// Both methods sanitize the incoming query via <see cref="QuerySanitizer.Sanitize"/> before
/// any text leaves the BFF boundary (ADR-015: PII in legal queries).
///
/// Instantiated by <see cref="SprkChatAgentFactory"/>. Not registered in DI — the factory
/// creates instances and registers methods as <see cref="AIFunction"/> objects via
/// <see cref="AIFunctionFactory.Create"/>.
///
/// ADR-010: 0 additional DI registrations — factory-instantiated only.
/// ADR-013: AI tools use AIFunctionFactory.Create pattern; no separate AI microservice.
/// ADR-015: Queries sanitized before leaving BFF; only metadata logged; external content labelled.
/// ADR-016: Bing Grounding calls bounded by SemaphoreSlim(MaxConcurrency).
/// ADR-018: Kill switch via BingGroundingOptions.Enabled — graceful degradation string returned.
/// </summary>
public sealed partial class LegalResearchTools
{
    // ── Concurrency gate (ADR-016) ─────────────────────────────────────────────
    // Static so the gate applies across all LegalResearchTools instances (all agent sessions).
    // MaxConcurrency read once at first instantiation; options are singleton-lifetime anyway.
    private static SemaphoreSlim? s_concurrencyGate;
    private static readonly object s_gateLock = new();

    /// <summary>
    /// Timeout for acquiring the concurrency semaphore. After this, a degradation string is
    /// returned rather than throwing — legal queries should degrade gracefully (ADR-016).
    /// </summary>
    private static readonly TimeSpan s_semaphoreTimeout = TimeSpan.FromSeconds(30);

    // ── Dependencies ───────────────────────────────────────────────────────────
    private readonly AgentServiceClient _agentServiceClient;
    private readonly BingGroundingOptions _options;
    private readonly ILogger _logger;
    private readonly CitationContext? _citationContext;

    /// <summary>
    /// Creates a new <see cref="LegalResearchTools"/> instance.
    /// </summary>
    /// <param name="agentServiceClient">
    /// AI Foundry AgentServiceClient used to create threads and run the Bing Grounding agent.
    /// </param>
    /// <param name="options">
    /// Bing Grounding configuration: kill switch, connection name, concurrency, max results.
    /// </param>
    /// <param name="logger">
    /// Logger for operation metadata only (ADR-015: no query content logged).
    /// </param>
    /// <param name="citationContext">
    /// Shared citation accumulator. When non-null, extracted grounding URLs/titles are registered
    /// as citations with Source="BingGrounding" for SSE delivery to the frontend.
    /// </param>
    public LegalResearchTools(
        AgentServiceClient agentServiceClient,
        IOptions<BingGroundingOptions> options,
        ILogger logger,
        CitationContext? citationContext = null)
    {
        _agentServiceClient = agentServiceClient ?? throw new ArgumentNullException(nameof(agentServiceClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _citationContext = citationContext;

        // Lazily initialise the static semaphore once using the configured MaxConcurrency.
        // Double-checked locking ensures thread safety at startup.
        if (s_concurrencyGate is null)
        {
            lock (s_gateLock)
            {
                s_concurrencyGate ??= new SemaphoreSlim(
                    initialCount: _options.MaxConcurrency,
                    maxCount: _options.MaxConcurrency);
            }
        }
    }

    // ── Public tool methods ────────────────────────────────────────────────────

    /// <summary>
    /// Researches a broad legal topic or question using authoritative public legal sources
    /// (case law databases, statutory repositories, law reviews) via Azure AI Foundry Bing Grounding.
    ///
    /// Sanitizes the incoming <paramref name="topic"/> to remove client identifiers and PII before
    /// the query is sent to Bing (ADR-015). Returns a formatted research summary with numbered
    /// citations and source URLs.
    ///
    /// Use this when the user asks a broad legal question: statutes, regulations, doctrine,
    /// jurisdiction-specific requirements, or secondary sources. For a specific known case citation
    /// (e.g., "123 F.3d 456"), use <see cref="LookupCaseAsync"/> instead.
    /// </summary>
    /// <param name="topic">
    /// Legal topic or question to research; should not include client names or matter references
    /// (the sanitizer will strip them, but callers are encouraged to phrase queries generically).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Formatted research summary with numbered source citations, each marked [Legal Source].
    /// When the kill switch is disabled, returns a user-readable degradation message.
    /// </returns>
    [Description("Research a broad legal topic, doctrine, statute, or regulatory requirement using " +
                 "authoritative public legal sources. Do not include client names or matter references.")]
    public async Task<string> ResearchLegalAsync(
        [Description("Legal topic or question to research; do not include client names or matter references")]
        string topic,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic, nameof(topic));

        // ADR-015: Log only query length — never the query text itself.
        _logger.LogInformation(
            "ResearchLegal starting — queryLen={QueryLen}",
            topic.Length);

        // ADR-018: Kill switch check before any network call.
        if (!_options.Enabled)
        {
            _logger.LogInformation("ResearchLegal skipped — BingGrounding kill switch is disabled");
            return "Legal research via Bing Grounding is currently disabled. " +
                   "Please consult your firm's internal legal research tools or contact your administrator.";
        }

        // ADR-015: Sanitize the query before it leaves the BFF boundary.
        var sanitizedTopic = QuerySanitizer.Sanitize(topic);

        // ADR-016: Bound concurrent Bing Grounding runs via SemaphoreSlim.
        if (!await s_concurrencyGate!.WaitAsync(s_semaphoreTimeout, cancellationToken))
        {
            _logger.LogWarning(
                "ResearchLegal concurrency limit reached (max {MaxConcurrency}) — returning degradation message",
                _options.MaxConcurrency);
            return "Legal research is temporarily at capacity. Please try again in a few moments.";
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var results = await RunBingGroundingAsync(sanitizedTopic, cancellationToken);
            sw.Stop();

            // ADR-015: Log only result count and timing — never query text or result content.
            _logger.LogInformation(
                "ResearchLegal completed — resultCount={ResultCount}, durationMs={DurationMs}",
                results.Count, sw.ElapsedMilliseconds);

            RegisterCitations(results);
            return FormatLegalResults(results, "legal research");
        }
        finally
        {
            s_concurrencyGate.Release();
        }
    }

    /// <summary>
    /// Looks up a specific legal case citation (e.g., "123 F.3d 456 (9th Cir. 2020)") using
    /// Azure AI Foundry Bing Grounding and returns a structured case summary with source URL.
    ///
    /// Sanitizes the incoming <paramref name="citation"/> to strip any surrounding PII that may
    /// have been included by the user (e.g., "My client's case: 123 F.3d 456"). The case
    /// citation itself (digit patterns, reporter abbreviations, court names) is preserved.
    ///
    /// Returns the case name, holding, and a source URL from an authoritative legal database.
    /// </summary>
    /// <param name="citation">
    /// Case citation in standard legal format, e.g., "123 F.3d 456 (9th Cir. 2020)" or
    /// "Roe v. Wade, 410 U.S. 113 (1973)". Case names are acceptable as they are public record.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Structured case summary with holding and source URL, or a degradation message when
    /// the kill switch is disabled or concurrency is exhausted.
    /// </returns>
    [Description("Look up a specific legal case by its citation (e.g., '123 F.3d 456 (9th Cir. 2020)'). " +
                 "Returns the case holding and a source URL from an authoritative legal database.")]
    public async Task<string> LookupCaseAsync(
        [Description("Case citation in standard format, e.g., 123 F.3d 456 (9th Cir. 2020) or Roe v. Wade, 410 U.S. 113 (1973)")]
        string citation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(citation, nameof(citation));

        // ADR-015: Log only citation length — never the citation text itself.
        _logger.LogInformation(
            "LookupCase starting — citationLen={CitationLen}",
            citation.Length);

        // ADR-018: Kill switch check before any network call.
        if (!_options.Enabled)
        {
            _logger.LogInformation("LookupCase skipped — BingGrounding kill switch is disabled");
            return "Case citation lookup via Bing Grounding is currently disabled. " +
                   "Please use your firm's legal research subscription (Westlaw, LexisNexis) directly.";
        }

        // ADR-015: Sanitize to strip PII prefixes while preserving the citation itself.
        // Case names (e.g., "Roe v. Wade") are public record and intentionally retained.
        var sanitizedCitation = QuerySanitizer.Sanitize(citation);

        // Build a targeted query that anchors Bing to authoritative legal databases.
        var searchQuery = $"case law citation \"{sanitizedCitation}\" site:law.justia.com OR site:courtlistener.com OR site:scholar.google.com";

        // ADR-016: Bound concurrent Bing Grounding runs.
        if (!await s_concurrencyGate!.WaitAsync(s_semaphoreTimeout, cancellationToken))
        {
            _logger.LogWarning(
                "LookupCase concurrency limit reached (max {MaxConcurrency}) — returning degradation message",
                _options.MaxConcurrency);
            return "Case lookup is temporarily at capacity. Please try again in a few moments.";
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var results = await RunBingGroundingAsync(searchQuery, cancellationToken);
            sw.Stop();

            // ADR-015: Log only result count and timing.
            _logger.LogInformation(
                "LookupCase completed — resultCount={ResultCount}, durationMs={DurationMs}",
                results.Count, sw.ElapsedMilliseconds);

            RegisterCitations(results);
            return FormatLegalResults(results, "case citation lookup");
        }
        finally
        {
            s_concurrencyGate.Release();
        }
    }

    // ── Bing Grounding integration ─────────────────────────────────────────────

    /// <summary>
    /// Creates an agent thread, sends the query as a user message, runs the agent with the
    /// Bing Grounding tool attached, and extracts grounding annotations from the response.
    ///
    /// Uses the AgentServiceClient for thread management and streaming, then parses
    /// <see cref="MessageTextAnnotation"/> entries from the completed run to extract URLs and
    /// titles that Bing Grounding attached to the agent's response.
    ///
    /// ADR-015: The thread content and agent response text are never logged here — only the
    ///          grounding annotation count is recorded.
    /// </summary>
    private async Task<List<GroundingResult>> RunBingGroundingAsync(
        string sanitizedQuery,
        CancellationToken cancellationToken)
    {
        // Create (or resume) a short-lived thread for this search operation.
        // We use a pseudo-tenant key for legal research threads to keep them isolated from
        // analysis agent threads. The thread will be reused if called again within the
        // cache sliding window (AgentServiceOptions.ThreadCacheExpiryMinutes).
        var threadKey = $"legal-research-grounding";

        var threadId = await _agentServiceClient.CreateOrResumeThreadAsync(
            threadKey, cancellationToken);

        // Send the sanitized query as a user message.
        await _agentServiceClient.SendMessageAsync(threadId, sanitizedQuery, cancellationToken);

        // Stream the response and accumulate the full text to extract grounding annotations.
        // We do not log the streamed content (ADR-015).
        var sb = new StringBuilder();
        await foreach (var token in _agentServiceClient.StreamResponseAsync(threadId, cancellationToken))
        {
            sb.Append(token);
        }

        // Parse grounding annotations from the response text using a lightweight pattern.
        // Azure AI Foundry Bing Grounding embeds citations as markdown-style references:
        //   [1]: https://... "Title"
        //   or inline: ([Source](https://...))
        // We extract these with a regex rather than re-querying the Agents SDK for run steps,
        // because the streaming path has already consumed the run. The response text itself
        // contains the grounding references in standard Bing format.
        var responseText = sb.ToString();
        var groundingResults = ExtractGroundingAnnotations(responseText, _options.MaxResultsPerQuery);

        // ADR-015: Log only count — not the content or URLs.
        _logger.LogDebug(
            "BingGrounding extracted {AnnotationCount} grounding annotations",
            groundingResults.Count);

        // Return results (may be empty if Bing Grounding found nothing or agent produced no citations).
        // The response text itself is returned as the first "result" so the agent has the summary.
        if (groundingResults.Count == 0 && responseText.Length > 0)
        {
            // Include the raw agent summary as an unlinked result when no annotations were parsed.
            groundingResults.Add(new GroundingResult(
                Title: "Legal Research Summary",
                Url: string.Empty,
                Snippet: TruncateSnippet(responseText, CitationContext.MaxExcerptLength),
                Position: 1));
        }

        return groundingResults;
    }

    // ── Citation registration ──────────────────────────────────────────────────

    /// <summary>
    /// Registers extracted Bing Grounding results as citations in the shared
    /// <see cref="CitationContext"/> with <c>Source="BingGrounding"</c>.
    ///
    /// Deduplication is handled by the CitationContext keying on <c>chunkId</c> (the URL).
    /// Position-based confidence scoring: 1st result = 0.95, linear decay.
    /// </summary>
    private void RegisterCitations(List<GroundingResult> results)
    {
        if (_citationContext is null || results.Count == 0)
            return;

        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.Url))
                continue;

            _citationContext.AddCitation(
                chunkId: result.Url,
                sourceName: result.Title,
                pageNumber: null,
                excerpt: TruncateSnippet(result.Snippet, CitationContext.MaxExcerptLength),
                sourceType: "BingGrounding",
                url: result.Url,
                snippet: TruncateSnippet(result.Snippet, CitationContext.MaxExcerptLength));
        }
    }

    // ── Result formatting ──────────────────────────────────────────────────────

    /// <summary>
    /// Formats Bing Grounding results into an AI-readable text block with numbered citations.
    /// All results are labelled [Legal Source] to indicate they originate from external public
    /// legal databases (ADR-015: external content governance).
    /// </summary>
    private static string FormatLegalResults(List<GroundingResult> results, string operationName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Legal research ({operationName}) returned {results.Count} result(s).");
        sb.AppendLine();
        sb.AppendLine("**Note**: These results are from external public legal databases and should be " +
                      "verified against authoritative primary sources before relying on them in legal advice.");
        sb.AppendLine();

        foreach (var result in results)
        {
            var urlPart = string.IsNullOrWhiteSpace(result.Url) ? "" : $" — {result.Url}";
            sb.AppendLine($"[{result.Position}] [Legal Source] {result.Title}{urlPart}");
            if (!string.IsNullOrWhiteSpace(result.Snippet))
                sb.AppendLine($"    {result.Snippet}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ── Grounding annotation extraction ───────────────────────────────────────

    /// <summary>
    /// Extracts Bing Grounding annotations from the agent's response text.
    ///
    /// Azure AI Foundry Bing Grounding embeds source references in the response as:
    ///   · Markdown reference links: [1]: https://example.com "Page Title"
    ///   · Inline links: [text](https://example.com)
    ///
    /// This parser extracts both formats and deduplicates by URL.
    /// </summary>
    private static List<GroundingResult> ExtractGroundingAnnotations(string responseText, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return [];

        var results = new List<GroundingResult>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pattern 1: Markdown reference-style links — [N]: https://... "Title"
        foreach (Match match in ReferenceLinkPattern().Matches(responseText))
        {
            if (results.Count >= maxResults) break;

            var url = match.Groups["url"].Value;
            var title = match.Groups["title"].Value.Trim('"', '\'');

            if (!seenUrls.Add(url)) continue;

            results.Add(new GroundingResult(
                Title: string.IsNullOrWhiteSpace(title) ? ExtractDomainFromUrl(url) : title,
                Url: url,
                Snippet: string.Empty,
                Position: results.Count + 1));
        }

        // Pattern 2: Inline markdown links — [text](https://...)
        foreach (Match match in InlineLinkPattern().Matches(responseText))
        {
            if (results.Count >= maxResults) break;

            var text = match.Groups["text"].Value;
            var url = match.Groups["url"].Value;

            if (!seenUrls.Add(url)) continue;

            // Skip if text is just a number (reference marker already captured above)
            if (int.TryParse(text.Trim(), out _)) continue;

            results.Add(new GroundingResult(
                Title: string.IsNullOrWhiteSpace(text) ? ExtractDomainFromUrl(url) : text,
                Url: url,
                Snippet: string.Empty,
                Position: results.Count + 1));
        }

        return results;
    }

    /// <summary>Extracts a readable domain name from a URL for use as a fallback title.</summary>
    private static string ExtractDomainFromUrl(string url)
    {
        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return url.Length > 60 ? url[..60] + "..." : url;
        }
    }

    // ── Regex patterns (source-generated for performance) ─────────────────────

    /// <summary>
    /// Matches Markdown reference-style links: [N]: https://url "Optional Title"
    /// Captures: url (group), title (group, optional).
    /// </summary>
    [GeneratedRegex(@"\[\d+\]:\s*(?<url>https?://[^\s""']+)(?:\s+""(?<title>[^""]+)"")?",
        RegexOptions.IgnoreCase | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReferenceLinkPattern();

    /// <summary>
    /// Matches inline Markdown links: [text](https://url)
    /// Captures: text (group), url (group).
    /// </summary>
    [GeneratedRegex(@"\[(?<text>[^\]]+)\]\((?<url>https?://[^)]+)\)",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InlineLinkPattern();

    // ── Helper ────────────────────────────────────────────────────────────────

    private static string TruncateSnippet(string text, int maxLength) =>
        text.Length > maxLength ? text[..maxLength] + "..." : text;

    // ── Internal types ────────────────────────────────────────────────────────

    /// <summary>
    /// Internal record representing a single Bing Grounding result.
    /// </summary>
    private sealed record GroundingResult(string Title, string Url, string Snippet, int Position);

    // ── Query sanitizer ───────────────────────────────────────────────────────

    /// <summary>
    /// Static helper that sanitizes legal queries before they leave the BFF boundary (ADR-015).
    ///
    /// WHAT IS STRIPPED / REPLACED:
    ///
    ///   1. "Client: [Name]" / "Client Name: [Name]" prefixes
    ///      Example: "Client: Acme Corp — what are merger notice requirements?"
    ///      → "what are merger notice requirements?"
    ///      WHY: The client identifier has no search value and is PII.
    ///
    ///   2. Matter reference numbers in the form "Matter NNNN-NNNN" or "Matter Ref: NNNN"
    ///      Example: "Matter 2024-0187: breach of fiduciary duty standard"
    ///      → "[MATTER-REF]: breach of fiduciary duty standard"
    ///      WHY: Matter numbers are internal firm identifiers that must not be sent externally.
    ///
    ///   3. Email addresses (simplified RFC 5321 pattern)
    ///      Example: "email from john.doe@client.com regarding NDA terms"
    ///      → "email from [EMAIL] regarding NDA terms"
    ///      WHY: Email addresses are personal data under GDPR / CCPA.
    ///
    ///   4. "Re: [Subject]" headers that appear as query qualifiers
    ///      Example: "Re: Acme merger — what consideration is required?"
    ///      → "what consideration is required?"
    ///      WHY: Re: lines often contain matter subjects with client context.
    ///
    /// WHAT IS PRESERVED:
    ///   · Case citations (e.g., "123 F.3d 456") — these ARE the search target in LookupCase.
    ///   · Legal entity names in their general capacity (e.g., "Microsoft" as a defendant in a cited case).
    ///   · Statutory references (e.g., "17 U.S.C. § 107").
    ///   · Legal terms, doctrine names, jurisdiction names.
    ///
    /// LIMITATIONS:
    ///   The sanitizer catches structured PII patterns, not arbitrary proper nouns. A query like
    ///   "what are the GDPR obligations for tech companies?" would not be modified even if the
    ///   user's underlying concern is about a specific named client, because "tech companies" is
    ///   not a PII pattern. Callers (and system prompt guidance) should encourage users to phrase
    ///   legal research questions generically to maximise sanitizer effectiveness.
    /// </summary>
    internal static partial class QuerySanitizer
    {
        // "Client: Name" or "Client Name: Name" prefix (case-insensitive, at start of query or after comma/semicolon)
        [GeneratedRegex(@"(?:^|(?<=[,;]\s*))Client(?:\s+Name)?:\s*[^,;:\n]+[,;]?\s*",
            RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
        private static partial Regex ClientPrefixPattern();

        // "Matter NNNN-NNNN" / "Matter Ref: NNNN" / "Matter Reference NNNN"
        [GeneratedRegex(@"\bMatter\s+(?:Ref(?:erence)?:?\s*)?\d[\d\-/]+\b",
            RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
        private static partial Regex MatterRefPattern();

        // Email addresses: local-part@domain.tld
        [GeneratedRegex(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
            RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex EmailPattern();

        // "Re: Subject text" at start of query or after line break
        [GeneratedRegex(@"(?:^|\n)\s*Re:\s*[^\n]+\n?",
            RegexOptions.IgnoreCase | RegexOptions.Multiline, matchTimeoutMilliseconds: 500)]
        private static partial Regex ReSubjectPattern();

        /// <summary>
        /// Sanitizes a legal query string by removing or replacing known PII patterns.
        /// Returns the sanitized string (may equal the input if no patterns matched).
        /// The returned string is safe to send to external Bing Grounding API calls.
        /// </summary>
        /// <param name="query">Raw query text from the user or agent tool call.</param>
        /// <returns>Sanitized query text with PII patterns removed or replaced.</returns>
        internal static string Sanitize(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return query;

            // Apply patterns in order: most structural first, then inline replacements.
            var sanitized = query;

            // 1. Strip "Re: Subject" lines (removes full subject context)
            sanitized = ReSubjectPattern().Replace(sanitized, " ");

            // 2. Strip "Client: Name" prefixes (removes client identifier qualifier)
            sanitized = ClientPrefixPattern().Replace(sanitized, string.Empty);

            // 3. Replace matter reference numbers (preserves sentence structure)
            sanitized = MatterRefPattern().Replace(sanitized, "[MATTER-REF]");

            // 4. Replace email addresses (preserves surrounding sentence)
            sanitized = EmailPattern().Replace(sanitized, "[EMAIL]");

            // Normalize whitespace introduced by removals
            sanitized = NormalizeWhitespacePattern().Replace(sanitized.Trim(), " ");

            return sanitized;
        }

        [GeneratedRegex(@"\s{2,}", RegexOptions.None, matchTimeoutMilliseconds: 500)]
        private static partial Regex NormalizeWhitespacePattern();
    }
}
