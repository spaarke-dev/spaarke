using System.ComponentModel.DataAnnotations;

namespace Spe.Bff.Api.Configuration;

/// <summary>
/// Configuration options for Azure Service Bus queue-based job processing.
/// </summary>
public class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    /// <summary>
    /// Azure Service Bus connection string.
    /// Store in Key Vault (production) or user-secrets (development).
    /// Example: Endpoint=sb://your-namespace.servicebus.windows.net/;SharedAccessKeyName=...
    /// </summary>
    [Required(ErrorMessage = "ServiceBus:ConnectionString is required")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Service Bus queue name for background jobs.
    /// Default: "sdap-jobs"
    /// </summary>
    [Required(ErrorMessage = "ServiceBus:QueueName is required")]
    public string QueueName { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of concurrent message processing calls.
    /// Range: 1-100
    /// Recommended: 5 for staging, 10+ for production
    /// </summary>
    [Range(1, 100, ErrorMessage = "ServiceBus:MaxConcurrentCalls must be between 1 and 100")]
    public int MaxConcurrentCalls { get; set; } = 5;

    /// <summary>
    /// Maximum duration to automatically renew message locks.
    /// Default: 5 minutes
    /// </summary>
    public TimeSpan MaxAutoLockRenewalDuration { get; set; } = TimeSpan.FromMinutes(5);
}
