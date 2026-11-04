namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>What a create call did.</summary>
public enum TrackingNumberOutcome
{
    /// <summary>The submission row was created holding the next number for its firm and firm invoice number.</summary>
    Created,

    /// <summary>
    /// A submission with that id already exists and holds a number for that firm invoice number; nothing was written and the existing number is
    /// returned. A redelivered request, or a retry after a timeout that had in fact created the row, ends here.
    /// </summary>
    AlreadyCreated,

    /// <summary>Nothing was written; <see cref="TrackingNumberResult.Reason"/> is a stable code from <see cref="TrackingNumberReasons"/>.</summary>
    Refused,
}

/// <summary>Stable reason codes of a refused create. Callers map them to their own responses; the strings are a contract.</summary>
public static class TrackingNumberReasons
{
    /// <summary>
    /// Every attempt lost the race for the next number (or the number is held by a row the sequence cannot see), so no row was created.
    /// The caller may try again later.
    /// </summary>
    public const string Unavailable = "ebilling.tracking.unavailable";

    /// <summary>
    /// Task 095c: a submission with no firm was asked for before this environment's scope key is Active (tracking-scope phase P0 or P1), so
    /// nothing guards its number and no row was created. Not retryable as such: the intake answers it as "a firm is required" until the
    /// schema script has run (notes/095c-from-email-firm-optional.md, "Notice for uac-r2").
    /// </summary>
    public const string FirmRequired = "ebilling.tracking.firm_required";
}

/// <summary>The result of <see cref="ITrackingNumberAllocator.CreateSubmissionAsync"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="TrackingNumber">The submission's <c>{firm invoice number}.{n}</c>; null when refused.</param>
/// <param name="Sequence">The <c>n</c> of <paramref name="TrackingNumber"/>; null when refused.</param>
/// <param name="Reason">Set only for <see cref="TrackingNumberOutcome.Refused"/>; one of <see cref="TrackingNumberReasons"/>.</param>
public sealed record TrackingNumberResult(
    TrackingNumberOutcome Outcome,
    string? TrackingNumber,
    int? Sequence,
    string? Reason)
{
    /// <summary>True for <see cref="TrackingNumberOutcome.Created"/> and <see cref="TrackingNumberOutcome.AlreadyCreated"/>.</summary>
    public bool Succeeded => Outcome != TrackingNumberOutcome.Refused;

    internal static TrackingNumberResult Created(string trackingNumber, int sequence) =>
        new(TrackingNumberOutcome.Created, trackingNumber, sequence, null);

    internal static TrackingNumberResult AlreadyCreated(string trackingNumber, int sequence) =>
        new(TrackingNumberOutcome.AlreadyCreated, trackingNumber, sequence, null);

    internal static TrackingNumberResult Refused(string reason) =>
        new(TrackingNumberOutcome.Refused, null, null, reason);
}

/// <summary>
/// The single server-side owner of a submission's tracking number (ebilling-r1 FR-10, D-37, D-87, ADR-002) and the only creator of
/// <c>sprk_invoicesubmission</c> rows: a submission is created with its <c>{firm invoice number}.{n}</c> already set, so the number is never
/// blank. The submission operation (task 044) calls it with the firm invoice number the submitter typed; the submission job (task 049) never
/// allocates.
/// </summary>
public interface ITrackingNumberAllocator
{
    /// <summary>
    /// Creates the submission <paramref name="submissionId"/> for <paramref name="firmId"/> holding the next tracking number
    /// <c>{firm invoice number}.{n}</c>: n is 1 for the first submission of that firm invoice number, then 2, 3 and so on, per firm and never across
    /// firms. The row is created in one write that carries the number, <paramref name="fields"/> and the firm.
    /// </summary>
    /// <param name="submissionId">
    /// The new row's id, chosen by the caller (a new Guid per submission, also the job's idempotency key). It is the idempotency key of this
    /// call: if a row with this id already exists, that row's number is returned and nothing is written, so a redelivered create never mints
    /// a second number. A second submission of the same firm invoice number is a new submission with a new id (D-37).
    /// </param>
    /// <param name="fields">
    /// Every other column of the new row (name, status, channel, document, core-record fields, owner...); null for none. It must not contain
    /// the columns this service owns (the id, <c>sprk_vendororg</c>, <c>sprk_firminvoicenumber</c>, <c>sprk_submissionsequence</c>,
    /// <c>sprk_trackingnumber</c>). On an <see cref="TrackingNumberOutcome.AlreadyCreated"/> result the existing row stands; these values are not applied.
    /// </param>
    /// <remarks>
    /// Task 095c: <paramref name="firmId"/> null numbers a submission with no firm, in the <c>none</c> scope; it is refused with
    /// <see cref="TrackingNumberReasons.FirmRequired"/> (nothing written) until the environment's scope key is Active
    /// (<see cref="TrackingScopePhase"/>). <see cref="Guid.Empty"/> is never "no firm" and throws.
    /// Uniqueness comes from the Dataverse alternate key on (<c>sprk_vendororg</c>, <c>sprk_trackingnumber</c>), and from the scope key on
    /// (<c>sprk_trackingnumber</c>, <c>sprk_trackingscope</c>) once it is Active, not from the read that picks n:
    /// two parallel callers can pick the same n, one create wins, the other is refused whole by the key and recomputes. At most
    /// <see cref="TrackingNumberAllocator.MaxAttempts"/> attempts, then <see cref="TrackingNumberOutcome.Refused"/> with
    /// <see cref="TrackingNumberReasons.Unavailable"/> and no row created. Any other fault propagates on the first attempt.
    /// The allocator never refuses because the firm invoice number was seen before: a repeated number is a Duplicate-rule finding with a
    /// configured consequence (D-88), and each arrival gets its own n.
    /// </remarks>
    /// <exception cref="ArgumentException">An id is empty, <paramref name="firmInvoiceNumber"/> is blank or longer than
    /// <see cref="TrackingNumberAllocator.MaxFirmInvoiceNumberLength"/> after trimming, or <paramref name="fields"/> names a column this service
    /// owns. Nothing was written.</exception>
    /// <exception cref="InvalidOperationException">A row with <paramref name="submissionId"/> already exists but belongs to a different firm,
    /// holds a different firm invoice number, or was not numbered by this service. A caller bug; nothing was written.</exception>
    Task<TrackingNumberResult> CreateSubmissionAsync(
        Guid submissionId,
        Guid? firmId,
        string firmInvoiceNumber,
        IReadOnlyDictionary<string, object>? fields = null,
        CancellationToken ct = default);

    /// <summary>
    /// Task 095c-2: whether <see cref="CreateSubmissionAsync"/> would number a submission with no firm now, that is, whether this environment's
    /// scope key is Active (<see cref="TrackingScopePhase.P2ScopeKeyActive"/>). The intake asks it before it saves or files anything, so a
    /// request with no firm in P0 or P1 is refused with nothing written (notes/095c-from-email-firm-optional.md section 8.2). The same
    /// process-wide, forward-only phase cache the create reads; never throws for an unreadable phase (that reads as the cached one, and P0 is
    /// "no"). The create still refuses on its own (<see cref="TrackingNumberReasons.FirmRequired"/>), so this answer is never what guards a number.
    /// </summary>
    Task<bool> NumbersSubmissionsWithoutFirmAsync(CancellationToken ct = default);
}
