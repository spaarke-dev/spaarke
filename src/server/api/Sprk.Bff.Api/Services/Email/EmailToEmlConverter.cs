using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using MimeKit;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Converts Dataverse Email activities to RFC 5322 compliant .eml files using MimeKit.
/// Fetches email data and attachments from Dataverse Web API.
/// </summary>
public class EmailToEmlConverter : IEmailToEmlConverter
{
    private readonly HttpClient _httpClient;
    private readonly EmailProcessingOptions _options;
    private readonly ILogger<EmailToEmlConverter> _logger;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;
    private AccessToken? _currentToken;
    private readonly string _apiUrl;

    // Dataverse email direction values
    private const int DirectionReceived = 100000000;
    private const int DirectionSent = 100000001;

    /// <summary>
    /// Creates a new EmailToEmlConverter with default credential from configuration.
    /// </summary>
    public EmailToEmlConverter(
        HttpClient httpClient,
        IOptions<EmailProcessingOptions> options,
        IConfiguration configuration,
        ILogger<EmailToEmlConverter> logger)
        : this(httpClient, options, configuration, logger, credential: null)
    {
    }

    /// <summary>
    /// Creates a new EmailToEmlConverter with an optional custom TokenCredential (for testing).
    /// </summary>
    internal EmailToEmlConverter(
        HttpClient httpClient,
        IOptions<EmailProcessingOptions> options,
        IConfiguration configuration,
        ILogger<EmailToEmlConverter> logger,
        TokenCredential? credential)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        // Important: BaseAddress must end with '/' for relative URIs to combine correctly
        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/";

        // Use provided credential or create from configuration
        if (credential != null)
        {
            _credential = credential;
        }
        else
        {
            var tenantId = configuration["TENANT_ID"]
                ?? throw new InvalidOperationException("TENANT_ID configuration is required");
            var clientId = configuration["API_APP_ID"]
                ?? throw new InvalidOperationException("API_APP_ID configuration is required");
            var clientSecret = configuration["Dataverse:ClientSecret"]
                ?? throw new InvalidOperationException("Dataverse:ClientSecret configuration is required");

            _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        }

        _httpClient.BaseAddress = new Uri(_apiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<EmlConversionResult> ConvertToEmlAsync(
        Guid emailActivityId,
        bool includeAttachments = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);

            _logger.LogInformation("Converting email {EmailId} to .eml format", emailActivityId);

            // Fetch email activity from Dataverse
            var email = await FetchEmailActivityAsync(emailActivityId, cancellationToken);
            if (email == null)
            {
                return EmlConversionResult.Failed($"Email activity {emailActivityId} not found");
            }

            // Fetch attachments if requested
            var attachments = new List<EmailAttachmentInfo>();
            if (includeAttachments)
            {
                attachments = await FetchAttachmentsAsync(emailActivityId, cancellationToken);
            }

            // Build MimeMessage
            var message = BuildMimeMessage(email, attachments);

            // Write to stream
            var stream = new MemoryStream();
            await message.WriteToAsync(stream, cancellationToken);
            stream.Position = 0;

            var metadata = BuildMetadata(email, emailActivityId);

            _logger.LogInformation(
                "Successfully converted email {EmailId} to .eml ({Size} bytes, {AttachmentCount} attachments)",
                emailActivityId, stream.Length, attachments.Count);

            return EmlConversionResult.Succeeded(stream, metadata, attachments, stream.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert email {EmailId} to .eml", emailActivityId);
            return EmlConversionResult.Failed($"Conversion failed: {ex.Message}");
        }
    }

    public async Task<string> GenerateEmlFileNameAsync(
        Guid emailActivityId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var email = await FetchEmailActivityAsync(emailActivityId, cancellationToken);
        if (email == null)
        {
            return $"{emailActivityId}.eml";
        }

        var date = email.ActualEnd ?? email.CreatedOn ?? DateTime.UtcNow;
        var subject = email.Subject ?? "No Subject";

        // Sanitize subject for filename
        var sanitized = SanitizeFileName(subject);

        // Format: YYYY-MM-DD_Subject.eml
        var fileName = $"{date:yyyy-MM-dd}_{sanitized}.eml";

        // Truncate if too long
        if (fileName.Length > _options.MaxEmlFileNameLength)
        {
            var prefix = $"{date:yyyy-MM-dd}_";
            var maxSubjectLength = _options.MaxEmlFileNameLength - prefix.Length - 4; // -4 for ".eml"
            sanitized = sanitized[..Math.Min(sanitized.Length, maxSubjectLength)];
            fileName = $"{prefix}{sanitized}.eml";
        }

        return fileName;
    }

    private MimeMessage BuildMimeMessage(EmailActivityData email, List<EmailAttachmentInfo> attachments)
    {
        var message = new MimeMessage();

        // Set headers
        if (!string.IsNullOrEmpty(email.Sender))
        {
            try
            {
                message.From.Add(MailboxAddress.Parse(email.Sender));
            }
            catch
            {
                message.From.Add(new MailboxAddress(email.Sender, email.Sender));
            }
        }

        // Parse To recipients
        if (!string.IsNullOrEmpty(email.ToRecipients))
        {
            foreach (var recipient in ParseRecipients(email.ToRecipients))
            {
                message.To.Add(recipient);
            }
        }

        // Parse CC recipients
        if (!string.IsNullOrEmpty(email.CcRecipients))
        {
            foreach (var recipient in ParseRecipients(email.CcRecipients))
            {
                message.Cc.Add(recipient);
            }
        }

        // Subject
        message.Subject = email.Subject ?? "(No Subject)";

        // Date
        message.Date = email.ActualEnd ?? email.CreatedOn ?? DateTimeOffset.UtcNow;

        // Message-ID (generate one if not present)
        message.MessageId = email.MessageId ?? $"<{Guid.NewGuid()}@spaarke.local>";

        // Build body
        var builder = new BodyBuilder();

        // Set body content (HTML preferred, fallback to plain text)
        if (!string.IsNullOrEmpty(email.Description))
        {
            // Dataverse stores email body as HTML in the description field
            if (email.Description.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                email.Description.Contains("<body", StringComparison.OrdinalIgnoreCase) ||
                email.Description.Contains("<p>", StringComparison.OrdinalIgnoreCase))
            {
                builder.HtmlBody = email.Description;
                // Generate plain text version
                builder.TextBody = StripHtml(email.Description);
            }
            else
            {
                builder.TextBody = email.Description;
            }
        }

        // Add attachments
        foreach (var attachment in attachments.Where(a => a.Content != null))
        {
            var data = ReadStreamToByteArray(attachment.Content!);
            builder.Attachments.Add(attachment.FileName, data, MimeKit.ContentType.Parse(attachment.MimeType));
        }

        message.Body = builder.ToMessageBody();

        return message;
    }

    private EmailActivityMetadata BuildMetadata(EmailActivityData email, Guid activityId)
    {
        // Determine direction based on directioncode
        var direction = email.DirectionCode == true ? DirectionReceived : DirectionSent;

        return new EmailActivityMetadata
        {
            ActivityId = activityId,
            Subject = email.Subject ?? string.Empty,
            From = email.Sender ?? string.Empty,
            To = email.ToRecipients ?? string.Empty,
            Cc = email.CcRecipients,
            Body = TruncateBody(email.Description),
            MessageId = email.MessageId,
            Direction = direction,
            EmailDate = email.ActualEnd ?? email.CreatedOn,
            TrackingToken = email.TrackingToken,
            ConversationIndex = email.ConversationIndex,
            RegardingObjectId = email.RegardingObjectId,
            RegardingObjectType = email.RegardingObjectType
        };
    }

    private async Task<EmailActivityData?> FetchEmailActivityAsync(
        Guid emailActivityId,
        CancellationToken cancellationToken)
    {
        // Note: ccrecipients field doesn't exist on email entity - CC recipients are in activityparty
        var select = "$select=subject,description,sender,torecipients,messageid," +
                     "directioncode,actualend,createdon,trackingtoken,conversationindex," +
                     "_regardingobjectid_value";

        var url = $"emails({emailActivityId})?{select}";

        _logger.LogInformation("Fetching email from Dataverse: {BaseUrl}{Url}", _httpClient.BaseAddress, url);

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            // Log the error response body for debugging
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Dataverse API error {StatusCode} for email {EmailId}: {ErrorBody}",
                (int)response.StatusCode, emailActivityId, errorBody);

            response.EnsureSuccessStatusCode();
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        return new EmailActivityData
        {
            Subject = GetStringOrNull(data, "subject"),
            Description = GetStringOrNull(data, "description"),
            Sender = GetStringOrNull(data, "sender"),
            ToRecipients = GetStringOrNull(data, "torecipients"),
            CcRecipients = null, // CC recipients need to be fetched from activityparty if needed
            MessageId = GetStringOrNull(data, "messageid"),
            DirectionCode = GetBoolOrNull(data, "directioncode"),
            ActualEnd = GetDateTimeOrNull(data, "actualend"),
            CreatedOn = GetDateTimeOrNull(data, "createdon"),
            TrackingToken = GetStringOrNull(data, "trackingtoken"),
            ConversationIndex = GetStringOrNull(data, "conversationindex"),
            RegardingObjectId = GetGuidOrNull(data, "_regardingobjectid_value"),
            RegardingObjectType = GetStringOrNull(data, "_regardingobjectid_value@Microsoft.Dynamics.CRM.lookuplogicalname")
        };
    }

    private async Task<List<EmailAttachmentInfo>> FetchAttachmentsAsync(
        Guid emailActivityId,
        CancellationToken cancellationToken)
    {
        var attachments = new List<EmailAttachmentInfo>();

        var url = $"activitymimeattachments?$filter=_objectid_value eq {emailActivityId}" +
                  "&$select=activitymimeattachmentid,filename,mimetype,filesize,body";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        if (!data.TryGetProperty("value", out var items))
        {
            return attachments;
        }

        foreach (var item in items.EnumerateArray())
        {
            var attachmentId = GetGuidOrNull(item, "activitymimeattachmentid") ?? Guid.Empty;
            var fileName = GetStringOrNull(item, "filename") ?? "attachment";
            var mimeType = GetStringOrNull(item, "mimetype") ?? "application/octet-stream";
            var fileSize = item.TryGetProperty("filesize", out var fs) ? fs.GetInt64() : 0;
            var bodyBase64 = GetStringOrNull(item, "body");

            // Check if attachment should be skipped
            var (shouldCreate, skipReason) = EvaluateAttachment(fileName, mimeType, fileSize);

            Stream? content = null;
            if (!string.IsNullOrEmpty(bodyBase64))
            {
                var bytes = Convert.FromBase64String(bodyBase64);
                content = new MemoryStream(bytes);
            }

            attachments.Add(new EmailAttachmentInfo
            {
                AttachmentId = attachmentId,
                FileName = fileName,
                MimeType = mimeType,
                SizeBytes = fileSize,
                Content = content,
                ShouldCreateDocument = shouldCreate,
                SkipReason = skipReason
            });
        }

        _logger.LogDebug(
            "Fetched {Count} attachments for email {EmailId}, {SkipCount} will be skipped",
            attachments.Count, emailActivityId, attachments.Count(a => !a.ShouldCreateDocument));

        return attachments;
    }

    private (bool shouldCreate, string? skipReason) EvaluateAttachment(
        string fileName, string mimeType, long sizeBytes)
    {
        // Check blocked extensions
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;
        if (_options.BlockedAttachmentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return (false, $"Blocked extension: {extension}");
        }

        // Check max size
        if (sizeBytes > _options.MaxAttachmentSizeBytes)
        {
            return (false, $"Exceeds max size: {sizeBytes / 1024 / 1024}MB > {_options.MaxAttachmentSizeMB}MB");
        }

        // Check signature image patterns
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            // Check size threshold for images
            if (sizeBytes < _options.MinImageSizeKB * 1024)
            {
                return (false, $"Image too small ({sizeBytes / 1024}KB), likely signature/spacer");
            }

            // Check filename patterns
            foreach (var pattern in _options.SignatureImagePatterns)
            {
                if (Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase))
                {
                    return (false, $"Matches signature pattern: {pattern}");
                }
            }
        }

        return (true, null);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var dataverseUrl = _configuration["Dataverse:ServiceUrl"]!.TrimEnd('/');
            var scope = $"{dataverseUrl}/.default";

            _logger.LogInformation("Acquiring Dataverse token with scope: {Scope}", scope);

            try
            {
                _currentToken = await _credential.GetTokenAsync(
                    new TokenRequestContext([scope]),
                    cancellationToken);

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

                _logger.LogInformation("Acquired Dataverse token, expires: {ExpiresOn}", _currentToken.Value.ExpiresOn);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to acquire Dataverse access token");
                throw;
            }
        }
    }

    private static IEnumerable<MailboxAddress> ParseRecipients(string recipients)
    {
        if (string.IsNullOrWhiteSpace(recipients))
            yield break;

        // Dataverse stores recipients as semicolon-separated
        var parts = recipients.Split([';', ','], StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            MailboxAddress? address;
            try
            {
                address = MailboxAddress.Parse(trimmed);
            }
            catch
            {
                // If parsing fails, create a simple address
                address = new MailboxAddress(trimmed, trimmed);
            }

            yield return address;
        }
    }

    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "email";

        // Remove invalid filename characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder();

        foreach (var c in input)
        {
            if (invalidChars.Contains(c))
                sanitized.Append('_');
            else if (char.IsWhiteSpace(c))
                sanitized.Append('_');
            else
                sanitized.Append(c);
        }

        // Remove consecutive underscores and trim
        var result = Regex.Replace(sanitized.ToString(), @"_+", "_").Trim('_');

        return string.IsNullOrEmpty(result) ? "email" : result;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Remove HTML tags
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        // Decode HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);
        // Normalize whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }

    private static string? TruncateBody(string? body, int maxLength = 10000)
    {
        if (string.IsNullOrEmpty(body) || body.Length <= maxLength)
            return body;

        return body[..maxLength] + "\n\n[Content truncated]";
    }

    private static byte[] ReadStreamToByteArray(Stream stream)
    {
        if (stream is MemoryStream ms)
            return ms.ToArray();

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private static string? GetStringOrNull(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? GetBoolOrNull(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) &&
               (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : null;
    }

    private static DateTime? GetDateTimeOrNull(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(value.GetString(), out var dt))
                return dt;
        }
        return null;
    }

    private static Guid? GetGuidOrNull(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            if (Guid.TryParse(value.GetString(), out var guid))
                return guid;
        }
        return null;
    }

    /// <summary>
    /// Internal class to hold email activity data from Dataverse.
    /// </summary>
    private class EmailActivityData
    {
        public string? Subject { get; init; }
        public string? Description { get; init; }
        public string? Sender { get; init; }
        public string? ToRecipients { get; init; }
        public string? CcRecipients { get; init; }
        public string? MessageId { get; init; }
        public bool? DirectionCode { get; init; }
        public DateTime? ActualEnd { get; init; }
        public DateTime? CreatedOn { get; init; }
        public string? TrackingToken { get; init; }
        public string? ConversationIndex { get; init; }
        public Guid? RegardingObjectId { get; init; }
        public string? RegardingObjectType { get; init; }
    }
}
