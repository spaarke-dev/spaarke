using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Communication.Models;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The rules that decide <c>sprk_isinternalonly</c> for a communication whose thread or quoted parent may keep it from
/// partners (external-access r3 task 065, owner decisions 2026-10-13 and 2026-10-14). The external read hides a row whose
/// flag is <c>true</c> or unset, so a row must never be written <c>false</c> while its thread or parent is unknown.
/// </summary>
/// <remarks>
/// <para><b>One floor, two callers.</b> The outbound send (<see cref="CommunicationService"/>) takes the thread decision
/// from <see cref="IsConfirmedOpen"/>; the three inbound writers (<c>MessagingIngestor</c>,
/// <c>EmailUploadCaptureService</c>, <c>IncomingCommunicationProcessor</c>) create their row <c>true</c> and call
/// <see cref="LowerWhenSafeAsync"/> once the thread is known. A received item that joins a Private thread, a thread with
/// no privacy value or an unreadable thread, or that replies to a parent not explicitly shareable, stays <c>true</c>.</para>
/// <para><b>Fail closed.</b> Every read fault here answers "not safe": the row stays <c>true</c>. No fault is thrown;
/// cancellation is the one exception and propagates as <see cref="OperationCanceledException"/>, so a caller that must
/// finish its own work after the row exists catches it (<c>MessagingIngestor</c> does).</para>
/// <para>Visibility to partners then comes from the external read itself (it filters by the regarding record and the
/// caller's access to it) plus this flag. Workforce contacts and CIAM contacts are both non-systemuser callers, so the one
/// read-side filter (<c>CommunicationThreadReadService.FilterExternalMessagesAsync</c>) hides <c>true</c>/<c>null</c> from both.</para>
/// </remarks>
internal static class CommunicationInternalOnlyFloor
{
    internal const string InternalOnlyField = "sprk_isinternalonly";

    /// <summary>The thread table's logical name (the entity <see cref="ThreadPrivacyField"/> lives on).</summary>
    internal const string ThreadEntity = "sprk_communicationthread";

    /// <summary>The thread's privacy column (Open / Private). Read by the write-side floor and by the read-side D7 gate
    /// (<see cref="CommunicationOpenThreadGate"/>); never redefine the name.</summary>
    internal const string ThreadPrivacyField = "sprk_privacystate";

    /// <summary>
    /// OWNER DECISION D6 (2026-10-14, final): an Outlook add-in save that carries NO In-Reply-To and no References stays
    /// visible to partners (it may be lowered to partner-visible when its thread is Open). <c>true</c> is the decision: the
    /// current add-in sends no headers, so the parent is unknown and the capture is treated as having none. Used by the
    /// upload capture and by the filing sweep (<c>ThreadResolver.ResolveMissingThreadAsync</c>) for a row the add-in
    /// created. A save that DOES carry headers is unaffected: it always follows the parent rule.
    /// </summary>
    internal const bool AddInSaveWithoutReplyHeadersIsShareable = true;

    /// <summary>
    /// True only when the thread row reads Open. Private, a missing privacy value and a null row are all false (the same
    /// fail-closed rule as <c>CommunicationFanOutTargetingService.IsExplicitlyOpen</c>).
    /// </summary>
    internal static bool IsConfirmedOpen(DataverseEntity? thread) =>
        thread?.GetAttributeValue<OptionSetValue>(ThreadPrivacyField)?.Value == (int)ThreadPrivacyState.Open;

    /// <summary>Reads the thread's privacy; a null row or a fault is "not confirmed Open".</summary>
    internal static async Task<bool> IsThreadConfirmedOpenAsync(
        IGenericEntityService entities, Guid threadId, ILogger logger, CancellationToken ct)
    {
        try
        {
            var thread = await entities.RetrieveAsync(
                ThreadEntity, threadId, new[] { ThreadPrivacyField }, ct);
            return IsConfirmedOpen(thread);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Could not read thread {ThreadId} privacy; the received communication stays internal-only (fail closed)",
                threadId);
            return false;
        }
    }

    /// <summary>
    /// True when a received message replies to a communication that is NOT explicitly marked shareable: the parent flag is
    /// <c>true</c>, unset, its row is missing, or the lookup faulted. The same raise-only rule as the outbound reply
    /// (<c>CommunicationService.CopyRegardingFromSourceAsync</c>). A message with no known ancestor has no parent: false.
    /// The ancestor is found the way the email thread key strategy finds it (<c>In-Reply-To</c>, then <c>References</c>
    /// newest to oldest; Internet-Message-Id, else Graph message id; the nearest existing one wins).
    /// </summary>
    internal static async Task<bool> HasParentNotExplicitlyShareableAsync(
        ICommunicationDataverseService communications, IGenericEntityService entities, NormalizedMessage message,
        ILogger logger, CancellationToken ct)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.InReplyTo))
            candidates.Add(message.InReplyTo);
        for (var i = message.References.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(message.References[i]))
                candidates.Add(message.References[i]);
        }

        if (candidates.Count == 0)
            return false;

        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                if (!seen.Add(candidate))
                    continue;

                var parent = await communications.GetCommunicationByInternetMessageIdAsync(candidate, ct)
                             ?? await communications.GetCommunicationByGraphMessageIdAsync(candidate, ct);
                if (parent is null)
                    continue;

                var row = await entities.RetrieveAsync("sprk_communication", parent.Id, new[] { InternalOnlyField }, ct);
                return row?.GetAttributeValue<bool?>(InternalOnlyField) != false;
            }

            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Could not read the parent of a received communication; it stays internal-only (fail closed)");
            return true;
        }
    }

    /// <summary>
    /// Lowers a received communication from <c>true</c> (written at create, before its thread was known) to <c>false</c>,
    /// only when its thread was resolved AND reads explicitly Open AND <paramref name="parentNotShareable"/> is false. In
    /// every other case, including a failed update, the row stays <c>true</c>. A fault never escapes; cancellation does
    /// (<see cref="OperationCanceledException"/> propagates).
    /// </summary>
    internal static async Task LowerWhenSafeAsync(
        IGenericEntityService entities, Guid communicationId, Guid? threadId, bool parentNotShareable,
        ILogger logger, CancellationToken ct)
    {
        if (threadId is not { } id || id == Guid.Empty || parentNotShareable)
            return;

        try
        {
            if (!await IsThreadConfirmedOpenAsync(entities, id, logger, ct))
                return;

            await entities.UpdateAsync(
                "sprk_communication", communicationId,
                new Dictionary<string, object> { [InternalOnlyField] = false }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Could not mark received communication {CommunicationId} shareable; it stays internal-only (fail closed)",
                communicationId);
        }
    }
}
