using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Communication.Acs;

/// <summary>
/// The thin, transport-boundary logic behind the ACS Event Grid webhook ingress (messaging-communication-app-r1
/// task 030 / FR-02). Given the raw request body of an Event Grid delivery, it:
/// <list type="number">
///   <item>enforces the optional shared-secret (<c>?sig=</c>) defense-in-depth control;</item>
///   <item>answers the Event Grid <b>subscription-validation handshake</b> (echo <c>data.validationCode</c>) so the subscription activates;</item>
///   <item>verifies real chat events originate from an allow-listed ACS system topic (fail-closed origin control); and</item>
///   <item>enqueues one <c>IncomingMessaging</c> job per chat event onto the EXISTING <c>Services/Jobs/</c>
///   Service Bus contract (the same communication queue the email inbound path uses).</item>
/// </list>
/// It is deliberately HTTP-agnostic (takes the body + query secret, returns an <see cref="AcsEventGridIngressOutcome"/>)
/// so the security-boundary behavior is unit-testable without HTTP plumbing; <c>AcsEventGridEndpoints</c> maps the
/// outcome to the wire response.
/// </summary>
/// <remarks>
/// <para><b>Scope (task 030):</b> validate → handshake → enqueue → return fast. It MUST NOT normalize the event,
/// persist <c>sprk_communication</c>, dedupe, run enrichment, or wire the DLQ — that is task 031's job handler,
/// which consumes <see cref="IncomingMessagingJobPayload"/>. Event Grid delivery is at-least-once, unordered, and
/// may duplicate (design §8.3); keeping the ingress thin means retries do not stack — durability + dedupe live on
/// the queue and in 031.</para>
/// <para><b>Security boundary:</b> only a validated delivery enqueues. An unvalidated/wrong-topic/malformed payload
/// never reaches the queue (see the fail-closed <see cref="AcsEventGridIngressOptions.AllowedTopics"/> check).</para>
/// </remarks>
public sealed class AcsEventGridIngressService
{
    /// <summary>Job type routed to task 031's handler. Kept as a public constant so 031 binds to the same string.</summary>
    public const string JobTypeIncomingMessaging = "IncomingMessaging";

    private const string SubscriptionValidationEventType = "Microsoft.EventGrid.SubscriptionValidationEvent";

    private static readonly HashSet<string> ChatEventTypes =
        new(AcsChatEventTypes.Default, StringComparer.OrdinalIgnoreCase);

    private readonly JobSubmissionService _jobSubmissionService;
    private readonly IOptionsMonitor<AcsEventGridIngressOptions> _options;
    private readonly ILogger<AcsEventGridIngressService> _logger;

    public AcsEventGridIngressService(
        JobSubmissionService jobSubmissionService,
        IOptionsMonitor<AcsEventGridIngressOptions> options,
        ILogger<AcsEventGridIngressService> logger)
    {
        _jobSubmissionService = jobSubmissionService ?? throw new ArgumentNullException(nameof(jobSubmissionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes a single Event Grid delivery. Returns an <see cref="AcsEventGridIngressOutcome"/> describing what
    /// the endpoint should return; enqueues jobs as a side effect for validated chat events only.
    /// </summary>
    public async Task<AcsEventGridIngressOutcome> ProcessAsync(
        string? requestBody, string? providedSecret, CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid().ToString();
        var opts = _options.CurrentValue;

        // ─── Control 1: optional shared-secret (?sig=) — enforced on EVERY request when configured ───
        if (!string.IsNullOrEmpty(opts.ValidationKey) && !FixedTimeEquals(providedSecret, opts.ValidationKey))
        {
            _logger.LogWarning(
                "ACS Event Grid ingress rejected: missing/invalid webhook secret. CorrelationId={CorrelationId}",
                correlationId);
            return AcsEventGridIngressOutcome.Rejected("Invalid or missing webhook secret.", correlationId);
        }

        // ─── Parse the Event Grid batch (array, or a single event object) ───
        if (string.IsNullOrWhiteSpace(requestBody))
            return AcsEventGridIngressOutcome.BadRequest("Webhook payload is empty.", correlationId);

        List<JsonElement> events;
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(requestBody);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "ACS Event Grid ingress rejected: malformed JSON. CorrelationId={CorrelationId}", correlationId);
            return AcsEventGridIngressOutcome.BadRequest("Payload is not valid JSON.", correlationId);
        }

        using (doc)
        {
            events = doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => doc.RootElement.EnumerateArray().ToList(),
                JsonValueKind.Object => new List<JsonElement> { doc.RootElement },
                _ => new List<JsonElement>()
            };

            if (events.Count == 0)
                return AcsEventGridIngressOutcome.BadRequest("Payload contains no Event Grid events.", correlationId);

            // ─── Handshake: answer the FIRST subscription-validation event (Event Grid sends it alone) ───
            foreach (var e in events)
            {
                if (!string.Equals(GetString(e, "eventType"), SubscriptionValidationEventType, StringComparison.OrdinalIgnoreCase))
                    continue;

                var validationCode = TryGetData(e, out var data) ? GetString(data, "validationCode") : null;
                if (string.IsNullOrEmpty(validationCode))
                    return AcsEventGridIngressOutcome.BadRequest("Subscription-validation event missing validationCode.", correlationId);

                _logger.LogInformation(
                    "ACS Event Grid subscription-validation handshake answered. CorrelationId={CorrelationId}", correlationId);
                return AcsEventGridIngressOutcome.Handshake(validationCode, correlationId);
            }

            // ─── Control 2 (mandatory, fail-closed): topic-origin allow-list on ALL real events ───
            // Verify BEFORE enqueuing anything so a single untrusted event rejects the whole batch and
            // nothing reaches the queue (mirrors the email webhook's all-or-nothing clientState check).
            if (opts.AllowedTopics is not { Length: > 0 })
            {
                _logger.LogError(
                    "ACS Event Grid ingress misconfigured: Communication:Acs:EventGridIngress:AllowedTopics is empty — " +
                    "cannot verify delivery origin, rejecting. CorrelationId={CorrelationId}", correlationId);
                return AcsEventGridIngressOutcome.Misconfigured(
                    "Webhook topic allow-list is not configured on this server.", correlationId);
            }

            foreach (var e in events)
            {
                var topic = GetString(e, "topic");
                if (!IsAllowedTopic(topic, opts.AllowedTopics))
                {
                    _logger.LogWarning(
                        "ACS Event Grid ingress rejected: untrusted topic '{Topic}'. CorrelationId={CorrelationId}",
                        topic, correlationId);
                    return AcsEventGridIngressOutcome.Rejected("Delivery originates from an untrusted topic.", correlationId);
                }
            }

            // ─── Enqueue one job per chat event; ignore non-chat siblings (thin) ───
            var enqueued = 0;
            foreach (var e in events)
            {
                var eventType = GetString(e, "eventType") ?? string.Empty;
                if (!ChatEventTypes.Contains(eventType))
                    continue;

                var job = BuildJob(e, eventType, GetString(e, "topic"), correlationId);
                await _jobSubmissionService.SubmitCommunicationJobAsync(job, ct);
                enqueued++;

                _logger.LogInformation(
                    "Enqueued IncomingMessaging job {JobId} | EventType={EventType}, AcsMessageId={AcsMessageId}, " +
                    "IdempotencyKey={IdempotencyKey}, CorrelationId={CorrelationId}",
                    job.JobId, eventType, job.SubjectId, job.IdempotencyKey, correlationId);
            }

            _logger.LogInformation(
                "ACS Event Grid ingress accepted: {Received} event(s) received, {Enqueued} enqueued. CorrelationId={CorrelationId}",
                events.Count, enqueued, correlationId);

            return AcsEventGridIngressOutcome.Accepted(events.Count, enqueued, correlationId);
        }
    }

    private static JobContract BuildJob(JsonElement e, string eventType, string? topic, string correlationId)
    {
        TryGetData(e, out var data);

        // Event Grid nests message fields under `data`; fall back to top-level for the flattened
        // offline-simulator fixtures (notes/spikes/acs-harness/EventSchemas.cs).
        var acsMessageId = GetString(data, "messageId") ?? GetString(e, "messageId");
        var acsThreadId = GetString(data, "threadId") ?? GetString(e, "threadId");
        var eventId = GetString(e, "id");
        var subject = GetString(e, "subject");
        DateTimeOffset? eventTime = TryGetDateTimeOffset(e, "eventTime");

        var payload = new IncomingMessagingJobPayload
        {
            AcsMessageId = acsMessageId,
            AcsThreadId = acsThreadId,
            EventType = eventType,
            EventId = eventId,
            Topic = topic,
            Subject = subject,
            EventTime = eventTime,
            TriggerSource = "AcsEventGrid",
            RawEvent = e.Clone()
        };

        // Idempotency key includes the event type so an edit does not collapse onto the original received
        // message at the Service Bus duplicate-detection layer. Full echo-dedup (on sprk_acsmessageid) is 031.
        var dedupeSeed = !string.IsNullOrWhiteSpace(acsMessageId) ? acsMessageId : eventId ?? Guid.NewGuid().ToString();

        return new JobContract
        {
            JobType = JobTypeIncomingMessaging,
            SubjectId = acsMessageId ?? eventId ?? "unknown",
            CorrelationId = correlationId,
            IdempotencyKey = $"Messaging:{eventType}:{dedupeSeed}",
            Payload = JsonSerializer.SerializeToDocument(payload),
            MaxAttempts = 3
        };
    }

    private static bool IsAllowedTopic(string? topic, string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return false;

        foreach (var t in allowed)
        {
            if (string.Equals(t, topic, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool FixedTimeEquals(string? provided, string expected)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided ?? string.Empty);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private static bool TryGetData(JsonElement e, out JsonElement data)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Object)
            return true;
        data = default;
        return false;
    }

    private static string? GetString(JsonElement e, string property)
    {
        if (e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }
        return null;
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement e, string property)
    {
        if (e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.TryGetDateTimeOffset(out var dto))
        {
            return dto;
        }
        return null;
    }
}

/// <summary>The action the endpoint must take for an Event Grid delivery.</summary>
public enum AcsEventGridIngressAction
{
    /// <summary>Subscription-validation handshake — return 200 with <c>{ validationResponse }</c>.</summary>
    ValidationHandshake,

    /// <summary>Real chat events accepted — return 202 (jobs enqueued).</summary>
    Accepted,

    /// <summary>Authenticity failed (bad secret / untrusted topic) — return 401 ProblemDetails; nothing enqueued.</summary>
    Rejected,

    /// <summary>Malformed/empty payload — return 400 ProblemDetails; nothing enqueued.</summary>
    BadRequest,

    /// <summary>Server cannot verify origin (allow-list unconfigured) — return 500 ProblemDetails; nothing enqueued.</summary>
    Misconfigured
}

/// <summary>Outcome of <see cref="AcsEventGridIngressService.ProcessAsync"/> — mapped to the wire response by the endpoint.</summary>
public sealed record AcsEventGridIngressOutcome
{
    public required AcsEventGridIngressAction Action { get; init; }
    public string? ValidationCode { get; init; }
    public int ReceivedCount { get; init; }
    public int EnqueuedCount { get; init; }
    public string? Detail { get; init; }
    public required string CorrelationId { get; init; }

    public static AcsEventGridIngressOutcome Handshake(string validationCode, string correlationId) => new()
    { Action = AcsEventGridIngressAction.ValidationHandshake, ValidationCode = validationCode, CorrelationId = correlationId };

    public static AcsEventGridIngressOutcome Accepted(int received, int enqueued, string correlationId) => new()
    { Action = AcsEventGridIngressAction.Accepted, ReceivedCount = received, EnqueuedCount = enqueued, CorrelationId = correlationId };

    public static AcsEventGridIngressOutcome Rejected(string detail, string correlationId) => new()
    { Action = AcsEventGridIngressAction.Rejected, Detail = detail, CorrelationId = correlationId };

    public static AcsEventGridIngressOutcome BadRequest(string detail, string correlationId) => new()
    { Action = AcsEventGridIngressAction.BadRequest, Detail = detail, CorrelationId = correlationId };

    public static AcsEventGridIngressOutcome Misconfigured(string detail, string correlationId) => new()
    { Action = AcsEventGridIngressAction.Misconfigured, Detail = detail, CorrelationId = correlationId };
}
