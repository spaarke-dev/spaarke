using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Api.Finance;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.RecordProcedureRig;
using static Sprk.Bff.Api.Tests.Api.Finance.RecordProcedureWriteRig;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// ebilling-r1 task 084h-3 (EB-D-239 Q10; design notes/084h section 7.3): the vertical slice from a per-record Procedure change whose inline
/// Decision Record fails, through 084h-2's enqueue, the Service Bus wire and the processor's routing rule, to the repair job's record. The
/// REAL <see cref="ProcedureAssignmentService"/> (built by its public constructor, as DI builds it), the REAL recorder and writer (on the
/// writer's Dataverse-doubled harness), the REAL enqueuer (only <see cref="JobSubmissionService"/>'s send is doubled) and the REAL handlers.
/// The handler's own outcomes are in <c>RecordProcedureDecisionJobTests</c>.
/// </summary>
/// <remarks>KEEP seam path (ADR-038): a change to the enqueue seam, the wire, or the job type breaks the slice here, not only a unit.</remarks>
public sealed class RecordProcedureRepairSeamTests : IClassFixture<CustomWebAppFactory>
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const string NoteText = "Agreed on the call with the client, 16 November";
    private const string ReasonText = "Client instruction";

    private readonly CustomWebAppFactory _factory;

    public RecordProcedureRepairSeamTests(CustomWebAppFactory factory) => _factory = factory;

    private sealed class Slice
    {
        public RecordProcedureWriteRig World { get; } = new();
        public DecisionRecordWriterTests.Harness Records { get; } = DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness());
        public Mock<JobSubmissionService> Queue { get; }
        public List<JobContract> Sent { get; } = new();
        public List<Entity> InlineAttempts { get; } = new();
        public ProcedureAssignmentService Service { get; }
        public IReadOnlyList<IJobHandler> Handlers { get; }

        public Slice()
        {
            var identity = new Mock<IIdentityNormalizationService>();
            identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid user, CancellationToken _) => new PersonIdentity(user, ContactId: Guid.NewGuid()));
            var recorder = new BillingDecisionRecorder(Records.Writer, identity.Object, NullLogger<BillingDecisionRecorder>.Instance);

            Queue = new Mock<JobSubmissionService>(
                MockBehavior.Loose,
                Options.Create(new Sprk.Bff.Api.Configuration.ServiceBusOptions { QueueName = "test" }),
                NullLogger<JobSubmissionService>.Instance,
                new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);
            Queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Callback<JobContract, CancellationToken>((job, _) => Sent.Add(job))
                .Returns(Task.CompletedTask);
            var enqueuer = new RecordProcedureDecisionEnqueuer(Queue.Object, NullLogger<RecordProcedureDecisionEnqueuer>.Instance);

            var counter = new AffectedInvoiceCounter(World.Rig.Entities.Object, World.Rig.Probe, NullLogger<AffectedInvoiceCounter>.Instance);
            Service = new ProcedureAssignmentService(
                World.Rig.Entities.Object, World.Ownership.Object, World.Rig.RuleSets, World.WriterClosure.Object, counter,
                NullLogger<ProcedureAssignmentService>.Instance, recorder, enqueuer)
            { Clock = World.Clock };

            var idempotency = new IdempotencyService(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<IdempotencyService>.Instance);
            // The main queue's handlers that write Decision Records, as the processor resolves them.
            Handlers = new IJobHandler[]
            {
                new GateDecisionRecordJobHandler(Records.Writer, Records.Entities.Object, idempotency, NullLogger<GateDecisionRecordJobHandler>.Instance),
                new RecordProcedureDecisionJobHandler(recorder, idempotency, NullLogger<RecordProcedureDecisionJobHandler>.Instance),
            };
        }

        /// <summary>The inline create fails; <paramref name="lands"/>: the row is written but the answer is lost (a timeout).</summary>
        public void InlineCreateFails(bool lands) =>
            Records.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .Returns<Entity, CancellationToken>((entity, _) =>
                {
                    InlineAttempts.Add(entity);
                    if (lands)
                    {
                        Records.Created.Add(entity);
                    }

                    return Task.FromException<Guid>(new TimeoutException("dataverse"));
                });

        public void CreatesSucceed() =>
            Records.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .Callback<Entity, CancellationToken>((e, _) => Records.Created.Add(e))
                .ReturnsAsync(Records.NewId);

        /// <summary>What <c>ServiceBusJobProcessor</c> does with a message: read the contract back (its options), then run the ONE handler
        /// whose job type equals the contract's.</summary>
        public Task<JobOutcome> DeliverAsync(JobContract sent)
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var job = JsonSerializer.Deserialize<JobContract>(JsonSerializer.Serialize(sent, options), options)!;
            job.DeliveryCount = 1;
            var handler = Handlers.Where(h => h.JobType == job.JobType).Should().ContainSingle("the processor runs the first handler of the job's type").Subject;
            handler.Should().BeOfType<RecordProcedureDecisionJobHandler>();
            return handler.ProcessAsync(job, Ct);
        }

        public int CreateCalls => Records.Org.Invocations.Count(i => i.Method.Name == "CreateAsync");
    }

    [Theory(DisplayName = "084h-3 J1 slice: a change whose inline record fails answers Queued, its enqueue reaches the repair handler's job type, and the repair files the record the inline write would have (same steps, hash, reason, time)")]
    [InlineData(Matter)]
    [InlineData(Project)]
    [InlineData(WorkAssignment)]
    public async Task AFailedInlineRecord_IsQueuedToTheRepairJob_WhichFilesTheInlineRecord(string table)
    {
        var slice = new Slice();
        var s = slice.World.Rig.Simple("REPAIR", ProcedureAppliesTo.AssignedRecordsOnly);
        var record = table switch { Matter => M, Project => P, _ => W };
        slice.InlineCreateFails(lands: false);

        var outcome = await slice.Service.AssignProcedureAsync(
            table, record, new(s.Procedure, "FollowLatest", null, ReasonText, NoteText), CallerObjectId, Decider, Ct);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, "the change committed; a failed record never turns it into an error");
        outcome.Value!.DecisionRecord!.Status.Should().Be(OverrideDecisionRecord.Queued, "the enqueuer is registered, so the failure is queued, not NotRecorded");
        var inline = slice.InlineAttempts.Should().ContainSingle().Subject;
        slice.Records.Created.Should().BeEmpty();
        var sent = slice.Sent.Should().ContainSingle("one repair job per failed record").Subject;

        slice.CreatesSucceed();
        var repaired = await slice.DeliverAsync(sent);

        repaired.Status.Should().Be(JobStatus.Completed);
        var row = slice.Records.Created.Should().ContainSingle().Subject;
        row.GetAttributeValue<string>("sprk_steps").Should().Be(inline.GetAttributeValue<string>("sprk_steps"),
            "the same key, values, outcome and hash: the repair is the inline request");
        row.GetAttributeValue<string>("sprk_reason").Should().Be(inline.GetAttributeValue<string>("sprk_reason")).And.Be(ReasonText);
        row.GetAttributeValue<DateTime>("sprk_decidedon").Should().Be(inline.GetAttributeValue<DateTime>("sprk_decidedon"));
        row.GetAttributeValue<string>("sprk_actioncode").Should().Be("assign-procedure");
        row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Decider);
        JsonDocument.Parse(row.GetAttributeValue<string>("sprk_steps")).RootElement.GetProperty("steps")[0].GetProperty("values")
            .GetProperty("note").GetString().Should().Be(NoteText, "the note travels in the payload (design 7.3)");
    }

    [Fact(DisplayName = "084h-3 J2 slice: when the inline create in fact landed, the queued repair answers AlreadyRecorded with zero creates")]
    public async Task AnInlineCreateThatLanded_IsAlreadyRecordedByTheRepair_WithZeroCreates()
    {
        var slice = new Slice();
        var s = slice.World.Rig.Simple("LANDED", ProcedureAppliesTo.AssignedRecordsOnly);
        slice.InlineCreateFails(lands: true);

        var outcome = await slice.Service.AssignProcedureAsync(
            Project, P, new(s.Procedure, "FollowLatest", null, ReasonText, NoteText), CallerObjectId, Decider, Ct);

        outcome.Value!.DecisionRecord!.Status.Should().Be(OverrideDecisionRecord.Queued);
        slice.CreatesSucceed();
        var createsBefore = slice.CreateCalls;

        var repaired = await slice.DeliverAsync(slice.Sent.Single());

        repaired.Status.Should().Be(JobStatus.Completed);
        slice.CreateCalls.Should().Be(createsBefore, "the writer found the landed record by its key");
        slice.Records.Created.Should().ContainSingle();
    }

    /// <summary>
    /// The real host composition (<see cref="CustomWebAppFactory"/>, as <c>AssignTaskNotifierRegistrationTests</c> does for its optional
    /// constructor). <see cref="ProcedureAssignmentService"/> takes the enqueuer as an OPTIONAL argument (084h-2): if it ever stops resolving,
    /// DI silently builds the service without it and every failed record answers NotRecorded forever. And the processor runs the one handler
    /// whose job type equals the message's: no handler, or two, and the repair never runs.
    /// </summary>
    [Fact(DisplayName = "084h-3 registration: the real container gives the Procedure service this enqueuer, and routes its job type to exactly the repair handler")]
    public void TheRealContainer_WiresTheEnqueuerIntoTheProcedureService_AndRoutesItsJobTypeToTheRepairHandler()
    {
        using var validating = _factory.WithWebHostBuilder(builder => builder.UseDefaultServiceProvider(options => options.ValidateScopes = true));
        using var scope = validating.Services.CreateScope();
        var enqueuer = scope.ServiceProvider.GetRequiredService<IRecordProcedureDecisionEnqueuer>();

        var service = scope.ServiceProvider.GetRequiredService<ProcedureAssignmentService>();

        EnqueueTargetOf(service).Should().BeSameAs(enqueuer, "DI must pass the registered enqueuer, not leave the optional argument null");
        var jobType = RecordProcedureDecisionEnqueuer.JobFor(new RecordProcedureDecision(
            Guid.NewGuid(), "assign-procedure", "sprk_matter", Guid.NewGuid(), Guid.NewGuid(), "procedure-editor",
            new Dictionary<string, object?>(), "Assigned", null, DateTimeOffset.UtcNow)).JobType;
        scope.ServiceProvider.GetServices<IJobHandler>().Where(h => h.JobType == jobType)
            .Should().ContainSingle().Which.Should().BeOfType<RecordProcedureDecisionJobHandler>();
    }

    private static object? EnqueueTargetOf(ProcedureAssignmentService service) =>
        ((Delegate)typeof(ProcedureAssignmentService)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(Func<RecordProcedureDecision, CancellationToken, Task>))
            .GetValue(service)!).Target;
}
