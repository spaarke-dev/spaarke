using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication; // CapturingLogger<T> / LogEntry (internal, same assembly)
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using SettableClock = Sprk.Bff.Api.Tests.Api.Finance.SettableClock;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// ebilling-r1 task 084h-3 (EB-D-239 Q10; design notes/084h section 7.3, tests J1-J5; ontology-r1 K1): the repair job of a per-record
/// Procedure change whose inline Decision Record failed after the change committed. The REAL writer (on its Dataverse-doubled harness), the
/// REAL <see cref="BillingDecisionRecorder"/>, the REAL idempotency service (over an in-memory cache) and the REAL enqueuer are used; each job
/// reaches the handler as Service Bus carries it (the JobContract serialised and read back with the processor's options). The vertical slice
/// from the Procedure service's enqueue to this handler is <c>RecordProcedureRepairSeamTests</c>.
/// </summary>
/// <remarks>KEEP data-mutation path (ADR-038): the job writes a Decision Record; the tests assert the row it writes, or that it writes none.</remarks>
public sealed class RecordProcedureDecisionJobTests
{
    private static readonly Guid Matter = Guid.Parse("084c3000-0000-4000-8000-0000000000a0");
    private static readonly Guid Decider = Guid.Parse("084c3000-0000-4000-8000-0000000000d1");
    private static readonly Guid DeciderContact = Guid.Parse("084c3000-0000-4000-8000-0000000000d2");
    private static readonly Guid Override = Guid.Parse("084c3000-0000-4000-8000-0000000000f1");
    private static readonly Guid Procedure = Guid.Parse("084c3000-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherProcedure = Guid.Parse("084c3000-0000-4000-8000-0000000000b9");
    private static readonly Guid Version = Guid.Parse("084c3000-0000-4000-8000-0000000000b2");
    private static readonly Guid Rule = Guid.Parse("084c3000-0000-4000-8000-0000000000c1");
    private static readonly Guid Firm = Guid.Parse("084c3000-0000-4000-8000-0000000000c2");

    /// <summary>Sub-second ticks, so a decided-on that lost precision on the wire would show.</summary>
    private static readonly DateTimeOffset DecidedOn = new DateTimeOffset(2026, 11, 16, 9, 30, 15, TimeSpan.Zero).AddTicks(1234567);

    /// <summary>Sentinels for J5: neither may appear in any log line or job outcome.</summary>
    private const string ReasonText = "Client agreed SENTINEL-REASON-084h3 at the kickoff";

    private const string NoteText = "SENTINEL-NOTE-084h3: résumé\nsecond line\twith a tab";

    public static readonly string[] Codes =
        ["assign-procedure", "pin-procedure", "unpin-procedure", "exclude-procedure", "exclude-rule", "change-rule", "remove-override"];

    public static IEnumerable<object[]> CodesWithAndWithoutNote() =>
        Codes.SelectMany(code => new[] { new object[] { code, true }, new object[] { code, false } });

    private static readonly AsyncLocal<Guid> MetricScope = new();

    private sealed class Rig
    {
        public DecisionRecordWriterTests.Harness Writer { get; }
        public Mock<IIdentityNormalizationService> Identity { get; } = new();
        public IIdempotencyService Idempotency { get; }
        public BillingDecisionRecorder Recorder { get; }
        public CapturingLogger<RecordProcedureDecisionJobHandler> Log { get; } = new();
        public RecordProcedureDecisionJobHandler Handler { get; }

        /// <summary>The eBilling flag the handler reads on each job (on unless a test turns it off).</summary>
        public EbillingOptions Ebilling { get; } = new() { Enabled = true };

        /// <summary>The idempotency service's clock (its owner takeover age).</summary>
        public SettableClock Clock { get; } = new(DecidedOn.AddMinutes(5));

        public Rig(IRecordOwnershipResolver? realResolver = null)
        {
            Writer = DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness(realResolver: realResolver));
            Identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid user, CancellationToken _) => new PersonIdentity(user, ContactId: DeciderContact));
            Idempotency = new IdempotencyService(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<IdempotencyService>.Instance, Clock);
            Recorder = new BillingDecisionRecorder(Writer.Writer, Identity.Object, NullLogger<BillingDecisionRecorder>.Instance);
            var flag = new Mock<IOptionsMonitor<EbillingOptions>>();
            flag.Setup(f => f.CurrentValue).Returns(() => Ebilling);
            Handler = new RecordProcedureDecisionJobHandler(Recorder, Idempotency, flag.Object, Log);
        }

        public Task<JobOutcome> RunAsync(RecordProcedureDecision decision, int delivery = 1, CancellationToken ct = default) =>
            Handler.ProcessAsync(Delivered(RecordProcedureDecisionEnqueuer.JobFor(decision), delivery), ct);

        /// <summary>The creates the writer sent to Dataverse (landed or not).</summary>
        public int CreateCalls => Writer.Org.Invocations.Count(i => i.Method.Name == "CreateAsync");
    }

    /// <summary>The Values the Procedure service builds for each code (design section 4.1), with the note only when one is given.</summary>
    private static Dictionary<string, object?> ValuesOf(string code, bool withNote)
    {
        var values = code switch
        {
            "assign-procedure" or "exclude-procedure" => new Dictionary<string, object?> { ["procedure"] = Procedure, ["override"] = Override },
            "pin-procedure" or "unpin-procedure" => new() { ["procedure"] = Procedure, ["version"] = Version, ["override"] = Override },
            "exclude-rule" => new() { ["rule"] = Rule, ["firm"] = Firm, ["override"] = Override },
            "change-rule" => new()
            {
                ["rule"] = Rule,
                ["firm"] = Firm,
                ["override"] = Override,
                ["basedOnVersion"] = Guid.Parse("084c3000-0000-4000-8000-0000000000c3"),
                ["recordRuleVersion"] = Guid.Parse("084c3000-0000-4000-8000-0000000000c4"),
            },
            "remove-override" => new() { ["procedure"] = Procedure, ["override"] = Override, ["kind"] = "Procedure" },
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };
        if (withNote)
        {
            values["note"] = NoteText;
        }

        return values;
    }

    private static RecordProcedureDecision Decision(
        string code = "exclude-rule", bool withNote = true, Guid? decisionId = null, string coreTable = "sprk_matter", Guid? recordId = null,
        string? reason = "use code default") =>
        new(
            decisionId ?? Guid.NewGuid(),
            code,
            coreTable,
            recordId ?? Matter,
            Decider,
            "procedure-editor",
            ValuesOf(code, withNote),
            code switch
            {
                "unpin-procedure" => "Unpinned",
                "remove-override" => "Removed",
                "change-rule" => "Changed",
                "pin-procedure" => "Pinned",
                "assign-procedure" => "Assigned",
                _ => "Excluded",
            },
            reason == "use code default" ? (code is "unpin-procedure" or "remove-override" ? null : ReasonText) : reason,
            DecidedOn,
            new[] { new DecisionRecordRef("sprk_procedureassignment", Override) });

    /// <summary>The job as the handler receives it: serialised by <c>JobSubmissionService</c> and read back by <c>ServiceBusJobProcessor</c>
    /// (camelCase options, each side's own), with the broker's delivery count.</summary>
    internal static JobContract Delivered(JobContract job, int delivery = 1)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var wire = JsonSerializer.Serialize(job, options);
        var read = JsonSerializer.Deserialize<JobContract>(wire, options)!;
        read.DeliveryCount = delivery;
        return read;
    }

    private static string StoredHash(Entity row) =>
        JsonDocument.Parse(row.GetAttributeValue<string>("sprk_steps")).RootElement.GetProperty("reviewHash").GetString()!;

    private static string StoredValues(Entity row) =>
        JsonDocument.Parse(row.GetAttributeValue<string>("sprk_steps")).RootElement.GetProperty("steps")[0].GetProperty("values").GetRawText();

    // ── J1: the repaired request is the inline request; its hash is the inline hash ──────────────────────────────────────

    [Theory]
    [MemberData(nameof(CodesWithAndWithoutNote))]
    public async Task J1_TheRepairedRecord_HashesAsTheInlineRequest_AndCarriesItsContent_ForEachCodeWithAndWithoutANote(string code, bool withNote)
    {
        var decision = Decision(code, withNote);
        var inline = BillingDecisionRecorder.ToRequest(decision);
        var rig = new Rig();

        var outcome = await rig.RunAsync(decision);

        outcome.Status.Should().Be(JobStatus.Completed);
        var row = rig.Writer.Row;
        StoredHash(row).Should().Be(DecisionRecordWriter.RecordProcedureDecisionHash(inline),
            "the payload rebuilds the inline request: Values that crossed the wire as JSON hash as the inline Guids and strings did");
        row.GetAttributeValue<string>("sprk_actioncode").Should().Be(code);
        row.GetAttributeValue<string>("sprk_reason").Should().Be(decision.Reason);
        row.GetAttributeValue<DateTime>("sprk_decidedon").Should().Be(DecidedOn.UtcDateTime, "the record states when the change was made");
        row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Decider);
        row.GetAttributeValue<EntityReference>("sprk_assignedtointernal").Id.Should().Be(DeciderContact, "looked up from the decider (K3)");
        row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(Matter);

        // The inline write of the same decision on a fresh world stores the same values and the same hash.
        var inlineRig = new Rig();
        await inlineRig.Recorder.RecordProcedureAsync(decision);
        StoredValues(row).Should().Be(StoredValues(inlineRig.Writer.Row));
        StoredHash(row).Should().Be(StoredHash(inlineRig.Writer.Row));
    }

    // ── J2: the inline create landed after all ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task J2_WhenTheInlineCreateLandedButTimedOut_TheJobAnswersAlreadyRecorded_WithZeroCreates()
    {
        var rig = new Rig();
        var decision = Decision();
        rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns<Entity, CancellationToken>((entity, _) =>
            {
                rig.Writer.Created.Add(entity); // the row lands, the answer is lost
                return Task.FromException<Guid>(new TimeoutException("response lost"));
            });
        (await rig.Recorder.Invoking(r => r.RecordProcedureAsync(decision)).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.CreateFailed, "the inline write saw a failure, so the service queued the repair");
        var createsBeforeTheJob = rig.CreateCalls;

        var outcome = await rig.RunAsync(decision);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.CreateCalls.Should().Be(createsBeforeTheJob, "the job found the landed row by its key and created nothing");
        rig.Writer.Created.Should().ContainSingle();
        rig.Log.Entries.Should().ContainSingle(e => e.EventId == RecordProcedureDecisionJobEvents.HandlerCompleted)
            .Which.Message.Should().Contain("already recorded");
    }

    // ── J3 (must-not-fire): no read of the override row; a later episode of the row id changes nothing ─────────────────────

    [Fact]
    public async Task J3_TheJobReadsNoOverrideRow_AndARowWithTheSameIdAndOtherContent_ChangesNothingRecorded()
    {
        var rig = new Rig();
        // A later episode: the row was removed and added again under the same derived id, with another Procedure and reason.
        rig.Writer.Rows[("sprk_procedureassignment", Override)] = new Entity("sprk_procedureassignment", Override)
        {
            ["sprk_procedure"] = new EntityReference("sprk_procedure", OtherProcedure),
            ["sprk_reason"] = "a later episode's reason",
            ["sprk_matter"] = new EntityReference("sprk_matter", Matter),
        };
        var decision = Decision("assign-procedure");

        var outcome = await rig.RunAsync(decision);

        outcome.Status.Should().Be(JobStatus.Completed);
        OverrideRowReads(rig.Writer).Should().BeEmpty("the payload carries everything; row ids recur across episodes (design 7.3)");
        StoredHash(rig.Writer.Row).Should().Be(DecisionRecordWriter.RecordProcedureDecisionHash(BillingDecisionRecorder.ToRequest(decision)));
        var values = JsonDocument.Parse(StoredValues(rig.Writer.Row)).RootElement;
        values.GetProperty("procedure").GetGuid().Should().Be(Procedure, "the decision's Procedure, never the later row's");
        rig.Writer.Row.GetAttributeValue<string>("sprk_reason").Should().Be(ReasonText);
    }

    [Fact]
    public async Task J3_Control_TheReadDetector_FiresOnAnOverrideRowRead()
    {
        // Must-fire control for the detector the J3 test relies on: a read of the override table by either reader is seen.
        var rig = new Rig();

        await rig.Writer.Entities.Object.RetrieveAsync("sprk_procedureassignment", Override, ["sprk_procedure"], CancellationToken.None);
        await rig.Writer.Entities.Object.RetrieveMultipleAsync(new QueryExpression("sprk_matter")
        {
            LinkEntities = { new LinkEntity("sprk_matter", "sprk_procedureassignment", "sprk_matterid", "sprk_matter", JoinOperator.Inner) },
        }, CancellationToken.None);

        OverrideRowReads(rig.Writer).Should().HaveCount(2);
    }

    /// <summary>Every reader call (the shared reader and the writer's own connection) that names the override table, as a table or in a
    /// query (its entity or any link).</summary>
    private static List<string> OverrideRowReads(DecisionRecordWriterTests.Harness h)
    {
        const string table = "sprk_procedureassignment";
        static bool Names(object? argument) => argument switch
        {
            string text => string.Equals(text, table, StringComparison.OrdinalIgnoreCase),
            QueryExpression query => string.Equals(query.EntityName, table, StringComparison.OrdinalIgnoreCase) || query.LinkEntities.Any(Links),
            _ => false,
        };
        static bool Links(LinkEntity link) =>
            string.Equals(link.LinkToEntityName, table, StringComparison.OrdinalIgnoreCase) || link.LinkEntities.Any(Links);

        return h.Entities.Invocations.Concat(h.Org.Invocations)
            .Where(i => !i.Method.Name.StartsWith("Create", StringComparison.Ordinal) && i.Arguments.Any(Names))
            .Select(i => i.Method.Name)
            .ToList();
    }

    // ── J4: retryable refusals are retried; permanent refusals are logged, metered and completed ────────────────────────────

    public static TheoryData<string, string> RetryableFaults() => new()
    {
        { "lookup", DecisionRecordRefusalReason.RecordProcedureLookupFailed },
        { "writerIdentity", DecisionRecordRefusalReason.RecordProcedureLookupFailed },
        { "deciderContact", DecisionRecordRefusalReason.RecordProcedureLookupFailed },
        { "ownerRead", DecisionRecordRefusalReason.OwnerResolutionFailed },
        { "create", DecisionRecordRefusalReason.CreateFailed },
    };

    [Theory]
    [MemberData(nameof(RetryableFaults))]
    public async Task J4_ARetryableRefusal_IsAFailureForRedelivery_WritesNothing_AndTheRedeliveryWritesTheRecord(string fault, string reason)
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, failures) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig();
        Arm(rig, fault);
        var decision = Decision();

        var first = await rig.RunAsync(decision, delivery: 1);

        first.Status.Should().Be(JobStatus.Failed, "ServiceBusJobProcessor abandons a Failure, so the broker redelivers it");
        first.ErrorMessage.Should().Be(reason);
        rig.Writer.Created.Should().BeEmpty();
        rig.Log.Entries.Should().ContainSingle(e => e.EventId == RecordProcedureDecisionJobEvents.HandlerRetrying)
            .Which.Level.Should().Be(LogLevel.Warning);
        failures.Should().NotContain(RecordProcedureDecisionRepairReason.Abandoned);

        Disarm(rig, fault);
        var redelivered = await rig.RunAsync(decision, delivery: 2);

        redelivered.Status.Should().Be(JobStatus.Completed);
        rig.Writer.Created.Should().ContainSingle();
        StoredHash(rig.Writer.Row).Should().Be(DecisionRecordWriter.RecordProcedureDecisionHash(BillingDecisionRecorder.ToRequest(decision)));
    }

    [Fact]
    public async Task J4_ARetryableRefusalOnTheLastDelivery_IsStillAFailure_LoggedAtError_SoTheProcessorDeadLettersIt()
    {
        var rig = new Rig();
        rig.Writer.LookupFault = new TimeoutException("dataverse");

        var outcome = await rig.RunAsync(Decision(), delivery: JobContract.MaxDeliveryCount);

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.Log.Entries.Should().ContainSingle(e => e.EventId == RecordProcedureDecisionJobEvents.HandlerRetrying)
            .Which.Level.Should().Be(LogLevel.Error, "the next step is the dead-letter queue: the gap needs an operator");
    }

    public static TheoryData<string, string> PermanentRefusals() => new()
    {
        { "noReasonOnARuleExclude", DecisionRecordRefusalReason.ReviewInvalid },
        { "notACoreTable", DecisionRecordRefusalReason.SubjectUnsupported },
        { "notAProcedureCode", DecisionRecordRefusalReason.RecordClassUnderivable },
        { "keyHoldsOtherContent", DecisionRecordRefusalReason.ReviewInvalid },
    };

    [Theory]
    [MemberData(nameof(PermanentRefusals))]
    public async Task J4_APermanentRefusal_IsLoggedAtErrorMeteredAndPoisoned_NotRetried_AndWritesNothing(string shape, string reason)
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, failures) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig();
        var decision = shape switch
        {
            "noReasonOnARuleExclude" => Decision("exclude-rule", reason: null),
            "notACoreTable" => Decision(coreTable: "sprk_invoice"),
            "notAProcedureCode" => Decision() with { ActionCode = "approve-invoice" },
            _ => Decision(),
        };
        if (shape == "keyHoldsOtherContent")
        {
            // The key already holds a record of the same change with another note: the repair is not a repeat of it.
            await rig.Recorder.RecordProcedureAsync(decision with { Values = ValuesOf(decision.ActionCode, withNote: false) });
        }

        var createsBefore = rig.Writer.Created.Count;
        var outcome = await rig.RunAsync(decision);

        outcome.Status.Should().Be(JobStatus.Poisoned,
            "a retry meets the same refusal, and the payload is the only copy of the reason and note: dead-lettered at once, never completed (ontology-r1 F2)");
        outcome.ErrorMessage.Should().Be(reason);
        rig.Writer.Created.Should().HaveCount(createsBefore, "nothing was written");
        var entry = rig.Log.Entries.Should().ContainSingle(e => e.EventId == RecordProcedureDecisionJobEvents.HandlerAbandoned).Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Field("Reason").Should().Be(reason);
        entry.Field("DecisionId").Should().Be(decision.DecisionId);
        failures.Count(r => r == RecordProcedureDecisionRepairReason.Abandoned).Should().Be(1, "metered once per abandoned repair");
    }

    [Fact]
    public async Task J4_OwnerRefused_ADeletedCoreRecord_IsPermanent_LoggedMeteredAndPoisoned_WithTheRealResolver()
    {
        // ontology-r1 K1: the REAL ownership resolver over an in-memory directory that has no row for the matter (it was deleted after the
        // change): the resolver refuses (parent unresolved), the writer refuses OwnerRefused, and a retry would meet the same answer.
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, failures) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig(realResolver: Standard().Resolver());

        var outcome = await rig.RunAsync(Decision(recordId: Guid.NewGuid()));

        outcome.Status.Should().Be(JobStatus.Poisoned, "dead-lettered, so the message (the only copy of the reason and note) is kept");
        rig.Writer.Created.Should().BeEmpty();
        var entry = rig.Log.Entries.Should().ContainSingle(e => e.EventId == RecordProcedureDecisionJobEvents.HandlerAbandoned).Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Field("Reason").Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        entry.Field("OwnerRefusalCode").Should().Be(RecordOwnerRefusal.ParentUnresolved);
        failures.Count(r => r == RecordProcedureDecisionRepairReason.Abandoned).Should().Be(1);
    }

    [Theory]
    [InlineData(RecordOwnerRefusal.NoDefaultOwnerTeam)]
    [InlineData(RecordOwnerRefusal.SecureOwnerTeamUnresolved)]
    [InlineData(RecordOwnerRefusal.ActingUserUnresolved)]
    public async Task J4_OwnerRefused_ForAConfigurationOrIdentityCode_IsAFailureForRedelivery_NotAbandoned_AndTheRedeliveryWritesTheRecord(string code)
    {
        // Must-fire (ontology-r1 F1): the writer raises OwnerRefused for EVERY resolver refusal. A unit with no default Owner team (and the
        // like) is a configuration an administrator fixes; the message must be redelivered, never dropped or dead-lettered at once.
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, failures) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig();
        rig.Writer.Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordOwnerResolution.Refused(code, "configuration"));
        var decision = Decision();

        var first = await rig.RunAsync(decision, delivery: 1);

        first.Status.Should().Be(JobStatus.Failed, "an administrator can clear it: abandoned and redelivered");
        first.ErrorMessage.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        rig.Writer.Created.Should().BeEmpty();
        rig.Log.Entries.Should().NotContain(e => e.EventId == RecordProcedureDecisionJobEvents.HandlerAbandoned);
        rig.Log.Entries.Should().ContainSingle(e => e.EventId == RecordProcedureDecisionJobEvents.HandlerRetrying)
            .Which.Field("OwnerRefusalCode").Should().Be(code);
        failures.Should().NotContain(RecordProcedureDecisionRepairReason.Abandoned);

        Disarm(rig, "ownerRead"); // the administrator fixed it
        var redelivered = await rig.RunAsync(decision, delivery: 2);

        redelivered.Status.Should().Be(JobStatus.Completed);
        rig.Writer.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task J4_AnExceptionThatIsNotARefusal_IsAFailureForRedelivery_AndMetered()
    {
        // A Dataverse client timeout can surface as a TaskCanceledException the writer does not wrap (here from the writer's own log call after the create, which nothing wraps; a timeout in any Dataverse call the writer makes is that call's refusal, task 084h-5 review F2). The host's token is not cancelled.
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, failures) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig();
        DecisionRecordWriterTests.FaultTheWritersLogAfterTheCreate(rig.Writer, new TaskCanceledException("timeout"));

        var outcome = await rig.RunAsync(Decision());

        outcome.Status.Should().Be(JobStatus.Failed);
        outcome.ErrorMessage.Should().Be(nameof(TaskCanceledException));
        failures.Should().Contain(RecordProcedureDecisionRepairReason.Faulted);

        // 084h-3 review round 1: the line carries the stack trace (method names), so an unexpected fault can be located; never the exception.
        var entry = rig.Log.Entries.Should().ContainSingle(e => e.EventId == RecordProcedureDecisionJobEvents.HandlerRetrying).Subject;
        entry.Exception.Should().BeNull();
        entry.Field("ExceptionType").Should().Be(nameof(TaskCanceledException));
        Convert.ToString(entry.Field("StackTrace")).Should().Contain("RecordProcedureAsync", "the trace names the methods the fault passed through");
    }

    // ── K1 (ontology-r1, 2026-11-16): every writer refusal is classified ───────────────────────────────────────────────

    /// <summary>Every <see cref="DecisionRecordRefusalReason"/> constant, read by reflection, so a reason added later is in the set.</summary>
    private static IReadOnlyList<string> AllRefusalReasons() => typeof(DecisionRecordRefusalReason)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    private static IReadOnlyList<string> Unclassified(IEnumerable<string> reasons) =>
        reasons.Where(r => !RecordProcedureDecisionJobHandler.RefusalClasses.ContainsKey(r)).ToList();

    [Fact]
    public void K1_EveryWriterRefusalReason_IsClassifiedRetryableOrPermanent_AndTheTableNamesNoOtherReason()
    {
        var all = AllRefusalReasons();

        all.Should().HaveCountGreaterThan(20, "the reflection read the refusal reasons");
        Unclassified(all).Should().BeEmpty("a writer refusal this job does not classify must fail here (ontology-r1 K1)");
        RecordProcedureDecisionJobHandler.RefusalClasses.Keys.Should().BeSubsetOf(all, "the table classifies writer refusal reasons only");
    }

    [Fact]
    public void K1_Control_AReasonAddedToTheWriter_WouldBeReportedUnclassified_AndIsRetriedAtRunTime()
    {
        const string added = "decision_some_future_refusal";

        Unclassified(AllRefusalReasons().Append(added)).Should().Equal(added);
        RecordProcedureDecisionJobHandler.ClassOf(added).Should().Be(RecordProcedureRefusalClass.Retryable,
            "fail safe: an unlisted reason is retried and dead-lettered, never dropped");
    }

    [Theory]
    [InlineData(DecisionRecordRefusalReason.RecordProcedureLookupFailed, RecordProcedureRefusalClass.Retryable)]
    [InlineData(DecisionRecordRefusalReason.BillingRecordLookupFailed, RecordProcedureRefusalClass.Retryable)]
    [InlineData(DecisionRecordRefusalReason.OwnerResolutionFailed, RecordProcedureRefusalClass.Retryable)]
    [InlineData(DecisionRecordRefusalReason.CreateFailed, RecordProcedureRefusalClass.Retryable)]
    [InlineData(DecisionRecordRefusalReason.FilingFailed, RecordProcedureRefusalClass.Retryable)]
    [InlineData(DecisionRecordRefusalReason.DataverseAccessDenied, RecordProcedureRefusalClass.Retryable)]
    [InlineData(DecisionRecordRefusalReason.OwnerRefused, RecordProcedureRefusalClass.Retryable)] // with no code; with one, by OwnerRefusalClasses
    [InlineData(DecisionRecordRefusalReason.ReviewInvalid, RecordProcedureRefusalClass.Permanent)]
    [InlineData(DecisionRecordRefusalReason.SubjectUnsupported, RecordProcedureRefusalClass.Permanent)]
    [InlineData(DecisionRecordRefusalReason.SubjectUnfileable, RecordProcedureRefusalClass.Permanent)]
    [InlineData(DecisionRecordRefusalReason.RecordClassUnderivable, RecordProcedureRefusalClass.Permanent)]
    [InlineData(DecisionRecordRefusalReason.FactSnapshotMissing, RecordProcedureRefusalClass.Permanent)]
    public void K1_TheNamedRefusals_HaveTheirAgreedClass(string reason, RecordProcedureRefusalClass expected)
    {
        RecordProcedureDecisionJobHandler.RefusalClasses[reason].Should().Be(expected);
    }

    // ── 084h-3 review round 1 (ontology-r1 F1): an owner refusal is classified by its resolver code ─────────────────────────

    /// <summary>Every <see cref="RecordOwnerRefusal"/> constant, read by reflection, so a code added later is in the set.</summary>
    private static IReadOnlyList<string> AllOwnerRefusalCodes() => typeof(RecordOwnerRefusal)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    private static IReadOnlyList<string> UnclassifiedOwnerCodes(IEnumerable<string> codes) =>
        codes.Where(c => !RecordProcedureDecisionJobHandler.OwnerRefusalClasses.ContainsKey(c)).ToList();

    [Fact]
    public void F1_EveryOwnerRefusalCode_IsClassified_OnlyTheDeletedCoreCodeIsPermanent_AndTheTableNamesNoOtherCode()
    {
        var all = AllOwnerRefusalCodes();

        all.Should().HaveCountGreaterThan(10, "the reflection read the owner refusal codes");
        UnclassifiedOwnerCodes(all).Should().BeEmpty("a resolver code this job does not classify must fail here (ontology-r1 F1)");
        RecordProcedureDecisionJobHandler.OwnerRefusalClasses.Keys.Should().BeSubsetOf(all, "the table classifies resolver codes only");
        RecordProcedureDecisionJobHandler.OwnerRefusalClasses.Where(c => c.Value == RecordProcedureRefusalClass.Permanent).Select(c => c.Key)
            .Should().Equal(new[] { RecordOwnerRefusal.ParentUnresolved },
                "only a parent that does not exist (the core record was deleted, or has no owning business unit) is permanent");
    }

    [Fact]
    public void F1_Control_ACodeAddedToTheResolver_WouldBeReportedUnclassified_AndIsRetriedAtRunTime()
    {
        const string added = "record_owner_some_future_refusal";

        UnclassifiedOwnerCodes(AllOwnerRefusalCodes().Append(added)).Should().Equal(added);
        RecordProcedureDecisionJobHandler.ClassOf(DecisionRecordRefusalReason.OwnerRefused, added).Should().Be(RecordProcedureRefusalClass.Retryable,
            "fail safe: an unlisted code is retried and dead-lettered after the last delivery, never dropped");
        RecordProcedureDecisionJobHandler.ClassOf(DecisionRecordRefusalReason.OwnerRefused, null).Should().Be(RecordProcedureRefusalClass.Retryable);
    }

    [Theory]
    [InlineData(RecordOwnerRefusal.ParentUnresolved, RecordProcedureRefusalClass.Permanent)]
    [InlineData(RecordOwnerRefusal.NoDefaultOwnerTeam, RecordProcedureRefusalClass.Retryable)]
    [InlineData(RecordOwnerRefusal.SecureOwnerTeamUnresolved, RecordProcedureRefusalClass.Retryable)]
    [InlineData(RecordOwnerRefusal.SecureBusinessUnitAmbiguous, RecordProcedureRefusalClass.Retryable)]
    [InlineData(RecordOwnerRefusal.SecureParentNotIsolated, RecordProcedureRefusalClass.Retryable)]
    [InlineData(RecordOwnerRefusal.ActingUserUnresolved, RecordProcedureRefusalClass.Retryable)]
    [InlineData(RecordOwnerRefusal.ParentUndetermined, RecordProcedureRefusalClass.Retryable)]
    [InlineData(RecordOwnerRefusal.RecordMissing, RecordProcedureRefusalClass.Retryable)]
    public void F1_AnOwnerRefusal_HasTheClassOfItsCode(string code, RecordProcedureRefusalClass expected)
    {
        RecordProcedureDecisionJobHandler.ClassOf(DecisionRecordRefusalReason.OwnerRefused, code).Should().Be(expected);
    }

    // ── J5: no log line carries the reason or the note ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task J5_NoLogLineOrJobOutcome_FromTheEnqueuerOrTheHandler_CarriesTheReasonOrTheNoteText()
    {
        var outcomes = new List<JobOutcome>();
        var handlerLogs = new List<LogEntry>();

        // Written; already recorded.
        var rig = new Rig();
        var decision = Decision("change-rule");
        outcomes.Add(await rig.RunAsync(decision));
        outcomes.Add(await rig.RunAsync(decision));
        handlerLogs.AddRange(rig.Log.Entries);

        // Retried (a refusal, and an exception that is not one) and abandoned.
        var retried = new Rig();
        retried.Writer.LookupFault = new TimeoutException(ReasonText + NoteText); // even a fault that echoed the text is not logged
        outcomes.Add(await retried.RunAsync(Decision(), delivery: JobContract.MaxDeliveryCount));
        retried.Writer.LookupFault = null;
        DecisionRecordWriterTests.FaultTheWritersLogAfterTheCreate(retried.Writer, new TaskCanceledException(ReasonText + NoteText));
        outcomes.Add(await retried.RunAsync(Decision()));
        outcomes.Add(await retried.RunAsync(Decision(coreTable: "sprk_invoice")));
        handlerLogs.AddRange(retried.Log.Entries);

        // 084h-3 review round 1: a held lock, and eBilling off.
        var held = new Rig();
        var heldDecision = Decision();
        await held.Idempotency.TryAcquireProcessingLockAsync(
            RecordProcedureDecisionKey.Of("sprk_matter", Matter, heldDecision.DecisionId), "another-job", TimeSpan.FromMinutes(5));
        outcomes.Add(await held.RunAsync(heldDecision));
        held.Ebilling.Enabled = false;
        outcomes.Add(await held.RunAsync(Decision()));
        handlerLogs.AddRange(held.Log.Entries);

        // The enqueuer: queued, failed and timed out.
        var enqueuerLogs = new CapturingLogger<RecordProcedureDecisionEnqueuer>();
        var queue = Queue();
        var enqueuer = new RecordProcedureDecisionEnqueuer(queue.Object, enqueuerLogs);
        await enqueuer.EnqueueAsync(Decision(), CancellationToken.None);
        queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(ReasonText + NoteText));
        await enqueuer.Invoking(e => e.EnqueueAsync(Decision(), CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns<JobContract, CancellationToken>((_, ct) => Task.Delay(Timeout.Infinite, ct));
        await enqueuer.Invoking(e => e.EnqueueAsync(Decision(), CancellationToken.None)).Should().ThrowAsync<TimeoutException>();

        var entries = handlerLogs.Concat(enqueuerLogs.Entries).ToList();
        entries.Select(e => e.EventId.Id).Should().Contain(new[]
        {
            RecordProcedureDecisionJobEvents.HandlerCompleted.Id, RecordProcedureDecisionJobEvents.HandlerRetrying.Id,
            RecordProcedureDecisionJobEvents.HandlerAbandoned.Id, RecordProcedureDecisionJobEvents.Queued.Id,
            RecordProcedureDecisionJobEvents.EnqueueTimedOut.Id, RecordProcedureDecisionJobEvents.HandlerDisabled.Id,
        }, "every kind of line was produced, so the check below is not vacuous");
        entries.Should().Contain(e => e.Fields.ContainsKey("StackTrace") && e.Fields["StackTrace"] != null,
            "the unexpected-exception line, with its stack trace, is among the lines checked");
        foreach (var entry in entries)
        {
            var text = entry.Message + "\n" + string.Join("\n", entry.Fields.Values.Select(v => Convert.ToString(v)));
            text.Should().NotContain("SENTINEL", $"event {entry.EventId} must log ids, codes and type names only");
            entry.Exception.Should().BeNull("an exception's message could carry a column value");
        }

        outcomes.Should().OnlyContain(o => o.ErrorMessage == null || !o.ErrorMessage.Contains("SENTINEL"),
            "the processor logs a Failure's error message");
    }

    // ── The enqueuer: the job it sends ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheEnqueuer_SendsOneJobOfThisHandlersType_KeyedByTheWritersKey_CarryingTheDecisionUnchanged()
    {
        var queue = Queue();
        JobContract? sent = null;
        queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => sent = job)
            .Returns(Task.CompletedTask);
        var decision = Decision("change-rule");
        var rig = new Rig();

        await new RecordProcedureDecisionEnqueuer(queue.Object, NullLogger<RecordProcedureDecisionEnqueuer>.Instance).EnqueueAsync(decision, CancellationToken.None);

        sent.Should().NotBeNull();
        sent!.JobType.Should().Be(rig.Handler.JobType, "the processor routes the job to the handler whose JobType equals it");
        sent.IdempotencyKey.Should().Be(RecordProcedureDecisionKey.Of("sprk_matter", Matter, decision.DecisionId));
        sent.IdempotencyKey.Length.Should().BeLessThanOrEqualTo(128, "the Service Bus MessageId limit");
        sent.CorrelationId.Should().Be(decision.DecisionId.ToString("D"), "the id the Procedure service's failure log names");
        var carried = Delivered(sent).Payload!.Deserialize<RecordProcedureDecision>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        carried.Should().BeEquivalentTo(decision, o => o.Excluding(d => d.Values));
        DecisionRecordWriter.RecordProcedureDecisionHash(BillingDecisionRecorder.ToRequest(carried))
            .Should().Be(DecisionRecordWriter.RecordProcedureDecisionHash(BillingDecisionRecorder.ToRequest(decision)));
    }

    [Fact]
    public async Task TheEnqueuer_WhenTheQueueFaultsOrTimesOut_Throws_SoTheServiceAnswersNotRecorded_AndMetersIt()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, failures) = ListenReasons(scope);
        using var _ = listener;
        var queue = Queue();
        var enqueuer = new RecordProcedureDecisionEnqueuer(queue.Object, NullLogger<RecordProcedureDecisionEnqueuer>.Instance);

        queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("bus"));
        await enqueuer.Invoking(e => e.EnqueueAsync(Decision(), CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns<JobContract, CancellationToken>((_, ct) => Task.Delay(Timeout.Infinite, ct));
        await enqueuer.Invoking(e => e.EnqueueAsync(Decision(), CancellationToken.None)).Should().ThrowAsync<TimeoutException>();

        failures.Should().Equal(RecordProcedureDecisionRepairReason.EnqueueFailed, RecordProcedureDecisionRepairReason.EnqueueFailed);
    }

    // ── The handler's other outcomes ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("null")]
    [InlineData("notJson")]
    [InlineData("noDecisionId")]
    public async Task AnUnreadablePayload_IsPoisoned_Metered_AndNothingIsWritten(string flaw)
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, failures) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig();
        var job = RecordProcedureDecisionEnqueuer.JobFor(Decision());
        job.Payload = flaw switch
        {
            "null" => null,
            "notJson" => JsonDocument.Parse("[1,2,3]"),
            _ => JsonSerializer.SerializeToDocument(Decision() with { DecisionId = Guid.Empty }),
        };

        var outcome = await rig.Handler.ProcessAsync(Delivered(job), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned, "dead-lettered at once, so the message is kept for an operator");
        rig.Writer.Created.Should().BeEmpty();
        failures.Should().Equal(RecordProcedureDecisionRepairReason.PayloadInvalid);
    }

    [Fact]
    public async Task AJobWhileAnotherJobHoldsTheSameDecision_WritesNothing_AndIsAFailureForRedelivery_ThenAnswersAlreadyRecorded()
    {
        var rig = new Rig();
        var decision = Decision();
        var key = RecordProcedureDecisionKey.Of("sprk_matter", Matter, decision.DecisionId);
        (await rig.Idempotency.TryAcquireProcessingLockAsync(key, "another-job", TimeSpan.FromMinutes(5))).Should().BeTrue();

        var outcome = await rig.RunAsync(decision);

        outcome.Status.Should().Be(JobStatus.Failed, "never completed while another holds the lock: the other copy may still fail (review F1)");
        outcome.ErrorMessage.Should().Be(RecordProcedureDecisionJobHandler.ProcessingLockHeld);
        rig.Writer.Created.Should().BeEmpty();

        // The other copy writes the record and releases the lock; this copy's next delivery answers AlreadyRecorded with no create.
        await rig.Recorder.RecordProcedureAsync(decision);
        await rig.Idempotency.ReleaseProcessingLockAsync(key, "another-job");
        var redelivered = await rig.RunAsync(decision, delivery: 2);

        redelivered.Status.Should().Be(JobStatus.Completed);
        rig.Writer.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task F1_ALockLeftByAnEarlierAttemptOfTheSameJob_IsAFailure_NotASuccess_AndTheRecordIsNotLost()
    {
        // Must-fire (084h-3 review F1): an earlier delivery of THIS job took the lock and its release faulted (IdempotencyService swallows
        // the fault). The lock is younger than the owner takeover age, so it is refused. Completing here would drop the only copy of the
        // decision; the job must be redelivered, and once the lock is stale its own redelivery takes it over and writes the record.
        var rig = new Rig();
        var decision = Decision();
        var job = RecordProcedureDecisionEnqueuer.JobFor(decision);
        var key = RecordProcedureDecisionKey.Of("sprk_matter", Matter, decision.DecisionId);
        (await rig.Idempotency.TryAcquireProcessingLockAsync(key, job.JobId.ToString("N"), RecordProcedureDecisionJobHandler.LockDuration))
            .Should().BeTrue("the earlier attempt's lock, never released");

        var refused = await rig.Handler.ProcessAsync(Delivered(job, delivery: 2), CancellationToken.None);

        refused.Status.Should().Be(JobStatus.Failed, "a Success would complete the message with nothing written");
        rig.Writer.Created.Should().BeEmpty();
        rig.Log.Entries.Should().ContainSingle(e => e.EventId == RecordProcedureDecisionJobEvents.HandlerRetrying)
            .Which.Level.Should().Be(LogLevel.Warning);

        rig.Clock.SetUtcNow(rig.Clock.GetUtcNow() + IdempotencyService.OwnerTakeoverAge);
        var taken = await rig.Handler.ProcessAsync(Delivered(job, delivery: 3), CancellationToken.None);

        taken.Status.Should().Be(JobStatus.Completed, "the same job's redelivery takes its own stale lock over");
        rig.Writer.Created.Should().ContainSingle("the record is written: nothing was lost");
    }

    [Fact]
    public async Task F1_MustNotFire_TheNormalPath_TakesTheLockAndReleasesIt()
    {
        var rig = new Rig();
        var decision = Decision();
        var key = RecordProcedureDecisionKey.Of("sprk_matter", Matter, decision.DecisionId);
        var lockSeenDuringTheWrite = false;
        rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns<Entity, CancellationToken>(async (entity, _) =>
            {
                // While the job writes, a second copy cannot take the lock.
                lockSeenDuringTheWrite = !await rig.Idempotency.TryAcquireProcessingLockAsync(key, "another-job", TimeSpan.FromMinutes(1));
                rig.Writer.Created.Add(entity);
                return rig.Writer.NewId;
            });

        var outcome = await rig.RunAsync(decision);

        outcome.Status.Should().Be(JobStatus.Completed);
        lockSeenDuringTheWrite.Should().BeTrue("the job holds the decision's lock while it writes");
        rig.Writer.Created.Should().ContainSingle();
        (await rig.Idempotency.TryAcquireProcessingLockAsync(key, "another-job", TimeSpan.FromMinutes(1)))
            .Should().BeTrue("the job released its lock when it finished");
    }

    // ── 084h-3 review round 1 (F2, ADR-018): the eBilling flag ─────────────────────────────────────────────────────────

    [Fact]
    public async Task F2_WithEbillingDisabled_TheJobWritesNothing_AndIsPoisoned_SoTheMessageIsKept_LoggedAndMetered()
    {
        // Must-fire: off, the handler writes nothing AND does not complete the message (the payload is the only copy of the decision).
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, failures) = ListenReasons(scope);
        using var _ = listener;
        var rig = new Rig();
        rig.Ebilling.Enabled = false;

        var outcome = await rig.RunAsync(Decision());

        outcome.Status.Should().Be(JobStatus.Poisoned, "kept in the dead-letter queue for replay once eBilling is on; never Success while disabled");
        rig.Writer.Created.Should().BeEmpty();
        rig.CreateCalls.Should().Be(0);
        rig.Writer.Org.Invocations.Should().BeEmpty("nothing is read or written while eBilling is off");
        rig.Writer.Entities.Invocations.Should().BeEmpty();
        rig.Log.Entries.Should().ContainSingle(e => e.EventId == RecordProcedureDecisionJobEvents.HandlerDisabled)
            .Which.Level.Should().Be(LogLevel.Warning);
        failures.Should().Equal(RecordProcedureDecisionRepairReason.Disabled);
    }

    [Fact]
    public async Task F2_MustNotFire_WithEbillingEnabled_TheJobWritesTheRecord()
    {
        var rig = new Rig();
        rig.Ebilling.Enabled.Should().BeTrue();

        var outcome = await rig.RunAsync(Decision());

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Writer.Created.Should().ContainSingle();
        rig.Log.Entries.Should().NotContain(e => e.EventId == RecordProcedureDecisionJobEvents.HandlerDisabled);
    }

    [Fact]
    public async Task ACancelledHost_IsNotSwallowed_AndTheLockIsReleased()
    {
        var rig = new Rig();
        var decision = Decision();
        using var cts = new CancellationTokenSource();
        rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns<Entity, CancellationToken>((_, _) => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        await rig.Invoking(r => r.RunAsync(decision, ct: cts.Token)).Should().ThrowAsync<OperationCanceledException>();

        (await rig.Idempotency.TryAcquireProcessingLockAsync(
                RecordProcedureDecisionKey.Of("sprk_matter", Matter, decision.DecisionId), "another-owner", TimeSpan.FromMinutes(1)))
            .Should().BeTrue("the release does not honour the cancelled token");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Mock<JobSubmissionService> Queue() => new(
        MockBehavior.Loose,
        Options.Create(new Sprk.Bff.Api.Configuration.ServiceBusOptions { QueueName = "test" }),
        NullLogger<JobSubmissionService>.Instance,
        new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);

    private static void Arm(Rig rig, string fault)
    {
        switch (fault)
        {
            case "lookup": rig.Writer.LookupFault = new TimeoutException("dataverse"); break;
            case "writerIdentity": rig.Writer.WhoAmIFault = new TimeoutException("whoami"); break;
            case "deciderContact":
                rig.Identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("identity"));
                break;
            case "ownerRead":
                rig.Writer.Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new TimeoutException("owner"));
                break;
            case "create":
                rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("create"));
                break;
        }
    }

    private static void Disarm(Rig rig, string fault)
    {
        rig.Writer.LookupFault = null;
        rig.Writer.WhoAmIFault = null;
        rig.Identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, CancellationToken _) => new PersonIdentity(user, ContactId: DeciderContact));
        rig.Writer.Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordOwnerResolution.Owned(Guid.NewGuid()));
        rig.Writer.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => rig.Writer.Created.Add(e))
            .ReturnsAsync(rig.Writer.NewId);
    }

    private static (MeterListener Listener, List<string?> Reasons) ListenReasons(Guid scope)
    {
        var reasons = new List<string?>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == "ontology.writer.failures")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (MetricScope.Value != scope) return;
            foreach (var tag in tags)
                if (tag.Key == "reason") lock (reasons) reasons.Add(tag.Value as string);
        });
        listener.Start();
        return (listener, reasons);
    }
}
