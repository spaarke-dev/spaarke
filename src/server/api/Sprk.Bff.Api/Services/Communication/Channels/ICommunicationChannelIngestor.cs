using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Channels;

/// <summary>
/// Channel-extensibility seam for the "ingest" (inbound-capture) leg of a communication — the inbound
/// counterpart to <see cref="ICommunicationChannelSender"/> and <see cref="ICommunicationArchiver"/>
/// (ADR-045 rule 4 / NFR-04). Completes the channel-seam triad ADR-045 STATED but left unbuilt: R4 shipped
/// the OUTBOUND legs (sender + archiver) but left inbound CONCRETE and email-only (the Graph-specific
/// <see cref="IncomingCommunicationProcessor"/>). This seam makes inbound capture channel-agnostic — one
/// implementation per <see cref="CommunicationType"/>, keyed by <see cref="SupportedType"/> and resolved by
/// <see cref="CommunicationChannelDispatcher.ResolveIngestor"/>, exactly as the sender/archiver seams are.
///
/// An implementation accepts a channel-normalized inbound message (a <see cref="NormalizedMessage"/> envelope,
/// the SAME type the Association Engine + enrichment already operate over) and persists it as a
/// <c>sprk_communication</c> record, then hands off to the shared enrichment/association path. The channel
/// event → <see cref="NormalizedMessage"/> mapping happens ONCE at the pipeline boundary (messaging: task 031's
/// normalizer), NOT inside the ingestor — so no <c>Azure.Communication.*</c> / <c>Microsoft.Graph.Message</c>
/// type ever crosses this seam (ADR-045). Adding a future inbound channel (SMS in R2) is purely additive:
/// register a new ingestor keyed to its own <see cref="SupportedType"/> — the engine, enrichment service,
/// regarding model, and review UI do NOT change (NFR-04).
/// </summary>
public interface ICommunicationChannelIngestor
{
    /// <summary>The <see cref="CommunicationType"/> this ingestor handles (dispatch key).</summary>
    CommunicationType SupportedType { get; }

    /// <summary>
    /// Persists a channel-normalized inbound message as a <c>sprk_communication</c> record (Direction=Incoming)
    /// and invokes shared enrichment/association BEST-EFFORT (NFR-02 — an enrichment failure MUST NOT fail the
    /// persist; Dataverse is the record). Implementations MUST NOT re-fetch from the channel provider — the
    /// <see cref="NormalizedMessage"/> is already materialized at the pipeline boundary by the caller's
    /// normalizer (messaging: task 031). Idempotent dedupe on the provider message id + DLQ wiring are the
    /// CALLER's concern (task 031), which fills the <see cref="ChannelIngestResult.WasDuplicate"/> short-circuit.
    /// </summary>
    Task<ChannelIngestResult> IngestAsync(ChannelIngestRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Channel-neutral input to <see cref="ICommunicationChannelIngestor.IngestAsync"/>. Carries the normalized
/// envelope plus the provider-assigned ids captured at the pipeline boundary, so no channel provider type
/// appears in the seam signature (ADR-045). Mirrors <see cref="ChannelSendRequest"/>'s DTO discipline.
/// </summary>
public sealed record ChannelIngestRequest
{
    /// <summary>The channel-normalized inbound message envelope (see <see cref="NormalizedMessage"/>).</summary>
    public required NormalizedMessage Message { get; init; }

    /// <summary>
    /// Provider-assigned message id — the echo-dedup / idempotency key persisted as <c>sprk_acsmessageid</c>
    /// for the messaging channel (ACS <c>SendChatMessageResult.Id</c>; FR-04 / NFR-03). Channel-neutral name:
    /// another channel supplies its own provider id here. Null when the provider returns none.
    /// </summary>
    public string? ProviderMessageId { get; init; }

    /// <summary>
    /// Provider-assigned thread/conversation id when the channel is thread-based — persisted as the
    /// denormalized <c>sprk_acsthreadid</c> for the messaging channel (the ACS <c>ChatThreadId</c>). The
    /// message → thread LOOKUP (<c>sprk_communicationthread</c>) is assigned later by the thread resolver
    /// (task 040); this seam only carries the raw transport id. Null for channels with no thread concept.
    /// </summary>
    public string? ProviderThreadId { get; init; }

    /// <summary>Correlation id for tracing (mirrors <see cref="ChannelSendRequest.CorrelationId"/>).</summary>
    public required string CorrelationId { get; init; }
}

/// <summary>Channel-neutral result of an ingest. Mirrors <see cref="ChannelSendResult"/>'s DTO discipline.</summary>
public sealed record ChannelIngestResult
{
    /// <summary>The persisted (or, on a dedupe hit, pre-existing) <c>sprk_communication</c> record id.</summary>
    public required Guid CommunicationId { get; init; }

    /// <summary>
    /// True when the message was recognized as an already-captured duplicate and NO new record was created.
    /// The R1 skeleton always persists (returns false); the idempotent-dedupe short-circuit that sets this
    /// true is wired by task 031 (dedupe on <c>sprk_acsmessageid</c>, NFR-03).
    /// </summary>
    public bool WasDuplicate { get; init; }
}
