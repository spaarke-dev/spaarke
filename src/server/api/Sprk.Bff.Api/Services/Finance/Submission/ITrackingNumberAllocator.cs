namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>What an allocation call did.</summary>
public enum TrackingNumberOutcome
{
    /// <summary>The submission was given the next number for its firm and firm invoice number; the row was written.</summary>
    Allocated,

    /// <summary>
    /// The submission already held a number for that firm invoice number; nothing was written and the existing number is returned.
    /// A job redelivery, or a retry after a timeout that had in fact written the row, ends here.
    /// </summary>
    AlreadyAllocated,

    /// <summary>Nothing was written; <see cref="TrackingNumberResult.Reason"/> is a stable code from <see cref="TrackingNumberReasons"/>.</summary>
    Refused,
}

/// <summary>Stable reason codes of a refused allocation. Callers map them to their own responses; the strings are a contract.</summary>
public static class TrackingNumberReasons
{
    /// <summary>
    /// Every attempt lost the race for the next number (or the number is held by a row the sequence cannot see), so none was written.
    /// The caller may try again later.
    /// </summary>
    public const string Unavailable = "ebilling.tracking.unavailable";
}

/// <summary>The result of <see cref="ITrackingNumberAllocator.AllocateAsync"/>.</summary>
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
    /// <summary>True for <see cref="TrackingNumberOutcome.Allocated"/> and <see cref="TrackingNumberOutcome.AlreadyAllocated"/>.</summary>
    public bool Succeeded => Outcome != TrackingNumberOutcome.Refused;

    internal static TrackingNumberResult Allocated(string trackingNumber, int sequence) =>
        new(TrackingNumberOutcome.Allocated, trackingNumber, sequence, null);

    internal static TrackingNumberResult AlreadyAllocated(string trackingNumber, int sequence) =>
        new(TrackingNumberOutcome.AlreadyAllocated, trackingNumber, sequence, null);

    internal static TrackingNumberResult Refused(string reason) =>
        new(TrackingNumberOutcome.Refused, null, null, reason);
}

/// <summary>
/// The single server-side owner of a submission's tracking number (ebilling-r1 FR-10, D-37, ADR-002): the only code that writes
/// <c>sprk_invoicesubmission.sprk_trackingnumber</c> and <c>sprk_submissionsequence</c> to a real number. Callers (the submission operation,
/// task 044, when the firm invoice number is typed; the submission job, task 049, when it is only read from the PDF) own which firm invoice
/// number to use; this service owns only the numbering.
/// </summary>
public interface ITrackingNumberAllocator
{
    /// <summary>
    /// Gives the existing submission <paramref name="submissionId"/> the next tracking number <c>{firm invoice number}.{n}</c> for
    /// <paramref name="firmId"/>: n is 1 for the first submission of that firm invoice number, then 2, 3 and so on, per firm and never across firms.
    /// Writes <c>sprk_firminvoicenumber</c>, <c>sprk_submissionsequence</c> and <c>sprk_trackingnumber</c> in one update.
    /// </summary>
    /// <remarks>
    /// Uniqueness comes from the Dataverse alternate key on (<c>sprk_vendororg</c>, <c>sprk_trackingnumber</c>), not from the read that picks n:
    /// two parallel callers can pick the same n, one write wins, the other is refused whole by the key and re-reads. At most
    /// <see cref="TrackingNumberAllocator.MaxAttempts"/> attempts, then <see cref="TrackingNumberOutcome.Refused"/> with
    /// <see cref="TrackingNumberReasons.Unavailable"/> and nothing written. Any other fault propagates on the first attempt.
    /// A submission that already holds a number for the same firm invoice number is returned unchanged
    /// (<see cref="TrackingNumberOutcome.AlreadyAllocated"/>), so the call is safe to repeat.
    /// </remarks>
    /// <exception cref="ArgumentException">An id is empty, or <paramref name="firmInvoiceNumber"/> is blank or longer than
    /// <see cref="TrackingNumberAllocator.MaxFirmInvoiceNumberLength"/> after trimming. Nothing was written.</exception>
    /// <exception cref="InvalidOperationException">The submission belongs to a different firm than <paramref name="firmId"/> (or to none), or
    /// already holds a number for a different firm invoice number. A caller bug; nothing was written.</exception>
    Task<TrackingNumberResult> AllocateAsync(Guid submissionId, Guid firmId, string firmInvoiceNumber, CancellationToken ct = default);
}
