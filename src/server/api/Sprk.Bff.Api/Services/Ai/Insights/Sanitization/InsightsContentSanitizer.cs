using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.Insights.Sanitization;

/// <summary>
/// Default Phase 1 minimal-viable implementation of <see cref="IInsightsContentSanitizer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Algorithm</b> (deterministic, mechanical, zero-LLM):
/// <list type="number">
///   <item>Null/whitespace → empty result (not an error).</item>
///   <item>Strip control characters (U+0000–U+001F except tab/newline/CR; U+007F–U+009F).</item>
///   <item>Collapse runs of internal whitespace to single spaces but preserve newlines.</item>
///   <item>Trim leading/trailing whitespace.</item>
///   <item>Detect + strip recognized prompt-injection prefixes (see <see cref="InjectionPrefixRegex"/>).</item>
///   <item>If length exceeds <see cref="MaxLength"/>, truncate to <see cref="MaxLength"/> chars.</item>
/// </list>
/// </para>
/// <para>
/// <b>What we DON'T do</b> (intentional): no case normalization, no Unicode normalization
/// beyond control-character stripping, no quote-character normalization. The downstream
/// <c>GroundingVerifier</c> needs verbatim substring matching against this text — anything
/// that alters substantive characters would defeat grounding verification.
/// </para>
/// <para>
/// <b>Registered Singleton</b> (stateless; only <see cref="ILogger{T}"/> dependency).
/// </para>
/// </remarks>
internal sealed partial class InsightsContentSanitizer : IInsightsContentSanitizer
{
    /// <summary>
    /// Hard upper bound on sanitized content length. 200K chars (~50K tokens) is enough
    /// for any reasonable legal document chunk while bounding the worst-case prompt cost
    /// per ingest call. Documents larger than this are truncated; the truncation is logged
    /// so the D-P11 review surface can flag mis-extracted (e.g., binary content
    /// mis-classified as text) inputs.
    /// </summary>
    internal const int MaxLength = 200_000;

    /// <summary>
    /// Structured event id for App Insights queries. Stable id lets dashboards group
    /// sanitization events (KQL: <c>traces | where customDimensions.EventId == 8040</c>).
    /// Chosen as 8040 = "8" extraction-primitives bucket + "040" (this task id).
    /// </summary>
    private static readonly EventId SanitizationAppliedEvent = new(8040, "InsightsContentSanitized");

    /// <summary>
    /// Single compiled multi-pattern regex for prompt-injection prefix detection.
    /// Source-generated regex; compiled once at startup. Matches at start-of-text only
    /// (so we don't strip legitimate uses mid-document). Recognized prefixes cover the
    /// most common LLM-jailbreak patterns observed in the wild as of 2026-05.
    /// </summary>
    [GeneratedRegex(
        @"^ignore\s+(all\s+)?(previous|prior|above)\s+instructions[^\n]*|" +
        @"^disregard\s+(all\s+)?(previous|prior|above)\s+instructions[^\n]*|" +
        @"^new\s+instructions\s*:[^\n]*|" +
        @"^system\s*:[^\n]*\b(ignore|override|forget)\b[^\n]*|" +
        @"^you\s+are\s+now[^\n]*\b(jailbroken|unrestricted|dan)\b[^\n]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InjectionPrefixRegex();

    private readonly ILogger<InsightsContentSanitizer> _logger;

    public InsightsContentSanitizer(ILogger<InsightsContentSanitizer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<SanitizationResult> SanitizeAsync(string? rawText, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return Task.FromResult(new SanitizationResult(
                SanitizedText: string.Empty,
                OriginalLength: rawText?.Length ?? 0,
                SanitizedLength: 0,
                WasTruncated: false,
                HadInjectionPrefix: false));
        }

        var originalLength = rawText.Length;

        // Step 1: strip control characters except whitespace we want to preserve.
        var stripped = StripControlCharacters(rawText);

        // Step 2: collapse internal whitespace (spaces/tabs), preserving newlines.
        var collapsed = CollapseInternalWhitespace(stripped);

        // Step 3: trim.
        var trimmed = collapsed.Trim();

        // Step 4: detect + strip injection prefix.
        var hadInjection = false;
        var injectionMatch = InjectionPrefixRegex().Match(trimmed);
        if (injectionMatch.Success)
        {
            hadInjection = true;
            trimmed = trimmed[injectionMatch.Length..].TrimStart();
        }

        // Step 5: truncate to MaxLength.
        var wasTruncated = false;
        if (trimmed.Length > MaxLength)
        {
            wasTruncated = true;
            trimmed = trimmed[..MaxLength];
        }

        var result = new SanitizationResult(
            SanitizedText: trimmed,
            OriginalLength: originalLength,
            SanitizedLength: trimmed.Length,
            WasTruncated: wasTruncated,
            HadInjectionPrefix: hadInjection);

        // Log only when sanitization actually changed something — quiet for normal inputs,
        // visible for inputs that look adversarial or malformed.
        if (wasTruncated || hadInjection || originalLength != trimmed.Length)
        {
            _logger.Log(
                LogLevel.Information,
                SanitizationAppliedEvent,
                "InsightsContentSanitizer applied: originalLength={OriginalLength} sanitizedLength={SanitizedLength} truncated={WasTruncated} hadInjectionPrefix={HadInjectionPrefix}",
                originalLength,
                trimmed.Length,
                wasTruncated,
                hadInjection);
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Strips control characters except tab, newline, and carriage return (which carry
    /// document structure). Operates in O(n) time over the input.
    /// </summary>
    private static string StripControlCharacters(string input)
    {
        // Pre-size at input length (output is the same length or smaller).
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            // Preserve tab (U+0009), LF (U+000A), CR (U+000D).
            if (ch == '\t' || ch == '\n' || ch == '\r')
            {
                sb.Append(ch);
                continue;
            }
            // Strip C0 control set (U+0000–U+001F) and C1 control set (U+007F–U+009F).
            if (ch < ' ' || (ch >= '\u007F' && ch <= '\u009F'))
            {
                continue;
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Collapses runs of spaces/tabs to a single space; preserves newlines (LF/CR) and
    /// blank-line structure (a run of \n stays as \n+).
    /// </summary>
    private static string CollapseInternalWhitespace(string input)
    {
        var sb = new StringBuilder(input.Length);
        var lastWasInlineSpace = false;
        foreach (var ch in input)
        {
            if (ch == '\n' || ch == '\r')
            {
                sb.Append(ch);
                lastWasInlineSpace = false;
                continue;
            }
            if (ch == ' ' || ch == '\t')
            {
                if (!lastWasInlineSpace)
                {
                    sb.Append(' ');
                    lastWasInlineSpace = true;
                }
                continue;
            }
            sb.Append(ch);
            lastWasInlineSpace = false;
        }
        return sb.ToString();
    }
}
