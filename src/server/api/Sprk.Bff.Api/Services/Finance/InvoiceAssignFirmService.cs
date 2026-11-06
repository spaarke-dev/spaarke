// ebilling-r1 task 095d-1b (notes/095c-from-email-firm-optional.md 16.4 to 16.7; owner 2026-11-04 option 1, "apply then record", "renumber
// everything"; ontology-r1 16.7; uac-r2 additions 2026-11-04).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — TrackingNumberAllocator.RenumberSubmissionAsync (095d-1a) moves a no-firm submission into a firm's scope in one transaction,
//       but decides nothing about the invoice; InvoiceApprovalService and SubmissionOverrideService decide an invoice but never set its firm;
//       InvoiceFirmHold reads the hold and nothing clears it (grep: no writer of sprk_firmhold but the persister's create).
//   (2) Extension — the allocator is the number columns' only writer (D-37) and must stay ignorant of invoices, lines and files; the approval
//       and override services each own another decision's order and recovery (ADR-002: one owner per invariant). Setting a firm is its own
//       decision with its own refusals, stored decision and repeat rule, so it is its own service that hands its apply to the allocator.
//   (3) Cost of doing nothing — an invoice submitted without a firm stays held for ever: approval refuses it (095c-4) and nothing can assign the
//       firm through the pipeline (095d acceptance criterion 1 fails).
//
// Placement (bff-extensions.md): BFF, Services/Finance (B1 caller identity, B4 live request). Scoped like its scoped dependencies. No package.

using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>The outcome class of a Set-firm call (task 095d-1b). The endpoint renders <see cref="AssignFirmResult.Status"/> and <see cref="AssignFirmResult.Code"/>.</summary>
public enum AssignFirmOutcome
{
    /// <summary>This call applied the firm: the one transaction committed (renumber, firm everywhere, hold cleared, decision stored).</summary>
    Assigned,

    /// <summary>The submission's stored decision already names this firm: this call applied nothing (a repeat, or the loser of a race for the
    /// same firm). It completed the Decision Record from the stored decision (095d-1c), or found it already written.</summary>
    AlreadyAssigned,

    /// <summary>Refused before anything was written; <see cref="AssignFirmResult.Status"/> is the HTTP status.</summary>
    Refused,

    /// <summary>095d-1c: the apply committed (now or by an earlier call) but the Decision Record was not written, or its write got no answer
    /// (503 <see cref="AssignFirmCodes.Incomplete"/> with <see cref="AssignFirmResult.FailedStep"/>). A repeat naming the same firm finishes it
    /// from the stored decision alone and never applies again.</summary>
    Incomplete,
}

/// <summary>Stable ProblemDetails codes of the Set-firm route (ADR-019). No code or detail names another firm.</summary>
public static class AssignFirmCodes
{
    /// <summary>400: the body names no firm.</summary>
    public const string FirmRequired = "ebilling.assign_firm.firm_required";

    /// <summary>400: the note is longer than <see cref="InvoiceAssignFirmService.NoteMaxLength"/>.</summary>
    public const string NoteTooLong = "ebilling.assign_firm.note_too_long";

    /// <summary>400: the firm does not exist or is not active (the intake's code and rule).</summary>
    public const string FirmInvalid = InvoiceSubmissionErrors.FirmInvalid;

    /// <summary>403: the firm is not one of the invoice's core record's assigned law firms (the intake's code and rule, D-106 / D-110).</summary>
    public const string FirmNotLinked = InvoiceSubmissionErrors.FirmNotLinked;

    /// <summary>503: the firm or its link could not be read; nothing was changed.</summary>
    public const string FirmUnverifiable = InvoiceSubmissionErrors.FirmUnverifiable;

    /// <summary>409: the invoice is not Active and In Review.</summary>
    public const string NotInReview = "ebilling.assign_firm.not_in_review";

    /// <summary>409: the invoice or its submission already names a firm (and no stored Set-firm decision explains it).</summary>
    public const string FirmAlreadySet = "ebilling.assign_firm.firm_already_set";

    /// <summary>409: the invoice is not held for a firm (<see cref="InvoiceFirmHold"/>, read app-only).</summary>
    public const string NotHeld = "ebilling.assign_firm.not_held";

    /// <summary>409: the invoice has no submission of its own (none, or more than one, whose id derives the invoice).</summary>
    public const string NoSubmission = "ebilling.assign_firm.no_submission";

    /// <summary>409: the submission's stored Set-firm decision names a different firm (a repeat naming another firm). Names no firm.</summary>
    public const string AssignedOtherFirm = "ebilling.assign_firm.assigned_other_firm";

    /// <summary>409: the invoice or its submission changed while the request was prepared; reload and try again.</summary>
    public const string Changed = "ebilling.assign_firm.changed";

    /// <summary>409: the submission cannot be renumbered into a firm's scope (the allocator's <see cref="TrackingNumberReasons.NotUnnumbered"/>).</summary>
    public const string NotRenumberable = "ebilling.assign_firm.not_renumberable";

    /// <summary>409: the apply would not fit one transaction (<see cref="TrackingNumberAllocator.FitsRenumberTransaction"/>) or the stored
    /// findings would exceed their column; nothing was changed and nothing is applied in part.</summary>
    public const string TooLarge = "ebilling.assign_firm.too_large";

    /// <summary>500: the stored findings of the submission are not a JSON object, so the decision cannot be stored with them.</summary>
    public const string FindingsUnreadable = "ebilling.assign_firm.findings_unreadable";

    /// <summary>500: the invoice names no single core record, or its derived filing is not that record.</summary>
    public const string CoreUnresolved = "ebilling.assign_firm.core_unresolved";

    /// <summary>503: a read, the lock or the apply failed with nothing changed, or the apply's outcome is unknown (repeat: the stored decision decides).</summary>
    public const string Unavailable = "ebilling.assign_firm.unavailable";

    /// <summary>503 (095d-1c): the firm was set but its Decision Record was not written (or got no answer); repeat the request to finish it.
    /// The body names the failed step only: no id and no exception text.</summary>
    public const string Incomplete = "ebilling.assign_firm.incomplete";

    public const string Busy = LineDecisionCodes.Busy;
    public const string NotAuthorized = LineDecisionCodes.NotAuthorized;
    public const string DecisionInvalid = LineDecisionCodes.DecisionInvalid;
    public const string DeciderUnresolved = LineDecisionCodes.DeciderUnresolved;
}

/// <summary>The result of <see cref="InvoiceAssignFirmService.SetFirmAsync"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Status">The HTTP status (200 for <see cref="AssignFirmOutcome.Assigned"/> and <see cref="AssignFirmOutcome.AlreadyAssigned"/>).</param>
/// <param name="Code">The stable code (<c>ok</c> on success).</param>
/// <param name="Detail">A sentence for the reviewer; never names another firm.</param>
/// <param name="SubmissionId">The invoice's submission, when known.</param>
/// <param name="DecisionId">The Set-firm decision id (derived from the submission).</param>
/// <param name="TrackingNumber">The number the submission and invoice now hold.</param>
/// <param name="PreviousTrackingNumber">The no-firm number it gave up.</param>
/// <param name="RulesRerun">Always <c>pending</c> after an apply in 095d-1 (the re-run is 095d-2).</param>
/// <param name="DecisionRecordId">095d-1c: the Decision Record written (or found) for the decision; set on success only. Not rendered by the route.</param>
/// <param name="FailedStep">095d-1c, <see cref="AssignFirmOutcome.Incomplete"/> only: the step that did not finish (<c>record</c> or <c>filing</c>).</param>
public sealed record AssignFirmResult(
    AssignFirmOutcome Outcome,
    int Status,
    string Code,
    string? Detail = null,
    Guid? SubmissionId = null,
    Guid? DecisionId = null,
    string? TrackingNumber = null,
    string? PreviousTrackingNumber = null,
    string? RulesRerun = null,
    Guid? DecisionRecordId = null,
    string? FailedStep = null)
{
    internal static AssignFirmResult Refuse(int status, string code, string detail, Guid? submissionId = null) =>
        new(AssignFirmOutcome.Refused, status, code, detail, submissionId);
}

/// <summary>One renamed row of the apply (the old-to-new mapping, ontology-r1 16.7 addition 3). It is kept ONLY in the stored <c>assignFirm</c> member of the findings; the Decision Record carries counts only (<see cref="InvoiceAssignFirmService.RenamedCounts"/>).</summary>
internal sealed record AssignFirmRename(string Entity, Guid Id, string Column, string From, string To);

/// <summary>
/// The Set-firm decision as stored with the apply, under <see cref="InvoiceAssignFirmService.StoredDecisionMember"/> of the submission's findings
/// (16.4 condition 2). It is written ONLY in the apply transaction, so its presence means the apply committed. 095d-1c rebuilds the Decision Record
/// from it alone (<see cref="InvoiceAssignFirmService.BuildDecision"/>), so a repeat gives the same id and hash.
/// </summary>
internal sealed record AssignFirmStoredDecision(
    Guid DecisionId,
    Guid FirmId,
    string? Note,
    string PreviousTrackingNumber,
    int? PreviousSequence,
    string TrackingNumber,
    int Sequence,
    Guid DeciderUserId,
    DateTimeOffset DecidedOn,
    InvoiceSubmissionAfterSteps.ResolvedSignalSet FirmMissingSignals,
    IReadOnlyList<AssignFirmRename> Renamed,
    int FirmSetLines,
    string RulesRerun)
{
    public JsonObject ToJson() => new()
    {
        ["schema"] = 1,
        ["decisionId"] = DecisionId.ToString("D"),
        ["firmId"] = FirmId.ToString("D"),
        ["note"] = Note,
        ["previousTrackingNumber"] = PreviousTrackingNumber,
        ["previousSequence"] = PreviousSequence,
        ["trackingNumber"] = TrackingNumber,
        ["sequence"] = Sequence,
        ["deciderUserId"] = DeciderUserId.ToString("D"),
        ["decidedOn"] = DecidedOn.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        ["firmMissingSignals"] = FirmMissingSignals.ToJson(),
        ["renamed"] = new JsonArray(Renamed.Select(r => (JsonNode)new JsonObject
        {
            ["entity"] = r.Entity,
            ["id"] = r.Id.ToString("D"),
            ["column"] = r.Column,
            ["from"] = r.From,
            ["to"] = r.To,
        }).ToArray()),
        ["firmSetLines"] = FirmSetLines,
        ["rulesRerun"] = RulesRerun,
    };

    /// <summary>The stored decision, or null when the member is absent. A member that is present but does not parse throws
    /// <see cref="FormatException"/> (the caller refuses: what was applied cannot be known).</summary>
    public static AssignFirmStoredDecision? Read(JsonObject root)
    {
        if (root[InvoiceAssignFirmService.StoredDecisionMember] is not { } member)
        {
            return null;
        }

        if (member is not JsonObject node
            || !Guid.TryParse(Text(node, "decisionId"), out var decisionId)
            || !Guid.TryParse(Text(node, "firmId"), out var firmId)
            || Text(node, "previousTrackingNumber") is not { Length: > 0 } previous
            || Text(node, "trackingNumber") is not { Length: > 0 } tracking
            || Int(node, "sequence") is not { } sequence
            || !Guid.TryParse(Text(node, "deciderUserId"), out var decider)
            || !DateTimeOffset.TryParse(Text(node, "decidedOn"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var decidedOn)
            || Int(node, "firmSetLines") is not { } firmSetLines
            || node["firmMissingSignals"] is not JsonObject signals)
        {
            throw new FormatException("The stored Set-firm decision cannot be read.");
        }

        var renamed = new List<AssignFirmRename>();
        foreach (var entry in node["renamed"] as JsonArray ?? [])
        {
            if (entry is not JsonObject r || Text(r, "entity") is not { } entity || !Guid.TryParse(Text(r, "id"), out var id)
                || Text(r, "column") is not { } column || Text(r, "from") is not { } from || Text(r, "to") is not { } to)
            {
                throw new FormatException("The stored Set-firm decision's rename mapping cannot be read.");
            }

            renamed.Add(new AssignFirmRename(entity, id, column, from, to));
        }

        return new AssignFirmStoredDecision(
            decisionId, firmId, Text(node, "note"), previous, Int(node, "previousSequence"), tracking, sequence, decider,
            decidedOn.ToUniversalTime(), InvoiceSubmissionAfterSteps.ResolvedSignalSet.Parse(signals), renamed, firmSetLines,
            Text(node, "rulesRerun") ?? InvoiceAssignFirmService.RulesRerunPending);
    }

    private static string? Text(JsonObject node, string name) =>
        node.TryGetPropertyValue(name, out var value) && value is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;

    private static int? Int(JsonObject node, string name) =>
        node.TryGetPropertyValue(name, out var value) && value is JsonValue v && v.TryGetValue<int>(out var number) ? number : null;
}

/// <summary>
/// Sets the firm on a held no-firm invoice (task 095d-1b): an internal reviewer with Write on the invoice names a firm linked to its core record;
/// every refusal is given before anything is written; then ONE transaction, built here and sent by
/// <see cref="ITrackingNumberAllocator.RenumberSubmissionAsync"/>, renumbers the submission into the firm's scope and applies the firm everywhere.
/// </summary>
/// <remarks>
/// <para><b>Authorization.</b> The route has proven, as the caller, Write on the invoice (<c>ebilling.invoice.assign_firm</c>, uniform 404) and
/// Read and AppendTo on the firm (<c>read</c>, <c>finance.attach_invoice</c>, 403), and runs step 0 and the kill switch. The decider is the
/// caller's <c>systemuserid</c> from the authenticated principal; the invoice, the submission, the core record, the number and the Signal set
/// are all derived server-side. The service asks the rest as for approval: AppendTo on the core record and Write on the firm_missing Signal's
/// subject (<see cref="HumanDecisionSignalClose.AuthorizeAsync"/>), and the recorder's filing (<see cref="CoreAncestorResolver.ResolveStampsAsync"/>).</para>
/// <para><b>Refusals, all before the apply (16.4 condition 1), in this order.</b> The two locks (the invoice-decision lock and the line lock:
/// the apply renames the lines); the invoice (gone: 404) and its ONE submission (the row whose id derives the invoice); then, unless a stored
/// decision makes this a repeat: Active and In Review (409); no firm on the invoice or the submission (409); held for a firm, app-only
/// (<see cref="InvoiceFirmHold.IsHeldAsync"/>, 409); one core record (500); the Signal-close rights (403) and the
/// filing (503 or 500), BEFORE any read of the firm, so a caller without the core record's rights is refused the same whichever firm it names
/// (095d-1b fix round 2, K1); then the firm active (400) and linked (<see cref="InvoiceSubmissionService.CheckFirmLinkAsync"/>: 403 not linked,
/// 503 unreadable); phase P2 (503); the stored findings readable (500); the transaction ceiling and the findings size (409); the Decision Record
/// writer's <see cref="BillingDecisionRecorder.Preflight"/> (500). A refusal writes nothing.</para>
/// <para><b>The apply (16.4 condition 2; 16.7).</b> One transaction, conditioned on the submission's and the invoice's row versions: the
/// submission's firm, scope, sequence, number and previous sequence (the allocator's), its findings with the new <c>trackingNumber</c> and the
/// stored decision (<see cref="StoredDecisionMember"/>: the firm, note, previous and new number, the firm_missing Signal set and its read time,
/// the decider, the old-to-new name mapping and <c>rulesRerun</c> "pending"); the invoice's firm, number and <c>sprk_firmhold</c> cleared;
/// each line's firm and name; the approval's name; each stored file's name (<c>sprk_storedfile.sprk_name</c> only). The builder runs once per
/// attempt with that attempt's number, so every name matches the number written.</para>
/// <para><b>Repeats (1a review K3).</b> The stored decision is written only by the apply, so a submission that carries one was applied. A request
/// that finds one is a repeat: one that names a different firm is 409 <see cref="AssignFirmCodes.AssignedOtherFirm"/>; one that names the same
/// firm asks the allocator at the current version, whose <see cref="RenumberOutcome.AlreadyRenumbered"/> must name the stored number, and answers
/// <see cref="AssignFirmOutcome.AlreadyAssigned"/>. A repeat's builder throws, so a repeat can never apply twice. A legacy row that held no
/// sequence in the none scope is renumbered without a previous sequence, so its repeat is <see cref="TrackingNumberReasons.NotUnnumbered"/>
/// (16.6.1); the stored decision decides it (same firm, same number: already assigned). A row-version conflict re-reads and goes the same way.</para>
/// <para><b>The Decision Record (095d-1c; owner option 1, apply then record).</b> After the apply commits, ONE record through the existing
/// billing path (<see cref="BillingDecisionRecorder.RecordAsync"/>), built by <see cref="BuildDecision"/> from the stored decision alone, so the
/// first call and every repeat send the same id and content. A refusal, a fault or no answer from the writer is 503
/// <see cref="AssignFirmCodes.Incomplete"/> with <c>failedStep</c> <c>record</c> (never 200, never "nothing changed"). A repeat naming the same
/// firm finishes it: the core-record rights are asked again as THIS caller before the repeat is judged, the filing before the record (a filing
/// that cannot be confirmed is <c>failedStep</c> <c>filing</c>), and the record names the stored decider. The repeat never applies twice (its
/// builder throws). Rows applied with no record yet are listed by <c>Set-EbillingInvoiceFirmHoldColumn.ps1 -Verify</c> (INFO inside its window,
/// FAIL after it).</para>
/// <para><b>Not in 095d-1.</b> No Signal is closed: the firm_missing Signal is shared (ontology-r1 16.7), so <c>ResolvedSignalIds</c> is
/// empty, its set is in the facts, and 095d-2 closes it as ConditionCleared after the re-run. <c>rulesRerun</c> stays "pending" (095d-2).
/// No AI call.</para>
/// </remarks>
public sealed class InvoiceAssignFirmService
{
    internal const string AssignFirmActionCode = "assign-firm";

    /// <summary>The decider role recorded with the decision: the reviewer who set the firm.</summary>
    public const string DeciderRole = InvoiceLineDecisionService.DeciderRole;

    /// <summary>The findings member that stores the decision with the apply.</summary>
    internal const string StoredDecisionMember = "assignFirm";

    /// <summary>The findings member that names the submission's tracking number (the persister's).</summary>
    internal const string FindingsTrackingNumberMember = "trackingNumber";

    internal const string RulesRerunPending = "pending";

    /// <summary>The stored <c>rulesRerun</c> once 095d-2's re-run for the assigned firm has finished (EB-D-150). 095d-2 writes it; <see cref="InvoiceFirmGate"/> reads it.</summary>
    internal const string RulesRerunCompleted = "completed";

    /// <summary>The longest note kept (the line decision's reason length).</summary>
    public const int NoteMaxLength = InvoiceLineDecisionService.ReasonMaxLength;

    /// <summary>The approval's name prefix (<see cref="InvoiceSubmissionPersister"/>'s BuildApproval writes <c>Approval {tracking number}</c>).</summary>
    internal const string ApprovalNamePrefix = "Approval ";

    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);

    private static readonly string[] InvoiceColumns =
    [
        "statecode", "statuscode", "versionnumber", InvoiceSubmissionPersister.InvoiceVendorOrg, InvoiceReviewService.InvTrackingNumber,
        .. EligibleApproverResolver.CoreLookupColumns(InvoiceReviewReadService.InvoiceEntity),
    ];

    /// <summary>
    /// The submission columns every phase has. <see cref="TrackingNumberAllocator.ScopeColumn"/> is NOT here: at P0 it does not exist and a query
    /// that names it faults whole (every Set firm answered 503 unavailable before it reached its own scope_key_inactive refusal; uac-r2 pre-P2
    /// safety). <see cref="SubmissionColumnsFor"/> adds it once the allocator says the column exists.
    /// </summary>
    private static readonly string[] SubmissionColumns =
    [
        "versionnumber", InvoiceSubmissionPersister.SubmissionInvoice, TrackingNumberAllocator.FirmColumn,
        TrackingNumberAllocator.SequenceColumn, TrackingNumberAllocator.TrackingNumberColumn, TrackingNumberAllocator.FirmInvoiceNumberColumn,
        InvoiceSubmissionPersister.SubmissionFindingsJson,
        // 082c-2 fix round 3 (ontology F1): the stored outcome reason, which the apply leaves as it is and approval's firm hold still reads.
        InvoiceSubmissionPersister.SubmissionReasonCode,
    ];

    /// <summary>The submission read's columns: <see cref="SubmissionColumns"/>, and the tracking scope only where it exists (P1 and later).</summary>
    private static string[] SubmissionColumnsFor(bool scopeColumnExists) =>
        scopeColumnExists ? [.. SubmissionColumns, TrackingNumberAllocator.ScopeColumn] : SubmissionColumns;

    private readonly IGenericEntityService _entities;
    private readonly ITrackingNumberAllocator _allocator;
    private readonly IIdempotencyService _locks;
    private readonly Action<BillingDecision> _preflight;
    private readonly Func<BillingDecision, CancellationToken, Task<BillingDecisionRecordResult>> _record;
    private readonly Func<Guid, CancellationToken, Task<bool?>> _isActiveFirm;
    private readonly Func<InvoiceCoreRecord, Guid, CancellationToken, Task<InvoiceFirmLink>> _firmLink;
    private readonly Func<DecisionCoreRecord, Guid, string?, CancellationToken, Task<DecisionCallerRefusal>> _closeRights;
    private readonly Func<Guid, CancellationToken, Task<CoreAncestorResult>> _filing;
    private readonly ILogger<InvoiceAssignFirmService> _logger;

    /// <summary>082c-2: whether <see cref="InvoiceReviewDetail.Column"/> exists (the process-wide probe; a test sets its own).</summary>
    internal ReviewDetailColumnProbe ReviewDetailProbe { get; init; } = ReviewDetailColumnProbe.Shared;
    private readonly TimeProvider _time;

    public InvoiceAssignFirmService(
        IGenericEntityService entities,
        ITrackingNumberAllocator allocator,
        IIdempotencyService locks,
        BillingDecisionRecorder recorder,
        InvoiceSubmissionService submissions,
        CoreAncestorResolver coreAncestors,
        CallerRecordAccessProbe probe,
        ILogger<InvoiceAssignFirmService> logger)
        : this(
            entities,
            allocator,
            locks,
            (recorder ?? throw new ArgumentNullException(nameof(recorder))).Preflight,
            recorder.RecordAsync,
            (submissions ?? throw new ArgumentNullException(nameof(submissions))).IsActiveFirmForFromEmailAsync,
            submissions.CheckFirmLinkAsync,
            CloseRightsCheck(entities, probe ?? throw new ArgumentNullException(nameof(probe)), logger),
            (invoiceId, ct) => (coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors)))
                .ResolveStampsAsync(InvoiceReviewReadService.InvoiceEntity, invoiceId, ct, CoreAncestorIntent.ReadOnly),
            logger,
            null)
    {
    }

    /// <summary>The seam the unit tests use: every check that is not a Dataverse read of this service's own is passed as a call (103's pattern).</summary>
    internal InvoiceAssignFirmService(
        IGenericEntityService entities,
        ITrackingNumberAllocator allocator,
        IIdempotencyService locks,
        Action<BillingDecision> preflight,
        Func<BillingDecision, CancellationToken, Task<BillingDecisionRecordResult>> record,
        Func<Guid, CancellationToken, Task<bool?>> isActiveFirm,
        Func<InvoiceCoreRecord, Guid, CancellationToken, Task<InvoiceFirmLink>> firmLink,
        Func<DecisionCoreRecord, Guid, string?, CancellationToken, Task<DecisionCallerRefusal>> closeRights,
        Func<Guid, CancellationToken, Task<CoreAncestorResult>> filing,
        ILogger<InvoiceAssignFirmService> logger,
        TimeProvider? time)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _isActiveFirm = isActiveFirm ?? throw new ArgumentNullException(nameof(isActiveFirm));
        _firmLink = firmLink ?? throw new ArgumentNullException(nameof(firmLink));
        _closeRights = closeRights ?? throw new ArgumentNullException(nameof(closeRights));
        _filing = filing ?? throw new ArgumentNullException(nameof(filing));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>FR-51 as for approval: AppendTo on the core record and Write on the firm_missing Signal's subject (the invoice), asked as the caller.</summary>
    internal static Func<DecisionCoreRecord, Guid, string?, CancellationToken, Task<DecisionCallerRefusal>> CloseRightsCheck(
        IGenericEntityService entities, CallerRecordAccessProbe probe, ILogger logger) =>
        (core, invoiceId, token, ct) => HumanDecisionSignalClose.AuthorizeAsync(
            entities, probe, token, core, invoiceId, [(InvoiceReviewReadService.InvoiceEntity, invoiceId)], logger, ct);

    /// <summary>The Set-firm decision id: derived from the submission, so every attempt by every reviewer is one decision (D-201).</summary>
    public static Guid DecisionIdFor(Guid submissionId) => InvoiceSubmissionPersister.IdOf($"ebilling-assign-firm-{submissionId:D}");

    /// <summary>
    /// The Decision Record's decision, rebuilt ONLY from the stored decision (16.4 condition 2), so a repeat gives the same id and hash. It is
    /// preflighted before the apply and recorded after it (095d-1c). Facts are not hashed; the values (<c>firm</c>, <c>note</c>) are. No Signal
    /// is resolved (16.7): the firm_missing Signal set, the old and new number and the bounded rename counts are in the facts.
    /// </summary>
    internal static BillingDecision BuildDecision(Guid invoiceId, Guid submissionId, AssignFirmStoredDecision stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var values = new Dictionary<string, object?> { ["firm"] = stored.FirmId.ToString("D") };
        if (!string.IsNullOrWhiteSpace(stored.Note))
        {
            values["note"] = stored.Note;
        }

        var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["invoiceId"] = invoiceId,
            ["submissionId"] = submissionId,
            ["previousTrackingNumber"] = stored.PreviousTrackingNumber,
            ["trackingNumber"] = stored.TrackingNumber,
            ["policyCode"] = BillingRuleCatalogue.SystemPolicyCode,
            ["firmMissingSignals"] = InvoiceLineDecisionService.SetFact(stored.FirmMissingSignals),
            ["firmMissingCleared"] = true,
            ["renamedCounts"] = RenamedCounts(stored.Renamed, stored.FirmSetLines),
            ["rulesRerun"] = stored.RulesRerun,
        };

        return new BillingDecision(
            stored.DecisionId,
            AssignFirmActionCode,
            invoiceId,
            BillingDecider.Human(stored.DeciderUserId, DeciderRole),
            facts,
            "FirmAssigned",
            Values: values,
            ResolvedSignalIds: null,
            Written: [new DecisionRecordRef(InvoiceReviewReadService.InvoiceEntity, invoiceId)],
            DecidedOn: stored.DecidedOn);
    }

    /// <summary>
    /// The BOUNDED summary of the rename that goes into the Decision Record's facts (095d-1b review F1): a count per renamed table, never a row
    /// per rename. The full old-to-new mapping stays in the stored <see cref="StoredDecisionMember"/> member, so the fact snapshot stays far below
    /// the writer's 100,000-character cap at the transaction ceiling. The old and new invoice numbers are the <c>previousTrackingNumber</c> and
    /// <c>trackingNumber</c> facts. <c>firmSetLines</c> is every line the apply wrote the firm on, including those whose name did not change (so the
    /// record states every row the apply wrote); <c>lines</c> is only the lines renamed. <c>findings</c> is the submission's findings member,
    /// rewritten once by every apply.
    /// </summary>
    internal static Dictionary<string, object?> RenamedCounts(IReadOnlyCollection<AssignFirmRename> renamed, int firmSetLines) => new(StringComparer.Ordinal)
    {
        ["firmSetLines"] = firmSetLines,
        ["lines"] = renamed.Count(r => r.Entity == InvoiceSubmissionPersister.LineEntity),
        ["approval"] = renamed.Count(r => r.Entity == InvoiceSubmissionPersister.ApprovalEntity),
        ["storedFiles"] = renamed.Count(r => r.Entity == StoredFileSchema.Entity),
        ["findings"] = 1,
    };

    /// <summary>Sets <paramref name="firmId"/> on <paramref name="invoiceId"/> as <paramref name="deciderUserId"/>. See the class remarks.</summary>
    public async Task<AssignFirmResult> SetFirmAsync(
        Guid invoiceId, Guid firmId, string? note, Guid deciderUserId, string? callerBearerToken, CancellationToken ct)
    {
        if (invoiceId == Guid.Empty || deciderUserId == Guid.Empty)
        {
            return AssignFirmResult.Refuse(StatusCodes.Status400BadRequest, LineDecisionCodes.InvalidRequest, "An invoice and a caller are required.");
        }

        if (firmId == Guid.Empty)
        {
            return AssignFirmResult.Refuse(StatusCodes.Status400BadRequest, AssignFirmCodes.FirmRequired, "A firm is required.");
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote is { Length: > NoteMaxLength })
        {
            return AssignFirmResult.Refuse(StatusCodes.Status400BadRequest, AssignFirmCodes.NoteTooLong, $"The note is at most {NoteMaxLength} characters.");
        }

        var owner = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var decisionKey = SubmissionOverrideService.LockKey(invoiceId);
        var lineKey = InvoiceLineDecisionService.LockKeyFor(invoiceId);
        if (await AcquireAsync(decisionKey, owner, invoiceId, ct).ConfigureAwait(false) is { } first)
        {
            return first;
        }

        try
        {
            if (await AcquireAsync(lineKey, owner, invoiceId, ct).ConfigureAwait(false) is { } second)
            {
                return second;
            }

            try
            {
                return await AssignHeldAsync(invoiceId, firmId, trimmedNote, deciderUserId, callerBearerToken, ct).ConfigureAwait(false);
            }
            finally
            {
                await ReleaseAsync(lineKey, owner, invoiceId).ConfigureAwait(false);
            }
        }
        finally
        {
            await ReleaseAsync(decisionKey, owner, invoiceId).ConfigureAwait(false);
        }
    }

    /// <param name="OtherSubmissions">Fix round 3 (ontology F1): the invoice's other linked submissions (not the one that derives it), read with the state;
    /// approval's holds read every linked submission, so the review detail the apply derives must too.</param>
    private sealed record State(
        Entity Invoice, Entity Submission, string SubmissionVersion, string? InvoiceVersion, JsonObject? Findings, AssignFirmStoredDecision? Stored,
        IReadOnlyList<Entity> OtherSubmissions);

    private async Task<AssignFirmResult> AssignHeldAsync(
        Guid invoiceId, Guid firmId, string? note, Guid deciderUserId, string? callerBearerToken, CancellationToken ct)
    {
        var read = await ReadStateAsync(invoiceId, ct).ConfigureAwait(false);
        if (read.Failure is { } readFailure)
        {
            return readFailure;
        }

        var state = read.State!;
        var submissionId = state.Submission.Id;

        // ── A stored decision: the apply already committed. A repeat, never a second apply (1a K3). ──
        // 095d-1b F1: the core-record rights come first, so a caller without them gets one answer whatever firm it names (never the stored decision).
        // 095d-1c (pass 1 K2, uac-r2): the same check is what a repeat that RECORDS needs, asked as THIS caller in this request, before the record;
        // the filing is asked too, after the repeat is confirmed as this firm's and before the record (CompleteRepeatAsync).
        if (state.Stored is { } stored)
        {
            var repeatCheck = await CheckCoreRightsAsync(state.Invoice, invoiceId, submissionId, callerBearerToken, ct).ConfigureAwait(false);
            if (repeatCheck.Failure is { } repeatRefused)
            {
                return repeatRefused;
            }

            return await JudgeRepeatAsync(state, stored, firmId, deciderUserId, repeatCheck.Core, filingAsked: false, ct).ConfigureAwait(false);
        }

        var invoice = state.Invoice;
        if (invoice.GetAttributeValue<OptionSetValue>("statecode")?.Value != InvoiceStatusCodes.StateActive
            || invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value != InvoiceStatusCodes.InReview)
        {
            return AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.NotInReview, "Only an invoice In Review can have its firm set.", submissionId);
        }

        if (!InvoiceFirmHold.HasNoFirm(invoice)
            || state.Submission.GetAttributeValue<EntityReference>(TrackingNumberAllocator.FirmColumn) is { } subFirm && subFirm.Id != Guid.Empty)
        {
            return AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.FirmAlreadySet, "This invoice already has a firm.", submissionId);
        }

        try
        {
            if (!await InvoiceFirmHold.IsHeldAsync(_entities, invoiceId, ct).ConfigureAwait(false))
            {
                return AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.NotHeld, "This invoice is not waiting for a firm.", submissionId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "hold", ex);
        }

        var rights = await CheckCoreRightsAsync(invoice, invoiceId, submissionId, callerBearerToken, ct).ConfigureAwait(false);
        if (rights.Failure is { } rightsRefused)
        {
            return rightsRefused;
        }

        var core = rights.Core;
        var coreKind = rights.CoreKind;

        if (await CheckFilingAsync(invoiceId, core, submissionId, ct).ConfigureAwait(false) is { } filing)
        {
            return filing;
        }

        // ── The firm: active (the intake's rule), then one of the core record's assigned law firms (CheckFirmLinkAsync). Names no other firm. ──
        bool? active;
        try
        {
            active = await _isActiveFirm(firmId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            active = null;
            _logger.LogWarning("Set firm on invoice {InvoiceId}: the firm could not be read ({ExceptionType}).", invoiceId, ex.GetType().Name);
        }

        switch (active)
        {
            case null:
                return AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, AssignFirmCodes.FirmUnverifiable, "The firm could not be checked right now. Try again shortly.", submissionId);
            case false:
                return AssignFirmResult.Refuse(StatusCodes.Status400BadRequest, AssignFirmCodes.FirmInvalid, "The firm was not found or is not active.", submissionId);
        }

        switch (await _firmLink(new InvoiceCoreRecord(coreKind, core.Id), firmId, ct).ConfigureAwait(false))
        {
            case InvoiceFirmLink.Linked:
                break;
            case InvoiceFirmLink.NotLinked:
                _logger.LogInformation("Set firm on invoice {InvoiceId}: firm {FirmId} is not linked to its core record; nothing changed.", invoiceId, firmId);
                return AssignFirmResult.Refuse(StatusCodes.Status403Forbidden, AssignFirmCodes.FirmNotLinked,
                    "The firm you chose is not linked to this invoice's record.", submissionId);
            default:
                return AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, AssignFirmCodes.FirmUnverifiable, "The firm could not be checked right now. Try again shortly.", submissionId);
        }

        // ── P2: a no-firm submission is renumbered only once the scope key is Active (the allocator refuses too). ──
        if (!await _allocator.NumbersSubmissionsWithoutFirmAsync(ct).ConfigureAwait(false))
        {
            return AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, TrackingNumberReasons.ScopeKeyInactive,
                "The firm cannot be set in this environment yet. Try again later.", submissionId);
        }

        if (state.Findings is null)
        {
            _logger.LogError("Set firm on invoice {InvoiceId}: the submission's stored findings are not a JSON object; nothing changed.", invoiceId);
            return AssignFirmResult.Refuse(StatusCodes.Status500InternalServerError, AssignFirmCodes.FindingsUnreadable,
                "The submission's findings could not be read, so the firm was not set.", submissionId);
        }

        // ── What the apply renames and the firm_missing Signal set (read app-only, before any write). ──
        InvoiceSubmissionAfterSteps.ResolvedSignalSet signals;
        IReadOnlyList<Entity> lines, approvals, files;
        try
        {
            signals = await ReadFirmMissingSignalsAsync(invoiceId, ct).ConfigureAwait(false);
            lines = await ReadLinesAsync(invoiceId, ct).ConfigureAwait(false);
            approvals = await ReadApprovalsAsync(invoiceId, ct).ConfigureAwait(false);
            files = await ReadStoredFilesAsync(invoiceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "read", ex);
        }

        var previous = state.Submission.GetAttributeValue<string>(TrackingNumberAllocator.TrackingNumberColumn) ?? string.Empty;
        var firmInvoiceNumber = state.Submission.GetAttributeValue<string>(TrackingNumberAllocator.FirmInvoiceNumberColumn);
        if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(firmInvoiceNumber))
        {
            _logger.LogError("Set firm on invoice {InvoiceId}: the submission holds no tracking or firm invoice number; nothing changed.", invoiceId);
            return AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.NotRenumberable, "This invoice's submission cannot be renumbered.", submissionId);
        }

        var plan = new ApplyPlan(
            invoiceId, submissionId, firmId, note, deciderUserId, Seconds(_time.GetUtcNow()), previous,
            state.Submission.GetAttributeValue<int?>(TrackingNumberAllocator.SequenceColumn), state.InvoiceVersion, state.Findings, signals,
            lines, approvals, files)
        {
            WriteReviewDetail = await ReviewDetailProbe.IsPresentAsync(_entities, _logger, ct).ConfigureAwait(false),
            SubmissionReasonCode = state.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode),
            OtherSubmissions = state.OtherSubmissions
                .Select(s => new InvoiceOpenHolds.StoredSubmission(
                    s.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode),
                    s.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson)))
                .ToList(),
        };

        // ── The ceiling (16.7): one transaction or none. Asked before anything is built for the allocator. ──
        var requestCount = plan.RequestCount(previous);
        if (!TrackingNumberAllocator.FitsRenumberTransaction(requestCount))
        {
            _logger.LogWarning("Set firm on invoice {InvoiceId}: {Count} requests do not fit one transaction (at most {Max}); nothing changed.",
                invoiceId, requestCount, TrackingNumberAllocator.MaxRenumberCallerRequests);
            return AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.TooLarge,
                "This invoice has too many rows to set its firm in one step; nothing was changed.", submissionId);
        }

        // ── The longest number the allocator can hand out, so the size and Preflight checks hold for every attempt's number. ──
        var longest = new RenumberCandidate(
            string.Create(CultureInfo.InvariantCulture, $"{firmInvoiceNumber.Trim()}.{int.MaxValue}"), int.MaxValue);
        var probe = plan.Build(longest);
        if (probe.FindingsJson.Length > InvoiceSubmissionPersister.FindingsJsonMaxLength)
        {
            return AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.TooLarge,
                "This invoice has too many rows to set its firm in one step; nothing was changed.", submissionId);
        }

        try
        {
            _preflight(BuildDecision(invoiceId, submissionId, probe.Stored));
        }
        catch (DecisionRecordRefusedException ex)
        {
            _logger.LogError("Set firm on invoice {InvoiceId}: the Decision Record would be refused ({Reason}); nothing changed.", invoiceId, ex.Reason);
            return AssignFirmResult.Refuse(StatusCodes.Status500InternalServerError, AssignFirmCodes.DecisionInvalid,
                "The firm could not be set because its decision cannot be recorded; nothing was changed.", submissionId);
        }

        // ── The apply: ONE transaction through the allocator (095d-1a). The builder runs once per attempt; the last one built is what the
        //    committed attempt stored, so the record below is built from exactly the stored decision (095d-1c). ──
        RenumberResult result;
        BuiltApply? lastBuilt = null;
        try
        {
            result = await _allocator.RenumberSubmissionAsync(
                submissionId, state.SubmissionVersion, firmId, firmInvoiceNumber, candidate => (lastBuilt = plan.Build(candidate)).Apply, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError("Set firm on invoice {InvoiceId}: the allocator refused the apply as built ({ExceptionType}); nothing changed.", invoiceId, ex.GetType().Name);
            return AssignFirmResult.Refuse(StatusCodes.Status500InternalServerError, AssignFirmCodes.DecisionInvalid, "The firm could not be set.", submissionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The outcome is unknown (a timeout can land after the commit): the repeat reads the stored decision and never applies twice.
            _logger.LogWarning("Set firm on invoice {InvoiceId}: the apply's outcome is unknown ({ExceptionType}); repeat to check.", invoiceId, ex.GetType().Name);
            return AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, AssignFirmCodes.Unavailable,
                "The firm may not have been set. Repeat the request to check.", submissionId);
        }

        return result.Outcome switch
        {
            // 095d-1c (option 1, apply then record): the apply committed; the record follows from the decision that commit stored.
            RenumberOutcome.Renumbered => await RecordAppliedAsync(invoiceId, submissionId, lastBuilt, result.TrackingNumber, ct).ConfigureAwait(false),
            RenumberOutcome.Conflict or RenumberOutcome.AlreadyRenumbered => await AfterConflictAsync(invoiceId, firmId, deciderUserId, core, ct).ConfigureAwait(false),
            _ => FromRefused(result.Reason, submissionId),
        };
    }

    /// <summary>
    /// 095d-1c, after THIS call's apply committed: the Decision Record of the decision the committed attempt stored. The rights and the filing
    /// were asked before the apply in this request. Every failure from here on is <see cref="AssignFirmOutcome.Incomplete"/>, never 200 and never
    /// a "nothing changed" refusal: the firm is set.
    /// </summary>
    private async Task<AssignFirmResult> RecordAppliedAsync(
        Guid invoiceId, Guid submissionId, BuiltApply? committed, string? committedNumber, CancellationToken ct)
    {
        if (committed is null || !string.Equals(committed.Stored.TrackingNumber, committedNumber, StringComparison.Ordinal))
        {
            // Cannot happen with the allocator (it builds before it sends, and answers the number it sent). Fail closed: the repeat reads
            // the stored decision from the row and records that.
            _logger.LogError("Set firm on invoice {InvoiceId}: the committed number is not the one last built; the record is left to the repeat.", invoiceId);
            return Incomplete(invoiceId, submissionId, IncompleteStepRecord, "build mismatch");
        }

        return await RecordAsync(invoiceId, submissionId, committed.Stored, AssignFirmOutcome.Assigned, callerUserId: null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 095d-1c: a repeat of an applied decision for this firm (the allocator confirmed the stored number). The core-record rights were asked
    /// as this caller in this request before the repeat was judged; the filing is asked here, unless this request already asked it before its
    /// own apply attempt (the conflict path). Then the record, from the stored decision alone (same id and same content hash: the writer answers
    /// a second call with the first record and writes nothing). It never applies.
    /// </summary>
    private async Task<AssignFirmResult> CompleteRepeatAsync(
        State state, AssignFirmStoredDecision stored, Guid callerUserId, (string Entity, Guid Id) core, bool filingAsked, CancellationToken ct)
    {
        var invoiceId = state.Invoice.Id;
        var submissionId = state.Submission.Id;
        if (!filingAsked && await CheckFilingAsync(invoiceId, core, submissionId, ct).ConfigureAwait(false) is { } filing)
        {
            // 095d-1c review K1: a derived core stamp that is another record (CoreUnresolved) will not heal by repeating; a transient fault will.
            // Same response body either way; the marker lets an operator tell them apart in the log.
            if (string.Equals(filing.Code, AssignFirmCodes.CoreUnresolved, StringComparison.Ordinal))
            {
                _logger.LogError("Set firm on invoice {InvoiceId}: the repeat's filing is a permanent mismatch (Permanent={Permanent}, FilingCode={FilingCode}); the record is not written.",
                    invoiceId, true, filing.Code);
            }

            return Incomplete(invoiceId, submissionId, IncompleteStepFiling, filing.Code, callerUserId);
        }

        return await RecordAsync(invoiceId, submissionId, stored, AssignFirmOutcome.AlreadyAssigned, callerUserId, ct).ConfigureAwait(false);
    }

    /// <summary>The one record write of Set firm: <see cref="BillingDecisionRecorder.RecordAsync"/> with <see cref="BuildDecision"/> of the stored
    /// decision. A refusal or a fault (including no answer) is <see cref="AssignFirmOutcome.Incomplete"/> at step <c>record</c>.</summary>
    private async Task<AssignFirmResult> RecordAsync(
        Guid invoiceId, Guid submissionId, AssignFirmStoredDecision stored, AssignFirmOutcome outcome, Guid? callerUserId, CancellationToken ct)
    {
        BillingDecisionRecordResult recorded;
        try
        {
            recorded = await _record(BuildDecision(invoiceId, submissionId, stored), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Incomplete(invoiceId, submissionId, IncompleteStepRecord, (ex as DecisionRecordRefusedException)?.Reason.ToString() ?? ex.GetType().Name, callerUserId);
        }

        var recordState = recorded.AlreadyRecorded ? "already recorded" : "written";
        if (callerUserId is { } repeater)
        {
            // 095d-1c review K2: a repeat records the stored decider's decision; the log names who asked for the repeat.
            _logger.LogInformation("Set firm on invoice {InvoiceId}: Decision Record {RecordId} ({State}); repeated by {CallerUserId}.",
                invoiceId, recorded.RecordId, recordState, repeater);
        }
        else
        {
            _logger.LogInformation("Set firm on invoice {InvoiceId}: Decision Record {RecordId} ({State}).", invoiceId, recorded.RecordId, recordState);
        }

        return Assigned(outcome, submissionId, stored.TrackingNumber, stored.PreviousTrackingNumber) with { DecisionRecordId = recorded.RecordId };
    }

    /// <summary>
    /// The core-record rights, in one place for both the first apply and a repeat: the invoice's single core record, then the Signal-close rights
    /// (AppendTo on the core record, Write on the Signal's subject). The recorder's filing is not part of it (a repeat needs no filing).
    /// A failing caller gets the same <c>not_authorized</c> refusal whatever firm it names.
    /// </summary>
    private async Task<(AssignFirmResult? Failure, (string Entity, Guid Id) Core, InvoiceCoreRecordKind CoreKind)> CheckCoreRightsAsync(
        Entity invoice, Guid invoiceId, Guid submissionId, string? callerBearerToken, CancellationToken ct)
    {
        if (EligibleApproverResolver.SingleCoreOf(InvoiceReviewReadService.InvoiceEntity, invoice) is not { } core
            || InvoiceSubmissionService.CoreRecordKindOfTable(core.Entity) is not { } coreKind)
        {
            _logger.LogError("Set firm on invoice {InvoiceId}: the invoice names no single core record; nothing changed.", invoiceId);
            return (AssignFirmResult.Refuse(StatusCodes.Status500InternalServerError, AssignFirmCodes.CoreUnresolved, "The invoice names no single core record.", submissionId), default, default);
        }

        // ── As for approval: the Signal-close rights (AppendTo on the core record, Write on the Signal's subject). ──
        DecisionCallerRefusal refusal;
        try
        {
            refusal = await _closeRights(new DecisionCoreRecord(core.Entity, core.Id), invoiceId, callerBearerToken, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (Unavailable(invoiceId, "signal-rights", ex), default, default);
        }

        switch (refusal)
        {
            case DecisionCallerRefusal.None:
                return (null, core, coreKind);
            case DecisionCallerRefusal.Unavailable:
                return (AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, AssignFirmCodes.Unavailable, "The firm could not be set; nothing was changed. Try again.", submissionId), default, default);
            default:
                _logger.LogWarning("Set firm on invoice {InvoiceId}: the caller may not record this decision ({Refusal}); nothing changed.", invoiceId, refusal);
                return (AssignFirmResult.Refuse(StatusCodes.Status403Forbidden, AssignFirmCodes.NotAuthorized,
                    "You do not have permission to record a decision on this invoice's record.", submissionId), default, default);
        }
    }

    /// <summary>
    /// The submission changed since it was read (or the allocator saw it already moved): re-read. A stored decision now present is judged as a
    /// repeat (this firm: already assigned; another: 409). Without one, something else changed the row: 409, reload.
    /// </summary>
    private async Task<AssignFirmResult> AfterConflictAsync(Guid invoiceId, Guid firmId, Guid callerUserId, (string Entity, Guid Id) core, CancellationToken ct)
    {
        var read = await ReadStateAsync(invoiceId, ct).ConfigureAwait(false);
        if (read.Failure is { } failure)
        {
            return failure;
        }

        // Reached only after AssignHeldAsync's core-record rights check AND its filing check passed in this request (095d-1b F1), so no
        // second check here; a repeat confirmed below records (095d-1c).
        if (read.State!.Stored is { } stored)
        {
            return await JudgeRepeatAsync(read.State, stored, firmId, callerUserId, core, filingAsked: true, ct).ConfigureAwait(false);
        }

        _logger.LogWarning("Set firm on invoice {InvoiceId}: the invoice or its submission changed while the request was prepared; nothing changed.", invoiceId);
        return AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.Changed,
            "The invoice changed while the firm was being set. Reload and try again.", read.State.Submission.Id);
    }

    /// <summary>
    /// 1a review K3: a stored decision exists, so the apply committed. It is this request's only when it names this firm. The allocator is asked
    /// at the current version with a builder that throws (a repeat can never apply), and its answer must name the stored number. A repeat so
    /// confirmed completes the Decision Record (095d-1c, <see cref="CompleteRepeatAsync"/>).
    /// </summary>
    private async Task<AssignFirmResult> JudgeRepeatAsync(
        State state, AssignFirmStoredDecision stored, Guid firmId, Guid callerUserId, (string Entity, Guid Id) core, bool filingAsked, CancellationToken ct)
    {
        var submissionId = state.Submission.Id;
        if (stored.FirmId != firmId)
        {
            _logger.LogInformation("Set firm on invoice {InvoiceId}: a firm was already assigned by another decision; nothing changed.", state.Invoice.Id);
            return AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.AssignedOtherFirm,
                "This invoice's firm was already set by another decision.", submissionId);
        }

        // 095d-1c review K3: the apply stores the decision under the id derived from this submission. A stored member with any other id is not
        // what the apply wrote; the record is never built from it (the writer would file a second decision under that id).
        if (stored.DecisionId != DecisionIdFor(submissionId))
        {
            _logger.LogError("Set firm on invoice {InvoiceId}: the stored decision's id is not the one derived from its submission; nothing changed.", state.Invoice.Id);
            return AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.Changed, "The invoice changed after its firm was set. Reload.", submissionId);
        }

        var firmInvoiceNumber = state.Submission.GetAttributeValue<string>(TrackingNumberAllocator.FirmInvoiceNumberColumn);
        if (string.IsNullOrWhiteSpace(firmInvoiceNumber))
        {
            return AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.Changed, "The invoice changed after its firm was set. Reload.", submissionId);
        }

        RenumberResult result;
        try
        {
            result = await _allocator.RenumberSubmissionAsync(
                submissionId, state.SubmissionVersion, firmId, firmInvoiceNumber,
                _ => throw new RepeatMustNotApplyException(), ct).ConfigureAwait(false);
        }
        catch (RepeatMustNotApplyException ex)
        {
            // The builder's trip (a repeat must never apply): the allocator reached for an apply, so the row is not as the stored decision says.
            _logger.LogError("Set firm on invoice {InvoiceId}: the repeat reached the apply builder ({ExceptionType}); nothing changed.", state.Invoice.Id, ex.GetType().Name);
            return AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.Changed, "The invoice changed after its firm was set. Reload.", submissionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Any other fault (a timeout, a read fault) says nothing about the row: the repeat can be tried again.
            _logger.LogWarning("Set firm on invoice {InvoiceId}: the repeat could not be confirmed ({ExceptionType}); nothing changed.", state.Invoice.Id, ex.GetType().Name);
            return AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, AssignFirmCodes.Unavailable,
                "The firm could not be confirmed right now. Try again shortly.", submissionId);
        }

        switch (result.Outcome)
        {
            case RenumberOutcome.AlreadyRenumbered when string.Equals(result.TrackingNumber, stored.TrackingNumber, StringComparison.Ordinal):
                return await CompleteRepeatAsync(state, stored, callerUserId, core, filingAsked, ct).ConfigureAwait(false);

            // 16.6.1 edge: a legacy row that held no sequence in the none scope was renumbered without a previous sequence, so the allocator
            // cannot tell it from a native row of the firm. The stored decision decides: it names this firm and the row holds its number.
            case RenumberOutcome.Refused when result.Reason == TrackingNumberReasons.NotUnnumbered && stored.PreviousSequence is null
                && HoldsStoredNumber(state.Submission, stored):
                return await CompleteRepeatAsync(state, stored, callerUserId, core, filingAsked, ct).ConfigureAwait(false);

            case RenumberOutcome.Refused when result.Reason != TrackingNumberReasons.NotUnnumbered:
                return FromRefused(result.Reason, submissionId);

            default:
                _logger.LogError("Set firm on invoice {InvoiceId}: the stored decision and the submission's number disagree ({Outcome}); nothing changed.",
                    state.Invoice.Id, result.Outcome);
                return AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.Changed, "The invoice changed after its firm was set. Reload.", submissionId);
        }
    }

    /// <summary>
    /// The repeat's apply builder throws this: the allocator reached for an apply, so the row is not as the stored decision says. It is its own
    /// type so that only this trip is 409 changed; the allocator's own <see cref="InvalidOperationException"/>s and any other fault are 503.
    /// </summary>
    private sealed class RepeatMustNotApplyException()
        : InvalidOperationException("A repeat of an applied Set-firm decision must never apply again.");

    /// <summary>
    /// The submission is in the stored firm's scope with the stored number (the legacy repeat's check). P0: the scope column is not read (it does not
    /// exist), so the scope is null and the check is false (fail closed: an unconfirmed legacy repeat is a 409 "reload", never a completed record).
    /// The allocator refuses a renumber before P2 first, so this is not reached there.
    /// </summary>
    private static bool HoldsStoredNumber(Entity submission, AssignFirmStoredDecision stored) =>
        submission.GetAttributeValue<EntityReference>(TrackingNumberAllocator.FirmColumn)?.Id == stored.FirmId
        && string.Equals(submission.GetAttributeValue<string>(TrackingNumberAllocator.ScopeColumn), TrackingNumberAllocator.ScopeOf(stored.FirmId), StringComparison.OrdinalIgnoreCase)
        && string.Equals(submission.GetAttributeValue<string>(TrackingNumberAllocator.TrackingNumberColumn), stored.TrackingNumber, StringComparison.Ordinal);

    // ── The apply ──

    /// <summary>One attempt's apply and the decision it stores.</summary>
    private sealed record BuiltApply(RenumberApply Apply, AssignFirmStoredDecision Stored, string FindingsJson);

    /// <summary>Everything the apply is built from, read before any write. <see cref="Build"/> is pure: called once per attempt.</summary>
    private sealed record ApplyPlan(
        Guid InvoiceId,
        Guid SubmissionId,
        Guid FirmId,
        string? Note,
        Guid DeciderUserId,
        DateTimeOffset DecidedOn,
        string PreviousTrackingNumber,
        int? PreviousSequence,
        string? InvoiceVersion,
        JsonObject Findings,
        InvoiceSubmissionAfterSteps.ResolvedSignalSet Signals,
        IReadOnlyList<Entity> Lines,
        IReadOnlyList<Entity> Approvals,
        IReadOnlyList<Entity> Files)
    {
        /// <summary>082c-2: whether <see cref="InvoiceReviewDetail.Column"/> exists (read by the service before it builds the plan).</summary>
        public bool WriteReviewDetail { get; init; }

        /// <summary>Fix round 3 (ontology F1): the derived submission's stored outcome reason, which the apply does not write (so it is what remains).</summary>
        public string? SubmissionReasonCode { get; init; }

        /// <summary>Fix round 3 (ontology F1): the invoice's other linked submissions as stored (the apply writes none of them).</summary>
        public IReadOnlyList<InvoiceOpenHolds.StoredSubmission> OtherSubmissions { get; init; } = [];

        /// <summary>The caller requests of one attempt: the invoice, every line, and each approval and file whose name carries the number.</summary>
        public int RequestCount(string newNumberShape) =>
            1 + Lines.Count
            + Approvals.Count(a => RenamedApproval(a.GetAttributeValue<string>(InvoiceSubmissionPersister.ApprovalName), PreviousTrackingNumber, newNumberShape) is not null)
            + Files.Count(f => RenamedFile(f.GetAttributeValue<string>(StoredFileSchema.Name), PreviousTrackingNumber, newNumberShape) is not null);

        public BuiltApply Build(RenumberCandidate candidate)
        {
            var number = candidate.TrackingNumber;
            var firm = new EntityReference("sprk_organization", FirmId);
            var renamed = new List<AssignFirmRename>();
            var requests = new List<OrganizationRequest>();

            // The invoice: its firm, its number and its own hold cleared, conditioned on the version read (16.4 condition 2).
            var invoice = new Entity(InvoiceReviewReadService.InvoiceEntity, InvoiceId)
            {
                [InvoiceSubmissionPersister.InvoiceVendorOrg] = firm,
                [InvoiceReviewService.InvTrackingNumber] = number,
                [InvoiceFirmHold.InvoiceHoldColumn] = null,
            };

            // (The review detail, 082c-2 ontology D-205 writer 3, is set below, once the findings this apply stores are built.)
            if (InvoiceVersion is not null)
            {
                invoice.RowVersion = InvoiceVersion;
            }

            requests.Add(new UpdateRequest
            {
                Target = invoice,
                ConcurrencyBehavior = InvoiceVersion is not null ? ConcurrencyBehavior.IfRowVersionMatches : ConcurrencyBehavior.Default,
            });

            // Each line: its firm, and its name when it carries the number (InvoiceSubmissionPersister.LineNameFor).
            foreach (var line in Lines)
            {
                var row = new Entity(InvoiceSubmissionPersister.LineEntity, line.Id) { [InvoiceSubmissionPersister.LineVendorOrg] = firm };
                var name = line.GetAttributeValue<string>(InvoiceSubmissionPersister.LineName);
                if (RenamedLine(name, PreviousTrackingNumber, number) is { } to)
                {
                    row[InvoiceSubmissionPersister.LineName] = to;
                    renamed.Add(new AssignFirmRename(InvoiceSubmissionPersister.LineEntity, line.Id, InvoiceSubmissionPersister.LineName, name!, to));
                }

                requests.Add(new UpdateRequest { Target = row });
            }

            foreach (var approval in Approvals)
            {
                var name = approval.GetAttributeValue<string>(InvoiceSubmissionPersister.ApprovalName);
                if (RenamedApproval(name, PreviousTrackingNumber, number) is { } to)
                {
                    requests.Add(new UpdateRequest
                    {
                        Target = new Entity(InvoiceSubmissionPersister.ApprovalEntity, approval.Id) { [InvoiceSubmissionPersister.ApprovalName] = to },
                    });
                    renamed.Add(new AssignFirmRename(InvoiceSubmissionPersister.ApprovalEntity, approval.Id, InvoiceSubmissionPersister.ApprovalName, name!, to));
                }
            }

            // Stored files (ontology-r1 16.7: inside the apply): the display name only, never purpose, parent or status.
            foreach (var file in Files)
            {
                var name = file.GetAttributeValue<string>(StoredFileSchema.Name);
                if (RenamedFile(name, PreviousTrackingNumber, number) is { } to)
                {
                    var rename = StoredFileRename(file.Id, name!, to);
                    requests.Add(rename.Request);
                    renamed.Add(rename.Mapping);
                }
            }

            var stored = new AssignFirmStoredDecision(
                DecisionIdFor(SubmissionId), FirmId, Note, PreviousTrackingNumber, PreviousSequence, number, candidate.Sequence, DeciderUserId,
                DecidedOn, Signals, renamed, Lines.Count, RulesRerunPending);

            // The findings: the new number, and the decision stored with the apply. Every other member is kept as it was.
            var findings = (JsonObject)Findings.DeepClone();
            findings[FindingsTrackingNumberMember] = number;
            findings[StoredDecisionMember] = stored.ToJson();
            var json = findings.ToJsonString();

            // 082c-2 (ontology D-205 writer 3), fix round 3 (ontology F1): the review detail is recomputed from the holds that REMAIN after this
            // write, in this same write, through the one derivation approval's holds agree with (InvoiceOpenHolds). The apply sets the firm and
            // clears the invoice's own hold, but it keeps every findings member except the number and the decision, and leaves the stored outcome
            // reason: so a firm_missing marker the findings or the reason still carry still holds the invoice (approval refuses it until 095d-2's
            // re-run clears the markers), and the detail stays "Waiting for the firm". It is blank only when nothing is held. Left out where the
            // column does not exist yet. (An In Review invoice only: the service refuses any other status above.)
            if (WriteReviewDetail)
            {
                invoice[InvoiceReviewDetail.Column] = InvoiceReviewDetail.ValueOf(InvoiceReviewDetail.ReviewDetailOf(
                    InvoiceOpenHolds.ReasonsOf(
                        invoiceHasNoFirm: false, // the firm is set in this write
                        invoiceHoldSet: false, // its own hold is cleared in this write
                        [new InvoiceOpenHolds.StoredSubmission(SubmissionReasonCode, json), .. OtherSubmissions])));
            }

            return new BuiltApply(
                new RenumberApply(new Dictionary<string, object> { [InvoiceSubmissionPersister.SubmissionFindingsJson] = json }, requests),
                stored,
                json);
        }
    }

    /// <summary>
    /// The ONE Stored File write of assign-firm (<c>InvoicePdfWriterGuardTests.ApprovedWriters</c>): the display name <c>sprk_name</c> only. It
    /// never writes purpose, parent, source or status, so nothing is attached onto the invoice. Not conditioned: no other writer changes a name.
    /// It also gives the rename's old-to-new mapping, so the apply's builder names no Stored File itself.
    /// </summary>
    internal static (UpdateRequest Request, AssignFirmRename Mapping) StoredFileRename(Guid fileId, string from, string to) =>
        (new UpdateRequest { Target = new Entity(StoredFileSchema.Entity, fileId) { [StoredFileSchema.Name] = to } },
            new AssignFirmRename(StoredFileSchema.Entity, fileId, StoredFileSchema.Name, from, to));

    /// <summary>A line name <c>{number} #{n}</c> (<see cref="InvoiceSubmissionPersister.LineNameFor"/>) of the old number, renamed; null otherwise.</summary>
    internal static string? RenamedLine(string? name, string previous, string next) =>
        name is not null && name.StartsWith(previous + " #", StringComparison.Ordinal) ? next + name[previous.Length..] : null;

    /// <summary>The approval name <c>Approval {number}</c> of the old number, renamed; null otherwise.</summary>
    internal static string? RenamedApproval(string? name, string previous, string next) =>
        string.Equals(name, ApprovalNamePrefix + previous, StringComparison.Ordinal) ? ApprovalNamePrefix + next : null;

    /// <summary>
    /// A stored file name that starts with the old number's name segment (<see cref="StoredFileSchema.NameSegmentOf"/>) followed by <c>.</c>,
    /// <c>-</c> or nothing (<c>{n}.pdf</c>, <c>{n}-2.pdf</c>), renamed and cut to the column; null otherwise (so <c>INV-7.10.pdf</c> is not
    /// <c>INV-7.1</c>'s).
    /// </summary>
    internal static string? RenamedFile(string? name, string previous, string next)
    {
        if (name is null || StoredFileSchema.NameSegmentOf(previous) is not { } from || StoredFileSchema.NameSegmentOf(next) is not { } to
            || !name.StartsWith(from, StringComparison.Ordinal)
            || (name.Length > from.Length && name[from.Length] is not ('.' or '-')))
        {
            return null;
        }

        var renamed = to + name[from.Length..];
        return renamed.Length <= StoredFileSchema.NameMaxLength ? renamed : renamed[..StoredFileSchema.NameMaxLength];
    }

    // ── Reads (app-only, IGenericEntityService) ──

    private sealed record StateRead(State? State, AssignFirmResult? Failure);

    /// <summary>The invoice and its ONE submission (the row whose id derives the invoice), with their row versions and the stored decision.</summary>
    private async Task<StateRead> ReadStateAsync(Guid invoiceId, CancellationToken ct)
    {
        Entity? invoice;
        List<Entity> submissions, others;
        try
        {
            var query = new QueryExpression(InvoiceReviewReadService.InvoiceEntity) { ColumnSet = new ColumnSet(InvoiceColumns), TopCount = 1 };
            query.Criteria.AddCondition($"{InvoiceReviewReadService.InvoiceEntity}id", ConditionOperator.Equal, invoiceId);
            invoice = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
            if (invoice is null)
            {
                return new StateRead(null, AssignFirmResult.Refuse(StatusCodes.Status404NotFound, "ebilling.invoice.not_found", "Not found."));
            }

            // The allocator's own phase source (the same cache its numbering reads): the scope column is selected only once it exists (not at P0).
            var scopeColumnExists = await _allocator.ScopeColumnExistsAsync(ct).ConfigureAwait(false);
            var subs = new QueryExpression(TrackingNumberAllocator.SubmissionEntity) { ColumnSet = new ColumnSet(SubmissionColumnsFor(scopeColumnExists)) };
            subs.Criteria.AddCondition(InvoiceSubmissionPersister.SubmissionInvoice, ConditionOperator.Equal, invoiceId);
            var linked = (await _entities.RetrieveMultipleAsync(subs, ct).ConfigureAwait(false)).Entities;
            submissions = linked.Where(s => InvoiceSubmissionPersister.InvoiceIdFor(s.Id) == invoiceId).ToList();
            others = linked.Where(s => InvoiceSubmissionPersister.InvoiceIdFor(s.Id) != invoiceId).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new StateRead(null, Unavailable(invoiceId, "read", ex));
        }

        if (submissions.Count != 1)
        {
            _logger.LogWarning("Set firm on invoice {InvoiceId}: {Count} submissions derive the invoice; nothing changed.", invoiceId, submissions.Count);
            return new StateRead(null, AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.NoSubmission, "This invoice has no submission to set the firm on."));
        }

        var submission = submissions[0];
        if (VersionOf(submission) is not { } version)
        {
            return new StateRead(null, AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, AssignFirmCodes.Unavailable,
                "The firm could not be set; nothing was changed. Try again.", submission.Id));
        }

        JsonObject? findings = null;
        AssignFirmStoredDecision? stored = null;
        var json = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
        try
        {
            findings = string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject;
            stored = findings is null ? null : AssignFirmStoredDecision.Read(findings);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            // A stored decision that cannot be read: what was applied cannot be known, so nothing is changed (fail closed).
            _logger.LogError("Set firm on invoice {InvoiceId}: the submission's stored findings cannot be read ({ExceptionType}); nothing changed.", invoiceId, ex.GetType().Name);
            return new StateRead(null, AssignFirmResult.Refuse(StatusCodes.Status500InternalServerError, AssignFirmCodes.FindingsUnreadable,
                "The submission's findings could not be read, so the firm was not set.", submission.Id));
        }

        return new StateRead(new State(invoice, submission, version, VersionOf(invoice), findings, stored, others), null);
    }

    /// <summary>The open firm_missing Signal set on the invoice: the system policy's invoice-level Signals (16.7: stored, never closed here).</summary>
    private async Task<InvoiceSubmissionAfterSteps.ResolvedSignalSet> ReadFirmMissingSignalsAsync(Guid invoiceId, CancellationToken ct)
    {
        var readAt = _time.GetUtcNow().UtcDateTime;
        var open = await InvoiceSubmissionAfterSteps.ReadOpenSignalsAsync(_entities, invoiceId, [], _logger, ct).ConfigureAwait(false);
        var signals = open
            .Where(s => s.SubjectEntity == InvoiceReviewReadService.InvoiceEntity && s.SubjectId == invoiceId
                && string.Equals(s.Signal.PolicyCode, BillingRuleCatalogue.SystemPolicyCode, StringComparison.Ordinal))
            .Select(s => s.Signal).DistinctBy(s => s.Id).OrderBy(s => s.Id).ToList();
        return new InvoiceSubmissionAfterSteps.ResolvedSignalSet(readAt, signals);
    }

    private Task<List<Entity>> ReadLinesAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceSubmissionPersister.LineEntity)
        {
            ColumnSet = new ColumnSet(InvoiceSubmissionPersister.LineName),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 },
        };
        query.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.Equal, invoiceId);
        query.AddOrder($"{InvoiceSubmissionPersister.LineEntity}id", OrderType.Ascending);
        return InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct);
    }

    private Task<List<Entity>> ReadApprovalsAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceSubmissionPersister.ApprovalEntity)
        {
            ColumnSet = new ColumnSet(InvoiceSubmissionPersister.ApprovalName),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 },
        };
        query.Criteria.AddCondition(InvoiceSubmissionPersister.ApprovalInvoice, ConditionOperator.Equal, invoiceId);
        query.AddOrder($"{InvoiceSubmissionPersister.ApprovalEntity}id", OrderType.Ascending);
        return InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct);
    }

    /// <summary>The invoice's Stored File rows (a read: names only).</summary>
    private Task<List<Entity>> ReadStoredFilesAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(StoredFileSchema.Entity)
        {
            ColumnSet = new ColumnSet(StoredFileSchema.Name),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 },
        };
        query.Criteria.AddCondition(StoredFileSchema.InvoiceTable, ConditionOperator.Equal, invoiceId);
        query.AddOrder(StoredFileSchema.IdColumn, OrderType.Ascending);
        return InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct);
    }

    /// <summary>The recorder files the record by the invoice's derived core stamp; it must be exactly the invoice's core record (as approval).</summary>
    private async Task<AssignFirmResult?> CheckFilingAsync(Guid invoiceId, (string Entity, Guid Id) core, Guid submissionId, CancellationToken ct)
    {
        CoreAncestorResult derived;
        try
        {
            derived = await _filing(invoiceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "filing", ex);
        }

        if (!derived.Succeeded && !derived.IsPermanent)
        {
            return AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, AssignFirmCodes.Unavailable, "The firm could not be set; nothing was changed. Try again.", submissionId);
        }

        if (derived.Stamps is [var own] && string.Equals(own.EntityType, core.Entity, StringComparison.OrdinalIgnoreCase) && own.RecordId == core.Id)
        {
            return null;
        }

        _logger.LogError("Set firm on invoice {InvoiceId}: the derived core record ({Status}, {Count} stamps) is not its core lookup; nothing changed.",
            invoiceId, derived.Status, derived.Stamps.Count);
        return AssignFirmResult.Refuse(StatusCodes.Status500InternalServerError, AssignFirmCodes.CoreUnresolved, "The invoice names no single core record.", submissionId);
    }

    /// <summary>The row version as the persister reads it: the <c>versionnumber</c> column, else <see cref="Entity.RowVersion"/>.</summary>
    private static string? VersionOf(Entity row) =>
        row.TryGetAttributeValue<long>("versionnumber", out var v) && v > 0 ? v.ToString(CultureInfo.InvariantCulture)
        : string.IsNullOrWhiteSpace(row.RowVersion) ? null : row.RowVersion;

    private static DateTimeOffset Seconds(DateTimeOffset now) => new(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    // ── Results ──

    private static AssignFirmResult Assigned(AssignFirmOutcome outcome, Guid submissionId, string trackingNumber, string previous) =>
        new(outcome, StatusCodes.Status200OK, "ok", SubmissionId: submissionId, DecisionId: DecisionIdFor(submissionId),
            TrackingNumber: trackingNumber, PreviousTrackingNumber: previous, RulesRerun: RulesRerunPending);

    /// <summary>The allocator's refusals (095d-1a outcomes): each a stable code; nothing was written.</summary>
    private static AssignFirmResult FromRefused(string? reason, Guid submissionId) => reason switch
    {
        TrackingNumberReasons.Unavailable => AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, TrackingNumberReasons.Unavailable,
            "A tracking number could not be taken right now; nothing was changed. Try again.", submissionId),
        TrackingNumberReasons.ScopeKeyInactive => AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, TrackingNumberReasons.ScopeKeyInactive,
            "The firm cannot be set in this environment yet. Try again later.", submissionId),
        TrackingNumberReasons.SchemaMissing => AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, TrackingNumberReasons.SchemaMissing,
            "The firm cannot be set in this environment yet. Try again later.", submissionId),
        TrackingNumberReasons.TooManyRequests => AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.TooLarge,
            "This invoice has too many rows to set its firm in one step; nothing was changed.", submissionId),
        TrackingNumberReasons.NotUnnumbered => AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.NotRenumberable,
            "This invoice's submission cannot be renumbered.", submissionId),
        _ => AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, AssignFirmCodes.Unavailable, "The firm could not be set; nothing was changed. Try again.", submissionId),
    };

    /// <summary>095d-1c <c>failedStep</c>: the Decision Record write was refused, faulted or got no answer.</summary>
    internal const string IncompleteStepRecord = "record";

    /// <summary>095d-1c <c>failedStep</c>: on a repeat, the recorder's filing (the invoice's derived core stamp) could not be confirmed before the record.</summary>
    internal const string IncompleteStepFiling = "filing";

    /// <summary>The one sentence of every <see cref="AssignFirmOutcome.Incomplete"/> answer: no id, no exception text.</summary>
    internal const string IncompleteDetail = "The firm was set, but its decision was not recorded. Repeat the request to finish it.";

    /// <summary>
    /// 095d-1c: the apply committed and <paramref name="step"/> did not finish. 503 with the step and the fixed sentence; the log carries the
    /// cause as a reason code or an exception type name (never its message). The repeat finishes from the stored decision and never applies again.
    /// 095d-1c uac-r2 K2: on a repeat <paramref name="callerUserId"/> is the repeating caller, a structured log parameter only (no response change),
    /// so a caller's failed repeats can be audited; the first apply's own incomplete passes none.
    /// </summary>
    private AssignFirmResult Incomplete(Guid invoiceId, Guid submissionId, string step, string cause, Guid? callerUserId = null)
    {
        if (callerUserId is { } repeater)
        {
            _logger.LogError("Set firm on invoice {InvoiceId}: the firm is set but the {Step} step did not finish ({Cause}); a repeat finishes it; repeated by {CallerUserId}.",
                invoiceId, step, cause, repeater);
        }
        else
        {
            _logger.LogError("Set firm on invoice {InvoiceId}: the firm is set but the {Step} step did not finish ({Cause}); a repeat finishes it.",
                invoiceId, step, cause);
        }

        return new AssignFirmResult(AssignFirmOutcome.Incomplete, StatusCodes.Status503ServiceUnavailable, AssignFirmCodes.Incomplete, IncompleteDetail,
            SubmissionId: submissionId, FailedStep: step);
    }

    private AssignFirmResult Unavailable(Guid invoiceId, string step, Exception ex)
    {
        _logger.LogWarning("Set firm on invoice {InvoiceId}: the {Step} step failed ({ExceptionType}); nothing was changed.", invoiceId, step, ex.GetType().Name);
        return AssignFirmResult.Refuse(StatusCodes.Status503ServiceUnavailable, AssignFirmCodes.Unavailable, "The firm could not be set; nothing was changed. Try again.");
    }

    // ── Locks (approval's pattern) ──

    private async Task<AssignFirmResult?> AcquireAsync(string key, string owner, Guid invoiceId, CancellationToken ct)
    {
        bool acquired;
        try
        {
            acquired = await _locks.TryAcquireProcessingLockAsync(key, owner, LockDuration, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Unavailable(invoiceId, "lock", ex);
        }

        return acquired
            ? null
            : AssignFirmResult.Refuse(StatusCodes.Status409Conflict, AssignFirmCodes.Busy, "Another decision on this invoice is in progress. Try again.");
    }

    private async Task ReleaseAsync(string key, string owner, Guid invoiceId)
    {
        try
        {
            await _locks.ReleaseProcessingLockAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Set firm on invoice {InvoiceId}: a processing lock could not be released ({ExceptionType}); it expires.", invoiceId, ex.GetType().Name);
        }
    }
}
