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
        return Build(
            policy,
            BillingSubject.ForInvoice(),
            BillingReasonCodes.SubmissionInvoiceNumberMismatch,
            [
                new BillingValueRead { Name = "submission.invoiceNumber", Value = typedNumber },
                new BillingValueRead
                {
                    Name = "invoice.invoiceNumber",
                    Value = readNumber,
                    ExtractedField = new Finance.Resolution.FieldRef(null, BillingFieldNames.FirmInvoiceNumber),
                    Confidence = readConfidence,
                },
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["submission.invoiceNumber"] = typedNumber,
                ["invoice.invoiceNumber"] = readNumber,
            });
    }

    /// <summary>047 (FR-22): a code the coding service proposed was not allowed for the line in its code set. Line level.</summary>
    public static BillingFinding CodeRefused(BillingSystemPolicyVersion policy, int lineIndex, RefusedCode refused)
    {
        ArgumentNullException.ThrowIfNull(refused);
        Require(refused.Code, nameof(refused));
        var kind = KindText(refused.Kind);
        return Build(
            policy,
            BillingSubject.ForLine(lineIndex),
            BillingReasonCodes.CodingRefusedCode,
            [
                new BillingValueRead { Name = "line.codeKind", Value = kind },
                new BillingValueRead { Name = "line.refusedCode", Value = refused.Code },
            ],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["line.codeKind"] = kind, ["line.refusedCode"] = refused.Code });
    }

    /// <summary>047 (D-05): the firm's code on a slot differs from the code suggested for it. Line level.</summary>
    public static BillingFinding FirmCodeMismatch(
        BillingSystemPolicyVersion policy, int lineIndex, BillingCodeKind kind, string firmCode, string suggestedCode)
    {
        Require(firmCode, nameof(firmCode));
        Require(suggestedCode, nameof(suggestedCode));
        var kindText = KindText(kind);
        return Build(
            policy,
            BillingSubject.ForLine(lineIndex),
            BillingReasonCodes.CodingFirmCodeMismatch,
            [
                new BillingValueRead { Name = "line.codeKind", Value = kindText },
                new BillingValueRead { Name = "line.firmCode", Value = firmCode },
                new BillingValueRead { Name = "line.suggestedCode", Value = suggestedCode },
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["line.codeKind"] = kindText,
                ["line.firmCode"] = firmCode,
                ["line.suggestedCode"] = suggestedCode,
            });
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
