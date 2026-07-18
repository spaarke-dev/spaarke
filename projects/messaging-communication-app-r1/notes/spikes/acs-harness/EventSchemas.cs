// Documented Event Grid schemas (design §8.3). Used by BOTH the live harness (to assert the
// inbound id matches the send id) and the offline simulator (to prove capture + dedup without
// a live subscription). Schemas per Microsoft.Azure.Communication.CommunicationServices events.
// Source of truth to reconcile against live: aka.ms/acs-eventgrid ChatMessageReceivedInThread.

using System.Text.Json;

internal static class EventSchemas
{
    /// <summary>
    /// The Event Grid SubscriptionValidationEvent sent once at subscription creation.
    /// The webhook MUST echo back { validationResponse = validationCode } within 5 min (§8.3).
    /// </summary>
    public static Dictionary<string, object?> SubscriptionValidationEvent(string validationCode) => new()
    {
        ["id"] = Guid.NewGuid().ToString(),
        ["eventType"] = "Microsoft.EventGrid.SubscriptionValidationEvent",
        ["data"] = new Dictionary<string, object?>
        {
            ["validationCode"] = validationCode,
            ["validationUrl"] = "https://rp-eastus.eventgrid.azure.net/..."
        },
        ["eventTime"] = DateTimeOffset.UtcNow.ToString("o"),
    };

    /// <summary>The handshake response the webhook returns for a SubscriptionValidationEvent.</summary>
    public static Dictionary<string, object?> ValidationResponse(string validationCode) => new()
    {
        ["validationResponse"] = validationCode
    };

    /// <summary>
    /// Microsoft.Communication.ChatMessageReceivedInThread — the inbound capture event.
    /// KEY INVARIANT: data.messageId == the SendChatMessageResult.Id from the outbound send.
    /// That equality is what makes the own-outbound echo dedupable (design §4 / NFR-03).
    /// </summary>
    public static Dictionary<string, object?> ChatMessageReceivedInThread(
        string threadId, string messageId, DateTimeOffset composeTime) => new()
    {
        ["id"] = Guid.NewGuid().ToString(),
        ["eventType"] = "Microsoft.Communication.ChatMessageReceivedInThread",
        ["subject"] = $"thread/{threadId}",
        ["messageId"] = messageId,                 // == send response Id  (echo-dedup key)
        ["threadId"] = threadId,
        ["senderCommunicationIdentifier"] = new Dictionary<string, object?>
        {
            ["rawId"] = "8:acs:...",
            ["communicationUser"] = new Dictionary<string, object?> { ["id"] = "8:acs:..." }
        },
        ["messageBody"] = "Hello from the Spaarke ACS Phase-0 spike.",
        ["messageType"] = "Text",
        ["composeTime"] = composeTime.ToString("o"),
        ["eventTime"] = DateTimeOffset.UtcNow.ToString("o"),
    };

    public static string ToJson(object o) =>
        JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true });
}
