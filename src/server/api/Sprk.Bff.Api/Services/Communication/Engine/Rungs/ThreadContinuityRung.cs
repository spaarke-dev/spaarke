using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Rung 1 — thread continuity. The single highest-value deterministic signal for ongoing matters:
/// if this message is a reply in a thread we have already filed, inherit that thread's regarding.
/// </summary>
/// <remarks>
/// <para>
/// Reimplements the <c>conversationindex</c> correlation of the retired <c>EmailAssociationService</c>
/// as DESIGN (not resurrected code): walk the RFC-2822 ancestry — <see cref="NormalizedMessage.InReplyTo"/>
/// (immediate parent) then <see cref="NormalizedMessage.References"/> from newest→oldest — and take the
/// nearest ancestor that already exists as a <c>sprk_communication</c>. Its regarding lookups are copied
/// verbatim across all supported targets (<see cref="RegardingFieldMap.AllRegardingFields"/>).
/// </para>
/// <para>
/// conversationId note (task 012): <see cref="NormalizedMessage.ConversationId"/> is carried on the
/// envelope, but <c>sprk_communication</c> does not yet persist/query a conversation id, so conversationId
/// -based correlation (a secondary signal to the RFC-2822 chain) is deferred pending a
/// <c>sprk_conversationid</c> field + query. The reference-chain walk covers standards-compliant threading.
/// </para>
/// Runs symmetrically inbound + outbound (reads only the envelope). Emits <see cref="RungMatch"/> with
/// confidence + provenance; task 015 maps confidence → status.
/// </remarks>
public sealed class ThreadContinuityRung : IAssociationRung
{
    private readonly ICommunicationDataverseService _communicationService;

    public ThreadContinuityRung(ICommunicationDataverseService communicationService)
    {
        _communicationService = communicationService;
    }

    public RungKind Kind => RungKind.ThreadContinuity;
    public int Order => 1;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        // Ancestry, nearest first: In-Reply-To (immediate parent), then References newest→oldest.
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.InReplyTo))
            candidates.Add(message.InReplyTo);
        for (var i = message.References.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(message.References[i]))
                candidates.Add(message.References[i]);
        }

        if (candidates.Count == 0)
            return Array.Empty<RungMatch>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!seen.Add(candidate))
                continue;

            var parent = await _communicationService.GetCommunicationByInternetMessageIdAsync(candidate, ct)
                         ?? await _communicationService.GetCommunicationByGraphMessageIdAsync(candidate, ct);
            if (parent is null)
                continue;

            var matches = CopyParentRegarding(parent, candidate);
            if (matches.Count > 0)
                return matches;

            // Parent exists but carries no regarding — stop here (nearest ancestor wins; do not
            // skip past an unassociated parent to an older one).
            return Array.Empty<RungMatch>();
        }

        return Array.Empty<RungMatch>();
    }

    private static List<RungMatch> CopyParentRegarding(Entity parent, string matchedMessageId)
    {
        var matches = new List<RungMatch>();
        foreach (var field in RegardingFieldMap.AllRegardingFields)
        {
            if (parent.Contains(field) && parent[field] is EntityReference parentRef)
            {
                matches.Add(new RungMatch
                {
                    RegardingFieldName = field,
                    Target = parentRef,
                    Confidence = 1.0,
                    Provenance = $"thread:ancestor:{matchedMessageId}→parent:{parent.Id}:{field}",
                    Rung = RungKind.ThreadContinuity,
                });
            }
        }

        return matches;
    }
}
