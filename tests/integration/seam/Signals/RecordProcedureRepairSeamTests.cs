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
using Sprk.Bff.Api.Configuration;
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
    private const string Token = "caller-token";

    private readonly CustomWebAppFactory _factory;

    public RecordProcedureRepairSeamTests(CustomWebAppFactory factory) => _factory = factory;

    private sealed class Slice
    {
        public RecordProcedureWriteRig World { get; } = new();
        public DecisionRecordWriterTests.Harness Records { get; } = DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness());
        public Mock<JobSubmissionService> Queue { get; }
        public List<JobContract> Sent { get; } = new();
        public List<Entity> InlineAttempts { get; } = new();

        /// <summary>Every decision the service queued, as it built it (before the payload's JSON round trip).</summary>
        public List<RecordProcedureDecision> Queued { get; } = new();

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
            var enqueuer = new SeeingEnqueuer(
                new RecordProcedureDecisionEnqueuer(Queue.Object, NullLogger<RecordProcedureDecisionEnqueuer>.Instance), Queued);

            var counter = new AffectedInvoiceCounter(World.Rig.Entities.Object, World.Rig.Probe, NullLogger<AffectedInvoiceCounter>.Instance);
            Service = new ProcedureAssignmentService(
                World.Rig.Entities.Object, World.Ownership.Object, World.Rig.RuleSets, World.WriterClosure.Object, counter,
                NullLogger<ProcedureAssignmentService>.Instance, recorder, enqueuer)
            { Clock = World.Clock };

            var idempotency = new IdempotencyService(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<IdempotencyService>.Instance);
            var ebilling = new Mock<IOptionsMonitor<EbillingOptions>>();
            ebilling.Setup(o => o.CurrentValue).Returns(new EbillingOptions { Enabled = true });
            // The main queue's handlers that write Decision Records, as the processor resolves them.
            Handlers = new IJobHandler[]
            {
                new GateDecisionRecordJobHandler(Records.Writer, Records.Entities.Object, idempotency, NullLogger<GateDecisionRecordJobHandler>.Instance),
                new RecordProcedureDecisionJobHandler(recorder, idempotency, ebilling.Object, NullLogger<RecordProcedureDecisionJobHandler>.Instance),
            };
        }

        /// <summary>The REAL enqueuer, with every decision it is given seen first (as the service built it).</summary>
        private sealed class SeeingEnqueuer(IRecordProcedureDecisionEnqueuer inner, List<RecordProcedureDecision> seen) : IRecordProcedureDecisionEnqueuer
        {
            public Task EnqueueAsync(RecordProcedureDecision decision, CancellationToken ct)
            {
                seen.Add(decision);
                return inner.EnqueueAsync(decision, ct);
            }
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

    // ── 084h-3 review round 1: every per-record act, through the REAL service ─────────────────────────────────────────

    public static TheoryData<string> SevenActs() => new()
    {
        "assign-procedure", "pin-procedure", "unpin-procedure", "exclude-procedure", "exclude-rule", "change-rule", "remove-override",
    };

    /// <summary>A rule change body (the dry-run parity tests' shape) with the note.</summary>
    private static RuleChangeRequest Change(Guid policy) =>
        new(policy, ReasonText, null, null, "The billed rate differs from the rate agreed for this record.", "Agreed rate", "Flag", NoteText);

    /// <summary>
    /// Runs <paramref name="act"/> on matter M through the REAL service with the inline create failing (a removal first writes the row it
    /// removes, its own record succeeding). Returns the change's answer about its record, the inline attempt, the decision the service queued
    /// (as it built it) and the job it sent.
    /// </summary>
    private static async Task<(OverrideDecisionRecord? Record, Entity Inline, RecordProcedureDecision Decision, JobContract Sent)> ActWithAFailingInlineRecordAsync(
        Slice slice, string act)
    {
        var rig = slice.World.Rig;
        Func<Task<OverrideDecisionRecord?>> change;
        switch (act)
        {
            case "unpin-procedure":
            case "remove-override":
                {
                    var s = rig.Simple(act == "unpin-procedure" ? "UNPIN" : "REMOVE", ProcedureAppliesTo.AssignedRecordsOnly);
                    slice.CreatesSucceed();
                    var assigned = await slice.Service.AssignProcedureAsync(Matter, M,
                        act == "unpin-procedure"
                            ? new(s.Procedure, "Pinned", s.Version, ReasonText)
                            : new(s.Procedure, "FollowLatest", null, ReasonText),
                        CallerObjectId, Decider, Ct);
                    assigned.Kind.Should().Be(RuleSetOutcomeKind.Done, assigned.Detail);
                    change = async () => (await Done(slice.Service.RemoveAsync(
                        Matter, M, assigned.Value!.Id, new OverrideRemovalRequest(NoteText), Decider, Token, Ct))).DecisionRecord;
                    break;
                }

            case "exclude-rule":
                {
                    var s = rig.Simple("EXCL-R");
                    change = async () => (await Done(slice.Service.ExcludeRuleAsync(
                        Matter, M, new RuleExcludeRequest(s.Rule, ReasonText, null, NoteText), CallerObjectId, Decider, Ct))).DecisionRecord;
                    break;
                }

            case "change-rule":
                {
                    var c = rig.Changeable("CHG");
                    change = async () => (await Done(slice.Service.ChangeRuleAsync(
                        Matter, M, Change(c.Rule), CallerObjectId, Decider, Token, Ct))).Override.DecisionRecord;
                    break;
                }

            default:
                {
                    var s = rig.Simple(act.ToUpperInvariant(), act == "assign-procedure" ? ProcedureAppliesTo.AssignedRecordsOnly : ProcedureAppliesTo.AllRecords);
                    var request = act switch
                    {
                        "pin-procedure" => new ProcedureAssignmentRequest(s.Procedure, "Pinned", s.Version, ReasonText, NoteText),
                        "exclude-procedure" => new ProcedureAssignmentRequest(s.Procedure, "Excluded", null, ReasonText, NoteText),
                        _ => new ProcedureAssignmentRequest(s.Procedure, "FollowLatest", null, ReasonText, NoteText),
                    };
                    change = async () => (await Done(slice.Service.AssignProcedureAsync(Matter, M, request, CallerObjectId, Decider, Ct))).DecisionRecord;
                    break;
                }
        }

        slice.InlineAttempts.Clear();
        slice.Sent.Clear();
        slice.Queued.Clear();
        slice.InlineCreateFails(lands: false);

        var record = await change();

        var inline = slice.InlineAttempts.Should().ContainSingle("one inline attempt of the change's record").Subject;
        var decision = slice.Queued.Should().ContainSingle("one repair per failed record").Subject;
        var sent = slice.Sent.Should().ContainSingle().Subject;
        return (record, inline, decision, sent);
    }

    private static async Task<T> Done<T>(Task<RuleSetOutcome<T>> outcome)
        where T : class
    {
        var answered = await outcome;
        answered.Kind.Should().Be(RuleSetOutcomeKind.Done, answered.Detail);
        return answered.Value!;
    }

    private static string StoredHash(Entity row) =>
        JsonDocument.Parse(row.GetAttributeValue<string>("sprk_steps")).RootElement.GetProperty("reviewHash").GetString()!;

    [Theory(DisplayName = "084h-3 review round 1: each of the seven per-record acts, through the REAL service with a failing inline create, is repaired by the job with the inline record's steps and hash")]
    [MemberData(nameof(SevenActs))]
    public async Task EachAct_AFailedInlineRecord_IsRepairedByTheJob_WithTheInlineStepsAndHash(string act)
    {
        var slice = new Slice();
        var (record, inline, decision, sent) = await ActWithAFailingInlineRecordAsync(slice, act);

        record!.Status.Should().Be(OverrideDecisionRecord.Queued);
        decision.ActionCode.Should().Be(act);
        var createdBefore = slice.Records.Created.Count;

        slice.CreatesSucceed();
        var repaired = await slice.DeliverAsync(sent);

        repaired.Status.Should().Be(JobStatus.Completed);
        slice.Records.Created.Should().HaveCount(createdBefore + 1, "the repair files exactly one record");
        var row = slice.Records.Created[^1];
        row.GetAttributeValue<string>("sprk_steps").Should().Be(inline.GetAttributeValue<string>("sprk_steps"),
            "the repair is the inline request: same key, values, outcome and hash after the payload's JSON round trip");
        StoredHash(row).Should().Be(StoredHash(inline))
            .And.Be(DecisionRecordWriter.RecordProcedureDecisionHash(BillingDecisionRecorder.ToRequest(decision)));
        row.GetAttributeValue<string>("sprk_actioncode").Should().Be(act);
        row.GetAttributeValue<string>("sprk_reason").Should().Be(inline.GetAttributeValue<string>("sprk_reason"));
        row.GetAttributeValue<DateTime>("sprk_decidedon").Should().Be(inline.GetAttributeValue<DateTime>("sprk_decidedon"));
        JsonDocument.Parse(row.GetAttributeValue<string>("sprk_steps")).RootElement.GetProperty("steps")[0].GetProperty("values")
            .GetProperty("note").GetString().Should().Be(NoteText);
    }

    /// <summary>A Values entry that survives the payload's JSON round trip unchanged. A Guid or a string does; a date does not (it comes
    /// back as text in another form, so the repaired request would hash differently and the writer would refuse it ReviewInvalid).</summary>
    private static bool SurvivesTheWire(object? value) => value is Guid or string;

    [Theory(DisplayName = "084h-3 review round 1: every Values entry each act builds is a Guid or a string, so the payload round trip cannot change the hash")]
    [MemberData(nameof(SevenActs))]
    public async Task EachAct_BuildsValuesOfGuidsAndStringsOnly(string act)
    {
        var slice = new Slice();
        var (_, _, decision, _) = await ActWithAFailingInlineRecordAsync(slice, act);

        decision.Values.Should().NotBeEmpty();
        decision.Values.Where(v => !SurvivesTheWire(v.Value)).Select(v => $"{v.Key}: {v.Value?.GetType().Name ?? "null"}")
            .Should().BeEmpty("a value of another type (a date, a number, null) must be added only with a test that its repaired hash equals the inline one");
    }

    [Fact(DisplayName = "084h-3 review round 1 control: the Values type check refuses a date, a number and null")]
    public void Control_TheValuesTypeCheck_RefusesADateANumberAndNull()
    {
        SurvivesTheWire(DateTimeOffset.UtcNow).Should().BeFalse();
        SurvivesTheWire(DateTime.UtcNow).Should().BeFalse();
        SurvivesTheWire(1).Should().BeFalse();
        SurvivesTheWire(null).Should().BeFalse();
        SurvivesTheWire(Guid.NewGuid()).Should().BeTrue();
        SurvivesTheWire("Procedure").Should().BeTrue();
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
