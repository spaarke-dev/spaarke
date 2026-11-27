using System.Collections.Concurrent;
using System.Data;
using System.ServiceModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Intake;
using Sprk.Bff.Api.Services.Intake.Harnesses;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Intake;

/// <summary>
/// The shared arrangement of the intake run tests (spaarke-intake-pipeline-r1 task 013): the REAL
/// <see cref="IntakeRunJobHandler"/>, <see cref="IntakeRunStore"/> and <see cref="IntakeHarnessRegistry"/> over an in-memory
/// Dataverse boundary (<see cref="IGenericEntityService"/>) that models row versions and conditional writes the way Dataverse
/// does (a version mismatch is <see cref="DBConcurrencyException"/>, a missing row is the ObjectDoesNotExist fault). Only
/// the boundaries are doubles: Dataverse, the AI facade, the content reader, the harness and the door (ADR-038).
/// </summary>
internal sealed class IntakeRunTestBed
{
    public const string SecretDocumentText = "CONFIDENTIAL-DRAFT-TEXT the parties agree to keep secret";
    public const string SecretReasoning = "CLASSIFIER-REASONING-mentions-the-counterparty";

    public static readonly Guid RequestId = Guid.Parse("01300000-0000-4000-8000-0000000000a1");
    public static readonly Guid DocumentId = Guid.Parse("01300000-0000-4000-8000-0000000000d1");
    public static readonly Guid OutputDocumentId = Guid.Parse("01300000-0000-4000-8000-0000000000d2");
    public const string CorrelationId = "0f3c2b1a-0000-4000-8000-000000000013";

    public InMemoryDataverse Dataverse { get; } = new();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero));
    public TestOptionsMonitor Options { get; } = new(new IntakePipelineOptions());
    public CapturingLoggerProvider Logs { get; } = new();
    public Mock<IAgreementAi> AgreementAi { get; } = new();
    public Mock<IntakeContentTextReader> TextReader { get; }
    public RecordingHarness Harness { get; set; } = new("nda");
    public RecordingDoor Door { get; } = new(IntakeChannels.FrontDoor);
    public List<JobContract> Enqueued { get; } = new();

    public int ClassifyCalls;
    public int TextReads;

    public IntakeRunTestBed(int requestType = 100000000, double confidence = 0.93, int? intakeStage = IntakeRequestStages.Submitted)
    {
        var request = new Entity(IntakeRunStore.RequestEntity, RequestId)
        {
            [IntakeRunStore.RequestType] = new OptionSetValue(requestType),
            [IntakeRunStore.RequestChannel] = new OptionSetValue(IntakeChannels.FrontDoor),
        };
        if (intakeStage is { } stage)
        {
            request[IntakeRunStore.RequestIntakeStage] = new OptionSetValue(stage);
        }

        Dataverse.Seed(request);
        Dataverse.Seed(new Entity(IntakeRunStore.DocumentEntity, DocumentId)
        {
            ["statecode"] = new OptionSetValue(0),
            [IntakeRunStore.DocumentRequest] = new EntityReference(IntakeRunStore.RequestEntity, RequestId),
        });

        AgreementAi
            .Setup(a => a.ClassifyAsync(It.IsAny<AgreementClassifyRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref ClassifyCalls))
            .ReturnsAsync(Classified(confidence));

        var entities = Dataverse.Object;
        TextReader = new Mock<IntakeContentTextReader>(
            entities,
            new RecordContainerResolver(new Mock<ISecurableEntityRegistry>().Object, entities, NullLogger<RecordContainerResolver>.Instance),
            new Mock<ISpeFileOperations>().Object,
            new Mock<ITextExtractor>().Object,
            NullLogger<IntakeContentTextReader>.Instance);
        TextReader
            .Setup(r => r.ReadTextAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref TextReads))
            .ReturnsAsync(SecretDocumentText);
    }

    public static AgreementClassifyResult Classified(double confidence = 0.93) => new()
    {
        IsAgreement = true,
        Candidates = [new AgreementCandidate("nda", confidence)],
        Composite = false,
        Reasoning = SecretReasoning,
    };

    public IntakeRunStore Store => new(Dataverse.Object, Time, Logs.CreateLogger<IntakeRunStore>());

    /// <summary>Creates the run as a door does, through the production create (deterministic id, lookups as references).</summary>
    public Task<Guid> CreateRunAsync(string passKey = "front-door-submit", params Guid[] content) =>
        Store.CreateRunAsync(RequestId, passKey, content.Length == 0 ? [DocumentId] : content, CorrelationId, initiatedBy: null, CancellationToken.None);

    public IntakeRunJobHandler CreateHandler()
    {
        var routing = new Mock<IConsumerRoutingService>();
        routing
            .Setup(r => r.ResolveBindingAsync(ConsumerTypes.IntakeHarness, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string? code, IRoutingContext? _, string? _, CancellationToken _) =>
                code == "nda" ? new Binding { BindingId = Guid.NewGuid(), ConsumerType = ConsumerTypes.IntakeHarness, ConsumerCode = "nda" } : null);
        var registry = new IntakeHarnessRegistry([Harness], routing.Object, NullLogger<IntakeHarnessRegistry>.Instance);

        return new IntakeRunJobHandler(
            Store,
            registry,
            AgreementAi.Object,
            TextReader.Object,
            [Door],
            Options,
            Time,
            Logs.CreateLogger<IntakeRunJobHandler>());
    }

    /// <summary>A delivery of the run's job. <paramref name="deliveryCount"/> 5 is the final delivery.</summary>
    public static JobContract Job(Guid runId, Guid? jobId = null, int deliveryCount = 1)
    {
        var job = IntakeRunJobHandler.CreateJob(RequestId, runId, CorrelationId);
        job.JobId = jobId ?? job.JobId;
        job.DeliveryCount = deliveryCount;
        return job;
    }

    public IntakeRunSnapshot Run(Guid runId) => Store.ReadRunAsync(runId, CancellationToken.None).GetAwaiter().GetResult()!;

    public int? RunStatus(Guid runId) => Dataverse.Get(IntakeRunStore.RunEntity, runId).GetAttributeValue<OptionSetValue>(IntakeRunStore.RunStatus)?.Value;

    public int? RequestStage() => Dataverse.Get(IntakeRunStore.RequestEntity, RequestId).GetAttributeValue<OptionSetValue>(IntakeRunStore.RequestIntakeStage)?.Value;

    /// <summary>What the approval endpoint (task 017) will do: record the approval and enqueue the run again.</summary>
    public void Approve(Guid runId)
    {
        var row = Dataverse.Get(IntakeRunStore.RunEntity, runId);
        var state = IntakeRunState.Parse(row.GetAttributeValue<string>(IntakeRunStore.RunState));
        state.ApprovedOn = Time.GetUtcNow();
        Dataverse.Seed(Clone(row, (IntakeRunStore.RunState, state.Serialize())));
    }

    private static Entity Clone(Entity row, params (string Column, object Value)[] set)
    {
        var copy = new Entity(row.LogicalName, row.Id);
        foreach (var attribute in row.Attributes)
        {
            copy[attribute.Key] = attribute.Value;
        }

        foreach (var (column, value) in set)
        {
            copy[column] = value;
        }

        return copy;
    }
}

/// <summary>A harness double that records every call (NDA's step shape: segment, edit-plan, redline).</summary>
internal sealed class RecordingHarness(string key, bool externalEngineSteps = false) : IIntakeHarness
{
    public ConcurrentDictionary<string, int> StepCalls { get; } = new();
    public int PersistCalls;
    public HashSet<Guid> CreatedDocuments { get; } = new();

    public string Key { get; } = key;

    public bool CanHandle(IntakeHarnessRoute route) => true;

    public IReadOnlyList<IntakeHarnessStep> Steps { get; } =
    [
        new("segment", IntakeHarnessStage.Analyse, External: externalEngineSteps),
        new("edit-plan", IntakeHarnessStage.Analyse),
        new("redline", IntakeHarnessStage.Build, External: externalEngineSteps),
    ];

    public Task<IntakeHarnessStepOutcome> RunStepAsync(
        IntakeHarnessStep step,
        IntakeHarnessRequest request,
        IReadOnlyDictionary<string, IntakeHarnessResultReference> completedSteps,
        CancellationToken cancellationToken)
    {
        StepCalls.AddOrUpdate(step.Name, 1, (_, n) => n + 1);
        return Task.FromResult(step.External
            ? IntakeHarnessStepOutcome.Dispatched($"exec-{step.Name}")
            : IntakeHarnessStepOutcome.Completed(Reference(step.Name, request.RunId)));
    }

    public static IntakeHarnessResultReference Reference(string step, Guid runId) =>
        new() { Kind = step, Reference = $"intake-staging/{runId:N}/out/{step}" };

    public Task<IntakeHarnessPersistResult> PersistAsync(
        IntakeHarnessRequest request,
        IReadOnlyDictionary<string, IntakeHarnessResultReference> completedSteps,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref PersistCalls);
        lock (CreatedDocuments)
        {
            CreatedDocuments.Add(IntakeRunTestBed.OutputDocumentId); // deterministic id: a repeat creates nothing new
        }

        return Task.FromResult(new IntakeHarnessPersistResult
        {
            DocumentIdsByRole = new Dictionary<string, Guid> { ["redline"] = IntakeRunTestBed.OutputDocumentId },
            OutputDocumentId = IntakeRunTestBed.OutputDocumentId,
            ReturnPayload = new IntakeHarnessReturnPayload { DocumentRoles = ["redline"] },
        });
    }
}

/// <summary>A door double: counts returns and records the idempotency keys it was given.</summary>
internal sealed class RecordingDoor(int channel) : IIntakeDoorReturn
{
    public List<string> Keys { get; } = new();
    public int Channel { get; } = channel;

    public Task ReturnAsync(IntakeReturnContext context, CancellationToken cancellationToken)
    {
        lock (Keys)
        {
            Keys.Add(context.IdempotencyKey);
        }

        return Task.CompletedTask;
    }
}

internal sealed class TestOptionsMonitor(IntakePipelineOptions value) : IOptionsMonitor<IntakePipelineOptions>
{
    public IntakePipelineOptions CurrentValue { get; set; } = value;
    public IntakePipelineOptions Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<IntakePipelineOptions, string?> listener) => null;
}

/// <summary>One write the handler made, in order (run and request rows).</summary>
internal sealed record WriteRecord(string Entity, Guid Id, IReadOnlyDictionary<string, object?> Fields);

/// <summary>
/// An in-memory Dataverse: rows by (table, id) with a <c>versionnumber</c> bumped on every write; conditional writes compare
/// it atomically. <see cref="CrashAfter"/> throws once right after the first run write that makes a condition true (a crash
/// injected after a stage persisted its result). <see cref="RunReadGate"/> runs after a run row was read and before it is
/// returned, so a test can hold a delivery on a version that goes stale.
/// </summary>
internal sealed class InMemoryDataverse
{
    private readonly object _gate = new();
    private readonly Dictionary<(string, Guid), Entity> _rows = new();
    private long _version = 100;
    private Func<IntakeRunState, bool>? _crash;

    public List<WriteRecord> Writes { get; } = new();
    public Func<Task>? RunReadGate { get; set; }
    public Mock<IGenericEntityService> Mock { get; } = new();
    public IGenericEntityService Object => Mock.Object;

    public InMemoryDataverse()
    {
        Mock.Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns(async (string table, Guid id, string[] _, CancellationToken _) =>
            {
                Entity copy;
                lock (_gate)
                {
                    if (!_rows.TryGetValue((table, id), out var row))
                    {
                        throw new FaultException<OrganizationServiceFault>(
                            new OrganizationServiceFault { ErrorCode = -2147220969 }, "does not exist");
                    }

                    copy = Copy(row);
                    copy.RowVersion = Convert.ToString(row["versionnumber"]);
                }

                // The gate runs AFTER the row was read: the caller holds a version that may be stale by the time it returns.
                if (table == IntakeRunStore.RunEntity && RunReadGate is { } gate)
                {
                    await gate();
                }

                return copy;
            });

        Mock.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity entity, CancellationToken _) =>
            {
                lock (_gate)
                {
                    if (_rows.ContainsKey((entity.LogicalName, entity.Id)))
                    {
                        throw new FaultException<OrganizationServiceFault>(
                            new OrganizationServiceFault { ErrorCode = -2147220937 }, "duplicate key");
                    }

                    Writes.Add(new WriteRecord(entity.LogicalName, entity.Id, entity.Attributes.ToDictionary(a => a.Key, a => (object?)a.Value)));
                    var row = Copy(entity);
                    row["versionnumber"] = ++_version;
                    _rows[(entity.LogicalName, entity.Id)] = row;
                    return entity.Id;
                }
            });

        Mock.Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
            {
                Apply(table, id, expectedVersion: null, fields);
                return Task.CompletedTask;
            });

        Mock.Setup(s => s.UpdateIfRowVersionMatchesAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string version, Dictionary<string, object> fields, CancellationToken _) =>
            {
                Apply(table, id, version, fields);
                return Task.CompletedTask;
            });
    }

    /// <summary>Arms a crash: the first run write that turns <paramref name="condition"/> true is applied, then throws.</summary>
    public void CrashAfter(Func<IntakeRunState, bool> condition) => _crash = condition;

    public void Seed(Entity row)
    {
        lock (_gate)
        {
            var copy = Copy(row);
            copy["versionnumber"] = ++_version;
            _rows[(row.LogicalName, row.Id)] = copy;
        }
    }

    public Entity Get(string table, Guid id)
    {
        lock (_gate)
        {
            return Copy(_rows[(table, id)]);
        }
    }

    public IReadOnlyList<WriteRecord> RunWrites(Guid runId) => Writes.Where(w => w.Entity == IntakeRunStore.RunEntity && w.Id == runId).ToList();

    private void Apply(string table, Guid id, string? expectedVersion, Dictionary<string, object> fields)
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue((table, id), out var row))
            {
                throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220969 }, "does not exist");
            }

            if (expectedVersion is not null && expectedVersion != Convert.ToString(row["versionnumber"]))
            {
                throw new DBConcurrencyException("changed since it was read");
            }

            var before = table == IntakeRunStore.RunEntity ? IntakeRunState.Parse(row.GetAttributeValue<string>(IntakeRunStore.RunState)) : null;
            Writes.Add(new WriteRecord(table, id, fields.ToDictionary(f => f.Key, f => f.Value is DBNull ? null : (object?)f.Value)));
            foreach (var (column, value) in fields)
            {
                if (value is DBNull)
                {
                    row.Attributes.Remove(column);
                }
                else
                {
                    row[column] = value;
                }
            }

            row["versionnumber"] = ++_version;

            if (before is not null && _crash is { } crash)
            {
                var after = IntakeRunState.Parse(row.GetAttributeValue<string>(IntakeRunStore.RunState));
                if (!crash(before) && crash(after))
                {
                    _crash = null;
                    throw new InjectedCrashException();
                }
            }
        }
    }

    private static Entity Copy(Entity row)
    {
        var copy = new Entity(row.LogicalName, row.Id);
        foreach (var attribute in row.Attributes)
        {
            copy[attribute.Key] = attribute.Value;
        }

        return copy;
    }
}

internal sealed class InjectedCrashException() : Exception("injected crash after the stage persisted");

/// <summary>Captures every log line with the scopes active when it was written.</summary>
internal sealed class CapturingLoggerProvider
{
    private readonly AsyncLocal<ImmutableStack> _scopes = new();

    public ConcurrentQueue<(string Message, IReadOnlyList<object?> Scopes)> Entries { get; } = new();

    public ILogger<T> CreateLogger<T>() => new Logger<T>(this);

    /// <summary>Every log line and every scope value, as text, for content checks.</summary>
    public string AllText() => string.Join("\n", Entries.Select(e =>
        e.Message + " | " + string.Join(";", e.Scopes.Select(s => s is IEnumerable<KeyValuePair<string, object?>> kv
            ? string.Join(",", kv.Select(p => $"{p.Key}={p.Value}"))
            : s?.ToString()))));

    private sealed record ImmutableStack(object? Head, ImmutableStack? Tail);

    private sealed class Logger<T>(CapturingLoggerProvider owner) : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            var previous = owner._scopes.Value;
            owner._scopes.Value = new ImmutableStack(state, previous);
            return new Pop(() => owner._scopes.Value = previous!);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<object?>();
            for (var s = owner._scopes.Value; s is not null; s = s.Tail)
            {
                scopes.Add(s.Head);
            }

            owner.Entries.Enqueue((formatter(state, exception) + (exception is null ? string.Empty : " " + exception), scopes));
        }
    }

    private sealed class Pop(Action pop) : IDisposable
    {
        public void Dispose() => pop();
    }
}

internal static class IntakeAssertions
{
    /// <summary>True when the scope set carries the correlation id under <c>CorrelationId</c>.</summary>
    public static bool HasCorrelation(IReadOnlyList<object?> scopes, string correlationId) =>
        scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .Any(s => s.Any(p => p.Key == "CorrelationId" && Equals(p.Value?.ToString(), correlationId)));

    public static string Json(object? value) => JsonSerializer.Serialize(value);
}
