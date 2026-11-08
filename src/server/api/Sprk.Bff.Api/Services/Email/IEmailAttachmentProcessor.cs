namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Decides which email attachments are worth keeping: filters signature images, small images and
/// blocked extensions. The inbound pipeline (IncomingCommunicationProcessor) asks it before it
/// creates an attachment document.
/// </summary>
public interface IEmailAttachmentProcessor
{
    /// <summary>
    /// Check if a file should be filtered out (signature image, too small, blocked extension).
    /// </summary>
    /// <param name="fileName">The attachment filename.</param>
    /// <param name="sizeBytes">The attachment size in bytes.</param>
    /// <param name="contentType">The MIME content type.</param>
    /// <returns>True if the file should be skipped, false if it should be processed.</returns>
    bool ShouldFilterAttachment(string fileName, long sizeBytes, string? contentType);
}
