using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Services.Signals.Billing;

/// <summary>Which text became the Signal's message.</summary>
public enum BillingMessageSource
{
    /// <summary>The policy version's <c>sprk_messagetemplate</c>.</summary>
    PolicyVersion,

    /// <summary><see cref="BillingFindingSignalMapper.InvoiceDuplicateTemplate"/>: an invoice-level Duplicate has no line tokens.</summary>
    InvoiceLevelDuplicate,

    /// <summary><see cref="BillingRuleCatalogue.WithheldDuplicateTemplate"/> (D-112): the prior is across the secure wall or unresolved.</summary>
    WithheldDuplicate,

    /// <summary>The reason's own message (<see cref="BillingRuleCatalogue.ReasonTemplate"/>), whatever the version's template says:
    /// a reason about the invoice's currency, or one whose date wording would be false (a Closed core record, a close date it lacks).</summary>
    ReasonTemplate,

    /// <summary>The type's default wording (<c>BillingRuleDraftTranslator.Wording</c>): the version's template is blank or names a
    /// token this finding has no value for.</summary>
    DefaultWording,

    /// <summary>The type's default headline as a sentence: no template's tokens are all present for this finding.</summary>
    HeadlineOnly,

    /// <summary>The Blocked outcome's held wording (<see cref="BillingFindingSignalMapper.HeldSentenceTemplate"/>, or
    /// <see cref="BillingFindingSignalMapper.HeldSentenceNoValue"/> when the deciding reason would not fit the sentence limit).</summary>
    HeldForReview,
}

/// <summary>How the rejection rule (073) decided a merged Signal's reasons, when the caller passed its decisions (task 049 part D).</summary>
public enum BillingGroupDecision
{
    /// <summary>No decisions were given (the mapping is the rule's configured view only).</summary>
    NotDecided,

    /// <summary>A merged reason rejected the submission: the primary reason is the first deciding finding in reason order.</summary>
    Rejects,

    /// <summary>The group's consequence is Reject but 073 held every reason: not presented as a rejection; the evidence carries a
    /// <see cref="BillingFindingSignalMapper.DecisionEvidenceKind"/> line <c>Held</c> with the hold reason.</summary>
    Held,

    /// <summary>A merged reason rejects, but the submission's outcome is Blocked (an Internal submission the rule would reject is held for
    /// review, not rejected): the Signal says the invoice is held (<see cref="BillingFindingSignalMapper.HeldHeadline"/>,
    /// <see cref="BillingFindingSignalMapper.HeldSentenceTemplate"/>) and the evidence carries a
    /// <see cref="BillingFindingSignalMapper.DecisionEvidenceKind"/> line <c>Held</c> with <see cref="SubmissionReasonCodes.Blocked"/>.</summary>
    Blocked,

    /// <summary>The group's consequence is not Reject: nothing to present.</summary>
    NotRejecting,
}

/// <summary>The rows 049 creates for the evaluated invoice: the invoice and each line by its <see cref="BillingLineFacts.Index"/>.</summary>
public sealed record BillingSignalSubjects(
    Guid InvoiceId,
    string? InvoiceDisplayName,
    IReadOnlyDictionary<int, Guid> LineIds,
    IReadOnlyDictionary<int, string>? LineDisplayNames = null);

/// <summary>
/// One Signal write: every finding of one policy on one subject, merged (ontology-r1's Signal dedupe option a), and where its
/// message came from.
/// </summary>
/// <param name="Request">The write. Its message is the primary reason's (the strongest consequence, then the first reason code).</param>
/// <param name="MessageSource">Where the primary reason's message came from.</param>
public sealed record BillingSignalMapping(SignalWriteRequest Request, BillingMessageSource MessageSource)
{
    /// <summary>The merged findings, in reason order (reason code, ordinal; then their content). Task 049's Decision Record fact
    /// snapshot lists every one.</summary>
    public IReadOnlyList<BillingFinding> Findings { get; init; } = [];

    /// <summary>The reason code of each merged finding, sorted (ordinal); a code repeats when two findings share it.</summary>
    public IReadOnlyList<string> ReasonCodes { get; init; } = [];

    /// <summary>The strongest consequence of the merged reasons (Reject over Suggest adjustment over Flag). A flag-only reason
    /// (<see cref="BillingRuleCatalogue.IsFlagOnlyReason"/>) counts as Flag; a version with no or an unknown consequence as Flag.</summary>
    public BillingConsequence Consequence { get; init; } = BillingConsequence.Flag;

    /// <summary>The reason-set fingerprint (<see cref="ReasonCodes"/> and <see cref="Consequence"/>), also stored in the Signal's
    /// evidence under <see cref="BillingFindingSignalMapper.FingerprintEvidenceKind"/>. Task 049 compares it with the open Signal's to
    /// decide between a refresh and Superseded plus episode n+1; the mapper does not supersede.</summary>
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>How 073 decided the group (task 049 part D); <see cref="BillingGroupDecision.NotDecided"/> when no decisions were given.</summary>
    public BillingGroupDecision Decision { get; init; } = BillingGroupDecision.NotDecided;
}

/// <summary>
/// Maps the findings of one evaluation to the <see cref="SignalWriteRequest"/>s task 049 hands <see cref="SignalWriter"/> (ebilling-r1
/// task 072, FR-25): exactly one request per (policy code, subject), so no two requests of one evaluation share the writer's dedupe key.
/// Pure: no I/O. The request always passes the writer's template and fact checks (every <c>{{token}}</c> of the message has a value),
/// so a finding is never lost to a template that names a value this finding does not have.
/// </summary>
/// <remarks>
/// <para><b>One Signal per (policy, subject) (ontology-r1, Signal dedupe option a, 2026-10-18).</b> The writer's key is
/// <c>policy|subjectEntity|subjectId|episode</c> and a write to an open row only refreshes it, so a second finding of one policy on one
/// subject (an ExpenseRule code and cap on one line, RequiredData with several missing fields) was silently dropped. <see cref="MapAll(IReadOnlyList{BillingFinding}, IReadOnlyDictionary{Guid, BillingRuleCandidate}, BillingSignalSubjects, IReadOnlyList{FindingDecision}, SubmissionEvaluationKind?)"/>
/// merges them: the reason codes sorted (ordinal); the strongest consequence wins (<see cref="BillingSignalMapping.Consequence"/>);
/// the evidence holds one <see cref="ReasonEvidenceKind"/> line per reason with that reason's own text, the union of the values read
/// (<see cref="FieldEvidenceKind"/> for an extracted field with its confidence, <see cref="ReferenceEvidenceKind"/> for a reference
/// value), and the reason-set fingerprint (<see cref="FingerprintEvidenceKind"/>). The message is the primary reason's: the strongest
/// consequence, then the first in reason order.</para>
/// <para><b>Withheld detail (D-112) in a merged Signal.</b> When any merged finding is a withheld Duplicate, the message is
/// <see cref="BillingRuleCatalogue.WithheldDuplicateTemplate"/> with no fact values, every reason's text is token-free (the withheld
/// template, the reason's own message or the type's headline), and no value text is stored: a duplicate line's own date, amount and
/// hours equal the hidden prior's, so no merged reason or field may name them.</para>
/// <para><b>Message.</b> The first of these whose tokens all have a value: a withheld Duplicate (D-112, 071 fix round 2) always takes
/// <see cref="BillingRuleCatalogue.WithheldDuplicateTemplate"/> with no fact values at all, so no withheld value reaches the Signal;
/// a reason with its own message (<see cref="BillingRuleCatalogue.ReasonTemplate"/>) takes it; otherwise the policy version's template, then (an invoice-level Duplicate only) <see cref="InvoiceDuplicateTemplate"/>, then the
/// type's default wording, then the default headline as a sentence. 071's findings leave out a token with no value for the subject
/// (a line token on an invoice-level finding, a timekeeper name that was not read), which is why the chain exists.</para>
/// <para><b>Facts.</b> Only the keys <see cref="BillingRuleCatalogue.TemplateFactFields"/> allows for the type, as 071 built them.</para>
/// <para><b>Subject.</b> <c>sprk_invoice</c> for an invoice-level finding, <c>sprk_billingevent</c> for a line (both verified Signal
/// subjects, task 026). <b>Lane</b> is the policy's, Decide when it has none; <b>severity</b> the policy's; the <b>headline</b> the
/// version's, else the type's default. Exception (ontology-r1 re-check F2): when the primary reason has its own message
/// (<see cref="BillingRuleCatalogue.ReasonTemplate"/>) the headline is its own too (<see cref="BillingRuleCatalogue.ReasonHeadline"/>),
/// and when the primary reason is flag-only the lane is Decide and the severity Info, not the host rule's.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <c>SignalEvaluationService</c> builds Ontology requests from a
/// predicate hit, not a billing finding. Extension: no; a billing finding's subject is a line index and its message needs the D-112
/// withheld variant. Cost of doing nothing: 049 has no tested way to turn a finding into a write the writer accepts.</para>
/// </remarks>
public static class BillingFindingSignalMapper
{
    /// <summary>The Duplicate message for an invoice-level match: names only invoice tokens.</summary>
    public const string InvoiceDuplicateTemplate = "Invoice {{invoice.firmInvoiceNumber}} duplicates {{duplicateOf.trackingNumber}}.";

    /// <summary>Evidence kind of the reason-set fingerprint line: <c>Ref</c> is <see cref="BillingSignalMapping.Fingerprint"/>.</summary>
    public const string FingerprintEvidenceKind = "billing.reasonset";

    /// <summary>Evidence kind of one merged reason: <c>Ref</c> its reason code, <c>Text</c> its own sentence, <c>Source</c> its
    /// policy version (<c>sprk_policyversion:{id}</c>).</summary>
    public const string ReasonEvidenceKind = "billing.reason";

    /// <summary>Evidence kind of an extracted value read: <c>Ref</c> the field (<c>invoice.{Field}</c> or <c>line[{n}].{Field}</c>),
    /// <c>Text</c> the value, <c>Confidence</c> its combined confidence, <c>Source</c> its name.</summary>
    public const string FieldEvidenceKind = "billing.field";

    /// <summary>Evidence kind of a reference value read (an approved rate, a tracking number): <c>Ref</c> its name, <c>Text</c> the value.</summary>
    public const string ReferenceEvidenceKind = "billing.reference";

    /// <summary>Evidence kind of 073's decision on a group whose consequence is Reject but which did not reject (task 049 part D, 072 part W
    /// K1): <c>Ref</c> <see cref="HeldDecisionRef"/>, <c>Text</c> the primary reason's <see cref="SubmissionHoldReasons"/> code.</summary>
    public const string DecisionEvidenceKind = "billing.decision";

    /// <summary>The <c>Ref</c> of a <see cref="DecisionEvidenceKind"/> line: the rule held the reasons for review.</summary>
    public const string HeldDecisionRef = "Held";

    /// <summary>The headline of a Signal whose group decision is <see cref="BillingGroupDecision.Blocked"/> (the invoice is held for review).</summary>
    public const string HeldHeadline = "Invoice held for review";

    /// <summary>The fact (<see cref="HeldSentenceTemplate"/>'s one token) that carries the deciding finding's rendered reason.</summary>
    public const string HeldReasonFact = "decidingReason";

    /// <summary>The sentence of a <see cref="BillingGroupDecision.Blocked"/> Signal: the rejection rule's own deciding reason, as it is
    /// rendered everywhere else (never a raw code).</summary>
    public const string HeldSentenceTemplate = "The rejection rule would reject this invoice: {{" + HeldReasonFact + "}}";

    /// <summary>The no-value wording: used when <see cref="HeldSentenceTemplate"/> with the deciding reason would be refused by the writer's
    /// own checks (the sentence would exceed its limit). The reason is left out whole, never truncated; it stays in its evidence line.</summary>
    public const string HeldSentenceNoValue = "The rejection rule would reject this invoice.";

    internal const string InvoiceEntity = "sprk_invoice";
    internal const string LineEntity = "sprk_billingevent";
    private const int MaxHeadlineLength = 200;

    /// <summary>The lane of a Signal whose primary reason is flag-only: a reviewer's item, never a Do (action) item.</summary>
    internal const int FlagOnlyLane = SignalWriter.LaneDecide;

    /// <summary>The severity of a Signal whose primary reason is flag-only: the lowest, so a note never ranks like a breach.</summary>
    internal const int FlagOnlySeverity = SignalWriter.SeverityInfo;

    /// <summary>The Signal writes of one evaluation: <see cref="MapAll(IReadOnlyList{BillingFinding}, IReadOnlyDictionary{Guid, BillingRuleCandidate}, BillingSignalSubjects, IReadOnlyList{FindingDecision}, SubmissionEvaluationKind?)"/>
    /// over its findings and rule versions.</summary>
    public static IReadOnlyList<BillingSignalMapping> MapAll(
        BillingEvaluationResult evaluation, BillingSignalSubjects subjects, IReadOnlyList<FindingDecision>? decisions = null,
        SubmissionEvaluationKind? outcome = null)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        return MapAll(evaluation.Findings, evaluation.RulesByVersion, subjects, decisions, outcome);
    }

    /// <summary>
    /// Maps every finding onto the rows in <paramref name="subjects"/>, one request per (policy code, subject), in the order each pair
    /// first appears among <paramref name="findings"/>.
    /// </summary>
    /// <param name="findings">The evaluation's findings, in its order.</param>
    /// <param name="rulesByVersion">The rule version behind each finding.</param>
    /// <param name="subjects">The invoice and line rows.</param>
    /// <param name="decisions">Optional (task 049 part D, 072 part W K1): 073's decision about each finding, <see cref="FindingDecision.Index"/>
    /// = the finding's position in <paramref name="findings"/>. A group holding a deciding finding (<see cref="FindingDecision.Rejects"/>) takes
    /// the first deciding finding in reason order as its primary reason (message, headline, lane, severity), so the firm sees the reason that
    /// actually rejected; a group whose consequence is Reject but which 073 held throughout is not presented as a rejection (a
    /// <see cref="DecisionEvidenceKind"/> line). The fingerprint does not depend on them.</param>
    /// <param name="outcome">Optional (task 049, Held presentation): how the pipeline classified the submission. With
    /// <see cref="SubmissionEvaluationKind.Blocked"/> a group holding a deciding finding is presented as held for review
    /// (<see cref="BillingGroupDecision.Blocked"/>: <see cref="HeldHeadline"/>, <see cref="HeldSentenceTemplate"/> and a
    /// <see cref="DecisionEvidenceKind"/> <c>Held</c> line with <see cref="SubmissionReasonCodes.Blocked"/>); any other value changes nothing.
    /// The caller does not special-case it: it passes the outcome and stores what the mapper returns.</param>
    /// <exception cref="ArgumentException">A finding's version is not in <paramref name="rulesByVersion"/>, or its line has no row in
    /// <paramref name="subjects"/>, or <paramref name="decisions"/> is not one decision per finding, of that finding, or
    /// <paramref name="outcome"/> is Blocked and <paramref name="decisions"/> is null (nothing says which reason would reject).</exception>
    public static IReadOnlyList<BillingSignalMapping> MapAll(
        IReadOnlyList<BillingFinding> findings, IReadOnlyDictionary<Guid, BillingRuleCandidate> rulesByVersion, BillingSignalSubjects subjects,
        IReadOnlyList<FindingDecision>? decisions = null, SubmissionEvaluationKind? outcome = null)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(rulesByVersion);
        ArgumentNullException.ThrowIfNull(subjects);
        if (outcome == SubmissionEvaluationKind.Blocked && decisions is null)
        {
            throw new ArgumentException("A Blocked outcome needs the rejection rule's decisions: they say which reason would reject.", nameof(decisions));
        }

        var decisionOf = DecisionsByIndex(findings, decisions);

        var groups = new List<List<(BillingFinding Finding, BillingRuleCandidate Rule, FindingDecision? Decision)>>();
        var byKey = new Dictionary<string, List<(BillingFinding, BillingRuleCandidate, FindingDecision?)>>(StringComparer.Ordinal);
        for (var index = 0; index < findings.Count; index++)
        {
            var finding = findings[index];
            if (!rulesByVersion.TryGetValue(finding.PolicyVersionId, out var rule))
            {
                throw new ArgumentException($"No rule version {finding.PolicyVersionId} was given for a finding of policy {finding.PolicyCode}.", nameof(rulesByVersion));
            }

            var (entity, subjectId, _) = SubjectOf(finding.Subject, subjects);
            var key = SignalWriter.DedupeKeySubjectPrefix(rule.PolicyCode, entity, subjectId.ToString("D").ToLowerInvariant());
            if (!byKey.TryGetValue(key, out var group))
            {
                byKey[key] = group = [];
                groups.Add(group);
            }

            group.Add((finding, rule, decisionOf?[index]));
        }

        return groups.Select(g => MapGroup(g, subjects, decided: decisionOf is not null, blocked: outcome == SubmissionEvaluationKind.Blocked)).ToList();
    }

    private static FindingDecision[]? DecisionsByIndex(IReadOnlyList<BillingFinding> findings, IReadOnlyList<FindingDecision>? decisions)
    {
        if (decisions is null)
        {
            return null;
        }

        if (decisions.Count != findings.Count)
        {
            throw new ArgumentException("The decisions must cover every finding, one each.", nameof(decisions));
        }

        var byIndex = new FindingDecision[findings.Count];
        foreach (var decision in decisions)
        {
            if (decision is null || decision.Index < 0 || decision.Index >= findings.Count
                || !SameFinding(decision.Finding, findings[decision.Index]) || byIndex[decision.Index] is not null)
            {
                throw new ArgumentException("Each decision must be the rejection rule's decision about the finding at its index.", nameof(decisions));
            }

            byIndex[decision.Index] = decision;
        }

        return byIndex;

        // The decision names the finding at its index: the same version, subject and reason (a copy made with `with` still matches).
        static bool SameFinding(BillingFinding? decided, BillingFinding finding) =>
            decided is not null && decided.PolicyVersionId == finding.PolicyVersionId && decided.Subject == finding.Subject
            && string.Equals(decided.ReasonCode, finding.ReasonCode, StringComparison.Ordinal);
    }

    /// <summary>One finding alone (a group of one). Internal: 049 maps an evaluation with <see cref="MapAll(BillingEvaluationResult, BillingSignalSubjects, IReadOnlyList{FindingDecision}, SubmissionEvaluationKind?)"/>,
    /// so two findings of one policy on one subject are never sent as two requests.</summary>
    /// <exception cref="ArgumentException">The finding is not <paramref name="rule"/>'s, or its line has no row in <paramref name="subjects"/>.</exception>
    internal static BillingSignalMapping Map(BillingFinding finding, BillingRuleCandidate rule, BillingSignalSubjects subjects)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(subjects);
        if (finding.PolicyVersionId != rule.PolicyVersionId)
        {
            throw new ArgumentException("The finding was not produced by this rule version.", nameof(rule));
        }

        return MapGroup([(finding, rule, null)], subjects, decided: false, blocked: false);
    }

    /// <summary>The reason-set fingerprint: the sorted reason codes and the strongest consequence.</summary>
    internal static string FingerprintOf(IEnumerable<string> sortedReasonCodes, BillingConsequence consequence) =>
        string.Join(",", sortedReasonCodes) + "|" + consequence;

    private static BillingSignalMapping MapGroup(
        IReadOnlyList<(BillingFinding Finding, BillingRuleCandidate Rule, FindingDecision? Decision)> group, BillingSignalSubjects subjects, bool decided, bool blocked)
    {
        // Reason order: code (ordinal), then content, so the result does not depend on the order the findings arrived in.
        var members = group
            .Select(m => (m.Finding, m.Rule, Consequence: EffectiveConsequence(m.Finding, m.Rule), m.Decision))
            .OrderBy(m => m.Finding.ReasonCode, StringComparer.Ordinal)
            .ThenBy(m => ContentKey(m.Finding), StringComparer.Ordinal)
            .ToList();
        var consequence = members.Select(m => m.Consequence).MaxBy(Strength);
        // ontology-r1 W re-check F2: in a Flag tie a flag-only note sorts first by code (currency_not_usd < missing_field), and as the
        // primary it would title and rank a real Flag breach as a note. A real breach at the winning consequence takes precedence.
        // ontology-r1 D-125 review (a): among flag-only notes the D-91 invoice-number mismatch titles the Signal. It sorts after the coding
        // notes by code, and a non-primary reason survives only as an evidence line that the read path strips.
        // 072 part W K1 (task 049 part D): when 073 rejected on a reason of this group, that reason is the primary one, not merely the
        // first reject-consequence reason in code order.
        var deciding = members.Where(m => m.Decision is { Rejects: true }).ToList();
        var primary = deciding.Count > 0
            ? deciding[0]
            : members.Where(m => m.Consequence == consequence)
                .OrderBy(m => BillingRuleCatalogue.IsFlagOnlyReason(m.Finding.ReasonCode) ? 1 : 0)
                .ThenBy(m => m.Finding.ReasonCode is BillingReasonCodes.SubmissionInvoiceNumberMismatch
                    or BillingReasonCodes.SubmissionInvoiceNumberMismatchNotShown ? 0 : 1)
                .First();
        var groupDecision = !decided ? BillingGroupDecision.NotDecided
            : deciding.Count > 0 ? BillingGroupDecision.Rejects
            : consequence == BillingConsequence.Reject ? BillingGroupDecision.Held
            : BillingGroupDecision.NotRejecting;
        var withheld = members.Any(m => m.Finding.DetailWithheld);
        // 049 Held presentation: the rule would reject (a deciding finding), but the outcome is Blocked, so the invoice is held for review.
        var held = blocked && groupDecision == BillingGroupDecision.Rejects;
        if (held)
        {
            groupDecision = BillingGroupDecision.Blocked;
        }

        var (entity, subjectId, displayName) = SubjectOf(primary.Finding.Subject, subjects);
        var (template, facts, source) = withheld
            ? (BillingRuleCatalogue.WithheldDuplicateTemplate, new Dictionary<string, object?>(), BillingMessageSource.WithheldDuplicate)
            : MessageOf(primary.Finding, primary.Rule);
        var rule = primary.Rule;

        // ontology-r1 re-check F2: a reason with its own sentence (ReasonTemplate) has its own title too. sprk_name is what the user
        // sees first, and the host rule's title ("Billed rate above the approved rate") would claim a test that never ran (D-123).
        // A flag-only primary reason is a note, not a breach: it ranks at the Flag level (Decide lane, Info severity), whatever the
        // host rule's lane and severity say. No Flag-level mapping existed before; these two are the lowest-ranking values.
        var ownHeadline = withheld ? null : BillingRuleCatalogue.ReasonHeadline(primary.Finding.ReasonCode);
        var flagOnly = !withheld && BillingRuleCatalogue.IsFlagOnlyReason(primary.Finding.ReasonCode);
        var headline = ownHeadline
            ?? (!string.IsNullOrWhiteSpace(rule.ShortHeadline) && rule.ShortHeadline.Trim().Length <= MaxHeadlineLength
                ? rule.ShortHeadline.Trim()
                : BillingRuleDraftTranslator.Wording(primary.Finding.RuleType).Headline);
        if (held)
        {
            headline = HeldHeadline;
        }

        var reasonCodes = members.Select(m => m.Finding.ReasonCode).ToList();
        var fingerprint = FingerprintOf(reasonCodes, consequence);
        var evidence = new List<SignalEvidenceRef> { new(FingerprintEvidenceKind, fingerprint, "Fact") };
        evidence.AddRange(members.Select(m => new SignalEvidenceRef(ReasonEvidenceKind, m.Finding.ReasonCode, "Fact")
        {
            Text = ReasonText(m.Finding, m.Rule, withheld),
            Source = "sprk_policyversion:" + m.Finding.PolicyVersionId.ToString("D").ToLowerInvariant(),
        }));
        if (groupDecision == BillingGroupDecision.Held)
        {
            // Not presented as a rejection: lane, severity and sentence stay the policy's and the primary reason's; the evidence says 073 held it.
            evidence.Add(new SignalEvidenceRef(DecisionEvidenceKind, HeldDecisionRef, "Fact") { Text = primary.Decision?.HeldBecause });
        }
        else if (held)
        {
            // The same line 073's own hold uses, with the submission's blocked code: a reader sees the invoice is held, and why it is not rejected.
            evidence.Add(new SignalEvidenceRef(DecisionEvidenceKind, HeldDecisionRef, "Fact") { Text = SubmissionReasonCodes.Blocked });
        }

        evidence.AddRange(members
            .SelectMany(m => m.Finding.ValuesRead)
            // 049 F1-3: a value read that is not citable (IsCitable) keeps its evidence line (field, confidence) without its text.
            .Select(v => ValueEvidence(v, withheld || v.Value is null || !BillingRuleCatalogue.IsCitable(v.Name, v.Value) ? null : v.Value))
            .DistinctBy(e => (e.Kind, e.Ref, e.Text)));

        SignalWriteRequest RequestOf(string messageTemplate, IReadOnlyDictionary<string, object?> factValues) => new(
            rule.PolicyId,
            rule.PolicyCode,
            rule.PolicyVersionId,
            entity,
            subjectId,
            displayName,
            headline,
            flagOnly ? FlagOnlyLane : rule.Lane ?? SignalWriter.LaneDecide,
            flagOnly ? FlagOnlySeverity : rule.Severity,
            messageTemplate,
            factValues,
            evidence);

        var request = RequestOf(template, facts);
        if (held)
        {
            // The deciding finding's own rendered reason (the text its evidence line carries; never a raw code) follows the held wording as
            // a fact. The writer's own checks (SignalWriter.PreflightRefusal, the same ones the persisted plan is held to) decide whether
            // that sentence fits; if not, the reason is left out whole (the no-value wording), never cut. The reason stays in its evidence line.
            var withReason = RequestOf(HeldSentenceTemplate, new Dictionary<string, object?>(facts) { [HeldReasonFact] = ReasonText(primary.Finding, primary.Rule, withheld) });
            request = SignalWriter.PreflightRefusal(withReason) is null ? withReason : RequestOf(HeldSentenceNoValue, facts);
            source = BillingMessageSource.HeldForReview;
        }

        return new BillingSignalMapping(request, source)
        {
            Findings = members.Select(m => m.Finding).ToList(),
            ReasonCodes = reasonCodes,
            Consequence = consequence,
            Fingerprint = fingerprint,
            Decision = groupDecision,
        };
    }

    /// <summary>A flag-only reason is a Flag whatever its version says; a missing or unknown consequence is a Flag (task 073 fails
    /// closed the same way).</summary>
    private static BillingConsequence EffectiveConsequence(BillingFinding finding, BillingRuleCandidate rule) =>
        !BillingRuleCatalogue.IsFlagOnlyReason(finding.ReasonCode)
        && rule.Consequence is { } value && Enum.IsDefined(typeof(BillingConsequence), value)
            ? (BillingConsequence)value
            : BillingConsequence.Flag;

    private static int Strength(BillingConsequence consequence) => consequence switch
    {
        BillingConsequence.Reject => 3,
        BillingConsequence.SuggestAdjustment => 2,
        _ => 1,
    };

    /// <summary>A stable text of a finding's content, to order findings that share a reason code.</summary>
    private static string ContentKey(BillingFinding finding) =>
        string.Join('\u001F', finding.FactValues.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value))
        + '\u001E'
        + string.Join('\u001F', finding.ValuesRead.Select(v => v.Name + "=" + v.ExtractedField + "=" + v.Value))
        + '\u001E' + finding.PolicyVersionId.ToString("D") + (finding.DetailWithheld ? "|withheld" : string.Empty);

    /// <summary>One reason's own sentence. In a group with withheld detail, only token-free text.</summary>
    private static string ReasonText(BillingFinding finding, BillingRuleCandidate rule, bool withheldGroup)
    {
        if (withheldGroup)
        {
            return finding.DetailWithheld
                ? BillingRuleCatalogue.WithheldDuplicateTemplate
                : BillingRuleCatalogue.ReasonTemplate(finding.ReasonCode) ?? BillingRuleDraftTranslator.Wording(finding.RuleType).Headline + ".";
        }

        var (template, facts, _) = MessageOf(finding, rule);
        return SignalWriter.RenderSentence(template, facts);
    }

    private static SignalEvidenceRef ValueEvidence(BillingValueRead value, string? text) =>
        value.ExtractedField is { } field
            ? new SignalEvidenceRef(
                FieldEvidenceKind,
                field.LineIndex is { } line ? $"line[{line.ToString(System.Globalization.CultureInfo.InvariantCulture)}].{field.Field}" : $"invoice.{field.Field}",
                "Observation",
                value.Confidence is { } confidence ? (double)confidence : null)
            {
                Text = text,
                Source = value.Name,
            }
            : new SignalEvidenceRef(ReferenceEvidenceKind, value.Name, "Fact") { Text = text };

    private static (string Entity, Guid Id, string? DisplayName) SubjectOf(BillingSubject subject, BillingSignalSubjects subjects)
    {
        if (subject.Kind == BillingSubjectKind.Invoice)
        {
            return (InvoiceEntity, subjects.InvoiceId, subjects.InvoiceDisplayName);
        }

        if (subject.LineIndex is not { } index || !subjects.LineIds.TryGetValue(index, out var lineId) || lineId == Guid.Empty)
        {
            throw new ArgumentException($"No line row was given for line index {subject.LineIndex}.", nameof(subjects));
        }

        return (LineEntity, lineId, subjects.LineDisplayNames?.GetValueOrDefault(index));
    }

    private static (string Template, IReadOnlyDictionary<string, object?> Facts, BillingMessageSource Source) MessageOf(
        BillingFinding finding, BillingRuleCandidate rule)
    {
        if (finding.DetailWithheld)
        {
            // D-112, 071 fix round 2: only the withheld template, and no fact at all.
            return (BillingRuleCatalogue.WithheldDuplicateTemplate, new Dictionary<string, object?>(), BillingMessageSource.WithheldDuplicate);
        }

        var allowed = BillingRuleCatalogue.TemplateFactFields(finding.RuleType);
        // 049 F1-3: a fact whose value is firm-supplied text is cited only in its shape (BillingRuleCatalogue.IsCitable); otherwise it is left
        // out, never truncated, and a wording below that does not name it is used. A reason's own message is built by BillingSystemFindings,
        // which picks the variant that names no such value, so this never removes a token an own message names.
        var facts = finding.FactValues
            .Where(kv => allowed.Contains(kv.Key) && kv.Value is not null && BillingRuleCatalogue.IsCitable(kv.Key, kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal);

        if (BillingRuleCatalogue.ReasonTemplate(finding.ReasonCode) is { } own)
        {
            return (own, facts, BillingMessageSource.ReasonTemplate);
        }

        if (Renders(rule.MessageTemplate, facts))
        {
            return (rule.MessageTemplate!, facts, BillingMessageSource.PolicyVersion);
        }

        if (finding.RuleType == RuleType.Duplicate && finding.Subject.Kind == BillingSubjectKind.Invoice
            && Renders(InvoiceDuplicateTemplate, facts))
        {
            return (InvoiceDuplicateTemplate, facts, BillingMessageSource.InvoiceLevelDuplicate);
        }

        var wording = BillingRuleDraftTranslator.Wording(finding.RuleType);
        if (Renders(wording.Template, facts))
        {
            return (wording.Template, facts, BillingMessageSource.DefaultWording);
        }

        return (wording.Headline + ".", facts, BillingMessageSource.HeadlineOnly);
    }

    /// <summary>The writer's own rule (<see cref="SignalWriter.RenderSentence"/>): well-formed, and every token has a value.</summary>
    private static bool Renders(string? template, IReadOnlyDictionary<string, object?> facts) =>
        !string.IsNullOrWhiteSpace(template)
        && !SignalWriter.HasMalformedPlaceholder(template)
        && SignalWriter.ExtractTemplateTokens(template).All(facts.ContainsKey);
}
