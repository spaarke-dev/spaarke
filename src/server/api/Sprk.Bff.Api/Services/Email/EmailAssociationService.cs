using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Implementation of email association service that determines the best
/// Matter, Account, or Contact to associate an email with.
/// Uses multiple signals with confidence scoring per SPEC.md Appendix B.
/// </summary>
public class EmailAssociationService : IEmailAssociationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly EmailProcessingOptions _options;
    private readonly ILogger<EmailAssociationService> _logger;

    /// <summary>
    /// Confidence levels for each association method.
    /// </summary>
    private static class ConfidenceLevels
    {
        public const double TrackingToken = 0.95;
        public const double ConversationThread = 0.90;
        public const double ExistingRegarding = 0.85;
        public const double RecentSenderActivity = 0.70;
        public const double DomainToAccount = 0.60;
        public const double ContactEmailMatch = 0.50;
        public const double ManualOverride = 1.0;
    }

    /// <summary>
    /// Default minimum confidence threshold for automatic association.
    /// </summary>
    private const double DefaultConfidenceThreshold = 0.50;

    /// <summary>
    /// Regex patterns for tracking tokens in subject lines.
    /// Matches various formats used by different systems.
    /// </summary>
    private static readonly Regex[] TrackingTokenPatterns =
    [
        // [SPRK:ABC123], [MATTER:12345], [REF:XYZ789], [TRACK:000123]
        new Regex(@"\[(?:SPRK|MATTER|REF|TRACK)[:\-]([A-Z0-9\-]+)\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),

        // CRM-style: RE: Subject [12345]
        new Regex(@"\[(\d{5,})\]$",
            RegexOptions.Compiled, TimeSpan.FromSeconds(1)),

        // spaarke ticket format: SPRK-12345
        new Regex(@"\bSPRK[:\-](\d{4,})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),

        // Matter reference: Matter #12345 or Matter-12345
        new Regex(@"\bMatter[\s#\-]+(\d{4,})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),

        // CRM tracking token format from X-MS-Exchange headers (often in subject)
        new Regex(@"CRM:([A-Z0-9]{7,})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1))
    ];

    public EmailAssociationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IOptions<EmailProcessingOptions> options,
        ILogger<EmailAssociationService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AssociationResult?> DetermineAssociationAsync(
        Guid emailId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Determining association for email {EmailId}", emailId);

        var signalsResponse = await GetAssociationSignalsAsync(emailId, cancellationToken);

        if (signalsResponse.RecommendedAssociation != null)
        {
            _logger.LogInformation(
                "Email {EmailId} associated with {EntityType} {EntityId} via {Method} (confidence: {Confidence:F2})",
                emailId,
                signalsResponse.RecommendedAssociation.EntityType,
                signalsResponse.RecommendedAssociation.EntityId,
                signalsResponse.RecommendedAssociation.Method,
                signalsResponse.RecommendedAssociation.Confidence);

            return signalsResponse.RecommendedAssociation;
        }

        _logger.LogDebug("No association found for email {EmailId} above threshold {Threshold:F2}",
            emailId, signalsResponse.ConfidenceThreshold);

        return null;
    }

    public async Task<AssociationSignalsResponse> GetAssociationSignalsAsync(
        Guid emailId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting association signals for email {EmailId}", emailId);

        // Load email data from Dataverse
        var emailData = await LoadEmailDataAsync(emailId, cancellationToken);
        if (emailData == null)
        {
            _logger.LogWarning("Email {EmailId} not found in Dataverse", emailId);
            return new AssociationSignalsResponse
            {
                EmailId = emailId,
                Signals = [],
                RecommendedAssociation = null,
                ConfidenceThreshold = DefaultConfidenceThreshold
            };
        }

        var signals = new List<AssociationSignal>();

        // Evaluate all association methods
        await EvaluateTrackingTokenAsync(emailData, signals, cancellationToken);
        await EvaluateConversationThreadAsync(emailData, signals, cancellationToken);
        await EvaluateExistingRegardingAsync(emailData, signals, cancellationToken);
        await EvaluateRecentSenderActivityAsync(emailData, signals, cancellationToken);
        await EvaluateDomainToAccountAsync(emailData, signals, cancellationToken);
        await EvaluateContactEmailMatchAsync(emailData, signals, cancellationToken);

        // Sort by confidence (highest first)
        var sortedSignals = signals.OrderByDescending(s => s.Confidence).ToList();

        // Get the recommended association (highest confidence above threshold)
        var threshold = DefaultConfidenceThreshold;
        var bestSignal = sortedSignals.FirstOrDefault(s => s.Confidence >= threshold);

        AssociationResult? recommended = null;
        if (bestSignal != null)
        {
            recommended = new AssociationResult
            {
                EntityType = bestSignal.EntityType,
                EntityId = bestSignal.EntityId,
                EntityName = bestSignal.EntityName,
                Method = bestSignal.Method,
                Confidence = bestSignal.Confidence,
                Reason = bestSignal.Description
            };
        }

        _logger.LogDebug(
            "Found {Count} association signals for email {EmailId}, recommended: {Recommended}",
            sortedSignals.Count, emailId, recommended?.EntityType ?? "none");

        return new AssociationSignalsResponse
        {
            EmailId = emailId,
            Signals = sortedSignals.AsReadOnly(),
            RecommendedAssociation = recommended,
            ConfidenceThreshold = threshold
        };
    }

    #region Association Methods

    private async Task EvaluateTrackingTokenAsync(
        EmailData emailData,
        List<AssociationSignal> signals,
        CancellationToken cancellationToken)
    {
        var tokensFound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // First check if document already has a tracking token stored
        if (!string.IsNullOrEmpty(emailData.TrackingToken))
        {
            tokensFound.Add(emailData.TrackingToken);
            _logger.LogDebug("Found stored tracking token '{Token}'", emailData.TrackingToken);
        }

        // Extract tokens from subject line using multiple patterns
        if (!string.IsNullOrEmpty(emailData.Subject))
        {
            foreach (var pattern in TrackingTokenPatterns)
            {
                try
                {
                    var match = pattern.Match(emailData.Subject);
                    if (match.Success)
                    {
                        var token = match.Groups[1].Value;
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            tokensFound.Add(token);
                            _logger.LogDebug("Found tracking token '{Token}' in subject via pattern", token);
                        }
                    }
                }
                catch (RegexMatchTimeoutException ex)
                {
                    _logger.LogWarning(ex, "Regex timeout evaluating tracking token pattern");
                }
            }
        }

        // Look up Matter for each token found
        foreach (var token in tokensFound)
        {
            var matter = await FindMatterByTrackingTokenAsync(token, cancellationToken);
            if (matter != null)
            {
                signals.Add(new AssociationSignal
                {
                    EntityType = "sprk_matter",
                    EntityId = matter.Value.Id,
                    EntityName = matter.Value.Name,
                    Method = AssociationMethod.TrackingToken,
                    Confidence = ConfidenceLevels.TrackingToken,
                    Description = $"Tracking token [{token}] matches Matter"
                });
                // Return on first match - highest confidence method
                return;
            }
        }
    }

    private async Task EvaluateConversationThreadAsync(
        EmailData emailData,
        List<AssociationSignal> signals,
        CancellationToken cancellationToken)
    {
        // Conversation thread matching requires analyzing the conversation index
        // and finding other emails in the thread that already have associations
        if (string.IsNullOrEmpty(emailData.ConversationIndex))
            return;

        try
        {
            // Extract conversation root from the conversation index
            // The conversation index is a base64-encoded binary value
            // The first 22 characters (when decoded) represent the conversation root
            var conversationRoot = ExtractConversationRoot(emailData.ConversationIndex);
            if (string.IsNullOrEmpty(conversationRoot))
                return;

            _logger.LogDebug("Looking for thread associations with conversation root: {Root}", conversationRoot);

            // Find other emails in the same conversation thread that have regarding objects
            var threadAssociations = await FindThreadAssociationsAsync(conversationRoot, emailData.Id, cancellationToken);

            foreach (var association in threadAssociations)
            {
                signals.Add(new AssociationSignal
                {
                    EntityType = association.EntityType,
                    EntityId = association.EntityId,
                    EntityName = association.EntityName,
                    Method = AssociationMethod.ConversationThread,
                    Confidence = ConfidenceLevels.ConversationThread,
                    Description = $"Conversation thread contains email linked to {association.EntityType}"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error evaluating conversation thread for email {EmailId}", emailData.Id);
        }
    }

    /// <summary>
    /// Extract the conversation root identifier from a conversation index.
    /// The conversation index is base64 encoded, first 22 bytes are the root.
    /// </summary>
    private static string? ExtractConversationRoot(string conversationIndex)
    {
        try
        {
            // Conversation index is base64 encoded
            // Return first 22 characters as the root identifier for matching
            if (conversationIndex.Length >= 22)
            {
                return conversationIndex[..22];
            }
            return conversationIndex;
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<(string EntityType, Guid EntityId, string EntityName)>> FindThreadAssociationsAsync(
        string conversationRoot,
        Guid excludeEmailId,
        CancellationToken cancellationToken)
    {
        var client = await CreateDataverseClientAsync(cancellationToken);
        if (client == null)
            return [];

        // Find emails in the same thread with regarding objects
        var query = $"emails?" +
                   $"$select=activityid,_regardingobjectid_value" +
                   $"&$filter=startswith(conversationindex,'{conversationRoot}') and " +
                            $"activityid ne {excludeEmailId} and " +
                            $"_regardingobjectid_value ne null" +
                   $"&$orderby=createdon desc" +
                   $"&$top=5";

        try
        {
            var response = await client.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return [];

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<JsonElement>(content);

            var associations = new List<(string EntityType, Guid EntityId, string EntityName)>();
            var seenEntities = new HashSet<Guid>();

            if (result.TryGetProperty("value", out var values))
            {
                foreach (var email in values.EnumerateArray())
                {
                    var regardingId = GetJsonGuidValue(email, "_regardingobjectid_value");
                    if (regardingId.HasValue && !seenEntities.Contains(regardingId.Value))
                    {
                        seenEntities.Add(regardingId.Value);
                        var entityType = GetJsonStringValue(email,
                            "_regardingobjectid_value@Microsoft.Dynamics.CRM.lookuplogicalname") ?? "unknown";
                        var entityName = GetJsonStringValue(email,
                            "_regardingobjectid_value@OData.Community.Display.V1.FormattedValue") ?? "Unknown";

                        associations.Add((entityType, regardingId.Value, entityName));
                    }
                }
            }

            return associations;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding thread associations");
            return [];
        }
    }

    private Task EvaluateExistingRegardingAsync(
        EmailData emailData,
        List<AssociationSignal> signals,
        CancellationToken cancellationToken)
    {
        // If email already has a regarding object, use it with high confidence
        if (emailData.RegardingId == null || string.IsNullOrEmpty(emailData.RegardingType))
            return Task.CompletedTask;

        signals.Add(new AssociationSignal
        {
            EntityType = emailData.RegardingType,
            EntityId = emailData.RegardingId.Value,
            EntityName = emailData.RegardingName ?? "Unknown",
            Method = AssociationMethod.ExistingRegarding,
            Confidence = ConfidenceLevels.ExistingRegarding,
            Description = "Email's existing regarding object"
        });

        return Task.CompletedTask;
    }

    private async Task EvaluateRecentSenderActivityAsync(
        EmailData emailData,
        List<AssociationSignal> signals,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(emailData.From))
            return;

        // Extract sender email address
        var senderEmail = ExtractEmailAddress(emailData.From);
        if (string.IsNullOrEmpty(senderEmail))
            return;

        // Find Matters with recent activity from this sender
        var recentMatters = await FindMattersWithRecentSenderActivityAsync(senderEmail, cancellationToken);

        foreach (var matter in recentMatters)
        {
            signals.Add(new AssociationSignal
            {
                EntityType = "sprk_matter",
                EntityId = matter.Id,
                EntityName = matter.Name,
                Method = AssociationMethod.RecentSenderActivity,
                Confidence = ConfidenceLevels.RecentSenderActivity,
                Description = $"Sender has recent activity on Matter (last: {matter.LastActivityDate:d})"
            });
        }
    }

    private async Task EvaluateDomainToAccountAsync(
        EmailData emailData,
        List<AssociationSignal> signals,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(emailData.From))
            return;

        // Extract sender domain
        var senderEmail = ExtractEmailAddress(emailData.From);
        if (string.IsNullOrEmpty(senderEmail))
            return;

        var domain = ExtractDomain(senderEmail);
        if (string.IsNullOrEmpty(domain) || IsCommonEmailProvider(domain))
            return;

        // Find Accounts matching this domain
        var accounts = await FindAccountsByDomainAsync(domain, cancellationToken);

        foreach (var account in accounts)
        {
            signals.Add(new AssociationSignal
            {
                EntityType = "account",
                EntityId = account.Id,
                EntityName = account.Name,
                Method = AssociationMethod.DomainToAccount,
                Confidence = ConfidenceLevels.DomainToAccount,
                Description = $"Sender domain '{domain}' matches Account"
            });
        }
    }

    private async Task EvaluateContactEmailMatchAsync(
        EmailData emailData,
        List<AssociationSignal> signals,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(emailData.From))
            return;

        var senderEmail = ExtractEmailAddress(emailData.From);
        if (string.IsNullOrEmpty(senderEmail))
            return;

        // Find Contacts with this email address
        var contacts = await FindContactsByEmailAsync(senderEmail, cancellationToken);

        foreach (var contact in contacts)
        {
            signals.Add(new AssociationSignal
            {
                EntityType = "contact",
                EntityId = contact.Id,
                EntityName = contact.Name,
                Method = AssociationMethod.ContactEmailMatch,
                Confidence = ConfidenceLevels.ContactEmailMatch,
                Description = $"Sender email matches Contact"
            });
        }
    }

    #endregion

    #region Dataverse Queries

    private async Task<EmailData?> LoadEmailDataAsync(Guid emailId, CancellationToken cancellationToken)
    {
        var client = await CreateDataverseClientAsync(cancellationToken);
        if (client == null)
            return null;

        var query = $"emails({emailId})?" +
                   "$select=activityid,subject,sender,emailsender,torecipients," +
                          "description,conversationindex,_regardingobjectid_value";

        try
        {
            var response = await client.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to load email {EmailId}: {Status}", emailId, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var record = JsonSerializer.Deserialize<JsonElement>(content);

            return new EmailData
            {
                Id = emailId,
                Subject = GetJsonStringValue(record, "subject"),
                From = GetJsonStringValue(record, "sender") ?? GetJsonStringValue(record, "emailsender"),
                To = GetJsonStringValue(record, "torecipients"),
                ConversationIndex = GetJsonStringValue(record, "conversationindex"),
                RegardingId = GetJsonGuidValue(record, "_regardingobjectid_value"),
                RegardingType = GetJsonStringValue(record, "_regardingobjectid_value@Microsoft.Dynamics.CRM.lookuplogicalname"),
                RegardingName = GetJsonStringValue(record, "_regardingobjectid_value@OData.Community.Display.V1.FormattedValue")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading email {EmailId}", emailId);
            return null;
        }
    }

    private async Task<(Guid Id, string Name)?> FindMatterByTrackingTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var client = await CreateDataverseClientAsync(cancellationToken);
        if (client == null)
            return null;

        // Look for Matter with matching tracking token or reference number
        var query = $"sprk_matters?" +
                   $"$select=sprk_matterid,sprk_name" +
                   $"&$filter=contains(sprk_referencenumber,'{token}') or contains(sprk_name,'{token}')" +
                   $"&$top=1";

        try
        {
            var response = await client.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<JsonElement>(content);

            if (result.TryGetProperty("value", out var values) && values.GetArrayLength() > 0)
            {
                var matter = values[0];
                return (
                    Guid.Parse(matter.GetProperty("sprk_matterid").GetString()!),
                    matter.GetProperty("sprk_name").GetString() ?? "Unknown"
                );
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding Matter by tracking token '{Token}'", token);
            return null;
        }
    }

    private async Task<List<(Guid Id, string Name, DateTime LastActivityDate)>> FindMattersWithRecentSenderActivityAsync(
        string senderEmail,
        CancellationToken cancellationToken)
    {
        var client = await CreateDataverseClientAsync(cancellationToken);
        if (client == null)
            return [];

        // Find recent emails from this sender that are linked to Matters
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var query = $"emails?" +
                   $"$select=activityid,createdon,_regardingobjectid_value" +
                   $"&$filter=contains(sender,'{senderEmail}') and " +
                            $"_regardingobjectid_value ne null and " +
                            $"createdon ge {thirtyDaysAgo}" +
                   $"&$orderby=createdon desc" +
                   $"&$top=10";

        try
        {
            var response = await client.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return [];

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<JsonElement>(content);

            var matters = new Dictionary<Guid, (string Name, DateTime LastActivityDate)>();

            if (result.TryGetProperty("value", out var values))
            {
                foreach (var email in values.EnumerateArray())
                {
                    var regardingType = GetJsonStringValue(email,
                        "_regardingobjectid_value@Microsoft.Dynamics.CRM.lookuplogicalname");

                    if (regardingType == "sprk_matter")
                    {
                        var matterId = GetJsonGuidValue(email, "_regardingobjectid_value");
                        if (matterId.HasValue && !matters.ContainsKey(matterId.Value))
                        {
                            var matterName = GetJsonStringValue(email,
                                "_regardingobjectid_value@OData.Community.Display.V1.FormattedValue") ?? "Unknown";
                            var createdOn = DateTime.Parse(email.GetProperty("createdon").GetString()!);
                            matters[matterId.Value] = (matterName, createdOn);
                        }
                    }
                }
            }

            return matters.Select(m => (m.Key, m.Value.Name, m.Value.LastActivityDate)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding Matters with recent sender activity");
            return [];
        }
    }

    private async Task<List<(Guid Id, string Name)>> FindAccountsByDomainAsync(
        string domain,
        CancellationToken cancellationToken)
    {
        var client = await CreateDataverseClientAsync(cancellationToken);
        if (client == null)
            return [];

        // Find Accounts with matching website or email domain
        var query = $"accounts?" +
                   $"$select=accountid,name" +
                   $"&$filter=contains(websiteurl,'{domain}') or contains(emailaddress1,'{domain}')" +
                   $"&$top=5";

        try
        {
            var response = await client.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return [];

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<JsonElement>(content);

            var accounts = new List<(Guid Id, string Name)>();
            if (result.TryGetProperty("value", out var values))
            {
                foreach (var account in values.EnumerateArray())
                {
                    accounts.Add((
                        Guid.Parse(account.GetProperty("accountid").GetString()!),
                        account.GetProperty("name").GetString() ?? "Unknown"
                    ));
                }
            }

            return accounts;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding Accounts by domain '{Domain}'", domain);
            return [];
        }
    }

    private async Task<List<(Guid Id, string Name)>> FindContactsByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var client = await CreateDataverseClientAsync(cancellationToken);
        if (client == null)
            return [];

        // Find Contacts with matching email address
        var query = $"contacts?" +
                   $"$select=contactid,fullname" +
                   $"&$filter=emailaddress1 eq '{email}' or emailaddress2 eq '{email}'" +
                   $"&$top=5";

        try
        {
            var response = await client.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return [];

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<JsonElement>(content);

            var contacts = new List<(Guid Id, string Name)>();
            if (result.TryGetProperty("value", out var values))
            {
                foreach (var contact in values.EnumerateArray())
                {
                    contacts.Add((
                        Guid.Parse(contact.GetProperty("contactid").GetString()!),
                        contact.GetProperty("fullname").GetString() ?? "Unknown"
                    ));
                }
            }

            return contacts;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding Contacts by email '{Email}'", email);
            return [];
        }
    }

    #endregion

    #region Helpers

    private async Task<HttpClient?> CreateDataverseClientAsync(CancellationToken cancellationToken)
    {
        var dataverseUrl = _configuration["Dataverse:ServiceUrl"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(dataverseUrl))
        {
            _logger.LogWarning("Dataverse:ServiceUrl not configured");
            return null;
        }

        var accessToken = await GetDataverseAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("Failed to acquire Dataverse access token");
            return null;
        }

        var client = _httpClientFactory.CreateClient("DataverseAssociation");
        client.BaseAddress = new Uri($"{dataverseUrl}/api/data/v9.2/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("Prefer", "odata.include-annotations=*");

        return client;
    }

    private async Task<string?> GetDataverseAccessTokenAsync(CancellationToken ct)
    {
        try
        {
            var tenantId = _configuration["AzureAd:TenantId"];
            var clientId = _configuration["AzureAd:ClientId"];
            var clientSecret = _configuration["AzureAd:ClientSecret"];
            var dataverseUrl = _configuration["Dataverse:ServiceUrl"]?.TrimEnd('/');

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) ||
                string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(dataverseUrl))
            {
                _logger.LogWarning("Missing Azure AD or Dataverse configuration");
                return null;
            }

            var app = Microsoft.Identity.Client.ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithTenantId(tenantId)
                .WithClientSecret(clientSecret)
                .Build();

            var scopes = new[] { $"{dataverseUrl}/.default" };
            var result = await app.AcquireTokenForClient(scopes).ExecuteAsync(ct);

            return result.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Dataverse access token");
            return null;
        }
    }

    private static string? ExtractEmailAddress(string emailField)
    {
        if (string.IsNullOrEmpty(emailField))
            return null;

        // Handle formats like "John Doe <john@example.com>" or just "john@example.com"
        var match = Regex.Match(emailField, @"[\w.+-]+@[\w.-]+\.\w+", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    private static string? ExtractDomain(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email[(atIndex + 1)..].ToLowerInvariant() : null;
    }

    private static bool IsCommonEmailProvider(string domain)
    {
        var commonProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gmail.com", "outlook.com", "hotmail.com", "yahoo.com",
            "live.com", "msn.com", "icloud.com", "aol.com",
            "protonmail.com", "mail.com"
        };
        return commonProviders.Contains(domain);
    }

    private static string? GetJsonStringValue(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static Guid? GetJsonGuidValue(JsonElement element, string propertyName)
    {
        var str = GetJsonStringValue(element, propertyName);
        return Guid.TryParse(str, out var guid) ? guid : null;
    }

    #endregion

    /// <summary>
    /// Internal email data structure.
    /// </summary>
    private class EmailData
    {
        public Guid Id { get; init; }
        public string? Subject { get; init; }
        public string? From { get; init; }
        public string? To { get; init; }
        public string? ConversationIndex { get; init; }
        public Guid? RegardingId { get; init; }
        public string? RegardingType { get; init; }
        public string? RegardingName { get; init; }
        /// <summary>
        /// Tracking token from sprk_document.sprk_emailtrackingtoken if already converted.
        /// </summary>
        public string? TrackingToken { get; init; }
    }
}
