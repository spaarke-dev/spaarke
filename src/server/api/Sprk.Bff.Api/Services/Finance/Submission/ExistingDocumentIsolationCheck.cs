// ebilling-r1 task 050a, uac-r2 review round 3 (F1-a / F1-b): may a confirmed EXISTING document be submitted for the confirm's core record,
// judged by the document's ISOLATION, not by a list of filing columns.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — SecureChildShareSynchronizer.SecureRootsAboveAsync (uac-r2 task 147, public) is the upward walk that places a row under
//       its secure roots exactly as the ownership resolver would isolate it (through ANY lineage lookup: a thread, an event, an invoice, a
//       parent document); IsSecureOwnerTeamAsync names the Secure Record owner team. Both are reused, never re-implemented. The D-140 filing
//       chain (InvoiceSubmissionService.ResolveFilingAsync) reads six core columns, the communication's three regarding columns together
//       with its thread's three (round 4, the union SecureChildReconciler.ContextFor takes) and the parent chain: still a narrower set
//       than isolation uses (no event, invoice, budget or other ownership parent), which is the finding this check answers.
//   (2) Extension — the walk answers "which secure roots", not "may this confirm name this record"; that decision is the e-billing intake's
//       and is not added to a uac-r2 file. One type, because the intake (at confirm) and the persister (at commit, and again after a
//       rolled-back transaction) must decide it identically.
//   (3) Cost of doing nothing — an isolated document (a firm's reply on a Secure matter's thread, an attachment under a Secure matter's
//       event) is confirmed onto an ordinary matter as "unfiled", and the job commits the invoice and its content under that matter.
//
// Placement (bff-extensions.md): Services/Finance/Submission, an in-process read of the intake and the ADR-004 submission job in the BFF
// (ADR-052). No endpoint, no package, no AI call. DI: FinanceModule (scoped, like the scoped synchronizer it composes).

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>What <see cref="ExistingDocumentIsolationCheck.CheckAsync"/> decided.</summary>
public enum ExistingDocumentIsolationVerdict
{
    /// <summary>The document is under no secure root other than the confirm's record, and is not isolated without one.</summary>
    Allowed,

    /// <summary>The document is under a secure root that is not the confirm's record (another record: 409).</summary>
    OtherSecureRoot,

    /// <summary>The document is isolated, or may be, but its secure roots could not all be established from the data (409, fail closed).</summary>
    Undetermined,

    /// <summary>The isolation facts could not be read (a fault, or an ambiguous Secure Record owner team): nothing is decided (503 / retry).</summary>
    Unavailable,
}

/// <summary>The verdict and, for the log only, why (ids and reasons, never business data).</summary>
/// <param name="Roots">Task 094: on <see cref="ExistingDocumentIsolationVerdict.Allowed"/>, the document's secure roots as the walk found them
/// (none, or exactly the confirm's record); empty when the environment isolates nothing. Null on every other verdict.</param>
public sealed record ExistingDocumentIsolation(
    ExistingDocumentIsolationVerdict Verdict, string? Detail = null, IReadOnlyList<(string Table, Guid Id)>? Roots = null)
{
    /// <summary>Allowed, under no secure root.</summary>
    public static ExistingDocumentIsolation Allowed { get; } = new(ExistingDocumentIsolationVerdict.Allowed, null, Array.Empty<(string, Guid)>());
}

/// <summary>Task 094 (procedure-and-file-model 9.1 steps 3 and 5): what <see cref="InvoiceSubmissionService.CheckSourceFilingAsync"/> decided.</summary>
public enum SourceFilingVerdict
{
    /// <summary>Filed under the core record only (D-140) and isolated nowhere else.</summary>
    Allowed,

    /// <summary>Filed under, or isolated under, something else, or its filing cannot be placed (409 before commit; undo after).</summary>
    Mismatch,

    /// <summary>The source document no longer exists.</summary>
    Gone,

    /// <summary>The isolation facts could not be read: nothing is decided (a retry before commit; a failure after).</summary>
    Unavailable,
}

/// <summary>Task 094: the source's Restricted, internal-only and thread-privacy state as 9.1 step 3 read it. Owner K: never a refusal ground and
/// never stored; carried for the log and the tests only. Null is "no such row".</summary>
public sealed record SourceIsolationFlags(bool Restricted, bool? InternalOnly, int? Privacy);

/// <summary>Task 094: the verdict of the source re-check, why (ids and reasons only), and on Allowed the source's secure roots and flags.</summary>
public sealed record SourceFilingCheck(
    SourceFilingVerdict Verdict,
    string? Detail,
    IReadOnlyList<(string Table, Guid Id)>? Roots = null,
    SourceIsolationFlags? Flags = null);

/// <summary>
/// 050a review round 3 (uac-r2 F1-a / F1-b): the isolation rule of a confirmed existing document. The document's secure roots are found by
/// the resolver's own upward walk (<see cref="SecureChildShareSynchronizer.SecureRootsAboveAsync"/>) over every lineage lookup it has, so a
/// document isolated through ANY ownership parent is seen: its communication's thread, its communication's invoice / event / budget, its
/// own event, to-do, agreement or invoice, its parent document.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> Allowed only when every secure root above the document is the confirm's own record (none, or exactly that one).
/// A root elsewhere (an ordinary matter named, or another Secure matter) is <see cref="ExistingDocumentIsolationVerdict.OtherSecureRoot"/>.
/// A document whose roots are undetermined (a missing ancestor, a root flagged secure but not isolated, a filing deeper than the walk), or
/// that is owned by the Secure Record owner team while no root is found above it, is <see cref="ExistingDocumentIsolationVerdict.Undetermined"/>:
/// it is isolated under something this check cannot name, so it is never treated as unfiled.</para>
/// <para><b>Stricter than "the record is among the roots", on purpose.</b> A document under S and S2 confirmed onto S2 is refused too: its
/// content committed under S2 alone would reach S2's sharees, while the document itself is visible only to the sharees of both.</para>
/// <para><b>Fail closed.</b> A Dataverse fault on any read, and an ambiguous Secure Record owner team (the walk's Failed), are
/// <see cref="ExistingDocumentIsolationVerdict.Unavailable"/>: the caller writes nothing (503 at confirm; a Retry in the job). An environment
/// with no Secure Record owner team isolates nothing, so every document is Allowed there. A document that is no longer there is Allowed
/// by this check: nothing of it can leak, and the callers' own live-row checks refuse a missing document (confirm 404; the job's
/// <c>document_not_found</c>, and its transaction guard on the document's row version).</para>
/// </remarks>
public class ExistingDocumentIsolationCheck
{
    private const string DocumentEntity = "sprk_document";
    private const string OwningTeamColumn = "owningteam";
    private const string OwningUserColumn = "owninguser";

    private readonly IGenericEntityService _entities;
    private readonly SecureChildShareSynchronizer _shares;
    private readonly ILogger<ExistingDocumentIsolationCheck> _logger;

    public ExistingDocumentIsolationCheck(
        IGenericEntityService entities, SecureChildShareSynchronizer shares, ILogger<ExistingDocumentIsolationCheck> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _shares = shares ?? throw new ArgumentNullException(nameof(shares));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The columns the walk reads of the document: every lineage lookup of <c>sprk_document</c> (<see cref="SecureChildLineage"/>, the one
    /// map) and its owner. A lookup not read would count as absent and could only leave a root out, so the set is the map's, never a list.
    /// </summary>
    internal static string[] DocumentColumns { get; } =
        [.. SecureChildLineage.Children[DocumentEntity].Lookups.Keys, OwningTeamColumn, OwningUserColumn];

    /// <summary>
    /// Decides whether <paramref name="documentId"/> may be submitted for <paramref name="core"/> (see the class remarks). Never throws for a
    /// fault: a fault is <see cref="ExistingDocumentIsolationVerdict.Unavailable"/>. Only the caller's own cancellation propagates.
    /// </summary>
    public virtual async Task<ExistingDocumentIsolation> CheckAsync(Guid documentId, InvoiceCoreRecord core, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(core);
        try
        {
            var query = new QueryExpression(DocumentEntity) { ColumnSet = new ColumnSet(DocumentColumns), TopCount = 1, NoLock = true };
            query.Criteria.AddCondition(SecureChildLineage.Children[DocumentEntity].IdColumn, ConditionOperator.Equal, documentId);
            var document = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
            if (document is null)
            {
                return ExistingDocumentIsolation.Allowed; // gone: nothing of it can leak; the callers' live-row checks refuse it
            }

            var above = await _shares.SecureRootsAboveAsync(new[] { document }, ct).ConfigureAwait(false);
            switch (above.Status)
            {
                case SecureChildShareSyncStatus.NotApplicable:
                    return ExistingDocumentIsolation.Allowed; // no Secure Record owner team here: nothing is isolated
                case SecureChildShareSyncStatus.Completed:
                    break;
                default:
                    return Unavailable(documentId, above.Detail ?? above.Status.ToString());
            }

            if (above.Undetermined.Count > 0)
            {
                return Decided(documentId, core, ExistingDocumentIsolationVerdict.Undetermined, string.Join("; ", above.Undetermined));
            }

            var own = (Table: core.EntityLogicalName, Id: core.RecordId);
            if (above.Roots.FirstOrDefault(r => !(string.Equals(r.Table, own.Table, StringComparison.OrdinalIgnoreCase) && r.Id == own.Id))
                is { Table: not null } other)
            {
                return Decided(documentId, core, ExistingDocumentIsolationVerdict.OtherSecureRoot, $"under secure root {other.Table} {other.Id}");
            }

            if (above.Roots.Count == 0
                && document.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } team && team != Guid.Empty
                && await _shares.IsSecureOwnerTeamAsync(team, ct).ConfigureAwait(false))
            {
                return Decided(documentId, core, ExistingDocumentIsolationVerdict.Undetermined, "owned by the Secure Record owner team under no secure root");
            }

            return new ExistingDocumentIsolation(ExistingDocumentIsolationVerdict.Allowed, null, above.Roots);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Existing-document isolation of {DocumentId} could not be read; nothing is decided", documentId);
            return new ExistingDocumentIsolation(ExistingDocumentIsolationVerdict.Unavailable, ex.GetType().Name);
        }
    }

    private ExistingDocumentIsolation Unavailable(Guid documentId, string detail)
    {
        _logger.LogWarning("Existing-document isolation of {DocumentId} could not be decided ({Detail})", documentId, detail);
        return new ExistingDocumentIsolation(ExistingDocumentIsolationVerdict.Unavailable, detail);
    }

    private ExistingDocumentIsolation Decided(Guid documentId, InvoiceCoreRecord core, ExistingDocumentIsolationVerdict verdict, string detail)
    {
        _logger.LogInformation("Existing-document isolation: document {DocumentId} may not be submitted for {Entity} {RecordId} ({Verdict}: {Detail})",
            documentId, core.EntityLogicalName, core.RecordId, verdict, detail);
        return new ExistingDocumentIsolation(verdict, detail);
    }
}
