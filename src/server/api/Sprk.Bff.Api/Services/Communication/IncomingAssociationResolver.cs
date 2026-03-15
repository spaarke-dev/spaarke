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
    private readonly ICommunicationDataverseService _communicationService;
    private readonly IGenericEntityService _genericEntityService;
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
        ICommunicationDataverseService communicationService,
        IGenericEntityService genericEntityService,
        IGraphClientFactory graphClientFactory,
        ILogger<IncomingAssociationResolver> logger)
    {
        _communicationService = communicationService;
        _genericEntityService = genericEntityService;
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

        // ── No match: flag as Pending Review ────────────────────────────────────
        // Unassociated emails remain unassociated and surface for manual review.
        // No default-matter fallback — shared mailboxes are not matter-specific.
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

            // Look up parent communication by the In-Reply-To internet message ID.
            // In-Reply-To contains an RFC 2822 internet message ID (e.g., <ABC@contoso.com>).
            // Search sprk_internetmessageid first (exact match), fall back to sprk_graphmessageid.
            var parentComm = await _communicationService.GetCommunicationByInternetMessageIdAsync(inReplyTo, ct)
                             ?? await _communicationService.GetCommunicationByGraphMessageIdAsync(inReplyTo, ct);
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
            var contact = await _communicationService.QueryContactByEmailAsync(senderEmail, ct);
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
                var account = await _communicationService.QueryAccountByDomainAsync(domain, ct);
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
            var patternsTested = 0;
            var patternsMatched = 0;
            var timeouts = 0;

            foreach (var pattern in MatterReferencePatterns)
            {
                try
                {
                    patternsTested++;
                    var match = pattern.Match(subject);
                    if (!match.Success)
                        continue;

                    var referenceNumber = match.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(referenceNumber))
                        continue;

                    patternsMatched++;

                    // Also try with prefix variations
                    var matter = await _communicationService.QueryMatterByReferenceNumberAsync(referenceNumber, ct)
                                 ?? await _communicationService.QueryMatterByReferenceNumberAsync($"MAT-{referenceNumber}", ct);

                    if (matter is not null)
                    {
                        fields["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter.Id);

                        _logger.LogDebug(
                            "Subject pattern matching complete: tested {PatternsTested} patterns, {PatternsMatched} matched, resolved to matter {MatterId}",
                            patternsTested, patternsMatched, matter.Id);
                        return true;
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    timeouts++;
                }
            }

            _logger.LogDebug(
                "Subject pattern matching complete: tested {PatternsTested} patterns, {PatternsMatched} matched, {Timeouts} timeouts, no matter resolved",
                patternsTested, patternsMatched, timeouts);

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
    /// Also populates the Polymorphic Resolver fields (ADR-024) for cross-entity views.
    /// </summary>
    private async Task ApplyAssociationAsync(
        Guid communicationId,
        Dictionary<string, object> fields,
        int associationStatus,
        CancellationToken ct)
    {
        fields["sprk_associationstatus"] = new OptionSetValue(associationStatus);

        // Populate polymorphic resolver fields from the primary regarding entity
        if (associationStatus == AssociationStatusResolved)
        {
            await PopulateResolverFieldsAsync(fields, ct);
        }

        await _genericEntityService.UpdateAsync("sprk_communication", communicationId, fields, ct);

        _logger.LogDebug(
            "Applied association to communication {CommunicationId} | Status: {Status}, FieldCount: {FieldCount}",
            communicationId, associationStatus == AssociationStatusResolved ? "Resolved" : "PendingReview",
            fields.Count);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Polymorphic Resolver (ADR-024)
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Entity-specific regarding fields in priority order for determining the primary record.
    /// Business entities first (matter, project, etc.), then people/orgs.
    /// </summary>
    private static readonly (string FieldName, string EntityLogicalName)[] RegardingFieldPriority =
    [
        ("sprk_regardingmatter", "sprk_matter"),
        ("sprk_regardingproject", "sprk_project"),
        ("sprk_regardinginvoice", "sprk_invoice"),
        ("sprk_regardingworkassignment", "sprk_workassignment"),
        ("sprk_regardingbudget", "sprk_budget"),
        ("sprk_regardinganalysis", "sprk_analysis"),
        ("sprk_regardingorganization", "sprk_organization"),
        ("sprk_regardingperson", "contact"),
    ];

    /// <summary>
    /// In-memory cache for sprk_recordtype_ref lookups (entity logical name → GUID + display name).
    /// Populated lazily, lives for the lifetime of the singleton service.
    /// </summary>
    private readonly Dictionary<string, (Guid Id, string DisplayName)?> _recordTypeRefCache = new();

    /// <summary>
    /// Populates the 4 denormalized resolver fields based on the highest-priority
    /// entity-specific regarding field that was set.
    ///
    /// Fields set:
    ///   - sprk_regardingrecordtype  (Lookup → sprk_recordtype_ref)
    ///   - sprk_regardingrecordid    (Text — parent GUID)
    ///   - sprk_regardingrecordname  (Text — parent display name)
    ///   - sprk_regardingrecordurl   (URL — clickable link to parent record)
    /// </summary>
    private async Task PopulateResolverFieldsAsync(
        Dictionary<string, object> fields,
        CancellationToken ct)
    {
        // Find the primary regarding entity (highest priority field that was set)
        EntityReference? primaryRef = null;
        string? primaryEntityLogicalName = null;

        foreach (var (fieldName, entityLogicalName) in RegardingFieldPriority)
        {
            if (fields.TryGetValue(fieldName, out var value) && value is EntityReference entityRef)
            {
                primaryRef = entityRef;
                primaryEntityLogicalName = entityLogicalName;
                break;
            }
        }

        if (primaryRef is null || primaryEntityLogicalName is null)
            return;

        try
        {
            // Set sprk_regardingrecordid (GUID as text)
            var cleanId = primaryRef.Id.ToString("D").ToLowerInvariant();
            fields["sprk_regardingrecordid"] = cleanId;

            // Set sprk_regardingrecordname (display name from the EntityReference or retrieve)
            var recordName = primaryRef.Name;
            if (string.IsNullOrEmpty(recordName))
            {
                // EntityReference.Name may not always be populated; try to retrieve it
                try
                {
                    var nameField = GetPrimaryNameField(primaryEntityLogicalName);
                    if (nameField is not null)
                    {
                        var record = await _genericEntityService.RetrieveAsync(
                            primaryEntityLogicalName, primaryRef.Id, [nameField], ct);
                        recordName = record.GetAttributeValue<string>(nameField) ?? "";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not retrieve name for {Entity} {Id}",
                        primaryEntityLogicalName, primaryRef.Id);
                }
            }
            fields["sprk_regardingrecordname"] = recordName ?? "";

            // Set sprk_regardingrecordurl
            fields["sprk_regardingrecordurl"] = BuildRecordUrl(primaryEntityLogicalName, cleanId);

            // Set sprk_regardingrecordtype (Lookup to sprk_recordtype_ref)
            var recordTypeRef = await ResolveRecordTypeRefAsync(primaryEntityLogicalName, ct);
            if (recordTypeRef.HasValue)
            {
                fields["sprk_regardingrecordtype"] = new EntityReference(
                    "sprk_recordtype_ref", recordTypeRef.Value.Id)
                {
                    Name = recordTypeRef.Value.DisplayName
                };
            }

            _logger.LogDebug(
                "Populated resolver fields: Entity={Entity}, Name={Name}, RecordType={RecordType}",
                primaryEntityLogicalName, recordName ?? "(unknown)",
                recordTypeRef?.DisplayName ?? "(not found)");
        }
        catch (Exception ex)
        {
            // Non-fatal — resolver fields are for display, not critical data
            _logger.LogWarning(ex, "Failed to populate resolver fields for {Entity}", primaryEntityLogicalName);
        }
    }

    /// <summary>
    /// Resolve the sprk_recordtype_ref GUID for an entity logical name. Cached.
    /// </summary>
    private async Task<(Guid Id, string DisplayName)?> ResolveRecordTypeRefAsync(
        string entityLogicalName, CancellationToken ct)
    {
        if (_recordTypeRefCache.TryGetValue(entityLogicalName, out var cached))
            return cached;

        var record = await _communicationService.QueryRecordTypeRefAsync(entityLogicalName, ct);
        if (record is not null)
        {
            var entry = (
                Id: record.Id,
                DisplayName: record.GetAttributeValue<string>("sprk_recorddisplayname") ?? entityLogicalName
            );
            _recordTypeRefCache[entityLogicalName] = entry;
            return entry;
        }

        _recordTypeRefCache[entityLogicalName] = null;
        return null;
    }

    /// <summary>
    /// Build a Dataverse record URL for the resolver.
    /// Uses the Dataverse environment base URL from the service client connection.
    /// </summary>
    private static string BuildRecordUrl(string entityLogicalName, string recordId)
    {
        // On the server side we don't have Xrm context, but we know the org URL
        // from the service client. Use a relative URL that works in model-driven apps.
        return $"/main.aspx?pagetype=entityrecord&etn={entityLogicalName}&id={recordId}";
    }

    /// <summary>
    /// Map entity logical name to its primary name attribute.
    /// </summary>
    private static string? GetPrimaryNameField(string entityLogicalName) => entityLogicalName switch
    {
        "sprk_matter" => "sprk_mattername",
        "sprk_project" => "sprk_projectname",
        "sprk_invoice" => "sprk_name",
        "sprk_event" => "sprk_eventname",
        "sprk_workassignment" => "sprk_name",
        "sprk_budget" => "sprk_name",
        "sprk_analysis" => "sprk_name",
        "sprk_organization" => "sprk_name",
        "contact" => "fullname",
        "account" => "name",
        _ => null,
    };

    // ═════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private static string? ExtractDomain(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email[(atIndex + 1)..].ToLowerInvariant() : null;
    }
}
