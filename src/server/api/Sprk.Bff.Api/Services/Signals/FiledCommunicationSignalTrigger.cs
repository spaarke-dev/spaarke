using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The association-confirmed Signal trigger for a communication that has just been filed to a matter (ontology D-48, issue
/// #1659; owner decision D-125). A communication classified FIRST and filed to a matter LATER never reaches the classification
/// trigger with a matter, so without this call its matter waits for the nightly pass. Every server-side filing path - the
/// <c>PATCH /api/communications/{id}/filing</c> route, the Office reconcile (<c>ReconciledEmailFiling</c>) and the AI chat tool
/// <c>dataverse.update_record</c> - calls this one helper after its write, so the "is it classified and on a matter" check
/// cannot drift between them.
/// </summary>
/// <remarks>
/// <para><b>Additive, never fails the caller.</b> The filing has already been written. A failed read or enqueue is logged and
/// the nightly pass remains the backstop; nothing here throws.</para>
/// <para><b>Survives the caller's cancellation, never holds the caller long.</b> The filing is written, so a caller that goes
/// away must not drop the trigger: the reads here use their own token, bounded by <see cref="ReadTimeout"/>, never the
/// caller's. A read that fails or times out is logged at Warning with <see cref="SignalTriggerEvents.FilingReadFailed"/>
/// (the trigger family's stable, alertable event id).</para>
/// <para><b>Same rule as the classification path.</b> It reads the same two columns <c>RuleGatedAssessedConsumer</c> reads
/// (<c>sprk_regardingmatter</c>, <c>sprk_triagecategory</c>) from the same app-only service, and enqueues only when the
/// communication is classified AND on a matter. An unclassified row needs nothing: its classification fires its own trigger,
/// which then finds the matter.</para>
/// <para><b>Only a real change, and both ends of a move.</b> A caller that read the matter BEFORE its write
/// (<see cref="ReadMatterAsync"/>) passes it back: a write that left the matter as it was (a PATCH re-sending the same filing)
/// queues nothing - the main queue has no duplicate detection, so each enqueue is a full matter evaluation - and a move A to B,
/// or an unfile from A, also queues A, so A's Work Item is closed now rather than at the nightly closure. Without a "before"
/// (the Office reconcile files only an unfiled communication) the current matter is queued.</para>
/// <para>§11: <i>Existing</i> - <see cref="SignalEvaluationTrigger"/> (the enqueue) and the classification caller's read.
/// <i>Extension</i> - both are reused as is; this only joins them for the filing paths. <i>Cost of doing nothing</i> - a
/// classified-then-filed email's matter waits up to a day for its Work Item (#1659).</para>
/// </remarks>
public static class FiledCommunicationSignalTrigger
{
    private const string CommunicationEntity = "sprk_communication";
    private const string RegardingMatterColumn = "sprk_regardingmatter";
    private const string TriageCategoryColumn = "sprk_triagecategory";

    /// <summary>The longest one app-only read here may hold its caller (same bound as the enqueue,
    /// <see cref="SignalEvaluationTrigger.EnqueueTimeout"/>); on timeout the trigger is skipped and the nightly pass covers it.</summary>
    public static readonly TimeSpan ReadTimeout = SignalEvaluationTrigger.EnqueueTimeout;

    /// <summary>The matter a communication was filed to BEFORE a write. <see cref="Known"/> is false when it was not read
    /// (no trigger registered, or the read failed): the after-write step then cannot tell a change from a repeat and queues
    /// the current matter, never fewer.</summary>
    public readonly record struct MatterBefore(bool Known, Guid? Matter);

    /// <summary>
    /// Reads the matter <paramref name="communicationId"/> is filed to, to compare after the caller's write. Skips the read
    /// (unknown) when no trigger is registered. Never throws.
    /// </summary>
    public static async Task<MatterBefore> ReadMatterAsync(
        IGenericEntityService entities, SignalEvaluationTrigger? signalTrigger, Guid communicationId, ILogger logger)
    {
        if (signalTrigger is null)
            return default;

        var row = await TryReadAsync(entities, communicationId, new[] { RegardingMatterColumn }, logger).ConfigureAwait(false);
        return row is null ? default : new MatterBefore(true, MatterOf(row));
    }

    /// <summary>
    /// After <paramref name="communicationId"/> was filed, queues the matter's Signal re-evaluation when the communication is
    /// classified and its matter changed (see the class remarks), plus the previous matter's when it was moved or unfiled.
    /// Returns whether any job was queued. Never throws.
    /// </summary>
    /// <param name="before">What <see cref="ReadMatterAsync"/> returned before the write; <c>default</c> when not read.</param>
    public static async Task<bool> EnqueueIfClassifiedOnMatterAsync(
        IGenericEntityService entities,
        SignalEvaluationTrigger? signalTrigger,
        Guid communicationId,
        ILogger logger,
        MatterBefore before = default)
    {
        if (signalTrigger is null)
            return false;

        var communication = await TryReadAsync(
            entities, communicationId, new[] { RegardingMatterColumn, TriageCategoryColumn }, logger).ConfigureAwait(false);

        var classified = communication?.GetAttributeValue<EntityReference>(TriageCategoryColumn) is { Id: var category }
                         && category != Guid.Empty;
        if (communication is null || !classified)
            return false;

        var current = MatterOf(communication);
        if (before.Known && current == before.Matter)
            return false; // the write left the matter as it was: nothing to re-evaluate

        var queued = false;
        // CancellationToken.None: the filing is already written; a caller that goes away must not drop the trigger.
        // EnqueueAsync bounds itself (SignalEvaluationTrigger.EnqueueTimeout) and never throws.
        if (current is { } matter)
            queued |= await Enqueue(matter).ConfigureAwait(false);
        if (before is { Known: true, Matter: { } previous } && previous != current)
            queued |= await Enqueue(previous).ConfigureAwait(false);
        return queued;

        async Task<bool> Enqueue(Guid matterId)
        {
            try
            {
                return await signalTrigger.EnqueueAsync(
                    SignalTriggerKind.AssociationConfirmed, "sprk_matter", matterId, communicationId, ct: CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // EnqueueAsync promises never to throw; the filing is written, so a broken promise still must not fail it.
                logger.LogError(SignalTriggerEvents.EnqueueFailed, ex,
                    "[SIGNAL-TRIGGER] queuing the association-confirmed trigger for matter {MatterId} (source {CommunicationId}) " +
                    "threw; the nightly pass will pick it up ({ExceptionType}).",
                    matterId, communicationId, ex.GetType().Name);
                return false;
            }
        }
    }

    private static Guid? MatterOf(Entity communication) =>
        communication.GetAttributeValue<EntityReference>(RegardingMatterColumn) is { Id: var id } && id != Guid.Empty ? id : null;

    /// <summary>One bounded read of the communication; null (and a Warning with the stable event id) when it fails or times out.</summary>
    private static async Task<Entity?> TryReadAsync(
        IGenericEntityService entities, Guid communicationId, string[] columns, ILogger logger)
    {
        try
        {
            using var bound = new CancellationTokenSource(ReadTimeout);
            return await entities.RetrieveAsync(CommunicationEntity, communicationId, columns, bound.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(SignalTriggerEvents.FilingReadFailed, ex,
                "[SIGNAL-TRIGGER] could not check whether filed communication {CommunicationId} is classified on a matter; " +
                "the nightly pass will pick it up ({ExceptionType}).",
                communicationId, ex.GetType().Name);
            return null;
        }
    }
}
