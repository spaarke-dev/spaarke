using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;

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
/// written by the caller's act, and the provenance marks every dropped candidate not written. Then <see cref="SecureChildReconciler.AfterRefileAsync"/>, the one step after every re-file (the row's share
/// mirror and task 148's pass over what is filed under it). Nothing here writes a regarding column by hand.</para>
/// <para><b>Authorization: the communication re-file rule.</b> <c>PATCH /api/communications/{id}/filing</c> requires Write on
/// the communication as the caller (<c>CommunicationRecordAuthorizationFilter</c>, <c>Refile</c>) and AppendTo on the record
/// it is filed under. The save route's <c>EntityAccessFilter</c> has already required AppendTo on the save's record
/// (<c>entity.associate_document</c>); Write on the communication is asked here, through the same
/// <see cref="CallerRecordAccessProbe"/>, with the caller's own token. The F3 rule (a move OUT of a secure record) cannot
/// arise: only an unfiled communication is filed.</para>
/// <para><b>Never fails the save.</b> A refusal or a failure leaves the communication as it was and is logged; the document
/// still saves and links. <see cref="SaveResponse"/> has no warnings channel, so nothing is reported to the client.</para>
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
    private readonly ILogger<ReconciledEmailFiling> _logger;

    public ReconciledEmailFiling(
        IncomingAssociationResolver associationResolver,
        IGenericEntityService dataverse,
        CallerRecordAccessProbe accessProbe,
        SecureChildReconciler children,
        ILogger<ReconciledEmailFiling> logger)
    {
        _associationResolver = associationResolver ?? throw new ArgumentNullException(nameof(associationResolver));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _accessProbe = accessProbe ?? throw new ArgumentNullException(nameof(accessProbe));
        _children = children ?? throw new ArgumentNullException(nameof(children));
        _logger = logger;
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

            await _associationResolver
                .ApplyToExistingRecordAsync(communicationId, capture.Decision, target.EntityId, ct)
                .ConfigureAwait(false);

            // The one step after every re-file. Never throws; the filing above stands.
            await _children.AfterRefileAsync(
                CommunicationEntity, communicationId,
                () => before.OwningTeam is { } team
                    ? _children.IsSecureOwnerTeamAsync(team, CancellationToken.None)
                    : Task.FromResult(false),
                CancellationToken.None).ConfigureAwait(false);

            _logger.LogInformation(
                "Office save filed the unfiled communication {CommunicationId} it reconciled to under {EntityType} {EntityId} " +
                "(task 121).", communicationId, target.EntityType, target.EntityId);
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
