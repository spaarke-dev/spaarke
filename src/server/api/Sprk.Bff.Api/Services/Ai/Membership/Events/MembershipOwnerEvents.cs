// unified-access-control-r2 task 152 — the ONE publishing path for "a row was created; here is its owner".
//
// Used by the four create sites that publish an owner MembershipChangedEvent (POST /api/v1/documents,
// POST /api/v1/events, Office quick-create matter, Office save). Each used to build the event inline with the
// CALLER's AAD oid as a User owner; this reads the row's real owner (or takes the one the create path already
// holds), applies the same application-user rule as MembershipReconciliationJob, and publishes fire-and-forget.

using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Membership.Events;

/// <summary>
/// Publishes the owner <see cref="MembershipChangedEvent"/> for a freshly written row (ADR-034 Q2 fire-and-forget:
/// never throws; the nightly reconciliation job is the backstop for anything not published).
/// </summary>
internal static class MembershipOwnerEvents
{
    /// <summary>
    /// Builds and publishes the Added owner event for <paramref name="entityLogicalName"/>(<paramref name="recordId"/>).
    /// </summary>
    /// <param name="publisher">The (possibly Null-Object) publisher.</param>
    /// <param name="dataverse">App-only reader: the row's owner when <paramref name="knownOwner"/> is null, and the
    /// owner's <c>applicationid</c> when it is a systemuser. Reads ids only; nothing is shown to anyone.</param>
    /// <param name="entityLogicalName">The created row's table.</param>
    /// <param name="recordId">The created row's id.</param>
    /// <param name="knownOwner">The owner the create path wrote, when it holds it; otherwise it is read back.</param>
    /// <param name="correlationId">Request trace id.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The published event, or <see langword="null"/> when nothing was published (and why is logged).</returns>
    public static async Task<MembershipChangedEvent?> PublishOwnerAddedAsync(
        IMembershipEventPublisher publisher,
        IGenericEntityService dataverse,
        string entityLogicalName,
        Guid recordId,
        EntityReference? knownOwner,
        string correlationId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var owner = knownOwner;
            if (owner is null)
            {
                var row = await dataverse
                    .RetrieveAsync(entityLogicalName, recordId, new[] { MembershipChangedEvent.OwnerSourceField }, ct)
                    .ConfigureAwait(false);
                owner = row is not null && row.Contains(MembershipChangedEvent.OwnerSourceField)
                    ? row[MembershipChangedEvent.OwnerSourceField] as EntityReference
                    : null;
            }

            var evt = MembershipChangedEvent.ForRowOwner(
                owner, entityLogicalName, recordId, MembershipMutationType.Added, correlationId);
            if (evt is null)
            {
                logger.LogDebug(
                    "MembershipOwnerEvents: no owner event for {Entity}({RecordId}) — owner absent or not a user/team",
                    entityLogicalName, recordId);
                return null;
            }

            if (evt.PersonIdType == PersonIdentityType.User)
            {
                // The SAME rule MembershipReconciliationJob applies: an application user is not a person and gets no
                // junction row. Unknown (unreadable) is skipped too — the backstop re-decides it on its next run.
                var isApplicationUser = await ApplicationUserCheck
                    .IsApplicationUserAsync(dataverse, evt.PersonId, logger, ct)
                    .ConfigureAwait(false);
                if (isApplicationUser != false)
                {
                    logger.LogInformation(
                        "MembershipOwnerEvents: {Entity}({RecordId}) is owned by {Reason} systemuser {OwnerId} — no owner event "
                        + "(an application user is not a person; reconciliation applies the same rule)",
                        entityLogicalName, recordId,
                        isApplicationUser == true ? "an application" : "an unreadable", evt.PersonId);
                    return null;
                }
            }

            await publisher.PublishAsync(evt, ct).ConfigureAwait(false);
            return evt;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            // Fire-and-forget (ADR-034 Q2): the write already succeeded; the nightly reconciliation is the backstop.
            logger.LogWarning(ex,
                "MembershipOwnerEvents: owner event for {Entity}({RecordId}) not published (correlationId={CorrelationId}); "
                + "reconciliation will converge it",
                entityLogicalName, recordId, correlationId);
            return null;
        }
    }
}
