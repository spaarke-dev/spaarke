namespace Sprk.Bff.Api.Services.Finance.Models;

/// <summary>
/// The choices of the global option set <c>sprk_billingapproverrole</c> (D-33): which assignment field of a core record holds its
/// invoice approvers. The numeric values are the Dataverse option values.
/// </summary>
public enum BillingApproverRole
{
    AssignedAttorney1 = 100000000,
    AssignedAttorney2 = 100000001,
    AssignedParalegal1 = 100000002,
    AssignedParalegal2 = 100000003,
    AssignedToInternal = 100000004,
    AssignedToExternal = 100000005,
    AssignedTo = 100000006,
}

/// <summary>
/// The eBilling business settings (FR-48, D-59): the typed view of the one <c>sprk_ebillingsetting</c> row, read through
/// <see cref="IEbillingSettingsReader"/>. Immutable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Defaults.</b> Every property's initial value equals the value the schema script
/// (<c>scripts/ebilling/Deploy-EbillingSettingsSchema.ps1</c>) seeds into the row, so a missing row, or a cleared column on the row,
/// behaves like the seeded record. The exceptions are <see cref="ApExportMappingJson"/> (<c>null</c>: the AP export uses its own
/// built-in D-56 default mapping, task 150) and <see cref="DefaultCodeSetId"/> (<c>null</c>: the code-set resolver falls back to the
/// "UTBMS Litigation" set by name, task 047).
/// </para>
/// <para>
/// <b>Provisional values (D-69).</b> The four weights and two caps are first estimates, owner-accepted as provisional and validated
/// against the calibration report (task 161), the evaluation (162) and at UAT (163). The weights sum to 1; the caps are the
/// maximum combined confidence a field can have when that check fails.
/// </para>
/// </remarks>
public sealed record EbillingSettings
{
    private static readonly IReadOnlyList<BillingApproverRole> DefaultApproverRoles =
        Array.AsReadOnly(new[] { BillingApproverRole.AssignedAttorney1, BillingApproverRole.AssignedToInternal });

    /// <summary>The roles that hold the approvers of a Matter whose own Billing Approver Role is empty (D-54).</summary>
    public IReadOnlyList<BillingApproverRole> MatterApproverRoles { get; init; } = DefaultApproverRoles;

    /// <summary>The roles that hold the approvers of a Project whose own Billing Approver Role is empty (D-54).</summary>
    public IReadOnlyList<BillingApproverRole> ProjectApproverRoles { get; init; } = DefaultApproverRoles;

    /// <summary>The roles that hold the approvers of a Work Assignment whose own Billing Approver Role is empty (D-54).</summary>
    public IReadOnlyList<BillingApproverRole> WorkAssignmentApproverRoles { get; init; } = DefaultApproverRoles;

    /// <summary>Combined confidence a field must reach before it can support an automatic rejection (FR-26, D-47). 0 to 1.</summary>
    public decimal ReliabilityThreshold { get; init; } = 0.90m;

    /// <summary>Weight of the analyzer (OCR) confidence in the combined confidence (D-47). Provisional (D-69).</summary>
    public decimal WeightAnalyzer { get; init; } = 0.50m;

    /// <summary>Weight of the arithmetic-consistency check in the combined confidence (D-47). Provisional (D-69).</summary>
    public decimal WeightArithmetic { get; init; } = 0.25m;

    /// <summary>Weight of the reference-data match in the combined confidence (D-47). Provisional (D-69).</summary>
    public decimal WeightReferenceMatch { get; init; } = 0.15m;

    /// <summary>Weight of the model self-reported confidence in the combined confidence (D-47). Provisional (D-69).</summary>
    public decimal WeightSelfReport { get; init; } = 0.10m;

    /// <summary>Maximum combined confidence of a field whose arithmetic check fails (D-47). Provisional (D-69).</summary>
    public decimal ArithmeticFailCap { get; init; } = 0.50m;

    /// <summary>Maximum combined confidence of a field with no reference-data match (D-47). Provisional (D-69).</summary>
    public decimal ReferenceMissingCap { get; init; } = 0.80m;

    /// <summary>Days before and after a line date searched for related work product (FR-34). 0 to 90.</summary>
    public int RelatedWorkWindowDays { get; init; } = 7;

    /// <summary>Invoice lines sent to the coding Action per call (FR-22). 1 to 200.</summary>
    public int CodingChunkSize { get; init; } = 25;

    /// <summary>Whether firm notices are drafted by the AI; false uses the template only (FR-50, D-60).</summary>
    public bool FirmNoticeAiEnabled { get; init; } = true;

    /// <summary>The UTBMS code set used when nothing else resolves one for a line (FR-22 step 5); <c>null</c> when none is set.</summary>
    public Guid? DefaultCodeSetId { get; init; }

    /// <summary>The AP export field mapping as JSON (FR-41, D-56); <c>null</c> when the row holds none.</summary>
    public string? ApExportMappingJson { get; init; }

    /// <summary>
    /// Why these settings cannot back a rejection (FR-26, I-5, D-89), or <c>null</c> when they can. The rule: the reliability threshold
    /// is above 0 and at most 1, and <see cref="ReferenceMissingCap"/> and <see cref="ArithmeticFailCap"/> are both below it, so a field
    /// with no reference match, or one whose arithmetic check failed, can never reach the threshold and so can never support a rejection
    /// (D-47, D-89; 073 review F1-c added the arithmetic cap). The platform checks each column's range but not these pairs (a cross-field
    /// rule, and ADR-002 allows no plugin or business rule to own it), so the readers check it when they use the row:
    /// <see cref="EbillingSettingsReader"/> logs it, and <c>SubmissionRejectionRule</c> then rejects nothing until it is fixed.
    /// </summary>
    public string? RejectionInvariantProblem =>
        ReliabilityThreshold is <= 0m or > 1m
            ? $"The reliability threshold ({ReliabilityThreshold}) must be above 0 and at most 1."
            : ReferenceMissingCap >= ReliabilityThreshold
                ? $"The reference-missing cap ({ReferenceMissingCap}) must be below the reliability threshold ({ReliabilityThreshold}); otherwise a field with no reference match can support a rejection."
                : ArithmeticFailCap >= ReliabilityThreshold
                    ? $"The arithmetic-fail cap ({ArithmeticFailCap}) must be below the reliability threshold ({ReliabilityThreshold}); otherwise a field whose arithmetic check failed can support a rejection."
                    : null;
}
