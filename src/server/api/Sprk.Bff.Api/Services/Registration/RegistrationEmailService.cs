using System.Reflection;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Sends branded registration lifecycle emails (admin notification, welcome, expiration warning, expired)
/// via the existing CommunicationService pipeline.
/// Templates are loaded as embedded resources and rendered via simple {{placeholder}} replacement.
/// </summary>
public sealed class RegistrationEmailService
{
    private const string FromMailbox = "demo@demo.spaarke.com";
    private const string SupportEmail = "support@spaarke.com";
    private const string QuickStartGuideUrl = "https://www.spaarke.com/quick-start";
    private const string ProductionAccessUrl = "https://www.spaarke.com/contact";

    private const string TemplateNamespace = "Sprk.Bff.Api.Services.Registration.EmailTemplates";

    private readonly CommunicationService _communicationService;
    private readonly ILogger<RegistrationEmailService> _logger;

    public RegistrationEmailService(
        CommunicationService communicationService,
        ILogger<RegistrationEmailService> logger)
    {
        _communicationService = communicationService;
        _logger = logger;
    }

    /// <summary>
    /// Sends an admin notification email when a new demo request is submitted.
    /// </summary>
    public async Task SendAdminNotificationAsync(
        string[] adminEmails,
        string trackingId,
        string firstName,
        string lastName,
        string email,
        string organization,
        string useCase,
        DateTimeOffset requestDate,
        string recordUrl,
        CancellationToken cancellationToken = default)
    {
        var templateHtml = await LoadTemplateAsync("AdminNotificationTemplate.html");

        var body = templateHtml
            .Replace("{{TrackingId}}", Encode(trackingId))
            .Replace("{{FirstName}}", Encode(firstName))
            .Replace("{{LastName}}", Encode(lastName))
            .Replace("{{Email}}", Encode(email))
            .Replace("{{Organization}}", Encode(organization))
            .Replace("{{UseCase}}", Encode(useCase))
            .Replace("{{RequestDate}}", Encode(requestDate.ToString("MMMM d, yyyy 'at' h:mm tt 'UTC'")))
            .Replace("{{RecordUrl}}", recordUrl)
            .Replace("{{Year}}", DateTime.UtcNow.Year.ToString());

        var request = new SendCommunicationRequest
        {
            To = adminEmails,
            Subject = $"New Demo Request: {firstName} {lastName} ({organization})",
            Body = body,
            BodyFormat = BodyFormat.HTML,
            FromMailbox = FromMailbox,
            SendMode = SendMode.SharedMailbox,
            CorrelationId = $"reg-admin-{trackingId}"
        };

        _logger.LogInformation(
            "Sending admin notification email | TrackingId: {TrackingId}, AdminCount: {AdminCount}",
            trackingId, adminEmails.Length);

        await _communicationService.SendAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends an acknowledgement email to the applicant confirming their request was received.
    /// BCC: demo@demo.spaarke.com for record keeping (BCC avoids Graph self-CC issues when sender == CC).
    /// </summary>
    public async Task SendAcknowledgementEmailAsync(
        string recipientEmail,
        string firstName,
        string lastName,
        string organization,
        string trackingId,
        CancellationToken cancellationToken = default)
    {
        var templateHtml = await LoadTemplateAsync("AcknowledgementTemplate.html");

        var body = templateHtml
            .Replace("{{FirstName}}", Encode(firstName))
            .Replace("{{LastName}}", Encode(lastName))
            .Replace("{{Organization}}", Encode(organization))
            .Replace("{{TrackingId}}", Encode(trackingId))
            .Replace("{{SupportEmail}}", SupportEmail)
            .Replace("{{Year}}", DateTime.UtcNow.Year.ToString());

        var request = new SendCommunicationRequest
        {
            To = [recipientEmail],
            Bcc = [FromMailbox], // BCC demo@demo.spaarke.com — BCC avoids Graph issues when sender == recipient
            Subject = "We Received Your Spaarke Demo Request",
            Body = body,
            BodyFormat = BodyFormat.HTML,
            FromMailbox = FromMailbox,
            SendMode = SendMode.SharedMailbox,
            CorrelationId = $"reg-ack-{trackingId}"
        };

        _logger.LogInformation(
            "Sending acknowledgement email | Recipient: {Recipient}, TrackingId: {TrackingId}",
            recipientEmail, trackingId);

        await _communicationService.SendAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends a welcome email with demo credentials to the user.
    /// CC: demo@demo.spaarke.com for record keeping.
    /// </summary>
    public async Task SendWelcomeEmailAsync(
        string recipientEmail,
        string firstName,
        string username,
        string temporaryPassword,
        string accessUrl,
        DateTimeOffset expirationDate,
        string environmentName,
        CancellationToken cancellationToken = default)
    {
        var templateHtml = await LoadTemplateAsync("WelcomeTemplate.html");

        var body = templateHtml
            .Replace("{{FirstName}}", Encode(firstName))
            .Replace("{{Username}}", Encode(username))
            .Replace("{{TemporaryPassword}}", Encode(temporaryPassword))
            .Replace("{{AccessUrl}}", accessUrl)
            .Replace("{{ExpirationDate}}", Encode(expirationDate.ToString("MMMM d, yyyy")))
            .Replace("{{EnvironmentName}}", Encode(environmentName))
            .Replace("{{QuickStartGuideUrl}}", QuickStartGuideUrl)
            .Replace("{{SupportEmail}}", SupportEmail)
            .Replace("{{Year}}", DateTime.UtcNow.Year.ToString());

        var request = new SendCommunicationRequest
        {
            To = [recipientEmail],
            Bcc = [FromMailbox], // BCC demo@demo.spaarke.com for record keeping
            Subject = "Your Spaarke Demo Access is Ready!",
            Body = body,
            BodyFormat = BodyFormat.HTML,
            FromMailbox = FromMailbox,
            SendMode = SendMode.SharedMailbox,
            CorrelationId = $"reg-welcome-{username}"
        };

        _logger.LogInformation(
            "Sending welcome email | Recipient: {Recipient}, Username: {Username}, ExpiresOn: {ExpirationDate}",
            recipientEmail, username, expirationDate);

        await _communicationService.SendAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends an expiration warning email 3 days before demo access expires.
    /// </summary>
    public async Task SendExpirationWarningAsync(
        string recipientEmail,
        string firstName,
        DateTimeOffset expirationDate,
        CancellationToken cancellationToken = default)
    {
        var templateHtml = await LoadTemplateAsync("ExpirationWarningTemplate.html");

        var body = templateHtml
            .Replace("{{FirstName}}", Encode(firstName))
            .Replace("{{ExpirationDate}}", Encode(expirationDate.ToString("MMMM d, yyyy")))
            .Replace("{{SupportEmail}}", SupportEmail)
            .Replace("{{ProductionAccessUrl}}", ProductionAccessUrl)
            .Replace("{{Year}}", DateTime.UtcNow.Year.ToString());

        var request = new SendCommunicationRequest
        {
            To = [recipientEmail],
            Subject = "Your Spaarke Demo Expires in 3 Days",
            Body = body,
            BodyFormat = BodyFormat.HTML,
            FromMailbox = FromMailbox,
            SendMode = SendMode.SharedMailbox,
            CorrelationId = $"reg-expwarn-{recipientEmail}"
        };

        _logger.LogInformation(
            "Sending expiration warning email | Recipient: {Recipient}, ExpiresOn: {ExpirationDate}",
            recipientEmail, expirationDate);

        await _communicationService.SendAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends an expired notification after demo access has ended.
    /// </summary>
    public async Task SendExpiredNotificationAsync(
        string recipientEmail,
        string firstName,
        CancellationToken cancellationToken = default)
    {
        var templateHtml = await LoadTemplateAsync("ExpiredTemplate.html");

        var body = templateHtml
            .Replace("{{FirstName}}", Encode(firstName))
            .Replace("{{SupportEmail}}", SupportEmail)
            .Replace("{{ProductionAccessUrl}}", ProductionAccessUrl)
            .Replace("{{Year}}", DateTime.UtcNow.Year.ToString());

        var request = new SendCommunicationRequest
        {
            To = [recipientEmail],
            Subject = "Your Spaarke Demo Access Has Ended",
            Body = body,
            BodyFormat = BodyFormat.HTML,
            FromMailbox = FromMailbox,
            SendMode = SendMode.SharedMailbox,
            CorrelationId = $"reg-expired-{recipientEmail}"
        };

        _logger.LogInformation(
            "Sending expired notification email | Recipient: {Recipient}",
            recipientEmail);

        await _communicationService.SendAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Loads an HTML email template from embedded resources.
    /// </summary>
    private static async Task<string> LoadTemplateAsync(string templateFileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{TemplateNamespace}.{templateFileName}";

        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Email template '{templateFileName}' not found as embedded resource '{resourceName}'.");

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// HTML-encodes a value for safe insertion into template placeholders.
    /// </summary>
    private static string Encode(string value)
        => System.Net.WebUtility.HtmlEncode(value);
}
