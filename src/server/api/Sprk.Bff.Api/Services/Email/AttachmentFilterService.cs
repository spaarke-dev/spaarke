using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Service for filtering email attachments to exclude noise (signature logos, tracking pixels,
/// calendar files, and small inline images). Filters are configurable via EmailProcessingOptions.
/// </summary>
public class AttachmentFilterService
{
    private readonly EmailProcessingOptions _options;
    private readonly ILogger<AttachmentFilterService> _logger;

    // Pre-compiled regex patterns for performance
    private readonly Regex[] _signaturePatterns;
    private readonly Regex[] _trackingPixelPatterns;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // Image MIME types for size-based filtering
    private static readonly HashSet<string> ImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/gif", "image/jpeg", "image/jpg", "image/bmp", "image/webp"
    };

    public AttachmentFilterService(
        IOptions<EmailProcessingOptions> options,
        ILogger<AttachmentFilterService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Pre-compile signature patterns for performance
        _signaturePatterns = _options.SignatureImagePatterns
            .Select(p => CreateSafeRegex(p, "signature"))
            .Where(r => r != null)
            .Cast<Regex>()
            .ToArray();

        // Pre-compile tracking pixel patterns
        _trackingPixelPatterns = _options.TrackingPixelPatterns
            .Select(p => CreateSafeRegex(p, "tracking"))
            .Where(r => r != null)
            .Cast<Regex>()
            .ToArray();
    }

    /// <summary>
    /// Filter a list of attachments, returning only those that should be processed as documents.
    /// </summary>
    /// <param name="attachments">The attachments to filter.</param>
    /// <returns>Filtered list of attachments that should become documents.</returns>
    public IReadOnlyList<EmailAttachmentInfo> FilterAttachments(IEnumerable<EmailAttachmentInfo> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        var result = new List<EmailAttachmentInfo>();
        var totalCount = 0;
        var filteredCount = 0;

        foreach (var attachment in attachments)
        {
            totalCount++;
            var (shouldFilter, reason) = ShouldFilterAttachment(attachment);

            if (shouldFilter)
            {
                filteredCount++;
                _logger.LogDebug(
                    "Filtering attachment '{FileName}': {Reason}",
                    attachment.FileName, reason);
            }
            else
            {
                result.Add(attachment);
            }
        }

        _logger.LogDebug(
            "Filtered {FilteredCount} of {TotalCount} attachments, {ResultCount} remaining",
            filteredCount, totalCount, result.Count);

        return result;
    }

    /// <summary>
    /// Determine if an attachment should be filtered out.
    /// </summary>
    /// <param name="attachment">The attachment to evaluate.</param>
    /// <returns>Tuple of (shouldFilter, reason).</returns>
    public (bool ShouldFilter, string? Reason) ShouldFilterAttachment(EmailAttachmentInfo attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        // Already marked as shouldn't create document
        if (!attachment.ShouldCreateDocument)
        {
            return (true, attachment.SkipReason ?? "Pre-filtered");
        }

        // Check empty filename
        if (string.IsNullOrWhiteSpace(attachment.FileName))
        {
            return (true, "Empty filename");
        }

        var fileName = attachment.FileName;
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

        // Check blocked extensions
        if (_options.BlockedAttachmentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return (true, $"Blocked extension: {extension}");
        }

        // Check calendar files
        if (_options.FilterCalendarFiles &&
            _options.CalendarFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return (true, $"Calendar file: {extension}");
        }

        // Check inline attachments
        if (_options.FilterInlineAttachments && attachment.IsInline)
        {
            return (true, "Inline attachment");
        }

        // Check max size
        if (attachment.SizeBytes > _options.MaxAttachmentSizeBytes)
        {
            return (true, $"Exceeds max size: {attachment.SizeBytes / 1024 / 1024}MB > {_options.MaxAttachmentSizeMB}MB");
        }

        // Check tracking pixel patterns
        if (IsTrackingPixel(fileName))
        {
            return (true, "Matches tracking pixel pattern");
        }

        // Image-specific filters
        if (IsImageMimeType(attachment.MimeType))
        {
            // Check signature image patterns
            if (IsSignatureImage(fileName))
            {
                return (true, "Matches signature image pattern");
            }

            // Check small image size
            if (attachment.SizeBytes < _options.MinImageSizeKB * 1024)
            {
                return (true, $"Small image ({attachment.SizeBytes / 1024}KB < {_options.MinImageSizeKB}KB threshold)");
            }
        }

        return (false, null);
    }

    /// <summary>
    /// Check if a filename matches known signature image patterns.
    /// </summary>
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
                _logger.LogWarning("Regex timeout checking signature pattern for '{FileName}'", fileName);
            }
        }
        return false;
    }

    /// <summary>
    /// Check if a filename matches known tracking pixel patterns.
    /// </summary>
    private bool IsTrackingPixel(string fileName)
    {
        foreach (var pattern in _trackingPixelPatterns)
        {
            try
            {
                if (pattern.IsMatch(fileName))
                    return true;
            }
            catch (RegexMatchTimeoutException)
            {
                _logger.LogWarning("Regex timeout checking tracking pixel pattern for '{FileName}'", fileName);
            }
        }
        return false;
    }

    /// <summary>
    /// Check if a MIME type is an image type.
    /// </summary>
    private static bool IsImageMimeType(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
            return false;

        return ImageMimeTypes.Contains(mimeType) ||
               mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Create a compiled regex with error handling.
    /// </summary>
    private Regex? CreateSafeRegex(string pattern, string patternType)
    {
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid {PatternType} regex pattern: {Pattern}", patternType, pattern);
            return null;
        }
    }
}
