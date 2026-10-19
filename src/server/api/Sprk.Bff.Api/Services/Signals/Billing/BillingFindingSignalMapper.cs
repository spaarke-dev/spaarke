using Sprk.Bff.Api.Services.Finance;

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
/// subject (an ExpenseRule code and cap on one line, RequiredData with several missing fields) was silently dropped. <see cref="MapAll(IReadOnlyList{BillingFinding}, IReadOnlyDictionary{Guid, BillingRuleCandidate}, BillingSignalSubjects)"/>
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

    internal const string InvoiceEntity = "sprk_invoice";
    internal const string LineEntity = "sprk_billingevent";
    private const int MaxHeadlineLength = 200;

    /// <summary>The lane of a Signal whose primary reason is flag-only: a reviewer's item, never a Do (action) item.</summary>
    internal const int FlagOnlyLane = SignalWriter.LaneDecide;

    /// <summary>The severity of a Signal whose primary reason is flag-only: the lowest, so a note never ranks like a breach.</summary>
    internal const int FlagOnlySeverity = SignalWriter.SeverityInfo;

    /// <summary>The Signal writes of one evaluation: <see cref="MapAll(IReadOnlyList{BillingFinding}, IReadOnlyDictionary{Guid, BillingRuleCandidate}, BillingSignalSubjects)"/>
    /// over its findings and rule versions.</summary>
    public static IReadOnlyList<BillingSignalMapping> MapAll(BillingEvaluationResult evaluation, BillingSignalSubjects subjects)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        return MapAll(evaluation.Findings, evaluation.RulesByVersion, subjects);
    }

    /// <summary>
    /// Maps every finding onto the rows in <paramref name="subjects"/>, one request per (policy code, subject), in the order each pair
    /// first appears among <paramref name="findings"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A finding's version is not in <paramref name="rulesByVersion"/>, or its line has no row in
    /// <paramref name="subjects"/>.</exception>
    public static IReadOnlyList<BillingSignalMapping> MapAll(
        IReadOnlyList<BillingFinding> findings, IReadOnlyDictionary<Guid, BillingRuleCandidate> rulesByVersion, BillingSignalSubjects subjects)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(rulesByVersion);
        ArgumentNullException.ThrowIfNull(subjects);

        var groups = new List<List<(BillingFinding Finding, BillingRuleCandidate Rule)>>();
        var byKey = new Dictionary<string, List<(BillingFinding, BillingRuleCandidate)>>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
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

            group.Add((finding, rule));
        }

        return groups.Select(g => MapGroup(g, subjects)).ToList();
    }

    /// <summary>One finding alone (a group of one). Internal: 049 maps an evaluation with <see cref="MapAll(BillingEvaluationResult, BillingSignalSubjects)"/>,
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

        return MapGroup([(finding, rule)], subjects);
    }

    /// <summary>The reason-set fingerprint: the sorted reason codes and the strongest consequence.</summary>
    internal static string FingerprintOf(IEnumerable<string> sortedReasonCodes, BillingConsequence consequence) =>
        string.Join(",", sortedReasonCodes) + "|" + consequence;

    private static BillingSignalMapping MapGroup(IReadOnlyList<(BillingFinding Finding, BillingRuleCandidate Rule)> group, BillingSignalSubjects subjects)
    {
        // Reason order: code (ordinal), then content, so the result does not depend on the order the findings arrived in.
        var members = group
            .Select(m => (m.Finding, m.Rule, Consequence: EffectiveConsequence(m.Finding, m.Rule)))
            .OrderBy(m => m.Finding.ReasonCode, StringComparer.Ordinal)
            .ThenBy(m => ContentKey(m.Finding), StringComparer.Ordinal)
            .ToList();
        var consequence = members.Select(m => m.Consequence).MaxBy(Strength);
        var primary = members.First(m => m.Consequence == consequence);
        var withheld = members.Any(m => m.Finding.DetailWithheld);

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

        var reasonCodes = members.Select(m => m.Finding.ReasonCode).ToList();
        var fingerprint = FingerprintOf(reasonCodes, consequence);
        var evidence = new List<SignalEvidenceRef> { new(FingerprintEvidenceKind, fingerprint, "Fact") };
        evidence.AddRange(members.Select(m => new SignalEvidenceRef(ReasonEvidenceKind, m.Finding.ReasonCode, "Fact")
        {
            Text = ReasonText(m.Finding, m.Rule, withheld),
            Source = "sprk_policyversion:" + m.Finding.PolicyVersionId.ToString("D").ToLowerInvariant(),
        }));
        evidence.AddRange(members
            .SelectMany(m => m.Finding.ValuesRead)
            .Select(v => ValueEvidence(v, withheld || v.Value is null ? null : v.Value))
            .DistinctBy(e => (e.Kind, e.Ref, e.Text)));

        var request = new SignalWriteRequest(
            rule.PolicyId,
            rule.PolicyCode,
            rule.PolicyVersionId,
            entity,
            subjectId,
            displayName,
            headline,
            flagOnly ? FlagOnlyLane : rule.Lane ?? SignalWriter.LaneDecide,
            flagOnly ? FlagOnlySeverity : rule.Severity,
            template,
            facts,
            evidence);
        return new BillingSignalMapping(request, source)
        {
            Findings = members.Select(m => m.Finding).ToList(),
            ReasonCodes = reasonCodes,
            Consequence = consequence,
            Fingerprint = fingerprint,
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
        var facts = finding.FactValues
            .Where(kv => allowed.Contains(kv.Key) && kv.Value is not null)
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
