using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The ONE place that decides "may this caller export these invoices to AP" (ebilling-r1 task 150; owner decision EB-D-178, 2026-10-30).
/// </summary>
/// <remarks>
/// <para><b>The rule today (EB-D-178).</b> The caller needs <c>Create</c> on <c>sprk_billingexport</c> (the table privilege, asked of Dataverse as the
/// caller: on-behalf-of, uncached, fail closed) AND <c>ebilling.invoice.decide</c> (Write) on EVERY invoice in the batch (the right approval uses,
/// so whoever can approve an invoice can export it; asked of Dataverse as the caller) AND Read (<c>finance.read</c>) on every invoice (D-85: the
/// batch copies the invoice's values into files, so a caller who can write but not read an invoice must not export it). No schema change. If any invoice fails, the whole batch is
/// refused before anything is written, and the answer names no invoice (<see cref="ApExportAuthorizationOutcome"/> carries no id).</para>
/// <para><b>Kept open on purpose (owner: "leave open the option that we move this to a more specific role in the future").</b> The route filter
/// (<see cref="RouteTargets"/>) and <see cref="ApExportService"/> ask ONLY this class. A dedicated, separately grantable export right built on the
/// D-76 override-right pattern (<see cref="EbillingOverridePrivilege"/>: a carrier table, <see cref="CallerRecordAccessProbe.CallerHoldsPrivilegeAsync"/>)
/// replaces the privilege constants and the body of <see cref="AuthorizeExportAsync"/> here and changes nothing else: not the endpoints, not the
/// service, not the export flow.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="CallerReadCheck"/> (the per-record rights probe the approval and review
/// services use) and <see cref="CallerRecordAccessProbe.CallerHoldsPrivilegeAsync"/> (the table-privilege question). Extension: both are reused unchanged;
/// the class only composes them under one name so the decision has one home. Cost of doing nothing: the route filter and the service would each
/// spell the rule, and moving to a dedicated right later would mean editing the export flow (the owner asked that it not).</para>
/// </remarks>
public sealed class ApExportAuthorization
{
    /// <summary>The table privilege "Create a batch": <c>prv{Verb}{SchemaName}</c>, schema name all lower case (pinned by a test against the Core User role).</summary>
    public const string CreateBatchPrivilege = "prvCreatesprk_billingexport";

    /// <summary>The batch table's entity SET name (read live; never pluralized).</summary>
    public const string BatchEntitySet = "sprk_billingexports";

    /// <summary>The per-invoice right: the approval right (Write on the invoice).</summary>
    public const string InvoiceDecideOperation = "ebilling.invoice.decide";

    /// <summary>
    /// The per-invoice Read right (<c>finance.read</c>, the key the invoice review read routes use on an invoice). D-85: the batch copies every invoice's
    /// values into files that anyone who can see the batch can read, so the exporting caller must be able to read each invoice itself; Write alone does
    /// not imply Read in Dataverse.
    /// </summary>
    public const string InvoiceReadOperation = "finance.read";

    /// <summary>The rights asked on every invoice, in this order (Write is the decide right, EB-D-178; Read is D-85). All of them or the batch is refused.</summary>
    private static readonly string[] RequiredInvoiceOperations = [InvoiceDecideOperation, InvoiceReadOperation];

    private readonly AuthorizationService _authorization;
    private readonly CallerRecordAccessProbe _probe;
    private readonly ILogger<ApExportAuthorization> _logger;

    public ApExportAuthorization(AuthorizationService authorization, CallerRecordAccessProbe probe, ILogger<ApExportAuthorization> logger)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The route-level declaration for the export routes: the table-level part of the rule, so a caller without Create on the batch table is refused
    /// by the filter before the handler runs. It is the same privilege <see cref="AuthorizeExportAsync"/> asks again (the service never trusts the
    /// filter); the per-invoice part cannot be declared here because the invoice ids are in the request body.
    /// </summary>
    public static FinanceAuthorizationTargets RouteTargets() =>
        FinanceAuthorizationTargets.Authorize(
            FinanceAuthorizationCheck.CallerPrivilege(CreateBatchPrivilege, BatchEntitySet, "ap export: create a batch"));

    /// <summary>
    /// May <paramref name="caller"/> export <paramref name="invoiceIds"/> in one batch? Fails closed: a missing token, an unanswered or faulted
    /// probe and a throwing check all refuse (<see cref="ApExportAuthorizationOutcome.Undetermined"/> for "no answer", never an allow).
    /// </summary>
    public async Task<ApExportAuthorizationOutcome> AuthorizeExportAsync(
        InvoiceContextService.InvoiceCaller caller, IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(invoiceIds);
        if (string.IsNullOrWhiteSpace(caller.AccessToken) || invoiceIds.Count == 0)
        {
            return ApExportAuthorizationOutcome.NotPermitted;
        }

        try
        {
            if (!await _probe.CallerHoldsPrivilegeAsync(caller.AccessToken, CreateBatchPrivilege, ct).ConfigureAwait(false))
            {
                _logger.LogWarning("AP export refused: the caller does not hold {Privilege}", CreateBatchPrivilege);
                return ApExportAuthorizationOutcome.NotPermitted;
            }

            // Each right is asked of Dataverse as the caller, on every invoice: Write (the decide right), then Read (the batch carries the invoice's
            // values into files, D-85, so the caller must be able to read every invoice). A refusal on either refuses the whole batch.
            foreach (var operation in RequiredInvoiceOperations)
            {
                var scan = await CallerReadCheck.ScanAsync(
                    _authorization, caller, FinanceAuthorizationFilter.InvoiceEntitySet, operation,
                    invoiceIds.ToList(), id => id, stopAtReadable: int.MaxValue, maxChecks: invoiceIds.Count,
                    _logger, "AP export", ct).ConfigureAwait(false);

                if (scan.Failed)
                {
                    // Dataverse did not answer for at least one invoice: not an answer, so the batch is not authorized (fail closed).
                    return ApExportAuthorizationOutcome.Undetermined;
                }

                if (scan.Readable.Count != invoiceIds.Count)
                {
                    _logger.LogWarning(
                        "AP export refused: the caller holds {Operation} on {Held} of {Asked} invoices; nothing is written",
                        operation, scan.Readable.Count, invoiceIds.Count);
                    return ApExportAuthorizationOutcome.NotPermitted;
                }
            }

            return ApExportAuthorizationOutcome.Allowed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "AP export authorization faulted; refusing (fail closed)");
            return ApExportAuthorizationOutcome.Undetermined;
        }
    }

    /// <summary>
    /// The subset of <paramref name="candidateIds"/> the caller may export (Write on the invoice), in order, for the candidates list: it shows no
    /// invoice the batch request would refuse. <c>null</c> when the rights could not be determined (nothing is shown).
    /// </summary>
    public async Task<IReadOnlyList<Guid>?> FilterExportableAsync(
        InvoiceContextService.InvoiceCaller caller, IReadOnlyList<Guid> candidateIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(candidateIds);
        if (string.IsNullOrWhiteSpace(caller.AccessToken) || candidateIds.Count == 0)
        {
            return [];
        }

        try
        {
            if (!await _probe.CallerHoldsPrivilegeAsync(caller.AccessToken, CreateBatchPrivilege, ct).ConfigureAwait(false))
            {
                return [];
            }

            // The same two rights as the batch request, narrowing the list in turn: what is left is what the batch request would accept.
            IReadOnlyList<Guid> remaining = candidateIds;
            foreach (var operation in RequiredInvoiceOperations)
            {
                if (remaining.Count == 0)
                {
                    break;
                }

                var scan = await CallerReadCheck.ScanAsync(
                    _authorization, caller, FinanceAuthorizationFilter.InvoiceEntitySet, operation,
                    remaining, id => id, stopAtReadable: int.MaxValue, maxChecks: remaining.Count,
                    _logger, "AP export candidates", ct).ConfigureAwait(false);
                if (scan.Failed)
                {
                    return null;
                }

                remaining = scan.Readable;
            }

            return remaining;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "AP export candidate authorization faulted; showing nothing (fail closed)");
            return null;
        }
    }
}

/// <summary>The verdict of <see cref="ApExportAuthorization.AuthorizeExportAsync"/>. It carries no invoice id: a refusal names nothing about any invoice.</summary>
public enum ApExportAuthorizationOutcome
{
    /// <summary>Create on the batch table and the decide right on every invoice.</summary>
    Allowed,

    /// <summary>Dataverse answered and a right is missing (on the table, or on at least one invoice).</summary>
    NotPermitted,

    /// <summary>Dataverse gave no answer (a fault, no identity): not an allow.</summary>
    Undetermined,
}
