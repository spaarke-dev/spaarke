using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The association-confirmed Signal trigger for a communication that has just been filed to a matter (ontology D-48, issue
/// #1659; owner decision D-125). A communication classified FIRST and filed to a matter LATER never reaches the classification
/// trigger with a matter, so without this call its matter waits for the nightly pass. Both live filing paths - the
/// <c>PATCH /api/communications/{id}/filing</c> route and the Office reconcile (<c>ReconciledEmailFiling</c>) - call this one
/// helper after their write, so the "is it classified and on a matter" check cannot drift between them.
/// </summary>
/// <remarks>
/// <para><b>Additive, never fails the caller.</b> The filing has already been written. A failed read or enqueue is logged and
/// the nightly pass remains the backstop; nothing here throws (cancellation included - the write stands either way).</para>
/// <para><b>Same rule as the classification path.</b> It reads the same two columns <c>RuleGatedAssessedConsumer</c> reads
/// (<c>sprk_regardingmatter</c>, <c>sprk_triagecategory</c>) from the same app-only service, and enqueues only when the
/// communication is classified AND on a matter. An unclassified row needs nothing: its classification fires its own trigger,
/// which then finds the matter.</para>
/// <para>§11: <i>Existing</i> - <see cref="SignalEvaluationTrigger"/> (the enqueue) and the classification caller's read.
/// <i>Extension</i> - both are reused as is; this only joins them for the two filing paths. <i>Cost of doing nothing</i> - a
/// classified-then-filed email's matter waits up to a day for its Work Item (#1659).</para>
/// </remarks>
public static class FiledCommunicationSignalTrigger
{
    private const string CommunicationEntity = "sprk_communication";
    private const string RegardingMatterColumn = "sprk_regardingmatter";
    private const string TriageCategoryColumn = "sprk_triagecategory";

    /// <summary>
    /// After <paramref name="communicationId"/> was filed, queues the matter's Signal re-evaluation when the communication is
    /// classified and on a matter. Returns whether a job was queued. Never throws.
    /// </summary>
    public static async Task<bool> EnqueueIfClassifiedOnMatterAsync(
        IGenericEntityService entities,
        SignalEvaluationTrigger? signalTrigger,
        Guid communicationId,
        ILogger logger,
        CancellationToken readCt = default)
    {
        if (signalTrigger is null)
            return false;

        try
        {
            var communication = await entities
                .RetrieveAsync(CommunicationEntity, communicationId, new[] { RegardingMatterColumn, TriageCategoryColumn }, readCt)
                .ConfigureAwait(false);

            var matter = communication?.GetAttributeValue<EntityReference>(RegardingMatterColumn);
            var classified = communication?.GetAttributeValue<EntityReference>(TriageCategoryColumn) is { Id: var category }
                             && category != Guid.Empty;
            if (!classified || matter is null || matter.Id == Guid.Empty)
                return false;

            // CancellationToken.None: the filing is already written; a caller that goes away must not drop the trigger.
            // EnqueueAsync bounds itself (SignalEvaluationTrigger.EnqueueTimeout) and never throws.
            return await signalTrigger
                .EnqueueAsync(SignalTriggerKind.AssociationConfirmed, "sprk_matter", matter.Id, communicationId, ct: CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[SIGNAL-TRIGGER] could not check whether filed communication {CommunicationId} is classified on a matter; " +
                "the nightly pass will pick it up ({ExceptionType}).",
                communicationId, ex.GetType().Name);
            return false;
        }
    }
}
