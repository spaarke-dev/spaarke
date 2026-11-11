/// <summary>
/// The ONE reviewed list of derived base columns (<c>FinanceBaseValues.DerivedBaseColumns</c>) that are field-secured on purpose
/// (ebilling-r1 task 166, EB-D-200; owner + uac-r2 option D). Shared by the offline guard
/// (<c>DerivedBaseColumnsFieldSecurityTests</c>, the SpaarkeMaster source) and the live guard
/// (<c>SignalPredicateTests.AuthorableColumns_ExistAndAreNotFieldSecured_OnTheLiveSchema</c>, the live schema), so the two cannot disagree.
/// A listed column must be secured in both; every other derived base column must be unsecured in both.
/// </summary>
/// <remarks>
/// Reviewed by ontology-r1 (2026-11-06). Each listed column is secured for write integrity; every user reads it, and the evaluator's
/// identity is granted read by the profile that covers it. A different evaluator identity would need the same read grant.
/// Global namespace, like <see cref="RecordTypeCatalogSetup"/>.
/// </remarks>
public static class DerivedBaseColumnSecurityExceptions
{
    /// <summary>Derived base columns that ARE field-secured, each with the reason the evaluator still reads them.</summary>
    public static readonly IReadOnlyDictionary<(string Entity, string Column), string> SecuredForTheBffWriters =
        new Dictionary<(string, string), string>
        {
            [("sprk_invoice", "sprk_totalamount")] = "EB-D-200: secured for write integrity; every user reads it, and the evaluator's identity is granted read by the profile that covers it",
            [("sprk_invoice", "sprk_approvedtotal")] = "EB-D-200: secured for write integrity; every user reads it, and the evaluator's identity is granted read by the profile that covers it",
        };
}
