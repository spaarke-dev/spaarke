using Microsoft.Xrm.Sdk;
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

    internal const string SubmissionEntity = "sprk_invoicesubmission";
    internal const string SubmissionIdColumn = "sprk_invoicesubmissionid";
    internal const string FirmColumn = "sprk_vendororg";
    internal const string FirmEntity = "sprk_organization";
    internal const string FirmInvoiceNumberColumn = "sprk_firminvoicenumber";
    internal const string SequenceColumn = "sprk_submissionsequence";
    internal const string TrackingNumberColumn = "sprk_trackingnumber";

    /// <summary>The columns this service writes; a caller's <c>fields</c> must not name them.</summary>
    private static readonly string[] OwnedColumns =
        [SubmissionIdColumn, FirmColumn, FirmInvoiceNumberColumn, SequenceColumn, TrackingNumberColumn];

    private static readonly string[] ExistingRowColumns = [FirmColumn, FirmInvoiceNumberColumn, SequenceColumn, TrackingNumberColumn];

    private readonly IGenericEntityService _entities;
    private readonly ILogger<TrackingNumberAllocator> _logger;

    public TrackingNumberAllocator(IGenericEntityService entities, ILogger<TrackingNumberAllocator> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TrackingNumberResult> CreateSubmissionAsync(
        Guid submissionId,
        Guid firmId,
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
            throw new ArgumentException("A firm id is required: the sequence is per firm.", nameof(firmId));
        }

        var number = NormalizeFirmInvoiceNumber(firmInvoiceNumber);
        RejectOwnedColumns(fields);

        if (await FindExistingAsync(submissionId, ct).ConfigureAwait(false) is { } already)
        {
            return Reconcile(already, submissionId, firmId, number);
        }

        // ebilling-r1 task 094 (EB-D-167): a number may be held by a row the sequence read cannot see - an attempt whose commit was
        // undone has its firm invoice number cleared (SubmissionCommitGate) but keeps its tracking number under the key. Such a number
        // is skipped (never re-read as free), and a skip is not a lost race: it has its own bound.
        var floor = 0;
        var skips = 0;
        for (var attempt = 1; ;)
        {
            var next = Math.Max(await ReadHighestSequenceAsync(firmId, number, ct).ConfigureAwait(false) + 1, floor + 1);
            var trackingNumber = Format(number, next);

            try
            {
                await _entities.CreateAsync(BuildRow(submissionId, firmId, number, next, trackingNumber, fields), ct).ConfigureAwait(false);
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
                if (await IsHeldByAnUnnumberedRowAsync(firmId, trackingNumber, ct).ConfigureAwait(false))
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

    /// <summary>
    /// ebilling-r1 task 094 (EB-D-167): true when the firm's row holding <paramref name="trackingNumber"/> has no firm invoice number, so
    /// the sequence read cannot see it: an attempt whose commit was undone. A row that still has its number (a concurrent winner) or no
    /// row at all (deleted in between) is a lost race. A fault propagates.
    /// </summary>
    private async Task<bool> IsHeldByAnUnnumberedRowAsync(Guid firmId, string trackingNumber, CancellationToken ct)
    {
        var query = new QueryExpression(SubmissionEntity) { ColumnSet = new ColumnSet(FirmInvoiceNumberColumn), TopCount = 1 };
        query.Criteria.AddCondition(FirmColumn, ConditionOperator.Equal, firmId);
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
    private static bool IsPrimaryKeyDuplicate(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.ServiceModel.FaultException<OrganizationServiceFault> fault && fault.Detail?.ErrorCode == unchecked((int)0x80040237))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>{firm invoice number}.{n}</c>: n a positive integer without padding (D-37).</summary>
    private static string Format(string firmInvoiceNumber, int sequence) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{firmInvoiceNumber}.{sequence}");

    private static Entity BuildRow(
        Guid submissionId, Guid firmId, string number, int sequence, string trackingNumber, IReadOnlyDictionary<string, object>? fields)
    {
        var row = new Entity(SubmissionEntity, submissionId);
        if (fields is not null)
        {
            foreach (var (column, value) in fields)
            {
                row[column] = value;
            }
        }

        row[FirmColumn] = new EntityReference(FirmEntity, firmId);
        row[FirmInvoiceNumberColumn] = number;
        row[SequenceColumn] = sequence;
        row[TrackingNumberColumn] = trackingNumber;
        return row;
    }

    /// <summary>
    /// The highest sequence held for the firm and firm invoice number, over rows in every state (an inactive, closed submission still owns its
    /// number); 0 when none. Dataverse compares strings without regard to case, as the alternate key does.
    /// </summary>
    private async Task<int> ReadHighestSequenceAsync(Guid firmId, string number, CancellationToken ct)
    {
        var query = new QueryExpression(SubmissionEntity)
        {
            ColumnSet = new ColumnSet(SequenceColumn),
            TopCount = 1,
        };
        query.Criteria.AddCondition(FirmColumn, ConditionOperator.Equal, firmId);
        query.Criteria.AddCondition(FirmInvoiceNumberColumn, ConditionOperator.Equal, number);
        query.Criteria.AddCondition(SequenceColumn, ConditionOperator.NotNull);
        query.AddOrder(SequenceColumn, OrderType.Descending);

        var found = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return found.Entities.Count == 0 ? 0 : found.Entities[0].GetAttributeValue<int?>(SequenceColumn) ?? 0;
    }

    /// <summary>The submission with this id as it stands, or null. A query (not a retrieve), so "no such row" is an empty answer and not a fault.</summary>
    private async Task<Entity?> FindExistingAsync(Guid submissionId, CancellationToken ct)
    {
        var query = new QueryExpression(SubmissionEntity)
        {
            ColumnSet = new ColumnSet(ExistingRowColumns),
            TopCount = 1,
        };
        query.Criteria.AddCondition(SubmissionIdColumn, ConditionOperator.Equal, submissionId);

        var found = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return found.Entities.Count == 0 ? null : found.Entities[0];
    }

    /// <summary>
    /// A submission with the caller's id exists: it is the answer to a redelivered create when it holds the same firm's number for the same firm invoice
    /// number; anything else is a caller bug, and is not repaired (nothing is renumbered, nothing is overwritten).
    /// </summary>
    private TrackingNumberResult Reconcile(Entity existing, Guid submissionId, Guid firmId, string number)
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
