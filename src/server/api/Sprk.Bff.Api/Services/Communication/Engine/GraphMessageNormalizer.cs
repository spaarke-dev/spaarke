using Microsoft.Graph.Models;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// Maps a channel-specific <see cref="Message"/> (Microsoft Graph) into the channel-neutral
/// <see cref="NormalizedMessage"/> envelope. This is the SINGLE Graph→envelope boundary (FR-09):
/// downstream the Association Engine and enrichment see only <see cref="NormalizedMessage"/>, never
/// <c>Microsoft.Graph.Message</c>.
/// </summary>
/// <remarks>
/// Pure mapping — no I/O. The caller is responsible for fetching the message with the fields this
/// mapper reads (notably <c>internetMessageHeaders</c> for In-Reply-To/References and
/// <c>conversationId</c>), so the thread rung no longer needs a second Graph round-trip.
/// </remarks>
public sealed class GraphMessageNormalizer
{
    /// <summary>Maps a Graph message to the normalized envelope for the given direction.</summary>
    public NormalizedMessage Normalize(Message message, CommunicationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(message);

        var to = AddressesOf(message.ToRecipients);
        var cc = AddressesOf(message.CcRecipients);

        var isHtml = message.Body?.ContentType == BodyType.Html;

        return new NormalizedMessage
        {
            Direction = direction,
            From = message.From?.EmailAddress?.Address,
            To = to,
            Cc = cc,
            Subject = message.Subject,
            BodyText = isHtml ? null : message.Body?.Content,
            BodyHtml = isHtml ? message.Body?.Content : null,
            InternetMessageId = message.InternetMessageId,
            InReplyTo = Header(message, "In-Reply-To"),
            References = ParseReferences(Header(message, "References")),
            ConversationId = message.ConversationId,
            SentAt = message.ReceivedDateTime ?? message.SentDateTime,
            Attachments = MapAttachments(message.Attachments),
        };
    }

    private static IReadOnlyList<string> AddressesOf(IList<Recipient>? recipients) =>
        recipients?
            .Where(r => r.EmailAddress?.Address is not null)
            .Select(r => r.EmailAddress!.Address!)
            .ToArray() ?? Array.Empty<string>();

    private static string? Header(Message message, string name) =>
        message.InternetMessageHeaders?
            .FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    /// <summary>
    /// RFC-2822 References is a single header whose value is a whitespace-separated list of
    /// message-ids (oldest→newest). Split into individual ids; empty when absent.
    /// </summary>
    private static IReadOnlyList<string> ParseReferences(string? referencesHeader)
    {
        if (string.IsNullOrWhiteSpace(referencesHeader))
            return Array.Empty<string>();

        return referencesHeader
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyList<NormalizedAttachment> MapAttachments(IList<Attachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return Array.Empty<NormalizedAttachment>();

        return attachments
            .OfType<FileAttachment>()
            .Select(a => new NormalizedAttachment
            {
                Name = a.Name,
                ContentType = a.ContentType,
                SizeBytes = a.Size,
                IsInline = a.IsInline ?? false,
            })
            .ToArray();
    }
}
