using Microsoft.Graph.Models;
using Sprk.Bff.Api.Services.Email;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Maps Microsoft Graph FileAttachment objects to EmailAttachmentInfo,
/// preserving compatibility with existing AttachmentFilterService and EmailAttachmentProcessor.
/// </summary>
public static class GraphAttachmentAdapter
{
    /// <summary>
    /// Converts a single Graph FileAttachment to EmailAttachmentInfo.
    /// </summary>
    public static EmailAttachmentInfo ToAttachmentInfo(FileAttachment graphAttachment)
    {
        var contentBytes = graphAttachment.ContentBytes ?? Array.Empty<byte>();
        return new EmailAttachmentInfo
        {
            AttachmentId = Guid.Empty, // Graph attachments don't have Dataverse activity IDs
            FileName = graphAttachment.Name ?? "attachment",
            MimeType = graphAttachment.ContentType ?? "application/octet-stream",
            Content = new MemoryStream(contentBytes),
            SizeBytes = contentBytes.Length,
            IsInline = graphAttachment.IsInline ?? false,
            ContentId = graphAttachment.ContentId,
            ShouldCreateDocument = true // Filtering is done by EmailAttachmentProcessor
        };
    }

    /// <summary>
    /// Converts a collection of Graph attachments (filtering to FileAttachment only).
    /// </summary>
    public static IReadOnlyList<EmailAttachmentInfo> ToAttachmentInfoList(
        IEnumerable<Attachment>? graphAttachments)
    {
        if (graphAttachments is null)
            return Array.Empty<EmailAttachmentInfo>();

        return graphAttachments
            .OfType<FileAttachment>()
            .Where(a => a.ContentBytes is { Length: > 0 })
            .Select(ToAttachmentInfo)
            .ToList()
            .AsReadOnly();
    }
}
