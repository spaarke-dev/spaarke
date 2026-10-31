using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>What <see cref="ReconciledEmailFiling.FileIfUnfiledAsync"/> did with the reconciled communication.</summary>
public enum ReconciledFilingResult
{
    /// <summary>Nothing to decide: the save created its own communication, named no record, or named one the engine does not file to.</summary>
    NotApplicable,

    /// <summary>The communication was filed to the save's record.</summary>
    Filed,

    /// <summary>The communication was already filed (to any record) and was left as it was.</summary>
    AlreadyFiled,

    /// <summary>The caller may not change the communication; it was left unfiled.</summary>
    NotPermitted,

    /// <summary>The filing could not be completed (a read, the owner decision or the write failed); left as it was.</summary>
    Failed,
}

/// <summary>
/// spaarkeai-word-add-in-r1 task 121, owner decision 2026-10-09 ("file it if unfiled"): an Office email save (task pane
/// or Quick Save) that reconciles to a <c>sprk_communication</c> that ALREADY existed — the same email, captured from a
/// mailbox or saved by someone else, found by its RFC Message-ID — files that communication to the record the user picked,
/// when it is filed to nothing. A communication that is already filed is never moved, and an unfiled save files nothing.
/// </summary>
/// <remarks>
/// <para><b>One writer per invariant.</b> The filing is the association engine's own write for an EXISTING communication
/// (<see cref="IncomingAssociationResolver.ApplyToExistingRecordAsync"/> — what <c>ResolveAsync</c> writes after evaluating):
/// the regarding lookup, the ADR-024 resolver fields, the FR-26 core-ancestor stamps, the inherited Access Permission, and
/// the owner re-derived by <see cref="Dataverse.IRecordOwnershipResolver.ReparentAsync"/> (the named Secure team under a
/// secure record). It applies the decision the capture already evaluated for THIS save, with the save's record as the
/// caller-supplied regarding (rung 0) — narrowed by the resolver to that one record, so nothing else the engine proposed is
/// written by the caller's act, and the provenance marks every dropped candidate not written. Then the core-ancestor re-stamp
/// of what is filed under the communication (<see cref="CoreAncestorRestamper.AfterWriteAsync"/>, ontology task 144 — the
/// same call the communication filing route and the AI update tool make), then <see cref="SecureChildReconciler.AfterRefileAsync"/>, the one step after every re-file (the row's share
/// mirror and task 148's pass over what is filed under it). Nothing here writes a regarding column by hand.</para>
/// <para><b>Authorization: the communication re-file rule.</b> <c>PATCH /api/communications/{id}/filing</c> requires Write on
/// the communication as the caller (<c>CommunicationRecordAuthorizationFilter</c>, <c>Refile</c>) and AppendTo on the record
/// it is filed under. The save route's <c>EntityAccessFilter</c> has already required AppendTo on the save's record
/// (<c>entity.associate_document</c>); Write on the communication is asked here, through the same
/// <see cref="CallerRecordAccessProbe"/>, with the caller's own token. The F3 rule (a move OUT of a secure record) CAN arise
/// although only an unfiled communication is filed: an unfiled email may still sit in a secure record's thread (an unlink
/// leaves it there, #2165), and the filing moves it out of that thread (#2143 round 7b asks F3 at the filing). So the filing
/// and the thread move after it act for the caller (<see cref="SecureRemovalCaller.ForBearerToken"/>, #2143 round 8 F1-B):
/// a Full Access holder on that record is permitted; anyone else is refused, and the communication is left unfiled.</para>
/// <para><b>Never fails the save.</b> A refusal or a failure leaves the communication as it was and is logged; the document
/// still saves and links. The outcome is returned in <see cref="SaveResponse.EmailFiling"/> (task 131), so the client can tell
/// the user the email was not filed.</para>
/// <para><b>Concurrency.</b> The filing is read again immediately before the write; a communication filed in between is left
/// alone. The generic Dataverse seam has no conditional update, so a filing that lands in the milliseconds between that
/// re-read and the write is not detected (the engine's write is additive, so it would add the save's record beside it).</para>
/// </remarks>
public sealed class ReconciledEmailFiling
{
    internal const string CommunicationEntity = "sprk_communication";
    internal const string CommunicationEntitySet = "sprk_communications";
    private const string PairColumn = "sprk_regardingrecordid";
    private const string OwningTeamColumn = "owningteam";

    /// <summary>Every column that says what a communication is filed under: the regarding family and the ADR-024 pair.</summary>
    internal static readonly string[] FilingColumns = RegardingFieldMap.AllRegardingFields.Append(PairColumn).ToArray();

    private readonly IncomingAssociationResolver _associationResolver;
    private readonly IGenericEntityService _dataverse;
    private readonly CallerRecordAccessProbe _accessProbe;
    private readonly SecureChildReconciler _children;
    private readonly CoreAncestorRestamper _restamper;
    private readonly ILogger<ReconciledEmailFiling> _logger;
    private readonly SignalEvaluationTrigger? _signalTrigger;

    /// <param name="signalTrigger">Ontology D-48 (#1659): queues the matter's Signal re-evaluation once a classified communication
    /// is filed to it. Registered in production; null only where a test builds the filing without it.</param>
    public ReconciledEmailFiling(
        IncomingAssociationResolver associationResolver,
        IGenericEntityService dataverse,
        CallerRecordAccessProbe accessProbe,
        SecureChildReconciler children,
        CoreAncestorRestamper restamper,
        ILogger<ReconciledEmailFiling> logger,
        SignalEvaluationTrigger? signalTrigger = null)
    {
        _associationResolver = associationResolver ?? throw new ArgumentNullException(nameof(associationResolver));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _accessProbe = accessProbe ?? throw new ArgumentNullException(nameof(accessProbe));
        _children = children ?? throw new ArgumentNullException(nameof(children));
        _restamper = restamper ?? throw new ArgumentNullException(nameof(restamper));
        _logger = logger;
        _signalTrigger = signalTrigger;
    }

    /// <summary>
    /// Files the communication <paramref name="capture"/> reconciled to under the save's record, when it is filed to
    /// nothing and the caller may change it. Never throws (other than cancellation).
    /// </summary>
    public async Task<ReconciledFilingResult> FileIfUnfiledAsync(
        SaveRequest request, EmailCaptureOutcome? capture, string? callerBearerToken, CancellationToken ct)
    {
        if (capture is not { ReconciledToExisting: true } || request.TargetEntity is not { } target)
            return ReconciledFilingResult.NotApplicable;

        // Only the save's own record — the caller-supplied regarding — never another target the engine proposed. The
        // resolver narrows the decision (writes and provenance together) when it applies it; this is the early answer.
        if (IncomingAssociationResolver.NarrowToRecord(capture.Decision, target.EntityId) is null)
            return ReconciledFilingResult.NotApplicable;

        var communicationId = capture.CommunicationId;
        try
        {
            var before = await ReadFilingAsync(communicationId, ct).ConfigureAwait(false);
            if (before.Filed)
                return ReconciledFilingResult.AlreadyFiled;

            var rights = await _accessProbe
                .GetCallerRightsAsync(callerBearerToken, CommunicationEntitySet, communicationId, ct)
                .ConfigureAwait(false);
            if ((rights & AccessRights.Write) != AccessRights.Write)
            {
                _logger.LogInformation(
                    "Office save reconciled to unfiled communication {CommunicationId}; the caller may not change it, so it " +
                    "was left unfiled (task 121).", communicationId);
                return ReconciledFilingResult.NotPermitted;
            }

            if ((await ReadFilingAsync(communicationId, ct).ConfigureAwait(false)).Filed)
                return ReconciledFilingResult.AlreadyFiled; // filed by someone else since the first read — never moved

            // #2143 round 8 (F1-B): the person who saved — the reparent asks F3 for them, and the thread move after it acts for them.
            var caller = SecureRemovalCaller.ForBearerToken(_accessProbe, callerBearerToken);
            await _associationResolver
                .ApplyToExistingRecordAsync(communicationId, capture.Decision, target.EntityId, caller, ct)
                .ConfigureAwait(false);

            // Ontology task 144: the rows filed under the communication (to-dos, events, Signals, Decision Records) copy its
            // root, so they are re-stamped NOW, in the same operation — the filing route and the AI update tool do the same.
            // A stamp left stale since the communication was unfiled outside the BFF is corrected here rather than at the
            // 5-minute job. Before the step below, which reads those stamps as ownership parents. The restamper never throws
            // by contract (no caller token is passed); a fault here is still caught locally, because the filing has landed and
            // the ownership step and the Signal trigger below must run whatever happens to the re-stamp.
            try
            {
                var restamp = await _restamper
                    .AfterWriteAsync(CommunicationEntity, communicationId, FilingColumns, CancellationToken.None)
                    .ConfigureAwait(false);
                if (!restamp.Complete)
                {
                    _logger.LogWarning(
                        "Office save filed communication {CommunicationId}, but the core-ancestor re-stamp of what is filed under " +
                        "it did not finish (failures={Failures} truncated={Truncated}); the reconciliation job repairs it within one " +
                        "cycle, except Signals and Decision Records while they are report-only there (ontology task 144).",
                        communicationId, restamp.Failures.Count, restamp.Truncated);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Office save filed communication {CommunicationId}, but the core-ancestor re-stamp of what is filed under it " +
                    "faulted; the filing stands, and the reconciliation job re-stamps them within one cycle, except Signals and " +
                    "Decision Records while they are report-only there (ontology task 144).",
                    communicationId);
            }

            // The one step after every re-file. Never throws; the filing above stands.
            await _children.AfterRefileAsync(
                CommunicationEntity, communicationId,
                () => before.OwningTeam is { } team
                    ? _children.IsSecureOwnerTeamAsync(team, CancellationToken.None)
                    : Task.FromResult(false),
                RefileThreadStep.Reconcile, caller, CancellationToken.None).ConfigureAwait(false);

            _logger.LogInformation(
                "Office save filed the unfiled communication {CommunicationId} it reconciled to under {EntityType} {EntityId} " +
                "(task 121).", communicationId, target.EntityType, target.EntityId);

            // Ontology D-48 / #1659 (additive): a communication classified first and filed to a matter now is evaluated for
            // that matter's Signals at once, not at the nightly pass. Never throws; the filing above stands either way.
            await FiledCommunicationSignalTrigger
                .EnqueueIfClassifiedOnMatterAsync(_dataverse, _signalTrigger, communicationId, _logger)
                .ConfigureAwait(false);
            return ReconciledFilingResult.Filed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Office save could not file the communication {CommunicationId} it reconciled to; it was left as it was and " +
                "the save proceeds (task 121).", communicationId);
            return ReconciledFilingResult.Failed;
        }
    }

    private async Task<(bool Filed, Guid? OwningTeam)> ReadFilingAsync(Guid communicationId, CancellationToken ct)
    {
        var row = await _dataverse
            .RetrieveAsync(CommunicationEntity, communicationId, FilingColumns.Append(OwningTeamColumn).ToArray(), ct)
            .ConfigureAwait(false);
        if (row is null)
            throw new InvalidOperationException($"Communication {communicationId} could not be read.");

        var filed = FilingColumns.Any(column => row.Attributes.TryGetValue(column, out var value) && value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true,
        });
        var owningTeam = row.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id;
        return (filed, owningTeam);
    }
}
