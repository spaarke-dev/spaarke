namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Channel-agnostic normalized message envelope consumed by the Association Engine
/// and <see cref="ICommunicationEnrichmentService"/>. Per ADR-045, the engine operates
/// over THIS type only — never <c>Microsoft.Graph.Message</c> or any channel-specific type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Skeleton (task 010).</b> This is the minimal FR-09 envelope introduced so the
/// direction-agnostic enrichment entry point can land first. Task 011 (Association Engine
/// refactor) FINALIZES the shape: it adds per-attribute provenance, the attachment-content
/// contract used by structural detectors (rung 3), and the <c>conversationindex</c>-derived
/// thread key. Do NOT treat the current field set as stable until 011.
/// </para>
/// <para>
/// FR-09 fields: <c>direction, from, to[], cc[], subject, bodyText, bodyHtml,
/// internetMessageId, inReplyTo, references[], conversationId, sentAt, attachments[]</c>.
/// </para>
/// </remarks>
public sealed record NormalizedMessage
{
    /// <summary>Direction of the communication (inbound vs outbound).</summary>
    public required CommunicationDirection Direction { get; init; }

    /// <summary>Sender address (single).</summary>
    public string? From { get; init; }

    /// <summary>To recipients.</summary>
    public IReadOnlyList<string> To { get; init; } = Array.Empty<string>();

    /// <summary>Cc recipients.</summary>
    public IReadOnlyList<string> Cc { get; init; } = Array.Empty<string>();

    /// <summary>Subject line.</summary>
    public string? Subject { get; init; }

    /// <summary>Plain-text body (channel-normalized).</summary>
    public string? BodyText { get; init; }

    /// <summary>HTML body when available.</summary>
    public string? BodyHtml { get; init; }

    /// <summary>RFC-2822 Internet-Message-Id of this message (when known).</summary>
    public string? InternetMessageId { get; init; }

    /// <summary>RFC-2822 In-Reply-To header (parent message id) — feeds thread rung (1).</summary>
    public string? InReplyTo { get; init; }

    /// <summary>RFC-2822 References chain — feeds thread rung (1).</summary>
    public IReadOnlyList<string> References { get; init; } = Array.Empty<string>();

    /// <summary>Channel conversation/thread id (e.g. Graph <c>conversationId</c>).</summary>
    public string? ConversationId { get; init; }

    /// <summary>When the message was sent/received (UTC).</summary>
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>Attachment metadata (skeleton — content contract finalized in task 011).</summary>
    public IReadOnlyList<NormalizedAttachment> Attachments { get; init; } = Array.Empty<NormalizedAttachment>();
}

/// <summary>
/// Minimal attachment descriptor on the normalized envelope. Skeleton for task 010;
/// task 011 adds the content-access contract used by structural detectors (rung 3).
/// </summary>
public sealed record NormalizedAttachment
{
    /// <summary>File name including extension.</summary>
    public string? Name { get; init; }

    /// <summary>MIME content type when known.</summary>
    public string? ContentType { get; init; }

    /// <summary>Size in bytes when known.</summary>
    public long? SizeBytes { get; init; }
}
