using Microsoft.Xrm.Sdk;

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

    /// <summary>
    /// Task 095d-1a: a renumber was asked for before this environment's scope key is Active (tracking-scope phase P0 or P1). A submission with
    /// no firm exists only from P2, and before P2 nothing guards the target scope's number on update, so nothing was written. Retryable as a 503.
    /// </summary>
    public const string ScopeKeyInactive = "ebilling.tracking.scope_key_inactive";

    /// <summary>
    /// Task 095d-1a: the caller's apply requests and the submission update together exceed one Dataverse transaction
    /// (<c>InvoiceSubmissionPersister.MaxRequestsPerTransaction</c>); nothing was sent. Not retryable: the caller refuses before the apply
    /// (<see cref="TrackingNumberAllocator.FitsRenumberTransaction"/>), so reaching this is a caller bug caught fail-closed.
    /// </summary>
    public const string TooManyRequests = "ebilling.tracking.too_many_requests";

    /// <summary>
    /// Task 095d-1a (review F4): a renumber was asked for a submission that is not a no-firm submission in the <c>none</c> scope and is not already
    /// renumbered into the requested firm's scope: it has another firm, or its scope is another firm's (or is not <c>none</c>). A renumber moves a
    /// no-firm submission into a firm's scope and never a firm's submission to another firm, so nothing was written. Not retryable: the state
    /// does not change by waiting; the caller answers it as a refusal.
    /// </summary>
    public const string NotUnnumbered = "ebilling.tracking.not_unnumbered";

    /// <summary>
    /// Task 095d-1a (K4; owner decision 2026-11-04, "new column, never reuse"): a renumber was asked for in an environment that does not have
    /// <c>sprk_invoicesubmission.sprk_previoussequence</c> yet. The renumber records there the number a submission gives up in the <c>none</c>
    /// scope, so without the column it could let that number be issued again; nothing was sent. Not retryable until
    /// <c>scripts/ebilling/Deploy-EbillingSubmissionPreviousSequenceColumn.ps1</c> has run (<c>-Verify</c> exits 0): the caller answers it as a 503.
    /// It is a different code from <see cref="ScopeKeyInactive"/> because the remedy is a different script.
    /// </summary>
    public const string SchemaMissing = "ebilling.tracking.schema_missing";
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

/// <summary>What a renumber call did (task 095d-1a).</summary>
public enum RenumberOutcome
{
    /// <summary>The one transaction committed: the submission holds the firm, its scope and the firm's next number, and every caller request
    /// in <see cref="RenumberApply.Requests"/> was applied with it.</summary>
    Renumbered,

    /// <summary>
    /// The submission, read before anything was sent, was already moved into <c>firmId</c>'s scope and holds a number there (review F4): a
    /// repeat at the version the committing renumber left, after its reply was lost or a step after it failed. "Moved" means the row carries
    /// <c>sprk_previoussequence</c>, which only a renumber writes; a native row of that firm (created in its scope) is refused as
    /// <see cref="TrackingNumberReasons.NotUnnumbered"/> instead. <see cref="RenumberResult.TrackingNumber"/>
    /// and <see cref="RenumberResult.Sequence"/> are the held number; <b>nothing was sent</b>, so the caller's builder was not called and none of
    /// its requests ran in this call (the commit that moved the submission applied the caller's requests of that call, in the same transaction).
    /// It is a separate outcome from <see cref="Renumbered"/>, as <see cref="TrackingNumberOutcome.AlreadyCreated"/> is from
    /// <see cref="TrackingNumberOutcome.Created"/>, so a caller that must not treat "I applied it now" like "it was applied" has to choose: the
    /// caller (095d-1b) finishes the steps after the apply for both and never repeats the apply.
    /// <para><b>It does not say which decision renumbered the row (review K3).</b> The same firm can reach the row through a different request
    /// (another decision, another reason, another set of requests) than the one that committed. 1b must compare the row's STORED decision, not its
    /// own request, before it finishes the record, the close, the link or a re-run, and treat a different stored decision as a conflict, never as
    /// its own.</para>
    /// </summary>
    AlreadyRenumbered,

    /// <summary>
    /// A row-version condition failed: the submission is gone or is not at <c>rowVersion</c> when it is read first, or a condition in the
    /// transaction failed (the submission's, or a caller request's on its own row): the row changed since the caller read it, so nothing was
    /// written. Never retried as a race; the caller re-reads and decides ("already applied for this firm", or 409).
    /// </summary>
    Conflict,

    /// <summary>Nothing was written; <see cref="RenumberResult.Reason"/> is a stable code from <see cref="TrackingNumberReasons"/>.</summary>
    Refused,
}

/// <summary>The number a renumber attempt is about to write, handed to the caller's builder so its own requests can name it.</summary>
/// <param name="TrackingNumber">The candidate <c>{firm invoice number}.{n}</c> in the target firm's scope.</param>
/// <param name="Sequence">The candidate n.</param>
public sealed record RenumberCandidate(string TrackingNumber, int Sequence);

/// <summary>
/// The caller's part of the renumber transaction for one <see cref="RenumberCandidate"/> (task 095d-1a). Built again on every attempt, so every
/// value that names the number (the findings <c>trackingNumber</c>, the line and approval names, the stored decision) names the number that
/// attempt writes.
/// </summary>
/// <param name="SubmissionFields">
/// The submission's other columns, written in the allocator's own update of the submission (one row is not updated twice in one transaction
/// under its row version): the findings JSON with the stored decision, for one. Null for none. It must not name a column this service owns.
/// </param>
/// <param name="Requests">
/// The rest of the apply, sent in this order after the submission update: the invoice, the lines, the approval and the file renames, each
/// conditioned on its own row version where the caller needs it. Only <see cref="Microsoft.Xrm.Sdk.Messages.UpdateRequest"/> and
/// <see cref="Microsoft.Xrm.Sdk.Messages.CreateRequest"/> are accepted (review K3), and none may target <c>sprk_invoicesubmission</c>. At most
/// <see cref="TrackingNumberAllocator.MaxRenumberCallerRequests"/>.
/// </param>
public sealed record RenumberApply(IReadOnlyDictionary<string, object>? SubmissionFields, IReadOnlyList<OrganizationRequest> Requests);

/// <summary>The result of <see cref="ITrackingNumberAllocator.RenumberSubmissionAsync"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="TrackingNumber">The number the submission now holds; set only for <see cref="RenumberOutcome.Renumbered"/> and <see cref="RenumberOutcome.AlreadyRenumbered"/>.</param>
/// <param name="Sequence">Its n; set only for <see cref="RenumberOutcome.Renumbered"/> and <see cref="RenumberOutcome.AlreadyRenumbered"/>.</param>
/// <param name="Reason">Set only for <see cref="RenumberOutcome.Refused"/>; one of <see cref="TrackingNumberReasons"/>.</param>
public sealed record RenumberResult(RenumberOutcome Outcome, string? TrackingNumber, int? Sequence, string? Reason)
{
    internal static RenumberResult Renumbered(string trackingNumber, int sequence) => new(RenumberOutcome.Renumbered, trackingNumber, sequence, null);

    internal static RenumberResult AlreadyRenumbered(string trackingNumber, int sequence) =>
        new(RenumberOutcome.AlreadyRenumbered, trackingNumber, sequence, null);

    internal static RenumberResult Conflict() => new(RenumberOutcome.Conflict, null, null, null);

    internal static RenumberResult Refused(string reason) => new(RenumberOutcome.Refused, null, null, reason);
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
    /// Task 095d-1a (K4): in the <c>none</c> scope n is also above every <c>sprk_previoussequence</c> a row with this firm invoice number
    /// holds, so a number a renumber moved into a firm's scope is never issued again. An environment without that column keeps creating (the
    /// scope-only read, a warning logged once): its renumber is refused, so no number has left the scope yet.
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

    /// <summary>
    /// 095d pre-P2 safety (uac-r2): whether <c>sprk_invoicesubmission.sprk_trackingscope</c> exists in this environment, that is, whether the phase is
    /// <see cref="TrackingScopePhase.P1ScopeColumn"/> or later. A reader that selects the column must ask first: at
    /// <see cref="TrackingScopePhase.P0NoScopeColumn"/> a query that names it faults whole. The same process-wide, forward-only phase cache as
    /// <see cref="NumbersSubmissionsWithoutFirmAsync"/> (so the two answers always agree); never throws for an unreadable phase (that reads as the
    /// cached one, and P0 is "no column").
    /// </summary>
    Task<bool> ScopeColumnExistsAsync(CancellationToken ct = default);

    /// <summary>
    /// Task 095d-1a (D-37): moves the existing submission <paramref name="submissionId"/>, numbered with no firm, into <paramref name="firmId"/>'s
    /// scope with that firm's next <c>{firm invoice number}.{n}</c>, in ONE transaction that also carries the caller's apply
    /// (<paramref name="buildApply"/>). The submission update is the transaction's first request, conditioned on <paramref name="rowVersion"/>
    /// (<c>IfRowVersionMatches</c>); it writes the firm, the scope, the sequence and the tracking number, and leaves the firm invoice number as it is.
    /// </summary>
    /// <param name="submissionId">The submission to renumber.</param>
    /// <param name="rowVersion">The submission's row version from the caller's read: the version its refusals were checked against.</param>
    /// <param name="firmId">The firm whose scope the submission moves into; never empty.</param>
    /// <param name="firmInvoiceNumber">The submission's firm invoice number from the same read (trimmed here, as at create). It must be the
    /// number the row holds, compared exactly after trimming.</param>
    /// <param name="buildApply">
    /// Called once per attempt with the number that attempt writes; returns the caller's part of the transaction. A lost race calls it again
    /// with the next number, so it builds from its inputs and the candidate only, with no side effect.
    /// </param>
    /// <remarks>
    /// <para><b>Race.</b> n is the highest sequence held in the firm's scope for the firm invoice number, plus one. The scope key on
    /// (<c>sprk_trackingnumber</c>, <c>sprk_trackingscope</c>), enforced by Dataverse on update, decides who wins. The race is the key duplicate
    /// (<c>0x80060892</c>) on the submission update (index 0, or no index), or <c>0x80040237</c> at index 0; a key fault on a caller request
    /// propagates. A lost race faults the whole
    /// transaction, so none of it lands; the allocator re-reads the next n, rebuilds the apply and retries, within
    /// <see cref="TrackingNumberAllocator.MaxAttempts"/>, then answers <see cref="RenumberOutcome.Refused"/> with
    /// <see cref="TrackingNumberReasons.Unavailable"/>. A number held by an undone attempt (no firm invoice number, so the sequence read cannot
    /// see it) is skipped, as at create, within <see cref="TrackingNumberAllocator.MaxHiddenSkips"/>.</para>
    /// <para><b>Precondition (review F4).</b> Before any number is read, the submission is read by id and decided on its state, in this order,
    /// writing and sending nothing: a missing row, or a row version that is not <paramref name="rowVersion"/>, is
    /// <see cref="RenumberOutcome.Conflict"/>; a <paramref name="firmInvoiceNumber"/> that is not the row's is an <see cref="ArgumentException"/>;
    /// a row already moved into <paramref name="firmId"/>'s scope (that firm, that scope, a sequence held) is
    /// <see cref="RenumberOutcome.AlreadyRenumbered"/> with the held number, the builder never called; a row that is not a no-firm row in the
    /// <c>none</c> scope is <see cref="RenumberOutcome.Refused"/> with <see cref="TrackingNumberReasons.NotUnnumbered"/>. Only a no-firm row in
    /// the <c>none</c> scope, at the caller's version, goes on to the transaction, whose <c>IfRowVersionMatches</c> then pins exactly that state.</para>
    /// <para><b>Row version.</b> A row-version mismatch anywhere in the transaction is <see cref="RenumberOutcome.Conflict"/>, never a retry: the
    /// caller re-reads. A repeat with the same, now stale, version is a Conflict (it never allocates a second number); a repeat at the version
    /// the commit left is <see cref="RenumberOutcome.AlreadyRenumbered"/> (nor does that).</para>
    /// <para><b>Previous sequence (K4).</b> The submission update also writes <c>sprk_previoussequence</c>, the sequence the row held in the
    /// <c>none</c> scope, so the <c>none</c> scope never issues that number again. In an environment without the column the renumber is
    /// <see cref="RenumberOutcome.Refused"/> with <see cref="TrackingNumberReasons.SchemaMissing"/> (found by the precondition read, before
    /// anything is sent).</para>
    /// <para><b>Phase and ceiling.</b> Only at <see cref="TrackingScopePhase.P2ScopeKeyActive"/>, else
    /// <see cref="TrackingNumberReasons.ScopeKeyInactive"/>. More caller requests than
    /// <see cref="TrackingNumberAllocator.MaxRenumberCallerRequests"/> is <see cref="TrackingNumberReasons.TooManyRequests"/>. Both write nothing.
    /// Any other fault, an unknown outcome (a timeout) included, propagates on the first attempt: the caller's re-read decides.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">An id is empty, the row version or the firm invoice number is blank or is not the submission's own,
    /// or the apply names a column this service owns, targets a submission, or holds a request that is not an
    /// <see cref="Microsoft.Xrm.Sdk.Messages.UpdateRequest"/> or <see cref="Microsoft.Xrm.Sdk.Messages.CreateRequest"/>. The builder is null
    /// (<see cref="ArgumentNullException"/>). Nothing was written.</exception>
    Task<RenumberResult> RenumberSubmissionAsync(
        Guid submissionId,
        string rowVersion,
        Guid firmId,
        string firmInvoiceNumber,
        Func<RenumberCandidate, RenumberApply> buildApply,
        CancellationToken ct = default);
}
