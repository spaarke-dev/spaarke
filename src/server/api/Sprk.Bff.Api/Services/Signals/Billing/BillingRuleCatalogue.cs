using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Services.Signals.Billing;

/// <summary>
/// The authorable billing rule types: the eleven of spec FR-24 / design.md 6.1 (ebilling-r1 task 070) plus AddedFee and Discount
/// (task 115, procedure-and-file-model.md 6.3): their catalogue order, embedded
/// schema, template fact-field set and describer sentence. Closed: every member is a switch over
/// <see cref="RuleType"/> (a throwing default for a non-billing type), never a registry, a reflection scan or a data row (D-27).
/// </summary>
/// <remarks>
/// <para><b>Parameters only.</b> A body carries the rule's parameters (a tolerance, an increment, a code list, a number of
/// days). Rates, timekeepers and code descriptions are reference data the evaluator (task 071) reads; the consequence is the
/// <c>sprk_policyversion.sprk_consequence</c> column (task 019), never body content.</para>
/// <para><b>Template fact fields.</b> A billing <c>sprk_messagetemplate</c> may name only the <c>{{field}}</c> tokens in
/// <see cref="TemplateFactFields"/> for its type: the values the type's handler reads for a failing line, plus the rule's own
/// required parameters (<c>rule.*</c>). Task 071's handlers must supply exactly these.</para>
/// </remarks>
public static class BillingRuleCatalogue
{
    /// <summary>FR-24 order, then the task 115 types. Position N (1-based) is FR-26's numbering; for positions 1-11 its option value is
    /// 100000002 + N. AddedFee (12) and Discount (13) are 100000015 and 100000016: 100000014 is the system type, which has no position.</summary>
    public static IReadOnlyList<RuleType> Types { get; } = new[]
    {
        RuleType.ApprovedTimekeeper,
        RuleType.ApprovedRate,
        RuleType.TimeIncrement,
        RuleType.DailyHoursCap,
        RuleType.NonBillableActivity,
        RuleType.ExpenseRule,
        RuleType.InvoiceAge,
        RuleType.CoreRecordWindow,
        RuleType.Duplicate,
        RuleType.RequiredData,
        RuleType.AiObservation,
        RuleType.AddedFee,
        RuleType.Discount,
    };

    /// <summary>
    /// The SYSTEM billing type (D-125): a Billing member that is not in <see cref="Types"/>, so it has no catalogue position, is not offered
    /// to the drafting Action and cannot be authored. It is the type of the seeded policy <c>BILL-SYSTEM-SUBMISSION</c> only.
    /// </summary>
    public static bool IsSystemType(RuleType ruleType) => ruleType == RuleType.SubmissionIntegrity;

    /// <summary>The system policy's code (D-125): one policy, seeded by <c>scripts/ebilling/Seed-BillingSystemPolicy.ps1</c>.</summary>
    public const string SystemPolicyCode = "BILL-SYSTEM-SUBMISSION";

    /// <summary>The refusal text of a system type at an authoring or save path (D-125). Names no rule content.</summary>
    public const string SystemRuleRefusal = "SubmissionIntegrity is a system rule; not authorable.";

    /// <summary>AddedFee's <c>allowance</c> values (the schema's closed vocabulary). Only <see cref="AllowanceCap"/> is evaluated (task 115).</summary>
    public const string AllowanceNotAllowed = "notAllowed";

    /// <inheritdoc cref="AllowanceNotAllowed"/>
    public const string AllowanceCap = "cap";

    /// <inheritdoc cref="AllowanceNotAllowed"/>
    public const string AllowanceAfterDays = "afterDays";

    /// <summary>The refusal of an AddedFee "not allowed" body: no fact says which lines carry a fee kind.</summary>
    public const string AddedFeeNotAllowedRefusal =
        "An AddedFee rule with allowance 'notAllowed' cannot be evaluated yet: it needs the fee kind of each invoice line, which invoice " +
        "extraction does not return. Use allowance 'cap' with feeCodes and capAmount or capPercent.";

    /// <summary>The refusal of an AddedFee "after N days" body: no fact gives a per-line fee kind or the invoice due date.</summary>
    public const string AddedFeeAfterDaysRefusal =
        "An AddedFee rule with allowance 'afterDays' cannot be evaluated yet: it needs the fee kind of each invoice line and the invoice " +
        "due date, which the billing facts do not carry. Use allowance 'cap' with feeCodes and capAmount or capPercent.";

    /// <summary>
    /// Task 115 (procedure model 6.3, v5.1 row 12): the refusal of a schema-valid body whose form its type cannot evaluate from
    /// <see cref="BillingFactSet"/>, or <c>null</c>. Today only AddedFee's <c>notAllowed</c> and <c>afterDays</c> allowances, until
    /// extraction returns a per-line fee kind and the due date. Called after the schema has passed (the body parses and the
    /// allowance is one of the three); any other type returns <c>null</c>.
    /// </summary>
    internal static string? UnsupportedFormRefusal(RuleType ruleType, string? ruleBodyJson)
    {
        if (ruleType != RuleType.AddedFee || string.IsNullOrWhiteSpace(ruleBodyJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(ruleBodyJson);
        var allowance = document.RootElement.TryGetProperty("allowance", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        return allowance switch
        {
            AllowanceCap => null,
            AllowanceNotAllowed => AddedFeeNotAllowedRefusal,
            AllowanceAfterDays => AddedFeeAfterDaysRefusal,
            // Unreachable after the schema; fail closed all the same.
            _ => $"An AddedFee rule must have allowance '{AllowanceCap}'.",
        };
    }

    /// <summary>The 1-based FR-24 / FR-26 catalogue position of a billing type.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static int CataloguePosition(RuleType ruleType)
    {
        var index = IndexOf(ruleType);
        return index >= 0
            ? index + 1
            : throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type.");
    }

    /// <summary>The embedded schema resource suffix (flat in <c>Services/Signals/Schemas</c>; the csproj glob is not recursive).</summary>
    internal static string SchemaResourceSuffix(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedTimekeeper => Suffix("approved-timekeeper"),
        RuleType.ApprovedRate => Suffix("approved-rate"),
        RuleType.TimeIncrement => Suffix("time-increment"),
        RuleType.DailyHoursCap => Suffix("daily-hours-cap"),
        RuleType.NonBillableActivity => Suffix("non-billable-activity"),
        RuleType.ExpenseRule => Suffix("expense-rule"),
        RuleType.InvoiceAge => Suffix("invoice-age"),
        RuleType.CoreRecordWindow => Suffix("core-record-window"),
        RuleType.Duplicate => Suffix("duplicate"),
        RuleType.RequiredData => Suffix("required-data"),
        RuleType.AiObservation => Suffix("ai-observation"),
        RuleType.SubmissionIntegrity => Suffix("submission-integrity"),
        RuleType.AddedFee => Suffix("added-fee"),
        RuleType.Discount => Suffix("discount"),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    /// <summary>
    /// The text of the embedded body schema of a billing type (what <see cref="RuleBodySchemaValidator"/> validates against), for a caller that
    /// must show a type's parameters to someone else (the rule drafting Action, task 075).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    /// <exception cref="InvalidOperationException">The embedded resource is missing.</exception>
    internal static string SchemaText(RuleType ruleType)
    {
        var suffix = SchemaResourceSuffix(ruleType);
        var assembly = typeof(BillingRuleCatalogue).Assembly;
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded resource ending in '{suffix}' was not found in assembly '{assembly.FullName}'.");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource stream '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The code-identifier pattern (<c>$defs.code.pattern</c>) of the billing rule bodies, read from the ExpenseRule schema: the one definition
    /// the NonBillableActivity schema and Rule Review's <c>CODE_PATTERN</c> (<c>src/solutions/RuleReview/src/services/ruleTypes.ts</c>) repeat.
    /// The schema is the enforced one (the rule body validator); Rule Review's copy only gives an administrator the same answer before a save.
    /// <c>BillingCodePatternTests</c> pins the string itself, both schemas, and the TypeScript literal (read from its source), so a change to
    /// any one of them fails a test. Not copied into C#.
    /// </summary>
    internal static string CodePattern => CodeRegex.Value.ToString();

    private static readonly Lazy<Regex> CodeRegex = new(() =>
    {
        using var schema = JsonDocument.Parse(SchemaText(RuleType.ExpenseRule));
        var pattern = schema.RootElement.GetProperty("$defs").GetProperty("code").GetProperty("pattern").GetString()
            ?? throw new InvalidOperationException("The ExpenseRule schema's $defs.code has no pattern.");
        return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    });

    /// <summary>
    /// True when <paramref name="value"/> is a code identifier by the rule bodies' own pattern (<see cref="CodePattern"/>), so a message may
    /// cite it (049 part D, ontology-r1 D-125 review (b)): never text the model returned as an instruction, and never more than 20 characters.
    /// The whole value must match (.NET's <c>$</c> also matches before a final newline, which JSON Schema's does not).
    /// </summary>
    public static bool IsCodeShaped(string? value) =>
        value is { Length: > 0 } && CodeRegex.Value.Match(value) is { Success: true } match && match.Length == value.Length;

    /// <summary>
    /// The shape an invoice number must have for a message to cite it (049 F1-3): a letter or digit, then up to 39 letters, digits or
    /// <c>. _ / # -</c>, no space, at most 40 characters. The one definition for the D-91 read number and Duplicate's firm invoice number.
    /// </summary>
    internal const string InvoiceNumberPattern = @"^[A-Za-z0-9][A-Za-z0-9._/#-]{0,39}\z";

    private static readonly Regex InvoiceNumberRegex = new(InvoiceNumberPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// The shape a person's name must have for a message to cite it (049 F1-3): a letter, then up to 99 letters, marks, spaces or
    /// <c>. , ' ’ -</c>, at most 100 characters. No digit, colon, bracket or line break, so a value that is not a name is not repeated.
    /// </summary>
    internal const string NamePattern = @"^\p{L}[\p{L}\p{M} .,'\u2019-]{0,99}\z";

    private static readonly Regex NameRegex = new(NamePattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>True when <paramref name="value"/> is an invoice number by <see cref="InvoiceNumberPattern"/> (the whole value).</summary>
    public static bool IsInvoiceNumberShaped(string? value) => value is { Length: > 0 } && InvoiceNumberRegex.IsMatch(value);

    /// <summary>True when <paramref name="value"/> is a person's name by <see cref="NamePattern"/> (the whole value).</summary>
    public static bool IsNameShaped(string? value) => value is { Length: > 0 } && NameRegex.IsMatch(value);

    /// <summary>
    /// Whether a message may cite <paramref name="value"/> for <paramref name="token"/> (049 F1-3). The template facts whose value is
    /// firm-supplied text (extracted from the document, typed by the firm, or the code the firm billed) are cited only in their shape, so a
    /// long or crafted value never reaches <c>sprk_sentence</c> (2,000 characters) and never reads as an instruction: the codes
    /// (<see cref="IsCodeShaped"/>), the invoice numbers (<see cref="IsInvoiceNumberShaped"/>) and the timekeeper name
    /// (<see cref="IsNameShaped"/>). Every other fact is a number, a date, a rule parameter or reference data, and is always citable.
    /// A value that is not citable is left out, never truncated: the message then uses a wording that does not name it.
    /// </summary>
    public static bool IsCitable(string token, string? value) => token switch
    {
        "line.expenseCode" or "line.activityCode" or "line.taskCode" or "line.firmCode" or "line.refusedCode" => IsCodeShaped(value),
        "invoice.invoiceNumber" or "invoice.firmInvoiceNumber" => IsInvoiceNumberShaped(value),
        "line.timekeeperName" => IsNameShaped(value),
        _ => true,
    };

    /// <summary>The <c>{{field}}</c> tokens a billing <c>sprk_messagetemplate</c> of this type may name.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static IReadOnlySet<string> TemplateFactFields(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedTimekeeper => Fields("line.date", "line.timekeeperName"),
        RuleType.ApprovedRate => Fields("line.date", "line.timekeeperName", "line.rate", "approvedRate", "rule.tolerancePercent"),
        RuleType.TimeIncrement => Fields("line.date", "line.timekeeperName", "line.hours", "rule.incrementHours"),
        RuleType.DailyHoursCap => Fields("line.date", "line.timekeeperName", "dailyHours", "rule.maxHoursPerDay"),
        RuleType.NonBillableActivity => Fields("line.date", "line.timekeeperName", "line.activityCode", "line.taskCode"),
        RuleType.ExpenseRule => Fields("line.date", "line.expenseCode", "line.amount"),
        RuleType.InvoiceAge => Fields("line.date", "invoice.date", "rule.maxAgeDays"),
        RuleType.CoreRecordWindow => Fields("line.date", "coreRecord.openDate", "coreRecord.closeDate", "rule.graceDays"),
        RuleType.Duplicate => Fields("invoice.firmInvoiceNumber", "line.date", "line.timekeeperName", "duplicateOf.trackingNumber"),
        RuleType.RequiredData => Fields("missingField"),
        RuleType.AiObservation => Fields("line.date", "observation", "observationConfidence", "rule.minConfidence"),
        // D-125: the values the four system reasons compare, as 049 reads them (BillingSystemFindings builds exactly these).
        RuleType.SubmissionIntegrity => Fields(
            "submission.invoiceNumber", "invoice.invoiceNumber", "line.codeKind", "line.refusedCode", "line.firmCode", "line.suggestedCode",
            "codeSet.name"),
        // Task 115: AddedFee's cap form only (the other two allowances are refused before evaluation); feeTotal / feeLimit are amounts.
        // AddedFee's finding is invoice level (ontology-r1 115 review F3-6), so it has no line token; rule.feeKind is the kind's label.
        RuleType.AddedFee => Fields("feeTotal", "feeLimit", "rule.feeKind"),
        // discountedRate is rack x (1 - discount); discountLimit adds the tolerance and is what the rate is compared with (review F2-1).
        RuleType.Discount => Fields(
            "line.date", "line.timekeeperName", "line.rate", "rackRate", "discountedRate", "discountLimit", "rule.discountPercent"),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    /// <summary>
    /// The message of a Duplicate finding whose matching prior is across the secure wall, or whose wall could not be established
    /// (D-112, <see cref="BillingFinding.DetailWithheld"/>). It names no token: the finding carries no tracking number, no
    /// invoice or line detail. The variant of the Duplicate template (see <c>BillingRuleDraftTranslator.Wording</c>), which names
    /// <c>{{duplicateOf.trackingNumber}}</c>; a message composer uses this text, not the policy version's template, for such a finding.
    /// It IS <see cref="EarlierInvoiceReadability.WithheldSentence"/> (ontology task 157 owns the text; D-176, task 119), so a finding
    /// withheld at write time and one withheld at read time read alike and the two cannot drift.
    /// </summary>
    public const string WithheldDuplicateTemplate = EarlierInvoiceReadability.WithheldSentence;

    /// <summary>
    /// The <c>{{field}}</c> tokens of a withheld finding's message (D-112): none. A withheld Duplicate finding is built against this
    /// set, not <see cref="TemplateFactFields"/>, so it cannot name the tracking number or a line value.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a type with a withheld variant (only Duplicate has one).</exception>
    public static IReadOnlySet<string> WithheldTemplateFactFields(RuleType ruleType) => ruleType switch
    {
        RuleType.Duplicate => Fields(),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Only Duplicate has a withheld-detail variant."),
    };

    /// <summary>The message of RequiredData's <see cref="BillingReasonCodes.CurrencyNotUsd"/> finding (ontology-r1 072 review F1): the
    /// type's default wording ("The required data {{missingField}} is missing.") would tell the firm a currency it read was missing.</summary>
    public const string CurrencyNotUsdTemplate = "The invoice currency is not USD.";

    /// <summary>The message of CoreRecordWindow's <see cref="BillingReasonCodes.CoreRecordClosedToBilling"/> finding (D-116; 072 review
    /// F1-a). The version's template and the type's default wording name the open and close dates ("outside the open date ... and
    /// close date ..."), which is false for a Closed record whose dates the line is inside; this names no date.</summary>
    public const string ClosedToBillingTemplate = "The matter, project or work assignment this line is billed to is closed to billing.";

    /// <summary>The message of CoreRecordWindow's <see cref="BillingReasonCodes.CoreRecordClosed"/> finding (D-111, D-116; 072 review
    /// F1-a): a Final record with no close date, or (billing status empty) an inactive one with none. There is no date the line is
    /// outside of, so the date wording would be false; this names none.</summary>
    public const string NoCloseDateTemplate = "This line cannot be checked against a close date: the record is closed or final but has no close date.";

    /// <summary>
    /// The reason's own message, used instead of the policy version's template and the type's default wording, or <c>null</c> when the
    /// reason has none. Names no token, so it always renders.
    /// </summary>
    public static string? ReasonTemplate(string reasonCode) => reasonCode switch
    {
        BillingReasonCodes.CurrencyNotUsd => CurrencyNotUsdTemplate,
        BillingReasonCodes.CoreRecordClosedToBilling => ClosedToBillingTemplate,
        BillingReasonCodes.CoreRecordClosed => NoCloseDateTemplate,
        BillingReasonCodes.SubmissionInvoiceNumberMismatch => InvoiceNumberMismatchTemplate,
        BillingReasonCodes.SubmissionInvoiceNumberMismatchNotShown => InvoiceNumberMismatchNotShownTemplate,
        BillingReasonCodes.CodingRefusedCode => RefusedCodeTemplate,
        BillingReasonCodes.CodingRefusedCodeNotShown => RefusedCodeNotShownTemplate,
        BillingReasonCodes.CodingCodeKindNotForLine => CodeKindNotForLineTemplate,
        BillingReasonCodes.CodingFirmCodeMismatch => FirmCodeMismatchTemplate,
        BillingReasonCodes.CodingFirmCodeMismatchNotShown => FirmCodeMismatchNotShownTemplate,
        BillingReasonCodes.CodingCodeSetNeedsReview => CodeSetNeedsReviewTemplate,
        BillingReasonCodes.CodingNoCodeSet => NoCodeSetTemplate,
        BillingReasonCodes.CodingUnavailable => CodingUnavailableTemplate,
        BillingEvaluationReasons.CurrencyMissing => RatesNotCheckedPrefix + "missing.",
        BillingEvaluationReasons.CurrencyUnreliable => RatesNotCheckedPrefix + "unclear.",
        BillingEvaluationReasons.CurrencyNotUsd => RatesNotCheckedPrefix + "not USD.",
        BillingReasonCodes.DiscountNotCheckedNoRackRate => DiscountNotCheckedTemplate,
        BillingReasonCodes.AddedFeeNotChecked => AddedFeeNotCheckedTemplate,
        _ => null,
    };

    // ── Task 115 "not checked" reasons (ontology-r1 115 review F2-3, F2-4; the D-115 pattern): flag only, invoice level, no token. ──

    /// <summary>The message of <see cref="BillingReasonCodes.DiscountNotCheckedNoRackRate"/>.</summary>
    public const string DiscountNotCheckedTemplate =
        "The agreed discount was not checked on every fee line: at least one line has no standard (rack) rate on the rate card.";

    /// <summary>The title of <see cref="BillingReasonCodes.DiscountNotCheckedNoRackRate"/>'s Signal.</summary>
    public const string DiscountNotCheckedHeadline = "Discount not checked: no rack rate";

    /// <summary>The message of <see cref="BillingReasonCodes.AddedFeeNotChecked"/>.</summary>
    public const string AddedFeeNotCheckedTemplate =
        "The added-fee limit was not checked: it is a percent of the invoice's other lines, and their total is zero or less or a line has no amount.";

    /// <summary>The title of <see cref="BillingReasonCodes.AddedFeeNotChecked"/>'s Signal.</summary>
    public const string AddedFeeNotCheckedHeadline = "Added-fee limit not checked";

    // ── System reasons (D-125). Each claims only what was compared, never that a document is "invalid". ───────────────────────────

    /// <summary>The message of <see cref="BillingReasonCodes.SubmissionInvoiceNumberMismatch"/> (D-91): cites both values it compared.</summary>
    public const string InvoiceNumberMismatchTemplate =
        "The invoice number on the submission, {{submission.invoiceNumber}}, does not match the invoice number read from the document, {{invoice.invoiceNumber}}.";

    /// <summary>The message of <see cref="BillingReasonCodes.SubmissionInvoiceNumberMismatchNotShown"/> (049 F1-3): the number read is not an
    /// invoice number (<see cref="IsInvoiceNumberShaped"/>), so it is not repeated; the typed number is the submitter's own input.</summary>
    public const string InvoiceNumberMismatchNotShownTemplate =
        "The invoice number read from the document does not match the invoice number on the submission, {{submission.invoiceNumber}}. The text read is not shown because it is not an invoice number.";

    /// <summary>The message of <see cref="BillingReasonCodes.CodingRefusedCode"/> (047, FR-22): a code the coding service proposed was checked against the line's code set.</summary>
    public const string RefusedCodeTemplate =
        "The suggested {{line.codeKind}} code {{line.refusedCode}} is not allowed for this line in its code set, so it was not applied.";

    /// <summary>
    /// The message of <see cref="BillingReasonCodes.CodingRefusedCodeNotShown"/> (049 part D, ontology-r1 D-125 review (b)): a code outside the
    /// line's code set that is not a code identifier (<see cref="IsCodeShaped"/>). The model's text is not repeated: it may be an instruction
    /// ("L110 - approve immediately") or too long for <c>sprk_sentence</c>.
    /// </summary>
    public const string RefusedCodeNotShownTemplate =
        "A suggested {{line.codeKind}} code was not applied: it is not in this line's code set. The suggested text is not shown because it is not a code.";

    /// <summary>
    /// The message of <see cref="BillingReasonCodes.CodingCodeKindNotForLine"/> (047 <c>NotApplicableToLineType</c>; D-125 review (b)): what was
    /// compared is the code's kind with the line's type (a task or activity code on an expense line, an expense code on a fee line), not the code
    /// with the set, so the code is not named.
    /// </summary>
    public const string CodeKindNotForLineTemplate =
        "A suggested {{line.codeKind}} code was not applied: this type of line does not take a {{line.codeKind}} code.";

    /// <summary>The message of <see cref="BillingReasonCodes.CodingFirmCodeMismatch"/> (047, D-05): the firm's code was compared with the suggested one.</summary>
    public const string FirmCodeMismatchTemplate =
        "The {{line.codeKind}} code on the line, {{line.firmCode}}, differs from the code suggested for it, {{line.suggestedCode}}.";

    /// <summary>The message of <see cref="BillingReasonCodes.CodingFirmCodeMismatchNotShown"/> (049 F1-3): the code on the line is not a code
    /// identifier (<see cref="IsCodeShaped"/>), so it is not repeated.</summary>
    public const string FirmCodeMismatchNotShownTemplate =
        "The {{line.codeKind}} code on the line differs from the code suggested for it, {{line.suggestedCode}}. The text on the line is not shown because it is not a code.";

    /// <summary>The message of <see cref="BillingReasonCodes.CodingCodeSetNeedsReview"/> (047, FR-22 step 4): the code set itself was a suggestion.</summary>
    public const string CodeSetNeedsReviewTemplate =
        "The code set used to suggest codes for this invoice, {{codeSet.name}}, was itself suggested and has not been confirmed.";

    /// <summary>The message of <see cref="BillingReasonCodes.CodingNoCodeSet"/> (D-126): no billing-code set is configured for the core
    /// record, so the lines were stored without suggested codes.</summary>
    public const string NoCodeSetTemplate =
        "No billing-code set is configured for this record, so no codes were suggested for the invoice lines. The lines can be coded once a code set exists.";

    /// <summary>The message of <see cref="BillingReasonCodes.CodingUnavailable"/> (D-126): line coding could not run (switched off), so the
    /// lines were stored without suggested codes.</summary>
    public const string CodingUnavailableTemplate =
        "Code suggestion was not available when this invoice was processed, so no codes were suggested for the invoice lines.";

    /// <summary>The title of <see cref="BillingReasonCodes.SubmissionInvoiceNumberMismatch"/>'s Signal.</summary>
    public const string InvoiceNumberMismatchHeadline = "Invoice number differs from the submission";

    /// <summary>The title of <see cref="BillingReasonCodes.SubmissionInvoiceNumberMismatchNotShown"/>'s Signal.</summary>
    public const string InvoiceNumberMismatchNotShownHeadline = "Invoice number read differs from the submission";

    /// <summary>The title of <see cref="BillingReasonCodes.CodingRefusedCode"/>'s Signal.</summary>
    public const string RefusedCodeHeadline = "Suggested code not applied";

    /// <summary>The title of <see cref="BillingReasonCodes.CodingRefusedCodeNotShown"/>'s Signal.</summary>
    public const string RefusedCodeNotShownHeadline = "Unrecognised suggested code not applied";

    /// <summary>The title of <see cref="BillingReasonCodes.CodingCodeKindNotForLine"/>'s Signal.</summary>
    public const string CodeKindNotForLineHeadline = "Suggested code does not fit the line type";

    /// <summary>The title of <see cref="BillingReasonCodes.CodingFirmCodeMismatch"/>'s Signal.</summary>
    public const string FirmCodeMismatchHeadline = "Billed code differs from the suggested code";

    /// <summary>The title of <see cref="BillingReasonCodes.CodingFirmCodeMismatchNotShown"/>'s Signal.</summary>
    public const string FirmCodeMismatchNotShownHeadline = "Unrecognised billed code differs from the suggested code";

    /// <summary>The title of <see cref="BillingReasonCodes.CodingCodeSetNeedsReview"/>'s Signal.</summary>
    public const string CodeSetNeedsReviewHeadline = "Suggested code set needs review";

    /// <summary>The title of <see cref="BillingReasonCodes.CodingNoCodeSet"/>'s Signal.</summary>
    public const string NoCodeSetHeadline = "No billing-code set for this record";

    /// <summary>The title of <see cref="BillingReasonCodes.CodingUnavailable"/>'s Signal.</summary>
    public const string CodingUnavailableHeadline = "Lines not coded";

    /// <summary>The title (<c>sprk_name</c>) of D-115's "money not judged" Signal.</summary>
    public const string RatesNotCheckedHeadline = "Rates and expense caps not checked";

    /// <summary>The title of RequiredData's <see cref="BillingReasonCodes.CurrencyNotUsd"/> Signal.</summary>
    public const string CurrencyNotUsdHeadline = "Invoice currency is not USD";

    /// <summary>The title of CoreRecordWindow's <see cref="BillingReasonCodes.CoreRecordClosedToBilling"/> Signal.</summary>
    public const string ClosedToBillingHeadline = "Closed to billing";

    /// <summary>The title of CoreRecordWindow's <see cref="BillingReasonCodes.CoreRecordClosed"/> Signal.</summary>
    public const string NoCloseDateHeadline = "Close date missing";

    /// <summary>
    /// The reason's own title, used instead of the policy version's <c>ShortHeadline</c> and the type's default headline, or <c>null</c>
    /// when the reason has none (ontology-r1 072 re-check F2). Exactly the reasons that have a <see cref="ReasonTemplate"/>: a sentence
    /// that says something else than the host rule's breach needs a title that does too, because the title is what the user sees first.
    /// </summary>
    public static string? ReasonHeadline(string reasonCode) => reasonCode switch
    {
        BillingReasonCodes.CurrencyNotUsd => CurrencyNotUsdHeadline,
        BillingReasonCodes.CoreRecordClosedToBilling => ClosedToBillingHeadline,
        BillingReasonCodes.CoreRecordClosed => NoCloseDateHeadline,
        BillingReasonCodes.SubmissionInvoiceNumberMismatch => InvoiceNumberMismatchHeadline,
        BillingReasonCodes.SubmissionInvoiceNumberMismatchNotShown => InvoiceNumberMismatchNotShownHeadline,
        BillingReasonCodes.CodingRefusedCode => RefusedCodeHeadline,
        BillingReasonCodes.CodingRefusedCodeNotShown => RefusedCodeNotShownHeadline,
        BillingReasonCodes.CodingCodeKindNotForLine => CodeKindNotForLineHeadline,
        BillingReasonCodes.CodingFirmCodeMismatch => FirmCodeMismatchHeadline,
        BillingReasonCodes.CodingFirmCodeMismatchNotShown => FirmCodeMismatchNotShownHeadline,
        BillingReasonCodes.CodingCodeSetNeedsReview => CodeSetNeedsReviewHeadline,
        BillingReasonCodes.CodingNoCodeSet => NoCodeSetHeadline,
        BillingReasonCodes.CodingUnavailable => CodingUnavailableHeadline,
        BillingEvaluationReasons.CurrencyMissing or BillingEvaluationReasons.CurrencyUnreliable or BillingEvaluationReasons.CurrencyNotUsd
            => RatesNotCheckedHeadline,
        BillingReasonCodes.DiscountNotCheckedNoRackRate => DiscountNotCheckedHeadline,
        BillingReasonCodes.AddedFeeNotChecked => AddedFeeNotCheckedHeadline,
        _ => null,
    };

    /// <summary>D-115: the start of the message of the evaluator's "money not judged" finding; the reason completes it.</summary>
    private const string RatesNotCheckedPrefix = "Rates and expense caps were not checked: the invoice currency is ";

    /// <summary>
    /// A reason that never rejects or adjusts, whatever consequence its policy version carries (D-115; ontology-r1 072 review F1):
    /// the currency is a fact about the invoice, not a breach the rule's author priced. Task 073 holds such a finding as a Flag, and
    /// <see cref="BillingFindingSignalMapper"/> counts it as a Flag when it merges reasons.
    /// </summary>
    public static bool IsFlagOnlyReason(string reasonCode) => reasonCode switch
    {
        BillingReasonCodes.CurrencyNotUsd => true,
        // D-125: the system reasons hold or note, never reject or adjust, whatever a stored version's consequence says.
        BillingReasonCodes.SubmissionInvoiceNumberMismatch or BillingReasonCodes.CodingRefusedCode
            or BillingReasonCodes.CodingRefusedCodeNotShown or BillingReasonCodes.CodingCodeKindNotForLine
            or BillingReasonCodes.CodingFirmCodeMismatch or BillingReasonCodes.CodingCodeSetNeedsReview
            or BillingReasonCodes.SubmissionInvoiceNumberMismatchNotShown or BillingReasonCodes.CodingFirmCodeMismatchNotShown
            or BillingReasonCodes.CodingNoCodeSet or BillingReasonCodes.CodingUnavailable => true,
        BillingEvaluationReasons.CurrencyMissing or BillingEvaluationReasons.CurrencyUnreliable or BillingEvaluationReasons.CurrencyNotUsd => true,
        // Task 115: "not checked" is a fact about the invoice's data, not a breach (as D-115).
        BillingReasonCodes.DiscountNotCheckedNoRackRate or BillingReasonCodes.AddedFeeNotChecked => true,
        _ => false,
    };

    /// <summary>
    /// FR-26 reject-eligibility (ebilling-r1 task 073): catalogue positions 1, 2 and 6 to 10, that is ApprovedTimekeeper, ApprovedRate,
    /// ExpenseRule, InvoiceAge, CoreRecordWindow, Duplicate and RequiredData. TimeIncrement, DailyHoursCap, NonBillableActivity and
    /// AiObservation (I-5) are not. Eligibility is one of the three conditions of a rejection; it is not the whole of "may reject"
    /// (see <see cref="IsHoldOnly"/>, and Duplicate rejects at invoice level only).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static bool IsRejectEligible(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedTimekeeper or RuleType.ApprovedRate or RuleType.ExpenseRule or RuleType.InvoiceAge
            or RuleType.CoreRecordWindow or RuleType.Duplicate or RuleType.RequiredData => true,
        RuleType.TimeIncrement or RuleType.DailyHoursCap or RuleType.NonBillableActivity or RuleType.AiObservation
            or RuleType.SubmissionIntegrity => false,
        // Task 115 (the FR-26 list is unchanged): an added fee over its cap and a discount not applied are for a reviewer, never a rejection.
        RuleType.AddedFee or RuleType.Discount => false,
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    /// <summary>
    /// D-89 ("ApprovedTimekeeper holds, never auto-rejects"): a type that is reject-eligible under FR-26 but whose findings only ever
    /// send the invoice to review. A Reject consequence is refused for it at publish and at the evaluation gate, and the rejection rule
    /// never rejects on its findings. Enforced by name here, not by 046's confidence cap staying below the threshold.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static bool IsHoldOnly(RuleType ruleType) => IsRejectEligible(ruleType) && ruleType == RuleType.ApprovedTimekeeper;

    /// <summary>
    /// The consequences a billing admin may choose for a rule of this type (design.md 6.1 "Allowed consequences", narrowed by D-89 and
    /// I-5): Reject only where the type is reject-eligible and not hold-only; Suggest adjustment only where a handler suggests one
    /// (ApprovedRate, NonBillableActivity); Flag always.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static IReadOnlySet<BillingConsequence> AllowedConsequences(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedTimekeeper => Consequences(BillingConsequence.Flag),
        RuleType.ApprovedRate => Consequences(BillingConsequence.Reject, BillingConsequence.Flag, BillingConsequence.SuggestAdjustment),
        RuleType.TimeIncrement => Consequences(BillingConsequence.Flag),
        RuleType.DailyHoursCap => Consequences(BillingConsequence.Flag),
        RuleType.NonBillableActivity => Consequences(BillingConsequence.Flag, BillingConsequence.SuggestAdjustment),
        RuleType.ExpenseRule => Consequences(BillingConsequence.Reject, BillingConsequence.Flag),
        RuleType.InvoiceAge => Consequences(BillingConsequence.Reject, BillingConsequence.Flag),
        RuleType.CoreRecordWindow => Consequences(BillingConsequence.Reject, BillingConsequence.Flag),
        RuleType.Duplicate => Consequences(BillingConsequence.Reject, BillingConsequence.Flag),
        RuleType.RequiredData => Consequences(BillingConsequence.Reject, BillingConsequence.Flag),
        RuleType.AiObservation => Consequences(BillingConsequence.Flag),
        RuleType.SubmissionIntegrity => Consequences(BillingConsequence.Flag),
        RuleType.AddedFee => Consequences(BillingConsequence.Flag),
        RuleType.Discount => Consequences(BillingConsequence.Flag),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    /// <summary>True when <paramref name="consequence"/> is one of the three defined values and <see cref="AllowedConsequences"/> lists it for the type.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static bool IsConsequenceAllowed(RuleType ruleType, int consequence) =>
        Enum.IsDefined(typeof(BillingConsequence), consequence) && AllowedConsequences(ruleType).Contains((BillingConsequence)consequence);

    /// <summary>The sentence a save-time caller shows when <see cref="IsConsequenceAllowed"/> is false. Names no rule content.</summary>
    internal static string ConsequenceRefusal(RuleType ruleType, int consequence)
    {
        var allowed = string.Join(", ", AllowedConsequences(ruleType).OrderBy(c => (int)c).Select(c => c.ToString()));
        if (!Enum.IsDefined(typeof(BillingConsequence), consequence))
        {
            return $"consequence {consequence} is not one of Reject, Flag or SuggestAdjustment. A {ruleType} rule may use: {allowed}.";
        }

        var chosen = (BillingConsequence)consequence;
        var why = ruleType switch
        {
            RuleType.AiObservation => "an AI observation never rejects a submission (I-5)",
            RuleType.SubmissionIntegrity => "a submission-integrity finding holds the invoice for review and never rejects or adjusts it (D-125)",
            RuleType.AddedFee => "an added fee over its allowance is flagged for review, never rejected or adjusted (FR-26)",
            RuleType.Discount => "a discount rule is a check only: it flags a line rate above the agreed discounted rate and never rejects or adjusts",
            RuleType.ApprovedTimekeeper when chosen == BillingConsequence.Reject => "an unapproved timekeeper holds the invoice for review and never rejects it (D-89)",
            _ when chosen == BillingConsequence.Reject => "this rule type may not reject a submission (FR-26)",
            _ => "this rule type does not suggest adjustments (design.md 6.1)",
        };
        return $"A {ruleType} rule may not have the consequence {chosen}: {why}. Allowed: {allowed}.";
    }

    // ── Reject-capable reason codes (ebilling-r1 task 073 review, F-ALLOW) ──────────────────────────────────────────────────────

    /// <summary>
    /// The finding reason codes (<see cref="BillingReasonCodes"/>) on which a rule of this type may reject a submission: an allowlist, so
    /// a reason code added later (a new sub-reason, a currency finding) only ever holds or flags until it is listed here on purpose. The
    /// rejection rule checks it as part of eligibility. Empty for a type that never rejects (TimeIncrement, DailyHoursCap,
    /// NonBillableActivity, AiObservation) or holds only (ApprovedTimekeeper, D-89). Not listed on purpose:
    /// the ApprovedRate reference-missing reasons (071 judgment call 2), the line-level Duplicate reasons (design.md 6.1) and
    /// <see cref="BillingReasonCodes.CurrencyNotUsd"/> (flag-only, D-115).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static IReadOnlySet<string> RejectCapableReasons(RuleType ruleType) => ruleType switch
    {
        RuleType.ApprovedRate => RejectCapable(BillingReasonCodes.RateAboveApproved),
        RuleType.ExpenseRule => RejectCapable(BillingReasonCodes.ExpenseCodeNotReimbursable, BillingReasonCodes.ExpenseAmountOverCap),
        RuleType.InvoiceAge => RejectCapable(BillingReasonCodes.InvoiceTooOld),
        RuleType.CoreRecordWindow => RejectCapable(
            BillingReasonCodes.BeforeCoreRecordOpened, BillingReasonCodes.AfterCoreRecordClosed, BillingReasonCodes.CoreRecordClosed,
            BillingReasonCodes.CoreRecordClosedToBilling),
        RuleType.Duplicate => RejectCapable(BillingReasonCodes.DuplicateInvoiceNumber),
        RuleType.RequiredData => RejectCapable(BillingReasonCodes.RequiredFieldMissing),
        RuleType.ApprovedTimekeeper or RuleType.TimeIncrement or RuleType.DailyHoursCap or RuleType.NonBillableActivity
            or RuleType.AiObservation or RuleType.SubmissionIntegrity or RuleType.AddedFee or RuleType.Discount => RejectCapable(),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    /// <summary>True when <see cref="RejectCapableReasons"/> lists <paramref name="reasonCode"/> (ordinal) for the type.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a billing type.</exception>
    public static bool IsRejectCapableReason(RuleType ruleType, string? reasonCode) =>
        reasonCode is not null && RejectCapableReasons(ruleType).Contains(reasonCode);

    private static IReadOnlySet<string> RejectCapable(params string[] reasonCodes) => reasonCodes.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// One deterministic sentence and formal form for a body that <see cref="PolicyVersionValidator.ValidateForSave"/> has
    /// accepted for <paramref name="ruleType"/>. Same body, same text; no lookup is read.
    /// </summary>
    internal static (string Text, string Formal) Describe(RuleType ruleType, JsonElement root) => ruleType switch
    {
        RuleType.ApprovedTimekeeper => (
            "Each line's timekeeper is approved for the firm on the line date.",
            "ApprovedTimekeeper()"),
        RuleType.ApprovedRate => (
            $"Each line's billed rate is at most the approved rate for its timekeeper or classification on the line date, plus {Num(root, "tolerancePercent")} percent.",
            $"ApprovedRate(tolerancePercent={Num(root, "tolerancePercent")})"),
        RuleType.TimeIncrement => (
            $"Each line's hours are a multiple of {Num(root, "incrementHours")} hours.",
            $"TimeIncrement(incrementHours={Num(root, "incrementHours")})"),
        RuleType.DailyHoursCap => (
            $"Each timekeeper bills at most {Num(root, "maxHoursPerDay")} hours per day on the invoice.",
            $"DailyHoursCap(maxHoursPerDay={Num(root, "maxHoursPerDay")})"),
        RuleType.NonBillableActivity => (
            $"No line carries a non-billable activity or task code ({List(root, "codes")}).",
            $"NonBillableActivity(codes=[{List(root, "codes")}])"),
        RuleType.ExpenseRule => DescribeExpense(root),
        RuleType.InvoiceAge => (
            $"No line is dated more than {Num(root, "maxAgeDays")} days before the invoice date.",
            $"InvoiceAge(maxAgeDays={Num(root, "maxAgeDays")})"),
        RuleType.CoreRecordWindow => (
            $"Judged by the core record's billing status, with {Num(root, "graceDays")} grace days on each date; credit lines are never flagged. Open: a line dated before the core record's open date is flagged; the close date and closed state are ignored. Final: also a line dated after its close date is flagged, and work dated on or before the close date and billed later is not; with no close date, every charge line (credits excluded) is flagged. Closed: every charge line (credits excluded) is flagged as closed to billing. No billing status: a line dated before the open date or after the close date is flagged, and a closed core record with no close date flags every charge line (credits excluded).",
            $"CoreRecordWindow(graceDays={Num(root, "graceDays")})"),
        RuleType.Duplicate => (
            $"No invoice or line duplicates an earlier one from the firm on {List(root, "matchFields")}.",
            $"Duplicate(matchFields=[{List(root, "matchFields")}])"),
        RuleType.RequiredData => (
            $"Each invoice and line has {List(root, "fields")}.",
            $"RequiredData(fields=[{List(root, "fields")}])"),
        RuleType.AiObservation => (
            $"No line has the AI observation '{root.GetProperty("observation").GetString()}' at a confidence of {Num(root, "minConfidence")} or more.",
            $"AiObservation(observation={root.GetProperty("observation").GetString()}, minConfidence={Num(root, "minConfidence")})"),
        RuleType.SubmissionIntegrity => (
            "System rule: the submission's invoice number and the line coding are compared, and a difference is flagged for review.",
            "SubmissionIntegrity()"),
        RuleType.AddedFee => DescribeAddedFee(root),
        RuleType.Discount => DescribeDiscount(root),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Not a billing rule type."),
    };

    private static (string, string) DescribeExpense(JsonElement root)
    {
        var hasCodes = root.TryGetProperty("nonReimbursableCodes", out _);
        var hasCap = root.TryGetProperty("maxAmountPerItem", out _);
        var parts = new List<string>();
        var formal = new List<string>();
        if (hasCodes)
        {
            parts.Add($"carries a non-reimbursable expense code ({List(root, "nonReimbursableCodes")})");
            formal.Add($"nonReimbursableCodes=[{List(root, "nonReimbursableCodes")}]");
        }

        if (hasCap)
        {
            parts.Add($"is over {Num(root, "maxAmountPerItem")} per item");
            formal.Add($"maxAmountPerItem={Num(root, "maxAmountPerItem")}");
        }

        return ($"No expense line {string.Join(" or ", parts)}.", $"ExpenseRule({string.Join(", ", formal)})");
    }

    private static (string, string) DescribeAddedFee(JsonElement root)
    {
        var kind = root.GetProperty("feeKind").GetString();
        var label = FeeKindLabel(kind);
        switch (root.GetProperty("allowance").GetString())
        {
            case "cap" when root.TryGetProperty("capAmount", out _):
                return (
                    $"The {label} lines (expense codes {List(root, "feeCodes")}) total at most {Num(root, "capAmount")} on an invoice.",
                    $"AddedFee(feeKind={kind}, allowance=cap, feeCodes=[{List(root, "feeCodes")}], capAmount={Num(root, "capAmount")})");
            case "cap":
                return (
                    $"The {label} lines (expense codes {List(root, "feeCodes")}) total at most {Num(root, "capPercent")} percent of the invoice's other lines.",
                    $"AddedFee(feeKind={kind}, allowance=cap, feeCodes=[{List(root, "feeCodes")}], capPercent={Num(root, "capPercent")})");
            case "afterDays":
                // Refused before it is saved or evaluated (rule_form_not_supported); described only so the closed switch has an answer.
                return (
                    $"A {label} is allowed only {Num(root, "afterDays")} days after the invoice due date.",
                    $"AddedFee(feeKind={kind}, allowance=afterDays, afterDays={Num(root, "afterDays")})");
            default:
                return ($"No {label} may be billed.", $"AddedFee(feeKind={kind}, allowance=notAllowed)");
        }
    }

    /// <summary>The words for an AddedFee <c>feeKind</c> (the schema's closed vocabulary): what a description or a finding shows, never the raw value.</summary>
    internal static string FeeKindLabel(string? feeKind) => feeKind switch
    {
        "lateFee" => "late fee",
        "administrativeFee" => "administrative fee",
        "interest" => "interest",
        _ => "added fee",
    };

    private static (string, string) DescribeDiscount(JsonElement root)
    {
        var tolerance = root.TryGetProperty("tolerancePercent", out var t) ? t.GetRawText() : "0";
        return (
            $"Each fee line's billed rate is at most the rack rate less the agreed discount of {Num(root, "discountPercent")} percent, plus {tolerance} percent.",
            $"Discount(discountPercent={Num(root, "discountPercent")}, tolerancePercent={tolerance})");
    }

    private static int IndexOf(RuleType ruleType)
    {
        for (var i = 0; i < Types.Count; i++)
        {
            if (Types[i] == ruleType) return i;
        }

        return -1;
    }

    private static string Suffix(string name) => $"Services.Signals.Schemas.billing-{name}.schema.json";

    private static IReadOnlySet<string> Fields(params string[] names) => names.ToFrozenSet(StringComparer.Ordinal);

    private static IReadOnlySet<BillingConsequence> Consequences(params BillingConsequence[] values) => values.ToFrozenSet();

    private static string Num(JsonElement root, string name) => root.GetProperty(name).GetRawText();

    private static string List(JsonElement root, string name) =>
        string.Join(", ", root.GetProperty(name).EnumerateArray().Select(e => e.GetString()));
}
