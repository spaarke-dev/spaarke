using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Threads;

/// <summary>
/// Messaging (ACS Chat) thread-key strategy: resolves the thread from the ACS <c>ChatThreadId</c> carried
/// on the message (<see cref="ThreadResolutionRequest.AcsThreadId"/> = the denormalized <c>sprk_acsthreadid</c>).
/// The canonical mapping lives on the <c>sprk_communicationchannelref</c> child table (one row per
/// (thread, channel, external-ref)); this strategy looks up an existing row by <c>sprk_externalref</c> and,
/// when a new thread is created, writes that channel-ref row so subsequent messages on the same ACS thread
/// JOIN it. A message with no ACS thread id has no groupable key → <see cref="ThreadKeyResolution.Skip"/>
/// (no ungroupable orphan thread is created).
/// </summary>
public sealed class MessagingThreadKeyStrategy : IThreadKeyStrategy
{
    private readonly IGenericEntityService _entityService;

    public MessagingThreadKeyStrategy(IGenericEntityService entityService)
        => _entityService = entityService;

    public CommunicationType SupportedType => CommunicationType.Message;

    public async Task<ThreadKeyResolution> ResolveAsync(ThreadResolutionRequest request, CancellationToken ct)
    {
        var acsThreadId = request.AcsThreadId;
        if (string.IsNullOrWhiteSpace(acsThreadId))
            return ThreadKeyResolution.Skip; // no ACS thread id (e.g. outbound R1) → the inbound echo assigns it

        var query = new QueryExpression("sprk_communicationchannelref")
        {
            ColumnSet = new ColumnSet("sprk_thread"),
            TopCount = 1,
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("sprk_externalref", ConditionOperator.Equal, acsThreadId),
                    new ConditionExpression("sprk_channeltype", ConditionOperator.Equal, (int)CommunicationType.Message),
                },
            },
        };

        var result = await _entityService.RetrieveMultipleAsync(query, ct);
        if (result.Entities.Count > 0 &&
            result.Entities[0].GetAttributeValue<EntityReference>("sprk_thread") is { } threadRef)
        {
            return ThreadKeyResolution.Join(threadRef.Id);
        }

        // First message on this ACS thread — create a new thread and (below) its channel-ref row.
        return ThreadKeyResolution.CreateNew;
    }

    public async Task OnThreadCreatedAsync(Guid threadId, ThreadResolutionRequest request, CancellationToken ct)
    {
        var acsThreadId = request.AcsThreadId;
        if (string.IsNullOrWhiteSpace(acsThreadId))
            return; // nothing to link (Skip path never reaches here)

        var channelRef = new Entity("sprk_communicationchannelref")
        {
            ["sprk_name"] = TruncateTo($"ACS: {acsThreadId}", 200),
            ["sprk_thread"] = new EntityReference("sprk_communicationthread", threadId),
            ["sprk_channeltype"] = new OptionSetValue((int)CommunicationType.Message),
            ["sprk_externalref"] = TruncateTo(acsThreadId, 400),
        };

        await _entityService.CreateAsync(channelRef, ct);
    }

    private static string TruncateTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
