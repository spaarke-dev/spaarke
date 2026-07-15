using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// Rung 1 — thread continuity. If the envelope carries an In-Reply-To parent id, looks up the parent
/// <c>sprk_communication</c> and copies its regarding associations onto the reply.
/// </summary>
/// <remarks>
/// Task-011 adapter wrapping the current <c>TryResolveByThreadAsync</c> logic, envelope-only: the
/// In-Reply-To value now comes from <see cref="NormalizedMessage.InReplyTo"/> (mapped at the boundary
/// from the Graph message's internet headers) instead of a second Graph round-trip. Behavior is
/// otherwise unchanged — it copies exactly matter/organization/person from the parent. Task 012
/// replaces this adapter with the real rungs 0–1.
/// </remarks>
public sealed class ThreadContinuityRung : IAssociationRung
{
    private readonly ICommunicationDataverseService _communicationService;

    /// <summary>Regarding fields copied from the matched parent (exactly as the pre-011 resolver did).</summary>
    private static readonly string[] CopiedRegardingFields =
    [
        "sprk_regardingmatter",
        "sprk_regardingorganization",
        "sprk_regardingperson",
    ];

    public ThreadContinuityRung(ICommunicationDataverseService communicationService)
    {
        _communicationService = communicationService;
    }

    public RungKind Kind => RungKind.ThreadContinuity;
    public int Order => 1;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        var inReplyTo = message.InReplyTo;
        if (string.IsNullOrEmpty(inReplyTo))
            return Array.Empty<RungMatch>();

        // In-Reply-To contains an RFC 2822 internet message id (e.g. <ABC@contoso.com>).
        // Search sprk_internetmessageid first (exact match), fall back to sprk_graphmessageid.
        var parent = await _communicationService.GetCommunicationByInternetMessageIdAsync(inReplyTo, ct)
                     ?? await _communicationService.GetCommunicationByGraphMessageIdAsync(inReplyTo, ct);
        if (parent is null)
            return Array.Empty<RungMatch>();

        var matches = new List<RungMatch>();
        foreach (var field in CopiedRegardingFields)
        {
            if (parent.Contains(field) && parent[field] is EntityReference parentRef)
            {
                matches.Add(new RungMatch
                {
                    RegardingFieldName = field,
                    Target = parentRef,
                    Confidence = 1.0,
                    Provenance = $"thread:in-reply-to→parent:{field}",
                    Rung = RungKind.ThreadContinuity,
                });
            }
        }

        return matches;
    }
}
