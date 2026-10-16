using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// <see cref="ITrackingNumberAllocator"/> over the Dataverse SDK client: reads the highest <c>sprk_submissionsequence</c> held for the firm
/// and firm invoice number, writes the next one, and lets the alternate key on (<c>sprk_vendororg</c>, <c>sprk_trackingnumber</c>) (task 020)
/// decide who won a race.
/// </summary>
/// <remarks>
/// <para><b>Why this shape.</b> The platform autonumber cannot count per (firm, firm invoice number), so the number is computed here and the
/// key is the only thing that makes it unique. This follows <c>RecordCreationService.CreateNumberedAsync</c> (task 076): only the
/// alternate-key duplicate (<c>0x80060892</c>, <see cref="DataverseServiceClientImpl.IsAlternateKeyDuplicate"/>) is retried, because
/// Dataverse refuses such a write whole, so a retry cannot double-write. Every other fault propagates on the first attempt: a timeout, for
/// one, may have written the row, and the next call finds it by its sequence and returns it (<see cref="TrackingNumberOutcome.AlreadyAllocated"/>)
/// instead of numbering it twice. Measured in dev 2026-10-16: the key refuses a duplicate on UPDATE as well as on create, and it ignores case.</para>
/// <para><b>One update, three columns.</b> <c>sprk_firminvoicenumber</c> is written with the sequence and the tracking number so the three can
/// never disagree: the highest-sequence read below filters on <c>sprk_firminvoicenumber</c>, the key guards <c>sprk_trackingnumber</c>.</para>
/// <para><b>Firm check.</b> The key only guards rows that carry a firm. The submission's own <c>sprk_vendororg</c> must therefore equal the
/// <c>firmId</c> the caller names, or the sequence would be counted for one firm and guarded for another; a mismatch throws and writes nothing.</para>
/// <para><b>Logging (ADR-015).</b> Submission id, attempt count and outcome at Information and above. The firm invoice number and the tracking
/// number built from it are business data and appear at Debug only. Each refused write is also logged as an error by
/// <c>DataverseServiceClientImpl.UpdateAsync</c> itself, so a retried allocation leaves one such error per refusal in telemetry; the warning
/// below names it as a lost race.</para>
/// <para><b>Known limit.</b> A numbered submission that is later deleted frees its n: the next submission of that invoice number takes it again.
/// Submissions are not deleted by any eBilling flow (the lifecycle closes them), so this is only a manual-cleanup effect.</para>
/// </remarks>
public sealed class TrackingNumberAllocator : ITrackingNumberAllocator
{
    /// <summary>Attempts before a lost race is a refusal. One collision is a rare coincidence; five in a row is a pile-up or a held number.</summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// The longest firm invoice number the allocator accepts: <c>sprk_trackingnumber</c> is 100 characters and holds the number, a dot and an
    /// n of up to 10 digits (100 - 1 - 10). Callers validate against this at intake, so a long number is refused with a clear message there
    /// rather than as a Dataverse length fault here.
    /// </summary>
    public const int MaxFirmInvoiceNumberLength = 89;

    internal const string SubmissionEntity = "sprk_invoicesubmission";
    internal const string SubmissionIdColumn = "sprk_invoicesubmissionid";
    internal const string FirmColumn = "sprk_vendororg";
    internal const string FirmInvoiceNumberColumn = "sprk_firminvoicenumber";
    internal const string SequenceColumn = "sprk_submissionsequence";
    internal const string TrackingNumberColumn = "sprk_trackingnumber";

    private static readonly string[] RowColumns = [FirmColumn, FirmInvoiceNumberColumn, SequenceColumn, TrackingNumberColumn];

    private readonly IGenericEntityService _entities;
    private readonly ILogger<TrackingNumberAllocator> _logger;

    public TrackingNumberAllocator(IGenericEntityService entities, ILogger<TrackingNumberAllocator> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TrackingNumberResult> AllocateAsync(
        Guid submissionId, Guid firmId, string firmInvoiceNumber, CancellationToken ct = default)
    {
        if (submissionId == Guid.Empty)
        {
            throw new ArgumentException("A submission id is required.", nameof(submissionId));
        }

        if (firmId == Guid.Empty)
        {
            throw new ArgumentException("A firm id is required: the sequence is per firm.", nameof(firmId));
        }

        var number = NormalizeFirmInvoiceNumber(firmInvoiceNumber);

        var row = await _entities.RetrieveAsync(SubmissionEntity, submissionId, RowColumns, ct).ConfigureAwait(false);

        var rowFirmId = row.GetAttributeValue<EntityReference>(FirmColumn)?.Id;
        if (rowFirmId != firmId)
        {
            throw new InvalidOperationException(
                $"Submission {submissionId} does not belong to the firm the tracking number was requested for (or has no firm), so it was not numbered.");
        }

        var existingSequence = row.GetAttributeValue<int?>(SequenceColumn);
        if (existingSequence is { } held)
        {
            var heldNumber = row.GetAttributeValue<string>(FirmInvoiceNumberColumn)?.Trim();
            if (!string.Equals(heldNumber, number, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Submission {submissionId} already has a tracking number for a different firm invoice number, so it was not renumbered.");
            }

            var heldTracking = row.GetAttributeValue<string>(TrackingNumberColumn) ?? Format(number, held);
            _logger.LogInformation(
                "Submission {SubmissionId} already has tracking sequence {Sequence}; nothing written", submissionId, held);
            _logger.LogDebug("Submission {SubmissionId} tracking number {TrackingNumber}", submissionId, heldTracking);
            return TrackingNumberResult.AlreadyAllocated(heldTracking, held);
        }

        for (var attempt = 1; ; attempt++)
        {
            var next = await ReadHighestSequenceAsync(submissionId, firmId, number, ct).ConfigureAwait(false) + 1;
            var trackingNumber = Format(number, next);

            try
            {
                await _entities.UpdateAsync(
                    SubmissionEntity,
                    submissionId,
                    new Dictionary<string, object>
                    {
                        [FirmInvoiceNumberColumn] = number,
                        [SequenceColumn] = next,
                        [TrackingNumberColumn] = trackingNumber,
                    },
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (DataverseServiceClientImpl.IsAlternateKeyDuplicate(ex))
            {
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
                continue;
            }

            _logger.LogInformation(
                "Submission {SubmissionId} given tracking sequence {Sequence} on attempt {Attempt}", submissionId, next, attempt);
            _logger.LogDebug("Submission {SubmissionId} tracking number {TrackingNumber}", submissionId, trackingNumber);
            return TrackingNumberResult.Allocated(trackingNumber, next);
        }
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

    /// <summary><c>{firm invoice number}.{n}</c>: n a positive integer without padding (D-37).</summary>
    private static string Format(string firmInvoiceNumber, int sequence) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{firmInvoiceNumber}.{sequence}");

    /// <summary>
    /// The highest sequence held for the firm and firm invoice number, over rows in every state (an inactive, closed submission still owns its
    /// number); 0 when none. Dataverse compares strings without regard to case, as the alternate key does. The submission being numbered
    /// is excluded: if a concurrent call for the same submission (a redelivered job) wrote its number between this call's row read and this
    /// read, counting that row would renumber it n + 1 and leave a gap; excluded, both calls compute the same n and the same write.
    /// </summary>
    private async Task<int> ReadHighestSequenceAsync(Guid submissionId, Guid firmId, string number, CancellationToken ct)
    {
        var query = new QueryExpression(SubmissionEntity)
        {
            ColumnSet = new ColumnSet(SequenceColumn),
            TopCount = 1,
        };
        query.Criteria.AddCondition(SubmissionIdColumn, ConditionOperator.NotEqual, submissionId);
        query.Criteria.AddCondition(FirmColumn, ConditionOperator.Equal, firmId);
        query.Criteria.AddCondition(FirmInvoiceNumberColumn, ConditionOperator.Equal, number);
        query.Criteria.AddCondition(SequenceColumn, ConditionOperator.NotNull);
        query.AddOrder(SequenceColumn, OrderType.Descending);

        var found = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return found.Entities.Count == 0 ? 0 : found.Entities[0].GetAttributeValue<int?>(SequenceColumn) ?? 0;
    }
}
