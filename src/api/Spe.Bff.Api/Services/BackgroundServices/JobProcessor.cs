using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Spe.Bff.Api.Services.Jobs;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Spe.Bff.Api.Services.BackgroundServices;

/// <summary>
/// Background service that processes jobs using the ADR-004 job contract.
/// Provides idempotent processing, retry logic, and poison queue handling.
/// </summary>
public class JobProcessor : BackgroundService
{
    private readonly ILogger<JobProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentQueue<JobContract> _jobQueue;
    private readonly ConcurrentDictionary<string, JobOutcome> _processedJobs;
    public JobProcessor(ILogger<JobProcessor> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _jobQueue = new ConcurrentQueue<JobContract>();
        _processedJobs = new ConcurrentDictionary<string, JobOutcome>();
    }

    /// <summary>
    /// Enqueues a job for processing.
    /// This would typically be called from a Service Bus message handler.
    /// </summary>
    public void EnqueueJob(JobContract job)
    {
        _jobQueue.Enqueue(job);
        _logger.LogInformation("Job {JobId} of type {JobType} enqueued for processing",
            job.JobId, job.JobType);
    }

    /// <summary>
    /// Gets the current queue depth for health monitoring.
    /// </summary>
    public int QueueDepth => _jobQueue.Count;

    /// <summary>
    /// Gets the count of processed jobs (for testing/monitoring).
    /// </summary>
    public int ProcessedJobsCount => _processedJobs.Count;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobProcessor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_jobQueue.TryDequeue(out var job))
                {
                    await ProcessJobAsync(job, stoppingToken);
                }
                else
                {
                    // No jobs to process, wait a bit
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected when shutting down
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in job processor main loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("JobProcessor stopped");
    }

    private async Task ProcessJobAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Check for idempotency - if we've already processed this job successfully, skip it
            if (_processedJobs.TryGetValue(job.IdempotencyKey, out var existingOutcome) &&
                existingOutcome.Status == JobStatus.Completed)
            {
                _logger.LogInformation("Job {JobId} with idempotency key {IdempotencyKey} already processed successfully, skipping",
                    job.JobId, job.IdempotencyKey);
                return;
            }

            _logger.LogInformation("Processing job {JobId} of type {JobType}, attempt {Attempt}/{MaxAttempts}",
                job.JobId, job.JobType, job.Attempt, job.MaxAttempts);

            // Find the appropriate handler
            var handler = await GetJobHandlerAsync(job.JobType);
            if (handler == null)
            {
                var outcome = JobOutcome.Poisoned(job.JobId, job.JobType,
                    $"No handler found for job type {job.JobType}", job.Attempt, stopwatch.Elapsed);

                await RecordOutcomeAsync(job, outcome);
                return;
            }

            // Process the job directly (retry logic handled at the job level)
            var result = await handler.ProcessAsync(job, ct);

            // Record successful outcome
            _processedJobs.TryAdd(job.IdempotencyKey, result);
            await RecordOutcomeAsync(job, result);

            _logger.LogInformation("Job {JobId} completed successfully in {Duration}ms",
                job.JobId, result.Duration.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Job {JobId} processing was cancelled", job.JobId);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            if (job.IsAtMaxAttempts)
            {
                // Send to poison queue
                var poisonOutcome = JobOutcome.Poisoned(job.JobId, job.JobType,
                    ex.Message, job.Attempt, stopwatch.Elapsed);

                _processedJobs.TryAdd(job.IdempotencyKey, poisonOutcome);
                await RecordOutcomeAsync(job, poisonOutcome);

                _logger.LogError(ex, "Job {JobId} failed after {MaxAttempts} attempts, sending to poison queue",
                    job.JobId, job.MaxAttempts);
            }
            else
            {
                // Schedule retry
                var failureOutcome = JobOutcome.Failure(job.JobId, job.JobType,
                    ex.Message, job.Attempt, stopwatch.Elapsed);

                await RecordOutcomeAsync(job, failureOutcome);

                _logger.LogWarning(ex, "Job {JobId} failed on attempt {Attempt}, will retry",
                    job.JobId, job.Attempt);

                // Re-enqueue for retry (in real implementation, this would go back to Service Bus)
                var retryJob = job.CreateRetry();
                EnqueueJob(retryJob);
            }
        }
    }

    private async Task<IJobHandler?> GetJobHandlerAsync(string jobType)
    {
        using var scope = _serviceProvider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IJobHandler>();

        return await Task.FromResult(handlers.FirstOrDefault(h => h.JobType == jobType));
    }

    private async Task RecordOutcomeAsync(JobContract job, JobOutcome outcome)
    {
        // In a real implementation, this would:
        // 1. Persist the outcome to a database
        // 2. Emit metrics for observability
        // 3. Send events to Service Bus for downstream consumers

        _logger.LogInformation("Recording outcome for job {JobId}: {Status} after {Duration}ms",
            outcome.JobId, outcome.Status, outcome.Duration.TotalMilliseconds);

        await Task.CompletedTask;
    }
}