using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095b: a Dataverse double for <see cref="SubmissionOutcomeNotifier"/> over the REAL <see cref="NotificationService"/>. It answers
/// the notifier's one read of the submission and records every <c>appnotification</c> create, so a test sees exactly what the bell would hold.
/// Nothing else is faked: the composition under test (who, which text, which link) and the notification writer are the production code.
/// </summary>
internal sealed class SubmissionBellWorld
{
    internal static readonly Guid Person = Guid.Parse("09500000-0000-4000-8000-0000000000c1");

    public Guid SubmissionId { get; }

    /// <summary>The submission row as the notifier reads it.</summary>
    public Entity Submission { get; }

    /// <summary>Every appnotification the world was asked to create, in order.</summary>
    public List<Entity> Created { get; } = [];

    /// <summary>When set, creating the appnotification throws it (Dataverse refuses, the user is disabled, ...).</summary>
    public Exception? CreateFault { get; set; }

    /// <summary>When set, reading the submission throws it.</summary>
    public Exception? ReadFault { get; set; }

    public CapturingLog<SubmissionOutcomeNotifier> Log { get; } = new();

    public SubmissionBellWorld(Guid submissionId, bool withPerson = true, string? reasonCode = null)
    {
        SubmissionId = submissionId;
        Submission = new Entity(FinanceBaseValues.SubmissionEntity, submissionId)
        {
            ["sprk_trackingnumber"] = "INV-100.2",
            ["sprk_firminvoicenumber"] = "INV-100",
        };
        if (withPerson)
        {
            Submission["sprk_createdbyperson"] = new EntityReference("systemuser", Person);
        }

        if (reasonCode is not null)
        {
            Submission["sprk_outcomereasoncode"] = reasonCode;
        }
    }

    public SubmissionOutcomeNotifier Build(TimeSpan? timeout = null)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync(FinanceBaseValues.SubmissionEntity, SubmissionId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid _, string[] _, CancellationToken _) =>
                ReadFault is { } fault ? Task.FromException<Entity>(fault) : Task.FromResult(Copy(Submission)));
        entities.Setup(e => e.CreateAsync(It.Is<Entity>(x => x.LogicalName == "appnotification"), It.IsAny<CancellationToken>()))
            .Returns((Entity entity, CancellationToken _) =>
            {
                if (CreateFault is { } fault)
                {
                    return Task.FromException<Guid>(fault);
                }

                Created.Add(entity);
                return Task.FromResult(Guid.NewGuid());
            });

        var notifications = new NotificationService(entities.Object, NullLogger<NotificationService>.Instance);
        return new SubmissionOutcomeNotifier(entities.Object, notifications, Log) { Timeout = timeout ?? SubmissionOutcomeNotifier.DefaultTimeout };
    }

    /// <summary>The <c>actionUrl</c> the notification carries, from its <c>data</c> JSON.</summary>
    public static string? ActionUrl(Entity notification) =>
        System.Text.Json.JsonDocument.Parse(notification.GetAttributeValue<string>("data")!).RootElement.TryGetProperty("actionUrl", out var url)
            ? url.GetString()
            : null;

    private static Entity Copy(Entity row)
    {
        var copy = new Entity(row.LogicalName, row.Id);
        foreach (var (k, v) in row.Attributes)
        {
            copy[k] = v;
        }

        return copy;
    }
}

/// <summary>A logger that keeps level, event id and rendered message.</summary>
internal sealed class CapturingLog<T> : ILogger<T>
{
    public List<(LogLevel Level, EventId Event, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, eventId, formatter(state, exception)));
        }
    }
}
