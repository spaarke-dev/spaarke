namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Channel-agnostic normalized message envelope consumed by the Association Engine
/// (<see cref="Engine.IAssociationRung"/> rungs) and <see cref="ICommunicationEnrichmentService"/>.
/// Per ADR-045 / FR-09, the engine operates over THIS type only — never
/// <c>Microsoft.Graph.Message</c> or any channel-specific type. Channel messages are mapped to the
/// envelope exactly once, at the pipeline boundary (see
/// <see cref="Engine.GraphMessageNormalizer"/> for the Graph→envelope mapping).
/// </summary>
/// <remarks>
/// FR-09 fields: <c>direction, from, to[], cc[], subject, bodyText, bodyHtml, internetMessageId,
/// inReplyTo, references[], conversationId, sentAt, attachments[]</c>. Finalized in task 011.
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

    /// <summary>
    /// RFC-2822 In-Reply-To header (parent message id) — feeds the thread-continuity rung.
    /// On the inbound path this is read from the Graph message's internet headers at the boundary,
    /// NOT via a second Graph round-trip inside a rung.
    /// </summary>
    public string? InReplyTo { get; init; }

    /// <summary>RFC-2822 References chain (ordered oldest→newest) — feeds the thread-continuity rung.</summary>
    public IReadOnlyList<string> References { get; init; } = Array.Empty<string>();

    /// <summary>Channel conversation/thread id (e.g. Graph <c>conversationId</c>).</summary>
    public string? ConversationId { get; init; }

    /// <summary>When the message was sent/received (UTC).</summary>
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>Attachment descriptors (metadata; see <see cref="NormalizedAttachment"/>).</summary>
    public IReadOnlyList<NormalizedAttachment> Attachments { get; init; } = Array.Empty<NormalizedAttachment>();
}

/// <summary>
/// Attachment descriptor on the normalized envelope. Carries the metadata the deterministic rungs
/// (0–3) need to reason about attachments WITHOUT loading content into memory eagerly. The
/// structural-detector rung (rung 3, task 014) resolves content on demand from the SPE drive/item
/// via the existing document pipeline — it does not read bytes off this descriptor.
/// </summary>
public sealed record NormalizedAttachment
{
    /// <summary>File name including extension.</summary>
    public string? Name { get; init; }

    /// <summary>MIME content type when known.</summary>
    public string? ContentType { get; init; }

    /// <summary>Size in bytes when known.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>True when the attachment is inline (e.g. an embedded signature image) rather than a real file part.</summary>
    public bool IsInline { get; init; }
}
