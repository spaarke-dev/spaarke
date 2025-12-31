using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Service for seeding default email processing rules in Dataverse.
/// Creates exclusion rules per NFR-05 (blocked extensions) and common patterns.
/// </summary>
public class EmailRuleSeedService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailRuleSeedService> _logger;

    public EmailRuleSeedService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<EmailRuleSeedService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Default exclusion rules for email processing.
    /// These rules exclude common system emails and dangerous attachments.
    /// </summary>
    public static readonly IReadOnlyList<SeedRule> DefaultRules =
    [
        // ═══════════════════════════════════════════════════════════════════════════
        // NFR-05: Blocked Attachment Extensions (Priority 1-10)
        // ═══════════════════════════════════════════════════════════════════════════
        new SeedRule
        {
            Name = "Block Executable Attachments (.exe)",
            Description = "NFR-05: Block emails with .exe attachments for security",
            RuleType = 0, // Exclude
            Priority = 1,
            TargetField = "attachmentname",
            Pattern = @"\.exe$",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Block DLL Attachments (.dll)",
            Description = "NFR-05: Block emails with .dll attachments for security",
            RuleType = 0, // Exclude
            Priority = 2,
            TargetField = "attachmentname",
            Pattern = @"\.dll$",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Block Batch File Attachments (.bat, .cmd)",
            Description = "NFR-05: Block emails with batch file attachments for security",
            RuleType = 0, // Exclude
            Priority = 3,
            TargetField = "attachmentname",
            Pattern = @"\.(bat|cmd)$",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Block PowerShell Attachments (.ps1, .psm1)",
            Description = "NFR-05: Block emails with PowerShell script attachments for security",
            RuleType = 0, // Exclude
            Priority = 4,
            TargetField = "attachmentname",
            Pattern = @"\.(ps1|psm1|psd1)$",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Block VBScript Attachments (.vbs, .vbe)",
            Description = "NFR-05: Block emails with VBScript attachments for security",
            RuleType = 0, // Exclude
            Priority = 5,
            TargetField = "attachmentname",
            Pattern = @"\.(vbs|vbe)$",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Block JavaScript Attachments (.js, .jse)",
            Description = "NFR-05: Block emails with JavaScript attachments for security",
            RuleType = 0, // Exclude
            Priority = 6,
            TargetField = "attachmentname",
            Pattern = @"\.(js|jse)$",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Block Windows Script Host (.wsf, .wsh)",
            Description = "Block emails with Windows Script Host attachments",
            RuleType = 0, // Exclude
            Priority = 7,
            TargetField = "attachmentname",
            Pattern = @"\.(wsf|wsh|wsc)$",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Block MSI Installer Attachments",
            Description = "Block emails with Windows installer attachments",
            RuleType = 0, // Exclude
            Priority = 8,
            TargetField = "attachmentname",
            Pattern = @"\.(msi|msp)$",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Block Shortcut Files (.lnk, .url)",
            Description = "Block emails with shortcut file attachments",
            RuleType = 0, // Exclude
            Priority = 9,
            TargetField = "attachmentname",
            Pattern = @"\.(lnk|url|scf)$",
            IsActive = true
        },

        // ═══════════════════════════════════════════════════════════════════════════
        // System Email Exclusions (Priority 20-29)
        // ═══════════════════════════════════════════════════════════════════════════
        new SeedRule
        {
            Name = "Exclude No-Reply Addresses",
            Description = "Skip automated noreply/no-reply system emails",
            RuleType = 0, // Exclude
            Priority = 20,
            TargetField = "from",
            Pattern = @"no[-_]?reply@",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Exclude Mailer Daemon",
            Description = "Skip bounce/delivery failure notifications",
            RuleType = 0, // Exclude
            Priority = 21,
            TargetField = "from",
            Pattern = @"(mailer-daemon|postmaster)@",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Exclude Calendar Notifications",
            Description = "Skip calendar invite/response emails (processed separately)",
            RuleType = 0, // Exclude
            Priority = 22,
            TargetField = "subject",
            Pattern = @"^(Accepted|Declined|Tentative|Canceled):|Meeting (Request|Response|Canceled)",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Exclude Out of Office Replies",
            Description = "Skip automatic out-of-office responses",
            RuleType = 0, // Exclude
            Priority = 23,
            TargetField = "subject",
            Pattern = @"(Out of Office|Automatic reply|Auto-Reply|I am currently out)",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Exclude Delivery Status Notifications",
            Description = "Skip email delivery failure/success notifications",
            RuleType = 0, // Exclude
            Priority = 24,
            TargetField = "subject",
            Pattern = @"(Delivery Status Notification|Undeliverable|Mail Delivery Failed|Returned mail)",
            IsActive = true
        },

        // ═══════════════════════════════════════════════════════════════════════════
        // Include Rules - Matter-Related Emails (Priority 50-59)
        // ═══════════════════════════════════════════════════════════════════════════
        new SeedRule
        {
            Name = "Include Emails Regarding Matters",
            Description = "Auto-save emails that are linked to a Matter",
            RuleType = 1, // Include
            Priority = 50,
            TargetField = "regardingtype",
            Pattern = @"^sprk_matter$",
            IsActive = true
        },
        new SeedRule
        {
            Name = "Include Emails Regarding Projects",
            Description = "Auto-save emails that are linked to a Project",
            RuleType = 1, // Include
            Priority = 51,
            TargetField = "regardingtype",
            Pattern = @"^sprk_project$",
            IsActive = true
        }
    ];

    /// <summary>
    /// Seeds default email processing rules to Dataverse if they don't already exist.
    /// Uses rule name for existence check.
    /// </summary>
    /// <param name="forceUpdate">If true, updates existing rules with new values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of seed operation.</returns>
    public async Task<SeedResult> SeedDefaultRulesAsync(bool forceUpdate = false, CancellationToken cancellationToken = default)
    {
        var result = new SeedResult();

        var dataverseUrl = _configuration["Dataverse:ServiceUrl"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(dataverseUrl))
        {
            _logger.LogWarning("Dataverse:ServiceUrl not configured, cannot seed rules");
            result.Errors.Add("Dataverse:ServiceUrl not configured");
            return result;
        }

        var accessToken = await GetDataverseAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("Failed to acquire Dataverse access token");
            result.Errors.Add("Failed to acquire Dataverse access token");
            return result;
        }

        var client = _httpClientFactory.CreateClient("DataversePolling");
        client.BaseAddress = new Uri($"{dataverseUrl}/api/data/v9.2/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Get existing rules to check for duplicates
        var existingRules = await GetExistingRuleNamesAsync(client, cancellationToken);

        foreach (var rule in DefaultRules)
        {
            try
            {
                if (existingRules.Contains(rule.Name))
                {
                    if (forceUpdate)
                    {
                        // Update existing rule (would need rule ID - skip for now)
                        _logger.LogDebug("Rule '{RuleName}' already exists, skipping (forceUpdate not fully implemented)", rule.Name);
                        result.Skipped++;
                    }
                    else
                    {
                        _logger.LogDebug("Rule '{RuleName}' already exists, skipping", rule.Name);
                        result.Skipped++;
                    }
                    continue;
                }

                // Create new rule
                var payload = new Dictionary<string, object>
                {
                    ["sprk_name"] = rule.Name,
                    ["sprk_description"] = rule.Description ?? string.Empty,
                    ["sprk_ruletype"] = rule.RuleType,
                    ["sprk_priority"] = rule.Priority,
                    ["sprk_targetfield"] = rule.TargetField,
                    ["sprk_pattern"] = rule.Pattern,
                    ["sprk_isactive"] = rule.IsActive
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("sprk_emailprocessingrules", content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Created email processing rule: {RuleName}", rule.Name);
                    result.Created++;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Failed to create rule '{RuleName}': {StatusCode} - {Error}",
                        rule.Name, response.StatusCode, error);
                    result.Errors.Add($"{rule.Name}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating rule '{RuleName}'", rule.Name);
                result.Errors.Add($"{rule.Name}: {ex.Message}");
            }
        }

        _logger.LogInformation(
            "Email rule seeding complete. Created: {Created}, Skipped: {Skipped}, Errors: {Errors}",
            result.Created, result.Skipped, result.Errors.Count);

        return result;
    }

    private async Task<HashSet<string>> GetExistingRuleNamesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetAsync(
                "sprk_emailprocessingrules?$select=sprk_name",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to query existing rules: {StatusCode}", response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("value", out var valueArray))
            {
                foreach (var element in valueArray.EnumerateArray())
                {
                    if (element.TryGetProperty("sprk_name", out var nameElement) &&
                        nameElement.ValueKind == JsonValueKind.String)
                    {
                        names.Add(nameElement.GetString()!);
                    }
                }
            }

            return names;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying existing rules");
            return [];
        }
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
                _logger.LogWarning("Missing Azure AD or Dataverse configuration for rule seed service");
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
            _logger.LogError(ex, "Failed to acquire Dataverse access token for rule seed service");
            return null;
        }
    }

    /// <summary>
    /// Rule definition for seeding.
    /// </summary>
    public record SeedRule
    {
        public required string Name { get; init; }
        public string? Description { get; init; }
        public int RuleType { get; init; } // 0=Exclude, 1=Include, 2=Route
        public int Priority { get; init; }
        public required string TargetField { get; init; }
        public required string Pattern { get; init; }
        public bool IsActive { get; init; } = true;
    }

    /// <summary>
    /// Result of seed operation.
    /// </summary>
    public class SeedResult
    {
        public int Created { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; } = [];
        public bool IsSuccess => Errors.Count == 0;
    }
}
