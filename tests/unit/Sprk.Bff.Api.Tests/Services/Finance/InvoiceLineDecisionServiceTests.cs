using System.Collections.Concurrent;
using System.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 100 (FR-31, FR-07, FR-30, D-64, D-75, D-77, D-128, D-129): <see cref="InvoiceLineDecisionService"/>. It runs over the REAL
/// <see cref="InvoiceTotalsService"/> (016) and the REAL <see cref="BillingDecisionRecorder"/> on the writer's test harness, with the Dataverse
/// boundary modelled in memory (row versions, conditional all-or-nothing line writes, upserts), so each test proves the stored state and the
/// ledger, not the calls the service makes.
/// </summary>
public sealed class InvoiceLineDecisionServiceTests
{
    private static readonly Guid Decider = Guid.Parse("01000000-0000-4000-8000-000000000001");
    private static readonly Guid Core = Guid.Parse("01000000-0000-4000-8000-0000000000c0");
    private static readonly Guid Reviewer = Decider;
    private const string Reason = "Rate above the agreed card";

    private sealed class InMemoryLocks : IIdempotencyService
    {
        private readonly ConcurrentDictionary<string, string> _held = new();

        public bool AlwaysGrant { get; set; }

        public int Acquired;

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
        {
            var granted = AlwaysGrant || _held.TryAdd(eventId, eventId);
            if (granted)
            {
                Interlocked.Increment(ref Acquired);
            }

            return Task.FromResult(granted);
        }

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default)
        {
            _held.TryRemove(eventId, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class World
    {
        public readonly Guid InvoiceId = Guid.NewGuid();
        public Entity Invoice;
        public readonly List<Entity> Lines = [];
        public readonly Dictionary<Guid, Entity> Corrections = new();
        public readonly List<(string Entity, int Updates)> LineWrites = [];
        public readonly List<Dictionary<string, object?>> TotalsWrites = [];
        public readonly InMemoryLocks Locks = new();
        public readonly DecisionRecordWriterTests.Harness H;
        public readonly Mock<IGenericEntityService> Entities = new();
        public readonly Mock<IFieldMappingDataverseService> Records = new();
        public readonly Mock<IIdentityNormalizationService> Identity = new();
        public readonly Mock<IRecordOwnershipResolver> Ownership = new();
        public int QueryCount;
        public Exception? ReadFault;
        public Exception? CorrectionFault;
        public Exception? TotalsFault;
        public Action<string>? BeforeLineWrite;
        public RecordOwnerResolution Owner = RecordOwnerResolution.Owned(Guid.Parse("01000000-0000-4000-8000-0000000000d0"));
        private long _nextVersion = 10;

        public World(decimal billedTotal = 1000m, int status = InvoiceStatusCodes.InReview)
        {
            Invoice = new Entity("sprk_invoice", InvoiceId)
            {
                ["statuscode"] = new OptionSetValue(status),
                ["sprk_totalamount"] = new Money(billedTotal),
                ["sprk_project"] = new EntityReference("sprk_project", Core),
                ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", Guid.NewGuid()),
                ["sprk_regardingrecordid"] = Core.ToString("D"),
                ["sprk_regardingrecordname"] = "Project",
                ["sprk_regardingrecordurl"] = "https://example.invalid/p",
                ["sprk_regardingrecordnumber"] = "P-1",
            };

            H = DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness());
            H.Rows[("sprk_invoice", InvoiceId)] = new Entity("sprk_invoice", InvoiceId) { ["sprk_project"] = new EntityReference("sprk_project", Core) };

            Identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid user, CancellationToken _) => new PersonIdentity(user, ContactId: Guid.Parse("01000000-0000-4000-8000-0000000000e1")));
            Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Owner);

            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken _) =>
                {
                    Interlocked.Increment(ref QueryCount);
                    if (ReadFault is not null)
                    {
                        return Task.FromException<EntityCollection>(ReadFault);
                    }

                    lock (Lines)
                    {
                        return Task.FromResult(query.EntityName switch
                        {
                            "sprk_invoice" => new EntityCollection([Copy(Invoice)]),
                            "sprk_billingevent" => new EntityCollection(Lines.Select(Copy).ToList()),
                            _ => new EntityCollection(),
                        });
                    }
                });

            Entities.Setup(e => e.BulkUpdateIfRowVersionsMatchAsync(
                    It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields, long rowVersion)>>(), It.IsAny<CancellationToken>()))
                .Returns((string entity, List<(Guid id, Dictionary<string, object> fields, long rowVersion)> updates, CancellationToken _) =>
                {
                    BeforeLineWrite?.Invoke(entity);
                    lock (Lines)
                    {
                        // All or nothing, each update only at the row version read.
                        if (updates.Any(u => Lines.Single(l => l.Id == u.id).GetAttributeValue<long>("versionnumber") != u.rowVersion))
                        {
                            throw new DBConcurrencyException("simulated row version mismatch: nothing applied");
                        }

                        LineWrites.Add((entity, updates.Count));
                        foreach (var (id, fields, _) in updates)
                        {
                            var line = Lines.Single(l => l.Id == id);
                            foreach (var (name, value) in fields)
                            {
                                if (value is DBNull)
                                {
                                    line.Attributes.Remove(name);
                                }
                                else
                                {
                                    line[name] = value;
                                }
                            }

                            line["versionnumber"] = ++_nextVersion;
                        }
                    }

                    return Task.CompletedTask;
                });

            Entities.Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken _) =>
                {
                    if (CorrectionFault is not null)
                    {
                        return Task.FromException(CorrectionFault);
                    }

                    foreach (var upsert in requests.OfType<UpsertRequest>())
                    {
                        Corrections[upsert.Target.Id] = Copy(upsert.Target);
                    }

                    return Task.CompletedTask;
                });

            Records.Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns(() => TotalsFault is not null
                    ? Task.FromException<Dictionary<string, object?>>(TotalsFault)
                    : Task.FromResult(new Dictionary<string, object?>
                    {
                        ["sprk_totalamount"] = Invoice.GetAttributeValue<Money>("sprk_totalamount").Value,
                        ["versionnumber"] = 5L,
                    }));
            Records.Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 5L, It.IsAny<CancellationToken>()))
                .Callback((string _, Guid _, Dictionary<string, object?> fields, long _, CancellationToken _) =>
                {
                    TotalsWrites.Add(fields);
                    Invoice["sprk_approvedtotal"] = new Money((decimal)fields["sprk_approvedtotal"]!);
                })
                .Returns(Task.CompletedTask);
            Records.Setup(r => r.UpdateExistingRecordFieldsAsync("sprk_billingevent", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        public InvoiceLineDecisionService Service()
        {
            var recorder = new BillingDecisionRecorder(H.Writer, Identity.Object, NullLogger<BillingDecisionRecorder>.Instance);
            var totals = new InvoiceTotalsService(Records.Object, Entities.Object, NullLogger<InvoiceTotalsService>.Instance);
            return new InvoiceLineDecisionService(Entities.Object, totals, recorder, Locks, Ownership.Object, NullLogger<InvoiceLineDecisionService>.Instance);
        }

        public Entity AddLine(
            decimal amount, InvoiceLineStatus status, InvoiceLineCostType type = InvoiceLineCostType.Fee, string? extraction = null, decimal? approved = null,
            InvoiceAdjustmentMethod? method = null, decimal? adjustmentValue = null)
        {
            var id = Guid.NewGuid();
            var line = new Entity("sprk_billingevent", id)
            {
                ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
                ["sprk_linesequence"] = Lines.Count + 1,
                ["sprk_costtype"] = new OptionSetValue((int)type),
                ["sprk_amount"] = new Money(amount),
                ["sprk_linestatus"] = new OptionSetValue((int)status),
                ["sprk_description"] = "Draft the response",
                ["sprk_quantity"] = 2.5m,
                ["sprk_rate"] = new Money(amount / 2.5m),
                ["sprk_eventdate"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                ["versionnumber"] = ++_nextVersion,
            };
            if (approved is not null) line["sprk_approvedamount"] = new Money(approved.Value);
            if (extraction is not null) line["sprk_extractionconfidence"] = extraction;
            if (method is not null) line["sprk_adjustmentmethod"] = new OptionSetValue((int)method.Value);
            if (adjustmentValue is not null) line["sprk_adjustmentvalue"] = adjustmentValue.Value;
            Lines.Add(line);
            H.Rows[("sprk_billingevent", id)] = new Entity("sprk_billingevent", id)
            {
                ["sprk_project"] = new EntityReference("sprk_project", Core),
                ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
            };
            return line;
        }

        public Entity Line(Guid id) => Lines.Single(l => l.Id == id);

        public IEnumerable<Entity> Records_(string action) => H.Created.Where(r => r.GetAttributeValue<string>("sprk_actioncode") == action);

        public decimal ApprovedTotal => Invoice.GetAttributeValue<Money>("sprk_approvedtotal").Value;

        private static Entity Copy(Entity source)
        {
            var copy = new Entity(source.LogicalName, source.Id);
            foreach (var attribute in source.Attributes)
            {
                copy[attribute.Key] = attribute.Value;
            }

            return copy;
        }
    }

    /// <summary>An extraction JSON as 049 stores it: date, hours, rate, amount and narrative extracted; the quantity not; task and activity coded.</summary>
    private const string Extraction =
        "{\"fields\":{\"Date\":{\"confidence\":0.95,\"combined\":0.9,\"page\":1},\"Hours\":{\"confidence\":0.9,\"page\":1},\"Rate\":{\"confidence\":0.92,\"page\":1},"
        + "\"Quantity\":{\"confidence\":null,\"page\":null},\"Amount\":{\"confidence\":0.99,\"page\":1},\"Narrative\":{\"confidence\":0.8,\"page\":1}},"
        + "\"coding\":{\"status\":\"Coded\",\"confidence\":0.82,\"task\":{\"code\":\"L110\",\"mismatch\":false},\"activity\":{\"code\":\"A101\",\"mismatch\":false},\"expense\":{\"code\":null}}}";

    private static LineDecisionCommand Command(
        World w, LineDecisionAction action, IEnumerable<LineDecisionLine> lines, string? reason = null, Guid? decisionId = null, bool bulk = false) =>
        new(w.InvoiceId, decisionId ?? Guid.NewGuid(), action, reason, lines.ToList(), Decider, bulk);

    // ── Outcomes: the line, the totals, the ledger ──

    [Fact(DisplayName = "100: a Reduce stores status, approved amount, reason, reviewer and time, recomputes the approved total, and records one Decision Record on the line")]
    public async Task Reduce_StoresTheDecision_RecomputesTheApprovedTotal_AndRecordsOneDecisionRecord()
    {
        var w = new World(billedTotal: 1000m);
        var line = w.AddLine(600m, InvoiceLineStatus.Flagged);
        w.AddLine(400m, InvoiceLineStatus.Clean);

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Reduce, [new(line.Id, 450m)], Reason), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        var stored = w.Line(line.Id);
        stored.GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Reduced);
        stored.GetAttributeValue<Money>("sprk_approvedamount").Value.Should().Be(450m);
        stored.GetAttributeValue<string>("sprk_adjustmentreason").Should().Be(Reason);
        stored.GetAttributeValue<EntityReference>("sprk_reviewedby").Id.Should().Be(Reviewer);
        stored.GetAttributeValue<DateTime>("sprk_reviewedon").Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        w.ApprovedTotal.Should().Be(850m, "I-1 via 016: 1000 billed - (600 - 450) reduced");
        result.Totals!.Approved.Should().Be(850m);
        w.H.Created.Should().ContainSingle();
        var record = w.H.Created.Single();
        record.GetAttributeValue<string>("sprk_actioncode").Should().Be("reduce-line");
        record.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline").Id.Should().Be(line.Id, "D-75: the line is the subject");
        record.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Decider, "the decider is the authenticated user, passed by the endpoint");
    }

    [Fact(DisplayName = "100: an Accept sets approved = billed, clears an earlier reason, and writes no correction rows on the single-line path")]
    public async Task Accept_ApprovesTheBilledAmount_ClearsAnEarlierReason_AndWritesNoCorrectionRows()
    {
        var w = new World(billedTotal: 600m);
        var line = w.AddLine(600m, InvoiceLineStatus.Reduced, approved: 300m, extraction: Extraction);
        line["sprk_adjustmentreason"] = "earlier";

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        var stored = w.Line(line.Id);
        stored.GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Accepted);
        stored.GetAttributeValue<Money>("sprk_approvedamount").Value.Should().Be(600m);
        stored.Contains("sprk_adjustmentreason").Should().BeFalse("an Accept after a Reduce drops the reduction's reason");
        w.ApprovedTotal.Should().Be(600m, "accepting after a Reduce restores the amount: spend follows the approved total (D-130)");
        w.Corrections.Should().BeEmpty("D-64: the single-line Accept writes no correction rows (101 owns the label outcomes)");
        w.H.Created.Single().GetAttributeValue<string>("sprk_actioncode").Should().Be("accept-line");
    }

    [Fact(DisplayName = "100: a Disallow sets the approved amount to 0 and needs a reason")]
    public async Task Disallow_SetsApprovedToZero()
    {
        var w = new World(billedTotal: 1000m);
        var line = w.AddLine(250m, InvoiceLineStatus.Flagged);
        w.AddLine(750m, InvoiceLineStatus.Clean);

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Disallow, [new(line.Id)], "Block billing"), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        w.Line(line.Id).GetAttributeValue<Money>("sprk_approvedamount").Value.Should().Be(0m);
        w.ApprovedTotal.Should().Be(750m);
        w.H.Created.Single().GetAttributeValue<string>("sprk_actioncode").Should().Be("disallow-line");
    }

    // ── Validation: nothing is written ──

    [Theory(DisplayName = "100: Reduce or Disallow without a reason is refused (400) and nothing is read or written")]
    [InlineData(LineDecisionAction.Reduce, null)]
    [InlineData(LineDecisionAction.Reduce, "   ")]
    [InlineData(LineDecisionAction.Disallow, null)]
    [InlineData(LineDecisionAction.Disallow, "")]
    public async Task MissingReason_IsRefused_BeforeAnyRead(LineDecisionAction action, string? reason)
    {
        var w = new World();
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);

        var result = await w.Service().DecideAsync(Command(w, action, [new(line.Id, action == LineDecisionAction.Reduce ? 50m : null)], reason), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.InvalidRequest);
        result.Code.Should().Be(LineDecisionCodes.ReasonRequired);
        w.QueryCount.Should().Be(0);
        w.LineWrites.Should().BeEmpty();
        w.H.Created.Should().BeEmpty();
    }

    [Theory(DisplayName = "100: a Reduce amount outside 0 .. billed (or equal to billed) is refused (400) and nothing is written")]
    [InlineData(-0.01)]
    [InlineData(100)]
    [InlineData(100.01)]
    public async Task ReduceAmountOutsideTheRange_IsRefused(double amount)
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Reduce, [new(line.Id, (decimal)amount)], Reason), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.InvalidRequest);
        result.Code.Should().Be(LineDecisionCodes.AmountOutOfRange);
        w.LineWrites.Should().BeEmpty();
        w.H.Created.Should().BeEmpty();
    }

    [Fact(DisplayName = "100: a Reduce without an amount, and an amount on an Accept or Disallow, are refused (400)")]
    public async Task AmountRules_ByAction()
    {
        var w = new World();
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var service = w.Service();

        (await service.DecideAsync(Command(w, LineDecisionAction.Reduce, [new(line.Id)], Reason), CancellationToken.None)).Code.Should().Be(LineDecisionCodes.AmountRequired);
        (await service.DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id, 50m)]), CancellationToken.None)).Code.Should().Be(LineDecisionCodes.AmountNotAllowed);
        (await service.DecideAsync(Command(w, LineDecisionAction.Disallow, [new(line.Id, 0m)], Reason), CancellationToken.None)).Code.Should().Be(LineDecisionCodes.AmountNotAllowed);
        w.LineWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "100: a reason over the column length and a request with no, duplicate or empty line ids are refused (400)")]
    public async Task RequestShape_IsRefused()
    {
        var w = new World();
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var service = w.Service();

        (await service.DecideAsync(Command(w, LineDecisionAction.Disallow, [new(line.Id)], new string('x', 2001)), CancellationToken.None)).Code.Should().Be(LineDecisionCodes.ReasonTooLong);
        (await service.DecideAsync(Command(w, LineDecisionAction.Accept, []), CancellationToken.None)).Code.Should().Be(LineDecisionCodes.InvalidRequest);
        (await service.DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id), new(line.Id)]), CancellationToken.None)).Code.Should().Be(LineDecisionCodes.InvalidRequest);
        (await service.DecideAsync(Command(w, LineDecisionAction.Accept, [new(Guid.Empty)]), CancellationToken.None)).Code.Should().Be(LineDecisionCodes.InvalidRequest);
        (await service.DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)], decisionId: Guid.Empty), CancellationToken.None)).Code.Should().Be(LineDecisionCodes.InvalidRequest);
        w.LineWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "100: a Pending line cannot be decided (409) and nothing is written")]
    public async Task PendingLine_IsRefused()
    {
        var w = new World();
        var line = w.AddLine(100m, InvoiceLineStatus.Pending);

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.LineNotDecidable);
        result.Code.Should().Be(LineDecisionCodes.LinePending);
        w.LineWrites.Should().BeEmpty();
        w.H.Created.Should().BeEmpty();
    }

    [Fact(DisplayName = "100: an Adjustment line is computed, not decided (409)")]
    public async Task AdjustmentLine_IsNotDecidable()
    {
        var w = new World();
        var adjustment = w.AddLine(0m, InvoiceLineStatus.Clean, InvoiceLineCostType.Adjustment, method: InvoiceAdjustmentMethod.Fixed, adjustmentValue: 10m);

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(adjustment.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.LineNotDecidable);
        result.Code.Should().Be(LineDecisionCodes.LineNotDecidable);
        w.LineWrites.Should().BeEmpty();
    }

    [Theory(DisplayName = "100: an invoice that is not In Review refuses every decision (409) and writes nothing")]
    [InlineData(InvoiceStatusCodes.Approved)]
    [InlineData(InvoiceStatusCodes.Rejected)]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm)]
    [InlineData(InvoiceStatusCodes.Paid)]
    public async Task NotInReview_IsRefused(int status)
    {
        var w = new World(status: status);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var service = w.Service();

        var single = await service.DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);
        var clean = await service.AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), Decider, CancellationToken.None);

        single.Status.Should().Be(LineDecisionStatus.NotInReview);
        single.Code.Should().Be(LineDecisionCodes.NotInReview);
        clean.Status.Should().Be(LineDecisionStatus.NotInReview);
        w.LineWrites.Should().BeEmpty();
        w.H.Created.Should().BeEmpty();
    }

    [Fact(DisplayName = "100: a line of another invoice is the uniform not-found and nothing is written, even when another line in the request is valid")]
    public async Task ForeignLine_IsNotFound_AndNothingIsWritten()
    {
        var w = new World();
        var mine = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var foreign = Guid.NewGuid(); // a line of some other invoice is simply not among this invoice's lines

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(mine.Id), new(foreign)], bulk: true), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.LineNotFound);
        w.LineWrites.Should().BeEmpty();
        w.Corrections.Should().BeEmpty();
        w.H.Created.Should().BeEmpty();
    }

    [Fact(DisplayName = "100: a multi-line request with one invalid line writes nothing (all or nothing)")]
    public async Task MultiLine_WithOneInvalidLine_WritesNothing()
    {
        var w = new World(billedTotal: 300m);
        var a = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var b = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var pending = w.AddLine(100m, InvoiceLineStatus.Pending);

        var result = await w.Service().DecideAsync(
            Command(w, LineDecisionAction.Disallow, [new(a.Id), new(b.Id), new(pending.Id)], Reason, bulk: true), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.LineNotDecidable);
        w.LineWrites.Should().BeEmpty();
        w.Line(a.Id).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Flagged);
        w.H.Created.Should().BeEmpty();

        var badAmount = await w.Service().DecideAsync(
            Command(w, LineDecisionAction.Reduce, [new(a.Id, 40m), new(b.Id, 100m)], Reason, bulk: true), CancellationToken.None);
        badAmount.Code.Should().Be(LineDecisionCodes.AmountOutOfRange);
        w.LineWrites.Should().BeEmpty();
    }

    // ── Multi-line: one action, a per-line amount for a Reduce, one Decision Record per line (D-75) ──

    [Fact(DisplayName = "100: one Reduce on several lines applies each line's own amount and records one Decision Record per line")]
    public async Task MultiLineReduce_AppliesPerLineAmounts_AndRecordsOneRecordPerLine()
    {
        var w = new World(billedTotal: 300m);
        var a = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var b = w.AddLine(200m, InvoiceLineStatus.Flagged);

        var result = await w.Service().DecideAsync(
            Command(w, LineDecisionAction.Reduce, [new(a.Id, 80m), new(b.Id, 150m)], Reason, bulk: true), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        w.Line(a.Id).GetAttributeValue<Money>("sprk_approvedamount").Value.Should().Be(80m);
        w.Line(b.Id).GetAttributeValue<Money>("sprk_approvedamount").Value.Should().Be(150m);
        w.ApprovedTotal.Should().Be(230m);
        w.LineWrites.Should().ContainSingle("one conditional all-or-nothing write for the request").Which.Updates.Should().Be(2);
        w.H.Created.Should().HaveCount(2);
        w.H.Created.Select(r => r.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline").Id).Should().BeEquivalentTo([a.Id, b.Id]);
        w.Corrections.Should().BeEmpty("only an Accept writes the bulk correction rows");
    }

    // ── Accept all Clean and the bulk correction rows (D-64) ──

    [Fact(DisplayName = "100: Accept all Clean touches only Clean Fee and Expense lines and writes 'Accepted in bulk' rows with islabel false")]
    public async Task AcceptClean_TouchesOnlyCleanLines_AndWritesBulkCorrectionRows()
    {
        var w = new World(billedTotal: 700m);
        var clean = w.AddLine(100m, InvoiceLineStatus.Clean, extraction: Extraction);
        var clean2 = w.AddLine(200m, InvoiceLineStatus.Clean, InvoiceLineCostType.Expense);
        var flagged = w.AddLine(150m, InvoiceLineStatus.Flagged);
        var pending = w.AddLine(100m, InvoiceLineStatus.Pending);
        var reduced = w.AddLine(150m, InvoiceLineStatus.Reduced, approved: 100m);
        var decisionId = Guid.NewGuid();

        var result = await w.Service().AcceptCleanAsync(w.InvoiceId, decisionId, Decider, CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        result.Lines!.Select(l => l.LineId).Should().BeEquivalentTo([clean.Id, clean2.Id]);
        Status(w, clean).Should().Be(InvoiceLineStatus.Accepted);
        Status(w, clean2).Should().Be(InvoiceLineStatus.Accepted);
        Status(w, flagged).Should().Be(InvoiceLineStatus.Flagged);
        Status(w, pending).Should().Be(InvoiceLineStatus.Pending);
        Status(w, reduced).Should().Be(InvoiceLineStatus.Reduced);
        w.Line(reduced.Id).GetAttributeValue<Money>("sprk_approvedamount").Value.Should().Be(100m, "an already decided line is untouched");
        w.ApprovedTotal.Should().Be(650m, "the Reduced line's 50 off still stands");

        w.Corrections.Should().NotBeEmpty();
        w.Corrections.Values.Should().OnlyContain(r => r.GetAttributeValue<bool>("sprk_islabel") == false, "D-64: bulk accept is never a label");
        w.Corrections.Values.Should().OnlyContain(r => r.GetAttributeValue<OptionSetValue>("sprk_outcometype").Value == 100000002, "Accepted in bulk");
        w.Corrections.Values.Select(r => r.GetAttributeValue<EntityReference>("sprk_invoiceline").Id).Distinct().Should().BeEquivalentTo([clean.Id], "only lines with AI-sourced fields get rows");
        w.Corrections.Values.Select(r => r.GetAttributeValue<string>("sprk_fieldname")).Should()
            .BeEquivalentTo(["Date", "Hours", "Rate", "Amount", "Narrative", "TaskCode", "ActivityCode"], "the extracted fields with a confidence plus the coded codes; Quantity had none");
        w.Corrections.Values.Should().OnlyContain(r => r.GetAttributeValue<EntityReference>("sprk_invoice").Id == w.InvoiceId);
        w.Corrections.Values.Should().OnlyContain(r => r.Contains("sprk_regardingproject") && !r.Contains("sprk_regardingmatter"), "013: exactly one core-record lookup");
        w.Corrections.Values.Should().OnlyContain(r => r.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id == Decider);
        w.Corrections.Values.Single(r => r.GetAttributeValue<string>("sprk_fieldname") == "Amount").GetAttributeValue<string>("sprk_aivalue").Should().Be("100.00");
        w.H.Created.Should().HaveCount(2, "D-75: n clean lines write n Decision Records");
    }

    [Fact(DisplayName = "100: Accept all Clean with no Clean line writes nothing and recalculates nothing")]
    public async Task AcceptClean_WithNothingClean_WritesNothing()
    {
        var w = new World();
        w.AddLine(100m, InvoiceLineStatus.Flagged);

        var result = await w.Service().AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), Decider, CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        result.Lines.Should().BeEmpty();
        w.LineWrites.Should().BeEmpty();
        w.TotalsWrites.Should().BeEmpty();
        w.H.Created.Should().BeEmpty();
    }

    [Fact(DisplayName = "100: the multi-line Accept writes the bulk rows too; the correction row ids are deterministic so a repeat upserts the same rows")]
    public async Task MultiLineAccept_WritesBulkRows_WithDeterministicIds()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged, extraction: Extraction);
        var decisionId = Guid.NewGuid();
        var service = w.Service();

        await service.DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)], decisionId: decisionId, bulk: true), CancellationToken.None);
        var first = w.Corrections.Keys.ToHashSet();
        await service.DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)], decisionId: decisionId, bulk: true), CancellationToken.None);

        first.Should().NotBeEmpty();
        w.Corrections.Keys.Should().BeEquivalentTo(first, "the repeat rewrote the same rows, it did not add more");
        w.H.Created.Should().ContainSingle("the repeat recorded nothing twice");
    }

    [Fact(DisplayName = "100: correction rows without a resolvable owner refuse the request before the first write")]
    public async Task UnresolvedOwner_RefusesBeforeAnyWrite()
    {
        var w = new World { Owner = RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "no team") };
        w.AddLine(100m, InvoiceLineStatus.Clean, extraction: Extraction);

        var result = await w.Service().AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), Decider, CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.OwnerUnresolved);
        w.LineWrites.Should().BeEmpty();
        w.H.Created.Should().BeEmpty();
    }

    // ── Credit lines (D-128, D-129) ──

    [Fact(DisplayName = "100: a credit line can be disallowed, and reduced to an amount between the billed credit and 0 (never refused for being negative)")]
    public async Task CreditLine_CanBeDisallowed_AndReducedTowardsZero()
    {
        var w = new World(billedTotal: 900m);
        var charge = w.AddLine(1000m, InvoiceLineStatus.Clean);
        var credit = w.AddLine(-100m, InvoiceLineStatus.Flagged);
        var service = w.Service();

        var reduced = await service.DecideAsync(Command(w, LineDecisionAction.Reduce, [new(credit.Id, -40m)], "Only part of the credit applies"), CancellationToken.None);

        reduced.Status.Should().Be(LineDecisionStatus.Applied);
        w.ApprovedTotal.Should().Be(960m, "-100 reduced to -40 raises the payable by 60");

        var outside = await service.DecideAsync(Command(w, LineDecisionAction.Reduce, [new(credit.Id, -150m)], Reason), CancellationToken.None);
        outside.Code.Should().Be(LineDecisionCodes.AmountOutOfRange, "more credit than billed");
        (await service.DecideAsync(Command(w, LineDecisionAction.Reduce, [new(credit.Id, 10m)], Reason), CancellationToken.None))
            .Code.Should().Be(LineDecisionCodes.AmountOutOfRange, "the opposite sign");

        var disallowed = await service.DecideAsync(Command(w, LineDecisionAction.Disallow, [new(credit.Id)], "Not a valid credit"), CancellationToken.None);
        disallowed.Status.Should().Be(LineDecisionStatus.Applied);
        w.ApprovedTotal.Should().Be(1000m, "a disallowed credit raises the approved total");
        charge.Id.Should().NotBeEmpty();
    }

    // ── Totals: the dry run refuses before any write ──

    [Fact(DisplayName = "100: a decision that would make the approved total negative is refused (409) before anything is written")]
    public async Task TotalsRefused_WritesNothing()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        w.AddLine(0m, InvoiceLineStatus.Clean, InvoiceLineCostType.Adjustment, method: InvoiceAdjustmentMethod.Fixed, adjustmentValue: 60m);

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Disallow, [new(line.Id)], Reason), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.TotalsRefused);
        result.Code.Should().Be(LineDecisionCodes.TotalsRefused);
        w.LineWrites.Should().BeEmpty();
        w.H.Created.Should().BeEmpty();
    }

    // ── Exactly once and serialisation (074 obligation 1) ──

    [Fact(DisplayName = "100: repeating a request with the same decision id writes no second Decision Record and leaves the line as decided")]
    public async Task Retry_WithTheSameDecisionId_WritesNoSecondRecord()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var service = w.Service();
        var command = Command(w, LineDecisionAction.Reduce, [new(line.Id, 60m)], Reason);

        var first = await service.DecideAsync(command, CancellationToken.None);
        var retry = await service.DecideAsync(command, CancellationToken.None);

        first.Lines!.Single().AlreadyRecorded.Should().BeFalse();
        retry.Status.Should().Be(LineDecisionStatus.Applied);
        retry.Lines!.Single().AlreadyRecorded.Should().BeTrue();
        w.H.Created.Should().ContainSingle();
        w.ApprovedTotal.Should().Be(60m);
    }

    [Fact(DisplayName = "100: two concurrent requests with one decision id are serialised by the per-invoice lock: one applies, one is told to retry, one record exists")]
    public async Task ConcurrentSameDecisionId_IsSerialisedByTheInvoiceLock()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var service = w.Service();
        var command = Command(w, LineDecisionAction.Disallow, [new(line.Id)], Reason);
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var entered = 0;
        w.BeforeLineWrite = _ =>
        {
            if (Interlocked.Increment(ref entered) > 1)
            {
                return; // only the first request is held; a second one that gets in (no lock) is not blocked, so a broken lock fails the assertions instead of hanging
            }

            inside.TrySetResult();
            release.Task.Wait(TimeSpan.FromSeconds(20)); // hold the first request inside its critical section
        };

        var first = Task.Run(() => service.DecideAsync(command, CancellationToken.None));
        await inside.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var second = await service.DecideAsync(command, CancellationToken.None);
        release.SetResult();
        var firstResult = await first;

        second.Status.Should().Be(LineDecisionStatus.Busy);
        second.Code.Should().Be(LineDecisionCodes.Busy);
        firstResult.Status.Should().Be(LineDecisionStatus.Applied);
        w.H.Created.Should().ContainSingle("exactly one Decision Record for the decision id");
        w.LineWrites.Should().ContainSingle();
    }

    [Fact(DisplayName = "100: with the lock defeated, the row-version-conditional line write still lets only one of two concurrent same-id requests through, and the loser records nothing")]
    public async Task ConcurrentSameDecisionId_WithoutTheLock_IsStillExactlyOnce_ByTheConditionalWrite()
    {
        var w = new World(billedTotal: 100m);
        w.Locks.AlwaysGrant = true;
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var service = w.Service();
        var command = Command(w, LineDecisionAction.Disallow, [new(line.Id)], Reason);
        var bothRead = new Barrier(2);
        var reads = 0;
        w.Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_billingevent"), It.IsAny<CancellationToken>()))
            .Returns(async (QueryExpression _, CancellationToken _) =>
            {
                var snapshot = w.Lines.Select(l => { var c = new Entity(l.LogicalName, l.Id); foreach (var a in l.Attributes) c[a.Key] = a.Value; return c; }).ToList();
                if (Interlocked.Increment(ref reads) <= 2)
                {
                    await Task.Run(() => bothRead.SignalAndWait(TimeSpan.FromSeconds(10)));
                }

                return new EntityCollection(snapshot);
            });
        w.Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_invoice"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new EntityCollection([new Entity("sprk_invoice", w.InvoiceId) { ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.InReview), ["sprk_totalamount"] = new Money(100m), ["sprk_project"] = new EntityReference("sprk_project", Core) }]));

        var results = await Task.WhenAll(
            Task.Run(() => service.DecideAsync(command, CancellationToken.None)),
            Task.Run(() => service.DecideAsync(command, CancellationToken.None)));

        results.Count(r => r.Status == LineDecisionStatus.Applied).Should().Be(1);
        results.Count(r => r.Status == LineDecisionStatus.Changed).Should().Be(1, "the loser's conditional write found the row at another version");
        w.H.Created.Should().ContainSingle();
    }

    [Fact(DisplayName = "100: a conditional write that finds a changed line refuses (409), changes nothing and records nothing")]
    public async Task ChangedLine_IsRefused_AndNothingIsRecorded()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        w.BeforeLineWrite = _ => line["versionnumber"] = 9999L; // another writer got there after our read

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Changed);
        result.Code.Should().Be(LineDecisionCodes.LineChanged);
        w.LineWrites.Should().BeEmpty();
        w.H.Created.Should().BeEmpty();
        w.TotalsWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "100: when the lock is held by another decision the request is refused (409) before anything is read")]
    public async Task BusyInvoice_IsRefused_BeforeAnyRead()
    {
        var w = new World();
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var locks = new Mock<IIdempotencyService>();
        locks.Setup(l => l.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var service = new InvoiceLineDecisionService(
            w.Entities.Object, Mock.Of<IInvoiceTotalsService>(),
            new BillingDecisionRecorder(w.H.Writer, w.Identity.Object, NullLogger<BillingDecisionRecorder>.Instance),
            locks.Object, w.Ownership.Object, NullLogger<InvoiceLineDecisionService>.Instance);

        var result = await service.DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Busy);
        w.QueryCount.Should().Be(0);
    }

    [Fact(DisplayName = "100: the lock is taken per invoice and released after the decision, so the next decision is not blocked")]
    public async Task TheLock_IsReleased_AfterTheDecision()
    {
        var w = new World(billedTotal: 200m);
        var a = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var b = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var service = w.Service();

        (await service.DecideAsync(Command(w, LineDecisionAction.Accept, [new(a.Id)]), CancellationToken.None)).Status.Should().Be(LineDecisionStatus.Applied);
        (await service.DecideAsync(Command(w, LineDecisionAction.Accept, [new(b.Id)]), CancellationToken.None)).Status.Should().Be(LineDecisionStatus.Applied);
        w.Locks.Acquired.Should().Be(2);
    }

    // ── Failures after the lines were written: the recovery protocol ──

    [Fact(DisplayName = "100: a Decision Record failure after the lines were written answers 'incomplete' with the committed lines; repeating the decision completes it with exactly one record per line")]
    public async Task RecordFailure_IsIncomplete_AndTheSameDecisionIdCompletesIt()
    {
        var w = new World(billedTotal: 200m);
        var a = w.AddLine(100m, InvoiceLineStatus.Clean, extraction: Extraction);
        var b = w.AddLine(100m, InvoiceLineStatus.Clean);
        var decisionId = Guid.NewGuid();
        var failing = true;
        w.Identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid user, CancellationToken _) => failing
                ? Task.FromException<PersonIdentity>(new InvalidOperationException("identity down"))
                : Task.FromResult(new PersonIdentity(user, ContactId: Guid.NewGuid())));
        var service = w.Service();

        var first = await service.AcceptCleanAsync(w.InvoiceId, decisionId, Decider, CancellationToken.None);

        first.Status.Should().Be(LineDecisionStatus.Incomplete);
        first.Code.Should().Be(LineDecisionCodes.Incomplete);
        first.FailedStep.Should().Be("record");
        first.CommittedLineIds.Should().BeEquivalentTo([a.Id, b.Id]);
        Status(w, a).Should().Be(InvoiceLineStatus.Accepted, "the lines were written");
        w.ApprovedTotal.Should().Be(200m, "the totals were recomputed before the records");
        w.H.Created.Should().BeEmpty();

        failing = false;
        var again = await service.DecideAsync(
            Command(w, LineDecisionAction.Accept, first.CommittedLineIds!.Select(id => new LineDecisionLine(id)), decisionId: decisionId, bulk: true), CancellationToken.None);

        again.Status.Should().Be(LineDecisionStatus.Applied);
        w.H.Created.Should().HaveCount(2, "one record per line, written once");
        var correctionCount = w.Corrections.Count;
        await service.DecideAsync(
            Command(w, LineDecisionAction.Accept, first.CommittedLineIds!.Select(id => new LineDecisionLine(id)), decisionId: decisionId, bulk: true), CancellationToken.None);
        w.H.Created.Should().HaveCount(2);
        w.Corrections.Count.Should().Be(correctionCount);
    }

    [Fact(DisplayName = "100: a totals failure after the lines were written is 'incomplete' too, naming the step")]
    public async Task TotalsFailure_IsIncomplete()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        w.TotalsFault = new InvalidOperationException("dataverse down");

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Incomplete);
        result.FailedStep.Should().Be("totals");
        result.CommittedLineIds.Should().BeEquivalentTo([line.Id]);
        w.H.Created.Should().BeEmpty("no record before the totals follow the lines");
    }

    [Fact(DisplayName = "100: a correction-row failure after the lines were written is 'incomplete' too, naming the step, before any Decision Record")]
    public async Task CorrectionFailure_IsIncomplete()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Clean, extraction: Extraction);
        w.CorrectionFault = new InvalidOperationException("transaction failed");

        var result = await w.Service().AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), Decider, CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Incomplete);
        result.FailedStep.Should().Be("corrections");
        result.CommittedLineIds.Should().BeEquivalentTo([line.Id]);
        w.H.Created.Should().BeEmpty();
    }

    [Fact(DisplayName = "100: a failed read before any write is 'unavailable' and changes nothing")]
    public async Task ReadFailure_ChangesNothing()
    {
        var w = new World();
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        w.ReadFault = new InvalidOperationException("dataverse down");

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Unavailable);
        w.LineWrites.Should().BeEmpty();
        w.H.Created.Should().BeEmpty();
    }

    [Fact(DisplayName = "100: a decision on an invoice that does not exist is the uniform not-found")]
    public async Task UnknownInvoice_IsNotFound()
    {
        var w = new World();
        w.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EntityCollection());

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(Guid.NewGuid())]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.InvoiceNotFound);
    }

    private static InvoiceLineStatus Status(World w, Entity line) =>
        (InvoiceLineStatus)w.Line(line.Id).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value;
}
