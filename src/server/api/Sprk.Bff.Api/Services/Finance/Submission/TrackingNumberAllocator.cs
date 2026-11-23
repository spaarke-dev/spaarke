using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// <see cref="ITrackingNumberAllocator"/> over the Dataverse SDK client: reads the highest <c>sprk_submissionsequence</c> held for the firm
/// and firm invoice number, creates the submission row holding the next one, and lets the alternate key on (<c>sprk_vendororg</c>,
/// <c>sprk_trackingnumber</c>) (task 020) decide who won a race.
/// </summary>
/// <remarks>
/// <para><b>Why this shape.</b> The platform autonumber cannot count per (firm, firm invoice number), so the number is computed here and the
/// key is the only thing that makes it unique. This follows <c>RecordCreationService.CreateNumberedAsync</c> (task 076): only the
/// alternate-key duplicate (<c>0x80060892</c>, <see cref="DataverseServiceClientImpl.IsAlternateKeyDuplicate"/>) is retried, because
/// Dataverse refuses such a write whole, so a retry cannot double-write. Every other fault propagates on the first attempt: a timeout, for
/// one, may have created the row, and the next call finds it by its id and returns it (<see cref="TrackingNumberOutcome.AlreadyCreated"/>)
/// instead of numbering a second row.</para>
/// <para><b>Create, not update (D-87).</b> <c>sprk_trackingnumber</c> is SystemRequired, so a submission cannot exist without a number; the row is
/// created with the number already on it, in the same write that carries the caller's other columns and the firm. The firm is written
/// by this service, so the firm the sequence was counted for is always the firm the key guards.</para>
/// <para><b>Idempotency by submission id.</b> The caller chooses the new row's id and passes the same id on every redelivery. A row with that id
/// already there is the answer: this call reads it first, and a create that learns of it only afterwards (two calls with one id racing) is refused
/// by the primary key (<c>0x80040237</c>, which Dataverse checks before the alternate key) and re-reads the row instead of retrying. Compared with a
/// separate idempotency-key column this needs no schema change, and 044's job idempotency key is already the submission id.</para>
/// <para><b>Attempt bound.</b> k parallel callers for one (firm, firm invoice number) need at most k attempts each: a caller loses an attempt
/// only to another caller's committed number, and there are k - 1 others. <see cref="MaxAttempts"/> = 10 therefore covers the ten-way
/// month-end case (NFR-07); measured live in dev with ten simultaneous callers, every one got a number. An eleventh contender can be refused.</para>
/// <para><b>Logging (ADR-015).</b> Submission id, attempt count and outcome at Information and above. The firm invoice number and the tracking
/// number built from it are business data and appear at Debug only. Each refused write is also logged as an error by
/// <c>DataverseServiceClientImpl.CreateAsync</c> itself, so a retried create leaves one such error per refusal in telemetry; the warning
/// below names it as a lost race.</para>
/// <para><b>Undone attempts (task 094, EB-D-167).</b> The undo of a commit clears the submission's firm invoice number but keeps its
/// tracking number, so the sequence read no longer sees it; the key refuses that number, and a number held by such a row is skipped
/// (<see cref="MaxHiddenSkips"/>), not counted as a lost race.</para>
/// <para><b>Known limit.</b> A numbered submission that is later deleted frees its n: the next submission of that invoice number takes it again.
/// Submissions are not deleted by any eBilling flow (the lifecycle closes them), so this is only a manual-cleanup effect.</para>
/// <para><b>Tracking scope (task 095c, option B).</b> A submission may have no firm (the internal from-email channel, mode New). The firm key
/// does not cover a row whose firm is null, so the sequence is kept per <i>scope</i>: <c>sprk_trackingscope</c> holds the firm id in D form,
/// or <see cref="NoFirmScope"/>, and the key <see cref="ScopeKeyName"/> on (<c>sprk_trackingnumber</c>, <c>sprk_trackingscope</c>) guards it
/// the way the firm key guards it today. What this service does follows the environment's <see cref="TrackingScopePhase"/>, read from
/// metadata (<see cref="TrackingScopePhaseProbe"/>) and cached per process (<see cref="TrackingScopePhaseCache"/>, forward-only, 5 minutes
/// until P2): P0 is today's behaviour; P1 also writes the scope on every row and still counts per firm; P2 counts per scope and numbers a
/// submission with no firm. Before P2 a submission with no firm is refused (<see cref="TrackingNumberReasons.FirmRequired"/>) and nothing is
/// written; but the submission id's existing row is looked up first and returned in any phase (a P2 instance may have created a no-firm row
/// whose reply was lost, and the redelivery can land on a stale one). A P0 instance whose cached phase is stale after the schema script raised
/// the scope column to SystemRequired is refused by the platform (<c>0x80040200</c>); it re-reads the phase once (callers refused together
/// share one read, <see cref="TrackingScopePhaseCache.GetAsync"/>) and retries in the new phase, or answers the retryable
/// <see cref="TrackingNumberReasons.Unavailable"/> - never a 500 and never a second number.</para>
/// <para><b>Never reuse a no-firm number (task 095d-1a, K4; owner decision 2026-11-04).</b> A renumber moves a submission out of the <c>none</c>
/// scope and overwrites its sequence, so without more the <c>none</c> scope would hold no trace of the n it gave up and the next no-firm intake of
/// the same firm invoice number would be numbered with it again. The renumber therefore writes the row's old <c>none</c>-scope sequence to
/// <see cref="PreviousSequenceColumn"/> in the same update. The <c>none</c> scope's next-number read is the highest of two bounded reads, in this
/// order: the sequences of the rows still in the <c>none</c> scope, then the previous sequences of the rows with this firm invoice number (a
/// renumber only ever leaves <c>none</c>, so they all refer to it). The order matters: a row that moves between the two reads was seen by the
/// first, and one that moved before the first is seen by the second. That order covers a renumber that lands between the two reads of one create;
/// it does not make a create atomic with its reads (known limit K2 below). The scope key decides a race only while the row that holds the number is
/// still in the scope; the read only raises the floor.
/// While the column does not exist the renumber is refused (<see cref="TrackingNumberReasons.SchemaMissing"/>, nothing sent), so no row can have
/// left <c>none</c> yet and the create falls back to the scope-only read, logging a warning once.</para>
/// <para><b>Known limit (K2, task 095d-1a re-check; the bound, not a guarantee).</b> Two creates C0 and C1 of the same no-firm number can both read
/// the highest as n-1. C0 commits <c>.n</c>. Before C1's write lands, C0's row completes intake, a firm is assigned and the renumber commits
/// (the row leaves the <c>none</c> scope, recording n in <see cref="PreviousSequenceColumn"/>). C1 then commits <c>.n</c> again: the scope key no
/// longer holds C0's number in <c>none</c>, and C1 read the previous sequences before the renumber wrote n. The window is the write latency of one
/// create, against a full intake plus a human step (assigning a firm) that the renumber needs, so it is not a path in practice; it is not closed
/// in code, and nothing here claims the interleaving is impossible.</para>
/// <para><b>Known limit (K4 residue).</b> The previous-sequence read finds rows by their firm invoice number. The undo of a commit
/// (<c>SubmissionCommitGate</c>) clears that number, so a renumbered row whose commit were undone afterwards would hide its previous sequence.
/// The undo runs only inside the intake's commit, before assign-firm can renumber the row, so this is not a path today.</para>
/// </remarks>
public sealed class TrackingNumberAllocator : ITrackingNumberAllocator
{
    /// <summary>
    /// Attempts before a lost race is a refusal. k parallel callers of one (firm, firm invoice number) need at most k attempts, so 10 gives
    /// every caller of a ten-way burst (NFR-07) a number; more than ten at once, or a number held by a row the sequence cannot see, is refused.
    /// </summary>
    public const int MaxAttempts = 10;

    /// <summary>
    /// ebilling-r1 task 094 (EB-D-167): the most numbers one call skips because an undone attempt holds them (its firm invoice number
    /// cleared, so the sequence read cannot see it). Above the attempts one document or email can make
    /// (<see cref="FromEmailSubmissionKey.MaxGeneration"/> generations), so a skip run never refuses a legitimate retry.
    /// </summary>
    public const int MaxHiddenSkips = 64;

    /// <summary>
    /// The longest firm invoice number the allocator accepts: <c>sprk_trackingnumber</c> is 100 characters and holds the number, a dot and an
    /// n of up to 10 digits (100 - 1 - 10). Callers validate against this at intake, so a long number is refused with a clear message there
    /// rather than as a Dataverse length fault here.
    /// </summary>
    public const int MaxFirmInvoiceNumberLength = 89;

    /// <summary>
    /// Task 095d-1a: the most requests a renumber's caller may add (<see cref="RenumberApply.Requests"/>): one Dataverse transaction
    /// (<see cref="InvoiceSubmissionPersister.MaxRequestsPerTransaction"/>, 1000) less the allocator's own submission update.
    /// </summary>
    public const int MaxRenumberCallerRequests = InvoiceSubmissionPersister.MaxRequestsPerTransaction - RenumberOwnRequests;

    /// <summary>Task 095d-1a: the requests a renumber adds itself: the submission update.</summary>
    private const int RenumberOwnRequests = 1;

    /// <summary>
    /// Task 095d-1a: whether an apply of <paramref name="callerRequestCount"/> requests (the invoice, approval, lines and file renames: everything
    /// but the submission update) fits the one renumber transaction. The caller asks it before the apply and refuses above it, writing nothing;
    /// <see cref="ITrackingNumberAllocator.RenumberSubmissionAsync"/> refuses the same count on its own
    /// (<see cref="TrackingNumberReasons.TooManyRequests"/>). The apply is never split.
    /// </summary>
    public static bool FitsRenumberTransaction(int callerRequestCount) =>
        callerRequestCount >= 0 && callerRequestCount <= MaxRenumberCallerRequests;

    internal const string SubmissionEntity = "sprk_invoicesubmission";
    internal const string SubmissionIdColumn = "sprk_invoicesubmissionid";
    internal const string FirmColumn = "sprk_vendororg";
    internal const string FirmEntity = "sprk_organization";
    internal const string FirmInvoiceNumberColumn = "sprk_firminvoicenumber";
    internal const string SequenceColumn = "sprk_submissionsequence";
    internal const string TrackingNumberColumn = "sprk_trackingnumber";

    /// <summary>Task 095c: the sequence's scope, the firm id in D form or <see cref="NoFirmScope"/>. Written only by this service.</summary>
    internal const string ScopeColumn = "sprk_trackingscope";

    /// <summary>Task 095c: the scope of a submission with no firm.</summary>
    internal const string NoFirmScope = "none";

    /// <summary>Task 095c: the alternate key on (<c>sprk_trackingnumber</c>, <c>sprk_trackingscope</c>) relied on from P2.</summary>
    internal const string ScopeKeyName = "sprk_invoicesubmission_scope_trackingnumber";

    /// <summary>
    /// Task 095d-1a (K4; owner decision 2026-11-04, "new column, never reuse"): <c>sprk_invoicesubmission.sprk_previoussequence</c>, a whole number
    /// (Integer, 1 to 2147483647, not field-secured). The sequence a submission held in the <c>none</c> scope before a renumber moved it into a
    /// firm's scope. Written only by <see cref="RenumberSubmissionAsync"/>, in the submission update of the renumber transaction; read only by
    /// the <c>none</c> scope's next-number read, which counts it so a number that left the scope is not issued again. Created by
    /// <c>scripts/ebilling/Deploy-EbillingSubmissionPreviousSequenceColumn.ps1</c> (pinned by <c>SubmissionPreviousSequenceColumnAgreementTests</c>).
    /// </summary>
    internal const string PreviousSequenceColumn = "sprk_previoussequence";

    /// <summary>The columns this service writes; a caller's <c>fields</c> must not name them.</summary>
    private static readonly string[] OwnedColumns =
        [SubmissionIdColumn, FirmColumn, FirmInvoiceNumberColumn, SequenceColumn, TrackingNumberColumn, ScopeColumn, PreviousSequenceColumn];

    private static readonly string[] ExistingRowColumns = [FirmColumn, FirmInvoiceNumberColumn, SequenceColumn, TrackingNumberColumn];

    /// <summary>Task 095d-1a (F4): the platform's row version column, read the way the persister reads it (<c>InvoiceSubmissionPersister.SubmissionColumns</c>).</summary>
    private const string VersionNumberColumn = "versionnumber";

    /// <summary>Task 095d-1a (F4): what the renumber's precondition read needs: the row version, the scope and the firm, the sequence and the
    /// numbers the row holds. Read only at P2, where the scope column exists. It also names <see cref="PreviousSequenceColumn"/>: a column that
    /// does not exist yet faults this read, before anything is sent, and that fault is the renumber's schema check (K4).</summary>
    private static readonly string[] RenumberRowColumns =
        [VersionNumberColumn, ScopeColumn, FirmColumn, FirmInvoiceNumberColumn, SequenceColumn, TrackingNumberColumn, PreviousSequenceColumn];

    /// <summary>Task 095d-1a (K4): "'{table}' entity doesn't contain attribute with Name = '{column}'", <c>0x80041103</c>: a query names a column the
    /// environment does not have. Measured on dev 2026-11-04 with a read-only fetch of <c>sprk_previoussequence</c> before it existed (HTTP 400).</summary>
    private const int AttributeDoesNotExistFault = unchecked((int)0x80041103);

    /// <summary>Task 095c: "Attribute 'x' cannot be NULL", <c>0x80040200</c>: a SystemRequired column was missing from the write (measured in
    /// dev, notes/022 and notes/027). Dataverse refuses the write whole.</summary>
    private const int RequiredColumnNullFault = unchecked((int)0x80040200);

    /// <summary>The alternate-key duplicate, <c>0x80060892</c> (the code <see cref="DataverseServiceClientImpl.IsAlternateKeyDuplicate"/> types on).</summary>
    private const int AlternateKeyDuplicateFault = unchecked((int)0x80060892);

    /// <summary>"Cannot insert duplicate key", <c>0x80040237</c>: a unique index refused the write (see <see cref="IsPrimaryKeyDuplicate"/>).</summary>
    private const int PrimaryKeyDuplicateFault = unchecked((int)0x80040237);

    private readonly IGenericEntityService _entities;
    private readonly Func<CancellationToken, Task<TrackingScopePhase?>> _readPhase;
    private readonly TrackingScopePhaseCache _phases;
    private readonly ILogger<TrackingNumberAllocator> _logger;

    public TrackingNumberAllocator(IGenericEntityService entities, IDataverseService dataverse, ILogger<TrackingNumberAllocator> logger)
        : this(
            entities,
            ReaderOver(dataverse ?? throw new ArgumentNullException(nameof(dataverse)), logger ?? throw new ArgumentNullException(nameof(logger))),
            TrackingScopePhaseCache.Shared,
            logger)
    {
    }

    /// <summary>Tests: a canned phase reader and a cache of their own (ADR-038 B1: the seam is the reader, not the transport).</summary>
    internal TrackingNumberAllocator(
        IGenericEntityService entities,
        Func<CancellationToken, Task<TrackingScopePhase?>> readPhase,
        TrackingScopePhaseCache phases,
        ILogger<TrackingNumberAllocator> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _readPhase = readPhase ?? throw new ArgumentNullException(nameof(readPhase));
        _phases = phases ?? throw new ArgumentNullException(nameof(phases));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static Func<CancellationToken, Task<TrackingScopePhase?>> ReaderOver(IDataverseService dataverse, ILogger logger) =>
        ct => TrackingScopePhaseProbe.ReadAsync(dataverse, logger, ct);

    /// <inheritdoc />
    public async Task<TrackingNumberResult> CreateSubmissionAsync(
        Guid submissionId,
        Guid? firmId,
        string firmInvoiceNumber,
        IReadOnlyDictionary<string, object>? fields = null,
        CancellationToken ct = default)
    {
        if (submissionId == Guid.Empty)
        {
            throw new ArgumentException("A submission id is required: it is the idempotency key of the create.", nameof(submissionId));
        }

        if (firmId == Guid.Empty)
        {
            // Task 095c: "no firm" is null, never the empty id (a caller that lost its firm must not be numbered as a no-firm submission).
            throw new ArgumentException("A firm id is a firm or null, never empty.", nameof(firmId));
        }

        var number = NormalizeFirmInvoiceNumber(firmInvoiceNumber);
        RejectOwnedColumns(fields);

        // The row this id already names is the answer whatever phase this instance holds: a P2 instance may have created a no-firm row whose
        // reply was lost, and the redelivery can land on a stale P1 instance, which must return that row, not refuse it.
        if (await FindExistingAsync(submissionId, ct).ConfigureAwait(false) is { } already)
        {
            return Reconcile(already, submissionId, firmId, number);
        }

        var phase = await _phases.GetAsync(_readPhase, force: false, ct).ConfigureAwait(false);
        if (firmId is null && phase != TrackingScopePhase.P2ScopeKeyActive)
        {
            // Task 095c: before the scope key is Active nothing guards a no-firm row's number (the firm key ignores a null firm).
            _logger.LogWarning(
                "{Code}: submission={SubmissionId} phase={Phase} - a submission with no firm is numbered only once the scope key is Active; nothing written",
                TrackingNumberReasons.FirmRequired, submissionId, phase);
            return TrackingNumberResult.Refused(TrackingNumberReasons.FirmRequired);
        }

        // ebilling-r1 task 094 (EB-D-167): a number may be held by a row the sequence read cannot see - an attempt whose commit was
        // undone has its firm invoice number cleared (SubmissionCommitGate) but keeps its tracking number under the key. Such a number
        // is skipped (never re-read as free), and a skip is not a lost race: it has its own bound.
        var floor = 0;
        var skips = 0;
        var reread = false;
        for (var attempt = 1; ;)
        {
            var next = Math.Max(await ReadHighestSequenceAsync(phase, firmId, number, ct).ConfigureAwait(false) + 1, floor + 1);
            var trackingNumber = Format(number, next);

            try
            {
                await _entities.CreateAsync(BuildRow(phase, submissionId, firmId, number, next, trackingNumber, fields), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (phase == TrackingScopePhase.P0NoScopeColumn && HasFaultCode(ex, RequiredColumnNullFault))
            {
                // Task 095c: this instance still holds P0, but the scope column exists and is SystemRequired (schema script step 5), so the
                // platform refused the row whole: nothing was written. Re-read the phase once and retry in it; a phase that still reads P0 is
                // the retryable 503, never a 500.
                var refusedAtRead = _phases.ReadsStarted;
                if (!reread)
                {
                    reread = true;
                    // One forced re-read at a time: callers refused together share the read that starts after the first of them (K2).
                    phase = await _phases.GetAsync(_readPhase, force: true, ct, refusedAtRead).ConfigureAwait(false);
                    if (phase != TrackingScopePhase.P0NoScopeColumn)
                    {
                        _logger.LogWarning(
                            "Submission {SubmissionId}: the cached tracking-scope phase was stale; re-read as {Phase}, retrying", submissionId, phase);
                        continue;
                    }
                }

                _logger.LogError(ex,
                    "{Code}: submission={SubmissionId} - a required column was refused while the tracking-scope phase reads P0; nothing written",
                    TrackingNumberReasons.Unavailable, submissionId);
                return TrackingNumberResult.Refused(TrackingNumberReasons.Unavailable);
            }
            catch (Exception ex) when (IsPrimaryKeyDuplicate(ex))
            {
                // This very submission was created between the read above and this write (an earlier call with the same id, still
                // running, or one whose reply was lost): it is the answer, not a competitor.
                if (await FindExistingAsync(submissionId, ct).ConfigureAwait(false) is { } created)
                {
                    return Reconcile(created, submissionId, firmId, number);
                }

                // The id is taken yet no row answers to it (deleted in between): not a race for a number, so not retried.
                throw;
            }
            catch (Exception ex) when (DataverseServiceClientImpl.IsAlternateKeyDuplicate(ex))
            {
                floor = next;
                if (await IsHeldByAnUnnumberedRowAsync(phase, firmId, trackingNumber, ct).ConfigureAwait(false))
                {
                    if (++skips > MaxHiddenSkips)
                    {
                        _logger.LogError(
                            "{Code}: submission={SubmissionId} skips={Skips} - every next number is held by an undone submission; nothing written",
                            TrackingNumberReasons.Unavailable, submissionId, skips);
                        return TrackingNumberResult.Refused(TrackingNumberReasons.Unavailable);
                    }

                    _logger.LogInformation(
                        "Submission {SubmissionId}: tracking sequence {Sequence} is held by an undone submission; skipped", submissionId, next);
                    continue;
                }

                if (attempt >= MaxAttempts)
                {
                    _logger.LogError(ex,
                        "{Code}: submission={SubmissionId} attempts={Attempts} — the next number was taken on every attempt; nothing written",
                        TrackingNumberReasons.Unavailable, submissionId, attempt);
                    return TrackingNumberResult.Refused(TrackingNumberReasons.Unavailable);
                }

                _logger.LogWarning(
                    "Submission {SubmissionId}: tracking sequence {Sequence} was taken by a concurrent submission (attempt {Attempt} of {Max}); " +
                    "re-reading and retrying",
                    submissionId, next, attempt, MaxAttempts);
                attempt++;
                continue;
            }

            _logger.LogInformation(
                "Submission {SubmissionId} created with tracking sequence {Sequence} on attempt {Attempt}", submissionId, next, attempt);
            _logger.LogDebug("Submission {SubmissionId} tracking number {TrackingNumber}", submissionId, trackingNumber);
            return TrackingNumberResult.Created(trackingNumber, next);
        }
    }

    /// <inheritdoc />
    public async Task<bool> NumbersSubmissionsWithoutFirmAsync(CancellationToken ct = default) =>
        await _phases.GetAsync(_readPhase, force: false, ct).ConfigureAwait(false) == TrackingScopePhase.P2ScopeKeyActive;

    /// <inheritdoc />
    public async Task<bool> ScopeColumnExistsAsync(CancellationToken ct = default) =>
        await _phases.GetAsync(_readPhase, force: false, ct).ConfigureAwait(false) != TrackingScopePhase.P0NoScopeColumn;

    /// <inheritdoc />
    /// <remarks>
    /// Task 095d-1a. The loop is <see cref="CreateSubmissionAsync"/>'s: re-read the highest sequence in the scope, write, and treat only the
    /// alternate-key duplicate as a lost race (Dataverse refuses the transaction whole, so a retry cannot double-write). It differs in three
    /// ways. The write is one transaction (the submission update first, then the caller's requests), so a lost race rolls back the caller's
    /// apply with it. A row-version mismatch is <see cref="RenumberOutcome.Conflict"/>, never a retry. And it runs only at P2, where the target
    /// scope's key exists.
    /// <para><b>Precondition read (review F4).</b> The submission's version is the idempotency guard of a renumber, the way its id is at create, but
    /// a version alone cannot tell a repeat that must finish from a request that must refuse: a caller that re-read after a <c>Conflict</c> holds the
    /// post-commit version, at which the conditional update would succeed and allocate a second number. So before the loop the row is read by id
    /// and its state decided (<see cref="DecideFromStateAsync"/>): a missing row or a stale version is <see cref="RenumberOutcome.Conflict"/>; a
    /// firm invoice number that is not the row's is an <see cref="ArgumentException"/>; a row already in this firm's scope holding a sequence is
    /// <see cref="RenumberOutcome.AlreadyRenumbered"/> (its held number, nothing sent, no second number); a row that is not a no-firm row in the
    /// <c>none</c> scope is refused (<see cref="TrackingNumberReasons.NotUnnumbered"/>). The update's <c>IfRowVersionMatches</c> then pins the
    /// state that was read at write time, so a change between the read and the write is the transaction's <c>Conflict</c>.</para>
    /// <para><b>Previous sequence (K4).</b> The same read gives the row's <c>none</c>-scope sequence, which the submission update writes to
    /// <see cref="PreviousSequenceColumn"/> (the sequence read at the version the update is conditioned on). The read names that column, so an
    /// environment that does not have it yet faults here, before anything is sent, and the renumber answers
    /// <see cref="RenumberOutcome.Refused"/> with <see cref="TrackingNumberReasons.SchemaMissing"/>.</para>
    /// <para><b>Lost race.</b> A key fault on the submission update (index 0, or no index) is a race, as is <c>0x80040237</c> at index 0: on that
    /// update the only unique indexes are the two keys (<see cref="IsLostRaceOnTheSubmissionUpdate"/>). Both are refused whole.</para>
    /// </remarks>
    public async Task<RenumberResult> RenumberSubmissionAsync(
        Guid submissionId,
        string rowVersion,
        Guid firmId,
        string firmInvoiceNumber,
        Func<RenumberCandidate, RenumberApply> buildApply,
        CancellationToken ct = default)
    {
        if (submissionId == Guid.Empty)
        {
            throw new ArgumentException("A submission id is required to renumber a submission.", nameof(submissionId));
        }

        if (firmId == Guid.Empty)
        {
            throw new ArgumentException("A renumber moves a submission into a firm's scope: the firm id is required.", nameof(firmId));
        }

        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            throw new ArgumentException("The submission's row version is required: the renumber is conditioned on it.", nameof(rowVersion));
        }

        ArgumentNullException.ThrowIfNull(buildApply);
        var number = NormalizeFirmInvoiceNumber(firmInvoiceNumber);

        var phase = await _phases.GetAsync(_readPhase, force: false, ct).ConfigureAwait(false);
        if (phase != TrackingScopePhase.P2ScopeKeyActive)
        {
            // A submission with no firm exists only from P2; before it nothing guards the target scope's number on update.
            _logger.LogWarning(
                "{Code}: submission={SubmissionId} phase={Phase} - a submission is renumbered only once the scope key is Active; nothing written",
                TrackingNumberReasons.ScopeKeyInactive, submissionId, phase);
            return RenumberResult.Refused(TrackingNumberReasons.ScopeKeyInactive);
        }

        var state = await DecideFromStateAsync(submissionId, rowVersion, firmId, number, ct).ConfigureAwait(false);
        if (state.Decided is { } decided)
        {
            return decided;
        }

        var floor = 0;
        var skips = 0;
        for (var attempt = 1; ;)
        {
            var next = Math.Max(await ReadHighestSequenceAsync(phase, firmId, number, ct).ConfigureAwait(false) + 1, floor + 1);
            var trackingNumber = Format(number, next);

            var apply = buildApply(new RenumberCandidate(trackingNumber, next))
                ?? throw new InvalidOperationException("The renumber's apply builder returned no apply.");
            RejectOwnedColumns(apply.SubmissionFields);
            RejectSubmissionTargets(apply.Requests);
            if (!FitsRenumberTransaction(apply.Requests.Count))
            {
                _logger.LogError(
                    "{Code}: submission={SubmissionId} requests={Count} max={Max} - the apply does not fit one transaction; nothing written",
                    TrackingNumberReasons.TooManyRequests, submissionId, apply.Requests.Count, MaxRenumberCallerRequests);
                return RenumberResult.Refused(TrackingNumberReasons.TooManyRequests);
            }

            var requests = new List<OrganizationRequest>(RenumberOwnRequests + apply.Requests.Count)
            {
                new UpdateRequest
                {
                    Target = BuildRenumberUpdate(submissionId, rowVersion, firmId, next, trackingNumber, state.PreviousSequence, apply.SubmissionFields),
                    ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
                },
            };
            requests.AddRange(apply.Requests);

            try
            {
                await _entities.ExecuteTransactionAsync(requests, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && DataverseServiceClientImpl.IsRowVersionMismatch(ex))
            {
                // Never a race: the submission (or a row the caller conditioned) changed since the caller's read. The caller re-reads.
                _logger.LogWarning(
                    "Submission {SubmissionId}: renumber refused by a row-version condition (request {Index} of {Count}); nothing written",
                    submissionId, (ex as DataverseTransactionException)?.FailedRequestIndex, requests.Count);
                return RenumberResult.Conflict();
            }
            catch (DataverseTransactionException ex) when (IsLostRaceOnTheSubmissionUpdate(ex))
            {
                // The scope key refused the submission update, so the whole transaction rolled back. A key fault on a caller request is not
                // a race for this number and propagates.
                floor = next;
                if (await IsHeldByAnUnnumberedRowAsync(phase, firmId, trackingNumber, ct).ConfigureAwait(false))
                {
                    if (++skips > MaxHiddenSkips)
                    {
                        _logger.LogError(
                            "{Code}: submission={SubmissionId} skips={Skips} - every next number is held by an undone submission; nothing written",
                            TrackingNumberReasons.Unavailable, submissionId, skips);
                        return RenumberResult.Refused(TrackingNumberReasons.Unavailable);
                    }

                    _logger.LogInformation(
                        "Submission {SubmissionId}: tracking sequence {Sequence} is held by an undone submission; skipped", submissionId, next);
                    continue;
                }

                if (attempt >= MaxAttempts)
                {
                    _logger.LogError(ex,
                        "{Code}: submission={SubmissionId} attempts={Attempts} - the next number was taken on every renumber attempt; nothing written",
                        TrackingNumberReasons.Unavailable, submissionId, attempt);
                    return RenumberResult.Refused(TrackingNumberReasons.Unavailable);
                }

                _logger.LogWarning(
                    "Submission {SubmissionId}: tracking sequence {Sequence} was taken by a concurrent submission (renumber attempt {Attempt} of {Max}); " +
                    "re-reading and retrying",
                    submissionId, next, attempt, MaxAttempts);
                attempt++;
                continue;
            }

            _logger.LogInformation(
                "Submission {SubmissionId} renumbered into its firm's scope with tracking sequence {Sequence} on attempt {Attempt} ({Count} requests)",
                submissionId, next, attempt, requests.Count);
            _logger.LogDebug("Submission {SubmissionId} tracking number {TrackingNumber}", submissionId, trackingNumber);
            return RenumberResult.Renumbered(trackingNumber, next);
        }
    }

    /// <summary>
    /// Task 095d-1a (review F4): reads the submission by id before the loop and decides from its state, or null to go on and renumber. The
    /// outcomes are in the order they are checked, and each writes nothing:
    /// <list type="number">
    /// <item>No such row, or a row version that is not <paramref name="rowVersion"/>: <see cref="RenumberOutcome.Conflict"/> (the row changed
    /// since the caller read it; the caller re-reads).</item>
    /// <item>A firm invoice number that is not the row's: <see cref="ArgumentException"/> (the caller's read was of another row's number, a bug;
    /// compared after trimming, exactly: the number is the row's own, read with its version).</item>
    /// <item>The row is already in <paramref name="firmId"/>'s scope, with that firm, holds a sequence, and carries <see cref="PreviousSequenceColumn"/>
    /// (only a renumber writes it, so it is what tells a renumbered row from one created in that firm's scope):
    /// <see cref="RenumberOutcome.AlreadyRenumbered"/> with the held number. This is a repeat after a commit whose reply was lost, at the version
    /// the commit left. A native row of that firm (a sequence, no previous sequence) falls through to the next outcome.</item>
    /// <item>Anything but a no-firm row in the <c>none</c> scope (another firm's row, this firm's row in some other scope, a row in another
    /// firm's scope, a native row of this firm that was never renumbered): <see cref="RenumberOutcome.Refused"/> with <see cref="TrackingNumberReasons.NotUnnumbered"/>. A renumber moves a
    /// no-firm submission into a firm's scope and nothing else: it never moves a firm's submission to another firm.</item>
    /// </list>
    /// Before all of these, a read that faults because <see cref="PreviousSequenceColumn"/> does not exist is
    /// <see cref="RenumberOutcome.Refused"/> with <see cref="TrackingNumberReasons.SchemaMissing"/> (K4). When it goes on, the state also carries
    /// the row's sequence, the value the renumber writes to <see cref="PreviousSequenceColumn"/>.
    /// </summary>
    private async Task<RenumberState> DecideFromStateAsync(
        Guid submissionId, string rowVersion, Guid firmId, string firmInvoiceNumber, CancellationToken ct)
    {
        Entity? existing;
        try
        {
            existing = await FindExistingAsync(submissionId, RenumberRowColumns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPreviousSequenceColumnMissing(ex))
        {
            // Nothing is sent, so nothing is written: a renumber without the column could not record the number it gives up (K4).
            _logger.LogWarning(ex,
                "{Code}: submission={SubmissionId} - {Column} does not exist in this environment yet (run Deploy-EbillingSubmissionPreviousSequenceColumn.ps1); nothing written",
                TrackingNumberReasons.SchemaMissing, submissionId, PreviousSequenceColumn);
            return RenumberState.Stop(RenumberResult.Refused(TrackingNumberReasons.SchemaMissing));
        }

        if (existing is null)
        {
            _logger.LogWarning("Submission {SubmissionId}: renumber refused, the row no longer exists; nothing written", submissionId);
            return RenumberState.Stop(RenumberResult.Conflict());
        }

        var heldVersion = existing.GetAttributeValue<long?>(VersionNumberColumn)?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? existing.RowVersion
            ?? throw new InvalidOperationException($"Submission {submissionId} was read without a row version, so it was not renumbered safely.");
        if (!string.Equals(heldVersion, rowVersion.Trim(), StringComparison.Ordinal))
        {
            _logger.LogWarning("Submission {SubmissionId}: renumber refused, the row changed since the caller read it; nothing written", submissionId);
            return RenumberState.Stop(RenumberResult.Conflict());
        }

        if (!string.Equals(existing.GetAttributeValue<string>(FirmInvoiceNumberColumn)?.Trim(), firmInvoiceNumber, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The firm invoice number is not the one submission {submissionId} holds, so it was not renumbered.", nameof(firmInvoiceNumber));
        }

        var firm = existing.GetAttributeValue<EntityReference>(FirmColumn)?.Id;
        var scope = existing.GetAttributeValue<string>(ScopeColumn);
        if (firm == firmId && string.Equals(scope, ScopeOf(firmId), StringComparison.OrdinalIgnoreCase)
            && existing.GetAttributeValue<int?>(SequenceColumn) is { } heldSequence
            && existing.GetAttributeValue<int?>(PreviousSequenceColumn) is not null)
        {
            var heldNumber = existing.GetAttributeValue<string>(TrackingNumberColumn) ?? Format(firmInvoiceNumber, heldSequence);
            _logger.LogInformation(
                "Submission {SubmissionId} is already renumbered into its firm's scope with tracking sequence {Sequence}; nothing written", submissionId, heldSequence);
            _logger.LogDebug("Submission {SubmissionId} tracking number {TrackingNumber}", submissionId, heldNumber);
            return RenumberState.Stop(RenumberResult.AlreadyRenumbered(heldNumber, heldSequence));
        }

        if (firm is not null || !string.Equals(scope, NoFirmScope, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "{Code}: submission={SubmissionId} - it is not a no-firm submission in the none scope; nothing written",
                TrackingNumberReasons.NotUnnumbered, submissionId);
            return RenumberState.Stop(RenumberResult.Refused(TrackingNumberReasons.NotUnnumbered));
        }

        // The row is a no-firm submission in the none scope at the caller's version: the sequence it holds there is the one it gives up (K4).
        // None (a row the allocator never numbered) writes no previous sequence: there is no number to keep, and the column's minimum is 1.
        return RenumberState.Go(existing.GetAttributeValue<int?>(SequenceColumn));
    }

    /// <summary>Task 095d-1a (K4): what the renumber's precondition read decided: stop with a result, or go on with the sequence the row holds now.</summary>
    private readonly record struct RenumberState(RenumberResult? Decided, int? PreviousSequence)
    {
        public static RenumberState Stop(RenumberResult result) => new(result, null);

        public static RenumberState Go(int? previousSequence) => new(null, previousSequence);
    }

    /// <summary>
    /// Task 095d-1a (review K1, K2): whether <paramref name="ex"/> says the scope key (or the firm key, until it is retired) refused the
    /// submission update, so the whole transaction rolled back and the number was taken: a retry cannot double-write.
    /// <list type="bullet">
    /// <item>The alternate-key duplicate (<c>0x80060892</c>) at index 0 or with no index (<b>known limit K1:</b> a fault with no index may be a
    /// concurrent request's, GitHub #971 - the retry is then a wasted attempt that skips a number, never a double write, because the retry's
    /// update is conditioned on the row version, so a first transaction that did commit makes it a Conflict; 045's create shares the exposure).</item>
    /// <item><c>0x80040237</c> ("Cannot insert duplicate key") at index 0: on a submission update the primary key cannot be inserted twice, so
    /// the only unique indexes left are the two keys, and a platform that words their violation this way is a lost race too. At another
    /// index, or with none, it propagates: only the submission update is known to fault on these keys.</item>
    /// </list>
    /// An unknown outcome (no fault at all) is never a race.
    /// </summary>
    private static bool IsLostRaceOnTheSubmissionUpdate(DataverseTransactionException ex) =>
        !ex.OutcomeUnknown
        && ((ex.FailedRequestIndex is null or 0 && IsKeyDuplicate(ex))
            || (ex.FailedRequestIndex == 0 && HasFaultCodeInChain(ex, PrimaryKeyDuplicateFault)));

    /// <summary>
    /// Task 095d-1a: the renumber's submission update. The firm, its scope, the sequence and the number, at the caller's row version; the firm
    /// invoice number is not written (it does not change). K4: also <see cref="PreviousSequenceColumn"/>, the <c>none</c>-scope sequence the row
    /// gives up, in the same update (so it lands with the move or not at all).
    /// </summary>
    private static Entity BuildRenumberUpdate(
        Guid submissionId, string rowVersion, Guid firmId, int sequence, string trackingNumber, int? previousSequence,
        IReadOnlyDictionary<string, object>? fields)
    {
        var row = new Entity(SubmissionEntity, submissionId) { RowVersion = rowVersion };
        if (fields is not null)
        {
            foreach (var (column, value) in fields)
            {
                row[column] = value;
            }
        }

        row[FirmColumn] = new EntityReference(FirmEntity, firmId);
        row[ScopeColumn] = ScopeOf(firmId);
        row[SequenceColumn] = sequence;
        row[TrackingNumberColumn] = trackingNumber;
        if (previousSequence is { } previous)
        {
            row[PreviousSequenceColumn] = previous;
        }

        return row;
    }

    /// <summary>
    /// Task 095d-1a (review K3): the caller's requests are an allowlist, not a denylist: an <see cref="UpdateRequest"/> or a
    /// <see cref="CreateRequest"/> whose target table is not <c>sprk_invoicesubmission</c>. Any other request type (an association, a state
    /// change, a delete, an upsert) could reach a submission, or lose the row version, without naming the table in a place a denylist reads,
    /// so it is refused with the rest. The submission's own columns go in <see cref="RenumberApply.SubmissionFields"/> (a second update of
    /// the row would fail its row version), and this service is the only writer of any submission's number.
    /// </summary>
    private static void RejectSubmissionTargets(IReadOnlyList<OrganizationRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        for (var index = 0; index < requests.Count; index++)
        {
            var table = requests[index] switch
            {
                UpdateRequest update => update.Target?.LogicalName,
                CreateRequest create => create.Target?.LogicalName,
                null => throw new ArgumentException($"The renumber's request at index {index} is null.", nameof(requests)),
                var other => throw new ArgumentException(
                    $"The renumber's request at index {index} is a {other.GetType().Name}; only an UpdateRequest or a CreateRequest on another table is accepted.",
                    nameof(requests)),
            };
            if (string.IsNullOrWhiteSpace(table))
            {
                throw new ArgumentException($"The renumber's request at index {index} names no target table.", nameof(requests));
            }

            if (string.Equals(table, SubmissionEntity, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"The renumber's request at index {index} targets {SubmissionEntity}; the submission's columns go in the apply's submission fields.",
                    nameof(requests));
            }
        }
    }

    /// <summary>
    /// Task 095d-1a: the alternate-key duplicate (<c>0x80060892</c>) on a transaction. Inside an <c>ExecuteTransactionFault</c> the request's own
    /// fault can be the inner fault, which <see cref="DataverseServiceClientImpl.IsAlternateKeyDuplicate"/> does not walk, so the fault chain is
    /// walked here too.
    /// </summary>
    private static bool IsKeyDuplicate(Exception ex) =>
        HasFaultCodeInChain(ex, AlternateKeyDuplicateFault) || DataverseServiceClientImpl.IsAlternateKeyDuplicate(ex);

    /// <summary>
    /// Task 095d-1a: true when <paramref name="ex"/> or an inner exception is an <see cref="OrganizationServiceFault"/> whose own code, or whose
    /// inner fault's code at any depth, is exactly <paramref name="code"/>. Unlike <see cref="HasFaultCode"/> it walks the inner faults: in an
    /// <c>ExecuteTransactionFault</c> the faulted request's own code can be the inner fault.
    /// </summary>
    private static bool HasFaultCodeInChain(Exception ex, int code)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.ServiceModel.FaultException<OrganizationServiceFault> faultException)
            {
                for (var fault = faultException.Detail; fault is not null; fault = fault.InnerFault)
                {
                    if (fault.ErrorCode == code)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// ebilling-r1 task 094 (EB-D-167): true when the firm's (from P2: the scope's) row holding <paramref name="trackingNumber"/> has no firm
    /// invoice number, so the sequence read cannot see it: an attempt whose commit was undone. A row that still has its number (a concurrent
    /// winner) or no row at all (deleted in between) is a lost race. A fault propagates.
    /// </summary>
    private async Task<bool> IsHeldByAnUnnumberedRowAsync(TrackingScopePhase phase, Guid? firmId, string trackingNumber, CancellationToken ct)
    {
        var query = new QueryExpression(SubmissionEntity) { ColumnSet = new ColumnSet(FirmInvoiceNumberColumn), TopCount = 1 };
        AddSequenceScope(query, phase, firmId);
        query.Criteria.AddCondition(TrackingNumberColumn, ConditionOperator.Equal, trackingNumber);
        var found = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return found.Entities.Count == 1 && string.IsNullOrWhiteSpace(found.Entities[0].GetAttributeValue<string>(FirmInvoiceNumberColumn));
    }

    /// <summary>The firm invoice number as submitted, trimmed (D-37). Throws for a blank number or one the tracking-number column cannot hold.</summary>
    private static string NormalizeFirmInvoiceNumber(string? firmInvoiceNumber)
    {
        var number = firmInvoiceNumber?.Trim();
        if (string.IsNullOrEmpty(number))
        {
            throw new ArgumentException("A firm invoice number is required to number a submission.", nameof(firmInvoiceNumber));
        }

        if (number.Length > MaxFirmInvoiceNumberLength)
        {
            throw new ArgumentException(
                $"A firm invoice number can be at most {MaxFirmInvoiceNumberLength} characters to fit its tracking number.", nameof(firmInvoiceNumber));
        }

        return number;
    }

    /// <summary>The single owner of these columns (ADR-002): a caller-supplied value would let a number be set without the sequence behind it.</summary>
    private static void RejectOwnedColumns(IReadOnlyDictionary<string, object>? fields)
    {
        if (fields is null)
        {
            return;
        }

        foreach (var column in fields.Keys)
        {
            if (OwnedColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"The column '{column}' is written by the tracking number allocator and cannot be supplied.", nameof(fields));
            }
        }
    }

    /// <summary>
    /// True when <paramref name="ex"/> (or an inner exception) is Dataverse's "Cannot insert duplicate key" fault, <c>0x80040237</c>: a create
    /// whose primary key is taken. Measured in dev 2026-10-16 through <c>DataverseServiceClientImpl.CreateAsync</c>: raised for a repeated id whether
    /// or not the tracking number is also taken (the primary key is checked before the alternate key), and not matched by
    /// <see cref="DataverseServiceClientImpl.IsAlternateKeyDuplicate"/>. The typed fault code only, as an exact match.
    /// </summary>
    private static bool IsPrimaryKeyDuplicate(Exception ex) => HasFaultCode(ex, PrimaryKeyDuplicateFault);

    /// <summary>True when <paramref name="ex"/> or an inner exception is an <see cref="OrganizationServiceFault"/> with exactly this code.</summary>
    private static bool HasFaultCode(Exception ex, int code)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.ServiceModel.FaultException<OrganizationServiceFault> fault && fault.Detail?.ErrorCode == code)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Task 095c: the rows one sequence counts. Before P2 the firm's rows (the firm key guards them; a no-firm submission never gets this
    /// far); from P2 the scope's rows (the scope key guards them; a firm's rows carry the firm's id as their scope).
    /// </summary>
    private static void AddSequenceScope(QueryExpression query, TrackingScopePhase phase, Guid? firmId)
    {
        if (phase == TrackingScopePhase.P2ScopeKeyActive)
        {
            query.Criteria.AddCondition(ScopeColumn, ConditionOperator.Equal, ScopeOf(firmId));
        }
        else
        {
            query.Criteria.AddCondition(FirmColumn, ConditionOperator.Equal, firmId!.Value);
        }
    }

    /// <summary>Task 095c: the scope a submission is numbered in: its firm's id in D form, or <see cref="NoFirmScope"/>.</summary>
    internal static string ScopeOf(Guid? firmId) =>
        firmId is { } firm ? firm.ToString("D", System.Globalization.CultureInfo.InvariantCulture) : NoFirmScope;

    /// <summary><c>{firm invoice number}.{n}</c>: n a positive integer without padding (D-37).</summary>
    private static string Format(string firmInvoiceNumber, int sequence) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{firmInvoiceNumber}.{sequence}");

    private static Entity BuildRow(
        TrackingScopePhase phase, Guid submissionId, Guid? firmId, string number, int sequence, string trackingNumber,
        IReadOnlyDictionary<string, object>? fields)
    {
        var row = new Entity(SubmissionEntity, submissionId);
        if (fields is not null)
        {
            foreach (var (column, value) in fields)
            {
                row[column] = value;
            }
        }

        if (firmId is { } firm)
        {
            row[FirmColumn] = new EntityReference(FirmEntity, firm);
        }

        if (phase != TrackingScopePhase.P0NoScopeColumn)
        {
            // Task 095c: from P1 on every row, so the scope key can be built over every row once the backfill has run.
            row[ScopeColumn] = ScopeOf(firmId);
        }

        row[FirmInvoiceNumberColumn] = number;
        row[SequenceColumn] = sequence;
        row[TrackingNumberColumn] = trackingNumber;
        return row;
    }

    /// <summary>
    /// The highest sequence held for the firm (from P2: the scope) and firm invoice number, over rows in every state (an inactive, closed
    /// submission still owns its number); 0 when none. Dataverse compares strings without regard to case, as the alternate key does.
    /// <para><b>The <c>none</c> scope (K4).</b> From P2, a no-firm number also counts the sequences that rows with this firm invoice number gave
    /// up when a renumber moved them into a firm's scope (<see cref="PreviousSequenceColumn"/>): the result is the larger of the two reads, the
    /// scope's own sequences FIRST and the previous sequences second. A row moves between them in one update, so a row that moves between the two
    /// reads was seen by the first, and one that moved before the first is seen by the second. A firm's scope never counts the column: it refers to
    /// the <c>none</c> scope only. The scope key still decides every race; this read only raises the floor.</para>
    /// </summary>
    private async Task<int> ReadHighestSequenceAsync(TrackingScopePhase phase, Guid? firmId, string number, CancellationToken ct)
    {
        var query = new QueryExpression(SubmissionEntity)
        {
            ColumnSet = new ColumnSet(SequenceColumn),
            TopCount = 1,
        };
        AddSequenceScope(query, phase, firmId);
        query.Criteria.AddCondition(FirmInvoiceNumberColumn, ConditionOperator.Equal, number);
        query.Criteria.AddCondition(SequenceColumn, ConditionOperator.NotNull);
        query.AddOrder(SequenceColumn, OrderType.Descending);

        var found = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var highest = found.Entities.Count == 0 ? 0 : found.Entities[0].GetAttributeValue<int?>(SequenceColumn) ?? 0;

        return phase == TrackingScopePhase.P2ScopeKeyActive && firmId is null
            ? Math.Max(highest, await ReadHighestPreviousSequenceAsync(number, ct).ConfigureAwait(false))
            : highest;
    }

    /// <summary>
    /// Task 095d-1a (K4): the highest <see cref="PreviousSequenceColumn"/> held by a row with this firm invoice number, in any scope; 0 when none.
    /// A column that does not exist yet (the schema script has not run) answers 0 and logs a warning once: while it is missing the renumber is
    /// refused (<see cref="TrackingNumberReasons.SchemaMissing"/>), so no row has left the <c>none</c> scope through one and the scope-only read
    /// is complete. Any other fault propagates, as the sequence read's does.
    /// </summary>
    private async Task<int> ReadHighestPreviousSequenceAsync(string number, CancellationToken ct)
    {
        var query = new QueryExpression(SubmissionEntity)
        {
            ColumnSet = new ColumnSet(PreviousSequenceColumn),
            TopCount = 1,
        };
        query.Criteria.AddCondition(FirmInvoiceNumberColumn, ConditionOperator.Equal, number);
        query.Criteria.AddCondition(PreviousSequenceColumn, ConditionOperator.NotNull);
        query.AddOrder(PreviousSequenceColumn, OrderType.Descending);

        try
        {
            var found = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            return found.Entities.Count == 0 ? 0 : found.Entities[0].GetAttributeValue<int?>(PreviousSequenceColumn) ?? 0;
        }
        catch (Exception ex) when (IsPreviousSequenceColumnMissing(ex))
        {
            if (_phases.TryMarkPreviousSequenceMissing())
            {
                _logger.LogWarning(ex,
                    "{Code}: {Column} does not exist in this environment yet (run Deploy-EbillingSubmissionPreviousSequenceColumn.ps1); a no-firm " +
                    "number is counted from the none scope alone, which is complete until a renumber runs (it is refused without the column). Logged once",
                    TrackingNumberReasons.SchemaMissing, PreviousSequenceColumn);
            }

            return 0;
        }
    }

    /// <summary>
    /// Task 095d-1a (K4): whether <paramref name="ex"/> says <see cref="PreviousSequenceColumn"/> does not exist in this environment: Dataverse's
    /// "'{table}' entity doesn't contain attribute with Name = '{column}'" (<c>0x80041103</c>, measured on dev). Matched by the typed fault code
    /// (the fault and its inner faults) or, where the client surfaces only text, by the message; either way the message must name THIS column, so a
    /// missing column of some other query is never read as this one. Walks the exception chain: the client may wrap the fault.
    /// </summary>
    internal static bool IsPreviousSequenceColumnMissing(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.ServiceModel.FaultException<OrganizationServiceFault> faultException)
            {
                for (var fault = faultException.Detail; fault is not null; fault = fault.InnerFault)
                {
                    if (fault.ErrorCode == AttributeDoesNotExistFault && NamesThePreviousSequenceColumn(fault.Message))
                    {
                        return true;
                    }
                }
            }

            if (NamesThePreviousSequenceColumn(e.Message)
                && (e.Message.Contains("contain attribute with Name", StringComparison.OrdinalIgnoreCase)
                    || e.Message.Contains("0x80041103", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NamesThePreviousSequenceColumn(string? message) =>
        message is { Length: > 0 } && message.Contains(PreviousSequenceColumn, StringComparison.OrdinalIgnoreCase);

    /// <summary>The submission with this id as it stands, or null. A query (not a retrieve), so "no such row" is an empty answer and not a fault.</summary>
    private async Task<Entity?> FindExistingAsync(Guid submissionId, CancellationToken ct) =>
        await FindExistingAsync(submissionId, ExistingRowColumns, ct).ConfigureAwait(false);

    private async Task<Entity?> FindExistingAsync(Guid submissionId, string[] columns, CancellationToken ct)
    {
        var query = new QueryExpression(SubmissionEntity)
        {
            ColumnSet = new ColumnSet(columns),
            TopCount = 1,
        };
        query.Criteria.AddCondition(SubmissionIdColumn, ConditionOperator.Equal, submissionId);

        var found = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return found.Entities.Count == 0 ? null : found.Entities[0];
    }

    /// <summary>
    /// A submission with the caller's id exists: it is the answer to a redelivered create when it holds the same firm's number (no firm for no
    /// firm) for the same firm invoice number; anything else is a caller bug, and is not repaired (nothing is renumbered, nothing is overwritten).
    /// </summary>
    private TrackingNumberResult Reconcile(Entity existing, Guid submissionId, Guid? firmId, string number)
    {
        if (existing.GetAttributeValue<EntityReference>(FirmColumn)?.Id != firmId)
        {
            throw new InvalidOperationException(
                $"Submission {submissionId} already exists for a different firm than the one the tracking number was requested for, so it was not numbered.");
        }

        if (existing.GetAttributeValue<int?>(SequenceColumn) is not { } held)
        {
            throw new InvalidOperationException(
                $"Submission {submissionId} already exists but was not numbered by the tracking number allocator, so it was not renumbered.");
        }

        var heldNumber = existing.GetAttributeValue<string>(FirmInvoiceNumberColumn)?.Trim();
        if (!string.Equals(heldNumber, number, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Submission {submissionId} already has a tracking number for a different firm invoice number, so it was not renumbered.");
        }

        var heldTracking = existing.GetAttributeValue<string>(TrackingNumberColumn) ?? Format(number, held);
        _logger.LogInformation(
            "Submission {SubmissionId} already exists with tracking sequence {Sequence}; nothing written", submissionId, held);
        _logger.LogDebug("Submission {SubmissionId} tracking number {TrackingNumber}", submissionId, heldTracking);
        return TrackingNumberResult.AlreadyCreated(heldTracking, held);
    }
}
