namespace Sprk.Bff.Api.Services.Ai.Safety.Citations;

/// <summary>
/// Represents a single citation extracted from LLM-generated text.
/// </summary>
/// <param name="RawText">
/// The verbatim text matched by the extraction regex (e.g. "Smith v. Jones, 542 U.S. 296 (2004)").
/// </param>
/// <param name="CitationType">
/// The classified type of the citation, determined by <see cref="CitationExtractor"/>.
/// </param>
/// <param name="NormalizedKey">
/// A canonical identifier for the citation, suitable for deduplication and provider lookup.
/// Examples:
///   CaseLaw  → "542 U.S. 296"
///   Statute  → "35 U.S.C. § 101"
///   Patent   → "US9123456"
///   SecFiling→ "10-K"
///   Regulation → "47 C.F.R. § 73.3999"
/// </param>
public sealed record Citation(
    string RawText,
    CitationType CitationType,
    string NormalizedKey);
