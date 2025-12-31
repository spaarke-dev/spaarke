using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Background service that polls Dataverse for unprocessed emails.
/// Acts as a backup to the webhook trigger, ensuring reliable processing
/// even if webhooks fail due to network issues or service restarts.
///
/// Implements ADR-001 BackgroundService pattern with PeriodicTimer.
/// Uses same IdempotencyKey format as webhook (Email:{emailId}:Archive) for deduplication.
/// </summary>
public class EmailPollingBackupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmailPollingBackupService> _logger;
    private readonly EmailProcessingOptions _options;
    private readonly IConfiguration _configuration;

    private const string JobTypeProcessEmail = "ProcessEmailToDocument";

    public EmailPollingBackupService(
        IServiceProvider serviceProvider,
        IOptions<EmailProcessingOptions> options,
        IConfiguration configuration,
        ILogger<EmailPollingBackupService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !_options.EnablePolling)
        {
            _logger.LogInformation(
                "Email polling backup service disabled (Enabled={Enabled}, EnablePolling={EnablePolling})",
                _options.Enabled, _options.EnablePolling);
            return;
        }

        var interval = TimeSpan.FromMinutes(_options.PollingIntervalMinutes);
        _logger.LogInformation(
            "Email polling backup service starting with {Interval} minute interval, {LookbackHours} hour lookback, batch size {BatchSize}",
            _options.PollingIntervalMinutes,
            _options.PollingLookbackHours,
            _options.PollingBatchSize);

        // Use PeriodicTimer for efficient periodic execution (ADR-001 pattern)
        using var timer = new PeriodicTimer(interval);

        // Execute immediately on startup, then on interval
        await EnqueueMissedEmailsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await EnqueueMissedEmailsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Email polling backup service stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during email polling cycle, will retry on next interval");
                // Continue running - don't let one failure stop the service
            }
        }

        _logger.LogInformation("Email polling backup service stopped");
    }

    /// <summary>
    /// Queries Dataverse for unprocessed emails and enqueues jobs for processing.
    /// Uses OData query to find completed emails without associated documents.
    /// </summary>
    private async Task EnqueueMissedEmailsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var correlationId = Guid.NewGuid().ToString();

        _logger.LogDebug(
            "Starting email polling cycle, correlation {CorrelationId}",
            correlationId);

        try
        {
            var unprocessedEmails = await GetUnprocessedEmailsAsync(scope.ServiceProvider, ct);
            var emailList = unprocessedEmails.ToList();

            if (emailList.Count == 0)
            {
                _logger.LogDebug("No unprocessed emails found during polling cycle");
                return;
            }

            _logger.LogInformation(
                "Found {Count} unprocessed emails during polling backup, correlation {CorrelationId}",
                emailList.Count, correlationId);

            var jobSubmissionService = scope.ServiceProvider.GetRequiredService<JobSubmissionService>();
            var enqueuedCount = 0;
            var skippedCount = 0;

            foreach (var email in emailList)
            {
                try
                {
                    var job = CreateJobContract(email.EmailId, correlationId);
                    await jobSubmissionService.SubmitJobAsync(job, ct);
                    enqueuedCount++;

                    _logger.LogDebug(
                        "Enqueued email {EmailId} for processing via polling backup, job {JobId}",
                        email.EmailId, job.JobId);
                }
                catch (Exception ex)
                {
                    skippedCount++;
                    _logger.LogWarning(ex,
                        "Failed to enqueue email {EmailId} during polling backup",
                        email.EmailId);
                }
            }

            _logger.LogInformation(
                "Polling backup cycle complete: {Enqueued} enqueued, {Skipped} skipped, correlation {CorrelationId}",
                enqueuedCount, skippedCount, correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to complete email polling cycle, correlation {CorrelationId}",
                correlationId);
            throw;
        }
    }

    /// <summary>
    /// Queries Dataverse for emails that meet the following criteria:
    /// - statecode = 1 (Completed - email is sent/received)
    /// - createdon within lookback window
    /// - No existing sprk_document record linked via sprk_emailactivityid
    /// </summary>
    private async Task<IEnumerable<UnprocessedEmail>> GetUnprocessedEmailsAsync(
        IServiceProvider services,
        CancellationToken ct)
    {
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        var client = httpClientFactory.CreateClient("DataversePolling");

        // Configure Dataverse client
        var dataverseUrl = _configuration["Dataverse:ServiceUrl"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(dataverseUrl))
        {
            _logger.LogWarning("Dataverse:ServiceUrl not configured, skipping polling");
            return [];
        }

        // Get access token for Dataverse
        var accessToken = await GetDataverseAccessTokenAsync(services, ct);
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("Failed to acquire Dataverse access token, skipping polling");
            return [];
        }

        client.BaseAddress = new Uri($"{dataverseUrl}/api/data/v9.2/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("Prefer", $"odata.maxpagesize={_options.PollingBatchSize}");

        // Calculate lookback window
        var lookbackDate = DateTime.UtcNow.AddHours(-_options.PollingLookbackHours);
        var lookbackDateStr = lookbackDate.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // Query for completed emails created within lookback window
        // that don't have a linked document record
        // Note: This query uses a left join pattern via $expand with $filter
        var query = $"emails?" +
                   $"$select=activityid,subject,createdon,directioncode" +
                   $"&$filter=statecode eq 1 " +  // Completed state
                   $"and createdon ge {lookbackDateStr} " +
                   $"and sprk_documentprocessingstatus eq null " +  // Not yet processed
                   $"&$orderby=createdon desc" +
                   $"&$top={_options.PollingBatchSize}";

        try
        {
            var response = await client.GetAsync(query, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Dataverse query failed with status {StatusCode}: {Error}",
                    response.StatusCode, errorContent);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<ODataResponse<EmailRecord>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Value == null)
            {
                return [];
            }

            return result.Value.Select(e => new UnprocessedEmail
            {
                EmailId = e.ActivityId,
                Subject = e.Subject,
                CreatedOn = e.CreatedOn,
                DirectionCode = e.DirectionCode
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error querying Dataverse for unprocessed emails");
            return [];
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing Dataverse response for unprocessed emails");
            return [];
        }
    }

    /// <summary>
    /// Gets an access token for Dataverse using client credentials flow.
    /// </summary>
    private async Task<string?> GetDataverseAccessTokenAsync(IServiceProvider services, CancellationToken ct)
    {
        try
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var tenantId = configuration["AzureAd:TenantId"];
            var clientId = configuration["AzureAd:ClientId"];
            var clientSecret = configuration["AzureAd:ClientSecret"];
            var dataverseUrl = configuration["Dataverse:ServiceUrl"]?.TrimEnd('/');

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) ||
                string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(dataverseUrl))
            {
                _logger.LogWarning("Missing Azure AD or Dataverse configuration for polling service");
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
            _logger.LogError(ex, "Failed to acquire Dataverse access token for polling");
            return null;
        }
    }

    /// <summary>
    /// Creates a JobContract for email processing with the standard IdempotencyKey format.
    /// </summary>
    private JobContract CreateJobContract(Guid emailId, string correlationId)
    {
        var payloadJson = JsonSerializer.Serialize(new
        {
            EmailId = emailId,
            TriggerSource = "PollingBackup",
            TriggeredAt = DateTime.UtcNow.ToString("o")
        });

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = JobTypeProcessEmail,
            SubjectId = emailId.ToString(),
            CorrelationId = correlationId,
            IdempotencyKey = $"Email:{emailId}:Archive",  // Same format as webhook
            Payload = JsonDocument.Parse(payloadJson),
            MaxAttempts = 3,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Represents an unprocessed email found during polling.
    /// </summary>
    private record UnprocessedEmail
    {
        public Guid EmailId { get; init; }
        public string? Subject { get; init; }
        public DateTime? CreatedOn { get; init; }
        public bool DirectionCode { get; init; }  // true = Incoming, false = Outgoing
    }

    /// <summary>
    /// OData response wrapper for Dataverse queries.
    /// </summary>
    private class ODataResponse<T>
    {
        public List<T>? Value { get; set; }
    }

    /// <summary>
    /// Email record from Dataverse query.
    /// </summary>
    private class EmailRecord
    {
        public Guid ActivityId { get; set; }
        public string? Subject { get; set; }
        public DateTime? CreatedOn { get; set; }
        public bool DirectionCode { get; set; }
    }
}
