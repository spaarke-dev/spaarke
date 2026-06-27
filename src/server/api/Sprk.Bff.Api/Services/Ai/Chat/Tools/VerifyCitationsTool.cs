using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Sprk.Bff.Api.Services.Ai.Safety.Citations;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing explicit citation verification to the SprkChat agent.
///
/// Exposes one method registered as an <see cref="Microsoft.Extensions.AI.AIFunction"/> via
/// <see cref="Microsoft.Extensions.AI.AIFunctionFactory.Create"/>:
///   - <see cref="VerifyCitationsAsync"/> — extracts and verifies all legal citations in the
///     provided text against the registered verification providers.
///
/// The LLM invokes this tool by name ("verify_citations") when the user asks to verify
/// references, check case validity, or confirm regulatory citations in a passage of text.
/// For the automatic post-LLM check mode (which runs after every response without LLM
/// invocation), see <see cref="CitationSafetyCheck"/>.
///
/// Instantiated by <see cref="SprkChatAgentFactory"/>. Not registered in DI — the factory
/// creates instances and registers methods as <see cref="Microsoft.Extensions.AI.AIFunction"/>
/// objects via <see cref="Microsoft.Extensions.AI.AIFunctionFactory.Create"/>.
///
/// ADR-010: 0 additional DI registrations — factory-instantiated only.
/// ADR-013: AI tools use AIFunctionFactory.Create pattern; no separate AI microservice.
/// ADR-015: Citation text is NEVER logged — only counts, types, and outcomes.
/// </summary>
public sealed class VerifyCitationsTool
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ICitationVerificationService _verificationService;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new <see cref="VerifyCitationsTool"/> instance.
    /// </summary>
    /// <param name="verificationService">
    /// Citation verification service that extracts and verifies legal citations against
    /// registered <see cref="IVerificationProvider"/> implementations.
    /// </param>
    /// <param name="logger">
    /// Logger for operation metadata only (ADR-015: no citation text logged).
    /// </param>
    public VerifyCitationsTool(
        ICitationVerificationService verificationService,
        ILogger logger)
    {
        _verificationService = verificationService ?? throw new ArgumentNullException(nameof(verificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Public tool method ─────────────────────────────────────────────────────

    /// <summary>
    /// Verifies legal citations found in the provided text against authoritative sources.
    /// Returns verification status, confidence, and source URLs for each citation.
    ///
    /// Use this when the user asks to verify references, check case validity, or confirm
    /// regulatory citations in a passage of text.
    /// </summary>
    /// <param name="text">
    /// The passage of text from which to extract and verify legal citations. May include
    /// case law (e.g., "542 U.S. 296"), statutes (e.g., "35 U.S.C. § 101"), patents,
    /// SEC filings (e.g., "Form 10-K"), or federal regulations (e.g., "47 C.F.R. § 73.3999").
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// JSON string containing an array of citation verification results. Each element includes:
    ///   - raw: the verbatim citation text as extracted
    ///   - type: citation type (CaseLaw, Statute, Patent, SecFiling, Regulation, Unknown)
    ///   - normalized: canonical identifier for the citation
    ///   - isVerified: whether the citation was confirmed by an authoritative provider
    ///   - confidence: provider confidence score [0.0, 1.0]
    ///   - sourceUrl: canonical URL at the verification provider, or null
    ///   - provider: name of the provider that handled the citation, "none", or "error"
    ///
    /// When no citations are found in the text, returns a JSON object with an informational
    /// message and an empty citations array.
    /// </returns>
    [Description("Verifies legal citations found in the provided text against authoritative sources. " +
                 "Returns verification status, confidence, and source URLs for each citation. " +
                 "Use when the user asks to verify references, check case validity, or confirm " +
                 "regulatory citations.")]
    public async Task<string> VerifyCitationsAsync(
        [Description("The text passage containing the legal citations to extract and verify. " +
                     "Supports case law, statutes, patents, SEC filings, and federal regulations.")]
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(text));

        // ADR-015: log only text length — never the text content itself.
        _logger.LogInformation(
            "VerifyCitations: invoked via LLM tool call — textLen={TextLen}",
            text.Length);

        var report = await _verificationService.VerifyAllAsync(text, cancellationToken)
            .ConfigureAwait(false);

        if (report.TotalCitations == 0)
        {
            _logger.LogInformation("VerifyCitations: no citations found in text ({CharCount} chars).", text.Length);
            return JsonSerializer.Serialize(new
            {
                message = "No legal citations were found in the provided text.",
                citations = Array.Empty<object>()
            }, JsonOptions);
        }

        // ADR-015: log only counts and outcome categories — not citation text.
        _logger.LogInformation(
            "VerifyCitations: verified={Verified}, unverified={Unverified}, errors={Errors}",
            report.Verified.Count, report.Unverified.Count, report.Errors.Count);

        var results = report.All.Select(r => new
        {
            raw = r.Citation.RawText,
            type = r.Citation.CitationType.ToString(),
            normalized = r.Citation.NormalizedKey,
            isVerified = r.IsVerified,
            confidence = r.ConfidenceScore,
            sourceUrl = r.SourceUrl,
            provider = r.VerificationProvider,
            errorMessage = r.ErrorMessage
        }).ToArray();

        return JsonSerializer.Serialize(new { citations = results }, JsonOptions);
    }

    // ── Formatted text result (internal helper for FormatReport) ───────────────

    /// <summary>
    /// Formats a <see cref="CitationVerificationReport"/> into a human-readable string
    /// suitable for inclusion in an LLM response.
    ///
    /// Used when the tool result needs to be presented as markdown text rather than
    /// raw JSON (e.g., when the agent is building a visible response to the user).
    /// </summary>
    internal static string FormatReport(CitationVerificationReport report)
    {
        if (report.TotalCitations == 0)
            return "No legal citations were found in the provided text.";

        var sb = new StringBuilder();
        sb.AppendLine($"Citation verification complete: **{report.Verified.Count} verified**, " +
                      $"{report.Unverified.Count} unverified, {report.Errors.Count} errors " +
                      $"out of {report.TotalCitations} total citations.");
        sb.AppendLine();

        if (report.Verified.Count > 0)
        {
            sb.AppendLine("### Verified Citations");
            foreach (var r in report.Verified)
            {
                sb.Append($"- ✔ **{r.Citation.RawText}** ({r.Citation.CitationType})");
                if (!string.IsNullOrWhiteSpace(r.SourceUrl))
                    sb.Append($" — [Source]({r.SourceUrl})");
                sb.AppendLine($" (confidence: {r.ConfidenceScore:P0})");
            }
            sb.AppendLine();
        }

        if (report.Unverified.Count > 0)
        {
            sb.AppendLine("### Unverified Citations");
            foreach (var r in report.Unverified)
            {
                var reason = r.VerificationProvider == "none"
                    ? "no verification provider available for this citation type"
                    : "provider returned unverified";
                sb.AppendLine($"- ⚠ **{r.Citation.RawText}** ({r.Citation.CitationType}) — {reason}");
            }
            sb.AppendLine();
        }

        if (report.Errors.Count > 0)
        {
            sb.AppendLine("### Verification Errors");
            foreach (var r in report.Errors)
            {
                sb.AppendLine($"- ✗ **{r.Citation.RawText}** ({r.Citation.CitationType}) — error during verification");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
