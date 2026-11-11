using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Filters email attachments: signature images, small images and blocked extensions. It creates
/// nothing; the inbound pipeline (IncomingCommunicationProcessor) asks it before it creates an
/// attachment document.
/// </summary>
public class EmailAttachmentProcessor : IEmailAttachmentProcessor
{
    private readonly EmailProcessingOptions _options;
    private readonly ILogger<EmailAttachmentProcessor> _logger;

    private readonly Regex[] _signaturePatterns;

    // Blocked file extensions (security)
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse",
        ".wsf", ".wsh", ".msc", ".scr", ".pif", ".com", ".hta"
    };

    // Image MIME types for size filtering
    private static readonly HashSet<string> ImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/gif", "image/jpeg", "image/jpg", "image/bmp", "image/webp"
    };

    public EmailAttachmentProcessor(
        IOptions<EmailProcessingOptions> options,
        ILogger<EmailAttachmentProcessor> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Pre-compile signature patterns for performance. Timeout is configurable
        // (EmailProcessingOptions.SignatureImageRegexTimeout, default 1s unchanged) so tests can
        // use a budget appropriate to their own execution environment — see task 095.
        _signaturePatterns = _options.SignatureImagePatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled, _options.SignatureImageRegexTimeout))
            .ToArray();
    }

    public bool ShouldFilterAttachment(string fileName, long sizeBytes, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return true;

        // Check blocked extensions
        var extension = Path.GetExtension(fileName);
        if (BlockedExtensions.Contains(extension))
            return true;

        // Check if it's a signature image pattern
        if (IsSignatureImage(fileName))
            return true;

        // Check if it's a small image (likely signature/spacer)
        if (IsSmallImage(fileName, sizeBytes, contentType))
            return true;

        return false;
    }

    private bool IsSignatureImage(string fileName)
    {
        foreach (var pattern in _signaturePatterns)
        {
            try
            {
                if (pattern.IsMatch(fileName))
                    return true;
            }
            catch (RegexMatchTimeoutException)
            {
                _logger.LogWarning(
                    "Regex timeout checking signature pattern for '{FileName}'", fileName);
            }
        }
        return false;
    }

    private bool IsSmallImage(string fileName, long sizeBytes, string? contentType)
    {
        // Check if it's an image by content type or extension
        var isImage = false;

        if (!string.IsNullOrEmpty(contentType) && ImageMimeTypes.Contains(contentType))
        {
            isImage = true;
        }
        else
        {
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            isImage = extension is ".png" or ".gif" or ".jpg" or ".jpeg" or ".bmp" or ".webp";
        }

        if (!isImage)
            return false;

        // Check size threshold (convert KB to bytes)
        var minSizeBytes = _options.MinImageSizeKB * 1024;
        return sizeBytes < minSizeBytes;
    }
}
