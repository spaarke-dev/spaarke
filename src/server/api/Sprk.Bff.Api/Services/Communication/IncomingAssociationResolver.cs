using System.Text.RegularExpressions;
using Microsoft.Graph.Models;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Models;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Resolves associations for incoming emails using a priority cascade:
///   1. Thread matching (In-Reply-To/References → parent communication)
///   2. Sender matching (email → contact/account)
///   3. Subject pattern matching (regex → matter reference number)
///   4. Mailbox context (account default regarding matter)
///
/// If no match is found, sets sprk_associationstatus = Pending Review (100000001).
/// If matched, sets sprk_associationstatus = Resolved (100000000).
///
/// Non-fatal: association failure never prevents communication record creation.
/// Registered as concrete type in AddCommunicationModule() per ADR-010.
/// </summary>
public sealed class IncomingAssociationResolver
{
    private readonly IDataverseService _dataverseService;
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<IncomingAssociationResolver> _logger;

    /// <summary>Association status: Resolved.</summary>
    private const int AssociationStatusResolved = 100000000;

    /// <summary>Association status: Pending Review.</summary>
    private const int AssociationStatusPendingReview = 100000001;

    /// <summary>
    /// Common email providers to skip for domain-based account matching.
    /// </summary>
    private static readonly HashSet<string> CommonEmailProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "outlook.com", "hotmail.com", "yahoo.com",
        "live.com", "msn.com", "icloud.com", "aol.com",
        "protonmail.com", "mail.com", "ymail.com", "googlemail.com"
    };

    /// <summary>
    /// Regex patterns for extracting matter reference numbers from email subjects.
    /// </summary>
    private static readonly Regex[] MatterReferencePatterns =
    [
        // MAT-12345
        new(@"\bMAT-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        // Matter #12345 or Matter #12345
        new(@"\bMatter\s*#(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        // SPRK-12345
        new(@"\bSPRK-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        // [MATTER:12345]
        new(@"\[MATTER:(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
    ];

    public IncomingAssociationResolver(
        IDataverseService dataverseService,
        IGraphClientFactory graphClientFactory,
        ILogger<IncomingAssociationResolver> logger)
    {
        _dataverseService = dataverseService;
        _graphClientFactory = graphClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Resolves associations for an incoming communication record using a priority cascade.
    /// Updates the sprk_communication record with resolved associations and status.
    /// </summary>
    /// <param name="communicationId">The ID of the sprk_communication record to update.</param>
    /// <param name="mailboxEmail">The shared mailbox email that received the message.</param>
    /// <param name="graphMessageId">The Graph message ID for header lookups.</param>
    /// <param name="graphMessage">The fetched Graph message (for subject, sender, etc.).</param>
    /// <param name="account">The communication account (optional, for mailbox context fallback).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ResolveAsync(
        Guid communicationId,
        string mailboxEmail,
        string graphMessageId,
        Message graphMessage,
        CommunicationAccount? account,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Starting association resolution for communication {CommunicationId} | GraphMessageId: {GraphMessageId}",
            communicationId, graphMessageId);

        var fields = new Dictionary<string, object>();

        // ── Priority 1: Thread matching ─────────────────────────────────────────
        if (await TryResolveByThreadAsync(mailboxEmail, graphMessageId, fields, ct))
        {
            _logger.LogInformation(
                "Association resolved via thread matching for communication {CommunicationId}",
                communicationId);
            await ApplyAssociationAsync(communicationId, fields, AssociationStatusResolved, ct);
            return;
        }

        // ── Priority 2: Sender matching ─────────────────────────────────────────
        var senderEmail = graphMessage.From?.EmailAddress?.Address;
        if (!string.IsNullOrEmpty(senderEmail) &&
            await TryResolveBySenderAsync(senderEmail, fields, ct))
        {
            _logger.LogInformation(
                "Association resolved via sender matching for communication {CommunicationId} | Sender: {Sender}",
                communicationId, senderEmail);
            await ApplyAssociationAsync(communicationId, fields, AssociationStatusResolved, ct);
            return;
        }

        // ── Priority 3: Subject pattern matching ────────────────────────────────
        var subject = graphMessage.Subject;
        if (!string.IsNullOrEmpty(subject) &&
            await TryResolveBySubjectPatternAsync(subject, fields, ct))
        {
            _logger.LogInformation(
                "Association resolved via subject pattern for communication {CommunicationId} | Subject: '{Subject}'",
                communicationId, subject);
            await ApplyAssociationAsync(communicationId, fields, AssociationStatusResolved, ct);
            return;
        }

        // ── Priority 4: Mailbox context (default regarding matter) ──────────────
        if (account?.DefaultRegardingMatterId is { } defaultMatterId && defaultMatterId != Guid.Empty)
        {
            fields["sprk_regardingmatter"] = new EntityReference("sprk_matter", defaultMatterId);

            _logger.LogInformation(
                "Association resolved via mailbox context for communication {CommunicationId} | DefaultMatter: {MatterId}",
                communicationId, defaultMatterId);
            await ApplyAssociationAsync(communicationId, fields, AssociationStatusResolved, ct);
            return;
        }

        // ── No match: flag as Pending Review ────────────────────────────────────
        _logger.LogInformation(
            "No association found for communication {CommunicationId}. Setting status to Pending Review.",
            communicationId);

        await ApplyAssociationAsync(communicationId, fields, AssociationStatusPendingReview, ct);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Priority 1: Thread matching
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks In-Reply-To header from Graph message internet headers,
    /// looks up existing sprk_communication by parent message ID,
    /// and copies associations from the parent.
    /// </summary>
    private async Task<bool> TryResolveByThreadAsync(
        string mailboxEmail,
        string graphMessageId,
        Dictionary<string, object> fields,
        CancellationToken ct)
    {
        try
        {
            // Fetch internet message headers from Graph to get In-Reply-To
            var inReplyTo = await GetInReplyToHeaderAsync(mailboxEmail, graphMessageId, ct);
            if (string.IsNullOrEmpty(inReplyTo))
                return false;

            _logger.LogDebug(
                "Found In-Reply-To header: {InReplyTo} for message {GraphMessageId}",
                inReplyTo, graphMessageId);

            // Look up parent communication by the In-Reply-To message ID
            // The In-Reply-To header contains an internet message ID (e.g., <ABC@contoso.com>),
            // but sprk_graphmessageid stores the Graph message ID. We need to search
            // by sprk_internetmessageid if available, or fall back.
            // For simplicity, try matching against sprk_graphmessageid first.
            var parentComm = await _dataverseService.GetCommunicationByGraphMessageIdAsync(inReplyTo, ct);
            if (parentComm is null)
            {
                _logger.LogDebug("No parent communication found for In-Reply-To: {InReplyTo}", inReplyTo);
                return false;
            }

            // Copy associations from parent
            var copied = false;
            if (parentComm.Contains("sprk_regardingmatter") && parentComm["sprk_regardingmatter"] is EntityReference matterRef)
            {
                fields["sprk_regardingmatter"] = matterRef;
                copied = true;
            }

            if (parentComm.Contains("sprk_regardingorganization") && parentComm["sprk_regardingorganization"] is EntityReference orgRef)
            {
                fields["sprk_regardingorganization"] = orgRef;
                copied = true;
            }

            if (parentComm.Contains("sprk_regardingperson") && parentComm["sprk_regardingperson"] is EntityReference personRef)
            {
                fields["sprk_regardingperson"] = personRef;
                copied = true;
            }

            return copied;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thread matching failed for message {GraphMessageId}", graphMessageId);
            return false;
        }
    }

    /// <summary>
    /// Fetches the In-Reply-To header from Graph message internet headers.
    /// </summary>
    private async Task<string?> GetInReplyToHeaderAsync(
        string mailboxEmail, string graphMessageId, CancellationToken ct)
    {
        try
        {
            var graphClient = _graphClientFactory.ForApp();
            var message = await graphClient.Users[mailboxEmail]
                .Messages[graphMessageId]
                .GetAsync(config =>
                {
                    config.QueryParameters.Select = new[] { "internetMessageHeaders" };
                }, ct);

            var inReplyTo = message?.InternetMessageHeaders?
                .FirstOrDefault(h =>
                    string.Equals(h.Name, "In-Reply-To", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            return inReplyTo;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch internet message headers for {GraphMessageId} in {Mailbox}",
                graphMessageId, mailboxEmail);
            return null;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Priority 2: Sender matching
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Queries Dataverse for a contact matching the sender email,
    /// and optionally an account matching the sender domain.
    /// </summary>
    private async Task<bool> TryResolveBySenderAsync(
        string senderEmail,
        Dictionary<string, object> fields,
        CancellationToken ct)
    {
        try
        {
            var matched = false;

            // Try contact match by email
            var contact = await _dataverseService.QueryContactByEmailAsync(senderEmail, ct);
            if (contact is not null)
            {
                fields["sprk_regardingperson"] = new EntityReference("contact", contact.Id);
                matched = true;

                _logger.LogDebug("Sender matched to contact {ContactId} for email {Email}",
                    contact.Id, senderEmail);
            }

            // Try account match by domain (skip common providers)
            var domain = ExtractDomain(senderEmail);
            if (!string.IsNullOrEmpty(domain) && !CommonEmailProviders.Contains(domain))
            {
                var account = await _dataverseService.QueryAccountByDomainAsync(domain, ct);
                if (account is not null)
                {
                    fields["sprk_regardingorganization"] = new EntityReference("account", account.Id);
                    matched = true;

                    _logger.LogDebug("Sender domain matched to account {AccountId} for domain {Domain}",
                        account.Id, domain);
                }
            }

            return matched;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sender matching failed for {SenderEmail}", senderEmail);
            return false;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Priority 3: Subject pattern matching
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Uses regex patterns to extract matter reference numbers from the subject line.
    /// Looks up sprk_matter by sprk_referencenumber.
    /// </summary>
    private async Task<bool> TryResolveBySubjectPatternAsync(
        string subject,
        Dictionary<string, object> fields,
        CancellationToken ct)
    {
        try
        {
            foreach (var pattern in MatterReferencePatterns)
            {
                try
                {
                    var match = pattern.Match(subject);
                    if (!match.Success)
                        continue;

                    var referenceNumber = match.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(referenceNumber))
                        continue;

                    _logger.LogDebug("Extracted reference number '{Reference}' from subject '{Subject}'",
                        referenceNumber, subject);

                    // Also try with prefix variations
                    var matter = await _dataverseService.QueryMatterByReferenceNumberAsync(referenceNumber, ct)
                                 ?? await _dataverseService.QueryMatterByReferenceNumberAsync($"MAT-{referenceNumber}", ct);

                    if (matter is not null)
                    {
                        fields["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter.Id);

                        _logger.LogDebug("Subject pattern matched to matter {MatterId} via reference '{Reference}'",
                            matter.Id, referenceNumber);
                        return true;
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    _logger.LogWarning("Regex timeout evaluating subject pattern");
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subject pattern matching failed for subject '{Subject}'", subject);
            return false;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Apply resolved associations to the communication record
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Updates the sprk_communication record with resolved association fields and status.
    /// </summary>
    private async Task ApplyAssociationAsync(
        Guid communicationId,
        Dictionary<string, object> fields,
        int associationStatus,
        CancellationToken ct)
    {
        fields["sprk_associationstatus"] = new OptionSetValue(associationStatus);

        await _dataverseService.UpdateAsync("sprk_communication", communicationId, fields, ct);

        _logger.LogDebug(
            "Applied association to communication {CommunicationId} | Status: {Status}, FieldCount: {FieldCount}",
            communicationId, associationStatus == AssociationStatusResolved ? "Resolved" : "PendingReview",
            fields.Count);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private static string? ExtractDomain(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email[(atIndex + 1)..].ToLowerInvariant() : null;
    }
}
