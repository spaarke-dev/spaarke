using System.Text.Json;

namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Payload of the <c>IncomingMessaging</c> Service Bus job enqueued by the ACS Event Grid ingress
/// (messaging-communication-app-r1 task 030). This is the <b>contract task 031 consumes</b>: 031's job
/// handler deserializes this payload, normalizes <see cref="RawEvent"/> into a
/// <c>NormalizedMessage</c>, dedupes on <see cref="AcsMessageId"/> (NFR-03), and calls
/// <c>MessagingIngestor.IngestAsync</c> (task 021) to persist the <c>sprk_communication</c> record.
/// </summary>
/// <remarks>
/// Task 030 (the ingress) deliberately does NOT normalize/persist/dedupe — it carries the raw Event Grid
/// event plus the ids 031 needs so the heavy work stays on the queue (Event Grid delivery is at-least-once,
/// unordered, and may duplicate — design §8.3). The idempotency lives in 031's handler.
/// </remarks>
public sealed class IncomingMessagingJobPayload
{
    /// <summary>
    /// ACS message id (Event Grid <c>data.messageId</c>) — the echo-dedup / idempotency key persisted as
    /// <c>sprk_acsmessageid</c> (FR-04 / NFR-03). A numeric string in live ACS (e.g. <c>1784231661455</c>),
    /// NOT a GUID. Null for event types that carry no message (e.g. participant add/remove).
    /// </summary>
    public string? AcsMessageId { get; set; }

    /// <summary>
    /// ACS chat thread id (Event Grid <c>data.threadId</c>) — persisted as the denormalized
    /// <c>sprk_acsthreadid</c>. The <c>sprk_communicationthread</c> lookup is assigned later by task 040.
    /// </summary>
    public string? AcsThreadId { get; set; }

    /// <summary>The Event Grid <c>eventType</c> (e.g. <c>Microsoft.Communication.ChatMessageReceivedInThread</c>).</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The Event Grid event id (top-level <c>id</c>) — used as a fallback idempotency key when no message id is present.</summary>
    public string? EventId { get; set; }

    /// <summary>The Event Grid <c>topic</c> (the ACS system-topic ARM resource id) — validated against the ingress allow-list before enqueue.</summary>
    public string? Topic { get; set; }

    /// <summary>The Event Grid <c>subject</c> (e.g. <c>thread/{threadId}</c>).</summary>
    public string? Subject { get; set; }

    /// <summary>The Event Grid <c>eventTime</c> when present.</summary>
    public DateTimeOffset? EventTime { get; set; }

    /// <summary>Provenance tag identifying the ingress that enqueued this job.</summary>
    public string TriggerSource { get; set; } = "AcsEventGrid";

    /// <summary>
    /// The full raw Event Grid event JSON. Task 031's normalizer maps this into a <c>NormalizedMessage</c>
    /// (sender identity, body, compose time, etc.) — carried verbatim so the ingress stays thin and 031 owns
    /// the channel-event → envelope mapping at the pipeline boundary (ADR-045).
    /// </summary>
    public JsonElement RawEvent { get; set; }
}
