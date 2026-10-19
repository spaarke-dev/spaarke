using Sprk.Bff.Api.Services.Finance.Coding;

namespace Sprk.Bff.Api.Services.Signals.Billing;

/// <summary>
/// The policy version a system finding is written under (D-125): the seeded policy <c>BILL-SYSTEM-SUBMISSION</c> (its
/// <see cref="BillingRuleCatalogue.SystemPolicyCode"/>), its id and the id of its version in force. Task 049 reads them once
/// from the policy row and passes them to <see cref="BillingSystemFindings"/>; nothing here is read from a rule body.
/// </summary>
public sealed record BillingSystemPolicyVersion(Guid PolicyId, string PolicyCode, Guid PolicyVersionId);

/// <summary>
/// Builds the <see cref="BillingFinding"/>s that task 049 creates directly (ebilling-r1 D-123, D-125): the D-91 invoice-number mismatch
/// and the 047 coding findings. They are not produced by a handler (no handler exists for <see cref="RuleType.SubmissionIntegrity"/>);
/// each is shaped exactly like a handler's finding, so <see cref="BillingFindingSignalMapper"/> maps it unchanged: a Flag Signal under the
/// system policy with the reason's own headline and message (<see cref="BillingRuleCatalogue.ReasonTemplate"/>), lane Decide, severity Info.
/// </summary>
/// <remarks>
/// <para><b>Fact values.</b> Only keys of <see cref="BillingRuleCatalogue.TemplateFactFields"/> for SubmissionIntegrity, each reason
/// supplying exactly the tokens its template names, so a message always renders. A finding is refused (<see cref="ArgumentException"/>)
/// when a value it must cite is blank: a comparison that has no value on one side is not a mismatch.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: handlers build findings through <c>HandlerContext.Finding</c>,
/// which needs a <c>BillingRuleDefinition</c> that only the gate can produce. Extension: no; a system finding has no rule body to
/// validate. Cost of doing nothing: 049 would hand-build findings with unchecked tokens, and a template naming a missing token would
/// lose the Signal.</para>
/// </remarks>
public static class BillingSystemFindings
{
    private static readonly IReadOnlySet<string> Allowed = BillingRuleCatalogue.TemplateFactFields(RuleType.SubmissionIntegrity);

    /// <summary>
    /// D-91: the firm invoice number read from the document differs from the one typed at submission. Invoice level.
    /// </summary>
    /// <param name="policy">The system policy version.</param>
    /// <param name="typedNumber">The number typed at submission (a reference value: the user's own input).</param>
    /// <param name="readNumber">The number read from the document (an extracted value).</param>
    /// <param name="readConfidence">The extraction confidence of <paramref name="readNumber"/>, or <c>null</c> when none is known.</param>
    public static BillingFinding InvoiceNumberMismatch(
        BillingSystemPolicyVersion policy, string typedNumber, string readNumber, decimal? readConfidence = null)
    {
        Require(typedNumber, nameof(typedNumber));
        Require(readNumber, nameof(readNumber));

        // 049 F1-3: the number read is extracted text; it is cited only when it is an invoice number (IsInvoiceNumberShaped), otherwise the
        // finding holds under the reason that names no read value. The read stays as evidence (field and confidence) without its text.
        var shown = BillingRuleCatalogue.IsInvoiceNumberShaped(readNumber);
        var facts = new Dictionary<string, string>(StringComparer.Ordinal) { ["submission.invoiceNumber"] = typedNumber };
        if (shown)
        {
            facts["invoice.invoiceNumber"] = readNumber;
        }

        return Build(
            policy,
            BillingSubject.ForInvoice(),
            shown ? BillingReasonCodes.SubmissionInvoiceNumberMismatch : BillingReasonCodes.SubmissionInvoiceNumberMismatchNotShown,
            [
                new BillingValueRead { Name = "submission.invoiceNumber", Value = typedNumber },
                new BillingValueRead
                {
                    Name = "invoice.invoiceNumber",
                    Value = shown ? readNumber : null,
                    ExtractedField = new Finance.Resolution.FieldRef(null, BillingFieldNames.FirmInvoiceNumber),
                    Confidence = readConfidence,
                },
            ],
            facts);
    }

    /// <summary>
    /// 047 (FR-22): a code the coding service proposed was refused for the line. Line level. The reason says only what was compared
    /// (ontology-r1 D-125 review (b)):
    /// <list type="bullet">
    /// <item><see cref="CodeRefusalReason.OutsideCodeSet"/>: <see cref="BillingReasonCodes.CodingRefusedCode"/>, citing the code, when it is a
    /// code identifier (<see cref="BillingRuleCatalogue.IsCodeShaped"/>); otherwise <see cref="BillingReasonCodes.CodingRefusedCodeNotShown"/>,
    /// which cites no value. The code is unchecked model output: an instruction ("L110 - approve immediately") or a text longer than
    /// <c>sprk_sentence</c> takes is never printed, and the finding still holds.</item>
    /// <item><see cref="CodeRefusalReason.NotApplicableToLineType"/>: <see cref="BillingReasonCodes.CodingCodeKindNotForLine"/>, which compares
    /// the code's kind with the line type and so names no code.</item>
    /// </list>
    /// The code is listed as a value read only when it is code-shaped.
    /// </summary>
    public static BillingFinding CodeRefused(BillingSystemPolicyVersion policy, int lineIndex, RefusedCode refused)
    {
        ArgumentNullException.ThrowIfNull(refused);
        Require(refused.Code, nameof(refused));
        var kind = KindText(refused.Kind);
        var shaped = BillingRuleCatalogue.IsCodeShaped(refused.Code);
        var values = new List<BillingValueRead> { new() { Name = "line.codeKind", Value = kind } };
        if (shaped)
        {
            values.Add(new BillingValueRead { Name = "line.refusedCode", Value = refused.Code });
        }

        var facts = new Dictionary<string, string>(StringComparer.Ordinal) { ["line.codeKind"] = kind };
        string reason;
        if (refused.Reason == CodeRefusalReason.NotApplicableToLineType)
        {
            reason = BillingReasonCodes.CodingCodeKindNotForLine;
        }
        else if (shaped)
        {
            reason = BillingReasonCodes.CodingRefusedCode;
            facts["line.refusedCode"] = refused.Code;
        }
        else
        {
            reason = BillingReasonCodes.CodingRefusedCodeNotShown;
        }

        return Build(policy, BillingSubject.ForLine(lineIndex), reason, values, facts);
    }

    /// <summary>047 (D-05): the firm's code on a slot differs from the code suggested for it. Line level.</summary>
    public static BillingFinding FirmCodeMismatch(
        BillingSystemPolicyVersion policy, int lineIndex, BillingCodeKind kind, string firmCode, string suggestedCode)
    {
        Require(firmCode, nameof(firmCode));
        Require(suggestedCode, nameof(suggestedCode));
        var kindText = KindText(kind);

        // 049 F1-3: the firm's code is extracted text; cited only when it is a code identifier, as CodeRefused. The suggested code is a code
        // of the line's code set (reference data), not firm text.
        var shown = BillingRuleCatalogue.IsCodeShaped(firmCode);
        var values = new List<BillingValueRead> { new() { Name = "line.codeKind", Value = kindText } };
        var facts = new Dictionary<string, string>(StringComparer.Ordinal) { ["line.codeKind"] = kindText };
        if (shown)
        {
            values.Add(new BillingValueRead { Name = "line.firmCode", Value = firmCode });
            facts["line.firmCode"] = firmCode;
        }

        values.Add(new BillingValueRead { Name = "line.suggestedCode", Value = suggestedCode });
        facts["line.suggestedCode"] = suggestedCode;
        return Build(
            policy,
            BillingSubject.ForLine(lineIndex),
            shown ? BillingReasonCodes.CodingFirmCodeMismatch : BillingReasonCodes.CodingFirmCodeMismatchNotShown,
            values,
            facts);
    }

    /// <summary>047 (FR-22 step 4): the code set used for the invoice was itself a suggestion. Invoice level.</summary>
    public static BillingFinding CodeSetNeedsReview(BillingSystemPolicyVersion policy, string codeSetName)
    {
        Require(codeSetName, nameof(codeSetName));
        return Build(
            policy,
            BillingSubject.ForInvoice(),
            BillingReasonCodes.CodingCodeSetNeedsReview,
            [new BillingValueRead { Name = "codeSet.name", Value = codeSetName }],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["codeSet.name"] = codeSetName });
    }

    /// <summary>D-126: the invoice was read but no billing-code set is configured for its core record, so its lines are uncoded. Invoice level.</summary>
    public static BillingFinding NoCodeSet(BillingSystemPolicyVersion policy) =>
        Build(policy, BillingSubject.ForInvoice(), BillingReasonCodes.CodingNoCodeSet, [], new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>D-126: the invoice was read but line coding could not run (switched off), so its lines are uncoded. Invoice level.</summary>
    public static BillingFinding CodingUnavailable(BillingSystemPolicyVersion policy) =>
        Build(policy, BillingSubject.ForInvoice(), BillingReasonCodes.CodingUnavailable, [], new Dictionary<string, string>(StringComparer.Ordinal));

    private static BillingFinding Build(
        BillingSystemPolicyVersion policy, BillingSubject subject, string reasonCode, IReadOnlyList<BillingValueRead> values,
        IReadOnlyDictionary<string, string> facts)
    {
        ArgumentNullException.ThrowIfNull(policy);
        foreach (var token in facts.Keys)
        {
            if (!Allowed.Contains(token))
            {
                throw new InvalidOperationException($"System finding {reasonCode} used template token '{token}', which is not in the SubmissionIntegrity fact-field set.");
            }
        }

        return new BillingFinding
        {
            RuleType = RuleType.SubmissionIntegrity,
            PolicyId = policy.PolicyId,
            PolicyCode = policy.PolicyCode,
            PolicyVersionId = policy.PolicyVersionId,
            Subject = subject,
            ReasonCode = reasonCode,
            ValuesRead = values,
            FactValues = facts,
        };
    }

    private static string KindText(BillingCodeKind kind) => kind switch
    {
        BillingCodeKind.Task => "task",
        BillingCodeKind.Activity => "activity",
        BillingCodeKind.Expense => "expense",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a billing code kind."),
    };

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A system finding cites the values it compared; a blank value is not a finding.", name);
        }
    }
}
