using System.Data;
using System.Globalization;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The invoice lifecycle states (ebilling-r1 FR-06, D-41). Business names; the Dataverse numbers live in
/// <see cref="InvoiceStatusCodes"/> and nowhere else.
/// </summary>
public enum InvoiceLifecycleStatus
{
    /// <summary>Active (statecode 0). Every new invoice starts here.</summary>
    InReview,

    /// <summary>Active (statecode 0).</summary>
    Approved,

    /// <summary>Active (statecode 0). Exported to accounts payable.</summary>
    SentToAp,

    /// <summary>Inactive (statecode 1). Final: one payment of the full approved total, never reopened.</summary>
    Paid,

    /// <summary>Inactive (statecode 1). Final: not payable, distinct from Return to Firm (D-34).</summary>
    Rejected,

    /// <summary>Inactive (statecode 1). Final: closed; an accepted resubmission is a new invoice (D-43).</summary>
    ReturnedToFirm,

    /// <summary>Inactive (statecode 1). Final.</summary>
    Cancelled,
}

/// <summary>
/// The one home of the <c>sprk_invoice</c> <c>statecode</c> / <c>statuscode</c> numbers and their pairing. Values are the live dev
/// option values task 012 created (<c>scripts/ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1</c>; recorded in
/// <c>docs/data-model/sprk_invoice.md</c> "Status"). In Review and Cancelled are the platform's own values 1 and 2, relabelled; the other
/// five are explicit publisher-range values (prefix 65949) so every environment gets the same numbers. Dataverse rejects a write whose
/// statecode does not own the statuscode, so the pair always travels together (see <see cref="StateCodeOf"/>).
/// </summary>
public static class InvoiceStatusCodes
{
    /// <summary>statecode of an in-flight invoice (In Review, Approved, Sent to AP).</summary>
    public const int StateActive = 0;

    /// <summary>statecode of a closed invoice (Paid, Rejected, Returned to Firm, Cancelled).</summary>
    public const int StateInactive = 1;

    /// <summary>In Review — statecode 0 (the platform default "Active", relabelled).</summary>
    public const int InReview = 1;

    /// <summary>Approved — statecode 0.</summary>
    public const int Approved = 659490000;

    /// <summary>Sent to AP — statecode 0.</summary>
    public const int SentToAp = 659490001;

    /// <summary>Paid — statecode 1.</summary>
    public const int Paid = 659490002;

    /// <summary>Rejected — statecode 1.</summary>
    public const int Rejected = 659490003;

    /// <summary>Returned to Firm — statecode 1.</summary>
    public const int ReturnedToFirm = 659490004;

    /// <summary>Cancelled — statecode 1 (the platform default "Inactive", relabelled).</summary>
    public const int Cancelled = 2;

    /// <summary>The statuscode Dataverse stores for <paramref name="status"/>.</summary>
    public static int StatusCodeOf(InvoiceLifecycleStatus status) => status switch
    {
        InvoiceLifecycleStatus.InReview => InReview,
        InvoiceLifecycleStatus.Approved => Approved,
        InvoiceLifecycleStatus.SentToAp => SentToAp,
        InvoiceLifecycleStatus.Paid => Paid,
        InvoiceLifecycleStatus.Rejected => Rejected,
        InvoiceLifecycleStatus.ReturnedToFirm => ReturnedToFirm,
        InvoiceLifecycleStatus.Cancelled => Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown invoice status."),
    };

    /// <summary>The statecode that owns <paramref name="status"/>'s statuscode.</summary>
    public static int StateCodeOf(InvoiceLifecycleStatus status) => status switch
    {
        InvoiceLifecycleStatus.InReview or InvoiceLifecycleStatus.Approved or InvoiceLifecycleStatus.SentToAp => StateActive,
        InvoiceLifecycleStatus.Paid or InvoiceLifecycleStatus.Rejected or InvoiceLifecycleStatus.ReturnedToFirm or InvoiceLifecycleStatus.Cancelled => StateInactive,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown invoice status."),
    };

    /// <summary>Maps a stored statuscode to its status; false for a value this lifecycle does not know (fail closed).</summary>
    public static bool TryFromStatusCode(long statusCode, out InvoiceLifecycleStatus status)
    {
        switch (statusCode)
        {
            case InReview: status = InvoiceLifecycleStatus.InReview; return true;
            case Approved: status = InvoiceLifecycleStatus.Approved; return true;
            case SentToAp: status = InvoiceLifecycleStatus.SentToAp; return true;
            case Paid: status = InvoiceLifecycleStatus.Paid; return true;
            case Rejected: status = InvoiceLifecycleStatus.Rejected; return true;
            case ReturnedToFirm: status = InvoiceLifecycleStatus.ReturnedToFirm; return true;
            case Cancelled: status = InvoiceLifecycleStatus.Cancelled; return true;
            default: status = default; return false;
        }
    }
}

/// <summary>What a transition call did.</summary>
public enum InvoiceTransitionOutcome
{
    /// <summary>The invoice moved; statecode and statuscode were written.</summary>
    Transitioned,

    /// <summary>The invoice was already in the requested status; nothing was written (idempotent no-op).</summary>
    AlreadyInTarget,

    /// <summary>Nothing was written; <see cref="InvoiceTransitionResult.Reason"/> is a stable code from <see cref="InvoiceTransitionReasons"/>.</summary>
    Refused,
}

/// <summary>Stable reason codes of a refused transition. Callers map them to their own responses; the strings are a contract.</summary>
public static class InvoiceTransitionReasons
{
    /// <summary>The (current, requested) pair is not in the FR-06 matrix. This includes every move out of a final status.</summary>
    public const string NotAllowed = "invoice.transition_not_allowed";

    /// <summary>The invoice does not exist (or was deleted while the transition ran).</summary>
    public const string NotFound = "invoice.not_found";

    /// <summary>The stored statuscode is not one the lifecycle knows, so no move out of it can be judged (fail closed).</summary>
    public const string StatusUnknown = "invoice.status_unknown";

    /// <summary>Other writers changed the invoice on every attempt; nothing was written. The caller may retry.</summary>
    public const string ConcurrentChange = "invoice.concurrent_change";
}

/// <summary>The result of <see cref="IInvoiceLifecycleService.TransitionAsync"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Requested">The status the caller asked for.</param>
/// <param name="Current">The status read last (before the write for <see cref="InvoiceTransitionOutcome.Transitioned"/>); null when the
/// invoice was not found or its stored status is unknown.</param>
/// <param name="Reason">Set only for <see cref="InvoiceTransitionOutcome.Refused"/>; one of <see cref="InvoiceTransitionReasons"/>.</param>
public sealed record InvoiceTransitionResult(
    InvoiceTransitionOutcome Outcome,
    InvoiceLifecycleStatus Requested,
    InvoiceLifecycleStatus? Current,
    string? Reason)
{
    /// <summary>True for <see cref="InvoiceTransitionOutcome.Transitioned"/> and <see cref="InvoiceTransitionOutcome.AlreadyInTarget"/>.</summary>
    public bool Succeeded => Outcome != InvoiceTransitionOutcome.Refused;

    internal static InvoiceTransitionResult Moved(InvoiceLifecycleStatus from, InvoiceLifecycleStatus to) =>
        new(InvoiceTransitionOutcome.Transitioned, to, from, null);

    internal static InvoiceTransitionResult Already(InvoiceLifecycleStatus status) =>
        new(InvoiceTransitionOutcome.AlreadyInTarget, status, status, null);

    internal static InvoiceTransitionResult Refuse(InvoiceLifecycleStatus requested, InvoiceLifecycleStatus? current, string reason) =>
        new(InvoiceTransitionOutcome.Refused, requested, current, reason);
}

/// <summary>
/// The single server-side owner of invoice status (ADR-002, FR-06, I-6): the only code that writes <c>sprk_invoice</c>
/// <c>statecode</c> / <c>statuscode</c> after creation. Callers (approve, reject / return, send to AP, mark paid) own authorization and
/// Decision Records; this service owns only the status move.
/// </summary>
public interface IInvoiceLifecycleService
{
    /// <summary>
    /// Moves the invoice to <paramref name="target"/> if FR-06 allows it from its current status. Reads the status and row version, validates,
    /// then writes statecode + statuscode conditionally on that version; on a concurrency conflict it re-reads and re-validates, so a
    /// concurrent writer's move is never overwritten.
    /// </summary>
    Task<InvoiceTransitionResult> TransitionAsync(Guid invoiceId, InvoiceLifecycleStatus target, CancellationToken ct = default);
}

/// <summary>
/// <see cref="IInvoiceLifecycleService"/> over the app-only Dataverse Web API, using the conditional write
/// (<c>If-Match</c> on <c>versionnumber</c>) that <see cref="InvoiceReviewService"/> uses for document confirmation.
/// </summary>
/// <remarks>
/// FR-06 matrix: In Review to Approved, Rejected, Returned to Firm or Cancelled; Approved to Sent to AP or Cancelled; Sent to AP to Paid.
/// Anything else, including every move out of Paid, Rejected, Returned to Firm and Cancelled, is refused with
/// <see cref="InvoiceTransitionReasons.NotAllowed"/> and no write.
/// </remarks>
public sealed class InvoiceLifecycleService : IInvoiceLifecycleService
{
    private const string StateCode = "statecode";
    private const string StatusCode = "statuscode";

    private readonly IFieldMappingDataverseService _records;
    private readonly ILogger<InvoiceLifecycleService> _logger;

    public InvoiceLifecycleService(IFieldMappingDataverseService records, ILogger<InvoiceLifecycleService> logger)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>True when FR-06 allows moving from <paramref name="from"/> to <paramref name="to"/> (never for from == to).</summary>
    internal static bool IsAllowed(InvoiceLifecycleStatus from, InvoiceLifecycleStatus to) => (from, to) switch
    {
        (InvoiceLifecycleStatus.InReview, InvoiceLifecycleStatus.Approved) => true,
        (InvoiceLifecycleStatus.InReview, InvoiceLifecycleStatus.Rejected) => true,
        (InvoiceLifecycleStatus.InReview, InvoiceLifecycleStatus.ReturnedToFirm) => true,
        (InvoiceLifecycleStatus.InReview, InvoiceLifecycleStatus.Cancelled) => true,
        (InvoiceLifecycleStatus.Approved, InvoiceLifecycleStatus.SentToAp) => true,
        (InvoiceLifecycleStatus.Approved, InvoiceLifecycleStatus.Cancelled) => true,
        (InvoiceLifecycleStatus.SentToAp, InvoiceLifecycleStatus.Paid) => true,
        _ => false,
    };

    /// <inheritdoc />
    public async Task<InvoiceTransitionResult> TransitionAsync(Guid invoiceId, InvoiceLifecycleStatus target, CancellationToken ct = default)
    {
        // Validates the target is a defined status before any read.
        var targetStatusCode = InvoiceStatusCodes.StatusCodeOf(target);
        var targetStateCode = InvoiceStatusCodes.StateCodeOf(target);
        InvoiceLifecycleStatus? lastSeen = null;

        for (var attempt = 1; attempt <= InvoiceReviewService.MaxConditionalWriteAttempts; attempt++)
        {
            var row = await _records.RetrieveRecordFieldsAsync(
                InvoiceReviewService.InvoiceEntity, invoiceId,
                new[] { StatusCode, InvoiceReviewService.VersionNumber }, ct);

            if (row.Count == 0)
            {
                _logger.LogWarning("Invoice {InvoiceId} not found; no transition to {Target}", invoiceId, target);
                return InvoiceTransitionResult.Refuse(target, null, InvoiceTransitionReasons.NotFound);
            }

            if (!TryReadLong(row, StatusCode, out var rawStatus) || !InvoiceStatusCodes.TryFromStatusCode(rawStatus, out var current))
            {
                _logger.LogError(
                    "Invoice {InvoiceId} has a statuscode the lifecycle does not know; refusing the move to {Target}", invoiceId, target);
                return InvoiceTransitionResult.Refuse(target, null, InvoiceTransitionReasons.StatusUnknown);
            }

            lastSeen = current;

            if (current == target)
            {
                _logger.LogDebug("Invoice {InvoiceId} is already {Status}; nothing written", invoiceId, target);
                return InvoiceTransitionResult.Already(target);
            }

            if (!IsAllowed(current, target))
            {
                _logger.LogWarning(
                    "Invoice {InvoiceId}: transition {From} to {Target} is not allowed by the lifecycle; nothing written",
                    invoiceId, current, target);
                return InvoiceTransitionResult.Refuse(target, current, InvoiceTransitionReasons.NotAllowed);
            }

            // Every Dataverse row has a version. Without one the write cannot be conditional, so refuse rather than overwrite blind.
            if (!TryReadLong(row, InvoiceReviewService.VersionNumber, out var version))
            {
                throw new InvalidOperationException(
                    $"The invoice's {InvoiceReviewService.VersionNumber} could not be read, so the status cannot be changed safely. Nothing was saved.");
            }

            var fields = new Dictionary<string, object?>
            {
                [StatusCode] = targetStatusCode,
                [StateCode] = targetStateCode,
            };

            try
            {
                await _records.UpdateRecordFieldsIfUnchangedAsync(InvoiceReviewService.InvoiceEntity, invoiceId, fields, version, ct);
            }
            catch (DBConcurrencyException changed)
            {
                // Another writer touched the invoice (maybe moved its status): loop to re-read and re-validate.
                _logger.LogWarning(changed,
                    "Invoice {InvoiceId} changed before {Current} to {Target} was written (attempt {Attempt} of {MaxAttempts})",
                    invoiceId, current, target, attempt, InvoiceReviewService.MaxConditionalWriteAttempts);
                continue;
            }
            catch (KeyNotFoundException)
            {
                _logger.LogWarning("Invoice {InvoiceId} was deleted before its transition to {Target} was written", invoiceId, target);
                return InvoiceTransitionResult.Refuse(target, current, InvoiceTransitionReasons.NotFound);
            }

            _logger.LogInformation("Invoice {InvoiceId} moved {From} to {Target} (version {Version})", invoiceId, current, target, version);
            return InvoiceTransitionResult.Moved(current, target);
        }

        _logger.LogWarning(
            "Invoice {InvoiceId} kept changing; gave up moving it to {Target} after {MaxAttempts} attempts",
            invoiceId, target, InvoiceReviewService.MaxConditionalWriteAttempts);
        return InvoiceTransitionResult.Refuse(target, lastSeen, InvoiceTransitionReasons.ConcurrentChange);
    }

    private static bool TryReadLong(Dictionary<string, object?> row, string column, out long value)
    {
        value = 0;
        return row.TryGetValue(column, out var raw)
            && raw is not null
            && long.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
