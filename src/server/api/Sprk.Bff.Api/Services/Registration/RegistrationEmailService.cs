using System.Reflection;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Sends branded registration lifecycle emails (admin notification, welcome, expiration warning, expired)
/// via the existing CommunicationService pipeline.
/// Templates are loaded as embedded resources and rendered via simple {{placeholder}} replacement.
/// </summary>
/// <remarks>
/// Not sealed only so <see cref="SendPortalNotificationEmailAsync"/> (spaarke-SPA-external-access-platform-r3 task 036) and
/// <see cref="SendCiamOnboardingEmailAsync"/> are test seams: the grant tests replace those two methods to record the send;
/// no production type derives from this class.
/// </remarks>
public class RegistrationEmailService
{
    // The system sender is configuration (owner decision 2026-10-11), not a literal: it was the constant "demo@demo.spaarke.com".
    // It reuses Communication:DefaultMailbox - the platform's one central mailbox (the canonical manifest's
    // Communication-DefaultMailbox, intake-supplied per stamp). ENFORCED invariant: CommunicationOptionsValidator fails the
    // host's start outside Development/Testing unless DefaultMailbox is non-blank AND equals the Email of an entry in
    // Communication:ApprovedSenders (the send path rejects any other explicit FromMailbox as InvalidSender). Deployed hosts
    // therefore need App Settings Communication__DefaultMailbox and a matching Communication__ApprovedSenders__N__Email.
    private string FromMailbox => ResolveFromMailbox(_communicationOptions.Value);

    private const string SupportEmail = "support@spaarke.com";
    private const string QuickStartGuideUrl = "https://www.spaarke.com/quick-start";
    private const string ProductionAccessUrl = "https://www.spaarke.com/contact";

    private const string TemplateNamespace = "Sprk.Bff.Api.Services.Registration.EmailTemplates";

    private readonly CommunicationService _communicationService;
    private readonly IOptions<CommunicationOptions> _communicationOptions;
    private readonly ILogger<RegistrationEmailService> _logger;

    public RegistrationEmailService(
        CommunicationService communicationService,
        IOptions<CommunicationOptions> communicationOptions,
        ILogger<RegistrationEmailService> logger)
    {
        _communicationService = communicationService;
        _communicationOptions = communicationOptions;
        _logger = logger;
    }

    /// <summary>
    /// The system sender mailbox: <c>Communication:DefaultMailbox</c>. Throws when it is blank - there is no fallback
    /// address, so a missing setting fails the send (every caller treats a send failure as best effort or an error)
    /// instead of mailing from an unintended mailbox. Outside Development/Testing a blank value never reaches here: the
    /// host does not start (<see cref="CommunicationOptionsValidator"/>).
    /// </summary>
    internal static string ResolveFromMailbox(CommunicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.DefaultMailbox))
            throw new InvalidOperationException(
                "Communication:DefaultMailbox is not configured, so the system sender mailbox is unknown and no email was sent.");
        return options.DefaultMailbox.Trim();
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
    /// BCC: the system sender mailbox for record keeping (BCC avoids Graph self-CC issues when sender == CC).
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
            Bcc = [FromMailbox], // BCC the system sender - BCC avoids Graph issues when sender == recipient
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
    /// BCC: the system sender mailbox for record keeping.
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
            Bcc = [FromMailbox], // BCC the system sender for record keeping
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
    /// The text that replaces the invite link in the STORED copy of an invite email (task 185, design note §12 I-6): the
    /// <c>sprk_communication</c> row keeps the email, never the link's one-time code.
    /// </summary>
    internal const string RemovedInviteLinkText = "[invitation link removed]";

    /// <summary>
    /// Sends a CIAM onboarding email to an admin-provisioned external user.
    /// R1 has no admin-delivered permanent password: the email directs the user to set their own
    /// password via SSPR ("Forgot password") the first time they sign in.
    /// Sent via the existing CommunicationService → shared-mailbox app-only pipeline, as SYSTEM MAIL (task 185): the
    /// stored copy has the invite link removed, and the send is neither enriched nor saved to Sent Items.
    /// </summary>
    /// <param name="portalUrl">The portal URL (trusted config), shown as the link's visible text.</param>
    /// <param name="inviteUrl">
    /// Task 185: the invite link every button and link opens: the portal URL with <c>?customer=</c> and, in the fragment,
    /// <c>#invite=</c> (the one-time code, §12 I-12). Built by the caller from trusted config and the code; HTML-attribute-encoded here. Never logged.
    /// </param>
    public virtual async Task SendCiamOnboardingEmailAsync(
        string recipientEmail,
        string firstName,
        string portalUrl,
        string inviteUrl,
        CancellationToken cancellationToken = default)
    {
        var templateHtml = await LoadTemplateAsync("CiamOnboardingTemplate.html");

        var request = BuildCiamInviteRequest(
            templateHtml, recipientEmail, firstName, portalUrl, inviteUrl, DateTime.UtcNow.Year, FromMailbox,
            subject: "Welcome to the Spaarke Portal — Set Your Password",
            correlationId: $"reg-ciam-onboarding-{recipientEmail}");

        _logger.LogInformation(
            "Sending CIAM onboarding email | Recipient: {Recipient}",
            recipientEmail);

        await _communicationService.SendAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends the "sign in with your existing account" email (unified-access-control-r2 task 184): the invite found that the
    /// person already has a CIAM account (Graph <c>POST /users</c> answered <c>ObjectConflict</c>), so no account was created and
    /// there is no password to set. Sent INSTEAD of <see cref="SendCiamOnboardingEmailAsync"/>, through the same template
    /// mechanism, sender, send mode and pipeline, and as system mail (task 185). Not sealed only as a test seam, like the
    /// onboarding email.
    /// </summary>
    /// <param name="portalUrl">The portal URL (trusted config), shown as the link's visible text.</param>
    /// <param name="inviteUrl">Task 185: the invite link with its one-time code. See <see cref="SendCiamOnboardingEmailAsync"/>.</param>
    public virtual async Task SendCiamExistingAccountEmailAsync(
        string recipientEmail,
        string firstName,
        string portalUrl,
        string inviteUrl,
        CancellationToken cancellationToken = default)
    {
        var templateHtml = await LoadTemplateAsync("CiamExistingAccountTemplate.html");

        var request = BuildCiamInviteRequest(
            templateHtml, recipientEmail, firstName, portalUrl, inviteUrl, DateTime.UtcNow.Year, FromMailbox,
            subject: "You've Been Invited to the Spaarke Portal — Sign In With Your Existing Account",
            correlationId: $"reg-ciam-existing-account-{recipientEmail}");

        _logger.LogInformation(
            "Sending CIAM existing-account invite email | Recipient: {Recipient}",
            recipientEmail);

        await _communicationService.SendAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The shared-mailbox SYSTEM-MAIL request for a CIAM invite email (task 185). The sent body carries
    /// <paramref name="inviteUrl"/> in every <c>{{InviteUrl}}</c>. The stored body (<see cref="SystemMailOptions.StoredBody"/>)
    /// is the same template with <see cref="RemovedInviteLinkText"/> there instead, so <c>CommunicationService</c> never
    /// parses a body. The visible link text stays <c>{{PortalUrl}}</c>, so the code is never printed as text. Every value
    /// is HTML-encoded and the template is filled in ONE pass (<see cref="FillTemplate"/>). Pure, so it is testable
    /// without a mailbox.
    /// </summary>
    internal static SendCommunicationRequest BuildCiamInviteRequest(
        string templateHtml,
        string recipientEmail,
        string firstName,
        string portalUrl,
        string inviteUrl,
        int year,
        string fromMailbox,
        string subject,
        string correlationId)
    {
        string Fill(string inviteLinkValue) => FillTemplate(templateHtml, new Dictionary<string, string>
        {
            ["FirstName"] = Encode(firstName),
            ["Email"] = Encode(recipientEmail),
            ["PortalUrl"] = Encode(portalUrl),
            ["InviteUrl"] = Encode(inviteLinkValue),
            ["SupportEmail"] = SupportEmail,
            ["Year"] = year.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

        return new SendCommunicationRequest
        {
            To = [recipientEmail],
            Subject = subject,
            Body = Fill(inviteUrl),
            BodyFormat = BodyFormat.HTML,
            FromMailbox = fromMailbox,
            SendMode = SendMode.SharedMailbox,
            CorrelationId = correlationId,
            SystemMail = new SystemMailOptions { StoredBody = Fill(RemovedInviteLinkText) },
        };
    }

    /// <summary>
    /// Sends a portal notification email (spaarke-SPA-external-access-platform-r3 task 036, FR-07): "you were given access
    /// to a record", with one link into the external portal. Same sender, send
    /// mode and pipeline as <see cref="SendCiamOnboardingEmailAsync"/> (the shared mailbox, app-only — never the user's
    /// mailbox, no OBO). Throws when the send is refused (an unapproved sender, the daily limit) or fails; the caller
    /// treats every such failure as best effort.
    /// </summary>
    /// <param name="linkUrl">
    /// The portal deep link. Built by the caller from server configuration plus URL-encoded query values; it is
    /// HTML-attribute-encoded here, like every other value, so an <c>&amp;</c> in it is written <c>&amp;amp;</c>.
    /// </param>
    /// <param name="correlationId">Stable per recipient and event, so one event's emails can be told apart in the send log.</param>
    public virtual async Task SendPortalNotificationEmailAsync(
        string recipientEmail,
        string subject,
        string headline,
        string message,
        string linkUrl,
        string linkText,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var templateHtml = await LoadTemplateAsync("PortalNotificationTemplate.html");

        var request = BuildPortalNotificationRequest(
            templateHtml, recipientEmail, subject, headline, message, linkUrl, linkText, correlationId, DateTime.UtcNow.Year,
            FromMailbox);

        _logger.LogInformation(
            "Sending portal notification email | CorrelationId: {CorrelationId}", correlationId);

        await _communicationService.SendAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The shared-mailbox request for a portal notification: the recipient, this service's from-mailbox, the HTML body from
    /// the template, <see cref="SendMode.SharedMailbox"/> (app-only; never the user's mailbox). Pure, so the real request is
    /// testable without a mailbox (task 036 review K3). Every caller-supplied value is HTML-encoded and the template is filled
    /// in ONE pass (<see cref="FillTemplate"/>), so a value that happens to contain <c>{{Year}}</c> is written as text, not
    /// substituted by a later placeholder.
    /// </summary>
    internal static SendCommunicationRequest BuildPortalNotificationRequest(
        string templateHtml,
        string recipientEmail,
        string subject,
        string headline,
        string message,
        string linkUrl,
        string linkText,
        string correlationId,
        int year,
        string fromMailbox)
        => new()
        {
            To = [recipientEmail],
            Subject = subject,
            Body = FillTemplate(templateHtml, new Dictionary<string, string>
            {
                ["Headline"] = Encode(headline),
                ["Message"] = Encode(message),
                ["LinkUrl"] = Encode(linkUrl),
                ["LinkText"] = Encode(linkText),
                ["SupportEmail"] = SupportEmail,
                ["Year"] = year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }),
            BodyFormat = BodyFormat.HTML,
            FromMailbox = fromMailbox,
            SendMode = SendMode.SharedMailbox,
            CorrelationId = correlationId
        };

    /// <summary>
    /// Replaces each <c>{{Name}}</c> in <paramref name="template"/> with its value in ONE regex pass: substituted text is never
    /// re-scanned, so a value containing another placeholder (a record named <c>{{Year}}</c>) cannot be substituted by it.
    /// A placeholder with no value is left as written.
    /// </summary>
    internal static string FillTemplate(string template, IReadOnlyDictionary<string, string> values)
        => System.Text.RegularExpressions.Regex.Replace(
            template,
            @"\{\{(\w+)\}\}",
            m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);

    /// <summary>
    /// Sends an expiration warning email 3 days before demo access expires.
    /// </summary>
    /// <remarks><c>virtual</c> is the substitution seam (ADR-038 §4) for <c>DemoExpirationJob</c>'s send-once tests.</remarks>
    public virtual async Task SendExpirationWarningAsync(
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
    /// <remarks><c>virtual</c> is the substitution seam (ADR-038 §4) for <c>DemoExpirationJob</c>'s send-once tests.</remarks>
    public virtual async Task SendExpiredNotificationAsync(
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
