using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Implementation of email filter service that evaluates rules from Dataverse.
/// Rules are cached in Redis with configurable TTL (default 5 minutes per NFR-06).
/// </summary>
public class EmailFilterService : IEmailFilterService
{
    private readonly IDistributedCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly EmailProcessingOptions _options;
    private readonly ILogger<EmailFilterService> _logger;

    private const string CacheKey = "email:filter:rules";
    private static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromSeconds(1);

    public EmailFilterService(
        IDistributedCache cache,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IOptions<EmailProcessingOptions> options,
        ILogger<EmailFilterService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EmailFilterResult> EvaluateAsync(EmailFilterContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        _logger.LogDebug(
            "Evaluating filter rules for email {EmailId}, Subject={Subject}, From={From}",
            context.EmailId, context.Subject, context.From);

        var rules = await GetActiveRulesAsync(cancellationToken);

        if (rules.Count == 0)
        {
            _logger.LogDebug("No active filter rules found, applying default action: {Action}", _options.DefaultAction);
            return GetDefaultResult();
        }

        // Evaluate rules in priority order (already sorted)
        foreach (var rule in rules)
        {
            try
            {
                if (MatchesRule(context, rule))
                {
                    _logger.LogInformation(
                        "Email {EmailId} matched rule '{RuleName}' (Priority={Priority}, Type={RuleType})",
                        context.EmailId, rule.Name, rule.Priority, rule.RuleType);

                    return CreateResultFromRule(rule);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Error evaluating rule '{RuleName}' for email {EmailId}, skipping rule",
                    rule.Name, context.EmailId);
                // Continue to next rule
            }
        }

        // No rule matched - apply default action
        _logger.LogDebug("No rules matched for email {EmailId}, applying default action: {Action}",
            context.EmailId, _options.DefaultAction);

        return GetDefaultResult();
    }

    public async Task RefreshRulesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Refreshing email filter rules cache");

        try
        {
            // Remove from cache
            await _cache.RemoveAsync(CacheKey, cancellationToken);

            // Reload (will be cached on next GetActiveRulesAsync call)
            await GetActiveRulesAsync(cancellationToken);

            _logger.LogInformation("Email filter rules cache refreshed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh email filter rules cache");
            throw;
        }
    }

    public async Task<IReadOnlyList<EmailFilterRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default)
    {
        // Try to get from cache first
        try
        {
            var cachedJson = await _cache.GetStringAsync(CacheKey, cancellationToken);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                var cachedRules = JsonSerializer.Deserialize<List<EmailFilterRule>>(cachedJson);
                if (cachedRules != null)
                {
                    _logger.LogDebug("Loaded {Count} filter rules from cache", cachedRules.Count);
                    return cachedRules.AsReadOnly();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading filter rules from cache, loading from Dataverse");
        }

        // Load from Dataverse
        var rules = await LoadRulesFromDataverseAsync(cancellationToken);

        // Cache the rules
        try
        {
            var rulesJson = JsonSerializer.Serialize(rules);
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.FilterRuleCacheTtlMinutes)
            };

            await _cache.SetStringAsync(CacheKey, rulesJson, cacheOptions, cancellationToken);
            _logger.LogDebug("Cached {Count} filter rules for {Ttl} minutes",
                rules.Count, _options.FilterRuleCacheTtlMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error caching filter rules");
            // Continue without caching
        }

        return rules.AsReadOnly();
    }

    private async Task<List<EmailFilterRule>> LoadRulesFromDataverseAsync(CancellationToken cancellationToken)
    {
        var dataverseUrl = _configuration["Dataverse:ServiceUrl"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(dataverseUrl))
        {
            _logger.LogWarning("Dataverse:ServiceUrl not configured, returning empty rule set");
            return [];
        }

        var client = _httpClientFactory.CreateClient("DataversePolling");

        // Get access token for Dataverse
        var accessToken = await GetDataverseAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("Failed to acquire Dataverse access token, returning empty rule set");
            return [];
        }

        client.BaseAddress = new Uri($"{dataverseUrl}/api/data/v9.2/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Query active rules ordered by priority
        var query = "sprk_emailprocessingrules?" +
                   "$select=sprk_emailprocessingruleid,sprk_name,sprk_ruletype,sprk_priority," +
                          "sprk_isactive,sprk_targetfield,sprk_pattern,sprk_criteriajson,sprk_description" +
                   "&$filter=sprk_isactive eq true" +
                   "&$orderby=sprk_priority asc";

        try
        {
            var response = await client.GetAsync(query, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Dataverse query for filter rules failed with status {StatusCode}: {Error}",
                    response.StatusCode, errorContent);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<ODataResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Value == null)
            {
                _logger.LogDebug("No filter rules found in Dataverse");
                return [];
            }

            var rules = result.Value.Select(MapToRule).ToList();
            _logger.LogInformation("Loaded {Count} active filter rules from Dataverse", rules.Count);

            return rules;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error querying Dataverse for filter rules");
            return [];
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing Dataverse response for filter rules");
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
                _logger.LogWarning("Missing Azure AD or Dataverse configuration for filter service");
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
            _logger.LogError(ex, "Failed to acquire Dataverse access token for filter service");
            return null;
        }
    }

    private static EmailFilterRule MapToRule(DataverseRuleRecord record)
    {
        return new EmailFilterRule
        {
            Id = record.SprK_EmailProcessingRuleId,
            Name = record.SprK_Name ?? string.Empty,
            RuleType = (EmailRuleType)(record.SprK_RuleType ?? 0),
            Priority = record.SprK_Priority ?? 100,
            IsActive = record.SprK_IsActive ?? true,
            TargetField = record.SprK_TargetField ?? string.Empty,
            Pattern = record.SprK_Pattern ?? string.Empty,
            CriteriaJson = record.SprK_CriteriaJson,
            Description = record.SprK_Description,
            CreateAttachmentDocuments = true  // Default, could be added to entity
        };
    }

    private bool MatchesRule(EmailFilterContext context, EmailFilterRule rule)
    {
        if (string.IsNullOrEmpty(rule.Pattern) || string.IsNullOrEmpty(rule.TargetField))
        {
            return false;
        }

        var targetValue = GetTargetFieldValue(context, rule.TargetField);
        if (string.IsNullOrEmpty(targetValue))
        {
            return false;
        }

        try
        {
            // Use compiled regex with timeout for safety
            var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase, DefaultRegexTimeout);
            return regex.IsMatch(targetValue);
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger.LogWarning(ex,
                "Regex timeout for rule '{RuleName}' with pattern '{Pattern}'",
                rule.Name, rule.Pattern);
            return false;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex,
                "Invalid regex pattern in rule '{RuleName}': '{Pattern}'",
                rule.Name, rule.Pattern);
            return false;
        }
    }

    private static string? GetTargetFieldValue(EmailFilterContext context, string targetField)
    {
        return targetField.ToLowerInvariant() switch
        {
            "subject" => context.Subject,
            "from" => context.From,
            "to" => context.To,
            "cc" => context.Cc,
            "body" => context.Body,
            "attachmentname" or "attachmentnames" =>
                context.AttachmentNames.Count > 0
                    ? string.Join(";", context.AttachmentNames)
                    : null,
            "regardingtype" => context.RegardingEntityType,
            _ => null
        };
    }

    private EmailFilterResult CreateResultFromRule(EmailFilterRule rule)
    {
        return rule.RuleType switch
        {
            EmailRuleType.Exclude => EmailFilterResult.Ignore(rule, $"Excluded by rule: {rule.Name}"),
            EmailRuleType.Include => EmailFilterResult.Process(rule, rule.CreateAttachmentDocuments),
            EmailRuleType.Route => EmailFilterResult.RequireReview(rule, $"Routed by rule: {rule.Name}"),
            _ => EmailFilterResult.Process(rule, rule.CreateAttachmentDocuments)
        };
    }

    private EmailFilterResult GetDefaultResult()
    {
        return _options.DefaultAction?.ToLowerInvariant() switch
        {
            "autosave" => EmailFilterResult.Process(null, true),
            "reviewrequired" => EmailFilterResult.RequireReview(null, "Default action: Review required"),
            _ => EmailFilterResult.Ignore(null, "Default action: Ignore")
        };
    }

    /// <summary>
    /// OData response wrapper for Dataverse queries.
    /// </summary>
    private class ODataResponse
    {
        public List<DataverseRuleRecord>? Value { get; set; }
    }

    /// <summary>
    /// Dataverse record for sprk_emailprocessingrule entity.
    /// Property names use exact casing from Dataverse OData response.
    /// </summary>
    private class DataverseRuleRecord
    {
        public Guid SprK_EmailProcessingRuleId { get; set; }
        public string? SprK_Name { get; set; }
        public int? SprK_RuleType { get; set; }
        public int? SprK_Priority { get; set; }
        public bool? SprK_IsActive { get; set; }
        public string? SprK_TargetField { get; set; }
        public string? SprK_Pattern { get; set; }
        public string? SprK_CriteriaJson { get; set; }
        public string? SprK_Description { get; set; }
    }
}
