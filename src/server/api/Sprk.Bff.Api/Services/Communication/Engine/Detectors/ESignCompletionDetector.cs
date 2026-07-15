using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Detectors;

/// <summary>
/// Detects an e-signature completion notice (DocuSign / Adobe Acrobat Sign "completed" signals).
/// Resolves the <c>esign-completion</c> category + an <c>executed-document</c> obligation.
/// </summary>
public sealed class ESignCompletionDetector : IStructuralDetector
{
    public string Name => "esign-completion";

    // Strong completion signals — require an e-sign provider/context AND a completion verb to avoid
    // firing on ordinary "completed" prose.
    private static readonly Regex CompletionPattern = new(
        @"\b(docusign|adobe\s+sign|adobe\s+acrobat\s+sign|echosign)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static readonly Regex CompletedVerb = new(
        @"\b(completed|has\s+been\s+signed|all\s+parties\s+have\s+signed|signing\s+complete|envelope\s+completed|fully\s+executed)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public StructuralMatch? Detect(NormalizedMessage message)
    {
        var text = DetectorText.Of(message);
        if (string.IsNullOrEmpty(text))
            return null;

        try
        {
            // Require BOTH a named e-sign provider AND a completion verb. Requiring the provider avoids
            // false positives on ordinary legal prose ("the fully executed agreement is attached").
            if (!CompletionPattern.IsMatch(text) || !CompletedVerb.IsMatch(text))
                return null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        return new StructuralMatch
        {
            Category = "esign-completion",
            Obligations = new[] { "executed-document" },
            Confidence = 0.88,
            Provenance = "structural:esign-completion",
        };
    }
}
