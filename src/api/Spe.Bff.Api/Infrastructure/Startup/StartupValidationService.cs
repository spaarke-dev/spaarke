using Microsoft.Extensions.Options;
using Spe.Bff.Api.Configuration;

namespace Spe.Bff.Api.Infrastructure.Startup;

/// <summary>
/// Validates configuration and dependencies at startup.
/// Fails fast if critical configuration is missing.
/// </summary>
public class StartupValidationService : IHostedService
{
    private readonly ILogger<StartupValidationService> _logger;
    private readonly IOptions<GraphOptions> _graphOptions;
    private readonly IOptions<DataverseOptions> _dataverseOptions;
    private readonly IOptions<ServiceBusOptions> _serviceBusOptions;
    private readonly IOptions<RedisOptions> _redisOptions;

    public StartupValidationService(
        ILogger<StartupValidationService> logger,
        IOptions<GraphOptions> graphOptions,
        IOptions<DataverseOptions> dataverseOptions,
        IOptions<ServiceBusOptions> serviceBusOptions,
        IOptions<RedisOptions> redisOptions)
    {
        _logger = logger;
        _graphOptions = graphOptions;
        _dataverseOptions = dataverseOptions;
        _serviceBusOptions = serviceBusOptions;
        _redisOptions = redisOptions;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting configuration validation...");

        try
        {
            // Access .Value to trigger validation
            _ = _graphOptions.Value;
            _ = _dataverseOptions.Value;
            _ = _serviceBusOptions.Value;
            _ = _redisOptions.Value;

            _logger.LogInformation("✅ Configuration validation successful");
            LogConfigurationSummary();

            return Task.CompletedTask;
        }
        catch (OptionsValidationException ex)
        {
            _logger.LogCritical(ex, "❌ Configuration validation failed. Application cannot start.");
            _logger.LogCritical("Validation errors:");
            foreach (var failure in ex.Failures)
            {
                _logger.LogCritical("  - {Error}", failure);
            }

            _logger.LogCritical("");
            _logger.LogCritical("Please check your appsettings.json, environment variables, and user secrets.");
            _logger.LogCritical("See README-Secrets.md for local development setup.");

            // Fail fast - stop application startup
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void LogConfigurationSummary()
    {
        var graph = _graphOptions.Value;
        var dataverse = _dataverseOptions.Value;
        var serviceBus = _serviceBusOptions.Value;
        var redis = _redisOptions.Value;

        _logger.LogInformation("Configuration Summary:");
        _logger.LogInformation("  Graph API:");
        _logger.LogInformation("    - TenantId: {TenantId}", graph.TenantId);
        _logger.LogInformation("    - ClientId: {ClientId}", MaskSensitive(graph.ClientId));
        _logger.LogInformation("    - ManagedIdentity: {Enabled}", graph.ManagedIdentity.Enabled);
        if (graph.ManagedIdentity.Enabled)
        {
            _logger.LogInformation("    - UAMI ClientId: {ClientId}", MaskSensitive(graph.ManagedIdentity.ClientId ?? ""));
        }

        _logger.LogInformation("  Dataverse:");
        _logger.LogInformation("    - Environment: {Url}", dataverse.EnvironmentUrl);
        _logger.LogInformation("    - ClientId: {ClientId}", MaskSensitive(dataverse.ClientId));

        _logger.LogInformation("  Service Bus:");
        _logger.LogInformation("    - Queue: {QueueName}", serviceBus.QueueName);
        _logger.LogInformation("    - MaxConcurrency: {MaxConcurrency}", serviceBus.MaxConcurrentCalls);

        _logger.LogInformation("  Redis:");
        _logger.LogInformation("    - Enabled: {Enabled}", redis.Enabled);
        _logger.LogInformation("    - InstanceName: {InstanceName}", redis.InstanceName);
    }

    /// <summary>
    /// Masks sensitive values for logging (shows first 4 and last 4 characters).
    /// </summary>
    private string MaskSensitive(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 8)
        {
            return "****";
        }

        return $"{value[..4]}...{value[^4..]}";
    }
}
