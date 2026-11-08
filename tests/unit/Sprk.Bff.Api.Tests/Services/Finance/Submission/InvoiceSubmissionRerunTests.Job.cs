using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095d-2b-1: the queued re-run job (<see cref="InvoiceSubmissionRerunJobHandler"/>) over the re-run's own in-memory world,
/// the after-commit totals (016), the outcome map, and the partner view while the re-run is pending, after it commits and after it fails
/// (uac-r2 2026-11-05 conditions 1-2; 2026-11-06 U1(a), K2b).
/// </summary>
public sealed partial class InvoiceSubmissionRerunTests
{
    // ── the outcome map (every typed outcome) ──

    public static TheoryData<InvoiceSubmissionRerunStatus, bool, JobStatus, int> OutcomeMap() => new()
    {
        // status, final delivery, job status, totals recalculations
        { InvoiceSubmissionRerunStatus.Completed, false, JobStatus.Completed, 1 },
        { InvoiceSubmissionRerunStatus.AlreadyCompleted, false, JobStatus.Completed, 1 },
        { InvoiceSubmissionRerunStatus.Refused, false, JobStatus.Poisoned, 0 },
        { InvoiceSubmissionRerunStatus.Refused, true, JobStatus.Poisoned, 0 },
        { InvoiceSubmissionRerunStatus.Busy, false, JobStatus.Failed, 0 },
        { InvoiceSubmissionRerunStatus.Changed, false, JobStatus.Failed, 0 },
        { InvoiceSubmissionRerunStatus.Failed, false, JobStatus.Failed, 0 },
        { InvoiceSubmissionRerunStatus.Busy, true, JobStatus.Poisoned, 0 },
        { InvoiceSubmissionRerunStatus.Changed, true, JobStatus.Poisoned, 0 },
        { InvoiceSubmissionRerunStatus.Failed, true, JobStatus.Poisoned, 0 },
    };

    [Theory(DisplayName = "095d-2b-1: the job maps every typed re-run outcome - Completed/AlreadyCompleted done (totals recomputed), Refused poisoned without retry, Busy/Changed/Failed retried, dead-lettered on the final delivery")]
    [MemberData(nameof(OutcomeMap))]
    public async Task TheJob_MapsEveryOutcome(InvoiceSubmissionRerunStatus status, bool final, JobStatus expected, int totals)
    {
        var job = new JobHarness((_, _, _, _) => Task.FromResult(Result(status)));

        var outcome = await job.ProcessAsync(final);

        outcome.Status.Should().Be(expected);
        job.Totals.Invocations.Count(i => i.Method.Name == nameof(IInvoiceTotalsService.RecalculateAsync)).Should().Be(totals);
        job.Locks.Released.Should().Equal(job.Locks.Acquired, "the 049 lock is released whatever the outcome");
    }

    [Theory(DisplayName = "095d-2b-1: the job passes the delivery's final flag to the re-run (only the final delivery commits an unresolved-Procedures hold)")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheJob_PassesTheFinalDelivery(bool final)
    {
        bool? seen = null;
        var job = new JobHarness((_, _, f, _) => { seen = f; return Task.FromResult(Result(InvoiceSubmissionRerunStatus.Completed)); });

        await job.ProcessAsync(final);

        seen.Should().Be(final);
    }

    [Fact(DisplayName = "095d-2b-1 (seed: Refused mapped to retry): a refused re-run is Poisoned on its FIRST delivery, never retried, logged Permanent=true with its code and counted (K2b)")]
    public async Task ARefusedRerun_IsPoisonedAtOnce_LoggedAndCounted()
    {
        var counted = new List<string?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "finance.ebilling.rerun.refused")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "code")
                {
                    for (var i = 0; i < value; i++)
                    {
                        counted.Add(tag.Value as string);
                    }
                }
            }
        });
        listener.Start();
        var job = new JobHarness((_, _, _, _) => Task.FromResult(Result(InvoiceSubmissionRerunStatus.Refused, InvoiceSubmissionRerunCodes.LineNotOwned)));

        var outcome = await job.ProcessAsync(final: false);

        outcome.Status.Should().Be(JobStatus.Poisoned, "a permanent refusal is never retried");
        outcome.ErrorMessage.Should().Be(InvoiceSubmissionRerunCodes.LineNotOwned, "the dead-lettered message carries the code");
        counted.Should().Contain(InvoiceSubmissionRerunCodes.LineNotOwned, "K2b: a stuck invoice is visible without a log query");
        job.Log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("Permanent=True") && e.Message.Contains(InvoiceSubmissionRerunCodes.LineNotOwned));
    }

    [Fact(DisplayName = "095d-2b-1: an invalid payload is Poisoned and nothing runs; eBilling off is Poisoned and nothing runs; another delivery holding the 049 lock is a retry")]
    public async Task TheJob_Step0()
    {
        var ran = 0;
        Func<Guid, Guid, bool, CancellationToken, Task<InvoiceSubmissionRerunResult>> run = (_, _, _, _) => { ran++; return Task.FromResult(Result(InvoiceSubmissionRerunStatus.Completed)); };

        var bad = new JobHarness(run);
        var invalid = InvoiceSubmissionRerunJobHandler.BuildJob(SubmissionId, DecisionId, "c");
        invalid.Payload = JsonDocument.Parse("""{"submissionId":"not-a-guid"}""");
        (await bad.Handler().ProcessAsync(invalid, CancellationToken.None)).Status.Should().Be(JobStatus.Poisoned);

        var off = new JobHarness(run) { Enabled = false };
        (await off.ProcessAsync(final: false)).Status.Should().Be(JobStatus.Poisoned);

        var busy = new JobHarness(run);
        busy.Locks.BusyKeys.Add(InvoiceSubmissionProcessingJobHandler.LockKeyPrefix + SubmissionId.ToString("N"));
        (await busy.ProcessAsync(final: false)).Status.Should().Be(JobStatus.Failed);

        ran.Should().Be(0);
    }

    [Fact(DisplayName = "095d-2b-1: BuildJob carries ids only, a deterministic key per submission and the correlation id")]
    public void BuildJob_IsDeterministic()
    {
        var a = InvoiceSubmissionRerunJobHandler.BuildJob(SubmissionId, DecisionId, "corr");
        var b = InvoiceSubmissionRerunJobHandler.BuildJob(SubmissionId, DecisionId, "corr");

        (a.JobType, a.SubjectId, a.CorrelationId).Should().Be((InvoiceSubmissionRerunJobHandler.JobTypeName, SubmissionId.ToString(), "corr"));
        a.IdempotencyKey.Should().Be(b.IdempotencyKey).And.Be($"ebilling-assign-firm-rerun-{SubmissionId:N}");
        a.JobId.Should().NotBe(b.JobId);
        var payload = a.Payload!.RootElement;
        payload.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["submissionId", "decisionId"]);
        payload.GetProperty("decisionId").GetGuid().Should().Be(DecisionId);
    }

    // ── over the re-run's own world ──

    [Fact(DisplayName = "095d-2b-1 (obligation 3): a completed re-run is followed by the invoice totals through 016, after the commit")]
    public async Task ACompletedRerun_RecomputesTheTotals_AfterTheCommit()
    {
        var world = await World.SeedAsync();
        var job = new JobHarness(world);
        var appliedAtTotals = -1;
        job.Totals.Setup(t => t.RecalculateAsync(InvoiceId, It.IsAny<CancellationToken>()))
            .Callback(() => appliedAtTotals = world.Applied.Count)
            .ReturnsAsync(Calculation());

        var outcome = await job.ProcessAsync(final: false);

        outcome.Status.Should().Be(JobStatus.Completed);
        appliedAtTotals.Should().Be(1, "the totals are recomputed after the one commit");
        job.Totals.Verify(t => t.RecalculateAsync(InvoiceId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "095d-2b-1: a totals fault after the commit is a retry; the retry is AlreadyCompleted (no second commit) and recomputes the totals")]
    public async Task ATotalsFault_IsRetried_WithoutASecondCommit()
    {
        var world = await World.SeedAsync();
        var job = new JobHarness(world);
        var calls = 0;
        job.Totals.Setup(t => t.RecalculateAsync(InvoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++calls == 1 ? throw new TimeoutException("totals") : Calculation());

        (await job.ProcessAsync(final: false)).Status.Should().Be(JobStatus.Failed);
        (await job.ProcessAsync(final: false)).Status.Should().Be(JobStatus.Completed);

        world.Applied.Should().ContainSingle("one commit");
        calls.Should().Be(2);
    }

    [Fact(DisplayName = "095d-2b-1 (T17): a message delivered twice (a 503 then the repeat's enqueue) gives exactly one re-run")]
    public async Task TwoMessages_GiveExactlyOneRerun()
    {
        var world = await World.SeedAsync();
        var job = new JobHarness(world);

        (await job.ProcessAsync(final: false)).Status.Should().Be(JobStatus.Completed);
        (await job.ProcessAsync(final: false)).Status.Should().Be(JobStatus.Completed);

        world.Applied.Should().ContainSingle("the second message finds rulesRerun completed and writes nothing");
        world.Attempts.Should().Be(1);
    }

    // ── the partner withhold (uac-r2 conditions 1 and 2) ──

    [Fact(DisplayName = "095d-2b-1 (uac-r2 condition 1): after Set firm and before the re-run commits, the partner view of the submission is withheld; it lifts with the commit")]
    public async Task ThePartnerView_IsWithheld_WhileTheRerunIsPending_AndLiftsWithTheCommit()
    {
        var world = await World.SeedAsync();
        var pending = InvoiceSubmissionService.ToView(world.Row(SubmissionId));
        pending.FirmId.Should().Be(Firm, "the setup: Set firm wrote the firm");

        var before = await PartnerDecisionAsync(world, pending);

        before.IsVisible(SubmissionId).Should().BeFalse("partners only see a state decided with the firm");
        before.HiddenSummary().Should().Be($"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.FirmMissing)}=1");

        (await new JobHarness(world).ProcessAsync(final: false)).Status.Should().Be(JobStatus.Completed);
        var after = await PartnerDecisionAsync(world, InvoiceSubmissionService.ToView(world.Row(SubmissionId)));

        // Must-not-fire control: once committed, the withhold no longer applies (the invoice predicate decides, as for any submission).
        after.HiddenSummary().Should().NotContain(InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.FirmMissing));
        after.IsVisible(SubmissionId).Should().BeTrue(after.HiddenSummary());
    }

    [Theory(DisplayName = "095d-2b-1 fix round (uac-r2 C1, seed: the invoice-marker condition dropped from the submission withhold): a user edit of the submission's reason, firm or invoice link does not lift the withhold while the invoice's marker is pending")]
    [InlineData("reason")]
    [InlineData("firm")]
    [InlineData("unlinked")]
    [InlineData("repointed")]
    public async Task AUserEditOfTheSubmission_DoesNotLiftTheWithhold_WhileTheMarkerIsPending(string edit)
    {
        var world = await World.SeedAsync();
        var view = InvoiceSubmissionService.ToView(world.Row(SubmissionId));
        var edited = edit switch
        {
            "reason" => view with { OutcomeReasonCode = SubmissionReasonCodes.Blocked },
            "firm" => view with { OutcomeReasonCode = SubmissionReasonCodes.Blocked, FirmId = Guid.NewGuid() },
            "unlinked" => view with { OutcomeReasonCode = SubmissionReasonCodes.Blocked, InvoiceId = null },
            _ => view with { OutcomeReasonCode = SubmissionReasonCodes.Blocked, InvoiceId = Guid.NewGuid() },
        };
        InvoiceFirmHold.StateOf(world.Row(InvoiceId)).Should().Be(InvoiceFirmHold.InvoiceHoldState.RerunPending, "the setup: Set firm wrote the marker");

        var decision = await PartnerDecisionAsync(world, edited);

        decision.IsVisible(SubmissionId).Should().BeFalse("the invoice's field-secured marker decides, not a user-writable submission column");
        decision.HiddenSummary().Should().Be($"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.FirmMissing)}=1");
    }

    [Fact(DisplayName = "095d-2b-1 fix round (uac-r2 C1) must not fire: after the re-run commit clears the marker, the same edited submission shows")]
    public async Task AfterTheCommit_TheSubmissionShows_WhateverItsReason()
    {
        var world = await World.SeedAsync();
        (await new JobHarness(world).ProcessAsync(final: false)).Status.Should().Be(JobStatus.Completed);
        InvoiceFirmHold.StateOf(world.Row(InvoiceId)).Should().Be(InvoiceFirmHold.InvoiceHoldState.None, "the commit cleared the marker");

        var decision = await PartnerDecisionAsync(world, InvoiceSubmissionService.ToView(world.Row(SubmissionId)));

        decision.IsVisible(SubmissionId).Should().BeTrue(decision.HiddenSummary());
    }

    [Fact(DisplayName = "095d-2b-1 (uac-r2 condition 2): a re-run that dead-letters (its commit failing on every delivery) leaves the hold and the partner withhold in place")]
    public async Task ADeadLetteredRerun_KeepsTheHold_AndThePartnerWithhold()
    {
        var world = await World.SeedAsync();
        world.CommitFault = new InvalidOperationException("service unavailable");
        var job = new JobHarness(world);

        (await job.ProcessAsync(final: false)).Status.Should().Be(JobStatus.Failed);
        var last = await job.ProcessAsync(final: true);

        last.Status.Should().Be(JobStatus.Poisoned, "the final delivery dead-letters the message");
        world.Applied.Should().BeEmpty();
        InvoiceFirmHold.StateOf(world.Row(InvoiceId)).Should().Be(InvoiceFirmHold.InvoiceHoldState.RerunPending, "the invoice stays held");
        world.Findings()[InvoiceAssignFirmService.StoredDecisionMember]!["rulesRerun"]!.GetValue<string>().Should().Be(InvoiceAssignFirmService.RulesRerunPending);
        job.Totals.VerifyNoOtherCalls();

        var decision = await PartnerDecisionAsync(world, InvoiceSubmissionService.ToView(world.Row(SubmissionId)));
        decision.IsVisible(SubmissionId).Should().BeFalse("a failed re-run never releases the submission to partners");
    }

    [Fact(DisplayName = "095d-2b-1 (uac-r2 condition 2): a refused re-run keeps the hold and the partner withhold")]
    public async Task ARefusedRerun_KeepsThePartnerWithhold()
    {
        var world = await World.SeedAsync();
        world.SwapInForeignLine(world.LineIds[0]);

        (await new JobHarness(world).ProcessAsync(final: false)).Status.Should().Be(JobStatus.Poisoned);

        (await PartnerDecisionAsync(world, InvoiceSubmissionService.ToView(world.Row(SubmissionId)))).IsVisible(SubmissionId).Should().BeFalse();
    }

    // ── U1(a): the partner projection of a committed procedures_unresolved hold ──

    [Fact(DisplayName = "095d-2b-1 (uac-r2 U1 a): once the withhold lifts on a committed procedures_unresolved hold, the partner projection shows only the closed 'could not be checked' value - no Procedure name, rule id or internal detail")]
    public async Task ThePartnerProjection_OfAnUnresolvedHold_IsNeutral()
    {
        var world = await World.SeedAsync();
        world.Processor = Unresolved();
        (await new JobHarness(world).ProcessAsync(final: true)).Status.Should().Be(JobStatus.Completed);
        // Internal detail a finding may carry (a Procedure name, a rule id, a fault text): planted in the stored findings, which a partner never reads.
        world.EditFindings(root => root["pipelineFindings"]!.AsArray().Add(new JsonObject
        {
            ["code"] = SubmissionReasonCodes.ProceduresUnresolved,
            ["procedure"] = PlantedProcedureName,
            ["ruleId"] = PlantedRuleId,
            ["detail"] = PlantedDetail,
        }));

        var view = InvoiceSubmissionService.ToView(world.Row(SubmissionId));
        (await PartnerDecisionAsync(world, view)).IsVisible(SubmissionId).Should().BeTrue("the re-run committed: the withhold is lifted");
        var projection = PartnerProjection(view, world.Row(InvoiceId));

        Leaks(projection).Should().BeEmpty(projection);
        world.Row(InvoiceId).GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value
            .Should().Be(InvoiceReviewDetail.BillingTermsNotChecked, "the one partner-readable reason is the closed 'could not be checked' value");
    }

    [Fact(DisplayName = "095d-2b-1 (uac-r2 U1 a) must-fire control: the leak check fires when a Procedure name, a rule id or internal detail reaches the projection")]
    public void TheLeakCheck_FiresOnEachPlantedValue()
    {
        var clean = new InvoiceSubmissionView { SubmissionId = SubmissionId, OutcomeReasonCode = SubmissionReasonCodes.Blocked };
        var invoice = new Entity("sprk_invoice", InvoiceId);
        Leaks(PartnerProjection(clean, invoice)).Should().BeEmpty();

        foreach (var planted in new[] { PlantedProcedureName, PlantedRuleId, PlantedDetail })
        {
            var leaky = clean with { Warning = new SubmissionHoldWarning(SubmissionReasonCodes.ProceduresUnresolved, "held: " + planted) };
            Leaks(PartnerProjection(leaky, invoice)).Should().ContainSingle().Which.Should().Be(planted);
        }
    }

    private const string PlantedProcedureName = "Outside Counsel Guidelines 2026";
    private const string PlantedRuleId = "OCG-TIME-INCREMENT";
    private const string PlantedDetail = "two versions in force";

    private static IReadOnlyList<string> Leaks(string projection) =>
        new[] { PlantedProcedureName, PlantedRuleId, PlantedDetail }.Where(p => projection.Contains(p, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>What a partner receives for the submission (the SPA status route's body) and may read of the invoice (statuscode, the review
    /// detail), serialized as the route serializes it.</summary>
    private static string PartnerProjection(InvoiceSubmissionView view, Entity invoice) =>
        JsonSerializer.Serialize(new
        {
            submission = ExternalInvoiceSubmissionEndpoints.ExternalView(view),
            invoice = new
            {
                statuscode = invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value,
                reviewDetail = invoice.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column)?.Value,
            },
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>
    /// The partner decision over one submission (<see cref="ExternalInvoiceVisibility.DecideSubmissionsAsync"/>, the routes' and the assistant
    /// tools' one decision), with readers that find no Stored File, source or Restricted root: so only the firm rules can withhold it. The
    /// invoice's firm hold is read by the REAL app-only reader (<see cref="ExternalInvoiceVisibility.ReadFirmHeldInvoicesAsync"/>) over the
    /// world's rows, so the marker the re-run clears is the one the decision reads. Task 165e: the submission read returns the submission
    /// itself, linked to the invoice the view names, on the Spa channel (never a source), so the invoice's own submission is found.
    /// </summary>
    private static Task<InvoiceVisibilityResult> PartnerDecisionAsync(World world, InvoiceSubmissionView view)
    {
        var entities = new Mock<Spaarke.Dataverse.IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression query, CancellationToken _) => world.Query(query));
        return ExternalInvoiceVisibility.DecideSubmissionsAsync([view], new InvoiceVisibilityReaders(
            (set, _, _) => Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(
                set == "sprk_invoicesubmissions" && view.InvoiceId is { } linked
                    ? [new Dictionary<string, JsonElement>
                    {
                        ["sprk_invoicesubmissionid"] = JsonSerializer.SerializeToElement(view.SubmissionId),
                        ["_sprk_invoice_value"] = JsonSerializer.SerializeToElement(linked),
                        ["sprk_channel"] = JsonSerializer.SerializeToElement((int)InvoiceSubmissionChannel.Spa),
                    }]
                    : []),
            (_, _) => throw new InvalidOperationException("no source is decided here"),
            (_, _) => throw new InvalidOperationException("no root walk here"),
            (_, ids, _) => Task.FromResult<IReadOnlyDictionary<Guid, RootRecordFlags>>(ids.ToDictionary(id => id, _ => default(RootRecordFlags))),
            NullLogger.Instance,
            (ids, ct) => ExternalInvoiceVisibility.ReadFirmHeldInvoicesAsync(entities.Object, ids, ct)), CancellationToken.None);
    }

    // ── harness ──

    private static InvoiceSubmissionRerunResult Result(InvoiceSubmissionRerunStatus status, string code = "ebilling.rerun.test") => status switch
    {
        InvoiceSubmissionRerunStatus.Completed or InvoiceSubmissionRerunStatus.AlreadyCompleted =>
            new(status, code, false, SubmissionId, RerunState: InvoiceSubmissionRerun.StateCommitted),
        InvoiceSubmissionRerunStatus.Refused => new(status, code, true, SubmissionId),
        _ => new(status, code, false, SubmissionId),
    };

    private static InvoiceTotalsCalculation Calculation() => new(0m, 0m, 0m, 0m, 0m, new Dictionary<Guid, decimal>(), null);

    internal sealed class JobLocks : IIdempotencyService
    {
        public List<string> Acquired { get; } = [];

        public List<string> Released { get; } = [];

        public HashSet<string> BusyKeys { get; } = [];

        public Task<bool> TryAcquireProcessingLockAsync(string key, string ownerId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
        {
            if (BusyKeys.Contains(key))
            {
                return Task.FromResult(false);
            }

            Acquired.Add(key);
            return Task.FromResult(true);
        }

        public Task ReleaseProcessingLockAsync(string key, string ownerId, CancellationToken cancellationToken = default)
        {
            Released.Add(key);
            return Task.CompletedTask;
        }

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default) =>
            TryAcquireProcessingLockAsync(eventId, "ownerless", lockDuration, cancellationToken);

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default) =>
            ReleaseProcessingLockAsync(eventId, "ownerless", cancellationToken);

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> RenewProcessingLockAsync(string key, string ownerId, TimeSpan lockDuration, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    internal sealed class JobLogger : ILogger<InvoiceSubmissionRerunJobHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class JobHarness
    {
        private readonly Func<Guid, Guid, bool, CancellationToken, Task<InvoiceSubmissionRerunResult>> _run;

        public JobHarness(Func<Guid, Guid, bool, CancellationToken, Task<InvoiceSubmissionRerunResult>> run)
        {
            _run = run;
            Totals.Setup(t => t.RecalculateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Calculation());
        }

        /// <summary>The re-run AND its after-commit Signal steps (095d-2b-2) over <paramref name="world"/>. Each entry to the Signal steps first
        /// records <see cref="World.HoldSnapshot"/> (<see cref="RemapEntrySnapshots"/>), so a test can compare what the steps found with what they left.</summary>
        public JobHarness(World world)
            : this((s, d, f, ct) => world.Rerun().RunAsync(s, d, f, ct))
        {
            RemapSignals = (s, ct) =>
            {
                RemapEntrySnapshots.Add(world.HoldSnapshot());
                return world.Rerun().RemapSignalsAsync(s, ct);
            };
        }

        /// <summary>The world's hold state on each entry to the Signal steps (the world constructor only).</summary>
        public List<Dictionary<string, string>> RemapEntrySnapshots { get; } = [];

        /// <summary>095d-2b-2: the Signal steps the handler runs after the totals (default: done, nothing written).</summary>
        public Func<Guid, CancellationToken, Task<InvoiceSubmissionRerunSignalsResult>> RemapSignals { get; set; } = (_, _) =>
            Task.FromResult(new InvoiceSubmissionRerunSignalsResult(InvoiceSubmissionRerunSignalsStatus.Done, "test.done"));

        public Mock<IInvoiceTotalsService> Totals { get; } = new(MockBehavior.Strict);

        public JobLocks Locks { get; } = new();

        public JobLogger Log { get; } = new();

        public bool Enabled { get; init; } = true;

        public InvoiceSubmissionRerunJobHandler Handler() => new(
            _run, RemapSignals, Totals.Object, Locks, Mock.Of<IOptionsMonitor<EbillingOptions>>(m => m.CurrentValue == new EbillingOptions { Enabled = Enabled }),
            new FinanceTelemetry(), Log);

        public Task<JobOutcome> ProcessAsync(bool final)
        {
            var job = InvoiceSubmissionRerunJobHandler.BuildJob(SubmissionId, DecisionId, "corr-1");
            // As in production: Attempt stays 1 (nothing advances it) and only the broker's DeliveryCount grows (ServiceBusJobProcessor sets it).
            job.Attempt = 1;
            job.DeliveryCount = final ? JobContract.MaxDeliveryCount : 1;
            return Handler().ProcessAsync(job, CancellationToken.None);
        }
    }
}
