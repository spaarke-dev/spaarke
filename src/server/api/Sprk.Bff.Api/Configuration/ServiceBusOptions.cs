using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for Azure Service Bus queue-based job processing.
/// </summary>
public class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    /// <summary>
    /// Fully-qualified Service Bus namespace, e.g. <c>spaarke-servicebus-dev.servicebus.windows.net</c>.
    /// When set, clients authenticate with the DI-injected managed-identity
    /// <see cref="Azure.Core.TokenCredential"/> and <see cref="ConnectionString"/> is ignored.
    /// </summary>
    /// <remarks>
    /// <para>This is the target state (auth-v4 task 051 / FR-E2): a SAS connection string is a
    /// bearer secret with no rotation story, and the one in dev had leaked into a local settings
    /// file. The namespace path carries no secret at all.</para>
    /// <para>Requires <c>Azure Service Bus Data Sender</c> + <c>Azure Service Bus Data Receiver</c>
    /// at <b>namespace</b> scope for the UAMI — a topic-scoped grant does not cover the job queues.
    /// Selection happens in <see cref="Infrastructure.Auth.ServiceBusClientFactory"/>.</para>
    /// </remarks>
    public string FullyQualifiedNamespace { get; set; } = string.Empty;

    /// <summary>
    /// Azure Service Bus SAS connection string. Transitional — prefer
    /// <see cref="FullyQualifiedNamespace"/>.
    /// Example: Endpoint=sb://your-namespace.servicebus.windows.net/;SharedAccessKeyName=...
    /// </summary>
    /// <remarks>
    /// <para><b>[Required] removed by auth-v4 task 051.</b> The attribute made the SAS string
    /// mandatory at <c>ValidateOnStart</c>, so the app could not boot on the managed-identity path
    /// — the credential would have had to stay in config forever to satisfy a validator that only
    /// existed to catch the case where NO credential is set. That check now lives in
    /// <see cref="Infrastructure.Auth.ServiceBusClientFactory.Create"/>, where it can accept
    /// <i>either</i> credential and still fail loudly when both are absent.</para>
    /// <para>Same latent-blocker class as <c>DocumentIntelligenceOptionsValidator</c> (task 054):
    /// a validator that outlives the credential it was written for silently blocks its own
    /// removal.</para>
    /// </remarks>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Service Bus queue name for background jobs.
    /// Default: "sdap-jobs"
    /// </summary>
    [Required(ErrorMessage = "ServiceBus:QueueName is required")]
    public string QueueName { get; set; } = string.Empty;

    /// <summary>
    /// Dedicated Service Bus queue for communication/email jobs.
    /// Isolates email processing from the shared job queue to prevent
    /// cross-domain failures (e.g., a broken finance handler blocking email processing).
    /// Default: "sdap-communication"
    /// </summary>
    public string CommunicationQueueName { get; set; } = "sdap-communication";

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
