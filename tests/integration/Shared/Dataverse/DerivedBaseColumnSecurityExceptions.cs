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

            // #2799 / #2859 (unified-access-control-r2): the invoice family's core-record lookups and invoice links
            // (scripts/common/InvoiceFamilyLockedColumns.psd1) are secured so only the BFF application user writes them. Every user
            // reads them ("Spaarke BFF-Managed Field Readers", on every business unit's default team); the evaluator's identity, the
            // BFF application identity the rollup reads with, holds read in "Spaarke BFF-Managed Field Writers" (read/create/update).
            [("sprk_invoice", "sprk_matter")] = "#2799: secured for write integrity; every user reads it (Readers), and the evaluator's identity (the BFF application user) reads it through the Writers profile",
            [("sprk_invoice", "sprk_project")] = "#2799: secured for write integrity; every user reads it (Readers), and the evaluator's identity (the BFF application user) reads it through the Writers profile",
            [("sprk_invoiceapproval", "sprk_invoice")] = "#2799: secured for write integrity; every user reads it (Readers), and the evaluator's identity (the BFF application user) reads it through the Writers profile",
            [("sprk_invoicesubmission", "sprk_invoice")] = "#2799: secured for write integrity; every user reads it (Readers), and the evaluator's identity (the BFF application user) reads it through the Writers profile",
        };
}
