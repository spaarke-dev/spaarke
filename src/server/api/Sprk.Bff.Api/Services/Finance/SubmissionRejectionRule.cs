using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>What the rejection rule decided for a whole submission (spec FR-26).</summary>
public enum SubmissionOutcome
{
    /// <summary>The submission is accepted. Every finding still stands as a Flag for the reviewer.</summary>
    Accepted,

    /// <summary>At least one finding met all three conditions of FR-26.</summary>
    Rejected,
}

/// <summary>
/// Why a finding did not reject (stable codes for the report and the logs). Exactly one per held finding: the first condition that
/// failed, in the order consequence, type, settings, read fields.
/// </summary>
public static class SubmissionHoldReasons
{
    /// <summary>The rule's consequence is Flag or Suggest adjustment.</summary>
    public const string ConsequenceNotReject = "consequence_not_reject";

    /// <summary>The version carries no consequence, or a value outside the three defined: treated as not Reject (fail closed).</summary>
    public const string ConsequenceUnknown = "consequence_unknown";

    /// <summary>The finding's reason never rejects, whatever the version's consequence (<see cref="BillingRuleCatalogue.IsFlagOnlyReason"/>:
    /// the invoice currency, D-115 and ontology-r1's 072 review F1).</summary>
    public const string ReasonFlagOnly = "reason_flag_only";

    /// <summary>The type is not reject-eligible (FR-26: TimeIncrement, DailyHoursCap, NonBillableActivity, AiObservation). A stored Reject on it is ignored (I-5).</summary>
    public const string TypeNotRejectEligible = "type_not_reject_eligible";

    /// <summary>D-89: ApprovedTimekeeper holds the invoice for review and never rejects it.</summary>
    public const string TypeHoldsOnly = "type_holds_only";

    /// <summary>ApprovedRate with no approved rate to compare against (not found, ambiguous, unresolved, no timekeeper or date): it holds (071 judgment call 2).</summary>
    public const string ReferenceMissingHolds = "reference_missing_holds";

    /// <summary>A Duplicate finding about a line: only an invoice-level Duplicate may reject (design.md 6.1).</summary>
    public const string DuplicateAtLineLevel = "duplicate_at_line_level";

    /// <summary>
    /// The finding's reason code is not on the type's reject-capable allowlist (<see cref="BillingRuleCatalogue.RejectCapableReasons"/>):
    /// a reason added later, or one that is flag-only by decision (a foreign currency, D-115), holds until it is listed on purpose
    /// (073 review F-ALLOW).
    /// </summary>
    public const string ReasonNotRejectCapable = "reason_not_reject_capable";

    /// <summary>
    /// The settings could not be read (the reader threw), so there is no threshold to apply. Transient: task 049 may retry the decision
    /// (073 review K1).
    /// </summary>
    public const string SettingsReadFailed = "settings_read_failed";

    /// <summary>
    /// The settings were read but break the rejection invariant (<see cref="EbillingSettings.RejectionInvariantProblem"/>), so the threshold
    /// is not applied. Not transient: it lasts until an administrator fixes the settings row; a retry decides the same (073 review K1).
    /// </summary>
    public const string SettingsInvalid = "settings_invalid";

    /// <summary>The finding lists no extracted field it read, so nothing shows its inputs were reliable.</summary>
    public const string NoReadFields = "no_read_fields";

    /// <summary>
    /// The finding read a field the AI assigned rather than read from the invoice (<see cref="BillingFieldNames.IsAiAssigned"/>: the coding
    /// Action's task, activity or expense code, or an AI observation). Its confidence is the model's own report, not a D-47 combined
    /// score, so it never supports a rejection (I-5; 073 review F1-a).
    /// </summary>
    public const string AiAssignedInput = "ai_assigned_input";

    /// <summary>A read field has no confidence, or one outside 0 to 1.</summary>
    public const string ReadFieldConfidenceUnknown = "read_field_confidence_unknown";

    /// <summary>A read field's combined confidence is below the reliability threshold.</summary>
    public const string ReadFieldBelowThreshold = "read_field_below_threshold";
}

/// <summary>The decision about one finding.</summary>
public sealed record FindingDecision
{
    /// <summary>The finding's position in the list that was decided (the evaluator's order).</summary>
    public required int Index { get; init; }

    public required BillingFinding Finding { get; init; }

    /// <summary>The rule version's consequence as the rule understood it; <c>null</c> when the version states none or an unknown value.</summary>
    public BillingConsequence? Consequence { get; init; }

    /// <summary>True when all three conditions of FR-26 held for this finding (and nothing held it). False: the finding is a Flag.</summary>
    public required bool Rejects { get; init; }

    /// <summary>A <see cref="SubmissionHoldReasons"/> code when <see cref="Rejects"/> is false; <c>null</c> when it rejects.</summary>
    public string? HeldBecause { get; init; }

    /// <summary>
    /// The read fields (as the finding lists them in <see cref="BillingFinding.ReadFields"/>) that were AI-assigned, unknown, outside 0 to
    /// 1 or below the threshold: what a reviewer should check. Empty unless the finding was held on its read fields.
    /// </summary>
    public IReadOnlyList<FieldRef> UnreliableFields { get; init; } = [];

    /// <summary>
    /// The values the finding read, for the rejection report and the Flag: the finding's own list, except that a withheld Duplicate
    /// finding (D-112, <see cref="BillingFinding.DetailWithheld"/>) shows no value text at all, whatever the finding holds.
    /// </summary>
    public required IReadOnlyList<BillingValueRead> ValuesRead { get; init; }
}

/// <summary>The outcome for a submission and the decision about every finding.</summary>
public sealed record SubmissionDecision
{
    public required SubmissionOutcome Outcome { get; init; }

    /// <summary>One entry per finding given, in the same order.</summary>
    public required IReadOnlyList<FindingDecision> Findings { get; init; }

    /// <summary>The threshold that was applied; <c>null</c> when it could not be used (see <see cref="SubmissionHoldReasons.SettingsReadFailed"/> and <see cref="SubmissionHoldReasons.SettingsInvalid"/>).</summary>
    public decimal? ReliabilityThreshold { get; init; }

    /// <summary>The findings that rejected the submission: empty when it was accepted.</summary>
    public IReadOnlyList<FindingDecision> DecidingFindings => Findings.Where(f => f.Rejects).ToArray();
}

/// <summary>
/// The rejection rule (ebilling-r1 task 073, spec FR-26, I-5, NFR-06; Processing Pipeline step 5): decides, from the findings of
/// <see cref="InvoiceComplianceEvaluator"/>, whether a submission is rejected. It rejects only when a finding meets all three
/// conditions at once, and otherwise accepts the submission with the failure as a Flag:
/// <list type="number">
/// <item>the rule's consequence is Reject;</item>
/// <item>every extracted field the finding read has a combined confidence (D-47) at or above the reliability threshold;</item>
/// <item>the rule type is reject-eligible (FR-26 positions 1, 2, 6 to 10).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>Fail closed.</b> Anything that cannot be established counts against rejecting: a consequence that is missing or unknown, a
/// read field with no confidence (or one outside 0 to 1), a finding that lists no read field, a threshold that cannot be read or breaks
/// <see cref="EbillingSettings.RejectionInvariantProblem"/>. Each gives Accepted plus a Flag, never Rejected.</para>
/// <para><b>Never on an AI-assigned input.</b> A finding that read a field the AI assigned (<see cref="BillingFieldNames.IsAiAssigned"/>)
/// holds, whatever that field's confidence: it is the model's own report, not a D-47 combined score (I-5).</para>
/// <para><b>Read fields are used as stated.</b> The decider checks the extracted values a finding lists
/// (<see cref="BillingFinding.ReadFields"/>, task 071) and never works out which fields a rule type reads. A comparison is
/// <c>confidence &gt;= threshold</c> against the threshold read from settings at decide time, not <see cref="BillingValueRead.BelowReliabilityThreshold"/>
/// (set against whatever the evaluation read), so a changed threshold applies to the next decision without a restart.</para>
/// <para><b>Holds beyond FR-26.</b> D-89: ApprovedTimekeeper never rejects, by type, not by 046's cap staying under the threshold.
/// 071 judgment call 2: an ApprovedRate finding with no approved rate to compare against never rejects; only
/// <see cref="BillingReasonCodes.RateAboveApproved"/> can. Duplicate rejects at invoice level only. A flag-only reason
/// (<see cref="BillingRuleCatalogue.IsFlagOnlyReason"/>, the invoice currency) never rejects, whatever its version's consequence. AiObservation
/// never rejects, even if a stored row says Reject (defence in depth beyond the publish check and the evaluation gate). And only a reason code on
/// the type's allowlist (<see cref="BillingRuleCatalogue.RejectCapableReasons"/>) can reject: a new reason code holds until it is listed.</para>
/// <para><b>No I/O but the settings read.</b> It writes nothing and renders nothing: task 049 persists the findings and the outcome.
/// A withheld Duplicate (D-112) carries no value text into <see cref="FindingDecision.ValuesRead"/>.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: none decides a submission outcome (grep "Reject" under
/// Services/Finance finds only <c>InvoiceReviewService</c>'s not-an-invoice rejection). Extension: no; the evaluator judges rules and
/// the handlers never decide (071). Cost of doing nothing: the pipeline either never rejects (FR-26 fails) or rejects on an AI
/// observation or a misread value (I-5, NFR-06).</para>
/// </remarks>
public sealed class SubmissionRejectionRule
{
    private readonly IEbillingSettingsReader _settings;
    private readonly ILogger<SubmissionRejectionRule> _logger;

    public SubmissionRejectionRule(IEbillingSettingsReader settings, ILogger<SubmissionRejectionRule> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Decides <paramref name="evaluation"/>: the settings are read now (a changed threshold applies at once), the consequence of each
    /// finding is its rule version's (<see cref="BillingEvaluationResult.RulesByVersion"/>), and a settings read that fails is logged
    /// and treated as "threshold unavailable" (Accepted plus Flag), not rethrown.
    /// </summary>
    public async Task<SubmissionDecision> DecideAsync(BillingEvaluationResult evaluation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        EbillingSettings? settings = null;
        try
        {
            settings = await _settings.GetAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Submission rejection rule: the eBilling settings could not be read, so the reliability threshold is unavailable. Nothing is rejected; every finding is a Flag.");
        }

        if (settings?.RejectionInvariantProblem is { } problem)
        {
            _logger.LogWarning(
                "Submission rejection rule: the eBilling settings cannot back a rejection ({Problem}). Nothing is rejected; every finding is a Flag.",
                problem);
        }

        var decision = Decide(
            evaluation.Findings,
            versionId => evaluation.RulesByVersion.TryGetValue(versionId, out var rule) ? rule.Consequence : null,
            settings);

        foreach (var stored in decision.Findings
                     .Where(f => f.Consequence == BillingConsequence.Reject
                                 && f.HeldBecause is SubmissionHoldReasons.TypeNotRejectEligible or SubmissionHoldReasons.TypeHoldsOnly)
                     .GroupBy(f => f.Finding.PolicyVersionId))
        {
            // A published rule cannot carry this (publish refuses it, and the evaluation gate refuses a stored row), so this is a
            // defence-in-depth hit worth seeing: the row was written outside the publish path.
            var first = stored.First().Finding;
            _logger.LogWarning(
                "Submission rejection rule: policy {PolicyCode} version {PolicyVersionId} ({RuleType}) has the consequence Reject, which its type may not carry ({Reason}); its {Count} finding(s) are Flags.",
                first.PolicyCode, first.PolicyVersionId, first.RuleType, stored.First().HeldBecause, stored.Count());
        }

        return decision;
    }

    /// <summary>
    /// The pure decision. <paramref name="consequenceOf"/> returns the <c>sprk_consequence</c> option value of a policy version
    /// (<c>null</c> = none). <paramref name="settings"/> is <c>null</c> when the settings could not be read.
    /// </summary>
    public static SubmissionDecision Decide(
        IReadOnlyList<BillingFinding> findings, Func<Guid, int?> consequenceOf, EbillingSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(consequenceOf);

        // Why the threshold cannot be used, when it cannot: not read at all (transient), or read but breaking the invariant (lasting).
        var settingsHold = settings is null
            ? SubmissionHoldReasons.SettingsReadFailed
            : settings.RejectionInvariantProblem is not null ? SubmissionHoldReasons.SettingsInvalid : null;
        decimal? threshold = settingsHold is null ? settings!.ReliabilityThreshold : null;

        var decided = new List<FindingDecision>(findings.Count);
        for (var i = 0; i < findings.Count; i++)
        {
            decided.Add(DecideFinding(i, findings[i], consequenceOf(findings[i].PolicyVersionId), threshold, settingsHold));
        }

        return new SubmissionDecision
        {
            Outcome = decided.Any(d => d.Rejects) ? SubmissionOutcome.Rejected : SubmissionOutcome.Accepted,
            Findings = decided,
            ReliabilityThreshold = threshold,
        };
    }

    private static FindingDecision DecideFinding(
        int index, BillingFinding finding, int? consequenceValue, decimal? threshold, string? settingsHold)
    {
        BillingConsequence? consequence = consequenceValue is { } v && Enum.IsDefined(typeof(BillingConsequence), v)
            ? (BillingConsequence)v
            : null;
        var values = ValuesOf(finding);

        FindingDecision Held(string reason, IReadOnlyList<FieldRef>? unreliable = null) => new()
        {
            Index = index,
            Finding = finding,
            Consequence = consequence,
            Rejects = false,
            HeldBecause = reason,
            UnreliableFields = unreliable ?? [],
            ValuesRead = values,
        };

        // 1. The rule's consequence is Reject.
        if (consequence is null)
        {
            return Held(SubmissionHoldReasons.ConsequenceUnknown);
        }

        if (consequence != BillingConsequence.Reject)
        {
            return Held(SubmissionHoldReasons.ConsequenceNotReject);
        }

        // 1b. The reason may carry the Reject: a flag-only reason (the invoice currency) never does.
        if (BillingRuleCatalogue.IsFlagOnlyReason(finding.ReasonCode))
        {
            return Held(SubmissionHoldReasons.ReasonFlagOnly);
        }

        // 2. The type is reject-eligible, and nothing narrows it for this finding.
        if (!RuleDomains.IsBilling(finding.RuleType) || !BillingRuleCatalogue.IsRejectEligible(finding.RuleType))
        {
            return Held(SubmissionHoldReasons.TypeNotRejectEligible);
        }

        if (BillingRuleCatalogue.IsHoldOnly(finding.RuleType))
        {
            return Held(SubmissionHoldReasons.TypeHoldsOnly);
        }

        if (finding.RuleType == RuleType.ApprovedRate && finding.ReasonCode != BillingReasonCodes.RateAboveApproved)
        {
            return Held(SubmissionHoldReasons.ReferenceMissingHolds);
        }

        if (finding.RuleType == RuleType.Duplicate && finding.Subject.Kind != BillingSubjectKind.Invoice)
        {
            return Held(SubmissionHoldReasons.DuplicateAtLineLevel);
        }

        // Only a reason code the catalogue lists for the type may reject; any other holds by default (073 review F-ALLOW).
        if (!BillingRuleCatalogue.IsRejectCapableReason(finding.RuleType, finding.ReasonCode))
        {
            return Held(SubmissionHoldReasons.ReasonNotRejectCapable);
        }

        // 3. The threshold can be used.
        if (threshold is not { } limit)
        {
            return Held(settingsHold ?? SubmissionHoldReasons.SettingsReadFailed);
        }

        // 4. Every extracted field the finding read is reliable. The list is the finding's own; none listed is no evidence.
        var readFields = finding.ReadFields.Distinct().ToArray();
        if (readFields.Length == 0)
        {
            return Held(SubmissionHoldReasons.NoReadFields);
        }

        // Never on an AI-assigned input (I-5): its confidence is the model's self-report, which D-47 does not let gate a rejection.
        var aiAssigned = readFields.Where(f => BillingFieldNames.IsAiAssigned(f.Field)).ToArray();
        if (aiAssigned.Length > 0)
        {
            return Held(SubmissionHoldReasons.AiAssignedInput, aiAssigned);
        }

        var unknown = new List<FieldRef>();
        var below = new List<FieldRef>();
        foreach (var field in readFields)
        {
            // The confidence of a listed field is the one on the value that carries it; a field with no such value, or any value
            // with no confidence or one outside 0 to 1, is unknown. A field listed twice must pass on every value.
            var confidences = finding.ValuesRead.Where(v => v.ExtractedField == field).Select(v => v.Confidence).ToArray();
            if (confidences.Length == 0 || confidences.Any(c => c is null or < 0m or > 1m))
            {
                unknown.Add(field);
            }
            else if (confidences.Any(c => c < limit))
            {
                below.Add(field);
            }
        }

        if (unknown.Count > 0)
        {
            return Held(SubmissionHoldReasons.ReadFieldConfidenceUnknown, unknown);
        }

        if (below.Count > 0)
        {
            return Held(SubmissionHoldReasons.ReadFieldBelowThreshold, below);
        }

        return new FindingDecision
        {
            Index = index,
            Finding = finding,
            Consequence = consequence,
            Rejects = true,
            ValuesRead = values,
        };
    }

    /// <summary>The finding's values, with the value text left out of a withheld Duplicate (D-112) however the finding was built.</summary>
    private static IReadOnlyList<BillingValueRead> ValuesOf(BillingFinding finding) =>
        finding.DetailWithheld
            ? finding.ValuesRead.Select(v => v with { Value = null }).ToArray()
            : finding.ValuesRead;
}
