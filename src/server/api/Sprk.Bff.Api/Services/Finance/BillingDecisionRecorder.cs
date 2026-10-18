using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Who decided a billing decision: a person (who confirmed it) or the system (an automatic decision, D-101).</summary>
public sealed record BillingDecider
{
    private BillingDecider(Guid? systemUserId, string role, Guid? contactId)
    {
        SystemUserId = systemUserId;
        Role = role;
        ContactId = contactId;
    }

    /// <summary>The confirming user (<c>systemuserid</c>); null for a system decision.</summary>
    public Guid? SystemUserId { get; }

    /// <summary>The decider's role, recorded in the fact snapshot; <c>automated: &lt;what&gt;</c> for a system decision.</summary>
    public string Role { get; }

    /// <summary>The person's linked internal contact (D-129), when they have one.</summary>
    public Guid? ContactId { get; }

    /// <summary>A person confirmed the decision.</summary>
    public static BillingDecider Human(Guid systemUserId, string role, Guid? contactId = null) => new(systemUserId, role, contactId);

    /// <summary>The system decided with no person (049's automatic rejection): no confirmer, decider <c>automated: {what}</c>.</summary>
    public static BillingDecider Automated(string what) => new(null, $"automated: {what}", null);
}

/// <summary>
/// ONE billing decision for <see cref="BillingDecisionRecorder"/> (ebilling-r1 task 074; FR-30, FR-19, FR-35 to FR-37; D-75, D-101).
/// </summary>
/// <param name="DecisionId">The caller's decision id. A retry with the same id writes nothing new; callers that need exactly-once across
/// callers derive it deterministically (103: from the approval portion).</param>
/// <param name="ActionCode">A billing code (<c>DecisionActionCatalog.BillingActions</c>).</param>
/// <param name="SubjectId">The line for a line code, the invoice for every other code (an override and an automatic rejection included,
/// D-101). The table follows the code, so a caller names only the id.</param>
/// <param name="Decider">Who decided.</param>
/// <param name="Facts">What the data said at decision time (billed and approved amounts, totals, findings). Mandatory.</param>
/// <param name="Outcome">What the caller did (<c>Reduced</c>, <c>Approved</c>, <c>Rejected</c>).</param>
/// <param name="Reason">Required for reduce, disallow, reject, return and override.</param>
/// <param name="Values">The action's other parameter values (<c>approvedAmount</c>, <c>note</c>).</param>
/// <param name="PolicyVersionId">The primary resolved finding's policy version; null when it resolves none.</param>
/// <param name="ResolvedSignalIds">The findings it resolves. The caller closes and links them with the returned record id.</param>
/// <param name="Written">Rows the decision wrote.</param>
/// <param name="PrivilegeFlagged">Copied forward (ADR-015).</param>
/// <param name="DecidedOn">When decided; now when null.</param>
public sealed record BillingDecision(
    Guid DecisionId,
    string ActionCode,
    Guid SubjectId,
    BillingDecider Decider,
    IReadOnlyDictionary<string, object?> Facts,
    string Outcome,
    string? Reason = null,
    IReadOnlyDictionary<string, object?>? Values = null,
    Guid? PolicyVersionId = null,
    IReadOnlyList<Guid>? ResolvedSignalIds = null,
    IReadOnlyList<DecisionRecordRef>? Written = null,
    bool PrivilegeFlagged = false,
    DateTimeOffset? DecidedOn = null);

/// <summary>The recorded decision: the Decision Record the caller links its findings to.</summary>
/// <param name="RecordId">The <c>sprk_decisionrecord</c> id (the existing one when <paramref name="AlreadyRecorded"/>).</param>
/// <param name="AlreadyRecorded">True when this decision id was recorded before and nothing was written.</param>
/// <param name="RecordClass">The record's class, from the catalog.</param>
public sealed record BillingDecisionRecordResult(Guid RecordId, bool AlreadyRecorded, RecordedDecisionClass RecordClass);

/// <summary>
/// ebilling-r1 task 074 (spec FR-30; D-75, D-100, D-101): the ONE Finance-side owner of a billing decision's Decision Record. The line
/// decisions (100), approve (103), reject and return (104), override (105) and the automatic rejection (049) call it, so each builds the same
/// request and none can pair a code with the wrong subject table or a system decision with a confirmer.
/// </summary>
/// <remarks>
/// <para><b>What it does.</b> It maps a <see cref="BillingDecision"/> to the writer's additive billing entry
/// (<see cref="DecisionRecordWriter.WriteBillingDecisionAsync"/>, D-100): the subject table from the code (line codes file under the line,
/// every other code under the invoice), the confirmer and decider role from <see cref="BillingDecider"/>. Every rule (billing codes only, a
/// required reason, a null confirmer only for an <c>automated:</c> decider, exactly once per decision id, the 026 filing checks) is the
/// writer's; the recorder copies none of them. It does not close or link findings: each caller does that with the returned record id.</para>
/// <para><b>Refuse before your own writes.</b> A caller that changes the invoice before recording calls <see cref="Preflight"/> first: it
/// gives every refusal the writer can give without I/O (a missing reason among them), so nothing changes when the decision would be refused.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="DecisionRecordWriter"/> writes the record; nothing in
/// Finance records a decision (no <c>DecisionRecordWriter</c> use under <c>Services/Finance</c> before this task). Extension: the writer is
/// ontology-owned and must stay subject-agnostic about billing callers; the code-to-table mapping and the decider shape are the eBilling
/// callers' contract, so they live here. Cost of doing nothing: five callers would each build the request, and one that passed a confirmer
/// for a system decision, or a line code with the invoice id, would be refused at run time after the invoice had already changed.</para>
/// </remarks>
public sealed class BillingDecisionRecorder
{
    private readonly DecisionRecordWriter _writer;

    public BillingDecisionRecorder(DecisionRecordWriter writer) =>
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    /// <summary>Writes the decision's one Decision Record, or finds the one already written for its decision id.</summary>
    /// <exception cref="DecisionRecordRefusedException">The writer refused (nothing written; after a create timeout the row may exist).</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    public async Task<BillingDecisionRecordResult> RecordAsync(BillingDecision decision, CancellationToken ct = default)
    {
        var result = await _writer.WriteBillingDecisionAsync(ToRequest(decision), ct).ConfigureAwait(false);
        return new BillingDecisionRecordResult(result.RecordId, result.AlreadyRecorded, result.RecordClass);
    }

    /// <summary>Every refusal <see cref="RecordAsync"/> can give without I/O. Reads and writes nothing.</summary>
    /// <exception cref="DecisionRecordRefusedException">The decision as given would be refused.</exception>
    public void Preflight(BillingDecision decision) => _writer.PreflightBillingDecision(ToRequest(decision));

    private static BillingDecisionRecordRequest ToRequest(BillingDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(decision.Decider);

        // An unknown code has no table; the writer refuses the code before it looks at the subject.
        var table = DecisionRecordWriter.BillingSubjectEntityOf(decision.ActionCode) ?? string.Empty;
        return new BillingDecisionRecordRequest(
            decision.DecisionId,
            decision.ActionCode,
            new DecisionRecordRef(table, decision.SubjectId),
            decision.Decider.SystemUserId,
            decision.Decider.Role,
            decision.Values,
            decision.Outcome,
            decision.Facts,
            decision.Reason,
            decision.PolicyVersionId,
            decision.ResolvedSignalIds,
            decision.Written,
            decision.Decider.ContactId,
            decision.PrivilegeFlagged,
            decision.DecidedOn);
    }
}
