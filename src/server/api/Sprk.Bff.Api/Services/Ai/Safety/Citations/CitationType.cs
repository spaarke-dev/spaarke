namespace Sprk.Bff.Api.Services.Ai.Safety.Citations;

/// <summary>
/// Classifies the type of a legal citation extracted from LLM-generated text.
/// Used by <see cref="CitationExtractor"/> to route citations to the correct
/// <see cref="IVerificationProvider"/> implementation.
/// </summary>
public enum CitationType
{
    /// <summary>
    /// Case law citation (e.g. "Smith v. Jones, 542 U.S. 296 (2004)").
    /// </summary>
    CaseLaw,

    /// <summary>
    /// US statute / code citation (e.g. "35 U.S.C. § 101").
    /// </summary>
    Statute,

    /// <summary>
    /// Patent citation (e.g. "U.S. Patent No. 9,123,456" or "WO2021/123456").
    /// </summary>
    Patent,

    /// <summary>
    /// SEC filing citation (e.g. "Form 10-K", "Form 8-K" with company identifier).
    /// </summary>
    SecFiling,

    /// <summary>
    /// Federal regulation citation (e.g. "47 C.F.R. § 73.3999").
    /// </summary>
    Regulation,

    /// <summary>
    /// The citation could not be classified into any known category.
    /// Citations of this type are returned with <c>IsVerified = false</c>
    /// and <c>VerificationProvider = "none"</c>.
    /// </summary>
    Unknown,
}
