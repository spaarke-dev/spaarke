using System.Collections.Concurrent;
using System.Data;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
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
    private const string CallerToken = "caller-bearer-token";

    /// <summary>One call of the app-identity close.</summary>
    private sealed record CloseCall(Guid SignalId, Guid RecordId, DateTime SetReadAtUtc, string PolicyCode, Guid? DecidedBy);

    /// <summary>Captures log entries (level and rendered message), so a warning is asserted, not assumed.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

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

        // ── D-122 / FR-51: the Signals on the lines, the caller's rights (asked as the caller), the app-identity close ──
        public readonly List<Entity> Signals = [];
        public readonly List<CloseCall> Closes = [];
        public readonly Dictionary<Guid, Guid> ClosedBy = new();
        public readonly List<(string? Token, string Set, Guid Id)> RightsAsked = [];
        public readonly ListLogger<InvoiceLineDecisionService> Log = new();
        public readonly Mock<CallerRecordAccessProbe> Probe = new(
            new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!) { CallBase = false };
        /// <summary>The caller's rights, by default the REAL roles' (F1-2): Core User and Basic User hold only Read on <c>sprk_billingevent</c>
        /// (D-90), Write on the invoice and AppendTo on the core.</summary>
        public Func<string, Guid, AccessRights> Rights = RealReviewerRights;

        public static AccessRights RealReviewerRights(string set, Guid id) =>
            set == "sprk_billingevents" ? AccessRights.Read : AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo;
        public Func<Guid, Exception?> CloseFault = _ => null;

        /// <summary>A forced outcome of the close for a Signal (ChangedSinceRead, LeftAsIs): the writer's non-throwing results.</summary>
        public Func<Guid, SignalAutomatedCloseOutcome?> CloseOutcome = _ => null;

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
                            // The Signals whose subject lookup is any value the query names (the OR of invoice Equal / lines In).
                            "sprk_signal" => new EntityCollection(Signals
                                .Where(s => query.Criteria.Conditions.Any(c => s.GetAttributeValue<EntityReference>(c.AttributeName) is { } subject
                                    && c.Values.Any(v => Equals(v, subject.Id))))
                                .Select(Copy).ToList()),
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

            Entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string table, CancellationToken _) => table + "s");
            Probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? token, string set, Guid id, CancellationToken _) =>
                {
                    lock (RightsAsked)
                    {
                        RightsAsked.Add((token, set, id));
                    }

                    return token == CallerToken ? Rights(set, id) : AccessRights.None;
                });
        }

        public InvoiceLineDecisionService Service()
        {
            var recorder = new BillingDecisionRecorder(H.Writer, Identity.Object, NullLogger<BillingDecisionRecorder>.Instance);
            var totals = new InvoiceTotalsService(Records.Object, Entities.Object, NullLogger<InvoiceTotalsService>.Instance);
            return new InvoiceLineDecisionService(Entities.Object, totals, recorder, Locks, Ownership.Object, Probe.Object, CloseAsync, Log);
        }

        /// <summary>The app-identity close as the Signal writer behaves: Open/Acknowledged → Resolved Acted by the record; a repeat by the
        /// same record → AlreadyClosedByThisRecord; resolved otherwise → LeftAsIs. Every call is recorded.</summary>
        public Task<SignalAutomatedCloseOutcome> CloseAsync(
            Guid signalId, Guid recordId, DateTime setReadAtUtc, string policyCode, Guid? decidedBy, CancellationToken ct)
        {
            lock (Closes)
            {
                Closes.Add(new CloseCall(signalId, recordId, setReadAtUtc, policyCode, decidedBy));
                if (CloseFault(signalId) is { } fault)
                {
                    return Task.FromException<SignalAutomatedCloseOutcome>(fault);
                }

                if (CloseOutcome(signalId) is { } forced)
                {
                    return Task.FromResult(forced);
                }

                var row = Signals.Single(s => s.Id == signalId);
                if (ClosedBy.TryGetValue(signalId, out var by))
                {
                    return Task.FromResult(by == recordId ? SignalAutomatedCloseOutcome.AlreadyClosedByThisRecord : SignalAutomatedCloseOutcome.LeftAsIs);
                }

                ClosedBy[signalId] = recordId;
                row["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
                return Task.FromResult(SignalAutomatedCloseOutcome.Closed);
            }
        }

        /// <summary>An Open (or <paramref name="status"/>) Signal on a line, filed as the Signal writer files one: the dedupe key, the pair id
        /// and the subject lookup. The writer's harness holds it too, so the record's resolved-Signal checks find it.</summary>
        public Guid AddSignal(Guid lineId, string policyCode = "EB-RATE", int status = SignalWriter.SignalStatusOpen)
        {
            var id = Guid.NewGuid();
            var lookup = SignalWriter.RegardingLookupByEntity["sprk_billingevent"];
            Signals.Add(new Entity("sprk_signal", id)
            {
                ["sprk_dedupekey"] = SignalWriter.BuildDedupeKey(policyCode, "sprk_billingevent", lineId.ToString("D"), 1),
                ["sprk_signalstatus"] = new OptionSetValue(status),
                ["sprk_regardingrecordid"] = lineId.ToString("D"),
                [lookup] = new EntityReference("sprk_billingevent", lineId),
            });
            H.Rows[("sprk_signal", id)] = new Entity("sprk_signal", id)
            {
                ["sprk_regardingrecordid"] = lineId.ToString("D"),
                [lookup] = new EntityReference("sprk_billingevent", lineId),
            };
            return id;
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
        new(w.InvoiceId, decisionId ?? Guid.NewGuid(), action, reason, lines.ToList(), Decider, bulk, CallerToken);

    /// <summary>
    /// For the persister's end-to-end test (EB-D-175 / EB-D-176): puts a line row AS THE REAL PERSISTER WROTE IT into this service's in-memory
    /// Dataverse (only its parent invoice is re-pointed at the world), accepts it through the real <see cref="InvoiceLineDecisionService"/>,
    /// and returns the outcome, the row as stored after the decision and the Decision Records it wrote.
    /// </summary>
    internal static async Task<(LineDecisionStatus Status, Entity Line, IReadOnlyList<Entity> Records)> AcceptPersistedLineAsync(Entity persisted, decimal billedTotal)
    {
        var w = new World(billedTotal);
        var row = new Entity(persisted.LogicalName, persisted.Id);
        foreach (var attribute in persisted.Attributes)
        {
            row[attribute.Key] = attribute.Value;
        }

        row["sprk_invoice"] = new EntityReference("sprk_invoice", w.InvoiceId);
        row["versionnumber"] = 50L;
        w.Lines.Add(row);
        w.H.Rows[("sprk_billingevent", row.Id)] = new Entity("sprk_billingevent", row.Id)
        {
            ["sprk_project"] = new EntityReference("sprk_project", Core),
            ["sprk_invoice"] = new EntityReference("sprk_invoice", w.InvoiceId),
        };

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(row.Id)]), CancellationToken.None);
        return (result.Status, w.Line(row.Id), w.H.Created.ToList());
    }

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

    [Fact(DisplayName = "194b R3b-2: each line's correction rows are asked their own question (ForChild): the invoice primary, then the line and the core record")]
    public async Task CorrectionRows_AskTheRowsOwnQuestion_InvoicePrimary_OnePerLine()
    {
        var w = new World(billedTotal: 300m);
        var first = w.AddLine(100m, InvoiceLineStatus.Clean, extraction: Extraction);
        var second = w.AddLine(200m, InvoiceLineStatus.Clean, extraction: Extraction);

        var result = await w.Service().AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), Decider, CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        var asks = w.Ownership.Invocations
            .Where(i => i.Method.Name == nameof(IRecordOwnershipResolver.ResolveOwnerAsync))
            .Select(i => (RecordOwnershipContext)i.Arguments[0])
            .ToList();
        asks.Should().HaveCount(2, "one question per line, never one per row");
        foreach (var (ask, line) in asks.Zip(new[] { first, second }))
        {
            ask.ChildEntityLogicalName.Should().Be(InvoiceLineDecisionService.CorrectionEntity);
            ask.Parents.Should().Equal(
                new RecordOwnershipParent("sprk_invoice", w.InvoiceId),
                new RecordOwnershipParent("sprk_billingevent", line.Id),
                new RecordOwnershipParent("sprk_project", Core));
        }
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

    // ── #2455: exactly one core record ──

    private static void NoWrites(World w)
    {
        w.LineWrites.Should().BeEmpty("a refused decision writes nothing");
        w.H.Created.Should().BeEmpty("no Decision Record");
        w.Corrections.Should().BeEmpty("no correction row");
        w.TotalsWrites.Should().BeEmpty("no totals");
    }

    [Fact(DisplayName = "#2455 SEEDED: an invoice naming TWO core records refuses a line decision (OwnerUnresolved) and writes nothing")]
    public async Task TwoCores_RefuseTheDecision_AndWriteNothing()
    {
        var w = new World();
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        w.Invoice["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid());

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.OwnerUnresolved);
        result.Code.Should().Be(LineDecisionCodes.OwnerUnresolved);
        NoWrites(w);
    }

    [Fact(DisplayName = "#2455: an invoice naming NO core record is the stable OwnerUnresolved refusal, not a throw, on a single decision")]
    public async Task NoCore_IsTheStableRefusal_OnADecision()
    {
        var w = new World();
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        w.Invoice.Attributes.Remove("sprk_project");

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.OwnerUnresolved);
        result.Code.Should().Be(LineDecisionCodes.OwnerUnresolved);
        NoWrites(w);
    }

    [Fact(DisplayName = "#2455: Accept all Clean on an invoice with no core record (lines with AI fields) is the stable refusal, not a throw from the correction rows")]
    public async Task NoCore_AcceptAllClean_IsRefused_NotAThrow()
    {
        var w = new World();
        w.AddLine(100m, InvoiceLineStatus.Clean, extraction: Extraction);
        w.Invoice.Attributes.Remove("sprk_project");

        var result = await w.Service().AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), Decider, CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.OwnerUnresolved);
        NoWrites(w);
    }

    [Fact(DisplayName = "#2455: Accept all Clean on an invoice naming two cores is refused with nothing written")]
    public async Task TwoCores_AcceptAllClean_IsRefused()
    {
        var w = new World();
        w.AddLine(100m, InvoiceLineStatus.Clean, extraction: Extraction);
        w.Invoice["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", Guid.NewGuid());

        var result = await w.Service().AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), Decider, CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.OwnerUnresolved);
        NoWrites(w);
    }

    [Fact(DisplayName = "#2455: the Unreviewed rows (103 D-72) on an invoice naming two cores are refused and no row is built")]
    public async Task TwoCores_WriteUnreviewed_IsRefused()
    {
        var w = new World();
        w.AddLine(100m, InvoiceLineStatus.Accepted, extraction: Extraction);
        w.Invoice["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid());

        var result = await w.Service().WriteUnreviewedAsync(w.InvoiceId, Guid.NewGuid(), Decider, "Approver", DateTime.UtcNow, CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.OwnerUnresolved);
        w.Corrections.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2455: an invoice naming exactly one core still decides (the new check does not refuse the normal case)")]
    public async Task OneCore_StillDecides()
    {
        var w = new World();
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
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
            locks.Object, w.Ownership.Object, w.Probe.Object, w.CloseAsync, NullLogger<InvoiceLineDecisionService>.Instance);

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

    // ── D-122 / FR-51: the human-decision Signal close (the shape agreed with ontology-r1) ──

    /// <summary>The set the line's record stored, read back through the writer (the same read a retry makes).</summary>
    private static Task<DecisionRecordWriter.BillingStoredResolvedSet?> StoredSet(World w, string actionCode, Guid lineId, Guid decisionId) =>
        new BillingDecisionRecorder(w.H.Writer, w.Identity.Object, NullLogger<BillingDecisionRecorder>.Instance)
            .FindStoredResolvedSetAsync(actionCode, lineId, decisionId);

    /// <summary>Nothing changed and nothing was closed: no line write, no correction, no total, no record, no close.</summary>
    private static void NothingWritten(World w)
    {
        w.LineWrites.Should().BeEmpty();
        w.Corrections.Should().BeEmpty();
        w.TotalsWrites.Should().BeEmpty();
        w.H.Created.Should().BeEmpty();
        w.Closes.Should().BeEmpty();
    }

    [Theory(DisplayName = "100: each outcome closes the decided line's Open and Acknowledged Signals, of every policy, as Acted by its record naming the decider; another line's Signal stays open")]
    [InlineData(LineDecisionAction.Accept, "accept-line")]
    [InlineData(LineDecisionAction.Reduce, "reduce-line")]
    [InlineData(LineDecisionAction.Disallow, "disallow-line")]
    public async Task EachOutcome_ClosesTheDecidedLinesSignals_AsActed_NamingTheDecider(LineDecisionAction action, string code)
    {
        var w = new World(billedTotal: 200m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var other = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var open = w.AddSignal(line.Id, "EB-RATE");
        var acknowledged = w.AddSignal(line.Id, "EB-TASK", SignalWriter.SignalStatusAcknowledged);
        var resolved = w.AddSignal(line.Id, "EB-OLD", SignalWriter.SignalStatusResolved);
        var otherLines = w.AddSignal(other.Id, "EB-RATE");
        var decisionId = Guid.NewGuid();
        var before = DateTime.UtcNow;

        var result = await w.Service().DecideAsync(
            Command(w, action, [action == LineDecisionAction.Reduce ? new(line.Id, 60m) : new(line.Id)], action == LineDecisionAction.Accept ? null : Reason, decisionId),
            CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        w.Closes.Select(c => c.SignalId).Should().BeEquivalentTo([open, acknowledged]);
        w.Closes.Should().OnlyContain(c => c.DecidedBy == Decider, "the person is the authenticated decider, never request input");
        w.Closes.Single(c => c.SignalId == acknowledged).PolicyCode.Should().Be("EB-TASK", "each Signal's own policy code");
        w.ClosedBy.Should().NotContainKey(otherLines, "only the decided line's Signals");
        w.ClosedBy.Should().NotContainKey(resolved, "a resolved Signal is not in the set");

        var stored = (await StoredSet(w, code, line.Id, decisionId))!;
        stored.Signals.Select(s => s.SignalId).Should().BeEquivalentTo([open, acknowledged], "the record lists the set it resolved");
        w.Closes.Should().OnlyContain(c => c.RecordId == stored.RecordId && c.SetReadAtUtc == stored.SetReadAtUtc,
            "closed by the line's own record, with the read time stored with it");
        stored.SetReadAtUtc.Should().BeOnOrAfter(before.AddSeconds(-1)).And.BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact(DisplayName = "100: a multi-line decision closes each line's Signals with that line's set only")]
    public async Task MultiLineDecision_ClosesEachLinesOwnSet()
    {
        var w = new World(billedTotal: 300m);
        var a = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var b = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var c = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var onA = w.AddSignal(a.Id);
        var onB = w.AddSignal(b.Id);
        var onC = w.AddSignal(c.Id);
        var decisionId = Guid.NewGuid();

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Disallow, [new(a.Id), new(b.Id)], Reason, decisionId, bulk: true), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        w.ClosedBy.Keys.Should().BeEquivalentTo([onA, onB]);
        w.ClosedBy.Should().NotContainKey(onC);
        (await StoredSet(w, "disallow-line", a.Id, decisionId))!.Signals.Select(s => s.SignalId).Should().Equal(onA);
        (await StoredSet(w, "disallow-line", b.Id, decisionId))!.Signals.Select(s => s.SignalId).Should().Equal(onB);
    }

    [Fact(DisplayName = "100: the caller's rights are asked as the caller: AppendTo on the core record and, for a decided line with a Signal, Write on its PARENT INVOICE")]
    public async Task TheRights_AreAskedAsTheCaller_OnTheCoreAndEachLineWithASignal()
    {
        var w = new World(billedTotal: 200m);
        var a = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var b = w.AddLine(100m, InvoiceLineStatus.Flagged);
        w.AddSignal(a.Id);

        await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(a.Id), new(b.Id)], bulk: true), CancellationToken.None);

        w.RightsAsked.Should().BeEquivalentTo([(CallerToken, "sprk_projects", Core), (CallerToken, "sprk_invoices", w.InvoiceId)],
            "a line subject is asked on its parent invoice (ontology-r1 2026-10-20, D-90); b carries no Signal");
    }

    [Fact(DisplayName = "100 F1-2: a reviewer with exactly the real roles' rights (Read only on sprk_billingevent, Write on the invoice) decides a line with an open line Signal")]
    public async Task ARealReviewer_DecidesALine_WithAnOpenLineSignal()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var signal = w.AddSignal(line.Id);
        w.Rights = (set, _) => set switch
        {
            "sprk_billingevents" => AccessRights.Read,
            "sprk_invoices" => AccessRights.Read | AccessRights.Write,
            "sprk_projects" => AccessRights.Read | AccessRights.AppendTo,
            _ => AccessRights.None,
        };

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Reduce, [new(line.Id, 60m)], Reason), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        w.ClosedBy.Should().ContainKey(signal);
        w.RightsAsked.Should().NotContain(r => r.Set == "sprk_billingevents", "Write on a line is never asked");
    }

    [Fact(DisplayName = "100: a decision that closes no Signal asks no record rights (the route's authorization stands alone)")]
    public async Task NoSignal_AsksNoRecordRights()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        w.RightsAsked.Should().BeEmpty();
        w.Closes.Should().BeEmpty();
    }

    [Fact(DisplayName = "100: without AppendTo on the core record the decision is 403 and nothing is written")]
    public async Task MissingAppendToOnTheCore_Is403_AndNothingIsWritten()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        w.AddSignal(line.Id);
        w.Rights = (set, _) => set == "sprk_projects" ? AccessRights.Read | AccessRights.Write : AccessRights.Read | AccessRights.Write | AccessRights.AppendTo;

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Reduce, [new(line.Id, 60m)], Reason), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Forbidden);
        result.Code.Should().Be(LineDecisionCodes.NotAuthorized);
        NothingWritten(w);
        Status(w, line).Should().Be(InvoiceLineStatus.Flagged);
    }

    [Fact(DisplayName = "100 F1-2: without Write on the PARENT INVOICE of a decided line that has a Signal the decision is 403 and nothing is written, not even the other lines")]
    public async Task MissingWriteOnASubject_Is403_AndNothingIsWritten()
    {
        var w = new World(billedTotal: 200m);
        var a = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var b = w.AddLine(100m, InvoiceLineStatus.Flagged);
        w.AddSignal(a.Id);
        w.AddSignal(b.Id);
        w.Rights = (set, _) => set == "sprk_invoices"
            ? AccessRights.Read | AccessRights.AppendTo
            : World.RealReviewerRights(set, Guid.Empty);

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Disallow, [new(a.Id), new(b.Id)], Reason, bulk: true), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Forbidden);
        NothingWritten(w);
    }

    [Fact(DisplayName = "100: a caller with no token holds no right, so a decision that closes Signals is 403 and nothing is written")]
    public async Task NoCallerToken_Is403_WhenThereAreSignals()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        w.AddSignal(line.Id);

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]) with { CallerBearerToken = null }, CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Forbidden);
        NothingWritten(w);
    }

    [Fact(DisplayName = "100 F2: a failed close answers 'incomplete' at step 'signals', not Applied: the decision stands (line, total, record), the line's other Signals are closed, and the failure is warned")]
    public async Task CloseFailure_IsIncompleteAtSignals_AndTheDecisionStands()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var failing = w.AddSignal(line.Id, "EB-RATE");
        var fine = w.AddSignal(line.Id, "EB-TASK");
        w.CloseFault = id => id == failing ? new TimeoutException("dataverse") : null;

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Reduce, [new(line.Id, 60m)], Reason), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Incomplete, "a Signal left Open with no record link is not a finished decision");
        result.Code.Should().Be(LineDecisionCodes.Incomplete);
        result.FailedStep.Should().Be("signals");
        result.CommittedLineIds.Should().BeEquivalentTo([line.Id], "the repeat names these lines");
        Status(w, line).Should().Be(InvoiceLineStatus.Reduced, "the decision stands");
        w.ApprovedTotal.Should().Be(60m);
        w.H.Created.Should().ContainSingle("the Decision Record was written before the close");
        w.ClosedBy.Keys.Should().Equal(fine);
        w.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains(failing.ToString("D"), StringComparison.Ordinal));
    }

    [Fact(DisplayName = "100 F2: a Signal that kept changing (ChangedSinceRead) is a failed close too: 'incomplete' at 'signals'")]
    public async Task ChangedSinceRead_IsIncompleteAtSignals()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var changing = w.AddSignal(line.Id, "EB-RATE");
        w.CloseOutcome = id => id == changing ? SignalAutomatedCloseOutcome.ChangedSinceRead : null;

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Incomplete);
        result.FailedStep.Should().Be("signals");
        w.H.Created.Should().ContainSingle();
        w.ClosedBy.Should().BeEmpty();
    }

    [Fact(DisplayName = "100 F2: a clean close is still Applied: every Signal closed, the same response, no warning or error")]
    public async Task CleanClose_IsStillApplied()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var a = w.AddSignal(line.Id, "EB-RATE");
        var b = w.AddSignal(line.Id, "EB-TASK");

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Reduce, [new(line.Id, 60m)], Reason), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        result.Code.Should().Be("ok");
        result.FailedStep.Should().BeNull();
        result.Lines!.Single().Status.Should().Be(nameof(InvoiceLineStatus.Reduced));
        w.ClosedBy.Keys.Should().BeEquivalentTo([a, b]);
        w.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact(DisplayName = "100 F2: a Signal resolved otherwise (LeftAsIs) is not a failed close: the decision is Applied")]
    public async Task LeftAsIs_IsNotAFailedClose_AndTheDecisionIsApplied()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var resolvedElsewhere = w.AddSignal(line.Id, "EB-RATE");
        w.CloseOutcome = id => id == resolvedElsewhere ? SignalAutomatedCloseOutcome.LeftAsIs : null;

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        w.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact(DisplayName = "100 F2: a Decision Record failure is still reported as step 'record' (it comes before a signals failure)")]
    public async Task RecordFailure_StillWins_OverASignalsFailure()
    {
        var w = new World(billedTotal: 200m);
        var a = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var b = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var onA = w.AddSignal(a.Id);
        w.CloseFault = id => id == onA ? new TimeoutException("dataverse") : null;
        // The second line's record fails: its identity lookup throws on the second resolution only.
        var resolutions = 0;
        w.Identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid user, CancellationToken _) => ++resolutions > 1
                ? Task.FromException<PersonIdentity>(new InvalidOperationException("identity down"))
                : Task.FromResult(new PersonIdentity(user, ContactId: Guid.NewGuid())));

        var result = await w.Service().DecideAsync(
            Command(w, LineDecisionAction.Accept, [new(a.Id), new(b.Id)], bulk: true), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Incomplete);
        result.FailedStep.Should().Be("record");
        w.Closes.Should().ContainSingle(c => c.SignalId == onA, "line a's close was attempted and failed, so the signals count was above zero when line b's record failed");
        w.H.Created.Should().ContainSingle("line b's record failed");
    }

    [Fact(DisplayName = "100 F2: a multi-line decision with one failed close still records and closes every other line, then answers 'incomplete' at 'signals' naming all committed lines")]
    public async Task MultiLine_OneFailedClose_RecordsEveryLine_AndIsIncompleteAtSignals()
    {
        var w = new World(billedTotal: 200m);
        var a = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var b = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var onA = w.AddSignal(a.Id);
        var onB = w.AddSignal(b.Id);
        w.CloseFault = id => id == onA ? new TimeoutException("dataverse") : null;

        var result = await w.Service().DecideAsync(
            Command(w, LineDecisionAction.Disallow, [new(a.Id), new(b.Id)], Reason, bulk: true), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Incomplete);
        result.FailedStep.Should().Be("signals");
        result.CommittedLineIds.Should().BeEquivalentTo([a.Id, b.Id]);
        w.H.Created.Should().HaveCount(2, "a failed close on one line never stops the other line's record");
        w.ClosedBy.Keys.Should().Equal([onB], "the other line's Signal is still closed");
    }

    [Fact(DisplayName = "100: a Signal that no longer exists has nothing to close: no warning, and the decision stands")]
    public async Task ADeletedSignal_HasNothingToClose()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var gone = w.AddSignal(line.Id);
        w.CloseFault = _ => new System.ServiceModel.FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = -2147220969 }, new System.ServiceModel.FaultReason("does not exist"));

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        w.Closes.Should().ContainSingle().Which.SignalId.Should().Be(gone);
        w.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact(DisplayName = "100: a resend after success answers AlreadyRecorded (not review_invalid), closes the STORED set again (nothing twice) and leaves a later Signal open")]
    public async Task Resend_AfterSuccess_ReusesTheStoredSet_ClosesNothingTwice_AndLeavesALaterSignalOpen()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var first = w.AddSignal(line.Id);
        var command = Command(w, LineDecisionAction.Reduce, [new(line.Id, 60m)], Reason);
        var service = w.Service();
        (await service.DecideAsync(command, CancellationToken.None)).Status.Should().Be(LineDecisionStatus.Applied);
        var firstClose = w.Closes.Single();
        var raisedLater = w.AddSignal(line.Id, "EB-LATE");

        var resend = await service.DecideAsync(command, CancellationToken.None);

        resend.Status.Should().Be(LineDecisionStatus.Applied, "the stored set rebuilds the same decision, so the record answers it");
        resend.Lines!.Single().AlreadyRecorded.Should().BeTrue();
        w.H.Created.Should().ContainSingle();
        w.Closes.Should().HaveCount(2, "the close re-runs over the stored set");
        w.Closes[1].Should().Be(firstClose, "the same Signal, record, read time, code and decider");
        w.ClosedBy.Should().ContainSingle().Which.Key.Should().Be(first, "AlreadyClosedByThisRecord: nothing is closed twice");
        w.ClosedBy.Should().NotContainKey(raisedLater, "a Signal raised after the first attempt stays open");
    }

    [Fact(DisplayName = "100 F2: the repeat of a decision that was incomplete at 'signals' completes the close over the stored set, with the stored read time, and writes NO second Decision Record")]
    public async Task Repeat_AfterAFailedClose_CompletesIt_WithNoSecondDecisionRecord()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var signal = w.AddSignal(line.Id);
        w.CloseFault = _ => new TimeoutException("dataverse");
        var command = Command(w, LineDecisionAction.Accept, [new(line.Id)]);
        var service = w.Service();
        var first = await service.DecideAsync(command, CancellationToken.None);
        first.Status.Should().Be(LineDecisionStatus.Incomplete);
        first.FailedStep.Should().Be("signals");
        w.ClosedBy.Should().BeEmpty();
        w.H.Created.Should().ContainSingle("the one Decision Record, written before the close");
        w.CloseFault = _ => null;

        var repeat = await service.DecideAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(LineDecisionStatus.Applied);
        repeat.Lines!.Single().AlreadyRecorded.Should().BeTrue("the repeat found the record, it did not write another");
        w.H.Created.Should().ContainSingle("no second Decision Record");
        w.ClosedBy.Should().ContainKey(signal);
        w.Closes[1].SetReadAtUtc.Should().Be(w.Closes[0].SetReadAtUtc, "the stored read time, not the retry's");
        w.Closes[1].RecordId.Should().Be(w.Closes[0].RecordId);
        w.ClosedBy[signal].Should().Be(w.Closes[0].RecordId, "closed by the one record");

        var third = await service.DecideAsync(command, CancellationToken.None);
        third.Status.Should().Be(LineDecisionStatus.Applied);
        w.H.Created.Should().ContainSingle();
        w.ClosedBy.Should().ContainSingle("AlreadyClosedByThisRecord: nothing is closed twice");
    }

    [Fact(DisplayName = "100 F2: the repeat of a multi-line decision incomplete at 'signals' (the committed ids, the same decision id) closes the missing Signal, one record per line, none duplicated")]
    public async Task Repeat_OfAMultiLineDecision_CompletesTheMissingClose_WithOneRecordPerLine()
    {
        var w = new World(billedTotal: 200m);
        var a = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var b = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var onA = w.AddSignal(a.Id);
        var onB = w.AddSignal(b.Id);
        w.CloseFault = id => id == onA ? new TimeoutException("dataverse") : null;
        var decisionId = Guid.NewGuid();
        var service = w.Service();
        var first = await service.DecideAsync(Command(w, LineDecisionAction.Disallow, [new(a.Id), new(b.Id)], Reason, decisionId, bulk: true), CancellationToken.None);
        first.Status.Should().Be(LineDecisionStatus.Incomplete);
        w.H.Created.Should().HaveCount(2);
        w.CloseFault = _ => null;

        var repeat = await service.DecideAsync(
            Command(w, LineDecisionAction.Disallow, first.CommittedLineIds!.Select(id => new LineDecisionLine(id)), Reason, decisionId, bulk: true), CancellationToken.None);

        repeat.Status.Should().Be(LineDecisionStatus.Applied);
        repeat.Lines!.Should().OnlyContain(l => l.AlreadyRecorded);
        w.H.Created.Should().HaveCount(2, "one record per line, written once");
        w.ClosedBy.Keys.Should().BeEquivalentTo([onA, onB]);
        w.Closes.Count(c => c.SignalId == onB).Should().Be(2, "onB: closed by the first attempt, then AlreadyClosedByThisRecord on the repeat; never closed twice");
        w.ClosedBy.Should().HaveCount(2);
    }

    [Fact(DisplayName = "100: when the open Signals cannot be read the decision is 'unavailable' and nothing is written")]
    public async Task SignalReadFailure_ChangesNothing()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        w.AddSignal(line.Id);
        var service = w.Service();
        w.Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_signal"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("dataverse"));

        var result = await service.DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)]), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Unavailable);
        NothingWritten(w);
    }

    [Fact(DisplayName = "100 K2: the stored sets of every decided line are found in ONE query (subject In), not one lookup per line")]
    public async Task StoredSets_OfEveryLine_AreOneQuery()
    {
        var w = new World(billedTotal: 300m);
        var lines = new[] { w.AddLine(100m, InvoiceLineStatus.Flagged), w.AddLine(100m, InvoiceLineStatus.Flagged), w.AddLine(100m, InvoiceLineStatus.Flagged) };
        var lineLookup = "sprk_regardinginvoiceline";

        var result = await w.Service().DecideAsync(
            Command(w, LineDecisionAction.Accept, lines.Select(l => new LineDecisionLine(l.Id)), bulk: true), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        var setLookups = w.H.Lookups.Where(q => q.EntityName == "sprk_decisionrecord"
            && q.Criteria.Conditions.Any(c => c.AttributeName == lineLookup && c.Operator == ConditionOperator.In)).ToList();
        setLookups.Should().ContainSingle("one query over all the lines");
        setLookups[0].Criteria.Conditions.Single(c => c.AttributeName == lineLookup).Values.Should().BeEquivalentTo(lines.Select(l => (object)l.Id));
    }

    [Fact(DisplayName = "100 K2: two records under one line's key fail closed (decision_invalid) and nothing is written")]
    public async Task StoredSets_TwoRecordsUnderOneKey_FailClosed()
    {
        var w = new World(billedTotal: 100m);
        var line = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var decisionId = Guid.NewGuid();
        for (var i = 0; i < 2; i++)
        {
            w.H.Seeded.Add(new Entity("sprk_decisionrecord", Guid.NewGuid())
            {
                ["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", line.Id),
                ["sprk_steps"] = $"{{\"reviewId\":\"{BillingDecisionKey.IdOf("sprk_billingevent", line.Id, decisionId):D}\",\"resolvedSignalIds\":[]}}",
                ["sprk_actioncode"] = "accept-line",
                ["createdby"] = new EntityReference("systemuser", DecisionRecordWriterTests.WriterUser),
            });
        }

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(line.Id)], decisionId: decisionId), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.InvalidRequest);
        result.Code.Should().Be(LineDecisionCodes.DecisionInvalid);
        NothingWritten(w);
    }

    [Fact(DisplayName = "100 K3: a Signal whose dedupe-key subject differs from its regarding row is skipped with a warning, never closed")]
    public async Task ASignalWhoseKeyAndRowDisagree_IsSkipped_AndWarned()
    {
        var w = new World(billedTotal: 200m);
        var a = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var b = w.AddLine(100m, InvoiceLineStatus.Flagged);
        var mismatched = w.AddSignal(b.Id);
        // The key names line a; the row is filed on line b.
        w.Signals.Single(s => s.Id == mismatched)["sprk_dedupekey"] = SignalWriter.BuildDedupeKey("EB-RATE", "sprk_billingevent", a.Id.ToString("D"), 1);

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Accept, [new(a.Id), new(b.Id)], bulk: true), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        w.Closes.Should().BeEmpty("a row whose rights were not asked is never closed");
        w.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains(mismatched.ToString("D"), StringComparison.Ordinal));
    }
}
