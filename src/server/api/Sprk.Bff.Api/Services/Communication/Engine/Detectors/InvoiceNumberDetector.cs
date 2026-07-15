using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Detectors;

/// <summary>
/// Detects an invoice-number reference in the subject/body (e.g. <c>Invoice #12345</c>, <c>INV-12345</c>,
/// <c>Invoice No. 12345</c>). Resolves the <c>invoice</c> category.
/// </summary>
/// <remarks>
/// Resolving the specific <c>sprk_invoice</c> record (by invoice number) is a follow-up needing a
/// <c>QueryInvoiceByNumber</c> query + confirmed number-field; task 014 resolves the category + records
/// the extracted number in provenance.
/// </remarks>
public sealed class InvoiceNumberDetector : IStructuralDetector
{
    public string Name => "invoice-number";

    private static readonly Regex[] InvoicePatterns =
    [
        new(@"\bINV-(\d{3,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new(@"\bInvoice\s*#\s*(\d{3,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new(@"\bInvoice\s*(?:No\.?|Number)\s*:?\s*(\d{3,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
    ];

    public StructuralMatch? Detect(NormalizedMessage message)
    {
        var text = DetectorText.Of(message);
        if (string.IsNullOrEmpty(text))
            return null;

        foreach (var pattern in InvoicePatterns)
        {
            try
            {
                var match = pattern.Match(text);
                if (match.Success)
                {
                    var number = match.Groups[1].Value;
                    return new StructuralMatch
                    {
                        Category = "invoice",
                        Obligations = new[] { "invoice-review" },
                        Confidence = 0.80,
                        Provenance = $"structural:invoice-number:{number}",
                    };
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Skip this pattern on timeout.
            }
        }

        return null;
    }
}
