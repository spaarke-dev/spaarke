using System.Globalization;
using Azure.Messaging.ServiceBus;

namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// ISS-025 / DEF-055: the retry-with-back-off and settlement rules the two ADR-004 queue consumers share,
/// <see cref="ServiceBusJobProcessor"/> (<c>sdap-jobs</c>) and <c>CommunicationJobProcessor</c> (<c>sdap-communication</c>). A static
/// helper the processors call (no DI registration); each processor owns one <see cref="RetrySender"/> for its OWN queue.
/// </summary>
/// <remarks>
/// <para><b>Logical delivery count.</b> A retry is a copy of the message, and the broker starts the copy's delivery count at 1, so the
/// copy carries the deliveries earlier copies spent in <see cref="JobContract.PriorDeliveryCountProperty"/>.
/// <see cref="LogicalDeliveryCount"/> = that property + the broker's count for this message; it is what
/// <see cref="JobContract.DeliveryCount"/> holds and what the processors' dead-letter rules count.</para>
/// <para><b>Retry later</b> (<see cref="RetryLaterAsync"/>): schedule the copy, then complete the original. See that method for what
/// happens when either step fails.</para>
/// <para><b>Precondition: duplicate detection is OFF on both queues.</b> The copy reuses the original's <c>MessageId</c>; with
/// detection ON the broker would silently drop it after the original is completed, losing the job. Enforced by:
/// <c>JobQueueDuplicateDetectionOffGuardTests</c> (<c>tests/Spaarke.ArchTests</c>), which scans the bicep queue resources.</para>
/// </remarks>
internal static class JobMessageRetry
{
    /// <summary>
    /// The deliveries earlier copies of this job spent (<see cref="JobContract.PriorDeliveryCountProperty"/>): 0 when the property is
    /// absent or not a whole number, and never negative. Bounded, so adding the broker's own count cannot overflow.
    /// </summary>
    internal static int ReadPriorDeliveryCount(ServiceBusReceivedMessage message)
    {
        if (!message.ApplicationProperties.TryGetValue(JobContract.PriorDeliveryCountProperty, out var raw) || raw is null)
        {
            return 0;
        }

        long value = raw switch
        {
            int i => i,
            long l => l,
            short s => s,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };

        return (int)Math.Clamp(value, 0, 10_000);
    }

    /// <summary>The logical delivery of <paramref name="message"/>: prior deliveries (copies) + the broker's count for it.</summary>
    internal static int LogicalDeliveryCount(ServiceBusReceivedMessage message) =>
        ReadPriorDeliveryCount(message) + message.DeliveryCount;

    /// <summary>
    /// True for the failure that means this receiver no longer holds the message (the broker redelivers it when the lock lapses), so
    /// neither a settlement nor a retry copy may follow.
    /// </summary>
    internal static bool IsLockLost(Exception exception) =>
        exception is ServiceBusException { Reason: ServiceBusFailureReason.MessageLockLost };

    /// <summary>
    /// Runs one settlement call (complete or dead-letter) and logs a failure instead of throwing it: the processor never turns a failed
    /// settlement into a retry copy. An unsettled message is redelivered by the broker when its lock lapses, and the same rule
    /// settles it then. Returns false when the call failed.
    /// </summary>
    internal static async Task<bool> TrySettleAsync(Func<Task> settle, string settlement, ServiceBusReceivedMessage message, ILogger logger)
    {
        try
        {
            await settle();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not {Settlement} message {MessageId} (delivery count: {DeliveryCount}); the broker redelivers it when its lock lapses",
                settlement, message.MessageId, LogicalDeliveryCount(message));
            return false;
        }
    }

    /// <summary>
    /// Retries <paramref name="args"/>' message after <see cref="JobContract.RetryDelay"/> for <paramref name="deliveryCount"/>, in
    /// this order: (1) schedule a COPY of the message on <paramref name="retrySender"/> (the processor's own queue) for then — the SAME body,
    /// MessageId, CorrelationId, SessionId, PartitionKey, Subject, ContentType, To, ReplyTo, time-to-live and application properties,
    /// via the SDK's received-message copy — with <see cref="JobContract.PriorDeliveryCountProperty"/> = <paramref name="deliveryCount"/>,
    /// keeping its sequence number; (2) complete the original.
    /// </summary>
    /// <remarks>
    /// <para><b>The schedule fails</b> → abandon the original (immediate redelivery, the behaviour before ISS-025). The job loses its
    /// back-off, never a delivery. If the abandon also fails it is logged, not thrown, so the caller never retries a second time; the
    /// broker redelivers the original when its lock lapses.</para>
    /// <para><b>The complete fails after the schedule</b> → <see cref="ResolveFailedCompleteAsync"/> finds out whether the original
    /// is still on the queue, and leans towards a duplicate run over a lost job.</para>
    /// <para>Never throws.</para>
    /// </remarks>
    internal static async Task RetryLaterAsync(
        ProcessMessageEventArgs args, RetrySender retrySender, int deliveryCount, TimeProvider time, ILogger logger)
    {
        var message = args.Message;
        var delay = JobContract.RetryDelay(deliveryCount);
        var dueAt = time.GetUtcNow() + delay;

        long sequenceNumber;
        try
        {
            var copy = new ServiceBusMessage(message);
            copy.ApplicationProperties[JobContract.PriorDeliveryCountProperty] = deliveryCount;
            sequenceNumber = await retrySender.Get().ScheduleMessageAsync(copy, dueAt, args.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not schedule the retry of message {MessageId} (delivery count: {DeliveryCount}); abandoning it for immediate redelivery instead",
                message.MessageId, deliveryCount);
            try
            {
                await args.AbandonMessageAsync(message, null, args.CancellationToken);
            }
            catch (Exception abandonEx)
            {
                logger.LogWarning(abandonEx,
                    "Could not abandon message {MessageId} either; the broker redelivers it when its lock lapses",
                    message.MessageId);
            }

            return;
        }

        try
        {
            await args.CompleteMessageAsync(message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            await ResolveFailedCompleteAsync(message, retrySender, sequenceNumber, ex, logger);
            return;
        }

        logger.LogWarning(
            "Message {MessageId} retry scheduled for {DueAt:o} (in {RetryDelay}, sequence {SequenceNumber}) after delivery {DeliveryCount} of {MaxDeliveryCount}",
            message.MessageId, dueAt, delay, sequenceNumber, deliveryCount, JobContract.MaxDeliveryCount);
    }

    /// <summary>
    /// ISS-025 fix round 2: the retry copy <paramref name="copySequenceNumber"/> is scheduled but completing <paramref name="original"/>
    /// failed. A failed complete is ambiguous: the broker may have applied it and lost the answer. So the processor's own queue is
    /// PEEKED at the original's sequence number (a peek takes no lock and also returns a message another receiver holds):
    /// <list type="bullet">
    ///   <item><b>Present</b> (same sequence number) → the original was not settled and is redelivered when its lock lapses: cancel the
    ///   copy, so only the original carries the job on. A failed cancel is logged at Error: the copy and the original may both
    ///   run.</item>
    ///   <item><b>Absent</b> (nothing, or a later sequence number) → the complete was applied: keep the copy, which now carries the job
    ///   on alone.</item>
    ///   <item><b>The peek fails</b> → keep the copy. A duplicate run is covered by handler idempotency (ADR-004: the per-job processing
    ///   lock and the completion marker or conditional write, keyed on <see cref="JobContract.IdempotencyKey"/>, which the copy
    ///   carries unchanged); a lost job would be silent.</item>
    /// </list>
    /// Never throws.
    /// </summary>
    private static async Task ResolveFailedCompleteAsync(
        ServiceBusReceivedMessage original, RetrySender retrySender, long copySequenceNumber, Exception completeFailure, ILogger logger)
    {
        bool originalPresent;
        try
        {
            var peeked = await retrySender.PeekAsync(original.SequenceNumber);
            originalPresent = peeked is not null && peeked.SequenceNumber == original.SequenceNumber;
        }
        catch (Exception peekEx)
        {
            logger.LogWarning(peekEx,
                "Message {MessageId} could not be completed ({CompleteError}) and the queue could not be peeked to tell whether it was; "
                + "keeping retry copy {SequenceNumber}: the copy and the original may both run (handlers are idempotent per ADR-004)",
                original.MessageId, completeFailure.Message, copySequenceNumber);
            return;
        }

        if (!originalPresent)
        {
            logger.LogWarning(completeFailure,
                "Completing message {MessageId} reported a failure, but the message is no longer on the queue, so the complete was "
                + "applied; keeping retry copy {SequenceNumber}, which carries the job on",
                original.MessageId, copySequenceNumber);
            return;
        }

        logger.LogWarning(completeFailure,
            "Message {MessageId} could not be completed and is still on the queue; cancelling retry copy {SequenceNumber} so that only "
            + "the original, redelivered when its lock lapses, carries the job on",
            original.MessageId, copySequenceNumber);
        try
        {
            // Not the processor's token: a shutdown must not leave a copy running beside the redelivered original.
            await retrySender.Get().CancelScheduledMessageAsync(copySequenceNumber, CancellationToken.None);
        }
        catch (Exception cancelEx)
        {
            logger.LogError(cancelEx,
                "Could not cancel retry copy {SequenceNumber} of message {MessageId}; the copy and the redelivered original may both run "
                + "(handlers are idempotent per ADR-004: per-job processing lock and completion marker on the idempotency key)",
                copySequenceNumber, original.MessageId);
        }
    }

    /// <summary>
    /// The sender of one processor's retry copies, to that processor's OWN queue: created on the first retry (lock-guarded) and
    /// disposed after the processor has stopped.
    /// </summary>
    internal sealed class RetrySender
    {
        private readonly ServiceBusClient _client;
        private readonly object _gate = new();
        private ServiceBusSender? _sender;

        public RetrySender(ServiceBusClient client, string queueName)
        {
            _client = client;
            QueueName = queueName;
        }

        /// <summary>The queue the copies go to: the processor's own.</summary>
        public string QueueName { get; }

        public ServiceBusSender Get()
        {
            lock (_gate)
            {
                return _sender ??= _client.CreateSender(QueueName);
            }
        }

        /// <summary>
        /// Peeks the processor's OWN queue at <paramref name="fromSequenceNumber"/> with a short-lived receiver from the same client: the
        /// first message at or after it, or null. A peek takes no lock and returns locked messages too. The receiver is disposed.
        /// </summary>
        public async Task<ServiceBusReceivedMessage?> PeekAsync(long fromSequenceNumber)
        {
            var receiver = _client.CreateReceiver(QueueName);
            try
            {
                return await receiver.PeekMessageAsync(fromSequenceNumber, CancellationToken.None);
            }
            finally
            {
                try
                {
                    await receiver.DisposeAsync();
                }
                catch (Exception)
                {
                    // A failed close of the peek link does not change what the peek answered.
                }
            }
        }

        public async Task DisposeAsync(ILogger logger)
        {
            ServiceBusSender? sender;
            lock (_gate)
            {
                sender = _sender;
                _sender = null;
            }

            if (sender is null)
            {
                return;
            }

            try
            {
                await sender.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error disposing the retry sender for queue {QueueName} during shutdown", QueueName);
            }
        }
    }
}
