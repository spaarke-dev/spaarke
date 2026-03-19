using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;

/// <summary>
/// Hosted background service that runs the playbook embedding indexing queue processor.
/// Follows ADR-001 (BackgroundService for async processing, no Azure Functions).
/// </summary>
/// <remarks>
/// <para>
/// This service creates and owns the <see cref="PlaybookIndexingService"/> instance
/// using factory instantiation (ADR-010 — no new DI registrations for this feature).
/// The <see cref="PlaybookIndexingService"/> is exposed via a static accessor so that
/// the trigger endpoint can enqueue indexing requests without DI registration.
/// </para>
/// <para>
/// Lifecycle: starts with the host, processes indexing requests from the channel until
/// the host shuts down. Graceful shutdown drains pending requests.
/// </para>
/// </remarks>
public sealed class PlaybookIndexingBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PlaybookIndexingBackgroundService> _logger;

    /// <summary>
    /// Static accessor for the indexing service instance. Used by the trigger endpoint
    /// to enqueue indexing requests without DI registration (ADR-010).
    /// </summary>
    /// <remarks>
    /// Thread-safe: set once during <see cref="ExecuteAsync"/> before processing starts.
    /// Null when the background service has not yet started or has been stopped.
    /// </remarks>
    internal static PlaybookIndexingService? Instance { get; private set; }

    /// <summary>
    /// Initializes a new instance of <see cref="PlaybookIndexingBackgroundService"/>.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving dependencies.</param>
    /// <param name="logger">Logger instance.</param>
    public PlaybookIndexingBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<PlaybookIndexingBackgroundService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates the <see cref="PlaybookIndexingService"/> and starts processing the queue.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PlaybookIndexingBackgroundService starting");

        try
        {
            // Resolve dependencies from DI and factory-instantiate the indexing service
            using var scope = _serviceProvider.CreateScope();
            var playbookService = scope.ServiceProvider.GetRequiredService<IPlaybookService>();
            var searchIndexClient = scope.ServiceProvider.GetRequiredService<SearchIndexClient>();
            var openAiClient = scope.ServiceProvider.GetRequiredService<IOpenAiClient>();
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

            var indexingService = new PlaybookIndexingService(
                playbookService,
                searchIndexClient,
                openAiClient,
                loggerFactory);

            // Expose to static accessor for endpoint access
            Instance = indexingService;

            _logger.LogInformation("PlaybookIndexingBackgroundService ready — processing queue");

            // Process the queue until shutdown
            await indexingService.ProcessQueueAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("PlaybookIndexingBackgroundService stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlaybookIndexingBackgroundService encountered a fatal error");
            throw;
        }
        finally
        {
            Instance = null;
        }
    }
}
