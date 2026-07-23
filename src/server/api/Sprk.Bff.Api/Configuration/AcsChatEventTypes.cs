namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Canonical Azure Event Grid event types for ACS chat, subscribed by the messaging capture path
/// (design §8.3). These are the exact <c>Microsoft.Communication.*</c> type strings Event Grid
/// filters on; they MUST match the Bicep <c>includedEventTypes</c> in
/// <c>infrastructure/bicep/modules/acs-communication.bicep</c>.
/// </summary>
public static class AcsChatEventTypes
{
    public const string ChatMessageReceivedInThread = "Microsoft.Communication.ChatMessageReceivedInThread";
    public const string ChatMessageEditedInThread = "Microsoft.Communication.ChatMessageEditedInThread";
    public const string ChatMessageDeletedInThread = "Microsoft.Communication.ChatMessageDeletedInThread";
    public const string ParticipantAddedToThread = "Microsoft.Communication.ParticipantAddedToThread";
    public const string ParticipantRemovedFromThread = "Microsoft.Communication.ParticipantRemovedFromThread";

    /// <summary>The chat events messaging subscribes from day one (design §8.3).</summary>
    public static string[] Default =>
    [
        ChatMessageReceivedInThread,
        ChatMessageEditedInThread,
        ChatMessageDeletedInThread,
        ParticipantAddedToThread,
        ParticipantRemovedFromThread,
    ];
}
