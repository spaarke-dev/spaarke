// ebilling-r1 082c-2 (notes/082c-brief.md section 7: the owner's "In Review with a description" decision; ontology D-205 on the lifecycle;
// uac-r2 2026-11-04 conditions): sprk_invoice.sprk_reviewdetail, the partner-readable label of WHICH kind of review a held invoice is in.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — statuscode (the owner keeps a held invoice In Review); sprk_firmhold (field-secured, internal, the firm case only);
//       InvoiceProcedureHold / InvoiceFirmHold (the holds themselves, read from the stored submission state).
//   (2) Extension — statuscode cannot carry it by the owner's decision, and sprk_firmhold is not partner-readable by design (095c-5). The
//       holds stay where they are: this is a derived label over them, never a hold of its own.
//   (3) Cost of doing nothing — the partner's grid and record say "In Review" with no explanation, which is what the owner asked to fix.
//
// Placement (bff-extensions.md): BFF, Services/Finance/Submission beside InvoiceFirmHold and InvoiceProcedureHold. No DI registration, no
// endpoint, no package: a static derivation and a process-wide column probe (the TrackingScopePhaseCache shape).

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// <c>sprk_invoice.sprk_reviewdetail</c> ("Review Detail"): a LOCAL choice whose value is DERIVED from the invoice's open hold reasons by
/// <see cref="ReviewDetailOf"/>, never set or cleared on one writer's say-so (ontology D-205). Blank is a normal review.
/// </summary>
/// <remarks>
/// <para><b>Invariant.</b> Non-blank only while the invoice is In Review (<c>statuscode</c> <see cref="InvoiceStatusCodes.InReview"/>): every
/// exit from In Review writes blank in the same write as the status (uac-r2 condition (b)). Checked by
/// <c>scripts/ebilling/Deploy-EbillingInvoiceReviewDetailColumn.ps1 -Verify</c> and by the unit tests.</para>
/// <para><b>Writers</b> (each named in <c>InvoiceReviewDetailWritersGuardTests</c>): the submission persister's held create; the 095d-1 Set firm
/// apply (<c>InvoiceAssignFirmService</c>, recomputed from the remaining Procedure reasons); <see cref="InvoiceLifecycleService"/>'s every move
/// out of In Review (approve, reject, return to firm, cancel; and Approved / Sent to AP / Paid onwards) and the correction's Superseded close,
/// whose undo (<c>SubmissionCommitGate.ReopenSupersededAsync</c>) reopens the invoice with the detail its remaining holds derive (fix round 1).
/// The 114 Override release (<c>SubmissionOverrideService.BuildReleaseTransaction</c>, task 114) recomputes it from the holds that remain after
/// the release (<see cref="InvoiceOpenHolds.ReasonsAfterAsync"/>), in the same transaction as the submission's state. Pending writers, each to
/// recompute from the remaining reasons in its own transaction: the re-check that clears a gap or unresolved (no 082c-2 path clears one;
/// 082d / 101 and the ebilling-r2 re-check) and the 095d-2 re-run. The 089e chain close is NOT a writer: it writes the Signal only (C19), so
/// it neither sets nor clears an invoice's detail.</para>
/// <para><b>Not a hold.</b> Approval reads the stored submission state (<see cref="InvoiceProcedureHold"/>, <see cref="InvoiceFirmHold"/>), so
/// the holds hold on an environment where this column does not exist yet (<see cref="ReviewDetailColumnProbe"/>).</para>
/// </remarks>
public static class InvoiceReviewDetail
{
    /// <summary>The column (local choice on <c>sprk_invoice</c>).</summary>
    public const string Column = "sprk_reviewdetail";

    /// <summary>"Waiting for billing terms": the Procedure gap (<see cref="SubmissionReasonCodes.ProcedureGap"/>).</summary>
    public const int WaitingForBillingTerms = 659490000;

    /// <summary>"Billing terms could not be checked": <see cref="SubmissionReasonCodes.ProceduresUnresolved"/>.</summary>
    public const int BillingTermsNotChecked = 659490001;

    /// <summary>"Waiting for the firm to be confirmed": <see cref="SubmissionReasonCodes.FirmMissing"/>.</summary>
    public const int WaitingForFirm = 659490002;

    /// <summary>The closed option set: (value, label), as the schema script creates it. A value is never reused for another meaning.</summary>
    public static IReadOnlyList<(int Value, string Label)> Options { get; } =
    [
        (WaitingForBillingTerms, "Waiting for billing terms"),
        (BillingTermsNotChecked, "Billing terms could not be checked"),
        (WaitingForFirm, "Waiting for the firm to be confirmed"),
    ];

    /// <summary>
    /// The review detail of an invoice whose open hold reasons are <paramref name="openHoldReasons"/> (reason codes), by the fixed priority of
    /// ontology D-205: (1) the firm (without it no Procedure can resolve), (2) the terms could not be checked, (3) waiting for terms; null
    /// (blank) when none of the three is open. Any other code is ignored. Pure.
    /// </summary>
    public static int? ReviewDetailOf(IEnumerable<string>? openHoldReasons)
    {
        var reasons = openHoldReasons as IReadOnlySet<string> ?? (openHoldReasons ?? []).ToHashSet(StringComparer.Ordinal);
        if (reasons.Contains(SubmissionReasonCodes.FirmMissing))
        {
            return WaitingForFirm;
        }

        if (reasons.Contains(SubmissionReasonCodes.ProceduresUnresolved))
        {
            return BillingTermsNotChecked;
        }

        return reasons.Contains(SubmissionReasonCodes.ProcedureGap) ? WaitingForBillingTerms : null;
    }

    /// <summary>The column value of <paramref name="detail"/>: its option, or null (blank).</summary>
    public static OptionSetValue? ValueOf(int? detail) => detail is { } value ? new OptionSetValue(value) : null;

    /// <summary>The Dataverse fault "'sprk_invoice' entity doesn't contain attribute with Name = 'sprk_reviewdetail'" (0x80041103).</summary>
    private const int AttributeDoesNotExistFault = unchecked((int)0x80041103);

    /// <summary>
    /// Whether <paramref name="ex"/> says <see cref="Column"/> does not exist in this environment (as
    /// <c>TrackingNumberAllocator.IsPreviousSequenceColumnMissing</c>): the typed fault code, or the message where the client surfaces only
    /// text; either way the message must name THIS column. Walks the exception chain.
    /// </summary>
    internal static bool IsColumnMissing(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.ServiceModel.FaultException<OrganizationServiceFault> faultException)
            {
                for (var fault = faultException.Detail; fault is not null; fault = fault.InnerFault)
                {
                    if (fault.ErrorCode == AttributeDoesNotExistFault && NamesTheColumn(fault.Message))
                    {
                        return true;
                    }
                }
            }

            if (NamesTheColumn(e.Message)
                && (e.Message.Contains("contain attribute with Name", StringComparison.OrdinalIgnoreCase)
                    || e.Message.Contains("0x80041103", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NamesTheColumn(string? message) =>
        message is { Length: > 0 } && message.Contains(Column, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Whether <see cref="InvoiceReviewDetail.Column"/> exists in this environment, so a writer can leave it out where it does not (the BFF may be
/// deployed before the column's script runs). A process-wide cache: present is cached for good; missing is re-checked after
/// <see cref="RefreshInterval"/>; a probe that faults for another reason answers present (the writer then names the column, so the write fails
/// rather than a detail being left behind on a closed invoice).
/// </summary>
/// <remarks>
/// <para><b>Mismatch choice (brief 3, 082c-2): skip with a warning logged once, never a silent pass on the hold.</b> Without the column the
/// review detail is not written, and nothing else changes: the holds are the stored submission state, read by approval, so they hold
/// without it. Fail closed (refusing every held create and every status move) was rejected: it would stop approve, reject and pay
/// everywhere until the script ran, to protect a label.</para>
/// </remarks>
public sealed class ReviewDetailColumnProbe
{
    /// <summary>How long a "missing" answer is used before the column is probed again.</summary>
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>The probe every writer in this process uses.</summary>
    public static ReviewDetailColumnProbe Shared { get; } = new(TimeProvider.System);

    private const int Unknown = 0;
    private const int Present = 1;
    private const int Missing = 2;

    private readonly TimeProvider _clock;
    private int _state = Unknown;
    private long _missingAtTicks;
    private int _missingLogged;

    internal ReviewDetailColumnProbe(TimeProvider clock) => _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>A probe whose answer is fixed (tests).</summary>
    internal static ReviewDetailColumnProbe Fixed(bool present)
    {
        var probe = new ReviewDetailColumnProbe(TimeProvider.System);
        if (present)
        {
            probe._state = Present;
        }
        else
        {
            probe._state = Missing;
            probe._missingAtTicks = long.MaxValue / 2;
        }

        return probe;
    }

    /// <summary>
    /// The cached answer without a read: false only when the column was found missing less than <see cref="RefreshInterval"/> ago; true
    /// otherwise (present, or not yet known: a writer then names the column).
    /// </summary>
    public bool WriteColumn => Volatile.Read(ref _state) != Missing || IsStale();

    /// <summary>Whether the column exists: the cached answer, else one read of it (app-only, through <paramref name="entities"/>).</summary>
    public async Task<bool> IsPresentAsync(IGenericEntityService entities, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(logger);
        var state = Volatile.Read(ref _state);
        if (state == Present || (state == Missing && !IsStale()))
        {
            return state == Present;
        }

        try
        {
            await entities.RetrieveMultipleAsync(
                new QueryExpression(InvoiceReviewService.InvoiceEntity) { ColumnSet = new ColumnSet(InvoiceReviewDetail.Column), TopCount = 1 },
                ct).ConfigureAwait(false);
            Volatile.Write(ref _state, Present);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (InvoiceReviewDetail.IsColumnMissing(ex))
        {
            MarkMissing(logger);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Whether {Column} exists could not be read ({ExceptionType}); it is written as if it exists", InvoiceReviewDetail.Column, ex.GetType().Name);
            return true;
        }
    }

    /// <summary>Records that a write or read found the column missing, logging it once per process.</summary>
    public void MarkMissing(ILogger logger)
    {
        Interlocked.Exchange(ref _missingAtTicks, _clock.GetUtcNow().UtcTicks);
        Volatile.Write(ref _state, Missing);
        if (Interlocked.Exchange(ref _missingLogged, 1) == 0)
        {
            logger.LogWarning(
                "{Column} does not exist in this environment yet (run scripts/ebilling/Deploy-EbillingInvoiceReviewDetailColumn.ps1): the review " +
                "detail is not written. The firm and Procedure holds are unaffected (approval reads them from the stored submission state). Logged once",
                InvoiceReviewDetail.Column);
        }
    }

    private bool IsStale() => _clock.GetUtcNow().UtcTicks - Interlocked.Read(ref _missingAtTicks) >= RefreshInterval.Ticks;
}
