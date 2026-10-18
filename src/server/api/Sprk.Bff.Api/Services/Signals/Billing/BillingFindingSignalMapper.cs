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

/// <summary>One finding as a Signal write, and where its message came from.</summary>
public sealed record BillingSignalMapping(SignalWriteRequest Request, BillingMessageSource MessageSource);

/// <summary>
/// Maps a billing finding to the <see cref="SignalWriteRequest"/> task 049 hands <see cref="SignalWriter"/> (ebilling-r1 task 072, FR-25).
/// Pure: no I/O. The request always passes the writer's template and fact checks (every <c>{{token}}</c> of the message has a value),
/// so a finding is never lost to a template that names a value this finding does not have.
/// </summary>
/// <remarks>
/// <para><b>Message.</b> The first of these whose tokens all have a value: a withheld Duplicate (D-112, 071 fix round 2) always takes
/// <see cref="BillingRuleCatalogue.WithheldDuplicateTemplate"/> with no fact values at all, so no withheld value reaches the Signal;
/// otherwise the policy version's template, then (an invoice-level Duplicate only) <see cref="InvoiceDuplicateTemplate"/>, then the
/// type's default wording, then the default headline as a sentence. 071's findings leave out a token with no value for the subject
/// (a line token on an invoice-level finding, a timekeeper name that was not read), which is why the chain exists.</para>
/// <para><b>Facts.</b> Only the keys <see cref="BillingRuleCatalogue.TemplateFactFields"/> allows for the type, as 071 built them.</para>
/// <para><b>Subject.</b> <c>sprk_invoice</c> for an invoice-level finding, <c>sprk_billingevent</c> for a line (both verified Signal
/// subjects, task 026). <b>Lane</b> is the policy's, Decide when it has none; <b>severity</b> the policy's; the <b>headline</b> the
/// version's, else the type's default.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <c>SignalEvaluationService</c> builds Ontology requests from a
/// predicate hit, not a billing finding. Extension: no; a billing finding's subject is a line index and its message needs the D-112
/// withheld variant. Cost of doing nothing: 049 has no tested way to turn a finding into a write the writer accepts.</para>
/// </remarks>
public static class BillingFindingSignalMapper
{
    /// <summary>The Duplicate message for an invoice-level match: names only invoice tokens.</summary>
    public const string InvoiceDuplicateTemplate = "Invoice {{invoice.firmInvoiceNumber}} duplicates {{duplicateOf.trackingNumber}}.";

    internal const string InvoiceEntity = "sprk_invoice";
    internal const string LineEntity = "sprk_billingevent";
    private const int MaxHeadlineLength = 200;

    /// <summary>Maps <paramref name="finding"/>, produced by <paramref name="rule"/>, onto the rows in <paramref name="subjects"/>.</summary>
    /// <exception cref="ArgumentException">The finding is not <paramref name="rule"/>'s, or its line has no row in <paramref name="subjects"/>.</exception>
    public static BillingSignalMapping Map(BillingFinding finding, BillingRuleCandidate rule, BillingSignalSubjects subjects)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(subjects);
        if (finding.PolicyVersionId != rule.PolicyVersionId)
        {
            throw new ArgumentException("The finding was not produced by this rule version.", nameof(rule));
        }

        var (entity, subjectId, displayName) = SubjectOf(finding.Subject, subjects);
        var (template, facts, source) = MessageOf(finding, rule);
        var wording = BillingRuleDraftTranslator.Wording(finding.RuleType);
        var headline = !string.IsNullOrWhiteSpace(rule.ShortHeadline) && rule.ShortHeadline.Trim().Length <= MaxHeadlineLength
            ? rule.ShortHeadline.Trim()
            : wording.Headline;

        var request = new SignalWriteRequest(
            rule.PolicyId,
            rule.PolicyCode,
            rule.PolicyVersionId,
            entity,
            subjectId,
            displayName,
            headline,
            rule.Lane ?? SignalWriter.LaneDecide,
            rule.Severity,
            template,
            facts);
        return new BillingSignalMapping(request, source);
    }

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
