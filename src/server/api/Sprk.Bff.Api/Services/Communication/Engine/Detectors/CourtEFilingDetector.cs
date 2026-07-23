using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Detectors;

/// <summary>
/// Detects a court / e-filing notice (e-filing confirmations, notices of hearing, docket/case-number
/// references). Resolves the <c>court-notice</c> category + a <c>deadline-response</c> obligation.
/// </summary>
public sealed class CourtEFilingDetector : IStructuralDetector
{
    public string Name => "court-efiling";

    // e-filing systems + court-notice phrasing. Require a court/e-filing context term to avoid firing
    // on ordinary "notice" prose.
    private static readonly Regex EFilingPattern = new(
        @"\b(e-?fil(?:e|ing|ed)|notice\s+of\s+(?:electronic\s+)?filing|court\s+e-?filing|odyssey\s+efile|pacer|nef)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    // Court-notice phrasing. The case-number alternative requires a DIGIT-bearing token so ordinary
    // prose ("case number is pending", "the district court ruled") does not match; bare court names
    // were intentionally dropped as too prose-prone (S3 tuning).
    private static readonly Regex CourtNoticePattern = new(
        @"\b(notice\s+of\s+hearing|case\s+(?:no\.?|number)\s*[:#]?\s*[\w.\-]*\d|docket\s+(?:no\.?|number)\s*[:#]?\s*[\w.\-]*\d)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public StructuralMatch? Detect(NormalizedMessage message)
    {
        var text = DetectorText.Of(message);
        if (string.IsNullOrEmpty(text))
            return null;

        try
        {
            if (!EFilingPattern.IsMatch(text) && !CourtNoticePattern.IsMatch(text))
                return null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        return new StructuralMatch
        {
            Category = "court-notice",
            Obligations = new[] { "deadline-response" },
            Confidence = 0.80,
            Provenance = "structural:court-efiling",
        };
    }
}
