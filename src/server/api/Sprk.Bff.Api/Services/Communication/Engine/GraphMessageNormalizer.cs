using System.Net;
using System.Text.RegularExpressions;
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
        var bodyContent = message.Body?.Content;

        return new NormalizedMessage
        {
            Direction = direction,
            From = message.From?.EmailAddress?.Address,
            To = to,
            Cc = cc,
            Subject = message.Subject,
            // BodyText is the text-consuming rungs' input (AI classification, semantic
            // match). For HTML bodies — the common case — reduce to plain text; leaving
            // it null (the old behavior) made the classifier body-blind, so it saw only
            // the subject line. BodyHtml keeps the original markup for anything that wants it.
            BodyText = isHtml ? HtmlToPlainText(bodyContent) : bodyContent,
            BodyHtml = isHtml ? bodyContent : null,
            InternetMessageId = message.InternetMessageId,
            InReplyTo = Header(message, "In-Reply-To"),
            References = ParseReferences(Header(message, "References")),
            ConversationId = message.ConversationId,
            SentAt = message.ReceivedDateTime ?? message.SentDateTime,
            Attachments = MapAttachments(message.Attachments),
        };
    }

    private static readonly Regex ScriptStylePattern = new(
        "<(script|style)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagPattern = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Reduce an HTML body to plain text for the text-consuming rungs. Drops script/style
    /// blocks, strips tags, decodes entities, and collapses whitespace. Not a full HTML
    /// renderer — deliberately lightweight (no new dependency, per §10 BFF hygiene); it only
    /// needs to be good enough as classifier / semantic-search input. Returns null for a
    /// blank/empty result so downstream "no body" checks still hold.
    /// </summary>
    internal static string? HtmlToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        var text = ScriptStylePattern.Replace(html, " ");
        text = TagPattern.Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = WhitespacePattern.Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
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
