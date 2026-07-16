using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Threads;

/// <summary>
/// Email thread-key strategy: resolves the thread from RFC-2822 reply-chain ancestry — the SAME signal
/// <see cref="Engine.Rungs.ThreadContinuityRung"/> already walks (<c>In-Reply-To</c>, then <c>References</c>
/// newest→oldest), taking the nearest existing ancestor <c>sprk_communication</c>. No new matching logic.
/// If that ancestor already carries a <c>sprk_communicationthread</c> the reply JOINs it; otherwise (ancestor
/// without a thread, or no ancestor at all — a fresh email) a NEW thread is created. Point-forward: a
/// pre-thread ancestor is NOT backfilled — the reply starts a new thread that later replies converge onto.
/// </summary>
public sealed class EmailThreadKeyStrategy : IThreadKeyStrategy
{
    private readonly ICommunicationDataverseService _communicationService;
    private readonly IGenericEntityService _entityService;

    public EmailThreadKeyStrategy(
        ICommunicationDataverseService communicationService,
        IGenericEntityService entityService)
    {
        _communicationService = communicationService;
        _entityService = entityService;
    }

    public CommunicationType SupportedType => CommunicationType.Email;

    public async Task<ThreadKeyResolution> ResolveAsync(ThreadResolutionRequest request, CancellationToken ct)
    {
        var message = request.Message;

        // Ancestry, nearest first: In-Reply-To (immediate parent), then References newest→oldest —
        // identical ordering to ThreadContinuityRung (no second walk semantics introduced).
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.InReplyTo))
            candidates.Add(message.InReplyTo);
        for (var i = message.References.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(message.References[i]))
                candidates.Add(message.References[i]);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!seen.Add(candidate))
                continue;

            var parent = await _communicationService.GetCommunicationByInternetMessageIdAsync(candidate, ct)
                         ?? await _communicationService.GetCommunicationByGraphMessageIdAsync(candidate, ct);
            if (parent is null)
                continue;

            // Nearest existing ancestor wins (mirror the rung — do not skip past it to an older one).
            // The ancestry query above does NOT project the thread lookup, so read it explicitly.
            var parentThreadId = await GetThreadLookupAsync(parent.Id, ct);
            return parentThreadId is { } id
                ? ThreadKeyResolution.Join(id)          // reply joins the ancestor's thread
                : ThreadKeyResolution.CreateNew;        // ancestor predates threads → new thread (point-forward)
        }

        // Not a reply (no known ancestor) — a fresh email is a new thread.
        return ThreadKeyResolution.CreateNew;
    }

    public Task OnThreadCreatedAsync(Guid threadId, ThreadResolutionRequest request, CancellationToken ct)
        => Task.CompletedTask; // email needs no channel-ref row (the reply chain is the thread key)

    private async Task<Guid?> GetThreadLookupAsync(Guid communicationId, CancellationToken ct)
    {
        var comm = await _entityService.RetrieveAsync(
            "sprk_communication", communicationId, new[] { "sprk_communicationthread" }, ct);
        return comm.GetAttributeValue<EntityReference>("sprk_communicationthread")?.Id;
    }
}
