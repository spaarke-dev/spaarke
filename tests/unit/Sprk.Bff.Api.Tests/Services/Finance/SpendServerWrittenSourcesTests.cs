using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Finance.Tools;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 165b-4 (EB-D-208; ontology-r1 option (b), uac-r2 F1): every spend reader decides only from server-written sources. Counted
/// spend keeps an Approved / Sent to AP / Paid invoice only when the writer created its approve Decision Record under the approval's own key
/// (<see cref="InvoiceApproveRecord"/>); pending spend leaves out what <see cref="InvoiceBlockedHold"/> holds. Each case runs on every reader in
/// scope: counted through the evaluator's <see cref="FinanceBaseValues.ReadAsync"/>, <see cref="FinanceRollupService"/> and the finance tool;
/// pending through <see cref="FinanceBaseValues.ReadPendingSpendAsync"/> and the rollup. The cases are notes/165b-integrity-checks.md §7.10.
/// </summary>
/// <remarks>Maintain-class (ADR-038): these decide what a Threshold Signal, the matter form and the AI tool say about spend.</remarks>
public class SpendServerWrittenSourcesTests
{
    private const string Matter = FinanceBaseValues.MatterEntity;
    private static readonly Guid MatterId = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");

    public static TheoryData<string> CountedReaders => new() { "evaluator", "rollup", "tool" };
    public static TheoryData<string> PendingReaders => new() { "pending", "rollup" };

    // ── The readers ──

    private static BillingDecisionRecorder Recorder(Exception? whoAmIFault = null)
    {
        var harness = new DecisionRecordWriterTests.Harness { WhoAmIFault = whoAmIFault };
        return new BillingDecisionRecorder(harness.Writer, new Mock<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>().Object,
            NullLogger<BillingDecisionRecorder>.Instance);
    }

    private sealed class RollupRun
    {
        public Mock<IFieldMappingDataverseService> Writes { get; } = new();
        public List<Dictionary<string, object?>> Written { get; } = new();

        public RollupRun() =>
            Writes.Setup(w => w.UpdateExistingRecordFieldsAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
                .Callback((string _, Guid _, Dictionary<string, object?> fields, CancellationToken _) => Written.Add(fields))
                .Returns(Task.CompletedTask);

        public decimal? Counted => Written.FirstOrDefault(f => f.ContainsKey("sprk_totalspendtodate")) is { } f ? (decimal)f["sprk_totalspendtodate"]! : null;
        public decimal? Pending => Written.FirstOrDefault(f => f.ContainsKey("sprk_pendingspend")) is { } f ? (decimal)f["sprk_pendingspend"]! : null;
    }

    private static async Task<RollupRun> RunRollupAsync(SpendWorld world, Exception? whoAmIFault = null)
    {
        var run = new RollupRun();
        var service = new FinanceRollupService(world.Service().Object, run.Writes.Object, Recorder(whoAmIFault), NullLogger<FinanceRollupService>.Instance);
        await service.RecalculateMatterAsync(MatterId);
        return run;
    }

    private static async Task<PlaybookToolResult> RunToolAsync(SpendWorld world, Exception? whoAmIFault = null)
    {
        var tool = new FinancialCalculationToolHandler(world.Service().Object, Recorder(whoAmIFault), new Mock<FinanceTelemetry>().Object,
            NullLogger<FinancialCalculationToolHandler>.Instance);
        return await tool.ExecuteAsync(new ToolParameters(new Dictionary<string, object> { ["matterId"] = MatterId }), CancellationToken.None);
    }

    private static async Task<decimal> CountedAsync(string reader, SpendWorld world)
    {
        switch (reader)
        {
            case "evaluator":
                return (await FinanceBaseValues.ReadAsync(world.Service().Object, Matter, [MatterId], needSpend: true, needBudget: false, SpendWorld.Writer,
                    CancellationToken.None))[MatterId].Spend;
            case "rollup":
                return (await RunRollupAsync(world)).Counted ?? throw new InvalidOperationException("the rollup wrote no counted spend");
            case "tool":
                var result = await RunToolAsync(world);
                result.Success.Should().BeTrue(result.Error);
                using (var json = JsonDocument.Parse((string)result.Data!))
                {
                    return json.RootElement.GetProperty("totalSpend").GetDecimal();
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(reader));
        }
    }

    private static async Task<decimal> PendingAsync(string reader, SpendWorld world, ILogger? logger = null) => reader switch
    {
        "pending" => (await FinanceBaseValues.ReadPendingSpendAsync(world.Service().Object, Matter, [MatterId], CancellationToken.None, logger))[MatterId],
        "rollup" => (await RunRollupAsync(world)).Pending ?? throw new InvalidOperationException("the rollup wrote no pending spend"),
        _ => throw new ArgumentOutOfRangeException(nameof(reader)),
    };

    // ── S1: no approve record ──

    [Theory]
    [MemberData(nameof(CountedReaders))]
    public async Task S1_AnApprovedInvoiceWithNoApproveRecord_IsNotCounted_AndWithItsRecordItIs(string reader)
    {
        var world = new SpendWorld();
        var invoice = world.Invoice(Matter, MatterId, 1000m);
        world.Portion(invoice);

        (await CountedAsync(reader, world)).Should().Be(0m, "no writer-created approve record exists");

        world.Records.Clear();
        world.ApproveRecord(invoice, SpendWorld.KeyOf(invoice.Id, world.Portions.Single().Id));
        (await CountedAsync(reader, world)).Should().Be(1000m, "control: the same invoice with its record counts");
    }

    // ── S2: a record by another identity, or on the writer's behalf ──

    [Theory]
    [MemberData(nameof(CountedReaders))]
    public async Task S2_ARecordByAnotherIdentity_OrOnTheWritersBehalf_IsNotCounted_TheWritersOwnIs(string reader)
    {
        var world = new SpendWorld();
        var byOther = world.Invoice(Matter, MatterId, 100m);
        world.ApproveRecord(byOther, SpendWorld.KeyOf(byOther.Id, world.Portion(byOther)), createdBy: SpendWorld.OtherUser);
        var onBehalf = world.Invoice(Matter, MatterId, 200m);
        world.ApproveRecord(onBehalf, SpendWorld.KeyOf(onBehalf.Id, world.Portion(onBehalf)), onBehalfOf: SpendWorld.OtherUser);
        world.Approved(Matter, MatterId, 400m);   // control

        (await CountedAsync(reader, world)).Should().Be(400m, "only the writer's own record counts");
    }

    // ── Key: the record must carry the approval's own key ──

    public static TheoryData<string, string> KeyCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var reader in new[] { "evaluator", "rollup", "tool" })
        {
            foreach (var shape in new[] { "another-invoices-key", "unrelated-key", "no-key", "unreadable-steps", "no-portion" })
            {
                data.Add(reader, shape);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(KeyCases))]
    public async Task Key_AWriterRecordUnderAnyOtherKey_IsNotCounted_ItsOwnKeyIs(string reader, string shape)
    {
        var world = new SpendWorld();
        var invoice = world.Invoice(Matter, MatterId, 700m);
        var other = world.Invoice(FinanceBaseValues.ProjectEntity, Guid.NewGuid(), 5m);
        var portion = shape == "no-portion" ? Guid.NewGuid() : world.Portion(invoice);
        var otherPortion = world.Portion(other);
        switch (shape)
        {
            case "another-invoices-key": world.ApproveRecord(invoice, SpendWorld.KeyOf(other.Id, otherPortion)); break;
            case "unrelated-key": world.ApproveRecord(invoice, Guid.NewGuid()); break;
            case "no-key": world.ApproveRecord(invoice, key: null); break;
            case "unreadable-steps": world.ApproveRecord(invoice, key: null, steps: "{not json"); break;
            case "no-portion": world.ApproveRecord(invoice, SpendWorld.KeyOf(invoice.Id, portion)); break;
        }

        (await CountedAsync(reader, world)).Should().Be(0m, $"a writer record under {shape} is not the approval's");

        if (shape == "no-portion") world.Portions.Add(new Entity("sprk_invoiceapproval", portion) { ["sprk_invoice"] = new EntityReference("sprk_invoice", invoice.Id) });
        else world.ApproveRecord(invoice, SpendWorld.KeyOf(invoice.Id, portion));
        (await CountedAsync(reader, world)).Should().Be(700m, "control: the approval's own key counts");
    }

    // ── D-211 and the amount rule ──

    [Theory]
    [MemberData(nameof(CountedReaders))]
    public async Task D211_AReviewerAddedLine_CountsAtTheApprovedTotal_AndTheAddLineRecordIsNotAnApproveRecord(string reader)
    {
        var world = new SpendWorld();
        var added = world.Approved(Matter, MatterId, 1000m, approved: 1300m);                       // approved > billed (EB-D-199)
        world.ApproveRecord(added, SpendWorld.KeyOf(added.Id, world.Portions.Single().Id), actionCode: InvoiceLineAddService.ActionCode);
        var addLineOnly = world.Invoice(Matter, MatterId, 50m, approved: 80m);                        // only an add-line record, under the key
        world.ApproveRecord(addLineOnly, SpendWorld.KeyOf(addLineOnly.Id, world.Portion(addLineOnly)), actionCode: InvoiceLineAddService.ActionCode);
        world.Approved(Matter, MatterId, 900m, approved: 0m);                                         // everything disallowed: counts 0, not 900

        (await CountedAsync(reader, world)).Should().Be(1300m, "1300 (approved, not billed) + 0 (approved total 0 is a value), and no add-line record counts");
    }

    // ── S5: two approve records ──

    [Theory]
    [MemberData(nameof(CountedReaders))]
    public async Task S5_AnInvoiceWithTwoApproveRecords_IsCountedOnce(string reader)
    {
        var world = new SpendWorld();
        var invoice = world.Approved(Matter, MatterId, 250m);
        world.ApproveRecord(invoice, SpendWorld.KeyOf(invoice.Id, world.Portions.Single().Id));
        world.Portion(invoice);   // and a second portion

        (await CountedAsync(reader, world)).Should().Be(250m);
    }

    // ── S3: pending leaves out what InvoiceBlockedHold holds ──

    public static TheoryData<string, string> HoldCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var reader in new[] { "pending", "rollup" })
        {
            foreach (var shape in new[] { "recorded-outcome-blocked", "column-state", "own-submission-unlinked" })
            {
                data.Add(reader, shape);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(HoldCases))]
    public async Task S3_AHeldInReviewInvoice_IsNotPending_AndReleasedByAnOverrideItIs(string reader, string shape)
    {
        var world = new SpendWorld();
        var held = world.Invoice(Matter, MatterId, 600m, InvoiceStatusCodes.InReview);
        switch (shape)
        {
            case "recorded-outcome-blocked": world.OwnSubmission(held, kind: "Blocked"); break;   // columns say Accepted: only the findings show it
            case "column-state": world.OwnSubmission(held, status: (int)InvoiceSubmissionStatus.Processing, reasonCode: FinanceBaseValues.BlockedReasonCode); break;
            case "own-submission-unlinked": world.OwnSubmission(held, linked: false); break;
        }

        world.Pending(Matter, MatterId, 40m);   // an open one alongside

        (await PendingAsync(reader, world)).Should().Be(40m, $"the {shape} hold leaves pending spend");

        world.OwnSubmission(held, kind: "Blocked", overrideStamp: true);   // released by an Override (server-written stamp), linked, columns Accepted
        (await PendingAsync(reader, world)).Should().Be(640m, "control: released, it is pending");
    }

    [Fact]
    public async Task S3_TheUnlinkedInvoicesLeftOut_AreCountedInTheLog_WithNoExtraRead()
    {
        var world = new SpendWorld();
        world.OwnSubmission(world.Invoice(Matter, MatterId, 10m, InvoiceStatusCodes.InReview), linked: false);
        world.OwnSubmission(world.Invoice(Matter, MatterId, 20m, InvoiceStatusCodes.InReview), linked: false);
        world.Pending(Matter, MatterId, 5m);
        var logger = new CapturingLogger();

        (await PendingAsync("pending", world, logger)).Should().Be(5m);

        logger.Warnings.Should().ContainSingle().Which.Should().Contain("2 In Review invoice(s)");
        world.ReadCount.Should().Be(2, "the count is taken in memory: one invoice read and one submission read");
    }

    // ── O5: the canary ──

    [Theory]
    [MemberData(nameof(PendingReaders))]
    public async Task O5_AnUnreadableHoldSource_FailsThePendingRead_NeverHeldOrNotHeld(string reader)
    {
        var world = new SpendWorld();
        world.Pending(Matter, MatterId, 75m);
        world.FindingsUnreadable = true;

        if (reader == "pending")
        {
            await FluentActions.Awaiting(() => PendingAsync(reader, world)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*no readable findings*");
        }
        else
        {
            var run = await RunRollupAsync(world);
            run.Counted.Should().Be(0m, "the counted rollup is still written");
            run.Pending.Should().BeNull("the stored pending value is left as it was, never 0");
        }

        world.FindingsUnreadable = false;
        (await PendingAsync(reader, world)).Should().Be(75m, "control: readable findings give a value");
    }

    // ── S4: a fault fails the read the way the path fails today ──

    public static TheoryData<string, string> FaultCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var reader in new[] { "evaluator", "rollup", "tool" })
        {
            foreach (var read in new[] { "sprk_invoice", "sprk_invoiceapproval", "sprk_decisionrecord", "writer-id" })
            {
                data.Add(reader, read);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FaultCases))]
    public async Task S4_AFaultOnAnyCountedRead_OrOnTheWriterId_FailsTheRead_NeverCountsTheInvoice(string reader, string read)
    {
        var world = new SpendWorld();
        world.Approved(Matter, MatterId, 300m);
        world.FaultOn = q => q.EntityName == read ? new InvalidOperationException("Dataverse unavailable") : null;
        var whoAmIFault = read == "writer-id" ? new InvalidOperationException("WhoAmI failed") : null;

        switch (reader)
        {
            case "evaluator":
                // The evaluator reads the writer id itself (SignalEvaluator); here the reader refuses an unknown one (Guid.Empty).
                var writer = read == "writer-id" ? Guid.Empty : SpendWorld.Writer;
                await FluentActions.Awaiting(() => FinanceBaseValues.ReadAsync(world.Service().Object, Matter, [MatterId], true, false, writer, CancellationToken.None))
                    .Should().ThrowAsync<Exception>();
                break;
            case "rollup":
                var run = new RollupRun();
                var rollup = new FinanceRollupService(world.Service().Object, run.Writes.Object, Recorder(whoAmIFault), NullLogger<FinanceRollupService>.Instance);
                await FluentActions.Awaiting(() => rollup.RecalculateMatterAsync(MatterId)).Should().ThrowAsync<Exception>();
                run.Written.Should().BeEmpty("the counted write is not made and pending is unchanged");
                break;
            case "tool":
                (await RunToolAsync(world, whoAmIFault)).Success.Should().BeFalse();
                break;
        }
    }

    [Theory]
    [MemberData(nameof(PendingReaders))]
    public async Task S4_AFaultOnThePendingReads_FailsPending_TheRollupLeavesItUnchanged(string reader)
    {
        foreach (var read in new[] { "sprk_invoicesubmission", "sprk_invoice" })
        {
            var world = new SpendWorld();
            world.Pending(Matter, MatterId, 75m);
            world.FaultOn = q => q.EntityName == read && q.Criteria.Conditions.All(c => c.AttributeName != "statuscode" || c.Values.Contains(InvoiceStatusCodes.InReview))
                ? new InvalidOperationException("Dataverse unavailable") : null;

            if (reader == "pending")
            {
                await FluentActions.Awaiting(() => PendingAsync(reader, world)).Should().ThrowAsync<InvalidOperationException>();
            }
            else
            {
                (await RunRollupAsync(world)).Pending.Should().BeNull($"a fault on {read} leaves the stored pending value unchanged");
            }
        }
    }

    [Fact]
    public async Task S4_AnUnknownWriter_IsRefusedBeforeAnyRead_EvenWhenNoInvoiceIsCounted()
    {
        var world = new SpendWorld();

        await FluentActions.Awaiting(() => FinanceBaseValues.ReadAsync(world.Service().Object, Matter, [MatterId], true, false, Guid.Empty, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => FinanceBaseValues.ReadCountedInvoicesAsync(world.Service().Object, Matter, [MatterId], Guid.Empty, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
        world.ReadCount.Should().Be(0, "never 'no record, so 0 counted spend'");

        // A read that needs no spend never asks for the writer.
        world.Budget(Matter, MatterId, 10m);
        (await FinanceBaseValues.ReadAsync(world.Service().Object, Matter, [MatterId], false, true, Guid.Empty, CancellationToken.None))[MatterId].Budget.Should().Be(10m);
    }

    // ── O6: parity across the three readers ──

    [Fact]
    public async Task O6_TheSameFixture_GivesTheSameSpend_ThroughAllThreeReaders()
    {
        var world = new SpendWorld();
        world.Approved(Matter, MatterId, 1000m, approved: 800m);
        world.Approved(Matter, MatterId, 400m, InvoiceStatusCodes.Paid);
        world.Approved(Matter, MatterId, 50m, InvoiceStatusCodes.SentToAp, approved: -250m);     // a credit memo
        world.Approved(Matter, MatterId, 900m, approved: 0m);
        var unrecorded = world.Invoice(Matter, MatterId, 5000m);                                   // no record
        world.Portion(unrecorded);
        var foreign = world.Invoice(Matter, MatterId, 6000m);                                      // another identity's record
        world.ApproveRecord(foreign, SpendWorld.KeyOf(foreign.Id, world.Portion(foreign)), createdBy: SpendWorld.OtherUser);
        world.Approved(FinanceBaseValues.ProjectEntity, Guid.NewGuid(), 7000m);                     // another parent
        world.Pending(Matter, MatterId, 30m);
        world.OwnSubmission(world.Invoice(Matter, MatterId, 8000m, InvoiceStatusCodes.InReview), kind: "Blocked");   // held

        var counted = new Dictionary<string, decimal>();
        foreach (var reader in new[] { "evaluator", "rollup", "tool" })
        {
            counted[reader] = await CountedAsync(reader, world);
        }

        counted.Values.Should().AllBeEquivalentTo(950m, "800 + 400 - 250 + 0; the unrecorded, the foreign and the other parent's invoices are not counted");
        (await PendingAsync("pending", world)).Should().Be(30m);
        (await PendingAsync("rollup", world)).Should().Be(30m, "one pending reader: the rollup calls ReadPendingSpendAsync");
    }

    // ── Count: 1 invoice and 50 invoices make the same reads ──

    [Fact]
    public async Task Count_OneInvoiceAndFiftyInvoices_GiveEqualReadCounts()
    {
        static async Task<(int Counted, int Pending)> ReadsFor(int invoices)
        {
            var world = new SpendWorld();
            world.Budget(Matter, MatterId, 1m);
            for (var i = 0; i < invoices; i++)
            {
                world.Approved(Matter, MatterId, 10m);
                world.Pending(Matter, MatterId, 1m);
            }

            await FinanceBaseValues.ReadAsync(world.Service().Object, Matter, [MatterId], true, true, SpendWorld.Writer, CancellationToken.None);
            var counted = world.ReadCount;
            await FinanceBaseValues.ReadPendingSpendAsync(world.Service().Object, Matter, [MatterId], CancellationToken.None);
            return (counted, world.ReadCount - counted);
        }

        var one = await ReadsFor(1);
        var fifty = await ReadsFor(50);

        one.Should().Be(fifty);
        one.Should().Be((4, 2), "row + portion + record + budget for spend, row + submission batch for pending (NFR-01: never one per invoice)");
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
